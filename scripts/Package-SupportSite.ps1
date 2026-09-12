param(
    [string]$Version = '0.5.0-beta'
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$') { throw 'Invalid version' }
$root = Resolve-Path "$PSScriptRoot/.."
$projectVersion = ([xml](Get-Content -LiteralPath "$root/Valtrans.csproj" -Raw)).Project.PropertyGroup.Version | Where-Object { $_ }
if ($Version -ne $projectVersion) { throw "Requested version $Version does not match project $projectVersion" }
if (-not (Get-Content -LiteralPath "$root/site/index.html" -Raw).Contains("v$Version")) { throw 'Site version does not match package' }
$out = Join-Path $root "releases/valtrans-support-v$Version.tar.gz"
New-Item -ItemType Directory -Force -Path (Split-Path $out) | Out-Null

$staging = Join-Path $root ('.deployment/support-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path "$staging/scripts", "$staging/site", "$staging/server" | Out-Null

Copy-Item -Recurse -Force (Join-Path $root 'site/*') "$staging/site/"
Copy-Item -Force (Join-Path $root 'server/Dockerfile') "$staging/server/"
Copy-Item -Force (Join-Path $root 'server/server.js') "$staging/server/"
Copy-Item -Force (Join-Path $root 'server/package.json') "$staging/server/"
Copy-Item -Force (Join-Path $root 'server/compose.yaml') "$staging/server/"
Copy-Item -Force (Join-Path $root 'scripts/sync-support-site.sh') "$staging/scripts/"
Copy-Item -Force (Join-Path $root 'server/README.md') "$staging/server/"
Copy-Item -Force (Join-Path $root 'README.md') "$staging/"

$manifest = [ordered]@{
    version = $Version
    generatedUtc = [DateTime]::UtcNow.ToString('o')
    files = @(Get-ChildItem -LiteralPath $staging -Recurse -File | ForEach-Object {
        [ordered]@{name=[IO.Path]::GetRelativePath($staging, $_.FullName).Replace('\','/'); sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
    })
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$staging/support-build-manifest.json" -Encoding utf8

$tempArchive = "$staging.tar.gz"
& tar -czf $tempArchive -C $staging .
if ($LASTEXITCODE -ne 0) { throw "Archive failed (exit $LASTEXITCODE); previous package retained." }
Move-Item -LiteralPath $tempArchive -Destination $out -Force
$hash = (Get-FileHash -LiteralPath $out -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($out))" | Set-Content -LiteralPath "$out.sha256" -Encoding ascii
Write-Output "Support site package: $out"
Write-Output "SMB: copy this file to the server, then extract and run scripts/sync-support-site.sh"
