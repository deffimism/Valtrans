param([string]$Version='0.5.0-beta', [string]$BenchmarkMeta='')
$ErrorActionPreference='Stop'
$root=(Resolve-Path "$PSScriptRoot/..").Path
if($Version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$'){throw 'Invalid version'}
$staging=Join-Path $root ('.deployment/verify-'+[Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $staging)
foreach($kind in @('client','support')){
    $name=if($kind -eq 'client'){"v$Version.zip"}else{"valtrans-support-v$Version.tar.gz"}
    $archive=Join-Path $root "releases/$name"
    $actual=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
    $expected=((Get-Content -LiteralPath "$archive.sha256" -Raw).Trim() -split '\s+')[0]
    if($actual -ne $expected){throw "Archive hash mismatch: $name"}
    $destination=Join-Path $staging $kind
    [void](New-Item -ItemType Directory -Path $destination)
    # Reject traversal before extraction, even though these archives were built locally.
    $entries=@(& tar -tf $archive)
    if($LASTEXITCODE -ne 0){throw 'Cannot list archive'}
    foreach($entry in $entries){
        $normalized=$entry.Replace('\','/')
        if($normalized -match '(^/|^[A-Za-z]:|(^|/)\.\.(/|$))'){throw "Unsafe entry: $entry"}
    }
    & tar -xf $archive -C $destination
    if($LASTEXITCODE -ne 0){throw 'Cannot extract archive'}
    $manifestName=if($kind -eq 'client'){'build-manifest.json'}else{'support-build-manifest.json'}
    $manifest=Get-Content -LiteralPath (Join-Path $destination $manifestName) -Raw | ConvertFrom-Json
    if($manifest.version -ne $Version){throw 'Manifest version mismatch'}
    foreach($file in $manifest.files){
        $target=[IO.Path]::GetFullPath((Join-Path $destination $file.name))
        if(-not $target.StartsWith($destination+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe manifest path'}
        if((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $file.sha256){throw "File hash mismatch: $($file.name)"}
    }
    $allFiles=@(Get-ChildItem -LiteralPath $destination -Recurse -File)
    if($allFiles.Count -ne $manifest.files.Count+1){throw 'Unexpected archive files'}
    if($kind -eq 'client'){
        if(-not (Test-Path -LiteralPath "$destination/Valtrans.exe")){throw 'Missing executable'}
        $ocrFiles=@('Ocr/paddle_host.py','Ocr/fast_ocr_host.py')
        if([version]($Version -split '-')[0] -ge [version]'0.5.0') { $ocrFiles += 'Ocr/chat_ocr.py' }
        foreach($required in $ocrFiles) {
            if(-not (Test-Path -LiteralPath (Join-Path $destination $required))){throw "Missing OCR runtime file: $required"}
        }
        if($BenchmarkMeta){
            $meta=Get-Content -LiteralPath $BenchmarkMeta -Raw|ConvertFrom-Json
            if((Get-FileHash -LiteralPath "$destination/Valtrans.dll" -Algorithm SHA256).Hash -ne $meta.assemblySha256){throw 'Packaged DLL differs from benchmark DLL'}
        }
    }else{
        if(Test-Path -LiteralPath "$destination/server/.env"){throw 'Environment file was packaged'}
        if(Test-Path -LiteralPath "$destination/server/data"){throw 'Server data was packaged'}
        foreach($path in @('site/index.html','site/terms.html','site/privacy.html','README.md','scripts/sync-support-site.sh')){
            if(-not (Test-Path -LiteralPath (Join-Path $destination $path))){throw "Missing $path"}
        }
    }
    Write-Output "VERIFIED $name : $($manifest.files.Count) files; SHA256 $actual"
}
Write-Output "Inspection files retained at $staging"
