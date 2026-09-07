# Valtrans Support Site

Valtrans 소개 페이지와 Fairy 후원 webhook 수신을 한 컨테이너에서 제공합니다. 기존 Mahstering/RVBot처럼 공개 gateway 앞에는 Cloudflare Tunnel을 두고, 이 서비스 자체는 홈서버의 로컬 포트에만 바인딩합니다.

## Local setup

1. `.env.example`을 `server/.env`로 복사합니다.
2. `FAIRY_PROJECT_URL`을 실제 Valtrans Fairy 프로젝트 주소로 바꿉니다.
3. `FAIRY_WEBHOOK_SECRET`과 `ADMIN_TOKEN`을 서로 다른 긴 임의값으로 설정합니다.
4. `docker compose --env-file .env up -d --build`를 실행합니다.
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

정상 응답은 `PASS`입니다. 컨테이너 상태는 `/health`에서 확인합니다. Cloudflare Tunnel ingress는 기존 사이트와 같은 방식으로 홈서버의 `http://192.168.0.19:13020`을 가리키면 됩니다. Compose는 기본적으로 `192.168.0.19:13020`에 바인딩하며, 서버 IP가 다르면 `.env`의 `VALTRANS_BIND_ADDRESS`를 바꾸세요. 실제 Fairy 프로젝트 URL과 webhook secret은 저장소에 커밋하지 마세요.
