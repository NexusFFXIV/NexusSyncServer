namespace NexusSyncServer.Hosting.Modules;

/// <summary>
/// A module's answer to "can this instance serve traffic yet?"
/// <para>Separate from liveness on purpose, and the distinction is load-bearing for an
/// orchestrator: <c>/health</c> says the process is alive, so failing it gets the container
/// <b>restarted</b>. <c>/ready</c> says it can serve, so failing it only takes the instance
/// out of rotation. Reporting "database unreachable" as a liveness failure would restart a
/// perfectly healthy server in a loop while the database is down.</para>
/// </summary>
public interface IReadinessCheck
{
    /// <summary>Name shown in the <c>/ready</c> payload.</summary>
    string Name { get; }

    /// <summary>
    /// Returns null when ready, or a short reason when not. The reason is returned to the
    /// caller, so keep it free of connection strings and credentials.
    /// </summary>
    Task<string?> CheckAsync(CancellationToken ct);
}
