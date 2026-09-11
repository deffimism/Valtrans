using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Valtrans.Models;

namespace Valtrans.Services;

public sealed class TranslatorService
{
    private readonly HttpClient _localHttp = new() { Timeout = TimeSpan.FromMinutes(2) };
    private readonly GlossaryService _glossary;
    private readonly LocalAiService _localAi;
    private readonly ValtransLiteService _lite;

    public event EventHandler<HybridRouteEventArgs>? HybridRouteSelected;
    public event EventHandler<TranslationSafetyEventArgs>? TranslationSafetyAdjusted;
    public string LastHybridRoute { get; private set; } = "";
    public LitePivotMetrics? LastLitePivotMetrics { get; private set; }

    public TranslatorService(GlossaryService glossary, LocalAiService localAi, ValtransLiteService lite)
    {
        _glossary = glossary;
        _localAi = localAi;
        _lite = lite;
    }

    public async Task<string> TranslateAsync(string text, string targetLanguage, AppSettings settings,
        bool preserveLines = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        text = text.Trim();
        if (text.Length == 0) return text;
        text = preserveLines && text.Contains('\n')
            ? ChatTextSanitizer.ContentForLanguageDetection(text)
            : ChatTextSanitizer.StripChatPrefix(text).Trim();
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
        else if (settings.TranslationProvider == "Ollama")
            result = await TranslateWithChatModelAsync(text, targetLanguage, settings, preserveLines, cancellationToken);
        else
            throw new InvalidOperationException("지원하지 않는 번역 엔진입니다. 무료 로컬 엔진을 선택해 주세요.");

        result = BriefingTranslationGuard.CompactCommonCallout(result, targetLanguage);
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
                    var briefingGuarded = BriefingTranslationGuard.CompactCommonCallout(raw, probe.TargetLanguage);
                    var factGuarded = TranslationFactGuard.Apply(probe.Source, briefingGuarded,
                        probe.TargetLanguage, settings, _glossary);
                    var adjusted = factGuarded.Adjusted ||
                                   !NormalizeForComparison(raw).Equals(NormalizeForComparison(briefingGuarded),
                                       StringComparison.OrdinalIgnoreCase);
                    var planned = engine == "Lite" && LiteUncertaintyPlan.Create(probe.Source, settings, _glossary) is not null;
                    results.Add(new EngineCompatibilityResult(EngineDisplayName(engine, settings), probe.Name,
                        true, adjusted || planned, watch.ElapsedMilliseconds,
                        planned ? "추정 분리·중간 번역 보존 검사 통과" : adjusted ? "콜아웃 보정 · 기본 검사 통과" : "응답·기본 보존 검사 통과"));
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
            var result = await TranslateLiteBatchAsync(new[] { source }, new[] { normalized }, sourceLanguage,
                targetLanguage, settings, cancellationToken);
            return Clean(LiteTranslationGuard.Validate(source, result.FirstOrDefault() ?? "", targetLanguage,
                settings, _glossary), false);
        }
        if (engine.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
            return await TranslateWithChatModelAsync(source, targetLanguage, settings, false, cancellationToken,
                localModel: settings.LocalAiModel);
        throw new InvalidOperationException("로컬 번역 엔진을 선택해 주세요.");
    }

    private static string EngineDisplayName(string engine, AppSettings settings) => engine switch
    {
        "Lite" => "Valtrans Lite",
        "Ollama" => LocalAiService.GetModel(settings.LocalAiModel).DisplayName.Split('·')[0].Trim(),
        _ => engine
    };

    private static string NormalizeForComparison(string value) => Regex.Replace(value, @"\s+", " ").Trim();

    private static string FriendlyCompatibilityError(Exception error) => error switch
    {
        OperationCanceledException or TimeoutException => "검사 시간 초과",
        _ when error.Message.StartsWith("Lite 번역 확인 필요 · ", StringComparison.Ordinal) =>
            "품질 검사 보류 · " + error.Message.Split(" · ")[1],
        _ when error.Message.Contains("비어", StringComparison.OrdinalIgnoreCase) => "빈 결과",
        _ => error.GetType().Name.Replace("Exception", "", StringComparison.OrdinalIgnoreCase)
    };

    private async Task<string> TranslateWithChatModelAsync(string text, string targetLanguage, AppSettings settings,
        bool preserveLines, CancellationToken cancellationToken, string? localModel = null)
    {
        var baseUrl = LocalAiService.CompletionBaseUrl;
        var model = LocalAiService.NormalizeModelName(localModel ?? settings.LocalAiModel);

        var localPrompt = (model.StartsWith("qwen3:", StringComparison.OrdinalIgnoreCase) ? "/no_think\n" : "") +
            GameTranslationPrompt.Build(text, targetLanguage, settings, _glossary, preserveLines);
        var messages = new object[] { new { role = "user", content = localPrompt } };
        if (model.StartsWith("qwen3:", StringComparison.OrdinalIgnoreCase) ||
            LocalAiService.IsHyMtModel(model))
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
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await _localHttp.SendAsync(request, cancellationToken);
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
            return await TranslateHybridLineAsync(text, targetLanguage, settings, cancellationToken);

        var lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
        var output = new List<string>(lines.Length);
        foreach (var line in lines)
            output.Add(await TranslateHybridLineAsync(line, targetLanguage, settings, cancellationToken));
        return string.Join(Environment.NewLine, output);
    }

