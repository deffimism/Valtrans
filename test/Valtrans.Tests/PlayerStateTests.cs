using Valtrans.Models;
using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

public sealed class PlayerStateTests
{
    private readonly GlossaryService glossary = new();
    private readonly AppSettings settings = new() { Game = "VALORANT" };

    [Theory]
    [InlineData("걔 원탭이야", "EN", "they're one shot")]
    [InlineData("쟤 원샷임", "JP", "あいつはあと一発")]
    [InlineData("나 원탭이야", "EN", "I'm one shot")]
    [InlineData("He's one shot", "KO", "걔 한 대면 죽어")]
    [InlineData("I'm one-shot", "JP", "自分はあと一発")]
    [InlineData("Reyna is one shot", "KO", "Reyna 한 대면 죽어")]
    [InlineData("제트 원탭이야", "JP", "Jettはあと一発")]
    [InlineData("あいつワンショット", "KO", "걔 한 대면 죽어")]
    [InlineData("後ろについてトレードして", "KO", "뒤에 붙어서 킬 교환해줘")]
    [InlineData("Stay behind me and trade me", "KO", "내 뒤에 붙어서 킬 교환해줘")]
    [InlineData("내 뒤에 붙어서 트레이드해줘", "JP", "自分の後ろについてカバーキルして")]
    [InlineData("뒤에 붙어서 트레이드해", "EN", "stay behind and get the trade kill")]
    public void Whole_state_and_directive_keep_intent(string source, string target, string expected)
    {
        Assert.True(glossary.TryTranslateStructuredCallout(source, target, settings, out var output));
        Assert.Equal(expected, output);
        Assert.Equal(output, TranslationFactGuard.Apply(source, output, target, settings, glossary).Text);
        var check = CriticalFactValidator.Validate(source, output, "Tactical");
        Assert.True(check.Passed, check.Code);
    }

    [Theory]
    [InlineData("He can one shot them")]
    [InlineData("He one shots them")]
    [InlineData("He isn't one shot")]
    [InlineData("걔 원탭이야?")]
    [InlineData("걔 원탭 아니야")]
    [InlineData("걔 원탭이야 아마")]
    [InlineData("걔 원탭이야, 다른 한 명은 풀피")]
    [InlineData("그 무기는 원탭이야")]
    [InlineData("Ascent is one shot")]
    [InlineData("어센트 원탭이야")]
    [InlineData("I hit Bind for 80")]
    [InlineData("They are one shot")]
    [InlineData("I said 'he's one shot'")]
    [InlineData("後ろについてトレードした")]
    [InlineData("後ろについてトレードしないで")]
    [InlineData("Stay behind me and trade me if I die")]
    [InlineData("뒤에 붙어서 트레이드했어")]
    public void Changed_intent_uncertainty_or_extra_clause_is_not_truncated(string source)
        => Assert.False(glossary.TryTranslateStructuredCallout(source, "EN", settings, out _));
}
