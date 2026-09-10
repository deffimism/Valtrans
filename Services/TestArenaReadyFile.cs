using System.Drawing;
using System.IO;
using System.Text.Json;
using Valtrans.Models;

namespace Valtrans.Services;

public sealed class TestArenaReadyFile
{
    public string WindowTitle { get; set; } = "";
    public CaptureRegion WindowBounds { get; set; } = new();
    public CaptureRegion ChatRegion { get; set; } = new();
    public string Scenario { get; set; } = "";
    public int Seed { get; set; }
    public DateTime ReadyUtc { get; set; }

    public static TestArenaReadyFile Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<TestArenaReadyFile>(json, JsonOptions)
               ?? throw new InvalidOperationException($"Invalid arena ready file: {path}");
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    public Rectangle ChatRectangle() => ChatRegion.ToRectangle();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}
