using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NexusSyncServer.Hosting.Modules;

namespace NexusSyncServer.Modules.Storage.MariaDb;

/// <summary>
/// Reports the database as a readiness condition, not a liveness one.
/// <para>The distinction decides what an orchestrator does when MariaDB is down. Reported
/// here, the instance leaves the load balancer and comes back by itself once the database
/// returns. Reported on <c>/health</c>, the container would be killed and restarted in a loop
/// for a fault that has nothing to do with it.</para>
/// </summary>
public sealed class DatabaseReadinessCheck : IReadinessCheck
{
    private readonly IServiceProvider mServices;

    /// <summary>Creates the check.</summary>
    /// <remarks>
    /// Resolves the context per call from the root provider rather than holding one: the
    /// check is a singleton and a <see cref="DbContext"/> is scoped, so capturing one would
    /// hand every probe the same connection for the lifetime of the process.
    /// </remarks>
    public DatabaseReadinessCheck(IServiceProvider services) => mServices = services;

    /// <inheritdoc />
    public string Name => "database";

    /// <inheritdoc />
    public async Task<string?> CheckAsync(CancellationToken ct)
    {
        using var scope = mServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();

        // CanConnect rather than a query: it answers the question without depending on any
        // table existing, so a database that is up but not yet migrated reports honestly.
        return await db.Database.CanConnectAsync(ct).ConfigureAwait(false)
            ? null
            : "cannot connect";
    }
}
