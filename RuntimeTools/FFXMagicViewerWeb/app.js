import * as THREE from "./vendor/three/build/three.module.js";
import { OrbitControls } from "./vendor/three/examples/jsm/controls/OrbitControls.js";

const catalogUrl = new URL(
  new URLSearchParams(window.location.search).get("catalog") || "/viewer-data/magic/magic-viewer-catalog.json",
  window.location.href
);

const state = {
  catalog: null,
  entries: [],
  selected: null,
  activeTab: "runtime",
  stageMode: "runtime",
  stageGroup: null,
  gridVisible: true,
  lastStatus: "Waiting for catalog",
  textureInspector: {
    selectedSourceKey: null,
    channelMode: "rgba",
    alphaThreshold: 24,
    showBoxes: true,
    renderToken: 0,
    imageCache: new Map(),
    analysisCache: new Map(),
  },
};

const el = {
  viewport: document.getElementById("viewport"),
  runtimeMap: document.getElementById("runtimeMap"),
  viewportStack: document.querySelector(".viewportStack"),
  catalogSummary: document.getElementById("catalogSummary"),
  reloadCatalog: document.getElementById("reloadCatalog"),
  searchBox: document.getElementById("searchBox"),
  filters: [...document.querySelectorAll(".filters input[type='checkbox']")],
  assetList: document.getElementById("assetList"),
  selectedMagic: document.getElementById("selectedMagic"),
  selectedBand: document.getElementById("selectedBand"),
  fitCamera: document.getElementById("fitCamera"),
  toggleGrid: document.getElementById("toggleGrid"),
  toggleRuntimeMap: document.getElementById("toggleRuntimeMap"),
  toggleSimulation: document.getElementById("toggleSimulation"),
  toggleComposite: document.getElementById("toggleComposite"),
  toggleCycle: document.getElementById("toggleCycle"),
  stageNote: document.getElementById("stageNote"),
  statusText: document.getElementById("statusText"),
  stageModeBadge: document.getElementById("stageModeBadge"),
  stagePs2Badge: document.getElementById("stagePs2Badge"),
  stageHdBadge: document.getElementById("stageHdBadge"),
  stageOverlayBadge: document.getElementById("stageOverlayBadge"),
  stageHeadline: document.getElementById("stageHeadline"),
  stageSubline: document.getElementById("stageSubline"),
  stageLayerList: document.getElementById("stageLayerList"),
  decisionBand: document.getElementById("decisionBand"),
  laneTags: document.getElementById("laneTags"),
  ps2State: document.getElementById("ps2State"),
  ps3State: document.getElementById("ps3State"),
  runtimeDllState: document.getElementById("runtimeDllState"),
  overlayState: document.getElementById("overlayState"),
  sourcePaths: document.getElementById("sourcePaths"),
  runtimeSummary: document.getElementById("runtimeSummary"),
  packagePanel: document.getElementById("packagePanel"),
  textureSummary: document.getElementById("textureSummary"),
  textureThumbs: document.getElementById("textureThumbs"),
  textureSourceSelect: document.getElementById("textureSourceSelect"),
  textureMipSelect: document.getElementById("textureMipSelect"),
  textureAlphaThreshold: document.getElementById("textureAlphaThreshold"),
  textureAlphaThresholdValue: document.getElementById("textureAlphaThresholdValue"),
  textureShowBoxes: document.getElementById("textureShowBoxes"),
  textureChannelButtons: [...document.querySelectorAll(".channelButton")],
  textureInspectorCanvas: document.getElementById("textureInspectorCanvas"),
  textureInspectorFacts: document.getElementById("textureInspectorFacts"),
  runtimePanel: document.getElementById("runtimePanel"),
  crosswalkPanel: document.getElementById("crosswalkPanel"),
  tabButtons: [...document.querySelectorAll(".detailTab")],
  tabPanels: [...document.querySelectorAll(".tabPanel")],
};

const scene = new THREE.Scene();
scene.background = new THREE.Color(0x120f13);

const camera = new THREE.PerspectiveCamera(45, 1, 0.01, 1000);
camera.position.set(0, 0, 5.5);

let stageRendererAvailable = true;
let renderer;
let controls;

try {
  renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false });
  renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  el.viewport.appendChild(renderer.domElement);

  controls = new OrbitControls(camera, renderer.domElement);
  controls.enableDamping = true;
  controls.dampingFactor = 0.08;
} catch (error) {
  stageRendererAvailable = false;
  renderer = {
    capabilities: { getMaxAnisotropy: () => 1 },
    setPixelRatio() {},
    setSize() {},
    render() {},
  };
  controls = {
    target: new THREE.Vector3(),
    update() {},
  };
  const fallback = document.createElement("div");
  fallback.className = "webglFallback";
  fallback.textContent = `WebGL preview unavailable in this WebView: ${error?.message || error}`;
  el.viewport.appendChild(fallback);
}

const grid = new THREE.GridHelper(6, 20, 0x4c3d44, 0x2a2329);
grid.rotation.x = Math.PI / 2;
scene.add(grid);

scene.add(new THREE.AmbientLight(0xffffff, 1.2));
const keyLight = new THREE.DirectionalLight(0xffffff, 1.8);
keyLight.position.set(2, 4, 4);
scene.add(keyLight);

const textureLoader = new THREE.TextureLoader();
const clock = new THREE.Clock();
const glowTexture = createGlowTexture();

window.ffxMagicViewerDebug = { THREE, state, scene, camera, controls, renderer, stageRendererAvailable };

const STATIC_RUNTIME_SEQUENCE = [
  {
    phase: "SetMagicId",
    functionName: "sub_9DA780",
    address: "0x009DA780",
    proof: "stores selected magic id; rejects when another magic is already loaded",
  },
  {
    phase: "Load",
    functionName: "sub_9DA420",
    address: "0x009DA420",
    proof: "calls preload management, builds magicFiles/FFX/magic_%04d.dll, binds exports via sub_9DB0F0",
  },
  {
    phase: "Preload",
    functionName: "sub_9DAE70",
    address: "0x009DAE70",
    proof: "manages preload slots/handles before live load; can reuse or clear conflicting handles",
  },
  {
    phase: "Bind DLL exports",
    functionName: "sub_9DB0F0",
    address: "0x009DB0F0",
    proof: "GetProcAddress(GetEffectOverlayTable) + GetProcAddress(InitMagicPRX)",
  },
  {
    phase: "Start",
    functionName: "sub_9DA7D0",
    address: "0x009DA7D0",
    proof: "calls InitMagicPRX(&off_C64CE8) after the DLL is loaded",
  },
  {
    phase: "Stop",
    functionName: "sub_9DA860",
    address: "0x009DA860",
    proof: "runtime stop wrapper; clears per-effect state and special cases before unload path",
  },
  {
    phase: "Unload",
    functionName: "sub_9DA940",
    address: "0x009DA940",
    proof: "tears down live module and calls clearPreloadedPrx helper before FreeLibrary-like release",
  },
  {
    phase: "Clear preloaded PRX",
    functionName: "sub_9DACA0",
    address: "0x009DACA0",
    proof: "clears cached preload handles/fios ops for matching id/index pairs",
  },
];

const STATIC_GATE_EVIDENCE = [
  {
    surface: "Start gate loop",
    functionName: "sub_8222E0",
    proof:
      "polls readiness above Start wrappers and only fires sub_7881D0 / sub_788DD0 when sub_679580, sub_9DA280 and sub_642280 paths line up",
  },
  {
    surface: "Scene bootstrap",
    functionName: "sub_820970",
    proof:
      "loads dat_et effect bins, runs sub_88DFE0(scene), then calls sub_9DA3D0(); this is higher scene setup, not per-magic overlay timing",
  },
  {
    surface: "Scene reset / preload clear",
    functionName: "sub_88DFE0",
    proof:
      "calls sub_9DAC80(-1) during scene init/reset and in one branch forces sub_643410(0); this is scene hygiene around the magic lane",
  },
  {
    surface: "Start latch",
    functionName: "sub_643410",
    proof:
      "just stores a flag (dword_CCB468 = a1); Start calls it with (n146 == 670), so this is a small latch, not the recipe itself",
  },
  {
    surface: "Start/Stop side hook",
    functionName: "sub_6430C0",
    proof:
      "called from both Start and Stop; wraps sub_6E33F0 + sub_6E37F0(a1 == 0), so it behaves like a shared runtime mode flip",
  },
  {
    surface: "Load / Unload bookends",
    functionName: "sub_642240 / sub_6422A0 / sub_642290",
    proof:
      "Load touches sub_642240(n146) before DLL bind; Unload later touches sub_6422A0(n146) and sub_642290(); these are lifecycle bookends, not preview planes",
  },
  {
    surface: "One-shot ready latch",
    functionName: "sub_9DA280",
    proof:
      "once a live module exists, this helper runs sub_642260(n146) exactly once and flips dword_1940AB0; the gate loop treats that as readiness, not as per-slot effect timing",
  },
  {
    surface: "Overlay surface fetch",
    functionName: "sub_9DA370",
    proof:
      "Start wrappers call this only after sub_9DA7D0(); it returns GetEffectOverlayTable() only while both n146 and hModule stay live, so the overlay table is a post-start callback surface, not preview cadence",
  },
  {
    surface: "Stop -> unload handoff",
    functionName: "sub_9DA2D0",
    proof:
      "called on Start failure and from Stop; it snapshots n146/hModule into the unload-side globals (n146_0/hModule_0) and clears the live pair, so it is lifecycle state transfer, not render timing",
  },
];

const STATIC_OVERLAY_DISPATCH_EVIDENCE = [
  {
    surface: "Start wrapper stores live callback surface",
    functionName: "sub_7881D0 / sub_788DD0",
    proof:
      "after FFX_MagicFile_Start succeeds, both wrappers call sub_9DA370(0) and store the returned pointer into actor/runtime state; on failure, wrapper A drops straight into sub_9DA2D0() instead of pretending the table is ready",
  },
  {
    surface: "Per-record dispatch through +12 / +16",
    functionName: "sub_787EC0",
    proof:
      "walks active records in states 4/5, compares each stored pointer with the current sub_9DA370() result, toggles dword_113335C, then calls callback-table entry +12 when a1==0 or +16 when a1==1; this is live runtime dispatch over active records, not slot-by-slot texture timing",
  },
  {
    surface: "Special-case dispatch through +32",
    functionName: "sub_787B70",
    proof:
      "when n670 == 671, fetches sub_9DA370() and immediately calls callback-table entry +32 on dword_113336C; otherwise it falls back to cleanup helpers. That reads like one specific runtime hook, not a folder-side sequence file",
  },
  {
    surface: "Load wrappers keep placeholder state until live start",
    functionName: "sub_787FE0 / sub_788D20",
    proof:
      "both load wrappers call SetMagicId -> Load first, then either keep placeholder pointers like &unk_1340830 / zero state or, in one guarded case, jump into StartWrapper_A. So the real callback surface is not treated as ready at load time",
  },
];

const STATIC_CALLBACK_STATE_MACHINE_EVIDENCE = [
  {
    surface: "Bootstrap / readiness latch",
    functionName: "+900 / +904 / +908 -> sub_7FCD30 / sub_7882C0 / sub_787D60",
    proof:
      "the large 0611/0667/0668 callbacks first hit a global disable gate, then advance an actor/runtime mode from 4 to 5, then wait for an actor-side readiness byte before the heavier body runs",
  },
  {
    surface: "Resource guard + blob attach",
    functionName: "+1252 / +2844 / +2840 -> sub_7E45E0 / sub_712080 / sub_716AB0",
    proof:
      "before pppKeThRes24 is attached, the callbacks call a ring/mask guard, relocate internal resource pointers in the loaded blob, then register/attach that relocated block into the host runtime root",
  },
  {
    surface: "Bitfield clear/set",
    functionName: "+1068 / +1084 -> sub_909E10 / sub_909E20",
    proof:
      "these helpers directly clear or set bits inside dword_18DED08, so repeated callback milestones are touching runtime flags, not stepping through a texture playlist",
  },
  {
    surface: "RGBA/runtime apply",
    functionName: "+1328 -> sub_90A040",
    proof:
      "this helper copies four channel bytes into runtime globals and, except for one magic-id special case, converts the first three channels to float RGB for sub_643040",
  },
  {
    surface: "Scalar shaping + vec copy",
    functionName: "+1200 / +1300 -> sub_9DBDF0 / sub_6ED4B0",
    proof:
      "part of the case flow is plain curve math (sqrt of a scalar) plus repeated 4-dword vector-like copies between local state blocks, which is much closer to staged runtime shaping than to slot timing metadata",
  },
  {
    surface: "Actor-space fetch helpers",
    functionName: "+948 / +1156 / +1160 -> sub_833D90 / sub_794FC0 / sub_795730",
    proof:
      "the big bodies also sample actor-linked transform/space data: sub_833D90 builds a transformed output from actor state, sub_794FC0 reads one actor scalar via sub_82ACC0, and sub_795730 copies actor xyz directly from the live runtime object",
  },
  {
    surface: "Root pass / quad basis builder",
    functionName: "+988 / +1012 / +1108 / +1244 -> sub_72CA10 / sub_82D860 / sub_82ABE0 / sub_7EB210",
    proof:
      "sub_72CA10 builds a 4-corner basis from an angle table, sub_82D860 combines global flags into one mode/status word, sub_82ABE0 reads one float from actor/model state, and sub_7EB210 is a tiny wrapper into sub_7EB3B0(..., 32) used as another runtime helper rather than a folder-side timing key",
  },
  {
    surface: "Per-index toggle + resource-chain teardown",
    functionName: "+1112 / +1184 / +2852 -> sub_82AF20 / sub_796670 / sub_712000",
    proof:
      "the big bodies toggle per-index runtime/model state, wrap a smaller per-index dispatch path, and repeatedly free/reset a linked resource chain or pooled handle before later fan-out",
  },
];

const STATIC_OWNER_INSTANCE_PACKET_EVIDENCE = [
  {
    surface: "Owner-tag purge over active records",
    functionName: "sub_7FDE90",
    proof:
      "given one owner tag, it sweeps root+96, clears matching owner entries, walks three root lists and for every matching record tag runs sub_8013E0 + sub_800C30; this is record/owner teardown inside the runtime root, not per-texture timing",
  },
  {
    surface: "Lane/handle release by bitmask",
    functionName: "sub_7FFE50",
    proof:
      "sweeps 32 lane records in dword_23C37A4, clears matching mask bits, and when a lane goes empty calls sub_886E10 + sub_886CE0 on the paired handle; this reads like packet/runtime service release, not overlay cadence",
  },
  {
    surface: "Owner-row allocator / registry",
    functionName: "sub_7FE890",
    proof:
      "allocates or reuses one dword_12A2280 owner row, stores owner/pointer/index fields and clears byte_12A8480 / byte_12A8570; this is instance registration above the deeper interpreter path",
  },
  {
    surface: "Derived OPU/ET unit instantiation",
    functionName: "sub_7FD710",
    proof:
      "starts from dword_12A4080[0], registers the owner in root+96, allocates one record with sub_801620, zeroes it with sub_801120 and stamps the literal LoadedByOPU_ET_UNIT before registering the result in the active record table",
  },
  {
    surface: "Detach / rebind helper chain",
    functionName: "sub_7FC570 -> sub_7FDE90 / sub_8060C0 / sub_7FEDE0",
    proof:
      "the higher helper first clears owner packet lanes, then may resolve one auxiliary slot, release its old packet via sub_7FDE90 and rebind through sub_8060C0 before finishing with sub_7FEDE0 row reset; that is active-instance teardown/rebind logic, not texture sequencing",
  },
  {
    surface: "Spawn / kill of derived active records",
    functionName: "sub_7FC8D0 / sub_7FC960",
    proof:
      "sub_7FC8D0 turns one owner row into a packed runtime id and spawns another record through sub_7FD710; sub_7FC960 later marks the active record with bit 0x8000. This is runtime record lifecycle, not preview-layer order",
  },
];

