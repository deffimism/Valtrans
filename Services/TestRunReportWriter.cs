using System.IO;
using System.Text.Json;

namespace Valtrans.Services;

public sealed class TestRunReport
{
    public string Scenario { get; set; } = "";
    public string Status { get; set; } = "PENDING";
    public DateTime StartedUtc { get; set; }
    public DateTime CompletedUtc { get; set; }
    public List<TestRunCaseResult> Cases { get; set; } = new();
    public List<string> Failures { get; set; } = new();
    public TestRunMetrics? Metrics { get; set; }
}

public sealed class TestRunCaseResult
{
    public string Source { get; set; } = "";
    public string? TraceId { get; set; }
    public string? OcrRaw { get; set; }
    public string? TranslationOutput { get; set; }
    public string Status { get; set; } = "PENDING";
    public string? Detail { get; set; }
    public double? TotalLatencyMs { get; set; }
}

public sealed class TestRunMetrics
{
    public int Passed { get; set; }
    public int Failed { get; set; }
    public int Pending { get; set; }
    public double? OcrP50Ms { get; set; }
    public double? TranslationP50Ms { get; set; }
    public double? TotalP50Ms { get; set; }
}

public static class TestRunReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static void Write(string path, TestRunReport report)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(report, JsonOptions));
    }
}
