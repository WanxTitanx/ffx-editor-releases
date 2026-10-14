using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Player;
using FFXProjectEditor.FfxLib.SpiraDataAtlas;
using FFXProjectEditor.Modules.Common;
using FFXProjectEditor.Services;
using FFXProjectEditor.Utils.Editing;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Modules.PlayerGrowthEditor
{
    internal partial class PlayerGrowthEditor_DataModel : ObservableObject
    {
        PlayerSaveTable? saveTable;
        PlayerRomTable? romTable;

        public ObservableCollection<PlayerKernelRow> LoadedCharacters { get; } = new();
        public ObservableCollection<PlayerKernelRow> DisplayedCharacters { get; } = new();

        [ObservableProperty] private string filterText = string.Empty;
        [ObservableProperty] private string loadSummary = "Loading ply_save.bin + ply_rom.bin...";
        [ObservableProperty] private string scopeSummary = "Safe scope: edit known stat and growth fields in existing entries only. Text refs and obscures stay preserved from the original bytes.";
        [ObservableProperty] private string selectedCharacterSummary = "Select a player/aeon slot to inspect its starting stats and growth coefficients.";
        [ObservableProperty] private PlayerKernelRow? selectedCharacter;
        [ObservableProperty] private ByteSnapshotEditorSession? editSession;
        [ObservableProperty] private AtlasEvidenceInfo? selectedCharacterEvidence;

        public PlayerGrowthEditor_DataModel()
        {
            LoadFromDisk();
        }

        partial void OnFilterTextChanged(string value) => ApplyFilter();

        partial void OnSelectedCharacterChanged(PlayerKernelRow? value)
        {
            UpdateSelectedSummary(value);
            SelectedCharacterEvidence = BuildSelectedCharacterEvidence(value);
        }

        public void RefreshFromDisk() => LoadFromDisk();
        public void Save() => EditSession?.Save();
        public void Undo() => EditSession?.Undo();
        public void Discard() => EditSession?.Discard();

        void LoadFromDisk()
        {
            EditSession?.Dispose();
            EditSession = null;
            saveTable = null;
            romTable = null;

            if (!Project_Service.Instance.IsProjectLoaded)
            {
                ClearRows("Project root not loaded.");
                return;
            }

            if (!File.Exists(Project_Service.Instance.Path_KernelPlySaveUs))
            {
                ClearRows("new_uspc/battle/kernel/ply_save.bin not found in the loaded workspace.");
                return;
            }

            if (!File.Exists(Project_Service.Instance.Path_KernelPlyRomUs))
            {
                ClearRows("new_uspc/battle/kernel/ply_rom.bin not found in the loaded workspace.");
                return;
            }

            byte[] saveBytes = File.ReadAllBytes(Project_Service.Instance.Path_KernelPlySaveUs);
            byte[] romBytes = File.ReadAllBytes(Project_Service.Instance.Path_KernelPlyRomUs);

            saveTable = PlayerKernel_File.ReadSave(saveBytes);
            romTable = PlayerKernel_File.ReadRom(romBytes);

            LoadRowsFromTables(saveTable, romTable, SelectedCharacter?.Index);

            EditSession = new ByteSnapshotEditorSession(
                BuildSnapshot,
                RestoreSnapshot,
                PersistSnapshot,
                "player stats / growth",
                BuildSnapshot());
        }

        void ClearRows(string message)
        {
            foreach (PlayerKernelRow row in LoadedCharacters)
                row.PropertyChanged -= CharacterRowChanged;

            LoadedCharacters.Clear();
            DisplayedCharacters.Clear();
            SelectedCharacter = null;
            LoadSummary = message;
        }

        void LoadRowsFromTables(PlayerSaveTable save, PlayerRomTable rom, int? preserveSelectionIndex)
        {
            foreach (PlayerKernelRow row in LoadedCharacters)
                row.PropertyChanged -= CharacterRowChanged;

            LoadedCharacters.Clear();
            DisplayedCharacters.Clear();

            Dictionary<int, PlayerRomEntry> romByIndex = rom.Entries.ToDictionary(entry => entry.Index);
            foreach (PlayerSaveEntry saveEntry in save.Entries)
            {
                if (!romByIndex.TryGetValue(saveEntry.Index, out PlayerRomEntry? romEntry))
                    continue;

                PlayerKernelRow row = PlayerKernelRow.Wrap(saveEntry, romEntry);
                row.PropertyChanged += CharacterRowChanged;
                LoadedCharacters.Add(row);
            }

            ApplyFilter();
            SelectedCharacter = preserveSelectionIndex.HasValue
                ? LoadedCharacters.FirstOrDefault(row => row.Index == preserveSelectionIndex.Value)
                : LoadedCharacters.FirstOrDefault();

            LoadSummary = $"Loaded {LoadedCharacters.Count} player/aeon growth entries from ply_save.bin + ply_rom.bin (US localized kernel).";
        }

        void ApplyFilter()
        {
            DisplayedCharacters.Clear();
            string normalized = FilterText.Trim();

            foreach (PlayerKernelRow row in LoadedCharacters)
            {
                if (normalized.Length == 0 || row.SearchBlob.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                    DisplayedCharacters.Add(row);
            }

            if (SelectedCharacter != null && !DisplayedCharacters.Contains(SelectedCharacter))
                SelectedCharacter = DisplayedCharacters.FirstOrDefault();
        }

        byte[] BuildSnapshot()
        {
            if (saveTable == null || romTable == null)
                return Array.Empty<byte>();

            saveTable = new PlayerSaveTable
            {
                OriginalBytes = saveTable.OriginalBytes,
                Header = saveTable.Header,
                Entries = LoadedCharacters.Select(row => row.ToSaveEntry()).ToList()
            };

            romTable = new PlayerRomTable
            {
                OriginalBytes = romTable.OriginalBytes,
                Header = romTable.Header,
                Entries = LoadedCharacters.Select(row => row.ToRomEntry()).ToList()
            };

            byte[] saveBytes = PlayerKernel_File.WriteSave(saveTable);
            byte[] romBytes = PlayerKernel_File.WriteRom(romTable);

            byte[] snapshot = new byte[8 + saveBytes.Length + romBytes.Length];
            WriteInt32(snapshot, 0x00, saveBytes.Length);
            Array.Copy(saveBytes, 0, snapshot, 0x04, saveBytes.Length);
            WriteInt32(snapshot, 0x04 + saveBytes.Length, romBytes.Length);
            Array.Copy(romBytes, 0, snapshot, 0x08 + saveBytes.Length, romBytes.Length);
            return snapshot;
        }

        void RestoreSnapshot(byte[] snapshot)
        {
            if (snapshot == null || snapshot.Length < 8)
                return;

            int saveLength = ReadInt32(snapshot, 0x00);
            if (saveLength <= 0 || 0x08 + saveLength > snapshot.Length)
                return;

            byte[] saveBytes = new byte[saveLength];
            Array.Copy(snapshot, 0x04, saveBytes, 0, saveLength);

            int romLengthOffset = 0x04 + saveLength;
            int romLength = ReadInt32(snapshot, romLengthOffset);
            int romBytesOffset = romLengthOffset + 0x04;
            if (romLength <= 0 || romBytesOffset + romLength > snapshot.Length)
                return;

            byte[] romBytes = new byte[romLength];
            Array.Copy(snapshot, romBytesOffset, romBytes, 0, romLength);

            saveTable = PlayerKernel_File.ReadSave(saveBytes);
            romTable = PlayerKernel_File.ReadRom(romBytes);
            LoadRowsFromTables(saveTable, romTable, SelectedCharacter?.Index);
        }

        void PersistSnapshot(byte[] snapshot)
        {
            if (snapshot == null || snapshot.Length < 8)
                return;

            int saveLength = ReadInt32(snapshot, 0x00);
            byte[] saveBytes = new byte[saveLength];
            Array.Copy(snapshot, 0x04, saveBytes, 0, saveLength);

            int romLengthOffset = 0x04 + saveLength;
            int romLength = ReadInt32(snapshot, romLengthOffset);
            byte[] romBytes = new byte[romLength];
            Array.Copy(snapshot, romLengthOffset + 0x04, romBytes, 0, romLength);

            File.WriteAllBytes(Project_Service.Instance.Path_KernelPlySaveUs, saveBytes);
            File.WriteAllBytes(Project_Service.Instance.Path_KernelPlyRomUs, romBytes);

            saveTable = PlayerKernel_File.ReadSave(saveBytes);
            romTable = PlayerKernel_File.ReadRom(romBytes);
        }

        void CharacterRowChanged(object? sender, PropertyChangedEventArgs e)
        {
            EditSession?.NotifyPotentialMutation();

            if (sender is PlayerKernelRow row && ReferenceEquals(row, SelectedCharacter))
            {
                UpdateSelectedSummary(row);
                SelectedCharacterEvidence = BuildSelectedCharacterEvidence(row);
            }
        }

        void UpdateSelectedSummary(PlayerKernelRow? row)
        {
            SelectedCharacterSummary = row == null
                ? "Select a player/aeon slot to inspect its starting stats and growth coefficients."
                : $"{row.Label} · Base HP/MP {row.BaseHp}/{row.BaseMp} · AP formula {row.ApFormulaSummary}";
        }

        // Read-only Spira Data Atlas evidence for the selected player/aeon slot (Jarvis-TIDUS provider, v2.65.0),
        // value-guarded so the badge only shows while the slot's growth values still match the compiled byte-grounded
        // corpus. It anchors on three gate-proven growth values that span both files and both editor cards — Base HP
        // (ply_save / Start State), AP-requirement max and HP growth coefficient A (ply_rom / Growth Curves) — exactly
        // the same anchoring depth the shipped Mix/Aeon badges use. Editing any anchor breaks the value-guard and the
        // strip hides itself (no stale badge). Returns null when there is no detail or any anchor diverges. Read-only:
        // this never writes and never touches the PlayerGrowth save path.
        static AtlasEvidenceInfo? BuildSelectedCharacterEvidence(PlayerKernelRow? row)
        {
            if (row == null)
                return null;

            (string Source, string Field, int Value)[] anchors =
            {
                ("ply_save", "basehp", row.BaseHp),
                ("ply_rom", "apreqmax", row.ApReqMax),
                ("ply_rom", "hpcoefa", row.HpCoefficientA),
            };

            SpiraDataAtlasDetailEntry? detail = null;
            foreach ((string source, string field, int value) in anchors)
            {
                if (!SpiraDataAtlasCatalog.TryGetPlayerGrowthStat(source, row.Index, field, value, out SpiraDataAtlasDetailEntry? anchorDetail) || anchorDetail == null)
                    return null;

                detail ??= anchorDetail;
            }

            return detail == null ? null : AtlasEvidenceInfo.ForDetail(detail);
        }

        static int ReadInt32(byte[] bytes, int offset)
        {
            if (offset < 0 || offset + 4 > bytes.Length)
                return 0;

            return bytes[offset]
                | (bytes[offset + 1] << 8)
                | (bytes[offset + 2] << 16)
                | (bytes[offset + 3] << 24);
        }

        static void WriteInt32(byte[] bytes, int offset, int value)
        {
            if (offset < 0 || offset + 4 > bytes.Length)
                return;

            bytes[offset] = unchecked((byte)(value & 0xFF));
            bytes[offset + 1] = unchecked((byte)((value >> 8) & 0xFF));
            bytes[offset + 2] = unchecked((byte)((value >> 16) & 0xFF));
            bytes[offset + 3] = unchecked((byte)((value >> 24) & 0xFF));
        }

        internal partial class PlayerKernelRow : ObservableObject
        {
            [ObservableProperty] private int baseHp;
            [ObservableProperty] private int baseMp;
            [ObservableProperty] private int baseStrength;
            [ObservableProperty] private int baseDefense;
            [ObservableProperty] private int baseMagic;
            [ObservableProperty] private int baseMagicDefense;
            [ObservableProperty] private int baseAgility;
            [ObservableProperty] private int baseLuck;
            [ObservableProperty] private int baseEvasion;
            [ObservableProperty] private int baseAccuracy;
            [ObservableProperty] private int currentAp;
            [ObservableProperty] private int currentHp;
            [ObservableProperty] private int currentMp;
            [ObservableProperty] private int maxHp;
            [ObservableProperty] private int maxMp;
            [ObservableProperty] private int equippedWeaponIndex;
            [ObservableProperty] private int equippedArmorIndex;
            [ObservableProperty] private int strength;
            [ObservableProperty] private int defense;
            [ObservableProperty] private int magic;
            [ObservableProperty] private int magicDefense;
            [ObservableProperty] private int agility;
            [ObservableProperty] private int luck;
            [ObservableProperty] private int evasion;
            [ObservableProperty] private int accuracy;
            [ObservableProperty] private int poisonDamagePercent;
            [ObservableProperty] private int overdriveMode;
            [ObservableProperty] private int overdriveCurrent;
            [ObservableProperty] private int overdriveMax;
            [ObservableProperty] private int sphereLevelsAvailable;
            [ObservableProperty] private int sphereLevelsUsed;
            [ObservableProperty] private int encounterCount;
            [ObservableProperty] private int killCount;
            [ObservableProperty] private int genreByte;
            [ObservableProperty] private int apReqCoefficientA;
            [ObservableProperty] private int apReqCoefficientB;
            [ObservableProperty] private int apReqCoefficientC;
            [ObservableProperty] private int apReqMax;
            [ObservableProperty] private int hpCoefficientA;
            [ObservableProperty] private int hpCoefficientB;
            [ObservableProperty] private int mpCoefficientA;
            [ObservableProperty] private int mpCoefficientB;
            [ObservableProperty] private int strengthCoefficientA;
            [ObservableProperty] private int strengthCoefficientB;
            [ObservableProperty] private int defenseCoefficientA;
            [ObservableProperty] private int defenseCoefficientB;
            [ObservableProperty] private int magicCoefficientA;
            [ObservableProperty] private int magicCoefficientB;
            [ObservableProperty] private int magicDefenseCoefficientA;
            [ObservableProperty] private int magicDefenseCoefficientB;
            [ObservableProperty] private int agilityCoefficientA;
            [ObservableProperty] private int agilityCoefficientB;
            [ObservableProperty] private int evasionCoefficientA;
            [ObservableProperty] private int evasionCoefficientB;
            [ObservableProperty] private int accuracyCoefficientA;
            [ObservableProperty] private int accuracyCoefficientB;
            [ObservableProperty] private int tailFlags;

            public required int Index { get; init; }
            public required string Label { get; init; }
            public required byte[] SaveRawBytes { get; init; }
            public required byte[] RomRawBytes { get; init; }

            public string IndexLabel => $"#{Index:D2}";
            public string RoleSummary => Index <= 6 ? "Playable party member" : Index <= 17 ? "Aeon / guest slot" : "Unmapped slot";
            public string Summary => $"Base {BaseHp}/{BaseMp} HP/MP · STR {BaseStrength} · AGI {BaseAgility}";
            public string SearchBlob => $"{Index:D2} {Label} {RoleSummary} {Summary} AP {ApFormulaSummary}";
            public string ApFormulaSummary => $"{ApReqCoefficientA}A + {ApReqCoefficientB}B + {ApReqCoefficientC}C (max {ApReqMax})";
            public string AeonGrowthSummary =>
                $"HP {HpCoefficientA}/{HpCoefficientB} · MP {MpCoefficientA}/{MpCoefficientB} · STR {StrengthCoefficientA}/{StrengthCoefficientB} · DEF {DefenseCoefficientA}/{DefenseCoefficientB}";
            public string ObscureFieldSummary => $"Genre raw {GenreByte:X2}h · Tail raw {TailFlags:X4}h";

            public static PlayerKernelRow Wrap(PlayerSaveEntry saveEntry, PlayerRomEntry romEntry)
            {
                return new PlayerKernelRow
                {
                    Index = saveEntry.Index,
                    Label = saveEntry.Label,
                    SaveRawBytes = saveEntry.RawBytes.ToArray(),
                    RomRawBytes = romEntry.RawBytes.ToArray(),
                    BaseHp = saveEntry.BaseHp,
                    BaseMp = saveEntry.BaseMp,
                    BaseStrength = saveEntry.BaseStrength,
                    BaseDefense = saveEntry.BaseDefense,
                    BaseMagic = saveEntry.BaseMagic,
                    BaseMagicDefense = saveEntry.BaseMagicDefense,
                    BaseAgility = saveEntry.BaseAgility,
                    BaseLuck = saveEntry.BaseLuck,
                    BaseEvasion = saveEntry.BaseEvasion,
                    BaseAccuracy = saveEntry.BaseAccuracy,
                    CurrentAp = saveEntry.CurrentAp,
                    CurrentHp = saveEntry.CurrentHp,
                    CurrentMp = saveEntry.CurrentMp,
                    MaxHp = saveEntry.MaxHp,
                    MaxMp = saveEntry.MaxMp,
                    EquippedWeaponIndex = saveEntry.EquippedWeaponIndex,
                    EquippedArmorIndex = saveEntry.EquippedArmorIndex,
                    Strength = saveEntry.Strength,
                    Defense = saveEntry.Defense,
                    Magic = saveEntry.Magic,
                    MagicDefense = saveEntry.MagicDefense,
                    Agility = saveEntry.Agility,
                    Luck = saveEntry.Luck,
                    Evasion = saveEntry.Evasion,
                    Accuracy = saveEntry.Accuracy,
                    PoisonDamagePercent = saveEntry.PoisonDamagePercent,
                    OverdriveMode = saveEntry.OverdriveMode,
                    OverdriveCurrent = saveEntry.OverdriveCurrent,
                    OverdriveMax = saveEntry.OverdriveMax,
                    SphereLevelsAvailable = saveEntry.SphereLevelsAvailable,
                    SphereLevelsUsed = saveEntry.SphereLevelsUsed,
                    EncounterCount = saveEntry.EncounterCount,
                    KillCount = saveEntry.KillCount,
                    GenreByte = romEntry.GenreByte,
                    ApReqCoefficientA = romEntry.ApReqCoefficientA,
                    ApReqCoefficientB = romEntry.ApReqCoefficientB,
                    ApReqCoefficientC = romEntry.ApReqCoefficientC,
                    ApReqMax = romEntry.ApReqMax,
                    HpCoefficientA = romEntry.HpCoefficientA,
                    HpCoefficientB = romEntry.HpCoefficientB,
                    MpCoefficientA = romEntry.MpCoefficientA,
                    MpCoefficientB = romEntry.MpCoefficientB,
                    StrengthCoefficientA = romEntry.StrengthCoefficientA,
                    StrengthCoefficientB = romEntry.StrengthCoefficientB,
                    DefenseCoefficientA = romEntry.DefenseCoefficientA,
                    DefenseCoefficientB = romEntry.DefenseCoefficientB,
                    MagicCoefficientA = romEntry.MagicCoefficientA,
                    MagicCoefficientB = romEntry.MagicCoefficientB,
                    MagicDefenseCoefficientA = romEntry.MagicDefenseCoefficientA,
                    MagicDefenseCoefficientB = romEntry.MagicDefenseCoefficientB,
                    AgilityCoefficientA = romEntry.AgilityCoefficientA,
                    AgilityCoefficientB = romEntry.AgilityCoefficientB,
                    EvasionCoefficientA = romEntry.EvasionCoefficientA,
                    EvasionCoefficientB = romEntry.EvasionCoefficientB,
                    AccuracyCoefficientA = romEntry.AccuracyCoefficientA,
                    AccuracyCoefficientB = romEntry.AccuracyCoefficientB,
                    TailFlags = romEntry.TailFlags
                };
            }

            public PlayerSaveEntry ToSaveEntry()
            {
                return new PlayerSaveEntry
                {
                    Index = Index,
                    Label = Label,
                    RawBytes = SaveRawBytes.ToArray(),
                    BaseHp = BaseHp,
                    BaseMp = BaseMp,
                    BaseStrength = BaseStrength,
                    BaseDefense = BaseDefense,
                    BaseMagic = BaseMagic,
                    BaseMagicDefense = BaseMagicDefense,
                    BaseAgility = BaseAgility,
                    BaseLuck = BaseLuck,
                    BaseEvasion = BaseEvasion,
                    BaseAccuracy = BaseAccuracy,
                    CurrentAp = CurrentAp,
                    CurrentHp = CurrentHp,
                    CurrentMp = CurrentMp,
                    MaxHp = MaxHp,
                    MaxMp = MaxMp,
                    EquippedWeaponIndex = EquippedWeaponIndex,
                    EquippedArmorIndex = EquippedArmorIndex,
                    Strength = Strength,
                    Defense = Defense,
                    Magic = Magic,
                    MagicDefense = MagicDefense,
                    Agility = Agility,
                    Luck = Luck,
                    Evasion = Evasion,
                    Accuracy = Accuracy,
                    PoisonDamagePercent = PoisonDamagePercent,
                    OverdriveMode = OverdriveMode,
                    OverdriveCurrent = OverdriveCurrent,
                    OverdriveMax = OverdriveMax,
                    SphereLevelsAvailable = SphereLevelsAvailable,
                    SphereLevelsUsed = SphereLevelsUsed,
                    EncounterCount = EncounterCount,
                    KillCount = KillCount
                };
            }

            public PlayerRomEntry ToRomEntry()
            {
                return new PlayerRomEntry
                {
                    Index = Index,
                    Label = Label,
                    RawBytes = RomRawBytes.ToArray(),
                    GenreByte = GenreByte,
                    ApReqCoefficientA = ApReqCoefficientA,
                    ApReqCoefficientB = ApReqCoefficientB,
                    ApReqCoefficientC = ApReqCoefficientC,
                    ApReqMax = ApReqMax,
                    HpCoefficientA = HpCoefficientA,
                    HpCoefficientB = HpCoefficientB,
                    MpCoefficientA = MpCoefficientA,
                    MpCoefficientB = MpCoefficientB,
                    StrengthCoefficientA = StrengthCoefficientA,
                    StrengthCoefficientB = StrengthCoefficientB,
                    DefenseCoefficientA = DefenseCoefficientA,
                    DefenseCoefficientB = DefenseCoefficientB,
                    MagicCoefficientA = MagicCoefficientA,
                    MagicCoefficientB = MagicCoefficientB,
                    MagicDefenseCoefficientA = MagicDefenseCoefficientA,
                    MagicDefenseCoefficientB = MagicDefenseCoefficientB,
                    AgilityCoefficientA = AgilityCoefficientA,
                    AgilityCoefficientB = AgilityCoefficientB,
                    EvasionCoefficientA = EvasionCoefficientA,
                    EvasionCoefficientB = EvasionCoefficientB,
                    AccuracyCoefficientA = AccuracyCoefficientA,
                    AccuracyCoefficientB = AccuracyCoefficientB,
                    TailFlags = unchecked((ushort)TailFlags)
                };
            }

            partial void OnBaseHpChanged(int value) => NotifyComputedChanged();
            partial void OnBaseMpChanged(int value) => NotifyComputedChanged();
            partial void OnBaseStrengthChanged(int value) => NotifyComputedChanged();
            partial void OnBaseAgilityChanged(int value) => NotifyComputedChanged();
            partial void OnApReqCoefficientAChanged(int value) => NotifyComputedChanged();
            partial void OnApReqCoefficientBChanged(int value) => NotifyComputedChanged();
            partial void OnApReqCoefficientCChanged(int value) => NotifyComputedChanged();
            partial void OnApReqMaxChanged(int value) => NotifyComputedChanged();
            partial void OnHpCoefficientAChanged(int value) => NotifyComputedChanged();
            partial void OnHpCoefficientBChanged(int value) => NotifyComputedChanged();
            partial void OnStrengthCoefficientAChanged(int value) => NotifyComputedChanged();
            partial void OnStrengthCoefficientBChanged(int value) => NotifyComputedChanged();
            partial void OnDefenseCoefficientAChanged(int value) => NotifyComputedChanged();
            partial void OnDefenseCoefficientBChanged(int value) => NotifyComputedChanged();
            partial void OnGenreByteChanged(int value) => NotifyComputedChanged();
            partial void OnTailFlagsChanged(int value) => NotifyComputedChanged();

            void NotifyComputedChanged()
            {
                OnPropertyChanged(nameof(Summary));
                OnPropertyChanged(nameof(SearchBlob));
                OnPropertyChanged(nameof(ApFormulaSummary));
                OnPropertyChanged(nameof(AeonGrowthSummary));
                OnPropertyChanged(nameof(ObscureFieldSummary));
            }
        }
    }
}
