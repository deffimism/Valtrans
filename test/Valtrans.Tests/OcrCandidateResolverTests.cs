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

    [Theory]
    [InlineData("[TEAM] PlayerC: 스파이크 ㄷ 롱에 떨어졌어")]
    [InlineData("ㄴ 메인 두명")]
    public void Suspicious_site_glyph_requires_another_read_even_when_confident(string text)
        => Assert.False(OcrCandidateResolver.MeetsHybridFastAccept(Candidate(text, .999), _glossary, Valorant));

    [Theory]
    [InlineData("[TEAM] ㄷ: 스파이크 C 롱에 떨어졌어")]
    [InlineData("ㄷㄷ 무섭다")]
    [InlineData("ㅋㅋ 롱에 있네")]
    [InlineData("스파이크 C 롱에 떨어졌어")]
    public void Normal_text_and_nicknames_do_not_trigger_site_glyph_retry(string text)
        => Assert.False(OcrCandidateResolver.HasSuspectSiteGlyph(text));

    [Fact]
    public void Clean_site_reading_outweighs_confident_jamo_misread()
    {
        var noisy = Candidate("스파이크 ㄷ 롱에 떨어졌어", .99);
        var clean = Candidate("스파이크 C 롱에 떨어졌어", .92);
        Assert.Equal(clean.Text, OcrCandidateResolver.Choose(noisy, clean, _glossary, Valorant).Text);
    }

    [Fact]
    public void Vl_without_confidence_is_not_treated_as_zero_accuracy()
    {
        const string history = "[TEAM] PlayerB: 私が死んでもまだピークしないで\n[TEAM] PlayerC: ";
        var fast = Candidate(history + "스파이크 ㄷ 롱에 떨어졌어", .9326);
        var vl = Candidate(history + "스파이크 C 롱에 떨어졌어", 0) with { QualityScoreAvailable = false };
        Assert.Equal(vl.Text, OcrCandidateResolver.Choose(fast, vl, _glossary, Valorant).Text);
    }

    [Fact]
    public void Empty_vl_response_never_overwrites_a_nonempty_reading()
    {
        var fast = Candidate("watch left", .91);
        var vl = Candidate("", 0) with { QualityScoreAvailable = false };
        Assert.Equal(fast.Text, OcrCandidateResolver.Choose(fast, vl, _glossary, Valorant).Text);
    }

    [Fact]
    public void Missing_confidence_cannot_skip_verification()
        => Assert.False(OcrCandidateResolver.MeetsHybridFastAccept(
            Candidate("B heaven 2", .99) with { QualityScoreAvailable = false }, _glossary, Valorant));
}

public sealed class HybridOcrOptionsTests
{
    [Fact]
    public void Production_runs_every_stage()
    {
        Assert.True(HybridOcrOptions.Production.AllowLatestLineRetry);
        Assert.True(HybridOcrOptions.Production.AllowVlFallback);
    }

    // FastOnly remains available for isolated unit probes, not production E2E.
    [Fact]
    public void Fast_only_skips_the_latest_line_retry_and_the_vl_fallback()
    {
        Assert.False(HybridOcrOptions.FastOnly.AllowLatestLineRetry);
        Assert.False(HybridOcrOptions.FastOnly.AllowVlFallback);
    }
}
