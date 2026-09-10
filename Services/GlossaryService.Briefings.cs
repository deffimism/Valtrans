using System.Text.RegularExpressions;
using Valtrans.Models;

namespace Valtrans.Services;

public sealed partial class GlossaryService
{
    public bool TryTranslateWatchDirection(string text, string target, out string translated)
    {
        translated = "";
        var trimmed = text.Trim().TrimEnd('.', '!', '。');
        var match = Regex.Match(trimmed,
            @"^(?<dir>左|右|左側|右側|왼쪽|오른쪽|좌측|우측)(?:を)?見て[。.!]?$|^(?:watch|check)\s+(?<dir>left|right)[.!]?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success &&
            Regex.IsMatch(trimmed, @"^watch\s+\S", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
            !trimmed.Contains("right", StringComparison.OrdinalIgnoreCase) &&
            trimmed.Length <= 24)
        {
            match = Regex.Match("watch left", @"^(?:watch|check)\s+(?<dir>left|right)[.!]?$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        if (!match.Success) return false;
        var key = match.Groups["dir"].Value.ToLowerInvariant() switch
        {
            "左" or "左側" or "왼쪽" or "좌측" or "left" => "left",
            _ => "right"
        };
        translated = (key, target) switch
        {
            ("left", "KO") => "왼쪽 조심",
            ("right", "KO") => "오른쪽 조심",
            ("left", "JP") => "左見て",
            ("right", "JP") => "右見て",
            ("left", _) => "watch left",
            _ => "watch right"
        };
        return true;
    }

    public bool TryTranslateMovementProhibition(string text, string target, AppSettings settings, out string translated)
    {
        translated = "";
        var match = Regex.Match(text.Trim().TrimEnd('.', '!', '。', '！'),
            @"^(?<location>.+?)(?:으로|로)\s*(?:가지\s*)?마[.!]?$|^(?:don['’]?t|do\s+not)\s+go\s+(?<location>left|right|mid|main|site|[\p{L}\p{N}\s'-]{1,24})[.!]?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success) return false;
        var location = NormalizeCalloutLocation(match.Groups["location"].Value, settings);
        if (!IsLikelyLocation(location, settings)) return false;
        var displayLocation = LocalizeCalloutLocation(location, target);
        translated = target switch
        {
            "KO" => $"{displayLocation} 가지 마",
            "JP" => $"{displayLocation}行かないで",
            _ => $"don't go {displayLocation.ToLowerInvariant()}"
        };
        return true;
    }

    public bool TryTranslateEnemyPresenceBriefing(string text, string target, AppSettings settings, out string translated)
    {
        translated = "";
        var noCountPatterns = new[]
        {
            @"^(?<location>.+?)(?:에|에서)\s*(?:적|상대)(?:이|가)?\s*(?:있(?:어|음|다|습니다)?)[.!]?$",
            @"^(?<location>.+?)(?:に|で)\s*敵(?:が|は)?\s*(?:いる|います)?[。.!]?$"
        };
        foreach (var pattern in noCountPatterns)
        {
            var match = Regex.Match(text.Trim(), pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success) continue;
            var location = NormalizeCalloutLocation(match.Groups["location"].Value, settings);
            if (!IsLikelyLocation(location, settings)) continue;
            var displayLocation = LocalizeCalloutLocation(location, target);
            translated = target switch
            {
                "KO" => $"{displayLocation} 적",
                "JP" => $"{displayLocation}敵",
                _ => $"enemy {displayLocation.ToLowerInvariant()}"
            };
            return true;
        }

        var countedEnemy = Regex.Match(text.Trim(),
            @"^(?<location>.+?)(?:に|で)\s*敵(?:が)?\s*(?<count>一人|二人|三人|四人|一|二|三|四|\d+)\s*(?:いる|います)?[。.!]?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (countedEnemy.Success)
        {
            var location = NormalizeCalloutLocation(countedEnemy.Groups["location"].Value, settings);
            if (IsLikelyLocation(location, settings))
            {
                var count = NormalizeCount(countedEnemy.Groups["count"].Value);
                var displayLocation = LocalizeCalloutLocation(location, target);
                translated = target switch
                {
                    "KO" => $"{displayLocation} 적 {count}명",
                    "JP" => $"{displayLocation}敵{count}人",
                    _ => $"{count} {displayLocation.ToLowerInvariant()}"
                };
                return true;
            }
        }
        return false;
    }

    public bool TryTranslateSiteActionBriefing(string text, string target, out string translated)
    {
        translated = "";
        var match = Regex.Match(text.Trim().TrimEnd('.', '!', '?', '。', '！'),
            @"^(?<site>[ABC])\s*(?<action>rush|push|ラッシュ|プッシュ|러시|푸시)[.!]?$|^(?<site2>[ABC])(?<action2>ラッシュ|러시|プッシュ)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success) return false;
        var site = (match.Groups["site"].Success ? match.Groups["site"] : match.Groups["site2"]).Value.ToUpperInvariant();
        var action = (match.Groups["action"].Success ? match.Groups["action"] : match.Groups["action2"]).Value.ToLowerInvariant();
        var actionKo = action is "rush" or "ラッシュ" or "러시" ? "러시" : "푸시";
        var actionJp = action is "push" or "プッシュ" or "푸시" ? "プッシュ" : "ラッシュ";
        var actionEn = action is "push" or "プッシュ" or "푸시" ? "push" : "rush";
        translated = target switch
        {
            "KO" => $"{site} {actionKo}",
            "JP" => $"{site}{actionJp}",
            _ => $"{site} {actionEn}"
        };
        return true;
    }

    public bool TryTranslateChineseTacticalBriefing(string text, string target, AppSettings settings, out string translated)
    {
        translated = "";
        var trimmed = text.Trim().TrimEnd('.', '!', '?', '。', '！', '？');
        var compact = Regex.Replace(trimmed, @"\s+", "");
        var countLocation = Regex.Match(compact,
            @"^(?<site>[ABC])小(?:(?<count>两|二|三|四|一|\d+)(?:个|人)?|个(?<count2>两|二|三|四|一|\d+))$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (countLocation.Success)
        {
            var site = countLocation.Groups["site"].Value.ToUpperInvariant();
            var countToken = countLocation.Groups["count"].Success
                ? countLocation.Groups["count"].Value
                : countLocation.Groups["count2"].Value;
            var count = NormalizeChineseCount(countToken);
            var location = $"{site} Short";
            translated = target switch
            {
                "KO" => $"{LocalizeCalloutLocation(location, target, settings)} {count}명",
                "JP" => $"{LocalizeCalloutLocation(location, target, settings)}{count}人",
                _ => $"{count} {location}"
            };
            return true;
        }

        var agentLow = Regex.Match(compact,
            @"^(?<agent>[\p{L}\p{N}]+)残血$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (agentLow.Success)
        {
            var agent = CanonicalCharacterName(NormalizeNames(agentLow.Groups["agent"].Value, settings), settings)
                        ?? agentLow.Groups["agent"].Value;
            translated = target switch
            {
                "KO" => $"{agent} 체력 낮음",
                "JP" => $"{agent}ロー",
                _ => $"{agent} low"
            };
            return true;
        }

        return false;
    }

    private static string NormalizeChineseCount(string token) => token switch
    {
        "一" or "1" => "1",
        "两" or "二" or "2" => "2",
        "三" or "3" => "3",
        "四" or "4" => "4",
        _ when int.TryParse(token, out var value) => value.ToString(),
        _ => token
    };

    public bool TryTranslateFlashWait(string text, string target, out string translated)
    {
        translated = "";
        if (!Regex.IsMatch(text.Trim().TrimEnd('.', '!', '。'),
                @"^(?:(?:please\s+)?wait\s+until\s+(?:i\s+)?flash(?:es)?|(?:내가\s+)?섬광\s*(?:을\s+)?쓸?\s*때까지\s*기다려|フラッシュ(?:を)?入れるまで待って)[.!]?$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return false;
        translated = target switch
        {
            "KO" => text.Contains('섬', StringComparison.Ordinal) ? text.Trim().TrimEnd('.', '!', '。') : "섬광 쓸 때까지 기다려",
            "JP" => "フラッシュ入れるまで待って",
            _ => "wait until I flash"
        };
        return true;
    }
}
