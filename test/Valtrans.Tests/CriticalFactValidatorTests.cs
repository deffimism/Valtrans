using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

public sealed class CriticalFactValidatorTests
{
    [Theory]
    [InlineData("B Main 2", "A Heaven 3")]
    [InlineData("B Main", "B Heaven 2")]
    [InlineData("右に敵はいない", "오른쪽 적 있음")]
    [InlineData("たぶん右に二人", "오른쪽 2명")]
    [InlineData("B", "B 헤븐 2명")]
    public void Rejects_changed_or_invented_facts(string source, string translated)
        => Assert.False(CriticalFactValidator.Validate(source, translated, "Tactical").Passed);

    [Theory]
    [InlineData("2 B Heaven", "B 헤븐 2명")]
    [InlineData("Bメイン", "B 메인")]
    [InlineData("제트 딸피인데 정확한 체력은 몰라", "Jett has low HP, but exact health is unknown")]
    [InlineData("왼쪽 비었는지 아직 몰라", "Still unsure if the Left is empty")]
    [InlineData("君が設置するなら自分がカバーする", "네가 설치하면 내가 엄호할게")]
    [InlineData("自分のスモークが消える前にピークしないで", "내 연막이 사라지기 전에 피킹하지 마")]
    [InlineData("I don't know if left is clear yet", "왼쪽이 비었는지 아직 몰라")]
    [InlineData("ミッド二つ", "미드 2명")]
    [InlineData("右に三つ", "오른쪽 3명")]
    [InlineData("I'll be right back, bathroom break", "잠시 뒤에 돌아올게, 화장실 좀 다녀올게")]
    [InlineData("A小两个", "A 숏 2명")]
    [InlineData("右に敵はいない", "오른쪽 적 없음")]
    [InlineData("たぶん右に二人", "아마 오른쪽 2명")]
    [InlineData("don't go left", "왼쪽 가지 마")]
    [InlineData("Two teammates are holding B heaven", "동료 2명이 B 헤븐을 지키고 있어")]
    [InlineData("I have 3900, I can buy you a Vandal", "나에게 3900이 있으니 밴달을 하나 사줄 수 있어")]
    [InlineData("한 명인 줄 알았는데 세 명이었어", "I thought there was one person, but there were three.")]
    [InlineData("궁 아직 안 썼어", "I haven't used my ult yet")]
    [InlineData("궁 아직 안 썼어", "I haven’t used my ult yet")]
    [InlineData("로테하지 마", "You shouldn't rotate")]
    [InlineData("스파이크 없어", "I don't have the spike")]
    [InlineData("I have 12 HP left", "체력 12 남았어")]
    public void Accepts_equivalent_facts_across_scripts(string source, string translated)
        => Assert.True(CriticalFactValidator.Validate(source, translated, "Tactical").Passed);

    [Theory]
    [InlineData("Three people B", "B에 한 명")]
    [InlineData("밴달을 하나 사줄게, 미드 둘", "I'll buy you a Vandal, one mid")]
    [InlineData("한 명인 줄 알았는데 세 명이었어", "I thought there was one person, but there were two.")]
    public void Counter_fixes_still_reject_changed_headcounts(string source, string output)
        => Assert.False(CriticalFactValidator.Validate(source, output, "Tactical").Passed);

    [Theory]
    [InlineData("I haven't used my ult yet", "궁 이미 썼어")]
    [InlineData("You mustn't peek", "피킹해")]
    [InlineData("We can't rotate", "로테 가능")]
    public void English_contracted_negation_cannot_disappear(string source, string output)
        => Assert.Equal("NEGATION_MISSING", CriticalFactValidator.Validate(source, output, "Tactical").Code);

    [Fact]
    public void Detects_missing_negation_in_tactical_message()
    {
        var result = CriticalFactValidator.Validate("B 2명 아님", "2 B", "Tactical");
        Assert.False(result.Passed);
        Assert.Equal("NEGATION_MISSING", result.Code);
    }

    [Fact]
    public void Allows_social_message_without_strict_facts()
    {
        var result = CriticalFactValidator.Validate("오늘 처음이라 잘 못해요", "I'm new and not very good", "Social");
        Assert.True(result.Passed);
    }

    [Fact]
    public void Detects_missing_uncertainty()
    {
        var result = CriticalFactValidator.Validate("maybe A", "A", "Tactical");
        Assert.False(result.Passed);
        Assert.Equal("UNCERTAINTY_MISSING", result.Code);
    }
}
