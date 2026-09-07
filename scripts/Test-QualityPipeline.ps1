param([string]$AssemblyPath = "$PSScriptRoot/../publish/Valtrans.dll")
$ErrorActionPreference = 'Stop'
Add-Type -Path (Resolve-Path -LiteralPath $AssemblyPath)
$glossary = [Valtrans.Services.GlossaryService]::new()
$settings = [Valtrans.Models.AppSettings]::new()
$settings.Game = 'VALORANT'; $settings.Map = 'Ascent'; $settings.ServerRegion = 'JP'
$staticFlags = [Reflection.BindingFlags]'Static,NonPublic'
$templateMethod = [Valtrans.Services.LocalAiService].GetMethod('HyMtChatTemplate', $staticFlags)
$stopsMethod = [Valtrans.Services.LocalAiService].GetMethod('HyMtStopTokens', $staticFlags)
$smallTemplate = $templateMethod.Invoke($null, @('valtrans-hymt2:1.8b'))
$largeTemplate = $templateMethod.Invoke($null, @('valtrans-hymt2:7b'))
if (-not $smallTemplate.Contains('<｜hy_User｜>') -or $smallTemplate.Contains('<|extra_0|>')) { throw '1.8B template changed incorrectly' }
if (-not $largeTemplate.Contains('<|extra_0|>') -or $largeTemplate.Contains('<｜hy_User｜>')) { throw '7B inherited 1.8B tokens' }
if ('<|eos|>' -notin $stopsMethod.Invoke($null, @('valtrans-hymt2:7b'))) { throw '7B stop token missing' }
Write-Output 'Separate Hy-MT2 1.8B/7B templates and stop tokens: PASS'
$settings.CustomGlossary['pizza'] = '피자'
$settings.CustomGlossary['irrelevant'] = 'should not be included'
$prompt = [Valtrans.Services.GameTranslationPrompt]::Build('save me near pizza, not mid', 'KO', $settings, $glossary, $false)
foreach ($required in @('VALORANT', 'Ascent', 'Japan', 'save me near pizza, not mid', 'no character limit')) {
    if (-not $prompt.Contains($required)) { throw "Missing prompt context: $required" }
}
if ($prompt.Contains('irrelevant') -or $prompt.Contains('{{VT') -or $prompt.Contains('35 characters')) { throw 'Unsafe/unrelated prompt preprocessing' }
$settings.CustomGlossary.Clear()
if ([Valtrans.Services.GameTranslationPrompt]::NormalizeRegion('unknown') -ne 'Auto') { throw 'Invalid region accepted' }
foreach ($source in @('not left, right if clear', 'do not push left, go right', '2 left, 1 right if clear')) {
    $briefing = ''
    if ([Valtrans.Services.TranslationFactGuard]::TryBuildSafeBriefing($source, 'KO', $settings, $glossary, [ref]$briefing)) {
        throw "Unsafe guessed deadline result: $source => $briefing"
    }
}
Write-Output 'Prompt context and conservative deadline: PASS'
foreach ($case in @(
    @{Source='2 left, 1 right'; Broken='왼쪽 1명, 오른쪽 2명'; Expected='왼쪽 2명, 오른쪽 1명'},
    @{Source='not left, right'; Broken='왼쪽도 오른쪽도 아님'; Expected='왼쪽 말고 오른쪽'},
    @{Source='右1人、左2人'; Broken='오른쪽 2명, 왼쪽 1명'; Expected='오른쪽 1명, 왼쪽 2명'}
)) {
    $actual = [Valtrans.Services.TranslationFactGuard]::Apply($case.Source, $case.Broken, 'KO', $settings, $glossary)
    if ($actual.Text -ne $case.Expected) { throw "Direction association: $($actual.Text)" }
}
Write-Output 'Exact directional pairs and correction negation: PASS'
$rejected = $false
try { [Valtrans.Services.TranslationFactGuard]::Apply('maybe two left, do not move', '움직이지 마', 'KO', $settings, $glossary) | Out-Null }
catch { $rejected = $_.Exception.GetBaseException().Message.Contains('번역 의미 확인 필요') }
if (-not $rejected) { throw 'Unknown sentence was repaired by guessing facts' }
$ordinary = [Valtrans.Services.TranslationFactGuard]::Apply('I left the game because I had to go', '가야 해서 게임을 나갔다', 'KO', $settings, $glossary)
if ($ordinary.Adjusted) { throw 'Past-tense left treated as direction' }
Write-Output 'Missing facts withheld, ordinary left preserved: PASS'

Add-Type -TypeDefinition @'
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
public sealed class QualityMockHandler : HttpMessageHandler {
    public string LastPayload = "";
    public int Calls;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
        LastPayload = await request.Content.ReadAsStringAsync(token); Calls++;
        return new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent("{\"message\":{\"content\":\"섬광 쓸 때까지 기다려\"},\"done_reason\":\"stop\"}")
        };
    }
}
'@
$mockLite = [Valtrans.Services.ValtransLiteService]::new()
$mockTranslator = [Valtrans.Services.TranslatorService]::new($glossary, [Valtrans.Services.LocalAiService]::new(), $mockLite)
$mockHandler = [QualityMockHandler]::new()
$mockClient = [Net.Http.HttpClient]::new($mockHandler)
$field = [Valtrans.Services.TranslatorService].GetField('_localHttp', [Reflection.BindingFlags]'Instance,NonPublic')
$oldClient = $field.GetValue($mockTranslator)
$field.SetValue($mockTranslator, $mockClient)
try {
    foreach ($receive in @($false, $true)) {
        $output = $mockTranslator.TranslateAsync('please wait until I flash', 'KO', $settings, $receive, [Threading.CancellationToken]::None).GetAwaiter().GetResult()
        if ($output -ne '섬광 쓸 때까지 기다려') { throw 'Model-first path did not return model result' }
        $payload = $mockHandler.LastPayload | ConvertFrom-Json
        if ($payload.model -ne $settings.LocalAiModel -or -not $payload.messages[0].content.Contains('Server region: Japan')) { throw 'Live model payload context missing' }
    }
    if ($mockHandler.Calls -ne 2) { throw 'Receive path did not prefer local model' }
} finally { $mockLite.Dispose(); $mockClient.Dispose(); $oldClient.Dispose() }
Write-Output 'Sending/receiving model-first route and actual payload context: PASS (mock HTTP)'

