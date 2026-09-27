using System;
using System.Collections.Generic;
using System.Linq;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.FfxLib.Ai.Sin;

public enum SinUniTarget { Self, Frontline, Allies, RandomFrontline, RandomAlly, Previous }

public sealed record SinUniSkill(ushort Operand, string Name, SinUniTarget Target);

/// <summary>Exact command IDs from the Spira Reforge monmagic2 pack. No BOSS recipes.</summary>
public sealed record SinPostActionPreset(string Id, IReadOnlyList<SinUniSkill> Skills,
    string? RequiredMonster = null, ushort? ForbiddenField = null, bool DamageOnly = false, bool ReplayNative = false);

public static class SinPostActionPresetCatalog
{
    static SinUniSkill Skill(int operand, string name, SinUniTarget target) => new((ushort)operand, name, target);
    const SinUniTarget Single = SinUniTarget.RandomFrontline, Party = SinUniTarget.Frontline;
    const SinUniTarget Allies = SinUniTarget.Allies, Same = SinUniTarget.Previous;

    public static IReadOnlyList<SinPostActionPreset> All { get; } = Array.AsReadOnly(new[]
    {
        new SinPostActionPreset("UNI-009", new[] { Skill(0x612A, "Sand Mantle", SinUniTarget.Self) }),
        new SinPostActionPreset("UNI-010", Array.Empty<SinUniSkill>(), ReplayNative: true),
        new SinPostActionPreset("UNI-011", new[] { Skill(0x612B, "Sin Siphon", Single) }),
        new SinPostActionPreset("UNI-012", new[] { Skill(0x612C, "Sandstorm", Party) }),
        new SinPostActionPreset("UNI-013A", new[] { Skill(0x612D, "Dust Ward", Allies) }),
        new SinPostActionPreset("UNI-013B", new[] { Skill(0x612E, "Hush Wave", Party) }),
        new SinPostActionPreset("UNI-014", new[] { Skill(0x612F, "Silica Shards", Party) }),
        new SinPostActionPreset("UNI-015", new[] { Skill(0x6130, "Arcane Pulse", Single), Skill(0x6131, "Arcane Wave", Party) }),
        new SinPostActionPreset("UNI-016", new[] { Skill(0x6132, "Flanking Bite", Single), Skill(0x6133, "Guardbreaker", Same) }),
        new SinPostActionPreset("UNI-017", new[] { Skill(0x6134, "Veil", SinUniTarget.RandomAlly), Skill(0x6135, "Dragging Strike", Single) }),
        new SinPostActionPreset("UNI-018", new[] { Skill(0x6136, "Shadow Thrust", Single), Skill(0x6137, "Blindside", Single) }),
        new SinPostActionPreset("UNI-019", new[] { Skill(0x6138, "Arcane Voice", Single), Skill(0x6139, "Cutting Silence", Single) }),
        new SinPostActionPreset("UNI-020", new[] { Skill(0x613A, "Slow", Single), Skill(0x613B, "Rhythm Impact", Same) }),
        new SinPostActionPreset("UNI-021", new[] { Skill(0x613C, "Quaking Tail", Party), Skill(0x613D, "Petrifying Gaze", Single) }, "m186"),
        new SinPostActionPreset("UNI-022", new[] { Skill(0x613E, "Demolishing Fist", Single), Skill(0x613F, "Colossus Quake", Party) }, "m066"),
        new SinPostActionPreset("UNI-023", new[] { Skill(0x6140, "Toxic Mist", Party), Skill(0x6141, "Foul Breath", Single) }, "m064"),
        new SinPostActionPreset("UNI-024", new[] { Skill(0x6142, "Sepulchral Blessing", Allies) }),
        new SinPostActionPreset("UNI-025", new[] { Skill(0x6143, "Wraith Restoration", Allies) }),
        new SinPostActionPreset("UNI-026", new[] { Skill(0x6144, "Wraith Ascension", Allies) }),
        new SinPostActionPreset("UNI-027", new[] { Skill(0x6145, "Netherburst", Party) }),
        new SinPostActionPreset("UNI-028", new[] { Skill(0x6146, "Despairing Breath", Party) }),
        new SinPostActionPreset("UNI-029", new[] { Skill(0x6147, "Gravitic Shackles", Party) }),
        new SinPostActionPreset("UNI-030", new[] { Skill(0x6148, "Soul Drain", Single) }),
        new SinPostActionPreset("UNI-031", new[] { Skill(0x6149, "Peak Bastion", Allies) }),
        new SinPostActionPreset("UNI-032", new[] { Skill(0x614A, "Mountain Rend", Single) }),
        new SinPostActionPreset("UNI-033", new[] { Skill(0x614B, "Gagazet Miasma", Party) }),
        new SinPostActionPreset("UNI-034", new[] { Skill(0x614C, "Sepulchral Avalanche", Party) }),
        new SinPostActionPreset("UNI-035", new[] { Skill(0x614D, "Undertow", Party) }),
        new SinPostActionPreset("UNI-036", new[] { Skill(0x614E, "Condemnation Bell", Single) }),
        new SinPostActionPreset("UNI-037", new[] { Skill(0x614F, "Frostcleave", Single), Skill(0x6150, "Armorbreaker", Same) }),
        new SinPostActionPreset("UNI-038", new[] { Skill(0x6151, "Ronso Warcry", Allies), Skill(0x6152, "Glacial Pulse", Party) }),
        new SinPostActionPreset("UNI-039", new[] { Skill(0x6153, "Profane Vow", Single), Skill(0x6154, "Willbreaker", Single) }, ForbiddenField: 0x002A),
        new SinPostActionPreset("UNI-040", Array.Empty<SinUniSkill>(), ForbiddenField: 0x0007, DamageOnly: true),
    });

    public static SinPostActionPreset? Find(string? id) => All.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static string Describe(SinPostActionPreset recipe)
    {
        string chain = string.Join(" → ", recipe.Skills.Select(s => $"{s.Name} (0x{s.Operand:X4})"));
        if (recipe.ReplayNative) return Strings.U_Ai_UniMirageRecipe;
        if (recipe.DamageOnly) return Strings.U_Ai_UniFallenTouchRecipe;
        string result = string.Format(Strings.U_Ai_UniNativeThenSkills, chain);
        if (recipe.ForbiddenField.HasValue) result += "\n" + Strings.U_Ai_UniBerserkRecipe;
        return result;
    }

    // Keep editor-only recipes out of the regional spread pool. Existing content prose remains visible;
    // execution metadata is taken from the same definition the writer consumes, never parsed from prose.
    internal static AiSinPresetEntry WithRecipe(AiSinPresetEntry entry)
    {
        SinPostActionPreset? recipe = Find(entry.Id);
        if (recipe == null) return entry;
        return new AiSinPresetEntry
        {
            Id = entry.Id, Name = entry.Name, Scope = entry.Scope, Tier = entry.Tier,
            Threat = entry.Threat, CatalogOrder = entry.CatalogOrder, LegacyProtoId = entry.LegacyProtoId,
            Maturity = "bake-ready", Primitives = entry.Primitives, Summary = entry.Summary,
            Preview = Describe(recipe) + (recipe.ReplayNative || recipe.DamageOnly ? "" : "\n\n" + entry.Preview),
            Risk = Strings.U_Ai_UniOfflineRecipeRisk,
            NextGate = recipe.RequiredMonster is string monster
                ? string.Format(Strings.U_Ai_UniRequiredMonster, monster)
                : recipe.DamageOnly ? Strings.U_Ai_UniFallenOnly : Strings.U_Ai_UniVerifyBattle,
        };
    }
}
