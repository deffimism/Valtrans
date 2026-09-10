param(
    [Parameter(Mandatory)][string]$BaselinePath,
    [Parameter(Mandatory)][string]$CurrentPath,
    [double]$LatencyWarningPercent = 10,
    [double]$LatencyFailPercent = 20
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot/.."
. "$PSScriptRoot/Resolve-ValtransBuild.ps1"
dotnet build "$root/test/TestRunner/Valtrans.TestRunner.csproj" -c Release | Out-Null
$runner = Get-ValtransTestRunner -Root $root
& $runner --compare-baseline (Resolve-Path -LiteralPath $BaselinePath) --current-baseline (Resolve-Path -LiteralPath $CurrentPath)
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Output "Compare-Baseline: PASS"
