using System.IO;
using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

public sealed class LitePackageActivationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "valtrans-activation-" + Guid.NewGuid().ToString("N"));
    public LitePackageActivationTests() => Directory.CreateDirectory(root);

    [Fact]
    public void Verified_staging_replaces_only_the_selected_pair()
    {
        var destination = Path.Combine(root, "ko_en");
        var staging = Path.Combine(root, "ko_en.installing");
        Directory.CreateDirectory(destination);
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(destination, "model"), "previous");
        File.WriteAllText(Path.Combine(staging, "model"), "verified-new");
        File.WriteAllText(Path.Combine(root, "keep-other-pair"), "unchanged");
        ValtransLiteService.ActivateVerifiedPackage(staging, destination);
        Assert.Equal("verified-new", File.ReadAllText(Path.Combine(destination, "model")));
        Assert.Equal("unchanged", File.ReadAllText(Path.Combine(root, "keep-other-pair")));
        Assert.False(Directory.Exists(staging));
    }

    [Fact]
    public void Missing_staging_does_not_erase_previous_model()
    {
        var destination = Path.Combine(root, "ko_en");
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, "model"), "previous");
        Assert.Throws<DirectoryNotFoundException>(() =>
            ValtransLiteService.ActivateVerifiedPackage(Path.Combine(root, "missing"), destination));
        Assert.Equal("previous", File.ReadAllText(Path.Combine(destination, "model")));
    }

    [Fact]
    public void Rejects_identical_or_non_sibling_targets()
    {
        Assert.Throws<InvalidDataException>(() => ValtransLiteService.ActivateVerifiedPackage(root, root));
        Assert.Throws<InvalidDataException>(() =>
            ValtransLiteService.ActivateVerifiedPackage(root, Path.Combine(root, "nested")));
    }

    public void Dispose() => Directory.Delete(root, true);
}