const STATIC_OPCODE_HANDLER_EVIDENCE = [
  {
    surface: "Table-driven packet builder family",
    functionName: "sub_80F370 / sub_8148F0 / sub_815860",
    proof:
      "these handlers are reached from data-table entries rather than one obvious direct caller and branch by the high opcode nibble. They allocate packet buffers, register ids with sub_800C80 and, in one branch, arm active-instance hooks via sub_831440 / sub_831410. That reads like runtime operation staging, not slot timing playback",
  },
  {
    surface: "Owner-context apply over child packet rows",
    functionName: "sub_80ECB0",
    proof:
      "walks the child rows materialized under one packet, repeatedly rebinds owner context through sub_805350 / sub_7E82C0, then fans out into sub_80E390 / sub_80EA60 and active-instance calls like sub_639320 / sub_82AF20. This is per-record runtime work, not a transparent texture stack",
  },
  {
    surface: "Transform / selector handler family",
    functionName: "sub_804BB0 / sub_8019D0",
    proof:
      "both functions decode multiple opcode bands and use sub_805350, sub_7E82C0, sub_7E78B0 and sub_7EA760 to build or blend transforms, vectors and selector-driven motion. They look like behavior handlers over runtime state, not a hidden mesh timeline script",
  },
  {
    surface: "Script/table stream handlers",
    functionName: "sub_81BAC0 / sub_81C600",
    proof:
      "these handlers dereference sub_7E3720 tables, allocate or populate buffers with sub_7FF6A0, and in some branches detach existing carriers with sub_7FF0F0. They read like table-backed control/data lanes around active records, not evidence that ps3data folders contain the real sequencing",
  },
  {
    surface: "Bootstrap resets root before live population",
    functionName: "sub_7FD680 / sub_8013E0",
    proof:
      "the primary runtime root bootstrap explicitly seeds root+88 = 0, then immediately runs sub_8013E0 cleanup/reset logic before first use. This tightens the boundary: we have the reset side clearly, but not yet the final causal writer that makes root+88 carry one live cursor/timing source",
  },
];

const STATIC_DISPATCH_TABLE_EVIDENCE = [
  {
    surface: "Phase trio table",
    functionName: "0xC48D9C / 0xC48DA0 / 0xC48E04",
    proof:
      "raw IDA data dump now proves contiguous table entries for phase 0/1/2: sub_800530, sub_800590 and sub_800950 sit in one real pointer-table neighborhood rather than being isolated guesses",
  },
  {
    surface: "Opcode dispatcher family table",
    functionName: "0xC490DC neighborhood",
    proof:
      "the raw table around 0xC490DC contains sub_813B10 together with siblings like sub_81B670, sub_816750, sub_803B20, sub_8019D0, sub_819590, sub_81BCD0 and sub_80A0F0. That is direct data-table evidence for one dispatch family, not just one suspicious function",
  },
  {
    surface: "Packet / stream family tables",
    functionName: "0xC49000 / 0xC48EC0 / 0xC48FF0 / 0xC49084 / 0xC49128",
    proof:
      "separate contiguous pointer blocks now hold the packet builder, owner-context apply, stream/table and transform handlers: for example sub_80F370 at 0xC49000, sub_80ECB0 at 0xC48EC0, sub_81BAC0 spilling into the 0xC48FF0->0xC49000 region, sub_81C600 at 0xC49084 and sub_804BB0 at 0xC49128",
  },
  {
    surface: "Root-default global handoff remains narrow",
    functionName: "dword_2332E8C / sub_7FF5A0 / sub_800530 / sub_800590 / sub_800950",
    proof:
      "the new data dump reconfirms that dword_2332E8C still has a very narrow writer/read surface in the current proof set: sub_7FF5A0 refreshes it globally and the phase trio only snapshots/restores it when root+88 is absent. The final live writer for root+88 still is not proved here",
  },
];

const STATIC_PREINST_SOURCES = [
  {
    surface: "Battle-scene queue",
    functionName: "FFX_BattleScene_InitStateMachine / sub_7AD400 / sub_7AD1A0",
    proof:
      "the battle state machine enqueues actor/resource work into byte_112BEA* via sub_7AD400, and sub_7AD1A0 drains that queue into sub_783C40 / sub_783C70 / sub_783A60; this feeder path is explicitly state-machine driven",
  },
  {
    surface: "Actor/runtime object lookup",
    functionName: "sub_794030",
    proof:
      "maps actor index into one of two runtime object arrays (stride 3984 for the first bank, stride 912 for the second), so the feeder layer starts from live actor state, not from texture folders",
  },
  {
    surface: "Per-actor toggle scan",
    functionName: "sub_79F010",
    proof:
      "scans actor bitfields around +1542/+1544/+1558, routes each enabled flag through sub_79EF60, computes a compact mode mask via sub_79E7E0, and only then notifies sub_7FADF0 when that mode changes",
  },
  {
    surface: "Target/participant mask resolver",
    functionName: "sub_794340 / sub_7B09C0",
    proof:
      "sub_794340 builds 31-bit participation masks from live actor state and special selector codes (-26..-2); sub_7B09C0 chooses the active 72-byte relation block used by several feeders",
  },
  {
    surface: "Targeting helper family",
    functionName: "sub_794800 / sub_78DC30 / sub_799830 / sub_7A31F0",
    proof:
      "this family reads like first-hit selection, relation propagation, compatibility/range pruning and mask comparison built on top of sub_794340, not like visual timing or overlay playback",
  },
  {
    surface: "Selector decode + target bucket helpers",
    functionName: "sub_79A4C0 / sub_79A930 / sub_799920",
    proof:
      "sub_79A4C0 decodes command/item/monmagic-style ids through kernel tables, sub_79A930 resolves relation-slot metadata for an actor pair, and sub_799920 clusters a mask by target bucket before choosing one bucket via battle RNG",
  },
  {
    surface: "Selector helper bands",
    functionName: "sub_79AF00 / sub_79AF70 / sub_795790 / sub_795810 / sub_793660",
    proof:
      "sub_793660 gates actor indices <= 30, sub_79AF00 isolates the 20..27 band, sub_79AF70 reads one runtime bit at +1424, and sub_795790 / sub_795810 convert that live actor state into loaded/active flags used by the mask resolver",
  },
  {
    surface: "Enable/disable ET record gate",
    functionName: "sub_79EF60",
    proof:
      "if a feature bit turns on and the slot is empty, it builds a packed runtime id with sub_788EB0(counter, 3, actorIndex) and calls sub_7FC200(root, 0, effectCode, packedId); if the bit turns off and a slot exists, it calls sub_7FC370(handle)",
  },
  {
    surface: "Event router family",
    functionName: "sub_79E3F0 / sub_79E5B0 / sub_79E8E0 / sub_79EB30 / sub_79EC00 / sub_79EC60 / sub_79ECC0 / sub_79ED60 / sub_79EF60 / sub_79F180",
    proof:
      "these functions choose event code families (for example n41 = 41/42/43/44/57), allocate packed ids with sub_788EB0, and feed sub_7FC200; this is battle/event logic driving instantiation, not preview-layer timing",
  },
  {
    surface: "High battle-scene emitters",
    functionName: "sub_7A26B0 / sub_78DCF0 / sub_78F0B0",
    proof:
      "higher scene/result paths also emit into the same lane: sub_7A26B0 drains a short queue and calls sub_79E3F0(..., 7), sub_78DCF0 routes actor state into sub_79E3F0(..., mode/2), and sub_78F0B0 re-enters sub_79F010 mid result/target processing",
  },
  {
    surface: "Actor init seeding",
    functionName: "sub_783C70",
    proof:
      "during actor setup it seeds sub_7FB280(*(u16**)actor) and then runs sub_79F010(actorIndex), which means some ET records are created directly from actor initialization state",
  },
  {
    surface: "Packed id + free-slot semantics",
    functionName: "sub_788EB0 / sub_A446A0",
    proof:
      "sub_788EB0 packs a 12-bit counter plus kind byte plus actor byte into one runtime id, and sub_A446A0 returns true only when the probed record word is zero, so allocator pressure here is about live record occupancy",
  },
];

const STATIC_ROOT_PHASE_EVIDENCE = [
  {
    surface: "Runtime root materializer",
    functionName: "sub_7FD9A0",
    proof:
      "writes FFX_Magic_RuntimeRootTable[a1] = a4, zeroes 0x100 bytes, seeds internal pointer blocks and fills four table regions with -1 sentinels; this is the structural root builder, not one more overlay callback",
  },
  {
    surface: "Bootstrap seed / reset side",
    functionName: "sub_7FD680",
    proof:
      "called from FFX_BattleEffect_LoadDatEtEffectBins, it materializes root[0], sets root+88 = 0, points root+40/root+44 at off_C498DC, runs cleanup through sub_8013E0 and seeds the first registry slot",
  },
  {
    surface: "Phase table selector",
    functionName: "sub_80CD60",
    proof:
      "after copying the root into a work buffer, it chooses the active phase table with work+548 = *(work + 784 + 4*phase), which maps back to root+16 / root+20 / root+24 for phases 0 / 1 / 2",
  },
  {
    surface: "Record base resolver",
    functionName: "sub_80CD60",
    proof:
      "the interpreter then resolves each active record as copiedRoot+32 + (recordIndex << 8), so the phase pass is walking real runtime records from the root body rather than layering preview quads by texture slot",
  },
  {
    surface: "Auxiliary phase-1 side pass",
    functionName: "sub_80BEA0",
    proof:
      "paired especially with phase 1, it iterates another root table through root+28 and dispatches by record type byte 187 through dword_C48E78 or the host callback table for negative entries; this is extra runtime work around the main interpreter, not raster sequencing",
  },
  {
    surface: "root+88 boundary",
    functionName: "sub_800530 / sub_800590 / sub_800950",
    proof:
      "the three phase wrappers only promote root+88 into root+84 when present, otherwise they fall back to dword_2332E8C/dword_2332E90 before calling the interpreter; root+88 is still an override/cursor surface here, not the phase-table selector itself",
  },
];

const STATIC_INTERPRETER_PHASES = [
  {
    phase: "Phase 0",
    functionName: "sub_800530",
    proof:
      "chooses root+84 from root+88 when present, otherwise falls back to dword_2332E8C and arms dword_2332E88 from dword_2332E90 + 983040, then calls sub_80CD60(0, 0)",
  },
  {
    phase: "Phase 1",
    functionName: "sub_800590",
    proof:
      "runs extra setup/cleanup bookends (sub_7E4630, sub_7E4D90, sub_800090, sub_7BFFF0, sub_7BB9D0), then calls sub_80CD60(0, 1) and sub_80BEA0(0); sub_800090 now reads more like a pending-pointer pump with 32-slot registration plus battle-streaming handoff than like a slot-by-slot timing table",
  },
  {
    phase: "Phase 2",
    functionName: "sub_800950",
    proof:
      "reuses the same root+84 / dword_2332E8C fallback pattern and then calls sub_80CD60(0, 2), closing the proved 0/1/2 interpreter trio",
  },
];

const STATIC_ACTOR_RUNTIME_HOOKS = [
  {
    surface: "Active motion bind / playback reset",
    functionName: "sub_804400",
    proof:
      "multiple opcode families in this interpreter helper call FFX_Mseq_ResetPlaybackFlags, FFX_Mgrp_BindMseqToActiveInstance and FFX_Mgrp_SelectResidentMotionForInstance on the live actor instance selected through buf__3[index]; that is real actor-motion runtime, not texture stacking",
  },
  {
    surface: "Transform / position application",
    functionName: "sub_809780",
    proof:
      "reads actor-local transform/state from the active runtime instance, pushes it through matrix/vector helpers (sub_808D80 / sub_808CE0 / sub_808C70 / sub_737690), then fans into owner helpers like sub_80A6C0 / sub_80AB60; this looks like spatial/runtime shaping around the effect owner",
  },
  {
    surface: "Global per-instance mode toggles",
    functionName: "sub_800AD0 / sub_800B00",
    proof:
      "when sub_780D80() is live, these sweep every buf__3 instance and call sub_82AAB0(instance, 0/1); this reads like a bulk runtime mode flip, not a visual overlay phase",
  },
  {
    surface: "State fan-out gate",
    functionName: "sub_8001E0",
    proof:
      "gated by sub_9DA3A0() != 507, it walks sub_7E3720(..., 6, i) rows and only forwards those with first byte == 1 into sub_7FFDD0(...); this is another runtime/state dispatch layer above preview-plane intuition",
  },
  {
    surface: "RGBA latch + apply",
    functionName: "sub_9086D0 / sub_90A040 / sub_909E10",
    proof:
      "sub_90A040 latches RGBA bytes and, unless sub_9DA3A0() == 706, normalizes them into sub_643040; sub_9086D0 reconstructs/filters those RGBA values under flag bits in dword_18DED08; sub_909E10 clears flag bits by mask. This is live color/state plumbing, not folder-side sequencing",
  },
];

const STATIC_CALLBACK_FAMILY_SAMPLES = [
  {
    family: "0697 / 0698",
    ids: ["0697", "0698"],
    summary:
      "16 nonzero slots collapse to 5 unique targets. The dominant repeat (slots 2,3,5,6,8,9,10,11,12,13,14,15 in 0697) is sub_10001360, a 3-byte `return 0` stub.",
    highlights: [
      "sub_10001000 = larger setup body (371 bytes)",
      "sub_10001180 = short before/update-style body (227 bytes)",
      "sub_10001270 = runtime gate that rewires two overlay-table entries",
      "sub_100012F0 = short execution callback (76 bytes)",
      "sub_10001340 = short cleanup / mode-toggle callback (17 bytes)",
    ],
    implication:
      "At least this family proves that the overlay table is not a fixed 16-phase playlist: one real callback can rewrite later entries at runtime.",
  },
  {
    family: "0148 / 0151 / 0289 / 0290",
    ids: ["0148", "0151", "0289", "0290"],
    summary:
      "These sampled/structural twins all sit on the same broad 16-slot signature, and the sampled 0148 family collapses 16 nonzero slots to 6 unique targets. Its dominant repeat is nullsub_1 @ 0x100010D0.",
    highlights: [
      "nullsub_1 @ 0x100010D0 = 1-byte empty callback with 12 incoming slot aliases",
      "sub_100010E0 = short active body (116 bytes)",
      "sub_10001160 = short active body (144 bytes)",
      "sub_10001660 = alloc/config path that calls sub_10001000(...)",
      "sub_10001820 = cleanup/finalization body (139 bytes)",
    ],
    implication:
      "The structural twin ids should not be overread as 16 rich phases either. The family already proves that many repeated slots are empty aliases, with only a few distinct callback bodies worth inspecting.",
  },
  {
    family: "0003 short family",
    ids: ["0003"],
    summary:
      "9 nonzero slots collapse to 5 unique targets. The dominant repeat (slots 1,2,5,6,15) is sub_10006AA0, another 3-byte `return 0` stub.",
    highlights: [
      "sub_100068D0 carries EgoTask / EgoCtrl / pppAccele strings and looks like the heavier active body",
      "sub_10006AB0 = short distinct body (176 bytes)",
      "sub_10006B70 = another active body with helper fan-out (275 bytes)",
    ],
    implication:
      "Even the shorter family keeps the same pattern: repeated slots are mostly structural noise, while the interesting behavior lives in the small set of unique bodies.",
  },
  {
    family: "0611 / 0667 / 0668 cluster",
    ids: ["0611", "0667", "0668"],
    summary:
      "The extra HD sidecars do not imply a rich 16-phase overlay either. 0611, 0667 and 0668 all sit in the same 15-nonzero / 6-unique callback cluster, with slot 7 null and a repeated nullsub_1 across 9 slots.",
    highlights: [
      "Shared slot pattern in the cluster: slot 0 = heavier setup body, 1 = short callback, 2 = return-0 stub, 3 = large active body, 4 = secondary active/cleanup body, 5/6 and 8..14 = repeated nullsub_1 aliases, slot 7 = null, slot 15 = same short return-0 stub as slot 2",
      "0611 direct twin: sub_10004260 (1938 bytes), sub_10004D10 (15936 bytes), sub_10008BF0 (785 bytes), nullsub_1 @ 0x10008F40 with 9 slot aliases",
      "0668: sub_10004100 = heavier setup/init body (1714 bytes) that seeds actor/target-side arrays and alloc/config paths through host-context callbacks",
      "0668: sub_10004AD0 = very large active body (15488 bytes) with long internal state/case flow; large does not mean slot-by-slot timing proof",
      "0668: sub_100087F0 = cleanup/fan-out body (785 bytes) with resource clears and state-conditioned teardown",
      "0667 direct twin: sub_100040E0 (1714 bytes), sub_10004AF0 (15746 bytes), sub_10008910 (723 bytes), nullsub_1 @ 0x10008C20 with 9 slot aliases",
      "0611 and 0667 also share the same plain ps3data texture layout, while 0668 is the only one carrying extra texlist sidecars; that makes the sidecars look like asset-pipeline metadata, not a different callback recipe",
    ],
    implication:
      "0611/0667/0668 together are a stronger warning shot: even when one HD folder carries extra texlist sidecars, the DLL family still behaves like a small callback cluster with heavy aliasing, not like a transparent per-slot timeline file.",
  },
];

