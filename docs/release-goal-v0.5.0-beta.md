# v0.5.0-beta 릴리스 Goal

> 최신 진행: [translation-candidate-677-v050.md](translation-candidate-677-v050.md).677 검사 및 동일 DLL 실제960건 검토, 최신 ZIP/SHA82993F...검증 완료. 일부 의미 오류와 공개 준비가 남아 Goal ACTIVE. 이번 턴 progress이며 외부 blocker 없음. 아래 이전 빌드 표를 최신 수치로 사용하지 않는다.

2026-09-12 사용자 요청. 발견된 오역을 추적·개선하고 추가 문장까지 검사한다. v0.4.0-beta 결과를 기준선으로 보존한다.

## 최신 진행 — 2026-09-13 03:20 수신 필터 검사

[receive-filter-v050.md](receive-filter-v050.md)가 아래 후보 기록보다 최신이다.664 자동 검사 통과, 기본 Briefing 경로144건씩 두 단계 실제 검토 완료. 소스 변경은 아직617 ZIP에 들어있지 않다. 전역 프롬프트 변경의 회귀와 남은 의미 오류가 있어 공개 릴리스는 하지 않았다. 배포 도구는 자동 push/기존 release 덮어쓰기 없이 재작성하고 구문·dirty-source 차단만 검증했다. SSH 전용 권한·운영 health 재확인 완료, 기존 사이트 재시작 없음. Goal ACTIVE, blocker 없음, UI 자동화 재개 안 함.

## 최신 진행 — 2026-09-13 후보 검증·운영 배포 완료

상세 최신 기록은 [release-candidate-v050.md](release-candidate-v050.md). 아래 절들은 이전 빌드의 이력이며 현재 상태보다 우선하지 않는다.

- 이번 턴 progress: 610 빌드 실제 번역960건 판정, 화자/계획/비꼼 보완. 초기 클라이언트 패키지6/6 실제 Hybrid E2E 확인.
- 사이트 v0.5.0-beta를 전용 도구로 **실제 배포**했다. LAN/공식 도메인의 홈·health·terms·privacy·CSS·아이콘 모두200, 버전 일치. 기존 이미지/파일 백업, `.env`/후원 데이터/다른 컨테이너는 교체하지 않았다. 인증/권한 설치 추가 요청 불필요.
- 새 OCR 빈 폴더에서 Python/라이브러리70개/두 인식 모델 다운로드 및 실행 성공. Fast 단독 C→ㄷ 오독은 여전히 재현됐으며 설치 성공과 정확도를 구분한다.
- 정상7B 출력의 お前를 ‘앞’ 방향으로 세던 오탐 수정.617/617 테스트와 추가128건 실모델 회귀: 기존127건 동일,206-send-jp만 정상 복구(A109/W15/E3/X1).
- 컴퓨터 사용 스킬로 앱 대시보드/가이드/엔진 화면을 실제 확인했다. 가이드 OCR 제목이 Paddle로 고정된 문제와 툴팁의 잘못된 Lite 자동전환 설명을 수정.617/617 재통과, 검사용 창 종료 확인.
- 현재 클라이언트 ZIP SHA `145A92CF8E68080B2710FC94B21C3F9C8760E5C6EDFD8BE28B7C513254B11661`, DLL `C1355CDEAA28AD91FAED176635F88ACEE7ACD957CC70397CC2D0C3549A31AAF5`. 사이트 archive SHA는 배포된 `8C0D45BE6696DECC529D0DCF8C619D9364205BB646C269A60D76C2647B1F4BED` 그대로다.
- Goal ACTIVE. GitHub 공개 모델 asset/소스·릴리스 대응, 남은 의미 오류/오탐/속도 한계는 계속 작업한다. `Release-GitHub.ps1`은 구버전 자동push/exitcode 처리 문제 때문에 아직 실행하지 않았으며 수정/검증 전 실행 금지.

## 이전 진행 — 2026-09-13 의미 대조 및 오탐 보완

이번 턴도 progress. [translation-contrast-v050.md](translation-contrast-v050.md)에 실제 출력 판정과 한계를 기록했다.

- 기존 추가128건/모델 재검토: 1.8B A92/W17/E15/X4, 7B A106/W17/E3/X2. 알려진 회귀 표본이며 프롬프트 변경만의 효과라고 단정하지 않는다.
- 새 대조32건/모델에서 정상 번역 차단 원인을 재현해 `대신`, 단위 선행 `라운드 하나`, 어순 `site B`/`메인 A`, `하나는 아군`을 보완했다. 잡담의 명시적인 의미 부정도 보호한다.
- `contrast-final` 실제 재검사: 1.8B A11/W7/E7/X7, 7B A26/W6/E0/X0. 7B 잘못된 차단6건 해소, 1.8B 실제 사이트·부정·단위 누락은 차단 유지. 남은 의미 오류/모호함을 공개했으며 전체 무오류 판정이 아니다.
- 전체320×2 회귀와 변경분 전수 검토 완료 후, 선택적 지시가 없는 프롬프트의 불필요한 빈 줄을 제거하고 다시640건 실행했다. 최신 `prompt-layout-regression`: 1.8B A272/W36/E8/X4, 7B A304/W10/E5/X1. 두 번의 실행에서 변경66/30건 및44/8건 각각 직접 대조했으며 동일 출력/오류만 이전 판정을 유지했다. 단위 **500/500**, whitespace 검사 통과. 모든 비교 프로세스 종료. 다음 턴에 중복 재실행하지 말 것.
- 최신 DLL SHA `34E59CD62150F9E194B09FC3A52E539552C7480FC5F8EFED8C75BF140A6FCE9F`. 기존128/새32 대조 결과는 마지막 공백 변경 전 빌드이며 전체 회귀와 구분한다. 남은 원탭(건강상태→공격능력), 트레이드 지시→과거, 오퍼/딸피 복합 역할과 차단 원시 출력, 잡담 발화자/계획/비꼼 오류부터 계속한다. 상세는 위 의미 대조 문서의 마지막 절.
- 전용 sudo 권한 재확인 완료: `NOPASSWD: /DATA/.valtrans-admin/valtrans-deploy ""`. 서버 재시작/배포 없음. 앱·사이트 버전은 아직0.4이며 v0.5 패키지는 아직 생성하지 않았다. Goal ACTIVE, 막힌 상태가 아니다.

## 직전 진행 — 2026-09-13 OCR 성능

직전 Goal 턴은 Hybrid 후보 비교/원문 보존 검증 수정으로 progress였고, 이번 턴도 실제 구현·비교 검사를 수행했다. 최신 성능 상세는 [ocr-performance-v050.md](ocr-performance-v050.md)를 우선 참고한다.

