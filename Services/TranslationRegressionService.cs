using Valtrans.Models;

namespace Valtrans.Services;

public sealed class TranslationRegressionService
{
    private readonly GlossaryService _glossary;

    public TranslationRegressionService(GlossaryService glossary) => _glossary = glossary;

    public TranslationRegressionReport Run(AppSettings settings)
    {
        var cases = new[]
        {
            new RegressionCase("인사", "안녕하세요", "EN", "hello"),
            new RegressionCase("FPS 약어 nt", "nt", "KO", "아깝다"),
            new RegressionCase("FPS 약어 mb", "mb", "EN", "my bad"),
            new RegressionCase("방향 경고", "왼쪽 조심해", "EN", "watch left"),
            new RegressionCase("방향 금지", "왼쪽으로 가지 마", "EN", "don't go left"),
            new RegressionCase("일본어 방향", "左見て", "EN", "watch left"),
            new RegressionCase("일본어 방향 우", "右見て", "EN", "watch right"),
            new RegressionCase("일본어 적·인원", "左に敵が一人いる", "KO", "왼쪽 적 1명"),
            new RegressionCase("한국어 적 존재", "왼쪽에 적이 있다", "EN", "enemy left"),
            new RegressionCase("헤더 제거·일본어", "(팀) Player: わかりました", "KO", "확인"),
            new RegressionCase("인원·콜아웃", "B 헤븐에 두 명", "EN", "2 B Heaven"),
            new RegressionCase("사이트 러시 JP", "Bラッシュ", "KO", "B 러시"),
            new RegressionCase("사이트 러시 EN", "Bラッシュ", "EN", "B rush"),
            new RegressionCase("중국어 인원·위치", "A小两个", "EN", "2 A Short"),
            new RegressionCase("중국어 체력", "jett残血", "EN", "Jett low"),
            new RegressionCase("혼합 일본어·중국어", "jett残血", "KO", "Jett 체력 낮음"),
            new RegressionCase("섬광 대기", "내가 섬광 쓸 때까지 기다려", "EN", "wait until I flash"),
            new RegressionCase("일본어 부정", "右に敵はいない", "KO", "오른쪽 · 없음"),
            new RegressionCase("인원·위치", "미드에 적 2명 있어", "EN", "2 mid"),
            new RegressionCase("행동 부정", "로테하지 마", "EN", "don't rotate"),
            new RegressionCase("불확실성", "아마 미드", "EN", "maybe · Mid"),
            new RegressionCase("캐릭터·피해", "제트 120", "EN", "Jett · 120 dmg"),
            new RegressionCase("체력 콜", "one shot", "KO", "딸피"),
            new RegressionCase("방향 경고 JP", "뒤 조심", "JP", "裏警戒"),
            new RegressionCase("부정 보호", "no one mid", "EN", "Mid · none")
        };

        var results = cases.Select(test =>
        {
            var actual = TranslateCore(test.Source, test.TargetLanguage, settings);
            var passed = string.Equals(Normalize(actual), Normalize(test.Expected), StringComparison.OrdinalIgnoreCase);
            return new TranslationRegressionResult(test.Name, passed, passed ? "정상" : $"예상 규칙 불일치 ({actual})");
        }).ToList();

        var safetyCases = new[]
        {
            new SafetyCase("안전 보정 · 방향", "왼쪽 조심해", "be careful", "EN", "watch left"),
            new SafetyCase("안전 보정 · 인원", "미드에 적 2명 있어", "mid", "EN", "2 mid"),
            new SafetyCase("안전 보정 · 피해량", "제트 120", "Jett", "EN", "Jett · 120 dmg"),
            new SafetyCase("안전 보정 · 부정 방향", "오른쪽에 적 없어", "none", "KO", "오른쪽 · 없음")
        };
        foreach (var test in safetyCases)
        {
            var actual = TranslationFactGuard.Apply(test.Source, test.BrokenTranslation, test.TargetLanguage,
                settings, _glossary).Text;
            var passed = string.Equals(Normalize(actual), Normalize(test.Expected), StringComparison.OrdinalIgnoreCase);
            results.Add(new TranslationRegressionResult(test.Name, passed, passed ? "정상" : $"안전 보정 불일치 ({actual})"));
        }

        var safeBriefingCases = new[]
        {
            new SafeBriefingCase("지연 대체 · 방향", "왼쪽 조심해", "EN", true, "watch left"),
            new SafeBriefingCase("지연 대체 · 인원", "미드에 적 2명 있어", "EN", true, "2 mid"),
            new SafeBriefingCase("지연 대체 · 일반 대화 제외", "안녕하세요", "EN", false, "")
        };
        foreach (var test in safeBriefingCases)
        {
            var available = TranslationFactGuard.TryBuildSafeBriefing(test.Source, test.TargetLanguage, settings,
                _glossary, out var actual);
            var passed = available == test.ExpectedAvailable &&
                         (!available || string.Equals(Normalize(actual), Normalize(test.Expected),
                             StringComparison.OrdinalIgnoreCase));
            results.Add(new TranslationRegressionResult(test.Name, passed,
                passed ? "정상" : $"지연 대체 규칙 불일치 ({actual})"));
        }

        var slangCases = new[]
        {
            new SlangCase("영어 안심 약어", "NP!!!", "KO", "Auto", "괜찮아"),
            new SlangCase("일본어 격려", "ドンマイ", "EN", "Auto", "all good"),
            new SlangCase("일본어 로마자 격려", "donmai", "KO", "Auto", "괜찮아"),
            new SlangCase("한국어 자음 약어", "ㄱㅊ", "JP", "Auto", "大丈夫"),
            new SlangCase("합류 약어", "omw", "KO", "Auto", "가는 중"),
            new SlangCase("한국어 축약 콜", "힐좀", "EN", "Auto", "need heal"),
            new SlangCase("부정 축약", "피킹 ㄴㄴ", "EN", "Auto", "don't peek"),
            new SlangCase("영어 구두점 생략", "dont peek", "KO", "Auto", "피킹 금지"),
            new SlangCase("동시 피킹", "double swing", "KO", "Auto", "같이 나가자"),
            new SlangCase("교환 콜", "trade me", "KO", "Auto", "내 킬 교환해 줘"),
            new SlangCase("시간 콜", "play time", "JP", "Auto", "時間を稼いで"),
            new SlangCase("대상 피해 은어", "Jett lit", "KO", "VALORANT", "Jett · 피해 입음"),
            new SlangCase("낮은 체력 정확도", "Jett low", "KO", "VALORANT", "Jett · 체력 낮음"),
            new SlangCase("한국어 대상 은어", "제트 딸피", "EN", "VALORANT", "Jett · one shot"),
            new SlangCase("은어 추정 보존", "maybe Jett lit", "KO", "VALORANT", "추정 · Jett · 피해 입음"),
            new SlangCase("구매 은어", "포바", "EN", "VALORANT", "force buy"),
            new SlangCase("해체 완료 콜", "stick it", "KO", "VALORANT", "해체 끝까지 해")
        };
        foreach (var test in slangCases)
        {
            var scoped = new AppSettings { Game = test.Game };
            var actual = TranslateCore(test.Source, test.TargetLanguage, scoped);
            var passed = string.Equals(Normalize(actual), Normalize(test.Expected), StringComparison.OrdinalIgnoreCase);
            results.Add(new TranslationRegressionResult(test.Name, passed, passed ? "정상" : $"은어 불일치 ({actual})"));
        }

        var expansionCases = new[]
        {
            (Source: "one shot", Expected: "very low HP"),
            (Source: "save me", Expected: "save me"),
            (Source: "you are cracked", Expected: "you are cracked"),
            (Source: "force buy", Expected: "force buy"),
            (Source: "nt Jett", Expected: "nice try Jett"),
            (Source: "nothing defaulted battalion", Expected: "nothing defaulted battalion")
        };
        foreach (var test in expansionCases)
        {
            var actual = _glossary.PrepareForLocalTranslation(test.Source, new AppSettings { Game = "VALORANT" });
            var passed = actual == test.Expected;
            results.Add(new TranslationRegressionResult($"문맥·중복 확장 보호 {test.Source}", passed,
                passed ? "정상" : $"전처리 불일치 ({actual})"));
        }
        foreach (var text in new[] { "Jett not low", "maybe Jett lit but two right", "you are cracked" })
        {
            var passed = !_glossary.TryTranslateStructuredCallout(text, "KO", new AppSettings(), out _);
            results.Add(new TranslationRegressionResult($"복합·부정 은어 모델 위임 {text}", passed,
                passed ? "정상" : "일부 사실만 남기는 규칙 적용됨"));
        }
        var filter = new GameChatFilterService(_glossary);
        foreach (var text in new[] { "omw", "힐좀", "double swing", "포바", "stick it" })
        {
            var passed = filter.Filter(text, GameChatFilterService.StrictMode,
                new AppSettings { Game = "VALORANT" }).Keep;
            results.Add(new TranslationRegressionResult($"OCR 은어 보존 {text}", passed,
                passed ? "정상" : "전술 은어가 필터링됨"));
        }

        return new TranslationRegressionReport(results.Count(result => result.Passed), results.Count, results);
    }

    private string TranslateCore(string source, string targetLanguage, AppSettings settings)
    {
        source = ChatTextSanitizer.StripChatPrefix(source).Trim();
        string translated;
        if (_glossary.TryTranslateExactShortcut(source, targetLanguage, out translated, settings) ||
            _glossary.TryTranslateStructuredCallout(source, targetLanguage, settings, out translated))
            return BriefingTranslationGuard.Apply(source, translated, targetLanguage);

        return BriefingTranslationGuard.Apply(source, source, targetLanguage);
    }

    private static string Normalize(string value) => string.Join(' ',
        value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private sealed record RegressionCase(string Name, string Source, string TargetLanguage, string Expected);
    private sealed record SafetyCase(string Name, string Source, string BrokenTranslation, string TargetLanguage, string Expected);
    private sealed record SafeBriefingCase(string Name, string Source, string TargetLanguage,
        bool ExpectedAvailable, string Expected);
    private sealed record SlangCase(string Name, string Source, string TargetLanguage, string Game, string Expected);
}

public sealed record TranslationRegressionResult(string Name, bool Passed, string Detail);
public sealed record TranslationRegressionReport(int Passed, int Total, IReadOnlyList<TranslationRegressionResult> Results)
{
    public bool Success => Passed == Total;
}
