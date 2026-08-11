namespace NexusSyncServer.Hosting.Modules;

/// <summary>
/// A navigation entry for a page a module contributes.
/// </summary>
/// <param name="Route">
/// Where the page lives, e.g. <c>/account/keys</c>. Must match the component's <c>@page</c>
/// directive — this record describes the link, it does not create the route. Blazor discovers
/// routes from the attribute, which is why <see cref="IPortalPageModule.ComponentAssembly"/>
/// exists.
/// </param>
/// <param name="Title">Label shown in navigation.</param>
/// <param name="RequiredScope">
/// Scope needed to see the entry, or null for everyone.
/// <para><b>This hides a link. It does not protect a page.</b> The page itself must check —
/// a hidden link is still a reachable URL.</para>
/// </param>
/// <param name="Order">Sort order. Ties fall back to <paramref name="Title"/>.</param>
public sealed record PortalPage(
    string Route,
    string Title,
    string? RequiredScope = null,
    int Order = 0);
