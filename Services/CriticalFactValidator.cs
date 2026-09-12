using System.Text.RegularExpressions;
using Valtrans.Models;

namespace Valtrans.Services;

public sealed class CriticalFactValidationResult
{
    public bool Passed { get; set; }
    public string Code { get; set; } = "PASS";
    public string Detail { get; set; } = "";
}

/// <summary>Phase 5: tactical fact preservation including negation and uncertainty.</summary>
public static partial class CriticalFactValidator
{
    public static CriticalFactValidationResult Validate(string source, string translated, string messageType)
    {
        var quantityMismatch = TranslationQuantityGuard.FindMismatch(source, translated);
        if (quantityMismatch is not null) return Fail(quantityMismatch, "explicit measured quantity or unit changed");
        // Correcting what was meant is meaningful even in social chat. Do not let
        // the loose social path turn "I don't mean one round" into "I mean one round".
        // This is deliberately narrower than all negative words (no worries -> 괜찮아).
        if (MeaningDenialPattern().IsMatch(source) && !Extract(translated).HasNegation)
            return Fail("NEGATION_MISSING", "explicit denial of meaning not preserved");
        if (!string.Equals(messageType, "Tactical", StringComparison.OrdinalIgnoreCase))
            return new CriticalFactValidationResult { Passed = true, Code = "PASS", Detail = "social/loose" };

        var sourceFacts = Extract(source);
        var translatedFacts = Extract(translated);
        if (sourceFacts.HasNegation && !translatedFacts.HasNegation)
            return Fail("NEGATION_MISSING", "source negation not preserved");
        if (sourceFacts.HasUncertainty && !translatedFacts.HasUncertainty)
            return Fail("UNCERTAINTY_MISSING", "source uncertainty not preserved");
        var comparable = TranslationQuantityGuard.MaskEquivalentNumbers(source, translated);
        var before = TranslationFactGuard.ExtractFacts(source, comparable.Source);
        var after = TranslationFactGuard.ExtractFacts(translated, comparable.Translated);
        if (!before.Directions.SetEquals(after.Directions))
            return Fail("DIRECTION_CHANGED", "direction added, removed or changed");
        if (!before.Counts.SetEquals(after.Counts))
            return Fail("COUNT_CHANGED", "explicit count added, removed or changed");
        if (!before.OtherNumbers.SetEquals(after.OtherNumbers))
            return Fail("NUMBER_CHANGED", "damage, time or other number changed");
        if (!sourceFacts.Locations.SetEquals(translatedFacts.Locations))
            return Fail("LOCATION_CHANGED", "site or area added, removed or changed");
        return new CriticalFactValidationResult { Passed = true, Code = "PASS" };
    }

    private static CriticalFactValidationResult Fail(string code, string detail) =>
        new() { Passed = false, Code = code, Detail = detail };

    private static FactSnapshot Extract(string text)
    {
        text = ChatTextSanitizer.ContentForLanguageDetection(text);
        text = Regex.Replace(text, @"(?<![A-Za-z0-9])([ABC])小", "$1 Short");
        text = Regex.Replace(text, @"(?i)\b(?:for|too|how|so)\s+long\b|\blong\s+(?:time|enough|story)\b", " ");
        text = Regex.Replace(text, @"(?i)(?<![A-Za-z0-9_])(?<area>Heaven|Main|Hell|Short|Long|Mid|Site|메인|헤븐|헬|숏|롱|미드|사이트|メイン|ヘブン|ヘル|ショート|ロング|ミッド|サイト)\s+(?<site>(?-i:[ABC]))(?=$|[^A-Za-z0-9_])",
            "${site} ${area}");
        var locations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rest = LocationPattern().Replace(text, match =>
        {
            var area = match.Groups["area"].Value.ToLowerInvariant() switch
            {
                "메인" or "メイン" => "main", "헤븐" or "ヘブン" => "heaven",
                "헬" or "ヘル" => "hell", "숏" or "ショート" => "short",
                "롱" or "ロング" => "long", "미드" or "ミッド" or "中路" => "mid",
                "사이트" or "サイト" => "site", var other => other
            };
            locations.Add((match.Groups["site"].Value.ToUpperInvariant() + " " + area).Trim());
            return " ";
        });
        foreach (Match match in SitePattern().Matches(rest))
            locations.Add(match.Groups["site"].Value.ToUpperInvariant());

        return new FactSnapshot(
            HasNegation: NegationPattern().IsMatch(Regex.Replace(text, @"かもしれない", "")) ||
                EnglishContractedNegation().IsMatch(text) ||
                Regex.IsMatch(text, @"せず(?=に|、|。|\s|$)|ません|대신(?:에)?(?=$|[\s,.!?])|の代わりに|모름(?=$|[\s,.!?])") ||
                KoreanUnknownConjugation().IsMatch(text) ||
                Regex.IsMatch(text, @"(?i)\b(?:unknown|unsure|uncertain)\b"),
            HasUncertainty: UncertaintyPattern().IsMatch(text),
            Locations: locations);
    }

