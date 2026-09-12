using Valtrans.Models;
using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

public sealed class TranslationCacheServiceTests
{
    [Fact]
    public void A_pending_translation_keeps_its_original_settings_and_glossary()
    {
        var current = new AppSettings();
        current.CustomGlossary["hold"] = "지켜";
        var snapshot = current.SnapshotForTranslation();
        current.LocalAiModel = "changed";
        current.CustomGlossary["hold"] = "대기";
        var cache = new TranslationCacheService();
        cache.Set("hold", "KO", "Tactical", snapshot, "지켜");
        Assert.False(cache.TryGet("hold", "KO", "Tactical", current, out _));
        Assert.True(cache.TryGet("hold", "KO", "Tactical", snapshot, out _));
        Assert.Equal("지켜", snapshot.CustomGlossary["hold"]);
    }

    [Fact]
    public void Case_can_change_meaning()
    {
        var cache = new TranslationCacheService();
        var settings = new AppSettings();
        cache.Set("US", "KO", "Social", settings, "미국");
        Assert.False(cache.TryGet("us", "KO", "Social", settings, out _));
    }
    [Fact]
    public void Whitespace_does_not_merge_distinct_words()
    {
        var cache = new TranslationCacheService();
        var settings = new AppSettings();
        cache.Set("now here", "KO", "Social", settings, "지금 여기");
        Assert.False(cache.TryGet("nowhere", "KO", "Social", settings, out _));
        Assert.True(cache.TryGet("now   here", "KO", "Social", settings, out _));
    }

    [Fact]
    public void Configuration_changes_invalidate_cached_results()
    {
        var cache = new TranslationCacheService();
        var settings = new AppSettings();
        cache.Set("hold", "KO", "Tactical", settings, "지켜");
        settings.LocalAiModel = "valtrans-hymt2:7b";
        Assert.False(cache.TryGet("hold", "KO", "Tactical", settings, out _));
        cache.Set("hold", "KO", "Tactical", settings, "지켜");
        settings.CustomGlossary["hold"] = "대기";
        Assert.False(cache.TryGet("hold", "KO", "Tactical", settings, out _));
    }

    [Fact]
    public void Stores_and_returns_cached_translation()
    {
        var cache = new TranslationCacheService();
        var settings = new AppSettings { Game = "VALORANT" };
        cache.Set("B rush", "KO", "Tactical", settings, "B 러시");
        Assert.True(cache.TryGet("B rush", "KO", "Tactical", settings, out var translated));
        Assert.Equal("B 러시", translated);
    }
}
