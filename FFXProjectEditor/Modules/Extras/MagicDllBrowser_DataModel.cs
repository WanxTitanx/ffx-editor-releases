using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Ps3;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.Extras
{
    internal partial class MagicDllBrowser_DataModel : ObservableObject
    {
        static string DefaultMagicFilesRoot => PortablePathResolver.MagicFilesRoot("FFX") ?? string.Empty;

        readonly List<MagicDllEntry> allDlls = [];

        public ObservableCollection<MagicDllEntry> Dlls { get; } = new();
        public ObservableCollection<MagicDllSection> Sections { get; } = new();
        public ObservableCollection<MagicDllExport> Exports { get; } = new();
        public ObservableCollection<MagicDllImportRow> Imports { get; } = new();
        public ObservableCollection<MagicDllAsciiString> Strings { get; } = new();
        public ObservableCollection<MagicDllOverlaySlot> OverlaySlots { get; } = new();
        public ObservableCollection<MagicDllSlotSemanticCandidate> SlotRoleCandidates { get; } = new();
        public ObservableCollection<MagicDllStringFamilyStat> StringFamilies { get; } = new();
        public ObservableCollection<MagicDllHostFieldRole> HostFieldRoles { get; } = new();
        public ObservableCollection<MagicDllValueCandidate> ValueCandidates { get; } = new();
        public ObservableCollection<MagicDllHostReferenceRow> HostFieldReferences { get; } = new();
        public ObservableCollection<MagicDllFamilySpellRow> FamilySpells { get; } = new();
        public ObservableCollection<MagicDllFamilySiblingRow> FamilySiblings { get; } = new();
        public ObservableCollection<MagicDllFamilySiblingRow> FamilyTwinDlls { get; } = new();
        public ObservableCollection<MagicDllLogicalSlotRow> LogicalSlots { get; } = new();
        public ObservableCollection<MagicDllWave4KernelRefRow> Wave4KernelRefs { get; } = new();
        public ObservableCollection<MagicDllSeSepRow> SeSepRecords { get; } = new();
        public ObservableCollection<string> Warnings { get; } = new();
        public ObservableCollection<PppOpcodeRow> PppOpcodes { get; } = new();

        readonly List<MagicDllValueCandidate> allValueCandidates = [];
        MagicDllEffectFamily selectedEffectFamily = MagicDllEffectFamily.Unknown;
        string ps3ExtractFolder = string.Empty;
        string ps3ModsFolder = string.Empty;

        [ObservableProperty]
        private string rootPath = DefaultMagicFilesRoot;

        [ObservableProperty]
        private string searchText = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasSelectedDll))]
        [NotifyPropertyChangedFor(nameof(SelectedDllTitle))]
        [NotifyPropertyChangedFor(nameof(SelectedDllPath))]
        private MagicDllEntry? selectedDll;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(InspectionSummary))]
        [NotifyPropertyChangedFor(nameof(OverlaySummary))]
        private MagicDllInspection? selectedInspection;

        [ObservableProperty]
        private string statusText = "Load magicFiles\\FFX to inspect native magic_#### DLLs.";

        // Jarvis-UI (Sprint F 2026-06-20, OPT-F4): estado do Expander master do shell. Default expandido
        // pra preservar o layout de v2.159.4.0; o usuário pode recolher para recuperar largura. Persiste
        // durante a sessão do módulo (TwoWay no shell). UI-only; sem lógica de negócio.
        [ObservableProperty]
        private bool isMasterExpanded = true;

        [ObservableProperty]
        private string cloneMagicIdText = "0716";

        [ObservableProperty]
        private string patchFileOffsetText = string.Empty;

        [ObservableProperty]
        private string patchRvaText = string.Empty;

        [ObservableProperty]
        private string patchHexBytesText = "90";

        [ObservableProperty]
        private string patchAsciiText = string.Empty;

        [ObservableProperty]
        private string patchAsciiMaxLengthText = string.Empty;

        [ObservableProperty]
        private bool patchAsciiNullTerminate = true;

        [ObservableProperty]
        private string valueCandidateSearchText = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasSelectedValueCandidate))]
        [NotifyPropertyChangedFor(nameof(CanPatchSelectedValueCandidate))]
        private MagicDllValueCandidate? selectedValueCandidate;

        [ObservableProperty]
        private string assistedValueText = string.Empty;

        [ObservableProperty]
        private MagicDllHostFieldRole? selectedHostFieldRole;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasSelectedHostReference))]
        private MagicDllHostReferenceRow? selectedHostReference;

        [ObservableProperty]
        private string assistedHostOffsetText = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasLogicalDecompile))]
        private string logicalDecompileSummary = "Select a DLL to run static logical decompile (host-offset fingerprint + pseudocode per overlay slot).";

        // Jarvis-WD3 (2026-07-07, todo 11): ViewModel for the WD3 Streams tab. Parses the
        // hierarchical stream structure (5 overlapping views of the same sprite blob) and
        // exposes bindable stream items + ApplyRecolorCommand. Replaces the obsolete
        // PrismMagicDllRecolor path (Family A only) for Thundaga (Family D).
        [ObservableProperty]
        private Wd3StreamViewModel wd3Streams = new();

        // Jarvis-PPP (2026-07-07, todo 14): PPP opcode browser — loads 36 opcode handler
        // entries from docs/reverse/magic_dlls/PPP_HANDLER_NAMES.json. Visualization only.
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasPppOpcodes))]
        private string pppOpcodesSummary = "PPP opcode table not loaded.";

        [ObservableProperty]
        private PppOpcodeRow? selectedPppOpcode;

        public string HeaderSummary => "Native Magic DLL LAB: inspect PE structure, extract sections/manifests, re-emit byte-identical DLLs, apply controlled byte/string/ASM patches, and generate a C/ASM rebuild harness. Visual color = PS3 phyre; blue-dominant vec4f on Family C/D is often cast→hit TIMING (RT2 Flan Flood 0718 — patch advanced damage, not opener tint).";
        public string BoundarySummary => "This does not claim automatic recovery of the original C source. It gives a real compiler/repacker path plus native rebuild scaffolding for manually authored C/ASM. Value Workbench flags timing-risk vec4 — do not treat as color without RT2.";
        public string PatchBuilderSummary => "Direct patches write a chosen output DLL only. Role candidates remain evidence labels; changing behavior still means patching bytes/strings/ASM at a real file offset or RVA.";
        public string ValueWorkbenchSummary => "Assisted value editing scans float/vector constants, push immediates, and host-offset references. Offsets with RT2− (ThundaFira/Flan Flood ledger) show rt2-dead / rt2-timing — patch blocked on proved dead sites. Blue vec4 Family C/D without ledger entry = timing-risk until RT2. Visual color = PS3 phyre.";
        public string HostReferenceSummary => "Host fields are offsets in the runtime context copied by InitMagicPRX. Editing here patches one concrete u32 reference inside the DLL, not the original FFX.exe host table.";
        public string FamilyComparatorSummary => "Cross-reference spell rows (moveAnim), overlay signatures and byte-identical DLL twins. Color = PS3 phyre; Family C/D vec4f blue-dominant = possible timing — see RT2 Flan Flood 0718.";
        public string Wave4CatalogSummary => MagicDllWave4CatalogLoader.AvailabilitySummary;

        // Jarvis-MAGIC-DLL (2026-07-05): Pipeline status — reused from CampaignSummary because the
        // previously bound key (F2_f1_headers_...) held the F1-F6 section title, not a live status.
        // This property is not currently bound in the MagicDllBrowser XAML (dead), kept for future use.
        public string PipelineStatusSummary => CampaignSummary;
        public string CampaignSummary => "583/587 (99.3%) DLLs decompiled — 4 corrupt. 1.907.920 lines pseudocode. 4 families classified. 6 clone groups confirmed.";
        public string CampaignDecompiledCount => "583/587";
        public string CampaignDecompiledPct => "99.3%";
        public string CampaignLines => "1.907.920";
        public string CampaignFamilies => "A_ParticleSelfContained · B_RootRecordInterpreter · C_RootSelfGovernedParam · D_EgoTasklist";
        public string CampaignCloneGroups => "6 pares (0082↔0714, 0086↔0715, 0094↔0716, 0095↔0717, 0096↔0718, 0097↔0719)";

        public bool HasTemplateDir => Directory.Exists(Path.Combine(
            Directory.GetCurrentDirectory(), "docs", "reverse", "magic_dlls", "templates"));
        public string SelectedFamilyTemplatePath
        {
            get
            {
                if (SelectedDll?.MagicId is not int _)
                    return string.Empty;
                string family = selectedEffectFamily switch
                {
                    MagicDllEffectFamily.A_ParticleSelfContained => "family_a_template.c",
                    MagicDllEffectFamily.B_RootRecordInterpreter => "family_b_template.c",
                    MagicDllEffectFamily.C_RootSelfGovernedParam => "family_c_template.c",
                    MagicDllEffectFamily.D_EgoTasklist => "family_d_template.c",
                    _ => string.Empty
                };
                if (string.IsNullOrEmpty(family))
                    return string.Empty;
                string path = Path.Combine(
                    Directory.GetCurrentDirectory(), "docs", "reverse", "magic_dlls", "templates", family);
                return File.Exists(path) ? path : string.Empty;
            }
        }
        public string SelectedFamilyTemplateLabel
        {
            get
            {
                string path = SelectedFamilyTemplatePath;
                return string.IsNullOrEmpty(path) ? "No template" : Path.GetFileName(path);
            }
        }

        public string PipelineF1Status => "✅ (3 headers)";
        public string PipelineF2Status => "✅ (4 templates, 3.966 lines)";
        public string PipelineF3Status => "✅ (instantiate.py, 747 lines)";
        public string PipelineF4Status => "✅ (plan: MSVC + .def)";
        public string PipelineF5Status => "⏳ (Ps3MagicTextureWriter exists)";
        public string PipelineF6Status => FFXProjectEditor.Resources.Strings.U_Md_ThisModuleStatus;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasWave4Attribution))]
        [NotifyPropertyChangedFor(nameof(ShowWave4AttributionCard))]
        private string wave4Category = "-";

        [ObservableProperty] private string wave4Family = "-";
        [ObservableProperty] private string wave4RecommendedAction = "-";
        [ObservableProperty] private string wave4OverlaySignature = "-";
        [ObservableProperty] private string wave4EditingGuidance = "-";
        [ObservableProperty] private string wave4Rt2Priority = "-";
        [ObservableProperty] private string wave4CatalogSpells = "-";
        [ObservableProperty] private string wave4KernelRefSummary = "-";
        [ObservableProperty] private string wave4ClusterNote = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasPhyrePackage))]
        private string? firstPhyrePackagePath;

        public bool HasWave4Attribution => !string.Equals(Wave4Category, "-", StringComparison.Ordinal) && !string.Equals(Wave4Category, "unknown", StringComparison.Ordinal);
        public bool ShowWave4AttributionCard => HasWave4Attribution || MagicDllWave4CatalogLoader.LoadedCount > 0;
        public bool HasPhyrePackage => !string.IsNullOrWhiteSpace(FirstPhyrePackagePath) && File.Exists(FirstPhyrePackagePath);
        public bool HasLogicalDecompile => LogicalSlots.Count > 0;
        public bool HasPppOpcodes => PppOpcodes.Count > 0;
        public bool ShowValueWorkbenchFamilyWarning =>
            selectedEffectFamily is MagicDllEffectFamily.C_RootSelfGovernedParam or MagicDllEffectFamily.D_EgoTasklist;
        public string ValueWorkbenchFamilyWarning =>
            "Family C/D: blue-dominant vec4f in .data = possible cast→hit TIMING (RT2 magic_0718 Flan Flood — patch advanced damage; opener blue persisted). NOT a visual tint. Use PS3 Magic phyre recolor. Bulk vec4 still crashes (0082).";
        public bool HasPs3ExtractFolder => Directory.Exists(ps3ExtractFolder);
        public bool HasPs3ModsFolder => Directory.Exists(ps3ModsFolder);
        public string Ps3PathSummary =>
            $"extract: {(HasPs3ExtractFolder ? ps3ExtractFolder : "(missing)")} · mods: {(HasPs3ModsFolder ? ps3ModsFolder : "(missing)")}";
        public string SourceRootSummary => Directory.Exists(RootPath)
            ? $"{RootPath} ({allDlls.Count} magic DLLs)"
            : $"{RootPath} (missing)";
        public bool HasSelectedDll => SelectedDll != null;
        // Empty-state flag for the master list: false after a refresh that yields no magic_*.dll
        // (root missing or search filtering everything out). Notified in ApplyFilter so it tracks
        // both Refresh() and SearchText changes. UI-only; no business logic.
        public bool HasDllList => Dlls.Count > 0;
        // Jarvis-UI (Sprint F 2026-06-20, OPT-F7): severidade semântica derivada do StatusText pra pintar
        // o header (erro de decompile/patch em DangerBrush, avisos em WarningBrush, resto neutro). Derivação
        // por string follow o padrão dos handlers (todos emitem "... failed: ..." ou "... not found ...").
        // Sem novo campo mutável — só leitura do texto que já existe.
        public string StatusSeverity => ClassifyStatusSeverity(StatusText);

        static string ClassifyStatusSeverity(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return "None";
            // Erros explícitos dos handlers (catch ...) e falhas de root/inspect.
            if (status.Contains("failed", StringComparison.OrdinalIgnoreCase)
                || status.Contains("not found", StringComparison.OrdinalIgnoreCase)
                || status.Contains("root not found", StringComparison.OrdinalIgnoreCase))
                return "Danger";
            // Drift de hash (repack não byte-identical) e incompletos.
            if (status.Contains("drifted", StringComparison.OrdinalIgnoreCase)
                || status.Contains("incomplete", StringComparison.OrdinalIgnoreCase)
                || status.Contains("not completed", StringComparison.OrdinalIgnoreCase))
                return "Warning";
            return "Info";
        }
        public bool HasSelectedValueCandidate => SelectedValueCandidate != null;
        public bool CanPatchSelectedValueCandidate =>
            SelectedValueCandidate is { PatchBlocked: false };
        public bool HasSelectedHostReference => SelectedHostReference != null;
        public string SelectedDllTitle => SelectedDll?.FileName ?? "Select a magic DLL";
        public string SelectedDllPath => SelectedDll?.FullPath ?? "-";
        public string InspectionSummary => SelectedInspection == null
            ? "-"
            : BuildInspectionSummary(SelectedInspection);

        static string BuildInspectionSummary(MagicDllInspection inspection)
        {
            MagicDllFamilyClassification classification = MagicDllSemanticAnalyzer.ClassifyFamily(inspection);
            string family = FormatEffectFamily(classification.Family);
            string method = classification.Method switch
            {
                MagicDllFamilyDetectionMethod.SlotKindSignature => $"slot_kind:{classification.PreclassifiedBucket}",
                MagicDllFamilyDetectionMethod.Slot0ByteScan => classification.Slot0DiscriminantOffsets.Count == 0
                    ? "slot0_scan:none"
                    : $"slot0_scan:{string.Join(",", classification.Slot0DiscriminantOffsets.Select(o => $"0x{o:X}"))}",
                MagicDllFamilyDetectionMethod.EgoStringFallback => "ego_strings",
                _ => "unknown"
            };
            return $"{inspection.MachineName} {(inspection.IsPe32Plus ? "PE32+" : "PE32")} · family {family} ({method}) · sections {inspection.Sections.Count} · exports {inspection.Exports.Count} · imports {inspection.Imports.Sum(i => i.Imports.Count)} · strings {inspection.Strings.Count} · SHA256 {inspection.Sha256}";
        }

        static string FormatEffectFamily(MagicDllEffectFamily family) => family switch
        {
            MagicDllEffectFamily.A_ParticleSelfContained => "A",
            MagicDllEffectFamily.B_RootRecordInterpreter => "B",
            MagicDllEffectFamily.C_RootSelfGovernedParam => "C",
            MagicDllEffectFamily.D_EgoTasklist => "D",
            _ => "?"
        };
        public string OverlaySummary => SelectedInspection?.OverlayEvidence == null
            ? "No overlay-table CSV row attached for this DLL."
            : $"GetEffectOverlayTable {SelectedInspection.OverlayEvidence.GetEffectOverlayEa} · InitMagicPRX {SelectedInspection.OverlayEvidence.InitMagicPrxEa} · nonzero slots {SelectedInspection.OverlayEvidence.NonzeroSlotCount}";

        public MagicDllBrowser_DataModel()
        {
            LoadPppOpcodes();
            Refresh();
        }

        partial void OnRootPathChanged(string value)
        {
            OnPropertyChanged(nameof(SourceRootSummary));
        }

        // Jarvis-UI (Sprint F 2026-06-20, OPT-F7): StatusSeverity é computed de StatusText — precisa de
        // notify sempre que o texto muda, senão o header nunca repinta a cor semântica do erro/aviso.
        partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(StatusSeverity));

        partial void OnSearchTextChanged(string value) => ApplyFilter();

        partial void OnValueCandidateSearchTextChanged(string value) => RefreshValueCandidates();

        partial void OnSelectedDllChanged(MagicDllEntry? value)
        {
            InspectSelected();
        }

        partial void OnSelectedValueCandidateChanged(MagicDllValueCandidate? value)
        {
            AssistedValueText = value?.CurrentValueForEdit ?? string.Empty;
        }

        partial void OnSelectedHostFieldRoleChanged(MagicDllHostFieldRole? value)
        {
            AssistedHostOffsetText = value == null ? string.Empty : $"0x{value.Offset:X}";
            RefreshHostFieldReferences();
        }

        partial void OnSelectedHostReferenceChanged(MagicDllHostReferenceRow? value)
        {
            if (SelectedHostFieldRole != null && value != null)
                AssistedHostOffsetText = $"0x{SelectedHostFieldRole.Offset:X}";
        }

        public void Refresh()
        {
            allDlls.Clear();
            if (Directory.Exists(RootPath))
            {
                foreach (string path in Directory.EnumerateFiles(RootPath, "magic_*.dll", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    allDlls.Add(MagicDllEntry.FromPath(path));
                }
                StatusText = $"Loaded {allDlls.Count} DLLs from magicFiles\\FFX.";
            }
            else
            {
                StatusText = "magicFiles\\FFX root not found. Point the field at the game's magicFiles\\FFX folder.";
            }

            OnPropertyChanged(nameof(SourceRootSummary));
            EnrichDllEntriesWithWave4();
            ApplyFilter();
        }

        void EnrichDllEntriesWithWave4()
        {
            foreach (MagicDllEntry entry in allDlls)
            {
                if (entry.MagicId is int magicId && MagicDllWave4CatalogLoader.TryGet(magicId, out MagicDllWave4Attribution row))
                    entry.Wave4CategoryBadge = row.CategoryBadge;
                else
                    entry.Wave4CategoryBadge = null;
            }
        }

        public void SetRoot(string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
                RootPath = path;
            Refresh();
        }

        void ApplyFilter()
        {
            IEnumerable<MagicDllEntry> query = allDlls;
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                query = query.Where(dll =>
                    dll.FileName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                    || dll.MagicIdText.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
            }

            Replace(Dlls, query.ToList());
            OnPropertyChanged(nameof(HasDllList));
            if (SelectedDll == null || !Dlls.Contains(SelectedDll))
                SelectedDll = Dlls.FirstOrDefault();
        }

        void InspectSelected()
        {
            Sections.Clear();
            Exports.Clear();
            Imports.Clear();
            Strings.Clear();
            OverlaySlots.Clear();
            SlotRoleCandidates.Clear();
            StringFamilies.Clear();
            HostFieldRoles.Clear();
            ValueCandidates.Clear();
            HostFieldReferences.Clear();
            FamilySpells.Clear();
            FamilySiblings.Clear();
            FamilyTwinDlls.Clear();
            LogicalSlots.Clear();
            SeSepRecords.Clear();
            Wd3Streams.ClearStreams();
            allValueCandidates.Clear();
            Warnings.Clear();
            logicalDecompileSummary = "Select a DLL to run static logical decompile (host-offset fingerprint + pseudocode per overlay slot).";
            selectedEffectFamily = MagicDllEffectFamily.Unknown;
            ps3ExtractFolder = string.Empty;
            ps3ModsFolder = string.Empty;
            OnPropertyChanged(nameof(ShowValueWorkbenchFamilyWarning));
            OnPropertyChanged(nameof(HasPs3ExtractFolder));
            OnPropertyChanged(nameof(HasPs3ModsFolder));
            OnPropertyChanged(nameof(Ps3PathSummary));
            SelectedValueCandidate = null;
            SelectedHostFieldRole = null;
            SelectedHostReference = null;
            SelectedInspection = null;
            ClearWave4Attribution();

            if (SelectedDll == null)
                return;

            try
            {
                MagicDllInspection inspection = MagicDllDecompiler.Inspect(SelectedDll.FullPath, Directory.GetCurrentDirectory());
                SelectedInspection = inspection;
                Replace(Sections, inspection.Sections);
                Replace(Exports, inspection.Exports.Take(256).ToList());
                Replace(Imports, inspection.Imports.Select(lib => new MagicDllImportRow(lib.Library, lib.Imports.Count, string.Join(", ", lib.Imports.Take(20).Select(i => i.DisplayName)))).ToList());
                Replace(Strings, inspection.Strings.Take(300).ToList());
                if (inspection.OverlayEvidence != null)
                    Replace(OverlaySlots, inspection.OverlayEvidence.Slots);
                Replace(SlotRoleCandidates, MagicDllSemanticAnalyzer.AnalyzeOverlaySlots(inspection));
                Replace(StringFamilies, MagicDllSemanticAnalyzer.AnalyzeStringFamilies(inspection).Take(64).ToList());
                Replace(HostFieldRoles, MagicDllSemanticAnalyzer.HostFieldRoles().ToList());
                allValueCandidates.AddRange(BuildValueCandidates(inspection, SelectedDll.FullPath, SelectedDll.MagicId));
                RefreshValueCandidates();
                SelectedHostFieldRole = HostFieldRoles.FirstOrDefault(role =>
                    role.CandidateName.Contains("Timer", StringComparison.OrdinalIgnoreCase)
                    || role.CandidateName.Contains("Progress", StringComparison.OrdinalIgnoreCase)
                    || role.CandidateName.Contains("Random", StringComparison.OrdinalIgnoreCase))
                    ?? HostFieldRoles.FirstOrDefault();
                List<string> warnings = inspection.Warnings.ToList();
                MagicDllFamilyClassification classification = MagicDllSemanticAnalyzer.ClassifyFamily(inspection);
                selectedEffectFamily = classification.Family;
                RefreshLogicalDecompile(inspection);
                if (ShowValueWorkbenchFamilyWarning)
                    warnings.Add(ValueWorkbenchFamilyWarning);
                Replace(Warnings, warnings);
                RefreshPs3Folders();
                RefreshFamilyComparator();
                RefreshWave4Attribution();
                RefreshSeSepRecords(SelectedDll.FullPath);
                RefreshWd3Streams(SelectedDll.FullPath);
                OnPropertyChanged(nameof(ShowValueWorkbenchFamilyWarning));
                StatusText = $"Inspected {inspection.FileName}: {inspection.Sections.Count} sections, {inspection.Exports.Count} exports.";
            }
            catch (Exception ex)
            {
                StatusText = $"Inspect failed: {ex.Message}";
            }
        }

        void RefreshSeSepRecords(string dllPath)
        {
            SeSepRecords.Clear();
            try
            {
                IReadOnlyList<MagicDllSoundRecordScanner.SeSepHit> hits = MagicDllSoundWriter.ListRecords(dllPath);
                int idx = 0;
                foreach (MagicDllSoundRecordScanner.SeSepHit hit in hits)
                {
                    SeSepRecords.Add(new MagicDllSeSepRow(idx, hit));
                    idx++;
                }
            }
            catch
            {
                // non-fatal — DLL may lack SeSep
            }
        }

        // Jarvis-WD3 (2026-07-07, todo 12): Scan the selected DLL for the WD3 magic
        // "WD3\x01" (0x57 0x44 0x33 0x01) and hand the offset to Wd3StreamViewModel.
        // Non-fatal — DLLs without a WD3 blob simply show the empty state.
        void RefreshWd3Streams(string dllPath)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(dllPath);
                int blobOffset = FindWd3MagicOffset(bytes);
                if (blobOffset < 0)
                {
                    Wd3Streams.ClearStreams();
                    return;
                }
                Wd3Streams.LoadFromDll(bytes, blobOffset);
            }
            catch
            {
                // non-fatal — WD3 parsing is best-effort and isolated from the rest of the inspection
                Wd3Streams.ClearStreams();
            }
        }

        /// <summary>
        /// Scan DLL bytes for the WD3 magic "WD3\x01" (4 bytes: 0x57 0x44 0x33 0x01).
        /// Returns the file offset of the magic, or -1 if not found.
        /// Mirrors the scan in <see cref="FFXProjectEditor.FfxLib.Ps3.Wd3StreamRecolor"/>.
        /// </summary>
        static int FindWd3MagicOffset(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 4)
                return -1;
            for (int i = 0; i <= bytes.Length - 4; i++)
            {
                if (bytes[i] == 0x57
                    && bytes[i + 1] == 0x44
                    && bytes[i + 2] == 0x33
                    && bytes[i + 3] == 0x01)
                {
                    return i;
                }
            }
            return -1;
        }

        public void StageSelectedValuePatch()
        {
            try
            {
                (MagicDllValueCandidate candidate, byte[] payload) = BuildSelectedValuePayload();
                PatchFileOffsetText = $"0x{candidate.FileOffset:X}";
                PatchRvaText = string.Empty;
                PatchHexBytesText = FormatHexBytes(payload);
                StatusText = $"Staged {candidate.Kind} candidate patch at file 0x{candidate.FileOffset:X8}: {candidate.CurrentValue} -> {AssistedValueText}.";
            }
            catch (Exception ex)
            {
                StatusText = $"Value patch staging failed: {ex.Message}";
            }
        }

        public void ApplySelectedValuePatch(string outputDll)
        {
            try
            {
                (MagicDllValueCandidate candidate, byte[] payload) = BuildSelectedValuePayload();
                PatchFileOffsetText = $"0x{candidate.FileOffset:X}";
                PatchRvaText = string.Empty;
                PatchHexBytesText = FormatHexBytes(payload);
                MagicDllPatchPlan plan = new()
                {
                    BytePatches =
                    [
                        new MagicDllBytePatch
                        {
                            FileOffset = candidate.FileOffset,
                            Rva = 0,
                            Hex = FormatHexBytes(payload),
                            Note = $"Generated by Magic DLL Value Workbench: {candidate.Kind} / {candidate.Hypothesis}"
                        }
                    ]
                };
                ApplyInlinePatchPlan(plan, outputDll, "Value workbench patch");
            }
            catch (Exception ex)
            {
                StatusText = $"Value patch failed: {ex.Message}";
            }
        }

        public void StageSelectedHostReferencePatch()
        {
            try
            {
                (MagicDllHostReferenceRow row, byte[] payload, int newOffset) = BuildSelectedHostReferencePayload();
                PatchFileOffsetText = $"0x{row.FileOffset:X}";
                PatchRvaText = string.Empty;
                PatchHexBytesText = FormatHexBytes(payload);
                StatusText = $"Staged host-offset reference patch at file 0x{row.FileOffset:X8}: 0x{row.HostOffset:X} -> 0x{newOffset:X}.";
            }
            catch (Exception ex)
            {
                StatusText = $"Host reference staging failed: {ex.Message}";
            }
        }

        public void ApplySelectedHostReferencePatch(string outputDll)
        {
            try
            {
                (MagicDllHostReferenceRow row, byte[] payload, int newOffset) = BuildSelectedHostReferencePayload();
                PatchFileOffsetText = $"0x{row.FileOffset:X}";
                PatchRvaText = string.Empty;
                PatchHexBytesText = FormatHexBytes(payload);
                MagicDllPatchPlan plan = new()
                {
                    BytePatches =
                    [
                        new MagicDllBytePatch
                        {
                            FileOffset = row.FileOffset,
                            Rva = 0,
                            Hex = FormatHexBytes(payload),
                            Note = $"Generated by Magic DLL Host Context assistant: {row.CandidateName} -> 0x{newOffset:X}."
                        }
                    ]
                };
                ApplyInlinePatchPlan(plan, outputDll, "Host reference patch");
            }
            catch (Exception ex)
            {
                StatusText = $"Host reference patch failed: {ex.Message}";
            }
        }

        public void DecompileSelected(string outputDir)
        {
            if (SelectedDll == null)
                return;
            try
            {
                MagicDllDecompileResult result = MagicDllDecompiler.DecompileToFolder(SelectedDll.FullPath, outputDir, Directory.GetCurrentDirectory());
                StatusText = $"Extracted decompile manifest, sections, patch template, and C/ASM project to {result.MarkdownPath}.";
            }
            catch (Exception ex)
            {
                StatusText = $"Decompile failed: {ex.Message}";
            }
        }

        public void LogicalDecompileSelected(string outputDir)
        {
            if (SelectedDll == null || SelectedInspection == null)
                return;
            try
            {
                MagicDllLogicalDecompileResult logical = MagicDllLogicalDecompiler.Decompile(SelectedInspection);
                string id = SelectedDll.MagicIdText;
                string dllDir = Path.Combine(outputDir, $"magic_{id}");
                MagicDllLogicalDecompiler.WritePerDllReport(logical, dllDir);
                RefreshLogicalDecompile(SelectedInspection);
                StatusText = $"Logical decompile exported to {dllDir} (LOGICAL_DECOMPILE.md + overlay_pseudocode.c). Hex-Rays tier: work/magic_dll_logical_decompile_wave2/wave2_hexrays_queue_pinned.json";
            }
            catch (Exception ex)
            {
                StatusText = $"Logical decompile failed: {ex.Message}";
            }
        }

        public int? GetSelectedMagicId() => SelectedDll?.MagicId;

        public string? GetPs3ExtractFolder() => HasPs3ExtractFolder ? ps3ExtractFolder : null;

        public string? GetFirstPhyrePackagePath() => HasPhyrePackage ? FirstPhyrePackagePath : null;

        void ClearWave4Attribution()
        {
            Wave4Category = "-";
            Wave4Family = "-";
            Wave4RecommendedAction = "-";
            Wave4OverlaySignature = "-";
            Wave4EditingGuidance = "-";
            Wave4Rt2Priority = "-";
            Wave4CatalogSpells = "-";
            Wave4KernelRefSummary = "-";
            Wave4ClusterNote = string.Empty;
            FirstPhyrePackagePath = null;
            Wave4KernelRefs.Clear();
        }

        void RefreshWave4Attribution()
        {
            ClearWave4Attribution();
            if (SelectedDll?.MagicId is not int magicId)
                return;

            FirstPhyrePackagePath = MagicDllPs3PhyreResolver.ResolveFirstPhyrePackage(magicId);
            if (!MagicDllWave4CatalogLoader.TryGet(magicId, out MagicDllWave4Attribution row))
                return;

            Wave4Category = row.CategoryBadge;
            Wave4Family = row.Family;
            Wave4RecommendedAction = row.RecommendedAction;
            Wave4OverlaySignature = row.OverlaySlotKindSignature;
            Wave4EditingGuidance = row.EditingGuidance;
            Wave4Rt2Priority = row.Rt2Priority;
            Wave4CatalogSpells = row.CatalogSpellsSummary;
            Wave4KernelRefSummary = row.KernelRefSummary;
            Wave4ClusterNote = row.IsClusterRep
                ? "Cluster representative for overlay/hash siblings."
                : row.ClusterRepMagicId is int rep
                    ? $"Cluster member — compare against rep magic_{rep:D4}."
                    : string.Empty;
            Replace(Wave4KernelRefs, row.KernelRefs.Take(24).Select(r => new MagicDllWave4KernelRefRow(r)).ToList());
        }

        public string? GetPs3ModsFolder() => HasPs3ModsFolder ? ps3ModsFolder : null;

        void RefreshLogicalDecompile(MagicDllInspection inspection)
        {
            MagicDllLogicalDecompileResult logical = MagicDllLogicalDecompiler.Decompile(inspection);
            Replace(LogicalSlots, logical.Slots.Select(s => MagicDllLogicalSlotRow.FromSlot(s)).ToList());
            LogicalDecompileSummary = string.Join(" · ", logical.SummaryLines)
                + " — static fingerprint; full behavior needs Hex-Rays (wave2 pinned queue).";
            OnPropertyChanged(nameof(HasLogicalDecompile));
        }

        void RefreshPs3Folders()
        {
            if (SelectedDll?.MagicId is not int magicId)
            {
                ps3ExtractFolder = string.Empty;
                ps3ModsFolder = string.Empty;
            }
            else
            {
                string ps3Root = Project_Service.Instance.Path_Ps3DataRoot is string ps3
                    ? Path.Combine(ps3, "magic")
                    : string.Empty;
                ps3ExtractFolder = Path.Combine(ps3Root, $"magic_{magicId:D4}");
                string gameRoot = ResolveGameRootFromMagicPath(RootPath)
                    ?? PortablePathResolver.GameInstallRoot
                    ?? string.Empty;
                ps3ModsFolder = MagicEffectClonePipeline.ResolveModsPs3MagicFolder(gameRoot, magicId);
            }

            OnPropertyChanged(nameof(HasPs3ExtractFolder));
            OnPropertyChanged(nameof(HasPs3ModsFolder));
            OnPropertyChanged(nameof(Ps3PathSummary));
        }

        private static string? ResolveGameRootFromMagicPath(string? magicRoot)
        {
            if (string.IsNullOrWhiteSpace(magicRoot))
                return null;
            try
            {
                DirectoryInfo? magicFiles = Directory.GetParent(Path.GetFullPath(magicRoot));
                return magicFiles?.Parent?.FullName;
            }
            catch
            {
                return null;
            }
        }

        public void RepackSelected(string outputDll)
        {
            if (SelectedDll == null)
                return;
            try
            {
                MagicDllCompileResult result = MagicDllDecompiler.CompileBytePreserving(SelectedDll.FullPath, outputDll);
                StatusText = result.Pass
                    ? $"Repacked byte-identical DLL: {result.OutputDll}"
                    : $"Repack drifted: source {result.SourceSha256}, output {result.OutputSha256}";
            }
            catch (Exception ex)
            {
                StatusText = $"Repack failed: {ex.Message}";
            }
        }

        public void ApplyPatchPlan(string patchPlanPath, string outputDll)
        {
            if (SelectedDll == null)
                return;
            try
            {
                MagicDllCompileResult result = MagicDllDecompiler.ApplyPatchPlan(SelectedDll.FullPath, patchPlanPath, outputDll);
                StatusText = $"Patched DLL emitted: {result.OutputDll} ({result.OutputSha256}).";
            }
            catch (Exception ex)
            {
                StatusText = $"Patch compile failed: {ex.Message}";
            }
        }

        public void ApplyInlineBytePatch(string outputDll)
        {
            if (SelectedDll == null)
                return;

            try
            {
                (int fileOffset, int rva) = ReadPatchAddress();
                string cleanHex = NormalizeHexBytes(PatchHexBytesText);
                if (cleanHex.Length == 0)
                    throw new InvalidOperationException("Byte patch needs at least one hex byte.");
                if (cleanHex.Length % 2 != 0)
                    throw new InvalidOperationException("Hex byte string has odd length.");

                MagicDllPatchPlan plan = new()
                {
                    BytePatches =
                    [
                        new MagicDllBytePatch
                        {
                            FileOffset = fileOffset,
                            Rva = rva,
                            Hex = cleanHex,
                            Note = "Generated by Magic DLL Direct Patch Builder."
                        }
                    ]
                };
                ApplyInlinePatchPlan(plan, outputDll, "Byte patch");
            }
            catch (Exception ex)
            {
                StatusText = $"Byte patch failed: {ex.Message}";
            }
        }

        public void ApplyInlineAsciiPatch(string outputDll)
        {
            if (SelectedDll == null)
                return;

            try
            {
                (int fileOffset, int rva) = ReadPatchAddress();
                int maxLength = string.IsNullOrWhiteSpace(PatchAsciiMaxLengthText)
                    ? 0
                    : ParseFlexibleInt(PatchAsciiMaxLengthText);
                if (maxLength < 0)
                    throw new InvalidOperationException("ASCII max length must be zero or positive.");

                MagicDllPatchPlan plan = new()
                {
                    AsciiPatches =
                    [
                        new MagicDllAsciiPatch
                        {
                            FileOffset = fileOffset,
                            Rva = rva,
                            Text = PatchAsciiText ?? string.Empty,
                            MaxLength = maxLength,
                            NullTerminate = PatchAsciiNullTerminate,
                            Note = "Generated by Magic DLL Direct Patch Builder."
                        }
                    ]
                };
                ApplyInlinePatchPlan(plan, outputDll, "ASCII patch");
            }
            catch (Exception ex)
            {
                StatusText = $"ASCII patch failed: {ex.Message}";
            }
        }

        public void CloneSelected()
        {
            if (SelectedDll == null)
                return;
            if (!int.TryParse(CloneMagicIdText, out int newId))
            {
                StatusText = "Clone id must be a number like 0716.";
                return;
            }

            try
            {
                MagicDllCloneResult result = MagicDllDecompiler.CloneToMagicId(SelectedDll.FullPath, RootPath, newId);
                StatusText = result.ByteIdentical
                    ? $"Cloned {Path.GetFileName(result.SourceDll)} to {Path.GetFileName(result.OutputDll)} byte-identical."
                    : $"Clone wrote {result.OutputDll}, but hash drifted.";
                SearchText = newId.ToString("D4");
                Refresh();
                SelectedDll = Dlls.FirstOrDefault(dll => dll.MagicId == newId);
            }
            catch (Exception ex)
            {
                StatusText = $"Clone failed: {ex.Message}";
            }
        }

        (int FileOffset, int Rva) ReadPatchAddress()
        {
            bool hasFileOffset = !string.IsNullOrWhiteSpace(PatchFileOffsetText);
            bool hasRva = !string.IsNullOrWhiteSpace(PatchRvaText);
            if (hasFileOffset == hasRva)
                throw new InvalidOperationException("Fill exactly one address: file offset or RVA.");
            return (
                hasFileOffset ? ParseFlexibleInt(PatchFileOffsetText) : -1,
                hasRva ? ParseFlexibleInt(PatchRvaText) : 0);
        }

        void ApplyInlinePatchPlan(MagicDllPatchPlan plan, string outputDll, string label)
        {
            string patchPlanPath = Path.Combine(Path.GetTempPath(), $"ffx_magicdll_patch_{Guid.NewGuid():N}.json");
            try
            {
                File.WriteAllText(patchPlanPath, JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true }));
                MagicDllCompileResult result = MagicDllDecompiler.ApplyPatchPlan(SelectedDll!.FullPath, patchPlanPath, outputDll);
                StatusText = $"{label} emitted: {result.OutputDll} ({result.OutputSha256}).";
            }
            finally
            {
                try
                {
                    if (File.Exists(patchPlanPath))
                        File.Delete(patchPlanPath);
                }
                catch
                {
                    // A stale temp patch plan is harmless and should not hide the real patch result.
                }
            }
        }

        static int ParseFlexibleInt(string text)
        {
            string value = (text ?? string.Empty).Trim();
            if (value.Length == 0)
                throw new FormatException("numeric value is empty.");
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return int.Parse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (value.EndsWith("h", StringComparison.OrdinalIgnoreCase))
                return int.Parse(value[..^1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        static string NormalizeHexBytes(string? text)
        {
            string withoutPrefixes = (text ?? string.Empty).Replace("0x", " ", StringComparison.OrdinalIgnoreCase);
            return new string(withoutPrefixes.Where(Uri.IsHexDigit).ToArray());
        }

        void RefreshFamilyComparator()
        {
            FamilySpells.Clear();
            FamilySiblings.Clear();
            FamilyTwinDlls.Clear();
            if (SelectedDll?.MagicId is not int magicId)
                return;

            MagicDllFamilySiblingReport report = MagicDllFamilyComparator.BuildSiblingReport(magicId, RootPath);
            Replace(FamilySpells, report.SpellsUsingThisDll
                .Select(spell => new MagicDllFamilySpellRow(spell))
                .ToList());
            Replace(FamilySiblings, report.OverlaySiblings
                .Take(32)
                .Select(sib => MagicDllFamilySiblingRow.FromMatch(sib))
                .ToList());
            Replace(FamilyTwinDlls, report.TwinDllMatches
                .Select(sib => MagicDllFamilySiblingRow.FromMatch(sib))
                .ToList());
            OnPropertyChanged(nameof(FamilyComparatorSummary));
        }

        internal static bool TryResolveCloneDeployIds(
            int? selectedId,
            string? targetText,
            out int sourceId,
            out int targetId,
            out string error)
        {
            sourceId = -1;
            targetId = -1;
            if (selectedId is not int selected || selected is < 0 or > 9999)
            {
                error = "Select a magic DLL with a numeric id first.";
                return false;
            }
            if (!int.TryParse(
                    targetText,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int parsedTarget) ||
                parsedTarget is < 0 or > 9999)
            {
                error = "Clone target id must be a number from 0000 through 9999.";
                return false;
            }
            if (selected == parsedTarget)
            {
                error = "Clone source and target ids must be different.";
                return false;
            }

            sourceId = selected;
            targetId = parsedTarget;
            error = string.Empty;
            return true;
        }

        public void DeploySelectedCloneToMods()
        {
            string? selectedSourceDll = SelectedDll?.FullPath;
            if (!TryResolveCloneDeployIds(
                    SelectedDll?.MagicId,
                    CloneMagicIdText,
                    out int sourceId,
                    out int targetId,
                    out string idError))
            {
                StatusText = idError;
                return;
            }

            try
            {
                string gameRoot = ResolveGameRootFromMagicPath(RootPath)
                    ?? PortablePathResolver.GameInstallRoot
                    ?? string.Empty;

                string ps3Root = Project_Service.Instance.Path_Ps3DataRoot is string ps3
                    ? Path.Combine(ps3, "magic")
                    : string.Empty;

                MagicEffectCloneDeployResult result = MagicEffectClonePipeline.DeployClone(
                    sourceId,
                    targetId,
                    ps3Root,
                    gameRoot,
                    RootPath,
                    applyPrismTextureRecolor: false,
                    selectedSourceDll: selectedSourceDll);

                StatusText = result.Pass
                    ? $"Deployed clone ps3data to {result.DeployedPs3Folder} ({result.DeployedTextureCount} textures) + DLL."
                    : $"Clone deploy incomplete for magic_{targetId:D4}.";
            }
            catch (Exception ex)
            {
                StatusText = $"Clone deploy failed: {ex.Message}";
            }
        }

        void RefreshValueCandidates()
        {
            IEnumerable<MagicDllValueCandidate> query = allValueCandidates;
            if (!string.IsNullOrWhiteSpace(ValueCandidateSearchText))
            {
                string needle = ValueCandidateSearchText.Trim();
                query = query.Where(candidate => candidate.SearchText.Contains(needle, StringComparison.OrdinalIgnoreCase));
            }

            Replace(ValueCandidates, query.Take(360).ToList());
            if (SelectedValueCandidate == null || !ValueCandidates.Contains(SelectedValueCandidate))
                SelectedValueCandidate = ValueCandidates.FirstOrDefault();
        }

        void RefreshHostFieldReferences()
        {
            HostFieldReferences.Clear();
            SelectedHostReference = null;
            if (SelectedDll == null || SelectedInspection == null || SelectedHostFieldRole == null || !File.Exists(SelectedDll.FullPath))
                return;

            try
            {
                byte[] bytes = File.ReadAllBytes(SelectedDll.FullPath);
                int target = SelectedHostFieldRole.Offset;
                List<MagicDllHostReferenceRow> rows = [];
                for (int i = 0; i <= bytes.Length - 4; i++)
                {
                    if (BitConverter.ToInt32(bytes, i) != target)
                        continue;

                    rows.Add(new MagicDllHostReferenceRow(
                        target,
                        SelectedHostFieldRole.CandidateName,
                        i,
                        TryFileOffsetToRva(SelectedInspection, i),
                        SectionNameForOffset(SelectedInspection, i),
                        "u32 host-context offset reference",
                        BuildContextHex(bytes, i, 10, 14)));
                    if (rows.Count >= 160)
                        break;
                }

                Replace(HostFieldReferences, rows);
                SelectedHostReference = HostFieldReferences.FirstOrDefault();
            }
            catch (Exception ex)
            {
                StatusText = $"Host reference scan failed: {ex.Message}";
            }
        }

        (MagicDllValueCandidate Candidate, byte[] Payload) BuildSelectedValuePayload()
        {
            if (SelectedValueCandidate == null)
                throw new InvalidOperationException("Select a value candidate first.");
            if (SelectedValueCandidate.PatchBlocked)
                throw new InvalidOperationException(
                    $"RT2− blocked: {SelectedValueCandidate.Hypothesis} — {SelectedValueCandidate.Rt2Symptom}");
            byte[] payload = SelectedValueCandidate.EncodePatchPayload(AssistedValueText);
            return (SelectedValueCandidate, payload);
        }

        (MagicDllHostReferenceRow Row, byte[] Payload, int NewOffset) BuildSelectedHostReferencePayload()
        {
            if (SelectedHostReference == null)
                throw new InvalidOperationException("Select a host reference occurrence first.");
            int newOffset = ParseFlexibleInt(AssistedHostOffsetText);
            if (newOffset < 0 || newOffset > 0xFFFF)
                throw new InvalidOperationException("Host context offset should stay in the 0..0xFFFF range.");
            return (SelectedHostReference, BitConverter.GetBytes(newOffset), newOffset);
        }

        static IReadOnlyList<MagicDllValueCandidate> BuildValueCandidates(
            MagicDllInspection inspection,
            string dllPath,
            int? magicId)
        {
            if (!File.Exists(dllPath))
                return [];

            byte[] bytes = File.ReadAllBytes(dllPath);
            MagicDllEffectFamily family = MagicDllSemanticAnalyzer.ClassifyFamily(inspection).Family;
            List<MagicDllValueCandidate> candidates = [];
            foreach (MagicDllSection section in inspection.Sections)
            {
                int start = Math.Max(0, section.RawPointer);
                int end = Math.Min(bytes.Length, section.RawPointer + section.RawSize);
                if (end - start < 4)
                    continue;

                bool text = section.Name.Equals(".text", StringComparison.OrdinalIgnoreCase);
                if (!text)
                    ScanFloatVectors(inspection, bytes, section, start, end, candidates, family, magicId);
                ScanFloatConstants(inspection, bytes, section, start, end, candidates, text);
                if (text)
                    ScanPushImmediates(inspection, bytes, section, start, end, candidates);
            }

            return candidates
                .GroupBy(c => $"{c.FileOffset:X8}:{c.ByteLength}:{c.Kind}", StringComparer.Ordinal)
                .Select(g => g.OrderByDescending(c => c.Score).First())
                .OrderByDescending(c => c.Score)
                .ThenBy(c => c.FileOffset)
                .Take(900)
                .ToList();
        }

        static void ScanFloatVectors(
            MagicDllInspection inspection,
            byte[] bytes,
            MagicDllSection section,
            int start,
            int end,
            List<MagicDllValueCandidate> candidates,
            MagicDllEffectFamily family,
            int? magicId)
        {
            int alignedStart = Align4(start);
            for (int offset = alignedStart; offset <= end - 12; offset += 4)
            {
                float a = BitConverter.ToSingle(bytes, offset);
                float b = BitConverter.ToSingle(bytes, offset + 4);
                float c = BitConverter.ToSingle(bytes, offset + 8);
                if (!LooksLikeColorComponent(a) || !LooksLikeColorComponent(b) || !LooksLikeColorComponent(c))
                    continue;

                bool hasFourth = offset <= end - 16 && LooksLikeColorComponent(BitConverter.ToSingle(bytes, offset + 12));
                float[] values = hasFourth
                    ? [a, b, c, BitConverter.ToSingle(bytes, offset + 12)]
                    : [a, b, c];
                if (values.All(v => Math.Abs(v) < 0.0001f) || values.Max() - values.Min() < 0.0001f && values[0] is 0f)
                    continue;

                string kind = hasFourth ? "vec4f" : "vec3f";
                bool possibleTiming = IsPossibleTimingBlueDominantVec3(a, b, c);
                MagicDllRt2VerdictCatalog.Overlay rt2 = MagicDllRt2VerdictCatalog.Resolve(
                    magicId, family, offset, hasFourth, possibleTiming);

                string hypothesis;
                string confidence;
                int score = 90 + values.Count(v => v > 0f && v <= 1.0f) * 4;
                bool timingRisk = possibleTiming;
                MagicDllRt2VerdictCatalog.VerdictKind rt2Kind = MagicDllRt2VerdictCatalog.VerdictKind.None;
                string rt2Symptom = string.Empty;
                string rt2LedgerId = string.Empty;

                if (rt2.Kind != MagicDllRt2VerdictCatalog.VerdictKind.None)
                {
                    hypothesis = rt2.Hypothesis;
                    confidence = rt2.Confidence;
                    score = rt2.ScoreCap;
                    timingRisk = rt2.IsTimingProved || possibleTiming;
                    rt2Kind = rt2.Kind;
                    rt2Symptom = rt2.Symptom;
                    rt2LedgerId = rt2.LedgerId;
                }
                else
                {
                    hypothesis = ClassifyVectorHypothesis(values, hasFourth, family, possibleTiming);
                    if (possibleTiming)
                        score += family is MagicDllEffectFamily.C_RootSelfGovernedParam or MagicDllEffectFamily.D_EgoTasklist ? 45 : 25;
                    confidence = possibleTiming ? "timing-risk" : "needs-rt2";
                }

                candidates.Add(new MagicDllValueCandidate(
                    MagicDllValueEncoding.FloatVector,
                    offset,
                    TryFileOffsetToRva(inspection, offset),
                    section.Name,
                    hasFourth ? 16 : 12,
                    kind,
                    string.Join(", ", values.Select(FormatFloat)),
                    string.Join(", ", values.Select(FormatFloat)),
                    hypothesis,
                    confidence,
                    BuildContextHex(bytes, offset, 12, (hasFourth ? 16 : 12) + 8),
                    score,
                    values.Length,
                    timingRisk,
                    rt2Kind,
                    rt2Symptom,
                    rt2LedgerId));
            }
        }

        static bool IsPossibleTimingBlueDominantVec3(float r, float g, float b) =>
            b >= 0.55f && r <= 0.55f;

        static string ClassifyVectorHypothesis(float[] values, bool hasFourth, MagicDllEffectFamily family, bool possibleTiming)
        {
            if (possibleTiming)
            {
                return family is MagicDllEffectFamily.C_RootSelfGovernedParam or MagicDllEffectFamily.D_EgoTasklist
                    ? "possible timing (cast→hit sync) — RT2 0718: blue-dominant vec4 is NOT visual color"
                    : "possible timing / phase sync (blue-dominant vec4 — verify RT2 before patch)";
            }

            if (values.Length >= 3 && values[0] >= 0.92f && values[1] >= 0.92f && values[2] >= 0.92f)
                return hasFourth
                    ? FFXProjectEditor.Resources.Strings.U_Md_HypothesisVec4
                    : FFXProjectEditor.Resources.Strings.U_Md_HypothesisVec3;

            return MagicDllRt2VerdictCatalog.ClassifyUnprovenVectorHypothesis(hasFourth);
        }

        static void ScanFloatConstants(MagicDllInspection inspection, byte[] bytes, MagicDllSection section, int start, int end, List<MagicDllValueCandidate> candidates, bool textSection)
        {
            int alignedStart = Align4(start);
            for (int offset = alignedStart; offset <= end - 4; offset += 4)
            {
                float value = BitConverter.ToSingle(bytes, offset);
                if (!LooksLikeInterestingFloat(value, textSection, bytes, offset, start, end))
                    continue;

                string hypothesis = ClassifyFloatHypothesis(value);
                int score = (textSection ? 42 : 60)
                    + (IsCommonFloat(value) ? 24 : 0)
                    + (HasNearbyFloat(bytes, offset, start, end) ? 12 : 0)
                    + (value >= 0f && value <= 1f ? 10 : 0);
                candidates.Add(new MagicDllValueCandidate(
                    MagicDllValueEncoding.Float32,
                    offset,
                    TryFileOffsetToRva(inspection, offset),
                    section.Name,
                    4,
                    "float32",
                    FormatFloat(value),
                    FormatFloat(value),
                    hypothesis,
                    score >= 86 ? "medium" : "low",
                    BuildContextHex(bytes, offset, 8, 12),
                    score,
                    1));
            }
        }

        static void ScanPushImmediates(MagicDllInspection inspection, byte[] bytes, MagicDllSection section, int start, int end, List<MagicDllValueCandidate> candidates)
        {
            for (int offset = start; offset < end; offset++)
            {
                if (bytes[offset] == 0x6A && offset + 1 < end)
                {
                    int value = unchecked((sbyte)bytes[offset + 1]);
                    if (!LooksLikeUsefulInteger(value))
                        continue;
                    int patchOffset = offset + 1;
                    candidates.Add(new MagicDllValueCandidate(
                        MagicDllValueEncoding.Int8,
                        patchOffset,
                        TryFileOffsetToRva(inspection, patchOffset),
                        section.Name,
                        1,
                        "push imm8",
                        $"{value} / 0x{(byte)value:X2}",
                        value.ToString(CultureInfo.InvariantCulture),
                        ClassifyIntegerHypothesis(value),
                        "low",
                        BuildContextHex(bytes, offset, 8, 10),
                        58 + ScoreInteger(value),
                        1));
                }
                else if (bytes[offset] == 0x68 && offset + 4 < end)
                {
                    int value = BitConverter.ToInt32(bytes, offset + 1);
                    if (!LooksLikeUsefulInteger(value))
                        continue;
                    int patchOffset = offset + 1;
                    candidates.Add(new MagicDllValueCandidate(
                        MagicDllValueEncoding.Int32,
                        patchOffset,
                        TryFileOffsetToRva(inspection, patchOffset),
                        section.Name,
                        4,
                        "push imm32",
                        $"{value} / 0x{value:X8}",
                        value.ToString(CultureInfo.InvariantCulture),
                        ClassifyIntegerHypothesis(value),
                        "low",
                        BuildContextHex(bytes, offset, 8, 14),
                        54 + ScoreInteger(value),
                        1));
                }
            }
        }

        static bool LooksLikeInterestingFloat(float value, bool textSection, byte[] bytes, int offset, int start, int end)
        {
            if (!float.IsFinite(value))
                return false;
            float abs = Math.Abs(value);
            if (abs < 0.0001f || abs > 100000f)
                return false;
            if (IsCommonFloat(value))
                return true;
            if (textSection)
                return false;
            return abs <= 720f && HasNearbyFloat(bytes, offset, start, end);
        }

        static bool HasNearbyFloat(byte[] bytes, int offset, int start, int end)
        {
            for (int delta = -12; delta <= 12; delta += 4)
            {
                if (delta == 0)
                    continue;
                int other = offset + delta;
                if (other < start || other > end - 4)
                    continue;
                float value = BitConverter.ToSingle(bytes, other);
                if (!float.IsFinite(value))
                    continue;
                float abs = Math.Abs(value);
                if (abs >= 0.0001f && abs <= 100000f)
                    return true;
            }
            return false;
        }

        static bool LooksLikeColorComponent(float value) => float.IsFinite(value) && value >= 0f && value <= 1.5f;

        static bool IsCommonFloat(float value)
        {
            float abs = Math.Abs(value);
            float[] common =
            [
                0.016666668f, 0.033333335f, 0.0625f, 0.1f, 0.125f, 0.2f, 0.25f, 0.33333334f,
                0.5f, 0.75f, 1f, 1.5f, 2f, 3f, 4f, 5f, 8f, 10f, 16f, 30f, 45f, 60f, 90f,
                120f, 180f, 255f, 256f, 360f
            ];
            return common.Any(c => Math.Abs(abs - c) <= Math.Max(0.00001f, c * 0.0001f));
        }

        static string ClassifyFloatHypothesis(float value)
        {
            float abs = Math.Abs(value);
            if (value >= 0f && value <= 1f)
                return "alpha/color weight/interpolation scalar candidate";
            if (abs <= 8f)
                return "scale/speed multiplier or local movement scalar candidate";
            if (abs <= 32f)
                return "particle scale/radius/speed candidate";
            if (abs <= 360f)
                return "timer/frame count/angle/radius candidate";
            return "large distance/timer/resource scalar candidate";
        }

        static bool LooksLikeUsefulInteger(int value)
        {
            int abs = Math.Abs(value);
            if (abs == 0 || abs > 100000)
                return false;
            return abs <= 720 || IsCommonInteger(abs);
        }

        static bool IsCommonInteger(int value)
        {
            int[] common = [1, 2, 3, 4, 5, 6, 8, 10, 12, 15, 16, 24, 30, 32, 45, 60, 64, 90, 100, 120, 128, 180, 240, 255, 256, 300, 360, 512, 720, 1000, 1024, 2048, 4096, 10000];
            return common.Contains(Math.Abs(value));
        }

        static int ScoreInteger(int value)
        {
            int abs = Math.Abs(value);
            int score = IsCommonInteger(abs) ? 22 : 0;
            if (abs <= 360)
                score += 8;
            if (abs is 30 or 60 or 90 or 120 or 180 or 255 or 360)
                score += 10;
            return score;
        }

        static string ClassifyIntegerHypothesis(int value)
        {
            int abs = Math.Abs(value);
            if (abs is 30 or 60 or 90 or 120 or 180)
                return "timer/frame/angle argument candidate";
            if (abs is 255 or 128 or 64 or 32 or 16 or 8 or 4 or 2 or 1)
                return "flag/count/color-channel argument candidate";
            if (abs <= 12)
                return "phase/index/count argument candidate";
            if (abs <= 720)
                return "duration/radius/angle/count argument candidate";
            return "large allocation/resource/count argument candidate";
        }

        static int Align4(int value) => (value + 3) & ~3;

        static int TryFileOffsetToRva(MagicDllInspection inspection, int fileOffset)
        {
            try
            {
                return inspection.FileOffsetToRva(fileOffset);
            }
            catch
            {
                return 0;
            }
        }

        static string SectionNameForOffset(MagicDllInspection inspection, int fileOffset)
        {
            MagicDllSection? section = inspection.Sections.FirstOrDefault(s => fileOffset >= s.RawPointer && fileOffset < s.RawPointer + s.RawSize);
            return section?.Name ?? "headers/overlay";
        }

        static string FormatFloat(float value) => value.ToString("G9", CultureInfo.InvariantCulture);

        static string FormatHexBytes(byte[] bytes) => string.Join(" ", bytes.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));

        static string BuildContextHex(byte[] bytes, int offset, int before, int after)
        {
            int start = Math.Max(0, offset - before);
            int end = Math.Min(bytes.Length, offset + after);
            string hex = string.Join(" ", bytes.Skip(start).Take(end - start).Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
            return $"0x{start:X8}: {hex}";
        }

        public void BuildNativeProject(string outputDir)
        {
            if (SelectedDll == null)
                return;
            try
            {
                MagicDllDecompileResult decompile = MagicDllDecompiler.DecompileToFolder(SelectedDll.FullPath, outputDir, Directory.GetCurrentDirectory());
                MagicDllNativeBuildResult build = MagicDllDecompiler.BuildNativeProject(decompile.NativeProjectDir, Path.Combine(outputDir, "native_rebuild.dll"));
                StatusText = build.Pass
                    ? $"Native C/ASM build produced {build.OutputDll}."
                    : $"C/ASM project generated at {decompile.NativeProjectDir}. Build not completed: {build.Summary}";
            }
            catch (Exception ex)
            {
                StatusText = $"Native project failed: {ex.Message}";
            }
        }

        /// <summary>
        /// Gera o template instanciado para a DLL selecionada usando o instantiate.py (F3).
        /// </summary>
        public void GenerateInstantiatedTemplate()
        {
            if (SelectedDll == null)
            {
                StatusText = "Select a magic DLL first.";
                return;
            }
            try
            {
                string scriptPath = Path.Combine(
                    Directory.GetCurrentDirectory(), "docs", "reverse", "magic_dlls", "scripts", "instantiate.py");
                if (!File.Exists(scriptPath))
                {
                    StatusText = $"instantiate.py not found at {scriptPath}";
                    return;
                }
                string dllPath = SelectedDll.FullPath;
                string args = $"\"{dllPath}\"";
                System.Diagnostics.ProcessStartInfo psi = new("python", args)
                {
                    WorkingDirectory = Path.GetDirectoryName(scriptPath),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using System.Diagnostics.Process proc = System.Diagnostics.Process.Start(psi)
                    ?? throw new InvalidOperationException("Failed to start python.");
                proc.WaitForExit(60000);
                string output = proc.StandardOutput.ReadToEnd();
                string error = proc.StandardError.ReadToEnd();
                StatusText = proc.ExitCode == 0
                    ? $"Template generated: {output.Split(Environment.NewLine).FirstOrDefault(l => l.Contains("created") || l.Contains("SAVED")) ?? "OK"}"
                    : $"instantiate.py failed (exit {proc.ExitCode}): {error}";
            }
            catch (Exception ex)
            {
                StatusText = $"Template generation failed: {ex.Message}";
            }
        }

        public void BuildSemanticReport(string outputDir)
        {
            try
            {
                string repoRoot = Directory.GetCurrentDirectory();
                MagicDllCorpusSemanticAnalysis ffx = MagicDllSemanticAnalyzer.AnalyzeRoot("FFX", RootPath, repoRoot, attachFfxOverlayEvidence: true);
                string ffx2Root = Path.Combine(Directory.GetParent(RootPath)?.FullName ?? string.Empty, "FFX-2");
                MagicDllCorpusSemanticAnalysis? ffx2 = Directory.Exists(ffx2Root)
                    ? MagicDllSemanticAnalyzer.AnalyzeRoot("FFX-2", ffx2Root, repoRoot, attachFfxOverlayEvidence: false)
                    : null;
                MagicDllSemanticReportFiles files = MagicDllSemanticAnalyzer.WriteReport(ffx, ffx2, outputDir);
                StatusText = $"Semantic RE report generated: {files.MarkdownPath}. FFX {ffx.InspectedDlls}/{ffx.TotalDlls}; FFX-2 {(ffx2 == null ? "not found" : $"{ffx2.InspectedDlls}/{ffx2.TotalDlls}")}.";
            }
            catch (Exception ex)
            {
                StatusText = $"Semantic report failed: {ex.Message}";
            }
        }

        static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
        {
            target.Clear();
            foreach (T item in source)
                target.Add(item);
        }

        // Jarvis-PPP (2026-07-07, todo 14): Load the 36-entry PPP opcode handler table
        // from docs/reverse/magic_dlls/PPP_HANDLER_NAMES.json. Visualization only — no
        // opcode execution. If the JSON is missing or malformed, shows an empty-state
        // warning in the UI.
        static readonly JsonSerializerOptions PppJsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        void LoadPppOpcodes()
        {
            string jsonPath = Path.Combine(
                Directory.GetCurrentDirectory(), "docs", "reverse", "magic_dlls", "PPP_HANDLER_NAMES.json");
            if (!File.Exists(jsonPath))
            {
                PppOpcodesSummary = "PPP_HANDLER_NAMES.json not found. Run the RE pipeline (todo 1) to generate it.";
                PppOpcodes.Clear();
                OnPropertyChanged(nameof(HasPppOpcodes));
                return;
            }
            try
            {
                string json = File.ReadAllText(jsonPath);
                PppHandlerNamesDoc? doc = JsonSerializer.Deserialize<PppHandlerNamesDoc>(json, PppJsonOpts);
                if (doc?.Entries is null || doc.Entries.Count == 0)
                {
                    PppOpcodesSummary = "PPP_HANDLER_NAMES.json is empty or malformed.";
                    PppOpcodes.Clear();
                    OnPropertyChanged(nameof(HasPppOpcodes));
                    return;
                }
                Replace(PppOpcodes, doc.Entries.Select(PppOpcodeRow.FromEntry).ToList());
                PppOpcodesSummary = $"{PppOpcodes.Count} PPP opcodes loaded (table_base={doc.TableBase}, imagebase={doc.Imagebase}).";
                OnPropertyChanged(nameof(HasPppOpcodes));
            }
            catch (Exception ex)
            {
                PppOpcodesSummary = $"PPP opcode load failed: {ex.Message}";
                PppOpcodes.Clear();
                OnPropertyChanged(nameof(HasPppOpcodes));
            }
        }
    }

    internal sealed class MagicDllEntry
    {
        public required string FullPath { get; init; }
        public required string FileName { get; init; }
        public int? MagicId { get; init; }
        public long FileSize { get; init; }
        public DateTime LastWriteTime { get; init; }
        public string MagicIdText => MagicId.HasValue ? MagicId.Value.ToString("D4") : "-";
        public string? Wave4CategoryBadge { get; set; }
        public string Summary
        {
            get
            {
                string badge = string.IsNullOrWhiteSpace(Wave4CategoryBadge) ? string.Empty : $" · {Wave4CategoryBadge}";
                return $"magic {MagicIdText}{badge} · {FileSize:N0} bytes · {LastWriteTime:g}";
            }
        }

        public static MagicDllEntry FromPath(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            int? id = name.StartsWith("magic_", StringComparison.OrdinalIgnoreCase) && int.TryParse(name.AsSpan(6), out int parsed)
                ? parsed
                : null;
            FileInfo info = new(path);
            return new MagicDllEntry
            {
                FullPath = path,
                FileName = info.Name,
                MagicId = id,
                FileSize = info.Length,
                LastWriteTime = info.LastWriteTime
            };
        }
    }

    internal sealed record MagicDllImportRow(string Library, int Count, string Preview)
    {
        public string CountSummary => $"{Count} imports";
    }

    internal enum MagicDllValueEncoding
    {
        Float32,
        FloatVector,
        Int32,
        Int8
    }

    internal sealed class MagicDllValueCandidate(
        MagicDllValueEncoding encoding,
        int fileOffset,
        int rva,
        string section,
        int byteLength,
        string kind,
        string currentValue,
        string currentValueForEdit,
        string hypothesis,
        string confidence,
        string contextHex,
        int score,
        int componentCount,
        bool isPossibleTimingRisk = false,
        MagicDllRt2VerdictCatalog.VerdictKind rt2Verdict = MagicDllRt2VerdictCatalog.VerdictKind.None,
        string rt2Symptom = "",
        string rt2LedgerId = "")
    {
        public MagicDllValueEncoding Encoding { get; } = encoding;
        public int FileOffset { get; } = fileOffset;
        public int Rva { get; } = rva;
        public string Section { get; } = section;
        public int ByteLength { get; } = byteLength;
        public string Kind { get; } = kind;
        public string CurrentValue { get; } = currentValue;
        public string CurrentValueForEdit { get; } = currentValueForEdit;
        public string Hypothesis { get; } = hypothesis;
        public string Confidence { get; } = confidence;
        public string ContextHex { get; } = contextHex;
        public int Score { get; } = score;
        public int ComponentCount { get; } = componentCount;
        public bool IsPossibleTimingRisk { get; } = isPossibleTimingRisk;
        public MagicDllRt2VerdictCatalog.VerdictKind Rt2Verdict { get; } = rt2Verdict;
        public string Rt2Symptom { get; } = rt2Symptom;
        public string Rt2LedgerId { get; } = rt2LedgerId;
        public bool IsRt2Dead => Rt2Verdict is MagicDllRt2VerdictCatalog.VerdictKind.DeadNoVisual
            or MagicDllRt2VerdictCatalog.VerdictKind.DeadTransform
            or MagicDllRt2VerdictCatalog.VerdictKind.DeadSoftlock;
        public bool IsRt2ProvedTiming => Rt2Verdict == MagicDllRt2VerdictCatalog.VerdictKind.TimingProved;
        public bool PatchBlocked => IsRt2Dead;
        public bool ShowRt2DeadBadge => IsRt2Dead;
        public bool ShowRt2TimingProvedBadge => IsRt2ProvedTiming;
        public bool ShowRt2TimingRiskBadge => IsPossibleTimingRisk && !IsRt2Dead && !IsRt2ProvedTiming;
        public string Rt2DeadBadgeText =>
            string.IsNullOrWhiteSpace(Rt2Symptom)
                ? Hypothesis
                : string.Format(FFXProjectEditor.Resources.Strings.U_Md_SymptomLabel, Hypothesis, Rt2Symptom) + (string.IsNullOrEmpty(Rt2LedgerId) ? "" : $" [{Rt2LedgerId}]");
        public string Rt2TimingProvedBadgeText =>
            string.IsNullOrWhiteSpace(Rt2Symptom)
                ? Hypothesis
                : $"⏱ {Hypothesis} — {Rt2Symptom}";
        public string FileOffsetDisplay => $"file 0x{FileOffset:X8}";
        public string RvaDisplay => Rva > 0 ? $"RVA 0x{Rva:X8}" : "RVA -";
        public string ByteLengthDisplay => $"{ByteLength} byte{(ByteLength == 1 ? string.Empty : "s")}";
        public string SearchText =>
            $"{Kind} {CurrentValue} {Hypothesis} {Confidence} timing-risk:{IsPossibleTimingRisk} rt2-dead:{IsRt2Dead} rt2-timing:{IsRt2ProvedTiming} ledger:{Rt2LedgerId} {Section} {FileOffsetDisplay} {RvaDisplay} {ContextHex}";
        public string EditWatermark => Encoding == MagicDllValueEncoding.FloatVector
            ? string.Join(", ", Enumerable.Repeat("1.0", ComponentCount))
            : Encoding == MagicDllValueEncoding.Float32
                ? "1.0"
                : "60 or 0x3C";

        public byte[] EncodePatchPayload(string text)
        {
            string value = (text ?? string.Empty).Trim();
            if (value.Length == 0)
                throw new InvalidOperationException("New value is empty.");

            return Encoding switch
            {
                MagicDllValueEncoding.Float32 => BitConverter.GetBytes(float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture)),
                MagicDllValueEncoding.FloatVector => EncodeFloatVector(value),
                MagicDllValueEncoding.Int32 => BitConverter.GetBytes(ParseFlexibleInt32(value)),
                MagicDllValueEncoding.Int8 => EncodeInt8(value),
                _ => throw new InvalidOperationException("Unsupported value candidate encoding.")
            };
        }

        byte[] EncodeFloatVector(string value)
        {
            string[] parts = value.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != ComponentCount)
                throw new InvalidOperationException($"Expected {ComponentCount} float components.");

            byte[] output = new byte[ComponentCount * 4];
            for (int i = 0; i < parts.Length; i++)
            {
                float component = float.Parse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture);
                BitConverter.GetBytes(component).CopyTo(output, i * 4);
            }
            return output;
        }

        static byte[] EncodeInt8(string value)
        {
            int parsed = ParseFlexibleInt32(value);
            if (parsed < -128 || parsed > 255)
                throw new InvalidOperationException("imm8 patch must be between -128 and 255.");
            return [(byte)parsed];
        }

        static int ParseFlexibleInt32(string text)
        {
            string value = (text ?? string.Empty).Trim();
            if (value.Length == 0)
                throw new FormatException("numeric value is empty.");
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return int.Parse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (value.EndsWith("h", StringComparison.OrdinalIgnoreCase))
                return int.Parse(value[..^1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }
    }

    internal sealed record MagicDllHostReferenceRow(
        int HostOffset,
        string CandidateName,
        int FileOffset,
        int Rva,
        string Section,
        string Kind,
        string ContextHex)
    {
        public string OffsetDisplay => $"{HostOffset} / 0x{HostOffset:X}";
        public string FileOffsetDisplay => $"file 0x{FileOffset:X8}";
        public string RvaDisplay => Rva > 0 ? $"RVA 0x{Rva:X8}" : "RVA -";
        public string Summary => $"{Kind} · {Section} · {FileOffsetDisplay}";
    }

    internal sealed class MagicDllFamilySpellRow(MagicDllFamilySpellUsage usage)
    {
        public string DisplayName => usage.DisplayName;
        public string SourceFile => usage.SourceFile;
        public string OperandHex => usage.OperandHex;
        public string MoveAnimSummary => usage.MoveAnimSummary;
        public string GameplaySummary => usage.GameplaySummary;
    }

    internal sealed class MagicDllLogicalSlotRow
    {
        public required int SlotIndex { get; init; }
        public required string Kind { get; init; }
        public required string Rva { get; init; }
        public required string RoleName { get; init; }
        public required string MatchLevel { get; init; }
        public required string Confidence { get; init; }
        public required string HostOffsets { get; init; }
        public required string Pseudocode { get; init; }
        public required bool IsStub { get; init; }

        public static MagicDllLogicalSlotRow FromSlot(MagicDllLogicalSlotDecompile slot) => new()
        {
            SlotIndex = slot.SlotIndex,
            Kind = slot.Kind,
            Rva = slot.Rva,
            RoleName = slot.RoleName,
            MatchLevel = slot.MatchLevel,
            Confidence = slot.Confidence,
            HostOffsets = slot.HostOffsets.Count == 0
                ? "-"
                : string.Join(", ", slot.HostOffsets.Select(o => $"0x{o:X}")),
            Pseudocode = string.Join(Environment.NewLine, slot.Pseudocode),
            IsStub = slot.IsStub
        };
    }

    internal sealed class MagicDllFamilySiblingRow
    {
        public required int MagicId { get; init; }
        public required string MagicIdText { get; init; }
        public required string OverlaySignature { get; init; }
        public required string DllHashPrefix { get; init; }
        public required bool SameDllHash { get; init; }
        public required string SampleSpells { get; init; }

        public static MagicDllFamilySiblingRow FromMatch(MagicDllFamilySiblingMatch match) => new()
        {
            MagicId = match.MagicId,
            MagicIdText = $"magic_{match.MagicId:D4}",
            OverlaySignature = match.OverlaySignature,
            DllHashPrefix = match.DllSha256Prefix,
            SameDllHash = match.SameDllHashAsSelected,
            SampleSpells = string.Join(", ", match.SampleSpells.Select(s => s.DisplayName).Take(4))
        };
    }

    internal sealed record MagicDllWave4KernelRefRow(string Locale, string Table, int RowIndex, string Field, string RowName)
    {
        public MagicDllWave4KernelRefRow(MagicDllWave4KernelRef source)
            : this(source.Locale, source.Table, source.RowIndex, source.Field, source.RowName)
        {
        }

        public string Summary => $"{Locale}/{Table}:{RowIndex} {Field}={RowName}";
    }

    internal sealed class MagicDllSeSepRow
    {
        public int RecordIndex { get; }
        public string FileOffsetHex { get; }
        public string SeIdText { get; }
        public string WaveDataIdText { get; }
        public string RecLenText { get; }
        public string VoiceCountText { get; }

        public MagicDllSeSepRow(int index, MagicDllSoundRecordScanner.SeSepHit hit)
        {
            RecordIndex = index;
            FileOffsetHex = $"0x{hit.FileOffset:X}";
            SeIdText = hit.SeId.ToString();
            WaveDataIdText = hit.WaveDataId.ToString();
            RecLenText = hit.RecLen.ToString();
            VoiceCountText = hit.VoiceCount.ToString();
        }
    }

    // Jarvis-PPP (2026-07-07, todo 14): JSON model + bindable row for the PPP opcode
    // browser. Loads from docs/reverse/magic_dlls/PPP_HANDLER_NAMES.json (36 entries).
    // Categories are derived from PPP_OPCODE_TABLE.md §1.
    internal sealed class PppHandlerNamesDoc
    {
        public List<PppHandlerEntry> Entries { get; set; } = new();
        public int Total { get; set; }
        public string TableBase { get; set; } = string.Empty;
        public string Imagebase { get; set; } = string.Empty;
        public int EntrySize { get; set; }
        public string Notes { get; set; } = string.Empty;
    }

    internal sealed class PppHandlerEntry
    {
        public int Index { get; set; }
        public string OpcodeByte { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? HandlerRvaM86 { get; set; }
        public string? HandlerRvaM87 { get; set; }
    }

    internal sealed class PppOpcodeRow
    {
        static readonly Dictionary<int, string> CategoryMap = new()
        {
            [0] = "thread resolution", [1] = "thread resolution", [2] = "thread resolution",
            [3] = "gravity target",
            [4] = "acceleration", [5] = "angular accel", [6] = "scale accel", [7] = "color accel",
            [8] = "gravity effect",
            [9] = "movement", [10] = "angular movement", [11] = "scale movement", [12] = "color movement",
            [13] = "point",
            [14] = "angle",
            [15] = "scale",
            [16] = "color",
            [17] = "direction",
            [18] = "random",
            [19] = "matrix", [20] = "matrix", [21] = "matrix",
            [22] = "draw matrix", [23] = "draw matrix front",
            [24] = "z-correct shape",
            [25] = "thread type",
            [26] = "thread shift",
            [27] = "thread",
            [28] = "draw model", [29] = "draw model TS",
            [30] = "draw shape", [31] = "draw shape ext",
            [32] = "point appearance",
            [33] = "vertex appearance",
            [34] = "born random 2", [35] = "born random 3",
        };

        public required int Index { get; init; }
        public required string OpcodeByte { get; init; }
        public required string Name { get; init; }
        public required string HandlerRvaM86 { get; init; }
        public required string HandlerRvaM87 { get; init; }
        public required string Category { get; init; }
        public required bool IsHostResolved { get; init; }

        public string HandlerRvaDisplay => IsHostResolved
            ? "NULL (host-resolved)"
            : HandlerRvaM86;

        public string Signature => IsHostResolved
            ? $"// {Name} — host-resolved (NULL handler in magic DLL).\n// Handler lives in FFX.exe, looked up at runtime via FFX_MagicHost_ClassifyPppOpcodeByte."
            : $"// {Name} thunk (magic_0086.dll)\nint {Name}_thunk(void) {{\n    return (*(int (**)(void))(host_context + {HostContextOffset}))();\n}}";

        public string HostContextOffset => Index switch
        {
            0 => "1880", 1 => "1664", 2 => "2724", 3 => "420",
            4 => "164", 5 => "172", 6 => "180", 7 => "188",
            8 => "428", 9 => "196", 10 => "204", 11 => "212",
            12 => "220", 13 => "400", 14 => "408", 15 => "228",
            16 => "236", 17 => "244", 25 => "456", 26 => "496",
            27 => "328", 29 => "344", 30 => "156", 31 => "460",
            32 => "556", 33 => "368", 34 => "376",
            _ => "(host-resolved)",
        };

        public static PppOpcodeRow FromEntry(PppHandlerEntry e) => new()
        {
            Index = e.Index,
            OpcodeByte = e.OpcodeByte,
            Name = e.Name,
            HandlerRvaM86 = e.HandlerRvaM86 ?? "(null)",
            HandlerRvaM87 = e.HandlerRvaM87 ?? "(null)",
            Category = CategoryMap.TryGetValue(e.Index, out string? cat) ? cat : "unknown",
            IsHostResolved = string.IsNullOrEmpty(e.HandlerRvaM86),
        };
    }
}