    private sealed record FactSnapshot(bool HasNegation, bool HasUncertainty,
        HashSet<string> Locations);

    // 모르다 contracts differently in 모른다/모릅니다/몰랐다. Matching only
    // 모르/몰라 rejects valid Korean translations of "don't know".
    [GeneratedRegex(@"(?<![가-힣])(?:모른(?:다|다고|다는|대|다니)?|모를(?:걸|지도|까)?|모릅(?:니다|니까)|몰랐(?:다|어|어요|는데|던|지|습니다)|불명)(?=$|[\s,.!?])")]
    private static partial Regex KoreanUnknownConjugation();

    [GeneratedRegex(@"(?ix)(?:말하는\s*(?:거|게|것)|뜻(?:이)?|의미(?:가)?)\s*아니|\b(?:don['’]t|do\s+not|didn['’]t|did\s+not)\s+mean\b|(?:意味|ってこと)(?:じゃ|では)ない")]
    private static partial Regex MeaningDenialPattern();

    [GeneratedRegex(@"(?ix)(?<![A-Za-z])(?:not|no|none|nobody|don['’]t|do\s+not|never)(?![A-Za-z])|아님|아니|아닌|아닙|없|말고|금지|모르|몰라|[가-힣]+지\s*마|않|(?<![가-힣])안\s|ない|なく|なし|禁止|(?:するな|入るな)(?=$|[よね\s、。.!！?？])|不是|没有|并非|instead\s+of")]
    private static partial Regex NegationPattern();

    [GeneratedRegex(@"(?i)\b(?:(?:don|doesn|didn|isn|aren|wasn|weren|hasn|haven|hadn|can|couldn|won|wouldn|shouldn|mustn|needn|ain)['’]t|cannot)\b")]
    private static partial Regex EnglishContractedNegation();

    [GeneratedRegex(@"(?ix)\b(?:maybe|probably|perhaps|might|could|think|guess)\b|아마|추정|같아|같음|같은|같다|같습|지도|일\s*수|있을\s*수|たぶん|多分|おそらく|かもしれ|かも|と思(?!った|いました|っていた|っていました)|可能|大概|어쩌면")]
    private static partial Regex UncertaintyPattern();

    [GeneratedRegex(@"(?ix)(?<![A-Za-z0-9_])(?:(?<site>[ABC])\s*)?(?<area>(?:main|heaven|hell|short|long|mid|site)(?![A-Za-z])|메인|헤븐|헬|숏|롱|미드|사이트|メイン|ヘブン|ヘル|ショート|ロング|ミッド|サイト|中路)")]
    private static partial Regex LocationPattern();

    // Exclude English 'a friend' but keep standalone and explicit destination A.
    [GeneratedRegex(@"(?x)(?<![A-Za-z0-9_])(?<site>[ABCbc])(?=$|[^A-Za-z0-9_]|[1-5]人)|^(?<site>a)$")]
    private static partial Regex SitePattern();
}
