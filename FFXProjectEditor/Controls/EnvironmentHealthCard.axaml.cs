using System;
using Avalonia.Controls;
using Avalonia.Media;
using FFXProjectEditor.Core;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Controls
{
    public partial class EnvironmentHealthCard : UserControl
    {
        public event EventHandler? RefreshRequested;
        public event EventHandler? SelectGameFolderRequested;

        public EnvironmentHealthCard()
        {
            InitializeComponent();
            RefreshButton.Click += (s, e) => RefreshRequested?.Invoke(this, EventArgs.Empty);
            SelectGameFolderButton.Click += (s, e) => SelectGameFolderRequested?.Invoke(this, EventArgs.Empty);
        }

        public void SetReport(GameEnvironmentReport report)
        {
            if (report == null) return;

            TimestampText.Text = string.Format(Strings.U_EnvLastCheck, report.ScanTimestamp.ToLocalTime().ToString("HH:mm:ss"));
            GameRootText.Text = report.GameFound ? (report.GameRoot ?? Strings.U_EnvDetected) : Strings.U_EnvNotFound;

            if (report.GameFound)
            {
                string ver = report.GameVersion ?? "v1.0";
                string reg = report.Region switch
                {
                    GameRegion.JP => "JP",
                    GameRegion.US => "US/INTL",
                    GameRegion.EU => "EU",
                    _ => Strings.U_EnvUnknown
                };
                VersionRegionText.Text = $"{ver} ({reg})";
            }
            else
            {
                VersionRegionText.Text = Strings.U_EnvNA;
            }

            // Modules
            if (report.ProbeModulePresent && report.HooksModulePresent)
                ModulesText.Text = "ffx-probe + Hooks OK";
            else if (report.ProbeModulePresent)
                ModulesText.Text = Strings.F2_only_ffx_probe_dll_6031c952;
            else if (report.HooksModulePresent)
                ModulesText.Text = Strings.F2_only_ffxhooksdll_dll_475d200e;
            else
                ModulesText.Text = Strings.U_EnvMissingModules;

            // Live Probe
            if (report.ProbeAttached)
            {
                ProbeStatusText.Text = report.ProbeHooked ? Strings.U_EnvProbeActiveHooked : Strings.U_EnvProbeActiveStandby;
                ProbeStatusText.Foreground = new SolidColorBrush(Color.Parse("#4CAF8A"));
            }
            else
            {
                ProbeStatusText.Text = Strings.U_EnvProbeInactive;
                ProbeStatusText.Foreground = new SolidColorBrush(Color.Parse("#8BA3B5"));
            }

            // Status Pill
            switch (report.State)
            {
                case EnvironmentCheckState.Ok:
                    StatusPill.Background = new SolidColorBrush(Color.Parse("#142B24"));
                    StatusPill.BorderBrush = new SolidColorBrush(Color.Parse("#2E7D63"));
                    StatusIcon.Text = "✓";
                    StatusIcon.Foreground = new SolidColorBrush(Color.Parse("#4CAF8A"));
                    StatusText.Text = Strings.U_EnvHealthy;
                    FooterNote.Text = Strings.F2_all_set_for_offline_editing_and_safe_mod_a238ed9a;
                    break;
                case EnvironmentCheckState.Warning:
                    StatusPill.Background = new SolidColorBrush(Color.Parse("#2B2514"));
                    StatusPill.BorderBrush = new SolidColorBrush(Color.Parse("#8C7326"));
                    StatusIcon.Text = "⚠️";
                    StatusIcon.Foreground = new SolidColorBrush(Color.Parse("#E8943A"));
                    StatusText.Text = Strings.U_EnvWithWarnings;
                    FooterNote.Text = Strings.F2_offline_editing_functional_runtime_resou_064ef822;
                    break;
                case EnvironmentCheckState.Error:
                    StatusPill.Background = new SolidColorBrush(Color.Parse("#2B1417"));
                    StatusPill.BorderBrush = new SolidColorBrush(Color.Parse("#8C2633"));
                    StatusIcon.Text = "❌";
                    StatusIcon.Foreground = new SolidColorBrush(Color.Parse("#D95A6A"));
                    StatusText.Text = Strings.F2_game_not_found_error_d1953177;
                    FooterNote.Text = Strings.F2_configure_the_ffx_hd_game_path_to_enable_c76c7a7b;
                    break;
            }

            IssuesList.ItemsSource = report.Issues;
        }
    }
}
