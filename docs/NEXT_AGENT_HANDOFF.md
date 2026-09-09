# Valtrans 다음 작업 인수인계 문서

작성 기준: `v0.2.1-beta`  
목적: OCR 속도와 일본어·한국어 채팅 번역 품질을 개선하고, 앱·사이트·README·배포 파일을 같은 릴리스 상태로 맞춘다.

## 현재 기준과 변경 원칙

- 모든 변경사항은 한 번 되돌려진 상태에서 다시 시작한다. 현재 저장소 소스를 기준으로 판단한다.
- 이번 작업에서는 버전을 올리지 않는다. 버전 변경은 사용자가 명시했을 때만 한다.
- 기본 방향은 로컬 처리다. 외부 LLM API나 DLX 연동을 새로 넣지 않는다.
- 기본 OCR은 PaddleOCR-VL이다. 설치되지 않은 환경에서는 앱 안의 OCR 설치·준비 흐름이 안내되어야 한다.
- 기본 번역은 로컬 Hybrid이며, Hy-MT2와 Valtrans Lite를 사용한다.
- 채팅 헤더 `(팀) 닉네임:`·시스템 문구·입력줄은 번역 대상에서 제외한다.
- FPS 브리핑은 짧게 만들되 방향, 인원, 부정, 불확실성, 조건, 행동의 의미는 절대 잃지 않는다.
- `적 한 명` 같은 명시적 정보는 보존하되, 출력에 불필요한 `enemy`를 습관적으로 붙이지 않는다.
- 기존 사용자 변경사항을 초기화하거나 강제 checkout/reset하지 않는다.

## 이번에 처리할 작업

### 1. OCR 속도

대상 파일 후보:

- `Services/WindowsOcrService.cs`
- `MainWindow.xaml.cs`
- `Ocr/paddle_host.py`

확인·수정할 내용:

1. Paddle 경로에서 같은 화면을 해시 확인용과 OCR용으로 두 번 캡처하지 말고, 한 번 캡처한 PNG와 해시를 함께 사용한다.
2. 화면이 변하지 않으면 OCR을 재실행하지 않고 현재의 backoff 정책을 유지한다.
3. 실제 OCR 파이프라인이 느릴 때 안정화 지연과 2프레임 확인을 자동으로 줄인다. 단, 품질이 낮거나 방향·인원·부정 정보가 있는 경우 확인을 생략하지 않는다.
4. Paddle의 `max_new_tokens`는 채팅 OCR에 맞는 작은 상한을 사용해 긴 출력·환각 tail을 줄인다.
5. OCR 캡처·인식·보정·합의 시간을 기존 진단 표시에서 계속 확인할 수 있게 한다.

### 2. 일본어 송신·수신 품질

대상 파일 후보:

- `Services/TranslatorService.cs`
- `Services/GameTranslationPrompt.cs`
- `Services/GlossaryService.cs`
- `Services/GameChatFilterService.cs`
- `Services/ChatTextSanitizer.cs`

확인·수정할 내용:

1. 번역 전 `(팀) Player: わかりました` 같은 헤더를 제거한 뒤 언어를 감지한다. 헤더의 한글 때문에 일본어가 한국어로 오인되지 않아야 한다.
2. 일본어 원문과 일본어 로마자(`wakarimashita`, `hidari`, `migi`, `teki` 등)를 공통 사전에서 안정적으로 처리한다.
3. `左見て`, `右見て`, `左に敵が一人いる`, `左に敵がいる` 같은 방향·인원·존재 콜을 규칙으로 먼저 처리한다.
4. `왼쪽 조심해`는 영어에서 `watch left`가 되어야 하며 단순한 `be careful`로 뭉개지면 안 된다.
5. 일본어 조사와 한국어 조사를 제거할 때 `に`, `で`, `を`, `は`, `에`, `에서`, `을`, `를`을 위치 정규화에 반영한다.
6. `GameChatFilterService`가 `敵`, `相手`, `적`, `상대`, `enemy`, `opponent`를 전술 관련 단어로 인식해 브리핑 필터에서 버리지 않게 한다.

