using Valtrans.Models;
using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

public sealed class TacticalStateTests
{
    private readonly GlossaryService glossary = new();
    private readonly AppSettings settings = new() { Game = "VALORANT" };

    [Theory]
    [InlineData("아군 둘이 B 헤븐 지키고 있어", "JP", "味方2人がB ヘブンを守ってる")]
    [InlineData("Three teammates are holding A main", "KO", "아군 3명 A 메인 지키는 중")]
    [InlineData("敵五人がCロングを守ってる", "KO", "적 5명 C 롱 지키는 중")]
    [InlineData("Spike dropped B", "KO", "스파이크 B에 떨어짐")]
    [InlineData("스파이크 A 메인에 떨어졌어", "EN", "spike down A Main")]
    [InlineData("スパイクがミッドに落ちた", "KO", "스파이크 미드에 떨어짐")]
    [InlineData("B is clear, rotate C", "KO", "B 비었어, C로 로테하자")]
    [InlineData("A 비었어 B로 로테하자", "EN", "A clear, rotate B")]
    [InlineData("Let's save this round", "KO", "이번 라운드 세이브하자")]
    [InlineData("상대 궁 빠졌어", "JP", "相手はウルトを使った")]
    [InlineData("적 궁극기 빠졌어", "EN", "they used their ult")]
    [InlineData("내가 죽으면 같이 피킹해", "EN", "if I die, peek together")]
    [InlineData("내가 죽으면 바로 같이 피킹해", "EN", "if I die, peek together right away")]
    [InlineData("한 명인 줄 알았는데 세 명이었어", "EN", "I thought there was 1 person, but there were 3 people")]
    [InlineData("I thought one, but there were three", "KO", "1명인 줄 알았는데 3명이었어")]
    [InlineData("I thought it was one, but there were three", "KO", "1명인 줄 알았는데 3명이었어")]
    [InlineData("一人だと思ったけど三人いた", "KO", "1명인 줄 알았는데 3명이었어")]
    [InlineData("Bに二人いるかもしれないけど確信はない", "KO", "B에 2명 있을지도, 확실하진 않아")]
    [InlineData("二人だと思ったけど四人だった", "KO", "2명인 줄 알았는데 4명이었어")]
    [InlineData("There might be three C, but I'm not sure", "KO", "C에 3명 있을지도, 확실하진 않아")]
    [InlineData("A 메인에 넷 있을지도 모르는데 확실하진 않아", "EN", "maybe 4 A Main, not sure")]
    [InlineData("ミッド五人", "KO", "미드 5명")]
    [InlineData("아마 C 롱에 넷 있을지도 모르는데 확실하지 않아", "EN", "maybe 4 C Long, not sure")]
    [InlineData("B 헤븐에 두 명 있을지도 모르는데 확실하지는 않아", "JP", "B ヘブンに2人かも、確かではない")]
    [InlineData("There may be four C long, but I'm not sure", "KO", "C 롱에 4명 있을지도, 확실하진 않아")]
    [InlineData("내가 죽어도 아직 피킹하지 마", "JP", "私が死んでもまだピークしないで")]
    [InlineData("내가 죽어도 피킹하지 마", "EN", "even if I die, don't peek")]
    [InlineData("Don't peek yet even if I die", "KO", "내가 죽어도 아직 피킹하지 마")]
    [InlineData("Even if I die, do not peek yet", "JP", "私が死んでもまだピークしないで")]
    [InlineData("自分が死んでもまだピークしないで", "KO", "내가 죽어도 아직 피킹하지 마")]
    [InlineData("俺が死んでもピークしないで", "EN", "even if I die, don't peek")]
    public void Keeps_roles_locations_and_actions(string source, string target, string expected)
    {
        Assert.True(glossary.TryTranslateStructuredCallout(source, target, settings, out var output));
        Assert.Equal(expected, output);
        Assert.True(CriticalFactValidator.Validate(source, output, "Tactical").Passed,
            CriticalFactValidator.Validate(source, output, "Tactical").Code);
    }

    [Theory]
    [InlineData("Three teammates are holding A main but leaving soon")]
    [InlineData("Two teammates were holding A main")]
    [InlineData("Two enemies are not holding B heaven")]
    [InlineData("Spike dropped B?")]
    [InlineData("Spike down B, but maybe C now")]
    [InlineData("The price of Spike dropped sharply")]
    [InlineData("A is clear, don't rotate B")]
    [InlineData("아군 둘이 B 헤븐 지키고 있어?")]
    [InlineData("Let's save this round in the replay folder")]
    [InlineData("상대 궁 빠졌어?")]
    [InlineData("상대 궁 빠졌어 근데 아직 위험해")]
    [InlineData("내가 죽으면 같이 피킹해 아니면 기다려")]
    [InlineData("There might be three C, but I'm not sure, don't move yet")]
    [InlineData("한 명인 줄 알았는데 세 명이었어?")]
    [InlineData("한 명인 줄 알았는데 세 명이었어 하지만 확실하지 않아")]
    [InlineData("내가 죽어도 아직 피킹하지 마라고 말했어")]
    [InlineData("내가 죽어도 아직 피킹하지 마?")]
    [InlineData("네가 죽어도 아직 피킹하지 마")]
    [InlineData("Don't peek yet even if I die, unless they plant")]
    [InlineData("My friend said 'even if I die, don't peek yet'")]
    [InlineData("私が死んでもまだピークしないでと言った")]
    [InlineData("아마 C 롱에 넷 있을지도 모르는데 확실하지 않아 기다려")]
    public void Does_not_drop_unrecognized_time_negation_or_tail(string source)
        => Assert.False(glossary.TryTranslateStructuredCallout(source, "KO", settings, out _));
}
