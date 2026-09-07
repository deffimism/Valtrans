using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Valtrans.Interop;
using Valtrans.Models;
using Valtrans.Services;

namespace Valtrans;

public partial class MainWindow : System.Windows.Window
{
    private readonly SettingsService _settingsService = new();
    private readonly GlossaryService _glossary = new();
    private readonly GameChatFilterService _chatFilter;
    private readonly KeyboardReplacementService _keyboard = new();
    private readonly WindowsOcrService _ocr = new();
    private readonly LanguagePackService _languagePacks = new();
    private readonly LocalAiService _localAi = new();
    private readonly ValtransLiteService _lite = new();
    private readonly DlxDockerService _dlxDocker = new();
    private readonly TranslatorService _translator;
    private readonly TranslationRegressionService _regressionTests;
    private readonly DiagnosticLogService _diagnosticLog = new();
    private AppSettings _settings;
    private GlobalHotkeyService? _hotkey;
    private DispatcherTimer? _gameHotkeyTimer;
    private DispatcherTimer? _liteMemoryTimer;
    private DateTime _nextHotkeyRegisterAttempt;
    private bool _wasSupportedGameForeground;
    private bool _liteMaintenanceBusy;
    private OverlayWindow? _overlay;
    private CancellationTokenSource? _ocrCancellation;
    private CancellationTokenSource? _translationQueueCancellation;
    private readonly object _translationQueueLock = new();
    private readonly List<TranslationQueueItem> _translationQueue = new();
    private bool _translationWorkerRunning;
    private bool _sendBusy;
    private bool _translationTestBusy;
    private string _dashboardPage = "Overview";
    private bool _engineCompatibilityBusy;
    private string _lastHybridRoute = "";
    private bool _capturingHotkey;
    private bool _dlxAutoStarting;
    private readonly SemaphoreSlim _localAiWarmLock = new(1, 1);
    private string _hotkeyBeforeCapture = "\\";
    private string _lastOcrText = "";
    private ulong? _lastOcrFrameHash;
    private int _unchangedOcrFrames;
    private int _ocrConsensusRejectedFrames;
    private DateTime _lastOcrEnhanceLogUtc;
    private int _ocrEnhancementEvaluationCounter;
    private double _averageCaptureMs;
    private double _averageRecognitionMs;
    private double _averageConsensusMs;
    private double _averageTranslationMs;
    private double _averageOcrPipelineMs;
    private string _adaptiveOcrMode = "Balanced";
    private int _ocrQualityDropFrames;
    private bool _ocrBaselinePending = true;
    private HashSet<string> _previousOcrLines = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _recentOcrBodies = new(StringComparer.OrdinalIgnoreCase);
    private string _activeRegionGame = "Auto";
    private bool _updatingMapOptions;
    private bool _guideRefreshBusy;
    private bool _recommendedSetupBusy;
    private bool _diagnosticsBusy;
    private bool _autoSwitchingGameProfile;
    private string _lastForegroundProfileGame = "";
    private string _lastForegroundProfileSignature = "";
    private string _loadedRegionProfileKey = "";
    private IReadOnlyList<string> _lastDiagnosticLines = Array.Empty<string>();

    public MainWindow()
    {
        InitializeComponent();
        _settings = _settingsService.Load();
        _chatFilter = new GameChatFilterService(_glossary);
        _translator = new TranslatorService(_glossary, _localAi, _lite);
        _regressionTests = new TranslationRegressionService(_glossary);
        _translator.LocalFallbackActivated += Translator_OnLocalFallbackActivated;
        _translator.HybridRouteSelected += Translator_OnHybridRouteSelected;
        _translator.TranslationSafetyAdjusted += Translator_OnTranslationSafetyAdjusted;
        ApplySettingsToUi();
        ShowDashboardPage(_settings.ShowStartupGuide ? "Guide" : "Overview");
        OverlayTransparencySlider.ValueChanged += OverlayTransparency_OnValueChanged;
        OverlayBorderTransparencySlider.ValueChanged += OverlayBorderTransparency_OnValueChanged;
        OverlayFontSizeSlider.ValueChanged += OverlayFontSize_OnValueChanged;
    }

