using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using FFXProjectEditor.FfxLib.Ps3;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll
{
    /// <summary>
    /// Regression coverage for clone operations that write into a user's Magic DLL folder.
    /// Existing files are never disposable: a new ID may be created, but an occupied ID must
    /// be rejected before any bytes are changed.
    /// </summary>
    public sealed class MagicDllDecompilerSafetyTests
    {
        [Fact]
        public void CloneToMagicId_WhenTargetExists_RefusesAndPreservesTheTarget()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                string source = Path.Combine(directory, "source.dll");
                File.Copy(typeof(MagicDllDecompiler).Assembly.Location, source);
                string target = Path.Combine(directory, "magic_0716.dll");
                byte[] sentinel = { 0x46, 0x46, 0x58, 0x21 };
                File.WriteAllBytes(target, sentinel);
                string before = Sha256(target);

                IOException error = Assert.Throws<IOException>(() =>
                    MagicDllDecompiler.CloneToMagicId(source, directory, 716));

                Assert.Contains("already exists", error.Message, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(before, Sha256(target));
                Assert.Equal(sentinel, File.ReadAllBytes(target));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void CloneToMagicId_WhenTargetIsNew_CreatesAnIdenticalCopy()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                string source = Path.Combine(directory, "source.dll");
                File.Copy(typeof(MagicDllDecompiler).Assembly.Location, source);

                MagicDllCloneResult result = MagicDllDecompiler.CloneToMagicId(source, directory, 716);

                Assert.True(result.ByteIdentical);
                Assert.Equal(Sha256(source), Sha256(result.OutputDll));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void CompileBytePreserving_WhenOutputExists_RefusesAndPreservesTheTarget()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                string source = Path.Combine(directory, "source.dll");
                File.Copy(typeof(MagicDllDecompiler).Assembly.Location, source);
                string output = Path.Combine(directory, "repacked.dll");
                byte[] sentinel = { 0x52, 0x45, 0x50, 0x41, 0x43, 0x4B };
                File.WriteAllBytes(output, sentinel);
                string before = Sha256(output);

                IOException error = Assert.Throws<IOException>(() =>
                    MagicDllDecompiler.CompileBytePreserving(source, output));

                Assert.Contains("already exists", error.Message, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(before, Sha256(output));
                Assert.Equal(sentinel, File.ReadAllBytes(output));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void ApplyPatchPlan_WhenOutputExists_RefusesAndPreservesTheTarget()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                string source = Path.Combine(directory, "source.dll");
                File.Copy(typeof(MagicDllDecompiler).Assembly.Location, source);
                string patchPlan = Path.Combine(directory, "patch.json");
                File.WriteAllText(patchPlan, "{}");
                string output = Path.Combine(directory, "patched.dll");
                byte[] sentinel = { 0x50, 0x41, 0x54, 0x43, 0x48 };
                File.WriteAllBytes(output, sentinel);
                string before = Sha256(output);

                IOException error = Assert.Throws<IOException>(() =>
                    MagicDllDecompiler.ApplyPatchPlan(source, patchPlan, output));

                Assert.Contains("already exists", error.Message, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(before, Sha256(output));
                Assert.Equal(sentinel, File.ReadAllBytes(output));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void BuildNativeProject_WhenOutputExists_RefusesAndPreservesTheTarget()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                string output = Path.Combine(directory, "native_rebuild.dll");
                byte[] sentinel = { 0x4E, 0x41, 0x54, 0x49, 0x56, 0x45 };
                File.WriteAllBytes(output, sentinel);
                string before = Sha256(output);

                IOException error = Assert.Throws<IOException>(() =>
                    MagicDllDecompiler.BuildNativeProject(directory, output));

                Assert.Contains("already exists", error.Message, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(before, Sha256(output));
                Assert.Equal(sentinel, File.ReadAllBytes(output));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void BuildNativeProjectCore_WhenCompilerSucceeds_CommitsOnlyTheStagedDll()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                File.WriteAllText(Path.Combine(directory, "magic_stub.c"), "/* fixture */");
                File.WriteAllText(Path.Combine(directory, "exports.def"), "EXPORTS\n    InitMagicPRX @1");
                string output = Path.Combine(directory, "native_rebuild.dll");
                string unrelated = Path.Combine(directory, "keep.txt");
                File.WriteAllText(unrelated, "sentinel");
                byte[] compilerPayload = CreateMinimalPe32X86();
                string? stagedOutput = null;

                MagicDllNativeBuildResult result = MagicDllDecompiler.BuildNativeProjectCore(
                    directory,
                    output,
                    "auto",
                    _ => "fake-cl.exe",
                    (exe, args, workingDirectory) =>
                    {
                        Assert.Equal("fake-cl.exe", exe);
                        Assert.Equal(directory, workingDirectory);
                        stagedOutput = ExtractQuotedArgument(args, "/OUT:\"");
                        Assert.NotEqual(Path.GetFullPath(output), Path.GetFullPath(stagedOutput));
                        string stagingDirectory = Path.GetDirectoryName(Path.GetFullPath(stagedOutput))!;
                        Assert.Equal(Path.GetDirectoryName(Path.GetFullPath(output)), Path.GetDirectoryName(stagingDirectory));
                        Assert.StartsWith(Path.GetFileName(output) + ".ffxms-staging-", Path.GetFileName(stagingDirectory), StringComparison.Ordinal);
                        Assert.Equal(Path.GetFileName(output), Path.GetFileName(stagedOutput));
                        Assert.Equal(stagingDirectory, Path.GetDirectoryName(ExtractQuotedArgument(args, "/Fo:\"")));
                        Assert.Equal(stagingDirectory, Path.GetDirectoryName(ExtractQuotedArgument(args, "/IMPLIB:\"")));
                        Assert.Equal(stagingDirectory, Path.GetDirectoryName(ExtractQuotedArgument(args, "/PDB:\"")));
                        Assert.Contains("/INCREMENTAL:NO", args, StringComparison.Ordinal);
                        File.WriteAllBytes(stagedOutput, compilerPayload);
                        return (0, "fake stdout", string.Empty);
                    });

                Assert.True(result.Pass);
                Assert.Equal(output, result.OutputDll);
                Assert.Equal(compilerPayload, File.ReadAllBytes(output));
                Assert.Equal("sentinel", File.ReadAllText(unrelated));
                Assert.Empty(Directory.GetFileSystemEntries(directory, "native_rebuild.dll.ffxms-staging-*"));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void BuildNativeProjectCore_WhenCompilerProducesMZFake_RejectsWithoutCommitting()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                File.WriteAllText(Path.Combine(directory, "magic_stub.c"), "/* fixture */");
                File.WriteAllText(Path.Combine(directory, "exports.def"), "EXPORTS");
                string output = Path.Combine(directory, "native_rebuild.dll");

                MagicDllNativeBuildResult result = MagicDllDecompiler.BuildNativeProjectCore(
                    directory,
                    output,
                    "auto",
                    _ => "fake-cl.exe",
                    (_, args, _) =>
                    {
                        string staging = ExtractQuotedArgument(args, "/OUT:\"");
                        File.WriteAllBytes(staging, new byte[] { 0x4D, 0x5A, 0x46, 0x41, 0x4B, 0x45 });
                        return (0, "fake stdout", string.Empty);
                    });

                Assert.False(result.Pass);
                Assert.Contains("invalid", result.Summary, StringComparison.OrdinalIgnoreCase);
                Assert.False(File.Exists(output));
                Assert.Empty(Directory.GetFileSystemEntries(directory, "native_rebuild.dll.ffxms-staging-*"));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Theory]
        [InlineData(false, "InitMagicPRX", "DLL")]
        [InlineData(true, "UnrelatedExport", "InitMagicPRX")]
        public void BuildNativeProjectCore_RejectsWrongImageKindOrMissingDeclaredExport(
            bool imageIsDll,
            string imageExport,
            string expectedFailure)
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                File.WriteAllText(Path.Combine(directory, "magic_stub.c"), "/* fixture */");
                File.WriteAllText(Path.Combine(directory, "exports.def"), "EXPORTS\n    InitMagicPRX @1");
                string output = Path.Combine(directory, "native_rebuild.dll");

                MagicDllNativeBuildResult result = MagicDllDecompiler.BuildNativeProjectCore(
                    directory,
                    output,
                    "auto",
                    _ => "fake-cl.exe",
                    (_, args, _) =>
                    {
                        string staging = ExtractQuotedArgument(args, "/OUT:\"");
                        File.WriteAllBytes(staging, CreateMinimalPe32X86(imageIsDll, imageExport));
                        return (0, "fake stdout", string.Empty);
                    });

                Assert.False(result.Pass);
                Assert.Contains(expectedFailure, result.Summary, StringComparison.OrdinalIgnoreCase);
                Assert.False(File.Exists(output));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void BuildNativeProjectCore_WhenCompilerFails_CleansOnlyStaging()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                File.WriteAllText(Path.Combine(directory, "magic_stub.c"), "/* fixture */");
                File.WriteAllText(Path.Combine(directory, "exports.def"), "EXPORTS");
                string output = Path.Combine(directory, "native_rebuild.dll");
                string unrelated = Path.Combine(directory, "keep.txt");
                File.WriteAllText(unrelated, "sentinel");

                MagicDllNativeBuildResult result = MagicDllDecompiler.BuildNativeProjectCore(
                    directory,
                    output,
                    "auto",
                    _ => "fake-cl.exe",
                    (_, args, _) =>
                    {
                        string staging = ExtractQuotedArgument(args, "/OUT:\"");
                        File.WriteAllBytes(staging, new byte[] { 0x4D, 0x5A });
                        return (1, string.Empty, "fake failure");
                    });

                Assert.False(result.Pass);
                Assert.False(File.Exists(output));
                Assert.Equal("sentinel", File.ReadAllText(unrelated));
                Assert.Empty(Directory.GetFileSystemEntries(directory, "native_rebuild.dll.ffxms-staging-*"));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        private static string CreateTemporaryDirectory()
        {
            string directory = Path.Combine(Path.GetTempPath(), "ffx_magic_clone_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static string Sha256(string path) =>
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

        private static string ExtractQuotedArgument(string commandLine, string marker)
        {
            int start = commandLine.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Missing compiler argument marker: {marker}");
            start += marker.Length;
            int end = commandLine.IndexOf('"', start);
            Assert.True(end > start, $"Missing closing quote for compiler argument marker: {marker}");
            return commandLine[start..end];
        }

        private static byte[] CreateMinimalPe32X86(
            bool imageIsDll = true,
            string exportName = "InitMagicPRX")
        {
            const int peOffset = 0x80;
            const int optionalOffset = peOffset + 24;
            const int sectionOffset = optionalOffset + 0xE0;
            byte[] bytes = new byte[0x600];

            WriteU16(bytes, 0x00, 0x5A4D); // MZ
            WriteU32(bytes, 0x3C, peOffset);
            WriteU32(bytes, peOffset, 0x00004550); // PE\0\0
            WriteU16(bytes, peOffset + 4, 0x014C); // IMAGE_FILE_MACHINE_I386
            WriteU16(bytes, peOffset + 6, 1);
            WriteU16(bytes, peOffset + 20, 0xE0);
            WriteU16(bytes, peOffset + 22, imageIsDll ? (ushort)0x2102 : (ushort)0x0102);

            WriteU16(bytes, optionalOffset, 0x10B); // PE32
            WriteU32(bytes, optionalOffset + 4, 0x400);
            WriteU32(bytes, optionalOffset + 16, 0x1000);
            WriteU32(bytes, optionalOffset + 20, 0x1000);
            WriteU32(bytes, optionalOffset + 24, 0x1000);
            WriteU32(bytes, optionalOffset + 28, 0x10000000);
            WriteU32(bytes, optionalOffset + 32, 0x1000);
            WriteU32(bytes, optionalOffset + 36, 0x200);
            WriteU16(bytes, optionalOffset + 40, 4);
            WriteU16(bytes, optionalOffset + 48, 4);
            WriteU32(bytes, optionalOffset + 56, 0x2000);
            WriteU32(bytes, optionalOffset + 60, 0x200);
            WriteU16(bytes, optionalOffset + 68, 2);
            WriteU32(bytes, optionalOffset + 72, 0x100000);
            WriteU32(bytes, optionalOffset + 76, 0x1000);
            WriteU32(bytes, optionalOffset + 80, 0x100000);
            WriteU32(bytes, optionalOffset + 84, 0x1000);
            WriteU32(bytes, optionalOffset + 92, 16);
            WriteU32(bytes, optionalOffset + 96, 0x1100); // export directory RVA
            WriteU32(bytes, optionalOffset + 100, 0x80);

            Encoding.ASCII.GetBytes(".text\0\0\0").CopyTo(bytes, sectionOffset);
            WriteU32(bytes, sectionOffset + 8, 0x400);
            WriteU32(bytes, sectionOffset + 12, 0x1000);
            WriteU32(bytes, sectionOffset + 16, 0x400);
            WriteU32(bytes, sectionOffset + 20, 0x200);
            WriteU32(bytes, sectionOffset + 36, 0x60000020);
            bytes[0x200] = 0xC3; // ret

            const int exportOffset = 0x300;
            WriteU32(bytes, exportOffset + 16, 1);
            WriteU32(bytes, exportOffset + 20, 1);
            WriteU32(bytes, exportOffset + 24, 1);
            WriteU32(bytes, exportOffset + 28, 0x1140);
            WriteU32(bytes, exportOffset + 32, 0x1144);
            WriteU32(bytes, exportOffset + 36, 0x1148);
            WriteU32(bytes, 0x340, 0x1000);
            WriteU32(bytes, 0x344, 0x1150);
            WriteU16(bytes, 0x348, 0);
            Encoding.ASCII.GetBytes(exportName + "\0").CopyTo(bytes, 0x350);
            return bytes;
        }

        private static void WriteU16(byte[] bytes, int offset, ushort value) =>
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset, sizeof(ushort)), value);

        private static void WriteU32(byte[] bytes, int offset, int value) =>
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, sizeof(int)), value);
    }
}
