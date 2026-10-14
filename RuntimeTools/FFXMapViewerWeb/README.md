# FFXMapViewerWeb

Static Three.js viewer for read-only FFX HD `ps3data\map` exports.

The viewer now supports a generated map catalog:

```text
RuntimeTools\FFXMapViewerWeb\public\maps\catalog.json
RuntimeTools\FFXMapViewerWeb\public\maps\status.json
RuntimeTools\FFXMapViewerWeb\public\maps\status.csv
```

`public\maps\` is generated local output and is intentionally gitignored. The
current local batch is about 5.6 GB.

## Run

Serve the repository root over HTTP, then open the viewer:

```powershell
cd C:\Users\wande\Documents\ffx-editor-main
python -m http.server 8765 --bind 127.0.0.1
```

Open:

```text
http://127.0.0.1:8765/RuntimeTools/FFXMapViewerWeb/index.html
```

Open a specific map:

```text
http://127.0.0.1:8765/RuntimeTools/FFXMapViewerWeb/index.html?map=map/kino/kino07
```

Do not open `index.html` directly from the filesystem. The viewer fetches glTF,
PNG, and JSON files, so it needs an HTTP origin.

## Generate The Catalog

Input list:

```text
RuntimeTools\FFXMapViewerWeb\prompts\2026-06-03-map-batch-web-ready\map-entities.csv
```

Batch command:

```powershell
dotnet run --project RuntimeTools\PhyreMapExportLab\PhyreMapExportLab.csproj -- batch --portable --skip-failed --list RuntimeTools\FFXMapViewerWeb\prompts\2026-06-03-map-batch-web-ready\map-entities.csv --output-root RuntimeTools\FFXMapViewerWeb\public\maps
```

Current local batch, generated on 2026-06-03:

- 299 `map/<area>/<field_token>` entries cataloged.
- 298 entries generated a glTF and open in the web viewer.
- 240 entries use `root_3d` manifests and 58 use the `2d_prerendered`
  fallback (`2d/<field>.ahwin32` + `2d/mdl/d3d11/<field>.dae.phyre`).
- 1 entry remains blocked in the catalog: `map/dream/fp`, which has no root or
  `2d` `.ahwin32` manifest.
- `map/azit/azit00` remains the textured Phyre-slot pilot.
- `map/azit/azit05`, `map/bsvr/bsvr05`, `map/guad/guad01`, and
  `map/titl/titl00` now open as `2d_prerendered` candidates instead of blocked
  entries.
- Texture status split: 289 `decoded_png_bound_by_submesh_candidate`, 1
  `decoded_png_bound_by_file_material_id_candidate`, 8 `texture_not_requested`,
  1 `texture_not_attempted`.

## Controls

- `Filter maps`: narrow the catalog by path, layer, or status.
- Map list: choose any generated or blocked map.
- `Orbit`: inspect the map candidate with orbit camera.
- `Inside`: place the camera near the decoded bounds center.
- `Walk` / `Enter Walk`: no-clip camera with mouse look.
- `WASD`: move horizontally in walk mode.
- `Q` / `E`: move down/up in walk mode.
- `Fit`, `Top`, `Reset`: camera presets.
- `Grid`, `Wireframe`, `Axes`: debug display toggles.

## Evidence

Smoke run on 2026-06-04:

- `dotnet build RuntimeTools\PhyreMapExportLab\PhyreMapExportLab.csproj`: 0 errors / 0 warnings.
- `dotnet build RuntimeTools\Ps3MapViewerLab\Ps3MapViewerLab.csproj`: 0 errors / 0 warnings.
- `RuntimeTools\FFXMapViewerWeb\public\maps\catalog.json` has 299 entries.
- All 298 catalog entries with `status=success` point to existing glTF files.
- Edge headless screenshots were nonblank for:
  - `map/azit/azit00` textured pilot.
  - `map/azit/azit05` 2D prerendered candidate.
  - `map/dream/fp` blocked state with the missing-manifest reason.
- QA screenshots:
  - `work\mapviewer_web_qa\azit00_root3d.png`
  - `work\mapviewer_web_qa\azit05_2d.png`
  - `work\mapviewer_web_qa\dream_fp_blocked.png`

## Limits

This is a lab viewer, not a real in-game map runtime yet.

- Texture binding is a structural candidate, not engine-exact shader proof.
- `map/azit/azit00` binds decoded root 3D PNGs through the current
  `PMesh -> PMaterial -> PParameterBuffer field172` route.
- `decoded_png_bound_by_submesh_candidate` does not automatically mean a
  fully-covered map. Some entries still have only partial material-slot
  coverage; `map/bsvr/bsvr00` is a concrete example.
- `2d_prerendered` entries are still lab candidates. Their current texture route
  is either structural import-link binding or conservative submesh-order
  assignment when the decoded 2D texture count exactly matches the descriptor
  submesh count.
- Blocked maps remain listed with a reason rather than hidden.
- No AGS texture animation.
- No collision, walkmesh, triggers, doors, actors, or spawn points.
- No engine-exact camera/FOV/state from `FFX.exe` yet.

Do not call this an in-game-style or engine-exact viewer until the runtime and
shader/material path is confirmed beyond the current structural evidence.
