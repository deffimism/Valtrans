using System.Diagnostics;
using Valtrans.Models;

namespace Valtrans.Services;

/// <summary>
/// Which optional stages of the hybrid read are allowed. The caller decides, so the
/// service itself stays free of global test-mode state and both paths are testable.
/// </summary>
/// <param name="AllowLatestLineRetry">
/// Re-read a narrow latest-line crop when the full-region Fast read is not accepted.
/// </param>
/// <param name="AllowVlFallback">
/// Consult PaddleOCR-VL when Fast never reaches the accept threshold. Production
/// and Test Arena E2E both enable this stage; FastOnly is for isolated unit probes.
/// </param>
public sealed record HybridOcrOptions(bool AllowLatestLineRetry, bool AllowVlFallback)
{
    public static HybridOcrOptions Production { get; } = new(true, true);
    public static HybridOcrOptions FastOnly { get; } = new(false, false);
}

/// <summary>Result of a hybrid read plus which stage produced it, for tracing and UX.</summary>
public sealed record HybridOcrRead(OcrReadResult Result, string Stage, HybridOcrTimings? Timings = null);
public sealed record HybridOcrTimings(double FastMs, double RetryMs, double VlMs, double TotalMs);

/// <summary>Phase 4: Fast OCR first, PaddleOCR-VL fallback when confidence/plausibility is low.</summary>
public sealed class HybridOcrService
{
    public const string StageFastFullRegion = "fast-full";
    public const string StageFastLatestLine = "fast-latest";
    public const string StageVlFallback = "vl-fallback";
    public const string StageFastUnverified = "fast-unverified";
    public const string StageFastAfterVl = "fast-after-vl";

    private readonly FastOcrService _fast;
    private readonly PaddleOcrService _paddle;

    public string LastStage { get; private set; } = "";

    public HybridOcrService(FastOcrService fast, PaddleOcrService paddle)
    {
        _fast = fast;
        _paddle = paddle;
    }

    public string Status => _fast.IsReady && _paddle.IsReady
        ? "준비 완료 · Hybrid OCR (Fast + VL)"
        : _fast.IsReady ? "준비 중 · VL 대기" : _fast.Status;

    public async Task PrepareAsync(string fastRuntime, string paddleRuntime, CancellationToken token)
    {
        await _fast.PrepareAsync(fastRuntime, token);
        await _paddle.PrepareAsync(paddleRuntime, token);
    }

    public async Task<OcrReadResult> ReadAsync(
        byte[] png,
        byte[]? latestLinePng,
        string fastRuntime,
        string paddleRuntime,
        GlossaryService glossary,
        AppSettings settings,
        HybridOcrOptions options,
        CancellationToken token)
        => (await ReadDetailedAsync(png, latestLinePng, fastRuntime, paddleRuntime, glossary, settings, options,
            token)).Result;

    public async Task<HybridOcrRead> ReadDetailedAsync(
        byte[] png,
        byte[]? latestLinePng,
        string fastRuntime,
        string paddleRuntime,
        GlossaryService glossary,
        AppSettings settings,
        HybridOcrOptions options,
        CancellationToken token)
    {
        var elapsed = Stopwatch.StartNew();
        double fastMs = 0, retryMs = 0, vlMs = 0;
        // Count every attempted stage, not only the chosen candidate's latency.
        HybridOcrRead Finish(OcrReadResult result, string stage)
        {
            var duration = elapsed.Elapsed.TotalMilliseconds;
            return Accept(result with { RecognitionDurationMs = duration, TotalDurationMs = duration }, stage)
                with { Timings = new HybridOcrTimings(fastMs, retryMs, vlMs, duration) };
        }
        var fast = await _fast.ReadAsync(png, fastRuntime, token, settings.OcrLanguages, settings.Game);
        fastMs = elapsed.Elapsed.TotalMilliseconds;
        if (OcrCandidateResolver.MeetsHybridFastAccept(fast, glossary, settings))
            return Finish(fast, StageFastFullRegion);

        if (options.AllowLatestLineRetry && latestLinePng is { Length: > 0 })
        {
            var retryStart = elapsed.Elapsed.TotalMilliseconds;
            var latestFast = await _fast.ReadAsync(latestLinePng, fastRuntime, token, settings.OcrLanguages, settings.Game);
            retryMs = elapsed.Elapsed.TotalMilliseconds - retryStart;
            if (OcrCandidateResolver.MeetsHybridFastAccept(latestFast, glossary, settings))
                return Finish(latestFast, StageFastLatestLine);
            fast = OcrCandidateResolver.Choose(fast, latestFast, glossary, settings);
            if (OcrCandidateResolver.MeetsHybridFastAccept(fast, glossary, settings))
                return Finish(fast, StageFastLatestLine);
        }

        if (!options.AllowVlFallback)
            return Finish(fast, StageFastUnverified);

        var vlStart = elapsed.Elapsed.TotalMilliseconds;
        var vl = await _paddle.ReadAsync(latestLinePng ?? png, paddleRuntime, token);
        vlMs = elapsed.Elapsed.TotalMilliseconds - vlStart;
        var chosen = OcrCandidateResolver.Choose(fast, vl, glossary, settings);
        return Finish(chosen, ReferenceEquals(chosen, vl) ? StageVlFallback : StageFastAfterVl);
    }

    private HybridOcrRead Accept(OcrReadResult result, string stage)
    {
        LastStage = stage;
        return new HybridOcrRead(result with { DetectedLanguage = "MIXED" }, stage);
    }
}
