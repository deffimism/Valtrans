using System.IO;

namespace Valtrans.Services;

public sealed partial class ValtransLiteService
{
    // Include this asset in v0.5.0-beta and verify its public download before
    // announcing the release. Existing Argos users remain ready meanwhile.
    internal static LiteModelPackage CreateKoreanEnglishPackage() => new(
        "ko", "en", "2.0",
        "https://github.com/deffimism/Valtrans/releases/download/v0.5.0-beta/valtrans-lite-ko-en-opus-20220728-int8.zip",
        200_961_112, "ce9b357a409bf0a1d71c5bd3f7ebdf1ae4f67c2eac22c4fda630762cd7ce2a73",
        218_915_789, "641b3f89e45d080d6b1d22b88c4bb75e2de4e75d5165566c7ef594b06de8db6c",
        815_483, "bcf0b63c9685de766ab3e10571a9a7f4a38eb39da51a4d64462757051d1986c7",
        "source.spm",
        [
            new("target.spm", 789_870, "7188e2e643ef82257f2d0d5e2592185a42b0e472aff8ee6880331a0bff8904cd"),
            new("model/config.json", 231, "95dabd564e6abdb470e95f9106766647d8dc59e1bc2a1517d292c0cee5ca5812"),
            new("model/source_vocabulary.json", 777_969, "e9d0300fce8fa7a734a7cede1b0faf9dba3328432652e607d2c8eebdb026e8b1"),
            new("model/target_vocabulary.json", 562_596, "a87e56c72f921d1c1fffe747098228bdb7a5cb7b16b86372a34419b1bb275028")
        ], IsArgosIndex: false);

    private static LiteModelPackage LegacyKoreanEnglishPackage => new(
        "ko", "en", "1.1", "https://argos-net.com/v1/translate-ko_en-1_1.argosmodel", 118_852_077,
        "6da8f3db6ca40f42b1875570a1c06856f6e17c7ef62845d85de217ba548c1471", 132_573_333,
        "30170587ac837b737a749d4f821feb0925937cc9cbbedcf6aed07b9cf974f6ad", 775_609,
        "98506598307754afd153041193f46cf8b07c5dadbd5ffbebf67ef1c45ffbe74b");

    private bool HasLegacyKoreanEnglish() => HasPackageFileSizes(PairDirectory("ko", "en"), LegacyKoreanEnglishPackage);

    private static IEnumerable<LitePackageFile> RequiredPackageFiles(LiteModelPackage package)
    {
        yield return new("model/model.bin", package.ModelBytes, package.ModelSha256);
        yield return new(package.TokenizerFileName, package.TokenizerBytes, package.TokenizerSha256);
        foreach (var extra in package.ExtraFiles ?? []) yield return extra;
    }

    internal static bool HasPackageFileSizes(string directory, LiteModelPackage package)
    {
        if (package.TokenizerFileName == "sentencepiece.model" &&
            (File.Exists(Path.Combine(directory, "source.spm")) || File.Exists(Path.Combine(directory, "target.spm")))) return false;
        return RequiredPackageFiles(package).All(file =>
        {
            var path = Path.Combine(directory, file.Path);
            return File.Exists(path) && new FileInfo(path).Length == file.Bytes;
        });
    }
}
