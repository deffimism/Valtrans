param(
    [string]$TraceLog = "$env:LOCALAPPDATA\Valtrans\Logs\message-traces.jsonl",
    [string]$OutputPath = "$PSScriptRoot/../artifacts/baseline-v0.2.3.json"
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $TraceLog)) { throw "Trace log not found: $TraceLog" }
$records = @()
Get-Content -LiteralPath $TraceLog -Encoding UTF8 | ForEach-Object {
    if ([string]::IsNullOrWhiteSpace($_)) { return }
    try { $records += ($_ | ConvertFrom-Json) } catch { }
}
if ($records.Count -eq 0) { throw 'No trace records found' }
$latencies = $records | Where-Object { $_.latency.totalMs } | ForEach-Object { [double]$_.latency.totalMs }
$ocrLatencies = $records | Where-Object { $_.ocr.latencyMs } | ForEach-Object { [double]$_.ocr.latencyMs }
$translationLatencies = $records | Where-Object { $_.translation.latencyMs } | ForEach-Object { [double]$_.translation.latencyMs }
function Get-Percentile($values, $p) {
    if ($values.Count -eq 0) { return $null }
    $sorted = $values | Sort-Object
    $index = [int][math]::Round(($sorted.Count - 1) * $p)
    return [math]::Round($sorted[[math]::Max(0, [math]::Min($index, $sorted.Count - 1))], 1)
}
$baseline = @{
    capturedUtc = (Get-Date).ToUniversalTime().ToString('o')
    source = 'message-traces.jsonl'
    recordCount = $records.Count
    ocr = @{
        p50Ms = Get-Percentile $ocrLatencies 0.5
        p95Ms = Get-Percentile $ocrLatencies 0.95
    }
    translation = @{
        p50Ms = Get-Percentile $translationLatencies 0.5
        p95Ms = Get-Percentile $translationLatencies 0.95
    }
    e2e = @{
        p50Ms = Get-Percentile $latencies 0.5
        p95Ms = Get-Percentile $latencies 0.95
    }
}
$dir = Split-Path -Parent $OutputPath
if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
$baseline | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
Write-Output "Baseline written: $OutputPath"
