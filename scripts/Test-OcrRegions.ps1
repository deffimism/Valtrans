param([string]$AssemblyPath = "$PSScriptRoot/../publish/Valtrans.dll")
$ErrorActionPreference = 'Stop'
Add-Type -Path (Resolve-Path -LiteralPath $AssemblyPath)
$cases = @(
    @(1280,720,18,524,295,162),
    @(1920,1080,26,787,444,242),
    @(2560,1440,35,1049,591,323),
    @(3840,2160,53,1573,886,486),
    @(3837,2157,53,1571,886,485)
)
foreach ($case in $cases) {
    foreach ($origin in @(@(0,0),@(-2560,80),@(120,150))) {
        $bounds = [Drawing.Rectangle]::new($origin[0],$origin[1],$case[0],$case[1])
        $region = [Valtrans.Services.OcrRegionRecommendationService]::Recommend('VALORANT',$bounds)
        if ($region.X -ne $origin[0]+$case[2] -or $region.Y -ne $origin[1]+$case[3] -or
            $region.Width -ne $case[4] -or $region.Height -ne $case[5]) { throw "Preset mismatch: $case" }
        if (-not $region.IsValid -or -not $bounds.Contains($region.ToRectangle())) { throw 'Out of bounds' }
        if ($region.Y+$region.Height -ge $bounds.Bottom) { throw 'Input-row margin missing' }
        if ([Math]::Abs(($region.Y - $bounds.Top) / $bounds.Height - (1 - 0.2717)) -gt 0.003) { throw 'Full-panel top ratio changed' }
        if (($region.Y+$region.Height-$bounds.Top) / $bounds.Height -gt 0.953) { throw 'Input row captured' }
    }
}
foreach ($size in @(@(3440,1440),@(1280,1024),@(640,480),@(20,20))) {
    $bounds = [Drawing.Rectangle]::new(0,0,$size[0],$size[1])
    $region = [Valtrans.Services.OcrRegionRecommendationService]::Recommend('valorant',$bounds)
    if (-not $region.IsValid -or -not $bounds.Contains($region.ToRectangle())) { throw 'Fallback bounds invalid' }
}
Write-Output 'PASS: 5 presets including supplied screenshot, 3 monitor/window origins, normalized full panel, fallback bounds, input margin. No settings or game processes touched.'
