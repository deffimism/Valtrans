using System.IO;
using System.Text.Json;

namespace Valtrans.Services;

internal static class LiteResponseReader
{
    internal static async Task<JsonDocument> ReadAsync(StreamReader reader, string requestId, CancellationToken token)
    {
        // Ignore well-formed responses belonging to older requests; never attach them to a new chat.
        for (var stale = 0; stale < 32; stale++)
        {
            var line = await reader.ReadLineAsync(token);
            if (string.IsNullOrWhiteSpace(line)) throw new IOException("Lite 응답 스트림이 닫혔습니다.");
            var response = JsonDocument.Parse(line);
            if (!response.RootElement.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String)
            {
                response.Dispose();
                throw new IOException("Lite 응답에 요청 식별자가 없습니다.");
            }
            if (id.GetString() == requestId) return response;
            response.Dispose();
        }
        throw new IOException("Lite 응답 식별자가 반복해서 일치하지 않습니다.");
    }
}
