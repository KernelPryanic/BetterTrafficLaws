<#
.SYNOPSIS
  Fetch the SHVDN / LemonUI reference assemblies the build links against.

.DESCRIPTION
  The csproj references ..\packages\ScriptHookVDotNet3.dll and
  ..\packages\LemonUI.SHVDN3.dll (SpecificVersion=False) — the player's runtime
  assemblies, gitignored and not in the repo. Locally they sit in the shared
  ..\packages\ folder of the dev's GTA workspace; CI has neither, so this script
  pulls pinned copies into that same ..\packages\ location before the build.

  Sources (the Enhanced SHVDN fork is NOT on NuGet, so it comes from its GitHub
  release; LemonUI is on NuGet):
    - ScriptHookVDotNet3.dll  <- Chiheb-Bacha/ScriptHookVDotNetEnhanced release zip
    - LemonUI.SHVDN3.dll      <- nuget.org LemonUI.SHVDN3 package

  Idempotent: a DLL already present is left untouched (so a local run never
  clobbers the dev's own packages folder). Pass -Force to re-download.
#>
[CmdletBinding()]
param(
    # Pinned to match the assemblies this mod is developed against
    # (SHVDN 3.9.0 ships in the Enhanced fork's v1.1.0.5 release; LemonUI 2.2.0).
    [string]$ShvdnReleaseTag = 'v1.1.0.5',
    [string]$LemonUiVersion  = '2.2.0',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue' # keep CI logs quiet + downloads fast

# ..\packages relative to the repo root (this script lives in <repo>\scripts).
$repoRoot = Split-Path -Parent $PSScriptRoot
$packages = Join-Path (Split-Path -Parent $repoRoot) 'packages'
New-Item -ItemType Directory -Force -Path $packages | Out-Null

function Get-DllFromZip {
    param([string]$Url, [string]$EntryName, [string]$OutFile)

    if ((Test-Path $OutFile) -and -not $Force) {
        Write-Host "have    $(Split-Path -Leaf $OutFile) (skip)"
        return
    }
    $tmp = New-TemporaryFile
    try {
        Invoke-WebRequest -Uri $Url -OutFile $tmp
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zip = [IO.Compression.ZipFile]::OpenRead($tmp)
        try {
            # Match by leaf name so a nupkg's lib/net48/ prefix or a release zip's
            # flat layout both resolve without hard-coding the full inner path.
            $entry = $zip.Entries | Where-Object { $_.Name -ieq $EntryName } | Select-Object -First 1
            if (-not $entry) { throw "'$EntryName' not found inside $Url" }
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $OutFile, $true)
        } finally { $zip.Dispose() }
    } finally { Remove-Item $tmp -Force -ErrorAction SilentlyContinue }
    Write-Host "fetched $(Split-Path -Leaf $OutFile)"
}

Get-DllFromZip `
    -Url "https://github.com/Chiheb-Bacha/ScriptHookVDotNetEnhanced/releases/download/$ShvdnReleaseTag/ScriptHookVDotNetEnhanced-$ShvdnReleaseTag.zip" `
    -EntryName 'ScriptHookVDotNet3.dll' `
    -OutFile (Join-Path $packages 'ScriptHookVDotNet3.dll')

Get-DllFromZip `
    -Url "https://api.nuget.org/v3-flatcontainer/lemonui.shvdn3/$LemonUiVersion/lemonui.shvdn3.$LemonUiVersion.nupkg" `
    -EntryName 'LemonUI.SHVDN3.dll' `
    -OutFile (Join-Path $packages 'LemonUI.SHVDN3.dll')

Write-Host "packages ready in $packages"
