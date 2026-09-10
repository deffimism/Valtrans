# Valtrans

Windows 게임 채팅 번역 · Local game chat translation for Windows

[다운로드 / Download](https://github.com/deffimism/Valtrans/releases) · [공식 Discord / Official Discord](https://discord.gg/GMYuAJ8bZ2)

[한국어 사용법](#korean) · [English guide](#english)

> **작성 기준 버전 / Documentation baseline: v0.3.0-beta**<br>
> **검토일 / Reviewed: 2026-09-11**<br>
> 현재 화면과 사용 흐름을 기준으로 작성했습니다. 사용법이 변하지 않으면 앱 버전이 올라가도 README는 업데이트되지 않을 수 있습니다.<br>
> This guide describes the interface and workflow at the version above. It may remain unchanged across releases when the usage instructions still apply.

<a id="korean"></a>

## 한국어 사용법

### 무엇을 하는 프로그램인가요?

Valtrans는 VALORANT 채팅을 한국어·영어·일본어 사이에서 번역하는 Windows 앱입니다.

- **보내는 채팅:** 게임 채팅창에 입력한 내용을 단축키로 번역해 교체합니다. 전송은 직접 합니다.
- **받는 채팅:** 게임의 채팅 영역을 OCR로 읽고 새 메시지의 번역을 오버레이에 표시합니다.
- **로컬 번역:** 기본 구성은 Hy-MT2 1.8B를 우선 사용하고 필요하면 Valtrans Lite를 시도합니다.
- **FPS 표현:** 약어·은어·고유명사 사전과 번역 규칙을 사용합니다. 인원·방향·부정·불확실성을 보존하려 하지만 오역은 가능합니다.

OCR은 **화면을 글자로 읽는 단계**, 번역 엔진은 **그 글자를 다른 언어로 바꾸는 단계**입니다. 번역 모델을 바꿔도 OCR이 잘못 읽은 원문 자체는 고쳐지지 않습니다.

### 1. 설치와 첫 실행

1. [GitHub Releases](https://github.com/deffimism/Valtrans/releases)에서 배포 ZIP을 받아 **압축을 모두 풉니다**.
2. 폴더 안의 `Valtrans.exe`를 실행합니다. EXE만 따로 옮기지 말고 DLL과 `Ocr` 폴더 등 동봉 파일을 함께 유지하세요.
3. .NET 실행 환경이 필요하다는 안내가 나오면 **.NET 10 Desktop Runtime**을 설치합니다.
4. 왼쪽 **시작 가이드 · 점검**에서 **권장 엔진 준비**를 누릅니다. Lite → Hy-MT2 → Paddle OCR 순서로 준비합니다. 최초 설치·모델 다운로드에는 인터넷이 필요합니다.
5. **번역 엔진**에서 설치 진행과 **예열 완료** 상태를 확인합니다. 처음 준비할 때는 시간이 걸릴 수 있습니다.
6. Paddle 설치 안내가 나오면 용량·GPU 조건을 확인하고 승인합니다. 필요한 uv·Python·라이브러리·OCR 모델을 자동 설치한 뒤 모델을 준비합니다. OCR만 준비하려면 같은 시작 가이드의 **OCR 설치 · 준비**를 누르세요. NVIDIA GPU 환경이 맞지 않으면 대시보드에서 Windows OCR을 선택하고 언어팩을 설치하세요.
7. 아래 사용법에 따라 언어를 정하고 상단 **설정 저장**을 누릅니다.

기본 사용에는 Windows 10/11과 .NET 10 Desktop Runtime이 필요합니다. 로컬 AI는 Ollama와 모델이 필요하며 앱에서 설치를 진행할 수 있습니다. **API 키·Docker·WSL은 필요하지 않습니다.**

**권장값 적용 / 권장 엔진 준비 주의:** 엔진뿐 아니라 모델·단축키·언어·필터·오버레이 등의 설정도 권장값으로 바꿉니다. 이미 설정해 둔 사용자는 **번역 엔진**의 개별 설치 버튼을 이용하세요.

#### 처음에는 이렇게 사용하세요

| 항목 | 권장 시작값 |
| --- | --- |
| 번역 엔진 | 무료 로컬 · AI 우선 + Lite 대체 |
| 로컬 모델 | Hy-MT2 1.8B |
| 보내는 채팅 목표 언어 | English |
| 받는 채팅 원문 언어 | EN / JP / KO 모두 선택 |
| 받는 채팅 출력 언어 | 한국어 |
| OCR 엔진 | Hybrid OCR(Fast+VL) 또는 PaddleOCR-VL · 중국어/혼합은 Fast OCR도 가능 |
| 채팅 필터 | 브리핑 중심. 동작 시험 중에는 **잡담도 번역** |
| 오버레이 표시 시간 | 15초 |

기존 설정이 있으면 화면의 값이 위 표와 다를 수 있습니다. 게임과 로컬 모델이 CPU/GPU·메모리를 함께 사용하므로 큰 모델보다 기본 모델부터 확인하는 편이 좋습니다.

### 2. 메뉴 찾기

| 왼쪽 메뉴 | 주요 기능 |
| --- | --- |
| 채팅 대시보드 | 보내는 언어·단축키, 받는 언어, 자동 OCR 영역, OCR 시작/중지, 오버레이 표시 |
| 번역 엔진 | 로컬 엔진·모델 선택, 설치·예열, 서버·언어권 참고 |
| 오버레이 설정 | 이동·크기·잠금, 표시 시간, 배경/테두리 투명도, 글자 크기 |
| 번역 테스트 · 사전 | 텍스트 번역 시험, 호환성 검사, 팀 전용 사전 |
| 시작 가이드 · 점검 | 권장 구성 준비, 전체 점검, 자동 복구, 진단 저장 |

OCR 선택·상태·**OCR 설치 / OCR 준비** 버튼은 **채팅 대시보드 → 받는 채팅 · OCR**에 항상 표시됩니다. 필터·안정화·실행 환경 폴더만 **세부 설정 · 진단**에 있습니다.

### 3. 내가 보내는 채팅 번역하기

1. **채팅 대시보드 → 보내는 채팅**에서 목표 언어를 고릅니다. 한국어를 영어로 보내려면 **English**입니다.
2. 지원 게임을 전면에 놓고 Enter로 채팅 입력창을 엽니다.
3. 문장을 입력한 뒤 기본 단축키 **백슬래시 `\`**를 누릅니다.
4. 입력 내용이 번역문으로 교체될 때까지 기다립니다. 처리 중에는 추가 입력을 잠시 멈춰 주세요.
5. 결과를 확인하고 **Enter를 직접 눌러 전송**합니다.

단축키는 입력 내용을 선택·복사하고 번역문으로 교체하는 데 사용됩니다. 일반 게임 조작 중에 누르지 말고 **채팅 입력창을 연 상태**에서 사용하세요.

- **단축키 변경:** 옆의 **입력** 버튼 → 원하는 키 또는 조합키. Esc로 지정 취소.
- 단일 키도 지정할 수 있습니다. 이동·스킬 키처럼 자주 쓰는 키는 피하세요.
- 키보드에 따라 백슬래시 키가 `₩`로 표시될 수 있습니다. 설정에 `Oem5`가 보이면 해당 물리 키를 뜻합니다.
- 단축키는 지원 게임이 전면에 있을 때 활성화됩니다. 메모장보다 **번역 테스트** 메뉴를 이용하세요.
- 이미 목표 언어인 문장은 번역이 생략될 수 있습니다.

### 4. 받는 채팅 번역하기

1. 지원 게임을 실행합니다. 오버레이가 보이지 않으면 테두리 없는 창 모드에서 먼저 확인하세요.
2. **채팅 대시보드 → 받는 채팅 · OCR**에서 게임과 언어를 확인합니다.
   - **원문 EN / JP / KO:** 번역할 메시지의 원래 언어입니다. 여러 개를 선택할 수 있습니다.
   - **출력:** 오버레이에서 읽을 언어입니다.
3. **OCR 영역 표시**를 눌러 자동으로 잡힌 범위를 확인합니다. 다시 누르면 테두리가 사라집니다.
4. 필요하면 **추천 영역 새로고침**을 누릅니다. 수동 드래그로 영역을 지정하는 기능은 없습니다.
5. 게임 채팅이 보일 때 **OCR 테스트**로 인식 원문과 추출 본문을 확인합니다.
6. **OCR 시작**을 누르고 오버레이가 켜져 있는지 확인합니다.
7. **시작한 다음 새 메시지**로 시험합니다. 처음부터 화면에 있던 채팅은 기준 화면으로 등록되어 생략될 수 있습니다.

영역은 게임 창의 위치·크기를 기준으로 계산하고 창 이동·해상도 변경을 반영합니다. 게임을 찾지 못한 상태의 영역 표시는 모니터 기준 미리보기이며, OCR 시작에는 지원 게임 감지가 필요합니다.

VALORANT 추천 범위는 게임 화면 **좌측 하단 (0%,0%)** 기준으로 측정한 비율을 사용합니다. 메시지 영역은 **가로 1.37~24.5%, 세로 4.7~27.2%**(입력줄 4.7% 이하는 제외)입니다. Enter로 채팅 입력창을 **짧게** 열면 최신 1줄만 빠르게 번역하고, 입력창이 **설정한 시간(기본 4초) 이상** 열려 있으면 전체 채팅을 번역합니다. 시간은 **세부 설정 · 진단 → 전체 채팅 번역 대기**에서 바꿀 수 있습니다.

#### 채팅이 생략되는 경우

- 이미 처리한 메시지 또는 시작할 때 보이던 메시지
- 출력 언어와 같은 본문
- 시스템·방송 문구와 채팅 입력줄
- 브리핑 필터가 게임 관련성이 낮다고 판단한 잡담

닉네임·채널 표시는 본문과 분리합니다. **내가 보낸 메시지라는 이유만으로 제외하지 않습니다.** 자신의 일본어 메시지로도 시험할 수 있지만 같은 문장을 반복하면 중복으로 걸러질 수 있습니다.

`hello` 같은 인사말로 시험하려면 **세부 설정 · 진단 → 잡담도 번역**을 선택하고, 이전과 다른 새 문장을 보내세요.

#### OCR 세부 조정

설치·준비에는 메뉴를 펼칠 필요가 없습니다. 부가 옵션은 **채팅 대시보드 → 세부 설정 · 진단**을 펼칩니다.

- **채팅 필터 · OCR 세부 조정:** 잡담 포함 여부, **전체 채팅 번역 대기(기본 4초)**, 안정화, 작은 글자 자동 확대·대비 보정, 두 프레임 합의.
- OCR 엔진 선택·설치·준비는 받는 채팅 카드에 바로 표시됩니다.
- **OCR 처리 상세:** 인식·생략·대기·번역 상태를 확인합니다.

대안인 Windows OCR은 최신 부분과 전체 영역을 자동으로 확인합니다. 변화가 없는 화면은 반복 처리를 줄입니다. 두 영역을 직접 설정하거나 맵을 선택할 필요는 없습니다.

### 5. 오버레이 위치와 모양

**오버레이 설정 → 오버레이 위치 · 크기 조정**을 누릅니다.

1. 상단 이동 바를 드래그해 옮깁니다.
2. 오른쪽 아래 손잡이를 드래그해 크기를 바꿉니다.
3. **완료 · 잠금**으로 위치를 저장하고 게임 클릭을 통과시킵니다.

완전히 투명한 배경에서도 편집 중에는 잡을 수 있는 영역이 임시로 나타납니다.

| 설정 | 동작 |
| --- | --- |
| 표시 시간 | 5·10·15·30·60초 또는 계속 표시 |
| 배경 투명도 | 0~100%. 100%이면 배경이 보이지 않음 |
| 테두리 투명도 | 배경과 별개로 0~100% |
| 글자 크기 | 11~32px |
| 클릭 통과·이동/크기 잠금 | 게임 조작을 방해하지 않도록 잠금 |

**오버레이 끄기**는 창만 숨깁니다. 읽기·번역 작업까지 멈추려면 **OCR 중지**를 누르세요. **OCR 영역 표시**는 캡처 범위 확인용 테두리이며 번역 오버레이와 별개입니다.

### 6. 번역 엔진과 OCR 엔진 선택

#### 번역 엔진

| 선택 | 용도와 주의점 |
| --- | --- |
| 무료 로컬 · AI 우선 + Lite 대체 | 기본 구성. 확실한 표현은 사전·규칙으로 처리하고, 일반 문장은 AI 우선으로 번역하며 필요하면 Lite를 시도 |
| 로컬 AI | Hy-MT2 / Qwen / TranslateGemma. 모델이 커지면 메모리와 지연도 증가할 수 있음 |
| Valtrans Lite | CPU 기반 경량 번역. 일본어↔한국어는 영어를 거칠 수 있어 표현 손실에 주의 |

모델 변경은 **모델 선택 → 로컬 AI 설치 → 예열 상태 확인 → 설정 저장** 순서로 확인하세요. Lite는 **Valtrans Lite 준비**로 설치합니다. 설치와 예열은 다릅니다. 모델 파일이 있어도 메모리에 준비될 때까지 첫 번역이 느릴 수 있습니다.

**서버·언어권 참고**는 은어 해석에 참고할 문맥입니다. 실제 서버 접속 설정이나 원문 언어 필터가 아닙니다.

맵 선택 없이 캐릭터·맵 이름 등 고유명사를 공통 사전으로 처리합니다. 예: `제트 / ジェット → Jett`, `어센트 / アセント → Ascent`. 사전이 모든 은어와 문맥을 보장하지는 않습니다.

#### 기본 PaddleOCR-VL

혼합 언어를 함께 읽는 **기본 OCR 엔진**입니다. 첫 사용 전에 별도 실행 환경과 모델을 준비해야 합니다. 기본값이지만 실험적 기능이며 인식 품질·지연에는 한계가 있습니다.

1. **시작 가이드 · 점검 → OCR 설치 · 준비**를 누릅니다. **권장 엔진 준비**에서도 이 과정이 자동으로 이어집니다.
2. 설치 안내를 승인하면 필요한 도구·모델을 자동으로 받습니다. 진행과 오류는 같은 가이드에 표시됩니다.
3. 설치가 끝나면 자동으로 OCR 모델을 준비합니다. 이미 설치되어 있으면 다운로드 없이 준비를 시도합니다. 대시보드의 **OCR 설치**로 설치를 다시 실행하거나 **OCR 준비**로 이어갈 수 있습니다.
4. **OCR 테스트**로 결과를 비교한 뒤 OCR을 시작합니다.

현재 설치 경로는 NVIDIA GPU용입니다. 다운로드 수 GB, 디스크 여유 약 10GB를 권장하며, 실측 환경에서는 추가 GPU 메모리 약 2GB와 수 초의 인식 시간이 필요했습니다. PC·게임 부하에 따라 달라집니다. Windows 언어팩은 필요하지 않지만 원문 언어 선택은 여전히 번역 필터에 적용됩니다.

이 모드에는 Windows 확대 보정·이중 영역·두 프레임 합의가 적용되지 않습니다. **취소 · 메모리 해제**, OCR 중지 또는 앱 종료로 앱이 시작한 OCR 프로세스를 종료할 수 있습니다. 더 정확하거나 더 빠르다고 항상 보장하지 않습니다.

#### Fast OCR · Hybrid OCR (v0.3.0)

중국어·영문 혼합 채팅이 많을 때 **Fast OCR(PP-OCRv5)** 또는 **Hybrid OCR(Fast + Paddle VL 폴백)**을 선택할 수 있습니다.

| 엔진 | 용도 |
| --- | --- |
| Fast OCR | GPU 없이 동작. 중국어·혼합 인식에 적합. `Ocr/Setup-FastOcr.ps1`로 설치 |
| Hybrid OCR | Fast를 먼저 시도하고 신뢰도가 낮을 때만 Paddle VL로 폴백 |
| PaddleOCR-VL | 기본값. NVIDIA GPU 필요 |
| Windows OCR | 가벼운 대안. 언어팩 필요 |

대시보드 **받는 채팅 · OCR**에서 엔진을 고른 뒤 **OCR 설치 · 준비**를 누르면 선택한 엔진에 맞는 설치·준비가 진행됩니다.

### 7. 번역 시험과 사전

**번역 테스트 · 사전**에서 테스트 방식·목표 언어·원문을 정하고 **번역 테스트**를 누릅니다. 결과와 실제 처리 경로, 소요 시간을 확인할 수 있습니다.

| 검사 | 확인하는 것 |
| --- | --- |
| 번역 테스트 | 직접 입력한 텍스트의 번역. 화면 OCR은 하지 않음 |
| OCR 테스트 | 현재 게임 영역의 인식 원문·추출 본문 |
| 품질 자가 테스트 | 내장 규칙의 기본 동작 |
| 엔진 호환성 검사 | 실제 예문 요청에 대한 응답과 주요 사실 보존 |

호환성 검사 통과는 모든 문장의 번역 품질을 보장하지 않습니다.

**팀 전용 사전**에는 한 줄에 `원문=표기`를 입력하고 저장합니다.

```text
제트=Jett
후카=Hookah
```

일반적인 짧은 단어를 엉뚱한 의미로 등록하면 다른 문장에도 영향을 줄 수 있습니다. 자주 쓰는 이름·콜 위주로 추가하고 번역 테스트로 확인하세요.

### 8. 문제가 있을 때

| 증상 | 확인 순서 |
| --- | --- |
| 실행이 안 됨 | ZIP 전체 압축 해제 → 동봉 DLL 확인 → .NET 10 Desktop Runtime 확인 |
| 보내는 번역이 안 됨 | 지원 게임 전면 → 채팅 입력창 열기 → 목표 언어·단축키 충돌 확인 → 번역 테스트 |
| 받는 번역이 안 뜸 | OCR 시작·오버레이 켜짐 → 원문 언어 → 영역 표시 → OCR 테스트 → 잡담도 번역 → 새 메시지 |
| 번역문이 엉뚱함 | OCR 원문부터 확인. 원문이 맞으면 같은 글자로 번역 테스트 |
| 오버레이를 못 옮김 | 오버레이 설정 → 오버레이 위치 · 크기 조정 |
| 지연·게임 프레임 저하 | 1.8B 모델·가벼운 대안 Windows OCR로 비교, 불필요한 OCR 중지 |
| Lite 번역 확인 필요 | 의심 결과를 보류한 상태. 기본 복합 모드나 AI로 비교하고 숫자·방향·부정 확인 |
| Paddle 설치 실패 | 시작 가이드의 오류 확인 → 네트워크·NVIDIA 환경·디스크 여유 확인 → OCR 설치 · 준비 재시도 |

해결되지 않으면 **시작 가이드 · 점검 → 전체 점검**을 실행하고 필요한 경우 **진단 저장**을 사용하세요. 공유 전에는 저장된 파일에 공개하고 싶지 않은 내용이 없는지 확인해 주세요.

[공식 Discord](https://discord.gg/GMYuAJ8bZ2)에 제보할 때 **앱 버전, 게임, 해상도·Windows 배율, 번역/OCR 엔진, 원문과 결과, OCR 처리 상태**를 함께 알려주면 도움이 됩니다.

### 9. 업데이트·데이터·주의 사항

- 업데이트 전 앱을 종료하고 새 ZIP을 별도 폴더에 모두 풉니다. 새 폴더의 `Valtrans.exe`를 실행하세요.
- 설정과 Lite 데이터는 `%LOCALAPPDATA%\Valtrans`에 저장됩니다. Ollama 모델은 별도 저장소를 사용합니다.
- 새 설정과 **권장값 적용**은 PaddleOCR-VL을 선택합니다. 기존 Windows OCR 설정도 이번 업데이트의 첫 실행에서 Paddle로 한 번 전환됩니다. 이후 Windows OCR을 직접 선택하고 저장하면 그 선택은 유지됩니다.
- 기존 외부 API 엔진 설정은 로컬 기본값으로 전환됩니다. API 키는 다음 설정 저장에서 제외됩니다.
- 외부 LLM/DeepL API·DLX/Docker 준비, 수동 OCR 영역·보정 마법사, 맵 선택 기능은 현재 버전에서 제공하지 않습니다. 설치되어 있던 Docker·WSL 자체를 삭제하지는 않습니다.
- 최초 다운로드에는 인터넷을 사용하지만 채팅 번역과 OCR 인식은 로컬에서 처리합니다. 게임과 모델이 자원을 공유합니다.
- OCR 오인식·오역·중복 판정 오류는 남을 수 있습니다. 중요한 인원·방향·부정 표현을 직접 확인하세요.
- 버전은 관리자가 명시적으로 결정할 때 올립니다. 사용법이 같다면 README 기준 버전은 유지될 수 있습니다.

---

<a id="english"></a>

## English guide

### What Valtrans does

Valtrans translates VALORANT chat between Korean, English and Japanese on Windows.

- **Outgoing:** replaces text in the game's chat input using a hotkey. You send it yourself.
- **Incoming:** reads game chat with OCR and displays new translations in an overlay.
- **Local translation:** the default uses Hy-MT2 1.8B first and attempts Valtrans Lite when needed.
- **FPS terminology:** dictionaries and rules help preserve names, slang, counts, directions and negation, but errors remain possible.

**OCR reads pixels into text. Translation converts that text into another language.** A larger translation model does not fix incorrectly recognized source text.

The interface currently uses Korean labels. The labels below match the app; this guide does not imply an English UI option.

### 1. Install and prepare

1. Download the ZIP from [GitHub Releases](https://github.com/deffimism/Valtrans/releases) and **extract everything**.
2. Run `Valtrans.exe`. Keep the DLLs, `Ocr` folder and other bundled files beside it.
3. Install the **.NET 10 Desktop Runtime** if prompted. Windows 10/11 is required.
4. Open **시작 가이드 · 점검** and click **권장 엔진 준비**. Setup proceeds through Lite, Hy-MT2 and Paddle OCR.
5. Wait for installation, model downloads and **예열 완료** (Warm-up complete) in **번역 엔진**.
6. Review and approve the Paddle setup prompt. Required tools (including uv), Python, libraries and the model are downloaded automatically, followed by model preparation. Use **OCR 설치 · 준비** in the same guide for OCR-only setup. If your NVIDIA GPU environment is unsuitable, select Windows OCR on the dashboard and install its language packs.
7. Configure languages and click **설정 저장** (Save settings).

Ollama and the local model can be prepared through the app. Initial downloads require internet access. **No API key, Docker or WSL is required.**

**Warning:** Apply recommended settings / Prepare recommended engines also changes the model, hotkey, languages, filter and overlay preferences. Use individual engine setup buttons if you want to keep a customized setup.

Start with the default hybrid engine, **Hy-MT2 1.8B**, **PaddleOCR-VL**, EN/JP/KO sources and a 15-second overlay duration. Outgoing English and incoming Korean are defaults; choose your own target languages. Use **잡담도 번역** (Include casual chat) when testing greetings.

### 2. Find the controls

| Menu | Purpose |
| --- | --- |
| 채팅 대시보드 | Outgoing hotkey/language, incoming languages, automatic crop, OCR and overlay switches |
| 번역 엔진 | Local engine/model selection, installation, warm-up, language-region context |
| 오버레이 설정 | Move, resize, lock, duration, transparency and font size |
| 번역 테스트 · 사전 | Text translation tests, compatibility checks and custom glossary |
| 시작 가이드 · 점검 | Recommended setup, diagnostics, repair and diagnostic export |

The OCR selector, status and **OCR 설치 / OCR 준비** buttons are always visible on the chat dashboard. Only filters, stabilization and runtime-folder options are under **세부 설정 · 진단** (Advanced / diagnostics). Save settings after changes.

### 3. Translate outgoing chat

1. Select the target language under **보내는 채팅** (Outgoing chat).
2. Bring a supported game to the foreground and open chat with Enter.
3. Type a message and press the default **backslash `\`** hotkey.
4. Wait for replacement; avoid typing during processing.
5. Review the result and **press Enter yourself to send it**.

Use the hotkey only with the chat input open. It selects/copies the input and replaces it with translated text.

Click **입력** beside the hotkey to assign a single key or combination; Esc cancels capture. Avoid movement and ability keys. Some keyboards label the backslash key `₩`; `Oem5` is its key identifier. The hotkey is active in supported foreground games, so use the app's translation test instead of a text editor. Text already in the target language may be skipped.

### 4. Translate incoming chat

1. Launch a supported game. Try borderless windowed mode if the overlay is not visible.
2. Under **받는 채팅 · OCR**, check the game and languages:
   - **EN / JP / KO:** message source languages to translate; multiple selections are allowed.
   - **출력:** the language displayed in the overlay.
3. Click **OCR 영역 표시** (Show OCR region) to inspect the automatic crop; click again to hide it.
4. Use **추천 영역 새로고침** (Refresh recommended region) if needed. There is no manual crop selector.
5. With chat visible, run **OCR 테스트** to inspect recognized text and extracted message bodies.
6. Click **OCR 시작**, ensure the overlay is enabled, then test a **new message**.

The crop follows game-client position and resolution. Without a detected game, the outline is a monitor preview; starting OCR requires detection of a supported game.

The VALORANT preset uses bottom-left client coordinates: message OCR covers **X 1.37–24.5%, Y 4.7–27.2%** (input row below 4.7% is excluded). Brief Enter opens **latest-line-only** OCR; holding the input open for **4 seconds** (configurable under **Advanced OCR → Full-chat wait**) switches to full-chat translation.

#### Why messages can be skipped

Startup chat, previously processed text, system/broadcast messages, input rows and text already in the output language may be excluded. The briefing filter can also exclude casual chat.

Nicknames and channel labels are separated from the body. **Your messages are not excluded merely because you sent them**, but repeated identical text can be deduplicated.

For greetings, choose **세부 설정 · 진단 → 잡담도 번역**, then send a different new message.

#### OCR options

Setup needs no expanded menu. Under **세부 설정 · 진단**:

- **채팅 필터 · OCR 세부 조정:** casual-chat filtering, stabilization, small-text enhancement and two-frame agreement.
- The OCR selector and preparation controls are directly on the incoming-chat card.
- **OCR 처리 상세:** recognition, exclusions, queue and translation status.

Windows OCR automatically checks the latest portion and full panel while reducing work on unchanged frames. No manual dual-region or map configuration is needed.

### 5. Adjust the overlay

Open **오버레이 설정 → 오버레이 위치 · 크기 조정**.

1. Drag the top bar to move.
2. Drag the bottom-right handle to resize.
3. Click **완료 · 잠금** (Done / lock) to save and restore click-through.

A temporary editing surface appears even with a fully transparent background.

- Duration: 5, 10, 15, 30 or 60 seconds, or keep visible.
- Background and border transparency: independently 0–100%; 100% is transparent.
- Font size: 11–32px.
- Click-through / position lock prevents the overlay from intercepting gameplay clicks.

**오버레이 끄기** only hides the overlay. **OCR 중지** stops OCR processing. The OCR region outline is a separate diagnostic display, not the translation overlay.

### 6. Choose engines

#### Translation

| Engine | Use |
| --- | --- |
| Local AI first + Lite fallback | Default. Known expressions can use rules; general text uses AI first, with Lite attempted when needed |
| Local AI | Hy-MT2 / Qwen / TranslateGemma; larger models can use more memory and add latency |
| Valtrans Lite | Lightweight CPU translation; Japanese/Korean may pass through English and lose meaning |

To switch models: select the model → **로컬 AI 설치** → **예열 상태 확인** → save. Prepare Lite with **Valtrans Lite 준비**. Downloaded does not necessarily mean warmed up in memory.

**서버·언어권 참고** is contextual guidance for slang, not a network connection or source-language filter. Shared proper-name dictionaries replace map-specific selection; custom terminology can still be added.

#### Default PaddleOCR-VL

PaddleOCR-VL is the **default OCR engine** for mixed-language chat. Prepare its separate runtime and model before first use. It remains experimental; use Windows OCR as a lighter alternative if your GPU environment is unsuitable.

1. Click **시작 가이드 · 점검 → OCR 설치 · 준비**. Recommended-engine setup also includes this step.
2. Approve setup to download required tools and models automatically. Progress and errors appear in the guide.
3. Model preparation follows installation automatically. Existing runtimes are reused; use **OCR 설치** on the dashboard to rerun installation or **OCR 준비** to continue.
4. Compare **OCR 테스트** results before starting OCR.

The current setup targets NVIDIA GPUs. Allow several GB of downloads and about 10GB free disk space. The measured development setup used roughly 2GB additional GPU memory and seconds per recognition pass; actual performance varies. Windows language packs are not required for this engine, but source-language checkboxes still filter translation.

Windows enhancement, dual-region checks and two-frame agreement do not apply to Paddle. Release its owned worker with **취소 · 메모리 해제**, OCR stop or app exit. Higher accuracy or faster processing is not guaranteed.

#### Fast OCR · Hybrid OCR (v0.3.0)

For Chinese and mixed-language chat, choose **Fast OCR (PP-OCRv5)** or **Hybrid OCR (Fast with Paddle VL fallback)** on the incoming-chat dashboard.

| Engine | Notes |
| --- | --- |
| Fast OCR | CPU-friendly; install with `Ocr/Setup-FastOcr.ps1` |
| Hybrid OCR | Fast first, Paddle VL only when confidence is low |
| PaddleOCR-VL | Default; NVIDIA GPU required |
| Windows OCR | Lighter alternative; language packs required |

Use **OCR 설치 · 준비** after selecting the engine.

### 7. Test translation and add terminology

Open **번역 테스트 · 사전**, select the test route and target language, enter text and click **번역 테스트**. Review the output, processing route and elapsed time.

| Test | Checks |
| --- | --- |
| 번역 테스트 | Translation of typed text, not screen recognition |
| OCR 테스트 | Current crop recognition and extracted message bodies |
| 품질 자가 테스트 | Built-in rule behavior |
| 엔진 호환성 검사 | Actual example requests and preservation of key facts |

Passing a compatibility check does not guarantee accurate translation of every sentence.

In **팀 전용 사전**, enter one `source=preferred spelling` pair per line, such as `제트=Jett`, and save. Prefer specific names/callouts over common short words, and test the results.

### 8. Troubleshoot

| Problem | Check |
| --- | --- |
| App does not start | Extract the full ZIP; check bundled DLLs and .NET 10 Desktop Runtime |
| Outgoing replacement fails | Supported foreground game, open chat input, target language, hotkey conflict, then translation test |
| No incoming translations | OCR/overlay switches, source languages, region outline, OCR test, casual-chat filter, then a new message |
| Nonsensical result | Inspect OCR source first; if correct, test that same text through translation |
| Cannot move overlay | Use the dedicated overlay position/size editing button |
| Delay or FPS loss | Compare the 1.8B model and the lighter Windows OCR alternative; stop unnecessary OCR |
| Lite output withheld | Compare hybrid/local AI and inspect counts, directions and negation |
| Paddle installation fails | Read the guide status; check network, NVIDIA environment and disk space, then retry OCR setup |

Use **시작 가이드 · 점검 → 전체 점검** for diagnostics and **진단 저장** to export details. Review files before sharing.

When reporting in [Discord](https://discord.gg/GMYuAJ8bZ2), include app version, game, resolution/Windows scaling, translation/OCR engines, source/output and OCR status.

### 9. Updates, data and limitations

- Close the app before updating. Extract the new ZIP into a separate folder and run its executable.
- Settings and Lite data live under `%LOCALAPPDATA%\Valtrans`; Ollama manages its own model storage.
- New settings and **Apply recommended settings** select PaddleOCR-VL. Existing Windows OCR settings switch to Paddle once on first launch after this update. A later manual Windows OCR selection is preserved after saving.
- Old cloud-engine selections migrate to the local default. Retired API keys are omitted on the next settings save.
- External translation APIs, DLX/Docker setup, manual crop/calibration and map selection are no longer available. Existing Docker/WSL installations are not uninstalled.
- Setup downloads use the internet; OCR and chat translation run locally and share resources with the game.
- Recognition, translation and deduplication can be wrong. Review important counts, directions and negation.
- Versions change only when explicitly decided by the maintainer. This README may keep its baseline if usage does not change.

---

## 소스에서 빌드 / Build from source

.NET 10 SDK가 설치된 Windows 개발 환경에서 실행합니다.<br>
Run in a Windows development environment with the .NET 10 SDK.

```powershell
dotnet publish Valtrans.csproj -c Release -o publish
```

결과 / Output: `publish/Valtrans.exe`. 일반 사용자는 빌드 없이 배포 ZIP을 사용하면 됩니다.<br>
End users can use the release ZIP without building.

PowerShell 7 기본 회귀 검사 / Basic regression checks:

```powershell
pwsh -NoProfile -STA -File scripts/Test-LocalFirst.ps1 -AssemblyPath publish/Valtrans.dll
pwsh -NoProfile -STA -File scripts/Test-QualityPipeline.ps1 -AssemblyPath publish/Valtrans.dll
pwsh -NoProfile -STA -File scripts/Test-OcrPipeline.ps1 -AssemblyPath publish/Valtrans.dll

# Agent / CI regression gate (Test Arena E2E, baseline compare)
pwsh -NoProfile -File scripts/test.ps1 -Profile Full
```

Test Arena E2E는 실제 Capture→OCR 경로를 사용합니다. Arena 창 위치는 `%LOCALAPPDATA%\Valtrans\TestArena\window-layout.json`에 저장되며, `scripts/Save-ArenaLayout.ps1`로 현재 위치를 저장할 수 있습니다. `test.ps1`의 E2E 프로필은 기본으로 `-NoFocusArena`를 사용합니다(캡처 시점에 Arena가 보이면 됩니다).

| 스크립트 | 시나리오 | OCR |
| --- | --- | --- |
| `Run-E2ESmoke.ps1` | `smoke_basic_001` | Windows |
| `Run-E2EZhSmoke.ps1` | `smoke_zh_mixed_001` | Fast |
| `Run-E2EHybridSmoke.ps1` | `smoke_zh_mixed_001` | Hybrid |

합성 OCR·모의 응답 검사는 실게임 정확도 측정과 다릅니다.<br>
Synthetic OCR and mocked-response tests are not real-game accuracy measurements.

| Script | Scenario | OCR |
| --- | --- | --- |
| `Run-E2ESmoke.ps1` | `smoke_basic_001` | Windows |
| `Run-E2EZhSmoke.ps1` | `smoke_zh_mixed_001` | Fast |
| `Run-E2EHybridSmoke.ps1` | `smoke_zh_mixed_001` | Hybrid |
