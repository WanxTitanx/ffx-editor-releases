using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using FFXProjectEditor.Resources;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace FFXProjectEditor.Utils
{
    public class AvaloniaDialog_Util
    {
        /*
         * Opens a folder picker and returns the path of the selected folders.
         * If no folder is selected the returned list will be empty.
         */
        public static async ValueTask<List<string>> OpenFolderDialog(Visual callingVisual, string title = "Open Folder", bool allowMultiple = false)
        {
            // Get top level from the current control. Alternatively, you can use Window reference instead.
            var topLevel = TopLevel.GetTopLevel(callingVisual);

            // Start async operation to open the dialog.
            var files = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = allowMultiple
            });

            List<string> filePaths = new List<string>();
            foreach (var file in files)
            {
                filePaths.Add(Uri.UnescapeDataString(file.Path.AbsolutePath));
            }
            return filePaths;
        }

        /*
         * Opens a file picker and returns the path of the selected files.
         * If no file is selected the returned list will be empty.
         */
        public static async ValueTask<List<string>> OpenFileDialog(Visual callingVisual, string title = "Open File", bool allowMultiple = false, string suggestedFileName = null, List<FilePickerFileType> fileTypeFilter = null)
        {
            // Get top level from the current control. Alternatively, you can use Window reference instead.
            var topLevel = TopLevel.GetTopLevel(callingVisual);

            // Start async operation to open the dialog.
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = allowMultiple,
                SuggestedFileName = suggestedFileName,
                FileTypeFilter = fileTypeFilter
            });

            List<string> filePaths = new List<string>();
            foreach (var file in files)
            {
                filePaths.Add(Uri.UnescapeDataString(file.Path.AbsolutePath));
            }
            return filePaths;
        }

        /*
         * Opens a save file picker and returns the selected path.
         */
        public static async ValueTask<string> SaveFileDialog(Visual callingVisual, string title = "Save File", string suggestedFileName = null, string defaultExtension = null, List<FilePickerFileType> fileTypeChoices = null)
        {
            // Get top level from the current control. Alternatively, you can use Window reference instead.
            var topLevel = TopLevel.GetTopLevel(callingVisual);

            // Start async operation to open the dialog.
            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = suggestedFileName,
                DefaultExtension = defaultExtension,
                FileTypeChoices = fileTypeChoices,
                ShowOverwritePrompt = true
            });

            if (file is not null)
            {
                return Uri.UnescapeDataString(file.Path.AbsolutePath);
            }
            else
            {
                return null;
            }
        }

        /*
         * Opens a save file picker and saves the given file.
         */
        public static async void SaveFile(Visual callingVisual, byte[] byteFile, string title = "Save File", string suggestedFileName = null, string defaultExtension = null, List<FilePickerFileType> fileTypeChoices = null)
        {
            // Get top level from the current control. Alternatively, you can use Window reference instead.
            var topLevel = TopLevel.GetTopLevel(callingVisual);

            // Start async operation to open the dialog.
            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = suggestedFileName,
                DefaultExtension = defaultExtension,
                FileTypeChoices = fileTypeChoices,
                ShowOverwritePrompt = true
            });

            if (file is not null)
            {
                File.WriteAllBytes(Uri.UnescapeDataString(file.Path.AbsolutePath), byteFile);
            }
            else
            {
                return;
            }
        }

        /// <summary>
        /// Jarvis-UI (Save Editor Hub Phase 2 2026-06-20): prompt modal Yes/No simples para confirmar
        /// descarte de alterações não salvas. O app não tinha helper de confirmação (só pickers de
        /// arquivo/pasta); este constrói uma <see cref="Window"/> mínima, sem asset novo, e devolve
        /// true quando o usuário confirma o descarte. Retorna true imediatamente se não houver owner
        /// Window (fallback não-bloqueante: procede com a ação).
        /// </summary>
        public static async Task<bool> ConfirmYesNoAsync(Visual callingVisual, string title, string message,
            string yesLabel = "Yes", string noLabel = "No")
        {
            if (TopLevel.GetTopLevel(callingVisual) is not Window owner)
                return true;

            bool result = false;

            var yes = new Button { Content = yesLabel, Classes = { "primaryAction" }, MinWidth = 90, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
            var no = new Button { Content = noLabel, Classes = { "secondaryAction" }, MinWidth = 90, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };

            var dialog = new Window
            {
                Title = title,
                Width = 420,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                ShowInTaskbar = false,
                Content = new Border
                {
                    Padding = new Avalonia.Thickness(20),
                    Child = MakeDialogContent(title, message, no, yes)
                }
            };

            yes.Click += (_, _) => { result = true; dialog.Close(); };
            no.Click += (_, _) => { result = false; dialog.Close(); };

            await dialog.ShowDialog(owner);
            return result;
        }

        /// <summary>
        /// Single-button message dialog sharing the confirm-dialog chrome. Exists for actionable
        /// validation feedback (e.g. rejecting a non-"master" workspace folder) where Yes/No is
        /// the wrong shape. No-op when there is no owner Window.
        /// </summary>
        public static async Task ShowMessageAsync(Visual callingVisual, string title, string message)
        {
            if (TopLevel.GetTopLevel(callingVisual) is not Window owner)
                return;

            var ok = new Button { Content = Strings.CommonClose, Classes = { "primaryAction" }, MinWidth = 90, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };

            var dialog = new Window
            {
                Title = title,
                Width = 420,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                ShowInTaskbar = false,
                Content = new Border
                {
                    Padding = new Avalonia.Thickness(20),
                    Child = MakeDialogContent(title, message, ok)
                }
            };

            ok.Click += (_, _) => dialog.Close();

            await dialog.ShowDialog(owner);
        }

        static Grid MakeDialogContent(string title, string message, params Button[] dialogButtons)
        {
            var header = new StackPanel
            {
                Spacing = 10,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = title, FontSize = 16, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                    new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "muted" } },
                }
            };

            var buttons = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Spacing = 10,
            };
            foreach (Button button in dialogButtons)
                buttons.Children.Add(button);

            var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
            grid.Children.Add(header);
            Grid.SetRow(buttons, 1);
            grid.Children.Add(buttons);
            return grid;
        }
    }
}
