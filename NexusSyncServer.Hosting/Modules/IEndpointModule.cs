using Microsoft.AspNetCore.Routing;

namespace NexusSyncServer.Hosting.Modules;

/// <summary>
/// A module that contributes HTTP endpoints.
/// <para>Implemented alongside <see cref="IServerModule"/> — the module registers its services
/// there and maps its routes here. Splitting the two keeps registration and routing in the
/// phase each belongs to, since routes need a built container and services do not.</para>
/// <para>The shape follows Carter's <c>ICarterModule</c>: one method, given the route builder,
/// mapping minimal-API endpoints. Nothing exotic, and familiar to anyone who has seen it.</para>
/// </summary>
public interface IEndpointModule
{
    /// <summary>
    /// Maps this module's routes. Called once at startup, after the container is built.
    /// <para>Paths should come from <c>SyncRoutes</c> where the protocol defines them, so
    /// client and server cannot drift apart over a string literal.</para>
    /// </summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
