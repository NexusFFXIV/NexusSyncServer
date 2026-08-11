# NexusSyncServer.Modules.Auth

Accounts, API keys and scope enforcement — the provider-agnostic half of authentication.

Sign-in itself lives in **provider plugins**. Compose in
[`NexusSyncServer.Modules.Auth.XivAuth`](../NexusSyncServer.Modules.Auth.XivAuth/README.md),
[`NexusSyncServer.Modules.Auth.Discord`](../NexusSyncServer.Modules.Auth.Discord/README.md), or both; when
more than one is registered the user picks. Composing none is valid too — keys can then only
be issued out of band, which is a reasonable posture for a server with a handful of known
users.

**→ [How to get a key](docs/api-keys.md)** is probably what you are looking for.

## Public API

| Type | File | Purpose |
|---|---|---|
| `AuthModule` | `AuthModule.cs` | `IServerModule`. Registers the tables, the issuer, the authenticator and the startup seeding. |
| `IApiKeyIssuer`, `IssuedApiKey` | `IApiKeyIssuer.cs` | Create, revoke, list. The plaintext is returned exactly once and never stored. |
| `IApiKeyAuthenticator`, `AuthResult`, `AuthFailure` | `IApiKeyAuthenticator.cs` | Validates a presented key and counts it against the rate limit. |
| `AuthenticatedCaller` | `AuthenticatedCaller.cs` | Who is making the current request: account, key prefix, contract restriction, scopes, operator flag. |
| `ApiKeySecret` | `ApiKeySecret.cs` | Generation, hashing, the indexed lookup prefix, constant-time comparison. |
| `AccountEntity`, `AccountIdentityEntity`, `ApiKeyEntity` | `*.cs` | The stored shape. One account, many linked identities, many keys. |
| `AuthOptions` | `AuthOptions.cs` | Operator seeding, bootstrap secret, rate limit, validation cache lifetime. |
| `IIdentityProvider`, `ExternalIdentity` | `Providers/` | The seam a sign-in plugin implements. |
| `OAuth2IdentityProvider`, `OAuth2ProviderOptions` | `Providers/` | The authorization-code flow, implemented once so a plugin is roughly one file. |

## Registration

```csharp
builder.AddNexusSyncServer(hub => hub
    .AddModule<StorageMariaDbModule>()
    .AddModule<AuthModule>()
    .AddModule<XivAuthModule>()     // optional, config-gated
    .AddModule<DiscordAuthModule>() // optional, config-gated
    .AddModule<ApiModule>());
```

## Configuration

```jsonc
{
  "Auth": {
    "RequestsPerMinute": 300,
    "ValidationCacheLifetime": "00:00:30",
    "OperatorIdentities": [ "xivauth:12345" ],

    // Optional unattended provisioning — a mounted secret, never an env var.
    "BootstrapKeyFile": "/run/secrets/bootstrap_key",
    "BootstrapKeyScopes": [ "reference_items:pull" ],
    "BootstrapKeyContract": "example.showcase"
  }
}
```

## What the design commits to

**Keys are stored as `SHA-256` and nothing else.** A database dump cannot be replayed as
credentials, and an operator with full database access still cannot impersonate a user. The
cost is that a key cannot be shown twice — rotate instead of recovering.

**No passwords and no email addresses.** Sign-in is delegated, and the `email` scope is
deliberately not requested from either provider. A credential this server never holds cannot
leak from it.

**One account, many identities.** `account_identities` is keyed on `(provider, subject)`, so
someone who signs in with Discord today and XIVAuth tomorrow is the same account holding the
same keys.

**Scopes are derived from the contract**, never hand-maintained — a scope list that drifts from
the collections it guards grants access nobody remembers approving. A key may carry fewer
scopes than the contract implies; it can never carry more.

## Known limitation

The rate limiter is in-memory and therefore **per instance**. Two replicas each allow the
configured budget, so the effective limit is the budget times the replica count. Acceptable for
what it is — a guard against a runaway client, not a billing meter — and it belongs in Redis as
soon as anything runs more than one instance.

## Scopes

Two kinds, and they behave differently.

**Contract scopes** are derived from a contract's collections — `observations:push`,
`reference_items:pull`. Stored **qualified** with the contract that declares them
(`example.showcase/observations:push`), because one key may span several and two contracts may
each declare a collection of the same name. Keys issued before qualification carry bare scopes;
those still work, but only on a key pinned to one contract, which is where they were
unambiguous to begin with.

**`contract:pull` is built in** and belongs to no contract. It gates `GET /v1/contracts` and
`GET /v1/contracts/{id}` — the index and the documents — and it is what lets a client follow
the server's version of a contract instead of its own. Being derived from nothing, it has to be
handled by hand wherever scopes are: the picker offers it separately, the issuer exempts it from
the `collection:verb` grammar, and `QualifiedScope.Grants` answers it before looking at any
contract.

The wire stays unqualified. A handshake answers with the bare scopes of the contract being
negotiated, because that is what a client compares against its own — it already knows which
contract it asked about.

## Key lifecycle

| | |
|---|---|
| Issue | Web form or `--issue-key`. Plaintext exists once, in the response that created it. |
| Renew | New secret **in the same row**. Everything else, including the remaining lifetime, stays. |
| Shorten | Bring an expiry forward, or set one where there was none. Never push it out. |
| Revoke | A timestamp, not a delete — the audit trail stays joinable. |
| Expire | Refused by the authenticator; no sweep, and the row survives. |

Revocation and shortening take effect within `Auth:ValidationCacheLifetime`, not instantly.
Browser sessions are different: they are checked against the database on every request, so a
disabled account is out on its next one.

## Further reading

| Document | What it covers |
|---|---|
| [docs/api-keys.md](docs/api-keys.md) | The three ways to obtain a key, and what the client does with it |
| [docs/providers.md](docs/providers.md) | Writing a sign-in provider plugin |

## License

**AGPL-3.0-only.**
