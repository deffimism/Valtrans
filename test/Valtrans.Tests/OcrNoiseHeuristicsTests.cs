using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

public sealed class OcrNoiseHeuristicsTests
{
    [Theory]
    [InlineData("watch 厄 升")]
    [InlineData("rotate 中 now")]
    [InlineData("enemy 升 mid")]
    public void Flags_single_han_characters_inside_a_latin_sentence(string text)
        => Assert.True(OcrNoiseHeuristics.HasScatteredHanNoise(text));

    [Theory]
    [InlineData("jett残血")]          // real Chinese word: contiguous han run
    [InlineData("A小两个")]
    [InlineData("左見て")]             // kana present, never noise
    [InlineData("watch left")]        // no han at all
    [InlineData("B 헤븐 2명")]
    [InlineData("中")]                 // too little Latin context to judge
    public void Keeps_real_cjk_content_and_plain_latin(string text)
        => Assert.False(OcrNoiseHeuristics.HasScatteredHanNoise(text));
}

public sealed class OcrLineSelectorTests
{
    private static OcrPositionedLine Line(string text, int y = 10, int height = 20)
        => new(text, 0, y, Math.Max(20, text.Length * 8), height,
            text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select((word, index) => new OcrPositionedWord(word, index * 40, y, 36, height))
                .ToArray());

    private static OcrReadResult Select(string english, string japanese, string korean = "")
        => OcrLineSelector.Select(new Dictionary<string, IReadOnlyList<OcrPositionedLine>>
        {
            ["EN"] = english.Length == 0 ? Array.Empty<OcrPositionedLine>() : new[] { Line(english) },
            ["JP"] = japanese.Length == 0 ? Array.Empty<OcrPositionedLine>() : new[] { Line(japanese) },
            ["KO"] = korean.Length == 0 ? Array.Empty<OcrPositionedLine>() : new[] { Line(korean) }
        });

    [Fact]
    public void Prefers_english_when_japanese_engine_returns_scattered_han_for_latin_glyphs()
    {
        var result = Select("watch left", "watch 厄 升");
        Assert.Equal("watch left", result.Text);
        Assert.Equal("EN", result.DetectedLanguage);
    }

    [Fact]
    public void Still_prefers_japanese_for_real_kana_content()
    {
        var result = Select("B 7 ss", "Bラッシュ");
        Assert.Equal("Bラッシュ", result.Text);
    }

    [Fact]
    public void Still_prefers_korean_for_hangul_content()
    {
        var result = Select("B heaven 2", "", "B 헤븐 2명");
        Assert.Equal("B 헤븐 2명", result.Text);
    }
}
