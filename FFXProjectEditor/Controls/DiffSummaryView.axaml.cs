using System;

using Avalonia.Controls;
using FFXProjectEditor.Core;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Controls
{
    public partial class DiffSummaryView : UserControl
    {
        public DiffSummaryView()
        {
            InitializeComponent();
        }

        public void SetSummary(FilePreviewSummary summary)
        {
            if (summary == null) return;

            FileNameText.Text = summary.FileId;
            string before = summary.BeforeHash.Length > 8 ? summary.BeforeHash[..8] : summary.BeforeHash;
            string after = summary.PredictedAfterHash.Length > 8 ? summary.PredictedAfterHash[..8] : summary.PredictedAfterHash;
            HashesText.Text = $"{before} ➔ {after}";

            HumanSummaryText.Text = string.IsNullOrWhiteSpace(summary.HumanSummary) ? Strings.U_DiffNoHumanSummary : summary.HumanSummary;

            SemanticDiffList.ItemsSource = summary.SemanticDiffLines;
            DisassemblyDiffList.ItemsSource = summary.DisassemblyDiffLines;
            ByteDiffList.ItemsSource = summary.ByteDiffLines;
        }
    }
}
