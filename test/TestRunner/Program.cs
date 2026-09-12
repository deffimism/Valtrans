using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Valtrans.Models;
using Valtrans.Services;

var jsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    WriteIndented = true
};

var options = RunnerOptions.Parse(args);
if (options.ShowHelp)
{
    Console.WriteLine("""
        Valtrans.TestRunner --scenario <path> [--seed 20260910] [--timeout 180]
            [--ocr-engine Windows|Paddle|Fast|Hybrid] [--no-focus-arena]
            [--baseline-output <path>] [--output <path>] [--app-exe <packaged Valtrans.exe>]

        Valtrans.TestRunner --compare-baseline <path> --current-baseline <path>

        Launches Test Arena + Valtrans test-mode and writes structured PASS/FAIL JSON.
        """);
    return 0;
}

if (options.CompareMode)
{
    var baselineReport = BaselineReportService.Load(options.BaselineComparePath!);
    var currentReport = BaselineReportService.Load(options.CurrentBaselinePath!);
    var result = BaselineReportService.Compare(baselineReport, currentReport);
    var payload = new
    {
        status = result.Status,
        baselinePath = options.BaselineComparePath,
        currentPath = options.CurrentBaselinePath,
        issues = result.Issues,
        warnings = result.Warnings
    };
    Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    return result.Status == "FAIL" ? 1 : 0;
}

var workspace = WorkspacePaths.Root;
var arenaExe = Path.Combine(workspace, "test/TestArena/bin/Release/net10.0-windows10.0.26100.0/Valtrans.TestArena.exe");
var valtransExe = options.AppExecutable ?? Path.Combine(workspace, "bin/Release/net10.0-windows10.0.26100.0/Valtrans.exe");
var appDll = Path.Combine(Path.GetDirectoryName(valtransExe)!, "Valtrans.dll");
if (!File.Exists(arenaExe) || !File.Exists(valtransExe) || !File.Exists(appDll))
{
    Console.Error.WriteLine("Build outputs missing. Run: dotnet build -c Release");
    return 2;
}
options = options with
{
    AppExecutable = valtransExe,
    AppAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(appDll))),
    AppVersion = FileVersionInfo.GetVersionInfo(appDll).ProductVersion
};

