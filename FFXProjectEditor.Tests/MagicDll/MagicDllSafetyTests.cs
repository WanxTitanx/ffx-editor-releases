using System;
using FFXProjectEditor.Tests.Infrastructure;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Modules.MagicDllEditor;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll
{
    /// <summary>
    /// Testes de segurança sugeridos pela auditoria de cobertura (2026-08-02):
    /// hash-gate NEGATIVO do restore, bloqueio de Steam Library no save, largura
    /// errada no write-back.
    /// </summary>
    public class MagicDllSafetyTests
    {
        private static string MakeTempDir()
        {
            string dir = Path.Combine(TestDataPaths.RepoRoot, "work", "ffx_safety_" + Guid.NewGuid().ToString("N"));
            TestDirectory.CreatePrivate(dir);
            return dir;
        }

        [Theory]
        [InlineData(false, true, true, false)]
        [InlineData(true, false, true, false)]
        [InlineData(true, true, false, false)]
        [InlineData(true, true, true, true)]
        public void SchemaWriteGate_RequiresPayloadEditableAndAContainingWindow(
            bool payloadConsumer,
            bool editable,
            bool hasWindow,
            bool expected)
        {
            var schema = new MagicFamilySchema(
                "pppTest",
                handlerAddr: null,
                payloadConsumer,
                editable,
                hasWindow ? new MagicWindow(8, 4) : null,
                matchWord: null,
                guard: null,
                fields: new List<MagicFieldSpec>(),
                status: null,
                usage: null,
                rawWidthYonishi: null,
                widthReconciled: null,
                semanticsCategory: null,
                realFunc: null);

            Assert.Equal(
                expected,
                MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, offset: 8, width: 4));
        }

        [Fact]
        public void KnownReadOnlyAndUnknownFamilies_NeverAcquireAWriteWindow()
        {
            MagicFieldMap map = MagicFieldMap.LoadEmbedded();
            Assert.True(map.TryGet("pppSMatrix", out MagicFamilySchema? knownReadOnly));
            Assert.False(MagicDllDocument_Wrapper.IsSchemaFieldWritable(
                knownReadOnly,
                offset: 8,
                width: 4));
            Assert.False(MagicDllDocument_Wrapper.IsSchemaFieldWritable(
                schema: null,
                offset: 8,
                width: 4));
            Assert.False(map.TryGet("pppDefinitelyUnknown", out _));
        }

        [Fact]
        public void TryLoad_RejectsWrongMachineBeforeTheParserCanTreatItAsMagic()
        {
            string dir = MakeTempDir();
            try
            {
                string path = Path.Combine(dir, "magic_0021.dll");
                byte[] bytes = MagicDllTestFixture.ReadBytes("magic_0021.dll");
                int peOffset = BitConverter.ToInt32(bytes, 0x3C);
                BitConverter.GetBytes((ushort)0x8664).CopyTo(bytes, peOffset + 4);
                File.WriteAllBytes(path, bytes);

                var wrapper = new MagicDllDocument_Wrapper();

                Assert.False(wrapper.TryLoad(path, out string error));
                Assert.Contains("x86", error, StringComparison.OrdinalIgnoreCase);
                Assert.False(wrapper.HasDocument);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void TryLoad_RejectsMalformedNonPeInput()
        {
            string dir = MakeTempDir();
            try
            {
                string path = Path.Combine(dir, "magic_0021.dll");
                File.WriteAllBytes(path, Encoding.ASCII.GetBytes("not a portable executable"));
                var wrapper = new MagicDllDocument_Wrapper();

                Assert.False(wrapper.TryLoad(path, out string error));

                Assert.Contains("PE", error, StringComparison.OrdinalIgnoreCase);
                Assert.False(wrapper.HasDocument);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void TryLoad_RejectsAPeWhoseRequiredMagicExportWasRenamed()
        {
            string dir = MakeTempDir();
            try
            {
                string path = Path.Combine(dir, "magic_0021.dll");
                byte[] bytes = MagicDllTestFixture.ReadBytes("magic_0021.dll");
                byte[] expected = Encoding.ASCII.GetBytes("InitMagicPRX");
                byte[] replacement = Encoding.ASCII.GetBytes("NotMagic_PRX");
                int exportNameOffset = FindSequence(bytes, expected);
                Assert.True(exportNameOffset >= 0);
                replacement.CopyTo(bytes, exportNameOffset);
                File.WriteAllBytes(path, bytes);

                var wrapper = new MagicDllDocument_Wrapper();

                Assert.False(wrapper.TryLoad(path, out string error));
                Assert.Contains("InitMagicPRX", error, StringComparison.Ordinal);
                Assert.False(wrapper.HasDocument);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void TryLoad_RejectsAnExportNameWithAnInvalidOrdinal()
        {
            string dir = MakeTempDir();
            try
            {
                string path = Path.Combine(dir, "magic_0021.dll");
                byte[] bytes = MagicDllTestFixture.ReadBytes("magic_0021.dll");
                int peOffset = BitConverter.ToInt32(bytes, 0x3C);
                int optionalHeader = peOffset + 24;
                int exportDirectory = RvaToFileOffset(
                    bytes,
                    optionalHeader,
                    BitConverter.ToUInt32(bytes, optionalHeader + 96));
                int ordinalTable = RvaToFileOffset(
                    bytes,
                    optionalHeader,
                    BitConverter.ToUInt32(bytes, exportDirectory + 36));
                BitConverter.GetBytes(ushort.MaxValue).CopyTo(bytes, ordinalTable);
                File.WriteAllBytes(path, bytes);

                var wrapper = new MagicDllDocument_Wrapper();

                Assert.False(wrapper.TryLoad(path, out string error));
                Assert.Contains("export", error, StringComparison.OrdinalIgnoreCase);
                Assert.False(wrapper.HasDocument);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Theory]
        [InlineData("data")]
        [InlineData("rdata")]
        [InlineData("null")]
        [InlineData("unmapped")]
        [InlineData("forwarder")]
        public void TryLoad_RejectsRequiredExportTargetsOutsideFileBackedExecutableCode(string targetKind)
        {
            string dir = MakeTempDir();
            try
            {
                string path = Path.Combine(dir, "magic_0021.dll");
                byte[] bytes = MagicDllTestFixture.ReadBytes("magic_0021.dll");
                SetRequiredExportTarget(bytes, "InitMagicPRX", targetKind);
                File.WriteAllBytes(path, bytes);

                var wrapper = new MagicDllDocument_Wrapper();

                Assert.False(wrapper.TryLoad(path, out string error));
                Assert.Contains("export", error, StringComparison.OrdinalIgnoreCase);
                Assert.False(wrapper.HasDocument);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Theory]
        [InlineData("function-table")]
        [InlineData("name-table")]
        [InlineData("ordinal-table")]
        [InlineData("export-name")]
        public void TryLoad_RejectsExportMetadataThatCrossesItsFileBackedSection(string rangeKind)
        {
            string dir = MakeTempDir();
            try
            {
                string path = Path.Combine(dir, "magic_0021.dll");
                byte[] bytes = MagicDllTestFixture.ReadBytes("magic_0021.dll");
                MoveExportMetadataAcrossRdataBoundary(bytes, rangeKind);
                File.WriteAllBytes(path, bytes);

                var wrapper = new MagicDllDocument_Wrapper();

                Assert.False(wrapper.TryLoad(path, out string error));
                Assert.Contains("export", error, StringComparison.OrdinalIgnoreCase);
                Assert.False(wrapper.HasDocument);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void NameRewrite_WithoutTheExpectedInternalIdentity_IsRejected()
        {
            byte[] unrelated = Encoding.Unicode.GetBytes("magic_9999.dll");

            Assert.Throws<InvalidDataException>(() =>
                FFXProjectEditor.FfxLib.Magic.MagicDllNameRewriter.RewriteMagicNameStrings(
                    unrelated,
                    oldId: 21,
                    newId: 140));
        }

        [Fact]
        public void RestoreBackup_WrongSha_Aborts()
        {
            string dir = MakeTempDir();
            try
            {
                string path = Path.Combine(dir, "magic_0021.dll");
                MagicDllTestFixture.Write(dir, "magic_0021.dll");

                var wrapper = new MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(path, out string loadError), loadError);

                string dest = Path.Combine(dir, "magic_out.dll");
                Assert.True(wrapper.TrySaveCopy(dest, out string saveError), saveError);

                // Corrompe o dest DEPOIS do save (SHA atual != ShaAfter esperado).
                File.WriteAllBytes(dest, new byte[] { 1, 2, 3, 4 });

                Assert.False(wrapper.TryRestoreBackup(dest, out string restoreError),
                    "restore com SHA divergente deveria abortar");
                Assert.Contains("hash-gated", restoreError);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void SaveCopy_ToSteamLibraryPath_Blocked()
        {
            string dir = MakeTempDir();
            try
            {
                string path = Path.Combine(dir, "magic_0021.dll");
                MagicDllTestFixture.Write(dir, "magic_0021.dll");

                var wrapper = new MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(path, out string loadError), loadError);

                string steamPath = Path.Combine(dir, "SteamLibrary", "steamapps", "magic_x.dll");
                TestDirectory.CreatePrivate(Path.GetDirectoryName(steamPath)!);

                Assert.False(wrapper.TrySaveCopy(steamPath, out string saveError),
                    "save em Steam Library deveria ser bloqueado");
                Assert.Contains("Steam", saveError, StringComparison.OrdinalIgnoreCase);
                Assert.False(File.Exists(steamPath));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void ApplyFieldEdit_WrongWidth_Rejected()
        {
            string dir = MakeTempDir();
            try
            {
                string path = Path.Combine(dir, "magic_0021.dll");
                MagicDllTestFixture.Write(dir, "magic_0021.dll");

                var wrapper = new MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(path, out string loadError), loadError);

                // Primeiro campo f32 editável (largura 4B) — aplica com 2 bytes (errado).
                var field = wrapper.ParsedFile!.Roots
                    .SelectMany(r => r.Programs)
                    .SelectMany(p => p.Slots)
                    .SelectMany(s => wrapper.ResolveAllFields(s))
                    .FirstOrDefault(f =>
                        f.Type is FFXProjectEditor.FfxLib.MagicDll.MagicFieldType.F32 &&
                        f.Offset >= 8);
                Assert.NotNull(field);

                Assert.False(wrapper.TryApplyFieldEdit(field!, new byte[] { 1, 2 }, out string editError),
                    "write-back com largura errada deveria rejeitar");
                Assert.Contains("width", editError, StringComparison.OrdinalIgnoreCase);
                Assert.False(wrapper.IsDirty, "falha de validação não deve sujar o documento");
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void SaveCopy_RefusesTheOpenedSourceAndPreservesItsBytes()
        {
            string dir = MakeTempDir();
            try
            {
                string source = Path.Combine(dir, "magic_0021.dll");
                MagicDllTestFixture.Write(dir, "magic_0021.dll");
                byte[] before = File.ReadAllBytes(source);
                var wrapper = new MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(source, out string loadError), loadError);

                Assert.False(wrapper.TrySaveCopy(source, out string error));
                Assert.Contains("source", error, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(before, File.ReadAllBytes(source));
                Assert.False(File.Exists(source + ".bak"));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void PublicByteSnapshots_CannotBypassSchemaOrChangeSaveOutput()
        {
            string dir = MakeTempDir();
            try
            {
                string source = Path.Combine(dir, "magic_0021.dll");
                MagicDllTestFixture.Write(dir, "magic_0021.dll");
                byte[] expected = File.ReadAllBytes(source);
                var wrapper = new MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(source, out string loadError), loadError);

                byte[] workingSnapshot = wrapper.WorkingBytes!;
                byte[] sourceSnapshot = wrapper.SourceBytes!;
                workingSnapshot[0] ^= 0xFF;
                sourceSnapshot[0] ^= 0xFF;

                string destination = Path.Combine(dir, "magic_snapshot_copy.dll");
                Assert.True(wrapper.TrySaveCopy(destination, out string saveError), saveError);
                Assert.Equal(expected, wrapper.WorkingBytes);
                Assert.Equal(expected, wrapper.SourceBytes);
                Assert.Equal(expected, File.ReadAllBytes(destination));
                Assert.False(wrapper.IsDirty);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void TryCloneMagic_RenamedValidSourceWithoutProvableMagicIdentity_IsRefused()
        {
            string dir = MakeTempDir();
            try
            {
                string source = Path.Combine(dir, "renamed.dll");
                File.WriteAllBytes(source, MagicDllTestFixture.ReadBytes("magic_0021.dll"));
                string destination = Path.Combine(dir, "magic_0140.dll");
                var wrapper = new MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(source, out string loadError), loadError);
                Assert.Equal(-1, wrapper.MagicId);

                Assert.False(wrapper.TryCloneMagic(destination, out string error));
                Assert.Contains("identity", error, StringComparison.OrdinalIgnoreCase);
                Assert.False(File.Exists(destination));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void TryApplyFieldEdit_RejectsUnknownOpcodeAndForgedSchemaField()
        {
            string dir = MakeTempDir();
            try
            {
                string source = Path.Combine(dir, "magic_0021.dll");
                File.WriteAllBytes(source, MagicDllTestFixture.ReadBytes("magic_0021.dll"));
                var wrapper = new MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(source, out string loadError), loadError);
                MagicField known = wrapper.ParsedFile!.Roots
                    .SelectMany(root => root.Programs)
                    .SelectMany(program => program.Slots)
                    .SelectMany(wrapper.ResolveAllFields)
                    .First(field => field.Offset >= 8);
                byte[] before = (byte[])wrapper.WorkingBytes!.Clone();
                byte[] value = new byte[known.Width];
                var unknownOpcode = new MagicField(
                    "pppDefinitelyUnknown",
                    known.Name,
                    known.Offset,
                    known.Width,
                    known.Type,
                    known.Semantics,
                    known.RecordOffset,
                    known.RecordSha256,
                    known.ValueFloat,
                    known.ValueInt,
                    known.ValueUInt);
                var forgedField = new MagicField(
                    known.OpcodeName,
                    "not_declared_by_schema",
                    known.Offset,
                    known.Width,
                    known.Type,
                    known.Semantics,
                    known.RecordOffset,
                    known.RecordSha256,
                    known.ValueFloat,
                    known.ValueInt,
                    known.ValueUInt);

                Assert.False(wrapper.TryApplyFieldEdit(unknownOpcode, value, out _));
                Assert.False(wrapper.TryApplyFieldEdit(forgedField, value, out _));
                Assert.Equal(before, wrapper.WorkingBytes);
                Assert.False(wrapper.IsDirty);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void TryApplyFieldEdit_UsesPrivateImmutableSchemaAndAnchorSnapshots()
        {
            string dir = MakeTempDir();
            try
            {
                string source = Path.Combine(dir, "magic_0021.dll");
                File.WriteAllBytes(source, MagicDllTestFixture.ReadBytes("magic_0021.dll"));
                var wrapper = new MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(source, out string loadError), loadError);
                MagicField field = wrapper.ParsedFile!.Roots
                    .SelectMany(root => root.Programs)
                    .SelectMany(program => program.Slots)
                    .SelectMany(wrapper.ResolveAllFields)
                    .First(candidate => candidate.Offset >= 8);
                int fileOffset = wrapper.ParsedFile.DataSectionRawPtr + field.RecordOffset + field.Offset;
                byte[] before = wrapper.WorkingBytes!;
                byte[] replacement = before.AsSpan(fileOffset, field.Width).ToArray();
                for (int index = 0; index < replacement.Length; index++)
                    replacement[index] ^= 0x5A;

                // These internal diagnostic graphs are deliberately mutable under InternalsVisibleTo.
                // The product writer must remain anchored to private snapshots captured at load.
                Assert.True(((IDictionary<string, MagicFamilySchema>)wrapper.FieldMap!.Families)
                    .Remove(field.OpcodeName));
                ((IList<MagicDllRoot>)wrapper.ParsedFile.Roots).Clear();

                Assert.True(wrapper.TryApplyFieldEdit(field, replacement, out string editError), editError);

                byte[] after = wrapper.WorkingBytes!;
                Assert.Equal(replacement, after.AsSpan(fileOffset, field.Width).ToArray());
                Assert.Equal(before.Length, after.Length);
                Assert.Equal(field.Width, before.Zip(after).Count(pair => pair.First != pair.Second));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        private static int FindSequence(byte[] haystack, byte[] needle)
        {
            for (int offset = 0; offset <= haystack.Length - needle.Length; offset++)
            {
                if (haystack.AsSpan(offset, needle.Length).SequenceEqual(needle))
                    return offset;
            }
            return -1;
        }

        private static int RvaToFileOffset(byte[] bytes, int optionalHeader, uint rva)
        {
            int peOffset = optionalHeader - 24;
            int sectionCount = BitConverter.ToUInt16(bytes, peOffset + 6);
            int optionalSize = BitConverter.ToUInt16(bytes, peOffset + 20);
            int sectionTable = optionalHeader + optionalSize;
            for (int index = 0; index < sectionCount; index++)
            {
                int section = sectionTable + index * 40;
                uint virtualSize = BitConverter.ToUInt32(bytes, section + 8);
                uint virtualAddress = BitConverter.ToUInt32(bytes, section + 12);
                uint rawSize = BitConverter.ToUInt32(bytes, section + 16);
                uint rawOffset = BitConverter.ToUInt32(bytes, section + 20);
                uint span = Math.Max(virtualSize, rawSize);
                if (rva >= virtualAddress && (ulong)rva < (ulong)virtualAddress + span)
                    return checked((int)(rawOffset + rva - virtualAddress));
            }
            throw new InvalidDataException($"RVA 0x{rva:X8} is not file-backed.");
        }

        private static void SetRequiredExportTarget(byte[] bytes, string exportName, string targetKind)
        {
            int peOffset = BitConverter.ToInt32(bytes, 0x3C);
            int coff = peOffset + 4;
            int optional = coff + 20;
            int sectionCount = BitConverter.ToUInt16(bytes, coff + 2);
            int optionalSize = BitConverter.ToUInt16(bytes, coff + 16);
            int sectionTable = optional + optionalSize;
            uint exportRva = BitConverter.ToUInt32(bytes, optional + 96);
            uint exportSize = BitConverter.ToUInt32(bytes, optional + 100);
            int exportOffset = RvaToFileOffset(bytes, optional, exportRva);
            uint functionCount = BitConverter.ToUInt32(bytes, exportOffset + 20);
            uint nameCount = BitConverter.ToUInt32(bytes, exportOffset + 24);
            int functionTable = RvaToFileOffset(bytes, optional, BitConverter.ToUInt32(bytes, exportOffset + 28));
            int nameTable = RvaToFileOffset(bytes, optional, BitConverter.ToUInt32(bytes, exportOffset + 32));
            int ordinalTable = RvaToFileOffset(bytes, optional, BitConverter.ToUInt32(bytes, exportOffset + 36));
            int ordinal = -1;
            for (int index = 0; index < nameCount; index++)
            {
                int nameOffset = RvaToFileOffset(bytes, optional, BitConverter.ToUInt32(bytes, nameTable + index * 4));
                int end = Array.IndexOf(bytes, (byte)0, nameOffset);
                if (end > nameOffset && Encoding.ASCII.GetString(bytes, nameOffset, end - nameOffset) == exportName)
                {
                    ordinal = BitConverter.ToUInt16(bytes, ordinalTable + index * 2);
                    break;
                }
            }
            Assert.InRange(ordinal, 0, checked((int)functionCount - 1));

            uint SectionRva(string name)
            {
                for (int index = 0; index < sectionCount; index++)
                {
                    int entry = sectionTable + index * 40;
                    string actual = Encoding.ASCII.GetString(bytes, entry, 8).TrimEnd('\0');
                    if (actual == name)
                        return BitConverter.ToUInt32(bytes, entry + 12);
                }
                throw new Xunit.Sdk.XunitException($"PE fixture has no {name} section.");
            }

            uint targetRva = targetKind switch
            {
                "data" => SectionRva(".data"),
                "rdata" => SectionRva(".rdata"),
                "null" => 0u,
                "unmapped" => 0x7FFF0000u,
                "forwarder" => checked(exportRva + Math.Min(exportSize - 1, 4u)),
                _ => throw new ArgumentOutOfRangeException(nameof(targetKind)),
            };
            BitConverter.GetBytes(targetRva).CopyTo(bytes, functionTable + ordinal * 4);
        }

        private static void MoveExportMetadataAcrossRdataBoundary(byte[] bytes, string rangeKind)
        {
            int peOffset = BitConverter.ToInt32(bytes, 0x3C);
            int optional = peOffset + 24;
            int sectionTable = optional + BitConverter.ToUInt16(bytes, peOffset + 20);
            int rdata = sectionTable + 40;
            uint rdataRva = BitConverter.ToUInt32(bytes, rdata + 12);
            int rdataRawSize = checked((int)BitConverter.ToUInt32(bytes, rdata + 16));
            int rdataRaw = checked((int)BitConverter.ToUInt32(bytes, rdata + 20));
            int rawEnd = checked(rdataRaw + rdataRawSize);
            int exportOffset = RvaToFileOffset(
                bytes,
                optional,
                BitConverter.ToUInt32(bytes, optional + 96));
            uint textRva = BitConverter.ToUInt32(bytes, sectionTable + 12);

            switch (rangeKind)
            {
                case "function-table":
                    BitConverter.GetBytes(rdataRva + (uint)rdataRawSize - 4).CopyTo(bytes, exportOffset + 28);
                    BitConverter.GetBytes(textRva).CopyTo(bytes, rawEnd - 4);
                    BitConverter.GetBytes(textRva + 0x10).CopyTo(bytes, rawEnd);
                    break;
                case "name-table":
                    BitConverter.GetBytes(rdataRva + (uint)rdataRawSize - 4).CopyTo(bytes, exportOffset + 32);
                    BitConverter.GetBytes(rdataRva + 0x60).CopyTo(bytes, rawEnd - 4);
                    BitConverter.GetBytes(rdataRva + 0x70).CopyTo(bytes, rawEnd);
                    break;
                case "ordinal-table":
                    BitConverter.GetBytes(rdataRva + (uint)rdataRawSize - 2).CopyTo(bytes, exportOffset + 36);
                    BitConverter.GetBytes((ushort)0).CopyTo(bytes, rawEnd - 2);
                    BitConverter.GetBytes((ushort)1).CopyTo(bytes, rawEnd);
                    break;
                case "export-name":
                    int nameTable = RvaToFileOffset(
                        bytes,
                        optional,
                        BitConverter.ToUInt32(bytes, exportOffset + 32));
                    int start = rawEnd - 4;
                    BitConverter.GetBytes(rdataRva + (uint)rdataRawSize - 4).CopyTo(bytes, nameTable);
                    Encoding.ASCII.GetBytes("InitMagicPRX\0").CopyTo(bytes, start);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(rangeKind));
            }
        }
    }

    // ── Self-contained Magic PE fixture ───────────────────────────────────────────────
    // The release gate must run in a clean checkout without a private Steam/extraction corpus. This
    // builder emits a small PE32 DLL with executable exports, an ordered .rdata handler catalog, and
    // one structurally valid PPP root whose records exercise the reviewed embedded schemas.
    internal static class MagicDllTestFixture
    {
        private static readonly object Sync = new();
        private static readonly string Root = Path.Combine(
            FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot + "/work",
            $"ffx_magic_generated_{Environment.ProcessId}");

        internal static string GetPath(string dllName)
        {
            lock (Sync)
            {
                TestDirectory.CreatePrivate(Root);
                string path = Path.Combine(Root, dllName);
                if (!File.Exists(path))
                    File.WriteAllBytes(path, CreateBytes(ParseId(dllName)));
                return path;
            }
        }

        internal static string Write(string directory, string dllName)
        {
            TestDirectory.CreatePrivate(directory);
            string path = Path.Combine(directory, dllName);
            File.WriteAllBytes(path, CreateBytes(ParseId(dllName)));
            return path;
        }

        // Preview sessions resolve each parsed root through a resource-header chain whose
        // entries are absolute .data offsets ≥ 64, so a root at .data+0 can never match.
        // The publishable variant relocates the same PPP root to 0x1000 and links it from
        // a minimal header at .data+0; every other byte is identical to CreateBytes.
        internal static string WritePublishable(string directory, string dllName)
        {
            TestDirectory.CreatePrivate(directory);
            string path = Path.Combine(directory, dllName);
            File.WriteAllBytes(path, CreateBytes(ParseId(dllName), publishable: true));
            return path;
        }

        internal static byte[] ReadBytes(string dllName) => CreateBytes(ParseId(dllName));

        private static int ParseId(string dllName)
        {
            int id = MagicDllFile.ParseMagicIdFromName(dllName);
            return id >= 0 ? id : 21;
        }

        private static byte[] CreateBytes(int magicId, bool publishable = false)
        {
            const int PeOffset = 0x80;
            const int OptionalOffset = PeOffset + 24;
            const int OptionalSize = 0xE0;
            const int SectionTable = OptionalOffset + OptionalSize;
            const int TextRaw = 0x200;
            const int RdataRaw = 0x400;
            const int DataRaw = 0xC00;
            const int DataSize = 0x6000;
            const uint TextRva = 0x1000;
            const uint RdataRva = 0x2000;
            const uint DataRva = 0x3000;
            byte[] bytes = new byte[DataRaw + DataSize];

            WriteU16(bytes, 0, 0x5A4D);
            WriteI32(bytes, 0x3C, PeOffset);
            WriteU32(bytes, PeOffset, 0x00004550);
            WriteU16(bytes, PeOffset + 4, 0x014C);
            WriteU16(bytes, PeOffset + 6, 3);
            WriteU16(bytes, PeOffset + 20, OptionalSize);
            WriteU16(bytes, PeOffset + 22, 0x2102);
            WriteU16(bytes, OptionalOffset, 0x010B);
            WriteU32(bytes, OptionalOffset + 92, 16);
            WriteU32(bytes, OptionalOffset + 96, RdataRva);
            WriteU32(bytes, OptionalOffset + 100, 0x180);

            WriteSection(bytes, SectionTable, ".text", 0x200, TextRva, 0x200, TextRaw, 0x60000020);
            WriteSection(bytes, SectionTable + 40, ".rdata", 0x800, RdataRva, 0x800, RdataRaw, 0x40000040);
            WriteSection(bytes, SectionTable + 80, ".data", DataSize, DataRva, DataSize, DataRaw, 0xC0000040);
            bytes[TextRaw] = 0xC3;
            bytes[TextRaw + 0x10] = 0xC3;

            WriteU32(bytes, RdataRaw + 20, 2);
            WriteU32(bytes, RdataRaw + 24, 2);
            WriteU32(bytes, RdataRaw + 28, RdataRva + 0x40);
            WriteU32(bytes, RdataRaw + 32, RdataRva + 0x48);
            WriteU32(bytes, RdataRaw + 36, RdataRva + 0x50);
            WriteU32(bytes, RdataRaw + 0x40, TextRva);
            WriteU32(bytes, RdataRaw + 0x44, TextRva + 0x10);
            WriteU32(bytes, RdataRaw + 0x48, RdataRva + 0x60);
            WriteU32(bytes, RdataRaw + 0x4C, RdataRva + 0x70);
            WriteU16(bytes, RdataRaw + 0x50, 0);
            WriteU16(bytes, RdataRaw + 0x52, 1);
            WriteAsciiZ(bytes, RdataRaw + 0x60, "InitMagicPRX");
            WriteAsciiZ(bytes, RdataRaw + 0x70, "GetEffectOverlayTable");

            IReadOnlyList<string> handlers = HandlerNames(magicId);
            int handlerOffset = RdataRaw + 0x100;
            foreach (string handler in handlers)
            {
                WriteAsciiZ(bytes, handlerOffset, handler);
                handlerOffset += handler.Length + 1;
            }

            int root = DataRaw + (publishable ? 0x1000 : 0);
            if (publishable)
            {
                // header@0: +60 → resource @0x800; resource +80 = count 1, +32 → table @0x860;
                // table[0] = 0x800 resolves the root at .data offset 0x1000 (start + 0x800).
                WriteU32(bytes, DataRaw + 60, 0x800);
                WriteU16(bytes, DataRaw + 0x800 + 80, 1);
                WriteU32(bytes, DataRaw + 0x800 + 32, 96);
                WriteU32(bytes, DataRaw + 0x860, 0x800);
            }
            WriteU32(bytes, root, 0x31);
            WriteU16(bytes, root + 6, 1);
            WriteI32(bytes, root + 16, 32);
            WriteI32(bytes, root + 20, 36);
            WriteI32(bytes, root + 24, 40);
            WriteI32(bytes, root + 28, 44);
            WriteI32(bytes, root + 32, 64);
            int section = root + 64;
            WriteI32(bytes, section, 0x5000);
            WriteI32(bytes, section + 8, 0x4F00);
            WriteI32(bytes, section + 12, 0x4F04);
            int program = section + 16;
            WriteU32(bytes, program + 4, 0x040C);
            int slotCount = handlers.Count * 2;
            WriteU16(bytes, program + 38, checked((ushort)slotCount));
            for (int index = 0; index < slotCount; index++)
            {
                int handlerIndex = index / 2;
                if (magicId == 117 && index == 11)
                    handlerIndex = 22;
                int slot = program + 40 + index * 16;
                WriteU32(bytes, slot, checked((uint)handlerIndex));
                WriteU16(bytes, slot + 4, 0x40);
                WriteU16(bytes, slot + 6, 1);
                WriteU32(bytes, slot + 8, checked((uint)(0x800 + index * 0x80)));
            }
            WriteU32(bytes, section + 0x4F00, 0);
            WriteU32(bytes, section + 0x4F04, 0);

            byte[] identity = Encoding.Unicode.GetBytes($"magic_{magicId:D4}.dll\0magic_{magicId:D4}\0");
            identity.CopyTo(bytes, DataRaw + 0x5800);
            return bytes;
        }

        private static IReadOnlyList<string> HandlerNames(int magicId)
        {
            IReadOnlyDictionary<int, string>? known = MagicKnownEffectHandlers.TryGet(magicId);
            if (known != null)
                return known.OrderBy(entry => entry.Key).Select(entry => entry.Value).ToArray();
            return new[]
            {
                "pppAccele", "pppAngAccele", "pppSclAccele", "pppMove",
                "pppAngMove", "pppSclMove", "pppPoint", "pppAngle",
                "pppScale", "pppColor", "pppRandFV", "pppRandIV",
                "pppSMatrix", "pppMatrixXYZ", "pppMatrixYXZ", "pppMatrixLoc",
                "pppMatrixScl", "pppDrawMatrix", "pppKeTh", "pppDrawMdl",
                "pppDrawMdlTs", "pppDrawShape", "pppDrawMatrixFront", "pppKeMdlDtt",
                "pppVertexAp", "pppKeBornRnd3", "pppKeBornRnd6", "pppKeThRes32",
                "pppKeDrct", "pppColMove", "pppRandUpFV", "pppSRandFV",
            };
        }

        private static void WriteSection(
            byte[] bytes,
            int offset,
            string name,
            uint virtualSize,
            uint virtualAddress,
            uint rawSize,
            uint rawOffset,
            uint characteristics)
        {
            Encoding.ASCII.GetBytes(name).CopyTo(bytes, offset);
            WriteU32(bytes, offset + 8, virtualSize);
            WriteU32(bytes, offset + 12, virtualAddress);
            WriteU32(bytes, offset + 16, rawSize);
            WriteU32(bytes, offset + 20, rawOffset);
            WriteU32(bytes, offset + 36, characteristics);
        }

        private static void WriteAsciiZ(byte[] bytes, int offset, string value)
        {
            Encoding.ASCII.GetBytes(value).CopyTo(bytes, offset);
            bytes[offset + value.Length] = 0;
        }

        private static void WriteU16(byte[] bytes, int offset, ushort value) =>
            BitConverter.GetBytes(value).CopyTo(bytes, offset);

        private static void WriteU32(byte[] bytes, int offset, uint value) =>
            BitConverter.GetBytes(value).CopyTo(bytes, offset);

        private static void WriteI32(byte[] bytes, int offset, int value) =>
            BitConverter.GetBytes(value).CopyTo(bytes, offset);
    }
}
