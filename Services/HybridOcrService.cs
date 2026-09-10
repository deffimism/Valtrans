using Valtrans.Models;

namespace Valtrans.Services;

/// <summary>Phase 4: Fast OCR first, PaddleOCR-VL fallback when confidence/plausibility is low.</summary>
public sealed class HybridOcrService
{
    private readonly FastOcrService _fast;
    private readonly PaddleOcrService _paddle;

    public double FastAcceptThreshold { get; set; } = 0.90;
    public double FastPlausibilityThreshold { get; set; } = 0.75;

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
        CancellationToken token)
    {
        var fast = await _fast.ReadAsync(png, fastRuntime, token);
        if (OcrCandidateResolver.MeetsHybridFastAccept(fast, glossary, settings))
            return fast with { DetectedLanguage = "MIXED" };

        if (latestLinePng is { Length: > 0 } && !TestModeContext.Enabled)
        {
            var latestFast = await _fast.ReadAsync(latestLinePng, fastRuntime, token);
            if (OcrCandidateResolver.MeetsHybridFastAccept(latestFast, glossary, settings))
                return latestFast with { DetectedLanguage = "MIXED" };
            fast = OcrCandidateResolver.Choose(fast, latestFast, glossary, settings);
            if (OcrCandidateResolver.MeetsHybridFastAccept(fast, glossary, settings))
                return fast with { DetectedLanguage = "MIXED" };
        }

        if (TestModeContext.Enabled)
            return fast with { DetectedLanguage = "MIXED" };

        var vl = await _paddle.ReadAsync(latestLinePng ?? png, paddleRuntime, token);
        return OcrCandidateResolver.Choose(fast, vl, glossary, settings) with { DetectedLanguage = "MIXED" };
    }
}
