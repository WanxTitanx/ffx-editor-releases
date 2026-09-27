# noclip.website runtime provenance

- Upstream: `magcius/noclip.website`
- Source snapshot supplied locally: `39605028765aa2cfaf2cea175f01f3a77cd99c2e`
- Build date: 2026-08-21
- License: MIT (`LICENSE` in this directory)
- Build command: `PUBLIC_STORAGE_URL=/data npm run build`, with Rsbuild
  `output.assetPrefix=/noclip/`
- Runtime adaptation: game data is never bundled or downloaded. The compiled frontend resolves
  assets only below the FFX Mod Studio loopback `/data/` route, which is mapped to a user-selected,
  locally validated FFX extraction.

The source archive did not contain `.git`, so the directory name is retained as snapshot provenance,
not claimed as a cryptographically verified Git checkout. Release verification pins every file in
`dist-ffxstudio/` by SHA-256 and rejects any reappearance of the former public storage origin.

## Spira Reforge Studio integration

The maintained addon source is in `RuntimeTools/FFXNoclipStudio/`. It adds Studio
branding (using the existing editor logo), preserves upstream credits, hides the
general game picker in Aurora, Monster Studio and Magic Studio, and provides monster X/Z
placement through a project-bound, authenticated same-origin save route.

Run `python3 RuntimeTools/FFXNoclipStudio/sync_runtime.py` from the repository to
embed the source JS/CSS/logo in both runtime indexes and apply the narrow
renderer hooks. `--check` verifies generated content and the affected runtime pins.
Embedding keeps the existing runtime file inventory intact. The renderer still
comes from the upstream snapshot above; Studio additions are maintained separately.

The placement editor preserves Y/W and unrelated battle bytes and creates an
`.aurora.bak` before its first write. It is a scene/formation preview, not proof of
complete FFX battle AI or in-game runtime behaviour.

### Working PC magic preview (2026-09-23)

`magic-preview.js` consumes an explicit, session-owned PPP resource manifest. The
host extracts the PC DLL's `.data` bytes and resolves resource-header links and
local handler names; it does not run x86 code or pass a PE image to MIPS sniffing.
The existing NoClip particle/texture reader and renderer consume those working
bytes. `magic-preview-evidence.json` records 19 paired PC/PS2 table hashes used to
map 81 handler names. The maintained hook adds ZXY matrix order for opcode 137.
PC previews do not execute the PS2 cast controller against PC bytecode; supported
particle emitters run directly. This is not full game-casting/runtime parity.

Matching local `.dds.phyre` project textures are decoded by the existing host
reader into session-owned RGBA files. Project overrides take precedence over
extracted textures by filename; matching requires GS address, pixel format and
original dimensions, with no list-order or ambiguous-palette substitutions.
Every update publishes a new content revision and reloads the same viewer session.
