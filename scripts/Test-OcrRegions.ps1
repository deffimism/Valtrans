param([string]$AssemblyPath = "$PSScriptRoot/../publish/Valtrans.dll")
$ErrorActionPreference = 'Stop'
Add-Type -Path (Resolve-Path -LiteralPath $AssemblyPath)
$cases = @(
    @(1280,720,5,600,320,96),
    @(1920,1080,8,900,480,144),
    @(2560,1440,11,1200,640,192),
    @(3840,2160,16,1800,960,288)
)
foreach ($case in $cases) {
    foreach ($origin in @(@(0,0),@(-2560,80),@(120,150))) {
        $bounds = [Drawing.Rectangle]::new($origin[0],$origin[1],$case[0],$case[1])
        $region = [Valtrans.Services.OcrRegionRecommendationService]::Recommend('VALORANT',$bounds)
        if ($region.X -ne $origin[0]+$case[2] -or $region.Y -ne $origin[1]+$case[3] -or
            $region.Width -ne $case[4] -or $region.Height -ne $case[5]) { throw "Preset mismatch: $case" }
        if (-not $region.IsValid -or -not $bounds.Contains($region.ToRectangle())) { throw 'Out of bounds' }
        if ($region.Y+$region.Height -ge $bounds.Bottom) { throw 'Input-row margin missing' }
    }
}
foreach ($size in @(@(3440,1440),@(1280,1024),@(640,480),@(20,20))) {
    $bounds = [Drawing.Rectangle]::new(0,0,$size[0],$size[1])
    $region = [Valtrans.Services.OcrRegionRecommendationService]::Recommend('valorant',$bounds)
    if (-not $region.IsValid -or -not $bounds.Contains($region.ToRectangle())) { throw 'Fallback bounds invalid' }
}
$apex = [Valtrans.Services.OcrRegionRecommendationService]::Recommend('Apex Legends',[Drawing.Rectangle]::new(0,0,1920,1080))
if ($apex.X -ne 19 -or $apex.Y -ne 713 -or $apex.Width -ne 594 -or $apex.Height -ne 367) { throw 'Apex regression' }
Write-Output 'PASS: 4 presets, 3 monitor/window origins, fallback bounds, input margin, Apex unchanged. No settings or game processes touched.'
