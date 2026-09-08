using System.Drawing;
using Valtrans.Models;

namespace Valtrans.Services;

public static class DualOcrRegions
{
    public static Rectangle Resolve(Rectangle full, RelativeOcrRegion? relative)
    {
        if (full.Width < 20 || full.Height < 20) return Rectangle.Empty;
        var r = relative ?? new RelativeOcrRegion();
        if (!double.IsFinite(r.X + r.Y + r.Width + r.Height) || r.Width <= 0 || r.Height <= 0)
            r = new RelativeOcrRegion();
        var x = (int)Math.Round(Math.Clamp(r.X, 0, 1) * full.Width);
        var y = (int)Math.Round(Math.Clamp(r.Y, 0, 1) * full.Height);
        x = Math.Min(x, full.Width - 20); y = Math.Min(y, full.Height - 20);
        var w = Math.Clamp((int)Math.Round(Math.Clamp(r.Width, 0, 1) * full.Width), 20, full.Width - x);
        var h = Math.Clamp((int)Math.Round(Math.Clamp(r.Height, 0, 1) * full.Height), 20, full.Height - y);
        return new Rectangle(full.X + x, full.Y + y, w, h);
    }

    public static RelativeOcrRegion FromSelection(Rectangle full, Rectangle selection)
    {
        if (selection.Width < 20 || selection.Height < 20 || !full.Contains(selection))
            throw new ArgumentException("최신 채팅 영역은 전체 OCR 영역 안에서 선택해 주세요.");
        return new RelativeOcrRegion { X = (selection.X - full.X) / (double)full.Width,
            Y = (selection.Y - full.Y) / (double)full.Height,
            Width = selection.Width / (double)full.Width, Height = selection.Height / (double)full.Height };
    }

    public static string Fingerprint(string text) => string.Join("\n", text.Replace("\r", "").Split('\n')
        .Select(ChatTextSanitizer.NormalizeOcrBody)
        .Select(line => string.Concat(line.Where(c => !char.IsWhiteSpace(c))).ToLowerInvariant()));

    public static OcrReadResult Merge(OcrReadResult full, OcrReadResult latest, int offsetX, int offsetY)
    {
        var fullLines = full.PositionedLines ?? Array.Empty<OcrPositionedLine>();
        var latestLines = (latest.PositionedLines ?? Array.Empty<OcrPositionedLine>()).Select(line => line with
        {
            X = line.X + offsetX, Y = line.Y + offsetY,
            Words = line.Words.Select(word => word with { X = word.X + offsetX, Y = word.Y + offsetY }).ToArray()
        });
        // Full-region rows take precedence on overlap (including wrapped text).
        // Do not append a contradictory cropped recognition of the same row.
        var added = latestLines.Where(line => !fullLines.Any(other =>
            Math.Min(line.Y + line.Height, other.Y + other.Height) - Math.Max(line.Y, other.Y) > 0 &&
            Math.Min(line.X + line.Width, other.X + other.Width) - Math.Max(line.X, other.X) > 0)).ToArray();
        var merged = fullLines.Concat(added).OrderBy(line => line.Y).ThenBy(line => line.X).ToArray();
        return full with { Text = string.Join(Environment.NewLine, merged.Select(line => line.Text)),
            PositionedLines = merged,
            DetectedLanguage = added.Length > 0 && full.DetectedLanguage != latest.DetectedLanguage ? "MIXED" : full.DetectedLanguage };
    }
}
