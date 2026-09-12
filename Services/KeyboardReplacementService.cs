using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Windows;
using Valtrans.Interop;

[assembly: InternalsVisibleTo("Valtrans.Tests")]

namespace Valtrans.Services;

public sealed class KeyboardReplacementService
{
    private readonly IChatInputBackend _input;
    public KeyboardReplacementService() : this(new WindowsChatInputBackend()) { }
    internal KeyboardReplacementService(IChatInputBackend input) => _input = input;

    public async Task<string> ReadSelectedChatAsync(IntPtr gameWindow)
    {
        RequireForeground(gameWindow);
        for (var attempt = 0; _input.ModifiersDown; attempt++)
        {
            if (attempt >= 45) throw new InvalidOperationException("보조 키를 놓은 뒤 다시 번역해 주세요.");
            await _input.DelayAsync(20);
            RequireForeground(gameWindow);
        }
        SendToGame(gameWindow, 0x41);
        await _input.DelayAsync(35);
        RequireForeground(gameWindow);
        // Clearing must succeed: never accept an older clipboard value as game chat.
        _input.ClearClipboard();
        var beforeCopy = _input.ClipboardSequence;
        SendToGame(gameWindow, 0x43);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            await _input.DelayAsync(attempt == 0 ? 110 : 50);
            RequireForeground(gameWindow);
            if (_input.ClipboardSequence == beforeCopy) continue;
            try
            {
                var text = _input.ReadClipboardText();
                if (text is not null) return text.Trim();
            }
            catch (ExternalException) { }
        }
        throw new InvalidOperationException("새 채팅을 복사하지 못했습니다. 채팅 입력창을 연 뒤 다시 눌러 주세요.");
    }

    public async Task ReplaceChatAsync(IntPtr gameWindow, string expectedSource, string translated)
    {
        // Translation may take seconds. Never overwrite newer chat or steal focus.
        var current = await ReadSelectedChatAsync(gameWindow);
        if (!string.Equals(current, expectedSource.Trim(), StringComparison.Ordinal))
            throw new InvalidOperationException("번역 중 채팅 내용이 바뀌어 교체하지 않았습니다. 다시 번역해 주세요.");
        RequireForeground(gameWindow);
        _input.SetClipboardText(translated);
        var translationSequence = _input.ClipboardSequence;
        SendToGame(gameWindow, 0x41);
        await _input.DelayAsync(25);
        RequireForeground(gameWindow);
        if (_input.ClipboardSequence != translationSequence || _input.ModifiersDown)
            throw new InvalidOperationException("입력 상태가 바뀌어 교체하지 않았습니다. 다시 번역해 주세요.");
        SendToGame(gameWindow, 0x56); // Ctrl+V, never Enter
    }

    private void RequireForeground(IntPtr gameWindow)
    {
        if (gameWindow == IntPtr.Zero || _input.ForegroundWindow != gameWindow)
            throw new InvalidOperationException("게임 창을 벗어나 채팅 교체를 취소했습니다.");
    }

    private void SendToGame(IntPtr gameWindow, ushort key)
    {
        RequireForeground(gameWindow);
        _input.SendChord(key);
    }
}

internal interface IChatInputBackend
{
    IntPtr ForegroundWindow { get; }
    uint ClipboardSequence { get; }
    bool ModifiersDown { get; }
    void ClearClipboard();
    string? ReadClipboardText();
    void SetClipboardText(string text);
    void SendChord(ushort key);
    Task DelayAsync(int milliseconds);
}

internal sealed class WindowsChatInputBackend : IChatInputBackend
{
    public IntPtr ForegroundWindow => NativeMethods.GetForegroundWindow();
    public uint ClipboardSequence => NativeMethods.GetClipboardSequenceNumber();
    public bool ModifiersDown => IsDown(0x11) || IsDown(0x12) || IsDown(0x10) || IsDown(0x5B) || IsDown(0x5C);
    public void ClearClipboard() => Clipboard.Clear();
    public string? ReadClipboardText() => Clipboard.ContainsText() ? Clipboard.GetText() : null;
    public void SetClipboardText(string text) => Clipboard.SetText(text);
    public Task DelayAsync(int milliseconds) => Task.Delay(milliseconds);
    private static bool IsDown(int key) => (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;

    public void SendChord(ushort key)
    {
        var inputs = new[]
        {
            Key(0x11, false), Key(key, false), Key(key, true), Key(0x11, true)
        };
        if (NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>()) != inputs.Length)
        {
            var release = new[] { Key(key, true), Key(0x11, true) };
            NativeMethods.SendInput((uint)release.Length, release, Marshal.SizeOf<NativeMethods.INPUT>());
            throw new InvalidOperationException("키 입력을 게임에 전달하지 못했습니다.");
        }
    }

    private static NativeMethods.INPUT Key(ushort key, bool up) => new()
    {
        type = 1,
        U = new NativeMethods.INPUTUNION
        {
            ki = new NativeMethods.KEYBDINPUT { wVk = key, dwFlags = up ? 0x0002u : 0u }
        }
    };
}
