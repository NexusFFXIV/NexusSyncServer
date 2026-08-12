# syntax=docker/dockerfile:1

# ---- build -------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# One COPY, not a hand-maintained list of every .csproj.
#
# The usual trick is to copy the manifests first so `dotnet restore` caches independently of
# source changes. It was tried here and removed: the list has to be edited whenever a project
# is added, and forgetting once produced an image that built cleanly and silently shipped
# without NexusSyncServer.Ux — a 404 on every page with nothing in the logs. Restore caching is not
# worth a failure mode that looks like a routing bug.
COPY . .

# Where NexusKit.Sync comes from. Two ways, and the default is the one CI uses — a build with
# no flag set has to be the shippable one, because that is the build nobody stops to think about.
#
#   feed  (default) — GitHub Packages, for CI and for anyone building from a tag. Needs
#                     credentials, passed as a BuildKit secret rather than a build arg so the
#                     token never lands in an image layer or in `docker history`.
#   local           — ./localdev holds a .nupkg produced by localdev/pack-local-deps.ps1, and
#                     the package sources pointing at it. The development loop, and the only way
#                     to build against a NexusKit.Sync change that is not published yet — which,
#                     while it is being written, is every change. See localdev/README.md.
#
# Either way the chosen file lands over ./nuget.config rather than in the user profile. The
# repository's own file sits in the working directory and wins there, and its credential is a
# %GITHUB_PACKAGES_PAT% placeholder that nothing sets inside a container — left in place it
# answers 401 whatever else is configured.
# Backslash continuations rather than a heredoc, and not by preference: this file has CRLF
# endings and no .gitattributes normalises them, so a heredoc would hand /bin/sh a script with a
# carriage return on every line and fail in CI exactly as it does here. Docker strips the CR when
# it joins continued lines, which is why this shape survives and that one does not.
#
# On DEPS=local the missing pieces are refused rather than fallen back on. Without the folder
# source, restore would reach the GitHub feed and build against the published package —
# succeeding, and testing the wrong code. A local build that silently is not local is worse than
# one that stops.
ARG DEPS=feed
RUN --mount=type=secret,id=nuget_auth,required=false set -eu; \
    case "${DEPS}" in \
      feed) \
        if [ -f /run/secrets/nuget_auth ]; then cp /run/secrets/nuget_auth ./nuget.config; fi ;; \
      local) \
        if [ ! -f localdev/nuget.local.config ]; then \
          echo "DEPS=local needs localdev/nuget.local.config, missing from the build context." >&2; \
          exit 1; \
        fi; \
        if ! ls localdev/packages/NexusKit.Sync.*.nupkg >/dev/null 2>&1; then \
          echo "DEPS=local needs ./localdev/packages/NexusKit.Sync.*.nupkg." >&2; \
          echo "Run localdev/pack-local-deps.ps1 first, then rebuild." >&2; \
          exit 1; \
        fi; \
        cp localdev/nuget.local.config ./nuget.config ;; \
      *) \
        echo "DEPS must be 'feed' or 'local', got '${DEPS}'." >&2; \
        exit 1 ;; \
    esac; \
    dotnet restore NexusSyncServer.sln

# Version comes from outside because .git is not in the build context, so MinVer has no tags
# to read. CI passes the release tag; a local build gets a clearly-local default rather than a
# silent 0.0.0 that looks like a real version.
ARG VERSION=0.0.0-local
RUN dotnet publish NexusSyncServer/NexusSyncServer.csproj \
        -c Release \
        -o /app \
        --no-restore \
        -p:Version=${VERSION} \
        -p:MinVerSkip=true

# ---- runtime -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# No extra native packages are installed. The database client is fully managed, so nothing
# has to be added on top of the runtime image.

# Non-root. The process writes nothing but logs to stdout, so there is no reason for a
# network-facing service to run as root.
RUN useradd --system --uid 64198 --create-home nexussyncserver
# Created here, as root, before dropping privileges — and this is the only reason it exists in
# the image at all. Docker initialises an empty named volume from whatever the image has at the
# mount point, ownership included; without this the volume would arrive owned by root and the
# non-root process could not write its data-protection keys. The failure is indirect and ugly:
# no key ring means no cookie can be signed, so sign-in ends in a 500 rather than anything
# naming a permission.
RUN mkdir -p /home/nexussyncserver/.aspnet/DataProtection-Keys \
 && chown -R nexussyncserver:nexussyncserver /home/nexussyncserver

USER nexussyncserver

COPY --from=build --chown=nexussyncserver:nexussyncserver /app .

# Contracts are mounted here. An empty directory is fine — the server then serves whatever is
# already registered in the database.
VOLUME ["/contracts"]

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Registry__ContractsDirectory=/contracts

EXPOSE 8080

# Liveness only, and it probes via this same binary rather than curl — the aspnet image ships
# neither curl nor wget, and adding one to probe your own process is a package and a CVE
# surface for something the process can already do. Readiness lives at /ready and depends on
# the database; failing that here would restart a healthy container for an outside fault.
HEALTHCHECK --interval=30s --timeout=3s --start-period=20s --retries=3 \
    CMD ["/app/NexusSyncServer", "--healthcheck"]

ENTRYPOINT ["/app/NexusSyncServer"]
