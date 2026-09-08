param([string]$AssemblyPath = "$PSScriptRoot/../publish/Valtrans.dll")
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
Add-Type -Path (Resolve-Path -LiteralPath $AssemblyPath)
function Check($condition, $message) { if (-not $condition) { throw $message } }
foreach ($full in @([Drawing.Rectangle]::new(51,1567,836,490), [Drawing.Rectangle]::new(-1900,0,900,400), [Drawing.Rectangle]::new(0,0,20,20))) {
    $latest = [Valtrans.Services.DualOcrRegions]::Resolve($full, $null)
    Check ($full.Contains($latest) -and $latest.Width -ge 20 -and $latest.Height -ge 20) 'Default crop outside full region'
    $fraction = [Valtrans.Services.DualOcrRegions]::FromSelection($full, $latest)
    Check ([Valtrans.Services.DualOcrRegions]::Resolve($full, $fraction) -eq $latest) 'Relative crop roundtrip lost pixels'
    $fraction.Y = [double]::NaN
    Check ($full.Contains([Valtrans.Services.DualOcrRegions]::Resolve($full, $fraction))) 'Invalid saved fraction escaped bounds'
}
$failed = $false
try { [Valtrans.Services.DualOcrRegions]::FromSelection([Drawing.Rectangle]::new(0,0,200,200), [Drawing.Rectangle]::new(180,180,80,80)) | Out-Null }
catch { $failed = $true }
Check $failed 'Out-of-region selection was accepted'
function Line($text, $y) {
    [Valtrans.Services.OcrPositionedLine]::new($text,0,$y,100,18,
        [Valtrans.Services.OcrPositionedWord[]]@([Valtrans.Services.OcrPositionedWord]::new($text,0,$y,100,18)))
}
function Frame($lines) {
    [Valtrans.Services.OcrReadResult]::new(($lines.Text -join "`n"),'JP',1,$true,
        [Valtrans.Services.OcrPositionedLine[]]$lines,$false,60,18,60,0,$false,0,0,0,0)
}
$full = Frame @((Line 'long message begins' 0),(Line 'continued briefing' 25),(Line 'ミッド2' 100))
$latest = Frame @((Line 'ミッド3' 0),(Line 'hello' 30))
$merged = [Valtrans.Services.DualOcrRegions]::Merge($full,$latest,0,100)
Check ($merged.PositionedLines.Count -eq 4) 'Overlap duplicated, or full history/continuation lost'
Check ($merged.Text.Contains('ミッド2') -and -not $merged.Text.Contains('ミッド3')) 'Conflicting crop was appended'
Check ($merged.PositionedLines[3].Y -eq 130 -and $merged.PositionedLines[3].Words[0].Y -eq 130) 'Crop coordinates were not restored'
Check ([Valtrans.Services.DualOcrRegions]::Fingerprint('(팀) 나: ミ ッ ド 2') -eq [Valtrans.Services.DualOcrRegions]::Fingerprint('偲 ) L ト : ミッド2')) 'Header caused false latest-text change'

$assembly = [Valtrans.Services.WindowsOcrService].Assembly
$pixelsType = $assembly.GetType('Valtrans.Services.CapturedPixels')
$bytes = [byte[]]::new(40*40*4)
for($i=0;$i -lt $bytes.Length;$i++) { $bytes[$i] = $i % 251 }
$pixels = [Activator]::CreateInstance($pixelsType, [object[]]@($bytes,40,40,[ulong]0))
$cropMethod = [Valtrans.Services.WindowsOcrService].GetMethod('CropPixels',[Reflection.BindingFlags]'Static,NonPublic')
$crop = $cropMethod.Invoke($null,@($pixels,[Drawing.Rectangle]::new(3,5,20,25)))
Check ($crop.Bytes.Length -eq 20*25*4) 'Crop buffer size mismatch'
for($row=0;$row -lt 25;$row++) {
    for($column=0;$column -lt 20*4;$column++) {
        Check ($crop.Bytes[$row*80+$column] -eq $bytes[(($row+5)*40+3)*4+$column]) 'Shared capture crop pixel mismatch'
    }
}
$settings = [Valtrans.Models.AppSettings]::new()
$settings.LatestOcrRegions['VALORANT@3840x2160'] = [Valtrans.Models.RelativeOcrRegion]::new()
$json = [Text.Json.JsonSerializer]::Serialize($settings, [Valtrans.Models.AppSettings], [Text.Json.JsonSerializerOptions]$null)
$copy = [Text.Json.JsonSerializer]::Deserialize[Valtrans.Models.AppSettings]($json, [Text.Json.JsonSerializerOptions]$null)
Check ($copy.DualRegionOcr -and $copy.LatestOcrRegions.Count -eq 1) 'Settings did not persist dual OCR/profile'
'PASS: dual crop bounds/roundtrip, invalid input, full+latest overlap/conflict, wrap retention, geometry, shared pixels, settings'
