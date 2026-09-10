param(
    [string]$Scenario = "$PSScriptRoot/../testdata/scenarios/smoke_basic_001.json",
    [int]$Seed = 20260910,
    [int]$Timeout = 180
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot/.."
$artifactDir = Join-Path $root 'artifacts/ocr-comparison'
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null
$windowsBaseline = Join-Path $artifactDir 'windows-baseline.json'
$fastBaseline = Join-Path $artifactDir 'fast-baseline.json'
& "$PSScriptRoot/Run-BaselineCapture.ps1" -Scenario $Scenario -OutputPath $windowsBaseline -OcrEngine Windows -Seed $Seed -Timeout $Timeout
& "$PSScriptRoot/Run-BaselineCapture.ps1" -Scenario $Scenario -OutputPath $fastBaseline -OcrEngine Fast -Seed $Seed -Timeout $Timeout
$assemblyDir = Resolve-Path "$root/bin/Release/net10.0-windows10.0.26100.0"
$resolve = {
    param($sender, $eventArgs)
    $name = ($eventArgs.Name -split ',')[0]
    $candidate = Join-Path $assemblyDir "$name.dll"
    if (Test-Path -LiteralPath $candidate) { return [Reflection.Assembly]::LoadFrom($candidate) }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolve)
[void][Reflection.Assembly]::LoadFrom((Join-Path $assemblyDir 'Valtrans.dll'))
$windows = [Valtrans.Services.BaselineReportService]::Load($windowsBaseline)
$fast = [Valtrans.Services.BaselineReportService]::Load($fastBaseline)
$report = @{
    scenario = $Scenario
    seed = $Seed
    windows = @{
        status = $windows.E2EStatus
        passRate = $windows.Accuracy.CasePassRate
        ocrP50Ms = $windows.Latency.OcrP50Ms
        e2eP50Ms = $windows.Latency.E2EP50Ms
        cases = $windows.Cases
    }
    fast = @{
        status = $fast.E2EStatus
        passRate = $fast.Accuracy.CasePassRate
        ocrP50Ms = $fast.Latency.OcrP50Ms
        e2eP50Ms = $fast.Latency.E2EP50Ms
        cases = $fast.Cases
    }
    completedUtc = (Get-Date).ToUniversalTime().ToString('o')
}
$reportPath = Join-Path $artifactDir 'comparison-report.json'
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding UTF8
Write-Output "OCR comparison written: $reportPath"
Write-Output ($report | ConvertTo-Json -Depth 6)
