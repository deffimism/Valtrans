using System.Text.Json;
using System.Text.RegularExpressions;
using Valtrans.Models;

namespace Valtrans.Services;

public sealed partial class GlossaryService
{
    private sealed record TranslationTerm(string En, string Ko, string Jp, params string[] Aliases);

    // Terminology references, not whole-sentence replacement rules. They must never
    // discard an actor, negation or a clause to force a sentence into a fixed callout.
    private static readonly TranslationTerm[] TranslationTerms =
    [
        new("heal", "힐", "ヒール", "힐", "ヒール", "heal", "healing"),
        new("plant the spike", "스파이크 설치", "スパイク設置", "설치", "設置", "plant", "planting"),
        new("defuse the spike", "스파이크 해체", "スパイク解除", "해체", "解除", "defuse", "defusing"),
        new("cover", "엄호", "カバー", "엄호", "カバー", "cover"),
        new("smoke", "연막", "スモーク", "연막", "スモーク", "smoke"),
        new("flash", "섬광", "フラッシュ", "섬광", "플래시", "フラッシュ", "flash"),
        new("ultimate", "궁극기", "ウルト", "궁", "궁극기", "ウルト", "ult", "ultimate"),
        new("Operator", "오퍼레이터", "オペレーター", "오퍼", "オペ", "オペレーター", "Operator", "Op"),
        new("Vandal", "밴달", "ヴァンダル", "밴달", "ヴァンダル", "Vandal"),
        new("Phantom", "팬텀", "ファントム", "팬텀", "ファントム", "Phantom"),
        new("spike", "스파이크", "スパイク", "스파이크", "スパイク", "spike"),
        new("full buy", "풀바이", "フルバイ", "풀바이", "フルバイ", "full buy"),
        new("save weapons", "무기 세이브", "武器をセーブ", "세이브", "セーブ"),
        new("used their ultimate", "궁극기를 사용함", "ウルトを使った", "궁 빠졌어", "궁극기 빠졌어"),
        new("won", "이겼다", "勝った", "이겼네", "이겼어", "이겼다", "勝てた", "勝った"),
        new("can't play for long", "오래 플레이하지 못함", "長くプレイできない", "오래 못 해", "오래 못해", "오래못해"),
        new("go to sleep", "자러 감", "寝る", "하고 잘래", "하고 잘게"),
        new("match", "판", "試合", "판", "試合"),
        new("round", "라운드", "ラウンド", "라운드", "ラウンド", "round", "rounds"),
        new("wait a moment", "잠깐만", "ちょっと待って", "one second", "just a second", "잠깐만", "ちょっと待って"),
        new("leave the game", "게임에서 나가다", "ゲームを抜ける", "抜けた"),
        new("low HP", "딸피", "ロー", "딸피", "ロー", "激ロー", "low HP"),
        new("full HP", "체력 가득", "体力満タン", "풀피", "フルHP", "full HP", "full health"),
        new("one shot", "한 방", "ワンショット", "원탭", "원샷", "ワンショット", "one shot", "one-shot"),
        new("lag", "렉", "ラグ", "렉", "ラグ", "lag", "laggy"),
        new("trade kill", "킬 교환", "カバーキル", "트레이드", "トレード", "trade"),
        new("hold an angle", "각 잡기", "射線を見て待つ", "각 잡", "射線"),
        new("flank", "뒤돌기", "裏取り", "뒤돌기", "뒤돌고", "뒤도는", "裏取り", "flank", "flanking"),
        new("turn away from the flash", "섬광을 등져", "フラッシュから顔を背ける", "뒤돌아", "後ろ向いて", "turn away"),
        new("rotate", "로테", "ローテ", "로테", "ローテ", "rotate", "rotating"),
        new("retake", "리테이크", "リテイク", "리테이크", "リテイク", "retake", "retaking"),
        new("nice try", "좋은 시도였어", "ナイストライ", "nt", "nice try", "ナイストライ"),
        new("my bad", "내 실수야", "自分のミス", "mb", "my bad"),
        new("sarcasm", "비꼬는 말", "皮肉", "sarcasm", "皮肉", "비꼬는 말", "빈정거림", "비꼰"),
        new("doing this together", "같이 해줘서", "一緒にやってくれて", "같이 해줘서", "함께 해줘서"),
        new("swearing", "욕설", "暴言", "swearing", "욕설", "暴言"),
        new("push forward", "진입", "プッシュ", "푸시", "プッシュ", "push"),
        new("peek", "피킹", "ピーク", "피킹", "ピーク", "peek", "peeking")
    ];