const PS3DATA_REALITY_NOTES = [
  "A direct local sweep over ps3data/magic currently finds only four file kinds: .phyre, .txt, .log and .pal. No separate sequence/timeline extension is proved in the folder lane.",
  "Pairs 0148/0151 and 0289/0290 share overlay-family shape while texture inventories differ, so texture count alone does not explain sequence/timing.",
  "Pair 0697/0698 stays a strong structural twin with mirrored tex + tex_cn folders.",
  "Current local counts reinforce the same boundary: 0148/0151 = 22/15 tex d3d11 payloads, 0289/0290 = 8/10, 0697/0698 = 24 + mirrored tex_cn 24, while 0668 is still the only sampled outlier with tex/00texlist.txt + texlist.log + texlist.pal.",
  "Outlier 0668 still reads like texture pipeline metadata rather than a proved runtime recipe.",
  "In 0611 / 0667 / 0668, texlist.txt is byte-identical across all three, but every sampled DDS/Phyre payload hash still differs across the cluster; same list, different bytes, still no explicit timeline artifact.",
  "In 0668 specifically, texlist.log resolves hashes back to <game>/FFX_Data/.../magic_0668/tex/GCM/*.dds.phyre and 00texlist.txt is still just a filename list, so the extra sidecars still read like asset-pipeline metadata, not effect scheduling.",
  "In 0697 / 0698, texlist.txt and tex_cn/texlist.txt match by hash while the actual DDS/Phyre payload hashes differ across both folders; again this reads like shared asset inventory, not a folder-side sequence script.",
  "A fresh sweep over 0511 / 0563 / 0565 / 0589 / 0644 / 0691 / 0692 shows the same tex + tex1 folder pattern, while 0697 / 0698 keep tex + tex_cn. Right now those look like packaging/localization variants, not proved alternate timing carriers.",
];

function resize() {
  const rect = el.viewport.getBoundingClientRect();
  const width = Math.max(1, Math.floor(rect.width));
  const height = Math.max(1, Math.floor(rect.height));
  renderer.setSize(width, height, false);
  camera.aspect = width / height;
  camera.updateProjectionMatrix();
}

function animate() {
  requestAnimationFrame(animate);
  const elapsed = clock.getElapsedTime();
  updateStageAnimation(elapsed);
  controls.update();
  renderer.render(scene, camera);
}

async function loadCatalog() {
  setStatus(`Loading ${catalogUrl.pathname}`);
  state.textureInspector.selectedSourceKey = null;
  state.textureInspector.renderToken += 1;
  state.textureInspector.imageCache.clear();
  state.textureInspector.analysisCache.clear();
  const response = await fetch(catalogUrl.href, { cache: "no-store" });
  if (!response.ok) {
    throw new Error(`Catalog load failed: ${response.status}`);
  }

  state.catalog = await response.json();
  state.entries = state.catalog.entries || [];
  const ps2Tm2PreviewCount = state.entries.reduce(
    (sum, entry) => sum + ((entry.ps2Package?.tm2Previews || []).filter((preview) => preview.previewUrl).length),
    0
  );
  const runtimeDllCount = state.catalog.summary.runtimeDllCount || state.entries.filter((entry) => entry.runtimeDll).length;
  el.catalogSummary.textContent = `${state.catalog.summary.entryCount} entries / ${state.catalog.summary.multiLaneCount} multi lane / ${runtimeDllCount} runtime DLLs / ${state.catalog.summary.entriesWithPreviews} with cover previews / ${ps2Tm2PreviewCount} ps2 tm2`;
  el.runtimeSummary.textContent = `${runtimeDllCount} runtime DLLs / ${state.catalog.runtimeEvidence.overlaySummary.with_16_slots || 0} overlay tables / ${state.catalog.runtimeEvidence.exeFunctions.length} EXE bridge rows`;

  renderList();
  const defaultEntry = selectInitialEntry();
  if (defaultEntry) {
    await selectEntry(defaultEntry.magicId);
  }
}

function selectInitialEntry() {
  const requested = new URLSearchParams(window.location.search).get("magic");
  const pilotOrder = ["0148", "0151", "0697", "0698", "0021"];
  return (
    state.entries.find((entry) => entry.magicId === requested) ||
    pilotOrder.map((id) => state.entries.find((entry) => entry.magicId === id)).find(Boolean) ||
    state.entries.find((entry) => entry.decisionBand === "multi_lane_candidate") ||
    state.entries[0]
  );
}

function renderList() {
  const query = el.searchBox.value.trim().toLowerCase();
  const enabled = new Set(el.filters.filter((filter) => filter.checked).map((filter) => filter.value));
  const filtered = state.entries.filter((entry) => {
    const matchesQuery =
      !query ||
      entry.magicId.includes(query) ||
      entry.label.toLowerCase().includes(query) ||
      entry.laneTags.some((tag) => tag.toLowerCase().includes(query));
    const matchesBand = enabled.has(entry.decisionBand);
    return matchesQuery && matchesBand;
  });

  el.assetList.innerHTML = "";
  for (const entry of filtered) {
    const button = document.createElement("button");
    button.className = `assetItem${state.selected?.magicId === entry.magicId ? " active" : ""}`;
    button.type = "button";
    button.setAttribute("role", "option");
    button.setAttribute("aria-selected", state.selected?.magicId === entry.magicId ? "true" : "false");
    button.innerHTML = `
      <div class="assetTop">
        <span class="assetId">${escapeHtml(entry.label)}</span>
        <span class="band ${escapeClass(entry.decisionBand)}">${escapeHtml(entry.decisionBand)}</span>
      </div>
      <div class="assetMeta">PS2 ${entry.ps2Package ? "yes" : "no"} / HD ${entry.ps3Magic ? "yes" : "no"} / DLL ${entry.runtimeDll ? "yes" : "no"} / overlay ${entry.overlay ? "yes" : "no"}${entry.wave4Attribution ? ` / W4 ${escapeHtml(entry.wave4Attribution.categoryBadge || entry.wave4Attribution.category)}` : ""}</div>
      <div class="assetTagRow">${entry.laneTags.map((tag) => `<span class="laneTag ${escapeClass(tag)}">${escapeHtml(tag)}</span>`).join("")}</div>
    `;
    button.addEventListener("click", () => selectEntry(entry.magicId));
    el.assetList.appendChild(button);
  }
}

async function selectEntry(magicId) {
  const entry = state.entries.find((item) => item.magicId === magicId);
  if (!entry) {
    return;
  }

  state.selected = entry;
  renderList();
  renderDetails(entry);
  await renderStage(entry);
}

function renderDetails(entry) {
  el.selectedMagic.textContent = entry.label;
  el.selectedBand.textContent = entry.decisionBand;
  el.selectedBand.className = `band ${escapeClass(entry.decisionBand)}`;
  el.decisionBand.textContent = entry.decisionBand;
  el.laneTags.textContent = [
    entry.laneTags.join(", ") || "-",
    entry.wave4Attribution
      ? `wave4:${entry.wave4Attribution.categoryBadge || entry.wave4Attribution.category} · RT2 ${entry.wave4Attribution.rt2Priority || "-"}`
      : null,
  ].filter(Boolean).join(" · ");
  el.ps2State.textContent = entry.ps2Package ? `${entry.ps2Package.packageClass} / ${entry.ps2Package.fileCount} files` : "missing";
  el.ps3State.textContent = entry.ps3Magic ? `${entry.ps3Magic.textureCount} textures / ${entry.previewUrls.length} previews copied` : "missing";
  el.runtimeDllState.textContent = entry.runtimeDll ? `${entry.runtimeDll.peKind} / ${entry.runtimeDll.exportCount} exports / ${entry.runtimeDll.importCount} imports` : "missing";
  el.overlayState.textContent = entry.overlay ? `${entry.overlay.nonzeroSlotCount}/${entry.overlay.slotCountRead} nonzero slots` : "missing";

  const sourceRows = [
    ["ps2 package", entry.sources.ps2PackagePath || "-"],
    ["ps3 magic", entry.sources.ps3MagicPath || "-"],
    ["runtime dll", entry.sources.runtimeDllPath || "-"],
    ["magic dll", entry.sources.magicDllName || "-"],
    ["cover preview", entry.sources.coverPreviewUrl || "-"],
  ];
  el.sourcePaths.innerHTML = sourceRows.map(([label, value]) => `<div class="pathLine"><strong>${escapeHtml(label)}</strong><br>${escapeHtml(value)}</div>`).join("");
  renderStageHud(entry);

  renderPackage(entry);
  renderTextures(entry, { enableInspector: state.activeTab === "textures" });
  renderRuntime(entry);
  renderCrosswalk(entry);

  const next = new URL(window.location.href);
  next.searchParams.set("magic", entry.magicId);
  window.history.replaceState(null, "", next);
}

function renderPackage(entry) {
  if (!entry.ps2Package) {
    el.packagePanel.innerHTML = `<div class="emptyLine">No PS2 mag_* package indexed for this id.</div>`;
    return;
  }

  const extRows = Object.entries(entry.ps2Package.extensionCounts || {})
    .sort((a, b) => a[0].localeCompare(b[0]))
    .map(([ext, count]) => `<li>${escapeHtml(ext || "[no ext]")}: ${count}</li>`)
    .join("");
  const sampleRows = (entry.ps2Package.sampleFiles || [])
    .map((path) => `<li>${escapeHtml(path)}</li>`)
    .join("");
  const anchorRows = (entry.sharedBatEffAnchors || []).length
    ? entry.sharedBatEffAnchors.map((anchor) => `<li>${escapeHtml(anchor)}</li>`).join("")
    : `<li>None proved for this entry.</li>`;
  const tm2PreviewRows = (entry.ps2Package.tm2Previews || [])
    .map((preview) => `
      <div class="thumbCard">
        ${preview.previewUrl
          ? `<img src="${escapeHtml(resolveCatalogAssetUrl(preview.previewUrl))}" alt="${escapeHtml(preview.name)} preview" loading="lazy" />`
          : `<div class="emptyLine">No raster preview</div>`}
        <span><strong>${escapeHtml(preview.name)}</strong><br>${preview.width}x${preview.height} / ${escapeHtml(preview.previewState)} / ${escapeHtml(preview.variant)}</span>
      </div>
    `)
    .join("");
  const rsdPreviewRows = (entry.ps2Package.modelCandidates || [])
    .filter((model) => model.previewUrl)
    .map((model) => `
      <div class="thumbCard">
        <img src="${escapeHtml(resolveCatalogAssetUrl(model.previewUrl))}" alt="${escapeHtml(model.name)} preview" loading="lazy" />
        <span><strong>${escapeHtml(model.name)}</strong><br>V ${model.vertexCount} / Poly ${model.polygonCount} / MAT ${model.materialCount}</span>
      </div>
    `)
    .join("");
  const modelRows = (entry.ps2Package.modelCandidates || [])
    .map((model) => `
      <div class="structureBlock">
        <div class="blockHead"><strong>${escapeHtml(model.name)}</strong><span>${escapeHtml(model.status)}</span></div>
        <div class="nodeMeta">${escapeHtml(model.lane)} / V ${model.vertexCount} / Poly ${model.polygonCount} / MAT ${model.materialCount} / TEX ${model.texturesResolved}/${model.textureRefCount}</div>
        <div class="pathLine">${escapeHtml(model.relativePath)}</div>
      </div>
    `)
    .join("");

  el.packagePanel.innerHTML = `
    <div class="structureBlock">
      <div class="blockHead"><strong>${escapeHtml(entry.ps2Package.folderName)}</strong><span>${escapeHtml(entry.ps2Package.packageClass)}</span></div>
      <div class="nodeMeta">${entry.ps2Package.fileCount} files / ${formatBytes(entry.ps2Package.totalBytes)} / tm2 ${entry.ps2Package.hasTm2 ? "yes" : "no"} / model ${entry.ps2Package.hasModel ? "yes" : "no"} / anm ${entry.ps2Package.hasAnimation ? "yes" : "no"} / preview ${getPs2PreviewUrls(entry).length}</div>
    </div>
    <div class="structureBlock">
      <div class="blockHead"><strong>Extensions</strong><span>${Object.keys(entry.ps2Package.extensionCounts || {}).length} kinds</span></div>
      <ul>${extRows || "<li>None</li>"}</ul>
    </div>
    <div class="structureBlock">
      <div class="blockHead"><strong>Sample Files</strong><span>read-only</span></div>
      <ul>${sampleRows || "<li>None</li>"}</ul>
    </div>
    <div class="structureBlock">
      <div class="blockHead"><strong>Shared bat_eff anchors</strong><span>${entry.sharedBatEffAnchors.length}</span></div>
      <ul>${anchorRows}</ul>
    </div>
    <div class="structureBlock">
      <div class="blockHead"><strong>TM2 Preview Stack</strong><span>${entry.ps2Package.tm2Previews?.length || 0}</span></div>
      <div class="thumbGrid">${tm2PreviewRows || `<div class="emptyLine">No TM2 preview candidates were exported for this package.</div>`}</div>
    </div>
    <div class="structureBlock">
      <div class="blockHead"><strong>PS2 Model Carriers</strong><span>${entry.ps2Package.modelCandidates?.length || 0}</span></div>
      <div class="thumbGrid">${rsdPreviewRows || `<div class="emptyLine">No static RSD preview export was generated for this package.</div>`}</div>
      <div class="structurePanel">${modelRows || `<div class="emptyLine">No RSD bundle candidates indexed for this package.</div>`}</div>
    </div>
  `;
}

function renderTextures(entry, options = {}) {
  const enableInspector = options.enableInspector ?? state.activeTab === "textures";
  const sources = getInspectableTextureSources(entry);
  if (!entry.ps3Magic && sources.length === 0) {
    el.textureSummary.textContent = "No PS3 HD magic folder indexed for this id.";
    el.textureThumbs.innerHTML = `<div class="emptyLine">No copied previews.</div>`;
    renderTextureInspectorEmpty("No raster source is available for texture inspection on this entry.");
    return;
  }

  const hdTextureCount = entry.ps3Magic?.textureCount || 0;
  const copiedCount = entry.previewUrls.length;
  const sourceKind = sources[0]?.kind || "none";
  el.textureSummary.textContent = `${hdTextureCount} dds.phyre textures / ${copiedCount} copied preview PNGs / inspector ${sourceKind}`;

  if (sources.length === 0) {
    el.textureThumbs.innerHTML = `<div class="emptyLine">PNG preview source was not available for this folder.</div>`;
    renderTextureInspectorEmpty("The current catalog has no copied raster source to inspect here.");
    return;
  }

  if (!sources.some((source) => source.key === state.textureInspector.selectedSourceKey)) {
    state.textureInspector.selectedSourceKey = sources[0].key;
  }

  el.textureThumbs.innerHTML = sources
    .map((source) => `
      <button class="thumbCard${state.textureInspector.selectedSourceKey === source.key ? " active" : ""}" type="button" data-texture-source="${escapeHtml(source.key)}">
        <img src="${escapeHtml(resolveCatalogAssetUrl(source.url))}" alt="${escapeHtml(source.label)} preview" loading="lazy" />
        <span><strong>${escapeHtml(source.label)}</strong><br>${escapeHtml(source.kindLabel)}<br>${escapeHtml(source.url)}</span>
      </button>
    `)
    .join("");

  el.textureThumbs.querySelectorAll("[data-texture-source]").forEach((node) => {
    node.addEventListener("click", () => {
      state.textureInspector.selectedSourceKey = node.getAttribute("data-texture-source");
      renderTextures(state.selected, { enableInspector: true });
    });
  });

  if (!enableInspector) {
    renderTextureInspectorEmpty("Texture Inspector loads when the Textures tab is active.");
    return;
  }

  renderTextureInspector(entry, sources).catch((error) => {
    if (entry === state.selected && state.activeTab === "textures") {
      renderTextureInspectorEmpty(`Inspector failed: ${error.message}`);
    }
  });
}

