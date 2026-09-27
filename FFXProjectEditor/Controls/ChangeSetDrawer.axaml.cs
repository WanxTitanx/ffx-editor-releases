using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using FFXProjectEditor.Core;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Controls
{
    public partial class ChangeSetDrawer : UserControl
    {
        public event EventHandler? ChangesDiscarded;
        public event EventHandler<CommitReceipt>? OperationApplied;

        private OperationPlan? _currentPlan;
        private readonly WriterAdapterCatalog _catalog = new();

        public ChangeSetDrawer()
        {
            InitializeComponent();
            DiscardButton.Click += (s, e) => DiscardChanges();
            SaveButton.Click += async (s, e) => await ApplyChangesAsync();
        }

        public void SetPreview(OperationPreview preview, OperationPlan? plan)
        {
            _currentPlan = plan;
            if (preview == null || preview.FileCount == 0)
            {
                IsVisible = false;
                return;
            }

            IsVisible = true;
            ProgressPanel.IsVisible = false;
            ReceiptCardControl.IsVisible = false;

            // Preview sem plano executável (ex.: preview hardcoded de módulos que ainda não têm
            // EditSession real) = o botão "Aplicar" ficaria morto. Desabilitar com explicação
            // honesta em vez de executar um plano falso (SourceRoot="Source") que sempre falharia.
            bool executable = plan != null && plan.Operations.Count > 0;
            SaveButton.IsEnabled = executable;
            Avalonia.Controls.ToolTip.SetTip(SaveButton,
                executable ? null : Strings.F2_preview_unavailable_editor_not_yet_conne_5ae3bd46);

            PendingChangesText.Text = string.Format(Strings.U_ChgFilesModified, preview.FileCount, preview.TotalBytes.ToString("N0"));
            RiskBadgeText.Text = string.Format(Strings.U_ChgRisk, preview.OverallRisk);

            // Map summaries to DiffSummaryView items
            DiffItemsControl.ItemsSource = preview.FilePreviewSummaries;
        }

        public void DiscardChanges()
        {
            _currentPlan = null;
            IsVisible = false;
            ChangesDiscarded?.Invoke(this, EventArgs.Empty);
        }

        private async Task ApplyChangesAsync()
        {
            if (_currentPlan == null) return;

            SaveButton.IsEnabled = false;
            ProgressPanel.IsVisible = true;
            OperationProgressBar.Value = 25;
            ProgressStatusText.Text = Strings.U_ChgStep1;

            var executor = new OperationExecutorV2(_catalog);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            OperationProgressBar.Value = 60;
            ProgressStatusText.Text = Strings.U_ChgStep2;

            OperationResult result = await executor.ExecuteAsync(_currentPlan, cts.Token);

            OperationProgressBar.Value = 100;
            ProgressPanel.IsVisible = false;

            if (result.Success)
            {
                CommitReceipt receipt = ReceiptBuilder.BuildReceipt(_currentPlan, result);
                ReceiptCardControl.SetReceipt(receipt);
                ReceiptCardControl.IsVisible = true;
                OperationApplied?.Invoke(this, receipt);
            }
            else
            {
                PendingChangesText.Text = string.Format(Strings.U_ChgApplyError, result.ErrorMessage);
                RiskBadgeText.Text = result.RecoveryInstructions ?? Strings.U_ChgRollback;
                SaveButton.IsEnabled = true;
            }
        }
    }
}
