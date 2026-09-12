param(
    [string]$Version = '0.5.0-beta',
    [string]$SshIdentity = 'C:\Users\User\.ssh\valtrans_deploy_ed25519',
    [switch]$Deploy
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$') { throw 'Invalid version' }
$root = (Resolve-Path "$PSScriptRoot/..").Path
$archive = Join-Path $root "releases/valtrans-support-v$Version.tar.gz"
$checksum = "$archive.sha256"
$expected = ((Get-Content -LiteralPath $checksum -Raw).Trim() -split '\s+')[0]
$actual = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
if ($expected -notmatch '^[a-fA-F0-9]{64}$' -or $expected -ne $actual) {
    throw 'Local archive SHA256 mismatch; nothing uploaded'
}
if (-not (Test-Path -LiteralPath $SshIdentity -PathType Leaf)) { throw 'Deployment SSH key not found' }
$sshOptions = @('-i', $SshIdentity, '-o', 'IdentitiesOnly=yes', '-o', 'BatchMode=yes', '-o', 'ConnectTimeout=10')
$destination = 'deffimism@192.168.0.19'
$remoteArchive = '/DATA/valtrans-deploy-inbox/valtrans-support-release.tar.gz'

& scp @sshOptions $archive ($destination + ':' + $remoteArchive)
if ($LASTEXITCODE -ne 0) { throw 'Archive upload failed; do not deploy' }
& scp @sshOptions $checksum ($destination + ':' + $remoteArchive + '.sha256')
if ($LASTEXITCODE -ne 0) { throw 'Checksum upload failed; do not deploy' }
$remoteHashOutput = & ssh @sshOptions $destination "sha256sum $remoteArchive"
if ($LASTEXITCODE -ne 0) { throw 'Remote hash check failed; do not deploy' }
$remoteHash = (($remoteHashOutput -join [Environment]::NewLine).Trim() -split '\s+')[0]
if ($remoteHash -ne $actual) { throw 'Remote archive SHA256 mismatch; do not deploy' }
Write-Output "SYNC VERIFIED: $actual"

if ($Deploy) {
    # The root-owned no-argument helper validates the package, preserves .env
    # and payment data, and can replace only the Valtrans service.
    & ssh @sshOptions $destination 'sudo -n /DATA/.valtrans-admin/valtrans-deploy'
    if ($LASTEXITCODE -ne 0) { throw 'Deployment failed; inspect helper output and rollback status' }
} else {
    Write-Output 'Upload only; no service restarted. SSH: sudo -n /DATA/.valtrans-admin/valtrans-deploy'
}
