param([int]$Width = 1340, [int]$Height = 860, [ValidateSet(96,120,144,192)][int]$Dpi = 96, [switch]$ExpandOcr)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
$root = Split-Path $PSScriptRoot -Parent
$appMarkup = [xml](Get-Content -Raw -LiteralPath "$root/App.xaml")
$namespace = [Xml.XmlNamespaceManager]::new($appMarkup.NameTable)
$namespace.AddNamespace('p', 'http://schemas.microsoft.com/winfx/2006/xaml/presentation')
$resources = $appMarkup.SelectSingleNode('//p:Application.Resources', $namespace)
$resourceMarkup = '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">' + $resources.InnerXml + '</ResourceDictionary>'
$resourceMarkup = $resourceMarkup.Replace('pack://application:,,,/Assets/Fonts/', $root.Replace('\', '/') + '/Assets/Fonts/')
$previewApp = [System.Windows.Application]::new()
$previewApp.Resources = [System.Windows.Markup.XamlReader]::Parse($resourceMarkup)
$markup = Get-Content -Raw -LiteralPath "$root/MainWindow.xaml"
# Render the real layout without starting OCR, models, hotkeys or loading personal settings.
$markup = $markup -replace 'x:Class="[^"]+"', ''
$markup = $markup -replace '\s+\w+="\w+_On\w+"', ''
$markup = $markup.Replace('Source="Themes/Dashboard.xaml"', 'Source="' + $root.Replace('\', '/') + '/Themes/Dashboard.xaml"')
$markup = $markup.Replace('Assets/Valtrans.ico', $root.Replace('\', '/') + '/Assets/Valtrans.ico')
$markup = $markup.Replace('Assets/Valtrans-mark.png', $root.Replace('\', '/') + '/Assets/Valtrans-mark.png')
$markup = $markup.Replace('Assets/Valtrans-donate.png', $root.Replace('\', '/') + '/Assets/Valtrans-donate.png')
$markup = $markup.Replace('pack://application:,,,/Assets/Fonts/', $root.Replace('\', '/') + '/Assets/Fonts/')
$window = [System.Windows.Markup.XamlReader]::Parse($markup)
# Verify the actual embedded-family selection, not a silent system-font fallback.
foreach ($face in @(@('Normal', 'Regular'), @('SemiBold', 'SemiBold'), @('Bold', 'Bold'))) {
    $typeface = [System.Windows.Media.Typeface]::new($window.FontFamily, [System.Windows.FontStyles]::Normal, [System.Windows.FontWeights]::($face[0]), [System.Windows.FontStretches]::Normal)
    $glyph = $null
    if (-not $typeface.TryGetGlyphTypeface([ref]$glyph) -or
        $glyph.FontUri.LocalPath -notlike "*Pretendard-$($face[1]).ttf" -or $glyph.IsBoldSimulated) {
        throw "Pretendard face did not resolve correctly: $($face[1])"
    }
    foreach ($character in '가나다ABC'.ToCharArray()) {
        if (-not $glyph.CharacterToGlyphMap.ContainsKey([int]$character)) { throw "Missing font glyph: $character" }
    }
    Write-Output "Font verified: $($face[1])"
}
$window.FindName('HotkeyBox').Text = '\'
[xml]$project = Get-Content -LiteralPath "$root/Valtrans.csproj" -Raw
$window.FindName('VersionText').Text = 'Valtrans ' + $project.Project.PropertyGroup.Version
$window.FindName('LanguagePackStatusText').Text = 'EN ✓  JP ✓  KO ✓'
$window.FindName('GuideNextActionText').Text = '권장 엔진 준비로 번역과 Paddle OCR을 함께 준비하세요.'
foreach ($name in @('SendTargetCombo','GameCombo','OverlayTargetCombo','TranslationProviderCombo','LocalModelCombo','TestModeCombo','TestTargetCombo','OverlayDurationCombo','OcrChatFilterCombo','OcrStabilityCombo')) {
    $window.FindName($name).SelectedIndex = 0
}
foreach ($name in @('OcrEnCheck','OcrJpCheck','OcrKoCheck')) { $window.FindName($name).IsChecked = $true }
$window.FindName('OcrKoCheck').IsChecked = $false
$window.FindName('OcrEngineCombo').SelectedIndex = if ($ExpandOcr) { 1 } else { 0 }
$window.FindName('OcrEngineCombo').SelectedIndex = 0
$window.FindName('OcrDetailsPanel').IsExpanded = [bool]$ExpandOcr
if ($ExpandOcr) { $window.FindName('PaddleOcrStatusText').Text = '준비 필요 · 별도 GPU 실행 환경' }
$pages = @('Overview','Engines','Overlay','Lab','Guide')
$titles = @('채팅 대시보드','번역 엔진','오버레이 설정','번역 테스트 · 사전','시작 가이드 · 점검')
$navs = @('OverviewNavigation','AdvancedModeButton','OverlayNavigation','LabNavigation','GuideNavigation')
$output = Join-Path $root 'artifacts/dashboard'
[void][System.IO.Directory]::CreateDirectory($output)
foreach ($page in $pages) {
    for ($i = 0; $i -lt $pages.Count; $i++) {
        $window.FindName($pages[$i] + 'Page').Visibility = if ($pages[$i] -eq $page) { 'Visible' } else { 'Collapsed' }
        $window.FindName($navs[$i]).IsChecked = $pages[$i] -eq $page
    }
    $window.FindName('DashboardPageTitle').Text = $titles[[array]::IndexOf($pages, $page)]
    if ($page -eq 'Engines') {
        foreach ($n in @('HybridPanel','LitePanel','LocalAiPanel')) { $window.FindName($n).Visibility = 'Visible' }
    }
    if ($page -eq 'Guide') {
        foreach ($n in @('AutoRepairButton','RecommendedPrepareButton')) { $window.FindName($n).IsEnabled = $false }
        $window.FindName('RecommendedPrepareButton').Content = '권장 엔진 준비됨'
    }
    $visual = $window.Content
    [System.Windows.Media.VisualTreeHelper]::SetRootDpi($window, [System.Windows.DpiScale]::new($Dpi / 96.0, $Dpi / 96.0))
    if ([Math]::Abs([System.Windows.Media.VisualTreeHelper]::GetDpi($visual).PixelsPerInchX - $Dpi) -gt 0.01) {
        throw 'Preview DPI was not applied to the layout.'
    }
    $visual.Measure([System.Windows.Size]::new($Width, $Height))
    $visual.Arrange([System.Windows.Rect]::new(0, 0, $Width, $Height))
    $visual.UpdateLayout()
    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new([int][Math]::Ceiling($Width * $Dpi / 96.0), [int][Math]::Ceiling($Height * $Dpi / 96.0), $Dpi, $Dpi, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $suffix = if ($Dpi -eq 96) { '' } else { "-dpi$Dpi" }
    $path = Join-Path $output "$page-$Width$suffix.png"
    $stream = [System.IO.File]::Create($path)
    try { $encoder.Save($stream) } finally { $stream.Dispose() }
    Write-Output $path
}
