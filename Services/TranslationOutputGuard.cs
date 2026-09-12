using System.Text.RegularExpressions;

namespace Valtrans.Services;

internal static class TranslationOutputGuard
{
    public static void Validate(string source, string output)
    {
        if (Regex.IsMatch(output, @"<unk>|\[unk\]|\uFFFD|\u2047", RegexOptions.IgnoreCase))
            Reject("깨진 문자·알 수 없는 토큰");
        if (!source.Contains("{{", StringComparison.Ordinal) &&
            Regex.IsMatch(output, @"\{\{(?:/?if\b|html\b|[^}\r\n]+\()", RegexOptions.IgnoreCase))
            Reject("원문에 없는 템플릿 코드가 생성됨");
        // Observed Lite decoder failure on ordinary bathroom-break messages.
        // Narrow protection against newly invented explicit vocabulary, not a chat profanity filter.
        if (Regex.IsMatch(output, @"(?i)\b(?:handjobs?|blowjobs?)\b") &&
            !Regex.IsMatch(source, @"(?i)\b(?:handjobs?|blowjobs?|sex|sexual|porn)\b|수음|구강성교|펠라|핸드잡|블로우잡|手コキ|フェラ|性行為"))
            Reject("원문에 없는 성적 표현이 생성됨");
        foreach (var marker in new[] { "[Source Text]", "[Translation Tasks]", "[원본 텍스트]", "Reference the following translations" })
            if (output.Contains(marker, StringComparison.OrdinalIgnoreCase) &&
                !source.Contains(marker, StringComparison.OrdinalIgnoreCase)) Reject("번역 지시문이 결과에 섞임");

        // A purchase/damage call may have an implicit unit. Do not accept e.g. 3900
        // credits or 80 damage being turned into people/hits. Not a full semantic judge.
        if (Regex.IsMatch(source, @"(?i)\b(?:buy|credits?|hit|damage|dmg)\b|사줄|사주|크레딧|넣었|피해|買|入れた"))
            foreach (Match match in Regex.Matches(output, @"(?i)(?<number>\d{2,})\s*(?:명|人|people\b|players?\b|hits?\b|타(?=$|[를가의는에로\s,.!?]))"))
            {
                var number = match.Groups["number"].Value;
                if (Regex.IsMatch(source, $@"(?<!\d){number}(?!\d)") &&
                    !Regex.IsMatch(source, $@"(?i)(?<!\d){number}\s*(?:명|人|people\b|players?\b|hits?\b|타(?=$|[를가의는에로\s,.!?]))"))
                    Reject("수치의 단위가 인원·횟수로 바뀜");
            }
    }

    private static void Reject(string reason) => throw new InvalidOperationException(
        $"번역 의미 확인 필요 · {reason} · 의심스러운 결과를 사용하지 않았습니다.");
}
