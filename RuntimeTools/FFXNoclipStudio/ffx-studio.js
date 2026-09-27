// Spira Reforge Studio integration. The upstream NoClip renderer and MIT attribution remain intact.
export function collectPositions(monsters) {
  if (!Array.isArray(monsters) || monsters.length < 1 || monsters.length > 8)
    throw new Error("Invalid monster count");
  return monsters.map((monster, slot) => {
    const x = Number(monster.pos[0]), z = Number(monster.pos[2]);
    if (![x, z].every(value => Number.isFinite(value) && Math.abs(value) <= 1_000_000))
      throw new Error("Invalid position");
    return { slot, x, z };
  });
}

export function bridgeAddress(origin, route) {
  const url = new URL(origin);
  if (url.protocol !== "http:" || !["127.0.0.1", "localhost", "[::1]"].includes(url.hostname) ||
      !["/positions", "/position-session"].includes(route)) throw new Error("Invalid bridge");
  url.pathname = "/api/aurora" + route;
  url.search = "";
  url.hash = "";
  return url.href;
}

export function hasPositionChanges(monsters, actors) {
  return monsters.some((m, i) => !actors[i] ||
    Math.fround(m.pos[0]) !== Math.fround(actors[i].x) ||
    Math.fround(m.pos[2]) !== Math.fround(actors[i].z));
}

export function applyCoordinate(monster, axis, value) {
  const coordinate = Number(value);
  if (!["X", "Z"].includes(axis) || value === "" || !Number.isFinite(coordinate) || Math.abs(coordinate) > 1_000_000) return false;
  monster.pos[axis === "X" ? 0 : 2] = coordinate;
  return true;
}

// NoClip supplies its live clip matrix and FFX→viewer transform. Work in CSS pixels,
// keep the camera snapshot fixed during the gesture, and preserve the actor's Y.
export function invertMatrix4(matrix) {
  if (!matrix || matrix.length !== 16 || !Array.from(matrix).every(Number.isFinite)) return null;
  const rows = Array.from({length:4}, (_, r) => Array.from({length:8}, (_, c) => c < 4 ? matrix[c * 4 + r] : Number(c - 4 === r)));
  for (let c = 0; c < 4; c++) {
    let pivot = c;
    for (let r = c + 1; r < 4; r++) if (Math.abs(rows[r][c]) > Math.abs(rows[pivot][c])) pivot = r;
    if (Math.abs(rows[pivot][c]) < 1e-12) return null;
    [rows[c], rows[pivot]] = [rows[pivot], rows[c]];
    const divisor = rows[c][c];
    rows[c] = rows[c].map(v => v / divisor);
    for (let r = 0; r < 4; r++) if (r !== c) {
      const factor = rows[r][c];
      rows[r] = rows[r].map((v, i) => v - factor * rows[c][i]);
    }
  }
  return Array.from({length:16}, (_, i) => rows[i % 4][4 + Math.floor(i / 4)]);
}

function transformPoint(matrix, point) {
  const v = Array.from({length:4}, (_, r) => matrix[r] * point[0] + matrix[r + 4] * point[1] + matrix[r + 8] * point[2] + matrix[r + 12]);
  if (!v.every(Number.isFinite) || Math.abs(v[3]) < 1e-12) return null;
  return v.slice(0,3).map(n => n / v[3]);
}

