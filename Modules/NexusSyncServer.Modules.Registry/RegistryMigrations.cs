using Microsoft.EntityFrameworkCore;
using NexusSyncServer.Hosting.Persistence;

namespace NexusSyncServer.Modules.Registry;

/// <summary>
/// Schema evolution for <c>registry_contracts</c>.
/// <para>Needed because <c>EnsureCreated</c> only builds tables that do not exist yet. A server that
/// has been running has the table already, so a new column reaches it only through a step like this
/// one — and a fresh database gets the same column from the entity mapping. Both are required;
/// either alone leaves half the installations wrong.</para>
/// </summary>
public sealed class RegistryMigrations : IMigrationModule
{
    /// <inheritdoc />
    public string ModuleId => "nexussyncserver.registry";

    /// <inheritdoc />
    public IReadOnlyList<IMigration> Migrations { get; } = [new AddRetiredAt()];

    /// <summary>Adds the column that takes a version out of service without deleting it.</summary>
    private sealed class AddRetiredAt : IMigration
    {
        public string Id => "20260812_add_retired_at";

        public async Task UpAsync(DbContext context, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(context);

            // IF NOT EXISTS so that a database created by EnsureCreated from the current model —
            // which already has the column — reaches the same end state as one being upgraded.
            // Purely additive and nullable: existing rows read as "still served", which is what
            // they were before this column existed.
            //
            // Plain datetime, matching what EF emits for a DateTimeOffset here and what
            // registered_at already is. datetime(6) works and is wrong: the two creation paths
            // would then produce schemas that differ by a column type, which is the kind of
            // divergence nobody notices until something compares them.
            await context.Database.ExecuteSqlRawAsync(
                "ALTER TABLE registry_contracts ADD COLUMN IF NOT EXISTS retired_at datetime NULL",
                ct).ConfigureAwait(false);
        }
    }
}
