#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Packs the sibling NexusKit.Sync into ./local-packages so the container can build.

.DESCRIPTION
    The Docker build context is this repo, so it cannot reach ../NexusKit. And inside the
    image there is no workspace Directory.Build.targets to swap NexusKit.Sync for a
    ProjectReference — it restores as a package.

    Until that package is published, this script bridges the gap: it packs the sibling clone
    into ./local-packages, which nuget.config lists as a source.

    Run it after changing anything in NexusKit.Sync, then rebuild the image.

.EXAMPLE
    ./scripts/pack-local-deps.ps1
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
$output    = Join-Path $repoRoot 'local-packages'

if (-not (Test-Path $source)) {
    throw @"
Cannot find the NexusKit.Sync source at:
  $source

This script expects the NexusFFXIV workspace layout, with NexusKit and NexusSyncServer as siblings.
If you are building against a published NexusKit.Sync instead, you do not need this script —
delete ./local-packages/*.nupkg and restore normally.
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

# Deliberately a STABLE version, not something like 0.5.0-local. The floor in
# Directory.Packages.props is `[0.5.0,)`, and NuGet excludes pre-release versions from a range
# unless the range itself is pre-release — so a `-local` suffix restores as NU1102 "found 1
# version, nearest 0.5.0-local" and nothing builds. Widening the range to `[0.5.0-0,)` would
# fix it and also let an -rc from the real feed slip into a normal build, which the release
# process explicitly does not want.
#
# MinVerSkip because the version only has to clear that floor; deriving a real one from git
# tags is the published build's job.
$localVersion = '0.5.0'

dotnet pack $source -c $Configuration -o $output -p:Version=$localVersion -p:MinVerSkip=true

if ($LASTEXITCODE -ne 0) {
    throw "dotnet pack failed with exit code $LASTEXITCODE."
}

Get-ChildItem -Path $output -Filter '*.nupkg' | ForEach-Object {
    Write-Host "  $($_.Name)" -ForegroundColor Green
}
