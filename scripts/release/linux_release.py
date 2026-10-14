#!/usr/bin/env python3
"""Spira Reforge Studio linux-x64 release CLI (preflight/build/generate-contract/verify/archive).

Standard library only. All outputs are absolute, external to every Git worktree,
and race-checked while hashing. Modes: Diagnostic (may report optional gaps),
Candidate and Release (fail closed on dirty Git, unreconciled versions, or any
mandatory issue). Nothing here publishes; publication is a separate audited step.

Plan: docs/superpowers/plans/2026-09-13-linux-d-distribution-and-module-proof.md Task 3.
"""

from __future__ import annotations

import argparse
import errno
import gzip
import hashlib
import io
import json
import os
import re
import shutil
import stat
import subprocess
import sys
import tarfile
import tempfile
from datetime import datetime, timezone
from pathlib import Path, PurePosixPath

SCHEMA_VERSION = 2
MODES = ("Diagnostic", "Candidate", "Release")
STRICT_MODES = ("Candidate", "Release")
PROFILE = "linux-x64-portable"
COMPONENT = "studio-core-linux-x64"
APP_HOST = "FFXProjectEditor"
DEPS_NAME = "FFXProjectEditor.deps.json"
MANIFEST_NAME = "release-manifest.json"
EXPECTED_DEPS_TARGET = ".NETCoreApp,Version=v8.0/linux-x64"

ELF_MAGIC = b"\x7fELF"
ELFCLASS64 = 2
EM_X86_64 = 62


class ReleaseError(Exception):
    """Fatal CLI error with a stable code."""


def result_entry(severity: str, code: str, subject: str, remediation: str) -> dict:
    return {"severity": severity, "code": code, "subject": subject, "remediation": remediation}


def write_json_atomic(path: Path, payload: dict) -> None:
    """Atomically write owner-only JSON evidence (GF-11: 0600, exclusive create)."""
    text = json.dumps(payload, indent=2, sort_keys=True) + "\n"
    tmp = path.with_name(path.name + ".tmp")
    fd = os.open(tmp, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    with os.fdopen(fd, "w", encoding="utf-8") as handle:
        handle.write(text)
        handle.flush()
        os.fsync(handle.fileno())
    os.replace(tmp, path)


def sha256_stat_checked(path: Path) -> tuple[str, dict]:
    """Hash while re-statting inode/dev/size/mtime before and after the read."""
    before = path.stat()
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    after = path.stat()
    for key in ("st_ino", "st_dev", "st_size", "st_mtime_ns"):
        if getattr(before, key) != getattr(after, key):
            raise ReleaseError(
                f"source file mutated during hashing: {path} ({key} changed)")
    return digest.hexdigest(), {
        "inode": before.st_ino,
        "device": before.st_dev,
        "size": before.st_size,
        "mtime_ns": before.st_mtime_ns,
    }


def sha256_fd_checked(path: Path) -> tuple[str, dict]:
    """Hash through an O_NOFOLLOW descriptor and re-stat the SAME fd (GF-04).

    WHY: a path-based stat/open pair can be swapped to a symlink (or another
    regular file) between the two calls; hashing the opened descriptor with
    fstat before/after binds the hashed bytes to the identity we validated.
    ELOOP on symlink targets surfaces as ReleaseError(INVALID_FORMAT).
    """
    try:
        fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW)
    except OSError as error:
        if error.errno in (errno.ELOOP, errno.EMLINK):
            raise ReleaseError(f"symlink refused during no-follow hashing: {path}") from error
        raise
    try:
        before = os.fstat(fd)
        digest = hashlib.sha256()
        while True:
            chunk = os.read(fd, 1024 * 1024)
            if not chunk:
                break
            digest.update(chunk)
        after = os.fstat(fd)
        for key in ("st_ino", "st_dev", "st_size", "st_mtime_ns"):
            if getattr(before, key) != getattr(after, key):
                raise ReleaseError(
                    f"source file mutated during hashing: {path} ({key} changed)")
        return digest.hexdigest(), {
            "inode": before.st_ino,
            "device": before.st_dev,
            "size": before.st_size,
            "mtime_ns": before.st_mtime_ns,
        }
    finally:
        os.close(fd)


def open_stream_checked(path: Path):
    """Open a regular file O_NOFOLLOW for streaming and return (fd, before_stat).

    GF-05: archive streams must re-stat the SAME descriptor after the stream so
    a mid-read swap cannot feed the tar bytes that were never hashed.
    """
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW)
    return fd, os.fstat(fd)


