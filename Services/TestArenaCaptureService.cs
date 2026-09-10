using System.Text;
using Valtrans.Interop;
using Valtrans.Models;

namespace Valtrans.Services;

/// <summary>
/// Resolves Test Arena chat capture coordinates from the live HWND so screen capture
/// stays aligned even when DPI awareness or window placement differs between processes.
/// </summary>
public static class TestArenaCaptureService
{
    public static bool TryResolveChatRegion(TestArenaReadyFile ready, string windowTitle, out CaptureRegion region)
    {
        region = new CaptureRegion();
        if (!ready.ChatRegion.IsValid || !ready.WindowBounds.IsValid) return false;

        var hwnd = FindTopLevelWindow(windowTitle);
        if (hwnd == IntPtr.Zero) return false;

        var origin = new NativeMethods.POINT();
        if (!NativeMethods.ClientToScreen(hwnd, ref origin)) return false;

        var relX = ready.ChatRegion.X - ready.WindowBounds.X;
        var relY = ready.ChatRegion.Y - ready.WindowBounds.Y;
        region = new CaptureRegion
        {
            X = origin.X + relX,
            Y = origin.Y + relY,
            Width = ready.ChatRegion.Width,
            Height = ready.ChatRegion.Height
        };
        return region.IsValid;
    }

    public static bool TryFocusArena(string windowTitle)
    {
        var hwnd = FindTopLevelWindow(windowTitle);
        if (hwnd == IntPtr.Zero) return false;
        return NativeMethods.SetForegroundWindow(hwnd);
    }

    private static IntPtr FindTopLevelWindow(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return IntPtr.Zero;
        IntPtr found = IntPtr.Zero;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd)) return true;
            var buffer = new StringBuilder(256);
            NativeMethods.GetWindowText(hwnd, buffer, buffer.Capacity);
            if (!buffer.ToString().Equals(title, StringComparison.OrdinalIgnoreCase)) return true;
            found = hwnd;
            return false;
        }, IntPtr.Zero);
        return found;
    }
}
