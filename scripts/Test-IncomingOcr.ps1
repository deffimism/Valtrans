param([string]$AssemblyPath = "$PSScriptRoot/../publish/Valtrans.dll")
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
Add-Type -Path (Resolve-Path -LiteralPath $AssemblyPath)
function Check($condition, $message) { if (-not $condition) { throw $message } }
$glossary = [Valtrans.Services.GlossaryService]::new()
$settings = [Valtrans.Models.AppSettings]::new()
$filter = [Valtrans.Services.GameChatFilterService]::new($glossary)
foreach ($case in @(
    @('(팀) 나: わ か り ま し た','わかりました'),
    @('偲 ) L ト : ミ ッ ド 2','ミッド2'),
    @('(팀) 나: ミ ッ ド 二 つ','ミッド二つ'),
    @('(팀) 나: hello','hello'),
    @('(팀) 나: do not push left','do not push left')
)) {
    $body = [Valtrans.Services.ChatTextSanitizer]::NormalizeOcrBody($case[0])
    Check ($body -eq $case[1]) "OCR body mismatch: $body"
    Check ($filter.Filter($body, 'All', $settings).Keep) "Own chat incorrectly filtered: $body"
}
Check ($filter.Filter('ミッド2', 'Briefing', $settings).Keep) 'Japanese mid callout filtered'
function Line($text, $y) {
    [Valtrans.Services.OcrPositionedLine]::new($text, 10, $y, 250, 20, [Valtrans.Services.OcrPositionedWord[]]@())
}
function Frame($lines) {
    [Valtrans.Services.OcrReadResult]::new(($lines.Text -join "`n"), 'JP', 1, $true,
        [Valtrans.Services.OcrPositionedLine[]]$lines, $false, 60, 20, 60, 0, $false, 0, 0, 0, 0)
}
$consensus = [Valtrans.MainWindow].GetMethod('TryBuildOcrConsensus', [Reflection.BindingFlags]'Static,NonPublic')
$first = Frame @((Line '(팀) 나: ミ ッ ド 2' 20), (Line 'input cursor first' 60))
$second = Frame @((Line '偲 ) L ト : ミ ッ ド 2' 20), (Line 'totally different typing' 60))
$argsList = [object[]]@($first, $second, $null)
Check ($consensus.Invoke($null, $argsList)) 'Changing input row blocked stable chat'
Check ($argsList[2].PositionedLines.Count -eq 1) 'Consensus lost geometry or admitted unconfirmed row'
Check ($argsList[2].Text -eq 'ミッド2') 'Consensus retained nickname/input noise'
$argsList = [object[]]@((Frame @((Line 'ミッド2' 20))), (Frame @((Line 'ミッド3' 20))), $null)
Check (-not $consensus.Invoke($null, $argsList)) 'Changed count accepted without confirmation'

# Instantiate the overlay without displaying a window or activating game input.
$appMarkup = [xml](Get-Content -Raw "$PSScriptRoot/../App.xaml")
$ns = [Xml.XmlNamespaceManager]::new($appMarkup.NameTable)
$ns.AddNamespace('p','http://schemas.microsoft.com/winfx/2006/xaml/presentation')
$resources = $appMarkup.SelectSingleNode('//p:Application.Resources', $ns)
$app = [Windows.Application]::new()
$app.Resources = [Windows.Markup.XamlReader]::Parse('<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">' + $resources.InnerXml + '</ResourceDictionary>')
$overlay = [Valtrans.OverlayWindow]::new()
try {
    $overlay.SetBackgroundOpacity(0); $overlay.SetBorderOpacity(0); $overlay.SetClickThrough($true)
    Check ($overlay.FindName('MoveBar').Visibility -eq 'Collapsed') 'Locked overlay still shows move bar'
    Check ($overlay.FindName('OverlayRoot').Background.Color.A -eq 0) 'Locked transparency changed'
    $overlay.SetClickThrough($false)
    Check ($overlay.FindName('MoveBar').Visibility -eq 'Visible') 'Edit bar missing'
    Check ($overlay.FindName('OverlayRoot').Background.Color.A -gt 0) 'Transparent edit area cannot receive mouse'
    Check ($overlay.FindName('ResizeHandle').Width -ge 36) 'Resize target too small'
    $overlay.SetClickThrough($true)
    Check ($overlay.FindName('OverlayRoot').BorderBrush.Color.A -eq 0) 'Border opacity not restored'
    Check ($overlay.FindName('OverlayRoot').Background.Color.A -eq 0) 'Background opacity not restored'
} finally { $overlay.Close() }
$outlineBounds = [Valtrans.OcrRegionPreviewWindow].GetMethod('OutlineBounds', [Reflection.BindingFlags]'Static,NonPublic')
foreach ($region in @([Drawing.Rectangle]::new(51,1567,836,490), [Drawing.Rectangle]::new(-1800,120,800,400))) {
    $outer = $outlineBounds.Invoke($null, @($region))
    Check ($outer.X + 3 -eq $region.X -and $outer.Y + 3 -eq $region.Y) 'Preview offset differs from physical capture origin'
    Check ($outer.Width - 6 -eq $region.Width -and $outer.Height - 6 -eq $region.Height) 'Preview covers captured pixels'
}
$preview = [Valtrans.OcrRegionPreviewWindow]::new()
try {
    Check ($preview.Topmost -and -not $preview.ShowActivated -and -not $preview.ShowInTaskbar) 'Preview activates or is not an overlay'
    Check ($preview.Background.Color.A -eq 0) 'Preview covers game background'
    $preview.ShowRegion([Drawing.Rectangle]::Empty)
    Check (-not $preview.IsVisible) 'Invalid OCR region displayed'
} finally { $preview.Close() }
'PASS: OCR body normalization, self chat, partial consensus/geometry, number protection, overlay edit/lock states'
'PASS: OCR preview physical bounds (4K/negative coordinates), transparent/nonactivating window, invalid-region guard'
