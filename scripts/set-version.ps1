<#
.SYNOPSIS
  Write a release version into the project so the packaged artifact matches the tag.

.DESCRIPTION
  The git tag is the source of truth for a release. Two places carry the version:
    - csproj <Version>/<AssemblyVersion>/<FileVersion> — <Version> is read by
      `make package` (trimmed to semver for gta5mod.json); the other two are the
      actual DLL version.
    - The LemonUI menu subtitle string ("Version X.Y.Z" in BetterTrafficLaws.cs),
      shown in-game.
  We stamp the tag into all of them before packaging, so the artifact, gta5mod.json,
  and the in-game version all match the tag. This touches only the runner's checkout,
  never a commit, so no pre-release bump is needed.

  Accepts a semver tag ("3.0.4" or "v3.0.4"); the csproj version attributes want four
  parts, so a 3-part tag is padded with a .0 build field. The menu string uses the
  3-part semver.
#>
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Tag)

$ErrorActionPreference = 'Stop'

$semver = $Tag.TrimStart('v', 'V')
if ($semver -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') {
    throw "tag '$Tag' is not a semver version (expected e.g. 3.0.4)"
}
$threePart = ($semver -split '\.')[0..2] -join '.'
$fourPart = if ($semver -match '^\d+\.\d+\.\d+$') { "$semver.0" } else { $semver }

$root = Split-Path -Parent $PSScriptRoot

# csproj <Version>/<AssemblyVersion>/<FileVersion> — make package reads <Version>
# for gta5mod.json; all three are the DLL's version.
$csproj = Join-Path $root 'Better Traffic Laws.csproj'
$xml = Get-Content $csproj -Raw
foreach ($tagName in 'Version', 'AssemblyVersion', 'FileVersion') {
    $xml = $xml -replace "<$tagName>.*?</$tagName>", "<$tagName>$fourPart</$tagName>"
}
Set-Content -Path $csproj -Value $xml -Encoding UTF8 -NoNewline

# Menu subtitle — the in-game version string (3-part semver).
$mod = Join-Path $root 'BetterTrafficLaws.cs'
$cs = Get-Content $mod -Raw
$cs = $cs -replace '"Version \d+\.\d+\.\d+"', "`"Version $threePart`""
Set-Content -Path $mod -Value $cs -Encoding UTF8 -NoNewline

Write-Host "set version to $fourPart (menu $threePart) from tag $Tag"
