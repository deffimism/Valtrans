param(
    [string]$AssemblyPath = "$PSScriptRoot/../publish/Valtrans.dll",
    [string[]]$Models = @('valtrans-hymt2:1.8b', 'qwen3:1.7b', 'translategemma:4b'),
    [string]$ReportName = 'model-comparison'
)
$ErrorActionPreference = 'Stop'
Add-Type -Path (Resolve-Path -LiteralPath $AssemblyPath)
$installed = @((Invoke-RestMethod http://127.0.0.1:11434/api/tags -TimeoutSec 5).models.name)
$previouslyLoaded = @((Invoke-RestMethod http://127.0.0.1:11434/api/ps -TimeoutSec 5).models.name)
$glossary = [Valtrans.Services.GlossaryService]::new()
$lite = [Valtrans.Services.ValtransLiteService]::new()
$translator = [Valtrans.Services.TranslatorService]::new($glossary, [Valtrans.Services.LocalAiService]::new(), $lite)
# Deliberately bypass deterministic shortcuts to measure the real model prompt path.
$method = [Valtrans.Services.TranslatorService].GetMethod('TranslateWithChatModelAsync', [Reflection.BindingFlags]'Instance,NonPublic')
$cases = @(
    @{ Text = 'どうぞよろしくおねがいします'; Target = 'KO'; Meaning = '잘 부탁해요 / 잘 부탁해'; },
    @{ Text = '왼쪽 조심해'; Target = 'EN'; Meaning = 'watch left / careful on the left'; },
    @{ Text = '2 left, 1 right'; Target = 'KO'; Meaning = '왼쪽 2명, 오른쪽 1명'; },
    @{ Text = 'not left, right'; Target = 'KO'; Meaning = '왼쪽 말고 오른쪽'; },
    @{ Text = 'do not push left, go right'; Target = 'KO'; Meaning = '왼쪽 밀지 말고 오른쪽 가'; },
    @{ Text = 'save me, not the gun'; Target = 'KO'; Meaning = '총 말고 나를 구해줘'; },
    @{ Text = 'you are cracked, nice shots'; Target = 'KO'; Meaning = '칭찬: 잘한다 / 샷 좋다 (실드 파괴 아님)'; },
    @{ Text = 'Jett is low but not one shot'; Target = 'KO'; Meaning = 'Jett 피 적지만 한 방은 아님'; },
    @{ Text = 'たぶん右に二人、左にはいない'; Target = 'KO'; Meaning = '아마 오른쪽 2명, 왼쪽엔 없음'; },
    @{ Text = '미드 말고 B로 가자, 아직 들어가진 마'; Target = 'EN'; Meaning = 'go B not mid, do not enter yet'; },
    @{ Text = 'hold tree until I flash'; Target = 'KO'; Meaning = '내가 섬광 쓸 때까지 트리 지켜'; },
    @{ Text = 'I left the game because I had to go'; Target = 'KO'; Meaning = '가야 해서 게임을 나감 (왼쪽 아님)'; }
)
$results = [Collections.Generic.List[object]]::new()
try {
    foreach ($model in $Models) {
        if ($model -notin $installed) { Write-Output "SKIP not installed: $model"; continue }
        $settings = [Valtrans.Models.AppSettings]::new()
        $settings.TranslationProvider = 'Ollama'; $settings.LocalAiModel = $model
        $settings.Game = 'VALORANT'; $settings.Map = 'Ascent'
        if ($settings.PSObject.Properties['ServerRegion']) { $settings.ServerRegion = 'JP' }
        $runtimeAllocation = $null
        foreach ($case in $cases) {
            $token = [Threading.CancellationTokenSource]::new(45000)
            $timer = [Diagnostics.Stopwatch]::StartNew()
            $output = $null; $failure = $null
            try { $output = $method.Invoke($translator, @($case.Text, $case.Target, $settings, $false, $token.Token, $true, $model)).GetAwaiter().GetResult() }
            catch { $failure = $_.Exception.GetBaseException().Message }
            finally { $timer.Stop(); $token.Dispose() }
            if ($null -eq $runtimeAllocation -and $null -eq $failure) {
                try {
                    $runtimeAllocation = (Invoke-RestMethod http://127.0.0.1:11434/api/ps -TimeoutSec 5).models |
                        Where-Object name -eq $model | Select-Object -First 1
                } catch { }
            }
            $entry = [pscustomobject]@{ Model = $model; Source = $case.Text; Target = $case.Target; ExpectedMeaning = $case.Meaning; Output = $output; Error = $failure; ElapsedMs = $timer.ElapsedMilliseconds; LoadedBytes = $runtimeAllocation.size; GpuLoadedBytes = $runtimeAllocation.size_vram }
            $results.Add($entry)
            Write-Output ($entry | ConvertTo-Json -Compress)
        }
        if ($model -notin $previouslyLoaded) {
            Invoke-RestMethod http://127.0.0.1:11434/api/generate -Method Post -ContentType 'application/json' -Body (@{model=$model; prompt=''; stream=$false; keep_alive=0} | ConvertTo-Json) -TimeoutSec 10 | Out-Null
        }
    }
} finally {
    $lite.Dispose()
    foreach ($model in $Models | Where-Object { $_ -in $installed -and $_ -notin $previouslyLoaded }) {
        try { Invoke-RestMethod http://127.0.0.1:11434/api/generate -Method Post -ContentType 'application/json' -Body (@{model=$model; prompt=''; stream=$false; keep_alive=0} | ConvertTo-Json) -TimeoutSec 10 | Out-Null } catch { }
    }
    $directory = Join-Path $PSScriptRoot '../artifacts/quality'
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $safeName = [IO.Path]::GetFileName($ReportName)
    if ($safeName.EndsWith('.json', [StringComparison]::OrdinalIgnoreCase)) { $safeName = $safeName[..($safeName.Length - 6)] }
    [IO.File]::WriteAllText((Join-Path $directory "$safeName.json"), (ConvertTo-Json -InputObject @($results.ToArray()) -Depth 6), [Text.UTF8Encoding]::new($false))
}
