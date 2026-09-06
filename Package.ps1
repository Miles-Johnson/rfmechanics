[CmdletBinding()]
param(
    [string]$Dll = "",
    [string]$Pdb = "",
    [string]$DeployPath = "",
    [string]$StagingPath = "",
    [string]$Configuration = "Release",
    [string]$OutputDirectory = "",
    [switch]$ReleaseCandidate
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repoRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$manifest = Get-Content -LiteralPath (Join-Path $repoRoot 'modinfo.json') -Raw | ConvertFrom-Json
$modId = $manifest.modid
$version = $manifest.version
if ($modId -notmatch '^[a-z0-9]+$' -or $version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw 'Invalid package identity' }
& (Join-Path $repoRoot 'Validate.ps1')
if ($manifest.type -eq 'code' -and (-not $Dll -or -not (Test-Path -LiteralPath $Dll -PathType Leaf))) { throw 'A built mod DLL is required' }

function Assert-ChildPath([string]$Path, [string]$Parent) {
    $resolved = [IO.Path]::GetFullPath($Path)
    $root = [IO.Path]::GetFullPath($Parent).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw "Path outside intended directory: $resolved" }
    if (Test-Path -LiteralPath $resolved) {
        if ((Get-Item -LiteralPath $resolved).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Refusing reparse-point target: $resolved" }
    }
}
function Hash-Bytes([byte[]]$Bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($Bytes)).Replace('-','').ToLowerInvariant() }
    finally { $sha.Dispose() }
}
function Hash-Tree([string]$Directory) {
    $rows = Get-ChildItem -LiteralPath $Directory -Recurse -File | ForEach-Object {
        $name = $_.FullName.Substring($Directory.Length).TrimStart('\','/').Replace('\','/')
        "$name`:$(Hash-Bytes ([IO.File]::ReadAllBytes($_.FullName)))"
    }
    return Hash-Bytes ([Text.Encoding]::UTF8.GetBytes((($rows | Sort-Object) -join "`n")))
}
function Hash-Zip([string]$Path) {
    $zip = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $rows = foreach ($entry in $zip.Entries) {
            $stream = $entry.Open(); $memory = New-Object IO.MemoryStream
            try { $stream.CopyTo($memory); "$($entry.FullName):$(Hash-Bytes ($memory.ToArray()))" }
            finally { $stream.Dispose(); $memory.Dispose() }
        }
        return Hash-Bytes ([Text.Encoding]::UTF8.GetBytes((($rows | Sort-Object) -join "`n")))
    } finally { $zip.Dispose() }
}

