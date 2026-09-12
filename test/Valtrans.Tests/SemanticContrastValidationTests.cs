using Valtrans.Models;
using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

public sealed class SemanticContrastValidationTests
{
    [Theory]
    [InlineData("Can this gun one shot an enemy at full HP?", "이 총으로 풀피인 적을 한 방에 죽일 수 있나?")]
    [InlineData("この銃でフルHPの敵をワンショットできる？", "이 총으로 풀피인 적을 한 방에 죽일 수 있어?")]
    [InlineData("Two mid, one shot", "미드 두 명, 한 방")]
    public void Single_hit_does_not_add_a_player(string source, string translated)
    {
        var result = CriticalFactValidator.Validate(source, translated, "Tactical");
        Assert.True(result.Passed, result.Code);
    }

    [Theory]
    [InlineData("Two mid, one shot", "Three mid, one shot")]
    [InlineData("Two mid, one shot", "미드 세 명, 한 방")]
    public void One_shot_does_not_disable_other_headcount_checks(string source, string translated)
        => Assert.Equal("COUNT_CHANGED", CriticalFactValidator.Validate(source, translated, "Tactical").Code);

    [Theory]
    [InlineData("I have 2900. I'll buy you a Phantom instead of a Vandal", "나는 2900이 있어. 밴달 대신 팬텀을 사줄게.")]
    [InlineData("Buy a Phantom instead of a Vandal", "ヴァンダルの代わりにファントムを買って")]
    [InlineData("설정은 B 키로 열어. B 사이트로 가라는 말 아니야", "Open settings with key B. It's not saying to go to site B.")]
    [InlineData("I thought there were two enemies A main, but one was a teammate", "메인 A에 적이 둘 있다고 생각했는데, 하나는 팀원이었다.")]
    [InlineData("I'll play one more match today. I don't mean one round", "오늘 한 경기 더 할 거야. 라운드 하나 말하는 게 아니야.")]
    [InlineData("오늘은 한 경기만 더 할 거야. 라운드 하나 말하는 거 아니야", "We're just playing one more match today. I'm not talking about one round.")]
    [InlineData("오늘은 한 경기만 더 할 거야. 라운드 하나 말하는 거 아니야", "今日はあと1試合だけやるよ。ラウンド1つってことじゃないんだ。")]
    [InlineData("one round, not two matches", "라운드 하나, 경기 둘이 아니라")]
    public void Equivalent_facts_pass_both_guards(string source, string output)
    {
        var validation = CriticalFactValidator.Validate(source, output, "Tactical");
        Assert.True(validation.Passed, validation.Code);
        Assert.Equal(output, TranslationFactGuard.Apply(source, output, "KO", new AppSettings(), new GlossaryService()).Text);
    }

    [Theory]
    [InlineData("one round", "라운드 둘")]
    [InlineData("two matches", "라운드 둘")]
    [InlineData("one round", "라운드 하나, 두 라운드")]
    [InlineData("B site", "site A")]
    [InlineData("A main", "Main")]
    [InlineData("둘은 적이고 하나는 아군", "Two enemies and two allies")]
    [InlineData("밴달 대신 팬텀 사줘", "Buy a Vandal and a Phantom")]
    [InlineData("今日は一試合。ラウンド一つじゃない", "One round today")]
    public void Real_unit_location_count_and_replacement_changes_still_fail(string source, string output)
        => Assert.False(CriticalFactValidator.Validate(source, output, "Tactical").Passed);

    [Fact]
    public void English_indefinite_article_after_main_is_not_site_A()
        => Assert.True(CriticalFactValidator.Validate("Watch main, a teammate is there", "메인 봐, 팀원이 있어", "Tactical").Passed);

    [Theory]
    [InlineData("오늘은 한 경기만 더 할 거야. 라운드 하나 말하는 거 아니야", "今日はもう一試合だけやるよ。ラウンド一つってことだよ。")]
    [InlineData("I don't mean one round", "라운드 하나라는 뜻이야")]
    [InlineData("ラウンド一つって意味じゃない", "I mean one round")]
    public void Social_meaning_correction_cannot_lose_its_denial(string source, string output)
        => Assert.Equal("NEGATION_MISSING", CriticalFactValidator.Validate(source, output, "Social").Code);

    [Fact]
    public void Social_reassurance_can_use_positive_wording()
        => Assert.True(CriticalFactValidator.Validate("no worries", "괜찮아", "Social").Passed);

    [Theory]
    [InlineData("Two B main, one has an Op and the other is low", "B 메인 두 곳, 한 곳은 오퍼레이터, 다른 한 곳은 낮음")]
    [InlineData("미드 두 명", "two spots mid")]
    [InlineData("two spots on B", "B 세 곳")]
    public void Places_and_players_are_not_interchangeable(string source, string output)
        => Assert.False(CriticalFactValidator.Validate(source, output, "Tactical").Passed);

    [Fact]
    public void Real_place_counts_still_translate_as_places()
        => Assert.True(CriticalFactValidator.Validate("two spots on B", "B 두 곳", "Tactical").Passed);
}
