using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Threading.Tasks;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Tests.Infrastructure;
using FFXProjectEditor.Modules.MagicDllEditor;
using FFXProjectEditor.Resources;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll;

public class MagicEgoDescriptorTests
{
    [Fact]
    public void Current0387_UsesActualDescriptorOffsetsAndTypedCallbackCells()
    {
        var file = MagicDllFile.Load(Path.Combine(TestDataPaths.MagicCorpus, "magic_0387.dll"));
        byte[] original = (byte[])file.FileBytes.Clone();
        var types = MagicEgoDescriptorInfo.Read(file);
        Assert.Equal(49, types.Count);
        var vector = Assert.Single(types.Where(t => t.Name == "EgoVec"));
        Assert.Equal(0x11674u, vector.Rva);
        Assert.Equal(2u, vector.HeaderWord16);
        Assert.Equal("EgoCtrl", vector.LinkedDescriptorName);
        Assert.Equal(new uint[] { 0x1000, 0x3630 }, vector.CallbackRvas);
        var task = Assert.Single(types.Where(t => t.Name == "EgoTask"));
        Assert.Equal(0x1156Cu, task.Rva);
        Assert.Equal(new uint[] { 0x1000, 0x3770, 0x7A60 }, task.CallbackRvas);
        Assert.Null(Assert.Single(types.Where(t => t.Name == "EgoBs2")).LinkedDescriptorName);
        Assert.Equal(original, file.FileBytes);
    }

    [Fact]
    public void DeclaredCallbackPointingIntoData_IsNotAnExecutableDescriptor()
    {
        var file = MagicDllFile.Load(Path.Combine(TestDataPaths.MagicCorpus, "magic_0387.dll"));
        byte[] bytes = (byte[])file.FileBytes.Clone();
        BitConverter.GetBytes(0x10011674u).CopyTo(bytes, file.RvaToOffset(0x11674 + 28));
        var changed = new MagicDllFile("copied.dll", bytes,
            bytes.AsSpan(file.DataSectionRawPtr, file.DataSectionSize).ToArray(),
            file.DataSectionRawPtr, file.DataSectionSize, file.DataSectionRva,
            file.Sections, file.Roots);
        Assert.DoesNotContain(MagicEgoDescriptorInfo.Read(changed), t => t.Name == "EgoVec");
        Assert.Equal(file.FileBytes, File.ReadAllBytes(file.SourcePath));
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
    public async Task Editor_InspectsStaticDescriptorsOnceAndKeepsAllCellsReadOnly(string culture)
    {
        var prior = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var vm = new MagicDllEditor_ViewModel();
            Assert.True(await vm.OpenFileAsync(Path.Combine(TestDataPaths.MagicCorpus, "magic_0387.dll")));
            var node = Assert.Single(MagicTimingEditorTests.Walk(vm.RootNodes).OfType<MagicEgoDescriptorNode>());
            Assert.Equal(Strings.U_Md_EgoDescriptors, node.Label);
            Assert.Equal(49, node.Children.Count);
            var add = Assert.Single(node.Children.Where(child => child.Label == "EgoAddIVec"));
            var callback = add.Children[3];
            Assert.Equal($"RVA 0x1080 · {Strings.U_Md_EgoOpAddIntegerVector}", callback.MetaText);
            Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.U_Md_EgoOperationHint,
                Strings.U_Md_EgoOpAddIntegerVector), callback.DetailText);
            Assert.All(MagicTimingEditorTests.Walk(node.Children), child => Assert.False(child.IsValueEditable));
            Assert.False(vm.IsDirty);
        }
        finally { CultureInfo.CurrentUICulture = prior; }
    }

    [Fact]
    public void EveryCorpusDescriptor_HasBoundedRelocatedCallbacksAndLeavesSourceUntouched()
    {
        int files = 0, descriptors = 0;
        foreach (string path in Directory.GetFiles(TestDataPaths.MagicCorpus, "magic_*.dll"))
        {
            var file = MagicDllFile.Load(path);
            byte[] original = (byte[])file.FileBytes.Clone();
            foreach (var type in MagicEgoDescriptorInfo.Read(file))
            {
                Assert.InRange(type.HeaderWord16, 1u, 8u);
                Assert.Equal(type.HeaderWord16, (uint)type.CallbackRvas.Count);
                Assert.All(type.CallbackRvas, rva => Assert.True(file.RvaToOffset((int)rva) >= 0));
                descriptors++;
            }
            Assert.Equal(original, file.FileBytes);
            files++;
        }
        Assert.True(files >= 705);
        Assert.True(descriptors >= 5885);
    }
}
