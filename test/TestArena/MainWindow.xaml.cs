using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.TextFormatting;
using System.Windows.Threading;
using Valtrans.TestArena.Models;
using Valtrans.TestArena.Services;

namespace Valtrans.TestArena;

public partial class MainWindow : Window
{
    public const string WindowTitle = "Valtrans Test Arena";
    private readonly List<ChatLineView> _lines = new();
    private readonly DispatcherTimer _clock = new();
    private ArenaScenario? _scenario;
    private DeterministicRandom? _random;
    private int _elapsedMs;
    private int _nextEventIndex;
    private int _lineCounter;
    private bool _autoExit;
    private string? _readyFilePath;
    private int _seed;
    private bool _readyWritten;
    private string? _startSignalPath;
    private string? _injectFilePath;
    private string? _hookManifestPath;
    private bool _scenarioStarted;
    private double _backgroundPhase;
    private double _dpiScale = 1.0;

    public MainWindow()
    {
        InitializeComponent();
        Title = WindowTitle;
        _clock.Interval = TimeSpan.FromMilliseconds(50);
        _clock.Tick += Clock_OnTick;
        ContentRendered += (_, _) => WriteReadyFileIfNeeded();
        Closing += (_, _) => ArenaLayoutStore.Save(Left, Top, Width, Height);
    }

    public void Configure(ArenaLaunchOptions options)
    {
        _autoExit = options.AutoExit;
        _readyFilePath = options.ReadyFilePath;
        _startSignalPath = options.StartSignalPath;
        _injectFilePath = options.InjectFilePath;
        _hookManifestPath = options.HookManifestPath;
        _seed = options.Seed;
        _scenario = ScenarioLoader.Load(options.ScenarioPath);
        _random = new DeterministicRandom(options.Seed);
        Title = WindowTitle;
        Topmost = options.Topmost;
        ApplyWindowResolution(_scenario);
        if (!string.IsNullOrWhiteSpace(options.ReadyFilePath))
        {
            ChatPanel.MaxHeight = double.PositiveInfinity;
            ChatPanel.MinHeight = 280;
            var scroll = _scenario.Animation.ChatScroll || _scenario.Chat.ScrollEnabled;
            ChatScrollViewer.VerticalScrollBarVisibility = scroll ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
            StatusBar.Visibility = Visibility.Collapsed;
        }
        ApplyScenarioLayout(_scenario);
        ApplyBackgroundSettings(_scenario);
        if (_hookManifestPath is not null)
            ArenaHookService.WriteManifest(_hookManifestPath,
                ArenaHookService.CreateManifest(_scenario, _injectFilePath));
        StatusText.Text = string.IsNullOrWhiteSpace(options.ReadyFilePath)
            ? "위치를 맞춘 뒤 창을 닫으면 이 위치가 저장됩니다."
            : $"scenario={_scenario.Scenario} seed={options.Seed} events={_scenario.Events.Count}";
        _elapsedMs = 0;
        _nextEventIndex = 0;
        _lineCounter = 0;
        _clock.Start();
        if (options.Topmost)
            Activate();
    }

    public void InjectMessage(string speaker, string text, string channel = "TEAM")
    {
        AddChatLine(channel, speaker, text);
        StatusText.Text = $"injected · {speaker}: {text}";
        WriteReadyFileIfNeeded();
    }

    private void ApplyWindowResolution(ArenaScenario scenario)
    {
        var (width, height) = ArenaResolutionHelper.Parse(scenario.Resolution);
        var (windowWidth, windowHeight) = ArenaResolutionHelper.FitToScreen(width, height);
        Width = windowWidth;
        Height = windowHeight;
        _dpiScale = ArenaResolutionHelper.DpiScale(scenario.Dpi);
        ChatPanel.Width = Math.Clamp(420 * _dpiScale, 320, 560);
        WindowStartupLocation = WindowStartupLocation.Manual;
        var saved = ArenaLayoutStore.TryLoad();
        if (saved is { Width: > 200, Height: > 120 })
        {
            Left = saved.Left;
            Top = saved.Top;
            Width = saved.Width;
            Height = saved.Height;
            return;
        }
        var work = SystemParameters.WorkArea;
        Left = work.Left + 16;
        Top = work.Top + work.Height - Height - 16;
    }

