using NexusKit.Sync.Contracts;
using NexusKit.Sync.Protocol;

namespace NexusSyncServer.Modules.Storage.MariaDb.Records;

/// <summary>
/// Reading and writing contract-defined records.
/// <para>The one place that touches the generic store, so the API module never writes SQL and
/// the validation rules cannot be bypassed by a second code path.</para>
/// </summary>
public interface IRecordStore
{
    /// <summary>
    /// Applies a push batch and returns one outcome per submitted record.
    /// <para>Validates every record against <paramref name="collection"/> first — the contract
    /// is enforced here, on the server, which is the whole reason a forged client cannot store
    /// what the contract forbids. Records already applied under their operation id come back
    /// as <see cref="RecordWriteStatus.Duplicate"/> rather than being written twice.</para>
    /// </summary>
    Task<IReadOnlyList<RecordOutcome>> WriteAsync(
        string contractId,
        CollectionDefinition collection,
        IReadOnlyList<RecordWrite> writes,
        Guid? ownerId,
        CancellationToken ct);

    /// <summary>Reads one page of changes after a cursor, tombstones included.</summary>
    Task<PullResult> ReadAsync(
        string contractId,
        string collection,
        long since,
        int limit,
        CancellationToken ct);

    /// <summary>
    /// Creates the indexes a collection declares, if they do not exist.
    /// <para>Called when a contract is registered. Since records live in JSON, this is the
    /// only way a collection gets targeted query performance — there is no migration in which
    /// someone could write the index by hand.</para>
    /// </summary>
    Task EnsureIndexesAsync(string contractId, CollectionDefinition collection, CancellationToken ct);

    /// <summary>
    /// Deletes records past their collection's retention and forgets expired operation ids.
    /// Returns how many rows went.
    /// </summary>
    Task<int> PruneAsync(string contractId, CollectionDefinition collection, CancellationToken ct);
}
