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

    // VALORANT chat measured with bottom-left origin (0%,0%) at the client bottom-left.
    // Full widget: (1.37%, 1.74%) → (24.46%, 27.17%); input row ends at 4.69% from bottom.
    // OCR uses the message stack only and excludes the input row.
    public const double ValorantLeft = 0.0137;
    public const double ValorantRight = 0.2446;
    public const double ValorantInputTopFromBottom = 0.0469;
    public const double ValorantChatTopFromBottom = 0.2717;

    public static RelativeOcrRegion DefaultValorantLatestRegion() => new()
    {
        X = 0,
        Y = 0.76,
        Width = 1,
        Height = 0.24
    };

    private static CaptureRegion RecommendValorant(Rectangle bounds)
    {
        var left = Math.Clamp((int)Math.Round(bounds.Width * ValorantLeft), 0, bounds.Width - 20);
        var top = Math.Clamp((int)Math.Round(bounds.Height * (1 - ValorantChatTopFromBottom)), 0, bounds.Height - 20);
        var right = Math.Clamp((int)Math.Round(bounds.Width * ValorantRight), left + 20, bounds.Width);
        var bottom = Math.Clamp((int)Math.Round(bounds.Height * (1 - ValorantInputTopFromBottom)), top + 20, bounds.Height);
        return new CaptureRegion
        {
            X = bounds.Left + left,
            Y = bounds.Top + top,
            Width = right - left,
            Height = bottom - top
        };
    }
}
