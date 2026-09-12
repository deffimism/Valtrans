using System.Net;
using System.Net.Http;
using Valtrans.Models;
using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

public sealed class HybridFailurePolicyTests
{
    [Fact]
    public async Task Hybrid_compatibility_checks_only_the_selected_AI_not_unused_Lite()
    {
        using var handler = new UnavailableModel();
        using var http = new HttpClient(handler);
        using var lite = new ValtransLiteService();
        var service = new TranslatorService(new GlossaryService(), new LocalAiService(), lite, http);
        var report = await service.TestCompatibilityAsync(new AppSettings { TranslationProvider = "Hybrid" });
        Assert.Equal(2, report.Total);
        Assert.Equal(2, handler.Calls);
        Assert.All(report.Results, result => Assert.NotEqual("Valtrans Lite", result.Engine));
        Assert.Null(service.LastLitePivotMetrics);
    }

    private sealed class UnavailableModel : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            { Content = new StringContent("{}") });
        }
    }

    [Fact]
    public async Task Unverified_free_text_does_not_fall_back_to_Lite_when_the_model_fails()
    {
        using var handler = new UnavailableModel();
        using var http = new HttpClient(handler);
        using var lite = new ValtransLiteService();
        var service = new TranslatorService(new GlossaryService(), new LocalAiService(), lite, http);
        var settings = new AppSettings { TranslationProvider = "Hybrid", Game = "VALORANT" };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.TranslateAsync("오늘은 친구랑 저녁 먹고 늦게 접속할 것 같아", "EN", settings));
        Assert.Contains("검증되지 않은 Lite", error.Message);
        Assert.Equal("번역 보류", service.LastHybridRoute);
        Assert.Equal(1, handler.Calls);
        Assert.Null(service.LastLitePivotMetrics);
    }

    [Fact]
    public async Task Verified_complete_callouts_remain_available_without_the_model()
    {
        using var handler = new UnavailableModel();
        using var http = new HttpClient(handler);
        using var lite = new ValtransLiteService();
        var service = new TranslatorService(new GlossaryService(), new LocalAiService(), lite, http);
        var result = await service.TranslateAsync("Aヘブン三人", "KO",
            new AppSettings { TranslationProvider = "Hybrid", Game = "VALORANT" });
        Assert.Equal("A 헤븐 3명", result);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task User_cancellation_does_not_trigger_a_replacement_translation()
    {
        using var handler = new UnavailableModel();
        using var http = new HttpClient(handler);
        using var lite = new ValtransLiteService();
        var service = new TranslatorService(new GlossaryService(), new LocalAiService(), lite, http);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.TranslateAsync(
            "오늘은 친구랑 저녁 먹고 접속할게", "EN", new AppSettings { TranslationProvider = "Hybrid" },
            cancellationToken: cancellation.Token));
        Assert.Equal(0, handler.Calls);
    }
}
