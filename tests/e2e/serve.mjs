// Static server for a local WebGL build, so the e2e tests run against the same files that ship.
//   node serve.mjs            env: BUILD_DIR (default ../../Builds/WebRelease), PORT (default 8080)
// Headers follow web/site/vercel.json: *.unityweb files get the type of the file underneath (.wasm -> application/wasm,
// .js -> application/javascript, anything else octet-stream). A gzip build (magic 1f 8b) also gets Content-Encoding: gzip;
// a Brotli build goes out as stored over this plain-http server (browsers decode br only on secure hosts) and the loader's
// decompression fallback unpacks it. The real host serves Content-Encoding: br.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(process.env.BUILD_DIR || path.join(here, '../../Builds/WebRelease'));
const port = Number(process.env.PORT || 8080);

if (!fs.existsSync(path.join(root, 'index.html'))) {
  console.error(`No WebGL build at ${root} (index.html missing). Build it first: tools/build.sh WebRelease`);
  process.exit(1);
}

const TYPES = {
  '.html': 'text/html; charset=utf-8', '.js': 'application/javascript', '.json': 'application/json', '.wasm': 'application/wasm',
  '.css': 'text/css', '.png': 'image/png', '.jpg': 'image/jpeg', '.svg': 'image/svg+xml', '.ico': 'image/x-icon',
  '.woff2': 'font/woff2', '.txt': 'text/plain',
};

function isGzip(file) {
  const fd = fs.openSync(file, 'r'), b = Buffer.alloc(2);
  try { return fs.readSync(fd, b, 0, 2, 0) === 2 && b[0] === 0x1f && b[1] === 0x8b; } finally { fs.closeSync(fd); }
}

function headersFor(file) {
  if (file.endsWith('.unityweb')) {
    const ext = path.extname(file.slice(0, -'.unityweb'.length));
    const type = ext === '.wasm' ? 'application/wasm' : ext === '.js' ? 'application/javascript' : 'application/octet-stream';
    return isGzip(file) ? { 'Content-Type': type, 'Content-Encoding': 'gzip' } : { 'Content-Type': type };
  }
  return { 'Content-Type': TYPES[path.extname(file)] || 'application/octet-stream' };
}

http.createServer((req, res) => {
  let rel;
  try { rel = decodeURIComponent(new URL(req.url, 'http://localhost').pathname); } catch { res.writeHead(400).end(); return; }
  if (rel.endsWith('/')) rel += 'index.html';
  const file = path.join(root, path.normalize(rel));
  if (!file.startsWith(root + path.sep)) { res.writeHead(403).end(); return; }
  fs.stat(file, (err, st) => {
    if (err || !st.isFile()) { res.writeHead(404).end('not found'); return; }
    res.writeHead(200, { ...headersFor(file), 'Content-Length': st.size, 'Cache-Control': 'no-cache' });
    if (req.method === 'HEAD') res.end(); else fs.createReadStream(file).pipe(res);
  });
}).listen(port, '127.0.0.1', () => console.log(`serving ${root} on http://127.0.0.1:${port}`));
