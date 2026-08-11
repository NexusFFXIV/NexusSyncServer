using NexusSyncServer.Modules.Auth.Providers;

namespace NexusSyncServer.Modules.Auth.Discord;

/// <summary>
/// Discord configuration, bound from the <c>Auth:Discord</c> configuration section.
/// </summary>
public sealed class DiscordOptions : OAuth2ProviderOptions
{
    /// <summary>Configuration section this binds from.</summary>
    public const string SectionName = "Auth:Discord";

    /// <summary>Creates the options with Discord's endpoints and the narrowest useful scope.</summary>
    public DiscordOptions()
    {
        AuthorizationEndpoint = new Uri("https://discord.com/oauth2/authorize");
        TokenEndpoint = new Uri("https://discord.com/api/v10/oauth2/token");
        UserEndpoint = new Uri("https://discord.com/api/v10/users/@me");

        // `identify` only. The `email` scope is deliberately not requested — this server has
        // no use for an address, and not asking is the simplest way to never hold one.
        Scopes.Add("identify");
    }

    /// <summary>
    /// Restrict sign-in to members of this Discord guild, or null for anyone.
    /// <para>Requires the <c>guilds</c> scope, which this provider does not request by
    /// default — set it explicitly if you want the check. Reserved; the membership lookup is
    /// not implemented yet, and a value here without it would be a silent no-op, so it is
    /// rejected at startup instead.</para>
    /// </summary>
    public string? RequiredGuildId { get; set; }

    /// <inheritdoc />
    public override void Validate(string providerId)
    {
        base.Validate(providerId);

        if (Enabled && !string.IsNullOrWhiteSpace(RequiredGuildId))
        {
            // Failing loudly beats pretending to enforce something. An operator who sets this
            // believes sign-in is restricted; silently ignoring it would be the worst outcome.
            throw new InvalidOperationException(
                $"Auth provider '{providerId}': {nameof(RequiredGuildId)} is not implemented yet. "
                + "Remove it rather than relying on a restriction that is not applied.");
        }
    }
}
