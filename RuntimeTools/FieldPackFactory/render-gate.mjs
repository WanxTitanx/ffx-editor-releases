// FieldPack Render Gate — headless screenshot + validation pipeline v2.176.0.0
// Uses Playwright + Chromium to render each field and produce screenshots for SSIM comparison.
//
// Usage:
//   node render-gate.mjs [--fields map/maca/maca03,map/mcfr/mcfr01] [--out work/field_pack/validation]
//   node render-gate.mjs --serve-only  (just start the viewer HTTP server)
//   node render-gate.mjs --all         (render all fields in public/maps/)

import { chromium } from 'playwright';
import { createServer } from 'http';
import { readFileSync, existsSync, mkdirSync, writeFileSync, readdirSync, statSync } from 'fs';
import { join, dirname, extname } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const repo = join(__dirname, '..', '..');
const viewerRoot = join(repo, 'RuntimeTools', 'FFXMapViewerWeb');
const publicMaps = join(viewerRoot, 'public', 'maps', 'map');
const defaultOut = join(repo, 'work', 'field_pack', 'validation');

// Parse args
const args = process.argv.slice(2);
const serveOnly = args.includes('--serve-only');
const renderAll = args.includes('--all');
const fieldsArg = args.find(a => a.startsWith('--fields='));
const fields = fieldsArg ? fieldsArg.split('=')[1].split(',').map(f => f.trim()) : [];
const outRoot = args.find(a => a.startsWith('--out='))?.split('=')[1] || defaultOut;

// Camera presets per field (overridable)
const CAMERA_PRESETS = {
  default: [
    { label: 'overview', pos: [0, 50, 100], target: [0, 0, 0] },
    { label: 'top', pos: [0, 150, 0], target: [0, 0, 0] },
    { label: 'side', pos: [100, 30, 0], target: [0, 0, 0] },
  ]
};

// Simple HTTP server for the MapViewer
function startServer(port = 0) {
  const mimeTypes = {
    '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript',
    '.css': 'text/css', '.json': 'application/json', '.gltf': 'model/gltf+json',
    '.glb': 'model/gltf-binary', '.bin': 'application/octet-stream',
    '.png': 'image/png', '.jpg': 'image/jpeg', '.dds': 'application/octet-stream',
    '.phyre': 'application/octet-stream',
  };

  const server = createServer((req, res) => {
    let url = req.url.split('?')[0];
    if (url === '/') url = '/index.html';
    const filePath = join(viewerRoot, url);

    if (!existsSync(filePath)) {
      res.writeHead(404);
      res.end('Not found');
      return;
    }

    const ext = extname(filePath);
    const contentType = mimeTypes[ext] || 'application/octet-stream';
    const content = readFileSync(filePath);
    res.writeHead(200, { 'Content-Type': contentType, 'Cache-Control': 'no-store' });
    res.end(content);
  });

  return new Promise((resolve) => {
    server.listen(port, () => {
      const addr = server.address();
      const actualPort = addr.port;
      console.log(`MapViewer HTTP server: http://localhost:${actualPort}`);
      console.log(`  viewerRoot: ${viewerRoot}`);
      console.log(`  index.html exists: ${existsSync(join(viewerRoot, 'index.html'))}`);
      resolve({ server, port: actualPort });
    });
  });
}

async function discoverFields() {
  const areas = [];
  if (!existsSync(publicMaps)) return areas;

  for (const areaDir of readdirSync(publicMaps)) {
    const areaPath = join(publicMaps, areaDir);
    if (!statSync(areaPath).isDirectory()) continue;
    for (const fieldDir of readdirSync(areaPath)) {
      const fieldPath = join(areaPath, fieldDir);
      if (!statSync(fieldPath).isDirectory()) continue;
      const gltfFiles = readdirSync(fieldPath).filter(f => f.endsWith('.gltf') || f.endsWith('.glb'));
      if (gltfFiles.length > 0) {
        areas.push({ area: `map/${areaDir}/${fieldDir}`, gltf: gltfFiles[0] });
      }
    }
  }
  return areas;
}

