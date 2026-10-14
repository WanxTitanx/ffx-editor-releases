using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FFXProjectEditor.Modules.MagicDllEditor;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll;

public class MagicFieldOverlayTests
{
    private static MagicDllFile Read() => new MagicDllParser().Parse(
        Path.Combine(TestDataPaths.MagicCorpus, "magic_0507.dll"));
    private static MagicDllFile WithBytes(MagicDllFile file, byte[] bytes, string name = "renamed.dll") =>
        new(name, bytes, bytes.AsSpan(file.DataSectionRawPtr, file.DataSectionSize).ToArray(),
            file.DataSectionRawPtr, file.DataSectionSize, file.DataSectionRva, file.Sections, file.Roots);

    [Fact]
    public void AuditedFieldOverlay_ReportsItsUnregisteredPppAndReadOnlyCallbacks()
    {
        var file = Read();
        var overlay = Assert.IsType<MagicFieldOverlayInfo>(MagicFieldOverlayInfo.TryRead(file));
        Assert.Equal(0, overlay.RegisteredPppCount);
        Assert.Equal(0x74130u, overlay.CallbackTableRva);
        Assert.Equal(16, overlay.CallbackRvas.Count);
        Assert.Equal(0x10D0u, overlay.CallbackRvas[0]);
        Assert.Equal(453, file.Roots.Sum(r => r.TotalSlots));
        Assert.All(file.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots),
            s => Assert.Null(s.OpcodeName));
    }

    [Fact]
    public void CodeProfile_IsIndependentOfFilenameAndMutablePppPayload()
    {
        var file = Read();
        byte[] bytes = (byte[])file.FileBytes.Clone();
        int record = file.Roots[0].Programs.First(p => p.SlotCount > 0).Slots[0].RecordOffset;
        bytes[file.DataSectionRawPtr + record + 8] ^= 1;
        Assert.NotNull(MagicFieldOverlayInfo.TryRead(WithBytes(file, bytes)));
        Assert.Equal(file.FileBytes, File.ReadAllBytes(file.SourcePath));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ChangedCodeOrCallbackTable_DoesNotInheritVerifiedInterface(bool code)
    {
        var file = Read();
        byte[] bytes = (byte[])file.FileBytes.Clone();
        int at = code ? file.Sections.Single(s => s.Name == ".text").RawPtr + 0xDD :
            file.DataSectionRawPtr + 0x74130 - file.DataSectionRva;
        bytes[at] ^= 1;
        Assert.Null(MagicFieldOverlayInfo.TryRead(WithBytes(file, bytes)));
    }

    [Fact]
    public void AutomaticallyDiscoveredFpHintsCannotBindTheUnregisteredRoot()
    {
        var file = Read();
        string directory = Path.Combine(TestDataPaths.RepoRoot, "work", "magic-editor-completion-20261007",
            "overlay-fp", Guid.NewGuid().ToString("N"));
        TestDirectory.CreatePrivate(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "fp.h"), "pppProgTbl_FP[]={\nPPMPN(\"pppScale\")\n};\n");
            var parser = new MagicDllParser(new MagicDllParserOptions { FpDirectoryPath = directory });
            var parsed = parser.Parse(file.SourcePath);
            Assert.All(parsed.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots),
                s => Assert.Null(s.OpcodeName));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Editor_ShowsTheVerifiedInterfaceAndKeepsEveryCallbackReadOnly()
    {
        var vm = new MagicDllEditor_ViewModel();
        Assert.True(await vm.OpenFileAsync(Path.Combine(TestDataPaths.MagicCorpus, "magic_0507.dll")));
        var node = Assert.Single(MagicTimingEditorTests.Walk(vm.RootNodes).OfType<MagicFieldOverlayNode>());
        Assert.Equal(16, node.Children.Count);
        Assert.All(node.Children, c => Assert.False(c.IsValueEditable));
        Assert.Contains("PPP", node.DetailText);
        Assert.False(vm.IsDirty);
    }
}
