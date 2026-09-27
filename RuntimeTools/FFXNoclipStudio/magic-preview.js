// PC PPP names to NoClip PS2 opcodes. Derived by matching 19 PC/PS2 tables; see magic-preview-evidence.json.
export const pppOpcodes = {
  "pppAccele": 0,
  "pppAngAccele": 1,
  "pppAngMove": 5,
  "pppAngle": 9,
  "pppColAccele": 3,
  "pppColMove": 7,
  "pppColor": 11,
  "pppDrawMatrix": 19,
  "pppDrawMatrixFront": 20,
  "pppDrawMatrixWood": 48,
  "pppDrawMdl": 21,
  "pppDrawMdl2": 55,
  "pppDrawMdl3": 80,
  "pppDrawMdlSemi3": 94,
  "pppDrawMdlTs": 23,
  "pppDrawMdlTs2": 71,
  "pppDrawMdlTs3": 78,
  "pppDrawShape": 24,
  "pppDrawShapeX": 81,
  "pppDummyFunc": 15,
  "pppKeBornRnd2": 63,
  "pppKeBornRnd3": 62,
  "pppKeBornRnd5": 31,
  "pppKeBornRnd6": 67,
  "pppKeDMat": 44,
  "pppKeDMatFr": 52,
  "pppKeDrct": 57,
  "pppKeGrvEff": 93,
  "pppKeGrvTgt": 92,
  "pppKeLnsArnd": 33,
  "pppKeLnsClm": 34,
  "pppKeLnsCrn": 35,
  "pppKeLnsLp": 32,
  "pppKeLnsLpSft": 47,
  "pppKeMdlDtt": 50,
  "pppKeMdlTfd3": 4103,
  "pppKeMvYpEff": 43,
  "pppKeParMatR": 4099,
  "pppKeShpDtt": 45,
  "pppKeShpTail": 25,
  "pppKeShpTail2": 26,
  "pppKeShpTail2X": 99,
  "pppKeTh": 61,
  "pppKeThRes16": 56,
  "pppKeThRes32": 65,
  "pppKeThRes64": 4104,
  "pppKeThSft": 60,
  "pppKeThTp": 59,
  "pppKeZCrct": 77,
  "pppKeZCrctShp": 79,
  "pppMatrixLoc": 58,
  "pppMatrixScl": 17,
  "pppMatrixXYZ": 16,
  "pppMatrixXZY": 83,
  "pppMatrixYXZ": 115,
  "pppMatrixYZX": 16,
  "pppMatrixZXY": 137,
  "pppMove": 4,
  "pppParMatrix": 18,
  "pppPoint": 8,
  "pppPointAp": 41,
  "pppRandDownFV": 30,
  "pppRandDownIV": 40,
  "pppRandFV": 12,
  "pppRandFloat": 87,
  "pppRandHCV": 85,
  "pppRandIV": 13,
  "pppRandUpFV": 29,
  "pppRandUpIV": 4105,
  "pppSMatrix": 15,
  "pppSRandDownFV": 37,
  "pppSRandFV": 14,
  "pppSRandUpFV": 39,
  "pppScale": 10,
  "pppSclAccele": 2,
  "pppSclMove": 6,
  "pppVertexAp": 27,
  "pppVertexApAt": 116,
  "pppVertexApLc": 28,
  "pppVertexAttend": 114,
  "pppVtMime": 86
};

export function previewSessionPath(id) {
  if (!/^[a-f0-9]{32}$/.test(id ?? "")) throw new Error("Invalid preview session");
  return `/viewer-data/magic-preview/${id}/current.json`;
}

