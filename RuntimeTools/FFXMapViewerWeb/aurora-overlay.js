// 🌅 AURORA CHAMBER — additive MapViewer overlay (Layer 3).
//
// This file is LOADED ALONGSIDE app.js (one extra <script type="module"> line in index.html) and NEVER modifies it.
// It hooks the public debug surface app.js already exposes — window.ffxMapViewerDebug = { THREE, scene, camera,
// renderer, orbitControls, walkControls, state } — to add two strictly-optional, non-breaking features:
//
//   1. ACTOR ANCHORS  — when the URL carries ?actors=<url>, it fetches that JSON (written by the Aurora Chamber) and
//      plots a gizmo per actor anchor in a NEW top-level THREE.Group (scene.add), colored by role. Coordinates are
//      battle-local: the battle→scene transform is IDA-proven IDENTITY in X/Z (engine copies chunk3 verbatim — see
//      docs/reverse/FFX_AURORA_BATTLE_TO_SCENE_TRANSFORM_IDA_PROVEN_2026-06-05.md). Y residual per area/model height
//      (actor+0x534) is RT2 pending — see FFX_AURORA_MASTER_RT2_CHECKLIST_2026-06-15.md A01/A10. The "flip-Z (refuted)"
//      toggle stays as a comparison/debug knob only — DO NOT use as default.
//   2. PICK MODE      — raycasts the pointer onto map mesh geometry first, then falls back to the y=0 plane;
//      reports world (x,y,z) for grabbing scene coordinates. Active only while toggled.
//
// The overlay is a true NO-OP for plain MapViewer use: its UI panel + picker only activate when the Aurora Chamber
// launched the viewer (it appends ?aurora=1) or an ?actors= overlay is present. If the debug hook never appears
// (older app.js) it also silently no-ops. Read-only: it writes no game asset.

import * as THREE from "three";
import { GLTFLoader } from "three/addons/loaders/GLTFLoader.js";

const params = new URLSearchParams(window.location.search);
const actorsUrl = params.get("actors");
const dragPort = params.get("drag"); // editor's drag-bridge port (OS-assigned; passed in the deep-link)
const dragToken = params.get("dragToken"); // per-process capability token; never persisted or sent off-loopback

const ROLE_COLOR = {
  monster_live: 0xff4d4d,
  party: 0x4d9bff,
  party_front: 0x4d9bff,
  aeon: 0x57e0ff,
  monster_staging_a: 0x9a6b6b,
  monster_staging_b: 0x9a6b6b,
  camera: 0xffd24d,
  default: 0xb0b0b0,
};

const overlayState = {
  dbg: null,
  group: null,
  rawAnchors: [],
  flipZ: false,
  pickMode: false,
  raycaster: new THREE.Raycaster(),
  ndc: new THREE.Vector2(),
  plane: new THREE.Plane(new THREE.Vector3(0, 1, 0), 0),
  hit: new THREE.Vector3(),
  pickMarker: null,
  // 🌅 drag-to-place
  placeMode: false,
  dragging: null,            // { sphere } currently being dragged
  dragPlane: new THREE.Plane(new THREE.Vector3(0, 1, 0), 0),
  dragHit: new THREE.Vector3(),
  anchorSpheres: [],         // raycast targets (the role spheres), tagged with userData {role,index,anchor,stem,monsterRoot}
  battleId: null,
  cameraData: null,          // 🎥 establishing-camera marker { hasEye, eyeX/Y/Z, hasRef, refX/Y/Z, angle, distance, label }
  exportQuality: null,
  degradedModelCount: 0,
  mapMeshCache: null,
  // 🧱 ground-height raycast cache (cell -> y | null) — F2: real ground under anchors/drag
  groundCache: new Map(),
  groundCacheMax: 4096,
  // 🗺️ PS2 field encounter zones (mapout.vpa) — F4
  zones: null,
  zonesNote: "",
  zonesGroup: null,
  showZones: false,
  // 🎬 per-monster animation playback (F3)
  selectedSphere: null,       // currently inspected anchor sphere (highlight + anim controls)
  animEntries: new Map(),     // sphere -> { anchor, mixer, clips, root }
  // 📦 asset loading stats (F5)
  bytesLoaded: 0,
  resourceNames: new Set(),
};

// 🌅 Monster models — the real CHR glTFs (work/phyre_chr_anim/models/mNNN) loaded at monster_live anchors.
const gltfLoader = new GLTFLoader();
const monsterModels = []; // { root, mixer, anchor }
let monsterScale = 1.0;   // global visual factor; per-anchor scale (anchor.scale) multiplies it
let animClock = null;
let animLoopRunning = false;

function ensureAnimLoop() {
  if (animLoopRunning) return;
  animLoopRunning = true;
  animClock = animClock || new THREE.Clock();
  const tick = () => {
    const d = animClock.getDelta();
    for (const m of monsterModels) m.mixer?.update(d);
    requestAnimationFrame(tick);
  };
  requestAnimationFrame(tick);
}

/** Effective uniform scale for an anchor: per-anchor catalog scale (ATEL scaleOwnSize) × the global dial. */
function effectiveMonsterScale(anchor) {
  const anchorScale = Number(anchor?.scale) > 0 ? Number(anchor.scale) : 1;
  return anchorScale * monsterScale;
}

/**
 * Load the monster model at an anchor, with an honest fallback cascade:
 *   1. Procedural placeholder box (THREE.BoxGeometry, role color) — ALWAYS works immediately.
 *   2. Best-effort real glTF HD model: fetch(URL) → arrayBuffer → GLTFLoader.parse(arrayBuffer, basePath).
 *      This bypasses the built-in FileLoader/Manager (which hangs in this sandbox — loadAsync never
 *      resolves/rejects and never dispatches a network request for unclear reasons). The parse() call
 *      is callback-based: we promisify it. On success the gltf scene replaces the box in-place.
 *   3. If fetch or parse fails, the box placeholder stays (non-blocking).
 * Applies per-anchor scale (anchor.scale, ATEL scaleOwnSize) × global dial, per-anchor flipX, and stores
 * the AnimationMixer + clips so the anim panel (F3) can play named cycles (empty for the placeholder).
 */
function loadMonster(anchor, x, y, z, group, marker) {
  const s = effectiveMonsterScale(anchor);
  const flipX = !!anchor.flipX;
  const isMonster = anchor.role === "monster_live";
  const boxColor = (ROLE_COLOR[anchor.role] ?? 0xb0b0b0);
  const size = isMonster ? 4.0 : 3.0;
  // ── Immediate procedural placeholder ──
  const root = new THREE.Mesh(
    new THREE.BoxGeometry(size, size * 1.5, size),
    new THREE.MeshBasicMaterial({ color: boxColor })
  );
  root.position.set(x, y, z);
  root.scale.setScalar(s);
  if (flipX) root.rotation.x = Math.PI;
  root.name = `monster_m${anchor.monsterId ?? -1}`;
  root.castShadow = false;
  group.add(root);
  if (marker) marker.userData.monsterRoot = root;
  const entry = { root, mixer: null, anchor, clips: [] };
  monsterModels.push(entry);
  overlayState.animEntries.set(marker, entry);
  refreshAnimPanel?.();
  markResourceLoaded(urlFormFromAnchor(anchor));
  // ── Best-effort real glTF via manual fetch + GLTFLoader.parse ──
  if (!anchor.model) return;
  const glbUrl = urlFormFromAnchor(anchor);
  if (!glbUrl) return;
  fetch(glbUrl)
    .then((resp) => {
      if (!resp.ok) throw new Error("fetch status " + resp.status);
      return resp.arrayBuffer();
    })
    .then((buf) => {
      const base = glbUrl.substring(0, glbUrl.lastIndexOf("/") + 1);
      return new Promise((resolve, reject) => {
        try { gltfLoader.parse(buf, base, resolve, reject); }
        catch (e) { reject(e); }
      });
    })
    .then((gltf) => {
      const real = gltf.scene;
      real.position.copy(root.position);
      real.scale.setScalar(s);
      if (flipX) real.rotation.x = Math.PI;
      real.name = root.name;
      group.add(real);
      group.remove(root);
      if (marker) marker.userData.monsterRoot = real;
      const e = overlayState.animEntries.get(marker);
      if (e) e.root = real;
      if (gltf.animations && gltf.animations.length) {
        const mixer = new THREE.AnimationMixer(real);
        if (e) { e.mixer = mixer; e.clips = gltf.animations; }
        const action = mixer.clipAction(gltf.animations[0]);
        action.time = 0; action.play(); mixer.update(0); action.stop();
      }
      refreshAnimPanel?.();
      markResourceLoaded(glbUrl);
    })
    .catch(() => { /* placeholder kept; the batch is simply absent */ });
}

