# Releasing NexusSyncServer

> **Never add a `<Version>` to a csproj here.** MinVer derives it from the tag, and that is what
> makes tag and package version impossible to desynchronise. PlayerNexusTracker pinned one
> instead and shipped a release whose assembly version did not match its tag, which made the
> release invisible to existing users. Wanting to pin a version is a signal something else is
> wrong.

## What a tag produces

| Artefact | Where | Who consumes it |
|---|---|---|
| `ghcr.io/nexusffxiv/nexussyncserver:X.Y.Z` | GHCR | operators running a server |
| `NexusSyncServer.Hosting`, `.Ux`, `.Modules.*` | GitHub Packages | authors composing their own image |
| GitHub Release | this repository | humans, with the whole stack's changes folded in |

All of it comes from **`ci.yml`**, and **only a tag publishes anything**. A push to `main` or a
pull request builds the image as a check and throws it away; the `release` job runs on a tag
only, after `build`, `audit` and `image` have passed.

Every image in the registry therefore corresponds to a release anybody can look up. Publishing
on each push filled it with `main` and `sha-…` images nobody asked for, and made "which one is
released?" a question you answered by reading dates.

`latest` moves only on a stable tag. A prerelease such as `v0.2.0-rc.1` publishes `0.2.0-rc.1`
and nothing else, so `docker pull …:latest` cannot land on a release candidate by accident.

One workflow on purpose. A separate release workflow would repeat the restore, the build and
the pack, and the two would drift — the first attempt at this repository had exactly that, and
the duplicate packed against an unauthenticated feed while the original did not.

## The chain

```
NexusKit          tag vX.Y.Z  →  NexusKit.Sync on the feed
      ↓
NexusKit.Modules  tag vX.Y.Z  →  NexusKit.Modules.Sync on the feed
      ↓
NexusSyncServer   tag vX.Y.Z  →  image + packages + release notes
```

Downwards only. The server consumes `NexusKit.Sync`; nothing upstream consumes the server.

**A floor must name a release that exists.** `Directory.Packages.props` carries open-ended
ranges like `[0.5.1,)`. A floor naming a version that never shipped the library restores the
substitute and raises `NU1603`, which `TreatWarningsAsErrors` turns into a failed build — this
has already happened once. Raise the floor in the same change that needs the newer package.

## Which number moves

The server is an **end product**, like the plugin — not a library. Its version says what the
deployment does, not what its API looks like.

| Change | Bump |
|---|---|
| A breaking change in `NexusKit.Sync`, so old clients stop working | **major** |
| New collections, endpoints or modules; a new contract feature | **minor** |
| Fixes, dependency bumps, anything invisible to a client | **patch** |

The distinction that matters: **a protocol break is a major bump here even when the code change
is small.** An operator upgrading a server is deciding on behalf of every plugin pointed at it,
and the version number is the only warning they get. `SyncProtocolVersion.Current` states the
wire version separately, and that is what a client negotiates against — but nobody reads it
before running `docker pull`.

## Steps

1. Land everything through pull requests. `main` is protected; `build` must be green.
2. Confirm the NexusKit floors in `Directory.Packages.props` name released versions.
3. Tag and push:

   ```bash
   git tag -a v0.2.0 -m "v0.2.0 — what changed and who should care"
   git push origin v0.2.0
   ```

4. Watch CI. The `image` job publishes the container, then `release` packs, pushes and writes
   the notes.
5. Check the release notes: the internal-dependency step should have appended the NexusKit and
   NexusKit.Modules entries beneath the generated ones.

## The changelog covers the whole stack

`expand-internal-deps-changelog` folds the upstream repositories' entries into this release's
notes, the same way PlayerNexusTracker's release does.

The reason is the same in both cases. The server is where a protocol change becomes visible, but
it is not where that change was written. Somebody reading "why did my plugin stop pushing" should
find the answer on one page, not by working out which `NexusKit.Sync` version this build carried
and then going to read that repository's history.

The step is `continue-on-error`. A failure there costs annotation, not the release.

## Pre-releases

A tag containing a hyphen is marked pre-release automatically:

```bash
git tag -a v0.3.0-rc.1 -m "v0.3.0-rc.1"
```

Useful when the server needs an unreleased `NexusKit.Sync`: publish that as an `-rc` too, pin the
floor to it for the duration, and unpin before the final tag. A pinned protocol floor left in
place is how a server ends up unable to serve clients that have moved on.
