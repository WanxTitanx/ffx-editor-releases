using System;
using System.Collections.Generic;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.Monster;

namespace FFXProjectEditor.FfxLib.Ai.Sin
{
    // Flan-family battle-init scale — corpus: m017 (0.7), m020 (1.3), m184 Gemini (-1,1,1 via PUSHII).
    // Default inline path inserts PUSHF×3 + scaleOwnSize (0x7028) after DeathAnimation writeChrProperty.
    public static class SinScaleOpener
    {
        public const string RecipeId = "SIN-SCALE-OPENER";
        public const float DefaultUniformScale = 1.8f;

        const byte PUSHF = 0xAF, CALLPOPA = 0xD8;
        const ushort ScaleOwnSize = 0x7028;
        const ushort WriteChrProperty = 0x7018;
        const ushort DeathAnimationField = 0x004F;

        public static bool ScriptHasScaleOwnSize(AiScriptFile script) =>
            script.Instructions.Any(i => i.Opcode == CALLPOPA && i.Operand == ScaleOwnSize);

        /// <summary>Uniform PUSHF×3 + scaleOwnSize sites (pool index + current f32).</summary>
        public static IReadOnlyList<(int PoolIndex, float Value)> EnumerateUniformScaleOwnSize(AiScriptFile script)
        {
            ArgumentNullException.ThrowIfNull(script);
            var seen = new HashSet<int>();
            var result = new List<(int, float)>();
            IReadOnlyList<AiInstruction> instrs = script.Instructions;
            for (int i = 3; i < instrs.Count; i++)
            {
                if (instrs[i].Opcode != CALLPOPA || instrs[i].Operand != ScaleOwnSize)
                    continue;
                AiInstruction a = instrs[i - 3], b = instrs[i - 2], c = instrs[i - 1];
                if (a.Opcode != PUSHF || b.Opcode != PUSHF || c.Opcode != PUSHF
                    || a.Operand != b.Operand || b.Operand != c.Operand)
                    continue;
                int idx = a.Operand;
                if (!seen.Add(idx))
                    continue;
                result.Add((idx, ReadFloatConst(script, idx)));
            }
            return result;
        }

        public static IReadOnlyList<AiInstruction> BuildUniformScaleAction(ushort floatPoolIndex) => new[]
        {
            Op(PUSHF, floatPoolIndex),
            Op(PUSHF, floatPoolIndex),
            Op(PUSHF, floatPoolIndex),
            Op(CALLPOPA, ScaleOwnSize),
        };

        /// <summary>Corpus-faithful: insert scale block right after DeathAnimation init (m020 pattern).</summary>
        public static SinMonsterEmitResult TryEmitInlineAfterDeathAnimation(byte[] monsterBin, float uniformScale)
        {
            ArgumentNullException.ThrowIfNull(monsterBin);
            if (!float.IsFinite(uniformScale) || uniformScale <= 0f)
                return SinMonsterEmitResult.Fail(RecipeId, "scale must be finite and > 0");

            byte[]? origAi = AiScript_File.SliceAiFileFromMonster(monsterBin);
            if (origAi is null)
                return SinMonsterEmitResult.Fail(RecipeId, "fixture has no AI partition");

            AiScriptFile origScript = AiScript_File.Read(origAi);
            if (!origScript.HasScript)
                return SinMonsterEmitResult.Fail(RecipeId, "fixture AI script is empty/degenerate");
            if (ScriptHasScaleOwnSize(origScript))
                return SinMonsterEmitResult.Fail(RecipeId, "script already contains scaleOwnSize (0x7028)");

            byte[] aiWithFloat;
            int poolIndex;
            try
            {
                (aiWithFloat, poolIndex) = AiScript_File.AppendFloatConst(origScript, uniformScale);
            }
            catch (Exception ex)
            {
                return SinMonsterEmitResult.Fail(RecipeId, $"float pool: {ex.Message}");
            }

            AiScriptFile script = AiScript_File.Read(aiWithFloat);
            List<AiInstruction> instrs = script.Instructions.ToList();
            int anchor = FindDeathAnimationWriteIndex(instrs);
            if (anchor < 0)
                return SinMonsterEmitResult.Fail(RecipeId, "DeathAnimation writeChrProperty anchor not found");

            if (anchor + 1 < instrs.Count
                && instrs[anchor + 1].Opcode == CALLPOPA
                && instrs[anchor + 1].Operand == ScaleOwnSize)
            {
                return SinMonsterEmitResult.Fail(RecipeId, "scaleOwnSize already present after DeathAnimation");
            }

            var block = BuildUniformScaleAction((ushort)poolIndex);
            instrs.InsertRange(anchor + 1, block);

            byte[] newAi;
            try { newAi = AiScript_File.Rebuild(script, instrs); }
            catch (Exception ex) { return SinMonsterEmitResult.Fail(RecipeId, $"rebuild: {ex.Message}"); }

            byte[] editedMonster;
            try { editedMonster = AiScript_File.SpliceAiFileIntoMonsterGrow(monsterBin, newAi); }
            catch (Exception ex) { return SinMonsterEmitResult.Fail(RecipeId, $"splice: {ex.Message}"); }

            return ValidateEmit(origScript, origAi, editedMonster, newAi, "inline after DeathAnimation");
        }

