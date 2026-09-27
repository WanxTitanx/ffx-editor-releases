using System;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.FfxLib.Ai
{
    /// <summary>The exact worker/entrypoint selected by the monster WorkerFile event mapping.</summary>
    public readonly record struct AiEventHook(int WorkerIndex, int EntrypointIndex);

    /// <summary>
    /// Decodes the event mapping stored between WorkerFilePointer and StatSheetPointer in a monster bin.
    /// The mapping identifies the CombatHandler worker and maps purpose slot 0 (onTurn) to an entrypoint index.
    /// </summary>
    public static class AiWorkerMapping
    {
        const byte CombatHandler = 2;
        const int OnTurnPurpose = 0;
        const int OnHitPurpose = 3;

        public static bool TryResolveCombatOnTurn(
            byte[] monsterBin,
            AiScriptFile script,
            out AiEventHook hook,
            out string error) =>
            TryResolveCombatPurpose(monsterBin, script, OnTurnPurpose, "onTurn", out hook, out error);

        public static bool TryResolveCombatOnHit(
            byte[] monsterBin,
            AiScriptFile script,
            out AiEventHook hook,
            out string error) =>
            TryResolveCombatPurpose(monsterBin, script, OnHitPurpose, "onHit", out hook, out error);

        static bool TryResolveCombatPurpose(
            byte[] monsterBin,
            AiScriptFile script,
            int purposeIndex,
            string purposeLabel,
            out AiEventHook hook,
            out string error)
        {
            hook = default;
            error = string.Empty;
            ArgumentNullException.ThrowIfNull(monsterBin);
            ArgumentNullException.ThrowIfNull(script);

            if (monsterBin.Length < 0x34)
                return Fail("monster bin smaller than header 0x34.", out error);

            int workerPtr = I32(monsterBin, 0x08);
            int statPtr = I32(monsterBin, 0x0C);
            if (workerPtr <= 0 || statPtr <= workerPtr || statPtr > monsterBin.Length)
                return Fail(Strings.U_Ai_WorkerBadPointers, out error);

            int length = statPtr - workerPtr;
            if (length < 2)
                return Fail(Strings.U_Ai_WorkerFileEmpty, out error);

            int sectionCount = monsterBin[workerPtr];
            int preSectionLength = monsterBin[workerPtr + 1];
            int sectionLine = preSectionLength + ((preSectionLength & 1) == 0 ? 2 : 3);
            if (sectionCount <= 0 || preSectionLength < 2 || sectionLine + sectionCount * 4 > length)
                return Fail("WorkerFile header out of bounds.", out error);

            bool foundCombat = false;
            for (int i = 0; i < sectionCount; i++)
            {
                int record = workerPtr + sectionLine + i * 4;
                int workerIndex = monsterBin[record];
                byte workerType = monsterBin[record + 1];
                int sectionOffset = U16(monsterBin, record + 2);
                if (workerType != CombatHandler) continue;

                foundCombat = true;
                if (workerIndex < 0 || workerIndex >= script.Workers.Count)
                    return Fail(string.Format(Strings.U_Ai_CombatHandlerPointsTo, workerIndex, script.Workers.Count), out error);
                if (sectionOffset < 0 || sectionOffset + 2 > length)
                    return Fail("CombatHandler section out of WorkerFile.", out error);

                int section = workerPtr + sectionOffset;
                int valueCount = U16(monsterBin, section);
                if (valueCount <= purposeIndex || sectionOffset + 2 + valueCount * 2 > length)
                    return Fail(Strings.U_Ai_CombatHandlerPayloadTruncated, out error);

                int entrypointIndex = U16(monsterBin, section + 2 + purposeIndex * 2);
                if (entrypointIndex == 0xFFFF)
                    return Fail(string.Format(Strings.U_Ai_CombatHandlerNoEvent, purposeLabel), out error);
                if (entrypointIndex >= script.Workers[workerIndex].Entrypoints.Count)
                    return Fail(
                        string.Format(Strings.U_Ai_EntrypointPointsTo, purposeLabel, entrypointIndex, workerIndex,
                            script.Workers[workerIndex].Entrypoints.Count),
                        out error);

                hook = new AiEventHook(workerIndex, entrypointIndex);
                return true;
            }

            return Fail(foundCombat ? string.Format(Strings.U_Ai_CombatHandlerNoUsable, purposeLabel) : Strings.U_Ai_WorkerFileNoCombatHandler, out error);
        }

        static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }

        static int I32(byte[] bytes, int offset) => BitConverter.ToInt32(bytes, offset);
        static int U16(byte[] bytes, int offset) => BitConverter.ToUInt16(bytes, offset);
    }
}
