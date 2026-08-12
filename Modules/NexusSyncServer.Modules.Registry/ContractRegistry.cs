using System.Collections.Immutable;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexusSyncServer.Modules.Storage.MariaDb;
using NexusSyncServer.Modules.Storage.MariaDb.Records;
using NexusKit.Sync.Contracts;
using NexusKit.Sync.Protocol;

namespace NexusSyncServer.Modules.Registry;

/// <summary>
/// Database-backed registry with an in-memory snapshot for reads.
/// </summary>
public sealed class ContractRegistry : IContractRegistry
{
    /// <summary>One version in the snapshot, and whether it is still offered.</summary>
    private sealed record Entry(SyncContract Contract, bool Retired);

    private readonly IServiceScopeFactory mScopes;
    private readonly RegistryOptions mOptions;
    private readonly ILogger<ContractRegistry> mLog;

    // Replaced wholesale on refresh rather than mutated. Readers therefore never see a
    // half-updated map and need no lock — the cost is copying a dictionary that holds a
    // handful of entries and changes about once a release.
    private volatile ImmutableDictionary<string, ImmutableSortedDictionary<ContractVersion, Entry>> mSnapshot =
        ImmutableDictionary<string, ImmutableSortedDictionary<ContractVersion, Entry>>.Empty;

