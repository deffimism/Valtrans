using System.IO;
using System.Windows;
using System.Windows.Threading;
using Valtrans.Models;
using Valtrans.Services;

namespace Valtrans;

public partial class MainWindow
{
    private TestRunReport? _testRunReport;
    private TestScenarioFile? _testScenario;
    private TestArenaReadyFile? _testArenaReady;
    private DispatcherTimer? _testModeTimer;
    private DateTime _testModeStartedUtc;

    private async void ConfigureTestModeIfNeeded()
    {
        if (!TestModeContext.Enabled) return;

        _settings.ShowStartupGuide = false;
        _settings.OcrEngine = TestModeContext.OcrEngine;
        _settings.FastOcrRuntime = FastOcrService.FindRuntime();
        _settings.TranslationProvider = "Hybrid";
        _settings.Game = "VALORANT";
        _settings.OcrChatFilterMode = GameChatFilterService.AllMode;
        _settings.EnableMessageTrace = true;
        _settings.SaveTraceErrorSamples = true;
        _settings.OcrIntervalMs = 600;
        _settings.OcrStabilizationMs = 0;
        _settings.OcrTwoFrameConsensus = false;
        _settings.DualRegionOcr = false;
        _settings.OcrLanguages = new List<string> { "JP", "EN", "KO" };
        _settings.OcrAutoEnhance = true;
        ApplySettingsToUi();

        _testScenario = TestScenarioFile.Load(TestModeContext.ScenarioPath);
        if (_testScenario.Tags.Any(tag => tag.Equals("zh", StringComparison.OrdinalIgnoreCase) ||
                                            tag.Equals("mixed", StringComparison.OrdinalIgnoreCase)))
        {
            if (FastOcrService.HasRuntimeFiles(_settings.FastOcrRuntime) &&
                TestModeContext.OcrEngine == "Windows")
                _settings.OcrEngine = "Fast";
            else if (FastOcrService.HasRuntimeFiles(_settings.FastOcrRuntime) &&
                     PaddleOcrService.HasRuntimeFiles(_settings.PaddleOcrRuntime) &&
                     TestModeContext.OcrEngine == "Windows")
                _settings.OcrEngine = "Hybrid";
            foreach (var expectation in _testScenario.Expectations.Where(item => MixedLanguageDetector.IsMixed(item.Source)))
                _pipelineLog.Record("test_mode", "mixed_language_case", new Dictionary<string, object?>
                {
                    ["source"] = expectation.Source,
                    ["script"] = MixedLanguageDetector.DetectPrimaryScript(expectation.Source)
                });
        }
        _testRunReport = new TestRunReport
        {
            Scenario = _testScenario.Scenario,
            StartedUtc = DateTime.UtcNow,
            Cases = _testScenario.Expectations.Select(expectation => new TestRunCaseResult
            {
                Source = expectation.Source,
                Status = "PENDING"
            }).ToList()
        };
        _testModeStartedUtc = DateTime.UtcNow;
        _messageTrace.TraceCompleted += TestMode_OnTraceCompleted;

        var ready = await WaitForArenaReadyAsync(TestModeContext.ReadyFilePath, TimeSpan.FromSeconds(30));
        if (ready is null)
        {
            FinishTestMode("FAIL", "Arena ready file not found");
            return;
        }

        _testArenaReady = ready;
        _loadedRegionProfileKey = RegionProfileKey(_settings.Game);
        if (!TryRefreshArenaCaptureRegion())
        {
            FinishTestMode("FAIL", "Arena chat capture region could not be resolved");
            return;
        }
        _settings.LatestOcrRegions[_loadedRegionProfileKey] = OcrRegionRecommendationService.DefaultValorantLatestRegion();
        StoreCurrentRegionProfile();
        RegionText.Text = RegionDescription(_settings.CaptureRegion);
        SetStatus("Test Mode", $"Arena capture · {ready.Scenario} · {ready.ChatRegion.Width}x{ready.ChatRegion.Height}");

        if (!await StartTestModeOcrAsync())
        {
            FinishTestMode("FAIL", "test-mode OCR failed to start");
            return;
        }
        try
        {
            var ocrReady = TestModeContext.OcrReadySignalPath;
            if (!string.IsNullOrWhiteSpace(ocrReady))
                File.WriteAllText(ocrReady, DateTime.UtcNow.ToString("O"));
        }
        catch { }

        _testModeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _testModeTimer.Tick += (_, _) => CheckTestModeTimeout();
        _testModeTimer.Start();
    }

