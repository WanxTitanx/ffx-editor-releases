# Building from public source

## Core editor

Install the .NET 8 SDK and Git. Clone this repository normally; no private
repositories, submodules, Git LFS or maintainer credentials are required.

```sh
dotnet restore FFXProjectEditor/FFXProjectEditor.csproj
dotnet run --project FFXProjectEditor/FFXProjectEditor.csproj
```

The checkout includes the memory assembly, x64 Keystone library, product viewer
code, public metadata catalogs, vgmstream binaries and notices, and legacy FFXED
runtime dependencies. NuGet restores the managed packages declared in the project.

On Linux, a graphical X11/XWayland session and native font/graphics libraries are
needed. On Debian/Ubuntu, the usual packages are `libx11-6`, `libice6`, `libsm6`,
`libfontconfig1`, `libfreetype6` and `libglib2.0-0` (package suffixes can vary by
distribution). The product uses an external browser for its web viewers on Linux.
On Windows, embedded viewers use Microsoft WebView2.

## Complete Windows package dependencies

Python 3.11+ and PowerShell 7 are used by the packaging tools. The following
command downloads only previously released, SHA-256-pinned dependencies. It
does not install software, request credentials or run the downloaded binaries.

```sh
python scripts/bootstrap_public.py --windows
```

This prepares the offline WebView2 installer and the legacy FSBank audio tool.
These optional binary inputs are ignored by Git. They keep their original
licensing/provenance status from `release/tool-dependencies.json`; publication
of this project does not relicense third-party programs. Existing editor UI also
supports importing the audio tool from the user's own local installation.

Build the project's Phyre helper before creating a Windows package:

```sh
dotnet publish RuntimeTools/PhyreMapExportLab/PhyreMapExportLab.csproj -c Release -r win-x64 --self-contained true -p:DebugSymbols=false -p:DebugType=None -o ExternalLibs/Tools/PhyreMapExportLab/win-x64
dotnet publish FFXProjectEditor/FFXProjectEditor.csproj -p:PublishProfile=win-x64-portable -o artifacts/win-x64
```

The helper source and its PhyreModelExportLab dependency are both included.
The Windows profile invokes PowerShell and belongs on a Windows build host.
Use `artifacts/win-x64/FFXProjectEditor.exe` to launch the resulting package.
Rebuilding a dependency changes its hash; official release contracts must then
be reviewed and regenerated. Do not treat Diagnostic packaging as release approval.

## Linux package

```sh
dotnet publish FFXProjectEditor/FFXProjectEditor.csproj -p:PublishProfile=linux-x64-portable -o artifacts/linux-x64
./artifacts/linux-x64/FFXProjectEditor
```

The self-contained package includes the .NET runtime and Linux vgmstream. It does
not need the Windows offline prerequisites, Java helper or legacy FSBank binaries.

## Tests and game data

The source includes the test suites. Tests that consume game files need the
corresponding inputs from your own installation. No user saves or extracted
game fixtures are distributed. Consult each fixture-dependent test's expected
path and configure `FFX_TEST_MASTER_ROOT` and `FFX_TEST_MAGIC_CORPUS` when needed.
Missing game corpus is not evidence that a format or game feature passed validation.

A focused source-only check is:

```sh
dotnet test FFXProjectEditor.Tests/FFXProjectEditor.Tests.csproj -c Release --filter "FullyQualifiedName~StringsIntegrityTests|FullyQualifiedName~WindowsDesktopDependencyContractTests|FullyQualifiedName~LlmRedactorTests|FullyQualifiedName~LlmHttpAdapterTests"
node --test RuntimeTools/FFXNoclipStudio/ffx-studio.test.mjs RuntimeTools/FFXNoclipStudio/magic-preview.test.mjs
```

All application source is included, including development-only commands guarded
by `FFX_INCLUDE_DEVTOOLS`. Distribution profiles keep those commands excluded.
SPIRA FORGE remains paused; Monster AI Editor 2 remains legacy. Compilation and
offline tests do not enable or prove live-game operations.
