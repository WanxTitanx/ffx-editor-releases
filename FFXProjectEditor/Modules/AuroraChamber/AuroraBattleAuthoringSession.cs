using System;
using System.IO;
using FFXProjectEditor.FfxLib.Battle;
using FFXProjectEditor.Modules.Common.ViewerHub;

namespace FFXProjectEditor.Modules.AuroraChamber;

/// <summary>One encounter revision; backups are immutable and saves replace a complete validated file.</summary>
internal sealed class AuroraBattleAuthoringSession
{
    private readonly string _battleId;
    private readonly string _path;
    private readonly object _gate = new();
    private string _revision;
    internal string? LastBackup { get; private set; }

    internal AuroraBattleAuthoringSession(string battleId, string path, byte[] source)
    {
        _battleId = battleId; _path = Path.GetFullPath(path); _revision = BattleOpeningWriter.Revision(source);
    }

    internal bool Save(string battleId, string path, byte[] candidate)
    {
        lock (_gate)
        {
            if (_battleId != battleId || !string.Equals(_path, Path.GetFullPath(path), OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidOperationException("The authoring draft belongs to another encounter or project.");
            if (FileSystemReparseGuard.ContainsReparsePointInExistingChain(_path))
                throw new IOException("The encounter path contains a symbolic link or reparse point.");
            byte[] current = File.ReadAllBytes(_path);
            if (BattleOpeningWriter.Revision(current) != _revision)
                throw new InvalidOperationException("The encounter changed on disk. Reload it before saving.");
            if (current.AsSpan().SequenceEqual(candidate)) return false;
            string backup = _path + ".aurora-" + _revision + ".bak";
            if (FileSystemReparseGuard.ContainsReparsePointInExistingChain(backup))
                throw new IOException("The backup path contains a symbolic link or reparse point.");
            if (File.Exists(backup))
            {
                if (BattleOpeningWriter.Revision(File.ReadAllBytes(backup)) != _revision)
                    throw new IOException("An existing backup does not match this encounter revision.");
            }
            else WriteNew(backup, current);
            string temporary = _path + ".aurora-authoring-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                WriteNew(temporary, candidate);
                if (BattleOpeningWriter.Revision(File.ReadAllBytes(_path)) != _revision)
                    throw new InvalidOperationException("The encounter changed while the edit was being prepared.");
                File.Move(temporary, _path, true);
                if (!File.ReadAllBytes(_path).AsSpan().SequenceEqual(candidate))
                    throw new IOException("Saved encounter failed file readback.");
                _revision = BattleOpeningWriter.Revision(candidate); LastBackup = backup;
                return true;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    private static void WriteNew(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes); stream.Flush(true);
    }
}
