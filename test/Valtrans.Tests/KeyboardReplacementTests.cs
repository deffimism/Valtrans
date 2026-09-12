using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

public sealed class KeyboardReplacementTests
{
    private sealed class FakeInput : IChatInputBackend
    {
        public IntPtr ForegroundWindow { get; set; } = new(42);
        public uint ClipboardSequence { get; set; } = 1;
        public bool ModifiersDown { get; set; }
        public string? Clipboard { get; set; } = "old private text";
        public string Chat { get; set; } = "왼쪽 조심해";
        public bool CopyWorks { get; set; } = true;
        public bool ClearFails { get; set; }
        public List<ushort> Chords { get; } = [];
        public Action<int>? OnDelay { get; set; }
        public void ClearClipboard()
        {
            if (ClearFails) throw new InvalidOperationException("clipboard busy");
            Clipboard = null;
            ClipboardSequence++;
        }
        public string? ReadClipboardText() => Clipboard;
        public void SetClipboardText(string text) { Clipboard = text; ClipboardSequence++; }
        public void SendChord(ushort key)
        {
            Chords.Add(key);
            if (key == 0x43 && CopyWorks) SetClipboardText(Chat);
            if (key == 0x56) Chat = Clipboard!;
        }
        public Task DelayAsync(int milliseconds) { OnDelay?.Invoke(milliseconds); return Task.CompletedTask; }
    }

    [Fact]
    public async Task Unchanged_chat_is_replaced_without_sending_enter()
    {
        var input = new FakeInput();
        await new KeyboardReplacementService(input).ReplaceChatAsync(new(42), input.Chat, "Watch left");
        Assert.Equal("Watch left", input.Chat);
        Assert.DoesNotContain((ushort)0x0D, input.Chords);
    }

    [Fact]
    public async Task New_chat_is_not_overwritten()
    {
        var input = new FakeInput { Chat = "새로 쓴 문장" };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new KeyboardReplacementService(input).ReplaceChatAsync(new(42), "이전 문장", "Old translation"));
        Assert.Equal("새로 쓴 문장", input.Chat);
        Assert.DoesNotContain((ushort)0x56, input.Chords);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Failed_copy_never_reads_old_clipboard(bool clearFails)
    {
        var input = new FakeInput { CopyWorks = false, ClearFails = clearFails };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new KeyboardReplacementService(input).ReadSelectedChatAsync(new(42)));
        Assert.DoesNotContain((ushort)0x56, input.Chords);
    }

    [Theory]
    [InlineData(35)]
    [InlineData(110)]
    [InlineData(25)]
    public async Task Losing_focus_cancels_before_any_paste(int delay)
    {
        var input = new FakeInput();
        input.OnDelay = ms => { if (ms == delay) input.ForegroundWindow = new(99); };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new KeyboardReplacementService(input).ReplaceChatAsync(new(42), input.Chat, "Watch left"));
        Assert.DoesNotContain((ushort)0x56, input.Chords);
    }

    [Fact]
    public async Task Clipboard_change_before_paste_cancels()
    {
        var input = new FakeInput();
        input.OnDelay = ms => { if (ms == 25) input.SetClipboardText("unrelated"); };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new KeyboardReplacementService(input).ReplaceChatAsync(new(42), input.Chat, "Watch left"));
        Assert.DoesNotContain((ushort)0x56, input.Chords);
    }

    [Fact]
    public async Task Held_modifier_times_out_without_selecting_anything()
    {
        var input = new FakeInput { ModifiersDown = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new KeyboardReplacementService(input).ReadSelectedChatAsync(new(42)));
        Assert.Empty(input.Chords);
    }
}
