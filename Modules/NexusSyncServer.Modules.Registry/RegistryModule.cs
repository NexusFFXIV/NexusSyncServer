using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexusSyncServer.Hosting.Catalog;
using NexusSyncServer.Hosting.Modules;
using NexusSyncServer.Hosting.Persistence;

namespace NexusSyncServer.Modules.Registry;

/// <summary>
/// The contract registry: which contracts this server speaks, at which versions.
/// <para>Depends on the storage module for its table and for index creation, so compose it
/// after <c>StorageMariaDbModule</c>.</para>
/// </summary>
public sealed class RegistryModule : IServerModule, IPortalPageModule
{
    /// <inheritdoc />
    public string Id => "nexussyncserver.registry";

    /// <inheritdoc />
    public System.Reflection.Assembly ComponentAssembly => typeof(RegistryModule).Assembly;

    /// <inheritdoc />
    /// <remarks>
    /// Reading a contract in the browser needs a session; reading one over the API needs a key
    /// with <c>contract:pull</c>. Two surfaces, two credentials — see the page itself for why
    /// a session is deliberately not accepted by the API.
    /// </remarks>
    public IEnumerable<PortalPage> Pages =>
    [
        new PortalPage("/contracts", "Contracts", Order: 20),
    ];

    /// <inheritdoc />
    public void Register(IServiceCollection services, IServerContext context)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(context);

        var options = new RegistryOptions();
        context.Configuration.GetSection(RegistryOptions.SectionName).Bind(options);
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(options));

        services.AddSingleton<IEntityModule, RegistryEntityModule>();

        // The mapping above covers a database being created; this covers one that already exists.
        // A new column needs both, and neither substitutes for the other.
        services.AddSingleton<IMigrationModule, RegistryMigrations>();

        // Singleton because the snapshot it holds is the point — a scoped registry would
        // rebuild or re-query per request, which is exactly what the cache exists to avoid.
        services.AddSingleton<IContractRegistry, ContractRegistry>();

        // Offered to whoever wants to build a permission picker — today the auth module's key
        // manager. Registered here rather than there so the dependency points one way: the
        // module that knows about contracts provides, the module that issues keys consumes an
        // interface from Hosting and never learns this module exists.
        services.AddSingleton<IScopeCatalog, RegistryScopeCatalog>();

        services.AddHostedService<RegistryStartupService>();
    }
}
