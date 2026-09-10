# Valtrans 다음 작업 인수인계 문서

작성 기준: `v0.3.0-beta` (Phase 0–10 + 하드닝 완료)  
목적: 에이전트·개발자가 Test Arena E2E, Trace, Baseline, Fast/Hybrid OCR 상태를 빠르게 파악하고 다음 작업을 이어간다.

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
# 단위 테스트 (37개)
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
| Hybrid | `Services/HybridOcrService.cs` |
| 배포 | `scripts/Package-Release.ps1` → `releases/v0.3.0-beta.zip` |

## Definition of Done (v0.3.0)

- Build 성공
- `dotnet test` 37/37 PASS
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

- Windows OCR 일본어 `二/ニ` 혼동은 OCR 엔진 한계로 문맥 검증 없이 일괄 치환하지 않음
- Hybrid E2E는 Fast + Paddle VL runtime 설치 후 `Run-E2EHybridSmoke.ps1` (기본 시나리오: `smoke_zh_mixed_001`; `smoke_basic_001`은 Windows E2E용)

## 작업 원칙

1. Phase 단위 점진 구현, 기존 동작 유지
2. Baseline regression 확인
3. OCR 오류와 번역 오류 Trace에서 분리 유지
4. CLI + JSON 입출력 우선