function urlFormFromAnchor(anchor) {
  try { return new URL(anchor.model ?? "", window.location.href).href; } catch { return ""; }
}

/* ---------------- Asset loading stats (F5) ---------------- */

/** Count network bytes for same-origin resources (glTF/PNG/JSON) via resource timing. Honest approximation. */
function markResourceLoaded(url) {
  overlayState.resourceNames.add(url);
  refreshBytesLoaded();
}

function refreshBytesLoaded() {
  try {
    const entries = performance.getEntriesByType("resource");
    let bytes = 0;
    for (const e of entries) {
      if (e.transferSize > 0) bytes += e.transferSize;
      else if (e.encodedBodySize > 0) bytes += e.encodedBodySize;
    }
    overlayState.bytesLoaded = bytes;
    const el = document.getElementById("auroraBytesInfo");
    if (el) el.textContent = `📦 ${(bytes / (1024 * 1024)).toFixed(2)} MB carregados (${overlayState.resourceNames.size} assets)`;
  } catch { /* resource timing unavailable */ }
}
if (typeof performance !== "undefined" && performance.getEntriesByType) {
  setInterval(refreshBytesLoaded, 2000);
}

/** Cache-first fetch for static JSON (anchors are network-first so editor writes always show). */
async function fetchCached(url, opts = {}) {
  const networkFirst = opts.networkFirst || false;
  const req = new Request(url, { cache: "no-store" });
  let cache = null;
  try { cache = await caches.open("aurora-overlay-v1"); } catch { /* Cache API unavailable */ }
  if (networkFirst) {
    try {
      const res = await fetch(req);
      if (res.ok && cache) cache.put(req, res.clone());
      return res;
    } catch {
      if (cache) { const hit = await cache.match(req); if (hit) return hit; }
      throw new Error("network + cache failed");
    }
  }
  if (cache) {
    const hit = await cache.match(req);
    if (hit) return hit;
  }
  const res = await fetch(req);
  if (res.ok && cache) cache.put(req, res.clone());
  return res;
}

/* ---------------- Ground height (F2) ---------------- */

/** Raycast straight down onto the map mesh to find the ground Y at (x, z). Cached per 1-unit cell.
 * Returns null when there is no mesh or no hit (caller falls back to the anchor's own Y). */
function getGroundY(x, z) {
  const cx = Math.round(x), cz = Math.round(z);
  const key = `${cx},${cz}`;
  if (overlayState.groundCache.has(key)) return overlayState.groundCache.get(key);
  if (overlayState.groundCache.size >= overlayState.groundCacheMax) overlayState.groundCache.clear();

  const { camera, scene } = overlayState.dbg;
  const meshes = collectMapMeshes();
  let result = null;
  if (meshes.length > 0) {
    const ray = new THREE.Raycaster();
    ray.set(new THREE.Vector3(x, 10000, z), new THREE.Vector3(0, -1, 0));
    ray.far = 20000;
    const hits = ray.intersectObjects(meshes, true);
    if (hits.length > 0) result = hits[0].point.y;
  }
  overlayState.groundCache.set(key, result);
  return result;
}

/** Ground Y at (x, z) inside the ANCHOR GROUP's frame (group is NOT flipped — glTF is Y-up). */
function getGroundYInGroup(x, z) {
  const gy = getGroundY(x, z);
  if (gy === null) return null;
  // O grupo não inverte mais Y (glTF Y-up desde v2.182.10) — o Y local == Y do mundo.
  return gy;
}

function invalidateGroundCache() {
  overlayState.groundCache.clear();
}

/* ---------------- Encounter zones (F4) ---------------- */

/** Draw the PS2 field encounter zones (mapout.vpa) as flat line-loops on the group's y=0 debug plane. */
function drawZones() {
  const group = overlayState.group;
  if (!group || !overlayState.zones) return;
  if (overlayState.zonesGroup) {
    group.remove(overlayState.zonesGroup);
    overlayState.zonesGroup = null;
  }
  if (!overlayState.showZones) return;
  const zg = new THREE.Group();
  zg.name = "auroraZones";
  for (const z of overlayState.zones) {
    const color = z.tag === 1 ? 0xff5a5a : (z.tag === 2 ? 0x5aff8a : 0xffd24d);
    for (const poly of (z.polygons || [])) {
      const pts = [];
      const xs = poly.xs || [], zs = poly.zs || [];
      for (let i = 0; i < xs.length && i < zs.length; i++) pts.push(new THREE.Vector3(xs[i], 0, zs[i]));
      if (pts.length < 3) continue;
      const geo = new THREE.BufferGeometry().setFromPoints(pts.concat([pts[0]]));
      const line = new THREE.Line(geo, new THREE.LineBasicMaterial({ color, depthTest: false, transparent: true, opacity: 0.85 }));
      line.renderOrder = 995;
      zg.add(line);
      // centroid tag label
      let mx = 0, mz = 0;
      for (const p of pts) { mx += p.x; mz += p.z; }
      mx /= pts.length; mz /= pts.length;
    }
  }
  group.add(zg);
  overlayState.zonesGroup = zg;
}

function toggleZones() {
  overlayState.showZones = !overlayState.showZones;
  const btn = document.getElementById("auroraZonesBtn");
  if (btn) btn.textContent = `🗺️ Zonas PS2 (campo): ${overlayState.showZones ? "ON" : "OFF"}`;
  drawZones();
  setAnchorInfo(overlayState.showZones
    ? `🗺️ ${overlayState.zonesNote} — polígonos do mapout.vpa PS2 no plano y=0 (coords de campo, NÃO alinhados à cena HD).`
    : "Zonas PS2 ocultas.");
}

function collectMapMeshes() {
  const asset = overlayState.dbg?.state?.asset;
  if (!asset) return [];
  const meshes = [];
  asset.traverse((o) => {
    if (o.isMesh && o.geometry) meshes.push(o);
  });
  if (meshes.length > 0) overlayState.mapMeshCache = meshes;
  return meshes;
}

function invalidateMapMeshCache() {
  overlayState.mapMeshCache = null;
}

function refreshDegradedInfo() {
  const base = document.getElementById("auroraAnchorInfo")?.textContent?.split(" · ⚠")[0] || "";
  const deg = overlayState.degradedModelCount;
  const extra = deg > 0 ? ` · ⚠ ${deg} modelo(s) CHR degradado(s) — esfera laranja` : "";
  if (document.getElementById("auroraAnchorInfo")) setAnchorInfo(base + extra);
}

function showExportQuality(data) {
  const el = document.getElementById("auroraQualityInfo");
  if (!el) return;
  const q = data?.exportQuality;
  if (!q || !q.submeshCount) { el.textContent = ""; return; }
  el.textContent = `🎨 ${q.label || `${q.boundSubmeshCount}/${q.submeshCount} DDS`}${q.breakdown ? ` · ${q.breakdown}` : ""}`;
}

function waitForDebug(attempt = 0) {
  const dbg = window.ffxMapViewerDebug;
  if (dbg && dbg.scene && dbg.camera && dbg.renderer) {
    window.__viewerReady?.then((result) => { if (result.ok) boot(dbg); });
    return;
  }
  if (attempt > 200) return; // ~20s; viewer never came up — silent no-op.
  setTimeout(() => waitForDebug(attempt + 1), 100);
}

