using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using Valtrans.Interop;

namespace Valtrans.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int HotkeyId = 0xA17;
    private readonly HwndSource _source;
    private bool _registered;
    public bool IsRegistered => _registered;

    public event EventHandler? Pressed;

    public GlobalHotkeyService(System.Windows.Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(handle) ?? throw new InvalidOperationException("창 핸들을 만들 수 없습니다.");
        _source.AddHook(WndProc);
    }

    public void Register(string gesture)
    {
        Unregister();
        var (modifiers, key) = Parse(gesture);
        if (!NativeMethods.RegisterHotKey(_source.Handle, HotkeyId, modifiers | NativeMethods.ModNoRepeat,
                (uint)KeyInterop.VirtualKeyFromKey(key)))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"단축키 {gesture}를 등록하지 못했습니다. 다른 앱에서 사용 중일 수 있습니다.");
        }
        _registered = true;
    }

    public static bool TryValidate(string gesture, out string error)
    {
        try
        {
            Parse(gesture);
            error = "";
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static (uint Modifiers, Key Key) Parse(string gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture)) throw new ArgumentException("단축키가 비어 있습니다.");
        var parts = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) throw new ArgumentException("올바른 단축키를 입력해 주세요.");

        uint modifiers = 0;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            modifiers |= parts[i].ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => NativeMethods.ModControl,
                "ALT" => NativeMethods.ModAlt,
                "SHIFT" => NativeMethods.ModShift,
                "WIN" or "WINDOWS" => NativeMethods.ModWin,
                _ => throw new ArgumentException($"알 수 없는 보조키: {parts[i]}")
            };
        }

        var keyText = parts[^1];
        Key key;
        if (keyText.Length == 1 && char.IsLetter(keyText[0]))
            key = Enum.Parse<Key>(keyText.ToUpperInvariant());
        else if (keyText.Length == 1 && char.IsDigit(keyText[0]))
            key = Enum.Parse<Key>($"D{keyText}");
        else if (keyText is "\\" or "₩")
            key = Key.Oem5;
        else if (!Enum.TryParse(keyText, true, out key))
            throw new ArgumentException($"알 수 없는 키: {keyText}");

        return (modifiers, key);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }
        return IntPtr.Zero;
    }

    public void Unregister()
    {
        if (!_registered) return;
        NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = false;
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(WndProc);
    }
}
