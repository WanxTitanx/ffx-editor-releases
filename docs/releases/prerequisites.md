# Build prerequisites

Original publication: 2026-09-24T21:54:22Z

Offline build/packaging prerequisites for Spira Reforge Studio — not versioned with the app (evergreen payloads).

- `MicrosoftEdgeWebView2RuntimeInstallerX64.exe` — Microsoft Edge WebView2 Runtime evergreen offline installer (x64). Bundled into the Windows self-contained package when `FFXShipWindowsPrerequisites=true` (see `ExternalLibs/WindowsPrerequisites/WebView2/`). Verify against the `.sha256` asset.

## Preserved artifact hashes

- `MicrosoftEdgeWebView2RuntimeInstallerX64.exe` — 212949712 bytes — `sha256:82b2d8a7013e0c0ea15d48ff4742ee3778ba16bd8b7b4a47876645b3e48d4016`
- `webview2.sha256` — 111 bytes — `sha256:e5d625e70dbab32584b76d53c1c0e488e66fab9c86de253c549e269ef29b60cf`
