using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Ai;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.MonsterAiEditor
{
    internal partial class MonsterAiEditor_DataModel
    {
        const byte PushLiteralOpcode = 0xAE;
        const byte PushVariableOpcode = 0x9F;
        const byte PopVariableOpcode = 0xA0;
        const byte FluxCallOpcode = 0xB5;
        const byte MultiplyOpcode = 0x16;
        const byte DivideOpcode = 0x17;
        const byte AddOpcode = 0x14;
        const byte SubtractOpcode = 0x15;
        const ushort ReadChrPropertyFuncId = 0x700F;

        public ObservableCollection<AiAdvancedFluxThresholdEvidenceVm> AdvancedFluxThresholdRows { get; } = new();

        [ObservableProperty] private string advancedFluxThresholdSummary =
            Strings.U_Ai_FluxNoRawEvidence;
        [ObservableProperty] private string advancedFluxThresholdHonestSummary =
            Strings.U_Ai_FluxHonestSummary;
        [ObservableProperty] private string advancedFluxThresholdApplySummary =
            Strings.U_Ai_FluxNoNativeWriter;

        public bool HasAdvancedFluxThresholdEvidence => AdvancedFluxThresholdRows.Count > 0;

        void UpdateAdvancedFluxThresholdContext()
        {
            AdvancedFluxThresholdRows.Clear();

            if (selectedScript == null
                || !selectedScript.HasScript
                || !TryGetSelectedMonsterNumber(out int monsterNumber)
                || monsterNumber != 142)
            {
                AdvancedFluxThresholdSummary = Strings.U_Ai_FluxNoEvidence;
                AdvancedFluxThresholdHonestSummary = Strings.U_Ai_FluxHonestNoFocus;
                AdvancedFluxThresholdApplySummary = Strings.U_Ai_FluxWriterOutside;
                OnPropertyChanged(nameof(HasAdvancedFluxThresholdEvidence));
                return;
            }

            if (TryBuildAdvancedFluxThresholdEvidence("priv0018", out AiAdvancedFluxThresholdEvidenceVm? first))
                AdvancedFluxThresholdRows.Add(first);
            if (TryBuildAdvancedFluxThresholdEvidence("priv001C", out AiAdvancedFluxThresholdEvidenceVm? second))
                AdvancedFluxThresholdRows.Add(second);

            if (AdvancedFluxThresholdRows.Count == 0)
            {
                AdvancedFluxThresholdSummary = Strings.U_Ai_FluxParseNotClosed;
                AdvancedFluxThresholdHonestSummary = Strings.U_Ai_FluxPreviewOnly;
                AdvancedFluxThresholdApplySummary = Strings.U_Ai_FluxNoProvenChain;
                OnPropertyChanged(nameof(HasAdvancedFluxThresholdEvidence));
                return;
            }

            bool hasWriter = AdvancedFluxThresholdRows.Any(row => row.CanEditRawThreshold);
            AdvancedFluxThresholdSummary =
                hasWriter
                    ? string.Format(Strings.U_Ai_FluxRawPackage, AdvancedFluxThresholdRows.Count)
                    : string.Format(Strings.U_Ai_FluxRawPackageNoWriter, AdvancedFluxThresholdRows.Count);
            AdvancedFluxThresholdHonestSummary =
                hasWriter
                    ? Strings.U_Ai_FluxNarrowPatch
                    : Strings.U_Ai_FluxIsRawIR;
            AdvancedFluxThresholdApplySummary =
                hasWriter
                    ? Strings.U_Ai_FluxExperimentalWriter
                    : "Nenhuma chain raw do Flux ficou editavel neste parse.";
            OnPropertyChanged(nameof(HasAdvancedFluxThresholdEvidence));
        }

        public bool ApplyAdvancedFluxThresholdEdit(AiAdvancedFluxThresholdEvidenceVm? row)
        {
            if (selectedScript != null && HasPendingAuthoringEdits)
            {
                AdvancedFluxThresholdApplySummary = Strings.AiAdvancedPendingEdits;
                return false;
            }
            if (row != null && !AdvancedFluxThresholdRows.Contains(row))
            {
                AdvancedFluxThresholdApplySummary = Strings.AiAdvancedEditorChanged;
                return false;
            }

            if (row == null)
            {
                AdvancedFluxThresholdApplySummary = Strings.U_Ai_FluxNoChain;
                return false;
            }

            if (!row.CanEditRawThreshold)
            {
                AdvancedFluxThresholdApplySummary =
                    string.Format(Strings.U_Ai_FluxPreviewReadOnly, row.VariableName);
                return false;
            }

            if (selectedScript == null || !selectedScript.HasScript || string.IsNullOrWhiteSpace(selectedPath))
            {
                AdvancedFluxThresholdApplySummary = Strings.U_Ai_FluxNeedAiFile;
                return false;
            }

            ushort newNumerator = 1;
            if (!AiAdvancedNumericInput.TryParse(row.DenominatorText, out ushort newDenominator)
                || (row.HasNumeratorEditor && !AiAdvancedNumericInput.TryParse(row.NumeratorText, out newNumerator)))
            {
                AdvancedFluxThresholdApplySummary = Strings.AiAdvancedInvalidNumber;
                return false;
            }
            if (newDenominator == 0)
            {
                AdvancedFluxThresholdApplySummary = $"{row.RoleLabel}: o divisor precisa ser maior que zero.";
                return false;
            }

            if (newNumerator == 0)
            {
                AdvancedFluxThresholdApplySummary = $"{row.RoleLabel}: o numerador precisa ser maior que zero.";
                return false;
            }

            if (newNumerator > newDenominator)
            {
                AdvancedFluxThresholdApplySummary =
                    string.Format(Strings.U_Ai_FluxNarrowLimit, row.RoleLabel);
                return false;
            }

            if (newDenominator == row.CurrentDenominator
                && (!row.HasNumeratorEditor || newNumerator == row.CurrentNumerator))
            {
                AdvancedFluxThresholdApplySummary = $"{row.RoleLabel}: nada mudou nesta chain raw.";
                return false;
            }

            var request = new AiFluxNativeThresholdPatchRequest(
                row.VariableName,
                row.DenominatorInstructionOffset,
                row.CurrentDenominator,
                newDenominator,
                row.NumeratorInstructionOffset,
                row.HasNumeratorEditor ? row.CurrentNumerator : null,
                row.HasNumeratorEditor ? newNumerator : null);

            if (!AiFluxNativeThresholdWriter.TryApplyPatch(
                    AiScript_File.Read(AiScript_File.Write(selectedScript)),
                    request,
                    out AiFluxNativeThresholdEditResult? result,
                    out string error)
                || result == null)
            {
                AdvancedFluxThresholdApplySummary = error;
                return false;
            }

            if (!AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(
                    result.EditedAiFileBytes,
                    selectedScript,
                    selectedScript.OriginalAiFileBytes.Length,
                    out _,
                    out string validationReason))
            {
                AdvancedFluxThresholdApplySummary =
                    $"Validacao estrutural bloqueou o patch raw do Flux: {validationReason}";
                return false;
            }

            try
            {
                AiAdvancedFileWriter.Save(selectedPath, selectedScript.OriginalAiFileBytes, result.EditedAiFileBytes);
            }
            catch (Exception ex)
            {
                AdvancedFluxThresholdApplySummary = $"Save abortado no patch raw do Flux: {ex.Message}";
                return false;
            }

            bool reloaded = ReloadSelectedFromDisk();
            RefreshPhaseRotationAudit();
            RebuildAdvancedPhaseUnits();

            string percentSummary = row.HasNumeratorEditor
                ? $"{newNumerator}/{newDenominator}"
                : $"1/{newDenominator}";
            string summary =
                $"✅ {row.RoleLabel} salvo em {Path.GetFileName(selectedPath)} " +
                $"({percentSummary}, {result.ChangedBytes.Count} byte(s) no diff, raw family-specific, .prev.bak criado). " +
                $"{(reloaded ? "Readback ok." : "Readback parcial; use Refresh.")} RT2 ainda recomendado.";

            AdvancedFluxThresholdApplySummary = summary;
            RemoveActionSummary = summary;
            return true;
        }

        bool TryBuildAdvancedFluxThresholdEvidence(string variableName, out AiAdvancedFluxThresholdEvidenceVm? row)
        {
            row = null;

            AiPhaseVariableAuditRow? audit = PhaseVariableAudits.FirstOrDefault(candidate =>
                candidate.VariableName.Equals(variableName, StringComparison.OrdinalIgnoreCase));
            if (audit == null || !TryGetRuntimeVariableIndex(audit, out ushort variableIndex) || selectedScript == null)
                return false;

            var instructions = selectedScript.Instructions;
            int storeIndex = FindVariableStoreIndex(instructions, variableIndex);
            if (storeIndex < 0)
                return false;

            int formulaStart = FindFormulaStartIndex(instructions, storeIndex);
            string rawFormula = string.Join(
                " -> ",
                instructions
                    .Skip(formulaStart)
                    .Take(storeIndex - formulaStart + 1)
                    .Select(FormatInstructionForFluxEvidence));

            int rereadIndex = FindVariableReadIndex(instructions, storeIndex + 1, variableIndex);
            string comparisonSummary = rereadIndex < 0
                ? Strings.U_Ai_FluxReReadNotRecognized
                : $"Primeira releitura posterior em 0x{instructions[rereadIndex].Offset:X4}.";
            string followUpSummary = BuildFluxFollowUpSummary(instructions, rereadIndex);

            AiFluxNativeThresholdDescriptor? descriptor = null;
            AiFluxNativeThresholdWriter.TryBuildDescriptor(selectedScript, variableName, out descriptor, out _);

            row = new AiAdvancedFluxThresholdEvidenceVm(
                variableName,
                $"store @0x{instructions[storeIndex].Offset:X4}",
                rawFormula,
                comparisonSummary,
                followUpSummary,
                descriptor);
            return true;
        }

        static int FindVariableStoreIndex(
            System.Collections.Generic.IReadOnlyList<AiInstruction> instructions,
            ushort variableIndex)
        {
            for (int i = 0; i < instructions.Count; i++)
            {
                AiInstruction instruction = instructions[i];
                if (instruction.Opcode == PopVariableOpcode && instruction.HasOperand && instruction.Operand == variableIndex)
                    return i;
            }

            return -1;
        }

        static int FindFormulaStartIndex(
            System.Collections.Generic.IReadOnlyList<AiInstruction> instructions,
            int storeIndex)
        {
            int callIndex = -1;
            for (int i = storeIndex - 1; i >= 0 && i >= storeIndex - 10; i--)
            {
                AiInstruction instruction = instructions[i];
                if (instruction.Opcode == FluxCallOpcode
                    && instruction.HasOperand
                    && instruction.Operand == ReadChrPropertyFuncId)
                {
                    callIndex = i;
                    break;
                }
            }

            if (callIndex >= 2
                && IsFormulaOperandInstruction(instructions[callIndex - 2])
                && IsFormulaOperandInstruction(instructions[callIndex - 1]))
            {
                return callIndex - 2;
            }

            if (callIndex >= 0)
                return callIndex;

            int fallback = Math.Max(0, storeIndex - 4);
            while (fallback < storeIndex && !IsFormulaOperandInstruction(instructions[fallback]))
                fallback++;
            return fallback;
        }

        static int FindVariableReadIndex(
            System.Collections.Generic.IReadOnlyList<AiInstruction> instructions,
            int startIndex,
            ushort variableIndex)
        {
            for (int i = Math.Max(0, startIndex); i < instructions.Count; i++)
            {
                AiInstruction instruction = instructions[i];
                if (instruction.Opcode == PushVariableOpcode
                    && instruction.HasOperand
                    && instruction.Operand == variableIndex)
                {
                    return i;
                }
            }

            return -1;
        }

        static bool IsFormulaOperandInstruction(AiInstruction instruction) =>
            instruction.Opcode == PushLiteralOpcode
            || instruction.Opcode == PushVariableOpcode
            || instruction.Opcode == FluxCallOpcode
            || instruction.Opcode == MultiplyOpcode
            || instruction.Opcode == DivideOpcode
            || instruction.Opcode == AddOpcode
            || instruction.Opcode == SubtractOpcode;

        static string FormatInstructionForFluxEvidence(AiInstruction instruction)
        {
            string mnemonic = AiScript_File.Mnemonic(instruction.Opcode);
            string operand = instruction.HasOperand ? AiScript_File.OperandGloss(instruction) : string.Empty;
            return string.IsNullOrWhiteSpace(operand)
                ? mnemonic
                : $"{mnemonic} {operand}";
        }

        static string BuildFluxFollowUpSummary(
            System.Collections.Generic.IReadOnlyList<AiInstruction> instructions,
            int rereadIndex)
        {
            if (rereadIndex < 0)
                return Strings.U_Ai_FluxCompareCastNotBound;

            int windowEnd = Math.Min(instructions.Count, rereadIndex + 48);
            var commands = instructions
                .Skip(rereadIndex)
                .Take(windowEnd - rereadIndex)
                .Where(instruction => instruction.Opcode == PushLiteralOpcode
                    && instruction.HasOperand
                    && AiCommandId.IsCommandOperand(instruction.Operand))
                .Select(instruction =>
                {
                    AiCommandDecode decoded = AiCommandId.Decode(instruction.Operand);
                    return $"{decoded.Name} 0x{instruction.Operand:X4} @0x{instruction.Offset:X4}";
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();

            if (commands.Count == 0)
                return "Releitura vista, mas nenhum comando literal reconhecivel apareceu na janela curta deste parse.";

            return "Janela curta do mesmo bloco tambem mostra: " + string.Join(", ", commands) + ".";
        }
    }

    internal sealed partial class AiAdvancedFluxThresholdEvidenceVm : ObservableObject
    {
        public AiAdvancedFluxThresholdEvidenceVm(
            string variableName,
            string siteSummary,
            string rawFormulaSummary,
            string comparisonSummary,
            string followUpSummary,
            AiFluxNativeThresholdDescriptor? descriptor)
        {
            VariableName = variableName;
            SiteSummary = siteSummary;
            RawFormulaSummary = rawFormulaSummary;
            ComparisonSummary = comparisonSummary;
            FollowUpSummary = followUpSummary;

            CanEditRawThreshold = descriptor != null;
            RoleLabel = descriptor?.RoleLabel ?? Strings.U_Ai_FluxObservedRawChain;
            CurrentThresholdSummary = descriptor?.DerivedThresholdSummary ?? Strings.U_Ai_FluxNoRawWriter;
            WriterSummary = descriptor == null
                ? Strings.U_Ai_FluxPreviewReadOnly
                : descriptor.HasExplicitNumerator
                    ? $"Writer experimental: patch raw em 0x{descriptor.DenominatorInstructionOffset:X4} (divisor) e 0x{descriptor.NumeratorInstructionOffset!.Value:X4} (numerador)."
                    : $"Writer experimental: patch raw em 0x{descriptor.DenominatorInstructionOffset:X4} (divisor).";

            CurrentDenominator = descriptor?.CurrentDenominator ?? (ushort)0;
            DenominatorInstructionOffset = descriptor?.DenominatorInstructionOffset ?? -1;
            CurrentNumerator = descriptor?.CurrentNumerator ?? (ushort)1;
            NumeratorInstructionOffset = descriptor?.NumeratorInstructionOffset;

            DenominatorText = CanEditRawThreshold ? CurrentDenominator.ToString() : string.Empty;
            NumeratorText = HasNumeratorEditor ? CurrentNumerator.ToString() : "1";
        }

        public string VariableName { get; }
        public string SiteSummary { get; }
        public string RawFormulaSummary { get; }
        public string ComparisonSummary { get; }
        public string FollowUpSummary { get; }
        public bool CanEditRawThreshold { get; }
        public string RoleLabel { get; }
        public string CurrentThresholdSummary { get; }
        public string WriterSummary { get; }
        public ushort CurrentDenominator { get; }
        public int DenominatorInstructionOffset { get; }
        public ushort CurrentNumerator { get; }
        public int? NumeratorInstructionOffset { get; }
        public bool HasNumeratorEditor => NumeratorInstructionOffset.HasValue;

        [ObservableProperty] private string denominatorText = string.Empty;
        [ObservableProperty] private string numeratorText = "1";
    }
}
