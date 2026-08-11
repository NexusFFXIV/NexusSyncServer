# NexusSyncServer

**The server half of the NexusKit sync stack.** Runs as a container, stores what plugins send,
serves what you curate.

The point of it: **a plugin author should not have to write server code to store and load
data.** You upload a contract — a JSON document declaring named datasets and their direction —
and the server provisions storage, endpoints, scopes and indexes from it. No rebuild, no
deployment, one published image for everyone.

## The picture

```
Plugin (FFXIV)                          NexusSyncServer (your server)
├─ NexusKit.Modules.Sync   ──HTTPS──►   ├─ Api        the four protocol operations
│  RestSyncProtocol                     ├─ Registry   contracts, versions, indexes
│                                       ├─ Storage    MariaDB, generic record store
└─ NexusKit.Sync   ────── the norm ─────┤─ Auth       Discord login, API keys, scopes
   contracts, ISyncProtocol             └─ Ux         component kit for your own pages
```

`NexusKit.Sync` is the norm both sides compile against — zero dependencies, no Dalamud
reference. This repo is the implementation on the server side.

## One server per author

There is no shared instance. Each author runs their own, which is why the whole quota,
tenancy and public-registration machinery a multi-tenant hub would need is simply absent —
along with its attack surface. One server serves **several contracts** (three plugins do not
need three servers), and a plugin can talk to **several servers**.

Contracts are registered by the **operator**, not by anyone who signs up.

## Layout

| Project | What it is |
|---|---|
| `NexusSyncServer.Hosting` | The module model — `IServerModule`, `IEndpointModule`, `IPortalPageModule`, the persistence seams, and the composition root. Third-party modules reference this. |
| `NexusSyncServer.Ux` | Blazor component kit: tables, forms, CRUD scaffolding, data access. What you build your own admin pages out of. |
| `Modules/NexusSyncServer.Modules.Api` | The four protocol endpoints. |
| `Modules/NexusSyncServer.Modules.Registry` | Contract registration, version compatibility, index creation. |
| `Modules/NexusSyncServer.Modules.Storage.MariaDb` | The generic record store, cursors, retention. |
| `Modules/NexusSyncServer.Modules.Auth` | Discord OAuth2, API-key issuance and validation, scopes, rate limits. |
| `NexusSyncServer` | The executable that composes the modules. What the container runs. |

**Modules are composed at build time, not dropped in at runtime.** Foreign assemblies in a
server process are an attack surface and an operational problem — version conflicts, partial
failures, unclear migration order. The flexibility users need comes from *registrable
contracts*, which are data; it does not need to come from loadable code. Building your own
image with your own modules is two lines of Dockerfile.

## Run it

```bash
docker compose up
```

That starts the server and a MariaDB instance. Then register a contract and issue a key —
see `docs/` once it exists, or `CONTRIBUTING.md` for the development loop.

## Security posture

- HTTPS is expected in front of it; the client refuses plain HTTP unless explicitly overridden for localhost
- API keys are stored as `SHA-256` only. The plaintext exists exactly once, at creation
- Every contract constraint is enforced **server-side** on every write — a forged or outdated client cannot write something the contract forbids
- Discord OAuth2 for login means no passwords and no email addresses are stored

What this does *not* claim: that only our client can write. With an open-source client that is
not achievable, and pretending otherwise would be worse than saying so. The guarantee is
**attribution and revocation** — every write hangs on an identity you can disable.

## License

**AGPL-3.0-only.** Derivative works and redistribution must remain open — including if you run
a modified version as a network service.
