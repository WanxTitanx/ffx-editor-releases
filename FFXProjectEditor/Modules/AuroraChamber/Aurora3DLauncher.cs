using FFXProjectEditor.FfxLib.Battle;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using FFXProjectEditor.Modules.Common.ViewerHub;

namespace FFXProjectEditor.Modules.AuroraChamber
{
    internal readonly record struct AuroraBattleOverlayResult(
        bool Success,
        string Message,
        string? RequestPath = null,
        string? StagedPath = null,
        StudioWebServer.ExactMemoryLease? MemoryLease = null,
        AuroraMemoryPreviewSession? MemorySession = null);

    /// <summary>
    /// Owns at most one Aurora memory lease or Windows exact-file overlay for an embedded viewer.
    /// MAINT: Replace/clear releases only this owner; family cleanup invalidates memory Current.
    /// </summary>
    internal sealed class AuroraBattleOverlayOwner : IDisposable
    {
        private readonly string _viewerDataRoot;
        private StudioWebServer? _server;

        internal AuroraBattleOverlayOwner(string viewerDataRoot) =>
            _viewerDataRoot = viewerDataRoot;

        private AuroraBattleOverlayResult? _current;
        internal AuroraBattleOverlayResult? Current
        {
            get
            {
                if (_current is AuroraBattleOverlayResult old &&
                    old.MemoryLease is { IsActive: false })
                {
                    Aurora3DLauncher.TryReleaseBattleOverlay(_viewerDataRoot, _server, old);
                    _current = null;
                    _server = null;
                }
                return _current;
            }
            private set => _current = value;
        }

        internal string Update(
            bool enabled,
            StudioWebServer server,
            Func<AuroraBattleOverlayResult> stage,
            string disabledMessage)
        {
            if (!enabled)
            {
                Clear();
                return disabledMessage;
            }

            if (OperatingSystem.IsLinux() && !server.IsRunning)
            {
                Clear();
                return Strings.U_Au_MemoryPreviewUnavailable;
            }

            AuroraBattleOverlayResult next;
            try { next = stage(); }
            catch (Exception error) when (OperatingSystem.IsLinux() &&
                error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                Clear();
                FFXProjectEditor.Diagnostics.DebugLog.Error(
                    "Aurora.RealGame", "Native preview acquisition failed; previous owner released.", error);
                return Strings.U_Au_MemoryPreviewUnavailable;
            }
            if (!next.Success)
            {
                Clear();
                return next.Message;
            }

            if (!Aurora3DLauncher.TryRegisterBattleOverlay(server, next))
            {
                Aurora3DLauncher.TryReleaseBattleOverlay(_viewerDataRoot, server, next);
                Clear();
                return OperatingSystem.IsLinux()
                    ? Strings.U_Au_MemoryPreviewUnavailable
                    : string.Format(Strings.U_Au_Hub3DStartFailed, "exact overlay registration failed");
            }

            AuroraBattleOverlayResult? previous = Current;
            StudioWebServer? previousServer = _server;
            Current = next;
            _server = server;

            if (previous is AuroraBattleOverlayResult old)
                Aurora3DLauncher.TryReleaseBattleOverlay(_viewerDataRoot, previousServer, old);
            return next.Message;
        }

        internal void Clear()
        {
            if (Current is AuroraBattleOverlayResult current)
                Aurora3DLauncher.TryReleaseBattleOverlay(_viewerDataRoot, _server, current);
            Current = null;
            _server = null;
        }

        public void Dispose() => Clear();
    }

