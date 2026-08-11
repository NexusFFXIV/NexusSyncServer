using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexusSyncServer.Hosting.Modules;
using NexusSyncServer.Modules.Auth.Providers;

namespace NexusSyncServer.Modules.Auth.XivAuth;

/// <summary>
/// Adds XIVAuth as a sign-in option.
/// <para>Compose after <c>AuthModule</c>. Enable it with <c>Auth:XivAuth:Enabled</c> and a
/// client id and secret from the XIVAuth developer portal; the endpoints default to the
/// public instance.</para>
/// </summary>
public sealed class XivAuthModule : IServerModule
{
    /// <inheritdoc />
    public string Id => "nexussyncserver.auth.xivauth";

    /// <inheritdoc />
    public void Register(IServiceCollection services, IServerContext context)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(context);

        var options = new XivAuthOptions();
        context.Configuration.GetSection(XivAuthOptions.SectionName).Bind(options);
        options.Validate(XivAuthIdentityProvider.ProviderId);

        // A disabled provider registers nothing at all, so it cannot appear on the sign-in
        // page by accident — rather than registering it and filtering later.
        if (!options.Enabled) return;

        services.AddSingleton(options);
        services.AddHttpClient<XivAuthIdentityProvider>();

        services.AddSingleton<IIdentityProvider>(sp => new XivAuthIdentityProvider(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(XivAuthIdentityProvider)),
            sp.GetRequiredService<XivAuthOptions>(),
            sp.GetRequiredService<ILogger<XivAuthIdentityProvider>>()));
    }
}
