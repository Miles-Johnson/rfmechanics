[CmdletBinding()]
param([string]$OutputFile = '')
$ErrorActionPreference = 'Stop'
$repoPath = $PSScriptRoot.Replace('\','/')
$sha = 'unknown'
$tree = 'unknown'
$dirty = $true
try {
    $candidate = & git -c "safe.directory=$repoPath" -C $repoPath rev-parse HEAD 2>$null
    if ($LASTEXITCODE -eq 0 -and "$candidate" -match '^[0-9a-f]{40}$') {
        $sha = "$candidate"
        $candidateTree = & git -c "safe.directory=$repoPath" -C $repoPath rev-parse 'HEAD^{tree}' 2>$null
        if ($LASTEXITCODE -eq 0) { $tree = "$candidateTree" }
        $status = & git -c "safe.directory=$repoPath" -C $repoPath status --porcelain --untracked-files=normal 2>$null
        if ($LASTEXITCODE -eq 0) { $dirty = [bool]$status }
    }
} catch { }
$shortSha = if ($sha -eq 'unknown') { 'unknown' } else { $sha.Substring(0,7) }
if ($OutputFile) {
    $fullOutput = [IO.Path]::GetFullPath($OutputFile)
    New-Item -ItemType Directory -Path (Split-Path -Parent $fullOutput) -Force | Out-Null
    $code = 'internal static class GitInfo { public const string Sha = "' + $sha + '"; public const bool Dirty = ' + $dirty.ToString().ToLowerInvariant() + "; }"
    [IO.File]::WriteAllText($fullOutput, $code, (New-Object Text.UTF8Encoding($false)))
} else { [PSCustomObject]@{ sha = $sha; shortSha = $shortSha; tree = $tree; dirty = $dirty } }