def _assert_same_stat(before: os.stat_result, after: os.stat_result, path: Path) -> None:
    """GF-05: bind streamed tar bytes to the descriptor identity we validated."""
    for key in ("st_ino", "st_dev", "st_size", "st_mtime_ns"):
        if getattr(before, key) != getattr(after, key):
            raise ReleaseError(f"source mutated while streaming: {path} ({key} changed)")


def git_state(source: Path) -> dict:
    def run(*args: str) -> str:
        proc = subprocess.run(
            ["git", "-C", str(source), *args],
            capture_output=True, text=True, check=False)
        if proc.returncode != 0:
            raise ReleaseError(f"git {' '.join(args)} failed: {proc.stderr.strip()}")
        return proc.stdout.strip()

    commit = run("rev-parse", "HEAD")
    timestamp = run("log", "-1", "--format=%cI")
    porcelain = run("status", "--porcelain")
    top = run("rev-parse", "--show-toplevel")
    return {
        "commit": commit,
        "timestamp": timestamp,
        "dirty": bool(porcelain),
        "porcelain": porcelain.splitlines(),
        "top": Path(top).resolve(),
    }


def read_project_version(source: Path) -> str | None:
    csproj = source / "FFXProjectEditor" / "FFXProjectEditor.csproj"
    if not csproj.is_file():
        return None
    match = re.search(r"<Version>([^<]+)</Version>", csproj.read_text(encoding="utf-8"))
    return match.group(1).strip() if match else None


def version_reconciled(source: Path, version: str | None) -> list[dict]:
    entries: list[dict] = []
    live = read_project_version(source)
    expected = version or live
    if live is None:
        entries.append(result_entry("error", "VERSION_UNRECONCILED",
                                    "FFXProjectEditor/FFXProjectEditor.csproj",
                                    "project must declare <Version>"))
        return entries
    if version is not None and live != version:
        entries.append(result_entry("error", "VERSION_UNRECONCILED", live,
                                    f"csproj version {live} != requested {version}"))
    for changelog in ("CHANGELOG.md", "changelogUS.md"):
        path = source / changelog
        if not path.is_file() or expected not in path.read_text(encoding="utf-8"):
            entries.append(result_entry("error", "VERSION_UNRECONCILED", changelog,
                                        f"changelog must mention {expected}"))
    return entries


def is_external(path: Path, source: Path) -> bool:
    """True when path is not inside the source tree nor any enclosing git worktree."""
    resolved = path.resolve()
    try:
        top = subprocess.run(
            ["git", "-C", str(source), "rev-parse", "--show-toplevel"],
            capture_output=True, text=True, check=False)
        if top.returncode == 0:
            worktree = Path(top.stdout.strip()).resolve()
            if resolved == worktree or worktree in resolved.parents:
                return False
    except OSError:
        pass
    source_resolved = source.resolve()
    return resolved != source_resolved and source_resolved not in resolved.parents


def validate_output(output: Path, source: Path, create: bool = False) -> list[dict]:
    entries: list[dict] = []
    if not output.is_absolute():
        entries.append(result_entry("error", "OUTPUT_NOT_EXTERNAL", str(output),
                                    "output must be an absolute path"))
        return entries
    if not is_external(output, source):
        entries.append(result_entry("error", "OUTPUT_NOT_EXTERNAL", str(output),
                                    "output must live outside every git worktree and the source tree"))
        return entries
    if output.exists() and any(output.iterdir()):
        entries.append(result_entry("error", "OUTPUT_NOT_EMPTY", str(output),
                                    "output directory must be newly created or empty"))
        return entries
    if create and not output.exists():
        output.mkdir(parents=True, exist_ok=False)
    return entries


def elf_info(path: Path) -> dict:
    with path.open("rb") as handle:
        header = handle.read(20)
    if len(header) < 20 or header[:4] != ELF_MAGIC:
        raise ReleaseError(f"not an ELF file: {path}")
    elf_class = header[4]
    machine = int.from_bytes(header[18:20], "little")
    return {"class": elf_class, "machine": machine}


# ── preflight ────────────────────────────────────────────────────────────────

