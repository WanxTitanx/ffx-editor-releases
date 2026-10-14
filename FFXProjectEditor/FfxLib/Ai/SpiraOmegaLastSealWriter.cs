using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Monster;
using static FFXProjectEditor.FfxLib.Ai.SpiraNativeBossWriter;

namespace FFXProjectEditor.FfxLib.Ai;

public static class SpiraOmegaLastSealWriter
{
    public static byte[] Build(byte[] source)
    {
        int id = Monster_File.Read(source).StatSheetFile.MonsterId;
        if (id != 100 && id is not (911 or 912 or 913 or 914))
            throw new InvalidDataException("Last Seal ATEL is private to m100 and m911-m914.");
        // Arena Choice356 also loads m100. Its native turn emits one command,
        // which the admitted story controller replaces at the queue boundary.
        // Keep this fallback (and gauge initialization) native outside that fight.
        if (id == 100) return source.ToArray();
        var original = AiScript_File.Read(AiScript_File.SliceAiFileFromMonster(source)!);
        if (!AiWorkerMapping.TryResolveCombatOnTurn(source, original, out var turn, out string error)
            || !AiWorkerMapping.TryResolveCombatOnHit(source, original, out var hit, out error))
            throw new InvalidDataException(error);
        int entry = original.Workers[turn.WorkerIndex].Entrypoints[turn.EntrypointIndex] + original.ScriptStart;
        if (original.Instructions.Single(i => i.Offset == entry).Opcode != 0x9F)
            throw new InvalidDataException("Expected the native Omega turn entry; do not stack controller tickets.");
        int onHit = original.Workers[hit.WorkerIndex].Entrypoints[hit.EntrypointIndex] + original.ScriptStart;
        if (original.Instructions.Single(i => i.Offset == onHit).Opcode != 0x3C)
            throw new InvalidDataException("Last Seal onHit must remain empty; Hooks owns the reaction.");

        // One ordinary native action is the controller's input ticket. The Hooks
        // around-append integration replaces this packet, never appends a second
        // independent turn after it. The original Nova cycle is unreachable.
        var ticket = new[] { Op(0xAE, 0xFFF2), Op(0xAE, 4), Op(0xAE, 0), Op(0xAE, 0),
            Op(0xB5, 0x7010), Op(0xAE, 0x410F), Op(0xD8, 0x700B) };
        byte[] ai = AiScript_File.AppendGuardedAction(original, turn.WorkerIndex, turn.EntrypointIndex,
            new[] { Op(0xAE, 1) }, ticket, stopAfterAction: true);
        byte[] intermediate = AiScript_File.SpliceAiFileIntoMonsterGrow(source, ai);
        ai = SpiraUltimaWriter.BuildSmallNormal(intermediate, id <= 912 ? .45f : .35f);
        if (!AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(ai, original, out _, out error))
            throw new InvalidDataException(error);
        byte[] result = AiScript_File.SpliceAiFileIntoMonsterGrow(source, ai);
        if (!Monster_File.Read(result).Write().SequenceEqual(result)
            || !Monster_File.Read(result).WorkerFile.SequenceEqual(Monster_File.Read(source).WorkerFile)
            || !AiScript_File.SpliceAiFileIntoMonsterGrow(result, original.OriginalAiFileBytes).SequenceEqual(source))
            throw new InvalidDataException("Last Seal ATEL changed another partition or failed round-trip.");
        return result;
    }
}
