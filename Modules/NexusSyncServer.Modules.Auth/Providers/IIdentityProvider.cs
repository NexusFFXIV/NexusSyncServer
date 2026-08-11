namespace NexusSyncServer.Modules.Auth.Providers;

/// <summary>
/// A way for a user to sign in.
/// <para>The seam that makes sign-in pluggable. An operator composes in whichever providers
/// they want — Discord, XIVAuth, both — and when more than one is registered the portal offers
/// the choice rather than deciding for the user.</para>
/// <para>Only the authorization-code flow is modelled. It is what a browser sign-in needs, and
/// the alternatives each provider might additionally support (device code, client credentials)
/// solve problems this server does not have.</para>
/// </summary>
public interface IIdentityProvider
{
    /// <summary>
    /// Stable id used in URLs, in the identity table and in configuration, e.g. <c>xivauth</c>.
    /// Lowercase, no spaces. Changing it orphans every linked identity, so it is chosen once.
    /// </summary>
    string Id { get; }

    /// <summary>Name shown on the sign-in button, e.g. <c>XIVAuth</c>.</summary>
    string DisplayName { get; }

    /// <summary>
    /// How this provider's sign-in button should look.
    /// <para>Defaulted, so an existing provider keeps compiling and simply renders as a plain
    /// button. Override it to make the option recognisable at a glance.</para>
    /// </summary>
    ProviderBranding Branding => ProviderBranding.Default;

    /// <summary>
    /// Builds the URL to send the browser to.
    /// </summary>
    /// <param name="state">
    /// Opaque anti-forgery value. The caller generates and stores it, and must reject a
    /// callback whose state does not match — without that check the callback endpoint accepts
    /// an authorization code an attacker obtained elsewhere.
    /// </param>
    /// <param name="redirectUri">Absolute callback URL, matching what was registered with the provider.</param>
    Uri BuildAuthorizationUrl(string state, string redirectUri);

    /// <summary>
    /// Exchanges the authorization code for tokens and reads the resulting identity.
    /// </summary>
    /// <exception cref="IdentityProviderException">The provider refused, or answered unusably.</exception>
    Task<ExternalIdentity> ExchangeAsync(string code, string redirectUri, CancellationToken ct);
}
