using System.Runtime.InteropServices;

namespace Valtrans;

public partial class App : System.Windows.Application
{
    private const uint ShellChangeUpdateItem = 0x00002000;
    private const uint ShellNotifyPathW = 0x0005;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        _ = SetCurrentProcessExplicitAppUserModelID("Valtrans.GameChatTranslator");
        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
            SHChangeNotify(ShellChangeUpdateItem, ShellNotifyPathW, Environment.ProcessPath, IntPtr.Zero);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(uint eventId, uint flags,
        [MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr unused);
}
