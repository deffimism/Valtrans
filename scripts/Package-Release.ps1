param(
    [string]$Version = '0.5.0-beta',
    [string]$OutputDir = "$PSScriptRoot/../releases"
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$') { throw 'Invalid version' }
$root = Resolve-Path "$PSScriptRoot/.."
$projectVersion = ([xml](Get-Content -LiteralPath "$root/Valtrans.csproj" -Raw)).Project.PropertyGroup.Version | Where-Object { $_ }
if ($Version -ne $projectVersion) { throw "Requested version $Version does not match project $projectVersion" }
$publish = Join-Path $root ('.deployment/client-' + [Guid]::NewGuid().ToString('N'))
$zipName = "v$Version.zip"
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$zipPath = Join-Path (Resolve-Path $OutputDir) $zipName

$sourceCommit = & git -C $root rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify source commit' }
$sourceBefore = @(& git -C $root status --porcelain)
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect source status' }

dotnet publish "$root/Valtrans.csproj" -c Release -o $publish --force
if ($LASTEXITCODE -ne 0) { throw "Publish failed (exit $LASTEXITCODE). Previous release left untouched." }
if (-not (Test-Path -LiteralPath (Join-Path $publish 'Valtrans.exe'))) {
    throw 'Publish failed: Valtrans.exe missing'
}

$assembly = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $publish 'Valtrans.dll'))
$sourceAfter = @(& git -C $root status --porcelain)
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect source status after build' }
$commitAfter = & git -C $root rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $commitAfter -ne $sourceCommit) { throw 'Source commit changed during build' }
$manifest = [ordered]@{
    version = $Version
    assemblyVersion = $assembly.Version.ToString()
    productVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $publish 'Valtrans.dll')).ProductVersion
    sourceCommit = $sourceCommit
    sourceDirty = ($sourceBefore.Count -gt 0 -or $sourceAfter.Count -gt 0)
    generatedUtc = [DateTime]::UtcNow.ToString('o')
    files = @(Get-ChildItem -LiteralPath $publish -Recurse -File | ForEach-Object {
        [ordered]@{name=[IO.Path]::GetRelativePath($publish,$_.FullName).Replace('\','/'); sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
    })
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $publish 'build-manifest.json') -Encoding utf8
# Do not replace a working release until both build and compression have succeeded.
$temporaryZip = Join-Path (Split-Path $publish -Parent) ((Split-Path $publish -Leaf) + '.zip')
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $temporaryZip
Move-Item -LiteralPath $temporaryZip -Destination $zipPath -Force
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $zipName" | Set-Content -LiteralPath "$zipPath.sha256" -Encoding ascii
Write-Output "Release package: $zipPath"
