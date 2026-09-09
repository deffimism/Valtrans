import { createHmac, randomUUID } from 'node:crypto';
import { spawn } from 'node:child_process';
import { mkdtemp, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const port = 13120;
const secret = `test-${randomUUID()}`;
const payload = JSON.stringify({ event: 'payment.completed', timestamp: new Date().toISOString(), data: { paymentId: `test_${Date.now()}`, amount: 1000, projectName: 'valtrans', source: 'test' } });
const signature = createHmac('sha256', secret).update(payload).digest('hex');
const dataDir = await mkdtemp(join(tmpdir(), 'valtrans-support-test-'));
const dataFile = join(dataDir, 'events.json');
const child = spawn(process.execPath, ['server.js'], { cwd: new URL('.', import.meta.url), env: { ...process.env, PORT: String(port), FAIRY_WEBHOOK_SECRET: secret, ADMIN_TOKEN: 'admin-test', VALTRANS_DATA_FILE: dataFile }, stdio: ['ignore', 'pipe', 'pipe'] });
try {
  await new Promise((resolve, reject) => { const timer = setTimeout(() => reject(new Error('server start timeout')), 5000); child.stdout.on('data', (chunk) => { if (chunk.toString().includes(`listening on ${port}`)) { clearTimeout(timer); resolve(); } }); child.on('error', reject); });
  const response = await fetch(`http://127.0.0.1:${port}/webhook`, { method: 'POST', headers: { 'content-type': 'application/json', 'x-fairy-signature': `sha256=${signature}` }, body: payload });
  const body = await response.json();
  if (response.status !== 200 || !body.test) throw new Error(`test webhook failed: ${response.status} ${JSON.stringify(body)}`);
  const root = await fetch(`http://127.0.0.1:${port}/`);
  const rootText = await root.text();
  if (!root.ok || !rootText.includes('Valtrans')) throw new Error('static site failed');
  for (const required of ['PaddleOCR-VL', 'NVIDIA GPU', '로컬 전용', 'id="setup"', 'v0.2.2-beta']) {
    if (!rootText.includes(required)) throw new Error(`site product information missing: ${required}`);
  }
  if (rootText.includes('외부 API는 선택 사항') || rootText.includes('v0.1.1-beta')) throw new Error('stale site information');
  const setup = rootText.match(/<section id="setup"[\s\S]*?<\/section>/)?.[0] || '';
  if (!setup.includes('첫 게임 전, 세 가지만') || !setup.includes('한 번에 설치 준비') || !setup.includes('class="setup-note"')) throw new Error('setup copy/layout missing');
  if (/한 번 전환|uv 준비|class="lead"/.test(setup)) throw new Error('technical release notes leaked into setup copy');
  const setupCss = await fetch(`http://127.0.0.1:${port}/setup.css`);
  if (!setupCss.ok || !(await setupCss.text()).includes('word-break: keep-all')) throw new Error('setup stylesheet missing');
  const config = await fetch(`http://127.0.0.1:${port}/config.js`);
  if (!config.ok || !(await config.text()).includes('fairySupportUrl')) throw new Error('runtime config failed');
  const terms = await fetch(`http://127.0.0.1:${port}/terms`);
  const termsText = await terms.text();
  if (!terms.ok || !termsText.includes('이용약관') || !termsText.includes('deffimism@gmail.com')) throw new Error('terms page failed');
  const privacy = await fetch(`http://127.0.0.1:${port}/privacy`);
  const privacyText = await privacy.text();
  if (!privacy.ok || !privacyText.includes('개인정보 처리방침') || !privacyText.includes('paymentId')) throw new Error('privacy page failed');
  const termsHead = await fetch(`http://127.0.0.1:${port}/terms`, { method: 'HEAD' });
  if (!termsHead.ok || (await termsHead.text()) !== '') throw new Error('static HEAD request failed');
  const donate = await fetch(`http://127.0.0.1:${port}/valtrans_donate.png`, { method: 'HEAD' });
  if (!donate.ok || donate.headers.get('content-type') !== 'image/png') throw new Error('donation image failed');
  const payment = JSON.stringify({ event: 'payment.completed', timestamp: new Date().toISOString(), data: { paymentId: 'payment_test_001', amount: 1200, projectName: 'valtrans', source: 'payple', fairyName: 'must-not-save', fairyMessage: 'must-not-save' } });
  const paymentSignature = createHmac('sha256', secret).update(payment).digest('hex');
  const paymentRequest = { method: 'POST', headers: { 'content-type': 'application/json', 'x-fairy-signature': paymentSignature, 'x-fairy-event': 'payment.completed', 'x-fairy-timestamp': JSON.parse(payment).timestamp }, body: payment };
  const saved = await (await fetch(`http://127.0.0.1:${port}/webhook`, paymentRequest)).json();
  const duplicate = await (await fetch(`http://127.0.0.1:${port}/webhook`, paymentRequest)).json();
  if (!saved.processed || duplicate.processed) throw new Error('payment idempotency failed');
  const stored = await readFile(dataFile, 'utf8');
  if (stored.includes('must-not-save')) throw new Error('private payload was stored');
  const denied = await fetch(`http://127.0.0.1:${port}/api/events`);
  if (denied.status !== 401) throw new Error('admin endpoint is not protected');
  const health = await fetch(`http://127.0.0.1:${port}/health`);
  if (!health.ok) throw new Error('health failed');
  console.log('PASS: static site, legal pages, signed test webhook, payment idempotency, private-field omission, admin protection, and health endpoint');
} finally { child.kill(); }
await rm(dataDir, { recursive: true, force: true });
