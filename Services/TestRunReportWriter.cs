using System.IO;
using System.Text.Json;
using Valtrans.Models;

namespace Valtrans.Services;

public sealed class TestRunReport
{
    public List<string> ConsensusRecognizers { get; set; } = new();
    public string Scenario { get; set; } = "";
    public string Status { get; set; } = "PENDING";
    public DateTime StartedUtc { get; set; }
    public DateTime CompletedUtc { get; set; }
    public List<TestRunCaseResult> Cases { get; set; } = new();
    public List<string> Failures { get; set; } = new();
    public List<string> Diagnostics { get; set; } = new();
    public List<MessageTraceRecord> UnmatchedTraces { get; set; } = new();
    public TestRunMetrics? Metrics { get; set; }

    public void ObserveUnmatchedTrace(MessageTraceRecord trace)
    {
        // Evidence only. Never turn a near-match or the correct output for another
        // message into a passing case, and bound long-running failure reports.
        if (UnmatchedTraces.Count < 32) UnmatchedTraces.Add(trace);
    }
}

public sealed class TestRunCaseResult
{
    public string Source { get; set; } = "";
    public string? TraceId { get; set; }
    public string? OcrRaw { get; set; }
    public string? TranslationOutput { get; set; }
    public string? TranslationInput { get; set; }
    public bool? CompleteSourceMatched { get; set; }
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
    // Output coincidence cannot prove that OCR preserved the full input. Retain
    // internal punctuation (including negation apostrophes), allowing only OCR
    // whitespace/fullwidth differences and optional sentence-final punctuation.
    public static bool CompleteSourceMatches(string expected, string actual)
    {
        static string Normalize(string text) => string.Concat(text.Normalize(System.Text.NormalizationForm.FormKC)
            .Trim().TrimEnd('.', '!', '?', '。', '！', '？').Where(ch => !char.IsWhiteSpace(ch)));
        return Normalize(expected).Length > 0 && string.Equals(Normalize(expected), Normalize(actual),
            StringComparison.OrdinalIgnoreCase);
    }

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