function boot(dbg) {
  overlayState.dbg = dbg;
  // 🐛 AURORA INSTRUMENT (2026-08-16): trace why monsters/camera don't render — console.warn is captured by the
  // WebView.Console bridge -> DebugLog (WebView.Console category).
  console.warn("[aurora-dbg] boot() chamado; dbg?", !!dbg, "scene?", !!(dbg && dbg.scene), "camera?", !!(dbg && dbg.camera),
    "actorsUrl?", actorsUrl, "aurora=1?", params.get("aurora"));
  // Only activate when the Aurora Chamber launched this viewer (?aurora=1) or an ?actors overlay is present —
  // otherwise stay a true no-op (no panel, no listener) for plain MapViewer use.
  if (!actorsUrl && params.get("aurora") !== "1") { console.warn("[aurora-dbg] NO-OP (sem ?aurora=1 nem ?actors=)"); return; }
  console.warn("[aurora-dbg] ATIVOU (painel + anchors=", actorsUrl, ")");
  buildPanel();
  if (actorsUrl) loadAnchors(actorsUrl);
  else console.warn("[aurora-dbg] SEM ?actors= — não vai carregar âncoras/monstros");
  installPicker();
}

/* ---------------- UI panel ---------------- */

function buildPanel() {
  const panel = document.createElement("div");
  panel.id = "auroraOverlayPanel";
  Object.assign(panel.style, {
    // 🌅 BARRA LATERAL (plano UI/UX): não flutua na frente do 3D — encosta na borda, colapsável.
    position: "fixed", left: "10px", top: "10px", bottom: "10px", zIndex: 9999,
    width: "296px", maxHeight: "calc(100vh - 20px)", overflowY: "auto",
    font: "12px/1.4 system-ui, sans-serif", color: "#eee",
    background: "rgba(16,12,28,0.94)", border: "1px solid #5A3A7A",
    borderRadius: "10px", padding: "10px 12px",
    boxShadow: "0 6px 20px rgba(0,0,0,0.45)",
    display: "flex", flexDirection: "column",
  });
  panel.innerHTML = `
    <div id="auroraDragHandle" style="display:flex;align-items:center;justify-content:space-between;font-weight:700;color:#caa6ff;margin-bottom:8px;cursor:pointer;user-select:none" title="Clique para recolher/expandir">
      <span>⠿ 🌅 Aurora overlay</span>
      <button id="auroraCollapseBtn" type="button" style="background:#2a1b45;color:#eee;border:1px solid #5A3A7A;border-radius:6px;padding:2px 8px;cursor:pointer">❮ Ocultar</button>
    </div>
    <div id="auroraOverlayBody" style="display:flex;flex-direction:column;gap:6px">
    <div id="auroraBanner" style="color:#c9b8e6;margin-bottom:4px">
      Selecione um monstro e ative Place mode para ajustar sua posição. A prévia não executa a IA completa da batalha.
    </div>
    <div style="display:flex;gap:8px;flex-wrap:wrap">
      <button id="auroraPickBtn" type="button"
              title="Pick mode: clica no chão da cena e mostra a coordenada X/Z daquele ponto (só leitura — não move nada). Útil pra conferir posições.">Pick mode: OFF</button>
      <button id="auroraPlaceBtn" type="button"
              title="Place mode: arrasta os monstros (esferas vermelhas) pra reposicioná-los no mapa. Ao soltar, a coordenada nova é mandada pro editor.">🖱️ Place mode: OFF</button>
      <button id="auroraSaveBtn" type="button"
              title="Grava no .bin do battle as posições que você arrastou (byte-safe: só os bytes de posição mudam, cria backup .aurora.bak). Mesmo botão do editor.">💾 Salvar posições</button>
      <button id="auroraFlipBtn" type="button"
              title="DEBUG/comparação: espelha o eixo Z das âncoras. Era uma hipótese de transform (flip-Z) REFUTADA no IDA — o transform real é IDENTIDADE. Deixe OFF.">flip-Z (refut.): OFF</button>
      <button id="auroraZonesBtn" type="button"
              title="Desenha os polígonos de zona de encontro do mapout.vpa PS2 (campo) no plano y=0. Coords de campo — NÃO alinhadas à cena HD btlmap (inspenção honesta).">🗺️ Zonas PS2 (campo): OFF</button>
    </div>
    <div style="display:flex;gap:8px;flex-wrap:wrap;margin-top:6px">
      <button id="auroraFocusBtn" type="button"
              title="Foca a câmera na âncora selecionada (ou na primeira, se nenhuma).">🎯 Focar âncora</button>
      <button id="auroraTopBtn" type="button" title="Snap: vista de topo">Topo</button>
      <button id="auroraFrontBtn" type="button" title="Snap: vista frontal">Frente</button>
      <button id="auroraFitBtn" type="button" title="Fit: enquadra a cena">Fit</button>
      <button id="auroraResetBtn" type="button" title="Reset: câmera na origem">Reset</button>
    </div>
    <div id="auroraInspector" style="margin-top:8px;border:1px solid #4a3a6a;border-radius:6px;padding:6px;display:none">
      <div style="font-weight:700;color:#caa6ff;margin-bottom:4px" id="auroraInspectorTitle">Âncora</div>
      <div style="display:flex;gap:6px;align-items:center;flex-wrap:wrap">
        <span>X</span><input id="auroraInsX" type="number" step="0.1" style="width:80px" />
        <span>Y</span><input id="auroraInsY" type="number" step="0.1" style="width:80px" />
        <span>Z</span><input id="auroraInsZ" type="number" step="0.1" style="width:80px" />
        <button id="auroraInsApply" type="button" title="Envia a posição digitada pro editor (mesmo fluxo do drag).">Aplicar</button>
      </div>
      <div id="auroraInsScale" style="margin-top:4px;color:#c9b8e6;font-size:11px"></div>
      <div id="auroraAnimPanel" style="margin-top:6px;display:none">
        <div style="font-weight:700;color:#caa6ff;margin-bottom:3px">🎬 Animação (.ath)</div>
        <select id="auroraAnimSelect" style="width:100%;background:#1a1230;color:#eee;border:1px solid #5A3A7A;border-radius:6px;padding:3px"></select>
        <div style="display:flex;gap:6px;margin-top:4px">
          <button id="auroraAnimPlay" type="button">▶ Play</button>
          <button id="auroraAnimStop" type="button">⏹ Stop</button>
          <button id="auroraAnimTPose" type="button">🧍 T-pose</button>
        </div>
      </div>
    </div>
    <div id="auroraQualityInfo" title="Cobertura DDS offline (material-slot-analysis) — resto usa vertex-color no glTF."
         style="margin-top:6px;color:#b8d4ff;font-family:Consolas,monospace;font-size:11px"></div>
    <div id="auroraBytesInfo" title="Bytes de rede carregados na sessão (resource timing)."
         style="margin-top:4px;color:#8fa8c8;font-family:Consolas,monospace;font-size:11px">📦 — MB</div>
    <div id="auroraReadout" title="Leitura ao vivo: coordenada (X/Y/Z da cena) do último pick ou drag."
         style="margin-top:8px;font-family:Consolas,monospace;color:#9be29b">pick: —</div>
    <div id="auroraAnchorInfo" title="Status: contagem de âncoras/monstros do battle + mensagens de envio e salvamento do drag-to-place."
         style="margin-top:4px;color:#c9b8e6"></div>
    <div title="Fator global de escala dos modelos de monstro (multiplica a escala por-âncora do ATEL scaleOwnSize). Não afeta o que é salvo, só a visualização."
         style="margin-top:8px;display:flex;align-items:center;gap:6px">
      <span>Scale global</span>
      <input id="auroraScale" type="range" min="0.05" max="20" step="0.05" value="1" style="flex:1" />
      <span id="auroraScaleVal">1.00</span>
    </div>
    <div style="margin-top:6px;color:#8fa8c8;font-size:11px">
      Atalhos: <b>P</b> pick · <b>V</b> place · <b>S</b> salvar · <b>F</b> fit · <b>T</b> topo ·
      <b>B</b> reset · <b>1-9</b> savestate de câmera (Shift+1-9 grava) · <b>G</b> snap chão
    </div>
    </div>
  `;
  document.body.classList.add("aurora-mode");
  (document.querySelector(".sidePanel") || document.body).appendChild(panel);
  Object.assign(panel.style, { position: "relative", left: "auto", top: "auto", bottom: "auto", width: "auto", maxHeight: "none" });
  // Refuted coordinate transforms have no place in the placement workflow.
  panel.querySelector("#auroraFlipBtn").hidden = true;
  panel.querySelector("#auroraFlipBtn").disabled = true;
  const diagnostics = document.createElement("details");
  const summary = document.createElement("summary");
  summary.textContent = "Diagnóstico";
  diagnostics.appendChild(summary);
  for (const id of ["auroraZonesBtn", "auroraQualityInfo", "auroraBytesInfo"]) {
    const item = panel.querySelector(`#${id}`);
    if (item) diagnostics.appendChild(item);
  }
  panel.querySelector("#auroraOverlayBody").appendChild(diagnostics);

  for (const b of panel.querySelectorAll("button")) {
    b.style.cssText = "background:#2a1b45;color:#eee;border:1px solid #5A3A7A;border-radius:6px;padding:4px 8px;cursor:pointer";
  }

  panel.querySelector("#auroraPickBtn").addEventListener("click", togglePick);
  panel.querySelector("#auroraPlaceBtn").addEventListener("click", togglePlace);
  panel.querySelector("#auroraSaveBtn").addEventListener("click", postSave);
  panel.querySelector("#auroraFlipBtn").addEventListener("click", toggleFlip);
  panel.querySelector("#auroraZonesBtn").addEventListener("click", toggleZones);
  panel.querySelector("#auroraFocusBtn").addEventListener("click", () => focusAnchor(overlayState.selectedSphere));
  panel.querySelector("#auroraTopBtn").addEventListener("click", () => snapView("top"));
  panel.querySelector("#auroraFrontBtn").addEventListener("click", () => snapView("front"));
  panel.querySelector("#auroraFitBtn").addEventListener("click", () => snapView("fit"));
  panel.querySelector("#auroraResetBtn").addEventListener("click", () => snapView("reset"));
  panel.querySelector("#auroraInsApply").addEventListener("click", applyInspector);
  panel.querySelector("#auroraAnimPlay").addEventListener("click", playSelectedAnim);
  panel.querySelector("#auroraAnimStop").addEventListener("click", stopSelectedAnim);
  panel.querySelector("#auroraAnimTPose").addEventListener("click", tposeSelectedAnim);
  panel.querySelector("#auroraAnimSelect").addEventListener("change", (e) => { if (e.target.value) playSelectedAnim(); });

  // 🌅 sidebar colapsável (plano UI/UX): recolhe para uma tira — não bloqueia a visão do 3D.
  const bodyEl = panel.querySelector("#auroraOverlayBody");
  const collapseBtn = panel.querySelector("#auroraCollapseBtn");
  if (bodyEl && collapseBtn) {
    collapseBtn.addEventListener("click", () => {
      const hidden = bodyEl.style.display === "none";
      bodyEl.style.display = hidden ? "flex" : "none";
      collapseBtn.textContent = hidden ? "❮ Ocultar" : "❯";
      panel.style.width = hidden ? "296px" : "58px";
      panel.style.padding = hidden ? "10px 12px" : "10px 8px";
      panel.style.overflowY = hidden ? "auto" : "hidden";
    });
  }

  // 🌅 live global monster-scale factor (multiplies each anchor's catalog scale — re-applies without re-export)
  const scaleEl = panel.querySelector("#auroraScale");
  if (scaleEl) scaleEl.addEventListener("input", () => {
    monsterScale = parseFloat(scaleEl.value) || 1;
    const v = panel.querySelector("#auroraScaleVal");
    if (v) v.textContent = monsterScale.toFixed(2);
    for (const m of monsterModels) m.root.scale.setScalar(effectiveMonsterScale(m.anchor));
    refreshInspector();
  });

  installKeyboard();
}

