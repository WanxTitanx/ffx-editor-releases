// 🎯 FFX Map SCENE editor — SIDECAR persistence (T2 core).
//
// Additive ES module. It is the SOURCE OF TRUTH for per-map edits: a key-keyed diff that is replayed over the
// IMMUTABLE baked glTF every time a map loads. The glTF is NEVER re-serialized or mutated on disk — only this
// sidecar (map-edits.json) carries the user's changes, and applyTo() lays them back over the loaded three.js root.
//
// KEY CONTRACT (must match editor/selection.js keyOf scheme exactly — the two modules share the sidecar key space):
//   1. meshId AND submeshId both resolvable -> `${meshId}:${submeshId}`  (e.g. "0:7")
//   2. else object.name non-empty           -> object.name
//   3. else deterministic descendant index   -> `idx:<n>` (n = traversal index of pickable meshes under root)
//
// SIDECAR JSON shape (version 1):
//   {
//     version: 1,
//     mapId: "map/azit/azit00",
//     edits: {
//       "0:7": {
//         transform?: { position:[x,y,z], rotation:[x,y,z], scale:[x,y,z] },  // rotation in RADIANS (Euler XYZ)
//         material?:  { color?, emissive?, opacity?, transparent?, wireframe?, visible? },
//         light?:     { color?, intensity? }
//       },
//       ...
//     }
//   }
//
// PERSISTENCE:
//   - save(): if a writable file handle was granted (File System Access API showSaveFilePicker), write map-edits.json
//     through it. ALSO always mirror the JSON to localStorage keyed by mapId so reload-replay works WITHOUT any
//     directory/file grant (no-permission fallback).
//   - load(): prefer a granted/persisted file handle (read its bytes) else fall back to localStorage.
//
// IDEMPOTENCY (critical): applyTo() captures each object's ORIGINAL/baseline transform + material + light on FIRST
// sight (stashed on object.userData.__ffxBaseline). Re-applying always resets to that baseline first, then layers the
// stored edit on top — so calling applyTo() repeatedly NEVER compounds (no drift, no double-scale).
//
// VERIFICATION NOTE: on-screen visual truth is NOT available this run. Correctness was established via node --check
// and an in-module idempotency contract (baseline capture + reset-before-apply). Visual replay is unverified on screen.

import { keyOf } from "./selection.js";

const SIDECAR_FILENAME = "map-edits.json";
const SIDECAR_VERSION = 1;
const BASELINE_KEY = "__ffxBaseline";
const LOCALSTORAGE_PREFIX = "ffx-map-edits:";

/* ───────────────────────── small value helpers ───────────────────────── */

function isFiniteNumber(value) {
  return typeof value === "number" && Number.isFinite(value);
}

// Coerce an arbitrary value to a finite [x,y,z] array, else null.
function toVec3(value) {
  if (!Array.isArray(value) || value.length < 3) return null;
  const x = Number(value[0]);
  const y = Number(value[1]);
  const z = Number(value[2]);
  if (!Number.isFinite(x) || !Number.isFinite(y) || !Number.isFinite(z)) return null;
  return [x, y, z];
}

// Normalize a transform sub-object: only keep the [x,y,z] triples that are valid. Returns null when empty.
function sanitizeTransform(transform) {
  if (!transform || typeof transform !== "object") return null;
  const out = {};
  const position = toVec3(transform.position);
  const rotation = toVec3(transform.rotation);
  const scale = toVec3(transform.scale);
  if (position) out.position = position;
  if (rotation) out.rotation = rotation;
  if (scale) out.scale = scale;
  return Object.keys(out).length > 0 ? out : null;
}

// Normalize a material sub-object: keep only the recognized, well-typed fields. Returns null when empty.
function sanitizeMaterial(material) {
  if (!material || typeof material !== "object") return null;
  const out = {};
  if (material.color !== undefined && material.color !== null) out.color = material.color;
  if (material.emissive !== undefined && material.emissive !== null) out.emissive = material.emissive;
  if (isFiniteNumber(material.opacity)) out.opacity = material.opacity;
  if (typeof material.transparent === "boolean") out.transparent = material.transparent;
  if (typeof material.wireframe === "boolean") out.wireframe = material.wireframe;
  if (typeof material.visible === "boolean") out.visible = material.visible;
  return Object.keys(out).length > 0 ? out : null;
}

// Normalize a light sub-object. Returns null when empty.
function sanitizeLight(light) {
  if (!light || typeof light !== "object") return null;
  const out = {};
  if (light.color !== undefined && light.color !== null) out.color = light.color;
  if (isFiniteNumber(light.intensity)) out.intensity = light.intensity;
  return Object.keys(out).length > 0 ? out : null;
}

