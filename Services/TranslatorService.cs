using System.Net.Http.Headers;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Valtrans.Models;

namespace Valtrans.Services;

public sealed class TranslatorService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly HttpClient _localHttp = new() { Timeout = TimeSpan.FromMinutes(2) };
    private readonly GlossaryService _glossary;
    private readonly LocalAiService _localAi;
    private readonly ValtransLiteService _lite;
    private readonly DeepLApiService _deepLApi;
    private readonly object _deepLxCircuitLock = new();
    private DateTimeOffset _deepLxSuspendedUntil;
    private string _deepLxSuspendedReason = "";

    public event EventHandler<TranslationFallbackEventArgs>? LocalFallbackActivated;
    public event EventHandler<HybridRouteEventArgs>? HybridRouteSelected;
    public event EventHandler<TranslationSafetyEventArgs>? TranslationSafetyAdjusted;
    public string LastHybridRoute { get; private set; } = "";

    public TranslatorService(GlossaryService glossary, LocalAiService localAi, ValtransLiteService lite)
    {
        _glossary = glossary;
        _localAi = localAi;
        _lite = lite;
        _deepLApi = new DeepLApiService(_http);
    }

    public async Task<string> TranslateAsync(string text, string targetLanguage, AppSettings settings,
        bool preserveLines = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        text = text.Trim();
        if (text.Length == 0) return text;
        if (!ChatTextSanitizer.HasMeaningfulContent(text)) return text;
        text = ChatTextSanitizer.ConvertCommonRomanizedJapanese(text);
        if (preserveLines && text.Contains('\n'))
        {
            var translatedLines = new List<string>();
            foreach (var line in text.Replace("\r", "").Split('\n'))
                translatedLines.Add(await TranslateAsync(line, targetLanguage, settings, true, cancellationToken));
            return string.Join(Environment.NewLine, translatedLines);
        }
        if (_glossary.TryTranslateExactShortcut(text, targetLanguage, out var slang, settings) ||
            _glossary.TryTranslateStructuredCallout(text, targetLanguage, settings, out slang))
        {
            if (settings.TranslationProvider == "Hybrid") ReportHybridRoute("규칙", "FPS 약어·은어·콜아웃");
            return BriefingTranslationGuard.Apply(text, slang, targetLanguage);
        }
        if (DetectSourceLanguage(text).Equals(targetLanguage, StringComparison.OrdinalIgnoreCase)) return text;
        string result;
        if (settings.TranslationProvider.Equals("Hybrid", StringComparison.OrdinalIgnoreCase))
            result = await TranslateWithHybridAsync(text, targetLanguage, settings, preserveLines, cancellationToken);
        else if (!preserveLines && _glossary.TryTranslateExactShortcut(text, targetLanguage, out var shortcutTranslation))
            result = shortcutTranslation;
        else if (!preserveLines && _glossary.TryTranslateStructuredCallout(text, targetLanguage, settings, out var structuredCallout))
            result = BriefingTranslationGuard.Apply(text, structuredCallout, targetLanguage);
        else if (settings.TranslationProvider.Equals("Lite", StringComparison.OrdinalIgnoreCase))
            result = await TranslateWithLiteAsync(text, targetLanguage, settings, preserveLines, cancellationToken);
        else if (settings.TranslationProvider.Equals("DeepL", StringComparison.OrdinalIgnoreCase))
            result = Clean(await _deepLApi.TranslateAsync(text, targetLanguage, settings.DeepLApiKey, cancellationToken), preserveLines);
        else if (settings.TranslationProvider.Equals("DeepLX", StringComparison.OrdinalIgnoreCase))
        {
            if (TryGetDeepLxCircuit(out var remaining, out var reason))
                result = await TranslateWithLocalFallbackAsync(text, targetLanguage, settings, preserveLines,
                    $"DLX 요청 일시 중지 · {FormatRemaining(remaining)} 후 재시도 · {reason}", cancellationToken);
            else try
            {
                result = await TranslateWithDeepLxAsync(text, targetLanguage, settings, preserveLines, cancellationToken);
            }
            catch (DeepLxRequestException ex) when (ex.IsTransient)
            {
                var cooldown = OpenDeepLxCircuit(ex);
                result = await TranslateWithLocalFallbackAsync(text, targetLanguage, settings, preserveLines,
                    $"DLX 제한 감지 · {FormatRemaining(cooldown)} 동안 로컬 사용", cancellationToken, ex);
            }
        }
        else if (settings.TranslationProvider is "Ollama" or "OpenAI")
            result = await TranslateWithChatModelAsync(text, targetLanguage, settings, preserveLines, cancellationToken);
        else
            throw new InvalidOperationException("지원하지 않는 번역 엔진입니다. 무료 로컬 엔진을 선택해 주세요.");

        var guarded = TranslationFactGuard.Apply(text, result, targetLanguage, settings, _glossary);
        if (guarded.Adjusted)
        {
            try { TranslationSafetyAdjusted?.Invoke(this, new TranslationSafetyEventArgs(guarded.Reason)); }
            catch { }
        }
        return guarded.Text;
    }

    public async Task<EngineCompatibilityReport> TestCompatibilityAsync(AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        var engines = settings.TranslationProvider.Equals("Hybrid", StringComparison.OrdinalIgnoreCase)
            ? new[] { "Lite", "Ollama" }
            : new[] { settings.TranslationProvider };
        var probes = new[]
        {
            new CompatibilityProbe("방향·인원·불확실성", "왼쪽에 아마 두 명 있는 것 같아", "EN"),
            new CompatibilityProbe("부정·방향", "右には誰もいないと思います", "KO")
        };
        var results = new List<EngineCompatibilityResult>();

        foreach (var engine in engines)
        {
            foreach (var probe in probes)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    var raw = await TranslateDirectAsync(engine, probe.Source, probe.TargetLanguage, settings,
                        cancellationToken);
                    watch.Stop();
                    if (!ChatTextSanitizer.HasMeaningfulContent(raw))
                        throw new InvalidOperationException("유효한 번역 결과가 없습니다.");
                    var briefingGuarded = BriefingTranslationGuard.Apply(probe.Source, raw, probe.TargetLanguage);
                    var factGuarded = TranslationFactGuard.Apply(probe.Source, briefingGuarded,
                        probe.TargetLanguage, settings, _glossary);
                    var adjusted = factGuarded.Adjusted ||
                                   !NormalizeForComparison(raw).Equals(NormalizeForComparison(briefingGuarded),
                                       StringComparison.OrdinalIgnoreCase);
                    results.Add(new EngineCompatibilityResult(EngineDisplayName(engine, settings), probe.Name,
                        true, adjusted, watch.ElapsedMilliseconds, adjusted ? "안전 보정 필요" : "원문 사실 보존"));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    watch.Stop();
                    results.Add(new EngineCompatibilityResult(EngineDisplayName(engine, settings), probe.Name,
                        false, false, watch.ElapsedMilliseconds, FriendlyCompatibilityError(ex)));
                }
            }
        }

        return new EngineCompatibilityReport(results);
    }

    private async Task<string> TranslateDirectAsync(string engine, string source, string targetLanguage,
        AppSettings settings, CancellationToken cancellationToken)
    {
        if (engine.Equals("Lite", StringComparison.OrdinalIgnoreCase))
        {
            var normalized = ChatTextSanitizer.ConvertCommonRomanizedJapanese(
                _glossary.PrepareForLocalTranslation(source, settings));
            var sourceLanguage = DetectSourceLanguage(normalized);
            var result = await _lite.TranslateAsync(new[] { normalized }, sourceLanguage, targetLanguage,
                cancellationToken);
            return Clean(LiteTranslationGuard.Validate(source, result.FirstOrDefault() ?? "", targetLanguage,
                settings, _glossary), false);
        }
        if (engine.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
            return await TranslateWithChatModelAsync(source, targetLanguage, settings, false, cancellationToken,
                forceLocal: true, localModel: settings.LocalAiModel);
        if (engine.Equals("DeepLX", StringComparison.OrdinalIgnoreCase))
            return await TranslateWithDeepLxAsync(source, targetLanguage, settings, false, cancellationToken);
        if (engine.Equals("DeepL", StringComparison.OrdinalIgnoreCase))
            return await _deepLApi.TranslateAsync(source, targetLanguage, settings.DeepLApiKey, cancellationToken);
        return await TranslateWithChatModelAsync(source, targetLanguage, settings, false, cancellationToken);
    }

    private static string EngineDisplayName(string engine, AppSettings settings) => engine switch
    {
        "Lite" => "Valtrans Lite",
        "Ollama" => LocalAiService.GetModel(settings.LocalAiModel).DisplayName.Split('·')[0].Trim(),
        "DeepLX" => "DLX",
        "DeepL" => "DeepL 공식 API",
        "OpenAI" => "GPT-4o mini",
        _ => engine
    };

    private static string NormalizeForComparison(string value) => Regex.Replace(value, @"\s+", " ").Trim();

    private static string FriendlyCompatibilityError(Exception error) => error switch
    {
        OperationCanceledException => "검사 시간 초과",
        _ when error.Message.Contains("비어", StringComparison.OrdinalIgnoreCase) => "빈 결과",
        _ => error.GetType().Name.Replace("Exception", "", StringComparison.OrdinalIgnoreCase)
    };

    private async Task<string> TranslateWithChatModelAsync(string text, string targetLanguage, AppSettings settings,
        bool preserveLines, CancellationToken cancellationToken, bool forceLocal = false, string? localModel = null)
    {
        var useLocal = forceLocal || settings.TranslationProvider.Equals("Ollama", StringComparison.OrdinalIgnoreCase);
        var baseUrl = useLocal ? LocalAiService.OpenAiBaseUrl : "https://api.openai.com/v1";
        var model = useLocal ? LocalAiService.NormalizeModelName(localModel ?? settings.LocalAiModel) : "gpt-4o-mini";
        if (!useLocal && string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException("먼저 번역 API 키를 저장해 주세요.");

        // Do not pre-expand ambiguous words or replace quantities with opaque placeholders.
        // The model needs the original sentence to preserve relationships and ordinary meanings.
        var normalized = text;
        var targetName = targetLanguage switch { "KO" => "Korean", "JP" => "Japanese", _ => "English" };
        var game = settings.Game == "Auto" ? "VALORANT or Apex Legends" : settings.Game;
        var map = settings.Map == "Auto" ? "an unspecified map" : settings.Map;
        var lineRule = preserveLines
            ? "Keep the same line count and order. Return only translated lines."
            : "Return exactly one short line with no explanation or quotation marks.";
        var fpsStyle = FpsStyleExamples(targetLanguage);
        var system = $"""
            You translate live FPS game chat into {targetName} for {game} on {map}.
            Server region hint: {GameTranslationPrompt.NormalizeRegion(settings.ServerRegion)}; never use this to override the sentence's meaning.
            Compress messages into short, natural FPS callouts. Remove subjects, politeness, filler, and sentence endings when the intent stays clear.
            Romanized Japanese such as 'wakarimashita', 'daijoubu', or 'teki middo' is Japanese and must be translated by meaning, not copied phonetically.
            Preserve numbers, pings, map labels, and official English character names.
            Understand FPS shorthand and intent; 'mid' is a map position. Prefer established gamer slang over literal prose.
            Never add information. Preserve all meaning before shortening; there is no character limit. {lineRule}
            {BriefingTranslationGuard.PromptRules}
            Style examples: {fpsStyle}
            Glossary: {_glossary.BuildRelevantPromptGlossary(text, targetLanguage, settings)}
            """;

        object[] messages;
        if (useLocal)
        {
            var sourceCode = DetectSourceLanguage(text);
            var (sourceName, sourceTag) = LanguageDetails(sourceCode);
            var (_, targetTag) = LanguageDetails(targetLanguage);
            string localPrompt;
            if (LocalAiService.IsHyMtModel(model) || model.StartsWith("qwen3:", StringComparison.OrdinalIgnoreCase))
            {
                localPrompt = (model.StartsWith("qwen3:", StringComparison.OrdinalIgnoreCase) ? "/no_think\n" : "") +
                    GameTranslationPrompt.Build(text, targetLanguage, settings, _glossary, preserveLines);
            }
            else
            {
                localPrompt = $"""
                    You are a professional {sourceName} ({sourceTag}) to {targetName} ({targetTag}) translator. Your goal is to accurately convey the meaning and nuances of the original {sourceName} text while adhering to {targetName} grammar, vocabulary, and cultural sensitivities.
                    Context: {game}, map {map}, server region hint {GameTranslationPrompt.NormalizeRegion(settings.ServerRegion)}. Natural team chat, not a summary. Preserve meaning, negation, conditions and quantities. {lineRule}
                    Reference terminology: {_glossary.BuildRelevantPromptGlossary(text, targetLanguage, settings)}
                    Produce only the {targetName} translation, without any additional explanations or commentary. Please translate the following {sourceName} text into {targetName}:


                    {normalized}
                    """;
            }
            messages = new object[] { new { role = "user", content = localPrompt } };
        }
        else
        {
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = normalized }
            };
        }

        if (useLocal && (model.StartsWith("qwen3:", StringComparison.OrdinalIgnoreCase) ||
                         LocalAiService.IsHyMtModel(model)))
        {
            var isHyMt = LocalAiService.IsHyMtModel(model);
            object options = isHyMt
                ? new { temperature = 0.0, top_p = 0.6, top_k = 20, repeat_penalty = 1.05, num_predict = 384 }
                : new { temperature = 0.0, num_predict = 384 };
            var nativePayload = new
            {
                model,
                stream = false,
                think = false,
                keep_alive = -1,
                options,
                messages
            };
            using var nativeRequest = new HttpRequestMessage(HttpMethod.Post, LocalAiService.BaseUrl + "/api/chat")
            {
                Content = new StringContent(JsonSerializer.Serialize(nativePayload), Encoding.UTF8, "application/json")
            };
            using var nativeResponse = await _localHttp.SendAsync(nativeRequest, cancellationToken);
            var nativeJson = await nativeResponse.Content.ReadAsStringAsync(cancellationToken);
            if (!nativeResponse.IsSuccessStatusCode)
                throw new InvalidOperationException(TryReadError(nativeJson) ??
                                                    $"로컬 번역 서버 오류 ({(int)nativeResponse.StatusCode})");
            using var nativeDocument = JsonDocument.Parse(nativeJson);
            if (nativeDocument.RootElement.TryGetProperty("done_reason", out var doneReason) &&
                doneReason.GetString() == "length")
                throw new InvalidOperationException("번역문 생성이 중간에 잘렸습니다. 문장을 나누어 다시 시도해 주세요.");
            var nativeResult = nativeDocument.RootElement.TryGetProperty("message", out var nativeMessage) &&
                               nativeMessage.TryGetProperty("content", out var nativeContent)
                ? nativeContent.GetString()?.Trim()
                : null;
            if (string.IsNullOrWhiteSpace(nativeResult))
                throw new InvalidOperationException($"{LocalAiService.GetModel(model).DisplayName} 번역 결과가 비어 있습니다. 모델을 다시 예열해 주세요.");
            if (!ChatTextSanitizer.HasMeaningfulContent(nativeResult))
                throw new InvalidOperationException($"{LocalAiService.GetModel(model).DisplayName}가 유효한 번역문을 만들지 못했습니다. 다시 시도해 주세요.");
            return Clean(nativeResult, preserveLines);
        }

        var payload = new { model, temperature = 0.0, max_tokens = 384, messages };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/chat/completions");
        if (!useLocal && !string.IsNullOrWhiteSpace(settings.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await (useLocal ? _localHttp : _http).SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var message = TryReadError(json) ?? $"번역 서버 오류 ({(int)response.StatusCode})";
            throw new InvalidOperationException(message);
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.GetProperty("choices")[0].TryGetProperty("finish_reason", out var finishReason) &&
            finishReason.GetString() == "length")
            throw new InvalidOperationException("번역문 생성이 중간에 잘렸습니다. 문장을 나누어 다시 시도해 주세요.");
        var result = document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(result)) throw new InvalidOperationException("번역 결과가 비어 있습니다.");
        if (!ChatTextSanitizer.HasMeaningfulContent(result))
            throw new InvalidOperationException("유효한 번역문을 만들지 못했습니다. 다시 시도해 주세요.");
        return Clean(result, preserveLines);
    }

    private async Task<string> TranslateWithHybridAsync(string text, string targetLanguage, AppSettings settings,
        bool preserveLines, CancellationToken cancellationToken)
    {
        if (!preserveLines)
            return await TranslateHybridLineAsync(text, targetLanguage, settings, preferLocal: true, cancellationToken);

        var lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
        var output = new List<string>(lines.Length);
        foreach (var line in lines)
            output.Add(await TranslateHybridLineAsync(line, targetLanguage, settings,
                preferLocal: true, cancellationToken));
        return string.Join(Environment.NewLine, output);
    }

    private async Task<string> TranslateHybridLineAsync(string text, string targetLanguage, AppSettings settings,
        bool preferLocal, CancellationToken cancellationToken)
    {
        if (DetectSourceLanguage(text).Equals(targetLanguage, StringComparison.OrdinalIgnoreCase)) return text;
        if (_glossary.TryTranslateExactShortcut(text, targetLanguage, out var exact))
        {
            ReportHybridRoute("규칙", "공통 채팅·FPS 사전");
            return exact;
        }
        if (_glossary.TryTranslateStructuredCallout(text, targetLanguage, settings, out var callout))
        {
            ReportHybridRoute("규칙", "콜아웃 구조화");
            return BriefingTranslationGuard.Apply(text, callout, targetLanguage);
        }

        var localModel = LocalAiService.NormalizeModelName(settings.LocalAiModel);
        var localDisplayName = LocalAiService.GetModel(localModel).DisplayName.Split('·')[0].Trim();
        Exception? localError = null;
        Exception? liteError = null;

        if (preferLocal)
        {
            try
            {
                var result = await TranslateWithChatModelAsync(text, targetLanguage, settings, false,
                    cancellationToken, forceLocal: true, localModel: localModel);
                ReportHybridRoute(localDisplayName, "번역 특화 로컬 모델");
                return result;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                localError = ex;
            }
        }

        try
        {
            var liteResult = await TranslateWithLiteAsync(text, targetLanguage, settings, false, cancellationToken);
            ReportHybridRoute(preferLocal ? "Lite 대체" : "Lite", "기본 이상 징후 검사 통과 · 정확도 보장 아님");
            return liteResult;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            liteError = ex;
        }

        if (!preferLocal)
        {
            try
            {
                var refined = await TranslateWithChatModelAsync(text, targetLanguage, settings, false,
                    cancellationToken, forceLocal: true, localModel: localModel);
                ReportHybridRoute(preferLocal ? localDisplayName : $"{localDisplayName} 보정", "Lite 결과 보정");
                return refined;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                localError = ex;
            }
        }

        throw new InvalidOperationException(
            $"스마트 복합 번역 실패 · {localDisplayName}: {localError?.Message ?? "사용 불가"} · Lite: {liteError?.Message ?? "사용 불가"}");
    }

    private static bool ShouldPreferLocalModel(string text)
    {
        var body = ChatTextSanitizer.ContentForLanguageDetection(text);
        if (ChatTextSanitizer.LooksLikeRomanizedJapanese(body)) return true;
        if (body.Length >= 24 || body.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 7) return true;
        var hasLatin = body.Any(ch => ch is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z'));
        var hasHangul = body.Any(ch => ch is >= '\uAC00' and <= '\uD7AF');
        var hasJapanese = body.Any(ch => ch is (>= '\u3040' and <= '\u30FF') or (>= '\u4E00' and <= '\u9FFF'));
        if ((hasLatin ? 1 : 0) + (hasHangul ? 1 : 0) + (hasJapanese ? 1 : 0) >= 2) return true;
        return Regex.IsMatch(body,
            @"(?ix)(?:\?|왜|어떻게|부탁|같아|거야|아마|추정|하지\s*마|없어|でしょう|です|ます|かも|しない|いない|maybe|probably|don['’]?t|no\s+one)");
    }

    private void ReportHybridRoute(string route, string reason)
    {
        LastHybridRoute = route;
        try { HybridRouteSelected?.Invoke(this, new HybridRouteEventArgs(route, reason)); }
        catch { /* 상태 표시 구독자의 실패가 번역 결과를 취소하지 않게 합니다. */ }
    }

    private async Task<string> TranslateWithLiteAsync(string text, string targetLanguage, AppSettings settings,
        bool preserveLines, CancellationToken cancellationToken)
    {
        var source = DetectSourceLanguage(text);
        var normalized = _glossary.PrepareForLocalTranslation(text, settings);
        normalized = ChatTextSanitizer.ConvertCommonRomanizedJapanese(normalized);
        if (source.Equals(targetLanguage, StringComparison.OrdinalIgnoreCase)) return text;
        if (!preserveLines && _glossary.TryTranslateStructuredCallout(text, targetLanguage, settings, out var callout))
            return BriefingTranslationGuard.Apply(text, callout, targetLanguage);
        var lines = preserveLines
            ? normalized.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
            : new[] { normalized };
        IReadOnlyList<string> translated;
        if (preserveLines)
        {
            var originalLines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
            var output = new string[lines.Length];
            var pending = new List<string>();
            var pendingIndexes = new List<int>();
            for (var index = 0; index < lines.Length; index++)
            {
                var original = index < originalLines.Length ? originalLines[index] : lines[index];
                if (_glossary.TryTranslateExactShortcut(original.Trim(), targetLanguage, out var shortcut) ||
                    _glossary.TryTranslateStructuredCallout(original, targetLanguage, settings, out shortcut))
                {
                    output[index] = shortcut;
                    continue;
                }
                pending.Add(lines[index]);
                pendingIndexes.Add(index);
            }
            if (pending.Count > 0)
            {
                var modelResults = await _lite.TranslateAsync(pending, source, targetLanguage, cancellationToken);
                for (var index = 0; index < pendingIndexes.Count; index++)
                    output[pendingIndexes[index]] = modelResults[index];
            }
            translated = output;
        }
        else
        {
            translated = await _lite.TranslateAsync(lines, source, targetLanguage, cancellationToken);
        }
        var result = preserveLines ? string.Join(Environment.NewLine, translated) : translated.FirstOrDefault() ?? "";
        // Do not manufacture a negation/uncertainty prefix around a faulty Lite result.
        return Clean(LiteTranslationGuard.Validate(text, result, targetLanguage, settings, _glossary), preserveLines);
    }

    public async Task TestOpenAiConnectionAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("API 키를 먼저 입력해 주세요.");
        var payload = new
        {
            model = "gpt-4o-mini",
            temperature = 0,
            max_tokens = 3,
            messages = new[] { new { role = "user", content = "Reply only OK" } }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode) return;
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException(TryReadError(json) ?? $"OpenAI 연결 오류 ({(int)response.StatusCode})");
    }

    public Task<string> CheckDeepLApiUsageAsync(string key, CancellationToken cancellationToken = default) =>
        _deepLApi.CheckUsageAsync(key, cancellationToken);

    public async Task TestDeepLxConnectionAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        if (TryGetDeepLxCircuit(out var remaining, out var reason))
            throw new InvalidOperationException($"DLX 요청을 잠시 쉬는 중입니다. {FormatRemaining(remaining)} 후 다시 확인해 주세요. ({reason})");
        var settings = new AppSettings { TranslationProvider = "DeepLX", DeepLxUrl = endpoint };
        try
        {
            var result = await TranslateWithDeepLxAsync("hello", "KO", settings, preserveLines: false, cancellationToken);
            if (string.IsNullOrWhiteSpace(result)) throw new InvalidOperationException("DeepLX 테스트 결과가 비어 있습니다.");
        }
        catch (DeepLxRequestException ex) when (ex.IsTransient)
        {
            var cooldown = OpenDeepLxCircuit(ex);
            throw new InvalidOperationException($"DLX 익명 요청이 제한됐습니다. {FormatRemaining(cooldown)} 동안 재요청하지 않습니다.", ex);
        }
    }

    private async Task<string> TranslateWithDeepLxAsync(string text, string targetLanguage, AppSettings settings,
        bool preserveLines, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(settings.DeepLxUrl?.Trim(), UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("올바른 DeepLX 주소를 입력해 주세요.");

        var normalized = _glossary.PrepareForLocalTranslation(text, settings);
        normalized = ChatTextSanitizer.ConvertCommonRomanizedJapanese(normalized);
        var source = DetectSourceLanguage(text) switch { "JP" => "JA", var code => code };
        var target = targetLanguage == "JP" ? "JA" : targetLanguage;
        var payload = new { text = normalized, source_lang = source, target_lang = target };
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DeepLxRequestException("DLX 응답 시간이 초과됐습니다.");
        }
        catch (HttpRequestException ex)
        {
            throw new DeepLxRequestException("DLX 서버에 연결하지 못했습니다.", null, ex);
        }
        using (response)
        {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new DeepLxRequestException(TryReadDeepLxError(json) ?? $"DLX 오류 ({(int)response.StatusCode})",
                (int)response.StatusCode);

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.TryGetProperty("code", out var code) && code.GetInt32() != 200)
                throw new DeepLxRequestException(TryReadDeepLxError(json) ?? "DLX 번역에 실패했습니다.", code.GetInt32());
            var result = root.TryGetProperty("data", out var data) ? data.GetString()?.Trim() : null;
            if (string.IsNullOrWhiteSpace(result)) throw new InvalidOperationException("DeepLX 번역 결과가 비어 있습니다.");
            return BriefingTranslationGuard.Apply(text, Clean(result, preserveLines), targetLanguage);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("DeepLX 응답 형식이 올바르지 않습니다.");
        }
        }
    }

    private async Task<string> TranslateWithLocalFallbackAsync(string text, string targetLanguage,
        AppSettings settings, bool preserveLines, string notice, CancellationToken cancellationToken,
        Exception? deepLxError = null)
    {
        if (!settings.DeepLxAutoFallback)
            throw deepLxError ?? new InvalidOperationException(notice);

        var modelName = LocalAiService.NormalizeModelName(settings.LocalAiModel);
        var status = await _localAi.GetStatusAsync(modelName, cancellationToken);
        if (status.OllamaInstalled && (!status.Ready || !status.ModelLoaded))
        {
            await _localAi.WarmUpAsync(modelName, cancellationToken: cancellationToken);
            status = await _localAi.GetStatusAsync(modelName, cancellationToken);
        }
        if (!status.Ready)
            throw new InvalidOperationException($"{notice}. 선택한 로컬 대체 모델이 준비되지 않았습니다.", deepLxError);

        var fallbackSettings = CloneForLocal(settings, modelName);
        LocalFallbackActivated?.Invoke(this,
            new TranslationFallbackEventArgs(notice, LocalAiService.GetModel(modelName).DisplayName));
        return await TranslateAsync(text, targetLanguage, fallbackSettings, preserveLines, cancellationToken);
    }

    private static AppSettings CloneForLocal(AppSettings source, string modelName) => new()
    {
        TranslationProvider = "Ollama",
        ApiBaseUrl = LocalAiService.OpenAiBaseUrl,
        Model = modelName,
        LocalAiModel = modelName,
        Game = source.Game,
        Map = source.Map,
        CustomGlossary = new Dictionary<string, string>(source.CustomGlossary, StringComparer.OrdinalIgnoreCase)
    };

    private TimeSpan OpenDeepLxCircuit(DeepLxRequestException error)
    {
        var cooldown = error.StatusCode is 429 or 403
            ? TimeSpan.FromMinutes(30)
            : error.StatusCode is >= 500
                ? TimeSpan.FromMinutes(2)
                : TimeSpan.FromMinutes(1);
        lock (_deepLxCircuitLock)
        {
            _deepLxSuspendedUntil = DateTimeOffset.UtcNow.Add(cooldown);
            _deepLxSuspendedReason = error.StatusCode is 429 or 403
                ? "비공식 익명 요청 제한"
                : error.Message;
        }
        return cooldown;
    }

    private bool TryGetDeepLxCircuit(out TimeSpan remaining, out string reason)
    {
        lock (_deepLxCircuitLock)
        {
            remaining = _deepLxSuspendedUntil - DateTimeOffset.UtcNow;
            reason = _deepLxSuspendedReason;
            if (remaining > TimeSpan.Zero) return true;
            remaining = TimeSpan.Zero;
            _deepLxSuspendedUntil = default;
            _deepLxSuspendedReason = "";
            return false;
        }
    }

    private static string FormatRemaining(TimeSpan value) => value.TotalMinutes >= 2
        ? $"{Math.Ceiling(value.TotalMinutes):0}분"
        : $"{Math.Max(1, Math.Ceiling(value.TotalSeconds)):0}초";

    private static string DetectSourceLanguage(string text)
    {
        text = ChatTextSanitizer.ContentForLanguageDetection(text);
        if (text.Any(ch => ch is >= '\uAC00' and <= '\uD7AF')) return "KO";
        if (text.Any(ch => ch is (>= '\u3040' and <= '\u30FF') or (>= '\u4E00' and <= '\u9FFF'))) return "JP";
        if (ChatTextSanitizer.LooksLikeRomanizedJapanese(text)) return "JP";
        return "EN";
    }

    private static (string Name, string Code) LanguageDetails(string code) => code switch
    {
        "KO" => ("Korean", "ko"),
        "JP" => ("Japanese", "ja"),
        _ => ("English", "en")
    };

    private static string FpsStyleExamples(string targetLanguage) => targetLanguage switch
    {
        "KO" => "There is one enemy at mid → 미드 1명; watch left → 왼쪽 조심; I am rotating to A → A 로테; enemy is very low → 적 딸피; understood/roger → 확인; sorry, my mistake → ㅈㅅ 내 실수",
        "JP" => "There is one enemy at mid → ミッド1; watch left → 左注意; I am rotating to A → Aローテ; enemy is very low → 敵ロー; understood/roger → 了解; sorry, my mistake → ごめん、ミス",
        _ => "미드에 한 명 → 1 mid; 왼쪽 조심해 → watch left; A로 돌아갈게 → rotating A; 적 딸피 → enemy 1 shot; 알겠어 → copy; 내 실수 → mb"
    };

    private static string? TryReadError(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.GetProperty("error").GetProperty("message").GetString();
        }
        catch { return null; }
    }

    private static string? TryReadDeepLxError(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.TryGetProperty("message", out var message)) return message.GetString();
            if (root.TryGetProperty("error", out var error)) return error.GetString();
            return null;
        }
        catch { return null; }
    }

    private static string Clean(string value, bool preserveLines)
    {
        value = Regex.Replace(value, @"<think>.*?</think>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var closingThink = value.LastIndexOf("</think>", StringComparison.OrdinalIgnoreCase);
        if (closingThink >= 0) value = value[(closingThink + "</think>".Length)..];
        value = value.Trim().Trim('"', '`');
        if (preserveLines) return value;
        return string.Join(" ", value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }
}

public sealed class DeepLxRequestException : Exception
{
    public int? StatusCode { get; }
    public bool IsTransient => StatusCode is null or 403 or 408 or 429 or >= 500;

    public DeepLxRequestException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException) => StatusCode = statusCode;
}

public sealed record TranslationFallbackEventArgs(string Notice, string ModelDisplayName);
public sealed record HybridRouteEventArgs(string Route, string Reason);
public sealed record TranslationSafetyEventArgs(string Reason);
public sealed record EngineCompatibilityResult(string Engine, string Probe, bool Success, bool SafetyAdjusted,
    long DurationMs, string Detail);
public sealed record EngineCompatibilityReport(IReadOnlyList<EngineCompatibilityResult> Results)
{
    public int Passed => Results.Count(result => result.Success);
    public int Total => Results.Count;
    public int Adjusted => Results.Count(result => result.Success && result.SafetyAdjusted);
    public bool Success => Total > 0 && Passed == Total;
    public double AverageDurationMs => Results.Count == 0 ? 0 : Results.Average(result => result.DurationMs);
}
file sealed record CompatibilityProbe(string Name, string Source, string TargetLanguage);
