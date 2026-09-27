#!/usr/bin/env python3
"""Fake-based unit tests for scripts/release/smoke_linux_distribution.py."""

from __future__ import annotations

import json
import os
import subprocess
import sys
import tarfile
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parent / "smoke_linux_distribution.py"
sys.path.insert(0, str(SCRIPT.parent))
import smoke_linux_distribution as sm  # noqa: E402


def fake_archive(root: Path) -> Path:
    stage = root / "arch"
    stage.mkdir()
    app = stage / "FFXProjectEditor"
    app.write_bytes(b"\x7fELF" + b"\x02" + b"x" * 15)
    app.chmod(0o755)
    import hashlib
    manifest = {
        "schemaVersion": 2, "target": "linux-x64", "profile": "linux-x64-portable",
        "version": "9.9.9.9", "sourceCommit": "0" * 40,
        "sourceCommitTimestampUtc": "2026-09-13T00:00:00+00:00",
        "component": "studio-core-linux-x64",
        "files": [{"path": "FFXProjectEditor", "size": app.stat().st_size,
                   "sha256": hashlib.sha256(app.read_bytes()).hexdigest(),
                   "mode": "0755"}],
    }
    (stage / sm.MANIFEST_NAME).write_text(json.dumps(manifest))
    digest = sm.sha256_file(stage / sm.MANIFEST_NAME)
    (stage / (sm.MANIFEST_NAME + ".sha256")).write_text(f"{digest}  {sm.MANIFEST_NAME}\n")
    # The driver looks for the sidecar beside the ARCHIVE (its release layout).
    (root / (sm.MANIFEST_NAME + ".sha256")).write_text(f"{digest}  {sm.MANIFEST_NAME}\n")
    archive = root / "fake.tar.gz"
    import gzip
    payload = stage / "body.tar"
    with tarfile.open(payload, "w") as tar:
        for path in (app, stage / sm.MANIFEST_NAME):
            info = tar.gettarinfo(path, arcname=path.name)
            info.uid = info.gid = 0
            info.uname = info.gname = ""
            with path.open("rb") as fh:
                tar.addfile(info, fh)
    with archive.open("wb") as raw_archive:
        with gzip.GzipFile(filename="", mode="wb", fileobj=raw_archive, mtime=0) as gz:
            gz.write(payload.read_bytes())
    return archive


def fake_bin(root: Path, name: str, body: str) -> Path:
    folder = root / "bin"
    folder.mkdir(exist_ok=True)
    script = folder / name
    script.write_text("#!/bin/sh\n" + body)
    script.chmod(0o755)
    return script


class SmokeDriverTests(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        self.archive = fake_archive(self.tmp)

    def test_missing_evidence_dir_must_be_empty(self):
        evidence = self.tmp / "ev"
        evidence.mkdir()
        (evidence / "stale").write_text("x")
        result = subprocess.run(
            [sys.executable, str(SCRIPT), "--archive", str(self.archive),
             "--project-root", str(self.tmp), "--evidence", str(evidence),
             "--modules", "home"],
            capture_output=True, text=True, check=False)
        self.assertEqual(result.returncode, 2)
        self.assertIn("not empty", result.stdout)

    def test_missing_project_root_is_rejected(self):
        result = subprocess.run(
            [sys.executable, str(SCRIPT), "--archive", str(self.archive),
             "--project-root", str(self.tmp / "nope"), "--evidence", str(self.tmp / "ev1"),
             "--modules", "home"],
            capture_output=True, text=True, check=False)
        self.assertEqual(result.returncode, 2)
        self.assertIn("project root missing", result.stdout)

    def test_missing_tool_is_reported(self):
        env = dict(os.environ)
        env["PATH"] = str(self.tmp / "emptybin")
        (self.tmp / "emptybin").mkdir()
        result = subprocess.run(
            [sys.executable, str(SCRIPT), "--archive", str(self.archive),
             "--project-root", str(self.tmp), "--evidence", str(self.tmp / "ev2"),
             "--modules", "home"],
            env=env, capture_output=True, text=True, check=False)
        self.assertEqual(result.returncode, 2)
        self.assertIn("required tool missing", result.stdout)

    def test_tree_hashes_detect_mutation(self):
        install = self.tmp / "tree"
        install.mkdir()
        (install / "a.txt").write_text("one")
        before = sm.tree_hashes(install)
        (install / "a.txt").write_text("two")
        after = sm.tree_hashes(install)
        self.assertNotEqual(before["a.txt"], after["a.txt"])

    def test_screenshot_refuses_overwrite(self):
        shot = self.tmp / "shot.png"
        shot.write_bytes(b"old")
        with self.assertRaises(sm.SmokeError):
            sm.screenshot(shot, "true")

    def test_extract_restores_modes(self):
        install = self.tmp / "install-x"
        sm.extract_archive(self.archive, install)
        self.assertTrue(os.access(install / "FFXProjectEditor", os.X_OK))

    def test_unsafe_member_is_rejected(self):
        bad = self.tmp / "bad.tar"
        with tarfile.open(bad, "w") as tar:
            info = tarfile.TarInfo("../escape")
            info.size = 0
            tar.addfile(info)
        with self.assertRaises(sm.SmokeError):
            sm.extract_archive(bad, self.tmp / "install-bad")

    def test_tree_diff_detects_added_files(self):
        # GF-06: files ADDED after the snapshot must count as mutations.
        before = {"existing.txt": "1"}
        after = {"existing.txt": "1", "added-after-snapshot": "2"}
        diff = sm.diff_trees(before, after)
        self.assertEqual(diff["added"], ["added-after-snapshot"])
        self.assertEqual(diff["changed"], [])
        self.assertEqual(diff["removed"], [])

    def test_navigate_reports_xdotool_failure(self):
        # GF-06: an xdotool step failing must surface as a navigation failure.
        fake = fake_bin(self.tmp, "xdotool", "exit 1\n")
        failures = sm.navigate("home", "12345", xdotool=str(fake))
        self.assertTrue(failures, "navigate must report xdotool failures")


if __name__ == "__main__":
    unittest.main()