var runDir = Path.Combine(Path.GetTempPath(), "valtrans-e2e", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(runDir);
var readyFile = Path.Combine(runDir, "arena-ready.json");
var startSignal = Path.Combine(runDir, "arena-start.signal");
var hookManifest = Path.Combine(runDir, "arena-hooks.json");
var injectFile = Path.Combine(runDir, "arena-inject.json");
var reportFile = Path.Combine(runDir, "valtrans-report.json");
var runnerReportFile = options.OutputPath ?? Path.Combine(runDir, "runner-report.json");

var arena = Process.Start(new ProcessStartInfo(arenaExe)
{
    ArgumentList =
    {
        "--scenario", options.ScenarioPath,
        "--seed", options.Seed.ToString(),
        "--ready-file", readyFile,
        "--start-signal", startSignal,
        "--hook-manifest", hookManifest,
        "--inject-file", injectFile
    },
    UseShellExecute = true
});
if (arena is null) return 2;

if (!WaitForFile(readyFile, TimeSpan.FromSeconds(30)))
{
    await WriteRunnerReport(runnerReportFile, options, "FAIL", "Arena ready file timeout", null, null, "CAPTURE_FAILURE");
    TryKill(arena);
    return 1;
}

if (!options.SkipArenaFocus)
{
    FocusArenaWindow();
    await Task.Delay(800);
}

var valtransInfo = new ProcessStartInfo(valtransExe) { UseShellExecute = true };
foreach (var arg in new[]
         {
             "--test-mode",
             "--test-scenario", options.ScenarioPath,
             "--test-ready-file", readyFile,
             "--test-output", reportFile,
             "--test-timeout", options.TimeoutSeconds.ToString(),
             "--test-ocr-engine", options.OcrEngine
         })
    valtransInfo.ArgumentList.Add(arg);
var valtrans = Process.Start(valtransInfo);
if (valtrans is null)
{
    await WriteRunnerReport(runnerReportFile, options, "FAIL", "Failed to start Valtrans", null, null, "CRASH");
    TryKill(arena);
    return 2;
}

var ocrReadySignal = readyFile + ".ocr-ready";
var ocrReadyTimeout = options.OcrEngine is "Fast" or "Hybrid" or "Paddle"
    ? TimeSpan.FromSeconds(Math.Min(options.TimeoutSeconds, 240))
    : TimeSpan.FromSeconds(90);
if (!WaitForFile(ocrReadySignal, ocrReadyTimeout))
{
    await WriteRunnerReport(runnerReportFile, options, "FAIL", "Valtrans OCR ready timeout", null, null, "OCR_FAILURE");
    TryKill(valtrans);
    TryKill(arena);
    return 1;
}
await Task.Delay(1200);
await File.WriteAllTextAsync(startSignal, DateTime.UtcNow.ToString("O"));

var deadline = DateTime.UtcNow.AddSeconds(options.TimeoutSeconds + 30);
TestRunReport? valtransReport = null;
while (DateTime.UtcNow < deadline)
{
    if (File.Exists(reportFile))
    {
        try
        {
            var json = await File.ReadAllTextAsync(reportFile);
            valtransReport = JsonSerializer.Deserialize<TestRunReport>(json, jsonOptions);
            break;
        }
        catch { }
    }
    if (valtrans.HasExited && !File.Exists(reportFile)) break;
    await Task.Delay(500);
}

TryKill(valtrans);
TryKill(arena);

var status = valtransReport?.Status ?? "FAIL";
var detail = valtransReport is null ? "Valtrans test report missing" : null;
var failureClass = ClassifyFailure(valtransReport, detail);
BaselineReport? baseline = null;
if (valtransReport is not null && !string.IsNullOrWhiteSpace(options.BaselineOutputPath))
{
    try
    {
        var scenario = TestScenarioFile.Load(options.ScenarioPath);
        var traces = MessageTraceService.ReadFromLog(null, 200);
        baseline = BaselineReportService.Build(valtransReport, scenario, options.OcrEngine, options.Seed,
            version: options.AppVersion ?? "unknown", traces: traces);
        BaselineReportService.Write(options.BaselineOutputPath, baseline);
    }
    catch (Exception ex)
    {
        detail = (detail ?? "") + $" baseline-write-failed: {ex.Message}";
    }
}

await WriteRunnerReport(runnerReportFile, options, status, detail, valtransReport, baseline, failureClass);
Console.WriteLine(await File.ReadAllTextAsync(runnerReportFile));
return status == "PASS" ? 0 : 1;

static string ClassifyFailure(TestRunReport? valtrans, string? detail)
{
    if (valtrans is null) return "TIMEOUT";
    if (valtrans.Status == "PASS") return "PASS";
    if (detail?.Contains("ready file", StringComparison.OrdinalIgnoreCase) == true) return "CAPTURE_FAILURE";
    if (valtrans.Failures.Any(item => item.Contains("OCR", StringComparison.OrdinalIgnoreCase))) return "OCR_FAILURE";
    if (valtrans.Cases.Any(item => item.Status == "FAIL" && item.Detail?.Contains("translation", StringComparison.OrdinalIgnoreCase) == true))
        return "TRANSLATION_FAILURE";
    if (valtrans.Cases.Any(item => item.Status == "FAIL")) return "VALIDATION_FAILURE";
    return "TIMEOUT";
}

static async Task WriteRunnerReport(string path, RunnerOptions options, string status, string? detail,
    TestRunReport? valtrans, BaselineReport? baseline, string failureClass)
{
    var report = new
    {
        status,
        failureClassification = failureClass,
        scenario = options.ScenarioPath,
        seed = options.Seed,
        ocrEngine = options.OcrEngine,
        appExecutable = options.AppExecutable,
        appAssemblySha256 = options.AppAssemblySha256,
        appVersion = options.AppVersion,
        detail,
        baselinePath = options.BaselineOutputPath,
        baseline,
        valtrans,
        completedUtc = DateTime.UtcNow
    };
    await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    }));
}

