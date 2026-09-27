using FFXProjectEditor.FfxLib.Dictionaries;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.TreasureMap;

// ── TreasureRewardLookup ───────────────────────────────────────────────────────────────
// Resolves a takara.bin treasure record into a human-readable reward for the editor list:
//   Gil      -> "N×100 Gil"
//   Item     -> quantity + real item name from Item_Dictionary (IDs 0x2000+index)
//   KeyItem  -> a small curated key-item list
//   Equipment-> buki_get.bin index-based naming (equipment names are macro strings, honest
//               about the limitation — see docs).
// Describe() is what TreasureChestRow.RewardText uses in the UI.
// ──────────────────────────────────────────────────────────────────────────────────────
public sealed record TreasureRewardOption(ushort EncodedId, string Display)
{ public override string ToString() => Display; }

public static class TreasureRewardLookup
{
    public static IReadOnlyList<TreasureRewardOption> Build(TreasureKind kind, string masterPath)
    {
        return kind switch
        {
            TreasureKind.Gil => [new TreasureRewardOption(0, "Gil")],
            TreasureKind.Item => BuildItemOptions(),
            TreasureKind.KeyItem => BuildKeyItemOptions(),
            TreasureKind.Equipment => BuildEquipmentOptions(masterPath),
            _ => []
        };
    }

    public static string Describe(TreasureKind kind, byte quantity, ushort encodedId, string masterPath)
    {
        if (kind == TreasureKind.Gil) return $"{quantity * 100:N0} Gil";
        TreasureRewardOption? opt = Build(kind, masterPath).FirstOrDefault(v => v.EncodedId == encodedId);
        return opt is null ? $"Unknown 0x{encodedId:X4}" :
            kind == TreasureKind.Equipment ? opt.Display : $"{quantity} × {opt.Display}";
    }

    private static IReadOnlyList<TreasureRewardOption> BuildItemOptions()
    {
        var opts = new List<TreasureRewardOption>();
        foreach (var kv in Item_Dictionary.Instance)
            opts.Add(new TreasureRewardOption((ushort)(0x2000 + kv.Key), kv.Value));
        return opts;
    }

    private static IReadOnlyList<TreasureRewardOption> BuildKeyItemOptions()
    {
        string[] keys = { "Withered Bouquet", "Flint", "Cloudy Mirror", "Celestial Mirror",
            "Al Bhed Primer I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X",
            "XI", "XII", "XIII", "XIV", "XV", "XVI", "XVII", "XVIII", "XIX", "XX",
            "XXI", "XXII", "XXIII", "XXIV", "XXV", "XXVI",
            "Summoner's Soul", "Aeon's Soul", "Jecht's Sphere", "Rusty Sword",
            "Sun Crest/Sigil", "Moon Crest/Sigil", "Mars Crest/Sigil", "Mark of Conquest",
            "Saturn Crest/Sigil", "Jupiter Crest/Sigil", "Venus Crest/Sigil", "Mercury Crest/Sigil",
            "Blossom Crown", "Flower Scepter" };
        return keys.Select((name, i) => new TreasureRewardOption((ushort)(0xA000 + i), name)).ToArray();
    }

    private static IReadOnlyList<TreasureRewardOption> BuildEquipmentOptions(string masterPath)
    {
        // TODO: wire to buki_get.bin reader + AutoAbility_Dictionary
        string path = Path.Combine(masterPath, "jppc", "battle", "kernel", "buki_get.bin");
        if (!File.Exists(path)) return [];
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length < 0x14) return [];
            int count = BitConverter.ToUInt16(bytes, 0x0A) + 1;
            var opts = new List<TreasureRewardOption>(count);
            for (int id = 0; id < count; id++)
                opts.Add(new TreasureRewardOption((ushort)id, $"Equipment #{id}"));
            return opts;
        }
        catch { return []; }
    }
}
