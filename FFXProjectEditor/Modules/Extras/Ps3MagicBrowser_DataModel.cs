using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Ps3;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.Extras
{
    internal sealed partial class Ps3MagicTextureRow : ObservableObject
    {
        public required Ps3MagicTexture Texture { get; init; }

        public string Name => Texture.Name;
        public string RelativePath => Texture.RelativePath;
        public string NameDimSummary => Texture.NameDimSummary;
        public string SizeSummary => Texture.SizeSummary;

        [ObservableProperty] private Bitmap? thumbnail;
        [ObservableProperty] private string formatSummary = "...";
        [ObservableProperty] private string stateLabel = "pending";
        [ObservableProperty] private string decodeNote = "-";
        [ObservableProperty] private string decodedDimSummary = "-";
        public Ps3PhyreDecode? LastDecode { get; private set; }

        public bool Decoded { get; private set; }

        public void ApplyDecode(Ps3PhyreDecode decode)
        {
            LastDecode = decode;
            Thumbnail = decode.Bitmap;
            FormatSummary = decode.Format;
            StateLabel = decode.StateLabel;
            DecodeNote = decode.Note;
            DecodedDimSummary = decode.DimensionSummary;
            Decoded = true;
        }
    }

    internal partial class Ps3MagicBrowser_DataModel : ObservableObject
    {
        // Defensive cap on per-folder thumbnail decodes. The whole magic tree maxes out at 48
        // textures in a single folder, so in practice every folder decodes fully.
        const int ThumbnailDecodeCap = 64;
        static string DefaultMagicDllRoot => PortablePathResolver.MagicFilesRoot("FFX") ?? string.Empty;

        readonly List<Ps3MagicEntry> allMagic = [];
        int? requestedMagicId;

        // Bumped on every magic-folder selection so a slow background decode of a previous folder
        // can't write its thumbnails onto the new selection's rows.
        volatile int decodeGeneration;
        // Set when a folder switch auto-selects row[0]; tells OnSelectedTextureChanged NOT to sync-decode it
        // on the UI thread (the background pass decodes row[0] first and surfaces the preview).
        bool _suppressFirstRowSyncDecode;

        public ObservableCollection<Ps3MagicEntry> MagicEntries { get; } = new();
        public ObservableCollection<Ps3MagicTextureRow> Textures { get; } = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectedMagicTitle))]
        [NotifyPropertyChangedFor(nameof(SelectedMagicPath))]
        [NotifyPropertyChangedFor(nameof(SelectedMagicCounts))]
        [NotifyPropertyChangedFor(nameof(HasSelectedMagic))]
        [NotifyPropertyChangedFor(nameof(RuntimeDllPath))]
        [NotifyPropertyChangedFor(nameof(RuntimeDllExists))]
        [NotifyPropertyChangedFor(nameof(HasRuntimeDll))]
        private Ps3MagicEntry? selectedMagic;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(RuntimeDllSummary))]
        [NotifyPropertyChangedFor(nameof(RuntimeDllOverlaySummary))]
        private MagicDllInspection? selectedRuntimeDllInspection;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectedTextureName))]
        [NotifyPropertyChangedFor(nameof(SelectedTextureRelativePath))]
        [NotifyPropertyChangedFor(nameof(SelectedTextureFacts))]
        [NotifyPropertyChangedFor(nameof(SelectedTextureState))]
        [NotifyPropertyChangedFor(nameof(SelectedTextureNote))]
        [NotifyPropertyChangedFor(nameof(HasSelectedTexture))]
        private Ps3MagicTextureRow? selectedTexture;

        public bool HasSelectedMagic => SelectedMagic != null;
        public bool HasSelectedTexture => SelectedTexture != null;

        [ObservableProperty] private Bitmap? selectedPreviewBitmap;
        [ObservableProperty] private Bitmap? compositePreviewBitmap;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowSingle))]
        private bool showComposite;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CompositeModeLabel))]
        private bool compositeAdditive = true;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PlayButtonLabel))]
        private bool isPlaying;

        [ObservableProperty] private string searchText = string.Empty;
        [ObservableProperty] private string sourceRootSummary = "Detecting ps3data root...";
        [ObservableProperty] private string overviewSummary = "-";
        [ObservableProperty] private string authoringStatus = "Texture I/O idle.";
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ColorTransformSummary))]
        private double colorRedScale = 1.0;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ColorTransformSummary))]
        private double colorGreenScale = 1.0;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ColorTransformSummary))]
        private double colorBlueScale = 1.0;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ColorTransformSummary))]
        private double colorAlphaScale = 1.0;

        public string HeaderSummary =>
            "HD magic-effect texture viewer. Decodes PhyreEngine .dds.phyre (mip0) straight from the " +
            "ps3data\\magic tree using the proved RYHPT container model.";

        public string ReadonlyScopeSummary =>
            "Allowed now: browse magic_#### folders, decode + preview mip0, extract DDS, and repack same-format/same-size mip0. " +
            "Blocked: full mip chains, atlas-page reassembly, timeline/compiler, and any unknown-class decode (kept metadata-only).";

        public string MagicCountSummary => $"{MagicEntries.Count} visible / {allMagic.Count} magic folders";
        public string SelectedMagicTitle => SelectedMagic?.DisplayTitle ?? Strings.F2_select_a_magic_effect_4e1a9572;
        public string SelectedMagicPath => SelectedMagic?.FullPath ?? "-";
        public string SelectedMagicCounts => SelectedMagic == null ? "-" : $"{SelectedMagic.TextureCount} textures";
        public string RuntimeDllPath => SelectedMagic == null ? "-" : ResolveRuntimeDllPath(SelectedMagic);
        public bool RuntimeDllExists => SelectedMagic != null && File.Exists(RuntimeDllPath);
        public bool HasRuntimeDll => RuntimeDllExists;
        public string RuntimeDllSummary => SelectedRuntimeDllInspection == null
            ? (SelectedMagic == null ? "-" : RuntimeDllExists ? "DLL exists; not inspected yet." : "Runtime DLL missing for this magic_####.")
            : $"{SelectedRuntimeDllInspection.MachineName} {(SelectedRuntimeDllInspection.IsPe32Plus ? "PE32+" : "PE32")} · sections {SelectedRuntimeDllInspection.Sections.Count} · exports {SelectedRuntimeDllInspection.Exports.Count} · imports {SelectedRuntimeDllInspection.Imports.Sum(i => i.Imports.Count)} · strings {SelectedRuntimeDllInspection.Strings.Count}";
        public string RuntimeDllOverlaySummary => SelectedRuntimeDllInspection?.OverlayEvidence == null
            ? "Overlay CSV row not attached for this ID; use Magic DLLs (FFX) for raw PE details."
            : $"GetEffectOverlayTable {SelectedRuntimeDllInspection.OverlayEvidence.GetEffectOverlayEa} · InitMagicPRX {SelectedRuntimeDllInspection.OverlayEvidence.InitMagicPrxEa} · slots {SelectedRuntimeDllInspection.OverlayEvidence.NonzeroSlotCount}/{SelectedRuntimeDllInspection.OverlayEvidence.SlotCountRead}";

        public string SelectedTextureName => SelectedTexture?.Name ?? Strings.F2_select_a_texture_5b6d700e;
        public string SelectedTextureRelativePath => SelectedTexture?.RelativePath ?? "-";
        public string SelectedTextureFacts => SelectedTexture == null
            ? "-"
            : $"name dims {SelectedTexture.NameDimSummary} · header dims {SelectedTexture.DecodedDimSummary} · {SelectedTexture.FormatSummary} · {SelectedTexture.SizeSummary}";
        public string SelectedTextureState => SelectedTexture?.StateLabel ?? "-";
        public string SelectedTextureNote => SelectedTexture?.DecodeNote ?? "-";

        public bool ShowSingle => !ShowComposite;
        public string PlayButtonLabel => IsPlaying ? "⏸ Stop" : "▶ Cycle";
        public string CompositeModeLabel => CompositeAdditive ? "Additive" : "Alpha";
        public string SynthesizedNote => Strings.F2_synthesized_preview_only_composite_blend_0c3033bb;
        public string ColorTransformSummary => $"R x{ColorRedScale:0.00} · G x{ColorGreenScale:0.00} · B x{ColorBlueScale:0.00} · A x{ColorAlphaScale:0.00}";
        public string SuggestedDdsFileName => SelectedTexture == null
            ? "texture.extracted.dds"
            : SelectedTexture.Texture.Name.EndsWith(".dds.phyre", StringComparison.OrdinalIgnoreCase)
                ? SelectedTexture.Texture.Name[..^".dds.phyre".Length] + ".extracted.dds"
                : SelectedTexture.Texture.Name + ".extracted.dds";
        public string SuggestedPhyreFileName => SelectedTexture?.Texture.Name ?? "texture.dds.phyre";
        public int? SelectedMagicId => TryParseMagicId(SelectedMagic, out int id) ? id : null;

        public Ps3MagicBrowser_DataModel(int? initialMagicId = null)
        {
            requestedMagicId = NormalizeMagicId(initialMagicId);
            if (requestedMagicId.HasValue)
                searchText = requestedMagicId.Value.ToString("D4");

            Refresh();
        }

        public void Refresh()
        {
            allMagic.Clear();
            MagicEntries.Clear();
            Textures.Clear();
            SelectedMagic = null;
            SelectedTexture = null;
            SelectedPreviewBitmap = null;

            var scanRoots = new List<(string MagicRoot, string SourceLabel)>();
            string? ps3Root = Project_Service.Instance.Path_Ps3DataRoot;
            if (!string.IsNullOrWhiteSpace(ps3Root))
            {
                string extractMagic = Path.Combine(ps3Root, "magic");
                if (Directory.Exists(extractMagic))
                    scanRoots.Add((extractMagic, "ps3data"));
            }

            string? modsMagic = Project_Service.Instance.Path_ModsPs3MagicRoot;
            if (!string.IsNullOrWhiteSpace(modsMagic) && Directory.Exists(modsMagic))
                scanRoots.Add((modsMagic, "mods"));

            if (scanRoots.Count == 0)
            {
                SourceRootSummary = Strings.F2_no_ps3data_or_mods_magic_roots_detected__59cfb464;
                OverviewSummary = "-";
                OnPropertyChanged(nameof(MagicCountSummary));
                return;
            }

            SourceRootSummary = string.Join(" + ", scanRoots.Select(root => root.MagicRoot));
            allMagic.AddRange(Ps3MagicTextureReader.ScanAllMagicFolderRoots(scanRoots));
            int modsOnly = allMagic.Count(entry => string.Equals(entry.SourceRootLabel, "mods", StringComparison.OrdinalIgnoreCase));
            OverviewSummary = $"{allMagic.Count} magic folders · {allMagic.Sum(magic => magic.TextureCount):N0} .dds.phyre textures"
                + (modsOnly > 0 ? $" · {modsOnly} from mods overlay" : string.Empty);

            ApplyFilter();
        }

        static string ResolveRuntimeDllPath(Ps3MagicEntry magic)
        {
            string? dllRoot = Project_Service.Instance.Path_MagicDllRoot;
            if (!string.IsNullOrWhiteSpace(dllRoot))
            {
                string resolved = Path.Combine(dllRoot, $"{magic.FolderName}.dll");
                if (File.Exists(resolved))
                    return resolved;
            }

            return Path.Combine(DefaultMagicDllRoot, $"{magic.FolderName}.dll");
        }

        partial void OnSearchTextChanged(string value) => ApplyFilter();

        void ApplyFilter()
        {
            IEnumerable<Ps3MagicEntry> query = allMagic;
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                query = query.Where(magic =>
                    magic.FolderName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                    || magic.Id.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                    || magic.BestEffortNameDisplay.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                    || magic.Textures.Any(texture => texture.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)));
            }

            MagicEntries.Clear();
            foreach (Ps3MagicEntry magic in query)
                MagicEntries.Add(magic);

            OnPropertyChanged(nameof(MagicCountSummary));
            Ps3MagicEntry? requested = null;
            if (requestedMagicId.HasValue)
                requested = MagicEntries.FirstOrDefault(magic => TryParseMagicId(magic, out int id) && id == requestedMagicId.Value);

            SelectedMagic = requested ?? MagicEntries.FirstOrDefault();
            if (requested != null)
                requestedMagicId = null;
        }

        partial void OnShowCompositeChanged(bool value)
        {
            if (value)
                RebuildCompositePreview();
        }

        partial void OnCompositeAdditiveChanged(bool value)
        {
            RebuildCompositePreview();
        }

        partial void OnSelectedMagicChanged(Ps3MagicEntry? value)
        {
            int generation = ++decodeGeneration;

            Textures.Clear();
            SelectedTexture = null;
            SelectedPreviewBitmap = null;
            CompositePreviewBitmap = null;
            SelectedRuntimeDllInspection = null;
            if (value == null)
                return;

            InspectSelectedRuntimeDll();

            // Add every row up front showing its honest "pending" label, then stream the decoded
            // thumbnails in off the UI thread so selecting a heavy folder no longer freezes the UI.
            List<Ps3MagicTextureRow> rows = value.Textures
                .Select(texture => new Ps3MagicTextureRow { Texture = texture })
                .ToList();

            foreach (Ps3MagicTextureRow row in rows)
                Textures.Add(row);

            // Let the background pass decode row[0] (it surfaces the preview once it lands); don't sync-decode
            // it on the UI thread via OnSelectedTextureChanged — THAT was the per-folder freeze.
            _suppressFirstRowSyncDecode = true;
            SelectedTexture = Textures.FirstOrDefault();

            _ = Task.Run(() => DecodeRows(rows, generation));
        }

        void InspectSelectedRuntimeDll()
        {
            SelectedRuntimeDllInspection = null;
            if (SelectedMagic == null || !RuntimeDllExists)
                return;

            try
            {
                SelectedRuntimeDllInspection = MagicDllDecompiler.Inspect(RuntimeDllPath, Directory.GetCurrentDirectory());
            }
            catch (Exception ex)
            {
                AuthoringStatus = $"Runtime DLL inspect failed: {ex.Message}";
            }
        }

        void DecodeRows(List<Ps3MagicTextureRow> rows, int generation)
        {
            int decoded = 0;
            foreach (Ps3MagicTextureRow row in rows)
            {
                // The user moved to another folder (or refreshed); abandon this stale pass.
                if (generation != decodeGeneration)
                    return;

                // Already decoded (e.g. the user clicked this row and it decoded on demand) — don't redo it.
                if (row.Decoded)
                    continue;

                if (decoded >= ThumbnailDecodeCap)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (generation != decodeGeneration)
                            return;
                        row.FormatSummary = "-";
                        row.StateLabel = "Not decoded (cap)";
                        row.DecodeNote = $"Skipped to keep the folder responsive (>{ThumbnailDecodeCap} textures). Select it to decode on demand.";
                    });
                    continue;
                }

                Ps3PhyreDecode decode = Ps3MagicTextureReader.Decode(row.Texture.FullPath);
                decoded++;
                Dispatcher.UIThread.Post(() =>
                {
                    if (generation != decodeGeneration)
                        return;
                    row.ApplyDecode(decode);
                    // Surface the first thumbnail into the large preview once it lands, since the
                    // selection fired before any decode existed.
                    if (ReferenceEquals(SelectedTexture, row) && SelectedPreviewBitmap == null)
                        SelectedPreviewBitmap = row.Thumbnail;
                    if (ShowComposite)
                        RebuildCompositePreview();
                });
            }
        }

        partial void OnSelectedTextureChanged(Ps3MagicTextureRow? value)
        {
            if (value == null)
            {
                SelectedPreviewBitmap = null;
                return;
            }

            // On a folder switch the background pass decodes this (first) row and surfaces the preview — don't
            // block the UI thread doing a full decode here (that defeated the off-thread design).
            if (_suppressFirstRowSyncDecode)
            {
                _suppressFirstRowSyncDecode = false;
                SelectedPreviewBitmap = value.Thumbnail;   // null until the background pass fills row[0] (decoded first)
                return;
            }

            if (!value.Decoded)
                value.ApplyDecode(Ps3MagicTextureReader.Decode(value.Texture.FullPath));

            SelectedPreviewBitmap = value.Thumbnail;
        }

        void RebuildCompositePreview()
        {
            List<Ps3PhyreDecode> layers = Textures
                .Select(row => row.LastDecode)
                .Where(decode => decode?.Ok == true && decode.Bgra != null && decode.Width > 0 && decode.Height > 0)
                .Cast<Ps3PhyreDecode>()
                .ToList();

            if (layers.Count == 0)
            {
                CompositePreviewBitmap = null;
                return;
            }

            int width = layers.Max(layer => layer.Width);
            int height = layers.Max(layer => layer.Height);
            byte[] output = new byte[width * height * 4];

            foreach (Ps3PhyreDecode layer in layers)
            {
                byte[] src = layer.Bgra!;
                int x0 = (width - layer.Width) / 2;
                int y0 = (height - layer.Height) / 2;
                for (int y = 0; y < layer.Height; y++)
                {
                    int dstRow = (y + y0) * width;
                    int srcRow = y * layer.Width;
                    for (int x = 0; x < layer.Width; x++)
                    {
                        int dst = (dstRow + x + x0) * 4;
                        int s = (srcRow + x) * 4;
                        byte sa = src[s + 3];
                        if (sa == 0)
                            continue;

                        if (CompositeAdditive)
                        {
                            double a = sa / 255.0;
                            output[dst + 0] = ClampByte(output[dst + 0] + src[s + 0] * a);
                            output[dst + 1] = ClampByte(output[dst + 1] + src[s + 1] * a);
                            output[dst + 2] = ClampByte(output[dst + 2] + src[s + 2] * a);
                            output[dst + 3] = Math.Max(output[dst + 3], sa);
                        }
                        else
                        {
                            double a = sa / 255.0;
                            double inv = 1.0 - a;
                            output[dst + 0] = ClampByte(src[s + 0] * a + output[dst + 0] * inv);
                            output[dst + 1] = ClampByte(src[s + 1] * a + output[dst + 1] * inv);
                            output[dst + 2] = ClampByte(src[s + 2] * a + output[dst + 2] * inv);
                            output[dst + 3] = ClampByte(sa + output[dst + 3] * inv);
                        }
                    }
                }
            }

            CompositePreviewBitmap = Ps3MagicTextureReader.BuildBitmapFromBgra(output, width, height);
        }

        static byte ClampByte(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);

        public void ExtractSelectedDds(string outputPath)
        {
            if (SelectedTexture == null)
            {
                AuthoringStatus = Strings.F2_select_a_texture_first_bf26b718;
                return;
            }

            try
            {
                Ps3PhyreExtractResult result = Ps3MagicTextureWriter.ExtractMip0Dds(SelectedTexture.Texture.FullPath, outputPath);
                AuthoringStatus = $"Extracted DDS: {result.SourceLayout.Summary} -> {result.OutputPath}";
            }
            catch (Exception ex)
            {
                AuthoringStatus = $"Extract failed: {ex.Message}";
            }
        }

        public void RepackSelectedDds(string editedDdsPath, string outputPath)
        {
            if (SelectedTexture == null)
            {
                AuthoringStatus = Strings.F2_select_a_texture_first_bf26b718;
                return;
            }

            try
            {
                string sourcePath = SelectedTexture.Texture.FullPath;
                if (!Ps3MagicTextureWriter.TryReadMip0Layout(sourcePath, out Ps3PhyreMip0Layout? layout, out string note) || layout == null)
                    throw new InvalidDataException(note);

                byte[] payload = Ps3MagicTextureWriter.ReadCompatibleMip0Payload(editedDdsPath, layout, out string payloadKind);
                string? backupPath = null;
                if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
                {
                    backupPath = outputPath + ".backup_before_repack_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    File.Copy(sourcePath, backupPath, overwrite: false);
                }

                Ps3PhyreWriteResult result = Ps3MagicTextureWriter.WriteSameShapeMip0(sourcePath, payload, outputPath);
                AuthoringStatus = result.SameLayout
                    ? $"Repacked {payloadKind}: {result.OutputLayout.Summary} -> {result.OutputPath}" + (backupPath == null ? string.Empty : $" · backup {backupPath}")
                    : $"Repack wrote but layout drifted: {result.OutputPath}";

                if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
                {
                    SelectedTexture.ApplyDecode(Ps3MagicTextureReader.Decode(sourcePath));
                    SelectedPreviewBitmap = SelectedTexture.Thumbnail;
                }
            }
            catch (Exception ex)
            {
                AuthoringStatus = $"Repack failed: {ex.Message}";
            }
        }

        public void RecolorSelectedTexture(string outputPath)
        {
            if (SelectedTexture == null)
            {
                AuthoringStatus = Strings.F2_select_a_texture_first_bf26b718;
                return;
            }

            try
            {
                string sourcePath = SelectedTexture.Texture.FullPath;
                string? backupPath = null;
                if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
                {
                    backupPath = outputPath + ".backup_before_recolor_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    File.Copy(sourcePath, backupPath, overwrite: false);
                }

                Ps3MagicRecolorResult result = Ps3MagicTextureColorWriter.WriteRecoloredMip0(sourcePath, outputPath, CurrentColorTransform());
                AuthoringStatus = $"Recolored {result.Layout.Format} {result.RecoloredBlocksOrPixels} blocks/pixels -> {result.OutputPath}" + (backupPath == null ? string.Empty : $" · backup {backupPath}");

                if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
                {
                    SelectedTexture.ApplyDecode(Ps3MagicTextureReader.Decode(sourcePath));
                    SelectedPreviewBitmap = SelectedTexture.Thumbnail;
                    RebuildCompositePreview();
                }
            }
            catch (Exception ex)
            {
                AuthoringStatus = $"Recolor failed: {ex.Message}";
            }
        }

        public void RecolorSelectedMagicFolder(string outputFolder)
        {
            if (SelectedMagic == null)
            {
                AuthoringStatus = Strings.F2_select_a_magic_folder_first_1b2c2679;
                return;
            }

            try
            {
                int ok = 0;
                int failed = 0;
                foreach (Ps3MagicTexture texture in SelectedMagic.Textures)
                {
                    try
                    {
                        string rel = Path.GetRelativePath(SelectedMagic.FullPath, texture.FullPath);
                        string outputPath = Path.Combine(outputFolder, rel);
                        if (string.Equals(Path.GetFullPath(texture.FullPath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
                        {
                            string backupPath = outputPath + ".backup_before_recolor_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                            File.Copy(texture.FullPath, backupPath, overwrite: false);
                        }

                        Ps3MagicTextureColorWriter.WriteRecoloredMip0(texture.FullPath, outputPath, CurrentColorTransform());
                        ok++;
                    }
                    catch
                    {
                        failed++;
                    }
                }

                AuthoringStatus = $"Recolored folder {SelectedMagic.FolderName}: {ok} textures written to {outputFolder}" + (failed == 0 ? string.Empty : $" · {failed} failed/skipped");
            }
            catch (Exception ex)
            {
                AuthoringStatus = $"Folder recolor failed: {ex.Message}";
            }
        }

        Ps3MagicColorTransform CurrentColorTransform() =>
            new(ColorRedScale, ColorGreenScale, ColorBlueScale, ColorAlphaScale);

        public void DecompileSelectedRuntimeDll(string outputDir)
        {
            if (SelectedMagic == null)
            {
                AuthoringStatus = Strings.F2_select_a_magic_folder_first_1b2c2679;
                return;
            }
            if (!RuntimeDllExists)
            {
                AuthoringStatus = $"Runtime DLL missing: {RuntimeDllPath}";
                return;
            }

            try
            {
                MagicDllDecompileResult result = MagicDllDecompiler.DecompileToFolder(RuntimeDllPath, outputDir, Directory.GetCurrentDirectory());
                AuthoringStatus = $"Runtime DLL extracted: {result.MarkdownPath}";
                SelectedRuntimeDllInspection = result.Inspection;
            }
            catch (Exception ex)
            {
                AuthoringStatus = $"Runtime DLL extract failed: {ex.Message}";
            }
        }

        public void AdvanceLayer()
        {
            if (Textures.Count == 0)
                return;
            int index = SelectedTexture == null ? -1 : Textures.IndexOf(SelectedTexture);
            SelectedTexture = Textures[(index + 1) % Textures.Count];
        }

        public void SelectMagicId(int magicId)
        {
            requestedMagicId = NormalizeMagicId(magicId);
            if (!requestedMagicId.HasValue)
                return;

            SearchText = requestedMagicId.Value.ToString("D4");
        }

        static int? NormalizeMagicId(int? magicId) => magicId is >= 0 and <= 9999 ? magicId : null;

        static bool TryParseMagicId(Ps3MagicEntry? magic, out int id)
        {
            id = -1;
            return magic != null
                && int.TryParse(magic.Id, out id)
                && id is >= 0 and <= 9999;
        }
    }
}
