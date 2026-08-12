#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Packs the sibling NexusKit.Sync into ./packages so the container can build.

.DESCRIPTION
    The Docker build context is this repo, so it cannot reach ../NexusKit. And inside the
    image there is no workspace Directory.Build.targets to swap NexusKit.Sync for a
    ProjectReference — it restores as a package.

    Until that package is published, this script bridges the gap: it packs the sibling clone
    into ./packages, which nuget.local.config beside it lists as a source.

    Run it after changing anything in NexusKit.Sync, then rebuild the image with DEPS=local.
    The dev compose file already passes that; a bare `docker build` defaults to the feed.

.EXAMPLE
    ./localdev/pack-local-deps.ps1
    docker compose up --build
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$repoRoot  = Split-Path -Parent $PSScriptRoot
$workspace = Split-Path -Parent $repoRoot
$source    = Join-Path $workspace 'NexusKit/NexusKit.Sync/NexusKit.Sync.csproj'
$output    = Join-Path $PSScriptRoot 'packages'

if (-not (Test-Path $source)) {
    throw @"
Cannot find the NexusKit.Sync source at:
  $source

This script expects the NexusFFXIV workspace layout, with NexusKit and NexusSyncServer as siblings.
If you are building against a published NexusKit.Sync instead, you do not need this script —
delete ./localdev/packages/*.nupkg and build with DEPS=feed, which is the default.
"@
}

if (-not (Test-Path $output)) {
    New-Item -ItemType Directory -Path $output -Force | Out-Null
}

# Stale packages are worse than none: NuGet resolves the highest version it can see, so an
# old .nupkg left behind would quietly win over a freshly packed one with the same version.
Get-ChildItem -Path $output -Filter 'NexusKit.Sync.*.nupkg' -ErrorAction SilentlyContinue |
    Remove-Item -Force

Write-Host "Packing NexusKit.Sync -> $output" -ForegroundColor Cyan

# Read from the floor rather than written down beside it. This was a constant, and the floor
# moved to [0.5.1,) without it — leaving a script whose whole job is unblocking the local build
# producing a package NuGet then refuses as too old.
$packagesProps = Join-Path $repoRoot 'Directory.Packages.props'
$floorMatch = Select-String -Path $packagesProps -Pattern 'Include="NexusKit\.Sync"\s+Version="\[([0-9]+\.[0-9]+\.[0-9]+)'

if (-not $floorMatch) {
    throw @"
Cannot read the NexusKit.Sync version floor from:
  $packagesProps

Expected a PackageVersion entry of the form Version="[x.y.z,)".
"@
}

# Deliberately a STABLE version, not something like 0.5.1-local. NuGet excludes pre-release
# versions from a range unless the range itself is pre-release — so a `-local` suffix restores
# as NU1102 "found 1 version, nearest 0.5.1-local" and nothing builds. Widening the range to
# `[0.5.1-0,)` would fix it and also let an -rc from the real feed slip into a normal build,
# which the release process explicitly does not want.
#
# Exactly the floor, not above it: the package only has to clear the range, and a higher number
# here would be a version that never existed anywhere else. MinVerSkip because deriving a real
# version from git tags is the published build's job.
$localVersion = $floorMatch.Matches[0].Groups[1].Value

Write-Host "Version floor from Directory.Packages.props: $localVersion" -ForegroundColor Cyan

dotnet pack $source -c $Configuration -o $output -p:Version=$localVersion -p:MinVerSkip=true

if ($LASTEXITCODE -ne 0) {
    throw "dotnet pack failed with exit code $LASTEXITCODE."
}

Get-ChildItem -Path $output -Filter '*.nupkg' | ForEach-Object {
    Write-Host "  $($_.Name)" -ForegroundColor Green
}
