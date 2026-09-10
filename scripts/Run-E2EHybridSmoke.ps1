# Default: smoke_zh_mixed_001 (Fast/Hybrid OCR). Use smoke_basic_001 for Windows OCR via Run-E2ESmoke.ps1.
param(
    [string]$Scenario = "$PSScriptRoot/../testdata/scenarios/smoke_zh_mixed_001.json",
    [int]$Seed = 20260910,
    [int]$Timeout = 420,
    [switch]$NoFocusArena
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot/.."
. "$PSScriptRoot/Resolve-ValtransBuild.ps1"
Stop-ValtransE2EProcesses
$fastMarker = Join-Path $env:LOCALAPPDATA 'Valtrans/FastOcrRuntime/fast_model.json'
$paddleMarker = Join-Path $env:LOCALAPPDATA 'Valtrans/OcrRuntime/model.json'
if (-not (Test-Path -LiteralPath $fastMarker) -or -not (Test-Path -LiteralPath $paddleMarker)) {
    Write-Output 'SKIP: Hybrid E2E requires Fast OCR + Paddle VL runtimes.'
    exit 0
}
dotnet build "$root/Valtrans.csproj" -c Release | Out-Null
dotnet build "$root/test/TestArena/Valtrans.TestArena.csproj" -c Release | Out-Null
dotnet build "$root/test/TestRunner/Valtrans.TestRunner.csproj" -c Release | Out-Null
$runner = Get-ValtransTestRunner -Root $root
$args = @(
    '--scenario', (Resolve-Path -LiteralPath $Scenario),
    '--seed', $Seed,
    '--timeout', $Timeout,
    '--ocr-engine', 'Hybrid'
)
if ($NoFocusArena) { $args += '--no-focus-arena' }
& $runner @args
if ($LASTEXITCODE -ne 0) { throw "Hybrid E2E failed with exit code $LASTEXITCODE" }
Write-Output 'Hybrid E2E: PASS'
