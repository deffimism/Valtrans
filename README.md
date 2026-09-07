# Valtrans

게임 채팅 실시간 번역 도구 · Real-time game chat translator

[한국어 사용 안내](#korean) · [English guide](#english)

공식 Discord 서버에 참여할 수 있습니다: [Valtrans 공식 Discord](https://discord.gg/ZTa5EKZcsW)  
Join our official Discord server: [Valtrans Official Discord](https://discord.gg/ZTa5EKZcsW)

> **작성 기준 버전 / Documentation baseline: v0.2.0**  
> **작성 기준일 / Last reviewed: 2026-09-07**  
> 이 README는 위 버전의 사용법을 기준으로 작성되었습니다. 프로그램 버전이 올라가더라도 사용법이 변하지 않으면 README는 업데이트되지 않을 수 있습니다. 문서의 기준 버전과 설치된 앱 버전이 달라도 사용법은 동일할 수 있습니다.  
> This README describes usage as of the version above. It may not be updated for every application release if the instructions remain unchanged. A different documentation version does not necessarily mean the instructions are outdated.

버전은 `주.기능.수정` 형식입니다. 새로운 기능은 두 번째 자리(기능)를 올리고, 버그 수정·UI 조정·기타 세부 수정은 세 번째 자리(수정)를 올립니다.  
Versions use the `major.feature.patch` format. New features increase the second digit; bug fixes, UI adjustments, and other minor refinements increase the third digit.

<a id="korean"></a>

## 한국어 사용 안내

### 1. 프로그램 소개와 준비 사항

Valtrans는 VALORANT와 Apex Legends에서 사용할 수 있는 Windows용 채팅 번역 도구입니다.

- **보내는 채팅:** 게임 채팅창에 쓴 문장을 단축키로 번역하고 입력 내용을 교체합니다.
- **받는 채팅:** 지정한 화면 영역을 Windows OCR로 읽고 번역문을 별도 오버레이에 표시합니다.
- **지원 번역 언어:** 한국어(KO), 영어(EN), 일본어(JP).
- **기본 번역 방식:** 무료 로컬 AI 우선 + Lite 대체. 처음 사용하는 기본 모델은 Hy-MT2 1.8B입니다.

Windows 10/11과 **.NET 10 Desktop Runtime**이 필요합니다. 로컬 AI를 사용하려면 Ollama와 선택한 모델도 준비해야 하며, 앱에서 설치를 진행할 수 있습니다. 최초 엔진·모델·OCR 언어팩 다운로드에는 인터넷이 필요합니다.

**무료 로컬 모드에는 API 키, Docker, WSL이 필요하지 않습니다.** 준비된 로컬 모델은 채팅을 외부 번역 서버로 보내지 않습니다. 대신 게임과 PC의 CPU/GPU·메모리를 함께 사용합니다.

### 2. 처음 실행하기

1. 배포 폴더의 `Valtrans.exe`를 실행합니다. 이 저장소에서 빌드한 경우 위치는 `publish/Valtrans.exe`입니다.
2. 실행 파일만 따로 옮기지 말고 같은 폴더의 DLL 등 동봉 파일도 함께 보관합니다.
3. 왼쪽 **시작 가이드 · 점검**을 엽니다.
4. **전체 점검**으로 필요한 엔진, OCR 언어팩, 영역 설정을 확인합니다.
5. 처음이라면 **권장 엔진 준비**를 누르고 설치·다운로드·예열이 끝날 때까지 기다립니다.
6. **번역 엔진**에서 로컬 AI가 **예열 완료** 상태인지 확인합니다.
7. 필요한 OCR 언어팩이 없으면 **채팅 대시보드 → 언어팩**에서 설치합니다. Windows 관리자 승인이나 재시작 안내가 나오면 안내를 따릅니다.
8. 아래 순서대로 언어와 채팅 영역을 설정한 뒤 상단 **설정 저장**을 누릅니다.

**주의:** **권장값 적용**과 **권장 엔진 준비**는 기본 엔진·모델·단축키·언어·OCR 및 오버레이 설정을 권장값으로 바꿉니다. 이미 설정을 맞춰 두었다면 개별 엔진 카드의 설치 버튼을 사용하세요.

### 3. 화면 구성

| 메뉴 | 할 수 있는 일 |
| --- | --- |
| 채팅 대시보드 | 보내는 채팅 언어·단축키, 게임·맵·OCR 영역, 원문 언어, OCR와 오버레이 켜기/끄기 |
| 번역 엔진 | 번역 방식·로컬 모델 선택, 설치·예열, 선택형 API 키 입력 |
| 오버레이 설정 | 이동 잠금, 표시 시간, 배경·테두리 투명도, 글자 크기, OCR 필터·안정화 |
| 번역 테스트 · 사전 | 게임 없이 번역 시험, 처리 경로 확인, 팀 전용 사전 편집 |
| 시작 가이드 · 점검 | 준비 상태 확인, 권장 설정, 자동 복구, 진단 저장 |

설정을 바꾼 뒤에는 상단 **설정 저장**을 눌러 주세요.

### 4. 내가 보내는 채팅 번역하기

1. **채팅 대시보드 → 보내는 채팅**에서 번역할 **목표 언어**를 선택합니다. 한국어로 입력해 영어로 보낼 때는 **English**를 선택합니다.
2. 지원 게임을 전면에 두고 Enter로 게임 채팅 입력창을 엽니다.
3. 보내고 싶은 문장을 입력합니다.
4. 기본 단축키 **백슬래시 `\`**를 누릅니다.
5. 입력 내용이 선택되고 번역문으로 교체될 때까지 기다립니다.
6. 결과를 확인한 뒤 **Enter는 직접 눌러 전송**합니다.

예를 들어 한국어 문장을 입력한 뒤 영어 목표 언어로 번역할 수 있습니다. 실제 결과는 선택한 모델과 문맥에 따라 달라집니다.

**단축키 변경:** 단축키 옆 **입력** 버튼을 누르고 원하는 키 또는 조합키를 누릅니다. Esc는 지정 취소입니다. 단일 키도 가능하지만 이동·스킬 키처럼 게임에서 자주 쓰는 키는 피하세요. 단축키는 지원 게임이 전면에 있을 때 활성화되므로 메모장 등에서는 같은 방식으로 동작하지 않을 수 있습니다.

입력 언어와 목표 언어가 같거나 문장이 이미 처리된 표현이면 결과가 바뀌지 않을 수 있습니다. 번역 중에는 추가 입력을 잠시 멈추는 편이 좋습니다.

### 5. 팀원 채팅을 OCR로 번역하기

#### 언어와 게임 선택

1. **채팅 대시보드 → 받는 채팅 · OCR**에서 게임과 맵을 선택합니다.
2. **원문** 체크박스에서 읽고 싶은 언어를 선택합니다. 여러 언어를 동시에 선택할 수 있습니다.
3. **출력**에서는 번역 결과를 볼 언어 하나를 선택합니다.

예: 영어·일본어 채팅을 한국어로 보려면 **원문 EN + JP**, **출력 한국어**로 설정합니다. 여기서 원문은 상대가 쓴 언어이고, 출력은 내가 읽을 언어입니다.

#### OCR 영역 지정

1. 게임을 실행하고 채팅이 실제로 보이게 합니다.
2. **추천 영역**으로 시작하거나 **직접 선택**으로 채팅 메시지 영역을 지정합니다.
3. 채팅 본문이 들어오도록 잡되, 움직이는 배경·메뉴·입력창 등 불필요한 부분은 가능한 한 줄입니다.
4. **OCR 테스트**로 실제 인식된 글자를 확인합니다.
5. 글자가 잘리거나 다른 화면 요소가 섞이면 **영역 보정**에서 위치와 크기를 조정합니다.
6. 설정을 저장하고 **OCR 시작**, **오버레이 켜기**를 누릅니다.

영역은 게임·해상도별로 저장됩니다. **전면 게임에 맞춰 OCR 영역·맵 프로필 자동 전환**을 켜면 해당 게임의 저장 프로필을 사용합니다. 해상도, 창 모드, Windows 배율을 바꾼 뒤에는 영역을 다시 확인하세요.

#### 정상적으로 생략되는 경우

- OCR 시작 시 이미 떠 있던 채팅은 기준 화면으로 사용하므로 바로 번역되지 않을 수 있습니다. **새 채팅으로 확인하세요.**
- 이미 번역한 과거 채팅, 목표 언어로 적힌 문장, 시스템 문구 등은 생략할 수 있습니다.
- 채널명과 닉네임은 본문 판정에서 제외합니다. 예: `(파티) Player: watch left`.
- **브리핑만** 필터에서는 긴 잡담이나 불평이 제외될 수 있습니다. 필요하면 **오버레이 설정 → OCR 채팅 필터 → 잡담도 번역**을 선택합니다.
- 화면에 변화가 없으면 OCR 작업을 줄입니다. 같은 문장을 계속 표시하지 않는 것은 정상 동작일 수 있습니다.

### 6. 오버레이 조절하기

**오버레이 설정**에서 다음 항목을 바꿀 수 있습니다.

- **이동·크기:** **클릭 통과 · 이동/크기 잠금**을 해제하고 오버레이의 이동 영역을 드래그합니다. 크기 조절 손잡이로 크기를 바꿉니다. 배치가 끝나면 다시 잠그면 게임 클릭을 방해하지 않습니다.
- **표시 시간:** 5·10·15·30·60초 또는 **계속 표시**. 기본은 15초입니다.
- **배경·테두리 투명도:** 각각 0~100%. 숫자가 높을수록 더 투명합니다.
- **글자 크기:** 11~32px.
- **OCR 채팅 안정화:** 먼저 **균형**을 사용하고, 문자가 흔들리거나 잘리면 안정 쪽으로 조정합니다. 기다리는 시간이 늘어날 수 있습니다.

**작은 글자 자동 확대·대비 보정**, **두 프레임 합의**는 인식 흔들림을 줄이기 위한 옵션입니다. 성능과 인식 품질은 게임 화면에 따라 달라집니다.

오버레이만 숨기려면 **오버레이 끄기**, OCR 작업까지 중지하려면 **OCR 중지**를 사용합니다. 게임은 테두리 없는 창 모드에서 먼저 확인하세요.

### 7. 번역 엔진 선택하기

| 엔진 | 특징 |
| --- | --- |
| 무료 로컬 · AI 우선 + Lite 대체 | 기본값. 확실한 약어·콜은 사전으로 처리하고 선택한 AI가 실패하면 Lite를 시도합니다. |
| 로컬 AI | Hy-MT2 / Qwen / TranslateGemma 중 선택합니다. 큰 모델은 더 많은 자원을 사용하며 항상 더 정확한 것은 아닙니다. |
| Valtrans Lite | CPU INT8 경량 번역. 일본어↔한국어는 영어를 거쳐 품질 손실이 생길 수 있습니다. |
| DeepL 공식 API | 선택 사항. 별도 API 키가 필요하며 계정별 한도·요금이 적용됩니다. |
| GPT-4o mini | 선택 사항. OpenAI API 키와 서비스 이용 조건 확인이 필요합니다. |

로컬 모델을 바꿀 때는 모델 선택 → **로컬 AI 설치** → **예열 상태 확인** → **설정 저장** 순서로 진행합니다. 설치된 모델은 준비 상태를 확인하세요. Lite만 사용할 때는 **Valtrans Lite 준비**를 사용합니다.

Lite는 깨진 출력·반복·미번역·일부 의미 누락을 검사합니다. **Lite 번역 확인 필요**는 의심 결과를 보류했다는 뜻이며, 사용량 차단을 뜻하지 않습니다. 검사 통과도 번역 정확도를 보장하지는 않습니다.

**선택형 API 사용:**

1. 사용할 API 엔진을 직접 선택합니다.
2. 해당 서비스의 API 전용 키를 입력합니다. 발급 안내는 앱의 안내 버튼을 사용합니다.
3. DeepL은 **키·사용량 확인**, OpenAI는 **연결 확인**을 누릅니다.
4. **설정 저장** 후 번역 테스트로 확인합니다.

DeepL의 키 확인은 번역 없이 사용량만 조회하지만, 번역 테스트나 엔진 호환성 검사는 실제 번역 요청을 보냅니다. API 사용량이 발생할 수 있습니다. DeepL 키가 `:fx`로 끝나면 무료 API 주소, 그 외에는 Pro 주소를 사용합니다. 자동으로 유료 키나 공개 서버로 전환하지 않습니다.

### 8. 번역 테스트와 팀 전용 사전

**번역 테스트 · 사전**에서:

1. 테스트 방식으로 보내는 채팅 또는 받는 OCR 경로를 선택합니다.
2. 번역 목표 언어와 테스트 원문을 입력합니다. **예시 채우기**로 시작해도 됩니다.
3. **번역 테스트**를 누르고 번역 결과·처리 경로·시간을 확인합니다.

이 테스트는 입력한 글자의 번역 경로를 확인합니다. 실제 화면 캡처와 OCR 정확도를 확인하려면 별도의 **OCR 테스트**를 사용하세요.

- **품질 자가 테스트:** 내장 규칙 점검. 모든 실제 번역의 정확도를 보증하지 않습니다.
- **엔진 호환성 검사:** 선택 엔진에 실제 예문을 요청해 응답과 주요 사실 보존을 확인합니다.
- **팀 전용 사전:** 한 줄에 `원문=표기` 형식으로 입력하고 저장합니다.

```text
제트=Jett
후카=Hookah
```

너무 일반적인 단어를 전혀 다른 뜻으로 등록하면 정상 문장에도 영향을 줄 수 있으므로 번역 테스트로 확인하세요.

### 9. 문제가 생겼을 때

| 증상 | 먼저 확인할 사항 |
| --- | --- |
| 프로그램이 실행되지 않음 | .NET 10 Desktop Runtime과 배포 폴더의 동봉 파일을 확인합니다. |
| 단축키를 눌러도 교체되지 않음 | 지원 게임이 전면인지, 채팅 입력창이 열렸는지, 단축키 충돌과 목표 언어를 확인합니다. |
| OCR 번역이 나오지 않음 | OCR 시작·오버레이 켜짐·원문 언어·영역·언어팩·필터를 확인하고 새 채팅으로 시험합니다. |
| 한 줄을 엉뚱하게 읽음 | OCR 테스트 → 영역 보정 순서로 확인합니다. 영역이 너무 넓지 않은지 봅니다. |
| 오버레이를 옮길 수 없음 | 클릭 통과·이동/크기 잠금을 해제합니다. |
| 로컬 번역이 처음에 느림 | 모델 예열이 끝났는지 확인합니다. 큰 모델 대신 작은 모델로 비교해 봅니다. |
| Lite 결과가 보류됨 | 로컬 AI 또는 기본 복합 모드로 같은 원문을 시험합니다. 부정·숫자·방향을 직접 확인하세요. |
| API 연결 오류 | API 키, 해당 계정의 한도, 인터넷 연결을 확인하거나 무료 로컬 모드로 돌아갑니다. |

해결되지 않으면 **시작 가이드 · 점검 → 전체 점검**을 실행합니다. **자동 복구**는 안내된 설치·프로필 문제에 사용하고, **진단 저장**으로 상태 자료를 남길 수 있습니다.

### 10. 데이터와 주의 사항

- 설정과 Lite 파일은 `%LOCALAPPDATA%\Valtrans`에 저장됩니다. Ollama 모델은 Ollama의 별도 저장소를 사용합니다.
- API 키는 현재 Windows 사용자 계정으로 암호화하며 서비스별로 따로 보관합니다. 키를 저장하는 것만으로 외부 번역이 켜지지는 않습니다.
- 로컬 엔진은 채팅을 외부 번역 서버로 보내지 않습니다. API 엔진을 선택하면 해당 서비스로 전송됩니다.
- 현재 UI에서는 DLX·Docker 설치 설정을 제공하지 않으며 Docker를 자동 실행하지 않습니다.
- 번역·OCR은 틀릴 수 있습니다. 부정, 방향, 인원, 은어, 복합 문장은 특히 주의하세요. [품질 참고](docs/translation-quality.md)

---

<a id="english"></a>

## English guide

### 1. Overview and requirements

Valtrans is a Windows chat translator for VALORANT and Apex Legends.

- **Outgoing chat:** translate text already typed in the game's chat box and replace it with a hotkey.
- **Incoming chat:** read a selected screen region using Windows OCR and display translations in an overlay.
- **Translation languages:** Korean (KO), English (EN), and Japanese (JP).
- **Default engine:** free local AI first, with Lite as a fallback. The initial default model is Hy-MT2 1.8B.

You need Windows 10/11 and the **.NET 10 Desktop Runtime**. Local AI also requires Ollama and the selected model; the app provides a setup flow. Initial engine, model, and OCR language-pack downloads require internet access.

**Free local mode does not require an API key, Docker, or WSL.** After setup, translation runs on your PC without sending chat to an external translation server. Local models share CPU/GPU and memory with your game.

The interface currently uses Korean labels. English explanations below include the actual button labels so you can find them; this guide does not imply an English interface option.

### 2. First-time setup

1. Run `Valtrans.exe` from the distribution folder. For a build in this repository, use `publish/Valtrans.exe`.
2. Keep the accompanying DLLs and other files next to the executable. Do not move only the EXE.
3. Open **시작 가이드 · 점검** (Getting started and diagnostics).
4. Click **전체 점검** (Check all) to review engines, OCR language packs, and the capture profile.
5. For a fresh setup, click **권장 엔진 준비** (Prepare recommended engines). Wait for downloads, installation, and model warm-up.
6. In **번역 엔진** (Translation engine), look for **예열 완료** (Warm-up complete).
7. If OCR languages are missing, use **채팅 대시보드 → 언어팩** (Chat dashboard → Language packs). Follow any Windows administrator-approval or restart instructions.
8. Configure languages and the OCR region as described below, then click **설정 저장** (Save settings).

**Important:** **권장값 적용** (Apply recommended settings) and **권장 엔진 준비** also apply recommended engine, model, hotkey, language, OCR, and overlay settings. If you already customized the app, use individual engine setup buttons instead.

### 3. Navigation

| Menu label | Purpose |
| --- | --- |
| 채팅 대시보드 | Outgoing language/hotkey, game/map, OCR region and languages, OCR/overlay controls |
| 번역 엔진 | Engine/model selection, installation, warm-up, optional API credentials |
| 오버레이 설정 | Position lock, display duration, background/border transparency, font size, OCR filtering |
| 번역 테스트 · 사전 | Translation tests, processing routes, custom team glossary |
| 시작 가이드 · 점검 | Setup status, recommended settings, repair, diagnostic export |

Click **설정 저장** after changing settings.

### 4. Translating outgoing chat

1. In **채팅 대시보드 → 보내는 채팅** (Outgoing chat), select the **target language**. For example, choose English to translate a Korean message into English.
2. Bring a supported game to the foreground and press Enter to open its chat input.
3. Type your message.
4. Press the default hotkey: **backslash `\`**.
5. Wait for the app to select the input and replace it with the translation.
6. Review the result, then **press Enter yourself to send it**.

To change the hotkey, click **입력** next to the hotkey field and press a key or key combination. Press Esc to cancel hotkey capture. Single keys are supported, but avoid keys used for movement or abilities. The hotkey is enabled when a supported game is in the foreground; a text editor is not an equivalent test.

If the message is already in the target language, it may remain unchanged. Pause typing while replacement is in progress.

### 5. Translating incoming chat with OCR

#### Select languages

1. In **받는 채팅 · OCR** (Incoming chat/OCR), select your game and map.
2. Select the languages to recognize under **원문** (Source). Multiple selections are allowed.
3. Select one translation language under **출력** (Output).

For English and Japanese chat translated into Korean, select **EN + JP** as source languages and **한국어** as output. Source languages describe what teammates write; output is what you want to read.

#### Set the capture region

1. Run the game and make some chat messages visible.
2. Use **추천 영역** (Recommended region), or **직접 선택** (Select manually).
3. Include the message text while excluding unnecessary moving backgrounds, menus, and the chat input area where possible.
4. Click **OCR 테스트** (OCR test) to inspect the recognized text.
5. Use **영역 보정** (Region calibration) if text is clipped or unrelated UI is included.
6. Save settings, then click **OCR 시작** (Start OCR) and **오버레이 켜기** (Show overlay).

Regions are saved by game and resolution. The automatic foreground-game profile option loads the matching saved region and map. Recheck the region after changing resolution, window mode, or Windows scaling.

#### Messages that may be skipped normally

- Messages visible when OCR starts may be treated as the initial baseline. **Test with a new message.**
- Previously processed chat, text already in the output language, and system messages may be skipped.
- Channel labels and nicknames are separated from the message body.
- **브리핑만** (Briefings only) may filter long casual messages or complaints. For broader coverage, select **잡담도 번역** (Include casual chat) in the OCR chat filter.
- OCR work is reduced when the screen does not change. Repeatedly translating the same visible message is intentionally avoided.

### 6. Adjusting the overlay

Open **오버레이 설정** (Overlay settings).

- **Move and resize:** uncheck **클릭 통과 · 이동/크기 잠금** (Click-through and position/size lock). Drag the overlay's move area or use its resize handle. Lock it again afterward so clicks pass through to the game.
- **Display duration:** 5, 10, 15, 30, or 60 seconds, or **계속 표시** (Keep visible). The default is 15 seconds.
- **Background and border transparency:** independently adjustable from 0 to 100%. Higher values are more transparent.
- **Font size:** 11–32px.
- **OCR stabilization:** start with **균형** (Balanced). More stable settings can help unsettled text but add waiting time.

Small-text enhancement and two-frame agreement can reduce recognition instability. Their impact depends on the game screen.

Use **오버레이 끄기** to hide the overlay. Use **OCR 중지** to stop OCR processing as well. Try borderless windowed mode if the overlay is not visible over the game.

### 7. Choosing an engine

| Engine | What it does |
| --- | --- |
| Free local AI first + Lite fallback | Default. Exact known expressions use rules; other text uses the selected AI, with Lite attempted if AI fails. |
| Local AI | Choose Hy-MT2, Qwen, or TranslateGemma. Larger models use more resources and are not always more accurate. |
| Valtrans Lite | Lightweight CPU INT8 translation. Japanese/Korean translation goes through English and may lose meaning. |
| DeepL official API | Optional API key; account limits and pricing apply. |
| GPT-4o mini | Optional OpenAI API engine; review the service's usage conditions and billing. |

For a local model, select it, click **로컬 AI 설치** (Install local AI), check **예열 상태 확인** (Warm-up status), then save settings. For Lite-only setup, use **Valtrans Lite 준비**.

**Lite 번역 확인 필요** means a suspicious Lite output was withheld, not that you were rate-limited. Its checks cannot establish that every accepted translation is correct.

**Optional API setup:**

1. Explicitly select the API engine.
2. Enter that service's API key using the in-app key guide if needed.
3. Use **키·사용량 확인** for DeepL, or **연결 확인** for OpenAI.
4. Save settings and try a translation test.

DeepL's key check only queries usage. Translation tests and engine compatibility checks send actual translation requests and may consume your allowance. A DeepL key ending in `:fx` uses the Free API endpoint; other keys use the Pro endpoint. The app does not automatically switch to a paid key or public relay.

### 8. Testing and the custom glossary

In **번역 테스트 · 사전**:

1. Select outgoing-chat or incoming-OCR mode.
2. Choose the target language and enter a test message, or use **예시 채우기** (Fill example).
3. Click **번역 테스트** (Translate test).
4. Review the output, processing route, and elapsed time.

This tests translation of supplied text, not screen recognition. Use **OCR 테스트** for actual capture and recognition.

- **품질 자가 테스트:** checks built-in rules; it does not guarantee real-world translation accuracy.
- **엔진 호환성 검사:** sends example requests to the selected engine and checks responses and key facts.
- **팀 전용 사전:** enter one `source=preferred spelling` pair per line, then save.

For example, `제트=Jett` specifies a preferred character-name spelling. Avoid replacing very common words with unrelated meanings, and verify your entries with translation tests.

### 9. Troubleshooting

| Symptom | Check first |
| --- | --- |
| App will not start | Confirm the .NET 10 Desktop Runtime and all accompanying distribution files are present. |
| Hotkey does not replace text | Check the foreground game, open chat input, hotkey conflicts, and target language. |
| No incoming translation | Check OCR/overlay switches, source languages, region, language packs, and filter; test a new message. |
| Incorrect OCR text | Run OCR test, then region calibration; avoid an oversized capture area. |
| Overlay cannot move | Disable click-through and position/size lock. |
| First local translation is slow | Wait for warm-up. Compare a smaller model if resources are limited. |
| Lite output is withheld | Try the local AI or default hybrid engine and review directions, numbers, and negation. |
| API connection fails | Check the key, account allowance, and internet access, or return to free local mode. |

For further checks, open **시작 가이드 · 점검 → 전체 점검**. Use **자동 복구** (Automatic repair) for the reported setup/profile issues, or **진단 저장** (Export diagnostics) to save status information.

### 10. Data and limitations

- Settings and Lite files are stored under `%LOCALAPPDATA%\Valtrans`. Ollama manages its own model storage.
- API keys are encrypted for the current Windows user and stored separately by service. Saving a key alone does not activate cloud translation.
- Local engines do not send chat to an external translation server. Selecting an API engine sends text to that service.
- The current interface does not offer DLX/Docker setup and does not automatically start Docker.
- OCR and translation can be wrong, especially for negation, directions, counts, slang, and complex sentences. [Quality notes, in Korean](docs/translation-quality.md)

---

## 소스에서 빌드 / Build from source

.NET 10 SDK가 설치된 개발 환경에서 실행합니다. / Run in a development environment with the .NET 10 SDK installed.

```powershell
dotnet publish Valtrans.csproj -c Release -o publish
```

결과 / Output: `publish/Valtrans.exe`

PowerShell 7 회귀 테스트 / Regression tests with PowerShell 7:

```powershell
./scripts/Test-SlangTranslation.ps1
./scripts/Test-QualityPipeline.ps1
./scripts/Test-LocalFirst.ps1
./scripts/Test-LiteQuality.ps1
```

테스트는 기본적으로 현재 배포본을 검사합니다. 합성 OCR·모의 응답 검사는 실제 게임 정확도와 구분해야 합니다.  
Tests use the current published build by default. Synthetic OCR and mocked responses are not real-game accuracy measurements.
