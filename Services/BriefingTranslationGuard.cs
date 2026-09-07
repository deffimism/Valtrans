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

        if (HasUncertainty(source) && !HasUncertainty(translated))
            translated = Prefix(translated, targetLanguage switch { "KO" => "추정", "JP" => "たぶん", _ => "maybe" });

        var negativeKind = DetectNegativeKind(source);
        if (negativeKind != NegativeKind.None && !HasNegation(translated))
        {
            var marker = (negativeKind, targetLanguage) switch
            {
                (NegativeKind.Absent, "KO") => "없음",
                (NegativeKind.Absent, "JP") => "なし",
                (NegativeKind.Absent, _) => "none",
                (NegativeKind.Prohibition, "KO") => "금지",
                (NegativeKind.Prohibition, "JP") => "禁止",
                (NegativeKind.Prohibition, _) => "don't",
                (_, "KO") => "아님",
                (_, "JP") => "否定",
                _ => "not"
            };
            translated = Prefix(translated, marker);
        }

        return translated;
    }

    private static string CompactCommonCallout(string value, string targetLanguage)
    {
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
            value = KoreanHiddenCountPattern().Replace(value, match =>
                $"{match.Groups[2].Value} {match.Groups[1].Value}명");
            value = KoreanCountParticlePattern().Replace(value, "$1명");
            value = KoreanCountEndingArtifactPattern().Replace(value, "");
            value = KoreanNoRotatePattern().Replace(value, "로테 금지");
            value = Regex.Replace(value, @"\b우리\s+(?=뒤|왼쪽|오른쪽|앞)", "");
        }
        else if (targetLanguage == "JP")
            value = JapanesePresencePattern().Replace(value, match => $"{match.Groups[1].Value}{match.Groups[2].Value}");
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

    private static string Prefix(string value, string marker) =>
        value.StartsWith(marker, StringComparison.OrdinalIgnoreCase) ? value : $"{marker} · {value}";

    private static bool HasUncertainty(string value) => UncertaintyPattern().IsMatch(value);
    private static bool HasNegation(string value) => NegationPattern().IsMatch(value);

    private static NegativeKind DetectNegativeKind(string value)
    {
        if (AbsentPattern().IsMatch(value)) return NegativeKind.Absent;
        if (ProhibitionPattern().IsMatch(value)) return NegativeKind.Prohibition;
        return HasNegation(value) ? NegativeKind.Generic : NegativeKind.None;
    }

    private enum NegativeKind { None, Generic, Absent, Prohibition }

    [GeneratedRegex(@"(?ix)(?:\bmaybe\b|\bprobably\b|\bperhaps\b|\bmight\b|\bcould\s+be\b|\bi\s+think\b|아마|추정|같(?:아|음)|일\s*수도|多分|たぶん|かも|と思う)")]
    private static partial Regex UncertaintyPattern();

    [GeneratedRegex(@"(?ix)(?:\bno\b|\bnot\b|\bdon['’]?t\b|\bnone\b|\bnobody\b|\bnever\b|없|아니|(?:하지\s*)?(?<![가-힣])마(?:\b|세요)|말(?:자|고|아)|금지|ない|いない|なし|じゃない|ではない|禁止|するな|しないで)")]
    private static partial Regex NegationPattern();

    [GeneratedRegex(@"(?ix)(?:\bno\s*(?:one|enemy|enemies)?\b|\bnone\b|\bnobody\b|없(?:어|음|다)?|아무도\s*없|いない|誰も.{0,8}ない|なし)")]
    private static partial Regex AbsentPattern();

    [GeneratedRegex(@"(?ix)(?:\bdon['’]?t\b|\bdo\s+not\b|하지\s*마|(?<![가-힣])마(?:세요)?\b|말(?:자|고|아)|금지|するな|しないで|禁止)")]
    private static partial Regex ProhibitionPattern();

    [GeneratedRegex(@"(?ix)^\s*(?:there\s+(?:is|are)\s+)?(one|two|three|four|five|\d+)\s+(enemy|enemies|player|players)\s+(?:at|in|on)\s+(.+?)\s*[.!]?$", RegexOptions.IgnoreCase)]
    private static partial Regex EnglishTherePattern();

    [GeneratedRegex(@"^\s*(.+?)(?:에|에서)\s*(?:적\s*)?(\d+)\s*명(?:이|은|가)?\s*(?:있(?:어|음|다))?\s*[.!]?$")]
    private static partial Regex KoreanPresencePattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])([\p{L}][\p{L}\p{N}'-]{0,20})(?:에|에서)\s*(?:적\s*)?(\d+)\s*명\s*(?:있(?:어|음|다))")]
    private static partial Regex KoreanInlinePresencePattern();

    [GeneratedRegex(@"(?i)(?<![A-Za-z0-9_/])([A-Za-z][A-Za-z0-9_/' -]{1,23}?)(?:은|는)\s*(\d+)\s*(?:데미지|피해)(?:를)?\s*(?:입(?:었어|었다|음)|맞(?:았어|았다|음)|들어(?:갔어|감)|입음)")]
    private static partial Regex KoreanDamagePattern();

    [GeneratedRegex(@"(?ix)(\d+)(?:명)?(?:이|가)\s*(?:우리\s*)?(뒤|왼쪽|오른쪽|앞)(?:에)?\s*(?:숨어\s*있(?:어|음|다)?|숨겨져\s*있(?:어|음|다)?|숨어|숨었(?:어|다)?|대기(?:\s*중)?|있(?:어|음|다)?)")]
    private static partial Regex KoreanHiddenCountPattern();

    [GeneratedRegex(@"(?<!\d)(\d+)(?:이|가)(?![가-힣])")]
    private static partial Regex KoreanCountParticlePattern();

    [GeneratedRegex(@"(?<=\d명)(?:어|다|음)\b")]
    private static partial Regex KoreanCountEndingArtifactPattern();

    [GeneratedRegex(@"(?:아직\s*)?(?:회전|로테)(?:하)?지\s*마(?:세요)?|(?:회전|로테)하지\s*말자")]
    private static partial Regex KoreanNoRotatePattern();

    [GeneratedRegex(@"^\s*(.+?)(?:に|で)\s*敵?\s*(\d+)\s*(?:人)?\s*(?:いる|います)?\s*[。.!]?$")]
    private static partial Regex JapanesePresencePattern();
}
