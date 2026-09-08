using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.IO;
using Valtrans.Models;

namespace Valtrans.Services;

public sealed class DiagnosticLogService
{
    private const long MaxLogBytes = 512 * 1024;
    private static readonly HashSet<string> AllowedFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "provider", "model", "game", "result", "reason", "profile", "durationMs", "count", "version"
    };
    private readonly object _sync = new();
    public string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Valtrans", "Logs", "diagnostic.jsonl");

    public void Record(string eventName, string outcome, IReadOnlyDictionary<string, object?>? safeFields = null)
    {
        try
        {
            var fields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (safeFields is not null)
                foreach (var pair in safeFields)
                    if (AllowedFields.Contains(pair.Key)) fields[pair.Key] = SanitizeValue(pair.Value);

            var entry = new
            {
                timestampUtc = DateTime.UtcNow.ToString("O"),
                eventName = SanitizeToken(eventName),
                outcome = SanitizeToken(outcome),
                fields
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
            // Diagnostics must never interrupt translation or OCR.
        }
    }

    public void RecordException(string area, Exception exception) => Record("operation_error", "failed",
        new Dictionary<string, object?> { ["area"] = area, ["reason"] = exception.GetType().Name });

    public string BuildPrivacySafeReport(AppSettings settings, IEnumerable<string> diagnosticLines)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var builder = new StringBuilder()
            .AppendLine("VALTRANS PRIVACY-SAFE DIAGNOSTIC REPORT")
            .AppendLine($"Created (UTC): {DateTime.UtcNow:O}")
            .AppendLine($"App version: {version?.Major}.{version?.Minor}.{version?.Build}")
            .AppendLine($"OS: {RuntimeInformation.OSDescription}")
            .AppendLine($"Runtime: {RuntimeInformation.FrameworkDescription}")
            .AppendLine($"Provider: {SanitizeToken(settings.TranslationProvider)}")
            .AppendLine($"Local model: {SanitizeToken(settings.LocalAiModel)}")
            .AppendLine($"Game profile: {SanitizeToken(settings.Game)}")
            .AppendLine($"OCR languages: {string.Join('/', settings.OcrLanguages.Select(SanitizeToken))}")
            .AppendLine($"OCR region size: {settings.CaptureRegion.Width}x{settings.CaptureRegion.Height}")
            .AppendLine($"Saved OCR profiles: {settings.CaptureRegionsByGame.Count}")
            .AppendLine($"Learned OCR enhancement profiles: {settings.OcrEnhancementProfiles.Count}")
            .AppendLine($"Auto profile switch: {settings.AutoSwitchGameProfile}")
            .AppendLine()
            .AppendLine("CURRENT CHECK")
            .AppendLine(string.Join(Environment.NewLine, diagnosticLines.Select(RemovePotentialSecrets)))
            .AppendLine()
            .AppendLine("RECENT EVENTS (chat/OCR text, nicknames, API keys and server URLs are not recorded)");

        lock (_sync)
        {
            if (File.Exists(LogPath))
            {
                var recent = File.ReadLines(LogPath).TakeLast(200);
                foreach (var line in recent) builder.AppendLine(RemovePotentialSecrets(line));
            }
            else builder.AppendLine("No event log yet.");
        }
        return builder.ToString();
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(LogPath) || new FileInfo(LogPath).Length < MaxLogBytes) return;
        var oldPath = Path.ChangeExtension(LogPath, ".previous.jsonl");
        File.Copy(LogPath, oldPath, true);
        File.WriteAllText(LogPath, string.Empty, Encoding.UTF8);
    }

    private static object? SanitizeValue(object? value) => value switch
    {
        null => null,
        bool or byte or short or int or long or float or double or decimal => value,
        _ => SanitizeToken(value.ToString() ?? "")
    };

    private static string SanitizeToken(string value)
    {
        value = value.Trim();
        if (value.Length > 80) value = value[..80];
        return new string(value.Where(ch => char.IsLetterOrDigit(ch) || ch is ' ' or '_' or '-' or '.' or '@' or '×').ToArray());
    }

    private static string RemovePotentialSecrets(string value)
    {
        if (value.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("https://", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("api key", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("apikey", StringComparison.OrdinalIgnoreCase))
            return "[redacted diagnostic line]";
        return value;
    }
}
