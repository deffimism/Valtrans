using System.Text.RegularExpressions;

namespace Valtrans.Services;

public static partial class BriefingTranslationGuard
{
    public const string PromptRules = """
        For each original line, keep a one-to-one callout and use only explicitly stated facts.
        When it is clear, order facts as location -> count or target -> state or action. Never merge messages or infer enemies, danger, intent, timing, or location.
        Preserve every negation exactly: no/not/don't/ない/いない/아님/없음 must never become affirmative.
        Preserve uncertainty exactly: maybe/probably/かも/たぶん/아마/추정 must remain uncertain and must never become certain.
        """;

    public static string Apply(string source, string translated, string targetLanguage)
    {
        var sourceLines = source.Replace("\r", "").Split('\n');
        var translatedLines = translated.Replace("\r", "").Split('\n');
        if (sourceLines.Length > 1 && sourceLines.Length == translatedLines.Length)
            return string.Join(Environment.NewLine, sourceLines.Zip(translatedLines,
                (sourceLine, translatedLine) => Apply(sourceLine, translatedLine, targetLanguage)));

        translated = CompactCommonCallout(translated, targetLanguage);

        // Prefixing "not" or "maybe" cannot repair its scope within a sentence.
        // Complete rules must express those roles themselves; validation handles loss.
        return translated;
    }

    // Formatting only: never add inferred negation/uncertainty markers to model output.
    public static string CompactCommonCallout(string value, string targetLanguage)
    {
        if (value.Contains('\n'))
            return string.Join(Environment.NewLine, value.Replace("\r", "").Split('\n')
                .Select(line => CompactCommonCallout(line, targetLanguage)));
        value = value.Trim();
        if (targetLanguage == "EN")
            value = EnglishTherePattern().Replace(value, match =>
                $"{CompactCount(match.Groups[1].Value)} {match.Groups[3].Value.ToLowerInvariant()}");
        else if (targetLanguage == "KO")
        {
            value = KoreanPresencePattern().Replace(value, match => $"{match.Groups[1].Value} {match.Groups[2].Value}");
            value = KoreanInlinePresencePattern().Replace(value, match =>
                $"{match.Groups[1].Value} {match.Groups[2].Value}");
            value = KoreanDamagePattern().Replace(value, match =>
                $"{match.Groups[1].Value.Trim()} {match.Groups[2].Value} dmg");
            value = KoreanCountEndingArtifactPattern().Replace(value, "");
        }
        else if (targetLanguage == "JP")
            // Keep the counter and a word boundary: otherwise 右に2人 becomes 右2,
            // which our fact validator cannot distinguish from a location name.
            value = JapanesePresencePattern().Replace(value, match => $"{match.Groups[1].Value} {match.Groups[2].Value}人");
        return value.Trim();
    }

    private static string CompactCount(string value) => value.ToLowerInvariant() switch
    {
        "one" => "1",
        "two" => "2",
        "three" => "3",
        "four" => "4",
        "five" => "5",
        _ => value
    };

    [GeneratedRegex(@"(?ix)^\s*(?:there\s+(?:is|are)\s+)?(one|two|three|four|five|\d+)\s+(enemy|enemies|player|players)\s+(?:at|in|on)\s+(.+?)\s*[.!]?$", RegexOptions.IgnoreCase)]
    private static partial Regex EnglishTherePattern();

    [GeneratedRegex(@"^\s*(.+?)(?:에|에서)\s*(?:적(?:이|은|가)?\s*)?(\d+)\s*명(?:이|은|가)?\s*(?:있(?:습니다|어요|어|음|다))?\s*[.!]?$")]
    private static partial Regex KoreanPresencePattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])([\p{L}][\p{L}\p{N}'-]{0,20})(?:에|에서)\s*(?:적\s*)?(\d+)\s*명\s*(?:있(?:어|음|다))")]
    private static partial Regex KoreanInlinePresencePattern();

    [GeneratedRegex(@"(?i)(?<![A-Za-z0-9_/])([A-Za-z][A-Za-z0-9_/' -]{1,23}?)(?:은|는)\s*(\d+)\s*(?:데미지|피해)(?:를)?\s*(?:입(?:었어|었다|음)|맞(?:았어|았다|음)|들어(?:갔어|감)|입음)")]
    private static partial Regex KoreanDamagePattern();

    [GeneratedRegex(@"(?<=\d명)(?:어|다|음)\b")]
    private static partial Regex KoreanCountEndingArtifactPattern();

    [GeneratedRegex(@"^\s*(.+?)(?:に|で)\s*(?:敵(?:が|は)?)?\s*(\d+)\s*(?:人)?\s*(?:いる|います)?\s*[。.!]?$")]
    private static partial Regex JapanesePresencePattern();
}
