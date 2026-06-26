<#
.SYNOPSIS
  Write a release version into the project so the packaged artifact matches the tag.

.DESCRIPTION
  The git tag is the source of truth for a release. Two places carry the version:
    - csproj <Version> — read by `make package` (trimmed to semver for gta5mod.json).
    - Properties\AssemblyInfo.cs [assembly: AssemblyVersion]/[AssemblyFileVersion] —
      the actual DLL version (this legacy project ignores the csproj <AssemblyVersion>),
      which the mod reads at runtime to show the menu version.
  We stamp the tag into both before packaging, so the artifact, gta5mod.json, and the
  in-game version all match the tag. This touches only the runner's checkout, never a
  commit, so no pre-release bump is needed.

  Accepts a semver tag ("3.0.4" or "v3.0.4"); .NET version attributes want four
  parts, so a 3-part tag is padded with a .0 build field.
#>
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Tag)

$ErrorActionPreference = 'Stop'

$semver = $Tag.TrimStart('v', 'V')
if ($semver -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') {
    throw "tag '$Tag' is not a semver version (expected e.g. 3.0.4)"
}
$fourPart = if ($semver -match '^\d+\.\d+\.\d+$') { "$semver.0" } else { $semver }

$root = Split-Path -Parent $PSScriptRoot

# csproj <Version> — make package reads this for gta5mod.json.
$csproj = Join-Path $root 'Better Traffic Laws.csproj'
$xml = Get-Content $csproj -Raw
$xml = $xml -replace '<Version>.*?</Version>', "<Version>$fourPart</Version>"
Set-Content -Path $csproj -Value $xml -Encoding UTF8 -NoNewline

# AssemblyInfo.cs — the real DLL version (read at runtime for the menu string).
$asmInfo = Join-Path $root 'Properties\AssemblyInfo.cs'
$cs = Get-Content $asmInfo -Raw
foreach ($attr in 'AssemblyVersion', 'AssemblyFileVersion') {
    $cs = $cs -replace "$attr\(`"[^`"]*`"\)", "$attr(`"$fourPart`")"
}
Set-Content -Path $asmInfo -Value $cs -Encoding UTF8 -NoNewline

Write-Host "set version to $fourPart (from tag $Tag)"
