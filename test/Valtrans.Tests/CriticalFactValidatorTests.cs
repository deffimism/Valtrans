using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

public sealed class CriticalFactValidatorTests
{
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
