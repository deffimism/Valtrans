using Valtrans.Models;

namespace Valtrans.Services;

public static class OcrCandidateResolver
{
    /// <summary>Engine confidence a Fast read needs before its content is even scored.</summary>
    public const double FastConfidenceFloor = 0.90;

    /// <summary>Content score a multi-word Fast read needs to skip the VL fallback.</summary>
    public const double ContentFloor = 0.85;

    /// <summary>
    /// Relaxed floor for a lone token the glossary recognizes as a callout. Fast OCR often
    /// returns a single run-together token ("2BHeaven") that is complete despite the length.
    /// </summary>
    public const double GlossaryBackedContentFloor = 0.80;

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

    public static bool MeetsHybridFastAccept(OcrReadResult candidate, GlossaryService glossary, AppSettings settings)
    {
        if (candidate.QualityScore < FastConfidenceFloor) return false;
        var words = candidate.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2 && words.All(word => word.All(ch => ch < 0x2E80)))
        {
            if (glossary.TryTranslateStructuredCallout(candidate.Text, "EN", settings, out _) ||
                glossary.ContainsKnownGameReference(candidate.Text, settings))
                return Score(candidate, glossary, settings) >= GlossaryBackedContentFloor;
            return false;
        }
        return Score(candidate, glossary, settings) >= ContentFloor;
    }

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
        OcrNoiseHeuristics.HasScatteredHanNoise(text);

    private static string Normalize(string value) =>
        string.Concat(value.Trim().Normalize(System.Text.NormalizationForm.FormKC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
