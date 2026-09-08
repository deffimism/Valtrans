using Rectangle = System.Drawing.Rectangle;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Valtrans.Interop;

namespace Valtrans;

/// <summary>A click-through outline outside the physical-pixel OCR capture rectangle.</summary>
public sealed class OcrRegionPreviewWindow : Window
{
    private const int OutlinePixels = 3;
    private readonly Border _outline = new()
    {
        BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 211, 238)),
        Background = Brushes.Transparent,
        IsHitTestVisible = false
    };
    private Rectangle _region;
    public bool CaptureExcluded { get; private set; }

    public OcrRegionPreviewWindow()
    {
        Title = "Valtrans OCR 영역 표시";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        Content = _outline;
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64();
            NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlExStyle,
                new IntPtr(style | NativeMethods.WsExTransparent | NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow));
            // https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity
            CaptureExcluded = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) &&
                NativeMethods.SetWindowDisplayAffinity(handle, 0x11);
            HwndSource.FromHwnd(handle)?.AddHook(WindowHook);
            ApplyBounds();
        };
    }

    public void ShowRegion(Rectangle region)
    {
        if (region.Width < 20 || region.Height < 20) { Hide(); return; }
        _region = region;
        if (!IsVisible) Show();
        ApplyBounds();
    }

    public void SetOutlineColor(System.Windows.Media.Color color) => _outline.BorderBrush = new SolidColorBrush(color);

    internal static Rectangle OutlineBounds(Rectangle region) => Rectangle.FromLTRB(
        region.Left - OutlinePixels, region.Top - OutlinePixels,
        region.Right + OutlinePixels, region.Bottom + OutlinePixels);

    private void ApplyBounds()
    {
        if (_region.Width < 20 || _region.Height < 20) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var bounds = OutlineBounds(_region);
        // Set native bounds in physical pixels, not the main window's DPI-scaled DIPs.
        NativeMethods.SetWindowPos(handle, new IntPtr(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0010);
        var dpi = VisualTreeHelper.GetDpi(this);
        _outline.BorderThickness = new Thickness(OutlinePixels / dpi.DpiScaleX, OutlinePixels / dpi.DpiScaleY,
            OutlinePixels / dpi.DpiScaleX, OutlinePixels / dpi.DpiScaleY);
    }

    private IntPtr WindowHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x02E0) // WM_DPICHANGED: reapply physical bounds after WPF's DPI transition.
            Dispatcher.BeginInvoke(new Action(ApplyBounds));
        return IntPtr.Zero;
    }
}
