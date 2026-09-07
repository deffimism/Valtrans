param([int]$Width = 1340, [int]$Height = 860)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
$root = Split-Path $PSScriptRoot -Parent
$markup = Get-Content -Raw -LiteralPath "$root/MainWindow.xaml"
# Render the real layout without starting OCR, models, hotkeys or loading personal settings.
$markup = $markup -replace 'x:Class="[^"]+"', ''
$markup = $markup -replace '\s+\w+="\w+_On\w+"', ''
$markup = $markup.Replace('Source="Themes/Dashboard.xaml"', 'Source="' + $root.Replace('\', '/') + '/Themes/Dashboard.xaml"')
$markup = $markup.Replace('Assets/Valtrans.ico', $root.Replace('\', '/') + '/Assets/Valtrans.ico')
$window = [System.Windows.Markup.XamlReader]::Parse($markup)
$window.FontFamily = [System.Windows.Media.FontFamily]::new('Segoe UI, Malgun Gothic')
$window.FindName('HotkeyBox').Text = '\'
[xml]$project = Get-Content -LiteralPath "$root/Valtrans.csproj" -Raw
$window.FindName('VersionText').Text = 'Valtrans ' + $project.Project.PropertyGroup.Version
$window.FindName('LanguagePackStatusText').Text = 'EN ✓  JP ✓  KO ✓'
$window.FindName('GuideNextActionText').Text = '번역 엔진을 준비한 뒤 게임 채팅 영역을 선택하세요.'
foreach ($name in @('SendTargetCombo','GameCombo','OverlayTargetCombo','TranslationProviderCombo','LocalModelCombo','TestModeCombo','TestTargetCombo','OverlayDurationCombo','OcrChatFilterCombo','OcrStabilityCombo')) {
    $window.FindName($name).SelectedIndex = 0
}
$map = [System.Windows.Controls.ComboBoxItem]::new()
$map.Content = '맵 자동 · 공통'
[void]$window.FindName('MapCombo').Items.Add($map)
$window.FindName('MapCombo').SelectedIndex = 0
foreach ($name in @('OcrEnCheck','OcrJpCheck','OcrKoCheck')) { $window.FindName($name).IsChecked = $true }
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
        foreach ($n in @('DeepLxPanel','OpenAiPanel','SimpleEnginePanel')) { $window.FindName($n).Visibility = 'Collapsed' }
    }
    $visual = $window.Content
    $visual.Measure([System.Windows.Size]::new($Width, $Height))
    $visual.Arrange([System.Windows.Rect]::new(0, 0, $Width, $Height))
    $visual.UpdateLayout()
    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($Width, $Height, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $path = Join-Path $output "$page-$Width.png"
    $stream = [System.IO.File]::Create($path)
    try { $encoder.Save($stream) } finally { $stream.Dispose() }
    Write-Output $path
}
