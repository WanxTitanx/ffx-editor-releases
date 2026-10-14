using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Save;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace FFXProjectEditor.Modules.CelestialWeaponEditor
{
    internal sealed record CelestialWeaponDefinition(
        int CharacterId,
        string Character,
        string Weapon,
        string Crest,
        string Sigil,
        string Theme);

    internal partial class CelestialWeaponEditor_DataModel : ObservableObject
    {
        static readonly CelestialWeaponDefinition[] Definitions =
        [
            new(0, "Tidus", "Caladbolg", "Sun Crest", "Sun Sigil", "Speed"),
            new(1, "Yuna", "Nirvana", "Moon Crest", "Moon Sigil", "Summoning"),
            new(2, "Auron", "Masamune", "Mars Crest", "Mars Sigil", "Strength"),
            new(3, "Kimahri", "Spirit Lance", "Saturn Crest", "Saturn Sigil", "Versatility"),
            new(4, "Wakka", "World Champion", "Jupiter Crest", "Jupiter Sigil", "Accuracy"),
            new(5, "Lulu", "Onion Knight", "Venus Crest", "Venus Sigil", "Magic"),
            new(6, "Rikku", "God Hand", "Mercury Crest", "Mercury Sigil", "Agility"),
        ];

        public ObservableCollection<CelestialWeaponRow> CelestialWeapons { get; } = [];

        [ObservableProperty] private CelestialWeaponRow? selectedWeapon;
        [ObservableProperty] private string loadSummary = "Loading canonical save identities...";

        public CelestialWeaponEditor_DataModel()
        {
            Refresh();
        }

        public void Refresh()
        {
            CelestialWeapons.Clear();

            foreach (CelestialWeaponDefinition definition in Definitions)
            {
                FfxSaveCatalogEntry? catalogEntry = FfxSaveRegistry
                    .GetWeaponCatalogForCharacter(definition.CharacterId)
                    .FirstOrDefault(entry => string.Equals(entry.Label, definition.Weapon, StringComparison.Ordinal));

                CelestialWeapons.Add(new CelestialWeaponRow(definition, catalogEntry));
            }

            SelectedWeapon = CelestialWeapons.FirstOrDefault();
            int resolved = CelestialWeapons.Count(row => row.HasSaveIdentity);
            LoadSummary = $"Canonical save identities resolved: {resolved}/{Definitions.Length}.";
        }
    }

    internal sealed class CelestialWeaponRow
    {
        readonly CelestialWeaponDefinition definition;
        readonly FfxSaveCatalogEntry? catalogEntry;

        public CelestialWeaponRow(CelestialWeaponDefinition definition, FfxSaveCatalogEntry? catalogEntry)
        {
            this.definition = definition;
            this.catalogEntry = catalogEntry;
        }

        public string Character => definition.Character;
        public string Weapon => definition.Weapon;
        public string Crest => definition.Crest;
        public string Sigil => definition.Sigil;
        public string Theme => definition.Theme;
        public string CharacterBadge => definition.Character[..1].ToUpperInvariant();
        public bool HasSaveIdentity => catalogEntry?.Bytes.Length == 2;
        public string SaveIdentity => HasSaveIdentity
            ? $"0x{catalogEntry!.Bytes[0]:X2}{catalogEntry.Bytes[1]:X2}"
            : "Unresolved";
        public string SaveIdentityDetail => HasSaveIdentity
            ? $"Save catalog bytes: {catalogEntry!.Bytes[0]:X2} {catalogEntry.Bytes[1]:X2}"
            : "No matching save-catalog entry was found.";
        public string AcquisitionSummary => $"{Crest} · {Sigil}";
    }
}
