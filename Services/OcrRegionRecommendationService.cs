using System.Drawing;
using Valtrans.Models;

namespace Valtrans.Services;

public static class OcrRegionRecommendationService
{
    public static CaptureRegion Recommend(string game, Rectangle monitor)
    {
        var height = monitor.Height;
        var widthFactor = game.Equals("Apex Legends", StringComparison.OrdinalIgnoreCase) ? 0.55 :
            game.Equals("VALORANT", StringComparison.OrdinalIgnoreCase) ? 0.45 : 0.50;
        var heightFactor = game.Equals("Apex Legends", StringComparison.OrdinalIgnoreCase) ? 0.34 :
            game.Equals("VALORANT", StringComparison.OrdinalIgnoreCase) ? 0.29 : 0.32;
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
}
