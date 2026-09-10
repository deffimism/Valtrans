using Valtrans.Models;
using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

public sealed class TranslationCacheServiceTests
{
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
