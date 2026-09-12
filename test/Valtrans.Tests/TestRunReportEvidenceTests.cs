using Valtrans.Models;
using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

public sealed class TestRunReportEvidenceTests
{
    [Theory]
    [InlineData("내가 죽어도 아직 피킹하지 마", "내가 죽어도 아직 피킹하지")]
    [InlineData("Three teammates are holding A main", "holding A main")]
    [InlineData("There may be four C long, but I'm not sure", "but I'm not sure")]
    [InlineData("don't peek", "do peek")]
    public void Partial_source_cannot_become_a_pass_even_with_a_matching_output(string expected, string actual)
        => Assert.False(TestRunReportWriter.CompleteSourceMatches(expected, actual));

    [Theory]
    [InlineData("Bラッシュ", "Ｂ ラッシュ")]
    [InlineData("watch left", "watch left!")]
    public void Full_source_allows_layout_and_fullwidth_differences(string expected, string actual)
        => Assert.True(TestRunReportWriter.CompleteSourceMatches(expected, actual));

    [Fact]
    public void Unmatched_ocr_is_retained_without_passing_or_changing_a_case()
    {
        var report = new TestRunReport();
        report.Cases.Add(new TestRunCaseResult { Source = "Bラッシュ" });
        report.ObserveUnmatchedTrace(new MessageTraceRecord
        {
            Ocr = new MessageTraceOcr { Raw = "B フッシュ" },
            Translation = new MessageTraceTranslation { Output = "B 루프" }
        });
        Assert.Equal("B フッシュ", Assert.Single(report.UnmatchedTraces).Ocr.Raw);
        Assert.Equal("PENDING", Assert.Single(report.Cases).Status);
        Assert.Empty(report.Failures);
    }

    [Fact]
    public void Unmatched_evidence_is_bounded()
    {
        var report = new TestRunReport();
        for (var i = 0; i < 100; i++) report.ObserveUnmatchedTrace(new MessageTraceRecord { Id = i.ToString() });
        Assert.Equal(32, report.UnmatchedTraces.Count);
        Assert.Equal("31", report.UnmatchedTraces[^1].Id);
    }
}