def cmd_preflight(args: argparse.Namespace) -> int:
    source = Path(args.source).resolve()
    output = Path(args.output)
    entries: list[dict] = []

    try:
        git = git_state(source)
    except ReleaseError as error:
        print(json.dumps({"summary": {"ok": False}, "results": [
            result_entry("error", "MISSING_DEPENDENCY", "git", str(error))]}, indent=2))
        return 2

    if git["dirty"] and args.mode in STRICT_MODES:
        entries.append(result_entry("error", "GIT_DIRTY",
                                    f"{len(git['porcelain'])} path(s)",
                                    "commit or stash every change before Candidate/Release"))
    elif git["dirty"]:
        entries.append(result_entry("warning", "GIT_DIRTY",
                                    f"{len(git['porcelain'])} path(s)",
                                    "Diagnostic tolerates a dirty tree; Candidate/Release will not"))

    entries.extend(version_reconciled(source, args.version))

    strict_version_errors = [e for e in entries
                             if e["code"] == "VERSION_UNRECONCILED"]
    if args.mode in STRICT_MODES and strict_version_errors:
        for entry in strict_version_errors:
            entry["severity"] = "error"
    elif args.mode == "Diagnostic" and strict_version_errors:
        for entry in strict_version_errors:
            entry["severity"] = "warning"

    output_entries = validate_output(output, source, create=not args.no_mkdir)
    entries.extend(output_entries)
    # GF-03: a rejected output path must never be created or written to; report
    # on stdout only and fail closed before any filesystem mutation.
    output_rejected = any(e["severity"] == "error" for e in output_entries)

    if not os.environ.get("DISPLAY"):
        entries.append(result_entry("info", "OPTIONAL_UNAVAILABLE", "DISPLAY",
                                    "no X11/XWayland display detected; GUI smoke needs one"))

    payload = {
        "mode": args.mode,
        "source": str(source),
        "commit": git["commit"],
        "commitTimestampUtc": git["timestamp"],
        "version": read_project_version(source),
        "results": sorted(entries, key=lambda e: (e["severity"], e["code"], e["subject"])),
    }
    errors = sum(1 for e in entries if e["severity"] == "error")
    warnings = sum(1 for e in entries if e["severity"] == "warning")
    payload["summary"] = {"ok": errors == 0, "errors": errors, "warnings": warnings}

    if output_rejected:
        # GF-11: stdout is the public channel; keep full paths only in the
        # owner-only file evidence, never in the public JSON stream.
        public = dict(payload)
        public["source"] = source.name
        print(json.dumps(public))
        return 2
    output.mkdir(parents=True, exist_ok=True)
    write_json_atomic(output / "preflight.json", payload)
    public = dict(payload)
    public["source"] = source.name
    print(json.dumps(public))
    return 0 if errors == 0 else 2


# ── build ────────────────────────────────────────────────────────────────────

def cmd_build(args: argparse.Namespace) -> int:
    source = Path(args.source).resolve()
    output = Path(args.output)
    entries = validate_output(output, source, create=True)
    if any(e["severity"] == "error" for e in entries):
        for entry in entries:
            print(json.dumps(entry))
        return 2

    git_before = git_state(source)
    if git_before["dirty"] and args.mode in STRICT_MODES:
        print(json.dumps(result_entry("error", "GIT_DIRTY", "source",
                                      "Candidate/Release builds require a clean tree")))
        return 2

    version_errors = version_reconciled(source, args.version)
    if args.mode in STRICT_MODES and version_errors:
        for entry in version_errors:
            print(json.dumps(entry))
        return 2

    package = output / "package"
    packages_cache = output / "nuget-packages"
    artifacts = output / "artifacts"
    package.mkdir()
    packages_cache.mkdir()
    artifacts.mkdir()

    env = dict(os.environ)
    env["NUGET_PACKAGES"] = str(packages_cache)
    env["DOTNET_ARTIFACTS"] = str(artifacts)
    project = source / "FFXProjectEditor" / "FFXProjectEditor.csproj"
    proc = subprocess.run(
        ["dotnet", "publish", str(project), "-c", "Release",
         f"-p:PublishProfile={PROFILE}", "-o", str(package)],
        env=env, capture_output=True, text=True, check=False)
    (output / "publish.log").write_text(proc.stdout + proc.stderr, encoding="utf-8")
    if proc.returncode != 0:
        print(json.dumps(result_entry("error", "MISSING_DEPENDENCY", "dotnet publish",
                                      f"exit {proc.returncode}; see publish.log")))
        return 2

    files = []
    for path in sorted(package.rglob("*")):
        rel = path.relative_to(package).as_posix()
        if path.is_dir() or rel == MANIFEST_NAME:
            continue
        digest, _stats = sha256_stat_checked(path)
        files.append({
            "path": rel,
            "size": path.stat().st_size,
            "sha256": digest,
            "mode": format(stat.S_IMODE(path.stat().st_mode), "04o"),
        })

    git_after = git_state(source)
    if git_after["commit"] != git_before["commit"] or git_after["porcelain"] != git_before["porcelain"]:
        print(json.dumps(result_entry("error", "ARTIFACT_DIVERGENCE", "git identity",
                                      "source changed during build; discard this output")))
        return 2

    manifest = {
        "schemaVersion": SCHEMA_VERSION,
        "target": "linux-x64",
        "profile": PROFILE,
        "version": read_project_version(source),
        "sourceCommit": git_before["commit"],
        "sourceCommitTimestampUtc": git_before["timestamp"],
        "component": COMPONENT,
        "files": files,
    }
    write_json_atomic(package / MANIFEST_NAME, manifest)
    print(json.dumps({"ok": True, "package": str(package), "files": len(files)}))
    return 0


