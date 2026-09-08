namespace Valtrans.Services;

public static class OcrMessageParser
{
    // Keep row boundaries. Only attach a continuation when geometry proves it is
    // indented under a recognized message, not merely because adjacent text exists.
    public static IReadOnlyList<OcrMessage> Extract(OcrReadResult result)
    {
        var rows = result.PositionedLines is { Count: > 0 }
            ? result.PositionedLines.OrderBy(line => line.Y).ToArray()
            : result.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(text => new OcrPositionedLine(text, 0, -1, 0, 0, Array.Empty<OcrPositionedWord>())).ToArray();
        var messages = new List<OcrMessage>();
        OcrPositionedLine? previous = null;
        var previousSystem = false;
        foreach (var row in rows)
        {
            var system = ChatTextSanitizer.IsSystemMessage(row.Text);
            var body = ChatTextSanitizer.NormalizeOcrBody(row.Text);
            var header = ChatTextSanitizer.HasChatChannel(row.Text) ||
                !ChatTextSanitizer.StripChatPrefix(row.Text).Equals(row.Text.Trim(), StringComparison.Ordinal);
            var nearPrevious = previous is not null && row.Y >= 0 && previous.Y >= 0 &&
                row.Y >= previous.Y + previous.Height * 0.6 &&
                row.Y - previous.Y - previous.Height <= Math.Max(6, previous.Height * 0.65);
            if (system || (previousSystem && !header && (nearPrevious || row.Y < 0)))
            {
                previousSystem = true;
                previous = row;
                continue;
            }
            previousSystem = false;
            if (body.Length > 0)
            {
                if (nearPrevious && !header && messages.Count > 0 && previous is not null &&
                    row.X > previous.X + previous.Height * 1.5 &&
                    ChatTextSanitizer.HasChatChannel(previous.Text))
                {
                    var last = messages[^1];
                    messages[^1] = last with { Body = last.Body + " " + body,
                        Height = row.Y + row.Height - last.Y };
                }
                else messages.Add(new OcrMessage(body, row.Y, row.Height));
            }
            previous = row;
        }
        return messages;
    }
}

public sealed record OcrMessage(string Body, int Y, int Height);