async function renderTextureInspector(entry, sources) {
  syncTextureInspectorControls(sources);
  const selected = sources.find((source) => source.key === state.textureInspector.selectedSourceKey) || sources[0];
  if (!selected) {
    renderTextureInspectorEmpty("No selected source is available for inspection.");
    return;
  }

  const renderToken = ++state.textureInspector.renderToken;
  state.textureInspector.selectedSourceKey = selected.key;
  const payload = await loadTextureInspectorSource(selected);
  if (
    renderToken !== state.textureInspector.renderToken ||
    entry !== state.selected ||
    state.textureInspector.selectedSourceKey !== selected.key ||
    state.activeTab !== "textures"
  ) {
    return;
  }
  const analysis = analyzeTextureInspectorPayload(selected, payload, state.textureInspector.alphaThreshold);
  drawTextureInspectorCanvas(payload, analysis, state.textureInspector.channelMode, state.textureInspector.showBoxes);
  renderTextureInspectorFacts(entry, selected, payload, analysis);
}

function syncTextureInspectorControls(sources) {
  el.textureSourceSelect.innerHTML = sources
    .map((source) => `<option value="${escapeHtml(source.key)}"${source.key === state.textureInspector.selectedSourceKey ? " selected" : ""}>${escapeHtml(source.label)} - ${escapeHtml(source.kindLabel)}</option>`)
    .join("");
  el.textureMipSelect.innerHTML = `<option value="0">Mip 0 preview</option>`;
  el.textureMipSelect.disabled = true;
  el.textureAlphaThreshold.value = String(state.textureInspector.alphaThreshold);
  el.textureAlphaThresholdValue.textContent = String(state.textureInspector.alphaThreshold);
  el.textureShowBoxes.checked = state.textureInspector.showBoxes;
  el.textureChannelButtons.forEach((button) => {
    const active = button.dataset.channel === state.textureInspector.channelMode;
    button.classList.toggle("active", active);
    button.setAttribute("aria-pressed", String(active));
  });
}

function renderTextureInspectorEmpty(message) {
  const canvas = el.textureInspectorCanvas;
  const context = canvas.getContext("2d");
  canvas.width = 512;
  canvas.height = 288;
  context.clearRect(0, 0, canvas.width, canvas.height);
  context.fillStyle = "#120f13";
  context.fillRect(0, 0, canvas.width, canvas.height);
  context.fillStyle = "#f3eee4";
  context.font = "16px Segoe UI";
  context.fillText("Texture Inspector unavailable", 24, 42);
  context.font = "13px Segoe UI";
  wrapCanvasText(context, message, 24, 76, canvas.width - 48, 20);
  el.textureInspectorFacts.innerHTML = `<div class="emptyLine">${escapeHtml(message)}</div>`;
}

async function loadTextureInspectorSource(source) {
  if (state.textureInspector.imageCache.has(source.key)) {
    return state.textureInspector.imageCache.get(source.key);
  }

  const image = await loadHtmlImage(new URL(source.url, catalogUrl.href).href);
  const canvas = document.createElement("canvas");
  canvas.width = image.naturalWidth || image.width;
  canvas.height = image.naturalHeight || image.height;
  const context = canvas.getContext("2d", { willReadFrequently: true });
  context.drawImage(image, 0, 0);
  const imageData = context.getImageData(0, 0, canvas.width, canvas.height);
  const payload = {
    width: canvas.width,
    height: canvas.height,
    imageData,
  };
  state.textureInspector.imageCache.set(source.key, payload);
  return payload;
}

function loadHtmlImage(url) {
  return new Promise((resolve, reject) => {
    const image = new Image();
    image.decoding = "async";
    image.onload = () => resolve(image);
    image.onerror = () => reject(new Error(`Image load failed: ${url}`));
    image.src = url;
  });
}

function resolveCatalogAssetUrl(relativeUrl) {
  return new URL(relativeUrl, catalogUrl.href).href;
}

function analyzeTextureInspectorPayload(source, payload, alphaThreshold) {
  const cacheKey = `${source.key}|${alphaThreshold}`;
  if (state.textureInspector.analysisCache.has(cacheKey)) {
    return state.textureInspector.analysisCache.get(cacheKey);
  }

  const { width, height, imageData } = payload;
  const data = imageData.data;
  const totalPixels = width * height;
  const alphaMask = new Uint8Array(totalPixels);
  let visiblePixels = 0;
  let opaquePixels = 0;
  let alphaSum = 0;
  let luminanceSum = 0;
  let luminanceSqSum = 0;
  let chromaSum = 0;

  for (let index = 0; index < totalPixels; index++) {
    const offset = index * 4;
    const r = data[offset];
    const g = data[offset + 1];
    const b = data[offset + 2];
    const a = data[offset + 3];
    if (a >= alphaThreshold) {
      alphaMask[index] = 1;
      visiblePixels++;
      alphaSum += a;
      if (a >= 220) {
        opaquePixels++;
      }
    }

    const luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;
    luminanceSum += luminance;
    luminanceSqSum += luminance * luminance;
    chromaSum += Math.max(r, g, b) - Math.min(r, g, b);
  }

  const boxes = findAlphaIslands(alphaMask, width, height, Math.max(24, Math.floor(totalPixels * 0.00025)));
  const largestArea = boxes[0]?.area || 0;
  const alphaCoverage = visiblePixels / Math.max(1, totalPixels);
  const dominantCoverage = largestArea / Math.max(1, visiblePixels);
  const meanAlpha = visiblePixels > 0 ? alphaSum / visiblePixels : 0;
  const meanLuminance = luminanceSum / Math.max(1, totalPixels);
  const luminanceVariance = Math.max(0, luminanceSqSum / Math.max(1, totalPixels) - meanLuminance * meanLuminance);
  const meanChroma = chromaSum / Math.max(1, totalPixels);
  const heuristics = buildTextureHeuristics({
    width,
    height,
    alphaCoverage,
    dominantCoverage,
    visiblePixels,
    opaquePixels,
    boxCount: boxes.length,
    meanAlpha,
    luminanceVariance,
    meanChroma,
  });

  const analysis = {
    width,
    height,
    alphaCoverage,
    dominantCoverage,
    visiblePixels,
    opaquePixels,
    meanAlpha,
    luminanceVariance,
    meanChroma,
    boxes,
    heuristics,
  };
  state.textureInspector.analysisCache.set(cacheKey, analysis);
  return analysis;
}

function findAlphaIslands(mask, width, height, minArea) {
  const visited = new Uint8Array(mask.length);
  const boxes = [];
  const neighbors = [
    [1, 0],
    [-1, 0],
    [0, 1],
    [0, -1],
  ];

  for (let index = 0; index < mask.length; index++) {
    if (!mask[index] || visited[index]) {
      continue;
    }

    let area = 0;
    let minX = width;
    let minY = height;
    let maxX = 0;
    let maxY = 0;
    const queue = [index];
    visited[index] = 1;

    while (queue.length > 0) {
      const current = queue.pop();
      const x = current % width;
      const y = Math.floor(current / width);
      area++;
      minX = Math.min(minX, x);
      minY = Math.min(minY, y);
      maxX = Math.max(maxX, x);
      maxY = Math.max(maxY, y);

      for (const [dx, dy] of neighbors) {
        const nx = x + dx;
        const ny = y + dy;
        if (nx < 0 || ny < 0 || nx >= width || ny >= height) {
          continue;
        }
        const next = ny * width + nx;
        if (!mask[next] || visited[next]) {
          continue;
        }
        visited[next] = 1;
        queue.push(next);
      }
    }

    if (area >= minArea) {
      boxes.push({
        x: minX,
        y: minY,
        width: maxX - minX + 1,
        height: maxY - minY + 1,
        area,
      });
    }
  }

  return boxes.sort((left, right) => right.area - left.area).slice(0, 12);
}

function buildTextureHeuristics(metrics) {
  const chips = [];
  if (metrics.boxCount >= 4 && metrics.dominantCoverage < 0.72) {
    chips.push({ text: "atlas-like candidate", className: "strong" });
  } else {
    chips.push({ text: "single-surface leaning", className: "" });
  }

  if (metrics.alphaCoverage < 0.22 && metrics.boxCount <= 3) {
    chips.push({ text: "mask-like sparse alpha", className: "warning" });
  }

  if (Math.abs(metrics.width - metrics.height) > Math.min(metrics.width, metrics.height) * 0.2) {
    chips.push({ text: "non-square source", className: "strong" });
  }

  if (metrics.meanChroma < 18 && metrics.alphaCoverage > 0.2) {
    chips.push({ text: "low-chroma plate", className: "" });
  }

  if (metrics.luminanceVariance < 1200 && metrics.boxCount >= 6) {
    chips.push({ text: "fragmented glow sheet", className: "" });
  }

  return chips;
}

function drawTextureInspectorCanvas(payload, analysis, channelMode, showBoxes) {
  const canvas = el.textureInspectorCanvas;
  const context = canvas.getContext("2d", { willReadFrequently: true });
  canvas.width = payload.width;
  canvas.height = payload.height;
  context.clearRect(0, 0, canvas.width, canvas.height);

  const output = new ImageData(payload.width, payload.height);
  const source = payload.imageData.data;
  const target = output.data;
  for (let index = 0; index < source.length; index += 4) {
    const r = source[index];
    const g = source[index + 1];
    const b = source[index + 2];
    const a = source[index + 3];
    if (channelMode === "rgba") {
      target[index] = r;
      target[index + 1] = g;
      target[index + 2] = b;
      target[index + 3] = a;
    } else if (channelMode === "rgb") {
      target[index] = r;
      target[index + 1] = g;
      target[index + 2] = b;
      target[index + 3] = 255;
    } else if (channelMode === "a") {
      target[index] = a;
      target[index + 1] = a;
      target[index + 2] = a;
      target[index + 3] = 255;
    } else {
      const channelValue = channelMode === "r" ? r : channelMode === "g" ? g : b;
      target[index] = channelValue;
      target[index + 1] = channelValue;
      target[index + 2] = channelValue;
      target[index + 3] = 255;
    }
  }

  context.putImageData(output, 0, 0);
  if (showBoxes && analysis.boxes.length > 0) {
    context.save();
    context.lineWidth = Math.max(1, Math.ceil(Math.min(canvas.width, canvas.height) / 256));
    analysis.boxes.forEach((box, index) => {
      context.strokeStyle = index === 0 ? "#ffd27a" : "#8cd6ff";
      context.strokeRect(box.x + 0.5, box.y + 0.5, box.width, box.height);
    });
    context.restore();
  }
}

function renderTextureInspectorFacts(entry, selected, payload, analysis) {
  const heuristics = analysis.heuristics
    .map((chip) => `<span class="heuristicChip ${escapeClass(chip.className)}">${escapeHtml(chip.text)}</span>`)
    .join("");
  const dominantPercent = (analysis.dominantCoverage * 100).toFixed(1);
  const alphaPercent = (analysis.alphaCoverage * 100).toFixed(1);
  el.textureInspectorFacts.innerHTML = `
    <div class="structureBlock">
      <div class="blockHead"><strong>${escapeHtml(selected.label)}</strong><span>${escapeHtml(selected.kindLabel)}</span></div>
      <div class="nodeMeta">${escapeHtml(selected.url)}</div>
      <div class="textureFactNote">Inspector source is the generated raster preview currently available in the catalog. DDS/Phyre full mip-chain and runtime UV recipe remain blocked.</div>
    </div>
    <div class="textureFactGrid">
      <div class="textureFactCard"><strong>${payload.width}x${payload.height}</strong><span>source resolution</span></div>
      <div class="textureFactCard"><strong>${state.textureInspector.channelMode.toUpperCase()}</strong><span>channel view</span></div>
      <div class="textureFactCard"><strong>${alphaPercent}%</strong><span>alpha coverage >= ${state.textureInspector.alphaThreshold}</span></div>
      <div class="textureFactCard"><strong>${analysis.boxes.length}</strong><span>alpha islands kept</span></div>
      <div class="textureFactCard"><strong>${dominantPercent}%</strong><span>largest island share</span></div>
      <div class="textureFactCard"><strong>${analysis.meanAlpha.toFixed(0)}</strong><span>mean alpha on visible pixels</span></div>
    </div>
    <div class="structureBlock">
      <div class="blockHead"><strong>Heuristics</strong><span>${entry.overlay ? `overlay ${entry.overlay.nonzeroSlotCount}/${entry.overlay.slotCountRead}` : "no overlay row"}</span></div>
      <div class="heuristicChipRow">${heuristics || `<span>No heuristic fired.</span>`}</div>
    </div>
    <div class="structureBlock">
      <div class="blockHead"><strong>Mip Truth</strong><span>honest gate</span></div>
      <div class="nodeMeta">The inspector currently works on copied preview raster only (mip 0). Extra DDS mips, atlas sub-rect proof and shader recipe are not yet wired into this web lane.</div>
    </div>
  `;
}

function wrapCanvasText(context, text, x, y, maxWidth, lineHeight) {
  const words = String(text).split(/\s+/);
  let line = "";
  let cursorY = y;
  for (const word of words) {
    const next = line ? `${line} ${word}` : word;
    if (context.measureText(next).width > maxWidth && line) {
      context.fillText(line, x, cursorY);
      line = word;
      cursorY += lineHeight;
    } else {
      line = next;
    }
  }
  if (line) {
    context.fillText(line, x, cursorY);
  }
}

