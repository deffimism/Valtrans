using System.Text.RegularExpressions;

namespace Valtrans.Services;

public sealed partial class GlossaryService
{
    // Full-sentence grammar only: never extract isolated facts from a complex sentence.
    public bool TryTranslateExactDirectionalBriefing(string source, string target, out string translated)
    {
        translated = "";
        source = source.Trim().TrimEnd('.', '!', '。');
        const string direction = @"left|right|왼쪽|오른쪽|좌측|우측|左|右";
        string Key(string value) => Regex.IsMatch(value, "^(left|왼쪽|좌측|左)$", RegexOptions.IgnoreCase) ? "left" : "right";
        string Label(string key) => (key, target) switch
        {
            ("left", "KO") => "왼쪽", ("right", "KO") => "오른쪽",
            ("left", "JP") => "左", ("right", "JP") => "右", _ => key
        };
        var correction = Regex.Match(source, $@"^not\s+(?<no>{direction})\s*,\s*(?<yes>{direction})$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (correction.Success)
        {
            var no = Key(correction.Groups["no"].Value);
            var yes = Key(correction.Groups["yes"].Value);
            if (no == yes) return false;
            translated = target switch
            {
                "KO" => $"{Label(no)} 말고 {Label(yes)}",
                "JP" => $"{Label(no)}ではなく{Label(yes)}",
                _ => $"not {no}, {yes}"
            };
            return true;
        }
        var clauses = Regex.Split(source, @"\s*[,、;]\s*");
        if (clauses.Length != 2) return false;
        var pairs = new List<(string Direction, int Count)>();
        foreach (var clause in clauses)
        {
            var match = Regex.Match(clause,
                $@"^(?:(?<count>[1-5])\s+(?<direction>{direction})|(?<direction>{direction})\s*(?<count>[1-5])\s*(?:명|人)?)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success) return false;
            pairs.Add((Key(match.Groups["direction"].Value), int.Parse(match.Groups["count"].Value)));
        }
        if (pairs[0].Direction == pairs[1].Direction) return false;
        translated = string.Join(", ", pairs.Select(p => target switch
        {
            "KO" => $"{Label(p.Direction)} {p.Count}명",
            "JP" => $"{Label(p.Direction)}{p.Count}人",
            _ => $"{p.Count} {p.Direction}"
        }));
        return true;
    }
}
