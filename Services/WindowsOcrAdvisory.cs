namespace Valtrans.Services;

/// <summary>
/// Product guidance for Windows OCR limits measured in Test Arena (not hidden font inflation).
/// Japanese katakana in VALORANT-sized chat is often missed; Fast/Hybrid read the same pixels.
/// </summary>
public static class WindowsOcrAdvisory
{
    public const string JapaneseChatLimitation =
        "Windows OCR은 발로란트 채팅 크기의 일본어(가타카나·한자)를 자주 놓칩니다. JP 채팅이 필요하면 Fast 또는 Hybrid OCR을 권장합니다.";

    public static bool UsesWindowsOcr(string? ocrEngine) =>
        string.Equals(ocrEngine, "Windows", StringComparison.OrdinalIgnoreCase);

    public static bool IncludesJapanese(IReadOnlyList<string> languages) =>
        languages.Any(code => code.Equals("JP", StringComparison.OrdinalIgnoreCase));

    public static bool NeedsJapaneseEngineAdvisory(string? ocrEngine, IReadOnlyList<string> languages) =>
        UsesWindowsOcr(ocrEngine) && IncludesJapanese(languages);
}
