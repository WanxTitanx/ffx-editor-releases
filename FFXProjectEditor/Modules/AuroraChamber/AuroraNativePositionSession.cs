using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using FFXProjectEditor.FfxLib.BattleMap;
using FFXProjectEditor.FfxLib.Battle;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.AuroraChamber;

/// <summary>A preview is bound to one project file and revision. Only X/Z in the primary
/// live-monster and primary party arrays may change; visual height must never become file Y.</summary>
internal sealed class AuroraNativePositionSession
{
    internal sealed record Position(int Slot, float X, float Z);
    internal sealed record Request(string BattleId, string Revision, Position[] Positions, Position[]? PartyPositions = null);
    internal sealed record Reply(bool Ok, string Message, string? Revision = null, bool PreviewReady = true);
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
        var arena = BattleArenaAnchors_File.ReadFromBattleBin(BattleId, bytes);
        var anchors = arena.PrimaryMonsterAnchors.Take(PreviewMonsterCount(bytes, arena.PrimaryMonsterAnchors.Count)).ToArray();
        var party = arena.Areas.FirstOrDefault()?[BattleArena_AnchorRole.PartyFront]?.Anchors
            .Take(3).ToArray() ?? Array.Empty<BattleArena_Anchor>();
        var shot = BattleAtelCameraPoseReader.Read(BattleId, bytes);
        object? camera = shot == null ? null : new
        {
            eye = shot.Eye, target = shot.Target, source = "atel",
            worker = shot.Worker, entrypoint = shot.Entrypoint, command = shot.PositionCommand,
            screenDepth = shot.ScreenDepth, roll = shot.Roll
        };
        return JsonSerializer.Serialize(new
        {
            battleId = BattleId,
            revision = _revision,
            actors = anchors.Select((a, i) => new
            {
                slot = i, x = a.X, z = a.Z,
                label = i < _labels.Length ? _labels[i] : string.Format(Strings.U_Noclip_MonsterSlot, i + 1)
            }),
            party = party.Select((a, i) => new { slot = i, x = a.X, y = a.Y, z = a.Z,
                label = string.Format(Strings.U_Noclip_PartySlot, i + 1) }),
            camera,
            text = new
            {
                title = Strings.U_Noclip_BattleComposition,
                hint = Strings.U_Noclip_CompositionHint,
                moveMode = Strings.U_Noclip_MoveActor,
                cameraMode = Strings.U_Noclip_CameraMode,
                battleView = Strings.U_Noclip_BattleView,
                cameraFile = Strings.U_Noclip_CameraFile,
                cameraZoom = Strings.U_Noclip_CameraZoom,
                cameraFormation = Strings.U_Noclip_CameraFormation,
                showParty = Strings.U_Noclip_ShowParty,
                party = Strings.U_Noclip_Party,
                partyLoading = Strings.U_Noclip_PartyLoading,
                partyUnavailable = Strings.U_Noclip_PartyUnavailable,
                partyMissing = Strings.U_Noclip_PartyMissing,
                actor = Strings.U_Noclip_Actor,
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
                    positions.Length != PreviewMonsterCount(original, span.Value.Count) || positions.Any(p => p is null) ||
                    positions.Select(p => p.Slot).Distinct().Count() != positions.Length ||
                    positions.Any(p => p.Slot < 0 || p.Slot >= positions.Length ||
                        !float.IsFinite(p.X) || !float.IsFinite(p.Z) || Math.Abs(p.X) > 1_000_000 || Math.Abs(p.Z) > 1_000_000))
                    return new(false, Strings.U_Noclip_InvalidPositions);

                var partyPositions = request.PartyPositions;
                var partySpan = partyPositions == null ? null : BattleArenaPositionWriter.LocateAnchorArray(
                    original, 0, BattleArena_AnchorRole.PartyFront);
                if (partyPositions != null && (partySpan == null ||
                    partyPositions.Length != Math.Min(3, partySpan.Value.Count) ||
                    partyPositions.Length is < 1 or > 3 || partyPositions.Any(p => p is null) ||
                    partyPositions.Select(p => p.Slot).Distinct().Count() != partyPositions.Length ||
                    partyPositions.Any(p => p.Slot < 0 || p.Slot >= partyPositions.Length ||
                        !float.IsFinite(p.X) || !float.IsFinite(p.Z) || Math.Abs(p.X) > 1_000_000 || Math.Abs(p.Z) > 1_000_000) ||
                    partySpan.Value.Offset < span.Value.Offset + span.Value.Length &&
                    span.Value.Offset < partySpan.Value.Offset + partySpan.Value.Length))
                    return new(false, Strings.U_Noclip_InvalidPositions);

                var ordered = positions.OrderBy(p => p.Slot).Select(p => (p.X, p.Z)).ToArray();
                byte[] updated = BattleArenaPositionWriter.WritePositions(original, 0, BattleArena_AnchorRole.MonsterLive, ordered)
                    ?? throw new InvalidOperationException(Strings.U_Noclip_InvalidPositions);
                if (partyPositions != null)
                    updated = BattleArenaPositionWriter.WritePositions(updated, 0, BattleArena_AnchorRole.PartyFront,
                        partyPositions.OrderBy(p => p.Slot).Select(p => (p.X, p.Z)).ToArray())
                        ?? throw new InvalidOperationException(Strings.U_Noclip_InvalidPositions);

                // Two arrays may be edited atomically. Admit only X/Z bytes of the submitted
                // slots, including when the encounter has more than three party anchors.
                var allowed = new HashSet<int>();
                void AllowXZ(int offset, int count)
                {
                    for (int slot = 0; slot < count; slot++)
                    for (int b = 0; b < 4; b++)
                    {
                        allowed.Add(offset + slot * 16 + b);
                        allowed.Add(offset + slot * 16 + 8 + b);
                    }
                }
                AllowXZ(span.Value.Offset, positions.Length);
                if (partySpan != null) AllowXZ(partySpan.Value.Offset, partyPositions!.Length);
                if (original.Length != updated.Length || original.Where((b, i) => b != updated[i] && !allowed.Contains(i)).Any())
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
                return new(true, string.Format(Strings.U_Noclip_SavedFormation,
                    positions.Length + (partyPositions?.Length ?? 0)), _revision);
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

    // Removing a formation member preserves its reserved anchor. NoClip creates one
    // actor per live formation entry; only those leading positions belong to this preview.
    private int PreviewMonsterCount(byte[] bytes, int anchorCount) => Math.Min(anchorCount,
        Battle_File.Read(BattleId, bytes).Formation?.Slots.Count(slot => !slot.IsEmpty) ?? 0);
}
