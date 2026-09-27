// Aurora Field Explorer — additive MapViewer overlay (overworld mode).
// Activates with ?fieldExplorer=1 and optional ?encounters=./path/field-encounters.json
// Shows btl.bin encounter groups, mapout.vpa danger zones, and Field Scout chest markers.

import * as THREE from "three";
import { GLTFLoader } from "three/addons/loaders/GLTFLoader.js";

const chrGltfLoader = new GLTFLoader();
const chrModelCache = new Map();
const CHR_ANIM_FOLDER = {
  n: "npc_anim",
  c: "pc_anim",
  f: "obj_anim",
  m: "phyre_chr_anim",
  s: "summon_anim",
  w: "wep_anim",
};

const params = new URLSearchParams(window.location.search);
const encountersUrl = params.get("encounters");

/** @type {Promise<{ ok: boolean; encountersUrl?: string; error?: string; skipped?: boolean }>} */
window.__fieldExplorerReady = new Promise((resolve) => {
  window.__fieldExplorerReadyResolve = resolve;
});

if (params.get("fieldExplorer") === "1" && encountersUrl) {
  initFieldExplorerOverlay(encountersUrl)
    .then(() => window.__fieldExplorerReadyResolve?.({ ok: true, encountersUrl }))
    .catch((err) => {
      window.__fieldExplorerReadyResolve?.({ ok: false, error: err?.message || String(err) });
      console.warn("field-explorer:", err?.message || err);
    });
} else {
  window.__fieldExplorerReadyResolve?.({ ok: false, skipped: true });
}