function renderRuntime(entry) {
  const blocks = [];

  if (entry.runtimeDll) {
    const dll = entry.runtimeDll;
    const exportRows = (dll.exports || [])
      .slice(0, 12)
      .map((row) => `<li>${escapeHtml(row.name || `Ordinal_${row.ordinal}`)} <span class="monoSoft">${escapeHtml(row.rva || "")}</span></li>`)
      .join("");
    const importRows = (dll.importLibraries || [])
      .map((lib) => `
        <div class="runtimeMiniRow">
          <strong>${escapeHtml(lib.library)}</strong>
          <span>${lib.count} imports</span>
          <em>${escapeHtml((lib.sampleImports || []).slice(0, 8).join(", ") || "-")}</em>
        </div>
      `)
      .join("");
    const sectionRows = (dll.sections || [])
      .map((section) => `
        <div class="runtimeMiniRow">
          <strong>${escapeHtml(section.name || "[blank]")}</strong>
          <span>${escapeHtml(section.virtualAddress)} / raw 0x${Number(section.rawPointer || 0).toString(16).toUpperCase()}</span>
          <em>${formatBytes(section.rawSize)} / ${escapeHtml(section.characteristics)}</em>
        </div>
      `)
      .join("");
    const stringFamilyRows = (dll.stringFamilies || [])
      .slice(0, 8)
      .map((family) => `
        <div class="runtimeMiniRow">
          <strong>${escapeHtml(family.family)}</strong>
          <span>${family.count}</span>
          <em>${escapeHtml((family.examples || []).slice(0, 4).join(", ") || "-")}</em>
        </div>
      `)
      .join("");
    const sampleStringRows = (dll.sampleStrings || [])
      .slice(0, 12)
      .map((value) => `<li>${escapeHtml(value)}</li>`)
      .join("");
    const slotRows = (dll.overlaySlots || entry.overlay?.slots || [])
      .map((slot) => `
        <div class="slotCell ${escapeClass(slot.kind || "unknown")}">
          <div><strong>${String(slot.index).padStart(2, "0")}</strong><span>${escapeHtml(slot.kind || "-")}</span></div>
          <b>${escapeHtml(slot.roleName || "unknown")}</b>
          <small>${escapeHtml(slot.rva || slot.virtualAddress || "-")} ${slot.section ? `/ ${escapeHtml(slot.section)}` : ""}</small>
        </div>
      `)
      .join("");
    const warningRows = (dll.warnings || [])
      .map((warning) => `<li>${escapeHtml(warning)}</li>`)
      .join("");

    blocks.push(`
      <div class="structureBlock runtimeReaderBlock">
        <div class="blockHead"><strong>In-game DLL Carrier Map</strong><span>${escapeHtml(dll.dllName)}</span></div>
        <div class="nodeMeta">Parsed directly from <code>magicFiles/FFX</code>. This is real runtime carrier evidence: PE header, exports, imports, sections, strings and attached overlay slots. It is still not frame-accurate playback.</div>
        <div class="runtimeFactGrid">
          <div><span>PE</span><strong>${escapeHtml(dll.peKind)} / ${escapeHtml(dll.machine)}</strong></div>
          <div><span>Size</span><strong>${formatBytes(dll.fileSize)}</strong></div>
          <div><span>Image</span><strong>${escapeHtml(dll.imageBase)} + ${escapeHtml(dll.entryPointRva)}</strong></div>
          <div><span>Exports</span><strong>${dll.exportCount}</strong></div>
          <div><span>Imports</span><strong>${dll.importCount}</strong></div>
          <div><span>Sections</span><strong>${dll.sectionCount}</strong></div>
          <div><span>Strings</span><strong>${dll.asciiStringCount} / ${dll.engineStringCount} engine-like</strong></div>
          <div><span>Overlay CSV</span><strong>${dll.hasOverlayRow ? "attached" : "missing"}</strong></div>
        </div>
        <div class="pathLine"><strong>SHA256</strong><br>${escapeHtml(dll.sha256 || "-")}</div>
      </div>
      <div class="structureBlock">
        <div class="blockHead"><strong>Runtime Exports</strong><span>${dll.exportCount}</span></div>
        <ul>${exportRows || "<li>No named exports decoded.</li>"}</ul>
      </div>
      <div class="structureBlock">
        <div class="blockHead"><strong>Runtime Imports</strong><span>${dll.importLibraries?.length || 0} libraries</span></div>
        <div class="runtimeMiniTable">${importRows || `<div class="emptyLine">No import table decoded.</div>`}</div>
      </div>
      <div class="structureBlock">
        <div class="blockHead"><strong>PE Sections</strong><span>${dll.sectionCount}</span></div>
        <div class="runtimeMiniTable">${sectionRows || `<div class="emptyLine">No sections decoded.</div>`}</div>
      </div>
      <div class="structureBlock">
        <div class="blockHead"><strong>Overlay Callback/Data Slots</strong><span>${(dll.overlaySlots || entry.overlay?.slots || []).length || 0}/16</span></div>
        <div class="nodeMeta">These are callback/data candidates from the extracted <code>GetEffectOverlayTable</code> surface. They are not a texture playlist.</div>
        <div class="slotGrid">${slotRows || `<div class="emptyLine">No slot-level overlay row attached for this DLL.</div>`}</div>
      </div>
      <div class="structureBlock">
        <div class="blockHead"><strong>Runtime Strings</strong><span>${dll.asciiStringCount}</span></div>
        <div class="runtimeMiniTable">${stringFamilyRows || `<div class="emptyLine">No ASCII string families decoded.</div>`}</div>
        <ul>${sampleStringRows || "<li>No sample strings decoded.</li>"}</ul>
      </div>
      ${warningRows ? `
        <div class="structureBlock">
          <div class="blockHead"><strong>Reader Warnings</strong><span>honest boundary</span></div>
          <ul>${warningRows}</ul>
        </div>
      ` : ""}
    `);
  } else {
    blocks.push(`
      <div class="emptyLine">No local runtime DLL was read for ${escapeHtml(entry.label)}. Regenerate the catalog with <code>--magicfiles-root</code> pointing at the game's <code>magicFiles/FFX</code> folder.</div>
    `);
  }

  if (entry.ps3Magic) {
    const textureRows = (entry.ps3Magic.sampleTextures || [])
      .slice(0, 10)
      .map((path) => `<li>${escapeHtml(path)}</li>`)
      .join("");
    blocks.push(`
      <div class="structureBlock">
        <div class="blockHead"><strong>Phyre Payload Folder</strong><span>${escapeHtml(entry.ps3Magic.folderName)}</span></div>
        <div class="nodeMeta">This is the HD payload side consumed beside the DLL lane. It proves texture/package presence, not timeline order.</div>
        <div class="pathLine">${escapeHtml(entry.ps3Magic.relativePath)}</div>
        <ul>${textureRows || "<li>No sampled .dds.phyre paths in catalog.</li>"}</ul>
      </div>
    `);
  }

  const timingRows = STATIC_RUNTIME_SEQUENCE.map(
    (row) => `
      <li><strong>${escapeHtml(row.phase)}</strong> - ${escapeHtml(row.functionName)} @ ${escapeHtml(row.address)}<br>${escapeHtml(row.proof)}</li>
    `
  ).join("");

  blocks.push(`
    <div class="structureBlock">
      <div class="blockHead"><strong>Static Lifecycle / Gate Evidence</strong><span>proved static order</span></div>
      <div class="nodeMeta">This is the current proved lifecycle from EXE + DLL bridge evidence. It is timing/lifecycle truth, not synthetic surface behavior.</div>
      <ul>${timingRows}</ul>
      <div class="pathLine">Current boundary: this proves static order and export bind points, but not per-slot render/update semantics and not real frame timing inside the effect.</div>
      <div class="pathLine"><code>Runtime Map</code> is the default viewport mode. <code>Cycle Surface</code> and <code>Stack Surface</code> are secondary inspection modes for payload previews only.</div>
    </div>
  `);

  const gateRows = STATIC_GATE_EVIDENCE.map(
    (row) => `
      <li><strong>${escapeHtml(row.surface)}</strong> - ${escapeHtml(row.functionName)}<br>${escapeHtml(row.proof)}</li>
    `
  ).join("");

  blocks.push(`
    <div class="structureBlock">
      <div class="blockHead"><strong>Scene Gates / Helper Hooks</strong><span>proved context</span></div>
      <div class="nodeMeta">These helpers sit above or around Start/Stop and explain why the left pane cannot be read as real effect timing.</div>
      <ul>${gateRows}</ul>
    </div>
  `);

  const overlayDispatchRows = STATIC_OVERLAY_DISPATCH_EVIDENCE.map(
    (row) => `
      <li><strong>${escapeHtml(row.surface)}</strong> - ${escapeHtml(row.functionName)}<br>${escapeHtml(row.proof)}</li>
    `
  ).join("");

  blocks.push(`
    <div class="structureBlock">
      <div class="blockHead"><strong>Overlay Table Dispatch</strong><span>proved vtable usage</span></div>
      <div class="nodeMeta">This newer pass tightens the central boundary: GetEffectOverlayTable is being consumed as a live callback surface with specific offsets invoked by scene/runtime wrappers, not as a transparent 16-step playlist.</div>
      <ul>${overlayDispatchRows}</ul>
      <div class="pathLine">Current implication: the left viewport is only a synthetic surface stage. The real runtime lane stores, swaps and dispatches callback pointers over active records after Start succeeds.</div>
    </div>
  `);

  const callbackStateRows = STATIC_CALLBACK_STATE_MACHINE_EVIDENCE.map(
    (row) => `
      <li><strong>${escapeHtml(row.surface)}</strong> - ${escapeHtml(row.functionName)}<br>${escapeHtml(row.proof)}</li>
    `
  ).join("");

  blocks.push(`
    <div class="structureBlock">
      <div class="blockHead"><strong>DLL Callback State Machine</strong><span>proved helper semantics</span></div>
      <div class="nodeMeta">The heavy sampled DLL bodies now read like staged runtime state machines. The interesting milestones are host-context helper calls, flag flips, color/apply paths and resource attach/cleanup, not a transparent 16-step texture timeline.</div>
      <ul>${callbackStateRows}</ul>
      <div class="pathLine">Current implication: when the preview looks like stacked squares, the missing truth is not just "better texture conversion". The runtime lane is also deciding when to arm, hide, attach, clear and fan out resources.</div>
    </div>
  `);

  const ownerPacketRows = STATIC_OWNER_INSTANCE_PACKET_EVIDENCE.map(
    (row) => `
      <li><strong>${escapeHtml(row.surface)}</strong> - ${escapeHtml(row.functionName)}<br>${escapeHtml(row.proof)}</li>
    `
  ).join("");

  blocks.push(`
    <div class="structureBlock">
      <div class="blockHead"><strong>Owner / Instance Packet Layer</strong><span>proved root-side machinery</span></div>
      <div class="nodeMeta">This newer pass sits between the high event feeders and the deeper interpreter. The root is not just "holding textures"; it is managing owner rows, derived active records, packet lanes and teardown/rebind helpers.</div>
      <ul>${ownerPacketRows}</ul>
      <div class="pathLine">Current implication: when the stage looks like stacked quads, the missing truth is not only mesh conversion. The runtime lane is also spawning, purging, re-registering and killing effect records around the active owner.</div>
      <div class="pathLine">Boundary: this still does not prove who writes <code>root+88</code> or exact frame timing, but it tightens why the synthetic stage cannot be read as a timing oracle.</div>
    </div>
  `);

  const opcodeRows = STATIC_OPCODE_HANDLER_EVIDENCE.map(
    (row) => `
      <li><strong>${escapeHtml(row.surface)}</strong> - ${escapeHtml(row.functionName)}<br>${escapeHtml(row.proof)}</li>
    `
  ).join("");

  blocks.push(`
    <div class="structureBlock">
      <div class="blockHead"><strong>Opcode Handler Families</strong><span>proved table-driven operations</span></div>
      <div class="nodeMeta">The next layer below the root/interpreter is still not "timing by texture slot". Several handlers are reached through dispatch tables and then perform packet build, owner-context apply, transform shaping, stream/table decode and carrier cleanup over live records.</div>
      <ul>${opcodeRows}</ul>
      <div class="pathLine">Current implication: even when the left pane looks like stacked quads, the runtime lane is spending real work on packet rows, owner binds and active-instance state. That is stronger than any preview-plane timing guess, but still not exact frame timing per magic.</div>
    </div>
  `);

  const dispatchTableRows = STATIC_DISPATCH_TABLE_EVIDENCE.map(
    (row) => `
      <li><strong>${escapeHtml(row.surface)}</strong> - ${escapeHtml(row.functionName)}<br>${escapeHtml(row.proof)}</li>
    `
  ).join("");

  blocks.push(`
    <div class="structureBlock">
      <div class="blockHead"><strong>Dispatch Tables</strong><span>proved raw pointer blocks</span></div>
      <div class="nodeMeta">A raw IDA data-target pass now proves that several of these handlers live inside real contiguous pointer tables. So the runtime lane is not just "calling a few random helpers"; it has actual dispatch families wired in data.</div>
      <ul>${dispatchTableRows}</ul>
      <div class="pathLine">Boundary: these tables prove family wiring and strengthen the runtime reading, but they still do not close frame-accurate timing or the final causal writer that makes <code>root+88</code> carry one live source.</div>
    </div>
  `);

  const preinstRows = STATIC_PREINST_SOURCES.map(
    (row) => `
      <li><strong>${escapeHtml(row.surface)}</strong> - ${escapeHtml(row.functionName)}<br>${escapeHtml(row.proof)}</li>
    `
  ).join("");

  blocks.push(`
    <div class="structureBlock">
      <div class="blockHead"><strong>Pre-instantiation Sources</strong><span>proved feeder layer</span></div>
      <div class="nodeMeta">This is the layer above ET/UNIT record creation. Current proof says live records are fed by actor state, mode bits, owner/token routing and event codes before the deeper interpreter path runs.</div>
      <ul>${preinstRows}</ul>
      <div class="pathLine">Current implication: the synthetic surface stage is not just "too many textures stacked". The real lane includes battle-scene queue drains, actor/event routers, enable/disable gates and packed runtime record ids before any deeper callback/interpreter behavior.</div>
    </div>
  `);

  const rootPhaseRows = STATIC_ROOT_PHASE_EVIDENCE.map(
    (row) => `
      <li><strong>${escapeHtml(row.surface)}</strong> - ${escapeHtml(row.functionName)}<br>${escapeHtml(row.proof)}</li>
    `
  ).join("");

  blocks.push(`
    <div class="structureBlock">
      <div class="blockHead"><strong>Runtime Root / Phase Tables</strong><span>proved interpreter wiring</span></div>
      <div class="nodeMeta">The new pass tightens a very important boundary: the 0/1/2 interpreter rhythm is selecting phase tables from the runtime root itself, then walking real 0x100-byte records from that root. This is deeper than preview layering and stronger than any texture-count timing guess.</div>
      <ul>${rootPhaseRows}</ul>
      <div class="pathLine">Current implication: the runtime lane already looks like root materialization -> phase table select -> record walk -> opcode/helper dispatch. That is much closer to one real recipe engine than to copied PNGs trying to play themselves.</div>
      <div class="pathLine">Boundary: this still does not close the final causal writer for <code>root+88</code> or exact frame counts per magic, but it sharply reduces the remaining ambiguity about where the 0/1/2 passes get their work.</div>
    </div>
  `);

  const interpreterRows = STATIC_INTERPRETER_PHASES.map(
    (row) => `
      <li><strong>${escapeHtml(row.phase)}</strong> - ${escapeHtml(row.functionName)}<br>${escapeHtml(row.proof)}</li>
    `
  ).join("");

  blocks.push(`
    <div class="structureBlock">
      <div class="blockHead"><strong>Interpreter Passes</strong><span>proved 0/1/2 trio</span></div>
      <div class="nodeMeta">Current best runtime reading is not "slot 0..15 equals timing". A deeper interpreter path runs in at least three proved passes before/around the visible effect lifecycle.</div>
      <ul>${interpreterRows}</ul>
      <div class="pathLine">Current phase-1 hint: sub_7E4630 and sub_7E4D90 look like queue/buffer drains, and sub_800090 now looks like a pending-pointer pump with 32-slot dedupe/registration plus battle-streaming handoff (<code>sub_7FFEC0 -> sub_886E90 / sub_8873C0 -> sub_81D000</code>). That is stronger than any timing guess from the synthetic surface stage, but still not exact frame timing.</div>
      <div class="pathLine">Current root-xref hint: many direct <code>dword_12A4080</code> helpers read more like owner cleanup / active-owner rebind sweeps than like semantic definitions of <code>root+84</code> or <code>root+88</code>.</div>
      <div class="pathLine">Boundary: this proves a structural three-pass interpreter rhythm around the root, not exact frame counts or final mesh semantics inside one magic.</div>
    </div>
  `);

  const actorHookRows = STATIC_ACTOR_RUNTIME_HOOKS.map(
    (row) => `
      <li><strong>${escapeHtml(row.surface)}</strong> - ${escapeHtml(row.functionName)}<br>${escapeHtml(row.proof)}</li>
    `
  ).join("");

  blocks.push(`
    <div class="structureBlock">
      <div class="blockHead"><strong>Actor / Motion Hooks</strong><span>proved runtime side-effects</span></div>
      <div class="nodeMeta">This newer pass makes the boundary even stricter: parts of the magic interpreter directly touch actor motion, transform and RGBA/runtime state. So the effect lane is not just a stack of preview planes waiting for timing.</div>
      <ul>${actorHookRows}</ul>
      <div class="pathLine">Implication: the real battle effect path is already entangled with active-instance motion/runtime state, which is much closer to "records + actor hooks" than to "textures layered by slot".</div>
    </div>
  `);

  const callbackFamily = STATIC_CALLBACK_FAMILY_SAMPLES.find((row) => row.ids.includes(entry.magicId));
  if (callbackFamily) {
    const callbackRows = callbackFamily.highlights
      .map((row) => `<li>${escapeHtml(row)}</li>`)
      .join("");
    blocks.push(`
      <div class="structureBlock">
        <div class="blockHead"><strong>Sampled Callback Family</strong><span>${escapeHtml(callbackFamily.family)}</span></div>
        <div class="nodeMeta">${escapeHtml(callbackFamily.summary)}</div>
        <ul>${callbackRows}</ul>
        <div class="pathLine">${escapeHtml(callbackFamily.implication)}</div>
      </div>
    `);
  }

  if (entry.overlay) {
    const repeatedSlotCount = Math.max(0, Number(entry.overlay.nonzeroSlotCount || 0) - Number(entry.overlay.uniqueTargetCount || 0));
    const aliasNote = repeatedSlotCount > 0
      ? `This table collapses ${entry.overlay.nonzeroSlotCount} nonzero slots into ${entry.overlay.uniqueTargetCount} unique targets, so at least ${repeatedSlotCount} slots are repeated aliases. Sampled DLL callback passes show some of these dominant repeats are literal null stubs or return-0 helpers, not 1:1 rich effect phases.`
      : "This table does not show repeated nonzero slot aliases in the current catalog row.";
    blocks.push(`
      <div class="structureBlock">
        <div class="blockHead"><strong>${escapeHtml(entry.overlay.dllName)}</strong><span>overlay table</span></div>
        <div class="nodeMeta">${entry.overlay.nonzeroSlotCount}/${entry.overlay.slotCountRead} nonzero slots / unique targets ${entry.overlay.uniqueTargetCount}</div>
        <div class="pathLine">${escapeHtml(entry.overlay.slotKindSignature)}</div>
        <div class="pathLine">This row is a DLL-side callback surface. It is not the same thing as the synthetic layer stack shown in the left surface stage.</div>
        <div class="pathLine">${escapeHtml(aliasNote)}</div>
      </div>
    `);
  } else {
    blocks.push(`<div class="emptyLine">No local magicFiles overlay row indexed for this id.</div>`);
  }

  const exeRows = (state.catalog.runtimeEvidence.exeFunctions || [])
    .slice(0, 8)
    .map((row) => `<li>${escapeHtml(row.start)} ${escapeHtml(row.name)} / refs-to ${row.codeRefsTo} / refs-from ${row.codeRefsFrom}</li>`)
    .join("");

  blocks.push(`
    <div class="structureBlock">
      <div class="blockHead"><strong>EXE bridge surface</strong><span>${state.catalog.runtimeEvidence.exeFunctions.length} rows</span></div>
      <div class="nodeMeta">Bridge rows are structural EXE evidence only. Use them to reason about lifecycle ownership, not to infer synthetic-surface timing.</div>
      <ul>${exeRows}</ul>
    </div>
  `);

  const ps3dataRows = PS3DATA_REALITY_NOTES.map((row) => `<li>${escapeHtml(row)}</li>`).join("");
  blocks.push(`
    <div class="structureBlock">
      <div class="blockHead"><strong>ps3data Folder Reality</strong><span>current boundary</span></div>
      <div class="nodeMeta">The HD folder side currently reads as texture payload and texture-list metadata. No explicit per-magic timeline file is proved in this lane yet.</div>
      <ul>${ps3dataRows}</ul>
    </div>
  `);

  if (entry.captureEvidence) {
    const sampleRows = (entry.captureEvidence.samplePaths || [])
      .slice(0, 8)
      .map((path) => `<li>${escapeHtml(path)}</li>`)
      .join("");
    blocks.push(`
      <div class="structureBlock">
        <div class="blockHead"><strong>Capture Lab Pilot</strong><span>${escapeHtml(entry.captureEvidence.evidenceBand)}</span></div>
        <div class="nodeMeta">${entry.captureEvidence.hashIndexedTextureCount} indexed textures / dims ${escapeHtml((entry.captureEvidence.uniqueDimensions || []).join(", ") || "-")} / recipe ${entry.captureEvidence.hasRecipeScript ? "present" : "missing"}</div>
        ${entry.captureEvidence.recipeScriptPath ? `<div class="pathLine">${escapeHtml(entry.captureEvidence.recipeScriptPath)}</div>` : ""}
        <ul>${sampleRows || "<li>No sample texture paths recorded.</li>"}</ul>
      </div>
    `);
  }

  el.runtimePanel.innerHTML = blocks.join("");
}

