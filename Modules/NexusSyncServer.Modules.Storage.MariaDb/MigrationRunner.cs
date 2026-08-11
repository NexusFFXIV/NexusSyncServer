using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NexusSyncServer.Hosting.Persistence;
using NexusSyncServer.Modules.Storage.MariaDb.Records;

namespace NexusSyncServer.Modules.Storage.MariaDb;

/// <summary>
/// Applies every module's pending migrations at startup.
/// </summary>
public sealed class MigrationRunner
{
    private readonly ServerDbContext mDb;
    private readonly IReadOnlyList<IMigrationModule> mModules;
    private readonly ILogger<MigrationRunner> mLog;

    /// <summary>Creates the runner.</summary>
    public MigrationRunner(
        ServerDbContext db,
        IEnumerable<IMigrationModule> modules,
        ILogger<MigrationRunner> log)
    {
        mDb = db;
        mModules = modules.ToArray();
        mLog = log;
    }

    /// <summary>
    /// Creates missing tables, then applies pending steps per module in id order.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        // EnsureCreated brings a fresh database up to the current model in one step. On a
        // database that already has tables it does nothing, which is why the per-module
        // migrations below carry the evolution instead.
        await mDb.Database.EnsureCreatedAsync(ct).ConfigureAwait(false);

        // Strictly after EnsureCreated, and not part of the model. EF decides whether to
        // create tables by asking whether the database has any, and in MariaDB a sequence is
        // reported as a table — creating it first would convince EF the schema already
        // existed and leave the database empty. The model cannot carry it either: the MySQL
        // provider has no sequence support, since MySQL has none to support.
        await mDb.Database.ExecuteSqlRawAsync(
            $"CREATE SEQUENCE IF NOT EXISTS {StorageEntityModule.RecordSequence} START WITH 1 INCREMENT BY 1",
            ct).ConfigureAwait(false);

        var applied = await mDb.Set<AppliedMigrationEntity>()
            .Select(m => new { m.ModuleId, m.MigrationId })
            .ToListAsync(ct).ConfigureAwait(false);

        var known = applied
            .Select(a => $"{a.ModuleId}\0{a.MigrationId}")
            .ToHashSet(StringComparer.Ordinal);

        foreach (var module in mModules)
        {
            var pending = module.Migrations
                .Where(m => !known.Contains($"{module.ModuleId}\0{m.Id}"))
                .OrderBy(m => m.Id, StringComparer.Ordinal)
                .ToArray();

            if (pending.Length == 0) continue;

            mLog.LogInformation(
                "Applying {Count} migration(s) for module {Module}: {Ids}",
                pending.Length, module.ModuleId, string.Join(", ", pending.Select(p => p.Id)));

            foreach (var migration in pending)
            {
                // One transaction per step, not per module. A failure then leaves every step
                // before it applied and recorded, so the retry on next start resumes rather
                // than replaying work that already succeeded.
                await using var tx = await mDb.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

                await migration.UpAsync(mDb, ct).ConfigureAwait(false);

                mDb.Add(new AppliedMigrationEntity
                {
                    ModuleId = module.ModuleId,
                    MigrationId = migration.Id,
                    AppliedAt = DateTimeOffset.UtcNow,
                });

                await mDb.SaveChangesAsync(ct).ConfigureAwait(false);
                await tx.CommitAsync(ct).ConfigureAwait(false);
            }
        }
    }
}