# ── generate-contract ────────────────────────────────────────────────────────

def cmd_generate_contract(args: argparse.Namespace) -> int:
    package = Path(args.package)
    manifest = json.loads((package / MANIFEST_NAME).read_text(encoding="utf-8"))
    sidecar = Path(args.sidecar) if args.sidecar else package.parent / (MANIFEST_NAME + ".sha256")
    if not sidecar.is_file():
        print(json.dumps(result_entry("error", "ARTIFACT_DIVERGENCE", str(sidecar),
                                      "verify the package first; sidecar is required")))
        return 2
    expected = sidecar.read_text(encoding="utf-8").split()[0]
    actual, _ = sha256_stat_checked(package / MANIFEST_NAME)
    if actual != expected:
        print(json.dumps(result_entry("error", "ARTIFACT_DIVERGENCE", MANIFEST_NAME,
                                      "manifest does not match its authenticated sidecar")))
        return 2

    fragment = {
        "$comment": ("PROPOSED " + COMPONENT + " generated from a verified manifest. "
                     "External staging only; reconcile before any selective commit."),
        "target": "linux-x64",
        "selfContained": True,
        "profile": manifest["profile"],
        "version": manifest["version"],
        "sourceCommit": manifest["sourceCommit"],
        "files": manifest["files"],
    }
    output = Path(args.output)
    entries = validate_output(output, package, create=True)
    if any(e["severity"] == "error" for e in entries):
        for entry in entries:
            print(json.dumps(entry))
        return 2
    write_json_atomic(output / f"proposed-{COMPONENT}.json", fragment)
    print(json.dumps({"ok": True, "files": len(fragment["files"])}))
    return 0


# ── verify ───────────────────────────────────────────────────────────────────