- Fast 정확 픽셀 캐시128개: 저장 이미지7장×3회 출력/점수/좌표 변경0, 모델 배치54→18. 반복7장141.9/89.1ms. 첫 인식 및 전체 지연은 외부 부하 영향으로 변동이 커 일반 속도 향상률로 주장하지 않는다.
- Hybrid 단계별 시간/최종 채택 경로 계측과 캡처 시간 누락 수정. `ocr-multilingual-hybrid-exact-cache-v050.json` 실제6/6 PASS, 원문 전체 일치 확인. 전체997.8~6860.3ms로 일부 새 채팅 지연은 여전히 높다.
- 측정 중 CPU100%/GPU96%와 다른 프로젝트의 추론 작업들이 확인됐으며 그 작업은 변경·종료하지 않았다. 다른 작업이 지연의 유일 원인이라고 단정하지 않는다. Fast worker 소스/실행본 SHA 일치 확인.
- Python OCR12/12, 마지막 미사용 변수 제거까지 .NET464/464 통과, whitespace 검사 통과. 테스트/재생 작업은 모두 종료됐다. 영어 OCR README의 무조건적 Fast/Hybrid 추천을 실제 확인된 한계에 맞게 바로잡았다.
- Goal ACTIVE. 안정된 부하에서의 새 입력 지연, Fast 단독 C 오독/일반 KO 검출, 전체 번역 의미 회귀, v0.5 버전/문서/사이트/패키지/SHA/공개 모델 asset/서버 배포는 남아 있다. 외부 작업 때문에 모든 개선이 막힌 상태는 아니다.

## 승인된 범위

- 기존 작업 보존, 버전 v0.5.0-beta 변경, 앱/README/사이트/배포물 업데이트.
- 복합 모드는 품질 우선: AI 실패 시 검증된 표현에만 대체 허용. 별도 Lite 자유 문장 품질도 계속 개선·검사한다.
- 홈서버 배포 허용. 운영 `.env`·후원 데이터·다른 서비스는 보존. 복구 가능한 이전 아카이브/이미지 확인 후 Valtrans만 배포한다.
- 전용 SSH 키 생성/서버 공개키 등록 완료. 개인키는 저장소 밖 `.ssh/valtrans_deploy_ed25519`, 문서나 로그에 내용을 넣지 않는다. SSH 접속 및 Valtrans 전용 무인자 배포 권한 설치·확인 완료. 일반 Docker/sudo 권한은 확대하지 않았다.

## 작업 순서 / 완료 기준

1. `verified`/`verified-additional`의 모든 E/X와 모호한 W를 유형별 추적. 차단한 것을 정답으로 세지 않는다.
2. 발화 의도·행위자·승패·게임 동작·피해/금액 단위·일본어 지시/부정 범위·은어를 일반화된 문법/관련 문맥으로 보완. 문장 전체를 삼키는 부분 매칭 금지.
3. Lite 자유 문장 전처리·언어판정·입력/디코딩·피벗을 비교한다. 모델 한계를 단순히 빔 설정 문제로 단정하지 않는다.
4. 알려진 표본 전체와 새로운 변형/부정 대조군을 실제 엔진으로 재검사. 단위·파이프라인·실제 OCR Arena·성능 검사. 코드 변경에 쓴 예문은 회귀 표본으로 표기한다.
5. 버전/UI/README/사이트/릴리스 노트 동기화, 새 패키지·manifest·SHA256 확인.
6. 홈서버 권한·복구 경로 확보 후 동기화/배포, 내부 IP와 공개 URL 응답·버전 확인. GitHub 게시는 CLI/인증/변경 커밋 대상 확인 후 별도로 판단한다.

## 현재 상태

진행 중. 의미 오류 사례를 그대로 고정 답으로 외우는 것보다, 방향·인원·행동·시제와 절 구조를 보존하는 제한된 문법을 우선한다. 제한된 문법 밖은 로컬 모델을 사용하고 검증 한계를 공개한다. arbitrary free text 무오류 보증은 하지 않는다.

## 2026-09-12 후속 진행 (아직 릴리스 아님)

- `GlossaryService.ActionIntent.cs`: 전체 문장에 한정한 금지/허용·플래시 등지기·해체 속임·엄호/뒤돌기 의도, 요원+피해량, 일본어 위치+인원 문법 추가. 질문·인용·미완성 `넣었는데`는 단축 처리하지 않음.
- Hybrid는 규칙으로 확인된 표현을 먼저 처리하고, 로컬 AI 실패 시 검증되지 않은 Lite 자유 문장을 자동 대체하지 않음. 독립 Lite 모드는 계속 유지·개선 대상.
- HTTP 테스트 주입과 실패/취소/오프라인 규칙 테스트 추가. 단위 테스트 **274/274 통과**.
- 실제 `artifacts/quality-v050/phase1/hybrid18.jsonl` 320건 완료. 변경 출력 직접 대조, 출력/오류가 동일한 항목만 이전 판정 유지: **A253/W43/E15/X9**, P50 158ms/P95 243ms. 이전 320건 A225/W53/E32/X10. 알려진 회귀 표본 중간 결과이며 새 문장 일반화·7B/Lite·최종 실행파일 검증은 남아 있음.
- `scripts/Probe-LiteCandidate.py`로 공식 Helsinki `opus-mt-tc-big-ko-en` 고정 리비전 `fa26583a41d95346933f26b4cb8f9b700da0d445`를 별도 폴더에서 int8 변환, 96문장×2문장부호×2beam=384조건 실행. **대부분 빈 출력/무관한 출력: 토크나이저·변환 호환성부터 진단 필요**. 모델 품질 자체가 나쁘다고 단정하거나 앱에 반영하지 말 것. 모델은 `artifacts/lite-candidate-ko-en`, 격리 도구는 `artifacts/lite-candidate-runtime`. 기존 AppData Lite는 변경 없음.
- 앱/사이트 버전은 아직 0.4.0-beta. v0.5 패키지와 서버 배포는 아직 하지 않았음.

## 전용 서버 배포 도구 설치 진행

- `scripts/server/valtrans-deploy.py`: 고정 프로젝트·업로드 경로, 무인자 실행, SHA256+manifest, 링크/경로탈출 거부, Dockerfile/Compose 바이트 변경 거부. 기존 Compose 프로젝트 라벨을 읽어 동일 프로젝트/서비스만 교체. 검증된 파일만 별도 Docker 빌드 context에 넣어 `.env`와 후원 데이터를 제외. 파일/이미지 백업 및 실패 시 복구 코드 포함(실제 배포·복구 시험은 아직).
- 문서/기존 수동 배포 스크립트는 서버에서 덮어쓰지 않음. 설치된 helper 자체는 릴리스 아카이브로 갱신할 수 없음.
- ZimaOS `/root`, `/usr`는 읽기 전용이고 `/usr/local/sbin`도 없음. **helper 설치 경로는 `/DATA/.valtrans-admin/valtrans-deploy`**, root 소유 디렉터리. sudo 규칙은 그 파일의 인자 없는 실행만 허용. 무제한 sudo나 Docker 그룹 추가 금지.
- 업로드 inbox: `/DATA/valtrans-deploy-inbox/valtrans-support-release.tar.gz`와 `.sha256`. 프로젝트: `/media/HDD-Raid1/AppData/Valtrans`. 배포 호출: `sudo -n /DATA/.valtrans-admin/valtrans-deploy`.
- 사용자 승인: AppData sticky bit 추가, Valtrans `.env` 0600; 기존 `/etc/sudoers`와 `/etc/sudoers.d/zfs`를 0440으로 복구(내용 유지). 모두 완료, helper 설치 확인. 일반 사용자의 helper 읽기는 root 전용 부모 디렉터리 때문에 거부되는 것이 정상이며, 허용된 sudo 실행은 정상 동작한다.
- 최초 설치 파일은 서버 `/tmp/valtrans-bootstrap.GHHfRYfi/install-valtrans-deploy.py`, SHA256 `3642555a94974f62ba4211e61db3ae44f06810def992d84c562e5d652c11942e`. helper SHA256 `d17635ebc4df6866cacabefcb869ea7adc377fb2876270fc0a328ac5ff1698d0`. 로컬/서버 일치 확인. 설치 시 해시 검증한 바이트를 Python `exec(compile(...))`로 실행해 Markdown에서 `__name__`가 훼손되는 문제를 회피.
- 서버 일반 사용자로 archive 보안 5개 + 배포 transaction 3개 = **8개 통과**, 실제 패키지 선택 테스트 1개는 파일 미제공으로 skip. transaction은 임시 폴더에 실제 파일을 쓰고 Docker/권한/HTTP만 모의 처리한다. 빌드 실패·상태 검사 실패에서 이전 파일/이미지 복구, `.env`/후원 데이터 보존, 성공 경로 확인. **모의 `status: deployed` 출력은 운영 배포가 아니다.** Dockerfile/Compose 로컬·운영 해시 일치. 설치된 helper는 inbox에 아카이브가 없으면 배포 전 중단함을 실제 sudo로 확인했으며 운영 서비스는 재시작하지 않았다. bootstrap을 다시 요청할 필요 없다.

