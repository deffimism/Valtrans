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
        Write-Output '0/4 Installing uv in the private OCR folder'
        [void](New-Item -ItemType Directory -Path $uvTools -Force)
        $uvInstaller = Join-Path $uvTools 'install-uv.ps1'
        $env:UV_UNMANAGED_INSTALL = $uvTools
        $env:UV_NO_MODIFY_PATH = '1'
        Invoke-WebRequest -UseBasicParsing -Uri 'https://astral.sh/uv/install.ps1' -OutFile $uvInstaller -TimeoutSec 60
        & powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $uvInstaller
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $uv)) {
            throw 'uv installation failed. Check network access or use the installation guide.'
        }
    }
}
$env:UV_CACHE_DIR = Join-Path $runtime 'uv-cache'
$env:UV_PYTHON_INSTALL_DIR = Join-Path $runtime 'python'
$env:HF_HUB_OFFLINE = '0'
$env:PYTHONIOENCODING = 'utf-8'
$python = Join-Path $runtime '.venv/Scripts/python.exe'
if (-not (Test-Path -LiteralPath $python)) {
    Write-Output '1/4 Python runtime download (private folder)'
    & $uv python install 3.12 --no-bin --no-registry
    if ($LASTEXITCODE -ne 0) { throw 'Python download failed' }
    & $uv venv --managed-python --python 3.12 (Join-Path $runtime '.venv')
    if ($LASTEXITCODE -ne 0) { throw 'Python environment creation failed' }
}
Write-Output '2/4 GPU libraries download'
& $uv pip install --python $python 'torch==2.11.0' 'torchvision==0.26.0' --index-url https://download.pytorch.org/whl/cu128
if ($LASTEXITCODE -ne 0) { throw 'GPU library installation failed' }
Write-Output '3/4 OCR libraries download'
& $uv pip install --python $python 'transformers==5.16.1' 'accelerate==1.14.0' 'sentencepiece==0.2.2'
if ($LASTEXITCODE -ne 0) { throw 'OCR library installation failed' }
Write-Output '4/4 Official OCR model download'
& $python (Join-Path $PSScriptRoot 'download_model.py') $runtime
if ($LASTEXITCODE -ne 0) { throw 'OCR model download failed' }
Write-Output 'Installation complete'
