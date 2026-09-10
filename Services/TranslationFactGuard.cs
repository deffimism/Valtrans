using System.Text.RegularExpressions;
using Valtrans.Models;

namespace Valtrans.Services;

public static partial class TranslationFactGuard
{
    public static bool TryBuildSafeBriefing(string source, string targetLanguage, AppSettings settings,
        GlossaryService glossary, out string briefing)
    {
        briefing = "";
        if (!ChatTextSanitizer.HasMeaningfulContent(source)) return false;

        if (glossary.TryTranslateStructuredCallout(source, targetLanguage, settings, out var structured))
        {
            briefing = Apply(source, BriefingTranslationGuard.Apply(source, structured, targetLanguage),
                targetLanguage, settings, glossary).Text;
            return ChatTextSanitizer.HasMeaningfulContent(briefing);
        }

        // A bag of directions/numbers loses their relationships and negation scope.
        // Only a complete, recognized callout is eligible for a deadline replacement.
        return false;
    }

    public static TranslationFactGuardResult Apply(string source, string translated, string targetLanguage,
        AppSettings settings, GlossaryService glossary)
    {
        var sourceLines = SplitLines(source);
        var translatedLines = SplitLines(translated);
        if (sourceLines.Length > 1 && sourceLines.Length == translatedLines.Length)
        {
            var adjusted = false;
            var reasons = new List<string>();
            var output = new string[sourceLines.Length];
            for (var index = 0; index < sourceLines.Length; index++)
            {
                var line = Apply(sourceLines[index], translatedLines[index], targetLanguage, settings, glossary);
                output[index] = line.Text;
                adjusted |= line.Adjusted;
                if (line.Adjusted) reasons.Add(line.Reason);
            }
            return new TranslationFactGuardResult(string.Join(Environment.NewLine, output), adjusted,
                string.Join(", ", reasons.Distinct()));
        }

        if (glossary.TryTranslateSiteActionBriefing(source, targetLanguage, out var siteAction))
        {
            siteAction = BriefingTranslationGuard.Apply(source, siteAction, targetLanguage);
            return new TranslationFactGuardResult(siteAction,
                !siteAction.Equals(translated, StringComparison.OrdinalIgnoreCase), "사이트 행동 보존");
        }

        if (glossary.TryTranslateExactDirectionalBriefing(source, targetLanguage, out var directional))
            return new TranslationFactGuardResult(directional, !directional.Equals(translated, StringComparison.OrdinalIgnoreCase),
                "방향·인원 연결 또는 부정 대상 보존");

        var facts = ExtractFacts(source);
        var resultFacts = ExtractFacts(translated);
        var missingDirections = facts.Directions.Except(resultFacts.Directions, StringComparer.OrdinalIgnoreCase).ToArray();
        var missingCounts = facts.Counts.Except(resultFacts.Counts).ToArray();
        var missingNumbers = facts.OtherNumbers.Except(resultFacts.AllNumbers).ToArray();
        if (missingDirections.Length == 0 && missingCounts.Length == 0 && missingNumbers.Length == 0)
            return new TranslationFactGuardResult(translated, false, "");

        if (glossary.TryTranslateStructuredCallout(source, targetLanguage, settings, out var structured))
        {
            structured = BriefingTranslationGuard.Apply(source, structured, targetLanguage);
            return new TranslationFactGuardResult(structured, true, BuildReason(missingDirections, missingCounts, missingNumbers));
        }

        // Unknown clause structure cannot be repaired by prefixing disconnected facts.
        throw new InvalidOperationException("번역 의미 확인 필요 · " +
            BuildReason(missingDirections, missingCounts, missingNumbers) + " · 불완전한 번역문은 표시하지 않았습니다.");
    }

    private static CriticalFacts ExtractFacts(string text)
    {
        text = ChatTextSanitizer.ContentForLanguageDetection(text);
        // 'left' can be leave's past tense or indicate remaining time/items, not a direction.
        text = Regex.Replace(text,
            @"(?ix)\b(?:i|we|he|she|they|you)\s+(?:(?:have|had|just|already)\s+)*left\b|\b(?:time|seconds?|minutes?|rounds?|ammo)\s+left\b",
            " ");
        var directions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in DirectionPattern().Matches(text))
            directions.Add(CanonicalDirection(match.Value));

