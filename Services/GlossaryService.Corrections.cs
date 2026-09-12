using System.Text.RegularExpressions;
using Valtrans.Models;

namespace Valtrans.Services;

public sealed partial class GlossaryService
{
    // A complete correction has two separate roles. Never reconstruct it from an
    // unordered bag of locations, and never match a quotation or trailing clause.
    private bool TryTranslateLocationCorrection(string source, string target, AppSettings settings, out string translated)
    {
        translated = "";
        var grammars = new (string Pattern, string Action)[]
        {
            (@"^(?<no>.+?)로\s*가자\s*말고\s*(?<yes>.+?)로\s*가자[.!]?$", "go"),
            (@"^(?:don't|don’t|do not)\s+go\s+(?:to\s+)?(?<no>.+?),\s*go\s+(?:to\s+)?(?<yes>.+?)\s+instead[.!]?$", "go"),
            (@"^(?<no>.+?)に行かないで[、,]\s*(?<yes>.+?)に行こう[。.!]?$", "go"),
            (@"^(?<no>.+?)\s*말고\s*(?<yes>.+?)\s*봐\s*줘[.!]?$", "watch"),
            (@"^(?:watch|look at)\s+(?<yes>.+?),\s*not\s+(?<no>.+?)[.!]?$", "watch"),
            (@"^(?<no>.+?)じゃなく(?:て)?(?<yes>.+?)を見て[。.!]?$", "watch"),
            (@"^(?<no>.+?)\s*아니고\s*(?<yes>.+?)[.!]?$", "none"),
            (@"^not\s+(?<no>.+?),\s*(?<yes>.+?)[.!]?$", "none"),
            (@"^(?<no>.+?)じゃなく(?:て)?(?<yes>.+?)[。.!]?$", "none")
        };
        foreach (var (pattern, action) in grammars)
        {
            var match = Regex.Match(source, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success) continue;
            var no = NormalizeCalloutLocation(match.Groups["no"].Value, settings);
            var yes = NormalizeCalloutLocation(match.Groups["yes"].Value, settings);
            if (!IsLikelyLocation(no, settings) || !IsLikelyLocation(yes, settings) || no == yes) continue;
            no = LocalizeCalloutLocation(no, target, settings);
            yes = LocalizeCalloutLocation(yes, target, settings);
            translated = (target, action) switch
            {
                ("KO", "go") => $"{no} 말고 {yes}로 가자",
                ("JP", "go") => $"{no}ではなく{yes}に行こう",
                (_, "go") => $"don't go {no}, go {yes}",
                ("KO", "watch") => $"{no} 말고 {yes} 봐줘",
                ("JP", "watch") => $"{no}ではなく{yes}を見て",
                (_, "watch") => $"watch {yes}, not {no}",
                ("KO", _) => $"{no} 아니고 {yes}",
                ("JP", _) => $"{no}ではなく{yes}",
                _ => $"not {no}, {yes}"
            };
            return true;
        }
        return false;
    }
}
