using System;
using FFXProjectEditor.Tests.Infrastructure;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using FFXProjectEditor.Core;
using FFXProjectEditor.Core.Writers;
using FFXProjectEditor.FfxLib.Ai;
using Xunit;

namespace FFXProjectEditor.Tests.Core
{
    /// <summary>
    /// L2 of docs/ai/P2_INTEGRACAO_PIPELINE_2026-07-31.md: AtelPhaseRotationAdapter contract.
    ///
    /// Fixtures: Fixtures/Monster/m000.bin + m001.bin (vanilla monster_*.bin). Decoded by hand:
    ///   • m001 w[0] has 5 variables + 2 entrypoints (entryTab@0x874) → the phase-rotation recipe applies
    ///     (CounterVariableIndex 0 + var[0] mutations are valid) and the staged bin must re-slice clean;
    ///   • m000 has 0 variables (varsOff == varsEnd) → the recipe is rejected by the Apply ("var[0] não
    ///     existe na tabela deste AiFile") — the honest negative path.
    /// Every test copies the fixture to a fresh temp dir first; staging paths are isolated per test.
    /// </summary>
    public class AtelPhaseRotationAdapterTests
    {
        private static readonly AtelPhaseRotationAdapter Adapter = new();

        /// <summary>Firaga (character black-magic) — the command the phase steps perform.</summary>
        private const ushort Firaga = 0x3049;

        /// <summary>FrontlineChars sentinel (AiPhaseRotationWriter constant) — literal target operand.</summary>
        private const ushort FrontlineChars = 0xFFF2;

        static byte[] MonsterFixtureBytes(string name) =>
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Monster", name));

        static string TempSource(string fixtureName)
        {
            string dir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "AtelPhaseRotationAdapterTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, fixtureName);
            File.WriteAllBytes(path, MonsterFixtureBytes(fixtureName));
            return path;
        }

        static string Sha256Of(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();
        }

        /// <summary>Build a valid 2-phase recipe (1 common + 1 final) against a fixture's real script,
        /// resolving worker/entrypoint exactly like the AiScriptLab phase-rotation gate
        /// (PickCombatWorker + PickMainEntrypoint).</summary>
        static AiPhaseRotationRecipe BuildValidRecipe(byte[] monsterBin)
        {
            byte[]? ai = AiScript_File.SliceAiFileFromMonster(monsterBin);
            Assert.NotNull(ai);
            AiScriptFile script = AiScript_File.Read(ai!);
            AiWorker? worker = AiAutomation.PickCombatWorker(script);
            Assert.NotNull(worker);
            Assert.NotEmpty(worker!.Entrypoints);
            int entrypoint = AiAutomation.PickMainEntrypoint(script, worker);

            return new AiPhaseRotationRecipe(
                CounterVariableIndex: 0,
                Steps: new[]
                {
                    new AiPhaseRotationStep(
                        1,
                        "Fase 1",
                        Firaga,
                        IsFinalPhase: false,
                        StopHere: true,
                        VarMutations: new[] { new AiPhaseVarMutation(0, 1, 1) },
                        TargetRecipe: AiTargetRecipe.Literal(FrontlineChars),
                        TriggerKind: AiPhaseTriggerKind.OnTurn),
                    new AiPhaseRotationStep(
                        2,
                        "Fase final",
                        Firaga,
                        IsFinalPhase: true,
                        StopHere: true,
                        VarMutations: new[] { new AiPhaseVarMutation(0, 0, 0, SetInsteadOfAdd: true) },
                        TargetRecipe: AiTargetRecipe.Literal(FrontlineChars)),
                },
                FinalLimit: 1,
                WorkerIndex: worker.Index,
                EntrypointIndex: entrypoint,
                DefaultTargetRecipe: AiTargetRecipe.Literal(FrontlineChars));
        }

        static Dictionary<string, object> ValidEdits(string fixtureName)
        {
            byte[] monster = MonsterFixtureBytes(fixtureName);
            AiPhaseRotationRecipe recipe = BuildValidRecipe(monster);
            return new Dictionary<string, object>
            {
                ["Recipe"] = recipe,
                [AtelPhaseRotationAdapter.WorkerIndexField] = recipe.WorkerIndex,
                [AtelPhaseRotationAdapter.MonsterBinHashField] = Sha256Of(monster),
            };
        }

