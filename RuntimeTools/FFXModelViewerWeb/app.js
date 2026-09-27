// FFX HD Model Viewer — glTF gallery (three.js). Reads modelviewer-catalog.json (816 models),
// filters by category/verdict/search, loads glTF assets in bind/rest pose (T-pose) by default.
// Animation clips are opt-in via the play button — never auto-plays on load.
// Engine is host-agnostic: works in a browser today and inside an embedded WebView panel later.
import * as THREE from "three";
import { OrbitControls } from "three/addons/controls/OrbitControls.js";
import { GLTFLoader } from "three/addons/loaders/GLTFLoader.js";

const catalogUrl = new URL(
  new URLSearchParams(location.search).get("catalog") || "./modelviewer-catalog.json",
  location.href
);
const params = new URLSearchParams(location.search);

const VERDICTS = ["clean", "torsion", "exploded", "blank", "unknown"];
const state = { entries: [], cat: new Set(), verdict: new Set(VERDICTS), query: "", selected: null,
  object: null, mixer: null, clips: [], action: null, wire: false, spin: false, playing: true };

const $ = (id) => document.getElementById(id);
const el = {
  list: $("assetList"), search: $("searchBox"), catChips: $("categoryChips"), verdictChips: $("verdictChips"),
  summary: $("catalogSummary"), viewport: $("viewport"), status: $("statusText"), id: $("selectedId"),
  band: $("selectedBand"), clipSelect: $("clipSelect"), playPause: $("playPause"),
  dId: $("dId"), dCat: $("dCat"), dVerdict: $("dVerdict"), dBake: $("dBake"), dClips: $("dClips"),
  dMeshes: $("dMeshes"), dVerts: $("dVerts"), dThumb: $("dThumb"), dPath: $("dPath"),
};

// ---- three.js scene ----
const scene = new THREE.Scene();
scene.background = new THREE.Color(0x222831);
const camera = new THREE.PerspectiveCamera(45, 1, 0.01, 5000);
camera.position.set(7, 5, 9);
const renderer = new THREE.WebGLRenderer({ antialias: true });
renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
renderer.outputColorSpace = THREE.SRGBColorSpace;
el.viewport.appendChild(renderer.domElement);
const controls = new OrbitControls(camera, renderer.domElement);
controls.enableDamping = true; controls.dampingFactor = 0.08;
const grid = new THREE.GridHelper(20, 40, 0x4a5560, 0x333b44); grid.position.y = -0.001; scene.add(grid);
const key = new THREE.DirectionalLight(0xffffff, 2.0); key.position.set(5, 9, 6); scene.add(key);
const fill = new THREE.DirectionalLight(0xbcd0ff, 0.8); fill.position.set(-6, 3, -4); scene.add(fill);
scene.add(new THREE.AmbientLight(0xffffff, 0.9));
const gltfLoader = new GLTFLoader();
const clock = new THREE.Clock();
window.ffxModelViewer = { THREE, scene, camera, controls, state };

function resize() {
  const r = el.viewport.getBoundingClientRect();
  const w = Math.max(1, Math.floor(r.width)), h = Math.max(1, Math.floor(r.height));
  renderer.setSize(w, h, false); camera.aspect = w / h; camera.updateProjectionMatrix();
}
function animate() {
  requestAnimationFrame(animate);
  const dt = clock.getDelta();
  if (state.mixer && state.playing) state.mixer.update(dt);
  if (state.spin && state.object) state.object.rotation.y += dt * 0.5;
  controls.update();
  renderer.render(scene, camera);
}

