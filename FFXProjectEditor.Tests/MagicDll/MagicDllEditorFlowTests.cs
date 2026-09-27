using System;
using FFXProjectEditor.Tests.Infrastructure;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using FFXProjectEditor.FfxLib.MagicDll;
using FfxMagicFieldType = FFXProjectEditor.FfxLib.MagicDll.MagicFieldType;
using FFXProjectEditor.Modules.MagicDllEditor;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Tests.ViewerHub;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll
{
    /// <summary>
    /// Testes de integraÃ§Ã£o do fluxo do Magic DLL Editor (wrapper tÃ©cnico):
    /// abrir â†’ resolver campos â†’ editar (write-back confinado) â†’ salvar cÃ³pia
    /// (backup .bak + SHA) â†’ RT0 â†’ restaurar backup. Nunca escreve no corpus.
    /// </summary>
    [Collection(FileSystemReparseGuardHookCollection.Name)]
    public class MagicDllEditorFlowTests
    {
        private static string MakeTempDir()
        {
            string dir = Path.Combine(RepoWorkDir(), "ffx_magicflow_" + Guid.NewGuid().ToString("N"));
            TestDirectory.CreatePrivate(dir);
            return dir;
        }

        private static string CopyCorpusToTemp(string tempDir, string dllName)
        {
            return MagicDllTestFixture.Write(tempDir, dllName);
        }

        private static MagicSlot FirstKnownSlotWithFields(
            MagicDllFile dll,
            MagicDllDocument_Wrapper wrapper)
        {
            return dll.Roots
                .SelectMany(r => r.Programs)
                .SelectMany(p => p.Slots)
                .First(s => wrapper.ResolveAllFields(s).Count > 0);
        }

        /// <summary>Primeiro slot com campo f32 resolvido (para testes de edição).</summary>
        private static (MagicSlot Slot, MagicField Field) FirstF32Field(MagicDllFile dll, MagicDllDocument_Wrapper wrapper)
        {
            foreach (MagicSlot slot in dll.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots))
            {
                MagicField? f = wrapper.ResolveAllFields(slot).FirstOrDefault(x => x.Type == FfxMagicFieldType.F32);
                if (f is not null)
                    return (slot, f);
            }
            throw new Xunit.Sdk.XunitException("nenhum campo f32 encontrado no corpus magic_0021.dll");
        }
    internal static string RepoWorkDir()
    {
        string dir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "linux-tests");
        TestDirectory.CreatePrivate(dir);
        return dir;
    }


        [Fact]
        public void TryLoad_0021_ResolvesFields_WithAnchors()
        {
            string dir = MakeTempDir();
            try
            {
                string path = CopyCorpusToTemp(dir, "magic_0021.dll");
                var wrapper = new MagicDllDocument_Wrapper();

                Assert.True(wrapper.TryLoad(path, out string error), error);
                Assert.True(wrapper.HasDocument);
                Assert.Equal(21, wrapper.MagicId);
                Assert.Equal(64, wrapper.ShaBefore.Length);
                Assert.Equal(wrapper.ShaBefore, wrapper.ShaAfter);

                MagicSlot slot = FirstKnownSlotWithFields(wrapper.ParsedFile!, wrapper);
                var fields = wrapper.ResolveAllFields(slot);
                Assert.NotEmpty(fields);
                Assert.All(fields, f => Assert.True(f.RecordOffset >= 0));
                Assert.All(fields, f => Assert.Equal(64, f.RecordSha256.Length));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void ApplyFieldEdit_ChangesOnlyTheFieldBytes()
        {
            string dir = MakeTempDir();
            try
            {
                string path = CopyCorpusToTemp(dir, "magic_0021.dll");
                var wrapper = new MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(path, out _));


                (MagicSlot slot, MagicField field) = FirstF32Field(wrapper.ParsedFile!, wrapper);

                byte[] originalFile = File.ReadAllBytes(path);
                byte[] newBytes = BitConverter.GetBytes(123.456f);
                Assert.True(wrapper.TryApplyFieldEdit(field, newBytes, out string editError), editError);

                Assert.True(wrapper.IsDirty);
                Assert.NotEqual(wrapper.ShaAfter, wrapper.ShaBefore);

                // Confinado a janela do campo (working bytes vs arquivo original).
                byte[] working = wrapper.WorkingBytes!;
                int abs = wrapper.ParsedFile!.DataSectionRawPtr + field.RecordOffset + field.Offset;
                Assert.Equal(originalFile.Length, working.Length);
                Assert.True(originalFile.AsSpan(0, abs).SequenceEqual(working.AsSpan(0, abs)), "bytes antes do campo mudaram");
                Assert.True(
                    originalFile.AsSpan(abs + field.Width).SequenceEqual(working.AsSpan(abs + field.Width)),
                    "bytes depois do campo mudaram");
                Assert.Equal(newBytes, working.Skip(abs).Take(field.Width).ToArray());

            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void SaveCopy_WritesBackupAndMatchesShaAfter()
        {
            string dir = MakeTempDir();
            try
            {
                string path = CopyCorpusToTemp(dir, "magic_0021.dll");
                var wrapper = new MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(path, out _));

                string dest = Path.Combine(dir, "magic_copy.dll");
                Assert.True(wrapper.TrySaveCopy(dest, out string saveError), saveError);
                Assert.True(File.Exists(dest));
                Assert.True(File.Exists(dest + ".bak"));
                Assert.Equal(dest, wrapper.LastSavedPath);
                Assert.False(File.Exists(Path.Combine(dir, ".ffxms-magic-write.lock")));

                // Sem ediÃ§Ã£o: SHA do salvo == SHA do original (RT0 por construÃ§Ã£o).
                Assert.Equal(
                    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dest))).ToLowerInvariant(),
                    wrapper.ShaAfter);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void Rt0Check_CleanDocument_ReturnsTrue()
        {
            string dir = MakeTempDir();
            try
            {
                string path = CopyCorpusToTemp(dir, "magic_0021.dll");
                var wrapper = new MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(path, out _));

                Assert.True(wrapper.Rt0Check(out string message), message);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void RestoreBackup_AfterEdit_ReturnsOriginalBytes()
        {
            string dir = MakeTempDir();
            try
            {
                string path = CopyCorpusToTemp(dir, "magic_0021.dll");
                byte[] original = File.ReadAllBytes(path);

                var wrapper = new MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(path, out _));

                (MagicSlot slot, MagicField field) = FirstF32Field(wrapper.ParsedFile!, wrapper);
                Assert.True(wrapper.TryApplyFieldEdit(field, BitConverter.GetBytes(999.0f), out _));

                string dest = Path.Combine(dir, "magic_edited.dll");
                Assert.True(wrapper.TrySaveCopy(dest, out _));
                Assert.True(wrapper.TryRestoreBackup(dest, out string restoreError), restoreError);

                byte[] restored = File.ReadAllBytes(dest);
                Assert.True(original.AsSpan().SequenceEqual(restored), "restore nÃ£o voltou byte-idÃªntico");
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void TryCloneMagic_WhenDestinationExists_RefusesAndPreservesExistingFiles()
        {
            string dir = MakeTempDir();
            try
            {
                string source = CopyCorpusToTemp(dir, "magic_0021.dll");
                string destination = Path.Combine(dir, "magic_0140.dll");
                string backup = destination + ".bak";
                File.WriteAllBytes(destination, new byte[] { 0x44, 0x45, 0x53, 0x54 });
                File.WriteAllBytes(backup, new byte[] { 0x42, 0x41, 0x4B });
                string destinationHash = Sha256(destination);
                string backupHash = Sha256(backup);
                var wrapper = new MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(source, out string loadError), loadError);

                Assert.False(wrapper.TryCloneMagic(destination, out string error));

                Assert.Contains("exists", error, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(destinationHash, Sha256(destination));
                Assert.Equal(backupHash, Sha256(backup));
                Assert.False(File.Exists(Path.Combine(dir, ".ffxms-magic-write.lock")));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void TryCloneMagic_WhenLegacySiblingUsesTheSameId_RefusesLogicalCollision()
        {
            string dir = MakeTempDir();
            try
            {
                string source = CopyCorpusToTemp(dir, "magic_0021.dll");
                string legacySibling = Path.Combine(dir, "magic_140.dll");
                string destination = Path.Combine(dir, "magic_0140.dll");
                File.WriteAllBytes(legacySibling, new byte[] { 0x4C, 0x45, 0x47, 0x41, 0x43, 0x59 });
                var wrapper = new MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(source, out string loadError), loadError);

                Assert.False(wrapper.TryCloneMagic(destination, out string error));

                Assert.Contains("already exists", error, StringComparison.OrdinalIgnoreCase);
                Assert.False(File.Exists(destination));
                Assert.True(File.Exists(legacySibling));
                Assert.False(File.Exists(Path.Combine(dir, ".ffxms-magic-write.lock")));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void TryCloneMagic_ConcurrentEquivalentRequests_CreateExactlyOneClone()
        {
            string dir = MakeTempDir();
            try
            {
                string source = CopyCorpusToTemp(dir, "magic_0021.dll");
                string destination = Path.Combine(dir, "magic_0140.dll");
                var first = new MagicDllDocument_Wrapper();
                var second = new MagicDllDocument_Wrapper();
                Assert.True(first.TryLoad(source, out string firstLoadError), firstLoadError);
                Assert.True(second.TryLoad(source, out string secondLoadError), secondLoadError);
                bool firstResult = false;
                bool secondResult = false;
                string firstError = string.Empty;
                string secondError = string.Empty;

                Parallel.Invoke(
                    () => firstResult = first.TryCloneMagic(destination, out firstError),
                    () => secondResult = second.TryCloneMagic(destination, out secondError));

                Assert.True(
                    firstResult != secondResult,
                    $"Expected one successful create-only clone; first={firstResult} ({firstError}), second={secondResult} ({secondError}).");
                Assert.True(File.Exists(destination));
                Assert.True(
                    firstResult ? secondError.Contains("exists", StringComparison.OrdinalIgnoreCase) :
                        firstError.Contains("exists", StringComparison.OrdinalIgnoreCase));
                Assert.False(File.Exists(Path.Combine(dir, ".ffxms-magic-write.lock")));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void TryCloneMagic_NewDestination_UsesEditedSourceBytesWithoutMutatingTheSource()
        {
            string dir = MakeTempDir();
            try
            {
                string source = CopyCorpusToTemp(dir, "magic_0021.dll");
                string sourceHash = Sha256(source);
                var wrapper = new MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(source, out string loadError), loadError);
                byte[] expected = FFXProjectEditor.FfxLib.Magic.MagicDllNameRewriter.RewriteMagicNameStrings(
                    wrapper.WorkingBytes!,
                    oldId: 21,
                    newId: 140);
                string destination = Path.Combine(dir, "magic_0140.dll");

                Assert.True(wrapper.TryCloneMagic(destination, out string error), error);

                Assert.Equal(Convert.ToHexString(SHA256.HashData(expected)), Sha256(destination));
                Assert.Equal(sourceHash, Sha256(source));
                Assert.Equal(sourceHash, Convert.ToHexString(SHA256.HashData(wrapper.SourceBytes!)));
                Assert.False(File.Exists(Path.Combine(dir, ".ffxms-magic-write.lock")));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void SaveCopy_LeafSwapAfterHandleRead_IsRejectedWithMatchingBackupAndImmutableSource()
        {
            if (!OperatingSystem.IsWindows()) return;
            string dir = MakeTempDir();
            string destination = Path.Combine(dir, "magic_copy.dll");
            string movedDestination = Path.Combine(dir, "magic_copy_before_swap.dll");
            byte[] attacker = { 0x41, 0x54, 0x54, 0x41, 0x43, 0x4B };
            try
            {
                string source = CopyCorpusToTemp(dir, "magic_0021.dll");
                byte[] sourceBytes = File.ReadAllBytes(source);
                byte[] previousDestination = (byte[])sourceBytes.Clone();
                previousDestination[^1] ^= 0x5A;
                File.WriteAllBytes(destination, previousDestination);
                var wrapper = new MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(source, out string loadError), loadError);
                bool swapped = false;
                FileSystemReparseGuard.BeforeHandleOperationForTests = (operation, path) =>
                {
                    if (swapped || operation != "promote" ||
                        !string.Equals(path, Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                        return;
                    swapped = true;
                    File.Move(destination, movedDestination);
                    File.WriteAllBytes(destination, attacker);
                };

                Assert.False(wrapper.TrySaveCopy(destination, out _));
                Assert.True(swapped);
                Assert.Equal(attacker, File.ReadAllBytes(destination));
                Assert.Equal(previousDestination, File.ReadAllBytes(destination + ".bak"));
                Assert.Equal(sourceBytes, File.ReadAllBytes(source));
                Assert.False(File.Exists(Path.Combine(dir, ".ffxms-magic-write.lock")));
            }
            finally
            {
                FileSystemReparseGuard.BeforeHandleOperationForTests = null;
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void RestoreBackup_LeafSwapAfterVerifiedReads_IsRejectedWithoutWrongPathRestore()
        {
            if (!OperatingSystem.IsWindows()) return;
            string dir = MakeTempDir();
            string destination = Path.Combine(dir, "magic_copy.dll");
            string movedDestination = Path.Combine(dir, "magic_copy_before_restore_swap.dll");
            byte[] attacker = { 0x53, 0x57, 0x41, 0x50 };
            try
            {
                string source = CopyCorpusToTemp(dir, "magic_0021.dll");
                byte[] sourceBytes = File.ReadAllBytes(source);
                var wrapper = new MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(source, out string loadError), loadError);
                Assert.True(wrapper.TrySaveCopy(destination, out string saveError), saveError);
                string wrongTarget = Path.Combine(dir, "other.dll");
                File.WriteAllBytes(wrongTarget, attacker);
                File.WriteAllBytes(wrongTarget + ".bak", sourceBytes);
                Assert.False(wrapper.TryRestoreBackup(wrongTarget, out _));
                Assert.Equal(attacker, File.ReadAllBytes(wrongTarget));

                bool swapped = false;
                FileSystemReparseGuard.BeforeHandleOperationForTests = (operation, path) =>
                {
                    if (swapped || operation != "promote" ||
                        !string.Equals(path, Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                        return;
                    swapped = true;
                    File.Move(destination, movedDestination);
                    File.WriteAllBytes(destination, attacker);
                };

                Assert.False(wrapper.TryRestoreBackup(destination, out _));
                Assert.True(swapped);
                Assert.Equal(attacker, File.ReadAllBytes(destination));
                Assert.Equal(sourceBytes, File.ReadAllBytes(destination + ".bak"));
                Assert.Equal(sourceBytes, File.ReadAllBytes(source));
                Assert.False(File.Exists(Path.Combine(dir, ".ffxms-magic-write.lock")));
            }
            finally
            {
                FileSystemReparseGuard.BeforeHandleOperationForTests = null;
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void SaveCopy_HardLinkAliasToSource_ReplacesOnlyTheDestinationLeaf()
        {
            if (!OperatingSystem.IsWindows()) return;
            string dir = MakeTempDir();
            try
            {
                string source = CopyCorpusToTemp(dir, "magic_0021.dll");
                byte[] sourceBytes = File.ReadAllBytes(source);
                string sourceHash = Sha256(source);
                string destination = Path.Combine(dir, "magic_hardlink_copy.dll");
                Assert.True(CreateHardLink(destination, source, IntPtr.Zero));
                var wrapper = new MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(source, out string loadError), loadError);
                (MagicSlot _, MagicField field) = FirstF32Field(wrapper.ParsedFile!, wrapper);
                Assert.True(wrapper.TryApplyFieldEdit(field, BitConverter.GetBytes(7.5f), out string editError), editError);

                Assert.True(wrapper.TrySaveCopy(destination, out string saveError), saveError);

                Assert.Equal(sourceHash, Sha256(source));
                Assert.Equal(sourceBytes, File.ReadAllBytes(source));
                Assert.NotEqual(sourceBytes, File.ReadAllBytes(destination));
                Assert.Equal(sourceBytes, File.ReadAllBytes(destination + ".bak"));
                Assert.False(File.Exists(Path.Combine(dir, ".ffxms-magic-write.lock")));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Theory]
        [InlineData("magic_0140.dll")]
        [InlineData("magic_140.dll")]
        public void TryCloneMagic_DirectoryRenameDuringCollisionCheck_IsBlockedByStableDirectory(
            string collisionLeaf)
        {
            if (!OperatingSystem.IsWindows()) return;
            string dir = MakeTempDir();
            string outputDirectory = Path.Combine(dir, "clone-output");
            string movedOutput = Path.Combine(dir, "clone-output-held");
            try
            {
                string source = CopyCorpusToTemp(dir, "magic_0021.dll");
                TestDirectory.CreatePrivate(outputDirectory);
                File.WriteAllBytes(
                    Path.Combine(outputDirectory, collisionLeaf),
                    new byte[] { 0x43, 0x4F, 0x4C, 0x4C, 0x49, 0x44, 0x45 });
                string destination = Path.Combine(outputDirectory, "magic_0140.dll");
                var wrapper = new MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(source, out string loadError), loadError);
                bool renameAttempted = false;
                bool renameBlocked = false;
                FileSystemReparseGuard.BeforeHandleOperationForTests = (operation, path) =>
                {
                    if (renameAttempted || operation != "open-read-relative" ||
                        !string.Equals(path, Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                        return;
                    renameAttempted = true;
                    try
                    {
                        Directory.Move(outputDirectory, movedOutput);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        renameBlocked = true;
                    }
                };

                Assert.False(wrapper.TryCloneMagic(destination, out string error));

                Assert.True(renameAttempted);
                Assert.True(renameBlocked);
                Assert.Contains("exists", error, StringComparison.OrdinalIgnoreCase);
                Assert.True(File.Exists(Path.Combine(outputDirectory, collisionLeaf)));
                Assert.False(Directory.Exists(movedOutput));
                Assert.Single(Directory.EnumerateFiles(outputDirectory));
                Assert.False(File.Exists(Path.Combine(dir, ".ffxms-magic-write.lock")));
            }
            finally
            {
                FileSystemReparseGuard.BeforeHandleOperationForTests = null;
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void TryCloneMagic_PathSwapBeforeDirectoryOpen_FailsWithoutExternalWrite()
        {
            if (!OperatingSystem.IsWindows()) return;
            string dir = MakeTempDir();
            string outputDirectory = Path.Combine(dir, "clone-output");
            string movedOutput = Path.Combine(dir, "clone-output-original");
            string external = Path.Combine(dir, "external-target");
            try
            {
                string source = CopyCorpusToTemp(dir, "magic_0021.dll");
                TestDirectory.CreatePrivate(outputDirectory);
                TestDirectory.CreatePrivate(external);
                string destination = Path.Combine(outputDirectory, "magic_0140.dll");
                var wrapper = new MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(source, out string loadError), loadError);
                bool swapped = false;
                FileSystemReparseGuard.BeforeHandleOperationForTests = (operation, path) =>
                {
                    if (swapped || operation != "open-directory" ||
                        !string.Equals(path, Path.GetFullPath(outputDirectory), StringComparison.OrdinalIgnoreCase))
                        return;
                    swapped = true;
                    Directory.Move(outputDirectory, movedOutput);
                    CreateDirectoryLink(outputDirectory, external);
                };

                Assert.False(wrapper.TryCloneMagic(destination, out _));
                Assert.True(swapped);
                Assert.Empty(Directory.EnumerateFiles(external, "*", SearchOption.AllDirectories));
                Assert.False(File.Exists(Path.Combine(movedOutput, "magic_0140.dll")));
            }
            finally
            {
                FileSystemReparseGuard.BeforeHandleOperationForTests = null;
                try { if (Directory.Exists(outputDirectory)) Directory.Delete(outputDirectory); } catch { }
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void TryCloneMagic_DirectoryRenameAfterCreateValidation_IsBlockedUntilCleanup()
        {
            if (!OperatingSystem.IsWindows()) return;
            string dir = MakeTempDir();
            string outputDirectory = Path.Combine(dir, "clone-output");
            string movedOutput = Path.Combine(dir, "clone-output-moved");
            try
            {
                string source = CopyCorpusToTemp(dir, "magic_0021.dll");
                byte[] sourceBytes = File.ReadAllBytes(source);
                TestDirectory.CreatePrivate(outputDirectory);
                string destination = Path.Combine(outputDirectory, "magic_0140.dll");
                var wrapper = new MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(source, out string loadError), loadError);
                bool attempted = false;
                bool blocked = false;
                FileSystemReparseGuard.BeforeHandleOperationForTests = (operation, path) =>
                {
                    if (attempted || operation != "open-create-verified" ||
                        !string.Equals(path, Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                        return;
                    attempted = true;
                    try { Directory.Move(outputDirectory, movedOutput); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        blocked = true;
                    }
                };

                Assert.True(wrapper.TryCloneMagic(destination, out string error), error);

                Assert.True(attempted);
                Assert.True(blocked);
                Assert.False(Directory.Exists(movedOutput));
                Assert.True(File.Exists(destination));
                Assert.Equal(sourceBytes, File.ReadAllBytes(source));
            }
            finally
            {
                FileSystemReparseGuard.BeforeHandleOperationForTests = null;
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void SaveCopy_DirectoryRenameDuringReplacePromotion_IsBlockedUntilCleanup()
        {
            if (!OperatingSystem.IsWindows()) return;
            string dir = MakeTempDir();
            string outputDirectory = Path.Combine(dir, "save-output");
            string movedOutput = Path.Combine(dir, "save-output-moved");
            try
            {
                string source = CopyCorpusToTemp(dir, "magic_0021.dll");
                byte[] sourceBytes = File.ReadAllBytes(source);
                TestDirectory.CreatePrivate(outputDirectory);
                string destination = Path.Combine(outputDirectory, "magic_copy.dll");
                byte[] previous = (byte[])sourceBytes.Clone();
                previous[^1] ^= 0x5A;
                File.WriteAllBytes(destination, previous);
                var wrapper = new MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(source, out string loadError), loadError);
                bool attempted = false;
                bool blocked = false;
                FileSystemReparseGuard.BeforeHandleOperationForTests = (operation, path) =>
                {
                    if (attempted || operation != "promote" ||
                        !string.Equals(path, Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                        return;
                    attempted = true;
                    try { Directory.Move(outputDirectory, movedOutput); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        blocked = true;
                    }
                };

                Assert.True(wrapper.TrySaveCopy(destination, out string error), error);

                Assert.True(attempted);
                Assert.True(blocked);
                Assert.False(Directory.Exists(movedOutput));
                Assert.Equal(sourceBytes, File.ReadAllBytes(destination));
                Assert.Equal(previous, File.ReadAllBytes(destination + ".bak"));
                Assert.Equal(sourceBytes, File.ReadAllBytes(source));
            }
            finally
            {
                FileSystemReparseGuard.BeforeHandleOperationForTests = null;
                Directory.Delete(dir, recursive: true);
            }
        }

        [Theory]
        [InlineData("destination")]
        [InlineData("backup")]
        public void SaveCopy_RetainsStrictReadLeasesUntilBothPromotions(string mutationTarget)
        {
            if (!OperatingSystem.IsWindows()) return;
            string dir = MakeTempDir();
            try
            {
                string source = CopyCorpusToTemp(dir, "magic_0021.dll");
                string destination = Path.Combine(dir, "magic_copy.dll");
                string backup = destination + ".bak";
                byte[] previous = { 0x50, 0x52, 0x45, 0x56 };
                byte[] priorBackup = { 0x42, 0x41, 0x4B };
                File.WriteAllBytes(destination, previous);
                File.WriteAllBytes(backup, priorBackup);
                var wrapper = new MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(source, out string loadError), loadError);
                string guardedPath = mutationTarget == "destination" ? destination : backup;
                bool writerAttempted = false;
                bool writerBlocked = false;
                FileSystemReparseGuard.BeforeHandleOperationForTests = (operation, path) =>
                {
                    if (writerAttempted || operation != "promote" ||
                        !string.Equals(path, Path.GetFullPath(guardedPath), StringComparison.OrdinalIgnoreCase))
                        return;
                    writerAttempted = true;
                    try
                    {
                        using var writer = new FileStream(
                            guardedPath,
                            FileMode.Open,
                            FileAccess.Write,
                            FileShare.ReadWrite | FileShare.Delete);
                        writer.WriteByte(0xAA);
                        writer.Flush(flushToDisk: true);
                    }
                    catch (IOException)
                    {
                        writerBlocked = true;
                    }
                };

                Assert.True(wrapper.TrySaveCopy(destination, out string error), error);

                Assert.True(writerAttempted);
                Assert.True(writerBlocked);
                Assert.Equal(wrapper.WorkingBytes, File.ReadAllBytes(destination));
                Assert.Equal(previous, File.ReadAllBytes(backup));
            }
            finally
            {
                FileSystemReparseGuard.BeforeHandleOperationForTests = null;
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void SaveCopy_ConcurrentCreationAfterNotFoundCheck_IsNotOverwritten()
        {
            if (!OperatingSystem.IsWindows()) return;
            string dir = MakeTempDir();
            string destination = Path.Combine(dir, "magic_copy.dll");
            byte[] attacker = { 0x43, 0x4F, 0x4C, 0x4C, 0x49, 0x44, 0x45 };
            try
            {
                string source = CopyCorpusToTemp(dir, "magic_0021.dll");
                byte[] sourceBytes = File.ReadAllBytes(source);
                var wrapper = new MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(source, out string loadError), loadError);
                bool created = false;
                FileSystemReparseGuard.BeforeHandleOperationForTests = (operation, path) =>
                {
                    if (created || operation != "promote" ||
                        !string.Equals(path, Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                        return;
                    created = true;
                    File.WriteAllBytes(destination, attacker);
                };

                Assert.False(wrapper.TrySaveCopy(destination, out _));

                Assert.True(created);
                Assert.Equal(attacker, File.ReadAllBytes(destination));
                Assert.Equal(sourceBytes, File.ReadAllBytes(source));
                Assert.False(wrapper.HasRestorableBackup);
                Assert.False(File.Exists(Path.Combine(dir, ".ffxms-magic-write.lock")));
            }
            finally
            {
                FileSystemReparseGuard.BeforeHandleOperationForTests = null;
                Directory.Delete(dir, recursive: true);
            }
        }

        [Theory]
        [InlineData("destination")]
        [InlineData("backup")]
        public void RestoreBackup_RetainsStrictReadLeasesUntilPromotion(string mutationTarget)
        {
            if (!OperatingSystem.IsWindows()) return;
            string dir = MakeTempDir();
            try
            {
                string source = CopyCorpusToTemp(dir, "magic_0021.dll");
                string destination = Path.Combine(dir, "magic_copy.dll");
                var wrapper = new MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(source, out string loadError), loadError);
                Assert.True(wrapper.TrySaveCopy(destination, out string saveError), saveError);
                string backup = destination + ".bak";
                string guardedPath = mutationTarget == "destination" ? destination : backup;
                bool writerAttempted = false;
                bool writerBlocked = false;
                FileSystemReparseGuard.BeforeHandleOperationForTests = (operation, path) =>
                {
                    if (writerAttempted || operation != "promote" ||
                        !string.Equals(path, Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                        return;
                    writerAttempted = true;
                    try
                    {
                        using var writer = new FileStream(
                            guardedPath,
                            FileMode.Open,
                            FileAccess.Write,
                            FileShare.ReadWrite | FileShare.Delete);
                        writer.WriteByte(0xBB);
                        writer.Flush(flushToDisk: true);
                    }
                    catch (IOException)
                    {
                        writerBlocked = true;
                    }
                };

                Assert.True(wrapper.TryRestoreBackup(destination, out string error), error);

                Assert.True(writerAttempted);
                Assert.True(writerBlocked);
                Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(destination));
            }
            finally
            {
                FileSystemReparseGuard.BeforeHandleOperationForTests = null;
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void RestoreBackup_UsesImmutableLastSavedHashAfterFurtherEdits()
        {
            string dir = MakeTempDir();
            try
            {
                string source = CopyCorpusToTemp(dir, "magic_0021.dll");
                string destination = Path.Combine(dir, "magic_copy.dll");
                var wrapper = new MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(source, out string loadError), loadError);
                Assert.True(wrapper.TrySaveCopy(destination, out string saveError), saveError);
                Assert.True(wrapper.HasRestorableBackup);
                MagicSlot slot = wrapper.ParsedFile!.Roots
                    .SelectMany(root => root.Programs)
                    .SelectMany(program => program.Slots)
                    .First(candidate => wrapper.ResolveAllFields(candidate).Count > 0);
                MagicField field = wrapper.ResolveAllFields(slot).First();
                byte[] edited = BitConverter.GetBytes(field.ValueFloat.GetValueOrDefault() + 2.0f);
                Assert.True(wrapper.TryApplyFieldEdit(field, edited, out string editError), editError);

                Assert.True(wrapper.TryRestoreBackup(destination, out string restoreError), restoreError);

                Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(destination));
                Assert.False(wrapper.HasRestorableBackup);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Theory]
        [InlineData("in-place")]
        [InlineData("replacement")]
        public void RestoreBackup_RejectsBackupTamperOrIdentityReplacement(string tamperKind)
        {
            string dir = MakeTempDir();
            try
            {
                string source = CopyCorpusToTemp(dir, "magic_0021.dll");
                string destination = Path.Combine(dir, "magic_copy.dll");
                var wrapper = new MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(source, out string loadError), loadError);
                Assert.True(wrapper.TrySaveCopy(destination, out string saveError), saveError);
                string backup = destination + ".bak";
                byte[] backupBytes = File.ReadAllBytes(backup);
                byte[] destinationBytes = File.ReadAllBytes(destination);
                if (tamperKind == "in-place")
                {
                    backupBytes[0] ^= 0x5A;
                    File.WriteAllBytes(backup, backupBytes);
                    if (OperatingSystem.IsLinux()) File.SetUnixFileMode(backup, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                else
                {
                    File.Move(backup, backup + ".replaced");
                    File.WriteAllBytes(backup, backupBytes);
                    if (OperatingSystem.IsLinux()) File.SetUnixFileMode(backup, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }

                Assert.False(wrapper.TryRestoreBackup(destination, out string error));

                Assert.Contains("backup", error, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(destinationBytes, File.ReadAllBytes(destination));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void CloneWithoutSave_DoesNotCreateRestoreOwnership()
        {
            string dir = MakeTempDir();
            try
            {
                string source = CopyCorpusToTemp(dir, "magic_0021.dll");
                string clone = Path.Combine(dir, "magic_0140.dll");
                var wrapper = new MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(source, out string loadError), loadError);
                Assert.False(wrapper.HasRestorableBackup);

                Assert.True(wrapper.TryCloneMagic(clone, out string cloneError), cloneError);

                Assert.False(wrapper.HasRestorableBackup);
                Assert.Null(wrapper.LastSavedPath);
                Assert.False(wrapper.TryRestoreBackup(clone, out _));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        /// <summary>
        /// Serialized with the FileSystemReparseGuard hook collection. Proves that no test-only
        /// override can silently change the product default LocalAppData staging selection.
        /// </summary>
        [Fact]
        public void TryLoad_WhenStagingOverrideIsNull_UsesDefaultLocalAppDataRootAndFailsClosed()
        {
            if (!OperatingSystem.IsWindows()) return;
            const string sentinel = "magic-test-default-staging-sentinel";
            string dir = MakeTempDir();
            string? previousRoot = MagicDllDocument_Wrapper.StagingRootOverrideForTests;
            Action<string, string>? previousHook = FileSystemReparseGuard.BeforeHandleOperationForTests;
            string? observedDirectory = null;
            try
            {
                MagicDllDocument_Wrapper.StagingRootOverrideForTests = null;
                FileSystemReparseGuard.BeforeHandleOperationForTests = (operation, path) =>
                {
                    if (operation != "open-directory" || observedDirectory != null)
                        return;

                    observedDirectory = path;
                    throw new IOException(sentinel);
                };

                string source = CopyCorpusToTemp(dir, "magic_0021.dll");
                var wrapper = new MagicDllDocument_Wrapper();

                Assert.False(wrapper.TryLoad(source, out string error));

                string expectedDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FFXProjectEditor",
                    "MagicDll",
                    "Staging")));
                Assert.Equal(expectedDirectory, observedDirectory);
                Assert.Contains(sentinel, error);
                Assert.False(wrapper.HasDocument);
            }
            finally
            {
                FileSystemReparseGuard.BeforeHandleOperationForTests = previousHook;
                MagicDllDocument_Wrapper.StagingRootOverrideForTests = previousRoot;
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void Rt0Check_UsesAppOwnedStableStagingAndCleansTheExactLeaf()
        {
            if (!OperatingSystem.IsWindows()) return;
            string dir = MakeTempDir();
            using IDisposable stagingScope = MagicDllTestStaging.PushCustomRoot(out string stagingRoot);
            try
            {
                string source = CopyCorpusToTemp(dir, "magic_0021.dll");
                var wrapper = new MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(source, out string loadError), loadError);
                bool stableOpenSeen = false;
                FileSystemReparseGuard.BeforeHandleOperationForTests = (operation, path) =>
                {
                    if (operation == "open-stable-directory" &&
                        string.Equals(path, Path.GetFullPath(stagingRoot), StringComparison.OrdinalIgnoreCase))
                        stableOpenSeen = true;
                };

                Assert.True(wrapper.Rt0Check(out string message), message);

                Assert.True(stableOpenSeen);
                Assert.True(Directory.Exists(stagingRoot));
                Assert.Empty(Directory.EnumerateFiles(stagingRoot));
            }
            finally
            {
                FileSystemReparseGuard.BeforeHandleOperationForTests = null;
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void Rt0Check_StagingLeafReplacement_IsRejectedWithoutDeletingTheReplacement()
        {
            if (!OperatingSystem.IsWindows()) return;
            string dir = MakeTempDir();
            using IDisposable stagingScope = MagicDllTestStaging.PushCustomRoot(out string stagingRoot);
            string movedLeaf = Path.Combine(dir, "captured-staging.dll");
            byte[] attacker = { 0x41, 0x54, 0x54, 0x41, 0x43, 0x4B };
            try
            {
                string source = CopyCorpusToTemp(dir, "magic_0021.dll");
                var wrapper = new MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(source, out string loadError), loadError);
                bool swapped = false;
                string? replacementPath = null;
                FileSystemReparseGuard.BeforeHandleOperationForTests = (operation, path) =>
                {
                    if (swapped || operation != "open-read-strict" ||
                        !path.StartsWith(Path.GetFullPath(stagingRoot), StringComparison.OrdinalIgnoreCase))
                        return;
                    swapped = true;
                    replacementPath = path;
                    File.Move(path, movedLeaf);
                    File.WriteAllBytes(path, attacker);
                };

                Assert.False(wrapper.Rt0Check(out _));

                Assert.True(swapped);
                Assert.NotNull(replacementPath);
                Assert.Equal(attacker, File.ReadAllBytes(replacementPath!));
                Assert.True(File.Exists(movedLeaf));
            }
            finally
            {
                FileSystemReparseGuard.BeforeHandleOperationForTests = null;
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void Rt0Check_StagingRootJunctionSwap_IsRejectedWithoutExternalWrite()
        {
            if (!OperatingSystem.IsWindows()) return;
            string dir = MakeTempDir();
            using IDisposable stagingScope = MagicDllTestStaging.PushCustomRoot(out string stagingRoot);
            string movedRoot = Path.Combine(dir, "staging-original");
            string external = Path.Combine(dir, "external");
            try
            {
                TestDirectory.CreatePrivate(external);
                string source = CopyCorpusToTemp(dir, "magic_0021.dll");
                var wrapper = new MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(source, out string loadError), loadError);
                bool swapped = false;
                FileSystemReparseGuard.BeforeHandleOperationForTests = (operation, path) =>
                {
                    if (swapped || operation != "open-stable-directory" ||
                        !string.Equals(path, Path.GetFullPath(stagingRoot), StringComparison.OrdinalIgnoreCase))
                        return;
                    swapped = true;
                    Directory.Move(stagingRoot, movedRoot);
                    CreateDirectoryLink(stagingRoot, external);
                };

                Assert.False(wrapper.Rt0Check(out _));

                Assert.True(swapped);
                Assert.Empty(Directory.EnumerateFiles(external, "*", SearchOption.AllDirectories));
            }
            finally
            {
                FileSystemReparseGuard.BeforeHandleOperationForTests = null;
                try { if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot); } catch { }
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void TryLoad_ParsesOneImmutableStagedSnapshotAcrossSourceAba()
        {
            if (!OperatingSystem.IsWindows()) return;
            string dir = MakeTempDir();
            using IDisposable stagingScope = MagicDllTestStaging.PushCustomRoot(out string stagingRoot);
            try
            {
                string source = CopyCorpusToTemp(dir, "magic_0021.dll");
                byte[] snapshotA = File.ReadAllBytes(source);
                byte[] snapshotB = MagicDllTestFixture.ReadBytes("magic_0098.dll");
                bool changedToB = false;
                bool restoredToA = false;
                FileSystemReparseGuard.BeforeHandleOperationForTests = (operation, _) =>
                {
                    if (operation == "staging-before-parse")
                    {
                        File.WriteAllBytes(source, snapshotB);
                        changedToB = true;
                    }
                    else if (operation == "staging-after-parse")
                    {
                        File.WriteAllBytes(source, snapshotA);
                        restoredToA = true;
                    }
                };
                var wrapper = new MagicDllDocument_Wrapper();

                Assert.True(wrapper.TryLoad(source, out string error), error);

                Assert.True(changedToB);
                Assert.True(restoredToA);
                Assert.Equal(snapshotA, wrapper.SourceBytes);
                Assert.Equal(snapshotA, wrapper.ParsedFile!.FileBytes);
                Assert.Equal(Path.GetFullPath(source), wrapper.ParsedFile.SourcePath);
                Assert.Equal("magic_0021.dll", wrapper.ParsedFile.DllName);
            }
            finally
            {
                FileSystemReparseGuard.BeforeHandleOperationForTests = null;
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void IsSteamLibraryPath_BlocksGameInstall()
        {
            Assert.True(MagicDllDocument_Wrapper.IsSteamLibraryPath(Path.Combine(
                FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot + "/work", "SteamLibrary", "steamapps", "common", "game", "magic_0021.dll")));
            Assert.False(MagicDllDocument_Wrapper.IsSteamLibraryPath(Path.Combine(
                FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot + "/work", "ffx-editor-copy", "magic_copy.dll")));
        }

        private static string Sha256(string path) =>
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

        private static void CreateDirectoryLink(string link, string target)
        {
            try
            {
                Directory.CreateSymbolicLink(link, target);
                return;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                var startInfo = new ProcessStartInfo("cmd.exe")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                startInfo.ArgumentList.Add("/c");
                startInfo.ArgumentList.Add("mklink");
                startInfo.ArgumentList.Add("/J");
                startInfo.ArgumentList.Add(link);
                startInfo.ArgumentList.Add(target);
                using Process process = Process.Start(startInfo)!;
                Assert.True(process.WaitForExit(5000), "mklink /J timed out");
                Assert.Equal(0, process.ExitCode);
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateHardLink(
            string fileName,
            string existingFileName,
            IntPtr securityAttributes);
    }
}
