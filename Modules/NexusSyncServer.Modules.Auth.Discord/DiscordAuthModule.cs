using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexusSyncServer.Hosting.Modules;
using NexusSyncServer.Modules.Auth.Providers;

namespace NexusSyncServer.Modules.Auth.Discord;

/// <summary>
/// Adds Discord as a sign-in option.
/// <para>Compose after <c>AuthModule</c>. Enable it with <c>Auth:Discord:Enabled</c> and a
/// client id and secret from the Discord developer portal.</para>
/// </summary>
public sealed class DiscordAuthModule : IServerModule
{
    /// <inheritdoc />
    public string Id => "nexussyncserver.auth.discord";

    /// <inheritdoc />
    public void Register(IServiceCollection services, IServerContext context)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(context);

        var options = new DiscordOptions();
        context.Configuration.GetSection(DiscordOptions.SectionName).Bind(options);
        options.Validate(DiscordIdentityProvider.ProviderId);

        if (!options.Enabled) return;

        services.AddSingleton(options);
        services.AddHttpClient<DiscordIdentityProvider>();

        services.AddSingleton<IIdentityProvider>(sp => new DiscordIdentityProvider(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(DiscordIdentityProvider)),
            sp.GetRequiredService<DiscordOptions>(),
            sp.GetRequiredService<ILogger<DiscordIdentityProvider>>()));
    }
}
