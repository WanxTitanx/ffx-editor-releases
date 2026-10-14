# PhyreMapExportLab

Read-only exporter for FFX HD `ps3data` map assets.

The original pilot target was `map/azit/azit00`. The tool now also supports a
batch command for `RuntimeTools\FFXMapViewerWeb`.

This lab is intentionally separate from `PhyreModelExportLab`. It reuses public
descriptor parser/writer code through a project reference, but does not change
the monster exporter path.

## Commands

```powershell
dotnet build RuntimeTools\PhyreMapExportLab\PhyreMapExportLab.csproj
dotnet run --project RuntimeTools\PhyreMapExportLab\PhyreMapExportLab.csproj -- manifest --portable
dotnet run --project RuntimeTools\PhyreMapExportLab\PhyreMapExportLab.csproj -- export --portable
dotnet run --project RuntimeTools\PhyreMapExportLab\PhyreMapExportLab.csproj -- batch --portable --skip-failed --list RuntimeTools\FFXMapViewerWeb\prompts\2026-06-03-map-batch-web-ready\map-entities.csv --output-root RuntimeTools\FFXMapViewerWeb\public\maps
```

Validation commands used for the `azit00` static/debug/textured candidates:

```powershell
$nodeRoot = "$env:LOCALAPPDATA\Programs\nodejs-portable\node-v24.15.0-win-x64"
$env:Path = "$nodeRoot;$env:Path"
$env:NODE_PATH = (Resolve-Path work\node_tooling\node_modules).Path

node -e "const fs=require('fs'),path=require('path'),v=require('gltf-validator'); const f=process.argv[1]; v.validateBytes(new Uint8Array(fs.readFileSync(f)),{uri:path.basename(f),externalResourceFunction:u=>Promise.resolve(fs.readFileSync(path.resolve(path.dirname(f),decodeURIComponent(u))))}).then(r=>{console.log(JSON.stringify({errors:r.issues.numErrors,warnings:r.issues.numWarnings,infos:r.issues.numInfos,hints:r.issues.numHints},null,2)); process.exit(r.issues.numErrors>0?1:0);}).catch(e=>{console.error(e);process.exit(2);});" work\phyre_map_export_lab\azit00\map_azit_azit00.static-debug.gltf

& "C:\Program Files\F3D\bin\f3d.exe" work\phyre_map_export_lab\azit00\map_azit_azit00.static-debug.gltf --output work\phyre_map_export_lab\azit00\map_azit_azit00.f3d.png --resolution 1280,720 --camera-orthographic
```

Default output:

```text
work\phyre_map_export_lab\azit00\
```

Default batch output:

```text
RuntimeTools\FFXMapViewerWeb\public\maps\
```

`public\maps\` is generated local output and is gitignored.

Generated files:

- `map_azit_azit00.map-export-manifest.json`
- `map_azit_azit00.phyre-descriptor-report.json`
- `map_azit_azit00.phyre-link-dump.json`
- `map_azit_azit00.phyre-string-index.json`
- `map_azit_azit00.static-debug.gltf` when descriptor gates pass
- `map_azit_azit00.static-textured.gltf`
- `map_azit_azit00.static-textured-vertex-color.gltf`
- `map_azit_azit00.material-slot-analysis.json`
- `map_azit_azit00.static-textured-phyre-slots.gltf`
- `map_azit_azit00.static-textured-phyre-slots-vertex-color.gltf`
- `map_azit_azit00.static-export-report.json`

## Honest Scope

The first pilot export is a static debug glTF candidate. The current best pilot visual
candidate is `static-textured-phyre-slots.gltf`, which binds decoded root 3D
PNGs by the structural route `PMesh -> PMaterial -> PParameterBuffer field172`.
This is still not an engine-exact material proof.

The generalized material-slot route now also reads `PAssetReferenceImport`
strings from each `.dae.phyre` and resolves import names such as
`PS3Data/map/kino/kino07/tex/01024_44_000_000_4_256_256.dds` to the decoded
`tex/d3d11/*.dds.phyre` PNG. This import-path mapping is preferred over numeric
slot matching, with the older numeric bridge kept only as a fallback. A
secondary route may bind a `PParameterBuffer` only when exactly one object link
resolves through `PAssetReferenceImport` to a DDS texture; multiple texture links
remain blocked instead of guessed. Details:
`docs/history/FFX_MAPVIEWER_TEXTURE_IMPORT_BINDING_2026-06-03.md`.

Batch status from 2026-06-03 after the 2D prerendered recovery pass:

- 299 `map/<area>/<field_token>` entries processed from the MapViewerWeb prompt list.
- 298 generated glTF outputs.
- 1 remained cataloged as blocked: `map/dream/fp`, which has no root or `2d`
  `.ahwin32` manifest and behaves as a texture/support bucket rather than a
  standalone model export.
- 240 outputs use root 3D manifests and 58 outputs use the `2d_prerendered`
  manifest fallback.
- 298 generated glTFs were reference-checked with 0 missing glTF/image/buffer
  files.
- Texture status: 289 `decoded_png_bound_by_submesh_candidate`, 1
  `decoded_png_bound_by_file_material_id_candidate`, 8 `texture_not_requested`,
  and 1 `texture_not_attempted`.
- Material slot reports: 298 reports; 40 fully bound, 228 partially bound, 30
  zero-bound; 20,987 / 29,525 material rows bound to decoded PNGs.
- The batch writes `catalog.json`, `status.json`, and `status.csv` for the web viewer.

`azit00` current result:

- descriptor decision: `mesh_descriptor_decode_candidate`
- export decision: `mesh_descriptor_static_gltf_candidate`
- primitives: 14
- vertices: 4,896
- triangles: 1,632
- texture status: `decoded_png_bound_by_submesh_candidate`
- material slot analysis: 14/14 submeshes bound by link-table candidate
- shared-data bridge: `10->0, 11->1, 12->2, 13->3, 14->4, 15->5, 16->6`
- glTF-Validator: 0 errors / 0 warnings
- F3D: nonblank 1280x720 screenshot generated
- Three.js GLTFLoader: 14 meshes / 14 primitives / 14 textured materials / 4,896 vertices

`kino07` generalized import-path result:

- descriptor decision: `mesh_descriptor_decode_candidate`
- export decision: `mesh_descriptor_static_gltf_candidate`
- primitives: 85
- vertices: 95,071
- triangles: 33,372
- texture status: `decoded_png_bound_by_submesh_candidate`
- material slot analysis: 72/85 submeshes bound by import-path candidate
- status split: 36 primary `field172` bindings, 36 secondary single-import-link
  bindings, 8 missing texture links, 3 multiple texture links, 2 shader/non-DDS
  imports
- glTF-Validator: 0 errors / 0 warnings / 49 infos / 0 hints
- F3D: nonblank 1280x720 screenshot generated

2D prerendered scope:

- If a map slice has no root manifest but has `2d/<field>.ahwin32`, the exporter
  now uses `2d/mdl/d3d11/<field>.dae.phyre` as the primary descriptor and
  decodes `2d/tex/d3d11/*.dds.phyre` as the primary texture bucket.
- When the 2D texture count exactly matches the descriptor submesh count, the
  exporter emits a conservative `2d_prerendered_submesh_order_texture_candidate`.
- Some 2D maps also expose partial texture links through the same structural
  Phyre import path machinery; these remain candidates, not engine-exact shader
  proof.

Unresolved by design:

- `textureanimation.ags.phyre`
- engine-exact Phyre material/shader semantics
- shader state
- collision/walkmesh/triggers
- engine-exact runtime behavior