    private async Task<bool> StartTestModeOcrAsync()
    {
        if (_paddleOperation is not null || _ocrTask is { IsCompleted: false })
            return false;
        if (!_settings.CaptureRegion.IsValid) return false;

        if (_settings.OcrEngine == "Fast")
        {
            if (!FastOcrService.HasRuntimeFiles(_settings.FastOcrRuntime))
            {
                FinishTestMode("FAIL", "Fast OCR runtime missing. Run Ocr/Setup-FastOcr.ps1");
                return false;
            }
            if (!await PrepareFastOcrAsync())
            {
                FinishTestMode("FAIL", "Fast OCR prepare failed");
                return false;
            }
        }
        else if (_settings.OcrEngine == "Hybrid")
        {
            if (!FastOcrService.HasRuntimeFiles(_settings.FastOcrRuntime) ||
                !PaddleOcrService.HasRuntimeFiles(_settings.PaddleOcrRuntime))
            {
                FinishTestMode("FAIL", "Hybrid OCR requires Fast + Paddle runtimes");
                return false;
            }
            if (!await PrepareHybridOcrAsync())
            {
                FinishTestMode("FAIL", "Hybrid OCR prepare failed");
                return false;
            }
        }
        else if (_settings.OcrEngine == "Paddle")
        {
            if (!await PreparePaddleOcrAsync())
            {
                FinishTestMode("FAIL", "Paddle OCR prepare failed");
                return false;
            }
        }
        else
        {
            RestrictOcrLanguagesToInstalledPacks();
            if (!_languagePacks.IsInstalled("JP"))
            {
                FinishTestMode("FAIL", "Japanese Windows OCR language pack is required for smoke_basic_001");
                return false;
            }
            if (_settings.OcrLanguages.Count == 0)
                _settings.OcrLanguages = new List<string> { "EN" };
        }

        EnsureOverlay();
        SetOverlayVisible(false);
        _lastOcrText = "";
        _lastOcrFrameHash = null;
        _unchangedOcrFrames = 0;
        _ocrConsensusRejectedFrames = 0;
        _ocrBaselinePending = false;
        _chatInputVisibleSince = null;
        _chatInputWasVisible = false;
        _ocrDirtySince = null;
        _ocrPendingHash = null;
        _previousOcrLines.Clear();
        _lastHandledLatestLine = "";
        _recentOcrBodies.Clear();
        _ocrCancellation = new CancellationTokenSource();
        var sessionToken = _ocrCancellation.Token;
        _incomingQueue = new IncomingOcrQueue(ProcessIncomingLineAsync, RecordOcrStage, sessionToken);
        OcrToggleButton.Content = "OCR 중지";
        _pipelineLog.Record("session", "ocr_started", new Dictionary<string, object?>
        {
            ["mode"] = "test_mode",
            ["scenario"] = _testScenario?.Scenario ?? ""
        });
        RecordOcrStage("Test Mode · Arena capture OCR 시작");
        _ocrTask = Task.Run(() => RunOcrLoopAsync(sessionToken), sessionToken);
        await Task.Delay(250);
        return _ocrTask is not null;
    }

    private bool TryRefreshArenaCaptureRegion(bool updateUi = true)
    {
        if (_testArenaReady is null) return false;
        if (!TestArenaCaptureService.TryResolveChatRegion(_testArenaReady, TestModeContext.ArenaWindowTitle,
                out var region))
            region = _testArenaReady.ChatRegion.Clone();
        if (!region.IsValid) return false;
        _settings.CaptureRegion = region;
        if (updateUi)
        {
            if (Dispatcher.CheckAccess()) RegionText.Text = RegionDescription(region);
            else Dispatcher.BeginInvoke(() => RegionText.Text = RegionDescription(region));
        }
        return true;
    }

