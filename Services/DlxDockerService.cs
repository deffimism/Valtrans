using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;

namespace Valtrans.Services;

public sealed class DlxDockerService
{
    public const string ImageName = "ghcr.io/owo-network/deeplx:latest";
    public const string ContainerName = "valtrans-dlx";
    public const string BaseUrl = "http://127.0.0.1:1188";
    public const string TranslateUrl = BaseUrl + "/translate";
    private const string ManagedLabel = "com.valtrans.managed=true";
    private const string InstallerUrl = "https://desktop.docker.com/win/main/amd64/Docker%20Desktop%20Installer.exe";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };
    private bool _startedManagedContainer;

    public string? FindDockerCli()
    {
        var candidates = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "DockerDesktop", "resources", "bin", "docker.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Docker", "Docker", "resources", "bin", "docker.exe")
        };
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        candidates.AddRange(path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(folder => Path.Combine(folder.Trim('"'), "docker.exe")));
        return candidates.FirstOrDefault(File.Exists);
    }

    public string? FindDockerDesktop()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "DockerDesktop", "Docker Desktop.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Docker", "Docker", "Docker Desktop.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    public async Task<DlxDockerStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var wslReady = await IsWslReadyAsync(cancellationToken);
        var docker = FindDockerCli();
        if (docker is null) return new DlxDockerStatus(false, wslReady, false, false, false, false, false);

        var info = await RunDockerAsync(docker, new[] { "info", "--format", "{{.ServerVersion}}" },
            TimeSpan.FromSeconds(6), cancellationToken);
        if (!info.Success) return new DlxDockerStatus(true, wslReady, false, false, false, false, false);

        var inspect = await RunDockerAsync(docker,
            new[] { "inspect", "--format", "{{.State.Running}}", ContainerName },
            TimeSpan.FromSeconds(6), cancellationToken);
        var exists = inspect.Success;
        var running = exists && inspect.Output.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        var label = exists
            ? await RunDockerAsync(docker,
                new[] { "inspect", "--format", "{{index .Config.Labels \"com.valtrans.managed\"}}", ContainerName },
                TimeSpan.FromSeconds(6), cancellationToken)
            : new DockerCommandResult(false, "", "", -1);
        var managed = label.Success && label.Output.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        var reachable = running && await IsApiReachableAsync(cancellationToken);
        return new DlxDockerStatus(true, wslReady, true, exists, running, reachable, managed);
    }

    public async Task<DlxDockerResult> PrepareAsync(
        IProgress<DlxDockerProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!await IsWslReadyAsync(cancellationToken))
        {
            progress?.Report(new DlxDockerProgress("WSL 2 설치 준비 중… 관리자 승인이 필요합니다.", 3));
            var wslResult = await InstallWslAsync(cancellationToken);
            if (!wslResult.Success) return wslResult;
            if (!await IsWslReadyAsync(cancellationToken))
                return new DlxDockerResult(false,
                    "WSL 2 설치를 적용하려면 Windows를 재시작해야 합니다. 재시작 후 개인 DLX 준비를 다시 눌러 주세요.");
        }

        var docker = FindDockerCli();
        if (docker is null)
        {
            progress?.Report(new DlxDockerProgress("Docker Desktop 설치 파일 다운로드 중…", 4));
            docker = await DownloadAndInstallDockerAsync(progress, cancellationToken);
        }

        var status = await GetStatusAsync(cancellationToken);
        if (!status.DaemonRunning)
        {
            progress?.Report(new DlxDockerProgress("Docker Desktop 시작 중…", 42));
            var desktop = FindDockerDesktop();
            if (desktop is null)
                return new DlxDockerResult(false, "Docker Desktop 실행 파일을 찾지 못했습니다.");

            Process.Start(new ProcessStartInfo { FileName = desktop, UseShellExecute = true });
            status = await WaitForDaemonAsync(progress, cancellationToken);
            if (!status.DaemonRunning)
                return new DlxDockerResult(false,
                    "Docker Desktop에서 이용약관 승인과 WSL 2 설정을 완료한 뒤 다시 눌러 주세요. Windows 재시작이 필요할 수 있습니다.");
        }

        if (status.ContainerExists && !status.ManagedByValtrans)
            return new DlxDockerResult(false,
                $"'{ContainerName}' 이름의 다른 컨테이너가 이미 있습니다. 이름을 바꾸거나 직접 정리한 뒤 다시 시도해 주세요.");

        if (!status.ContainerExists)
        {
            progress?.Report(new DlxDockerProgress("공식 DLX 이미지 다운로드 중…", 58));
            var pull = await RunDockerAsync(docker, new[] { "pull", ImageName },
                TimeSpan.FromMinutes(15), cancellationToken);
            if (!pull.Success)
                return new DlxDockerResult(false, DockerError("DLX 이미지 다운로드 실패", pull));

            progress?.Report(new DlxDockerProgress("Valtrans 전용 DLX 컨테이너 생성 중…", 84));
            var run = await RunDockerAsync(docker, new[]
            {
                "run", "-d", "--name", ContainerName,
                "--label", ManagedLabel,
                "-p", "127.0.0.1:1188:1188",
                "--restart", "no",
                ImageName
            }, TimeSpan.FromMinutes(2), cancellationToken);
            if (!run.Success)
                return new DlxDockerResult(false, DockerError("DLX 컨테이너 생성 실패", run));
            _startedManagedContainer = true;
        }
        else if (!status.ContainerRunning)
        {
            progress?.Report(new DlxDockerProgress("기존 DLX 컨테이너 시작 중…", 84));
            var start = await RunDockerAsync(docker, new[] { "start", ContainerName },
                TimeSpan.FromMinutes(1), cancellationToken);
            if (!start.Success)
                return new DlxDockerResult(false, DockerError("DLX 컨테이너 시작 실패", start));
            _startedManagedContainer = true;
        }
        else if (status.ManagedByValtrans)
        {
            _startedManagedContainer = true;
        }

        progress?.Report(new DlxDockerProgress("로컬 DLX 응답 확인 중…", 94));
        for (var i = 0; i < 20; i++)
        {
            if (await IsApiReachableAsync(cancellationToken))
                return new DlxDockerResult(true, "개인 DLX 서버가 127.0.0.1:1188에서 준비됐습니다.");
            await Task.Delay(500, cancellationToken);
        }
        return new DlxDockerResult(false, "컨테이너는 실행됐지만 DLX API가 응답하지 않습니다. Docker Desktop 상태를 확인해 주세요.");
    }

    public async Task<DlxDockerResult> EnsureExistingContainerRunningAsync(CancellationToken cancellationToken = default)
    {
        if (!await IsWslReadyAsync(cancellationToken))
            return new DlxDockerResult(false, "WSL 2가 설치되지 않았습니다. 개인 DLX 준비를 먼저 눌러 주세요.");
        var docker = FindDockerCli();
        if (docker is null) return new DlxDockerResult(false, "Docker Desktop과 개인 DLX를 먼저 준비해 주세요.");

        var status = await GetStatusAsync(cancellationToken);
        if (!status.DaemonRunning)
        {
            var desktop = FindDockerDesktop();
            if (desktop is null) return new DlxDockerResult(false, "Docker Desktop을 찾지 못했습니다.");
            Process.Start(new ProcessStartInfo { FileName = desktop, UseShellExecute = true });
            status = await WaitForDaemonAsync(null, cancellationToken, 45);
            if (!status.DaemonRunning)
                return new DlxDockerResult(false, "Docker Desktop이 준비되지 않았습니다. 직접 연 뒤 상태를 확인해 주세요.");
        }

        if (!status.ContainerExists)
            return new DlxDockerResult(false, "개인 DLX 준비 버튼으로 컨테이너를 먼저 만들어 주세요.");
        if (!status.ManagedByValtrans)
            return new DlxDockerResult(false, $"'{ContainerName}'은 Valtrans가 만든 컨테이너가 아닙니다.");
        if (!status.ContainerRunning)
        {
            var start = await RunDockerAsync(docker, new[] { "start", ContainerName },
                TimeSpan.FromMinutes(1), cancellationToken);
            if (!start.Success) return new DlxDockerResult(false, DockerError("DLX 시작 실패", start));
            _startedManagedContainer = true;
        }
        else
        {
            _startedManagedContainer = true;
        }

        for (var i = 0; i < 15; i++)
        {
            if (await IsApiReachableAsync(cancellationToken))
                return new DlxDockerResult(true, "개인 DLX 서버가 준비됐습니다.");
            await Task.Delay(400, cancellationToken);
        }
        return new DlxDockerResult(false, "DLX 컨테이너가 응답하지 않습니다.");
    }

    public void StopManagedContainerOnExit(bool enabled)
    {
        if (!enabled || !_startedManagedContainer) return;
        var docker = FindDockerCli();
        if (docker is null || !IsManagedContainerSync(docker)) return;
        try
        {
            using var process = Process.Start(CreateDockerStartInfo(docker,
                new[] { "stop", "--time", "3", ContainerName }));
            if (process is null) return;
            if (!process.WaitForExit(7000)) process.Kill(entireProcessTree: true);
        }
        catch { }
        finally { _startedManagedContainer = false; }
    }

    private static bool IsManagedContainerSync(string docker)
    {
        try
        {
            using var process = Process.Start(CreateDockerStartInfo(docker,
                new[] { "inspect", "--format", "{{index .Config.Labels \"com.valtrans.managed\"}}", ContainerName }));
            if (process is null || !process.WaitForExit(5000) || process.ExitCode != 0) return false;
            return process.StandardOutput.ReadToEnd().Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private async Task<string> DownloadAndInstallDockerAsync(IProgress<DlxDockerProgress>? progress,
        CancellationToken cancellationToken)
    {
        var installerPath = Path.Combine(Path.GetTempPath(), $"Valtrans-DockerDesktop-{Guid.NewGuid():N}.exe");
        try
        {
            using var download = new HttpClient { Timeout = TimeSpan.FromHours(2) };
            using (var response = await download.GetAsync(InstallerUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
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
                            var percent = 4 + (int)Math.Min(30, downloaded * 30 / total.Value);
                            progress?.Report(new DlxDockerProgress($"Docker Desktop 다운로드 중… {downloaded / 1024 / 1024}MB", percent));
                        }
                    }
                    await destination.FlushAsync(cancellationToken);
                }
            }

            // The download stream must be closed before Windows can execute the installer.
            progress?.Report(new DlxDockerProgress("Docker Desktop 사용자 설치 진행 중…", 36));
            var startInfo = new ProcessStartInfo { FileName = installerPath, UseShellExecute = true };
            startInfo.ArgumentList.Add("install");
            startInfo.ArgumentList.Add("--user");
            using var installer = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Docker Desktop 설치 프로그램을 시작하지 못했습니다.");
            await installer.WaitForExitAsync(cancellationToken);
            if (installer.ExitCode != 0)
                throw new InvalidOperationException($"Docker Desktop 설치가 완료되지 않았습니다. 코드: {installer.ExitCode}");

            for (var i = 0; i < 20; i++)
            {
                var docker = FindDockerCli();
                if (docker is not null) return docker;
                await Task.Delay(500, cancellationToken);
            }
            throw new InvalidOperationException("설치 후 docker.exe를 찾지 못했습니다. 앱을 다시 실행해 주세요.");
        }
        finally
        {
            try { if (File.Exists(installerPath)) File.Delete(installerPath); } catch { }
        }
    }

    private static async Task<bool> IsWslReadyAsync(CancellationToken cancellationToken)
    {
        var wsl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe");
        if (!File.Exists(wsl)) return false;
        var startInfo = new ProcessStartInfo
        {
            FileName = wsl,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.Unicode,
            StandardErrorEncoding = Encoding.Unicode
        };
        // --version only proves that the Store WSL package exists. --status also
        // verifies that the Windows features and WSL service can actually run.
        startInfo.ArgumentList.Add("--status");
        using var process = Process.Start(startInfo);
        if (process is null) return false;
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            var detail = output + error;
            var wsl2Unsupported = detail.Contains("WSL2", StringComparison.OrdinalIgnoreCase)
                && (detail.Contains("not supported", StringComparison.OrdinalIgnoreCase)
                    || detail.Contains("지원되지", StringComparison.OrdinalIgnoreCase));
            return process.ExitCode == 0
                && !detail.Contains("E_ACCESSDENIED", StringComparison.OrdinalIgnoreCase)
                && !wsl2Unsupported;
        }
        catch { return false; }
    }

    private static async Task<DlxDockerResult> InstallWslAsync(CancellationToken cancellationToken)
    {
        var wsl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe");
        if (!File.Exists(wsl)) return new DlxDockerResult(false, "이 Windows에서는 WSL 설치 도구를 찾지 못했습니다.");
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = wsl,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Normal
            };
            startInfo.ArgumentList.Add("--install");
            startInfo.ArgumentList.Add("--no-distribution");
            startInfo.ArgumentList.Add("--web-download");
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("WSL 2 설치를 시작하지 못했습니다.");
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode is 0 or 3010
                ? new DlxDockerResult(true, "WSL 2 설치를 완료했습니다.")
                : new DlxDockerResult(false,
                    $"WSL 2 설치가 완료되지 않았습니다. 코드: {process.ExitCode}. 관리자 승인과 인터넷 연결을 확인해 주세요.");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new DlxDockerResult(false, "WSL 2 관리자 승인이 취소되었습니다.");
        }
    }

    private async Task<DlxDockerStatus> WaitForDaemonAsync(IProgress<DlxDockerProgress>? progress,
        CancellationToken cancellationToken, int attempts = 90)
    {
        for (var i = 0; i < attempts; i++)
        {
            var status = await GetStatusAsync(cancellationToken);
            if (status.DaemonRunning) return status;
            progress?.Report(new DlxDockerProgress("Docker 엔진 시작 대기 중… 약관 창이 보이면 승인해 주세요.", 46));
            await Task.Delay(2000, cancellationToken);
        }
        return await GetStatusAsync(cancellationToken);
    }

    private async Task<bool> IsApiReachableAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync(BaseUrl, cancellationToken);
            return true; // 404도 로컬 HTTP 서버가 응답한 상태다.
        }
        catch { return false; }
    }

    private static async Task<DockerCommandResult> RunDockerAsync(string docker, IEnumerable<string> arguments,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        var startInfo = CreateDockerStartInfo(docker, arguments);
        Process? startedProcess;
        try
        {
            startedProcess = Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            return new DockerCommandResult(false, "", ex.Message, -1);
        }
        using var process = startedProcess;
        if (process is null) return new DockerCommandResult(false, "", "Docker 명령을 시작하지 못했습니다.", -1);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        var outputTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
            return new DockerCommandResult(process.ExitCode == 0, await outputTask, await errorTask, process.ExitCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return new DockerCommandResult(false, "", "명령 시간이 초과되었습니다.", -1);
        }
    }

    private static ProcessStartInfo CreateDockerStartInfo(string docker, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = docker,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    private static string DockerError(string prefix, DockerCommandResult result)
    {
        var detail = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
        detail = detail.Trim();
        if (detail.Length > 180) detail = detail[..180] + "…";
        return detail.Length == 0 ? $"{prefix} (코드 {result.ExitCode})" : $"{prefix}: {detail}";
    }
}

public sealed record DlxDockerStatus(bool DockerInstalled, bool WslReady, bool DaemonRunning,
    bool ContainerExists, bool ContainerRunning, bool ApiReachable, bool ManagedByValtrans)
{
    public bool Ready => DaemonRunning && ContainerRunning && ApiReachable;
}

public sealed record DlxDockerProgress(string Message, int Percent);
public sealed record DlxDockerResult(bool Success, string Message);
internal sealed record DockerCommandResult(bool Success, string Output, string Error, int ExitCode);