async function initFieldExplorerOverlay(url) {
  const res = await fetch(url, { cache: "no-store" });
  if (!res.ok) throw new Error(`encounters HTTP ${res.status}`);
  const data = await res.json();

  const panel = document.createElement("div");
  panel.id = "fieldExplorerPanel";
  panel.style.cssText =
    "position:fixed;top:12px;right:12px;z-index:9999;max-width:360px;max-height:70vh;" +
    "overflow:auto;background:rgba(12,24,18,0.92);color:#dcefe4;border:1px solid #3a6b55;" +
    "border-radius:8px;padding:12px 14px;font:13px/1.45 system-ui,sans-serif;" +
    "box-shadow:0 8px 28px rgba(0,0,0,0.45);touch-action:none;";

  const groups = Array.isArray(data.groups) ? data.groups : [];
  const exact = groups.filter((g) => g.scope === "exact");
  const related = groups.filter((g) => g.scope === "area_related");
  const zones = Array.isArray(data.zones) ? data.zones : [];
  const chests = Array.isArray(data.chests) ? data.chests : [];
  const triggers = Array.isArray(data.triggers) ? data.triggers : [];
  const sceneNodes = Array.isArray(data.sceneNodes) ? data.sceneNodes : [];
  const entities = Array.isArray(data.entities)
    ? data.entities
    : Array.isArray(data.npcs)
      ? data.npcs.map((n) => ({
          ...n,
          chrCategory: n.chrCategory || "story_npc",
          layer: n.layer || n.chrCategory || "story_npc",
          defaultVisible: n.defaultVisible !== false,
        }))
      : [];

  const layerState = {
    story_npc: true,
    party: true,
    field_enemy: false,
    field_prop: true,
    summon: false,
    weapon: false,
    unknown: false,
    rig: false,
    scene_nodes: sceneNodes.length > 0,
  };

  for (const e of entities) {
    const cat = e.chrCategory || e.layer || "unknown";
    if (e.defaultVisible === false && layerState[cat] === true && cat !== "story_npc" && cat !== "party") {
      layerState[cat] = false;
    }
  }

  function visibleEntities() {
    return entities.filter((e) => layerState[e.chrCategory || e.layer || "unknown"]);
  }

  function renderPanelBody() {
    const chrVisible = visibleEntities();
    const counts = data.chrCounts || {};

    let html =
      `<div id="fieldExplorerDragHandle" title="Arraste para mover · duplo-clique = reset" style="cursor:grab;user-select:none;-webkit-user-select:none;display:flex;align-items:center;gap:8px;margin:-12px -14px 8px;padding:10px 14px;border-bottom:1px solid rgba(58,107,85,0.55);background:rgba(127,212,168,0.08);border-radius:8px 8px 0 0">` +
      `<span style="opacity:0.75;font-size:16px;line-height:1" aria-hidden="true">⠿</span>` +
      `<span style="font-weight:700;color:#7fd4a8;flex:1">🗺️ Field Explorer</span>` +
      `<span style="font-size:10px;color:#7fd4a8;opacity:0.65;letter-spacing:0.04em">ARRASTE</span>` +
      `</div>`;
    html += `<div style="color:#a8c9b8;margin-bottom:8px">${escapeHtml(data.mapEntity || "")}</div>`;
    html += `<div style="font-size:11px;color:#8aa;font-style:italic;margin-bottom:10px">${escapeHtml(data.honesty || "")}</div>`;

    if (data.suggestedArenaScene) {
      html += `<div style="margin-bottom:8px;color:#c9e6d4">Arena sugerida: <b>${escapeHtml(data.suggestedArenaScene)}</b></div>`;
    }

    if (zones.length) {
      html += `<div style="margin-bottom:8px;color:#9fd">Zonas de encontro (${zones.length})</div>`;
      html += `<div style="font-size:11px;color:#aac;margin-bottom:8px">Polígonos no chão = danger zones (mapout.vpa).</div>`;
    } else if (data.zoneMeta?.status) {
      html += `<div style="margin-bottom:8px;color:#f0c987">Zonas: ${escapeHtml(data.zoneMeta.status)}</div>`;
    }

    if (chests.length) {
      html += `<div style="margin-bottom:8px;color:#f0d080">📦 Baús (${chests.length})</div>`;
      html += `<div style="font-size:11px;color:#aac;margin-bottom:8px">Marcadores dourados = Field Scout walk (bauro/chest).</div>`;
      html += renderChestSection(chests);
    } else if (data.chestMeta?.note) {
      html += `<div style="margin-bottom:8px;color:#f0c987;font-size:11px">${escapeHtml(data.chestMeta.note)}</div>`;
    }

    if (entities.length) {
      const raw = counts.rawTotal ?? data.npcMeta?.rawCount ?? entities.length;
      const dedup = counts.dedupedTotal ?? data.npcMeta?.dedupedCount ?? entities.length;
      html += `<div style="margin-bottom:8px;color:#80c8ff">👤 CHR walk (${chrVisible.length}/${dedup} visíveis)</div>`;
      html += `<div style="font-size:11px;color:#aac;margin-bottom:6px">Instâncias CHR deduplicadas — não é roster de NPC. Bruto: ${raw} amostras.</div>`;
      html += renderLayerToggles(layerState);
      html += renderEntitySection(chrVisible);
    } else if (data.npcMeta?.note) {
      html += `<div style="margin-bottom:8px;color:#8ac4ff;font-size:11px">${escapeHtml(data.npcMeta.note)}</div>`;
    }

    if (triggers.length) {
      html += `<div style="margin-bottom:8px;color:#7fe8e0">⚡ Triggers (${triggers.length})</div>`;
      html += renderMarkerListSection(triggers, "trigger");
    } else if (data.triggerMeta?.note) {
      html += `<div style="margin-bottom:8px;color:#7fe8e0;font-size:11px">${escapeHtml(data.triggerMeta.note)}</div>`;
    }

    if (sceneNodes.length) {
      const vis = layerState.scene_nodes ? sceneNodes.length : 0;
      html += `<div style="margin-bottom:8px;color:#e0a0ff">🏠 Scene nodes (${vis}/${sceneNodes.length})</div>`;
      html += `<div style="font-size:11px;color:#aac;margin-bottom:6px">PNode Phyre colocados — proxies até export glTF com instâncias.</div>`;
      html += renderSceneNodeToggle(layerState);
      html += renderMarkerListSection(layerState.scene_nodes ? sceneNodes : [], "scene");
    } else if (data.sceneNodeMeta?.note) {
      html += `<div style="margin-bottom:8px;color:#e0a0ff;font-size:11px">${escapeHtml(data.sceneNodeMeta.note)}</div>`;
    }

    html += renderGroupSection("Grupos deste field", exact);
    if (related.length) html += renderGroupSection("Mesma área (outros NN)", related);

    if (!groups.length) {
      html += `<div style="color:#f0c987">Nenhum grupo btl.bin para este field.</div>`;
    }

    return html;
  }

  panel.innerHTML = renderPanelBody();
  document.body.appendChild(panel);
  makePanelDraggable(panel);

  panel.addEventListener("change", (ev) => {
    const t = ev.target;
    if (!(t instanceof HTMLInputElement) || t.type !== "checkbox" || !t.dataset.layer) return;
    layerState[t.dataset.layer] = t.checked;
    remountShapes();
    const body = renderPanelBody();
    const handle = panel.querySelector("#fieldExplorerDragHandle");
    panel.innerHTML = body;
    if (handle) makePanelDraggable(panel);
  });

  function remountShapes() {
    const visScene = layerState.scene_nodes ? sceneNodes : [];
    mountFieldOverlayShapes(zones, data.zoneMeta, chests, visibleEntities(), triggers, visScene);
  }

  remountShapes();
}

