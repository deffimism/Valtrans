using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Valtrans.Services;

public sealed partial class ValtransLiteService : IDisposable
{
    private const string HostResourceName = "Valtrans.Assets.ValtransLiteHost.exe";
    private const string HostFileName = "ValtransLiteHost-5.exe";
    private static readonly LiteModelPackage[] Packages =
    [
        new("en", "ko", "1.1", "https://argos-net.com/v1/translate-en_ko-1_1.argosmodel", 120_789_009,
            "e03d8e65e6d44525ec5808c3409fcf8728c76c2c76925372b6d3dc3278de17fc", 132_573_333,
            "3ad29824a57f3842b165c87513bc8723deb1486f91dbb154137be6dc2fa0a777", 775_634,
            "1e732c7195cf4d192bec9abf8f7f91d086edbd8f48aca8be3b1cdcffa530ad8b"),
        CreateKoreanEnglishPackage(),
        new("en", "ja", "1.1", "https://argos-net.com/v1/translate-en_ja-1_1.argosmodel", 120_470_284,
            "16300cc4eaa85320520cabcf433b63d01be40ef6966251de72043a083408f716", 132_573_333,
            "52a021f9d552beee5c15ec8f05e2bf72c90402ef428d58f7d625643ba2dc1780", 787_500,
            "33de6356768ef5eaca1a41883b1990273093ffbe70720083502cb1a975b59b3d"),
        new("ja", "en", "1.1", "https://argos-net.com/v1/translate-ja_en-1_1.argosmodel", 117_155_716,
            "623e3477959a815eb0a5ef53e09079ae8f1f9d3bbcd230473baf28c03fb83335", 132_573_333,
            "5d2ac0b0e427aa368b635812853a70b09976d2318bf889a9327488968b501c83", 787_932,
            "af3f880c259f55c62ed6c77c1ebc3cf46d6a95cc7cd8e9fcc78ee75417b987ef")
    ];

    private readonly HttpClient _downloadClient = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private readonly object _processLock = new();
    private Process? _process;
    private string _lastHostError = "";
    private int _loadedModelCount;
    private int _maxLoadedModels = 4;
    private string _modelUpdateNotice = "";
    private bool _disposed;

    public string RootDirectory { get; }
    public string ModelsDirectory => Path.Combine(RootDirectory, "models");
    private string HostPath => Path.Combine(RootDirectory, HostFileName);

    public ValtransLiteService(string? rootDirectory = null)
    {
        RootDirectory = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Valtrans", "Lite");
    }

    public LiteStatus GetStatus()
    {
        var installed = Packages.Count(package => IsPairInstalled(package.Source, package.Target));
        var running = IsHostRunning();
        var hostReady = File.Exists(HostPath) || HasEmbeddedHost();
        var ready = hostReady && installed == Packages.Length;
        var message = !hostReady
            ? "Valtrans Lite 실행 파일이 앱에 포함되지 않았습니다"
            : installed < Packages.Length
                ? $"언어 모델 {installed}/{Packages.Length} 설치됨 · 약 {RemainingDownloadMb():0}MB 남음"
                : running
                    ? _loadedModelCount > 0
                        ? $"예열 완료 · 모델 {_loadedModelCount}/{_maxLoadedModels} 적재 · CPU INT8"
                        : "실행 중 · 모델 메모리 해제됨 · 필요 시 자동 예열"
                    : "설치 완료 · 첫 번역 시 자동 시작";
        if (!string.IsNullOrWhiteSpace(_lastHostError) && !running)
            message = $"로컬 번역 호스트 확인 필요 · {_lastHostError}";
        if (!string.IsNullOrWhiteSpace(_modelUpdateNotice)) message += $" · {_modelUpdateNotice}";
        if (HasLegacyKoreanEnglish()) message += " · 한국어→영어 개선 모델 업데이트 가능";
        return new LiteStatus(hostReady, installed, Packages.Length, ready, running, _loadedModelCount,
            _maxLoadedModels, message);
    }