    /// <summary>
    /// 🐉 AURORA × NOCLIP — 3D battle preview launcher (melhor dos dois mundos).
    ///
    /// O noclip.website renderiza os MESMOS arquivos que o Aurora edita (prova: 0e/&lt;id&gt;.bin é
    /// byte-idêntico ao btl_*.bin — o `BIN.parseEncounter` do noclip usa exatamente o layout do nosso
    /// Battle_File: script@0x4, monster ids i16@+0xC, posições float32 16B). Este launcher:
    ///   1. resolves battleId through the selected NoClip encounter table; content matching is a fallback;
    ///   2. maps immutable native memory or stages the current btl_*.bin under the per-user
    ///      viewer-data overlay and maps only the exact /data/FinalFantasyX/0e/&lt;encId&gt;.bin
    ///      request — the selected extraction stays read-only;
    ///   3. abre `index.html?battle=&lt;encId&gt;#ffx/b&lt;mapIndex&gt;` — o param ?battle= é o patch
    ///      aditivo no scenes.ts do noclip (selectEncounter) que força aquele encounter exato.
    /// Read-only sobre o jogo (o override é sobre a CÓPIA do noclip data/); nenhum byte do corpus muda.
    /// </summary>
    internal static partial class Aurora3DLauncher
    {
        public const string BackupSuffix = ".aurora3d.bak";

        private static readonly object IndexCacheGate = new();
        private static EncounterIndexBridge? _indexCache;
        private static string? _indexBtlRoot;
        private static string? _indexNoclipRoot;
        private static DateTime _indexTableWriteTime;
        private static DateTime _indexEncounterDirectoryWriteTime;

        /// <summary>Top-level noclip root (must contain data/FinalFantasyX). DELEGATES to the single source of truth,
        /// <see cref="FFXProjectEditor.Modules.Common.ViewerHub.NoclipLocator"/> — it reads the persisted
        /// <c>%LOCALAPPDATA%\FFXProjectEditor\noclip.root</c> config or the explicit environment override.
        /// Root cause 2026-08-16:
        /// the RealGame override failed with "noclip data/ não encontrado (NOCLIP_ROOT?)" because THIS method returned
        /// null on the nested root while the ViewerHub (via NoclipLocator) served fine — the 0e/ override never applied.</summary>
        public static string? FindNoclipRoot()
            => FFXProjectEditor.Modules.Common.ViewerHub.NoclipLocator.Find();

        public static string? ResolveEncounter0eDir()
        {
            string? root = FindNoclipRoot();
            if (root == null) return null;
            string dir = Path.Combine(root, "data", "FinalFantasyX", "0e");
            return Directory.Exists(dir) ? dir : null;
        }

        /// <summary>Portable btl corpus root (vanilla 858 battle bins) for the SHA bridge. Resolves from the
        /// USER's ffx_ps2 extraction via <see cref="FFXProjectEditor.Services.Project_Service.Path_FfxPs2Root"/>
        /// (auto-detected / project-derived / manual override) → <c>jppc/battle/btl</c>; falls back to the loaded
        /// project's <c>jppc/battle/btl</c>. Never force a hardcoded dev path:
        /// the whole point is that any user reaches their own extraction/noclip, not this machine's Downloads.</summary>
        public static string? ResolveBtlCorpusRoot()
        {
            Project_Service? svc = Project_Service.Instance;
            string? ffxPs2 = svc?.Path_FfxPs2Root;
            if (!string.IsNullOrWhiteSpace(ffxPs2) && Directory.Exists(ffxPs2))
            {
                string candidate = Path.Combine(ffxPs2, "jppc", "battle", "btl");
                if (Directory.Exists(candidate)) return candidate;
            }
            try
            {
                if (svc != null && Directory.Exists(svc.Path_Btl)) return svc.Path_Btl; // project's own corpus
            }
            catch { /* project not loaded → capability remains unavailable */ }
            string resolved = EncounterIndexBridge.DefaultBtlRoot;
            return string.IsNullOrWhiteSpace(resolved) ? null : resolved;
        }

        private static EncounterIndexBridge Index()
        {
            // 🐛 FIX (2026-08-16): o bridge SHA mapeia battleId→encId pelo CONTEÚDO do bin. Se o corpus viesse do
            // PROJETO, qualquer batalha EDITADA mudava o SHA e quebrava o TryResolve → RealGame "sem correspondência"
            // ou encontro errado (raiz real relatada em batalha editada com 5 monstros). O mapeamento battleId→encId só
            // é ESTÁVEL se derivado da extração vanilla de 858 (mesma fonte que o noclip 0e/). O ApplyBattleOverride
            // continua lendo os BYTES ATUAIS do projeto (GetPathBattle) — a edição aparece no override.
            return GetOrBuildIndex(
                ResolveBtlCorpusRoot() ?? string.Empty,
                ResolveEncounter0eDir() ?? string.Empty);
        }

