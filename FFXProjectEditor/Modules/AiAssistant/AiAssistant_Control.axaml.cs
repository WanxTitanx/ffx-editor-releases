using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FFXProjectEditor.Modules.Common;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Utils;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FFXProjectEditor.Modules.AiAssistant
{
    /// <summary>
    /// Painel Assistente IA (BYOK) — lane Jarvis-MAGIC-IA, 2026-08-16; rework Jarvis-UI 2026-09-15.
    /// UI fina: apenas lê o campo de key e delega ao DataModel. Nenhuma escritura de arquivo aqui.
    /// O "Send" é assíncrono e não bloqueia a UI; Enter envia; Cancel aborta o request em voo.
    /// No modo Agent, o loop roda tools locais read-only e aprovações passam pelo card
    /// de proposta — o Apply final pede confirmação explícita do plano (ConfirmPlan).
    /// </summary>
    public partial class AiAssistant_Control : UserControl, IRestorableModule
    {
        readonly AiAssistant_DataModel dataModel;

        public AiAssistant_Control()
        {
            dataModel = new AiAssistant_DataModel();
            DataContext = dataModel;
            // Última porta humana antes de qualquer escrita: o diálogo mostra o plano
            // real (arquivo, destino, diff) e só devolve true se o usuário confirmar.
            dataModel.ConfirmPlan = plan => AvaloniaDialog_Util.ConfirmYesNoAsync(
                this,
                Strings.AiAssistant_ConfirmApplyTitle,
                $"{plan.DisplayName}\n\n" +
                $"{plan.Operations.Count} op · {plan.Operations[0].SourceRelativePath} → {plan.OutputRoot}\n" +
                (plan.Operations[0].Diff?.HumanSummary ?? ""),
                Strings.AiAssistant_ConfirmApplyYes,
                Strings.AiAssistant_ConfirmApplyNo);
            InitializeComponent();
        }

        private void Button_Connect(object? sender, RoutedEventArgs e)
        {
            // Key lida da propriedade ApiKeyText (two-way no PasswordBox) e zerada via TakeApiKey().
            dataModel.ConnectSession(dataModel.TakeApiKey());
        }

        private void Button_Clear(object? sender, RoutedEventArgs e)
        {
            dataModel.ClearSession();
        }

        private void Button_Send(object? sender, RoutedEventArgs e)
        {
            _ = dataModel.SendCommandAsync();
        }

        private void Button_Cancel(object? sender, RoutedEventArgs e)
        {
            dataModel.CancelSend();
        }

        private void Mode_Chat(object? sender, RoutedEventArgs e) => dataModel.ModeIndex = 0;
        private void Mode_Proposal(object? sender, RoutedEventArgs e) => dataModel.ModeIndex = 1;
        private void Mode_Agent(object? sender, RoutedEventArgs e) => dataModel.ModeIndex = 2;

        private void Proposal_Approve(object? sender, RoutedEventArgs e)
        {
            _ = dataModel.ApproveProposalAsync();
        }

        private void Proposal_Reject(object? sender, RoutedEventArgs e)
        {
            dataModel.RejectProposal();
        }

        private void Command_KeyDown(object? sender, KeyEventArgs e)
        {
            // Enter envia (o campo é single-line); Shift+Enter não existe aqui.
            if (e.Key == Key.Enter && dataModel.CanSend)
            {
                e.Handled = true;
                _ = dataModel.SendCommandAsync();
            }
        }

        // IRestorableModule: não restaura a key (segredo). Restaura só provider/model (config, não sensível).
        // Persistência em disco (ai-assistant.json) cobre restart; este capture cobre troca de módulo.
        public Dictionary<string, object?>? CaptureState() => new()
        {
            ["provider"] = dataModel.Provider,
            ["modelId"] = dataModel.ModelId,
            ["endpoint"] = dataModel.Endpoint,
            ["mode"] = dataModel.ModeIndex,
        };

        public void RestoreState(Dictionary<string, object?>? state)
        {
            if (state is null) return;
            if (state.TryGetValue("provider", out var p) && p is string provider) dataModel.Provider = provider;
            if (state.TryGetValue("modelId", out var m) && m is string model) dataModel.ModelId = model;
            if (state.TryGetValue("endpoint", out var ep) && ep is string endpoint) dataModel.Endpoint = endpoint;
            if (state.TryGetValue("mode", out var mi) && mi is int mode) dataModel.ModeIndex = mode;
            else if (state.TryGetValue("chatMode", out var cm) && cm is bool chatMode) dataModel.ChatMode = chatMode;
        }
    }
}
