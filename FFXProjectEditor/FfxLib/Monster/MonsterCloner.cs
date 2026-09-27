// ============================================================================
// MonsterCloner — byte-safe clone + CustomBossConfig overlay for a monster file
// PURPOSE : deep-clones a Monster_File (round-trip Write->Read for an independent copy), applies nullable stat,
//           name and loot overrides, returns a new Monster_File ready to write.
// WHY     : used by Custom Boss Creator to instantiate a new monster slot from an existing one without touching
//           the source; byte-safety comes from the round-trip (never mutates the input file object).
// EVIDENCE: RT0 round-trip (MonsterFileAdapter); used by Modules/CustomBossCreator.
// MAINT   : overriding DisplayNameEncoded clears OriginalSectionBytes to force a full rebuild (see Monster_Loot);
//           EncodeNameSafe replaces unmapped chars with space (byte 58) and never throws.
//           CustomBossConfig fields are nullable = "inherit from source"; keep Clone() writing them symmetrically.
// ============================================================================
using FFXProjectEditor.Utils.Encoding;
using System;
using System.Collections.Generic;

namespace FFXProjectEditor.FfxLib.Monster
{
    /// <summary>Clones an existing Monster_File and applies a CustomBossConfig overlay.
    /// The clone is byte-safe: the source is round-tripped through Write→Read to get
    /// an independent copy, then only the requested fields are patched.</summary>
    public static class MonsterCloner
    {
        /// <summary>Creates a deep-clone of <paramref name="source"/> and applies the
        /// overrides in <paramref name="config"/>. Returns a new <see cref="Monster_File"/>
        /// ready to be written via <see cref="Monster_File.Write"/>.</summary>
        public static Monster_File Clone(Monster_File source, CustomBossConfig config)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(config);

            // Round-trip to get a clean, independent copy.
            byte[] sourceBytes = source.Write();
            Monster_File clone = Monster_File.Read(sourceBytes);

            Monster_StatSheet sheet = clone.StatSheetFile;

            // --- Stat overrides ---
            if (config.Hp.HasValue) sheet.Hp = config.Hp.Value;
            if (config.Mp.HasValue) sheet.Mp = config.Mp.Value;
            if (config.HpOverkill.HasValue) sheet.HpOverkill = config.HpOverkill.Value;
            if (config.Strength.HasValue) sheet.Strength = config.Strength.Value;
            if (config.Defense.HasValue) sheet.Defense = config.Defense.Value;
            if (config.Magic.HasValue) sheet.Magic = config.Magic.Value;
            if (config.MagicDefense.HasValue) sheet.MagicDefense = config.MagicDefense.Value;
            if (config.Agility.HasValue) sheet.Agility = config.Agility.Value;
            if (config.Luck.HasValue) sheet.Luck = config.Luck.Value;
            if (config.Evasion.HasValue) sheet.Evasion = config.Evasion.Value;
            if (config.Accuracy.HasValue) sheet.Accuracy = config.Accuracy.Value;
            if (config.ModelId.HasValue) sheet.ModelId = config.ModelId.Value;
            if (config.MonsterId.HasValue) sheet.MonsterId = config.MonsterId.Value;

            // --- Name override ---
            // If a name was provided, encode it using the US encoder and rebuild
            // the section from scratch (null OriginalSectionBytes triggers the full
            // rebuild path in WriteSingle, which picks up the new NameScriptBytes).
            if (!string.IsNullOrWhiteSpace(config.DisplayNameEncoded))
            {
                sheet.NameScriptBytes = EncodeNameSafe(config.DisplayNameEncoded);
                sheet.OriginalSectionBytes = null;
            }

            ApplyLootOverrides(clone.LootFile, config);

            clone.StatSheetFile = sheet;
            return clone;
        }

        /// <summary>Creates an independent copy of a loot section while preserving its section length.</summary>
        public static Monster_Loot CloneLoot(Monster_Loot loot)
        {
            ArgumentNullException.ThrowIfNull(loot);
            return Monster_Loot.ReadSingle(loot.WriteSingle());
        }

