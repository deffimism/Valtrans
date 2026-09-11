# Valtrans 다음 작업 인수인계 문서

작성 기준: `v0.4.0-beta` (Phase 0–10 + v0.4 하드닝)  
목적: 에이전트·개발자가 Test Arena E2E, Trace, Baseline, Fast/Hybrid OCR 상태를 빠르게 파악하고 다음 작업을 이어간다.

## v0.4.0 완료 상태 (v0.3.0 기반 +)

| 항목 | 내용 |
|------|------|
| Arena | VALORANT 하단 정렬·인라인 스피커, DPI 보정, production latest-line E2E |
| Region | 비16:9 화면비 보정, alt-tab 프로필 유지 |
| OCR | Windows JP advisory, OCR test 전 엔진, noise heuristics, 3× upscale |
| UX | 오버레이 생략 이유, OCR 다시 준비 |
| Lite | JP↔KO EN pivot, Benchmark-LitePivot.ps1 |
| Gate | 156 unit + Full E2E 6/6 |

## v0.3.0 완료 상태

| Phase | 내용 | 상태 |
|-------|------|------|
| 0 | MessageTrace (OCR/번역 분리 추적) | ✅ |
| 1 | Test Arena MVP + E2E (실제 Capture→OCR) | ✅ |
| 2 | Baseline 수집·비교 | ✅ |
| 3 | Fast OCR (PP-OCRv5) | ✅ |
| 4 | Hybrid OCR (Fast + VL fallback) | ✅ |
| 5 | Critical Fact Validator | ✅ |
| 6 | Message Classifier | ✅ |
| 7 | Chinese/Mixed glossary + scenario | ✅ |
| 8 | Advanced Test Arena (배경/퍼징/DPI/fade) | ✅ |
| 9 | Agent Test Runner + `test.ps1` | ✅ |
| 10 | Translation cache + latest-frame-wins queue | ✅ |
| + | Fast/Hybrid OCR UI, Arena layout, regression gate | ✅ |

## 에이전트 테스트 CLI

```powershell
# 단위 테스트 (156개)
dotnet test test/Valtrans.Tests/Valtrans.Tests.csproj -c Release

# 스모크 (영어 E2E)
.\scripts\test.ps1 -Profile Smoke

# 전체 게이트 (Unit + Smoke + Fuzz + ZhSmoke + Hybrid + Baseline compare)
.\scripts\test.ps1 -Profile Full

# 중국어 혼합 E2E (Fast OCR 필요)
.\scripts\Run-E2EZhSmoke.ps1

# Hybrid E2E (Fast + Paddle VL 필요, 없으면 SKIP)
.\scripts\Run-E2EHybridSmoke.ps1

# Arena fuzz (다중 seed validate, -RunE2E로 캡처 경로)
.\scripts\Run-ArenaFuzz.ps1 -RunE2E

# Baseline 재수집
.\scripts\Run-AllBaselines.ps1

# 배포 ZIP 생성
.\scripts\Package-Release.ps1
```

TestRunner 예시:

```text
Valtrans.TestRunner.exe --scenario testdata/scenarios/smoke_basic_001.json --seed 20260910 --ocr-engine Windows
Valtrans.TestRunner.exe --scenario testdata/scenarios/smoke_zh_mixed_001.json --seed 20260910 --ocr-engine Hybrid --no-focus-arena
Valtrans.TestRunner.exe --compare-baseline artifacts/baseline-v0.2.3-windows.json --current-baseline <path>
```

Valtrans test mode:

```text
Valtrans.exe --test-mode --test-scenario <json> --test-ready-file <arena-ready.json> --test-output <report.json> --test-ocr-engine Fast
```

## Test Arena 개발 워크플로

- Arena 창 위치: `%LOCALAPPDATA%\Valtrans\TestArena\window-layout.json` (종료 시 자동 저장)
- 수동 저장: `.\scripts\Save-ArenaLayout.ps1`
- E2E 포커스 최소화: `Run-E2ESmoke.ps1 -NoFocusArena` 또는 TestRunner `--no-focus-arena`
- Topmost 기본 OFF; 필요 시 Arena `--topmost`

