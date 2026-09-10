using System.IO;

namespace Valtrans.Services;

public static class TestModeContext
{
    public static bool Enabled { get; private set; }
    public static string ScenarioPath { get; private set; } = "";
    public static string OutputPath { get; private set; } = "";
    public static string ReadyFilePath { get; private set; } = "";
    public static string OcrReadySignalPath => string.IsNullOrWhiteSpace(ReadyFilePath)
        ? ""
        : ReadyFilePath + ".ocr-ready";
    public static string ArenaWindowTitle { get; private set; } = "Valtrans Test Arena";
    public static int TimeoutSeconds { get; private set; } = 120;
    public static string OcrEngine { get; private set; } = "Windows";

    public static void Configure(
        string scenarioPath,
        string outputPath,
        string readyFilePath,
        string arenaWindowTitle,
        int timeoutSeconds,
        string ocrEngine)
    {
        Enabled = true;
        ScenarioPath = scenarioPath;
        OutputPath = outputPath;
        ReadyFilePath = readyFilePath;
        ArenaWindowTitle = string.IsNullOrWhiteSpace(arenaWindowTitle) ? "Valtrans Test Arena" : arenaWindowTitle;
        TimeoutSeconds = Math.Clamp(timeoutSeconds, 30, 900);
        OcrEngine = NormalizeOcrEngine(ocrEngine);
    }

    public static bool TryParse(string[] args, out string? error)
    {
        error = null;
        if (!args.Contains("--test-mode", StringComparer.OrdinalIgnoreCase)) return false;

        string? scenario = null;
        string? output = null;
        string? ready = null;
        var arenaTitle = "Valtrans Test Arena";
        var timeout = 120;
        var ocrEngine = "Windows";
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--test-scenario":
                    scenario = args[++index];
                    break;
                case "--test-output":
                    output = args[++index];
                    break;
                case "--test-ready-file":
                    ready = args[++index];
                    break;
                case "--arena-title":
                    arenaTitle = args[++index];
                    break;
                case "--test-timeout":
                    timeout = int.Parse(args[++index]);
                    break;
                case "--test-ocr-engine":
                    ocrEngine = args[++index];
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(scenario))
        {
            error = "--test-scenario is required in --test-mode";
            return true;
        }
        if (string.IsNullOrWhiteSpace(output))
            output = Path.Combine(Path.GetTempPath(), $"valtrans-test-{Guid.NewGuid():N}.json");
        if (string.IsNullOrWhiteSpace(ready))
        {
            error = "--test-ready-file is required in --test-mode";
            return true;
        }

        Configure(scenario, output, ready, arenaTitle, timeout, ocrEngine);
        return true;
    }

    private static string NormalizeOcrEngine(string value) => value.Trim() switch
    {
        "Windows" or "windows" => "Windows",
        "Paddle" or "paddle" or "VL" or "vl" => "Paddle",
        "Fast" or "fast" or "PP-OCRv5" or "pp-ocrv5" => "Fast",
        "Hybrid" or "hybrid" => "Hybrid",
        _ => "Windows"
    };
}