        /// <summary>Applies nullable Gil/AP overrides to an existing loot section.</summary>
        public static void ApplyRewardOverrides(Monster_Loot? loot, CustomBossConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            if (loot == null) return;

            if (config.RewardGil.HasValue) loot.Gil = config.RewardGil.Value;
            if (config.RewardAp.HasValue) loot.Ap = config.RewardAp.Value;
            if (config.RewardApOverkill.HasValue) loot.ApOverkill = config.RewardApOverkill.Value;
        }

        /// <summary>Applies all nullable loot overrides supported by the Custom Boss Creator.</summary>
        public static void ApplyLootOverrides(Monster_Loot? loot, CustomBossConfig config)
        {
            ApplyRewardOverrides(loot, config);
            if (loot == null) return;

            if (config.Drop1Chance.HasValue) loot.Drop1Chance = config.Drop1Chance.Value;
            if (config.Drop1Id.HasValue) loot.Drop1Id = config.Drop1Id.Value;
            if (config.Drop1RareId.HasValue) loot.Drop1RareId = config.Drop1RareId.Value;
            if (config.Drop1Count.HasValue) loot.Drop1Count = config.Drop1Count.Value;
            if (config.Drop1RareCount.HasValue) loot.Drop1RareCount = config.Drop1RareCount.Value;

            if (config.StealChance.HasValue) loot.StealChance = config.StealChance.Value;
            if (config.StealId.HasValue) loot.StealId = config.StealId.Value;
            if (config.StealRareId.HasValue) loot.StealRareId = config.StealRareId.Value;
            if (config.StealCount.HasValue) loot.StealCount = config.StealCount.Value;
            if (config.StealRareCount.HasValue) loot.StealRareCount = config.StealRareCount.Value;

            if (config.BribeId.HasValue) loot.BribeId = config.BribeId.Value;
            if (config.BribeCount.HasValue) loot.BribeCount = config.BribeCount.Value;
        }

        // Encode a display name as FFX US bytes. Any character that is not in the
        // UsEncoder is replaced with a space (byte 58 = ' '). Never throws.
        private static byte[] EncodeNameSafe(string name)
        {
            Dictionary<char, byte> enc = FfxEncoding.UsEncoder;
            List<byte> bytes = new();
            foreach (char c in name)
            {
                if (enc.TryGetValue(c, out byte b))
                    bytes.Add(b);
                else if (enc.TryGetValue(' ', out byte space))
                    bytes.Add(space);
            }
            return bytes.ToArray();
        }
    }

    /// <summary>Configuration overlay applied during <see cref="MonsterCloner.Clone"/>.
    /// Null/HasValue=false fields are not modified (inherit from source).</summary>
    public sealed class CustomBossConfig
    {
        public uint? Hp { get; init; }
        public uint? Mp { get; init; }
        public uint? HpOverkill { get; init; }
        public byte? Strength { get; init; }
        public byte? Defense { get; init; }
        public byte? Magic { get; init; }
        public byte? MagicDefense { get; init; }
        public byte? Agility { get; init; }
        public byte? Luck { get; init; }
        public byte? Evasion { get; init; }
        public byte? Accuracy { get; init; }
        public short? ModelId { get; init; }
        public short? MonsterId { get; init; }
        public ushort? RewardGil { get; init; }
        public ushort? RewardAp { get; init; }
        public ushort? RewardApOverkill { get; init; }
        public byte? Drop1Chance { get; init; }
        public ushort? Drop1Id { get; init; }
        public ushort? Drop1RareId { get; init; }
        public byte? Drop1Count { get; init; }
        public byte? Drop1RareCount { get; init; }
        public byte? StealChance { get; init; }
        public ushort? StealId { get; init; }
        public ushort? StealRareId { get; init; }
        public byte? StealCount { get; init; }
        public byte? StealRareCount { get; init; }
        public ushort? BribeId { get; init; }
        public byte? BribeCount { get; init; }

        /// <summary>US-encodable name string. Null = keep source name. Characters not
        /// in the US table are silently replaced with a space.</summary>
        public string? DisplayNameEncoded { get; init; }
    }
}
