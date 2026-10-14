using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FFXProjectEditor.FfxLib.Ai;

namespace FFXProjectEditor.Core.Writers
{
    /// <summary>
    /// L2 of docs/ai/P2_INTEGRACAO_PIPELINE_2026-07-31.md: writer adapter for the ATEL phase-rotation
    /// recipe (AiPhaseRotationWriter), staging a rebuilt monster_*.bin WITHOUT ever touching the source
    /// (the OperationPlan pipeline promotes the staged bytes later).
    ///
    /// The transformation is the REAL FfxLib recipe — `AiPhaseRotationWriter.Apply(AiScriptFile,
    /// AiPhaseRotationRecipe)` (AiPhaseRotationRecipe.cs:90) — which lowers each phase to the proven
    /// AppendGuardedAction shape (guard → action → optional RET) and re-validates every phase with
    /// `AiValidator.TryValidateRebuiltAllowingBaselineUnknowns` (AiPhaseRotationRecipe.cs:173-181).
    /// This recipe GROWS the AiFile (append-only), so the re-insertion into the monster bin uses the
    /// codec's own grow-aware splice `AiScript_File.SpliceAiFileIntoMonsterGrow` (AiScript_File.cs:1123):
    /// it shifts every later partition by +delta, rewrites the 0x34 header section pointers + FileSize,
    /// zero-pads the AiFile partition to a 16-byte boundary (WorkerFile stays 16-aligned — corpus
    /// invariant + the game heap is 16-aligned), and preserves every non-Ai byte verbatim
    /// (P2_VALIDACAO_SOURCE_2026-07-31.md §1.3 + recommendation 2).
    ///
    /// Honesty notes (docs/ai/P2_RECEITAS_ATEL_2026-07-31.md §4): phase rotation is a class-B (structural)
    /// recipe — it grows the script, so it is RT2-required. This adapter does NOT claim in-game behaviour;
    /// it guarantees the staged bin is structurally valid (walk closes exactly, no unknown opcodes,
    /// RT0 byte-identical round-trip of the emitted AiFile, re-slice byte-identical after splice).
    /// The whole-bin SHA-256 precondition (`monsterBinHash`) is the audit contract: the splice relocates
    /// partitions on grow, so the bin is the hash unit (P2_INTEGRACAO §2.2).
    /// </summary>
    public sealed class AtelPhaseRotationAdapter : IWriterAdapter
    {
        public string CapabilityId => "atel-recipe-phase-rotation";
        public string DisplayName => "ATEL Phase Rotation Recipe";
        public RiskLevel Risk => RiskLevel.Moderate;

        // ── allowlist — mirrors the REAL AiPhaseRotationRecipe parameters (AiPhaseRotationRecipe.cs:21-46) ──

        /// <summary>Transport of the complete recipe object (AiPhaseRotationRecipe) — the source of truth
        /// for the transformation, as sketched in P2_INTEGRACAO_PIPELINE §2 / P2_VALIDACAO_SOURCE:110.</summary>
        public const string RecipeField = "Recipe";

        /// <summary>CounterVariableIndex (ushort): index of the phase-counter variable in the AiFile
        /// variable table (the variable the guards compare/advance). Must exist in the script.</summary>
        public const string CounterVariableIndexField = "CounterVariableIndex";

        /// <summary>Steps (IReadOnlyList&lt;AiPhaseRotationStep&gt;): ordered phase list; each step carries
        /// Number, Label, CommandOperand, IsFinalPhase, StopHere, VarMutations, TargetRecipe, RandomChance,
        /// RandomK, ExtraGuard, AdditionalGuard, ForbiddenRite, TriggerKind (+ per-step Worker/Entrypoint).</summary>
        public const string StepsField = "Steps";

        /// <summary>FinalLimit (int, 0..65535): threshold of the final phase — the counter value above
        /// which the last phase always runs (guard PUSHV counter &gt; FinalLimit).</summary>
        public const string FinalLimitField = "FinalLimit";

        /// <summary>WorkerIndex (int, &gt;= 0): the AiFile worker the phases hook (required).</summary>
        public const string WorkerIndexField = "WorkerIndex";

        /// <summary>EntrypointIndex (int, &gt;= 0): the worker entrypoint the guarded blocks are appended to.</summary>
        public const string EntrypointIndexField = "EntrypointIndex";

