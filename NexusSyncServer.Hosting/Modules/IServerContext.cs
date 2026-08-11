using Microsoft.Extensions.Configuration;

namespace NexusSyncServer.Hosting.Modules;

/// <summary>
/// What a module may look at while registering itself.
/// <para>Narrow on purpose. A module that could reach the whole
/// <see cref="IServiceProvider"/> at registration time would be able to resolve services out
/// of a half-built container — which works until registration order changes, and then fails
/// somewhere unrelated.</para>
/// </summary>
public interface IServerContext
{
    /// <summary>Merged configuration: appsettings, environment variables, command line.</summary>
    IConfiguration Configuration { get; }

    /// <summary>The ASP.NET Core environment name, e.g. <c>Production</c>.</summary>
    string EnvironmentName { get; }

    /// <summary>
    /// True in development. Use it to relax something for local work, never to switch on a
    /// behaviour production depends on — the container ships as Production and nobody would
    /// find out until it mattered.
    /// </summary>
    bool IsDevelopment { get; }
}