## 후속 품질/호환성 검증 (2026-09-12, 진행 중)

- `TacticalState`와 `CalloutCountRevision`: 아군/적 구분, 떨어진 스파이크, clear/rotate, 명시적인 조건부 피킹, 추정 인원과 불확실성, 이전 추정 인원→실제 인원 교정. 인원 교정 문법은 번역/검증기가 공유해 생략된 명사를 서로 다르게 해석하지 않도록 했다. 질문/인용/추가 절은 전체 문법에 맞지 않으면 단축하지 않는다.
- 단순 `save this round`를 무조건 "무기 보존"으로 확정하지 않는다. 경제 라운드인지 무기 세이브인지 원문이 구분하지 않으면 그 모호성을 유지한다.
- 정상 출력 차단 원인 직접 확인: `a Vandal`→`밴달을 하나`의 하나를 인원 추가로 읽음, `one person` 누락, 이전 추정 `と思った`를 현재 불확실성으로 오인, `haven't` 등 영어 축약 부정형 누락. 관련 대조군 테스트 추가. **332개 단위 테스트 통과.**
- Lite 설치는 다운로드/검증 전에 기존 모델을 지우지 않는다. 새 파일과 manifest를 먼저 준비한 뒤 같은 부모 내 staging을 교체하고 실패 시 이전 폴더로 복구한다. 다른 언어 모델 보존·staging 실패 테스트 포함.
- `phase2`의 공통 프롬프트 강화는 부정/시제 회귀가 생겨 **폐기**. `phase3`부터 공통 지시는 원래대로 되돌리고 관련 용어만 보완. `판`→match 안내는 잡담에서 사용하며 풀바이/세이브 등 라운드 문맥에 강제하지 않음(최신 변경은 후속 재검사 대상).

### 실제 동일 320개 표본 phase4

수정에 이미 사용한 회귀 표본. A=의미상 사용 가능, W=어색/애매, E=의미 오류, X=차단/실행 실패. 내부 PASS를 의미 정답으로 세지 않음.

| 엔진 | A | W | E | X | P50 | P95 |
|---|---:|---:|---:|---:|---:|---:|
| 1.8B | 275 | 36 | 6 | 3 | 135ms | 225ms |
| 7B | 297 | 8 | 10 | 5 | 361ms | 831ms |

- `artifacts/quality-v050/phase4`: 원시 640건·DLL SHA·수동 판정·집계 저장. 1.8B phase1 대비 변경 36건, 7B와 1.8B 사이 변경 179건은 각각 직접 읽어 평가. **현재 소스보다 이전 빌드**이며 최종 릴리스 성적으로 쓰지 말 것.
- 7B의 X 중 2건은 첫 적재 중 30초 취소이고 다음 요청이 12초 후 성공. 이후 7B VRAM 전량 적재 상태 확인(약 5.27GB, 1.8B는 약 1.45GB). 콜드 스타트와 예열 후 지연을 별도 기록해야 함. 냉시작 실패를 숨기거나 모두 번역 품질 탓으로 돌리지 않는다.
- 큰 모델도 지시→과거 서술, 남은 체력→공격 능력, 화자/대상, 경기/라운드 단위 등을 틀렸다. 자동으로 7B가 모든 문장에 더 정확하다고 판단하지 않는다.
- corpus 38 JP 원문 `相手のウルト使わせた`는 사용을 유도했다는 뜻으로 KO/EN reference와 완전히 동등하지 않음. 원문 자체 기준으로 평가하고 과거 corpus를 조용히 바꾸지 않는다.
- 새 `testdata/translation-quality/unseen-v050.tsv`: 32상황×4방향, 처음 보는 추가 절/반대 의미/인용/화자·대상/금액/체력/잡담. `artifacts/quality-v050/unseen2`에서 두 엔진 **256건 완료**, 아직 전수 수동 판정/집계 전. 최초 평가 후에는 회귀 표본이며 다시 미관측 표본이라고 부르면 안 됨. 차단·오역이 새로 발견돼 릴리스 검사가 끝난 상태가 아니다.

### Lite 원본 모델 조사

- HF `opus-mt-tc-big-ko-en`의 한국어 SentencePiece 결과가 공유 영어 vocab에서 `<unk>`로 바뀜. PyTorch와 CT2 모두 재현됐으므로 int8 변환만의 문제가 아님. 이 HF 변환본은 배포하지 않는다.
- 공식 카드가 가리키는 원본 Marian separate-vocab archive를 검증해 별도 source/target vocab·SPM으로 CT2 변환. SHA256 `9132ed606b6964656520931d96a4a6afc7ddc7d55ca5438e1d403db0c05492ef`, CC-BY-4.0, 변환 model.bin 약 219MB. archive 내 스크립트는 실행하지 않음.
- `scripts/Compare-LiteInputs.py`, `Export-LiteInputs.ps1`: 현 Argos1.1 vs 원본 Marian, 원문 vs 기존 전처리, 96문장×4조건=384건 추가 실행. `artifacts/lite-candidate-ko-en/paired-results.jsonl`.
- 원본 Marian은 번역기 사용/화장실/농담/화자·대상 등 일부 자유 문장을 개선했으나 렉/비꼼/장문 생략/브리핑 오역이 여전히 많음. 기존 전처리 대부분은 입력을 바꾸지 않았고 이름 정규화가 오히려 도움이 되는 사례도 있었으므로 "영어 치환이 원인"으로 일반화하지 말 것.
- **현재 앱의 Lite 모델/호스트는 아직 교체하지 않았음**. 후보를 앱에 적용하려면 양쪽 토크나이저 지원, 설치 무결성, 호스트 재빌드, 별도 모델 asset/라이선스, 전체 파이프라인과 JP 경유 영향 재검사가 필요.
- Python 호스트 소스에 source.spm/target.spm 분리 및 기존 shared tokenizer 공존 지원 추가, 모의 배선 테스트 `Lite/test_lite_host.py` **4개 통과**. 한쪽 SPM만 있으면 shared로 조용히 대체하지 않는다. **embedded EXE는 아직 이전 버전이고 실제 모델 지원 활성화/설치 패키지는 후속 작업**. 새 호스트를 실제 생성하기 전 파일 이름만 올리지 말 것.

