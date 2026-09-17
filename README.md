<div align="center">

<img src="assets/spira-reforge-studio.png" width="160" alt="Spira Reforge Studio logo"/>

# Spira Reforge Studio

**Official public downloads — desktop modding studio for FINAL FANTASY X / X-2 HD Remaster (Steam, PC)**

[![Version](https://img.shields.io/badge/version-2.244.2.2-informational)](https://github.com/WanxTitanx/ffx-editor-releases/releases/latest)
[![Status](https://img.shields.io/badge/status-BETA-red)](#beta-status)
[![License](https://img.shields.io/badge/license-proprietary-lightgrey)]()
[![Platform](https://img.shields.io/badge/platform-Windows%20x64%20%C2%B7%20Linux%20x64-blue)]()
[![Game](https://img.shields.io/badge/game-FFX%20HD%20Remaster%20(Steam)-green)]()
[![.NET](https://img.shields.io/badge/.NET-8.0%20self--contained-purple)]()
[![UI](https://img.shields.io/badge/UI-Avalonia%2011.2.3-blueviolet)]()
[![i18n](https://img.shields.io/badge/i18n-9%20languages-orange)]()

</div>

---

## Beta status

> **Young software under active development.** `Writable` modules ship byte-safe proven writers; `Read-Only` surfaces are explorers/atlases with no writes; runtime labs stay **OFF by default** behind explicit gates. Use a disposable save — never mod over your main playthrough.

This repository is the official public download channel for **Spira Reforge Studio**, formerly known as FFX Project Editor. The application source remains in its private development repository. Public releases here contain the portable application, a SHA-256 checksum, a machine-readable release manifest, installation notes, and preserved third-party notices.

## Latest release

Download the latest build from [Releases](https://github.com/WanxTitanx/ffx-editor-releases/releases/latest):

| Artifact | Platform | Notes |
|---|---|---|
| `Spira-Reforge-Studio-v*-win-x64.zip` | Windows 10/11 x64 | Extract the whole ZIP, run `FFXProjectEditor.exe`. Self-contained — no .NET install needed. |
| `studio-core-linux-x64-*.tar.gz` | Linux x64 | Studio core build; embedded viewers fall back to the system browser. |

The current free release is not Authenticode-signed, so Windows SmartScreen may show the normal warning for a new unsigned application. **Verify the ZIP against the accompanying `.sha256` file before running it.**

```powershell
Get-FileHash .\Spira-Reforge-Studio-v2.244.2.2-win-x64.zip -Algorithm SHA256
# compare with the contents of the .sha256 asset
```

## What it does

- **Byte-safe file authoring** — Monster Editor, Battle Commands, Items/Key Items/Treasures/Shop/Mix Table, Customizations/Aeons, PC Stats, CTB Base, Formation (8-slot `btl_*`), Al Bhed dictionary, Sphere Grid panel, Thunder Plains lightning dodge, weapon names, battle text, and a native FFXED Save Editor port.
- **Maps & scenes** — Encounters & Formation hub, Battle Explorer, Aurora Field Explorer (~299 HD overworld fields), and **Aurora Chamber**: real rendered battle stages via the bundled noclip runtime, with an **EditViewer gizmo** (drag monsters, `S` saves, `R` resets) and sidecar→bin save merge.
- **Embedded 3D viewers (noclip)** — Monster Studio (PS2 skeletons), Magic Studio (all 372 spells), real battle stages — self-sufficient on a clean machine via official-CDN data bootstrap + lazy fetch-through.
- **Live runtime tooling** — Injected-DLL switchboard, Inventory/Arena trackers (with `ffx.exe` running).
- **Extras** — Music auto-import (10 official tracks via bundled vgmstream), PS2 audio `.wd` browser, VBF extraction, texture/container atlases with honest provenance.
- **AI Assistant (opt-in)** — bring-your-own-key chat/proposal/**agent** modes with sandboxed read-only tools and human-gated executable patches. Off by default; nothing writes without your approval.
- **9-language UI** — PT / EN / ES / FR / DE / IT / JA / KO / ZH, command palette (`Ctrl+K`), workspace dashboard.

## Requirements

- Windows 10/11 x64 (or Linux x64 for studio-core).
- An extracted `ffx_ps2/ffx/master` folder containing `new_uspc` **and** `jppc` for the file surfaces.
- `FFX.exe` running for the live runtime surfaces.
- WebView2 on Windows for embedded viewers (bundled with Windows 11).

## Sister projects

- Runtime layer (engine hooks): [ffx-mod-hooks](https://github.com/WanxTitanx/ffx-mod-hooks)
- Launcher binaries: [ffx-mod-launcher-releases](https://github.com/WanxTitanx/ffx-mod-launcher-releases)