### 3. 한국어 → 영어 송신 품질

대상 파일 후보:

- `Services/GameTranslationPrompt.cs`
- `Services/TranslatorService.cs`
- `Services/GlossaryService.cs`
- `Services/TranslationRegressionService.cs`

프롬프트와 규칙은 모든 로컬 모델에 공통 적용한다. 특히 다음을 강제한다.

- `B 헤븐에 두 명` → `2 B Heaven`
- `왼쪽 조심해` → `watch left`
- `왼쪽으로 가지 마` → `don't go left`
- `왼쪽에 적이 있다` → `enemy left` 또는 의미를 보존하는 짧은 동등 표현
- `아마`, `maybe`, `かも`의 불확실성 보존
- `없다`, `not`, `ない`의 부정 보존
- `섬광 쓸 때까지 기다려` → `wait until I flash`
- 원문이 짧은 콜이면 짧은 콜로, 원문이 문장이면 실제 주어·서술어를 보존한 문장으로 번역

모델이 임의로 설명, 따옴표, 요약, `B has two people in Heaven` 같은 장문 문장형을 만들지 않도록 한다.

## 회귀 검증 항목

기존 테스트를 모두 실행하고, 아래 케이스를 추가하거나 확인한다.

```text
안녕하세요 -> hello
왼쪽 조심해 -> watch left
왼쪽으로 가지 마 -> don't go left
左見て -> watch left
右見て -> watch right
左に敵が一人いる -> 왼쪽 적 1명 또는 지정된 짧은 동등 표현
왼쪽에 적이 있다 -> enemy left 또는 지정된 짧은 동등 표현
(팀) Player: わかりました -> copy / 확인 / 지정된 대상 언어 결과
B 헤븐에 두 명 -> 2 B Heaven
내가 섬광 쓸 때까지 기다려 -> wait until I flash
```

실행 명령:

```powershell
dotnet publish Valtrans.csproj -c Release --no-restore -o publish
pwsh -NoProfile -File scripts/Test-LocalFirst.ps1 -AssemblyPath publish/Valtrans.dll
pwsh -NoProfile -File scripts/Test-QualityPipeline.ps1 -AssemblyPath publish/Valtrans.dll
pwsh -NoProfile -File scripts/Test-LiteQuality.ps1 -AssemblyPath publish/Valtrans.dll
pwsh -NoProfile -File scripts/Test-OcrPipeline.ps1 -AssemblyPath publish/Valtrans.dll
```

테스트 실패를 새로 추가한 규칙으로 억지 통과시키지 말고, 기존 의미 보존 검사를 우선한다.

## README와 사이트 동기화

앱 사용법이 바뀌면 다음을 같은 작업에서 맞춘다.

1. `README.md`의 한글·영어 사용법을 함께 수정한다.
2. 버전 표기는 사용자가 요청한 버전만 반영한다. 이번 기준은 `v0.2.1-beta`다.
3. Paddle이 기본 OCR이면 시작 가이드에 설치·준비 버튼, GPU 메모리 안내, Windows OCR 대체 경로를 설명한다.
4. 사이트 파일은 `site/index.html`, `site/terms.html`, `site/privacy.html`, `site/styles.css`, `site/site.js`를 확인한다.
5. 다운로드 링크는 `https://github.com/deffimism/Valtrans/releases`를 사용한다.
6. 공식 Discord 링크는 `https://discord.gg/GMYuAJ8bZ2`를 사용한다.
7. 후원 버튼은 Fairy 후원 링크로만 연결하고 프로젝트 관리 링크와 섞지 않는다.
8. 흰 배경에서 본문 글자가 사라지지 않도록 텍스트 색상·Pretendard·줄바꿈을 확인한다.
9. 사이트의 후원 이미지는 저장소의 `mahstering_donate.png`를 직접 원본 크기로 키우지 말고, 카드 안에서 `max-width`, `height:auto`, `object-fit:contain`으로 버튼·카드 크기와 균형을 맞춘다.

