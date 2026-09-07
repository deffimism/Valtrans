using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Valtrans.Services;

public sealed class LocalAiService
{
    public const string BaseUrl = "http://localhost:11434";
    public const string OpenAiBaseUrl = BaseUrl + "/v1";
    public const string HyMtBaseModelName = "hf.co/tencent/Hy-MT2-1.8B-GGUF:Q4_K_M";
    public const string HyMtModelName = "valtrans-hymt2:1.8b";
    public const string HyMtQualityModelName = "valtrans-hymt2:7b";
    public const string HyMtQualityBaseModelName = "hf.co/tencent/Hy-MT2-7B-GGUF:Q4_K_M";
    public const string DefaultModelName = HyMtModelName;
    public const string UltraLightModelName = "qwen3:0.6b";
    public const string BalancedModelName = "qwen3:1.7b";
    public const string QualityModelName = "translategemma:4b";
    public static IReadOnlyList<LocalAiModelDefinition> SupportedModels { get; } =
    [
        new(HyMtModelName, "Hy-MT2 1.8B · 번역 특화", "약 1.13GB", "권장 · 빠른 KO·JP·EN 전용 번역"),
        new(HyMtQualityModelName, "Hy-MT2 7B · 품질 비교", "약 4.62GB", "선택 설치 · 더 많은 VRAM/RAM 필요 · 게임 중 지연 비교 권장"),
        new(UltraLightModelName, "Qwen3 0.6B · 초경량", "약 523MB", "게임 성능 우선 · 가장 적은 VRAM/RAM"),
        new(BalancedModelName, "Qwen3 1.7B · 범용", "약 1.4GB", "일반 대화 보조 · 번역 전용 모델 아님"),
        new(QualityModelName, "TranslateGemma 4B · 품질형", "약 3.3GB", "번역 품질 우선 · 더 많은 PC 자원 사용")
    ];
    private const string InstallerUrl = "https://ollama.com/download/OllamaSetup.exe";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };
    private Process? _serverProcess;
    private readonly HashSet<string> _pinnedModels = new(StringComparer.OrdinalIgnoreCase);

    public static string NormalizeModelName(string? modelName) =>
        SupportedModels.Any(model => model.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase))
            ? SupportedModels.First(model => model.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase)).Name
            : DefaultModelName;

    public static LocalAiModelDefinition GetModel(string? modelName)
    {
        var normalized = NormalizeModelName(modelName);
        return SupportedModels.First(model => model.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsHyMtModel(string? modelName) =>
        string.Equals(modelName, HyMtModelName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(modelName, HyMtQualityModelName, StringComparison.OrdinalIgnoreCase);

    private static string HyMtBaseFor(string modelName) =>
        modelName.Equals(HyMtQualityModelName, StringComparison.OrdinalIgnoreCase)
            ? HyMtQualityBaseModelName : HyMtBaseModelName;

    public string? FindOllamaExecutable()
    {
        var candidates = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ollama", "ollama.exe")
        };
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        candidates.AddRange(path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(folder => Path.Combine(folder.Trim('"'), "ollama.exe")));
        return candidates.FirstOrDefault(File.Exists);
    }

    public async Task<LocalAiStatus> GetStatusAsync(string? modelName, CancellationToken cancellationToken = default)
    {
        modelName = NormalizeModelName(modelName);
        var executable = FindOllamaExecutable();
        try
        {
            using var response = await _http.GetAsync(BaseUrl + "/api/tags", cancellationToken);
            if (!response.IsSuccessStatusCode) return new LocalAiStatus(executable is not null, false, false, false, 0, 0, executable);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(json);
            var installed = document.RootElement.TryGetProperty("models", out var models) &&
                            models.EnumerateArray().Any(item =>
                            {
                                var name = item.TryGetProperty("name", out var value) ? value.GetString() : null;
                                return ModelNamesMatch(name ?? "", modelName);
                            });
            var loaded = false;
            long modelSize = 0;
            long gpuVram = 0;
            if (installed)
            {
                try
                {
                    using var psResponse = await _http.GetAsync(BaseUrl + "/api/ps", cancellationToken);
                    if (psResponse.IsSuccessStatusCode)
                    {
                        var psJson = await psResponse.Content.ReadAsStringAsync(cancellationToken);
                        using var psDocument = JsonDocument.Parse(psJson);
                        if (psDocument.RootElement.TryGetProperty("models", out var runningModels))
                        {
                            foreach (var item in runningModels.EnumerateArray())
                            {
                                var name = item.TryGetProperty("name", out var value) ? value.GetString() : null;
                                if (name is null || !ModelNamesMatch(name, modelName, includeHyMtBase: true)) continue;
                                loaded = true;
                                if (item.TryGetProperty("size", out var sizeValue)) modelSize = sizeValue.GetInt64();
                                if (item.TryGetProperty("size_vram", out var vramValue)) gpuVram = vramValue.GetInt64();
                                break;
                            }
                        }
                    }
                }
                catch { }
            }
            return new LocalAiStatus(executable is not null, true, installed, loaded, modelSize, gpuVram, executable);
        }
        catch
        {
            return new LocalAiStatus(executable is not null, false, false, false, 0, 0, executable);
        }
    }

    public async Task<LocalAiInstallResult> WarmUpAsync(string? modelName,
        IProgress<LocalAiProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        modelName = NormalizeModelName(modelName);
        var model = GetModel(modelName);
        var status = await GetStatusAsync(modelName, cancellationToken);
        if (!status.ServerRunning && status.OllamaInstalled && status.ExecutablePath is not null)
        {
            progress?.Report(new LocalAiProgress("Ollama 서버를 시작하는 중…", 55));
            StartServer(status.ExecutablePath);
            status = await WaitForServerAsync(modelName, cancellationToken);
        }
        if (!status.Ready)
            return new LocalAiInstallResult(false, $"Ollama와 {model.DisplayName} 모델을 먼저 준비해 주세요.");
        progress?.Report(new LocalAiProgress(status.ModelLoaded
            ? "예열 유지 설정 중… 앱이 실행되는 동안 모델을 메모리에 유지합니다."
            : $"모델 예열 중… {model.SizeLabel} 모델을 메모리에 올리고 있습니다.", 72));
        using var warmupHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        var payload = new { model = modelName, prompt = "", stream = false, keep_alive = -1 };
        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await warmupHttp.PostAsync(BaseUrl + "/api/generate", content, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            return new LocalAiInstallResult(false,
                string.IsNullOrWhiteSpace(responseText) ? $"모델 예열 실패 ({(int)response.StatusCode})" : responseText);
        lock (_pinnedModels) _pinnedModels.Add(modelName);

        progress?.Report(new LocalAiProgress("예열 상태 확인 중…", 94));
        for (var i = 0; i < 10; i++)
        {
            status = await GetStatusAsync(modelName, cancellationToken);
            if (status.ModelLoaded)
                return new LocalAiInstallResult(true, $"예열 완료 · {model.DisplayName} 즉시 번역 가능");
            await Task.Delay(300, cancellationToken);
        }
        return new LocalAiInstallResult(false, "예열 요청은 끝났지만 메모리 로드 상태를 확인하지 못했습니다.");
    }

    public async Task UnloadModelAsync(string? modelName, CancellationToken cancellationToken = default)
    {
        modelName = NormalizeModelName(modelName);
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var payload = new { model = modelName, prompt = "", stream = false, keep_alive = 0 };
            using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/api/generate")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            using var response = await client.SendAsync(request, cancellationToken);
        }
        catch { }
        finally { lock (_pinnedModels) _pinnedModels.Remove(modelName); }
    }

    public void UnloadPinnedModelsOnExit()
    {
        string[] models;
        lock (_pinnedModels) models = _pinnedModels.ToArray();
        foreach (var modelName in models)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                var payload = new { model = modelName, prompt = "", stream = false, keep_alive = 0 };
                using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/api/generate")
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                };
                using var response = client.Send(request);
            }
            catch { }
        }
        lock (_pinnedModels) _pinnedModels.Clear();
    }

    public async Task<LocalAiInstallResult> InstallAndPrepareAsync(string? modelName,
        IProgress<LocalAiProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        modelName = NormalizeModelName(modelName);
        var model = GetModel(modelName);
        var executable = FindOllamaExecutable();
        if (executable is null)
        {
            progress?.Report(new LocalAiProgress("Ollama 설치 파일을 다운로드하는 중…", 5));
            executable = await DownloadAndInstallOllamaAsync(progress, cancellationToken);
        }

        var status = await GetStatusAsync(modelName, cancellationToken);
        if (!status.ServerRunning)
        {
            progress?.Report(new LocalAiProgress("로컬 AI 서버를 시작하는 중…", 48));
            StartServer(executable);
            status = await WaitForServerAsync(modelName, cancellationToken);
            if (!status.ServerRunning)
                return new LocalAiInstallResult(false, "Ollama는 설치됐지만 로컬 서버를 시작하지 못했습니다.");
        }

        if (!status.ModelInstalled)
        {
            progress?.Report(new LocalAiProgress($"{model.DisplayName} 다운로드 중… {model.SizeLabel}", 55));
            var downloadModel = IsHyMtModel(model.Name)
                ? model with { Name = HyMtBaseFor(model.Name) }
                : model;
            var pullResult = await PullModelAsync(executable, downloadModel, progress, cancellationToken);
            if (!pullResult.Success) return pullResult;
        }

        // Re-apply the verified chat template even when the alias already exists. Some Ollama
        // versions import the upstream GGUF template incorrectly, which produces unrelated output.
        if (IsHyMtModel(model.Name))
        {
            progress?.Report(new LocalAiProgress("Hy-MT2 번역 템플릿을 적용하는 중…", 88));
            var createResult = await CreateHyMtModelAsync(model.Name, cancellationToken);
            if (!createResult.Success) return createResult;
        }

        status = await GetStatusAsync(modelName, cancellationToken);
        return status.Ready
            ? new LocalAiInstallResult(true, $"{model.DisplayName} 로컬 번역이 준비되었습니다.")
            : new LocalAiInstallResult(false, "설치는 끝났지만 로컬 모델을 확인하지 못했습니다.");
    }

    private async Task<string> DownloadAndInstallOllamaAsync(IProgress<LocalAiProgress>? progress,
        CancellationToken cancellationToken)
    {
        var installerPath = Path.Combine(Path.GetTempPath(), $"Valtrans-OllamaSetup-{Guid.NewGuid():N}.exe");
        try
        {
            using var downloadHttp = new HttpClient { Timeout = TimeSpan.FromHours(1) };
            using (var response = await downloadHttp.GetAsync(InstallerUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength;
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using (var destination = new FileStream(installerPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                {
                    var buffer = new byte[81920];
                    long downloaded = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        downloaded += read;
                        if (total is > 0)
                        {
                            var percent = 5 + (int)Math.Min(35, downloaded * 35 / total.Value);
                            progress?.Report(new LocalAiProgress($"Ollama 다운로드 중… {downloaded / 1024 / 1024}MB", percent));
                        }
                    }
                    await destination.FlushAsync(cancellationToken);
                }
            }

            progress?.Report(new LocalAiProgress("Ollama 설치 창에서 설치를 완료해 주세요.", 42));
            using var installer = Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true
            }) ?? throw new InvalidOperationException("Ollama 설치 프로그램을 시작하지 못했습니다.");
            await installer.WaitForExitAsync(cancellationToken);
            if (installer.ExitCode != 0) throw new InvalidOperationException($"Ollama 설치가 완료되지 않았습니다. 코드: {installer.ExitCode}");

            for (var i = 0; i < 20; i++)
            {
                var executable = FindOllamaExecutable();
                if (executable is not null) return executable;
                await Task.Delay(500, cancellationToken);
            }
            throw new InvalidOperationException("Ollama 실행 파일을 찾지 못했습니다.");
        }
        finally
        {
            try { if (File.Exists(installerPath)) File.Delete(installerPath); } catch { }
        }
    }

    private void StartServer(string executable)
    {
        if (_serverProcess is { HasExited: false }) return;
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("serve");
        _serverProcess = Process.Start(startInfo);
    }

    private async Task<LocalAiStatus> WaitForServerAsync(string modelName, CancellationToken cancellationToken)
    {
        for (var i = 0; i < 30; i++)
        {
            var status = await GetStatusAsync(modelName, cancellationToken);
            if (status.ServerRunning) return status;
            await Task.Delay(500, cancellationToken);
        }
        return await GetStatusAsync(modelName, cancellationToken);
    }

    private static async Task<LocalAiInstallResult> PullModelAsync(string executable, LocalAiModelDefinition model,
        IProgress<LocalAiProgress>? progress, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("pull");
        startInfo.ArgumentList.Add(model.Name);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("모델 다운로드를 시작하지 못했습니다.");

        var latest = "";
        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            latest = Regex.Replace(e.Data, "\\x1B\\[[0-9;?]*[ -/]*[@-~]", "").Trim();
            if (latest.Length > 0) progress?.Report(new LocalAiProgress(Shorten(latest, 70), 75));
        };
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
        return process.ExitCode == 0
            ? new LocalAiInstallResult(true, $"{model.DisplayName} 다운로드를 완료했습니다.")
            : new LocalAiInstallResult(false, latest.Length > 0 ? latest : $"모델 다운로드 실패: {process.ExitCode}");
    }

    private static string HyMtChatTemplate(string modelName) =>
        modelName.Equals(HyMtQualityModelName, StringComparison.OrdinalIgnoreCase)
            ? "{{ if .System }}<|startoftext|>{{ .System }}<|extra_4|>{{ end }}{{ if .Prompt }}<|startoftext|>{{ .Prompt }}<|extra_0|>{{ end }}{{ .Response }}<|eos|>"
            : "<｜hy_begin▁of▁sentence｜>{{ if .System }}{{ .System }}<｜hy_place▁holder▁no▁3｜>{{ end }}{{ if .Prompt }}<｜hy_User｜>{{ .Prompt }}<｜hy_Assistant｜>{{ end }}{{ .Response }}<｜hy_place▁holder▁no▁2｜>";

    private static string[] HyMtStopTokens(string modelName) =>
        modelName.Equals(HyMtQualityModelName, StringComparison.OrdinalIgnoreCase)
            ? ["<|startoftext|>", "<|extra_4|>", "<|extra_0|>", "<|eos|>"]
            : ["<｜hy_place▁holder▁no▁2｜>"];

    private static async Task<LocalAiInstallResult> CreateHyMtModelAsync(string modelName, CancellationToken cancellationToken)
    {
        // 7B and 1.8B use different tokenizer special tokens, despite sharing a model family.
        // Verified against the official 7B tokenizer and the imported GGUF /api/show template.
        var template = HyMtChatTemplate(modelName);
        var payload = new
        {
            model = modelName,
            from = HyMtBaseFor(modelName),
            template,
            parameters = new
            {
                temperature = 0.0,
                top_p = 0.6,
                top_k = 20,
                repeat_penalty = 1.05,
                stop = HyMtStopTokens(modelName)
            },
            stream = false
        };
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(BaseUrl + "/api/create", content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return response.IsSuccessStatusCode
            ? new LocalAiInstallResult(true, "Hy-MT2 번역 템플릿 준비 완료")
            : new LocalAiInstallResult(false,
                string.IsNullOrWhiteSpace(body) ? $"Hy-MT2 템플릿 생성 실패 ({(int)response.StatusCode})" : body);
    }

    private static bool ModelNamesMatch(string actual, string selected, bool includeHyMtBase = false)
    {
        if (actual.Equals(selected, StringComparison.OrdinalIgnoreCase)) return true;
        if (includeHyMtBase && IsHyMtModel(selected) &&
            actual.Equals(HyMtBaseFor(selected), StringComparison.OrdinalIgnoreCase)) return true;
        return selected.Equals(QualityModelName, StringComparison.OrdinalIgnoreCase) &&
               actual.Equals("translategemma:latest", StringComparison.OrdinalIgnoreCase);
    }

    private static string Shorten(string value, int max) => value.Length <= max ? value : value[..max] + "…";
}

public sealed record LocalAiStatus(bool OllamaInstalled, bool ServerRunning, bool ModelInstalled,
    bool ModelLoaded, long ModelSizeBytes, long GpuVramBytes, string? ExecutablePath)
{
    public bool Ready => ServerRunning && ModelInstalled;
}

public sealed record LocalAiProgress(string Message, int Percent);
public sealed record LocalAiInstallResult(bool Success, string Message);
public sealed record LocalAiModelDefinition(string Name, string DisplayName, string SizeLabel, string Description);
