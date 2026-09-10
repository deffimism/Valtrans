using System.IO;
using System.Text.Json;
using Valtrans.TestArena.Models;

namespace Valtrans.TestArena.Services;

public static class ScenarioLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static ArenaScenario Load(string path)
    {
        var json = File.ReadAllText(path);
        var scenario = JsonSerializer.Deserialize<ArenaScenario>(json, JsonOptions)
                       ?? throw new InvalidOperationException($"Scenario file is empty: {path}");
        if (string.IsNullOrWhiteSpace(scenario.Scenario))
            scenario.Scenario = Path.GetFileNameWithoutExtension(path);
        return scenario;
    }
}
