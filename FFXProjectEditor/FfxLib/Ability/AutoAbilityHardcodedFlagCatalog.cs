using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ability
{
    internal static class AutoAbilityHardcodedFlagCatalog
    {
        static readonly IReadOnlyList<AutoAbilityHardcodedFlag> Flags =
        [
            new(0x62, 0x0001, "Sensor", true),
            new(0x62, 0x0002, "First Strike", true),
            new(0x62, 0x0004, "Initiative", true),
            new(0x62, 0x0008, "Counterattack", true),
            new(0x62, 0x0010, "Evade & Counter", true),
            new(0x62, 0x0020, "Magic Counter", true),
            new(0x62, 0x0040, "Magic Booster", true),
            new(0x62, 0x0080, "Unused 62-128", false),
            new(0x62, 0x0100, "Unused 63-1", false),
            new(0x62, 0x0200, "Alchemy", true),
            new(0x62, 0x0400, "Auto-Potion", true),
            new(0x62, 0x0800, "Auto-Med", true),
            new(0x62, 0x1000, "Auto-Phoenix", true),
            new(0x62, 0x2000, "Piercing", true),
            new(0x62, 0x4000, "Half MP Cost", true),
            new(0x62, 0x8000, "One MP Cost", true),

            new(0x64, 0x0001, "Double Overdrive", true),
            new(0x64, 0x0002, "Triple Overdrive", true),
            new(0x64, 0x0004, "SOS Overdrive", true),
            new(0x64, 0x0008, "Overdrive to AP", true),
            new(0x64, 0x0010, "Double AP", true),
            new(0x64, 0x0020, "Triple AP", true),
            new(0x64, 0x0040, "No AP", true),
            new(0x64, 0x0080, "Pickpocket", true),
            new(0x64, 0x0100, "Master Thief", true),
            new(0x64, 0x0200, "Break HP Limit", true),
            new(0x64, 0x0400, "Break MP Limit", true),
            new(0x64, 0x0800, "Break Damage Limit", true),
            new(0x64, 0x1000, "Double Drop", true),
            new(0x64, 0x2000, "Triple Drop", true),
            new(0x64, 0x4000, "Gillionaire", true),
            new(0x64, 0x8000, "HP Stroll", true),

            new(0x66, 0x0001, "MP Stroll", true),
            new(0x66, 0x0002, "No Encounters", true),
            new(0x66, 0x0004, "Capture", true),
            new(0x66, 0x0008, "Unused 66-8", false),
            new(0x66, 0x0010, "Unused 66-16", false),
            new(0x66, 0x0020, "Unused 66-32", false),
            new(0x66, 0x0040, "Unused 66-64", false),
            new(0x66, 0x0080, "Unused 66-128", false),
        ];

        public static IReadOnlyList<AutoAbilityHardcodedFlagDefinition> EditableFlags { get; } = Flags
            .Select(flag => new AutoAbilityHardcodedFlagDefinition(
                flag.Label,
                flag.Offset,
                flag.Mask,
                WordLabel(flag.Offset),
                $"0x{flag.Mask:X4}",
                $"{WordLabel(flag.Offset)} / 0x{flag.Mask:X4}"))
            .ToList();

        public static string Summarize(int byte62, int byte63, int byte64, int byte65, int byte66, int byte67)
        {
            ushort word62 = MakeWord(byte62, byte63);
            ushort word64 = MakeWord(byte64, byte65);
            ushort word66 = MakeWord(byte66, byte67);

            List<string> named = DecodeWord(0x62, word62, knownOnly: true)
                .Concat(DecodeWord(0x64, word64, knownOnly: true))
                .Concat(DecodeWord(0x66, word66, knownOnly: true))
                .ToList();

            List<string> technical = DecodeWord(0x62, word62, knownOnly: false)
                .Concat(DecodeWord(0x64, word64, knownOnly: false))
                .Concat(DecodeWord(0x66, word66, knownOnly: false))
                .ToList();

            string namedText = named.Count == 0
                ? "Active named auto-abilities: none."
                : $"Active named auto-abilities: {string.Join(", ", named)}.";
            string technicalText = technical.Count == 0
                ? "Active technical bits: none."
                : $"Active technical bits: {string.Join(", ", technical)}.";

            return $"{namedText} {technicalText}";
        }

        public static IReadOnlyList<AutoAbilityHardcodedFlagState> Describe(int byte62, int byte63, int byte64, int byte65, int byte66, int byte67)
        {
            ushort word62 = MakeWord(byte62, byte63);
            ushort word64 = MakeWord(byte64, byte65);
            ushort word66 = MakeWord(byte66, byte67);

            List<AutoAbilityHardcodedFlagState> rows = new();
            foreach (AutoAbilityHardcodedFlag flag in Flags)
            {
                ushort word = flag.Offset switch
                {
                    0x62 => word62,
                    0x64 => word64,
                    0x66 => word66,
                    _ => 0
                };

                bool isSet = (word & flag.Mask) != 0;
                rows.Add(new AutoAbilityHardcodedFlagState(
                    flag.Label,
                    WordLabel(flag.Offset),
                    $"0x{flag.Mask:X4}",
                    $"{WordLabel(flag.Offset)} / 0x{flag.Mask:X4}",
                    isSet,
                    flag.IsKnown));
            }

            return rows;
        }

        static IEnumerable<string> DecodeWord(int offset, ushort word, bool knownOnly)
        {
            foreach (AutoAbilityHardcodedFlag flag in Flags.Where(flag => flag.Offset == offset && flag.IsKnown == knownOnly))
            {
                if ((word & flag.Mask) != 0)
                    yield return flag.Label;
            }
        }

        static ushort MakeWord(int lo, int hi)
        {
            return unchecked((ushort)(((hi & 0xFF) << 8) | (lo & 0xFF)));
        }

        static string WordLabel(int offset)
        {
            return offset switch
            {
                0x62 => "62h/63h",
                0x64 => "64h/65h",
                0x66 => "66h/67h",
                _ => $"{offset:X2}h"
            };
        }

        readonly record struct AutoAbilityHardcodedFlag(int Offset, ushort Mask, string Label, bool IsKnown);
    }

    public sealed record AutoAbilityHardcodedFlagState(
        string Label,
        string WordLabel,
        string MaskLabel,
        string TechnicalLabel,
        bool IsSet,
        bool IsKnown)
    {
        public string StateLabel => IsSet ? "Active" : "Inactive";
        public string NameLabel => Label;
    }

    public sealed record AutoAbilityHardcodedFlagDefinition(
        string Label,
        int Offset,
        ushort Mask,
        string WordLabel,
        string MaskLabel,
        string TechnicalLabel);
}
