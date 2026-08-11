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

The published image carries **no contracts**. That is what lets one image serve every author:
you supply yours through a volume, and the server registers them at startup.

### 1. A contract

Write a `*.json` document describing your datasets, or start from
[`samples/example.showcase.json`](samples/). The format and its rules are in
[`contracts/README.md`](contracts/README.md).

```bash
mkdir -p contracts
cp samples/example.showcase.json contracts/
```

### 2. A compose file

```yaml
services:
  db:
    image: mariadb:11.4
    restart: unless-stopped
    environment:
      MARIADB_DATABASE: nexussyncserver
      MARIADB_USER: nexussyncserver
      MARIADB_PASSWORD: ${DB_PASSWORD:?set it in .env}
      MARIADB_ROOT_PASSWORD: ${DB_ROOT_PASSWORD:?set it in .env}
    volumes:
      - mariadb-data:/var/lib/mysql
    healthcheck:
      test: ["CMD", "healthcheck.sh", "--connect", "--innodb_initialized"]
      interval: 5s
      timeout: 3s
      retries: 10

  server:
    image: ghcr.io/nexusffxiv/nexussyncserver:latest
    restart: unless-stopped
    depends_on:
      db:
        # Not just "started": without this the server races the database on a cold
        # start and dies on its first migration.
        condition: service_healthy
    environment:
      Storage__ConnectionString: "Server=db;Port=3306;Database=nexussyncserver;User Id=nexussyncserver;Password=${DB_PASSWORD};"

      # Sign-in is off unless configured. A server with neither provider still works —
      # keys are then issued by the operator with --issue-key.
      Auth__XivAuth__Enabled: "true"
      Auth__XivAuth__ClientId: "${XIVAUTH_CLIENT_ID}"
      Auth__XivAuth__ClientSecret: "${XIVAUTH_CLIENT_SECRET}"
    ports:
      - "8080:8080"
    volumes:
      # Your contracts. Read-only: the server never writes here, and a writable mount
      # would let a bug edit the definitions it is being held to.
      - ./contracts:/contracts:ro

      # Data-protection keys, which sign the sign-in cookie and every form token.
      # Without this volume they live inside the container, so recreating it signs
      # everyone out and rejects any page that was already open.
      - dataprotection-keys:/home/nexussyncserver/.aspnet/DataProtection-Keys

volumes:
  mariadb-data:
  dataprotection-keys:
```

Register the callback `https://your-host/account/signin/xivauth/callback` with the provider —
the server derives it from the incoming request, so it must match the address people actually
use.

### 3. Up

```bash
docker compose up -d
docker compose logs server | grep Registered
```

### 4. A key

Sign in at `http://localhost:8080`, then create one under **API keys** — pick the permissions
from the list, which is built from your contracts. Paste it into the plugin's settings.

Without a sign-in provider, issue one from the command line instead:

```bash
docker compose exec server /app/NexusSyncServer --issue-key     --scopes example.showcase/observations:push     --label "my plugin"
```

### Adding a contract later

Drop the file into `contracts/` and restart the server. Registration is idempotent, so existing
documents re-register to no effect and nothing else is disturbed.

```bash
cp acme.myplugin.json contracts/ && docker compose restart server
```

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