    /// <summary>Creates the registry.</summary>
    public ContractRegistry(
        IServiceScopeFactory scopes,
        IOptions<RegistryOptions> options,
        ILogger<ContractRegistry> log)
    {
        ArgumentNullException.ThrowIfNull(options);

        mScopes = scopes;
        mOptions = options.Value;
        mLog = log;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ContractIds =>
        mSnapshot.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();

    /// <inheritdoc />
    public IReadOnlyList<ContractVersion> VersionsOf(string contractId) =>
        mSnapshot.TryGetValue(contractId, out var versions)
            ? versions.Where(v => !v.Value.Retired).Select(v => v.Key).ToArray()
            : [];

    /// <inheritdoc />
    public IReadOnlyList<ContractVersion> AllVersionsOf(string contractId) =>
        mSnapshot.TryGetValue(contractId, out var versions) ? versions.Keys.ToArray() : [];

    /// <inheritdoc />
    public bool IsRetired(string contractId, ContractVersion version) =>
        mSnapshot.TryGetValue(contractId, out var versions)
        && versions.TryGetValue(version, out var entry)
        && entry.Retired;

    /// <inheritdoc />
    public SyncContract? Negotiate(string contractId, ContractVersion wanted)
    {
        if (!mSnapshot.TryGetValue(contractId, out var versions)) return null;

        SyncContract? best = null;

        foreach (var (version, entry) in versions)
        {
            if (version.Major != wanted.Major) continue;

            // Retired versions are not offered. They stay in the snapshot because Find and the
            // compatibility check still need them; they are simply never the answer here.
            if (entry.Retired) continue;

            // Never above what the client asked for. A minor it has not been built against may
            // have widened or narrowed a field's type, and handing it one on the assumption that
            // minors are purely additive is exactly the assumption that no longer holds. A client
            // on 1.4 against a server holding 1.2 still gets 1.2 — that case is a ceiling, and
            // this is a ceiling.
            if (version > wanted) continue;

            if (best is null || version > best.Version) best = entry.Contract;
        }

        return best;
    }

    /// <summary>
    /// The lowest version of this major still served, or null when none is.
    /// <para>What a peer refused by <see cref="Negotiate"/> has to be told: it is the version it
    /// must reach. Only meaningful once negotiation has already failed — before that, the peer is
    /// being served and the floor is none of its concern.</para>
    /// </summary>
    public ContractVersion? MinimumServed(string contractId, int major)
    {
        if (!mSnapshot.TryGetValue(contractId, out var versions)) return null;

        ContractVersion? lowest = null;

        foreach (var (version, entry) in versions)
        {
            if (version.Major != major || entry.Retired) continue;
            if (lowest is null || version < lowest.Value) lowest = version;
        }

        return lowest;
    }

    /// <inheritdoc />
    public SyncContract? Find(string contractId, ContractVersion version) =>
        mSnapshot.TryGetValue(contractId, out var versions) && versions.TryGetValue(version, out var entry)
            ? entry.Contract
            : null;

    /// <inheritdoc />
    public ContractDescriptor? Describe(string contractId, ContractVersion? version = null)
    {
        if (!mSnapshot.TryGetValue(contractId, out var versions) || versions.IsEmpty) return null;

        // Only what is served, and in ascending order. This list is the whole retirement
        // mechanism as far as a client is concerned: it picks from what it is shown.
        var served = versions.Where(v => !v.Value.Retired).ToArray();
        if (served.Length == 0) return null;

        var selected = version is { } v
            ? served.Where(e => e.Key == v).Select(e => e.Value.Contract).FirstOrDefault()
            : served[^1].Value.Contract;

        if (selected is null) return null;

        return new ContractDescriptor(
            contractId,
            served.Select(e => e.Key).ToArray(),
            selected.Version,
            selected.CanonicalJson,
            selected.Hash);
    }

    /// <inheritdoc />
    public async Task<bool> SetRetiredAsync(
        string contractId, ContractVersion version, bool retired, CancellationToken ct)
    {
        using var scope = mScopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();

        var row = await db.Set<RegisteredContractEntity>()
            .FirstOrDefaultAsync(
                c => c.ContractId == contractId && c.Major == version.Major && c.Minor == version.Minor,
                ct).ConfigureAwait(false);

        if (row is null) return false;

        var already = row.RetiredAt is not null;
        if (already == retired) return true;

        row.RetiredAt = retired ? DateTimeOffset.UtcNow : null;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await RefreshAsync(ct).ConfigureAwait(false);

        mLog.LogWarning(
            "Contract {Contract} {Version} was {Action}. Peers that can go no higher will now be refused.",
            contractId, version, retired ? "retired" : "returned to service");

        return true;
    }

    /// <inheritdoc />
    public async Task<RegistrationResult> RegisterAsync(SyncContract contract, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contract);

        using var scope = mScopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();

        var existingRows = await db.Set<RegisteredContractEntity>()
            .Where(c => c.ContractId == contract.ContractId)
            .ToListAsync(ct).ConfigureAwait(false);

        var sameVersion = existingRows.FirstOrDefault(
            c => c.Major == contract.Version.Major && c.Minor == contract.Version.Minor);

        if (sameVersion is not null)
        {
            if (string.Equals(sameVersion.Hash, contract.Hash, StringComparison.Ordinal))
                return new RegistrationResult(RegistrationStatus.Unchanged, contract);

            // A registered version is immutable. Clients have negotiated against it and cached
            // its shape; quietly swapping the document underneath them would produce
            // validation failures nobody could trace back to a re-registration.
            return new RegistrationResult(
                RegistrationStatus.Conflict,
                Problems:
                [
                    $"{contract.ContractId} {contract.Version} is already registered with a different document "
                    + $"(registered {sameVersion.Hash}, offered {contract.Hash}). Publish a new minor version instead.",
                ]);
        }

        // Judged against every registered version of this major, not only the newest. Conversions
        // need not be transitive: integer → string widens and string → guid narrows, both pass on
        // their own, and integer → guid converts to nothing. A peer still on the first version —
        // which is allowed to stay there — would be handed data it cannot read, with every
        // individual registration having been approved.
        //
        // Retired versions are included. Nobody speaks them any more, but records written under
        // them are still in the table, and a conversion that was impossible across one does not
        // become possible because the version was withdrawn.
        var predecessors = existingRows
            .Where(c => c.Major == contract.Version.Major)
            .Select(c => ContractJson.Parse(c.CanonicalJson))
            .ToArray();

        var compatibility = ContractCompatibility.CheckAll(predecessors, contract);

        if (!compatibility.IsCompatible)
            return new RegistrationResult(RegistrationStatus.Incompatible, Problems: compatibility.BreakingChanges);

        // Allowed, but not silently. Every type change lands here, widening included, so the log
        // records what the shape of the data did on the way from one version to the next.
        foreach (var note in compatibility.Notes)
            mLog.LogWarning("Registering {Contract} {Version}: {Note}", contract.ContractId, contract.Version, note);

        // A narrowing is the one verdict the types cannot settle on their own. Ask the records.
        var blocked = await ScanNarrowingsAsync(contract, compatibility.Narrowings, scope, ct).ConfigureAwait(false);

        if (blocked.Count > 0 && mOptions.BlockNarrowingWithBadData)
            return new RegistrationResult(RegistrationStatus.Incompatible, Problems: blocked);

        db.Add(new RegisteredContractEntity
        {
            ContractId = contract.ContractId,
            Major = contract.Version.Major,
            Minor = contract.Version.Minor,
            CanonicalJson = contract.CanonicalJson,
            Hash = contract.Hash,
            RegisteredAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Indexes after the row, not before: an index without a registered contract is
        // orphaned state nobody would think to clean up.
        var store = scope.ServiceProvider.GetRequiredService<IRecordStore>();
        foreach (var collection in contract.Collections)
            await store.EnsureIndexesAsync(contract.ContractId, collection, ct).ConfigureAwait(false);

        await RefreshAsync(ct).ConfigureAwait(false);

        mLog.LogInformation(
            "Registered contract {Contract} {Version} ({Hash})",
            contract.ContractId, contract.Version, contract.Hash);

        return new RegistrationResult(RegistrationStatus.Registered, contract);
    }

    /// <summary>
    /// Asks the stored records whether they survive each narrowing, and reports what it found.
    /// <para>Always runs and always logs; whether a bad row <i>stops</i> the registration is
    /// <see cref="RegistryOptions.BlockNarrowingWithBadData"/>. The scan is the point either way —
    /// a narrowing is a decision an operator is entitled to make, and this is what makes its price
    /// visible before it lands rather than in a support ticket a week later.</para>
    /// </summary>
    /// <returns>One message per narrowing that stored data would break. Empty when all are clear.</returns>
    private async Task<IReadOnlyList<string>> ScanNarrowingsAsync(
        SyncContract contract,
        IReadOnlyList<NarrowingConversion> narrowings,
        IServiceScope scope,
        CancellationToken ct)
    {
        if (narrowings.Count == 0) return [];

        var store = scope.ServiceProvider.GetRequiredService<IRecordStore>();
        var blocked = new List<string>();

        foreach (var narrowing in narrowings)
        {
            var scan = await store.ScanFieldAsync(
                contract.ContractId, narrowing.Collection,
                narrowing.From, narrowing.To, mOptions.NarrowingScanLimit, ct)
                .ConfigureAwait(false);

            var where = $"{contract.ContractId} {narrowing.Collection}.{narrowing.To.Name} "
                        + $"{narrowing.From.Type} → {narrowing.To.Type}";

            // "Scanned n of m" every time, truncated or not. A capped scan that reported only its
            // findings would read as a clean bill of health for rows nobody looked at.
            var coverage = scan.Truncated
                ? $"scanned {scan.Scanned} of {scan.Total} records (capped)"
                : $"scanned {scan.Scanned} of {scan.Total} records";

            if (scan.Blocking == 0)
            {
                mLog.LogInformation("{Where}: {Coverage}, none would fail.", where, coverage);
                continue;
            }

            var samples = scan.SampleKeys.Count > 0
                ? $" First: {string.Join(", ", scan.SampleKeys)}."
                : string.Empty;

            var message = $"{where}: {scan.Blocking} of {scan.Scanned} scanned records would not "
                          + $"convert ({coverage}).{samples}";

            mLog.LogWarning("{Message}", message);
            blocked.Add(message);
        }

        return blocked;
    }

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken ct)
    {
        using var scope = mScopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();

        var rows = await db.Set<RegisteredContractEntity>()
            .AsNoTracking()
            .ToListAsync(ct).ConfigureAwait(false);

        var builder = ImmutableDictionary.CreateBuilder<string, ImmutableSortedDictionary<ContractVersion, Entry>>(
            StringComparer.Ordinal);

        foreach (var group in rows.GroupBy(r => r.ContractId, StringComparer.Ordinal))
        {
            var versions = ImmutableSortedDictionary.CreateBuilder<ContractVersion, Entry>();

            foreach (var row in group)
            {
                try
                {
                    versions[new ContractVersion(row.Major, row.Minor)] =
                        new Entry(ContractJson.Parse(row.CanonicalJson), row.RetiredAt is not null);
                }
                catch (ContractDefinitionException ex)
                {
                    // A stored document that no longer parses means this build is older than
                    // the one that wrote it, or the row was edited by hand. Skipping one
                    // version keeps the rest of the server serving; failing startup over it
                    // would take everything down for one bad row.
                    mLog.LogError(
                        ex, "Registered contract {Contract} {Major}.{Minor} could not be parsed and was skipped.",
                        row.ContractId, row.Major, row.Minor);
                }
            }

            if (versions.Count > 0) builder[group.Key] = versions.ToImmutable();
        }

        mSnapshot = builder.ToImmutable();
    }
}
