param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePath
)

# Convert the supplied artwork without cropping or changing its design.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetDirectory = Join-Path $PSScriptRoot '../Assets'
$sourceFile = (Resolve-Path -LiteralPath $SourcePath).Path
$pngPath = [IO.Path]::GetFullPath((Join-Path $assetDirectory 'Valtrans-icon.png'))
$markPath = [IO.Path]::GetFullPath((Join-Path $assetDirectory 'Valtrans-mark.png'))
$iconPath = [IO.Path]::GetFullPath((Join-Path $assetDirectory 'Valtrans.ico'))
$sourceImage = [Drawing.Image]::FromFile($sourceFile)
$frames = [Collections.Generic.List[byte[]]]::new()
$sizes = @(16, 24, 32, 48, 64, 128, 256)
try {
    if ($sourceImage.RawFormat.Guid -ne [Drawing.Imaging.ImageFormat]::Png.Guid) {
        throw 'Source artwork must be a PNG file.'
    }
    # Keep the supplied artwork intact on disk, but exclude its transparent canvas margin
    # from the ICO frames so it remains legible in Windows' 16px title-bar slot.
    $minX = $sourceImage.Width; $minY = $sourceImage.Height; $maxX = -1; $maxY = -1
    $sourceBitmap = [Drawing.Bitmap]$sourceImage
    for ($y = 0; $y -lt $sourceBitmap.Height; $y++) {
        for ($x = 0; $x -lt $sourceBitmap.Width; $x++) {
            if ($sourceBitmap.GetPixel($x, $y).A -gt 12) {
                if ($x -lt $minX) { $minX = $x }; if ($x -gt $maxX) { $maxX = $x }
                if ($y -lt $minY) { $minY = $y }; if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }
    if ($maxX -lt $minX -or $maxY -lt $minY) { throw 'Source artwork has no visible pixels.' }
    $side = [Math]::Max($maxX - $minX + 1, $maxY - $minY + 1)
    $centerX = ($minX + $maxX) / 2; $centerY = ($minY + $maxY) / 2
    $cropX = [Math]::Max(0, [Math]::Min($sourceBitmap.Width - $side, [int][Math]::Round($centerX - $side / 2)))
    $cropY = [Math]::Max(0, [Math]::Min($sourceBitmap.Height - $side, [int][Math]::Round($centerY - $side / 2)))
    $sourceRectangle = [Drawing.Rectangle]::new($cropX, $cropY, $side, $side)
    foreach ($size in $sizes) {
        $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $stream = [IO.MemoryStream]::new()
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $scale = [Math]::Min($size / $sourceRectangle.Width, $size / $sourceRectangle.Height)
            $width = [int][Math]::Round($sourceRectangle.Width * $scale)
            $height = [int][Math]::Round($sourceRectangle.Height * $scale)
            $rectangle = [Drawing.Rectangle]::new([int](($size - $width) / 2), [int](($size - $height) / 2), $width, $height)
            $graphics.DrawImage($sourceImage, $rectangle, $sourceRectangle.X, $sourceRectangle.Y,
                $sourceRectangle.Width, $sourceRectangle.Height, [Drawing.GraphicsUnit]::Pixel)
            $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
            $frames.Add($stream.ToArray())
        }
        finally {
            $stream.Dispose()
            $graphics.Dispose()
            $bitmap.Dispose()
        }
    }
}
finally {
    $sourceImage.Dispose()
}

# Windows supports PNG-compressed ICO frames, including the 256px frame.
$iconStream = [IO.MemoryStream]::new()
$writer = [IO.BinaryWriter]::new($iconStream)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$index].Length)
        $writer.Write([uint32]$offset)
        $offset += $frames[$index].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
    $writer.Flush()
    [IO.File]::WriteAllBytes($iconPath, $iconStream.ToArray())
    [IO.File]::WriteAllBytes($markPath, $frames[$sizes.IndexOf(128)])
}
finally {
    $writer.Dispose()
    $iconStream.Dispose()
}
if ($sourceFile -ne $pngPath) { [IO.File]::Copy($sourceFile, $pngPath, $true) }
Write-Output "Updated original-artwork icon frames and 128px UI mark: $($sizes -join ', ') px"
