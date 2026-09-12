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
        const string count = "[1-5]|one|two|three|four|five|하나|둘|셋|넷|다섯|한|두|세|네|一|二|三|四|五";
        var pairPattern = $@"(?:(?<count>{count})\s*(?:명|人)?\s+(?<direction>{direction})|(?<direction>{direction})(?:에|に)?\s*(?<count>{count})\s*(?:명|人)?)";
        var full = Regex.Match(source, $@"^{pairPattern}\s*[,、;]?\s*{pairPattern}$", RegexOptions.IgnoreCase);
        if (!full.Success) return false;
        var pairs = new List<(string Direction, int Count)>();
        for (var index = 0; index < 2; index++)
        {
            var number = full.Groups["count"].Captures[index].Value.ToLowerInvariant() switch
            {
                "one" or "한" or "하나" or "一" => 1,
                "two" or "두" or "둘" or "二" => 2,
                "three" or "세" or "셋" or "三" => 3,
                "four" or "네" or "넷" or "四" => 4,
                "five" or "다섯" or "五" => 5,
                var numeric => int.Parse(numeric)
            };
            pairs.Add((Key(full.Groups["direction"].Captures[index].Value), number));
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
