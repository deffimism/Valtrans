using Valtrans.Models;
using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

public sealed class ActionIntentTests
{
    private readonly GlossaryService glossary = new();
    private readonly AppSettings settings = new() { Game = "VALORANT" };

    [Theory]
    [InlineData("제트한테 80 넣었어", "EN", "hit Jett for 80")]
    [InlineData("레이나한테 120 넣었는데 아직 살아 있어", "KO", "Reyna에게 120 피해, 아직 살아 있음")]
    [InlineData("I hit Sage for 99, but she's still alive", "JP", "Sageに99ダメージ、まだ生きてる")]
    [InlineData("ジェットに80入れた", "KO", "Jett에게 80 피해")]
    [InlineData("Aヘブン三人", "KO", "A 헤븐 3명")]
    [InlineData("ミッド二つ", "KO", "미드 2명")]
    [InlineData("지금 들어가도 돼", "EN", "you can enter now")]
    [InlineData("まだ入らないで", "KO", "아직 들어가지 마")]
    [InlineData("플래시 온다 뒤돌아", "EN", "flash incoming, turn away")]
    [InlineData("スパイクを拾わずに射線を見て", "KO", "스파이크 줍지 말고 각 잡아")]
    [InlineData("解除するふりだけして", "KO", "해체하는 척만 해")]
    public void Preserves_intent_in_complete_recognized_clauses(string source, string target, string expected)
    {
        Assert.True(glossary.TryTranslateStructuredCallout(source, target, settings, out var result));
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("내가 들어가도 돼?")]
    [InlineData("지금 들어가도 돼?")]
    [InlineData("제트한테 80 넣었어 아니 30이야")]
    [InlineData("제트한테 80 넣었는데")]
    [InlineData("I hit Sage for 99, but she was already dead")]
    [InlineData("My friend said 'flash incoming, turn away'")]
    [InlineData("해체하는 척만 해라고 하진 않았어")]
    [InlineData("미드二つ but not sure")]
    public void Does_not_discard_unrecognized_tail_question_or_quote(string source)
        => Assert.False(glossary.TryTranslateStructuredCallout(source, "EN", settings, out _));

    [Theory]
    [InlineData("잘 부탁드립니다", "EN", "let's have a good game")]
    [InlineData("ナイストライ", "KO", "아깝다")]
    [InlineData("np dw", "JP", "大丈夫、心配しないで")]
    public void Common_phrases_keep_social_intent(string source, string target, string expected)
    {
        Assert.True(glossary.TryTranslateExactShortcut(source, target, out var output, settings));
        Assert.Equal(expected, output);
    }
}
