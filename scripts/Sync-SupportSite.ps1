param(
    [string]$RemoteHost = '192.168.0.19',
    [string]$RemotePath = '/opt/valtrans',
    [int]$SupportPort = 13020,
    [string]$DownloadUrl = 'https://github.com/deffimism/Valtrans/releases/download/v0.4.0-beta/v0.4.0-beta.zip'
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot/.."

Write-Output "Syncing site + server to ${RemoteHost}:$RemotePath and rebuilding valtrans-support..."

$tar = Join-Path $env:TEMP 'valtrans-support-sync.tar'
if (Test-Path $tar) { Remove-Item $tar -Force }
& tar -cf $tar -C $root site server/Dockerfile server/server.js server/package.json server/compose.yaml

scp $tar "${RemoteHost}:${RemotePath}/valtrans-support-sync.tar"
$remote = @"
set -e
cd '$RemotePath'
tar -xf valtrans-support-sync.tar
rm -f valtrans-support-sync.tar
cd server
export VALTRANS_DOWNLOAD_URL='$DownloadUrl'
docker compose build --no-cache valtrans-support
docker compose up -d valtrans-support
curl -fsS http://127.0.0.1:$SupportPort/health
"@
ssh $RemoteHost $remote

Write-Output 'Support site sync complete.'
