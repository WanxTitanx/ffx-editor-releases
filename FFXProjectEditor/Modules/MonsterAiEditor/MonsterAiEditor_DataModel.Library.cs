using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Ai;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.MonsterAiEditor
{
    // BEHAVIOR LIBRARY surface — parameterized, corpus-faithful snippets (FfxLib/Ai/AiSnippetLibrary).
    //   * LINEAR snippets expand to straight-line instructions inserted into the AI Assembler list (then Save
    //     Structural via the RT0-proven Rebuild).
    //   * GUARDED snippets add a real branch, so they expand through AiScript_File.AppendGuardedAction (which grows
    //     the chosen worker's jump-table) and write the m###.bin directly — STRUCTURE proven offline 346/346;
    //     in-game (RT2) behaviour must be confirmed via the DINPUT8 probe before treating a template as production.
    internal partial class MonsterAiEditor_DataModel
    {
        public ObservableCollection<AiSnippetVm> LibrarySnippets { get; } = new();
        public IReadOnlyList<AiCommandCategory> CommandCategories { get; } =
            new[] { AiCommandCategory.Character, AiCommandCategory.Monster, AiCommandCategory.Monster2 };
        public ObservableCollection<AiCommandOption> TemplateCommandOptions { get; } = new();
        public ObservableCollection<AiWorkerChoice> TemplateWorkers { get; } = new();
        public ObservableCollection<int> TemplateEntrypoints { get; } = new();

        [ObservableProperty] private AiSnippetVm? selectedSnippet;
        [ObservableProperty] private AiCommandCategory templateCommandCategory = AiCommandCategory.Monster;
        [ObservableProperty] private AiCommandOption? templateCommandOption;
        [ObservableProperty] private string templateValue = "0";
        [ObservableProperty] private string templateValue2 = "1";
        [ObservableProperty] private AiWorkerChoice? selectedTemplateWorker;
        [ObservableProperty] private int selectedTemplateEntrypoint;
        [ObservableProperty] private string templatePreview = string.Empty;
        [ObservableProperty] private string librarySummary = Strings.U_Ai_LibrarySummary;

        // Derived flags the axaml binds (visibility of the param widgets / button label).
        public bool TemplateUsesCommand => SelectedSnippet?.Snippet.UsesCommand ?? false;
        public bool TemplateUsesValue => SelectedSnippet?.Snippet.UsesValue ?? false;
        public bool TemplateUsesValue2 => SelectedSnippet?.Snippet.UsesValue2 ?? false;
        public bool TemplateIsGuarded => SelectedSnippet?.Snippet.Kind == AiSnippetKind.GuardedAction;
        public string TemplateValueLabel => SelectedSnippet?.Snippet.ValueLabel ?? Strings.CommonValue;
        public string TemplateValue2Label => SelectedSnippet?.Snippet.Value2Label ?? Strings.AiEditorSecondValue;
        public string TemplateActionLabel => TemplateIsGuarded ? Strings.AiEditorApplyTemplate : Strings.AiEditorInsertTemplate;

        void SeedLibrary()
        {
            if (LibrarySnippets.Count == 0)
            {
                foreach (AiSnippet s in AiSnippetLibrary.All) LibrarySnippets.Add(new AiSnippetVm(s));
                SelectedSnippet ??= LibrarySnippets.FirstOrDefault();
                ReloadTemplateCommandOptions();
            }
            SeedVisualBehaviorLibrary();
        }

        // Called from UpdateSelected (main partial) when the monster changes — refresh the worker/entrypoint pickers.
        void RebuildTemplateWorkerChoices()
        {
            TemplateWorkers.Clear();
            TemplateEntrypoints.Clear();
            if (selectedScript == null || !selectedScript.HasScript) { SelectedTemplateWorker = null; return; }
            foreach (AiWorker w in selectedScript.Workers)
                if (w.Entrypoints.Count > 0)
                    TemplateWorkers.Add(new AiWorkerChoice(w.Index, w.Entrypoints.Count, w.InferredType ?? "?"));
            AiEventHook? preferred = TryResolveOnTurnHook(out AiEventHook hook, out _) ? hook : null;
            SelectedTemplateWorker = preferred.HasValue
                ? TemplateWorkers.FirstOrDefault(worker => worker.Index == preferred.Value.WorkerIndex)
                : TemplateWorkers.FirstOrDefault();
            if (preferred.HasValue && SelectedTemplateWorker != null)
                SelectedTemplateEntrypoint = preferred.Value.EntrypointIndex;
            RefreshTemplatePreview();
        }

        partial void OnSelectedSnippetChanged(AiSnippetVm? value)
        {
            NotifyTemplateFlags();
            if (value?.Snippet.PreferCategory is AiCommandCategory pref && pref != TemplateCommandCategory)
                TemplateCommandCategory = pref;      // triggers ReloadTemplateCommandOptions
            else
                RefreshTemplatePreview();
        }
        partial void OnTemplateCommandCategoryChanged(AiCommandCategory value) => ReloadTemplateCommandOptions();
        partial void OnTemplateCommandOptionChanged(AiCommandOption? value) => RefreshTemplatePreview();
        partial void OnTemplateValueChanged(string value) => RefreshTemplatePreview();
        partial void OnTemplateValue2Changed(string value) => RefreshTemplatePreview();
        partial void OnSelectedTemplateWorkerChanged(AiWorkerChoice? value)
        {
            TemplateEntrypoints.Clear();
            if (value != null) for (int i = 0; i < value.EntrypointCount; i++) TemplateEntrypoints.Add(i);
            SelectedTemplateEntrypoint = 0;
            RefreshTemplatePreview();
        }

        void NotifyTemplateFlags()
        {
            OnPropertyChanged(nameof(TemplateUsesCommand));
            OnPropertyChanged(nameof(TemplateUsesValue));
            OnPropertyChanged(nameof(TemplateUsesValue2));
            OnPropertyChanged(nameof(TemplateIsGuarded));
            OnPropertyChanged(nameof(TemplateValueLabel));
            OnPropertyChanged(nameof(TemplateValue2Label));
            OnPropertyChanged(nameof(TemplateActionLabel));
        }

        void ReloadTemplateCommandOptions()
        {
            TemplateCommandOptions.Clear();
            foreach (AiCommandOption o in AiCommandId.OptionsFor(TemplateCommandCategory)) TemplateCommandOptions.Add(o);
            TemplateCommandOption = TemplateCommandOptions.FirstOrDefault();
            RefreshTemplatePreview();
        }

        static bool TryParseTemplateNumber(string? text, out ushort value)
        {
            text = (text ?? string.Empty).Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            // Template fields have always used hexadecimal by default.
            return ushort.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
        }
        static ushort ParseU16(string? text) => TryParseTemplateNumber(text, out ushort value) ? value : (ushort)0;

        AiSnippetArgs BuildArgs() => new AiSnippetArgs(
            TemplateCommandOption?.Operand ?? 0, ParseU16(TemplateValue), ParseU16(TemplateValue2));

        void RefreshTemplatePreview()
        {
            AiSnippet? s = SelectedSnippet?.Snippet;
            if (s == null) { TemplatePreview = string.Empty; return; }
            try
            {
                var sb = new StringBuilder();
                if (s.Kind == AiSnippetKind.Linear)
                {
                    foreach (AiInstruction i in s.ExpandLinear(BuildArgs())) sb.AppendLine(Line(i));
                }
                else
                {
                    var (guard, action) = s.ExpandGuarded(BuildArgs());
                    sb.AppendLine("; guard (leaves 1 bool):");
                    foreach (AiInstruction i in guard) sb.AppendLine(Line(i));
                    sb.AppendLine("  D7   POPXNCJMP  -> rejoin (skip action if false)");
                    sb.AppendLine("; action:");
                    foreach (AiInstruction i in action) sb.AppendLine(Line(i));
                    sb.AppendLine("  B0   JMP        -> original entrypoint");
                }
                TemplatePreview = sb.ToString().TrimEnd();
            }
            catch (Exception ex) { TemplatePreview = string.Format(Strings.U_Ai_LibInvalidTemplate, ex.Message); }

            static string Line(AiInstruction i) =>
                $"  {AiScript_File.Mnemonic(i.Opcode),-10} {(i.HasOperand ? $"0x{i.Operand:X4}" : "")}";
        }

        // UI action — linear: insert rows into the AI Assembler list; guarded: append + grow + write the m###.bin.
        public void ApplyOrInsertTemplate()
        {
            AiSnippet? s = SelectedSnippet?.Snippet;
            if (selectedScript == null || !selectedScript.HasScript || selectedPath == null)
            {
                LibrarySummary = Strings.U_Ai_LibNeedAiFile;
                return;
            }
            if (s == null) { LibrarySummary = Strings.U_Ai_LibSelectTemplate; return; }
            if ((s.UsesValue && !TryParseTemplateNumber(TemplateValue, out _))
                || (s.UsesValue2 && !TryParseTemplateNumber(TemplateValue2, out _)))
            { LibrarySummary = Strings.AiAdvancedInvalidNumber; return; }

            if (s.Kind == AiSnippetKind.Linear)
            {
                List<AiInstruction> instrs;
                try { instrs = s.ExpandLinear(BuildArgs()); }
                catch (Exception ex) { LibrarySummary = string.Format(Strings.U_Ai_LibNotExpanded, ex.Message); return; }
                int at = SelectedAssemblerRow != null
                    ? AssemblerInstructions.IndexOf(SelectedAssemblerRow) + 1 : AssemblerInstructions.Count;
                AiAsmRow? last = null;
                foreach (AiInstruction ins in instrs)
                {
                    var row = new AiAsmRow(ins.Opcode, ins.Operand, RefreshAssemblerPreview);
                    AssemblerInstructions.Insert(at++, row);
                    last = row;
                }
                if (last != null) SelectedAssemblerRow = last;
                RefreshAssemblerPreview();
                LibrarySummary = string.Format(Strings.U_Ai_LibInserted, s.Name, instrs.Count);
                return;
            }

            // GuardedAction: build, append (grows the worker jump-table), validate, splice-grow, write, reload.
            if (SelectedTemplateWorker == null)
            {
                LibrarySummary = Strings.U_Ai_LibPickHook;
                return;
            }
            int wi = SelectedTemplateWorker.Index;
            int ei = SelectedTemplateEntrypoint;
            byte[] newAi;
            try
            {
                var (guard, action) = s.ExpandGuarded(BuildArgs());
                newAi = AiScript_File.AppendGuardedAction(selectedScript, wi, ei, guard, action);
            }
            catch (Exception ex) { LibrarySummary = string.Format(Strings.U_Ai_LibAppendRejected, ex.Message); return; }

            AiValidationReport vr = AiValidator.ValidateRebuilt(newAi, selectedScript.OriginalAiFileBytes.Length);
            if (!vr.IsValid)
            {
                LibrarySummary = string.Format(Strings.U_Ai_LibValidationFailed, vr.Errors.FirstOrDefault()?.Message);
                return;
            }

            try
            {
                byte[] monster = File.ReadAllBytes(selectedPath);
                byte[] outBin = AiScript_File.SpliceAiFileIntoMonsterGrow(monster, newAi);
                WriteMonsterWithBackup(outBin);
            }
            catch (Exception ex) { LibrarySummary = string.Format(Strings.U_Ai_SaveAbortedSplice, ex.Message); return; }

            ReloadSelectedFromDisk();
            LibrarySummary = string.Format(Strings.U_Ai_LibSaved, s.Name, wi, ei, Path.GetFileName(selectedPath));
        }
    }

    internal sealed class AiSnippetVm
    {
        public AiSnippet Snippet { get; }
        public AiSnippetVm(AiSnippet s) { Snippet = s; }
        public string Name => Snippet.Name;
        public string Description => Snippet.Description;
        public string CategoryLabel => $"{Snippet.Category} · {(Snippet.Kind == AiSnippetKind.GuardedAction ? "guarded" : "linear")}";
    }

    internal sealed record AiWorkerChoice(int Index, int EntrypointCount, string Type)
    {
        public string Label => $"Worker {Index} · {Type} · {EntrypointCount} entrypoints";
    }
}
