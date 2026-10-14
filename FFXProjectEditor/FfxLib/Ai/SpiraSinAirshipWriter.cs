using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Monster;
using static FFXProjectEditor.FfxLib.Ai.SpiraNativeBossWriter;

namespace FFXProjectEditor.FfxLib.Ai;

public sealed record SinAirshipSpell(ushort Original, string Name, int Tier, ushort Target);
public sealed record SinAirshipCommands(IReadOnlyList<(ushort Command, ushort Target)> Arms,
    IReadOnlyList<(ushort Command, ushort Target)> GenaisT5,
    IReadOnlyList<(ushort Command, ushort Target)> GenaisT6);

public static class SpiraSinAirshipWriter
{
    const ushort Self = 0xFFF3, Party = 0xFFF2, Random = 0xFFEF;
    // Tiers from the maintained SIN catalog; omit physical, bite, gaze, breath,
    // Death and high-power T5 finishers. Buffs target only the casting part.
    public static IReadOnlyList<SinAirshipSpell> Pool { get; } = Array.AsReadOnly(new[]
    {
        new SinAirshipSpell(0x610B, "Ward Stack", 1, Self),
        new SinAirshipSpell(0x610E, "Sin Salve", 1, Self),
        new SinAirshipSpell(0x610C, "Frost-Flood Weave", 2, Random),
        new SinAirshipSpell(0x610D, "Shoreline Break", 2, Party),
        new SinAirshipSpell(0x610F, "Mist Chorus", 2, Self),
        new SinAirshipSpell(0x612A, "Sand Mantle", 3, Self),
        new SinAirshipSpell(0x612B, "Sin Siphon", 3, Random),
        new SinAirshipSpell(0x612C, "Sandstorm", 3, Party),
        new SinAirshipSpell(0x612D, "Dust Ward", 3, Self),
        new SinAirshipSpell(0x612E, "Hush Wave", 3, Party),
        new SinAirshipSpell(0x6130, "Arcane Pulse", 3, Random),
        new SinAirshipSpell(0x6131, "Arcane Wave", 3, Party),
        new SinAirshipSpell(0x6134, "Veil", 3, Self),
        new SinAirshipSpell(0x6138, "Arcane Voice", 4, Random),
        new SinAirshipSpell(0x6139, "Cutting Silence", 4, Random),
        new SinAirshipSpell(0x613A, "Slow", 4, Random),
        new SinAirshipSpell(0x6142, "Sepulchral Blessing", 4, Self),
        new SinAirshipSpell(0x6143, "Wraith Restoration", 4, Self),
        new SinAirshipSpell(0x6144, "Wraith Ascension", 4, Self),
        new SinAirshipSpell(0x6145, "Netherburst", 4, Party),
        new SinAirshipSpell(0x6147, "Gravitic Shackles", 4, Party),
        new SinAirshipSpell(0x6148, "Soul Drain", 4, Random),
        new SinAirshipSpell(0x614B, "Gagazet Miasma", 5, Party),
        new SinAirshipSpell(0x614D, "Undertow", 5, Party),
        new SinAirshipSpell(0x6151, "Ronso Warcry", 6, Self),
        new SinAirshipSpell(0x6152, "Glacial Pulse", 6, Party),
        new SinAirshipSpell(0x6153, "Profane Vow", 6, Random),
    });

