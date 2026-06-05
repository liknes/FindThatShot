#requires -Version 5.1
<#
.SYNOPSIS
    Prepends the standard GNU GPL v3 source-file header to hand-written C#
    files in this repository.

.DESCRIPTION
    Per the GPL's "How to Apply These Terms" guidance, every source file should
    carry a short copyright notice and a pointer to the full license. This makes
    authorship unambiguous and keeps attribution attached even when individual
    files are copied out of the project.

    The script is idempotent: files that already contain the header (detected via
    a sentinel marker) are skipped, so it is safe to re-run after adding new
    files.

    Auto-generated files are intentionally skipped (EF Core migrations,
    *.Designer.cs, and anything under obj/ or bin/) because tooling regenerates
    them and would clobber or duplicate the header.

.EXAMPLE
    pwsh ./scripts/add-license-headers.ps1            # apply headers
    pwsh ./scripts/add-license-headers.ps1 -WhatIf    # preview which files change
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [int]    $Year   = 2026,
    [string] $Author = 'Ingve Moss Liknes',
    [string] $Email  = 'findthatshot@ingve.no'
)

$ErrorActionPreference = 'Stop'

# Sentinel used to detect an already-stamped file (keep stable across runs).
$marker = 'SPDX-License-Identifier: GPL-3.0-or-later'

$header = @"
// Find That Shot - organize and search a large local video archive.
// $marker
// Copyright (C) $Year $Author <$Email>
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

"@ -replace "`r?`n", "`r`n"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$srcRoot  = Join-Path $repoRoot 'src'

$files = Get-ChildItem -Path $srcRoot -Recurse -Filter *.cs -File | Where-Object {
    $p = $_.FullName
    ($p -notmatch '[\\/]obj[\\/]') -and
    ($p -notmatch '[\\/]bin[\\/]') -and
    ($p -notmatch '[\\/]Migrations[\\/]') -and
    ($_.Name -notlike '*.Designer.cs')
}

$stamped = 0
$skipped = 0

foreach ($file in $files) {
    $content = Get-Content -Raw -LiteralPath $file.FullName

    if ($content -like "*$marker*") {
        $skipped++
        continue
    }

    if ($PSCmdlet.ShouldProcess($file.FullName, 'Add GPLv3 header')) {
        Set-Content -LiteralPath $file.FullName -Value ($header + $content) -NoNewline -Encoding UTF8
    }
    $stamped++
    Write-Host "stamped: $($file.FullName.Substring($repoRoot.Path.Length + 1))"
}

Write-Host ""
Write-Host "Done. Stamped $stamped file(s); skipped $skipped already-stamped file(s)."
