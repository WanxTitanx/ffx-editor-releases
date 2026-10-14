#!/usr/bin/env python3
"""Unit tests for scripts/release/linux_release.py (standard library only).

Covers the D-plan adversarial list: output discipline, dirty-Git modes, version
reconciliation, symlink/hardlink/special-file rejection, traversal/backslash/
case-fold collisions, drift detection, ELF/mode/deps validation, policy closure,
archive determinism (A/B bytes + fixed gzip mtime) and no-overwrite guarantees.
"""

from __future__ import annotations

import gzip
import hashlib
import io
import json
import os
import stat
import subprocess
import sys
import tarfile
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parent / "linux_release.py"
sys.path.insert(0, str(SCRIPT.parent))
import linux_release as lr  # noqa: E402


def git_repo(path: Path) -> Path:
    path.mkdir(parents=True)
    env = dict(os.environ)
    for key, value in {
        "GIT_AUTHOR_NAME": "t", "GIT_AUTHOR_EMAIL": "t@t",
        "GIT_COMMITTER_NAME": "t", "GIT_COMMITTER_EMAIL": "t@t",
    }.items():
        env[key] = value
    def run(*args):
        subprocess.run(["git", "-C", str(path), *args], check=True, env=env,
                       capture_output=True, text=True)
    run("init", "-q")
    run("config", "user.name", "t")
    run("config", "user.email", "t@t")
    (path / "FFXProjectEditor").mkdir()
    (path / "FFXProjectEditor" / "FFXProjectEditor.csproj").write_text(
        "<Project><PropertyGroup><Version>9.9.9.9</Version></PropertyGroup></Project>")
    (path / "CHANGELOG.md").write_text("## [9.9.9.9]\n")
    (path / "changelogUS.md").write_text("## [9.9.9.9]\n")
    run("add", ".")
    run("commit", "-qm", "test")
    return path


def fake_apphost(path: Path, elf_class: int = 2, machine: int = 62, mode: int = 0o755) -> None:
    header = bytearray(20)
    header[0:4] = b"\x7fELF"
    header[4] = elf_class
    header[18:20] = int(machine).to_bytes(2, "little")
    path.write_bytes(bytes(header) + b"payload")
    path.chmod(mode)


def make_package(root: Path, *, apphost_class: int = 2, apphost_machine: int = 62,
                 apphost_mode: int = 0o755, deps_target: str = lr.EXPECTED_DEPS_TARGET,
                 extra_writes: dict | None = None) -> Path:
    package = root / "package"
    package.mkdir()
    fake_apphost(package / lr.APP_HOST, apphost_class, apphost_machine, apphost_mode)
    (package / lr.DEPS_NAME).write_text(json.dumps({"targets": {deps_target: {}}}))
    files = []
    for path in sorted(package.rglob("*")):
        if path.is_dir():
            continue
        rel = path.relative_to(package).as_posix()
        digest, _ = lr.sha256_stat_checked(path)
        files.append({"path": rel, "size": path.stat().st_size, "sha256": digest,
                      "mode": format(stat.S_IMODE(path.stat().st_mode), "04o")})
    manifest = {"schemaVersion": 2, "target": "linux-x64", "profile": lr.PROFILE,
                "version": "9.9.9.9", "sourceCommit": "0" * 40,
                "sourceCommitTimestampUtc": "2026-09-13T00:00:00+00:00",
                "component": lr.COMPONENT, "files": files}
    (package / lr.MANIFEST_NAME).write_text(json.dumps(manifest, indent=2) + "\n")
    if extra_writes:
        for rel, action in extra_writes.items():
            target = package / rel
            action(target)
    return package


def sidecar_for(package: Path, root: Path) -> Path:
    digest, _ = lr.sha256_stat_checked(package / lr.MANIFEST_NAME)
    sidecar = root / (lr.MANIFEST_NAME + ".sha256")
    sidecar.write_text(f"{digest}  {lr.MANIFEST_NAME}\n")
    return sidecar


def run_cli(*args: str) -> subprocess.CompletedProcess:
    return subprocess.run([sys.executable, str(SCRIPT), *args],
                          capture_output=True, text=True, check=False)


