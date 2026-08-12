using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexusSyncServer.Hosting.Catalog;
using NexusSyncServer.Hosting.Modules;
using NexusSyncServer.Hosting.Persistence;

namespace NexusSyncServer.Modules.Auth;

/// <summary>
/// Accounts, API keys and scope enforcement — the provider-agnostic half of authentication.
/// <para>Sign-in itself lives in provider plugins. Compose in
/// <c>NexusSyncServer.Modules.Auth.XivAuth</c>, <c>NexusSyncServer.Modules.Auth.Discord</c>, or both; when
/// more than one is registered the portal offers the choice rather than deciding for the
/// user. Composing none is valid too — keys can then only be issued by an operator out of
/// band, which is a reasonable posture for a server with a handful of known users.</para>
/// </summary>
public sealed class AuthModule : IServerModule, IPortalPageModule
{
    /// <inheritdoc />
    public string Id => "nexussyncserver.auth";

    /// <inheritdoc />
    public Assembly ComponentAssembly => typeof(AuthModule).Assembly;

    /// <inheritdoc />
    /// <remarks>
    /// Two default mountings of the components this module also exposes on their own. An
    /// operator who builds their own interface embeds <c>NexusSignIn</c> and
    /// <c>NexusApiKeyManager</c> instead and can ignore these entirely.
    /// </remarks>
    public IEnumerable<PortalPage> Pages =>
    [
        new PortalPage("/account/keys", "API keys", Order: 10),
    ];

    /// <summary>
    /// Drops the session and clears the cookie, so the browser stops presenting it.
    /// <para>Rejecting alone would leave the cookie in place and repeat this lookup on every
    /// request for as long as it lives.</para>
    /// </summary>
    private static async Task Reject(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext
            .SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Register(IServiceCollection services, IServerContext context)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(context);

        var options = new AuthOptions();
        context.Configuration.GetSection(AuthOptions.SectionName).Bind(options);
        options.Validate();

        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(options));

        services.AddSingleton<IEntityModule, AuthEntityModule>();

        // The mapping above covers a database being created; this covers one that already exists.
        // A new table needs both, and neither substitutes for the other.
        services.AddSingleton<IMigrationModule, AuthMigrations>();

        services.AddScoped<IKeyContractStateWriter, KeyContractStateWriter>();

        // Offered to whoever wants to show who is still on which version — today the registry's
        // contracts page. Registered here rather than there so the dependency points one way: the
        // module that owns the table provides, the module that renders consumes an interface from
        // Hosting and never learns this module exists.
        services.AddSingleton<IClientVersionReport, KeyContractStateReport>();

        services.AddScoped<IApiKeyIssuer, ApiKeyIssuer>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddSingleton<IEndpointModule, SignInEndpoints>();

        // Cookie sessions for the browser side. Separate from API-key auth on purpose: a
        // browser session and a machine credential have different lifetimes, different
        // revocation stories and different exposure, and conflating them would mean a leaked
        // cookie granting API access or a revoked key logging someone out mid-page.
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "nexussyncserver_session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.LoginPath = SignInEndpoints.SignInPath;
                options.ExpireTimeSpan = TimeSpan.FromDays(14);
                options.SlidingExpiration = true;

                // A sign-in cookie is self-contained: once issued, nothing about it consults
                // the database again. Left alone, an account that is deleted or disabled keeps
                // browsing as though nothing happened, and only discovers otherwise when some
                // later action fails for reasons that look unrelated.
                //
                // That is also what would quietly defeat disabling an account: the flag would
                // be set and the person would carry on until their cookie happened to expire.
                // Checking here makes the next request the last one.
                options.Events.OnValidatePrincipal = async context =>
                {
                    var id = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                    if (!Guid.TryParse(id, out var accountId))
                    {
                        await Reject(context).ConfigureAwait(false);
                        return;
                    }

                    var accounts = context.HttpContext.RequestServices.GetRequiredService<IAccountService>();
                    var account = await accounts
                        .FindAsync(accountId, context.HttpContext.RequestAborted)
                        .ConfigureAwait(false);

                    // Gone or disabled — the same outcome either way, because "your account no
                    // longer exists" and "your account is switched off" both mean not signed in.
                    if (account is null || account.DisabledAt is not null)
                        await Reject(context).ConfigureAwait(false);
                };
            });

        services.AddAuthorization();

        // Named, rather than the framework default of ".AspNetCore.Antiforgery.<hash>".
        //
        // Two reasons, and the second is the one that hurt. A deterministic name matches the
        // session cookie above and is greppable in a browser's cookie list. And a cookie whose
        // name never changes is a cookie that can be *poisoned*: if the data-protection keys
        // are ever lost — which they are on any deployment that keeps them inside the
        // container — every browser holding an old cookie keeps presenting a token that cannot
        // be decrypted. The server answers each attempt with a bare 400, mints a replacement,
        // and the stale one can still win. Renaming orphans all of them at once, which is the
        // only recovery that does not require every user to clear cookies by hand.
        services.AddAntiforgery(options =>
        {
            options.Cookie.Name = "nexussyncserver_antiforgery";
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });

        // Singleton because the validation cache and the rate windows are the point; a scoped
        // authenticator would rebuild both per request and enforce nothing.
        services.AddSingleton<IApiKeyAuthenticator, ApiKeyAuthenticator>();

        // Holds a newly issued key across the redirect that follows issuing it. Singleton
        // because the POST and the GET after it are different requests.
        services.AddSingleton<RevealStore>();

        services.AddHostedService<AuthStartupService>();
    }
}
