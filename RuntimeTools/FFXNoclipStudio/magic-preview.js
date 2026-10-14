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
  if (snapshot.skipExtraFlipbooks !== undefined && typeof snapshot.skipExtraFlipbooks !== "boolean")
    throw new Error("Invalid magic preview compatibility policy");
  if (snapshot.compatibilityMagicId != null &&
      (!Number.isInteger(snapshot.compatibilityMagicId) || snapshot.compatibilityMagicId < 0 || snapshot.compatibilityMagicId > 65535))
    throw new Error("Invalid magic preview source family");
  for (const texture of snapshot.textures ?? []) {
    if (!new RegExp(`^/viewer-data/magic-preview/${sessionId}/[a-f0-9]{64}\\.rgba\\.bin$`).test(texture.url) ||
        ![texture.width,texture.height,texture.sourceWidth,texture.sourceHeight].every(v => Number.isInteger(v) && v > 0 && v <= 8192))
      throw new Error("Invalid preview texture");
  }
  const funcMap = snapshot.handlerNames.map(name => pppOpcodes[name] ?? 65535);
  const unsupported = [...new Set(snapshot.usedHandlers.filter(i => !supportedOpcodes.includes(funcMap[i])).map(i => snapshot.handlerNames[i] ?? `#${i}`))];
  const sourceId = snapshot.compatibilityMagicId ?? snapshot.magicId;
  const original = catalogue.find(item => item.main === sourceId || item.alt === sourceId);
  return {
    ...original, main: snapshot.magicId, name: String(snapshot.label ?? `magic_${snapshot.magicId}`).slice(0,200),
    studio: {...snapshot, funcMap, unsupported, special: original?.setup, id: snapshot.magicId},
  };
}

// Independent of the camera/UI clock. One step is one 30 Hz PPP frame.
export function createPreviewPlayback() {
  let elapsed=0, paused=false, speed=1, steps=0;
  return {
    reset() { elapsed=0; steps=0; },
    setPaused(value) { paused=!!value; },
    get paused() { return paused; },
    setSpeed(value) {
      if (![.25,.5,1,2].includes(value)) throw new Error("Invalid preview speed");
      speed=value;
    },
    step() { paused=true; steps++; },
    advance(input) {
      const delta=steps ? (steps--,1000/30) : paused ? 0 : Math.max(0,Math.min(250,Number.isFinite(input.deltaTime)?input.deltaTime:0))*speed;
      elapsed+=delta;
      return {...input,deltaTime:delta,time:elapsed};
    },
  };
}

if (typeof document !== "undefined") bootMagicPreview();

