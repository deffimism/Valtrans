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
        foreach (var pair in CharacterNames) Add(pair.Key, pair.Value, "official character name");
        foreach (var name in CharacterNames.Values.Distinct()) Add(name, name, "official character name; keep spelling");
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
        Add("cracked", "When praising a player's aim: highly skilled. When describing shields: broken. Decide from the subject and game.", "ambiguous word");
        Add("save", "Can mean rescue/help a person, or keep equipment for another round. Preserve the object of the verb.", "ambiguous word");
        Add("low", "Little health when describing an opponent; not necessarily one-shot. Otherwise use the ordinary meaning.", "ambiguous word");
        return entries.Count == 0 ? "None. Translate the sentence without guessing extra game context."
            : string.Join('\n', entries);
    }
}
