using Valtrans.Models;
using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

// The in-app "회귀 테스트" button was the only thing running these rules, so a broken
// glossary rule could ship as long as nobody clicked it. Gate the whole suite here.
public sealed class TranslationRegressionServiceTests
{
    [Fact]
    public void Every_translation_rule_case_passes()
    {
        var report = new TranslationRegressionService(new GlossaryService())
            .Run(new AppSettings { Game = "VALORANT" });

        var failures = report.Results.Where(result => !result.Passed)
            .Select(result => $"{result.Name}: {result.Detail}")
            .ToArray();

        Assert.True(failures.Length == 0,
            $"{failures.Length}/{report.Total} 실패:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }

    [Fact]
    public void Suite_covers_a_meaningful_number_of_rules()
        => Assert.True(new TranslationRegressionService(new GlossaryService())
            .Run(new AppSettings { Game = "VALORANT" }).Total >= 50);
}
