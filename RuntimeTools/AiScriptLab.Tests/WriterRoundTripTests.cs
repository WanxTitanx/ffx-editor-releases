using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai;
using Xunit;

namespace AiScriptLab.Tests;

/// <summary>
/// F7.2: xUnit de round-trip dos 8 writers de AI (mesmo padrao do gate --receita-dry-run do
/// AiScriptLab, sem reimplementacao). Para cada writer: carrega o monstro de referencia do corpus,
/// monta descriptors, aplica o patch, valida (AiValidator), confere splice round-trip e restaura o
/// patch — exigindo restore byte-idêntico ao AiFile original.
/// Corpus: var de ambiente FFX_CORPUS ou o caminho padrao do projeto.
/// </summary>
public class WriterRoundTripTests
{
    static string CorpusRoot =>
        Environment.GetEnvironmentVariable("FFX_CORPUS")
        ?? @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\battle\mon";

    static AiReceipt RunDryRun(
        string recipe,
        string monsterId,
        Func<byte[], AiScriptFile, (bool Ok, string Error)> buildDescriptors,
        Func<AiScriptFile, (bool Ok, byte[]? Edited, string Error)> applyPatch,
        Func<AiScriptFile, (bool Ok, byte[]? Restored, string Error)> restorePatch)
    {
        string? path = Directory.EnumerateFiles(CorpusRoot, "m*.bin", SearchOption.AllDirectories)
            .FirstOrDefault(c => string.Equals(Path.GetFileNameWithoutExtension(c), monsterId, StringComparison.OrdinalIgnoreCase));
        Assert.True(path != null, $"monster {monsterId} ausente no corpus");

        byte[] monster = File.ReadAllBytes(path!);
        byte[]? aiFile = AiScript_File.SliceAiFileFromMonster(monster);
        Assert.True(aiFile != null, "ai slice");

        AiScriptFile script = AiScript_File.Read(aiFile!);

        var (buildOk, buildError) = buildDescriptors(monster, script);
        Assert.True(buildOk, $"descriptors: {buildError}");

        var (patchOk, editedBytes, patchError) = applyPatch(script);
        Assert.True(patchOk && editedBytes != null, $"patch: {patchError}");

        bool validatorOk = AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(
            editedBytes!, script, aiFile!.Length, out _, out string validatorWhy);
        Assert.True(validatorOk, $"validator: {validatorWhy}");

        byte[] splicedMonster = AiScript_File.SpliceAiFileIntoMonster(monster, editedBytes!);
        byte[]? slicedBack = AiScript_File.SliceAiFileFromMonster(splicedMonster);
        Assert.True(slicedBack != null && slicedBack.SequenceEqual(editedBytes!), "splice round-trip");

        AiScriptFile editedScript = AiScript_File.Read(editedBytes!);
        var (restoreOk, restoredBytes, restoreError) = restorePatch(editedScript);
        Assert.True(restoreOk && restoredBytes != null, $"restore: {restoreError}");
        Assert.True(restoredBytes!.SequenceEqual(aiFile!), $"restore nao byte-identico: {restoreError}");

        return AiPatchGuard.BuildReceipt(
            recipe, monsterId, aiFile!, editedBytes!, script, editedScript,
            validatorOk, validatorWhy, true, true);
    }

    [Fact]
    public void OmnisCluster_RoundTrip()
    {
        AiReceipt r = OmnisDryRun();
        Assert.True(r.Verdict, r.ValidatorNote);
    }

    [Fact]
    public void SupportAccumulator_RoundTrip()
    {
        AiReceipt r = SupportAccumulatorDryRun();
        Assert.True(r.Verdict, r.ValidatorNote);
    }

    [Fact]
    public void MortiorchisCompanion_RoundTrip()
    {
        AiReceipt r = MortiorchisDryRun();
        Assert.True(r.Verdict, r.ValidatorNote);
    }

    [Fact]
    public void FluxNativeThreshold_RoundTrip()
    {
        AiReceipt r = FluxNativeDryRun();
        Assert.True(r.Verdict, r.ValidatorNote);
    }