function setReadout(text) {
  const el = document.getElementById("auroraReadout");
  if (el) el.textContent = text;
}
function setAnchorInfo(text) {
  const el = document.getElementById("auroraAnchorInfo");
  if (el) el.textContent = text;
}

/* ---------------- Actor anchors ---------------- */

async function loadAnchors(url) {
  try {
    const res = await fetchCached(new URL(url, window.location.href).href, { networkFirst: true });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    const data = await res.json();
    console.warn("[aurora-dbg] loadAnchors: HTTP ok, anchors=", Array.isArray(data.anchors) ? data.anchors.length : "n/a",
      "battleId=", data.battleId, "camera?", !!data.camera, "modelos (a.model)?", Array.isArray(data.anchors) ? data.anchors.filter(a => a.model).length : 0);
    overlayState.rawAnchors = Array.isArray(data.anchors) ? data.anchors : [];
    overlayState.battleId = data.battleId || null; // needed to POST drags back to the editor
    overlayState.cameraData = data.camera || null; // 🎥 establishing-camera marker (IDA-proven for camSetPolar 0x6004)
    overlayState.exportQuality = data.exportQuality || null;
    overlayState.zones = data.zones || null;       // 🗺️ PS2 field encounter zones (mapout.vpa)
    overlayState.zonesNote = data.zonesNote || "";
    overlayState.degradedModelCount = 0;
    invalidateMapMeshCache();
    invalidateGroundCache();
    rebuildAnchorGizmos();
    showExportQuality(data);
    const live = overlayState.rawAnchors.filter((a) => a.role === "monster_live").length;
    const zoneInfo = overlayState.zones
      ? ` · 🗺️ ${overlayState.zones.length} zona(s) PS2`
      : (overlayState.zonesNote ? ` · 🗺️ ${overlayState.zonesNote}` : "");
    setAnchorInfo(`${overlayState.rawAnchors.length} âncora(s) · ${live} monstro(s) · battle ${data.battleId || "?"}${zoneInfo}`);
    refreshBytesLoaded();
  } catch (err) {
    setAnchorInfo(`actors.json indisponível: ${err.message}`);
  }
}

