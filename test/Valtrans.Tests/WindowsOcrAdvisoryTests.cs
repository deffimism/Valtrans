using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

public class WindowsOcrAdvisoryTests
{
    [Theory]
    [InlineData("Windows", new[] { "JP" }, true)]
    [InlineData("Windows", new[] { "EN", "JP", "KO" }, true)]
    [InlineData("Windows", new[] { "EN", "KO" }, false)]
    [InlineData("Fast", new[] { "JP" }, false)]
    [InlineData("Hybrid", new[] { "JP" }, false)]
    [InlineData("Paddle", new[] { "JP" }, false)]
    public void Japanese_advisory_only_for_windows_ocr_with_jp(string engine, string[] languages, bool expected)
        => Assert.Equal(expected, WindowsOcrAdvisory.NeedsJapaneseEngineAdvisory(engine, languages));

    [Fact]
    public void Advisory_message_mentions_fast_and_hybrid()
    {
        Assert.Contains("Fast", WindowsOcrAdvisory.JapaneseChatLimitation);
        Assert.Contains("Hybrid", WindowsOcrAdvisory.JapaneseChatLimitation);
    }
}
