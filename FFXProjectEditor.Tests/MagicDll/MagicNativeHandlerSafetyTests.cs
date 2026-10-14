using System;
using System.IO;
using System.Linq;
using System.Text;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll;

public class MagicNativeHandlerSafetyTests
{
    [Fact]
    public void AdjacentTables_WithSeparateRelocatedReferences_DoNotShareIndices()
    {
        using var fixture = new NativeFixture();
        fixture.WriteTable(0x5100, 0, 1);
        fixture.WriteTable(0x5150, 0, 2);
        fixture.ReferenceTables(0x5100, 0x5150);
        var table = fixture.Extract();
        Assert.Equal(2, table.NativeTableCount);
        Assert.Equal(fixture.Names[0], table.Resolve(0));
        Assert.Null(table.Resolve(1));
        Assert.Null(table.Resolve(2));
    }

    [Fact]
    public void MultipleTables_ResolveOnlyIndicesSharedByEveryTable()
    {
        using var fixture = new NativeFixture();
        fixture.WriteTable(0x5100, 0, 1, 2, 3);
        fixture.WriteTable(0x5300, 0, 2, 1);
        var table = fixture.Extract();
        Assert.Equal(fixture.Names[0], table.Resolve(0));
        Assert.Null(table.Resolve(1));
        Assert.Null(table.Resolve(2));
        Assert.Null(table.Resolve(3));
        Assert.Equal(2, table.NativeTableCount);
    }

    [Fact]
    public void NativeTable_PreservesDuplicateEntriesAndNullCallbacks()
    {
        using var fixture = new NativeFixture();
        fixture.WriteTable(0x5100, 1, 1, 0);
        var table = fixture.Extract();
        Assert.Equal(3, table.Count);
        Assert.Equal(fixture.Names[1], table.Resolve(0));
        Assert.Equal(fixture.Names[1], table.Resolve(1));
        Assert.Equal(fixture.Names[0], table.Resolve(2));
    }

    [Fact]
    public void NativeNames_CannotCrossRdataBoundary()
    {
        using var fixture = new NativeFixture();
        fixture.WriteTable(0x5100, 0, 1);
        int at = fixture.Rdata.RawPtr + fixture.Rdata.RawSize - 4;
        Encoding.ASCII.GetBytes("pppXY\0").CopyTo(fixture.Bytes, at);
        uint pointer = fixture.Address(at, fixture.Rdata);
        fixture.WriteUInt(fixture.DataRaw + 0x5100, pointer);
        fixture.WriteUInt(fixture.DataRaw + 0x5100 + 40, pointer);
        Assert.DoesNotContain("native PPP entries", fixture.Extract().SourcePath);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Parser_UsesFpFileOnlyWhenNativeTableIsAbsent(bool nativeTable)
    {
        using var fixture = new NativeFixture();
        if (nativeTable) fixture.WriteTable(0x5100, 1, 0);
        fixture.Save();
        File.WriteAllText(Path.Combine(fixture.DirectoryPath, "fp.h"),
            "pppProgTbl_FP[]={\nPPMPN(\"pppWrongFamily\")\n};\n");
        var parser = new MagicDllParser(new MagicDllParserOptions
        {
            FpDirectoryPath = fixture.DirectoryPath,
            FieldMap = MagicFieldMap.LoadEmbedded()
        });
        Assert.True(parser.TryParse(fixture.DllPath, out var file, out var error), error);
        var slots = file!.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots)
            .Where(s => s.HandlerTableIndex == 0).ToArray();
        Assert.NotEmpty(slots);
        Assert.All(slots, slot => Assert.Equal(nativeTable ? fixture.Names[1] : "pppWrongFamily", slot.OpcodeName));
    }

    private sealed class NativeFixture : IDisposable
    {
        private const uint ImageBase = 0x10000000;
        public string DirectoryPath { get; }
        public string DllPath { get; }
        public byte[] Bytes { get; }
        public int DataRaw { get; }
        public MagicPeSection Rdata { get; }
        public string[] Names { get; }
        private readonly int[] _nameOffsets;
        private readonly MagicPeSection _data;

        public NativeFixture()
        {
            DirectoryPath = Path.Combine(TestDataPaths.RepoRoot, "work",
                "magic-editor-completion-20261003", "handler-tests", Guid.NewGuid().ToString("N"));
            TestDirectory.CreatePrivate(DirectoryPath);
            DllPath = MagicDllTestFixture.Write(DirectoryPath, "magic_0021.dll");
            var file = MagicDllFile.Load(DllPath);
            Bytes = file.FileBytes;
            DataRaw = file.DataSectionRawPtr;
            _data = file.Sections.Single(s => s.Name == ".data");
            Rdata = file.Sections.Single(s => s.Name == ".rdata");
            Names = MagicFpHandlerTable.TryExtractFromDll(file)!.Names.Values.ToArray();
            _nameOffsets = Names.Select(n => Find(Bytes, Encoding.ASCII.GetBytes(n + "\0"))).ToArray();
            WriteUInt(BitConverter.ToInt32(Bytes, 0x3C) + 24 + 28, ImageBase);
        }

        public uint Address(int offset, MagicPeSection section) =>
            ImageBase + (uint)section.VirtualAddress + (uint)(offset - section.RawPtr);

        public void WriteUInt(int offset, uint value) => BitConverter.GetBytes(value).CopyTo(Bytes, offset);

        public void WriteTable(int dataOffset, params int[] indices)
        {
            int at = DataRaw + dataOffset;
            Array.Clear(Bytes, at, (indices.Length + 1) * 40);
            for (int i = 0; i < indices.Length; i++)
                WriteUInt(at + i * 40, Address(_nameOffsets[indices[i]], Rdata));
        }

        public void Save() => File.WriteAllBytes(DllPath, Bytes);

        public void ReferenceTables(int first, int second)
        {
            WriteUInt(DataRaw + 0x5500, Address(DataRaw + first, _data));
            WriteUInt(DataRaw + 0x5504, Address(DataRaw + second, _data));
            int optional = BitConverter.ToInt32(Bytes, 0x3C) + 24;
            WriteUInt(optional + 92, 16);
            WriteUInt(optional + 136, (uint)_data.VirtualAddress + 0x5600);
            WriteUInt(optional + 140, 12);
            uint page = ((uint)_data.VirtualAddress + 0x5500) & ~0xFFFu;
            WriteUInt(DataRaw + 0x5600, page);
            WriteUInt(DataRaw + 0x5604, 12);
            for (int i = 0; i < 2; i++)
                BitConverter.GetBytes((ushort)(0x3000 | (((uint)_data.VirtualAddress + 0x5500 + (uint)(4 * i)) & 0xFFF)))
                    .CopyTo(Bytes, DataRaw + 0x5608 + 2 * i);
        }

        public MagicFpHandlerTable Extract()
        {
            Save();
            return MagicFpHandlerTable.TryExtractFromDll(MagicDllFile.Load(DllPath))!;
        }

        public void Dispose() => Directory.Delete(DirectoryPath, true);

        private static int Find(byte[] haystack, byte[] needle)
        {
            for (int i = 0; i <= haystack.Length - needle.Length; i++)
                if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle)) return i;
            throw new InvalidOperationException("Fixture string missing");
        }
    }
}
