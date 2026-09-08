param([string]$AssemblyPath = "$PSScriptRoot/../publish/Valtrans.dll")
$ErrorActionPreference = 'Stop'
Add-Type -Path (Resolve-Path -LiteralPath $AssemblyPath)
$glossary = [Valtrans.Services.GlossaryService]::new()
$settings = [Valtrans.Models.AppSettings]::new()
$settings.Game = 'VALORANT'; $settings.Map = 'Ascent'; $settings.TranslationProvider = 'Lite'
$rejections = @(
    @('wait until I flash','??','KO'),
    @('wait until I flash','기다려 <unk>','KO'),
    @('wait until I flash','기다려 �','KO'),
    @('wait until I flash','기다려 기다려 기다려 기다려','KO'),
    @('please wait','待って待って待って','JP'),
    @('please wait until I flash','please wait until I flash','KO'),
    @('maybe she is near us','그녀가 우리 근처에 있어','KO'),
    @('그녀는 오지 않을 수도 있어','She is not coming','EN'),
    @('she is not coming today','그녀는 오늘 와','KO'),
    @('do not move until I flash','내가 섬광 쓸 때까지 움직여','KO'),
    @('do not move until I flash','내가 섬광 쓸 때까지 움직이지 않았어','KO'),
    @('왼쪽으로 가지 마세요','Go left','EN'),
    @('左には行かないで、右に行って','Turn left and turn right','EN'),
    @('右には誰もいないと思います','오른쪽에 누군가 있어','KO'),
    @('maybe two left, do not move','움직이지 마','KO'),
    @('I have 7 hp','체력 9야','KO'),
    @('I dealt 120 damage','피해 줬어','KO'),
    @("hello`nplease wait",'기다려','KO')
)
foreach ($case in $rejections) {
    $rejected = $false
    try { [Valtrans.Services.LiteTranslationGuard]::Validate($case[0],$case[1],$case[2],$settings,$glossary) | Out-Null }
    catch { $rejected = $_.Exception.GetBaseException().Message.Contains('Lite 번역 확인 필요') }
    if (-not $rejected) { throw "Unsafe Lite result accepted: $($case -join ' => ')" }
}
$accepted = @(
    @('wait until I flash','내가 섬광 쓸 때까지 기다려','KO'),
    @('maybe she is near us','아마 우리 근처에 있어','KO'),
    @('She might not come','그녀는 오지 않을 수도 있어','KO'),
    @('do not push left, go right','왼쪽으로 밀지 말고 오른쪽으로 가','KO'),
    @('左には行かないで、右に行って','Go right without going left','EN'),
    @('右には誰もいないと思います','오른쪽엔 아무도 없는 것 같아','KO'),
    @('Please wait...','잠깐만...','KO'),
    @('really??','정말??','KO'),
    @('go go go','가 가 가','KO'),
    @('Jett','Jett','KO'),
    @('I left the game because I had to go','가야 해서 게임에서 나갔어','KO'),
    @('there are 2 players on the left','왼쪽에 두 명 있어','KO'),
    @('2 players left','左に二人','JP'),
    @('7 hp','seven hp','EN'),
    @("I don't know where she went",'어디로 갔는지 모르겠어','KO'),
    @('that does not kill in one shot','그건 한 발에 안 죽어','KO'),
    @('please stop moving','움직이는 거 멈춰','KO'),
    @("hello`nplease wait","안녕`n기다려",'KO')
)
foreach ($case in $accepted) {
    try { $value = [Valtrans.Services.LiteTranslationGuard]::Validate($case[0],$case[1],$case[2],$settings,$glossary) }
    catch { throw "Valid result rejected: $($case -join ' => '): $($_.Exception.GetBaseException().Message)" }
    if ($value.Replace("`r",'') -ne $case[1].Replace("`r",'')) { throw "Valid Lite result was rewritten: $($case -join ' => ')" }
}
Write-Output "Lite guard: $($rejections.Count) suspicious cases blocked; $($accepted.Count) valid cases preserved"

Add-Type -TypeDefinition @'
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
public sealed class FailedLocalForLite : HttpMessageHandler {
    public int Calls;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
        Calls++;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) {
            Content = new StringContent("{\"error\":\"fixture model unavailable\"}") });
    }
}
'@
$flags = [Reflection.BindingFlags]'Instance,NonPublic'
$none = [Threading.CancellationToken]::None
foreach ($mode in @('Good','Broken','Negative','WrongCount','WrongType')) {
    $lite = [Valtrans.Services.ValtransLiteService]::new((Join-Path $PSScriptRoot '../artifacts/lite-quality-fixture'))
    $start = [Diagnostics.ProcessStartInfo]::new((Get-Command pwsh).Source)
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true; $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    $start.StandardInputEncoding = [Text.UTF8Encoding]::new($false)
    $start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    foreach ($arg in @('-NoProfile','-File', (Join-Path $PSScriptRoot 'Fake-LiteQualityHost.ps1'), '-Mode', $mode)) { $start.ArgumentList.Add($arg) }
    $process = [Diagnostics.Process]::Start($start)
    [Valtrans.Services.ValtransLiteService].GetField('_process',$flags).SetValue($lite,$process)
    $translator = [Valtrans.Services.TranslatorService]::new($glossary,[Valtrans.Services.LocalAiService]::new(),$lite)
    $handler = [FailedLocalForLite]::new()
    $client = [Net.Http.HttpClient]::new($handler)
    $field = [Valtrans.Services.TranslatorService].GetField('_localHttp',$flags)
    $original = $field.GetValue($translator); $field.SetValue($translator,$client)
    try {
        foreach ($provider in @('Lite','Hybrid')) {
            $settings.TranslationProvider = $provider
            foreach ($receive in @($false,$true)) {
                $rejected = $false
                $source = if ($mode -eq 'Negative') { 'do not go right until I tell you' } else { 'please wait until I flash' }
                try { $result = $translator.TranslateAsync($source,'KO',$settings,$receive,$none).GetAwaiter().GetResult() }
                catch { $rejected = $true }
                if ($mode -eq 'Good' -and ($rejected -or $result -ne '섬광 쓸 때까지 기다려')) { throw 'Good result rejected' }
                if ($mode -ne 'Good' -and -not $rejected) { throw "Pipeline accepted $mode in $provider" }
            }
        }
        if ($handler.Calls -ne 2) { throw 'Hybrid repeated local AI or Lite invoked AI implicitly' }
        $request = @{ command='stats' }
        $stats = [Valtrans.Services.ValtransLiteService].GetMethod('SendAsync',$flags).Invoke($lite,@($request,$none)).GetAwaiter().GetResult()
        try { if ($stats.RootElement.GetProperty('calls').GetInt32() -ne 4) { throw 'Unexpected Lite retry/missing call' } }
        finally { $stats.Dispose() }
    } finally { $lite.Dispose(); $client.Dispose(); $original.Dispose() }
}
Write-Output 'Lite-only and Hybrid send/receive: raw validation, malformed response, single AI attempt, no retry: PASS'