function mountFieldOverlayShapes(zones, zoneMeta, chests, entities, triggers, sceneNodes = []) {
  let tries = 0;
  const tick = () => {
    const dbg = window.ffxMapViewerDebug;
    if (dbg?.THREE && dbg?.scene) {
      if (zones.length) drawZoneShapes(dbg, zones, zoneMeta);
      if (chests.length) drawPointMarkers(dbg, chests, "fieldExplorerChests", 0xf0c040, "box");
      if (entities.length) drawEntityMarkers(dbg, entities);
      else {
        const oldNpcs = dbg.scene.getObjectByName("fieldExplorerNpcs");
        if (oldNpcs) dbg.scene.remove(oldNpcs);
      }
      drawSceneNodeMarkers(dbg, sceneNodes);
      if (triggers.length) drawPointMarkers(dbg, triggers, "fieldExplorerTriggers", 0x40e8d0, "cone");
      return;
    }
    if (++tries < 80) setTimeout(tick, 200);
  };
  tick();
}

const CHR_LAYER_COLORS = {
  story_npc: 0x60b0ff,
  party: 0xffb060,
  field_enemy: 0xff6060,
  field_prop: 0xa0a0a0,
  summon: 0xc060ff,
  weapon: 0xffff80,
  rig: 0x808080,
  unknown: 0x888888,
};

function drawEntityMarkers(dbg, entities) {
  const { THREE, scene } = dbg;
  const old = scene.getObjectByName("fieldExplorerNpcs");
  if (old) scene.remove(old);

  const group = new THREE.Group();
  group.name = "fieldExplorerNpcs";

  entities.forEach((c) => {
    const cat = c.chrCategory || c.layer || "unknown";
    const color = CHR_LAYER_COLORS[cat] ?? CHR_LAYER_COLORS.unknown;
    if (cat === "field_prop") {
      drawPropBuildingProxy(THREE, group, c, color);
      return;
    }
    if (shouldTryChrModel(c)) {
      loadChrModelAt(c, THREE, group, () => {
        drawChrPinMarker(THREE, group, c, color, cat);
      });
    } else {
      drawChrPinMarker(THREE, group, c, color, cat);
    }
  });

  scene.add(group);
  console.info("field-explorer: drew", entities.length, "CHR entity marker(s)");
}

function shouldTryChrModel(c) {
  const cat = c.chrCategory || c.layer || "unknown";
  if (cat === "party" || cat === "rig" || cat === "weapon") return false;
  const name = String(c.name || "").toLowerCase();
  return name.length >= 4 && CHR_ANIM_FOLDER[name[0]] != null;
}

