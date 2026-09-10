using System.IO;
using Valtrans.TestArena.Models;
using Valtrans.TestArena.Services;
using Xunit;

namespace Valtrans.Tests;

public sealed class ArenaHelpersTests
{
    [Theory]
    [InlineData("1920x1080", 1920, 1080)]
    [InlineData("2560X1440", 2560, 1440)]
    [InlineData("", 1280, 720)]
    [InlineData("invalid", 1280, 720)]
    public void ResolutionHelper_Parse_maps_expected_sizes(string input, int width, int height)
    {
        var (parsedWidth, parsedHeight) = ArenaResolutionHelper.Parse(input);
        Assert.Equal(width, parsedWidth);
        Assert.Equal(height, parsedHeight);
    }

    [Theory]
    [InlineData(96, 1.0)]
    [InlineData(120, 1.25)]
    [InlineData(0, 100 / 96.0)]
    [InlineData(300, 200 / 96.0)]
    public void ResolutionHelper_DpiScale_clamps_and_normalizes(int dpi, double expected)
    {
        Assert.Equal(expected, ArenaResolutionHelper.DpiScale(dpi), 3);
    }

    [Fact]
    public void VisualFuzzer_is_deterministic_for_same_seed_and_line()
    {
        var chat = new ArenaChatSettings { FontSize = 16, Opacity = 0.9 };
        var fuzz = new ArenaFuzzSettings
        {
            OpacityMin = 0.8,
            OpacityMax = 1.0,
            FontSizeJitter = 0.1,
            BrightnessJitter = 0.05
        };
        var randomA = new DeterministicRandom(42);
        var randomB = new DeterministicRandom(42);
        var styleA = ArenaVisualFuzzer.FuzzLine(chat, fuzz, randomA, 3);
        var styleB = ArenaVisualFuzzer.FuzzLine(chat, fuzz, randomB, 3);
        Assert.Equal(styleA.FontSize, styleB.FontSize);
        Assert.Equal(styleA.Opacity, styleB.Opacity);
    }

    [Fact]
    public void VisualFuzzer_respects_opacity_bounds()
    {
        var chat = new ArenaChatSettings { FontSize = 16, Opacity = 0.9 };
        var fuzz = new ArenaFuzzSettings
        {
            OpacityMin = 0.7,
            OpacityMax = 0.85,
            FontSizeJitter = 0.2,
            BrightnessJitter = 0.3
        };
        var random = new DeterministicRandom(99);
        for (var line = 0; line < 20; line++)
        {
            var style = ArenaVisualFuzzer.FuzzLine(chat, fuzz, random, line);
            Assert.InRange(style.Opacity, fuzz.OpacityMin, fuzz.OpacityMax);
            Assert.InRange(style.FontSize, 10, 32);
        }
    }

    [Fact]
    public void BackgroundAnimator_creates_brush_for_gradient_and_noise()
    {
        var gradient = ArenaBackgroundAnimator.CreateBrush(
            new ArenaBackgroundSettings { Mode = "animated-gradient" }, 0.5, null);
        var noise = ArenaBackgroundAnimator.CreateBrush(
            new ArenaBackgroundSettings { Mode = "noise" }, 0, new DeterministicRandom(7));
        Assert.NotNull(gradient);
        Assert.NotNull(noise);
    }

    [Fact]
    public void BackgroundAnimator_animated_mode_varies_with_phase()
    {
        var settings = new ArenaBackgroundSettings { Mode = "animated-gradient", Brightness = 1.0, MotionSpeed = 1 };
        var early = ArenaBackgroundAnimator.Opacity(settings, 0);
        var late = ArenaBackgroundAnimator.Opacity(settings, Math.PI / 2);
        Assert.NotEqual(early, late);
    }

    [Fact]
    public void ScenarioLoader_reads_phase8_fuzz_fields()
    {
        var root = WorkspaceRoot();
        var path = Path.Combine(root, "testdata", "scenarios", "arena_fuzz_001.json");
        var scenario = ScenarioLoader.Load(path);
        Assert.Equal("arena_fuzz_001", scenario.Scenario);
        Assert.Equal(125, scenario.Dpi);
        Assert.Contains("fuzz", scenario.Tags);
        Assert.Equal("animated-gradient", scenario.Background.Mode);
        Assert.True(scenario.Animation.BackgroundMotion);
        Assert.True(scenario.Chat.ScrollEnabled);
        Assert.True(scenario.Animation.ChatFadeIn);
        Assert.Equal(150, scenario.Chat.FadeInMs);
        Assert.Equal(0.08, scenario.Fuzz.FontSizeJitter);
    }

    private static string WorkspaceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Valtrans.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Workspace root not found");
    }
}