        /// <summary>DefaultTargetRecipe (AiTargetRecipe): target statement used when a step does not
        /// override TargetRecipe (Literal operand / FindAliveFrontlineAny / FindAliveFrontlineLowestHp).</summary>
        public const string DefaultTargetRecipeField = "DefaultTargetRecipe";

        /// <summary>MonsterId (string, optional): informational monster identifier carried by the recipe.</summary>
        public const string MonsterIdField = "MonsterId";

        /// <summary>monsterBinHash (string, 64-hex): SHA-256 of the WHOLE monster bin (precondition).
        /// Not a recipe parameter — an audit precondition validated before anything is read/written.</summary>
        public const string MonsterBinHashField = "monsterBinHash";

        private static readonly HashSet<string> RecipeParameterFields = new(StringComparer.OrdinalIgnoreCase)
        {
            RecipeField, CounterVariableIndexField, StepsField, FinalLimitField,
            WorkerIndexField, EntrypointIndexField, DefaultTargetRecipeField, MonsterIdField,
        };

        private static readonly HashSet<string> EditableFields = new(StringComparer.OrdinalIgnoreCase)
        {
            RecipeField, CounterVariableIndexField, StepsField, FinalLimitField,
            WorkerIndexField, EntrypointIndexField, DefaultTargetRecipeField, MonsterIdField,
            MonsterBinHashField,
        };

        public string ComputeBeforeHash(string sourcePath)
        {
            ArgumentNullException.ThrowIfNull(sourcePath);
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(sourcePath);
            return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        }

        public IReadOnlyList<string> ValidateEdits(IReadOnlyDictionary<string, object> edits)
        {
            ArgumentNullException.ThrowIfNull(edits);
            var errors = new List<string>();

            // (1) Allowlist: every key must be a real recipe parameter (or the hash precondition).
            foreach (string key in edits.Keys)
            {
                if (!EditableFields.Contains(key))
                    errors.Add("Field '" + key + "' is not editable on ATEL phase-rotation recipes");
            }

            // (2) The complete recipe object is the source of truth for the transformation.
            if (!edits.TryGetValue(RecipeField, out object? recipeValue) || recipeValue is not AiPhaseRotationRecipe recipe)
            {
                errors.Add("'" + RecipeField + "' (AiPhaseRotationRecipe) is required");
                return errors;
            }

            // (3) Worker target field is required (index/worker of the recipe hook) with a basic range.
            if (!edits.TryGetValue(WorkerIndexField, out object? workerValue))
            {
                errors.Add("'" + WorkerIndexField + "' is required");
            }
            else if (workerValue is not int workerIndex || workerIndex < 0)
            {
                errors.Add("'" + WorkerIndexField + "' must be a non-negative integer");
            }
            else if (workerIndex != recipe.WorkerIndex)
            {
                errors.Add("'" + WorkerIndexField + "' (" + workerIndex + ") does not match recipe.WorkerIndex ("
                           + recipe.WorkerIndex + ")");
            }

            // (4) Optional recipe parameters: type/range validation + consistency with the recipe object.
            if (edits.TryGetValue(EntrypointIndexField, out object? entryValue))
            {
                if (entryValue is not int entryIndex || entryIndex < 0)
                    errors.Add("'" + EntrypointIndexField + "' must be a non-negative integer");
                else if (entryIndex != recipe.EntrypointIndex)
                    errors.Add("'" + EntrypointIndexField + "' (" + entryIndex + ") does not match recipe.EntrypointIndex ("
                               + recipe.EntrypointIndex + ")");
            }

            if (edits.TryGetValue(CounterVariableIndexField, out object? counterValue))
            {
                bool counterOk = counterValue switch
                {
                    ushort u => true,
                    int i => i >= 0 && i <= ushort.MaxValue,
                    _ => false,
                };
                if (!counterOk)
                    errors.Add("'" + CounterVariableIndexField + "' must be a non-negative integer (0-65535)");
                else if (ToUshort(counterValue!) != recipe.CounterVariableIndex)
                    errors.Add("'" + CounterVariableIndexField + "' does not match recipe.CounterVariableIndex ("
                               + recipe.CounterVariableIndex + ")");
            }

            if (edits.TryGetValue(FinalLimitField, out object? limitValue))
            {
                if (limitValue is not int finalLimit || finalLimit < 0 || finalLimit > ushort.MaxValue)
                    errors.Add("'" + FinalLimitField + "' must be an integer between 0 and 65535");
                else if (finalLimit != recipe.FinalLimit)
                    errors.Add("'" + FinalLimitField + "' (" + finalLimit + ") does not match recipe.FinalLimit ("
                               + recipe.FinalLimit + ")");
            }

            if (edits.TryGetValue(DefaultTargetRecipeField, out object? targetValue))
            {
                if (targetValue is not AiTargetRecipe target)
                    errors.Add("'" + DefaultTargetRecipeField + "' must be an AiTargetRecipe");
                else if (target != recipe.DefaultTargetRecipe)
                    errors.Add("'" + DefaultTargetRecipeField + "' does not match recipe.DefaultTargetRecipe");
            }

            if (edits.TryGetValue(StepsField, out object? stepsValue))
            {
                if (stepsValue is not IReadOnlyList<AiPhaseRotationStep> steps)
                    errors.Add("'" + StepsField + "' must be a list of AiPhaseRotationStep");
                else if (!steps.SequenceEqual(recipe.Steps))
                    errors.Add("'" + StepsField + "' does not match recipe.Steps (the recipe object is the source of truth)");
            }

            if (edits.TryGetValue(MonsterIdField, out object? monsterIdValue))
            {
                if (monsterIdValue is not string monsterId)
                    errors.Add("'" + MonsterIdField + "' must be a string");
                else if (!string.Equals(monsterId, recipe.MonsterId, StringComparison.Ordinal))
                    errors.Add("'" + MonsterIdField + "' does not match recipe.MonsterId");
            }

            if (edits.TryGetValue(MonsterBinHashField, out object? hashValue))
            {
                if (hashValue is not string hash || hash.Length != 64 || !IsHex(hash))
                    errors.Add("'" + MonsterBinHashField + "' must be a 64-character hex SHA-256 string");
            }

            // (5) Recipe shape — mirrors the checks the Apply itself performs on the object
            // (AiPhaseRotationRecipe.cs:96-149) that do NOT depend on the target AiFile (var existence
            // and worker/entrypoint counts are validated in StageAsync after the script is read).
            ValidateRecipeShape(recipe, errors);

            return errors;
        }

