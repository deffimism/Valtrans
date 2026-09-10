param(
    [string]$ArenaProject = "$PSScriptRoot/../test/TestArena/Valtrans.TestArena.csproj",
    [string]$Scenario = "$PSScriptRoot/../testdata/scenarios/smoke_basic_001.json"
)
$ErrorActionPreference = 'Stop'
dotnet build (Resolve-Path -LiteralPath $ArenaProject) -c Release | Out-Null
if (-not (Test-Path -LiteralPath $Scenario)) { throw "Scenario not found: $Scenario" }
$scenarioPath = Resolve-Path -LiteralPath $Scenario
$arenaDll = Resolve-Path "$PSScriptRoot/../test/TestArena/bin/Release/net10.0-windows10.0.26100.0/Valtrans.TestArena.dll"
if (-not (Test-Path -LiteralPath $arenaDll)) { throw "TestArena build output missing" }
Write-Output "TestArena build: PASS"
Write-Output "Scenario: $scenarioPath"
