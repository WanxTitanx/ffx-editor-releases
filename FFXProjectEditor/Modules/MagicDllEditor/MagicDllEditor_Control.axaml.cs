using FFXProjectEditor.Resources;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using FFXProjectEditor.Modules.Common;
using FFXProjectEditor.Utils;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FFXProjectEditor.Modules.MagicDllEditor;

// ====================================================================================
// MagicDllEditor_Control — code-behind thin (padrão MonEditor_Control.axaml.cs).
// DataContext = MagicDllEditor_ViewModel; pickers de arquivo vivem aqui (host da UI);
// os comandos da spec (OpenCommand/SaveCopyCommand/...) ficam no ViewModel.
// Implementa IRestorableModule (stateless default — nada a restaurar além do controle).
// ====================================================================================

public partial class MagicDllEditor_Control : UserControl, IRestorableModule
{
    private readonly MagicDllEditor_ViewModel _viewModel;

    public MagicDllEditor_Control()
    {
        _viewModel = new MagicDllEditor_ViewModel
        {
            OpenFilePicker = PickDllAsync,
            SaveFilePicker = PickSavePathAsync,
            CloneFilePicker = PickClonePathAsync,
        };
        _viewModel.PropertyChanged += OnVmPropertyChanged;

        DataContext = _viewModel;
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // GridSplitter creates platform cursors. Defer it until a real UI host exists;
        // constructing this module without a platform remains supported.
        if (ToolSplitterAnchor.Parent is Grid grid)
        {
            var splitter = new GridSplitter
            {
                ResizeDirection = GridResizeDirection.Rows,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            };
            Grid.SetRow(splitter, 2);
            grid.Children.Remove(ToolSplitterAnchor);
            grid.Children.Add(splitter);
        }
    }

    /// <summary>Navega o mini navegador (WebView2Host) quando o PreviewUrl muda (abre/fecha o preview).
    /// Fora do Windows o embed não existe — a mesma URL abre no navegador do sistema.</summary>
    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MagicDllEditor_ViewModel.PreviewUrl))
        {
            if (_viewModel.HasPreviewUrl)
            {
                if (!Modules.Common.ViewerShell.ExternalBrowserLauncher.IsEmbeddedViewerSupported)
                    Modules.Common.ViewerShell.ExternalBrowserLauncher.Open(_viewModel.PreviewUrl);
                else
                    PreviewHost?.Navigate(_viewModel.PreviewUrl);
            }
            else
            {
                PreviewHost?.Navigate("about:blank");
            }
        }
    }

    /// <summary>Releases the exact preview route and owned staging file when the module closes.</summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _viewModel.ClosePreviewCommand.Execute(null);
        base.OnDetachedFromVisualTree(e);
    }

    private static readonly List<FilePickerFileType> DllFilter = new()
    {
        new FilePickerFileType("Magic DLL (*.dll)") { Patterns = new[] { "*.dll" } },
        FilePickerFileTypes.All,
    };

    /// <summary>File picker de abertura — retorna o caminho da DLL escolhida ou null se cancelado.</summary>
    private async Task<string?> PickDllAsync()
    {
        List<string> files = await AvaloniaDialog_Util.OpenFileDialog(
            this, Strings.U_Md_OpenDialogTitle, allowMultiple: false, fileTypeFilter: DllFilter);
        return files.Count > 0 ? files[0] : null;
    }

    /// <summary>Save picker da cópia — retorna o caminho de destino ou null se cancelado.</summary>
    private async Task<string?> PickSavePathAsync()
    {
        return await AvaloniaDialog_Util.SaveFileDialog(
            this, Strings.U_Md_SaveCopyDialogTitle, suggestedFileName: "magic_copy.dll", defaultExtension: "dll");
    }

    /// <summary>Save picker do clone — retorna o caminho do novo magic_&lt;id&gt;.dll ou null se cancelado.</summary>
    private async Task<string?> PickClonePathAsync()
    {
        return await AvaloniaDialog_Util.SaveFileDialog(
            this, "Clonar magic DLL (novo id)", suggestedFileName: "magic_0140.dll", defaultExtension: "dll");
    }

    /// <summary>Double-click na árvore → fly-to (revela o nó: expande a cadeia + seleciona).</summary>
    private void OnTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MagicDllEditor_ViewModel vm)
            vm.FlyToCommand.Execute(vm.SelectedNode);
    }
}