def verify_package_tree(package: Path, sidecar: Path | None, *,
                        require_sidecar: bool = False,
                        policy: dict | None = None,
                        component_name: str | None = None,
                        expected_version: str | None = None,
                        expected_commit: str | None = None) -> tuple[list[dict], set[str]]:
    """Verify a package tree; returns (entries, disk_files). Never prints.

    GF-04 hardening: authenticates the release IDENTITY (target/profile/
    component/version/commit/timestamp) instead of file bytes only, hashes
    through O_NOFOLLOW descriptors with fstat re-checks, and reports malformed
    inputs as deterministic INVALID_FORMAT entries instead of tracebacks.
    """
    entries: list[dict] = []
    manifest_path = package / MANIFEST_NAME
    if not manifest_path.is_file():
        return [result_entry("error", "INVALID_FORMAT", MANIFEST_NAME,
                             "manifest missing")], set()

    if sidecar is not None and sidecar.is_file():
        try:
            expected = sidecar.read_text(encoding="utf-8").split()[0]
        except (OSError, IndexError, ValueError):
            expected = None
            entries.append(result_entry("error", "ARTIFACT_DIVERGENCE",
                                        MANIFEST_NAME + ".sha256",
                                        "sidecar is malformed"))
        if expected is not None:
            try:
                actual, _stats = sha256_fd_checked(manifest_path)
            except ReleaseError:
                actual = None
                entries.append(result_entry("error", "INVALID_FORMAT", MANIFEST_NAME,
                                            "manifest is not readable no-follow"))
            if actual is not None and actual != expected:
                entries.append(result_entry("error", "ARTIFACT_DIVERGENCE", MANIFEST_NAME,
                                            "manifest hash does not match its sidecar"))
    elif require_sidecar:
        entries.append(result_entry("error", "ARTIFACT_DIVERGENCE",
                                    MANIFEST_NAME + ".sha256",
                                    "authenticated manifest sidecar is required"))

    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        if not isinstance(manifest, dict) or not isinstance(manifest.get("files"), list):
            raise ValueError("manifest shape")
        manifest_files: dict[str, dict] = {}
        for item in manifest["files"]:
            if (not isinstance(item, dict)
                    or not isinstance(item.get("path"), str)
                    or not isinstance(item.get("sha256"), str)
                    or not isinstance(item.get("size"), int)
                    or not isinstance(item.get("mode"), str)):
                raise ValueError("manifest file entry shape")
            manifest_files[item["path"]] = item
    except (ValueError, TypeError, OSError):
        return entries + [result_entry("error", "INVALID_FORMAT", MANIFEST_NAME,
                                       "manifest is not valid JSON or has the wrong shape")], set()

    if manifest.get("schemaVersion") != SCHEMA_VERSION:
        entries.append(result_entry("error", "INVALID_FORMAT", MANIFEST_NAME,
                                    f"schemaVersion must be {SCHEMA_VERSION}"))
    # GF-04: a fully-hashed manifest with a VALID sidecar but a foreign identity
    # (e.g. a win-x64 tree) must be rejected, not accepted as linux evidence.
    for field, want, code in (("target", "linux-x64", "INVALID_PLATFORM"),
                              ("profile", PROFILE, "INVALID_FORMAT"),
                              ("component", COMPONENT, "INVALID_FORMAT")):
        found = manifest.get(field)
        if found != want:
            entries.append(result_entry("error", code, MANIFEST_NAME,
                                        f"{field} must be {want}, found {found!r}"))
    try:
        datetime.fromisoformat(
            str(manifest.get("sourceCommitTimestampUtc", "")).replace("Z", "+00:00"))
    except ValueError:
        entries.append(result_entry("error", "INVALID_FORMAT", MANIFEST_NAME,
                                    "sourceCommitTimestampUtc must be ISO-8601"))
    if expected_version is not None and manifest.get("version") != expected_version:
        entries.append(result_entry("error", "VERSION_UNRECONCILED", MANIFEST_NAME,
                                    f"manifest version {manifest.get('version')!r} "
                                    f"!= expected {expected_version!r}"))
    if expected_commit is not None and manifest.get("sourceCommit") != expected_commit:
        entries.append(result_entry("error", "ARTIFACT_DIVERGENCE", MANIFEST_NAME,
                                    f"manifest sourceCommit != expected {expected_commit}"))

    seen_exact: set[str] = set()
    seen_folded: dict[str, str] = {}
    disk_files: set[str] = set()

    def walk(directory: Path) -> None:
        with os.scandir(directory) as iterator:
            for entry in iterator:
                path = Path(entry.path)
                rel = path.relative_to(package).as_posix()
                st = entry.stat(follow_symlinks=False)
                if entry.is_symlink():
                    entries.append(result_entry("error", "INVALID_FORMAT", rel,
                                                "symlinks are forbidden in the package"))
                    continue
                if entry.is_dir(follow_symlinks=False):
                    walk(path)
                    continue
                if not stat.S_ISREG(st.st_mode):
                    entries.append(result_entry("error", "INVALID_FORMAT", rel,
                                                "only regular files are allowed"))
                    continue
                if st.st_nlink > 1:
                    entries.append(result_entry("error", "INVALID_FORMAT", rel,
                                                "multiply-linked files are forbidden"))
                    continue
                if "\\" in rel or rel.startswith("/") or ".." in PurePosixPath(rel).parts:
                    entries.append(result_entry("error", "INVALID_PLATFORM", rel,
                                                "path must be normalized POSIX without traversal"))
                    continue
                if rel in seen_exact:
                    entries.append(result_entry("error", "INVALID_FORMAT", rel, "duplicate path"))
                    continue
                folded = rel.casefold()
                if folded in seen_folded:
                    entries.append(result_entry("error", "INVALID_FORMAT", rel,
                                                f"case-fold collision with {seen_folded[folded]}"))
                    continue
                seen_exact.add(rel)
                seen_folded[folded] = rel
                disk_files.add(rel)

    walk(package)

    for rel in sorted(disk_files):
        if rel == MANIFEST_NAME:
            continue
        info = manifest_files.get(rel)
        path = package / rel
        if info is None:
            entries.append(result_entry("error", "ARTIFACT_DIVERGENCE", rel,
                                        "file on disk is not in the manifest"))
            continue
        try:
            digest, stats = sha256_fd_checked(path)
        except ReleaseError:
            entries.append(result_entry("error", "INVALID_FORMAT", rel,
                                        "not readable through a no-follow descriptor"))
            continue
        st = path.stat()
        if (digest != info["sha256"] or stats["size"] != info["size"]
                or st.st_size != info["size"]
                or format(stat.S_IMODE(st.st_mode), "04o") != info["mode"]):
            entries.append(result_entry("error", "ARTIFACT_DIVERGENCE", rel,
                                        "size/hash/mode drift against the manifest"))
    for rel in sorted(set(manifest_files) - disk_files):
        entries.append(result_entry("error", "ARTIFACT_DIVERGENCE", rel,
                                    "manifest entry missing on disk"))

    host = package / APP_HOST
    if not host.is_file():
        entries.append(result_entry("error", "INVALID_PLATFORM", APP_HOST,
                                    "app host executable missing"))
    else:
        try:
            info = elf_info(host)
            if info["class"] != ELFCLASS64 or info["machine"] != EM_X86_64:
                entries.append(result_entry("error", "INVALID_PLATFORM", APP_HOST,
                                            "app host must be ELF64 x86-64"))
            if not os.access(host, os.X_OK):
                entries.append(result_entry("error", "PERMISSION_DENIED", APP_HOST,
                                            "app host must be executable"))
        except ReleaseError as error:
            entries.append(result_entry("error", "INVALID_PLATFORM", APP_HOST,
                                        "app host is not a readable ELF"))

    deps_path = package / DEPS_NAME
    if deps_path.is_file():
        try:
            deps = json.loads(deps_path.read_text(encoding="utf-8"))
            targets = deps.get("targets", {})
        except (ValueError, OSError):
            targets = None
        if not isinstance(targets, dict):
            entries.append(result_entry("error", "INVALID_FORMAT", DEPS_NAME,
                                        "deps file is not valid JSON"))
        elif EXPECTED_DEPS_TARGET not in targets:
            entries.append(result_entry("error", "INVALID_PLATFORM", DEPS_NAME,
                                        f"deps target must include {EXPECTED_DEPS_TARGET}"))
    else:
        entries.append(result_entry("error", "MISSING_DEPENDENCY", DEPS_NAME,
                                    "deps file missing"))

    if policy is not None:
        component = policy.get("components", {}).get(component_name or COMPONENT)
        if component is None:
            entries.append(result_entry("error", "ARTIFACT_DIVERGENCE",
                                        component_name or COMPONENT,
                                        "policy component not reconciled yet"))
        else:
            # GF-04: the policy component must agree with the manifest identity.
            if component.get("target") not in (None, manifest.get("target")):
                entries.append(result_entry("error", "INVALID_PLATFORM",
                                            component_name or COMPONENT,
                                            "policy target does not match manifest target"))
            if component.get("version") not in (None, manifest.get("version")):
                entries.append(result_entry("error", "VERSION_UNRECONCILED",
                                            component_name or COMPONENT,
                                            "policy version does not match manifest version"))
            for required in component.get("requiredFiles", []):
                if required not in disk_files:
                    entries.append(result_entry("error", "MISSING_DEPENDENCY", required,
                                                "required policy file missing"))
            allowed_roots = component.get("allowedRoots", [])
            for rel in disk_files:
                first = rel.split("/", 1)[0]
                inside_root = any(rel.startswith(root) for root in allowed_roots)
                if not inside_root and first not in component.get("allowedTopLevel", []):
                    entries.append(result_entry("error", "INVALID_FORMAT", rel,
                                                "path outside every allowed root"))
            # Deny rules are structural gates too: forbidden identities and
            # extensions must fail even when the manifest itself lists them.
            deny = component.get("deny", {})
            denied_paths = set(deny.get("paths", []))
            denied_exts = tuple(deny.get("extensions", []))
            for rel in disk_files:
                first = rel.split("/", 1)[0]
                if rel in denied_paths or first + "/" in denied_paths:
                    entries.append(result_entry("error", "INVALID_FORMAT", rel,
                                                "denied policy path"))
                elif rel.endswith(denied_exts):
                    entries.append(result_entry("error", "INVALID_FORMAT", rel,
                                                "denied policy extension"))
    return entries, disk_files


