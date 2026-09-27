// 🎯 FFX Map SCENE editor — Milestone 3: TRANSFORM GIZMO.
//
// Additive ES module loaded by app.js. It wraps three's own (MIT) TransformControls so a selected submesh can be
// moved / rotated / scaled directly in the viewport, and reports the resulting transform so the host can write the
// delta to the sidecar (map-edits.json) via editStore.setTransform(key, {position,rotation,scale}).
//
// WHY A WRAPPER: keep app.js thin and keep all three.TransformControls quirks (it is an Object3D you add to the
// scene; it dispatches "dragging-changed" / "objectChange" / "mouseUp"; camera-vs-gizmo fighting) encapsulated here.
//
// CRITICAL CAMERA RULE (three's own example pattern): on the gizmo "dragging-changed" event, set
//   orbitControls.enabled = !event.value
// so the OrbitControls camera does NOT fight the gizmo while you drag a handle. We restore the *previous* orbit
// enabled state on drag-end rather than force-enabling, so walk mode (orbit disabled) is never silently re-enabled.
//
// VENDORED IMPORT (three@0.165.0, MIT): import via the "three/addons/" importmap specifier, which resolves to
//   ./vendor/three/jsm/controls/TransformControls.js (verified present; node --check OK).
// In v0.165.0 TransformControls EXTENDS Object3D — you add the controls instance itself to the scene (it parents its
// own gizmo + plane). There is no getHelper() in this version (that arrived in a later three release).
//
// API: createGizmo({THREE, scene, camera, renderer, domElement, orbitControls}) -> {
//   attach(object), detach(), setMode(m), onObjectChange(cb)->unsubscribe, isDragging(), getControls(), dispose()
// }
//   onObjectChange(cb): cb({ object, mode, phase }) fires on every live "objectChange" (phase:"drag") AND once on
//   "mouseUp" (phase:"end"). The host should read object.position/rotation/scale and persist on phase:"end"
//   (and may live-preview on phase:"drag"). rotation is a THREE.Euler in RADIANS (XYZ) — matches the sidecar shape.
//
// VERIFICATION NOTE: on-screen visual truth is NOT available this run. Correctness was established via node --check
// + reading the vendored TransformControls.js source (event names, Object3D-add semantics, attach/detach/setMode/
// dispose surface). Visual drag behavior is UNVERIFIED on screen.

import * as THREE from "three";
import { TransformControls } from "three/addons/controls/TransformControls.js";

const VALID_MODES = new Set(["translate", "rotate", "scale"]);

// createGizmo(opts) -> controller
//   Required: scene, camera, renderer (or domElement). orbitControls is strongly recommended (camera-fight guard).
export function createGizmo(opts) {
  const {
    THREE: THREElib = THREE,
    scene,
    camera,
    renderer,
    domElement: domElementOpt,
    orbitControls = null,
  } = opts || {};

  if (!scene || !camera || (!renderer && !domElementOpt)) {
    throw new Error("createGizmo: requires { scene, camera, renderer | domElement }");
  }

  const domElement = domElementOpt || renderer.domElement;
  const objectChangeCallbacks = new Set();

  const controls = new TransformControls(camera, domElement);
  controls.setMode("translate");
  // Slightly larger handles so the gizmo is grabbable over dense map geometry. (Cosmetic; safe default.)
  if (typeof controls.setSize === "function") controls.setSize(0.9);

  // TransformControls IS an Object3D in 0.165 → add it directly; it parents its own gizmo + interaction plane.
  scene.add(controls);

  let attached = null;          // the object currently controlled (mirror of controls.object)
  let dragging = false;         // live drag state (mirrors controls.dragging via dragging-changed)
  let orbitWasEnabled = null;   // remembered OrbitControls.enabled to restore after a drag (never force-enable)

  function fire(phase) {
    const object = attached || controls.object || null;
    if (!object) return;
    const payload = { object, mode: controls.mode, phase };
    for (const cb of objectChangeCallbacks) {
      try {
        cb(payload);
      } catch (err) {
        console.warn("gizmo onObjectChange callback threw:", err?.message || err);
      }
    }
  }

  // CRITICAL: stop the orbit camera from fighting the gizmo while a handle is being dragged.
  function onDraggingChanged(event) {
    dragging = Boolean(event.value);
    if (!orbitControls) return;
    if (dragging) {
      // Remember the current orbit state, then disable so the drag owns the pointer.
      orbitWasEnabled = orbitControls.enabled;
      orbitControls.enabled = false;
    } else {
      // Restore the PREVIOUS orbit state (do not blindly re-enable → respects walk mode where orbit is disabled).
      if (orbitWasEnabled !== null) {
        orbitControls.enabled = orbitWasEnabled;
        orbitWasEnabled = null;
      }
    }
  }

  function onObjectChange() {
    fire("drag");
  }

  // mouseUp = the commit moment (drag finished, transform is final) → emit phase:"end" for sidecar persistence.
  function onMouseUp() {
    fire("end");
  }

  controls.addEventListener("dragging-changed", onDraggingChanged);
  controls.addEventListener("objectChange", onObjectChange);
  controls.addEventListener("mouseUp", onMouseUp);

  return {
    // Attach the gizmo to a THREE.Object3D so its handles appear and can transform it.
    attach(object) {
      if (!object) {
        this.detach();
        return;
      }
      attached = object;
      controls.attach(object);
    },
    // Hide/disable the gizmo (no object controlled). Safe to call when nothing is attached.
    detach() {
      attached = null;
      if (controls.object) controls.detach();
    },
    // mode: "translate" | "rotate" | "scale". Ignores anything else.
    setMode(mode) {
      if (!VALID_MODES.has(mode)) return;
      controls.setMode(mode);
    },
    getMode() {
      return controls.mode;
    },
    // Subscribe to transform changes. cb({object, mode, phase}); phase "drag" (live) | "end" (commit). Returns an
    // unsubscribe function.
    onObjectChange(cb) {
      if (typeof cb === "function") objectChangeCallbacks.add(cb);
      return () => objectChangeCallbacks.delete(cb);
    },
    isDragging() {
      return dragging;
    },
    getAttached() {
      return attached || controls.object || null;
    },
    // Escape hatch for hosts that need the raw three control (e.g. setSpace, snapping).
    getControls() {
      return controls;
    },
    dispose() {
      controls.removeEventListener("dragging-changed", onDraggingChanged);
      controls.removeEventListener("objectChange", onObjectChange);
      controls.removeEventListener("mouseUp", onMouseUp);
      if (controls.object) controls.detach();
      scene.remove(controls);
      if (typeof controls.dispose === "function") controls.dispose();
      objectChangeCallbacks.clear();
      attached = null;
    },
  };
}
