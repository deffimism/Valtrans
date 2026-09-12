using Valtrans.Models;
using System.Text.RegularExpressions;

namespace Valtrans.Services;

public static class GameTranslationPrompt
{
    public static string NormalizeRegion(string? region) => region is "JP" or "KR" or "NA" or "EU" ? region : "Auto";

    public static string Build(string source, string target, AppSettings settings, GlossaryService glossary,
        bool preserveLines = false)
    {
        var language = target switch { "KO" => "Korean", "JP" => "Japanese", _ => "English" };
        var region = NormalizeRegion(settings.ServerRegion) switch
        {
            "JP" => "Japan", "KR" => "Korea", "NA" => "North America", "EU" => "Europe", _ => "unspecified"
        };
        var game = settings.Game == "Auto" ? "FPS game (game unspecified)" : settings.Game;
        var category = new GameChatFilterService(glossary).Categorize(source, settings).Category;
        var terminology = glossary.BuildTranslationTerminology(source, target, settings);
        var fidelityHints = BuildFidelityHints(source);
        var context = category == "Tactical"
            ? $"Player chat in {game}, {region} server. Style: concise FPS team chat."
            : $"Conversation between players of {game}. Style: natural casual chat, preserving the speaker's tone.";
        return $"""
            Background information:
            {context}
            Preserve actions, speakers, negation, uncertainty, conditions, directions and numbers. Do not invent facts.{(fidelityHints.Length > 0 ? "\n" + fidelityHints : "")}
            {(terminology.Length > 0 ? "Reference the following translations when relevant:\n" + terminology : "")}

            Translate the following text into {language}. Output only its translation, without explanation.
            {(preserveLines && source.Contains('\n') ? "Preserve line count and order.\n" : "")}
            {source}
            """;
    }

    internal static string BuildFidelityHints(string source)
    {
        var hints = new List<string>();
        if (Regex.IsMatch(source, @"말한\s*(?:건|거|것)|사람한테\s*한\s*거|別の人に言った|(?i)\bsaid\b.*\b(?:to|not)\b"))
            hints.Add("Keep who spoke distinct from who was addressed. Being said TO someone does not mean being said BY or ABOUT them.");
        if (!Regex.IsMatch(source, @"[?？]") &&
            Regex.IsMatch(source.Trim(), @"(?:하고\s*잘래|하고\s*잘게|먹으러\s*갈\s*거야|食べに行く)[.!。！]*$"))
            hints.Add("Preserve the personal plan as a statement, not an invitation. Keep every planned activity, including sleeping or eating after playing.");
        // English 'the other is low' needs health/actor disambiguation. Korean
        // 딸피 and Japanese ロー already receive terminology; adding the same
        // role instruction there regressed the small model's count preservation.
        if (Regex.IsMatch(source, @"(?i)\bone\s+(?:has|is)\b.*\bthe other\s+(?:one\s+)?(?:is\s+low(?:\s+hp)?(?=$|[,.;!?])|has\s+low\s+(?:hp|health)\b)") &&
            Regex.IsMatch(source, @"(?i)\b(?:op|operator|vandal|phantom)\b|오퍼|밴달|팬텀|オペ|ヴァンダル|ファントム") &&
            Regex.IsMatch(source, @"(?i)\blow\b|딸피|ロー"))
            hints.Add("The separate players have distinct weapon and health descriptions. Keep each description attached to its player, not a place or skill level.");
        if (Regex.IsMatch(source, @"(?i)줄\s*알았|と思った|\bthought\b|\bmistook\b"))
            hints.Add("Keep the earlier belief distinct from what was actually true; preserve both parts of a correction.");
        if (Regex.IsMatch(source, @"사\s*줄(?:까|게|\s*수)|買ってあげ|(?i)\bbuy\s+you\b|\bbuy\b.+\bfor\s+you\b"))
            hints.Add("Preserve who is buying for whom, and whether it is an offer, question or promise.");
        if (Regex.IsMatch(source, @"(?i)\bhit\s+[^\r\n,.!?]{1,45}\s+for\s+\d+|\b(?:dealt|take|took)\s+\d+\b|(?:한테|에게)\s*\d+\s*(?:넣|맞)|に\s*\d+\s*(?:入れ|当て|くらっ)"))
            hints.Add("Damage dealt to someone is not damage received from them; the number is damage, not score points.");
        if (Regex.IsMatch(source, @"(?i)\bif\b|들었으면|聞こえなかったなら"))
            hints.Add("Keep a hypothetical condition conditional; do not assert that it happened or invent who experienced it.");
        return string.Join('\n', hints);
    }

}
