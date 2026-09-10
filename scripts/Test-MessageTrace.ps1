param([string]$ProjectPath = "$PSScriptRoot/../test/Valtrans.Tests/Valtrans.Tests.csproj")
$ErrorActionPreference = 'Stop'
dotnet test (Resolve-Path -LiteralPath $ProjectPath) -c Release --no-restore 2>&1
if ($LASTEXITCODE -ne 0) {
    dotnet restore (Resolve-Path -LiteralPath $ProjectPath)
    dotnet test (Resolve-Path -LiteralPath $ProjectPath) -c Release
}
if ($LASTEXITCODE -ne 0) { throw "MessageTrace tests failed" }
Write-Output "MessageTrace: PASS"
