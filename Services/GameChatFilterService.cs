using System.Text.RegularExpressions;
using Valtrans.Models;

namespace Valtrans.Services;

public sealed class GameChatFilterService
{
    public const string BriefingMode = "Briefing";
    public const string AllMode = "All";
    public const string StrictMode = "Strict";

    private readonly GlossaryService _glossary;

    // Inflections and support/equipment calls must survive the receive filter,
    // not only the translation test's All mode. Match phrases for ambiguous
    // verbs (cover/retake) rather than treating every mention as a combat call.
    private static readonly Regex AdditionalCombatTerms = new(
        @"(?ix)\b(?:behind|footsteps?|planting|defusing|reloading|tripwires?)\b|" +
        @"\b(?:cover|covering)\s+(?:me|you|him|her|them|us|our\s+team|my\s+team)\b|" +
        @"\b(?:exact|remaining|full|low)\s+(?:hp|health)\b|\b\d+\s*hp\b|" +
        @"(?<![ァ-ヶ])(?:ヒール|フルバイ|トラップ|ワイヤー)(?![ァ-ヶ])|" +
        @"(?<![가-힣])(?:풀바이|트랩|발소리|엄호)(?=$|[\s,.!?]|하|할|해|좀|가|는|을|이|에)|" +
        @"\b(?:hit|dealt|deal|take|took)\b[^\r\n.!?]{0,35}\b\d+\b|" +
        @"(?:한테|에게)\s*\d+\s*(?:넣|맞)|に\s*\d+\s*(?:入れ|当て|くらっ)|" +
        @"(?:밴달|팬텀|오퍼(?:레이터)?)[^\r\n.!?]{0,20}사\s*줄|" +
        @"(?:ヴァンダル|ファントム|オペレーター)[^\r\n。！？]{0,20}買|" +
        @"(?:라운드|ラウンド)[^\r\n.!?。！？]{0,15}(?:이기|勝)|\bwin\b[^\r\n.!?]{0,20}\brounds?\b|" +
        @"\bplay\s+slow\b|천천히\s*해[^\r\n.!?]{0,20}\d+\s*초|ゆっくり[^\r\n。！？]{0,20}\d+\s*秒",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RetakeAction = new(@"(?i)\bretak(?:e|ing)\b|리테이크|リテイク",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex RetakeNonGameContext = new(
        @"(?i)\b(?:exam|test|photo|photograph|picture|class|course|driving|video)\b|시험|사진|재촬영|試験|写真|撮り直",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PlayerHealthTerms = new(
        @"(?ix)(?<![가-힣])(?:원탭|원샷|풀피)(?=$|[\s,.!?]|이|은|는|을|야|다|임|인|가|고|로|에|면|아니)|" +
        @"(?<![ァ-ヶ])(?:ワンショット|フルHP|体力満タン)(?![ァ-ヶ])|\bfull\s+(?:hp|health)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TacticalTerms = new(
        @"(?ix)(?:\b(?:mid|main|site|heaven|hell|short|long|left|right|front|back|spawn|ct|rotate|rotating|push|rush|hold|peek|flank|plant|defuse|save|buy|eco|flash|smoke|molly|ult|heal|revive|rez|cracked|knocked|one\s*shot|low\s*hp|dmg|damage|help|wait|enemy|enemies|opponent|opponents)\b|" +
        @"미드|메인|사이트|헤븐|숏|롱|왼쪽|오른쪽|앞|뒤|스폰|로테|합류|푸시|러시|막아|지켜|피킹|(?<![가-힣])피(?:가|는|은)?(?=$|\s*(?:[0-9]|없|남|낮|적|몇|얼마))|플랭크|설치|해체|세이브|구매|플래시|연막|스모크|(?<![가-힣])궁(?=$|\s|[,.!?]|극기|은|이|을|도|만|없|있|빠|썼|써)|힐|살려|부활|딸피|체력|데미지|도와|기다려|(?<![가-힣])적(?=$|\s|[,.!?0-9]|은|이|을|도|만|없|있)|상대|" +
        @"ミッド|メイン|サイト|ヘブン|ショート|ロング|左|右|前|後ろ|スポーン|ローテ|合流|プッシュ|ラッシュ|守って|ピーク|フランク|設置|解除|セーブ|購入|フラッシュ|スモーク|ウルト|回復|蘇生|ロー|ダメージ|助けて|待って|敵|相手|" +
        @"中路|短道|长道|天堂|地狱|残血|小|两个|后面|左边|右边|敌人|防守|进攻)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SiteLetter = new(
        @"(?:^|[\s:：,，/])(?:[ABC])(?=$|[\s:：,，/]|(?:로|에|에서|쪽|사이트|へ|に|で))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ToxicNoise = new(
        @"(?ix)(?:\b(?:idiot|stupid|moron|trash|noob|shut\s*up|wtf|fuck(?:ing)?|f+u+c+k+|dogshit)\b|" +
        @"바보(?:야|냐)?|멍청(?:이|아)?|병신(?:아|이야)?|ㅂㅅ|꺼져|닥쳐|개못|못하네|트롤|쓰레기|" +
        @"ばか|バカ|アホ|下手くそ|黙れ|ゴミ|雑魚)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public GameChatFilterService(GlossaryService glossary) => _glossary = glossary;

    public ChatFilterResult Filter(string text, string mode, AppSettings settings)
    {
        var body = ChatTextSanitizer.StripChatPrefix(text).Trim();
        if (!ChatTextSanitizer.HasMeaningfulContent(body))
            return new ChatFilterResult(false, "", "글자 없는 OCR 결과", "Noise");
        if (mode == AllMode)
            return new ChatFilterResult(true, body, "전체 채팅 번역", "All");

        var cleaned = CleanToxicNoise(body);
        if (!ChatTextSanitizer.HasMeaningfulContent(cleaned))
            return new ChatFilterResult(false, "", "감정 표현만 포함", "LowRelevance");

        var tactical = IsTactical(cleaned, settings);
        var social = IsSocial(cleaned, settings);
        if (mode == StrictMode && !tactical)
            return new ChatFilterResult(false, "", "콜아웃이 아닌 채팅", "LowRelevance");
        if (!tactical && !social)
            return new ChatFilterResult(false, "", "게임 관련성 낮음", "LowRelevance");

        if (tactical)
        {
            // Select messages, not fragments. The other clause may contain the
            // condition, speaker, negation or correction for the tactical clause.
            return new ChatFilterResult(true, body, "게임 콜아웃 · 전체 문장 보존", "Tactical");
        }
        return new ChatFilterResult(true, cleaned, "짧은 인사·감사·사과", "Social");
    }

    /// <summary>
    /// Category of a message that is already being translated. Unlike <see cref="Filter"/>
    /// this never drops anything: the caller has decided to keep the line and only needs to
    /// know how strictly to validate it. Callers must not use <see cref="AllMode"/> for this,
    /// because that mode short-circuits before any category is computed.
    /// </summary>
    public ChatFilterResult Categorize(string text, AppSettings settings)
    {
        var body = ChatTextSanitizer.StripChatPrefix(text).Trim();
        if (!ChatTextSanitizer.HasMeaningfulContent(body))
            return new ChatFilterResult(false, "", "글자 없는 OCR 결과", "Noise");

        var cleaned = CleanToxicNoise(body);
        if (!ChatTextSanitizer.HasMeaningfulContent(cleaned))
            return new ChatFilterResult(true, body, "감정 표현만 포함", "LowRelevance");
        if (IsTactical(cleaned, settings))
            return new ChatFilterResult(true, cleaned, "게임 콜아웃", "Tactical");
        if (IsSocial(cleaned, settings))
            return new ChatFilterResult(true, cleaned, "짧은 인사·감사·사과", "Social");
        return new ChatFilterResult(true, cleaned, "게임 관련성 낮음", "LowRelevance");
    }

    private bool IsTactical(string text, AppSettings settings) =>
        _glossary.ContainsTacticalSlang(text, settings) ||
        _glossary.TryTranslateStructuredCallout(text, "EN", settings, out _) ||
        _glossary.ContainsKnownGameReference(text, settings) ||
        TacticalTerms.IsMatch(text) || PlayerHealthTerms.IsMatch(text) || SiteLetter.IsMatch(text) ||
        AdditionalCombatTerms.IsMatch(text) || RetakeAction.IsMatch(text) && !RetakeNonGameContext.IsMatch(text);

    private bool IsSocial(string text, AppSettings settings) =>
        _glossary.TryTranslateExactShortcut(text, "EN", out _, settings);

    private static string CleanToxicNoise(string text)
    {
        text = ToxicNoise.Replace(text, " ");
        text = Regex.Replace(text, @"\s{2,}", " ").Trim(' ', ',', '.', '!', '?', '。', '！', '？');
        return text;
    }
}

public sealed record ChatFilterResult(bool Keep, string Text, string Reason, string Category);
