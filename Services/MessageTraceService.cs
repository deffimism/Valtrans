using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json;
using Valtrans.Models;

namespace Valtrans.Services;

/// <summary>
/// Structured per-message trace log. Keeps OCR raw, normalized text, translation input/output,
/// and validation separate for debugging OCR vs translation issues.
/// </summary>
public sealed class MessageTraceService
{
    private const long MaxLogBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly object _sync = new();
    private readonly ConcurrentDictionary<string, string> _pendingByInput = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<MessageTraceRecord> _recent = new();
    private int _sequence;

    public MessageTraceService(string? logPath = null, string? samplesDirectory = null)
    {
        LogPath = logPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Valtrans", "Logs", "message-traces.jsonl");
        SamplesDirectory = samplesDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Valtrans", "Logs", "trace-samples");
    }

    public string LogPath { get; }
    public string SamplesDirectory { get; }

    public event EventHandler<MessageTraceRecord>? TraceCompleted;

    public bool Enabled { get; set; } = true;
    public bool SaveErrorSamples { get; set; } = true;
    public int MaxRecent { get; set; } = 50;

    public string Begin(MessageTraceBeginRequest request)
    {
        if (!Enabled) return "";
        var id = $"msg-{Interlocked.Increment(ref _sequence):D7}";
        var record = new MessageTraceRecord
        {
            Id = id,
            CaptureTimestampUtc = DateTime.UtcNow,
            Outcome = "pending",
            Capture = new MessageTraceCapture
            {
                Mode = request.CaptureMode,
                Engine = request.OcrEngine,
                Game = request.Game
            },
            Ocr = new MessageTraceOcr
            {
                Raw = request.OcrRaw,
                DetectedLanguage = request.DetectedLanguage,
                Confidence = request.OcrConfidence,
                LatencyMs = SumNullable(request.CaptureMs, request.RecognitionMs, request.ConsensusMs)
            },
            Normalization = new MessageTraceNormalization
            {
                Text = request.OcrNormalized,
                FilterReason = request.FilterReason
            },
            Classification = new MessageTraceClassification
            {
                Type = request.ClassificationType,
                Reason = request.ClassificationReason
            },
            Translation = new MessageTraceTranslation
            {
                Input = request.TranslationInput
            },
            Latency = new MessageTraceLatency
            {
                CaptureMs = request.CaptureMs,
                OcrMs = request.RecognitionMs,
                ConsensusMs = request.ConsensusMs
            }
        };
        lock (_sync)
        {
            _recent.Add(record);
            TrimRecent();
        }
        if (!string.IsNullOrWhiteSpace(request.TranslationInput))
            _pendingByInput[request.TranslationInput] = id;
        return id;
    }

    public void Complete(MessageTraceCompleteRequest request)
    {
        if (!Enabled) return;
        if (!_pendingByInput.TryRemove(request.TranslationInput, out var id))
            id = FindPendingId(request.TranslationInput);
        if (string.IsNullOrWhiteSpace(id)) return;

        lock (_sync)
        {
            var record = _recent.LastOrDefault(item => item.Id == id);
            if (record is null) return;
            record.Outcome = request.Outcome;
            record.CompletedUtc = DateTime.UtcNow;
            record.FailureStage = request.FailureStage;
            record.FailureReason = request.FailureReason;
            record.Translation.Provider = request.Provider;
            record.Translation.Model = request.Model;
            record.Translation.Route = request.Route;
            record.Translation.Output = request.TranslationOutput;
            record.Translation.UsedSafeBriefing = request.UsedSafeBriefing;
            record.Translation.LatencyMs = Math.Round(request.TranslationMs, 1);
            record.Validation.Passed = request.ValidationPassed;
            record.Validation.Adjusted = request.ValidationAdjusted;
            record.Validation.Reason = request.ValidationReason;
            record.Latency.TranslationMs = Math.Round(request.TranslationMs, 1);
            record.Latency.TotalMs = SumNullable(
                record.Latency.CaptureMs,
                record.Latency.OcrMs,
                record.Latency.ConsensusMs,
                record.Latency.TranslationMs);
            Persist(record);
            if (SaveErrorSamples && !string.Equals(request.Outcome, "completed", StringComparison.OrdinalIgnoreCase))
                SaveSample(record);
            TraceCompleted?.Invoke(this, record);
        }
    }

    public MessageTraceRecord? Get(string id)
    {
        lock (_sync)
            return _recent.LastOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<MessageTraceSummary> GetRecentSummaries(int count = 20)
    {
        lock (_sync)
            return _recent
                .OrderByDescending(item => item.CaptureTimestampUtc)
                .Take(Math.Max(1, count))
                .Select(ToSummary)
                .ToArray();
    }

    public static IReadOnlyList<MessageTraceRecord> ReadFromLog(string? logPath = null, int maxEntries = 200)
    {
        logPath ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Valtrans", "Logs", "message-traces.jsonl");
        if (!File.Exists(logPath)) return Array.Empty<MessageTraceRecord>();
        var results = new List<MessageTraceRecord>();
        foreach (var line in File.ReadLines(logPath).Reverse().Take(maxEntries))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var record = JsonSerializer.Deserialize<MessageTraceRecord>(line, JsonOptions);
                if (record is not null) results.Add(record);
            }
            catch { }
        }
        return results;
    }

    private string? FindPendingId(string translationInput)
    {
        foreach (var pair in _pendingByInput)
        {
            if (pair.Key.Equals(translationInput, StringComparison.OrdinalIgnoreCase))
                return pair.Value;
        }
        lock (_sync)
            return _recent.LastOrDefault(item => item.Outcome == "pending" &&
                item.Translation.Input.Equals(translationInput, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    private void Persist(MessageTraceRecord record)
    {
        try
        {
            lock (_sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                RotateIfNeeded();
                File.AppendAllText(LogPath, JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Trace persistence must never interrupt OCR or translation.
        }
    }

    private void SaveSample(MessageTraceRecord record)
    {
        try
        {
            Directory.CreateDirectory(SamplesDirectory);
            var path = Path.Combine(SamplesDirectory, $"{record.Id}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }),
                Encoding.UTF8);
        }
        catch { }
    }

    private void TrimRecent()
    {
        while (_recent.Count > Math.Max(10, MaxRecent))
            _recent.RemoveAt(0);
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(LogPath) || new FileInfo(LogPath).Length < MaxLogBytes) return;
        var oldPath = Path.ChangeExtension(LogPath, ".previous.jsonl");
        File.Copy(LogPath, oldPath, true);
        File.WriteAllText(LogPath, string.Empty, Encoding.UTF8);
    }

    private static MessageTraceSummary ToSummary(MessageTraceRecord record) => new(
        record.Id,
        record.CaptureTimestampUtc,
        record.Outcome,
        record.Classification.Type,
        Preview(record.Ocr.Raw, 48),
        Preview(record.Translation.Output, 48),
        record.Latency.TotalMs);

    private static string Preview(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";

    private static double? SumNullable(params double?[] values)
    {
        if (values.All(value => !value.HasValue)) return null;
        return Math.Round(values.Sum(value => value ?? 0), 1);
    }
}