        var counts = new HashSet<int>();
        foreach (Match match in ExplicitCountPattern().Matches(text))
        {
            var value = NormalizeCount(match.Groups["count"].Value);
            if (value is >= 1 and <= 5) counts.Add(value);
        }
        // Japanese normally has no spaces: 左に二人 must still preserve the count.
        foreach (Match match in JapaneseCountPattern().Matches(text))
            counts.Add(NormalizeCount(match.Groups["count"].Value));
        if (CalloutContextPattern().IsMatch(text) && !OneShotPattern().IsMatch(text))
            foreach (Match match in BareCountPattern().Matches(text))
            {
                var value = NormalizeCount(match.Groups["count"].Value);
                if (value is >= 1 and <= 5) counts.Add(value);
            }

        var allNumbers = NumberPattern().Matches(text)
            .Select(match => int.TryParse(match.Value, out var value) ? value : -1)
            .Where(value => value >= 0)
            .ToHashSet();
        var otherNumbers = allNumbers.Where(value => !counts.Contains(value) && value >= 10).ToHashSet();
        return new CriticalFacts(directions, counts, otherNumbers, allNumbers);
    }

    private static string CanonicalDirection(string value) => value.ToLowerInvariant() switch
    {
        "left" or "왼쪽" or "좌측" or "左" => "left",
        "right" or "오른쪽" or "우측" or "右" => "right",
        "front" or "앞" or "전방" or "前" => "front",
        _ => "back"
    };

    private static string LocalizeDirection(string value, string targetLanguage) => (value, targetLanguage) switch
    {
        ("left", "KO") => "왼쪽", ("right", "KO") => "오른쪽", ("front", "KO") => "앞", ("back", "KO") => "뒤",
        ("left", "JP") => "左", ("right", "JP") => "右", ("front", "JP") => "前", ("back", "JP") => "後ろ",
        _ => value
    };

    private static int NormalizeCount(string value) => value.ToLowerInvariant() switch
    {
        "one" or "한" or "하나" or "一" => 1,
        "two" or "두" or "둘" or "二" => 2,
        "three" or "세" or "셋" or "三" => 3,
        "four" or "네" or "넷" or "四" => 4,
        "five" or "다섯" or "五" => 5,
        _ => int.TryParse(value, out var number) ? number : -1
    };

    private static string BuildReason(IReadOnlyCollection<string> directions, IReadOnlyCollection<int> counts,
        IReadOnlyCollection<int> numbers)
    {
        var values = new List<string>();
        if (directions.Count > 0) values.Add("방향");
        if (counts.Count > 0) values.Add("인원");
        if (numbers.Count > 0) values.Add("수치");
        return string.Join("·", values) + " 보존";
    }

    private static string[] SplitLines(string text) => text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private sealed record CriticalFacts(HashSet<string> Directions, HashSet<int> Counts,
        HashSet<int> OtherNumbers, HashSet<int> AllNumbers);

    [GeneratedRegex(@"(?ix)(?:(?<![A-Za-z0-9_])(?:left|right|front|back)(?![A-Za-z0-9_])|(?:왼쪽|오른쪽|좌측|우측|전방|앞|뒤)(?=$|[은는이가을를에에서쪽으로도엔\s,.!?])|(?:左|右|前|後ろ)(?=$|[はがをにで側\s、。.!?]))")]
    private static partial Regex DirectionPattern();

    [GeneratedRegex(@"(?ix)(?<![\p{L}\p{N}_])(?<count>one|two|three|four|five|한|하나|두|둘|세|셋|네|넷|다섯|一|二|三|四|五|[1-5])\s*(?:명|人|enemy|enemies|player|players)(?![\p{L}\p{N}_])")]
    private static partial Regex ExplicitCountPattern();

    [GeneratedRegex(@"(?<![\d一二三四五六七八九十百])(?<count>[1-5一二三四五])\s*人")]
    private static partial Regex JapaneseCountPattern();

    [GeneratedRegex(@"(?ix)(?<![\p{L}\p{N}_])(?<count>one|two|three|four|five|한|하나|두|둘|세|셋|네|넷|다섯|一|二|三|四|五|[1-5])(?![\p{L}\p{N}_])")]
    private static partial Regex BareCountPattern();

    [GeneratedRegex(@"(?ix)(?:enemy|enemies|player|players|적|상대|敵|mid|main|site|heaven|hell|short|long|left|right|front|back|미드|메인|사이트|헤븐|왼쪽|오른쪽|앞|뒤|ミッド|メイン|サイト|左|右|前|後ろ|\b[ABC]\b)")]
    private static partial Regex CalloutContextPattern();

    [GeneratedRegex(@"(?ix)(?:one\s*shot|1\s*hp|원샷|한\s*대|ワンショット)")]
    private static partial Regex OneShotPattern();

    [GeneratedRegex(@"(?<!\d)\d{1,3}(?!\d)")]
    private static partial Regex NumberPattern();
}

public sealed record TranslationFactGuardResult(string Text, bool Adjusted, string Reason);