    [Fact]
    public void AnimaOdThreshold_RoundTrip()
    {
        AiReceipt r = AnimaOdDryRun();
        Assert.True(r.Verdict, r.ValidatorNote);
    }

    [Fact]
    public void RoundScriptedBoss_RoundTrip()
    {
        AiReceipt r = RoundScriptedBossDryRun();
        Assert.True(r.Verdict, r.ValidatorNote);
    }

    [Theory]
    [InlineData("m106")]
    [InlineData("m118")]
    [InlineData("m150")]
    [InlineData("m154")]
    public void ReactiveSensor_RoundTrip(string monsterId)
    {
        AiReceipt r = ReactiveSensorDryRun(monsterId);
        Assert.True(r.Verdict, r.ValidatorNote);
    }

    [Fact]
    public void IndirectDispatchRow_RoundTrip()
    {
        AiReceipt r = IndirectDispatchRowDryRun();
        Assert.True(r.Verdict, r.ValidatorNote);
    }

    // ── closures por writer (mesma logica dos receipts do AiScriptLab) ──────────────────────────

    static AiReceipt OmnisDryRun()
    {
        string beatName = string.Empty;
        int cmdOffset = -1;
        ushort originalCommand = 0, swappedCommand = 0;
        int? stateOffset = null;
        ushort? stateValue = null;

        return RunDryRun("omnis-cluster", "m131",
            (_, script) =>
            {
                if (!AiOmnisClusterWriter.TryBuildDescriptors(script, out IReadOnlyList<AiOmnisClusterDescriptor> ds, out string err) || ds.Count == 0)
                    return (false, ds.Count == 0 ? "sem descriptors" : err);
                AiOmnisClusterDescriptor d = ds.First();
                beatName = d.BeatName;
                cmdOffset = d.CommandInstructionOffset;
                originalCommand = d.CurrentCommand;
                swappedCommand = d.CurrentCommand == 0x3045 ? (ushort)0x3046 : (ushort)0x3045;
                stateOffset = d.StateWriteOffset;
                stateValue = d.CurrentStateValue;
                return (true, string.Empty);
            },
            script =>
            {
                var req = new AiOmnisClusterPatchRequest(beatName, cmdOffset, originalCommand, swappedCommand, stateOffset, stateValue, stateValue);
                bool ok = AiOmnisClusterWriter.TryApplyPatch(script, req, out AiOmnisClusterEditResult? res, out string err);
                return ok && res != null ? (true, res.EditedAiFileBytes, string.Empty) : (false, null, err);
            },
            script =>
            {
                var req = new AiOmnisClusterPatchRequest(beatName, cmdOffset, swappedCommand, originalCommand, stateOffset, stateValue, stateValue);
                bool ok = AiOmnisClusterWriter.TryApplyPatch(script, req, out AiOmnisClusterEditResult? res, out string err);
                return ok && res != null ? (true, res.EditedAiFileBytes, string.Empty) : (false, null, err);
            });
    }

    static AiReceipt SupportAccumulatorDryRun()
    {
        string varName = string.Empty;
        int scoreOffset = -1;
        ushort originalScore = 0, newScore = 0;

        return RunDryRun("support-accumulator", "m127",
            (_, script) =>
            {
                if (!AiMortibodySupportAccumulatorWriter.TryBuildDescriptors(script, out IReadOnlyList<AiMortibodyAccumulatorDescriptor> ds, out string err) || ds.Count == 0)
                    return (false, ds.Count == 0 ? "sem descriptors" : err);
                AiMortibodyAccumulatorDescriptor d = ds.First();
                varName = d.VariableName;
                scoreOffset = d.ScoreInstructionOffset;
                originalScore = d.CurrentScoreValue;
                newScore = (ushort)(d.CurrentScoreValue + 1);
                return (true, string.Empty);
            },
            script =>
            {
                var req = new AiMortibodyAccumulatorPatchRequest(varName, scoreOffset, originalScore, newScore);
                bool ok = AiMortibodySupportAccumulatorWriter.TryApplyPatch(script, req, out AiMortibodyAccumulatorEditResult? res, out string err);
                return ok && res != null ? (true, res.EditedAiFileBytes, string.Empty) : (false, null, err);
            },
            script =>
            {
                var req = new AiMortibodyAccumulatorPatchRequest(varName, scoreOffset, newScore, originalScore);
                bool ok = AiMortibodySupportAccumulatorWriter.TryApplyPatch(script, req, out AiMortibodyAccumulatorEditResult? res, out string err);
                return ok && res != null ? (true, res.EditedAiFileBytes, string.Empty) : (false, null, err);
            });
    }

