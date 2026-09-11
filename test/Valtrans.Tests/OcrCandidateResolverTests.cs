using Valtrans.Models;
using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

public sealed class OcrCandidateResolverTests
{
    private readonly GlossaryService _glossary = new();
    private static AppSettings Valorant => new() { Game = "VALORANT" };

    private static OcrReadResult Candidate(string text, double quality)
        => new(text, "MIXED", QualityScore: quality);

    [Fact]
    public void Rejects_a_confident_but_meaningless_single_token()
        => Assert.False(OcrCandidateResolver.MeetsHybridFastAccept(Candidate("qqq", 0.99), _glossary, Valorant));

    [Fact]
    public void Accepts_a_single_token_the_glossary_recognizes()
        => Assert.True(OcrCandidateResolver.MeetsHybridFastAccept(Candidate("2BHeaven", 0.99), _glossary, Valorant));

    [Fact]
    public void Rejects_anything_below_the_confidence_floor()
        => Assert.False(OcrCandidateResolver.MeetsHybridFastAccept(Candidate("B 헤븐 2명", 0.60), _glossary, Valorant));

    [Fact]
    public void Prefers_the_candidate_without_scattered_han_noise()
    {
        var noisy = Candidate("watch 厄 升", 0.95);
        var clean = Candidate("watch left", 0.90);
        Assert.Equal("watch left", OcrCandidateResolver.Choose(noisy, clean, _glossary, Valorant).Text);
    }

    [Fact]
    public void Prefers_the_longer_candidate_that_contains_the_shorter_one()
    {
        var partial = Candidate("heaven", 0.92);
        var full = Candidate("B heaven two", 0.88);
        Assert.Equal("B heaven two", OcrCandidateResolver.Choose(partial, full, _glossary, Valorant).Text);
    }
}

public sealed class HybridOcrOptionsTests
{
    [Fact]
    public void Production_runs_every_stage()
    {
        Assert.True(HybridOcrOptions.Production.AllowLatestLineRetry);
        Assert.True(HybridOcrOptions.Production.AllowVlFallback);
    }

    // The Test Arena shares one GPU between Fast and VL, so E2E must stop after Fast.
    [Fact]
    public void Fast_only_skips_the_latest_line_retry_and_the_vl_fallback()
    {
        Assert.False(HybridOcrOptions.FastOnly.AllowLatestLineRetry);
        Assert.False(HybridOcrOptions.FastOnly.AllowVlFallback);
    }
}
