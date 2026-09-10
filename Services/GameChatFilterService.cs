using System.Text.RegularExpressions;
using Valtrans.Models;

namespace Valtrans.Services;

public sealed class GameChatFilterService
{
    public const string BriefingMode = "Briefing";
    public const string AllMode = "All";
    public const string StrictMode = "Strict";

    private readonly GlossaryService _glossary;

    private static readonly Regex TacticalTerms = new(
        @"(?ix)(?:\b(?:mid|main|site|heaven|hell|short|long|left|right|front|back|spawn|ct|rotate|rotating|push|rush|hold|peek|flank|plant|defuse|save|buy|eco|flash|smoke|molly|ult|heal|revive|rez|cracked|knocked|one\s*shot|low\s*hp|dmg|damage|help|wait|enemy|enemies|opponent|opponents)\b|" +
        @"미드|메인|사이트|헤븐|숏|롱|왼쪽|오른쪽|앞|뒤|스폰|로테|합류|푸시|러시|막아|지켜|피킹?|플랭크|설치|해체|세이브|구매|플래시|연막|스모크|궁|힐|살려|부활|딸피|체력|데미지|도와|기다려|적|상대|" +
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

    private static readonly Regex ClauseSeparator = new(
        @"(?:[.!?。！？,，、;；]+|\s+(?:but|and|however|근데|그리고|하지만|그래도|でも|けど|しかし)\s+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

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
            var extracted = ExtractTacticalClauses(cleaned, settings);
            return new ChatFilterResult(true, extracted, extracted.Equals(body, StringComparison.Ordinal)
                ? "게임 콜아웃"
                : "감정 표현 제거 · 콜아웃 추출", "Tactical");
        }
        return new ChatFilterResult(true, cleaned, "짧은 인사·감사·사과", "Social");
    }

    private bool IsTactical(string text, AppSettings settings) =>
        _glossary.ContainsTacticalSlang(text, settings) ||
        _glossary.TryTranslateStructuredCallout(text, "EN", settings, out _) ||
        _glossary.ContainsKnownGameReference(text, settings) ||
        TacticalTerms.IsMatch(text) || SiteLetter.IsMatch(text);

    private bool IsSocial(string text, AppSettings settings) =>
        _glossary.TryTranslateExactShortcut(text, "EN", out _, settings);

    private string ExtractTacticalClauses(string text, AppSettings settings)
    {
        var clauses = ClauseSeparator.Split(text)
            .Select(clause => clause.Trim())
            .Where(ChatTextSanitizer.HasMeaningfulContent)
            .Where(clause => IsTactical(clause, settings))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();
        return clauses.Length == 0 ? text : string.Join(" / ", clauses);
    }

    private static string CleanToxicNoise(string text)
    {
        text = ToxicNoise.Replace(text, " ");
        text = Regex.Replace(text, @"\s{2,}", " ").Trim(' ', ',', '.', '!', '?', '。', '！', '？');
        return text;
    }
}

public sealed record ChatFilterResult(bool Keep, string Text, string Reason, string Category);