$sourceIdentity = & (Join-Path $repoRoot 'SourceIdentity.ps1')
if ($ReleaseCandidate -and ($sourceIdentity.dirty -or $sourceIdentity.sha -eq 'unknown' -or $Configuration -ne 'Release')) { throw 'Release candidates require Release configuration, clean source and known full SHA' }
if ($ReleaseCandidate -and $manifest.type -eq 'code') {
    $generated = Join-Path $repoRoot "obj/$Configuration/GitInfo.g.cs"
    if (-not (Test-Path -LiteralPath $generated)) { throw 'Build the candidate with Build.ps1 before packaging' }
    $compiledStamp = Get-Content -LiteralPath $generated -Raw
    if (-not $compiledStamp.Contains('Sha = "' + $sourceIdentity.sha + '"') -or -not $compiledStamp.Contains('Dirty = false')) { throw 'Compiled source identity differs; rebuild with Build.ps1' }
}
$stageDir = Join-Path $repoRoot ('obj/package/stage-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stageDir -Force | Out-Null
if ($manifest.type -eq 'code') { Copy-Item -LiteralPath $Dll -Destination (Join-Path $stageDir "$modId.dll") }
Copy-Item -LiteralPath (Join-Path $repoRoot 'modinfo.json') -Destination $stageDir
Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md') -Destination $stageDir
foreach ($name in @('LICENSE','CREDITS.md','THIRD_PARTY_NOTICES.md','CHANGELOG.md')) { Copy-Item -LiteralPath (Join-Path $repoRoot $name) -Destination $stageDir }
foreach ($name in @('licenses','ModConfig-examples')) {
    if (Test-Path -LiteralPath (Join-Path $repoRoot $name)) { Copy-Item -LiteralPath (Join-Path $repoRoot $name) -Destination $stageDir -Recurse }
}
if ($Configuration -ne 'Release' -and $Pdb -ne '' -and (Test-Path -LiteralPath $Pdb)) { Copy-Item -LiteralPath $Pdb -Destination $stageDir }
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $repoRoot 'assets') -Recurse -File) {
    if ($file.Name -like 'dev-*.json' -or $file.FullName -like '*\patches-disabled-bugrace\*') { continue }
    $relative = $file.FullName.Substring($repoRoot.Length + 1)
    $destination = Join-Path $stageDir $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $destination
}
$dllHash = if ($manifest.type -eq 'code') { (Get-FileHash -LiteralPath $Dll -Algorithm SHA256).Hash.ToLowerInvariant() } else { $null }
$sourceDirectory = if (Test-Path -LiteralPath (Join-Path $repoRoot 'src')) { Join-Path $repoRoot 'src' } else { Join-Path $repoRoot 'assets' }
$identity = [ordered]@{
    modid = $modId; version = $version; sha = $sourceIdentity.sha; shortSha = $sourceIdentity.shortSha; dirty = $sourceIdentity.dirty
    sourceRevision = $sourceIdentity.sha; sourceDirty = $sourceIdentity.dirty; gitTree = $sourceIdentity.tree
    sourceTreeSha256 = (Hash-Tree $sourceDirectory); dllSha256 = $dllHash; configuration = $Configuration
}
foreach ($name in @('build-info.json','build-stamp.json')) {
    [IO.File]::WriteAllText((Join-Path $stageDir $name), ($identity | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
}

if ($Configuration -eq 'Release') {
    $artifacts = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repoRoot 'artifacts' }
    New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
    $zipPath = Join-Path $artifacts "${modId}_${version}.zip"
    if (Test-Path -LiteralPath $zipPath) {
        if ((Hash-Zip $zipPath) -ne (Hash-Tree $stageDir)) { throw "Version $version already has different content (including DLL). Choose a new prerelease version." }
        Write-Host "Reusing identical package $zipPath"
    } else {
        # Explicit forward slashes keep asset paths valid on Linux servers.
        $zip = [IO.Compression.ZipFile]::Open($zipPath, 'Create')
        try {
            foreach ($file in Get-ChildItem -LiteralPath $stageDir -Recurse -File) {
                $relative = $file.FullName.Substring($stageDir.Length + 1).Replace('\','/')
                [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $relative)
            }
        } finally { $zip.Dispose() }
    }
    $zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $names = @($zip.Entries | ForEach-Object { $_.FullName })
        if ($names | Where-Object { $_.Contains('\') -or $_.StartsWith('/') -or $_.Split('/') -contains '..' }) { throw 'Unsafe archive entry name' }
        if ($names | Group-Object | Where-Object Count -gt 1) { throw 'Duplicate archive entry' }
        $requiredEntries = @('modinfo.json', 'README.md', 'LICENSE', 'CREDITS.md', 'THIRD_PARTY_NOTICES.md', 'CHANGELOG.md', 'build-info.json', 'build-stamp.json')
        if ($manifest.type -eq 'code') { $requiredEntries += "$modId.dll"; $requiredEntries += 'licenses/VintageStory-source.txt' }
        if ($modId -eq 'dietsetup') { $requiredEntries += 'ModConfig-examples/bindings.json.example' }
        if ($modId -eq 'rfmechanics') { $requiredEntries += 'licenses/Spyglass-MIT.txt' }
        foreach ($required in $requiredEntries) {
            if ($names -notcontains $required) { throw "Missing $required" }
        }
        if (-not ($names | Where-Object { $_.StartsWith("assets/$modId/") })) { throw 'Missing assets' }
        if ($names | Where-Object { $_ -match '(^|/)dev-.*\.json$|\.pdb$|hydrateordiedrate' }) { throw 'Development or unsupported content in release' }
    } finally { $zip.Dispose() }
    $checksum = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText("$zipPath.sha256", "$checksum  $([IO.Path]::GetFileName($zipPath))`n")
    Write-Host "Packaged $zipPath SHA256=$checksum"
    if ($StagingPath -ne '') {
        New-Item -ItemType Directory -Path $StagingPath -Force | Out-Null
        $destination = Join-Path $StagingPath ([IO.Path]::GetFileName($zipPath))
        if ((Test-Path -LiteralPath $destination) -and (Get-FileHash -LiteralPath $destination).Hash.ToLowerInvariant() -ne $checksum) { throw 'Staging already contains a different archive for this version' }
        Copy-Item -LiteralPath $zipPath -Destination $destination -Force
        Copy-Item -LiteralPath "$zipPath.sha256" -Destination "$destination.sha256" -Force
    }
}
if ($DeployPath -ne '') {
    $DeployPath = [IO.Path]::GetFullPath($DeployPath).TrimEnd('\','/')
    if ([IO.Path]::GetFileName($DeployPath) -ne $modId) { throw 'DeployPath must be a dedicated directory named for this mod ID' }
    if (Test-Path -LiteralPath $DeployPath) {
        $existing = @(Get-ChildItem -LiteralPath $DeployPath -Recurse -Force)
        if (((Get-Item -LiteralPath $DeployPath).Attributes -band [IO.FileAttributes]::ReparsePoint) -or ($existing | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint })) { throw 'Resolve mod-directory links before installation' }
        foreach ($file in $existing | Where-Object { -not $_.PSIsContainer }) {
            $handle = [IO.File]::Open($file.FullName, 'Open', 'ReadWrite', 'None')
            $handle.Dispose()
        }
    }
    New-Item -ItemType Directory -Path $DeployPath -Force | Out-Null
    $staged = @{}
    foreach ($file in Get-ChildItem -LiteralPath $stageDir -Recurse -File) {
        $relative = $file.FullName.Substring($stageDir.Length + 1); $staged[$relative] = $true
        $destination = Join-Path $DeployPath $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
    }
    foreach ($file in Get-ChildItem -LiteralPath $DeployPath -Recurse -File) {
        if (-not $staged.ContainsKey($file.FullName.Substring($DeployPath.Length + 1))) {
            Assert-ChildPath $file.FullName $DeployPath
            Remove-Item -LiteralPath $file.FullName -Force
        }
    }
    if ((Hash-Tree $DeployPath) -ne (Hash-Tree $stageDir)) { throw 'Client verification failed; stop before server upload' }
    Write-Host "Deployed and verified on disk: $DeployPath. Relaunch a running client before joining."
}
