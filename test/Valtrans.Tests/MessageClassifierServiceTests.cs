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
}