export function createPlaneDrag(clipFromWorld, worldFromBattle, anchor, rect, pointer) {
  const clipInverse = invertMatrix4(clipFromWorld), battleInverse = invertMatrix4(worldFromBattle);
  if (!clipInverse || !battleInverse || !anchor || anchor.length < 3) return null;
  const worldAnchor = transformPoint(worldFromBattle, anchor);
  if (!clipInverse || !battleInverse || !worldAnchor || rect.width <= 0 || rect.height <= 0) return null;
  const pointAt = p => {
    const x = (p.x - rect.left) / rect.width * 2 - 1, y = 1 - (p.y - rect.top) / rect.height * 2;
    // NoClip uses an infinite far plane. Unprojecting a clip endpoint can have W=0;
    // two interior depths define the same ray while keeping both points finite.
    const near = transformPoint(clipInverse, [x,y,0.25]), far = transformPoint(clipInverse, [x,y,0.75]);
    if (!near || !far || Math.abs(far[1] - near[1]) < 1e-8) return null;
    const t = (worldAnchor[1] - near[1]) / (far[1] - near[1]);
    return transformPoint(battleInverse, near.map((v,i) => v + (far[i] - v) * t));
  };
  const start = pointAt(pointer);
  if (!start) return null;
  const original = Array.from(anchor);
  return p => {
    const next = pointAt(p);
    if (!next) return null;
    const position = {x: original[0] + next[0] - start[0], z: original[2] + next[2] - start[2]};
    return Object.values(position).every(v => Number.isFinite(v) && Math.abs(v) <= 1_000_000) ? position : null;
  };
}

if (typeof document !== "undefined") boot();

