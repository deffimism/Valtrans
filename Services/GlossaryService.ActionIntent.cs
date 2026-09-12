using System.Text.RegularExpressions;
using Valtrans.Models;

namespace Valtrans.Services;

public sealed partial class GlossaryService
{
    // A closed grammar for actions that cannot safely be inferred from a bag of nouns.
    // Whole input must match; unfamiliar clauses/quotes go to the model untouched.
    private bool TryTranslateActionIntent(string source, string target, AppSettings settings, out string translated)
    {
        translated = "";
        var text = source.Trim().TrimEnd('.', '!', '。', '！').Trim();
        string Render(string en, string ko, string jp) => target switch { "KO" => ko, "JP" => jp, _ => en };
        bool Match(string pattern) => Regex.IsMatch(text, "^(?:" + pattern + ")$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (Match(@"(?:아직\s*(?:들어가지|진입하지)\s*마|(?:don't|do not)\s+(?:enter|go in)\s+yet|まだ(?:入らないで|入るな))"))
            translated = Render("don't enter yet", "아직 들어가지 마", "まだ入らないで");
        else if (Match(@"(?:지금\s*(?:들어가도|진입해도)\s*돼|you can (?:enter|go in) now|今(?:入っていいよ|入ってもいいよ))"))
            translated = Render("you can enter now", "지금 들어가도 돼", "今入っていいよ");
        else if (Match(@"(?:(?:플래시|섬광)\s*온다\s*[,、]?\s*뒤돌아|flash\s+(?:incoming|coming)\s*[,、]\s*turn away|フラッシュ(?:が)?来る\s*[,、]\s*後ろ向いて)"))
            translated = Render("flash incoming, turn away", "섬광 온다, 뒤돌아", "フラッシュ来る、後ろ向いて");
        else if (Match(@"(?:스파이크\s*줍지\s*말고\s*각\s*잡아|(?:don't|do not) pick up the spike\s*[,、]\s*hold (?:the|an) angle|スパイクを拾わずに射線を見て)"))
            translated = Render("don't pick up the spike, hold the angle", "스파이크 줍지 말고 각 잡아", "スパイクを拾わずに射線を見て");
        else if (Match(@"(?:해체(?:하는)?\s*척만\s*해|just (?:fake the defuse|pretend to defuse(?: the spike)?)|解除するふりだけして)"))
            translated = Render("just fake the defuse", "해체하는 척만 해", "解除するふりだけして");
        else if (Match(@"(?:푸시하지\s*마\s*[,、]?\s*내가\s*먼저\s*볼게|(?:don't|do not) push\s*[,、]\s*I(?:'|’)ll check first|プッシュしないで\s*[,、]\s*先に自分が見る)"))
            translated = Render("don't push, I'll check first", "푸시하지 마, 내가 먼저 볼게", "プッシュしないで、先に自分が見る");
        else if (Match(@"(?:뒤도는\s*중이니까\s*조금만\s*버텨|I(?:'|’)m flanking\s*[,、]\s*hold on (?:a )?little longer|裏取り中だからもう少し耐えて)"))
            translated = Render("I'm flanking, hold on a little longer", "뒤도는 중이니까 조금만 버텨", "裏取り中だからもう少し耐えて");
        if (translated.Length > 0) return true;

        var heldWeapon = Regex.Match(text,
            @"^(?<weapon>오퍼|오퍼레이터|팬텀|밴달|Operator|Op|Phantom|Vandal)(?:을|를)?\s*들고\s*있으니까\s*(?<alone>혼자\s*)?피킹하지\s*마$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (heldWeapon.Success)
        {
            if (settings.CustomGlossary.Keys.Any(key => key.Length > 0 && text.Contains(key, StringComparison.OrdinalIgnoreCase)))
                return false;
            var weapon = TranslationTerms.First(term => term.En is "Operator" or "Vandal" or "Phantom" &&
                (term.Ko.Equals(heldWeapon.Groups["weapon"].Value, StringComparison.OrdinalIgnoreCase) ||
                 term.Aliases.Contains(heldWeapon.Groups["weapon"].Value, StringComparer.OrdinalIgnoreCase)));
            var alone = heldWeapon.Groups["alone"].Success;
            // The Korean clause omits the holder. Do not invent I/we/the enemy.
            translated = Render($"someone has {(weapon.En == "Operator" ? "an" : "a")} {weapon.En}, don't peek{(alone ? " alone" : "")}",
                $"누군가 {weapon.Ko}를 들고 있으니까 {(alone ? "혼자 " : "")}피킹하지 마",
                $"誰かが{weapon.Jp}を持ってるから、{(alone ? "一人で" : "")}ピークしないで");
            return true;
        }

        var names = NormalizeNames(text, settings);
        const string person = @"(?<person>[A-Za-z][A-Za-z/'-]{1,23})";
        const string amount = @"(?<damage>\d{1,3})";
        var damage = Regex.Match(names,
            $@"^(?:(?:내가\s*)?{person}(?:한테|에게)\s*{amount}\s*넣었(?:어|는데)|I hit {person} for {amount}|{person}に{amount}入れた)(?<alive>\s*[,、]?\s*(?:아직\s*살아\s*있어|but (?:he|she|they)(?:'s|’s| is| are) still alive|けどまだ生きてる))?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (damage.Success && !names.EndsWith("넣었는데", StringComparison.Ordinal) &&
            int.TryParse(damage.Groups["damage"].Value, out var points) && points > 0)
        {
            var character = CanonicalCharacterName(damage.Groups["person"].Value, settings);
            if (character is null) return false;
            var alive = damage.Groups["alive"].Success;
            translated = Render($"hit {character} for {points}" + (alive ? ", still alive" : ""),
                $"{character}에게 {points} 피해" + (alive ? ", 아직 살아 있음" : ""),
                $"{character}に{points}ダメージ" + (alive ? "、まだ生きてる" : ""));
            return true;
        }

        var japaneseCount = Regex.Match(text,
            @"^(?<location>(?:[ABC]\s*)?(?:メイン|ヘブン|ミッド|ロング|ショート|サイト|左|右))(?:に)?\s*(?<count>[1-5一二三四五])(?:人|つ)(?:いる)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (japaneseCount.Success)
        {
            var location = NormalizeCalloutLocation(japaneseCount.Groups["location"].Value, settings);
            if (!IsLikelyLocation(location, settings)) return false;
            var count = NormalizeCount(japaneseCount.Groups["count"].Value);
            translated = Render($"{count} {LocalizeCalloutLocation(location, "EN", settings)}",
                $"{LocalizeCalloutLocation(location, "KO", settings)} {count}명",
                $"{LocalizeCalloutLocation(location, "JP", settings)}{count}人");
            return true;
        }
        return false;
    }
}
