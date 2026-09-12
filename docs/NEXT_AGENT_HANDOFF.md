# Valtrans 다음 작업 인수인계 문서

> **최신 사용자 지시 / GitHub 공개 기준**: [github-release-v050.md](github-release-v050.md). 서버 점검 중: 동기화·SSH·재배포 금지. 검증한 677 검사 후보를 GitHub v0.5.0-beta로 공개하는 단계이며, 추가 트랩/short break 표본의 미해결 오류도 별도 기록했다. 아래 서버 운영 상태와 배포 안내는 과거 기록이다.

> **현재(2026-09-13 03:38)**: [translation-candidate-677-v050.md](translation-candidate-677-v050.md).677 검사/최신960건 검토/ZIP82993F... 재생성 완료. DLL E2BC39... 동일성 확인. 모든 모델·빌드 프로세스 종료. sourceDirty=true 후보이며 공개 전 소스 대응과 GitHub asset 검증이 남는다. UI 자동화 중단 상태 유지. 서버 배포·권한을 다시 요청하지 않는다.

> **가장 최신(2026-09-13 03:20)**: [receive-filter-v050.md](receive-filter-v050.md). 기본 수신 필터 콜 누락과 한국어 모름/일본어 ません 오탐 수정,664 검사 통과. 실제 모델 두 단계288건 판정 완료했지만 전역 문체 변경은 회귀도 있어 추가 분리 검증이 필요하다.617 ZIP은 이 수정 미포함. 서버는 이미 배포·권한 확인 완료. UI 자동화 중단 의사를 유지한다.

> 진행 중인 v0.5.0-beta Goal의 최신 상태는 [release-candidate-v050.md](release-candidate-v050.md)와 [release-goal-v0.5.0-beta.md](release-goal-v0.5.0-beta.md)를 먼저 읽는다. 아래 v0.4 완료 표는 과거 기준선이다. 운영 사이트 v0.5 배포 및 LAN/공식 도메인 검증 완료. 클라이언트 GitHub 공개는 아직 하지 않았다.

> 최신 자동 검사617건. 610 빌드 실제 번역960건 및 일본어 방향 오탐 수정 후 추가128건 판정 완료. [translation-health-v050.md](translation-health-v050.md)는 이전592 빌드 기록이다. 원탭/인원/일본어 방향 오탐 및 일부 화자·의도를 수정했다. 남은 모델 의미 오류와 클라이언트 공개 준비는 계속할 작업이다.

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

## 실게임 피드백 (2026-09-11)

| 증상 | 내용 |
|------|------|
| B 지역 브리핑 | B 관련 콜이 다양해도 **「B 헤븐 2명」**으로 고정되는 경우 다수 |
| 속도 | 전체적으로 번역이 **너무 느림** |
| 품질 | 전체적으로 번역 **품질이 낮음** |

### 추정 원인 (코드 기준, 미수정)

**B → B 헤븐 2명 고정**
- `OcrNoiseHeuristics.SplitRunTogetherCallout`이 `2BHeaven`처럼 붙은 OCR을 `2 B Heaven`으로 복원 → `GlossaryService.TryTranslateStructuredCallout` 인원 패턴이 **B 헤븐 2명**으로 매칭 (E2E golden과 동일 경로)
- OCR이 `B rush` / `B main` / `B`만 읽히는데 잘못된 숫자·`Heaven`이 섞이면 presence regex(`presencePatterns`)가 과매칭할 수 있음
- `TranslateWithSafeBriefingDeadlineAsync`(2.4s): 구조화 사전이 **한 줄이라도** 매칭되면 AI 대기 후 타임아웃 시 사전 결과로 대체 → 잘못 매칭되면 고정 출력
- `TranslationCacheService`가 잘못된 한 쌍을 캐시하면 동일 문맥 반복 시 고정
- 프롬프트·회귀 golden에 `B heaven` / `B 헤븐 2명` 예시가 많아 AI 경로도 편향 가능

**느림**
- Hybrid OCR(Fast→VL), 두 프레임 합의, Paddle VL 수 초
- 번역: Hy-MT2 + `TranslateWithRetryAsync`(재시도 450ms), Lite JP↔KO EN 피벗 2회
- 안전 브리핑 경로는 **최소 2.4s** 대기 후 사전 대체
- 전체 채팅 모드 기본 4s 대기

