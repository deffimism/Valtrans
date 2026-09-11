using System.Text.RegularExpressions;
using Valtrans.Models;

namespace Valtrans.Services;

public sealed class MessageClassification
{
    public string Type { get; set; } = "Unclassified";
    public string Reason { get; set; } = "";
}

/// <summary>Phase 6: Tactical / Social / System / Noise classification for trace + translation style.</summary>
public sealed class MessageClassifierService
{
    private readonly GameChatFilterService _filter;

    public MessageClassifierService(GameChatFilterService filter) => _filter = filter;

    public MessageClassification Classify(string text, AppSettings settings)
    {
        if (IsSystemMessage(text))
            return new MessageClassification { Type = "System", Reason = "system or match notice" };

        // AllMode returns the "All" category before any tactical/social decision is made,
        // which used to label every non-system line Tactical and apply the strict fact
        // validator to ordinary conversation.
        var result = _filter.Categorize(text, settings);
        return result.Category switch
        {
            "Tactical" => new MessageClassification { Type = "Tactical", Reason = result.Reason },
            "Social" => new MessageClassification { Type = "Social", Reason = result.Reason },
            "Noise" or "LowRelevance" => new MessageClassification { Type = "Noise", Reason = result.Reason },
            _ => new MessageClassification { Type = "Unclassified", Reason = result.Reason }
        };
    }

    private static bool IsSystemMessage(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return false;
        return trimmed.StartsWith("[SYSTEM]", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("SYSTEM:", StringComparison.OrdinalIgnoreCase) ||
               Regex.IsMatch(trimmed, @"^(?:Match|Round|Combat|Buy phase|Spike)", RegexOptions.IgnoreCase);
    }
}
