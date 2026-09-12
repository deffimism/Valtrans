# Valtrans Support Site

Valtrans 소개 페이지와 Fairy 후원 webhook 수신을 한 컨테이너에서 제공합니다. 기존 Mahstering/RVBot처럼 공개 gateway 앞에는 Cloudflare Tunnel을 두고, 이 서비스 자체는 홈서버의 로컬 포트에만 바인딩합니다.

## 등록된 전용 배포 권한으로 동기화

현재 홈서버는 `deffimism@192.168.0.19`, 설치 경로는 `/media/HDD-Raid1/AppData/Valtrans`입니다. 전용 키가 등록된 PC에서는 검증된 패키지를 업로드합니다.

```powershell
pwsh -NoProfile -File scripts/Package-SupportSite.ps1 -Version 0.5.0-beta
pwsh -NoProfile -File scripts/Sync-SupportSite.ps1 -Version 0.5.0-beta
```

두 번째 명령은 SHA256을 로컬과 서버에서 대조하고 업로드만 합니다. SSH에서 `sudo -n /DATA/.valtrans-admin/valtrans-deploy`를 실행하면 무인자 도구가 Valtrans만 재배포합니다. 배포까지 맡긴 경우에는 Sync 스크립트의 `-Deploy` 옵션으로 같은 도구를 호출할 수 있습니다. `.env`·후원 데이터를 덮지 않으며, 실패 시 이전 파일/이미지로 복구를 시도합니다. 실패 출력을 확인하기 전 성공으로 간주하지 마세요. Dockerfile/Compose 변경은 이 도구로 설치할 수 없습니다.

## SMB 수동 동기화 대안 (서버 관리자용)

Windows에서 패키지를 만들고 SMB로 서버에 복사합니다. `server/.env`와 `server/data/`는 서버에만 두고 덮어쓰지 않습니다.

```powershell
cd C:\Users\User\Documents\Codex\Valtrans
pwsh -NoProfile -File scripts\Package-SupportSite.ps1 -Version 0.5.0-beta
```

패키지와 SHA256 파일을 함께 복사하고 원본·대상 해시를 대조합니다. 서버의 `.env`와 결제 데이터는 패키지에 포함하지 않습니다.

```powershell
$ErrorActionPreference = 'Stop'
$src = 'C:\Users\User\Documents\Codex\Valtrans\releases\valtrans-support-v0.5.0-beta.tar.gz'
$dst = '\\192.168.0.19\HDD-Raid1\AppData\Valtrans\valtrans-support-v0.5.0-beta.tar.gz'
Copy-Item -LiteralPath $src -Destination $dst -Force
Copy-Item -LiteralPath "$src.sha256" -Destination "$dst.sha256" -Force
$sourceHash = (Get-FileHash -LiteralPath $src -Algorithm SHA256).Hash
$destinationHash = (Get-FileHash -LiteralPath $dst -Algorithm SHA256).Hash
if ($sourceHash -ne $destinationHash) { throw 'SHA256 mismatch; do not deploy' }
Write-Host "SHA256 verified: $destinationHash"
```

그다음 SSH에서 실행합니다. 기존 디렉터리 소유권을 일괄 변경하지 않습니다.

```bash
cd ~/AppData/Valtrans
sha256sum -c valtrans-support-v0.5.0-beta.tar.gz.sha256 &&
sudo tar --no-same-owner --no-same-permissions -xzf valtrans-support-v0.5.0-beta.tar.gz &&
bash scripts/sync-support-site.sh
```

## Local setup

1. `.env.example`을 `server/.env`로 복사합니다.
2. `FAIRY_SUPPORT_URL`을 실제 Valtrans Fairy 공개 후원 주소로 바꿉니다. 기본값은 `https://fairy.hada.io/@valtrans`입니다.
3. `FAIRY_WEBHOOK_SECRET`과 `ADMIN_TOKEN`을 서로 다른 긴 임의값으로 설정합니다.
4. 홈서버에서는 Docker 클라이언트 설정 경로와 권한을 지정해 `valtrans-support` 서비스만 실행합니다.

```bash
sudo env DOCKER_CONFIG="$PWD/.docker-client" docker compose -f "$PWD/compose.yaml" --env-file "$PWD/.env" config
sudo env DOCKER_CONFIG="$PWD/.docker-client" docker compose -f "$PWD/compose.yaml" --env-file "$PWD/.env" up -d --build --force-recreate --no-deps valtrans-support
```

5. Fairy에 다음 webhook 주소를 등록합니다.

```text
https://valtrans.deffimism.com/webhook
```

Fairy webhook은 `POST /webhook`에서 받습니다. 요청 원문에 HMAC-SHA256을 적용해 `X-Fairy-Signature`를 검증하고, `payment.completed`와 `source: payple`만 처리합니다. `source: test`는 저장하지 않으며, 실제 결제는 `paymentId` 중복을 확인한 뒤 금액·시각·프로젝트·source만 저장합니다. 이름, 이메일, 메시지, 원본 payload는 저장하지 않습니다. `X-Fairy-Event`와 `X-Fairy-Timestamp`가 있으면 본문 값과 일치하는지도 확인합니다.

공개 정책 페이지는 다음 주소입니다. Fairy 등록 화면의 링크 입력란에는 도메인에 맞춰 그대로 사용하세요.

```text
https://valtrans.deffimism.com/terms
https://valtrans.deffimism.com/privacy
```

현재 문서는 개인 운영자 기준의 초안입니다. 사업자명, 사업자등록번호, 주소, 환불·후원 제공 범위가 실제 운영과 달라지면 공개 전 해당 내용을 추가하거나 수정해야 합니다.

## Checks

메인 사이트 주소는 `https://valtrans.deffimism.com`이며, 웹훅은 같은 호스트의 `POST /webhook` 경로에서 받습니다. Cloudflare Tunnel은 홈서버의 `http://192.168.0.19:13020`으로 연결하면 됩니다. 공유기에서 13020 포트를 직접 공개할 필요는 없습니다.

Node.js 20 이상이 있으면 Docker 없이 서명 테스트를 실행할 수 있습니다.

```sh
node server/test-webhook.mjs
```

정상 응답은 `PASS`입니다. 홈서버에서 컨테이너 상태와 로그는 다음처럼 확인합니다.

```bash
sudo env DOCKER_CONFIG="$PWD/.docker-client" docker compose --env-file .env ps
sudo env DOCKER_CONFIG="$PWD/.docker-client" docker compose --env-file .env logs --tail=100 valtrans-support
curl http://192.168.0.19:13020/health
```

컨테이너는 `192.168.0.19:13020`에 고정 바인딩하며, Cloudflare Tunnel ingress는 `http://192.168.0.19:13020`을 가리키면 됩니다. 서버 IP가 변경되면 `server/compose.yaml`의 바인딩 주소도 함께 바꾸세요. 실제 Fairy 프로젝트 URL과 webhook secret은 저장소에 커밋하지 마세요.
