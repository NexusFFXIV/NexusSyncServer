# NexusSyncServer

The executable. Composes the modules, owns the HTML document, and is what the container runs.

**Not packable** — the reusable halves are `NexusSyncServer.Hosting`, `NexusSyncServer.Ux` and the modules.

## What is here

| File | Purpose |
|---|---|
| `Program.cs` | The composition root. The `AddModule` list **is** the deployment's module set. |
| `Components/App.razor` | The HTML document. Owned here because it is what an operator most often changes. |
| `Components/Routes.razor` | Router plus the default shell. Module assemblies are added by `UseNexusSyncServer`, so this never lists them. |
| `Components/ServerLayout.razor` | The default layout, built from `NexusPageLayout` and the composed modules' navigation. |
| `Components/Home.razor` | The landing page at `/`. |
| `HealthCheckCommand.cs` | `--healthcheck` — the container probes itself. |
| `IssueKeyCommand.cs` | `--issue-key` — mints an API key from the command line. |

## Running it

```bash
docker compose up            # server plus MariaDB
dotnet run                   # against a database you provide
```

It refuses to start without `Storage__ConnectionString`, naming the missing setting. A server
that boots, passes its liveness probe and only then reveals it has no database is harder to
diagnose than one that will not start.

## Command-line modes

Both run against the same configuration and container as the server, then exit.

```bash
# Mint a key — see Modules/NexusSyncServer.Modules.Auth/docs/api-keys.md
docker compose exec server /app/NexusSyncServer --issue-key \
    --scopes observations:push --contract example.showcase --label "my plugin"

# What the container's HEALTHCHECK runs
/app/NexusSyncServer --healthcheck
```

`--issue-key` runs migrations first, so it works against an empty database — which is exactly
the situation someone bootstrapping a deployment is in.

`--healthcheck` exists because the aspnet base image ships neither curl nor wget, and adding
one to probe your own process is a package and a CVE surface for something the process can
already do.

> On Windows with Git Bash, prefix container commands with `MSYS_NO_PATHCONV=1` — otherwise
> `/app/NexusSyncServer` is rewritten into a Windows path before Docker sees it.

## Changing the composition

`Program.cs` is the list. Add a module, rebuild the image:

```csharp
builder.AddNexusSyncServer(hub => hub
    .AddModule<StorageMariaDbModule>()   // first: owns the DbContext
    .AddModule<RegistryModule>()
    .AddModule<AuthModule>()
    .AddModule<XivAuthModule>()           // inert unless enabled in config
    .AddModule<DiscordAuthModule>()
    .AddModule<AcmeWidgetModule>()        // yours
    .AddModule<ApiModule>());             // last: resolves the others
```

Composition is static rather than discovered at runtime. Foreign code in a server process is an
attack surface and an operational problem; the flexibility users need comes from registrable
contracts, which are data. Building your own image is two lines of Dockerfile.

For an API-only deployment, use the non-generic `app.UseNexusSyncServer()` and drop the components.

## Configuration

Everything binds from configuration, so environment variables work with `__` as the separator.
See [`.env.example`](../.env.example) for the full set with defaults.

| Section | Required | What |
|---|---|---|
| `Storage` | yes | Connection string, batch and payload limits |
| `Registry` | no | Contract directory |
| `Auth` | no | Operator seeding, bootstrap key, rate limit |
| `Auth:XivAuth`, `Auth:Discord` | no | Sign-in providers, off unless enabled |

## Further reading

| Document | What it covers |
|---|---|
| [docs/deployment.md](docs/deployment.md) | Running it for real: reverse proxy, backups, upgrades |

## License

**AGPL-3.0-only.**
