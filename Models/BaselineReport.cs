namespace Valtrans.Models;

/// <summary>Phase 2 baseline snapshot from Test Arena E2E + message traces.</summary>
public sealed class BaselineReport
{
    public string Version { get; set; } = "v0.3.0";
    public string OcrEngine { get; set; } = "";
    public string Scenario { get; set; } = "";
    public int Seed { get; set; }
    public DateTime CapturedUtc { get; set; }
    public string E2EStatus { get; set; } = "";
    public BaselineAccuracyMetrics Accuracy { get; set; } = new();
    public BaselineLatencyMetrics Latency { get; set; } = new();
    public List<BaselineCaseSnapshot> Cases { get; set; } = new();
    public List<BaselineFailureSample> FailureSamples { get; set; } = new();
}

public sealed class BaselineAccuracyMetrics
{
    public int TotalCases { get; set; }
    public int PassedCases { get; set; }
    public double CasePassRate { get; set; }
    public double? OcrSourceMatchRate { get; set; }
    public double? TranslationAcceptRate { get; set; }
}

public sealed class BaselineLatencyMetrics
{
    public double? OcrP50Ms { get; set; }
    public double? OcrP95Ms { get; set; }
    public double? TranslationP50Ms { get; set; }
    public double? TranslationP95Ms { get; set; }
    public double? E2EP50Ms { get; set; }
    public double? E2EP95Ms { get; set; }
}

public sealed class BaselineCaseSnapshot
{
    public string Source { get; set; } = "";
    public string Status { get; set; } = "";
    public string? OcrRaw { get; set; }
    public string? TranslationOutput { get; set; }
    public string? TraceId { get; set; }
    public double? TotalLatencyMs { get; set; }
    public List<string> AcceptedTranslations { get; set; } = new();
}

public sealed class BaselineFailureSample
{
    public string Source { get; set; } = "";
    public string Reason { get; set; } = "";
    public string? OcrRaw { get; set; }
    public string? TranslationOutput { get; set; }
    public string? TraceId { get; set; }
}
