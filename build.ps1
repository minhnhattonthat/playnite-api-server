#requires -Version 5.1
<#
.SYNOPSIS
  Builds PlayniteApiServer and deploys the output into the Playnite Extensions folder.

.DESCRIPTION
  1. Runs msbuild in Release configuration.
  2. Wipes and re-creates H:\Playnite\Extensions\PlayniteApiServer_<guid>\.
  3. Copies PlayniteApiServer.dll (+ .pdb), extension.yaml, and icon.png.

  Does NOT copy Playnite.SDK.dll or Newtonsoft.Json.dll — Playnite already loads
  those from its install directory, and duplicates would cause type-identity
  conflicts.

.PARAMETER Configuration
  MSBuild configuration. Defaults to Release.

.PARAMETER SkipBuild
  Skip the msbuild step and just redeploy the current bin\<Configuration>\ output.
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ProjectRoot  = Split-Path -Parent $PSCommandPath
$Project      = Join-Path $ProjectRoot 'PlayniteApiServer.csproj'
$ExtensionId  = 'PlayniteApiServer_0a96c485-030a-4178-9c6c-6a9098fac2d5'
$DeployTarget = Join-Path 'H:\Playnite\Extensions' $ExtensionId

function Find-MsBuild {
    $candidates = @()

    # 1. MSBuild on PATH.
    $onPath = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($onPath) { $candidates += $onPath.Source }

    # 2. vswhere (Visual Studio 2017+).
    $vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $installPath = & $vswhere -latest -requires Microsoft.Component.MSBuild `
            -property installationPath -format value 2>$null
        if ($installPath) {
            $candidates += (Join-Path $installPath 'MSBuild\Current\Bin\MSBuild.exe')
            $candidates += (Join-Path $installPath 'MSBuild\15.0\Bin\MSBuild.exe')
        }
    }

    # 3. Build Tools default locations.
    $candidates += @(
        'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe',
        'C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe',
        'C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\MSBuild.exe',
        'C:\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe'
    )

    foreach ($c in $candidates) {
        if ($c -and (Test-Path $c)) { return $c }
    }

    throw "MSBuild was not found. Install Visual Studio, Visual Studio Build Tools, or put msbuild.exe on PATH."
}

if (-not $SkipBuild) {
    $msbuild = Find-MsBuild
    Write-Host "Using MSBuild: $msbuild"
    & $msbuild $Project "/p:Configuration=$Configuration" /nologo /v:m
    if ($LASTEXITCODE -ne 0) {
        throw "MSBuild failed with exit code $LASTEXITCODE."
    }
}

$binDir = Join-Path $ProjectRoot "bin\$Configuration"
$dllPath = Join-Path $binDir 'PlayniteApiServer.dll'
if (-not (Test-Path $dllPath)) {
    throw "Build output not found: $dllPath"
}

Write-Host "Deploying to $DeployTarget"
if (Test-Path $DeployTarget) {
    Remove-Item $DeployTarget -Recurse -Force
}
New-Item -ItemType Directory -Path $DeployTarget | Out-Null

$payload = @(
    (Join-Path $binDir  'PlayniteApiServer.dll'),
    (Join-Path $binDir  'PlayniteApiServer.pdb'),
    (Join-Path $ProjectRoot 'extension.yaml'),
    (Join-Path $ProjectRoot 'icon.png')
)

foreach ($p in $payload) {
    if (Test-Path $p) {
        Copy-Item $p -Destination $DeployTarget -Force
        Write-Host "  + $(Split-Path $p -Leaf)"
    }
    else {
        Write-Warning "Missing optional payload file: $p"
    }
}

Write-Host ""
Write-Host "Done. Restart Playnite to load the updated plugin." -ForegroundColor Green
