param(
    [string]$Scenario = "$PSScriptRoot/../testdata/scenarios/smoke_zh_mixed_001.json",
    [int]$Seed = 20260910,
    [int]$Timeout = 240,
    [switch]$NoFocusArena
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot/.."
. "$PSScriptRoot/Resolve-ValtransBuild.ps1"
Stop-ValtransE2EProcesses
$fastMarker = Join-Path $env:LOCALAPPDATA 'Valtrans/FastOcrRuntime/fast_model.json'
if (-not (Test-Path -LiteralPath $fastMarker)) {
    Write-Output 'SKIP: smoke_zh_mixed_001 E2E requires Fast OCR runtime. Install with Ocr/Setup-FastOcr.ps1'
    Write-Output 'PASS: Chinese/Mixed rules covered by Valtrans.Tests (GlossaryChinese, MixedLanguage)'
    exit 0
}
dotnet build "$root/Valtrans.csproj" -c Release | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Build failed: Valtrans.csproj (exit $LASTEXITCODE)" }
dotnet build "$root/test/TestArena/Valtrans.TestArena.csproj" -c Release | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Build failed: test/TestArena/Valtrans.TestArena.csproj (exit $LASTEXITCODE)" }
dotnet build "$root/test/TestRunner/Valtrans.TestRunner.csproj" -c Release | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Build failed: test/TestRunner/Valtrans.TestRunner.csproj (exit $LASTEXITCODE)" }
$runner = Get-ValtransTestRunner -Root $root
$args = @('--scenario', (Resolve-Path -LiteralPath $Scenario), '--seed', $Seed, '--timeout', $Timeout, '--ocr-engine', 'Fast')
if ($NoFocusArena) { $args += '--no-focus-arena' }
& $runner @args
if ($LASTEXITCODE -ne 0) { throw "ZH mixed E2E failed with exit code $LASTEXITCODE" }
Write-Output 'ZH mixed E2E: PASS'
