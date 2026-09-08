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

    // Full expanded chat panel measured from the user's 3837×2157 screenshot.
    // Normalized client edges: left 1.25%, top 72.5%, right 24%, bottom 95.2%.
    // Keep sender/channel prefixes, but exclude the input row and scrollbar.
    // This is a 16:9 starting preset, not detection of every possible HUD layout.
    // Round edges once in physical client pixels; never apply Windows DPI again.
    private static CaptureRegion RecommendValorant(Rectangle bounds)
    {
        var left = Math.Clamp((int)Math.Round(bounds.Width * 0.0125), 0, bounds.Width - 20);
        var top = Math.Clamp((int)Math.Round(bounds.Height * 0.725), 0, bounds.Height - 20);
        var right = Math.Clamp((int)Math.Round(bounds.Width * 0.24), left + 20, bounds.Width);
        var bottom = Math.Clamp((int)Math.Round(bounds.Height * 0.952), top + 20, bounds.Height);
        return new CaptureRegion
        {
            X = bounds.Left + left,
            Y = bounds.Top + top,
            Width = right - left,
            Height = bottom - top
        };
    }
}
