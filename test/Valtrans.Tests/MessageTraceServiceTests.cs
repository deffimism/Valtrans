using System.IO;
using Valtrans.Models;
using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

public sealed class MessageTraceServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly MessageTraceService _service;

    public MessageTraceServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "valtrans-trace-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _service = new MessageTraceService(
            Path.Combine(_tempDir, "message-traces.jsonl"),
            Path.Combine(_tempDir, "samples"))
        {
            Enabled = true,
            SaveErrorSamples = true
        };
    }

    [Fact]
    public void Begin_assigns_incrementing_trace_id()
    {
        var first = _service.Begin(CreateBeginRequest("Bラッシュ", "Bラッシュ", "Bラッシュ"));
        var second = _service.Begin(CreateBeginRequest("A rush", "A rush", "A rush"));
        Assert.StartsWith("msg-", first);
        Assert.StartsWith("msg-", second);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Complete_persists_separated_ocr_and_translation_fields()
    {
        var id = _service.Begin(CreateBeginRequest("Bラッシュ", "Bラッシュ", "Bラッシュ"));
        _service.Complete(new MessageTraceCompleteRequest(
            "Bラッシュ",
            "B 러시",
            "Hybrid",
            "valtrans-hymt2:1.8b",
            "규칙",
            false,
            42,
            true,
            false,
            null,
            "completed"));

        var record = _service.Get(id);
        Assert.NotNull(record);
        Assert.Equal("Bラッシュ", record!.Ocr.Raw);
        Assert.Equal("Bラッシュ", record.Normalization.Text);
        Assert.Equal("Bラッシュ", record.Translation.Input);
        Assert.Equal("B 러시", record.Translation.Output);
        Assert.Equal("completed", record.Outcome);
        Assert.True(File.Exists(_service.LogPath));
    }

    [Fact]
    public void Complete_failure_writes_error_sample()
    {
        _service.Begin(CreateBeginRequest("bad", "bad", "bad"));
        _service.Complete(new MessageTraceCompleteRequest(
            "bad", "", "Hybrid", null, null, false, 10, false, false, null,
            "failed", "translation", "timeout"));

        Assert.Single(Directory.GetFiles(_service.SamplesDirectory, "*.json"));
    }

    [Fact]
    public void ReadFromLog_round_trips_records()
    {
        _service.Begin(CreateBeginRequest("one", "one", "one"));
        _service.Complete(new MessageTraceCompleteRequest(
            "one", "uno", "Lite", null, null, false, 5, true, false, null, "completed"));

        var records = MessageTraceService.ReadFromLog(_service.LogPath);
        Assert.Single(records);
        Assert.Equal("one", records[0].Ocr.Raw);
        Assert.Equal("uno", records[0].Translation.Output);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private static MessageTraceBeginRequest CreateBeginRequest(string raw, string normalized, string input) => new(
        raw, normalized, input, "Tactical", "게임 콜아웃", "게임 콜아웃",
        "Paddle", "latest_line", "JP", "VALORANT", 10, 20, 0);
}
