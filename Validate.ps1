# Source/metadata validation; no game, network or deployment.
$ErrorActionPreference = 'Stop'
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'modinfo.json') -Raw | ConvertFrom-Json
if ($manifest.modid -notmatch '^[a-z0-9]+$' -or $manifest.version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw 'Invalid mod identity/version' }
if (-not $manifest.authors -or -not $manifest.description -or -not $manifest.dependencies.game) { throw 'Missing authors, description or game dependency' }
foreach ($file in @('LICENSE','README.md','CREDITS.md','THIRD_PARTY_NOTICES.md','RELEASING.md','Build.ps1','Package.ps1','SourceIdentity.ps1')) {
    if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot $file) -PathType Leaf)) { throw "Missing $file" }
}
foreach ($file in Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1' -File) {
    $tokens = $null
    $parseErrors = $null
    [void][Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors) { throw "$($file.Name): $parseErrors" }
}
# The game's broader asset JSON dialect is validated by mod load/playtesting.
Write-Host "Source metadata checks passed: $($manifest.modid) $($manifest.version)"
