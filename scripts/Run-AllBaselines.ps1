param(
    [int]$Seed = 20260910,
    [int]$Timeout = 300
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot/.."
$artifacts = Join-Path $root 'artifacts'
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null

& "$PSScriptRoot/Run-BaselineCapture.ps1" `
    -Scenario "$root/testdata/scenarios/smoke_basic_001.json" `
    -OutputPath "$artifacts/baseline-v0.2.3-windows.json" `
    -OcrEngine Windows -Seed $Seed -Timeout $Timeout

$fastMarker = Join-Path $env:LOCALAPPDATA 'Valtrans/FastOcrRuntime/fast_model.json'
if (Test-Path -LiteralPath $fastMarker) {
    & "$PSScriptRoot/Run-BaselineCapture.ps1" `
        -Scenario "$root/testdata/scenarios/smoke_zh_mixed_001.json" `
        -OutputPath "$artifacts/baseline-v0.3.0-zh-fast.json" `
        -OcrEngine Fast -Seed $Seed -Timeout $Timeout
    Write-Output 'ZH Fast baseline: PASS'
} else {
    Write-Output 'SKIP: baseline-v0.3.0-zh-fast.json (Fast OCR runtime missing)'
}

Write-Output 'Run-AllBaselines: PASS'
