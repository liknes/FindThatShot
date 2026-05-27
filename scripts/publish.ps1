<#
.SYNOPSIS
    Publish + pack Video Archive Manager with Velopack.

.DESCRIPTION
    1. Restores the local 'vpk' dotnet tool (if needed).
    2. Reads the version from the App csproj (or -Version override).
    3. Self-contained publishes the WPF app for win-x64 to .\publish.
    4. Optionally bundles ffmpeg.exe/ffprobe.exe from .\tools\ffmpeg into the publish output.
    5. Packs the publish folder into a Velopack release (installer, portable zip, delta nupkg).

    Resulting artifacts are written to .\releases.

.PARAMETER Version
    Override the version used by Velopack (default: read from csproj).

.PARAMETER Runtime
    Target runtime for dotnet publish (default: win-x64).

.PARAMETER SkipBundleFfmpeg
    Do not copy bundled ffmpeg from .\tools\ffmpeg even if present.

.EXAMPLE
    pwsh ./scripts/publish.ps1
    pwsh ./scripts/publish.ps1 -Version 0.2.0
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$Runtime = 'win-x64',
    [switch]$SkipBundleFfmpeg
)

$ErrorActionPreference = 'Stop'

# Resolve repo root (parent of this script's directory).
$RepoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $RepoRoot

$AppProj    = Join-Path $RepoRoot 'src\VideoArchiveManager.App\VideoArchiveManager.App.csproj'
$PublishDir = Join-Path $RepoRoot 'publish'
$ReleaseDir = Join-Path $RepoRoot 'releases'
$ToolsDir   = Join-Path $RepoRoot 'tools\ffmpeg'

if (-not (Test-Path $AppProj)) {
    throw "App csproj not found: $AppProj"
}

# 1. Restore local dotnet tools (vpk).
Write-Host "[publish] Restoring local dotnet tools..." -ForegroundColor Cyan
dotnet tool restore | Out-Host
if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed." }

# 2. Determine version.
if (-not $Version) {
    [xml]$xml = Get-Content $AppProj
    $Version = ($xml.Project.PropertyGroup | Where-Object { $_.Version }).Version
    if (-not $Version) { $Version = '0.1.0' }
}
Write-Host "[publish] Packing version: $Version" -ForegroundColor Cyan

# 3. Clean publish directory.
if (Test-Path $PublishDir) {
    Write-Host "[publish] Cleaning $PublishDir" -ForegroundColor Cyan
    Remove-Item $PublishDir -Recurse -Force
}
New-Item -ItemType Directory -Path $PublishDir | Out-Null

# 4. dotnet publish - self-contained, single-file, win-x64.
Write-Host "[publish] Publishing $Runtime..." -ForegroundColor Cyan
dotnet publish $AppProj `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:Version=$Version `
    -o $PublishDir | Out-Host
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

# 5. Optionally bundle ffmpeg into the publish output.
if (-not $SkipBundleFfmpeg -and (Test-Path $ToolsDir)) {
    $destTools = Join-Path $PublishDir 'tools\ffmpeg'
    Write-Host "[publish] Bundling ffmpeg from $ToolsDir" -ForegroundColor Cyan
    New-Item -ItemType Directory -Path $destTools -Force | Out-Null
    Copy-Item -Path (Join-Path $ToolsDir '*') -Destination $destTools -Recurse -Force
} else {
    Write-Host "[publish] No bundled ffmpeg (looked in $ToolsDir). End users will need ffmpeg/ffprobe on PATH." -ForegroundColor Yellow
}

# 6. Pack with Velopack.
if (-not (Test-Path $ReleaseDir)) {
    New-Item -ItemType Directory -Path $ReleaseDir | Out-Null
}

Write-Host "[publish] Running vpk pack..." -ForegroundColor Cyan
dotnet vpk pack `
    --packId VideoArchiveManager `
    --packVersion $Version `
    --packDir $PublishDir `
    --packTitle "Video Archive Manager" `
    --packAuthors "Find That Shot" `
    --mainExe VideoArchiveManager.exe `
    --outputDir $ReleaseDir | Out-Host
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed." }

Write-Host ""
Write-Host "[publish] Done." -ForegroundColor Green
Write-Host "[publish] Artifacts in: $ReleaseDir" -ForegroundColor Green
Get-ChildItem $ReleaseDir | Sort-Object Name | Format-Table Name, Length -AutoSize
