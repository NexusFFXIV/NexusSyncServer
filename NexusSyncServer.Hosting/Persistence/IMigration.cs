using Microsoft.EntityFrameworkCore;

namespace NexusSyncServer.Hosting.Persistence;

/// <summary>
/// One forward-only schema step contributed by a module.
/// <para>Migrations run at startup, which makes upgrades easy and rollbacks hard: an operator
/// rolls back by pulling the previous image tag, and that does not un-drop a column. Prefer
/// additive changes, and call out anything destructive in the release notes.</para>
/// </summary>
public interface IMigration
{
    /// <summary>
    /// Stable, sortable identifier — typically a timestamp prefix such as
    /// <c>20260804_add_revoked_at</c>. Unique within the owning module.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Applies the step. Runs inside a transaction, so an aborted startup rolls back cleanly
    /// and the next start retries.
    /// </summary>
    Task UpAsync(DbContext context, CancellationToken ct);
}
