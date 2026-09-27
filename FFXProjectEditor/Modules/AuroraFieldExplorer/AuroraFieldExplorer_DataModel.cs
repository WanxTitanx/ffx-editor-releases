using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.BattleMap;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FFXProjectEditor.Modules.AuroraFieldExplorer
{
    internal partial class AuroraFieldExplorer_DataModel : ObservableObject
    {
    readonly List<FieldMapRow> _allFields = new();
    BattleMapCatalog_File? _btlmapCatalog;
    AuroraFieldExplorer_WalkManifest.WalkBundle? _walkManifest;

        public ObservableCollection<FieldMapListRow> Fields { get; } = new();
        public ObservableCollection<FieldEncounterGroupRow> EncounterGroups { get; } = new();
        public ObservableCollection<FieldEncounterFormationRow> SelectedGroupFormations { get; } = new();

        [ObservableProperty] private string filterText = string.Empty;
        [ObservableProperty] private FieldMapListRow? selectedField;
        [ObservableProperty] private FieldEncounterGroupRow? selectedEncounterGroup;

        [ObservableProperty]
        private string honestyBanner = Strings.U_Au_OverworldModeBanner;

        [ObservableProperty] private string catalogSummary = Strings.U_Au_LoadingFieldCatalog;
        [ObservableProperty] private string fieldHeader = Strings.F2_no_field_selected_f60e8a8d;
        [ObservableProperty] private string fieldDetail = Strings.F2_choose_an_overworld_map_from_the_list_362f656d;
        [ObservableProperty] private string encounterSummary = "—";
        [ObservableProperty] private string arenaHint = "—";
        [ObservableProperty] private string renderStatus = "—";
        [ObservableProperty] private string? lastDeepLink;
        [ObservableProperty] private bool isBusy;

        /// <summary>True off-Windows: no WebView2 embed — the panel shows the external-browser
        /// fallback card and navigation opens the same loopback URL in the system browser.</summary>
        public bool IsExternalViewer =>
            !Modules.Common.ViewerShell.ExternalBrowserLauncher.IsEmbeddedViewerSupported;

        /// <summary>Embedded WebView2 navigates to the OS-assigned loopback URL returned by ViewerHub.</summary>
        public event Action<string?>? ViewerNavigateRequested;

        partial void OnFilterTextChanged(string value) => ApplyFilter();
        partial void OnSelectedFieldChanged(FieldMapListRow? value) => ApplySelectedField(value);
        partial void OnSelectedEncounterGroupChanged(FieldEncounterGroupRow? value) => RefreshSelectedGroupFormations(value);

        public AuroraFieldExplorer_DataModel()
        {
            RefreshCatalog();
        }

        public void RefreshCatalog()
        {
            _allFields.Clear();
            IReadOnlyList<FieldMapRow> loaded = AuroraFieldExplorer_FieldCatalog.LoadAll(out string? error);
            if (error != null)
            {
                CatalogSummary = error;
                ApplyFilter();
                return;
            }

            _allFields.AddRange(loaded);
            TryLoadBtlmapCatalog();
            _walkManifest = AuroraFieldExplorer_WalkManifest.TryLoad();
            int mounted = _allFields.Count(f => AuroraFieldExplorer_FieldRenderer.IsFieldMounted(f));
            int walked = _walkManifest?.FieldCount ?? 0;
            CatalogSummary =
                string.Format(Strings.U_Au_FieldCatalogSummary, _allFields.Count, mounted)
                + (walked > 0 ? $" · {walked} walked (Field Scout)" : "")
                + " · export on-demand via PhyreMapExportLab.";
            ApplyFilter();
        }

        void TryLoadBtlmapCatalog()
        {
            try
            {
                string root = BattleMapCatalog_File.DefaultBtlmapRoot;
                string? ps3 = Project_Service.Instance.Path_Ps3DataRoot;
                if (!string.IsNullOrWhiteSpace(ps3))
                {
                    string candidate = Path.Combine(ps3, "btlmap");
                    if (Directory.Exists(candidate))
                        root = candidate;
                }

                if (Directory.Exists(root))
                    _btlmapCatalog = BattleMapCatalog_File.Scan(root);
            }
            catch
            {
                _btlmapCatalog = null;
            }
        }

        void ApplyFilter()
        {
            string? keepArea = SelectedField?.Field.Area;
            string? keepToken = SelectedField?.Field.FieldToken;

            string q = FilterText.Trim();
            IEnumerable<FieldMapRow> src = _allFields;
            if (q.Length > 0)
            {
                src = src.Where(f =>
                    f.FieldToken.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || f.Area.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || f.MapEntity.Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            Fields.Clear();
            foreach (FieldMapRow f in src)
            {
                Fields.Add(new FieldMapListRow(f)
                {
                    IsMounted = AuroraFieldExplorer_FieldRenderer.IsFieldMounted(f),
                    IsWalked = AuroraFieldExplorer_WalkManifest.IsFieldWalked(_walkManifest, f),
                });
            }

            if (!string.IsNullOrEmpty(keepArea) && !string.IsNullOrEmpty(keepToken))
            {
                SelectedField = Fields.FirstOrDefault(r =>
                    r.Field.Area.Equals(keepArea, StringComparison.OrdinalIgnoreCase) &&
                    r.Field.FieldToken.Equals(keepToken, StringComparison.OrdinalIgnoreCase));
            }
        }

        void ApplySelectedField(FieldMapListRow? row)
        {
            EncounterGroups.Clear();
            SelectedEncounterGroup = null;
            SelectedGroupFormations.Clear();
            LastDeepLink = null;
            RenderStatus = "—";

            if (row == null)
            {
                FieldHeader = Strings.F2_no_field_selected_f60e8a8d;
                FieldDetail = Strings.F2_choose_an_overworld_map_from_the_list_362f656d;
                EncounterSummary = "—";
                ArenaHint = "—";
                return;
            }

            FieldMapRow field = row.Field;
            FieldHeader = field.FieldToken;
            FieldDetail = $"{field.MapEntity} · area {field.Area} · {(row.IsMounted ? Strings.U_Au_FieldDetailMounted : Strings.U_Au_FieldDetailPending)}";

            FieldEncounterIndexResult enc = AuroraFieldExplorer_EncounterIndex.Build(field, _btlmapCatalog);
            if (enc.LoadError != null)
            {
                EncounterSummary = enc.LoadError;
                ArenaHint = "—";
                return;
            }

            foreach (FieldEncounterGroupRow g in enc.ExactGroups)
                EncounterGroups.Add(g);
            foreach (FieldEncounterGroupRow g in enc.AreaRelatedGroups)
                EncounterGroups.Add(g);

            int battles = enc.ExactGroups.Concat(enc.AreaRelatedGroups)
                .SelectMany(g => g.Formations)
                .Select(f => f.BattleId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            EncounterSummary =
                enc.ExactGroups.Count > 0
                    ? string.Format(Strings.U_Au_EncSummaryExact, enc.ExactGroups.Count, battles)
                    : enc.AreaRelatedGroups.Count > 0
                        ? string.Format(Strings.U_Au_EncSummaryArea, field.FieldToken, enc.AreaRelatedGroups.Count)
                        : string.Format(Strings.U_Au_EncSummaryNone, field.FieldToken);

            ArenaHint = enc.SuggestedArenaScene != null
                ? string.Format(Strings.U_Au_ArenaSuggested, enc.SuggestedArenaScene.SceneId)
                : enc.SuggestedArenaScene == null && _btlmapCatalog != null
                    ? Strings.U_Au_ArenaNoHdScene
                    : Strings.U_Au_ArenaScanUnavailable;

            if (row.IsMounted)
                RequestViewerNavigation(field, enc);
        }

        void RequestViewerNavigation(FieldMapRow field, FieldEncounterIndexResult? enc = null)
        {
            enc ??= AuroraFieldExplorer_EncounterIndex.Build(field, _btlmapCatalog);
            string? url = AuroraFieldExplorer_FieldRenderer.TryBuildViewerUrl(field, enc);
            if (url == null)
                return;

            LastDeepLink = url;
            ViewerNavigateRequested?.Invoke(url + "&t=" + DateTime.UtcNow.Ticks);
        }

        void RefreshSelectedGroupFormations(FieldEncounterGroupRow? group)
        {
            SelectedGroupFormations.Clear();
            if (group == null)
                return;
            foreach (FieldEncounterFormationRow f in group.Formations)
                SelectedGroupFormations.Add(f);
        }

        public async Task PublishScoutAsync()
        {
            if (IsBusy)
                return;

            IsBusy = true;
            RenderStatus = Strings.U_Au_PublishingScout;
            try
            {
                AuroraFieldExplorer_WalkPublishService.OperationResult result =
                    await AuroraFieldExplorer_WalkPublishService.PublishFromLabAsync().ConfigureAwait(true);

                RenderStatus = result.Message;
                if (result.Ok)
                    AfterWalkManifestChanged(reloadViewer: true);
            }
            catch (Exception ex)
            {
                RenderStatus = ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task RefreshWalkMarkersAsync()
        {
            if (IsBusy)
                return;

            IsBusy = true;
            RenderStatus = Strings.U_Au_UpdatingScoutMarkers;
            try
            {
                AuroraFieldExplorer_WalkPublishService.OperationResult result =
                    await AuroraFieldExplorer_WalkPublishService.RefreshOverlaysAsync(_btlmapCatalog).ConfigureAwait(true);

                RenderStatus = result.Message;
                if (result.Ok)
                    AfterWalkManifestChanged(reloadViewer: true);
            }
            catch (Exception ex)
            {
                RenderStatus = ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task PublishAndOpenSelectedAsync()
        {
            if (SelectedField == null)
            {
                RenderStatus = Strings.F2_select_a_field_before_publishing_opening_37a5a880;
                return;
            }

            if (IsBusy)
                return;

            IsBusy = true;
            RenderStatus = Strings.U_Au_PublishingAndOpeningField;
            try
            {
                AuroraFieldExplorer_WalkPublishService.OperationResult pub =
                    await AuroraFieldExplorer_WalkPublishService.PublishFromLabAsync().ConfigureAwait(true);

                if (!pub.Ok)
                {
                    RenderStatus = pub.Message;
                    return;
                }

                string keepArea = SelectedField.Field.Area;
                string keepToken = SelectedField.Field.FieldToken;

                AfterWalkManifestChanged(reloadViewer: false);

                if (SelectedField == null)
                {
                    FilterText = keepToken;
                    ApplyFilter();
                    SelectedField = Fields.FirstOrDefault(r =>
                        r.Field.Area.Equals(keepArea, StringComparison.OrdinalIgnoreCase) &&
                        r.Field.FieldToken.Equals(keepToken, StringComparison.OrdinalIgnoreCase));
                }

                if (SelectedField == null)
                {
                    RenderStatus = pub.Message + Strings.U_Au_FieldLostAfterRefresh;
                    return;
                }

                FieldMapRow field = SelectedField.Field;
                FieldEncounterIndexResult enc = AuroraFieldExplorer_EncounterIndex.Build(field, _btlmapCatalog);
                AuroraFieldExplorer_FieldRenderer.RenderResult result = await Task.Run(() =>
                    AuroraFieldExplorer_FieldRenderer.RenderField(field, enc, forceReexport: false)).ConfigureAwait(true);

                RenderStatus = pub.Message + " · " + result.Message;
                LastDeepLink = result.DeepLinkUrl;
                if (result.Ok && result.DeepLinkUrl != null)
                {
                    RenderStatus += " · viewer embedded";
                    ViewerNavigateRequested?.Invoke(result.DeepLinkUrl + "&t=" + DateTime.UtcNow.Ticks);
                    SelectedField.IsMounted = true;
                    SelectedField.IsWalked = AuroraFieldExplorer_WalkManifest.IsFieldWalked(_walkManifest, field);
                    ApplyFilter();
                }
            }
            catch (Exception ex)
            {
                RenderStatus = ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        void AfterWalkManifestChanged(bool reloadViewer)
        {
            RefreshCatalog();
            if (SelectedField == null)
                return;

            FieldMapRow field = SelectedField.Field;
            SelectedField.IsWalked = AuroraFieldExplorer_WalkManifest.IsFieldWalked(_walkManifest, field);

            if (!reloadViewer)
                return;

            if (SelectedField.IsMounted)
            {
                FieldEncounterIndexResult enc = AuroraFieldExplorer_EncounterIndex.Build(field, _btlmapCatalog);
                RequestViewerNavigation(field, enc);
            }
        }

        public async Task RenderSelectedAsync(bool forceReexport = false)
        {
            if (SelectedField == null)
            {
                RenderStatus = Strings.U_Au_SelectAField;
                return;
            }

            IsBusy = true;
            RenderStatus = Strings.U_Au_ExportingFieldWithEncounters;
            try
            {
                FieldMapRow field = SelectedField.Field;
                FieldEncounterIndexResult enc = AuroraFieldExplorer_EncounterIndex.Build(field, _btlmapCatalog);
                AuroraFieldExplorer_FieldRenderer.RenderResult result = await Task.Run(() =>
                    AuroraFieldExplorer_FieldRenderer.RenderField(field, enc, forceReexport)).ConfigureAwait(true);

                RenderStatus = result.Message;
                LastDeepLink = result.DeepLinkUrl;
                if (result.Ok && result.DeepLinkUrl != null)
                {
                    RenderStatus += " · viewer embedded";
                    ViewerNavigateRequested?.Invoke(result.DeepLinkUrl + "&t=" + DateTime.UtcNow.Ticks);
                    SelectedField.IsMounted = true;
                    ApplyFilter();
                }
            }
            catch (Exception ex)
            {
                RenderStatus = ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void OpenInBrowser()
        {
            if (string.IsNullOrWhiteSpace(LastDeepLink))
            {
                RenderStatus = Strings.U_Au_RenderFieldFirst;
                return;
            }

            RenderStatus = AuroraFieldExplorer_FieldRenderer.Launch(LastDeepLink);
        }

        public void OpenOutputFolder()
        {
            if (SelectedField == null)
                return;
            string? dir = AuroraFieldExplorer_FieldRenderer.GetOutputDir(SelectedField.Field);
            if (dir == null || !Directory.Exists(dir))
            {
                RenderStatus = Strings.F2_export_folder_does_not_exist_yet_render__10ef3dd5;
                return;
            }

            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }

        public void OpenMapViewerFolder()
        {
            string? index = Modules.AuroraChamber.AuroraSceneRenderer.ResolveMapViewerIndex();
            if (index == null)
                return;
            string dir = Path.GetDirectoryName(index)!;
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }

        public string? GetSelectedMapKeyForArena() => SelectedField?.Field.FieldToken;

        public bool SelectFieldByMapEntity(string mapEntity)
        {
            if (string.IsNullOrWhiteSpace(mapEntity))
                return false;

            FieldMapRow? target = _allFields.FirstOrDefault(f =>
                f.MapEntity.Equals(mapEntity, StringComparison.OrdinalIgnoreCase)
                || f.FieldToken.Equals(mapEntity, StringComparison.OrdinalIgnoreCase));

            if (target == null)
                return false;

            FilterText = target.FieldToken;
            ApplyFilter();
            SelectedField = Fields.FirstOrDefault(r =>
                r.Field.FieldToken.Equals(target.FieldToken, StringComparison.OrdinalIgnoreCase));
            return SelectedField != null;
        }

        private static string? FindExe(string name)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("where", name)
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                var proc = System.Diagnostics.Process.Start(psi);
                if (proc != null)
                {
                    var path = proc.StandardOutput.ReadLine();
                    proc.WaitForExit(5000);
                    if (proc.ExitCode == 0 && !string.IsNullOrEmpty(path) && File.Exists(path))
                        return path;
                }
            }
            catch { }
            return null;
        }

        public async Task ValidateVisualAsync()
        {
            if (IsBusy)
                return;

            IsBusy = true;
            RenderStatus = Strings.U_Au_ValidatingVisual;
            try
            {
                string repo = AppDomain.CurrentDomain.BaseDirectory;
                for (int i = 0; i < 5; i++)
                    repo = Path.GetDirectoryName(repo) ?? repo;
                if (!Directory.Exists(Path.Combine(repo, "RuntimeTools")))
                    repo = Path.GetFullPath(Path.Combine(repo, ".."));

                string? nodeExe = FindExe("node");
                string? pythonExe = FindExe("python") ?? FindExe("python3");
                string renderGate = Path.Combine(repo, "RuntimeTools", "FieldPackFactory", "render-gate.mjs");
                string validator = Path.Combine(repo, "RuntimeTools", "FieldPackFactory", "validate-visual.py");

                if (!File.Exists(renderGate))
                    throw new Exception(string.Format(Strings.U_Au_RenderGateNotFound, renderGate));
                if (nodeExe == null || !File.Exists(nodeExe))
                    throw new Exception(string.Format(Strings.U_Au_NodeJsNotFound, "PATH"));
                if (pythonExe == null || !File.Exists(pythonExe))
                    throw new Exception(string.Format(Strings.U_Au_PythonNotFound, "PATH"));

                RenderStatus = Strings.U_Au_CapturingScreenshots;
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = nodeExe,
                    Arguments = $"\"{renderGate}\" --all",
                    WorkingDirectory = repo,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                var proc = System.Diagnostics.Process.Start(psi);
                if (proc != null)
                {
                    string stdout = await proc.StandardOutput.ReadToEndAsync();
                    string stderr = await proc.StandardError.ReadToEndAsync();
                    proc.WaitForExit(120000);
                    if (proc.ExitCode != 0)
                        throw new Exception(string.Format(Strings.U_Au_RenderGateFailed, proc.ExitCode, stderr));
                }

                RenderStatus = Strings.U_Au_ValidatingSSIM;
                psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = $"\"{validator}\" --auto-golden",
                    WorkingDirectory = repo,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                proc = System.Diagnostics.Process.Start(psi);
                if (proc != null)
                {
                    string stdout = await proc.StandardOutput.ReadToEndAsync();
                    string stderr = await proc.StandardError.ReadToEndAsync();
                    proc.WaitForExit(60000);
                    RenderStatus = (stdout + stderr).Replace("\n", " · ");
                }

                RenderStatus = Strings.U_Au_VisualValidationComplete;
            }
            catch (Exception ex)
            {
                RenderStatus = string.Format(Strings.U_Au_VisualValidationFailed, ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }
    }

    internal sealed partial class FieldMapListRow : ObservableObject
    {
        public FieldMapListRow(FieldMapRow field) => Field = field;

        public FieldMapRow Field { get; }
        public string DisplayName => Field.DisplayName;
        public string Summary => Field.Summary;
        public string DetailSummary => Field.DetailSummary;

        [ObservableProperty] private bool isMounted;
        [ObservableProperty] private bool isWalked;

        public string MountBadge => IsMounted ? Strings.U_Au_MountBadgeMounted : Strings.U_Au_MountBadgePending;
        public string WalkBadge => IsWalked ? Strings.U_Au_WalkBadgeWalked : "";
        public string DetailWithMount => DetailSummary + MountBadge + WalkBadge;
    }
}
