using System.Text.RegularExpressions;
using Valtrans.Models;

namespace Valtrans.Services;

public sealed partial class GlossaryService
{
    // These words have ordinary meanings too. Expand only complete, recognised phrases.
    private static readonly HashSet<string> ContextSensitiveTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "bot", "top", "low", "lit", "force", "save", "op", "bat", "batt", "cell", "tap",
        "trade", "default", "hold", "plant", "cracked", "knocked", "beam", "ring", "zone",
        "banner", "craft", "stack", "short", "long", "hell"
    };

    private sealed record SlangPhrase(string En, string Ko, string Jp, bool Tactical, string? Game,
        params string[] Aliases)
    {
        public string Translate(string target) => target switch { "KO" => Ko, "JP" => Jp, _ => En };
        public bool Supports(AppSettings? settings) => Game is null ||
            settings?.Game.Equals(Game, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static readonly SlangPhrase[] SlangPhrases =
    [
        new("no problem", "괜찮아", "大丈夫", false, null, "np", "no problem", "ㄱㅊ", "괜춘", "문제없어"),
        new("all good", "괜찮아", "大丈夫", false, null, "nws", "no worries", "dw", "don't worry", "dont worry", "ドンマイ", "どんまい", "donmai"),
        new("nice job", "잘했어", "ナイス", false, null, "nj", "gj", "nice job", "good job", "나이스", "ナイス", "ないす"),
        new("well played", "잘했어", "うまい", false, null, "wp", "well played"),
        new("good luck, have fun", "즐겜하자", "楽しもう", false, null, "glhf", "gl hf", "즐겜"),
        new("be right back", "잠깐 자리 비움", "すぐ戻る", false, null, "brb", "잠깐 자리 비움"),
        new("away from keyboard", "자리 비움", "離席中", false, null, "afk", "자리 비움", "離席中"),
        new("on my way", "가는 중", "向かってる", true, null, "omw", "on my way", "가는 중", "합류 중", "向かってる"),
        new("wait", "잠깐만", "待って", true, null, "sec", "1 sec", "one sec", "잠만", "잠깐만", "ちょい待ち"),
        new("let's go", "가자", "行こう", true, null, "ㄱㄱ", "고고"),
        new("don't peek", "피킹 금지", "ピークしないで", true, null, "don't peek", "dont peek", "no peek", "피킹하지 마", "피킹 ㄴㄴ", "ピークしないで"),
        new("swing together", "같이 나가자", "一緒にピーク", true, null, "double swing", "swing together", "같이 피킹", "같이 나가자", "一緒にピーク"),
        new("trade me", "내 킬 교환해 줘", "カバーキルお願い", true, null, "trade me", "cover my trade", "트레이드 해줘", "교환해줘"),
        new("play for time", "시간 끌어", "時間を稼いで", true, null, "play time", "play for time", "시간 끌어", "時間稼いで"),
        new("fall back", "빠져", "引いて", true, null, "fall back", "back off", "disengage", "빼자", "빠져", "引いて", "引こう"),
        new("reset the fight", "빠져서 재정비", "引いて立て直そう", true, null, "reset fight", "reset the fight", "재정비하자", "立て直そう"),
        new("hold crossfire", "교차 사격 잡자", "クロスを組もう", true, null, "hold crossfire", "crossfire", "크로스 잡자", "クロス組もう"),
        new("need heal", "힐 필요", "ヒールお願い", true, null, "need heal", "need heals", "힐좀", "힐 좀", "ヒールお願い"),
        new("no ultimate", "궁 없음", "ウルトなし", true, null, "no ult", "궁 없어", "궁없", "ウルトない"),
        new("ultimate ready", "궁 준비됨", "ウルト使える", true, null, "ult ready", "궁 있어", "궁있", "ウルトある"),
        new("low HP", "체력 낮음", "ロー", true, null, "low hp", "로우", "ロー"),
        new("one shot", "딸피", "激ロー", true, null, "one-shot", "oneshot", "딸피", "개딸피", "激ロー", "ミリ"),
        new("force buy", "포스 바이", "フォースバイ", true, "VALORANT", "force buy", "포바", "포스 바이", "フォースバイ"),
        new("half buy", "하프 바이", "ハーフバイ", true, "VALORANT", "half buy", "하프 바이", "ハーフバイ"),
        new("save weapons", "무기 세이브", "武器セーブ", true, "VALORANT", "save weapons", "총 세이브", "무기 세이브"),
        new("play post-plant", "설치 후 시간 끌자", "設置後は時間を稼ごう", true, "VALORANT", "play post plant", "play post-plant", "설치 후 시간 끌자"),
        new("need a drop", "총 사줘", "武器買って", true, "VALORANT", "drop pls", "drop please", "need drop", "총좀", "총 사줘", "武器買って"),
        new("stick the defuse", "해체 끝까지 해", "解除しきって", true, "VALORANT", "stick it", "stick the defuse", "해체 끝까지 해")
    ];

    private static string NormalizeSlangKey(string value) => Regex.Replace(
        value.Trim().TrimEnd('.', ',', '!', '?', '。', '、', '！', '？').Trim().Replace('’', '\''),
        @"\s+", " ");

    private static bool TryTranslateSlangPhrase(string text, string target, AppSettings? settings, out string translated)
    {
        var key = NormalizeSlangKey(text);
        foreach (var phrase in SlangPhrases)
        {
            if (phrase.Supports(settings) && phrase.Aliases.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                translated = phrase.Translate(target);
                return true;
            }
        }
        translated = "";
        return false;
    }

    private static readonly IReadOnlyDictionary<string, Regex> TacticalSlangPatterns =
        new[] { "Auto", "VALORANT" }.ToDictionary(game => game, game => new Regex(
            string.Join("|", SlangPhrases.Where(phrase => phrase.Tactical && phrase.Supports(new AppSettings { Game = game }))
                .SelectMany(phrase => phrase.Aliases).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(alias => alias.Length).Select(TermPattern)),
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), StringComparer.OrdinalIgnoreCase);

    public bool ContainsTacticalSlang(string text, AppSettings settings) =>
        TacticalSlangPatterns.GetValueOrDefault(settings.Game, TacticalSlangPatterns["Auto"]).IsMatch(text);

    private bool TryTranslateSlangCallout(string text, string target, AppSettings settings, out string translated)
    {
        translated = "";
        var key = NormalizeSlangKey(text);
        if (SlangPhrases.Any(phrase => phrase.Tactical && phrase.Supports(settings) &&
                phrase.Aliases.Contains(key, StringComparer.OrdinalIgnoreCase)))
            return TryTranslateSlangPhrase(text, target, settings, out translated);

        // Match the entire sentence and a known target; unknown words, extra clauses and negation go to the model.
        var match = Regex.Match(key,
            @"^(?:(?<uncertain>maybe|probably|아마|たぶん|多分)\s+)?(?<subject>.+?)\s*(?<state>one[- ]?shot|low(?: hp)?|lit|cracked|knocked|딸피|개딸피|체력 낮음|실드 깸|다운|激ロー|ロー|ミリ|アーマー割った|ノック)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success) return false;
        var subject = match.Groups["subject"].Value.Trim();
        var character = CanonicalCharacterName(NormalizeNames(subject, settings), settings);
        var location = NormalizeCalloutLocation(subject, settings);
        if (character is null && !IsLikelyLocation(location, settings)) return false;
        var state = match.Groups["state"].Value.ToLowerInvariant();
        (string En, string Ko, string Jp) meaning = state switch
        {
            "lit" => ("damaged", "피해 입음", "ダメージあり"),
            "low" or "low hp" or "체력 낮음" or "ロー" => ("low HP", "체력 낮음", "ロー"),
            "one shot" or "one-shot" or "oneshot" or "딸피" or "개딸피" or "激ロー" or "ミリ" => ("one shot", "딸피", "激ロー"),
            _ => ("", "", "")
        };
        if (meaning.En.Length == 0) return false;
        var display = character ?? LocalizeCalloutLocation(location, target, settings);
        translated = $"{display} · {target switch { "KO" => meaning.Ko, "JP" => meaning.Jp, _ => meaning.En }}";
        if (match.Groups["uncertain"].Success)
            translated = (target switch { "KO" => "추정 · ", "JP" => "たぶん · ", _ => "maybe · " }) + translated;
        return true;
    }

    private static string TermPattern(string term) =>
        $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(term).Replace(@"\ ", @"\s+")}(?![\p{{L}}\p{{N}}_])";

    private static readonly IReadOnlyDictionary<string, (Regex Pattern, Dictionary<string, string> Values)> ExpansionRules =
        new[] { "Auto", "VALORANT" }.ToDictionary(game => game,
            game => BuildExpansionRules(new AppSettings { Game = game }), StringComparer.OrdinalIgnoreCase);

    private static (Regex Pattern, Dictionary<string, string> Values) BuildExpansionRules(AppSettings settings)
    {
        var expansions = FpsTerms.Where(pair => !ContextSensitiveTerms.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var phrase in SlangPhrases.Where(phrase => phrase.Supports(settings)))
            foreach (var alias in phrase.Aliases.Where(alias => alias.All(ch => ch < 128)))
                expansions[alias] = phrase.En;
        // "cracked" in an ordinary sentence can be praise. Only anchored callouts expand it.
        foreach (var term in ContextSensitiveTerms) expansions.Remove(term);
        foreach (var name in ProperNames.Values.Concat(settings.CustomGlossary.Values))
            expansions[name] = name;
        var pattern = string.Join("|", expansions.Keys.OrderByDescending(key => key.Length).Select(TermPattern));
        return (new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), expansions);
    }

    private static string ExpandSlangOnce(string text, AppSettings settings)
    {
        var rules = settings.CustomGlossary.Count > 0
            ? BuildExpansionRules(settings)
            : ExpansionRules.GetValueOrDefault(settings.Game, ExpansionRules["Auto"]);
        return rules.Pattern.Replace(text, match =>
            rules.Values.TryGetValue(match.Value, out var exact) ? exact :
            rules.Values.GetValueOrDefault(Regex.Replace(match.Value, @"\s+", " "), match.Value));
    }
}