## Fast OCR 설치·워밍업

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Ocr\Setup-FastOcr.ps1 -RuntimeDirectory "$env:LOCALAPPDATA\Valtrans\FastOcrRuntime"
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Warmup-FastOcr.ps1
```

`fast_ocr_host.py`는 모델 로드 **후** `ready`를 보냅니다. TestRunner는 `*.ocr-ready` 시그널을 받은 뒤 Arena 시나리오를 시작합니다.

## 주요 경로

| 영역 | 경로 |
|------|------|
| Trace | `Models/MessageTraceRecord.cs`, `Services/MessageTraceService.cs` |
| Test Arena | `test/TestArena/` |
| Test Runner | `test/TestRunner/Program.cs` |
| 시나리오 | `testdata/scenarios/*.json` |
| Baseline | `artifacts/baseline-*.json` (git 추적 예외) |
| Fast OCR | `Services/FastOcrService.cs`, `Ocr/fast_ocr_host.py` |
| Hybrid | `Services/HybridOcrService.cs` (`HybridOcrOptions`로 test/prod 경로 분리) |
| OCR 글리프 보정 | `Services/OcrNoiseHeuristics.cs`, `Services/OcrLineSelector.cs` |
| 캡처 영역 계산 | `Services/OcrRegionRecommendationService.cs` (화면비 보정), `Services/OcrProfileValidationService.cs` |
| 배포 | `scripts/Package-Release.ps1` → `releases/v0.4.0-beta.zip` |

## Definition of Done (v0.3.0)

- Build 성공
- `dotnet test` 156/156 PASS (번역 회귀 규칙 65건 포함, Lite 피벗 벤치마크 1건은 Lite 미설치 시 자동 skip)
- `Run-E2ESmoke` 3/3 PASS
- `Run-E2EZhSmoke` 3/3 PASS (Fast OCR 설치 시)
- `Run-E2EHybridSmoke` PASS 또는 SKIP (런타임 없을 때)
- `Run-ArenaFuzz` validate + optional E2E PASS
- `test.ps1 -Profile Full` PASS
- Golden/expected 데이터를 잘못된 결과에 맞춰 수정하지 않음
- Test Arena는 반드시 Capture→OCR 경로 사용 (문자열 직접 주입 금지)

## 남은 작업

### 실게임 (수동)

- VALORANT 해상도/HUD별 OCR 영역 실측

### 운영 (로컬에서 실행)

```powershell
gh auth login
.\scripts\Release-GitHub.ps1
```

- **Windows OCR은 일본어를 약 22pt 미만에서 읽지 못한다** (측정됨). 16pt에서 영어·한글은 읽지만 `ラッシュ`는 통째로 사라진다
  - 이중선형 업스케일과 3x 업스케일 모두 해결하지 못함 → 업스케일로 없는 정보를 만들 수 없는 엔진 한계
  - Fast OCR은 동일 16pt에서 CJK를 읽는다 (`smoke_zh_mixed_001`이 증거). **일본어·중국어 사용자에게는 Fast/Hybrid를 권해야 한다**
  - `smoke_basic_001`은 이 한계를 `fontSize: 22` + `note` 필드로 명시한다. Arena 코드에 숨은 보정은 없으며 시나리오가 선언한 크기 그대로 렌더링한다
- Windows OCR 일본어 `二/ニ` 혼동은 OCR 엔진 한계로 문맥 검증 없이 일괄 치환하지 않음
- 캡처 영역은 16:9가 아닌 해상도에서 **높이 기준 HUD 배율**로 보정된다 (`HorizontalBasis`). 16:9에서는 수식적으로 기존과 동일한 픽셀값
  - 이 모델(VALORANT HUD가 높이로 스케일되고 좌하단 고정)은 **실게임 미검증**. 4:3 stretched·21:9에서 스크린샷 실측 필요
- 해상도 변경 감지는 400ms 타이머의 `ProfileSignature` 비교 (위치·크기·DPI·창 모드). 게임이 포그라운드를 벗어나도 signature를 유지해야 알트탭 중 해상도 변경을 잡는다
- Hybrid E2E는 Fast + Paddle VL runtime 설치 후 `Run-E2EHybridSmoke.ps1` (기본 시나리오: `smoke_zh_mixed_001`; `smoke_basic_001`은 Windows E2E용)
- Hybrid E2E는 `HybridOcrOptions.FastOnly`로 실행된다 (Fast/VL이 GPU 1개를 공유해 동시 로드 시 멈춤). VL fallback 경로는 `OcrCandidateResolverTests`로만 검증되며 실게임 수동 확인이 필요
- Test Arena는 이제 VALORANT처럼 하단부터 갱신되고, E2E가 프로덕션 최신 1줄 크롭 경로를 그대로 탄다 (`UseFullCaptureRegionForTest()` 제거됨)
  - Arena `chatRegion`은 좌표만 `PointToScreen`(물리 px)이고 크기는 `ActualWidth/Height`(DIP)라서, DPI 125% 화면에서 패널 하단 20%가 캡처 사각형 밖이었다. 양쪽 모서리를 모두 `PointToScreen`으로 계산하도록 수정
  - `ChatPanel`은 E2E에서 `Height = 384` 고정 + `ClipToBounds`. ready 파일은 줄이 생기기 전에 영역을 확정하므로 패널이 자라면 최신 줄이 영역 밖으로 밀려난다
  - 384는 하단 22% 밴드에 렌더링 3줄이 들어가는 값. 280이면 2줄만 들어가 3번째 메시지에서 최신 줄이 밴드를 벗어난다
  - Windows E2E 시나리오 `smoke_basic_001`은 `fontSize: 22`로 JP 한계를 명시한다 (Arena 코드에 숨은 보정 없음)
- 에이전트 이름 한글화(`Jett` → `제트`)는 미적용. `ProperNames`가 canonical 영문 유지 정책
- Windows OCR + JP 선택 시 `WindowsOcrAdvisory`가 빠른 시작·언어팩·엔진 상태에 Fast/Hybrid 권장 문구를 표시한다
- `OcrIssuePanel`에 엔진 준비 실패 시 `OCR 다시 준비` 버튼이 있다
- OCR 테스트는 선택한 엔진(Paddle/Fast/Hybrid/Windows) 경로를 그대로 사용한다 (`ReadRegionForCurrentEngineAsync`)
- 오버레이에 번역 생략 사유가 표시된다 (`OverlayWindow.AddSkipNotice` · 노란 제목 + 회색 상세)
- Lite JP↔KO는 EN 피벗으로 최대 2회 모델 호출. `TranslatorService.LastLitePivotMetrics`와 `scripts/Benchmark-LitePivot.ps1`로 측정
- `GlossaryService`의 `knocked`/`다운`/`ノック` 채팅 표현은 VALORANT 무관하지만 분류 회귀 위험으로 유지

## 작업 원칙

1. Phase 단위 점진 구현, 기존 동작 유지
2. Baseline regression 확인
3. OCR 오류와 번역 오류 Trace에서 분리 유지
4. CLI + JSON 입출력 우선
