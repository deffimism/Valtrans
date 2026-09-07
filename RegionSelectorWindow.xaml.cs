using System.Drawing;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;

namespace Valtrans;

public partial class RegionSelectorWindow : System.Windows.Window
{
    private System.Windows.Point _start;
    private bool _dragging;
    public Rectangle? SelectedRegion { get; private set; }

    public RegionSelectorWindow()
    {
        InitializeComponent();
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        Loaded += (_, _) => Activate();
    }

    private void Window_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(RootCanvas);
        _dragging = true;
        CaptureMouse();
        Selection.Visibility = Visibility.Visible;
        Canvas.SetLeft(Selection, _start.X);
        Canvas.SetTop(Selection, _start.Y);
        Selection.Width = Selection.Height = 0;
    }

    private void Window_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var point = e.GetPosition(RootCanvas);
        var left = Math.Min(point.X, _start.X);
        var top = Math.Min(point.Y, _start.Y);
        Canvas.SetLeft(Selection, left);
        Canvas.SetTop(Selection, top);
        Selection.Width = Math.Abs(point.X - _start.X);
        Selection.Height = Math.Abs(point.Y - _start.Y);
    }

    private void Window_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        var end = e.GetPosition(RootCanvas);
        var topLeft = PointToScreen(new System.Windows.Point(Math.Min(_start.X, end.X), Math.Min(_start.Y, end.Y)));
        var bottomRight = PointToScreen(new System.Windows.Point(Math.Max(_start.X, end.X), Math.Max(_start.Y, end.Y)));
        var rect = Rectangle.FromLTRB(
            (int)Math.Round(topLeft.X), (int)Math.Round(topLeft.Y),
            (int)Math.Round(bottomRight.X), (int)Math.Round(bottomRight.Y));
        if (rect.Width < 20 || rect.Height < 20) return;
        SelectedRegion = rect;
        DialogResult = true;
    }

    private void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) DialogResult = false;
    }
}