function resolveChrModelUrls(entity) {
  const urls = [];
  if (entity.modelUrl) urls.push(entity.modelUrl);
  const id = String(entity.name || "").toLowerCase();
  const folder = CHR_ANIM_FOLDER[id[0]];
  if (!folder || id.length < 4) return urls;
  // Models generated by the editor live in the per-user ViewerHub data root. The release server
  // deliberately exposes no repository/work route, so every fallback must remain portable.
  const base = `/viewer-data/model/${folder}/models/${id}`;
  urls.push(`${base}/${id}_static.gltf`, `${base}/${id}_animated.gltf`);
  return [...new Set(urls)];
}

function loadChrModelAt(entity, THREE, group, onFail) {
  const urls = resolveChrModelUrls(entity);
  if (!urls.length) {
    onFail();
    return;
  }
  const cacheKey = urls[0];
  if (chrModelCache.has(cacheKey)) {
    const cached = chrModelCache.get(cacheKey);
    if (cached === null) {
      onFail();
      return;
    }
    group.add(cloneChrRoot(cached, entity, THREE));
    return;
  }
  tryLoadChrUrls(urls, 0, entity, THREE, group, onFail);
}

function tryLoadChrUrls(urls, index, entity, THREE, group, onFail) {
  if (index >= urls.length) {
    chrModelCache.set(urls[0], null);
    onFail();
    return;
  }
  const url = urls[index];
  chrGltfLoader.load(
    url,
    (gltf) => {
      /* Force T-pose: stop all animations at frame 0 */
      if (gltf.animations && gltf.animations.length > 0) {
        const mixer = new THREE.AnimationMixer(gltf.scene);
        const action = mixer.clipAction(gltf.animations[0]);
        action.time = 0;
        action.play();
        mixer.update(0);
        action.stop();
      }
      chrModelCache.set(urls[0], gltf.scene);
      group.add(cloneChrRoot(gltf.scene, entity, THREE));
      console.info("field-explorer: CHR model", entity.name, "from", url);
    },
    undefined,
    () => tryLoadChrUrls(urls, index + 1, entity, THREE, group, onFail)
  );
}

function cloneChrRoot(template, entity, THREE) {
  const root = template.clone(true);
  const x = Number(entity.x ?? 0);
  const y = Number(entity.y ?? 0);
  const z = Number(entity.z ?? 0);
  root.position.set(x, y, z);
  const cat = entity.chrCategory || entity.layer || "unknown";
  const scale = cat === "field_prop" ? 1.0 : 1.0;
  root.scale.setScalar(scale);
  root.name = `fieldExplorerChr_${entity.name || "unknown"}`;
  root.traverse((o) => {
    if (o.isMesh) {
      o.renderOrder = 25;
    }
  });
  return root;
}

function drawChrPinMarker(THREE, group, c, color, cat) {
  const tall = cat === "story_npc" || cat === "field_enemy";
  const shape = cat === "party" ? "box" : "cylinder";
  drawSinglePointMarker(THREE, group, c, color, shape, { tall, label: c.name });
}

function drawPropBuildingProxy(THREE, group, c, color) {
  const x = Number(c.x ?? 0);
  const y = Number(c.y ?? 0);
  const z = Number(c.z ?? 0);
  if (!Number.isFinite(x) || !Number.isFinite(z)) return;

  const w = 5.5;
  const h = 3.2;
  const d = 4.8;
  const baseY = Number.isFinite(y) ? y + h * 0.5 : h * 0.5;

  const mesh = new THREE.Mesh(
    new THREE.BoxGeometry(w, h, d),
    new THREE.MeshBasicMaterial({
      color,
      transparent: true,
      opacity: 0.42,
      depthWrite: false,
    })
  );
  mesh.position.set(x, baseY, z);
  mesh.renderOrder = 21;
  mesh.userData = {
    name: c.name,
    source: c.source,
    chrCategory: "field_prop",
    proxyKind: "building",
  };
  group.add(mesh);

  const roof = new THREE.Mesh(
    new THREE.ConeGeometry(Math.max(w, d) * 0.55, 1.4, 4),
    new THREE.MeshBasicMaterial({
      color: 0xd4c090,
      transparent: true,
      opacity: 0.55,
      depthWrite: false,
    })
  );
  roof.position.set(x, baseY + h * 0.5 + 0.5, z);
  roof.rotation.y = Math.PI / 4;
  roof.renderOrder = 22;
  group.add(roof);
}