    private void RestrictOcrLanguagesToInstalledPacks()
    {
        var installed = _settings.OcrLanguages
            .Where(code => _languagePacks.IsInstalled(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (installed.Count > 0)
            _settings.OcrLanguages = installed;
    }

    private static async Task<TestArenaReadyFile?> WaitForArenaReadyAsync(string path, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path))
            {
                try { return TestArenaReadyFile.Load(path); }
                catch { }
            }
            await Task.Delay(250);
        }
        return null;
    }

    private void TestMode_OnTraceCompleted(object? sender, MessageTraceRecord record)
    {
        if (_testRunReport is null || _testScenario is null) return;
        Dispatcher.BeginInvoke(() =>
        {
            var expectation = _testScenario.Expectations.FirstOrDefault(item =>
            {
                var pending = _testRunReport.Cases.FirstOrDefault(caseItem => caseItem.Source == item.Source);
                if (pending is null || pending.Status == "PASS") return false;
                return SourceMatches(item.Source, record.Ocr.Raw) ||
                       SourceMatches(item.Source, record.Normalization.Text) ||
                       SourceMatches(item.Source, record.Translation.Input) ||
                       expectationOutputMatches(item, record.Translation.Output);
            });
            if (expectation is null) return;

            var caseResult = _testRunReport.Cases.FirstOrDefault(item => item.Source == expectation.Source);
            if (caseResult is null || caseResult.Status == "PASS") return;

            var accepted = expectationOutputMatches(expectation, record.Translation.Output);
            caseResult.TraceId = record.Id;
            caseResult.OcrRaw = record.Ocr.Raw;
            caseResult.TranslationOutput = record.Translation.Output;
            caseResult.TotalLatencyMs = record.Latency.TotalMs;
            caseResult.Status = accepted ? "PASS" : "FAIL";
            caseResult.Detail = accepted
                ? "translation matched accepted list"
                : $"expected one of [{string.Join(", ", expectation.AcceptedTranslations)}]";

            if (_testRunReport.Cases.Count > 0 && _testRunReport.Cases.All(item => item.Status == "PASS"))
                FinishTestMode("PASS", null);
        });
    }

    private void CheckTestModeTimeout()
    {
        if (_testRunReport is null) return;
        if (DateTime.UtcNow - _testModeStartedUtc < TimeSpan.FromSeconds(TestModeContext.TimeoutSeconds)) return;
        FinishTestMode("FAIL", "test-mode timeout");
    }

    private void FinishTestMode(string status, string? failure)
    {
        if (_testRunReport is null) return;
        _testModeTimer?.Stop();
        _messageTrace.TraceCompleted -= TestMode_OnTraceCompleted;

        _testRunReport.Status = status;
        _testRunReport.CompletedUtc = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(failure))
            _testRunReport.Failures.Add(failure);

        foreach (var pending in _testRunReport.Cases.Where(item => item.Status == "PENDING"))
        {
            pending.Status = "FAIL";
            pending.Detail = "no matching trace before timeout";
            _testRunReport.Failures.Add($"missing result for '{pending.Source}'");
        }

        var latencies = _testRunReport.Cases.Where(item => item.TotalLatencyMs.HasValue).Select(item => item.TotalLatencyMs!.Value).ToArray();
        _testRunReport.Metrics = new TestRunMetrics
        {
            Passed = _testRunReport.Cases.Count(item => item.Status == "PASS"),
            Failed = _testRunReport.Cases.Count(item => item.Status == "FAIL"),
            Pending = _testRunReport.Cases.Count(item => item.Status == "PENDING"),
            TotalP50Ms = latencies.Length == 0 ? null : Percentile(latencies, 0.5)
        };
        if (!string.IsNullOrWhiteSpace(_lastOcrStage))
            _testRunReport.Failures.Add($"lastOcrStage={_lastOcrStage}");

        TestRunReportWriter.Write(TestModeContext.OutputPath, _testRunReport);
        TestModeExitCode.ExitCode = status == "PASS" ? 0 : 1;
        try { StopOcr(); } catch { }
        Application.Current.Shutdown();
    }

    private static bool SourceMatches(string expected, string actual)
    {
        if (string.IsNullOrWhiteSpace(actual)) return false;
        var left = NormalizeForTestCompare(expected);
        var right = NormalizeForTestCompare(actual);
        return left.Equals(right, StringComparison.OrdinalIgnoreCase) ||
               right.Contains(left, StringComparison.OrdinalIgnoreCase) ||
               left.Contains(right, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeForTestCompare(string value) =>
        string.Concat(value.Trim().Normalize(System.Text.NormalizationForm.FormKC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim('\'', '"', '.', '!', '?', '。', '！', '？')));

    private static bool expectationOutputMatches(TestScenarioExpectation expectation, string actual) =>
        expectation.AcceptedTranslations.Any(candidate =>
            string.Equals(NormalizeForTestCompare(candidate), NormalizeForTestCompare(actual),
                StringComparison.OrdinalIgnoreCase));

    private static double Percentile(double[] values, double percentile)
    {
        if (values.Length == 0) return 0;
        Array.Sort(values);
        var index = (int)Math.Round((values.Length - 1) * percentile);
        return Math.Round(values[Math.Clamp(index, 0, values.Length - 1)], 1);
    }
}