    public async Task<LiteResult> InstallAndPrepareAsync(IProgress<LiteProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(ModelsDirectory);
        progress?.Report(new LiteProgress(1, "Valtrans Lite 실행 파일을 준비하는 중…"));
        EnsureHostBinary();
        if (IsHostRunning())
        {
            progress?.Report(new LiteProgress(2, "검사를 위해 적재된 모델 메모리를 정리하는 중…"));
            await ReleaseModelsAsync(cancellationToken);
        }

        for (var index = 0; index < Packages.Length; index++)
        {
            var package = Packages[index];
            if (IsPairInstalled(package.Source, package.Target))
            {
                progress?.Report(new LiteProgress(index * 22 + 4,
                    $"{LanguageName(package.Source)}→{LanguageName(package.Target)} 무결성 검사 중…"));
                if (await VerifyInstalledPackageAsync(package, cancellationToken))
                {
                    await WriteModelManifestAsync(package, cancellationToken);
                    progress?.Report(new LiteProgress((index + 1) * 22,
                        $"{LanguageName(package.Source)}→{LanguageName(package.Target)} 검사 완료"));
                    continue;
                }
                progress?.Report(new LiteProgress(index * 22 + 5,
                    $"{LanguageName(package.Source)}→{LanguageName(package.Target)} " +
                    (package.Source == "ko" && HasLegacyKoreanEnglish() ? "이전 모델 → 품질 개선 모델 준비 중…" : "손상 감지 · 개별 복구 중…")));
            }

            await DownloadAndInstallPackageAsync(package, index, progress, cancellationToken);
        }

        progress?.Report(new LiteProgress(91, "로컬 번역 프로세스를 시작하는 중…"));
        await EnsureRunningAsync(cancellationToken);
        progress?.Report(new LiteProgress(94, "영어→한국어 모델을 예열하는 중…"));
        await WarmUpAsync("EN", "KO", cancellationToken);
        progress?.Report(new LiteProgress(100, "준비 완료 · CPU INT8 · 인터넷 없이 번역 가능"));
        return new LiteResult(true, "Valtrans Lite 준비 완료 · CPU INT8 오프라인 번역");
    }

    public async Task WarmUpAsync(string sourceLanguage, string targetLanguage,
        CancellationToken cancellationToken = default)
    {
        var source = NormalizeLanguage(sourceLanguage);
        var target = NormalizeLanguage(targetLanguage);
        using var response = await SendAsync(new { command = "warmup", source, target }, cancellationToken);
    }

    public async Task KeepAliveAsync(CancellationToken cancellationToken = default)
    {
        if (!IsHostRunning()) return;
        using var _ = await SendAsync(new { command = "keepalive" }, cancellationToken);
    }

    public async Task<LiteStatus> RefreshRuntimeStatusAsync(CancellationToken cancellationToken = default)
    {
        if (IsHostRunning()) using (await SendAsync(new { command = "status" }, cancellationToken)) { }
        return GetStatus();
    }

    public async Task<LiteStatus> ReleaseModelsAsync(CancellationToken cancellationToken = default)
    {
        if (IsHostRunning()) using (await SendAsync(new { command = "release" }, cancellationToken)) { }
        return GetStatus();
    }