        /// <summary>Patches uniform PUSHF×3 + scaleOwnSize blocks by multiplying existing float-pool values
        /// by <paramref name="scaleFactor"/> (length-preserving). E.g. Chimera @ 0.75× + factor 1.8 → 1.35×.</summary>
        public static SinMonsterEmitResult TryReplaceUniformScaleOwnSize(byte[] monsterBin, float scaleFactor)
        {
            ArgumentNullException.ThrowIfNull(monsterBin);
            if (!float.IsFinite(scaleFactor) || scaleFactor <= 0f)
                return SinMonsterEmitResult.Fail(RecipeId, "scale factor must be finite and > 0");

            byte[]? origAi = AiScript_File.SliceAiFileFromMonster(monsterBin);
            if (origAi is null)
                return SinMonsterEmitResult.Fail(RecipeId, "fixture has no AI partition");

            AiScriptFile origScript = AiScript_File.Read(origAi);
            if (!origScript.HasScript)
                return SinMonsterEmitResult.Fail(RecipeId, "fixture AI script is empty/degenerate");
            if (!ScriptHasScaleOwnSize(origScript))
                return SinMonsterEmitResult.Fail(RecipeId, "script has no scaleOwnSize (0x7028) to replace");

            IReadOnlyList<AiInstruction> instrs = origScript.Instructions;
            var poolIndices = new HashSet<int>();
            for (int i = 3; i < instrs.Count; i++)
            {
                if (instrs[i].Opcode != CALLPOPA || instrs[i].Operand != ScaleOwnSize)
                    continue;
                AiInstruction a = instrs[i - 3], b = instrs[i - 2], c = instrs[i - 1];
                if (a.Opcode == PUSHF && b.Opcode == PUSHF && c.Opcode == PUSHF
                    && a.Operand == b.Operand && b.Operand == c.Operand)
                {
                    poolIndices.Add(a.Operand);
                }
            }

            if (poolIndices.Count == 0)
                return SinMonsterEmitResult.Fail(RecipeId, "scaleOwnSize found but no uniform PUSHF×3 prelude");

            byte[] ai = origScript.OriginalAiFileBytes;
            AiScriptFile script = origScript;
            var scaled = new List<(int Index, float Before, float After)>();
            foreach (int idx in poolIndices.OrderBy(i => i))
            {
                float before = ReadFloatConst(script, idx);
                float after = before * scaleFactor;
                if (!float.IsFinite(after))
                    return SinMonsterEmitResult.Fail(RecipeId, "scaled value must be finite");
                ai = AiScript_File.EditFloatConst(script = AiScript_File.Read(ai), idx, after);
                scaled.Add((idx, before, after));
            }

            byte[] editedMonster;
            try { editedMonster = AiScript_File.SpliceAiFileIntoMonster(monsterBin, ai); }
            catch (Exception ex) { return SinMonsterEmitResult.Fail(RecipeId, $"splice: {ex.Message}"); }

            return ValidateReplaceEmit(origScript, origAi, editedMonster, ai, scaled, scaleFactor);
        }

        /// <summary>Prepends scale on CombatHandler entrypoint 0 (runs before vanilla init body).</summary>
        public static SinMonsterEmitResult TryEmitBattleStartScale(byte[] monsterBin, float uniformScale)
        {
            ArgumentNullException.ThrowIfNull(monsterBin);
            byte[]? origAi = AiScript_File.SliceAiFileFromMonster(monsterBin);
            if (origAi is null)
                return SinMonsterEmitResult.Fail(RecipeId, "fixture has no AI partition");

            AiScriptFile origScript = AiScript_File.Read(origAi);
            (byte[] aiWithFloat, int poolIndex) = AiScript_File.AppendFloatConst(origScript, uniformScale);
            byte[] monsterWithPool = AiScript_File.SpliceAiFileIntoMonsterGrow(monsterBin, aiWithFloat);

            return SinSandboxApplySession.TryEmitRawAction(
                monsterWithPool,
                RecipeId,
                BuildUniformScaleAction((ushort)poolIndex),
                entrypointOverride: 0,
                guardOverride: AiAutomation.BuildAlwaysGuard(),
                stopAfterAction: false);
        }