        internal static EncounterIndexBridge GetOrBuildIndex(string btlRoot, string noclip0eRoot)
        {
            string canonicalBtl = CanonicalCacheRoot(btlRoot);
            string canonicalNoclip = CanonicalCacheRoot(noclip0eRoot);
            StringComparison comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            lock (IndexCacheGate)
            {
                string tablePath = Path.Combine(Path.GetDirectoryName(canonicalNoclip) ?? string.Empty, "0d", "0000.bin");
                DateTime tableWriteTime = File.GetLastWriteTimeUtc(tablePath);
                DateTime directoryWriteTime = Directory.GetLastWriteTimeUtc(canonicalNoclip.Length > 0 ? canonicalNoclip : ".");
                if (_indexCache == null ||
                    !string.Equals(_indexBtlRoot, canonicalBtl, comparison) ||
                    !string.Equals(_indexNoclipRoot, canonicalNoclip, comparison) ||
                    _indexTableWriteTime != tableWriteTime ||
                    _indexEncounterDirectoryWriteTime != directoryWriteTime)
                {
                    _indexCache = EncounterIndexBridge.Build(canonicalBtl, canonicalNoclip);
                    _indexBtlRoot = canonicalBtl;
                    _indexNoclipRoot = canonicalNoclip;
                    _indexTableWriteTime = tableWriteTime;
                    _indexEncounterDirectoryWriteTime = directoryWriteTime;
                }

                return _indexCache;
            }
        }

        private static string CanonicalCacheRoot(string root) =>
            string.IsNullOrWhiteSpace(root) ? string.Empty : Path.GetFullPath(root);


        /// <summary>Re-apply the override for a battle (resolve + copy current bytes). Returns a status string.</summary>
        public static string RefreshOverride(string battleId)
        {
            if (!Index().TryResolve(battleId, out int encId))
                return string.Format(Strings.U_Au_NoMatch0e, battleId);
            return ApplyBattleOverride(battleId, encId);
        }

        /// <summary>
        /// Registers immutable native memory or a Windows staged file outside the selected extraction.
        /// Native acquisition requires the actual running Studio server and never stages payload files.
        /// </summary>
        public static string ApplyBattleOverride(string battleId, int encounterId)
        {
            if (OperatingSystem.IsLinux())
                return ApplyNativeBattleOverride(battleId, encounterId, ViewerHubService.Server);
            AuroraBattleOverlayResult result = CreateBattleOverride(battleId, encounterId);
            if (!result.Success)
                return result.Message;

            if (ViewerHubService.Server is not { IsRunning: true } server ||
                !TryRegisterBattleOverlay(server, result))
            {
                TryReleaseBattleOverlay(ViewerHubService.ViewerDataRoot, server: null, result);
                return string.Format(Strings.U_Au_Hub3DStartFailed, ViewerHubService.StatusText);
            }
            return result.Message;
        }

        internal static AuroraBattleOverlayResult CreateBattleOverride(
            string battleId, int encounterId, StudioWebServer? server = null)
        {
            if (OperatingSystem.IsLinux())
                return CreateNativeBattleOverride(battleId, encounterId, server);
            string? encDir = ResolveEncounter0eDir();
            if (encDir == null) return new AuroraBattleOverlayResult(false, Strings.U_Au_NoclipRootMissing);
            string battlePath = Project_Service.Instance.GetPathBattle(battleId);
            if (!File.Exists(battlePath))
                return new AuroraBattleOverlayResult(
                    false,
                    string.Format(Strings.U_Au_BtlNotFound, battlePath));

            return StageBattleOverride(
                battlePath,
                encounterId,
                encDir,
                ViewerHubService.ViewerDataRoot);
        }