function bootMagicPreview() {
  const params = new URLSearchParams(location.search);
  const sessionId = params.get("magicPreview"), requestedId = params.get("magic");
  const managed = !!sessionId;
  const pt = params.get("lang") === "pt";
  let currentScene, catalogue, supported, revision, failedRevision, pending = false, panel, message, clockLabel, pauseButton;
  const playback=createPreviewPlayback();
  let background=[.04,.065,.085,1], retryAt=0;
  const text = (en,br) => pt ? br : en;
  function replay() {
    playback.reset(); currentScene?.particles?.reset();
    failedRevision=undefined; retryAt=0; void refresh();
  }
  function status(value, error = false) {
    if (!panel) {
      panel = document.createElement("section");
      panel.id = "ffx-magic-preview";
      const title = document.createElement("strong");
      title.textContent = text("Magic preview", "Prévia da magia");
      message = document.createElement("p"); message.setAttribute("role", "status");
      const replayButton = document.createElement("button"); replayButton.type = "button";
      replayButton.textContent = text("Replay", "Repetir"); replayButton.onclick=replay;
      pauseButton=document.createElement("button"); pauseButton.type="button";
      const updatePause=()=>pauseButton.textContent=playback.paused?text("Resume","Continuar"):text("Pause","Pausar");
      updatePause(); pauseButton.onclick=()=>{playback.setPaused(!playback.paused);updatePause();};
      const step=document.createElement("button");step.type="button";step.textContent=text("+1 frame","+1 quadro");
      step.onclick=()=>{playback.step();updatePause();};
      const controls=document.createElement("div");controls.className="ffx-magic-controls";controls.append(replayButton,pauseButton,step);
      const options=document.createElement("div");options.className="ffx-magic-controls";
      const speedLabel=document.createElement("label");speedLabel.textContent=text("Speed ","Velocidade ");
      const speed=document.createElement("select");
      for(const value of [.25,.5,1,2]) {const option=document.createElement("option");option.value=String(value);option.textContent=`${value}×`;speed.append(option);}
      speed.value="1";speed.onchange=()=>playback.setSpeed(Number(speed.value));speedLabel.append(speed);
      const bgLabel=document.createElement("label");bgLabel.textContent=text("Background ","Fundo ");
      const bg=document.createElement("select");
      const backgrounds=[[text("Dark","Escuro"),[.04,.065,.085,1]],[text("Gray","Cinza"),[.5,.5,.5,1]],[text("Light","Claro"),[.9,.9,.9,1]]];
      backgrounds.forEach(([name],index)=>{const option=document.createElement("option");option.value=String(index);option.textContent=name;bg.append(option);});
      bg.onchange=()=>background=backgrounds[Number(bg.value)][1];bgLabel.append(bg);
      clockLabel=document.createElement("output");clockLabel.textContent="0.00 s";
      clockLabel.setAttribute("aria-live","off");clockLabel.setAttribute("aria-label",text("Preview time","Tempo da prévia"));
      options.append(speedLabel,bgLabel,clockLabel);
      const help = document.createElement("small");
      help.textContent = managed ? text("Particle preview. Edit the DLL, then click Open / update preview. Space replays. Game casting logic is not executed.", "Prévia de partículas. Edite a DLL e clique em Abrir / atualizar prévia. Espaço repete. A lógica de conjuração do jogo não é executada.") : text("Space replays the selected effect.", "Espaço repete o efeito selecionado.");
      panel.append(title, message, controls, options, help); document.body.append(panel);
    }
    message.textContent = value; panel.dataset.error = String(error);
  }
  async function refresh() {
    if (pending || Date.now()<retryAt || !currentScene || !managed || !location.hash.startsWith("#ffx/magic-studio")) return;
    pending = true;
    let attemptedRevision;
    try {
      const response = await fetch(previewSessionPath(sessionId), {cache:"no-store"});
      if (!response.ok) throw new Error(text("Preview session closed or unavailable. Open it again in the editor.", "Sessão encerrada ou indisponível. Abra a prévia novamente no editor."));
      const snapshot = await response.json();
      attemptedRevision=JSON.stringify([snapshot.revision,snapshot.compatibilityMagicId,snapshot.skipExtraFlipbooks]);
      if (attemptedRevision === revision || attemptedRevision === failedRevision) return;
      const desc = previewDescriptor(snapshot, sessionId, catalogue, supported);
      status(text("Loading working effect…", "Carregando efeito em edição…"));
      await currentScene.setMagic(desc);
      if (!currentScene.particles?.emitters?.length) throw new Error(text("This resource has no renderable emitters.", "Este recurso não contém emissores renderizáveis."));
      revision = attemptedRevision; failedRevision=undefined; playback.reset();
      panel.dataset.revision = snapshot.revision;
      panel.dataset.magicId = String(snapshot.magicId);
      const limits = [];
      if (desc.studio.unsupported.length) limits.push(text("Unsupported handlers: ", "Handlers sem suporte: ") + desc.studio.unsupported.join(", "));
      if (snapshot.rootCount > 1) limits.push(text("Showing the first particle resource.", "Exibindo o primeiro recurso de partículas."));
      if (snapshot.textures?.length) limits.push(`${desc.studio.appliedTextures ?? 0}/${snapshot.textures.length} ${text("project textures matched", "texturas do projeto correspondentes")}`);
      if (snapshot.textureWarnings?.length) limits.push(text("Some project textures could not be decoded.", "Algumas texturas do projeto não puderam ser decodificadas."));
      status(`${desc.name} · DLL ${(snapshot.workingRevision??snapshot.revision).slice(0,8)}${limits.length ? " · " + limits.join(" ") : ""}`);
    } catch (error) {
      failedRevision=attemptedRevision; retryAt=Date.now()+1500;
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
    replay,
    advance: input => {
      if (!window.ffxMagicPreview.ownsSelection()) return input;
      const updated=playback.advance(input);
      if(clockLabel) clockLabel.textContent=`${(updated.time/1000).toFixed(2)} s`;
      return updated;
    },
    background: () => window.ffxMagicPreview.ownsSelection()?background:undefined,
  };
  setInterval(refresh, 700);
  window.addEventListener("hashchange", () => {
    if (panel) panel.hidden = !location.hash.startsWith("#ffx/magic-studio");
  });
}
