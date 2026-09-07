param([string]$AssemblyPath = "$PSScriptRoot/../publish/Valtrans.dll")
$ErrorActionPreference = 'Stop'
Add-Type -Path (Resolve-Path -LiteralPath $AssemblyPath)
Add-Type -TypeDefinition @'
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
public sealed class LocalFirstHttp : HttpMessageHandler {
    public string Url = "", Auth = "", Payload = "", Method = "";
    public int Calls, Code = 200;
    public string Body = "{\"translations\":[{\"text\":\"테스트 번역\"}]}";
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
        token.ThrowIfCancellationRequested();
        Calls++; Url = request.RequestUri.ToString(); Method = request.Method.Method;
        Auth = request.Headers.Authorization.ToString();
        Payload = request.Content == null ? "" : await request.Content.ReadAsStringAsync(token);
        var response = new HttpResponseMessage((HttpStatusCode)Code) { Content = new StringContent(Body) };
        if (Code == 429) response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(2));
        return response;
    }
}
'@
function Assert-True($value, $message) { if (-not $value) { throw $message } }
function Assert-Failure($action, $fragment) {
    $failed = $false
    try { & $action | Out-Null } catch { $failed = $_.Exception.GetBaseException().Message.Contains($fragment) }
    Assert-True $failed "Expected failure: $fragment"
}
$none = [Threading.CancellationToken]::None
$handler = [LocalFirstHttp]::new()
$client = [Net.Http.HttpClient]::new($handler)
$service = [Valtrans.Services.DeepLApiService]::new($client)
try {
    Assert-Failure { $service.TranslateAsync('example', 'KO', '', $none).GetAwaiter().GetResult() } 'API 키'
    Assert-True ($handler.Calls -eq 0) 'Missing key caused network request'
    $result = $service.TranslateAsync('example', 'JP', 'fake-deepl:fx', $none).GetAwaiter().GetResult()
    Assert-True ($result -eq '테스트 번역') 'Result parse'
    Assert-True ($handler.Url -eq 'https://api-free.deepl.com/v2/translate') 'Free endpoint'
    Assert-True ($handler.Auth -eq 'DeepL-Auth-Key fake-deepl:fx') 'DeepL authorization'
    $body = $handler.Payload | ConvertFrom-Json
    Assert-True ($body.target_lang -eq 'JA' -and $body.text[0] -eq 'example') 'Japanese/payload mapping'
    $service.TranslateAsync('example', 'KO', 'fake-pro-key', $none).GetAwaiter().GetResult() | Out-Null
    Assert-True ($handler.Url -eq 'https://api.deepl.com/v2/translate') 'Pro endpoint'
    $handler.Body = '{"character_count":123,"character_limit":500000}'
    $service.CheckUsageAsync('fake-deepl:fx', $none).GetAwaiter().GetResult() | Out-Null
    Assert-True ($handler.Method -eq 'GET' -and $handler.Url.EndsWith('/usage') -and $handler.Payload -eq '') 'Usage must not translate'
    $handler.Body = '{"translations":[]}'
    Assert-Failure { $service.TranslateAsync('example', 'KO', 'fake-deepl:fx', $none).GetAwaiter().GetResult() } '비어'
    foreach ($status in @(403,456,503)) {
        $handler.Code = $status
        $before = $handler.Calls
        Assert-Failure { $service.TranslateAsync('example', 'KO', 'fake-deepl:fx', $none).GetAwaiter().GetResult() } "$status"
        Assert-True ($handler.Calls -eq $before + 1) 'Unexpected retry or paid fallback'
    }
    $handler.Code = 429
    Assert-Failure { $service.TranslateAsync('example', 'KO', 'fake-deepl:fx', $none).GetAwaiter().GetResult() } '429'
    $before = $handler.Calls
    Assert-Failure { $service.TranslateAsync('example', 'KO', 'fake-deepl:fx', $none).GetAwaiter().GetResult() } '일시 중지'
    Assert-True ($handler.Calls -eq $before) 'Retry-After ignored'
} finally { $client.Dispose() }
Write-Output 'Official API mock: auth, endpoints, JP mapping, usage-only check, empty result, errors, no retry/fallback: PASS'