// ---- catalog ----
async function loadCatalog() {
  setStatus(`Loading catalog…`);
  const res = await fetch(catalogUrl.href, { cache: "no-store" });
  if (!res.ok) throw new Error(`catalog ${res.status}`);
  const cat = await res.json();
  state.entries = cat.entries || [];
  state.cat = new Set((cat.categories || []).map((c) => c.key));
  el.summary.textContent = `${cat.summary.total} models · ${Object.entries(cat.summary.byVerdict).map(([k, v]) => `${v} ${k}`).join(" · ")}`;
  renderCatChips(cat.categories || []);
  renderVerdictChips(cat.summary.byVerdict || {});
  renderList();
  const def = params.get("id") || cat.defaultId;
  const e0 = state.entries.find((e) => e.id === def) || state.entries[0];
  if (e0) selectEntry(e0.id);
}

function renderCatChips(cats) {
  el.catChips.innerHTML = "";
  for (const c of cats) {
    const b = document.createElement("button");
    b.className = `chip${state.cat.has(c.key) ? " on" : ""}`; b.textContent = `${c.label} ${c.count}`;
    b.onclick = () => { state.cat.has(c.key) ? state.cat.delete(c.key) : state.cat.add(c.key); b.classList.toggle("on"); renderList(); };
    el.catChips.appendChild(b);
  }
}
function renderVerdictChips(byVerdict) {
  el.verdictChips.innerHTML = "";
  for (const v of VERDICTS) {
    if (!byVerdict[v]) continue;
    const b = document.createElement("button");
    b.className = `chip v-${v}${state.verdict.has(v) ? " on" : ""}`; b.textContent = `${v} ${byVerdict[v]}`;
    b.onclick = () => { state.verdict.has(v) ? state.verdict.delete(v) : state.verdict.add(v); b.classList.toggle("on"); renderList(); };
    el.verdictChips.appendChild(b);
  }
}

function renderList() {
  const q = state.query.trim().toLowerCase();
  const items = state.entries.filter((e) =>
    state.cat.has(e.category) && state.verdict.has(e.verdict) &&
    (!q || e.id.toLowerCase().includes(q)));
  el.list.innerHTML = "";
  for (const e of items) {
    const b = document.createElement("button");
    b.className = `assetItem${state.selected?.id === e.id ? " active" : ""}`;
    b.innerHTML = `
      ${e.thumb ? `<img class="assetThumb" loading="lazy" src="${e.thumb}" alt="" />` : `<div class="assetThumb noimg"></div>`}
      <div class="assetText"><span class="assetId">${esc(e.id)}</span><span class="band v-${esc(e.verdict)}">${esc(e.verdict)}</span></div>`;
    b.onclick = () => selectEntry(e.id);
    el.list.appendChild(b);
  }
  el.summary.dataset.shown = items.length;
}

async function selectEntry(id) {
  const e = state.entries.find((x) => x.id === id);
  if (!e) return;
  state.selected = e; renderList();
  el.id.textContent = e.id; el.band.textContent = e.verdict; el.band.className = `band v-${e.verdict}`;
  el.dId.textContent = e.id; el.dCat.textContent = e.categoryLabel || e.category; el.dVerdict.textContent = e.verdict;
  el.dBake.textContent = e.bakeMode || "—"; el.dClips.textContent = e.clips ?? "—"; el.dPath.textContent = e.gltf;
  el.dThumb.src = e.thumb || ""; el.dThumb.style.display = e.thumb ? "" : "none";
  await loadModel(e);
}

async function loadModel(e) {
  clearObject();
  setStatus(`Loading ${e.id}…`);
  try {
    const gltf = await gltfLoader.loadAsync(new URL(e.gltf, location.origin).href);
    state.object = gltf.scene; state.object.name = e.id; scene.add(state.object);
    // animation
    state.clips = gltf.animations || [];
    el.clipSelect.innerHTML = "";
    if (state.clips.length) {
      state.mixer = new THREE.AnimationMixer(state.object);
      state.clips.forEach((c, i) => { const o = document.createElement("option"); o.value = i; o.textContent = c.name || `clip ${i}`; el.clipSelect.appendChild(o); });
      // Default to the CLEAN REST pose (no auto-play) — animation is opt-in (click ▶). Keeps the gallery
      // consistently clean instead of auto-playing janky retarget clips. The model sits at its bind/rest pose.
      el.clipSelect.value = 0; state.playing = false;
      el.playPause.textContent = "▶"; el.playPause.setAttribute("aria-pressed", "false");
      el.clipSelect.disabled = false; el.playPause.disabled = false;
    } else {
      const o = document.createElement("option"); o.textContent = "(static)"; el.clipSelect.appendChild(o);
      el.clipSelect.disabled = true; el.playPause.disabled = true; state.mixer = null;
    }
    applyWire(); fitCamera(); writeStats();
    setStatus(`${e.id} · ${e.verdict} · ${state.clips.length} clip(s)`);
  } catch (err) {
    setStatus(`${e.id}: load failed — ${err.message}`);
  }
}

