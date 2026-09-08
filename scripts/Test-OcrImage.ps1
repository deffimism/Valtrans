param(
    [Parameter(Mandatory)][string]$ImagePath,
    [string]$AssemblyPath = "$PSScriptRoot/../publish/Valtrans.dll",
    [switch]$TestDual
)
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
public static class OcrImageBuffer {
    public static Windows.Graphics.Imaging.SoftwareBitmap Create(byte[] pixels, int width, int height) {
        var buffer = Windows.Security.Cryptography.CryptographicBuffer.CreateFromByteArray(pixels);
        return Windows.Graphics.Imaging.SoftwareBitmap.CreateCopyFromBuffer(buffer,
            Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, width, height,
            Windows.Graphics.Imaging.BitmapAlphaMode.Ignore);
    }
}
'@
$bitmap = [Drawing.Bitmap]::new((Resolve-Path -LiteralPath $ImagePath).Path)
try {
    $area = [Drawing.Rectangle]::new(0,0,$bitmap.Width,$bitmap.Height)
    $data = $bitmap.LockBits($area, [Drawing.Imaging.ImageLockMode]::ReadOnly, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $bytes = [byte[]]::new($bitmap.Width * $bitmap.Height * 4)
        for ($row=0; $row -lt $bitmap.Height; $row++) {
            [Runtime.InteropServices.Marshal]::Copy([IntPtr]::Add($data.Scan0, $row*$data.Stride), $bytes, $row*$bitmap.Width*4, $bitmap.Width*4)
        }
    } finally { $bitmap.UnlockBits($data) }
    $software = [OcrImageBuffer]::Create($bytes, $bitmap.Width, $bitmap.Height)
    try {
        $ocr = [Valtrans.Services.WindowsOcrService]::new()
        $method = $ocr.GetType().GetMethod('RecognizeSelectedAsync', [Reflection.BindingFlags]'Instance,NonPublic')
        $result = $method.Invoke($ocr, @($software, [string[]]@('EN','JP'), [double]1)).GetAwaiter().GetResult()
        $glossary = [Valtrans.Services.GlossaryService]::new()
        $filter = [Valtrans.Services.GameChatFilterService]::new($glossary)
        $settings = [Valtrans.Models.AppSettings]::new()
        foreach ($line in $result.PositionedLines) {
            $body = [Valtrans.Services.ChatTextSanitizer]::NormalizeOcrBody($line.Text)
            $kept = $filter.Filter($body, 'All', $settings)
            [pscustomobject]@{OCR=$line.Text; Body=$body; Keep=$kept.Keep}
        }
        if ($TestDual) {
            $asm = [Valtrans.Services.WindowsOcrService].Assembly
            $pixelsType = $asm.GetType('Valtrans.Services.CapturedPixels')
            $frameType = $asm.GetType('Valtrans.Services.CapturedFrame')
            $method = $ocr.GetType().GetMethod('ReadDualFrameAsync', [Reflection.BindingFlags]'Instance,NonPublic')
            $latestRegion = [Valtrans.Services.DualOcrRegions]::Resolve([Drawing.Rectangle]::new(0,0,$bitmap.Width,$bitmap.Height),$null)
            $previous = $null
            foreach ($mode in @('Baseline','SameHash','SameText','ChangedLatest','PeriodicFull')) {
                $pixels = [Activator]::CreateInstance($pixelsType, [object[]]@($bytes,$bitmap.Width,$bitmap.Height,[ulong]123))
                $snapshot = [OcrImageBuffer]::Create($bytes,$bitmap.Width,$bitmap.Height)
                $frame = [Activator]::CreateInstance($frameType,[object[]]@($snapshot,$pixels,[ulong]123))
                $force = $mode -in @('Baseline','PeriodicFull')
                $hash = if ($mode -in @('SameText','ChangedLatest') -or $null -eq $previous) { $null } else { $previous.LatestHash }
                $text = if ($mode -eq 'ChangedLatest') { 'previous different briefing' } elseif ($null -eq $previous) { $null } else { $previous.LatestText }
                $dual = $method.Invoke($ocr, @($frame,$latestRegion,[string[]]@('EN','JP'),$hash,$text,$force,$false,'Raw',[Diagnostics.Stopwatch]::StartNew())).GetAwaiter().GetResult()
                $expectedFull = $force -or $mode -eq 'ChangedLatest'
                if ($dual.FullChecked -ne $expectedFull -or $dual.Result.FrameChanged -ne $expectedFull) { throw "Dual OCR route failed: $mode" }
                if ($expectedFull -and -not [Valtrans.Services.DualOcrRegions]::Fingerprint($dual.Result.Text).Contains('ミッド2')) { throw 'Full-region briefing was lost' }
                Write-Output "PASS: $mode => $($dual.Route)"
                $previous = $dual
            }
        }
    } finally { $software.Dispose() }
} finally { $bitmap.Dispose() }
