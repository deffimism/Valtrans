param(
    [string]$Version = '0.3.0-beta',
    [string]$Tag = 'v0.3.0-beta'
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot/.."
$zip = Join-Path $root "releases/v$Version.zip"
if (-not (Test-Path -LiteralPath $zip)) {
    & "$PSScriptRoot/Package-Release.ps1" -Version $Version
}
$gh = Get-Command gh -ErrorAction SilentlyContinue
if (-not $gh) { throw 'GitHub CLI (gh) not found. Install: winget install GitHub.cli' }
& gh auth status | Out-Null
git -C $root push origin master
& gh release create $Tag $zip `
    --repo deffimism/Valtrans `
    --title "Valtrans $Tag" `
    --notes @"
## v0.3.0-beta

- Test Arena E2E (Capture→OCR) + Agent TestRunner + test.ps1 regression gate
- Fast OCR (PP-OCRv5) + Hybrid OCR (Fast + Paddle VL fallback)
- MessageTrace, baseline capture/compare, Chinese/Mixed glossary
- Translation cache, latest-frame-wins queue, critical fact validator
- Arena layout persistence, --no-focus-arena for dev workflow

### Verify

``````powershell
.\scripts\test.ps1 -Profile Full
``````
"@
Write-Output "GitHub release $Tag published."
