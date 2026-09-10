using System.Diagnostics;
using System.Drawing;
using Valtrans.Interop;

namespace Valtrans.Services;

public static class GameWindowDetectionService
{
    private static readonly (string Game, string Process)[] SupportedGames =
    {
        ("VALORANT", "VALORANT-Win64-Shipping")
    };

    public static bool TryGetForegroundSupportedGame(string selectedGame, out string game)
    {
        var found = TryGetForegroundSupportedGameInfo(selectedGame, out var info);
        game = info?.Game ?? "";
        return found;
    }

    public static bool TryGetForegroundSupportedGameInfo(string selectedGame, out GameWindowInfo? info)
    {
        info = null;
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;
        NativeMethods.GetWindowThreadProcessId(foreground, out var processId);
        if (processId == 0) return false;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            var match = SupportedGames.FirstOrDefault(item =>
                item.Process.Equals(process.ProcessName, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(match.Game)) return false;
            if (!selectedGame.Equals("Auto", StringComparison.OrdinalIgnoreCase) &&
                !selectedGame.Equals(match.Game, StringComparison.OrdinalIgnoreCase)) return false;
            var bounds = GetClientBounds(foreground);
            if (!bounds.HasValue) return false;
            info = BuildInfo(match.Game, foreground, bounds.Value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static GameWindowInfo? Detect(string selectedGame)
    {
        var candidates = selectedGame.Equals("Auto", StringComparison.OrdinalIgnoreCase)
            ? SupportedGames
            : SupportedGames.Where(item => item.Game.Equals(selectedGame, StringComparison.OrdinalIgnoreCase)).ToArray();

        string? runningGameWithoutWindow = null;
        foreach (var (game, processName) in candidates)
        {
            var processes = Process.GetProcessesByName(processName);
            if (processes.Length > 0) runningGameWithoutWindow ??= game;
            foreach (var process in processes)
            {
                using (process)
                {
                    try
                    {
                        var window = FindLargestWindowForProcess(process.Id);
                        if (window.HasValue) return BuildInfo(game, window.Value.Handle, window.Value.Bounds);
                    }
                    catch
                    {
                        // A protected/elevated game process may hide its window handle.
                    }
                }
            }
        }
        return runningGameWithoutWindow is null ? null :
            new GameWindowInfo(runningGameWithoutWindow, Rectangle.Empty, IntPtr.Zero, 96, "Unknown");
    }

    private static (IntPtr Handle, Rectangle Bounds)? FindLargestWindowForProcess(int processId)
    {
        (IntPtr Handle, Rectangle Bounds)? largest = null;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out var ownerProcessId);
            if (ownerProcessId != processId || !NativeMethods.IsWindowVisible(hwnd) ||
                !NativeMethods.GetClientRect(hwnd, out var client)) return true;

            var origin = new NativeMethods.POINT();
            if (!NativeMethods.ClientToScreen(hwnd, ref origin)) return true;
            var width = client.Right - client.Left;
            var height = client.Bottom - client.Top;
            if (width < 640 || height < 480) return true;
            var candidate = new Rectangle(origin.X, origin.Y, width, height);
            if (!largest.HasValue || candidate.Width * candidate.Height > largest.Value.Bounds.Width * largest.Value.Bounds.Height)
                largest = (hwnd, candidate);
            return true;
        }, IntPtr.Zero);
        return largest;
    }

    private static Rectangle? GetClientBounds(IntPtr hwnd)
    {
        if (!NativeMethods.GetClientRect(hwnd, out var client)) return null;
        var origin = new NativeMethods.POINT();
        if (!NativeMethods.ClientToScreen(hwnd, ref origin)) return null;
        var width = client.Right - client.Left;
        var height = client.Bottom - client.Top;
        return width >= 640 && height >= 480 ? new Rectangle(origin.X, origin.Y, width, height) : null;
    }

    private static GameWindowInfo BuildInfo(string game, IntPtr hwnd, Rectangle bounds)
    {
        uint dpi;
        try { dpi = NativeMethods.GetDpiForWindow(hwnd); }
        catch { dpi = 96; }
        if (dpi == 0) dpi = 96;

        var mode = "Windowed";
        var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MonitorDefaultToNearest);
        var monitorInfo = new NativeMethods.MONITORINFO
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>()
        };
        if (monitor != IntPtr.Zero && NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
        {
            var monitorWidth = monitorInfo.rcMonitor.Right - monitorInfo.rcMonitor.Left;
            var monitorHeight = monitorInfo.rcMonitor.Bottom - monitorInfo.rcMonitor.Top;
            if (Math.Abs(bounds.X - monitorInfo.rcMonitor.Left) <= 3 &&
                Math.Abs(bounds.Y - monitorInfo.rcMonitor.Top) <= 3 &&
                Math.Abs(bounds.Width - monitorWidth) <= 3 && Math.Abs(bounds.Height - monitorHeight) <= 3)
                mode = "Borderless";
        }
        return new GameWindowInfo(game, bounds, hwnd, dpi, mode);
    }
}

public sealed record GameWindowInfo(string Game, Rectangle ClientBounds, IntPtr WindowHandle, uint Dpi, string WindowMode)
{
    public string ProfileSignature => $"{Game}@{ClientBounds.X},{ClientBounds.Y}@{ClientBounds.Width}x{ClientBounds.Height}@{Dpi}@{WindowMode}";
}
