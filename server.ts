import express from 'express';
import fs from 'fs';
import path from 'path';
import zlib from 'zlib';
import { fileURLToPath } from 'url';
import { createServer as createViteServer } from 'vite';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const WORKSPACE_ROOT = path.resolve(__dirname);

interface ProjectFileEntry {
  relativePath: string;
  sizeBytes: number;
  isBinary: boolean;
  lineCount: number;
}

const BINARY_EXTENSIONS = new Set(['.jpg', '.jpeg', '.png', '.ico', '.exe', '.dll', '.db']);

function collectProjectFiles(): ProjectFileEntry[] {
  const results: ProjectFileEntry[] = [];
  const roots = ['TuSkEerAbDoILPoLiCe.sln', 'README.md', '.github', 'src-wpf'];

  function walk(absPath: string) {
    if (!fs.existsSync(absPath)) return;
    const stat = fs.statSync(absPath);
    if (stat.isDirectory()) {
      const entries = fs.readdirSync(absPath).sort();
      for (const entry of entries) {
        if (entry === 'bin' || entry === 'obj') continue;
        walk(path.join(absPath, entry));
      }
      return;
    }

    const rel = path.relative(WORKSPACE_ROOT, absPath).replace(/\\/g, '/');
    const ext = path.extname(rel).toLowerCase();
    const isBinary = BINARY_EXTENSIONS.has(ext);
    let lineCount = 0;
    if (!isBinary) {
      const text = fs.readFileSync(absPath, 'utf8');
      lineCount = text.split(/\r?\n/).length;
    }

    results.push({
      relativePath: rel,
      sizeBytes: stat.size,
      isBinary,
      lineCount,
    });
  }

  for (const r of roots) {
    walk(path.join(WORKSPACE_ROOT, r));
  }

  return results;
}

function isAllowedRelativePath(relPath: string): boolean {
  const normalized = path.posix.normalize(relPath.replace(/\\/g, '/'));
  if (normalized.startsWith('..') || path.isAbsolute(normalized)) return false;
  return (
    normalized === 'TuSkEerAbDoILPoLiCe.sln' ||
    normalized === 'README.md' ||
    normalized.startsWith('.github/') ||
    normalized.startsWith('src-wpf/')
  );
}

// Standard CRC32 + PKZIP archive builder using Node built-in zlib
const CRC_TABLE = (() => {
  const table = new Uint32Array(256);
  for (let i = 0; i < 256; i++) {
    let c = i;
    for (let k = 0; k < 8; k++) {
      c = (c & 1) !== 0 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    }
    table[i] = c >>> 0;
  }
  return table;
})();

function crc32(buf: Buffer): number {
  let crc = 0xffffffff;
  for (let i = 0; i < buf.length; i++) {
    crc = CRC_TABLE[(crc ^ buf[i]) & 0xff] ^ (crc >>> 8);
  }
  return (crc ^ 0xffffffff) >>> 0;
}

