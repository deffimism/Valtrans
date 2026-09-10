using System.Windows;
using Valtrans.Services;

namespace Valtrans;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (TestModeContext.TryParse(args, out var error))
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                Console.Error.WriteLine(error);
                return 2;
            }
        }

        var app = new App();
        app.InitializeComponent();
        app.Run();
        return TestModeExitCode.ExitCode;
    }
}

internal static class TestModeExitCode
{
    public static int ExitCode { get; set; }
}
