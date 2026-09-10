namespace Valtrans.TestArena.Services;

public static class ArenaResolutionHelper
{
    public static (int Width, int Height) Parse(string resolution)
    {
        if (string.IsNullOrWhiteSpace(resolution)) return (1280, 720);
        var parts = resolution.Split('x', 'X');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var width) || !int.TryParse(parts[1], out var height))
            return (1280, 720);
        return (Math.Clamp(width, 800, 3840), Math.Clamp(height, 600, 2160));
    }

    public static double DpiScale(int dpi) => Math.Clamp(dpi <= 0 ? 100 : dpi, 72, 200) / 96.0;

    public static (double WindowWidth, double WindowHeight) FitToScreen(int width, int height)
    {
        var maxW = System.Windows.SystemParameters.PrimaryScreenWidth * 0.95;
        var maxH = System.Windows.SystemParameters.PrimaryScreenHeight * 0.90;
        var scale = Math.Min(1.0, Math.Min(maxW / width, maxH / height));
        return (width * scale, height * scale);
    }
}
