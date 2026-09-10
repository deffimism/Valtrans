using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

public sealed class MixedLanguageDetectorTests
{
    [Fact]
    public void Detects_mixed_japanese_and_chinese()
    {
        Assert.True(MixedLanguageDetector.IsMixed("jett残血"));
        Assert.Equal("JP", MixedLanguageDetector.DetectPrimaryScript("Bラッシュ"));
        Assert.Equal("ZH", MixedLanguageDetector.DetectPrimaryScript("A小两个"));
    }
}
