param(
    [string]$Scenario = "$PSScriptRoot/../testdata/scenarios/smoke_basic_001.json",
    [int]$Seed = 20260910,
    [int]$Timeout = 180,
    [switch]$NoFocusArena
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot/.."
. "$PSScriptRoot/Resolve-ValtransBuild.ps1"
Stop-ValtransE2EProcesses
dotnet build "$root/Valtrans.csproj" -c Release | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Build failed: Valtrans.csproj (exit $LASTEXITCODE)" }
dotnet build "$root/test/TestArena/Valtrans.TestArena.csproj" -c Release | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Build failed: test/TestArena/Valtrans.TestArena.csproj (exit $LASTEXITCODE)" }
dotnet build "$root/test/TestRunner/Valtrans.TestRunner.csproj" -c Release | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Build failed: test/TestRunner/Valtrans.TestRunner.csproj (exit $LASTEXITCODE)" }
$runner = Get-ValtransTestRunner -Root $root
$args = @('--scenario', (Resolve-Path -LiteralPath $Scenario), '--seed', $Seed, '--timeout', $Timeout)
if ($NoFocusArena) { $args += '--no-focus-arena' }
& $runner @args
if ($LASTEXITCODE -ne 0) { throw "E2E smoke failed with exit code $LASTEXITCODE" }
Write-Output 'E2E smoke: PASS'
