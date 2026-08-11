namespace NexusSyncServer.Modules.Auth.Providers;

/// <summary>
/// Configuration common to every OAuth2 sign-in provider.
/// <para>Each provider plugin binds its own instance from its own configuration section and
/// fills in the endpoints; an operator only ever supplies the client credentials.</para>
/// </summary>
public class OAuth2ProviderOptions
{
    /// <summary>Whether this provider is offered at all. A provider composed in but disabled simply does not appear.</summary>
    public bool Enabled { get; set; }

    /// <summary>OAuth2 client id, from registering an application with the provider.</summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// OAuth2 client secret. Supply it through the environment or a secret store — never in a
    /// committed appsettings file.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>Authorization endpoint the browser is sent to.</summary>
    public Uri? AuthorizationEndpoint { get; set; }

    /// <summary>Token endpoint the authorization code is exchanged at.</summary>
    public Uri? TokenEndpoint { get; set; }

    /// <summary>Endpoint returning the signed-in user.</summary>
    public Uri? UserEndpoint { get; set; }

    /// <summary>Scopes requested at authorization.</summary>
    public IList<string> Scopes { get; } = [];

    /// <summary>
    /// Assurances the provider must assert before an account may be created or signed in,
    /// e.g. <c>verified_characters</c>.
    /// <para>This is where a deployment decides how much friction it wants at the door.
    /// Requiring a verified FFXIV character is a far stronger anti-abuse measure than any rate
    /// limit, because it costs an attacker a real game account per identity.</para>
    /// </summary>
    public IList<string> RequiredAssurances { get; } = [];

    /// <summary>Throws when the provider cannot work with these settings.</summary>
    /// <exception cref="InvalidOperationException">A required value is missing.</exception>
    public virtual void Validate(string providerId)
    {
        if (!Enabled) return;   // a disabled provider needs no credentials

        if (string.IsNullOrWhiteSpace(ClientId))
            throw new InvalidOperationException($"Auth provider '{providerId}' is enabled but has no ClientId.");

        if (string.IsNullOrWhiteSpace(ClientSecret))
            throw new InvalidOperationException($"Auth provider '{providerId}' is enabled but has no ClientSecret.");

        foreach (var (name, value) in new[]
                 {
                     (nameof(AuthorizationEndpoint), AuthorizationEndpoint),
                     (nameof(TokenEndpoint), TokenEndpoint),
                     (nameof(UserEndpoint), UserEndpoint),
                 })
        {
            if (value is null)
                throw new InvalidOperationException($"Auth provider '{providerId}' has no {name}.");

            if (!string.Equals(value.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                // Tokens and a client secret cross these URLs. There is no localhost exception
                // here of the kind the plugin client has, because a sign-in provider is never
                // something you run on your own machine.
                throw new InvalidOperationException(
                    $"Auth provider '{providerId}' has a non-HTTPS {name} ('{value}').");
            }
        }
    }
}