function drawSceneNodeMarkers(dbg, sceneNodes) {
  const { THREE, scene } = dbg;
  const old = scene.getObjectByName("fieldExplorerSceneNodes");
  if (old) scene.remove(old);
  if (!sceneNodes.length) return;

  const group = new THREE.Group();
  group.name = "fieldExplorerSceneNodes";

  sceneNodes.forEach((n) => {
    const color = 0xe0a0ff;
    const kind = n.proxyKind || inferPropKind(n.name);
    if (kind === "building") {
      drawPropBuildingProxy(THREE, group, n, 0xc090e0);
    } else {
      drawSinglePointMarker(THREE, group, n, color, "cone");
    }
  });

  scene.add(group);
  console.info("field-explorer: drew", sceneNodes.length, "scene node marker(s)");
}

function inferPropKind(name) {
  const n = String(name || "").toLowerCase();
  if (/^f\d{3}/.test(n)) return "building";
  if (/shop|loja|cabana|hut|house|tent|store|building|mdl|prop|stall|vendor|inn|shack/.test(n)) {
    return "building";
  }
  return "marker";
}

function drawSinglePointMarker(THREE, group, c, color, shape, opts = {}) {
  const { tall = false, label = null } = opts;
  const x = Number(c.x ?? 0);
  const y = Number(c.y ?? 0.15);
  const z = Number(c.z ?? 0);
  if (!Number.isFinite(x) || !Number.isFinite(z)) return;

  const floorY = Number.isFinite(y) ? y : 0.15;
  let geom;
  if (shape === "cylinder") {
    geom = tall
      ? new THREE.CylinderGeometry(0.45, 0.55, 2.0, 14)
      : new THREE.CylinderGeometry(0.18, 0.18, 0.5, 12);
  } else if (shape === "cone") {
    geom = new THREE.ConeGeometry(0.2, 0.45, 12);
  } else {
    geom = new THREE.BoxGeometry(0.35, 0.25, 0.28);
  }

  const meshY = tall ? floorY + 1.0 : floorY;
  const mesh = new THREE.Mesh(
    geom,
    new THREE.MeshBasicMaterial({
      color,
      transparent: true,
      opacity: tall ? 0.72 : 0.88,
      depthWrite: false,
    })
  );
  mesh.position.set(x, meshY, z);
  mesh.renderOrder = 20;
  mesh.userData = {
    name: c.name,
    source: c.source,
    chrId: c.chrId,
    chrCategory: c.chrCategory || c.layer,
    sampleCount: c.sampleCount,
  };
  group.add(mesh);

  const ringY = tall ? floorY + 0.08 : floorY + 0.06;
  const ring = new THREE.Mesh(
    new THREE.RingGeometry(tall ? 0.55 : 0.22, tall ? 0.75 : 0.32, 24),
    new THREE.MeshBasicMaterial({
      color,
      transparent: true,
      opacity: 0.55,
      side: THREE.DoubleSide,
      depthWrite: false,
    })
  );
  ring.rotation.x = -Math.PI / 2;
  ring.position.set(x, ringY, z);
  ring.renderOrder = 19;
  group.add(ring);

  if (label && tall) {
    drawTextSprite(THREE, group, label, x, floorY + 2.25, z, color);
  }
}

function drawTextSprite(THREE, group, text, x, y, z, color) {
  const canvas = document.createElement("canvas");
  canvas.width = 256;
  canvas.height = 64;
  const ctx = canvas.getContext("2d");
  if (!ctx) return;
  ctx.fillStyle = "rgba(8,16,12,0.82)";
  ctx.fillRect(0, 0, canvas.width, canvas.height);
  ctx.strokeStyle = `#${color.toString(16).padStart(6, "0")}`;
  ctx.lineWidth = 3;
  ctx.strokeRect(2, 2, canvas.width - 4, canvas.height - 4);
  ctx.fillStyle = "#dcefe4";
  ctx.font = "bold 28px system-ui,sans-serif";
  ctx.textAlign = "center";
  ctx.textBaseline = "middle";
  ctx.fillText(String(text).slice(0, 12), canvas.width / 2, canvas.height / 2);
  const tex = new THREE.CanvasTexture(canvas);
  const mat = new THREE.SpriteMaterial({ map: tex, transparent: true, depthWrite: false });
  const sprite = new THREE.Sprite(mat);
  sprite.position.set(x, y, z);
  sprite.scale.set(3.2, 0.8, 1);
  sprite.renderOrder = 30;
  group.add(sprite);
}