### 검사 도구의 GPU 메모리 경합 발견·복구

- `unseen1`은 초반 반복 30초 취소로 중단, 일부 원시 결과는 보존. 검사 PID/명령줄을 확인해 그 PowerShell만 종료. 다른 GPU 앱/게임/Ollama 전체 프로세스는 종료하지 않음.
- 기존 runner가 keep_alive=-1로 요청한 1.8B와 7B를 모두 남겨 GPU 사용 메모리가 11,308MiB/12,282MiB에 도달. 비교용 7B만 API로 해제하자 6,179MiB로 줄고 단일 1.8B 요청이 약 350ms로 회복했다. 이는 비교 환경의 메모리 경합 증거이며 앱 자체에 동일 버그가 있다고 단정하지 않는다(앱 `MainWindow.xaml.cs`는 모델 변경 시 이전 모델 해제).
- `Run-TranslationQuality.ps1`에 기본 사전 예열, 별도 warmupMs/initialResidency 메타데이터, 명시적 `-IsolatedModels` 추가. 이 옵션은 비교용 두 Valtrans 모델 중 다른 모델만 해제하고 검사 후 대상 모델도 해제한다. 임의의 다른 Ollama 모델은 건드리지 않음. 냉시작 시험은 `-SkipWarmup`으로 별도 실행.
- `unseen2`는 위 옵션으로 순차 완료, 30초 연속 취소 재발 없음. phase4의 latency는 복수 적재 환경 값이므로 최종 단독 모델 성능으로 홍보하지 않는다.
- 후속 단위 테스트 **332/332 통과**. 새 표본에서 확인할 우선 이슈: `HP left`를 왼쪽으로 오인, `7초 뒤`의 뒤→Back 용어 주입, 10 미만 수치 검증 누락, 문장 내 `nt/mb`와 `retake/swearing` 의미, 인용 안의 생략된 인원, 장문 발화자·제안/보고 구별.

### 시간/잔여 체력 및 잡담 문맥 후속 수정

- 용어 생성 결과를 직접 확인: `7초 뒤`에서 `뒤→Back`, `12 HP left`에서 `Left→왼쪽`이 프롬프트에 들어갔음. `TranslationFactGuard.WithoutNonSpatialDirections`를 숫자/방향 검증과 용어 선택에서 공유. HP/health left를 잔여량으로 처리하고, 시간 절과 별도로 실제 `뒤에 적 있어`가 있으면 방향은 계속 보존한다.
- `artifacts/quality-v050/timing-fix` 실제 12건: 시간/HP 8건은 무관한 방향/차단 없이 번역. 예: `12 health left, heal in 7 seconds`. 수치 10 미만 검증 일반화는 아직 별도 과제.
- `GameChatFilterService`의 `피킹?`가 `피`만으로 매칭해 **피곤/피자**를 Tactical로 오인하던 오류 수정. `궁금/적당`도 단어 일부가 궁/적으로 분류되지 않도록 경계 보완. 실제 `피 12`, `피가 없어`, `피킹하지 마`, `궁 아직 안 썼어`, `적 없어`는 Tactical 유지.
- **346/346 단위 테스트 통과**. `artifacts/quality-v050/context-fix` 12건 실제 재검사: `오늘 피곤해서 두 판만 하고 잘래`의 EN은 `two matches and then go to sleep`로 개선. **JP는 여전히 취침 의도 누락**이므로 해결됐다고 표시하지 말 것.
- Windows OCR `smoke_basic_001` 실제 캡처→인식→번역→표시 검사 **3/3 PASS**(Bラッシュ, watch left, 왼쪽 조심해). 총 지연 254.9/83.6/59.9ms. 단순 규칙 예문이며 자유 문장 OCR/번역 전체 품질 검증을 대체하지 않음.
- 성공한 E2E 보고서의 `Failures`에 마지막 OCR 상태까지 추가하던 혼동을 수정해 `Diagnostics`로 분리. 재실행 **3/3 PASS**, Failures 빈 배열 확인, 총 지연 187.6/72.2/71.3ms. `artifacts/quality-v050/e2e-smoke-latest.json`에 해당 실행 보고서를 복사·보존했다.

### 수치·단위 보존 후속 검사

- 사용자 설치 완료 후 SSH로 `sudo -n -ll`을 다시 읽어 `/DATA/.valtrans-admin/valtrans-deploy ""`에만 무암호 권한이 있음을 확인. inbox는 비어 있고 서비스 재시작/배포는 하지 않았다. 추가 설치 명령을 사용자에게 요청할 필요 없음.
- `TranslationQuantityGuard`: 10 미만 수치도 명시적인 시간/체력/피해/크레딧/경기/라운드 단위와 함께 비교. 7초→7분, 체력12·시간7→체력7·시간12, 2경기→2라운드 등의 변경을 감지. 숫자뿐 아니라 제한된 한영일 수사와 공백 없는 일본어 단위를 지원한다. 임의 자유 문장의 모든 사실을 검증하는 의미 모델은 아님.
- 1분→60초처럼 같은 수량은 기존 숫자/인원 검사에서 오인하지 않도록 **양쪽에서 일치한 측정값의 숫자 토큰만** 비교용으로 가린다. 방향 검사는 원래 문장으로 진행해 `7초 뒤`의 숫자를 지운 뒤 `뒤`를 공간 방향으로 오인하지 않는다. 닉네임/원문 자체는 변경하지 않음.
- `판`의 경기/라운드 모호성, `두 번 더 라운드`, `한 게임`, `이번 라운드`, `한번 해봐`, 존칭 `두 분`을 대조군으로 추가. generic 판/번/회/回는 생략된 목표 단위를 읽는 보조 정보로만 사용하고, 그 자체를 확정 단위로 강제하지 않는다.
- **384/384 단위 테스트 통과**. 이전 실제 출력 1,108건(phase4/unseen2/quantity-check에서 비어 있지 않은 출력만)에 새 측정값 검사를 오프라인 적용했을 때 추가 불일치 0건. 이는 **신규 모델 호출이나 1,108건 의미 정답 판정이 아니라 정상 출력 과잉 차단 회귀 검사**이다.
- `unseen2` 256건 수동 판정 완료, `grades-hybrid18.tsv`, `grades-hybrid7.tsv`와 집계 저장: 1.8B **A82/W13/E23/X10**, 7B **A90/W19/E11/X8**(각128건). 당시 P50/P95는 229/322ms, 690/1116ms. 최신 수정 전 성적이며 지시/보고·화자·리테이크·은어 오류가 남아 있다. 검사 PASS를 의미 정답으로 세지 않는다.
- 수치 검사 중간 빌드로 같은 256건을 `quantity-check`에서 재실행. HP/시간 오탐은 해소됐으나 game/round/판/번 표현에서 새 오탐이 발견돼 최종 소스에서 다시 수정했다. **중간 결과를 릴리스 성적으로 사용하지 않는다.**
- `testdata/translation-quality/quantity-regression-v050.tsv`: 209/215/217/218의 4상황×4방향을 모은 명시적 회귀 표본. `quantity-final`에서 최신 빌드 두 엔진 **32건 완료, 차단 0건**. DLL SHA256 `544A40C0E1E5738DAA541F10861AB2C81F83878C420F8AA104611A15E75540ED`. 예열은 1.8B 5,345ms, 7B 7,779ms로 번역 시간과 별도 기록. **7B 218-send-jp는 여전히 두 경기/두 라운드 대조를 잘못 번역**하고 1.8B의 일부 장문은 의도 생략/어색함이 남아 있으므로 32건 무오역이라고 표시하지 않는다.
- `Inspect-TranslationRejections.ps1`의 reflection 호출을 `ExtractFacts`의 추가 숫자 비교 인자에 맞게 수정했다.