    private async void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _hotkey = new GlobalHotkeyService(this);
            _hotkey.Pressed += Hotkey_OnPressed;
            _gameHotkeyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _gameHotkeyTimer.Tick += (_, _) => UpdateGameHotkeyRegistration();
            _gameHotkeyTimer.Start();
            _liteMemoryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _liteMemoryTimer.Tick += LiteMemoryTimer_OnTick;
            _liteMemoryTimer.Start();
            UpdateGameHotkeyRegistration();
            EnsureOverlay();
            RefreshLanguagePackStatus();
            SetStatus("준비됨", $"지원 게임이 활성화되면 {_settings.Hotkey} 단축키가 켜집니다.");
            if ((_settings.TranslationProvider is "Ollama" or "Hybrid") ||
                (_settings.TranslationProvider == "DeepLX" && _settings.DeepLxAutoFallback))
                await WarmUpLocalAiAsync(showGlobalStatus: false);
            if (_settings.TranslationProvider is "Lite" or "Hybrid")
                RefreshLiteStatus();
            await RefreshQuickStartGuideAsync();
        }
        catch (Exception ex)
        {
            SetStatus("확인 필요", ex.Message, true);
        }
    }

    private async void ShowQuickStartGuide_OnClick(object sender, RoutedEventArgs e)
    {
        ShowDashboardPage("Guide");
        _settings.ShowStartupGuide = true;
        QuickStartPanel.Visibility = Visibility.Visible;
        _settingsService.Save(_settings);
        MainScrollViewer.ScrollToTop();
        await RefreshQuickStartGuideAsync();
    }

    private void HideQuickStartGuide_OnClick(object sender, RoutedEventArgs e)
    {
        _settings.ShowStartupGuide = false;
        QuickStartPanel.Visibility = Visibility.Collapsed;
        ShowDashboardPage("Overview");
        _settingsService.Save(_settings);
        SetStatus("가이드 접힘", "상단의 ‘사용 가이드’를 누르면 언제든 다시 볼 수 있습니다.");
    }

    private async void RefreshGuide_OnClick(object sender, RoutedEventArgs e) =>
        await RefreshQuickStartGuideAsync();

    private async void RunDiagnostics_OnClick(object sender, RoutedEventArgs e) =>
        await RunSystemDiagnosticsAsync(showPanel: true);

    private async Task<SystemDiagnosticReport> RunSystemDiagnosticsAsync(bool showPanel)
    {
        if (_diagnosticsBusy) return new SystemDiagnosticReport(0, 0, ["점검이 이미 실행 중입니다."]);
        _diagnosticsBusy = true;
        RefreshGuideButton.IsEnabled = false;
        AutoRepairButton.IsEnabled = false;
        if (showPanel)
        {
            ShowDashboardPage("Guide");
            _settings.ShowStartupGuide = true;
            QuickStartPanel.Visibility = Visibility.Visible;
            DiagnosticsPanel.Visibility = Visibility.Visible;
            DiagnosticsSummaryText.Text = "번역 엔진·단축키·언어팩·OCR 프로필을 확인하는 중…";
            _settingsService.Save(_settings);
            MainScrollViewer.ScrollToTop();
        }

        var lines = new List<string>();
        var blocking = 0;
        var repairable = 0;
        try
        {
            ReadUiIntoSettings();
            if (GlobalHotkeyService.TryValidate(_settings.Hotkey, out var hotkeyError))
                lines.Add($"✓ 단축키 {_settings.Hotkey} 형식 정상");
            else
            {
                lines.Add($"✕ 단축키 확인 필요 · {hotkeyError}");
                blocking++;
                repairable++;
            }

            var languageStates = _languagePacks.GetSupportedLanguageStatus();
            var missingLanguages = _settings.OcrLanguages.Where(code => !languageStates.GetValueOrDefault(code)).ToArray();
            if (missingLanguages.Length == 0)
                lines.Add($"✓ Windows OCR 언어팩 · {string.Join("/", _settings.OcrLanguages)}");
            else
            {
                lines.Add($"✕ OCR 언어팩 설치 필요 · {string.Join("/", missingLanguages)}");
                blocking++;
                repairable++;
            }

            if (_settings.CaptureRegion.IsValid)
            {
                var profile = ValidateCurrentOcrProfile();
                switch (profile.Validity)
                {
                    case OcrProfileValidity.Valid:
                        lines.Add($"✓ {_activeRegionGame} OCR 프로필 · {profile.Message}");
                        break;
                    case OcrProfileValidity.NotVerifiable:
                        lines.Add($"△ OCR 프로필 검증 보류 · {profile.Message}");
                        break;
                    default:
                        lines.Add($"✕ OCR 프로필 확인 필요 · {profile.Message}");
                        blocking++;
                        repairable++;
                        break;
                }
            }
            else
            {
                lines.Add("✕ OCR 영역 없음 · 게임 실행 후 추천 영역 필요");
                blocking++;
                repairable++;
            }

            var liteStatus = _lite.GetStatus();
            var localStatus = await _localAi.GetStatusAsync(_settings.LocalAiModel);
            switch (_settings.TranslationProvider)
            {
                case "Hybrid":
                    if (liteStatus.Ready && localStatus.Ready)
                        lines.Add("✓ 스마트 복합 · Lite + 로컬 AI 준비됨");
                    else if (liteStatus.Ready || localStatus.Ready)
                    {
                        lines.Add($"△ 스마트 복합 일부 준비 · {(liteStatus.Ready ? "Lite" : LocalAiService.GetModel(_settings.LocalAiModel).DisplayName)} 사용 가능");
                        repairable++;
                    }
                    else
                    {
                        lines.Add("✕ 스마트 복합 엔진 미설치 · Lite와 로컬 AI 준비 필요");
                        blocking++;
                        repairable++;
                    }
                    break;
                case "Lite":
                    if (liteStatus.Ready) lines.Add("✓ Valtrans Lite 준비됨");
                    else { lines.Add("✕ Valtrans Lite 설치 필요"); blocking++; repairable++; }
                    break;
                case "Ollama":
                    if (localStatus.Ready) lines.Add($"✓ {LocalAiService.GetModel(_settings.LocalAiModel).DisplayName} 준비됨");
                    else { lines.Add("✕ 선택한 로컬 AI 설치 필요"); blocking++; repairable++; }
                    break;
                case "OpenAI":
                    if (!string.IsNullOrWhiteSpace(_settings.ApiKey)) lines.Add("✓ OpenAI API 키 저장됨");
                    else { lines.Add("✕ OpenAI API 키 입력 필요 · 자동 복구 불가"); blocking++; }
                    break;
                case "DeepL":
                    if (!string.IsNullOrWhiteSpace(_settings.DeepLApiKey)) lines.Add("✓ DeepL API 키 입력됨 · 무료 로컬 엔진으로 전환 가능");
                    else { lines.Add("✕ DeepL API 키 필요 · 무료 사용은 로컬 엔진 선택"); blocking++; }
                    break;
                case "DeepLX" when _settings.DeepLxMode == "Docker":
                    lines.Add("△ 개인 DLX · Docker 카드에서 상태 확인 권장");
                    break;
                default:
                    lines.Add(Uri.TryCreate(_settings.DeepLxUrl, UriKind.Absolute, out _)
                        ? "✓ DLX 주소 형식 정상"
                        : "✕ DLX 주소 확인 필요 · 자동 복구 불가");
                    if (!Uri.TryCreate(_settings.DeepLxUrl, UriKind.Absolute, out _)) blocking++;
                    break;
            }

            lines.Add(_settings.AutoSwitchGameProfile
                ? $"✓ 게임 프로필 자동 전환 켜짐 · 저장 프로필 {_settings.CaptureRegionsByGame.Count}개"
                : "△ 게임 프로필 자동 전환 꺼짐");
            lines.Add(_settings.OcrAutoEnhance && _settings.OcrTwoFrameConsensus
                ? "✓ OCR 품질 보호 · 자동 확대·대비 보정 + 두 프레임 합의"
                : "△ OCR 품질 보호 일부 꺼짐 · 오버레이 설정에서 권장 옵션 확인");

            var regression = _regressionTests.Run(_settings);
            if (regression.Success)
                lines.Add($"✓ 핵심 번역 규칙 · {regression.Passed}/{regression.Total} 통과");
            else
            {
                var failed = string.Join(", ", regression.Results.Where(result => !result.Passed).Select(result => result.Name));
                lines.Add($"✕ 핵심 번역 규칙 회귀 · {regression.Passed}/{regression.Total} 통과 · {failed}");
                blocking++;
            }
        }
        catch (Exception ex)
        {
            lines.Add($"✕ 점검 중 오류 · {FriendlyMessage(ex)}");
            blocking++;
        }
        finally
        {
            _diagnosticsBusy = false;
        }

        _lastDiagnosticLines = lines.ToArray();
        DiagnosticsSummaryText.Text = string.Join(Environment.NewLine, lines);
        DiagnosticsPanel.Visibility = Visibility.Visible;
        AutoRepairButton.IsEnabled = repairable > 0 && !_recommendedSetupBusy;
        RefreshGuideButton.IsEnabled = !_recommendedSetupBusy;
        SetStatus(blocking == 0 ? "전체 점검 완료" : $"점검 완료 · {blocking}개 확인 필요",
            blocking == 0 ? "번역과 OCR 사용 준비가 끝났습니다." : "‘자동 복구’로 가능한 항목을 처리하거나 표시된 안내를 확인하세요.", blocking > 0);
        _diagnosticLog.Record("system_diagnostics", blocking == 0 ? "passed" : "attention",
            new Dictionary<string, object?> { ["count"] = blocking, ["provider"] = _settings.TranslationProvider });
        await RefreshQuickStartGuideAsync();
        return new SystemDiagnosticReport(blocking, repairable, lines);
    }

    private void RegressionTest_OnClick(object sender, RoutedEventArgs e)
    {
        ReadUiIntoSettings();
        var report = _regressionTests.Run(_settings);
        var failed = report.Results.Where(result => !result.Passed).ToArray();
        RegressionStatusText.Text = report.Success
            ? $"정상 · {report.Passed}/{report.Total} 통과 · 방향·인원·부정·불확실성·FPS 약어 규칙이 유지됩니다."
            : $"확인 필요 · {report.Passed}/{report.Total} 통과 · {string.Join(", ", failed.Select(result => result.Name))}";
        RegressionStatusText.Foreground = new SolidColorBrush(report.Success
            ? Color.FromRgb(4, 120, 87)
            : Color.FromRgb(180, 35, 58));
        SetStatus(report.Success ? "품질 자가 테스트 통과" : "번역 규칙 확인 필요", RegressionStatusText.Text, !report.Success);
        _diagnosticLog.Record("translation_regression", report.Success ? "passed" : "failed",
            new Dictionary<string, object?> { ["count"] = report.Passed, ["result"] = $"{report.Passed}of{report.Total}" });
    }

    private async void EngineCompatibility_OnClick(object sender, RoutedEventArgs e)
    {
        if (_engineCompatibilityBusy) return;
        ReadUiIntoSettings();
        if (!await EnsureTranslationProviderReadyAsync()) return;

        _engineCompatibilityBusy = true;
        EngineCompatibilityButton.IsEnabled = false;
        RegressionTestButton.IsEnabled = false;
        RegressionStatusText.Foreground = new SolidColorBrush(Color.FromRgb(79, 70, 229));
        RegressionStatusText.Text = "실제 선택 엔진으로 방향·인원·부정 보존을 확인하는 중…";
        SetStatus("엔진 호환성 검사 중", "짧은 테스트 문장 2개를 실제 번역 엔진에 보냅니다.");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var report = await _translator.TestCompatibilityAsync(_settings, timeout.Token);
            var failedEngines = report.Results.Where(result => !result.Success)
                .Select(result => result.Engine).Distinct().ToArray();
            var summary = report.Success
                ? report.Adjusted == 0
                    ? $"정상 · {report.Passed}/{report.Total} · 안전 보정 없이 사실 보존 · 평균 {report.AverageDurationMs:N0}ms"
                    : $"사용 가능 · {report.Passed}/{report.Total} · {report.Adjusted}건은 안전 보정됨 · 평균 {report.AverageDurationMs:N0}ms"
                : $"확인 필요 · {report.Passed}/{report.Total} 성공 · {string.Join(", ", failedEngines)}";
            RegressionStatusText.Text = summary;
            RegressionStatusText.Foreground = new SolidColorBrush(report.Success
                ? report.Adjusted == 0 ? Color.FromRgb(4, 120, 87) : Color.FromRgb(154, 91, 10)
                : Color.FromRgb(180, 35, 58));
            SetStatus(report.Success ? "엔진 호환성 검사 완료" : "엔진 호환성 확인 필요", summary, !report.Success);
            _diagnosticLog.Record("engine_compatibility", report.Success ? "passed" : "failed",
                new Dictionary<string, object?>
                {
                    ["provider"] = _settings.TranslationProvider,
                    ["count"] = report.Passed,
                    ["durationMs"] = Math.Round(report.AverageDurationMs),
                    ["result"] = $"{report.Passed}of{report.Total}-adjusted{report.Adjusted}"
                });
        }
        catch (OperationCanceledException)
        {
            RegressionStatusText.Text = "검사 시간 초과 · 엔진 예열 상태나 연결을 확인해 주세요.";
            RegressionStatusText.Foreground = new SolidColorBrush(Color.FromRgb(180, 35, 58));
            SetStatus("엔진 검사 시간 초과", "로컬 모델 예열 또는 서버 연결 상태를 확인해 주세요.", true);
        }
        catch (Exception ex)
        {
            RegressionStatusText.Text = $"검사 실패 · {FriendlyMessage(ex)}";
            RegressionStatusText.Foreground = new SolidColorBrush(Color.FromRgb(180, 35, 58));
            SetStatus("엔진 호환성 검사 실패", FriendlyMessage(ex), true);
        }
        finally
        {
            _engineCompatibilityBusy = false;
            EngineCompatibilityButton.IsEnabled = true;
            RegressionTestButton.IsEnabled = true;
        }
    }

    private async void ExportDiagnostics_OnClick(object sender, RoutedEventArgs e)
    {
        if (_lastDiagnosticLines.Count == 0)
            await RunSystemDiagnosticsAsync(showPanel: true);

        var dialog = new SaveFileDialog
        {
            Title = "개인정보 제외 진단 파일 저장",
            Filter = "텍스트 파일 (*.txt)|*.txt",
            FileName = $"Valtrans-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName,
                _diagnosticLog.BuildPrivacySafeReport(_settings, _lastDiagnosticLines), Encoding.UTF8);
            SetStatus("진단 파일 저장됨", "채팅·닉네임·API 키·서버 주소를 제외한 정보만 저장했습니다.");
            _diagnosticLog.Record("diagnostic_export", "saved");
        }
        catch (Exception ex)
        {
            _diagnosticLog.RecordException("diagnostic_export", ex);
            SetStatus("진단 저장 실패", FriendlyMessage(ex), true);
        }
    }

    private async void AutoRepair_OnClick(object sender, RoutedEventArgs e)
    {
        if (_recommendedSetupBusy || _diagnosticsBusy) return;
        _recommendedSetupBusy = true;
        AutoRepairButton.IsEnabled = false;
        RefreshGuideButton.IsEnabled = false;
        DiagnosticsPanel.Visibility = Visibility.Visible;
        try
        {
            ReadUiIntoSettings();
            if (!GlobalHotkeyService.TryValidate(_settings.Hotkey, out _))
            {
                _settings.Hotkey = "\\";
                HotkeyBox.Text = "\\";
                HotkeyHint.Text = "\\  →  전체 선택 · 번역 · 교체";
            }

            if (_settings.OcrLanguages.Any(code => !_languagePacks.IsInstalled(code)))
            {
                DiagnosticsSummaryText.Text = "Windows OCR 언어팩을 복구하는 중입니다. 관리자 승인 후 설치 창이 닫힐 때까지 기다려 주세요.";
                await EnsureLanguagePackAsync();
            }

            await RepairSelectedTranslationEngineAsync();

            if (!ValidateCurrentOcrProfile().CanUse)
            {
                var detected = GameWindowDetectionService.Detect("Auto");
                if (detected is { ClientBounds.Width: >= 640, ClientBounds.Height: >= 480 })
                {
                    SwitchToGameProfile(detected.Game);
                    _settings.CaptureRegion = OcrRegionRecommendationService.Recommend(detected.Game, detected.ClientBounds);
                    _loadedRegionProfileKey = RegionProfileKey(detected.Game);
                    ResetOcrEnhancementLearning();
                    StoreCurrentRegionProfile();
                    UpdateRegionText();
                    _diagnosticLog.Record("ocr_profile_repair", "recommended_region_applied",
                        new Dictionary<string, object?> { ["game"] = detected.Game, ["profile"] = _loadedRegionProfileKey });
                }
            }

            _settingsService.Save(_settings);
        }
        catch (Exception ex)
        {
            SetStatus("자동 복구 중단", FriendlyMessage(ex), true);
        }
        finally
        {
            _recommendedSetupBusy = false;
            RefreshLiteStatus();
            await RefreshLocalStatusAsync();
            await RunSystemDiagnosticsAsync(showPanel: true);
        }
    }

    private async Task RepairSelectedTranslationEngineAsync()
    {
        var provider = _settings.TranslationProvider;
        if (provider is "Hybrid" or "Lite")
        {
            var liteStatus = _lite.GetStatus();
            if (!liteStatus.Ready)
            {
                LiteInstallProgress.Visibility = Visibility.Visible;
                var progress = new Progress<LiteProgress>(update =>
                {
                    LiteInstallProgress.Value = update.Percent;
                    DiagnosticsSummaryText.Text = $"Valtrans Lite 복구 중 · {update.Message}";
                    SetStatus("자동 복구 중", update.Message);
                });
                var result = await _lite.InstallAndPrepareAsync(progress);
                if (!result.Success) throw new InvalidOperationException(result.Message);
                LiteInstallProgress.Visibility = Visibility.Collapsed;
            }
        }

        if (provider is "Hybrid" or "Ollama")
        {
            var localStatus = await _localAi.GetStatusAsync(_settings.LocalAiModel);
            if (!localStatus.Ready)
            {
                LocalInstallProgress.Visibility = Visibility.Visible;
                var progress = new Progress<LocalAiProgress>(update =>
                {
                    LocalInstallProgress.Value = update.Percent;
                    DiagnosticsSummaryText.Text = $"로컬 AI 복구 중 · {update.Message}";
                    SetStatus("자동 복구 중", update.Message);
                });
                var result = await _localAi.InstallAndPrepareAsync(_settings.LocalAiModel, progress);
                if (!result.Success) throw new InvalidOperationException(result.Message);
                LocalInstallProgress.Visibility = Visibility.Collapsed;
            }
            await WarmUpLocalAiAsync(showGlobalStatus: false);
        }
    }

    private void ToggleAdvancedMode_OnClick(object sender, RoutedEventArgs e)
    {
        ReadUiIntoSettings();
        _settings.ShowAdvancedSettings = !_settings.ShowAdvancedSettings;
        _settingsService.Save(_settings);
        ApplyAdvancedModePresentation();
        ApplyProviderPanels();
        SetStatus(_settings.ShowAdvancedSettings ? "고급 설정 표시" : "간편 설정 표시",
            _settings.ShowAdvancedSettings
                ? "엔진별 설치·오버레이·사용자 사전 설정을 모두 표시합니다."
                : "자주 쓰는 핵심 설정만 표시합니다.");
    }

    private void ApplyAdvancedModePresentation()
    {
        if (AdvancedModeButton is null) return;
        AdvancedModeButton.Content = "번역 엔진";
        AdvancedOcrPanel.Visibility = Visibility.Visible;
        SimpleOcrSummaryPanel.Visibility = Visibility.Visible;
        GlossaryExpander.Visibility = Visibility.Visible;
    }

    private void DashboardNavigation_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string page }) ShowDashboardPage(page);
    }

    private void ShowDashboardPage(string page)
    {
        if (OverviewPage is null) return;
        _dashboardPage = page;
        OverviewPage.Visibility = page == "Overview" ? Visibility.Visible : Visibility.Collapsed;
        EnginesPage.Visibility = page == "Engines" ? Visibility.Visible : Visibility.Collapsed;
        OverlayPage.Visibility = page == "Overlay" ? Visibility.Visible : Visibility.Collapsed;
        LabPage.Visibility = page == "Lab" ? Visibility.Visible : Visibility.Collapsed;
        GuidePage.Visibility = page == "Guide" ? Visibility.Visible : Visibility.Collapsed;
        OverviewNavigation.IsChecked = page == "Overview";
        AdvancedModeButton.IsChecked = page == "Engines";
        OverlayNavigation.IsChecked = page == "Overlay";
        LabNavigation.IsChecked = page == "Lab";
        GuideNavigation.IsChecked = page == "Guide";
        if (page == "Guide") QuickStartPanel.Visibility = Visibility.Visible;
        (DashboardPageTitle.Text, DashboardPageDescription.Text) = page switch
        {
            "Engines" => ("번역 엔진", "번역 방식 선택부터 로컬 모델 설치와 예열까지, 한곳에서 관리하세요."),
            "Overlay" => ("오버레이 설정", "게임 화면을 가리지 않도록, 나에게 맞는 표시 방식을 찾아보세요."),
            "Lab" => ("번역 테스트 · 사전", "게임을 켜기 전에 표현을 확인하고, 팀만의 용어를 추가하세요."),
            "Guide" => ("시작 가이드 · 점검", "처음 준비부터 문제 해결까지, 필요한 단계를 차근차근 안내합니다."),
            _ => ("채팅 대시보드", "보내는 말도, 팀원의 콜도. 익숙한 언어로 플레이하세요.")
        };
        MainScrollViewer.ScrollToTop();
        if (_settings is not null) ApplyProviderPanels();
    }

    private async void ApplyRecommendedSettings_OnClick(object sender, RoutedEventArgs e)
    {
        ApplyRecommendedSettings();
        ApplySettingsToUi();
        _settingsService.Save(_settings);
        SetStatus("권장값 적용됨", "스마트 복합·Hy-MT2·3개 OCR 언어·브리핑 필터를 적용했습니다.");
        await RefreshQuickStartGuideAsync();
    }

    private void ApplyRecommendedSettings()
    {
        _settings.TranslationProvider = "Hybrid";
        _settings.LocalAiModel = LocalAiService.DefaultModelName;
        _settings.Model = LocalAiService.DefaultModelName;
        _settings.ApiBaseUrl = LocalAiService.OpenAiBaseUrl;
        _settings.SendTargetLanguage = "EN";
        _settings.OverlayTargetLanguage = "KO";
        _settings.OcrLanguages = new List<string> { "EN", "JP", "KO" };
        _settings.OcrLanguage = "AUTO";
        _settings.OcrChatFilterMode = GameChatFilterService.BriefingMode;
        _settings.OcrStabilizationMs = 350;
        _settings.OcrAutoEnhance = true;
        _settings.OcrTwoFrameConsensus = true;
        _settings.OcrConsensusDelayMs = 180;
        _settings.OverlayDisplaySeconds = 15;
        _settings.OverlayBackgroundOpacity = 0.38;
        _settings.OverlayBorderOpacity = 0.48;
        _settings.OverlayFontSize = 17;
        _settings.Hotkey = "\\";
        _settings.ShowStartupGuide = true;
        _settings.ShowAdvancedSettings = false;
        _settings.AutoSwitchGameProfile = true;
    }

    private async void PrepareRecommendedSetup_OnClick(object sender, RoutedEventArgs e)
    {
        if (_recommendedSetupBusy) return;
        _recommendedSetupBusy = true;
        RecommendedPrepareButton.IsEnabled = false;
        RefreshGuideButton.IsEnabled = false;
        ApplyRecommendedSettings();
        ApplySettingsToUi();
        _settingsService.Save(_settings);

        var liteReady = _lite.GetStatus().Ready;
        var localStatus = await _localAi.GetStatusAsync(LocalAiService.DefaultModelName);
        var localReady = localStatus.Ready;
        try
        {
            if (!liteReady)
            {
                LiteInstallProgress.Visibility = Visibility.Visible;
                var liteProgress = new Progress<LiteProgress>(update =>
                {
                    LiteInstallProgress.Value = update.Percent;
                    GuideNextActionText.Text = $"1/2 · Valtrans Lite 준비 중 · {update.Message}";
                    SetLiteStatus(update.Message, false);
                    SetStatus("권장 구성 준비 중", update.Message);
                });
                var liteResult = await _lite.InstallAndPrepareAsync(liteProgress);
                liteReady = liteResult.Success;
                SetLiteStatus(liteResult.Message, liteResult.Success);
            }

            if (!localReady)
            {
                LocalInstallProgress.Visibility = Visibility.Visible;
                LocalInstallProgress.IsIndeterminate = false;
                var localProgress = new Progress<LocalAiProgress>(update =>
                {
                    LocalInstallProgress.Value = update.Percent;
                    GuideNextActionText.Text = $"2/2 · Hy-MT2 준비 중 · {update.Message}";
                    SetLocalStatus(update.Message, false);
                    SetStatus("권장 구성 준비 중", update.Message);
                });
                var localResult = await _localAi.InstallAndPrepareAsync(LocalAiService.DefaultModelName, localProgress);
                localReady = localResult.Success;
                SetLocalStatus(localResult.Message, localResult.Success);
            }

            if (localReady)
                localReady = await WarmUpLocalAiAsync(showGlobalStatus: false);
            _settingsService.Save(_settings);
            SetStatus(liteReady && localReady ? "권장 엔진 준비 완료" : "일부 엔진 준비됨",
                liteReady && localReady
                    ? "Valtrans Lite와 Hy-MT2가 모두 준비됐습니다. 이제 OCR 영역을 설정하세요."
                    : "준비되지 않은 엔진은 번역 엔진 카드에서 안내를 확인해 주세요.",
                !liteReady && !localReady);
        }
        catch (Exception ex)
        {
            SetStatus("권장 구성 준비 실패", FriendlyMessage(ex), true);
        }
        finally
        {
            _recommendedSetupBusy = false;
            LiteInstallProgress.Visibility = Visibility.Collapsed;
            LocalInstallProgress.IsIndeterminate = false;
            LocalInstallProgress.Visibility = Visibility.Collapsed;
            RefreshLiteStatus();
            await RefreshLocalStatusAsync();
            RefreshGuideButton.IsEnabled = true;
            await RefreshQuickStartGuideAsync();
        }
    }

    private async Task RefreshQuickStartGuideAsync()
    {
        if (_guideRefreshBusy || GuideNextActionText is null) return;
        _guideRefreshBusy = true;
        RefreshGuideButton.IsEnabled = false;
        try
        {
            var provider = GetTag(TranslationProviderCombo, _settings.TranslationProvider);
            var providerRecommended = provider == "Hybrid";
            SetGuideState(GuideEngineStateText,
                providerRecommended ? "✓ 권장값 사용 중" : $"현재 · {ProviderDisplayName(provider)}", providerRecommended);

            var liteStatus = _lite.GetStatus();
            var localStatus = await _localAi.GetStatusAsync(LocalAiService.DefaultModelName);
            var liteReady = liteStatus.Ready;
            var localReady = localStatus.Ready;
            SetGuideState(GuideModelStateText,
                liteReady && localReady ? "✓ Lite + Hy-MT2 준비됨"
                : liteReady ? "△ Lite만 준비됨"
                : localReady ? "△ Hy-MT2만 준비됨"
                : "준비 필요 · 버튼 한 번", liteReady && localReady);
            SimpleEngineStatusText.Text = liteReady && localReady
                ? "Lite + Hy-MT2 준비됨 · 외부 서버 없이 자동 전환"
                : liteReady || localReady
                    ? $"일부 준비됨 · {(liteReady ? "Lite" : "Hy-MT2")} 사용 가능"
                    : "번역 모델 준비 필요 · ‘엔진 준비’를 눌러 주세요";

            var regionReady = _settings.CaptureRegion.IsValid;
            SetGuideState(GuideRegionStateText,
                regionReady ? $"✓ {GameDisplayName(_activeRegionGame)} 영역 저장됨" : "게임 선택 후 추천 영역", regionReady);

            var selectedLanguages = IsLoaded ? ReadOcrLanguages(updateUiWhenEmpty: false) : _settings.OcrLanguages;
            var languageStates = _languagePacks.GetSupportedLanguageStatus();
            var missingLanguages = selectedLanguages.Where(code => !languageStates.GetValueOrDefault(code)).ToArray();
            SetGuideState(GuideLanguageStateText,
                missingLanguages.Length == 0 ? $"✓ {string.Join("/", selectedLanguages)} 언어팩 준비됨"
                : $"설치 필요 · {string.Join("/", missingLanguages)}", missingLanguages.Length == 0);

            GuideNextActionText.Text = !providerRecommended
                ? "‘권장값 적용’을 눌러 스마트 복합 구성을 선택하세요."
                : !liteReady || !localReady
                    ? "‘권장 엔진 준비’를 눌러 Lite와 Hy-MT2를 한 번에 설치하세요."
                    : missingLanguages.Length > 0
                        ? $"받는 채팅 카드의 ‘언어팩’을 눌러 {string.Join("/", missingLanguages)} OCR 기능을 설치하세요."
                        : !regionReady
                            ? "게임을 실행하고 ‘추천 영역’을 누른 뒤 OCR 테스트로 채팅만 읽히는지 확인하세요."
                            : "준비 완료 · 보내기는 \\ 단축키, 받기는 ‘OCR 시작’을 사용하세요.";

            RecommendedPrepareButton.Content = liteReady && localReady ? "권장 엔진 준비됨" : "권장 엔진 준비";
            RecommendedPrepareButton.IsEnabled = !_recommendedSetupBusy && (!liteReady || !localReady);
            var engineNeedsRepair = provider switch
            {
                "Hybrid" => !liteReady || !localReady,
                "Lite" => !liteReady,
                "Ollama" => !localReady,
                _ => false
            };
            AutoRepairButton.IsEnabled = !_recommendedSetupBusy && !_diagnosticsBusy &&
                                         (engineNeedsRepair || missingLanguages.Length > 0 || !regionReady ||
                                          !GlobalHotkeyService.TryValidate(_settings.Hotkey, out _));
        }
        catch (Exception ex)
        {
            GuideNextActionText.Text = $"상태 확인 필요 · {FriendlyMessage(ex)}";
        }
        finally
        {
            _guideRefreshBusy = false;
            RefreshGuideButton.IsEnabled = !_recommendedSetupBusy;
        }
    }

    private static void SetGuideState(TextBlock textBlock, string text, bool ready)
    {
        textBlock.Text = text;
        textBlock.Foreground = new SolidColorBrush(ready
            ? Color.FromRgb(4, 120, 87)
            : Color.FromRgb(154, 91, 10));
    }

    private static string ProviderDisplayName(string provider) => provider switch
    {
        "DeepL" => "DeepL 공식 API",
        "DeepLX" => "DLX",
        "Lite" => "Valtrans Lite",
        "OpenAI" => "GPT-4o mini",
        "Ollama" => "로컬 AI",
        _ => "스마트 복합"
    };

    private async void Hotkey_OnPressed(object? sender, EventArgs e)
    {
        if (_sendBusy) return;
        var detectionGame = _settings.AutoSwitchGameProfile ? "Auto" : _settings.Game;
        if (!GameWindowDetectionService.TryGetForegroundSupportedGame(detectionGame, out _))
        {
            _hotkey?.Unregister();
            return;
        }
        var gameWindow = NativeMethods.GetForegroundWindow();
        if (gameWindow == IntPtr.Zero || gameWindow == new WindowInteropHelper(this).Handle) return;
        _sendBusy = true;
        try
        {
            if (!await EnsureTranslationProviderReadyAsync()) return;
            SetStatus("읽는 중", "게임 채팅을 가져오는 중입니다.");
            var source = await _keyboard.ReadSelectedChatAsync(gameWindow);
            if (string.IsNullOrWhiteSpace(source))
                throw new InvalidOperationException("채팅을 복사하지 못했습니다. 채팅 입력창을 연 뒤 다시 눌러 주세요.");

            SetStatus("번역 중", Shorten(source, 60));
            _lastHybridRoute = "";
            var translated = await _translator.TranslateAsync(source, _settings.SendTargetLanguage, _settings);
            await _keyboard.ReplaceChatAsync(gameWindow, translated);
            SetStatus(_settings.TranslationProvider == "Hybrid" && _lastHybridRoute.Length > 0
                ? $"교체 완료 · {_lastHybridRoute}"
                : "교체 완료", translated);
        }
        catch (Exception ex)
        {
            SetStatus("번역 실패", FriendlyMessage(ex), true);
        }
        finally
        {
            _sendBusy = false;
        }
    }

    private void UpdateGameHotkeyRegistration()
    {
        if (_hotkey is null || _capturingHotkey) return;
        var selectedGame = IsInitialized && GameCombo is not null ? GetTag(GameCombo, _settings.Game) : _settings.Game;
        var detectionGame = _settings.AutoSwitchGameProfile ? "Auto" : selectedGame;
        if (!GameWindowDetectionService.TryGetForegroundSupportedGameInfo(detectionGame, out var activeInfo) || activeInfo is null)
        {
            _wasSupportedGameForeground = false;
            _lastForegroundProfileSignature = "";
            _lastForegroundProfileGame = "";
            _hotkey.Unregister();
            if (HotkeyHint is not null)
                HotkeyHint.Text = $"{_settings.Hotkey}  →  게임 창에서만 번역";
            return;
        }

        var activeGame = activeInfo.Game;
        var enteredGame = !_wasSupportedGameForeground;
        _wasSupportedGameForeground = true;
        if (_settings.AutoSwitchGameProfile &&
            !_lastForegroundProfileGame.Equals(activeGame, StringComparison.OrdinalIgnoreCase))
        {
            _lastForegroundProfileGame = activeGame;
            SwitchToGameProfile(activeGame, forceReload: true);
        }
        if (_lastForegroundProfileSignature.Length > 0 &&
            !_lastForegroundProfileSignature.Equals(activeInfo.ProfileSignature, StringComparison.OrdinalIgnoreCase))
            HandleForegroundProfileContextChanged(activeInfo);
        _lastForegroundProfileSignature = activeInfo.ProfileSignature;
        if (enteredGame) _ = MaintainLiteMemoryAsync(forceWarmup: true);

        if (!_hotkey.IsRegistered && DateTime.UtcNow >= _nextHotkeyRegisterAttempt)
        {
            try
            {
                _hotkey.Register(_settings.Hotkey);
                _nextHotkeyRegisterAttempt = DateTime.MinValue;
            }
            catch (Exception ex)
            {
                _nextHotkeyRegisterAttempt = DateTime.UtcNow.AddSeconds(5);
                SetStatus("단축키 확인 필요", FriendlyMessage(ex), true);
                return;
            }
        }
        if (HotkeyHint is not null)
            HotkeyHint.Text = $"{_settings.Hotkey}  →  {GameDisplayName(activeGame)}에서 활성";
    }

    private async void LiteMemoryTimer_OnTick(object? sender, EventArgs e)
    {
        try
        {
            var gameActive = GameWindowDetectionService.TryGetForegroundSupportedGame(
                _settings.AutoSwitchGameProfile ? "Auto" : _settings.Game, out _);
            if (gameActive || _ocrCancellation is not null)
                await MaintainLiteMemoryAsync(forceWarmup: false);
            else if ((_settings.TranslationProvider is "Lite" or "Hybrid") && _lite.GetStatus().Running && IsVisible)
            {
                await _lite.RefreshRuntimeStatusAsync();
                RefreshLiteStatus();
            }
        }
        catch
        {
            // 주기 상태 갱신 실패가 UI 스레드 예외로 이어지지 않게 합니다.
        }
    }

    private async Task MaintainLiteMemoryAsync(bool forceWarmup)
    {
        if (_liteMaintenanceBusy || _settings.TranslationProvider is not ("Lite" or "Hybrid")) return;
        var status = _lite.GetStatus();
        if (!status.Ready) return;
        _liteMaintenanceBusy = true;
        try
        {
            if (forceWarmup || status.LoadedModels == 0)
                await WarmUpLiteIfReadyAsync(showGlobalStatus: false);
            else
                await _lite.KeepAliveAsync();
            RefreshLiteStatus();
        }
        catch
        {
            // 번역 요청 자체의 재시도와 상태 카드에서 오류를 안내합니다.
        }
        finally
        {
            _liteMaintenanceBusy = false;
        }
    }

    private void SelectRegion_OnClick(object sender, RoutedEventArgs e)
    {
        var wasVisible = _overlay?.IsVisible == true;
        _overlay?.Hide();
        Hide();
        var selector = new RegionSelectorWindow();
        var selected = selector.ShowDialog() == true ? selector.SelectedRegion : null;
        Show();
        Activate();
        if (wasVisible) _overlay?.Show();
        if (selected is null) return;

        _settings.CaptureRegion = CaptureRegion.FromRectangle(selected.Value);
        _loadedRegionProfileKey = RegionProfileKey(_activeRegionGame);
        ResetOcrEnhancementLearning();
        StoreCurrentRegionProfile();
        UpdateRegionText();
        SaveCurrentSettings(showConfirmation: false);
        SetStatus("영역 저장됨", $"{GameDisplayName(_activeRegionGame)} OCR 영역을 저장했습니다.");
        _ = RefreshQuickStartGuideAsync();
    }

    private void RecommendRegion_OnClick(object sender, RoutedEventArgs e)
    {
        var selectedGame = GetTag(GameCombo, "Auto");
        var detected = GameWindowDetectionService.Detect(selectedGame);
        var game = detected?.Game ?? selectedGame;
        if (detected is not null && selectedGame.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            SetComboByTag(GameCombo, game);
        var hasGameBounds = detected is not null && detected.ClientBounds.Width >= 640 && detected.ClientBounds.Height >= 480;
        var reference = hasGameBounds ? detected!.ClientBounds : GetCurrentMonitorRectangle();

        _settings.Game = game;
        _activeRegionGame = game;
        _settings.CaptureRegion = OcrRegionRecommendationService.Recommend(game, reference);
        _loadedRegionProfileKey = RegionProfileKey(game);
        ResetOcrEnhancementLearning();
        StoreCurrentRegionProfile();
        UpdateRegionText();
        _settingsService.Save(_settings);
        SetStatus("추천 영역 적용됨",
            $"{GameDisplayName(game)} · {reference.Width}×{reference.Height} " +
            $"{(hasGameBounds ? "게임 창" : "모니터")} 기준입니다. 필요하면 직접 선택으로 미세 조정하세요.");
        _ = RefreshQuickStartGuideAsync();
    }

    private async void OcrToggle_OnClick(object sender, RoutedEventArgs e)
    {
        if (_ocrCancellation is null) await StartOcrAsync();
        else StopOcr();
    }

    private async Task StartOcrAsync()
    {
        ReadUiIntoSettings();
        if (!_settings.CaptureRegion.IsValid)
        {
            SetStatus("영역 필요", "먼저 게임 채팅 영역을 선택해 주세요.", true);
            return;
        }

        var profileValidation = ValidateCurrentOcrProfile();
        if (!profileValidation.CanUse)
        {
            SetStatus("OCR 영역 재설정 필요", profileValidation.Message, true);
            _diagnosticLog.Record("ocr_profile_validation", "blocked",
                new Dictionary<string, object?> { ["game"] = _activeRegionGame, ["reason"] = profileValidation.Validity.ToString() });
            return;
        }

        if (!await EnsureLanguagePackAsync()) return;
        if (!await EnsureTranslationProviderReadyAsync()) return;

        EnsureOverlay();
        SetOverlayVisible(true);
        _overlay!.SetClickThrough(_settings.OverlayClickThrough);
        _overlay.SetFontSize(_settings.OverlayFontSize);
        _lastOcrText = "";
        _lastOcrFrameHash = null;
        _unchangedOcrFrames = 0;
        _ocrConsensusRejectedFrames = 0;
        _ocrEnhancementEvaluationCounter = 0;
        _ocrQualityDropFrames = 0;
        _ocrBaselinePending = true;
        _previousOcrLines.Clear();
        _recentOcrBodies.Clear();
        _ocrCancellation = new CancellationTokenSource();
        _ = MaintainLiteMemoryAsync(forceWarmup: true);
        _translationQueueCancellation?.Dispose();
        _translationQueueCancellation = CancellationTokenSource.CreateLinkedTokenSource(_ocrCancellation.Token);
        OcrToggleButton.Content = "OCR 중지";
        OcrToggleButton.Background = new SolidColorBrush(Color.FromRgb(49, 58, 73));
        ClearOcrIssue();
        SetStatus("OCR 실행 중", "새 채팅을 감지하면 오버레이에 번역합니다.");
        _diagnosticLog.Record("ocr_started", "ok",
            new Dictionary<string, object?> { ["game"] = _activeRegionGame, ["profile"] = profileValidation.ProfileKey });
        _ = Task.Run(() => RunOcrLoopAsync(_ocrCancellation.Token), _ocrCancellation.Token);
    }

    private async Task<bool> EnsureTranslationProviderReadyAsync()
    {
        if (_settings.TranslationProvider == "DeepL")
        {
            if (!string.IsNullOrWhiteSpace(_settings.DeepLApiKey)) return true;
            SetStatus("DeepL API 키 필요", "API 키를 입력하거나 무료 로컬 엔진을 선택해 주세요.", true);
            return false;
        }
        if (_settings.TranslationProvider == "DeepLX")
        {
            if (_settings.DeepLxMode == "Docker")
            {
                var result = await _dlxDocker.EnsureExistingContainerRunningAsync();
                if (result.Success)
                {
                    _settings.DeepLxUrl = DlxDockerService.TranslateUrl;
                    SetDlxDockerStatus(result.Message, true);
                    return true;
                }
                SetDlxDockerStatus(result.Message, false);
                SetStatus("개인 DLX 준비 필요", result.Message, true);
                return false;
            }
            if (Uri.TryCreate(_settings.DeepLxUrl, UriKind.Absolute, out var endpoint) && endpoint.Scheme is "http" or "https")
                return true;
            SetStatus("DLX 주소 필요", "번역 엔진에서 올바른 DLX 주소를 입력해 주세요.", true);
            return false;
        }

        if (_settings.TranslationProvider == "OpenAI")
        {
            if (!string.IsNullOrWhiteSpace(_settings.ApiKey)) return true;
            SetStatus("API 키 필요", "GPT-4o mini를 사용하려면 번역 엔진에서 API 키를 입력해 주세요.", true);
            return false;
        }

        if (_settings.TranslationProvider == "Lite")
        {
            var liteStatus = _lite.GetStatus();
            SetLiteStatus(liteStatus.Message, liteStatus.Ready);
            if (!liteStatus.Ready)
            {
                SetStatus("Valtrans Lite 준비 필요", "번역 엔진에서 ‘Valtrans Lite 준비’를 먼저 완료해 주세요.", true);
                return false;
            }
            return await WarmUpLiteIfReadyAsync();
        }

        if (_settings.TranslationProvider == "Hybrid")
        {
            var liteStatus = _lite.GetStatus();
            SetLiteStatus(liteStatus.Message, liteStatus.Ready);
            var liteReady = liteStatus.Ready && await WarmUpLiteIfReadyAsync(showGlobalStatus: false);
            var localStatus = await _localAi.GetStatusAsync(_settings.LocalAiModel);
            var qwenReady = localStatus.ModelLoaded || localStatus.Ready &&
                await WarmUpLocalAiAsync(showGlobalStatus: false);
            if (liteReady || qwenReady) return true;
            SetStatus("복합 엔진 준비 필요", "Valtrans Lite 또는 선택한 로컬 모델 중 하나 이상을 먼저 준비해 주세요.", true);
            return false;
        }

        var status = await _localAi.GetStatusAsync(_settings.LocalAiModel);
        if (status.ModelLoaded) return true;
        if (status.Ready) return await WarmUpLocalAiAsync();
        SetStatus("로컬 AI 준비 필요", "번역 엔진에서 ‘로컬 AI 설치’를 먼저 완료해 주세요.", true);
        return false;
    }

    private void TranslationProvider_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized) return;
        var provider = GetTag(TranslationProviderCombo, "Hybrid");
        _settings.TranslationProvider = provider;
        _settings.ApiBaseUrl = provider is "Ollama" or "Hybrid" ? LocalAiService.OpenAiBaseUrl : "https://api.openai.com/v1";
        _settings.Model = provider is "Ollama" or "Hybrid" ? _settings.LocalAiModel : "gpt-4o-mini";
        ApplyProviderPanels();
        if (IsLoaded && ((provider is "Ollama" or "Hybrid") || provider == "DeepLX" && _settings.DeepLxAutoFallback))
            _ = WarmUpLocalAiAsync(showGlobalStatus: false);
        if (IsLoaded && (provider is "Lite" or "Hybrid"))
            RefreshLiteStatus();
        UpdateTranslationTestPresentation();
        _ = RefreshQuickStartGuideAsync();
    }

    private void ApplyProviderPanels()
    {
        if (HybridPanel is null || DeepLxPanel is null || OpenAiPanel is null || LitePanel is null || LocalAiPanel is null) return;
        var provider = _settings.TranslationProvider;
        var advanced = _settings.ShowAdvancedSettings || _dashboardPage == "Engines";
        HybridPanel.Visibility = provider == "Hybrid" ? Visibility.Visible : Visibility.Collapsed;
        SimpleEnginePanel.Visibility = provider == "Hybrid" && !advanced ? Visibility.Visible : Visibility.Collapsed;
        DeepLxPanel.Visibility = Visibility.Collapsed;
        if (DeepLApiPanel is not null)
            DeepLApiPanel.Visibility = provider == "DeepL" ? Visibility.Visible : Visibility.Collapsed;
        OpenAiPanel.Visibility = provider == "OpenAI" ? Visibility.Visible : Visibility.Collapsed;
        LitePanel.Visibility = provider == "Lite" || provider == "Hybrid" && advanced ? Visibility.Visible : Visibility.Collapsed;
        LocalAiPanel.Visibility = provider == "Ollama" || provider == "Hybrid" && advanced ||
            (provider == "DeepLX" && _settings.DeepLxAutoFallback)
                ? Visibility.Visible
                : Visibility.Collapsed;
        CredentialNoticeText.Text = provider switch
        {
            "DeepL" => "DeepL 공식 API를 직접 사용합니다. 키는 Windows 계정으로 암호화하며 DLX·Docker는 사용하지 않습니다.",
            "DeepLX" when _settings.DeepLxMode == "Docker" => "개인 DLX는 공개 중계 서버를 거치지 않지만 번역 요청은 DeepL 외부 서비스로 전달됩니다.",
            "DeepLX" => "무료 공개 서버는 채팅 원문을 해당 서버 운영자에게 전송합니다.",
            "Hybrid" => "스마트 복합 모드는 선택한 로컬 AI와 Valtrans Lite를 PC 안에서만 사용합니다.",
            "Lite" => "Valtrans Lite는 채팅과 번역 모델을 PC 안에서만 처리합니다.",
            "Ollama" => "로컬 번역은 채팅 내용을 외부 서버로 보내지 않습니다.",
            _ => "API 키는 현재 Windows 사용자 계정으로 암호화해 저장합니다."
        };
        ApplyDeepLxModePanels();
        UpdateTranslationTestPresentation();
    }

    private void TestMode_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || TestTargetCombo is null) return;
        var mode = GetTag(TestModeCombo, "Send");
        SetComboByTag(TestTargetCombo, mode == "Receive"
            ? _settings.OverlayTargetLanguage
            : _settings.SendTargetLanguage);
        TestRouteStatusText.Text = mode == "Receive"
            ? "처리 경로 · OCR 방식 · 현재 엔진 (복합 모드는 AI 우선)"
            : "처리 경로 · 보내기 방식 · 로컬 번역 우선";
    }

    private void OcrChatFilter_OnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateOcrOptionHelpText();

    private void OcrStability_OnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateOcrOptionHelpText();

    private void OcrQualityOptions_OnChanged(object sender, RoutedEventArgs e) =>
        UpdateOcrOptionHelpText();

    private void UpdateOcrOptionHelpText()
    {
        if (OcrOptionHelpText is null || OcrChatFilterCombo is null || OcrStabilityCombo is null) return;
        var filter = GetTag(OcrChatFilterCombo, GameChatFilterService.BriefingMode) switch
        {
            GameChatFilterService.AllMode => "잡담도 번역 · 모든 새 채팅을 표시합니다",
            GameChatFilterService.StrictMode => "강한 필터 · 위치·인원·행동 콜아웃만 표시합니다",
            _ => "권장 필터 · 콜아웃과 짧은 팀 소통만 표시합니다"
        };
        var stability = GetTag(OcrStabilityCombo, "350") switch
        {
            "0" => "안정화 끔 · 가장 빠르지만 글자가 덜 읽힐 수 있습니다",
            "200" => "빠른 안정화 · 선명한 채팅창에 적합합니다",
            "500" => "안정 우선 · 흔들리는 배경에 적합합니다",
            "700" => "매우 안정 · 인식은 느리지만 오독을 줄입니다",
            _ => "균형 안정화 · 대부분의 게임에서 권장합니다"
        };
        var quality = $"자동 보정 {(OcrAutoEnhanceCheck?.IsChecked == true ? "켬" : "끔")} · " +
                      $"두 프레임 합의 {(OcrConsensusCheck?.IsChecked == true ? "켬" : "끔")}";
        OcrOptionHelpText.Text = $"{filter} · {stability} · {quality}";
        if (SimpleOcrSummaryText is not null)
            SimpleOcrSummaryText.Text = $"{filter} · {stability} · " +
                $"{(OcrAutoEnhanceCheck?.IsChecked == true && OcrConsensusCheck?.IsChecked == true ? "OCR 자동 보정·합의" : "OCR 품질 옵션 일부 꺼짐")} · " +
                $"{GetTag(OverlayDurationCombo, "15")}초 표시";
    }

    private void TestSample_OnClick(object sender, RoutedEventArgs e)
    {
        var receive = GetTag(TestModeCombo, "Send") == "Receive";
        var target = GetTag(TestTargetCombo, "EN");
        TestChatInputBox.Text = (receive, target) switch
        {
            (true, "KO") => "watch left\nmaybe two A\nnice try",
            (true, "JP") => "왼쪽 조심해\n아마 A에 두 명\n나이스 트라이",
            (true, _) => "왼쪽 조심해\n아마 A에 두 명\n내 실수야",
            (false, "KO") => "watch left",
            (false, "JP") => "왼쪽 조심해",
            _ => "왼쪽 조심해"
        };
        TestChatInputBox.Focus();
        TestChatInputBox.CaretIndex = TestChatInputBox.Text.Length;
    }

    private async void TestChatTranslate_OnClick(object sender, RoutedEventArgs e)
    {
        if (_translationTestBusy) return;
        var source = TestChatInputBox.Text.Trim();
        if (!ChatTextSanitizer.HasMeaningfulContent(source))
        {
            TestChatOutputText.Text = "번역할 글자가 포함된 문장을 입력해 주세요.";
            TestRouteStatusText.Text = "처리 경로 · 입력 확인 필요";
            return;
        }

        _translationTestBusy = true;
        TestChatTranslateButton.IsEnabled = false;
        TestChatOutputText.Text = "현재 엔진으로 번역 중…";
        TestRouteStatusText.Text = "처리 경로 · 준비 중";
        try
        {
            var target = GetTag(TestTargetCombo, "EN");
            var receive = GetTag(TestModeCombo, "Send") == "Receive";
            var textToTranslate = source;
            var filterSkipped = 0;
            if (receive)
            {
                var filterMode = GetTag(OcrChatFilterCombo, _settings.OcrChatFilterMode);
                var filtered = source.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => _chatFilter.Filter(line, filterMode, _settings))
                    .ToArray();
                filterSkipped = filtered.Count(result => !result.Keep);
                var kept = filtered.Where(result => result.Keep).Select(result => result.Text).ToArray();
                if (kept.Length == 0)
                {
                    TestChatOutputText.Text = "게임 관련성 낮음 · 생략";
                    TestRouteStatusText.Text = $"처리 경로 · 채팅 필터 · {filterSkipped}줄 제외";
                    SetStatus("게임 관련성 낮음 · 생략", "잡담이나 감정 표현으로 판정해 번역하지 않았습니다.");
                    return;
                }
                textToTranslate = string.Join(Environment.NewLine, kept);
            }

            if (!await EnsureTranslationProviderReadyAsync())
            {
                TestChatOutputText.Text = "선택한 번역 엔진을 먼저 준비해 주세요.";
                TestRouteStatusText.Text = "처리 경로 · 엔진 준비 필요";
                return;
            }

            _lastHybridRoute = "";
            var stopwatch = Stopwatch.StartNew();
            var translated = await _translator.TranslateAsync(textToTranslate, target, _settings,
                preserveLines: receive, CancellationToken.None);
            stopwatch.Stop();
            TestChatOutputText.Text = translated;
            var route = _settings.TranslationProvider == "Hybrid"
                ? _translator.LastHybridRoute.Length > 0 ? _translator.LastHybridRoute : "변환 없음"
                : TranslationProviderDisplayName(_settings.TranslationProvider);
            TestRouteStatusText.Text = $"처리 경로 · {route}" +
                                       (filterSkipped > 0 ? $" · {filterSkipped}줄 제외" : "") +
                                       $" · {stopwatch.ElapsedMilliseconds:N0}ms";
            SetStatus("번역 테스트 완료", $"{route} · {Shorten(translated, 65)}");
        }
        catch (Exception ex)
        {
            TestChatOutputText.Text = FriendlyMessage(ex);
            TestRouteStatusText.Text = "처리 경로 · 번역 실패";
            SetStatus("번역 테스트 실패", FriendlyMessage(ex), true);
        }
        finally
        {
            _translationTestBusy = false;
            TestChatTranslateButton.IsEnabled = true;
        }
    }

    private void UpdateTranslationTestPresentation()
    {
        if (TestCurrentEngineText is null) return;
        var engine = _settings.TranslationProvider switch
        {
            "Hybrid" => $"스마트 복합 · {LocalAiService.GetModel(_settings.LocalAiModel).DisplayName} + Lite",
            "Ollama" => LocalAiService.GetModel(_settings.LocalAiModel).DisplayName,
            _ => TranslationProviderDisplayName(_settings.TranslationProvider)
        };
        TestCurrentEngineText.Text = $"현재 엔진 · {engine}";
    }

    private static string TranslationProviderDisplayName(string provider) => provider switch
    {
        "Hybrid" => "스마트 복합",
        "Lite" => "Valtrans Lite",
        "Ollama" => "로컬 AI",
        "DeepLX" => "DLX",
        "DeepL" => "DeepL 공식 API",
        "OpenAI" => "GPT-4o mini",
        _ => provider
    };

    private void DeepLxFallback_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || DeepLxFallbackCheck is null) return;
        _settings.DeepLxAutoFallback = DeepLxFallbackCheck.IsChecked == true;
        ApplyProviderPanels();
        if (IsLoaded && _settings.DeepLxAutoFallback)
            _ = WarmUpLocalAiAsync(showGlobalStatus: false);
    }

    private void DeepLxMode_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || DeepLxUrlBox is null) return;
        var mode = GetTag(DeepLxModeCombo, "Public");
        if (mode == "Docker")
        {
            if (!string.IsNullOrWhiteSpace(DeepLxUrlBox.Text) &&
                !DeepLxUrlBox.Text.Contains("127.0.0.1:1188", StringComparison.OrdinalIgnoreCase))
                _settings.PublicDeepLxUrl = DeepLxUrlBox.Text.Trim();
            DeepLxUrlBox.Text = DlxDockerService.TranslateUrl;
        }
        else
        {
            DeepLxUrlBox.Text = string.IsNullOrWhiteSpace(_settings.PublicDeepLxUrl)
                ? "https://deeplx.1stg.me/translate"
                : _settings.PublicDeepLxUrl;
        }
        _settings.DeepLxMode = mode;
        _settings.DeepLxUrl = mode == "Docker" ? DlxDockerService.TranslateUrl : DeepLxUrlBox.Text.Trim();
        ApplyProviderPanels();
        if (IsLoaded && _settings.TranslationProvider == "DeepLX" && mode == "Docker")
        {
            if (AutoStartDockerCheck.IsChecked == true) _ = AutoStartDlxDockerAsync();
            else _ = RefreshDlxDockerStatusAsync();
        }
    }

    private void ApplyDeepLxModePanels()
    {
        if (DeepLxPublicPanel is null || DeepLxDockerPanel is null) return;
        var docker = _settings.DeepLxMode == "Docker";
        DeepLxPublicPanel.Visibility = docker ? Visibility.Collapsed : Visibility.Visible;
        DeepLxDockerPanel.Visibility = docker ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void TestDeepLx_OnClick(object sender, RoutedEventArgs e)
    {
        var endpoint = GetTag(DeepLxModeCombo, "Public") == "Docker"
            ? DlxDockerService.TranslateUrl
            : DeepLxUrlBox.Text.Trim();
        SetStatus("연결 확인 중", "DLX에 짧은 테스트 번역을 요청합니다.");
        try
        {
            await _translator.TestDeepLxConnectionAsync(endpoint);
            SetStatus("연결 성공", "DLX 테스트 번역이 정상 처리됐습니다.");
        }
        catch (Exception ex)
        {
            SetStatus("연결 실패", FriendlyMessage(ex), true);
        }
    }

    private async void RefreshDlxDocker_OnClick(object sender, RoutedEventArgs e) =>
        await RefreshDlxDockerStatusAsync();

    private async Task<DlxDockerStatus> RefreshDlxDockerStatusAsync()
    {
        RefreshDlxDockerButton.IsEnabled = false;
        try
        {
            var status = await _dlxDocker.GetStatusAsync();
            if (status.Ready)
            {
                SetDlxDockerStatus("준비 완료 · 개인 DLX가 127.0.0.1:1188에서 실행 중", true);
                PrepareDlxDockerButton.Content = "준비 완료";
            }
            else if (!status.WslReady)
            {
                SetDlxDockerStatus("WSL 2가 설치되지 않았습니다 · 관리자 승인 후 재시작 필요", false);
                PrepareDlxDockerButton.Content = "WSL 2 · Docker 준비";
            }
            else if (!status.DockerInstalled)
            {
                SetDlxDockerStatus("Docker Desktop이 설치되어 있지 않습니다", false);
                PrepareDlxDockerButton.Content = "Docker · DLX 준비";
            }
            else if (!status.DaemonRunning)
            {
                SetDlxDockerStatus("Docker Desktop이 실행되고 있지 않습니다", false);
                PrepareDlxDockerButton.Content = "Docker 시작 · 준비";
            }
            else if (!status.ContainerExists)
            {
                SetDlxDockerStatus("Docker 실행 중 · Valtrans DLX 컨테이너 없음", false);
                PrepareDlxDockerButton.Content = "개인 DLX 만들기";
            }
            else
            {
                SetDlxDockerStatus("DLX 컨테이너가 중지됐거나 응답하지 않습니다", false);
                PrepareDlxDockerButton.Content = "개인 DLX 시작";
            }
            return status;
        }
        finally
        {
            RefreshDlxDockerButton.IsEnabled = true;
        }
    }

    private async Task AutoStartDlxDockerAsync()
    {
        if (_dlxAutoStarting) return;
        _dlxAutoStarting = true;
        PrepareDlxDockerButton.IsEnabled = false;
        RefreshDlxDockerButton.IsEnabled = false;
        DlxDockerProgress.Visibility = Visibility.Visible;
        DlxDockerProgress.Value = 2;
        SetDlxDockerStatus("WSL · Docker Desktop · 개인 DLX 자동 확인 중…", false);
        SetStatus("개인 DLX 자동 준비 중", "WSL이 없으면 관리자 승인 후 자동 설치합니다.");
        var progress = new Progress<DlxDockerProgress>(update =>
        {
            DlxDockerProgress.Value = update.Percent;
            SetDlxDockerStatus(update.Message, false);
            SetStatus("개인 DLX 자동 준비 중", update.Message);
        });
        try
        {
            var result = await _dlxDocker.PrepareAsync(progress);
            DlxDockerProgress.Value = result.Success ? 100 : DlxDockerProgress.Value;
            SetDlxDockerStatus(result.Message, result.Success);
            if (result.Success)
            {
                _settings.DeepLxUrl = DlxDockerService.TranslateUrl;
                _settingsService.Save(_settings);
                SetStatus("개인 DLX 준비됨", "WSL, Docker Desktop과 DLX 컨테이너가 준비됐습니다.");
            }
            else
                SetStatus("개인 DLX 준비 필요", result.Message, true);
        }
        catch (Exception ex)
        {
            SetDlxDockerStatus(FriendlyMessage(ex), false);
            SetStatus("Docker 자동 시작 실패", FriendlyMessage(ex), true);
        }
        finally
        {
            await Task.Delay(500);
            DlxDockerProgress.Visibility = Visibility.Collapsed;
            PrepareDlxDockerButton.IsEnabled = true;
            RefreshDlxDockerButton.IsEnabled = true;
            _dlxAutoStarting = false;
        }
    }

    private async void PrepareDlxDocker_OnClick(object sender, RoutedEventArgs e)
    {
        var refreshStatusAfterPrepare = false;
        PrepareDlxDockerButton.IsEnabled = false;
        RefreshDlxDockerButton.IsEnabled = false;
        DlxDockerProgress.Visibility = Visibility.Visible;
        DlxDockerProgress.Value = 2;
        var progress = new Progress<DlxDockerProgress>(update =>
        {
            DlxDockerProgress.Value = update.Percent;
            SetDlxDockerStatus(update.Message, false);
            SetStatus("개인 DLX 준비 중", update.Message);
        });
        try
        {
            var result = await _dlxDocker.PrepareAsync(progress);
            DlxDockerProgress.Value = result.Success ? 100 : DlxDockerProgress.Value;
            SetDlxDockerStatus(result.Message, result.Success);
            SetStatus(result.Success ? "개인 DLX 준비됨" : "Docker 확인 필요", result.Message, !result.Success);
            if (result.Success)
            {
                refreshStatusAfterPrepare = true;
                _settings.DeepLxMode = "Docker";
                _settings.DeepLxUrl = DlxDockerService.TranslateUrl;
                _settings.TranslationProvider = "DeepLX";
                _settings.StopLocalDlxOnExit = StopDlxOnExitCheck.IsChecked == true;
                _settingsService.Save(_settings);
            }
        }
        catch (Exception ex)
        {
            SetDlxDockerStatus(FriendlyMessage(ex), false);
            SetStatus("개인 DLX 준비 실패", FriendlyMessage(ex), true);
        }
        finally
        {
            PrepareDlxDockerButton.IsEnabled = true;
            RefreshDlxDockerButton.IsEnabled = true;
            await Task.Delay(500);
            DlxDockerProgress.Visibility = Visibility.Collapsed;
            if (refreshStatusAfterPrepare) await RefreshDlxDockerStatusAsync();
        }
    }

    private void SetDlxDockerStatus(string text, bool ready)
    {
        DlxDockerStatusText.Text = text;
        DlxDockerStatusDot.Fill = new SolidColorBrush(ready ? Color.FromRgb(5, 150, 105) : Color.FromRgb(217, 119, 6));
    }

    private async void CheckDeepLApi_OnClick(object sender, RoutedEventArgs e)
    {
        CheckDeepLApiButton.IsEnabled = false;
        SetStatus("키 확인 중", "번역 요청 없이 DeepL API 사용량만 조회합니다.");
        try
        {
            var message = await _translator.CheckDeepLApiUsageAsync(DeepLApiKeyBox.Password);
            SetStatus("DeepL API 확인 완료", message);
        }
        catch (Exception ex) { SetStatus("DeepL API 확인 실패", FriendlyMessage(ex), true); }
        finally { CheckDeepLApiButton.IsEnabled = true; }
    }

    private void OpenDeepLApiGuide_OnClick(object sender, RoutedEventArgs e) =>
        OpenWebPage("https://developers.deepl.com/docs/getting-started/auth");

    private async void TestOpenAi_OnClick(object sender, RoutedEventArgs e)
    {
        SetStatus("연결 확인 중", "OpenAI API 키와 GPT-4o mini 접근 권한을 확인합니다.");
        try
        {
            await _translator.TestOpenAiConnectionAsync(ApiKeyBox.Password);
            SetStatus("연결 성공", "API 키와 GPT-4o mini 테스트 요청이 정상 처리됐습니다.");
        }
        catch (Exception ex)
        {
            SetStatus("연결 실패", FriendlyMessage(ex), true);
        }
    }

    private void OpenApiKeys_OnClick(object sender, RoutedEventArgs e) =>
        OpenWebPage("https://platform.openai.com/api-keys");

    private void OpenBilling_OnClick(object sender, RoutedEventArgs e) =>
        OpenWebPage("https://platform.openai.com/settings/organization/billing/overview");

    private static void OpenWebPage(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message, "페이지를 열 수 없습니다"); }
    }

    private async void RefreshLite_OnClick(object sender, RoutedEventArgs e)
    {
        RefreshLiteButton.IsEnabled = false;
        try
        {
            await _lite.RefreshRuntimeStatusAsync();
            var update = await _lite.CheckForModelUpdatesAsync();
            RefreshLiteStatus();
            SetStatus("Valtrans Lite 확인 완료", update);
        }
        catch (Exception ex)
        {
            SetStatus("Valtrans Lite 확인 실패", FriendlyMessage(ex), true);
        }
        finally
        {
            RefreshLiteButton.IsEnabled = true;
        }
    }

    private async void ReleaseLiteMemory_OnClick(object sender, RoutedEventArgs e)
    {
        if (GameWindowDetectionService.TryGetForegroundSupportedGame(_settings.Game, out _) ||
            _ocrCancellation is not null)
        {
            SetStatus("모델 유지 중", "게임 또는 OCR이 활성화된 동안에는 번역 지연을 막기 위해 모델을 유지합니다.");
            return;
        }
        ReleaseLiteMemoryButton.IsEnabled = false;
        try
        {
            var status = await _lite.ReleaseModelsAsync();
            SetLiteStatus(status.Message, status.Ready);
            SetStatus("Lite 메모리 해제됨", "필요한 모델은 다음 게임 진입 또는 번역 전에 자동 예열됩니다.");
        }
        catch (Exception ex)
        {
            SetStatus("메모리 해제 실패", FriendlyMessage(ex), true);
        }
        finally
        {
            ReleaseLiteMemoryButton.IsEnabled = true;
            RefreshLiteStatus();
        }
    }

    private void RefreshLiteStatus()
    {
        var status = _lite.GetStatus();
        SetLiteStatus(status.Message, status.Ready);
        InstallLiteButton.Content = status.Ready ? "검사 · 복구" : "Valtrans Lite 준비";
        ReleaseLiteMemoryButton.IsEnabled = status.Running && status.LoadedModels > 0;
    }

    private async void InstallLite_OnClick(object sender, RoutedEventArgs e)
    {
        InstallLiteButton.IsEnabled = false;
        RefreshLiteButton.IsEnabled = false;
        LiteInstallProgress.Visibility = Visibility.Visible;
        LiteInstallProgress.Value = 1;
        var progress = new Progress<LiteProgress>(update =>
        {
            LiteInstallProgress.Value = update.Percent;
            SetLiteStatus(update.Message, false);
            SetStatus("Valtrans Lite 준비 중", update.Message);
        });
        try
        {
            var result = await _lite.InstallAndPrepareAsync(progress);
            LiteInstallProgress.Value = result.Success ? 100 : LiteInstallProgress.Value;
            SetLiteStatus(result.Message, result.Success);
            SetStatus(result.Success ? "Valtrans Lite 준비됨" : "설치 확인 필요", result.Message, !result.Success);
            if (result.Success)
            {
                if (_settings.TranslationProvider != "Hybrid")
                {
                    _settings.TranslationProvider = "Lite";
                    SetComboByTag(TranslationProviderCombo, "Lite");
                }
                _settingsService.Save(_settings);
                InstallLiteButton.Content = "검사 · 복구";
            }
        }
        catch (Exception ex)
        {
            SetLiteStatus(FriendlyMessage(ex), false);
            SetStatus("Valtrans Lite 설치 실패", FriendlyMessage(ex), true);
        }
        finally
        {
            InstallLiteButton.IsEnabled = true;
            RefreshLiteButton.IsEnabled = true;
            await Task.Delay(600);
            LiteInstallProgress.Visibility = Visibility.Collapsed;
            RefreshLiteStatus();
        }
    }

    private async Task<bool> WarmUpLiteIfReadyAsync(bool showGlobalStatus = true)
    {
        var status = _lite.GetStatus();
        if (!status.Ready)
        {
            SetLiteStatus(status.Message, false);
            return false;
        }
        SetLiteStatus("모델 예열 중 · 첫 실행은 수 초 걸릴 수 있습니다…", false);
        if (showGlobalStatus) SetStatus("Valtrans Lite 예열 중", "필요한 OPUS-MT 모델을 메모리에 불러오는 중입니다.");
        try
        {
            var target = _ocrCancellation is not null ? _settings.OverlayTargetLanguage : _settings.SendTargetLanguage;
            var source = _ocrCancellation is not null
                ? _settings.OcrLanguages.FirstOrDefault(language =>
                    !language.Equals(target, StringComparison.OrdinalIgnoreCase)) ?? "EN"
                : target.Equals("EN", StringComparison.OrdinalIgnoreCase) ? "KO" : "EN";
            await _lite.WarmUpAsync(source, target);
            RefreshLiteStatus();
            InstallLiteButton.Content = "검사 · 복구";
            if (showGlobalStatus) SetStatus("Valtrans Lite 준비됨", "오프라인 번역 모델이 메모리에 준비됐습니다.");
            return true;
        }
        catch (Exception ex)
        {
            SetLiteStatus(FriendlyMessage(ex), false);
            if (showGlobalStatus) SetStatus("Valtrans Lite 예열 실패", FriendlyMessage(ex), true);
            return false;
        }
    }

    private void SetLiteStatus(string text, bool ready)
    {
        LiteStatusText.Text = text;
        LiteStatusDot.Fill = new SolidColorBrush(ready ? Color.FromRgb(5, 150, 105) : Color.FromRgb(217, 119, 6));
    }

    private async void RefreshLocal_OnClick(object sender, RoutedEventArgs e) => await RefreshLocalStatusAsync();

    private async void LocalModel_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || LocalModelCombo is null) return;
        var previousModel = _settings.LocalAiModel;
        var selectedModel = LocalAiService.NormalizeModelName(GetTag(LocalModelCombo, LocalAiService.DefaultModelName));
        if (selectedModel.Equals(previousModel, StringComparison.OrdinalIgnoreCase))
        {
            ApplyLocalModelPresentation();
            return;
        }

        _settings.LocalAiModel = selectedModel;
        if (_settings.TranslationProvider is "Ollama" or "Hybrid") _settings.Model = selectedModel;
        ApplyLocalModelPresentation();
        _settingsService.Save(_settings);
        await _localAi.UnloadModelAsync(previousModel);
        await RefreshLocalStatusAsync();
    }

    private void ApplyLocalModelPresentation()
    {
        if (LocalAiTitle is null || LocalAiSubtitle is null) return;
        var model = LocalAiService.GetModel(_settings.LocalAiModel);
        LocalAiTitle.Text = $"{model.DisplayName} · Ollama";
        LocalAiSubtitle.Text = $"API 키와 사용료 없음 · {model.SizeLabel} · {model.Description}";
        UpdateTranslationTestPresentation();
    }

    private void Translator_OnLocalFallbackActivated(object? sender, TranslationFallbackEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            SetLocalStatus($"자동 대체 사용 중 · {e.ModelDisplayName}", true);
            SetStatus("로컬 대체 번역", $"{e.ModelDisplayName} · {e.Notice}");
        });
    }

    private void Translator_OnHybridRouteSelected(object? sender, HybridRouteEventArgs e)
    {
        _lastHybridRoute = e.Route;
        Dispatcher.BeginInvoke(() =>
            HybridRouteStatusText.Text = $"최근 처리 · {e.Route} · {e.Reason}");
    }

    private void Translator_OnTranslationSafetyAdjusted(object? sender, TranslationSafetyEventArgs e)
    {
        _lastHybridRoute = "안전 보정";
        _diagnosticLog.Record("translation_fact_guard", "adjusted",
            new Dictionary<string, object?> { ["reason"] = e.Reason });
        Dispatcher.BeginInvoke(() =>
            HybridRouteStatusText.Text = $"최근 처리 · 안전 보정 · {e.Reason}");
    }

    private async Task<LocalAiStatus> RefreshLocalStatusAsync()
    {
        RefreshLocalButton.IsEnabled = false;
        try
        {
            var model = LocalAiService.GetModel(_settings.LocalAiModel);
            var status = await _localAi.GetStatusAsync(_settings.LocalAiModel);
            if (status.ModelLoaded)
            {
                SetLocalStatus(LocalAiReadyText(status), true);
                InstallLocalAiButton.Content = "예열 완료";
            }
            else if (status.Ready)
            {
                SetLocalStatus("설치 완료 · 모델이 아직 메모리에 로드되지 않았습니다", false);
                InstallLocalAiButton.Content = "모델 예열";
            }
            else if (!status.OllamaInstalled)
            {
                SetLocalStatus("Ollama가 설치되어 있지 않습니다", false);
                InstallLocalAiButton.Content = "로컬 AI 설치";
            }
            else if (!status.ServerRunning)
            {
                SetLocalStatus("Ollama가 설치됐지만 서버가 실행 중이 아닙니다", false);
                InstallLocalAiButton.Content = "서버 · 모델 준비";
            }
            else
            {
                SetLocalStatus($"Ollama 실행 중 · {model.DisplayName} 다운로드 필요", false);
                InstallLocalAiButton.Content = "모델 다운로드";
            }
            return status;
        }
        finally
        {
            RefreshLocalButton.IsEnabled = true;
        }
    }

    private async void InstallLocalAi_OnClick(object sender, RoutedEventArgs e)
    {
        InstallLocalAiButton.IsEnabled = false;
        RefreshLocalButton.IsEnabled = false;
        LocalInstallProgress.Visibility = Visibility.Visible;
        LocalInstallProgress.Value = 2;
        var progress = new Progress<LocalAiProgress>(update =>
        {
            LocalInstallProgress.Value = update.Percent;
            SetLocalStatus(update.Message, false);
            SetStatus("로컬 AI 설치 중", update.Message);
        });
        try
        {
            var result = await _localAi.InstallAndPrepareAsync(_settings.LocalAiModel, progress);
            LocalInstallProgress.Value = result.Success ? 100 : LocalInstallProgress.Value;
            SetLocalStatus(result.Message, result.Success);
            SetStatus(result.Success ? "로컬 AI 준비됨" : "설치 확인 필요", result.Message, !result.Success);
            if (result.Success)
            {
                var selectedProvider = GetTag(TranslationProviderCombo, "Hybrid");
                _settings.TranslationProvider = selectedProvider;
                _settings.ApiBaseUrl = selectedProvider is "Ollama" or "Hybrid"
                    ? LocalAiService.OpenAiBaseUrl
                    : "https://api.openai.com/v1";
                _settings.Model = selectedProvider is "Ollama" or "Hybrid" ? _settings.LocalAiModel : "gpt-4o-mini";
                _settingsService.Save(_settings);
                await WarmUpLocalAiAsync();
            }
        }
        catch (Exception ex)
        {
            SetLocalStatus(FriendlyMessage(ex), false);
            SetStatus("로컬 설치 실패", FriendlyMessage(ex), true);
        }
        finally
        {
            InstallLocalAiButton.IsEnabled = true;
            RefreshLocalButton.IsEnabled = true;
            await Task.Delay(600);
            LocalInstallProgress.Visibility = Visibility.Collapsed;
            await RefreshLocalStatusAsync();
        }
    }

    private void SetLocalStatus(string text, bool ready)
    {
        LocalStatusText.Text = text;
        LocalStatusDot.Fill = new SolidColorBrush(ready ? Color.FromRgb(5, 150, 105) : Color.FromRgb(217, 119, 6));
    }

    private static string LocalAiReadyText(LocalAiStatus status)
    {
        if (status.GpuVramBytes <= 0 || status.ModelSizeBytes <= 0)
            return "예열 완료 · CPU 번역 · 즉시 사용 가능";
        var percent = Math.Round(status.GpuVramBytes * 100d / status.ModelSizeBytes);
        var vramGb = status.GpuVramBytes / 1024d / 1024d / 1024d;
        return $"예열 완료 · GPU {percent:0}% · VRAM {vramGb:0.0}GB · 즉시 번역 가능";
    }

    private async Task<bool> WarmUpLocalAiAsync(bool showGlobalStatus = true)
    {
        await _localAiWarmLock.WaitAsync();
        RefreshLocalButton.IsEnabled = false;
        InstallLocalAiButton.IsEnabled = false;
        LocalInstallProgress.Visibility = Visibility.Visible;
        LocalInstallProgress.IsIndeterminate = true;
        var model = LocalAiService.GetModel(_settings.LocalAiModel);
        SetLocalStatus($"예열 중 · {model.DisplayName} 모델을 메모리에 로드하고 있습니다…", false);
        if (showGlobalStatus)
            SetStatus("로컬 AI 예열 중", "첫 실행은 PC 성능에 따라 30초 이상 걸릴 수 있습니다.");
        var progress = new Progress<LocalAiProgress>(update =>
        {
            SetLocalStatus(update.Message, false);
            if (showGlobalStatus) SetStatus("로컬 AI 예열 중", update.Message);
        });
        try
        {
            var result = await _localAi.WarmUpAsync(_settings.LocalAiModel, progress);
            var status = await _localAi.GetStatusAsync(_settings.LocalAiModel);
            var ready = result.Success && status.ModelLoaded;
            SetLocalStatus(ready ? LocalAiReadyText(status) : result.Message, ready);
            InstallLocalAiButton.Content = ready ? "예열 완료" : "모델 예열";
            if (showGlobalStatus)
                SetStatus(ready ? "로컬 AI 예열 완료" : "로컬 AI 예열 실패", result.Message, !ready);
            return ready;
        }
        catch (Exception ex)
        {
            SetLocalStatus(FriendlyMessage(ex), false);
            if (showGlobalStatus) SetStatus("로컬 AI 예열 실패", FriendlyMessage(ex), true);
            return false;
        }
        finally
        {
            LocalInstallProgress.IsIndeterminate = false;
            LocalInstallProgress.Visibility = Visibility.Collapsed;
            RefreshLocalButton.IsEnabled = true;
            InstallLocalAiButton.IsEnabled = true;
            _localAiWarmLock.Release();
        }
    }

    private async void InstallLanguagePack_OnClick(object sender, RoutedEventArgs e)
    {
        ReadUiIntoSettings();
        await EnsureLanguagePackAsync();
    }

    private async void TestOcr_OnClick(object sender, RoutedEventArgs e)
    {
        if (_ocrCancellation is not null)
        {
            SetStatus("OCR 실행 중", "테스트하려면 실행 중인 OCR을 먼저 중지해 주세요.", true);
            return;
        }
        ReadUiIntoSettings();
        if (!_settings.CaptureRegion.IsValid)
        {
            SetStatus("영역 필요", "추천 영역 또는 직접 선택으로 OCR 영역을 먼저 설정해 주세요.", true);
            return;
        }

        var profileValidation = ValidateCurrentOcrProfile();
        if (!profileValidation.CanUse)
        {
            SetStatus("OCR 영역 재설정 필요", profileValidation.Message, true);
            return;
        }

        var missing = _settings.OcrLanguages.Where(code => !_languagePacks.IsInstalled(code)).ToArray();
        if (missing.Length > 0)
        {
            SetStatus("언어팩 필요", $"{string.Join(", ", missing)} OCR 언어팩을 먼저 설치해 주세요.", true);
            return;
        }

        TestOcrButton.IsEnabled = false;
        SetStatus("OCR 테스트 중", "선택 영역에서 현재 보이는 글자를 읽고 있습니다.");
        try
        {
            var result = await _ocr.ReadDetailedAsync(_settings.CaptureRegion.ToRectangle(), _settings.OcrLanguages,
                autoEnhance: _settings.OcrAutoEnhance);
            var preview = string.IsNullOrWhiteSpace(result.Text)
                ? "인식된 글자가 없습니다. 채팅이 보이는 상태에서 영역을 다시 조정해 주세요."
                : result.Text.Trim();
            var qualityMode = result.EnhancementUsed ? "자동 보정" : "원본";
            MessageBox.Show(this, preview,
                $"OCR 테스트 · {result.DetectedLanguage} · {qualityMode} · 선택용 추정 점수 {result.QualityScore:0} (정확도 % 아님)",
                MessageBoxButton.OK, MessageBoxImage.Information);
            SetStatus("OCR 테스트 완료", string.IsNullOrWhiteSpace(result.Text)
                ? "글자 없음"
                : $"{qualityMode} · 추정 점수 {result.QualityScore:0} · {Shorten(result.Text, 55)}");
        }
        catch (Exception ex)
        {
            SetStatus("OCR 테스트 실패", OcrCaptureMessage(ex), true);
        }
        finally
        {
            TestOcrButton.IsEnabled = true;
        }
    }

    private void CalibrateOcr_OnClick(object sender, RoutedEventArgs e)
    {
        if (_ocrCancellation is not null)
        {
            SetStatus("OCR 실행 중", "영역을 보정하려면 실행 중인 OCR을 먼저 중지해 주세요.", true);
            return;
        }

        ReadUiIntoSettings();
        var missing = _settings.OcrLanguages.Where(code => !_languagePacks.IsInstalled(code)).ToArray();
        if (missing.Length > 0)
        {
            SetStatus("언어팩 필요", $"{string.Join(", ", missing)} OCR 언어팩을 먼저 설치해 주세요.", true);
            return;
        }

        var selectedGame = GetTag(GameCombo, "Auto");
        var detected = GameWindowDetectionService.Detect(selectedGame);
        var game = detected?.Game ?? selectedGame;
        if (detected is not null && selectedGame.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            SetComboByTag(GameCombo, game);
        var hasGameBounds = detected is not null && detected.ClientBounds.Width >= 640 && detected.ClientBounds.Height >= 480;
        var reference = hasGameBounds ? detected!.ClientBounds : GetCurrentMonitorRectangle();
        var recommended = OcrRegionRecommendationService.Recommend(game, reference);
        var initial = _settings.CaptureRegion.IsValid ? _settings.CaptureRegion.Clone() : recommended.Clone();

        var overlayWasVisible = _overlay?.IsVisible == true;
        _overlay?.Hide();
        Hide();
        try
        {
            var wizard = new OcrCalibrationWindow(game, initial, recommended, reference, _settings.OcrLanguages);
            if (wizard.ShowDialog() != true) return;
            _settings.Game = game;
            _activeRegionGame = game;
            _settings.CaptureRegion = wizard.SelectedRegion.Clone();
            _loadedRegionProfileKey = RegionProfileKey(game);
            ResetOcrEnhancementLearning();
            StoreCurrentRegionProfile();
            _settingsService.Save(_settings);
            UpdateRegionText();
            SetStatus("OCR 영역 보정됨", $"{GameDisplayName(game)} · {RegionDescription(_settings.CaptureRegion)}");
        }
        finally
        {
            Show();
            Activate();
            if (overlayWasVisible) _overlay?.Show();
        }
    }

    private async Task<bool> EnsureLanguagePackAsync()
    {
        var codes = _settings.OcrLanguages.ToArray();
        var missing = codes.Where(code => !_languagePacks.IsInstalled(code)).ToArray();
        if (missing.Length == 0)
        {
            RefreshLanguagePackStatus();
            SetStatus("언어팩 준비됨", $"{string.Join(", ", codes)} Windows OCR를 사용할 수 있습니다.");
            return true;
        }

        InstallLanguagePackButton.IsEnabled = false;
        InstallLanguagePackButton.Content = "설치 중…";
        OcrToggleButton.IsEnabled = false;
        SetStatus("언어팩 설치 중", "관리자 승인 후 나타나는 설치 창이 닫힐 때까지 기다려 주세요.");
        try
        {
            foreach (var code in missing)
            {
                LanguagePackStatusText.Text = $"{code} OCR 설치 중…";
                SetStatus("언어팩 설치 중", $"{LanguagePackService.GetLanguageName(code)} OCR 기능을 설치하고 있습니다. 검은 설치 창에 진행률이 표시됩니다.");
                var result = await _languagePacks.InstallAsync(code);
                RefreshLanguagePackStatus();
                if (!result.Installed)
                {
                    SetStatus("설치 확인 필요", result.Message, true);
                    return false;
                }
            }
            var readyNow = codes.All(code => _languagePacks.IsInstalled(code));
            SetStatus(
                readyNow ? "언어팩 설치됨" : "언어팩 설치 완료",
                readyNow
                    ? $"{string.Join(", ", codes)} OCR가 준비됐습니다."
                    : "설치는 완료됐습니다. 앱을 다시 실행한 뒤에도 표시되지 않으면 Windows를 재시작해 주세요.");
            return true;
        }
        catch (OperationCanceledException)
        {
            SetStatus("설치 취소됨", "OCR 언어팩 설치가 취소되었습니다.", true);
            return false;
        }
        catch (Exception ex)
        {
            SetStatus("설치 실패", FriendlyMessage(ex), true);
            return false;
        }
        finally
        {
            InstallLanguagePackButton.IsEnabled = true;
            OcrToggleButton.IsEnabled = true;
            RefreshLanguagePackStatus();
        }
    }

    private void RefreshLanguagePackStatus()
    {
        try
        {
            var states = _languagePacks.GetSupportedLanguageStatus();
            LanguagePackStatusText.Text = string.Join("   ", new[] { "EN", "JP", "KO" }.Select(code => $"{code} {(states[code] ? "✓" : "–")}"));
            var selected = IsLoaded ? ReadOcrLanguages(updateUiWhenEmpty: false) : _settings.OcrLanguages;
            var ready = selected.Count > 0 && selected.All(code => states[code]);
            InstallLanguagePackButton.Content = ready ? "설치됨" : "확인 · 설치";
            UpdateDetectedOcrLanguage();
        }
        catch
        {
            LanguagePackStatusText.Text = "OCR 언어팩 상태를 확인하지 못했습니다";
        }
    }

    private void OcrLanguages_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        RefreshLanguagePackStatus();
        UpdateDetectedOcrLanguage();
        _ = RefreshQuickStartGuideAsync();
    }

    private void UpdateDetectedOcrLanguage(string? detectedCode = null)
    {
        if (DetectedOcrLanguageText is null) return;
        var automatic = ReadOcrLanguages(updateUiWhenEmpty: false).Count > 1;
        DetectedOcrLanguageText.Visibility = automatic ? Visibility.Visible : Visibility.Collapsed;
        if (!automatic) return;
        DetectedOcrLanguageText.Text = detectedCode switch
        {
            "JP" => "감지: 日本語",
            "EN" => "감지: English",
            "KO" => "감지: 한국어",
            "MIXED" => "감지: 혼합 · 줄별 판정",
            _ => "감지 대기"
        };
    }

    private void CaptureHotkey_OnClick(object sender, RoutedEventArgs e)
    {
        if (_capturingHotkey)
        {
            CancelHotkeyCapture();
            return;
        }

        _hotkeyBeforeCapture = HotkeyBox.Text;
        _capturingHotkey = true;
        _hotkey?.Unregister();
        HotkeyBox.Text = "키 조합을 누르세요…";
        CaptureHotkeyButton.Content = "취소";
        CaptureHotkeyButton.Focus();
        SetStatus("단축키 입력", "원하는 키 조합을 지금 눌러 주세요. ESC를 누르면 취소됩니다.");
    }

    private void Window_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_capturingHotkey) return;
        e.Handled = true;

        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key
        };
        if (key == Key.Escape)
        {
            CancelHotkeyCapture();
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin) return;

        var modifiers = Keyboard.Modifiers;
        var gesture = BuildHotkeyGesture(modifiers, key);
        try
        {
            _hotkey?.Register(gesture);
            _hotkey?.Unregister();
            _settings.Hotkey = gesture;
            HotkeyBox.Text = gesture;
            HotkeyHint.Text = $"{gesture}  →  전체 선택 · 번역 · 교체";
            _settingsService.Save(_settings);
            FinishHotkeyCapture();
            UpdateGameHotkeyRegistration();
            SetStatus("단축키 등록됨", $"이제 {gesture}를 누르면 채팅을 번역합니다.");
        }
        catch (Exception ex)
        {
            HotkeyBox.Text = _hotkeyBeforeCapture;
            FinishHotkeyCapture();
            UpdateGameHotkeyRegistration();
            SetStatus("등록 실패", FriendlyMessage(ex), true);
        }
    }

    private void CancelHotkeyCapture()
    {
        HotkeyBox.Text = _hotkeyBeforeCapture;
        FinishHotkeyCapture();
        UpdateGameHotkeyRegistration();
        SetStatus("입력 취소됨", $"기존 단축키 {_hotkeyBeforeCapture}를 유지합니다.");
    }

    private void FinishHotkeyCapture()
    {
        _capturingHotkey = false;
        CaptureHotkeyButton.Content = "입력";
    }

    private static string BuildHotkeyGesture(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key switch
        {
            >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(),
            Key.OemBackslash or Key.Oem5 => "\\",
            _ => key.ToString()
        });
        return string.Join("+", parts);
    }

    private void StopOcr()
    {
        _ocrCancellation?.Cancel();
        _ocrCancellation?.Dispose();
        _ocrCancellation = null;
        _translationQueueCancellation?.Cancel();
        _translationQueueCancellation?.Dispose();
        _translationQueueCancellation = null;
        lock (_translationQueueLock)
        {
            _translationQueue.Clear();
            _translationWorkerRunning = false;
        }
        OcrToggleButton.Content = "OCR 시작";
        OcrToggleButton.Background = (Brush)FindResource("AccentBrush");
        ClearOcrIssue();
        SetStatus("OCR 중지됨", "송신 채팅 단축키는 계속 사용할 수 있습니다.");
    }

    private async Task RunOcrLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            OcrReadResult ocrResult;
            try
            {
                var region = _settings.CaptureRegion.ToRectangle();
                var candidateHash = await _ocr.CaptureFrameHashAsync(region);
                if (_lastOcrFrameHash.HasValue && candidateHash == _lastOcrFrameHash.Value)
                {
                    _unchangedOcrFrames = Math.Min(_unchangedOcrFrames + 1, 8);
                    var idleDelay = Math.Min(2400, _settings.OcrIntervalMs + _unchangedOcrFrames * 150);
                    try { await Task.Delay(idleDelay, cancellationToken); }
                    catch (OperationCanceledException) { return; }
                    continue;
                }

                _unchangedOcrFrames = 0;

                if (_settings.OcrStabilizationMs > 0)
                    await Task.Delay(_settings.OcrStabilizationMs, cancellationToken);

                var enhancementMode = GetOcrEnhancementMode();
                ocrResult = await _ocr.ReadDetailedAsync(region, _settings.OcrLanguages,
                    expectedFrameHash: _settings.OcrStabilizationMs > 0 ? candidateHash : null,
                    autoEnhance: _settings.OcrAutoEnhance, enhancementMode: enhancementMode);
                if (!ocrResult.FrameChanged)
                {
                    await Task.Delay(Math.Max(150, _settings.OcrIntervalMs / 3), cancellationToken);
                    continue;
                }
                UpdateOcrEnhancementProfile(ocrResult);
                MonitorOcrProfileQuality(ocrResult);

                var captureMs = ocrResult.CaptureDurationMs;
                var recognitionMs = ocrResult.RecognitionDurationMs + ocrResult.EnhancementDurationMs;
                var consensusMs = 0d;

                if (ShouldRunOcrConsensus(ocrResult))
                {
                    var consensusWatch = Stopwatch.StartNew();
                    await Task.Delay(GetAdaptiveConsensusDelay(), cancellationToken);
                    var confirmation = await _ocr.ReadDetailedAsync(region, _settings.OcrLanguages,
                        autoEnhance: _settings.OcrAutoEnhance &&
                                     (ocrResult.EnhancementUsed || ocrResult.QualityScore < 56),
                        enhancementMode: ocrResult.EnhancementUsed ? OcrEnhancementModes.Enhanced : OcrEnhancementModes.Raw);
                    consensusWatch.Stop();
                    captureMs += confirmation.CaptureDurationMs;
                    recognitionMs += confirmation.RecognitionDurationMs + confirmation.EnhancementDurationMs;
                    consensusMs = Math.Max(0, consensusWatch.Elapsed.TotalMilliseconds - confirmation.TotalDurationMs);
                    RecordOcrPerformance(captureMs, recognitionMs, consensusMs);
                    if (!TryBuildOcrConsensus(ocrResult, confirmation, out var consensus))
                    {
                        _ocrConsensusRejectedFrames++;
                        if (_ocrConsensusRejectedFrames % 4 == 1)
                            Dispatcher.Invoke(() => SetStatus("OCR 글자 안정화 중", "숫자·방향이 한 번 더 동일하게 읽히는지 확인하고 있습니다."));
                        await Task.Delay(Math.Max(120, _settings.OcrIntervalMs / 4), cancellationToken);
                        continue;
                    }
                    _ocrConsensusRejectedFrames = 0;
                    ocrResult = consensus;
                }
                else
                {
                    RecordOcrPerformance(captureMs, recognitionMs, consensusMs);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                _diagnosticLog.RecordException("ocr_capture", ex);
                Dispatcher.Invoke(() => ShowOcrIssue("OCR 캡처 오류", OcrCaptureMessage(ex)));
                try { await Task.Delay(2500, cancellationToken); }
                catch (OperationCanceledException) { return; }
                continue;
            }

            _lastOcrFrameHash = ocrResult.FrameHash;
            Dispatcher.Invoke(ClearOcrIssue);
            var original = ocrResult.Text;
            if (ocrResult.EnhancementUsed && DateTime.UtcNow - _lastOcrEnhanceLogUtc > TimeSpan.FromMinutes(1))
            {
                _lastOcrEnhanceLogUtc = DateTime.UtcNow;
                _diagnosticLog.Record("ocr_auto_enhance", "used",
                    new Dictionary<string, object?> { ["result"] = $"quality{ocrResult.QualityScore:0}" });
            }
            if (_settings.OcrLanguages.Count > 1)
                Dispatcher.Invoke(() => UpdateDetectedOcrLanguage(ocrResult.DetectedLanguage));
            var comparison = NormalizeOcr(original);
            var currentLines = ExtractOcrLines(ocrResult);
            if (_ocrBaselinePending)
            {
                _ocrBaselinePending = false;
                _lastOcrText = comparison;
                _previousOcrLines = currentLines.Select(line => line.Normalized).ToHashSet(StringComparer.OrdinalIgnoreCase);
                RememberOcrBodies(currentLines.Select(line => ChatTextSanitizer.StripChatPrefix(line.Original)));
                Dispatcher.Invoke(() => SetStatus("OCR 기준 화면 저장됨", "현재 보이는 기존 채팅은 건너뛰고 새 채팅부터 번역합니다."));
            }
            else if (comparison.Length >= 2 && comparison != _lastOcrText)
            {
                var newLines = currentLines
                    .Where(line => !_previousOcrLines.Any(previous => AreSimilarOcrText(line.Normalized, previous)))
                    .ToArray();
                var historyRedisplay = newLines.Length >= 3 && currentLines.Count >= 3;
                newLines = SelectNewestPositionedLines(newLines);
                _lastOcrText = comparison;
                _previousOcrLines = currentLines.Select(line => line.Normalized).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (newLines.Length > 0)
                {
                    var messageBodies = newLines
                        .Select(line => ChatTextSanitizer.StripChatPrefix(line.Original))
                        .Where(body => !string.IsNullOrWhiteSpace(body))
                        .ToArray();
                    var filteredBodies = messageBodies
                        .Select(body => new
                        {
                            Original = body,
                            Filter = _chatFilter.Filter(body, _settings.OcrChatFilterMode, _settings)
                        })
                        .ToArray();
                    var relevanceSkipped = filteredBodies.Count(item => !item.Filter.Keep);
                    var onlyLowRelevance = messageBodies.Length > 0 && relevanceSkipped == messageBodies.Length;
                    var translatableBodies = filteredBodies
                        .Where(item => item.Filter.Keep)
                        .Where(item => !DetectTextLanguage(item.Filter.Text, ocrResult.DetectedLanguage)
                            .Equals(_settings.OverlayTargetLanguage, StringComparison.OrdinalIgnoreCase))
                        .Where(item => !WasRecentlyHandled(item.Original, historyRedisplay))
                        .Select(item => item.Filter.Text)
                        .ToArray();
                    if (translatableBodies.Length == 0)
                    {
                        Dispatcher.Invoke(() => SetStatus(
                            onlyLowRelevance ? "게임 관련성 낮음 · 생략" : "번역 생략",
                            historyRedisplay
                                ? "채팅창에 다시 표시된 기존 기록을 걸러냈습니다."
                                : onlyLowRelevance
                                    ? $"{relevanceSkipped}줄 · 잡담이나 감정 표현으로 판정했습니다."
                                    : "시스템 문구·기존 기록이거나 이미 출력 언어인 새 줄입니다."));
                        continue;
                    }

                    var newText = string.Join(Environment.NewLine, translatableBodies);
                    RememberOcrBodies(messageBodies);
                    QueueLatestTranslation(newText, newLines.Length - translatableBodies.Length);
                }
            }

            try { await Task.Delay(_settings.OcrIntervalMs, cancellationToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private void QueueLatestTranslation(string text, int skippedCount)
    {
        var startWorker = false;
        lock (_translationQueueLock)
        {
            var normalized = NormalizeOcr(text);
            if (!_translationQueue.Any(item => NormalizeOcr(item.Text).Equals(normalized, StringComparison.OrdinalIgnoreCase)))
                _translationQueue.Add(new TranslationQueueItem(text, skippedCount));
            if (!_translationWorkerRunning)
            {
                _translationWorkerRunning = true;
                startWorker = true;
            }
        }

        if (startWorker && _translationQueueCancellation is not null)
            _ = ProcessTranslationQueueAsync(_translationQueueCancellation.Token);
    }

    private async Task ProcessTranslationQueueAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(250, cancellationToken);
                TranslationQueueItem[] batch;
                lock (_translationQueueLock)
                {
                    if (_translationQueue.Count == 0) return;
                    batch = _translationQueue.ToArray();
                    _translationQueue.Clear();
                }

                var lines = batch.SelectMany(item => item.Text.Split(new[] { '\r', '\n' },
                        StringSplitOptions.RemoveEmptyEntries))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var text = string.Join(Environment.NewLine, lines);
                var skippedCount = batch.Sum(item => item.SkippedCount);
                Dispatcher.Invoke(() => SetStatus("최신 채팅 묶음 번역 중", $"{lines.Length}줄 · {Shorten(text, 52)}"));

                string translated;
                var usedSafeBriefing = false;
                _lastHybridRoute = "";
                var translationWatch = Stopwatch.StartNew();
                try
                {
                    var batchResult = await TranslateWithSafeBriefingDeadlineAsync(lines, text, cancellationToken);
                    translated = batchResult.Text;
                    usedSafeBriefing = batchResult.UsedSafeBriefing;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    translationWatch.Stop();
                    RecordTranslationPerformance(translationWatch.Elapsed.TotalMilliseconds);
                    _diagnosticLog.RecordException("ocr_translation", ex);
                    Dispatcher.Invoke(() => ShowOcrIssue("번역 연결 오류", FriendlyMessage(ex)));
                    continue;
                }
                translationWatch.Stop();
                RecordTranslationPerformance(translationWatch.Elapsed.TotalMilliseconds);

                _overlay?.AddTranslation(ShortenLines(text, 180), ShortenLines(translated, 240),
                    _settings.OverlayDisplaySeconds);
                Dispatcher.Invoke(() => SetStatus(
                    usedSafeBriefing
                        ? "즉시 안전 브리핑 표시"
                        : _settings.TranslationProvider == "Hybrid" && _lastHybridRoute.Length > 0
                        ? $"새 채팅 번역됨 · {_lastHybridRoute}"
                        : "새 채팅 번역됨",
                    skippedCount > 0 ? $"{skippedCount}줄 제외 · {Shorten(translated, 58)}" : Shorten(translated, 75)));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            var restart = false;
            lock (_translationQueueLock)
            {
                _translationWorkerRunning = false;
                if (_translationQueue.Count > 0 && !cancellationToken.IsCancellationRequested)
                {
                    _translationWorkerRunning = true;
                    restart = true;
                }
            }
            if (restart) _ = ProcessTranslationQueueAsync(cancellationToken);
        }
    }

    private async Task<TranslationBatchResult> TranslateWithSafeBriefingDeadlineAsync(string[] lines, string text,
        CancellationToken cancellationToken)
    {
        var safeLines = new List<string>(lines.Length);
        foreach (var line in lines)
        {
            if (!TranslationFactGuard.TryBuildSafeBriefing(line, _settings.OverlayTargetLanguage, _settings,
                    _glossary, out var safeLine))
                return new TranslationBatchResult(await TranslateBatchAsync(lines, text, cancellationToken), false);
            safeLines.Add(safeLine);
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var translationTask = TranslateBatchAsync(lines, text, deadline.Token);
        var completed = await Task.WhenAny(translationTask, Task.Delay(TimeSpan.FromMilliseconds(1600), cancellationToken));
        if (completed == translationTask)
            return new TranslationBatchResult(await translationTask, false);

        cancellationToken.ThrowIfCancellationRequested();
        deadline.Cancel();
        _ = translationTask.ContinueWith(task => _ = task.Exception,
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        _lastHybridRoute = "안전 브리핑";
        _diagnosticLog.Record("translation_safe_briefing", "deadline",
            new Dictionary<string, object?>
            {
                ["provider"] = _settings.TranslationProvider,
                ["durationMs"] = 1600,
                ["count"] = lines.Length
            });
        return new TranslationBatchResult(string.Join(Environment.NewLine, safeLines), true);
    }

    private async Task<string> TranslateBatchAsync(string[] lines, string text, CancellationToken cancellationToken)
    {
        var sourceLanguages = lines.Select(line => DetectTextLanguage(line, "EN"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (sourceLanguages.Length <= 1)
            return await TranslateWithRetryAsync(text, preserveLines: true, cancellationToken);

        var translatedLines = new List<string>(lines.Length);
        foreach (var line in lines)
            translatedLines.Add(await TranslateWithRetryAsync(line, preserveLines: false, cancellationToken));
        return string.Join(Environment.NewLine, translatedLines);
    }

    private async Task<string> TranslateWithRetryAsync(string text, bool preserveLines,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                return await _translator.TranslateAsync(text, _settings.OverlayTargetLanguage, _settings,
                    preserveLines, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                lastError = ex;
                if (attempt == 0) await Task.Delay(450, cancellationToken);
            }
        }
        throw lastError ?? new InvalidOperationException("번역에 실패했습니다.");
    }

    private static bool TryBuildOcrConsensus(OcrReadResult first, OcrReadResult second, out OcrReadResult consensus)
    {
        consensus = second;
        if (string.IsNullOrWhiteSpace(first.Text) || string.IsNullOrWhiteSpace(second.Text)) return false;

        var firstNormalized = NormalizeOcr(first.Text);
        var secondNormalized = NormalizeOcr(second.Text);
        if (AreSimilarOcrText(firstNormalized, secondNormalized))
        {
            var preferred = second.QualityScore >= first.QualityScore ? second : first;
            consensus = preferred with
            {
                FrameHash = second.FrameHash,
                EnhancementUsed = first.EnhancementUsed || second.EnhancementUsed,
                QualityScore = Math.Max(first.QualityScore, second.QualityScore)
            };
            return true;
        }

        var firstLines = ExtractOcrLines(first);
        var secondLines = ExtractOcrLines(second);
        var stable = secondLines.Where(line => firstLines.Any(previous =>
            AreSimilarOcrText(line.Normalized, previous.Normalized))).ToArray();
        if (stable.Length == 0) return false;
        var newest = secondLines.Where(line => line.Y >= 0).OrderBy(line => line.Y + line.Height).LastOrDefault();
        if (newest is not null && !stable.Any(line =>
                AreSimilarOcrText(line.Normalized, newest.Normalized))) return false;
        if (newest is null && stable.Length < Math.Max(1, (int)Math.Ceiling(secondLines.Count * 0.75))) return false;

        var stableText = string.Join(Environment.NewLine, stable.Select(line => line.Original));
        var stableNormalized = stable.Select(line => line.Normalized).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var positioned = second.PositionedLines?.Where(line => stableNormalized.Any(value =>
            AreSimilarOcrText(NormalizeOcr(line.Text), value))).ToArray();
        consensus = second with
        {
            Text = stableText,
            PositionedLines = positioned,
            EnhancementUsed = first.EnhancementUsed || second.EnhancementUsed,
            QualityScore = Math.Max(first.QualityScore, second.QualityScore)
        };
        return true;
    }

    private string GetOcrEnhancementMode()
    {
        if (!_settings.OcrAutoEnhance) return OcrEnhancementModes.Raw;
        _ocrEnhancementEvaluationCounter++;
        if (_ocrEnhancementEvaluationCounter % 30 == 0) return OcrEnhancementModes.Auto;
        if (_settings.OcrEnhancementProfiles.TryGetValue(_loadedRegionProfileKey, out var profile))
            return OcrEnhancementModes.Normalize(profile.PreferredMode);
        return OcrEnhancementModes.Auto;
    }

    private void ResetOcrEnhancementLearning()
    {
        if (!string.IsNullOrWhiteSpace(_loadedRegionProfileKey))
            _settings.OcrEnhancementProfiles.Remove(_loadedRegionProfileKey);
        _ocrEnhancementEvaluationCounter = 0;
        UpdateOcrPerformanceText();
    }

    private void UpdateOcrEnhancementProfile(OcrReadResult result)
    {
        if (!result.ComparedVariants || string.IsNullOrWhiteSpace(_loadedRegionProfileKey)) return;
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => UpdateOcrEnhancementProfile(result));
            return;
        }

        if (!_settings.OcrEnhancementProfiles.TryGetValue(_loadedRegionProfileKey, out var profile))
        {
            profile = new OcrEnhancementProfile();
            _settings.OcrEnhancementProfiles[_loadedRegionProfileKey] = profile;
        }
        var previousMode = profile.PreferredMode;
        var previousSamples = profile.ComparedSamples;
        profile.RawAverageQuality = RunningAverage(profile.RawAverageQuality, result.RawQualityScore, previousSamples);
        profile.EnhancedAverageQuality = RunningAverage(profile.EnhancedAverageQuality, result.EnhancedQualityScore, previousSamples);
        profile.ComparedSamples = Math.Min(1000, previousSamples + 1);
        profile.LastUpdatedUtc = DateTime.UtcNow;
        if (profile.ComparedSamples >= 3)
            profile.PreferredMode = profile.EnhancedAverageQuality >= profile.RawAverageQuality + 2
                ? OcrEnhancementModes.Enhanced
                : OcrEnhancementModes.Raw;

        if (profile.ComparedSamples <= 4 || !previousMode.Equals(profile.PreferredMode, StringComparison.OrdinalIgnoreCase))
        {
            _settingsService.Save(_settings);
            _diagnosticLog.Record("ocr_profile_learning", "updated",
                new Dictionary<string, object?>
                {
                    ["profile"] = _loadedRegionProfileKey,
                    ["result"] = profile.PreferredMode,
                    ["count"] = profile.ComparedSamples
                });
        }
        UpdateOcrPerformanceText();
    }

    private void MonitorOcrProfileQuality(OcrReadResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Text) || string.IsNullOrWhiteSpace(_loadedRegionProfileKey) ||
            !_settings.OcrEnhancementProfiles.TryGetValue(_loadedRegionProfileKey, out var profile) ||
            profile.PreferredMode == OcrEnhancementModes.Auto) return;

        var expected = profile.PreferredMode == OcrEnhancementModes.Enhanced
            ? profile.EnhancedAverageQuality
            : profile.RawAverageQuality;
        if (expected <= 0) return;
        if (result.QualityScore < Math.Max(28, expected - 18))
            _ocrQualityDropFrames++;
        else
            _ocrQualityDropFrames = Math.Max(0, _ocrQualityDropFrames - 1);
        if (_ocrQualityDropFrames < 3) return;

        _ocrQualityDropFrames = 0;
        profile.PreferredMode = OcrEnhancementModes.Auto;
        profile.ComparedSamples = 0;
        profile.RawAverageQuality = 0;
        profile.EnhancedAverageQuality = 0;
        profile.LastUpdatedUtc = DateTime.UtcNow;
        _ocrEnhancementEvaluationCounter = 0;
        Dispatcher.BeginInvoke(() =>
        {
            _settingsService.Save(_settings);
            UpdateOcrPerformanceText();
            SetStatus("OCR 품질 재학습", "평소보다 인식 품질이 낮아 원본·보정 방식을 다시 비교합니다.");
        });
        _diagnosticLog.Record("ocr_quality_watchdog", "relearning",
            new Dictionary<string, object?> { ["profile"] = _loadedRegionProfileKey, ["reason"] = "quality_drop" });
    }

    private static double RunningAverage(double current, double sample, int previousSamples)
    {
        if (previousSamples <= 0) return sample;
        if (previousSamples < 20) return (current * previousSamples + sample) / (previousSamples + 1);
        return current * 0.9 + sample * 0.1;
    }

    private bool ShouldRunOcrConsensus(OcrReadResult result)
    {
        if (!_settings.OcrTwoFrameConsensus || string.IsNullOrWhiteSpace(result.Text)) return false;
        if (_adaptiveOcrMode == "Balanced") return true;
        var normalized = NormalizeOcr(result.Text);
        var critical = CriticalCalloutSignature(normalized).Length > 0 || HasProtectedBriefingTerms(normalized);
        return _adaptiveOcrMode == "Efficient" ? critical || result.QualityScore < 64 : critical;
    }

    private static bool HasProtectedBriefingTerms(string text)
    {
        var terms = new[]
        {
            "no", "not", "dont", "none", "없", "아니", "하지마", "ない", "いない", "なし",
            "maybe", "probably", "아마", "추정", "かも", "たぶん"
        };
        return terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private int GetAdaptiveConsensusDelay() => _adaptiveOcrMode switch
    {
        "Performance" => 100,
        "Efficient" => Math.Min(140, _settings.OcrConsensusDelayMs),
        _ => _settings.OcrConsensusDelayMs
    };

    private void RecordOcrPerformance(double captureMs, double recognitionMs, double consensusMs)
    {
        _averageCaptureMs = Smooth(_averageCaptureMs, captureMs);
        _averageRecognitionMs = Smooth(_averageRecognitionMs, recognitionMs);
        _averageConsensusMs = Smooth(_averageConsensusMs, consensusMs);
        _averageOcrPipelineMs = Smooth(_averageOcrPipelineMs, captureMs + recognitionMs + consensusMs);
        _adaptiveOcrMode = _averageOcrPipelineMs switch
        {
            > 950 => "Performance",
            > 620 => "Efficient",
            _ => "Balanced"
        };
        Dispatcher.BeginInvoke(UpdateOcrPerformanceText);
    }

    private void RecordTranslationPerformance(double translationMs)
    {
        _averageTranslationMs = Smooth(_averageTranslationMs, translationMs);
        Dispatcher.BeginInvoke(UpdateOcrPerformanceText);
    }

    private static double Smooth(double current, double sample) => current <= 0 ? sample : current * 0.75 + sample * 0.25;

    private void UpdateOcrPerformanceText()
    {
        if (OcrPerformanceText is null) return;
        var performanceLabel = _adaptiveOcrMode switch
        {
            "Performance" => "성능 우선",
            "Efficient" => "효율 조절",
            _ => "자동 균형"
        };
        var learnedMode = _settings.OcrEnhancementProfiles.TryGetValue(_loadedRegionProfileKey, out var profile)
            ? profile.PreferredMode switch
            {
                OcrEnhancementModes.Enhanced => "보정 학습",
                OcrEnhancementModes.Raw => "원본 학습",
                _ => "보정 평가"
            }
            : "보정 평가";
        OcrPerformanceText.Text = $"처리 시간 · 캡처 {FormatMs(_averageCaptureMs)} · 인식 {FormatMs(_averageRecognitionMs)} · " +
                                  $"합의 {FormatMs(_averageConsensusMs)} · 번역 {FormatMs(_averageTranslationMs)} · {performanceLabel} · {learnedMode}";
    }

    private static string FormatMs(double value) => value <= 0 ? "—" : $"{Math.Round(value):0}ms";

    private void RememberOcrBodies(IEnumerable<string> bodies)
    {
        var now = DateTime.UtcNow;
        foreach (var body in bodies)
        {
            var normalized = NormalizeOcr(body);
            if (normalized.Length >= 2) _recentOcrBodies[normalized] = now;
        }

        while (_recentOcrBodies.Count > 300)
            _recentOcrBodies.Remove(_recentOcrBodies.MinBy(pair => pair.Value).Key);
    }

    private bool WasRecentlyHandled(string body, bool historyRedisplay)
    {
        var normalized = NormalizeOcr(body);
        var match = _recentOcrBodies
            .Where(pair => AreSimilarOcrText(normalized, pair.Key))
            .OrderByDescending(pair => pair.Value)
            .FirstOrDefault();
        if (string.IsNullOrEmpty(match.Key)) return false;
        if (historyRedisplay) return true;

        // A single genuinely repeated short callout can be meaningful later. Use a
        // shorter window for it, while longer lines get stronger reopen protection.
        var suppressFor = normalized.Length <= 5 ? TimeSpan.FromSeconds(45) : TimeSpan.FromMinutes(3);
        return DateTime.UtcNow - match.Value <= suppressFor;
    }

    private static bool AreSimilarOcrText(string left, string right)
    {
        if (left.Equals(right, StringComparison.OrdinalIgnoreCase)) return true;
        var maxLength = Math.Max(left.Length, right.Length);
        if (maxLength < 6 || Math.Abs(left.Length - right.Length) > Math.Max(2, maxLength / 5)) return false;
        var leftCritical = CriticalCalloutSignature(left);
        var rightCritical = CriticalCalloutSignature(right);
        if ((leftCritical.Length > 0 || rightCritical.Length > 0) &&
            !leftCritical.Equals(rightCritical, StringComparison.OrdinalIgnoreCase)) return false;
        return LevenshteinDistance(left, right) <= Math.Max(1, (int)Math.Floor(maxLength * 0.16));
    }

    private static string CriticalCalloutSignature(string text)
    {
        var normalized = text.ToLowerInvariant();
        var parts = new List<string>();
        var digits = new string(normalized.Where(char.IsDigit).ToArray());
        if (digits.Length > 0) parts.Add($"n:{digits}");

        if (normalized.Length > 2 && normalized[^1] is 'a' or 'b' or 'c')
            parts.Add($"site:{normalized[^1]}");
        foreach (var direction in new[] { "left", "right", "front", "back", "왼쪽", "오른쪽", "앞", "뒤", "左", "右", "前", "後" })
            if (normalized.Contains(direction, StringComparison.Ordinal)) parts.Add($"dir:{direction}");
        return string.Join('|', parts);
    }

    private static int LevenshteinDistance(string left, string right)
    {
        if (left.Length > right.Length) (left, right) = (right, left);
        var previous = Enumerable.Range(0, left.Length + 1).ToArray();
        var current = new int[left.Length + 1];
        for (var row = 1; row <= right.Length; row++)
        {
            current[0] = row;
            for (var column = 1; column <= left.Length; column++)
            {
                var cost = char.ToLowerInvariant(left[column - 1]) == char.ToLowerInvariant(right[row - 1]) ? 0 : 1;
                current[column] = Math.Min(Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + cost);
            }
            (previous, current) = (current, previous);
        }
        return previous[left.Length];
    }

    private void ShowOcrIssue(string title, string detail)
    {
        OcrIssueText.Text = $"{title} · {detail}";
        OcrIssuePanel.Visibility = Visibility.Visible;
        SetStatus(title, detail, true);
    }

    private void ClearOcrIssue()
    {
        if (OcrIssuePanel.Visibility != Visibility.Visible) return;
        OcrIssuePanel.Visibility = Visibility.Collapsed;
        OcrIssueText.Text = "";
        SetStatus("OCR 실행 중", "새 채팅을 감지하면 오버레이에 번역합니다.");
    }

    private static string OcrCaptureMessage(Exception ex)
    {
        if (ex is ObjectDisposedException)
            return "OCR 이미지 처리 중 스트림이 닫혔습니다. 최신 버전으로 다시 실행해 주세요.";
        if (ex is System.Runtime.InteropServices.ExternalException or ArgumentException)
            return "선택 영역을 캡처하지 못했습니다. 게임을 테두리 없는 창 모드로 바꾸고 영역을 다시 선택해 주세요.";
        if (ex is UnauthorizedAccessException)
            return "화면 캡처 권한이 없습니다. 게임이 관리자 권한이면 Valtrans도 관리자 권한으로 실행해 주세요.";
        return FriendlyMessage(ex);
    }

    private void SaveSettings_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            ReadUiIntoSettings();
            _hotkey?.Unregister();
            _settingsService.Save(_settings);
            EnsureOverlay();
            _overlay!.SetClickThrough(_settings.OverlayClickThrough);
            _overlay.SetBackgroundOpacity(_settings.OverlayBackgroundOpacity);
            _overlay.SetBorderOpacity(_settings.OverlayBorderOpacity);
            _overlay.SetFontSize(_settings.OverlayFontSize);
            HotkeyHint.Text = $"{_settings.Hotkey}  →  전체 선택 · 번역 · 교체";
            UpdateGameHotkeyRegistration();
            SetStatus("저장됨", "번역과 단축키 설정을 적용했습니다.");
            _ = RefreshQuickStartGuideAsync();
        }
        catch (Exception ex)
        {
            SetStatus("저장 실패", FriendlyMessage(ex), true);
        }
    }

    private void Game_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _autoSwitchingGameProfile) return;

        _settings.MapsByGame[_activeRegionGame] = GetTag(MapCombo, _settings.Map);
        StoreCurrentRegionProfile();
        var selectedGame = GetTag(GameCombo, "Auto");
        _settings.Game = selectedGame;
        RefreshMapOptions(selectedGame, _settings.MapsByGame.GetValueOrDefault(selectedGame, "Auto"));
        _activeRegionGame = selectedGame;
        LoadRegionProfile(selectedGame);
        _settingsService.Save(_settings);

        if (_settings.CaptureRegion.IsValid)
            SetStatus("영역 불러옴", $"{GameDisplayName(selectedGame)}에 저장된 OCR 영역을 불러왔습니다.");
        else
            SetStatus("영역 미설정", $"{GameDisplayName(selectedGame)}용 OCR 영역을 선택해 주세요.");
        _ = RefreshQuickStartGuideAsync();
    }

    private void Map_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _updatingMapOptions) return;
        _settings.Map = GetTag(MapCombo, "Auto");
        _settings.MapsByGame[_activeRegionGame] = _settings.Map;
        _settingsService.Save(_settings);
        SetStatus("맵 사전 적용", _settings.Map == "Auto"
            ? "공통 위치 용어를 사용합니다."
            : $"{_settings.Map} 위치 용어를 번역에 반영합니다.");
    }

    private void AutoSwitchGameProfile_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settings.AutoSwitchGameProfile = AutoSwitchGameProfileCheck.IsChecked == true;
        _settingsService.Save(_settings);
        _lastForegroundProfileGame = "";
        UpdateGameHotkeyRegistration();
        SetStatus(_settings.AutoSwitchGameProfile ? "게임 프로필 자동 전환 켜짐" : "게임 프로필 자동 전환 꺼짐",
            _settings.AutoSwitchGameProfile
                ? "전면의 VALORANT 또는 Apex Legends에 맞춰 OCR 영역과 맵 사전을 불러옵니다."
                : "게임·맵 선택을 수동으로 유지합니다.");
    }

    private void SwitchToGameProfile(string activeGame, bool forceReload = false)
    {
        if (_autoSwitchingGameProfile ||
            !forceReload && _activeRegionGame.Equals(activeGame, StringComparison.OrdinalIgnoreCase)) return;

        _autoSwitchingGameProfile = true;
        try
        {
            _settings.MapsByGame[_activeRegionGame] = GetTag(MapCombo, _settings.Map);
            StoreCurrentRegionProfile();
            _settings.Game = activeGame;
            _activeRegionGame = activeGame;
            SetComboByTag(GameCombo, activeGame);
            var map = _settings.MapsByGame.GetValueOrDefault(activeGame, "Auto");
            RefreshMapOptions(activeGame, map);
            _settings.Map = GetTag(MapCombo, "Auto");
            LoadRegionProfile(activeGame);
            _lastOcrFrameHash = null;
            _ocrBaselinePending = true;
            _previousOcrLines.Clear();
            _recentOcrBodies.Clear();
            _settingsService.Save(_settings);
            SetStatus("게임 프로필 전환됨",
                _settings.CaptureRegion.IsValid
                    ? $"{activeGame} · {_settings.Map} · 저장된 OCR 영역을 불러왔습니다."
                    : $"{activeGame} 프로필에 저장된 OCR 영역이 없습니다. 추천 영역을 적용해 주세요.");
            _ = RefreshQuickStartGuideAsync();
        }
        finally
        {
            _autoSwitchingGameProfile = false;
        }
    }

    private void SaveCurrentSettings(bool showConfirmation)
    {
        ReadUiIntoSettings(preserveRegion: true);
        _settingsService.Save(_settings);
        if (showConfirmation) SetStatus("저장됨", "설정을 적용했습니다.");
    }

    private void ToggleOverlay_OnClick(object sender, RoutedEventArgs e)
    {
        EnsureOverlay();
        SetOverlayVisible(_overlay?.IsVisible != true);
    }

    private void SetOverlayVisible(bool visible)
    {
        EnsureOverlay();
        if (visible)
        {
            _overlay!.Show();
            _overlay.SetClickThrough(OverlayLockCheck.IsChecked == true);
            if (_overlay.TranslationItems.Items.Count == 0)
                _overlay.AddTranslation("", "번역 대기 중", _settings.OverlayDisplaySeconds);
            OverlayToggleButton.Content = "오버레이 끄기";
            SetStatus("오버레이 켜짐", "OCR 번역 창을 화면에 표시합니다.");
        }
        else
        {
            _overlay!.Hide();
            OverlayToggleButton.Content = "오버레이 켜기";
            SetStatus("오버레이 꺼짐", "OCR는 계속 실행할 수 있으며 번역 기록은 유지됩니다.");
        }
    }

    private void OverlayLock_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settings.OverlayClickThrough = OverlayLockCheck.IsChecked == true;
        _overlay?.SetClickThrough(_settings.OverlayClickThrough);
    }

    private void OverlayTransparency_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var transparency = Math.Clamp(OverlayTransparencySlider.Value, 0, 100);
        var opacity = 1 - transparency / 100d;
        OverlayTransparencyText.Text = $"배경 투명도 {transparency:0}%";
        _settings.OverlayBackgroundOpacity = opacity;
        _overlay?.SetBackgroundOpacity(opacity);
    }

    private void OverlayBorderTransparency_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var transparency = Math.Clamp(OverlayBorderTransparencySlider.Value, 0, 100);
        var opacity = 1 - transparency / 100d;
        OverlayBorderTransparencyText.Text = $"테두리 투명도 {transparency:0}%";
        _settings.OverlayBorderOpacity = opacity;
        _overlay?.SetBorderOpacity(opacity);
    }

    private void OverlayFontSize_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var fontSize = Math.Round(Math.Clamp(OverlayFontSizeSlider.Value, 11, 32));
        OverlayFontSizeText.Text = $"오버레이 글자 {fontSize:0}px";
        _settings.OverlayFontSize = fontSize;
        _overlay?.SetFontSize(fontSize);
    }

    private void EnsureOverlay()
    {
        if (_overlay is not null) return;
        var width = Math.Clamp(_settings.OverlayWidth, 280, Math.Max(280, SystemParameters.VirtualScreenWidth));
        var height = Math.Clamp(_settings.OverlayHeight, 90, Math.Max(90, SystemParameters.VirtualScreenHeight));
        var maxLeft = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - width;
        var maxTop = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - height;
        _overlay = new OverlayWindow
        {
            Width = width,
            Height = height,
            Left = Math.Clamp(_settings.OverlayLeft, SystemParameters.VirtualScreenLeft, Math.Max(SystemParameters.VirtualScreenLeft, maxLeft)),
            Top = Math.Clamp(_settings.OverlayTop, SystemParameters.VirtualScreenTop, Math.Max(SystemParameters.VirtualScreenTop, maxTop))
        };
        _overlay.SetClickThrough(_settings.OverlayClickThrough);
        _overlay.SetBackgroundOpacity(_settings.OverlayBackgroundOpacity);
        _overlay.SetBorderOpacity(_settings.OverlayBorderOpacity);
        _overlay.SetFontSize(_settings.OverlayFontSize);
    }

    private void StoreOverlayBounds()
    {
        if (_overlay is null) return;
        _settings.OverlayLeft = _overlay.Left;
        _settings.OverlayTop = _overlay.Top;
        _settings.OverlayWidth = _overlay.ActualWidth > 0 ? _overlay.ActualWidth : _overlay.Width;
        _settings.OverlayHeight = _overlay.ActualHeight > 0 ? _overlay.ActualHeight : _overlay.Height;
    }

    private void ApplySettingsToUi()
    {
        var version = typeof(MainWindow).Assembly.GetName().Version;
        VersionText.Text = $"Valtrans {version?.Major}.{version?.Minor}.{version?.Build} · Windows 10/11 · 시스템 OCR";
        ApiKeyBox.Password = _settings.ApiKey;
        DeepLApiKeyBox.Password = _settings.DeepLApiKey;
        if (_settings.DeepLxMode is not ("Public" or "Docker")) _settings.DeepLxMode = "Public";
        if (string.IsNullOrWhiteSpace(_settings.PublicDeepLxUrl))
            _settings.PublicDeepLxUrl = "https://deeplx.1stg.me/translate";
        SetComboByTag(DeepLxModeCombo, _settings.DeepLxMode);
        DeepLxUrlBox.Text = _settings.DeepLxMode == "Docker"
            ? DlxDockerService.TranslateUrl
            : string.IsNullOrWhiteSpace(_settings.PublicDeepLxUrl)
            ? "https://deeplx.1stg.me/translate"
            : _settings.PublicDeepLxUrl;
        StopDlxOnExitCheck.IsChecked = _settings.StopLocalDlxOnExit;
        AutoStartDockerCheck.IsChecked = _settings.AutoStartDockerDesktop;
        DeepLxFallbackCheck.IsChecked = _settings.DeepLxAutoFallback;
        _settings.LocalAiModel = LocalAiService.NormalizeModelName(_settings.LocalAiModel);
        SetComboByTag(LocalModelCombo, _settings.LocalAiModel);
        SetComboByTag(ServerRegionCombo, GameTranslationPrompt.NormalizeRegion(_settings.ServerRegion));
        ApplyLocalModelPresentation();
        if (_settings.TranslationProvider is not ("Hybrid" or "DeepL" or "Lite" or "OpenAI" or "Ollama")) _settings.TranslationProvider = "Hybrid";
        SetComboByTag(TranslationProviderCombo, _settings.TranslationProvider);
        SetComboByTag(SendTargetCombo, _settings.SendTargetLanguage);
        SetComboByTag(TestModeCombo, "Send");
        SetComboByTag(TestTargetCombo, _settings.SendTargetLanguage);
        OcrEnCheck.IsChecked = _settings.OcrLanguages.Contains("EN", StringComparer.OrdinalIgnoreCase);
        OcrJpCheck.IsChecked = _settings.OcrLanguages.Contains("JP", StringComparer.OrdinalIgnoreCase);
        OcrKoCheck.IsChecked = _settings.OcrLanguages.Contains("KO", StringComparer.OrdinalIgnoreCase);
        SetComboByTag(OverlayTargetCombo, _settings.OverlayTargetLanguage);
        SetComboByTag(OverlayDurationCombo, _settings.OverlayDisplaySeconds.ToString());
        SetComboByTag(OcrStabilityCombo, _settings.OcrStabilizationMs.ToString());
        SetComboByTag(OcrChatFilterCombo, _settings.OcrChatFilterMode);
        OcrAutoEnhanceCheck.IsChecked = _settings.OcrAutoEnhance;
        OcrConsensusCheck.IsChecked = _settings.OcrTwoFrameConsensus;
        AutoSwitchGameProfileCheck.IsChecked = _settings.AutoSwitchGameProfile;
        _activeRegionGame = _settings.Game;
        SetComboByTag(GameCombo, _settings.Game);
        RefreshMapOptions(_settings.Game, _settings.MapsByGame.GetValueOrDefault(_settings.Game, _settings.Map));
        HotkeyBox.Text = _settings.Hotkey;
        OverlayLockCheck.IsChecked = _settings.OverlayClickThrough;
        _settings.OverlayBackgroundOpacity = Math.Clamp(_settings.OverlayBackgroundOpacity, 0, 1);
        _settings.OverlayBorderOpacity = Math.Clamp(_settings.OverlayBorderOpacity, 0, 1);
        OverlayTransparencySlider.Value = Math.Round((1 - _settings.OverlayBackgroundOpacity) * 100);
        OverlayTransparencyText.Text = $"배경 투명도 {OverlayTransparencySlider.Value:0}%";
        OverlayBorderTransparencySlider.Value = Math.Round((1 - _settings.OverlayBorderOpacity) * 100);
        OverlayBorderTransparencyText.Text = $"테두리 투명도 {OverlayBorderTransparencySlider.Value:0}%";
        OverlayFontSizeSlider.Value = Math.Round(Math.Clamp(_settings.OverlayFontSize, 11, 32));
        OverlayFontSizeText.Text = $"오버레이 글자 {OverlayFontSizeSlider.Value:0}px";
        LoadRegionProfile(_settings.Game);
        HotkeyHint.Text = $"{_settings.Hotkey}  →  전체 선택 · 번역 · 교체";
        GlossaryBox.Text = string.Join(Environment.NewLine, _settings.CustomGlossary.Select(x => $"{x.Key}={x.Value}"));
        QuickStartPanel.Visibility = _settings.ShowStartupGuide || _dashboardPage == "Guide" ? Visibility.Visible : Visibility.Collapsed;
        ApplyAdvancedModePresentation();
        ApplyProviderPanels();
        RefreshLiteStatus();
        UpdateOcrOptionHelpText();
        UpdateTranslationTestPresentation();
    }

    private void ReadUiIntoSettings(bool preserveRegion = true)
    {
        _settings.ApiKey = ApiKeyBox.Password.Trim();
        _settings.DeepLApiKey = DeepLApiKeyBox.Password.Trim();
        _settings.DeepLxMode = GetTag(DeepLxModeCombo, "Public");
        _settings.AutoStartDockerDesktop = AutoStartDockerCheck.IsChecked == true;
        _settings.StopLocalDlxOnExit = StopDlxOnExitCheck.IsChecked == true;
        _settings.DeepLxAutoFallback = DeepLxFallbackCheck.IsChecked == true;
        _settings.LocalAiModel = LocalAiService.NormalizeModelName(GetTag(LocalModelCombo, LocalAiService.DefaultModelName));
        if (_settings.DeepLxMode == "Docker")
        {
            _settings.DeepLxUrl = DlxDockerService.TranslateUrl;
        }
        else
        {
            _settings.PublicDeepLxUrl = DeepLxUrlBox.Text.Trim();
            _settings.DeepLxUrl = _settings.PublicDeepLxUrl;
        }
        _settings.TranslationProvider = GetTag(TranslationProviderCombo, "Hybrid");
        _settings.ApiBaseUrl = _settings.TranslationProvider is "Ollama" or "Hybrid" ? LocalAiService.OpenAiBaseUrl : "https://api.openai.com/v1";
        _settings.Model = _settings.TranslationProvider is "Ollama" or "Hybrid" ? _settings.LocalAiModel : "gpt-4o-mini";
        _settings.SendTargetLanguage = GetTag(SendTargetCombo, "EN");
        _settings.OverlayTargetLanguage = GetTag(OverlayTargetCombo, "KO");
        _settings.OverlayDisplaySeconds = int.TryParse(GetTag(OverlayDurationCombo, "15"), out var displaySeconds)
            ? displaySeconds
            : 15;
        _settings.OcrStabilizationMs = int.TryParse(GetTag(OcrStabilityCombo, "350"), out var stabilizationMs)
            ? stabilizationMs
            : 350;
        _settings.OcrChatFilterMode = GetTag(OcrChatFilterCombo, GameChatFilterService.BriefingMode);
        _settings.OcrAutoEnhance = OcrAutoEnhanceCheck.IsChecked == true;
        _settings.OcrTwoFrameConsensus = OcrConsensusCheck.IsChecked == true;
        _settings.AutoSwitchGameProfile = AutoSwitchGameProfileCheck.IsChecked == true;
        _settings.OverlayFontSize = Math.Round(Math.Clamp(OverlayFontSizeSlider.Value, 11, 32));
        _settings.OcrLanguages = ReadOcrLanguages();
        _settings.OcrLanguage = _settings.OcrLanguages.Count == 1 ? _settings.OcrLanguages[0] : "AUTO";
        _settings.Game = GetTag(GameCombo, "Auto");
        _settings.Map = GetTag(MapCombo, "Auto");
        _settings.MapsByGame[_settings.Game] = _settings.Map;
        _settings.ServerRegion = GameTranslationPrompt.NormalizeRegion(GetTag(ServerRegionCombo, "Auto"));
        _activeRegionGame = _settings.Game;
        _settings.Hotkey = string.IsNullOrWhiteSpace(HotkeyBox.Text) ? "\\" : HotkeyBox.Text;
        _settings.OverlayClickThrough = OverlayLockCheck.IsChecked == true;
        _settings.OverlayBackgroundOpacity = 1 - Math.Clamp(OverlayTransparencySlider.Value, 0, 100) / 100d;
        _settings.OverlayBorderOpacity = 1 - Math.Clamp(OverlayBorderTransparencySlider.Value, 0, 100) / 100d;
        StoreOverlayBounds();
        _settings.CustomGlossary = ParseGlossary(GlossaryBox.Text);
        if (preserveRegion) StoreCurrentRegionProfile();
    }

    private void RefreshMapOptions(string game, string selectedMap)
    {
        _updatingMapOptions = true;
        try
        {
            MapCombo.Items.Clear();
            foreach (var map in _glossary.GetMaps(game))
            {
                MapCombo.Items.Add(new ComboBoxItem
                {
                    Tag = map,
                    Content = new TextBlock
                    {
                        Text = map == "Auto" ? "맵 자동 · 공통" : map,
                        Foreground = Brushes.Black
                    }
                });
            }
            SetComboByTag(MapCombo, selectedMap);
            _settings.Map = GetTag(MapCombo, "Auto");
        }
        finally
        {
            _updatingMapOptions = false;
        }
    }

    private List<string> ReadOcrLanguages(bool updateUiWhenEmpty = true)
    {
        var languages = new List<string>(3);
        if (OcrEnCheck?.IsChecked == true) languages.Add("EN");
        if (OcrJpCheck?.IsChecked == true) languages.Add("JP");
        if (OcrKoCheck?.IsChecked == true) languages.Add("KO");
        if (languages.Count > 0) return languages;

        if (!updateUiWhenEmpty) return languages;
        if (OcrEnCheck is not null) OcrEnCheck.IsChecked = true;
        languages.Add("EN");
        return languages;
    }

    private void StoreCurrentRegionProfile()
    {
        if (!_settings.CaptureRegion.IsValid) return;
        var context = GetCaptureReferenceContext(_activeRegionGame);
        var contextKey = OcrProfileValidationService.ProfileKey(_activeRegionGame, context.Bounds);
        var key = string.IsNullOrWhiteSpace(_loadedRegionProfileKey) ? contextKey : _loadedRegionProfileKey;
        _settings.CaptureRegionsByGame[key] = _settings.CaptureRegion.Clone();
        if (!context.GameDetected && !_activeRegionGame.Equals("Auto", StringComparison.OrdinalIgnoreCase) &&
            _settings.CaptureProfileMetadataByGame.ContainsKey(key)) return;
        if (!key.Equals(contextKey, StringComparison.OrdinalIgnoreCase) &&
            _settings.CaptureProfileMetadataByGame.ContainsKey(key)) return;
        _settings.CaptureProfileMetadataByGame[key] = new CaptureProfileMetadata
        {
            ReferenceX = context.Bounds.X,
            ReferenceY = context.Bounds.Y,
            ReferenceWidth = context.Bounds.Width,
            ReferenceHeight = context.Bounds.Height,
            Dpi = context.Dpi,
            WindowMode = context.WindowMode,
            SavedAtUtc = DateTime.UtcNow
        };
    }

    private void LoadRegionProfile(string game)
    {
        var context = GetCaptureReferenceContext(game);
        var key = OcrProfileValidationService.ProfileKey(game, context.Bounds);
        var profileChanged = !_loadedRegionProfileKey.Equals(key, StringComparison.OrdinalIgnoreCase);
        _loadedRegionProfileKey = key;
        if (profileChanged)
        {
            _ocrEnhancementEvaluationCounter = 0;
            _ocrQualityDropFrames = 0;
            _averageCaptureMs = _averageRecognitionMs = _averageConsensusMs = _averageTranslationMs = _averageOcrPipelineMs = 0;
            _adaptiveOcrMode = "Balanced";
            UpdateOcrPerformanceText();
        }
        if (_settings.CaptureRegionsByGame.TryGetValue(key, out var exactRegion))
        {
            _settings.CaptureRegion = exactRegion.Clone();
            if (_settings.CaptureProfileMetadataByGame.TryGetValue(key, out var metadata) &&
                metadata.ReferenceWidth == context.Bounds.Width && metadata.ReferenceHeight == context.Bounds.Height &&
                metadata.Dpi == context.Dpi &&
                string.Equals(metadata.WindowMode, context.WindowMode, StringComparison.OrdinalIgnoreCase) &&
                (metadata.ReferenceX != context.Bounds.X || metadata.ReferenceY != context.Bounds.Y))
            {
                _settings.CaptureRegion.X += context.Bounds.X - metadata.ReferenceX;
                _settings.CaptureRegion.Y += context.Bounds.Y - metadata.ReferenceY;
                StoreCurrentRegionProfile();
            }
        }
        else if (_settings.CaptureRegionsByGame.TryGetValue(game, out var legacyRegion) &&
                 context.Bounds.Contains(legacyRegion.ToRectangle()))
        {
            _settings.CaptureRegion = legacyRegion.Clone();
            StoreCurrentRegionProfile();
        }
        else
        {
            _settings.CaptureRegion = new CaptureRegion();
        }
        UpdateRegionText();
    }

    private void UpdateRegionText()
    {
        var reference = GetCaptureReferenceBounds(_activeRegionGame);
        RegionText.Text = _settings.CaptureRegion.IsValid
            ? $"{GameDisplayName(_activeRegionGame)} · {reference.Width}×{reference.Height} · {RegionDescription(_settings.CaptureRegion)}"
            : $"{GameDisplayName(_activeRegionGame)} · {reference.Width}×{reference.Height} · 저장된 영역 없음";
    }

    private string RegionProfileKey(string game)
    {
        var reference = GetCaptureReferenceBounds(game);
        return OcrProfileValidationService.ProfileKey(game, reference);
    }

    private OcrProfileValidationResult ValidateCurrentOcrProfile()
    {
        var context = GetCaptureReferenceContext(_activeRegionGame);
        return OcrProfileValidationService.Validate(_activeRegionGame, _settings, context.Bounds,
            context.Dpi, context.WindowMode, context.GameDetected);
    }

    private (System.Drawing.Rectangle Bounds, uint Dpi, string WindowMode, bool GameDetected) GetCaptureReferenceContext(string game)
    {
        var detected = GameWindowDetectionService.Detect(game);
        if (detected is { ClientBounds.Width: >= 640, ClientBounds.Height: >= 480 })
            return (detected.ClientBounds, detected.Dpi, detected.WindowMode, true);

        var monitor = GetCurrentMonitorRectangle();
        uint dpi = 96;
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero) dpi = NativeMethods.GetDpiForWindow(hwnd);
        }
        catch { }
        if (dpi == 0) dpi = 96;
        return (monitor, dpi, "Unknown", game.Equals("Auto", StringComparison.OrdinalIgnoreCase));
    }

    private void HandleForegroundProfileContextChanged(GameWindowInfo info)
    {
        if (!_activeRegionGame.Equals(info.Game, StringComparison.OrdinalIgnoreCase)) return;
        LoadRegionProfile(info.Game);
        var validation = ValidateCurrentOcrProfile();
        if (!validation.CanUse && _ocrCancellation is not null) StopOcr();

        if (validation.CanUse)
            SetStatus("OCR 프로필 자동 보정", $"게임 창 위치·표시 환경 변경을 반영했습니다. {validation.Message}");
        else
            SetStatus("OCR 프로필 확인 필요", validation.Message, true);

        _diagnosticLog.Record("ocr_profile_changed", validation.CanUse ? "adjusted" : "invalid",
            new Dictionary<string, object?>
            {
                ["game"] = info.Game,
                ["profile"] = validation.ProfileKey,
                ["reason"] = validation.Validity.ToString()
            });
        _settingsService.Save(_settings);
        _ = RefreshQuickStartGuideAsync();
    }

    private System.Drawing.Rectangle GetCaptureReferenceBounds(string game)
    {
        var detected = GameWindowDetectionService.Detect(game);
        return detected is not null && detected.ClientBounds.Width >= 640 && detected.ClientBounds.Height >= 480
            ? detected.ClientBounds
            : GetCurrentMonitorRectangle();
    }

    private System.Drawing.Rectangle GetCurrentMonitorRectangle()
    {
        var monitor = GetCurrentMonitorBounds();
        return new System.Drawing.Rectangle(monitor.Left, monitor.Top, monitor.Width, monitor.Height);
    }

    private (int Left, int Top, int Width, int Height) GetCurrentMonitorBounds()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            var monitorHandle = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MonitorDefaultToNearest);
            var info = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            if (monitorHandle != IntPtr.Zero && NativeMethods.GetMonitorInfo(monitorHandle, ref info))
                return (info.rcMonitor.Left, info.rcMonitor.Top,
                    info.rcMonitor.Right - info.rcMonitor.Left, info.rcMonitor.Bottom - info.rcMonitor.Top);
        }

        return (0, 0, NativeMethods.GetSystemMetrics(0), NativeMethods.GetSystemMetrics(1));
    }

    private static string GameDisplayName(string game) => game.Equals("Auto", StringComparison.OrdinalIgnoreCase)
        ? "공통 게임"
        : game;

    private static Dictionary<string, string> ParseGlossary(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var index = line.IndexOf('=');
            if (index <= 0 || index == line.Length - 1) continue;
            var key = line[..index].Trim();
            var value = line[(index + 1)..].Trim();
            if (key.Length > 0 && value.Length > 0) result[key] = value;
        }
        return result;
    }

    private void SetStatus(string top, string detail, bool error = false)
    {
        TopStatusText.Text = top;
        TopStatusText.Foreground = new SolidColorBrush(error ? Color.FromRgb(180, 35, 58) : Color.FromRgb(4, 120, 87));
        StatusDot.Fill = new SolidColorBrush(error ? Color.FromRgb(225, 29, 72) : Color.FromRgb(5, 150, 105));
        DetailStatusText.Text = detail;
    }

    private static string FriendlyMessage(Exception ex)
    {
        if (ex is System.Net.Http.HttpRequestException) return "번역 서버에 연결하지 못했습니다. 서버 주소와 인터넷 연결을 확인해 주세요.";
        return ex.Message;
    }

    private static string NormalizeOcr(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
            if (!char.IsWhiteSpace(ch)) builder.Append(char.ToLowerInvariant(ch));
        return builder.ToString();
    }

    private static IReadOnlyList<OcrLine> ExtractOcrLines(OcrReadResult result)
    {
        if (result.PositionedLines is { Count: > 0 })
            return result.PositionedLines
                .Select(line => new OcrLine(line.Text.Trim(), NormalizeOcr(line.Text), line.Y, line.Height))
                .Where(line => line.Normalized.Length >= 2)
                .OrderBy(line => line.Y)
                .ToArray();

        return result.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => new OcrLine(line.Trim(), NormalizeOcr(line), -1, 0))
            .Where(line => line.Normalized.Length >= 2)
            .ToArray();
    }

    private static OcrLine[] SelectNewestPositionedLines(OcrLine[] lines)
    {
        var positioned = lines.Where(line => line.Y >= 0 && line.Height > 0).ToArray();
        if (positioned.Length <= 1) return lines;
        var bottomLine = positioned.Max(line => line.Y + line.Height);
        var averageHeight = positioned.Average(line => line.Height);
        var newestBandHeight = Math.Max(48, averageHeight * 4.2);
        return positioned
            .Where(line => line.Y + line.Height >= bottomLine - newestBandHeight)
            .OrderBy(line => line.Y)
            .TakeLast(4)
            .ToArray();
    }

    private void ServerRegion_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || _settings is null) return;
        _settings.ServerRegion = GameTranslationPrompt.NormalizeRegion(GetTag(ServerRegionCombo, "Auto"));
    }

    private static string DetectTextLanguage(string text, string fallback)
    {
        text = ChatTextSanitizer.ContentForLanguageDetection(text);
        if (text.Any(ch => ch is >= '\uAC00' and <= '\uD7AF')) return "KO";
        if (text.Any(ch => ch is (>= '\u3040' and <= '\u30FF') or (>= '\u3400' and <= '\u9FFF'))) return "JP";
        if (ChatTextSanitizer.LooksLikeRomanizedJapanese(text)) return "JP";
        if (text.Any(char.IsLetter)) return "EN";
        return fallback;
    }

    private static string Shorten(string value, int max) => value.Length <= max ? value : value[..max] + "…";
    private static string ShortenLines(string value, int max) => Shorten(value.Trim(), max);
    private sealed record OcrLine(string Original, string Normalized, int Y, int Height);
    private sealed record TranslationQueueItem(string Text, int SkippedCount);
    private sealed record TranslationBatchResult(string Text, bool UsedSafeBriefing);
    private sealed record SystemDiagnosticReport(int BlockingIssues, int RepairableIssues, IReadOnlyList<string> Lines);
    private static string RegionDescription(CaptureRegion r) => $"선택됨 · {r.Width} × {r.Height}px  ({r.X}, {r.Y})";

    private static string GetTag(ComboBox combo, string fallback) =>
        (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;

    private static void SetComboByTag(ComboBox combo, string value)
    {
        combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(x => string.Equals(x.Tag?.ToString(), value, StringComparison.OrdinalIgnoreCase))
                             ?? combo.Items[0];
    }

    private void Window_OnClosing(object? sender, CancelEventArgs e)
    {
        _ocrCancellation?.Cancel();
        _translationQueueCancellation?.Cancel();
        _gameHotkeyTimer?.Stop();
        _liteMemoryTimer?.Stop();
        _hotkey?.Dispose();
        _localAi.UnloadPinnedModelsOnExit();
        _lite.Dispose();
        StoreOverlayBounds();
        _settingsService.Save(_settings);
        _overlay?.Close();
        _dlxDocker.StopManagedContainerOnExit(StopDlxOnExitCheck?.IsChecked == true);
    }
}