function drawZoneShapes(dbg, zones, zoneMeta) {
  const { THREE, scene } = dbg;
  const old = scene.getObjectByName("fieldExplorerZones");
  if (old) scene.remove(old);

  const group = new THREE.Group();
  group.name = "fieldExplorerZones";

  const palette = [0x7fd4a8, 0x6ab8ff, 0xffb86a, 0xff7f9a, 0xc9a0ff, 0xf0f987];
  const groundY = 0.05;

  zones.forEach((z, idx) => {
    const poly = Array.isArray(z.polygon) ? z.polygon : [];
    if (poly.length < 3) return;

    const color = palette[(z.groupIndex ?? idx) % palette.length];
    const shape = new THREE.Shape();
    shape.moveTo(poly[0][0], poly[0][1]);
    for (let i = 1; i < poly.length; i++) shape.lineTo(poly[i][0], poly[i][1]);
    shape.closePath();

    const geom = new THREE.ShapeGeometry(shape);
    const mat = new THREE.MeshBasicMaterial({
      color,
      transparent: true,
      opacity: 0.28,
      depthWrite: false,
      side: THREE.DoubleSide,
    });
    const mesh = new THREE.Mesh(geom, mat);
    mesh.rotation.x = -Math.PI / 2;
    mesh.position.y = groundY;
    mesh.renderOrder = 10;
    mesh.userData = { entryKey: z.entryKey, groupIndex: z.groupIndex };
    group.add(mesh);

    const edges = new THREE.EdgesGeometry(geom);
    const line = new THREE.LineSegments(
      edges,
      new THREE.LineBasicMaterial({ color, transparent: true, opacity: 0.85 })
    );
    line.rotation.x = -Math.PI / 2;
    line.position.y = groundY + 0.02;
    line.renderOrder = 11;
    group.add(line);
  });

  scene.add(group);
  console.info(
    "field-explorer: drew",
    group.children.length / 2,
    "zone shape(s)",
    zoneMeta?.status || ""
  );
}

function drawPointMarkers(dbg, items, groupName, color, shape) {
  const { THREE, scene } = dbg;
  const old = scene.getObjectByName(groupName);
  if (old) scene.remove(old);

  const group = new THREE.Group();
  group.name = groupName;

  items.forEach((c) => {
    const x = Number(c.x ?? 0);
    const y = Number(c.y ?? 0.15);
    const z = Number(c.z ?? 0);
    if (!Number.isFinite(x) || !Number.isFinite(z)) return;

    let geom;
    if (shape === "cylinder") geom = new THREE.CylinderGeometry(0.18, 0.18, 0.5, 12);
    else if (shape === "cone") geom = new THREE.ConeGeometry(0.2, 0.45, 12);
    else geom = new THREE.BoxGeometry(0.35, 0.25, 0.28);

    const mesh = new THREE.Mesh(
      geom,
      new THREE.MeshBasicMaterial({
        color,
        transparent: true,
        opacity: 0.88,
        depthWrite: false,
      })
    );
    mesh.position.set(x, y, z);
    mesh.renderOrder = 20;
    mesh.userData = { name: c.name, source: c.source, chrId: c.chrId, triggerClass: c.triggerClass };
    group.add(mesh);

    const ring = new THREE.Mesh(
      new THREE.RingGeometry(0.22, 0.32, 24),
      new THREE.MeshBasicMaterial({
        color,
        transparent: true,
        opacity: 0.55,
        side: THREE.DoubleSide,
        depthWrite: false,
      })
    );
    ring.rotation.x = -Math.PI / 2;
    ring.position.set(x, 0.06, z);
    ring.renderOrder = 19;
    group.add(ring);
  });

  scene.add(group);
  console.info("field-explorer: drew", items.length, groupName);
}

