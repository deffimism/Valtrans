using Valtrans.Models;
using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

public sealed class LiteNegationTests
{
    [Theory]
    [InlineData("미안 내 실수야", "Sorry, my bad")]
    [InlineData("미안해", "I'm sorry")]
    [InlineData("내 잘못이야", "It's my fault")]
    [InlineData("그건 칭찬이 아니었어", "That wasn't a compliment")]
    public void Apology_and_contracted_negation_are_not_false_rejections(string source, string output)
        => Assert.Equal(output, LiteTranslationGuard.Validate(source, output, "EN", new AppSettings(), new GlossaryService()));

    [Theory]
    [InlineData("나 안 해", "I'll do it")]
    [InlineData("나 안해", "I'll do it")]
    [InlineData("난 잘 못해", "I play well")]
    [InlineData("That wasn't a compliment", "그건 칭찬이었어")]
    public void Real_missing_negation_is_still_rejected(string source, string output)
        => Assert.Throws<InvalidOperationException>(() => LiteTranslationGuard.Validate(source, output, "EN", new AppSettings(), new GlossaryService()));

    [Fact]
    public void A_model_generated_store_template_is_not_chat()
        => Assert.Throws<InvalidOperationException>(() => TranslationOutputGuard.Validate("설치할게 엄호해줘",
            "{{if price_varies}}from {{/if}}{{html Shopify.formatMoney(price_min, window.money_format)}}"));

    [Fact]
    public void Discussing_a_literal_template_is_not_blocked()
        => TranslationOutputGuard.Validate("이건 {{if price_varies}} 코드야", "This is {{if price_varies}} code");
}
