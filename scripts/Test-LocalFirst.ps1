param([string]$AssemblyPath = "$PSScriptRoot/../publish/Valtrans.dll")
$ErrorActionPreference = 'Stop'
Add-Type -Path (Resolve-Path -LiteralPath $AssemblyPath)
function Check($ok, $why) { if (-not $ok) { throw $why } }
$fixtureDirectory = Join-Path $PSScriptRoot ('../artifacts/tests/local-only-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($fixtureDirectory)
$fixturePath = Join-Path $fixtureDirectory 'settings.json'
$settingsStore = [Valtrans.Services.SettingsService]::new($fixturePath)
Check ($settingsStore.Load().TranslationProvider -eq 'Hybrid') 'Default not local'
Check ($settingsStore.Load().OcrEngine -eq 'Paddle') 'Fresh OCR default must be Paddle'
foreach ($schema in @(0,23,24)) {
    foreach ($choice in @('Windows','Paddle','invalid',$null)) {
        [IO.File]::WriteAllText($fixturePath, (@{ SettingsSchemaVersion=$schema; OcrEngine=$choice } | ConvertTo-Json))
        $expectedOcr = if ($schema -ge 24 -and $choice -eq 'Windows') { 'Windows' } else { 'Paddle' }
        $migrated = $settingsStore.Load()
        Check ($migrated.OcrEngine -eq $expectedOcr) 'OCR one-time migration failed'
        Check ($migrated.SettingsSchemaVersion -eq 26) 'OCR migration marker missing'
        $settingsStore.Save($migrated)
        Check ($settingsStore.Load().OcrEngine -eq $expectedOcr) 'OCR migration not stable after save'
        $migrated.OcrEngine = 'Windows'
        $settingsStore.Save($migrated)
        Check ($settingsStore.Load().OcrEngine -eq 'Windows') 'Later manual Windows selection lost'
    }
}
Write-Output 'PASS: existing Windows migrates to Paddle once; subsequent manual Windows choice persists'
foreach ($schema in @(0,21,22,23)) {
    foreach ($provider in @('DeepLX','DeepL','OpenAI','Unknown','Hybrid','Ollama','Lite')) {
        $fixture = @{
            SettingsSchemaVersion=$schema; TranslationProvider=$provider; LocalAiModel='valtrans-hymt2:7b'
            Hotkey='F8'; ServerRegion='JP'; CustomGlossary=@{abc='custom'}; OverlayFontSize=20
            ApiKey='fake-old-key'; ApiKeyProtected='invalid-old-blob'; DeepLApiKeyProtected='invalid-old-blob'
            AutoStartDockerDesktop=$true; AutoSwitchGameProfile=$false; DualRegionOcr=$false
            Map='Bind'; MapsByGame=@{VALORANT='Bind'}
        }
        [IO.File]::WriteAllText($fixturePath, ($fixture | ConvertTo-Json -Depth 4))
        $loaded = $settingsStore.Load()
        Check ($loaded.TranslationProvider -in @('Hybrid','Ollama','Lite')) 'Legacy cloud resurrected'
        if ($schema -ge 22) {
            $expected = if ($provider -in @('Hybrid','Ollama','Lite')) { $provider } else { 'Hybrid' }
            Check ($loaded.TranslationProvider -eq $expected -and $loaded.LocalAiModel -eq 'valtrans-hymt2:7b') 'Local preference lost'
        }
        Check ($loaded.Hotkey -eq 'F8' -and $loaded.CustomGlossary['abc'] -eq 'custom' -and
               $loaded.ServerRegion -eq 'JP' -and $loaded.OverlayFontSize -eq 20) 'User preference lost'
        Check ($loaded.AutoSwitchGameProfile -and $loaded.DualRegionOcr) 'Automatic OCR missing'
        $settingsStore.Save($loaded)
        $saved = [IO.File]::ReadAllText($fixturePath)
        Check ($saved -notmatch 'ApiKey|DeepL|Docker|ApiBaseUrl|MapsByGame|"Map"') 'Retired settings persisted'
    }
}
Write-Output 'PASS: 28 migrations; preferences preserved; retired keys/map settings not persisted'
$asm = [Valtrans.Services.TranslatorService].Assembly
foreach ($type in @('Services.DeepLApiService','Services.DlxDockerService','Services.CredentialProtector','RegionSelectorWindow','OcrCalibrationWindow')) {
    Check ($null -eq $asm.GetType("Valtrans.$type")) "Retired feature still shipped: $type"
}
[xml]$xml = Get-Content "$PSScriptRoot/../MainWindow.xaml" -Raw
$ns = [Xml.XmlNamespaceManager]::new($xml.NameTable)
$ns.AddNamespace('w','http://schemas.microsoft.com/winfx/2006/xaml/presentation')
$ns.AddNamespace('x','http://schemas.microsoft.com/winfx/2006/xaml')
$choices = $xml.SelectNodes('//w:ComboBox[@x:Name="TranslationProviderCombo"]/w:ComboBoxItem',$ns)
Check ($choices.Count -eq 3 -and @($choices.Tag | Where-Object { $_ -notin @('Hybrid','Lite','Ollama') }).Count -eq 0) 'Nonlocal provider UI'
Check ($xml.SelectNodes('//w:PasswordBox',$ns).Count -eq 0) 'API key input remains'
foreach ($name in @('OcrChatFilterCombo','OcrStabilityCombo','OcrAutoEnhanceCheck','OcrConsensusCheck')) {
    Check ($null -ne $xml.SelectSingleNode("//w:Expander[@x:Name='OcrDetailsPanel']//*[@x:Name='$name']",$ns)) "Scattered OCR option: $name"
}
Check ($xml.OuterXml -notmatch 'MapCombo|SelectRegion_OnClick|CalibrateOcr_OnClick|SelectLatestOcrRegion|DeepLxPanel|SimpleEnginePanel') 'Retired control remains'
Write-Output 'PASS: local-only assembly/UI; grouped OCR controls; no manual crop or map selector'
Add-Type -TypeDefinition @'
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
public sealed class LocalOnlyHttp : HttpMessageHandler {
    public int Calls;
    public string LastPath = "";
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) {
        if (!r.RequestUri.IsLoopback || r.RequestUri.Port != 11434 || r.Headers.Authorization != null)
            throw new Exception("Nonlocal or credential-bearing request");
        Calls++; LastPath = r.RequestUri.AbsolutePath;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent("{\"done_reason\":\"stop\",\"message\":{\"content\":\"섬광 쓸 때까지 기다려\"},\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"섬광 쓸 때까지 기다려\"}}]}")
        });
    }
}
'@
$lite = [Valtrans.Services.ValtransLiteService]::new((Join-Path $fixtureDirectory 'unused-lite'))
$glossary = [Valtrans.Services.GlossaryService]::new()
$translator = [Valtrans.Services.TranslatorService]::new($glossary,[Valtrans.Services.LocalAiService]::new(),$lite)
$handler = [LocalOnlyHttp]::new()
$client = [Net.Http.HttpClient]::new($handler)
$field = [Valtrans.Services.TranslatorService].GetField('_localHttp',[Reflection.BindingFlags]'Instance,NonPublic')
$oldClient = $field.GetValue($translator)
$field.SetValue($translator,$client)
try {
    $settings = [Valtrans.Models.AppSettings]::new()
    foreach ($provider in @('Ollama','Hybrid')) {
        $settings.TranslationProvider = $provider
        foreach ($model in @('valtrans-hymt2:1.8b','translategemma:4b')) {
            $settings.LocalAiModel = $model
            foreach ($receive in @($false,$true)) {
                $result = $translator.TranslateAsync('please wait until I flash','KO',$settings,$receive,
                    [Threading.CancellationToken]::None).GetAwaiter().GetResult()
                Check ($result -eq '섬광 쓸 때까지 기다려') 'Local translation changed'
                $path = if ($model.StartsWith('translategemma')) { '/v1/chat/completions' } else { '/api/chat' }
                Check ($handler.LastPath -eq $path) 'Wrong local protocol'
            }
        }
    }
    Check ($handler.Calls -eq 8) 'Wrong local routing count'
    foreach ($provider in @('OpenAI','DeepL','DeepLX')) {
        $settings.TranslationProvider = $provider
        $rejected = $false
        try { $translator.TranslateAsync('please wait until I flash','KO',$settings,$false,
                [Threading.CancellationToken]::None).GetAwaiter().GetResult() | Out-Null } catch { $rejected = $true }
        Check $rejected 'Retired provider not rejected'
    }
    Check ($handler.Calls -eq 8) 'Retired provider made request'
    foreach ($example in @(@('제트','Jett'),@('ジェット','Jett'),@('어센트','Ascent'),@('ヘイヴン','Haven'),@('バインド','Bind'))) {
        Check ($glossary.NormalizeNames($example[0],$settings) -eq $example[1]) 'Proper-name dictionary regression'
    }
    Check ($glossary.NormalizeLocations('램프',$settings) -eq 'Ramp') 'Map-specific override survived'
    $prompt = [Valtrans.Services.GameTranslationPrompt]::Build('ジェット Bヘブン','KO',$settings,$glossary,$false)
    Check ($prompt.Contains('Jett') -and -not $prompt.Contains('Map:')) 'Map context or missing name'
} finally { $lite.Dispose(); $client.Dispose(); $oldClient.Dispose() }
Write-Output 'PASS: loopback-only send/receive protocols, retired provider rejection, shared proper-name dictionary'

foreach ($name in @('OcrEngineCombo','InstallPaddleOcrButton','PreparePaddleOcrButton','PaddleOcrStatusText')) {
    $control = $xml.SelectSingleNode("//*[@x:Name='$name']",$ns)
    Check ($null -ne $control -and $null -eq $control.SelectSingleNode('ancestor::w:Expander',$ns)) "Primary OCR control hidden: $name"
}
Check ($null -ne $xml.SelectSingleNode("//w:Button[@x:Name='GuideOcrPrepareButton']",$ns)) 'Guide OCR setup action missing'
Write-Output 'PASS: visible OCR preparation; guide setup action; advanced-only expanders'

& "$PSScriptRoot/Run-E2ESmoke.ps1"
Write-Output 'PASS: E2E smoke (Test Arena capture path)'
