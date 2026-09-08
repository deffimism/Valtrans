param([string]$AssemblyPath = "$PSScriptRoot/../publish/Valtrans.dll")
$ErrorActionPreference = 'Stop'
Add-Type -Path (Resolve-Path -LiteralPath $AssemblyPath)
function Check($condition, $message) { if (-not $condition) { throw $message } }
$root = Split-Path $PSScriptRoot -Parent
$tokens = $null
$errors = $null
[void][Management.Automation.Language.Parser]::ParseFile("$root/Ocr/Setup.ps1", [ref]$tokens, [ref]$errors)
Check ($errors.Count -eq 0) 'Installer syntax error'
$installer = [IO.File]::ReadAllText("$root/Ocr/Setup.ps1")
foreach ($required in @('UV_UNMANAGED_INSTALL','UV_NO_MODIFY_PATH','https://astral.sh/uv/install.ps1','-TimeoutSec 60')) {
    Check ($installer.Contains($required)) "Missing private uv bootstrap constraint: $required"
}
$main = [IO.File]::ReadAllText("$root/MainWindow.xaml.cs")
Check ($main -match 'var ocrReady = await PreparePaddleSetupAsync\(\)') 'Recommended setup does not prepare OCR'
Check ($main -match 'if \(!await InstallPaddleEnvironmentAsync\(\)\) return false;\s+return await PreparePaddleOcrAsync\(\);') 'Installer failure can fall through to warmup'
Write-Output 'PASS: installer syntax, private uv bootstrap contract, recommended setup orchestration'

# A controlled installer fixture exercises the real process wrapper without downloads.
$fixture = Join-Path $root ('artifacts/tests/ocr-setup-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($fixture)
$hostFile = Join-Path $fixture 'paddle_host.py'
[IO.File]::WriteAllText($hostFile, '# fixture; model host is not started')
$fakeInstaller = @'
param([string]$RuntimeDirectory)
$ErrorActionPreference = 'Stop'
$scenario = Split-Path $RuntimeDirectory -Leaf
if ($scenario -eq 'failure') { throw 'uv installation failed' }
if ($scenario -eq 'cancel') { Start-Sleep -Seconds 30 }
Write-Output 'Installation complete'
'@
[IO.File]::WriteAllText((Join-Path $fixture 'Setup.ps1'), $fakeInstaller)
$service = [Valtrans.Services.PaddleOcrService]::new($hostFile)
$progress = [Progress[string]]::new()
try {
    $service.InstallAsync('success', $progress, [Threading.CancellationToken]::None).GetAwaiter().GetResult()
    Check ($service.Status.StartsWith('설치 완료')) 'Successful installation was not reported'
    Check (-not $service.IsReady) 'Files installed incorrectly reported as loaded model'
    $failureCaught = $false
    try { $service.InstallAsync('failure', $progress, [Threading.CancellationToken]::None).GetAwaiter().GetResult() }
    catch { $failureCaught = $_.Exception.ToString().Contains('설치 도구 다운로드 실패') }
    Check $failureCaught 'Installer error did not reach the user-facing message'
    $cancel = [Threading.CancellationTokenSource]::new()
    try {
        $cancel.CancelAfter(250)
        $cancelled = $false
        $watch = [Diagnostics.Stopwatch]::StartNew()
        try { $service.InstallAsync('cancel', $progress, $cancel.Token).GetAwaiter().GetResult() }
        catch { $cancelled = $_.Exception.ToString() -match 'TaskCanceledException|OperationCanceledException' }
        Check ($cancelled -and $watch.Elapsed.TotalSeconds -lt 10) 'Installer did not cancel promptly'
    } finally { $cancel.Dispose() }
    $service.InstallAsync('success', $progress, [Threading.CancellationToken]::None).GetAwaiter().GetResult()
    Check ($service.Status.StartsWith('설치 완료')) 'Retry after cancellation failed'
    Write-Output 'PASS: real installer wrapper success/failure/cancel/retry (fixture, no model downloads)'
} finally { $service.Dispose() }
