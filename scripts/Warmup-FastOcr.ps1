param([string]$RuntimeDirectory = "$env:LOCALAPPDATA\Valtrans\FastOcrRuntime")
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot/.."
$python = Join-Path $RuntimeDirectory '.venv\Scripts\python.exe'
$hostScript = Join-Path $root 'Ocr\fast_ocr_host.py'
if (-not (Test-Path -LiteralPath $python)) { throw "Fast OCR python missing: $python" }
Write-Output 'Warming PP-OCRv5 (first run may download models)...'
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $python
$psi.Arguments = "`"$hostScript`" --runtime `"$RuntimeDirectory`""
$psi.UseShellExecute = $false
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$proc = [System.Diagnostics.Process]::Start($psi)
$ready = $proc.StandardOutput.ReadLine()
Write-Output "host: $ready"
$pngPath = Join-Path $env:TEMP 'valtrans-fast-ocr-warmup.png'
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap 120, 40
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::Black)
$font = New-Object System.Drawing.Font('Segoe UI', 14)
$g.DrawString('WARMUP', $font, [System.Drawing.Brushes]::White, 4, 8)
$g.Dispose()
$bmp.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
$bytes = [IO.File]::ReadAllBytes($pngPath)
$b64 = [Convert]::ToBase64String($bytes)
$request = "{`"id`":`"warmup`",`"png`":`"$b64`"}"
$proc.StandardInput.WriteLine($request)
$proc.StandardInput.Flush()
$deadline = (Get-Date).AddMinutes(10)
while ((Get-Date) -lt $deadline) {
    if ($proc.StandardOutput.Peek() -ge 0) {
        Write-Output $proc.StandardOutput.ReadLine()
        break
    }
    if ($proc.HasExited) { break }
    Start-Sleep -Milliseconds 500
}
if (-not $proc.HasExited) {
    $proc.StandardInput.WriteLine("{`"command`":`"stop`"}")
    $proc.WaitForExit(5000)
}
if (-not $proc.HasExited) { $proc.Kill() }
Write-Output 'Fast OCR warmup complete'
