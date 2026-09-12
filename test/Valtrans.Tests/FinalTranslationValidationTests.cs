using Valtrans.Models;
using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

public sealed class FinalTranslationValidationTests
{
    [Theory]
    [InlineData("I have 2800, I can buy a rifle", "2800명 있어서 총 사줄 수 있어")]
    [InlineData("I hit Jett for 120", "Jett에게 120타를 때렸어")]
    [InlineData("힐 있어?", "[Source Text] Do you have heal?")]
    [InlineData("hello", "⁇⁇")]
    [InlineData("잠깐 화장실 다녀올게", "handjobs, teens, teens")]
    public void Rejects_observed_structural_failures_without_guessing_a_repair(string source, string output)
        => Assert.Throws<InvalidOperationException>(() => TranslationOutputGuard.Validate(source, output));

    [Theory]
    [InlineData("I hit Jett for 120", "Jett에게 120 피해를 줬어")]
    [InlineData("20 players can buy", "20명 구매 가능")]
    [InlineData("그건 수음이라는 말이야", "It means handjob")]
    [InlineData("I have 2800, I can buy a rifle", "2800 있어서 총 사줄 수 있어")]
    public void Preserves_legitimate_units(string source, string output)
        => TranslationOutputGuard.Validate(source, output);
    [Theory]
    [InlineData("B main 2", "A Heaven 3")]
    [InlineData("右に敵はいない", "오른쪽 적 있음")]
    [InlineData("maybe two left", "왼쪽 2명")]
    [InlineData("Spike dropped B", "스파이크가 A에 떨어졌다")]
    public void The_shared_service_rejects_before_sending_or_receiving(string source, string badTranslation)
    {
        using var lite = new ValtransLiteService();
        var translator = new TranslatorService(new GlossaryService(), new LocalAiService(), lite);
        Assert.Throws<InvalidOperationException>(() =>
            translator.ValidateFinalResult(source, badTranslation, new AppSettings { Game = "VALORANT" }));
    }

    [Theory]
    [InlineData("(팀) 나: 왼쪽 둘", "左2人")]
    [InlineData("아마 오른쪽에 둘", "おそらく右に二人")]
    [InlineData("Two teammates are holding B heaven", "Two teammates are defending Heaven B")]
    [InlineData("I'll peek right away", "바로 피킹할게")]
    [InlineData("I can't play for long", "오래 플레이할 수 없어")]
    public void Valid_phrasings_do_not_false_alarm(string source, string translated)
        => Assert.True(CriticalFactValidator.Validate(source, translated, "Tactical").Passed);
}
