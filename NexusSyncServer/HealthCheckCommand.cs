using System.Globalization;

namespace NexusSyncServer;

/// <summary>
/// The <c>--healthcheck</c> mode the container's <c>HEALTHCHECK</c> invokes.
/// <para>The aspnet base image ships neither curl nor wget, and installing one to probe your
/// own process is a package and a CVE surface for something the process can already do. So the
/// binary probes itself: same image, no extra layer.</para>
/// </summary>
internal static class HealthCheckCommand
{
    /// <summary>True when the process was started as a probe rather than as the server.</summary>
    public static bool IsRequested(string[] args) =>
        args.Any(a => string.Equals(a, "--healthcheck", StringComparison.Ordinal));

    /// <summary>
    /// Probes <c>/health</c> on the port this container listens on. Returns 0 when healthy,
    /// 1 otherwise — which is what Docker reads.
    /// </summary>
    public static async Task<int> RunAsync()
    {
        var url = $"{BaseAddress()}/health";

        try
        {
            // Short timeout on purpose: a liveness probe that hangs is a liveness probe that
            // never fails, and Docker's own timeout would then be the only thing stopping it.
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using var response = await http.GetAsync(url).ConfigureAwait(false);

            if (response.IsSuccessStatusCode) return 0;

            await Console.Error.WriteLineAsync(
                $"health probe: {url} returned {(int)response.StatusCode}").ConfigureAwait(false);
            return 1;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"health probe: {url} failed — {ex.GetType().Name}").ConfigureAwait(false);
            return 1;
        }
    }

    private static string BaseAddress()
    {
        // Derived from ASPNETCORE_URLS so the probe follows a changed port automatically.
        // Rewritten to loopback regardless of what the server binds to: "+" and "0.0.0.0" are
        // bind wildcards, not addresses a client can connect to.
        var configured = Environment.GetEnvironmentVariable("ASPNETCORE_URLS")?
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        if (string.IsNullOrEmpty(configured) || !Uri.TryCreate(configured.Replace("+", "localhost", StringComparison.Ordinal), UriKind.Absolute, out var uri))
            return "http://localhost:8080";

        return string.Create(CultureInfo.InvariantCulture, $"{uri.Scheme}://localhost:{uri.Port}");
    }
}
