param([string]$AssemblyPath = "$PSScriptRoot/../publish/Valtrans.dll", [switch]$LiveModel, [switch]$LiveLite)
$ErrorActionPreference = 'Stop'
Add-Type -Path (Resolve-Path -LiteralPath $AssemblyPath)
$glossary = [Valtrans.Services.GlossaryService]::new()
$settings = [Valtrans.Models.AppSettings]::new()
$settings.Game = 'VALORANT'
$settings.LocalAiModel = 'valtrans-hymt2:7b'
$lite = [Valtrans.Services.ValtransLiteService]::new((Join-Path $PSScriptRoot '../artifacts/briefing-lite-fixture'))
$translator = [Valtrans.Services.TranslatorService]::new($glossary, [Valtrans.Services.LocalAiService]::new(), $lite)
$none = [Threading.CancellationToken]::None
function Check($condition, $reason) { if (-not $condition) { throw $reason } }
try {
    foreach ($provider in @('Ollama', 'Hybrid')) {
        $settings.TranslationProvider = $provider
        foreach ($receive in @($false, $true)) {
            foreach ($source in @('B 헤븐에 두명', 'B헤븐에 두 명 있어', 'B 헤븐에 적 두 명이 있습니다')) {
                $result = $translator.TranslateAsync($source, 'EN', $settings, $receive, $none).GetAwaiter().GetResult()
                Check ($result -eq '2 B Heaven') "$provider : $source => $result"
            }
        }
    }
    foreach ($source in @('아군 두 명 B 헤븐', 'B 헤븐 두 명이면 기다려', 'B 헤븐에는 두 명 없어')) {
        $value = ''
        Check (-not $glossary.TryTranslateStructuredCallout($source, 'EN', $settings, [ref]$value)) "Meaning lost by rule: $source => $value"
    }
    foreach ($case in @(
        @('There are two enemies at B Heaven.', 'EN', '2 b heaven'),
        @('미드에 적 2명이 있습니다.', 'KO', '미드 2'),
        @('ミッドに敵が2人います。', 'JP', 'ミッド 2人'),
        @('Maybe there are two enemies at B Heaven.', 'EN', 'Maybe there are two enemies at B Heaven.'),
        @('Do not push until I flash.', 'EN', 'Do not push until I flash.')
    )) {
        $result = [Valtrans.Services.BriefingTranslationGuard]::CompactCommonCallout($case[0], $case[1])
        Check ($result -eq $case[2]) "Compaction: $result"
    }
    Add-Type -TypeDefinition @'
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
public sealed class BriefingModelFixture : HttpMessageHandler {
    public int Calls;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
        Calls++;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent("{\"message\":{\"content\":\"There are two enemies at B Heaven.\"},\"done_reason\":\"stop\"}") });
    }
}
'@
    $handler = [BriefingModelFixture]::new()
    $client = [Net.Http.HttpClient]::new($handler)
    $field = [Valtrans.Services.TranslatorService].GetField('_localHttp', [Reflection.BindingFlags]'Instance,NonPublic')
    $originalClient = $field.GetValue($translator)
    $field.SetValue($translator, $client)
    try {
        foreach ($provider in @('Ollama', 'Hybrid')) {
            $settings.TranslationProvider = $provider
            foreach ($receive in @($false, $true)) {
                $result = $translator.TranslateAsync('Bヘブンに二人敵がいる', 'EN', $settings, $receive, $none).GetAwaiter().GetResult()
                Check ($result -eq '2 b heaven') "Model response skipped compaction: $result"
            }
        }
        Check ($handler.Calls -eq 4) 'Model response path was not exercised'
    } finally { $field.SetValue($translator, $originalClient); $client.Dispose() }
    $errorMethod = [Valtrans.Services.TranslatorService].GetMethod('FriendlyCompatibilityError', [Reflection.BindingFlags]'Static,NonPublic')
    try {
        [Valtrans.Services.LiteTranslationGuard]::Validate('右には誰もいないと思います', '나는 오른쪽에 아무도 없다', 'KO', $settings, $glossary) | Out-Null
        throw 'Uncertain Lite result incorrectly accepted'
    } catch {
        $detail = $errorMethod.Invoke($null, @($_.Exception.GetBaseException()))
        Check ($detail -eq '품질 검사 보류 · 불확실성 표현 누락') "Missing useful diagnostic: $detail"
    }
    $plan = [Valtrans.Services.LiteUncertaintyPlan]::Create('右には誰もいないと思います', $settings, $glossary)
    Check ($null -ne $plan) 'Single-callout source uncertainty not recognized'
    foreach ($source in @('右に誰もいないと思いますか？', '右には誰もいないが左にはいると思います', '彼は右にいると思います', '右には誰もいない、左にはいると思います')) {
        Check ($null -eq [Valtrans.Services.LiteUncertaintyPlan]::Create($source, $settings, $glossary)) "Unsafe uncertainty scope: $source"
    }
    # Restoring source uncertainty must never bypass validation of the core.
    $blocked = $false
    try { [Valtrans.Services.LiteTranslationGuard]::Validate($plan.Core, '오른쪽에 사람이 있습니다', 'KO', $settings, $glossary) | Out-Null }
    catch { $blocked = $true }
    Check $blocked 'A positive core passed negative validation'
    foreach ($mode in @('PivotGood', 'PivotFirstLoss', 'PivotSecondLoss')) {
        $fixture = [Valtrans.Services.ValtransLiteService]::new((Join-Path $PSScriptRoot '../artifacts/pivot-fixture'))
        $start = [Diagnostics.ProcessStartInfo]::new((Get-Command pwsh).Source)
        $start.UseShellExecute = $false; $start.CreateNoWindow = $true
        $start.RedirectStandardInput = $true; $start.RedirectStandardOutput = $true
        $start.StandardInputEncoding = [Text.UTF8Encoding]::new($false)
        $start.StandardOutputEncoding = [Text.Encoding]::UTF8
        foreach ($arg in @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'Fake-LiteQualityHost.ps1'), '-Mode', $mode)) { $start.ArgumentList.Add($arg) }
        $process = [Diagnostics.Process]::Start($start)
        $flags = [Reflection.BindingFlags]'Instance,NonPublic'
        [Valtrans.Services.ValtransLiteService].GetField('_process', $flags).SetValue($fixture, $process)
        $pipeline = [Valtrans.Services.TranslatorService]::new($glossary, [Valtrans.Services.LocalAiService]::new(), $fixture)
        try {
            $settings.TranslationProvider = 'Lite'
            $rejected = $false
            try { $outcome = $pipeline.TranslateAsync('彼女は来ないかもしれない', 'KO', $settings, $false, $none).GetAwaiter().GetResult() }
            catch { $rejected = $true }
            Check ($rejected -eq ($mode -ne 'PivotGood')) "Pivot validation failed: $mode"
            if (-not $rejected) { Check ($outcome -eq '아마 그녀는 오지 않아') 'Valid pivot changed meaning' }
            $stats = [Valtrans.Services.ValtransLiteService].GetMethod('SendAsync', $flags).Invoke($fixture, @(@{command='stats'}, $none)).GetAwaiter().GetResult()
            try {
                $expected = if ($mode -eq 'PivotFirstLoss') { 1 } else { 2 }
                Check ($stats.RootElement.GetProperty('calls').GetInt32() -eq $expected) 'Bad pivot retried or continued'
            } finally { $stats.Dispose() }
        } finally { $fixture.Dispose() }
    }
    if ($LiveLite) {
        # Exercise extraction/startup of the host embedded in this build; never
        # assume an older executable name already exists in the user's AppData.
        $lite.WarmUpAsync('EN', 'KO', $none).GetAwaiter().GetResult() | Out-Null
        $settings.TranslationProvider = 'Lite'
        $report = $translator.TestCompatibilityAsync($settings, $none).GetAwaiter().GetResult()
        $report.Results | Format-Table Engine,Probe,Success,Detail
        Check ($report.Passed -eq 2) 'Live Lite compatibility probe failed'
        foreach ($receive in @($false, $true)) {
            $output = $translator.TranslateAsync('右には誰もいないと思います', 'KO', $settings, $receive, $none).GetAwaiter().GetResult()
            Check ($output -match '추정' -and $output -match '오른쪽' -and $output -match '없') "Live Lite lost meaning: $output"
            Write-Output "Lite: $output"
        }
        $settings.TranslationProvider = 'Hybrid'
        if ($LiveModel) {
            $report = $translator.TestCompatibilityAsync($settings, $none).GetAwaiter().GetResult()
            $report.Results | Format-Table Engine,Probe,Success,Detail
            Check ($report.Passed -eq 4) 'Live Hybrid compatibility probe failed'
        }
    }
    if ($LiveModel) {
        $method = [Valtrans.Services.TranslatorService].GetMethod('TranslateDirectAsync', [Reflection.BindingFlags]'Instance,NonPublic')
        foreach ($source in @('B 헤븐에 두명', '아마 B 헤븐에 두 명 있는 것 같아', 'A 메인 말고 B 헤븐', '내가 섬광 쓸 때까지 왼쪽으로 가지 마', 'C 롱에서 세 명 봤어', 'A 메인에 우리 팀 두 명 대기 중이야', 'Bヘブンに敵が二人いるかも')) {
            $cancel = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(90))
            try {
                $task = $method.Invoke($translator, @('Ollama', $source, 'EN', $settings, $cancel.Token))
                $raw = $task.GetAwaiter().GetResult()
                Write-Output "7B raw: $source => $raw"
            } finally { $cancel.Dispose() }
        }
    }
    Write-Output 'PASS: compound callouts, send/receive, retained conditions/uncertainty, Lite failure reason'
} finally { $lite.Dispose() }
