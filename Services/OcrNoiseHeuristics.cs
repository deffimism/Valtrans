namespace Valtrans.Services;

/// <summary>
/// Shared glyph-level plausibility checks. A real CJK word is a contiguous run
/// ("残血", "左側"). When a Latin-dominant line only carries single han characters
/// separated by Latin letters or spaces, those characters are glyph misreads of
/// Latin letters ("watch left" read as "watch 厄 升") rather than real CJK content.
/// </summary>
public static class OcrNoiseHeuristics
{
    public static bool HasScatteredHanNoise(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var latin = 0;
        var han = 0;
        var longestRun = 0;
        var run = 0;
        foreach (var ch in text)
        {
            if (IsHan(ch))
            {
                han++;
                run++;
                longestRun = Math.Max(longestRun, run);
                continue;
            }
            run = 0;
            if (IsKana(ch) || IsHangul(ch)) return false;
            if (ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z') latin++;
        }
        return han > 0 && latin >= 3 && longestRun <= 1;
    }

    public static bool IsHan(char ch) => ch is >= '\u3400' and <= '\u9fff';
    public static bool IsKana(char ch) => ch is >= '\u3040' and <= '\u30ff';
    public static bool IsHangul(char ch) => ch is >= '\uac00' and <= '\ud7af';

    /// <summary>
    /// Fast OCR regularly drops the spaces in a short callout, so "2 B Heaven" comes back
    /// as "2BHeaven" and no location matcher recognizes it. Re-insert spaces at digit and
    /// capital boundaries. Returns the original text when it is not one run-together blob,
    /// so ordinary sentences and nicknames are never touched.
    /// </summary>
    public static string SplitRunTogetherCallout(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length is < 3 or > 24) return text;
        if (trimmed.Any(ch => ch > 127 || (!char.IsLetterOrDigit(ch)))) return text;
        var hasBoundary = false;
        for (var index = 1; index < trimmed.Length; index++)
        {
            var previous = trimmed[index - 1];
            var current = trimmed[index];
            if (char.IsDigit(previous) != char.IsDigit(current) ||
                (char.IsLower(previous) && char.IsUpper(current)))
            {
                hasBoundary = true;
                break;
            }
        }
        if (!hasBoundary) return text;

        var builder = new System.Text.StringBuilder(trimmed.Length + 4);
        builder.Append(trimmed[0]);
        for (var index = 1; index < trimmed.Length; index++)
        {
            var previous = trimmed[index - 1];
            var current = trimmed[index];
            if (char.IsDigit(previous) != char.IsDigit(current) ||
                (!char.IsDigit(current) && char.IsUpper(current)))
                builder.Append(' ');
            builder.Append(current);
        }
        return builder.ToString();
    }
}
