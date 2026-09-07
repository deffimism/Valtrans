using System.Text;
using System.Text.RegularExpressions;
using Valtrans.Models;

namespace Valtrans.Services;

/// <summary>Conservative Lite output checks, not a semantic correctness score.</summary>
public static class LiteTranslationGuard
{
    private static Regex Pattern(string value) => new(value,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Broken = Pattern(@"<unk>|\[unk\]|\uFFFD");
    private static readonly Regex Questions = Pattern(@"[?？]{2,}");
    private static readonly Regex Negative = Pattern(
        @"\b(?:no|not|none|nobody|never|without|cannot|absent|missing|unable|stop|avoid|can['’]?t|won['’]?t|don['’]?t|doesn['’]?t|didn['’]?t|isn['’]?t|aren['’]?t)\b|없|않|아니|아님|못|모르|모름|몰라|말(?:고|자)|금지|불가|그만|멈춰|안\s+(?=[가-힣])|안(?:돼|되|가|와|오|해|했|보|죽|맞|밀|들어)|[가-힣]+지(?:는)?\s*마|ない|なく|ません|ずに|なし|禁止");
    private static readonly Regex Prohibition = Pattern(
        @"(?:^|[.!?,;]\s*|\bplease\s+)(?:do\s+not|don['’]?t|avoid|stop)\b|\b(?:must|should)\s+not\b|\bwithout\b|[가-힣]+지(?:는)?\s*마|말(?:고|자)|금지|그만|멈춰|ないで|ないように|禁止|(?:する|行く|来る)な(?:[\s、。!！]|$)");
    private static readonly Regex Uncertain = Pattern(
        @"\b(?:maybe|probably|perhaps|might|seems?)\b|\bcould\s+be\b|\bi\s+think\b|아마|추정|어쩌면|같(?:아|음|다|습니다)|일\s*수도|多分|たぶん|かも|と思|ようです|おそらく");
    private static readonly Regex RepeatedWords = Pattern(@"(?<![\p{L}\p{N}])(?<phrase>[\p{L}\p{N}]+(?:\s+[\p{L}\p{N}]+){0,3}?)(?<repeat>[\s,.!?、。]+\k<phrase>){2,}(?![\p{L}\p{N}])");
    private static readonly Regex RepeatedCjk = Pattern(@"(?<phrase>[\u3040-\u30ff\u4e00-\u9fff\uac00-\ud7af]{2,12}?)(?<repeat>\k<phrase>){2,}");
    private static readonly Regex Digits = Pattern(@"(?<!\d)\d{1,4}(?!\d)");
    private static readonly (Regex Pattern, string Value)[] NumberWords = BuildNumberWords();

    public static string Validate(string source, string translated, string targetLanguage,
        AppSettings settings, GlossaryService glossary)
    {
        if (source.Length > 4096 || translated.Length > 4096) Reject("문장 길이 확인 필요");
        var sourceLines = source.Replace("\r", "").Split('\n');
        var resultLines = translated.Replace("\r", "").Split('\n');
        if (sourceLines.Length != resultLines.Length) Reject("원문·번역 줄 수 불일치");
        if (sourceLines.Length > 1)
            return string.Join(Environment.NewLine, sourceLines.Zip(resultLines,
                (s, t) => Validate(s, t, targetLanguage, settings, glossary)));

        if (!ChatTextSanitizer.HasMeaningfulContent(translated)) Reject("빈 결과 또는 읽을 수 없는 결과");
        if (Broken.IsMatch(translated) || Questions.IsMatch(translated) && !Questions.IsMatch(source))
            Reject("깨진 문자·알 수 없는 토큰");
        if (LongestRepeat(translated) > Math.Max(2, LongestRepeat(source))) Reject("비정상적인 반복");
        var same = Compact(source).Equals(Compact(translated), StringComparison.OrdinalIgnoreCase);
        if (same && source.Any(char.IsLetter) && !glossary.IsStandaloneCanonicalReference(source, settings))
            Reject("원문이 그대로 반환됨");

        // Inspect raw model output BEFORE any marker prefixing/compaction.
        if (Negative.IsMatch(source) && !Negative.IsMatch(translated)) Reject("부정 표현 누락");
        if (Prohibition.IsMatch(source) && !Prohibition.IsMatch(translated)) Reject("금지 지시 누락");
        if (Uncertain.IsMatch(source) && !Uncertain.IsMatch(translated)) Reject("불확실성 표현 누락");
        var sourceNumbers = Digits.Matches(source).Select(match => int.Parse(match.Value)).ToHashSet();
        if (sourceNumbers.Count > 0)
        {
            var resultNumbers = Digits.Matches(NormalizeNumberWords(translated))
                .Select(match => int.Parse(match.Value)).ToHashSet();
            if (sourceNumbers.Except(resultNumbers).Any()) Reject("수치 누락 또는 변경");
        }
        try
        {
            return TranslationFactGuard.Apply(source, translated, targetLanguage, settings, glossary).Text;
        }
        catch (InvalidOperationException)
        {
            Reject("방향·인원·수치 확인 필요");
            throw;
        }
    }

    private static string Compact(string text) => string.Concat(text.Normalize(NormalizationForm.FormKC)
        .Where(ch => char.IsLetterOrDigit(ch)));

    private static int LongestRepeat(string value) => RepeatedWords.Matches(value).Cast<Match>()
        .Concat(RepeatedCjk.Matches(value).Cast<Match>())
        .Select(match => match.Groups["repeat"].Captures.Count + 1).DefaultIfEmpty(1).Max();

    private static string NormalizeNumberWords(string value)
    {
        foreach (var replacement in NumberWords)
            value = replacement.Pattern.Replace(value, replacement.Value);
        return value;
    }

    private static (Regex Pattern, string Value)[] BuildNumberWords()
    {
        var replacements = new List<(Regex, string)>();
        var english = new[] { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten" };
        var korean = new[] { "영", "한|하나", "두|둘", "세|셋", "네|넷", "다섯", "여섯", "일곱", "여덟", "아홉", "열" };
        var japanese = new[] { "零", "一", "二", "三", "四", "五", "六", "七", "八", "九", "十" };
        for (var number = 0; number <= 10; number++)
        {
            replacements.Add((Pattern($@"\b{english[number]}\b"), number.ToString()));
            replacements.Add((Pattern($@"(?<![가-힣])(?:{korean[number]})\s*(?=명|개|발|초|번)"), number.ToString()));
            replacements.Add((Pattern($@"(?<![一二三四五六七八九十]){japanese[number]}(?=人|個|発|秒|回)"), number.ToString()));
        }
        return replacements.ToArray();
    }

    private static void Reject(string reason) => throw new InvalidOperationException(
        $"Lite 번역 확인 필요 · {reason} · 의심스러운 번역문은 표시하지 않았습니다. 로컬 AI 엔진을 사용할 수 있습니다.");
}