사이트 정적 파일을 묶을 때는 저장소 루트 기준으로 `site/`와 `server/`가 tar 안에 들어가야 한다.

```powershell
$releaseItems = (Get-ChildItem publish -Force | Where-Object { $_.Extension -ne '.zip' }).FullName
Compress-Archive -Path $releaseItems -DestinationPath releases/v0.2.1-beta.zip -Force
tar -czf releases/valtrans-support-v0.2.1-beta.tar.gz -C . site server
Get-FileHash -Algorithm SHA256 releases/valtrans-support-v0.2.1-beta.tar.gz
```

## 배포 순서

### 1) Windows PowerShell: 해시 확인 후 홈서버로 동기화

아래는 앱 ZIP과 사이트 서버 패키지를 각각 복사한 뒤 원본·대상 SHA256이 같은지 검증하는 표준 예시다. 실제 서버 폴더가 다르면 대상 경로만 확인해서 수정한다. 서버 주소는 `192.168.0.19`를 사용한다.

```powershell
$root = 'C:\Users\User\Documents\Codex\Valtrans'
$items = @(
  @{ src = Join-Path $root 'releases\v0.2.1-beta.zip'; dst = '\\192.168.0.19\HDD-Raid1\AppData\Valtrans\releases\v0.2.1-beta.zip' },
  @{ src = Join-Path $root 'releases\valtrans-support-v0.2.1-beta.tar.gz'; dst = '\\192.168.0.19\HDD-Raid1\AppData\Valtrans\server\valtrans-support-v0.2.1-beta.tar.gz' }
)
foreach ($item in $items) {
  Copy-Item -LiteralPath $item.src -Destination $item.dst -Force
  $srcHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $item.src).Hash
  $dstHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $item.dst).Hash
  if ($srcHash -ne $dstHash) { throw "HASH MISMATCH: $($item.src)" }
  "SYNC OK  $($item.src)  $srcHash"
}
```

### 2) SSH: 압축 해제·컨테이너 재빌드·상태 확인

tar는 `~/AppData/Valtrans`에서 풀어야 `site/`와 `server/`가 올바른 위치에 놓인다. `~/AppData/Valtrans/server` 안에서 풀면 잘못된 중첩 구조가 생길 수 있다.

```bash
ssh deffimism@192.168.0.19 'cd ~/AppData/Valtrans && sudo tar --no-same-owner --no-same-permissions -xzf server/valtrans-support-v0.2.1-beta.tar.gz -C . && cd server && sudo env DOCKER_CONFIG="$PWD/.docker-client" docker compose -f "$PWD/compose.yaml" --env-file "$PWD/.env" up -d --build --force-recreate --no-deps valtrans-support && curl -fsS http://192.168.0.19:13020/health'
```

권한 오류가 나면 `tar`를 서버 폴더 안에서 다시 풀지 말고 위처럼 `--no-same-owner --no-same-permissions`와 올바른 상위 경로를 사용한다. 컨테이너 포트는 `127.0.0.1`이 아니라 `192.168.0.19:13020`에 바인딩되어야 한다.

배포 후 확인 주소:

```text
https://valtrans.deffimism.com
https://valtrans.deffimism.com/terms
https://valtrans.deffimism.com/privacy
https://valtrans.deffimism.com/health
```

`HEAD` 요청은 서버 구현에 따라 405가 될 수 있으므로 정책 페이지는 브라우저 또는 `curl -i`의 GET으로 확인한다.

## 다음 에이전트용 작업 프롬프트

아래 내용을 그대로 다음 에이전트에게 전달한다.

