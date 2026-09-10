using System.IO;
using System.Text.Json;
using System.Windows;
using Valtrans.TestArena.Services;

namespace Valtrans.TestArena;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var options = ArenaCli.Parse(args);
        if (options.ShowHelp)
        {
            Console.WriteLine(ArenaCli.HelpText);
            return 0;
        }
        if (!File.Exists(options.ScenarioPath))
        {
            Console.Error.WriteLine($"Scenario not found: {options.ScenarioPath}");
            return 2;
        }

        if (options.ValidateOnly)
        {
            var scenario = ScenarioLoader.Load(options.ScenarioPath);
            var manifest = new
            {
                status = "PASS",
                scenario = scenario.Scenario,
                resolution = scenario.Resolution,
                dpi = scenario.Dpi,
                tags = scenario.Tags,
                events = scenario.Events.Count,
                background = scenario.Background,
                fuzz = scenario.Fuzz,
                animation = scenario.Animation
            };
            Console.WriteLine(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        var app = new App();
        app.Startup += (_, _) =>
        {
            var window = new MainWindow();
            window.Configure(new ArenaLaunchOptions(options.ScenarioPath, options.Seed, options.AutoExit,
                options.ReadyFilePath, options.StartSignalPath, options.InjectFilePath, options.HookManifestPath,
                options.Topmost));
            window.Show();
        };
        app.Run();
        return 0;
    }
}

internal static class ArenaCli
{
    public static ArenaCliOptions Parse(string[] args)
    {
        var scenario = "";
        var seed = 20260910;
        var autoExit = false;
        string? readyFile = null;
        string? startSignal = null;
        string? injectFile = null;
        string? hookManifest = null;
        var validateOnly = false;
        var topmost = false;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--help":
                case "-h":
                    return new ArenaCliOptions(true, "", seed, false, null, null, null, null, false, false);
                case "--validate-only":
                    validateOnly = true;
                    break;
                case "--topmost":
                    topmost = true;
                    break;
                case "--scenario":
                    scenario = args[++index];
                    break;
                case "--seed":
                    seed = int.Parse(args[++index]);
                    break;
                case "--auto-exit":
                    autoExit = true;
                    break;
                case "--ready-file":
                    readyFile = args[++index];
                    break;
                case "--start-signal":
                    startSignal = args[++index];
                    break;
                case "--inject-file":
                    injectFile = args[++index];
                    break;
                case "--hook-manifest":
                    hookManifest = args[++index];
                    break;
            }
        }
        if (string.IsNullOrWhiteSpace(scenario))
            scenario = Path.Combine(AppContext.BaseDirectory, "testdata", "scenarios", "smoke_basic_001.json");
        return new ArenaCliOptions(false, scenario, seed, autoExit, readyFile, startSignal, injectFile, hookManifest, validateOnly, topmost);
    }

    public const string HelpText = """
        Valtrans.TestArena.exe --scenario <path> [--seed 20260910] [--ready-file <path>] [--start-signal <path>]
            [--inject-file <path>] [--hook-manifest <path>] [--topmost] [--validate-only] [--auto-exit]

        Renders FPS-style chat for Valtrans screen-capture E2E tests.
        inject-file: poll JSON {"speaker":"PlayerA","text":"A小两个"} for agent injectMessage hook.
        validate-only: load scenario JSON and print manifest without opening UI.
        topmost: keep Arena above other windows (default off; OCR only needs the chat region visible at capture time).
        """;

    public sealed record ArenaCliOptions(bool ShowHelp, string ScenarioPath, int Seed, bool AutoExit,
        string? ReadyFilePath, string? StartSignalPath, string? InjectFilePath, string? HookManifestPath, bool ValidateOnly, bool Topmost);
}
