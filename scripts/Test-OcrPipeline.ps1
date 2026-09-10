param([string]$ProjectPath = "$PSScriptRoot/../test/Valtrans.Tests/Valtrans.Tests.csproj")
$ErrorActionPreference = 'Stop'
dotnet test (Resolve-Path -LiteralPath $ProjectPath) -c Release --filter "FullyQualifiedName~OcrPipeline|FullyQualifiedName~IncomingOcrQueue"
if ($LASTEXITCODE -ne 0) { throw "OCR pipeline regression failed" }
Write-Output 'PASS: OCR pipeline regression'