```text
현재 작업 폴더는 C:\Users\User\Documents\Codex\Valtrans 이다. 사용자가 변경사항을 모두 Discard했으므로 현재 소스를 기준으로 다시 작업한다. 버전은 v0.2.1-beta로 유지하고, 사용자가 명시하지 않는 한 버전을 올리지 마라.

목표는 세 가지다.
1) OCR 속도 개선: Paddle 경로의 중복 화면 캡처 제거, 화면 변화 없을 때 재인식 방지, 느린 환경에서 안정화 지연·2프레임 확인을 자동 완화, Paddle 출력 상한 축소.
2) 일본어 송신·수신 품질 개선: `(팀) 닉네임:` 헤더를 언어 감지 전에 제거하고, 일본어·로마자 일본어·방향·인원·부정 콜을 보존한다.
3) 한국어→영어 송신 품질 개선: 모든 로컬 모델에 공통 게임 번역 프롬프트를 적용하고 `왼쪽 조심해 -> watch left`, `왼쪽으로 가지 마 -> don't go left`, `B 헤븐에 두 명 -> 2 B Heaven`, `섬광 쓸 때까지 기다려 -> wait until I flash`를 보장한다. 문장형 원문은 실제 의미를 보존하되 짧은 콜은 짧게 출력한다.

수정 후보는 Services/WindowsOcrService.cs, MainWindow.xaml.cs, Ocr/paddle_host.py, Services/TranslatorService.cs, Services/GameTranslationPrompt.cs, Services/GlossaryService.cs, Services/GameChatFilterService.cs, Services/TranslationRegressionService.cs다. 기존 사용자 변경사항을 reset/checkout으로 지우지 말고, apply_patch로 최소 범위만 수정한다.

추가로 일본어 테스트를 회귀 검증에 넣어라: `左見て`, `右見て`, `左に敵が一人いる`, `왼쪽에 적이 있다`, `(팀) Player: わかりました`. 적 명시 여부, 방향, 숫자, 부정을 임의로 삭제하지 마라.

수정 후 다음을 실행하라.
dotnet publish Valtrans.csproj -c Release --no-restore -o publish
pwsh -NoProfile -File scripts/Test-LocalFirst.ps1 -AssemblyPath publish/Valtrans.dll
pwsh -NoProfile -File scripts/Test-QualityPipeline.ps1 -AssemblyPath publish/Valtrans.dll
pwsh -NoProfile -File scripts/Test-LiteQuality.ps1 -AssemblyPath publish/Valtrans.dll
pwsh -NoProfile -File scripts/Test-OcrPipeline.ps1 -AssemblyPath publish/Valtrans.dll

테스트가 통과하면 v0.2.1-beta 이름을 유지한 채 releases/v0.2.1-beta.zip을 다시 만들고, site/ 또는 server/가 바뀌었으면 releases/valtrans-support-v0.2.1-beta.tar.gz도 다시 만든다. 앱 README와 site/index.html의 사용법·Paddle 기본값 설명이 실제 동작과 맞는지 확인한다. 사이트 링크는 GitHub Releases, Discord는 https://discord.gg/GMYuAJ8bZ2, 서버 주소는 192.168.0.19:13020을 사용한다.

배포 명령은 문서의 표준을 따른다. PowerShell에서는 UNC 경로로 복사하고 SHA256 원본·대상 해시를 비교한다. SSH에서는 ~/AppData/Valtrans에서 tar를 `--no-same-owner --no-same-permissions`로 풀고, `cd server` 후 `sudo env DOCKER_CONFIG="$PWD/.docker-client" docker compose -f "$PWD/compose.yaml" --env-file "$PWD/.env" up -d --build --force-recreate --no-deps valtrans-support`를 실행한다. 최종 보고에는 수정 파일, 테스트 결과, 재생성한 배포파일과 해시, 사이트 변경 여부를 명확히 적어라.
```
