using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

public sealed class KoreanUnknownValidationTests
{
    [Theory]
    [InlineData("모른다")]
    [InlineData("모른다고")]
    [InlineData("모른대")]
    [InlineData("모를걸")]
    [InlineData("모릅니다")]
    [InlineData("몰랐어")]
    [InlineData("몰랐습니다")]
    [InlineData("불명")]
    public void Unknown_conjugations_are_negative_in_both_directions(string word)
    {
        var korean = $"정확한 체력은 {word}.";
        Assert.True(CriticalFactValidator.Validate("I don't know the exact HP", korean, "Tactical").Passed);
        Assert.Equal("NEGATION_MISSING", CriticalFactValidator.Validate(korean,
            "I know the exact HP", "Tactical").Code);
    }

    [Theory]
    [InlineData("정확한 체력은 알아.")]
    [InlineData("정확한 체력은 알려져 있어.")]
    [InlineData("정확한 체력은 확인했어.")]
    [InlineData("불명예스러운 일이야.")]
    public void Positive_statements_and_unrelated_words_cannot_satisfy_negation(string output)
        => Assert.Equal("NEGATION_MISSING", CriticalFactValidator.Validate(
            "I don't know the exact HP", output, "Tactical").Code);

    [Fact]
    public void Actual_japanese_hp_result_is_not_falsely_blocked()
        => Assert.True(CriticalFactValidator.Validate(
            "ジェットはローだけど正確な体力はわからない",
            "Jett는 딸피지만 정확한 체력은 모른다.", "Tactical").Passed);
}
