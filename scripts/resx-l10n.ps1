#requires -Version 5.1
<#
.SYNOPSIS
    Export or import Find That Shot UI strings between .resx files and Excel.

.EXAMPLE
    pwsh ./scripts/resx-l10n.ps1 export
    pwsh ./scripts/resx-l10n.ps1 import
    pwsh ./scripts/resx-l10n.ps1 import -DryRun
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet("export", "import", "help")]
    [string] $Command,

    [string] $Dir,
    [string] $In,
    [string] $Out,
    [switch] $DryRun
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $PSScriptRoot "resx-l10n/ResxL10n.csproj"

$argsList = @($Command)
if ($Dir) { $argsList += @("--dir", $Dir) }
if ($In) { $argsList += @("--in", $In) }
if ($Out) { $argsList += @("--out", $Out) }
if ($DryRun) { $argsList += "--dry-run" }

Push-Location $repoRoot
try {
    dotnet run --project $project -- @argsList
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    Pop-Location
}
