using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace NexusSyncServer.Modules.Auth.Providers;

/// <summary>
/// The authorization-code flow, implemented once.
/// <para>Every provider worth supporting speaks the same OAuth2 dance; what differs is the
/// endpoints, the scope names and the shape of the user payload. A plugin therefore supplies
/// its options and a mapping function, and inherits the rest — which is what keeps adding a
/// provider to roughly one small file.</para>
/// </summary>
public abstract class OAuth2IdentityProvider : IIdentityProvider
{
    private readonly HttpClient mHttp;
    private readonly OAuth2ProviderOptions mOptions;
    private readonly ILogger mLog;

    /// <summary>Creates the provider.</summary>
    protected OAuth2IdentityProvider(HttpClient http, OAuth2ProviderOptions options, ILogger log)
    {
        mHttp = http;
        mOptions = options;
        mLog = log;
    }

    /// <inheritdoc />
    public abstract string Id { get; }

    /// <inheritdoc />
    public abstract string DisplayName { get; }

    /// <summary>
    /// How this provider's sign-in button looks. Plain by default.
    /// <para>Declared here as well as on the interface: a default interface member cannot be
    /// reached with <c>override</c> from a derived class, so without this every provider that
    /// wanted branding would have to re-implement the interface member and shadow it.</para>
    /// </summary>
    public virtual ProviderBranding Branding => ProviderBranding.Default;

    /// <summary>The options this provider was configured with.</summary>
    protected OAuth2ProviderOptions Options => mOptions;

    /// <summary>Turns the provider's user payload into an identity.</summary>
    protected abstract ExternalIdentity MapIdentity(JsonElement user);

    /// <inheritdoc />
    public Uri BuildAuthorizationUrl(string state, string redirectUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(redirectUri);

        var query = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["client_id"] = mOptions.ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = string.Join(' ', mOptions.Scopes),
            ["state"] = state,
        };

        var builder = new UriBuilder(mOptions.AuthorizationEndpoint!)
        {
            Query = string.Join('&', query
                .Where(kv => !string.IsNullOrEmpty(kv.Value))
                .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}")),
        };

        return builder.Uri;
    }

    /// <inheritdoc />
    public async Task<ExternalIdentity> ExchangeAsync(string code, string redirectUri, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var accessToken = await RedeemCodeAsync(code, redirectUri, ct).ConfigureAwait(false);
        var user = await ReadUserAsync(accessToken, ct).ConfigureAwait(false);

        var identity = MapIdentity(user);

        foreach (var required in mOptions.RequiredAssurances)
        {
            if (identity.Asserts(required)) continue;

            // Refused at the door rather than after an account exists. Creating the account
            // first and then blocking it would leave rows behind for every attempt.
            throw new IdentityProviderException(
                Id, $"Sign-in requires '{required}', which this account does not have.");
        }

        return identity;
    }

    private async Task<string> RedeemCodeAsync(string code, string redirectUri, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, mOptions.TokenEndpoint);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = mOptions.ClientId!,
            ["client_secret"] = mOptions.ClientSecret!,
        });

        using var response = await mHttp.SendAsync(request, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            // Body deliberately not logged or surfaced: a failed token exchange can echo the
            // code back, and on some providers the client_id alongside it.
            throw new IdentityProviderException(
                Id, $"Token exchange failed with HTTP {(int)response.StatusCode}.");
        }

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct).ConfigureAwait(false);

        if (!payload.TryGetProperty("access_token", out var token) || token.GetString() is not { Length: > 0 } value)
            throw new IdentityProviderException(Id, "Token response carried no access_token.");

        return value;
    }

    private async Task<JsonElement> ReadUserAsync(string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, mOptions.UserEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await mHttp.SendAsync(request, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new IdentityProviderException(
                Id, $"User endpoint returned HTTP {(int)response.StatusCode}.");
        }

        try
        {
            return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new IdentityProviderException(Id, "User endpoint did not return JSON.", ex);
        }
    }

    /// <summary>Reads a string property, or null when absent.</summary>
    protected static string? StringOrNull(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Reads a required identifier, accepting either a JSON string or number.</summary>
    /// <exception cref="IdentityProviderException">The property is missing or unusable.</exception>
    protected string RequireSubject(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out var value))
        {
            // Providers disagree on whether an id is a string or a number, and a snowflake sent
            // as a number is already past what JSON parsers handle exactly — so it is read as
            // raw text either way rather than through a numeric type.
            var text = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null,
            };

            if (!string.IsNullOrEmpty(text)) return text;
        }

        throw new IdentityProviderException(Id, $"User payload carried no usable '{property}'.");
    }

    /// <summary>Reads a boolean assurance, defaulting to false when the provider omits it.</summary>
    protected static bool BoolOrFalse(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    /// <summary>Logs at debug with the provider prefix.</summary>
    protected void LogDebug(string message, params object?[] args) =>
        mLog.LogDebug($"[{Id}] {message}", args);

    /// <summary>Formats an invariant string, for building URLs in derived providers.</summary>
    protected static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
