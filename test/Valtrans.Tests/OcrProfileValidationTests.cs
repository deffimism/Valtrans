using System.Drawing;
using Valtrans.Models;
using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

public class OcrProfileValidationTests
{
    private static readonly Rectangle Bounds = new(0, 0, 1920, 1080);

    // A settings object in the state the app reaches after a normal profile load.
    private static AppSettings SettingsFor(Rectangle bounds, uint dpi = 96, string windowMode = "Borderless")
    {
        var settings = new AppSettings { Game = "VALORANT" };
        settings.CaptureRegion = OcrRegionRecommendationService.Recommend("VALORANT", bounds);
        var key = OcrProfileValidationService.ProfileKey("VALORANT", bounds);
        settings.CaptureRegionsByGame[key] = settings.CaptureRegion.Clone();
        settings.CaptureProfileMetadataByGame[key] = new CaptureProfileMetadata
        {
            ReferenceX = bounds.X,
            ReferenceY = bounds.Y,
            ReferenceWidth = bounds.Width,
            ReferenceHeight = bounds.Height,
            Dpi = dpi,
            WindowMode = windowMode,
            SavedAtUtc = DateTime.UtcNow
        };
        return settings;
    }

    private static OcrProfileValidationResult Validate(AppSettings settings, Rectangle bounds,
        uint dpi = 96, string windowMode = "Borderless", bool gameDetected = true) =>
        OcrProfileValidationService.Validate("VALORANT", settings, bounds, dpi, windowMode, gameDetected);

    [Fact]
    public void Profile_key_is_game_and_resolution()
        => Assert.Equal("VALORANT@1920x1080", OcrProfileValidationService.ProfileKey("VALORANT", Bounds));

    [Fact]
    public void Freshly_stored_profile_is_valid()
    {
        var result = Validate(SettingsFor(Bounds), Bounds);
        Assert.Equal(OcrProfileValidity.Valid, result.Validity);
        Assert.True(result.CanUse);
    }

    [Fact]
    public void Missing_region_is_reported_as_missing()
    {
        var result = Validate(new AppSettings { Game = "VALORANT" }, Bounds);
        Assert.Equal(OcrProfileValidity.Missing, result.Validity);
        Assert.False(result.CanUse);
    }

    // Changing resolution must invalidate, otherwise OCR keeps reading the old rectangle.
    [Fact]
    public void Resolution_change_invalidates_the_profile()
    {
        var settings = SettingsFor(Bounds);
        var result = Validate(settings, new Rectangle(0, 0, 2560, 1440));
        Assert.Equal(OcrProfileValidity.Invalid, result.Validity);
        Assert.False(result.CanUse);
    }

    [Fact]
    public void Windows_scale_change_invalidates_the_profile()
    {
        var settings = SettingsFor(Bounds, dpi: 96);
        var result = Validate(settings, Bounds, dpi: 120);
        Assert.Equal(OcrProfileValidity.Invalid, result.Validity);
    }

    [Fact]
    public void Window_mode_change_invalidates_the_profile()
    {
        var settings = SettingsFor(Bounds, windowMode: "Borderless");
        var result = Validate(settings, Bounds, windowMode: "Windowed");
        Assert.Equal(OcrProfileValidity.Invalid, result.Validity);
    }

    // "Unknown" means the mode could not be read, which is not evidence of a change.
    [Theory]
    [InlineData("Unknown", "Windowed")]
    [InlineData("Borderless", "Unknown")]
    public void Unknown_window_mode_does_not_invalidate(string saved, string live)
    {
        var settings = SettingsFor(Bounds, windowMode: saved);
        Assert.Equal(OcrProfileValidity.Valid, Validate(settings, Bounds, windowMode: live).Validity);
    }

    [Fact]
    public void Region_outside_the_client_area_invalidates()
    {
        var settings = SettingsFor(Bounds);
        settings.CaptureRegion = new CaptureRegion { X = 5000, Y = 5000, Width = 400, Height = 200 };
        Assert.Equal(OcrProfileValidity.Invalid, Validate(settings, Bounds).Validity);
    }

    // With the game closed the resolution cannot be checked, so OCR is allowed to proceed
    // rather than being blocked on unverifiable information.
    [Fact]
    public void Closed_game_defers_verification_but_stays_usable()
    {
        var result = Validate(SettingsFor(Bounds), Bounds, gameDetected: false);
        Assert.Equal(OcrProfileValidity.NotVerifiable, result.Validity);
        Assert.True(result.CanUse);
    }

    // Non-16:9 clients must round-trip too: the recommendation is what gets stored.
    [Theory]
    [InlineData(1280, 960)]
    [InlineData(3440, 1440)]
    [InlineData(1680, 1050)]
    public void Non_reference_aspect_profiles_validate(int width, int height)
    {
        var bounds = new Rectangle(0, 0, width, height);
        Assert.Equal(OcrProfileValidity.Valid, Validate(SettingsFor(bounds), bounds).Validity);
    }
}
