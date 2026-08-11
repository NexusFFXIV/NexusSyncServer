# NexusSyncServer.Modules.Auth.XivAuth

Sign-in via [XIVAuth](https://xivauth.net/) — the FFXIV community's identity provider, "the
last Lodestone code you'll ever need".

Compose it after `AuthModule`. It registers nothing unless enabled.

## Why prefer it over a social login

XIVAuth can assert that the user owns a **verified in-game character**. That is a far stronger
anti-abuse gate than any rate limit: an attacker needs a real FFXIV account per identity, which
is a cost no request budget can impose.

`RequireVerifiedCharacter` defaults to **true** for exactly that reason. Turn it off only if
you genuinely want to serve people who have not linked a character.

Discord cannot say anything about whether someone plays the game. Compose both if you want the
familiar button and the strong gate.

## Public API

| Type | File | Purpose |
|---|---|---|
| `XivAuthModule` | `XivAuthModule.cs` | `IServerModule`. Registers the provider when enabled. |
| `XivAuthIdentityProvider` | `XivAuthIdentityProvider.cs` | The provider. Maps `GET /api/v1/user`. |
| `XivAuthOptions` | `XivAuthOptions.cs` | Credentials, endpoints (pre-filled), the verified-character gate. |

## Configuration

```jsonc
{
  "Auth": {
    "XivAuth": {
      "Enabled": true,
      "ClientId": "…",
      "ClientSecret": "…",
      "RequireVerifiedCharacter": true
    }
  }
}
```

Endpoints default to the public instance and only need overriding for a self-hosted XIVAuth:

| | |
|---|---|
| Authorization | `https://xivauth.net/oauth/authorize` |
| Token | `https://xivauth.net/oauth/token` |
| User | `https://xivauth.net/api/v1/user` |

Register the callback with XIVAuth as `https://your-server/account/signin/xivauth/callback`.

## Scopes

Only `user` and `refresh` — the narrowest set that works. `user` is what the user endpoint
requires; `refresh` is what XIVAuth needs to issue a refreshable token at all.

Deliberately **no `user:email`**: this server has no use for an address, and not requesting one
means never having to store or protect it.

XIVAuth also offers `user:social`, `character`, `character:all`, `certificate*` and others.
None are needed here.

## What it reads

`GET /api/v1/user` returns `id`, `display_name`, `avatar_url`, `mfa_enabled` and
`verified_characters`. Fields behind scopes we do not request — notably `email` — are simply
absent, which is the intended outcome.

The last two become assurances a deployment can gate on.

## Getting credentials

XIVAuth's developer onboarding requires multifactor authentication and ownership of a verified
character, which is what keeps the provider itself free of spam applications. See their
developer docs.

## Implementation note

XIVAuth runs Doorkeeper, so the flow is ordinary OAuth2 and everything here is inherited from
`OAuth2IdentityProvider`. This project is three small files.

## License

**AGPL-3.0-only.**
