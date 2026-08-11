using Microsoft.Extensions.DependencyInjection;

namespace NexusSyncServer.Hosting.Modules;

/// <summary>
/// One composable piece of the server.
/// <para>Mirrors <c>IPluginModule</c> on the client side deliberately: an author who has
/// written a NexusKit module already knows this shape, and the two halves of a feature end up
/// looking like each other.</para>
/// <para>Modules are composed at <b>build time</b> — referenced as packages and registered in
/// the host's composition root. There is no runtime assembly loading, and adding it is not a
/// wanted feature: foreign code in a server process is an attack surface and an operational
/// problem (version conflicts, partial failures, unclear migration order). The flexibility
/// users need comes from registrable <i>contracts</i>, which are data.</para>
/// </summary>
public interface IServerModule
{
    /// <summary>
    /// Stable identifier, e.g. <c>nexussyncserver.api</c>. Used in startup logs, in the applied-
    /// migrations table, and to detect a module registered twice.
    /// <para>It outlives renames of the CLR type, which is why it is declared rather than
    /// derived from <c>GetType().Name</c>: a refactor must not orphan a module's migration
    /// history.</para>
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Registers the module's services. Called once at startup, in registration order.
    /// <para>Do not resolve anything here — the container is still being built. Work that
    /// needs a running service belongs in an <c>IHostedService</c> the module registers.</para>
    /// </summary>
    void Register(IServiceCollection services, IServerContext context);
}