function New-Line([string]$Text, [int]$Y, [int]$X = 0) {
    $word = [Valtrans.Services.OcrPositionedWord]::new($Text, $X, $Y, 160, 18)
    [Valtrans.Services.OcrPositionedLine]::new($Text, $X, $Y, 160, 18, [Valtrans.Services.OcrPositionedWord[]]@($word))
}
$candidates = [System.Collections.Generic.Dictionary[string,System.Collections.Generic.IReadOnlyList[Valtrans.Services.OcrPositionedLine]]]::new()
$candidates['EN'] = [Valtrans.Services.OcrPositionedLine[]]@((New-Line '(파티) 이름: watch left' 10), (New-Line '(party) player: ??' 40), (New-Line '??' 70))
$candidates['JP'] = [Valtrans.Services.OcrPositionedLine[]]@((New-Line '(파티) 이름: watch left' 11), (New-Line '(party) player: 右にいる' 41), (New-Line '??' 71))
$candidates['KO'] = [Valtrans.Services.OcrPositionedLine[]]@((New-Line '(파티) 이름: watch left' 9), (New-Line '??' 39), (New-Line '오른쪽 조심' 69))
$ocr = [Valtrans.Services.OcrLineSelector]::Select($candidates)
if ($ocr.PositionedLines.Count -ne 3 -or $ocr.DetectedLanguage -ne 'MIXED') { throw 'Mixed OCR row grouping failed' }
if (-not $ocr.Text.Contains('watch left') -or -not $ocr.Text.Contains('右にいる') -or -not $ocr.Text.Contains('오른쪽 조심') -or $ocr.Text.Contains('??')) { throw "Mixed OCR text: $($ocr.Text)" }
if ($ocr.PositionedLines[1].Words.Count -ne 1) { throw 'OCR word coordinates lost' }
Write-Output 'Mixed-language OCR row selection: PASS (synthetic candidates, not image accuracy)'

$flags = [Reflection.BindingFlags]'Instance,NonPublic'
$sendMethod = [Valtrans.Services.ValtransLiteService].GetMethod('SendAsync', $flags)
$processField = [Valtrans.Services.ValtransLiteService].GetField('_process', $flags)
$lite = [Valtrans.Services.ValtransLiteService]::new((Join-Path $PSScriptRoot '../artifacts/protocol-fixture'))
$start = [Diagnostics.ProcessStartInfo]::new((Get-Command pwsh).Source)
$start.UseShellExecute = $false; $start.CreateNoWindow = $true
$start.RedirectStandardInput = $true; $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
$start.StandardInputEncoding = [Text.UTF8Encoding]::new($false)
$start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
foreach ($arg in @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'Fake-LiteHost.ps1'))) { $start.ArgumentList.Add($arg) }
$fake = [Diagnostics.Process]::Start($start)
$processField.SetValue($lite, $fake)
$none = [Threading.CancellationToken]::None
function Send-Fixture([string]$Command, [Threading.CancellationToken]$Token) {
    $request = [System.Collections.Generic.Dictionary[string,object]]::new()
    $request['command'] = $Command
    $sendMethod.Invoke($lite, @($request, $Token))
}
try {
    $ready = (Send-Fixture 'ready' $none).GetAwaiter().GetResult(); $ready.Dispose()
    $cancel = [Threading.CancellationTokenSource]::new(60)
    try {
        $first = Send-Fixture 'slow' $cancel.Token
        try { $first.GetAwaiter().GetResult() | Out-Null; throw 'Cancellation ignored' }
        catch { if (-not $cancel.IsCancellationRequested) { throw } }
        $queuedCancel = [Threading.CancellationTokenSource]::new(20)
        try {
            $queued = Send-Fixture 'must-not-send' $queuedCancel.Token
            try { $queued.GetAwaiter().GetResult() | Out-Null; throw 'Queued cancellation ignored' }
            catch { if (-not $queuedCancel.IsCancellationRequested) { throw } }
        } finally { $queuedCancel.Dispose() }
        $next = (Send-Fixture 'next' $none).GetAwaiter().GetResult()
        try { if ($next.RootElement.GetProperty('text').GetString() -ne 'next') { throw 'Cancelled response leaked into next request' } }
        finally { $next.Dispose() }
        if ($fake.HasExited) { throw 'Cancellation unnecessarily restarted the host' }
    } finally { $cancel.Dispose() }
    $stale = (Send-Fixture 'stale' $none).GetAwaiter().GetResult()
    try { if ($stale.RootElement.GetProperty('text').GetString() -ne 'stale') { throw 'Request ID mismatch accepted' } }
    finally { $stale.Dispose() }
    $rejected = $false
    try { (Send-Fixture 'badid' $none).GetAwaiter().GetResult() | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Missing response ID accepted' }
} finally { $lite.Dispose() }
Write-Output 'Lite cancellation drain, subsequent response, stale ID, missing ID: PASS'