function rebuildAnchorGizmos() {
  const { scene } = overlayState.dbg;
  if (overlayState.group) {
    scene.remove(overlayState.group);
    overlayState.group.traverse((o) => { o.geometry?.dispose?.(); o.material?.dispose?.(); });
  }
  // stop monster mixers from the previous build (their roots lived under the group we just removed)
  for (const m of monsterModels) m.mixer?.stopAllAction();
  monsterModels.length = 0;
  overlayState.animEntries.clear();
  overlayState.selectedSphere = null;
  hideInspector();

  const group = new THREE.Group();
  group.name = "auroraAnchors";
  // IMPORTANTE: o glTF btlmap/map é Y-up desde v2.182.10 (app.js: "btlmap glTF exports now produce Y-up...
  // Legacy π flip about X removed"). O group NÃO rotaciona mais — âncoras/monstros usam as coords direto.
  overlayState.anchorSpheres = [];

  let skipped = 0;
  for (const a of overlayState.rawAnchors) {
    const x = Number(a.x), y = Number(a.y), zr = Number(a.z);
    if (!Number.isFinite(x) || !Number.isFinite(y) || !Number.isFinite(zr)) { skipped++; continue; }
    const color = ROLE_COLOR[a.role] ?? ROLE_COLOR.default;
    const z = overlayState.flipZ ? -zr : zr;
    // 🐛 FIX (2026-08-17, Jarvis-Aurora): os spheres de raio 0.7 eram INVISÍVEIS num mapa de ~80
    // unidades com câmera a distância ~88 (raio 0.7 ≈ 1px). Aumentado para 4.0 — um marcador que o
    // usuário vê de fato. Mesmo mesh continua sendo o hotspot de drag/raycast e o portador do modelo.
    const mesh = new THREE.Mesh(
      new THREE.SphereGeometry(4.0, 16, 12),
      new THREE.MeshBasicMaterial({ color, depthTest: false, transparent: true, opacity: 0.9 })
    );
    mesh.renderOrder = 999;
    mesh.position.set(x, y, z);
    group.add(mesh);
    // 🌅 Pino vertical (cone) para os atores serem inconfundíveis de longe — sobe do chão e aponta a
    // posição. Espelho do role color; sempre por cima (renderOrder 997). NUNCA afeta o raycast (só o
    // sphere é alvo de clique/drag).
    const pin = new THREE.Mesh(
      new THREE.ConeGeometry(1.6, 10, 8),
      new THREE.MeshBasicMaterial({ color, depthTest: false, transparent: true, opacity: 0.55 })
    );
    pin.position.set(x, y + 5, z); // centro do cone em Y = altura/2
    pin.renderOrder = 997;
    group.add(pin);
    mesh.userData.pin = pin; // esconder/mover junto quando o monóolive/âncora for selecionada ou arrastada

    // thin stem to the ground plane for depth readability
    const stem = new THREE.Line(
      new THREE.BufferGeometry().setFromPoints([new THREE.Vector3(x, 0, z), new THREE.Vector3(x, y, z)]),
      new THREE.LineBasicMaterial({ color, depthTest: false, transparent: true, opacity: 0.5 })
    );
    stem.renderOrder = 998;
    group.add(stem);

    // tag the sphere as a drag-to-place target (carries its role/index + the stem & monster to move together).
    mesh.userData = { role: a.role, index: a.index, anchor: a, stem, pin, monsterRoot: null };
    overlayState.anchorSpheres.push(mesh);

    // 🌅 If this anchor carries a monster model (from the formation lineup), load the real CHR glTF on the marker.
    if (a.model) loadMonster(a, x, y, z, group, mesh);
  }
  if (skipped > 0) setAnchorInfo(`${skipped} âncora(s) com coord inválida ignorada(s).`);

  addCameraMarker(group, overlayState.cameraData);
  drawZones();

  console.warn("[aurora-dbg] rebuildAnchorGizmos: gizmos=", overlayState.anchorSpheres.length,
    "monstros=", monsterModels.length, "scene?", !!scene, "rawAnchors=", overlayState.rawAnchors.length);
  scene.add(group);
  overlayState.group = group;
}

// 🎥 Establishing-camera marker: a gold EYE sphere (camSetPolar 0x6004) + a cyan look-at REF + a line + a
// ground stem. Added to the same group as the anchors (shares the Y-up flip + flip-Z toggle).
function addCameraMarker(group, cam) {
  if (!cam) return;
  const fz = (z) => (overlayState.flipZ ? -Number(z) : Number(z));
  const sphere = (x, y, z, color, r) => {
    const s = new THREE.Mesh(
      new THREE.SphereGeometry(r, 14, 10),
      new THREE.MeshBasicMaterial({ color, depthTest: false, transparent: true, opacity: 0.95 })
    );
    s.renderOrder = 1001;
    s.position.set(Number(x), Number(y), fz(z));
    group.add(s);
    return s;
  };

  let eye = null, ref = null, line = null, stem = null;
  if (cam.hasRef) ref = sphere(cam.refX, cam.refY, cam.refZ, 0x57e0ff, 0.6); // cyan look-at point (exact)
  if (cam.hasEye) eye = sphere(cam.eyeX, cam.eyeY, cam.eyeZ, 0xffd24d, 1.2); // gold camera eye (exact)

  if (eye && ref) {
    line = new THREE.Line(
      new THREE.BufferGeometry().setFromPoints([eye.position.clone(), ref.position.clone()]),
      new THREE.LineBasicMaterial({ color: 0xffd24d, depthTest: false, transparent: true, opacity: 0.85 })
    );
    line.renderOrder = 1000;
    group.add(line);
  }
  if (eye) {
    stem = new THREE.Line(
      new THREE.BufferGeometry().setFromPoints([new THREE.Vector3(eye.position.x, 0, eye.position.z), eye.position.clone()]),
      new THREE.LineBasicMaterial({ color: 0xffd24d, depthTest: false, transparent: true, opacity: 0.4 })
    );
    stem.renderOrder = 999;
    group.add(stem);
  }

  // make the cyan REF and, when all three polar args are PUSHF-backed, the gold EYE draggable in Place mode.
  if (ref) {
    ref.userData = { role: "camera_ref", index: 0, anchor: null, stem: null, monsterRoot: null, cameraLine: line, cameraEye: eye, cameraRef: ref };
    overlayState.anchorSpheres.push(ref);
  }
  if (eye && cam.eyeDraggable) {
    eye.userData = { role: "camera_eye", index: 0, anchor: null, stem, monsterRoot: null, cameraLine: line, cameraEye: eye, cameraRef: ref };
    overlayState.anchorSpheres.push(eye);
  }

  const prev = document.getElementById("auroraAnchorInfo")?.textContent || "";
  setAnchorInfo(`${prev}${prev ? " · " : ""}🎥 ${cam.label || "câmera"}`);
}

function toggleFlip() {
  overlayState.flipZ = !overlayState.flipZ;
  const btn = document.getElementById("auroraFlipBtn");
  if (btn) btn.textContent = `flip-Z (refut.): ${overlayState.flipZ ? "ON" : "OFF"}`;
  rebuildAnchorGizmos();
}

/* ---------------- Anchor selection + inspector (F6) ---------------- */

function selectSphere(sphere) {
  overlayState.selectedSphere = sphere;
  for (const s of overlayState.anchorSpheres) {
    s.material.color.setHex(ROLE_COLOR[s.userData?.role] ?? ROLE_COLOR.default);
    s.scale.setScalar(1);
  }
  if (sphere) {
    sphere.material.color.setHex(0xffffff);
    sphere.scale.setScalar(1.35);
  }
  refreshInspector();
}

function hideInspector() {
  const el = document.getElementById("auroraInspector");
  if (el) el.style.display = "none";
}

function refreshInspector() {
  const el = document.getElementById("auroraInspector");
  const sphere = overlayState.selectedSphere;
  if (!el || !sphere) { if (el) el.style.display = "none"; return; }
  el.style.display = "block";
  const ud = sphere.userData;
  const a = ud?.anchor;
  document.getElementById("auroraInspectorTitle").textContent = `Âncora ${ud?.role}[${ud?.index}]${a?.label ? ` — ${a.label}` : ""}`;
  if (a) {
    document.getElementById("auroraInsX").value = Number(a.x).toFixed(2);
    document.getElementById("auroraInsY").value = Number(a.y).toFixed(2);
    document.getElementById("auroraInsZ").value = Number(a.z).toFixed(2);
  }
  const scaleInfo = document.getElementById("auroraInsScale");
  if (scaleInfo) {
    const anchorScale = Number(a?.scale) > 0 ? Number(a.scale) : 1;
    scaleInfo.textContent = `escala: ${anchorScale.toFixed(2)} (ATEL scaleOwnSize)${a?.flipX ? " · flipX" : ""}${ud?.degraded ? ` · ⚠ degradado: ${ud.degradedReason || ""}` : ""}`;
  }
  refreshAnimPanel();
}

/** Populate the per-monster animation dropdown: .ath named cycles (anchor.anims) + raw clips from the glTF. */
function refreshAnimPanel() {
  const panel = document.getElementById("auroraAnimPanel");
  const sel = document.getElementById("auroraAnimSelect");
  const sphere = overlayState.selectedSphere;
  const ud = sphere?.userData;
  const isMonster = ud && ud.role === "monster_live";
  if (!panel || !sel || !isMonster) { if (panel) panel.style.display = "none"; return; }
  panel.style.display = "block";
  const entry = overlayState.animEntries.get(sphere);
  const anchor = ud.anchor;
  sel.innerHTML = "";
  const addOpt = (label, value) => {
    const o = document.createElement("option");
    o.value = value; o.textContent = label;
    sel.appendChild(o);
  };
  if (anchor?.anims && anchor.anims.length) {
    addOpt(`— .ath: ${anchor.anims.length} ciclo(s) nomeado(s) —`, "");
    for (const anim of anchor.anims) addOpt(`${anim.name} (${anim.id})`, `ath:${anim.id}:${anim.name}`);
  } else {
    addOpt("— sem catálogo .ath p/ este monstro —", "");
  }
  const clips = entry?.clips || [];
  if (clips.length) {
    addOpt(`— ${clips.length} clip(s) do glTF —`, "");
    clips.forEach((c, i) => addOpt(`clip[${i}] ${c.name || "(sem nome)"}`, `clip:${i}`));
  }
  if (!anchor?.anims?.length && !clips.length) addOpt("(nenhuma animação no modelo)", "");
}

