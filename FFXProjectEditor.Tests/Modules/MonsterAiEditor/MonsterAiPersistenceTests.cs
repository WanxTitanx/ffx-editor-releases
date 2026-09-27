using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.Modules.MonsterAiEditor;
using Xunit;

namespace FFXProjectEditor.Tests.Modules.MonsterAiEditor;

[Collection(MonsterAiTestCollection.Name)]
public sealed class MonsterAiPersistenceTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), "monster-ai-save-" + Guid.NewGuid().ToString("N"));
    readonly string path;
    readonly byte[] original;
    readonly MonsterAiEditor_DataModel model;

    public MonsterAiPersistenceTests()
    {
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "m001.bin");
        original = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Monster", "m001.bin"));
        File.WriteAllBytes(path, original);
        model = new MonsterAiEditor_DataModel { SelectedMonster = MonsterAiRow.TryLoad(path) };
        Assert.NotNull(model.SelectedMonster?.Script);
    }

    AiScriptFile Script => model.SelectedMonster!.Script!;
    byte[] Disk => File.ReadAllBytes(path);
    byte[] DiskAi => AiScript_File.SliceAiFileFromMonster(Disk)!;

    void EditOperand()
    {
        var instruction = Script.Instructions.First(i => i.Opcode == 0xAE);
        var row = model.EditableInstructions.Single(r => r.Label.StartsWith($"0x{instruction.Offset:X4} ", StringComparison.Ordinal));
        row.OperandHex = ((ushort)(instruction.Operand ^ 1)).ToString("X4");
        Assert.True(model.HasPendingEdits);
    }

    byte[] ChangeAiExternally()
    {
        var external = AiScript_File.Read(DiskAi);
        external.Instructions.Last(i => i.Opcode == 0xAE).Operand ^= 2;
        var changed = AiScript_File.SpliceAiFileIntoMonster(Disk, AiScript_File.Write(external));
        File.WriteAllBytes(path, changed);
        return changed;
    }

    void BlockBackup() => Directory.CreateDirectory(path + ".prev.bak");

    [Fact]
    public void Save_BackupFailure_PreservesDiskAndPendingEdit()
    {
        EditOperand();
        BlockBackup();
        model.Save();
        Assert.Equal(original, Disk);
        Assert.True(model.HasPendingEdits);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public void Save_ExternalAiChange_IsNotOverwritten()
    {
        EditOperand();
        byte[] external = ChangeAiExternally();
        model.Save();
        Assert.Equal(external, Disk);
        Assert.True(model.HasPendingEdits);
        Assert.False(File.Exists(path + ".prev.bak"));
    }

    [Fact]
    public void Save_ExternalNonAiChange_IsPreservedInOutputAndBackup()
    {
        EditOperand();
        byte[] external = (byte[])original.Clone();
        external[^1] ^= 1;
        Assert.Equal(Script.OriginalAiFileBytes, AiScript_File.SliceAiFileFromMonster(external));
        File.WriteAllBytes(path, external);
        byte[] pending = AiScript_File.Write(Script);
        model.Save();
        Assert.Equal(external[^1], Disk[^1]);
        Assert.Equal(pending, DiskAi);
        Assert.Equal(external, File.ReadAllBytes(path + ".prev.bak"));
        Assert.Equal(DiskAi, AiScript_File.Write(Script));
        Assert.False(model.HasPendingEdits);
    }

    [Fact]
    public void AtelApply_CreatesBackup_RefreshesAllSurfaces_AndAllowsAnotherSave()
    {
        model.AtelScriptText = "battleVar0014 = 7;";
        model.CompileAtelApply();
        Assert.False(original.SequenceEqual(Disk), model.AtelScriptResult);
        Assert.True(File.Exists(path + ".prev.bak"), model.AtelScriptResult);
        Assert.Equal(original, File.ReadAllBytes(path + ".prev.bak"));
        Assert.Equal(DiskAi, Script.OriginalAiFileBytes);
        Assert.Equal(Script.Instructions.Count, model.AssemblerInstructions.Count);
        Assert.True(model.CanUndoBackup);
        byte[] first = Disk;
        model.Save();
        Assert.Equal(first, Disk);
        // An unchanged Save must not consume the one-level undo of the ATEL edit.
        Assert.Equal(original, File.ReadAllBytes(path + ".prev.bak"));
    }

    [Fact]
    public void AtelApply_BackupFailure_PreservesDiskAndModel()
    {
        model.AtelScriptText = "battleVar0014 = 7;";
        BlockBackup();
        model.CompileAtelApply();
        Assert.Equal(original, Disk);
        Assert.Equal(AiScript_File.SliceAiFileFromMonster(original), Script.OriginalAiFileBytes);
    }

    [Fact]
    public void AtelApply_ExternalAiChange_IsRejectedUntilReload()
    {
        model.AtelScriptText = "battleVar0014 = 7;";
        byte[] external = ChangeAiExternally();
        model.CompileAtelApply();
        Assert.Equal(external, Disk);
        Assert.False(File.Exists(path + ".prev.bak"));
    }

    [Fact]
    public void AtelApply_PendingOperand_IsNeitherSavedNorDiscarded()
    {
        EditOperand();
        byte[] pending = AiScript_File.Write(Script);
        model.AtelScriptText = "battleVar0014 = 7;";
        model.CompileAtelApply();
        Assert.Equal(original, Disk);
        Assert.Equal(pending, AiScript_File.Write(Script));
        Assert.True(model.HasPendingEdits);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-number")]
    [InlineData("65536")]
    [InlineData("-1")]
    public void StatAuthoring_InvalidNumber_DoesNotWriteZero(string input)
    {
        model.PlantFieldValue = input;
        model.AddSetStatFieldAction();
        Assert.Equal(original, Disk);
        Assert.False(File.Exists(path + ".prev.bak"));
    }

    [Fact]
    public void EmptySelection_ClearsPendingEditState()
    {
        EditOperand();
        model.SelectedMonster = null;
        Assert.False(model.HasPendingEdits);
        Assert.Empty(model.PendingEditPreview);
    }

    [Fact]
    public void AdvancedSave_AcceptsOnlyTheRequiredZeroAlignmentPadding()
    {
        var compiled = FFXProjectEditor.FfxLib.Ai.AtelScript.AtelProgramCompiler.CompileProgram(
            new FFXProjectEditor.FfxLib.Ai.AtelScript.AtelParser("battleVar0014 = 7;").ParseProgram(),
            new FFXProjectEditor.FfxLib.Ai.AtelScript.AtelCompileContext
            {
                Script = Script,
                WorkerIndex = AiAutomation.PickCombatWorker(Script)!.Index,
                EntrypointIndex = AiAutomation.PickMainEntrypoint(Script, AiAutomation.PickCombatWorker(Script)!),
            });
        Assert.NotEqual(0, compiled.AiFile.Length % 16);
        AiAdvancedFileWriter.Save(path, Script.OriginalAiFileBytes, compiled.AiFile);
        Assert.Equal(compiled.AiFile, DiskAi.Take(compiled.AiFile.Length));
        Assert.Equal((compiled.AiFile.Length + 15) & ~15, DiskAi.Length);
        Assert.All(DiskAi.Skip(compiled.AiFile.Length), b => Assert.Equal(0, b));
        Assert.Equal(original, File.ReadAllBytes(path + ".prev.bak"));
    }

    [Fact]
    public void AdvancedSave_NoChange_PreservesPreviousBackup()
    {
        byte[] previous = { 1, 2, 3 };
        File.WriteAllBytes(path + ".prev.bak", previous);
        AiAdvancedFileWriter.Save(path, Script.OriginalAiFileBytes, Script.OriginalAiFileBytes);
        Assert.Equal(previous, File.ReadAllBytes(path + ".prev.bak"));
        Assert.Equal(original, Disk);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApplyingAnotherSurface_PreservesUnappliedAssemblerRows(bool atel)
    {
        model.InsertOpcodeHex = "00";
        model.InsertAssemblerInstruction();
        int count = model.AssemblerInstructions.Count;
        if (atel)
        {
            model.AtelScriptText = "battleVar0014 = 7;";
            model.CompileAtelApply();
        }
        else model.Save();
        Assert.Equal(original, Disk);
        Assert.Equal(count, model.AssemblerInstructions.Count);
        Assert.True(model.AssemblerInstructions.Any(r => r.IsNew));
    }

    [Fact]
    public void SwitchingMonsters_PreservesAssemblerDraft()
    {
        var first = model.SelectedMonster;
        model.InsertOpcodeHex = "00";
        model.InsertAssemblerInstruction();
        var draft = model.AssemblerInstructions.ToArray();
        string secondPath = Path.Combine(directory, "m000.bin");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Monster", "m000.bin"), secondPath);
        model.SelectedMonster = MonsterAiRow.TryLoad(secondPath);
        model.SelectedMonster = first;
        Assert.Equal(draft, model.AssemblerInstructions);
        Assert.Equal(original, Disk);
    }

    [Fact]
    public void StatAuthoring_SaveAndUndo_RestoreExactOriginal()
    {
        model.PlantFieldValue = "0x21";
        model.AddSetStatFieldAction();
        Assert.False(original.SequenceEqual(Disk), model.PlantFieldSummary);
        Assert.Equal(original, File.ReadAllBytes(path + ".prev.bak"));
        Assert.Equal(DiskAi, AiScript_File.Write(Script));
        model.RestoreBackup();
        Assert.Equal(original, Disk);
    }

    [Fact]
    public void InlineEdit_BackupFailure_DoesNotReportSuccess()
    {
        model.SelectedAutomationAction = model.AutomationActions.First(r =>
            r.Action.Kind is AiActionKind.Buff or AiActionKind.Stat);
        Assert.True(model.CanEditBuffStat);
        model.BuffStatValue = "33";
        BlockBackup();
        model.EditSelectedBuffStat();
        Assert.Equal(original, Disk);
        Assert.Equal(model.LoadSummary, model.BuffStatEditSummary);
        Assert.True(model.HasPendingEdits);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1e100")]
    public void ScaleAuthoring_NonFiniteFactor_IsRejected(string value)
    {
        model.ScaleFactorText = value;
        model.ApplyUniformScale();
        Assert.Equal(original, Disk);
        Assert.False(File.Exists(path + ".prev.bak"));
    }

    [Fact]
    public void OverdriveRecipe_InvalidCustomStart_DoesNotWriteZero()
    {
        model.SelectedOverdriveStartMode = model.OverdriveStartModeChoices.Single(x => x.Kind == OverdriveStartModeKind.Custom);
        model.OverdriveRecipeCustomStart = "invalid";
        model.ApplyOverdriveRecipeAction();
        Assert.Equal(original, Disk);
        Assert.False(File.Exists(path + ".prev.bak"));
    }

    [Fact]
    public void AdvancedCondition_PendingAssemblerRow_IsPreserved()
    {
        model.PrepareAdvancedPhaseRotationManager();
        Assert.NotEmpty(model.AdvancedNativeConditions);
        model.InsertOpcodeHex = "00";
        model.InsertAssemblerInstruction();
        model.AdvancedNativeConditionValue = "7";
        Assert.False(model.ApplyAdvancedNativeCondition());
        Assert.Equal(original, Disk);
        Assert.True(model.AssemblerInstructions.Any(r => r.IsNew));
    }

    [Fact]
    public void ScaleWriter_RejectsNonFiniteValuesAndArithmeticOverflow()
    {
        foreach (float value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            Assert.False(FFXProjectEditor.FfxLib.Ai.Sin.SinScaleOpener.TryEmitInlineAfterDeathAnimation(original, value).Ok);

        var inserted = FFXProjectEditor.FfxLib.Ai.Sin.SinScaleOpener.TryEmitInlineAfterDeathAnimation(original, 2f);
        Assert.True(inserted.Ok, inserted.Error);
        var valid = FFXProjectEditor.FfxLib.Ai.Sin.SinScaleOpener.TryReplaceUniformScaleOwnSize(inserted.EditedMonster!, 1.5f);
        Assert.True(valid.Ok, valid.Error);
        var script = AiScript_File.Read(AiScript_File.SliceAiFileFromMonster(valid.EditedMonster!)!);
        Assert.All(FFXProjectEditor.FfxLib.Ai.Sin.SinScaleOpener.EnumerateUniformScaleOwnSize(script), site => Assert.Equal(3f, site.Value));
        Assert.False(FFXProjectEditor.FfxLib.Ai.Sin.SinScaleOpener.TryReplaceUniformScaleOwnSize(inserted.EditedMonster!, float.MaxValue).Ok);
        Assert.Equal(original, Disk);
    }

    [Fact]
    public void AssemblerSave_PreservesPendingOperandEdits()
    {
        EditOperand();
        var edited = AiScript_File.Write(Script);
        model.SaveAssembler();
        Assert.Equal(original, Disk);
        Assert.Equal(edited, AiScript_File.Write(Script));
        Assert.True(model.HasPendingEdits);
    }

    [Fact]
    public void OperandSave_RejectsOutOfRangeJump()
    {
        var instruction = Script.Instructions.First(i => AiScript_File.OperandKindOf(i.Opcode) == AiOperandKind.JumpIndex);
        var row = model.EditableInstructions.Single(r => r.Label.StartsWith($"0x{instruction.Offset:X4} ", StringComparison.Ordinal));
        row.OperandHex = "FFFF";
        model.Save();
        Assert.Equal(original, Disk);
        Assert.True(model.HasPendingEdits);
    }

    [Fact]
    public void Undo_CorruptBackup_DoesNotReplaceMonster()
    {
        File.WriteAllBytes(path + ".prev.bak", new byte[] { 1, 2, 3 });
        model.RestoreBackup();
        Assert.Equal(original, Disk);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Save_MissingFile_IsReportedWithoutThrowing(bool scale)
    {
        File.Delete(path);
        Exception? error = Record.Exception(() =>
        {
            if (scale) model.ApplyUniformScale();
            else model.SaveAssembler();
        });
        Assert.Null(error);
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("chance")]
    [InlineData("hp")]
    [InlineData("hp-min")]
    [InlineData("hp-max")]
    [InlineData("damage")]
    public void OverdriveRecipe_InvalidEnabledThreshold_IsRejected(string field)
    {
        switch (field)
        {
            case "chance": model.OverdriveChargeChance = true; model.OverdriveChargeChanceK = "invalid"; break;
            case "hp": model.OverdriveChargeHpBelow = true; model.OverdriveChargeHpPercent = "invalid"; break;
            case "hp-min": model.OverdriveChargeHpRange = true; model.OverdriveChargeHpRangeMin = "invalid"; break;
            case "hp-max": model.OverdriveChargeHpRange = true; model.OverdriveChargeHpRangeMax = "invalid"; break;
            case "damage": model.OverdriveChargeDamageTakenPercent = true; model.OverdriveChargeDamageTakenPercentThreshold = "invalid"; break;
        }
        model.ApplyOverdriveRecipeAction();
        Assert.Equal(original, Disk);
        Assert.False(File.Exists(path + ".prev.bak"));
    }

    [Fact]
    public void Template_InvalidNumber_IsNotInsertedOrSavedAsZero()
    {
        model.SelectedSnippet = model.LibrarySnippets.First(row => row.Snippet.UsesValue);
        model.TemplateValue = "0xnot-hex";
        int count = model.AssemblerInstructions.Count;
        model.ApplyOrInsertTemplate();
        Assert.Equal(count, model.AssemblerInstructions.Count);
        Assert.Equal(original, Disk);
        Assert.False(File.Exists(path + ".prev.bak"));
    }

    public void Dispose()
    {
        model.Dispose();
        Directory.Delete(directory, recursive: true);
    }
}
