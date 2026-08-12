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

    /// <summary>
    /// Counts stored records whose value for one field would not satisfy a new declaration.
    /// <para>The question a narrowing type change raises and the types cannot answer:
    /// <c>integer → boolean</c> is fine exactly when the column holds nothing but 0 and 1. Asked
    /// before such a change is registered, so the cost is known in advance.</para>
    /// <para>Judged with <c>FieldValueConversion</c>, which re-expresses each stored value in the
    /// new shape and then hands it to the same <c>PayloadValidator</c> the write path uses. Not SQL
    /// that reimplements those rules: two rule sets that agree today are two rule sets that
    /// disagree eventually, and the failure mode is the worst available — a registration accepted
    /// on the strength of a check that the writes then fail.</para>
    /// <para>Both declarations are needed, not just the new one. "Would this value be valid as a
    /// boolean" is not the question and answers it wrongly: a stored <c>0</c> is a JSON number and
    /// no number is a JSON boolean, so every row would count as blocking including the ones that
    /// convert perfectly well. The question is whether it <i>converts</i>, and that cannot be
    /// answered without knowing what it was.</para>
    /// <para>Absent and null values do not count. They were legal before and stay legal — this asks
    /// about values that exist, not about a field becoming required.</para>
    /// </summary>
    /// <param name="contractId">The contract owning the collection.</param>
    /// <param name="collection">Collection name.</param>
    /// <param name="from">The declaration the stored values were written under.</param>
    /// <param name="to">The declaration they must satisfy — the <i>new</i> one.</param>
    /// <param name="rowLimit">Most records to read; zero or less means all of them.</param>
    /// <param name="ct">Cancels the scan.</param>
    Task<NarrowingScan> ScanFieldAsync(
        string contractId,
        string collection,
        FieldDefinition from,
        FieldDefinition to,
        int rowLimit,
        CancellationToken ct);
}

/// <summary>
/// What a narrowing scan found.
/// </summary>
/// <param name="Total">Live records in the collection.</param>
/// <param name="Scanned">How many were actually read. Below <paramref name="Total"/> when capped.</param>
/// <param name="Blocking">Of those read, how many carry a value the new declaration rejects.</param>
/// <param name="SampleKeys">A handful of offending record keys, so the problem can be looked at.</param>
/// <param name="Truncated">
/// True when the cap stopped the scan early. Reported separately, and never folded into
/// <paramref name="Blocking"/>: zero blocking rows out of a truncated scan means "none found so
/// far", which is not the same claim as "none exist", and only the caller can decide what to do
/// with the difference.
/// </param>
public sealed record NarrowingScan(
    long Total,
    long Scanned,
    long Blocking,
    IReadOnlyList<string> SampleKeys,
    bool Truncated);
