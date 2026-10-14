# FFXMagicViewerWeb

Static web viewer lane for FFX magic effects.

Status on 2026-06-03:

- lane created;
- phase-1 scaffold implemented;
- reads `magic-viewer-catalog.json`;
- synthetic `Three.js` stage implemented;
- first local catalog already generated under `public\magic\`;
- source-of-truth plan:
  - `docs/history/FFX_MAGIC_VIEWER_MASTER_STATUS_AND_PLAN_2026-06-03.md`

Status delta on 2026-06-04:

- `MagicCorpusIndexer` now also exports PS2 `TM2` previews for eligible `mag_*` packages into `public\magic\tm2-preview\`;
- catalog entries now carry `tm2Previews` and `modelCandidates` under `ps2Package`;
- the viewer can surface PS2 package previews and RSD carrier candidates, and can use PS2 TM2 previews as the synthetic stage fallback when HD preview copies are absent.
- the indexer now also renders first-pass static `rsd-preview` bitmaps for supported PS2 carriers into `public\magic\rsd-preview\`;
- catalog entries now carry `captureEvidence` sourced from `work\magic_capture_lab\magic_hash_index.csv` plus recipe-script presence from `work\magic_capture_lab\viewer\`;
- pilot cross for `0148 / 0151 / 0289 / 0290` is now live in the viewer as `hash_inventory_ready_no_recipe`, with real texture-count and dimension evidence.
- the `Texture Stack` tab now exposes a read-only `Texture Inspector` over the generated raster preview currently available in the catalog, with source select, channel modes (`RGBA / RGB / A / R / G / B`), alpha cut, box overlay and visual heuristics.
- local smoke on `magic=0003` and `magic=0148` confirmed inspector canvas render plus basic control response (`source`, `channel`, `alpha cut`, `show boxes`) for entries with eligible preview sources.
- the inspector surface was also hardened for the current web lane:
  - preview thumbs now resolve correctly against the generated catalog root;
  - texture-thumb overflow now scrolls inside the right rail instead of pushing the whole panel longer;
  - inspector fetch/render work is deferred until the `Textures` tab is active;
  - stale image/analysis cache is cleared on catalog reload;
  - async render token guards now prevent slow previous loads from repainting the newly selected source.
- the stage/runtime split is now explicit:
  - the left viewport is labeled `Preview Mesh` and defaults to the more contained `Cycle` synthetic preview mode;
  - the `Runtime` tab now carries a separate `Static Lifecycle / Timing Evidence` block so synthetic layer stacking does not read like proved game timing.

Status delta on 2026-06-05:

- a second runtime pass pulled extra timing/gate helpers from `FFX.exe` (`sub_643410`, `sub_643790`, `sub_642240`, `sub_642280`, `sub_6422A0`, `sub_642290`, `sub_6430C0`, `sub_643820`, `sub_820970`);
- the `Runtime` tab now also carries `Scene Gates / Helper Hooks` and `ps3data Folder Reality`, so the viewer says more clearly what is lifecycle truth vs texture-payload reality;
- a follow-up IDA pass climbed one level higher into the feeder layer (`sub_79F010`, `sub_79EF60`, `sub_79E3F0`, `sub_783C70`, `sub_788EB0`, `sub_794030`, `sub_A446A0`);
- the `Runtime` tab now also carries `Pre-instantiation Sources`, making it explicit that ET/UNIT records are fed by actor state, event codes, owner/token routing and packed runtime ids before deeper interpreter behavior;
- another IDA pass tightened the deeper root/interpreter rhythm around `dword_12A4080`: `sub_800530`, `sub_800590`, `sub_800950` now stand as a proved `phase 0 / 1 / 2` trio around `sub_80CD60`, with `sub_800590` carrying the heaviest middle setup/cleanup bookends;
- the `Runtime` tab now also carries `Interpreter Passes`, so the viewer distinguishes overlay slots from the deeper three-pass interpreter structure;
- the feeder panel was tightened again with the battle-scene queue (`FFX_BattleScene_InitStateMachine -> sub_7AD400 -> sub_7AD1A0`) and the target/participant mask resolver (`sub_794340 / sub_7B09C0`);
- the feeder panel now also calls out selector-helper semantics (`sub_79AF00 / sub_79AF70 / sub_795790 / sub_795810 / sub_793660`) so the UI shows more clearly that these masks are built from actor bands and live state flags;
- the feeder panel now also labels the `sub_794340` caller family as targeting helpers (`first-hit selection`, `relation propagation`, `compatibility/range pruning`, `mask comparison`) instead of leaving that whole cluster as a black box;
- the interpreter panel now calls out the current best `phase 1` hint: queue/buffer drains plus resource handoff/commit state, still below exact frame timing proof;
- the interpreter panel also warns that many direct `dword_12A4080` helpers are owner cleanup/rebind sweeps, so raw root xrefs should not be overread as semantic definitions of `root+84` / `root+88`;
- sampled DLL callback passes (`magic_0697.dll`, `magic_0148.dll`, `magic_0003.dll`) now prove that many repeated overlay-table slots collapse to tiny `nullsub` / `return 0` callbacks instead of rich per-slot phases;
- the runtime panel now warns that `nonzero slots` are not the same thing as `distinct behavior carriers`.
- a follow-up IDA pass on `sub_804400`, `sub_809780`, `sub_800AD0`, `sub_800B00`, `sub_8001E0`, `sub_9086D0`, `sub_90A040` and `sub_909E10` now proves that the lane also touches active-instance motion binding, resident motion selection, transform shaping, bulk per-instance toggles and RGBA/runtime state;
- the `Runtime` tab now carries `Actor / Motion Hooks`, so the viewer says more clearly that the magic lane is entangled with actor/runtime state and cannot be reduced to stacked texture quads plus guessed timing.
- the `Runtime` tab now also carries `Sampled Callback Family` for ids inside the sampled overlay families (`0697/0698`, `0148/0151/0289/0290`, `0003`, `0611/0667/0668`), so the user can see concrete examples of `return 0` stubs, rewired entries and the small set of distinct bodies that actually deserve attention.
- a fresh headless IDA reconfirmation pass now also hardens the wrapper chain itself (`sub_787FE0` / `sub_788D20` -> `sub_9DA780` -> `sub_9DA420`, `sub_7881D0` / `sub_788DD0` -> `sub_9DA7D0`, `sub_8222E0` as the start gate, `sub_788CC0` -> `sub_9DA860`, `sub_88DFE0` -> `sub_9DAC80(-1)`), so the runtime tab is anchored in direct caller evidence as well as isolated lifecycle functions.
- the `ps3data Folder Reality` wording is now also backed by a direct extension sweep over `ps3data\\magic`: only `.phyre`, `.txt`, `.log` and `.pal` are currently observed, which further weakens the case for a folder-side serialized timeline artifact.
- the viewport controls and badges now say `Preview Mesh` / `Stack Mesh` / `Cycle Mesh` explicitly, to make it harder to read viewer containment modes as engine timing modes.

Status delta on 2026-06-13:

- `MagicCorpusIndexer` now reads `magicFiles\FFX\magic_####.dll` directly and emits `runtime-dll-index.json`;
- catalog entries now carry `runtimeDll` with PE kind/machine/timestamp, SHA-256, sections, exports/imports, strings/families, and overlay slots preserved from `magicfiles-overlay-index.json`;
- the embedded viewer now opens with `Runtime Map` in the central viewport before any texture/surface preview;
- the first-screen artifact is the parsed runtime carrier: DLL/PE facts, sections, exports/imports, string families, payload links, and overlay callback/data slots;
- Phyre folders and copied preview textures remain separate payload evidence; `Cycle Surface` and `Stack Surface` are secondary inspection modes only;
- the central stage now has `Simulate`, a continuous `Runtime Simulation Candidate` mode driven by the structural EXE/DLL chain (`sub_800530/590/950`, `sub_80CD60`, `sub_817200`, overlay slots and Phyre payloads). This is intentionally labeled candidate: it is less fake than texture cycling, but still not RT2/frame-accurate playback;
- local validation regenerated the catalog with `587 entries`, `580 ps3 magic`, `583 runtime DLLs`, `581 overlay rows`, then checked `magic=0688` and `magic=0003` in Edge headless with `Runtime Map` active, `slotCount=16`, no page errors, and no horizontal overflow at 390px mobile width. The follow-up `Simulate` smoke checked `magic=0688` in Chromium headless desktop/mobile, clicked the mode successfully, produced screenshots, and kept the same no-error/no-mobile-overflow bar.