// Normalize one edit entry; drops empty sub-sections, returns null if nothing valid remains.
function sanitizeEdit(edit) {
  if (!edit || typeof edit !== "object") return null;
  const out = {};
  const transform = sanitizeTransform(edit.transform);
  const material = sanitizeMaterial(edit.material);
  const light = sanitizeLight(edit.light);
  if (transform) out.transform = transform;
  if (material) out.material = material;
  if (light) out.light = light;
  return Object.keys(out).length > 0 ? out : null;
}

/* ───────────────────────── store factory ───────────────────────── */

// createEditStore({ mapId }) -> store
//   mapId : stable per-map identifier (use entry.area, e.g. "map/azit/azit00"). Required.
export function createEditStore(opts) {
  const mapId = opts && typeof opts.mapId === "string" && opts.mapId.length > 0 ? opts.mapId : null;
  if (!mapId) {
    throw new Error("createEditStore: requires a non-empty { mapId }");
  }

  const localStorageKey = LOCALSTORAGE_PREFIX + mapId;

  // The in-memory edit table: { [key]: editObject }. This is the live diff; toJSON() wraps it with version/mapId.
  let edits = Object.create(null);

  // Granted File System Access handle (FileSystemFileHandle) for map-edits.json, if the user picked one.
  let fileHandle = null;

  /* ── localStorage mirror (no-permission fallback) ── */

  function readLocalStorage() {
    try {
      if (typeof localStorage === "undefined") return null;
      const raw = localStorage.getItem(localStorageKey);
      if (!raw) return null;
      return JSON.parse(raw);
    } catch (err) {
      console.warn("mapEdits: localStorage read failed:", err?.message || err);
      return null;
    }
  }

  function writeLocalStorage(json) {
    try {
      if (typeof localStorage === "undefined") return;
      localStorage.setItem(localStorageKey, JSON.stringify(json));
    } catch (err) {
      console.warn("mapEdits: localStorage write failed:", err?.message || err);
    }
  }

  // Replace the in-memory edit table from a parsed sidecar object (defensive: tolerate junk).
  function adoptSidecar(parsed) {
    const next = Object.create(null);
    const src = parsed && typeof parsed.edits === "object" && parsed.edits ? parsed.edits : null;
    if (src) {
      for (const key of Object.keys(src)) {
        const clean = sanitizeEdit(src[key]);
        if (clean) next[key] = clean;
      }
    }
    edits = next;
  }

  /* ── load / save ── */

  // Read sidecar bytes from a granted file handle (if any), else from localStorage. Adopts whatever it finds.
  async function load() {
    // 1) Prefer a granted file handle (re-verify read permission first).
    if (fileHandle && typeof fileHandle.getFile === "function") {
      try {
        if (await ensurePermission(fileHandle, "read")) {
          const file = await fileHandle.getFile();
          const text = await file.text();
          if (text && text.trim().length > 0) {
            adoptSidecar(JSON.parse(text));
            return toJSON();
          }
        }
      } catch (err) {
        console.warn("mapEdits: file handle load failed, falling back to localStorage:", err?.message || err);
      }
    }
    // 2) Fallback: localStorage mirror.
    const fromLocal = readLocalStorage();
    if (fromLocal) {
      adoptSidecar(fromLocal);
    } else {
      edits = Object.create(null);
    }
    return toJSON();
  }

  // Persist the sidecar. ALWAYS mirrors to localStorage; ADDITIONALLY writes through a file handle when available
  // (asking for one via showSaveFilePicker on the first save() if File System Access is supported and none granted).
  async function save({ promptForFile = true } = {}) {
    const json = toJSON();
    // localStorage mirror is unconditional and permission-free — this is what makes reload-replay work by default.
    writeLocalStorage(json);

    // Best-effort durable file via File System Access API. Never throws out of save() on a permission/abort.
    try {
      if (!fileHandle && promptForFile && typeof window !== "undefined" && typeof window.showSaveFilePicker === "function") {
        fileHandle = await window.showSaveFilePicker({
          suggestedName: SIDECAR_FILENAME,
          types: [
            {
              description: "FFX map edits sidecar",
              accept: { "application/json": [".json"] },
            },
          ],
        });
      }
      if (fileHandle && typeof fileHandle.createWritable === "function") {
        if (await ensurePermission(fileHandle, "readwrite")) {
          const writable = await fileHandle.createWritable();
          await writable.write(JSON.stringify(json, null, 2));
          await writable.close();
          return { ok: true, persisted: "file+localStorage" };
        }
      }
    } catch (err) {
      // AbortError = user cancelled the picker; treat as soft (localStorage already holds the data).
      if (err && err.name === "AbortError") {
        return { ok: true, persisted: "localStorage" };
      }
      console.warn("mapEdits: file save failed, kept localStorage mirror:", err?.message || err);
    }
    return { ok: true, persisted: "localStorage" };
  }

  // Allow a host to attach a previously granted/persisted handle (e.g. from IndexedDB) before load().
  function setFileHandle(handle) {
    fileHandle = handle || null;
  }
  function getFileHandle() {
    return fileHandle;
  }

  /* ── edit table mutators (return the resulting edit, or null if it was cleared) ── */

  function get(key) {
    if (typeof key !== "string" || key.length === 0) return null;
    return edits[key] || null;
  }

  // Merge `patch` into edits[key].<section>, dropping the section when it normalizes to empty.
  function mergeSection(key, section, patch, sanitizeFn) {
    if (typeof key !== "string" || key.length === 0) return null;
    const cleanPatch = sanitizeFn(patch);
    const current = edits[key] ? { ...edits[key] } : {};
    if (cleanPatch) {
      current[section] = { ...(current[section] || {}), ...cleanPatch };
    }
    // Re-sanitize the whole edit so the section/edit collapses to null when it becomes empty.
    const finalEdit = sanitizeEdit(current);
    if (finalEdit) {
      edits[key] = finalEdit;
      return finalEdit;
    }
    delete edits[key];
    return null;
  }

  function setTransform(key, transform) {
    return mergeSection(key, "transform", transform, sanitizeTransform);
  }
  function setMaterial(key, material) {
    return mergeSection(key, "material", material, sanitizeMaterial);
  }
  function setLight(key, light) {
    return mergeSection(key, "light", light, sanitizeLight);
  }

  // Remove all edits for a key (or, when key omitted/null, every edit for this map).
  function clear(key) {
    if (key === undefined || key === null) {
      edits = Object.create(null);
      return;
    }
    if (typeof key === "string") delete edits[key];
  }

  // Serialize the current diff to the canonical sidecar shape.
  function toJSON() {
    const outEdits = {};
    for (const key of Object.keys(edits)) {
      outEdits[key] = edits[key];
    }
    return { version: SIDECAR_VERSION, mapId, edits: outEdits };
  }

  /* ── replay (applyTo) ── */

  // Capture the baseline ONCE per object so re-application is idempotent. Stored on object.userData.__ffxBaseline.
  function captureBaseline(object) {
    if (!object.userData) object.userData = {};
    if (object.userData[BASELINE_KEY]) return object.userData[BASELINE_KEY];

    const baseline = {
      position: [object.position.x, object.position.y, object.position.z],
      rotation: [object.rotation.x, object.rotation.y, object.rotation.z],
      scale: [object.scale.x, object.scale.y, object.scale.z],
      visible: object.visible,
      materials: [],
      light: null,
    };

    const mats = Array.isArray(object.material) ? object.material : object.material ? [object.material] : [];
    for (const mat of mats) {
      if (!mat) {
        baseline.materials.push(null);
        continue;
      }
      baseline.materials.push({
        color: mat.color && typeof mat.color.getHex === "function" ? mat.color.getHex() : null,
        emissive: mat.emissive && typeof mat.emissive.getHex === "function" ? mat.emissive.getHex() : null,
        opacity: isFiniteNumber(mat.opacity) ? mat.opacity : null,
        transparent: typeof mat.transparent === "boolean" ? mat.transparent : null,
        wireframe: typeof mat.wireframe === "boolean" ? mat.wireframe : null,
      });
    }

    if (object.isLight) {
      baseline.light = {
        color: object.color && typeof object.color.getHex === "function" ? object.color.getHex() : null,
        intensity: isFiniteNumber(object.intensity) ? object.intensity : null,
      };
    }

    object.userData[BASELINE_KEY] = baseline;
    return baseline;
  }

  // Reset an object to its captured baseline (so we never compound on top of a prior apply).
  function resetToBaseline(object, baseline) {
    object.position.set(baseline.position[0], baseline.position[1], baseline.position[2]);
    object.rotation.set(baseline.rotation[0], baseline.rotation[1], baseline.rotation[2]);
    object.scale.set(baseline.scale[0], baseline.scale[1], baseline.scale[2]);
    object.visible = baseline.visible;

    const mats = Array.isArray(object.material) ? object.material : object.material ? [object.material] : [];
    for (let i = 0; i < mats.length; i += 1) {
      const mat = mats[i];
      const base = baseline.materials[i];
      if (!mat || !base) continue;
      if (base.color !== null && mat.color && typeof mat.color.setHex === "function") mat.color.setHex(base.color);
      if (base.emissive !== null && mat.emissive && typeof mat.emissive.setHex === "function") mat.emissive.setHex(base.emissive);
      if (base.opacity !== null) mat.opacity = base.opacity;
      if (base.transparent !== null) mat.transparent = base.transparent;
      if (base.wireframe !== null) mat.wireframe = base.wireframe;
      mat.needsUpdate = true;
    }

    if (object.isLight && baseline.light) {
      if (baseline.light.color !== null && object.color && typeof object.color.setHex === "function") {
        object.color.setHex(baseline.light.color);
      }
      if (baseline.light.intensity !== null) object.intensity = baseline.light.intensity;
    }
  }

  // Coerce a stored color value (number, "#rrggbb", or [r,g,b] 0..1) onto a THREE.Color via its setters.
  function applyColorTo(colorObj, value) {
    if (!colorObj) return;
    if (typeof value === "number" && Number.isFinite(value)) {
      if (typeof colorObj.setHex === "function") colorObj.setHex(value);
      return;
    }
    if (typeof value === "string" && value.length > 0) {
      if (typeof colorObj.set === "function") colorObj.set(value);
      return;
    }
    if (Array.isArray(value) && value.length >= 3) {
      const r = Number(value[0]);
      const g = Number(value[1]);
      const b = Number(value[2]);
      if (Number.isFinite(r) && Number.isFinite(g) && Number.isFinite(b) && typeof colorObj.setRGB === "function") {
        colorObj.setRGB(r, g, b);
      }
    }
  }

  function applyTransform(object, transform) {
    if (transform.position) object.position.set(transform.position[0], transform.position[1], transform.position[2]);
    if (transform.rotation) object.rotation.set(transform.rotation[0], transform.rotation[1], transform.rotation[2]);
    if (transform.scale) object.scale.set(transform.scale[0], transform.scale[1], transform.scale[2]);
  }

  function applyMaterial(object, material) {
    if (typeof material.visible === "boolean") object.visible = material.visible;
    const mats = Array.isArray(object.material) ? object.material : object.material ? [object.material] : [];
    for (const mat of mats) {
      if (!mat) continue;
      if (material.color !== undefined && material.color !== null) applyColorTo(mat.color, material.color);
      if (material.emissive !== undefined && material.emissive !== null) applyColorTo(mat.emissive, material.emissive);
      if (isFiniteNumber(material.opacity)) mat.opacity = material.opacity;
      if (typeof material.transparent === "boolean") mat.transparent = material.transparent;
      if (typeof material.wireframe === "boolean") mat.wireframe = material.wireframe;
      mat.needsUpdate = true;
    }
  }

  function applyLight(object, light) {
    if (!object.isLight) return;
    if (light.color !== undefined && light.color !== null) applyColorTo(object.color, light.color);
    if (isFiniteNumber(light.intensity)) object.intensity = light.intensity;
  }

  // Walk the root's descendants, and for every object with a stored edit, reset-to-baseline then layer the edit.
  // IDEMPOTENT: safe to call any number of times; never compounds. Returns the count of objects touched.
  function applyTo(root) {
    if (!root || typeof root.traverse !== "function") return 0;
    let applied = 0;
    root.traverse((object) => {
      if (!object || (!object.isMesh && !object.isLight)) return;
      const key = keyOf(object, root);
      if (!key) return;

      const baseline = captureBaseline(object);
      const edit = edits[key];

      // Always reset to baseline first so a removed/changed edit doesn't leave stale state from a prior apply.
      resetToBaseline(object, baseline);

      if (!edit) return;
      if (edit.transform) applyTransform(object, edit.transform);
      if (edit.material) applyMaterial(object, edit.material);
      if (edit.light) applyLight(object, edit.light);
      applied += 1;
    });
    return applied;
  }

  return {
    mapId,
    load,
    save,
    get,
    setTransform,
    setMaterial,
    setLight,
    clear,
    toJSON,
    applyTo,
    setFileHandle,
    getFileHandle,
  };
}

/* ───────────────────────── File System Access permission helper ───────────────────────── */

// Re-verify (and, where allowed, re-request) permission on a stored handle. Returns true when usable.
async function ensurePermission(handle, mode) {
  try {
    const descriptor = { mode };
    if (typeof handle.queryPermission === "function") {
      const status = await handle.queryPermission(descriptor);
      if (status === "granted") return true;
    }
    if (typeof handle.requestPermission === "function") {
      const status = await handle.requestPermission(descriptor);
      return status === "granted";
    }
    // Older implementations without the permission API: assume usable (createWritable/getFile will throw if not).
    return true;
  } catch (err) {
    console.warn("mapEdits: permission check failed:", err?.message || err);
    return false;
  }
}
