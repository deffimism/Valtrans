using System.Drawing;
using Valtrans.Models;

namespace Valtrans.Services;

public static class OcrRegionRecommendationService
{
    public static CaptureRegion Recommend(string game, Rectangle monitor)
    {
        if (monitor.Width < 20 || monitor.Height < 20)
            throw new ArgumentOutOfRangeException(nameof(monitor), "Capture bounds must be at least 20×20.");

        if (game.Equals("VALORANT", StringComparison.OrdinalIgnoreCase))
            return RecommendValorant(monitor);

        var height = monitor.Height;
        var widthFactor = game.Equals("Apex Legends", StringComparison.OrdinalIgnoreCase) ? 0.55 : 0.50;
        var heightFactor = game.Equals("Apex Legends", StringComparison.OrdinalIgnoreCase) ? 0.34 : 0.32;
        var width = Math.Min((int)Math.Round(height * widthFactor), (int)Math.Round(monitor.Width * 0.38));
        var regionHeight = (int)Math.Round(height * heightFactor);
        var leftPadding = game.Equals("Apex Legends", StringComparison.OrdinalIgnoreCase)
            ? (int)Math.Round(monitor.Width * 0.01)
            : 0;

        return new CaptureRegion
        {
            X = monitor.Left + leftPadding,
            Y = monitor.Top + monitor.Height - regionHeight,
            Width = width,
            Height = regionHeight
        };
    }

    // Starting preset based on the supplied 16:9 chat screenshots, not a detected
    // or guaranteed game layout. Scale from client pixels (never apply DPI twice).
    // At 1920×1080: x=8, y=900, width=480, height=144; leave 36px below
    // for the input row. Keep the sender prefix for the downstream chat parser.
    private static CaptureRegion RecommendValorant(Rectangle bounds)
    {
        var scale = bounds.Height / 1080.0;
        var width = Math.Clamp((int)Math.Round(480 * scale), 20, bounds.Width);
        var height = Math.Clamp((int)Math.Round(144 * scale), 20, bounds.Height);
        var left = Math.Clamp((int)Math.Round(8 * scale), 0, bounds.Width - width);
        var bottom = Math.Clamp((int)Math.Round(36 * scale), 0, bounds.Height - height);
        return new CaptureRegion
        {
            X = bounds.Left + left,
            Y = bounds.Bottom - bottom - height,
            Width = width,
            Height = height
        };
    }
}