### 남은 릴리스 작업

### 최신 호스트·Lite 개선 및 설치 검증 (앞의 중간 상태보다 우선)

- 전용 sudo 권한을 다시 읽기 전용 확인했다. `/DATA/.valtrans-admin/valtrans-deploy ""`만 NOPASSWD이며 inbox는 비어 있다. 설치를 다시 요청할 필요 없고 운영 서비스는 아직 재시작하지 않았다.
- Phantom/retake/nt/mb/욕설의 관련 문맥 용어를 보완했다. retake는 시험·사진 문맥, mb는 메모리 단위, swearing은 맹세 문맥을 대조군으로 구분한다. 최초 `retake the site` 용어가 LOCATION_CHANGED 오탐을 만들어 최종 용어는 `retake/리테이크/リテイク`로 되돌렸다. `context-terms` 256건은 최초 용어 기준이므로 최종 재검사가 필요하다.
- 인용 안 인원 정정은 사실 비교에서만 읽고 문장 전체를 짧은 콜아웃으로 대체하지 않는다. 일본어 `せず`와 한국어 `미안/잘못`의 부정 오인, 영어 부정 축약형을 보완했다. 원문에 없는 `{{if...` 등 템플릿 코드 출력은 차단한다. 최신 단위 테스트 **420/420 통과**.
- `Build-LiteHost.ps1` + 고정 requirements + 빌드 설명 추가. 분리 source/target SentencePiece를 실제 실행파일로 빌드하고 공유/분리 모델 실제 번역을 확인한 뒤 Assets에 반영했다. 호스트 파일명 `ValtransLiteHost-5.exe`, SHA256 `9C1DD3B08B02EBAEEE5B1590D53C3400BDAC90FEF11FDC08F6FDB0174458C5A0`. 호스트 Python 테스트 5개 통과. 앞의 "embedded EXE 미반영"은 과거 상태다.
- 모델 캐시 2→4로 KO/JP 송수신 경유 시 반복 적재를 줄였다. 번역/idle 해제는 같은 잠금 안에서 처리한다. 이전 캐시2와 캐시4의 실제 320건 출력·오류는 동일했다. `lite-argos`→`lite-argos-cache4`: 잡담 104건 P50 251→58ms/P95 332→114ms, 전체 320건 43,585→13,995ms. 새 KOEN 후보 `lite-marian`→`lite-marian-cache4-measured`: 잡담 P50 293→92ms/P95 505→187ms, 전체 60,430→24,160ms. 알려진 회귀 표본이며 최종 앱 전체 성능은 아니다.
- PyInstaller 부모만 측정한 초기 약9MB 기록은 잘못된 메모리 수치다. 부모+직계 자식별 최대 working set 합계로 수정: 기존 모델 645,488,640bytes, 후보 739,590,144bytes. 동시점 RSS가 아니라 각 프로세스 최대치 합계이며 측정 범위를 기록했다. 최신 runner는 실제 내장 호스트를 먼저 추출·해시하고 Lite 4쌍 예열 시간을 별도로 기록한다. 위 320건 측정은 이 사전 예열 변경 전 방식이다.
- 원본 Marian INT8 KOEN을 `releases/valtrans-lite-ko-en-opus-20220728-int8.zip`으로 준비했다. 200,961,112bytes, SHA256 `ce9b357a409bf0a1d71c5bd3f7ebdf1ae4f67c2eac22c4fda630762cd7ce2a73`. 라이선스/출처 포함, 원본 archive 검증, 고정 파일목록·ZIP timestamp 사용. **아직 GitHub 공개하지 않았다.** 앱에 고정한 v0.5.0-beta asset URL은 실제 공개·다운로드 해시 검증 전 배포 준비 완료로 표시하면 안 된다.
- 새 설치 명세는 source/target SPM·config·양쪽 vocab까지 크기와 SHA를 검증한다. 기존 Argos KOEN은 설치된 4/4 Ready를 유지하며 개선 모델 업데이트 가능을 안내한다. 준비 동작에서 staging 검증 후 새 모델로 교체한다.
- `artifacts/lite-pipeline-v050/upgrade-report.json`: 기존 모델 복사본의 실제 `InstallAndPrepareAsync` 업그레이드 성공. 전후 4/4 Ready, model SHA `30170587...`→`641B3F89...`, 새 모델 실제 번역 `I'm tired today, so I'm gonna play two games and sleep.` 확인. 격리 폴더에서 캐시 archive를 사용했으며 사용자 AppData 모델은 바꾸지 않았다. 공개 인터넷 다운로드 성공 시험은 별도로 남아 있다.
- 새 모델은 자유 문장 일부를 개선하지만 렉/비꼼/행위자·절 생략·게임 행동 오역과 JP 경유 오류가 여전히 많다. Lite 실패/차단이 60→45건이라고 의미 정확도가 그만큼 올랐다고 세지 않는다. 품질 우선 Hybrid의 임의 Lite 자유문장 대체 금지는 유지한다. 모델 전체 수동 의미 판정과 최신 AI 회귀 검사는 아직 남아 있다.

### 릴리스 잔여 항목

### 최신 문맥 재검사와 양보조건 수정

- `artifacts/quality-v050/latest-context`: 두 모델 128건씩 총256건 완료, 원문/출력 전수 대조 후 수동 grades·reviewed-results·summary 저장. 1.8B A82/W21/E19/X6, 7B A95/W21/E10/X2. P50/P95 각각218/329ms, 725/1126ms. 이미 수정에 사용한 어려운 회귀 표본이며 모델의 일반 정확도 추정치가 아니다. 차단 감소를 정답 증가로 취급하지 않는다.
- 최종 plain retake 용어로 214의 8방향 모두 차단 없이 의미 확인(1.8B EN의 retake them은 W, 다른7건 A). nt/mb/욕설 문맥 개선 확인. 여전히 7B가 `두 경기`를 `두 라운드`로 바꾸거나 `내가 못 들었다`를 추가하고, 1.8B가 제안→서술·가해자/피해자를 바꾸는 오류가 있다. Lite 모델과 별개인 AI 잔여 오류다.
- 위256건 확인 뒤 `TacticalState`에 **전체 입력에 한정한** 양보조건+피킹 금지 문법 추가. first-person/imperative/optional yet 보존. 기존 긍정 조건부 피킹과 구분하고 질문·인용·다른 행위자·추가 절은 매칭하지 않는다. 불확실성 문법에 아마/확실하지는/확실하지와 may 변형 추가, 위치는 기존 사전 검증을 통과해야 한다.
- 단위 테스트 **436/436 통과**. `concession-regression-v050.tsv` 4상황×4방향을 실제 TranslatorService에서 1.8B/7B/Lite 각16건 총48건 확인. 전부 규칙 경로이며 주체·금지·yet·위치·인원·불확실성 보존, 오류0. **모델 생성 품질 개선48건이 아니라 검증된 전체 문법으로 모델 호출을 피한 결과**이다. 원시 기록은 `artifacts/quality-v050/concession-final`.
- 후속 우선 검토: 잘못 추정한 적/아군 교정, 피해를 준 대상 교정, 잠시 대기의 One second, 마이크가 꺼진 조건문 주체, 장문 인용/수량 오탐. 검증기가 화자·피해 단위를 모두 의미적으로 검증한다고 주장하지 않는다.

