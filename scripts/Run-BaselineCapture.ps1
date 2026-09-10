param(
    [string]$Scenario = "$PSScriptRoot/../testdata/scenarios/smoke_basic_001.json",
    [string]$OutputPath = "$PSScriptRoot/../artifacts/baseline-v0.2.3-windows.json",
    [string]$OcrEngine = "Windows",
    [int]$Seed = 20260910,
    [int]$Timeout = 180
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot/.."
. "$PSScriptRoot/Resolve-ValtransBuild.ps1"
dotnet build "$root/Valtrans.csproj" -c Release | Out-Null
dotnet build "$root/test/TestArena/Valtrans.TestArena.csproj" -c Release | Out-Null
dotnet build "$root/test/TestRunner/Valtrans.TestRunner.csproj" -c Release | Out-Null
$runner = Get-ValtransTestRunner -Root $root
$scenarioPath = Resolve-Path -LiteralPath $Scenario
$baselinePath = [IO.Path]::GetFullPath($OutputPath)
$dir = Split-Path -Parent $baselinePath
if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
& $runner --scenario $scenarioPath --seed $Seed --timeout $Timeout --ocr-engine $OcrEngine --baseline-output $baselinePath
if ($LASTEXITCODE -ne 0) { throw "Baseline capture failed with exit code $LASTEXITCODE" }
if (-not (Test-Path -LiteralPath $baselinePath)) { throw "Baseline file missing: $baselinePath" }
Write-Output "Baseline captured: $baselinePath"
