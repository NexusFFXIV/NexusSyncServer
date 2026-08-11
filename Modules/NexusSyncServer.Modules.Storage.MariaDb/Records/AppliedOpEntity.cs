namespace NexusSyncServer.Modules.Storage.MariaDb.Records;

/// <summary>
/// A push operation the server has already applied.
/// <para>This table is what makes push idempotent. When a client's response is lost and it
/// retries the same batch, the operation ids are recognised and the writes are reported as
/// <c>Duplicate</c> rather than applied a second time — which is the difference between a
/// flaky connection costing a retry and it costing duplicated data.</para>
/// <para>Rows are pruned by age, not kept forever. A client that has been offline longer than
/// the window and then retries would re-apply its writes; since writes are keyed upserts, that
/// is harmless for the data and only wastes a round trip.</para>
/// </summary>
public sealed class AppliedOpEntity
{
    /// <summary>The client-generated operation id. A ULID by convention, which is what makes age-based pruning possible.</summary>
    public required string OpId { get; set; }

    /// <summary>Contract the operation belonged to — for auditing, not for lookup.</summary>
    public required string ContractId { get; set; }

    /// <summary>Collection the operation belonged to.</summary>
    public required string Collection { get; set; }

    /// <summary>When it was applied.</summary>
    public DateTimeOffset AppliedAt { get; set; }
}