    static AiReceipt MortiorchisDryRun()
    {
        string beatName = string.Empty;
        int cmdOffset = -1;
        ushort originalCommand = 0, swappedCommand = 0;
        int? gateOffset = null;
        ushort? gateValue = null;

        return RunDryRun("mortiorchis-companion", "m143",
            (_, script) =>
            {
                if (!AiMortiorchisCompanionWriter.TryBuildDescriptors(script, out IReadOnlyList<AiMortiorchisCompanionDescriptor> ds, out string err) || ds.Count == 0)
                    return (false, ds.Count == 0 ? "sem descriptors" : err);
                AiMortiorchisCompanionDescriptor d = ds.First();
                beatName = d.BeatName;
                cmdOffset = d.CommandInstructionOffset;
                originalCommand = d.CurrentCommand;
                swappedCommand = d.CurrentCommand == 0x608C ? (ushort)0x60A9 : (ushort)0x608C;
                gateOffset = d.GateWriteOffset;
                gateValue = d.CurrentGateValue;
                return (true, string.Empty);
            },
            script =>
            {
                var req = new AiMortiorchisCompanionPatchRequest(beatName, cmdOffset, originalCommand, swappedCommand, gateOffset, gateValue, gateValue);
                bool ok = AiMortiorchisCompanionWriter.TryApplyPatch(script, req, out AiMortiorchisCompanionEditResult? res, out string err);
                return ok && res != null ? (true, res.EditedAiFileBytes, string.Empty) : (false, null, err);
            },
            script =>
            {
                var req = new AiMortiorchisCompanionPatchRequest(beatName, cmdOffset, swappedCommand, originalCommand, gateOffset, gateValue, gateValue);
                bool ok = AiMortiorchisCompanionWriter.TryApplyPatch(script, req, out AiMortiorchisCompanionEditResult? res, out string err);
                return ok && res != null ? (true, res.EditedAiFileBytes, string.Empty) : (false, null, err);
            });
    }

    static AiReceipt FluxNativeDryRun()
    {
        string varName = string.Empty;
        int denomOffset = -1;
        ushort originalDenom = 0, newDenom = 0;
        int? numOffset = null;
        ushort? numValue = null;

        return RunDryRun("flux-native-threshold", "m142",
            (_, script) =>
            {
                if (!AiFluxNativeThresholdWriter.TryBuildDescriptors(script, out IReadOnlyList<AiFluxNativeThresholdDescriptor> ds, out string err) || ds.Count == 0)
                    return (false, ds.Count == 0 ? "sem descriptors" : err);
                AiFluxNativeThresholdDescriptor d = ds.First();
                varName = d.VariableName;
                denomOffset = d.DenominatorInstructionOffset;
                originalDenom = d.CurrentDenominator;
                newDenom = d.CurrentDenominator == 0 ? (ushort)4 : (ushort)(d.CurrentDenominator + 1);
                numOffset = d.NumeratorInstructionOffset;
                numValue = d.CurrentNumerator;
                return (true, string.Empty);
            },
            script =>
            {
                var req = new AiFluxNativeThresholdPatchRequest(varName, denomOffset, originalDenom, newDenom, numOffset, numValue, numValue);
                bool ok = AiFluxNativeThresholdWriter.TryApplyPatch(script, req, out AiFluxNativeThresholdEditResult? res, out string err);
                return ok && res != null ? (true, res.EditedAiFileBytes, string.Empty) : (false, null, err);
            },
            script =>
            {
                var req = new AiFluxNativeThresholdPatchRequest(varName, denomOffset, newDenom, originalDenom, numOffset, numValue, numValue);
                bool ok = AiFluxNativeThresholdWriter.TryApplyPatch(script, req, out AiFluxNativeThresholdEditResult? res, out string err);
                return ok && res != null ? (true, res.EditedAiFileBytes, string.Empty) : (false, null, err);
            });
    }