function resolveAnimTarget(selValue) {
  const sphere = overlayState.selectedSphere;
  const entry = overlayState.animEntries.get(sphere);
  if (!entry || !entry.mixer) return null;
  if (selValue.startsWith("ath:")) {
    const [, , name] = selValue.split(":");
    // match by token similarity against the glTF clip names (game symbol → clip); fallback to clip[0].
    const tokens = name.toLowerCase().split(/[_0-9]+/).filter(Boolean);
    let best = null, bestScore = -1;
    for (const c of entry.clips) {
      const cn = (c.name || "").toLowerCase();
      let score = 0;
      for (const t of tokens) if (cn.includes(t)) score++;
      if (score > bestScore) { bestScore = score; best = c; }
    }
    return best || entry.clips[0] || null;
  }
  if (selValue.startsWith("clip:")) {
    const i = parseInt(selValue.slice(5), 10);
    return entry.clips[i] || null;
  }
  return null;
}

function playSelectedAnim() {
  const sel = document.getElementById("auroraAnimSelect");
  const sphere = overlayState.selectedSphere;
  const entry = overlayState.animEntries.get(sphere);
  if (!sel || !entry || !entry.mixer) {
    setAnchorInfo("🎬 Sem modelo animado para esta âncora (glTF ausente/degradado).");
    return;
  }
  const clip = resolveAnimTarget(sel.value);
  if (!clip) { setAnchorInfo("🎬 Nenhum clip encontrado para essa animação."); return; }
  entry.mixer.stopAllAction();
  const action = entry.mixer.clipAction(clip);
  action.reset();
  action.setLoop(THREE.LoopRepeat, Infinity);
  action.play();
  ensureAnimLoop();
  setAnchorInfo(`🎬 tocando ${clip.name || "(sem nome)"} em ${entry.anchor?.monsterId || "?"}.`);
}

function stopSelectedAnim() {
  const sphere = overlayState.selectedSphere;
  const entry = overlayState.animEntries.get(sphere);
  if (entry?.mixer) entry.mixer.stopAllAction();
  setAnchorInfo("🎬 animação parada (pose atual congelada).");
}

function tposeSelectedAnim() {
  const sphere = overlayState.selectedSphere;
  const entry = overlayState.animEntries.get(sphere);
  if (!entry || !entry.mixer || !entry.clips.length) {
    setAnchorInfo("🧍 Sem animações no modelo — T-pose n/a.");
    return;
  }
  entry.mixer.stopAllAction();
  const action = entry.mixer.clipAction(entry.clips[0]);
  action.time = 0;
  action.play();
  entry.mixer.update(0);
  action.stop();
  setAnchorInfo("🧍 T-pose (frame 0 / bind pose).");
}

/** Push the inspector's typed X/Y/Z to the editor through the same POST /drag path the mouse uses. */
function applyInspector() {
  const sphere = overlayState.selectedSphere;
  const ud = sphere?.userData;
  if (!ud) return;
  const x = parseFloat(document.getElementById("auroraInsX").value);
  const y = parseFloat(document.getElementById("auroraInsY").value);
  const z = parseFloat(document.getElementById("auroraInsZ").value);
  if (![x, y, z].every(Number.isFinite)) { setAnchorInfo("Valores inválidos no inspector."); return; }
  const local = new THREE.Vector3(x, y, overlayState.flipZ ? -z : z);
  sphere.position.copy(local);
  if (ud.stem) ud.stem.geometry.setFromPoints([new THREE.Vector3(local.x, 0, local.z), new THREE.Vector3(local.x, local.y, local.z)]);
  if (ud.monsterRoot) ud.monsterRoot.position.copy(local);
  if (ud.pin) ud.pin.position.set(local.x, local.y + 5, local.z);
  const ra = overlayState.rawAnchors.find((a) => a.role === ud.role && a.index === ud.index);
  if (ra) { ra.x = x; ra.y = y; ra.z = z; }
  postDrag(ud.role, ud.index, x, y, z);
}

/* ---------------- Camera helpers: focus + snap views + savestates (F6) ---------------- */

function focusAnchor(sphere) {
  const target = sphere || overlayState.anchorSpheres[0];
  const { camera, orbitControls } = overlayState.dbg;
  if (!target) { setAnchorInfo("Nenhuma âncora para focar."); return; }
  const wp = target.getWorldPosition(new THREE.Vector3());
  if (orbitControls) {
    orbitControls.target.copy(wp);
    const dir = new THREE.Vector3(6, 6, 6).normalize();
    camera.position.copy(wp).add(dir.multiplyScalar(8));
    orbitControls.update();
    setAnchorInfo(`🎯 câmera focada em ${target.userData?.role}[${target.userData?.index}] (${wp.x.toFixed(1)}, ${wp.y.toFixed(1)}, ${wp.z.toFixed(1)}).`);
  }
}

function snapView(which) {
  const { camera, orbitControls } = overlayState.dbg;
  if (!orbitControls) return;
  const bounds = overlayState.dbg.state?.bounds;
  const center = new THREE.Vector3(0, 0, 0);
  let radius = 10;
  if (bounds?.center && bounds?.radius) {
    center.copy(bounds.center);
    radius = bounds.radius;
  }
  orbitControls.target.copy(center);
  switch (which) {
    case "top": camera.position.set(center.x, center.y + radius * 2.2, center.z); break;
    case "front": camera.position.set(center.x, center.y, center.z + radius * 2.2); break;
    case "reset": camera.position.set(radius * 1.6, radius * 1.1, radius * 1.6); break;
    case "fit":
    default: {
      camera.position.set(center.x + radius * 1.6, center.y + radius * 1.1, center.z + radius * 1.6);
      break;
    }
  }
  orbitControls.update();
}

const SAVESTATE_KEY = "aurora.camera.savestate.";

function saveCameraSlot(i) {
  const { camera, orbitControls } = overlayState.dbg;
  if (!orbitControls) return;
  try {
    localStorage.setItem(SAVESTATE_KEY + i, JSON.stringify({
      pos: camera.position.toArray(),
      target: orbitControls.target.toArray(),
    }));
    setAnchorInfo(`💾 savestate de câmera ${i} gravado.`);
  } catch { /* storage unavailable */ }
}

function loadCameraSlot(i) {
  const { camera, orbitControls } = overlayState.dbg;
  if (!orbitControls) return;
  try {
    const raw = localStorage.getItem(SAVESTATE_KEY + i);
    if (!raw) { setAnchorInfo(`savestate ${i} vazio (Shift+${i} grava).`); return; }
    const s = JSON.parse(raw);
    camera.position.fromArray(s.pos);
    orbitControls.target.fromArray(s.target);
    orbitControls.update();
    setAnchorInfo(`📷 savestate de câmera ${i} restaurado.`);
  } catch { /* storage unavailable */ }
}

/* ---------------- Pick mode ---------------- */

function installPicker() {
  const { renderer } = overlayState.dbg;
  renderer.domElement.addEventListener("pointerdown", onPointerDown, true);
  window.addEventListener("pointermove", onPointerMove);
  window.addEventListener("pointerup", onPointerUp);
}

