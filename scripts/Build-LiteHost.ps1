param([string]$Python = "$PSScriptRoot/../.lite-build/venv/Scripts/python.exe")
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot/..").Path
$pythonExe = (Resolve-Path $Python).Path
$buildRoot = Join-Path $root '.lite-build'
$dist = Join-Path $buildRoot 'dist'
New-Item -ItemType Directory -Force -Path $buildRoot,$dist | Out-Null

& $pythonExe -m pip check
if ($LASTEXITCODE -ne 0) { throw 'Lite build dependency check failed.' }
& $pythonExe "$root/Lite/test_lite_host.py"
if ($LASTEXITCODE -ne 0) { throw 'Lite host unit tests failed.' }

# Keep the pipe-backed console streams; the application launches this helper hidden.
# https://pyinstaller.org/en/stable/usage.html
& $pythonExe -m PyInstaller --noconfirm --onefile --console --hide-console hide-early --noupx `
    --name ValtransLiteHost --distpath $dist --workpath "$buildRoot/work" --specpath $buildRoot `
    --collect-binaries ctranslate2 --collect-all sentencepiece --copy-metadata ctranslate2 `
    --exclude-module torch --exclude-module transformers --icon "$root/Assets/Valtrans.ico" `
    "$root/Lite/valtrans_lite.py"
if ($LASTEXITCODE -ne 0) { throw 'Lite host packaging failed.' }
$executable = Join-Path $dist 'ValtransLiteHost.exe'
if (!(Test-Path -LiteralPath $executable) -or (Get-Item -LiteralPath $executable).Length -lt 1000000) {
    throw 'Lite host executable is missing or truncated.'
}

# Only the newly built helper is launched; no installed host or models are touched.
$start = [Diagnostics.ProcessStartInfo]::new($executable)
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$start.RedirectStandardInput = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
$start.StandardInputEncoding = [Text.UTF8Encoding]::new($false)
$start.Environment['VALTRANS_LITE_MODELS'] = Join-Path $buildRoot 'smoke-empty-models'
$process = [Diagnostics.Process]::Start($start)
try {
    $errors = $process.StandardError.ReadToEndAsync()
    $process.StandardInput.WriteLine('{"id":"build-status","command":"status"}')
    $process.StandardInput.Flush()
    $response = $process.StandardOutput.ReadLineAsync()
    if (!$response.Wait(30000)) { throw 'New Lite host status timed out.' }
    $status = $response.Result | ConvertFrom-Json
    if (!$status.ok -or $status.id -ne 'build-status' -or $status.result.compute -ne 'CPU INT8') {
        throw 'New Lite host returned an invalid status response.'
    }
    $process.StandardInput.WriteLine('{"id":"build-shutdown","command":"shutdown"}')
    $process.StandardInput.Flush()
    if (!$process.WaitForExit(10000) -or $process.ExitCode -ne 0) { throw 'New Lite host did not shut down cleanly.' }
    if ($errors.Result) { Write-Warning $errors.Result }
} finally {
    if (!$process.HasExited) { $process.Kill($true); $process.WaitForExit(5000) | Out-Null }
    $process.Dispose()
}
$dependencies = @(& $pythonExe -m pip freeze)
if ($LASTEXITCODE -ne 0) { throw 'Cannot record Lite build dependencies.' }
@{
    builtUtc = [DateTime]::UtcNow.ToString('o')
    sourceSha256 = (Get-FileHash "$root/Lite/valtrans_lite.py" -Algorithm SHA256).Hash
    executableSha256 = (Get-FileHash $executable -Algorithm SHA256).Hash
    dependencies = $dependencies
    statusSmokePassed = $true
    promotedToApp = $false
} | ConvertTo-Json -Depth 4 | Set-Content "$dist/build-manifest.json" -Encoding utf8
Write-Output "Built and status-tested: $executable (not yet copied into Assets)"
