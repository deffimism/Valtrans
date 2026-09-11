using System.IO;
using System.Text.Json;
using Valtrans.Models;
using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

// Fast and Hybrid arrived after the load-time engine normalization was written, so
// choosing either of them silently reverted to Paddle on the next launch.
public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"valtrans-settings-{Guid.NewGuid():N}");

    private SettingsService Service() => new(Path.Combine(_directory, "settings.json"));

    private void WriteSettings(AppSettings settings)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "settings.json"),
            JsonSerializer.Serialize(settings));
    }

    [Theory]
    [InlineData("Windows")]
    [InlineData("Paddle")]
    [InlineData("Fast")]
    [InlineData("Hybrid")]
    public void Keeps_the_selected_ocr_engine_across_a_reload(string engine)
    {
        WriteSettings(new AppSettings { OcrEngine = engine, SettingsSchemaVersion = 26 });
        Assert.Equal(engine, Service().Load().OcrEngine);
    }

    [Theory]
    [InlineData("")]
    [InlineData("SomethingRetired")]
    public void Falls_back_to_paddle_for_unknown_engines(string engine)
    {
        WriteSettings(new AppSettings { OcrEngine = engine, SettingsSchemaVersion = 26 });
        Assert.Equal("Paddle", Service().Load().OcrEngine);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
    }
}