def cmd_verify(args: argparse.Namespace) -> int:
    package = Path(args.package).resolve()
    sidecar = Path(args.sidecar) if args.sidecar else package.parent / (MANIFEST_NAME + ".sha256")
    policy = None
    if args.policy:
        try:
            policy = json.loads(Path(args.policy).read_text(encoding="utf-8"))
        except (ValueError, OSError):
            print(json.dumps(result_entry("error", "INVALID_FORMAT",
                                          Path(args.policy).name,
                                          "policy file is not valid JSON")))
            return 2
    entries, disk_files = verify_package_tree(
        package, sidecar, require_sidecar=args.require_sidecar, policy=policy,
        component_name=args.component, expected_version=args.version,
        expected_commit=args.source_commit)
    errors = [e for e in entries if e["severity"] == "error"]
    # GF-11: stdout stays path-free (basename only); absolute paths live in the
    # owner-only report file.
    payload = {"package": package.name, "files": len(disk_files),
               "entries": sorted(entries, key=lambda e: (e["severity"], e["code"], e["subject"])),
               "summary": {"ok": not errors, "errors": len(errors)}}
    print(json.dumps(payload))
    if args.report:
        report = Path(args.report)
        report.parent.mkdir(parents=True, exist_ok=True)
        detailed = dict(payload)
        detailed["package"] = str(package)
        write_json_atomic(report, detailed)
    return 0 if not errors else 2


