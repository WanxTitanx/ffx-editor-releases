using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.Files;
using FFXProjectEditor.Converters;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.FfxLib.Monster;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Modules.Common.ViewerShell;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Services;
using FFXProjectEditor.Utils.Editing;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;

namespace FFXProjectEditor.Modules.MonEditor
{
    internal partial class MonEditor_DataModel : ObservableObject
    {
        Monster_File monsterFile;
        string MonsterPath { get; }
        MonsterEnglishLocalizationBinding? englishLocalizationBinding;

        public MonEditorSelector_DataModel SelectorDM { get; }

        [ObservableProperty] private MonsterStatSheet_Wrapper monsterStatSheet;
        [ObservableProperty] private MonsterLoot_Wrapper monsterLoot;
        [ObservableProperty] private ByteSnapshotEditorSession editSession;
        [ObservableProperty] private bool mirrorToBatchOnSave;
        [ObservableProperty] private BatchMirrorScopeOption? selectedBatchMirrorScope;

        public BattleModelPicker_Wrapper Model1Picker { get; } = new();
        public BattleModelPicker_Wrapper Model2Picker { get; } = new();
        public IReadOnlyList<CtbIconOption> CtbIconOptions { get; } = CtbIcon_Dictionary.Instance
            .OrderBy(pair => pair.Key)
            .Select(pair => new CtbIconOption(pair.Key, pair.Value))
            .ToList();

        [ObservableProperty] private CtbIconOption? selectedCtbIconOption;
        [ObservableProperty] private string modelGuidanceText = string.Empty;
        [ObservableProperty] private string modelFusionSummary = string.Empty;
        [ObservableProperty] private string modelPreviewCatalogId = string.Empty;
        [ObservableProperty] private string modelPreviewStatus = "Preview loads when a catalog match is found.";

        // Last resolved monster-studio id ("1015"). On Linux/macOS the URL build is deferred to
        // the external-preview button click, so this id must survive past RefreshModelPanel.
        private string? _modelPreviewIdHex;
        [ObservableProperty] private string? modelPreviewUrl;

        public List<string> CategoryOptions => new GameCategory_Converter().Options.Values.ToList();
        public ObservableCollection<BatchMirrorScopeOption> BatchMirrorScopeOptions { get; } = new();

        public MonEditor_DataModel(Monster_File sourceMonsterFile, string monsterPath, MonEditorSelector_DataModel selectorDM)
        {
            monsterFile = sourceMonsterFile;
            MonsterPath = monsterPath;
            SelectorDM = selectorDM;
            BuildMirrorScopeOptions();

            LoadFromMonsterFile(sourceMonsterFile);
            EditSession = new ByteSnapshotEditorSession(
                CaptureSessionSnapshot,
                RestoreFromBytes,
                PersistBytes,
                "monster data",
                CaptureSessionSnapshot());

            Model1Picker.PropertyChanged += ModelPicker_PropertyChanged;
            Model2Picker.PropertyChanged += ModelPicker_PropertyChanged;
        }

        public void ApplyVariantMainModelPair()
        {
            if (!BattleModel_Dictionary.TryGet(Model2Picker.Value, out BattleModel_Dictionary.Entry? variant) ||
                !variant.MainModelId.HasValue)
            {
                return;
            }

            Model1Picker.Value = (short)variant.MainModelId.Value;
            MonsterStatSheet.ModelId = Model1Picker.Value;
            MonsterStatSheet.Model2Id = Model2Picker.Value;
            RefreshModelPanel();
            EditSession?.NotifyPotentialMutation();
        }

        /// <summary>True on Windows (WebView2 embed exists); false on Linux/macOS where the
        /// preview panel shows a fallback card and opens the same URL in the system browser.</summary>
        public bool IsEmbeddedModelPreviewSupported =>
            Modules.Common.ViewerShell.ExternalBrowserLauncher.IsEmbeddedViewerSupported;

        public bool IsExternalModelPreview => !IsEmbeddedModelPreviewSupported;