export function previewDescriptor(snapshot, sessionId, catalogue, supportedOpcodes) {
  previewSessionPath(sessionId);
  if (snapshot.format !== "ffx-pc-magic-v1" || !/^[a-f0-9]{64}$/.test(snapshot.revision) ||
      !/^[a-f0-9]{64}$/.test(snapshot.workingRevision ?? snapshot.revision) ||
      snapshot.binaryUrl !== `/viewer-data/magic-preview/${sessionId}/${snapshot.workingRevision ?? snapshot.revision}.bin` ||
      !Number.isInteger(snapshot.magicId) || snapshot.magicId < 0 || snapshot.magicId > 65535 ||
      !Array.isArray(snapshot.headers) || !snapshot.headers.length || snapshot.headers.length > 64 ||
      !snapshot.headers.every(v => Number.isInteger(v) && v >= 0 && v < 64 * 1024 * 1024) ||
      !Number.isInteger(snapshot.particleIndex) || snapshot.particleIndex < 0 || snapshot.particleIndex > 63 ||
      !Array.isArray(snapshot.handlerNames) || snapshot.handlerNames.length > 4096 ||
      !Array.isArray(snapshot.usedHandlers) || !snapshot.usedHandlers.every(i => Number.isInteger(i) && i >= 0 && i < snapshot.handlerNames.length))
    throw new Error("Invalid magic preview resource");
  for (const texture of snapshot.textures ?? []) {
    if (!new RegExp(`^/viewer-data/magic-preview/${sessionId}/[a-f0-9]{64}\\.rgba\\.bin$`).test(texture.url) ||
        ![texture.width,texture.height,texture.sourceWidth,texture.sourceHeight].every(v => Number.isInteger(v) && v > 0 && v <= 8192))
      throw new Error("Invalid preview texture");
  }
  const funcMap = snapshot.handlerNames.map(name => pppOpcodes[name] ?? 65535);
  const unsupported = [...new Set(snapshot.usedHandlers.filter(i => !supportedOpcodes.includes(funcMap[i])).map(i => snapshot.handlerNames[i] ?? `#${i}`))];
  const original = catalogue.find(item => item.main === snapshot.magicId || item.alt === snapshot.magicId);
  return {
    ...original, main: snapshot.magicId, name: String(snapshot.label ?? `magic_${snapshot.magicId}`).slice(0,200),
    studio: {...snapshot, funcMap, unsupported, special: original?.setup, id: snapshot.magicId},
  };
}

if (typeof document !== "undefined") bootMagicPreview();

