using System.IO;
using System.Text;
using System.Text.Json;

namespace Valtrans.Services;

/// <summary>Local dev log for OCR + translation timing. Chat text is truncated; not uploaded.</summary>
public sealed class PipelineLogService
{
    private const long MaxLogBytes = 4 * 1024 * 1024;
    private readonly object _sync = new();

    public string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Valtrans", "Logs", "pipeline.jsonl");

    public void Record(string phase, string stage, IReadOnlyDictionary<string, object?>? metrics = null)
    {
        try
        {
            var fields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (metrics is not null)
                foreach (var pair in metrics)
                    fields[pair.Key] = SanitizeValue(pair.Key, pair.Value);

            var entry = new
            {
                timestampUtc = DateTime.UtcNow.ToString("O"),
                phase = TrimToken(phase, 32),
                stage = TrimToken(stage, 240),
                metrics = fields
            };
            lock (_sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                RotateIfNeeded();
                File.AppendAllText(LogPath, JsonSerializer.Serialize(entry) + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Pipeline logging must never interrupt OCR or translation.
        }
    }

    public void RecordOcr(string stage, double? captureMs = null, double? recognitionMs = null, double? consensusMs = null,
        string? mode = null, string? outcome = null, int? lineCount = null)
    {
        var metrics = new Dictionary<string, object?>();
        if (captureMs.HasValue) metrics["captureMs"] = Math.Round(captureMs.Value, 1);
        if (recognitionMs.HasValue) metrics["recognitionMs"] = Math.Round(recognitionMs.Value, 1);
        if (consensusMs.HasValue) metrics["consensusMs"] = Math.Round(consensusMs.Value, 1);
        if (captureMs.HasValue || recognitionMs.HasValue || consensusMs.HasValue)
            metrics["totalMs"] = Math.Round((captureMs ?? 0) + (recognitionMs ?? 0) + (consensusMs ?? 0), 1);
        if (!string.IsNullOrWhiteSpace(mode)) metrics["mode"] = mode;
        if (!string.IsNullOrWhiteSpace(outcome)) metrics["outcome"] = outcome;
        if (lineCount.HasValue) metrics["lineCount"] = lineCount;
        Record("ocr", stage, metrics);
    }

    public void RecordTranslation(string stage, double durationMs, string? outcome = null, int? textLength = null,
        string? preview = null)
    {
        var metrics = new Dictionary<string, object?>
        {
            ["translationMs"] = Math.Round(durationMs, 1)
        };
        if (!string.IsNullOrWhiteSpace(outcome)) metrics["outcome"] = outcome;
        if (textLength.HasValue) metrics["textLength"] = textLength;
        if (!string.IsNullOrWhiteSpace(preview)) metrics["preview"] = TrimToken(preview, 64);
        Record("translation", stage, metrics);
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(LogPath) || new FileInfo(LogPath).Length < MaxLogBytes) return;
        var oldPath = Path.ChangeExtension(LogPath, ".previous.jsonl");
        File.Copy(LogPath, oldPath, true);
        File.WriteAllText(LogPath, string.Empty, Encoding.UTF8);
    }

    private static object? SanitizeValue(string key, object? value) => key.Equals("preview", StringComparison.OrdinalIgnoreCase)
        ? TrimToken(value?.ToString() ?? "", 64)
        : value switch
        {
            null => null,
            bool or byte or short or int or long or float or double or decimal => value,
            _ => TrimToken(value.ToString() ?? "", 80)
        };

    private static string TrimToken(string value, int maxLength)
    {
        value = value.Trim();
        if (value.Length > maxLength) value = value[..maxLength];
        return new string(value.Where(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch) ||
                                            ch is '_' or '-' or '.' or '×' or ':' or '·' or '%' or '/').ToArray());
    }
}