    public async Task<string> CheckForModelUpdatesAsync(CancellationToken cancellationToken = default)
    {
        const string indexUrl = "https://raw.githubusercontent.com/argosopentech/argospm-index/main/index.json";
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            var json = await _downloadClient.GetStringAsync(indexUrl, timeout.Token);
            using var document = JsonDocument.Parse(json);
            var updates = new List<string>();
            foreach (var package in Packages)
            {
                if (!package.IsArgosIndex) continue;
                var latest = document.RootElement.EnumerateArray()
                    .Where(item => item.TryGetProperty("from_code", out var source) && source.GetString() == package.Source &&
                                   item.TryGetProperty("to_code", out var target) && target.GetString() == package.Target)
                    .Select(item => item.TryGetProperty("package_version", out var version) ? version.GetString() : null)
                    .Where(version => !string.IsNullOrWhiteSpace(version))
                    .OrderByDescending(ParseVersion)
                    .FirstOrDefault();
                if (latest is not null && ParseVersion(latest) > ParseVersion(package.Version))
                    updates.Add($"{package.Source.ToUpperInvariant()}→{package.Target.ToUpperInvariant()} v{latest}");
            }
            _modelUpdateNotice = updates.Count == 0 ? "공식 모델 최신" : $"업데이트 있음: {string.Join(", ", updates)}";
        }
        catch
        {
            _modelUpdateNotice = "업데이트 확인 불가";
        }
        return _modelUpdateNotice;
    }

    public async Task<IReadOnlyList<string>> TranslateAsync(IReadOnlyList<string> texts,
        string sourceLanguage, string targetLanguage, CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0) return Array.Empty<string>();
        var source = NormalizeLanguage(sourceLanguage);
        var target = NormalizeLanguage(targetLanguage);
        if (source == target) return texts.ToArray();
        using var result = await SendAsync(new { command = "translate", source, target, texts }, cancellationToken);
        if (!result.RootElement.TryGetProperty("translations", out var translations) ||
            translations.ValueKind != JsonValueKind.Array || translations.GetArrayLength() != texts.Count ||
            translations.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
            throw new InvalidOperationException("Valtrans Lite 번역 응답의 개수나 형식이 올바르지 않습니다.");
        return translations.EnumerateArray().Select(item => item.GetString() ?? "").ToArray();
    }

    private async Task DownloadAndInstallPackageAsync(LiteModelPackage package, int packageIndex,
        IProgress<LiteProgress>? progress, CancellationToken cancellationToken)
    {
        var pair = $"{package.Source}_{package.Target}";
        var archivePath = Path.Combine(RootDirectory, $"{pair}.argosmodel.download");
        var stagingPath = Path.Combine(ModelsDirectory, $"{pair}.installing");
        var destinationPath = PairDirectory(package.Source, package.Target);

        if (Directory.Exists(stagingPath)) Directory.Delete(stagingPath, true);
        Directory.CreateDirectory(stagingPath);
        EnsureFreeSpace(package, archivePath);

        var basePercent = packageIndex * 22;
        var archiveValid = false;
        for (var attempt = 0; attempt < 2 && !archiveValid; attempt++)
        {
            await DownloadArchiveWithResumeAsync(package, archivePath, basePercent, progress, cancellationToken);
            progress?.Report(new LiteProgress(basePercent + 18.5,
                $"{LanguageName(package.Source)}→{LanguageName(package.Target)} 패키지 SHA-256 검사 중…"));
            archiveValid = await FileMatchesAsync(archivePath, package.ExpectedBytes, package.PackageSha256,
                cancellationToken);
            if (!archiveValid && File.Exists(archivePath)) File.Delete(archivePath);
        }
        if (!archiveValid)
            throw new InvalidDataException($"{pair} 공식 모델 패키지 무결성 검사에 실패했습니다. 다시 시도해 주세요.");

        progress?.Report(new LiteProgress(basePercent + 19,
            $"{LanguageName(package.Source)}→{LanguageName(package.Target)} 모델 설치 중…"));
        using (var archive = ZipFile.OpenRead(archivePath))
        {
            var prefix = pair + "/";
            foreach (var entry in archive.Entries)
            {
                if (!entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                var relative = entry.FullName[prefix.Length..].Replace('/', Path.DirectorySeparatorChar);
                if (string.IsNullOrWhiteSpace(relative)) continue;
                var targetPath = Path.GetFullPath(Path.Combine(stagingPath, relative));
                var safeRoot = Path.GetFullPath(stagingPath) + Path.DirectorySeparatorChar;
                if (!targetPath.StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("안전하지 않은 모델 패키지 경로가 감지됐습니다.");
                if (entry.FullName.EndsWith('/'))
                {
                    Directory.CreateDirectory(targetPath);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                entry.ExtractToFile(targetPath, true);
            }
        }

        if (!HasPackageFileSizes(stagingPath, package))
            throw new InvalidDataException($"{pair} 모델 패키지가 완전하지 않습니다.");
        if (!await VerifyExtractedPackageAsync(stagingPath, package, cancellationToken))
            throw new InvalidDataException($"{pair} 모델 파일 무결성 검사에 실패했습니다.");
        // Finish all cancellable verification/writes before touching an existing
        // installation. A failed download must not erase the user's current model.
        await WriteModelManifestAsync(package, cancellationToken, stagingPath);
        cancellationToken.ThrowIfCancellationRequested();
        ActivateVerifiedPackage(stagingPath, destinationPath);
        File.Delete(archivePath);
        progress?.Report(new LiteProgress(basePercent + 22,
            $"{LanguageName(package.Source)}→{LanguageName(package.Target)} 설치 완료"));
    }

    private async Task DownloadArchiveWithResumeAsync(LiteModelPackage package, string archivePath,
        double basePercent, IProgress<LiteProgress>? progress, CancellationToken cancellationToken)
    {
        var existing = File.Exists(archivePath) ? new FileInfo(archivePath).Length : 0;
        if (existing > package.ExpectedBytes)
        {
            File.Delete(archivePath);
            existing = 0;
        }
        if (existing == package.ExpectedBytes) return;

        using var request = new HttpRequestMessage(HttpMethod.Get, package.Url);
        if (existing > 0) request.Headers.Range = new RangeHeaderValue(existing, null);
        using var response = await _downloadClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var resumed = existing > 0 && response.StatusCode == System.Net.HttpStatusCode.PartialContent;
        if (!resumed) existing = 0;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new FileStream(archivePath, resumed ? FileMode.Append : FileMode.Create,
            FileAccess.Write, FileShare.None, 1024 * 128, true);
        var buffer = new byte[1024 * 128];
        var received = existing;
        while (true)
        {
            var count = await source.ReadAsync(buffer, cancellationToken);
            if (count == 0) break;
            await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            received += count;
            var withinPackage = Math.Clamp(received * 18d / package.ExpectedBytes, 0, 18);
            progress?.Report(new LiteProgress(basePercent + withinPackage,
                $"{LanguageName(package.Source)}→{LanguageName(package.Target)} " +
                $"{(resumed ? "이어받기" : "다운로드")} · {received / 1024d / 1024d:0}/{package.ExpectedBytes / 1024d / 1024d:0}MB"));
        }
    }

    private async Task<bool> VerifyInstalledPackageAsync(LiteModelPackage package, CancellationToken cancellationToken) =>
        await VerifyExtractedPackageAsync(PairDirectory(package.Source, package.Target), package, cancellationToken);

    internal static async Task<bool> VerifyExtractedPackageAsync(string directory, LiteModelPackage package,
        CancellationToken cancellationToken)
    {
        foreach (var file in RequiredPackageFiles(package))
            if (!await FileMatchesAsync(Path.Combine(directory, file.Path), file.Bytes, file.Sha256, cancellationToken)) return false;
        return true;
    }

    private static async Task<bool> FileMatchesAsync(string path, long expectedBytes, string expectedSha256,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != expectedBytes) return false;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 256, true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    private async Task WriteModelManifestAsync(LiteModelPackage package, CancellationToken cancellationToken,
        string? directory = null)
    {
        var manifest = new LiteModelManifest(package.Version, package.PackageSha256, package.ModelBytes,
            package.ModelSha256, package.TokenizerBytes, package.TokenizerSha256, DateTimeOffset.UtcNow,
            package.TokenizerFileName, package.ExtraFiles);
        var path = Path.Combine(directory ?? PairDirectory(package.Source, package.Target), "valtrans-manifest.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(manifest), cancellationToken);
    }

    private void EnsureFreeSpace(LiteModelPackage package, string archivePath)
    {
        var existing = File.Exists(archivePath) ? new FileInfo(archivePath).Length : 0;
        var required = Math.Max(0, package.ExpectedBytes - existing) + package.ModelBytes + 100L * 1024 * 1024;
        var root = Path.GetPathRoot(Path.GetFullPath(RootDirectory));
        if (string.IsNullOrWhiteSpace(root)) return;
        var drive = new DriveInfo(root);
        if (drive.AvailableFreeSpace < required)
            throw new IOException($"저장 공간이 부족합니다. {LanguageName(package.Source)}→{LanguageName(package.Target)} 설치에 " +
                                  $"약 {required / 1024d / 1024d:0}MB의 여유 공간이 필요합니다.");
    }

    private async Task<JsonDocument> SendAsync(object request, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _requestLock.WaitAsync(cancellationToken);
        var releaseHere = true;
        try
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            var exchange = ExchangeAsync(request);
            try { return await exchange.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Keep the single-reader lock until the outstanding response has been drained.
                // Cancelling StreamReader.ReadLineAsync mid-line can also corrupt framing.
                releaseHere = false;
                _ = DrainCancelledExchangeAsync(exchange);
                throw;
            }
        }
        finally { if (releaseHere) _requestLock.Release(); }
    }

    private async Task DrainCancelledExchangeAsync(Task<JsonDocument> exchange)
    {
        try { using var discarded = await exchange; }
        catch { /* ExchangeAsync owns process recovery; the cancelled caller has already left. */ }
        finally { _requestLock.Release(); }
    }

    private async Task<JsonDocument> ExchangeAsync(object request)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            await EnsureRunningAsync(timeout.Token);
            var process = _process ?? throw new InvalidOperationException("Valtrans Lite 프로세스를 시작하지 못했습니다.");
            var id = Guid.NewGuid().ToString("N");
            var json = JsonSerializer.Serialize(request);
            using var payload = JsonDocument.Parse(json);
            var body = new Dictionary<string, object?> { ["id"] = id };
            foreach (var property in payload.RootElement.EnumerateObject()) body[property.Name] = property.Value.Clone();

            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(body).AsMemory(), timeout.Token);
            await process.StandardInput.FlushAsync(timeout.Token);
            using var response = await LiteResponseReader.ReadAsync(process.StandardOutput, id, timeout.Token);
            var root = response.RootElement;
            if (!root.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            {
                var error = root.TryGetProperty("error", out var errorValue)
                    ? errorValue.GetString()
                    : "알 수 없는 로컬 번역 오류";
                throw new InvalidOperationException(error);
            }
            var result = root.GetProperty("result").Clone();
            UpdateRuntimeState(result);
            return JsonDocument.Parse(result.GetRawText());
        }
        catch (OperationCanceledException)
        {
            StopHostProcess();
            throw new TimeoutException("Valtrans Lite 모델 로딩 또는 번역 시간이 초과됐습니다.");
        }
        catch (ObjectDisposedException ex)
        {
            StopHostProcess();
            throw new InvalidOperationException("Valtrans Lite 프로세스가 예기치 않게 종료됐습니다. 다시 시도해 주세요.", ex);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            StopHostProcess();
            throw new InvalidOperationException("Valtrans Lite 응답 연결이 끊겼습니다. 다음 요청에서 다시 시작합니다.", ex);
        }
    }

    private Task EnsureRunningAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsHostRunning()) return Task.CompletedTask;
        EnsureHostBinary();
        lock (_processLock)
        {
            if (IsHostRunning()) return Task.CompletedTask;
            StopHostProcess();
            var startInfo = new ProcessStartInfo(HostPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = RootDirectory
            };
            startInfo.Environment["VALTRANS_LITE_MODELS"] = ModelsDirectory;
            startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
            startInfo.Environment["OMP_NUM_THREADS"] = Math.Max(1, Math.Min(4, Environment.ProcessorCount / 2)).ToString();
            _process = Process.Start(startInfo)
                       ?? throw new InvalidOperationException("Valtrans Lite 프로세스를 시작하지 못했습니다.");
            try { _process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            _lastHostError = "";
            _process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data)) _lastHostError = e.Data.Trim();
            };
            _process.BeginErrorReadLine();
        }
        return Task.CompletedTask;
    }

    private void EnsureHostBinary()
    {
        Directory.CreateDirectory(RootDirectory);
        if (!File.Exists(HostPath) || new FileInfo(HostPath).Length <= 1_000_000)
        {
            using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream(HostResourceName)
                               ?? throw new InvalidOperationException("Valtrans Lite 실행 파일 리소스를 찾지 못했습니다.");
            using var destination = new FileStream(HostPath, FileMode.Create, FileAccess.Write, FileShare.None);
            source.CopyTo(destination);
        }
        CleanupOldHostBinaries();
    }

    private void CleanupOldHostBinaries()
    {
        foreach (var oldHost in Directory.EnumerateFiles(RootDirectory, "ValtransLiteHost-*.exe"))
        {
            if (string.Equals(Path.GetFullPath(oldHost), Path.GetFullPath(HostPath),
                    StringComparison.OrdinalIgnoreCase)) continue;
            try { File.Delete(oldHost); }
            catch { /* 이전 호스트가 아직 종료 중이면 다음 실행에서 다시 정리합니다. */ }
        }
    }

    private static bool HasEmbeddedHost() =>
        Assembly.GetExecutingAssembly().GetManifestResourceNames().Contains(HostResourceName, StringComparer.Ordinal);

    private bool IsPairInstalled(string source, string target)
    {
        var package = Packages.FirstOrDefault(item => item.Source == source && item.Target == target);
        if (package is null) return false;
        var pairPath = PairDirectory(source, target);
        return HasPackageFileSizes(pairPath, package) || source == "ko" && target == "en" && HasLegacyKoreanEnglish();
    }

    private string PairDirectory(string source, string target) =>
        Path.Combine(ModelsDirectory, $"{source}_{target}");

    private void UpdateRuntimeState(JsonElement result)
    {
        if (result.TryGetProperty("loaded", out var loaded) && loaded.ValueKind == JsonValueKind.Array)
            Interlocked.Exchange(ref _loadedModelCount, loaded.GetArrayLength());
        if (result.TryGetProperty("maxLoadedModels", out var maximum) && maximum.TryGetInt32(out var max))
            Interlocked.Exchange(ref _maxLoadedModels, Math.Max(1, max));
    }

    private bool IsHostRunning()
    {
        try { return _process is { HasExited: false }; }
        catch { return false; }
    }

    private void StopHostProcess()
    {
        lock (_processLock)
        {
            if (_process is null) return;
            try
            {
                if (!_process.HasExited)
                {
                    _process.StandardInput.WriteLine("{\"id\":\"shutdown\",\"command\":\"shutdown\"}");
                    _process.StandardInput.Flush();
                    if (!_process.WaitForExit(1500)) _process.Kill(true);
                }
            }
            catch { }
            finally
            {
                _process.Dispose();
                _process = null;
                Interlocked.Exchange(ref _loadedModelCount, 0);
            }
        }
    }

    private static Version ParseVersion(string? value) =>
        Version.TryParse(value, out var version) ? version : new Version(0, 0);

    private static string NormalizeLanguage(string language) => language.ToUpperInvariant() switch
    {
        "JP" or "JA" => "ja",
        "KO" => "ko",
        _ => "en"
    };

    private static string LanguageName(string code) => code switch
    {
        "ko" => "한국어",
        "ja" => "일본어",
        _ => "영어"
    };

    private double RemainingDownloadMb() => Packages
        .Where(package => !IsPairInstalled(package.Source, package.Target))
        .Sum(package => package.ExpectedBytes) / 1024d / 1024d;

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopHostProcess();
        // Cancelled exchanges can still be draining. Let the semaphore be collected after they finish.
        _downloadClient.Dispose();
    }
}

public sealed record LiteStatus(bool HostReady, int InstalledModels, int TotalModels, bool Ready, bool Running,
    int LoadedModels, int MaxLoadedModels, string Message);
public sealed record LiteProgress(double Percent, string Message);
public sealed record LiteResult(bool Success, string Message);
internal sealed record LiteModelPackage(string Source, string Target, string Version, string Url, long ExpectedBytes,
    string PackageSha256, long ModelBytes, string ModelSha256, long TokenizerBytes, string TokenizerSha256,
    string TokenizerFileName = "sentencepiece.model", IReadOnlyList<LitePackageFile>? ExtraFiles = null, bool IsArgosIndex = true);
internal sealed record LitePackageFile(string Path, long Bytes, string Sha256);
internal sealed record LiteModelManifest(string Version, string PackageSha256, long ModelBytes, string ModelSha256,
    long TokenizerBytes, string TokenizerSha256, DateTimeOffset VerifiedAt,
    string TokenizerFileName = "sentencepiece.model", IReadOnlyList<LitePackageFile>? ExtraFiles = null);