static bool WaitForFile(string path, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
        if (File.Exists(path)) return true;
        Thread.Sleep(200);
    }
    return false;
}

static void TryKill(Process? process)
{
    try
    {
        if (process is { HasExited: false }) process.Kill(true);
    }
    catch { }
}

static void FocusArenaWindow()
{
    try
    {
        foreach (var process in Process.GetProcessesByName("Valtrans.TestArena"))
        {
            try
            {
                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    NativeSetForegroundWindow(process.MainWindowHandle);
                    return;
                }
            }
            catch { }
        }
    }
    catch { }
}

[System.Runtime.InteropServices.DllImport("user32.dll")]
static extern bool NativeSetForegroundWindow(IntPtr hWnd);

internal sealed record RunnerOptions(
    bool ShowHelp,
    bool CompareMode,
    bool SkipArenaFocus,
    string ScenarioPath,
    int Seed,
    int TimeoutSeconds,
    string OcrEngine,
    string? OutputPath,
    string? BaselineOutputPath,
    string? BaselineComparePath,
    string? CurrentBaselinePath)
{
    public string? AppExecutable { get; init; }
    public string? AppAssemblySha256 { get; init; }
    public string? AppVersion { get; init; }

    public static RunnerOptions Parse(string[] args)
    {
        var scenario = "";
        var seed = 20260910;
        var timeout = 180;
        var ocrEngine = "Windows";
        var skipArenaFocus = false;
        string? output = null;
        string? baseline = null;
        string? baselineCompare = null;
        string? currentBaseline = null;
        string? appExecutable = null;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--help":
                case "-h":
                    return new RunnerOptions(true, false, false, "", seed, timeout, ocrEngine, null, null, null, null);
                case "--no-focus-arena":
                    skipArenaFocus = true;
                    break;
                case "--app-exe":
                    appExecutable = Path.GetFullPath(args[++index]);
                    break;
                case "--compare-baseline":
                    baselineCompare = Path.GetFullPath(args[++index]);
                    break;
                case "--current-baseline":
                    currentBaseline = Path.GetFullPath(args[++index]);
                    break;
                case "--scenario":
                    scenario = Path.GetFullPath(args[++index]);
                    break;
                case "--seed":
                    seed = int.Parse(args[++index]);
                    break;
                case "--timeout":
                    timeout = int.Parse(args[++index]);
                    break;
                case "--ocr-engine":
                    ocrEngine = args[++index];
                    break;
                case "--output":
                    output = Path.GetFullPath(args[++index]);
                    break;
                case "--baseline-output":
                    baseline = Path.GetFullPath(args[++index]);
                    break;
            }
        }
        if (!string.IsNullOrWhiteSpace(baselineCompare) || !string.IsNullOrWhiteSpace(currentBaseline))
        {
            if (string.IsNullOrWhiteSpace(baselineCompare) || string.IsNullOrWhiteSpace(currentBaseline))
                throw new InvalidOperationException("--compare-baseline and --current-baseline are required together");
            return new RunnerOptions(false, true, skipArenaFocus, "", seed, timeout, ocrEngine, output, baseline,
                baselineCompare, currentBaseline);
        }
        if (string.IsNullOrWhiteSpace(scenario))
            scenario = Path.Combine(WorkspacePaths.Root, "testdata/scenarios/smoke_basic_001.json");
        return new RunnerOptions(false, false, skipArenaFocus, scenario, seed, timeout, ocrEngine, output, baseline, null, null)
        {
            AppExecutable = appExecutable
        };
    }
}

internal static class WorkspacePaths
{
    public static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Valtrans.csproj")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new InvalidOperationException("Workspace root not found");
        }
    }
}
