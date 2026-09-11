using Valtrans.Models;
using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

public sealed class MessageClassifierServiceTests
{
    private readonly MessageClassifierService _classifier =
        new(new GameChatFilterService(new GlossaryService()));

    [Fact]
    public void Classifies_tactical_callout()
    {
        var result = _classifier.Classify("B 헤븐에 두 명", new AppSettings { Game = "VALORANT" });
        Assert.Equal("Tactical", result.Type);
    }

    [Fact]
    public void Classifies_system_message()
    {
        var result = _classifier.Classify("[SYSTEM] Match starting", new AppSettings());
        Assert.Equal("System", result.Type);
    }

    // The classifier used to ask the filter in AllMode, which returns before any category
    // is decided, so every non-system line came back Tactical and ordinary conversation
    // was run through the strict fact validator.
    [Theory]
    [InlineData("오늘 처음이라 잘 못해요")]
    [InlineData("점심 먹고 올게")]
    public void Ordinary_conversation_is_not_tactical(string text)
        => Assert.NotEqual("Tactical", _classifier.Classify(text, Valorant).Type);

    [Theory]
    [InlineData("gg")]
    [InlineData("안녕하세요")]
    public void Greetings_are_social(string text)
        => Assert.Equal("Social", _classifier.Classify(text, Valorant).Type);

    [Theory]
    [InlineData("B 헤븐에 두 명")]
    [InlineData("watch mid")]
    [InlineData("A小两个")]
    public void Callouts_stay_tactical(string text)
        => Assert.Equal("Tactical", _classifier.Classify(text, Valorant).Type);

    private static AppSettings Valorant => new() { Game = "VALORANT" };
}
