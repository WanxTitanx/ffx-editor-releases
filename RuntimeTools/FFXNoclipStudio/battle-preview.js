// Aurora composition controls over the pinned NoClip actor renderer. Game assets are
// loaded by the existing local data fetcher; this addon does not supply game content.
const finitePoint = value => value?.length === 3 && Array.from(value).every(Number.isFinite);

// Steam FFX.exe SHA0537b2a1…: camera depth setter7BFBD0, projection7BC980.
// Native512x448 aperture / scales0.8 and14/15 yields horizontal half-width320.
// HD preserves that horizontal field: 4:3 uses vertical240;16:9 uses180.
// Recompute for every depth, not a list of guessed per-battle zoom presets.
export function nativeBattleProjection(screenDepth, aspect) {
  if (!Number.isFinite(screenDepth) || screenDepth <= 0 || !Number.isFinite(aspect) || aspect <= 0) return null;
  const halfWidth = 320, halfHeight = halfWidth / aspect;
  const fovY = 2 * Math.atan(halfHeight / screenDepth);
  return Number.isFinite(fovY) && fovY > 0 && fovY < Math.PI
    ? {fovY, aspect, scaleX:screenDepth/halfWidth, scaleY:screenDepth/halfHeight} : null;
}

export function readBattlePreviewRequest(search) {
  const params = new URLSearchParams(search);
  const integer = (key, max) => {
    const raw = params.get(key);
    if (!raw || !/^\d+$/.test(raw)) return null;
    const value = Number(raw);
    return Number.isSafeInteger(value) && value <= max ? value : null;
  };
  const kind = params.get('mapKind') ?? 'battle';
  if (kind !== 'field' && kind !== 'battle') return null;
  const encounter = integer('battle', 0xffff), map = integer('map', kind === 'field' ? 0x3ff : 0xff);
  return encounter === null || map === null ? null : {encounter, map, kind};
}

export function selectBattlePreviewEncounter(renderer, location) {
  if (location.hash !== '#ffx/battle-preview' && !location.hash.startsWith('#ffx/battle-preview;')) return false;
  const request = readBattlePreviewRequest(location.search);
  if (!request) return false;
  renderer.shatter.enc = request.encounter;
  // The descriptor already mounted the project's explicit field or arena.
  // Reconsulting the PS2 random table can change that scene or reject a boss.
  renderer.shatter.map = -1;
  return true;
}

export function battlePointToWorld(matrix, point) {
  if (!matrix || matrix.length !== 16 || !finitePoint(point)) return null;
  const w = matrix[3] * point[0] + matrix[7] * point[1] + matrix[11] * point[2] + matrix[15];
  if (!Number.isFinite(w) || Math.abs(w) < 1e-8) return null;
  const result = [0, 1, 2].map(row =>
    (matrix[row] * point[0] + matrix[row + 4] * point[1] + matrix[row + 8] * point[2] + matrix[row + 12]) / w);
  return finitePoint(result) ? result : null;
}

export function battleCameraMatrix(eye, target) {
  if (!finitePoint(eye) || !finitePoint(target)) return null;
  const normalize = v => { const n = Math.hypot(...v); return n > 1e-7 ? v.map(x => x / n) : null; };
  const cross = (a, b) => [a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0]];
  const z = normalize(eye.map((x, i) => x - target[i]));
  if (!z) return null;
  const x = normalize(cross(Math.abs(z[1]) > .999 ? [0, 0, 1] : [0, 1, 0], z));
  const y = cross(z, x);
  return [...x, 0, ...y, 0, ...z, 0, ...eye, 1];
}

export function formationCamera(party, monsters) {
  const points = [...party, ...monsters].filter(finitePoint);
  if (!points.length) return null;
  const average = group => [0, 1, 2].map(axis => group.reduce((sum, p) => sum + p[axis], 0) / group.length);
  const target = average(points);
  const front = party.filter(finitePoint), back = monsters.filter(finitePoint);
  const p = front.length ? average(front) : [target[0], target[1], target[2] - 50];
  const m = back.length ? average(back) : target;
  const dx = p[0] - m[0], dz = p[2] - m[2], length = Math.hypot(dx, dz);
  const forward = length > 1e-6 ? [dx / length, dz / length] : [0, -1];
  const radius = Math.max(30, ...points.map(point => Math.hypot(point[0] - target[0], point[2] - target[2])));
  const distance = radius * 2.4;
  // FFX's up direction is negative Y. The caller applies the renderer's own
  // FFXToNoclip matrix, including its scale, before creating a camera matrix.
  target[1] -= 12;
  return { eye: [target[0] + distance * (.8 * forward[0] - .6 * forward[1]),
    target[1] - radius * .8, target[2] + distance * (.8 * forward[1] + .6 * forward[0])], target };
}

