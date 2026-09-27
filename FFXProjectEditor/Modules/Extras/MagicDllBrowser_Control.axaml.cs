using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FFXProjectEditor.Services.Extras;
using FFXProjectEditor.Utils;
using System;
using System.Collections.Generic;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor.Modules.Extras
{
    public partial class MagicDllBrowser_Control : UserControl, IRestorableModule
    {
        readonly MagicDllBrowser_DataModel dataModel;

        public event Action<int>? OpenPs3MagicRequested;
        public event Action<int>? OpenMagicViewerRequested;
        public event Action<string>? OpenPhyrePackageRequested;

        public MagicDllBrowser_Control()
        {
            InitializeComponent();
            dataModel = new MagicDllBrowser_DataModel();
            DataContext = dataModel;
        }

        /// <summary>Índices das 12 abas por grupo: 0=Estrutura, 1=Runtime, 2=RE (2026-08-02).</summary>
        static readonly int[][] TabGroups =
        {
            new[] { 0, 1, 2, 4, 5, 11 },   // Estrutura: Sections, Exports, Imports, WD3 Streams, Overlay Slots, PPP Opcodes
            new[] { 3, 6 },                 // Runtime: Sound (SeSep), Role Candidates
            new[] { 7, 8, 9, 10 },          // RE: Family Comparator, Logical Decompile, Strings, Warnings
        };

        Button[] GroupPillButtons = null!;

        /// <summary>
        /// Seletor de grupo das abas (Estrutura/Runtime/RE): aplica a pill ativa e
        /// filtra os TabItems visíveis do TabControl. Só mexe em IsVisible/SelectedIndex
        /// — não toca nos dados nem nos bindings das abas.
        /// </summary>
        void Button_SetTabGroup(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { CommandParameter: string p } || !int.TryParse(p, out int group))
                return;
            if (group < 0 || group >= TabGroups.Length)
                return;

            if (GroupPillButtons == null)
                GroupPillButtons = new[] { GroupPill0, GroupPill1, GroupPill2 };

            for (int i = 0; i < GroupPillButtons.Length; i++)
            {
                bool active = i == group;
                GroupPillButtons[i].Classes.Set("tabPillActive", active);
                GroupPillButtons[i].Classes.Set("tabPill", !active);
            }

            System.Collections.Generic.HashSet<int> set =
                new System.Collections.Generic.HashSet<int>(TabGroups[group]);
            for (int i = 0; i < Tabs.Items.Count; i++)
            {
                if (Tabs.Items[i] is TabItem ti)
                    ti.IsVisible = set.Contains(i);
            }
            for (int i = 0; i < Tabs.Items.Count; i++)
            {
                if (set.Contains(i))
                {
                    Tabs.SelectedIndex = i;
                    break;
                }
            }
        }

        void Button_Refresh(object? sender, RoutedEventArgs e)
        {
            dataModel.Refresh();
        }

        async void Button_BrowseRoot(object? sender, RoutedEventArgs e)
        {
            List<string> folders = await AvaloniaDialog_Util.OpenFolderDialog(this, "Select magicFiles\\FFX root");
            if (folders.Count > 0)
                dataModel.SetRoot(folders[0]);
        }

        void Button_OpenDll(object? sender, RoutedEventArgs e)
        {
            if (dataModel.SelectedDll != null)
                ExtrasFileOpen_Service.TryOpenInExplorer(dataModel.SelectedDll.FullPath);
        }

        async void Button_Decompile(object? sender, RoutedEventArgs e)
        {
            if (dataModel.SelectedDll == null)
                return;
            List<string> folders = await AvaloniaDialog_Util.OpenFolderDialog(this, "Choose output folder for Magic DLL decompile");
            if (folders.Count > 0)
                dataModel.DecompileSelected(folders[0]);
        }

        void Button_GenerateTemplate(object? sender, RoutedEventArgs e)
        {
            dataModel.GenerateInstantiatedTemplate();
        }

        async void Button_LogicalDecompile(object? sender, RoutedEventArgs e)
        {
            if (dataModel.SelectedDll == null)
                return;
            List<string> folders = await AvaloniaDialog_Util.OpenFolderDialog(this, "Choose output folder for logical decompile report");
            if (folders.Count > 0)
                dataModel.LogicalDecompileSelected(folders[0]);
        }

        void Button_OpenPs3Magic(object? sender, RoutedEventArgs e)
        {
            if (dataModel.GetSelectedMagicId() is int magicId)
                OpenPs3MagicRequested?.Invoke(magicId);
        }

        void Button_OpenMagicViewer(object? sender, RoutedEventArgs e)
        {
            if (dataModel.GetSelectedMagicId() is int magicId)
                OpenMagicViewerRequested?.Invoke(magicId);
        }

        void Button_OpenPhyrePackage(object? sender, RoutedEventArgs e)
        {
            if (dataModel.GetFirstPhyrePackagePath() is string path)
                OpenPhyrePackageRequested?.Invoke(path);
        }

        void Button_OpenPs3ExtractFolder(object? sender, RoutedEventArgs e)
        {
            if (dataModel.GetPs3ExtractFolder() is string folder)
                ExtrasFileOpen_Service.TryOpenInExplorer(folder);
        }

        void Button_OpenPs3ModsFolder(object? sender, RoutedEventArgs e)
        {
            if (dataModel.GetPs3ModsFolder() is string folder)
                ExtrasFileOpen_Service.TryOpenInExplorer(folder);
        }

        async void Button_Repack(object? sender, RoutedEventArgs e)
        {
            if (dataModel.SelectedDll == null)
                return;
            string path = await AvaloniaDialog_Util.SaveFileDialog(
                this,
                "Save byte-identical rebuilt DLL",
                dataModel.SelectedDll.FileName,
                "dll",
                new List<FilePickerFileType>
                {
                    new("DLL") { Patterns = new[] { "*.dll" } }
                });
            if (!string.IsNullOrWhiteSpace(path))
                dataModel.RepackSelected(path);
        }

        async void Button_ApplyPatchPlan(object? sender, RoutedEventArgs e)
        {
            if (dataModel.SelectedDll == null)
                return;
            List<string> plans = await AvaloniaDialog_Util.OpenFileDialog(
                this,
                "Choose Magic DLL patch plan JSON",
                false,
                "patch_plan.json",
                new List<FilePickerFileType>
                {
                    new("Patch plan") { Patterns = new[] { "*.json" } }
                });
            if (plans.Count == 0)
                return;

            string output = await AvaloniaDialog_Util.SaveFileDialog(
                this,
                "Save patched DLL",
                dataModel.SelectedDll.FileName,
                "dll",
                new List<FilePickerFileType>
                {
                    new("DLL") { Patterns = new[] { "*.dll" } }
                });
            if (!string.IsNullOrWhiteSpace(output))
                dataModel.ApplyPatchPlan(plans[0], output);
        }

        void Button_Clone(object? sender, RoutedEventArgs e)
        {
            dataModel.CloneSelected();
        }

        async void Button_ApplyInlineBytePatch(object? sender, RoutedEventArgs e)
        {
            if (dataModel.SelectedDll == null)
                return;
            string output = await AvaloniaDialog_Util.SaveFileDialog(
                this,
                "Save byte-patched Magic DLL",
                dataModel.SelectedDll.FileName,
                "dll",
                new List<FilePickerFileType>
                {
                    new("DLL") { Patterns = new[] { "*.dll" } }
                });
            if (!string.IsNullOrWhiteSpace(output))
                dataModel.ApplyInlineBytePatch(output);
        }

        async void Button_ApplyInlineAsciiPatch(object? sender, RoutedEventArgs e)
        {
            if (dataModel.SelectedDll == null)
                return;
            string output = await AvaloniaDialog_Util.SaveFileDialog(
                this,
                "Save ASCII-patched Magic DLL",
                dataModel.SelectedDll.FileName,
                "dll",
                new List<FilePickerFileType>
                {
                    new("DLL") { Patterns = new[] { "*.dll" } }
                });
            if (!string.IsNullOrWhiteSpace(output))
                dataModel.ApplyInlineAsciiPatch(output);
        }

        void Button_StageSelectedValuePatch(object? sender, RoutedEventArgs e)
        {
            dataModel.StageSelectedValuePatch();
        }

        async void Button_ApplySelectedValuePatch(object? sender, RoutedEventArgs e)
        {
            if (dataModel.SelectedDll == null)
                return;
            string output = await AvaloniaDialog_Util.SaveFileDialog(
                this,
                "Save value-patched Magic DLL",
                dataModel.SelectedDll.FileName,
                "dll",
                new List<FilePickerFileType>
                {
                    new("DLL") { Patterns = new[] { "*.dll" } }
                });
            if (!string.IsNullOrWhiteSpace(output))
                dataModel.ApplySelectedValuePatch(output);
        }

        void Button_StageSelectedHostPatch(object? sender, RoutedEventArgs e)
        {
            dataModel.StageSelectedHostReferencePatch();
        }

        async void Button_ApplySelectedHostPatch(object? sender, RoutedEventArgs e)
        {
            if (dataModel.SelectedDll == null)
                return;
            string output = await AvaloniaDialog_Util.SaveFileDialog(
                this,
                "Save host-reference-patched Magic DLL",
                dataModel.SelectedDll.FileName,
                "dll",
                new List<FilePickerFileType>
                {
                    new("DLL") { Patterns = new[] { "*.dll" } }
                });
            if (!string.IsNullOrWhiteSpace(output))
                dataModel.ApplySelectedHostReferencePatch(output);
        }

        async void Button_NativeProject(object? sender, RoutedEventArgs e)
        {
            if (dataModel.SelectedDll == null)
                return;
            List<string> folders = await AvaloniaDialog_Util.OpenFolderDialog(this, "Choose output folder for C/ASM project");
            if (folders.Count > 0)
                dataModel.BuildNativeProject(folders[0]);
        }

        async void Button_SemanticReport(object? sender, RoutedEventArgs e)
        {
            List<string> folders = await AvaloniaDialog_Util.OpenFolderDialog(this, "Choose output folder for Magic DLL semantic RE report");
            if (folders.Count > 0)
                dataModel.BuildSemanticReport(folders[0]);
        }

        void Button_DeployCloneToMods(object? sender, RoutedEventArgs e) =>
            dataModel.DeploySelectedCloneToMods();

        // Jarvis-WD3 (2026-07-07, todo 12): invoke the WD3 Streams recolor command.
        // Delegates to Wd3StreamViewModel.ApplyRecolor — no patch logic duplicated here.
        void Button_ApplyWd3Recolor(object? sender, RoutedEventArgs e)
        {
            dataModel.Wd3Streams.ApplyRecolorCommand.Execute(null);
        }
    }
}
