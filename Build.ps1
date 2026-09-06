# Build/package only. No installation, server operations or publication.
[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [switch]$ReleaseCandidate, [string]$OutputDirectory = ''
)
$ErrorActionPreference = 'Stop'
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'modinfo.json') -Raw | ConvertFrom-Json
& (Join-Path $PSScriptRoot 'Validate.ps1')
if ($ReleaseCandidate) {
    $identity = & (Join-Path $PSScriptRoot 'SourceIdentity.ps1')
    if ($identity.dirty -or $identity.sha -eq 'unknown') { throw 'Release candidates require a clean Git checkout and known SHA' }
    if ($Configuration -ne 'Release') { throw 'Candidates require Release configuration' }
}
$packageArgs = @{ Configuration = $Configuration; ReleaseCandidate = $ReleaseCandidate; OutputDirectory = $OutputDirectory }
if ($manifest.type -eq 'code') {
    if (-not $env:VINTAGE_STORY -or -not (Test-Path -LiteralPath (Join-Path $env:VINTAGE_STORY 'VintagestoryAPI.dll'))) {
        throw 'Set VINTAGE_STORY to your legitimate game installation; .NET 10 SDK is required'
    }
    & dotnet build (Join-Path $PSScriptRoot ($manifest.modid + '.csproj')) -c $Configuration -p:DietSetupDeploy=false -p:RfMechanicsDeploy=false
    if ($LASTEXITCODE -ne 0) { throw "Mod build failed: $LASTEXITCODE" }
    $packageArgs.Dll = Join-Path $PSScriptRoot "bin\$Configuration\Mods\$($manifest.modid).dll"
    $packageArgs.Pdb = Join-Path $PSScriptRoot "bin\$Configuration\Mods\$($manifest.modid).pdb"
}
& (Join-Path $PSScriptRoot 'Package.ps1') @packageArgs