# Use isolated fixtures; never load or save the real settings file.
$directory = Join-Path $PSScriptRoot ('../artifacts/tests/local-first-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($directory)
$path = Join-Path $directory 'settings.json'
$store = [Valtrans.Services.SettingsService]::new($path)
$settings = $store.Load()
Assert-True ($settings.TranslationProvider -eq 'Hybrid' -and -not $settings.AutoStartDockerDesktop) 'New-install defaults'
foreach ($provider in @('DeepLX','OpenAI','Hybrid','Ollama','Lite')) {
    $fixture = @{ SettingsSchemaVersion=21; TranslationProvider=$provider; LocalAiModel='valtrans-hymt2:7b'; AutoStartDockerDesktop=$true; DeepLxMode='Docker'; Hotkey='F8'; ServerRegion='JP'; CustomGlossary=@{abc='custom'} }
    [IO.File]::WriteAllText($path, ($fixture | ConvertTo-Json -Depth 4))
    $loaded = $store.Load()
    $expected = if ($provider -in @('DeepLX','OpenAI')) { 'Hybrid' } else { $provider }
    Assert-True ($loaded.TranslationProvider -eq $expected -and -not $loaded.AutoStartDockerDesktop) 'Migration defaults'
    Assert-True ($loaded.LocalAiModel -eq 'valtrans-hymt2:7b' -and $loaded.Hotkey -eq 'F8' -and $loaded.CustomGlossary['abc'] -eq 'custom' -and $loaded.ServerRegion -eq 'JP') 'Migration lost user preference'
}
$settings = [Valtrans.Models.AppSettings]::new()
$settings.ApiKey = 'fake-openai-secret'
$settings.DeepLApiKey = 'fake-deepl-secret:fx'
$store.Save($settings)
$disk = [IO.File]::ReadAllText($path)
Assert-True (-not $disk.Contains('fake-openai') -and -not $disk.Contains('fake-deepl')) 'Plaintext credential leak'
$loaded = $store.Load()
Assert-True ($loaded.ApiKey -eq $settings.ApiKey -and $loaded.DeepLApiKey -eq $settings.DeepLApiKey) 'Keys mixed/lost'
Assert-True ($loaded.TranslationProvider -eq 'Hybrid') 'Saving key enabled cloud'
$settings.TranslationProvider = 'DeepL'
$settings.ApiBaseUrl = 'http://localhost:11434/v1'
$store.Save($settings)
Assert-True ($store.Load().TranslationProvider -eq 'DeepL') 'Explicit new cloud choice not preserved'
Write-Output 'Defaults/migration, retained model/glossary/hotkey, separate encrypted keys, opt-in persisted: PASS'

$rawXaml = Get-Content -LiteralPath "$PSScriptRoot/../MainWindow.xaml" -Raw
[xml]$xml = $rawXaml
$namespaces = [Xml.XmlNamespaceManager]::new($xml.NameTable)
$namespaces.AddNamespace('w', 'http://schemas.microsoft.com/winfx/2006/xaml/presentation')
$namespaces.AddNamespace('x', 'http://schemas.microsoft.com/winfx/2006/xaml')
$choices = $xml.SelectNodes('//w:ComboBox[@x:Name="TranslationProviderCombo"]/w:ComboBoxItem', $namespaces)
Assert-True ($choices[0].Tag -eq 'Hybrid' -and 'DeepLX' -notin $choices.Tag -and 'DeepL' -in $choices.Tag) 'Provider UI mismatch'
$legacy = $xml.SelectSingleNode('//w:Border[@x:Name="DeepLxPanel"]', $namespaces)
Assert-True ($legacy.Visibility -eq 'Collapsed' -and $legacy.IsEnabled -eq 'False') 'Legacy Docker UI reachable'
Write-Output 'Provider UI: local first, official API optional, legacy Docker hidden/disabled: PASS'

$lite = [Valtrans.Services.ValtransLiteService]::new()
$translator = [Valtrans.Services.TranslatorService]::new([Valtrans.Services.GlossaryService]::new(), [Valtrans.Services.LocalAiService]::new(), $lite)
$handler = [LocalFirstHttp]::new()
$client = [Net.Http.HttpClient]::new($handler)
$apiField = [Valtrans.Services.TranslatorService].GetField('_deepLApi', [Reflection.BindingFlags]'Instance,NonPublic')
$apiField.SetValue($translator, [Valtrans.Services.DeepLApiService]::new($client))
try {
    $handler.Body = '{"translations":[{"text":"섬광 쓸 때까지 기다려"}]}'
    foreach ($receive in @($false,$true)) {
        $result = $translator.TranslateAsync('please wait until I flash','KO',$settings,$receive,$none).GetAwaiter().GetResult()
        Assert-True ($result -eq '섬광 쓸 때까지 기다려') 'Official API send/receive routing'
        Assert-True ($handler.Auth -eq 'DeepL-Auth-Key fake-deepl-secret:fx') 'OpenAI key sent to DeepL'
    }
    $before = $handler.Calls
    $settings.TranslationProvider = 'Hybrid'
    $translator.TranslateAsync('nt','KO',$settings,$false,$none).GetAwaiter().GetResult() | Out-Null
    Assert-True ($handler.Calls -eq $before) 'Local rule used cloud'
} finally { $lite.Dispose(); $client.Dispose() }
Write-Output 'Send/receive official routing; local rule makes no cloud request: PASS'