        public void OpenModelPreviewInBrowser()
        {
            // Linux/macOS: RefreshModelPanel keeps ModelPreviewUrl null (no hidden listener behind
            // the fallback card). Build the loopback URL on click — the external browser is the
            // consumer, so the server starts exactly when the user asks for the preview.
            string? url = ModelPreviewUrl;
            if (string.IsNullOrWhiteSpace(url) && _modelPreviewIdHex != null)
            {
                url = ViewerHubService.BuildUrl(
                    "monster-studio", "monster=" + _modelPreviewIdHex, forExternalBrowser: true);
                if (url == null)
                {
                    ModelPreviewStatus = ViewerHubService.StatusText;
                    return;
                }
                ModelPreviewUrl = url;
            }
            if (string.IsNullOrWhiteSpace(url))
                return;

            Modules.Common.ViewerShell.ExternalBrowserLauncher.Open(url);
        }

        void ModelPicker_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(BattleModelPicker_Wrapper.Value))
                return;

            if (sender == Model1Picker)
                MonsterStatSheet.ModelId = Model1Picker.Value;
            else if (sender == Model2Picker)
                MonsterStatSheet.Model2Id = Model2Picker.Value;

            RefreshModelPanel();
            EditSession?.NotifyPotentialMutation();
        }

        partial void OnSelectedCtbIconOptionChanged(CtbIconOption? value)
        {
            if (value != null)
                MonsterStatSheet.CtbIconId = value.Id;

            EditSession?.NotifyPotentialMutation();
        }

        /// <summary>Resolves noclip-compatible monster catalog ids without starting the viewer server.</summary>
        private static string? ResolveModelPreviewId(BattleModelCatalogBridge.ModelPreviewPlan plan)
        {
            return CatalogIdToModelIdHex(plan.CatalogId1) ?? CatalogIdToModelIdHex(plan.CatalogId2);
        }

        /// <summary>"m021" → 0x1015 (hex, sem 0x). Aceita também hex puro ("1015").</summary>
        private static string? CatalogIdToModelIdHex(string? catalogId)
        {
            if (string.IsNullOrWhiteSpace(catalogId))
                return null;
            string c = catalogId.Trim();
            if (c.Length >= 2 && (c[0] == 'm' || c[0] == 'M') && int.TryParse(c.Substring(1), out int m) && m >= 0)
                return (0x1000 + m).ToString("X4");
            if (c.Length >= 3 && int.TryParse(c, System.Globalization.NumberStyles.HexNumber, null, out int hex) && hex >= 0x1000)
                return hex.ToString("X4");
            return null;
        }

        void RefreshModelPanel()
        {
            ModelGuidanceText = BattleModelCatalogBridge.BuildModelGuidance(
                MonsterStatSheet.ModelId,
                MonsterStatSheet.Model2Id);

            short slotId = TryGetMonsterFileIndex(out int monsterFileIndex)
                ? (short)monsterFileIndex
                : (short)-1;

            BattleModelCatalogBridge.ModelPreviewPlan plan = BattleModelCatalogBridge.BuildPreviewPlan(
                MonsterStatSheet.ModelId,
                MonsterStatSheet.Model2Id,
                slotId);

            ModelFusionSummary =
                $"Model1: {plan.Model1BattleLabel}" + Environment.NewLine +
                $"Model2: {plan.Model2BattleLabel}";

            ModelPreviewCatalogId = plan.CatalogId2 ?? plan.CatalogId1 ?? string.Empty;
            // Resolved eagerly (pure catalog math — no server start) so the Linux external-preview
            // button can build the loopback URL lazily on click.
            string? modelIdHex = ResolveModelPreviewId(plan);
            _modelPreviewIdHex = modelIdHex;
            // Platform capability is independent from catalog conversion. Check it first so every
            // Linux preview attempt reports the explicit localized unavailability state, including
            // fixed s### catalogs that Monster Studio cannot route today.
            if (!WebView2Host.IsSupported)
            {
                ModelPreviewUrl = null;
                ModelPreviewStatus = Strings.U_Vh_EmbeddedViewerUnavailableTitle;
                return;
            }

            if (string.IsNullOrWhiteSpace(plan.CatalogId1) && string.IsNullOrWhiteSpace(plan.CatalogId2))
            {
                ModelPreviewUrl = null;
                ModelPreviewStatus = "No catalog match for this model pair yet. Load a project to index monster slots, or open the full Model Viewer.";
                return;
            }

            if (modelIdHex == null)
            {
                ModelPreviewUrl = null;
                ModelPreviewStatus = Strings.U_Vh_ModelPreviewRouteUnavailable;
                return;
            }

            ModelPreviewUrl = ViewerHubService.BuildUrl("monster-studio", "monster=" + modelIdHex);
            if (ModelPreviewUrl == null)
            {
                // BuildUrl owns server/configuration failures after the platform and catalog gates.
                ModelPreviewStatus = ViewerHubService.StatusText;
                return;
            }

            if (plan.UsesFusionPair && !plan.SameCatalogAsset)
            {
                ModelPreviewStatus =
                    $"Fusion preview: {plan.CatalogId1} (base) + {plan.CatalogId2} (variant) · T-pose, no animation.";
            }
            else if (plan.UsesFusionPair && plan.SameCatalogAsset)
            {
                ModelPreviewStatus =
                    $"Fusion preview: {plan.CatalogId1} · Model1 base + Model2 variant share this mesh asset · T-pose.";
            }
            else
            {
                string only = plan.CatalogId2 ?? plan.CatalogId1!;
                ModelPreviewStatus = $"T-pose preview: {only} · variant id has no separate glTF — showing nearest base mesh.";
            }
        }

        void SyncModelPickersFromStatSheet()
        {
            Model1Picker.Value = MonsterStatSheet.ModelId;
            Model2Picker.Value = MonsterStatSheet.Model2Id;
            Model1Picker.RefreshOptions();
            Model2Picker.RefreshOptions();
            SelectedCtbIconOption = CtbIconOptions.FirstOrDefault(option => option.Id == MonsterStatSheet.CtbIconId)
                                    ?? CtbIconOptions.FirstOrDefault(option => option.Id == 20);
            RefreshModelPanel();
        }

        public void Save() => EditSession.Save();
        public void Undo() => EditSession.Undo();
        public void Discard() => EditSession.Discard();
        public void ClearMirrorTargets() => SelectorDM.ClearBatchSelection();

        void LoadFromMonsterFile(Monster_File sourceMonsterFile)
        {
            UnsubscribeEditorGraph();

            string preferredTextLocale = monsterStatSheet?.SelectedTextLocale ?? MonsterStatSheet_Wrapper.DefaultTextLocale;

            monsterFile = sourceMonsterFile;
            MonsterStatSheet = MonsterStatSheet_Wrapper.Wrap(monsterFile.StatSheetFile);
            ApplyEnglishMonsterLocalization(MonsterStatSheet);
            MonsterStatSheet.SelectedTextLocale = preferredTextLocale;
            MonsterLoot = MonsterLoot_Wrapper.Wrap(monsterFile.LootFile);
            RefreshSelectorDisplayName();

            SyncModelPickersFromStatSheet();
            SubscribeEditorGraph();
        }

        byte[] CaptureSessionSnapshot()
        {
            byte[] monsterBytes = CaptureCurrentMonsterBytes();
            byte[]? localizationBytes = CaptureCurrentEnglishLocalizationBytes();

            using MemoryStream stream = new();
            using BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true);
            writer.Write(monsterBytes.Length);
            writer.Write(monsterBytes);
            writer.Write(localizationBytes != null);
            if (localizationBytes != null)
            {
                writer.Write(localizationBytes.Length);
                writer.Write(localizationBytes);
            }

            writer.Flush();
            return stream.ToArray();
        }

        byte[] CaptureCurrentMonsterBytes()
        {
            monsterFile.StatSheetFile = MonsterStatSheet.Unwrap();
            monsterFile.LootFile = MonsterLoot.Unwrap();
            return monsterFile.Write();
        }

        void RestoreFromBytes(byte[] bytes)
        {
            MonsterEditorSnapshot snapshot = ReadSnapshot(bytes);
            LoadFromMonsterFile(Monster_File.Read(snapshot.MonsterBytes));
            if (snapshot.EnglishLocalizationBytes != null)
            {
                RestoreEnglishLocalizationFromBytes(snapshot.EnglishLocalizationBytes);
            }
        }

        void PersistBytes(byte[] bytes)
        {
            MonsterEditorSnapshot snapshot = ReadSnapshot(bytes);

            File.WriteAllBytes(MonsterPath, snapshot.MonsterBytes);
            PersistEnglishLocalizationBytes(snapshot.EnglishLocalizationBytes);

            monsterFile = Monster_File.Read(snapshot.MonsterBytes);
            if (snapshot.EnglishLocalizationBytes != null)
            {
                RestoreEnglishLocalizationFromBytes(snapshot.EnglishLocalizationBytes);
            }

            MirrorBytesToBatchTargetsIfEnabled(snapshot.MonsterBytes);
        }

        void MirrorBytesToBatchTargetsIfEnabled(byte[] bytes)
        {
            if (!MirrorToBatchOnSave || SelectedBatchMirrorScope == null)
            {
                return;
            }

            Monster_File sourceMonster = Monster_File.Read(bytes);

            foreach (MonEditorSelector_DataModel.MonsterListEntry entry in SelectorDM.GetBatchSelectedMonsters())
            {
                string targetPath = SelectorDM.GetMonsterPath(entry.Index);
                if (string.Equals(targetPath, MonsterPath, System.StringComparison.OrdinalIgnoreCase) || !File.Exists(targetPath))
                {
                    continue;
                }

                Monster_File targetMonster = Monster_File.Read(File.ReadAllBytes(targetPath));
                ApplyMirrorScope(sourceMonster, targetMonster, SelectedBatchMirrorScope.Scope);
                File.WriteAllBytes(targetPath, targetMonster.Write());
            }
        }

        static void ApplyMirrorScope(Monster_File sourceMonster, Monster_File targetMonster, BatchMirrorScope scope)
        {
            if ((scope == BatchMirrorScope.StatusTab || scope == BatchMirrorScope.StatusAndLoot) &&
                sourceMonster.StatSheetFile != null)
            {
                targetMonster.StatSheetFile = Monster_StatSheet.ReadSingle(sourceMonster.StatSheetFile.WriteSingle());
            }

            if ((scope == BatchMirrorScope.LootTab || scope == BatchMirrorScope.StatusAndLoot) &&
                sourceMonster.LootFile != null)
            {
                targetMonster.LootFile = Monster_Loot.ReadSingle(sourceMonster.LootFile.WriteSingle());
            }
        }

        void BuildMirrorScopeOptions()
        {
            BatchMirrorScopeOptions.Clear();
            BatchMirrorScopeOptions.Add(new BatchMirrorScopeOption(BatchMirrorScope.StatusTab, "Status Tab"));
            BatchMirrorScopeOptions.Add(new BatchMirrorScopeOption(BatchMirrorScope.LootTab, "Loot Tab"));
            BatchMirrorScopeOptions.Add(new BatchMirrorScopeOption(BatchMirrorScope.StatusAndLoot, "Status + Loot"));
            SelectedBatchMirrorScope = BatchMirrorScopeOptions.FirstOrDefault();
        }

        void SubscribeEditorGraph()
        {
            MonsterStatSheet.PropertyChanged += EditorNodeChanged;
            MonsterStatSheet.ElementalWeakness.PropertyChanged += EditorNodeChanged;
            MonsterStatSheet.StatusResistance.PropertyChanged += EditorNodeChanged;

            foreach (GameIndex_Wrapper wrapper in EnumerateStatSheetGameIndices())
            {
                wrapper.PropertyChanged += EditorNodeChanged;
            }

            MonsterLoot.PropertyChanged += EditorNodeChanged;
            foreach (GameIndex_Wrapper wrapper in EnumerateLootGameIndices())
            {
                wrapper.PropertyChanged += EditorNodeChanged;
            }

            MonsterStatSheet.PropertyChanged += StatSheet_ModelFieldsChanged;
        }

        void StatSheet_ModelFieldsChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(MonsterStatSheet_Wrapper.ModelId) or nameof(MonsterStatSheet_Wrapper.Model2Id))
            {
                Model1Picker.Value = MonsterStatSheet.ModelId;
                Model2Picker.Value = MonsterStatSheet.Model2Id;
                RefreshModelPanel();
            }
            else if (e.PropertyName == nameof(MonsterStatSheet_Wrapper.CtbIconId))
            {
                SelectedCtbIconOption = CtbIconOptions.FirstOrDefault(option => option.Id == MonsterStatSheet.CtbIconId);
            }
            else if (e.PropertyName == nameof(MonsterStatSheet_Wrapper.ArenaId))
            {
                RefreshModelPanel();
            }
        }

        void UnsubscribeEditorGraph()
        {
            if (monsterStatSheet != null)
            {
                MonsterStatSheet.PropertyChanged -= EditorNodeChanged;
                MonsterStatSheet.PropertyChanged -= StatSheet_ModelFieldsChanged;

                if (MonsterStatSheet.ElementalWeakness != null)
                {
                    MonsterStatSheet.ElementalWeakness.PropertyChanged -= EditorNodeChanged;
                }

                if (MonsterStatSheet.StatusResistance != null)
                {
                    MonsterStatSheet.StatusResistance.PropertyChanged -= EditorNodeChanged;
                }

                foreach (GameIndex_Wrapper wrapper in EnumerateStatSheetGameIndices())
                {
                    wrapper.PropertyChanged -= EditorNodeChanged;
                }
            }

            if (monsterLoot != null)
            {
                MonsterLoot.PropertyChanged -= EditorNodeChanged;

                foreach (GameIndex_Wrapper wrapper in EnumerateLootGameIndices())
                {
                    wrapper.PropertyChanged -= EditorNodeChanged;
                }
            }
        }

        void EditorNodeChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MonsterStatSheet_Wrapper.Name) ||
                e.PropertyName == nameof(MonsterStatSheet_Wrapper.SelectedTextLocale))
            {
                RefreshSelectorDisplayName();
            }

            EditSession?.NotifyPotentialMutation();
        }

        void RefreshSelectorDisplayName()
        {
            if (!TryGetMonsterFileIndex(out int monsterFileIndex))
            {
                return;
            }

            SelectorDM.UpdateMonsterDisplayName(monsterFileIndex, MonsterStatSheet?.EnglishNameValue);
        }

        IEnumerable<GameIndex_Wrapper> EnumerateStatSheetGameIndices()
        {
            if (monsterStatSheet == null)
                yield break;

            yield return MonsterStatSheet.ForcedAbility;
            yield return MonsterStatSheet.Ability1;
            yield return MonsterStatSheet.Ability2;
            yield return MonsterStatSheet.Ability3;
            yield return MonsterStatSheet.Ability4;
            yield return MonsterStatSheet.Ability5;
            yield return MonsterStatSheet.Ability6;
            yield return MonsterStatSheet.Ability7;
            yield return MonsterStatSheet.Ability8;
            yield return MonsterStatSheet.Ability9;
            yield return MonsterStatSheet.Ability10;
            yield return MonsterStatSheet.Ability11;
            yield return MonsterStatSheet.Ability12;
            yield return MonsterStatSheet.Ability13;
            yield return MonsterStatSheet.Ability14;
            yield return MonsterStatSheet.Ability15;
            yield return MonsterStatSheet.Ability16;
        }

        IEnumerable<GameIndex_Wrapper> EnumerateLootGameIndices()
        {
            if (monsterLoot == null)
                yield break;

            yield return MonsterLoot.RonsoRage;
            yield return MonsterLoot.Drop1;
            yield return MonsterLoot.Drop1Rare;
            yield return MonsterLoot.Drop2;
            yield return MonsterLoot.Drop2Rare;
            yield return MonsterLoot.DropOverkill1;
            yield return MonsterLoot.DropOverkill1Rare;
            yield return MonsterLoot.DropOverkill2;
            yield return MonsterLoot.DropOverkill2Rare;
            yield return MonsterLoot.Steal;
            yield return MonsterLoot.StealRare;
            yield return MonsterLoot.Bribe;

            foreach (GameIndex_Wrapper wrapper in MonsterLoot.TidusWeapons) yield return wrapper;
            foreach (GameIndex_Wrapper wrapper in MonsterLoot.TidusArmors) yield return wrapper;
            foreach (GameIndex_Wrapper wrapper in MonsterLoot.YunaWeapons) yield return wrapper;
            foreach (GameIndex_Wrapper wrapper in MonsterLoot.YunaArmors) yield return wrapper;
            foreach (GameIndex_Wrapper wrapper in MonsterLoot.AuronWeapons) yield return wrapper;
            foreach (GameIndex_Wrapper wrapper in MonsterLoot.AuronArmors) yield return wrapper;
            foreach (GameIndex_Wrapper wrapper in MonsterLoot.KimahriWeapons) yield return wrapper;
            foreach (GameIndex_Wrapper wrapper in MonsterLoot.KimahriArmors) yield return wrapper;
            foreach (GameIndex_Wrapper wrapper in MonsterLoot.WakkaWeapons) yield return wrapper;
            foreach (GameIndex_Wrapper wrapper in MonsterLoot.WakkaArmors) yield return wrapper;
            foreach (GameIndex_Wrapper wrapper in MonsterLoot.LuluWeapons) yield return wrapper;
            foreach (GameIndex_Wrapper wrapper in MonsterLoot.LuluArmors) yield return wrapper;
            foreach (GameIndex_Wrapper wrapper in MonsterLoot.RikkuWeapons) yield return wrapper;
            foreach (GameIndex_Wrapper wrapper in MonsterLoot.RikkuArmors) yield return wrapper;
        }

        public enum BatchMirrorScope
        {
            StatusTab,
            LootTab,
            StatusAndLoot
        }

        public sealed class BatchMirrorScopeOption
        {
            public BatchMirrorScope Scope { get; }
            public string Label { get; }

            public BatchMirrorScopeOption(BatchMirrorScope scope, string label)
            {
                Scope = scope;
                Label = label;
            }

            public override string ToString() => Label;
        }

        void ApplyEnglishMonsterLocalization(MonsterStatSheet_Wrapper target)
        {
            englishLocalizationBinding = null;

            if (!TryGetMonsterFileIndex(out int monsterFileIndex) ||
                !TryResolveEnglishLocalizationBinding(monsterFileIndex, out MonsterEnglishLocalizationBinding localizationBinding))
            {
                target.ApplyEnglishLocalization(null, null, null, canEdit: false);
                return;
            }

            englishLocalizationBinding = localizationBinding;
            target.ApplyEnglishLocalization(localizationBinding.Name, localizationBinding.Sensor, localizationBinding.Scan, canEdit: true);
        }

        bool TryGetMonsterFileIndex(out int monsterFileIndex)
        {
            monsterFileIndex = -1;

            string fileName = Path.GetFileNameWithoutExtension(MonsterPath);
            if (fileName.Length != 4 || !fileName.StartsWith("m", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return int.TryParse(fileName[1..], out monsterFileIndex);
        }

        static bool TryResolveEnglishLocalizationBinding(int monsterFileIndex, out MonsterEnglishLocalizationBinding localizationBinding)
        {
            foreach (string path in new[]
                     {
                         Project_Service.Instance.Path_KernelMonster1Us,
                         Project_Service.Instance.Path_KernelMonster2Us,
                         Project_Service.Instance.Path_KernelMonster3Us
                     })
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    MonX_File file = MonX_File.Read(File.ReadAllBytes(path));
                    int localIndex = monsterFileIndex - file.ThisHeader.PreviousFileCount;
                    if (localIndex < 0 || localIndex >= file.Entries.Count)
                    {
                        continue;
                    }

                    MonX_File.Entry entry = file.Entries[localIndex];
                    localizationBinding = new MonsterEnglishLocalizationBinding(
                        path,
                        localIndex,
                        entry.Name,
                        entry.Sensor,
                        entry.Scan);
                    return true;
                }
                catch
                {
                    // Ignore broken localization sidecars and keep searching the split files.
                }
            }

            localizationBinding = default;
            return false;
        }

        byte[]? CaptureCurrentEnglishLocalizationBytes()
        {
            if (englishLocalizationBinding == null || MonsterStatSheet == null || !File.Exists(englishLocalizationBinding.Value.SourcePath))
            {
                return null;
            }

            MonX_File file = MonX_File.Read(File.ReadAllBytes(englishLocalizationBinding.Value.SourcePath));
            if (englishLocalizationBinding.Value.EntryIndex < 0 || englishLocalizationBinding.Value.EntryIndex >= file.Entries.Count)
            {
                return null;
            }

            MonX_File.Entry entry = file.Entries[englishLocalizationBinding.Value.EntryIndex];
            entry.Name = MonsterStatSheet.EnglishNameValue;
            entry.Sensor = MonsterStatSheet.EnglishSensorValue;
            entry.Scan = MonsterStatSheet.EnglishScanValue;
            return file.Write();
        }

        void PersistEnglishLocalizationBytes(byte[]? localizationBytes)
        {
            if (localizationBytes == null || englishLocalizationBinding == null)
            {
                return;
            }

            File.WriteAllBytes(englishLocalizationBinding.Value.SourcePath, localizationBytes);
        }

        void RestoreEnglishLocalizationFromBytes(byte[] localizationBytes)
        {
            if (englishLocalizationBinding == null || MonsterStatSheet == null)
            {
                return;
            }

            MonX_File file = MonX_File.Read(localizationBytes);
            if (englishLocalizationBinding.Value.EntryIndex < 0 || englishLocalizationBinding.Value.EntryIndex >= file.Entries.Count)
            {
                return;
            }

            MonX_File.Entry entry = file.Entries[englishLocalizationBinding.Value.EntryIndex];
            MonsterStatSheet.ApplyEnglishLocalization(entry.Name, entry.Sensor, entry.Scan, canEdit: true);
            RefreshSelectorDisplayName();
        }

        static MonsterEditorSnapshot ReadSnapshot(byte[] bytes)
        {
            using MemoryStream stream = new(bytes);
            using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: true);

            int monsterLength = reader.ReadInt32();
            byte[] monsterBytes = reader.ReadBytes(monsterLength);
            byte[]? localizationBytes = null;
            if (reader.ReadBoolean())
            {
                int localizationLength = reader.ReadInt32();
                localizationBytes = reader.ReadBytes(localizationLength);
            }

            return new MonsterEditorSnapshot(monsterBytes, localizationBytes);
        }

        readonly record struct MonsterEnglishLocalizationBinding(
            string SourcePath,
            int EntryIndex,
            string? Name,
            string? Sensor,
            string? Scan);

        readonly record struct MonsterEditorSnapshot(byte[] MonsterBytes, byte[]? EnglishLocalizationBytes);

        public sealed class CtbIconOption
        {
            public CtbIconOption(byte id, string label)
            {
                Id = id;
                Label = label;
            }

            public byte Id { get; }
            public string Label { get; }
            public string Display => $"[{Id}] {Label}";

            public override string ToString() => Display;
        }
    }
}
