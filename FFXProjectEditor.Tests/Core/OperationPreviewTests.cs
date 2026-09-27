using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.Core;
using FFXProjectEditor.Core.Writers;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.Core
{
    /// <summary>
    /// OperationPreview — o fluxo P2 consumível (simular → revisar → aplicar → receipt):
    ///   (a) BuildFromFiles com AiFile real (Fixtures/Monster/m001.bin via SliceAiFileFromMonster)
    ///       antes/depois de um edit byte-local de operando → 3 camadas populadas no summary;
    ///   (b) before == after → camadas vazias;
    ///   (c) BuildFromPlan null-safe (provider null / retorno null / exceção) → nota
    ///       "preview indisponível" sem crash;
    ///   (c.2) BuildFromPlan com provider real (AtelScriptAdapter.ComputePreviewDiff) → diff exposto;
    ///   (d) END-TO-END: EditSession(ability-command) → OperationPlan → OperationExecutorV2 →
    ///       OperationResult.Success + ReceiptBuilder.BuildReceipt(FilesWritten == 1) + o output
    ///       re-lido contém o edit (CostMp == 7) — o fluxo completo provado ponta a ponta.
    /// </summary>
    public class OperationPreviewTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _sourceDir;
        private readonly string _outputDir;

        public OperationPreviewTests()
        {
            var id = Guid.NewGuid().ToString("N")[..8];
            _tempDir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "opv_" + id);
            // Source e output em diretórios IRMÃOS: PathGuard.ValidateOutputPath (usado pelo
            // OperationExecutorV2) exige separação — output não pode estar dentro do source.
            _sourceDir = Path.Combine(_tempDir, "src");
            _outputDir = Path.Combine(_tempDir, "out");
            Directory.CreateDirectory(_sourceDir);
            Directory.CreateDirectory(_outputDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        static byte[] MonsterFixtureBytes(string name) =>
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Monster", name));

        static string FixturePath(string fixtureName) =>
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "Battle", fixtureName);


        // --- (a) BuildFromFiles com AiFile real: edit byte-local de operando -----------------

        [Fact]
        public void BuildFromFiles_RealAiFileOperandEdit_AllThreeLayersPopulated()
        {
            using var language = TestUiCultureScope.English();
            byte[]? ai = AiScript_File.SliceAiFileFromMonster(MonsterFixtureBytes("m001.bin"));
            Assert.NotNull(ai);

            string beforePath = Path.Combine(_tempDir, "m001_ai_before.bin");
            string afterPath = Path.Combine(_tempDir, "m001_ai_after.bin");
            File.WriteAllBytes(beforePath, ai!);

            // m001: a primeira PUSHII (AE 9E 00) carrega operando 0x009E (unknown command);
            // 0x3049 (Firaga) é byte-local (3 bytes, mesmo comprimento) e muda os bytes crus,
            // o texto de disassembly E o Meaning/Evidence semântico — as 3 camadas devem reportar.
            AiScriptFile script = AiScript_File.Read(ai!);
            AiInstruction pushii = script.Instructions.First(i => i.Opcode == 0xAE);
            Assert.Equal((ushort)0x009E, pushii.Operand);
            pushii.Operand = 0x3049;
            File.WriteAllBytes(afterPath, AiScript_File.Write(script));

            FilePreviewSummary summary = OperationPreview.BuildFromFiles(beforePath, afterPath);

            Assert.NotEmpty(summary.ByteDiffLines);
            Assert.Contains(summary.ByteDiffLines, line => line.Contains("9E->49", StringComparison.Ordinal));
            Assert.NotEmpty(summary.DisassemblyDiffLines);
            Assert.Contains(summary.DisassemblyDiffLines, line => line.Contains("[Modified]", StringComparison.Ordinal));
            Assert.NotEmpty(summary.SemanticDiffLines);

            Assert.Equal(OperationExecutorV2.ComputeSha256(beforePath), summary.BeforeHash);
            Assert.Equal(OperationExecutorV2.ComputeSha256(afterPath), summary.PredictedAfterHash);
            Assert.NotEqual(summary.BeforeHash, summary.PredictedAfterHash);
        }

        // --- (b) before == after → camadas vazias ---------------------------------------------

        [Fact]
        public void BuildFromFiles_BeforeEqualsAfter_AllLayersEmpty()
        {
            using var language = TestUiCultureScope.English();
            byte[]? ai = AiScript_File.SliceAiFileFromMonster(MonsterFixtureBytes("m001.bin"));
            Assert.NotNull(ai);
            string path = Path.Combine(_tempDir, "m001_ai_same.bin");
            File.WriteAllBytes(path, ai!);

            FilePreviewSummary summary = OperationPreview.BuildFromFiles(path, path);

            Assert.Empty(summary.ByteDiffLines);
            Assert.Empty(summary.DisassemblyDiffLines);
            Assert.Empty(summary.SemanticDiffLines);
            Assert.Equal(summary.BeforeHash, summary.PredictedAfterHash);
            Assert.Equal("No changes (before == after)", summary.HumanSummary);
        }

        // --- (c) BuildFromPlan null-safe ------------------------------------------------------

        [Fact]
        public void BuildFromPlan_NullOrFailingProvider_UnavailableNoteWithoutCrash()
        {
            using var language = TestUiCultureScope.English();
            string source = Path.Combine(_sourceDir, "command.bin");
            File.Copy(FixturePath("command.bin"), source);
            OperationPlan plan = BuildSingleOpPlan(source);

            // provider null
            OperationPreview fromNullProvider = OperationPreview.BuildFromPlan(plan, null);
            AssertPreviewUnavailable(fromNullProvider);

            // provider retorna null (ex.: staged ainda não existe)
            OperationPreview fromNullDiff = OperationPreview.BuildFromPlan(plan, (_, _) => null);
            AssertPreviewUnavailable(fromNullDiff);

            // provider lança (ex.: domínio sem codec de 3 camadas tentando ler o staged)
            OperationPreview fromThrowing = OperationPreview.BuildFromPlan(plan, (_, _) =>
                throw new FileNotFoundException("staged não existe"));
            AssertPreviewUnavailable(fromThrowing);
        }

        static void AssertPreviewUnavailable(OperationPreview preview)
        {
            FilePreviewSummary summary = Assert.Single(preview.FilePreviewSummaries);
            Assert.Contains(OperationPreview.PreviewUnavailableNote, summary.HumanSummary, StringComparison.Ordinal);
            Assert.Empty(summary.ByteDiffLines);
            Assert.Empty(summary.DisassemblyDiffLines);
            Assert.Empty(summary.SemanticDiffLines);
        }


        // --- (c.2) BuildFromPlan com provider real → diff exposto por arquivo -----------------

        [Fact]
        public void BuildFromPlan_WithRealProvider_ExposesThreeLayersPerFile()
        {
            using var language = TestUiCultureScope.English();
            byte[]? ai = AiScript_File.SliceAiFileFromMonster(MonsterFixtureBytes("m001.bin"));
            Assert.NotNull(ai);
            string sourcePath = Path.Combine(_sourceDir, "ai_src.bin");
            string stagedPath = Path.Combine(_sourceDir, "file-1"); // convenção BuildFromPlan: StagingRoot/op.Id
            File.WriteAllBytes(sourcePath, ai!);

            // m001: PUSHII 0x009E (desconhecido) -> 0x3049 (Firaga) — muda bytes+disassembly+SEMÂNTICA
            // (padrão AtelScriptAdapterTests). CALLPOPA 0x5F->0x60 não muda Meaning — semântica fica vazia.
            AiScriptFile script = AiScript_File.Read(ai!);
            AiInstruction pushii = script.Instructions.First(i => i.Opcode == 0xAE);
            pushii.Operand = 0x3049;
            File.WriteAllBytes(stagedPath, AiScript_File.Write(script));

            var plan = new OperationPlan
            {
                OperationId = "op-diff",
                DisplayName = "plan com provider real",
                CreatedAt = DateTimeOffset.UtcNow,
                SourceRoot = _sourceDir,
                OutputRoot = _outputDir,
                StagingRoot = _sourceDir, // staged convencionado em StagingRoot/op.Id (igual ao executor)
                BackupRoot = _sourceDir,
                Operations = new[]
                {
                    new FileOperation
                    {
                        Id = "file-1",
                        Kind = FileOperationKind.Patch,
                        SourceRelativePath = "ai_src.bin",
                        OutputRelativePath = "ai_out.bin",
                        BeforeHash = OperationExecutorV2.ComputeSha256(sourcePath),
                        PredictedAfterHash = "pending-stage",
                        EstimatedBytes = 0,
                        Description = "op atel",
                        Diff = new FileDiffSummary
                        {
                            FieldsChanged = 0,
                            ChangedFieldNames = Array.Empty<string>(),
                            HumanSummary = string.Empty
                        },
                        Risk = RiskLevel.Safe,
                        Edits = new Dictionary<string, object>(),
                    }
                },
                Preconditions = Array.Empty<string>(),
                OwnerCapabilityId = "atel-script",
            };

            OperationPreview preview = OperationPreview.BuildFromPlan(plan, (src, staged) =>
                File.Exists(src) && File.Exists(staged)
                    ? AtelScriptAdapter.ComputePreviewDiff(File.ReadAllBytes(src), File.ReadAllBytes(staged))
                    : null);

            Assert.Equal("op-diff", preview.OperationId);
            Assert.Equal(1, preview.FileCount);
            FilePreviewSummary summary = Assert.Single(preview.FilePreviewSummaries);
            Assert.Equal("file-1", summary.FileId);
            Assert.Equal("ai_src.bin", summary.SourceRelativePath);

            Assert.NotEmpty(summary.ByteDiffLines);
            Assert.NotEmpty(summary.DisassemblyDiffLines);
            Assert.NotEmpty(summary.SemanticDiffLines);
            Assert.DoesNotContain(OperationPreview.PreviewUnavailableNote, summary.HumanSummary, StringComparison.Ordinal);
        }

        // --- (d) END-TO-END: simular → aplicar → receipt --------------------------------------

        [Fact]
        public void EndToEnd_EditSession_Executor_Receipt_Flow()
        {
            using var language = TestUiCultureScope.English();
            string source = Path.Combine(_sourceDir, "command.bin");
            string output = Path.Combine(_outputDir, "command.bin");
            File.Copy(FixturePath("command.bin"), source);

            var catalog = new WriterAdapterCatalog(); // o ctor registra o AbilityCommandAdapter

            // 1) SIMULAR: EditSession → OperationPlan (+ preview consumível)
            var plan = new EditSession(catalog)
                .ForCapability("ability-command")
                .WithSource(source)
                .WithOutput(output)
                .WithDisplayName("e2e: CostMp 7 no comando 0")
                .WithEdit(AbilityCommandAdapter.CommandIndexField, 0)
                .WithEdit("CostMp", (byte)7)
                .Build();

            OperationPreview preview = OperationPreview.BuildFromPlan(plan, null);
            Assert.Equal(plan.OperationId, preview.OperationId);
            Assert.Equal(1, preview.FileCount);
            // command.bin não tem codec de 3 camadas (diff é domínio ATEL) → nota, sem crash.
            Assert.Contains(OperationPreview.PreviewUnavailableNote,
                preview.FilePreviewSummaries[0].HumanSummary, StringComparison.Ordinal);

            // 2) APLICAR: OperationExecutorV2 (stage+verify → backup+promote)
            var result = new OperationExecutorV2(catalog).ExecuteAsync(plan).GetAwaiter().GetResult();
            Assert.True(result.Success, result.Validations.Count > 0
                ? string.Join(" | ", result.Validations.Select(v => v.Message).ToArray())
                : result.ErrorMessage);
            Assert.Equal(1, result.FilesWritten);

            // 3) RECEIPT
            var receipt = ReceiptBuilder.BuildReceipt(plan, result);
            Assert.True(receipt.Success);
            Assert.Equal(1, receipt.FilesWritten);
            Assert.Equal(result.ReceiptId, receipt.ReceiptId);

            // 4) O OUTPUT contém o edit (re-leitura via codec real)
            Assert.True(File.Exists(output), "output deve existir após o promote");
            var reread = Ability_Command.ReadList(File.ReadAllBytes(output), hasExtraInfo: true);
            Assert.Equal((byte)7, reread[0].CostMp);
        }

        // --- Helpers --------------------------------------------------------------------------

        OperationPlan BuildSingleOpPlan(string source)
        {
            return new OperationPlan
            {
                OperationId = Guid.NewGuid().ToString("N"),
                DisplayName = "single-op",
                CreatedAt = DateTimeOffset.UtcNow,
                SourceRoot = _sourceDir,
                OutputRoot = _outputDir,
                StagingRoot = _sourceDir,
                BackupRoot = _sourceDir,
                Operations = new[]
                {
                    new FileOperation
                    {
                        Id = "file-1",
                        Kind = FileOperationKind.Patch,
                        SourceRelativePath = Path.GetFileName(source),
                        OutputRelativePath = Path.GetFileName(source),
                        BeforeHash = OperationExecutorV2.ComputeSha256(source),
                        PredictedAfterHash = "pending-stage",
                        EstimatedBytes = new FileInfo(source).Length,
                        Description = "op preview",
                        Diff = new FileDiffSummary
                        {
                            FieldsChanged = 0,
                            ChangedFieldNames = Array.Empty<string>(),
                            HumanSummary = string.Empty
                        },
                        Risk = RiskLevel.Moderate,
                        Edits = new Dictionary<string, object>(),
                    }
                },
                Preconditions = Array.Empty<string>(),
                OwnerCapabilityId = "ability-command",
            };
        }
    }
}

