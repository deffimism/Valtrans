using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

public class JapaneseDirectionHomographTests
{
    [Theory]
    [InlineData("너 먼저 들어가면 내가 뒤에서 엄호할게", "お前が先に入れば、俺が後ろからカバーする。")]
    [InlineData("You cover behind", "お前が後ろをカバーして")]
    [InlineData("Your name", "お前の名前")]
    [InlineData("名前を教えて", "Tell me your name")]
    public void Pronoun_or_name_does_not_add_front(string source, string output)
    {
        var result = CriticalFactValidator.Validate(source, output, "Tactical");
        Assert.True(result.Passed, result.Code);
    }

    [Theory]
    [InlineData("You watch front", "お前が見て")]
    [InlineData("You watch behind", "お前が前を見て")]
    [InlineData("お前が前を見て", "You watch behind")]
    public void Real_spatial_direction_is_still_checked(string source, string output)
        => Assert.Equal("DIRECTION_CHANGED", CriticalFactValidator.Validate(source, output, "Tactical").Code);
}
