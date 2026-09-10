namespace Valtrans.Models;

/// <summary>End-to-end trace for one chat message through capture → OCR → translation → validation.</summary>
public sealed class MessageTraceRecord
{
    public string Id { get; set; } = "";
    public DateTime CaptureTimestampUtc { get; set; }
    public string Outcome { get; set; } = "pending";
    public MessageTraceCapture Capture { get; set; } = new();
    public MessageTraceOcr Ocr { get; set; } = new();
    public MessageTraceNormalization Normalization { get; set; } = new();
    public MessageTraceClassification Classification { get; set; } = new();
    public MessageTraceTranslation Translation { get; set; } = new();
    public MessageTraceValidation Validation { get; set; } = new();
    public MessageTraceLatency Latency { get; set; } = new();
    public string? FailureStage { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CompletedUtc { get; set; }
}

public sealed class MessageTraceCapture
{
    public string Mode { get; set; } = "";
    public string Engine { get; set; } = "";
    public string? Game { get; set; }
    public string? CropReference { get; set; }
}

public sealed class MessageTraceOcr
{
    public string Raw { get; set; } = "";
    public string? DetectedLanguage { get; set; }
    public double? Confidence { get; set; }
    public double? LatencyMs { get; set; }
}

public sealed class MessageTraceNormalization
{
    public string Text { get; set; } = "";
    public string? FilterReason { get; set; }
}

public sealed class MessageTraceClassification
{
    public string Type { get; set; } = "Unclassified";
    public string? Reason { get; set; }
}

public sealed class MessageTraceTranslation
{
    public string Provider { get; set; } = "";
    public string? Model { get; set; }
    public string? Route { get; set; }
    public string Input { get; set; } = "";
    public string Output { get; set; } = "";
    public bool UsedSafeBriefing { get; set; }
    public double? LatencyMs { get; set; }
}

public sealed class MessageTraceValidation
{
    public bool? Passed { get; set; }
    public bool Adjusted { get; set; }
    public string? Reason { get; set; }
}

public sealed class MessageTraceLatency
{
    public double? CaptureMs { get; set; }
    public double? OcrMs { get; set; }
    public double? ConsensusMs { get; set; }
    public double? TranslationMs { get; set; }
    public double? TotalMs { get; set; }
}

public sealed record MessageTraceSummary(
    string Id,
    DateTime CaptureTimestampUtc,
    string Outcome,
    string ClassificationType,
    string OcrRawPreview,
    string TranslationOutputPreview,
    double? TotalLatencyMs);

public sealed record MessageTraceBeginRequest(
    string OcrRaw,
    string OcrNormalized,
    string TranslationInput,
    string ClassificationType,
    string? ClassificationReason,
    string? FilterReason,
    string OcrEngine,
    string CaptureMode,
    string? DetectedLanguage,
    string? Game,
    double? CaptureMs,
    double? RecognitionMs,
    double? ConsensusMs,
    double? OcrConfidence = null);

public sealed record MessageTraceCompleteRequest(
    string TranslationInput,
    string TranslationOutput,
    string Provider,
    string? Model,
    string? Route,
    bool UsedSafeBriefing,
    double TranslationMs,
    bool? ValidationPassed,
    bool ValidationAdjusted,
    string? ValidationReason,
    string Outcome,
    string? FailureStage = null,
    string? FailureReason = null);
