using System.Runtime.InteropServices;
using System.Windows;
using Valtrans.Interop;

namespace Valtrans.Services;

public sealed class KeyboardReplacementService
{
    private const ushort VkControl = 0x11;
    private const ushort VkMenu = 0x12;
    private const ushort VkShift = 0x10;
    private const uint InputKeyboard = 1;
    private const uint KeyUp = 0x0002;

    public async Task<string> ReadSelectedChatAsync(IntPtr gameWindow)
    {
        await WaitForModifiersReleasedAsync();
        NativeMethods.SetForegroundWindow(gameWindow);
        SendChord(0x41); // Ctrl+A
        await Task.Delay(35);
        try { Clipboard.Clear(); } catch { }
        SendChord(0x43); // Ctrl+C
        await Task.Delay(110);

        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                if (Clipboard.ContainsText()) return Clipboard.GetText().Trim();
            }
            catch { }
            await Task.Delay(50);
        }
        return "";
    }

    public async Task ReplaceChatAsync(IntPtr gameWindow, string translated)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                Clipboard.SetText(translated);
                break;
            }
            catch when (attempt < 5) { await Task.Delay(50); }
        }

        NativeMethods.SetForegroundWindow(gameWindow);
        await Task.Delay(35);
        SendChord(0x41); // Ctrl+A again in case selection changed
        await Task.Delay(25);
        SendChord(0x56); // Ctrl+V
    }

    private static async Task WaitForModifiersReleasedAsync()
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(900);
        while (DateTime.UtcNow < deadline &&
               (IsDown(VkControl) || IsDown(VkMenu) || IsDown(VkShift)))
            await Task.Delay(20);
    }

    private static bool IsDown(int key) => (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;

    private static void SendChord(ushort key)
    {
        var inputs = new[]
        {
            Key(VkControl, false), Key(key, false), Key(key, true), Key(VkControl, true)
        };
        if (NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>()) == 0)
            throw new InvalidOperationException("키 입력을 게임에 전달하지 못했습니다.");
    }

    private static NativeMethods.INPUT Key(ushort key, bool up) => new()
    {
        type = InputKeyboard,
        U = new NativeMethods.INPUTUNION
        {
            ki = new NativeMethods.KEYBDINPUT { wVk = key, dwFlags = up ? KeyUp : 0 }
        }
    };
}