    public string BuildTranslationTerminology(string source, string target, AppSettings settings)
    {
        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var locations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var contextNotes = new List<string>();
        bool Matches(string term)
        {
            if (term == "풀피")
                return Regex.IsMatch(source, @"(?<![가-힣])풀피(?=$|[\s,.!?]|야|다|임|인|는|가|고|로|에|면|였|여|일|도|만|라도)");
            if (term is "one second" or "just a second")
                return Regex.IsMatch(source, $@"(?i)^\s*{Regex.Escape(term)}\s*(?:[,!.]|$)");
            if (term == "抜けた")
                return Regex.IsMatch(source, @"回線|ゲーム|試合|マッチ|チーム") &&
                    !Regex.IsMatch(source, @"歯|髪|毛|針");
            // Chat abbreviations are hints at clause starts, not arbitrary byte
            // units or parts of names such as Windows NT. Never rewrite the text.
            if (term is "nt" or "mb")
                return Regex.IsMatch(source, $@"(?i)(?:^|[,;.!?]\s*){term}(?=$|[\s,;.!?])") &&
                    !(term == "mb" && Regex.IsMatch(source, @"(?i)\bmb\s*(?:/\s*s\b|per\s+second\b|of\s+(?:ram|memory|data)\b)|\b\d+(?:\.\d+)?\s*mb\b"));
            if (term is "retake" or "retaking")
                if (Regex.IsMatch(source, @"(?i)\b(?:exam|test|photo|photograph|picture|class|course|driving|video)\b")) return false;
            if (term == "swearing")
                if (!Regex.IsMatch(source, @"(?i)\b(?:chat|toxic|insult|cursing)\b|\b(?:stop|quit|no)\s+swearing\b") ||
                    Regex.IsMatch(source, @"(?i)\b(?:oath|court|solemn|promise)\b|swearing-in")) return false;
            if (term == "판")
                return (source.Contains("라운드", StringComparison.Ordinal) &&
                    Regex.IsMatch(source, @"(?<![가-힣])(?:한|두|세|네|\d+)\s*판(?:이|은|을|만)?\s*(?:아니라|말고)")) ||
                    new GameChatFilterService(this).Categorize(source, settings).Category != "Tactical" &&
                    !Regex.IsMatch(source, @"풀\s*바이|세이브|에코|이코|바이|라운드") &&
                    Regex.IsMatch(source, @"(?<![가-힣])(?:이번|다음|지난|한|두|세|네|다섯|\d+)\s*판(?:만|은|을|이|에|도|째)?(?=$|\s|[,.!?])");
            // ロー must not match ローテ; 궁 must not match 궁금하다.
            var kana = term.All(ch => ch is >= '\u30a0' and <= '\u30ff');
            var left = kana ? "[A-Za-z0-9_\\u30a0-\\u30ff]" : "[A-Za-z0-9_]";
            var right = kana ? "[A-Za-z0-9_\\u30a0-\\u30ff]" : "[A-Za-z0-9_]";
            var suffix = term == "궁" ? @"(?=$|\s|[.,!?]|극기|은|이|을|도|만|없|있|빠|썼|써)" : "";
            return Regex.IsMatch(source, $@"(?<!{left}){Regex.Escape(term)}(?!{right}){suffix}",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        foreach (var term in TranslationTerms)
        {
            foreach (var alias in term.Aliases.Where(Matches))
                entries.TryAdd(alias, target switch { "KO" => term.Ko, "JP" => term.Jp, _ => term.En });
            if (term.En == "one shot" && term.Aliases.Any(Matches))
                contextNotes.Add("Context for one shot: A player who is one shot is vulnerable to one more hit; to one-shot a player means kill them in one hit. Choose according to the source grammar and preserve negation and tense.");
        }
        // Keep names, locations and user entries from the existing relevant-only lookup.
        var spatialSource = TranslationFactGuard.WithoutNonSpatialDirections(source);
        foreach (var line in BuildRelevantPromptGlossary(source, target, settings).Split('\n'))
        {
            if (!line.StartsWith('{')) continue;
            using var json = JsonDocument.Parse(line);
            var term = json.RootElement.GetProperty("term").GetString()!;
            var value = json.RootElement.GetProperty("meaning").GetString()!;
            var kind = json.RootElement.GetProperty("kind").GetString();
            if (kind == "ambiguous word" && term.Equals("low", StringComparison.OrdinalIgnoreCase))
            {
                // A contextual definition is not a literal translation pair.
                contextNotes.Add($"Context for {JsonSerializer.Serialize(term, PromptJson)}: {value}");
                continue;
            }
            if (kind?.StartsWith("possible location") == true &&
                Regex.IsMatch(term, @"(?i)^(?:뒤|앞|left|right|back|front|behind|前|後ろ)$") &&
                !spatialSource.Contains(term, StringComparison.OrdinalIgnoreCase)) continue;
            if (kind?.StartsWith("possible location") == true &&
                Regex.IsMatch(source, @"(?i)\b(?:for|too|how|so)\s+long\b|\blong\s+(?:time|enough|story)\b") &&
                term.Equals("long", StringComparison.OrdinalIgnoreCase)) continue;
            if (kind == "user terminology") { entries[term] = value; locations.Remove(term); }
            else if (kind is "proper name" or "proper name; keep spelling" ||
                     kind?.StartsWith("possible location") == true)
            {
                entries.TryAdd(term, value);
                if (kind?.StartsWith("possible location") == true) locations.Add(term);
            }
        }
        return string.Join('\n', entries.Take(20).Select(pair =>
            (locations.Contains(pair.Key) ? "Map location: " : "") +
            $"{JsonSerializer.Serialize(pair.Key, PromptJson)} translates to {JsonSerializer.Serialize(pair.Value, PromptJson)}").Concat(contextNotes));
    }
}
