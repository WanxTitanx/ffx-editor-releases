#!/usr/bin/env python3
"""External Linux distribution smoke driver (D Task 5).

Extracts a VERIFIED release archive into a fresh external install, launches the
shipped executable with an isolated user state, navigates every public module
ID through the real Ctrl+K command palette with xdotool, captures per-module
screenshots with scrot, and proves package immutability before/after. Evidence
directories are never overwritten. The driver never calls private Dispatch, a
dev CLI or the source assembly - only the shipped binary.

Plan: docs/superpowers/plans/2026-09-13-linux-d-distribution-and-module-proof.md Task 5.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tarfile
import time
from pathlib import Path

RELEASE_TOOL = Path(__file__).resolve().parent / "linux_release.py"
MANIFEST_NAME = "release-manifest.json"


class SmokeError(Exception):
    pass


def require_tool(name: str) -> str:
    path = shutil.which(name)
    if not path:
        raise SmokeError(f"required tool missing on PATH: {name}")
    return path


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def tree_hashes(root: Path) -> dict[str, str]:
    return {
        path.relative_to(root).as_posix(): sha256_file(path)
        for path in sorted(root.rglob("*"))
        if path.is_file()
    }


def diff_trees(before: dict[str, str], after: dict[str, str]) -> dict[str, list[str]]:
    """GF-06: immutability must catch ADDED and REMOVED paths too.

    Comparing only keys present before the run silently accepts files added
    while the matrix ran; all three directions must be empty for a green run.
    """
    changed = sorted(p for p in before if p in after and after[p] != before[p])
    removed = sorted(p for p in before if p not in after)
    added = sorted(p for p in after if p not in before)
    return {"changed": changed, "removed": removed, "added": added}


def extract_archive(archive: Path, install: Path) -> None:
    if install.exists() and any(install.iterdir()):
        raise SmokeError(f"install directory not empty: {install}")
    install.mkdir(parents=True)
    with tarfile.open(archive, "r:*") as tar:
        for member in tar.getmembers():
            target = install / member.name
            if not target.resolve().is_relative_to(install.resolve()):
                raise SmokeError(f"unsafe archive member: {member.name}")
        tar.extractall(install, filter="data")
        for member in tar.getmembers():
            (install / member.name).chmod(member.mode)


def verify_install(install: Path, sidecar: Path) -> None:
    proc = subprocess.run(
        [sys.executable, str(RELEASE_TOOL), "verify",
         "--package", str(install), "--sidecar", str(sidecar), "--require-sidecar"],
        capture_output=True, text=True, check=False)
    if proc.returncode != 0:
        raise SmokeError(f"install failed verification:\n{proc.stdout}\n{proc.stderr}")


def find_window(timeout_s: float = 30.0, xdotool: str = "xdotool",
                name_pattern: str = "Spira", pid: int | None = None) -> str:
    # Avalonia/X11 windows do not reliably publish _NET_WM_PID, so search by
    # title; liveness is tracked through the launched process handle itself.
    deadline = time.monotonic() + timeout_s
    while time.monotonic() < deadline:
        proc = subprocess.run(
            [xdotool, "search", "--onlyvisible", "--name", name_pattern],
            capture_output=True, text=True, check=False)
        matches = [line.strip() for line in proc.stdout.splitlines()
                   if line.strip().isdigit()]
        # GF-06: a single visible title match plus a single matching process
        # binds this window to the launched PID (Avalonia has no _NET_WM_PID).
        if len(matches) == 1:
            name = subprocess.run(
                [xdotool, "getwindowname", matches[0]],
                capture_output=True, text=True, check=False).stdout.strip()
            if name:
                if pid is not None and not single_spira_process(pid):
                    raise SmokeError("expected exactly one editor process; refusing to bind window")
                return matches[0]
        elif matches:
            raise SmokeError(f"ambiguous window matches ({len(matches)}); cannot bind to launched PID")
        time.sleep(0.5)
    raise SmokeError(f"no visible window matching {name_pattern!r} within {timeout_s}s")


def single_spira_process(pid: int) -> bool:
    """True when the ONLY editor process on the system is the launched PID."""
    proc = subprocess.run(["pgrep", "-f", "FFXProjectEditor"],
                          capture_output=True, text=True, check=False)
    pids = [line.strip() for line in proc.stdout.splitlines() if line.strip().isdigit()]
    return pids == [str(pid)]


def window_title(window_id: str, xdotool: str = "xdotool") -> str:
    return subprocess.run(
        [xdotool, "getwindowname", window_id],
        capture_output=True, text=True, check=False).stdout.strip()


def activate(window_id: str, xdotool: str = "xdotool") -> None:
    subprocess.run([xdotool, "windowactivate", "--sync", window_id],
                   capture_output=True, check=False)


def navigate(module_id: str, window_id: str, xdotool: str = "xdotool") -> list[str]:
    activate(window_id, xdotool)
    failures: list[str] = []
    for step in (["key", "ctrl+k"], ["type", "--delay", "40", module_id], ["key", "Return"]):
        # GF-06: an ignored xdotool failure silently degrades the module oracle.
        proc = subprocess.run([xdotool, *step], capture_output=True, check=False)
        if proc.returncode != 0:
            failures.append("xdotool " + step[0] + " rc=" + str(proc.returncode))
    return failures


def screenshot(path: Path, scrot: str = "scrot") -> bool:
    if path.exists():
        raise SmokeError(f"refusing to overwrite evidence: {path}")
    proc = subprocess.run(
        [scrot, "-o", str(path)], capture_output=True, text=True, check=False)
    return proc.returncode == 0 and path.is_file() and path.stat().st_size > 0


def screenshot_content(path: Path) -> str:
    """Classify captured content: real / uniform / unchecked (GF-06).

    A black/uniform capture must not be counted as visual evidence; when PIL is
    unavailable the limitation is recorded honestly instead of assumed away.
    """
    if not path.is_file() or path.stat().st_size == 0:
        return "missing"
    if path.read_bytes()[:8] != b"\x89PNG\r\n\x1a\n":
        return "not-png"
    try:
        from PIL import Image
    except ImportError:
        return "unchecked-no-pil"
    try:
        with Image.open(path) as image:
            gray = image.convert("L").resize((64, 64))
            levels = len(set(gray.getdata()))
            return "real" if levels > 1 else "uniform"
    except Exception:
        return "unreadable"


def lock_down_evidence(evidence: Path) -> None:
    """GF-11: evidence keeps absolute paths, so it stays owner-only (0700/0600)."""
    os.chmod(evidence, 0o700)
    for path in evidence.rglob("*"):
        # Executable files (the shipped app host lives under install/) keep
        # their exec bit; everything else drops to owner-only 0600.
        if path.is_dir():
            os.chmod(path, 0o700)
        elif os.access(path, os.X_OK):
            os.chmod(path, 0o700)
        else:
            os.chmod(path, 0o600)


def run_matrix(args: argparse.Namespace) -> int:
    archive = Path(args.archive).resolve()
    sidecar = archive.parent / (MANIFEST_NAME + ".sha256")
    if not sidecar.is_file():
        sidecar = Path(args.sidecar) if args.sidecar else None
    if sidecar is None or not sidecar.is_file():
        raise SmokeError("manifest sidecar missing; pass --sidecar with the archive sidecar path")
    project_root = Path(args.project_root).resolve()
    evidence = Path(args.evidence).resolve()
    if evidence.exists() and any(evidence.iterdir()):
        raise SmokeError(f"evidence directory not empty: {evidence}")
    if not project_root.is_dir():
        raise SmokeError(f"project root missing: {project_root}")

    xdotool = require_tool("xdotool")
    scrot = require_tool("scrot")
    if not os.environ.get("DISPLAY"):
        raise SmokeError("no DISPLAY; an X11/XWayland session is required")

    install_root = evidence / "install"
    extract_archive(archive, install_root)
    verify_install(install_root, sidecar)

    before = tree_hashes(install_root)
    # Plan D5 step 8: the selected project inventory must survive the whole run
    # byte-identically (read-only navigation; writers run on private copies only).
    project_before = tree_hashes(project_root)
    user_state = evidence / "user-state"
    for name in ("home", "config", "cache", "data", "runtime"):
        (user_state / name).mkdir(parents=True)

    env = dict(os.environ)
    env["HOME"] = str(user_state / "home")
    env["XDG_CONFIG_HOME"] = str(user_state / "config")
    env["XDG_CACHE_HOME"] = str(user_state / "cache")
    env["XDG_DATA_HOME"] = str(user_state / "data")
    env["XDG_RUNTIME_DIR"] = str(user_state / "runtime")

    executable = install_root / "FFXProjectEditor"
    if not os.access(executable, os.X_OK):
        raise SmokeError("shipped app host is not executable")
    log_handle = (evidence / "app.log").open("w", encoding="utf-8")
    # Real product pathway: the CLI accepts the master folder as the startup
    # project argument (same code path as the desktop launcher), so data-bearing
    # modules open against the real corpus without any test-only backdoor.
    process = subprocess.Popen(
        [str(executable), str(project_root)], cwd=str(install_root), env=env,
        stdout=log_handle, stderr=subprocess.STDOUT)
    try:
        window_id = find_window(args.window_timeout, xdotool, pid=process.pid)
        rows = []
        screenshots = evidence / "screenshots"
        screenshots.mkdir()
        for module_id in args.modules:
            nav_failures = navigate(module_id, window_id, xdotool)
            time.sleep(args.settle_seconds)
            if process.poll() is not None:
                raise SmokeError(f"process died while navigating module {module_id}")
            title = window_title(window_id, xdotool)
            shot = screenshots / f"{module_id}.png"
            captured = screenshot(shot, scrot)
            content = screenshot_content(shot) if captured else "missing"
            rows.append({
                "module_id": module_id,
                "window_title": title,
                "pid": process.pid,
                "alive": True,
                "screenshot": captured,
                "screenshot_content": content,
                "navigation_ok": not nav_failures,
                "navigation_failures": nav_failures,
                "package_unchanged": None,
            })

        after = tree_hashes(install_root)
        package_diff = diff_trees(before, after)
        project_after = tree_hashes(project_root)
        project_diff = diff_trees(project_before, project_after)
        mutated = package_diff["changed"]
        package_clean = not (package_diff["changed"] or package_diff["removed"] or package_diff["added"])
        project_clean = not (project_diff["changed"] or project_diff["removed"] or project_diff["added"])
        for row in rows:
            row["package_unchanged"] = package_clean

        shot_hashes = [sha256_file(screenshots / f"{m}.png") for m in args.modules
                       if (screenshots / f"{m}.png").is_file()]

        matrix = {
            "archive": str(archive),
            "archive_sha256": sha256_file(archive),
            # GF-11: full paths stay only in this owner-only evidence file.
            "project_root": str(project_root),
            "install": str(install_root),
            "rows": rows,
            "package_mutations": package_diff,
            "project_mutations": project_diff,
            "summary": {
                "modules": len(rows),
                "alive": sum(1 for r in rows if r["alive"]),
                "screenshots": sum(1 for r in rows if r["screenshot"]),
                "real_screenshots": sum(1 for r in rows if r["screenshot_content"] == "real"),
                "navigation_ok": sum(1 for r in rows if r["navigation_ok"]),
                "distinct_screens": len(set(shot_hashes)),
                "package_unchanged": package_clean,
                "project_unchanged": project_clean,
            },
        }
        (evidence / "matrix.json").write_text(json.dumps(matrix, indent=2) + "\n")
        os.chmod(evidence / "matrix.json", 0o600)
        print(json.dumps(matrix["summary"]))
        # GF-06 exit contract: alive AND navigated AND real screenshots AND dual
        # immutability; distinct screens are required with a loaded project.
        green = (all(r["alive"] and r["navigation_ok"] for r in rows)
                 and all(r["screenshot_content"] == "real" for r in rows)
                 and package_clean and project_clean
                 and (not args.require_distinct or
                      len(set(shot_hashes)) == len(args.modules)))
        lock_down_evidence(evidence)
        return 0 if green else 2
    finally:
        process.terminate()
        try:
            process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            process.kill()
        log_handle.close()


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="smoke_linux_distribution.py")
    parser.add_argument("--archive", required=True)
    parser.add_argument("--project-root", required=True)
    parser.add_argument("--evidence", required=True)
    parser.add_argument("--sidecar")
    parser.add_argument("--settle-seconds", type=float, default=3.0)
    parser.add_argument("--window-timeout", type=float, default=30.0)
    parser.add_argument("--require-distinct", action="store_true",
                        help="require every module screenshot to be distinct (real-data runs)")
    parser.add_argument("--modules", nargs="+", required=True)
    args = parser.parse_args(argv)
    try:
        return run_matrix(args)
    except SmokeError as error:
        print(json.dumps({"ok": False, "error": str(error)}))
        return 2


if __name__ == "__main__":
    sys.exit(main())
