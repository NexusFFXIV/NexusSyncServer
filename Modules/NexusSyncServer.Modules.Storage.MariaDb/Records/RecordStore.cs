using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexusKit.Sync.Contracts;
using NexusKit.Sync.Protocol;

namespace NexusSyncServer.Modules.Storage.MariaDb.Records;

/// <summary>MariaDB implementation of <see cref="IRecordStore"/>.</summary>
public sealed class RecordStore : IRecordStore
{
    private readonly ServerDbContext mDb;
    private readonly StorageOptions mOptions;
    private readonly ILogger<RecordStore> mLog;

    /// <summary>Creates the store.</summary>
    public RecordStore(ServerDbContext db, IOptions<StorageOptions> options, ILogger<RecordStore> log)
    {
        mDb = db;
        mOptions = options.Value;
        mLog = log;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecordOutcome>> WriteAsync(
        string contractId,
        CollectionDefinition collection,
        IReadOnlyList<RecordWrite> writes,
        Guid? ownerId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(writes);

        var outcomes = new List<RecordOutcome>(writes.Count);
        if (writes.Count == 0) return outcomes;

        // Two bulk reads instead of per-record round trips: which operation ids are already
        // applied, and which keys already exist.
        var opIds = writes.Select(w => w.OpId).Distinct(StringComparer.Ordinal).ToArray();
        var alreadyApplied = await mDb.Set<AppliedOpEntity>()
            .Where(o => opIds.Contains(o.OpId))
            .Select(o => o.OpId)
            .ToHashSetAsync(StringComparer.Ordinal, ct).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var seenInBatch = new HashSet<string>(StringComparer.Ordinal);

        // One transaction for the whole batch. A partially applied batch whose response says
        // "accepted" is the failure mode worth spending a transaction to avoid.
        await using var tx = await mDb.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        foreach (var write in writes)
        {
            if (alreadyApplied.Contains(write.OpId) || !seenInBatch.Add(write.OpId))
            {
                outcomes.Add(RecordOutcome.Duplicate(write.OpId));
                continue;
            }

            var problems = Check(collection, write);
            if (problems.Count > 0)
            {
                outcomes.Add(RecordOutcome.Rejected(write.OpId, problems));
                continue;
            }

            await UpsertAsync(contractId, collection.Name, write, ownerId, now, ct).ConfigureAwait(false);

            mDb.Add(new AppliedOpEntity
            {
                OpId = write.OpId,
                ContractId = contractId,
                Collection = collection.Name,
                AppliedAt = now,
            });

            outcomes.Add(RecordOutcome.Accepted(write.OpId));
        }

        await mDb.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return outcomes;
    }

    /// <inheritdoc />
    public async Task<PullResult> ReadAsync(
        string contractId,
        string collection,
        long since,
        int limit,
        CancellationToken ct)
    {
        var take = Math.Clamp(limit, 1, mOptions.MaxRecordsPerPull);

        // One extra row tells us whether another page exists without a second count query.
        var rows = await mDb.Set<RecordEntity>()
            .AsNoTracking()
            .Where(r => r.ContractId == contractId && r.Collection == collection && r.Seq > since)
            .OrderBy(r => r.Seq)
            .Take(take + 1)
            .ToListAsync(ct).ConfigureAwait(false);

        var hasMore = rows.Count > take;
        if (hasMore) rows.RemoveAt(rows.Count - 1);

        var changes = rows.Select(r => new RecordChange(
            r.Key,
            r.Payload is null ? null : JsonDocument.Parse(r.Payload).RootElement.Clone(),
            r.Deleted,
            r.Seq,
            r.Revision,
            r.UpdatedAt)).ToArray();

        // Carry the cursor forward even on an empty page, so a client never re-requests a gap
        // left by pruned or filtered rows.
        var next = rows.Count > 0 ? rows[^1].Seq : since;

        return new PullResult(changes, next, hasMore);
    }

    /// <inheritdoc />
    public async Task EnsureIndexesAsync(string contractId, CollectionDefinition collection, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(collection);

        foreach (var field in collection.Indexed)
        {
            // Identifiers are interpolated rather than parameterised because MariaDB does
            // not accept parameters in DDL. That is safe here only because every component has
            // already been validated by ContractNames — lowercase letters, digits, underscores
            // and dots. The guard below refuses anything else rather than trusting that.
            if (!IsSafeIdentifierPart(contractId) || !IsSafeIdentifierPart(collection.Name) || !IsSafeIdentifierPart(field))
            {
                throw new InvalidOperationException(
                    $"Refusing to build an index for '{contractId}/{collection.Name}/{field}': "
                    + "one of these is not a validated contract identifier.");
            }

            var column = GeneratedColumnName(field);
            var name = IndexName(contractId, collection.Name, field);

            // MariaDB cannot index an expression directly, so the JSON path becomes a virtual
            // column first and the index sits on that. The column is keyed by field name
            // alone: two contracts declaring the same field share one column, because the
            // extraction is identical and a row without that path simply yields NULL.
            //
            // LEFT(...) rather than the raw extraction: a value longer than the column would
            // abort the whole INSERT under strict mode. Truncating costs selectivity on very
            // long values and costs nothing on realistic ones — losing the write would be
            // the worse trade by far.
            var addColumn =
                $"""
                 ALTER TABLE {StorageEntityModule.RecordsTable}
                 ADD COLUMN IF NOT EXISTS `{column}` VARCHAR({GeneratedColumnLength})
                     AS (LEFT(JSON_UNQUOTE(JSON_EXTRACT(payload, '$.{field}')), {GeneratedColumnLength})) VIRTUAL
                 """;

            // Prefixed with contract and collection rather than filtered by them. MariaDB has
            // no partial index, so the discriminators move into the key instead — same
            // selectivity, one index shared by every contract that declares the field.
            var createIndex =
                $"""
                 CREATE INDEX IF NOT EXISTS `{name}`
                 ON {StorageEntityModule.RecordsTable} (contract_id, collection, `{column}`)
                 """;

            await mDb.Database.ExecuteSqlRawAsync(addColumn, ct).ConfigureAwait(false);
            await mDb.Database.ExecuteSqlRawAsync(createIndex, ct).ConfigureAwait(false);

            mLog.LogInformation("Ensured index {Index} on {Contract}/{Collection}", name, contractId, collection.Name);
        }
    }

    /// <inheritdoc />
    public async Task<int> PruneAsync(string contractId, CollectionDefinition collection, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(collection);

        var removed = 0;

        if (collection.Retention is { } retention)
        {
            var cutoff = DateTimeOffset.UtcNow - retention;
            removed += await mDb.Set<RecordEntity>()
                .Where(r => r.ContractId == contractId && r.Collection == collection.Name && r.UpdatedAt < cutoff)
                .ExecuteDeleteAsync(ct).ConfigureAwait(false);
        }

        var opCutoff = DateTimeOffset.UtcNow - mOptions.OperationDedupeWindow;
        removed += await mDb.Set<AppliedOpEntity>()
            .Where(o => o.ContractId == contractId && o.Collection == collection.Name && o.AppliedAt < opCutoff)
            .ExecuteDeleteAsync(ct).ConfigureAwait(false);

        return removed;
    }

    private List<ValidationProblem> Check(CollectionDefinition collection, RecordWrite write)
    {
        var problems = new List<ValidationProblem>();

        if (collection.Direction != SyncDirection.Uplink)
        {
            problems.Add(new ValidationProblem(null,
                $"Collection '{collection.Name}' is a downlink; clients may not write to it."));
            return problems;
        }

        if (string.IsNullOrEmpty(write.Key))
        {
            problems.Add(new ValidationProblem(null, "A record must carry a key."));
            return problems;
        }

        if (write.Deleted) return problems;   // a tombstone carries no payload to validate

        if (write.Payload is not { } payload)
        {
            problems.Add(new ValidationProblem(null, "A non-deleted record must carry a payload."));
            return problems;
        }

        var raw = payload.GetRawText();
        if (Encoding.UTF8.GetByteCount(raw) > mOptions.MaxPayloadBytes)
        {
            problems.Add(new ValidationProblem(null,
                $"Payload exceeds the server limit of {mOptions.MaxPayloadBytes} bytes."));
            return problems;
        }

        var result = PayloadValidator.Validate(collection, payload);
        if (!result.IsValid) problems.AddRange(result.Problems);

        return problems;
    }

    /// <summary>
    /// Inserts or updates one record, letting MariaDB assign the sequence.
    /// <para>Raw <c>ON DUPLICATE KEY UPDATE</c> rather than EF change tracking, for one specific
    /// reason: <b>an updated record has to move to the end of the cursor</b>, or a client that
    /// already read past its old sequence would never see the change. That means drawing a
    /// fresh sequence value on update, which is a database-side expression EF cannot express
    /// through a tracked property — it would write back whatever value the entity held.</para>
    /// <para>Doing the whole thing in one statement also makes the upsert atomic without a
    /// read-then-write race between concurrent pushes of the same key.</para>
    /// </summary>
    private async Task UpsertAsync(
        string contractId,
        string collection,
        RecordWrite write,
        Guid? ownerId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var payload = write.Deleted ? null : write.Payload!.Value.GetRawText();

        // $$ so that {0}…{6} stay literal placeholders for EF to parameterise, while
        // {{…}} interpolates the table and sequence names at compile time.
        const string sql =
            $$"""
              INSERT INTO {{StorageEntityModule.RecordsTable}}
                  (contract_id, collection, record_key, seq, revision, payload, deleted, owner_id, updated_at)
              VALUES
                  ({0}, {1}, {2}, NEXT VALUE FOR {{StorageEntityModule.RecordSequence}}, 1, {3}, {4}, {5}, {6})
              ON DUPLICATE KEY UPDATE
                  seq        = NEXT VALUE FOR {{StorageEntityModule.RecordSequence}},
                  revision   = revision + 1,
                  payload    = VALUES(payload),
                  deleted    = VALUES(deleted),
                  owner_id   = VALUES(owner_id),
                  updated_at = VALUES(updated_at)
              """;

        // Interpolated form: EF parameterises every hole, so the payload and key are never
        // concatenated into the statement.
        await mDb.Database.ExecuteSqlAsync(
            FormattableStringFactory.Create(sql, contractId, collection, write.Key, payload, write.Deleted, ownerId, now),
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Width of the virtual columns that back declared indexes.
    /// <para>191 rather than something rounder: the composite key is
    /// <c>contract_id(128) + collection(64) + this</c>, and at four bytes per utf8mb4
    /// character that totals 1532 bytes — comfortably inside InnoDB's 3072-byte index
    /// limit, with room for the key to grow.</para>
    /// </summary>
    private const int GeneratedColumnLength = 191;

    private static string GeneratedColumnName(string field)
    {
        // Keyed by field name only, so contracts sharing a field share the column. Hashed
        // past the point where MariaDB's 64-character identifier limit would bite.
        const string prefix = "g_";
        if (prefix.Length + field.Length <= 64) return prefix + field;

        var hash = Math.Abs(string.GetHashCode(field, StringComparison.Ordinal)).ToString(CultureInfo.InvariantCulture);
        return $"{prefix}{field[..(64 - prefix.Length - hash.Length - 1)]}_{hash}";
    }

    private static string IndexName(string contractId, string collection, string field)
    {
        // Deterministic and inside MariaDB's 64-character identifier limit, which contract
        // ids can otherwise blow past on their own.
        var raw = $"{contractId}_{collection}_{field}".Replace('.', '_');
        var hash = Math.Abs(string.GetHashCode(raw, StringComparison.Ordinal)).ToString(CultureInfo.InvariantCulture);
        var prefix = raw.Length > 40 ? raw[..40] : raw;
        return $"ix_rec_{prefix}_{hash}";
    }

    private static bool IsSafeIdentifierPart(string value)
    {
        foreach (var c in value)
        {
            if (c is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '_' and not '.' and not '-')
                return false;
        }

        return value.Length > 0;
    }
}