### 계속할 릴리스 잔여 항목

### 실제 OCR fuzz 검사에서 발견한 추가 릴리스 이슈

- `Run-ArenaFuzz.ps1 -RunE2E -NoFocusArena -Timeout 90`: Arena 단위16건·시나리오3 seeds 검증은 통과했지만 **실제 Windows OCR 첫 seed20260910은 2건 통과·1건 실패, 전체 FAIL**. 스크립트가 첫 실패에서 중단해 나머지 두 seed의 실제 E2E는 실행하지 않았다. `ocr-fuzz-windows-20260910.json` 보존.
- 실제 trace(2026-09-12T14:33:41Z): 화면 `Bラッシュ`→OCR `B フッシュ`→Hy-MT2 `B 루프`, OCR199.1ms/번역6030.9ms/전체6230ms. cold 모델 적재가 섞인 단일 요청이며 정상 예열 지연으로 홍보하지 않는다. watch left와 한국어는104.4/64ms로 정상. **상황 전체 PASS나 단순 번역 프롬프트 문제로 표시하면 안 된다.**
- 동일 fuzz/seed Fast OCR 비교도 **1건 통과·2건 실패, 전체 FAIL**: `B]`, `Bッシ1` 오독, 영어만 정상, 한국어 trace 미일치. `ocr-fuzz-fast-20260910.json`. 정상 영어의 OCR5261.5ms/전체5296ms로 Fast라는 이름만으로 빠르다고 가정할 수 없다.
- 설치된 PaddleOCR 파이프라인 코드를 직접 확인: `lang=ch/japan`은 PP-OCRv5_server_rec, `lang=korean`은 별도 korean_PP-OCRv5_mobile_rec. 현재 worker는 ch로 고정되어 원문 다중 언어 선택과 일치하지 않는다. 한국어 누락 개선을 위해 모델 선택/혼합줄 경로를 검토해야 한다. 설치 스크립트는 paddleocr 버전 하한만 있고 paddlepaddle CPU3.0을 설치하므로 재현성·장치/모델 메타데이터도 보완 대상.
- 설치된 OCR 기본 YAML은 문서 방향·unwarping·글줄 방향을 켠다. `fast_ocr_host.py`에서 수평 게임 화면에 불필요한 세 옵션을 명시적으로 껐다. 구성 회귀 Python 테스트1건 통과, 앱 Release빌드 경고/오류0. 같은 조건의 후속 실측 `ocr-fuzz-fast-upright-20260910.json`도 **1건 통과·2건 실패**. 영어 OCR632ms/전체667.6ms로 줄었지만 일본어 `Bッシ` 오독과 한국어 누락은 남아 있다. 단일 동일 seed 관찰이지 전체 성능 보장은 아니다.
- E2E 보고서의 `no matching trace`는 오독 결과를 직접 포함하지 않아 지금은 별도 message-traces 로그를 확인해야 한다. 출력만 맞아도 기대 사례에 연결하는 테스트 로직과 실패를 VALIDATION_FAILURE로 뭉뚱그리는 분류도 향후 검토 필요. 오독을 정답으로 인정하도록 기대 목록/매칭을 느슨하게 바꾸지 않는다.

### 배포 전 계속할 작업

### 2026-09-13: 동일 입력 OCR 비교 및 Fast 본문 인식 보완

- 전 Goal 턴은 실제 오독/지연 재현 및 코드 수정으로 진행했다. 이번 턴도 검사·구현을 계속하며 Goal은 ACTIVE, 서버 권한 대기는 아니다.
- 테스트 모드에만 `VALTRANS_TEST_SAVE_OCR_FRAMES=1`을 설정하면 실제 엔진 입력 영역 PNG와 결과를 최대32건 저장한다. 기본 실행에서는 저장하지 않는다. 전체 데스크톱이 아니라 이미 선택된 테스트 OCR 영역만 보존. `ocr-inputs-fuzz-20260910`에 원본 fixture17건·capture/result JSON을 복사했다. 이 fixture에서 본문은 잘리지 않았음을 이미지로 확인했다.
- `Probe-FastOcrFrames.py`: 3장×2모델×3확대=18조건. 초기 stdout cp949 오류로16건에서 중단된 뒤 UTF-8로 수정하고 나머지2조건만 별도 실행했다. 확대는 단어를 분리하거나 더 틀리기도 했다. 한국어 전용 모델이 한국어를 복구하지만 일본어는 잘못 읽으므로 단순 모델 교체는 적절하지 않다.
- `Probe-FastOcrLines.py`: 줄만 자른 경우 여전히 일본어가 빠졌다. 흰 본문을 분리한 실험에서는 ch 인식기가 Bラッシュ를, KO 인식기가 왼쪽 조심해를 복구했다. 반전은 작은ュ를큰ユ로 바꾸는 회귀가 있어 적용하지 않았다. 이 실험은 후보 비교이며 색상만으로 잘라내는 동작을 그대로 배포하지 않았다.
- `Ocr/chat_ocr.py`: VALORANT 수평 줄 분리, 잘린 상단 이력 줄 제외, 밝은 장면은 일반 검출로 대체. 색상 경계는 후보일 뿐이고 버릴 부분을 별도로 인식해 **완전한 `[채널] 닉네임:`이고 confidence>=0.9**인 경우에만 본문 분리. 콜론 뒤 colored body/추가 fullwidth colon/채널 없는 이름은 분리 거부. 독립 prefix 검증 자체도 OCR이라 무오류 보장은 아니다.
- JP/EN/ZH용 서버 인식기와 KO용 인식기를 함께 활용하며, 한국어 글자가 있고 점수가 충분한 KO 후보만 채택한다. 인식 점수는 정확도 확률이나 모델 간 보정된 확률이 아니다. 숫자·부정 의미가 맞는지 이 점수만으로 보장하지 않는다. 언어 선택/game을 Fast 및 Hybrid 요청에 전달, CPU4threads로 줄 인식 실행. 새 모듈을 csproj publish와 패키지 필수 파일 검사에 추가했다.
- Fast 준비 시 두 인식 모델을 적재한다. 선택에서 KO를 끄면 요청 시 KO 결과는 사용하지 않지만 현재 시작 준비는 두 모델을 모두 로드한다. 향후 메모리/시작 시간 최적화 대상. 일반 검출 fallback은 여전히 ch 인식이므로 불확실한 배치의 KO 처리도 더 검사해야 한다.
- 같은 seed20260910 **실제 Fast E2E 3/3 PASS**(`ocr-fuzz-fast-rows-20260910.json`), raw source가 각각 Bラッシュ/watch left/왼쪽 조심해인 것까지 확인. 전체 지연888.9/433/1911.4ms. 이전1/3 대비 정확도 복구지만 KO 지연은 높고 모든 환경에서 빠르다고 주장하지 않는다.
- 기본값 **PaddleOCR-VL 실제 E2E도 같은 seed 3/3 PASS**(`ocr-fuzz-paddle-20260910.json`), 전체522.9/738.4/861.9ms. 초기 준비 약30초가 별도로 포함된 실행이다. 기본값을 Fast로 바꾸지 않았다.
- Hybrid E2E가 실제 앱과 달리 `FastOnly`로 VL/retry를 끄던 것을 발견, 테스트도 Production 옵션과 준비 경로를 사용하게 바꿨다. 과거 Hybrid 녹색 결과를 실제 VL 경로 증거로 사용하지 않는다. 실제 Production Hybrid 후속 검사 진행.
- Python OCR 단위8건 및 .NET436건 통과(마지막 fullwidth-colon Python 변경은8건에 포함, .NET코드 변경 이후436건 통과). 주요 Fast 라이브러리5종을 현재 확인된 버전으로 고정했다. 전체 전이 의존성/모델 SHA 고정까지 완료했다는 뜻은 아니다. 설치 명령 자체의 fresh-runtime 검증은 아직 남아 있다.

