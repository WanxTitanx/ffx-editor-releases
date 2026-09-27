using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.Modules.MonsterAiEditor;
using Xunit;

namespace FFXProjectEditor.Tests.Modules.MonsterAiEditor;

[Collection(MonsterAiTestCollection.Name)]
public sealed class SinUniPersistenceTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), "sin-uni-save-" + Guid.NewGuid().ToString("N"));
    readonly string path;
    readonly byte[] original;
    readonly MonsterAiEditor_DataModel model;

    public SinUniPersistenceTests()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "FFXProjectEditor", "FFXProjectEditor.csproj"))) root = root.Parent;
        Assert.NotNull(root);
        original = File.ReadAllBytes(Path.Combine(root!.FullName, "mods", "Spira Reforge", "sin-clean-bins", "_m198", "m198.bin"));
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "m198.bin");
        File.WriteAllBytes(path, original);
        model = new MonsterAiEditor_DataModel { SelectedMonster = MonsterAiRow.TryLoad(path) };
    }

    [Fact]
    public void UiBake_SavesWithOriginalBackup_AndRejectsRepeatedApply()
    {
        Assert.True(model.ApplySinPostActionPreset("UNI-040"), model.SinCatalogSummary);
        byte[] baked = File.ReadAllBytes(path);
        Assert.False(original.SequenceEqual(baked));
        Assert.Equal(original, File.ReadAllBytes(path + ".prev.bak"));
        Assert.False(model.ApplySinPostActionPreset("UNI-040"));
        Assert.Equal(baked, File.ReadAllBytes(path));
        Assert.Equal(original, File.ReadAllBytes(path + ".prev.bak"));
    }

    [Fact]
    public void UiBake_BackupFailure_DoesNotWrite()
    {
        Directory.CreateDirectory(path + ".prev.bak");
        Assert.False(model.ApplySinPostActionPreset("UNI-040"));
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void UiBake_StaleAi_DoesNotOverwriteExternalChanges()
    {
        byte[] ai = AiScript_File.SliceAiFileFromMonster(original)!;
        var script = AiScript_File.Read(ai);
        script.Instructions.Last(op => op.Opcode == 0xAE).Operand ^= 1;
        byte[] external = AiScript_File.SpliceAiFileIntoMonster(original, AiScript_File.Write(script));
        File.WriteAllBytes(path, external);
        Assert.False(model.ApplySinPostActionPreset("UNI-040"));
        Assert.Equal(external, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".prev.bak"));
    }

    [Fact]
    public void UiBake_PendingAssemblerEdits_ArePreserved()
    {
        model.InsertOpcodeHex = "00";
        model.InsertAssemblerInstruction();
        Assert.True(model.AssemblerInstructions.Any(row => row.IsNew));
        Assert.False(model.ApplySinPostActionPreset("UNI-040"));
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.True(model.AssemblerInstructions.Any(row => row.IsNew));
    }

    public void Dispose()
    {
        model.Dispose();
        Directory.Delete(directory, recursive: true);
    }
}
