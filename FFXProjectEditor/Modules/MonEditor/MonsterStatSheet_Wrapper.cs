using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Monster;
using FFXProjectEditor.Utils;
using FFXProjectEditor.Utils.Encoding;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace FFXProjectEditor.Modules.MonEditor
{
    internal partial class MonsterStatSheet_Wrapper : ObservableObject
    {
        public sealed class ConfiguredAbilityDigest
        {
            public required ushort RawGameIndex { get; init; }
            public required byte Category { get; init; }
            public required ushort Index { get; init; }
            public required string CategoryLabel { get; init; }
            public required string DisplayName { get; init; }
            public required string Sources { get; init; }
            public required bool IsStatSheetConfigured { get; init; }
            public required bool IsAiReferenced { get; init; }
            public string RawHex => $"{RawGameIndex:X4}h";
            public string CoverageLabel => IsStatSheetConfigured && IsAiReferenced
                ? "Configured + AI"
                : IsAiReferenced
                    ? "AI Referenced"
                    : "Stat Sheet";
        }

        // Stats
        [ObservableProperty] public uint hp;
        [ObservableProperty] public uint mp;
        [ObservableProperty] public uint hpOverkill;
        [ObservableProperty] public byte strength;
        [ObservableProperty] public byte defense;
        [ObservableProperty] public byte magic;
        [ObservableProperty] public byte magicDefense;
        [ObservableProperty] public byte agility;
        [ObservableProperty] public byte luck;
        [ObservableProperty] public byte evasion;
        [ObservableProperty] public byte accuracy;

        [ObservableProperty] public bool prop_Armored;
        [ObservableProperty] public bool prop_ImmunityFractionalDamage;
        [ObservableProperty] public bool prop_ImmunityLife;
        [ObservableProperty] public bool prop_ImmunitySensor;
        [ObservableProperty] public bool prop_ImmunityScanAgain_Maybe;
        [ObservableProperty] public bool prop_ImmunityPhysicalDamage;
        [ObservableProperty] public bool prop_ImmunityMagicDamage;
        [ObservableProperty] public bool prop_ImmunityAllDamage;
        [ObservableProperty] public bool prop_ImmunityDelay;
        [ObservableProperty] public bool prop_ImmunitySlice_Maybe;
        [ObservableProperty] public bool prop_ImmunityBribe_Maybe;

        [ObservableProperty] public byte poisonDamage;

        // Elements
        [ObservableProperty] public ElementalWeaknessData elementalWeakness;

        // Status
        [ObservableProperty] public StatusByteList statusResistance;

        [ObservableProperty] public bool auto_Death;
        [ObservableProperty] public bool auto_Zombie;
        [ObservableProperty] public bool auto_Petrify;
        [ObservableProperty] public bool auto_Poison;
        [ObservableProperty] public bool auto_BreakPower;
        [ObservableProperty] public bool auto_BreakMagic;
        [ObservableProperty] public bool auto_BreakArmor;
        [ObservableProperty] public bool auto_BreakMental;
        [ObservableProperty] public bool auto_Confuse;
        [ObservableProperty] public bool auto_Berserk;
        [ObservableProperty] public bool auto_Provoke;
        [ObservableProperty] public bool auto_Threaten;
        [ObservableProperty] public bool auto_Sleep;
        [ObservableProperty] public bool auto_Silence;
        [ObservableProperty] public bool auto_Darkness;
        [ObservableProperty] public bool auto_Shell;
        [ObservableProperty] public bool auto_Protect;
        [ObservableProperty] public bool auto_Reflect;
        [ObservableProperty] public bool auto_NulTide;
        [ObservableProperty] public bool auto_NulBlaze;
        [ObservableProperty] public bool auto_NulShock;
        [ObservableProperty] public bool auto_NulFrost;
        [ObservableProperty] public bool auto_Regen;
        [ObservableProperty] public bool auto_Haste;
        [ObservableProperty] public bool auto_Slow;
        [ObservableProperty] public bool auto_Scan;
        [ObservableProperty] public bool auto_DistillPower;
        [ObservableProperty] public bool auto_DistillMana;
        [ObservableProperty] public bool auto_DistillSpeed;
        [ObservableProperty] public bool auto_DistillAbility;
        [ObservableProperty] public bool auto_Shield;
        [ObservableProperty] public bool auto_Boost;
        [ObservableProperty] public bool auto_Eject;
        [ObservableProperty] public bool auto_AutoLife;
        [ObservableProperty] public bool auto_Curse;
        [ObservableProperty] public bool auto_Defend;
        [ObservableProperty] public bool auto_Guard;
        [ObservableProperty] public bool auto_Sentinel;
        [ObservableProperty] public bool auto_Doom;
        [ObservableProperty] public bool immunity_Scan;
        [ObservableProperty] public bool immunity_DistillPower;
        [ObservableProperty] public bool immunity_DistillMana;
        [ObservableProperty] public bool immunity_DistillSpeed;
        [ObservableProperty] public bool immunity_DistillAbility;
        [ObservableProperty] public bool immunity_Shield;
        [ObservableProperty] public bool immunity_Boost;
        [ObservableProperty] public bool immunity_Eject;
        [ObservableProperty] public bool immunity_AutoLife;
        [ObservableProperty] public bool immunity_Curse;
        [ObservableProperty] public bool immunity_Defend;
        [ObservableProperty] public bool immunity_Guard;
        [ObservableProperty] public bool immunity_Sentinel;
        [ObservableProperty] public bool immunity_Doom;

        // Abilities
        [ObservableProperty] public GameIndex_Wrapper ability1;
        [ObservableProperty] public GameIndex_Wrapper ability2;
        [ObservableProperty] public GameIndex_Wrapper ability3;
        [ObservableProperty] public GameIndex_Wrapper ability4;
        [ObservableProperty] public GameIndex_Wrapper ability5;
        [ObservableProperty] public GameIndex_Wrapper ability6;
        [ObservableProperty] public GameIndex_Wrapper ability7;
        [ObservableProperty] public GameIndex_Wrapper ability8;
        [ObservableProperty] public GameIndex_Wrapper ability9;
        [ObservableProperty] public GameIndex_Wrapper ability10;
        [ObservableProperty] public GameIndex_Wrapper ability11;
        [ObservableProperty] public GameIndex_Wrapper ability12;
        [ObservableProperty] public GameIndex_Wrapper ability13;
        [ObservableProperty] public GameIndex_Wrapper ability14;
        [ObservableProperty] public GameIndex_Wrapper ability15;
        [ObservableProperty] public GameIndex_Wrapper ability16;

        [ObservableProperty] public GameIndex_Wrapper forcedAbility;
        public ObservableCollection<ConfiguredAbilityDigest> ConfiguredAbilityDigests { get; } = [];
        public ObservableCollection<ConfiguredAbilityDigest> FullCastableAbilityDigests { get; } = [];
        public string ConfiguredAbilityDigestsSummary => ConfiguredAbilityDigests.Count == 0
            ? "No configured menu abilities on the stat sheet."
            : $"{ConfiguredAbilityDigests.Count} unique configured abilities on the stat sheet.";
        public bool HasConfiguredAbilityDigests => ConfiguredAbilityDigests.Count > 0;
        public string FullCastableAbilityDigestsSummary
        {
            get
            {
                int aiReferencedUniqueCount = GetAiAbilityReferences()
                    .Select(reference => reference.RawGameIndex)
                    .Distinct()
                    .Count();

                if (FullCastableAbilityDigests.Count == 0)
                {
                    return MonsterAiAbilityCorpus_Service.IsAvailable
                        ? "No configured or AI-referenced castable abilities were found for this monster."
                        : "No configured abilities found, and the AI parser corpus is unavailable.";
                }

                if (!MonsterAiAbilityCorpus_Service.IsAvailable)
                {
                    return $"{FullCastableAbilityDigests.Count} unique configured abilities. AI parser corpus unavailable, so this is still stat-sheet only.";
                }

                if (aiReferencedUniqueCount == 0)
                {
                    return $"{FullCastableAbilityDigests.Count} unique configured abilities. No extra AI command references were found for this monster in the loaded corpus.";
                }

                return $"{FullCastableAbilityDigests.Count} unique castable abilities after merging stat-sheet slots with {aiReferencedUniqueCount} AI-referenced commands.";
            }
        }
        public string FullCastableAbilityDigestsNotes
        {
            get
            {
                if (!MonsterAiAbilityCorpus_Service.IsAvailable)
                {
                    return $"{MonsterAiAbilityCorpus_Service.StatusSummary}";
                }

                return GetAiAbilityReferences().Count > 0
                    ? $"Merged stat-sheet configuration with parser-derived AI references. Sources tagged as AI Script / AI Ability List / AI Forced Action come from monsterAiOutput.txt. {MonsterAiAbilityCorpus_Service.StatusSummary}"
                    : $"The AI parser corpus is loaded, but this monster did not expose extra command references there. {MonsterAiAbilityCorpus_Service.StatusSummary}";
            }
        }
        public bool HasFullCastableAbilityDigests => FullCastableAbilityDigests.Count > 0;

        [ObservableProperty] public short monsterId;
        [ObservableProperty] public short modelId;
        [ObservableProperty] public byte ctbIconId;
        [ObservableProperty] public sbyte doomCount;
        [ObservableProperty] public sbyte arenaId;
        [ObservableProperty] public byte arenaIdPadding;
        [ObservableProperty] public short model2Id;


        [ObservableProperty] public byte[] nameScriptBytes;
        [ObservableProperty] public byte[] sensorScriptBytes;
        [ObservableProperty] public byte[] japaneseOnlyText1ScriptBytes;
        [ObservableProperty] public byte[] scanScriptBytes;
        [ObservableProperty] public byte[] japaneseOnlyText2ScriptBytes;
        // Text ids kept to keep the original data. Ideally this would be calculated.
        [ObservableProperty] public ushort nameScriptId;
        [ObservableProperty] public ushort sensorScriptId;
        [ObservableProperty] public ushort japaneseOnlyText1ScriptId;
        [ObservableProperty] public ushort scanScriptId;
        [ObservableProperty] public ushort japaneseOnlyText2ScriptId;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Name))]
        [NotifyPropertyChangedFor(nameof(Sensor))]
        [NotifyPropertyChangedFor(nameof(Scan))]
        [NotifyPropertyChangedFor(nameof(TextLocaleSummary))]
        [NotifyPropertyChangedFor(nameof(CanEditLocalizedText))]
        [NotifyPropertyChangedFor(nameof(IsLocalizedTextReadOnly))]
        [NotifyPropertyChangedFor(nameof(LocalizedTextAuthoringSummary))]
        public string selectedTextLocale = DefaultTextLocale;

        string? englishName;
        string? englishSensor;
        string? englishScan;
        bool canWriteEnglishLocalization;

        public static string DefaultTextLocale => "English";
        public IReadOnlyList<string> TextLocaleOptions { get; } = ["English", "Japanese"];

        public string Name
        {
            get => ResolveLocalizedText(nameScriptBytes, englishName);
            set => SetLocalizedText(ref englishName, value, nameof(Name));
        }

        public string Sensor
        {
            get => ResolveLocalizedText(sensorScriptBytes, englishSensor);
            set => SetLocalizedText(ref englishSensor, value, nameof(Sensor));
        }

        public string Scan
        {
            get => ResolveLocalizedText(scanScriptBytes, englishScan);
            set => SetLocalizedText(ref englishScan, value, nameof(Scan));
        }

        public string TextLocaleSummary => SelectedTextLocale == "English"
            ? canWriteEnglishLocalization
                ? "English localization from monster1/2/3.bin. Empty editable fields stay empty instead of showing Japanese fallback."
                : "English localization not found for this monster. Showing Japanese fallback."
            : "Japanese text from the monster's native script bytes.";
        public bool CanEditLocalizedText => SelectedTextLocale == DefaultTextLocale && canWriteEnglishLocalization;
        public bool IsLocalizedTextReadOnly => !CanEditLocalizedText;
        public string LocalizedTextAuthoringSummary => CanEditLocalizedText
            ? "Editing here writes the safe English localization fields in monster1/2/3.bin. Native Japanese script bytes stay read-only in this panel."
            : SelectedTextLocale == DefaultTextLocale
                ? "English text is view-only here because no safe monster1/2/3 localization row is available for this monster."
                : "Japanese native script text stays view-only here. Switch back to English to edit the safe localization fields.";
        public string? EnglishNameValue => englishName;
        public string? EnglishSensorValue => englishSensor;
        public string? EnglishScanValue => englishScan;

        bool HasAnyEnglishLocalization =>
            !string.IsNullOrWhiteSpace(englishName) ||
            !string.IsNullOrWhiteSpace(englishSensor) ||
            !string.IsNullOrWhiteSpace(englishScan);

        public static MonsterStatSheet_Wrapper Wrap(Monster_StatSheet sheet)
        {
            MonsterStatSheet_Wrapper wrapper = new();

            PropertyUtil.CopyProperties(sheet, wrapper);

            wrapper.Ability1 = GameIndex_Wrapper.Wrap(sheet.Abilities[0]);
            wrapper.Ability2 = GameIndex_Wrapper.Wrap(sheet.Abilities[1]);
            wrapper.Ability3 = GameIndex_Wrapper.Wrap(sheet.Abilities[2]);
            wrapper.Ability4 = GameIndex_Wrapper.Wrap(sheet.Abilities[3]);
            wrapper.Ability5 = GameIndex_Wrapper.Wrap(sheet.Abilities[4]);
            wrapper.Ability6 = GameIndex_Wrapper.Wrap(sheet.Abilities[5]);
            wrapper.Ability7 = GameIndex_Wrapper.Wrap(sheet.Abilities[6]);
            wrapper.Ability8 = GameIndex_Wrapper.Wrap(sheet.Abilities[7]);
            wrapper.Ability9 = GameIndex_Wrapper.Wrap(sheet.Abilities[8]);
            wrapper.Ability10 = GameIndex_Wrapper.Wrap(sheet.Abilities[9]);
            wrapper.Ability11 = GameIndex_Wrapper.Wrap(sheet.Abilities[10]);
            wrapper.Ability12 = GameIndex_Wrapper.Wrap(sheet.Abilities[11]);
            wrapper.Ability13 = GameIndex_Wrapper.Wrap(sheet.Abilities[12]);
            wrapper.Ability14 = GameIndex_Wrapper.Wrap(sheet.Abilities[13]);
            wrapper.Ability15 = GameIndex_Wrapper.Wrap(sheet.Abilities[14]);
            wrapper.Ability16 = GameIndex_Wrapper.Wrap(sheet.Abilities[15]);

            wrapper.ForcedAbility = GameIndex_Wrapper.Wrap(sheet.ForcedAction);
            wrapper.InitializeConfiguredAbilityTracking();
            wrapper.RebuildConfiguredAbilityDigests();

            return wrapper;
        }

        public Monster_StatSheet Unwrap()
        {
            Monster_StatSheet sheet = new();

            PropertyUtil.CopyProperties(this, sheet);

            sheet.Abilities[0] = Ability1.Unwrap();
            sheet.Abilities[1] = Ability2.Unwrap();
            sheet.Abilities[2] = Ability3.Unwrap();
            sheet.Abilities[3] = Ability4.Unwrap();
            sheet.Abilities[4] = Ability5.Unwrap();
            sheet.Abilities[5] = Ability6.Unwrap();
            sheet.Abilities[6] = Ability7.Unwrap();
            sheet.Abilities[7] = Ability8.Unwrap();
            sheet.Abilities[8] = Ability9.Unwrap();
            sheet.Abilities[9] = Ability10.Unwrap();
            sheet.Abilities[10] = Ability11.Unwrap();
            sheet.Abilities[11] = Ability12.Unwrap();
            sheet.Abilities[12] = Ability13.Unwrap();
            sheet.Abilities[13] = Ability14.Unwrap();
            sheet.Abilities[14] = Ability15.Unwrap();
            sheet.Abilities[15] = Ability16.Unwrap();

            sheet.ForcedAction = ForcedAbility.Unwrap();

            return sheet;
        }

        public void ApplyEnglishLocalization(string? name, string? sensor, string? scan, bool canEdit = false)
        {
            englishName = NormalizeLocalizationField(name);
            englishSensor = NormalizeLocalizationField(sensor);
            englishScan = NormalizeLocalizationField(scan);
            canWriteEnglishLocalization = canEdit;

            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(Sensor));
            OnPropertyChanged(nameof(Scan));
            OnPropertyChanged(nameof(TextLocaleSummary));
            OnPropertyChanged(nameof(CanEditLocalizedText));
            OnPropertyChanged(nameof(IsLocalizedTextReadOnly));
            OnPropertyChanged(nameof(LocalizedTextAuthoringSummary));
        }

        void InitializeConfiguredAbilityTracking()
        {
            foreach (GameIndex_Wrapper wrapper in EnumerateAbilityWrappers())
            {
                wrapper.PropertyChanged += ConfiguredAbilityWrapper_PropertyChanged;
            }
        }

        void ConfiguredAbilityWrapper_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(GameIndex_Wrapper.Category) ||
                e.PropertyName == nameof(GameIndex_Wrapper.Index))
            {
                RebuildConfiguredAbilityDigests();
            }
        }

        void RebuildConfiguredAbilityDigests()
        {
            Dictionary<ushort, (GameIndex_Wrapper Wrapper, List<string> Sources)> combined = [];

            foreach ((string source, GameIndex_Wrapper wrapper) in EnumerateAbilitySlots())
            {
                if (IsEmptyAbility(wrapper))
                {
                    continue;
                }

                ushort rawGameIndex = wrapper.Unwrap();
                if (!combined.TryGetValue(rawGameIndex, out (GameIndex_Wrapper Wrapper, List<string> Sources) entry))
                {
                    entry = (wrapper, []);
                    combined.Add(rawGameIndex, entry);
                }

                entry.Sources.Add(source);
            }

            ConfiguredAbilityDigests.Clear();
            FullCastableAbilityDigests.Clear();

            foreach ((ushort rawGameIndex, (GameIndex_Wrapper Wrapper, List<string> Sources) entry) in combined.OrderBy(x => x.Key))
            {
                ConfiguredAbilityDigests.Add(new ConfiguredAbilityDigest
                {
                    RawGameIndex = rawGameIndex,
                    Category = entry.Wrapper.Category,
                    Index = entry.Wrapper.Index,
                    CategoryLabel = GetCategoryLabel(entry.Wrapper.Category),
                    DisplayName = GetAbilityDisplayName(entry.Wrapper),
                    Sources = string.Join(", ", entry.Sources),
                    IsStatSheetConfigured = true,
                    IsAiReferenced = false
                });
            }

            Dictionary<ushort, (GameIndex_Wrapper Wrapper, List<string> Sources, bool IsStatSheetConfigured, bool IsAiReferenced)> fullCoverage = combined
                .ToDictionary(
                    pair => pair.Key,
                    pair => (pair.Value.Wrapper, new List<string>(pair.Value.Sources), true, false));

            foreach (MonsterAiAbilityCorpus_Service.MonsterAiAbilityReference reference in GetAiAbilityReferences())
            {
                if (!fullCoverage.TryGetValue(reference.RawGameIndex, out (GameIndex_Wrapper Wrapper, List<string> Sources, bool IsStatSheetConfigured, bool IsAiReferenced) entry))
                {
                    entry = (GameIndex_Wrapper.Wrap(reference.RawGameIndex), new List<string>(), false, false);
                    fullCoverage.Add(reference.RawGameIndex, entry);
                }

                AddSourceIfMissing(entry.Sources, reference.Source);
                fullCoverage[reference.RawGameIndex] = (entry.Wrapper, entry.Sources, entry.IsStatSheetConfigured, true);
            }

            foreach ((ushort rawGameIndex, (GameIndex_Wrapper Wrapper, List<string> Sources, bool IsStatSheetConfigured, bool IsAiReferenced) entry) in fullCoverage.OrderBy(x => x.Key))
            {
                FullCastableAbilityDigests.Add(new ConfiguredAbilityDigest
                {
                    RawGameIndex = rawGameIndex,
                    Category = entry.Wrapper.Category,
                    Index = entry.Wrapper.Index,
                    CategoryLabel = GetCategoryLabel(entry.Wrapper.Category),
                    DisplayName = GetAbilityDisplayName(entry.Wrapper),
                    Sources = string.Join(", ", entry.Sources),
                    IsStatSheetConfigured = entry.IsStatSheetConfigured,
                    IsAiReferenced = entry.IsAiReferenced
                });
            }

            OnPropertyChanged(nameof(ConfiguredAbilityDigestsSummary));
            OnPropertyChanged(nameof(HasConfiguredAbilityDigests));
            OnPropertyChanged(nameof(FullCastableAbilityDigestsSummary));
            OnPropertyChanged(nameof(FullCastableAbilityDigestsNotes));
            OnPropertyChanged(nameof(HasFullCastableAbilityDigests));
        }

        IEnumerable<GameIndex_Wrapper> EnumerateAbilityWrappers()
        {
            foreach ((_, GameIndex_Wrapper wrapper) in EnumerateAbilitySlots())
            {
                yield return wrapper;
            }
        }

        IEnumerable<(string Source, GameIndex_Wrapper Wrapper)> EnumerateAbilitySlots()
        {
            yield return ("Forced", ForcedAbility);
            yield return ("Ability 1", Ability1);
            yield return ("Ability 2", Ability2);
            yield return ("Ability 3", Ability3);
            yield return ("Ability 4", Ability4);
            yield return ("Ability 5", Ability5);
            yield return ("Ability 6", Ability6);
            yield return ("Ability 7", Ability7);
            yield return ("Ability 8", Ability8);
            yield return ("Ability 9", Ability9);
            yield return ("Ability 10", Ability10);
            yield return ("Ability 11", Ability11);
            yield return ("Ability 12", Ability12);
            yield return ("Ability 13", Ability13);
            yield return ("Ability 14", Ability14);
            yield return ("Ability 15", Ability15);
            yield return ("Ability 16", Ability16);
        }

        static bool IsEmptyAbility(GameIndex_Wrapper wrapper)
        {
            return wrapper.Category == 0 && (wrapper.Index == 0 || wrapper.Index == 255);
        }

        IReadOnlyList<MonsterAiAbilityCorpus_Service.MonsterAiAbilityReference> GetAiAbilityReferences()
        {
            return MonsterAiAbilityCorpus_Service.GetAbilityReferences(MonsterId);
        }

        static void AddSourceIfMissing(ICollection<string> sources, string source)
        {
            if (!sources.Contains(source))
            {
                sources.Add(source);
            }
        }

        static string GetAbilityDisplayName(GameIndex_Wrapper wrapper)
        {
            try
            {
                string name = FfxCommon_Util.GetGameIndexName(wrapper.Category, wrapper.Index);
                return string.IsNullOrWhiteSpace(name) ? "<EMPTY>" : name;
            }
            catch
            {
                return "<INVALID_CATEGORY>";
            }
        }

        static string GetCategoryLabel(byte category)
        {
            return category switch
            {
                0 => "[0] -",
                1 => "[1] Models",
                2 => "[2] Items",
                3 => "[3] Commands",
                4 => "[4] Monster Commands 1",
                5 => "[5] Cat5",
                6 => "[6] Monster Commands 2",
                7 => "[7] Cat7",
                8 => "[8] Auto Abilities",
                9 => "[9] Cat9",
                10 => "[10] Key Items",
                _ => $"[{category}] Unknown"
            };
        }

        string ResolveLocalizedText(byte[] scriptBytes, string? englishText)
        {
            if (SelectedTextLocale == "English" && !string.IsNullOrWhiteSpace(englishText))
            {
                return englishText;
            }

            if (CanEditLocalizedText)
            {
                return string.Empty;
            }

            return FfxEncoding.DecodeScript(scriptBytes).GetString(FfxEncoding.JpDecoder);
        }

        static string? NormalizeLocalizationField(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.Trim();
        }

        void SetLocalizedText(ref string? englishField, string? value, string propertyName)
        {
            if (!CanEditLocalizedText)
            {
                return;
            }

            string? normalized = NormalizeLocalizationField(value);
            if (string.Equals(englishField, normalized, System.StringComparison.Ordinal))
            {
                return;
            }

            englishField = normalized;
            OnPropertyChanged(propertyName);
            OnPropertyChanged(nameof(TextLocaleSummary));
        }
    }
}