function togglePick() {
  const { orbitControls, state } = overlayState.dbg;
  overlayState.pickMode = !overlayState.pickMode;
  const btn = document.getElementById("auroraPickBtn");
  if (btn) btn.textContent = `Pick mode: ${overlayState.pickMode ? "ON" : "OFF"}`;
  if (!orbitControls) return;

  // Drive orbit off the AUTHORITATIVE app.js mode, never a stale snapshot (app.js setMode() also writes
  // orbitControls.enabled on Orbit/Walk clicks). In walk mode pointer-lock owns the pointer, so leave orbit alone.
  if (overlayState.pickMode) {
    if (state?.mode === "orbit") orbitControls.enabled = false;
  } else {
    orbitControls.enabled = state?.mode === "orbit";
  }
}

// 🐛 FIX (2026-08-17, Jarvis-Aurora): installKeyboard estava ANINHADA dentro de togglePick (struct errada) —
// nunca era exposta ao escopo global, então buildPanel() lançava "ReferenceError: installKeyboard is not defined"
// ao chamá-la → quebrava o boot() e o loadAnchors nunca rodava (nenhum marcador/âncora aparecia).
// Movida para o escopo global. O keydown registra os atalhos P/V/S/F/T/B/G/1-9.
function installKeyboard() {
  window.addEventListener("keydown", (ev) => {
    if (ev.target && ["INPUT", "SELECT", "TEXTAREA"].includes(ev.target.tagName)) return;
    const k = ev.key.toLowerCase();
    if (k === "p") { togglePick(); ev.preventDefault(); return; }
    if (k === "v") { togglePlace(); ev.preventDefault(); return; }
    if (k === "s") { postSave(); ev.preventDefault(); return; }
    if (k === "f") { snapView("fit"); ev.preventDefault(); return; }
    if (k === "t") { snapView("top"); ev.preventDefault(); return; }
    if (k === "b") { snapView("reset"); ev.preventDefault(); return; }
    if (k === "g") {
      // F2: snap the selected anchor to the real ground (mesh raycast); no-op badge when no mesh under it.
      const sphere = overlayState.selectedSphere;
      const ud = sphere?.userData;
      if (!ud || !ud.anchor) { setAnchorInfo("Selecione uma âncora antes (clique nela)."); return; }
      const x = Number(ud.anchor.x), z = Number(ud.anchor.z);
      const gy = getGroundYInGroup(x, z);
      if (gy === null) { setAnchorInfo("🧱 Sem malha sob a âncora — nada a ajustar (Y do anchor mantido)."); return; }
      const local = new THREE.Vector3(x, gy, overlayState.flipZ ? -Number(ud.anchor.z) : Number(ud.anchor.z));
      sphere.position.copy(local);
      if (ud.stem) ud.stem.geometry.setFromPoints([new THREE.Vector3(local.x, 0, local.z), new THREE.Vector3(local.x, local.y, local.z)]);
      if (ud.monsterRoot) ud.monsterRoot.position.copy(local);
      if (ud.pin) ud.pin.position.set(local.x, local.y + 5, local.z);
      const ra = overlayState.rawAnchors.find((a) => a.role === ud.role && a.index === ud.index);
      if (ra) { ra.x = x; ra.y = gy; ra.z = Number(ra.z); }
      postDrag(ud.role, ud.index, x, gy, Number(ud.anchor.z));
      setAnchorInfo(`🧱 snap-chão: Y ${Number(ud.anchor.y).toFixed(2)} → ${gy.toFixed(2)} (malha real).`);
      return;
    }
    if (k >= "1" && k <= "9") {
      const i = parseInt(k, 10);
      if (ev.shiftKey) saveCameraSlot(i); else loadCameraSlot(i);
      ev.preventDefault();
    }
  });
}

function onPointerDown(ev) {
  // 🌅 drag-to-place: in Place mode, grab a monster_live sphere under the cursor and start dragging it.
  if (overlayState.placeMode && overlayState.dbg.state?.mode !== "walk") {
    const { renderer, camera } = overlayState.dbg;
    const rect = renderer.domElement.getBoundingClientRect();
    overlayState.ndc.x = ((ev.clientX - rect.left) / rect.width) * 2 - 1;
    overlayState.ndc.y = -((ev.clientY - rect.top) / rect.height) * 2 + 1;
    overlayState.raycaster.setFromCamera(overlayState.ndc, camera);
    const hits = overlayState.raycaster.intersectObjects(overlayState.anchorSpheres, false);
    const sphere = hits.find((h) => ["monster_live", "camera_ref", "camera_eye"].includes(h.object.userData?.role))?.object;
    if (sphere) {
      const ud = sphere.userData;
      const orig = overlayState.rawAnchors.find((a) => a.role === ud.role && a.index === ud.index);
      const original = orig ? { x: Number(orig.x) || 0, y: Number(orig.y) || 0, z: Number(orig.z) || 0 } : null;
      overlayState.dragging = { sphere, original };
      selectSphere(sphere);
      const wp = sphere.getWorldPosition(new THREE.Vector3());
      // F2: horizontal drag plane AT THE ANCHOR'S OWN Y (not y=0) — dragging keeps the anchor height unless a
      // real mesh hit is found (see onPointerMove → snapGroundDuringDrag).
      const planeY = original ? original.y : wp.y;
      overlayState.dragPlane.setFromNormalAndCoplanarPoint(new THREE.Vector3(0, 1, 0), new THREE.Vector3(wp.x, planeY, wp.z));
      const oc = overlayState.dbg.orbitControls; if (oc) oc.enabled = false;
      ev.stopPropagation(); ev.preventDefault();
      return;
    }
  }

  // dormant unless picking, and never seize the pointer during walk (pointer-lock owns it there).
  if (!overlayState.pickMode || overlayState.dbg.state?.mode === "walk") return;
  const { renderer, camera, scene } = overlayState.dbg;
  const rect = renderer.domElement.getBoundingClientRect();
  overlayState.ndc.x = ((ev.clientX - rect.left) / rect.width) * 2 - 1;
  overlayState.ndc.y = -((ev.clientY - rect.top) / rect.height) * 2 + 1;
  overlayState.raycaster.setFromCamera(overlayState.ndc, camera);

  const mapMeshes = collectMapMeshes();
  if (mapMeshes.length > 0) {
    const meshHits = overlayState.raycaster.intersectObjects(mapMeshes, true);
    if (meshHits.length > 0) {
      const hit = meshHits[0].point;
      setReadout(`pick (mesh):  X ${hit.x.toFixed(2)}  Y ${hit.y.toFixed(2)}  Z ${hit.z.toFixed(2)}`);
      if (!overlayState.pickMarker) {
        overlayState.pickMarker = new THREE.Mesh(
          new THREE.SphereGeometry(0.5, 12, 8),
          new THREE.MeshBasicMaterial({ color: 0x9be29b, depthTest: false })
        );
        overlayState.pickMarker.renderOrder = 1000;
        scene.add(overlayState.pickMarker);
      }
      overlayState.pickMarker.position.copy(hit);
      ev.stopPropagation();
      ev.preventDefault();
      return;
    }
  }

  const p = overlayState.raycaster.ray.intersectPlane(overlayState.plane, overlayState.hit);
  if (!p) { setReadout("pick: (raio paralelo ao chão)"); return; }

  setReadout(`pick (plano y=0 fallback):  X ${p.x.toFixed(2)}  Y ${p.y.toFixed(2)}  Z ${p.z.toFixed(2)}`);

  // drop / move a small marker at the pick
  if (!overlayState.pickMarker) {
    overlayState.pickMarker = new THREE.Mesh(
      new THREE.SphereGeometry(0.5, 12, 8),
      new THREE.MeshBasicMaterial({ color: 0x9be29b, depthTest: false })
    );
    overlayState.pickMarker.renderOrder = 1000;
    scene.add(overlayState.pickMarker);
  }
  overlayState.pickMarker.position.copy(p);
  ev.stopPropagation();
  ev.preventDefault();
}

/* ---------------- Drag-to-place ---------------- */

