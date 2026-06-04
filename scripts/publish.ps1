<#
.SYNOPSIS
    Publish + pack Find That Shot with Velopack.

.DESCRIPTION
    1. Restores the local 'vpk' dotnet tool (if needed).
    2. Reads the version from the App csproj (or -Version override).
    3. Self-contained publishes the WPF app for win-x64 to .\publish.
    4. Optionally bundles ffmpeg.exe/ffprobe.exe from .\tools\ffmpeg into the publish output.
    5. Optionally bundles libmpv-2.dll from .\tools\mpv into the publish output (GPU player).
    6. Packs the publish folder into a Velopack release (installer, portable zip, delta nupkg).

    Resulting artifacts are written to .\releases.

.PARAMETER Version
    Override the version used by Velopack (default: read from csproj).

.PARAMETER Runtime
    Target runtime for dotnet publish (default: win-x64).

.PARAMETER SkipBundleFfmpeg
    Do not copy bundled ffmpeg from .\tools\ffmpeg even if present.

.PARAMETER SkipBundleMpv
    Do not copy bundled libmpv from .\tools\mpv even if present.

.PARAMETER SkipBundleModel
    Do not copy a bundled CLIP ONNX model from .\tools\models even if present.

.EXAMPLE
    pwsh ./scripts/publish.ps1
    pwsh ./scripts/publish.ps1 -Version 0.2.0
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$Runtime = 'win-x64',
    [switch]$SkipBundleFfmpeg,
    [switch]$SkipBundleMpv,
    [switch]$SkipBundleModel
)

$ErrorActionPreference = 'Stop'

# Resolve repo root (parent of this script's directory).
$RepoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $RepoRoot

$AppProj    = Join-Path $RepoRoot 'src\VideoArchiveManager.App\VideoArchiveManager.App.csproj'
$PublishDir = Join-Path $RepoRoot 'publish'
$ReleaseDir = Join-Path $RepoRoot 'releases'
$ToolsDir   = Join-Path $RepoRoot 'tools\ffmpeg'
$MpvDir     = Join-Path $RepoRoot 'tools\mpv'
$ModelsDir  = Join-Path $RepoRoot 'tools\models'
$AppIcon    = Join-Path $RepoRoot 'src\VideoArchiveManager.App\Assets\AppIcon.ico'

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

# 5a. Optionally bundle libmpv into the publish output. This is what enables the
#     GPU-rendered mpv player for end users who don't generate proxies; without
#     it App.xaml.cs leaves UseMpvPlayer = false and the app falls back to FFME.
if (-not $SkipBundleMpv -and (Test-Path $MpvDir)) {
    $destMpv = Join-Path $PublishDir 'tools\mpv'
    Write-Host "[publish] Bundling libmpv from $MpvDir" -ForegroundColor Cyan
    New-Item -ItemType Directory -Path $destMpv -Force | Out-Null
    Copy-Item -Path (Join-Path $MpvDir '*') -Destination $destMpv -Recurse -Force
} else {
    Write-Host "[publish] No bundled libmpv (looked in $MpvDir). The GPU player will be disabled; FFME is used instead." -ForegroundColor Yellow
}

# 5c. Optionally bundle a CLIP ONNX model into the publish output. This is what
#     makes the opt-in AI tagging / natural-language search work out of the box
#     for end users; without it the feature stays dark until a user supplies a
#     model directory themselves. The model files are NOT committed to the repo
#     (tools/models is .gitignored); produce them with scripts/export-clip-onnx.py.
if (-not $SkipBundleModel -and (Test-Path $ModelsDir)) {
    $destModels = Join-Path $PublishDir 'tools\models'
    Write-Host "[publish] Bundling CLIP model from $ModelsDir" -ForegroundColor Cyan
    New-Item -ItemType Directory -Path $destModels -Force | Out-Null
    Copy-Item -Path (Join-Path $ModelsDir '*') -Destination $destModels -Recurse -Force
} else {
    Write-Host "[publish] No bundled CLIP model (looked in $ModelsDir). AI tagging stays off unless the user supplies a model." -ForegroundColor Yellow
}

# 5b. Ensure attribution files ship next to the executable, even if MSBuild Content copy
#     misses them. These are required to satisfy the GPL/LGPL distribution obligations
#     of bundled dependencies (FFmpeg, FFmpeg.AutoGen via FFME) as well as this app's own
#     GPLv3 license.
foreach ($name in @('LICENSE', 'THIRD-PARTY-NOTICES.md')) {
    $src = Join-Path $RepoRoot $name
    if (Test-Path $src) {
        Write-Host "[publish] Copying $name to publish/" -ForegroundColor Cyan
        Copy-Item -Path $src -Destination (Join-Path $PublishDir $name) -Force
    } else {
        Write-Host "[publish] WARNING: $name not found at repo root — distribution may be non-compliant." -ForegroundColor Yellow
    }
}

# 6. Pack with Velopack.
if (-not (Test-Path $ReleaseDir)) {
    New-Item -ItemType Directory -Path $ReleaseDir | Out-Null
}

Write-Host "[publish] Running vpk pack..." -ForegroundColor Cyan
$vpkArgs = @(
    'vpk', 'pack',
    '--packId', 'VideoArchiveManager',
    '--packVersion', $Version,
    '--packDir', $PublishDir,
    '--packTitle', 'Find That Shot',
    '--packAuthors', 'Find That Shot',
    '--mainExe', 'VideoArchiveManager.exe',
    '--outputDir', $ReleaseDir
)
if (Test-Path $AppIcon) {
    Write-Host "[publish] Using app icon: $AppIcon" -ForegroundColor Cyan
    $vpkArgs += @('--icon', $AppIcon)
} else {
    Write-Host "[publish] WARNING: $AppIcon not found - installer / shortcut will use the default Velopack icon." -ForegroundColor Yellow
}
dotnet @vpkArgs | Out-Host
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed." }

Write-Host ""
Write-Host "[publish] Done." -ForegroundColor Green
Write-Host "[publish] Artifacts in: $ReleaseDir" -ForegroundColor Green
Get-ChildItem $ReleaseDir | Sort-Object Name | Format-Table Name, Length -AutoSize
