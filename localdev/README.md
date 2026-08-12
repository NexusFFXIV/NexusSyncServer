# localdev/

Everything needed to build the image against the **sibling `NexusKit.Sync` source** instead of the
published package. Only useful while developing; nothing here ships.

Grouped into one directory on purpose. These three pieces only make sense together, and scattered
across the repository root they read as production configuration — which the `nuget.local.config`
next to `nuget.config` in particular very much did.

| | |
|---|---|
| `pack-local-deps.ps1` | Packs `../../NexusKit/NexusKit.Sync` into `packages/` |
| `nuget.local.config` | Package sources with `NexusKit.*` mapped at `packages/` rather than GitHub |
| `packages/` | The packed `.nupkg`. Git-ignored — a build artefact, not a source |

## Why it exists

The Docker build context is this repository, so the build cannot reach `../NexusKit`. Inside the
image there is also no workspace `Directory.Build.targets` to rewrite the `NexusKit.Sync`
`PackageReference` into a `ProjectReference` the way a workstation build does. It restores as a
package, and while a change is being written that package does not exist anywhere yet.

## Using it

```powershell
./localdev/pack-local-deps.ps1
docker compose up -d --build
```

`docker-compose.yml` passes `DEPS=local`, so the dev stack picks this up on its own. Re-run the
script after every change to `NexusKit.Sync` — the image builds from the `.nupkg`, not from the
source, so an un-packed change is simply not in the image.

## What the default does instead

`DEPS` defaults to `feed` in the `Dockerfile`: GitHub Packages, credentials supplied as a BuildKit
secret. That is what CI and a build from a tag use, and it is the default deliberately — the build
nobody thinks to configure has to be the shippable one.

`DEPS=local` fails loudly if either the config or a `.nupkg` is missing rather than falling back to
the feed. A local build that silently is not local would compile against the published package and
pass, which is the one outcome worth refusing.

## The version

The package is stamped with the exact floor from `Directory.Packages.props` — read at run time, not
written down here. It used to be a constant, and when the floor moved from `0.5.0` to `0.5.1` the
script kept producing a package NuGet then rejected as too old.

A stable version, never `-local`: NuGet excludes pre-release versions from a range unless the range
itself is pre-release, so a suffix would restore as `NU1102` and nothing would build.
