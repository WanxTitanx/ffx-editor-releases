using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Modules.MagicDllEditor;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll;

public class MagicShapeResourceContractTests
{
    [Theory]
    [InlineData("frame")]
    [InlineData("colors")]
    [InlineData("counts")]
    public void InvalidFlipbookStructure_DoesNotBecomeAResourceChoice(string mutation)
    {
        var file = new MagicDllParser().Parse(Path.Combine(TestDataPaths.MagicCorpus, "magic_0003.dll"));
        byte[] original = (byte[])file.Data.Clone(), changed = (byte[])file.Data.Clone();
        var root = file.Roots[0];
        int table = root.RootAbs + root.Table3Rel;
        int at = root.RootAbs + (int)BitConverter.ToUInt32(changed, table);
        int packet = at + BitConverter.ToUInt16(changed, at + 16);
        if (mutation == "frame") BitConverter.GetBytes(ushort.MaxValue).CopyTo(changed, at + 16);
        else if (mutation == "colors") BitConverter.GetBytes(uint.MaxValue).CopyTo(changed, packet + 4);
        else BitConverter.GetBytes(short.MaxValue).CopyTo(changed, at + 6);
        var layouts = MagicShapeResourceLayouts.Read(changed, file.Roots, new List<(int,int)>(), new List<(int,long)>());
        Assert.False(layouts.ContainsKey((root.RootAbs, 0u)));
        Assert.Equal(original, file.Data);
        Assert.Equal(file.FileBytes, File.ReadAllBytes(file.SourcePath));
    }

    [Fact]
    public void ChangedFlipbookMetadata_RollsBackAPendingShapeReferenceEdit()
    {
        var file = new MagicDllParser().Parse(Path.Combine(TestDataPaths.MagicCorpus, "magic_0003.dll"));
        var snapshot = new MagicPrefixSnapshot(file, new MagicGeometrySnapshot(file));
        var field = snapshot.Fields.First(f => f.Opcode == "pppDrawShape" && f.IsEditable &&
            f.OriginalBits != MagicPrefixSnapshot.DisabledResource);
        var root = file.Roots[0];
        int resource = root.RootAbs + (int)BitConverter.ToUInt32(file.Data,
            root.RootAbs + root.Table3Rel + (int)field.OriginalBits * 4);
        byte[] bytes = (byte[])file.FileBytes.Clone();
        bytes[file.DataSectionRawPtr + resource + 4] ^= 1;
        byte[] before = (byte[])bytes.Clone();
        Assert.False(snapshot.TryApply(bytes, new[] { (field, MagicPrefixSnapshot.DisabledResource) }, out _, out _));
        Assert.Equal(before, bytes);
        Assert.Equal(file.FileBytes, File.ReadAllBytes(file.SourcePath));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(40)]
    public void ShapeResourceChoices_UseTheNativeFlipbookTableInsteadOfGeometryDescriptors(int id)
    {
        var document = new MagicDllDocument_Wrapper();
        Assert.True(document.TryLoad(Path.Combine(TestDataPaths.MagicCorpus, $"magic_{id:0000}.dll"), out var error), error);
        var file = document.ParsedFile!;
        var fields = document.PrefixOperands.Where(f => f.Opcode == "pppDrawShape").ToArray();
        Assert.NotEmpty(fields);
        foreach (var field in fields)
        {
            var root = file.Roots.Single(r => r.Programs.SelectMany(p => p.Slots)
                .Any(s => s.OpcodeName == "pppDrawShape" && s.Keyframes.Any(k => k.RecordOffset == field.RecordOffset)));
            Assert.All(field.ResourceChoices.Where(i => i != MagicPrefixSnapshot.DisabledResource && i != field.OriginalBits),
                i => Assert.True(i < root.Aux3Count, $"Choice {i} is a descriptor index outside the native shape table."));
            if (field.IsEditable)
            {
                int frameCount(uint index)
                {
                    int at = root.RootAbs + root.Table3Rel + checked((int)index * 4);
                    int resource = root.RootAbs + checked((int)BitConverter.ToUInt32(file.Data, at));
                    return BitConverter.ToInt16(file.Data, resource + 6);
                }
                foreach (uint index in field.ResourceChoices.Where(i => i != MagicPrefixSnapshot.DisabledResource))
                {
                    int pointerAt = root.RootAbs + root.Table3Rel + checked((int)index * 4);
                    uint relative = BitConverter.ToUInt32(file.Data, pointerAt);
                    Assert.NotEqual(0u, relative);
                    if (field.OriginalBits != MagicPrefixSnapshot.DisabledResource)
                        Assert.Equal(frameCount(field.OriginalBits), frameCount(index));
                }
            }
        }
    }
}
