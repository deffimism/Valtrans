param(
    [string]$Version = '0.4.0-beta',
    [string]$Tag = 'v0.4.0-beta'
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot/.."
$zip = Join-Path $root "releases/v$Version.zip"
$notesPath = Join-Path $root "docs/release-notes/v$Version.md"
if (-not (Test-Path -LiteralPath $zip)) {
    & "$PSScriptRoot/Package-Release.ps1" -Version $Version
}
if (-not (Test-Path -LiteralPath $notesPath)) {
    throw "Release notes not found (UTF-8 markdown): $notesPath"
}
$gh = Get-Command gh -ErrorAction SilentlyContinue
if (-not $gh) { throw 'GitHub CLI (gh) not found. Install: winget install GitHub.cli' }
& gh auth status | Out-Null
git -C $root push origin master

$releaseExists = $false
try {
    & gh release view $Tag --repo deffimism/Valtrans 2>$null | Out-Null
    $releaseExists = $true
} catch {
    $releaseExists = $false
}

if ($releaseExists) {
    & gh release edit $Tag `
        --repo deffimism/Valtrans `
        --notes-file $notesPath
    Write-Output "GitHub release $Tag notes updated."
} else {
    & gh release create $Tag $zip `
        --repo deffimism/Valtrans `
        --title "Valtrans $Tag" `
        --notes-file $notesPath
    Write-Output "GitHub release $Tag published."
}