        public async Task<string> StageAsync(
            string sourcePath,
            string stagingPath,
            IReadOnlyDictionary<string, object> edits,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(edits);

            // (a) Precondition: whole-monster-bin SHA-256. The splice relocates partitions on grow, so the
            //     bin is the hash unit (P2_INTEGRACAO §2.2). A mismatch aborts BEFORE anything is read/staged.
            if (edits.TryGetValue(MonsterBinHashField, out object? hashValue))
            {
                if (hashValue is not string expectedHash)
                    throw new InvalidOperationException(
                        "'monsterBinHash' must be a 64-character hex SHA-256 string.");

                string? hashError = AtelScriptAdapter.ValidateMonsterBinHash(sourcePath, expectedHash);
                if (hashError != null)
                    throw new InvalidOperationException(hashError);
            }

            ct.ThrowIfCancellationRequested();
            byte[] monsterBytes = await File.ReadAllBytesAsync(sourcePath, ct);

            // (b) Slice the AiFile partition out of the monster bin (codec API).
            byte[]? aiFile = AiScript_File.SliceAiFileFromMonster(monsterBytes);
            if (aiFile == null)
                throw new InvalidOperationException(
                    "Monster bin " + sourcePath + " has no AI partition "
                    + "(AiFilePointer@0x04 / WorkerFilePointer@0x08 invalid or out of range).");

            // (c) Read the AiFile through the codec.
            AiScriptFile script = AiScript_File.Read(aiFile);

            // (d) Pre-validation: the script must walk closed exactly on CodeLength. If it does not, the
            //     codec does not understand it — writing would be guessing, so we refuse before applying.
            if (!script.HasScript)
                throw new InvalidOperationException("AiFile has no script (refusing to write).");
            if (!script.CodeWalkClosedExactly)
                throw new InvalidOperationException(
                    "AiFile script walk does not close exactly on CodeLength — invalid/corrupt script; refusing to write.");

            // (e) Apply the REAL recipe (in-memory only — the source file is never written here).
            AiPhaseRotationRecipe recipe = ResolveRecipe(edits);
            AiPhaseRotationApplyResult result;
            try
            {
                result = AiPhaseRotationWriter.Apply(script, recipe);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException)
            {
                // The Apply re-validates everything against the actual AiFile (var table, worker/entrypoint
                // counts, triggers, limits). Its messages are the authority; surface them with context.
                throw new InvalidOperationException(
                    "ATEL phase-rotation recipe rejected for " + sourcePath + ": " + ex.Message, ex);
            }

            // (f) Post-validation of the emitted AiFile: it must walk closed, carry no unknown opcodes and
            //     round-trip byte-identical (Write(Read(x)) == x). Any failure → throw, nothing staged.
            AiScriptFile reRead;
            try
            {
                reRead = AiScript_File.Read(result.AiFile);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException(
                    "ATEL phase-rotation output does not re-parse: " + ex.Message, ex);
            }

            if (!reRead.CodeWalkClosedExactly || reRead.UnknownOpcodes.Count > 0
                || !AiScript_File.RoundTripsByteIdentical(result.AiFile))
            {
                throw new InvalidDataException(
                    "ATEL phase-rotation output failed post-validation: CodeWalkClosedExactly="
                    + reRead.CodeWalkClosedExactly + ", UnknownOpcodes=" + reRead.UnknownOpcodes.Count
                    + ", RoundTripsByteIdentical=" + AiScript_File.RoundTripsByteIdentical(result.AiFile)
                    + ". Nothing was staged.");
            }

            // (g) Re-insert the emitted AiFile into the monster bin via the codec's grow-aware splice:
            //     every non-Ai byte is preserved verbatim, later partitions are shifted by +delta, the
            //     0x34 header section pointers + FileSize are rewritten and the AiFile partition is
            //     zero-padded to a 16-byte boundary (WorkerFile stays 16-aligned).
            byte[] newMonsterBytes = AiScript_File.SpliceAiFileIntoMonsterGrow(monsterBytes, result.AiFile);

            // Post-splice verification: re-slicing the new bin must return exactly the emitted AiFile
            // (plus the 16-boundary zero pad the splice added).
            byte[]? reSliced = AiScript_File.SliceAiFileFromMonster(newMonsterBytes);
            if (reSliced == null || !SplicePrefixMatches(reSliced, result.AiFile))
            {
                throw new InvalidDataException(
                    "Re-slicing the staged monster bin did not return the emitted AiFile — splice integrity "
                    + "check failed. Nothing was staged.");
            }

            // (h) Staging is written ONLY after every check passed.
            string? dir = Path.GetDirectoryName(stagingPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            await File.WriteAllBytesAsync(stagingPath, newMonsterBytes, ct);
            return ComputeSha256(newMonsterBytes);
        }

        public FileDiffSummary DescribeChanges(IReadOnlyDictionary<string, object> edits)
        {
            ArgumentNullException.ThrowIfNull(edits);

            var changed = new List<string>();
            foreach (string key in RecipeParameterFields)
            {
                if (edits.ContainsKey(key))
                    changed.Add(key);
            }

            string human = "ATEL phase rotation";
            if (edits.TryGetValue(RecipeField, out object? recipeValue) && recipeValue is AiPhaseRotationRecipe recipe)
            {
                human = "ATEL phase rotation: worker " + recipe.WorkerIndex + ", entrypoint "
                        + recipe.EntrypointIndex + ", var[" + recipe.CounterVariableIndex + "], "
                        + recipe.Steps.Count + " fase(s), final limit " + recipe.FinalLimit;
            }

            return new FileDiffSummary
            {
                FieldsChanged = changed.Count,
                ChangedFieldNames = changed,
                HumanSummary = human,
            };
        }

        /// <summary>
        /// Three-layer dry-run diff (bytes / disassembly / semantic IR) between two monster bin states for
        /// the P2 "simulate → review diff" flow. Each side's AiFile partition is sliced via the codec
        /// (SliceAiFileFromMonster) and the diff is computed with AiDiffThreeLayer.Compute(hasExtraInfo: false)
        /// — the ATEL AiFile format has no entry-size variant, so the flag is irrelevant (documented in
        /// AiDiffThreeLayer). Returns null when either side has no AI partition (not comparable).
        /// </summary>
        public static ThreeLayerDiff? ComputeDryRunDiff(byte[] monsterBinBefore, byte[] monsterBinAfter)
        {
            ArgumentNullException.ThrowIfNull(monsterBinBefore);
            ArgumentNullException.ThrowIfNull(monsterBinAfter);

            byte[]? before = AiScript_File.SliceAiFileFromMonster(monsterBinBefore);
            byte[]? after = AiScript_File.SliceAiFileFromMonster(monsterBinAfter);
            if (before == null || after == null)
                return null;

            return AiDiffThreeLayer.Compute(before, after, hasExtraInfo: false);
        }

        // ── helpers ───────────────────────────────────────────────────────────────────────────────────────

        static AiPhaseRotationRecipe ResolveRecipe(IReadOnlyDictionary<string, object> edits)
        {
            if (!edits.TryGetValue(RecipeField, out object? value) || value is not AiPhaseRotationRecipe recipe)
            {
                throw new InvalidOperationException(
                    "'" + RecipeField + "' (AiPhaseRotationRecipe) is required to stage an ATEL phase rotation.");
            }

            return recipe;
        }

        /// <summary>Recipe shape checks that mirror AiPhaseRotationRecipe.Apply (AiPhaseRotationRecipe.cs:96-149)
        /// and can run without the target AiFile. Var-table existence and worker/entrypoint counts stay in
        /// StageAsync (the Apply validates them against the real script).</summary>
        static void ValidateRecipeShape(AiPhaseRotationRecipe recipe, List<string> errors)
        {
            if (recipe.Steps.Count == 0)
            {
                errors.Add("Recipe has no phases (Steps must contain at least one AiPhaseRotationStep)");
                return;
            }

            if (recipe.FinalLimit < 0 || recipe.FinalLimit > ushort.MaxValue)
                errors.Add("Recipe FinalLimit must be between 0 and 65535");

            int finalCount = recipe.Steps.Count(s => s.IsFinalPhase);
            if (finalCount > 1)
                errors.Add("Recipe has " + finalCount + " final phases; the writer accepts at most one");

            foreach (AiPhaseRotationStep step in recipe.Steps)
            {
                if (step.TriggerKind == AiPhaseTriggerKind.BattleStart)
                    errors.Add("Step '" + step.Label + "': BattleStart trigger is disabled in this recipe path");
                if (step.TriggerKind == AiPhaseTriggerKind.AfterAnyValidTurn)
                    errors.Add("Step '" + step.Label + "': AfterAnyValidTurn is a legacy runtime hook, not an AiFile trigger");

                if (step.RandomChance && (step.RandomK < 2 || step.RandomK > ushort.MaxValue))
                    errors.Add("Step '" + step.Label + "': chance must be 1 in K with K between 2 and 65535");

                int stepWorker = step.WorkerIndex ?? recipe.WorkerIndex;
                int stepEntrypoint = step.EntrypointIndex ?? recipe.EntrypointIndex;
                if (stepWorker < 0)
                    errors.Add("Step '" + step.Label + "': worker index must be non-negative");
                if (stepEntrypoint < 0)
                    errors.Add("Step '" + step.Label + "': entrypoint index must be non-negative");

                foreach (AiPhaseVarMutation mutation in step.VarMutations ?? Array.Empty<AiPhaseVarMutation>())
                {
                    if (mutation.SetInsteadOfAdd)
                    {
                        if (mutation.AddMin < 0 || mutation.AddMin > ushort.MaxValue)
                            errors.Add("Step '" + step.Label + "': reset/set of var[" + mutation.VariableIndex
                                       + "] must be between 0 and 65535");
                    }
                    else if (mutation.AddMin < 0 || mutation.AddMax < mutation.AddMin || mutation.AddMax > ushort.MaxValue)
                    {
                        errors.Add("Step '" + step.Label + "': advance of var[" + mutation.VariableIndex
                                   + "] must be 0..65535 with min <= max");
                    }
                }
            }
        }

        static bool SplicePrefixMatches(byte[] reSliced, byte[] emitted)
        {
            if (reSliced.Length < emitted.Length)
                return false;
            for (int i = 0; i < emitted.Length; i++)
            {
                if (reSliced[i] != emitted[i])
                    return false;
            }
            for (int i = emitted.Length; i < reSliced.Length; i++)
            {
                if (reSliced[i] != 0)
                    return false;
            }
            return true;
        }

        static ushort ToUshort(object value) => value switch
        {
            ushort u => u,
            int i => (ushort)i,
            _ => 0,
        };

        static bool IsHex(string text)
        {
            foreach (char c in text)
            {
                if (!Uri.IsHexDigit(c))
                    return false;
            }
            return true;
        }

        static string ComputeSha256(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();
        }
    }
}
