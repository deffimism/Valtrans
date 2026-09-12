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

        var comparable = TranslationQuantityGuard.MaskEquivalentNumbers(source, translated);
        var facts = ExtractFacts(source, comparable.Source);
        var resultFacts = ExtractFacts(translated, comparable.Translated);
        var quantityMismatch = TranslationQuantityGuard.FindMismatch(source, translated);
        if (quantityMismatch is not null)
            throw new InvalidOperationException("번역 의미 확인 필요 · " + quantityMismatch + " · 수치·단위를 확인하지 못했습니다.");
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

    internal static CriticalFacts ExtractFacts(string text, string? comparableNumberText = null)
    {
        text = ChatTextSanitizer.ContentForLanguageDetection(text);
        text = WithoutNonSpatialDirections(text);
        var directions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in DirectionPattern().Matches(text))
            directions.Add(CanonicalDirection(match.Value));

        // Direction disambiguation needs the original time expression ("7초 뒤").
        // Number-only masking must not turn it back into a physical "뒤".
        return ExtractNumericFacts(comparableNumberText is null ? text :
            WithoutNonSpatialDirections(ChatTextSanitizer.ContentForLanguageDetection(comparableNumberText)), directions);
    }

    // Also used when choosing prompt glossary entries. A timing phrase must not
    // inject "Back" into a translation before the validator sees its output.
    internal static string WithoutNonSpatialDirections(string text)
    {
        // 前 is part of the pronoun お前 and the noun 名前, not a direction.
        // Keep separate spatial 前/後ろ in the same sentence available to guards.
        text = Regex.Replace(text, @"お前|名前", " ");
        // 'left' can be leave's past tense or indicate remaining time/items, not a direction.
        text = Regex.Replace(text,
            @"(?ix)\b(?:i|we|he|she|they|you)\s+(?:(?:have|had|just|already)\s+)*left\b|\b(?:time|seconds?|minutes?|rounds?|ammo|hp|health)\s+left\b",
            " ");
        text = Regex.Replace(text,
            @"(?ix)\b(?:be\s+)?right\s+back\b|\b(?:come|coming|came|go|going|went|get|getting|got)\s+back\b|\b(?:right\s+(?:now|away)|all\s+right|that's\s+right)\b",
            " ");
        text = Regex.Replace(text,
            @"(?ix)\b\d+\s+(?:seconds?|minutes?)\s+(?:left|back)\b|\d+\s*(?:초|분|시간)\s*(?:뒤|후)|(?:あと|後)\s*\d+\s*(?:秒|分)",
            match => Regex.Replace(match.Value, @"(?i)left|back|뒤|후|あと|後", " "));
        text = Regex.Replace(text, @"(?:잠시|조금|잠깐|이따)(?:\s*후|\s*뒤)(?:에)?", " ");
        // Predicate + 前に is 'before doing', not the physical direction 'front'.
        text = Regex.Replace(text, @"(?:(?<=[ぁ-ゖ]{2})|(?<=入る)|(?<=撃つ))前(?=に|は|まで)", " ");
        return text;
    }

    private static CriticalFacts ExtractNumericFacts(string text, HashSet<string> directions)
    {
        var counts = new HashSet<int>();
        // Korean commonly makes an English indefinite article explicit ("a Vandal"
        // -> "밴달을 하나"). This single-item counter is not another player.
        var countText = Regex.Replace(text,
            @"(?:밴달|팬텀|오퍼레이터|셰리프|고스트|권총|소총)(?:을|를)?\s*하나(?=$|[\s,.!?])",
            match => match.Value.Replace("하나", " "));
        // Two places on B is not two players on B. Keep ordinary measured-place
        // quantities separate instead of letting the bare callout counter accept
        // a player-to-place mistranslation simply because the digits agree.
        countText = Regex.Replace(countText,
            @"(?i)(?<![\p{L}\p{N}])(?<number>[1-5]|one|two|three|four|five|한|하나|두|둘|세|셋|네|넷|다섯)\s*(?:곳|군데|places?\b|spots?\b)",
            match => match.Value.Remove(0, match.Groups["number"].Length));
        // A single hit is not an extra player. Mask just its number, not all
        // headcounts in a line that happens to contain "one shot".
        countText = SingleHitNumberPattern().Replace(countText, match =>
        {
            var number = match.Groups["number"];
            return match.Value.Remove(number.Index - match.Index, number.Length);
        });
        foreach (Match match in ExplicitCountPattern().Matches(countText))
        {
            var value = NormalizeCount(match.Groups["count"].Value);
            if (value is >= 1 and <= 5) counts.Add(value);
        }
        // Japanese normally has no spaces: 左に二人 must still preserve the count.
        foreach (Match match in JapaneseCountPattern().Matches(text))
            counts.Add(NormalizeCount(match.Groups["count"].Value));
        // Generic Japanese counters are only people in an otherwise complete location call.
        // Do not reinterpret a shopping or multi-clause sentence as a headcount.
        var shortJapaneseCall = Regex.Match(text,
            @"^(?:[ABC]\s*)?(?:ミッド|メイン|ヘブン|ロング|ショート|サイト|左|右)\s*(?:に)?\s*(?<count>[1-5一二三四五])つ[。.!]?$");
        if (shortJapaneseCall.Success) counts.Add(NormalizeCount(shortJapaneseCall.Groups["count"].Value));
        foreach (Match match in Regex.Matches(text, @"(?<![\d一二三四五六七八九十百])(?<count>[1-5一二两三四五])\s*个"))
            counts.Add(NormalizeCount(match.Groups["count"].Value));
        foreach (Match match in KoreanCountPattern().Matches(countText))
            counts.Add(NormalizeCount(match.Groups["count"].Value));
        // Elliptical people counts: "one person, but there were three".
        // Do not treat unrelated "three rounds" as three people.
        foreach (Match match in Regex.Matches(countText,
            @"(?ix)\bthere\s+(?:is|are|was|were)\s+(?<count>one|two|three|four|five|[1-5])(?=\s*[,.;!?]|\s*$)"))
            counts.Add(NormalizeCount(match.Groups["count"].Value));
        if (CalloutCountRevision.TryParse(countText, out var estimated, out var actual))
        {
            counts.Add(estimated);
            counts.Add(actual);
        }
        // A quoted correction keeps its speaker/quotation intact for translation;
        // only its numerical facts are read here. Never translate the quote alone.
        foreach (Match quote in Regex.Matches(countText, "[\"“「『'](?<body>[^\"“”「」『』'\\r\\n]{1,200})[\"”」』']"))
            if (CalloutCountRevision.TryParse(quote.Groups["body"].Value, out var prior, out var revised))
            {
                counts.Add(prior);
                counts.Add(revised);
            }
        if (CalloutContextPattern().IsMatch(countText))
            foreach (Match match in Regex.Matches(countText,
                @"(?<![가-힣])(?<count>하나|둘|셋|넷)(?=라고|이라고|인\s*줄|이었)"))
                counts.Add(NormalizeCount(match.Groups["count"].Value));
        if (Regex.IsMatch(text, @"(?i)\balone\b|\bon my own\b|혼자|一人で")) counts.Add(1);
        if (CalloutContextPattern().IsMatch(text))
            foreach (Match match in BareCountPattern().Matches(countText))
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
        "two" or "두" or "둘" or "二" or "两" => 2,
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

    internal sealed record CriticalFacts(HashSet<string> Directions, HashSet<int> Counts,
        HashSet<int> OtherNumbers, HashSet<int> AllNumbers);

    [GeneratedRegex(@"(?ix)(?:(?<![A-Za-z0-9_])(?:left|right|front|back|behind|flank(?:ing)?)(?![A-Za-z0-9_])|플랭크|裏取り|(?:왼쪽|오른쪽|좌측|우측|전방|앞|뒤)(?=$|돌|[은는이가을를에에서쪽으로도엔\s,.!?])|(?:左|右|前|後ろ)(?=$|は|が|を|に|で|側|から|へ|じゃ|では|と|も|の|警戒|注意|見|行|向|[1-5一二三四五\s、。.!?]))")]
    private static partial Regex DirectionPattern();

    [GeneratedRegex(@"(?ix)(?<![\p{L}\p{N}_])(?<count>one|two|three|four|five|한|하나|두|둘|세|셋|네|넷|다섯|一|二|三|四|五|[1-5])\s*(?:enemy|enemies|players?|teammates?|allies|ally|people|persons?)(?![\p{L}\p{N}_])")]
    private static partial Regex ExplicitCountPattern();

    [GeneratedRegex(@"(?<![\d一二三四五六七八九十百])(?<count>[1-5一二三四五])\s*人")]
    private static partial Regex JapaneseCountPattern();

    [GeneratedRegex(@"(?<!\d)(?<count>[1-5]|한|하나|두|둘|세|셋|네|넷|다섯)\s*(?:명|팀원|동료|아군)(?=$|[이가은는을를의에과와도인\s,.!?])|(?<![가-힣])(?<count>하나|둘|셋|넷)(?=이|은|는|을|과|도|$|\s)")]
    private static partial Regex KoreanCountPattern();

    [GeneratedRegex(@"(?ix)(?<![\p{L}\p{N}_])(?<count>one|two|three|four|five|한|하나|두|둘|세|셋|네|넷|다섯|一|二|三|四|五|[1-5])(?![\p{L}\p{N}_])")]
    private static partial Regex BareCountPattern();

    [GeneratedRegex(@"(?ix)(?:enemy|enemies|player|players|적|상대|敵|mid|main|site|heaven|hell|short|long|left|right|front|back|미드|메인|사이트|헤븐|왼쪽|오른쪽|앞|뒤|ミッド|メイン|サイト|左|右|前|後ろ|\b[ABC]\b)")]
    private static partial Regex CalloutContextPattern();

    [GeneratedRegex(@"(?ix)(?<![\p{L}\p{N}_])(?<number>one|1)[ -]*shot(?![A-Za-z])|(?<![가-힣])(?<number>한)\s*(?:대|방|발)(?=$|[\s,.!?]|에|으로|으론|만|씩|이|을|맞|면)|(?<number>一)発|(?<![\d])(?<number>1)\s*hp\b")]
    private static partial Regex SingleHitNumberPattern();

    [GeneratedRegex(@"(?<!\d)\d{1,9}(?!\d)")]
    private static partial Regex NumberPattern();
}

public sealed record TranslationFactGuardResult(string Text, bool Adjusted, string Reason);
