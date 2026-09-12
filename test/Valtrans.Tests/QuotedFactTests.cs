using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

public sealed class QuotedFactTests
{
    [Theory]
    [InlineData("왼쪽에 하나라고 했지 오른쪽에 둘이라고 안 했어", "I said one on the left, not two on the right")]
    [InlineData("친구가 '둘인 줄 알았는데 셋이었어'라고 했어", "My friend said, 'I thought there were two, but there were three'")]
    [InlineData("友達が「二人だと思ったけど三人だった」と言った", "친구가 '둘인 줄 알았는데 셋이었어'라고 했어")]
    [InlineData("무기 세이브 말고 리테이크하자", "武器をセーブせずにリテイクしよう")]
    public void Quoted_counts_and_contracted_negation_do_not_false_alarm(string source, string output)
    {
        var result = CriticalFactValidator.Validate(source, output, "Tactical");
        Assert.True(result.Passed, result.Code);
    }

    [Theory]
    [InlineData("왼쪽에 하나라고 했지 오른쪽에 둘이라고 안 했어", "I said two on the left, not three on the right")]
    [InlineData("친구가 '둘인 줄 알았는데 셋이었어'라고 했어", "My friend said, 'I thought there were two, but there were four'")]
    [InlineData("武器をセーブせずにリテイクしよう", "무기 세이브하고 리테이크하자")]
    public void Changed_counts_or_missing_negation_still_fail(string source, string output)
        => Assert.False(CriticalFactValidator.Validate(source, output, "Tactical").Passed);

    [Fact]
    public void Quoted_correction_is_not_a_whole_message_shortcut()
        => Assert.False(CalloutCountRevision.TryParse("친구가 '둘인 줄 알았는데 셋이었어'라고 했어", out _, out _));
}