Current inspector guardrail:

- it works on the generated preview raster currently exposed by the catalog;
- current truth is `preview-only` / current raster (`mip 0` in the present HD lane);
- do not claim raw `.dds.phyre` inspection, full mip-chain proof, atlas sub-rect proof, UV/runtime recipe proof or engine-accurate texture truth from this surface.
- do not read `Stack Surface` / `Cycle Surface` preview modes as effect timing modes; they are preview-inspection modes only.
- do not read `Simulate` as engine-accurate playback; it is structural runtime simulation until an RT2 probe correlates phase/callback/opcode/root fields with captured frames.
- do not read `16 slots` as `16 meaningful phases`; repeated aliases and stub callbacks are now proved in sampled DLL families.

## Goal

Unify the current magic lanes into one read-only viewer:

- PS2 kernel context (`magic.bin`, `command.bin`, `monmagic1/2`);
- PS2 effect/presentation packages (`mag_*`, `bat_eff`);
- PS3 HD texture lane (`ps3data\magic\magic_####`);
- `magicFiles\FFX\magic_####.dll` overlay evidence;
- capture/runtime evidence from `work/magic_capture_lab`.

## Expected generated inputs

The viewer should consume generated local output, not crawl the game corpus at runtime.

Planned catalog files:

```text
RuntimeTools\FFXMagicViewerWeb\public\magic\magic-viewer-catalog.json
RuntimeTools\FFXMagicViewerWeb\public\magic\kernel-index.json
RuntimeTools\FFXMagicViewerWeb\public\magic\mag-package-index.json
RuntimeTools\FFXMagicViewerWeb\public\magic\bat-eff-index.json
RuntimeTools\FFXMagicViewerWeb\public\magic\ps3-magic-index.json
RuntimeTools\FFXMagicViewerWeb\public\magic\magicfiles-overlay-index.json
RuntimeTools\FFXMagicViewerWeb\public\magic\runtime-dll-index.json
RuntimeTools\FFXMagicViewerWeb\public\magic\runtime-evidence-index.json
RuntimeTools\FFXMagicViewerWeb\public\magic\capture-evidence-index.json
```

