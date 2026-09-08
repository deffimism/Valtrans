# 다국어 OCR 비교 실험

Valtrans 기본 OCR, 번역 모델, 사용자 설정 및 버전을 바꾸지 않는 별도 실험입니다.
공식 `PaddlePaddle/PaddleOCR-VL-1.5`를 Windows에서 Transformers/CUDA로 실행합니다.
Paddle 공식 전체 문서 파이프라인이나 최적화된 llama.cpp와 동일한 속도라고 해석하지 마세요.

## 준비

- PowerShell 7, uv, Python 3.12, NVIDIA CUDA 호환 GPU가 필요합니다.
- 실행 환경과 모델은 `artifacts/ocr-vl`에 저장됩니다. 수 GB의 디스크 공간이 필요합니다.
- 라이브러리/모델 다운로드만 인터넷을 사용합니다. 스크린샷은 업로드하지 않습니다.
- 모델 원격 Python 코드는 실행하지 않습니다 (`trust_remote_code=False`).
- 재다운로드 시 모델 커밋은 `model.json`에 기록됩니다. 비교 보고서에도 커밋과 라이브러리 버전을 남깁니다.

저장소 루트에서:

```powershell
pwsh -NoProfile -File scripts/ocr-vl/Setup.ps1 -PythonPath 'C:\경로\python.exe'
```

## 비교 실행

`cases.json`은 사용자가 제공한 게임 스크린샷 3개의 파일명, 채팅 영역 좌표와 수동 전사 정답입니다.
기본 원본 위치는 Windows TEMP입니다. 원본이 삭제됐다면 동일한 스크린샷을 다시 준비하고
`--images 'C:\스크린샷폴더'`를 지정하세요. 이미지는 저장소에 포함하지 않습니다.
새 스크린샷은 `--manifest`로 별도 사례 파일을 넘길 수 있습니다.

```powershell
$env:PYTHONIOENCODING = 'utf-8'
& artifacts/ocr-vl/.venv/Scripts/python.exe scripts/ocr-vl/benchmark.py --self-test
& artifacts/ocr-vl/.venv/Scripts/python.exe scripts/ocr-vl/benchmark.py --prepare-only
pwsh -NoProfile -File scripts/ocr-vl/Compare-WindowsOcr.ps1
& artifacts/ocr-vl/.venv/Scripts/python.exe scripts/ocr-vl/benchmark.py
& artifacts/ocr-vl/.venv/Scripts/python.exe scripts/ocr-vl/summarize.py
```

출력: `artifacts/ocr-vl/results/{inputs,windows,paddle,summary}.json`.
양쪽 엔진이 같은 픽셀을 읽었는지 SHA256으로 검사합니다.
Windows 비교는 현재 배포 DLL의 원본 이미지 인식/언어별 행 선택을 사용합니다.
실시간 영역 추적, 확대 보정, 중복 제거, 번역 시간은 포함하지 않습니다.

Paddle은 `OCR:`만 입력받고 정답/게임 사전은 제공받지 않습니다.
기본 최대 생성 512토큰, 생성 시간 제한 45초, 사례당 2회입니다.
시간 제한은 생성 단계의 소프트 제한으로, 모델 로드나 정지된 GPU 연산을 강제 종료하는 장치는 아닙니다.
`finished=false`인 출력은 잘린 결과일 수 있어 정상 인식으로 취급하면 안 됩니다.
`--task spotting --output artifacts/ocr-vl/spotting`은 위치 추출 진단용이며
OCR 텍스트 정답 점수와 직접 비교하지 않습니다.

공식 가중치의 `use_cache=false` 기본값은 순차 생성에 불리할 수 있어 테스트는 KV 캐시를 켭니다.
`--no-kv-cache`로 비활성화, `--threads`로 CPU 스레드 수(기본 4)를 지정해 별도 비교할 수 있습니다.
다른 실행 옵션은 `--output`으로 결과 폴더를 분리하세요.

## 판정 기준

- CER은 공백만 무시한 문자 오류율입니다. 정확도/모델 신뢰도 퍼센트가 아닙니다.
- 핵심 단어 포함 검사는 방향·숫자의 의미 정확도를 보장하지 않습니다. 원문과 함께 사람이 확인해야 합니다.
- 특히 `二`(한자 2)와 `ニ`(가타카나 ni)의 혼동, 반복 채팅 누락, 시스템 문구 혼입을 확인합니다.
- 첫 호출과 이후 호출을 구분합니다. 모델 로드 시간은 별도이며 전체 실행 시간에는 최초 라이브러리 import도 추가됩니다.
- VRAM 수치는 PyTorch가 이 프로세스에서 할당/예약한 피크입니다. GPU 전체 사용량이 아닙니다.
- 정적 이미지 3개의 결과를 전체 게임 OCR 정확도나 실제 게임 FPS로 일반화하지 않습니다.
- 아직 앱에 통합되지 않았으며 프레임 변화 시 실행/결과 행 분리/취소/지연 상한/게임 성능 검증이 필요합니다.

테스트 프로세스가 끝나면 적재된 모델은 해제됩니다. 상시 서버나 시작 프로그램은 설치하지 않습니다.
정답·결과에는 닉네임과 채팅이 포함될 수 있으므로 `artifacts` 결과를 공개 저장소에 올리지 마세요.

공식 모델 및 실행 예시: https://huggingface.co/PaddlePaddle/PaddleOCR-VL-1.5
