using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using FFXProjectEditor.FfxLib.ThunderPlains;
using FFXProjectEditor.Modules.Common;
using FFXProjectEditor.Utils;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FFXProjectEditor.Modules.ThunderPlainsEditor
{
    public partial class ThunderPlainsEditor_Control : UserControl, IRestorableModule
    {
        readonly ThunderPlainsEditor_DataModel dataModel;

        public ThunderPlainsEditor_Control()
        {
            dataModel = new ThunderPlainsEditor_DataModel();
            DataContext = dataModel;
            InitializeComponent();
        }

        private void Button_Refresh(object? sender, RoutedEventArgs e)
            => dataModel.RefreshFromDisk();

        private async void Button_Save(object? sender, RoutedEventArgs e)
            => await SaveWithPreviewAsync();

        private void Button_Undo(object? sender, RoutedEventArgs e)
            => dataModel.Undo();

        private void Button_Redo(object? sender, RoutedEventArgs e)
            => dataModel.Redo();

        private void Button_ResetVanilla(object? sender, RoutedEventArgs e)
            => dataModel.ResetToVanilla();

        private void Button_PresetVanilla(object? sender, RoutedEventArgs e) => dataModel.ApplyPreset("vanilla");
        private void Button_PresetHalf(object? sender, RoutedEventArgs e) => dataModel.ApplyPreset("half");
        private void Button_PresetEasy(object? sender, RoutedEventArgs e) => dataModel.ApplyPreset("easy");
        private void Button_PresetExtreme(object? sender, RoutedEventArgs e) => dataModel.ApplyPreset("extreme");

        private async void Button_SaveCustomPreset(object? sender, RoutedEventArgs e)
        {
            string? name = await PromptForStringAsync(
                "Save Custom Preset",
                "Enter a name for this preset:");
            if (string.IsNullOrWhiteSpace(name)) return;
            dataModel.SaveCurrentAsCustomPreset(name);
        }

        private void Button_ApplyCustomPreset(object? sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string name)
                dataModel.ApplyCustomPreset(name);
        }

        private void Button_DeleteCustomPreset(object? sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string name)
                dataModel.DeleteCustomPreset(name);
        }

        async Task SaveWithPreviewAsync()
        {
            ThresholdApplyResult? diff = dataModel.ComputeSaveDiff();

            if (diff is { IsSuccess: false })
            {
                await ShowOkAsync(this, "Cannot Save", diff.Error ?? "Validation failed.");
                return;
            }
            if (diff == null)
            {
                await ShowOkAsync(this, "Nothing to Save", "No threshold values differ from disk.");
                return;
            }

            string preview = dataModel.BuildSavePreview(diff);
            bool proceed = await AvaloniaDialog_Util.ConfirmYesNoAsync(
                this,
                "Confirm Save",
                preview,
                yesLabel: "Save",
                noLabel: "Cancel");
            if (!proceed) return;

            SaveOutcome outcome = dataModel.Save();
            if (outcome.Kind == SaveOutcomeKind.Ok)
                await ShowOkAsync(this, "Saved", outcome.Message);
            else if (outcome.Kind == SaveOutcomeKind.Error)
                await ShowOkAsync(this, "Save Failed", outcome.Message);
        }

        static async Task ShowOkAsync(Visual anchor, string title, string message)
        {
            if (TopLevel.GetTopLevel(anchor) is not Window owner) return;
            var ok = new Button
            {
                Content = "OK",
                Classes = { "primaryAction" },
                MinWidth = 90,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var dialog = new Window
            {
                Title = title,
                Width = 500,
                Height = 280,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                ShowInTaskbar = false,
                Content = BuildMessagePanel(title, message, ok),
            };
            ok.Click += (_, _) => dialog.Close();
            await dialog.ShowDialog(owner);
        }

        static async Task<string?> PromptForStringAsync(string title, string prompt)
        {
            if (Application.Current?.ApplicationLifetime is not
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desk)
                return null;
            Window owner = desk.MainWindow;

            var input = new TextBox { Width = 320, Watermark = "Preset name" };
            var saveBtn = new Button
            {
                Content = "Save",
                Classes = { "primaryAction" },
                MinWidth = 90,
                IsEnabled = false,
            };
            var cancelBtn = new Button
            {
                Content = "Cancel",
                Classes = { "secondaryAction" },
                MinWidth = 90,
            };

            input.TextChanged += (_, _) =>
                saveBtn.IsEnabled = !string.IsNullOrWhiteSpace(input.Text);

            string? result = null;
            var dialog = new Window
            {
                Title = title,
                Width = 460,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                ShowInTaskbar = false,
            };
            saveBtn.Click += (_, _) => dialog.Close();
            cancelBtn.Click += (_, _) => dialog.Close();
            saveBtn.Click += (_, _) => { result = input.Text; dialog.Close(); };
            cancelBtn.Click += (_, _) => { result = null; dialog.Close(); };
            dialog.Content = BuildPromptPanel(prompt, input, saveBtn, cancelBtn);
            await dialog.ShowDialog(owner);
            return result;
        }

        static Grid BuildMessagePanel(string title, string message, Button okBtn)
        {
            var header = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold },
                    new ScrollViewer
                    {
                        Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Classes = { "muted" } },
                        MaxHeight = 180,
                    },
                },
            };
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10,
                Children = { okBtn },
            };
            var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
            grid.Children.Add(header);
            Grid.SetRow(buttons, 1);
            grid.Children.Add(buttons);
            return grid;
        }

        static Grid BuildPromptPanel(string prompt, TextBox input, Button saveBtn, Button cancelBtn)
        {
            var header = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap },
                    input,
                },
            };
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10,
                Children = { cancelBtn, saveBtn },
            };
            var grid = new Grid
            {
                RowDefinitions = new RowDefinitions("*,Auto"),
                Margin = new Thickness(20),
            };
            grid.Children.Add(header);
            Grid.SetRow(buttons, 1);
            grid.Children.Add(buttons);
            return grid;
        }

        public Dictionary<string, object?>? CaptureState() =>
            new() { ["filterText"] = "" };

        public void RestoreState(Dictionary<string, object?>? state) { }
    }
}
