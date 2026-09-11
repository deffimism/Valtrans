using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

public sealed class OcrTokenRepairTests
{
    [Theory]
    [InlineData("2BHeaven", "2 B Heaven")]
    [InlineData("AShort2", "A Short 2")]
    [InlineData("midTwo", "mid Two")]
    public void Restores_spaces_that_fast_ocr_dropped(string raw, string expected)
        => Assert.Equal(expected, OcrNoiseHeuristics.SplitRunTogetherCallout(raw));

    [Theory]
    [InlineData("watch left")]                 // already spaced
    [InlineData("heaven")]                     // no boundary to split
    [InlineData("A小两个")]                     // not ASCII
    [InlineData("don't go mid")]               // punctuation, ordinary prose
    [InlineData("ThisIsAVeryLongNicknameHere")] // longer than a callout
    public void Leaves_everything_else_untouched(string raw)
        => Assert.Equal(raw, OcrNoiseHeuristics.SplitRunTogetherCallout(raw));
}
