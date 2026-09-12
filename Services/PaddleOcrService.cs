using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Valtrans.Services;

public sealed class PaddleOcrService : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _processGate = new();
    private Process? _process;
    private bool _ready;
    private string _runtime = "";
    private bool _disposed;
    private readonly string _hostPath;
    public string Status { get; private set; } = "준비 필요 · 별도 GPU 실행 환경";

    public bool IsReady
    {
        get { lock (_processGate) return _ready && _process is { HasExited: false }; }
    }

    public static bool HasRuntimeFiles(string runtime)
    {
        try
        {
            runtime = string.IsNullOrWhiteSpace(runtime) ? FindRuntime() : runtime;
            return File.Exists(Path.Combine(runtime, "model.json")) &&
                   File.Exists(Path.Combine(runtime, ".venv", "Scripts", "python.exe"));
        }
        catch { return false; }
    }

    public PaddleOcrService(string? hostPath = null)
        => _hostPath = hostPath ?? Path.Combine(AppContext.BaseDirectory, "Ocr", "paddle_host.py");

    public static string FindRuntime()
    {
        var packaged = Path.Combine(AppContext.BaseDirectory, "OcrRuntime");
        if (File.Exists(Path.Combine(packaged, "model.json"))) return packaged;
        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Valtrans", "OcrRuntime");
        if (File.Exists(Path.Combine(installed, "model.json"))) return installed;
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, "artifacts", "ocr-vl");
            if (File.Exists(Path.Combine(candidate, "model.json"))) return candidate;
        }
        return installed;
    }

    public async Task PrepareAsync(string runtime, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try { await EnsureReadyAsync(runtime, token); }
        finally { _gate.Release(); }
    }

    public async Task InstallAsync(string runtime, IProgress<string> progress, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Stop();
            runtime = Path.GetFullPath(string.IsNullOrWhiteSpace(runtime) ? FindRuntime() : runtime);
            var setup = Path.Combine(Path.GetDirectoryName(_hostPath)!, "Setup.ps1");
            if (!File.Exists(setup)) throw new FileNotFoundException("OCR 설치 파일이 없습니다.", setup);
            var info = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", setup, "-RuntimeDirectory", runtime })
                info.ArgumentList.Add(arg);
            var errors = new StringBuilder();
            var process = new Process { StartInfo = info };
            process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) progress.Report(e.Data); };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                lock (errors) { errors.AppendLine(e.Data); if (errors.Length > 8000) errors.Remove(0, errors.Length - 8000); }
            };
            lock (_processGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                process.Start(); _process = process;
            }
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
            try
            {
                await process.WaitForExitAsync(token);
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    string details;
                    lock (errors) details = errors.ToString();
                    throw new InvalidOperationException(details.Contains("uv installation failed", StringComparison.Ordinal)
                        ? "설치 도구 다운로드 실패 · 네트워크를 확인하고 OCR 설치를 다시 눌러 주세요."
                        : "OCR 환경 설치 실패 · 설치 가이드의 명령으로 상세 오류를 확인하세요.");
                }
            }
            finally { Stop(); }
            Status = "설치 완료 · OCR 모델을 준비합니다";
        }
        finally { _gate.Release(); }
    }

    private async Task EnsureReadyAsync(string runtime, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        runtime = Path.GetFullPath(string.IsNullOrWhiteSpace(runtime) ? FindRuntime() : runtime);
        if (_ready && _runtime == runtime && _process is { HasExited: false }) return;
        Stop();
        var python = Path.Combine(runtime, ".venv", "Scripts", "python.exe");
        var host = _hostPath;
        if (!File.Exists(python) || !File.Exists(Path.Combine(runtime, "model.json")))
            throw new InvalidOperationException("Paddle OCR 설치가 필요합니다. 시작 가이드의 ‘OCR 설치 · 준비’를 누르세요.");
        if (!File.Exists(host)) throw new FileNotFoundException("OCR 실행 스크립트가 배포 파일에 없습니다.", host);
        var info = new ProcessStartInfo(python)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = runtime
        };
        info.ArgumentList.Add(host);
        info.ArgumentList.Add("--runtime");
        info.ArgumentList.Add(runtime);
        info.Environment["HF_HUB_OFFLINE"] = "1";
        info.Environment["PYTHONIOENCODING"] = "utf-8";
        var process = new Process { StartInfo = info };
        // Drain warnings without storing user text or blocking the child pipe.
        process.ErrorDataReceived += (_, _) => { };
        lock (_processGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            process.Start();
            _process = process;
        }
        process.BeginErrorReadLine();
        Status = "준비 중 · 모델을 GPU에 적재하고 있습니다";
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(90));
            var response = await process.StandardOutput.ReadLineAsync(deadline.Token)
                ?? throw new IOException("OCR 프로세스가 준비 중 종료됐습니다.");
            using var json = JsonDocument.Parse(response);
            if (!json.RootElement.TryGetProperty("ready", out var ready) || !ready.GetBoolean())
                throw new InvalidOperationException(Error(json.RootElement));
            _runtime = runtime;
            _ready = true;
            Status = $"준비 완료 · {json.RootElement.GetProperty("gpu").GetString()} · 로컬 OCR";
        }
        catch { Stop(); throw; }
    }

    public async Task<OcrReadResult> ReadAsync(byte[] png, string runtime, CancellationToken token)
    {
        if (png.Length > 8_000_000) throw new ArgumentException("OCR 영역이 너무 큽니다. 채팅 부분만 선택하세요.");
        await _gate.WaitAsync(token);
        try
        {
            await EnsureReadyAsync(runtime, token);
            var process = _process!;
            var id = Guid.NewGuid().ToString("N");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(28));
            var request = JsonSerializer.Serialize(new { id, png = Convert.ToBase64String(png) });
            try
            {
                await process.StandardInput.WriteLineAsync(request.AsMemory(), deadline.Token);
                await process.StandardInput.FlushAsync(deadline.Token);
                var response = await process.StandardOutput.ReadLineAsync(deadline.Token)
                    ?? throw new IOException("OCR 프로세스가 종료됐습니다.");
                using var json = JsonDocument.Parse(response);
                var root = json.RootElement;
                if (!root.TryGetProperty("id", out var returnedId) || returnedId.GetString() != id)
                    throw new IOException("OCR 응답 순서를 확인할 수 없습니다.");
                if (root.TryGetProperty("error", out _)) throw new InvalidOperationException(Error(root));
                var text = root.GetProperty("text").GetString() ?? "";
                var finished = root.GetProperty("finished").GetBoolean();
                if (!finished && string.IsNullOrWhiteSpace(text))
                    throw new TimeoutException("Paddle OCR이 제한 시간 안에 읽지 못했습니다. 최신 1줄 모드·영역 표시를 확인하거나 Windows OCR을 시도하세요.");
                var ms = root.GetProperty("milliseconds").GetDouble();
                return new OcrReadResult(text, "MIXED",
                    RecognitionDurationMs: ms, TotalDurationMs: ms, QualityScoreAvailable: false);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                Stop();
                throw new TimeoutException("Paddle OCR 응답이 지연되어 프로세스를 정리했습니다. 영역과 GPU 메모리를 확인하세요.");
            }
            catch { Stop(); throw; }
        }
        finally { _gate.Release(); }
    }

    private static string Error(JsonElement root) => root.TryGetProperty("error", out var value)
        ? value.GetString() ?? "OCR 준비 실패" : "OCR 준비 실패";

    public void Stop()
    {
        lock (_processGate)
        {
            _ready = false;
            Status = "대기 · OCR 모델 메모리 해제됨";
            var process = _process;
            _process = null;
            if (process is null) return;
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            finally { process.Dispose(); }
        }
    }

    public void Dispose() { _disposed = true; Stop(); }
}
