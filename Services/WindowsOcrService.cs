using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;

namespace Valtrans.Services;

public sealed class WindowsOcrService
{
    private readonly object _engineLock = new();
    private readonly Dictionary<string, OcrEngine> _engines = new(StringComparer.OrdinalIgnoreCase);

    public async Task<ulong> CaptureFrameHashAsync(Rectangle region)
    {
        if (region.Width < 20 || region.Height < 20) return 0UL;
        return await Task.Run(() =>
        {
            using var bitmap = CaptureBitmap(region);
            return ComputeFrameHash(bitmap);
        }).ConfigureAwait(false);
    }

    public async Task<byte[]> CapturePngAsync(Rectangle region)
        => (await CaptureFramePngAsync(region)).Png;

    public async Task<CapturedFramePng> CaptureFramePngAsync(Rectangle region)
    {
        if (region.Width < 20 || region.Height < 20)
            return new CapturedFramePng(0, Array.Empty<byte>(), 0);
        return await Task.Run(() =>
        {
            var watch = Stopwatch.StartNew();
            using var bitmap = CaptureBitmap(region);
            var hash = ComputeFrameHash(bitmap);
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return new CapturedFramePng(hash, stream.ToArray(), watch.Elapsed.TotalMilliseconds);
        }).ConfigureAwait(false);
    }

    public async Task<string> ReadAsync(Rectangle region, string languageCode)
        => (await ReadDetailedAsync(region, languageCode)).Text;

