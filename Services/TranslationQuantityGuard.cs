using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Valtrans.Services;

/// <summary>Explicit measured quantities, not a general semantic correctness score.</summary>
internal static class TranslationQuantityGuard
{
    private const string Number = @"\d+(?:\.\d+)?|zero|one|two|three|four|five|six|seven|eight|nine|ten|영|한|하나|두|둘|세|셋|네|넷|다섯|여섯|일곱|여덟|아홉|열|일|이|삼|사|오|육|칠|팔|구|십|零|一|二|三|四|五|六|七|八|九|十";
    // Japanese words are not whitespace-delimited. Keep numeral boundaries so
    // 十二 cannot be misread as 二; Korean/English number words still need a boundary.
    private const string BoundedNumber = @"(?<![A-Za-z0-9_.])\d+(?:\.\d+)?|(?<![\p{L}\p{N}_.])(?:zero|one|two|three|four|five|six|seven|eight|nine|ten|영|한|하나|두|둘|세|셋|네|넷|다섯|여섯|일곱|여덟|아홉|열|일|이|삼|사|오|육|칠|팔|구|십)|(?<![A-Za-z0-9_.零一二三四五六七八九十百千万])(?:零|一|二|三|四|五|六|七|八|九|十)";
    private const string Unit = @"seconds?|secs?|minutes?|mins?|hours?|rounds?|matches|match|games?|hp|health|dmg|damage|credits?|places?|spots?|곳|군데|초|분|시간|라운드|경기|게임|판|번|회|체력|피해|데미지|크레딧|秒|分|時間|ラウンド|試合|回|ダメージ|クレジット";
    private static readonly Regex Suffix = new($@"(?<n>{BoundedNumber})\s*(?:(?:more|extra|additional|remaining)\s+|(?<=\s)번\s*(?:만\s*)?(?:더\s*)?(?=라운드))?(?<unit>{Unit})(?![A-Za-z])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Prefix = new($@"(?<![A-Za-z])(?<unit>hp|health|체력|피해|데미지|ダメージ)(?:は|が|는|은|이|가)?\s*(?:[:：=]\s*)?(?:残り\s*)?(?<n>{Number})(?![A-Za-z0-9.])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex KoreanRoundPrefix = new($@"(?<unit>라운드)\s*(?<n>{Number})\s*번",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    // Counted activities can put the unit first: 라운드 하나 / ラウンド1つ.
    // Recognize these before the legacy headcount guard mistakes 하나 for a player.
    private static readonly Regex ActivityPrefix = new($@"(?<unit>라운드|경기|게임|ラウンド|試合)\s*(?<n>{Number})(?:\s*(?:번|회|つ|回))?(?=$|[\s,.!?。]|(?:만|은|는|이|가|을|를|도|라는|라고|이라는|이라고)(?=$|[\s,.!?])|(?<=[つ回])[^0-9])",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly string[][] Words =
    [
        ["zero", "영", "零"], ["one", "한", "하나", "일", "一"], ["two", "두", "둘", "이", "二"],
        ["three", "세", "셋", "삼", "三"], ["four", "네", "넷", "사", "四"], ["five", "다섯", "오", "五"],
        ["six", "여섯", "육", "六"], ["seven", "일곱", "칠", "七"], ["eight", "여덟", "팔", "八"],
        ["nine", "아홉", "구", "九"], ["ten", "열", "십", "十"]
    ];
    private sealed record Measure(string? Kind, decimal Value, decimal RawValue, int NumberStart, int NumberLength);
    private sealed record Snapshot(string Text, List<Measure> Measures, HashSet<decimal> Numbers);

    // The legacy untyped number/headcount check must not reject 1 minute -> 60
    // seconds, or mistake "two seconds at mid" for two people. Mask ONLY the
    // numeric tokens in measurements explicitly matched in both languages.
    internal static (string Source, string Translated) MaskEquivalentNumbers(string source, string translated)
    {
        var before = Extract(source);
        var after = Extract(translated);
        static bool Equivalent(Measure a, Measure b) => a.Kind is not null && a.Kind == b.Kind && a.Value == b.Value;
        static string Mask(Snapshot snapshot, IEnumerable<Measure> matches)
        {
            var characters = snapshot.Text.ToCharArray();
            foreach (var measure in matches)
                Array.Fill(characters, ' ', measure.NumberStart, measure.NumberLength);
            return new string(characters);
        }
        return (Mask(before, before.Measures.Where(a => after.Measures.Any(b => Equivalent(a, b)))),
            Mask(after, after.Measures.Where(b => before.Measures.Any(a => Equivalent(a, b)))));
    }

    internal static string? FindMismatch(string source, string translated)
    {
        var before = Extract(source);
        if (!before.Measures.Any(item => item.Kind is not null)) return null;
        var after = Extract(translated);
        foreach (var measure in before.Measures)
        {
            if (measure.Kind is null)
            {
                // Generic counters can be idiomatic (한번 해봐 -> give it a try).
                // They help recognize an implicit target unit, but cannot prove
                // a timed/match fact on their own.
                continue;
            }
            var sameKind = after.Measures.Where(item => item.Kind == measure.Kind).ToArray();
            if (sameKind.Length > 0)
            {
                if (!sameKind.Any(item => item.Value == measure.Value)) return "QUANTITY_CHANGED";
                if (sameKind.Any(item => !before.Measures.Any(original => original.Kind == item.Kind && original.Value == item.Value)
                    && !before.Numbers.Contains(item.RawValue))) return "QUANTITY_ADDED";
            }
            else
            {
                if (!after.Numbers.Contains(measure.RawValue)) return "QUANTITY_MISSING";
                if (after.Measures.Any(item => item.Kind is null && item.RawValue == measure.RawValue)) continue;
                // An omitted unit can be natural in a terse callout. An explicit
                // conflicting unit with the same number is different.
                if (after.Measures.Any(item => item.Kind is not null && item.RawValue == measure.RawValue))
                    return "UNIT_CHANGED";
            }
        }
        return null;
    }

    private static Snapshot Extract(string value)
    {
        value = value.Normalize(NormalizationForm.FormKC);
        value = Regex.Replace(value, @"(?<!\d)\d{1,3}(?:,\d{3})+(?!\d)", match => match.Value.Replace(",", ""));
        var numbers = Regex.Matches(value, @"(?<![\d.])\d+(?:\.\d+)?(?![\d.])")
            .Select(match => decimal.TryParse(match.Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n) ? n : -1)
            .Where(n => n >= 0).ToHashSet();
        var measures = new List<Measure>();
        foreach (var match in Suffix.Matches(value).Cast<Match>().Concat(Prefix.Matches(value).Cast<Match>())
                     .Concat(KoreanRoundPrefix.Matches(value).Cast<Match>())
                     .Concat(ActivityPrefix.Matches(value).Cast<Match>()))
        {
            var number = match.Groups["n"].Value;
            var raw = decimal.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n)
                ? n : Array.FindIndex(Words, words => words.Contains(number.ToLowerInvariant()));
            if (raw < 0) continue;
            var unit = match.Groups["unit"].Value.ToLowerInvariant();
            // Native Korean 한/두/세 분 are respectful people counters, not minutes.
            if (unit == "분" && number is "한" or "두" or "세" or "네" or "다섯" or "여섯" or "일곱" or "여덟" or "아홉" or "열") continue;
            // "One second, my delivery is here" is a request to wait, not a clock.
            if (match.Index == 0 && number.Equals("one", StringComparison.OrdinalIgnoreCase) &&
                unit == "second" && value[match.Length..].TrimStart().StartsWith(',')) continue;
            var (kind, factor) = unit switch
            {
                "second" or "seconds" or "sec" or "secs" or "초" or "秒" => ("time", 1m),
                "minute" or "minutes" or "min" or "mins" or "분" or "分" => ("time", 60m),
                "hour" or "hours" or "시간" or "時間" => ("time", 3600m),
                "round" or "rounds" or "라운드" or "ラウンド" => ("round", 1m),
                "match" or "matches" or "game" or "games" or "경기" or "게임" or "試合" => ("match", 1m),
                "hp" or "health" or "체력" => ("hp", 1m),
                "dmg" or "damage" or "피해" or "데미지" or "ダメージ" => ("damage", 1m),
                "credit" or "credits" or "크레딧" or "クレジット" => ("credits", 1m),
                "place" or "places" or "spot" or "spots" or "곳" or "군데" => ("place", 1m),
                _ => ((string?)null, 1m) // 판/번/회/回 can leave the measured activity implicit.
            };
            numbers.Add(raw);
            measures.Add(new Measure(kind, raw * factor, raw, match.Groups["n"].Index, match.Groups["n"].Length));
        }
        return new Snapshot(value, measures, numbers);
    }
}