    public static byte[] Build(byte[] monster, int id, SinAirshipCommands commands)
    {
        if (id is not (136 or 137 or 139) || Monster_File.Read(monster).StatSheetFile.MonsterId != id)
            throw new InvalidDataException("Expected a Sin arm or Genais.");
        if (commands.Arms.Count != 22 || commands.GenaisT5.Count != 2 || commands.GenaisT6.Count != 3)
            throw new ArgumentException("Unexpected filtered spell pool.");
        var baseline = AiScript_File.Read(AiScript_File.SliceAiFileFromMonster(monster)!); var script = baseline;
        if (!AiWorkerMapping.TryResolveCombatOnTurn(monster, script, out var turn, out string error))
            throw new InvalidDataException(error);
        var active = Reachable(script, turn);
        var attackIds = id == 139 ? new ushort[] { 0x6097, 0x6098 } : new ushort[] { 0x6090, 0x6092, 0x6093, 0x60B6 };
        var ops = script.Instructions;
        var sites = Enumerable.Range(1, ops.Count - 1).Where(i => active.Contains(ops[i].Offset - script.ScriptStart)
            && ops[i].Opcode == 0xD8 && ops[i].Operand == 0x700B
            && ops[i - 1].Opcode == 0xAE && attackIds.Contains(ops[i - 1].Operand))
            .Select(i => ops[i].Offset - script.ScriptStart).ToHashSet();
        if (sites.Count != (id == 136 ? 5 : id == 137 ? 4 : 2))
            throw new InvalidDataException("Native Sin attack sites changed.");
        var ownCommands = commands.Arms.Concat(commands.GenaisT5).Concat(commands.GenaisT6).Select(x => x.Command).ToHashSet();
        if (ops.Any(o => o.Opcode == 0xAE && ownCommands.Contains(o.Operand)))
            throw new InvalidOperationException("This native boss has already been authored.");
        ushort random = AddPrivateVariables(ref script, turn.WorkerIndex, 1)[0];
        var emit = new Emission(script, turn.WorkerIndex);
        void Pick(IReadOnlyList<(ushort Command, ushort Target)> pool)
        {
            emit.Add(Op(0xB5, 0x00A9), Op(0xAE, checked((ushort)pool.Count)), Op(0x18), Op(0xA0, random));
            for (ushort i = 0; i < pool.Count; i++)
            {
                ushort index = i;
                emit.If([Op(0x9F, random), Op(0xAE, index), Op(0x06)], () =>
                {
                    var choice = pool[index];
                    if (choice.Target == Random) emit.RandomCommand(choice.Command);
                    else emit.Command(choice.Target, choice.Command);
                });
            }
        }
        int strengthWrites = 0;
        for (int i = 0; i < script.Instructions.Count; i++)
        {
            var op = script.Instructions[i];
            if (id == 139 && op.Opcode == 0xAE && op.Operand is 20 or 27
                && i >= 2 && i + 1 < script.Instructions.Count
                && script.Instructions[i - 2].Operand == Self && script.Instructions[i - 1].Operand == 9
                && script.Instructions[i + 1].Opcode == 0xD8 && script.Instructions[i + 1].Operand == 0x70A8)
            {
                strengthWrites++;
                emit.Add(new AiInstruction { Offset = op.Offset, Opcode = op.Opcode,
                    Operand = SpiraNativeBossStats.ScaleAttribute((byte)op.Operand, 7), HasOperand = true, OperandKind = op.OperandKind });
                continue;
            }
            emit.Add(op);
            if (!sites.Contains(op.Offset - script.ScriptStart)) continue;
            IEnumerable<AiInstruction> guard = [Op(0xAE, Self), Op(0xAE, 4), Op(0xB5, 0x700F)];
            if (id == 139) guard = guard.Concat([Op(0x9F, 1), Op(0xAE, 1), Op(0x06), Op(0x02)]);
            emit.If(guard, () =>
            {
                Pick(id == 139 ? commands.GenaisT5 : commands.Arms);
                Pick(id == 139 ? commands.GenaisT6 : commands.Arms);
            });
        }
        if (id == 139 && strengthWrites != 2) throw new InvalidDataException("Genais phase STR witnesses changed.");
        byte[] ai = emit.Build();
        if (!AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(ai, baseline, out _, out error))
            throw new InvalidDataException(error);
        return AiScript_File.SpliceAiFileIntoMonsterGrow(monster, ai);
    }
}
