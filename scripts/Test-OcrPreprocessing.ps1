param([string]$AssemblyPath = "$PSScriptRoot/../bin/Release/net10.0-windows10.0.26100.0/Valtrans.dll")
$ErrorActionPreference = 'Stop'
$assembly = (Resolve-Path -LiteralPath $AssemblyPath).Path
$folder = Split-Path $assembly
Add-Type -Path (Join-Path $folder 'WinRT.Runtime.dll')
Add-Type -Path (Join-Path $folder 'Microsoft.Windows.SDK.NET.dll')
Add-Type -Path $assembly
$cases = Get-Content "$PSScriptRoot/../artifacts/ocr-vl/results/inputs.json" -Raw | ConvertFrom-Json
$ocr = [Valtrans.Services.WindowsOcrService]::new()
$records = @()
foreach ($case in $cases) {
    $png = [IO.File]::ReadAllBytes($case.image)
    foreach ($mode in @('Raw','Auto','Enhanced','Binary')) {
        $result = $ocr.ReadPngAsync($png,[string[]]@('EN','JP'),$mode).GetAwaiter().GetResult()
        $bodies = [Valtrans.Services.OcrMessageParser]::Extract($result)
        $entry = [pscustomobject]@{id=$case.id; mode=$mode; milliseconds=$result.TotalDurationMs; text=$result.Text; bodies=$bodies.Body}
        $records += $entry
        $entry | Format-List
    }
}
$records | ConvertTo-Json -Depth 7 | Set-Content "$PSScriptRoot/../artifacts/ocr-vl/preprocessing.json" -Encoding utf8
