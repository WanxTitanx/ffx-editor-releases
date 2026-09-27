# Updating this public repository

This repository is a reviewed source export and binary download channel. The
private development repository has a different history that contains internal
material. Do not mirror it, merge its history into this repository or change the
visibility of the private archive to publish an update.

1. Start from this public repository and select a committed development snapshot.
   Export only editor source, required viewer/helper source, public metadata,
   reviewed runtime dependencies, tests without game/personal fixtures, and build
   tooling. Preserve third-party notices. Review newly added inputs explicitly.
2. Run `gitleaks git . --config .gitleaks.toml --redact`. Review the actual staged
   diff and new binary inputs as well. Do not copy transcripts, environment files,
   credential stores, dumps, game extractions, personal saves or agent state.
3. Build from a fresh clone and run the source-only tests in `BUILDING.md`. Compile
   both development and product profiles so `FFX_INCLUDE_DEVTOOLS` boundaries are
   checked. The manual `Public source validation` workflow covers Windows/Linux.
4. A new binary release still needs the project's release checks and authorization.
   Preserve existing tags/assets. Verify each artifact's size and SHA-256 through
   anonymous download before making it the stable release.
5. Stable tags must be version tags such as `v2.245.1.0` and contain the matching
   `Spira-Reforge-Studio-v2.245.1.0-release.json` manifest. The installed launcher
   resolves `/releases/latest`, validates that tag and reads that manifest.
   Dependency bundles such as `prerequisites` must be prereleases and must never
   become the latest stable release. Keep the canonical repository name and asset
   filenames unchanged unless an explicitly coordinated launcher migration exists.

Historical tags preserved during recovery contain release records, as the old
download repository did; they are not source snapshots for those binary builds.
Their manifests retain the original source commit metadata and artifact hashes.

The recovery source snapshot comes from development commit
`399164236638bd34d44c4833b02ea3b15a49d771` plus the narrow product-compilation fix
that keeps legacy recovery, add-field and simulation commands behind the existing
development-only gate. Uncommitted development work was not published.
