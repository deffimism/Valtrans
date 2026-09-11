using System.Text.Json;
using System.IO;
using Valtrans.Models;

namespace Valtrans.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string SettingsPath { get; }

    public SettingsService(string? settingsPath = null)
    {
        SettingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Valtrans", "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();
            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                           ?? new AppSettings();
            settings.OcrEngine = NormalizeOcrEngine(settings.OcrEngine);
            settings.PaddleOcrRuntime ??= "";
            using (var schema = JsonDocument.Parse(json))
                if (!schema.RootElement.TryGetProperty("SettingsSchemaVersion", out _))
                    settings.SettingsSchemaVersion = 0;

            using (var metadata = JsonDocument.Parse(json))
            {
                if (!metadata.RootElement.TryGetProperty("SettingsSchemaVersion", out _) &&
                    settings.OcrLanguage.Equals("EN", StringComparison.OrdinalIgnoreCase))
                    settings.OcrLanguage = "AUTO";
                if (!metadata.RootElement.TryGetProperty("OcrLanguages", out _))
                    settings.OcrLanguages = settings.OcrLanguage == "AUTO"
                        ? new List<string> { "EN", "JP", "KO" }
                        : new List<string> { settings.OcrLanguage };
                if (!metadata.RootElement.TryGetProperty("LocalAiModel", out _))
                    settings.LocalAiModel = settings.Model?.StartsWith("translategemma", StringComparison.OrdinalIgnoreCase) == true
                        ? "translategemma:4b"
                        : LocalAiService.DefaultModelName;
            }

            settings.CaptureRegionsByGame = new Dictionary<string, CaptureRegion>(
                settings.CaptureRegionsByGame ?? new Dictionary<string, CaptureRegion>(),
                StringComparer.OrdinalIgnoreCase);
            settings.CaptureProfileMetadataByGame = new Dictionary<string, CaptureProfileMetadata>(
                settings.CaptureProfileMetadataByGame ?? new Dictionary<string, CaptureProfileMetadata>(),
                StringComparer.OrdinalIgnoreCase);
            settings.OcrEnhancementProfiles = new Dictionary<string, OcrEnhancementProfile>(
                settings.OcrEnhancementProfiles ?? new Dictionary<string, OcrEnhancementProfile>(),
                StringComparer.OrdinalIgnoreCase);
            settings.CaptureRegion ??= new CaptureRegion();
            settings.LatestOcrRegions = new Dictionary<string, RelativeOcrRegion>(
                settings.LatestOcrRegions ?? new Dictionary<string, RelativeOcrRegion>(), StringComparer.OrdinalIgnoreCase);
            if (settings.CaptureRegionsByGame.Count == 0 && settings.CaptureRegion.IsValid)
                settings.CaptureRegionsByGame[settings.Game] = settings.CaptureRegion.Clone();

            settings.CaptureRegion = settings.CaptureRegionsByGame.TryGetValue(settings.Game, out var savedRegion)
                ? savedRegion.Clone()
                : new CaptureRegion();
            if (settings.SettingsSchemaVersion < 5)
                settings.OverlayBackgroundOpacity = 0.38;
            settings.OcrLanguages ??= new List<string>();
            settings.OcrLanguages = settings.OcrLanguages
                .Where(language => language is "KO" or "EN" or "JP")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (settings.OcrLanguages.Count == 0)
                settings.OcrLanguages = settings.OcrLanguage == "AUTO"
                    ? new List<string> { "EN", "JP", "KO" }
                    : new List<string> { settings.OcrLanguage is "KO" or "EN" or "JP" ? settings.OcrLanguage : "EN" };
            settings.OverlayDisplaySeconds = settings.OverlayDisplaySeconds is 0 or 5 or 10 or 15 or 30 or 60
                ? settings.OverlayDisplaySeconds
                : 15;
            settings.OverlayBackgroundOpacity = Math.Clamp(settings.OverlayBackgroundOpacity, 0, 1);
            settings.OverlayBorderOpacity = Math.Clamp(settings.OverlayBorderOpacity, 0, 1);
            settings.OverlayFontSize = settings.OverlayFontSize is >= 11 and <= 32 ? settings.OverlayFontSize : 17;
            settings.OcrStabilizationMs = settings.OcrStabilizationMs is 0 or 200 or 350 or 500 or 700
                ? settings.OcrStabilizationMs
                : 350;
            settings.OcrConsensusDelayMs = settings.OcrConsensusDelayMs is >= 100 and <= 350
                ? settings.OcrConsensusDelayMs
                : 180;
            if (settings.OcrChatFilterMode is not (GameChatFilterService.BriefingMode or
                GameChatFilterService.AllMode or GameChatFilterService.StrictMode))
                settings.OcrChatFilterMode = GameChatFilterService.BriefingMode;
            settings.LocalAiModel = LocalAiService.NormalizeModelName(settings.LocalAiModel);
            if (settings.SettingsSchemaVersion < 13)
            {
                if (string.Equals(settings.Hotkey, "Ctrl+Alt+T", StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(settings.Hotkey))
                    settings.Hotkey = "\\";
            }
            if (settings.SettingsSchemaVersion < 14 &&
                settings.TranslationProvider is "Lite" or "Ollama")
            {
                settings.TranslationProvider = "Hybrid";
                if (!settings.LocalAiModel.StartsWith("qwen3:", StringComparison.OrdinalIgnoreCase))
                    settings.LocalAiModel = LocalAiService.DefaultModelName;
            }
            if (settings.SettingsSchemaVersion < 16 &&
                settings.TranslationProvider == "Hybrid" &&
                settings.LocalAiModel.Equals("qwen3:1.7b", StringComparison.OrdinalIgnoreCase))
                settings.LocalAiModel = LocalAiService.DefaultModelName;
            // Retired cloud settings are ignored on load and omitted on the next save.
            // Preserve local model, language, glossary and overlay preferences.
            if (settings.TranslationProvider is not ("Hybrid" or "Ollama" or "Lite"))
                settings.TranslationProvider = "Hybrid";
            // Switch existing installations once; later user choices remain available.
            if (settings.SettingsSchemaVersion < 24)
                settings.OcrEngine = "Paddle";
            settings.ChatInputFullChatSeconds = settings.ChatInputFullChatSeconds is >= 1 and <= 15
                ? settings.ChatInputFullChatSeconds
                : 4;
            if (settings.SettingsSchemaVersion < 26)
            {
                settings.EnableMessageTrace = true;
                settings.SaveTraceErrorSamples = true;
                settings.MessageTraceMaxRecent = 50;
            }
            if (settings.Game.Equals("Apex Legends", StringComparison.OrdinalIgnoreCase))
                settings.Game = "VALORANT";
            settings.SettingsSchemaVersion = 26;
            settings.AutoSwitchGameProfile = true;
            settings.DualRegionOcr = true;
            settings.Model = settings.LocalAiModel;
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    // Fast and Hybrid arrived after this normalization was written. Collapsing every
    // non-Windows value to Paddle silently discarded the user's engine choice on restart.
    private static string NormalizeOcrEngine(string? value) => value?.Trim() switch
    {
        "Windows" => "Windows",
        "Fast" => "Fast",
        "Hybrid" => "Hybrid",
        _ => "Paddle"
    };

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        settings.SettingsSchemaVersion = 26;
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