### 현재 배포 전 잔여 항목

### 2026-09-13: Hybrid 위치 오독과 후보 점수 부재 문제

- 직전 Goal 턴은 실제 줄바꿈 누락 수정·검증으로 progress였다. 이번 턴도 검사/코드 변경 진행이며 권한/외부 응답 대기 때문의 blocker는 없다.
- `ocr-multilingual-hybrid-v050.json`: 실제 Production Hybrid도 5/6 FAIL. C 오독 요청은 OCR4893ms+번역2645.5ms, 전체7538.5ms. Fast 단독 한계뿐 아니라 Hybrid 후보 선택 문제임을 확인했다.
- 정확한 입력 PNG를 `ocr-site-glyph-source-v050.png`에 보존, SHA256 `F4F12F38DDA0AC01D44505D0E1A7D8654BB8FF1E02A6417EB979E237C5DEE140`. 눈으로 선명한 C임을 확인했다. `scripts/Replay-FastOcr.py`의 생산 recognizer 재생 결과 CH 본문 후보는 빈 값, KO 후보는 `스파이크 ㄷ 롱에 떨어졌어`/0.932621. 원시 후보 기록 `ocr-site-glyph-candidates-v050.jsonl`.
- 채널/닉네임 제외한 본문에서 단독 자모+위치어가 보이면 Fast의 높은 점수만으로 바로 채택하지 않도록 했다. ㄷ을 C로 변환하는 규칙이 아니라 별도 재인식을 요구하는 힌트다. 닉네임의 ㄷ, ㄷㄷ/ㅋㅋ 등은 대조군 테스트로 제외. 이 변경만 적용한 `ocr-multilingual-hybrid-site-retry-v050.json`도 5/6 FAIL, 해당 요청 전체10019.5ms로 오히려 지연만 늘었다. 개선 완료 증거로 세지 않는다.
- `scripts/Replay-PaddleOcr.py`로 동일 PNG를 실제 VL worker에 전달: `[TEAM] PlayerC: 스파이크 C 롱에 떨어졌어`를 정확히 반환했다. `ocr-site-vl-candidate-v050.jsonl`, 단일 OCR2981ms, 모델 준비 시간은 별도. **PaddleOcrService는 confidence를 받지 않는데 QualityScore 기본0이 실제 0점으로 비교돼 올바른 후보를 버리는 코드 문제가 확인됐다.**
- `OcrReadResult.QualityScoreAvailable`를 추가하고 VL은 false로 명시. 어느 후보든 점수가 없으면 양쪽의 점수 가중치를 빼고 내용 기준으로 비교하며 동률은 재인식 후보를 채택한다. 빈 VL 결과가 정상 Fast를 덮지 않도록 했다. 이는 confidence를 임의의 높은 확률로 조작한 것이 아니며 내용 휴리스틱 자체의 정확도를 보장하지 않는다.
- E2E PASS 조건 강화: 기대 출력이 같다는 이유만으로 사례에 연결하지 않고, 번역 입력에 **원문 전체가 보존돼야** PASS. 부분일치는 실패 진단 연결에만 남는다. 누락된 부정/화자/불확실성의 우연한 출력 일치를 막는 테스트와, OCR 공백/전각 및 문장 끝 구두점 허용 대조군 추가. 보고서에 TranslationInput/CompleteSourceMatched 추가. 최신 .NET464/464, runner Release 경고/오류0.
- 최종 점수 부재 수정 후 `ocr-multilingual-hybrid-confidence-v050.json` **실제 Hybrid 6/6 PASS**, 여섯 건 모두 CompleteSourceMatched=true, OCR 원문/번역 입력/결과 전수 대조. 실패하던 `스파이크 C 롱에 떨어졌어`→`spike down C Long`은 OCR C 보존과 전체3062.1ms 확인. 기대 목록을 바꾸거나 ㄷ을 C로 강제 치환한 결과가 아니다.
- 같은 실행 전체 지연5691.9/7391.1/9154.7/5348.5/3062.1/1252.2ms. 원문 보존은 해결했지만 전반적 지연은 이전보다 나빠진 구간이 있으며 **속도 개선 완료로 표시하지 않는다**. 후보별·준비·GPU/CPU 사용을 별도로 재측정해야 한다. Fast 단독의 C 오독, VL 일반 긴 줄 조립 및 전체 품질 회귀도 남아 있다. 현재 실행 중인 테스트/worker handle은 없고 Goal은 ACTIVE다.

### 2026-09-13 추가 검증: 긴 채팅 줄바꿈 손실 복구