function renderLayerToggles(layerState) {
  const labels = {
    story_npc: "NPC história (n###)",
    party: "Party (c###)",
    field_enemy: "Inimigos (m###)",
    field_prop: "Props/loja (f###)",
    summon: "Summon (s###)",
    weapon: "Arma (w###)",
    unknown: "Outros",
    rig: "Rig (k###)",
  };
  let s = `<div style="display:flex;flex-wrap:wrap;gap:6px;margin-bottom:8px">`;
  for (const [key, label] of Object.entries(labels)) {
    const checked = layerState[key] ? "checked" : "";
    s += `<label style="font-size:10px;color:#aac;display:flex;align-items:center;gap:3px">`;
    s += `<input type="checkbox" data-layer="${key}" ${checked} style="accent-color:#7fd4a8"> ${escapeHtml(label)}`;
    s += `</label>`;
  }
  s += `</div>`;
  return s;
}

function renderSceneNodeToggle(layerState) {
  const checked = layerState.scene_nodes ? "checked" : "";
  return (
    `<div style="display:flex;flex-wrap:wrap;gap:6px;margin-bottom:8px">` +
    `<label style="font-size:10px;color:#aac;display:flex;align-items:center;gap:3px">` +
    `<input type="checkbox" data-layer="scene_nodes" ${checked} style="accent-color:#e0a0ff"> Mostrar scene nodes (Phyre)` +
    `</label></div>`
  );
}

function renderEntitySection(list) {
  let s = "";
  const max = Math.min(list.length, 12);
  for (let i = 0; i < max; i++) {
    const c = list[i];
    const cat = c.chrCategory || c.layer || "?";
    s += `<div style="margin:4px 0;padding:4px 6px;background:rgba(255,255,255,0.05);border-radius:4px;font-size:11px">`;
    s += `<b>${escapeHtml(c.name || c.chrName || "?")}</b> · <span style="color:#8ac">${escapeHtml(cat)}</span>`;
    s += ` · (${Number(c.x).toFixed(1)}, ${Number(c.z).toFixed(1)})`;
    if (c.chrId != null) s += ` · id ${c.chrId}`;
    if (c.sampleCount > 1) s += ` · ${c.sampleCount} amostras`;
    s += `</div>`;
  }
  if (list.length > max) {
    s += `<div style="font-size:11px;color:#aac">+${list.length - max} no mapa</div>`;
  }
  return s;
}

function renderMarkerListSection(list, kind) {
  let s = "";
  const max = Math.min(list.length, 12);
  for (let i = 0; i < max; i++) {
    const c = list[i];
    s += `<div style="margin:4px 0;padding:4px 6px;background:rgba(255,255,255,0.05);border-radius:4px;font-size:11px">`;
    s += `<b>${escapeHtml(c.name || c.chrName || "?")}</b> · (${Number(c.x).toFixed(1)}, ${Number(c.z).toFixed(1)})`;
    if (kind === "npc" && c.chrId != null) s += ` · id ${c.chrId}`;
    if (kind === "trigger" && c.triggerClass) s += ` · ${escapeHtml(c.triggerClass)}`;
    s += `</div>`;
  }
  if (list.length > max) {
    s += `<div style="font-size:11px;color:#aac">+${list.length - max} no mapa</div>`;
  }
  return s;
}

function renderChestSection(list) {
  let s = "";
  const max = Math.min(list.length, 12);
  for (let i = 0; i < max; i++) {
    const c = list[i];
    s += `<div style="margin:4px 0;padding:4px 6px;background:rgba(240,192,64,0.08);border-radius:4px;font-size:11px">`;
    s += `<b>${escapeHtml(c.name || "?")}</b> · (${Number(c.x).toFixed(1)}, ${Number(c.z).toFixed(1)})`;
    s += `</div>`;
  }
  if (list.length > max) {
    s += `<div style="font-size:11px;color:#aac">+${list.length - max} baús no mapa</div>`;
  }
  return s;
}

