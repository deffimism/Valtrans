using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Valtrans.Interop;

namespace Valtrans;

public partial class OverlayWindow : System.Windows.Window
{
    private readonly ObservableCollection<OverlayLine> _lines = new();
    private bool _clickThrough;

    public OverlayWindow()
    {
        InitializeComponent();
        TranslationItems.ItemsSource = _lines;
        SourceInitialized += (_, _) => ApplyExtendedStyle();
    }

    public void AddTranslation(string original, string translation, int displaySeconds = 15)
    {
        Dispatcher.Invoke(() =>
        {
            if (_lines.Count > 0 && _lines[^1].Original == original && _lines[^1].Translation == translation) return;
            var line = new OverlayLine(Guid.NewGuid(), original, translation);
            _lines.Add(line);
            while (_lines.Count > 5) _lines.RemoveAt(0);
            _ = ExpireLineAsync(line, displaySeconds);
        });
    }

    public void SetFontSize(double fontSize) => TranslationItems.FontSize = Math.Clamp(fontSize, 11, 32);

    private async Task ExpireLineAsync(OverlayLine line, int displaySeconds)
    {
        if (displaySeconds <= 0) return;
        await Task.Delay(displaySeconds * 1000);
        if (!Dispatcher.HasShutdownStarted)
            await Dispatcher.InvokeAsync(() => _lines.Remove(line));
    }

    public void SetClickThrough(bool enabled)
    {
        _clickThrough = enabled;
        ResizeHandle.Visibility = enabled ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        if (IsInitialized) ApplyExtendedStyle();
    }

    public void SetBackgroundOpacity(double opacity)
    {
        opacity = Math.Clamp(opacity, 0, 1);
        OverlayRoot.Background = Brush(opacity, 17, 24, 44);
    }

    public void SetBorderOpacity(double opacity) =>
        OverlayRoot.BorderBrush = Brush(Math.Clamp(opacity, 0, 1), 129, 140, 248);

    private static SolidColorBrush Brush(double opacity, byte red, byte green, byte blue) =>
        new(Color.FromArgb((byte)Math.Round(opacity * 255), red, green, blue));

    private void ApplyExtendedStyle()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64();
        style |= NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate;
        if (_clickThrough) style |= NativeMethods.WsExTransparent;
        else style &= ~NativeMethods.WsExTransparent;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, new IntPtr(style));
    }

    private void DragBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_clickThrough || e.LeftButton != MouseButtonState.Pressed) return;
        e.Handled = true;
        NativeMethods.ReleaseCapture();
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SendMessage(hwnd, NativeMethods.WmNcLButtonDown, new IntPtr(NativeMethods.HtCaption), IntPtr.Zero);
    }

    private void ResizeHandle_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_clickThrough || e.LeftButton != MouseButtonState.Pressed) return;
        e.Handled = true;
        NativeMethods.ReleaseCapture();
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SendMessage(hwnd, NativeMethods.WmNcLButtonDown, new IntPtr(NativeMethods.HtBottomRight), IntPtr.Zero);
    }
}

public sealed record OverlayLine(Guid Id, string Original, string Translation);