    private void ApplyScenarioLayout(ArenaScenario scenario)
    {
        var opacity = scenario.Chat.Opacity;
        if (_random is not null && scenario.Fuzz.FontSizeJitter > 0)
        {
            var jitter = scenario.Fuzz.FontSizeJitter;
            opacity = Math.Clamp(opacity + (_random.NextDouble() * 2 - 1) * jitter,
                scenario.Fuzz.OpacityMin, scenario.Fuzz.OpacityMax);
        }
        ChatPanel.Opacity = Math.Clamp(opacity, 0.4, 1);
    }

    private void ApplyBackgroundSettings(ArenaScenario scenario)
    {
        BackgroundLayer.Fill = ArenaBackgroundAnimator.CreateBrush(scenario.Background, 0, _random);
        BackgroundLayer.Opacity = ArenaBackgroundAnimator.Opacity(scenario.Background, 0);
        MotionLayer.Visibility = scenario.Background.MotionSpeed > 0 || scenario.Animation.BackgroundMotion
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void Clock_OnTick(object? sender, EventArgs e)
    {
        if (_scenario is null) return;
        if (!string.IsNullOrWhiteSpace(_startSignalPath) && !File.Exists(_startSignalPath))
            return;
        if (!_scenarioStarted)
        {
            _scenarioStarted = true;
            _elapsedMs = 0;
            _nextEventIndex = 0;
            StatusText.Text = $"scenario started · {_scenario.Scenario}";
        }
        _elapsedMs += (int)_clock.Interval.TotalMilliseconds;
        AnimateBackground();
        if (ArenaHookService.TryReadInject(_injectFilePath, out var inject))
            AddChatLine(inject.Channel, inject.Speaker, inject.Text);
        while (_nextEventIndex < _scenario.Events.Count &&
               _scenario.Events[_nextEventIndex].AtMs <= _elapsedMs)
        {
            var eventItem = _scenario.Events[_nextEventIndex++];
            AddChatLine(eventItem.Channel, eventItem.Speaker, eventItem.Text);
            if (string.IsNullOrWhiteSpace(_readyFilePath))
                StatusText.Text = $"t={_elapsedMs}ms · {eventItem.Speaker}: {eventItem.Text}";
            WriteReadyFileIfNeeded();
        }

        if (_nextEventIndex >= _scenario.Events.Count && _elapsedMs > LastEventAt(_scenario) + 1500)
        {
            _clock.Stop();
            if (_autoExit)
            {
                var reportPath = Path.Combine(Path.GetTempPath(), $"valtrans-arena-{_scenario.Scenario}.json");
                File.WriteAllText(reportPath, JsonSerializer.Serialize(new
                {
                    scenario = _scenario.Scenario,
                    seed = _seed,
                    renderedLines = _lines.Select(line => line.DisplayText).ToArray(),
                    expectations = _scenario.Expectations
                }, new JsonSerializerOptions { WriteIndented = true }));
                StatusText.Text = $"complete · report={reportPath}";
                Close();
            }
            else
            {
                StatusText.Text = "scenario complete · ready for capture";
            }
        }
    }

    private void AnimateBackground()
    {
        if (_scenario is null) return;
        var motion = _scenario.Background.MotionSpeed > 0 || _scenario.Animation.BackgroundMotion;
        if (!motion) return;
        var speed = _scenario.Background.MotionSpeed > 0 ? _scenario.Background.MotionSpeed : 0.6;
        _backgroundPhase += speed * _clock.Interval.TotalMilliseconds / 1000.0;
        BackgroundLayer.Fill = ArenaBackgroundAnimator.CreateBrush(_scenario.Background, _backgroundPhase, _random);
        BackgroundLayer.Opacity = ArenaBackgroundAnimator.Opacity(_scenario.Background, _backgroundPhase);
        MotionLayer.Fill = new SolidColorBrush(Color.FromArgb(
            (byte)(40 + 30 * Math.Sin(_backgroundPhase * 1.7)),
            30, 60, 110));
    }

    private void WriteReadyFileIfNeeded()
    {
        if (_readyWritten || string.IsNullOrWhiteSpace(_readyFilePath) || _scenario is null) return;
        ChatPanel.UpdateLayout();
        if (ChatPanel.ActualWidth < 10 || ChatPanel.ActualHeight < 10) return;

        var windowTopLeft = PointToScreen(new Point(0, 0));
        var chatTopLeft = ChatPanel.PointToScreen(new Point(0, 0));
        var chatSize = new Point(ChatPanel.ActualWidth, ChatPanel.ActualHeight);
        var payload = new
        {
            windowTitle = WindowTitle,
            windowBounds = new
            {
                x = (int)windowTopLeft.X,
                y = (int)windowTopLeft.Y,
                width = (int)ActualWidth,
                height = (int)ActualHeight
            },
            chatRegion = new
            {
                x = (int)chatTopLeft.X,
                y = (int)chatTopLeft.Y,
                width = Math.Max(20, (int)chatSize.X),
                height = Math.Max(20, (int)chatSize.Y)
            },
            scenario = _scenario.Scenario,
            resolution = _scenario.Resolution,
            dpi = _scenario.Dpi,
            seed = _seed,
            tags = _scenario.Tags,
            hooks = _hookManifestPath,
            background = _scenario.Background,
            fuzz = _scenario.Fuzz,
            animation = _scenario.Animation,
            readyUtc = DateTime.UtcNow
        };
        Directory.CreateDirectory(Path.GetDirectoryName(_readyFilePath)!);
        File.WriteAllText(_readyFilePath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        _readyWritten = true;
    }

    private static int LastEventAt(ArenaScenario scenario) =>
        scenario.Events.Count == 0 ? 0 : scenario.Events.Max(eventItem => eventItem.AtMs);

    private void AddChatLine(string channel, string speaker, string text)
    {
        if (_scenario is null || _random is null) return;
        var display = $"[{channel}] {speaker}: {text}";
        var spacing = _scenario.Chat.LineSpacing;
        var fuzzed = ArenaVisualFuzzer.FuzzLine(_scenario.Chat, _scenario.Fuzz, _random, _lineCounter++);
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, spacing), Opacity = 0 };
        var header = new TextBlock
        {
            Text = $"[{channel}] {speaker}:",
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13 * _dpiScale
        };
        stack.Children.Add(header);
        var cjk = ContainsCjk(text);
        var bodyFontSize = (fuzzed.FontSize + (cjk ? 6 : 2)) * _dpiScale;
        var body = new TextBlock
        {
            Text = text,
            Foreground = Brushes.White,
            FontFamily = ResolveChatFont(text),
            FontSize = bodyFontSize,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0),
            Opacity = fuzzed.Opacity
        };
        if (fuzzed.BlurRadius > 0.1)
            body.Effect = new BlurEffect { Radius = fuzzed.BlurRadius };
        TextOptions.SetTextFormattingMode(body, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(body, TextRenderingMode.ClearType);
        stack.Children.Add(body);
        ChatLinesPanel.Children.Add(stack);
        _lines.Add(new ChatLineView(channel, speaker, text, display));
        var maxLines = Math.Clamp(_scenario.Chat.MaxVisibleLines, 4, 24);
        while (ChatLinesPanel.Children.Count > maxLines)
            ChatLinesPanel.Children.RemoveAt(0);
        var fadeMs = _scenario.Chat.FadeInMs;
        if ((_scenario.Animation.ChatFadeIn || fadeMs > 0) && fadeMs <= 0) fadeMs = 180;
        if (fadeMs > 0)
        {
            var animation = new DoubleAnimation(0, fuzzed.Opacity, TimeSpan.FromMilliseconds(fadeMs));
            stack.BeginAnimation(OpacityProperty, animation);
        }
        else
        {
            stack.Opacity = fuzzed.Opacity;
        }
        if (_scenario.Animation.ChatScroll || _scenario.Chat.ScrollEnabled)
            ChatScrollViewer.ScrollToEnd();
    }

    private static bool ContainsCjk(string text) =>
        Regex.IsMatch(text, @"[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}]");

    private static FontFamily ResolveChatFont(string text)
    {
        if (Regex.IsMatch(text, @"[\p{IsHiragana}\p{IsKatakana}]"))
            return new FontFamily("Yu Gothic UI");
        if (Regex.IsMatch(text, @"[\p{IsCJKUnifiedIdeographs}]"))
            return new FontFamily("Microsoft YaHei UI, Microsoft YaHei, SimHei");
        return new FontFamily("Segoe UI");
    }

    private sealed record ChatLineView(string Channel, string Speaker, string Text, string DisplayText);
}

public sealed record ArenaLaunchOptions(string ScenarioPath, int Seed, bool AutoExit, string? ReadyFilePath,
    string? StartSignalPath, string? InjectFilePath, string? HookManifestPath, bool Topmost = false);
