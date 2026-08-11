using System.Text.Json;
using Microsoft.Extensions.Logging;
using NexusSyncServer.Modules.Auth.Providers;

namespace NexusSyncServer.Modules.Auth.XivAuth;

/// <summary>
/// Sign-in via <see href="https://xivauth.net/">XIVAuth</see>.
/// <para>XIVAuth is the FFXIV community's identity provider — "the last Lodestone code you'll
/// ever need". It runs Doorkeeper, so the flow is ordinary OAuth2; what makes it worth
/// preferring here is that it can assert the user owns a verified in-game character.</para>
/// </summary>
public sealed class XivAuthIdentityProvider : OAuth2IdentityProvider
{
    /// <summary>The provider id used in URLs, configuration and the identity table.</summary>
    public const string ProviderId = "xivauth";

    /// <summary>Creates the provider.</summary>
    public XivAuthIdentityProvider(HttpClient http, XivAuthOptions options, ILogger<XivAuthIdentityProvider> log)
        : base(http, options, log)
    {
    }

    /// <inheritdoc />
    public override string Id => ProviderId;

    /// <inheritdoc />
    public override string DisplayName => "XIVAuth";

    /// <summary>
    /// A dark blue and a verified-shield glyph.
    /// <para><b>Chosen here, not taken from XIVAuth.</b> Their brand assets are not bundled
    /// with this module and guessing a wordmark would be worse than not showing one, so the
    /// mark says what the provider <i>does</i> — proves a character belongs to you — rather
    /// than pretending to be their logo. Replace both if you have the real palette.</para>
    /// </summary>
    public override ProviderBranding Branding { get; } = new(
        Accent: "#1f3a5f",
        OnAccent: "#ffffff",
        IconSvg: """
                 <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor"
                      stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                   <path d="M12 2 4 5.5v6c0 4.6 3.2 8.9 8 10.5 4.8-1.6 8-5.9 8-10.5v-6L12 2Z"/>
                   <path d="m8.5 11.8 2.4 2.4 4.6-4.6"/>
                 </svg>
                 """);

    /// <inheritdoc />
    /// <remarks>
    /// Maps <c>GET /api/v1/user</c>, which returns <c>id</c>, <c>display_name</c>,
    /// <c>avatar_url</c>, <c>mfa_enabled</c> and <c>verified_characters</c>. Fields behind
    /// scopes we do not request — notably <c>email</c> — are simply absent, which is the
    /// intended outcome.
    /// </remarks>
    protected override ExternalIdentity MapIdentity(JsonElement user)
    {
        var subject = RequireSubject(user, "id");

        var assurances = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [ExternalIdentity.VerifiedCharacters] = BoolOrFalse(user, "verified_characters"),
            [ExternalIdentity.MfaEnabled] = BoolOrFalse(user, "mfa_enabled"),
        };

        LogDebug("Signed in {Subject} (verified characters: {Verified})",
            subject, assurances[ExternalIdentity.VerifiedCharacters]);

        return new ExternalIdentity(
            ProviderId,
            subject,
            StringOrNull(user, "display_name"),
            StringOrNull(user, "avatar_url"),
            assurances);
    }
}
