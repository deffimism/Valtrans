using System.IO;
using System.Security.Cryptography;
using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

public sealed class LitePackageIntegrityTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "valtrans-package-" + Guid.NewGuid().ToString("N"));
    private readonly LiteModelPackage package;

    public LitePackageIntegrityTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "model"));
        var model = MakeFile("model/model.bin", "weights");
        var encoder = MakeFile("source.spm", "source tokens");
        var decoder = MakeFile("target.spm", "target tokens");
        var vocabulary = MakeFile("model/target_vocabulary.json", "vocabulary");
        package = new LiteModelPackage("ko", "en", "test", "https://example.invalid/model.zip", 0,
            "unused", model.Bytes, model.Sha256, encoder.Bytes, encoder.Sha256, "source.spm", [decoder, vocabulary], false);
    }

    private LitePackageFile MakeFile(string relative, string content)
    {
        var path = Path.Combine(root, relative);
        File.WriteAllText(path, content);
        return new LitePackageFile(relative, new FileInfo(path).Length, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
    }

    [Fact]
    public async Task Complete_separate_vocab_package_is_verified()
    {
        Assert.True(ValtransLiteService.HasPackageFileSizes(root, package));
        Assert.True(await ValtransLiteService.VerifyExtractedPackageAsync(root, package, default));
    }

    [Theory]
    [InlineData("source.spm")]
    [InlineData("target.spm")]
    [InlineData("model/target_vocabulary.json")]
    public async Task Missing_encoder_decoder_or_vocabulary_is_not_ready(string relative)
    {
        File.Delete(Path.Combine(root, relative));
        Assert.False(ValtransLiteService.HasPackageFileSizes(root, package));
        Assert.False(await ValtransLiteService.VerifyExtractedPackageAsync(root, package, default));
    }

    [Fact]
    public async Task Same_size_corrupted_decoder_fails_hash_check()
    {
        File.WriteAllText(Path.Combine(root, "target.spm"), "broken tokens");
        Assert.True(ValtransLiteService.HasPackageFileSizes(root, package));
        Assert.False(await ValtransLiteService.VerifyExtractedPackageAsync(root, package, default));
    }

    [Fact]
    public void Shared_package_cannot_hide_half_installed_separate_tokenizers()
    {
        var shared = MakeFile("sentencepiece.model", "shared tokens");
        var sharedPackage = package with { TokenizerFileName = shared.Path, TokenizerBytes = shared.Bytes, TokenizerSha256 = shared.Sha256, ExtraFiles = null };
        Assert.False(ValtransLiteService.HasPackageFileSizes(root, sharedPackage));
    }

    public void Dispose() => Directory.Delete(root, true);
}
