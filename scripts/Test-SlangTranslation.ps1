param([string]$AssemblyPath = "$PSScriptRoot/../publish/Valtrans.dll")
$ErrorActionPreference = 'Stop'
Add-Type -Path (Resolve-Path -LiteralPath $AssemblyPath)
$glossary = [Valtrans.Services.GlossaryService]::new()
$report = ([Valtrans.Services.TranslationRegressionService]::new($glossary)).Run([Valtrans.Models.AppSettings]::new())
foreach ($result in $report.Results) {
    if (-not $result.Passed) { throw "$($result.Name): $($result.Detail)" }
}
Write-Output "Rules: $($report.Passed)/$($report.Total)"

$lite = [Valtrans.Services.ValtransLiteService]::new($null)
$translator = [Valtrans.Services.TranslatorService]::new($glossary, [Valtrans.Services.LocalAiService]::new(), $lite)
$count = 0
try {
    foreach ($provider in @('Hybrid', 'Lite', 'Ollama', 'DeepLX', 'OpenAI')) {
        $settings = [Valtrans.Models.AppSettings]::new()
        $settings.TranslationProvider = $provider
        $settings.Game = 'VALORANT'
        $settings.DeepLxUrl = 'http://127.0.0.1:1/translate'
        $settings.DeepLxAutoFallback = $false
        $cases = @(
            @{ Text = "omw`nJett lit"; Target = 'KO'; Expected = "가는 중`nJett · 피해 입음" },
            @{ Text = "힐좀`n피킹 ㄴㄴ"; Target = 'EN'; Expected = "need heal`ndon't peek" },
            @{ Text = "np`nJett low"; Target = 'JP'; Expected = "大丈夫`nJett · ロー" },
            @{ Text = "NP!!!`n제트 딸피`n포바"; Target = 'KO'; Expected = "괜찮아`nJett · 딸피`nforce buy" }
        )
        foreach ($case in $cases) {
            $timeout = [System.Threading.CancellationTokenSource]::new(5000)
            try {
                $task = $translator.TranslateAsync($case.Text, $case.Target, $settings, $true, $timeout.Token)
                $actual = $task.GetAwaiter().GetResult().Replace("`r", '')
                if ($actual -cne $case.Expected) { throw "$provider / $($case.Target): $actual" }
                $count++
            }
            finally { $timeout.Dispose() }
        }
    }
}
finally { $lite.Dispose() }
Write-Output "Provider / multiline routes: $count/20"
