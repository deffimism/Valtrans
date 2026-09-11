using System.Drawing;
using Valtrans.Models;
using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

public class OcrRegionRecommendationTests
{
    private static CaptureRegion Recommend(int width, int height) =>
        OcrRegionRecommendationService.Recommend("VALORANT", new Rectangle(0, 0, width, height));

    // Pins the measured 16:9 output. Any change to the aspect handling must leave these alone.
    [Theory]
    [InlineData(1920, 1080, 26, 787, 444, 242)]
    [InlineData(2560, 1440, 35, 1049, 591, 323)]
    [InlineData(3840, 2160, 53, 1573, 886, 486)]
    [InlineData(1280, 720, 18, 524, 295, 162)]
    public void Reference_aspect_region_is_unchanged(int width, int height,
        int expectedX, int expectedY, int expectedWidth, int expectedHeight)
    {
        var region = Recommend(width, height);
        Assert.Equal(expectedX, region.X);
        Assert.Equal(expectedY, region.Y);
        Assert.Equal(expectedWidth, region.Width);
        Assert.Equal(expectedHeight, region.Height);
    }

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    [InlineData(1366, 768)]
    public void Sixteen_by_nine_counts_as_reference_aspect(int width, int height)
        => Assert.True(OcrRegionRecommendationService.IsReferenceAspect(width, height));

    [Theory]
    [InlineData(1280, 960)]   // 4:3, common in VALORANT
    [InlineData(1440, 1080)]  // 4:3
    [InlineData(1680, 1050)]  // 16:10
    [InlineData(3440, 1440)]  // 21:9 ultrawide
    public void Other_aspects_are_not_reference(int width, int height)
        => Assert.False(OcrRegionRecommendationService.IsReferenceAspect(width, height));

    // The chat box is anchored bottom-left and scaled by height, so two clients of the same
    // height get the same box no matter how wide the screen is.
    [Fact]
    public void Ultrawide_keeps_the_same_box_as_sixteen_by_nine_of_equal_height()
    {
        var wide = Recommend(3440, 1440);
        var reference = Recommend(2560, 1440);
        Assert.Equal(reference.X, wide.X);
        Assert.Equal(reference.Width, wide.Width);
        Assert.Equal(reference.Y, wide.Y);
        Assert.Equal(reference.Height, wide.Height);
    }

    // Multiplying by the real width would have shrunk the box on a 4:3 client, cutting off
    // the right side of longer callouts.
    [Fact]
    public void Four_by_three_box_is_wider_than_the_raw_width_fraction_would_give()
    {
        var region = Recommend(1280, 960);
        var naive = (int)Math.Round(1280 * (OcrRegionRecommendationService.ValorantRight -
                                            OcrRegionRecommendationService.ValorantLeft));
        Assert.True(region.Width > naive, $"width={region.Width} naive={naive}");
    }

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(1280, 960)]
    [InlineData(3440, 1440)]
    [InlineData(2560, 1080)]
    [InlineData(800, 600)]
    public void Region_stays_inside_the_client_area(int width, int height)
    {
        var region = Recommend(width, height);
        Assert.True(region.IsValid);
        Assert.True(region.X >= 0 && region.Y >= 0);
        Assert.True(region.X + region.Width <= width, $"right={region.X + region.Width} width={width}");
        Assert.True(region.Y + region.Height <= height, $"bottom={region.Y + region.Height} height={height}");
    }

    // The region excludes the input row, which is what keeps the typed draft out of OCR.
    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(1280, 960)]
    public void Region_excludes_the_input_row(int width, int height)
    {
        var region = Recommend(width, height);
        var inputTop = height * (1 - OcrRegionRecommendationService.ValorantInputTopFromBottom);
        Assert.True(region.Y + region.Height <= Math.Ceiling(inputTop));
    }

    [Fact]
    public void Region_is_offset_by_the_client_origin()
    {
        var atOrigin = OcrRegionRecommendationService.Recommend("VALORANT", new Rectangle(0, 0, 1920, 1080));
        var moved = OcrRegionRecommendationService.Recommend("VALORANT", new Rectangle(-3000, 250, 1920, 1080));
        Assert.Equal(atOrigin.X - 3000, moved.X);
        Assert.Equal(atOrigin.Y + 250, moved.Y);
        Assert.Equal(atOrigin.Width, moved.Width);
        Assert.Equal(atOrigin.Height, moved.Height);
    }

    [Fact]
    public void Tiny_bounds_are_rejected()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            OcrRegionRecommendationService.Recommend("VALORANT", new Rectangle(0, 0, 10, 10)));

    // The latest-line band must sit at the bottom of the full region and stay inside it,
    // because that is the crop the production OCR loop reads every frame.
    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(1280, 960)]
    [InlineData(3440, 1440)]
    public void Latest_line_band_sits_at_the_bottom_of_the_region(int width, int height)
    {
        var full = Recommend(width, height).ToRectangle();
        var latest = DualOcrRegions.Resolve(full, OcrRegionRecommendationService.DefaultValorantLatestRegion());
        Assert.True(latest.Bottom <= full.Bottom, $"latest={latest} full={full}");
        Assert.True(latest.Top > full.Top, "band must exclude the oldest lines");
        Assert.True(latest.Height < full.Height);
        Assert.Equal(full.Left, latest.Left);
    }
}
