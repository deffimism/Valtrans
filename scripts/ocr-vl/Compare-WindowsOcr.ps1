param(
    [string]$InputsPath = "$PSScriptRoot/../../artifacts/ocr-vl/results/inputs.json",
    [string]$AssemblyPath = "$PSScriptRoot/../../publish/Valtrans.dll",
    [ValidateRange(1,5)][int]$Repeats = 2
)
# Run with PowerShell 7. Uses the exact same saved crop as the VLM test.
$ErrorActionPreference = 'Stop'
$assembly = (Resolve-Path -LiteralPath $AssemblyPath).Path
$base = Split-Path $assembly
Add-Type -Path (Join-Path $base 'WinRT.Runtime.dll')
Add-Type -Path (Join-Path $base 'Microsoft.Windows.SDK.NET.dll')
Add-Type -Path $assembly
Add-Type -AssemblyName System.Drawing.Common
$references = @([AppContext]::GetData('TRUSTED_PLATFORM_ASSEMBLIES') -split [IO.Path]::PathSeparator) + @(
    (Join-Path $base 'WinRT.Runtime.dll'), (Join-Path $base 'Microsoft.Windows.SDK.NET.dll'))
Add-Type -CompilerOptions '/nowarn:1701' -ReferencedAssemblies ($references | Select-Object -Unique) -TypeDefinition @'
public static class VlmComparisonBitmap {
    public static Windows.Graphics.Imaging.SoftwareBitmap Load(string path) {
        using var bitmap = new System.Drawing.Bitmap(path);
        var area = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(area, System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try {
            var pixels = new byte[bitmap.Width * bitmap.Height * 4];
            for (int row = 0; row < bitmap.Height; row++)
                System.Runtime.InteropServices.Marshal.Copy(System.IntPtr.Add(data.Scan0, row * data.Stride),
                    pixels, row * bitmap.Width * 4, bitmap.Width * 4);
            var buffer = Windows.Security.Cryptography.CryptographicBuffer.CreateFromByteArray(pixels);
            return Windows.Graphics.Imaging.SoftwareBitmap.CreateCopyFromBuffer(buffer,
                Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, bitmap.Width, bitmap.Height,
                Windows.Graphics.Imaging.BitmapAlphaMode.Ignore);
        } finally { bitmap.UnlockBits(data); }
    }
}
'@
$ocr = [Valtrans.Services.WindowsOcrService]::new()
$method = $ocr.GetType().GetMethod('RecognizeSelectedAsync', [Reflection.BindingFlags]'Instance,NonPublic')
$cases = Get-Content -LiteralPath $InputsPath -Raw | ConvertFrom-Json
$results = @()
foreach ($case in $cases) {
    $actualHash = (Get-FileHash -LiteralPath $case.image -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $case.sha256) { throw "Input changed after preparation: $($case.id)" }
    foreach ($mode in @('EN-JP', 'EN-JP-KO')) {
        $languages = [string[]]($mode.Split('-'))
        $runs = @()
        for ($index = 0; $index -lt $Repeats; $index++) {
            $timer = [Diagnostics.Stopwatch]::StartNew()
            $bitmap = [VlmComparisonBitmap]::Load($case.image)
            try {
                $result = $method.Invoke($ocr, @($bitmap, $languages, [double]1)).GetAwaiter().GetResult()
                $timer.Stop()
                $runs += [pscustomobject]@{seconds=$timer.Elapsed.TotalSeconds; text=$result.Text; lines=$result.PositionedLines}
            } finally { $bitmap.Dispose() }
        }
        $results += [pscustomobject]@{id=$case.id; mode=$mode; sha256=$case.sha256; runs=$runs}
        Write-Host "$($case.id) / $mode / $([math]::Round($runs[-1].seconds,3))s"
    }
}
$output = Join-Path (Split-Path (Resolve-Path -LiteralPath $InputsPath).Path) 'windows.json'
# Generated diagnostic output, not a source edit.
$results | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $output -Encoding utf8
Write-Host "Saved: $output"
