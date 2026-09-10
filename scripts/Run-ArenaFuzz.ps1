param(
    [string]$Scenario = "$PSScriptRoot/../testdata/scenarios/arena_fuzz_001.json",
    [int[]]$Seeds = @(20260910, 20260911, 20260912),
    [switch]$RunE2E,
    [switch]$NoFocusArena,
    [int]$Timeout = 240
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot/.."
. "$PSScriptRoot/Resolve-ValtransBuild.ps1"
Stop-ValtransE2EProcesses
$scenarioPath = Resolve-Path -LiteralPath $Scenario

dotnet build "$root/test/TestArena/Valtrans.TestArena.csproj" -c Release | Out-Null
dotnet test "$root/test/Valtrans.Tests/Valtrans.Tests.csproj" -c Release --filter "FullyQualifiedName~Arena"
if ($LASTEXITCODE -ne 0) { throw 'Arena helper unit tests failed' }

$arenaExe = Get-ValtransTestArena -Root $root
foreach ($seed in $Seeds) {
    Write-Output "validate seed=$seed"
    & $arenaExe --scenario $scenarioPath --seed $seed --validate-only | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Arena validate-only failed for seed $seed" }
}
Write-Output "Arena fuzz validation: PASS ($($Seeds.Count) seeds)"

if ($RunE2E) {
    dotnet build "$root/Valtrans.csproj" -c Release | Out-Null
    dotnet build "$root/test/TestRunner/Valtrans.TestRunner.csproj" -c Release | Out-Null
    $runner = Get-ValtransTestRunner -Root $root
    foreach ($seed in $Seeds) {
        Write-Output "E2E fuzz seed=$seed"
        $runnerArgs = @('--scenario', $scenarioPath, '--seed', $seed, '--timeout', $Timeout)
        if ($NoFocusArena) { $runnerArgs += '--no-focus-arena' }
        & $runner @runnerArgs
        if ($LASTEXITCODE -ne 0) { throw "Arena fuzz E2E failed for seed $seed" }
    }
    Write-Output "Arena fuzz E2E: PASS ($($Seeds.Count) seeds)"
}
