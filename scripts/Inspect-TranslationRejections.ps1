param([string]$ResultPath="$PSScriptRoot/../artifacts/quality-v050/phase3/hybrid18.jsonl",
    [string]$OutputPath="$PSScriptRoot/../artifacts/quality-v050/phase3/rejections-raw.jsonl",
    [switch]$IsolatedModels)
$ErrorActionPreference='Stop'
$root=(Resolve-Path "$PSScriptRoot/..").Path
Add-Type -Path "$root/bin/Release/net10.0-windows10.0.26100.0/Valtrans.dll"
$settings=[Text.Json.JsonSerializer]::Deserialize((Get-Content "$env:LOCALAPPDATA/Valtrans/settings.json" -Raw),[Valtrans.Models.AppSettings])
$settings.Game='VALORANT'
$rows=@(Get-Content $ResultPath | ForEach-Object { $_ | ConvertFrom-Json })
$profiles=@($rows.profile | Select-Object -Unique)
if($profiles.Count -ne 1 -or $profiles[0] -notin @('hybrid18','hybrid7')) { throw 'Expected one chat-model profile' }
$settings.LocalAiModel=if($profiles[0] -eq 'hybrid7'){'valtrans-hymt2:7b'}else{'valtrans-hymt2:1.8b'}
$glossary=[Valtrans.Services.GlossaryService]::new()
$lite=[Valtrans.Services.ValtransLiteService]::new()
$local=[Valtrans.Services.LocalAiService]::new()
$translator=[Valtrans.Services.TranslatorService]::new($glossary,$local,$lite)
if($IsolatedModels) {
    foreach($other in @('valtrans-hymt2:1.8b','valtrans-hymt2:7b') | Where-Object {$_ -ne $settings.LocalAiModel}) {
        [void]$local.UnloadModelAsync($other,[Threading.CancellationToken]::None).GetAwaiter().GetResult()
    }
}
$method=$translator.GetType().GetMethod('TranslateWithChatModelAsync',[Reflection.BindingFlags]'NonPublic,Instance')
$facts=[Valtrans.Services.TranslationFactGuard].GetMethod('ExtractFacts',[Reflection.BindingFlags]'NonPublic,Static')
if(Test-Path $OutputPath){throw 'Refusing to overwrite diagnostics'}
$stream=[IO.StreamWriter]::new($OutputPath,$false,[Text.UTF8Encoding]::new($false))
try {
    foreach($row in $rows) {
        if(!$row.error){continue}
        $timeout=[Threading.CancellationTokenSource]::new(30000)
        try {
            $raw=$method.Invoke($translator,@($row.source,$row.target,$settings,$false,$timeout.Token,$settings.LocalAiModel)).GetAwaiter().GetResult()
            $record=[ordered]@{id=$row.id;model=$settings.LocalAiModel;source=$row.source;raw=$raw;error=$row.error;
                before=$facts.Invoke($null,@($row.source,$null));after=$facts.Invoke($null,@($raw,$null));
                validation=[Valtrans.Services.CriticalFactValidator]::Validate($row.source,$raw,'Tactical')}
            $stream.WriteLine(($record|ConvertTo-Json -Compress -Depth 8))
            $stream.Flush()
        } finally {$timeout.Dispose()}
    }
} finally {
    $stream.Dispose();$lite.Dispose()
    if($IsolatedModels) { [void]$local.UnloadModelAsync($settings.LocalAiModel,[Threading.CancellationToken]::None).GetAwaiter().GetResult() }
}