Generated preview assets:

```text
RuntimeTools\FFXMagicViewerWeb\public\magic\thumbs\
RuntimeTools\FFXMagicViewerWeb\public\magic\tm2-preview\
RuntimeTools\FFXMagicViewerWeb\public\magic\rsd-preview\
```

Current truth:

- `thumbs\` = HD preview copies from exported `ps3data\magic`;
- `tm2-preview\` = generated PS2 package previews for previewable TIM2 cohorts;
- `rsd-preview\` = generated first-pass static carrier previews from `RSD/PLY` geometry;
- `runtime-dll-index.json` = parsed `magicFiles\FFX\magic_####.dll` PE/runtime carrier evidence;
- `capture-evidence-index.json` = hash inventory + pilot recipe-script presence from `work\magic_capture_lab`.

`public\magic\` should stay local output and be gitignored once generation starts.

## Viewer scope

Phase 1:

- spell index;
- package graph;
- crosswalk panel;
- texture stack panel;
- runtime evidence panel.

Phase 2:

- synthetic effect stage in `Three.js`;
- static object preview for supported PS2 carriers.

Phase 3:

- capture-driven timeline and recipe candidate playback.

Current phase-3 truth:

- hash-inventory correlation is live;
- recipe-script coverage is still sparse (`recipe_0003.js` present; pilot recipes for `0148 / 0151 / 0289 / 0290` still absent);
- do not claim runtime recipe proof from the current capture panel.

## Guardrails

- read-only only;
- no writer;
- no repack;
- do not claim engine-accurate playback until runtime proof exists;
- keep `proved`, `structural`, `guess`, and `blocked` visible in the UI.
