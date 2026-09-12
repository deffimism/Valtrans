using Valtrans.Models;
using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

public sealed class SpeakerIntentPromptTests
{
    private static string Prompt(string source) => GameTranslationPrompt.Build(source, "KO",
        new AppSettings { Game = "VALORANT" }, new GlossaryService());

    [Theory]
    [InlineData("방금 말한 건 너 말고 다른 사람한테 한 거야", "who was addressed")]
    [InlineData("さっきのは君じゃなくて別の人に言ったんだ", "BY or ABOUT")]
    [InlineData("오늘 피곤해서 두 판만 하고 잘래", "every planned activity")]
    [InlineData("この試合が終わったらご飯食べに行く", "not an invitation")]
    [InlineData("이번 판 끝나고 밥 먹으러 갈 거야", "not an invitation")]
    public void Relevant_semantic_roles_are_explained_without_rewriting_source(string source, string expected)
    {
        var prompt = Prompt(source);
        Assert.Contains(expected, prompt);
        Assert.EndsWith(source, prompt.Trim());
    }

    [Theory]
    [InlineData("두 판만 하고 잘래?")]
    [InlineData("この試合が終わったらご飯食べに行く？")]
    [InlineData("같이 밥 먹으러 가자")]
    public void Question_or_invitation_is_not_forced_into_a_personal_plan(string source)
        => Assert.DoesNotContain("personal plan as a statement", Prompt(source));

    [Fact]
    public void Sarcasm_is_not_replaced_with_generic_joking()
    {
        var prompt = Prompt("That was sarcasm, not a compliment");
        Assert.Contains("비꼬는 말", prompt);
        Assert.DoesNotContain("농담", prompt);
    }

    [Theory]
    [InlineData("오퍼 들고 있으니까 혼자 피킹하지 마", "EN", "someone has an Operator, don't peek alone")]
    [InlineData("오퍼레이터를 들고 있으니까 피킹하지 마", "EN", "someone has an Operator, don't peek")]
    [InlineData("밴달을 들고 있으니까 피킹하지 마", "EN", "someone has a Vandal, don't peek")]
    [InlineData("팬텀 들고 있으니까 혼자 피킹하지 마", "JP", "誰かがファントムを持ってるから、一人でピークしないで")]
    public void Unspecified_weapon_holder_is_not_invented(string source, string target, string expected)
    {
        var settings = new AppSettings { Game = "VALORANT" };
        Assert.True(new GlossaryService().TryTranslateStructuredCallout(source, target, settings, out var output));
        Assert.Equal(expected, output);
        Assert.True(CriticalFactValidator.Validate(source, output, "Tactical").Passed);
    }

    [Theory]
    [InlineData("내가 오퍼 들고 있으니까 혼자 피킹하지 마")]
    [InlineData("레이나가 오퍼 들고 있으니까 혼자 피킹하지 마")]
    [InlineData("오퍼 들고 있을 수도 있으니까 혼자 피킹하지 마")]
    [InlineData("오퍼 들고 있으니까 혼자 피킹하지 마?")]
    public void Explicit_or_uncertain_holder_is_not_erased(string source)
        => Assert.False(new GlossaryService().TryTranslateStructuredCallout(source, "EN",
            new AppSettings { Game = "VALORANT" }, out _));

    [Fact]
    public void Custom_terminology_does_not_get_overwritten_by_the_weapon_grammar()
    {
        var settings = new AppSettings { Game = "VALORANT" };
        settings.CustomGlossary["오퍼"] = "custom-name";
        Assert.False(new GlossaryService().TryTranslateStructuredCallout(
            "오퍼 들고 있으니까 혼자 피킹하지 마", "EN", settings, out _));
    }
}