function boot() {
  const params = new URLSearchParams(location.search);
  const pt = params.get("lang") === "pt";
  const battleId = params.get("auroraBattle");
  const token = params.get("bridgeToken");
  const nativeMode = params.get("edit") === "1" && !!battleId && !!token;
  let controller, session, panel, select, xInput, zInput, saveButton, resetButton, status;
  let loading = false, busy = false, selected = -1, dirty = false;
  let frame, worldFromBattle, dragging = null, moveMode = true, moveButton, cameraButton;
  const styledMenus = new WeakSet();
  const menuSize = new ResizeObserver(entries => {
    for (const entry of entries)
      entry.target.dataset.ffxExpanded = String(entry.contentRect.width > 60);
  });

  function decorateNativeMenus() {
    for (const header of document.querySelectorAll("h1")) {
      if (!header.querySelector(":scope > svg")) continue;
      const panel = header.parentElement?.parentElement;
      if (!panel || styledMenus.has(panel)) continue;
      panel.dataset.ffxStudioPanel = "true";
      header.parentElement.dataset.ffxStudioHeaderShell = "true";
      header.dataset.ffxStudioHeader = "true";
      if (panel.parentElement) panel.parentElement.dataset.ffxStudioMenuGroup = "true";
      styledMenus.add(panel);
      menuSize.observe(panel);
    }
  }


  function decorate() {
    decorateNativeMenus();
    const focusedStudio = /^#ffx\/(battle-preview|monster-studio|magic-studio)(?:;|$)/.test(location.hash);
    document.body.classList.toggle("ffx-aurora-preview", focusedStudio);
    const games = [...document.querySelectorAll("h1")].find(e => e.textContent.trim() === "Games");
    if (games) games.parentElement.parentElement.parentElement.dataset.ffxGamesPanel = "true";
    const about = document.getElementById("About");
    if (about && !document.getElementById("ffx-studio-about")) {
      const section = document.createElement("section");
      section.id = "ffx-studio-about";
      const brand = document.createElement("div");
      brand.className = "ffx-studio-brand";
      const logo = document.createElement("img");
      logo.src = "/noclip/spira-reforge-studio-256.png";
      logo.alt = "Spira Reforge Studio";
      const title = document.createElement("h2");
      title.textContent = "Spira Reforge Studio";
      brand.append(logo, title);
      const description = document.createElement("p");
      description.textContent = pt
        ? "Integração Aurora: cenários de Final Fantasy X e edição das posições dos monstros no projeto."
        : "Aurora integration: Final Fantasy X scenes and monster placement saved to your project.";
      const repo = document.createElement("a");
      repo.href = "https://github.com/WanxTitanx/ffx-editor-main";
      repo.target = "_blank";
      repo.rel = "noopener noreferrer";
      repo.textContent = "WanxTitanx / ffx-editor-main";
      const credit = document.createElement("p");
      credit.className = "ffx-upstream-credit";
      credit.textContent = pt ? "Baseado em noclip.website — licença MIT. Créditos originais abaixo." : "Based on noclip.website — MIT license. Original credits below.";
      section.append(brand, description, repo, credit);
      about.prepend(section);
    }
    return !!games && !!about;
  }
  const observer = new MutationObserver(() => { if (decorate()) observer.disconnect(); });
  observer.observe(document.body, { childList: true, subtree: true });
  if (decorate()) observer.disconnect();
  window.addEventListener("hashchange", decorate);
  // NoClip clones About into a flyout. Bound both copies after layout so the added
  // credits remain reachable even in the editor's smaller embedded viewer window.
  let aboutFitPending = false;
  const scrollableAbout = new WeakSet();
  function fitAbout() {
    aboutFitPending = false;
    decorateNativeMenus();
    for (const about of document.querySelectorAll("#About")) {
      const bounds = about.getBoundingClientRect();
      if (!bounds.width || !bounds.height) continue;
      const root = about.closest("#Panel");
      const bottomInset = Math.max(16, root ? parseFloat(getComputedStyle(root).paddingBottom) : 0) + 4;
      about.style.maxHeight = `${Math.max(64, innerHeight - bounds.top - bottomInset)}px`;
      about.style.overflowY = "auto";
      about.style.boxSizing = "border-box";
      about.tabIndex = 0;
      if (!scrollableAbout.has(about)) {
        about.addEventListener("wheel", e => e.stopPropagation());
        scrollableAbout.add(about);
      }
    }
  }
  function scheduleAboutFit() {
    if (!aboutFitPending) { aboutFitPending = true; requestAnimationFrame(fitAbout); }
  }
  new MutationObserver(scheduleAboutFit).observe(document.body, { childList: true, subtree: true });
  window.addEventListener("resize", scheduleAboutFit);

  async function post(route, payload) {
    const response = await fetch(bridgeAddress(location.origin, route), {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-FFX-Studio-Token": token },
      body: JSON.stringify(payload),
    });
    const result = await response.json();
    if (!response.ok || result.ok === false) throw new Error(result.message || session?.text.error || "Save failed");
    return result;
  }

  function element(tag, text, parent) {
    const e = document.createElement(tag);
    if (text) e.textContent = text;
    if (parent) parent.appendChild(e);
    return e;
  }

  async function mount() {
    loading = true;
    panel = element("section", null, document.body);
    panel.id = "ffx-position-editor";
    panel.setAttribute("aria-label", pt ? "Posições dos monstros" : "Monster positions");
    panel.addEventListener("pointerdown", e => e.stopPropagation());
    panel.addEventListener("keydown", e => { if (!(e.ctrlKey || e.metaKey)) e.stopPropagation(); });
    panel.addEventListener("keyup", e => e.stopPropagation());
    status = element("p", pt ? "Conectando ao editor…" : "Connecting to editor…", panel);
    status.setAttribute("role", "status");
    try {
      session = await post("/position-session", { battleId });
      const text = session.text;
      panel.replaceChildren();
      const heading = element("div", null, panel);
      heading.className = "ffx-position-heading";
      const title = element("h2", text.title, heading);
      const collapse = element("button", "−", heading);
      collapse.type = "button";
      collapse.setAttribute("aria-label", text.title);
      collapse.setAttribute("aria-expanded", "true");
      const reveal = element("div", null, panel);
      reveal.className = "ffx-position-reveal";
      const body = element("div", null, reveal);
      body.className = "ffx-position-body";
      collapse.onclick = () => {
        const collapsed = panel.classList.toggle("is-collapsed");
        body.inert = collapsed;
        reveal.setAttribute("aria-hidden", String(collapsed));
        collapse.textContent = collapsed ? "+" : "−";
        collapse.setAttribute("aria-expanded", String(!collapsed));
      };
      element("p", battleId, body).className = "ffx-position-battle";
      element("p", text.hint, body);
      const modes = element("div", null, body);
      modes.className = "ffx-position-modes";
      moveButton = element("button", text.moveMode, modes);
      cameraButton = element("button", text.cameraMode, modes);
      for (const button of [moveButton, cameraButton]) button.type = "button";
      moveButton.onclick = () => setMoveMode(true);
      cameraButton.onclick = () => setMoveMode(false);
      setMoveMode(true);
      const label = element("label", text.monster, body);
      select = element("select", null, label);
      for (const actor of session.actors) {
        const option = element("option", `${actor.slot + 1} · ${actor.label}`, select);
        option.value = actor.slot;
      }
      select.onchange = () => { finishDrag(); controller.editSelectedSlot = Number(select.value); sync(); };
      const coords = element("div", null, body);
      coords.className = "ffx-position-coords";
      const number = name => {
        const label = element("label", name, coords);
        const input = element("input", null, label);
        input.type = "number"; input.step = "any"; input.min = "-1000000"; input.max = "1000000";
        input.addEventListener("input", () => { input.dataset.pending = "true"; commitNumbers(); });
        input.addEventListener("change", commitNumbers);
        return input;
      };
      xInput = number("X"); zInput = number("Z");
      const actions = element("div", null, body);
      actions.className = "ffx-position-actions";
      saveButton = element("button", text.save, actions);
      saveButton.type = "button"; saveButton.onclick = save;
      resetButton = element("button", text.reset, actions);
      resetButton.type = "button"; resetButton.onclick = resetSelection;
      status = element("p", text.ready, body);
      status.setAttribute("role", "status");
      element("p", text.advanced, body).className = "ffx-position-note";
      controller.editSelectedSlot = controller.editSelectedSlot >= 0 ? controller.editSelectedSlot : 0;
      sync();
    } catch (error) {
      status.textContent = `${pt ? "Reabra a prévia pelo editor." : "Reopen this preview from the editor."} ${error.message}`;
    }
  }

  function commitNumbers() {
    if (busy || !session || selected < 0) return;
    if (!xInput.checkValidity() || !zInput.checkValidity()) { xInput.reportValidity(); zInput.reportValidity(); return; }
    const monster = controller.battleState.monsters[selected];
    for (const [axis, input] of [["X", xInput], ["Z", zInput]]) {
      if (input.dataset.pending === "true" && applyCoordinate(monster, axis, input.value))
        delete input.dataset.pending;
    }
    sync();
  }

  function sync() {
    if (!session || !controller?.battleState || !select || busy) return;
    selected = controller.editSelectedSlot;
    const monster = controller.battleState.monsters[selected];
    select.value = String(selected);
    xInput.disabled = zInput.disabled = resetButton.disabled = !monster;
    if (monster) {
      if (document.activeElement !== xInput && xInput.dataset.pending !== "true") xInput.value = Number(monster.pos[0].toFixed(3));
      if (document.activeElement !== zInput && zInput.dataset.pending !== "true") zInput.value = Number(monster.pos[2].toFixed(3));
    }
    const changed = hasPositionChanges(controller.battleState.monsters, session.actors);
    if (changed !== dirty) status.textContent = changed ? session.text.changed : session.text.ready;
    dirty = changed;
    saveButton.disabled = !dirty;
  }

  function resetSelection() {
    if (busy || !session || selected < 0) return;
    const actor = session.actors[selected], monster = controller.battleState.monsters[selected];
    if (!actor || !monster) return;
    monster.pos[0] = actor.x; monster.pos[2] = actor.z;
    xInput.blur(); zInput.blur(); sync();
  }

  async function save() {
    if (busy || !session || !controller || !window.ffxAuroraPositionEditor.canSaveProject()) return;
    finishDrag();
    if (!xInput.checkValidity() || !zInput.checkValidity() || xInput.value === "" || zInput.value === "") {
      xInput.reportValidity(); zInput.reportValidity(); return;
    }
    commitNumbers();
    busy = true;
    controller.editMode = false;
    saveButton.disabled = resetButton.disabled = true;
    status.textContent = session.text.saving;
    try {
      const reply = await post("/positions", {
        battleId, revision: session.revision, positions: collectPositions(controller.battleState.monsters),
      });
      status.textContent = reply.message;
      // The file and staged preview now have a new baseline. Reload so flight height and
      // cleared sidecar deltas are applied exactly once; NoClip retains its camera in the hash.
      setTimeout(() => location.reload(), 800);
    } catch (error) {
      status.textContent = `${session.text.error} ${error.message}`;
      busy = false;
      controller.editMode = true;
      saveButton.disabled = false;
      resetButton.disabled = false;
    }
  }

  window.ffxAuroraPositionEditor = {
    canSaveProject: () => nativeMode && location.hash.startsWith("#ffx/battle-preview"),
    attach(scene, input, transform) {
      if (!this.canSaveProject()) return;
      controller = scene;
      frame = input;
      worldFromBattle = transform;
      if (!loading) void mount();
      sync();
    },
    save, resetSelection,
  };

  function setMoveMode(enabled) {
    finishDrag();
    moveMode = enabled;
    moveButton?.setAttribute("aria-pressed", String(enabled));
    cameraButton?.setAttribute("aria-pressed", String(!enabled));
    if (enabled && document.pointerLockElement) document.exitPointerLock();
    for (const canvas of document.querySelectorAll("canvas"))
      if (!canvas.closest("#Panel")) canvas.style.cursor = enabled ? "move" : "";
  }

  function finishDrag() {
    if (!dragging) return;
    const old = dragging; dragging = null;
    if (old.canvas.hasPointerCapture(old.id)) old.canvas.releasePointerCapture(old.id);
    sync();
  }

  document.addEventListener("pointerdown", event => {
    if (!window.ffxAuroraPositionEditor.canSaveProject() || !moveMode || busy || !session ||
        event.button !== 0 || !(event.target instanceof HTMLCanvasElement) || event.target.closest("#Panel")) return;
    const monster = controller?.battleState?.monsters[controller.editSelectedSlot];
    if (!monster || !frame?.camera || !worldFromBattle) return;
    const canvas = event.target;
    const move = createPlaneDrag(Array.from(frame.camera.clipFromWorldMatrix), Array.from(worldFromBattle),
      Array.from(monster.pos), canvas.getBoundingClientRect(), {x:event.clientX,y:event.clientY});
    if (!move) {
      event.preventDefault(); event.stopImmediatePropagation();
      status.textContent = session.text.dragView;
      return;
    }
    // Cancelling pointerdown suppresses compatibility mousedown, so NoClip's camera
    // cannot grab the same left-button gesture. Other buttons remain native controls.
    event.preventDefault(); event.stopImmediatePropagation();
    canvas.setPointerCapture(event.pointerId);
    dragging = {id:event.pointerId,canvas,monster,move};
  }, true);
  document.addEventListener("pointermove", event => {
    if (!dragging || event.pointerId !== dragging.id || busy) return;
    event.preventDefault(); event.stopImmediatePropagation();
    const next = dragging.move({x:event.clientX,y:event.clientY});
    if (next) { dragging.monster.pos[0] = next.x; dragging.monster.pos[2] = next.z; sync(); }
  }, true);
  for (const type of ["pointerup", "pointercancel", "lostpointercapture"])
    document.addEventListener(type, event => {
      if (!dragging || event.pointerId !== dragging.id) return;
      event.preventDefault(); event.stopImmediatePropagation(); finishDrag();
    }, true);
  window.addEventListener("blur", finishDrag);
  window.addEventListener("resize", finishDrag);
  document.addEventListener("keydown", event => {
    if (nativeMode && (event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "s") {
      event.preventDefault(); event.stopImmediatePropagation(); void save();
    }
  }, true);
}
