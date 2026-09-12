using Valtrans.Models;
using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

public sealed class GameContextClassificationTests
{
    private readonly GameChatFilterService filter = new(new GlossaryService());
    private readonly AppSettings settings = new() { Game = "VALORANT" };

    [Theory]
    [InlineData("오늘 피곤해서 두 판만 하고 잘래")]
    [InlineData("피자 먹고 올게")]
    [InlineData("궁금한 게 있어")]
    [InlineData("적당히 쉬면서 하자")]
    public void Korean_word_fragments_do_not_turn_social_chat_into_briefings(string source)
        => Assert.NotEqual("Tactical", filter.Categorize(source, settings).Category);

    [Theory]
    [InlineData("피 12 남음")]
    [InlineData("피가 없어")]
    [InlineData("피킹하지 마")]
    [InlineData("궁 아직 안 썼어")]
    [InlineData("적 없어")]
    public void Actual_briefings_keep_their_context(string source)
        => Assert.Equal("Tactical", filter.Categorize(source, settings).Category);
}
