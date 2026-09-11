param(
    [string]$Version = '0.4.0-beta',
    [string]$OutputDir = "$PSScriptRoot/../releases"
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot/.."
$publish = Join-Path $root 'publish'
$zipName = "v$Version.zip"
$zipPath = Join-Path (Resolve-Path $OutputDir) $zipName

dotnet publish "$root/Valtrans.csproj" -c Release -o $publish --force | Out-Null
if (-not (Test-Path -LiteralPath (Join-Path $publish 'Valtrans.exe'))) {
    throw 'Publish failed: Valtrans.exe missing'
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zipPath -Force
Write-Output "Release package: $zipPath"
