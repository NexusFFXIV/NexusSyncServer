using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using NexusKit.Sync.Contracts;
using NexusSyncServer.Modules.Storage.MariaDb;

namespace NexusSyncServer.Modules.Auth;

/// <summary>Records what a key is doing with a contract, one handshake at a time.</summary>
public interface IKeyContractStateWriter
{
    /// <summary>
    /// Notes that this key just handshook for this contract.
    /// </summary>
    /// <param name="keyRowId">The key's row identity, not its prefix.</param>
    /// <param name="contractId">The contract that was negotiated.</param>
    /// <param name="negotiated">The version the server actually served.</param>
    /// <param name="supported">
    /// The highest version the peer says it could speak, or null when it did not say — which is
    /// what an older build does, and must stay distinguishable from "could only do this one".
    /// </param>
    /// <param name="ct">Cancels the write.</param>
    Task RecordAsync(
        Guid keyRowId,
        string contractId,
        ContractVersion negotiated,
        ContractVersion? supported,
        CancellationToken ct);
}

/// <inheritdoc />
public sealed class KeyContractStateWriter : IKeyContractStateWriter
{
    private readonly ServerDbContext mDb;

    /// <summary>Creates the writer.</summary>
    public KeyContractStateWriter(ServerDbContext db) => mDb = db;

    /// <inheritdoc />
    public async Task RecordAsync(
        Guid keyRowId,
        string contractId,
        ContractVersion negotiated,
        ContractVersion? supported,
        CancellationToken ct)
    {
        // Upsert in one statement rather than read-modify-write. Handshakes for the same key run
        // concurrently in the ordinary case — several plugin instances behind one key — and a
        // read-then-write would race into a duplicate-key failure on a path that must not fail.
        const string sql =
            """
            INSERT INTO auth_key_contract_state
                (key_id, contract_id, negotiated_major, negotiated_minor,
                 supported_major, supported_minor, last_seen_at)
            VALUES
                ({0}, {1}, {2}, {3}, {4}, {5}, {6})
            ON DUPLICATE KEY UPDATE
                negotiated_major = VALUES(negotiated_major),
                negotiated_minor = VALUES(negotiated_minor),
                supported_major  = VALUES(supported_major),
                supported_minor  = VALUES(supported_minor),
                last_seen_at     = VALUES(last_seen_at)
            """;

        await mDb.Database.ExecuteSqlAsync(
            FormattableStringFactory.Create(
                sql,
                keyRowId,
                contractId,
                negotiated.Major,
                negotiated.Minor,
                supported?.Major,
                supported?.Minor,
                DateTimeOffset.UtcNow),
            ct).ConfigureAwait(false);
    }
}