- 사용자 설치 완료 이후 전용 권한을 SSH에서 다시 읽기 전용 확인: `(root) NOPASSWD: /DATA/.valtrans-admin/valtrans-deploy ""`. root 소유 관리자 디렉터리를 일반 사용자 `ls`로 읽을 수 없는 것은 예상된 보호 동작이다. 실제 helper 실행/배포/서비스 재시작은 아직 하지 않았다. 추가 설치 요청은 필요 없다.
- 이전 턴의 `ocr-fuzz-hybrid-production-20260911.json`은 Production 경로 3/3 통과(전체 기록 4791.9/6611.4/679.1ms). 당시 선택된 OCR 후보 시간만 집계해 앞선 시도의 지연을 누락할 수 있었으므로 최종 성능으로 사용하지 않는다. `HybridOcrService`에서 모든 시도의 경과 시간을 집계하도록 보완했다. Fast의 다른 seed20260912도 3/3 통과(515.5/355.5/270.2ms).
- 긴 문장·부정·아군·불확실성 6건의 `ocr-multilingual-fast-v050.json`은 **2/6, 전체 FAIL**이었다. 원문 마지막 `마` 누락, `Three teammates are`와 `There may be four C long` 부분 탈락으로 주체/인원/부정이 소실됐다. 좁은 짧은 문장 회귀가 통과했다고 OCR 전체 품질이 해결됐다고 판단할 수 없다.
- 직접 원인 중 하나는 Fast Python이 글줄 좌표를 돌려줘도 C#에서 이를 버리고, 최신 줄 처리에서 줄바꿈의 맨 마지막 행만 선택한 것이다. Fast 응답의 좌표와 이미지 너비를 `OcrReadResult`에 유지하고 `OcrMessageParser`에서 인식된 채널 헤더와 인접 위치/들여쓰기 또는 오른쪽 끝에 닿는 줄바꿈 근거로 문장을 연결한다. 세 줄 이상 들여쓰기 연속도 유지한다. 새 발신자·큰 수직 간격·짧은 별개 행은 합치지 않는다. 이 기준은 배치 휴리스틱이며 모든 OCR 오독/헤더 누락을 해결하지 않는다.
- 같은 실제 시나리오 `ocr-multilingual-fast-geometry-v050.json`: **5/6, 전체 FAIL**. 통과 5건의 OCR 원문 전체와 번역을 대조했다. 전체 지연 470.3/651.1/638.1/2295.9/933.2ms. 단순히 문자열 기대값을 느슨하게 바꾼 결과가 아니다. 누락된 부정·아군·인원·불확실성 구절이 실제 번역 입력으로 돌아왔다.
- 남은 실패는 `스파이크 C 롱에 떨어졌어`를 `스파이크 ㄷ 롱에 떨어졌어`로 읽고 `Spike landed on the Long.`으로 번역한 사례. 위치 문자 C의 OCR 오독이므로 ㄷ을 무조건 C로 치환하는 규칙으로 숨기지 않았다. Fast 단독과 Hybrid 불확실 후보 재인식/선택을 추가 검사해야 한다.
- `TestRunReport.UnmatchedTraces`에 연결되지 않은 완료 trace를 최대32건 보존한다. 기대 사례의 PASS로 승격하지 않는다. 위 실행의 구버전 runner DTO는 새 필드를 누락했으므로 앱 원본 보고서를 `ocr-multilingual-fast-geometry-app-v050.json`에 별도로 보존했고 runner도 다시 빌드했다. 기존 기대 원문 부분일치/출력만으로 사례를 찾는 로직은 여전히 재검토 대상이다.
- 테스트 스냅샷은 동일 frame hash를 중복 저장하지 않도록 개선했다. 최대32건 제한과 명시적 테스트 전용 opt-in은 유지한다. 정상 사용자 실행에서는 저장하지 않는다.
- 좌표가 잘못된 일부 행 때문에 전체 텍스트가 빠지지 않도록, 불완전한 Fast 좌표 응답은 plain text 경로로 처리한다. JSON 타입/범위/양수 크기 검사 포함. **.NET448/448, Python OCR8/8 통과**, runner Release 빌드 경고/오류0, git whitespace 검사 통과. 최신 실제 E2E 이후 추가한 것은 좌표 입력 방어와 스냅샷 중복 방지이며, 주된 줄바꿈 수정은 실제5/6 검사에 포함됐다.
- Goal은 ACTIVE. 모델 의미 오류, Fast C/ㄷ 및 일반 검출 KO 문제, Hybrid 지연, 최종 전체 표본 재검증, 앱/README/사이트 v0.5 동기화·패키지·SHA·GitHub 모델 asset 공개와 홈서버 배포는 여전히 남아 있다.
- 후속 `ocr-hybrid-aggregate-timing-v050.json`: 최신 코드의 Production Hybrid **3/3 PASS**, 전체 요청 지연1244.7/1025.9/1217.1ms. 앞선 후보까지 포함하는 집계로 실행했으며 각 OCR 원문도 정확히 일치했다. 시작부터 완료까지 약58초에는 OCR 모델 준비/시나리오 대기 등이 포함돼 요청 지연과 구분한다. 3개 짧은 사례만의 측정으로 일반 Hybrid 성능이나 C/ㄷ 실패 해결을 주장하지 않는다. 현재 실행 중인 E2E는 없다.

1. 최신 변경을 모든 기존/추가 실제 표본에 재검증하고 E/W/X를 계속 추적. Lite 후보 적용 여부는 실제 앱 경로 비교 후 결정.
2. OCR Arena/성능/전체 통합검사, README/사이트/UI 품질 우선 안내, v0.5 버전 동기화·패키지·SHA 생성.
3. 준비된 inbox에 검증된 사이트 아카이브와 SHA를 업로드한 뒤 전용 helper로 배포, LAN 및 공개 URL 확인.
4. GitHub CLI는 `C:/Program Files/GitHub CLI/gh.exe`에 있으며 `deffimism/Valtrans`, 기본 `master`, push 권한을 읽기 전용으로 확인. 아직 GitHub 쓰기/릴리스 공개/소스 커밋은 하지 않음.

### 2026-09-13: 체력 은어·수신 문장 보존·불필요한 Lite 준비 제거

- 상세 최신 기록은 [translation-health-v050.md](translation-health-v050.md). Goal ACTIVE, 이번 턴도 실제 검사와 코드 개선으로 progress이며 배포 권한 대기 상태가 아니다.
- 원탭 상태/트레이드 지시의 완전한 문장 규칙, B main 같은 복합 위치 힌트, low와 풀피 용어 및 활용형 보완. 지도 이름은 인원으로 다루지 않는다. 모델이 알려주지 않은 정확한 HP를 만들어내지 않는다.
- 브리핑 분류가 KO/JP 체력 은어를 놓치던 문제와 수신 필터가 조건·부정·정정 절을 버리던 문제 수정. 채택한 브리핑의 전체 문장을 유지한다. 공격 수사의 ‘한 방’을 한 명으로 세던 실제 검사 오탐도 재현 후 수정했다.
- Hybrid에 실제로 사용하지 않는 Lite 설치·예열·주기 메모리 유지·호환성 검사를 요구하던 UI 불일치를 제거했다. 선택한 AI 준비 상태를 확인하며 Lite 단독 모드는 유지한다. 가이드/README/사이트 정책 안내를 일치시켰다.
- 최신 자동 테스트592/592 통과, DLL `4A5A25E5B5C200D5F6470E16C4C4A0DB152283CCD00D5A69FD93A231EB689785`. 587 빌드의 추가48건 전수 판정에는 여전히 실제 오역이 남으며 상세 문서 표에 숨기지 않고 기록했다.
- 현재592 빌드 전체320×2 회귀 `player-filter-regression` 완료. 변경13건/12건 전수 대조 및 동일 결과만 기존 판정 상속: 1.8B A281/W32/E6/X1, 7B A307/W10/E3/X0. 이전500 빌드 A272/W36/E8/X4 및 A304/W10/E5/X1 대비 개선됐지만 잔여 E와 추가 대조군 오류는 남아 있다. 상세 문서에 사례 ID 기록.
- `single-hit-count-fixed`는 실제 Briefing 필터를 포함한405 네 방향×두 모델 모두 A. 원시 정상 출력의 ‘한 방’을 한 명으로 잘못 세던 차단이 해소됐고 전체 원문도 유지됐다. 모든 모델 검사 세션 종료, 최신 DLL과 결과 SHA 대응 확인.
- 앱/사이트 버전은 아직0.4.0-beta이고 v0.5 릴리스·공개 Lite asset·SHA 패키지·서버 배포는 미완료다. 설치된 전용 배포 권한은 유효하며 사용자에게 bootstrap 등록을 다시 요청할 이유는 없다.