export function idleAnimation(model) {
  // The renderer uses group 1 / entry 0x10 as battle idle. Characters also have
  // a field idle at group 0 / entry 0, used only when battle idle is absent.
  for (const [group, index] of [[1, 0x10], [0, 0]]) {
    const id = model?.defaultAnimations?.[group]?.[index];
    if (Number.isInteger(id) && id > 0) return { group, index, id, resource: id >>> 16 };
  }
  return null;
}

export async function loadIdleAnimation(model, objects) {
  for (const [group, index] of [[1, 0x10], [0, 0]]) {
    const id = model?.defaultAnimations?.[group]?.[index];
    if (!Number.isInteger(id) || id <= 0) continue;
    const resourceId = id >>> 16, resource = objects.ensureActorResource(resourceId);
    const present = () => resource.animations.some(animation => animation.id === (id & 0xffff));
    // Monster battle animations normally live in packet 1; c001's shipped local
    // corpus also uses packet 0. Resolve a declared idle from its actual packet,
    // never substitute an arbitrary animation or report a bind pose as animated.
    for (const packet of [1, 0]) {
      if (present()) break;
      resource.fetched &= ~(1 << (packet + 1));
      await objects.loadActorResource(resourceId, packet);
    }
    if (present()) return { group, index, id, resource: resourceId };
  }
  throw new Error("Tidus idle animation packet is unavailable");
}

export function cameraFramesFormation(matrix, projection, points) {
  if (!matrix || !projection || !points.length) return false;
  return points.every(point => {
    if (!finitePoint(point)) return false;
    const delta = point.map((n, i) => n - matrix[12 + i]);
    const dot = column => delta.reduce((sum, n, i) => sum + n * matrix[column + i], 0);
    const depth = -dot(8);
    return depth > .1 && Math.abs(dot(0) * projection[0] / depth) < .82 &&
      Math.abs(dot(4) * projection[5] / depth) < .82;
  });
}

