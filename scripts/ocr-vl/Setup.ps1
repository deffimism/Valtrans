param([Parameter(Mandatory)][string]$PythonPath)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot/../..").Path
Push-Location $root
try {
    $runtime = 'artifacts/ocr-vl/.venv/Scripts/python.exe'
    if (-not (Test-Path -LiteralPath $runtime)) {
        & uv --cache-dir artifacts/ocr-vl/uv-cache venv --python $PythonPath artifacts/ocr-vl/.venv
        if ($LASTEXITCODE -ne 0) { throw 'OCR test environment creation failed' }
    }
    & uv --cache-dir artifacts/ocr-vl/uv-cache pip install --python $runtime 'torch==2.11.0' 'torchvision==0.26.0' --index-url https://download.pytorch.org/whl/cu128
    if ($LASTEXITCODE -ne 0) { throw 'GPU dependency installation failed' }
    & uv --cache-dir artifacts/ocr-vl/uv-cache pip install --python $runtime 'transformers==5.16.1' 'accelerate==1.14.0' 'sentencepiece==0.2.2'
    if ($LASTEXITCODE -ne 0) { throw 'OCR dependency installation failed' }
    & $runtime scripts/ocr-vl/benchmark.py --download
    if ($LASTEXITCODE -ne 0) { throw 'OCR model download failed' }
} finally { Pop-Location }