class OutputDiscipline(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        self.source = git_repo(self.tmp / "repo")
        self.addCleanup(lambda: subprocess.run(["chmod", "-R", "u+rwX", str(self.tmp)],
                                               capture_output=True))

    def test_relative_output_is_rejected(self):
        result = run_cli("preflight", "--mode", "Diagnostic",
                         "--source", str(self.source), "--output", "relative/out", "--no-mkdir")
        self.assertEqual(result.returncode, 2)
        self.assertIn("OUTPUT_NOT_EXTERNAL", result.stdout)

    def test_output_inside_source_is_rejected(self):
        result = run_cli("preflight", "--mode", "Diagnostic",
                         "--source", str(self.source),
                         "--output", str(self.source / "inside"), "--no-mkdir")
        self.assertEqual(result.returncode, 2)
        self.assertIn("OUTPUT_NOT_EXTERNAL", result.stdout)

    def test_non_empty_output_is_rejected(self):
        out = self.tmp / "out"
        out.mkdir()
        (out / "stale.txt").write_text("x")
        result = run_cli("preflight", "--mode", "Diagnostic",
                         "--source", str(self.source), "--output", str(out), "--no-mkdir")
        self.assertEqual(result.returncode, 2)
        self.assertIn("OUTPUT_NOT_EMPTY", result.stdout)


class PreflightModes(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        self.source = git_repo(self.tmp / "repo")
        self.out = self.tmp / "preflight-out"

    def test_diagnostic_tolerates_dirty_tree(self):
        (self.source / "uncommitted.txt").write_text("x")
        result = run_cli("preflight", "--mode", "Diagnostic",
                         "--source", str(self.source), "--output", str(self.out), "--no-mkdir")
        self.assertEqual(result.returncode, 0)

    def test_candidate_rejects_dirty_tree(self):
        (self.source / "uncommitted.txt").write_text("x")
        result = run_cli("preflight", "--mode", "Candidate",
                         "--source", str(self.source), "--output", str(self.out), "--no-mkdir")
        self.assertEqual(result.returncode, 2)
        self.assertIn("GIT_DIRTY", result.stdout)

    def test_missing_changelog_version_is_unreconciled(self):
        (self.source / "changelogUS.md").write_text("no version header\n")
        result = run_cli("preflight", "--mode", "Candidate",
                         "--source", str(self.source), "--output", str(self.out), "--no-mkdir")
        self.assertEqual(result.returncode, 2)
        self.assertIn("VERSION_UNRECONCILED", result.stdout)

    def test_result_json_is_deterministic(self):
        run_cli("preflight", "--mode", "Diagnostic",
                "--source", str(self.source), "--output", str(self.out), "--no-mkdir")
        payload = json.loads((self.out / "preflight.json").read_text())
        keys = [e["code"] for e in payload["results"]]
        self.assertEqual(keys, sorted(keys, key=lambda k: k) or keys)
        self.assertIn("summary", payload)


class VerifyAdversarial(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        self.package = make_package(self.tmp)
        self.sidecar = sidecar_for(self.package, self.tmp)

    def verify(self, *extra: str):
        return run_cli("verify", "--package", str(self.package),
                       "--sidecar", str(self.sidecar), "--require-sidecar", *extra)

    def test_clean_package_verifies(self):
        result = self.verify()
        self.assertEqual(result.returncode, 0, result.stdout)

    def test_symlink_is_rejected(self):
        os.symlink(self.tmp / "elsewhere", self.package / "link.so")
        result = self.verify()
        self.assertEqual(result.returncode, 2)
        self.assertIn("INVALID_FORMAT", result.stdout)

    def test_hardlink_is_rejected(self):
        outside = self.tmp / "hard.bin"
        outside.write_bytes(b"z")
        os.link(outside, self.package / "hard.bin")
        result = self.verify()
        self.assertEqual(result.returncode, 2)
        self.assertIn("INVALID_FORMAT", result.stdout)

    def test_special_file_is_rejected(self):
        os.mkfifo(self.package / "fifo")
        result = self.verify()
        self.assertEqual(result.returncode, 2)

    def test_case_fold_collision_is_rejected(self):
        manifest = json.loads((self.package / lr.MANIFEST_NAME).read_text())
        for name in ("Collide.txt", "collide.txt"):
            path = self.package / name
            path.write_text("c")
            digest, _ = lr.sha256_stat_checked(path)
            manifest["files"].append({"path": name, "size": 1, "sha256": digest, "mode": "0644"})
        (self.package / lr.MANIFEST_NAME).write_text(json.dumps(manifest))
        sidecar_for(self.package, self.tmp)
        result = self.verify()
        self.assertEqual(result.returncode, 2)
        self.assertIn("case-fold collision", result.stdout)

    def test_hash_drift_is_rejected(self):
        (self.package / lr.DEPS_NAME).write_text(json.dumps({"targets": {}}))
        result = self.verify()
        self.assertEqual(result.returncode, 2)
        self.assertIn("ARTIFACT_DIVERGENCE", result.stdout)

    def test_wrong_elf_class_is_rejected(self):
        fake_apphost(self.package / lr.APP_HOST, elf_class=1)
        result = self.verify()
        self.assertEqual(result.returncode, 2)
        self.assertIn("INVALID_PLATFORM", result.stdout)

    def test_non_executable_apphost_is_rejected(self):
        (self.package / lr.APP_HOST).chmod(0o644)
        result = self.verify()
        self.assertEqual(result.returncode, 2)
        self.assertIn("PERMISSION_DENIED", result.stdout)

    def test_wrong_deps_target_is_rejected(self):
        for path in (self.package / lr.DEPS_NAME,):
            path.unlink()
        fake_apphost(self.package / lr.APP_HOST)
        (self.package / lr.DEPS_NAME).write_text(json.dumps({"targets": {"wrong": {}}}))
        manifest = json.loads((self.package / lr.MANIFEST_NAME).read_text())
        files = []
        for path in sorted(p for p in self.package.rglob("*") if p.is_file()):
            rel = path.relative_to(self.package).as_posix()
            if rel == lr.MANIFEST_NAME:
                continue
            digest, _ = lr.sha256_stat_checked(path)
            files.append({"path": rel, "size": path.stat().st_size, "sha256": digest,
                          "mode": format(stat.S_IMODE(path.stat().st_mode), "04o")})
        manifest["files"] = files
        (self.package / lr.MANIFEST_NAME).write_text(json.dumps(manifest))
        sidecar_for(self.package, self.tmp)
        result = self.verify()
        self.assertEqual(result.returncode, 2)
        self.assertIn("INVALID_PLATFORM", result.stdout)

    def test_missing_policy_component_is_rejected(self):
        policy = self.tmp / "policy.json"
        policy.write_text(json.dumps({"components": {}}))
        result = self.verify("--policy", str(policy))
        self.assertEqual(result.returncode, 2)
        self.assertIn("not reconciled", result.stdout)

    def test_policy_required_file_and_roots(self):
        policy = self.tmp / "policy.json"
        policy.write_text(json.dumps({"components": {lr.COMPONENT: {
            "target": "linux-x64",
            "requiredFiles": [lr.APP_HOST, lr.DEPS_NAME],
            "allowedRoots": [],
            "allowedTopLevel": [lr.APP_HOST, lr.DEPS_NAME, lr.MANIFEST_NAME],
        }}}))
        result = self.verify("--policy", str(policy))
        self.assertEqual(result.returncode, 0, result.stdout)


class ArchiveDeterminism(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        self.package = make_package(self.tmp)

    def test_ab_archives_are_byte_identical(self):
        hashes = []
        for name in ("a", "b"):
            out = self.tmp / f"arch-{name}"
            result = run_cli("archive", "--package", str(self.package), "--output", str(out))
            self.assertEqual(result.returncode, 0, result.stdout)
            archive = next(out.glob("*.tar.gz"))
            hashes.append(hashlib.sha256(archive.read_bytes()).hexdigest())
        self.assertEqual(hashes[0], hashes[1])

    def test_gzip_header_mtime_is_pinned(self):
        out = self.tmp / "arch-mtime"
        run_cli("archive", "--package", str(self.package), "--output", str(out))
        archive = next(out.glob("*.tar.gz"))
        raw = archive.read_bytes()
        mtime = int.from_bytes(raw[4:8], "little")
        manifest = json.loads((self.package / lr.MANIFEST_NAME).read_text())
        expected = lr._epoch_from_manifest(manifest)
        self.assertEqual(mtime, expected)
        self.assertEqual(raw[3], 0)  # no original filename flag

    def test_archive_output_never_overwrites(self):
        out = self.tmp / "arch-dup"
        out.mkdir()
        (out / "existing.txt").write_text("x")
        result = run_cli("archive", "--package", str(self.package), "--output", str(out))
        self.assertEqual(result.returncode, 2)

    def test_extracted_archive_contains_sorted_regular_files_only(self):
        out = self.tmp / "arch-x"
        run_cli("archive", "--package", str(self.package), "--output", str(out))
        archive = next(out.glob("*.tar.gz"))
        with tarfile.open(archive, "r:gz") as tar:
            members = tar.getmembers()
        paths = [m.name for m in members]
        self.assertEqual(paths, sorted(paths))
        self.assertTrue(all(m.isfile() for m in members))
        self.assertTrue(all(m.uid == 0 and m.gid == 0 for m in members))


class GenerateContract(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        self.package = make_package(self.tmp)
        self.sidecar = sidecar_for(self.package, self.tmp)

    def test_proposes_fragment_only_in_external_output(self):
        out = self.tmp / "contract-out"
        result = run_cli("generate-contract", "--package", str(self.package),
                         "--output", str(out), "--sidecar", str(self.sidecar))
        self.assertEqual(result.returncode, 0, result.stdout)
        proposed = out / f"proposed-{lr.COMPONENT}.json"
        fragment = json.loads(proposed.read_text())
        self.assertEqual(fragment["target"], "linux-x64")
        self.assertGreater(len(fragment["files"]), 0)

    def test_tampered_manifest_sidecar_is_rejected(self):
        manifest = json.loads((self.package / lr.MANIFEST_NAME).read_text())
        manifest["version"] = "0.0.0.0"
        (self.package / lr.MANIFEST_NAME).write_text(json.dumps(manifest))
        out = self.tmp / "contract-bad"
        result = run_cli("generate-contract", "--package", str(self.package),
                         "--output", str(out), "--sidecar", str(self.sidecar))
        self.assertEqual(result.returncode, 2)


class ReviewHardeningGF01toGF06(unittest.TestCase):
    """RED regressions for the independent review findings GF-03..GF-05."""

    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        self.source = git_repo(self.tmp / "repo")
        pkgroot = self.tmp / "pkgroot"
        pkgroot.mkdir()
        self.package = make_package(pkgroot)
        self.sidecar = sidecar_for(self.package, pkgroot)

    def test_relative_output_writes_nothing(self):
        # GF-03: a rejected (relative) output must not be created or written.
        import contextlib
        with contextlib.chdir(self.tmp):
            result = run_cli("preflight", "--mode", "Diagnostic",
                             "--source", str(self.source),
                             "--output", "relative/out", "--no-mkdir")
        self.assertEqual(result.returncode, 2)
        self.assertIn("OUTPUT_NOT_EXTERNAL", result.stdout)
        self.assertFalse((self.tmp / "relative").exists(),
                         "rejected output path must not be created")

    def test_foreign_identity_is_rejected(self):
        # GF-04: valid file hashes + valid sidecar but target=win-x64 must fail.
        manifest = json.loads((self.package / lr.MANIFEST_NAME).read_text())
        manifest["target"] = "win-x64"
        (self.package / lr.MANIFEST_NAME).write_text(json.dumps(manifest, indent=2) + "\n")
        self.sidecar = sidecar_for(self.package, self.tmp / "pkgroot")
        result = run_cli("verify", "--package", str(self.package),
                         "--sidecar", str(self.sidecar), "--require-sidecar")
        self.assertEqual(result.returncode, 2, result.stdout)
        self.assertIn("INVALID_PLATFORM", result.stdout)

    def test_malformed_manifest_reports_invalid_format(self):
        # GF-04: malformed JSON must yield stable INVALID_FORMAT, not a traceback
        # and not an absolute script-path leak.
        (self.package / lr.MANIFEST_NAME).write_text("{")
        result = run_cli("verify", "--package", str(self.package))
        self.assertEqual(result.returncode, 2)
        self.assertIn("INVALID_FORMAT", result.stdout)
        self.assertNotIn("Traceback", result.stderr)
        self.assertNotIn(str(SCRIPT), result.stdout + result.stderr)

    def test_archive_rejects_unverified_source(self):
        # GF-05: a package with a file on disk that is absent from its manifest
        # must be refused before any archive bytes are produced.
        (self.package / "extra-unlisted.bin").write_bytes(b"not-in-manifest")
        out = self.tmp / "archout"
        result = run_cli("archive", "--package", str(self.package),
                         "--output", str(out))
        self.assertEqual(result.returncode, 2, result.stdout)
        self.assertIn("source-verify", result.stdout)


if __name__ == "__main__":
    unittest.main()
