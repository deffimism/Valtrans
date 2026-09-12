using System.Text.Json;
using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

public sealed class OcrWrappedMessageTests
{
    private static OcrPositionedLine Row(string text, int x, int y, int width = 390) =>
        new(text, x, y, width, 20, Array.Empty<OcrPositionedWord>());
    private static IReadOnlyList<OcrMessage> Parse(params OcrPositionedLine[] rows) =>
        OcrMessageParser.Extract(new OcrReadResult("", "MIXED", PositionedLines: rows, ImageWidth: 440));

    [Fact]
    public void Preserves_fast_geometry_for_wrapped_message()
    {
        using var json = JsonDocument.Parse("""
            {"imageWidth":440,"lines":[
              {"text":"[TEAM] PlayerA: Three teammates are", "box":[8,10,425,30]},
              {"text":"holding A main", "box":[8,34,220,54]}]}
            """);
        var result = FastOcrService.ParseRecognition(json.RootElement, "unused", .95, 120);
        Assert.Equal(440, result.ImageWidth);
        Assert.Equal(2, result.PositionedLines!.Count);
        Assert.Equal("Three teammates are holding A main", Assert.Single(OcrMessageParser.Extract(result)).Body);
    }

    [Fact]
    public void Preserves_negation_on_second_row() => Assert.Equal("내가 죽어도 아직 피킹하지 마",
        Assert.Single(Parse(Row("[TEAM] 김데피: 내가 죽어도 아직 피킹하지", 8, 10), Row("마", 8, 34, 20))).Body);

    [Fact]
    public void Keeps_three_indented_rows_in_one_message() => Assert.Equal("first second third",
        Assert.Single(Parse(Row("[TEAM] PlayerA: first", 8, 10, 200),
            Row("second", 120, 34, 60), Row("third", 120, 58, 60))).Body);

    [Fact]
    public void Does_not_merge_a_new_sender() => Assert.Equal(2,
        Parse(Row("[TEAM] PlayerA: wait", 8, 10), Row("[TEAM] PlayerB: go", 8, 34)).Count);

    [Fact]
    public void Does_not_merge_aligned_short_unrelated_rows() => Assert.Equal(2,
        Parse(Row("[TEAM] PlayerA: wait", 8, 10, 180), Row("independent text", 8, 34, 200)).Count);

    [Fact]
    public void Does_not_merge_across_large_vertical_gap() => Assert.Equal(2,
        Parse(Row("[TEAM] PlayerA: wait", 8, 10), Row("independent text", 8, 80)).Count);

    [Theory]
    [InlineData("[0,0,\"oops\",20]")]
    [InlineData("[0,0,0,20]")]
    [InlineData("[-1,0,100,20]")]
    public void Invalid_geometry_never_discards_unpositioned_text(string invalidBox)
    {
        using var json = JsonDocument.Parse("{\"lines\":[{\"text\":\"first\",\"box\":[0,0,100,20]}," +
            "{\"text\":\"second\",\"box\":" + invalidBox + "}]}");
        var result = FastOcrService.ParseRecognition(json.RootElement, "first\nsecond", .8, 123);
        Assert.Null(result.PositionedLines);
        Assert.Equal("first\nsecond", result.Text);
    }

    [Fact]
    public void Legacy_document_response_keeps_plain_text()
    {
        using var json = JsonDocument.Parse("{\"lines\":null}");
        var result = FastOcrService.ParseRecognition(json.RootElement, "watch left", .8, 123);
        Assert.Null(result.PositionedLines);
        Assert.Equal("watch left", result.Text);
        Assert.Equal(123, result.TotalDurationMs);
    }
}
