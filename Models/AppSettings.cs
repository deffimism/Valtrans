using System.Drawing;
using System.Text.Json.Serialization;

namespace Valtrans.Models;

public sealed class AppSettings
{
    public string TranslationProvider { get; set; } = "Hybrid";
    public string Model { get; set; } = "valtrans-hymt2:1.8b";
    public string LocalAiModel { get; set; } = "valtrans-hymt2:1.8b";
    public string SendTargetLanguage { get; set; } = "EN";
    public string OverlayTargetLanguage { get; set; } = "KO";
    public List<string> OcrLanguages { get; set; } = new() { "EN", "JP", "KO" };
    public int OverlayDisplaySeconds { get; set; } = 15;
    public double OverlayFontSize { get; set; } = 17;
    public string OcrLanguage { get; set; } = "AUTO";
    public string OcrEngine { get; set; } = "Windows";
    public string PaddleOcrRuntime { get; set; } = "";
    public int SettingsSchemaVersion { get; set; } = 23;
    public bool ShowStartupGuide { get; set; } = true;
    public bool ShowAdvancedSettings { get; set; }
    public bool AutoSwitchGameProfile { get; set; } = true;
    public string Game { get; set; } = "Auto";
    public string ServerRegion { get; set; } = "Auto";
    public string Hotkey { get; set; } = "\\";
    public int OcrIntervalMs { get; set; } = 1200;
    public int OcrStabilizationMs { get; set; } = 350;
    public bool OcrAutoEnhance { get; set; } = true;
    public bool OcrTwoFrameConsensus { get; set; } = true;
    public bool DualRegionOcr { get; set; } = true;
    public Dictionary<string, RelativeOcrRegion> LatestOcrRegions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int OcrConsensusDelayMs { get; set; } = 180;
    public string OcrChatFilterMode { get; set; } = "Briefing";
    public bool OverlayClickThrough { get; set; }
    public double OverlayBackgroundOpacity { get; set; } = 0.38;
    public double OverlayBorderOpacity { get; set; } = 0.48;
    public double OverlayLeft { get; set; } = 40;
    public double OverlayTop { get; set; } = 80;
    public double OverlayWidth { get; set; } = 560;
    public double OverlayHeight { get; set; } = 210;
    public CaptureRegion CaptureRegion { get; set; } = new();
    public Dictionary<string, CaptureRegion> CaptureRegionsByGame { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, CaptureProfileMetadata> CaptureProfileMetadataByGame { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, OcrEnhancementProfile> OcrEnhancementProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> CustomGlossary { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class OcrEnhancementProfile
{
    public string PreferredMode { get; set; } = "Auto";
    public int ComparedSamples { get; set; }
    public double RawAverageQuality { get; set; }
    public double EnhancedAverageQuality { get; set; }
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class CaptureProfileMetadata
{
    public int ReferenceX { get; set; }
    public int ReferenceY { get; set; }
    public int ReferenceWidth { get; set; }
    public int ReferenceHeight { get; set; }
    public uint Dpi { get; set; } = 96;
    public string WindowMode { get; set; } = "Unknown";
    public DateTime SavedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class CaptureRegion
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public bool IsValid => Width >= 20 && Height >= 20;
    public Rectangle ToRectangle() => new(X, Y, Width, Height);

    public CaptureRegion Clone() => new()
    {
        X = X,
        Y = Y,
        Width = Width,
        Height = Height
    };

    public static CaptureRegion FromRectangle(Rectangle value) => new()
    {
        X = value.X,
        Y = value.Y,
        Width = value.Width,
        Height = value.Height
    };
}
