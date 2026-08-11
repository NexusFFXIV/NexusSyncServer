# Setting up Discord (NexusSyncServer.Modules.Auth.Discord)

From nothing to a working sign-in button.

## 1. Create the application

[discord.com/developers/applications](https://discord.com/developers/applications) → New
Application.

Under **OAuth2**, add a redirect:

```
https://your-server.example/account/signin/discord/callback
```

The path is fixed — `/account/signin/{provider}/callback`, with `discord` as the provider id.
Only the scheme and host are yours. Discord matches redirects exactly, including the trailing
slash and the scheme.

For local development: `http://localhost:8080/account/signin/discord/callback`. Discord does
allow plain-HTTP localhost redirects, which makes it the easier of the two providers to try
first.

Copy the **Client ID** and generate a **Client Secret**.

## 2. Configure the server

```bash
# .env
DISCORD_ENABLED=true
DISCORD_CLIENT_ID=…
DISCORD_CLIENT_SECRET=…
```

Restart. The button appears on `/account/signin` — the module registers nothing while disabled,
so it cannot show up half-configured.

No scopes need selecting in the portal. The module requests `identify` at authorization time,
and nothing else.

## 3. Behind a reverse proxy, forward the scheme

The callback URL is derived from the incoming request. Without forwarded headers it becomes
your proxy's internal address and Discord rejects it as an invalid redirect:

```csharp
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});
```

This is the single most common cause of "Invalid OAuth2 redirect_uri" here.

## 4. Make yourself an operator

```bash
OPERATOR_IDENTITY=discord:<your Discord user id>
```

Enable Developer Mode in Discord (Settings → Advanced) and right-click your own name → Copy
User ID. Set it, restart, and your existing account is promoted — signing up again is not
needed.

## Verifying it

| Check | Expected |
|---|---|
| `/account/signin` | A "Sign in with Discord" button |
| Click it | Discord's consent screen, then back to `/account/keys` |
| Server log | `Created account … from discord:…` |

## Know what this does not prove

A Discord account is a minute's work to create. This provider authenticates *someone*; it does
not establish that they play FINAL FANTASY XIV, or that they are not the same person as five
other accounts.

For a public server that matters, enable
[XIVAuth](../../NexusSyncServer.Modules.Auth.XivAuth/docs/setup.md) as well — both can run at once and
the user chooses. A deployment can then require a verified character while still offering the
button everyone recognises.

## Guild restriction

`Auth:Discord:RequiredGuildId` **throws at startup if set.** It is not implemented, and an
operator who sets it would reasonably believe sign-in is limited to their server's members.
Failing loudly beats pretending to enforce something.

Implementing it needs the `guilds` scope and a membership lookup on callback. Until then,
restrict access by not handing out keys rather than by not handing out logins.
