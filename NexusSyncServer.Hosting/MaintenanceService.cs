using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexusSyncServer.Hosting.Persistence;

namespace NexusSyncServer.Hosting;

/// <summary>
/// Drives every registered <see cref="IMaintenanceContributor"/> from one loop.
/// </summary>
internal sealed class MaintenanceService : BackgroundService
{
    // Coarse tick: contributors declare intervals in minutes or hours, so checking once a
    // minute is precise enough and keeps an idle server genuinely idle.
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(1);

    private readonly IReadOnlyList<IMaintenanceContributor> mContributors;
    private readonly ILogger<MaintenanceService> mLog;
    private readonly Dictionary<string, DateTimeOffset> mLastRun = new(StringComparer.Ordinal);

    public MaintenanceService(
        IEnumerable<IMaintenanceContributor> contributors,
        ILogger<MaintenanceService> log)
    {
        mContributors = contributors.ToArray();
        mLog = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (mContributors.Count == 0) return;

        mLog.LogInformation(
            "Maintenance loop started with {Count} contributor(s): {Names}",
            mContributors.Count, string.Join(", ", mContributors.Select(c => c.Name)));

        using var timer = new PeriodicTimer(Tick);

        // Deliberately no immediate first pass. Startup is already doing migrations and
        // warming connections; adding a vacuum to that is how a cold start turns into a
        // failed readiness probe.
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            foreach (var contributor in mContributors)
            {
                if (stoppingToken.IsCancellationRequested) return;
                await RunIfDueAsync(contributor, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private async Task RunIfDueAsync(IMaintenanceContributor contributor, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        if (mLastRun.TryGetValue(contributor.Name, out var last) && now - last < contributor.Interval)
            return;

        mLastRun[contributor.Name] = now;

        try
        {
            await contributor.RunAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown, not a fault.
        }
        catch (Exception ex)
        {
            // One failing contributor must not take the loop down with it — housekeeping is
            // the least important thing this process does, and a server that stops serving
            // because a prune job threw would be a poor trade.
            mLog.LogError(ex, "Maintenance contributor {Name} failed; continuing.", contributor.Name);
        }
    }
}
