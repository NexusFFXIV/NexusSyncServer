using Microsoft.EntityFrameworkCore;
using NexusSyncServer.Hosting.Persistence;

namespace NexusSyncServer.Modules.Storage.MariaDb;

/// <summary>
/// The one <see cref="DbContext"/> every module contributes to.
/// <para>One context rather than one per module: a single connection pool, cross-module
/// queries that are ordinary joins, and one transaction when a request touches two modules —
/// accepting an API key's rate-limit tick and writing the records it carried should not be
/// able to half-succeed.</para>
/// <para>Isolation comes from a table-name prefix. Each <see cref="IEntityModule"/> declares
/// one and the context applies it to everything that module maps, so two modules can both own
/// a table called <c>state</c> without colliding.</para>
/// <para>A prefix rather than a real namespace because in MariaDB a schema <i>is</i> a
/// database. Giving each module its own would mean several connection strings, several
/// grants and a backup per module, to separate tables that are meant to be joinable and to
/// live or die together. <c>storage_records</c> and <c>auth_accounts</c> in one database buy
/// the same freedom from collisions at none of that cost.</para>
/// </summary>
public sealed class ServerDbContext : DbContext
{
    private readonly IReadOnlyList<IEntityModule> mEntityModules;

    /// <summary>Creates the context with the entity modules resolved from DI.</summary>
    public ServerDbContext(DbContextOptions<ServerDbContext> options, IEnumerable<IEntityModule> entityModules)
        : base(options) =>
        mEntityModules = entityModules.ToArray();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (var module in mEntityModules)
        {
            var before = modelBuilder.Model.GetEntityTypes().Select(e => e.Name).ToHashSet(StringComparer.Ordinal);

            module.ConfigureEntities(modelBuilder);

            // Prefix whatever the module just added, so implementations do not repeat the
            // prefix on every entity — and cannot forget to.
            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                if (before.Contains(entity.Name)) continue;
                if (entity.GetTableName() is not { } table) continue;
                if (table.StartsWith(module.SchemaName + "_", StringComparison.Ordinal)) continue;

                entity.SetTableName($"{module.SchemaName}_{table}");
            }
        }
    }
}
