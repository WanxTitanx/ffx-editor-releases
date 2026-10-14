// 🎯 FFX Map SCENE editor — Milestone 1: SUBMESH SELECTION.
//
// Additive ES module loaded by app.js (it never edits the render loop). It makes the loaded glTF root CLICKABLE:
// pointerdown raycasts the loaded map, picks the nearest submesh under the cursor, highlights it, and fires onPick.
//
// IDENTITY (PROVED 2026-06-06 against the vendored three@0.165.0 GLTFLoader + the real azit00 glTF):
//   GLTFLoader splits the 1 mesh / N primitives into a Group of N child THREE.Mesh. The per-primitive
//   extras { submeshId, meshId, fileMaterialId, ... } are assigned to **child.geometry.userData** (lowercase
//   camelCase), NOT child.userData (which is {}). GLTFLoader.js:4710 assignExtrasToUserData(geometry, primitiveDef).
//   The human label lives on **child.material.name** (e.g. "submesh_0_material_0_texture_candidate").
//   keyOf() reads geometry.userData first, then object.userData, accepting both camelCase and PascalCase casings,
//   so it stays correct if a future writer emits identity on the mesh node or a different map uses PascalCase.
//
// keyOf SCHEME (STABLE across reloads — this is the sidecar key contract later modules must match):
//   1. when meshId AND submeshId are both resolvable  -> `${meshId}:${submeshId}`  (e.g. "0:7")
//   2. else when object.name is non-empty             -> object.name
//   3. else deterministic descendant path under root  -> `idx:<n>` (n = traversal index of pickable meshes)
//
// onPick PAYLOAD shape: { object, key, submeshId, meshId, fileMaterialId, materialName }
//   - object        : the THREE.Mesh that was picked (highlighted)
//   - key           : keyOf(object) — the stable sidecar key
//   - submeshId     : number | null
//   - meshId        : number | null
//   - fileMaterialId: number | null
//   - materialName  : string | null  (from object.material.name)
//
// VERIFICATION NOTE: on-screen visual truth is NOT available this run. Correctness was established via a node
// end-to-end GLTFLoader probe (extras location/casing) + node --check. Visual highlight is unverified on screen.

import * as THREE from "three";

/* ───────────────────────── identity helpers (exported) ───────────────────────── */

// Read an identity field from the proved location (geometry.userData) with defensive fallbacks:
//   geometry.userData[camel] -> geometry.userData[Pascal] -> object.userData[camel] -> object.userData[Pascal]
function readIdField(object, camel, pascal) {
  const gu = object?.geometry?.userData;
  if (gu) {
    if (gu[camel] !== undefined && gu[camel] !== null) return gu[camel];
    if (gu[pascal] !== undefined && gu[pascal] !== null) return gu[pascal];
  }
  const ou = object?.userData;
  if (ou) {
    if (ou[camel] !== undefined && ou[camel] !== null) return ou[camel];
    if (ou[pascal] !== undefined && ou[pascal] !== null) return ou[pascal];
  }
  return null;
}

// Coerce a raw extras value to a finite number, else null.
function toNum(value) {
  if (value === null || value === undefined) return null;
  const n = Number(value);
  return Number.isFinite(n) ? n : null;
}

export function submeshIdOf(object) {
  return toNum(readIdField(object, "submeshId", "SubmeshId"));
}
export function meshIdOf(object) {
  return toNum(readIdField(object, "meshId", "MeshId"));
}
export function fileMaterialIdOf(object) {
  return toNum(readIdField(object, "fileMaterialId", "FileMaterialId"));
}
export function materialNameOf(object) {
  const mat = Array.isArray(object?.material) ? object.material[0] : object?.material;
  const name = mat?.name;
  return typeof name === "string" && name.length > 0 ? name : null;
}

// STABLE sidecar key. Pass an optional `root` so the path-index fallback is deterministic relative to that root.
export function keyOf(object, root = null) {
  if (!object) return null;
  const meshId = meshIdOf(object);
  const submeshId = submeshIdOf(object);
  if (meshId !== null && submeshId !== null) {
    return `${meshId}:${submeshId}`;
  }
  if (typeof object.name === "string" && object.name.length > 0) {
    return object.name;
  }
  // Deterministic path index: position among pickable descendant meshes of root (stable for a fixed glTF).
  if (root) {
    let idx = -1;
    let found = -1;
    root.traverse((o) => {
      if (o.isMesh) {
        idx += 1;
        if (o === object && found < 0) found = idx;
      }
    });
    if (found >= 0) return `idx:${found}`;
  }
  return `idx:${object.id}`; // last resort: three's per-object runtime id (NOT reload-stable; only when no root given)
}

