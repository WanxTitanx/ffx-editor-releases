using System;
using System.IO;
using System.Linq;
using System.Globalization;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Modules.MagicDllEditor;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll;

public class MagicEgoMatrixContractTests
{
    private static MagicDllFile Load() => MagicDllFile.Load(Path.Combine(TestDataPaths.MagicCorpus, "magic_0070.dll"));
    private static MagicDllFile Copy(MagicDllFile file, byte[] bytes) => new("copy.dll", bytes,
        bytes.AsSpan(file.DataSectionRawPtr, file.DataSectionSize).ToArray(),
        file.DataSectionRawPtr, file.DataSectionSize, file.DataSectionRva, file.Sections, file.Roots);

    [Fact]
    public void StandardMatrix_RequiresReviewedCodeAndRotationTable()
    {
        var file = Load();
        var node = Assert.Single(MagicEgoDescriptorInfo.Read(file).Where(t => t.Name == "EgoStdMat"));
        Assert.Equal(MagicEgoCallbackKind.ComposeStandardMatrix, node.CallbackKinds[1]);
        Assert.Equal(file.FileBytes, File.ReadAllBytes(file.SourcePath));
    }

    [Fact]
    public void ChangedDataFunctionTable_RemovesMatrixMeaningButPreservesUnrelatedCode()
    {
        var file = Load();
        byte[] bytes = (byte[])file.FileBytes.Clone();
        bytes[file.RvaToOffset(0x10558)] ^= 1;
        var nodes = MagicEgoDescriptorInfo.Read(Copy(file, bytes));
        Assert.Equal(MagicEgoCallbackKind.Unknown, Assert.Single(nodes.Where(t => t.Name == "EgoStdMat")).CallbackKinds[1]);
        Assert.Equal(MagicEgoCallbackKind.AddIntegerVector, Assert.Single(nodes.Where(t => t.Name == "EgoAddIVec")).CallbackKinds[1]);
    }

    [Fact]
    public void MissingTableRelocation_RemovesOnlyMatrixContract()
    {
        var file = Load();
        byte[] bytes = (byte[])file.FileBytes.Clone();
        int pe = BitConverter.ToInt32(bytes, 0x3C);
        int at = file.RvaToOffset((int)BitConverter.ToUInt32(bytes, pe + 160));
        int end = at + (int)BitConverter.ToUInt32(bytes, pe + 164);
        bool removed = false;
        while (at < end && !removed)
        {
            uint page = BitConverter.ToUInt32(bytes, at);
            int size = (int)BitConverter.ToUInt32(bytes, at + 4);
            for (int slot = at + 8; slot < at + size; slot += 2)
            {
                ushort value = BitConverter.ToUInt16(bytes, slot);
                if (value >> 12 != 3 || page + (uint)(value & 0xFFF) != 0x10558) continue;
                BitConverter.GetBytes((ushort)(value & 0xFFF)).CopyTo(bytes, slot);
                removed = true;
                break;
            }
            at += size;
        }
        Assert.True(removed);
        var nodes = MagicEgoDescriptorInfo.Read(Copy(file, bytes));
        Assert.Equal(MagicEgoCallbackKind.Unknown, Assert.Single(nodes.Where(t => t.Name == "EgoStdMat")).CallbackKinds[1]);
        Assert.Equal(MagicEgoCallbackKind.AddIntegerVector, Assert.Single(nodes.Where(t => t.Name == "EgoAddIVec")).CallbackKinds[1]);
    }

    [Fact]
    public void ChangedMatrixConstant_RemovesMatrixMeaningWithUnchangedCodeAndTable()
    {
        var file = Load();
        byte[] bytes = (byte[])file.FileBytes.Clone();
        bytes[file.RvaToOffset(0xF3F8)] ^= 1;
        var nodes = MagicEgoDescriptorInfo.Read(Copy(file, bytes));
        Assert.Equal(MagicEgoCallbackKind.Unknown, Assert.Single(nodes.Where(t => t.Name == "EgoStdMat")).CallbackKinds[1]);
        Assert.Equal(MagicEgoCallbackKind.AddIntegerVector, Assert.Single(nodes.Where(t => t.Name == "EgoAddIVec")).CallbackKinds[1]);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("pt")]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("it")]
    [InlineData("ja")]
    [InlineData("ko")]
    [InlineData("zh")]
    public void MatrixOperation_IsLocalizedAndReadOnly(string language)
    {
        var prior = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            var root = new MagicEgoDescriptorNode(MagicEgoDescriptorInfo.Read(Load()));
            var matrix = Assert.Single(root.Children.Where(n => n.Label == "EgoStdMat"));
            var callback = matrix.Children[3];
            Assert.Contains(Strings.U_Md_EgoOpComposeStandardMatrix, callback.MetaText);
            Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.U_Md_EgoOperationHint,
                Strings.U_Md_EgoOpComposeStandardMatrix), callback.DetailText);
            Assert.False(callback.IsValueEditable);
        }
        finally { CultureInfo.CurrentUICulture = prior; }
    }
}
