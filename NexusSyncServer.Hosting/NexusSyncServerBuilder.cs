using Microsoft.Extensions.DependencyInjection;
using NexusSyncServer.Hosting.Modules;

namespace NexusSyncServer.Hosting;

/// <summary>
/// Collects the modules an instance is composed of.
/// <para>Obtained from <see cref="NexusSyncServerHostingExtensions.AddNexusSyncServer"/>; not constructed
/// directly.</para>
/// </summary>
public sealed class NexusSyncServerBuilder
{
    private readonly IServiceCollection mServices;
    private readonly IServerContext mContext;
    private readonly List<IServerModule> mModules = [];
    private readonly HashSet<string> mIds = new(StringComparer.Ordinal);

    internal NexusSyncServerBuilder(IServiceCollection services, IServerContext context)
    {
        mServices = services;
        mContext = context;
    }

    internal IReadOnlyList<IServerModule> Modules => mModules;

    /// <summary>Adds a module with a parameterless constructor.</summary>
    public NexusSyncServerBuilder AddModule<TModule>() where TModule : IServerModule, new() =>
        AddModule(new TModule());

    /// <summary>Adds an already-constructed module.</summary>
    /// <exception cref="InvalidOperationException">A module with the same id is already registered.</exception>
    public NexusSyncServerBuilder AddModule(IServerModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        if (!mIds.Add(module.Id))
        {
            // Registering twice would run Register twice, which for anything using
            // AddSingleton produces two instances behind one interface and a bug that only
            // shows up as "the background worker ran the job twice".
            throw new InvalidOperationException(
                $"Module '{module.Id}' is already registered. Each module may be composed in once.");
        }

        mModules.Add(module);

        // A module that also contributes pages has to be resolvable, not just held here: the
        // router asks DI which assemblies carry @page components, and the layout asks it for
        // navigation entries. Registering the instance rather than the type keeps it the same
        // object the composition root added — a second one would answer differently.
        if (module is IPortalPageModule pageModule)
            mServices.AddSingleton(pageModule);

        module.Register(mServices, mContext);
        return this;
    }
}