function renderCrosswalk(entry) {
  el.crosswalkPanel.innerHTML = (entry.crosswalk || [])
    .map((row) => `<div class="structureBlock"><div class="nodeMeta">${escapeHtml(row)}</div></div>`)
    .join("");
}

function renderRuntimeMap(entry) {
  el.viewportStack?.classList.add("runtimeMode");
  el.runtimeMap.classList.add("active");

  const dll = entry.runtimeDll;
  const overlaySlots = (dll?.overlaySlots || entry.overlay?.slots || []).slice(0, 16);
  const codeCount = overlaySlots.filter((slot) => slot.kind === "code").length;
  const dataCount = overlaySlots.filter((slot) => slot.kind === "data").length;
  const nullCount = overlaySlots.filter((slot) => slot.kind === "null").length;
  const imports = dll?.importLibraries || [];
  const exports = dll?.exports || [];
  const sections = dll?.sections || [];
  const maxSectionSize = Math.max(...sections.map((section) => Number(section.rawSize || section.virtualSize || 1)), 1);
  const engineFamilies = (dll?.stringFamilies || []).slice(0, 8);
  const topStrings = (dll?.sampleStrings || []).slice(0, 10);

  const slotHtml = overlaySlots.map((slot) => `
    <div class="runtimeMapSlot ${escapeClass(slot.kind || "unknown")}">
      <div class="runtimeMapSlotTop">
        <strong>${String(slot.index).padStart(2, "0")}</strong>
        <span>${escapeHtml(slot.kind || "-")}</span>
      </div>
      <b>${escapeHtml(slot.roleName || "unknown")}</b>
      <small>${escapeHtml(slot.rva || slot.virtualAddress || "-")} ${slot.section ? `/ ${escapeHtml(slot.section)}` : ""}</small>
    </div>
  `).join("");

  const importHtml = imports.slice(0, 8).map((lib) => `
    <div class="runtimeMapRow">
      <strong>${escapeHtml(lib.library)}</strong>
      <span>${lib.count} imports</span>
      <em>${escapeHtml((lib.sampleImports || []).slice(0, 5).join(", ") || "-")}</em>
    </div>
  `).join("");

  const exportHtml = exports.slice(0, 10).map((row) => `
    <div class="runtimeMapRow compact">
      <strong>${escapeHtml(row.name || `Ordinal_${row.ordinal}`)}</strong>
      <span>${escapeHtml(row.rva || "-")}</span>
    </div>
  `).join("");

  const sectionHtml = sections.map((section) => {
    const width = Math.max(8, Math.round((Number(section.rawSize || section.virtualSize || 1) / maxSectionSize) * 100));
    return `
      <div class="runtimeSectionRow">
        <div>
          <strong>${escapeHtml(section.name || "[blank]")}</strong>
          <span>${escapeHtml(section.virtualAddress)} / ${formatBytes(section.rawSize || section.virtualSize)}</span>
        </div>
        <i style="width:${width}%"></i>
      </div>
    `;
  }).join("");

  const familyHtml = engineFamilies.map((family) => `
    <div class="runtimeFamily">
      <strong>${escapeHtml(family.family)}</strong>
      <span>${family.count}</span>
      <small>${escapeHtml((family.examples || []).slice(0, 3).join(", ") || "-")}</small>
    </div>
  `).join("");

  const stringHtml = topStrings.map((value) => `<li>${escapeHtml(value)}</li>`).join("");
  const payloadRows = [
    ["DLL", dll?.relativePath || entry.sources.runtimeDllPath || "missing"],
    ["Phyre", entry.sources.ps3MagicPath || "missing"],
    ["PS2", entry.sources.ps2PackagePath || "missing"],
    ["Preview", `${entry.previewUrls.length} copied PNG / surface-only`],
  ].map(([label, value]) => `
    <div class="runtimePayloadRow">
      <strong>${escapeHtml(label)}</strong>
      <span>${escapeHtml(value)}</span>
    </div>
  `).join("");

  if (!dll) {
    el.runtimeMap.innerHTML = `
      <div class="runtimeMapShell">
        <section class="runtimeMapHero danger">
          <span>Runtime DLL missing</span>
          <h2>${escapeHtml(entry.label)}</h2>
          <p>No parsed <code>magicFiles/FFX/magic_####.dll</code> carrier is attached to this catalog entry. Surface previews remain secondary evidence.</p>
        </section>
        <section class="runtimeMapPanel">
          <h3>Available Sources</h3>
          <div class="runtimePayload">${payloadRows}</div>
        </section>
      </div>
    `;
    el.stageNote.textContent = `${entry.label}: runtime DLL missing. The central pane is refusing to pretend texture previews are engine behavior.`;
    return;
  }

  el.runtimeMap.innerHTML = `
    <div class="runtimeMapShell">
      <section class="runtimeMapHero">
        <span>In-game DLL carrier</span>
        <h2>${escapeHtml(dll.dllName)}</h2>
        <p>${escapeHtml(dll.peKind)} / ${escapeHtml(dll.machine)} / ${formatBytes(dll.fileSize)} / SHA256 ${escapeHtml((dll.sha256 || "").slice(0, 16))}...</p>
        <div class="runtimeMapStats">
          <div><strong>${dll.exportCount}</strong><span>exports</span></div>
          <div><strong>${dll.importCount}</strong><span>imports</span></div>
          <div><strong>${dll.sectionCount}</strong><span>sections</span></div>
          <div><strong>${codeCount}/${dataCount}/${nullCount}</strong><span>code/data/null</span></div>
        </div>
      </section>

      <section class="runtimeMapPanel runtimeMapSlots">
        <div class="runtimeMapPanelHead">
          <h3>Overlay callback table</h3>
          <span>${overlaySlots.length || 0} slots from GetEffectOverlayTable CSV</span>
        </div>
        <div class="runtimeMapSlotGrid">${slotHtml || `<div class="runtimeMapEmpty">No overlay slot row attached.</div>`}</div>
      </section>

      <section class="runtimeMapPanel runtimeMapSections">
        <div class="runtimeMapPanelHead">
          <h3>PE sections</h3>
          <span>${sections.length} sections</span>
        </div>
        <div class="runtimeSectionList">${sectionHtml || `<div class="runtimeMapEmpty">No section table decoded.</div>`}</div>
      </section>

      <section class="runtimeMapPanel">
        <div class="runtimeMapPanelHead">
          <h3>Exports</h3>
          <span>${exports.length}</span>
        </div>
        <div class="runtimeMapRows">${exportHtml || `<div class="runtimeMapEmpty">No named exports decoded.</div>`}</div>
      </section>

      <section class="runtimeMapPanel">
        <div class="runtimeMapPanelHead">
          <h3>Imports</h3>
          <span>${imports.length} libraries</span>
        </div>
        <div class="runtimeMapRows">${importHtml || `<div class="runtimeMapEmpty">No imports decoded.</div>`}</div>
      </section>

      <section class="runtimeMapPanel">
        <div class="runtimeMapPanelHead">
          <h3>Runtime strings</h3>
          <span>${dll.engineStringCount} engine-like / ${dll.asciiStringCount} ascii</span>
        </div>
        <div class="runtimeFamilies">${familyHtml || `<div class="runtimeMapEmpty">No family grouping.</div>`}</div>
        <ul class="runtimeStringList">${stringHtml || `<li>No sample strings.</li>`}</ul>
      </section>

      <section class="runtimeMapPanel">
        <div class="runtimeMapPanelHead">
          <h3>Payload links</h3>
          <span>DLL first, textures second</span>
        </div>
        <div class="runtimePayload">${payloadRows}</div>
      </section>
    </div>
  `;
  el.stageNote.textContent = `${entry.label}: central pane is reading the DLL carrier and overlay table. Cycle/Stack are secondary texture inspection modes.`;
}

function hideRuntimeMap() {
  el.viewportStack?.classList.remove("runtimeMode");
  el.runtimeMap.classList.remove("active");
  el.runtimeMap.innerHTML = "";
}

async function renderStage(entry) {
  clearStage();
  renderStageHud(entry);
  setStatus(`Rendering ${entry.label}`);
  if (state.stageMode === "runtime") {
    renderRuntimeMap(entry);
    setStatus(`${entry.label}: runtime DLL map`);
    return;
  }

  hideRuntimeMap();
  if (state.stageMode === "simulation") {
    await renderRuntimeSimulation(entry);
    fitCamera();
    setStatus(`${entry.label}: runtime simulation candidate`);
    return;
  }

  const stagePreviewUrls = getStagePreviewUrls(entry);
  if (stagePreviewUrls.length > 0) {
    const sourceKind = entry.previewUrls.length > 0 ? "hd" : getPs2PreviewUrls(entry).length > 0 ? "ps2" : "rsd";
    await renderPreviewPlanes(stagePreviewUrls, sourceKind, entry);
    fitCamera();
    setStatus(`${entry.label}: synthetic surface stage`);
    return;
  }

  renderProceduralFallback(entry.magicId);
  fitCamera();
  setStatus(`${entry.label}: procedural preview fallback`);
}

async function renderRuntimeSimulation(entry) {
  const group = new THREE.Group();
  group.name = "runtimeSimulationStage";
  group.userData.kind = "runtimeSimulation";
  group.userData.plan = buildRuntimeSimulationPlan(entry);
  scene.add(group);
  state.stageGroup = group;

  const plan = group.userData.plan;
  addRuntimePhaseRings(group, plan);
  addRuntimeCursor(group, plan);
  addRuntimeCallbackOrbit(group, plan);
  addParticleHalo(group, plan.seed, Math.max(4, plan.activeCodeSlots));

  const previewUrls = getStagePreviewUrls(entry).slice(0, 4);
  if (previewUrls.length > 0) {
    const textures = await Promise.all(previewUrls.map((url) => textureLoader.loadAsync(new URL(url, catalogUrl.href).href)));
    textures.forEach((texture, index) => {
      texture.colorSpace = THREE.SRGBColorSpace;
      texture.anisotropy = renderer.capabilities.getMaxAnisotropy();
      const planeSize = getPlaneSizeForTexture(texture, 1.15 + index * 0.18);
      const material = new THREE.MeshBasicMaterial({
        map: texture,
        transparent: true,
        opacity: 0.52 - index * 0.08,
        blending: THREE.AdditiveBlending,
        depthWrite: false,
        side: THREE.DoubleSide,
        alphaTest: 0.01,
      });
      const plane = new THREE.Mesh(new THREE.PlaneGeometry(planeSize.width, planeSize.height), material);
      const angle = (index / Math.max(1, previewUrls.length)) * Math.PI * 2;
      plane.position.set(Math.cos(angle) * 0.44, Math.sin(angle) * 0.26, 0.08 - index * 0.11);
      plane.rotation.z = angle + index * 0.23;
      plane.userData.kind = "runtimePayload";
      plane.userData.basePosition = plane.position.clone();
      plane.userData.baseRotation = plane.rotation.z;
      plane.userData.phase = index * 0.6;
      group.add(plane);
    });
  } else {
    addEnergySprites(group, 4);
  }

  el.stageNote.textContent = "Runtime Simulation Candidate: continuous loop driven by EXE phase/root/callback evidence. It is less fake than texture cycling, but still not frame-accurate RT2 playback.";
}

function buildRuntimeSimulationPlan(entry) {
  const overlaySlots = (entry.runtimeDll?.overlaySlots || entry.overlay?.slots || []).slice(0, 16);
  const codeSlots = overlaySlots.filter((slot) => slot.kind === "code");
  const dataSlots = overlaySlots.filter((slot) => slot.kind === "data");
  const nullSlots = overlaySlots.filter((slot) => slot.kind === "null");
  const uniqueTargets = Number(entry.overlay?.uniqueTargetCount || new Set(codeSlots.map((slot) => slot.rva || slot.virtualAddress || slot.index)).size || 1);
  const nonzeroSlots = Number(entry.overlay?.nonzeroSlotCount || codeSlots.length + dataSlots.length);
  const repeatedAliases = Math.max(0, nonzeroSlots - uniqueTargets);
  const seed = Number.parseInt(entry.magicId, 10) || 1;
  return {
    seed,
    overlaySlots,
    activeCodeSlots: codeSlots.length,
    dataSlots: dataSlots.length,
    nullSlots: nullSlots.length,
    uniqueTargets,
    repeatedAliases,
    phaseSpeed: 0.72 + Math.min(0.48, uniqueTargets * 0.045),
    cursorRadius: 0.72 + Math.min(0.42, Math.max(1, codeSlots.length) * 0.025),
    hasDll: Boolean(entry.runtimeDll),
    hasPayload: getStagePreviewUrls(entry).length > 0,
  };
}

function addRuntimePhaseRings(group, plan) {
  const phaseColors = [0x5e91c2, 0xd08b4c, 0x67a16d];
  const phaseLabels = ["sub_800530", "sub_800590", "sub_800950"];
  for (let index = 0; index < 3; index++) {
    const radius = 0.88 + index * 0.42;
    const ring = new THREE.Mesh(
      new THREE.RingGeometry(radius, radius + 0.055, 96),
      new THREE.MeshBasicMaterial({
        color: phaseColors[index],
        transparent: true,
        opacity: 0.3 + index * 0.04,
        side: THREE.DoubleSide,
        blending: THREE.AdditiveBlending,
        depthWrite: false,
      })
    );
    ring.userData.kind = "runtimePhase";
    ring.userData.phaseIndex = index;
    ring.userData.label = phaseLabels[index];
    ring.userData.baseScale = 1;
    ring.position.z = -0.16 * index;
    group.add(ring);
  }
}

