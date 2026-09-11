using System.Text;
using System.Text.RegularExpressions;
using Valtrans.Models;

namespace Valtrans.Services;

public sealed partial class GlossaryService
{
    static GlossaryService()
    {
        // Callout recognition evaluates roughly a hundred distinct patterns through the
        // static Regex helpers, which share an LRU cache that holds 15 by default. Every
        // OCR line walks that set several times over (accept gate, chat filter, classifier,
        // candidate scoring, translation), so the cache thrashed and re-parsed almost every
        // pattern on every call: 0.86 ms per TryTranslateStructuredCallout, down to 0.10 ms
        // once the whole set fits. Never shrink a cache another component already grew.
        Regex.CacheSize = Math.Max(Regex.CacheSize, 256);
    }

    private static readonly Dictionary<string, string> CommonLocations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["헤븐"] = "Heaven", ["ヘブン"] = "Heaven", ["天堂"] = "Heaven", ["地狱"] = "Hell", ["헬"] = "Hell", ["ヘル"] = "Hell",
        ["메인"] = "Main", ["メイン"] = "Main", ["링크"] = "Link", ["リンク"] = "Link",
        ["숏"] = "Short", ["ショート"] = "Short", ["롱"] = "Long", ["ロング"] = "Long",
        ["백사이트"] = "Back Site", ["バックサイト"] = "Back Site", ["사이트 안"] = "Site",
        ["수비 스폰"] = "Defender Spawn", ["디펜더 스폰"] = "Defender Spawn", ["防衛スポーン"] = "Defender Spawn",
        ["공격 스폰"] = "Attacker Spawn", ["アタッカースポーン"] = "Attacker Spawn",
        ["미드"] = "Mid", ["ミッド"] = "Mid", ["중앙"] = "Mid", ["中央"] = "Mid",
        ["왼쪽"] = "Left", ["좌측"] = "Left", ["左"] = "Left", ["오른쪽"] = "Right", ["우측"] = "Right", ["右"] = "Right",
        ["앞"] = "Front", ["전방"] = "Front", ["前"] = "Front", ["뒤"] = "Back", ["후방"] = "Back", ["後ろ"] = "Back",
        ["큐비"] = "Cubby", ["キュービー"] = "Cubby", ["라프터"] = "Rafters", ["ラフター"] = "Rafters",
        ["램프"] = "Ramp", ["ランプ"] = "Ramp", ["엘보"] = "Elbow", ["エルボー"] = "Elbow",
        ["터널"] = "Tunnel", ["トンネル"] = "Tunnel", ["브리지"] = "Bridge", ["ブリッジ"] = "Bridge",
        ["타워"] = "Tower", ["タワー"] = "Tower", ["로프"] = "Ropes", ["ロープ"] = "Ropes",
        ["벤트"] = "Vents", ["ベント"] = "Vents", ["윈도우"] = "Window", ["ウィンドウ"] = "Window",
        ["도어"] = "Door", ["ドア"] = "Door", ["네스트"] = "Nest", ["ネスト"] = "Nest",
        ["언더"] = "Under", ["アンダー"] = "Under",
        ["고지대"] = "High Ground", ["高所"] = "High Ground", ["초크"] = "Choke", ["チョーク"] = "Choke",
        ["가든"] = "Garden", ["ガーデン"] = "Garden", ["마켓"] = "Market", ["マーケット"] = "Market",
        ["보일러"] = "Boiler", ["ボイラー"] = "Boiler", ["갈매기"] = "Seagull", ["カモメ"] = "Seagull",
        ["캐트워크"] = "Catwalk", ["キャットウォーク"] = "Catwalk", ["스크린"] = "Screens", ["スクリーン"] = "Screens",
        ["파이프"] = "Pipes", ["パイプ"] = "Pipes", ["커비홀"] = "Cubby", ["야드"] = "Yard", ["ヤード"] = "Yard"
    };

    private static readonly Dictionary<string, string> FpsTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["nt"] = "nice try",
        ["nice try"] = "nice try",
        ["nj"] = "nice job",
        ["ns"] = "nice shot",
        ["mb"] = "my bad",
        ["my bad"] = "my bad",
        ["gg"] = "good game",
        ["wp"] = "well played",
        ["glhf"] = "good luck, have fun",
        ["afk"] = "away from keyboard",
        ["brb"] = "be right back",
        ["igl"] = "in-game leader",
        ["ult"] = "ultimate ability",
        ["ulti"] = "ultimate ability",
        ["mid"] = "mid",
        ["top"] = "top",
        ["bot"] = "bottom",
        ["site"] = "site",
        ["rotate"] = "rotate",
        ["flank"] = "flank",
        ["lurker"] = "lurker",
        ["lurking"] = "lurking",
        ["entry"] = "entry player",
        ["stack"] = "stack players",
        ["default"] = "default position",
        ["one tap"] = "one tap",
        ["one shot"] = "very low HP",
        ["low"] = "low HP",
        ["lit"] = "damaged",
        ["eco"] = "economy round",
        ["force"] = "force buy",
        ["full buy"] = "full buy round",
        ["save"] = "save weapons",
        ["rush"] = "rush",
        ["push"] = "push",
        ["hold"] = "hold position",
        ["peek"] = "peek",
        ["dinked"] = "hit in the head",
        ["whiff"] = "missed shots",
        ["ace"] = "ace",
        ["clutch"] = "clutch",
        ["frag"] = "kill",
        ["op"] = "Operator sniper rifle",
        ["util"] = "utility abilities",
        ["flash"] = "flash ability",
        ["smoke"] = "smoke ability",
        ["molly"] = "damage-over-time area ability",
        ["nade"] = "grenade",
        ["plant"] = "plant the spike",
        ["defuse"] = "defuse the spike",
        ["tap"] = "tap the spike",
        ["fake"] = "fake the action",
        ["retake"] = "retake the site",
        ["heaven"] = "heaven position",
        ["hell"] = "hell position",
        ["short"] = "short route",
        ["long"] = "long route",
        ["ct"] = "defender spawn",
        ["spawn"] = "spawn",
        ["refrag"] = "trade the kill",
        ["trade"] = "trade the kill",
        ["rez"] = "resurrect",
        ["res"] = "resurrect",
        ["beam"] = "deal heavy continuous damage",
        ["orb"] = "ultimate orb",
        ["lineup"] = "pre-aimed utility throw",
        ["one way"] = "one-way smoke",
        ["oneway"] = "one-way smoke",
        ["dry peek"] = "peek without utility",
        ["pop flash"] = "quick flash around a corner",
        ["wallbang"] = "shoot through a wall",
        ["crossfire"] = "two angles covering one spot",
        ["off angle"] = "unexpected shooting angle",
        ["half buy"] = "half buy round",
        ["anti eco"] = "round against a low-buy enemy"
        // Deliberately absent: "spike", "bonus", "spam". These substitute inside ordinary
        // sentences ("bonus damage", "plant the spike"), and plant/defuse/tap already
        // establish the spike for the model.
    };

    private static readonly Dictionary<string, (string En, string Ko, string Jp)> CommonChatPhrases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["안녕"] = ("hello", "안녕", "こんにちは"),
            ["안녕하세요"] = ("hello", "안녕", "こんにちは"),
            ["반가워"] = ("nice to meet you", "반가워", "よろしく"),
            ["고마워"] = ("thanks", "고마워", "ありがとう"),
            ["감사"] = ("thanks", "고마워", "ありがとう"),
            ["감사합니다"] = ("thanks", "고마워", "ありがとう"),
            ["ㄳ"] = ("thanks", "고마워", "ありがとう"),
            ["미안"] = ("sorry", "미안", "ごめん"),
            ["미안해"] = ("sorry", "미안", "ごめん"),
            ["내 실수"] = ("my bad", "내 실수", "ごめん、ミス"),
            ["내 실수야"] = ("my bad", "내 실수", "ごめん、ミス"),
            ["나의 실수"] = ("my bad", "내 실수", "ごめん、ミス"),
            ["죄송"] = ("sorry", "미안", "ごめん"),
            ["죄송합니다"] = ("sorry", "미안", "ごめん"),
            ["ㅈㅅ"] = ("sorry", "미안", "ごめん"),
            ["알겠어"] = ("copy", "확인", "了解"),
            ["알겠습니다"] = ("copy", "확인", "了解"),
            ["확인"] = ("copy", "확인", "了解"),
            ["ㅇㅋ"] = ("ok", "확인", "了解"),
            ["괜찮아"] = ("all good", "괜찮아", "大丈夫"),
            ["수고"] = ("gg", "수고", "お疲れ"),
            ["수고했어"] = ("gg", "수고", "お疲れ"),
            ["도와줘"] = ("help", "도와줘", "助けて"),
            ["기다려"] = ("wait", "기다려", "待って"),
            ["가자"] = ("let's go", "가자", "行こう"),
            ["こんにちは"] = ("hello", "안녕", "こんにちは"),
            ["こんばんは"] = ("hello", "안녕", "こんばんは"),
            ["おはよう"] = ("hello", "안녕", "おはよう"),
            ["ありがとう"] = ("thanks", "고마워", "ありがとう"),
            ["ありがとうございます"] = ("thanks", "고마워", "ありがとう"),
            ["ごめん"] = ("sorry", "미안", "ごめん"),
            ["ごめんなさい"] = ("sorry", "미안", "ごめん"),
            ["すみません"] = ("sorry", "미안", "ごめん"),
            ["了解"] = ("copy", "확인", "了解"),
            ["わかった"] = ("copy", "확인", "了解"),
            ["わかりました"] = ("copy", "확인", "了解"),
            ["大丈夫"] = ("all good", "괜찮아", "大丈夫"),
            ["お疲れ"] = ("gg", "수고", "お疲れ"),
            ["お疲れ様"] = ("gg", "수고", "お疲れ"),
            ["よろしく"] = ("let's have a good game", "잘 부탁해", "よろしく"),
            ["よろしくお願いします"] = ("let's have a good game", "잘 부탁해", "よろしくお願いします"),
            ["よろしくおねがいします"] = ("let's have a good game", "잘 부탁해", "よろしくお願いします"),
            ["どうぞよろしくお願いします"] = ("let's have a good game", "잘 부탁해", "よろしくお願いします"),
            ["どうぞよろしくおねがいします"] = ("let's have a good game", "잘 부탁해", "よろしくお願いします"),
            ["助けて"] = ("help", "도와줘", "助けて"),
            ["待って"] = ("wait", "기다려", "待って"),
            ["行こう"] = ("let's go", "가자", "行こう"),
            ["hello"] = ("hello", "안녕", "こんにちは"),
            ["hi"] = ("hi", "안녕", "こんにちは"),
            ["hey"] = ("hey", "안녕", "やあ"),
            ["thanks"] = ("thanks", "고마워", "ありがとう"),
            ["thank you"] = ("thanks", "고마워", "ありがとう"),
            ["thx"] = ("thanks", "고마워", "ありがとう"),
            ["ty"] = ("thanks", "고마워", "ありがとう"),
            ["sorry"] = ("sorry", "미안", "ごめん"),
            ["understood"] = ("copy", "확인", "了解"),
            ["got it"] = ("copy", "확인", "了解"),
            ["okay"] = ("ok", "확인", "了解"),
            ["all good"] = ("all good", "괜찮아", "大丈夫"),
            ["help"] = ("help", "도와줘", "助けて"),
            ["wait"] = ("wait", "기다려", "待って"),
            ["let's go"] = ("let's go", "가자", "行こう")
            ,
            ["딸피"] = ("one shot", "딸피", "激ロー"),
            ["원샷"] = ("one shot", "딸피", "激ロー"),
            ["한 대"] = ("one shot", "딸피", "激ロー"),
            ["피 없어"] = ("low HP", "딸피", "激ロー"),
            ["실드 깸"] = ("cracked", "실드 깸", "アーマー割った"),
            ["갑옷 깸"] = ("cracked", "실드 깸", "アーマー割った"),
            ["다운"] = ("knocked", "다운", "ノック"),
            ["눕힘"] = ("knocked", "다운", "ノック"),
            ["뒤 조심"] = ("watch flank", "뒤 조심", "裏警戒"),
            ["플랭크"] = ("flank", "플랭크", "裏取り"),
            ["로테"] = ("rotate", "로테", "ローテ"),
            ["로테 중"] = ("rotating", "로테 중", "ローテ中"),
            ["밀자"] = ("push", "밀자", "プッシュ"),
            ["설치 중"] = ("planting", "설치 중", "設置中"),
            ["해체 중"] = ("defusing", "해체 중", "解除中"),
            ["세이브"] = ("save", "세이브", "セーブ"),
            ["에코"] = ("eco", "에코", "エコ"),
            ["플래시 줄게"] = ("flashing", "플래시", "フラッシュ入れる"),
            ["연막 줄게"] = ("smoking", "연막", "スモーク入れる"),
            ["힐 줘"] = ("heal me", "힐 줘", "ヒールお願い"),
            ["살려줘"] = ("revive me", "살려줘", "蘇生お願い"),
            ["激ロー"] = ("one shot", "딸피", "激ロー"),
            ["ロー"] = ("low HP", "딸피", "ロー"),
            ["ワンショット"] = ("one shot", "딸피", "激ロー"),
            ["アーマー割った"] = ("cracked", "실드 깸", "アーマー割った"),
            ["割った"] = ("cracked", "실드 깸", "割った"),
            ["ノック"] = ("knocked", "다운", "ノック"),
            ["裏警戒"] = ("watch flank", "뒤 조심", "裏警戒"),
            ["裏取り"] = ("flank", "플랭크", "裏取り"),
            ["ローテ"] = ("rotate", "로테", "ローテ"),
            ["ローテ中"] = ("rotating", "로테 중", "ローテ中"),
            ["プッシュ"] = ("push", "밀자", "プッシュ"),
            ["設置中"] = ("planting", "설치 중", "設置中"),
            ["解除中"] = ("defusing", "해체 중", "解除中"),
            ["セーブ"] = ("save", "세이브", "セーブ"),
            ["エコ"] = ("eco", "에코", "エコ"),
            ["フラッシュ入れる"] = ("flashing", "플래시", "フラッシュ入れる"),
            ["スモーク入れる"] = ("smoking", "연막", "スモーク入れる"),
            ["ヒールお願い"] = ("heal me", "힐 줘", "ヒールお願い"),
            ["蘇生お願い"] = ("revive me", "살려줘", "蘇生お願い"),
            ["one shot"] = ("one shot", "딸피", "激ロー"),
            ["1 hp"] = ("one shot", "딸피", "激ロー"),
            ["1hp"] = ("one shot", "딸피", "激ロー"),
            ["low hp"] = ("low HP", "딸피", "ロー"),
            ["cracked"] = ("cracked", "실드 깸", "アーマー割った"),
            ["shield broken"] = ("cracked", "실드 깸", "アーマー割った"),
            ["knocked"] = ("knocked", "다운", "ノック"),
            ["watch flank"] = ("watch flank", "뒤 조심", "裏警戒"),
            ["flank"] = ("flank", "플랭크", "裏取り"),
            ["rotate"] = ("rotate", "로테", "ローテ"),
            ["rotating"] = ("rotating", "로테 중", "ローテ中"),
            ["push"] = ("push", "밀자", "プッシュ"),
            ["planting"] = ("planting", "설치 중", "設置中"),
            ["defusing"] = ("defusing", "해체 중", "解除中"),
            ["save"] = ("save", "세이브", "セーブ"),
            ["eco"] = ("eco", "에코", "エコ"),
            ["flashing"] = ("flashing", "플래시", "フラッシュ入れる"),
            ["smoking"] = ("smoking", "연막", "スモーク入れる"),
            ["heal me"] = ("heal me", "힐 줘", "ヒールお願い"),
            ["revive me"] = ("revive me", "살려줘", "蘇生お願い"),
            ["rez me"] = ("revive me", "살려줘", "蘇生お願い")
        };

    // Official English character names (Riot/EA) and map names (Riot map directory).
    private static readonly Dictionary<string, string> ProperNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // Map names are references only, never a selected map or tactical context.
        // https://playvalorant.com/ko-kr/maps/ and /ja-jp/maps/ (2026-09-08).
        ["어센트"] = "Ascent", ["アセント"] = "Ascent",
        ["바인드"] = "Bind", ["バインド"] = "Bind",
        ["헤이븐"] = "Haven", ["ヘイヴン"] = "Haven",
        ["스플릿"] = "Split", ["スプリット"] = "Split",
        ["아이스박스"] = "Icebox", ["브리즈"] = "Breeze", ["프랙처"] = "Fracture",
        ["펄"] = "Pearl", ["로터스"] = "Lotus", ["선셋"] = "Sunset",
        ["어비스"] = "Abyss", ["코로드"] = "Corrode", ["서밋"] = "Summit",

        // VALORANT
        ["아스트라"] = "Astra", ["브리치"] = "Breach", ["브림"] = "Brimstone", ["브림스톤"] = "Brimstone",
        ["체임버"] = "Chamber", ["클로브"] = "Clove", ["사이퍼"] = "Cypher", ["데드록"] = "Deadlock",
        ["페이드"] = "Fade", ["게코"] = "Gekko", ["하버"] = "Harbor", ["아이소"] = "Iso", ["제트"] = "Jett",
        ["케이오"] = "KAY/O", ["킬조이"] = "Killjoy", ["믹스"] = "Miks", ["네온"] = "Neon", ["오멘"] = "Omen",
        ["피닉스"] = "Phoenix", ["레이즈"] = "Raze", ["레이나"] = "Reyna", ["세이지"] = "Sage", ["스카이"] = "Skye",
        ["소바"] = "Sova", ["테호"] = "Tejo", ["베토"] = "Veto", ["바이퍼"] = "Viper", ["바이스"] = "Vyse",
        ["웨이레이"] = "Waylay", ["요루"] = "Yoru",
        ["アストラ"] = "Astra", ["ブリーチ"] = "Breach", ["ブリムストーン"] = "Brimstone", ["チェンバー"] = "Chamber",
        ["クローヴ"] = "Clove", ["サイファー"] = "Cypher", ["デッドロック"] = "Deadlock", ["フェイド"] = "Fade",
        ["ゲッコー"] = "Gekko", ["ハーバー"] = "Harbor", ["アイソ"] = "Iso", ["ジェット"] = "Jett",
        ["キルジョイ"] = "Killjoy", ["ネオン"] = "Neon", ["オーメン"] = "Omen", ["フェニックス"] = "Phoenix",
        ["レイズ"] = "Raze", ["レイナ"] = "Reyna", ["セージ"] = "Sage", ["スカイ"] = "Skye", ["ソーヴァ"] = "Sova",
        ["ヴァイパー"] = "Viper", ["ヨル"] = "Yoru"
    };

    public string NormalizeNames(string text, AppSettings settings)
    {
        foreach (var pair in ProperNames.Concat(settings.CustomGlossary).OrderByDescending(x => x.Key.Length))
        {
            text = Regex.Replace(text, Regex.Escape(pair.Key), pair.Value, RegexOptions.IgnoreCase);
        }
        return text;
    }

    internal bool IsStandaloneCanonicalReference(string text, AppSettings settings)
    {
        var value = text.Trim().Trim('.', '!', '?', '。', '！', '？');
        return ProperNames.Values.Concat(SelectedLocations(settings).Values)
            .Concat(settings.CustomGlossary.Values)
            .Any(term => value.Equals(term, StringComparison.OrdinalIgnoreCase));
    }

    public bool ContainsKnownGameReference(string text, AppSettings settings)
    {
        text = OcrNoiseHeuristics.SplitRunTogetherCallout(
            ChatTextSanitizer.ContentForLanguageDetection(text));
        var terms = SelectedLocations(settings).Keys
            .Concat(SelectedLocations(settings).Values)
            .Where(term => term.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var term in terms)
        {
            if (term.Any(ch => ch > 127))
            {
                if (text.Contains(term, StringComparison.OrdinalIgnoreCase)) return true;
                continue;
            }
            if (Regex.IsMatch(text, $@"(?<![A-Za-z0-9_]){Regex.Escape(term)}(?![A-Za-z0-9_])",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return true;
        }
        return false;
    }

    public string NormalizeLocations(string text, AppSettings settings)
    {
        var locations = SelectedLocations(settings);
        foreach (var pair in locations.OrderByDescending(pair => pair.Key.Length))
            text = Regex.Replace(text, Regex.Escape(pair.Key), pair.Value, RegexOptions.IgnoreCase);
        return text;
    }

    public string PrepareForLocalTranslation(string text, AppSettings settings)
    {
        text = NormalizeNames(text, settings);
        text = NormalizeLocations(text, settings);
        return ExpandSlangOnce(text, settings);
    }

    public ProtectedTranslationText ProtectTranslationFacts(string text, AppSettings settings, string targetLanguage)
    {
        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        string AddToken(string value)
        {
            var token = $"{{{{VT{index++}}}}}";
            tokens[token] = value;
            return token;
        }

        string ProtectFacts(string value, IEnumerable<(string Match, string Restore)> facts)
        {
            foreach (var fact in facts
                         .Where(fact => !string.IsNullOrWhiteSpace(fact.Match))
                         .DistinctBy(fact => fact.Match, StringComparer.OrdinalIgnoreCase)
                         .OrderByDescending(fact => fact.Match.Length))
            {
                var pattern = fact.Match.All(ch => ch <= 127)
                    ? $@"(?<![A-Za-z0-9_]){Regex.Escape(fact.Match)}(?![A-Za-z0-9_])"
                    : Regex.Escape(fact.Match);
                value = Regex.Replace(value, pattern, _ => AddToken(fact.Restore),
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            return value;
        }

        // Character names stay in their official English spelling. Location names are restored in the
        // output language so a protected "Mid" does not leak into a Korean/Japanese callout.
        text = ProtectFacts(text, ProperNames.Values.Select(name => (name, name)));
        text = ProtectFacts(text, SelectedLocations(settings).Values.Select(location =>
            (location, LocalizeCalloutLocation(location, targetLanguage, settings))));
        text = ProtectFacts(text, settings.CustomGlossary.Values.Select(value => (value, value)));

        text = Regex.Replace(text, @"(?<![A-Za-z])(?:one|two|three|four)(?![A-Za-z])",
            match => AddToken(NormalizeCount(match.Value)), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        text = Regex.Replace(text, @"(?<![가-힣])(?:한|두|둘|세|셋|네|넷)(?=\s*(?:명|팀|개|발|대))",
            match => AddToken(NormalizeCount(match.Value)), RegexOptions.CultureInvariant);
        text = Regex.Replace(text, @"(?<![一-龯])(?:一|二|三|四)(?=\s*人)",
            match => AddToken(NormalizeCount(match.Value)), RegexOptions.CultureInvariant);
        text = Regex.Replace(text, @"(?<![A-Za-z_])\d+(?:[./]\d+)?(?![A-Za-z_])",
            match => AddToken(match.Value), RegexOptions.CultureInvariant);
        text = Regex.Replace(text, @"(?<![A-Za-z0-9_])[ABC](?![A-Za-z0-9_])",
            match => AddToken(match.Value), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return new ProtectedTranslationText(text, tokens);
    }

    public static string RestoreTranslationFacts(string text, ProtectedTranslationText protectedText)
    {
        foreach (var pair in protectedText.Tokens)
        {
            var tokenBody = pair.Key.Trim('{', '}');
            text = Regex.Replace(text,
                $@"(?:\{{\{{\s*{Regex.Escape(tokenBody)}\s*\}}\}}|(?<![A-Za-z0-9_]){Regex.Escape(tokenBody)}(?![A-Za-z0-9_]))",
                _ => pair.Value,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        return text;
    }

    public string BuildPromptGlossary(AppSettings settings)
    {
        var builder = new StringBuilder();
        foreach (var pair in FpsTerms.Where(pair => !ContextSensitiveTerms.Contains(pair.Key)))
            builder.Append(pair.Key).Append('=').Append(pair.Value).Append("; ");
        builder.Append("Ambiguous slang: infer only from this sentence. 'save me' means help/revive me; 'you are cracked' is praise for sharp aim in VALORANT. Never assume low HP means exactly 1 HP. Preserve who acted, negation and uncertainty. ");
        builder.Append("Common location vocabulary: ");
        foreach (var pair in SelectedLocations(settings)) builder.Append(pair.Key).Append('=').Append(pair.Value).Append("; ");
        if (settings.CustomGlossary.Count > 0)
        {
            builder.Append("Custom: ");
            foreach (var pair in settings.CustomGlossary) builder.Append(pair.Key).Append('=').Append(pair.Value).Append("; ");
        }
        return builder.ToString();
    }

    // No selected-map overrides: the same unambiguous common vocabulary is used everywhere.
    private static Dictionary<string, string> SelectedLocations(AppSettings settings) =>
        new(CommonLocations, StringComparer.OrdinalIgnoreCase);

    public bool TryTranslateExactShortcut(string text, string target, out string translated, AppSettings? settings = null)
    {
        if (TryTranslateSlangPhrase(text, target, settings, out translated)) return true;
        var key = text.Trim().TrimEnd('.', ',', '!', '?', '。', '、', '！', '？').Trim();
        if (CommonChatPhrases.TryGetValue(key, out var phrase))
        {
            translated = target switch { "KO" => phrase.Ko, "JP" => phrase.Jp, _ => phrase.En };
            return true;
        }
        if (!FpsTerms.TryGetValue(key, out var expanded))
        {
            translated = "";
            return false;
        }

        translated = (key.ToLowerInvariant(), target) switch
        {
            ("nt", "KO") => "아깝다",
            ("nice try", "KO") => "아깝다",
            ("nt", "JP") => "惜しい",
            ("nice try", "JP") => "惜しい",
            ("nt", "EN") => "nice try",
            ("nice try", "EN") => "nice try",
            ("mb", "KO") => "내 실수",
            ("my bad", "KO") => "내 실수",
            ("mb", "JP") => "ごめん、ミスした",
            ("my bad", "JP") => "ごめん、ミス",
            ("mb", "EN") => "my bad",
            ("my bad", "EN") => "my bad",
            ("gg", "KO") => "좋은 게임",
            ("gg", "JP") => "いい試合だった",
            ("gg", "EN") => "good game",
            ("ns", "KO") => "나이스 샷",
            ("ns", "JP") => "ナイスショット",
            ("ns", "EN") => "nice shot",
            _ when target == "EN" => expanded,
            _ => ""
        };
        return translated.Length > 0;
    }

    public bool TryTranslateStructuredCallout(string text, string target, AppSettings settings, out string translated)
    {
        text = OcrNoiseHeuristics.SplitRunTogetherCallout(
            ChatTextSanitizer.ContentForLanguageDetection(text).Trim());
        translated = "";
        if (text.Length == 0) return false;
        if (TryTranslateChineseTacticalBriefing(text, target, settings, out translated)) return true;
        if (TryTranslateSiteActionBriefing(text, target, out translated)) return true;
        if (TryTranslateExactDirectionalBriefing(text, target, out translated)) return true;
        if (TryTranslateWatchDirection(text, target, out translated)) return true;
        if (TryTranslateMovementProhibition(text, target, settings, out translated)) return true;
        if (TryTranslateEnemyPresenceBriefing(text, target, settings, out translated)) return true;
        if (TryTranslateFlashWait(text, target, out translated)) return true;
        if (TryTranslateSlangCallout(text, target, settings, out translated)) return true;
        if (IsSiteActionCallout(text)) return false;

        var noEnemyPatterns = new[]
        {
            @"^(?:no\s+(?:one|enemy|enemies)|nobody|none)(?:\s+(?:is|are))?(?:\s+(?:at|in|on))?(?:\s+the)?\s+(?<location>.+?)[.!]?$",
            @"^(?<location>.+?)(?:에|에서)?\s*(?:적|사람|아무도)?\s*(?:없어|없음|없다)[.!]?$",
            @"^(?<location>.+?)(?:に|で)?\s*(?:敵(?:は|が)?|誰も)?\s*(?:いない|なし)[。.!]?$"
        };
        foreach (var pattern in noEnemyPatterns)
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success) continue;
            var location = NormalizeCalloutLocation(match.Groups["location"].Value, settings);
            if (!IsLikelyLocation(location, settings)) continue;
            var displayLocation = LocalizeCalloutLocation(location, target);
            translated = target switch
            {
                "KO" => $"{displayLocation} · 없음",
                "JP" => $"{displayLocation} · なし",
                _ => $"{displayLocation} · none"
            };
            return true;
        }

        var noRotate = Regex.Match(text,
            @"^(?:(?:don['’]?t|do\s+not|no)\s+(?:rotate|rotating)|(?:로테|이동|돌)(?:하지\s*마|금지|지\s*마)|(?:ローテ|移動)(?:しないで|するな|禁止))\s*[。.!]?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (noRotate.Success)
        {
            translated = target switch { "KO" => "로테 금지", "JP" => "ローテ禁止", _ => "don't rotate" };
            return true;
        }

        var warningPatterns = new[]
        {
            @"^(?<location>.+?)\s*(?:조심(?:\s*해(?:요|하세요)?|하세요)?|경계(?:\s*해(?:요|하세요)?|하세요)?|봐\s*줘)[.!]?$",
            @"^(?<location>.+?)\s*(?:注意|警戒|気をつけて)[。.!]?$",
            @"^(?:watch|check|careful(?:\s+(?:of|on))?)\s+(?<location>.+?)[.!]?$",
            @"^(?<location>.+?)\s+(?:careful|watch\s*out)[.!]?$"
        };
        foreach (var pattern in warningPatterns)
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success) continue;
            var location = NormalizeCalloutLocation(match.Groups["location"].Value, settings);
            if (!IsLikelyLocation(location, settings)) continue;
            var displayLocation = LocalizeCalloutLocation(location, target);
            translated = target switch
            {
                "KO" => $"{displayLocation} 조심",
                "JP" => $"{displayLocation}警戒",
                _ => $"watch {displayLocation.ToLowerInvariant()}"
            };
            return true;
        }

        var uncertainLocation = Regex.Match(text,
            @"^(?:maybe|probably|perhaps|아마|추정|たぶん|多分)\s+(?<location>[\p{L}\p{N}\s'-]{1,30})(?:\s*かも)?[。.!]?$|^(?<location>[\p{L}\p{N}\s'-]{1,30})\s+(?:かも|일\s*수도)[。.!]?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (uncertainLocation.Success)
        {
            var location = NormalizeCalloutLocation(uncertainLocation.Groups["location"].Value, settings);
            if (IsLikelyLocation(location, settings))
            {
                translated = target switch
                {
                    "KO" => $"추정 · {location}",
                    "JP" => $"たぶん · {location}",
                    _ => $"maybe · {location}"
                };
                return true;
            }
        }

        var normalizedCharacterText = NormalizeNames(text, settings);
        var damagePatterns = new[]
        {
            @"^(?<damage>\d{2,3})\s+(?<character>[\p{L}/'-]{2,24})(?:\s*(?:dmg|damage|딜|ダメージ))?[.!]?$",
            @"^(?<character>[\p{L}/'-]{2,24})\s+(?<damage>\d{2,3})(?:\s*(?:dmg|damage|딜|ダメージ))?[.!]?$"
        };
        foreach (var pattern in damagePatterns)
        {
            var match = Regex.Match(normalizedCharacterText, pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success || !int.TryParse(match.Groups["damage"].Value, out var damage) || damage is < 10 or > 999)
                continue;
            var character = CanonicalCharacterName(match.Groups["character"].Value, settings);
            if (character is null) continue;
            translated = $"{character} · {damage} dmg";
            return true;
        }

        var presencePatterns = new[]
        {
            @"^(?<location>.+?)(?:에|에서)\s*(?:(?:적|상대)(?:이|가)?\s*)?(?<count>한|두|세|네|\d+)\s*명(?:이|가)?\s*(?:있(?:습니다|어요|어|음|다))?\s*[.!]?$",
            @"^(?:적|상대)\s*(?<count>한|두|세|네|\d+)\s*명\s*(?<location>.+?)\s*[.!]?$",
            @"^(?<location>.+?)(?:に|で)\s*(?:敵(?:が)?\s*)?(?<count>一人|二人|三人|四人|一|二|三|四|\d+)\s*(?:いる|います)?\s*[。.!]?$",
            @"^(?:敵(?:が)?\s*)?(?<location>.+?)\s+(?<count>一|二|三|四|\d+)\s*(?:人)?\s*(?:いる|います)?[。.!]?$",
            @"^(?:(?:there\s+(?:is|are)\s+)?)?(?<count>one|two|three|four|\d+)\s+(?:enemy|enemies|opponents?)(?:\s+(?:at|in|on))?\s+(?<location>.+?)[.!]?$",
            @"^(?<location>.+?)\s+(?<count>one|two|three|four|\d+)\s+(?:enemy|enemies)[.!]?$",
            @"^(?<location>[A-Ca-c]|[\p{L}][\p{L}\s'-]{1,24})(?:에|에서)?\s+(?<count>한|두|세|네|둘|셋|넷|一|二|三|四|one|two|three|four|\d+)\s*(?:명|人)?[.!]?$",
            @"^(?<count>한|두|세|네|둘|셋|넷|一|二|三|四|one|two|three|four|\d+)\s+(?<location>[A-Ca-c]|[\p{L}][\p{L}\s'-]{1,24})[.!]?$"
        };
        foreach (var pattern in presencePatterns)
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success) continue;
            var location = NormalizeCalloutLocation(match.Groups["location"].Value, settings);
            if (!IsLikelyLocation(location, settings) || IsActionNotLocation(location)) continue;
            var count = NormalizeCount(match.Groups["count"].Value);
            var displayLocation = LocalizeCalloutLocation(location, target);
            // Keep the counter word. "B 헤븐 2" reads as a place called "B Heaven 2";
            // "B 헤븐 2명" can only mean two people, and matches the counted-enemy paths.
            translated = target switch
            {
                "KO" => $"{displayLocation} {count}명",
                "JP" => $"{displayLocation}{count}人",
                _ => $"{count} {(Regex.IsMatch(displayLocation, @"^[ABC]\s") ? displayLocation : displayLocation.ToLowerInvariant())}"
            };
            return true;
        }

        var rotatePatterns = new[]
        {
            @"^(?:i(?:'m| am)?\s+)?(?:rotate|rotating|go|going|move|moving)(?:\s+to)?\s+(?<location>[A-Za-z0-9][\w\s'-]{0,30})[.!]?$",
            @"^(?<location>.+?)(?:로|으로)\s*(?:로테(?:할게|하자|중)?|이동(?:할게|하자|중)?|갈게|가자|돌(?:자|게)).*$",
            @"^(?<location>.+?)(?:へ|に)\s*(?:ローテ|移動|行)(?:する|こう|く|きます|こう)?[。.!]?$"
        };
        foreach (var pattern in rotatePatterns)
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success) continue;
            var location = NormalizeCalloutLocation(match.Groups["location"].Value, settings);
            translated = target switch
            {
                "KO" => $"{location} · 로테",
                "JP" => $"{location} · ローテ",
                _ => $"{location} · rotate"
            };
            return true;
        }

        return false;
    }

    private string NormalizeCalloutLocation(string value, AppSettings settings)
    {
        value = value.Trim().TrimEnd('.', '。', '!', '?');
        value = Regex.Replace(value, @"^(?:at|in|on|to)\s+", "", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"(?:에서|으로|에|로|には|では|に|で|へ)$", "");
        value = NormalizeLocations(value, settings);
        var compound = Regex.Match(value, @"^(?<site>[ABC])\s*(?<area>.+)$", RegexOptions.IgnoreCase);
        if (compound.Success && IsLikelyLocation(compound.Groups["area"].Value, settings))
            return compound.Groups["site"].Value.ToUpperInvariant() + " " +
                   NormalizeCalloutLocation(compound.Groups["area"].Value, settings);
        if (Regex.IsMatch(value, @"^[ABC]$", RegexOptions.IgnoreCase)) return value.ToUpperInvariant();
        var canonical = SelectedLocations(settings).Values
            .FirstOrDefault(location => location.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (canonical is not null) return canonical;
        var generic = new[] { "Mid", "Main", "Site", "Heaven", "Hell", "Short", "Long", "Link", "Spawn", "Left", "Right", "Front", "Back", "Top", "Bottom" };
        canonical = generic.FirstOrDefault(location => location.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (canonical is not null) return canonical;
        return value.Length == 0 ? "?" : value;
    }

    private static string? CanonicalCharacterName(string value, AppSettings settings) =>
        ProperNames.Values.Concat(settings.CustomGlossary.Values)
            .FirstOrDefault(name => name.Equals(value, StringComparison.OrdinalIgnoreCase));

    private static bool IsSiteActionCallout(string text) =>
        Regex.IsMatch(text.Trim(),
            @"^(?<site>[ABC])\s*(?<action>rush|push|ラッシュ|プッシュ|러시|푸시)[.!]?$|^(?<site2>[ABC])(?<action2>ラッシュ|러시|プッシュ)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool IsActionNotLocation(string location) =>
        Regex.IsMatch(location.Trim(), @"^(?:rush|push|ラッシュ|プッシュ|러시|푸시)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool IsLikelyLocation(string value, AppSettings settings)
    {
        if (Regex.IsMatch(value, @"^[ABC]$", RegexOptions.IgnoreCase)) return true;
        var compound = Regex.Match(value, @"^[ABC]\s+(?<area>.+)$", RegexOptions.IgnoreCase);
        if (compound.Success) return IsLikelyLocation(compound.Groups["area"].Value, settings);
        var generic = new[]
        {
            "Mid", "Main", "Site", "Heaven", "Hell", "Short", "Long", "Link", "Spawn",
            "Left", "Right", "Front", "Back", "Top", "Bottom", "미드", "왼쪽", "오른쪽", "앞", "뒤",
            "ミッド", "左", "右", "前", "後ろ"
        };
        if (generic.Any(location => location.Equals(value, StringComparison.OrdinalIgnoreCase))) return true;
        var locations = SelectedLocations(settings);
        return locations.Keys.Concat(locations.Values)
            .Any(location => location.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeCount(string value) => value.ToLowerInvariant() switch
    {
        "one" or "한" or "一" or "一人" or "ひとり" => "1",
        "two" or "두" or "둘" or "二" or "二人" or "ふたり" => "2",
        "three" or "세" or "셋" or "三" or "三人" => "3",
        "four" or "네" or "넷" or "四" or "四人" => "4",
        _ => value
    };

    private static string LocalizeCalloutLocation(string location, string target, AppSettings? settings = null)
    {
        var compound = Regex.Match(location, @"^(?<site>[ABC])\s+(?<area>.+)$", RegexOptions.IgnoreCase);
        if (compound.Success)
            return compound.Groups["site"].Value.ToUpperInvariant() + " " +
                   LocalizeCalloutLocation(compound.Groups["area"].Value, target, settings);
        var localized = target switch
        {
            "KO" => location.ToLowerInvariant() switch
            {
                "mid" or "ミッド" => "미드", "left" or "左" => "왼쪽", "right" or "右" => "오른쪽",
                "front" or "前" => "앞", "back" or "後ろ" => "뒤",
                "main" => "메인", "site" => "사이트", "heaven" => "헤븐", "hell" => "헬", "short" => "숏",
                "long" => "롱", "link" => "링크", "spawn" => "스폰", "top" => "위", "bottom" => "아래",
                _ => location
            },
            "JP" => location.ToLowerInvariant() switch
            {
                "mid" or "미드" => "ミッド", "left" or "왼쪽" => "左", "right" or "오른쪽" => "右",
                "front" or "앞" => "前", "back" or "뒤" => "後ろ",
                "main" => "メイン", "site" => "サイト", "heaven" => "ヘブン", "hell" => "ヘル", "short" => "ショート",
                "long" => "ロング", "link" => "リンク", "spawn" => "スポーン", "top" => "上", "bottom" => "下",
                _ => location
            },
            _ => location switch
            {
                "미드" or "ミッド" => "Mid", "왼쪽" or "左" => "Left", "오른쪽" or "右" => "Right",
                "앞" or "前" => "Front", "뒤" or "後ろ" => "Back",
                _ => location
            }
        };

        if (!localized.Equals(location, StringComparison.OrdinalIgnoreCase) || settings is null || target == "EN")
            return localized;

        var aliases = SelectedLocations(settings)
            .Where(pair => pair.Value.Equals(location, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Key);
        return target switch
        {
            "KO" => aliases.FirstOrDefault(alias => alias.Any(ch => ch is >= '\uAC00' and <= '\uD7A3')) ?? location,
            "JP" => aliases.FirstOrDefault(alias => alias.Any(ch =>
                ch is >= '\u3040' and <= '\u30FF' or >= '\u4E00' and <= '\u9FFF')) ?? location,
            _ => location
        };
    }
}

public sealed record ProtectedTranslationText(string Text, IReadOnlyDictionary<string, string> Tokens);
