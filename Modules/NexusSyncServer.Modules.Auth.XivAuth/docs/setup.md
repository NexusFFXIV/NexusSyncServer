# Setting up XIVAuth (NexusSyncServer.Modules.Auth.XivAuth)

From nothing to a working sign-in button.

## 1. Get through XIVAuth's onboarding

Developer access requires **multifactor authentication** on your XIVAuth account and
**ownership of at least one verified character**. That is deliberate on their side — it is what
keeps the provider free of spam applications — and it means the first step is verifying a
character of your own, not filling in a form.

Start at [xivauth.net](https://xivauth.net/) → Developer.

## 2. Register the application

You need one value from your deployment: the callback URL.

```
https://your-server.example/account/signin/xivauth/callback
```

The path is fixed — `/account/signin/{provider}/callback`, with `xivauth` as the provider id.
Only the scheme and host are yours.

For local development against a container, that is
`http://localhost:8080/account/signin/xivauth/callback`. Whether XIVAuth accepts a plain-HTTP
localhost redirect is their call; if it does not, put a TLS-terminating proxy in front even
locally.

Request the scopes this module uses and no more: **`user`** and **`refresh`**.

## 3. Configure the server

```bash
# .env
XIVAUTH_ENABLED=true
XIVAUTH_CLIENT_ID=…
XIVAUTH_CLIENT_SECRET=…
```

Restart. The button appears on `/account/signin` — the module registers nothing at all while
disabled, so it cannot show up half-configured.

## 4. Behind a reverse proxy, forward the scheme

The callback URL is derived from the incoming request. Without forwarded headers it becomes
your proxy's internal address and XIVAuth rejects the redirect as unregistered:

```csharp
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});
```

This is the single most common cause of "redirect_uri mismatch" here.

## 5. Make yourself an operator

Registering contracts needs an operator, and promoting an account needs an operator — so the
first one is seeded from configuration:

```bash
OPERATOR_IDENTITY=xivauth:<your XIVAuth user id>
```

Sign in once to find your id: it is the `subject` in the `account_identities` row, and it
appears in the log line when your account is created. Set it, restart, and your existing
account is promoted — you do not have to sign up again.

Remove the setting afterwards if you like; the flag lives in the database.

## Verifying it

| Check | Expected |
|---|---|
| `/account/signin` | A "Sign in with XIVAuth" button |
| Click it | Redirect to xivauth.net, then back to `/account/keys` |
| `/account/keys` | Your account, and a form to create a key |
| Server log | `Created account … from xivauth:…` |

## The verified-character gate

`RequireVerifiedCharacter` is **on by default**. A XIVAuth account without a linked, verified
character is refused with a redirect to `/account/signin?error=refused`, and the sign-in page
explains what to do about it.

This is the main reason to choose XIVAuth: it costs an attacker a real game account per
identity, which is a barrier no rate limit can put up. Turn it off only if you actually intend
to serve people who have not linked a character.

```jsonc
{ "Auth": { "XivAuth": { "RequireVerifiedCharacter": false } } }
```
