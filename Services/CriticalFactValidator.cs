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
        if (!string.Equals(messageType, "Tactical", StringComparison.OrdinalIgnoreCase))
            return new CriticalFactValidationResult { Passed = true, Code = "PASS", Detail = "social/loose" };

        var sourceFacts = Extract(source);
        var translatedFacts = Extract(translated);
        if (sourceFacts.HasNegation && !translatedFacts.HasNegation)
            return Fail("NEGATION_MISSING", "source negation not preserved");
        if (sourceFacts.HasUncertainty && !translatedFacts.HasUncertainty)
            return Fail("UNCERTAINTY_MISSING", "source uncertainty not preserved");
        if (sourceFacts.Counts.Count > 0 && translatedFacts.Counts.Count == 0)
            return Fail("COUNT_MISSING", "count dropped in translation");
        if (sourceFacts.Locations.Count > 0 && translatedFacts.Locations.Count == 0)
            return Fail("LOCATION_MISSING", "location dropped in translation");
        return new CriticalFactValidationResult { Passed = true, Code = "PASS" };
    }

    private static CriticalFactValidationResult Fail(string code, string detail) =>
        new() { Passed = false, Code = code, Detail = detail };

    private static FactSnapshot Extract(string text)
    {
        text = ChatTextSanitizer.ContentForLanguageDetection(text);
        var locations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in LocationPattern().Matches(text))
            locations.Add(match.Value.ToUpperInvariant());

        var counts = new HashSet<int>();
        foreach (Match match in CountPattern().Matches(text))
        {
            if (int.TryParse(match.Groups["count"].Value, out var value) && value is >= 1 and <= 5)
                counts.Add(value);
        }

        return new FactSnapshot(
            HasNegation: NegationPattern().IsMatch(text),
            HasUncertainty: UncertaintyPattern().IsMatch(text),
            Locations: locations,
            Counts: counts);
    }

    private sealed record FactSnapshot(bool HasNegation, bool HasUncertainty,
        HashSet<string> Locations, HashSet<int> Counts);

    [GeneratedRegex(@"(?ix)\b(?:not|no|none|nobody|don't|do not|아님|아니|없어|없음|없다|말고|ない|いない|不是|没有|并非)\b")]
    private static partial Regex NegationPattern();

    [GeneratedRegex(@"(?ix)\b(?:maybe|probably|perhaps|아마|추정|たぶん|多分|可能|大概)\b")]
    private static partial Regex UncertaintyPattern();

    [GeneratedRegex(@"(?ix)\b(?:[ABC]\s*(?:short|long|main|heaven|hell|site|mid)|[ABC](?:숏|롱|메인|헤븐|헬)|[ABC](?:ショート|ロング|メイン|ヘブン))\b")]
    private static partial Regex LocationPattern();

    [GeneratedRegex(@"(?ix)(?<count>[1-5])\b|(?<count>one|two|three|four|five|한|하나|두|둘|세|셋|네|넷|다섯|一|二|三|四|五|两|二)")]
    private static partial Regex CountPattern();
}
