using System.Text.RegularExpressions;
using Valtrans.Models;

namespace Valtrans.Services;

// A source-side plan for a single, fully recognized callout. This does not repair
// an arbitrary translation by adding a guessed marker after validation fails.
public sealed record LiteUncertaintyPlan(string Core)
{
    public static LiteUncertaintyPlan? Create(string source, AppSettings settings, GlossaryService glossary)
    {
        var value = source.Trim().TrimEnd('。', '.');
        var match = Regex.Match(value, @"^(?<core>[^。！？!?;,、，\r\n]+?)と思(?:います|う)$",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success) return null;
        var core = match.Groups["core"].Value.Trim();
        // Whitelist a complete known callout; conditions, quoted speech, multiple
        // clauses, and someone else's beliefs must stay with the model/guard.
        if (!glossary.TryTranslateStructuredCallout(core, "EN", settings, out _)) return null;
        return new LiteUncertaintyPlan(core);
    }

    public string Compose(string translation, string target) =>
        (target switch { "KO" => "추정", "JP" => "たぶん", _ => "I think" }) + " · " + translation;
}
