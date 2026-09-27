using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.Utils.Encoding;
using FFXProjectEditor.FfxLib.Text;
using FFXProjectEditor.FfxLib.Treasure;
using FFXProjectEditor.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Modules.Unwired
{
    // Legacy Wave-1 browser. buki_get remains readable here for compatibility, but its product route now lives
    // under Gear Rewards / buki_get; the other families still park under the "???" nav category.
    public enum UnwiredFamily
    {
        BukiGetTreasure,
        AlBhedDictionary,
        PointerScriptTable,
    }

    internal sealed class UnwiredRow
    {
        public required string Title { get; init; }
        public required string Detail { get; init; }
    }

    internal partial class UnwiredCatalog_DataModel : ObservableObject
    {
        public string Title { get; }
        public string RelativePath { get; }
        public ObservableCollection<UnwiredRow> Rows { get; } = new();

        [ObservableProperty] private string headerSummary = "";
        [ObservableProperty] private string sourcePath = "";
        [ObservableProperty] private string statusNote = "";

        public UnwiredCatalog_DataModel(UnwiredFamily family)
        {
            (Title, RelativePath) = family switch
            {
                UnwiredFamily.BukiGetTreasure   => ("buki_get.bin — Weapon-Get Treasure Catalog", Path.Combine("jppc", "battle", "kernel", "buki_get.bin")),
                UnwiredFamily.AlBhedDictionary  => ("albheddic.bin — Al Bhed Dictionary",          Path.Combine("new_uspc", "menu", "albheddic.bin")),
                UnwiredFamily.PointerScriptTable => ("battle_script.bin — Pointer/Script Table",   Path.Combine("jppc", "menu", "battle_script.bin")),
                _ => ("?", ""),
            };
            Load(family);
        }

        void Load(UnwiredFamily family)
        {
            string? path = ResolveKernelFile(RelativePath);
            if (path == null)
            {
                StatusNote = $"Arquivo nao encontrado: <master>\\{RelativePath} (nem no workspace nem na referencia extraida). Carregue um master que contenha o arquivo, ou use 'Set ffx_ps2 Root...'.";
                return;
            }
            SourcePath = path;

            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                switch (family)
                {
                    case UnwiredFamily.BukiGetTreasure:
                    {
                        BukiGetTreasureCatalog cat = BukiGetTreasureCatalog_File.Read(bytes);
                        foreach (BukiGetTreasureEntry e in cat.EntriesByIndex.Values.OrderBy(e => e.Index))
                            Rows.Add(new UnwiredRow { Title = e.DisplayLabel, Detail = e.DetailSummary });
                        HeaderSummary = $"{Rows.Count} entries (idx {cat.Header.MinIndex}..{cat.Header.MaxIndex}). Auto-abilities decodadas via 0x8xxx; nome final de arma NAO provado aqui.";
                        break;
                    }
                    case UnwiredFamily.AlBhedDictionary:
                    {
                        AlBhedDictionary_File t = AlBhedDictionary_File.Read(bytes, FfxEncoding.UsDecoder);
                        foreach (AlBhedDictionary_Entry e in t.Entries)
                            Rows.Add(new UnwiredRow
                            {
                                Title = $"#{e.Index:D3}  src {e.SourceCode:X2}h",
                                Detail = e.IsPadding ? "padding row" : $"source {e.SourceCode:X2}h -> group bucket {e.GroupIndex}",
                            });
                        HeaderSummary = t.Summary;
                        break;
                    }
                    case UnwiredFamily.PointerScriptTable:
                    {
                        PointerScriptTable_File t = PointerScriptTable_File.Read(bytes, FfxEncoding.UsDecoder);
                        foreach (PointerScriptTable_Entry e in t.Entries)
                            Rows.Add(new UnwiredRow
                            {
                                Title = $"slot {e.Index:X2}h",
                                Detail = e.IsEmpty ? "empty slot" : $"script offset {e.Offset:X}h",
                            });
                        HeaderSummary = t.Summary;
                        break;
                    }
                }

                StatusNote = family == UnwiredFamily.BukiGetTreasure
                    ? "Read-only legacy browser. buki_get now has a dedicated Gear Rewards / buki_get atlas under Core Authoring; editing remains outside this screen."
                    : "Read-only. Esta familia TEM writer byte-safe (preserve-only) + gate RT0 provado, mas ainda sem editor dedicado — por isso fica na categoria '???'. Editar continua fora de escopo desta tela.";
            }
            catch (Exception ex)
            {
                StatusNote = $"Falha ao ler {Path.GetFileName(path)}: {ex.Message}";
            }
        }

        // Resolve a kernel-relative file: 1) the loaded workspace master; 2) the EXTRACTED reference master
        // (Path_FfxPs2Root\ffx\master) so the browser works even when the loaded workspace is the asset-sparse Steam mod.
        static string? ResolveKernelFile(string relativePath)
        {
            string? master = Project_Service.Instance.ProjectPath;
            if (!string.IsNullOrWhiteSpace(master))
            {
                string p = Path.Combine(master, relativePath);
                if (File.Exists(p)) return p;
            }

            string? ffxPs2 = Project_Service.Instance.Path_FfxPs2Root;
            if (!string.IsNullOrWhiteSpace(ffxPs2))
            {
                string p = Path.Combine(ffxPs2, "ffx", "master", relativePath);
                if (File.Exists(p)) return p;
            }
            return null;
        }
    }
}
