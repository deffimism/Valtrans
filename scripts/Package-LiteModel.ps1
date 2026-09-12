param([string]$OutputDirectory = "$PSScriptRoot/../releases")
$ErrorActionPreference = 'Stop'
$root=(Resolve-Path "$PSScriptRoot/..").Path
$candidate=Join-Path $root 'artifacts/lite-candidate-ko-en'
$original=Join-Path $candidate 'original-marian.zip'
$originalHash='9132ed606b6964656520931d96a4a6afc7ddc7d55ca5438e1d403db0c05492ef'
if ((Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash.ToLowerInvariant() -ne $originalHash) {
    throw 'Original OPUS archive hash mismatch.'
}
$files=[ordered]@{
    'model/config.json' = "$candidate/marian-ct2-int8/config.json"
    'model/model.bin' = "$candidate/marian-ct2-int8/model.bin"
    'model/source_vocabulary.json' = "$candidate/marian-ct2-int8/source_vocabulary.json"
    'model/target_vocabulary.json' = "$candidate/marian-ct2-int8/target_vocabulary.json"
    'source.spm' = "$candidate/marian-original/source.spm"
    'target.spm' = "$candidate/marian-original/target.spm"
    'LICENSE' = "$candidate/marian-original/LICENSE"
    'README.OPUS.md' = "$candidate/marian-original/README.md"
    'NOTICE.md' = "$root/Lite/OPUS-KO-EN-NOTICE.md"
}
$manifest=[ordered]@{modelVersion='2.0';sourceArchiveSha256=$originalHash;files=@()}
foreach ($entry in $files.GetEnumerator()) {
    $file=Get-Item -LiteralPath $entry.Value
    $manifest.files+=@{path=$entry.Key;bytes=$file.Length;sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$output=Join-Path (Resolve-Path $OutputDirectory) 'valtrans-lite-ko-en-opus-20220728-int8.zip'
if(Test-Path -LiteralPath $output){throw 'Model package already exists; keep the immutable release artifact.'}
$zip=[IO.Compression.ZipFile]::Open($output,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($entry in $files.GetEnumerator()) {
        $item=$zip.CreateEntry('ko_en/'+$entry.Key,[IO.Compression.CompressionLevel]::Optimal)
        $item.LastWriteTime=[DateTimeOffset]::Parse('2022-07-28T00:00:00Z')
        $target=$item.Open()
        $source=[IO.File]::OpenRead($entry.Value)
        try {$source.CopyTo($target)} finally {$source.Dispose();$target.Dispose()}
    }
} finally {$zip.Dispose()}
$hash=(Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()
$manifest.package=@{file=[IO.Path]::GetFileName($output);bytes=(Get-Item -LiteralPath $output).Length;sha256=$hash}
$manifest | ConvertTo-Json -Depth 5 | Set-Content "$output.manifest.json" -Encoding utf8
"$hash  $([IO.Path]::GetFileName($output))" | Set-Content "$output.sha256" -Encoding ascii
Write-Output "Prepared model package (not published): $output"