        // --- (a) ValidateEdits rejects fields outside the allowlist -----------------------------

        [Fact]
        public void ValidateEdits_RejectsFieldOutsideAllowlist()
        {
            Dictionary<string, object> edits = ValidEdits("m001.bin");
            edits["Operand"] = (ushort)1;

            IReadOnlyList<string> errors = Adapter.ValidateEdits(edits);

            Assert.Contains(errors, e => e.Contains("Operand", StringComparison.Ordinal)
                                         && e.Contains("not editable", StringComparison.OrdinalIgnoreCase));
        }

        // --- (b) ValidateEdits requires the worker target field --------------------------------

        [Fact]
        public void ValidateEdits_RequiresWorkerIndex()
        {
            byte[] monster = MonsterFixtureBytes("m001.bin");
            var edits = new Dictionary<string, object>
            {
                ["Recipe"] = BuildValidRecipe(monster),
            };

            IReadOnlyList<string> errors = Adapter.ValidateEdits(edits);

            Assert.Contains(errors, e => e.Contains(AtelPhaseRotationAdapter.WorkerIndexField, StringComparison.Ordinal)
                                         && e.Contains("required", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void ValidateEdits_ValidEdits_ReturnNoErrors()
        {
            Assert.Empty(Adapter.ValidateEdits(ValidEdits("m001.bin")));
        }

        [Fact]
        public void ValidateEdits_RejectsBattleStartTrigger()
        {
            byte[] monster = MonsterFixtureBytes("m001.bin");
            AiPhaseRotationRecipe recipe = BuildValidRecipe(monster);
            var steps = recipe.Steps.ToList();
            steps[0] = steps[0] with { TriggerKind = AiPhaseTriggerKind.BattleStart };
            recipe = recipe with { Steps = steps };

            var edits = new Dictionary<string, object>
            {
                ["Recipe"] = recipe,
                [AtelPhaseRotationAdapter.WorkerIndexField] = recipe.WorkerIndex,
            };

            IReadOnlyList<string> errors = Adapter.ValidateEdits(edits);

            Assert.Contains(errors, e => e.Contains("BattleStart", StringComparison.Ordinal));
        }

        // --- (c) StageAsync with the correct monsterBinHash → success and staging exists ---------

        [Fact]
        public async Task StageAsync_WithCorrectHash_SucceedsAndStages()
        {
            string source = TempSource("m001.bin");
            string stagingDir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "AtelPhaseRotationAdapterTests_" + Guid.NewGuid().ToString("N"));
            string stagingPath = Path.Combine(stagingDir, "staged.bin");
            byte[] original = MonsterFixtureBytes("m001.bin");

            string predictedAfterHash = await Adapter.StageAsync(source, stagingPath, ValidEdits("m001.bin"));

            Assert.Equal(64, predictedAfterHash.Length);
            Assert.True(File.Exists(stagingPath), "staging must exist after a successful stage");

            byte[] staged = File.ReadAllBytes(stagingPath);
            Assert.Equal(predictedAfterHash, Sha256Of(staged));
            Assert.NotEqual(Sha256Of(original), predictedAfterHash);

            // The staged bin must re-slice to a script that walks closed (structural integrity).
            byte[]? stagedAi = AiScript_File.SliceAiFileFromMonster(staged);
            Assert.NotNull(stagedAi);
            AiScriptFile stagedScript = AiScript_File.Read(stagedAi!);
            Assert.True(stagedScript.CodeWalkClosedExactly);
            Assert.True(stagedScript.HasScript);
            Assert.Empty(stagedScript.UnknownOpcodes);
        }

        // --- (d) StageAsync with the wrong hash → throws before touching staging -----------------

        [Fact]
        public async Task StageAsync_WithWrongHash_ThrowsAndWritesNothing()
        {
            string source = TempSource("m001.bin");
            string stagingDir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "AtelPhaseRotationAdapterTests_" + Guid.NewGuid().ToString("N"));
            string stagingPath = Path.Combine(stagingDir, "staged.bin");

            Dictionary<string, object> edits = ValidEdits("m001.bin");
            edits[AtelPhaseRotationAdapter.MonsterBinHashField] =
                "0000000000000000000000000000000000000000000000000000000000000000";

            InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Adapter.StageAsync(source, stagingPath, edits));

            Assert.Contains("hash mismatch", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(stagingPath), "staging must not exist when the precondition fails");
        }

