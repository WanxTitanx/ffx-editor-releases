#!/usr/bin/env python3
"""Nine-language shell pass for the shipped Linux distribution (D Task 5).

Uses the REAL product pathway: Strings.ResolveLanguage reads
LocalAppData/FFXProjectEditor/ui_lang.config at startup (the same file the
in-app language selector persists). For each of the nine languages we seed
that file in the isolated user state, launch the shipped executable with the
real master project, wait for the shell, capture a screenshot and prove the
process stayed responsive. No test-only switches.
"""

from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import sys
import time
from pathlib import Path

RELEASE_TOOL = Path(__file__).resolve().parent / "linux_release.py"
LANGUAGES = ["pt", "en", "es", "fr", "de", "it", "ja", "ko", "zh"]


def sha_file(path: Path) -> str:
    import hashlib
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="smoke_languages.py")
    parser.add_argument("--install", required=True, help="verified install root")
    parser.add_argument("--project-root", required=True)
    parser.add_argument("--evidence", required=True)
    parser.add_argument("--window-timeout", type=float, default=30.0)
    parser.add_argument("--settle-seconds", type=float, default=4.0)
    parser.add_argument("--allow-duplicate-shells", action="store_true",
                        help="GF-10 escape hatch: record (do not gate on) duplicate shells")
    args = parser.parse_args(argv)

    install = Path(args.install).resolve()
    project = Path(args.project_root).resolve()
    evidence = Path(args.evidence).resolve()
    if evidence.exists() and any(evidence.iterdir()):
        print(json.dumps({"ok": False, "error": "evidence directory not empty"}))
        return 2
    for tool in ("xdotool", "scrot"):
        if not shutil.which(tool):
            print(json.dumps({"ok": False, "error": f"missing tool: {tool}"}))
            return 2

    evidence.mkdir(parents=True)
    user_state = evidence / "user-state"
    home = user_state / "home"
    # WHY: with XDG_DATA_HOME isolated below, .NET LocalApplicationData resolves
    # to that XDG dir (NOT ~/.local/share) — seed the file where the shipped
    # binary actually reads it; the home copy stays as a compatibility fallback.
    local_share = user_state / "data" / "FFXProjectEditor"
    legacy_share = home / ".local" / "share" / "FFXProjectEditor"
    legacy_share.mkdir(parents=True, exist_ok=True)
    local_share.mkdir(parents=True)
    for name in ("config", "cache", "data", "runtime"):
        (user_state / name).mkdir(exist_ok=True)

    env = dict(os.environ)
    env["HOME"] = str(home)
    env["XDG_CONFIG_HOME"] = str(user_state / "config")
    env["XDG_CACHE_HOME"] = str(user_state / "cache")
    env["XDG_DATA_HOME"] = str(user_state / "data")
    env["XDG_RUNTIME_DIR"] = str(user_state / "runtime")

    executable = install / "FFXProjectEditor"
    rows = []
    for lang in LANGUAGES:
        (local_share / "ui_lang.config").write_text(lang + "\n")
        (legacy_share / "ui_lang.config").write_text(lang + "\n")
        log = (evidence / f"app-{lang}.log").open("w", encoding="utf-8")
        process = subprocess.Popen(
            [str(executable), str(project)], cwd=str(install), env=env,
            stdout=log, stderr=subprocess.STDOUT)
        try:
            window_id = ""
            deadline = time.monotonic() + args.window_timeout
            while time.monotonic() < deadline:
                found = subprocess.run(
                    ["xdotool", "search", "--onlyvisible", "--name", "Spira"],
                    capture_output=True, text=True, check=False).stdout.splitlines()
                digits = [f.strip() for f in found if f.strip().isdigit()]
                # GF-06 binding: exactly one visible match, or refuse to guess.
                if len(digits) == 1:
                    window_id = digits[0]
                    break
                if len(digits) > 1:
                    rows.append({"lang": lang, "alive": False, "window": False,
                                 "screenshot": False,
                                 "note": f"ambiguous windows ({len(digits)})"})
                    window_id = ""
                    break
                time.sleep(0.4)
            if not window_id:
                rows.append({"lang": lang, "alive": False, "window": False,
                             "screenshot": False, "note": "window timeout"})
                continue
            time.sleep(args.settle_seconds)
            alive = process.poll() is None
            subprocess.run(["xdotool", "windowactivate", "--sync", window_id],
                           capture_output=True, check=False)
            shot = evidence / f"lang-{lang}.png"
            captured = False
            if alive:
                proc = subprocess.run(["scrot", "-o", str(shot)],
                                       capture_output=True, check=False)
                captured = proc.returncode == 0 and shot.is_file() and shot.stat().st_size > 0
            rows.append({"lang": lang, "alive": alive, "window": True,
                         "screenshot": captured, "note": ""})
        finally:
            process.terminate()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()
            log.close()

    hashes = {}
    for lang in LANGUAGES:
        shot = evidence / f"lang-{lang}.png"
        if shot.is_file():
            hashes[lang] = sha_file(shot)
    distinct = len(set(hashes.values()))
    summary = {
        "languages": len(rows),
        "alive": sum(1 for r in rows if r["alive"]),
        "screenshots": sum(1 for r in rows if r["screenshot"]),
        "distinct_shells": distinct,
        "duplicate_shells_allowed": bool(args.allow_duplicate_shells),
    }
    (evidence / "languages.json").write_text(
        json.dumps({"rows": rows, "hashes": hashes, "summary": summary}, indent=2) + "\n")
    # GF-10: an all-same untranslated shell must FAIL the gate by default; the
    # escape hatch records the limitation instead of silently claiming proof.
    distinct_ok = distinct == len(LANGUAGES) or args.allow_duplicate_shells
    ok = (summary["alive"] == len(LANGUAGES)
          and summary["screenshots"] == len(LANGUAGES)
          and distinct_ok)
    # GF-11: language evidence stays owner-only.
    os.chmod(evidence, 0o700)
    for path in evidence.rglob("*"):
        os.chmod(path, 0o700 if path.is_dir() else 0o600)
    print(json.dumps(summary))
    return 0 if ok else 2


if __name__ == "__main__":
    sys.exit(main())
