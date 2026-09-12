param(
    [string]$Version = '0.5.0-beta',
    [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{40}$')][string]$TargetCommit,
    [switch]$Publish,
    [switch]$CheckOnly
)
$ErrorActionPreference = 'Stop'
if ($Publish -and $CheckOnly) { throw 'Use either CheckOnly or Publish, not both' }
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$') { throw 'Invalid version' }
$root = (Resolve-Path "$PSScriptRoot/..").Path
$tag = "v$Version"
$repo = 'deffimism/Valtrans'
# This tool never commits, pushes, force-tags or edits an existing release.
$currentCommit = & git -C $root rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $currentCommit -ne $TargetCommit) { throw 'Target must equal local HEAD' }
$changes = @(& git -C $root status --porcelain)
if ($LASTEXITCODE -ne 0 -or $changes.Count -ne 0) { throw 'Commit/review source changes before packaging a release' }
$notesPath = Join-Path $root "docs/release-notes/v$Version.md"
if (-not (Test-Path -LiteralPath $notesPath -PathType Leaf)) { throw 'Release notes missing' }
$assets = @()
foreach ($name in @("v$Version.zip", 'valtrans-lite-ko-en-opus-20220728-int8.zip')) {
    $path = Join-Path $root "releases/$name"
    $shaPath = "$path.sha256"
    $expected = ((Get-Content -LiteralPath $shaPath -Raw).Trim() -split '\s+')[0]
    if ($expected -notmatch '^[a-fA-F0-9]{64}$' -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $expected) {
        throw "Asset hash mismatch: $name"
    }
    $assets += @($path, $shaPath)
}
# Missing provenance means rebuild after the source commit, not infer it from a filename.
$archive = [IO.Compression.ZipFile]::OpenRead($assets[0])
try {
    $entry = $archive.GetEntry('build-manifest.json')
    if ($null -eq $entry) { throw 'Build manifest missing' }
    $reader = [IO.StreamReader]::new($entry.Open())
    try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
    if ($manifest.version -ne $Version -or $manifest.sourceCommit -ne $TargetCommit -or
        $manifest.sourceDirty -ne $false -or
        $manifest.productVersion -notlike "*+$TargetCommit*") {
        throw 'Package is not from this clean source commit; rebuild first'
    }
} finally { $archive.Dispose() }
& "$PSScriptRoot/Verify-ReleasePackages.ps1" -Version $Version
$gh = (Get-Command gh -ErrorAction SilentlyContinue).Source
if (-not $gh -and (Test-Path 'C:/Program Files/GitHub CLI/gh.exe')) {
    $gh = 'C:/Program Files/GitHub CLI/gh.exe'
}
if (-not $gh) { throw 'GitHub CLI not found' }
function Invoke-GitHub {
    param([string[]]$Arguments)
    $result = & $gh @Arguments
    if ($LASTEXITCODE -ne 0) { throw "GitHub command failed (exit $LASTEXITCODE); no blind retry" }
    return $result
}
$remoteCommit = Invoke-GitHub -Arguments @('api', "repos/$repo/commits/$TargetCommit", '--jq', '.sha')
if ($remoteCommit -ne $TargetCommit) { throw 'Source commit must already exist in the target repository' }
$existing = Invoke-GitHub -Arguments @('release', 'list', '--repo', $repo, '--limit', '1000', '--json', 'tagName,isDraft') | ConvertFrom-Json
if (@($existing | Where-Object tagName -eq $tag).Count -gt 0) {
    throw 'Release already exists; inspect it explicitly instead of replacing assets or notes'
}
if ($CheckOnly) {
    Write-Output "CHECKED: $tag at $TargetCommit; no GitHub writes"
    return
}
$createArgs = @('release', 'create', $tag) + $assets +
    @('--repo', $repo, '--target', $TargetCommit, '--title', "Valtrans $tag",
      '--notes-file', $notesPath, '--draft', '--prerelease')
Invoke-GitHub -Arguments $createArgs
$created = Invoke-GitHub -Arguments @('release', 'view', $tag, '--repo', $repo, '--json', 'isDraft,isPrerelease,assets,url') | ConvertFrom-Json
$wantedNames = @($assets | ForEach-Object { [IO.Path]::GetFileName($_) })
$actualNames = @($created.assets | ForEach-Object name)
if (-not $created.isDraft -or -not $created.isPrerelease -or
    @(Compare-Object $wantedNames $actualNames).Count -ne 0) {
    throw 'Draft asset verification failed; leave the draft unpublished for inspection'
}
# Verify bytes served by GitHub, not merely the uploaded filenames. A failed
# check leaves a recoverable draft; it must never proceed to publication.
$downloadDirectory = Join-Path $root ('.deployment/github-draft-' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $downloadDirectory)
Invoke-GitHub -Arguments @('release', 'download', $tag, '--repo', $repo, '--dir', $downloadDirectory)
foreach ($asset in $assets) {
    $downloaded = Join-Path $downloadDirectory ([IO.Path]::GetFileName($asset))
    if (-not (Test-Path -LiteralPath $downloaded -PathType Leaf) -or
        (Get-FileHash -LiteralPath $downloaded -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash) {
        throw 'Downloaded draft asset hash mismatch; leave the draft unpublished for inspection'
    }
}
Write-Output "DRAFT DOWNLOAD VERIFIED: $($assets.Count) assets; SHA256 matched"
if ($Publish) {
    Invoke-GitHub -Arguments @('release', 'edit', $tag, '--repo', $repo, '--draft=false', '--prerelease', '--latest=false')
    $published = Invoke-GitHub -Arguments @('release', 'view', $tag, '--repo', $repo, '--json', 'isDraft,isPrerelease,url') | ConvertFrom-Json
    if ($published.isDraft -or -not $published.isPrerelease) { throw 'Publication status was not verified' }
    Write-Output "PUBLISHED prerelease: $($published.url)"
} else {
    Write-Output "DRAFT ONLY: $($created.url). Public downloads are not available yet."
}
