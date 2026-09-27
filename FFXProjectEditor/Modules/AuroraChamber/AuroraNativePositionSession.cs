using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using FFXProjectEditor.FfxLib.BattleMap;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.AuroraChamber;

/// <summary>A preview is bound to one project file and revision. Only X/Z in the primary
/// live-monster array may change; NoClip's visual flight height must never become file Y.</summary>
internal sealed class AuroraNativePositionSession
{
    internal sealed record Position(int Slot, float X, float Z);
    internal sealed record Request(string BattleId, string Revision, Position[] Positions);
    internal sealed record Reply(bool Ok, string Message, string? Revision = null);
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _path;
    private readonly string[] _labels;
    private readonly object _gate = new();
    private string _revision;
    internal string BattleId { get; }

    internal AuroraNativePositionSession(string battleId, string path, IEnumerable<string>? labels = null)
    {
        BattleId = battleId;
        _path = Path.GetFullPath(path);
        _labels = labels?.ToArray() ?? Array.Empty<string>();
        _revision = Revision(File.ReadAllBytes(_path));
    }

    internal bool Matches(string battleId, string path) =>
        string.Equals(BattleId, battleId, StringComparison.Ordinal) &&
        string.Equals(_path, Path.GetFullPath(path), OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    internal string Describe()
    {
        byte[] bytes = File.ReadAllBytes(_path);
        if (Revision(bytes) != _revision) throw new InvalidOperationException(Strings.U_Noclip_Conflict);
        var anchors = BattleArenaAnchors_File.ReadFromBattleBin(BattleId, bytes).PrimaryMonsterAnchors;
        return JsonSerializer.Serialize(new
        {
            battleId = BattleId,
            revision = _revision,
            actors = anchors.Select((a, i) => new
            {
                slot = i, x = a.X, z = a.Z,
                label = i < _labels.Length ? _labels[i] : string.Format(Strings.U_Noclip_MonsterSlot, i + 1)
            }),
            text = new
            {
                title = Strings.U_Noclip_PositionEditor,
                hint = Strings.U_Noclip_PositionHint,
                moveMode = Strings.U_Noclip_MoveMode,
                cameraMode = Strings.U_Noclip_CameraMode,
                dragView = Strings.U_Noclip_DragView,
                monster = Strings.CommonMonster,
                save = Strings.U_Noclip_SaveProject,
                reset = Strings.U_Noclip_ResetPosition,
                saving = Strings.U_Noclip_Saving,
                error = Strings.U_Noclip_SaveError,
                changed = Strings.U_Noclip_Unsaved,
                ready = Strings.U_Noclip_Ready,
                advanced = Strings.U_Noclip_AdvancedHint
            }
        }, JsonOptions);
    }

    internal Reply Save(Request request)
    {
        lock (_gate)
        {
            string? temporary = null;
            try
            {
                if (request.BattleId != BattleId || request.Revision != _revision)
                    return new(false, Strings.U_Noclip_Conflict);
                if (FileSystemReparseGuard.ContainsReparsePointInExistingChain(_path))
                    return new(false, Strings.U_Noclip_SaveError);
                byte[] original = File.ReadAllBytes(_path);
                if (Revision(original) != _revision)
                    return new(false, Strings.U_Noclip_Conflict);
                var span = BattleArenaPositionWriter.LocateAnchorArray(original, 0, BattleArena_AnchorRole.MonsterLive);
                var positions = request.Positions;
                if (span == null || positions == null || positions.Length is < 1 or > 8 ||
                    positions.Length != span.Value.Count || positions.Any(p => p is null) ||
                    positions.Select(p => p.Slot).Distinct().Count() != positions.Length ||
                    positions.Any(p => p.Slot < 0 || p.Slot >= positions.Length ||
                        !float.IsFinite(p.X) || !float.IsFinite(p.Z) || Math.Abs(p.X) > 1_000_000 || Math.Abs(p.Z) > 1_000_000))
                    return new(false, Strings.U_Noclip_InvalidPositions);

                var ordered = positions.OrderBy(p => p.Slot).Select(p => (p.X, p.Z)).ToArray();
                byte[] updated = BattleArenaPositionWriter.WritePositions(original, 0, BattleArena_AnchorRole.MonsterLive, ordered)
                    ?? throw new InvalidOperationException(Strings.U_Noclip_InvalidPositions);
                if (!BattleArenaPositionWriter.IsPositionOnly(original, updated, span.Value.Offset, span.Value.Length))
                    return new(false, Strings.U_Noclip_InvalidPositions);
                if (original.AsSpan().SequenceEqual(updated))
                    return new(true, Strings.U_Noclip_NoChanges, _revision);

                string backup = _path + BattleArenaPositionWriter.DefaultBackupSuffix;
                if (FileSystemReparseGuard.ContainsReparsePointInExistingChain(backup))
                    return new(false, Strings.U_Noclip_SaveError);
                if (!File.Exists(backup)) File.WriteAllBytes(backup, original);
                temporary = _path + ".aurora-position-" + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllBytes(temporary, updated);
                if (Revision(File.ReadAllBytes(_path)) != _revision)
                    return new(false, Strings.U_Noclip_Conflict);
                File.Move(temporary, _path, overwrite: true);
                _revision = Revision(updated);
                return new(true, string.Format(Strings.U_Noclip_Saved, positions.Length), _revision);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            {
                FFXProjectEditor.Diagnostics.DebugLog.Error("aurora-chamber.NativeSave", "Position save failed", ex);
                return new(false, Strings.U_Noclip_SaveError);
            }
            finally
            {
                if (temporary != null && File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }

    internal static string Revision(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
