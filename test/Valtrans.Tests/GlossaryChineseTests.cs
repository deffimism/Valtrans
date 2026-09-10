using Valtrans.Models;
using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

public sealed class GlossaryChineseTests
{
    private readonly GlossaryService _glossary = new();
    private readonly AppSettings _settings = new() { Game = "VALORANT" };

    [Theory]
    [InlineData("A小两个", "EN", "2 A Short")]
    [InlineData("A小两个", "KO", "A 숏 2명")]
    [InlineData("jett残血", "EN", "Jett low")]
    [InlineData("jett残血", "KO", "Jett 체력 낮음")]
    [InlineData("jett 残 血", "EN", "Jett low")]
    public void Chinese_tactical_briefings_translate_deterministically(string source, string target, string expected)
    {
        var ok = _glossary.TryTranslateStructuredCallout(source, target, _settings, out var translated);
        Assert.True(ok);
        Assert.Equal(expected, translated);
    }
}
