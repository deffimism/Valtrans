using System.IO;
using System.Text.Json;

namespace Valtrans.Services;

public sealed class TestScenarioFile
{
    public string Scenario { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    public List<TestScenarioExpectation> Expectations { get; set; } = new();

    public static TestScenarioFile Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<TestScenarioFile>(json, JsonOptions)
               ?? throw new InvalidOperationException($"Invalid scenario file: {path}");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}

public sealed class TestScenarioExpectation
{
    public string Source { get; set; } = "";
    public List<string> AcceptedTranslations { get; set; } = new();
}
