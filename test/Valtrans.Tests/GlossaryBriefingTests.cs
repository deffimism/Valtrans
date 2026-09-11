using System.Text.RegularExpressions;
using Valtrans.Models;
using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

public sealed class GlossaryBriefingTests
{
    private readonly GlossaryService _glossary = new();
    private static AppSettings Valorant => new() { Game = "VALORANT" };

    [Theory]
    [InlineData("watch left", "KO", "왼쪽 조심")]
    [InlineData("watch right", "KO", "오른쪽 조심")]
    [InlineData("左見て", "KO", "왼쪽 조심")]
    [InlineData("watch left", "JP", "左見て")]
    public void Explicit_direction_warnings_keep_their_side(string source, string target, string expected)
    {
        Assert.True(_glossary.TryTranslateStructuredCallout(source, target, Valorant, out var translated));
        Assert.Equal(expected, translated);
    }

    // A location warning must keep its location. Guessing "left" for every unrecognized
    // "watch X" sends a teammate to the wrong angle, which is worse than no translation.
    [Theory]
    [InlineData("watch mid", "미드 조심")]
    [InlineData("watch heaven", "헤븐 조심")]
    [InlineData("watch short", "숏 조심")]
    [InlineData("watch main", "메인 조심")]
    public void Location_warnings_are_not_collapsed_into_a_direction(string source, string expected)
    {
        Assert.True(_glossary.TryTranslateStructuredCallout(source, "KO", Valorant, out var translated));
        Assert.Equal(expected, translated);
    }

    [Theory]
    [InlineData("watch 厄 升")]
    [InlineData("watch qqq")]
    public void Unrecognized_watch_targets_produce_no_fabricated_briefing(string source)
        => Assert.False(_glossary.TryTranslateStructuredCallout(source, "KO", Valorant, out _));

    [Theory]
    [InlineData("A rush", "KO", "A 러시")]
    [InlineData("A小两个", "KO", "A 숏 2명")]
    [InlineData("jett残血", "KO", "Jett 체력 낮음")]
    [InlineData("2BHeaven", "KO", "B 헤븐 2명")]
    public void Known_callouts_still_translate(string source, string target, string expected)
    {
        Assert.True(_glossary.TryTranslateStructuredCallout(source, target, Valorant, out var translated));
        Assert.Equal(expected, translated);
    }

    [Theory]
    [InlineData("ring")]
    [InlineData("zone")]
    [InlineData("evo")]
    [InlineData("banner")]
    [InlineData("syringe")]
    [InlineData("링 끝")]
    [InlineData("제작기")]
    [InlineData("집라인")]
    public void Apex_only_vocabulary_is_gone_from_the_model_glossary(string term)
        => Assert.DoesNotContain($"{term}=", _glossary.BuildPromptGlossary(Valorant),
            StringComparison.OrdinalIgnoreCase);

    [Theory]
    [InlineData("lineup")]
    [InlineData("wallbang")]
    [InlineData("pop flash")]
    [InlineData("가든")]
    [InlineData("캐트워크")]
    public void Valorant_vocabulary_reaches_the_model_glossary(string term)
        => Assert.Contains($"{term}=", _glossary.BuildPromptGlossary(Valorant),
            StringComparison.OrdinalIgnoreCase);

    // Callout recognition walks ~100 distinct patterns through the static Regex helpers.
    // At the default cache size of 15 the set thrashes and every call re-parses almost all
    // of them: measured 0.86 ms per call versus 0.09 ms once the whole set fits.
    [Fact]
    public void Regex_cache_holds_the_whole_callout_pattern_set()
    {
        _glossary.TryTranslateStructuredCallout("watch mid", "KO", Valorant, out _);
        Assert.True(Regex.CacheSize >= 256, $"Regex.CacheSize={Regex.CacheSize}");
    }

    // These read as ordinary English inside a sentence, so they must never be substituted.
    [Theory]
    [InlineData("bonus damage")]
    [InlineData("stop spam")]
    [InlineData("plant the spike")]
    public void Ordinary_words_survive_the_fps_term_expansion(string text)
        => Assert.Equal(text, _glossary.PrepareForLocalTranslation(text, Valorant));
}
