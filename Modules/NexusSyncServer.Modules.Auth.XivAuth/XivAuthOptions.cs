using NexusSyncServer.Modules.Auth.Providers;

namespace NexusSyncServer.Modules.Auth.XivAuth;

/// <summary>
/// XIVAuth configuration, bound from the <c>Auth:XivAuth</c> configuration section.
/// <para>Endpoints are pre-filled with the public instance and only need overriding for a
/// self-hosted XIVAuth. An operator normally supplies nothing but the client credentials.</para>
/// </summary>
public sealed class XivAuthOptions : OAuth2ProviderOptions
{
    /// <summary>Configuration section this binds from.</summary>
    public const string SectionName = "Auth:XivAuth";

    /// <summary>Creates the options with the public instance's endpoints and a minimal scope set.</summary>
    public XivAuthOptions()
    {
        AuthorizationEndpoint = new Uri("https://xivauth.net/oauth/authorize");
        TokenEndpoint = new Uri("https://xivauth.net/oauth/token");
        UserEndpoint = new Uri("https://xivauth.net/api/v1/user");

        // The narrowest set that works. `user` is what the user endpoint requires; `refresh`
        // is what XIVAuth needs to issue a refreshable token at all. Deliberately no
        // `user:email` — this server has no use for an email address, and not requesting one
        // means never having to store or protect it.
        Scopes.Add("user");
        Scopes.Add("refresh");
    }

    /// <summary>
    /// Require a verified FINAL FANTASY XIV character before an account may sign in.
    /// <para>Defaults to <b>true</b>, and this is the main reason to prefer XIVAuth over a
    /// generic social login: an attacker needs a real game account per identity, which is a
    /// cost no rate limit can impose. Turn it off only if you genuinely want to serve people
    /// who have not linked a character.</para>
    /// </summary>
    public bool RequireVerifiedCharacter { get; set; } = true;

    /// <inheritdoc />
    public override void Validate(string providerId)
    {
        if (RequireVerifiedCharacter && !RequiredAssurances.Contains(ExternalIdentity.VerifiedCharacters))
            RequiredAssurances.Add(ExternalIdentity.VerifiedCharacters);

        base.Validate(providerId);
    }
}
