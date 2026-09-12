param([Parameter(Mandatory)][string]$RuntimeDirectory)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$runtime = [IO.Path]::GetFullPath($RuntimeDirectory)
if ($runtime.TrimEnd('\') -eq [IO.Path]::GetPathRoot($runtime).TrimEnd('\')) { throw 'Choose a dedicated OCR folder, not a drive root.' }
$uv = Get-Command uv -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source
if (-not $uv) {
    $candidate = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.local/bin/uv.exe'
    if (Test-Path -LiteralPath $candidate) { $uv = $candidate }
}
[void](New-Item -ItemType Directory -Path $runtime -Force)
if (-not $uv) {
    $uvTools = Join-Path $runtime 'tools'
    $uv = Join-Path $uvTools 'uv.exe'
    if (-not (Test-Path -LiteralPath $uv)) {
        Write-Output '0/3 Installing uv in the private OCR folder'
        [void](New-Item -ItemType Directory -Path $uvTools -Force)
        $uvInstaller = Join-Path $uvTools 'install-uv.ps1'
        $env:UV_UNMANAGED_INSTALL = $uvTools
        $env:UV_NO_MODIFY_PATH = '1'
        Invoke-WebRequest -UseBasicParsing -Uri 'https://astral.sh/uv/install.ps1' -OutFile $uvInstaller -TimeoutSec 60
        & powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $uvInstaller
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $uv)) { throw 'uv installation failed' }
    }
}
$env:UV_CACHE_DIR = Join-Path $runtime 'uv-cache'
$env:UV_PYTHON_INSTALL_DIR = Join-Path $runtime 'python'
$env:PYTHONIOENCODING = 'utf-8'
$python = Join-Path $runtime '.venv/Scripts/python.exe'
if (-not (Test-Path -LiteralPath $python)) {
    Write-Output '1/3 Python runtime download (private folder)'
    & $uv python install 3.12 --no-bin --no-registry
    if ($LASTEXITCODE -ne 0) { throw 'Python download failed' }
    & $uv venv --managed-python --python 3.12 (Join-Path $runtime '.venv')
    if ($LASTEXITCODE -ne 0) { throw 'Python environment creation failed' }
}
Write-Output '2/3 Fast OCR libraries download'
& $uv pip install --python $python 'paddlepaddle==3.0.0' 'paddleocr==3.7.0' 'paddlex==3.7.2' 'pillow==12.3.0' 'numpy==2.3.5'
if ($LASTEXITCODE -ne 0) { throw 'Fast OCR library installation failed' }
Write-Output '3/3 Writing runtime marker'
@{ engine = 'PP-OCRv5'; chatRuntimeSchema = 2; paddleocr = '3.7.0'; installedUtc = (Get-Date).ToUniversalTime().ToString('o') } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runtime 'fast_model.json') -Encoding UTF8
Write-Output 'Fast OCR installation complete'
Write-Output 'Tip: first OCR load may take several minutes. Run scripts/Warmup-FastOcr.ps1 before ZH E2E tests.'
