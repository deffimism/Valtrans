using System.Text.RegularExpressions;

namespace Valtrans.Services;

public static class ChatTextSanitizer
{
    private static readonly char[] StrongHeaderSeparators = { ':', '：', '﹕', '꞉', '∶' };

    private static readonly Regex ChannelPrefix = new(
        @"^\s*(?:[\(\[\{<＜【（]\s*)?(?:party|team|all|whisper|squad|파티|팀|전체|귓속말|분대|パーティー?|チーム|全体|ささやき)(?=\s|[\)\]\}>＞】）])(?:\s*[\)\]\}>＞】）])?\s*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex SystemLine = new(
        @"^\s*(?:[\(\[\{<＜【（]\s*)?(?:system|notice|시스템|알림|공지|システム|通知|お知らせ)(?:\s*[\)\]\}>＞】）]|\s*[:：﹕꞉∶])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex SimpleNickname = new(
        @"^[\p{L}\p{N}][\p{L}\p{N}\s._#\-\[\]\(\)]{0,47}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SingleTokenNickname = new(
        @"^(?<name>[\p{L}\p{N}][\p{L}\p{N}._#\-]{1,31})\s+(?<body>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> CalloutPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "b", "c", "a site", "b site", "c site", "site", "mid", "enemy", "enemies",
        "left", "right", "front", "back", "short", "long", "heaven", "hell", "spawn", "ct",
        "적", "미드", "왼쪽", "오른쪽", "앞", "뒤", "사이트", "敵", "ミッド", "左", "右"
    };

    private static readonly (string Romanized, string Japanese)[] RomanizedJapaneseTerms =
    {
        ("wakari mashita", "わかりました"), ("wakari masita", "わかりました"),
        ("wakarimashita", "わかりました"), ("wakarimasita", "わかりました"),
        ("wakarimasen", "わかりません"), ("wakatta", "わかった"),
        ("daijoubu", "大丈夫"), ("daijobu", "大丈夫"), ("arigatou", "ありがとう"), ("arigato", "ありがとう"),
        ("sumimasen", "すみません"), ("gomennasai", "ごめんなさい"), ("gomen", "ごめん"),
        ("yoroshiku", "よろしく"), ("onegaishimasu", "お願いします"), ("onegai", "お願い"),
        ("kudasai", "ください"), ("otsukare", "お疲れ"), ("ohayou", "おはよう"),
        ("ryoukai", "了解"), ("ryokai", "了解"), ("tasukete", "助けて"),
        ("teki", "敵"), ("nakama", "味方"), ("doko", "どこ"), ("koko", "ここ"), ("soko", "そこ"),
        ("middo", "ミッド"), ("hidari", "左"), ("migi", "右"), ("ushiro", "後ろ"), ("mae", "前"),
        ("hitori", "一人"), ("futari", "二人"), ("sannin", "三人"), ("yonin", "四人"),
        ("roote", "ローテ"), ("ikimasu", "行きます"), ("kimashita", "来ました"),
        ("irimasu", "います"), ("inai", "いない"), ("hayaku", "早く")
    };

    private static readonly Regex RomanizedJapanese = new(
        @"\b(?:arigatou|arigato|wakarimashita|wakarimasita|wakari\s+ma(?:shi|si)ta|wakatta|wakarimasen|daijou?bu|sumimasen|gomen(?:nasai)?|yoroshiku|onegai(?:shimasu)?|kudasai|ohayou|konbanwa|sayonara|otsukare|ryou?kai|tasukete|teki|nakama|doko|koko|soko|middo|hidari|migi|ushiro|mae|hitori|futari|sannin|yonin|roote|hayaku|ikimasu|kimashita|irimasu|inai|desu|masu|janai)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static string ContentForLanguageDetection(string text)
    {
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(StripChatPrefix)
            .Where(line => line.Length > 0);
        return string.Join(Environment.NewLine, lines);
    }

    public static string MessageBodies(string text) => ContentForLanguageDetection(text);

    public static bool HasMeaningfulContent(string text)
    {
        text = ContentForLanguageDetection(text).Trim();
        return text.Any(char.IsLetterOrDigit);
    }

    public static bool LooksLikeRomanizedJapanese(string text)
    {
        text = ContentForLanguageDetection(text);
        return RomanizedJapanese.IsMatch(text);
    }

    public static string ConvertCommonRomanizedJapanese(string text)
    {
        if (!LooksLikeRomanizedJapanese(text)) return text;
        foreach (var (romanized, japanese) in RomanizedJapaneseTerms)
            text = Regex.Replace(text, $@"\b{Regex.Escape(romanized)}\b", japanese,
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        return text;
    }

    public static string StripChatPrefix(string line)
    {
        line = line.Trim();
        if (line.Length == 0 || SystemLine.IsMatch(line)) return "";

        var channelMatch = ChannelPrefix.Match(line);
        var hasChannel = channelMatch.Success && channelMatch.Length > 0;
        var separator = line.IndexOfAny(StrongHeaderSeparators);
        if (separator > 0)
        {
            var prefix = line[..separator].Trim();
            var body = line[(separator + 1)..].Trim();
            if (body.Length > 0 && IsLikelyChatHeader(prefix, hasChannel)) return body;
        }

        // Windows OCR sometimes recognizes ':' as ';' or '；'. Only trust this weaker
        // separator when a known game channel marker is present.
        if (hasChannel)
        {
            var weakSeparator = line.IndexOfAny(new[] { ';', '；', '|' }, channelMatch.Length);
            if (weakSeparator > channelMatch.Length)
            {
                var body = line[(weakSeparator + 1)..].Trim();
                if (body.Length > 0) return body;
            }

            // If punctuation disappeared entirely, remove a single obvious ID-style
            // nickname after the channel marker. Natural message words are retained.
            var remainder = line[channelMatch.Length..].Trim();
            var nicknameMatch = SingleTokenNickname.Match(remainder);
            if (nicknameMatch.Success)
            {
                var possibleNickname = nicknameMatch.Groups["name"].Value;
                if (!CalloutPrefixes.Contains(possibleNickname) && IsHighConfidenceNickname(possibleNickname))
                    return nicknameMatch.Groups["body"].Value.Trim();
            }

            // The channel marker itself is reliable even when the nickname separator
            // vanished. Keep the complete remainder rather than dropping a callout word.
            return remainder;
        }

        return line;
    }

    private static bool IsLikelyChatHeader(string prefix, bool hasChannel)
    {
        if (hasChannel) return true;
        if (prefix.Length is < 2 or > 48 || CalloutPrefixes.Contains(prefix)) return false;
        if (prefix.Count(char.IsWhiteSpace) > 3) return false;
        return SimpleNickname.IsMatch(prefix);
    }

    private static bool IsHighConfidenceNickname(string value)
    {
        if (value.Any(ch => char.IsDigit(ch) || ch is '#' or '_' or '.' or '-')) return true;
        return value.Count(char.IsUpper) >= 2;
    }
}
