using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;
using FFXProjectEditor.Modules.MagicDllEditor;

namespace FFXProjectEditor.Tests.MagicDll;

public class MagicResourceBindingTests
{
    [Fact]
    public void Directory_PreservesResourceOrderRatherThanSortingRootAddresses()
    {
        byte[] data = MakeDirectory();
        Assert.True(MagicPppResourceBindings.TryReadDirectory(data, 0, out var roots));
        Assert.Equal(new[] { 0x400, 0x300 }, roots);
    }

    [Theory]
    [InlineData(60, uint.MaxValue)]
    [InlineData(0x100 + 32, uint.MaxValue)]
    [InlineData(0x160, uint.MaxValue)]
    [InlineData(0x160, 0u)]
    [InlineData(0x160, 0x301u)]
    [InlineData(0x100 + 80, 65u)]
    public void MalformedResourceDirectory_IsRejected(int offset, uint value)
    {
        byte[] data = MakeDirectory();
        BitConverter.GetBytes(value).CopyTo(data, offset);
        Assert.False(MagicPppResourceBindings.TryReadDirectory(data, 0, out var roots));
        Assert.Empty(roots);
    }

    private static byte[] MakeDirectory()
    {
        var data = new byte[0x800];
        BitConverter.GetBytes(0x100u).CopyTo(data, 60);
        BitConverter.GetBytes((ushort)2).CopyTo(data, 80);
        BitConverter.GetBytes((ushort)2).CopyTo(data, 0x100 + 80);
        BitConverter.GetBytes(96u).CopyTo(data, 0x100 + 32);
        BitConverter.GetBytes(0x300u).CopyTo(data, 0x160);
        BitConverter.GetBytes(0x200u).CopyTo(data, 0x164);
        return data;
    }

    [Fact]
    public void ChangedCode_DoesNotInheritAnAuditedResourceBinding()
    {
        var file = MagicDllFile.Load(Path.Combine(TestDataPaths.MagicCorpus, "magic_0069.dll"));
        var tables = MagicFpHandlerTable.TryExtractFromDll(file);
        Assert.NotEmpty(MagicPppResourceBindings.Resolve(file, tables));
        file.FileBytes[file.Sections.Single(s => s.Name == ".text").RawPtr] ^= 1;
        Assert.Empty(MagicPppResourceBindings.Resolve(file, tables));
    }

    [Fact]
    public void AddedCodeRelocation_CannotReuseTheSameNormalizedCodeIdentity()
    {
        var file = MagicDllFile.Load(Path.Combine(TestDataPaths.MagicCorpus, "magic_0069.dll"));
        var tables = MagicFpHandlerTable.TryExtractFromDll(file);
        Assert.NotEmpty(MagicPppResourceBindings.Resolve(file, tables));
        byte[] bytes = file.FileBytes;
        int optional = BitConverter.ToInt32(bytes, 0x3C) + 24;
        int start = file.RvaToOffset(BitConverter.ToInt32(bytes, optional + 136));
        int end = start + BitConverter.ToInt32(bytes, optional + 140);
        var text = file.Sections.Single(s => s.Name == ".text");
        bool changed = false;
        for (int block = start; block < end;)
        {
            int page = BitConverter.ToInt32(bytes, block);
            int next = block + BitConverter.ToInt32(bytes, block + 4);
            ushort existing = 0;
            int padding = -1;
            for (int at = block + 8; at < next; at += 2)
            {
                ushort value = BitConverter.ToUInt16(bytes, at);
                int operand = page + (value & 0xFFF);
                if (value >> 12 == 3 && operand >= text.VirtualAddress && operand < text.VirtualAddress + text.RawSize)
                    existing = value;
                if (value == 0) padding = at;
            }
            if (existing != 0 && padding >= 0)
            {
                BitConverter.GetBytes(existing).CopyTo(bytes, padding);
                changed = true;
                break;
            }
            block = next;
        }
        Assert.True(changed, "The native fixture must have an ABSOLUTE padding entry in a code relocation block.");
        Assert.Empty(MagicPppResourceBindings.Resolve(file, tables));
    }

    [Fact]
    public void RelocatedDeclarationPointer_CannotSelectAnInvalidDirectory()
    {
        var file = MagicDllFile.Load(Path.Combine(TestDataPaths.MagicCorpus, "magic_0069.dll"));
        var tables = MagicFpHandlerTable.TryExtractFromDll(file);
        Assert.NotEmpty(MagicPppResourceBindings.Resolve(file, tables));
        BitConverter.GetBytes(0xFFFFFFFFu).CopyTo(file.FileBytes, file.RvaToOffset(0x16DD));
        Assert.Empty(MagicPppResourceBindings.Resolve(file, tables));
    }

