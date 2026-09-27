using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using FFXProjectEditor.Resources;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FFXProjectEditor.Modules.Common.ViewerShell;

// ── Direct WebView2-host recovery UI ──────────────────────────────────────────────
// ViewerShell has an inline failure panel, but several product modules host the browser facade
// directly. Windows runtime failures may reveal the packaged installer; unsupported platforms
// expose capability-unavailable copy with no installer or process action.
internal static class WebView2RecoveryDialog
{
    private static int _dialogVisible;

    internal static async Task ShowAsync(
        Visual ownerVisual,
        WebView2InitializationFailedEventArgs failure)
    {
        ArgumentNullException.ThrowIfNull(ownerVisual);
        ArgumentNullException.ThrowIfNull(failure);

        if (TopLevel.GetTopLevel(ownerVisual) is not Window owner ||
            Interlocked.CompareExchange(ref _dialogVisible, 1, 0) != 0)
            return;

        try
        {
            bool platformUnavailable =
                failure.Kind == WebView2InitializationFailureKind.PlatformUnavailable;
            string title = platformUnavailable
                ? Strings.U_Vh_EmbeddedViewerUnavailableTitle
                : Strings.U_Vh_WebView2InitializationFailedTitle;
            var message = new TextBlock
            {
                Text = platformUnavailable
                    ? Strings.U_Vh_EmbeddedViewerUnavailableMessage
                    : failure.InstallerAvailable
                        ? Strings.U_Vh_WebView2RecoveryMessage
                        : Strings.U_Vh_WebView2InstallerMissingMessage,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Classes = { "muted" },
            };
            var close = new Button
            {
                Content = Strings.CommonClose,
                MinWidth = 90,
                Classes = { "secondaryAction" },
            };
            var reveal = new Button
            {
                Content = Strings.U_Vh_ShowWebView2Installer,
                MinWidth = 150,
                IsVisible = !platformUnavailable,
                IsEnabled = failure.InstallerAvailable,
                Classes = { "primaryAction" },
            };
            var dialog = new Window
            {
                Title = title,
                Width = 540,
                Height = 240,
                MinWidth = 440,
                MinHeight = 220,
                CanResize = true,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10,
                Children = { close, reveal },
            };
            var content = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
            content.Children.Add(new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontSize = 16,
                        FontWeight = Avalonia.Media.FontWeight.SemiBold,
                    },
                    message,
                },
            });
            Grid.SetRow(buttons, 1);
            content.Children.Add(buttons);
            dialog.Content = new Border
            {
                Padding = new Thickness(20),
                Child = content,
            };

            close.Click += (_, _) => dialog.Close();
            reveal.Click += (_, _) =>
            {
                if (TryRevealInstaller(failure, out string error))
                {
                    dialog.Close();
                    return;
                }

                reveal.IsEnabled = false;
                message.Text = Strings.U_Vh_WebView2InstallerRevealFailed;
                FFXProjectEditor.Diagnostics.DebugLog.Error(
                    "WebView.Recovery",
                    $"Failed to reveal the packaged WebView2 installer: {error}");
            };

            await dialog.ShowDialog(owner);
        }
        catch (Exception ex)
        {
            FFXProjectEditor.Diagnostics.DebugLog.Error(
                "WebView.Recovery",
                "Failed to display the WebView2 recovery dialog.",
                ex);
        }
        finally
        {
            Volatile.Write(ref _dialogVisible, 0);
        }
    }

    internal static bool TryRevealInstaller(
        WebView2InitializationFailedEventArgs failure,
        out string error)
    {
        if (failure.Kind != WebView2InitializationFailureKind.RuntimeInitializationFailed ||
            !OperatingSystem.IsWindows())
        {
            error = "INSTALLER_ACTION_UNAVAILABLE";
            return false;
        }

        if (string.IsNullOrWhiteSpace(failure.InstallerPath) ||
            !File.Exists(failure.InstallerPath))
        {
            error = "PACKAGED_INSTALLER_MISSING";
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{failure.InstallerPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            error = string.Empty;
            FFXProjectEditor.Diagnostics.DebugLog.Info(
                "WebView.Recovery",
                "Revealed the packaged WebView2 installer; no installer process was executed.");
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            error = ex.GetType().Name;
            return false;
        }
    }
}
