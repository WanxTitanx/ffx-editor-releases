using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Save;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.SaveEditor
{
    internal partial class SaveEditor_DataModel : ObservableObject
    {
        FfxSaveFile? session;

        public ObservableCollection<FfxSaveCharacterSnapshot> Characters { get; } = new();
        public ObservableCollection<FfxSaveEquipmentSnapshot> EquipmentSlots { get; } = new();
        public ObservableCollection<FfxSaveItemSlotSnapshot> ItemSlots { get; } = new();
        public ObservableCollection<FfxSaveKeyItemSnapshot> KeyItems { get; } = new();
        public ObservableCollection<FfxSaveBlitzballPlayerSnapshot> BlitzballPlayers { get; } = new();
        public ObservableCollection<SaveEditor_FieldRow> MinigameFields { get; } = new();
        public ObservableCollection<SaveEditor_FieldRow> MiscFields { get; } = new();
        public ObservableCollection<string> ItemCatalogLabels { get; } = new();
        public ObservableCollection<string> MemoryCardSlotLabels { get; } = new();

        readonly List<FfxSaveMemoryCardSlot> memoryCardSlots = new();
        string? memoryCardPath;
        bool suppressMemoryCardSlotChange;

        [ObservableProperty] private string statusSummary = Strings.F2_no_save_loaded_use_load_to_open_an_ffx_f_ca614267;
        [ObservableProperty] private string fileLabel = Strings.F2_no_file_72ce9a31;
        [ObservableProperty] private string formatLabel = "—";
        [ObservableProperty] private int selectedMemoryCardSlotIndex = -1;
        [ObservableProperty] private FfxSaveCharacterSnapshot? selectedCharacter;
        [ObservableProperty] private SaveEditor_CharacterBindings? characterEditor;
        [ObservableProperty] private FfxSaveEquipmentSnapshot? selectedEquipment;
        [ObservableProperty] private SaveEditor_EquipmentBindings? equipmentEditor;
        [ObservableProperty] private FfxSaveItemSlotSnapshot? selectedItem;
        [ObservableProperty] private FfxSaveBlitzballPlayerSnapshot? selectedBlitzballPlayer;
        [ObservableProperty] private FfxSaveSphereGridNodeSnapshot? selectedSphereNode;
        [ObservableProperty] private int gil;
        [ObservableProperty] private int selectedSphereNodeIndex;
        [ObservableProperty] private bool isDirty;
        // Jarvis-UI (Save Editor audit 2026-06-20): estado do Expander de navegação de seções.
        // Default expandido para preservar o layout anterior; o usuário pode recolher para recuperar
        // largura de tela quando está editando uma seção densa.
        [ObservableProperty] private bool isSectionsExpanded = true;

        // Jarvis-UI (Save Editor Hub Phase 2 2026-06-20): fingerprint SHA256 do payload (Core.Data).
        // Short = primeiros 12 chars + "…" pro header; Full = 64 chars pro tooltip/HelpText.
        // Recalculado só em pontos que resetam StatusSummary (load/save/MC switch). NÃO recalcular
        // em TouchDirty: dirty ≠ hash change até Apply+mutate — o hash é snapshot do blob em memória,
        // não um indicador de "não salvo" (esse papel é do IsDirty/chip).
        [ObservableProperty] private string payloadHashShort = "—";
        [ObservableProperty] private string payloadHashFull = string.Empty;

        public bool HasSession => session != null;
        public bool ShowMemoryCardSlotPicker => memoryCardSlots.Count > 1;
        public FfxSaveCore? Core => session?.Core;

        // Jarvis-UI (Sprint C 2026-06-20, OPT-C1): severidade semântica derivada do StatusSummary pra
        // pintar o header do hub (erro de load/save em DangerBrush, aviso de dirty-descartado em WarningBrush,
        // resto neutro). Derivação por string segue os handlers (catch emitem "Erro ..."; slot switch emite
        // "Troca de slot sem salvar — ..."). Mesmo approach do MagicDllBrowser OPT-F7.
        public string StatusSeverity => ClassifyStatusSeverity(StatusSummary);

        static string ClassifyStatusSeverity(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return "None";
            if (status.Contains("Erro", StringComparison.OrdinalIgnoreCase)
                || status.Contains("Falha", StringComparison.OrdinalIgnoreCase))
                return "Danger";
            if (status.Contains("descartadas", StringComparison.OrdinalIgnoreCase)
                || status.Contains("sem salvar", StringComparison.OrdinalIgnoreCase))
                return "Warning";
            return "Info";
        }

        // OPT-C1: StatusSeverity é computed de StatusSummary — notifica a cada troca p/ o header repintar.
        partial void OnStatusSummaryChanged(string value) => OnPropertyChanged(nameof(StatusSeverity));

        public void LoadFromPath(string path)
        {
            memoryCardPath = null;
            memoryCardSlots.Clear();
            MemoryCardSlotLabels.Clear();
            suppressMemoryCardSlotChange = true;
            SelectedMemoryCardSlotIndex = -1;
            suppressMemoryCardSlotChange = false;
            NotifySessionBindings();

            if (path.EndsWith(".ps2", System.StringComparison.OrdinalIgnoreCase)
                && new FileInfo(path).Length == FfxSaveMemoryCard.CardSize)
            {
                memoryCardPath = path;
                IReadOnlyList<FfxSaveMemoryCardSlot> slots = FfxSaveMemoryCard.ListSlots(path);
                if (slots.Count == 0)
                    throw new InvalidDataException(Strings.F2_no_ffx_save_found_on_the_memory_card_ps2_d5055677);

                for (int i = 0; i < slots.Count; i++)
                {
                    memoryCardSlots.Add(slots[i]);
                    MemoryCardSlotLabels.Add($"{i + 1}. {slots[i].Label}");
                }

                suppressMemoryCardSlotChange = true;
                SelectedMemoryCardSlotIndex = 0;
                suppressMemoryCardSlotChange = false;
                session = FfxSaveMemoryCard.LoadSlot(path, memoryCardSlots[0]);
            }
            else
            {
                session = FfxSaveFile.Load(path);
            }

            ReloadAllFromSession();
            FileLabel = session.DisplayLabel;
            FormatLabel = session.Format.ToString();
            IsDirty = false;
            string slotHint = memoryCardSlots.Count > 1
                ? $" — slot {SelectedMemoryCardSlotIndex + 1}/{memoryCardSlots.Count}"
                : string.Empty;
            StatusSummary = $"Carregado: {Path.GetFileName(path)} ({session.Format}, {FfxSaveCore.DataSize} bytes payload){slotHint}.";
            RefreshPayloadHash();
            NotifySessionBindings();
        }

        /// <summary>
        /// HasSession/ShowMemoryCardSlotPicker/Core são computed; o empty-state (Jarvis-UI) binda
        /// IsVisible em HasSession — sem este notify o TabHost nunca aparece após Load.
        /// </summary>
        void NotifySessionBindings()
        {
            OnPropertyChanged(nameof(HasSession));
            OnPropertyChanged(nameof(ShowMemoryCardSlotPicker));
            OnPropertyChanged(nameof(Core));
        }

        public void SwitchMemoryCardSlot(int index)
        {
            if (memoryCardPath == null || index < 0 || index >= memoryCardSlots.Count)
                return;

            if (IsDirty)
                StatusSummary = Strings.F2_slot_switch_without_saving_pending_chang_1484b26e;

            session = FfxSaveMemoryCard.LoadSlot(memoryCardPath, memoryCardSlots[index]);
            suppressMemoryCardSlotChange = true;
            SelectedMemoryCardSlotIndex = index;
            suppressMemoryCardSlotChange = false;
            ReloadAllFromSession();
            FileLabel = session.DisplayLabel;
            IsDirty = false;
            StatusSummary = $"Slot {index + 1}/{memoryCardSlots.Count}: {memoryCardSlots[index].Label}";
            RefreshPayloadHash();
        }

        partial void OnSelectedMemoryCardSlotIndexChanged(int value)
        {
            if (suppressMemoryCardSlotChange || value < 0)
                return;

            SwitchMemoryCardSlot(value);
        }

        public void ApplySelectedCharacter()
        {
            if (session == null || SelectedCharacter == null || CharacterEditor == null)
                return;

            CharacterEditor.Write(session.Core, SelectedCharacter);
            TouchDirty($"Character {SelectedCharacter.Label}");
        }

        public void ApplySelectedEquipment()
        {
            if (session == null || SelectedEquipment == null || EquipmentEditor == null)
                return;

            EquipmentEditor.Write(session.Core, SelectedEquipment);
            TouchDirty($"Equipment {SelectedEquipment.Label}");
        }

        public void ApplySelectedItem()
        {
            if (session == null || SelectedItem == null)
                return;

            SelectedItem.Write(session.Core);
            TouchDirty($"Item {SelectedItem.Label}");
        }

        public void ApplyGil()
        {
            if (session == null)
                return;

            session.Core.WriteInt32Le(FfxSaveItems.GilOffset, Gil, 4);
            TouchDirty("Gil");
        }

        public void ApplySelectedBlitzballPlayer()
        {
            if (session == null || SelectedBlitzballPlayer == null)
                return;

            SelectedBlitzballPlayer.Write(session.Core);
            TouchDirty($"Blitzball {SelectedBlitzballPlayer.Label}");
        }

        public void ApplySelectedSphereNode()
        {
            if (session == null || SelectedSphereNode == null)
                return;

            SelectedSphereNode.Write(session.Core);
            TouchDirty($"Sphere node {SelectedSphereNode.NodeIndex}");
        }

        public void RefreshSphereNodeFromIndex()
        {
            if (session == null)
                return;

            SelectedSphereNode = FfxSaveSphereGridNodeSnapshot.Read(session.Core, SelectedSphereNodeIndex);
        }

        public void ApplyMinigameFields()
        {
            if (session == null)
                return;

            foreach (SaveEditor_FieldRow row in MinigameFields)
                row.Write(session.Core);
            TouchDirty("Minigame fields");
        }

        public void ApplyMiscFields()
        {
            if (session == null)
                return;

            foreach (SaveEditor_FieldRow row in MiscFields)
                row.Write(session.Core);
            TouchDirty("Misc fields");
        }

        public void ApplyKeyItems()
        {
            if (session == null)
                return;

            foreach (FfxSaveKeyItemSnapshot key in KeyItems)
                key.Write(session.Core);
            TouchDirty("Key items");
        }

        byte[]? equipmentClipboard;

        public void RunBatchAction(FfxSaveSection section, int actionId, int slotIndex = 0, string? donorPath = null)
        {
            if (session == null)
                return;

            try
            {
                if (actionId == 46 && SelectedEquipment != null)
                {
                    equipmentClipboard = new byte[FfxSaveEquipment.SlotStride];
                    System.Array.Copy(session.Core.Data, SelectedEquipment.SlotBase, equipmentClipboard, 0, FfxSaveEquipment.SlotStride);
                    StatusSummary = $"Equipment slot copiado ({SelectedEquipment.Label}).";
                    return;
                }

                if (actionId == 47 && SelectedEquipment != null && equipmentClipboard != null)
                {
                    System.Array.Copy(equipmentClipboard, 0, session.Core.Data, SelectedEquipment.SlotBase, FfxSaveEquipment.SlotStride);
                    ReloadEquipment();
                    TouchDirty($"Paste equipment {SelectedEquipment.Label}");
                    StatusSummary = $"Equipment colado em {SelectedEquipment.Label}.";
                    return;
                }

                FfxSaveCore? donor = null;
                if (!string.IsNullOrEmpty(donorPath))
                    donor = FfxSaveFile.Load(donorPath).Core;

                FfxSaveBatchActions.Run(session.Core, actionId, slotIndex, donor);
                ReloadAllFromSession();
                TouchDirty($"Batch: {actionId}");
                StatusSummary = $"Batch action {actionId} aplicada. Revise e salve.";
            }
            catch (System.NotSupportedException ex)
            {
                StatusSummary = ex.Message;
            }
        }

        public void ImportRegionFromFile(string donorPath, int offset, int length)
        {
            if (session == null)
                return;

            var donor = FfxSaveFile.Load(donorPath);
            FfxSaveBatchActions.CopyRegion(session.Core, donor.Core, offset, length);
            ReloadAllFromSession();
            TouchDirty($"Import @ {offset}");
            string.Format(Strings.U_Sve_RegionImported, offset, offset + length - 1, Path.GetFileName(donorPath));
        }

        public void ImportEntireFromFile(string donorPath)
        {
            if (session == null)
                return;

            var donor = FfxSaveFile.Load(donorPath);
            FfxSaveBatchActions.ImportEntireSave(session.Core, donor.Core);
            ReloadAllFromSession();
            TouchDirty("Entire save import");
            StatusSummary = $"Save inteiro importado de {Path.GetFileName(donorPath)}.";
        }

        public void SaveCurrent()
        {
            if (session == null)
                return;

            session.Save();
            IsDirty = false;
            StatusSummary = $"Salvo: {session.SourcePath}";
            RefreshPayloadHash();
        }

        public void SaveCurrentAs(string path, FfxSaveFormat format)
        {
            if (session == null)
                return;

            session.SaveAs(path, format);
            FormatLabel = session.Format.ToString();
            FileLabel = session.DisplayLabel;
            IsDirty = false;
            StatusSummary = $"Salvo como {format}: {path}";
            RefreshPayloadHash();
        }

        void ReloadAllFromSession()
        {
            ReloadCharactersFromSession();
            ReloadEquipment();
            ReloadItems();
            ReloadBlitzball();
            ReloadMinigameFields();
            ReloadMiscFields();
            ReloadItemCatalog();
            RefreshSphereNodeFromIndex();
        }

        /// <summary>
        /// Jarvis-UI (Save Editor Hub Phase 2 2026-06-20): recalcula o fingerprint SHA256 do payload
        /// (Core.Data). Usar o util <see cref="FfxSaveHash.Sha256Hex"/> — não inventar CRC custom.
        /// Chamado nos mesmos pontos que resetam StatusSummary após load/save/MC switch.
        /// </summary>
        public void RefreshPayloadHash()
        {
            byte[]? data = session?.Core?.Data;
            if (data is { Length: > 0 })
            {
                string full = FfxSaveHash.Sha256Hex(data);
                PayloadHashFull = full;
                PayloadHashShort = (full.Length >= 12 ? full[..12] : full) + "…";
            }
            else
            {
                PayloadHashFull = string.Empty;
                PayloadHashShort = "—";
            }
        }

        void ReloadCharactersFromSession()
        {
            Characters.Clear();
            if (session == null)
                return;

            for (int i = 0; i < FfxSaveCharacterOffsets.CharacterNames.Length; i++)
                Characters.Add(FfxSaveCharacterSnapshot.Read(session.Core, i));

            SelectedCharacter = Characters.FirstOrDefault();
        }

        partial void OnSelectedCharacterChanged(FfxSaveCharacterSnapshot? value) => SyncCharacterEditor();

        void SyncCharacterEditor()
        {
            if (session != null && SelectedCharacter != null)
            {
                CharacterEditor = SaveEditor_CharacterBindings.Load(session.Core, SelectedCharacter);
                CharacterEditor.CharacterIndexChangeHandler = idx =>
                {
                    if (idx >= 0 && idx < Characters.Count)
                        SelectedCharacter = Characters[idx];
                };
            }
            else
            {
                CharacterEditor = null;
            }
        }

        void ReloadEquipment()
        {
            EquipmentSlots.Clear();
            if (session == null)
                return;

            var defs = FfxSaveEquipment.BuildSlotList();
            for (int i = 0; i < defs.Count; i++)
                EquipmentSlots.Add(FfxSaveEquipmentSnapshot.Read(session.Core, defs[i], i));

            SelectedEquipment = EquipmentSlots.FirstOrDefault();
            SyncEquipmentEditor();
        }

        partial void OnSelectedEquipmentChanged(FfxSaveEquipmentSnapshot? value) => SyncEquipmentEditor();

        void SyncEquipmentEditor()
        {
            if (session != null && SelectedEquipment != null)
                EquipmentEditor = SaveEditor_EquipmentBindings.Load(session.Core, SelectedEquipment);
            else
                EquipmentEditor = null;
        }

        void ReloadItems()
        {
            ItemSlots.Clear();
            KeyItems.Clear();
            if (session == null)
                return;

            for (int i = 0; i < FfxSaveItems.SlotCount; i++)
                ItemSlots.Add(FfxSaveItemSlotSnapshot.Read(session.Core, i));

            foreach (FfxSaveKeyItemSnapshot key in FfxSaveKeyItemSnapshot.ReadAll(session.Core))
                KeyItems.Add(key);

            Gil = session.Core.ReadInt32Le(FfxSaveItems.GilOffset, 4);
            SelectedItem = ItemSlots.FirstOrDefault();
        }

        void ReloadBlitzball()
        {
            BlitzballPlayers.Clear();
            if (session == null)
                return;

            for (int i = 0; i < FfxSaveBlitzball.PlayerCount; i++)
                BlitzballPlayers.Add(FfxSaveBlitzballPlayerSnapshot.Read(session.Core, i));

            SelectedBlitzballPlayer = BlitzballPlayers.FirstOrDefault();
        }

        void ReloadMinigameFields()
        {
            MinigameFields.Clear();
            if (session == null)
                return;

            var (min, max) = FfxSaveSectionRanges.GetRange(FfxSaveSection.Minigame);
            foreach (FfxSaveRegistryField f in FfxSaveRegistry.FieldsInRange(min, max))
                MinigameFields.Add(SaveEditor_FieldRow.FromRegistry(f, session.Core));
        }

        void ReloadMiscFields()
        {
            MiscFields.Clear();
            if (session == null)
                return;

            foreach (FfxSaveRegistryField f in FfxSaveRegistry.Root.MiscHighlights)
                MiscFields.Add(SaveEditor_FieldRow.FromRegistry(f, session.Core));
        }

        void ReloadItemCatalog()
        {
            ItemCatalogLabels.Clear();
            foreach (FfxSaveCatalogEntry e in FfxSaveRegistry.Root.ItemCatalog)
                ItemCatalogLabels.Add(e.Label);
        }

        void TouchDirty(string what)
        {
            IsDirty = true;
            string.Format(Strings.U_Sve_PendingClose, what);
        }

        public void RefreshSelectedFromBytes()
        {
            if (session == null || SelectedCharacter == null)
                return;

            int idx = SelectedCharacter.Index;
            var fresh = FfxSaveCharacterSnapshot.Read(session.Core, idx);
            CopyCharacterFields(fresh, SelectedCharacter);
        }

        static void CopyCharacterFields(FfxSaveCharacterSnapshot from, FfxSaveCharacterSnapshot to)
        {
            to.Activation = from.Activation;
            to.BaseHp = from.BaseHp;
            to.BaseMp = from.BaseMp;
            to.BaseStrength = from.BaseStrength;
            to.BaseDefense = from.BaseDefense;
            to.BaseMagic = from.BaseMagic;
            to.BaseMagicDefense = from.BaseMagicDefense;
            to.BaseAgility = from.BaseAgility;
            to.BaseLuck = from.BaseLuck;
            to.BaseEvasion = from.BaseEvasion;
            to.BaseAccuracy = from.BaseAccuracy;
            to.CurrentHp = from.CurrentHp;
            to.CurrentMp = from.CurrentMp;
            to.AbilityPoints = from.AbilityPoints;
            to.SphereLevel = from.SphereLevel;
            to.OverdriveGauge = from.OverdriveGauge;
            to.OverdriveMode = from.OverdriveMode;
            to.EnemiesDefeated = from.EnemiesDefeated;
            to.PoisonDamagePercent = from.PoisonDamagePercent;
            to.Affection = from.Affection;
            to.Name = from.Name;
        }
    }

    internal sealed partial class SaveEditor_FieldRow : ObservableObject
    {
        readonly FfxSaveRegistryField field;
        readonly System.Collections.Generic.List<string> comboLabels = new();

        public string Label => field.Label;
        public bool IsBit => field.Kind == "bit";
        public bool IsCombo => field.Kind == "combo";
        public bool IsInt => field.Kind == "int";

        [ObservableProperty] private string intValue = "0";
        [ObservableProperty] private bool bitValue;
        [ObservableProperty] private int comboIndex;
        [ObservableProperty] private string comboLabel = "—";

        public System.Collections.Generic.IReadOnlyList<string> ComboLabels => comboLabels;

        public static SaveEditor_FieldRow FromRegistry(FfxSaveRegistryField f, FfxSaveCore core)
        {
            var row = new SaveEditor_FieldRow(f);
            row.Load(core);
            return row;
        }

        SaveEditor_FieldRow(FfxSaveRegistryField field) => this.field = field;

        public void Load(FfxSaveCore core)
        {
            switch (field.Kind)
            {
                case "int":
                    IntValue = core.ReadInt32Le(field.Offset, field.Bytes).ToString();
                    break;
                case "bit":
                    BitValue = core.ReadBit(field.Offset, field.Bit);
                    break;
                case "combo":
                {
                    comboLabels.Clear();
                    var catalog = FfxSaveRegistry.GetCatalog(field.Catalog);
                    foreach (FfxSaveCatalogEntry e in catalog)
                        comboLabels.Add(e.Label);
                    ComboIndex = FfxSaveCatalogCodec.ReadCatalogIndex(core, field.Offset, 1, catalog);
                    if (ComboIndex < 0) ComboIndex = 0;
                    ComboLabel = comboLabels.Count > ComboIndex ? comboLabels[ComboIndex] : "—";
                    break;
                }
            }
        }

        public void Write(FfxSaveCore core)
        {
            switch (field.Kind)
            {
                case "int":
                    if (int.TryParse(IntValue, out int v))
                        core.WriteInt32Le(field.Offset, v, field.Bytes);
                    break;
                case "bit":
                    core.WriteBit(field.Offset, field.Bit, BitValue);
                    break;
                case "combo":
                {
                    var catalog = FfxSaveRegistry.GetCatalog(field.Catalog);
                    FfxSaveCatalogCodec.WriteCatalogBytes(core, field.Offset, catalog, ComboIndex);
                    break;
                }
            }
        }

        partial void OnComboIndexChanged(int value)
        {
            if (comboLabels.Count > value)
                ComboLabel = comboLabels[value];
        }
    }
}
