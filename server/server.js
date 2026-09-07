import { createServer } from 'node:http';
import { createHmac, timingSafeEqual } from 'node:crypto';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname, join, normalize, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = dirname(fileURLToPath(import.meta.url));
const siteRoot = resolve(root, '..', 'site');
const dataFile = process.env.VALTRANS_DATA_FILE || join(root, 'data', 'events.json');
const port = Number(process.env.PORT || 3080);
const secret = process.env.FAIRY_WEBHOOK_SECRET || '';
const projectName = process.env.FAIRY_PROJECT_NAME || 'valtrans';
const adminToken = process.env.ADMIN_TOKEN || '';
const maxBodyBytes = 256 * 1024;
let writeQueue = Promise.resolve();

const json = (res, status, body) => {
  const value = JSON.stringify(body);
  res.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'no-store' });
  res.end(value);
};
const readBody = async (req) => {
  const chunks = [];
  let size = 0;
  for await (const chunk of req) {
    size += chunk.length;
    if (size > maxBodyBytes) throw Object.assign(new Error('body_too_large'), { status: 413 });
    chunks.push(chunk);
  }
  return Buffer.concat(chunks);
};
const header = (req, name) => req.headers[name.toLowerCase()] || '';
const validSignature = (body, provided) => {
  if (!secret || !provided) return false;
  const candidate = String(provided).replace(/^sha256=/i, '').trim();
  if (!/^[a-f0-9]{64}$/i.test(candidate)) return false;
  const expected = createHmac('sha256', secret).update(body).digest('hex');
  return timingSafeEqual(Buffer.from(candidate.toLowerCase(), 'utf8'), Buffer.from(expected, 'utf8'));
};
async function events() {
  try { return JSON.parse(await readFile(dataFile, 'utf8')); }
  catch (error) { if (error.code === 'ENOENT') return []; throw error; }
}
async function writeEvents(value) {
    await mkdir(dirname(dataFile), { recursive: true });
    await writeFile(dataFile, JSON.stringify(value, null, 2) + '\n', 'utf8');
}
function recordPayment(record) {
  let processed = false;
  writeQueue = writeQueue.then(async () => {
    const current = await events();
    if (current.some((item) => item.paymentId === record.paymentId)) return;
    await writeEvents([...current, record]);
    processed = true;
  });
  return writeQueue.then(() => processed);
}
function staticPath(urlPath) {
  const requested = urlPath === '/' ? '/index.html' : urlPath === '/terms' ? '/terms.html' : urlPath === '/privacy' ? '/privacy.html' : urlPath;
  const file = resolve(siteRoot, '.' + normalize(requested));
  return file.startsWith(siteRoot) ? file : null;
}
async function serveStatic(req, res) {
  const file = staticPath(new URL(req.url, 'http://localhost').pathname);
  if (!file) return json(res, 400, { error: 'invalid_path' });
  try {
    const content = await readFile(file);
    const type = file.endsWith('.html') ? 'text/html; charset=utf-8' : file.endsWith('.css') ? 'text/css; charset=utf-8' : file.endsWith('.js') ? 'text/javascript; charset=utf-8' : file.endsWith('.png') ? 'image/png' : 'application/octet-stream';
    res.writeHead(200, { 'Content-Type': type, 'Cache-Control': file.endsWith('index.html') ? 'no-cache' : 'public, max-age=3600' });
    res.end(content);
  } catch (error) { json(res, error.code === 'ENOENT' ? 404 : 500, { error: 'not_found' }); }
}
const server = createServer(async (req, res) => {
  try {
    const url = new URL(req.url, 'http://localhost');
    if (req.method === 'GET' && url.pathname === '/health') return json(res, 200, { ok: true, service: 'valtrans-support' });
    if (req.method === 'GET' && url.pathname === '/config.js') {
      const fairyUrl = process.env.FAIRY_PROJECT_URL || 'https://fairy.hada.io/';
      const downloadUrl = process.env.VALTRANS_DOWNLOAD_URL || '#download';
      res.writeHead(200, { 'Content-Type': 'text/javascript; charset=utf-8', 'Cache-Control': 'no-store' });
      return res.end(`window.VALTRANS_CONFIG=${JSON.stringify({ fairyProjectUrl: fairyUrl, downloadUrl })};`);
    }
    if (req.method === 'GET' && url.pathname === '/api/events') {
      if (!adminToken || header(req, 'authorization') !== `Bearer ${adminToken}`) return json(res, 401, { error: 'unauthorized' });
      return json(res, 200, { events: await events() });
    }
    if (req.method === 'POST' && url.pathname === '/webhook') {
      const body = await readBody(req);
      if (!validSignature(body, header(req, 'x-fairy-signature'))) return json(res, 401, { error: 'invalid_signature' });
      const payload = JSON.parse(body.toString('utf8'));
      const data = payload?.data;
      const event = payload?.event;
      const timestamp = payload?.timestamp;
      const eventHeader = header(req, 'x-fairy-event');
      const timestampHeader = header(req, 'x-fairy-timestamp');
      if (!payload || typeof payload !== 'object' || !data || typeof data !== 'object') return json(res, 400, { error: 'invalid_payload' });
      if (event !== 'payment.completed' || (eventHeader && eventHeader !== event)) return json(res, 400, { error: 'unsupported_event' });
      if (typeof timestamp !== 'string' || timestamp.length > 40 || Number.isNaN(Date.parse(timestamp)) || (timestampHeader && timestampHeader !== timestamp)) return json(res, 400, { error: 'invalid_timestamp' });
      const source = data.source;
      if (source !== 'payple' && source !== 'test') return json(res, 400, { error: 'invalid_source' });
      if (source === 'test') return json(res, 200, { ok: true, test: true, processed: false });
      const paymentId = String(data.paymentId || '');
      if (!paymentId) return json(res, 400, { error: 'missing_payment_id' });
      const receivedProject = String(data.projectName || '').trim();
      if (receivedProject && receivedProject.toLowerCase() !== projectName.toLowerCase()) return json(res, 400, { error: 'wrong_project' });
      const record = { paymentId, amount: data.amount ?? null, completedAt: timestamp, project: receivedProject || projectName, source };
      return json(res, 200, { ok: true, processed: await recordPayment(record) });
    }
    if (req.method === 'GET') return serveStatic(req, res);
    return json(res, 405, { error: 'method_not_allowed' });
  } catch (error) { json(res, error.status || 400, { error: error.message === 'body_too_large' ? error.message : 'invalid_request' }); }
});
server.listen(port, '0.0.0.0', () => console.log(`Valtrans support server listening on ${port}`));
