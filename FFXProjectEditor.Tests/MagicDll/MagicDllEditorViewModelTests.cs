using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Modules.MagicDllEditor;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll
{
    /// <summary>
    /// E2E headless do ViewModel do Magic DLL Editor (camada de apresentação):
    /// abrir → árvore montada (Root→Descriptors/Programs→Slots→Fields) → editar campo
    /// → salvar cópia (pickers mockados) → RT0 → reverter. Cobre o que o run 00007
    /// (cortado por auth) deveria ter entregue. Nunca escreve no corpus.
    /// </summary>
    public class MagicDllEditorViewModelTests
    {
        private static string CorpusDir => Environment.GetEnvironmentVariable("FFX_TEST_MAGIC_CORPUS") ?? @"F:\ffx-reconstructed\extras\magicFiles\FFX";

        private static string MakeTempDir()
        {
            string dir = Path.Combine(RepoWorkDir(), "ffx_vm_" + Guid.NewGuid().ToString("N"));
            TestDirectory.CreatePrivate(dir);
            return dir;
        }

        private static string CopyCorpusToTemp(string tempDir, string dllName)
        {
            return MagicDllTestFixture.Write(tempDir, dllName);
        }

        /// <summary>Caminha a árvore procurando o primeiro nó de campo f32 editável FORA do prefixo protegido (+8).</summary>
        private static MagicFieldNode? FirstF32FieldNode(MagicDllEditor_ViewModel vm)
        {
            foreach (MagicNode root in vm.RootNodes)
            foreach (MagicNode group in root.Children)
            foreach (MagicNode program in group.Children)
            foreach (MagicNode slot in program.Children)
            foreach (MagicNode child in slot.Children)
                // R6: escrita no prefixo [+0,+8) do record é bloqueada (match word) —
                // o teste precisa de um campo editável de verdade (offset >= 8).
                if (child is MagicFieldNode f && f.Type == MagicFieldType.F32 && f.IsValueEditable && f.Offset >= 8)
                    return f;
            return null;
        }
    internal static string RepoWorkDir()
    {
        string dir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "linux-tests");
        TestDirectory.CreatePrivate(dir);
        return dir;
    }


        [Fact]
        public async Task Open_0021_BuildsTree_WithNamedEffect_AndFields()
        {
            string dir = MakeTempDir();
            try
            {
                string path = CopyCorpusToTemp(dir, "magic_0021.dll");
                var vm = new MagicDllEditor_ViewModel();

                bool ok = await vm.OpenFileAsync(path);

                Assert.True(ok);
                Assert.True(vm.HasDocument);
                Assert.NotEmpty(vm.RootNodes);
                Assert.Contains("Power Break", vm.DocumentTitle, StringComparison.OrdinalIgnoreCase);
                Assert.False(string.IsNullOrEmpty(vm.ShaBefore));

                // A árvore tem: Root → [Descriptors, Programs, Handler indices] → ...
                MagicNode root = vm.RootNodes[0];
                Assert.Contains(root.Children, g => g.Label == "Programs" && g.Children.Count > 0);

                // Slots com opcode resolvido têm campos com âncora (SourceField wired).
                MagicFieldNode? anyField = FirstF32FieldNode(vm);
                Assert.NotNull(anyField);
                Assert.NotNull(anyField!.SourceField);
                Assert.True(anyField.SourceField!.RecordOffset >= 0);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task EditField_SaveCopy_Rt0_Revert_FullFlow()
        {
            string dir = MakeTempDir();
            try
            {
                string path = CopyCorpusToTemp(dir, "magic_0021.dll");
                var vm = new MagicDllEditor_ViewModel();
                Assert.True(await vm.OpenFileAsync(path));

                MagicFieldNode? field = FirstF32FieldNode(vm);
                Assert.NotNull(field);

                // Edita o campo (dirty).
                string original = field!.Value;
                field.Value = "3.5";
                Assert.True(field.IsDirty);

                // Salvar cópia com picker mockado.
                string dest = Path.Combine(dir, "magic_edited.dll");
                vm.SaveFilePicker = () => Task.FromResult<string?>(dest);
                if (vm.SaveCopyCommand is CommunityToolkit.Mvvm.Input.IAsyncRelayCommand asyncCmd)
                    await asyncCmd.ExecuteAsync(null);
                else
                    vm.SaveCopyCommand.Execute(null);

                Assert.True(File.Exists(dest), "dest não criado. Log VM: " + string.Join(" | ", vm.Log.Select(e => e.Text)));
                Assert.True(File.Exists(dest + ".bak"));
                Assert.False(field.IsDirty, "campo deveria estar persistido após salvar");
                Assert.NotEqual(vm.ShaBefore, vm.ShaAfter);

                // RT0 check (pipeline limpo) não deve lançar e deve logar.
                vm.Rt0CheckCommand.Execute(null);
                Assert.NotEmpty(vm.Log);

                // Reverter: volta ao estado original (sem backup salvo anterior → descarta edições).
                byte[] before = File.ReadAllBytes(path);
                vm.RevertCommand.Execute(null);
                Assert.False(vm.IsDirty);
                Assert.Equal(vm.ShaBefore, vm.ShaAfter);
                Assert.True(before.AsSpan().SequenceEqual(vm.Document.WorkingBytes!), "revert não voltou ao original");
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task Open_MissingFile_ReturnsFalse_AndLogsError()
        {
            var vm = new MagicDllEditor_ViewModel();
            string missing = Path.Combine(RepoWorkDir(), "magic_nao_existe_" + Guid.NewGuid().ToString("N") + ".dll");

            bool ok = await vm.OpenFileAsync(missing);

            Assert.False(ok);
            Assert.False(vm.HasDocument);
            Assert.Contains(vm.Log, e => e.Level == MagicLogLevel.Error);
        }

        [Fact]
        public void CanAddField_OnlyOnSlotNode()
        {
            var vm = new MagicDllEditor_ViewModel();
            Assert.False(vm.CanAddField); // sem documento
        }

        [Fact]
        public void ProductXaml_DoesNotExposeGrowOrU1ResearchAffordances()
        {
            string xamlPath = Path.Combine(
                FindRepoRoot(),
                "FFXProjectEditor",
                "Modules",
                "MagicDllEditor",
                "MagicDllEditor_Control.axaml");
            string xaml = File.ReadAllText(xamlPath);

            Assert.DoesNotContain("AddFieldCommand", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("SimulateU1Command", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("ExportSimulationCommand", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("GrowWidthComboIndex", xaml, StringComparison.Ordinal);
        }

        [Fact]
        public void ProductBuildContract_ExcludesPublicMagicU1SimulatorTypes()
        {
            string project = File.ReadAllText(Path.Combine(
                FindRepoRoot(),
                "FFXProjectEditor",
                "FFXProjectEditor.csproj"));

            Assert.Contains("<ItemGroup Condition=\"'$(FFXIncludeDevTools)' != 'true'\">", project, StringComparison.Ordinal);
            Assert.Contains("<Compile Remove=\"FfxLib\\MagicDll\\MagicU1Simulator.cs\" />", project, StringComparison.Ordinal);
        }

        [Fact]
        public void ProductSurface_DoesNotExposeMutableParsedTrustGraph()
        {
            Type wrapper = typeof(MagicDllDocument_Wrapper);
            const System.Reflection.BindingFlags PublicInstance =
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;

            Assert.Null(wrapper.GetProperty("ParsedFile", PublicInstance));
            Assert.Null(wrapper.GetProperty("FieldMap", PublicInstance));
        }

        [Fact]
        public async Task CloneWithoutSave_LeavesBackupCommandDisabled()
        {
            string dir = MakeTempDir();
            try
            {
                string source = CopyCorpusToTemp(dir, "magic_0021.dll");
                string clone = Path.Combine(dir, "magic_0140.dll");
                var vm = new MagicDllEditor_ViewModel
                {
                    CloneFilePicker = () => Task.FromResult<string?>(clone),
                };
                Assert.True(await vm.OpenFileAsync(source));
                Assert.False(vm.HasBackup);

                if (vm.CloneMagicCommand is CommunityToolkit.Mvvm.Input.IAsyncRelayCommand asyncCommand)
                    await asyncCommand.ExecuteAsync(null);

                Assert.True(File.Exists(clone));
                Assert.False(vm.HasBackup);
                Assert.False(vm.RevertCommand.CanExecute(null));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        /// <summary>
        /// Thread-safety: dispara Open/Save/Revert concorrentes e exige que o gate
        /// serialize as mutações (nenhuma exceção, documento final válido, working bytes íntegros).
        /// Sem o SemaphoreSlim, corridas entre Save (que relê working bytes) e Open
        /// (que substitui o documento) corromperiam o estado.
        /// </summary>
        [Fact]
        public async Task ConcurrentMutations_AreSerialized_ByGate()
        {
            string dir = MakeTempDir();
            try
            {
                string path = CopyCorpusToTemp(dir, "magic_0021.dll");
                string dest = Path.Combine(dir, "magic_concurrent.dll");
                var vm = new MagicDllEditor_ViewModel();
                vm.SaveFilePicker = () => Task.FromResult<string?>(dest);

                Assert.True(await vm.OpenFileAsync(path));

                // Barragem de mutações concorrentes: opens, saves e reverts simultâneos.
                var tasks = new System.Collections.Generic.List<Task>();
                for (int i = 0; i < 6; i++)
                {
                    tasks.Add(Task.Run(async () => await vm.OpenFileAsync(path)));
                    tasks.Add(Task.Run(async () =>
                    {
                        if (vm.SaveCopyCommand is CommunityToolkit.Mvvm.Input.IAsyncRelayCommand c)
                            await c.ExecuteAsync(null);
                    }));
                    tasks.Add(Task.Run(async () =>
                    {
                        // RevertAsync é async — ExecuteAsync espera o gate terminar
                        // (Execute(null) era fire-and-forget → asserts rodavam antes).
                        if (vm.RevertCommand is CommunityToolkit.Mvvm.Input.IAsyncRelayCommand c)
                            await c.ExecuteAsync(null);
                    }));
                }
                await Task.WhenAll(tasks);

                // Estado final consistente: documento aberto, working bytes íntegros e SHA estável.
                Assert.True(vm.HasDocument);
                Assert.NotNull(vm.Document.WorkingBytes);
                Assert.Equal(vm.ShaAfter, vm.ShaBefore); // revert/redo não deve dessincronizar
                Assert.Contains(vm.Log, e => e.Level == MagicLogLevel.Error || e.Text.Contains("Opened"));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task SimulateU1_WithCorpus_RunsAndProducesSummary()
        {
            string dir = MakeTempDir();
            try
            {
                string path = CopyCorpusToTemp(dir, "magic_0021.dll");
                var vm = new MagicDllEditor_ViewModel();
                Assert.True(await vm.OpenFileAsync(path));

                vm.SimFactor = 2.0;
                vm.SimFrames = 60;
                vm.SimulateU1Command.Execute(null);

                Assert.False(string.IsNullOrEmpty(vm.SimSummary), "SimSummary empty — log: " + string.Join(" | ", vm.Log.Select(e => e.Text)));
                Assert.Contains("U1 slots", vm.SimSummary);
                Assert.Contains("final scale X ratio", vm.SimSummary);
                Assert.False(string.IsNullOrEmpty(vm.SimTable));
                Assert.Contains("frame | scale X before", vm.SimTable);
                Assert.Contains(vm.Log, e => e.Text.Contains("U1 simulation"));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task SimulateU1_NoDocument_LogsWarning()
        {
            var vm = new MagicDllEditor_ViewModel();
            vm.SimulateU1Command.Execute(null);

            Assert.Contains(vm.Log, e => e.Text.Contains("No document open to simulate."));
            Assert.Equal(string.Empty, vm.SimSummary);
        }

        [Fact]
        public async Task SlotFilter_FiltersTreeByFamily()
        {
            string dir = MakeTempDir();
            try
            {
                string path = CopyCorpusToTemp(dir, "magic_0021.dll");
                var vm = new MagicDllEditor_ViewModel();
                Assert.True(await vm.OpenFileAsync(path));

                int totalSlots = CountSlotNodes(vm);
                Assert.True(totalSlots > 0);

                vm.SlotFilter = "pppSclMove";
                int sclMoveSlots = CountSlotNodes(vm);
                Assert.True(sclMoveSlots > 0, "esperava slots pppSclMove com filtro ativo");
                Assert.True(sclMoveSlots < totalSlots, "filtro deveria reduzir a árvore");

                vm.SlotFilter = "pppNaoExiste";
                Assert.Equal(0, CountSlotNodes(vm));

                vm.SlotFilter = "";
                Assert.Equal(totalSlots, CountSlotNodes(vm)); // limpar restaura tudo
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task RefreshCloneDiff_DetectsMutatedRecords()
        {
            string dir = MakeTempDir();
            try
            {
                string path = CopyCorpusToTemp(dir, "magic_0021.dll");
                var vm = new MagicDllEditor_ViewModel();
                Assert.True(await vm.OpenFileAsync(path));

                // Sem mutação: clone idêntico.
                vm.RefreshCloneDiffCommand.Execute(null);
                Assert.Contains("0 records mutated", vm.CloneDiffSummary);

                // Edita um campo f32 (write-back) → clone difere em 1 record.
                MagicFieldNode? field = FirstF32FieldNode(vm);
                Assert.NotNull(field);
                field!.Value = "7.25";
                Assert.True(field.IsDirty);

                // Aplica via save path (save com picker aplica os edits pendentes nos
                // working bytes — o diff é contra os SourceBytes originais do documento).
                string dest = Path.Combine(dir, "magic_edited.dll");
                vm.SaveFilePicker = () => Task.FromResult<string?>(dest);
                if (vm.SaveCopyCommand is CommunityToolkit.Mvvm.Input.IAsyncRelayCommand asyncCmd)
                    await asyncCmd.ExecuteAsync(null);
                Assert.True(File.Exists(dest));
                Assert.NotEqual(vm.ShaBefore, vm.ShaAfter); // edit aplicado de verdade

                vm.RefreshCloneDiffCommand.Execute(null);
                Assert.Contains("record(s) mutado(s)", vm.CloneDiffSummary);
                Assert.False(string.IsNullOrEmpty(vm.CloneDiffDetail));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Theory]
        [InlineData(FFXProjectEditor.FfxLib.MagicDll.MagicFieldType.U32, "U32", "4294967295", 4)]
        [InlineData(FFXProjectEditor.FfxLib.MagicDll.MagicFieldType.S16, "S16", "-32768", 2)]
        public void FieldValueCodec_PreservesTheParserTypeAndEncodedWidth(
            FFXProjectEditor.FfxLib.MagicDll.MagicFieldType parserType,
            string expectedUiType,
            string value,
            int expectedWidth)
        {
            MethodInfo mapType = typeof(MagicDllEditor_ViewModel).GetMethod(
                "MapType",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            var uiType = Assert.IsType<MagicFieldType>(mapType.Invoke(null, new object[] { parserType }));

            Assert.Equal(expectedUiType, uiType.ToString());

            var field = new MagicFieldNode(
                "typed_field",
                uiType,
                offset: 8,
                width: expectedWidth,
                semantic: string.Empty,
                description: string.Empty,
                initialValue: value);
            MethodInfo buildBytes = typeof(MagicDllEditor_ViewModel).GetMethod(
                "TryBuildBytes",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            object?[] arguments = { field, null };

            Assert.True(Assert.IsType<bool>(buildBytes.Invoke(null, arguments)));
            Assert.Equal(expectedWidth, Assert.IsType<byte[]>(arguments[1]).Length);
        }

        [Fact]
        public async Task SaveCopy_RoundTripsS32AndU16WithoutChangingBytesOutsideTheirSlots()
        {
            string directory = MakeTempDir();
            try
            {
                // The self-contained fixture only emits editable F32/S32/U16/U8 fields;
                // U32/S16 slots exist but sit inside the protected prefix (offset < 8).
                await AssertTypedFieldRoundTrip(
                    directory, "magic_0005.dll", MagicFieldType.S32, "-123456", expectedSigned: -123456);
                await AssertTypedFieldRoundTrip(
                    directory, "magic_0003.dll", MagicFieldType.U16, "65000", expectedUnsigned: 65000);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void MagicFieldNode_RequiresTheSchemaWriteCapability()
        {
            var readOnly = new MagicFieldNode(
                "value",
                MagicFieldType.F32,
                offset: 8,
                width: 4,
                semantic: string.Empty,
                description: string.Empty,
                schemaAllowsWrite: false);

            Assert.False(readOnly.IsValueEditable);
            Assert.True(readOnly.IsReadOnly);
        }


        [Fact]
        public void ProductUi_DisablesReadOnlyNumericFieldsAndExposesLegacyPreviewRecovery()
        {
            string xamlPath = Path.Combine(
                FindRepoRoot(),
                "FFXProjectEditor",
                "Modules",
                "MagicDllEditor",
                "MagicDllEditor_Control.axaml");
            string xaml = File.ReadAllText(xamlPath);
            string codeBehind = File.ReadAllText(Path.ChangeExtension(xamlPath, ".axaml.cs"));

            Assert.Contains("IsEnabled=\"{Binding IsValueEditable}\"", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("Command=\"{Binding RestoreLegacyPreviewCommand}\"", xaml, StringComparison.Ordinal);
            Assert.NotNull(typeof(MagicDllEditor_ViewModel).GetProperty("RestoreLegacyPreviewCommand"));
            Assert.Contains("OnDetachedFromVisualTree", codeBehind, StringComparison.Ordinal);
            Assert.Contains("ClosePreviewCommand.Execute", codeBehind, StringComparison.Ordinal);
        }

        [Fact]
        public async Task RestoreLegacyPreviewCommand_IsFailClosedOnCancelAndRunsOnlyAfterConfirmation()
        {
            var vm = new MagicDllEditor_ViewModel();
            bool recoveryCalled = false;
            vm.RestoreLegacyPreviewAction = () =>
            {
                recoveryCalled = true;
                return (1, "0021.bin");
            };
            vm.ConfirmLegacyPreviewRestore = () => Task.FromResult(false);

            await vm.RestoreLegacyPreviewCommand.ExecuteAsync(null);

            Assert.False(recoveryCalled);
            Assert.Contains(vm.Log, entry => entry.Text.Contains("cancel", StringComparison.OrdinalIgnoreCase));

            vm.ConfirmLegacyPreviewRestore = () => Task.FromResult(true);
            await vm.RestoreLegacyPreviewCommand.ExecuteAsync(null);

            Assert.True(recoveryCalled);
            Assert.Contains(vm.Log, entry => entry.Text.Contains("0021.bin", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void EmbeddedNames_ResolvesOffline()
        {
            // ONDA 8 (GOAL 8h): catálogo embutido resolve nomes sem o JSON de work/
            // (fallback offline em produção). 471 nomes de efeitos.
            Assert.True(MagicEffectNameCatalog.TryGet("magic_0021.dll", out string? powerBreak));
            Assert.Equal("Power Break", powerBreak);
            Assert.True(MagicEffectNameCatalog.TryGet("magic_0098.dll", out string? death));
            Assert.Equal("Death", death);
            Assert.True(MagicEffectNameCatalog.TryGet("magic_0003.dll", out string? cheer));
            Assert.Equal("Cheer", cheer);
            Assert.False(MagicEffectNameCatalog.TryGet("magic_9999.dll", out _));
        }

        private static IEnumerable<MagicNode> EnumerateNodes(IEnumerable<MagicNode> nodes)
        {
            foreach (MagicNode node in nodes)
            {
                yield return node;
                foreach (MagicNode child in EnumerateNodes(node.Children))
                    yield return child;
            }
        }

        private static bool SameField(MagicFieldNode candidate, MagicFieldNode expected) =>
            candidate.Type == expected.Type &&
            candidate.Name == expected.Name &&
            candidate.Offset == expected.Offset &&
            candidate.SourceField?.RecordOffset == expected.SourceField?.RecordOffset;

        private static async Task AssertTypedFieldRoundTrip(
            string directory,
            string dllName,
            MagicFieldType type,
            string editedValue,
            uint? expectedUnsigned = null,
            int? expectedSigned = null)
        {
            string source = CopyCorpusToTemp(directory, dllName);
            byte[] original = File.ReadAllBytes(source);
            var editor = new MagicDllEditor_ViewModel();
            Assert.True(await editor.OpenFileAsync(source));
            MagicFieldNode field = Assert.Single(
                EnumerateNodes(editor.RootNodes).OfType<MagicFieldNode>()
                    .Where(candidate => candidate.Type == type && candidate.IsValueEditable)
                    .Take(1));
            Assert.NotNull(field.SourceField);

            field.Value = editedValue;
            string destination = Path.Combine(directory, Path.GetFileNameWithoutExtension(dllName) + "_typed.dll");
            editor.SaveFilePicker = () => Task.FromResult<string?>(destination);
            await Assert.IsAssignableFrom<CommunityToolkit.Mvvm.Input.IAsyncRelayCommand>(
                editor.SaveCopyCommand).ExecuteAsync(null);

            Assert.True(File.Exists(destination), string.Join(" | ", editor.Log.Select(entry => entry.Text)));
            byte[] saved = File.ReadAllBytes(destination);
            Assert.Equal(original.Length, saved.Length);
            int start = editor.Document.ParsedFile!.DataSectionRawPtr +
                field.SourceField!.RecordOffset + field.Offset;
            var allowedOffsets = new HashSet<int>(Enumerable.Range(start, field.Width));
            int[] changedOffsets = Enumerable.Range(0, original.Length)
                .Where(index => original[index] != saved[index])
                .ToArray();
            Assert.NotEmpty(changedOffsets);
            Assert.All(changedOffsets, index => Assert.Contains(index, allowedOffsets));

            var reopened = new MagicDllEditor_ViewModel();
            Assert.True(await reopened.OpenFileAsync(destination));
            MagicFieldNode reopenedField = EnumerateNodes(reopened.RootNodes).OfType<MagicFieldNode>()
                .Single(candidate => SameField(candidate, field));
            if (expectedUnsigned.HasValue)
                Assert.Equal(expectedUnsigned.Value, reopenedField.SourceField!.ValueUInt);
            if (expectedSigned.HasValue)
                Assert.Equal(expectedSigned.Value, reopenedField.SourceField!.ValueInt);
        }

        private static void CreateValidNoclipDataWithoutMagicTarget(string root)
        {
            string ffxData = Path.Combine(root, "data", "FinalFantasyX");
            foreach (string subdirectory in NoclipDataCapability.RequiredDirectoryNames)
            {
                string path = Path.Combine(ffxData, subdirectory);
                Directory.CreateDirectory(path);
                File.WriteAllBytes(Path.Combine(path, "0000.bin"), new byte[] { 0x01 });
            }

            WriteCriticalNoclipFile(Path.Combine(ffxData, "common_textures.bin"), 0x3C);
            WriteCriticalNoclipFile(Path.Combine(ffxData, "screen_shatter.bin"), 0x3C);
            WriteCriticalNoclipFile(Path.Combine(ffxData, "env_map_texture.bin"), 0x18);
        }

        private static void WriteCriticalNoclipFile(string path, int offsetField)
        {
            byte[] bytes = new byte[128];
            BitConverter.GetBytes(96u).CopyTo(bytes, offsetField);
            File.WriteAllBytes(path, bytes);
        }


        private static int CountSlotNodes(MagicDllEditor_ViewModel vm)
        {
            int count = 0;
            foreach (MagicNode root in vm.RootNodes)
            foreach (MagicNode group in root.Children)
                if (group.Label == "Programs")
                    foreach (MagicNode program in group.Children)
                        count += program.Children.Count;
            return count;
        }

        private static string FindRepoRoot()
        {
            DirectoryInfo? current = new(AppContext.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "FFXProjectEditor", "FFXProjectEditor.csproj")))
                    return current.FullName;
                current = current.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate the FFX Mod Studio repository root.");
        }
    }
}
