# Linux Release Tool (scripts/release/linux_release.py)

Standard-library Python CLI that owns the linux-x64 release pipeline:
preflight, build, generate-contract, verify, archive. Modes are Diagnostic
(tolerates optional gaps), Candidate and Release (fail closed on dirty Git,
unreconciled versions, or mandatory issues). Nothing publishes.

## Commands

    python3 scripts/release/linux_release.py preflight --mode Diagnostic --source . --output <external>
    python3 scripts/release/linux_release.py build --mode Diagnostic --source . --output <external>
    python3 scripts/release/linux_release.py verify --package <external>/package [--policy release/package-allowlist.json]
    python3 scripts/release/linux_release.py archive --package <external>/package --output <external>
    python3 scripts/release/linux_release.py generate-contract --package <external>/package --output <external> --sidecar <external>/release-manifest.json.sha256

## Invariants

- Outputs must be absolute, external to every Git worktree, and newly created
  or empty. Archive/contract never overwrite.
- Every file is hashed with inode/dev/size/mtime re-stat before and after;
  mutation during hashing is fatal.
- verify walks with no-follow semantics: symlinks, special files and
  multiply-linked files are rejected; paths must be normalized POSIX with no
  traversal, no backslashes, and no case-fold collisions. Every file except
  release-manifest.json must match manifest size/SHA-256/mode exactly.
- The app host must be ELF64 x86-64 and executable; the .deps.json must contain
  the linux-x64 target. Policy components must be reconciled (a missing
  studio-core-linux-x64 is an error, not a bypass).
- verify authenticates the manifest IDENTITY (target/profile/component/version/
  source commit/timestamp) in addition to file bytes: a foreign manifest (e.g.
  win-x64) with a valid sidecar and perfectly matching hashes is rejected.
  --version/--source-commit pin the expected identity; malformed manifests
  yield a stable INVALID_FORMAT result with no traceback/path leak; stdout
  carries basenames only (absolute paths stay in owner-only artifacts).
- archive is deterministic: sorted entries, uid/gid 0, empty owner names,
  commit-pinned mtime and gzip mtime, no original filename; A/B runs must be
  byte-identical. Outputs are created exclusively (O_EXCL|O_NOFOLLOW, never
  truncated), sources are streamed through no-follow descriptors with
  post-stream fstat re-checks, a package that would not verify against its own
  manifest is refused up front, and the archive is re-extracted and re-verified
  before success.
- generate-contract writes the proposed component only to an external
  directory; it never touches release/package-allowlist.json.

## Smoke drivers (GF-06/GF-10 hardening)

smoke_linux_distribution.py fails when any xdotool step errors, when the window
match is ambiguous (exactly one visible match bound to the single launched
PID), when a screenshot is missing/not-PNG/uniform-black (PIL), or when ANY
mutation direction (changed/removed/ADDED) is non-empty for package or project;
--require-distinct additionally requires one distinct screenshot per module for
real-data runs. smoke_languages.py gates on distinct shells across the nine
languages by default (--allow-duplicate-shells records the limitation without
failing). Evidence trees are locked owner-only (0700/0600).

Exit codes: 0 clean, 2 deterministic failure result. Full JSON verdicts go to
stdout; detailed artifacts (preflight.json, publish.log, verify reports) stay
in the external output.
