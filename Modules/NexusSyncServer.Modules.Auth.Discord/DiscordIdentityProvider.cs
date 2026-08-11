using System.Text.Json;
using Microsoft.Extensions.Logging;
using NexusSyncServer.Modules.Auth.Providers;

namespace NexusSyncServer.Modules.Auth.Discord;

/// <summary>
/// Sign-in via Discord.
/// <para>Familiar to everyone in the ecosystem, which is its whole advantage. Note what it
/// cannot tell you: a Discord account says nothing about whether the person plays the game.
/// If that matters, compose in XIVAuth alongside it.</para>
/// </summary>
public sealed class DiscordIdentityProvider : OAuth2IdentityProvider
{
    /// <summary>The provider id used in URLs, configuration and the identity table.</summary>
    public const string ProviderId = "discord";

    /// <summary>Creates the provider.</summary>
    public DiscordIdentityProvider(HttpClient http, DiscordOptions options, ILogger<DiscordIdentityProvider> log)
        : base(http, options, log)
    {
    }

    /// <inheritdoc />
    public override string Id => ProviderId;

    /// <inheritdoc />
    public override string DisplayName => "Discord";

    /// <summary>
    /// Discord's own blurple and wordmark glyph. <c>#5865F2</c> is the published brand colour.
    /// </summary>
    public override ProviderBranding Branding { get; } = new(
        Accent: "#5865F2",
        OnAccent: "#ffffff",
        IconSvg: """
                 <svg viewBox="0 0 24 24" width="18" height="18" fill="currentColor" aria-hidden="true">
                   <path d="M20.317 4.369A19.79 19.79 0 0 0 15.432 3c-.21.375-.455.88-.624 1.283a18.27 18.27 0 0 0-5.487 0A12.6 12.6 0 0 0 8.69 3a19.736 19.736 0 0 0-4.885 1.372C.72 8.977-.114 13.47.302 17.9a19.9 19.9 0 0 0 6.032 3.056c.487-.66.92-1.361 1.293-2.099a12.9 12.9 0 0 1-2.036-.978c.171-.126.338-.257.5-.392a14.2 14.2 0 0 0 12.02 0c.163.135.33.266.5.392-.65.383-1.334.71-2.04.98.374.736.807 1.438 1.293 2.098a19.86 19.86 0 0 0 6.035-3.056c.5-5.177-.838-9.63-3.582-13.532ZM8.02 15.2c-1.183 0-2.157-1.086-2.157-2.42 0-1.332.955-2.418 2.157-2.418 1.21 0 2.176 1.095 2.156 2.419 0 1.333-.955 2.419-2.156 2.419Zm7.975 0c-1.183 0-2.157-1.086-2.157-2.42 0-1.332.955-2.418 2.157-2.418 1.21 0 2.176 1.095 2.156 2.419 0 1.333-.946 2.419-2.156 2.419Z"/>
                 </svg>
                 """);

    /// <inheritdoc />
    /// <remarks>
    /// Maps <c>GET /users/@me</c>. Discord sends the avatar as a bare hash, so the CDN URL is
    /// assembled here rather than stored half-formed.
    /// </remarks>
    protected override ExternalIdentity MapIdentity(JsonElement user)
    {
        var subject = RequireSubject(user, "id");

        // global_name is the current display name; username is the legacy handle and is what
        // remains for accounts that never set one.
        var display = StringOrNull(user, "global_name") ?? StringOrNull(user, "username");

        var assurances = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [ExternalIdentity.MfaEnabled] = BoolOrFalse(user, "mfa_enabled"),

            // Discord has no notion of a verified game character. Recorded explicitly as false
            // so a deployment that requires it gets a clean refusal instead of a missing key.
            [ExternalIdentity.VerifiedCharacters] = false,
        };

        LogDebug("Signed in {Subject}", subject);

        return new ExternalIdentity(ProviderId, subject, display, AvatarUrl(subject, user), assurances);
    }

    private static string? AvatarUrl(string subject, JsonElement user)
    {
        var hash = StringOrNull(user, "avatar");
        if (string.IsNullOrEmpty(hash)) return null;

        // Animated avatars carry an "a_" prefix and are served as .gif; everything else as .png.
        var extension = hash.StartsWith("a_", StringComparison.Ordinal) ? "gif" : "png";
        return $"https://cdn.discordapp.com/avatars/{subject}/{hash}.{extension}";
    }
}
