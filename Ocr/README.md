# Paddle OCR 실험 모드 / Experimental Paddle OCR

v0.2.0-beta의 기본 OCR은 PaddleOCR-VL입니다. OCR 엔진과 번역 엔진은 각각 준비해야 합니다.
기존 Windows OCR 설정도 업데이트 후 첫 실행에서 Paddle로 한 번 전환됩니다. 이후에는 Windows OCR을 직접 선택하고 저장할 수 있습니다. NVIDIA GPU 환경이 맞지 않으면 Windows OCR을 대안으로 선택하세요.

## 한국어

1. NVIDIA GPU와 최신 드라이버를 준비하세요. 별도 GPU 메모리 약 2GB를 사용하며 게임/번역 모델과 경쟁합니다.
2. 설치 도구 uv가 없다면 공식 가이드에서 먼저 설치하세요: https://docs.astral.sh/uv/getting-started/installation/
3. **채팅 대시보드 → OCR 설정 · 진단 → OCR 엔진 · 설치와 준비**을 열고 **실행 환경 설치**를 누르세요.
   Python, GPU 라이브러리, 공식 OCR 모델을 별도 폴더에 받습니다. 최초 다운로드는 수 GB이며 충분한 디스크 공간(약 10GB 여유)을 권장합니다.
   관리자 권한·Docker·WSL·API 키는 필요하지 않습니다. 시스템 Python/PATH는 바꾸지 않습니다.
4. **PaddleOCR-VL**을 선택하고 **로컬 OCR 준비 · 확인**이 완료되는지 확인하세요.
5. 게임을 실행하고 자동 추천된 범위를 **OCR 영역 표시**로 확인한 뒤 **OCR 테스트**에서 원문과 추출 본문을 확인하세요.
6. **OCR 시작**을 누르면 현재 보이는 채팅은 기준 화면으로 저장하고 이후 새 본문만 번역합니다.

- 기존에 별도로 준비한 실행 환경은 **실행 환경 폴더**로 `model.json`과 `.venv`가 들어 있는 폴더를 선택할 수 있습니다.
- 이 PC의 개발용 `artifacts/ocr-vl`이 준비돼 있다면 자동으로 찾습니다. 다른 PC에는 모델이 자동 포함되지 않습니다.
- Windows OCR과 달리 별도 Windows 언어팩이 필요하지 않습니다. 원문 언어 체크는 번역할 본문을 선택합니다.
- 전체 영역을 한 번 읽습니다. 이중 영역/Windows 확대 보정/두 프레임 확인 옵션은 이 모드에 적용하지 않습니다.
- 처음 준비는 오래 걸릴 수 있고 인식에도 수 초가 걸릴 수 있습니다. 수치는 PC/영역/게임 부하에 따라 달라집니다.
- 준비 상태는 실제 모델 적재 확인입니다. 번역 모델의 예열 상태와는 다릅니다.
- 읽다가 시간 제한에 걸리면 잘린 문장을 번역하지 않습니다. 연속 3회 오류면 OCR을 중지하고 원인을 표시합니다.
- **취소 · 메모리 해제**, OCR 중지, 엔진 변경, 앱 종료 시 앱이 시작한 OCR 프로세스만 종료합니다.
- 설치/모델 다운로드만 인터넷을 사용합니다. 인식은 로컬 전용이며 스크린샷을 업로드하지 않습니다.
- 실행 파일만 복사하지 말고 배포 폴더의 `Ocr` 폴더도 함께 유지하세요. 실행 환경을 이동했다면 다시 설치/준비하세요.
- 일본어 `二/ニ` 등의 혼동은 남을 수 있습니다. 게임 중 프레임 영향은 자신의 환경에서 확인하세요.

설치가 실패하면 아래 명령으로 오류 내용을 확인할 수 있습니다. 배포 폴더에서 실행하세요.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Ocr\Setup.ps1 -RuntimeDirectory "$env:LOCALAPPDATA\Valtrans\OcrRuntime"
```

## English

PaddleOCR-VL is the default in v0.2.0-beta. Existing Windows OCR settings switch to Paddle once after this update; a later manual Windows selection is preserved after saving. Use Windows OCR as a lighter alternative if needed.

Open **OCR settings / diagnostics → OCR engine / installation and preparation** on the chat dashboard. Install **uv** from its official site first if needed.
Use the runtime installation button to download a private Python environment, CUDA libraries and the pinned official PaddleOCR-VL model.
Allow several GB of download and about 10GB free disk space. No administrator privileges, Docker, WSL or API key are required.

Select PaddleOCR-VL, prepare the model, check the automatically recommended chat outline, and run OCR Test before starting OCR.
Source-language checkboxes control which message bodies get translated, not which scripts the model can recognize.
This engine reads the full crop once; Windows enhancement, dual-region and two-frame consensus do not apply.
Expect seconds of latency and roughly 2GB additional GPU memory, depending on the workload. Actual game FPS is not guaranteed.

Inference is offline and images are never uploaded. Cancellation, OCR stop and app exit terminate only the owned OCR worker.
Incomplete output is discarded rather than translated. Three consecutive errors stop OCR with an explanation.
Keep the bundled `Ocr` directory beside the application. Existing runtimes can be selected by their folder containing `.venv` and `model.json`.

Official model (Apache-2.0): https://huggingface.co/PaddlePaddle/PaddleOCR-VL-1.5
Tested revision: `2a4195faa5e7914c12f2fc601d72c81caf8d2da5`.
