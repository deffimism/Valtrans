using System.Drawing;
using Valtrans.Models;

namespace Valtrans.Services;

public static class OcrProfileValidationService
{
    public static OcrProfileValidationResult Validate(string game, AppSettings settings,
        Rectangle referenceBounds, uint dpi, string windowMode, bool gameDetected)
    {
        if (!settings.CaptureRegion.IsValid)
            return new OcrProfileValidationResult(OcrProfileValidity.Missing,
                "저장된 OCR 영역이 없습니다.", ProfileKey(game, referenceBounds));

        if (!gameDetected && !game.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            return new OcrProfileValidationResult(OcrProfileValidity.NotVerifiable,
                "게임 창이 닫혀 있어 해상도·창 모드 검증을 보류했습니다.", ProfileKey(game, referenceBounds));

        var key = ProfileKey(game, referenceBounds);
        if (!settings.CaptureRegionsByGame.ContainsKey(key))
            return new OcrProfileValidationResult(OcrProfileValidity.Invalid,
                $"현재 {referenceBounds.Width}×{referenceBounds.Height}용 OCR 영역이 없습니다.", key);

        var region = settings.CaptureRegion.ToRectangle();
        if (!ContainsWithTolerance(referenceBounds, region, 4))
            return new OcrProfileValidationResult(OcrProfileValidity.Invalid,
                "영역이 현재 게임 창 밖에 있습니다. 추천 영역을 새로고침해 주세요.", key);

        if (!settings.CaptureProfileMetadataByGame.TryGetValue(key, out var metadata))
            return new OcrProfileValidationResult(OcrProfileValidity.Valid,
                "현재 해상도와 영역 위치가 정상입니다. 다음 저장부터 DPI·창 모드도 확인합니다.", key);

        if (metadata.ReferenceWidth != referenceBounds.Width || metadata.ReferenceHeight != referenceBounds.Height)
            return new OcrProfileValidationResult(OcrProfileValidity.Invalid,
                "저장 당시와 게임 해상도가 다릅니다.", key);
        if (metadata.Dpi != 0 && dpi != 0 && metadata.Dpi != dpi)
            return new OcrProfileValidationResult(OcrProfileValidity.Invalid,
                $"Windows 배율이 {metadata.Dpi * 100 / 96}%에서 {dpi * 100 / 96}%로 바뀌었습니다.", key);
        if (!string.Equals(metadata.WindowMode, "Unknown", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(windowMode, "Unknown", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(metadata.WindowMode, windowMode, StringComparison.OrdinalIgnoreCase))
            return new OcrProfileValidationResult(OcrProfileValidity.Invalid,
                "게임 창 모드가 바뀌었습니다. OCR 영역을 다시 확인해 주세요.", key);

        return new OcrProfileValidationResult(OcrProfileValidity.Valid,
            $"{referenceBounds.Width}×{referenceBounds.Height} · 배율 {dpi * 100 / 96}% · {DisplayMode(windowMode)}", key);
    }

    public static string ProfileKey(string game, Rectangle referenceBounds) =>
        $"{game}@{referenceBounds.Width}x{referenceBounds.Height}";

    private static bool ContainsWithTolerance(Rectangle outer, Rectangle inner, int tolerance) =>
        inner.Left >= outer.Left - tolerance && inner.Top >= outer.Top - tolerance &&
        inner.Right <= outer.Right + tolerance && inner.Bottom <= outer.Bottom + tolerance;

    public static string DisplayMode(string value) => value switch
    {
        "Borderless" => "전체 화면 창",
        "Windowed" => "창 모드",
        _ => "모드 미확인"
    };
}

public enum OcrProfileValidity { Valid, NotVerifiable, Missing, Invalid }
public sealed record OcrProfileValidationResult(OcrProfileValidity Validity, string Message, string ProfileKey)
{
    public bool CanUse => Validity is OcrProfileValidity.Valid or OcrProfileValidity.NotVerifiable;
}
