using System.Text.RegularExpressions;
using Valtrans.Models;

namespace Valtrans.Services;

public sealed partial class GlossaryService
{
    private bool TryTranslateTacticalState(string source, string target, AppSettings settings, out string translated)
    {
        translated = "";
        var text = source.Trim().TrimEnd('.', '!', '。', '！').Trim();
        string Render(string en, string ko, string jp) => target switch { "KO" => ko, "JP" => jp, _ => en };
        Match Match(string expression) => Regex.Match(text, "^(?:" + expression + ")$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        const string count = @"(?<count>[1-5]|one|two|three|four|five|한|하나|두|둘|세|셋|네|넷|다섯|一|二|三|四|五)";
        const string location = @"(?<location>[^,、!?？。\r\n]{1,40}?)";

        // Preserve the explicitly named side. Do not turn allies into enemies,
        // or infer a side when the source merely says "two holding heaven".
        var holding = Match(
            $@"(?<side>아군|팀원|적)\s*{count}\s*(?:명)?(?:이|가)\s*{location}\s*(?:지키고|막고)\s*있어|" +
            $@"{count}\s+(?<side>teammates?|allies|enemies)\s+(?:are\s+)?holding\s+{location}|" +
            $@"(?<side>味方|敵){count}人が{location}を守って(?:る|いる)");
        if (holding.Success)
        {
            var place = NormalizeCalloutLocation(holding.Groups["location"].Value, settings);
            if (!IsLikelyLocation(place, settings)) return false;
            var n = NormalizeCount(holding.Groups["count"].Value);
            var enemy = Regex.IsMatch(holding.Groups["side"].Value, @"^(적|敵|enemies)$", RegexOptions.IgnoreCase);
            translated = Render($"{n} {(enemy ? "enemies" : "teammates")} holding {LocalizeCalloutLocation(place, "EN", settings)}",
                $"{(enemy ? "적" : "아군")} {n}명 {LocalizeCalloutLocation(place, "KO", settings)} 지키는 중",
                $"{(enemy ? "敵" : "味方")}{n}人が{LocalizeCalloutLocation(place, "JP", settings)}を守ってる");
            return true;
        }

        var dropped = Match($@"스파이크(?:가)?\s*{location}에\s*떨어졌어|" +
            $@"(?:the\s+)?spike\s+(?:is\s+)?(?:dropped|down)\s+(?:at\s+)?{location}|" +
            $@"スパイクが{location}に落ち(?:た|てる|ている)");
        if (dropped.Success)
        {
            var place = NormalizeCalloutLocation(dropped.Groups["location"].Value, settings);
            if (!IsLikelyLocation(place, settings)) return false;
            translated = Render($"spike down {LocalizeCalloutLocation(place, "EN", settings)}",
                $"스파이크 {LocalizeCalloutLocation(place, "KO", settings)}에 떨어짐",
                $"スパイクが{LocalizeCalloutLocation(place, "JP", settings)}に落ちてる");
            return true;
        }

        var rotate = Match(@"(?<clear>[ABC])\s*비었어\s*[,、]?\s*(?<go>[ABC])로\s*로테하자|" +
            @"(?<clear>[ABC])\s+(?:is\s+)?clear\s*[,、]\s*rotate\s+(?:to\s+)?(?<go>[ABC])|" +
            @"(?<clear>[ABC])はクリア\s*[,、]\s*(?<go>[ABC])にローテしよう");
        if (rotate.Success)
        {
            var clear = rotate.Groups["clear"].Value.ToUpperInvariant();
            var go = rotate.Groups["go"].Value.ToUpperInvariant();
            translated = Render($"{clear} clear, rotate {go}", $"{clear} 비었어, {go}로 로테하자",
                $"{clear}はクリア、{go}にローテしよう");
            return true;
        }
        if (Match(@"이번\s*라운드\s*세이브하자|let(?:'|’)s save this round|このラウンドはセーブしよう").Success)
            // "Save" may mean an economy round OR saving a carried weapon.
            // Preserve that ambiguity unless the source explicitly names weapons.
            translated = Render("let's save this round", "이번 라운드 세이브하자", "このラウンドはセーブしよう");
        if (Match(@"(?:상대|적)\s*궁(?:극기)?\s*빠졌어|(?:the\s+)?(?:enemy|opponent)\s+(?:has\s+)?used\s+(?:their|the)\s+ult(?:imate)?|(?:相手|敵)のウルトは使用済み").Success)
            translated = Render("they used their ult", "상대 궁 썼어", "相手はウルトを使った");
        var conditional = Match(@"내가\s*죽으면\s*(?<immediate>바로\s*)?같이\s*피킹해|if I die[,]?\s*peek together(?<immediate>\s+right away)?|自分が死んだら(?<immediate>すぐ)?一緒にピークして");
        if (conditional.Success)
        {
            var immediately = conditional.Groups["immediate"].Success;
            translated = Render($"if I die, peek together{(immediately ? " right away" : "")}",
                $"내가 죽으면 {(immediately ? "바로 " : "")}같이 피킹해",
                $"自分が死んだら{(immediately ? "すぐ" : "")}一緒にピークして");
        }

        // A concessive prohibition is not the positive "if I die, peek" above.
        // Keep the first-person condition, the imperative, and optional "yet".
        var prohibition = Match(@"내가\s*죽어도\s*(?<yet>아직\s*)?피킹하지\s*마|" +
            @"even if I die[,]?\s*(?:don't|don’t|do not) peek(?<yet>\s+yet)?|" +
            @"(?:don't|don’t|do not) peek(?<yet>\s+yet)?\s+even if I die|" +
            @"(?:自分|私|僕|俺)が死んでも(?<yet>まだ)?ピークしないで");
        if (prohibition.Success)
        {
            var yet = prohibition.Groups["yet"].Success;
            translated = Render($"even if I die, don't peek{(yet ? " yet" : "")}",
                $"내가 죽어도 {(yet ? "아직 " : "")}피킹하지 마",
                $"私が死んでも{(yet ? "まだ" : "")}ピークしないで");
        }

        if (CalloutCountRevision.TryParse(text, out var before, out var after))
        {
            translated = Render($"I thought there {(before == 1 ? "was" : "were")} {before} {(before == 1 ? "person" : "people")}, but there {(after == 1 ? "was" : "were")} {after} {(after == 1 ? "person" : "people")}",
                $"{before}명인 줄 알았는데 {after}명이었어", $"{before}人だと思ったけど{after}人だった");
        }

        var uncertain = Match($@"(?:아마\s*)?{location}에\s*{count}\s*(?:명)?\s*있을지도\s*모르는데\s*확실(?:하진|하지는|하지)\s*않아|" +
            $@"there (?:might|may) be {count}\s+{location},\s*but I(?:'|’)m not sure|" +
            $@"{location}に{count}人いるかもしれないけど[、,]?(?:確かではない|確信はない)");
        if (uncertain.Success)
        {
            var place = NormalizeCalloutLocation(uncertain.Groups["location"].Value, settings);
            if (!IsLikelyLocation(place, settings)) return false;
            var n = NormalizeCount(uncertain.Groups["count"].Value);
            translated = Render($"maybe {n} {LocalizeCalloutLocation(place, "EN", settings)}, not sure",
                $"{LocalizeCalloutLocation(place, "KO", settings)}에 {n}명 있을지도, 확실하진 않아",
                $"{LocalizeCalloutLocation(place, "JP", settings)}に{n}人かも、確かではない");
        }
        return translated.Length > 0;
    }
}
