import { selectCatalogEntry } from "./assetSelection.js";
import * as THREE from "three";
import { OrbitControls } from "three/addons/controls/OrbitControls.js";
import { PointerLockControls } from "three/addons/controls/PointerLockControls.js";
import { GLTFLoader } from "three/addons/loaders/GLTFLoader.js";
import { createSelection } from "./editor/selection.js";
import { createEditStore } from "./editor/mapEdits.js";
import { createGizmo } from "./editor/gizmo.js";
import { createMaterialPanel } from "./editor/materialLightPanel.js";

// 📦 Asset loading (F5): THREE.Cache keeps decoded glTF/PNG in memory across map switches within the session
// (the static python server has no cache headers, so the browser would otherwise re-download every byte).
THREE.Cache.enabled = true;

const params = new URLSearchParams(window.location.search);
const catalogUrl = new URL(params.get("catalog") || "/viewer-data/map/maps/catalog.json", window.location.href);
const directAsset = params.get("asset");
const autoSpector = params.get("spector") === "1";
const SLOT_STATUS = {
  baseColor: "bound_by_pmesh_pmaterial_pparameterbuffer_field172_texturesampler_base_ida_structural_candidate",
  singleImport: "bound_by_pmesh_pmaterial_pparameterbuffer_single_texture_import_link_structural_candidate",
  missingTextureLink: "blocked_missing_pparameterbuffer_texture_import_link",
  unmappedSharedData: "blocked_texture_shared_data_id_unmapped_to_root_3d_texture",
  missingMaterial: "blocked_missing_pmesh_to_pmaterial_link",
  nonDdsImport: "blocked_texture_import_path_not_dds",
  multipleTextureLinks: "blocked_multiple_pparameterbuffer_texture_import_links",
  unmatchedRoot: "blocked_texture_import_unmatched_to_root_3d_texture",
};
const DEBUG_TEXTURE_SLOTS = [
  "map",
  "normalMap",
  "aoMap",
  "lightMap",
  "emissiveMap",
  "alphaMap",
  "metalnessMap",
  "roughnessMap",
  "bumpMap",
  "displacementMap",
  "specularMap",
  "envMap",
];

const defaultEntry = {
  area: params.get("map") || params.get("area") || "Direct asset",
  assetId: "direct_asset",
  status: "success",
  gltf: directAsset,
  manifest: params.get("manifest"),
  report: params.get("report"),
  validator: params.get("validator"),
  failedGates: [],
};

const state = {
  asset: null,
  bounds: null,
  mode: "orbit",
  wireframe: false,
  velocity: new THREE.Vector3(),
  keys: new Set(),
  speed: 0.8,
  lastTime: performance.now(),
  catalog: null,
  entries: [defaultEntry],
  currentEntry: defaultEntry,
  currentSlotSummary: null,
  materialEntries: [],
  selectedMaterialKey: "",
  lastSpectorCapture: null,
  lastFramePreviewAt: 0,
  framePreviewCanvas: document.createElement("canvas"),
};

const el = {
  viewport: document.getElementById("viewport"),
  toast: document.getElementById("toast"),
  assetSummary: document.getElementById("assetSummary"),
  currentAsset: document.getElementById("currentAsset"),
  currentBadge: document.getElementById("currentBadge"),
  mapSearch: document.getElementById("mapSearch"),
  layerFilter: document.getElementById("layerFilter"),
  statusFilter: document.getElementById("statusFilter"),
  mapList: document.getElementById("mapList"),
  orbitMode: document.getElementById("orbitMode"),
  walkMode: document.getElementById("walkMode"),
  lockPointer: document.getElementById("lockPointer"),
  fitCamera: document.getElementById("fitCamera"),
  insideCamera: document.getElementById("insideCamera"),
  topCamera: document.getElementById("topCamera"),
  resetCamera: document.getElementById("resetCamera"),
  toggleGrid: document.getElementById("toggleGrid"),
  toggleWire: document.getElementById("toggleWire"),
  toggleAxes: document.getElementById("toggleAxes"),
  speedSlider: document.getElementById("speedSlider"),
  speedValue: document.getElementById("speedValue"),
  openSpector: document.getElementById("openSpector"),
  captureSpectorFrame: document.getElementById("captureSpectorFrame"),
  exportSpectorCapture: document.getElementById("exportSpectorCapture"),
  debugStatus: document.getElementById("debugStatus"),
  exportStatus: document.getElementById("exportStatus"),
  textureStatus: document.getElementById("textureStatus"),
  layerStatus: document.getElementById("layerStatus"),
  slotStatus: document.getElementById("slotStatus"),
  coverageStatus: document.getElementById("coverageStatus"),
  reasonStatus: document.getElementById("reasonStatus"),
  validatorStatus: document.getElementById("validatorStatus"),
  f3dStatus: document.getElementById("f3dStatus"),
  triangleCount: document.getElementById("triangleCount"),
  meshCount: document.getElementById("meshCount"),
  textureCount: document.getElementById("textureCount"),
  vertexCount: document.getElementById("vertexCount"),
  primitiveCount: document.getElementById("primitiveCount"),
  boundsText: document.getElementById("boundsText"),
  materialSelect: document.getElementById("materialSelect"),
  materialDebugStatus: document.getElementById("materialDebugStatus"),
  materialFacts: document.getElementById("materialFacts"),
  debugTexturePanels: document.getElementById("debugTexturePanels"),
  coverageExplain: document.getElementById("coverageExplain"),
  coverageBreakdown: document.getElementById("coverageBreakdown"),
  fileList: document.getElementById("fileList"),
  selectedPanel: document.getElementById("ffx-selected-panel"),
  selectedKey: document.getElementById("ffx-selected-key"),
  selectedSubmesh: document.getElementById("ffx-selected-submesh"),
  selectedMesh: document.getElementById("ffx-selected-mesh"),
  selectedFileMaterial: document.getElementById("ffx-selected-filematerial"),
  selectedMaterialName: document.getElementById("ffx-selected-material"),
  materialLightPanel: document.getElementById("ffx-material-light-panel"),
  saveEdits: document.getElementById("ffx-save-edits"),
  loadEdits: document.getElementById("ffx-load-edits"),
  editsStatus: document.getElementById("ffx-edits-status"),
};

const scene = new THREE.Scene();
scene.background = new THREE.Color(0x14181b);

const camera = new THREE.PerspectiveCamera(65, 1, 0.01, 1000);
camera.position.set(4, 2.4, 5);

const renderer = new THREE.WebGLRenderer({ antialias: true, preserveDrawingBuffer: true });
renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
renderer.outputColorSpace = THREE.SRGBColorSpace;
el.viewport.appendChild(renderer.domElement);

const orbitControls = new OrbitControls(camera, renderer.domElement);
orbitControls.enableDamping = true;
orbitControls.dampingFactor = 0.08;
orbitControls.target.set(0, 0, 0);

const walkControls = new PointerLockControls(camera, renderer.domElement);
walkControls.addEventListener("lock", () => setToast("Walk mode: WASD move, Q/E down/up, Esc exits"));
walkControls.addEventListener("unlock", () => setToast("Pointer unlocked"));

const grid = new THREE.GridHelper(8, 32, 0x53706c, 0x2a3235);
scene.add(grid);

const axes = new THREE.AxesHelper(2.4);
scene.add(axes);

scene.add(new THREE.HemisphereLight(0xdcefff, 0x2a2520, 1.4));
const keyLight = new THREE.DirectionalLight(0xffffff, 1.8);
keyLight.position.set(4, 7, 5);
scene.add(keyLight);

const loader = new GLTFLoader();

// 🎯 Submesh selection controller (editor/selection.js). Instantiated lazily after the FIRST map finishes loading,
// then re-pointed at each newly loaded root via getRoot() (it reads state.asset live, so no re-instantiation needed).
let selection = null;

// 🎯 Sidecar edit store (editor/mapEdits.js). Re-created per loaded map (keyed by entry.area). load()+applyTo()
// replay the saved diff over the freshly loaded immutable glTF. The glTF is never re-serialized.
let editStore = null;

// 🎯 Transform gizmo (editor/gizmo.js). Wraps three's TransformControls so the selected submesh can be moved/
// rotated/scaled; the resulting transform is written to the sidecar on drag-end. Instantiated once with the scene.
let gizmo = null;

// 🎯 Material/Light override panel (editor/materialLightPanel.js). On selection it renders editable color/opacity/
// transparent/wireframe/visible (mesh) or color/intensity (light) controls; each change applies live to the THREE
// material/light AND is written to the sidecar via editStore.setMaterial/setLight under the selection's stable key.
let materialPanel = null;

window.ffxMapViewerDebug = {
  THREE,
  scene,
  camera,
  renderer,
  orbitControls,
  walkControls,
  state,
  get selection() {
    return selection;
  },
  get editStore() {
    return editStore;
  },
  get gizmo() {
    return gizmo;
  },
  get materialPanel() {
    return materialPanel;
  },
};

// Render gate API for headless validation (Playwright) — MUST be before init()
window.__viewerReady = new Promise((resolve) => {
  window.__viewerReadyResolve = resolve;
});

init();

