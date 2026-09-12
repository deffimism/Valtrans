using Valtrans.Services;
using Valtrans.Models;
using Xunit;

namespace Valtrans.Tests;

public sealed class TranslationQuantityGuardTests
{
    [Theory]
    [InlineData("힐 7초 뒤", "heal in eight seconds")]
    [InlineData("힐 7초 뒤", "heal later")]
    [InlineData("힐 7초 뒤", "heal in 7 minutes")]
    [InlineData("HP 12, heal in 7 seconds", "체력 7, 힐 12초 뒤")]
    [InlineData("two more matches", "두 라운드 더")]
    [InlineData("あと二試合", "두 라운드 더")]
    [InlineData("12 HP", "12 damage")]
    [InlineData("힐 일곱 초 뒤", "heal in six seconds")]
    [InlineData("ヒールはあと7秒", "힐은 8초 뒤")]
    [InlineData("あと二試合", "두 경기 아니라 세 경기")]
    [InlineData("라운드 두 번만 더 이기면 돼", "win three more rounds")]
    public void Rejects_missing_changed_or_swapped_measurements(string source, string output)
        => Assert.NotNull(TranslationQuantityGuard.FindMismatch(source, output));

    [Theory]
    [InlineData("힐 7초 뒤", "heal in seven seconds")]
    [InlineData("HP 12, heal in 7 seconds", "체력 12, 힐 7초 뒤")]
    [InlineData("HPは残り12で、ヒールはあと7秒で使える", "12 HP, heal in 7 seconds")]
    [InlineData("wait one minute", "60초 기다려")]
    [InlineData("힐 0.5분 뒤", "heal in 30 seconds")]
    [InlineData("two more matches", "두 판 더")]
    [InlineData("다음 판 풀바이", "full buy next round")]
    [InlineData("二試合だけやって寝る", "두 경기만 하고 잘래")]
    [InlineData("One second, my delivery is here", "잠깐만 택배가 왔어")]
    [InlineData("3,900 credits", "3900 크레딧")]
    [InlineData("힐은 7초 뒤에 가능", "ヒールは7秒後に使える")]
    [InlineData("두 분 오셨어", "Two people arrived")]
    [InlineData("one more game", "한 게임 더")]
    [InlineData("two more rounds, not two matches", "두 번 더 라운드, 두 경기가 아니라")]
    [InlineData("二試合じゃなくて二ラウンド", "두 판이 아니라 두 라운드")]
    [InlineData("라운드 두 번만 더 이기면 돼", "win two more rounds")]
    [InlineData("We only need to win two more rounds", "더 이길 라운드는 두 번만 더 이기면 돼")]
    [InlineData("한번 해봐", "give it a try")]
    [InlineData("もう一回やろう", "다시 해보자")]
    [InlineData("이번 라운드 세이브하자", "let's save this round")]
    [InlineData("이번 라운드 세이브하자", "このラウンドはセーブしよう")]
    public void Accepts_equivalent_values_and_implicit_units(string source, string output)
        => Assert.Null(TranslationQuantityGuard.FindMismatch(source, output));

    [Fact]
    public void Social_mode_cannot_bypass_an_explicit_match_unit_change()
        => Assert.False(CriticalFactValidator.Validate("two games", "두 라운드", "Social").Passed);

    [Theory]
    [InlineData("wait one minute", "60초 기다려")]
    [InlineData("힐 0.5분 뒤", "heal in 30 seconds")]
    [InlineData("wait two seconds at mid", "미드에서 2초 기다려")]
    [InlineData("HP 12, heal in 7 seconds", "체력 12, 힐 7초 뒤")]
    public void Equivalent_units_pass_the_full_fact_checks(string source, string output)
    {
        var validation = CriticalFactValidator.Validate(source, output, "Tactical");
        Assert.True(validation.Passed, validation.Code);
        Assert.Equal(output, TranslationFactGuard.Apply(source, output, "KO", new AppSettings(), new GlossaryService()).Text);
    }

    [Fact]
    public void Unit_conversion_does_not_hide_an_unrelated_changed_damage_number()
        => Assert.False(CriticalFactValidator.Validate("hit for 120, wait one minute", "피해 140, 60초 기다려", "Tactical").Passed);
}
