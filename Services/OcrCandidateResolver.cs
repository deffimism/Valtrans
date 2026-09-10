using Valtrans.Models;

namespace Valtrans.Services;

public static class OcrCandidateResolver
{
    public static OcrReadResult Choose(OcrReadResult fast, OcrReadResult vl, GlossaryService glossary, AppSettings settings)
    {
        var fastNorm = Normalize(fast.Text);
        var vlNorm = Normalize(vl.Text);
        if (string.Equals(fastNorm, vlNorm, StringComparison.OrdinalIgnoreCase))
            return fast with { QualityScore = Math.Max(fast.QualityScore, vl.QualityScore) };
        if (vlNorm.Length > fastNorm.Length + 2 &&
            vlNorm.Contains(fastNorm, StringComparison.OrdinalIgnoreCase))
            return vl;
        if (fastNorm.Length > vlNorm.Length + 2 &&
            fastNorm.Contains(vlNorm, StringComparison.OrdinalIgnoreCase))
            return fast;
        var fastScore = Score(fast, glossary, settings);
        var vlScore = Score(vl, glossary, settings);
        if (vlScore > fastScore + 0.05) return vl;
        if (fastScore > vlScore + 0.05) return fast;
        return vl.QualityScore >= fast.QualityScore ? vl : fast;
    }

    public static bool IsPlausible(string text, GlossaryService glossary, AppSettings settings, double confidence)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (confidence >= 0.90) return true;
        var score = Score(new OcrReadResult(text, "MIXED", QualityScore: confidence), glossary, settings);
        return score >= 0.75;
    }

    public static bool MeetsHybridFastAccept(OcrReadResult candidate, GlossaryService glossary, AppSettings settings)
    {
        if (candidate.QualityScore < 0.90) return false;
        var words = candidate.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2 && words.All(word => word.All(ch => ch < 0x2E80)))
        {
            if (glossary.TryTranslateStructuredCallout(candidate.Text, "EN", settings, out _) ||
                glossary.ContainsKnownGameReference(candidate.Text, settings))
                return ContentScore(candidate, glossary, settings) >= 0.80;
            return false;
        }
        return ContentScore(candidate, glossary, settings) >= 0.85;
    }

    private static double ContentScore(OcrReadResult candidate, GlossaryService glossary, AppSettings settings) =>
        Score(candidate, glossary, settings);

    private static double Score(OcrReadResult candidate, GlossaryService glossary, AppSettings settings)
    {
        var text = candidate.Text.Trim();
        if (text.Length == 0) return 0;
        var score = Math.Clamp(candidate.QualityScore, 0, 1) * 0.55;
        if (glossary.TryTranslateStructuredCallout(text, "EN", settings, out _)) score += 0.25;
        if (glossary.ContainsKnownGameReference(text, settings)) score += 0.10;
        if (glossary.ContainsTacticalSlang(text, settings)) score += 0.05;
        if (ContainsInvalidNoise(text)) score -= 0.20;
        score += Math.Min(text.Length, 24) / 120.0;
        return Math.Clamp(score, 0, 1);
    }

    private static bool ContainsInvalidNoise(string text) =>
        text.Contains('厄', StringComparison.Ordinal) ||
        text.Contains('升', StringComparison.Ordinal) ||
        text.Contains('火', StringComparison.Ordinal) && text.Contains('小', StringComparison.Ordinal);

    private static string Normalize(string value) =>
        string.Concat(value.Trim().Normalize(System.Text.NormalizationForm.FormKC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
