using Microsoft.EntityFrameworkCore;
using NexusSyncServer.Hosting.Persistence;
using NexusSyncServer.Modules.Storage.MariaDb.Records;

namespace NexusSyncServer.Modules.Storage.MariaDb;

/// <summary>
/// The storage module's own tables: the generic record store, the write-idempotency log, and
/// the applied-migrations ledger every other module's migrations are recorded in.
/// </summary>
public sealed class StorageEntityModule : IEntityModule
{
    /// <summary>Table-name prefix every table in this module carries.</summary>
    public const string Prefix = "storage";

    /// <summary>Physical name of the record table, for the hand-written upsert.</summary>
    public const string RecordsTable = Prefix + "_records";

    /// <summary>
    /// Name of the MariaDB sequence that drives the pull cursor.
    /// <para>Created by <see cref="MigrationRunner"/> rather than declared on the model.
    /// <c>modelBuilder.HasSequence</c> is a no-op here: the MySQL provider has no sequence
    /// support, because MySQL itself has none. MariaDB does, and that is the one place this
    /// module leans on a MariaDB extension rather than plain MySQL.</para>
    /// </summary>
    public const string RecordSequence = Prefix + "_record_seq";

    /// <inheritdoc />
    public string SchemaName => Prefix;

    /// <inheritdoc />
    public void ConfigureEntities(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<RecordEntity>(e =>
        {
            e.ToTable("records");
            e.HasKey(x => new { x.ContractId, x.Collection, x.Key });

            e.Property(x => x.ContractId).HasColumnName("contract_id").HasMaxLength(128);
            e.Property(x => x.Collection).HasColumnName("collection").HasMaxLength(64);

            // "key" is reserved in MariaDB, and quoting it in every hand-written statement is
            // the kind of detail that gets forgotten exactly once. 512 chars of utf8mb4 also
            // keeps the composite primary key at 2816 bytes, inside InnoDB's 3072-byte limit.
            e.Property(x => x.Key).HasColumnName("record_key").HasMaxLength(512);

            // No database-side default: the sequence is drawn explicitly by the upsert, which
            // has to draw it again on update anyway to move the row to the end of the cursor.
            e.Property(x => x.Seq).HasColumnName("seq");
            e.Property(x => x.Revision).HasColumnName("revision").HasDefaultValue(1);
            e.Property(x => x.Payload).HasColumnName("payload").HasColumnType("json");
            e.Property(x => x.Deleted).HasColumnName("deleted").HasDefaultValue(false);
            e.Property(x => x.OwnerId).HasColumnName("owner_id");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            // The pull cursor's index. Every read of a downlink collection is exactly this
            // predicate, so it is the one index that must exist before any data does.
            e.HasIndex(x => new { x.ContractId, x.Collection, x.Seq }).HasDatabaseName("ix_records_cursor");
        });

        modelBuilder.Entity<AppliedOpEntity>(e =>
        {
            e.ToTable("applied_ops");
            e.HasKey(x => x.OpId);

            e.Property(x => x.OpId).HasColumnName("op_id").HasMaxLength(64);
            e.Property(x => x.ContractId).HasColumnName("contract_id").HasMaxLength(128);
            e.Property(x => x.Collection).HasColumnName("collection").HasMaxLength(64);
            e.Property(x => x.AppliedAt).HasColumnName("applied_at");

            // Pruned by age, which is why the OpId is expected to be a ULID — sortable ids
            // mean the dedupe window can be trimmed instead of growing forever.
            e.HasIndex(x => x.AppliedAt).HasDatabaseName("ix_applied_ops_age");
        });

        modelBuilder.Entity<AppliedMigrationEntity>(e =>
        {
            e.ToTable("applied_migrations");
            e.HasKey(x => new { x.ModuleId, x.MigrationId });

            e.Property(x => x.ModuleId).HasColumnName("module_id").HasMaxLength(128);
            e.Property(x => x.MigrationId).HasColumnName("migration_id").HasMaxLength(128);
            e.Property(x => x.AppliedAt).HasColumnName("applied_at");
        });
    }
}
