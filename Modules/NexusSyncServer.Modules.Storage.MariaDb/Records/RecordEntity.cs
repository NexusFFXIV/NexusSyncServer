namespace NexusSyncServer.Modules.Storage.MariaDb.Records;

/// <summary>
/// One stored record, in the generic shape every contract shares.
/// <para>There is deliberately no table per collection. Contracts are registered at runtime,
/// so there is no migration in which per-collection tables could be created — and the moment
/// storage required a migration, an author could no longer add a collection without a server
/// deployment, which is the one thing this design exists to avoid.</para>
/// <para>The cost is real and worth naming: JSON is slower than a purpose-built table, and
/// queries cannot use ordinary column indexes. Contract-declared `indexed` fields get
/// generated column with an index on ites to compensate, and a collection that outgrows this can be
/// served by a module with its own schema — the seam for that is <c>IEntityModule</c>.</para>
/// </summary>
public sealed class RecordEntity
{
    /// <summary>The contract this record belongs to.</summary>
    public required string ContractId { get; set; }

    /// <summary>The collection within that contract.</summary>
    public required string Collection { get; set; }

    /// <summary>The record's key, as declared by the collection.</summary>
    public required string Key { get; set; }

    /// <summary>
    /// Server-assigned sequence, from one global sequence.
    /// <para>Global rather than per collection: the protocol only requires monotonicity
    /// <i>within</i> a collection, which a global sequence satisfies with gaps, and gaps cost
    /// a client nothing because it always takes <c>NextCursor</c> from the response rather
    /// than computing it. Per-collection sequences would mean a counter table and a hot row.</para>
    /// </summary>
    public long Seq { get; set; }

    /// <summary>Incremented on each write. Unused by v1's flow; carried so conflict detection can be added without a wire change.</summary>
    public int Revision { get; set; }

    /// <summary>
    /// The record as JSON, stored in a <c>json</c> column. Null for a tombstone.
    /// <para>Held as a string rather than a document type: it arrives as JSON, leaves as JSON,
    /// and is never inspected by this layer. MariaDB still parses and validates it on the way
    /// in, so a malformed payload cannot be stored.</para>
    /// </summary>
    public string? Payload { get; set; }

    /// <summary>True when the record has been deleted and only the tombstone remains.</summary>
    public bool Deleted { get; set; }

    /// <summary>
    /// The account that last wrote this record.
    /// <para>Nullable and unused by the single-operator model, but present from the start: it
    /// is the column a shared deployment would need, and adding it later would mean migrating
    /// a table that by then holds everything.</para>
    /// </summary>
    public Guid? OwnerId { get; set; }

    /// <summary>When the server applied the change.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