function addRuntimeCursor(group, plan) {
  const cursor = new THREE.Group();
  cursor.name = "root84_88_cursor";
  cursor.userData.kind = "runtimeCursor";
  cursor.userData.radius = plan.cursorRadius;

  const core = new THREE.Mesh(
    new THREE.CircleGeometry(0.12, 48),
    new THREE.MeshBasicMaterial({
      color: 0xfff1cf,
      transparent: true,
      opacity: 0.92,
      side: THREE.DoubleSide,
      blending: THREE.AdditiveBlending,
      depthWrite: false,
    })
  );
  core.userData.kind = "runtimeCursorCore";
  cursor.add(core);

  const packet = new THREE.Mesh(
    new THREE.PlaneGeometry(0.38, 0.12),
    new THREE.MeshBasicMaterial({
      color: 0xff8f70,
      transparent: true,
      opacity: 0.78,
      side: THREE.DoubleSide,
      blending: THREE.AdditiveBlending,
      depthWrite: false,
    })
  );
  packet.userData.kind = "runtimePacket";
  packet.position.x = 0.22;
  packet.rotation.z = -0.45;
  cursor.add(packet);

  group.add(cursor);
}

function addRuntimeCallbackOrbit(group, plan) {
  const slots = plan.overlaySlots.filter((slot) => slot.kind !== "null").slice(0, 16);
  const count = Math.max(1, slots.length);
  slots.forEach((slot, index) => {
    const isData = slot.kind === "data";
    const geometry = isData ? new THREE.BoxGeometry(0.13, 0.13, 0.02) : new THREE.CircleGeometry(0.07, 24);
    const material = new THREE.MeshBasicMaterial({
      color: isData ? 0xd8b35d : 0x78a8d8,
      transparent: true,
      opacity: isData ? 0.72 : 0.58,
      side: THREE.DoubleSide,
      blending: THREE.AdditiveBlending,
      depthWrite: false,
    });
    const marker = new THREE.Mesh(geometry, material);
    const angle = (index / count) * Math.PI * 2;
    marker.position.set(Math.cos(angle) * 1.9, Math.sin(angle) * 1.12, -0.38);
    marker.userData.kind = "runtimeSlot";
    marker.userData.slotIndex = Number(slot.index || index);
    marker.userData.baseAngle = angle;
    marker.userData.radiusX = 1.9;
    marker.userData.radiusY = 1.12;
    marker.userData.isData = isData;
    group.add(marker);
  });
}

async function renderPreviewPlanes(previewUrls, sourceKind, entry) {
  const group = new THREE.Group();
  group.name = "magicStage";
  const previewCount = Math.max(1, previewUrls.length);
  const visualIntensity = Math.min(6, previewCount);
  addBackdropRings(group, visualIntensity);
  addEnergySprites(group, visualIntensity);
  addParticleHalo(group, previewCount * 97 + 11, visualIntensity);
  scene.add(group);
  state.stageGroup = group;
  const urls = state.stageMode === "cycle" ? previewUrls.slice(0, 1) : previewUrls;
  const textures = await Promise.all(urls.map((url) => textureLoader.loadAsync(new URL(url, catalogUrl.href).href)));
  textures.forEach((texture, index) => {
    texture.colorSpace = THREE.SRGBColorSpace;
    texture.anisotropy = renderer.capabilities.getMaxAnisotropy();
    const planeScale = 2.9 + index * 0.42;
    const matteScale = 3.2 + index * 0.44;
    const planeSize = getPlaneSizeForTexture(texture, planeScale);
    const matteSize = getPlaneSizeForTexture(texture, matteScale);
    const material = new THREE.MeshBasicMaterial({
      map: texture,
      transparent: true,
      opacity: state.stageMode === "cycle" ? 1 : Math.max(0.18, 0.88 - index * 0.1),
      blending: THREE.AdditiveBlending,
      depthWrite: false,
      side: THREE.DoubleSide,
      alphaTest: 0.01,
    });
    const matte = new THREE.Mesh(
      new THREE.PlaneGeometry(matteSize.width, matteSize.height),
      new THREE.MeshBasicMaterial({
        color: 0x100d11,
        transparent: true,
        opacity: Math.max(0.14, 0.28 - index * 0.02),
        depthWrite: false,
        side: THREE.DoubleSide,
      })
    );
    matte.position.set(index % 2 === 0 ? -0.12 : 0.12, index * -0.03, -0.34 - index * 0.13);
    matte.userData.stageIndex = index + 20;
    matte.userData.kind = "matte";
    group.add(matte);

    const plane = new THREE.Mesh(new THREE.PlaneGeometry(planeSize.width, planeSize.height), material);
    plane.position.z = -index * 0.14;
    plane.position.x = index % 2 === 0 ? -0.08 : 0.08;
    plane.position.y = index * -0.025;
    plane.rotation.z = (index % 2 === 0 ? -1 : 1) * index * 0.08;
    plane.userData.stageIndex = index;
    plane.userData.kind = "preview";
    plane.userData.baseRotation = plane.rotation.z;
    group.add(plane);
  });

  if (state.stageMode === "cycle" && previewUrls.length > 1) {
    const allUrls = previewUrls.map((url) => new URL(url, catalogUrl.href).href);
    group.userData.cycleUrls = allUrls;
    group.userData.cycleTextureIndex = 0;
  }

  el.stageNote.textContent = sourceKind === "ps2"
    ? "Synthetic Surface Stage: PS2 TM2 rasters staged in Three.js. Cycle/Stack only change inspection containment; they do not assert engine timing."
    : sourceKind === "rsd"
      ? "Synthetic Surface Stage: PS2 RSD static rasters staged in Three.js. Cycle/Stack only change inspection containment; they do not assert engine timing."
    : "Synthetic Surface Stage: copied HD preview PNGs staged in Three.js. Cycle/Stack only change inspection containment; they do not assert engine timing.";
}

function getPlaneSizeForTexture(texture, maxSide) {
  const rawWidth = texture?.image?.width || texture?.source?.data?.width || 1;
  const rawHeight = texture?.image?.height || texture?.source?.data?.height || 1;
  const width = Math.max(1, rawWidth);
  const height = Math.max(1, rawHeight);
  const aspect = width / height;
  return aspect >= 1
    ? { width: maxSide, height: maxSide / aspect }
    : { width: maxSide * aspect, height: maxSide };
}

function renderProceduralFallback(magicId) {
  const group = new THREE.Group();
  group.name = "magicStage";
  const seed = Number.parseInt(magicId, 10) || 1;
  const palette = [0x8a2f2b, 0xc96b29, 0xd4b04f, 0x4a6a4f, 0x315f8b];
  addBackdropRings(group);
  addEnergySprites(group, 3);
  addParticleHalo(group, seed);

  for (let index = 0; index < 4; index++) {
    const radius = 0.6 + index * 0.38;
    const geometry = new THREE.RingGeometry(radius, radius + 0.08, 64);
    const material = new THREE.MeshBasicMaterial({
      color: palette[(seed + index) % palette.length],
      transparent: true,
      opacity: 0.7 - index * 0.12,
      side: THREE.DoubleSide,
      blending: THREE.AdditiveBlending,
      depthWrite: false,
    });
    const ring = new THREE.Mesh(geometry, material);
    ring.userData.stageIndex = index;
    ring.userData.kind = "ring";
    ring.position.z = -index * 0.14;
    group.add(ring);
  }

  scene.add(group);
  state.stageGroup = group;
  el.stageNote.textContent = "Synthetic Surface fallback: no copied HD preview was available, so the viewer renders a catalog-only placeholder. This still says nothing about runtime timing.";
}

function addBackdropRings(group, intensity = 3) {
  const colors = [0x8a2f2b, 0xc96b29, 0x315f8b];
  const core = new THREE.Mesh(
    new THREE.CircleGeometry(0.78, 80),
    new THREE.MeshBasicMaterial({
      color: 0xd46f3c,
      transparent: true,
      opacity: 0.26,
      side: THREE.DoubleSide,
      blending: THREE.AdditiveBlending,
      depthWrite: false,
    })
  );
  core.position.z = -0.72;
  core.userData.stageIndex = 99;
  core.userData.kind = "core";
  group.add(core);

  const ringCount = Math.max(3, Math.min(5, intensity));
  for (let index = 0; index < ringCount; index++) {
    const radius = 0.85 + index * 0.48;
    const geometry = new THREE.RingGeometry(radius, radius + 0.06, 80);
    const material = new THREE.MeshBasicMaterial({
      color: colors[index % colors.length],
      transparent: true,
      opacity: Math.max(0.12, 0.42 - index * 0.06),
      side: THREE.DoubleSide,
      blending: THREE.AdditiveBlending,
      depthWrite: false,
    });
    const ring = new THREE.Mesh(geometry, material);
    ring.position.z = -0.5 - index * 0.15;
    ring.userData.stageIndex = index + 4;
    ring.userData.kind = "ring";
    group.add(ring);
  }
}

function addEnergySprites(group, intensity) {
  const colors = [0xd46f3c, 0xca4f55, 0x4d74b0];
  const spriteCount = Math.max(3, Math.min(6, intensity + 2));
  for (let index = 0; index < spriteCount; index++) {
    const material = new THREE.SpriteMaterial({
      map: glowTexture,
      color: colors[index % colors.length],
      transparent: true,
      opacity: 0.34 - index * 0.02,
      blending: THREE.AdditiveBlending,
      depthWrite: false,
    });
    const sprite = new THREE.Sprite(material);
    const scale = 2.4 + index * 0.55;
    sprite.scale.set(scale, scale, 1);
    sprite.position.set(0, 0, -0.85 - index * 0.12);
    sprite.userData.kind = "glow";
    sprite.userData.stageIndex = index + 40;
    sprite.userData.baseScale = scale;
    sprite.userData.phase = index * 0.7;
    group.add(sprite);
  }
}

function addParticleHalo(group, seed, intensity = 3) {
  const count = 160 + Math.min(120, intensity * 16);
  const positions = new Float32Array(count * 3);
  const colors = new Float32Array(count * 3);
  const palette = [
    new THREE.Color(0xffb16d),
    new THREE.Color(0xff7a7a),
    new THREE.Color(0x87a8ff),
  ];

  for (let index = 0; index < count; index++) {
    const angle = ((seed * 0.017) + index / count) * Math.PI * 8;
    const radius = 0.58 + (index % 9) * 0.12;
    const height = ((index % 7) - 3) * 0.12;
    positions[index * 3 + 0] = Math.cos(angle) * radius;
    positions[index * 3 + 1] = height;
    positions[index * 3 + 2] = Math.sin(angle) * radius * 0.16 - 0.2;

    const color = palette[index % palette.length];
    colors[index * 3 + 0] = color.r;
    colors[index * 3 + 1] = color.g;
    colors[index * 3 + 2] = color.b;
  }

  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute("position", new THREE.BufferAttribute(positions.slice(), 3));
  geometry.setAttribute("color", new THREE.BufferAttribute(colors, 3));

  const points = new THREE.Points(
    geometry,
    new THREE.PointsMaterial({
      map: glowTexture,
      size: 0.14,
      vertexColors: true,
      transparent: true,
      opacity: 0.9,
      blending: THREE.AdditiveBlending,
      depthWrite: false,
      sizeAttenuation: true,
    })
  );
  points.userData.kind = "particles";
  points.userData.basePositions = positions;
  group.add(points);
}

function updateStageAnimation(elapsed) {
  if (state.stageMode === "runtime") {
    return;
  }

  if (!state.stageGroup) {
    return;
  }

  if (state.stageMode === "simulation") {
    updateRuntimeSimulation(elapsed);
    return;
  }

  state.stageGroup.rotation.z = elapsed * 0.12;
  state.stageGroup.children.forEach((child, index) => {
    if (child.userData.kind === "particles") {
      const positions = child.geometry.attributes.position.array;
      const basePositions = child.userData.basePositions;
      for (let positionIndex = 0; positionIndex < positions.length; positionIndex += 3) {
        positions[positionIndex + 1] =
          basePositions[positionIndex + 1] + Math.sin(elapsed * 1.6 + positionIndex * 0.03) * 0.07;
        positions[positionIndex + 2] =
          basePositions[positionIndex + 2] + Math.cos(elapsed * 1.3 + positionIndex * 0.02) * 0.04;
      }
      child.geometry.attributes.position.needsUpdate = true;
      child.rotation.z = elapsed * 0.08;
      return;
    }

    if (child.userData.kind === "preview") {
      child.rotation.z = (child.userData.baseRotation || 0) + Math.sin(elapsed * 0.22 + index * 0.35) * 0.025;
    } else {
      child.rotation.z = elapsed * (0.18 + index * 0.06);
    }
    child.position.y = Math.sin(elapsed * (0.8 + index * 0.25)) * 0.08 * (Math.min(index, 4) + 1);
    if (child.userData.kind === "glow") {
      const pulse = 0.92 + Math.sin(elapsed * 1.8 + child.userData.phase) * 0.18;
      child.scale.setScalar(child.userData.baseScale * pulse);
      child.material.opacity = 0.22 + Math.sin(elapsed * 1.4 + child.userData.phase) * 0.08;
    }
    if (child.material && "opacity" in child.material && state.stageMode !== "cycle" && child.userData.kind !== "glow") {
      child.material.opacity = Math.min(0.95, 0.42 + Math.sin(elapsed * (1.4 + index * 0.35)) * 0.18 + index * 0.08);
    }
  });

  if (state.stageMode === "cycle" && state.stageGroup.userData.cycleUrls) {
    const slot = state.stageGroup.children.find((child) => child.userData.kind === "preview");
    if (!slot) {
      return;
    }

    const cycleUrls = state.stageGroup.userData.cycleUrls;
    const nextIndex = Math.floor(elapsed * 1.4) % cycleUrls.length;
    if (state.stageGroup.userData.cycleTextureIndex !== nextIndex) {
      state.stageGroup.userData.cycleTextureIndex = nextIndex;
      textureLoader.load(cycleUrls[nextIndex], (texture) => {
        texture.colorSpace = THREE.SRGBColorSpace;
        if (slot.material.map) {
          slot.material.map.dispose();
        }
        slot.material.map = texture;
        slot.material.needsUpdate = true;
      });
    }
  }
}

function updateRuntimeSimulation(elapsed) {
  const group = state.stageGroup;
  const plan = group?.userData?.plan;
  if (!group || !plan) {
    return;
  }

  const phaseTime = elapsed * plan.phaseSpeed;
  const activePhase = Math.floor(phaseTime) % 3;
  const phaseProgress = phaseTime % 1;
  const cursorAngle = phaseTime * Math.PI * 2 + plan.seed * 0.013;
  group.rotation.z = Math.sin(elapsed * 0.16) * 0.045;

  group.children.forEach((child, index) => {
    if (child.userData.kind === "runtimePhase") {
      const isActive = child.userData.phaseIndex === activePhase;
      const pulse = isActive ? 1 + Math.sin(phaseProgress * Math.PI) * 0.13 : 0.96 + Math.sin(elapsed * 0.9 + index) * 0.025;
      child.scale.setScalar(pulse);
      child.rotation.z = elapsed * (0.18 + child.userData.phaseIndex * 0.08) * (child.userData.phaseIndex % 2 ? -1 : 1);
      child.material.opacity = isActive ? 0.56 : 0.22;
      return;
    }

    if (child.userData.kind === "runtimeCursor") {
      const radius = child.userData.radius || 0.9;
      child.position.set(Math.cos(cursorAngle) * radius, Math.sin(cursorAngle * 0.82) * radius * 0.38, 0.34);
      child.rotation.z = cursorAngle + Math.PI * 0.12;
      child.scale.setScalar(0.92 + Math.sin(phaseProgress * Math.PI) * 0.18);
      return;
    }

    if (child.userData.kind === "runtimeSlot") {
      const slotPhase = phaseTime * 0.58 + child.userData.baseAngle;
      child.position.x = Math.cos(slotPhase) * child.userData.radiusX;
      child.position.y = Math.sin(slotPhase * 1.07) * child.userData.radiusY;
      child.rotation.z = -slotPhase;
      const selected = (child.userData.slotIndex % 3) === activePhase || child.userData.isData;
      child.material.opacity = selected ? 0.78 : 0.28;
      child.scale.setScalar(selected ? 1.18 : 0.88);
      return;
    }

    if (child.userData.kind === "runtimePayload") {
      const base = child.userData.basePosition;
      const phase = child.userData.phase || 0;
      child.position.x = base.x + Math.sin(elapsed * 1.4 + phase) * 0.16;
      child.position.y = base.y + Math.cos(elapsed * 1.15 + phase) * 0.11;
      child.position.z = base.z + Math.sin(elapsed * 0.8 + phase) * 0.06;
      child.rotation.z = child.userData.baseRotation + Math.sin(elapsed * 0.7 + phase) * 0.32;
      child.material.opacity = 0.34 + Math.sin(elapsed * 1.55 + phase) * 0.13 + Math.max(0, 0.16 - index * 0.035);
      return;
    }

    if (child.userData.kind === "particles") {
      const positions = child.geometry.attributes.position.array;
      const basePositions = child.userData.basePositions;
      for (let positionIndex = 0; positionIndex < positions.length; positionIndex += 3) {
        const wave = elapsed * 2.0 + positionIndex * 0.021 + activePhase;
        positions[positionIndex + 0] = basePositions[positionIndex + 0] + Math.sin(wave) * 0.055;
        positions[positionIndex + 1] = basePositions[positionIndex + 1] + Math.cos(wave * 0.8) * 0.075;
        positions[positionIndex + 2] = basePositions[positionIndex + 2] + Math.sin(wave * 0.6) * 0.05;
      }
      child.geometry.attributes.position.needsUpdate = true;
      child.rotation.z = -elapsed * 0.12;
      child.material.opacity = 0.62 + Math.sin(phaseProgress * Math.PI) * 0.18;
      return;
    }

    if (child.userData.kind === "glow") {
      const pulse = 0.9 + Math.sin(elapsed * 1.6 + child.userData.phase) * 0.16;
      child.scale.setScalar(child.userData.baseScale * pulse);
      child.material.opacity = 0.22 + Math.sin(elapsed * 1.2 + child.userData.phase) * 0.08;
    }
  });
}

