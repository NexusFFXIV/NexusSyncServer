using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using NexusSyncServer.Hosting.Modules;
using NexusSyncServer.Modules.Auth.Providers;

namespace NexusSyncServer.Modules.Auth;

/// <summary>
/// The browser side of signing in: redirect to a provider, handle its callback, issue a
/// session cookie.
/// <para>Plain endpoints rather than an ASP.NET authentication handler per provider. There is
/// one flow here, it is short, and having it in one readable place beats spreading it across a
/// handler, an options class and a callback path for each provider we ever add.</para>
/// </summary>
internal sealed class SignInEndpoints : IEndpointModule
{
    /// <summary>Cookie carrying the anti-forgery state between redirect and callback.</summary>
    private const string StateCookie = "nexussyncserver_oauth_state";

    /// <summary>Where the sign-in page lives.</summary>
    public const string SignInPath = "/account/signin";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet($"{SignInPath}/{{provider}}", StartAsync).AllowAnonymous();
        endpoints.MapGet($"{SignInPath}/{{provider}}/callback", CallbackAsync).AllowAnonymous();
        // Cast to Delegate on purpose. SignOutAsync returns Task<IResult>, and delegate return
        // covariance lets that bind as a RequestDelegate — which returns plain Task and throws
        // the IResult away, so the redirect after signing out would never be written. The cast
        // forces the route-handler overload that honours the return value.
        endpoints.MapPost("/account/signout", (Delegate)SignOutAsync);
    }

    private static IResult StartAsync(
        HttpContext http,
        string provider,
        string? returnUrl,
        IEnumerable<IIdentityProvider> providers,
        ILogger<SignInEndpoints> log)
    {
        var selected = providers.FirstOrDefault(p => string.Equals(p.Id, provider, StringComparison.Ordinal));
        if (selected is null) return Results.NotFound();

        // Stashed before leaving for the provider; read again in the callback.
        RememberReturnUrl(http, returnUrl);

        // 256 bits of state, held in a short-lived cookie rather than in session storage. The
        // callback compares against it; without that check the callback would accept an
        // authorization code obtained by somebody else entirely.
        var state = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

        http.Response.Cookies.Append(StateCookie, state, new CookieOptions
        {
            HttpOnly = true,
            Secure = http.Request.IsHttps,
            SameSite = SameSiteMode.Lax,   // Lax, not Strict: the callback is a cross-site GET
            IsEssential = true,
            MaxAge = TimeSpan.FromMinutes(10),
            Path = SignInPath,
        });

        var redirectUri = CallbackUri(http, selected.Id);
        log.LogDebug("Starting {Provider} sign-in, callback {Callback}", selected.Id, redirectUri);

        // AbsoluteUri, never ToString(). Uri.ToString() returns the *safe-unescaped* form, so a
        // percent-encoded space comes back out as a literal space — which lands a raw space in
        // the Location header and leaves it to the client whether to re-encode it. It only
        // shows up with more than one scope: 'identify' alone is unaffected, 'user refresh'
        // is not. AbsoluteUri keeps the escaping the query builder applied.
        return Results.Redirect(selected.BuildAuthorizationUrl(state, redirectUri).AbsoluteUri);
    }

    private static async Task<IResult> CallbackAsync(
        HttpContext http,
        string provider,
        string? code,
        string? state,
        string? error,
        IEnumerable<IIdentityProvider> providers,
        IAccountService accounts,
        ILogger<SignInEndpoints> log,
        CancellationToken ct)
    {
        var expected = http.Request.Cookies[StateCookie];
        http.Response.Cookies.Delete(StateCookie, new CookieOptions { Path = SignInPath });

        if (!string.IsNullOrEmpty(error))
        {
            // The user declined, or the provider refused. Not an error condition on our side.
            log.LogInformation("{Provider} sign-in was not completed: {Error}", provider, error);
            return Results.Redirect($"{SignInPath}?error=declined");
        }

        if (string.IsNullOrEmpty(state) || string.IsNullOrEmpty(expected)
            || !CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(state),
                System.Text.Encoding.UTF8.GetBytes(expected)))
        {
            log.LogWarning("{Provider} callback arrived with a bad or missing state value.", provider);
            return Results.Redirect($"{SignInPath}?error=state");
        }

        if (string.IsNullOrEmpty(code)) return Results.Redirect($"{SignInPath}?error=nocode");

        var selected = providers.FirstOrDefault(p => string.Equals(p.Id, provider, StringComparison.Ordinal));
        if (selected is null) return Results.NotFound();

        ExternalIdentity identity;
        try
        {
            identity = await selected.ExchangeAsync(code, CallbackUri(http, selected.Id), ct).ConfigureAwait(false);
        }
        catch (IdentityProviderException ex)
        {
            // Includes the "requires a verified character" refusal, which is a legitimate
            // outcome rather than a fault — hence a redirect with a reason, not a 500.
            log.LogInformation(ex, "{Provider} sign-in refused.", provider);
            return Results.Redirect($"{SignInPath}?error=refused");
        }

        var account = await accounts.ResolveAsync(identity, ct).ConfigureAwait(false);

        if (account.DisabledAt is not null)
        {
            log.LogWarning("Disabled account {Account} attempted to sign in.", account.Id);
            return Results.Redirect($"{SignInPath}?error=disabled");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, account.Id.ToString()),
            new(ClaimTypes.Name, account.DisplayName ?? identity.Subject),
        };

        if (account.IsOperator) claims.Add(new Claim(ClaimTypes.Role, "operator"));

        await http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
            new AuthenticationProperties { IsPersistent = true }).ConfigureAwait(false);

        return Results.Redirect(TakeReturnUrl(http) ?? DefaultReturnUrl);
    }

    /// <summary>Default landing place when nothing asked for a particular page.</summary>
    private const string DefaultReturnUrl = "/account/keys";

    private const string ReturnCookie = "nexussyncserver_return";

    /// <summary>
    /// Remembers where to go after signing in, for the length of the provider round trip.
    /// <para>A cookie rather than a query parameter threaded through the provider: the value
    /// has to survive a redirect to a third party and back, and the callback URL registered
    /// with that provider is fixed — it cannot carry a per-request tail.</para>
    /// </summary>
    private static void RememberReturnUrl(HttpContext http, string? returnUrl)
    {
        if (LocalPathOrNull(returnUrl) is not { } safe) return;

        http.Response.Cookies.Append(ReturnCookie, safe, new CookieOptions
        {
            HttpOnly = true,
            Secure = http.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            MaxAge = TimeSpan.FromMinutes(10),
            Path = SignInPath,
        });
    }

    /// <summary>Reads the remembered destination and clears it, so it is used exactly once.</summary>
    private static string? TakeReturnUrl(HttpContext http)
    {
        var value = http.Request.Cookies[ReturnCookie];
        http.Response.Cookies.Delete(ReturnCookie, new CookieOptions { Path = SignInPath });
        return LocalPathOrNull(value);
    }

    /// <summary>
    /// The value if it is a path on this server, otherwise null.
    /// <para>Checked on the way in <b>and</b> on the way out. This is the open-redirect guard:
    /// without it, <c>/account/keys?ReturnUrl=https://evil.example</c> would hand somebody a
    /// link that signs in against this server and lands on theirs, with this server's domain
    /// in the address bar for the whole flow.</para>
    /// <para>Protocol-relative forms are the ones worth naming: <c>//evil.example</c> and
    /// <c>/\evil.example</c> both start with a slash and both leave this host.</para>
    /// </summary>
    private static string? LocalPathOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Length < 2 || value[0] != '/') return null;
        if (value[1] == '/' || value[1] == '\\') return null;
        if (Uri.IsWellFormedUriString(value, UriKind.Absolute)) return null;

        return value;
    }

    private static async Task<IResult> SignOutAsync(HttpContext http)
    {
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
        return Results.Redirect(SignInPath);
    }

    /// <summary>
    /// The absolute callback URL, built from the incoming request.
    /// <para>Derived rather than configured so a deployment does not have to declare its own
    /// address twice. Behind a reverse proxy this needs <c>UseForwardedHeaders</c>, or the
    /// scheme and host will be the proxy's internal ones and the provider will reject the
    /// redirect — which is exactly the misconfiguration worth failing loudly on.</para>
    /// </summary>
    private static string CallbackUri(HttpContext http, string provider) =>
        $"{http.Request.Scheme}://{http.Request.Host}{SignInPath}/{provider}/callback";
}