export function createBattlePreview() {
  let scene, battle, frame, transform, makeActor, session;
  let party = [], shown = false, state = "off", generation = 0;
  let cameraLocked = false, cameraWorld, freeWorld, freeLens, selectedParty = -1, cameraUsesFile = false;

  function releaseActors() {
    generation++;
    for (const actor of party) actor.destroy(scene.levelObjects.cache.device);
    party = [];
    state = "off";
  }

  async function loadParty() {
    if (!scene || !session || !shown || state !== "off") return;
    if (!session.party?.length) { state = "missing"; return; }
    const owner = scene, expectedBattle = battle, ticket = generation;
    state = "loading";
    try {
      await owner.loadAndParseActorModel(1); // BattleModelCatalogBridge: c001 = Tidus.
      if (ticket !== generation || owner !== scene || expectedBattle !== battle) return;
      const model = owner.levelObjects.actorResources.get(1)?.model;
      if (!model) throw new Error("Tidus model is missing");
      // Event scripts may mark a PC packet fetched while the field-only filter
      // rejects it. loadIdleAnimation retries only the declared idle's packets.
      const idle = await loadIdleAnimation(model, owner.levelObjects);
      if (ticket !== generation || owner !== scene || expectedBattle !== battle) return;
      const positions = session.party.slice(0, 3);
      for (const position of positions) {
        const actor = makeActor(1);
        // No field pathfinding or root movement: these are authored battle anchors.
        actor.pos.set([position.x, position.y, position.z]);
        actor.prevPos.set(actor.pos);
        actor.flags |= 0x80;
        actor.floorMode = 0;
        actor.visible = shown;
        actor.auroraIdle = idle;
        party.push(actor);
        actor.animation.defaultLoops = 1;
        actor.animation.setFromList(owner.levelObjects, idle.group, idle.index);
        actor.animation.isLoopLike = true;
        if (!actor.animation.running) throw new Error("Tidus idle animation could not be resolved");
        actor.animation.t = actor.animation.start + (actor.animation.end - actor.animation.start) * position.slot / 3;
      }
      state = "ready";
    } catch (error) {
      if (ticket !== generation) return;
      for (const actor of party) actor.destroy(owner.levelObjects.cache.device);
      party = [];
      state = "error";
      console.warn("Aurora party preview:", error.message);
    }
  }

  function applyCamera(matrix) {
    if (!frame?.camera || !matrix) return;
    for (let i = 0; i < 16; i++) frame.camera.worldMatrix[i] = matrix[i];
    frame.camera.worldMatrixUpdated();
  }

  function applyNativeZoom() {
    const camera = frame?.camera;
    if (!cameraUsesFile || !camera?.setPerspective) return;
    const projection = nativeBattleProjection(session?.camera?.screenDepth, camera.aspect);
    if (!projection) return;
    camera.setPerspective(projection.fovY, projection.aspect,
      Number.isFinite(camera.near) && camera.near > 0 ? camera.near : .5,
      camera.far ?? Infinity);
  }

  function resolveCamera() {
    if (!transform || !battle) return null;
    const partyPositions = party.length ? party.map(actor => Array.from(actor.pos)) :
      session?.party?.map(p => [p.x, p.y, p.z]) ?? [];
    const monsterPositions = battle.monsters.map(m => Array.from(m.pos));
    const fromView = view => view && battleCameraMatrix(
      battlePointToWorld(transform, view.eye), battlePointToWorld(transform, view.target));
    const fileMatrix = fromView(session?.camera);
    // Eye/reference are resolved together inside one ATEL camera entrypoint.
    // Retain the authored angle even if newly added actors extend past its frame.
    cameraUsesFile = !!fileMatrix;
    if (cameraUsesFile) return fileMatrix;
    const view = formationCamera(partyPositions, monsterPositions);
    if (!view) return null;
    return fromView(view);
  }

  return {
    configure(value) { session = value; cameraWorld = null; },
    allowsActor: id => id === 1 && !!scene && (shown || state === "loading" || state === "ready"),
    prepare(owner, input, worldFromBattle, factory) {
      if (!owner.battleState) return;
      if (scene !== owner || battle !== owner.battleState) {
        releaseActors();
        scene = owner;
        battle = owner.battleState;
        cameraWorld = null;
      }
      frame = input; transform = worldFromBattle; makeActor = factory;
      if (shown && state === "off") void loadParty();
      if (cameraLocked) { cameraWorld ??= resolveCamera(); applyCamera(cameraWorld); applyNativeZoom(); }
    },
    showParty(value) {
      shown = !!value;
      for (const actor of party) actor.visible = shown;
      if (shown && state === "error") state = "off"; // An explicit retry after assets become available.
      if (shown) void loadParty();
    },
    setBattleView(value) {
      if (value && !cameraLocked) {
        freeWorld = frame?.camera ? Array.from(frame.camera.worldMatrix) : null;
        const camera=frame?.camera;
        freeLens=camera&&Number.isFinite(camera.fovY)&&Number.isFinite(camera.aspect)
          ? {fovY:camera.fovY,aspect:camera.aspect,near:camera.near,far:camera.far,
            isOrthographic:camera.isOrthographic,left:camera.left,right:camera.right,bottom:camera.bottom,top:camera.top} : null;
        cameraWorld = resolveCamera();
      }
      cameraLocked = !!value;
      if (cameraLocked) { applyCamera(cameraWorld); applyNativeZoom(); }
      else {
        const camera=frame?.camera;
        if(camera&&freeLens){
          if(freeLens.isOrthographic&&camera.setFrustum){
            camera.isOrthographic=true;camera.fovY=freeLens.fovY;camera.aspect=freeLens.aspect;
            camera.setFrustum(freeLens.left,freeLens.right,freeLens.bottom,freeLens.top,freeLens.near,freeLens.far);
          }else camera.setPerspective?.(freeLens.fovY,freeLens.aspect,freeLens.near,freeLens.far);
        }
        applyCamera(freeWorld); freeWorld = freeLens = null;
      }
      return cameraLocked;
    },
    isBattleView: () => cameraLocked,
    hasFileCamera: () => cameraUsesFile,
    nativeZoom: () => cameraUsesFile ? nativeBattleProjection(session?.camera?.screenDepth, frame?.camera?.aspect) : null,
    partyState: () => shown ? state : "off",
    partyActors: () => party,
    selectParty(slot) { selectedParty = slot; },
    render(owner, device, delta, renderInstManager, input, drawLabel) {
      if (owner !== scene || !shown || state !== "ready") return;
      const monsters = battle.monsters;
      const target = monsters.length ? [0, 2].map(axis => monsters.reduce((sum, m) => sum + m.pos[axis], 0) / monsters.length) : null;
      for (let slot = 0; slot < party.length; slot++) {
        const actor = party[slot];
        if (target) actor.heading = actor.targetHeading = Math.atan2(target[1] - actor.pos[2], target[0] - actor.pos[0]);
        if (!actor.animation.running) {
          actor.animation.defaultLoops = 1;
          actor.animation.nextTransition = -1;
          actor.animation.setFromList(owner.levelObjects, actor.auroraIdle.group, actor.auroraIdle.index);
          actor.animation.isLoopLike = true;
        }
        actor.render(device, owner.levelObjects, delta, renderInstManager, input);
        if (slot === selectedParty) drawLabel?.(actor.pos, session.party[slot].label);
      }
    },
    dispose(owner) {
      if (owner !== scene) return;
      releaseActors(); scene = battle = frame = null;
    },
  };
}

if (typeof window !== "undefined") window.ffxAuroraBattlePreview = Object.assign(createBattlePreview(), {
  request: () => readBattlePreviewRequest(window.location.search),
  selectEncounter: renderer => selectBattlePreviewEncounter(renderer, window.location),
});
