param(
    [string]$Version = '0.4.0-beta',
    [string]$Tag = 'v0.4.0-beta'
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
## v0.4.0-beta

### OCR · capture
- Test Arena mirrors VALORANT chat layout (bottom-aligned, inline speaker line); E2E uses production latest-line crop
- Non-16:9 resolutions: chat region scales from height×16:9 basis (16:9 unchanged)
- Alt-tab refocus: resolution/profile change detection preserved when game loses foreground
- Windows OCR: bilinear upscale + 3× enhancement for small crops; Japanese advisory → prefer Fast/Hybrid
- OCR test supports all selected engines (Windows / Fast / Hybrid / Paddle)
- OCR noise heuristics, glossary cleanup, regex cache (~10× faster callout matching)

### UX
- Overlay shows skip reason when a message is not translated
- OCR issue panel: **OCR 다시 준비** retry button
- Windows OCR + JP language-pack guidance in startup guide

### Translation
- Lite JP↔KO uses EN pivot (max 2 model calls); ``Benchmark-LitePivot.ps1`` for measurement
- Message classifier uses game-context categorization (not all-mode)

### Quality gate
- 156 unit tests; Full profile E2E smoke (6/6 VALORANT callouts)

### Verify

``````powershell
.\scripts\test.ps1 -Profile Full
``````
"@
Write-Output "GitHub release $Tag published."
