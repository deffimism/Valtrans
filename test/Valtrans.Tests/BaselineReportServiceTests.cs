using Valtrans.Models;
using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

public sealed class BaselineReportServiceTests
{
    [Fact]
    public void Build_maps_passing_run_to_baseline_metrics()
    {
        var run = new TestRunReport
        {
            Scenario = "smoke_basic_001",
            Status = "PASS",
            Cases =
            {
                new TestRunCaseResult
                {
                    Source = "Bラッシュ",
                    Status = "PASS",
                    OcrRaw = "B ラッシュ",
                    TranslationOutput = "B 러시",
                    TraceId = "msg-0000001",
                    TotalLatencyMs = 120
                }
            }
        };
        var scenario = new TestScenarioFile
        {
            Scenario = "smoke_basic_001",
            Expectations =
            {
                new TestScenarioExpectation
                {
                    Source = "Bラッシュ",
                    AcceptedTranslations = { "B 러시" }
                }
            }
        };
        var traces = new List<MessageTraceRecord>
        {
            new()
            {
                Id = "msg-0000001",
                Ocr = new MessageTraceOcr { LatencyMs = 40 },
                Translation = new MessageTraceTranslation { LatencyMs = 55 }
            }
        };

        var baseline = BaselineReportService.Build(run, scenario, "Windows", 20260910, "v0.3.0", traces);

        Assert.Equal("PASS", baseline.E2EStatus);
        Assert.Equal(1, baseline.Accuracy.TotalCases);
        Assert.Equal(1, baseline.Accuracy.PassedCases);
        Assert.Equal(1.0, baseline.Accuracy.CasePassRate);
        Assert.Equal(40, baseline.Latency.OcrP50Ms);
        Assert.Equal(55, baseline.Latency.TranslationP50Ms);
        Assert.Equal(120, baseline.Latency.E2EP50Ms);
    }

    [Fact]
    public void Compare_flags_regression_when_pass_rate_drops()
    {
        var baseline = BaselineReportService.Build(
            SampleRun("PASS"),
            SampleScenario(),
            "Windows",
            1);
        var current = BaselineReportService.Build(
            SampleRun("FAIL"),
            SampleScenario(),
            "Windows",
            1);

        var result = BaselineReportService.Compare(baseline, current);

        Assert.Equal("FAIL", result.Status);
        Assert.Contains(result.Issues, issue => issue.Contains("pass rate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Compare_warns_on_latency_increase_without_fail_threshold()
    {
        var baselineRun = SampleRun("PASS");
        baselineRun.Cases[0].TotalLatencyMs = 100;
        var currentRun = SampleRun("PASS");
        currentRun.Cases[0].TotalLatencyMs = 115;
        var baseline = BaselineReportService.Build(baselineRun, SampleScenario(), "Windows", 1);
        var current = BaselineReportService.Build(currentRun, SampleScenario(), "Windows", 1);

        var result = BaselineReportService.Compare(baseline, current, latencyWarningPercent: 10, latencyFailPercent: 20);

        Assert.Equal("WARN", result.Status);
        Assert.NotEmpty(result.Warnings);
    }

    private static TestRunReport SampleRun(string status) => new()
    {
        Scenario = "smoke_basic_001",
        Status = status,
        Cases =
        {
            new TestRunCaseResult
            {
                Source = "Bラッシュ",
                Status = status,
                OcrRaw = "B ラッシュ",
                TranslationOutput = status == "PASS" ? "B 러시" : "wrong",
                TotalLatencyMs = 100
            }
        }
    };

    private static TestScenarioFile SampleScenario() => new()
    {
        Scenario = "smoke_basic_001",
        Expectations =
        {
            new TestScenarioExpectation
            {
                Source = "Bラッシュ",
                AcceptedTranslations = { "B 러시" }
            }
        }
    };
}
