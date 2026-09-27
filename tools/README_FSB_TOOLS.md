# Battle SFX audio tools (Tier 2 custom WAV)

Lane: **Jarvis-MAGIC**

## Bundled by default

`vgmstream` + `fsbext` are **versioned in git** under `tools/` and **copied next to `FFXProjectEditor.exe`** on every build. Fresh clone = ready; no download step required.

Attribution: `tools/AUDIO_TOOLS_LICENSES.md`

## Required tools (shipped)

| Tool | Role | License |
| --- | --- | --- |
| [vgmstream](https://github.com/vgmstream/vgmstream) `vgmstream-cli.exe` | Decode FSB subsongs to WAV; metadata | Open source (`COPYING`) |
| [fsbext](https://aluigi.altervista.org/papers/fsbext.zip) `fsbext.exe` | Extract/rebuild FSB4 banks | aluigi freeware |

## Optional fallback (bundled when present)

| Tool | Role | License |
| --- | --- | --- |
| FMOD `fsbankcl.exe` | FSB rebuild fallback | FMOD SDK — copy via editor **Import fsbankcl** or `-InstallFsbankCl` bootstrap |

## Repair bootstrap (fallback only)

```powershell
.\scripts\bootstrap_fsb_audio_tools.ps1
```

Use when `tools/` was deleted or you need a newer vgmstream build.

Stray `0.wav`…`121.wav` at the **repo root** = old fsbext runs without `-d wav`. Cleanup:

```powershell
.\scripts\cleanup_fsb_audio_scratch.ps1 -IncludeWorkProbeDirs
```

## Editor integration

Work dirs use a fixed layout:

- `wav/` — subsongs extracted by fsbext (`-d wav`)
- `scratch/` — vgmstream decode probes (`identity.wav`, `format_ref.wav`, …)
- `dump.dat` + rebuilt `.fsb` at the work root

- **Phase 1:** `Fsb9999SampleReplaceWriter` — replace sample at index N
- **Phase 2:** `Fsb9999SampleAppendWriter` + `FevLegacySequenceWriter` — new `seId` slot (no overwrite)
- Gates: `--fsb9999-lab`, `--fsb9999-append-lab`, `--audio-tools-health`, `--command-sound-custom-pack`, `--command-sound-new-seid-pack`
- UI: Kernel Commands → Battle SFX → **Verify audio tools** (health probe vgmstream+fsbext+fsbankcl)

## Constraints (fsbext replace)

Replacement WAV must match original **channels** and **sample rate**; duration must be **≤ original** (replace mode only).
