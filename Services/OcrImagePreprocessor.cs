namespace Valtrans.Services;

public static class OcrImagePreprocessor
{
    // Local bright-text extraction for dark/translucent game chat. Max-channel
    // brightness retains cyan/pink channel labels better than luminance alone.
    // White background + black foreground is only an OCR candidate, not a claim
    // of higher confidence. Keep the original screenshot for comparison.
    public static byte[] Binarize(byte[] bgra, int width, int height)
    {
        if (width <= 0 || height <= 0 || (long)width * height > 4_000_000 ||
            bgra.Length != (long)width * height * 4) throw new ArgumentException("Invalid OCR pixel buffer");
        var stride = width + 1;
        var integral = new long[(width + 1) * (height + 1)];
        for (var y = 0; y < height; y++)
        {
            long sum = 0;
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                sum += Math.Max(bgra[i], Math.Max(bgra[i + 1], bgra[i + 2]));
                integral[(y + 1) * stride + x + 1] = integral[y * stride + x + 1] + sum;
            }
        }
        var output = new byte[bgra.Length];
        const int radius = 12;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var x0 = Math.Max(0, x - radius); var x1 = Math.Min(width, x + radius + 1);
            var y0 = Math.Max(0, y - radius); var y1 = Math.Min(height, y + radius + 1);
            var sum = integral[y1 * stride + x1] - integral[y0 * stride + x1]
                      - integral[y1 * stride + x0] + integral[y0 * stride + x0];
            var mean = sum / (double)((x1 - x0) * (y1 - y0));
            var i = (y * width + x) * 4;
            var brightness = Math.Max(bgra[i], Math.Max(bgra[i + 1], bgra[i + 2]));
            var value = (byte)(brightness >= 145 && brightness >= mean + 16 ? 0 : 255);
            output[i] = output[i + 1] = output[i + 2] = value;
            output[i + 3] = 255;
        }
        return output;
    }
}
