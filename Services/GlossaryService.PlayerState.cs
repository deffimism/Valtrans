using System.Text.RegularExpressions;
using Valtrans.Models;

namespace Valtrans.Services;

public sealed partial class GlossaryService
{
    // Whole-clause grammars distinguish a player's state from their ability to
    // one-shot someone. Questions, reports, negations and extra clauses stay intact
    // for model translation; do not extract a reassuring fragment from them.
    private bool TryTranslatePlayerState(string source, string target, AppSettings settings, out string translated)
    {
        translated = "";
        var text = NormalizeNames(source.Trim().TrimEnd('.', '!', '。', '！').Trim(), settings);
        var state = Regex.Match(text,
            @"^(?:(?<actor>걔|쟤|나|너|[A-Za-z][A-Za-z/'-]{1,23})(?:는|가|이)?\s*(?:원탭|원샷)(?:이야|이네|임)?|" +
            @"(?<actor>I|you|he|she|they|[A-Za-z][A-Za-z/'-]{1,23})(?:['’](?:m|re|s)|\s+(?:am|are|is))\s+one[ -]shot|" +
            @"(?<actor>あいつ|自分|私|君|[A-Za-z][A-Za-z/'-]{1,23})(?:は|が)?(?:ワンショット|あと一発)(?:だよ|だ)?)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (state.Success)
        {
            var actor = state.Groups["actor"].Value;
            var person = actor.ToLowerInvariant() switch
            {
                "나" or "i" or "自分" or "私" => (En: "I'm", Ko: "나", Jp: "自分"),
                "너" or "you" or "君" => (En: "you're", Ko: "너", Jp: "君"),
                "he" => (En: "he's", Ko: "걔", Jp: "あいつ"),
                "she" => (En: "she's", Ko: "걔", Jp: "あいつ"),
                "걔" or "쟤" or "あいつ" => (En: "they're", Ko: "걔", Jp: "あいつ"),
                _ => (En: "", Ko: "", Jp: "")
            };
            if (person.En.Length == 0)
            {
                var character = CanonicalCharacterName(actor, settings);
                if (character is null) return false;
                person = ($"{character} is", character, character);
            }
            translated = target switch
            {
                "KO" => $"{person.Ko} 한 대면 죽어",
                "JP" => $"{person.Jp}はあと一発",
                _ => $"{person.En} one shot"
            };
            return true;
        }

        var trade = Regex.Match(text,
            @"^(?:(?<me>내\s*)?뒤에\s*(?:붙어서|따라서)\s*트레이드(?:해줘|해)|" +
            @"(?:stay|stick)\s+behind\s+(?<me>me)\s+and\s+trade\s+me|" +
            @"(?<me>私の|自分の)?後ろについて(?:トレード|カバーキル)して)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!trade.Success) return false;
        var mine = trade.Groups["me"].Success;
        translated = target switch
        {
            "KO" => $"{(mine ? "내 " : "")}뒤에 붙어서 킬 교환해줘",
            "JP" => $"{(mine ? "自分の" : "")}後ろについてカバーキルして",
            _ => $"stay behind{(mine ? " me" : "")} and get the trade kill"
        };
        return true;
    }
}
