param([string]$OutputPath="$PSScriptRoot/../artifacts/lite-candidate-ko-en/paired-inputs.json")
$ErrorActionPreference='Stop'
$root=(Resolve-Path "$PSScriptRoot/..").Path
Add-Type -Path "$root/bin/Release/net10.0-windows10.0.26100.0/Valtrans.dll"
$settings=[Valtrans.Models.AppSettings]::new()
$settings.Game='VALORANT'
$glossary=[Valtrans.Services.GlossaryService]::new()
$rows=foreach($name in @('corpus.tsv','held-out.tsv')) {
    foreach($row in (Import-Csv "$root/testdata/translation-quality/$name" -Delimiter "`t")) {
        [ordered]@{id=$row.id;original=$row.ko;expanded=$glossary.PrepareForLocalTranslation($row.ko,$settings);reference=$row.en}
    }
}
$rows | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $OutputPath -Encoding utf8
