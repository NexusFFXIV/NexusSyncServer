using System.Reflection;

namespace NexusSyncServer.Hosting.Modules;

/// <summary>
/// A module that contributes pages to the server's web interface.
/// <para><b>Modules ship components, not pages.</b> A page here is a thin default mounting of
/// a component the module also exposes on its own. That distinction is what lets an operator
/// choose: run the interface as delivered, or drop the same components into one of their own.
/// A module that only offered fixed pages would force the first option.</para>
/// </summary>
public interface IPortalPageModule
{
    /// <summary>
    /// The assembly holding the routable components.
    /// <para>Handed to Blazor's router as an additional assembly, because routes come from
    /// <c>@page</c> attributes rather than from anything registered here. Without it the
    /// module's pages compile fine and are simply never reachable.</para>
    /// </summary>
    Assembly ComponentAssembly { get; }

    /// <summary>Navigation entries for the pages this module mounts by default.</summary>
    IEnumerable<PortalPage> Pages { get; }
}