# ── archive ──────────────────────────────────────────────────────────────────

def _epoch_from_manifest(manifest: dict) -> int:
    text = manifest["sourceCommitTimestampUtc"]
    parsed = datetime.fromisoformat(text.replace("Z", "+00:00"))
    return int(parsed.astimezone(timezone.utc).timestamp())


def _write_exclusive(path: Path, text: str, mode_bits: int) -> None:
    """GF-05: outputs are created exclusively and never followed/truncated."""
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, mode_bits)
    with os.fdopen(fd, "w", encoding="utf-8") as handle:
        handle.write(text)


def cmd_archive(args: argparse.Namespace) -> int:
    package = Path(args.package).resolve()
    output = Path(args.output)
    entries = validate_output(output, package, create=True)
    if any(e["severity"] == "error" for e in entries):
        for entry in entries:
            print(json.dumps(entry))
        return 2

    try:
        manifest = json.loads((package / MANIFEST_NAME).read_text(encoding="utf-8"))
        epoch = _epoch_from_manifest(manifest)
        mode_by_path = {f["path"]: f["mode"] for f in manifest.get("files", [])}
    except (ValueError, TypeError, OSError, KeyError):
        print(json.dumps(result_entry("error", "INVALID_FORMAT", MANIFEST_NAME,
                                      "package manifest missing or invalid for archive")))
        return 2

    # GF-05: refuse to archive a tree that would not verify against its own
    # manifest (extra/missing/drifted files); otherwise the archive could ship
    # bytes that were never authenticated end to end.
    src_entries, _src_files = verify_package_tree(
        package, package.parent / (MANIFEST_NAME + ".sha256"),
        expected_version=manifest.get("version"),
        expected_commit=manifest.get("sourceCommit"))
    src_errors = [e for e in src_entries if e["severity"] == "error"]
    if src_errors:
        print(json.dumps({"ok": False, "stage": "source-verify", "errors": src_errors}))
        return 2

    archive_path = output / f"{COMPONENT}-{manifest.get('version')}.tar.gz"
    payload = io.BytesIO()
    with tarfile.open(fileobj=payload, mode="w") as tar:
        # The manifest ships INSIDE the archive (self-verifying installs) and the
        # entry list stays fully sorted; verify authenticates it via the sidecar.
        all_paths = sorted([MANIFEST_NAME, *mode_by_path.keys()])
        for rel in all_paths:
            source_file = package / rel
            if rel == MANIFEST_NAME:
                fd = os.open(source_file, os.O_RDONLY | os.O_NOFOLLOW)
                before = os.fstat(fd)
                with os.fdopen(fd, "rb") as handle:
                    ti = tarfile.TarInfo(rel)
                    ti.size = before.st_size
                    ti.mtime = epoch
                    ti.uid = 0
                    ti.gid = 0
                    ti.uname = ""
                    ti.gname = ""
                    ti.mode = 0o644
                    ti.type = tarfile.REGTYPE
                    tar.addfile(ti, handle)
                    after = os.fstat(handle.fileno())
                    _assert_same_stat(before, after, rel)
                continue
            info = next(f for f in manifest["files"] if f["path"] == rel)
            digest, _stats = sha256_fd_checked(source_file)
            if digest != info["sha256"]:
                raise ReleaseError(f"source mutated before archiving: {rel}")
            fd, before = open_stream_checked(source_file)
            with os.fdopen(fd, "rb") as handle:
                ti = tarfile.TarInfo(rel)
                ti.size = info["size"]
                ti.mtime = epoch
                ti.uid = 0
                ti.gid = 0
                ti.uname = ""
                ti.gname = ""
                ti.mode = int(info["mode"], 8)
                ti.type = tarfile.REGTYPE
                tar.addfile(ti, handle)
                after = os.fstat(handle.fileno())
                _assert_same_stat(before, after, rel)

    archive_fd = os.open(archive_path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o644)
    with os.fdopen(archive_fd, "wb") as handle:
        with gzip.GzipFile(filename="", mode="wb", fileobj=handle, mtime=epoch) as gz:
            gz.write(payload.getvalue())
        handle.flush()
        os.fsync(handle.fileno())
    archive_sha = hashlib.sha256(archive_path.read_bytes()).hexdigest()

    sidecar = output / (archive_path.name + ".sha256")
    _write_exclusive(sidecar, f"{archive_sha}  {archive_path.name}\n", 0o600)
    manifest_sidecar = output / (MANIFEST_NAME + ".sha256")
    manifest_hash, _ = sha256_fd_checked(package / MANIFEST_NAME)
    _write_exclusive(manifest_sidecar, f"{manifest_hash}  {MANIFEST_NAME}\n", 0o600)

    extract = output / "extracted"
    extract.mkdir()
    with tarfile.open(archive_path, "r:gz") as tar:
        for member in tar.getmembers():
            target = extract / member.name
            if not target.resolve().is_relative_to(extract.resolve()):
                raise ReleaseError(f"unsafe archive member: {member.name}")
        tar.extractall(extract, filter="data")
        # The "data" safety filter normalizes modes; restore the archived modes so
        # verify can authenticate the roundtrip exactly (app host must stay 0755).
        for member in tar.getmembers():
            (extract / member.name).chmod(member.mode)

    # GF-05: success requires the EXTRACTED tree to re-verify against the
    # authenticated sidecar (end-to-end roundtrip, no manifest overwrite).
    v_entries, _v_files = verify_package_tree(
        extract, manifest_sidecar, require_sidecar=True,
        expected_version=manifest.get("version"),
        expected_commit=manifest.get("sourceCommit"))
    v_errors = [e for e in v_entries if e["severity"] == "error"]
    if v_errors:
        print(json.dumps({"ok": False, "stage": "post-archive-verify", "errors": v_errors}))
        return 2
    print(json.dumps({"ok": True, "archive": archive_path.name, "sha256": archive_sha}))
    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="linux_release.py",
                                     description="Spira Reforge Studio linux-x64 release tool")
    sub = parser.add_subparsers(dest="command", required=True)

    pre = sub.add_parser("preflight", help="check source/output/version/display state")
    pre.add_argument("--mode", choices=MODES, required=True)
    pre.add_argument("--source", required=True)
    pre.add_argument("--output", required=True)
    pre.add_argument("--version")
    pre.add_argument("--no-mkdir", action="store_true")
    pre.set_defaults(func=cmd_preflight)

    build = sub.add_parser("build", help="dotnet publish into an external empty output")
    build.add_argument("--mode", choices=MODES, required=True)
    build.add_argument("--source", required=True)
    build.add_argument("--output", required=True)
    build.add_argument("--version")
    build.set_defaults(func=cmd_build)

    gen = sub.add_parser("generate-contract", help="propose a policy component from a verified manifest")
    gen.add_argument("--package", required=True)
    gen.add_argument("--output", required=True)
    gen.add_argument("--sidecar")
    gen.set_defaults(func=cmd_generate_contract)

    verify = sub.add_parser("verify", help="authenticate a package against manifest and policy")
    verify.add_argument("--package", required=True)
    verify.add_argument("--policy")
    verify.add_argument("--component")
    verify.add_argument("--sidecar")
    verify.add_argument("--require-sidecar", action="store_true")
    verify.add_argument("--report")
    verify.add_argument("--version", help="require this manifest version (GF-04 identity gate)")
    verify.add_argument("--source-commit", help="require this source commit (GF-04 identity gate)")
    verify.set_defaults(func=cmd_verify)

    archive = sub.add_parser("archive", help="deterministic tar.gz + sidecars + extract verify")
    archive.add_argument("--package", required=True)
    archive.add_argument("--output", required=True)
    archive.set_defaults(func=cmd_archive)
    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    try:
        return args.func(args)
    except ReleaseError as error:
        print(json.dumps(result_entry("error", "INVALID_FORMAT", "cli", str(error))))
        return 2


if __name__ == "__main__":
    sys.exit(main())
