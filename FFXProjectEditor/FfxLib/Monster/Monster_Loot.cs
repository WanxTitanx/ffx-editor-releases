// ============================================================================
// Monster_Loot — loot/drop/steal/bribe/gear section of the monster file
// PURPOSE : typed model (gil/ap/overkill, 2 drop sets + rare, overkill drops, steals+bribes, gear + per-char
//           abilities, zanmato) with ReadSingle/WriteSingle via Xe.BinaryMapper.
// WHY     : the loot section is fixed-size; WriteSingle is preserve-only (RT0): it keeps the section's exact
//           length (incl. trailing padding) and re-stamps only the fixed bytes in place — no-edit is byte-identical.
// EVIDENCE: RT0 round-trip on vanilla m000/m00X (MonsterFileAdapter gate, baseline 185/185).
// MAINT   : OriginalSectionBytes drives preserve-write — clearing it triggers a full rebuild (used for name/section
//           replacement). Field order mirrors the on-disk struct; add a field on BOTH ReadSingle and WriteSingle.
// ============================================================================
using System;
using System.IO;
using Xe.BinaryMapper;

namespace FFXProjectEditor.FfxLib.Monster
{
    public class Monster_Loot
    {
        // FFX consumes these three on-disk words as unsigned 16-bit values.
        // Keep the model unsigned so vanilla rewards above 32,767 remain positive in the editor.
        [Data] public ushort Gil { get; set; }
        [Data] public ushort Ap { get; set; }
        [Data] public ushort ApOverkill { get; set; }
        [Data] public ushort RonsoRageId { get; set; }

        // Drops
        [Data] public byte Drop1Chance { get; set; }
        [Data] public byte Drop2Chance { get; set; }
        [Data] public byte StealChance { get; set; }
        [Data] public byte GearChance { get; set; }

        [Data] public ushort Drop1Id { get; set; }
        [Data] public ushort Drop1RareId { get; set; }
        [Data] public ushort Drop2Id { get; set; }
        [Data] public ushort Drop2RareId { get; set; }
        [Data] public byte Drop1Count { get; set; }
        [Data] public byte Drop1RareCount { get; set; }
        [Data] public byte Drop2Count { get; set; }
        [Data] public byte Drop2RareCount { get; set; }

        // Overkills
        [Data] public ushort DropOverkillId { get; set; }
        [Data] public ushort DropOverkillRareId { get; set; }
        [Data] public ushort DropOverkill2Id { get; set; }
        [Data] public ushort DropOverkill2RareId { get; set; }
        [Data] public byte DropOverkillCount { get; set; }
        [Data] public byte DropOverkillRareCount { get; set; }
        [Data] public byte DropOverkill2Count { get; set; }
        [Data] public byte DropOverkill2RareCount { get; set; }

        // Steals
        [Data] public ushort StealId { get; set; }
        [Data] public ushort StealRareId { get; set; }
        [Data] public byte StealCount { get; set; }
        [Data] public byte StealRareCount { get; set; }
        [Data] public ushort BribeId { get; set; }
        [Data] public byte BribeCount { get; set; }

        // Gear
        [Data] public byte GearSlotCount { get; set; }
        [Data] public byte GearFormula { get; set; }
        [Data] public byte GearCrit { get; set; }
        [Data] public byte GearAttack { get; set; }
        [Data] public byte GearAbilityCount { get; set; }
        [Data] public LootGearAbilities TidusAbilities { get; set; }
        [Data] public LootGearAbilities YunaAbilities { get; set; }
        [Data] public LootGearAbilities AuronAbilities { get; set; }
        [Data] public LootGearAbilities KimahriAbilities { get; set; }
        [Data] public LootGearAbilities WakkaAbilities { get; set; }
        [Data] public LootGearAbilities LuluAbilities { get; set; }
        [Data] public LootGearAbilities RikkuAbilities { get; set; }

        // Extra
        [Data] public byte ZanmatoLevel { get; set; }
        [Data] public byte Unk1 { get; set; }
        [Data] public byte Unk2 { get; set; }
        [Data] public byte Unk3 { get; set; }
        [Data(Count=3)] public byte[] Padding { get; set; }

        /// <summary>Raw bytes of the loot section as read. Kept so WriteSingle preserves the section's
        /// exact length (incl. trailing padding) and only re-stamps the fixed fields in place (RT0).</summary>
        public byte[] OriginalSectionBytes { get; set; }

        public Monster_Loot()
        {
            TidusAbilities = new();
            YunaAbilities = new();
            AuronAbilities = new();
            KimahriAbilities = new();
            WakkaAbilities = new();
            LuluAbilities = new();
            RikkuAbilities = new();
            Padding = new byte[3];
        }

        public class LootGearAbilities
        {
            [Data(Count=8)] public ushort[] WeaponAbilities { get; set; }
            [Data(Count=8)] public ushort[] ArmorAbilities { get; set; }

            public LootGearAbilities()
            {
                WeaponAbilities = new ushort[8];
                ArmorAbilities = new ushort[8];
            }
        }

        public static Monster_Loot ReadSingle(byte[] byteFile)
        {
            using (MemoryStream stream = new MemoryStream(byteFile))
            {
                Monster_Loot loot = BinaryMapping.ReadObject<Monster_Loot>(stream);
                loot.OriginalSectionBytes = byteFile;
                return loot;
            }
        }

        public byte[] WriteSingle()
        {
            // Preserve-only (RT0): keep the section's exact length (incl. trailing padding) and only
            // re-stamp the fixed fields in place. No-edit save -> byte-identical; edit -> byte-local.
            if (OriginalSectionBytes != null)
            {
                byte[] preserved = (byte[])OriginalSectionBytes.Clone();
                byte[] block;
                using (MemoryStream ms = new MemoryStream())
                {
                    BinaryMapping.WriteObject<Monster_Loot>(ms, this);
                    block = ms.ToArray();
                }
                Array.Copy(block, 0, preserved, 0, Math.Min(block.Length, preserved.Length));
                return preserved;
            }
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryMapping.WriteObject<Monster_Loot>(stream, this);
                return stream.ToArray();
            }
        }
    }
}
