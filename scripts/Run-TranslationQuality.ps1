param([ValidateSet('hybrid18','hybrid7','lite')][string]$Profile='hybrid18', [string]$OutputDirectory="$PSScriptRoot/../artifacts/translation-quality",
    [string]$CorpusPath="$PSScriptRoot/../testdata/translation-quality/corpus.tsv", [switch]$AllFamilies,
    [switch]$IsolatedModels, [switch]$SkipWarmup, [string]$LiteRootDirectory, [string[]]$FamilyIds,
    [ValidateSet('All','Briefing','Strict')][string]$ReceiveFilterMode='All')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot/..").Path
Add-Type -Path "$root/bin/Release/net10.0-windows10.0.26100.0/Valtrans.dll"
$settingsJson = Get-Content -LiteralPath "$env:LOCALAPPDATA/Valtrans/settings.json" -Raw
$settings = [Text.Json.JsonSerializer]::Deserialize($settingsJson,[Valtrans.Models.AppSettings])
$settings.Game='VALORANT'
$settings.OcrChatFilterMode=$ReceiveFilterMode
$settings.TranslationProvider=if($Profile -eq 'lite'){'Lite'}else{'Hybrid'}
$settings.LocalAiModel=if($Profile -eq 'hybrid7'){'valtrans-hymt2:7b'}else{'valtrans-hymt2:1.8b'}
$subset=@('01','02','03','06','09','10','11','12','13','14','15','16','19','20','22','26','28','32','40','45','51','52','55','57','60','63','65','69','75','80')
$corpus = @(Import-Csv $CorpusPath -Delimiter "`t")
if($FamilyIds) {
    $unknown=@($FamilyIds | Where-Object {$_ -notin $corpus.id})
    if($unknown.Count) {throw "Unknown corpus families: $($unknown -join ', ')"}
    $corpus=@($corpus | Where-Object id -In $FamilyIds)
} elseif($Profile -ne 'hybrid18' -and -not $AllFamilies){$corpus=@($corpus | Where-Object id -In $subset)}
$glossary=[Valtrans.Services.GlossaryService]::new()
$lite=if($LiteRootDirectory){[Valtrans.Services.ValtransLiteService]::new((Resolve-Path $LiteRootDirectory).Path)}else{[Valtrans.Services.ValtransLiteService]::new()}
$local=[Valtrans.Services.LocalAiService]::new()
$translator=[Valtrans.Services.TranslatorService]::new($glossary,$local,$lite)
$classifier=[Valtrans.Services.MessageClassifierService]::new([Valtrans.Services.GameChatFilterService]::new($glossary))
$chatFilter=[Valtrans.Services.GameChatFilterService]::new($glossary)
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$resultPath=Join-Path $OutputDirectory "$Profile.jsonl"
if(Test-Path -LiteralPath $resultPath){throw "Results already exist: $resultPath"}
$residency=@()
$warmupMs=0
if ($Profile -eq 'lite') {
    # Resolve the embedded host version before hashing the files actually tested.
    $lite.GetType().GetMethod('EnsureHostBinary',[Reflection.BindingFlags]'NonPublic,Instance').Invoke($lite,@()) | Out-Null
    if (!$SkipWarmup) {
        $warmWatch=[Diagnostics.Stopwatch]::StartNew()
        foreach($pair in @(@('KO','EN'),@('EN','JP'),@('JP','EN'),@('EN','KO'))) {
            $lite.WarmUpAsync($pair[0],$pair[1],[Threading.CancellationToken]::None).GetAwaiter().GetResult() | Out-Null
        }
        $warmupMs=$warmWatch.ElapsedMilliseconds
    }
}
if($Profile -ne 'lite') {
    $residency=@((Invoke-RestMethod 'http://127.0.0.1:11434/api/ps' -TimeoutSec 5).models |
        Select-Object name,size,size_vram)
    if($IsolatedModels) {
        # Only these two Valtrans benchmark models are in scope. Never unload
        # arbitrary Ollama models or stop a user's GPU application.
        foreach($other in @('valtrans-hymt2:1.8b','valtrans-hymt2:7b')|Where-Object {$_ -ne $settings.LocalAiModel}) {
            [void]$local.UnloadModelAsync($other,[Threading.CancellationToken]::None).GetAwaiter().GetResult()
            if($local.GetStatusAsync($other,[Threading.CancellationToken]::None).GetAwaiter().GetResult().ModelLoaded) {
                throw "Could not release benchmark model $other; aborting mixed-residency measurement."
            }
        }
    }
    if(!$SkipWarmup) {
        Write-Output "WARMUP $($settings.LocalAiModel) (recorded separately from translation latency)"
        $warmWatch=[Diagnostics.Stopwatch]::StartNew()
        $warm=$local.WarmUpAsync($settings.LocalAiModel,$null,[Threading.CancellationToken]::None).GetAwaiter().GetResult()
        $warmupMs=$warmWatch.ElapsedMilliseconds
        if(!$warm.Success){throw "Warmup failed: $($warm.Message)"}
    }
}
$assemblyHash=(Get-FileHash -LiteralPath "$root/bin/Release/net10.0-windows10.0.26100.0/Valtrans.dll" -Algorithm SHA256).Hash
$liteFiles = @()
if ($Profile -eq 'lite') {
    $liteFiles = @(Get-ChildItem -LiteralPath $lite.RootDirectory -Recurse -File |
        Where-Object Extension -In @('.exe','.bin','.model','.spm','.json') |
        ForEach-Object { @{path=[IO.Path]::GetRelativePath($lite.RootDirectory,$_.FullName);bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash} })
}
@{assemblySha256=$assemblyHash;corpusSha256=(Get-FileHash -LiteralPath $CorpusPath -Algorithm SHA256).Hash;profile=$Profile;model=$settings.LocalAiModel;families=$corpus.Count;createdUtc=[DateTime]::UtcNow.ToString('o');initialResidency=$residency;isolatedBenchmarkModels=[bool]$IsolatedModels;warmupMs=$warmupMs;skipWarmup=[bool]$SkipWarmup;liteRootDirectory=$lite.RootDirectory;liteFiles=$liteFiles} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputDirectory "$Profile.meta.json") -Encoding utf8
$metadataPath=Join-Path $OutputDirectory "$Profile.meta.json"
$metadata=Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json -AsHashtable
$metadata.receiveFilterMode=$ReceiveFilterMode
$metadata.familyIds=@($corpus.id)
$metadata | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $metadataPath -Encoding utf8
$writer=[IO.StreamWriter]::new($resultPath,$false,[Text.UTF8Encoding]::new($false))
$writer.AutoFlush=$true
$index=0
$hostPeakWorkingSetBytes=0L
$hostCpuMs=0.0
$hostMeasuredParent=0
$hostChildIds=@()
$resourceMeasurementError=$null
try {
    Write-Output "START $Profile families=$($corpus.Count), calls=$($corpus.Count*4), serverRegion=$($settings.ServerRegion), LiteReady=$($lite.GetStatus().Ready)"
    foreach($family in $corpus){
        foreach($direction in @('send-en','send-jp','receive-en','receive-jp')){
            $source=switch($direction){'send-en'{$family.ko};'send-jp'{$family.ko};'receive-en'{$family.en};'receive-jp'{$family.jp}}
            $target=switch($direction){'send-en'{'EN'};'send-jp'{'JP'};default{'KO'}}
            $expected=switch($target){'EN'{$family.en};'JP'{$family.jp};default{$family.ko}}
            $receive=$direction.StartsWith('receive')
            $timeout=[Threading.CancellationTokenSource]::new(30000)
            $watch=[Diagnostics.Stopwatch]::StartNew()
            $record=[ordered]@{id="$($family.id)-$direction";profile=$Profile;category=$family.category;source=$source;target=$target;reference=$expected;output=$null;error=$null;ms=0;route=$null;type=$null;validator=$null;utc=[DateTime]::UtcNow.ToString('o')}
            try {
                $translator.GetType().GetProperty('LastHybridRoute').SetValue($translator,'')
                $translationInput=$source
                $record.filterMode=if($receive){$ReceiveFilterMode}else{'not-applied'}
                if($receive) {
                    $filtered=$chatFilter.Filter($source,$ReceiveFilterMode,$settings)
                    $record.filterKeep=$filtered.Keep
                    $record.filterReason=$filtered.Reason
                    $translationInput=$filtered.Text
                    $record.translationInput=$translationInput
                    if(!$filtered.Keep){throw "FILTERED: $($filtered.Reason)"}
                } else { $record.translationInput=$source }
                $output=$translator.TranslateAsync($translationInput,$target,$settings,$receive,$timeout.Token).GetAwaiter().GetResult()
                $record.output=$output
                $record.route=$translator.LastHybridRoute
                $classification=$classifier.Classify($source,$settings)
                $record.type=$classification.Type
                $record.validator=[Valtrans.Services.CriticalFactValidator]::Validate($source,$output,$classification.Type).Code
            } catch {$record.error=$_.Exception.GetBaseException().Message}
            finally {
                $record.ms=$watch.ElapsedMilliseconds;$timeout.Dispose()
                if ($Profile -eq 'lite') {
                    $hostProcess=$lite.GetType().GetField('_process',[Reflection.BindingFlags]'NonPublic,Instance').GetValue($lite)
                    if ($hostProcess -and !$hostProcess.HasExited) {
                        try {
                            # PyInstaller onefile has a small parent bootloader and
                            # a child running the models. Measuring only the parent
                            # incorrectly reports ~9 MB for a loaded translation host.
                            if ($hostMeasuredParent -ne $hostProcess.Id) {
                                $hostChildIds=@(Get-CimInstance Win32_Process -Filter "ParentProcessId = $($hostProcess.Id)" -ErrorAction Stop | Select-Object -ExpandProperty ProcessId)
                                $hostMeasuredParent=$hostProcess.Id
                            }
                            $memory=0L; $cpu=0.0
                            foreach($processId in @($hostProcess.Id)+$hostChildIds) {
                                $measured=[Diagnostics.Process]::GetProcessById($processId)
                                try { $measured.Refresh(); $memory+=$measured.PeakWorkingSet64; $cpu+=$measured.TotalProcessorTime.TotalMilliseconds }
                                finally { $measured.Dispose() }
                            }
                            $hostPeakWorkingSetBytes=[Math]::Max($hostPeakWorkingSetBytes,$memory)
                            $hostCpuMs=[Math]::Max($hostCpuMs,$cpu)
                        } catch { $resourceMeasurementError=$_.Exception.Message }
                    }
                }
            }
            $writer.WriteLine(($record | ConvertTo-Json -Compress -Depth 6))
            $index++
            if($index % 20 -eq 0){Write-Output "$Profile $index/$($corpus.Count*4) last=$($record.id) elapsed=$($record.ms)ms"}
        }
    }
} finally {
    $writer.Dispose();$lite.Dispose()
    if($IsolatedModels -and $Profile -ne 'lite') {
        [void]$local.UnloadModelAsync($settings.LocalAiModel,[Threading.CancellationToken]::None).GetAwaiter().GetResult()
    }
}
Write-Output "DONE $Profile $index calls: $resultPath"
if ($Profile -eq 'lite') {
    @{peakWorkingSetBytes=$hostPeakWorkingSetBytes;cpuMs=$hostCpuMs;calls=$index;scope='parent plus direct children (sum of individual peaks)';childProcesses=$hostChildIds.Count;measurementError=$resourceMeasurementError} |
        ConvertTo-Json | Set-Content (Join-Path $OutputDirectory 'lite-resource.json') -Encoding utf8
}
if($assemblyHash -ne (Get-FileHash -LiteralPath "$root/bin/Release/net10.0-windows10.0.26100.0/Valtrans.dll" -Algorithm SHA256).Hash){throw 'Build changed during the benchmark; do not treat this run as final.'}