function renderGroupSection(title, list) {
  if (!list.length) return "";
  let s = `<div style="margin-top:8px;font-weight:600;color:#9fd">${escapeHtml(title)} (${list.length})</div>`;
  for (const g of list) {
    const forms = (g.formations || [])
      .map((f) => `${f.battleId}${f.weight != null ? ` w${f.weight}` : ""}`)
      .join(", ");
    s += `<div style="margin:6px 0;padding:6px 8px;background:rgba(255,255,255,0.04);border-radius:4px">`;
    s += `<div><b>${escapeHtml(g.mapBucket)}</b> · grp ${g.groupIndex} · bf ${g.battlefield} · danger ${g.danger}</div>`;
    s += `<div style="font-size:11px;color:#aac;margin-top:2px">${escapeHtml(forms || "—")}</div>`;
    s += `</div>`;
  }
  return s;
}

function escapeHtml(s) {
  return String(s)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}

const FIELD_EXPLORER_PANEL_POS_KEY = "ffxFieldExplorerPanelPos";

function makePanelDraggable(panel) {
  const handle = panel.querySelector("#fieldExplorerDragHandle");
  if (!handle) return;

  try {
    const saved = JSON.parse(localStorage.getItem(FIELD_EXPLORER_PANEL_POS_KEY) || "null");
    if (saved && Number.isFinite(saved.left) && Number.isFinite(saved.top)) {
      panel.style.right = "auto";
      panel.style.left = `${clampPanelCoord(saved.left, panel, "x")}px`;
      panel.style.top = `${clampPanelCoord(saved.top, panel, "y")}px`;
    }
  } catch {
    /* ignore corrupt storage */
  }

  let dragging = false;
  let startX = 0;
  let startY = 0;
  let startLeft = 0;
  let startTop = 0;

  handle.addEventListener("pointerdown", (e) => {
    if (e.button !== 0) return;
    dragging = true;
    handle.style.cursor = "grabbing";
    handle.setPointerCapture(e.pointerId);
    const rect = panel.getBoundingClientRect();
    startX = e.clientX;
    startY = e.clientY;
    startLeft = rect.left;
    startTop = rect.top;
    panel.style.right = "auto";
    panel.style.left = `${startLeft}px`;
    panel.style.top = `${startTop}px`;
    e.preventDefault();
  });

  handle.addEventListener("pointermove", (e) => {
    if (!dragging) return;
    const left = clampPanelCoord(startLeft + (e.clientX - startX), panel, "x");
    const top = clampPanelCoord(startTop + (e.clientY - startY), panel, "y");
    panel.style.left = `${left}px`;
    panel.style.top = `${top}px`;
  });

  const endDrag = (e) => {
    if (!dragging) return;
    dragging = false;
    handle.style.cursor = "grab";
    try {
      handle.releasePointerCapture(e.pointerId);
    } catch {
      /* already released */
    }
    const left = parseFloat(panel.style.left) || 0;
    const top = parseFloat(panel.style.top) || 0;
    localStorage.setItem(FIELD_EXPLORER_PANEL_POS_KEY, JSON.stringify({ left, top }));
  };

  handle.addEventListener("pointerup", endDrag);
  handle.addEventListener("pointercancel", endDrag);

  handle.addEventListener("dblclick", (e) => {
    e.preventDefault();
    localStorage.removeItem(FIELD_EXPLORER_PANEL_POS_KEY);
    panel.style.left = "auto";
    panel.style.top = "12px";
    panel.style.right = "12px";
  });
}

function clampPanelCoord(value, panel, axis) {
  const margin = 4;
  if (axis === "x") {
    const max = Math.max(margin, window.innerWidth - panel.offsetWidth - margin);
    return Math.min(Math.max(margin, value), max);
  }
  const max = Math.max(margin, window.innerHeight - Math.min(panel.offsetHeight, 48) - margin);
  return Math.min(Math.max(margin, value), max);
}
