using System.IO;
using Valtrans.TestArena.Services;
using Xunit;

namespace Valtrans.Tests;

public sealed class ArenaLayoutStoreTests
{
    [Fact]
    public void Save_and_TryLoad_roundtrip_layout()
    {
        var path = Path.Combine(Path.GetTempPath(), "valtrans-layout-test", Guid.NewGuid().ToString("N") + ".json");
        try
        {
            ArenaLayoutStore.Save(-3068, 594, 1920, 1080, path);
            var loaded = ArenaLayoutStore.TryLoad(path);
            Assert.NotNull(loaded);
            Assert.Equal(-3068, loaded.Left);
            Assert.Equal(594, loaded.Top);
            Assert.Equal(1920, loaded.Width);
            Assert.Equal(1080, loaded.Height);
            Assert.False(string.IsNullOrWhiteSpace(loaded.SavedUtc));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Save_skips_too_small_dimensions()
    {
        var path = Path.Combine(Path.GetTempPath(), "valtrans-layout-test", Guid.NewGuid().ToString("N") + ".json");
        try
        {
            ArenaLayoutStore.Save(0, 0, 100, 100, path);
            Assert.Null(ArenaLayoutStore.TryLoad(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void TryLoad_returns_null_for_missing_file()
    {
        var path = Path.Combine(Path.GetTempPath(), "valtrans-layout-test", Guid.NewGuid().ToString("N") + ".json");
        Assert.Null(ArenaLayoutStore.TryLoad(path));
    }
}