function playClip(i) {
  if (!state.mixer || !state.clips[i]) return;
  if (state.action) state.action.stop();
  state.action = state.mixer.clipAction(state.clips[i]); state.action.reset().play();
  state.playing = true; el.playPause.textContent = "⏸"; el.playPause.setAttribute("aria-pressed", "true");
  el.clipSelect.value = i;
}

function writeStats() {
  let meshes = 0, verts = 0;
  state.object?.traverse((c) => { if (c.isMesh) { meshes++; verts += c.geometry?.attributes?.position?.count || 0; } });
  el.dMeshes.textContent = meshes; el.dVerts.textContent = verts.toLocaleString();
}
function clearObject() {
  if (state.action) { state.action.stop(); state.action = null; }
  state.mixer = null; state.clips = [];
  if (!state.object) return;
  scene.remove(state.object);
  state.object.traverse((c) => { if (c.geometry) c.geometry.dispose(); if (c.material) (Array.isArray(c.material) ? c.material : [c.material]).forEach((m) => m.dispose()); });
  state.object = null;
}
function fitCamera() {
  if (!state.object) return;
  const box = new THREE.Box3().setFromObject(state.object);
  if (box.isEmpty() || !isFinite(box.min.x)) { camera.position.set(7, 5, 9); controls.target.set(0, 0, 0); return; }
  const c = box.getCenter(new THREE.Vector3()), s = box.getSize(new THREE.Vector3());
  const r = Math.max(s.x, s.y, s.z, 0.5), d = r / (2 * Math.tan((camera.fov * Math.PI) / 360));
  camera.position.copy(c).add(new THREE.Vector3(d * 0.9, d * 0.6, d * 1.1));
  camera.near = Math.max(0.01, d / 100); camera.far = Math.max(100, d * 30); camera.updateProjectionMatrix();
  controls.target.copy(c); controls.update();
}
function applyWire() { state.object?.traverse((c) => { if (c.material && "wireframe" in c.material) { c.material.wireframe = state.wire; c.material.needsUpdate = true; } }); }
function setStatus(m) { el.status.textContent = m; }
function esc(v) { return String(v).replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c])); }

// ---- events ----
el.search.oninput = () => { state.query = el.search.value; renderList(); };
el.clipSelect.onchange = () => playClip(Number(el.clipSelect.value));
el.playPause.onclick = () => {
  if (state.mixer && !state.action) { playClip(Number(el.clipSelect.value)); return; } // rest -> start selected clip
  state.playing = !state.playing; el.playPause.textContent = state.playing ? "⏸" : "▶"; el.playPause.setAttribute("aria-pressed", state.playing);
};
$("fitCamera").onclick = () => fitCamera();
$("toggleGrid").onclick = (ev) => { grid.visible = !grid.visible; ev.target.setAttribute("aria-pressed", grid.visible); };
$("toggleWire").onclick = (ev) => { state.wire = !state.wire; ev.target.setAttribute("aria-pressed", state.wire); applyWire(); };
$("toggleSpin").onclick = (ev) => { state.spin = !state.spin; ev.target.setAttribute("aria-pressed", state.spin); };
window.addEventListener("resize", resize);
resize(); animate();
loadCatalog().catch((e) => setStatus(`catalog error: ${e.message}`));