function togglePlace() {
  overlayState.placeMode = !overlayState.placeMode;
  const btn = document.getElementById("auroraPlaceBtn");
  if (btn) btn.textContent = `🖱️ Place mode: ${overlayState.placeMode ? "ON" : "OFF"}`;
  const oc = overlayState.dbg?.orbitControls;
  if (oc) {
    if (overlayState.placeMode) { if (overlayState.dbg.state?.mode === "orbit") oc.enabled = false; }
    else oc.enabled = overlayState.dbg.state?.mode === "orbit" && !overlayState.pickMode;
  }
  setAnchorInfo(overlayState.placeMode
    ? "Place mode ON — arraste um monstro (🔴 vermelho) ou a câmera (🔵 ciano=ref, 🟡 dourado=olho); depois '💾 Salvar posições'."
    : "Place mode OFF.");
}

function onPointerMove(ev) {
  const drag = overlayState.dragging;
  if (!drag) return;
  const { renderer, camera } = overlayState.dbg;
  const rect = renderer.domElement.getBoundingClientRect();
  overlayState.ndc.x = ((ev.clientX - rect.left) / rect.width) * 2 - 1;
  overlayState.ndc.y = -((ev.clientY - rect.top) / rect.height) * 2 + 1;
  overlayState.raycaster.setFromCamera(overlayState.ndc, camera);

  // F2: prefer a REAL map-mesh hit (ground) over the y=plane; fallback keeps the anchor's own height.
  let wp = null;
  const meshes = collectMapMeshes();
  if (meshes.length > 0) {
    const meshHits = overlayState.raycaster.intersectObjects(meshes, true);
    if (meshHits.length > 0) wp = meshHits[0].point.clone();
  }
  if (!wp) {
    const p = overlayState.raycaster.ray.intersectPlane(overlayState.dragPlane, overlayState.dragHit);
    if (!p) return;
    wp = p.clone();
    const ud = drag.sphere.userData;
    const orig = overlayState.rawAnchors.find((a) => a.role === ud.role && a.index === ud.index);
    if (orig) {
      // Honest fallback badge: the plane is at the anchor's Y (anchor Y != 0 is the proven chunk3 case, e.g. klyt00_00 Y=2.9).
      setReadout(`place ${ud.role}[${ud.index}]: plano no Y da âncora (${orig.y.toFixed(2)}) — sem malha sob o cursor (fallback)`);
    }
  }

  // world hit → group-LOCAL (inverts whatever transform the anchor group has, incl. the Y-up flip), then undo the
  // per-anchor flip-Z so we recover the raw GAME coord the .bin stores. Flip-agnostic by construction.
  const local = overlayState.group.worldToLocal(wp.clone());
  const sphere = drag.sphere;
  sphere.position.copy(local);
  const ud = sphere.userData;
  if (ud.stem) ud.stem.geometry.setFromPoints([new THREE.Vector3(local.x, 0, local.z), new THREE.Vector3(local.x, local.y, local.z)]);
  if (ud.monsterRoot) ud.monsterRoot.position.copy(local);
  if (ud.pin) ud.pin.position.set(local.x, local.y + 5, local.z);
  if (ud.cameraLine) {
    const eyePos = ud.role === "camera_eye" ? local.clone() : ud.cameraEye?.position.clone();
    const refPos = ud.role === "camera_ref" ? local.clone() : ud.cameraRef?.position.clone();
    if (eyePos && refPos) ud.cameraLine.geometry.setFromPoints([eyePos, refPos]);
  }

  const gx = local.x, gy = local.y, gz = overlayState.flipZ ? -local.z : local.z;
  drag.game = { x: gx, y: gy, z: gz };
  if (drag.original) {
    const dx = gx - drag.original.x, dy = gy - drag.original.y, dz = gz - drag.original.z;
    setReadout(
      `place ${ud.role}[${ud.index}]: ` +
      `(${drag.original.x.toFixed(2)}, ${drag.original.y.toFixed(2)}, ${drag.original.z.toFixed(2)}) ` +
      `→ (${gx.toFixed(2)}, ${gy.toFixed(2)}, ${gz.toFixed(2)})  ` +
      `Δ=(${dx >= 0 ? "+" : ""}${dx.toFixed(2)}, ${dy >= 0 ? "+" : ""}${dy.toFixed(2)}, ${dz >= 0 ? "+" : ""}${dz.toFixed(2)})`
    );
  } else {
    setReadout(`place ${ud.role}[${ud.index}]:  X ${gx.toFixed(2)}  Y ${gy.toFixed(2)}  Z ${gz.toFixed(2)}`);
  }
  ev.preventDefault();
}

function onPointerUp() {
  const drag = overlayState.dragging;
  if (!drag) return;
  overlayState.dragging = null;
  const oc = overlayState.dbg.orbitControls;
  if (oc) oc.enabled = overlayState.dbg.state?.mode === "orbit" && !overlayState.pickMode && !overlayState.placeMode;
  if (!drag.game) return;
  const ud = drag.sphere.userData;
  const ra = overlayState.rawAnchors.find((a) => a.role === ud.role && a.index === ud.index);
  if (ra) { ra.x = drag.game.x; ra.y = drag.game.y; ra.z = drag.game.z; } // keep across rebuilds
  postDrag(ud.role, ud.index, drag.game.x, drag.game.y, drag.game.z);
  if (drag.original) {
    const dx = drag.game.x - drag.original.x, dy = drag.game.y - drag.original.y, dz = drag.game.z - drag.original.z;
    setAnchorInfo(
      `📐 ${ud.role}[${ud.index}]: ` +
      `Δ=(${dx >= 0 ? "+" : ""}${dx.toFixed(2)}, ${dy >= 0 ? "+" : ""}${dy.toFixed(2)}, ${dz >= 0 ? "+" : ""}${dz.toFixed(2)}) ` +
      `(${drag.original.x.toFixed(2)}, ${drag.original.y.toFixed(2)}, ${drag.original.z.toFixed(2)}) → ` +
      `(${drag.game.x.toFixed(2)}, ${drag.game.y.toFixed(2)}, ${drag.game.z.toFixed(2)})`
    );
  }
}

function bridgeHeaders() {
  return { "Content-Type": "application/json", "X-FFX-Studio-Token": dragToken ?? "" };
}

function bridgeUrl(path) {
  // Preserve the ViewerHub's already-pinned loopback scheme/host and replace only the ephemeral
  // capability-bridge port. This avoids constructing or trusting any second host in the viewer.
  const target = new URL(path, window.location.origin);
  target.port = dragPort;
  return target.toString();
}

function postSave() {
  if (!overlayState.battleId || !dragPort || !dragToken) { setAnchorInfo("re-renderize a cena pela Aurora primeiro (editor atualizado)."); return; }
  fetch(bridgeUrl("/save"), {
    method: "POST",
    headers: bridgeHeaders(),
    body: JSON.stringify({ battleId: overlayState.battleId }),
  })
    .then((r) => setAnchorInfo(r.ok
      ? "💾 salvar disparado no editor — confere o status lá (grava o .bin + backup .aurora.bak)."
      : `editor recusou o save (HTTP ${r.status})`))
    .catch((e) => setAnchorInfo(`falha ao salvar (editor aberto + cena renderizada?): ${e.message}`));
}

function postDrag(role, index, x, y, z) {
  if (!overlayState.battleId) { setAnchorInfo("sem battleId — re-renderize a cena pela Aurora."); return; }
  if (!dragPort || !dragToken) { setAnchorInfo("bridge não autorizado — re-renderize a cena pela Aurora (editor atualizado)."); return; }
  fetch(bridgeUrl("/drag"), {
    method: "POST",
    headers: bridgeHeaders(),
    body: JSON.stringify({ battleId: overlayState.battleId, role, index, x, y, z }),
  })
    .then((r) => setAnchorInfo(r.ok
      ? `enviado ${role}[${index}] → X ${x.toFixed(1)} Y ${y.toFixed(1)} Z ${z.toFixed(1)} · clique '💾 Salvar posições' no editor`
      : `editor recusou (HTTP ${r.status})`))
    .catch((e) => setAnchorInfo(`falha enviando (editor aberto + cena renderizada?): ${e.message}`));
}

waitForDebug();
