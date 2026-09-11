using System.Drawing;
using Valtrans.Models;

namespace Valtrans.Services;

public static class OcrRegionRecommendationService
{
    public static CaptureRegion Recommend(string game, Rectangle monitor)
    {
        if (monitor.Width < 20 || monitor.Height < 20)
            throw new ArgumentOutOfRangeException(nameof(monitor), "Capture bounds must be at least 20×20.");

        return RecommendValorant(monitor);
    }

    // VALORANT chat measured with bottom-left origin (0%,0%) at the client bottom-left.
    // Full widget: (1.37%, 1.74%) → (24.46%, 27.17%); input row ends at 4.69% from bottom.
    // OCR uses the message stack only and excludes the input row.
    public const double ValorantLeft = 0.0137;
    public const double ValorantRight = 0.2446;
    public const double ValorantInputTopFromBottom = 0.0469;
    public const double ValorantChatTopFromBottom = 0.2717;

    /// <summary>Aspect ratio the fractions above were measured against.</summary>
    public const double ValorantReferenceAspect = 16.0 / 9.0;

    /// <summary>
    /// Width the horizontal fractions are measured against. VALORANT scales its HUD with
    /// screen height and anchors chat to the bottom-left corner, so the chat box keeps the
    /// same pixel size whenever height is unchanged. Multiplying the fractions by the real
    /// width instead stretched the box on ultrawide and shrank it on the 4:3 resolutions
    /// VALORANT players commonly use. At exactly 16:9 this returns the real width, so the
    /// recommendation is unchanged there.
    /// </summary>
    public static double HorizontalBasis(int width, int height) =>
        IsReferenceAspect(width, height) ? width : height * ValorantReferenceAspect;

    /// <summary>True when bounds are close enough to 16:9 for the measured fractions to apply directly.</summary>
    public static bool IsReferenceAspect(int width, int height) =>
        height > 0 && Math.Abs((double)width / height - ValorantReferenceAspect) <= 0.03;

    public static RelativeOcrRegion DefaultValorantLatestRegion() => new()
    {
        X = 0,
        Y = 0.76,
        Width = 1,
        Height = 0.24
    };

    private static CaptureRegion RecommendValorant(Rectangle bounds)
    {
        var basis = HorizontalBasis(bounds.Width, bounds.Height);
        var left = Math.Clamp((int)Math.Round(basis * ValorantLeft), 0, bounds.Width - 20);
        var top = Math.Clamp((int)Math.Round(bounds.Height * (1 - ValorantChatTopFromBottom)), 0, bounds.Height - 20);
        var right = Math.Clamp((int)Math.Round(basis * ValorantRight), left + 20, bounds.Width);
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
