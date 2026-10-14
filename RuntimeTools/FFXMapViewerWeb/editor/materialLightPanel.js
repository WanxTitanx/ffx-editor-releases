// 🎯 FFX Map SCENE editor — Milestone 4 (c4): MATERIAL / LIGHT OVERRIDE PANEL.
//
// Additive ES module loaded by app.js. It renders editable controls for the CURRENTLY SELECTED object (from
// editor/selection.js onPick) and applies every change BOTH live to the three.js material/light AND into the
// sidecar (editor/mapEdits.js) via editStore.setMaterial(key,{...}) / editStore.setLight(key,{...}).
//
// SHAPE CONTRACT (must match the c2 sidecar shapes exactly so applyTo() replay restores them on reload):
//   material : { color?, emissive?, opacity?, transparent?, wireframe?, visible? }
//              color/emissive emitted as a number (0xRRGGBB) — accepted by mapEdits.applyColorTo (number|string|rgb).
//              opacity 0..1 number; transparent/wireframe/visible booleans.
//   light    : { color?, intensity? }
//              color emitted as number (0xRRGGBB); intensity >= 0 number.
//
// KEY: this module is given a fully-built selection payload (which already carries .key = keyOf(object)). It never
// computes keys itself; the sidecar key contract lives in selection.js/mapEdits.js. If the payload has no usable key
// the panel still shows live-only controls (changes apply to three but are not persisted, with a visible note).
//
// LIVE-vs-PERSIST: onChange(set) is called with a partial sidecar sub-object. The panel ITSELF applies the change to
// the live three.js object immediately (so the viewport updates without waiting for a sidecar round-trip), then the
// host's onChange writes the same partial to the edit store. The two paths use the SAME numeric/boolean values, so a
// later applyTo() replay reproduces exactly what the user saw.
//
// SELECTION SOURCE NOTE (honesty): selection.js currently raycasts only o.isMesh, so in practice the LIGHT branch is
// reachable only when a host hands this panel a light payload (e.g. a future light-picking lane). The branch is built
// and shape-correct regardless, matching the sidecar light shape. UNVERIFIED on screen (no on-screen truth this run);
// correctness established via node --check + shape review against mapEdits.js applyMaterial/applyLight/applyColorTo.

/* ───────────────────────── color helpers ───────────────────────── */

// THREE.Color.getHex() returns the LINEAR-space hex by default in three 0.165. The sidecar stores whatever number we
// emit and mapEdits replays it via Color.setHex (no color-management arg), i.e. round-trips in the same space. To keep
// the <input type="color"> swatch and the stored value consistent we read/write through getHexString()/setHex without
// a colorSpace arg on BOTH sides — so what the user sees in the swatch is what gets stored and what gets replayed.
function colorToHexString(color) {
  if (color && typeof color.getHexString === "function") {
    return `#${color.getHexString()}`;
  }
  return "#000000";
}

// "#rrggbb" -> 0xRRGGBB number. Returns null on malformed input.
function hexStringToNumber(hex) {
  if (typeof hex !== "string") return null;
  const m = /^#?([0-9a-fA-F]{6})$/.exec(hex.trim());
  if (!m) return null;
  return parseInt(m[1], 16);
}

// First material of an object (array or single). Null when none.
function firstMaterialOf(object) {
  if (!object) return null;
  if (Array.isArray(object.material)) return object.material[0] || null;
  return object.material || null;
}

function allMaterialsOf(object) {
  if (!object) return [];
  if (Array.isArray(object.material)) return object.material.filter(Boolean);
  return object.material ? [object.material] : [];
}

/* ───────────────────────── tiny DOM builders ───────────────────────── */

function elem(tag, className, attrs) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (attrs) {
    for (const k of Object.keys(attrs)) {
      if (k === "text") node.textContent = attrs[k];
      else node.setAttribute(k, attrs[k]);
    }
  }
  return node;
}

// A labeled control row: <div.mlRow><span.mlLabel>label</span><control/></div>
function row(labelText, control) {
  const r = elem("div", "mlRow");
  const label = elem("span", "mlLabel", { text: labelText });
  r.appendChild(label);
  r.appendChild(control);
  return r;
}

/* ───────────────────────── panel factory ───────────────────────── */

