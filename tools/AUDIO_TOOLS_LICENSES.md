# Bundled Battle SFX audio tools

These binaries ship with **FFX Project Editor** under `tools/` and are copied beside `FFXProjectEditor.exe` on build.

| Tool | Path | License / source |
| --- | --- | --- |
| vgmstream | `tools/vgmstream/` | See `tools/vgmstream/COPYING` |
| fsbext | `tools/fsbext/fsbext.exe` | aluigi freeware |
| fsbankcl | `tools/fsbankcl/` | FMOD FSBankEx 4.44.64 (`fsbankexcl`); bundled for FSB4 fallback |

## fsbankcl

Not auto-downloaded (no public CDN). One-time:

1. Install [FMOD Programmer API](https://www.fmod.com/download) (login), or
2. Editor → Battle SFX → **Import fsbankcl…**

Then `git add tools/fsbankcl` — ships with the repo like vgmstream/fsbext.

Repair: `scripts/bootstrap_fsb_audio_tools.ps1 -InstallFsbankCl`