function clearStage() {
  if (!state.stageGroup) {
    return;
  }

  scene.remove(state.stageGroup);
  state.stageGroup.traverse((child) => {
    if (child.geometry) {
      child.geometry.dispose();
    }
    if (child.material) {
      const materials = Array.isArray(child.material) ? child.material : [child.material];
      materials.forEach((material) => {
        if (material.map) {
          material.map.dispose();
        }
        material.dispose();
      });
    }
  });
  state.stageGroup = null;
}

function fitCamera() {
  if (!state.stageGroup) {
    camera.position.set(0, 0, 5.5);
    controls.target.set(0, 0, 0);
    return;
  }

  const box = new THREE.Box3().setFromObject(state.stageGroup);
  const center = box.getCenter(new THREE.Vector3());
  const size = box.getSize(new THREE.Vector3());
  const radius = Math.max(size.x, size.y, size.z, 1.2);
  camera.position.copy(center).add(new THREE.Vector3(0, 0, radius * 2.6));
  controls.target.copy(center);
  controls.update();
}

function setStatus(message) {
  state.lastStatus = message;
  el.statusText.textContent = message;
}

function setStageMode(mode) {
  state.stageMode = mode;
  el.toggleRuntimeMap.setAttribute("aria-pressed", String(mode === "runtime"));
  el.toggleSimulation.setAttribute("aria-pressed", String(mode === "simulation"));
  el.toggleComposite.setAttribute("aria-pressed", String(mode === "composite"));
  el.toggleCycle.setAttribute("aria-pressed", String(mode === "cycle"));
  if (state.selected) {
    renderStageHud(state.selected);
    renderStage(state.selected).catch((error) => setStatus(error.message));
  }
}

function setActiveTab(tabName) {
  state.activeTab = tabName;
  el.tabButtons.forEach((button) => button.classList.toggle("active", button.dataset.tab === tabName));
  el.tabPanels.forEach((panel) => panel.classList.toggle("active", panel.id === `panel-${tabName}`));
  if (tabName === "textures" && state.selected) {
    renderTextures(state.selected, { enableInspector: true });
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

function escapeClass(value) {
  return String(value ?? "").replace(/[^a-zA-Z0-9_-]/g, "_");
}

function formatBytes(bytes) {
  const value = Number(bytes || 0);
  if (value >= 1024 * 1024) return `${(value / 1024 / 1024).toFixed(2)} MB`;
  if (value >= 1024) return `${(value / 1024).toFixed(1)} KB`;
  return `${value} B`;
}

function renderStageHud(entry) {
  const isRuntimeMap = state.stageMode === "runtime";
  const isSimulation = state.stageMode === "simulation";
  el.stageModeBadge.textContent = isRuntimeMap
    ? `Runtime Map: ${entry.runtimeDll ? "DLL parsed" : "DLL missing"}`
    : isSimulation
      ? `Runtime Simulation: ${entry.runtimeDll ? "DLL guided" : "no DLL"}`
    : `Surface Preview: ${state.stageMode === "cycle" ? "Cycle" : "Stack"}`;
  el.stagePs2Badge.textContent = entry.ps2Package ? `PS2 ${entry.ps2Package.fileCount} files` : "PS2 missing";
  el.stageHdBadge.textContent = entry.ps3Magic
    ? `HD ${entry.ps3Magic.textureCount} textures`
    : "HD missing";
  el.stageOverlayBadge.textContent = entry.overlay
    ? `Runtime Overlay ${entry.overlay.nonzeroSlotCount}/${entry.overlay.slotCountRead}`
    : "Overlay missing";
  if (entry.wave4Attribution) {
    el.stageOverlayBadge.textContent += ` · W4 ${entry.wave4Attribution.categoryBadge || entry.wave4Attribution.category}`;
  }

  if (isRuntimeMap) {
    el.stageHeadline.textContent = `Runtime DLL Map - ${entry.label}`;
    if (entry.runtimeDll) {
      el.stageSubline.textContent = `${entry.runtimeDll.dllName} parsed from magicFiles/FFX: ${entry.runtimeDll.exportCount} exports, ${entry.runtimeDll.importCount} imports, ${entry.runtimeDll.sectionCount} sections, ${entry.runtimeDll.overlaySlots?.length || entry.overlay?.slots?.length || 0} overlay slots.`;
    } else {
      el.stageSubline.textContent = `No magicFiles/FFX DLL is attached to ${entry.label}. Texture previews are not being promoted to runtime behavior.`;
    }

    const runtimeRows = [];
    (entry.runtimeDll?.overlaySlots || entry.overlay?.slots || []).slice(0, 8).forEach((slot) => {
      runtimeRows.push(
        `<div class="stageLayerItem"><span class="stageLayerIndex">${String(slot.index).padStart(2, "0")}</span><span class="stageLayerText">${escapeHtml(slot.roleName || slot.kind || "slot")}</span></div>`
      );
    });

    if (runtimeRows.length === 0) {
      (entry.runtimeDll?.importLibraries || []).slice(0, 5).forEach((lib, index) => {
        runtimeRows.push(
          `<div class="stageLayerItem"><span class="stageLayerIndex">${index + 1}</span><span class="stageLayerText">${escapeHtml(lib.library)} (${lib.count})</span></div>`
        );
      });
    }

    if (runtimeRows.length === 0) {
      runtimeRows.push(`<div class="stageLayerItem"><span class="stageLayerIndex">0</span><span class="stageLayerText">No runtime map rows decoded</span></div>`);
    }

    el.stageLayerList.innerHTML = runtimeRows.join("");
    return;
  }

  if (isSimulation) {
    const overlaySlots = (entry.runtimeDll?.overlaySlots || entry.overlay?.slots || []).slice(0, 16);
    const codeSlots = overlaySlots.filter((slot) => slot.kind === "code").length;
    const dataSlots = overlaySlots.filter((slot) => slot.kind === "data").length;
    const uniqueTargets = Number(entry.overlay?.uniqueTargetCount || 0);
    const repeatedAliases = Math.max(0, Number(entry.overlay?.nonzeroSlotCount || 0) - uniqueTargets);
    el.stageHeadline.textContent = `Runtime Simulation Candidate - ${entry.label}`;
    el.stageSubline.textContent = `${entry.runtimeDll ? `${entry.runtimeDll.dllName}: ` : ""}continuous phase/cursor/callback loop from EXE evidence. Payload textures are visual material, not the clock.`;
    el.stageLayerList.innerHTML = [
      `<div class="stageLayerItem"><span class="stageLayerIndex">0</span><span class="stageLayerText">sub_800530 phase 0 / root+88 fallback</span></div>`,
      `<div class="stageLayerItem"><span class="stageLayerIndex">1</span><span class="stageLayerText">sub_800590 phase 1 / sub_80CD60 + sub_80BEA0</span></div>`,
      `<div class="stageLayerItem"><span class="stageLayerIndex">2</span><span class="stageLayerText">sub_800950 phase 2 / writeback rhythm</span></div>`,
      `<div class="stageLayerItem"><span class="stageLayerIndex">21</span><span class="stageLayerText">sub_817200 cursor root+84/root+88</span></div>`,
      `<div class="stageLayerItem"><span class="stageLayerIndex">DLL</span><span class="stageLayerText">${codeSlots} code slots / ${dataSlots} data / ${uniqueTargets || "?"} unique targets</span></div>`,
      `<div class="stageLayerItem"><span class="stageLayerIndex">Alias</span><span class="stageLayerText">${repeatedAliases} repeated nonzero slot aliases</span></div>`,
    ].join("");
    return;
  }

  const hdLayerCount = entry.previewUrls.length;
  const ps2PreviewUrls = getPs2PreviewUrls(entry);
  const rsdPreviewUrls = getRsdPreviewUrls(entry);
  el.stageHeadline.textContent = `Surface Preview - ${entry.label}`;
  if (hdLayerCount > 0) {
    el.stageSubline.textContent = `${entry.runtimeDll ? `${entry.runtimeDll.dllName} parsed from magicFiles/FFX; ` : ""}${hdLayerCount} copied preview surface${hdLayerCount === 1 ? "" : "s"} from ${entry.ps3Magic?.folderName || "ps3data magic"} remain synthetic inspection material only.`;
  } else if (ps2PreviewUrls.length > 0) {
    el.stageSubline.textContent = `${entry.runtimeDll ? `${entry.runtimeDll.dllName} parsed from magicFiles/FFX; ` : ""}${ps2PreviewUrls.length} PS2 TM2 preview surface${ps2PreviewUrls.length === 1 ? "" : "s"} remain package inspection material while the runtime recipe remains blocked.`;
  } else if (rsdPreviewUrls.length > 0) {
    el.stageSubline.textContent = `${entry.runtimeDll ? `${entry.runtimeDll.dllName} parsed from magicFiles/FFX; ` : ""}${rsdPreviewUrls.length} PS2 RSD static preview surface${rsdPreviewUrls.length === 1 ? "" : "s"} are model-carrier evidence only. No runtime timing is implied here.`;
  } else if (entry.ps2Package) {
    el.stageSubline.textContent = `${entry.runtimeDll ? `${entry.runtimeDll.dllName} parsed from magicFiles/FFX. ` : ""}No copied HD previews were available, so this entry falls back to a procedural surface while preserving package/runtime evidence.`;
  } else {
    el.stageSubline.textContent = `${entry.runtimeDll ? `${entry.runtimeDll.dllName} parsed from magicFiles/FFX. ` : ""}Catalog evidence exists, but this pane still depends on synthetic fallback visuals because frame-accurate playback is not yet proved.`;
  }

  const layerRows = [];
  const previewRows = getStagePreviewUrls(entry).slice(0, 6).map((url, index) => {
    const fileName = url.split("/").pop() || url;
    return `<div class="stageLayerItem"><span class="stageLayerIndex">${index + 1}</span><span class="stageLayerText">${escapeHtml(fileName)}</span></div>`;
  });
  layerRows.push(...previewRows);

  if (layerRows.length === 0 && entry.ps2Package?.sampleFiles?.length) {
    entry.ps2Package.sampleFiles.slice(0, 4).forEach((path, index) => {
      layerRows.push(
        `<div class="stageLayerItem"><span class="stageLayerIndex">${index + 1}</span><span class="stageLayerText">${escapeHtml(path.split("\\").pop() || path.split("/").pop() || path)}</span></div>`
      );
    });
  }

  if (layerRows.length === 0) {
    layerRows.push(`<div class="stageLayerItem"><span class="stageLayerIndex">1</span><span class="stageLayerText">Procedural fallback energy field</span></div>`);
  }

  el.stageLayerList.innerHTML = layerRows.join("");
}

function createGlowTexture() {
  const canvas = document.createElement("canvas");
  canvas.width = 256;
  canvas.height = 256;
  const context = canvas.getContext("2d");
  const gradient = context.createRadialGradient(128, 128, 8, 128, 128, 128);
  gradient.addColorStop(0, "rgba(255,255,255,1)");
  gradient.addColorStop(0.16, "rgba(255,235,200,0.96)");
  gradient.addColorStop(0.4, "rgba(255,174,101,0.42)");
  gradient.addColorStop(1, "rgba(255,174,101,0)");
  context.fillStyle = gradient;
  context.fillRect(0, 0, 256, 256);

  const texture = new THREE.CanvasTexture(canvas);
  texture.colorSpace = THREE.SRGBColorSpace;
  return texture;
}

function getPs2PreviewUrls(entry) {
  return (entry.ps2Package?.tm2Previews || [])
    .map((preview) => preview.previewUrl)
    .filter(Boolean);
}

function getRsdPreviewUrls(entry) {
  return (entry.ps2Package?.modelCandidates || [])
    .map((model) => model.previewUrl)
    .filter(Boolean);
}

function getInspectableTextureSources(entry) {
  if (entry.previewUrls.length > 0) {
    return entry.previewUrls.map((url, index) => ({
      key: `hd:${url}`,
      url,
      label: url.split("/").pop() || `hd_${index + 1}`,
      kind: "hd",
      kindLabel: "HD preview",
    }));
  }

  const ps2 = getPs2PreviewUrls(entry);
  if (ps2.length > 0) {
    return ps2.map((url, index) => ({
      key: `ps2:${url}`,
      url,
      label: url.split("/").pop() || `ps2_${index + 1}`,
      kind: "ps2",
      kindLabel: "PS2 TM2 preview",
    }));
  }

  return getRsdPreviewUrls(entry).map((url, index) => ({
    key: `rsd:${url}`,
    url,
    label: url.split("/").pop() || `rsd_${index + 1}`,
    kind: "rsd",
    kindLabel: "PS2 RSD preview",
  }));
}

function getStagePreviewUrls(entry) {
  if (entry.previewUrls.length > 0) return entry.previewUrls;
  const ps2PreviewUrls = getPs2PreviewUrls(entry);
  if (ps2PreviewUrls.length > 0) return ps2PreviewUrls;
  return getRsdPreviewUrls(entry);
}

el.reloadCatalog.addEventListener("click", () => loadCatalog().catch((error) => setStatus(error.message)));
el.searchBox.addEventListener("input", renderList);
el.filters.forEach((filter) => filter.addEventListener("change", renderList));
el.fitCamera.addEventListener("click", fitCamera);
el.toggleGrid.addEventListener("click", () => {
  state.gridVisible = !state.gridVisible;
  grid.visible = state.gridVisible;
  el.toggleGrid.setAttribute("aria-pressed", String(state.gridVisible));
});
el.toggleComposite.addEventListener("click", () => setStageMode("composite"));
el.toggleCycle.addEventListener("click", () => setStageMode("cycle"));
el.toggleRuntimeMap.addEventListener("click", () => setStageMode("runtime"));
el.toggleSimulation.addEventListener("click", () => setStageMode("simulation"));
el.tabButtons.forEach((button) => button.addEventListener("click", () => setActiveTab(button.dataset.tab)));
el.textureSourceSelect.addEventListener("change", () => {
  state.textureInspector.selectedSourceKey = el.textureSourceSelect.value || null;
  if (state.selected) {
    renderTextures(state.selected);
  }
});
el.textureAlphaThreshold.addEventListener("input", () => {
  state.textureInspector.alphaThreshold = Number(el.textureAlphaThreshold.value || 24);
  el.textureAlphaThresholdValue.textContent = String(state.textureInspector.alphaThreshold);
  if (state.selected) {
    renderTextures(state.selected);
  }
});
el.textureShowBoxes.addEventListener("change", () => {
  state.textureInspector.showBoxes = el.textureShowBoxes.checked;
  if (state.selected) {
    renderTextures(state.selected);
  }
});
el.textureChannelButtons.forEach((button) => button.addEventListener("click", () => {
  state.textureInspector.channelMode = button.dataset.channel || "rgba";
  if (state.selected) {
    renderTextures(state.selected);
  }
}));

window.addEventListener("resize", resize);
resize();
setActiveTab(state.activeTab);
animate();
loadCatalog().catch((error) => setStatus(error.message));