        internal static AuroraBattleOverlayResult StageBattleOverride(
            string battlePath,
            int encounterId,
            string selectedEncounterDirectory,
            string viewerDataRoot,
            StudioWebServer? server = null) =>
            StageBattleOverride(
                battlePath,
                encounterId,
                selectedEncounterDirectory,
                viewerDataRoot,
                NoclipOverlayStore.ProcessSessionId,
                server);

        internal static AuroraBattleOverlayResult StageBattleOverride(
            string battlePath,
            int encounterId,
            string selectedEncounterDirectory,
            string viewerDataRoot,
            string overlaySessionId,
            StudioWebServer? server = null)
        {
            if (OperatingSystem.IsLinux())
            {
                if (!string.Equals(overlaySessionId, NoclipOverlayStore.ProcessSessionId,
                        StringComparison.Ordinal))
                    return NativeBattleUnavailable("A native session capability is required.");
                return NativeProcessSession.Stage(
                    battlePath, encounterId, selectedEncounterDirectory, server);
            }
            try
            {
                string selectedDirectory = Path.GetFullPath(selectedEncounterDirectory);
                string selectedTarget = Path.Combine(selectedDirectory, $"{encounterId:X4}.bin");
                if (!File.Exists(selectedTarget))
                    selectedTarget = Directory.EnumerateFiles(selectedDirectory)
                        .FirstOrDefault(p => string.Equals(Path.GetFileName(p), $"{encounterId:x4}.bin", StringComparison.OrdinalIgnoreCase))
                        ?? selectedTarget;
                if (!File.Exists(selectedTarget))
                {
                    return new AuroraBattleOverlayResult(
                        false,
                        string.Format(Strings.U_Au_0eMissing, encounterId));
                }
                if (FileSystemReparseGuard.ContainsReparsePointInExistingChain(selectedTarget))
                    throw new IOException("The selected NoClip encounter path contains a reparse point.");

                byte[] currentBattle = File.ReadAllBytes(battlePath);
                string stagedPath = NoclipOverlayStore.StageBattle(
                    viewerDataRoot,
                    encounterId,
                    currentBattle,
                    overlaySessionId);
                // NoClip's loader emits hexadecimal IDs via toString(16), so its request path is
                // lowercase. Exact overlay routes stay case-sensitive by design.
                string requestPath = $"/data/FinalFantasyX/0e/{encounterId:x4}.bin";
                FFXProjectEditor.Diagnostics.DebugLog.Info(
                    "Aurora.RealGame",
                    $"Staged exact overlay {requestPath} at {stagedPath}; selected 0e remained unchanged.");
                return new AuroraBattleOverlayResult(
                    true,
                    string.Format(
                        Strings.U_Au_StagedBattlePreview,
                        encounterId,
                        Path.GetFileNameWithoutExtension(battlePath),
                        Path.GetFileName(stagedPath)),
                    requestPath,
                    stagedPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                ArgumentException or NotSupportedException or PathTooLongException)
            {
                FFXProjectEditor.Diagnostics.DebugLog.Error(
                    "Aurora.RealGame",
                    $"Failed to stage 0e/{encounterId:X4}.bin: {ex.Message}",
                    ex);
                return new AuroraBattleOverlayResult(
                    false,
                    string.Format(Strings.U_Au_BtlNotFound, battlePath));
            }
        }

        internal static bool TryRegisterBattleOverlay(
            StudioWebServer server, AuroraBattleOverlayResult overlay)
        {
            if (!overlay.Success || overlay.RequestPath == null || !server.IsRunning)
                return false;
            if (overlay.MemoryLease is { } lease)
                return overlay.StagedPath == null &&
                    string.Equals(overlay.RequestPath, lease.RequestPath, StringComparison.Ordinal) &&
                    lease.IsActive && lease.IsOwnedBy(server) &&
                    overlay.MemorySession?.Owns(lease) == true;
            return overlay.MemorySession == null && overlay.StagedPath != null &&
                server.TryMapExactFile(overlay.RequestPath, overlay.StagedPath);
        }

        internal static bool TryReleaseBattleOverlay(
            string viewerDataRoot,
            StudioWebServer? server,
            AuroraBattleOverlayResult overlay) =>
            TryReleaseBattleOverlay(
                viewerDataRoot,
                server,
                overlay,
                NoclipOverlayStore.ProcessSessionId);

        internal static bool TryReleaseBattleOverlay(
            string viewerDataRoot,
            StudioWebServer? server,
            AuroraBattleOverlayResult overlay,
            string overlaySessionId)
        {
            if (overlay.MemoryLease is { } lease)
            {
                overlay.MemorySession?.Forget(lease);
                lease.Dispose();
                return true;
            }
            if (overlay.MemorySession != null)
                return false;
            if (string.IsNullOrWhiteSpace(overlay.RequestPath) ||
                string.IsNullOrWhiteSpace(overlay.StagedPath))
                return false;

            try
            {
                string root = Path.GetFullPath(
                    NoclipOverlayStore.ResolveBattleSessionRoot(viewerDataRoot, overlaySessionId));
                string staged = Path.GetFullPath(overlay.StagedPath);
                StringComparison comparison = OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                if (!string.Equals(Path.GetDirectoryName(staged), root, comparison) ||
                    !IsCanonicalStagedBattleName(Path.GetFileNameWithoutExtension(staged)))
                    return false;

                server?.TryUnmapExactFile(overlay.RequestPath, staged);
                return NoclipOverlayStore.TryDeleteOwnedBattle(
                    viewerDataRoot,
                    overlaySessionId,
                    staged);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                ArgumentException or NotSupportedException or PathTooLongException)
            {
                FFXProjectEditor.Diagnostics.DebugLog.Error(
                    "Aurora.RealGame",
                    $"Failed to release owned battle overlay {overlay.StagedPath}: {ex.Message}",
                    ex);
                return false;
            }
        }

        /// <summary>Clears the native process family or Windows staged previews; selected 0e stays read-only.
        /// Other exact owners may still serve after this family's leases are released.</summary>
        public static int RestoreAllOverrides(out string detail)
        {
            if (OperatingSystem.IsLinux())
                return RestoreAllOverrides(NativeProcessSession, out detail);
            return ClearStagedOverrides(
                ViewerHubService.ViewerDataRoot,
                ViewerHubService.Server as StudioWebServer,
                out detail);
        }

        internal static int ClearStagedOverrides(string viewerDataRoot, out string detail)
            => ClearStagedOverrides(viewerDataRoot, server: null, out detail);

        internal static int ClearStagedOverrides(
            string viewerDataRoot,
            StudioWebServer? server,
            out string detail) =>
            ClearStagedOverrides(
                viewerDataRoot,
                server,
                NoclipOverlayStore.ProcessSessionId,
                out detail);

        internal static int ClearStagedOverrides(
            string viewerDataRoot,
            StudioWebServer? server,
            string overlaySessionId,
            out string detail)
        {
            if (OperatingSystem.IsLinux())
            {
                if (!string.Equals(overlaySessionId, NoclipOverlayStore.ProcessSessionId,
                        StringComparison.Ordinal))
                {
                    detail = Strings.U_Au_MemoryPreviewUnavailable;
                    return 0;
                }
                return ClearStagedOverrides(NativeProcessSession, out detail);
            }
            int restored = 0;
            var log = new System.Collections.Generic.List<string>();
            foreach (string staged in NoclipOverlayStore.SnapshotOwnedBattleFiles(overlaySessionId))
            {
                try
                {
                    string stem = Path.GetFileNameWithoutExtension(staged);
                    if (!IsCanonicalStagedBattleName(stem))
                        continue;
                    string requestPath = $"/data/FinalFantasyX/0e/{stem[..4].ToLowerInvariant()}.bin";
                    if (TryReleaseBattleOverlay(
                            viewerDataRoot,
                            server,
                            new AuroraBattleOverlayResult(true, string.Empty, requestPath, staged),
                            overlaySessionId))
                    {
                        restored++;
                        log.Add(Path.GetFileName(staged));
                    }
                }
                catch (Exception ex) { log.Add($"{Path.GetFileName(staged)}: {ex.Message}"); }
            }
            detail = string.Join(", ", log);
            return restored;
        }

        private static bool IsCanonicalStagedBattleName(string stem)
        {
            ReadOnlySpan<char> id = stem.AsSpan();
            if (stem.Length == 37 && stem[4] == '.' &&
                Guid.TryParseExact(stem.AsSpan(5), "N", out _))
                id = stem.AsSpan(0, 4);

            return id.Length == 4 && ushort.TryParse(
                id,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out _);
        }


        /// <summary>Resolve the 1a/ battle-map index for a battle via the project's btl.bin (EncounterTable:
        /// group battlefield u16 — bit 0x400 = battle on the field map, map = battlefield & 0xFF).
        /// 🐛 FIX (2026-08-16, Jarvis-Aurora): antes usava <c>table.Groups[0]</c> — o battlefield da PRIMEIRA
        /// formação da tabela do mapa. Se o battle selecionado pertencia a outro grupo/formação, montava o
        /// mapa 1a/ errado → o noclip <c>selectEncounter</c> não achava o encId na battleList daquele mapa →
        /// caía na seleção por peso → RealGame abria encontro ALEATÓRIO (sintoma relatado). Agora resolve o
        /// grupo que REALMENTE contém este battleId (Formations.Any) e usa o battlefield dele; fallback
        /// Groups[0] só se nenhum grupo casar (com log).</summary>
        public static int? ResolveBattleMapIndex(string battleId)
        {
            try
            {
                Project_Service svc = Project_Service.Instance;
                if (svc?.ProjectPath == null) return null;
                string btlBin = Path.Combine(svc.ProjectPath, "jppc", "battle", "kernel", "btl.bin");
                if (!File.Exists(btlBin)) return null;
                EncounterTable_File enc = EncounterTable_File.Read(File.ReadAllBytes(btlBin));
                string map = battleId.Length >= 6 ? battleId[..6] : battleId;
                EncounterTable_Entry? table = enc.Tables.FirstOrDefault(t =>
                    t.Map.Equals(map, StringComparison.OrdinalIgnoreCase));
                if (table == null || table.Groups.Count == 0) return null;

                EncounterTable_Group? group = table.Groups.FirstOrDefault(g =>
                    g.Formations.Any(f => f.BattleId.Equals(battleId, StringComparison.OrdinalIgnoreCase)));

                if (group == null) return null;

                int battlefield = group.Battlefield;
                // bit 0x400 = battle no campo (overworld); o índice real do mapa usa só os 8 bits baixos.
                return battlefield & 0xFF;
            }
            catch { return null; }
        }

        /// <summary>Full flow: resolve encounter → override 0e/ → ensure server → open browser deep-link.</summary>
        public static string OpenBattle3D(string battleId)
        {
            string? url = BuildBattleUrl(battleId, out string status);
            if (url == null) return status;
            if (!Modules.Common.ViewerShell.ExternalBrowserLauncher.Open(url))
                return string.Format(Strings.U_Au_BrowserOpenFailed, "no browser found");
            return $"🐉 {status}";
        }

        /// <summary>Resolve um battleId para o encounter 0e/ (bridge SHA) — usado pelos tools do ViewerShell.</summary>
        public static bool TryResolveEncounter(string battleId, out int encounterId)
        {
            try { return Index().TryResolve(battleId, out encounterId); }
            catch { encounterId = -1; return false; }
        }

        /// <summary>Lista dos battleIds resolvidos (bridge SHA) — para o seletor de battles do ViewerShell (F3).</summary>
        public static IReadOnlyList<string> ListResolvedBattles()
        {
            try
            {
                return Index().BattleIdToEncounter.Keys.Concat(Index().BattleIdToTableEncounter.Keys)
                    .Concat(Index().BattleIdToNearestEncounter.Keys).Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(k => k, StringComparer.Ordinal)
                    .ToList();
            }
            catch { return Array.Empty<string>(); }
        }

        /// <summary>Resolve encounter → override 0e/ → ensure server → return the RealGame deep-link URL
        /// (no browser). The embedded WebView2 navigates to it. Returns null + status when it cannot open.</summary>
        public static string? BuildBattleUrl(string battleId, out string status)
        {
            string? url = BuildBattleUrlWithoutOverride(battleId, out status, out int encId);
            if (url == null)
                return null;

            string overrideMsg = ApplyBattleOverride(battleId, encId);
            status = $"{overrideMsg} · {status}";
            return url;
        }

        /// <summary>
        /// Builds the pinned RealGame URL without acquiring an overlay. Embedded hosts use this
        /// boundary with <see cref="AuroraBattleOverlayOwner"/> so close/switch/disable can release
        /// the exact route and its LocalAppData staging file. <paramref name="edit"/> arms the
        /// noclip edit gizmo (?edit=1) — the EditViewer battle-stage path uses the same rendered
        /// scene with pick/drag/S/R instead of the read-only preview.
        /// </summary>
        internal static string? BuildBattleUrlWithoutOverride(
            string battleId,
            out string status,
            out int encId,
            bool edit = false)
        {
            status = string.Empty;
            encId = -1;
            if (!Index().TryResolve(battleId, out encId))
            {
                status = string.Format(Strings.U_Au_NoMatchRealGame, battleId);
                return null;
            }

            // 🐛 FIX (2026-08-18, Jarvis-Aurora): battles SEM 0e/ byte-idêntico agora resolvem pelo fallback de
            // SIMILARIDADE (EncounterIndexBridge.BattleIdToNearestEncounter). A mensagem abaixo é honesta — o
            // encontro é o MESMO do noclip, mas a extração dele difere em alguns bytes (SHA não casa exato).
            // (release/v2.245.1.0 gateava preview público a SHA exato — na branch principal o encounter
            // table + fallback de similaridade seguem ativos, é o que faz o cenário real abrir.)
            string via = Index().BattleIdToTableEncounter.ContainsKey(battleId)
                ? Strings.U_Au_EncounterTableMatch
                : Index().IsSimilarityResolved(battleId) ? "match aproximado (similaridade de blocos 256B)" : "match exato (SHA)";
            int? mapIndex = ResolveBattleMapIndex(battleId);
            if (mapIndex == null)
            {
                status = string.Format(Strings.U_Au_BattleMapUnavailable, battleId);
                return null;
            }
            // 🐉 RealGame via o ViewerHubService (StudioWebServer in-process, serve-dir do noclip).
            // O extraQuery NÃO pode começar com '?' — o BuildUrl já adiciona o '?' (senão vira "??battle="
            // e o URLSearchParams do noclip não lê o battle → só o campo, sem a batalha).
            FFXProjectEditor.Diagnostics.DebugLog.Info("Aurora.RealGame", $"battleId={battleId} encId={encId} map={mapIndex} via={via} edit={edit}");
            string query = $"battle={encId}&map={mapIndex}&actors=1";
            if (edit) query += "&edit=1";
            // forExternalBrowser: no Linux quem consome a URL loopback é o navegador do sistema
            // (AuroraChamber_Control -> ExternalBrowserLauncher.Open); o embed WebView2 não existe lá.
            string? url = FFXProjectEditor.Modules.Common.ViewerHub.ViewerHubService.BuildUrl(
                "aurora", query, forExternalBrowser: true);
            if (url == null)
            {
                status = string.Format(Strings.U_Au_Hub3DStartFailed, FFXProjectEditor.Modules.Common.ViewerHub.ViewerHubService.StatusText);
                FFXProjectEditor.Diagnostics.DebugLog.Error("Aurora.RealGame", $"hub falhou: {status}");
                return null;
            }
            FFXProjectEditor.Diagnostics.DebugLog.Info("Aurora.RealGame", $"URL final: {url}");
            status = $"{via} · mapa 1a/{mapIndex:X3} · {FFXProjectEditor.Modules.Common.ViewerHub.ViewerHubService.StatusText}";
            return url;
        }

    }
}
