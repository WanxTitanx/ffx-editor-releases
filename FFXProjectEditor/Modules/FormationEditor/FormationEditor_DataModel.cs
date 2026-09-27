using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Battle;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Services;
using FFXProjectEditor.Utils.Editing;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Modules.FormationEditor
{
    /// <summary>
    /// SPIRA FORGE — Formation editor (v0.2). Primeiro EDITOR de verdade do hub.
    ///
    /// Consome a espinha compartilhada <see cref="FieldContext"/> (pré-filtra os battles pela area do
    /// field selecionado no Hub) e edita os 8 monstros da formação de um btl_* via
    /// <see cref="Battle_File.WriteWithFormationSlots"/> — writer BYTE-SAFE slot-only, provado pelo gate
    /// RT0 headless (RuntimeTools/FormationSlotLab: RT0 858/858 + slot-only 858/858 + re-read 858/858).
    /// Save grava no workspace com backup .spiraforge.bak + guard AssertSlotOnlyDiff (defense-in-depth).
    /// Offline; nenhum probe / jogo vivo.
    /// </summary>
    internal partial class FormationEditor_DataModel : ObservableObject
    {
        readonly List<BattleRow> allBattles = new();
        readonly Dictionary<int, MonsterOption> monsterByDictId = new();

        Battle_File? loadedBattle;
        string? loadedBattlePath;
        bool slotsSyncing; // true while repopulating slots (suppress mutation churn)

        public IReadOnlyList<MonsterOption> MonsterOptions { get; }
        public ObservableCollection<BattleRow> Battles { get; } = new();
        public ObservableCollection<FormationSlotRow> Slots { get; } = new();

        [ObservableProperty] private BattleRow? selectedBattle;
        [ObservableProperty] private string filterText = string.Empty;

        [ObservableProperty]
        private string honestyBanner = Strings.U_Fe_HonestyBanner;

        [ObservableProperty] private string fieldContextSummary = Strings.F2_no_field_selected_in_the_hub_1b7018e8;
        [ObservableProperty] private string loadSummary = Strings.F2_loading_battle_list_643be093;
        [ObservableProperty] private string battleHeader = Strings.F2_no_battle_selected_9695c34f;
        [ObservableProperty] private string formationSummary = Strings.U_Fe_SelectBattleSummary;
        [ObservableProperty] private string slotRegionSummary = "-";
        [ObservableProperty] private bool canEdit;
        [ObservableProperty] private string editActionSummary =
            Strings.F2_save_writes_the_btl_backup_spiraforge_ba_81e2632a;
        [ObservableProperty] private ByteSnapshotEditorSession? editSession;

        public FormationEditor_DataModel()
        {
            // catálogo de monstros pro picker: Empty + todas as entradas do dicionário.
            List<MonsterOption> options = new() { MonsterOption.Empty };
            foreach (KeyValuePair<short, string> kv in Monster_Dictionary.Instance.OrderBy(kv => kv.Key))
            {
                MonsterOption opt = new(kv.Key, kv.Value);
                options.Add(opt);
                monsterByDictId[kv.Key] = opt;
            }
            MonsterOptions = options;

            LoadBattleList();
        }

        /******************************************
         * Lista de battles + contexto do field
         ******************************************/

        public void RefreshFromDisk() => LoadBattleList();

        public void SyncToField()
        {
            string? area = FieldContext.Instance.SelectedArea;
            string? token = FieldContext.Instance.FieldToken;
            string? battleId = FieldContext.Instance.SelectedBattleId;

            FieldContextSummary = token == null
                ? Strings.F2_no_field_selected_in_the_hub_open_the_fi_308fa457
                : $"Field do Hub: {token}" + (area != null ? $" · area {area}" : string.Empty)
                  + (battleId != null ? $" · battle {battleId}" : string.Empty) + Strings.U_Fe_PreFiltered;

            // Handoff: se o Hub escolheu um battle específico (encounter peek), pula direto pra ele.
            if (!string.IsNullOrEmpty(battleId)
                && allBattles.Any(b => string.Equals(b.Id, battleId, StringComparison.OrdinalIgnoreCase)))
            {
                FilterText = battleId; // dispara ApplyFilter (filtra a lista pro battle)
                SelectedBattle = Battles.FirstOrDefault(b => string.Equals(b.Id, battleId, StringComparison.OrdinalIgnoreCase));
                return;
            }

            if (!string.IsNullOrEmpty(area))
                FilterText = area; // dispara OnFilterTextChanged -> ApplyFilter
            else
                ApplyFilter();
        }

        void LoadBattleList()
        {
            allBattles.Clear();

            if (!Project_Service.Instance.IsProjectLoaded)
            {
                LoadSummary = Strings.U_Fe_ProjectNotLoaded;
                Battles.Clear();
                return;
            }

            string btlRoot = Project_Service.Instance.Path_Btl;
            if (!Directory.Exists(btlRoot))
            {
                LoadSummary = string.Format(Strings.U_Fe_BattlesFolderMissing, btlRoot);
                Battles.Clear();
                return;
            }

            foreach (string dir in Directory.EnumerateDirectories(btlRoot).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                string id = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(id))
                    continue;
                if (File.Exists(Path.Combine(dir, id + ".bin")))
                    allBattles.Add(new BattleRow { Id = id });
            }

            LoadSummary = string.Format(Strings.U_Fe_BattlesCount, allBattles.Count);
            // pré-filtra pelo field selecionado no Hub, se houver.
            SyncToField();
        }

        partial void OnFilterTextChanged(string value) => ApplyFilter();

        void ApplyFilter()
        {
            string filter = (FilterText ?? string.Empty).Trim();
            BattleRow? previous = SelectedBattle;

            IEnumerable<BattleRow> view = allBattles;
            if (filter.Length > 0)
                view = allBattles.Where(b => b.Id.Contains(filter, StringComparison.OrdinalIgnoreCase));

            Battles.Clear();
            foreach (BattleRow b in view)
                Battles.Add(b);

            if (previous != null && Battles.Contains(previous))
                SelectedBattle = previous;

            LoadVisibleFormationLabels();
        }

        const int LabelLoadCap = 90;
        readonly Dictionary<string, string> labelCache = new();

        // Carrega o label da formação (monstros) pros battles visíveis — "o que luta onde", read-only.
        // Lazy + cached + capped: com a lista cheia (sem filtro) seria caro, então pede pra filtrar por area.
        void LoadVisibleFormationLabels()
        {
            if (!Project_Service.Instance.IsProjectLoaded)
                return;

            if (Battles.Count > LabelLoadCap)
            {
                foreach (BattleRow row in Battles)
                    row.FormationLabel = labelCache.TryGetValue(row.Id, out string? c)
                        ? c
                        : Strings.U_Fe_FilterByArea;
                return;
            }

            foreach (BattleRow row in Battles)
            {
                if (labelCache.TryGetValue(row.Id, out string? cached)) { row.FormationLabel = cached; continue; }

                string label;
                try
                {
                    string path = Project_Service.Instance.GetPathBattle(row.Id);
                    if (!File.Exists(path))
                        label = Strings.F2_file_missing_b23ed7c5;
                    else
                    {
                        Battle_File bf = Battle_File.Read(row.Id, File.ReadAllBytes(path));
                        label = bf.Formation != null ? bf.FormationLabel : Strings.U_Fe_NoFormation;
                    }
                }
                catch
                {
                    label = Strings.U_Fe_ReaderNotOpened;
                }

                labelCache[row.Id] = label;
                row.FormationLabel = label;
            }
        }

        /******************************************
         * Carregar formação do battle selecionado
         ******************************************/

        partial void OnSelectedBattleChanged(BattleRow? value)
        {
            EditSession?.Dispose();
            EditSession = null;
            loadedBattle = null;
            loadedBattlePath = null;
            Slots.Clear();
            CanEdit = false;

            if (value == null)
            {
                BattleHeader = Strings.F2_no_battle_selected_9695c34f;
                FormationSummary = Strings.U_Fe_SelectBattle;
                SlotRegionSummary = "-";
                return;
            }

            string path = Project_Service.Instance.GetPathBattle(value.Id);
            if (!File.Exists(path))
            {
                BattleHeader = value.Id;
                FormationSummary = string.Format(Strings.U_Fe_FileNotFound, path);
                SlotRegionSummary = "-";
                return;
            }

            Battle_File battle;
            try
            {
                battle = Battle_File.Read(value.Id, File.ReadAllBytes(path));
            }
            catch (Exception ex)
            {
                BattleHeader = value.Id;
                FormationSummary = string.Format(Strings.U_Fe_ReaderFailed, ex.Message) +
                                   Strings.U_Fe_UnsupportedLayout;
                SlotRegionSummary = "-";
                return;
            }

            loadedBattle = battle;
            loadedBattlePath = path;
            BattleHeader = $"{value.Id} · {battle.ChunkCount} chunks";

            if (!battle.CanWriteFormation || battle.Formation == null)
            {
                FormationSummary = Strings.U_Fe_NoEditableFormation;
                SlotRegionSummary = "-";
                return;
            }

            PopulateSlots(battle.Formation);
            CanEdit = true;
            FormationSummary = string.Format(Strings.U_Fe_FormationLabel, battle.FormationLabel);
            SlotRegionSummary =
                string.Format(Strings.U_Fe_SlotOnly, battle.FormationSlotsOffset, battle.FormationChunkOffset) +
                Strings.F2_only_these_16_bytes_change_on_save_56715e51;

            EditSession = new ByteSnapshotEditorSession(
                CaptureSnapshot,
                RestoreSnapshot,
                PersistSnapshot,
                "formation",
                battle.OriginalBytes)
            {
                RevertWritesToDisk = true
            };
        }

        void PopulateSlots(Battle_Formation formation)
        {
            slotsSyncing = true;
            try
            {
                Slots.Clear();

                // FFX marca um monstro ATIVO/vivo da formação com uma flag na nibble alta do raw (observado 0x1000
                // nos slots vivos, ex.: 10DEh). Um slot que era vazio (FFFFh) e é preenchido PRECISA carregar a MESMA
                // flag, senão o jogo/Aurora não o conta como monstro vivo (era a causa de "só 4 vivos + adicionados
                // somem"). Herdamos a flag de um slot vivo irmão do PRÓPRIO battle (per-battle, sem chutar); se a
                // formação estava 100% vazia, default 0x1000 (a flag padrão observada).
                ushort liveHigh = (ushort)(formation.Slots
                    .Where(s => s.RawMonsterId != 0xFFFF)
                    .Select(s => s.RawMonsterId & 0xF000)
                    .DefaultIfEmpty(0x1000)
                    .First());

                foreach (Battle_FormationSlot slot in formation.Slots)
                {
                    MonsterOption selected = slot.IsEmpty
                        ? MonsterOption.Empty
                        : (monsterByDictId.TryGetValue(slot.DictionaryId, out MonsterOption? opt) ? opt : MonsterOption.Empty);

                    FormationSlotRow row = new(slot.SlotIndex, (ushort)slot.RawMonsterId, NotifySlotMutated, liveHigh)
                    {
                        SelectedMonster = selected
                    };
                    Slots.Add(row);
                }
            }
            finally
            {
                slotsSyncing = false;
            }

            // Avalonia ComboBox gotcha: a SelectedItem set while the per-row ComboBoxes haven't materialized their
            // ItemsSource yet shows BLANK on load (even the "(Vazio)" option) — it only displays once the user picks.
            // Re-apply each SelectedMonster after the view has loaded (toggle null->value) so the ComboBox re-resolves
            // the selection against a populated Items list. Guarded by slotsSyncing so it never marks the row dirty.
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                slotsSyncing = true;
                try
                {
                    foreach (FormationSlotRow row in Slots)
                    {
                        MonsterOption? sel = row.SelectedMonster;
                        row.SelectedMonster = null;
                        row.SelectedMonster = sel;
                    }
                }
                finally { slotsSyncing = false; }
            }, Avalonia.Threading.DispatcherPriority.Background);
        }

        void NotifySlotMutated()
        {
            if (slotsSyncing)
                return;
            EditSession?.NotifyPotentialMutation();
            if (loadedBattle != null)
                FormationSummary = Strings.U_Fe_FormationEdited + DescribeCurrentSlots();
        }

        string DescribeCurrentSlots()
        {
            List<string> parts = Slots
                .Where(r => !r.IsEmpty)
                .Select(r => r.SelectedMonster!.Label)
                .ToList();
            return parts.Count == 0 ? Strings.U_Fe_Empty : "[" + string.Join(", ", parts) + "]";
        }

        ushort[] CurrentSlotUshorts() => Slots.Select(r => r.RawValue).ToArray();

        /******************************************
         * Editor session hooks (capture/restore/persist) — byte-safe
         ******************************************/

        byte[] CaptureSnapshot()
        {
            // base = arquivo como carregado; só os 16 bytes dos slots mudam (RT0 por construção).
            return loadedBattle!.WriteWithFormationSlots(CurrentSlotUshorts());
        }

        void RestoreSnapshot(byte[] bytes)
        {
            if (loadedBattle == null)
                return;
            Battle_Formation? formation = Battle_Formation.TryRead(SliceFormationChunk(bytes));
            if (formation == null)
                return;
            PopulateSlots(formation);
            FormationSummary = string.Format(Strings.U_Fe_FormationRestored, loadedBattle.FormationLabel);
        }

        byte[] SliceFormationChunk(byte[] fileBytes)
        {
            int off = loadedBattle!.FormationChunkOffset;
            int len = loadedBattle.FormationChunkLength;
            if (off <= 0 || len <= 0 || off + len > fileBytes.Length)
                return Array.Empty<byte>();
            byte[] chunk = new byte[len];
            Array.Copy(fileBytes, off, chunk, 0, len);
            return chunk;
        }

        void PersistSnapshot(byte[] bytes)
        {
            if (loadedBattle == null || loadedBattlePath == null)
                return;

            // Production save path (guard slot-only + backup-once + write), shared with the headless gate
            // (RuntimeTools/FormationSlotLab --savecheck) so the exact disk-write logic is byte-tested.
            FormationSlotWriter.SaveResult result = FormationSlotWriter.WriteLooseFile(
                loadedBattlePath,
                loadedBattle.OriginalBytes,
                bytes,
                loadedBattle.FormationSlotsOffset,
                Battle_File.FormationSlotsLength);
            EditActionSummary = result.Message;
        }

        public void Save() => EditSession?.Save();
        public void Undo() => EditSession?.Undo();
        public void Discard() => EditSession?.Discard();
    }

    internal sealed partial class BattleRow : ObservableObject
    {
        public required string Id { get; init; }
        public string DisplayName => Id;

        // Label da formação (monstros) — carregado lazy pro conjunto filtrado. "" = ainda não carregado.
        [ObservableProperty] private string formationLabel = string.Empty;
    }

    /// <summary>Opção do picker de monstro (Empty + entradas do Monster_Dictionary).</summary>
    internal sealed class MonsterOption
    {
        public static readonly MonsterOption Empty = new();

        private MonsterOption()
        {
            DictionaryId = null;
            Label = "(Vazio / FFFFh)";
        }

        public MonsterOption(short dictionaryId, string name)
        {
            DictionaryId = dictionaryId;
            Label = $"m{dictionaryId:D3} - {name}";
        }

        public int? DictionaryId { get; }
        public bool IsEmpty => DictionaryId == null;
        public string Label { get; }
        public override string ToString() => Label;
    }

    /// <summary>Uma linha de slot editável (1 dos 8 monstros da formação).</summary>
    internal sealed partial class FormationSlotRow : ObservableObject
    {
        readonly Action onChanged;

        public FormationSlotRow(int slotIndex, ushort originalRaw, Action onChanged, ushort emptyFillHigh = 0x1000)
        {
            SlotIndex = slotIndex;
            OriginalRaw = originalRaw;
            this.onChanged = onChanged;
            EmptyFillHigh = emptyFillHigh;
        }

        // High nibble (flag de "monstro vivo no campo") a estampar ao preencher um slot que era vazio (FFFFh).
        // FFX marca o monstro ativo com essa flag (observado 0x1000 nos slots vivos); sem ela o jogo/Aurora não
        // conta o monstro como vivo. Herdada de um slot vivo irmão do mesmo battle (per-battle), senão 0x1000.
        public ushort EmptyFillHigh { get; }

        public int SlotIndex { get; }
        public ushort OriginalRaw { get; }
        public string SlotLabel => $"Slot {SlotIndex:00}";
        public string OriginalHex => OriginalRaw == 0xFFFF ? "FFFFh" : $"{OriginalRaw:X4}h";

        [ObservableProperty] private MonsterOption? selectedMonster;

        public bool IsEmpty => SelectedMonster == null || SelectedMonster.IsEmpty;

        // Preserva a nibble alta do raw original (flags/variante) e troca os 12 bits do dict id.
        // Slot vazio -> FFFFh. Empty original sendo preenchido -> nibble alta 0.
        public ushort RawValue
        {
            get
            {
                if (SelectedMonster == null || SelectedMonster.IsEmpty)
                    return 0xFFFF;
                ushort high = OriginalRaw == 0xFFFF ? EmptyFillHigh : (ushort)(OriginalRaw & 0xF000);
                return (ushort)(high | ((ushort)SelectedMonster.DictionaryId!.Value & 0x0FFF));
            }
        }

        public string RawHex => RawValue == 0xFFFF ? "FFFFh" : $"{RawValue:X4}h";
        public bool Changed => RawValue != OriginalRaw;

        partial void OnSelectedMonsterChanged(MonsterOption? value)
        {
            OnPropertyChanged(nameof(RawHex));
            OnPropertyChanged(nameof(RawValue));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(Changed));
            onChanged();
        }
    }
}
