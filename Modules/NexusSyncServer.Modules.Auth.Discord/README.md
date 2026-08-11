# NexusSyncServer.Modules.Auth.Discord

Sign-in via Discord.

Compose it after `AuthModule`. It registers nothing unless enabled.

## What it is good for, and what it is not

Familiar to everyone in the ecosystem — that is its whole advantage, and it is a real one.

What it cannot tell you: **a Discord account says nothing about whether the person plays the
game.** Anyone can make one in a minute, which makes it a weak gate against someone determined
to create identities. If that matters, compose
[`NexusSyncServer.Modules.Auth.XivAuth`](../NexusSyncServer.Modules.Auth.XivAuth/README.md) alongside it —
both can be enabled, and the user picks.

This provider records `verified_characters: false` explicitly rather than omitting it, so a
deployment that requires a verified character gets a clean refusal instead of a missing key.

## Public API

| Type | File | Purpose |
|---|---|---|
| `DiscordAuthModule` | `DiscordAuthModule.cs` | `IServerModule`. Registers the provider when enabled. |
| `DiscordIdentityProvider` | `DiscordIdentityProvider.cs` | The provider. Maps `GET /users/@me`. |
| `DiscordOptions` | `DiscordOptions.cs` | Credentials and endpoints (pre-filled). |

## Configuration

```jsonc
{
  "Auth": {
    "Discord": {
      "Enabled": true,
      "ClientId": "…",
      "ClientSecret": "…"
    }
  }
}
```

| | |
|---|---|
| Authorization | `https://discord.com/oauth2/authorize` |
| Token | `https://discord.com/api/v10/oauth2/token` |
| User | `https://discord.com/api/v10/users/@me` |

In the Discord developer portal, add the redirect
`https://your-server/account/signin/discord/callback`.

## Scopes

Only `identify`. The `email` scope is deliberately not requested — this server has no use for
an address, and not asking is the simplest way to never hold one.

## What it reads

`GET /users/@me` gives `id`, `username`, `global_name`, `avatar` and `mfa_enabled`.

`global_name` is the current display name; `username` is the legacy handle and is what remains
for accounts that never set one. The avatar arrives as a bare hash, so the CDN URL is assembled
here rather than stored half-formed — including the `a_` prefix that means an animated avatar
served as `.gif`.

## Guild restriction is not implemented

`RequiredGuildId` exists in the options and **throws at startup if set**.

Deliberate: an operator who sets it believes sign-in is restricted to their server's members.
Silently ignoring it would be the worst possible outcome. Implementing it needs the `guilds`
scope and a membership lookup, neither of which is here yet — so it fails loudly instead.

## License

**AGPL-3.0-only.**
