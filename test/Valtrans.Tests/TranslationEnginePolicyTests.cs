using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

public sealed class TranslationEnginePolicyTests
{
    [Theory]
    [InlineData("Hybrid", false, true, true)]
    [InlineData("Hybrid", true, false, false)]
    [InlineData("Hybrid", false, false, false)]
    [InlineData("Hybrid", true, true, true)]
    [InlineData("Ollama", false, true, true)]
    [InlineData("Ollama", true, false, false)]
    [InlineData("Lite", true, false, true)]
    [InlineData("Lite", false, true, false)]
    [InlineData("unknown", true, true, false)]
    [InlineData("hybrid", false, true, true)]
    public void Readiness_requires_the_engine_that_will_actually_translate(
        string provider, bool liteReady, bool localReady, bool expected) =>
        Assert.Equal(expected, TranslationEnginePolicy.IsReady(provider, liteReady, localReady));

    [Theory]
    [InlineData("Hybrid", false, true)]
    [InlineData("Ollama", false, true)]
    [InlineData("Lite", true, false)]
    public void Dependencies_do_not_prepare_an_unused_engine(string provider, bool lite, bool ai)
    {
        Assert.Equal(lite, TranslationEnginePolicy.RequiresLite(provider));
        Assert.Equal(ai, TranslationEnginePolicy.RequiresLocalAi(provider));
    }
}
