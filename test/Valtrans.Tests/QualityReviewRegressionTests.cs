using Valtrans.Models;
using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

public sealed class QualityReviewRegressionTests
{
    [Theory]
    [InlineData("3900이 있으니 밴달 사줄게")]
    [InlineData("120이 들어갔어")]
    [InlineData("2가 남았어")]
    [InlineData("아직 로테하지 마")]
    [InlineData("2명이 우리 뒤에 숨어 있어")]
    public void Compaction_never_invents_a_person_counter(string translated)
        => Assert.Equal(translated, BriefingTranslationGuard.CompactCommonCallout(translated, "KO"));

    [Fact]
    public void A_negative_correction_does_not_gain_a_prohibition_marker()
        => Assert.Equal("BではなくAに行こう", BriefingTranslationGuard.Apply("B로 가자 말고 A로 가자", "BではなくAに行こう", "JP"));
    private readonly GlossaryService _glossary = new();
    private readonly AppSettings _settings = new() { Game = "VALORANT" };

    [Theory]
    // The complete two-clause rotate grammar is now covered by TacticalStateTests.
    // An extra uncertainty clause must still prevent a partial shortcut.
    [InlineData("A 비었어 B로 로테하자, 그런데 아직 확실하지 않아")]
    [InlineData("I'm going to eat after this match")]
    [InlineData("この試合が終わったらご飯食べに行く")]
    [InlineData("친구가 '왼쪽 조심해'라고 했어")]
    [InlineData("going to the bathroom")]
    [InlineData("집으로 가자")]
    public void Rules_do_not_swallow_other_clauses_or_social_destinations(string source)
        => Assert.False(_glossary.TryTranslateStructuredCallout(source, "EN", _settings, out _));

    [Theory]
    [InlineData("B", "KO", "B")]
    [InlineData("B", "JP", "B")]
    [InlineData("B main", "KO", "B 메인")]
    [InlineData("A 숏", "EN", "A Short")]
    [InlineData("Cロング", "KO", "C 롱")]
    public void Standalone_locations_never_gain_counts_or_another_location(string source, string target, string expected)
    {
        Assert.True(_glossary.TryTranslateStructuredCallout(source, target, _settings, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("아마 오른쪽에 두 명", "たぶん右に2人", "JP")]
    [InlineData("아마 오른쪽에 두 명", "たぶん右側に2人", "JP")]
    [InlineData("뒤에서 발소리 들었어", "後ろから足音がした", "JP")]
    [InlineData("오른쪽 말고 왼쪽 봐줘", "右じゃなく左を見て", "JP")]
    [InlineData("I'll be right back, bathroom break", "곧 돌아올게, 화장실 다녀올게", "KO")]
    [InlineData("힐 없어 10초 뒤에 가능", "No heal, ready in 10 seconds", "EN")]
    [InlineData("Two teammates are holding B heaven", "동료 2명이 B 헤븐을 지키고 있어", "KO")]
    public void Valid_outputs_survive_compaction_then_fact_checks(string source, string translated, string target)
    {
        var compact = BriefingTranslationGuard.CompactCommonCallout(translated, target);
        var result = TranslationFactGuard.Apply(source, compact, target, _settings, _glossary);
        Assert.False(string.IsNullOrWhiteSpace(result.Text));
        if (target == "JP" && translated.Contains("2人")) Assert.Contains("2人", result.Text);
    }

    [Theory]
    [InlineData("왼쪽 하나 오른쪽 둘", "2 left, 2 right", "EN")]
    [InlineData("3900 크레딧 있어", "390 크레딧 있어", "KO")]
    public void Real_numeric_loss_is_still_rejected(string source, string translated, string target)
    {
        try
        {
            var actual = TranslationFactGuard.Apply(source, translated, target, _settings, _glossary);
            Assert.NotEqual(translated, actual.Text); // A complete grammar may safely correct it.
        }
        catch (InvalidOperationException) { }
    }

    [Theory]
    [InlineData("B로 가자 말고 A로 가자", "EN", "don't go B, go A")]
    [InlineData("Bに行かないで、Aに行こう", "KO", "B 말고 A로 가자")]
    [InlineData("A 메인 아니고 B 헤븐", "EN", "not A Main, B Heaven")]
    [InlineData("오른쪽 말고 왼쪽 봐줘", "JP", "右ではなく左を見て")]
    [InlineData("왼쪽 하나 오른쪽 둘", "JP", "左1人, 右2人")]
    [InlineData("左に一人、右に二人", "EN", "1 left, 2 right")]
    public void Full_grammars_preserve_each_role(string source, string target, string expected)
    {
        Assert.True(_glossary.TryTranslateStructuredCallout(source, target, _settings, out var actual));
        Assert.Equal(expected, actual);
    }
}
