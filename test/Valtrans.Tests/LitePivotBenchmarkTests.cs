using System.Diagnostics;
using System.IO;
using Valtrans.Models;
using Valtrans.Services;
using Xunit;
using Xunit.Abstractions;

namespace Valtrans.Tests;

public sealed class LitePivotBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public LitePivotBenchmarkTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Measure_jp_ko_pivot_vs_direct_en_ko_when_lite_ready()
    {
        var lite = new ValtransLiteService();
        if (!lite.GetStatus().Ready)
        {
            _output.WriteLine("SKIP: Valtrans Lite is not installed on this machine.");
            return;
        }

        await lite.WarmUpAsync("en", "ko");
        await lite.WarmUpAsync("ja", "en");
        await lite.WarmUpAsync("en", "ko");

        var glossary = new GlossaryService();
        var translator = new TranslatorService(glossary, new LocalAiService(), lite);
        var settings = new AppSettings
        {
            TranslationProvider = "Lite",
            Game = "VALORANT",
            OverlayTargetLanguage = "KO"
        };

        var directWatch = Stopwatch.StartNew();
        await lite.TranslateAsync(new[] { "watch left" }, "EN", "KO");
        directWatch.Stop();
        var directMs = directWatch.Elapsed.TotalMilliseconds;

        // Plain chat that should not short-circuit through glossary rules before Lite runs.
        var pivotWatch = Stopwatch.StartNew();
        var jpResult = await translator.TranslateAsync("敵が後ろにいるよ", "KO", settings);
        pivotWatch.Stop();

        var metrics = translator.LastLitePivotMetrics;
        if (metrics is null)
        {
            _output.WriteLine($"SKIP: Lite path did not run (result={jpResult}). Host may be installed but not warm.");
            return;
        }
        Assert.True(metrics.UsedEnglishPivot);
        Assert.True(metrics.ModelCalls >= 1);
        Assert.True(metrics.PivotLegMs > 0);

        var ratio = metrics.TotalMs / Math.Max(1, directMs);
        var report = $"""
            Lite pivot benchmark ({DateTime.UtcNow:O})
            Direct EN->KO: {directMs:F1} ms (1 model call)
            JP->KO via pivot: {metrics.TotalMs:F1} ms ({metrics.ModelCalls} model calls)
              pivot leg ({metrics.SourceLanguage}->EN): {metrics.PivotLegMs:F1} ms
              second leg (EN->{metrics.TargetLanguage}): {metrics.SecondLegMs:F1} ms
              callout short-circuits: {metrics.CalloutShortCircuits}/{metrics.Lines}
            Ratio pivot/direct: {ratio:F2}x
            JP input: 敵が後ろにいるよ
            KO output: {jpResult}
            """;
        _output.WriteLine(report);

        var reportPath = Path.Combine(Path.GetTempPath(), "valtrans-lite-pivot-benchmark.txt");
        await File.WriteAllTextAsync(reportPath, report);
        _output.WriteLine($"Report: {reportPath}");
    }
}
