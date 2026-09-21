import http from 'node:http';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
const root = fileURLToPath(new URL('../', import.meta.url));
const types = { '.html':'text/html; charset=utf-8', '.js':'text/javascript; charset=utf-8', '.css':'text/css; charset=utf-8', '.json':'application/json' };
// Test-only page-load barrier: CLI browsers wait for this image while asynchronous
// browser/circuit checks run with the real clock. No production host dependency.
const acceptance = new Map();
const server = http.createServer(async (request,response) => {
  try {
    const url = new URL(request.url, 'http://localhost');
    const pathname = decodeURIComponent(url.pathname);
    if (pathname === '/__acceptance/wait') {
      const id = url.searchParams.get('id');
      const timer = setTimeout(() => { acceptance.delete(id); response.writeHead(408).end(); }, 45000);
      acceptance.set(id, () => { clearTimeout(timer); response.writeHead(204).end(); });
      request.on('close', () => { clearTimeout(timer); acceptance.delete(id); });
      return;
    }
    if (pathname === '/__acceptance/done') {
      const id = url.searchParams.get('id'); acceptance.get(id)?.(); acceptance.delete(id);
      response.writeHead(204).end(); return;
    }
    const target = path.resolve(root, `.${pathname === '/' ? '/demo/index.html' : pathname}`);
    if (!target.startsWith(root)) { response.writeHead(403).end(); return; }
    const body = await readFile(target);
    response.writeHead(200, { 'Content-Type':types[path.extname(target)] ?? 'application/octet-stream', 'Cache-Control':'no-store' }).end(body);
  } catch { response.writeHead(404).end('Not found'); }
});
server.listen(Number(process.env.PORT ?? 4178), '127.0.0.1', () => console.log(`Job monitor: http://127.0.0.1:${server.address().port}`));
