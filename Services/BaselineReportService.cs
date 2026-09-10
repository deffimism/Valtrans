using System.IO;
using System.Text.Json;
using Valtrans.Models;

namespace Valtrans.Services;

public static class BaselineReportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static BaselineReport Build(
        TestRunReport run,
        TestScenarioFile scenario,
        string ocrEngine,
        int seed,
        string version = "v0.3.0",
        IReadOnlyList<MessageTraceRecord>? traces = null)
    {
        var report = new BaselineReport
        {
            Version = version,
            OcrEngine = ocrEngine,
            Scenario = run.Scenario,
            Seed = seed,
            CapturedUtc = DateTime.UtcNow,
            E2EStatus = run.Status
        };

        var expectations = scenario.Expectations.ToDictionary(item => item.Source, StringComparer.Ordinal);
        foreach (var caseResult in run.Cases)
        {
            expectations.TryGetValue(caseResult.Source, out var expectation);
            report.Cases.Add(new BaselineCaseSnapshot
            {
                Source = caseResult.Source,
                Status = caseResult.Status,
                OcrRaw = caseResult.OcrRaw,
                TranslationOutput = caseResult.TranslationOutput,
                TraceId = caseResult.TraceId,
                TotalLatencyMs = caseResult.TotalLatencyMs,
                AcceptedTranslations = expectation?.AcceptedTranslations.ToList() ?? new List<string>()
            });
            if (caseResult.Status != "PASS")
            {
                report.FailureSamples.Add(new BaselineFailureSample
                {
                    Source = caseResult.Source,
                    Reason = caseResult.Detail ?? "failed",
                    OcrRaw = caseResult.OcrRaw,
                    TranslationOutput = caseResult.TranslationOutput,
                    TraceId = caseResult.TraceId
                });
            }
        }

        report.Accuracy.TotalCases = run.Cases.Count;
        report.Accuracy.PassedCases = run.Cases.Count(item => item.Status == "PASS");
        report.Accuracy.CasePassRate = report.Accuracy.TotalCases == 0
            ? 0
            : Math.Round(report.Accuracy.PassedCases / (double)report.Accuracy.TotalCases, 4);

        var ocrMatches = run.Cases.Count(item =>
            item.Status == "PASS" && SourceMatches(item.Source, item.OcrRaw));
        var translationMatches = run.Cases.Count(item => item.Status == "PASS");
        if (report.Accuracy.TotalCases > 0)
        {
            report.Accuracy.OcrSourceMatchRate = Math.Round(ocrMatches / (double)report.Accuracy.TotalCases, 4);
            report.Accuracy.TranslationAcceptRate = Math.Round(translationMatches / (double)report.Accuracy.TotalCases, 4);
        }

        var ocrLatencies = run.Cases.Where(item => item.TraceId is not null)
            .Select(item => traces?.FirstOrDefault(trace => trace.Id == item.TraceId))
            .Where(trace => trace?.Ocr.LatencyMs is not null)
            .Select(trace => trace!.Ocr.LatencyMs!.Value)
            .ToArray();
        var translationLatencies = run.Cases.Where(item => item.TraceId is not null)
            .Select(item => traces?.FirstOrDefault(trace => trace.Id == item.TraceId))
            .Where(trace => trace?.Translation.LatencyMs is not null)
            .Select(trace => trace!.Translation.LatencyMs!.Value)
            .ToArray();
        var e2eLatencies = run.Cases.Where(item => item.TotalLatencyMs.HasValue)
            .Select(item => item.TotalLatencyMs!.Value)
            .ToArray();

        report.Latency.OcrP50Ms = Percentile(ocrLatencies, 0.5);
        report.Latency.OcrP95Ms = Percentile(ocrLatencies, 0.95);
        report.Latency.TranslationP50Ms = Percentile(translationLatencies, 0.5);
        report.Latency.TranslationP95Ms = Percentile(translationLatencies, 0.95);
        report.Latency.E2EP50Ms = run.Metrics?.TotalP50Ms ?? Percentile(e2eLatencies, 0.5);
        report.Latency.E2EP95Ms = Percentile(e2eLatencies, 0.95);
        return report;
    }

    public static void Write(string path, BaselineReport report)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(report, JsonOptions));
    }

    public static BaselineReport Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<BaselineReport>(json, JsonOptions)
               ?? throw new InvalidOperationException($"Invalid baseline report: {path}");
    }

    public static BaselineComparisonResult Compare(BaselineReport baseline, BaselineReport current,
        double latencyWarningPercent = 10, double latencyFailPercent = 20)
    {
        var result = new BaselineComparisonResult
        {
            BaselinePath = baseline.Scenario,
            CurrentScenario = current.Scenario,
            BaselineCapturedUtc = baseline.CapturedUtc,
            CurrentCapturedUtc = current.CapturedUtc
        };

        if (current.Accuracy.CasePassRate < baseline.Accuracy.CasePassRate)
        {
            result.Status = "FAIL";
            result.Issues.Add($"case pass rate regressed {baseline.Accuracy.CasePassRate:P1} -> {current.Accuracy.CasePassRate:P1}");
        }

        CompareLatency(result, "OCR P50", baseline.Latency.OcrP50Ms, current.Latency.OcrP50Ms, latencyWarningPercent, latencyFailPercent);
        CompareLatency(result, "Translation P50", baseline.Latency.TranslationP50Ms, current.Latency.TranslationP50Ms, latencyWarningPercent, latencyFailPercent);
        CompareLatency(result, "E2E P50", baseline.Latency.E2EP50Ms, current.Latency.E2EP50Ms, latencyWarningPercent, latencyFailPercent);

        foreach (var baselineCase in baseline.Cases.Where(item => item.Status == "PASS"))
        {
            var currentCase = current.Cases.FirstOrDefault(item => item.Source == baselineCase.Source);
            if (currentCase is null)
            {
                result.Status = "FAIL";
                result.Issues.Add($"missing case '{baselineCase.Source}'");
                continue;
            }
            if (currentCase.Status != "PASS")
            {
                result.Status = "FAIL";
                result.Issues.Add($"case '{baselineCase.Source}' regressed to {currentCase.Status}");
            }
        }

        if (result.Status == "PASS" && result.Warnings.Count > 0)
            result.Status = "WARN";
        return result;
    }

    private static void CompareLatency(BaselineComparisonResult result, string label, double? baselineMs,
        double? currentMs, double warningPercent, double failPercent)
    {
        if (!baselineMs.HasValue || !currentMs.HasValue || baselineMs.Value <= 0) return;
        var deltaPercent = (currentMs.Value - baselineMs.Value) / baselineMs.Value * 100;
        if (deltaPercent > failPercent)
        {
            result.Status = "FAIL";
            result.Issues.Add($"{label} latency +{deltaPercent:F1}% ({baselineMs:F1}ms -> {currentMs:F1}ms)");
        }
        else if (deltaPercent > warningPercent)
            result.Warnings.Add($"{label} latency +{deltaPercent:F1}% ({baselineMs:F1}ms -> {currentMs:F1}ms)");
    }

    private static bool SourceMatches(string expected, string? actual)
    {
        if (string.IsNullOrWhiteSpace(actual)) return false;
        var left = Normalize(expected);
        var right = Normalize(actual);
        return left.Equals(right, StringComparison.OrdinalIgnoreCase) ||
               right.Contains(left, StringComparison.OrdinalIgnoreCase) ||
               left.Contains(right, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string value) =>
        string.Concat(value.Trim().Normalize(System.Text.NormalizationForm.FormKC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static double? Percentile(double[] values, double percentile)
    {
        if (values.Length == 0) return null;
        Array.Sort(values);
        var index = (int)Math.Round((values.Length - 1) * percentile);
        return Math.Round(values[Math.Clamp(index, 0, values.Length - 1)], 1);
    }
}

public sealed class BaselineComparisonResult
{
    public string Status { get; set; } = "PASS";
    public string BaselinePath { get; set; } = "";
    public string CurrentScenario { get; set; } = "";
    public DateTime BaselineCapturedUtc { get; set; }
    public DateTime CurrentCapturedUtc { get; set; }
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
