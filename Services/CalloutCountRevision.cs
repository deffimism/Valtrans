using System.Text.RegularExpressions;

namespace Valtrans.Services;

// Shared by translation and validation so omitted nouns in a complete correction
// cannot be interpreted differently on the two sides of the pipeline.
internal static class CalloutCountRevision
{
    private const string Number = @"[1-5]|one|two|three|four|five|한|하나|두|둘|세|셋|네|넷|다섯|一|二|三|四|五";
    private static readonly Regex Pattern = new(
        $@"^(?:(?<before>{Number})\s*(?:명)?인\s*줄\s*알았는데\s*(?<after>{Number})\s*(?:명)?이었어|" +
        $@"I thought (?:(?:it|there) (?:was|were) )?(?<before>{Number})(?: person| people)?,?\s*but there (?:was|were) (?<after>{Number})(?: person| people)?|" +
        $@"(?<before>{Number})人だと思ったけど[、,]?(?<after>{Number})人(?:だった|いた))$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    internal static bool TryParse(string text, out int estimated, out int actual)
    {
        estimated = actual = 0;
        var match = Pattern.Match(text.Trim().TrimEnd('.', '!', '。', '！').Trim());
        if (!match.Success) return false;
        estimated = NumberValue(match.Groups["before"].Value);
        actual = NumberValue(match.Groups["after"].Value);
        return true;
    }

    private static int NumberValue(string value) => value.ToLowerInvariant() switch
    {
        "one" or "한" or "하나" or "一" => 1, "two" or "두" or "둘" or "二" => 2,
        "three" or "세" or "셋" or "三" => 3, "four" or "네" or "넷" or "四" => 4,
        "five" or "다섯" or "五" => 5, _ => int.Parse(value)
    };
}
