# Writing a sign-in provider (NexusSyncServer.Modules.Auth)

The seam, and what a plugin actually has to supply.

## The shape

```csharp
public interface IIdentityProvider
{
    string Id { get; }            // "xivauth" — goes in URLs and the identity table
    string DisplayName { get; }   // "XIVAuth" — goes on the button

    Uri BuildAuthorizationUrl(string state, string redirectUri);
    Task<ExternalIdentity> ExchangeAsync(string code, string redirectUri, CancellationToken ct);
}
```

Only the authorization-code flow is modelled. It is what a browser sign-in needs; the
alternatives a provider might also support — device code, client credentials — solve problems
this server does not have.

## You almost certainly want the base class

Every provider worth supporting speaks the same OAuth2 dance. What differs is the endpoints,
the scope names and the shape of the user payload. `OAuth2IdentityProvider` implements the
flow, so a plugin is options plus a mapping function:

```csharp
public sealed class AcmeIdentityProvider(
    HttpClient http, AcmeOptions options, ILogger<AcmeIdentityProvider> log)
    : OAuth2IdentityProvider(http, options, log)
{
    public const string ProviderId = "acme";

    public override string Id => ProviderId;
    public override string DisplayName => "Acme";

    protected override ExternalIdentity MapIdentity(JsonElement user) =>
        new(ProviderId,
            RequireSubject(user, "id"),
            StringOrNull(user, "name"),
            StringOrNull(user, "avatar"),
            new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                [ExternalIdentity.MfaEnabled] = BoolOrFalse(user, "mfa"),
            });
}
```

Plus options carrying the endpoints, and a module that registers it only when enabled.

## Assurances

`ExternalIdentity.Assurances` is an open set of flags a provider can assert — `verified_characters`,
`mfa_enabled`. A deployment gates on them through `RequiredAssurances`, and a sign-in missing
one is refused at the door rather than after an account exists.

It is an open dictionary rather than typed properties because what one provider can assert has
no counterpart in another. Forcing them into a shared shape would either invent fields
providers cannot fill or bake one provider's model into the seam.

**A provider that cannot assert something should say so explicitly.** Discord records
`verified_characters: false` rather than omitting it, so a deployment requiring it gets a clean
refusal instead of a missing key.

## Register only when enabled

```csharp
public void Register(IServiceCollection services, IServerContext context)
{
    var options = new AcmeOptions();
    context.Configuration.GetSection(AcmeOptions.SectionName).Bind(options);
    options.Validate(AcmeIdentityProvider.ProviderId);

    if (!options.Enabled) return;   // registers nothing at all

    services.AddSingleton(options);
    services.AddHttpClient<AcmeIdentityProvider>();
    services.AddSingleton<IIdentityProvider>(sp => new AcmeIdentityProvider(…));
}
```

Returning early rather than registering-and-filtering means a disabled provider cannot appear
on the sign-in page by accident.

`Validate` requires HTTPS on every endpoint, with no localhost exception of the kind the plugin
client has — a sign-in provider is never something you run on your own machine.

## What the flow does for you

`SignInEndpoints` handles the browser side once, for every provider:

- generates 256 bits of `state`, stores it in a short-lived cookie, and **compares it on the
  callback** — without that check the callback accepts an authorization code obtained elsewhere
- exchanges the code, reads the user, applies `RequiredAssurances`
- finds or creates the account and links the identity
- issues the session cookie

A provider never touches cookies, accounts or sessions.

## Errors

Throw `IdentityProviderException`. It carries the provider id, because "sign-in failed" with
two providers configured is not an actionable log line.

The endpoints turn it into a redirect with a reason rather than a 500 — a user declining, or an
account without a verified character, is a legitimate outcome and not a fault.

## Do not request scopes you do not need

Neither shipped provider asks for `email`. This server has no use for an address, and not
asking is the simplest way to never hold one — a credential or a datum you never store cannot
leak from you and is not something you have to account for.
