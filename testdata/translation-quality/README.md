# 송신·수신 실제 번역 품질 검사

`corpus.tsv`는 80상황, `held-out.tsv`는 추가16상황입니다. 상황마다 KO→EN, KO→JP, EN→KO, JP→KO 4방향을 호출합니다. reference와 글자가 같아야 정답인 검사는 아닙니다.

## 실행

Windows PowerShell 7, .NET 10 SDK와 선택한 프로필의 엔진이 필요합니다. Hybrid 검사는 Ollama 모델만, Lite 검사는 Valtrans Lite만 준비하면 됩니다. 게임·OCR을 중지하고 GPU 여유를 확보하세요. 설치나 앱 설정 저장은 하지 않지만 로컬 모델이 메모리에 올라갑니다.

```powershell
dotnet build Valtrans.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
pwsh -NoProfile -File scripts/Run-TranslationQuality.ps1 -Profile hybrid18 -OutputDirectory artifacts/my-run
pwsh -NoProfile -File scripts/Run-TranslationQuality.ps1 -Profile hybrid7 -OutputDirectory artifacts/my-run
pwsh -NoProfile -File scripts/Run-TranslationQuality.ps1 -Profile lite -OutputDirectory artifacts/my-run
pwsh -NoProfile -File scripts/Run-TranslationQuality.ps1 -Profile hybrid18 -AllFamilies -CorpusPath testdata/translation-quality/held-out.tsv -OutputDirectory artifacts/my-held
```

- 기본 hybrid18=320건, hybrid7/Lite=공통30상황 각120건. `-AllFamilies`면 해당 파일 전체 상황 사용.
- 실제 TranslatorService 규칙·선택 모델·최종 검사를 실행합니다. Hybrid는 AI 실패 시 Lite로 자동 대체하지 않습니다. 모델 단독 성능이 아니며 OCR·대기열·클립보드 교체는 포함하지 않습니다.
- 저장된 설정을 메모리에서 읽고 provider/model/game/filter를 검사값으로 바꿉니다. 사용자 사전·서버 지역은 영향을 줄 수 있으니 재현 시 확인하세요.
- 요청당 최대30초. JSONL에 원문/번역/예외/경로/시간, meta에 DLL·표본 SHA256을 기록합니다. 기존 결과는 덮어쓰지 않습니다.
- 실행 중 다시 빌드하지 마세요. DLL 변경 시 결과는 최종 검사로 취급하지 않습니다.

## 수동 판정

각 profile에 `grades-hybrid18.tsv` 같은 파일을 만듭니다. 헤더는 `family`, `grades`, `notes`를 탭으로 구분합니다. grades 네 글자는 위의 4방향 순서입니다.

| 판정 | 기준 |
|---|---|
| A | 핵심 의미 보존·사용 가능. 다른 자연스러운 표현 허용 |
| W | 이해 가능하지만 어색하거나 일부 모호함 |
| E | 화자·행동·시제·수치·부정 범위 등 의미 오류 |
| X | 예외·차단·시간 초과. 좋은 결과로 집계하지 않음 |

```powershell
pwsh -NoProfile -File scripts/Summarize-TranslationQuality.ps1 -ResultDirectory artifacts/my-run
```

규칙에 맞춘 표본의 개선과 새 문장 성능을 따로 보세요. 실제 게임 정확도·FPS·무오류 보증으로 해석하지 마세요. 새 표본을 보고 규칙을 고쳤다면 이후 검사는 엄밀한 미공개 held-out이 아니라 추가 회귀 검사입니다.

`Run-TranslationQuality.ps1 -FamilyIds 404`로 일부 가족만 재검사할 수 있습니다. `-ReceiveFilterMode Briefing` 또는 `Strict`를 지정하면 실제 수신 필터도 통과시킵니다(기본 `All`). 결과의 `source`는 비교할 원문, `translationInput`은 필터를 거친 실제 번역 입력입니다. `filterKeep=false`/`FILTERED:`는 모델 실패가 아니라 수신 필터에 의해 빠진 사례이며 성공으로 세지 않습니다. 메타데이터에 필터 모드와 선택한 가족 ID를 기록합니다. 기존 All-mode 검사 수치를 실제 브리핑 필터나 OCR 인식 정확도로 해석하면 안 됩니다.