    [Fact]
    public void RenamedCopy_WithPayloadEdit_KeepsTheResourceBindings()
    {
        string path = Path.Combine(TestDataPaths.MagicCorpus, "magic_0069.dll");
        var parser = new MagicDllParser(new MagicDllParserOptions { FieldMap = MagicFieldMap.LoadEmbedded() });
        var source = parser.Parse(path);
        var slot = source.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots)
            .First(s => s.OpcodeName == "pppAngle" && s.NativeRecordWidth == 32);
        byte[] copy = (byte[])source.FileBytes.Clone();
        BitConverter.GetBytes(1024).CopyTo(copy, source.DataSectionRawPtr + slot.RecordOffset + 16);
        Assert.True(parser.TryParseSnapshot(copy, Path.Combine(Path.GetDirectoryName(path)!, "magic_9876.dll"),
            out var renamed, out var error), error);
        Assert.Equal(9876, renamed!.MagicId);
        Assert.All(renamed.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots),
            s => Assert.False(string.IsNullOrEmpty(s.OpcodeName)));
        Assert.Equal(source.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots).Select(s => s.OpcodeName),
            renamed.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots).Select(s => s.OpcodeName));
        Assert.Equal(source.FileBytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Writer_EditsTheSecondResourceAndPreservesAllOtherBytes()
    {
        string source = Path.Combine(TestDataPaths.MagicCorpus, "magic_0069.dll");
        var document = new MagicDllDocument_Wrapper();
        Assert.True(document.TryLoad(source, out var error), error);
        var slot = document.ParsedFile!.Roots[1].Programs.SelectMany(p => p.Slots)
            .First(s => s.OpcodeName == "pppAngle" && s.NativeRecordWidth == 32);
        var field = document.ResolveKeyframeFields(slot, 0).Single(f => f.Offset == 16);
        byte[] original = File.ReadAllBytes(source);
        byte[] expected = (byte[])original.Clone();
        byte[] value = BitConverter.GetBytes(1024);
        Assert.True(document.TryApplyFieldEdit(field, value, out error), error);
        value.CopyTo(expected, document.ParsedFile.DataSectionRawPtr + field.RecordOffset + field.Offset);
        string dir = Path.Combine(TestDataPaths.RepoRoot, "work", "magic-editor-completion-20261003",
            "resource-binding-tests", Guid.NewGuid().ToString("N"));
        TestDirectory.CreatePrivate(dir);
        try
        {
            string destination = Path.Combine(dir, "magic_9876.dll");
            Assert.True(document.TrySaveCopy(destination, out error), error);
            Assert.Equal(expected, File.ReadAllBytes(destination));
            Assert.Equal(original, File.ReadAllBytes(source));
            var reloaded = new MagicDllParser().Parse(destination);
            Assert.All(reloaded.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots),
                s => Assert.False(string.IsNullOrEmpty(s.OpcodeName)));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData(69)]
    [InlineData(122)]
    [InlineData(208)]
    [InlineData(244)]
    [InlineData(304)]
    [InlineData(539)]
    [InlineData(568)]
    [InlineData(640)]
    [InlineData(752)]
    public void NativeResourceBindings_ResolveEveryInstanceWithoutWidthGuessing(int id)
    {
        string path = Path.Combine(TestDataPaths.MagicCorpus, $"magic_{id:0000}.dll");
        byte[] original = File.ReadAllBytes(path);
        var parser = new MagicDllParser(new MagicDllParserOptions { FieldMap = MagicFieldMap.LoadEmbedded() });
        var file = parser.Parse(path);
        var slots = file.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots).ToArray();
        Assert.NotEmpty(slots);
        Assert.All(slots, s => Assert.False(string.IsNullOrEmpty(s.OpcodeName),
            $"{file.DllName} slot 0x{s.SlotAbs:X} index {s.HandlerTableIndex} remains unresolved"));
        Assert.All(slots.Where(s => s.OpcodeName is "pppScale" or "pppMove" or "pppAngle"),
            s => Assert.Equal(32, s.NativeRecordWidth));
        Assert.Equal(original, file.Serialize());
        Assert.Equal(original, File.ReadAllBytes(path));
    }
}