function bootMagicPreview() {
  const params = new URLSearchParams(location.search);
  const sessionId = params.get("magicPreview"), requestedId = params.get("magic");
  const managed = !!sessionId;
  const pt = params.get("lang") === "pt";
  let currentScene, catalogue, supported, revision, pending = false, panel, message;
  const text = (en,br) => pt ? br : en;
  function status(value, error = false) {
    if (!panel) {
      panel = document.createElement("section");
      panel.id = "ffx-magic-preview";
      const title = document.createElement("strong");
      title.textContent = text("Magic preview", "Prévia da magia");
      message = document.createElement("p"); message.setAttribute("role", "status");
      const replay = document.createElement("button"); replay.type = "button";
      replay.textContent = text("Replay", "Repetir");
      replay.onclick = () => currentScene?.particles?.reset();
      const help = document.createElement("small");
      help.textContent = managed ? text("Particle preview. Edit the DLL, then click Open / update preview. Space replays. Game casting logic is not executed.", "Prévia de partículas. Edite a DLL e clique em Abrir / atualizar prévia. Espaço repete. A lógica de conjuração do jogo não é executada.") : text("Space replays the selected effect.", "Espaço repete o efeito selecionado.");
      panel.append(title, message, replay, help); document.body.append(panel);
    }
    message.textContent = value; panel.dataset.error = String(error);
  }
  async function refresh() {
    if (pending || !currentScene || !managed || !location.hash.startsWith("#ffx/magic-studio")) return;
    pending = true;
    try {
      const response = await fetch(previewSessionPath(sessionId), {cache:"no-store"});
      if (!response.ok) throw new Error(text("Preview session closed or unavailable. Open it again in the editor.", "Sessão encerrada ou indisponível. Abra a prévia novamente no editor."));
      const snapshot = await response.json();
      if (snapshot.revision === revision) return;
      const desc = previewDescriptor(snapshot, sessionId, catalogue, supported);
      status(text("Loading working effect…", "Carregando efeito em edição…"));
      await currentScene.setMagic(desc);
      if (!currentScene.particles?.emitters?.length) throw new Error(text("This resource has no renderable emitters.", "Este recurso não contém emissores renderizáveis."));
      revision = snapshot.revision;
      panel.dataset.revision = revision;
      panel.dataset.magicId = String(snapshot.magicId);
      const limits = [];
      if (desc.studio.unsupported.length) limits.push(text("Unsupported handlers: ", "Handlers sem suporte: ") + desc.studio.unsupported.join(", "));
      if (snapshot.rootCount > 1) limits.push(text("Showing the first particle resource.", "Exibindo o primeiro recurso de partículas."));
      if (snapshot.textures?.length) limits.push(`${desc.studio.appliedTextures ?? 0}/${snapshot.textures.length} ${text("project textures matched", "texturas do projeto correspondentes")}`);
      if (snapshot.textureWarnings?.length) limits.push(text("Some project textures could not be decoded.", "Algumas texturas do projeto não puderam ser decodificadas."));
      status(`${desc.name} · ${text("working revision", "revisão em edição")} ${revision.slice(0,8)}${limits.length ? " · " + limits.join(" ") : ""}`);
    } catch (error) {
      status(text("Could not update preview: ", "Não foi possível atualizar a prévia: ") + error.message, true);
    } finally { pending = false; }
  }
  window.ffxMagicPreview = {
    isManaged: () => managed,
    async loadBinary(url) {
      const response = await fetch(url, {cache:"no-store"});
      if (!response.ok) throw new Error(`Resource HTTP ${response.status}`);
      return response.arrayBuffer();
    },
    async applyTextures(textures, resource) {
      resource.appliedTextures = 0;
      for (const replacement of resource.textures ?? []) {
        const matches = textures.filter(t => t.tex0.tbp0 === replacement.tbp && t.tex0.psm === replacement.psm &&
          t.width === replacement.sourceWidth && t.height === replacement.sourceHeight);
        // Never assign by list order or guess between palettes/atlas regions.
        if (matches.length !== 1) continue;
        const pixels = new Uint8Array(await this.loadBinary(replacement.url));
        if (pixels.length !== replacement.width * replacement.height * 4) throw new Error("Invalid texture pixel count");
        Object.assign(matches[0], {pixels, width:replacement.width, height:replacement.height, name:replacement.name});
        resource.appliedTextures++;
      }
    },
    async attach(scene, descriptions, opcodes) {
      if (!location.hash.startsWith("#ffx/magic-studio")) return;
      currentScene = scene; catalogue = descriptions; supported = opcodes; revision = null;
      if (managed || requestedId !== null) {
        scene.label.parentElement.classList.add("ffx-magic-title");
        scene.label.parentElement.lastElementChild.textContent = text("Space to replay", "Espaço para repetir");
      }
      if (managed) { await refresh(); return; }
      if (requestedId !== null) {
        const id = Number(requestedId), desc = catalogue.find(item => item.main === id || item.alt === id);
        if (!desc) { status(text("This effect is not in the PS2 catalogue. Open its DLL in Magic DLL Editor to preview it.", "Este efeito não está no catálogo PS2. Abra a DLL no Magic DLL Editor para visualizá-lo."),true); return; }
        try { await scene.setMagic({...desc, main:id}); status(desc.name); }
        catch (error) { status(text("Could not load effect: ", "Não foi possível carregar o efeito: ") + error.message,true); }
      }
    },
    ownsSelection: () => location.hash.startsWith("#ffx/magic-studio") && (managed || requestedId !== null),
    replay: () => currentScene?.particles?.reset(),
  };
  setInterval(refresh, 700);
  window.addEventListener("hashchange", () => {
    if (panel) panel.hidden = !location.hash.startsWith("#ffx/magic-studio");
  });
}
