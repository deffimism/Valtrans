using System.Text.RegularExpressions;

namespace Valtrans.Services;

/// <summary>Phase 7 mixed-language script detection for OCR routing and trace metadata.</summary>
public static class MixedLanguageDetector
{
    public static string DetectPrimaryScript(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "EN";
        var hangul = CountMatches(text, @"[\uAC00-\uD7AF]");
        var kana = CountMatches(text, @"[\u3040-\u30FF]");
        var han = CountMatches(text, @"[\u4E00-\u9FFF]");
        var latin = CountMatches(text, @"[A-Za-z]");
        var max = Math.Max(Math.Max(hangul, kana), Math.Max(han, latin));
        if (max == 0) return "EN";
        if (max == hangul) return "KO";
        if (max == kana) return "JP";
        if (max == han) return "ZH";
        return "EN";
    }

    public static bool IsMixed(string text)
    {
        var scripts = 0;
        if (CountMatches(text, @"[A-Za-z]") > 0) scripts++;
        if (CountMatches(text, @"[\uAC00-\uD7AF]") > 0) scripts++;
        if (CountMatches(text, @"[\u3040-\u30FF]") > 0) scripts++;
        if (CountMatches(text, @"[\u4E00-\u9FFF]") > 0) scripts++;
        return scripts >= 2;
    }

    public static IReadOnlyList<string> RecommendedOcrLanguages(string text) => IsMixed(text)
        ? new[] { "EN", "JP", "KO", "ZH" }
        : DetectPrimaryScript(text) switch
        {
            "KO" => new[] { "KO", "EN" },
            "JP" => new[] { "JP", "EN" },
            "ZH" => new[] { "ZH", "EN" },
            _ => new[] { "EN", "JP", "KO" }
        };

    private static int CountMatches(string text, string pattern) =>
        Regex.Matches(text, pattern).Count;
}
