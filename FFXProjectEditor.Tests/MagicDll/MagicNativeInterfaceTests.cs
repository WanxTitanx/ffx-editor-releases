using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Threading.Tasks;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Modules.MagicDllEditor;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll;

public class MagicNativeInterfaceTests
{
    [Theory]
    [InlineData(3, 0x6C90u, 0x95B8u)]
    [InlineData(52, 0x2C70u, 0x5058u)]
    [InlineData(507, 0x11C0u, 0x74130u)]
    public void RealGetter_DistinguishesTheDataCellFromFifteenCallbacks(int id, uint getter, uint table)
    {
        var file = Read(id);
        var info = Assert.IsType<MagicNativeInterfaceInfo>(MagicNativeInterfaceInfo.TryRead(file));
        Assert.Equal(getter, info.GetterRva);
        Assert.Equal(table, info.TableRva);
        Assert.Equal(16, info.Entries.Count);
        Assert.Equal(7, Assert.Single(info.Entries.Where(e => e.IsDataPointer)).Index);
        Assert.All(info.Entries.Where(e => e.Index != 7), e => Assert.False(e.IsDataPointer));
        Assert.NotNull(info.Entries[15].Rva);
        Assert.Equal(file.FileBytes, File.ReadAllBytes(file.SourcePath));
    }

    [Theory]
    [InlineData(344, 0x1690u, 0x10D0u, 0x1110u, 0x1190u, 0x4020u)]
    [InlineData(533, 0xB290u, 0xC0E0u, 0xC1E0u, 0xC320u, 0xE47F0u)]
    [InlineData(387, 0xD970u, 0xDB30u, 0xDB40u, 0xDBC0u, 0xA93F0u)]
    public void AuditedOverlays_ExposeTheirOwnLifecycleRatherThanTaskHelpers(
        int id, uint init, uint cleanup, uint phase0, uint phase1, uint data)
    {
        // Constants come from hash-pinned EOT/IDA evidence for these inputs.
        // In 0533, 8490/9520 are installed task callbacks, outside the EOT.
        var file = Read(id);
        byte[] original = (byte[])file.FileBytes.Clone();
        var info = Assert.IsType<MagicNativeInterfaceInfo>(MagicNativeInterfaceInfo.TryRead(file));
        Assert.Equal(init, info.Entries[0].Rva);
        Assert.Equal(cleanup, info.Entries[1].Rva);
        Assert.Equal(phase0, info.Entries[3].Rva);
        Assert.Equal(phase1, info.Entries[4].Rva);
        Assert.True(info.Entries[7].IsDataPointer);
        Assert.Equal(data, info.Entries[7].Rva);
        Assert.Equal(original, file.FileBytes);
    }

    [Fact]
    public void FieldOverlay_NullDataDoesNotBecomeARegistrationClaim()
    {
        var file = Read(507);
        var info = Assert.IsType<MagicNativeInterfaceInfo>(MagicNativeInterfaceInfo.TryRead(file));
        Assert.Null(info.Entries[7].Rva);
        Assert.Equal(0, MagicFieldOverlayInfo.TryRead(file)!.RegisteredPppCount);
    }

    [Fact]
    public void EveryCorpusDll_UsesTheVerifiedGetterAndTypedInterfaceShape()
    {
        string[] files = Directory.GetFiles(TestDataPaths.MagicCorpus, "magic_*.dll");
        Assert.True(files.Length >= 705);
        foreach (string path in files)
        {
            var file = MagicDllFile.Load(path);
            var info = Assert.IsType<MagicNativeInterfaceInfo>(MagicNativeInterfaceInfo.TryRead(file));
            Assert.Equal(16, info.Entries.Count);
            Assert.Equal(7, Assert.Single(info.Entries.Where(e => e.IsDataPointer)).Index);
            Assert.True(info.Entries.Take(7).All(e => e.Rva.HasValue), path);
        }
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
    public async Task Editor_ShowsDataAsDataAndKeepsInterfaceReadOnly(string culture)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var vm = new MagicDllEditor_ViewModel();
            Assert.True(await vm.OpenFileAsync(Path.Combine(TestDataPaths.MagicCorpus, "magic_0003.dll")));
            var node = Assert.Single(MagicTimingEditorTests.Walk(vm.RootNodes).OfType<MagicNativeInterfaceNode>());
            Assert.Equal(Strings.U_Md_NativeDllInterface, node.Label);
            Assert.Equal(16, node.Children.Count);
            Assert.Equal("data", node.Children[7].TypeLabel);
            Assert.Equal(Strings.U_Md_NativeDllDataPointer, node.Children[7].Label);
            Assert.Equal("callback", node.Children[15].TypeLabel);
            Assert.All(node.Children, entry => Assert.False(entry.IsValueEditable));
            Assert.False(vm.IsDirty);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [Fact]
    public async Task MultiRootDll_ContainsOneFileInterfaceRatherThanOnePerPppRoot()
    {
        var vm = new MagicDllEditor_ViewModel();
        Assert.True(await vm.OpenFileAsync(Path.Combine(TestDataPaths.MagicCorpus, "magic_0069.dll")));
        Assert.True(vm.RootNodes.OfType<MagicRootNode>().Count() >= 2);
        Assert.Single(MagicTimingEditorTests.Walk(vm.RootNodes).OfType<MagicNativeInterfaceNode>());
        Assert.False(vm.IsDirty);
    }

    [Theory]
    [InlineData("getter")]
    [InlineData("callback")]
    [InlineData("data")]
    [InlineData("ordinal")]
    public void UnprovedGetterOrTableShape_IsRejectedWithoutChangingSource(string mutation)
    {
        var file = Read(3);
        var info = Assert.IsType<MagicNativeInterfaceInfo>(MagicNativeInterfaceInfo.TryRead(file));
        byte[] bytes = (byte[])file.FileBytes.Clone();
        int at = mutation switch
        {
            "getter" => file.RvaToOffset((int)info.GetterRva) + 15,
            "callback" => file.RvaToOffset((int)info.TableRva),
            "data" => file.RvaToOffset((int)info.TableRva) + 28,
            _ => OrdinalOffset(file),
        };
        if (mutation == "getter") bytes[at] ^= 1;
        else if (mutation == "ordinal") BitConverter.GetBytes(ushort.MaxValue).CopyTo(bytes, at);
        else BitConverter.GetBytes(0x10006C90u).CopyTo(bytes, at);
        if (mutation == "callback") BitConverter.GetBytes(0x100095B8u).CopyTo(bytes, at);
        var changed = new MagicDllFile("renamed.dll", bytes,
            bytes.AsSpan(file.DataSectionRawPtr, file.DataSectionSize).ToArray(),
            file.DataSectionRawPtr, file.DataSectionSize, file.DataSectionRva, file.Sections, file.Roots);
        Assert.Null(MagicNativeInterfaceInfo.TryRead(changed));
        Assert.Equal(file.FileBytes, File.ReadAllBytes(file.SourcePath));
    }

    private static MagicDllFile Read(int id) => MagicDllFile.Load(
        Path.Combine(TestDataPaths.MagicCorpus, $"magic_{id:0000}.dll"));

    private static int OrdinalOffset(MagicDllFile file)
    {
        int pe = BitConverter.ToInt32(file.FileBytes, 0x3C);
        int export = file.RvaToOffset((int)BitConverter.ToUInt32(file.FileBytes, pe + 120));
        return file.RvaToOffset((int)BitConverter.ToUInt32(file.FileBytes, export + 36));
    }
}