    static AiReceipt AnimaOdDryRun()
    {

        int maximumOffset = -1;
        ushort originalMaximum = 0, newMaximum = 0;

        return RunDryRun("anima-od-threshold", "m125",
            (_, script) =>
            {
                if (!AiAnimaOdThresholdWriter.TryBuildDescriptors(script, out IReadOnlyList<AiAnimaOdThresholdDescriptor> ds, out string err) || ds.Count == 0)
                    return (false, ds.Count == 0 ? "sem descriptors" : err);
                AiAnimaOdThresholdDescriptor d = ds.First();

                maximumOffset = d.MaximumInstructionOffset;
                originalMaximum = d.CurrentMaximum;
                newMaximum = d.CurrentMaximum == 100 ? (ushort)99 : (ushort)(d.CurrentMaximum + 1);

                return (true, string.Empty);
            },
            script =>
            {
                var req = new AiAnimaOdThresholdPatchRequest(maximumOffset, originalMaximum, newMaximum);
                bool ok = AiAnimaOdThresholdWriter.TryApplyPatch(script, req, out AiAnimaOdThresholdEditResult? res, out string err);
                return ok && res != null ? (true, res.EditedAiFileBytes, string.Empty) : (false, null, err);
            },
            script =>
            {
                var req = new AiAnimaOdThresholdPatchRequest(maximumOffset, newMaximum, originalMaximum);
                bool ok = AiAnimaOdThresholdWriter.TryApplyPatch(script, req, out AiAnimaOdThresholdEditResult? res, out string err);
                return ok && res != null ? (true, res.EditedAiFileBytes, string.Empty) : (false, null, err);
            });
    }

    static AiReceipt RoundScriptedBossDryRun()
    {
        string beatName = string.Empty;
        int cmdOffset = -1;
        ushort currentCommand = 0, newCommand = 0;
        int? repriseOffset = null;
        ushort? currentReprise = null;

        return RunDryRun("round-scripted-boss", "m238",
            (_, script) =>
            {
                if (!AiRoundScriptedBossWriter.TryBuildDescriptors(script, out IReadOnlyList<AiRoundScriptedBossDescriptor> ds, out string err) || ds.Count == 0)
                    return (false, ds.Count == 0 ? "sem descriptors" : err);
                AiRoundScriptedBossDescriptor d = ds.First();
                beatName = d.BeatName;
                cmdOffset = d.CommandInstructionOffset;
                currentCommand = d.CurrentCommand;
                newCommand = d.CurrentCommand == 0x4019 ? (ushort)0x401A : (ushort)0x4019;
                repriseOffset = d.LandingRepriseOffset;
                currentReprise = d.CurrentLandingReprise;
                return (true, string.Empty);
            },
            script =>
            {
                var req = new AiRoundScriptedBossPatchRequest(beatName, cmdOffset, currentCommand, newCommand, repriseOffset, currentReprise, currentReprise);
                bool ok = AiRoundScriptedBossWriter.TryApplyPatch(script, req, out AiRoundScriptedBossEditResult? res, out string err);
                return ok && res != null ? (true, res.EditedAiFileBytes, string.Empty) : (false, null, err);
            },
            script =>
            {
                var req = new AiRoundScriptedBossPatchRequest(beatName, cmdOffset, newCommand, currentCommand, repriseOffset, currentReprise, currentReprise);
                bool ok = AiRoundScriptedBossWriter.TryApplyPatch(script, req, out AiRoundScriptedBossEditResult? res, out string err);
                return ok && res != null ? (true, res.EditedAiFileBytes, string.Empty) : (false, null, err);
            });
    }

