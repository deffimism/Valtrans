using Valtrans.Models;
using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

public sealed class ReceiveCombatCoverageTests
{
    private readonly GameChatFilterService filter = new(new GlossaryService());
    private readonly AppSettings settings = new() { Game = "VALORANT" };

    [Theory]
    [InlineData("I heard footsteps behind us")]
    [InlineData("Someone might be behind us")]
    [InlineData("Jett is low, I don't know her exact HP")]
    [InlineData("セージ、ヒールある？")]
    [InlineData("ヒールない、あと10秒で使える")]
    [InlineData("I'm planting, cover me")]
    [InlineData("나 3900 있어 밴달 사줄 수 있어")]
    [InlineData("3900あるからヴァンダル買ってあげられる")]
    [InlineData("다음 판 풀바이 하자")]
    [InlineData("次のラウンドはフルバイしよう")]
    [InlineData("사이퍼 트랩 조심해")]
    [InlineData("Watch out for Cypher's tripwire")]
    [InlineData("サイファーのワイヤーに気をつけて")]
    [InlineData("천천히 해 아직 40초 남았어")]
    [InlineData("Play slow, we still have 40 seconds")]
    [InlineData("ゆっくりでいい、まだ40秒ある")]
    [InlineData("If you go in first, I'll cover you from behind")]
    [InlineData("Don't go yet, I'm covering you")]
    [InlineData("HPは残り12で、ヒールはあと7秒で使える")]
    [InlineData("나 4100 있는데 팬텀 사줄까?")]
    [InlineData("4100あるけどファントム買ってあげようか？")]
    [InlineData("Let's retake together this time instead of saving our weapons")]
    [InlineData("라운드 두 번만 더 이기면 돼")]
    [InlineData("We only need to win two more rounds")]
    [InlineData("あと二ラウンド勝てばいい")]
    [InlineData("제트가 아니라 레이나한테 120 넣었어")]
    [InlineData("I hit Reyna for 120, not Jett")]
    [InlineData("ジェットじゃなくてレイナに120入れた")]
    [InlineData("돈 2900 있어. 밴달 대신 팬텀 사줄게")]
    [InlineData("レイナに80くらったんじゃなくて、こっちが80当てた")]
    public void Combat_calls_reach_translation_without_losing_clauses(string source)
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
    [InlineData("I will retake the exam")]
    [InlineData("We're retaking a photo")]
    [InlineData("Look at the book cover")]
    [InlineData("This song has a great cover")]
    [InlineData("ハイヒールを買った")]
    [InlineData("풀바이오 회사야")]
    public void Unrelated_word_senses_are_not_combat_calls(string source)
        => Assert.NotEqual("Tactical", filter.Categorize(source, settings).Category);

    [Fact]
    public void Korean_shorthand_unknown_preserves_negation()
        => Assert.True(CriticalFactValidator.Validate("Jett is low, I don't know her exact HP",
            "Jett의 체력이 낮아, 정확한 HP는 모름.", "Tactical").Passed);

    [Fact]
    public void Certain_hp_is_not_accepted_as_unknown()
        => Assert.Equal("NEGATION_MISSING", CriticalFactValidator.Validate(
            "Jett is low, I don't know her exact HP", "Jett의 체력이 낮아, 정확한 HP를 알아.", "Tactical").Code);

    [Theory]
    [InlineData("제트 딸피인데 정확한 체력은 몰라", "Jettはローですが、正確な体力はわかりません")]
    [InlineData("I can't heal", "ヒールできません")]
    [InlineData("I didn't hear footsteps", "足音は聞こえませんでした")]
    public void Japanese_polite_negation_is_preserved(string source, string translated)
        => Assert.True(CriticalFactValidator.Validate(source, translated, "Tactical").Passed);

    [Theory]
    [InlineData("ヒールできません", "I can heal")]
    [InlineData("足音は聞こえませんでした", "I heard footsteps")]
    public void Japanese_polite_negation_cannot_be_dropped(string source, string translated)
        => Assert.Equal("NEGATION_MISSING", CriticalFactValidator.Validate(source, translated, "Tactical").Code);

    [Fact]
    public void Malformed_day_word_is_not_accepted_as_a_round_count()
        => Assert.Equal("QUANTITY_MISSING", CriticalFactValidator.Validate(
            "あと二ラウンド勝てばいい", "더 이틀라운드만 이기면 돼.", "Tactical").Code);

    [Theory]
    [InlineData("나 3900 있어 밴달 사줄 수 있어", "who is buying for whom")]
    [InlineData("레이나한테 80 맞은 게 아니라 내가 80 넣었어", "damage received")]
    [InlineData("I dealt 80 to Reyna, I didn't take 80 from Reyna", "damage received")]
    public void Complete_combat_sentences_keep_role_context(string source, string expected)
    {
        var prompt = GameTranslationPrompt.Build(source, "EN", settings, new GlossaryService());
        Assert.Contains(expected, prompt);
        Assert.Contains("Preserve actions, speakers", prompt);
        Assert.EndsWith(source, prompt);
    }
}