// createMaterialPanel({ mount }) -> { show(selectionInfo, { onChange }), clear() }
//   mount         : the HTMLElement container to render controls into (index.html #ffx-material-light-panel body).
//   show(info,h)  : render controls for info.object (a THREE.Mesh or THREE.Light); h.onChange(section, patch) is the
//                   host persist hook — section is "material" | "light", patch is the partial sidecar sub-object.
//                   This module applies the change live FIRST, then calls h.onChange so the host writes the store.
//   clear()       : empty the panel and drop internal references (call on select(null) / map unload).
export function createMaterialPanel(opts) {
  const mount = opts && opts.mount ? opts.mount : null;
  if (!mount) {
    throw new Error("createMaterialPanel: requires { mount }");
  }

  let current = null;     // last selectionInfo shown
  let onChange = null;    // host persist hook (section, patch)

  function setStatus(hasSelection) {
    // Drive a data attribute so CSS can show/hide the empty hint vs the controls if it wants to.
    mount.dataset.hasSelection = hasSelection ? "1" : "0";
  }

  // Persist a partial through the host hook (if a key exists). Always applied live regardless of key presence.
  function persist(section, patch) {
    if (typeof onChange === "function") {
      try {
        onChange(section, patch);
      } catch (err) {
        console.warn("materialLightPanel onChange threw:", err?.message || err);
      }
    }
  }

  /* ── live appliers (mirror mapEdits.applyMaterial / applyLight exactly) ── */

  function liveApplyMaterial(object, patch) {
    if (!object) return;
    if (typeof patch.visible === "boolean") object.visible = patch.visible;
    for (const mat of allMaterialsOf(object)) {
      if (patch.color !== undefined && patch.color !== null && mat.color && typeof mat.color.setHex === "function") {
        mat.color.setHex(patch.color);
      }
      if (patch.emissive !== undefined && patch.emissive !== null && mat.emissive && typeof mat.emissive.setHex === "function") {
        mat.emissive.setHex(patch.emissive);
      }
      if (typeof patch.opacity === "number" && Number.isFinite(patch.opacity)) mat.opacity = patch.opacity;
      if (typeof patch.transparent === "boolean") mat.transparent = patch.transparent;
      if (typeof patch.wireframe === "boolean") mat.wireframe = patch.wireframe;
      mat.needsUpdate = true;
    }
  }

  function liveApplyLight(object, patch) {
    if (!object || !object.isLight) return;
    if (patch.color !== undefined && patch.color !== null && object.color && typeof object.color.setHex === "function") {
      object.color.setHex(patch.color);
    }
    if (typeof patch.intensity === "number" && Number.isFinite(patch.intensity)) object.intensity = patch.intensity;
  }

  // Apply a material/light change live, then persist. `section` selects the lane.
  function change(section, patch) {
    if (!current || !current.object) return;
    if (section === "material") {
      liveApplyMaterial(current.object, patch);
    } else if (section === "light") {
      liveApplyLight(current.object, patch);
    }
    persist(section, patch);
  }

  /* ── control builders ── */

  function colorControl(initialColor, onPick) {
    const input = elem("input", "mlColor", { type: "color" });
    input.value = colorToHexString(initialColor);
    input.addEventListener("input", () => {
      const num = hexStringToNumber(input.value);
      if (num === null) return;
      onPick(num);
    });
    return input;
  }

  function rangeControl({ min, max, step, value }, onSlide) {
    const wrap = elem("div", "mlRange");
    const range = elem("input", null, { type: "range", min: String(min), max: String(max), step: String(step) });
    range.value = String(value);
    const out = elem("output", "mlRangeOut", { text: Number(value).toFixed(2) });
    range.addEventListener("input", () => {
      const v = Number(range.value);
      out.textContent = Number.isFinite(v) ? v.toFixed(2) : "-";
      onSlide(v);
    });
    wrap.appendChild(range);
    wrap.appendChild(out);
    return wrap;
  }

  function checkboxControl(checked, onToggle) {
    const input = elem("input", "mlCheck", { type: "checkbox" });
    input.checked = Boolean(checked);
    input.addEventListener("change", () => onToggle(input.checked));
    return input;
  }

  /* ── renderers ── */

  function renderMesh(info) {
    const object = info.object;
    const mat = firstMaterialOf(object);

    const frag = document.createDocumentFragment();

    const heading = elem("p", "mlHeading", {
      text: `Mesh: ${info.materialName || object.name || (mat && mat.name) || "submesh"}`,
    });
    frag.appendChild(heading);

    if (!info.key) {
      frag.appendChild(elem("p", "mlNote", { text: "No stable sidecar key — changes apply live but will NOT persist." }));
    }

    if (!mat) {
      frag.appendChild(elem("p", "mlNote", { text: "This mesh has no editable material." }));
      // Still allow visibility toggle (object-level, no material needed).
      frag.appendChild(
        row("Visible", checkboxControl(object.visible, (v) => change("material", { visible: v }))),
      );
      return frag;
    }

    // Color (diffuse / baseColor on MeshBasicMaterial too).
    if (mat.color && typeof mat.color.setHex === "function") {
      frag.appendChild(
        row("Color", colorControl(mat.color, (num) => change("material", { color: num }))),
      );
    }

    // Emissive — only when the material model supports it (FFX maps are unlit MeshBasicMaterial which has NO .emissive,
    // so this row is intentionally hidden for that dataset rather than throwing).
    if (mat.emissive && typeof mat.emissive.setHex === "function") {
      frag.appendChild(
        row("Emissive", colorControl(mat.emissive, (num) => change("material", { emissive: num }))),
      );
    }

    // Transparent toggle (declared before the opacity row so the opacity slider can flip it on; kept as its own
    // control so the user can force transparency independently of opacity).
    const transparentInput = checkboxControl(mat.transparent, (v) => change("material", { transparent: v }));

    // Opacity (0..1). Wire it together with the transparent flag: any opacity < 1 needs transparent=true to show,
    // so when the user pulls opacity below 1 we also flip transparent on (and persist BOTH), matching how a viewer
    // user expects fade to behave. We do NOT auto-clear transparent at opacity 1 (user may have set it on purpose).
    const opacityValue = typeof mat.opacity === "number" && Number.isFinite(mat.opacity) ? mat.opacity : 1;
    frag.appendChild(
      row(
        "Opacity",
        rangeControl({ min: 0, max: 1, step: 0.01, value: opacityValue }, (v) => {
          const patch = { opacity: v };
          if (v < 1 && !mat.transparent) {
            patch.transparent = true;
            transparentInput.checked = true;
          }
          change("material", patch);
        }),
      ),
    );

    frag.appendChild(row("Transparent", transparentInput));

    // Wireframe.
    frag.appendChild(
      row("Wireframe", checkboxControl(mat.wireframe, (v) => change("material", { wireframe: v }))),
    );

    // Visible (object-level; stored in the material sidecar section per the c2 shape).
    frag.appendChild(
      row("Visible", checkboxControl(object.visible, (v) => change("material", { visible: v }))),
    );

    return frag;
  }

  function renderLight(info) {
    const object = info.object;
    const frag = document.createDocumentFragment();

    frag.appendChild(elem("p", "mlHeading", { text: `Light: ${object.name || object.type || "light"}` }));

    if (!info.key) {
      frag.appendChild(elem("p", "mlNote", { text: "No stable sidecar key — changes apply live but will NOT persist." }));
    }

    if (object.color && typeof object.color.setHex === "function") {
      frag.appendChild(
        row("Color", colorControl(object.color, (num) => change("light", { color: num }))),
      );
    }

    const intensityValue = typeof object.intensity === "number" && Number.isFinite(object.intensity) ? object.intensity : 1;
    // Light intensity has no fixed upper bound; use a generous slider ceiling but never below the current value.
    const ceiling = Math.max(4, Math.ceil(intensityValue) + 1);
    frag.appendChild(
      row(
        "Intensity",
        rangeControl({ min: 0, max: ceiling, step: 0.05, value: intensityValue }, (v) => change("light", { intensity: v })),
      ),
    );

    return frag;
  }

  /* ── public surface ── */

  // Render controls for the picked object. `selectionInfo` is the selection.js onPick payload (or null to clear).
  function show(selectionInfo, handlers) {
    onChange = handlers && typeof handlers.onChange === "function" ? handlers.onChange : null;
    current = selectionInfo || null;

    mount.innerHTML = "";

    const object = current && current.object;
    if (!object) {
      setStatus(false);
      mount.appendChild(elem("p", "mlNote", { text: "Select a submesh (orbit mode) to edit its material." }));
      return;
    }

    setStatus(true);
    if (object.isLight) {
      mount.appendChild(renderLight(current));
    } else {
      mount.appendChild(renderMesh(current));
    }
  }

  function clear() {
    current = null;
    onChange = null;
    mount.innerHTML = "";
    setStatus(false);
    mount.appendChild(elem("p", "mlNote", { text: "Select a submesh (orbit mode) to edit its material." }));
  }

  // Initial empty state.
  clear();

  return { show, clear };
}
