using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Valtrans.Models;
using Valtrans.Services;

namespace Valtrans;

public partial class OcrCalibrationWindow : Window
{
    private readonly WindowsOcrService _ocr = new();
    private readonly IReadOnlyCollection<string> _languages;
    private readonly System.Drawing.Rectangle _referenceBounds;
    private readonly CaptureRegion _recommendedRegion;
    private bool _refreshing;

    public OcrCalibrationWindow(string game, CaptureRegion initialRegion, CaptureRegion recommendedRegion,
        System.Drawing.Rectangle referenceBounds, IReadOnlyCollection<string> languages)
    {
        InitializeComponent();
        Game = game;
        SelectedRegion = initialRegion.Clone();
        _recommendedRegion = recommendedRegion.Clone();
        _referenceBounds = referenceBounds;
        _languages = languages.ToArray();
        UpdateRegionInfo();
    }

    public string Game { get; }
    public CaptureRegion SelectedRegion { get; private set; }

    private async void Window_OnLoaded(object sender, RoutedEventArgs e) => await RefreshPreviewAsync();
    private async void RefreshPreview_OnClick(object sender, RoutedEventArgs e) => await RefreshPreviewAsync();

    private async void AdjustRegion_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string action } || _refreshing) return;
        var step = Math.Max(8, (int)Math.Round(_referenceBounds.Height * 0.008));
        var region = SelectedRegion.Clone();
        switch (action)
        {
            case "Left": region.X -= step; break;
            case "Right": region.X += step; break;
            case "Up": region.Y -= step; break;
            case "Down": region.Y += step; break;
            case "Grow":
                region.X -= step; region.Y -= step; region.Width += step * 2; region.Height += step * 2; break;
            case "Shrink":
                region.X += step; region.Y += step; region.Width -= step * 2; region.Height -= step * 2; break;
        }
        SelectedRegion = Clamp(region);
        await RefreshPreviewAsync();
    }

    private async void ResetRegion_OnClick(object sender, RoutedEventArgs e)
    {
        if (_refreshing) return;
        SelectedRegion = _recommendedRegion.Clone();
        await RefreshPreviewAsync();
    }

    private async Task RefreshPreviewAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        RefreshPreviewButton.IsEnabled = false;
        PreviewStatusText.Text = "게임 화면에서 캡처·인식 중…";
        UpdateRegionInfo();
        try
        {
            Opacity = 0;
            await Task.Delay(100);
            var rectangle = SelectedRegion.ToRectangle();
            var pngTask = _ocr.CapturePngAsync(rectangle);
            var ocrTask = _ocr.ReadDetailedAsync(rectangle, _languages);
            await Task.WhenAll(pngTask, ocrTask);
            Opacity = 1;
            Activate();

            var png = await pngTask;
            if (png.Length > 0)
            {
                using var stream = new MemoryStream(png);
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                PreviewImage.Source = image;
            }

            var result = await ocrTask;
            RecognizedTextBox.Text = string.IsNullOrWhiteSpace(result.Text)
                ? "인식된 글자가 없습니다. 영역을 이동하거나 확대해 보세요."
                : result.Text.Trim();
            PreviewStatusText.Text = $"감지 {result.DetectedLanguage} · {result.PositionedLines?.Count ?? 0}줄";
        }
        catch (Exception ex)
        {
            Opacity = 1;
            RecognizedTextBox.Text = ex.Message;
            PreviewStatusText.Text = "캡처 또는 OCR 실패";
        }
        finally
        {
            Opacity = 1;
            _refreshing = false;
            RefreshPreviewButton.IsEnabled = true;
        }
    }

    private CaptureRegion Clamp(CaptureRegion region)
    {
        region.Width = Math.Clamp(region.Width, 120, _referenceBounds.Width);
        region.Height = Math.Clamp(region.Height, 80, _referenceBounds.Height);
        region.X = Math.Clamp(region.X, _referenceBounds.Left, _referenceBounds.Right - region.Width);
        region.Y = Math.Clamp(region.Y, _referenceBounds.Top, _referenceBounds.Bottom - region.Height);
        return region;
    }

    private void UpdateRegionInfo() => RegionInfoText.Text =
        $"{Game} · 기준 {_referenceBounds.Width}×{_referenceBounds.Height} · " +
        $"영역 {SelectedRegion.Width}×{SelectedRegion.Height} ({SelectedRegion.X}, {SelectedRegion.Y})";

    private void UseRegion_OnClick(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
