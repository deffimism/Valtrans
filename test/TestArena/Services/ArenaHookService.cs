using System.IO;
using System.Text.Json;
using Valtrans.TestArena.Models;

namespace Valtrans.TestArena.Services;

/// <summary>Phase 8 hooks: inject-file polling + hook manifest for agent automation.</summary>
public sealed class ArenaHookService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static ArenaHookManifest CreateManifest(ArenaScenario scenario, string? injectFilePath) => new()
    {
        Version = 1,
        Scenario = scenario.Scenario,
        Tags = scenario.Tags,
        Commands = new List<string>
        {
            "loadScenario", "start", "pause", "resume", "reset",
            "setResolution", "setBackground", "injectMessage", "captureState", "stop"
        },
        InjectFile = injectFilePath,
        Background = scenario.Background,
        Fuzz = scenario.Fuzz
    };

    public static void WriteManifest(string path, ArenaHookManifest manifest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static bool TryReadInject(string? injectFilePath, out ArenaInjectCommand command)
    {
        command = new ArenaInjectCommand();
        if (string.IsNullOrWhiteSpace(injectFilePath) || !File.Exists(injectFilePath)) return false;
        try
        {
            var json = File.ReadAllText(injectFilePath);
            var parsed = JsonSerializer.Deserialize<ArenaInjectCommand>(json, JsonOptions);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.Text)) return false;
            command = parsed;
            File.Delete(injectFilePath);
            return true;
        }
        catch { return false; }
    }
}

public sealed class ArenaHookManifest
{
    public int Version { get; set; }
    public string Scenario { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    public List<string> Commands { get; set; } = new();
    public string? InjectFile { get; set; }
    public ArenaBackgroundSettings Background { get; set; } = new();
    public ArenaFuzzSettings Fuzz { get; set; } = new();
}

public sealed class ArenaInjectCommand
{
    public string Command { get; set; } = "injectMessage";
    public string Speaker { get; set; } = "PlayerA";
    public string Text { get; set; } = "";
    public string Channel { get; set; } = "TEAM";
}
