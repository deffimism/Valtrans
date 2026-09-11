# Measures JP->KO Lite pivot cost vs direct EN->KO when Valtrans Lite is installed.
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    dotnet test test\Valtrans.Tests\Valtrans.Tests.csproj -c Release --nologo `
        --filter "FullyQualifiedName~LitePivotBenchmarkTests" `
        --logger "console;verbosity=detailed"
    $report = Join-Path $env:TEMP "valtrans-lite-pivot-benchmark.txt"
    if (Test-Path $report) {
        Write-Host ""
        Write-Host "---- benchmark report ----"
        Get-Content $report -Encoding UTF8
    } else {
        Write-Host "Lite not installed or benchmark skipped — no report written."
    }
}
finally {
    Pop-Location
}
