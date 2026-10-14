using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.Ai.AtelScript;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.MonsterAiEditor
{
    /// <summary>
    /// F5-L4 / F6.2: painel de script ATEL de alto nível no Monster AI Editor.
    /// • Dry-run: compila o texto do script contra o monstro SELECIONADO e mostra o preview 3 camadas
    ///   (bytes alterados / instruções / resumo semântico) — nada é salvo.
    /// • Aplicar (F6.2): re-verifica a hash-precondição (F1/AiPatchGuard) e, se o arquivo não mudou
    ///   em disco, spliceia o AiFile compilado de volta ao monster_*.bin e salva, emitindo o receipt.
    /// </summary>
    internal partial class MonsterAiEditor_DataModel
    {
        [ObservableProperty] private string atelScriptText =
            "// Script ATEL de alto nível (F5)\n"
            + "if battleVar0014 < 5 {\n"
            + "  battleVar0014 = battleVar0014 + 1;\n"
            + "} else {\n"
            + "  battleVar0014 = 0;\n"
            + "}";

        [ObservableProperty] private string atelScriptResult = "";

        [ObservableProperty] private string atelScriptDiffSummary = "";

        [ObservableProperty] private IReadOnlyList<string> atelScriptDiffLines = Array.Empty<string>();

        /// <summary>Compila o script ATEL contra o monstro selecionado em modo dry-run (nada é salvo),
        /// mostrando o preview 3 camadas: bytes alterados / instruções / resumo semântico.</summary>
        public void CompileAtelDryRun()
        {
            AtelScriptResult = "";
            AtelScriptDiffSummary = "";
            AtelScriptDiffLines = Array.Empty<string>();

            if (!TryBuildAtelCompile(out string resultText, out byte[] originalAi, out byte[] editedAi, out AiScriptFile original, out AiScriptFile edited, out AtelCompileResult result))
            {
                AtelScriptResult = resultText;
                return;
            }

            // camada 1: bytes alterados
            int byteChanges = originalAi.Zip(editedAi, (before, after) => before != after).Count(changed => changed)
                + Math.Abs(originalAi.Length - editedAi.Length);
            // camada 2: instruções
            IReadOnlyList<string> instructionChanges = FormatAtelInstructionDiff(original, edited);
            AtelScriptDiffLines = instructionChanges.ToList();
            // camada 3: resumo semântico
            string summary = string.Join(Environment.NewLine, FormatAtelInstructionDiff(original, edited).Take(6));
            AtelScriptDiffSummary =
                string.Format(Strings.U_Ai_AslAppliedSteps, result.AppliedSteps, result.Summary) + "\n"
                + string.Format(Strings.U_Ai_AslBytesChanged, byteChanges, instructionChanges.Count, summary);
            AtelScriptResult = Strings.F2_dry_run_ok_nothing_was_saved_use_apply_s_00644fba;
        }

        /// <summary>F6.2: aplica o script ATEL de verdade. Re-verifica a hash-precondição do AiFile
        /// (aborta se o arquivo mudou em disco desde o load), spliceia o AiFile compilado de volta ao
        /// monster_*.bin, salva e emite o receipt 3 camadas.</summary>
        public void CompileAtelApply()
        {
            AtelScriptResult = "";
            AtelScriptDiffSummary = "";
            AtelScriptDiffLines = Array.Empty<string>();

            if (SelectedMonster == null || string.IsNullOrEmpty(SelectedMonster.Path))
            {
                AtelScriptResult = Strings.F2_select_a_monster_in_the_list_first_13bea8e8;
                return;
            }

            try
            {
                if (!TryBuildAtelCompile(out string resultText, out byte[] originalAi, out byte[] editedAi,
                        out AiScriptFile original, out AiScriptFile edited, out AtelCompileResult result))
                {
                    AtelScriptResult = resultText;
                    return;
                }

                string path = SelectedMonster.Path;
                string preconditionHash = AiPatchGuard.ComputeSha256(originalAi);
                AiAdvancedFileWriter.Save(path, originalAi, editedAi);
                bool reloaded = ReloadSelectedFromDisk();

                // receipt 3 camadas (bytes / instruções / resumo) + splice round-trip
                string summary = string.Join(Environment.NewLine, FormatAtelInstructionDiff(original, edited).Take(6));
                AtelScriptDiffLines = FormatAtelInstructionDiff(original, edited).ToList();
                AtelScriptDiffSummary =
                    string.Format(Strings.U_Ai_AslAppliedSteps, result.AppliedSteps, result.Summary) + "\n"
                    + summary;
                AtelScriptResult = reloaded
                    ? string.Format(Strings.U_Ai_AslApplied, path, new FileInfo(path).Length)
                        + string.Format(Strings.U_Ai_AslHashSplice, AiPatchGuard.ShortHash(preconditionHash), "OK")
                    : string.Format(Strings.U_Ai_ReloadFailed, Path.GetFileName(path));
            }
            catch (AtelSyntaxException ex)
            {
                AtelScriptResult = Strings.U_Ai_AslSyntaxError + ex.Message;
            }
            catch (AtelEmitException ex)
            {
                AtelScriptResult = Strings.U_Ai_AslEmitError + ex.Message;
            }
            catch (Exception ex)
            {
                AtelScriptResult = Strings.U_Ai_AslError + ex.Message + " " + Strings.AiEditorWriteRecovery;
            }
        }

        static IReadOnlyList<string> FormatAtelInstructionDiff(AiScriptFile original, AiScriptFile edited) =>
            AiScript_Diff.Compare(original, edited)
                .Where(change => change.Type != AiDiffType.Unchanged)
                .Select(change => $"w{change.WorkerIndex} | " + (change.Type switch
                {
                    AiDiffType.Added => $"+ {change.EditedText}",
                    AiDiffType.Removed => $"- {change.OriginalText}",
                    _ => $"{change.OriginalText} -> {change.EditedText}",
                })).ToArray();

        /// <summary>Compila o texto ATEL atual contra o monstro selecionado. Em caso de sucesso preenche
        /// <paramref name="originalAi"/>/<paramref name="editedAi"/> e os scripts correspondentes.</summary>
        private bool TryBuildAtelCompile(
            out string resultText,
            out byte[] originalAi,
            out byte[] editedAi,
            out AiScriptFile original,
            out AiScriptFile edited,
            out AtelCompileResult result)
        {
            originalAi = Array.Empty<byte>();
            editedAi = Array.Empty<byte>();
            original = null!;
            edited = null!;
            result = null!;
            resultText = "";

            if (SelectedMonster == null || string.IsNullOrEmpty(SelectedMonster.Path))
            {
                resultText = Strings.F2_select_a_monster_in_the_list_first_13bea8e8;
                return false;
            }

            if (selectedScript == null || HasPendingAuthoringEdits)
            {
                resultText = Strings.AiAdvancedPendingEdits;
                return false;
            }

            try
            {
                byte[] monster = File.ReadAllBytes(SelectedMonster.Path);
                byte[]? ai = AiScript_File.SliceAiFileFromMonster(monster);
                if (ai == null)
                {
                    resultText = Strings.U_Ai_AslNoAiPartition;
                    return false;
                }
                if (!ai.SequenceEqual(selectedScript.OriginalAiFileBytes))
                {
                    resultText = Strings.AiAdvancedFileChanged;
                    return false;
                }
                AiScriptFile script = AiScript_File.Read(ai);
                AiWorker? worker = AiAutomation.PickCombatWorker(script);
                if (worker == null)
                {
                    resultText = Strings.U_Ai_AslNoWorker;
                    return false;
                }
                int entrypoint = AiAutomation.PickMainEntrypoint(script, worker);

                AtelProgram program = new AtelParser(AtelScriptText).ParseProgram();
                AtelCompileResult compiled = AtelProgramCompiler.CompileProgram(program, new AtelCompileContext
                {
                    Script = script,
                    WorkerIndex = worker.Index,
                    EntrypointIndex = entrypoint,
                });

                originalAi = ai;
                editedAi = compiled.AiFile;
                original = script;
                edited = AiScript_File.Read(compiled.AiFile);
                result = compiled;
                return true;
            }
            catch (AtelSyntaxException ex)
            {
                resultText = Strings.U_Ai_AslSyntaxError + ex.Message;
            }
            catch (AtelEmitException ex)
            {
                resultText = Strings.U_Ai_AslEmitError + ex.Message;
            }
            catch (Exception ex)
            {
                resultText = Strings.U_Ai_AslError + ex.Message;
            }
            return false;
        }
    }
}
