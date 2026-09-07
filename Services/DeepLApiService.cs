using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Valtrans.Services;

// Official API only. Never falls back to an anonymous/public server.
public sealed class DeepLApiService
{
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _retryAfter;

    public DeepLApiService(HttpClient http) => _http = http;

    private static HttpRequestMessage CreateRequest(string key, HttpMethod method, string path)
    {
        key = key.Trim();
        if (key.Length == 0 || key.Any(char.IsWhiteSpace))
            throw new InvalidOperationException("DeepL 공식 API 키를 입력해 주세요. 로컬 번역에는 키가 필요 없습니다.");
        var host = key.EndsWith(":fx", StringComparison.OrdinalIgnoreCase)
            ? "https://api-free.deepl.com" : "https://api.deepl.com";
        var request = new HttpRequestMessage(method, host + "/v2/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("DeepL-Auth-Key", key);
        return request;
    }

    private async Task<JsonDocument> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (_retryAfter > DateTimeOffset.UtcNow)
                throw new InvalidOperationException("DeepL API 요청 일시 중지 중입니다. 잠시 후 다시 확인하거나 로컬 엔진을 선택해 주세요.");
            using var response = await _http.SendAsync(request, token);
            if (!response.IsSuccessStatusCode)
            {
                var code = (int)response.StatusCode;
                if (code == 429)
                {
                    var retry = response.Headers.RetryAfter;
                    _retryAfter = retry?.Date ?? DateTimeOffset.UtcNow.Add(retry?.Delta ?? TimeSpan.FromMinutes(1));
                    if (_retryAfter <= DateTimeOffset.UtcNow) _retryAfter = DateTimeOffset.UtcNow.AddMinutes(1);
                }
                // Do not surface raw upstream bodies, which can contain personal data.
                throw new InvalidOperationException(code switch
                {
                    403 => "DeepL API 인증 실패 (403). API 전용 키와 계정 상태를 확인해 주세요.",
                    429 => "DeepL API 요청 제한 (429). 자동 재전송하지 않습니다. 로컬 엔진을 사용할 수 있습니다.",
                    456 => "DeepL API 사용 한도 초과 (456). 로컬 엔진을 선택해 주세요. 유료 전환은 자동으로 하지 않습니다.",
                    >= 500 => $"DeepL API 서비스 오류 ({code}). 잠시 후 다시 확인해 주세요.",
                    _ => $"DeepL API 요청 실패 ({code}). 키와 번역 언어를 확인해 주세요."
                });
            }
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new InvalidOperationException("DeepL API 응답 시간 초과. 번역 요청은 자동 재전송하지 않습니다."); }
        catch (HttpRequestException)
        { throw new InvalidOperationException("DeepL API에 연결하지 못했습니다. 인터넷 연결을 확인해 주세요."); }
        catch (JsonException)
        { throw new InvalidOperationException("DeepL API 응답 형식이 올바르지 않습니다."); }
        finally { _gate.Release(); }
    }

    public async Task<string> TranslateAsync(string text, string target, string key, CancellationToken token = default)
    {
        using var request = CreateRequest(key, HttpMethod.Post, "translate");
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            text = new[] { text }, target_lang = target == "JP" ? "JA" : target,
            preserve_formatting = true
        }), Encoding.UTF8, "application/json");
        using var json = await SendAsync(request, token);
        if (json.RootElement.TryGetProperty("translations", out var list) && list.ValueKind == JsonValueKind.Array &&
            list.GetArrayLength() == 1 && list[0].TryGetProperty("text", out var value) && value.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value.GetString())) return value.GetString()!.Trim();
        throw new InvalidOperationException("DeepL API 번역 결과가 비어 있거나 올바르지 않습니다.");
    }

    public async Task<string> CheckUsageAsync(string key, CancellationToken token = default)
    {
        using var request = CreateRequest(key, HttpMethod.Get, "usage");
        using var json = await SendAsync(request, token);
        var root = json.RootElement;
        if (root.TryGetProperty("character_count", out var count) && count.TryGetInt64(out var used) &&
            root.TryGetProperty("character_limit", out var limit) && limit.TryGetInt64(out var maximum))
            return $"인증 확인 완료 · {used:N0} / {maximum:N0}자 사용 · 번역 요청 없음";
        throw new InvalidOperationException("DeepL API 사용량 응답을 확인할 수 없습니다.");
    }
}
