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
    private double _backgroundOpacity = 0.38;
    private double _borderOpacity = 0.48;
    public event EventHandler? MoveFinished;

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
            if (_lines.Count > 0 && _lines[^1].Original == original && _lines[^1].Translation == translation &&
                string.IsNullOrEmpty(_lines[^1].SkipReason)) return;
            var line = new OverlayLine(Guid.NewGuid(), original, translation);
            _lines.Add(line);
            while (_lines.Count > 5) _lines.RemoveAt(0);
            _ = ExpireLineAsync(line, displaySeconds);
        });
    }

    public void AddSkipNotice(string headline, string detail, string? sourceLine = null, int displaySeconds = 10)
    {
        Dispatcher.Invoke(() =>
        {
            var line = new OverlayLine(Guid.NewGuid(), sourceLine ?? "", headline, detail);
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
        MoveBar.Visibility = enabled ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        ResizeHandle.Visibility = enabled ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        UpdateAppearance();
        if (IsInitialized) ApplyExtendedStyle();
    }

    public void SetBackgroundOpacity(double opacity)
    {
        opacity = Math.Clamp(opacity, 0, 1);
        _backgroundOpacity = opacity;
        UpdateAppearance();
    }

    public void SetBorderOpacity(double opacity)
    {
        _borderOpacity = Math.Clamp(opacity, 0, 1);
        UpdateAppearance();
    }

    private void UpdateAppearance()
    {
        // Transparent layered pixels cannot reliably receive mouse input. Only
        // while editing, provide a visible hit area; restore exact settings on lock.
        OverlayRoot.Background = Brush(_clickThrough ? _backgroundOpacity : Math.Max(0.12, _backgroundOpacity), 17, 24, 44);
        OverlayRoot.BorderBrush = Brush(_clickThrough ? _borderOpacity : Math.Max(0.65, _borderOpacity), 129, 140, 248);
    }

    private void FinishMove_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        SetClickThrough(true);
        MoveFinished?.Invoke(this, EventArgs.Empty);
    }

    private static SolidColorBrush Brush(double opacity, byte red, byte green, byte blue) =>
        new(Color.FromArgb((byte)Math.Round(opacity * 255), red, green, blue));

    private void ApplyExtendedStyle()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64();
        style |= NativeMethods.WsExToolWindow;
        if (_clickThrough) style |= NativeMethods.WsExTransparent | NativeMethods.WsExNoActivate;
        else style &= ~(NativeMethods.WsExTransparent | NativeMethods.WsExNoActivate);
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

public sealed record OverlayLine(Guid Id, string Original, string Translation, string SkipReason = "");