### 다음 수정 방향 (제안)

1. B-only / B+rush / B+main 등 **사이트 단독·행동 콜**은 인원 패턴과 분리; `2BHeaven` 복원은 confidence·길이 게이트 강화
2. `TryBuildSafeBriefing` 타임아웃 대체는 **원문과 구조화 결과 fact 일치**할 때만 (숫자·위치 불일치 시 미표시)
3. 실패 케이스 수집: MessageTrace + OCR 원문 + 최종 번역 + 엔진/경로 (B 지역만 필터)
4. 속도: latest-line은 구조화 사전 **선행 반환**(AI 병렬), 캐시 키에 OCR raw 포함 검토, Hybrid Fast-only 옵션 UI

## 남은 작업

### 실게임 (수동)

- VALORANT 해상도/HUD별 OCR 영역 실측
- **B 지역 브리핑 오역·지연** 재현 로그 수집 (위 피드백)

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

## 2026-09-12 개선 Goal 후속 기록 (위의 가설보다 우선)

버전은 사용자 요청대로 **v0.4.0-beta 유지**. 상세 결과는 `improvement-goal-2026-09-12.md`와 `translation-improvements-2026-09-12.md` 참조.

- 2.4초는 최소 대기가 아니라 `Task.WhenAny`의 deadline이다. 번역이 먼저 끝나면 즉시 반환한다. 기본4초도 모든 메시지 대기가 아니라 입력창이 오래 열린 경우 전체 채팅 모드로 넘어가는 기준이다.
- Windows 일본어22pt는 당시 Arena 조건에서 관찰한 결과다. 모든 폰트·DPI·배경의 보편적 임계값이라고 단정하지 않는다.
- 위치 단독·방향별 인원·명시적 정정 문장 규칙을 보완하고, 복합 문장을 부분 매칭으로 잘라내지 않도록 제한했다. `3900이`를 `3900명`으로 바꾸던 앱 후처리를 제거했다.
- 짧은 로컬 프롬프트+원문 관련 용어를 사용한다. `ロー`/`ローテ` 충돌, 일본어 `するなら`를 금지로 오인, 시간 의미의 `前に`/`잠시 뒤`/`right back` 오탐을 수정했다.
- 최종 후처리 후 의미 검사 실패 시 송신 교체·수신 표시·캐시를 막는다. 검사는 제한된 패턴이며 행위자/발화 의도/부정 범위를 완전하게 검증하지 않는다.
- Lite 모델4쌍 해시 정상. 원시 모델10예문×beam1/2/4 및 Argos 옵션10건에서도 오역 재현. 모델을 교체하거나 beam을 높이는 패치는 하지 않았다. 무관한 성적 단어 생성 일부와 깨진 토큰을 차단하지만 Lite 일반 문장 품질은 여전히 낮다.
- 요청별 설정 복사, 캐시의 공백·대소문자·엔진·모델·지역·사용자사전 구분. 송신은 현재 입력 재확인과 포커스 확인 후 붙여넣고 Enter는 보내지 않는다. 검사는 원자적이지 않다.
- Fast/Hybrid 두 프레임 합의가 Windows로 돌아가던 경로를 수정. 테스트용 전체 영역 합의 시나리오와 실제 recognizer 기록 추가.
- Arena 실제 캡처4회×3건 통과(Windows 기본, Fast 혼합, Fast 전체합의, Hybrid 전체합의). **Hybrid는 FastOnly**였으며 VL fallback 실측·실게임4K·게임 FPS는 이번 검증 범위 밖이다.
- `scripts/Run-TranslationQuality.ps1` + `testdata/translation-quality/`로 반복 검사가 가능하다. 동일 표본을 보고 수정했으므로 정답률/일반화 보증으로 홍보하지 않는다. 추가16문장도 검증기 개선에 사용했으므로 최종에는 회귀 표본이다.
- 테스트/빌드/압축 실패를 전파하고 새 staging으로 패키징한다. `.deployment`는 빌드용이라 Git에서 제외했다. 운영 서버·GitHub에는 배포하지 않았다.
