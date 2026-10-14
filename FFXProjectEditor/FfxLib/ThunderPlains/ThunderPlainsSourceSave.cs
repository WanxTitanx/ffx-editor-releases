using FFXProjectEditor.FfxLib.Archives;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace FFXProjectEditor.FfxLib.ThunderPlains;

internal sealed record ThunderPlainsSaveTarget(string Path, byte[] Before, byte[] After, bool AllowCreate);

/// <summary>Stage both EBP outputs before publishing either; VBF fallback never writes to the archive.</summary>
internal static class ThunderPlainsSourceSave
{
    internal static void SavePair(ThunderPlainsSaveTarget north, ThunderPlainsSaveTarget south)
    {
        var staged = new List<(ThunderPlainsSaveTarget Target, string Temp, string? ExistingHash)>();
        var committed = new List<(ThunderPlainsSaveTarget Target, bool Existed, string? Backup)>();
        try
        {
            foreach (var item in new[] { north, south })
            {
                VbfSafeFileSystem.RejectLinks(item.Path);
                if (Directory.Exists(item.Path)) throw new IOException("An EBP destination is a directory: " + item.Path);
                bool exists = File.Exists(item.Path);
                if (!exists && !item.AllowCreate) throw new IOException("An EBP source was removed. Refresh before saving.");
                string? hash = exists ? VbfArchiveWriter.HashFile(item.Path, default) : null;
                if (hash != null && hash != Convert.ToHexString(SHA256.HashData(item.Before)))
                    throw new IOException("An EBP source changed. Refresh before saving.");
                VbfSafeFileSystem.EnsureSpace(item.Path, item.Before.Length + 2L * item.After.Length);
                Directory.CreateDirectory(Path.GetDirectoryName(item.Path)!);
                string temp = item.Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                staged.Add((item, temp, hash));
                using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { output.Write(item.After); output.Flush(true); }
                if (!File.ReadAllBytes(temp).AsSpan().SequenceEqual(item.After))
                    throw new IOException("Staged EBP readback failed.");
            }
            foreach (var item in staged)
            {
                string permanent = item.Target.Path + ".bak";
                VbfSafeFileSystem.RejectLinks(permanent);
                if (!File.Exists(permanent))
                {
                    using var backup = new FileStream(permanent, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    backup.Write(item.Target.Before); backup.Flush(true);
                }
                string? backupPath = VbfArchiveWriter.Promote(item.Temp, item.Target.Path, item.ExistingHash, default);
                committed.Add((item.Target, item.ExistingHash != null, backupPath));
            }
        }
        catch (Exception failure)
        {
            List<Exception> errors = new() { failure };
            foreach (var item in committed)
            {
                try
                {
                    // A concurrent edit belongs to its writer; do not overwrite it during rollback.
                    if (VbfArchiveWriter.HashFile(item.Target.Path, default) != Convert.ToHexString(SHA256.HashData(item.Target.After)))
                        throw new IOException("EBP rollback stopped: destination changed. Restore from " + item.Backup);
                    if (!item.Existed) File.Delete(item.Target.Path);
                    else
                    {
                        string restore = item.Target.Path + "." + Guid.NewGuid().ToString("N") + ".restore.tmp";
                        try { File.Copy(item.Backup!, restore, false); File.Move(restore, item.Target.Path, true); }
                        finally { if (File.Exists(restore)) File.Delete(restore); }
                    }
                }
                catch (Exception rollback) { errors.Add(rollback); }
            }
            if (errors.Count > 1) throw new AggregateException("EBP save failed; recovery requires the preserved backups.", errors);
            throw;
        }
        finally { foreach (var item in staged) if (File.Exists(item.Temp)) File.Delete(item.Temp); }
    }
}
