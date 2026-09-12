using Valtrans.Models;
using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

public sealed class ChatFilterFidelityTests
{
    private readonly GameChatFilterService filter = new(new GlossaryService());
    private readonly AppSettings settings = new() { Game = "VALORANT" };

    [Theory]
    [InlineData("If that was you, don't push B.")]
    [InlineData("I thought there were two B main, but one was a teammate.")]
    [InlineData("왼쪽으로 가, 내가 말하면.")]
    [InlineData("그랬던 건 아니야. 미드 둘이라고 말한 거야.")]
    [InlineData("右に二人だと思った。でも一人は味方だった。")]
    [InlineData("걔 원탭 아니야, 풀피야")]
    [InlineData("あいつワンショットじゃない、フルHPだ")]
    [InlineData("He's full HP, don't peek.")]
    [InlineData("Two B, idiot. Wait for me.")]
    [InlineData("A clear. B clear. C clear. I might be wrong.")]
    public void Tactical_filter_preserves_all_clauses_punctuation_and_conditions(string source)
    {
        foreach (var mode in new[] { GameChatFilterService.BriefingMode, GameChatFilterService.StrictMode })
        {
            var result = filter.Filter(source, mode, settings);
            Assert.True(result.Keep, result.Reason);
            Assert.Equal(source, result.Text);
            Assert.Equal("Tactical", result.Category);
        }
    }

    [Theory]
    [InlineData("걔 원탭 아니야")]
    [InlineData("나 풀피야")]
    [InlineData("あいつワンショットじゃない")]
    [InlineData("フルHPだ")]
    public void Health_slang_is_a_briefing_even_when_no_location_is_mentioned(string source)
        => Assert.Equal("Tactical", filter.Categorize(source, settings).Category);

    [Theory]
    [InlineData("풀피리를 불었어")]
    [InlineData("원탑 배우야")]
    [InlineData("ワンショットバー")]
    public void Unrelated_word_fragments_are_not_health_callouts(string source)
        => Assert.NotEqual("Tactical", filter.Categorize(source, settings).Category);

    [Fact]
    public void Standalone_insults_remain_hidden_in_briefing_mode()
        => Assert.False(filter.Filter("idiot", GameChatFilterService.BriefingMode, settings).Keep);
}
