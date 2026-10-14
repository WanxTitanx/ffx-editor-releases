// WHY: Native Aurora previews serve immutable snapshots; selected and battle files stay read-only.
// MAINT: Observe the size before allocation. This transient is outside server logical budgets.
// Retained read capabilities are not app-owned mutation authority or immutable filesystem locks.
using System;
using System.IO;
using System.Security.Cryptography;
using FFXProjectEditor.Diagnostics;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Services;
using G = FFXProjectEditor.Modules.Common.ViewerHub.FileSystemReparseGuard;
using L = FFXProjectEditor.Modules.Common.ViewerHub.LinuxReadFileSystem;

namespace FFXProjectEditor.Modules.AuroraChamber;

internal static partial class Aurora3DLauncher
{
    private const int MaxNativeBattleBytes = 64 * 1024 * 1024;
    private static readonly AuroraMemoryPreviewSession NativeProcessSession = new();

    internal static AuroraBattleOverlayResult StageBattleOverride(
        string battlePath, int encounterId, string selectedEncounterDirectory,
        string viewerDataRoot, AuroraMemoryPreviewSession session, StudioWebServer? server)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!OperatingSystem.IsLinux())
            return NativeBattleUnavailable("The native session overload is Linux-only.");
        return session.Stage(battlePath, encounterId, selectedEncounterDirectory, server);
    }

    internal static int ClearStagedOverrides(AuroraMemoryPreviewSession session, out string detail)
    {
        ArgumentNullException.ThrowIfNull(session);
        int count = session.Clear(out detail);
        DebugLog.Info("Aurora.RealGame", $"Released {count} native Aurora preview registrations.");
        return count;
    }

    internal static int RestoreAllOverrides(AuroraMemoryPreviewSession session, out string detail) =>
        ClearStagedOverrides(session, out detail);

    private static AuroraBattleOverlayResult CreateNativeBattleOverride(
        string battleId, int encounterId, StudioWebServer? server)
    {
        if (server is not { IsRunning: true } || string.IsNullOrWhiteSpace(battleId))
            return NativeBattleUnavailable("A running preview server and battle selection are required.");
        try
        {
            Project_Service? project = Project_Service.Instance;
            if (project == null || string.IsNullOrWhiteSpace(project.ProjectPath))
                return NativeBattleUnavailable("No project is loaded for the battle preview.");
            string? selected = ResolveEncounter0eDir();
            if (selected == null)
                return new(false, Strings.U_Au_NoclipRootMissing);
            return StageBattleOverride(project.GetPathBattle(battleId), encounterId,
                selected, ViewerHubService.ViewerDataRoot, server);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException)
        {
            DebugLog.Error("Aurora.RealGame", "Native battle selection was refused.", error);
            return new(false, Strings.U_Au_MemoryPreviewUnavailable);
        }
    }

    private static string ApplyNativeBattleOverride(
        string battleId, int encounterId, StudioWebServer? server)
    {
        AuroraBattleOverlayResult result = CreateNativeBattleOverride(battleId, encounterId, server);
        if (!result.Success) return result.Message;
        if (server == null || !TryRegisterBattleOverlay(server, result))
        {
            TryReleaseBattleOverlay(ViewerHubService.ViewerDataRoot, server, result);
            return Strings.U_Au_MemoryPreviewUnavailable;
        }
        return result.Message;
    }

    internal static AuroraBattleOverlayResult StageNativeBattleCore(
        string battlePath, int encounterId, string selectedDirectory, StudioWebServer? server)
    {
        if (!L.IsSupported || server is not { IsRunning: true } ||
            encounterId is < 0 or > ushort.MaxValue)
            return NativeBattleUnavailable("Native battle preview admission was refused.");

        byte[]? snapshot = null;
        StudioWebServer.ExactMemoryLease? lease = null;
        bool handedOff = false;
        try
        {
            L.ValidateAbsolutePath(battlePath);
            string parentPath = Path.GetDirectoryName(battlePath)!;
            string leaf = Path.GetFileName(battlePath);
            string selectedRoot = L.NormalizeAbsoluteRoot(selectedDirectory);
            string route = FormattableString.Invariant(
                $"/data/FinalFantasyX/0e/{encounterId:x4}.bin");

            if (!G.TryOpenVerifiedDirectory(parentPath, out var parent) || parent == null)
                return NativeBattleUnavailable("The battle source parent was refused.");
            using (parent)
            {
                var parentBefore = L.Observe(parent.Handle, L.DirectoryType);
                var opened = G.TryOpenVerifiedRead(parent, leaf, out var source);
                using (source)
                {
                    if (opened != G.VerifiedOpenResult.Success || source == null)
                        return NativeBattleUnavailable("The battle source read capability was refused.");
                    var before = L.Observe(source.Stream.SafeFileHandle, L.RegularFileType);
                    if (before.Length <= 0 || before.Length > MaxNativeBattleBytes)
                        return NativeBattleUnavailable("Battle preview length is outside the supported bound.");

                    snapshot = new byte[checked((int)before.Length)];
                    source.Stream.ReadExactly(snapshot);
                    if (source.Stream.ReadByte() != -1)
                        return NativeBattleUnavailable("The battle source grew during the snapshot.");

                    string candidate = Path.Combine(selectedRoot,
                        FormattableString.Invariant($"{encounterId:x4}.bin"));
                    var selectedState = NoclipHexDataRead.TryOpen(
                        selectedRoot, candidate, route, out var selected);
                    using (selected)
                    {
                        if (selectedState == G.VerifiedOpenResult.NotFound)
                            return new(false, string.Format(Strings.U_Au_0eMissing, encounterId));
                        if (selectedState != G.VerifiedOpenResult.Success || selected == null)
                            return NativeBattleUnavailable("The selected encounter capability was refused.");
                    }

                    // Revalidate both the held source and its observed name after the bounded read.
                    var namedState = G.TryOpenVerifiedRead(parent, leaf, out var named);
                    using (named)
                    using (var namedParent = L.OpenRoot(parentPath))
                    {
                        if (namedState != G.VerifiedOpenResult.Success || named == null ||
                            L.Observe(named.Stream.SafeFileHandle, L.RegularFileType) != before ||
                            L.Observe(source.Stream.SafeFileHandle, L.RegularFileType) != before ||
                            L.Observe(parent.Handle, L.DirectoryType) != parentBefore ||
                            L.Observe(namedParent, L.DirectoryType) != parentBefore)
                            return NativeBattleUnavailable("Battle source identity or observations changed.");
                    }

                    string hash = Convert.ToHexString(SHA256.HashData(snapshot));
                    if (!server.IsRunning ||
                        !server.TryMapExactBytes(route, snapshot, out lease) || lease == null)
                        return NativeBattleUnavailable("Exact-memory admission was refused.");
                    if (!server.IsRunning)
                        return NativeBattleUnavailable("The preview server stopped before handoff.");
                    var result = new AuroraBattleOverlayResult(
                        true, string.Format(Strings.U_Au_MemoryPreviewApplied, encounterId),
                        route, null, lease);
                    DebugLog.Info("Aurora.RealGame",
                        $"Registered native Aurora preview ({snapshot.Length} bytes, sha256={hash}).");
                    handedOff = true;
                    return result;
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException or OverflowException)
        {
            DebugLog.Error("Aurora.RealGame", "Native battle preview was refused.", error);
            return new(false, Strings.U_Au_MemoryPreviewUnavailable);
        }
        finally
        {
            if (!handedOff) lease?.Dispose();
            if (snapshot != null) Array.Clear(snapshot, 0, snapshot.Length);
        }
    }

    private static AuroraBattleOverlayResult NativeBattleUnavailable(string diagnostic)
    {
        DebugLog.Warn("Aurora.RealGame", diagnostic);
        return new(false, Strings.U_Au_MemoryPreviewUnavailable);
    }
}
