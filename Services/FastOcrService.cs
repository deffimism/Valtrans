using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Valtrans.Services;

/// <summary>Phase 3 PP-OCRv5 fast OCR worker. Keeps PaddleOCR-VL separate.</summary>
public sealed class FastOcrService : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _processGate = new();
    private Process? _process;
    private bool _ready;
    private string _runtime = "";
    private bool _disposed;
    private readonly string _hostPath;
    public string Status { get; private set; } = "준비 필요 · Fast OCR (PP-OCRv5)";

    public bool IsReady
    {
        get { lock (_processGate) return _ready && _process is { HasExited: false }; }
    }

    public static bool HasRuntimeFiles(string runtime)
    {
        try
        {
            runtime = string.IsNullOrWhiteSpace(runtime) ? FindRuntime() : runtime;
            return File.Exists(Path.Combine(runtime, "fast_model.json")) &&
                   File.Exists(Path.Combine(runtime, ".venv", "Scripts", "python.exe"));
        }
        catch { return false; }
    }

    public FastOcrService(string? hostPath = null)
        => _hostPath = hostPath ?? Path.Combine(AppContext.BaseDirectory, "Ocr", "fast_ocr_host.py");

    public static string FindRuntime()
    {
        var packaged = Path.Combine(AppContext.BaseDirectory, "FastOcrRuntime");
        if (File.Exists(Path.Combine(packaged, "fast_model.json"))) return packaged;
        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Valtrans", "FastOcrRuntime");
        if (File.Exists(Path.Combine(installed, "fast_model.json"))) return installed;
        return installed;
    }

    public async Task InstallAsync(string runtime, IProgress<string> progress, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Stop();
            runtime = Path.GetFullPath(string.IsNullOrWhiteSpace(runtime) ? FindRuntime() : runtime);
            var setup = Path.Combine(Path.GetDirectoryName(_hostPath)!, "Setup-FastOcr.ps1");
            if (!File.Exists(setup)) throw new FileNotFoundException("Fast OCR 설치 파일이 없습니다.", setup);
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
                process.Start();
                _process = process;
            }
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            try
            {
                await process.WaitForExitAsync(token);
                process.WaitForExit();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("Fast OCR 환경 설치 실패 · Ocr/Setup-FastOcr.ps1 로그를 확인하세요.");
            }
            finally { Stop(); }
            Status = "설치 완료 · PP-OCRv5 준비 가능";
        }
        finally { _gate.Release(); }
    }

    public async Task PrepareAsync(string runtime, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try { await EnsureReadyAsync(runtime, token); }
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
        if (!File.Exists(python) || !File.Exists(Path.Combine(runtime, "fast_model.json")))
            throw new InvalidOperationException("Fast OCR 설치가 필요합니다. Ocr/Setup-FastOcr.ps1을 실행하세요.");
        if (!File.Exists(host)) throw new FileNotFoundException("Fast OCR 실행 스크립트가 없습니다.", host);
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
        info.Environment["PYTHONIOENCODING"] = "utf-8";
        var process = new Process { StartInfo = info };
        process.ErrorDataReceived += (_, _) => { };
        lock (_processGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            process.Start();
            _process = process;
        }
        process.BeginErrorReadLine();
        Status = "준비 중 · PP-OCRv5 로드";
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(300));
            var response = await process.StandardOutput.ReadLineAsync(deadline.Token)
                ?? throw new IOException("Fast OCR 프로세스가 준비 중 종료됐습니다.");
            using var json = JsonDocument.Parse(response);
            if (!json.RootElement.TryGetProperty("ready", out var ready) || !ready.GetBoolean())
                throw new InvalidOperationException(Error(json.RootElement));
            _runtime = runtime;
            _ready = true;
            Status = "준비 완료 · PP-OCRv5 Fast OCR";
        }
        catch { Stop(); throw; }
    }

    public async Task<OcrReadResult> ReadAsync(byte[] png, string runtime, CancellationToken token)
    {
        if (png.Length > 8_000_000) throw new ArgumentException("OCR 영역이 너무 큽니다.");
        await _gate.WaitAsync(token);
        try
        {
            await EnsureReadyAsync(runtime, token);
            var process = _process!;
            var id = Guid.NewGuid().ToString("N");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(45));
            var request = JsonSerializer.Serialize(new { id, png = Convert.ToBase64String(png) });
            try
            {
                await process.StandardInput.WriteLineAsync(request.AsMemory(), deadline.Token);
                await process.StandardInput.FlushAsync(deadline.Token);
                var response = await process.StandardOutput.ReadLineAsync(deadline.Token)
                    ?? throw new IOException("Fast OCR 프로세스가 종료됐습니다.");
                using var json = JsonDocument.Parse(response);
                var root = json.RootElement;
                if (!root.TryGetProperty("id", out var returnedId) || returnedId.GetString() != id)
                    throw new IOException("Fast OCR 응답 순서를 확인할 수 없습니다.");
                if (root.TryGetProperty("error", out _)) throw new InvalidOperationException(Error(root));
                var text = root.GetProperty("text").GetString() ?? "";
                var ms = root.GetProperty("milliseconds").GetDouble();
                var confidence = root.TryGetProperty("confidence", out var confidenceNode)
                    ? confidenceNode.GetDouble()
                    : EstimateConfidence(text);
                return new OcrReadResult(text, "MIXED", QualityScore: confidence,
                    RecognitionDurationMs: ms, TotalDurationMs: ms);
            }
            catch { Stop(); throw; }
        }
        finally { _gate.Release(); }
    }

    private static string Error(JsonElement root) => root.TryGetProperty("error", out var value)
        ? value.GetString() ?? "Fast OCR 준비 실패" : "Fast OCR 준비 실패";

    public void Stop()
    {
        lock (_processGate)
        {
            _ready = false;
            Status = "대기 · Fast OCR 해제됨";
            var process = _process;
            _process = null;
            if (process is null) return;
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            finally { process.Dispose(); }
        }
    }

    private static double EstimateConfidence(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var letters = text.Count(char.IsLetterOrDigit);
        return Math.Clamp(letters / (double)Math.Max(text.Length, 1), 0.35, 0.85);
    }

    public void Dispose() { _disposed = true; Stop(); }
}
