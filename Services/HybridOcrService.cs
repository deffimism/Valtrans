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
/// Consult PaddleOCR-VL when Fast never reaches the accept threshold. Disabled under
/// Test Arena E2E: Fast and VL share one GPU and loading both stalls the run.
/// </param>
public sealed record HybridOcrOptions(bool AllowLatestLineRetry, bool AllowVlFallback)
{
    public static HybridOcrOptions Production { get; } = new(true, true);
    public static HybridOcrOptions FastOnly { get; } = new(false, false);
}

/// <summary>Result of a hybrid read plus which stage produced it, for tracing and UX.</summary>
public sealed record HybridOcrRead(OcrReadResult Result, string Stage);

/// <summary>Phase 4: Fast OCR first, PaddleOCR-VL fallback when confidence/plausibility is low.</summary>
public sealed class HybridOcrService
{
    public const string StageFastFullRegion = "fast-full";
    public const string StageFastLatestLine = "fast-latest";
    public const string StageVlFallback = "vl-fallback";
    public const string StageFastUnverified = "fast-unverified";

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
        var fast = await _fast.ReadAsync(png, fastRuntime, token);
        if (OcrCandidateResolver.MeetsHybridFastAccept(fast, glossary, settings))
            return Accept(fast, StageFastFullRegion);

        if (options.AllowLatestLineRetry && latestLinePng is { Length: > 0 })
        {
            var latestFast = await _fast.ReadAsync(latestLinePng, fastRuntime, token);
            if (OcrCandidateResolver.MeetsHybridFastAccept(latestFast, glossary, settings))
                return Accept(latestFast, StageFastLatestLine);
            fast = OcrCandidateResolver.Choose(fast, latestFast, glossary, settings);
            if (OcrCandidateResolver.MeetsHybridFastAccept(fast, glossary, settings))
                return Accept(fast, StageFastLatestLine);
        }

        if (!options.AllowVlFallback)
            return Accept(fast, StageFastUnverified);

        var vl = await _paddle.ReadAsync(latestLinePng ?? png, paddleRuntime, token);
        return Accept(OcrCandidateResolver.Choose(fast, vl, glossary, settings), StageVlFallback);
    }

    private HybridOcrRead Accept(OcrReadResult result, string stage)
    {
        LastStage = stage;
        return new HybridOcrRead(result with { DetectedLanguage = "MIXED" }, stage);
    }
}