        static SinMonsterEmitResult ValidateReplaceEmit(
            AiScriptFile origScript, byte[] origAi, byte[] editedMonster, byte[] newAi,
            IReadOnlyList<(int Index, float Before, float After)> scaled, float scaleFactor)
        {
            byte[]? reAi = AiScript_File.SliceAiFileFromMonster(editedMonster);
            if (reAi is null)
                return SinMonsterEmitResult.Fail(RecipeId, "edited monster lost AI partition");

            AiScriptFile editedScript = AiScript_File.Read(reAi);
            if (editedScript.Instructions.Count != origScript.Instructions.Count)
            {
                return SinMonsterEmitResult.Fail(RecipeId,
                    $"replace must preserve instruction count ({origScript.Instructions.Count} -> {editedScript.Instructions.Count})");
            }

            bool reparsed = editedScript.HasScript && editedScript.CodeWalkClosedExactly
                            && editedScript.UnknownOpcodes.Count == 0;
            AiValidationReport report = AiValidator.ValidateRebuilt(newAi, origAi.Length);
            if (!reparsed || !report.IsValid)
                return SinMonsterEmitResult.Fail(RecipeId, $"post-replace check failed (reparsed={reparsed}; labClean={report.IsValid})");

            var ci = System.Globalization.CultureInfo.InvariantCulture;
            string factor = scaleFactor.ToString(ci);
            string detail = string.Join("; ", scaled.Select(s =>
                $"pool[{s.Index}] {s.Before.ToString(ci)} × {factor} = {s.After.ToString(ci)}"));
            return new SinMonsterEmitResult
            {
                Ok = true,
                RecipeId = RecipeId,
                EditedMonster = editedMonster,
                AddedRows = 0,
                WorkerIndex = 1,
                EntrypointIndex = 0,
                WorkerResolution = $"multiply uniform scale ({detail})",
                Notes = new[] { "float-pool EditFloatConst only; stacks on existing mod scale" },
            };
        }

        static float ReadFloatConst(AiScriptFile script, int index)
        {
            int off = script.FloatPoolOffset + 4 * index;
            if (off < 0 || off + 4 > script.OriginalAiFileBytes.Length)
                throw new InvalidOperationException($"float pool index {index} out of range");
            return BitConverter.ToSingle(script.OriginalAiFileBytes, off);
        }

        static SinMonsterEmitResult ValidateEmit(
            AiScriptFile origScript, byte[] origAi, byte[] editedMonster, byte[] newAi, string mode)
        {
            byte[]? reAi = AiScript_File.SliceAiFileFromMonster(editedMonster);
            if (reAi is null)
                return SinMonsterEmitResult.Fail(RecipeId, "edited monster lost AI partition");

            AiScriptFile editedScript = AiScript_File.Read(reAi);
            IReadOnlyList<AiDiffEntry> diff = AiScript_Diff.Compare(origScript, editedScript);
            SinSandboxApplySession.IsConservativeAddedOnlyDiff(
                diff, origScript.Instructions.Count, editedScript.Instructions.Count,
                out int added, out int modified, out int removed);

            bool reparsed = editedScript.HasScript && editedScript.CodeWalkClosedExactly
                            && editedScript.UnknownOpcodes.Count == 0;
            AiValidationReport report = AiValidator.ValidateRebuilt(newAi, origAi.Length);
            if (!reparsed || !report.IsValid)
            {
                return SinMonsterEmitResult.Fail(RecipeId,
                    $"post-emit check failed (+{added} ~{modified} -{removed}; reparsed={reparsed}; labClean={report.IsValid})");
            }

            return new SinMonsterEmitResult
            {
                Ok = true,
                RecipeId = RecipeId,
                EditedMonster = editedMonster,
                AddedRows = added,
                WorkerIndex = 1,
                EntrypointIndex = 0,
                WorkerResolution = mode,
                Notes = new[] { $"scaleOwnSize via {mode}" },
            };
        }

        static int FindDeathAnimationWriteIndex(IReadOnlyList<AiInstruction> instrs)
        {
            for (int i = 0; i < instrs.Count; i++)
            {
                if (instrs[i].Opcode != CALLPOPA || instrs[i].Operand != WriteChrProperty)
                    continue;
                for (int j = Math.Max(0, i - 4); j < i; j++)
                {
                    if (instrs[j].Opcode == 0xAE && instrs[j].Operand == DeathAnimationField)
                        return i;
                }
            }
            return -1;
        }

        static AiInstruction Op(byte opcode, ushort operand = 0) => new()
        {
            Offset = -1,
            Opcode = opcode,
            HasOperand = AiScript_File.IsOperandBearing(opcode),
            Operand = operand,
            OperandKind = AiScript_File.OperandKindOf(opcode),
        };
    }
}
