// Local preview of dist/: node serve.mjs, then open http://localhost:8771/planes-playable.html
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';

createServer(async (req, res) => {
  try {
    const body = await readFile(new URL(`./dist${decodeURIComponent(new URL(req.url, 'http://x').pathname)}`, import.meta.url));
    res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' }).end(body);
  } catch {
    res.writeHead(404).end();
  }
}).listen(8771);