async function renderField(browser, area, gltfFile, port, outDir) {
  const url = `http://localhost:${port}/?map=${area}&headless=1`;
  const fieldOut = join(outDir, area.replace(/[/:]/g, '_'));
  mkdirSync(fieldOut, { recursive: true });

  const page = await browser.newPage({
    viewport: { width: 1920, height: 1080 },
    deviceScaleFactor: 1,
  });

  const results = [];

  try {
    // Log page errors
    page.on('pageerror', err => console.log(`    PAGE_ERROR: ${err.message}`));
    page.on('console', msg => { if (msg.type() === 'error') console.log(`    CONSOLE_ERR: ${msg.text()}`); });
    page.on('response', resp => { if (resp.status() >= 400) console.log(`    HTTP_${resp.status()}: ${resp.url().substring(0,120)}`); });

    // Navigate (domcontentloaded — viewer loads assets async)
    await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 15000 });

    // Wait for viewer ready (Promise resolves after init completes)
    await page.waitForFunction(() => window.__viewerReadyResolved === true, { timeout: 30000 });
    const readyState = await page.evaluate(() => window.__viewerReady);

    if (!readyState?.ok) {
      console.log(`  SKIP ${area}: ${readyState?.error || 'not ok'}`);
      await page.close();
      return { area, status: 'skip', reason: readyState?.error };
    }

    // Wait a bit for textures to load
    await page.waitForTimeout(2000);

    // Take screenshots from preset cameras
    const presets = CAMERA_PRESETS[area] || CAMERA_PRESETS.default;
    for (const cam of presets) {
      // Set camera via URL hash or evaluate
      await page.evaluate(({ pos, target }) => {
        if (window.__renderGateSetCamera) {
          window.__renderGateSetCamera(pos, target);
        }
      }, cam);

      await page.waitForTimeout(500);

      const screenshotPath = join(fieldOut, `${cam.label}.png`);
      await page.screenshot({ path: screenshotPath, fullPage: false });
      results.push({ camera: cam.label, path: screenshotPath });
      console.log(`  [${cam.label}] ${screenshotPath}`);
    }

    // Also take a full-page info screenshot
    const infoPath = join(fieldOut, 'info.png');
    await page.screenshot({ path: infoPath, fullPage: true });
    results.push({ camera: 'info', path: infoPath });

    await page.close();
    return { area, status: 'ok', screenshots: results };

  } catch (err) {
    console.log(`  ERROR ${area}: ${err.message}`);
    try { await page.close(); } catch {}
    return { area, status: 'error', error: err.message };
  }
}

async function main() {
  const { server, port } = await startServer(0);

  if (serveOnly) {
    console.log('Server running. Press Ctrl+C to stop.');
    return;
  }

  const outDir = outRoot;
  mkdirSync(outDir, { recursive: true });

  // Discover or use specified fields
  let fieldList;
  if (fields.length > 0) {
    fieldList = fields.map(f => {
      return { area: f, gltf: null };
    });
  } else if (renderAll) {
    fieldList = await discoverFields();
  } else {
    fieldList = await discoverFields();
  }

  if (fieldList.length === 0) {
    console.log('No fields to render. Use --fields=map/maca/maca03 or --all');
    server.close();
    return;
  }

  console.log(`Rendering ${fieldList.length} fields...`);

  const browser = await chromium.launch({
    headless: true,
    args: ['--no-sandbox', '--use-gl=swiftshader', '--enable-unsafe-swiftshader'],
  });

  const allResults = [];
  for (const f of fieldList) {
    console.log(`\nField: ${f.area}`);
    const result = await renderField(browser, f.area, f.gltf, port, outDir);
    allResults.push(result);
  }

  await browser.close();
  server.close();

  // Write summary
  const summary = {
    generatedAt: new Date().toISOString(),
    total: allResults.length,
    ok: allResults.filter(r => r.status === 'ok').length,
    skip: allResults.filter(r => r.status === 'skip').length,
    error: allResults.filter(r => r.status === 'error').length,
    fields: allResults,
  };

  const summaryPath = join(outDir, 'render-summary.json');
  writeFileSync(summaryPath, JSON.stringify(summary, null, 2));
  console.log(`\nSummary: ${summary.ok} ok, ${summary.skip} skip, ${summary.error} error`);
  console.log(`Report: ${summaryPath}`);
}

main().catch(err => {
  console.error('Fatal:', err);
  process.exit(1);
});
