using System;
using System.Linq;
using Avalonia.Controls;
using FFXProjectEditor.Core;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Controls
{
    public partial class WorkspaceStatusCard : UserControl
    {
        // This card owns WORKSPACE and OUTPUT only — the game install folder is picked
        // from the Environment card above (one control per concept, no duplicates).
        public event EventHandler? ChangeWorkspaceRequested;
        public event EventHandler? SelectOutputFolderRequested;
        public event EventHandler? ResetOutputRequested;

        public WorkspaceStatusCard()
        {
            InitializeComponent();
            ChangeWorkspaceButton.Click += (s, e) => ChangeWorkspaceRequested?.Invoke(this, EventArgs.Empty);
            ChangeOutputButton.Click += (s, e) => SelectOutputFolderRequested?.Invoke(this, EventArgs.Empty);
            ResetOutputButton.Click += (s, e) => ResetOutputRequested?.Invoke(this, EventArgs.Empty);
        }

        public void SetInspectionResult(WorkspaceInspectionResult inspection)
        {
            if (inspection == null) return;

            ScanTimeText.Text = string.Format(Strings.U_WsScanned, inspection.ScanTimestamp.ToLocalTime().ToString("HH:mm:ss"));
            // Three independent paths: GAME = FFX.exe install root (deploy paths derive
            // from it), WORKSPACE = extracted master (editing base, may live outside the
            // install), OUTPUT = where saved mods land (data\mods when the external
            // loader is in play, else output_staging).
            GamePathText.Text = string.IsNullOrWhiteSpace(inspection.GamePath) ? Strings.U_Ws_NoDirectorySelected : inspection.GamePath;
            SourcePathText.Text = string.IsNullOrWhiteSpace(inspection.SourcePath) ? Strings.U_Ws_NoDirectorySelected : inspection.SourcePath;
            OutputPathText.Text = string.IsNullOrWhiteSpace(inspection.OutputPath) ? Strings.F2_direct_output_staging_edit_91ecbb6b : inspection.OutputPath;

            PlatformText.Text = inspection.DetectedPlatform switch
            {
                Platform.PC => "PC Steam HD",
                Platform.PS2 => "PS2 ISO / Memory Card",
                Platform.PS3 => "PS3 Remaster",
                _ => Strings.U_WsGeneralPlatform
            };

            int activeCount = inspection.Capabilities.Count(c => c.IsAvailable);
            int totalCount = inspection.Capabilities.Count;
            ActiveCapabilitiesText.Text = $"{activeCount}/{totalCount}";

            WarningsCountText.Text = inspection.Warnings.Count.ToString();
        }
    }
}