function buildZipArchive(files: ProjectFileEntry[]): Buffer {
  const localParts: Buffer[] = [];
  const centralParts: Buffer[] = [];
  let offset = 0;

  for (const file of files) {
    const absPath = path.join(WORKSPACE_ROOT, file.relativePath);
    const raw = fs.readFileSync(absPath);
    const compressed = zlib.deflateRawSync(raw, { level: 6 });
    const nameBuf = Buffer.from(file.relativePath, 'utf8');
    const crc = crc32(raw);

    const localHeader = Buffer.alloc(30);
    localHeader.writeUInt32LE(0x04034b50, 0);
    localHeader.writeUInt16LE(20, 4); // version needed
    localHeader.writeUInt16LE(0x0800, 6); // UTF-8 flag
    localHeader.writeUInt16LE(8, 8); // deflate
    localHeader.writeUInt16LE(0, 10); // time
    localHeader.writeUInt16LE(0x5421, 12); // date
    localHeader.writeUInt32LE(crc, 14);
    localHeader.writeUInt32LE(compressed.length, 18);
    localHeader.writeUInt32LE(raw.length, 22);
    localHeader.writeUInt16LE(nameBuf.length, 26);
    localHeader.writeUInt16LE(0, 28);

    localParts.push(localHeader, nameBuf, compressed);

    const centralHeader = Buffer.alloc(46);
    centralHeader.writeUInt32LE(0x02014b50, 0);
    centralHeader.writeUInt16LE(20, 4);
    centralHeader.writeUInt16LE(20, 6);
    centralHeader.writeUInt16LE(0x0800, 8);
    centralHeader.writeUInt16LE(8, 10);
    centralHeader.writeUInt16LE(0, 12);
    centralHeader.writeUInt16LE(0x5421, 14);
    centralHeader.writeUInt32LE(crc, 16);
    centralHeader.writeUInt32LE(compressed.length, 20);
    centralHeader.writeUInt32LE(raw.length, 24);
    centralHeader.writeUInt16LE(nameBuf.length, 28);
    centralHeader.writeUInt16LE(0, 30);
    centralHeader.writeUInt16LE(0, 32);
    centralHeader.writeUInt16LE(0, 34);
    centralHeader.writeUInt16LE(0, 36);
    centralHeader.writeUInt32LE(0, 38);
    centralHeader.writeUInt32LE(offset, 42);

    centralParts.push(centralHeader, nameBuf);
    offset += localHeader.length + nameBuf.length + compressed.length;
  }

  const centralDir = Buffer.concat(centralParts);
  const eocd = Buffer.alloc(22);
  eocd.writeUInt32LE(0x06054b50, 0);
  eocd.writeUInt16LE(0, 4);
  eocd.writeUInt16LE(0, 6);
  eocd.writeUInt16LE(files.length, 8);
  eocd.writeUInt16LE(files.length, 10);
  eocd.writeUInt32LE(centralDir.length, 12);
  eocd.writeUInt32LE(offset, 16);
  eocd.writeUInt16LE(0, 20);

  return Buffer.concat([...localParts, centralDir, eocd]);
}

async function startServer() {
  const app = express();
  app.use(express.json());

  app.get('/api/project/files', (_req, res) => {
    try {
      const files = collectProjectFiles();
      res.json({ files });
    } catch (err) {
      res.status(500).json({ error: (err as Error).message });
    }
  });

  app.get('/api/project/file', (req, res) => {
    const relPath = typeof req.query.path === 'string' ? req.query.path : '';
    if (!relPath || !isAllowedRelativePath(relPath)) {
      res.status(400).json({ error: 'Invalid or disallowed project file path.' });
      return;
    }

    const absPath = path.resolve(WORKSPACE_ROOT, relPath);
    if (!absPath.startsWith(WORKSPACE_ROOT) || !fs.existsSync(absPath)) {
      res.status(404).json({ error: 'File not found.' });
      return;
    }

    const ext = path.extname(relPath).toLowerCase();
    if (BINARY_EXTENSIONS.has(ext)) {
      res.json({ path: relPath, isBinary: true, content: '' });
      return;
    }

    const content = fs.readFileSync(absPath, 'utf8');
    res.json({ path: relPath, isBinary: false, content });
  });

  app.get('/api/project/download', (_req, res) => {
    try {
      const files = collectProjectFiles();
      const zipBuffer = buildZipArchive(files);
      res.setHeader('Content-Type', 'application/zip');
      res.setHeader('Content-Disposition', 'attachment; filename="TuSkEer-AbDo-iL-PoLiCe-wpf-source.zip"');
      res.setHeader('Content-Length', String(zipBuffer.length));
      res.send(zipBuffer);
    } catch (err) {
      res.status(500).json({ error: (err as Error).message });
    }
  });

  const distDir = path.join(WORKSPACE_ROOT, 'dist');
  const isProd = process.env.NODE_ENV === 'production' && fs.existsSync(distDir);

  if (isProd) {
    app.use(express.static(distDir));
    app.get('*', (_req, res) => {
      res.sendFile(path.join(distDir, 'index.html'));
    });
  } else {
    const vite = await createViteServer({
      server: { middlewareMode: true },
      appType: 'spa',
    });
    app.use(vite.middlewares);
  }

  const port = 3000;
  app.listen(port, '0.0.0.0', () => {
    console.log(`TuSkEer WPF Project Studio listening on http://localhost:${port}`);
  });
}

startServer();