/* ───────────────────────── selection controller ───────────────────────── */

// createSelection({THREE, scene, camera, renderer, domElement, getRoot}) -> controller
//   getRoot()  : () => THREE.Object3D | null   the loaded map scene root to raycast against.
// Optional in opts:
//   isGizmoDragging : () => boolean            suppress picking while a transform gizmo is dragging (defaults false)
//   isGizmoEngaged  : () => boolean            suppress picking when a transform gizmo handle is hovered/active
//                                              (pre-drag: TransformControls.axis !== null); defaults false. This
//                                              closes the mid-click reselect race where the handle visually overlaps
//                                              a different mesh behind it before `dragging` has flipped true.
//   getMode         : () => string             authoritative app mode ("orbit"|"walk"); defaults to reading
//                                              window.ffxMapViewerDebug.state.mode, else "orbit".
//   isPointerLocked : () => boolean            defaults to document.pointerLockElement presence.
export function createSelection(opts) {
  const {
    THREE: THREElib = THREE,
    scene,
    camera,
    renderer,
    domElement: domElementOpt,
    getRoot,
  } = opts || {};

  if (!scene || !camera || !renderer || typeof getRoot !== "function") {
    throw new Error("createSelection: requires { scene, camera, renderer, getRoot }");
  }

  const domElement = domElementOpt || renderer.domElement;
  const raycaster = new THREElib.Raycaster();
  const ndc = new THREElib.Vector2();
  const pickCallbacks = new Set();

  let enabled = true;            // master switch (selection active)
  let gizmoDragging = false;     // set true by a future TransformControls agent during drag
  let selected = null;           // currently selected THREE.Mesh
  let boxHelper = null;          // outline highlight (works for unlit FFX materials)
  const emissiveSaved = new WeakMap(); // material -> { emissive:Color clone, emissiveIntensity:number, has:boolean }

  const isGizmoDragging =
    typeof opts.isGizmoDragging === "function" ? opts.isGizmoDragging : () => gizmoDragging;

  // True when a gizmo handle is hovered/active (axis !== null) even before a drag begins. Bailing here prevents a
  // click that lands on a gizmo handle from re-picking the mesh rendered behind it.
  const isGizmoEngaged =
    typeof opts.isGizmoEngaged === "function" ? opts.isGizmoEngaged : () => false;

  const getMode =
    typeof opts.getMode === "function"
      ? opts.getMode
      : () => {
          const m = window.ffxMapViewerDebug?.state?.mode;
          return typeof m === "string" ? m : "orbit";
        };

  const isPointerLocked =
    typeof opts.isPointerLocked === "function"
      ? opts.isPointerLocked
      : () => Boolean(document.pointerLockElement);

  // Collect the pickable descendant meshes of the current root.
  function collectPickables() {
    const root = getRoot();
    if (!root) return [];
    const meshes = [];
    root.traverse((o) => {
      if (o.isMesh && o.visible !== false) meshes.push(o);
    });
    return meshes;
  }

  function eventToNdc(ev) {
    const rect = domElement.getBoundingClientRect();
    ndc.x = ((ev.clientX - rect.left) / rect.width) * 2 - 1;
    ndc.y = -((ev.clientY - rect.top) / rect.height) * 2 + 1;
  }

  function clearHighlight() {
    if (boxHelper) {
      scene.remove(boxHelper);
      boxHelper.geometry?.dispose?.();
      boxHelper.material?.dispose?.();
      boxHelper = null;
    }
    if (selected) {
      const mats = Array.isArray(selected.material) ? selected.material : [selected.material];
      for (const mat of mats) {
        if (!mat) continue;
        const saved = emissiveSaved.get(mat);
        if (saved) {
          if (saved.has && mat.emissive && saved.emissive) {
            mat.emissive.copy(saved.emissive);
            if (typeof saved.emissiveIntensity === "number") mat.emissiveIntensity = saved.emissiveIntensity;
          }
          mat.needsUpdate = true;
          emissiveSaved.delete(mat);
        }
      }
    }
  }

  function applyHighlight(object) {
    // Emissive boost when the material supports it (lit materials); FFX maps are unlit MeshBasicMaterial which
    // has no .emissive, so the BoxHelper outline below is the dependable visual for this dataset.
    const mats = Array.isArray(object.material) ? object.material : [object.material];
    for (const mat of mats) {
      if (!mat) continue;
      if (mat.emissive) {
        emissiveSaved.set(mat, {
          has: true,
          emissive: mat.emissive.clone(),
          emissiveIntensity: typeof mat.emissiveIntensity === "number" ? mat.emissiveIntensity : 1,
        });
        mat.emissive.setRGB(0.25, 0.55, 0.35);
        if (typeof mat.emissiveIntensity === "number") mat.emissiveIntensity = Math.max(mat.emissiveIntensity, 1);
        mat.needsUpdate = true;
      }
    }
    // Outline that does not depend on material lighting model — always visible.
    boxHelper = new THREElib.BoxHelper(object, 0x6cff9b);
    boxHelper.material.depthTest = false;
    boxHelper.material.transparent = true;
    boxHelper.renderOrder = 1001;
    scene.add(boxHelper);
  }

  function buildPayload(object) {
    return {
      object,
      key: keyOf(object, getRoot()),
      submeshId: submeshIdOf(object),
      meshId: meshIdOf(object),
      fileMaterialId: fileMaterialIdOf(object),
      materialName: materialNameOf(object),
    };
  }

  // Programmatic select (or clear with null). Highlights and fires onPick; safe to call from other modules.
  function select(objOrNull) {
    clearHighlight();
    selected = objOrNull || null;
    if (selected) applyHighlight(selected);
    const payload = selected ? buildPayload(selected) : { object: null, key: null, submeshId: null, meshId: null, fileMaterialId: null, materialName: null };
    for (const cb of pickCallbacks) {
      try {
        cb(payload);
      } catch (err) {
        console.warn("selection onPick callback threw:", err?.message || err);
      }
    }
    return selected;
  }

  function onPointerDown(ev) {
    if (!enabled) return;
    if (ev.button !== undefined && ev.button !== 0) return; // left button only
    if (isGizmoDragging()) return;                          // don't pick while dragging a gizmo
    if (isGizmoEngaged()) return;                           // don't pick when a gizmo handle is hovered/active (pre-drag)
    if (getMode() !== "orbit") return;                      // only in orbit mode (walk uses pointer-lock)
    if (isPointerLocked()) return;                          // never while pointer-locked

    const pickables = collectPickables();
    if (pickables.length === 0) return;

    eventToNdc(ev);
    raycaster.setFromCamera(ndc, camera);
    const hits = raycaster.intersectObjects(pickables, false);
    if (hits.length === 0) {
      // Click on empty space clears the selection (don't preventDefault — let orbit keep working otherwise).
      if (selected) select(null);
      return;
    }
    select(hits[0].object);
  }

  // Keep the highlight box following the object across animation/transform updates.
  function refreshHelper() {
    if (boxHelper && selected) boxHelper.update();
  }

  // bubble-phase listener: cooperates with aurora-overlay's capture-phase picker (it stopPropagation's its own
  // hits before bubble), so the two pickers do not double-fire on the same click.
  domElement.addEventListener("pointerdown", onPointerDown);

  return {
    onPick(cb) {
      if (typeof cb === "function") pickCallbacks.add(cb);
      return () => pickCallbacks.delete(cb);
    },
    getSelected() {
      return selected;
    },
    select,
    keyOf: (obj) => keyOf(obj, getRoot()),
    refreshHelper,
    setEnabled(value) {
      enabled = Boolean(value);
      if (!enabled && selected) select(null);
    },
    isEnabled() {
      return enabled;
    },
    setGizmoDragging(value) {
      gizmoDragging = Boolean(value);
    },
    dispose() {
      domElement.removeEventListener("pointerdown", onPointerDown);
      clearHighlight();
      selected = null;
      pickCallbacks.clear();
    },
  };
}
