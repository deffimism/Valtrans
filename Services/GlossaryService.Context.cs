using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Valtrans.Models;

namespace Valtrans.Services;

public sealed partial class GlossaryService
{
    private static readonly JsonSerializerOptions PromptJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    // Keep the actual source intact. Only relevant reference entries enter the model context.
    public string BuildRelevantPromptGlossary(string source, string target, AppSettings settings)
    {
        var entries = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var length = 0;
        bool Matches(string term) => !string.IsNullOrWhiteSpace(term) && Regex.IsMatch(source,
            $@"(?<![A-Za-z0-9_]){Regex.Escape(term)}(?![A-Za-z0-9_])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        void Add(string key, string value, string kind)
        {
            if (!Matches(key) || !seen.Add(key) || entries.Count >= 24) return;
            var entry = JsonSerializer.Serialize(new { term = key, meaning = value, kind }, PromptJson);
            if (length + entry.Length > 2200) return;
            entries.Add(entry);
            length += entry.Length;
        }
        foreach (var pair in settings.CustomGlossary.OrderByDescending(p => p.Key.Length))
            Add(pair.Key, pair.Value, "user terminology");
        // Keep the site letter bound to its area. A lone Main -> 메인 hint was
        // losing B in multi-player calls. Never rewrite the input itself.
        foreach (Match match in Regex.Matches(source,
            @"(?<![A-Za-z0-9_])(?<site>[ABC])\s*(?<area>(?i:main|heaven|hell|short|long|mid|site)|메인|헤븐|헬|숏|롱|미드|사이트|メイン|ヘブン|ヘル|ショート|ロング|ミッド|サイト)(?![A-Za-z])"))
        {
            if (Regex.IsMatch(source[(match.Index + match.Length)..],
                @"(?i)^\s+(?:reason|thing|point|goal|character|menu|story|time|break|answer|walk|distance)\b")) continue;
            var place = NormalizeCalloutLocation(match.Value, settings);
            Add(match.Value, LocalizeCalloutLocation(place, target, settings), "possible location; site and area together");
        }
        // Single letters are not general glossary keys: a keyboard key, grade or
        // nickname must not become a site. Only explicit spatial constructions
        // and common 'there are enemies B' FPS ellipsis supply that context.
        foreach (Match match in Regex.Matches(source,
            @"(?<![A-Za-z0-9_])(?<site>[ABC])(?=에|엔|에서|쪽|には|に)|(?i:\b(?:at|on|towards?|site)\s+)(?<site>[ABC])\b|(?i:\bthere\s+(?:is|are|was|were)\b[^\r\n,.!?]{0,40}\b(?:enemy|enemies|teammates?)\s+)(?<site>[ABC])(?=$|[\s,.!?])"))
            Add(match.Groups["site"].Value, match.Groups["site"].Value, "possible location; site label, not a person");
        foreach (var pair in ProperNames) Add(pair.Key, pair.Value, "proper name");
        foreach (var name in ProperNames.Values.Distinct()) Add(name, name, "proper name; keep spelling");
        foreach (var pair in SelectedLocations(settings))
        {
            var localized = LocalizeCalloutLocation(pair.Value, target, settings);
            Add(pair.Key, localized, "possible location/direction; not ordinary verb/adjective");
            Add(pair.Value, localized, "possible location/direction; not ordinary verb/adjective");
        }
        foreach (var phrase in SlangPhrases.Where(p => p.Supports(settings)))
            foreach (var alias in phrase.Aliases) Add(alias, phrase.Translate(target), "phrase reference");
        foreach (var pair in FpsTerms.Where(p => !ContextSensitiveTerms.Contains(p.Key)))
            Add(pair.Key, pair.Value, "FPS abbreviation");
        Add("cracked", "In VALORANT this is almost always praise: a player with very sharp aim. Only read it as broken armor when the sentence is clearly about a shield.", "ambiguous word");
        Add("save", "Can mean rescue/help a person, or keep equipment for another round. Preserve the object of the verb.", "ambiguous word");
        Add("low", "Little health when describing an opponent; not necessarily one-shot. Otherwise use the ordinary meaning.", "ambiguous word");
        return entries.Count == 0 ? "None. Translate the sentence without guessing extra game context."
            : string.Join('\n', entries);
    }
}