    static AiReceipt ReactiveSensorDryRun(string monsterId)
    {
        string varName = string.Empty;
        int stateOffset = -1;
        ushort originalState = 0, newState = 0;

        return RunDryRun("reactive-sensor", monsterId,
            (_, script) =>
            {
                if (!AiReactiveSensorWriter.TryBuildDescriptors(script, out IReadOnlyList<AiReactiveSensorDescriptor> ds, out string err) || ds.Count == 0)
                    return (false, ds.Count == 0 ? "sem descriptors" : err);
                AiReactiveSensorDescriptor d = ds.First();
                varName = d.VariableName;
                stateOffset = d.StateWriteOffset;
                originalState = d.CurrentStateValue;
                newState = (ushort)(d.CurrentStateValue + 1);
                return (true, string.Empty);
            },
            script =>
            {
                var req = new AiReactiveSensorPatchRequest(varName, stateOffset, originalState, newState);
                bool ok = AiReactiveSensorWriter.TryApplyPatch(script, req, out AiReactiveSensorEditResult? res, out string err);
                return ok && res != null ? (true, res.EditedAiFileBytes, string.Empty) : (false, null, err);
            },
            script =>
            {
                var req = new AiReactiveSensorPatchRequest(varName, stateOffset, newState, originalState);
                bool ok = AiReactiveSensorWriter.TryApplyPatch(script, req, out AiReactiveSensorEditResult? res, out string err);
                return ok && res != null ? (true, res.EditedAiFileBytes, string.Empty) : (false, null, err);
            });
    }

    static AiReceipt IndirectDispatchRowDryRun()
    {
        string unitId = string.Empty, roleKey = string.Empty, roleLabel = string.Empty;
        int sourceOffset = -1;
        ushort oldValue = 0, newValue = 0;

        return RunDryRun("indirect-dispatch-row", "m124",
            (monsterBin, script) =>
            {
                IReadOnlyList<AiIndirectDispatchUnit> units = AiAutomation.DetectIndirectDispatchUnits(monsterBin, script);
                if (units.Count == 0) return (false, "sem unidades indiretas detectadas");
                AiIndirectDispatchUnit? u = units.FirstOrDefault(x => x.EditableTargetSlots.Any(s => s.CanEdit && s.EditableOperands.Count > 0));
                if (u == null) return (false, "sem unidades com slot editavel");
                AiIndirectDispatchEditableTargetSlot slot = u.EditableTargetSlots.First(s => s.CanEdit && s.EditableOperands.Count > 0);
                AiIndirectDispatchEditableOperand operand = slot.EditableOperands.First();
                unitId = u.UnitId;
                roleKey = operand.RoleKey;
                roleLabel = operand.RoleLabel;
                sourceOffset = operand.InstructionOffset;
                oldValue = operand.CurrentValue;
                newValue = operand.CurrentValue == 0 ? (ushort)1 : (ushort)(operand.CurrentValue - 1);
                return (true, string.Empty);
            },
            script =>
            {
                if (sourceOffset < 0) return (false, null, "descriptors nao montados");
                var req = new AiIndirectDispatchEditRequest(unitId, new[] { new AiIndirectDispatchValueEdit(roleKey, roleLabel, sourceOffset, oldValue, newValue) });
                bool ok = AiIndirectDispatchRowOnlyEditor.TryApplyEdits(script, req, out AiIndirectDispatchEditResult? res, out string err);
                return ok && res != null ? (true, res.EditedAiFileBytes, string.Empty) : (false, null, err);
            },
            script =>
            {
                if (sourceOffset < 0) return (false, null, "descriptors nao montados");
                var req = new AiIndirectDispatchEditRequest(unitId, new[] { new AiIndirectDispatchValueEdit(roleKey, roleLabel, sourceOffset, newValue, oldValue) });
                bool ok = AiIndirectDispatchRowOnlyEditor.TryApplyEdits(script, req, out AiIndirectDispatchEditResult? res, out string err);
                return ok && res != null ? (true, res.EditedAiFileBytes, string.Empty) : (false, null, err);
            });
    }
}
