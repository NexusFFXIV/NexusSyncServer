using Microsoft.Extensions.DependencyInjection;
using NexusSyncServer.Hosting.Modules;
using NexusKit.Sync.Protocol;

namespace NexusSyncServer.Modules.Api;

/// <summary>
/// The sync endpoints — the module that makes this a server rather than a database with a
/// login page.
/// <para>Compose it last: it resolves the registry, the record store and the authenticator,
/// so <c>StorageMariaDbModule</c>, <c>RegistryModule</c> and <c>AuthModule</c> all belong
/// before it.</para>
/// </summary>
public sealed class ApiModule : IServerModule
{
    /// <inheritdoc />
    public string Id => "nexussyncserver.api";

    /// <inheritdoc />
    public void Register(IServiceCollection services, IServerContext context)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(context);

        services.AddSingleton<IEndpointModule, SyncEndpoints>();

        // The protocol envelopes are serialised with the settings from the norm, not with
        // ASP.NET's defaults. Both sides using the same instance is what keeps a field from
        // silently arriving as null because one end changed a naming policy.
        services.ConfigureHttpJsonOptions(json =>
        {
            var shared = SyncJson.Options;

            json.SerializerOptions.PropertyNamingPolicy = shared.PropertyNamingPolicy;
            json.SerializerOptions.PropertyNameCaseInsensitive = shared.PropertyNameCaseInsensitive;
            json.SerializerOptions.DefaultIgnoreCondition = shared.DefaultIgnoreCondition;
            json.SerializerOptions.NumberHandling = shared.NumberHandling;

            foreach (var converter in shared.Converters) json.SerializerOptions.Converters.Add(converter);
        });
    }
}
