using System;
using System.IO;

namespace FFXProjectEditor.FfxLib.Ai.Sin
{
    // SIN Chain Builder — Gate 5 (backup/apply sandbox) backup of a SANDBOX copy. SANDBOX-ONLY.
    //
    // A SinSandboxBackup snapshots a sandbox copy file to a sibling `<file>.prev.bak` BEFORE any write, capturing
    // the pre-apply image hash. The whole point is the rung-4 (backup-ready) guarantee: a writable .prev.bak of the
    // (sandbox) target exists before the first byte is written, and Restore() returns the sandbox to it byte-for-byte.
    //
    // HARD: this is a backup of the SANDBOX COPY only. It is NEVER created for a real project/game file — the only
    // path that ever reaches Create() is a copy already living under work/ (the session enforces that). There is no
    // real `.prev.bak` of any user/game monster_*.bin anywhere in this class. Backup/apply sandbox nao e authoring
    // publico.
    public sealed class SinSandboxBackup
    {
        public string SandboxPath { get; }
        public string BackupPath { get; }
        /// <summary>SHA-256 of the sandbox copy at the instant the backup was taken (the pre-apply image).</summary>
        public string PreImageSha256 { get; }

        private SinSandboxBackup(string sandboxPath, string backupPath, string preImageSha256)
        {
            SandboxPath = sandboxPath;
            BackupPath = backupPath;
            PreImageSha256 = preImageSha256;
        }

        /// <summary>Create a `<sandboxPath>.prev.bak` snapshot BEFORE any write. The sandbox file must already exist
        /// (the session copies the source monster into the sandbox first). Captures the pre-apply hash.</summary>
        public static SinSandboxBackup Create(string sandboxPath)
        {
            ArgumentNullException.ThrowIfNull(sandboxPath);
            if (!File.Exists(sandboxPath))
                throw new FileNotFoundException("sandbox copy does not exist — cannot back it up before apply.", sandboxPath);

            string backupPath = sandboxPath + ".prev.bak";
            File.Copy(sandboxPath, backupPath, overwrite: true);
            return new SinSandboxBackup(sandboxPath, backupPath, SinSandboxRestoreVerifier.Sha256(sandboxPath));
        }

        /// <summary>Restore the sandbox copy from its .prev.bak backup (overwrites the sandbox file).</summary>
        public void Restore()
        {
            if (!File.Exists(BackupPath))
                throw new FileNotFoundException("backup .prev.bak missing — cannot restore the sandbox.", BackupPath);
            File.Copy(BackupPath, SandboxPath, overwrite: true);
        }

        /// <summary>True if the on-disk backup still matches the captured pre-apply hash (backup integrity check).</summary>
        public bool BackupMatchesPreImage() => SinSandboxRestoreVerifier.Sha256(BackupPath) == PreImageSha256;
    }
}