async function init() {
  bindUi();
  resize();
  window.addEventListener("resize", resize);
  window.addEventListener("keydown", (event) => state.keys.add(event.code));
  window.addEventListener("keyup", (event) => state.keys.delete(event.code));
  let ok = false;
  try {
    await loadCatalog();
    renderMapList();
    await loadEntry(selectInitialEntry(), false);
    ok = !!state.asset;
  } catch (error) {
    el.currentAsset.textContent = params.get("map") || "No map loaded";
    el.currentBadge.textContent = "Map unavailable";
    setToast(error.message);
  }
  window.__viewerReadyResolved = true;
  window.__viewerReadyResolve?.({ ok });
  if (autoSpector) {
    openSpectorUi().catch((error) => {
      setDebugStatus(`Spector failed: ${error.message}`);
      setToast(`Spector failed: ${error.message}`);
    });
  }
  animate();
}

/* AGS texture animation: cycles through frame textures for Mat_CTA materials */
function setupAGSAnimation(root) {
  if (!root || !state.currentEntry) return;
  const area = state.currentEntry.area || "";
  const assetId = (state.currentEntry.assetId || area.replace(/\//g, "_"));
  const agsUrl = `/viewer-data/map/maps/${area}/${assetId}.ags-animation.json`;
  fetch(agsUrl).then(r => {
    if (!r.ok) return;
    return r.json();
  }).then(ags => {
    if (!ags || !ags.frames || ags.frames.length === 0) return;
    const frameTextures = [];
    let currentFrame = 0;
    /* Preload all frame textures */
    root.traverse(child => {
      if (!child.isMesh || !child.material) return;
      const matName = (child.material.name || "").toLowerCase();
      if (!matName.includes("mat_cta")) return;
      ags.frames.forEach((f, i) => {
        const texUrl = `/viewer-data/map/maps/${area}/${f.png}`;
        const loader = new THREE.TextureLoader();
        loader.load(texUrl, tex => {
          frameTextures[i] = tex;
        });
      });
      /* Animate every 200ms */
      if (state._agsInterval) clearInterval(state._agsInterval);
      state._agsInterval = setInterval(() => {
        if (frameTextures.length === 0) return;
        currentFrame = (currentFrame + 1) % frameTextures.length;
        if (child.material.map && frameTextures[currentFrame]) {
          child.material.map = frameTextures[currentFrame];
          child.material.needsUpdate = true;
        }
      }, 200);
    });
  }).catch(() => {});
}

function bindUi() {
  el.mapSearch.addEventListener("input", renderMapList);
  el.layerFilter.addEventListener("change", renderMapList);
  el.statusFilter.addEventListener("change", renderMapList);
  el.orbitMode.addEventListener("click", () => setMode("orbit"));
  el.walkMode.addEventListener("click", () => setMode("walk"));
  el.lockPointer.addEventListener("click", () => {
    setMode("walk");
    walkControls.lock();
  });
  el.fitCamera.addEventListener("click", fitCamera);
  el.insideCamera.addEventListener("click", insideCamera);
  el.topCamera.addEventListener("click", topCamera);
  el.resetCamera.addEventListener("click", resetCamera);
  el.toggleGrid.addEventListener("change", () => (grid.visible = el.toggleGrid.checked));
  el.toggleAxes.addEventListener("change", () => (axes.visible = el.toggleAxes.checked));
  el.toggleWire.addEventListener("change", () => {
    state.wireframe = el.toggleWire.checked;
    applyWireframe();
  });
  el.openSpector?.addEventListener("click", () => {
    openSpectorUi().catch((error) => {
      setDebugStatus(`Spector failed: ${error.message}`);
      setToast(`Spector failed: ${error.message}`);
    });
  });
  el.captureSpectorFrame?.addEventListener("click", () => {
    captureSpectorFrame().catch((error) => {
      setDebugStatus(`Capture failed: ${error.message}`);
      setToast(`Capture failed: ${error.message}`);
    });
  });
  el.exportSpectorCapture?.addEventListener("click", () => {
    exportLastSpectorCapture();
  });
  el.materialSelect?.addEventListener("change", () => {
    state.selectedMaterialKey = el.materialSelect.value;
    renderMaterialDebug();
  });
  el.speedSlider.addEventListener("input", () => {
    state.speed = Number(el.speedSlider.value);
    el.speedValue.textContent = state.speed.toFixed(1);
  });
  el.saveEdits?.addEventListener("click", () => {
    handleSaveEdits();
  });
  el.loadEdits?.addEventListener("click", () => {
    handleLoadEdits();
  });
  window.addEventListener("keydown", onGizmoShortcut);
  renderMaterialDebug();
}

// 🎯 Gizmo shortcuts — W/E/R = translate/rotate/scale, Esc = detach. ONLY active in orbit/edit mode and when the
// pointer is NOT locked, so walk-mode WASD/QE (PointerLockControls) is never clobbered. Ignores keystrokes typed
// into form fields. Mode keys only act when a mesh is selected (otherwise they would silently change a hidden mode).
function onGizmoShortcut(event) {
  if (!gizmo) return;
  if (state.mode !== "orbit") return;            // walk mode owns W/E/R for movement
  if (document.pointerLockElement) return;       // never while pointer-locked
  if (event.ctrlKey || event.metaKey || event.altKey) return;
  const target = event.target;
  const tag = target && target.tagName ? target.tagName.toUpperCase() : "";
  if (tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT" || (target && target.isContentEditable)) return;

  switch (event.code) {
    case "Escape":
      if (selection?.getSelected()) {
        selection.select(null); // clears highlight + fires onPick(null) -> gizmo.detach()
        event.preventDefault();
      } else if (gizmo.getAttached()) {
        gizmo.detach();
        event.preventDefault();
      }
      return;
    case "KeyW":
      if (!gizmo.getAttached()) return;
      gizmo.setMode("translate");
      event.preventDefault();
      return;
    case "KeyE":
      if (!gizmo.getAttached()) return;
      gizmo.setMode("rotate");
      event.preventDefault();
      return;
    case "KeyR":
      if (!gizmo.getAttached()) return;
      gizmo.setMode("scale");
      event.preventDefault();
      return;
    default:
  }
}

async function loadCatalog() {
  if (directAsset) {
    state.entries = [defaultEntry];
    return;
  }
  try {
    const catalog = await fetchJson(catalogUrl);
    if (Array.isArray(catalog.entries) && catalog.entries.length > 0) {
      state.catalog = catalog;
      state.entries = catalog.entries;
      el.assetSummary.textContent = `${catalog.successCount} visual / ${catalog.totalCount} cataloged`;
      return;
    }
  } catch (error) {
    setToast(`Catalog unavailable: ${error.message}`);
  }

  state.catalog = null;
  state.entries = [];
  el.assetSummary.textContent = "No exported map available";
}

function selectInitialEntry() {
  return selectCatalogEntry(state.entries, params.get("map") || params.get("area"));
}

function renderMapList() {
  const query = el.mapSearch.value.trim().toLowerCase();
  const layer = el.layerFilter.value;
  const status = el.statusFilter.value;
  const entries = state.entries
    .filter((entry) =>
      (!query
        || entry.area.toLowerCase().includes(query)
        || entry.status.toLowerCase().includes(query)
        || (entry.textureStatus || "").toLowerCase().includes(query)
        || (entry.primaryAssetLayer || "").toLowerCase().includes(query)))
    .filter((entry) => layer === "all" || (entry.primaryAssetLayer || "unknown") === layer)
    .filter((entry) => {
      if (status === "all") return true;
      if (status === "success" || status === "blocked") return entry.status === status;
      return (entry.textureStatus || "") === status;
    })
    .slice(0, 500);

  el.mapList.innerHTML = entries
    .map((entry) => {
      const active = state.currentEntry?.area === entry.area ? " active" : "";
      const statusText = entry.status === "success" ? entry.textureStatus || "visual" : entry.status;
      const layerText = entry.primaryAssetLayer || "unknown";
      return `<button class="mapButton${active}" type="button" data-area="${escapeHtml(entry.area)}">
        <span>${escapeHtml(entry.area)}</span>
        <strong>${escapeHtml(statusText)}</strong>
        <small>${escapeHtml(layerText)}</small>
      </button>`;
    })
    .join("");

  for (const button of el.mapList.querySelectorAll("button[data-area]")) {
    button.addEventListener("click", () => {
      const entry = state.entries.find((item) => item.area === button.dataset.area);
      if (entry) loadEntry(entry, true);
    });
  }

  el.mapList.querySelector(".mapButton.active")?.scrollIntoView({ block: "nearest" });
}

async function loadEntry(entry, updateUrl) {
  state.currentEntry = entry;
  renderMapList();
  if (updateUrl) {
    const next = new URL(window.location.href);
    next.searchParams.set("map", entry.area);
    next.searchParams.delete("asset");
    next.searchParams.delete("manifest");
    next.searchParams.delete("report");
    next.searchParams.delete("validator");
    window.history.replaceState(null, "", next);
  }

  await loadReports(entry);
  await loadAsset(entry);
}

async function loadReports(entry) {
  const [manifest, report, validator, slotAnalysis] = await Promise.all([
    fetchJsonOptional(entry.manifest),
    fetchJsonOptional(entry.report),
    fetchJsonOptional(entry.validator),
    fetchJsonOptional(entry.materialSlotAnalysisReport),
  ]);
  const slotSummary = summarizeSlotAnalysis(slotAnalysis, report, entry);
  state.currentSlotSummary = slotSummary;

  el.currentAsset.textContent = entry.area;
  el.exportStatus.textContent = report?.exportDecisionBand || entry.decisionBand || "-";
  el.textureStatus.textContent = report?.textureStatus || entry.textureStatus || "-";
  el.layerStatus.textContent = report?.primaryAssetLayer || entry.primaryAssetLayer || "-";
  el.slotStatus.textContent = slotSummary.label;
  el.coverageStatus.textContent = slotSummary.coverage;
  el.reasonStatus.textContent = selectReasonText(entry, report, slotSummary);
  el.textureCount.textContent = (report?.textureCount ?? entry.textureCount ?? 0).toLocaleString();
  el.triangleCount.textContent = (report?.triangleCount ?? entry.triangleCount ?? 0).toLocaleString();
  el.validatorStatus.textContent = validator ? `${validator.issues.numErrors} errors / ${validator.issues.numWarnings} warnings` : "not run";
  el.f3dStatus.textContent = entry.status === "success" ? "not run for batch" : "blocked";
  renderCoverageBreakdown(slotSummary);
  el.currentBadge.textContent = [
    entry.status === "success" ? entry.textureStatus || "static_visual_candidate" : entry.status,
    entry.primaryAssetLayer || "unknown",
    slotSummary.badge,
  ]
    .filter(Boolean)
    .join(" / ");

  if (manifest) {
    el.assetSummary.textContent = `${manifest.fileCount} files / ${formatBytes(manifest.totalBytes)}`;
  } else if (state.catalog) {
    el.assetSummary.textContent = `${state.catalog.successCount} visual / ${state.catalog.totalCount} cataloged`;
  }

  const files = [
    ["layer", report?.primaryAssetLayer || entry.primaryAssetLayer],
    ["gltf", entry.gltf],
    ["manifest", entry.manifest],
    ["export report", entry.report],
    ["texture binding", entry.textureBindingReport],
    ["slot analysis", entry.materialSlotAnalysisReport],
    ["validator", entry.validator],
    ["primary dae", manifest?.primaryDaePath],
    ["2d dae", manifest?.twoDDaePath],
    ["ags", manifest?.textureAnimationAgsPath],
    ["error", entry.error],
  ].filter(([, value]) => value);

  el.fileList.innerHTML = files
    .map(([label, value]) => `<div class="fileItem"><strong>${escapeHtml(label)}</strong><span>${escapeHtml(value)}</span></div>`)
    .join("");
}

async function loadAsset(entry) {
  clearAsset();
  if (!entry.gltf) {
    refreshMaterialEntries();
    setToast(entry.error || "No glTF generated for this map yet.");
    return;
  }

  const assetUrl = new URL(entry.gltf, window.location.href);
  setToast(`Loading ${assetUrl.pathname}`);
  try {
    const gltf = await loader.loadAsync(assetUrl.href);
    state.asset = gltf.scene;
    state.asset.name = entry.assetId || entry.area;
    applyAssetOrientation(state.asset, entry);
    scene.add(state.asset);
    applyWireframe();
    updateGeometryStats();
    refreshMaterialEntries();
    setupSelection();
    await setupEditStore(entry);
    fitCamera();
    setupAGSAnimation(state.asset);
    setToast(buildLoadedToast());
  } catch (error) {
    clearAsset();
    refreshMaterialEntries();
    setToast(`Load failed: ${error.message}`);
  }
}

// ORIENTAÇÃO — o glTF dos mapas (btlmap e field map_*) já é Y-up (o chão embaixo, céu em cima).
// O flip π universal do legado invertia os mapas (de cabeça pra baixo) — corrigido 2026-08-02 (Jarvis-IFRIT):
// o flip π em X do btlmap (v2.182.10) deixava a arena de cabeça pra baixo. Sem flip: rotation 0.
function applyAssetOrientation(root, entry) {
  root.rotation.set(0, 0, 0);
}

function clearAsset() {
  // Drop any active submesh highlight before the old root leaves the scene (its BoxHelper targets that root).
  if (selection) {
    selection.select(null);
  }
  // Detach the transform gizmo so it never dangles on a mesh from the outgoing map root.
  gizmo?.detach();
  // Clear the material/light panel so it never edits a material from the outgoing map root.
  materialPanel?.clear();
  if (state.asset) {
    scene.remove(state.asset);
    state.asset = null;
  }

  state.bounds = null;
  state.materialEntries = [];
  state.selectedMaterialKey = "";
  el.meshCount.textContent = "-";
  el.vertexCount.textContent = "-";
  el.primitiveCount.textContent = "-";
  el.boundsText.textContent = "-";
  renderMaterialDebug();
}

// 🎯 Lazily create the submesh selection controller (once), then keep its panel updated on every pick.
// getRoot returns the live loaded root (state.asset), so a single controller follows each newly loaded map.
function setupSelection() {
  if (!selection) {
    selection = createSelection({
      THREE,
      scene,
      camera,
      renderer,
      domElement: renderer.domElement,
      getRoot: () => state.asset,
      // While a gizmo handle is being dragged, suppress submesh re-picking so a drag never reselects.
      isGizmoDragging: () => Boolean(gizmo && gizmo.isDragging()),
      // Also bail when a gizmo handle is merely hovered/active (axis !== null) — pre-drag, before isDragging flips —
      // so clicking a handle that overlaps a different mesh behind it never re-selects mid-click.
      isGizmoEngaged: () =>
        Boolean(gizmo && typeof gizmo.getControls === "function" && gizmo.getControls() && gizmo.getControls().axis !== null),
    });
    selection.onPick(updateSelectedPanel);
    setupGizmo();
    setupMaterialPanel();
    // Pick -> drive the gizmo: attach to the picked mesh, or detach when the selection is cleared.
    selection.onPick((payload) => {
      if (!gizmo) return;
      if (payload && payload.object) {
        gizmo.attach(payload.object);
      } else {
        gizmo.detach();
      }
    });
    // Pick -> drive the material/light override panel: render editable controls for the picked object, or clear it.
    selection.onPick((payload) => {
      if (!materialPanel) return;
      if (payload && payload.object) {
        materialPanel.show(payload, { onChange: onMaterialLightChange });
      } else {
        materialPanel.clear();
      }
    });
  } else {
    // New map loaded: clear the stale highlight/panel (getRoot already points at the new state.asset).
    selection.select(null);
    gizmo?.detach();
    materialPanel?.clear();
  }
}

// 🎯 Lazily create the material/light override panel (once), mounted into its index.html container.
function setupMaterialPanel() {
  if (materialPanel) return;
  if (!el.materialLightPanel) return; // container missing → skip silently (panel is optional UI)
  materialPanel = createMaterialPanel({ mount: el.materialLightPanel });
}

// 🎯 Persist hook handed to materialPanel.show(): the panel has ALREADY applied the change live to the THREE
// material/light; here we write the SAME partial into the sidecar under the selection's stable key so applyTo()
// replay restores it. section is "material" | "light"; patch matches the c2 sidecar material/light shapes.
function onMaterialLightChange(section, patch) {
  if (!editStore || !selection) return;
  const object = selection.getSelected();
  if (!object) return;
  const key = selection.keyOf(object);
  if (!key) return; // no stable key → live-only (panel already noted this to the user)
  if (section === "material") {
    editStore.setMaterial(key, patch);
  } else if (section === "light") {
    editStore.setLight(key, patch);
  } else {
    return;
  }
  setEditsStatus(`unsaved ${section} edit on ${key} (Save to persist)`);
}

// 🎯 Lazily create the transform gizmo (once). It shares the scene/camera/renderer/orbitControls with the viewer.
// On drag-end (phase "end") the selected object's live transform is written to the sidecar under its stable key.
function setupGizmo() {
  if (gizmo) return;
  gizmo = createGizmo({
    THREE,
    scene,
    camera,
    renderer,
    domElement: renderer.domElement,
    orbitControls,
  });
  gizmo.onObjectChange((payload) => {
    // Keep the selection highlight box aligned with the object as it moves under the gizmo.
    selection?.refreshHelper();
    // Persist only on commit (drag finished) — phase "drag" is a live preview and would spam the sidecar.
    if (payload.phase !== "end") return;
    if (!editStore || !selection) return;
    const object = payload.object;
    if (!object) return;
    const key = selection.keyOf(object);
    if (!key) return;
    editStore.setTransform(key, {
      position: [object.position.x, object.position.y, object.position.z],
      rotation: [object.rotation.x, object.rotation.y, object.rotation.z], // Euler XYZ, RADIANS (sidecar contract)
      scale: [object.scale.x, object.scale.y, object.scale.z],
    });
    setEditsStatus(`unsaved edit on ${key} (Save to persist)`);
  });
}

// 🎯 Sidecar edit store lifecycle. Re-create the store for the freshly loaded map (keyed by its stable area id),
// load the persisted diff (granted file handle else localStorage), and replay it over the loaded glTF root.
// applyTo() is idempotent (baseline captured on first sight), so this is safe to call on every map load.
async function setupEditStore(entry) {
  const mapId = entry?.area || entry?.assetId || "map";
  try {
    editStore = createEditStore({ mapId });
    await editStore.load();
    if (state.asset) {
      const applied = editStore.applyTo(state.asset);
      // Keep any active highlight box aligned with objects that just moved.
      selection?.refreshHelper();
      const count = Object.keys(editStore.toJSON().edits).length;
      setEditsStatus(count > 0 ? `${count} edit(s) loaded; ${applied} replayed` : "no saved edits");
    } else {
      setEditsStatus("no map loaded");
    }
  } catch (error) {
    setEditsStatus(`edit store error: ${error.message}`);
  }
}

function setEditsStatus(text) {
  if (el.editsStatus) el.editsStatus.textContent = text;
}

async function handleSaveEdits() {
  if (!editStore) {
    setEditsStatus("no map loaded");
    return;
  }
  try {
    const result = await editStore.save();
    const count = Object.keys(editStore.toJSON().edits).length;
    setEditsStatus(`saved ${count} edit(s) (${result.persisted})`);
    setToast(`Map edits saved (${result.persisted}).`);
  } catch (error) {
    setEditsStatus(`save failed: ${error.message}`);
    setToast(`Save failed: ${error.message}`);
  }
}

async function handleLoadEdits() {
  if (!editStore) {
    setEditsStatus("no map loaded");
    return;
  }
  try {
    // Let the user point at an existing sidecar file when File System Access is available, then replay it.
    if (typeof window !== "undefined" && typeof window.showOpenFilePicker === "function") {
      try {
        const [handle] = await window.showOpenFilePicker({
          multiple: false,
          types: [{ description: "FFX map edits sidecar", accept: { "application/json": [".json"] } }],
        });
        if (handle) editStore.setFileHandle(handle);
      } catch (pickErr) {
        if (pickErr && pickErr.name === "AbortError") {
          // user cancelled the file picker → fall through to localStorage reload
        } else {
          throw pickErr;
        }
      }
    }
    await editStore.load();
    let applied = 0;
    if (state.asset) applied = editStore.applyTo(state.asset);
    selection?.refreshHelper();
    const count = Object.keys(editStore.toJSON().edits).length;
    setEditsStatus(`loaded ${count} edit(s); ${applied} replayed`);
    setToast(`Map edits loaded (${count}).`);
  } catch (error) {
    setEditsStatus(`load failed: ${error.message}`);
    setToast(`Load failed: ${error.message}`);
  }
}

function updateSelectedPanel(payload) {
  if (!el.selectedPanel) return;
  const has = Boolean(payload && payload.object);
  el.selectedPanel.dataset.hasSelection = has ? "1" : "0";
  if (el.selectedKey) el.selectedKey.textContent = has ? payload.key ?? "-" : "-";
  if (el.selectedSubmesh) el.selectedSubmesh.textContent = has && payload.submeshId !== null ? String(payload.submeshId) : "-";
  if (el.selectedMesh) el.selectedMesh.textContent = has && payload.meshId !== null ? String(payload.meshId) : "-";
  if (el.selectedFileMaterial) el.selectedFileMaterial.textContent = has && payload.fileMaterialId !== null ? String(payload.fileMaterialId) : "-";
  if (el.selectedMaterialName) el.selectedMaterialName.textContent = has ? payload.materialName ?? "-" : "-";
}

function summarizeSlotAnalysis(slotAnalysis, report, entry) {
  const primaryLayer = report?.primaryAssetLayer || entry.primaryAssetLayer || "unknown";
  const rows = Array.isArray(slotAnalysis?.rows) ? slotAnalysis.rows : [];
  const statusCounts = countRowsBy(rows, (row) => row.status);
  const classificationSummary = slotAnalysis?.classificationSummary || null;
  const roleCandidateSummary = slotAnalysis?.roleCandidateSummary || null;
  const namedSignalSummary = slotAnalysis?.namedSignalSummary || null;
  const statusBreakdown = Array.isArray(slotAnalysis?.statusBreakdown) ? slotAnalysis.statusBreakdown : [];
  if (primaryLayer === "2d_prerendered") {
    const primitiveCount = Number(report?.primitiveCount ?? slotAnalysis?.submeshCount ?? 0);
    const textureCount = Number(report?.textureCount ?? 0);
    const hasCoverage = Number.isFinite(primitiveCount) && primitiveCount > 0;
    const unresolved2d = (statusCounts[SLOT_STATUS.missingTextureLink] || 0)
      + (statusCounts[SLOT_STATUS.multipleTextureLinks] || 0)
      + (statusCounts[SLOT_STATUS.nonDdsImport] || 0)
      + (statusCounts[SLOT_STATUS.unmappedSharedData] || 0)
      + (statusCounts[SLOT_STATUS.unmatchedRoot] || 0);
    return {
      label: "2d-fallback",
      coverage: hasCoverage
        ? `${textureCount.toLocaleString()} / ${primitiveCount.toLocaleString()} (2d submesh-order)`
        : `${textureCount.toLocaleString()} textures`,
      badge: hasCoverage ? `2d-fallback ${textureCount}/${primitiveCount}` : "2d-fallback",
      reason: report?.textureBindingDecisionBand || null,
      explanation:
        "This entry is on the 2d prerendered fallback lane. Visible coverage can come from texture-count order or structural import links, not from proved root base-color slot binding.",
      breakdown: [
        createBreakdownItem(
          "2d texture candidate",
          textureCount,
          "warn",
          "Fallback texture count available for the prerendered slice.",
          { showWhenZero: true },
        ),
        createBreakdownItem(
          "unresolved slot rows",
          unresolved2d,
          unresolved2d > 0 ? "muted" : "ok",
          "Rows that still do not resolve a clean DDS import path inside the fallback lane.",
        ),
      ].filter(Boolean),
      counts: {
        baseColor: 0,
        plainImport: 0,
        specialEffect: 0,
        missingMaterial: 0,
        missingTexture: statusCounts[SLOT_STATUS.missingTextureLink] || 0,
        otherUnresolved: unresolved2d - (statusCounts[SLOT_STATUS.missingTextureLink] || 0),
      },
    };
  }

  if (!slotAnalysis) {
    return {
      label: entry.status === "success" ? "not analyzed" : "-",
      coverage: "-",
      badge: null,
      reason: null,
      explanation: entry.status === "success"
        ? "No material-slot analysis report was available for this entry."
        : "This entry has no generated material-slot analysis because the export stayed blocked.",
      breakdown: [],
      counts: {
        baseColor: 0,
        plainImport: 0,
        specialEffect: 0,
        missingMaterial: 0,
        missingTexture: 0,
        otherUnresolved: 0,
      },
    };
  }

  const total = Number(slotAnalysis.submeshCount ?? report?.primitiveCount ?? 0);
  const bound = Number(slotAnalysis.boundSubmeshCount ?? 0);
  const hasCoverage = Number.isFinite(total) && total > 0;
  let label = slotAnalysis.decisionBand || "analyzed";
  let badge = null;

  if (hasCoverage) {
    if (bound <= 0) {
      label = "none";
      badge = "no-slot-bind";
    } else if (bound >= total) {
      label = "full";
      badge = "full-slot-bind";
    } else {
      label = "partial";
      badge = `partial-bind ${bound}/${total}`;
    }
  }

  const coverage = hasCoverage
    ? `${bound.toLocaleString()} / ${total.toLocaleString()} (${formatPercent(bound / total)})`
    : "-";

  const baseColorCount = classificationSummary?.boundBaseTextureSamplerRows ?? statusCounts[SLOT_STATUS.baseColor] ?? 0;
  const singleImportCount = classificationSummary?.boundImportLinkRows ?? statusCounts[SLOT_STATUS.singleImport] ?? 0;
  const specialEffectCount = rows.filter(
    (row) =>
      row.status === SLOT_STATUS.singleImport
      && row.textureLinkTargetBlockName
      && row.textureLinkTargetBlockName !== "PAssetReference",
  ).length;
  const plainImportCount = Math.max(0, singleImportCount - specialEffectCount);
  const missingMaterialCount = classificationSummary?.missingMaterialLinkRows ?? statusCounts[SLOT_STATUS.missingMaterial] ?? 0;
  const missingParameterBufferCount = classificationSummary?.missingParameterBufferLinkRows ?? 0;
  const missingTextureCount = classificationSummary?.missingTextureLinkRows ?? statusCounts[SLOT_STATUS.missingTextureLink] ?? 0;
  const singleImportCandidateCount = roleCandidateSummary?.singleImportCandidateRows ?? singleImportCount;
  const nonBaseTargetCount = roleCandidateSummary?.nonBaseTargetBlockCandidateRows ?? specialEffectCount;
  const baseTextureCandidateCount = roleCandidateSummary?.baseTextureSamplerCandidateRows ?? baseColorCount;
  const reflectionRowCount = classificationSummary?.reflectionTextureRows ?? 0;
  const waterRowCount = classificationSummary?.waterTextureRows ?? 0;
  const shaderAssetRowCount = classificationSummary?.shaderDrivenTextureRows ?? 0;
  const textureSamplerSignals = namedSignalSummary?.textureSamplerSignals ?? 0;
  const textureSamplerStateSignals = namedSignalSummary?.textureSamplerStateSignals ?? 0;
  const shadowMapSamplerSignals = namedSignalSummary?.shadowMapSamplerSignals ?? 0;
  const frameBufferSignals = namedSignalSummary?.frameBufferSignals ?? 0;
  const targetTextureSignals = namedSignalSummary?.targetTextureSignals ?? 0;
  const ccTextureSignals = namedSignalSummary?.ccTextureSignals ?? 0;
  const uvScaleBiasSignals = namedSignalSummary?.uvScaleBiasSignals ?? 0;
  const yuvSamplerSignals = namedSignalSummary?.yuvSamplerSignals ?? 0;
  const screenVideoSignals = namedSignalSummary?.screenVideoSignals ?? 0;
  const assetShaderPathSignals = namedSignalSummary?.assetShaderPathSignals ?? 0;
  const otherUnresolvedCount = classificationSummary
    ? (classificationSummary.multipleTextureLinkRows || 0)
      + (classificationSummary.missingSharedDataIdRows || 0)
      + (classificationSummary.specialOrWrongTextureTypeRows || 0)
      + (classificationSummary.unmappedTextureReferenceRows || 0)
      + (classificationSummary.decodeIssueRows || 0)
      + (classificationSummary.otherBlockedRows || 0)
    : (statusCounts[SLOT_STATUS.multipleTextureLinks] || 0)
      + (statusCounts[SLOT_STATUS.unmappedSharedData] || 0)
      + (statusCounts[SLOT_STATUS.nonDdsImport] || 0)
      + (statusCounts[SLOT_STATUS.unmatchedRoot] || 0);
  const topBlockedFamiliesText = summarizeTopBlockedFamilies(statusBreakdown);

  return {
    label,
    coverage,
    badge,
    reason: slotAnalysis.nextStrike || slotAnalysis.decisionBand || null,
    explanation: buildCoverageExplanation({
      total,
      bound,
      baseColorCount: baseTextureCandidateCount,
      plainImportCount,
      specialEffectCount: nonBaseTargetCount,
      singleImportCandidateCount,
      missingMaterialCount,
      missingParameterBufferCount,
      missingTextureCount,
      reflectionRowCount,
      waterRowCount,
      shaderAssetRowCount,
      textureSamplerSignals,
      textureSamplerStateSignals,
      shadowMapSamplerSignals,
      frameBufferSignals,
      targetTextureSignals,
      ccTextureSignals,
      uvScaleBiasSignals,
      yuvSamplerSignals,
      screenVideoSignals,
      assetShaderPathSignals,
      otherUnresolvedCount,
      topBlockedFamiliesText,
    }),
    breakdown: [
      createBreakdownItem(
        "base-color candidate",
        baseTextureCandidateCount,
        baseTextureCandidateCount > 0 ? "ok" : "muted",
        "field172 -> TextureSampler route. Strongest current base-color evidence, still structural candidate.",
      ),
      createBreakdownItem(
        "import-link candidate",
        singleImportCandidateCount,
        singleImportCandidateCount > 0 ? "warn" : "muted",
        describeImportLinkCandidate(singleImportCandidateCount, nonBaseTargetCount),
      ),
      createBreakdownItem(
        "non-base target block",
        nonBaseTargetCount,
        nonBaseTargetCount > 0 ? "warn" : "muted",
        "Rows that resolve a texture import candidate but currently route through non-base target blocks such as animation/effect-style paths.",
      ),
      createBreakdownItem(
        "missing material link",
        missingMaterialCount,
        missingMaterialCount > 0 ? "danger" : "muted",
        "PMesh rows that never resolved a PMaterial bridge.",
      ),
      createBreakdownItem(
        "missing parameter buffer link",
        missingParameterBufferCount,
        missingParameterBufferCount > 0 ? "danger" : "muted",
        "PMaterial rows that never resolved a PParameterBuffer bridge.",
      ),
      createBreakdownItem(
        "missing texture link",
        missingTextureCount,
        missingTextureCount > 0 ? "danger" : "muted",
        "PParameterBuffer rows that did not resolve a usable DDS import path.",
      ),
      createBreakdownItem(
        "other unresolved path",
        otherUnresolvedCount,
        otherUnresolvedCount > 0 ? "muted" : "ok",
        "Multiple links, non-DDS imports, or sharedData that did not map back to a root texture.",
      ),
      createBreakdownItem(
        "reflection-path rows",
        reflectionRowCount,
        reflectionRowCount > 0 ? "warn" : "muted",
        "Rows whose import path already points at reflection-style texture assets. This is a path family hint, not automatic runtime bind proof.",
      ),
      createBreakdownItem(
        "water-path rows",
        waterRowCount,
        waterRowCount > 0 ? "warn" : "muted",
        "Rows whose import path already points at Water/Znkd08_Water style assets. Useful for grouping effect-heavy maps, not for claiming a proved water-material lane.",
      ),
      createBreakdownItem(
        "shader-asset rows",
        shaderAssetRowCount,
        shaderAssetRowCount > 0 ? "warn" : "muted",
        "Rows that currently point at shader assets or shader-driven imports instead of a plain DDS texture path.",
      ),
      createBreakdownItem(
        "TextureSampler signal",
        textureSamplerSignals,
        textureSamplerSignals > 0 ? "ok" : "muted",
        "Named base texture parameter found in the asset strings. This matches the strongest current IDA-backed bind family, but still does not prove row-by-row draw use on its own.",
      ),
      createBreakdownItem(
        "sampler-state / shadow signal",
        textureSamplerStateSignals + shadowMapSamplerSignals,
        textureSamplerStateSignals + shadowMapSamplerSignals > 0 ? "warn" : "muted",
        "Named sampler-state and ShadowMapSampler signals. These are adjacent lanes around deferred/shadow or state binding, not automatic base-color proof.",
      ),
      createBreakdownItem(
        "framebuffer / post-process signal",
        frameBufferSignals + targetTextureSignals + ccTextureSignals + uvScaleBiasSignals,
        frameBufferSignals + targetTextureSignals + ccTextureSignals + uvScaleBiasSignals > 0 ? "warn" : "muted",
        "Named framebuffer/post-process params such as FrameBuffer, TargetTexture, CCTexture, or UvScaleBias. They help separate screen-space/composite paths from base map texture binds.",
      ),
      createBreakdownItem(
        "YUV / video signal",
        yuvSamplerSignals + screenVideoSignals,
        yuvSamplerSignals + screenVideoSignals > 0 ? "warn" : "muted",
        "Named TextureSamplerY/U/V and screen-video params such as zCrct/zMatrix/ScreenShift. These point to video-style lanes, not a separate normal-map or water-material proof.",
      ),
      createBreakdownItem(
        "asset shader-path signal",
        assetShaderPathSignals,
        assetShaderPathSignals > 0 ? "warn" : "muted",
        "Raw shader-path marker (.fx#) found in the asset strings. Useful for flagging shader-driven assets before any engine-exact bind claim.",
      ),
    ].filter(Boolean),
    counts: {
      baseColor: baseTextureCandidateCount,
      plainImport: plainImportCount,
      specialEffect: nonBaseTargetCount,
      singleImportCandidate: singleImportCandidateCount,
      missingMaterial: missingMaterialCount,
      missingParameterBuffer: missingParameterBufferCount,
      missingTexture: missingTextureCount,
      reflectionRows: reflectionRowCount,
      waterRows: waterRowCount,
      shaderAssetRows: shaderAssetRowCount,
      textureSamplerSignals,
      textureSamplerStateSignals,
      shadowMapSamplerSignals,
      frameBufferSignals,
      targetTextureSignals,
      ccTextureSignals,
      uvScaleBiasSignals,
      yuvSamplerSignals,
      screenVideoSignals,
      assetShaderPathSignals,
      otherUnresolved: otherUnresolvedCount,
    },
  };
}

function buildLoadedToast() {
  const slotSummary = state.currentSlotSummary;
  if (slotSummary?.label === "partial") {
    const missingLinks = (slotSummary.counts?.missingMaterial || 0) + (slotSummary.counts?.missingTexture || 0);
    const details = [
      slotSummary.counts?.baseColor ? `base-color ${slotSummary.counts.baseColor}` : null,
      slotSummary.counts?.plainImport || slotSummary.counts?.specialEffect
        ? `import-link ${(slotSummary.counts.plainImport || 0) + (slotSummary.counts.specialEffect || 0)}`
        : null,
      missingLinks ? `missing links ${missingLinks}` : null,
    ].filter(Boolean);
    return `Loaded. Partial slot bind ${slotSummary.coverage}${details.length ? `; ${details.join(", ")}` : ""}.`;
  }

  if (slotSummary?.label === "none") {
    return "Loaded. No slot binding proved for this map yet.";
  }

  return "Loaded. Use Orbit or Enter Walk.";
}

function selectReasonText(entry, report, slotSummary) {
  if (entry.error) return entry.error;
  if (slotSummary?.label === "partial" || slotSummary?.label === "none") {
    return slotSummary.reason || report?.unresolvedScope || "-";
  }
  return report?.unresolvedScope || slotSummary?.reason || "-";
}

function renderCoverageBreakdown(slotSummary) {
  el.coverageExplain.textContent = slotSummary?.explanation || "-";
  el.coverageBreakdown.innerHTML = (slotSummary?.breakdown || [])
    .map((item) => `
      <div class="breakdownItem" data-tone="${escapeHtml(item.tone)}">
        <div class="breakdownHead">
          <strong>${escapeHtml(item.label)}</strong>
          <span class="breakdownCount">${escapeHtml(item.countText)}</span>
        </div>
        <div class="breakdownNote">${escapeHtml(item.note)}</div>
      </div>
    `)
    .join("");
}

function refreshMaterialEntries() {
  const entriesByKey = new Map();
  if (state.asset) {
    state.asset.updateWorldMatrix(true, true);
    state.asset.traverse((object) => {
      if (!object.isMesh) return;
      const materials = Array.isArray(object.material) ? object.material : [object.material];
      for (const material of materials) {
        if (!material) continue;
        let entry = entriesByKey.get(material.uuid);
        if (!entry) {
          entry = {
            key: material.uuid,
            material,
            meshCount: 0,
            meshNames: new Set(),
          };
          entriesByKey.set(material.uuid, entry);
        }
        entry.meshCount += 1;
        entry.meshNames.add(object.name || object.type || "Mesh");
      }
    });
  }

  state.materialEntries = [...entriesByKey.values()]
    .map((entry, index) => ({
      ...entry,
      label: `${entry.material.name || entry.material.type || "Material"} [${index + 1}]`,
      meshNames: [...entry.meshNames].sort(),
    }))
    .sort((left, right) => left.label.localeCompare(right.label));

  if (!state.materialEntries.some((entry) => entry.key === state.selectedMaterialKey)) {
    state.selectedMaterialKey = state.materialEntries[0]?.key || "";
  }

  renderMaterialDebug();
}

function getSelectedMaterialEntry() {
  return state.materialEntries.find((entry) => entry.key === state.selectedMaterialKey) || state.materialEntries[0] || null;
}

function renderMaterialDebug() {
  if (!el.materialSelect || !el.materialDebugStatus || !el.materialFacts || !el.debugTexturePanels) return;

  if (el.exportSpectorCapture) {
    el.exportSpectorCapture.disabled = !state.lastSpectorCapture;
  }

  if (state.materialEntries.length <= 0) {
    el.materialSelect.innerHTML = '<option value="">No material loaded</option>';
    el.materialSelect.disabled = true;
    el.materialDebugStatus.textContent = state.asset
      ? "This scene has no mesh material to inspect."
      : "Load a map to inspect the selected material surface.";
    el.materialFacts.innerHTML = "";
    el.debugTexturePanels.innerHTML = `
      <article class="debugPanelCard">
        <div class="debugPanelHead">
          <strong>Scene frame preview</strong>
          <span class="debugPanelMeta">viewer debug only</span>
        </div>
        <div class="debugPanelPreview" data-frame-preview="1"></div>
        <ul class="debugPanelList">
          <li>Current viewer frame, not a proved engine render target.</li>
          <li>Useful for quick sanity checks while we grow the debug lane.</li>
        </ul>
      </article>
    `;
    hydrateDebugTexturePanels(null, []);
    return;
  }

  const selectedEntry = getSelectedMaterialEntry();
  if (!selectedEntry) return;

  el.materialSelect.disabled = false;
  el.materialSelect.innerHTML = state.materialEntries
    .map((entry) => `<option value="${escapeHtml(entry.key)}"${entry.key === selectedEntry.key ? " selected" : ""}>${escapeHtml(entry.label)}</option>`)
    .join("");

  const slotInfos = DEBUG_TEXTURE_SLOTS
    .map((slot) => buildTextureSlotInfo(selectedEntry.material, slot))
    .filter(Boolean);

  const material = selectedEntry.material;
  const visibleTextureCount = slotInfos.filter((slotInfo) => slotInfo.texture).length;
  el.materialDebugStatus.textContent =
    `Selected material debug preview: ${selectedEntry.label}. `
    + "These panels reflect the current Three.js material state, not an engine-exact sampler proof.";
  el.materialFacts.innerHTML = [
    `type ${material.type || "-"}`,
    `meshes ${selectedEntry.meshCount}`,
    `textures ${visibleTextureCount}`,
    `side ${formatSide(material.side)}`,
    `transparent ${formatBoolean(material.transparent)}`,
    `vertexColors ${formatBoolean(material.vertexColors)}`,
  ]
    .map((text) => `<span class="debugChip">${escapeHtml(text)}</span>`)
    .join("");

  el.debugTexturePanels.innerHTML = buildDebugTexturePanelsHtml(selectedEntry, slotInfos);
  hydrateDebugTexturePanels(selectedEntry, slotInfos);
}

function buildTextureSlotInfo(material, slot) {
  const texture = material?.[slot];
  if (!texture) return null;
  const image = texture.image ?? texture.source?.data ?? null;
  const width = image?.width ?? image?.videoWidth ?? null;
  const height = image?.height ?? image?.videoHeight ?? null;
  const previewBlockedReason = Array.isArray(image)
    ? "Cube-face array preview omitted in this debug pass."
    : texture.isCubeTexture
      ? "Cube texture preview omitted in this debug pass."
      : !image
        ? "Texture has metadata but no direct image payload on this surface."
        : null;

  return {
    slot,
    texture,
    image,
    width,
    height,
    sourceLabel: image?.currentSrc || image?.src || texture.name || "inline/embedded",
    colorSpace: texture.colorSpace || "NoColorSpace",
    channel: Number.isFinite(texture.channel) ? texture.channel : null,
    mapping: texture.mapping ?? null,
    flipY: Boolean(texture.flipY),
    previewBlockedReason,
  };
}

function buildDebugTexturePanelsHtml(selectedEntry, slotInfos) {
  const cards = [
    `
      <article class="debugPanelCard">
        <div class="debugPanelHead">
          <strong>Scene frame preview</strong>
          <span class="debugPanelMeta">${escapeHtml(state.currentEntry?.area || "-")}</span>
        </div>
        <div class="debugPanelPreview" data-frame-preview="1"></div>
        <ul class="debugPanelList">
          <li>Current viewer frame only.</li>
          <li>Useful as a quick debug surface beside Spector captures.</li>
        </ul>
      </article>
    `,
  ];

  if (slotInfos.length <= 0) {
    cards.push(`
      <article class="debugPanelCard">
        <div class="debugPanelHead">
          <strong>No texture slots on selected material</strong>
          <span class="debugPanelMeta">${escapeHtml(selectedEntry.material.type || "-")}</span>
        </div>
        <div class="debugPanelPreview">
          <div class="debugPanelEmpty">No texture-backed slot was found on this material.</div>
        </div>
      </article>
    `);
    return cards.join("");
  }

  for (const [index, slotInfo] of slotInfos.entries()) {
    const facts = [
      slotInfo.width && slotInfo.height ? `size ${slotInfo.width}x${slotInfo.height}` : "size unknown",
      `color ${slotInfo.colorSpace}`,
      `flipY ${formatBoolean(slotInfo.flipY)}`,
      slotInfo.channel === null ? null : `channel ${slotInfo.channel}`,
      slotInfo.mapping === null ? null : `mapping ${slotInfo.mapping}`,
    ].filter(Boolean);

    cards.push(`
      <article class="debugPanelCard">
        <div class="debugPanelHead">
          <strong>${escapeHtml(slotInfo.slot)}</strong>
          <span class="debugPanelMeta">${escapeHtml(slotInfo.texture.name || slotInfo.texture.uuid)}</span>
        </div>
        <div class="debugPanelPreview" data-debug-slot-index="${index}"></div>
        <div class="debugChipRow">${facts.map((fact) => `<span class="debugChip">${escapeHtml(fact)}</span>`).join("")}</div>
        <ul class="debugPanelList">
          <li>${escapeHtml(slotInfo.sourceLabel)}</li>
          <li>${escapeHtml(slotInfo.previewBlockedReason || "2D preview rendered from the current texture payload when available.")}</li>
        </ul>
      </article>
    `);
  }

  return cards.join("");
}

function hydrateDebugTexturePanels(selectedEntry, slotInfos) {
  const frameHost = el.debugTexturePanels?.querySelector("[data-frame-preview='1']");
  if (frameHost) {
    frameHost.innerHTML = "";
    const frameCanvas = updateFramePreview(true);
    if (frameCanvas) {
      frameHost.appendChild(frameCanvas);
    } else {
      frameHost.appendChild(buildEmptyPreview("Frame preview unavailable."));
    }
  }

  if (!selectedEntry) return;
  for (const [index, slotInfo] of slotInfos.entries()) {
    const host = el.debugTexturePanels?.querySelector(`[data-debug-slot-index='${index}']`);
    if (!host) continue;
    host.innerHTML = "";
    host.appendChild(buildTexturePreviewNode(slotInfo));
  }
}

function buildTexturePreviewNode(slotInfo) {
  if (!slotInfo?.texture) {
    return buildEmptyPreview("Texture preview unavailable.");
  }
  if (slotInfo.previewBlockedReason) {
    return buildEmptyPreview(slotInfo.previewBlockedReason);
  }

  const source = slotInfo.image;
  if (!source) {
    return buildEmptyPreview("Texture source payload unavailable.");
  }

  try {
    if (source.currentSrc || source.src) {
      const image = document.createElement("img");
      image.src = source.currentSrc || source.src;
      image.alt = `${slotInfo.slot} preview`;
      return image;
    }

    const sourceWidth = Math.max(1, slotInfo.width || 1);
    const sourceHeight = Math.max(1, slotInfo.height || 1);
    const scale = Math.min(1, 220 / Math.max(sourceWidth, sourceHeight));
    const canvas = document.createElement("canvas");
    canvas.width = Math.max(1, Math.round(sourceWidth * scale));
    canvas.height = Math.max(1, Math.round(sourceHeight * scale));
    const context = canvas.getContext("2d");
    if (!context) {
      return buildEmptyPreview("2D preview context unavailable.");
    }

    if (typeof ImageData !== "undefined" && source instanceof ImageData) {
      context.putImageData(source, 0, 0);
    } else {
      context.drawImage(source, 0, 0, canvas.width, canvas.height);
    }
    if (isPreviewCanvasEffectivelyBlank(canvas, context)) {
      return buildEmptyPreview("Texture preview unavailable on this source surface.");
    }
    return canvas;
  } catch (error) {
    return buildEmptyPreview(`Texture preview failed: ${error.message}`);
  }
}

function buildEmptyPreview(text) {
  const node = document.createElement("div");
  node.className = "debugPanelEmpty";
  node.textContent = text;
  return node;
}

function updateFramePreview(force = false) {
  const now = performance.now();
  if (!force && now - state.lastFramePreviewAt < 500) {
    return state.framePreviewCanvas;
  }

  const sourceCanvas = renderer.domElement;
  if (!sourceCanvas?.width || !sourceCanvas?.height) {
    return null;
  }

  const width = 256;
  const height = Math.max(1, Math.round(width * (sourceCanvas.height / sourceCanvas.width)));
  state.framePreviewCanvas.width = width;
  state.framePreviewCanvas.height = height;
  const context = state.framePreviewCanvas.getContext("2d");
  if (!context) {
    return null;
  }

  context.clearRect(0, 0, width, height);
  context.drawImage(sourceCanvas, 0, 0, width, height);
  if (isPreviewCanvasEffectivelyBlank(state.framePreviewCanvas, context)) {
    return null;
  }
  state.lastFramePreviewAt = now;
  return state.framePreviewCanvas;
}

function isPreviewCanvasEffectivelyBlank(canvas, context) {
  try {
    const { data } = context.getImageData(0, 0, canvas.width, canvas.height);
    let brightSamples = 0;
    for (let i = 0; i < data.length; i += 64) {
      const value = data[i] + data[i + 1] + data[i + 2];
      if (value > 24) {
        brightSamples += 1;
        if (brightSamples >= 8) {
          return false;
        }
      }
    }
    return true;
  } catch {
    return false;
  }
}

async function captureSpectorFrame() {
  const spector = await openSpectorUi();
  if (typeof spector.captureNextFrame === "function") {
    spector.captureNextFrame(renderer.domElement);
  } else {
    spector.captureCanvas(renderer.domElement);
  }
  setDebugStatus("Capturing next frame...");
}

function exportLastSpectorCapture() {
  if (!state.lastSpectorCapture) {
    setDebugStatus("No Spector capture available to export.");
    return;
  }

  const areaToken = (state.currentEntry?.area || "map")
    .replaceAll("/", "-")
    .replaceAll("\\", "-")
    .replace(/[^a-zA-Z0-9._-]/g, "-");
  const captureTime = new Date(state.lastSpectorCapture.startTime || Date.now()).toISOString().replaceAll(":", "-");
  const fileName = `${areaToken}.spector.${captureTime}.json`;
  const blob = new Blob([JSON.stringify(state.lastSpectorCapture, null, 2)], { type: "application/json" });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  link.click();
  URL.revokeObjectURL(url);
  setDebugStatus(`Exported last capture: ${fileName}`);
}

function updateGeometryStats() {
  let meshes = 0;
  let vertices = 0;
  let primitives = 0;
  const box = new THREE.Box3();
  state.asset.updateWorldMatrix(true, true);
  state.asset.traverse((object) => {
    if (!object.isMesh) return;
    meshes += 1;
    primitives += object.geometry?.groups?.length || 1;
    vertices += object.geometry?.attributes?.position?.count || 0;
    box.expandByObject(object);
  });
  state.bounds = box;
  el.meshCount.textContent = meshes.toLocaleString();
  el.vertexCount.textContent = vertices.toLocaleString();
  el.primitiveCount.textContent = primitives.toLocaleString();
  el.boundsText.textContent = `${formatVec(box.min)} -> ${formatVec(box.max)}`;
}

function setMode(mode) {
  state.mode = mode;
  orbitControls.enabled = mode === "orbit";
  el.orbitMode.setAttribute("aria-pressed", String(mode === "orbit"));
  el.walkMode.setAttribute("aria-pressed", String(mode === "walk"));
  if (mode === "orbit" && walkControls.isLocked) {
    walkControls.unlock();
  }
  setToast(mode === "walk" ? "Walk selected. Click Enter Walk for mouse look." : "Orbit selected.");
}

function fitCamera() {
  if (!state.bounds) return;
  setMode("orbit");
  const size = state.bounds.getSize(new THREE.Vector3());
  const center = state.bounds.getCenter(new THREE.Vector3());
  const radius = Math.max(size.x, size.y, size.z) || 1;
  camera.position.copy(center).add(new THREE.Vector3(radius * 1.3, radius * 0.7, radius * 1.25));
  camera.near = Math.max(0.001, radius / 1000);
  camera.far = Math.max(100, radius * 100);
  camera.updateProjectionMatrix();
  orbitControls.target.copy(center);
  orbitControls.update();
}

function insideCamera() {
  if (!state.bounds) return;
  const center = state.bounds.getCenter(new THREE.Vector3());
  camera.position.set(center.x, center.y + 0.25, center.z + 0.1);
  camera.rotation.set(0, 0, 0);
  setMode("walk");
  setToast("Inside camera set. Click Enter Walk for mouse look.");
}

function topCamera() {
  if (!state.bounds) return;
  setMode("orbit");
  const center = state.bounds.getCenter(new THREE.Vector3());
  const size = state.bounds.getSize(new THREE.Vector3());
  const radius = Math.max(size.x, size.z) || 1;
  camera.position.set(center.x, center.y + radius * 2.2, center.z + 0.01);
  orbitControls.target.copy(center);
  orbitControls.update();
}

function resetCamera() {
  camera.position.set(4, 2.4, 5);
  orbitControls.target.set(0, 0, 0);
  orbitControls.update();
  setMode("orbit");
}

// Render gate API for headless validation (Playwright)
window.__renderGateSetCamera = function(pos, target) {
  setMode("orbit");
  camera.position.set(pos[0], pos[1], pos[2]);
  if (target) {
    orbitControls.target.set(target[0], target[1], target[2]);
  } else {
    const center = state.bounds ? state.bounds.getCenter(new THREE.Vector3()) : new THREE.Vector3(0, 0, 0);
    orbitControls.target.copy(center);
  }
  orbitControls.update();
};

function applyWireframe() {
  if (!state.asset) return;
  state.asset.traverse((object) => {
    if (!object.isMesh) return;
    const materials = Array.isArray(object.material) ? object.material : [object.material];
    for (const material of materials) {
      material.wireframe = state.wireframe;
      material.needsUpdate = true;
    }
  });
}

function updateWalk(deltaSeconds) {
  if (state.mode !== "walk") return;
  const speed = state.speed * deltaSeconds;
  const forward = Number(state.keys.has("KeyW")) - Number(state.keys.has("KeyS"));
  const right = Number(state.keys.has("KeyD")) - Number(state.keys.has("KeyA"));
  const up = Number(state.keys.has("KeyE")) - Number(state.keys.has("KeyQ"));
  if (forward) walkControls.moveForward(forward * speed);
  if (right) walkControls.moveRight(right * speed);
  if (up) camera.position.y += up * speed;
}

function animate(now = performance.now()) {
  requestAnimationFrame(animate);
  const deltaSeconds = Math.min(0.05, (now - state.lastTime) / 1000);
  state.lastTime = now;
  orbitControls.update();
  updateWalk(deltaSeconds);
  selection?.refreshHelper();
  renderer.render(scene, camera);
  updateFramePreview();
}

function resize() {
  const rect = el.viewport.getBoundingClientRect();
  const width = Math.max(1, Math.floor(rect.width));
  const height = Math.max(1, Math.floor(rect.height));
  renderer.setSize(width, height, false);
  camera.aspect = width / height;
  camera.updateProjectionMatrix();
}

async function fetchJson(urlLike) {
  const url = new URL(urlLike, window.location.href);
  const response = await fetch(url.href, { cache: "no-store" });
  if (!response.ok) throw new Error(`Failed to load ${url.pathname}: ${response.status}`);
  return response.json();
}

async function fetchJsonOptional(urlLike) {
  if (!urlLike) return null;
  try {
    return await fetchJson(urlLike);
  } catch {
    return null;
  }
}

function setToast(text) {
  el.toast.textContent = text;
}

function setDebugStatus(text) {
  if (el.debugStatus) el.debugStatus.textContent = text;
}

async function openSpectorUi() {
  if (!window.__ffxSpectorLoaded) {
    // Spector.js 0.9.30 (BabylonJS/Spector.js, MIT; vendored 2026-08-21).  The
    // release viewer intentionally has no CDN fallback: missing/tampered vendor bytes must fail
    // locally instead of silently expanding the WebView trust boundary to the public internet.
    await loadScript("./vendor/spector/spector.bundle.js");
    window.__ffxSpectorLoaded = true;
  }

  if (!window.__ffxSpectorInstance) {
    const spector = new window.SPECTOR.Spector();
    spector.displayUI();
    spector.spyCanvases();
    spector.onCaptureStarted?.add(() => {
      setDebugStatus("Capturing next frame...");
    });
    spector.onCapture?.add((capture) => {
      state.lastSpectorCapture = capture;
      window.ffxMapViewerDebug.lastSpectorCapture = capture;
      renderMaterialDebug();
      const commandCount = capture?.commands?.length ?? 0;
      setDebugStatus(`GPU capture ready (${commandCount.toLocaleString()} commands)`);
    });
    window.__ffxSpectorInstance = spector;
    window.ffxMapViewerDebug.spector = spector;
  }

  setDebugStatus(state.lastSpectorCapture ? "GPU capture UI on; last capture ready" : "GPU capture UI on");
  return window.__ffxSpectorInstance;
}

function loadScript(src) {
  return new Promise((resolve, reject) => {
    const script = document.createElement("script");
    script.src = src;
    script.onload = () => resolve();
    script.onerror = () => reject(new Error(`Script failed: ${src}`));
    document.head.appendChild(script);
  });
}

function countRowsBy(rows, selector) {
  const counts = Object.create(null);
  for (const row of rows) {
    const key = selector(row);
    if (!key) continue;
    counts[key] = (counts[key] || 0) + 1;
  }
  return counts;
}

function createBreakdownItem(label, count, tone, note, options = {}) {
  if (!Number.isFinite(count)) return null;
  if (count <= 0 && !options.showWhenZero) return null;
  return {
    label,
    count,
    countText: count.toLocaleString(),
    tone,
    note,
  };
}

function buildCoverageExplanation({
  total,
  bound,
  baseColorCount,
  plainImportCount,
  specialEffectCount,
  singleImportCandidateCount,
  missingMaterialCount,
  missingParameterBufferCount,
  missingTextureCount,
  reflectionRowCount,
  waterRowCount,
  shaderAssetRowCount,
  textureSamplerSignals,
  textureSamplerStateSignals,
  shadowMapSamplerSignals,
  frameBufferSignals,
  targetTextureSignals,
  ccTextureSignals,
  uvScaleBiasSignals,
  yuvSamplerSignals,
  screenVideoSignals,
  assetShaderPathSignals,
  otherUnresolvedCount,
  topBlockedFamiliesText,
}) {
  const routeParts = [];
  if (baseColorCount > 0) {
    routeParts.push(`${baseColorCount.toLocaleString()} base-color candidate`);
  }
  if (plainImportCount + specialEffectCount > 0) {
    const importLinkCount = singleImportCandidateCount || (plainImportCount + specialEffectCount);
    let importText = `${importLinkCount.toLocaleString()} import-link candidate`;
    if (specialEffectCount > 0 && specialEffectCount === importLinkCount) {
      importText += " (all via non-base target blocks)";
    } else if (specialEffectCount > 0) {
      importText += ` (${specialEffectCount.toLocaleString()} via non-base target blocks)`;
    }
    routeParts.push(importText);
  }

  const blockerParts = [];
  if (missingMaterialCount > 0) {
    blockerParts.push(`${missingMaterialCount.toLocaleString()} missing material links`);
  }
  if (missingParameterBufferCount > 0) {
    blockerParts.push(`${missingParameterBufferCount.toLocaleString()} missing parameter-buffer links`);
  }
  if (missingTextureCount > 0) {
    blockerParts.push(`${missingTextureCount.toLocaleString()} missing texture links`);
  }
  if (otherUnresolvedCount > 0) {
    blockerParts.push(`${otherUnresolvedCount.toLocaleString()} other unresolved paths`);
  }
  const signalParts = [];
  if (reflectionRowCount > 0) {
    signalParts.push(`${reflectionRowCount.toLocaleString()} reflection-path row`);
  }
  if (waterRowCount > 0) {
    signalParts.push(`${waterRowCount.toLocaleString()} water-path row`);
  }
  if (shaderAssetRowCount > 0) {
    signalParts.push(`${shaderAssetRowCount.toLocaleString()} shader-asset row`);
  }
  if (textureSamplerSignals > 0) {
    signalParts.push(`TextureSampler x${textureSamplerSignals.toLocaleString()}`);
  }
  if (textureSamplerStateSignals + shadowMapSamplerSignals > 0) {
    signalParts.push(`sampler-state/shadow x${(textureSamplerStateSignals + shadowMapSamplerSignals).toLocaleString()}`);
  }
  if (frameBufferSignals + targetTextureSignals + ccTextureSignals + uvScaleBiasSignals > 0) {
    signalParts.push(`framebuffer/post x${(frameBufferSignals + targetTextureSignals + ccTextureSignals + uvScaleBiasSignals).toLocaleString()}`);
  }
  if (yuvSamplerSignals + screenVideoSignals > 0) {
    signalParts.push(`YUV/video x${(yuvSamplerSignals + screenVideoSignals).toLocaleString()}`);
  }
  if (assetShaderPathSignals > 0) {
    signalParts.push(`shader-path x${assetShaderPathSignals.toLocaleString()}`);
  }

  if (Number.isFinite(total) && total > 0 && bound >= total) {
    if (routeParts.length === 1) {
      return `All analyzed rows resolved through the current ${routeParts[0]} route.`;
    }
    return routeParts.length > 1
      ? `All analyzed rows resolved, but they still split across multiple evidence routes: ${routeParts.join(", ")}.`
      : "All analyzed rows resolved in the current structural candidate report.";
  }

  if (bound <= 0) {
    return blockerParts.length > 0
      ? `No slot coverage was proved here. The current report is dominated by ${blockerParts.join(", ")}.`
      : "No slot coverage was proved here yet.";
  }

  const sentenceParts = [];
  if (routeParts.length > 0) {
    sentenceParts.push(`Resolved rows split into ${routeParts.join(", ")}`);
  }
  if (blockerParts.length > 0) {
    sentenceParts.push(`the unresolved side is mostly ${blockerParts.join(", ")}`);
  }
  if (sentenceParts.length === 0) {
    return "Coverage is partial, but the current report does not explain the missing side more specifically.";
  }
  const signalSuffix = signalParts.length > 0 ? ` Special-path signals: ${signalParts.join(", ")}.` : "";
  const suffix = topBlockedFamiliesText ? ` Top blocked families: ${topBlockedFamiliesText}.` : "";
  return `${sentenceParts.join("; ")}.${suffix}${signalSuffix}`;
}

function describeImportLinkCandidate(singleImportCount, specialEffectCount) {
  if (singleImportCount <= 0) {
    return "No single DDS import candidate resolved for this entry.";
  }
  if (specialEffectCount > 0 && specialEffectCount === singleImportCount) {
    return "Single DDS import resolved, but every bound row currently routes through non-base target blocks such as animation/effect-style paths.";
  }
  if (specialEffectCount > 0) {
    return `Single DDS import resolved; ${specialEffectCount.toLocaleString()} of these rows currently route through non-base target blocks such as animation/effect-style paths.`;
  }
  return "Single DDS import resolved, but the sampler role is not proved as the base-color lane.";
}

function summarizeTopBlockedFamilies(statusBreakdown) {
  const blocked = (statusBreakdown || [])
    .filter((item) => item.statusFamily && !item.statusFamily.startsWith("bound_") && item.count > 0)
    .sort((left, right) => right.count - left.count)
    .slice(0, 2);
  if (blocked.length === 0) {
    return "";
  }
  return blocked
    .map((item) => `${formatStatusFamilyLabel(item.statusFamily)} ${item.count.toLocaleString()}`)
    .join(", ");
}

function formatStatusFamilyLabel(statusFamily) {
  switch (statusFamily) {
    case "missing_mesh":
      return "missing mesh";
    case "missing_material_link":
      return "missing material link";
    case "missing_parameter_buffer_link":
      return "missing parameter-buffer link";
    case "missing_texture_link":
      return "missing texture link";
    case "multiple_texture_links":
      return "multiple texture links";
    case "missing_shared_data_id":
      return "missing shared-data id";
    case "special_or_wrong_texture_type":
      return "special/wrong texture type";
    case "unmapped_texture_reference":
      return "unmapped texture reference";
    case "decode_issue":
      return "decode issue";
    case "other_blocked":
      return "other blocked";
    default:
      return statusFamily.replaceAll("_", " ");
  }
}

function formatVec(vector) {
  return [vector.x, vector.y, vector.z].map((value) => value.toFixed(2)).join(", ");
}

function formatBytes(bytes) {
  if (!Number.isFinite(bytes)) return "-";
  const units = ["B", "KB", "MB", "GB"];
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }
  return `${value.toFixed(unit === 0 ? 0 : 1)} ${units[unit]}`;
}

function formatPercent(value) {
  if (!Number.isFinite(value)) return "-";
  return `${(value * 100).toFixed(1)}%`;
}

function formatBoolean(value) {
  return value ? "yes" : "no";
}

function formatSide(side) {
  switch (side) {
    case THREE.FrontSide:
      return "FrontSide";
    case THREE.BackSide:
      return "BackSide";
    case THREE.DoubleSide:
      return "DoubleSide";
    default:
      return String(side ?? "-");
  }
}

function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

window.ffxMapViewerDebug.captureSpectorFrame = captureSpectorFrame;
window.ffxMapViewerDebug.exportLastSpectorCapture = exportLastSpectorCapture;
window.ffxMapViewerDebug.refreshMaterialEntries = refreshMaterialEntries;