        // --- (e) Pre-validation: monster bin without an AiFile → throws -------------------------

        [Fact]
        public async Task StageAsync_MonsterWithoutAiFile_ThrowsAndWritesNothing()
        {
            // Random bytes with the header pointers zeroed (aiPtr=0) → no AI partition, deterministically.
            byte[] random = new byte[128];
            Random.Shared.NextBytes(random);
            random[4] = random[5] = random[6] = random[7] = 0; // AiFilePointer = 0
            random[8] = random[9] = random[10] = random[11] = 0; // WorkerFilePointer = 0

            string dir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "AtelPhaseRotationAdapterTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string source = Path.Combine(dir, "no_ai.bin");
            File.WriteAllBytes(source, random);
            string stagingPath = Path.Combine(dir, "staged.bin");

            // O hash do bin aleatório precisa conferir para a validação chegar na partição AI.
            Dictionary<string, object> edits = ValidEdits("m001.bin");
            edits[AtelPhaseRotationAdapter.MonsterBinHashField] = Sha256Of(random);

            InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Adapter.StageAsync(source, stagingPath, edits));
            Assert.Contains("no AI partition", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(stagingPath), "staging must not exist when the bin has no AiFile");
        }

        // --- (f) ComputeDryRunDiff before == after → all layers empty ----------------------------

        [Fact]
        public void ComputeDryRunDiff_BeforeEqualsAfter_AllLayersEmpty()
        {
            byte[] monster = MonsterFixtureBytes("m001.bin");

            ThreeLayerDiff? diff = AtelPhaseRotationAdapter.ComputeDryRunDiff(monster, monster);

            Assert.NotNull(diff);
            Assert.True(diff!.IsEmpty);
            Assert.Empty(diff.ByteDiff);
            Assert.Empty(diff.DisassemblyDiff);
            Assert.Empty(diff.SemanticDiff);
        }

        // --- (g) The applied recipe produces a diff (bytes change) -------------------------------

        [Fact]
        public async Task ComputeDryRunDiff_RecipeApplied_ShowsChanges()
        {
            string source = TempSource("m001.bin");
            string stagingPath = Path.Combine(
                FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot + "/work",
                "AtelPhaseRotationAdapterTests_" + Guid.NewGuid().ToString("N"),
                "staged.bin");

            await Adapter.StageAsync(source, stagingPath, ValidEdits("m001.bin"));

            ThreeLayerDiff? diff = AtelPhaseRotationAdapter.ComputeDryRunDiff(
                MonsterFixtureBytes("m001.bin"),
                File.ReadAllBytes(stagingPath));

            Assert.NotNull(diff);
            Assert.False(diff!.IsEmpty, "applying the phase-rotation recipe must change the script bytes");
            Assert.NotEmpty(diff.ByteDiff);
            Assert.NotEmpty(diff.DisassemblyDiff);
        }

        // --- honest negatives ----------------------------------------------------------------

        [Fact]
        public async Task StageAsync_RecipeNeedsVariables_m000_Throws()
        {
            // m000 has no variable table → the Apply (the authority) rejects the recipe; nothing staged.
            string source = TempSource("m000.bin");
            string stagingPath = Path.Combine(
                FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot + "/work",
                "AtelPhaseRotationAdapterTests_" + Guid.NewGuid().ToString("N"),
                "staged.bin");

            InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Adapter.StageAsync(source, stagingPath, ValidEdits("m000.bin")));

            Assert.Contains("recipe rejected", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(stagingPath), "staging must not exist when the recipe is rejected");
        }

        [Fact]
        public void ComputeDryRunDiff_BinWithoutAiFile_ReturnsNull()
        {
            byte[] noAi = new byte[0x40]; // all-zero header → aiPtr=0 → no AI partition
            byte[] monster = MonsterFixtureBytes("m001.bin");

            Assert.Null(AtelPhaseRotationAdapter.ComputeDryRunDiff(noAi, monster));
        }
    }
}
