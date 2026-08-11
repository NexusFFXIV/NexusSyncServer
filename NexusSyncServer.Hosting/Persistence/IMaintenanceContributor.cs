namespace NexusSyncServer.Hosting.Persistence;

/// <summary>
/// One unit of periodic housekeeping — pruning expired records, clearing stale rate-limit
/// counters, vacuuming.
/// <para>The host drives every registered contributor on a shared timer rather than each
/// module starting its own background loop. One loop is one place to see what is running, one
/// place to stop it on shutdown, and no risk of a module leaking a timer that outlives it.</para>
/// </summary>
public interface IMaintenanceContributor
{
    /// <summary>Name for logs and the admin view.</summary>
    string Name { get; }

    /// <summary>How often this should run. The host may run it less often, never more.</summary>
    TimeSpan Interval { get; }

    /// <summary>
    /// Does the work. Must honour the token — it fires on shutdown, and a contributor that
    /// ignores it delays every container restart.
    /// </summary>
    Task RunAsync(CancellationToken ct);
}