    // Rules first, then the local translation model, then Valtrans Lite as a fallback.
    private async Task<string> TranslateHybridLineAsync(string text, string targetLanguage, AppSettings settings,
        CancellationToken cancellationToken)
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

        try
        {
            var result = await TranslateWithChatModelAsync(text, targetLanguage, settings, false,
                cancellationToken, localModel: localModel);
            ReportHybridRoute(localDisplayName, "번역 특화 로컬 모델");
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            localError = ex;
        }

        try
        {
            var liteResult = await TranslateWithLiteAsync(text, targetLanguage, settings, false, cancellationToken);
            ReportHybridRoute("Lite 대체", "기본 이상 징후 검사 통과 · 정확도 보장 아님");
            return liteResult;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            liteError = ex;
        }

        throw new InvalidOperationException(
            $"스마트 복합 번역 실패 · {localDisplayName}: {localError?.Message ?? "사용 불가"} · Lite: {liteError?.Message ?? "사용 불가"}");
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
            var pendingOriginals = new List<string>();
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
                pendingOriginals.Add(original);
                pendingIndexes.Add(index);
            }
            if (pending.Count > 0)
            {
                var modelResults = await TranslateLiteBatchAsync(pendingOriginals, pending, source,
                    targetLanguage, settings, cancellationToken);
                for (var index = 0; index < pendingIndexes.Count; index++)
                    output[pendingIndexes[index]] = modelResults[index];
            }
            translated = output;
        }
        else
        {
            translated = await TranslateLiteBatchAsync(new[] { text }, lines, source,
                targetLanguage, settings, cancellationToken);
        }
        var result = preserveLines ? string.Join(Environment.NewLine, translated) : translated.FirstOrDefault() ?? "";
        // Do not manufacture a negation/uncertainty prefix around a faulty Lite result.
        return Clean(LiteTranslationGuard.Validate(text, result, targetLanguage, settings, _glossary), preserveLines);
    }

    private async Task<IReadOnlyList<string>> TranslateLiteBatchAsync(IReadOnlyList<string> originals,
        IReadOnlyList<string> normalized, string source, string target, AppSettings settings,
        CancellationToken cancellationToken)
    {
        var plans = originals.Select(line => LiteUncertaintyPlan.Create(line, settings, _glossary)).ToArray();
        var inputs = normalized.Select((line, index) => plans[index]?.Core ?? line).ToArray();
        var batchWatch = Stopwatch.StartNew();
        IReadOnlyList<string> raw;
        if (source == "JP" && target == "KO" || source == "KO" && target == "JP")
        {
            // Inspect the English pivot before the second model can discard facts.
            // Known complete pivot callouts use the same dictionary as game chat.
            var pivotWatch = Stopwatch.StartNew();
            var pivot = await _lite.TranslateAsync(inputs, source, "EN", cancellationToken);
            pivotWatch.Stop();
            var resolved = new string[inputs.Length];
            var pending = new List<string>();
            var indexes = new List<int>();
            var calloutShortCircuits = 0;
            for (var index = 0; index < inputs.Length; index++)
            {
                var originalCore = plans[index]?.Core ?? originals[index];
                var checkedPivot = LiteTranslationGuard.Validate(originalCore, pivot[index], "EN", settings, _glossary);
                if (_glossary.TryTranslateStructuredCallout(checkedPivot, target, settings, out var callout))
                {
                    resolved[index] = callout;
                    calloutShortCircuits++;
                }
                else
                {
                    pending.Add(checkedPivot);
                    indexes.Add(index);
                }
            }
            var secondLegMs = 0d;
            if (pending.Count > 0)
            {
                var secondWatch = Stopwatch.StartNew();
                var second = await _lite.TranslateAsync(pending, "EN", target, cancellationToken);
                secondWatch.Stop();
                secondLegMs = secondWatch.Elapsed.TotalMilliseconds;
                for (var index = 0; index < indexes.Count; index++)
                    resolved[indexes[index]] = LiteTranslationGuard.Validate(pending[index], second[index], target, settings, _glossary);
            }
            raw = resolved;
            batchWatch.Stop();
            LastLitePivotMetrics = new LitePivotMetrics(source, target, true,
                pending.Count > 0 ? 2 : 1, pivotWatch.Elapsed.TotalMilliseconds, secondLegMs,
                batchWatch.Elapsed.TotalMilliseconds, inputs.Length, calloutShortCircuits);
        }
        else
        {
            var directWatch = Stopwatch.StartNew();
            raw = await _lite.TranslateAsync(inputs, source, target, cancellationToken);
            directWatch.Stop();
            batchWatch.Stop();
            LastLitePivotMetrics = new LitePivotMetrics(source, target, false, 1, 0, 0,
                directWatch.Elapsed.TotalMilliseconds, inputs.Length, 0);
        }
        var result = raw.ToArray();
        for (var index = 0; index < plans.Length; index++)
        {
            if (plans[index] is not { } plan) continue;
            // Validate the model's core before reattaching the explicitly parsed
            // source uncertainty; a missing negative or direction still fails.
            var core = LiteTranslationGuard.Validate(plan.Core, raw[index], target, settings, _glossary);
            result[index] = LiteTranslationGuard.Validate(originals[index], plan.Compose(core, target),
                target, settings, _glossary);
        }
        return result;
    }

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

    private static string? TryReadError(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.GetProperty("error").GetProperty("message").GetString();
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
