param(
    [string]$AssemblyPath = "$PSScriptRoot/../bin/Release/net10.0-windows10.0.26100.0/Valtrans.dll",
    [string]$RuntimePath = "$PSScriptRoot/../artifacts/ocr-vl",
    [string]$ImagePath = "$PSScriptRoot/../artifacts/ocr-vl/results/inputs/broadcast-mixed.png"
)
$ErrorActionPreference = 'Stop'
$assembly = (Resolve-Path -LiteralPath $AssemblyPath).Path
Add-Type -Path $assembly
# The test runner's BaseDirectory is PowerShell, so pass the packaged host location
# using the service's explicit test-only constructor override.
$service = [Valtrans.Services.PaddleOcrService]::new((Join-Path (Split-Path $assembly) 'Ocr/paddle_host.py'))
try {
    $token = [Threading.CancellationToken]::None
    $service.PrepareAsync((Resolve-Path $RuntimePath).Path,$token).GetAwaiter().GetResult()
    Write-Output $service.Status
    $png = [IO.File]::ReadAllBytes((Resolve-Path $ImagePath).Path)
    $result = $service.ReadAsync($png,(Resolve-Path $RuntimePath).Path,$token).GetAwaiter().GetResult()
    if (-not $result.Text.Contains('밴달') -or -not $result.Text.Contains('ミッド 2') -or -not $result.Text.Contains('mid 2')) { throw "Mixed OCR regression: $($result.Text)" }
    $bodies = [Valtrans.Services.OcrMessageParser]::Extract($result)
    if ($bodies.Count -ne 3) { throw "Expected three chat bodies, got $($bodies.Count)" }
    Write-Output "PASS: real local OCR IPC => $($bodies.Body -join ' / ') ($($result.RecognitionDurationMs)ms)"
    $cancel = [Threading.CancellationTokenSource]::new()
    try {
        $cancel.CancelAfter(100)
        $cancelled = $false
        try { $service.ReadAsync($png,(Resolve-Path $RuntimePath).Path,$cancel.Token).GetAwaiter().GetResult() | Out-Null }
        catch { $cancelled = $cancel.IsCancellationRequested }
        if (-not $cancelled) { throw 'Cancellation did not interrupt host' }
    } finally { $cancel.Dispose() }
    $service.PrepareAsync((Resolve-Path $RuntimePath).Path,$token).GetAwaiter().GetResult()
    Write-Output 'PASS: cancelled request kills owned host, next preparation succeeds'
} finally { $service.Dispose() }