    public async Task<OcrReadResult> ReadPngAsync(byte[] png, IReadOnlyCollection<string> languages,
        string enhancementMode = OcrEnhancementModes.Auto)
    {
        var watch = Stopwatch.StartNew();
        using var stream = new MemoryStream(png, writable: false);
        using var bitmap = new Bitmap(stream);
        if (bitmap.Width < 20 || bitmap.Height < 20 || (long)bitmap.Width * bitmap.Height > 4_000_000)
            throw new ArgumentException("Invalid OCR image dimensions");
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var bytes = new byte[bitmap.Width * bitmap.Height * 4];
        try
        {
            for (var y = 0; y < bitmap.Height; y++)
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), bytes, y * bitmap.Width * 4, bitmap.Width * 4);
        }
        finally { bitmap.UnlockBits(data); }
        var buffer = CryptographicBuffer.CreateFromByteArray(bytes);
        using var software = SoftwareBitmap.CreateCopyFromBuffer(buffer, BitmapPixelFormat.Bgra8,
            bitmap.Width, bitmap.Height, BitmapAlphaMode.Ignore);
        return await ReadFrameAsync(new CapturedFrame(software, new CapturedPixels(bytes, bitmap.Width, bitmap.Height, 0), 0),
            languages, null, null, true, enhancementMode, 0, watch);
    }

    public async Task<OcrReadResult> ReadDetailedAsync(Rectangle region, string languageCode,
        ulong? previousFrameHash = null, ulong? expectedFrameHash = null, bool autoEnhance = true)
    {
        var languages = languageCode.Equals("AUTO", StringComparison.OrdinalIgnoreCase)
            ? new[] { "EN", "JP", "KO" }
            : new[] { languageCode };
        return await ReadDetailedAsync(region, languages, previousFrameHash, expectedFrameHash, autoEnhance,
            OcrEnhancementModes.Auto);
    }

    public async Task<OcrReadResult> ReadDetailedAsync(Rectangle region, IReadOnlyCollection<string> languageCodes,
        ulong? previousFrameHash = null, ulong? expectedFrameHash = null, bool autoEnhance = true,
        string enhancementMode = OcrEnhancementModes.Auto)
    {
        if (region.Width < 20 || region.Height < 20) return new OcrReadResult("", "EN");
        var totalWatch = Stopwatch.StartNew();
        var captureWatch = Stopwatch.StartNew();
        var captured = await CaptureAsync(region);
        captureWatch.Stop();
        var captureMs = captureWatch.Elapsed.TotalMilliseconds;
        using (captured.Bitmap)
            return await ReadFrameAsync(captured, languageCodes, previousFrameHash, expectedFrameHash,
                autoEnhance, enhancementMode, captureMs, totalWatch);
    }

    private async Task<OcrReadResult> ReadFrameAsync(CapturedFrame captured, IReadOnlyCollection<string> languageCodes,
        ulong? previousFrameHash, ulong? expectedFrameHash, bool autoEnhance, string enhancementMode,
        double captureMs, Stopwatch totalWatch)
    {
        var softwareBitmap = captured.Bitmap;
        var region = new Rectangle(0, 0, captured.Pixels.Width, captured.Pixels.Height);
        if (expectedFrameHash.HasValue && expectedFrameHash.Value != captured.FrameHash)
            return new OcrReadResult("", languageCodes.FirstOrDefault() ?? "EN", captured.FrameHash, false,
                CaptureDurationMs: captureMs, TotalDurationMs: totalWatch.Elapsed.TotalMilliseconds);
        if (previousFrameHash.HasValue && previousFrameHash.Value == captured.FrameHash)
            return new OcrReadResult("", languageCodes.FirstOrDefault() ?? "EN", captured.FrameHash, false,
                CaptureDurationMs: captureMs, TotalDurationMs: totalWatch.Elapsed.TotalMilliseconds);

        var selected = languageCodes
            .Where(code => code is "EN" or "JP" or "KO")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (selected.Length == 0) selected = new[] { "EN" };
        // Recognition must also see Korean UI/system text. Translation language
        // selection is enforced on extracted message bodies, not on OCR engines.
        if (!selected.Contains("KO") && OcrEngine.IsLanguageSupported(new Language("ko-KR")))
            selected = selected.Append("KO").ToArray();

        enhancementMode = OcrEnhancementModes.Normalize(enhancementMode);
        if (enhancementMode == OcrEnhancementModes.Binary)
        {
            var preprocessing = Stopwatch.StartNew();
            var binary = OcrImagePreprocessor.Binarize(captured.Pixels.Bytes, region.Width, region.Height);
            var buffer = CryptographicBuffer.CreateFromByteArray(binary);
            using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(buffer, BitmapPixelFormat.Bgra8,
                region.Width, region.Height, BitmapAlphaMode.Ignore);
            preprocessing.Stop();
            var recognition = Stopwatch.StartNew();
            var binaryResult = await RecognizeSelectedAsync(bitmap, selected);
            recognition.Stop();
            return binaryResult with { FrameHash = captured.FrameHash, EnhancementUsed = true,
                QualityScore = ScoreRecognition(binaryResult), CaptureDurationMs = captureMs,
                RecognitionDurationMs = recognition.Elapsed.TotalMilliseconds,
                EnhancementDurationMs = preprocessing.Elapsed.TotalMilliseconds, TotalDurationMs = totalWatch.Elapsed.TotalMilliseconds };
        }
        var canEnhance = autoEnhance && CanEnhance(region.Width, region.Height);
        if (canEnhance && enhancementMode == OcrEnhancementModes.Enhanced)
        {
            var preferredScale = EnhancementScale(region.Width, region.Height);
            var enhanceWatch = Stopwatch.StartNew();
            using var preferredEnhancedBitmap = CreateEnhancedBitmap(captured.Pixels, preferredScale);
            enhanceWatch.Stop();
            var recognitionWatch = Stopwatch.StartNew();
            var enhancedOnly = await RecognizeSelectedAsync(preferredEnhancedBitmap, selected,
                positionScale: 1d / preferredScale);
            recognitionWatch.Stop();
            var enhancedOnlyScore = ScoreRecognition(enhancedOnly);
            if (enhancedOnlyScore < 56 || HasRecognitionArtifacts(enhancedOnly.Text))
            {
                // Learned enhancement can become wrong after a map/background change.
                // Recheck the raw image instead of locking into a blank/noisy profile.
                recognitionWatch.Start();
                var rescue = await RecognizeSelectedAsync(softwareBitmap, selected);
                recognitionWatch.Stop();
                var rescueScore = ScoreRecognition(rescue);
                totalWatch.Stop();
                var useRescue = rescueScore > enhancedOnlyScore && !string.IsNullOrWhiteSpace(rescue.Text);
                return (useRescue ? rescue : enhancedOnly) with
                {
                    FrameHash = captured.FrameHash, EnhancementUsed = !useRescue,
                    QualityScore = useRescue ? rescueScore : enhancedOnlyScore,
                    RawQualityScore = rescueScore, EnhancedQualityScore = enhancedOnlyScore,
                    ComparedVariants = true, CaptureDurationMs = captureMs,
                    RecognitionDurationMs = recognitionWatch.Elapsed.TotalMilliseconds,
                    EnhancementDurationMs = enhanceWatch.Elapsed.TotalMilliseconds,
                    TotalDurationMs = totalWatch.Elapsed.TotalMilliseconds
                };
            }
            totalWatch.Stop();
            return enhancedOnly with
            {
                FrameHash = captured.FrameHash,
                EnhancementUsed = true,
                QualityScore = enhancedOnlyScore,
                EnhancedQualityScore = enhancedOnlyScore,
                CaptureDurationMs = captureMs,
                RecognitionDurationMs = recognitionWatch.Elapsed.TotalMilliseconds,
                EnhancementDurationMs = enhanceWatch.Elapsed.TotalMilliseconds,
                TotalDurationMs = totalWatch.Elapsed.TotalMilliseconds
            };
        }

        var rawWatch = Stopwatch.StartNew();
        var raw = await RecognizeSelectedAsync(softwareBitmap, selected);
        rawWatch.Stop();
        var rawScore = ScoreRecognition(raw);
        var rawNeedsRescue = raw.Text.Length == 0 || HasRecognitionArtifacts(raw.Text);
        var shouldEnhance = canEnhance &&
            (enhancementMode == OcrEnhancementModes.Raw
                ? rawNeedsRescue
                : rawNeedsRescue || raw.AverageWordHeight < 17 || rawScore < 56);
        if (!shouldEnhance)
        {
            totalWatch.Stop();
            return raw with
            {
                FrameHash = captured.FrameHash,
                QualityScore = rawScore,
                RawQualityScore = rawScore,
                CaptureDurationMs = captureMs,
                RecognitionDurationMs = rawWatch.Elapsed.TotalMilliseconds,
                TotalDurationMs = totalWatch.Elapsed.TotalMilliseconds
            };
        }

        var enhancementScale = EnhancementScale(region.Width, region.Height);
        var preprocessingWatch = Stopwatch.StartNew();
        using var enhancedBitmap = CreateEnhancedBitmap(captured.Pixels, enhancementScale);
        preprocessingWatch.Stop();
        var enhancedWatch = Stopwatch.StartNew();
        var enhanced = await RecognizeSelectedAsync(enhancedBitmap, selected, positionScale: 1d / enhancementScale);
        enhancedWatch.Stop();
        var enhancedScore = ScoreRecognition(enhanced);
        var useEnhanced = !string.IsNullOrWhiteSpace(enhanced.Text) &&
            (enhancedScore >= rawScore + 1.5 || raw.Text.Length == 0);
        totalWatch.Stop();
        return (useEnhanced ? enhanced : raw) with
        {
            FrameHash = captured.FrameHash,
            EnhancementUsed = useEnhanced,
            QualityScore = useEnhanced ? enhancedScore : rawScore,
            RawQualityScore = rawScore,
            EnhancedQualityScore = enhancedScore,
            ComparedVariants = true,
            CaptureDurationMs = captureMs,
            RecognitionDurationMs = rawWatch.Elapsed.TotalMilliseconds + enhancedWatch.Elapsed.TotalMilliseconds,
            EnhancementDurationMs = preprocessingWatch.Elapsed.TotalMilliseconds,
            TotalDurationMs = totalWatch.Elapsed.TotalMilliseconds
        };
    }

    public async Task<DualOcrReadResult> ReadDualAsync(Rectangle fullRegion, Rectangle latestRegion,
        IReadOnlyCollection<string> languages, ulong? previousLatestHash, string? previousLatestText,
        bool forceFull, bool autoEnhance = true, string enhancementMode = OcrEnhancementModes.Auto)
    {
        if (fullRegion.Width < 20 || fullRegion.Height < 20 || !fullRegion.Contains(latestRegion) ||
            latestRegion.Width < 20 || latestRegion.Height < 20)
            throw new ArgumentException("최신 채팅 영역은 전체 OCR 영역 안에 있어야 합니다.");
        var watch = Stopwatch.StartNew();
        var captured = await CaptureAsync(fullRegion);
        return await ReadDualFrameAsync(captured,
            new Rectangle(latestRegion.X - fullRegion.X, latestRegion.Y - fullRegion.Y, latestRegion.Width, latestRegion.Height),
            languages, previousLatestHash, previousLatestText, forceFull, autoEnhance, enhancementMode, watch);
    }

    internal async Task<DualOcrReadResult> ReadDualFrameAsync(CapturedFrame captured, Rectangle latestRegion,
        IReadOnlyCollection<string> languages, ulong? previousLatestHash, string? previousLatestText,
        bool forceFull, bool autoEnhance, string enhancementMode, Stopwatch watch)
    {
        using (captured.Bitmap)
        {
            var captureMs = watch.Elapsed.TotalMilliseconds;
            var x = latestRegion.X;
            var y = latestRegion.Y;
            var latestPixels = CropPixels(captured.Pixels, new Rectangle(x, y, latestRegion.Width, latestRegion.Height));
            if (!forceFull && previousLatestHash == latestPixels.FrameHash)
                return new DualOcrReadResult(new OcrReadResult("", "EN", captured.FrameHash, false),
                    latestPixels.FrameHash, previousLatestText ?? "", false, -1, "최신 화면 변화 없음 · OCR 생략");
            using var latestBitmap = SoftwareBitmap.CreateCopyFromBuffer(CryptographicBuffer.CreateFromByteArray(latestPixels.Bytes),
                BitmapPixelFormat.Bgra8, latestPixels.Width, latestPixels.Height, BitmapAlphaMode.Ignore);
            var latest = await ReadFrameAsync(new CapturedFrame(latestBitmap, latestPixels, latestPixels.FrameHash),
                languages, null, null, autoEnhance, enhancementMode, 0, Stopwatch.StartNew());
            var fingerprint = DualOcrRegions.Fingerprint(latest.Text);
            var latestCount = latest.PositionedLines?.Count ?? 0;
            if (!forceFull && fingerprint == previousLatestText)
                return new DualOcrReadResult(latest with { FrameChanged = false }, latestPixels.FrameHash,
                    fingerprint, false, latestCount, "최신 본문 변화 없음 · 전체 OCR 생략");

            var full = await ReadFrameAsync(captured, languages, null, null, autoEnhance, enhancementMode, 0, Stopwatch.StartNew());
            var combined = DualOcrRegions.Merge(full, latest, x, y) with
            {
                CaptureDurationMs = captureMs,
                RecognitionDurationMs = full.RecognitionDurationMs + latest.RecognitionDurationMs,
                EnhancementDurationMs = full.EnhancementDurationMs + latest.EnhancementDurationMs,
                TotalDurationMs = watch.Elapsed.TotalMilliseconds
            };
            return new DualOcrReadResult(combined, latestPixels.FrameHash, fingerprint, true, latestCount,
                forceFull ? "전체 확인 · 기준 화면/주기 점검" : "최신 변화 → 전체 채팅 보완");
        }
    }

    internal static CapturedPixels CropPixels(CapturedPixels full, Rectangle region)
    {
        if (!new Rectangle(0, 0, full.Width, full.Height).Contains(region) || region.Width <= 0 || region.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(region));
        var bytes = new byte[region.Width * region.Height * 4];
        for (var row = 0; row < region.Height; row++)
            Buffer.BlockCopy(full.Bytes, ((region.Y + row) * full.Width + region.X) * 4,
                bytes, row * region.Width * 4, region.Width * 4);
        var hash = ComputeTextHash(region.Width, region.Height, (x, y) =>
        {
            var offset = (y * region.Width + x) * 4;
            return bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16 | bytes[offset + 3] << 24;
        });
        return new CapturedPixels(bytes, region.Width, region.Height, hash);
    }

    private async Task<OcrReadResult> RecognizeSelectedAsync(SoftwareBitmap bitmap, IReadOnlyCollection<string> selected,
        double positionScale = 1)
    {
        if (selected.Count == 1)
        {
            var languageCode = selected.First();
            var tag = languageCode switch { "JP" => "ja-JP", "KO" => "ko-KR", _ => "en-US" };
            var singleRecognition = ScaleRecognition(await RecognizeDetailedAsync(CreateEngine(tag), bitmap), positionScale);
            return new OcrReadResult(singleRecognition.Text, languageCode, PositionedLines: singleRecognition.Lines,
                AverageWordHeight: AverageWordHeight(singleRecognition));
        }

        var english = selected.Contains("EN")
            ? ScaleRecognition(await RecognizeDetailedAsync(CreateEngine("en-US"), bitmap), positionScale)
            : OcrRecognition.Empty;
        var japanese = selected.Contains("JP")
            ? ScaleRecognition(await RecognizeDetailedAsync(CreateEngine("ja-JP"), bitmap), positionScale)
            : OcrRecognition.Empty;
        var korean = selected.Contains("KO")
            ? ScaleRecognition(await RecognizeDetailedAsync(CreateEngine("ko-KR"), bitmap), positionScale)
            : OcrRecognition.Empty;
        return OcrLineSelector.Select(new Dictionary<string, IReadOnlyList<OcrPositionedLine>>
        {
            ["EN"] = english.Lines, ["JP"] = japanese.Lines, ["KO"] = korean.Lines
        });
    }

    private static OcrRecognition ScaleRecognition(OcrRecognition recognition, double scale)
    {
        if (Math.Abs(scale - 1) < 0.001 || recognition.Lines.Count == 0) return recognition;
        var lines = recognition.Lines.Select(line => new OcrPositionedLine(line.Text,
            Scale(line.X, scale), Scale(line.Y, scale), Scale(line.Width, scale), Scale(line.Height, scale),
            line.Words.Select(word => new OcrPositionedWord(word.Text,
                Scale(word.X, scale), Scale(word.Y, scale), Scale(word.Width, scale), Scale(word.Height, scale))).ToArray()))
            .ToArray();
        return new OcrRecognition(recognition.Text, lines);
    }

    private static int Scale(int value, double scale) => Math.Max(0, (int)Math.Round(value * scale));

    private static double AverageWordHeight(OcrRecognition recognition)
    {
        var heights = recognition.Lines.SelectMany(line => line.Words).Select(word => word.Height).Where(height => height > 0).ToArray();
        return heights.Length == 0 ? 0 : heights.Average();
    }

    private static double ScoreRecognition(OcrReadResult result)
    {
        var text = result.Text.Trim();
        if (text.Length == 0) return 0;
        var meaningful = text.Count(char.IsLetterOrDigit);
        var visible = text.Count(ch => !char.IsWhiteSpace(ch));
        var score = Math.Min(38, meaningful * 0.72) + (visible == 0 ? 0 : meaningful * 34d / visible);
        score += Math.Min(12, (result.PositionedLines?.Count ?? 0) * 2.5);
        if (HasRecognitionArtifacts(text)) score -= 24;
        if (result.AverageWordHeight is >= 11 and <= 42) score += 8;
        return Math.Clamp(score, 0, 100);
    }

    private static bool HasRecognitionArtifacts(string text) =>
        text.Contains('�') || text.Contains("??", StringComparison.Ordinal) ||
        text.Contains("``", StringComparison.Ordinal) ||
        text.Contains("<unk>", StringComparison.OrdinalIgnoreCase) ||
        text.Count(ch => ch is '|' or '¦' or '□') >= 3;

    private static bool CanEnhance(int width, int height) => width * 2 <= 3600 && height * 2 <= 2200 &&
        (long)width * height * 4 <= 10_000_000;

    /// <summary>
    /// Upscale factor for the enhanced pass. Windows OCR's Japanese engine needs more pixels per
    /// glyph than its English or Korean ones: at VALORANT's real chat size 2x dropped katakana
    /// ("ラッシュ") that 3x resolves, while Latin and Hangul already read fine at 2x. Only small
    /// crops take the 3x cost — a 4K capture already has plenty of pixels per glyph, and tripling
    /// it would add tens of milliseconds per frame for nothing.
    /// </summary>
    private static int EnhancementScale(int width, int height) =>
        width * 3 <= 3600 && height * 3 <= 2200 && (long)width * height * 9 <= 2_000_000 ? 3 : 2;

    private static SoftwareBitmap CreateEnhancedBitmap(CapturedPixels pixels, int scale)
    {
        var width = pixels.Width * scale;
        var height = pixels.Height * scale;
        var enhanced = new byte[width * height * 4];

        // Bilinear rather than pixel doubling. Chat glyphs are barely a dozen pixels tall, and
        // duplicating pixels only makes the staircase edges larger, which Windows OCR reads as
        // stroke noise. Interpolation restores the edge gradient the recognizer needs, which is
        // what lets small CJK text resolve at all.
        var (xLow, xHigh, xWeight) = BuildAxisMap(pixels.Width, width, scale);
        var (yLow, yHigh, yWeight) = BuildAxisMap(pixels.Height, height, scale);

        for (var y = 0; y < height; y++)
        {
            var rowLow = yLow[y] * pixels.Width;
            var rowHigh = yHigh[y] * pixels.Width;
            var wy = yWeight[y];
            for (var x = 0; x < width; x++)
            {
                var wx = xWeight[x];
                var target = (y * width + x) * 4;
                var topLeft = (rowLow + xLow[x]) * 4;
                var topRight = (rowLow + xHigh[x]) * 4;
                var bottomLeft = (rowHigh + xLow[x]) * 4;
                var bottomRight = (rowHigh + xHigh[x]) * 4;
                var blue = Sample(pixels.Bytes, topLeft, topRight, bottomLeft, bottomRight, 0, wx, wy);
                var green = Sample(pixels.Bytes, topLeft, topRight, bottomLeft, bottomRight, 1, wx, wy);
                var red = Sample(pixels.Bytes, topLeft, topRight, bottomLeft, bottomRight, 2, wx, wy);
                var luminance = (red * 3 + green * 6 + blue) / 10;
                var contrast = luminance < 72 ? 0.62 : 1.42;
                enhanced[target] = EnhanceChannel(blue, contrast);
                enhanced[target + 1] = EnhanceChannel(green, contrast);
                enhanced[target + 2] = EnhanceChannel(red, contrast);
                enhanced[target + 3] = 255;
            }
        }
        var buffer = CryptographicBuffer.CreateFromByteArray(enhanced);
        return SoftwareBitmap.CreateCopyFromBuffer(buffer, BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Ignore);
    }

    private static byte EnhanceChannel(int value, double contrast) =>
        (byte)Math.Clamp((int)Math.Round((value - 104) * contrast + 104), 0, 255);

    private static int Sample(byte[] bytes, int topLeft, int topRight, int bottomLeft, int bottomRight,
        int channel, double weightX, double weightY)
    {
        var top = bytes[topLeft + channel] * (1 - weightX) + bytes[topRight + channel] * weightX;
        var bottom = bytes[bottomLeft + channel] * (1 - weightX) + bytes[bottomRight + channel] * weightX;
        return (int)Math.Round(top * (1 - weightY) + bottom * weightY);
    }

    /// <summary>
    /// Precomputes the two source indices and the blend weight for every output row or column,
    /// so the per-pixel loop stays free of division. Uses pixel-centre alignment
    /// ((i + 0.5) / scale - 0.5) to avoid the half-pixel shift that corner alignment introduces.
    /// </summary>
    private static (int[] Low, int[] High, double[] Weight) BuildAxisMap(int sourceLength, int targetLength, int scale)
    {
        var low = new int[targetLength];
        var high = new int[targetLength];
        var weight = new double[targetLength];
        for (var index = 0; index < targetLength; index++)
        {
            var position = (index + 0.5) / scale - 0.5;
            var floor = (int)Math.Floor(position);
            var fraction = position - floor;
            if (floor < 0) { floor = 0; fraction = 0; }
            if (floor > sourceLength - 2) { floor = Math.Max(0, sourceLength - 2); fraction = sourceLength > 1 ? 1 : 0; }
            low[index] = floor;
            high[index] = Math.Min(floor + 1, sourceLength - 1);
            weight[index] = fraction;
        }
        return (low, high, weight);
    }

    internal static OcrReadResult SelectAutoResult(string englishText, string japaneseText)
        => SelectAutoResult(englishText, japaneseText, "");

    internal static OcrReadResult SelectAutoResult(string englishText, string japaneseText, string koreanText)
        => SelectAllowedResult(englishText, japaneseText, koreanText, new[] { "EN", "JP", "KO" });

    private static OcrReadResult SelectAllowedResult(string englishText, string japaneseText, string koreanText,
        IReadOnlyCollection<string> selected)
    {
        var japaneseContent = ChatTextSanitizer.ContentForLanguageDetection(japaneseText);
        var koreanContent = ChatTextSanitizer.ContentForLanguageDetection(koreanText);
        var kanaCharacters = japaneseContent.Count(IsKana);
        var cjkCharacters = japaneseContent.Count(IsCjk);
        if (selected.Contains("JP") && (kanaCharacters > 0 || cjkCharacters >= 2))
            return new OcrReadResult(japaneseText, "JP");

        if (selected.Contains("KO") && koreanContent.Any(ch => ch is >= '\uAC00' and <= '\uD7AF'))
            return new OcrReadResult(koreanText, "KO");

        if (selected.Contains("EN") && !string.IsNullOrWhiteSpace(englishText)) return new OcrReadResult(englishText, "EN");
        if (selected.Contains("JP") && !string.IsNullOrWhiteSpace(japaneseText)) return new OcrReadResult(japaneseText, "JP");
        if (selected.Contains("KO") && !string.IsNullOrWhiteSpace(koreanText)) return new OcrReadResult(koreanText, "KO");
        return new OcrReadResult("", selected.FirstOrDefault() ?? "EN");
    }

    private static bool IsKana(char ch) => ch is >= '\u3040' and <= '\u30FF';
    private static bool IsCjk(char ch) => ch is >= '\u3400' and <= '\u9FFF';

    private OcrEngine CreateEngine(string languageTag)
    {
        lock (_engineLock)
        {
            if (_engines.TryGetValue(languageTag, out var cached)) return cached;
            var engine = OcrEngine.TryCreateFromLanguage(new Language(languageTag))
                ?? throw new InvalidOperationException($"Windows의 {languageTag} OCR 언어 팩이 설치되어 있지 않습니다.");
            _engines[languageTag] = engine;
            return engine;
        }
    }

    private static async Task<OcrRecognition> RecognizeDetailedAsync(OcrEngine engine, SoftwareBitmap softwareBitmap)
    {
        var result = await engine.RecognizeAsync(softwareBitmap);
        var lines = result.Lines.Select(line =>
        {
            var words = line.Words.Select(word => new OcrPositionedWord(word.Text,
                (int)Math.Round(word.BoundingRect.X), (int)Math.Round(word.BoundingRect.Y),
                (int)Math.Round(word.BoundingRect.Width), (int)Math.Round(word.BoundingRect.Height))).ToArray();
            var text = string.Join(" ", words.Select(word => word.Text)).Trim();
            if (words.Length == 0) return new OcrPositionedLine(text, 0, 0, 0, 0, words);
            var left = words.Min(word => word.X);
            var top = words.Min(word => word.Y);
            var right = words.Max(word => word.X + word.Width);
            var bottom = words.Max(word => word.Y + word.Height);
            return new OcrPositionedLine(text, left, top, right - left, bottom - top, words);
        }).Where(line => line.Text.Length > 0).ToArray();
        return new OcrRecognition(string.Join(Environment.NewLine, lines.Select(line => line.Text)), lines);
    }

    private static async Task<CapturedFrame> CaptureAsync(Rectangle region)
    {
        var pixels = await Task.Run(() => CapturePixels(region)).ConfigureAwait(false);
        var buffer = CryptographicBuffer.CreateFromByteArray(pixels.Bytes);
        var softwareBitmap = SoftwareBitmap.CreateCopyFromBuffer(buffer, BitmapPixelFormat.Bgra8,
            region.Width, region.Height, BitmapAlphaMode.Ignore);
        return new CapturedFrame(softwareBitmap, pixels, pixels.FrameHash);
    }

    private static ulong ComputeFrameHash(Bitmap bitmap)
    {
        var rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            return ComputeTextHash(bitmap.Width, bitmap.Height,
                (x, y) => Marshal.ReadInt32(data.Scan0, y * data.Stride + x * 4));
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static Bitmap CaptureBitmap(Rectangle region)
    {
        var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(region.X, region.Y, 0, 0, region.Size, CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    private static CapturedPixels CapturePixels(Rectangle region)
    {
        using var bitmap = CaptureBitmap(region);
        var rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var stride = Math.Abs(data.Stride);
            var bytes = new byte[stride * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            var hash = ComputeTextHash(bitmap.Width, bitmap.Height, (x, y) =>
            {
                var index = y * stride + x * 4;
                return bytes[index] | bytes[index + 1] << 8 | bytes[index + 2] << 16 | bytes[index + 3] << 24;
            });
            return new CapturedPixels(bytes, bitmap.Width, bitmap.Height, hash);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static ulong ComputeTextHash(int width, int height, Func<int, int, int> pixelAt)
    {
        const int columns = 32;
        const int rows = 18;
        const int samplesPerAxis = 4;
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offset;

        for (var row = 0; row < rows; row++)
        for (var column = 0; column < columns; column++)
        {
            var brightMask = 0;
            for (var sy = 0; sy < samplesPerAxis; sy++)
            for (var sx = 0; sx < samplesPerAxis; sx++)
            {
                var x = Math.Min(width - 1, (column * samplesPerAxis + sx) * width / (columns * samplesPerAxis));
                var y = Math.Min(height - 1, (row * samplesPerAxis + sy) * height / (rows * samplesPerAxis));
                var pixel = pixelAt(x, y);
                var blue = pixel & 0xff;
                var green = (pixel >> 8) & 0xff;
                var red = (pixel >> 16) & 0xff;
                var maximum = Math.Max(red, Math.Max(green, blue));
                var luminance = (red * 3 + green * 6 + blue) / 10;
                if (maximum >= 160 && luminance >= 105)
                    brightMask |= 1 << (sy * samplesPerAxis + sx);
            }

            // Preserve sample positions, not only the number of bright pixels.
            // Two different glyphs can have equal brightness counts in a cell.
            hash ^= (ushort)brightMask;
            hash *= prime;
        }

        return hash;
    }
}

public sealed record CapturedFramePng(ulong FrameHash, byte[] Png, double CaptureDurationMs);
internal sealed record CapturedFrame(SoftwareBitmap Bitmap, CapturedPixels Pixels, ulong FrameHash);
internal sealed record CapturedPixels(byte[] Bytes, int Width, int Height, ulong FrameHash);
internal sealed record OcrRecognition(string Text, IReadOnlyList<OcrPositionedLine> Lines)
{
    public static OcrRecognition Empty { get; } = new("", Array.Empty<OcrPositionedLine>());
}
public sealed record OcrPositionedWord(string Text, int X, int Y, int Width, int Height);
public sealed record DualOcrReadResult(OcrReadResult Result, ulong LatestHash, string LatestText,
    bool FullChecked, int LatestLineCount, string Route);
public sealed record OcrPositionedLine(string Text, int X, int Y, int Width, int Height,
    IReadOnlyList<OcrPositionedWord> Words);
public sealed record OcrReadResult(string Text, string DetectedLanguage, ulong FrameHash = 0,
    bool FrameChanged = true, IReadOnlyList<OcrPositionedLine>? PositionedLines = null,
    bool EnhancementUsed = false, double QualityScore = 0, double AverageWordHeight = 0,
    double RawQualityScore = 0, double EnhancedQualityScore = 0, bool ComparedVariants = false,
    double CaptureDurationMs = 0, double RecognitionDurationMs = 0, double EnhancementDurationMs = 0,
    double TotalDurationMs = 0, int ImageWidth = 0, bool QualityScoreAvailable = true);

public static class OcrEnhancementModes
{
    public const string Auto = "Auto";
    public const string Raw = "Raw";
    public const string Enhanced = "Enhanced";
    public const string Binary = "Binary";

    public static string Normalize(string value) => value is Raw or Enhanced or Binary ? value : Auto;
}
