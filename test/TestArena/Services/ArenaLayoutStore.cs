using System.IO;
using System.Text.Json;

namespace Valtrans.TestArena.Services;

public sealed class ArenaWindowLayout
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string SavedUtc { get; set; } = "";
}

public static class ArenaLayoutStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string LayoutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Valtrans", "TestArena", "window-layout.json");

    public static ArenaWindowLayout? TryLoad(string? path = null)
    {
        path ??= LayoutPath;
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<ArenaWindowLayout>(File.ReadAllText(path), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(double left, double top, double width, double height, string? path = null)
    {
        if (width < 200 || height < 120) return;
        path ??= LayoutPath;
        var layout = new ArenaWindowLayout
        {
            Left = left,
            Top = top,
            Width = width,
            Height = height,
            SavedUtc = DateTime.UtcNow.ToString("O")
        };
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(layout, JsonOptions));
    }
}
