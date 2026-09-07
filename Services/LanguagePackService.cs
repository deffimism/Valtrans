using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Windows.Globalization;
using Windows.Media.Ocr;

namespace Valtrans.Services;

public sealed class LanguagePackService
{
    public static string GetLanguageTag(string code) => code switch
    {
        "JP" => "ja-JP",
        "KO" => "ko-KR",
        _ => "en-US"
    };

    public static string GetLanguageName(string code) => code switch
    {
        "AUTO" => "영어·일본어·한국어 자동 감지",
        "JP" => "일본어",
        "KO" => "한국어",
        _ => "영어"
    };

    public bool IsInstalled(string code)
    {
        if (code.Equals("AUTO", StringComparison.OrdinalIgnoreCase))
            return IsInstalled("EN") && IsInstalled("JP") && IsInstalled("KO");
        var language = new Language(GetLanguageTag(code));
        return OcrEngine.IsLanguageSupported(language) && OcrEngine.TryCreateFromLanguage(language) is not null;
    }

    public IReadOnlyDictionary<string, bool> GetSupportedLanguageStatus() => new Dictionary<string, bool>
    {
        ["EN"] = IsInstalled("EN"),
        ["JP"] = IsInstalled("JP"),
        ["KO"] = IsInstalled("KO")
    };

    public async Task<LanguagePackInstallResult> InstallAsync(string code, CancellationToken cancellationToken = default)
    {
        if (IsInstalled(code)) return new LanguagePackInstallResult(true, false, "이미 설치되어 있습니다.");

        var tag = GetLanguageTag(code);
        var dismPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "dism.exe");
        var arguments = $"/Online /Add-Capability /CapabilityName:Language.Basic~~~{tag}~0.0.1.0 " +
                        $"/CapabilityName:Language.OCR~~~{tag}~0.0.1.0 /NoRestart";
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = dismPath,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas",
                // Keep DISM visible. A hidden elevated process made the app look
                // unresponsive immediately after the UAC prompt was accepted.
                WindowStyle = ProcessWindowStyle.Normal
            }) ?? throw new InvalidOperationException("Windows 언어 기능 설치를 시작하지 못했습니다.");

            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode is not (0 or 3010))
                return new LanguagePackInstallResult(false, false, $"Windows 언어 기능 설치가 실패했습니다. DISM 코드: {process.ExitCode}");

            // The OCR language list can take a moment to refresh after DISM exits.
            for (var i = 0; i < 8; i++)
            {
                await Task.Delay(500, cancellationToken);
                if (IsInstalled(code))
                    return new LanguagePackInstallResult(true, process.ExitCode == 3010,
                        process.ExitCode == 3010 ? "설치되었습니다. Windows 재시작이 필요할 수 있습니다." : "설치되었습니다.");
            }

            // DISM completed successfully. Windows.Media.Ocr can keep the old
            // recognizer list cached until this process (or Windows) restarts.
            return new LanguagePackInstallResult(true, true,
                "설치는 완료됐습니다. 앱을 다시 실행한 뒤에도 표시되지 않으면 Windows를 재시작해 주세요.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new LanguagePackInstallResult(false, false, "관리자 승인이 취소되었습니다.");
        }
    }
}

public sealed record LanguagePackInstallResult(bool Installed, bool RestartRequired, string Message);
