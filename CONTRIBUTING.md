# Contributing to NexusSyncServer

Thanks for considering a contribution. This doc covers the workflow; the release process lives
in [RELEASING.md](RELEASING.md).

**Read this first:** unlike the other repos in this org, what you change here runs as a network
service holding other people's data and issuing credentials. Most of the extra rules below
exist for that reason and for no other.

## Branch & PR workflow

All changes go through a Pull Request. Direct pushes to `main` are blocked by branch protection.

1. **Branch off `main`**:
   ```powershell
   git checkout main; git pull
   git checkout -b <scope>/<short-summary>
   ```
   Suggested scopes: `feat/`, `fix/`, `chore/`, `docs/`, `refactor/`, `test/`.

2. **Commit** with clear imperative messages:
   ```
   fix(Storage): honour the collection retention on prune
   ```

3. **Push and open a PR**:
   ```powershell
   git push -u origin <branch>
   gh pr create
   ```

4. **CI must be green** — `build`, `audit` and `image`. The image is built on PRs too (without
   pushing), so a broken Dockerfile fails in review rather than at release time.

## The four things a reviewer will check

The PR template asks about these explicitly. They are not box-ticking:

**Wire protocol.** Endpoints, envelopes and the contract model live in `NexusKit.Sync`, not
here. Changing them affects every client, including implementations nobody here wrote. If a PR
needs a wire change, that lands in NexusKit first.

**Validation may not get more permissive.** Every contract constraint is enforced server-side
on every write, and that is the whole reason a forged or outdated client cannot store something
the contract forbids. Loosening a check is a security change even when it looks like a bug fix.

**Scopes and key material.** No endpoint loses its scope check. No API key reaches a log line,
a response body, or an exception message — use `ApiKeyFormat.Redact` if a key has to appear at
all. Keys are stored as `SHA-256`; the plaintext exists exactly once, at creation.

**Migrations run at startup, so they are hard to undo.** Prefer additive changes. If a step is
destructive, say so in the PR and in the release notes — an operator's rollback is pulling the
previous image tag, and that does not un-drop a column.

## Local development

```powershell
docker compose up -d db          # MariaDB only
dotnet run --project NexusSyncServer
```

Or the whole thing:

```powershell
docker compose up --build
```

Smoke-test before opening a PR:

```powershell
dotnet build NexusSyncServer.sln -c Release
dotnet test  NexusSyncServer.sln -c Release
```

The workspace's `Directory.Build.targets` swaps `NexusKit.Sync` for a `ProjectReference`
against the sibling clone, so protocol edits are picked up immediately without a NuGet
round-trip.

## Writing a module

A module is a class implementing `IServerModule`, plus whichever seams it needs:
`IEndpointModule` for routes, `IEntityModule` and `IMigrationModule` for its own tables,
`IPortalPageModule` for admin pages built with `NexusSyncServer.Ux`.

`IEntityModule` describes **the module's own tables**. Contract-defined user data does not go
through it — that lives in the generic record store, which is what lets contracts be registered
at runtime without a migration.

Modules are composed at **build time**. There is no runtime plugin loading, and adding it is
not a wanted feature: foreign assemblies in a server process are an attack surface and an
operational problem. The flexibility users need comes from registrable contracts, which are
data.

## Code style

Follows the NexusFFXIV conventions — see
[NexusKit's `docs/coding-conventions.md`](https://github.com/NexusFFXIV/NexusKit/blob/main/docs/coding-conventions.md):

- `m`-prefix on private instance fields
- `ConfigureAwait(false)` on every background-I/O `await`
- File-scoped namespaces, one public type per file
- `Nullable` enabled — never `!` to silence the compiler unless proven non-null
- `TreatWarningsAsErrors` is on repo-wide; don't suppress, fix

## License

By contributing, you agree your contribution is licensed under **AGPL-3.0-only**. Note what that
means for a server: running a modified version as a network service obliges you to offer its
source to its users.
