namespace Valtrans.Services;

public static class OcrLineSelector
{
    // Compare corresponding rows, not entire frames. Preserve geometry for nickname filtering.
    public static OcrReadResult Select(IReadOnlyDictionary<string, IReadOnlyList<OcrPositionedLine>> candidates)
    {
        var rows = new List<List<(string Language, OcrPositionedLine Line)>>();
        foreach (var candidate in candidates.SelectMany(pair => pair.Value.Select(line => (Language: pair.Key, Line: line)))
                     .Where(c => c.Line.Height > 0 && !string.IsNullOrWhiteSpace(c.Line.Text))
                     .OrderBy(c => c.Line.Y + c.Line.Height / 2d).ThenBy(c => c.Line.X))
        {
            var row = rows.LastOrDefault(group => SameRow(group[0].Line, candidate.Line));
            if (row is null) rows.Add(row = new());
            row.Add(candidate);
        }
        var chosen = rows.Select(row => row.GroupBy(c => c.Language)
            .Select(group => (Language: group.Key, Line: Join(group.Select(c => c.Line))))
            .OrderByDescending(c => Score(c.Line.Text, c.Language))
            .ThenBy(c => c.Language == "EN" ? 0 : 1).First())
            .OrderBy(c => c.Line.Y).ToArray();
        var lines = chosen.Select(c => c.Line).ToArray();
        var languages = chosen.Select(c => c.Language).Distinct().ToArray();
        var heights = lines.SelectMany(l => l.Words).Where(w => w.Height > 0).Select(w => w.Height).ToArray();
        return new OcrReadResult(string.Join(Environment.NewLine, lines.Select(l => l.Text)),
            languages.Length > 1 ? "MIXED" : languages.FirstOrDefault() ?? "EN", PositionedLines: lines,
            AverageWordHeight: heights.Length == 0 ? 0 : heights.Average());
    }

    private static bool SameRow(OcrPositionedLine a, OcrPositionedLine b)
    {
        var overlap = Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y);
        return overlap >= Math.Min(a.Height, b.Height) * 0.6 &&
               Math.Abs(a.Y + a.Height / 2d - b.Y - b.Height / 2d) <= Math.Min(a.Height, b.Height) * 0.5;
    }

    private static OcrPositionedLine Join(IEnumerable<OcrPositionedLine> source)
    {
        var lines = source.OrderBy(l => l.X).ToArray();
        if (lines.Length == 1) return lines[0];
        var x = lines.Min(l => l.X);
        var y = lines.Min(l => l.Y);
        return new OcrPositionedLine(string.Join(" ", lines.Select(l => l.Text)), x, y,
            lines.Max(l => l.X + l.Width) - x, lines.Max(l => l.Y + l.Height) - y,
            lines.SelectMany(l => l.Words).ToArray());
    }

    private static double Score(string text, string language)
    {
        // Selection heuristic, not a calibrated OCR confidence percentage.
        var body = ChatTextSanitizer.ContentForLanguageDetection(text);
        var visible = body.Count(c => !char.IsWhiteSpace(c));
        if (visible == 0) return -100;
        var letters = body.Count(char.IsLetterOrDigit);
        var kana = body.Count(c => c is >= '\u3040' and <= '\u30ff');
        var hangul = body.Count(c => c is >= '\uac00' and <= '\ud7af');
        var han = body.Count(c => c is >= '\u3400' and <= '\u9fff');
        var latin = body.Count(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z');
        var score = 30d * letters / visible;
        score -= body.Count(c => c is '�' or '□' or '¦' or '|') * 8;
        if (body.Contains("??", StringComparison.Ordinal)) score -= 20;
        score += language switch
        {
            "JP" => kana > 0 ? 35d * Math.Min(1, (kana + han) / (double)Math.Max(1, letters))
                : han >= 2 ? 16 : -4,
            "KO" => hangul > 0 ? 35d * hangul / Math.Max(1, letters) : -4,
            _ => kana + hangul + han == 0 ? 12d * latin / Math.Max(1, letters) : -15
        };
        return score;
    }
}
