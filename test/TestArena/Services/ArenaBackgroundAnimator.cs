using System.Windows.Media;
using Valtrans.TestArena.Models;

namespace Valtrans.TestArena.Services;

public static class ArenaBackgroundAnimator
{
    public static Color BaseColor(ArenaBackgroundSettings settings)
    {
        var mode = settings.Mode?.ToLowerInvariant() ?? "static-dark";
        return mode.Contains("bright", StringComparison.Ordinal)
            ? Color.FromRgb(42, 58, 82)
            : Color.FromRgb(26, 35, 48);
    }

    public static Brush CreateBrush(ArenaBackgroundSettings settings, double phase, DeterministicRandom? random)
    {
        var mode = settings.Mode?.ToLowerInvariant() ?? "static-dark";
        if (mode is "animated-gradient" or "animated")
        {
            var shift = (Math.Sin(phase) + 1) * 0.5;
            var baseColor = BaseColor(settings);
            var accent = Color.FromRgb(
                (byte)Math.Clamp(baseColor.R + shift * 40, 0, 255),
                (byte)Math.Clamp(baseColor.G + shift * 20, 0, 255),
                (byte)Math.Clamp(baseColor.B + (1 - shift) * 35, 0, 255));
            return new LinearGradientBrush(accent, baseColor, new System.Windows.Point(0, 0), new System.Windows.Point(1, 1));
        }

        if (mode is "noise" or "procedural-noise" && random is not null)
        {
            var noise = Color.FromRgb(
                (byte)(80 + random.NextInt(0, 60)),
                (byte)(90 + random.NextInt(0, 50)),
                (byte)(100 + random.NextInt(0, 40)));
            return new SolidColorBrush(noise);
        }

        return new SolidColorBrush(BaseColor(settings));
    }

    public static double Opacity(ArenaBackgroundSettings settings, double phase) =>
        settings.MotionSpeed > 0
            ? Math.Clamp(settings.Brightness + Math.Sin(phase) * 0.08, 0.5, 1.5)
            : Math.Clamp(settings.Brightness, 0.5, 1.5);
}
