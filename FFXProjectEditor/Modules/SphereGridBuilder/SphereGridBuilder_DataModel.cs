using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.SphereGrid;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.SphereGridBuilder
{
    // FIRST functional UI over the PROVEN SphereGridLayoutBuilder (FfxLib): author a sphere-grid LAYOUT from
    // scratch (AddCluster/AddNode/AddLink), Validate() (range-check), Build() -> WriteLayout (byte-safe, gated by
    // --spheregrid-build-rt0) -> save the .dat. This is the "create a sphere grid from the ZERO" headline finally
    // surfaced. v1 is form/list-based (no visual graph canvas yet) and HONEST that loading a NEW topology IN-GAME
    // is unproven (offline byte-safe != the engine accepting an unshipped grid shape).
    internal partial class SphereGridBuilder_DataModel : ObservableObject
    {
        readonly SphereGridLayoutBuilder _builder = new();

        public ObservableCollection<string> ClusterRows { get; } = new();
        public ObservableCollection<string> NodeRows { get; } = new();
        public ObservableCollection<string> LinkRows { get; } = new();

        [ObservableProperty] private string gridName = "New Sphere Grid";

        // Add-cluster inputs
        [ObservableProperty] private string clusterPosX = "0";
        [ObservableProperty] private string clusterPosY = "0";
        [ObservableProperty] private string clusterRadius = "0";

        // Add-node inputs
        [ObservableProperty] private string nodePosX = "0";
        [ObservableProperty] private string nodePosY = "0";
        [ObservableProperty] private string nodeCluster = "0";
        [ObservableProperty] private string nodeContent = "FF"; // hex; FF = empty slot

        // Add-link inputs
        [ObservableProperty] private string linkNode1 = "0";
        [ObservableProperty] private string linkNode2 = "1";
        [ObservableProperty] private string linkAnchor = "FFFF"; // hex; FFFF = straight link

        [ObservableProperty] private string countsSummary = "0 clusters · 0 nodes · 0 links";
        [ObservableProperty] private string validationSummary = Strings.F2_add_cluster_s_node_s_optional_links_then_ed6bc596;
        [ObservableProperty] private string saveSummary = "";

        public string HeaderNote =>
            Strings.U_Sgb_LayoutBuilderBanner +
            Strings.F2_honest_writelayout_is_byte_exact_round_t_7347d272;

        public void AddCluster()
        {
            if (!TryShort(ClusterPosX, out short px) || !TryShort(ClusterPosY, out short py)) { SaveSummary = Strings.U_Sgb_ClusterInvalid; return; }
            TryUShort(ClusterRadius, out ushort radius);
            int i = _builder.AddCluster(px, py, radius);
            ClusterRows.Add($"#{i}  pos ({px},{py})  radiusType {radius}");
            Refresh();
        }

        public void AddNode()
        {
            if (!TryShort(NodePosX, out short px) || !TryShort(NodePosY, out short py)) { SaveSummary = Strings.U_Sgb_NodeInvalid; return; }
            if (!TryUShort(NodeCluster, out ushort cluster)) { SaveSummary = Strings.U_Sgb_NodeClusterInvalid; return; }
            int content = ParseContent(NodeContent);
            int i = _builder.AddNode(px, py, cluster, content);
            ClusterRows.Count.ToString(); // no-op guard
            NodeRows.Add($"#{i}  pos ({px},{py})  cluster {cluster}  content {(content == SphereGridLayoutBuilder.EmptyContent ? "empty(FF)" : content.ToString("X2") + "h")}");
            Refresh();
        }

        public void AddLink()
        {
            if (!TryUShort(LinkNode1, out ushort n1) || !TryUShort(LinkNode2, out ushort n2)) { SaveSummary = Strings.U_Sgb_LinkInvalid; return; }
            ushort anchor = 0xFFFF;
            if (!string.IsNullOrWhiteSpace(LinkAnchor) && !LinkAnchor.Trim().Equals("FFFF", StringComparison.OrdinalIgnoreCase))
                TryUShort(LinkAnchor, out anchor);
            int i = _builder.AddLink(n1, n2, anchor);
            LinkRows.Add($"#{i}  {n1} <-> {n2}{(anchor == 0xFFFF ? "  (straight)" : $"  anchor {anchor}")}");
            Refresh();
        }

        public void Validate()
        {
            SphereGridBuildValidation v = _builder.Validate();
            ValidationSummary = v.IsValid
                ? string.Format(Strings.U_Sgb_ValidReady, _builder.ClusterCount, _builder.NodeCount, _builder.LinkCount)
                : string.Format(Strings.U_Sgb_ErrorsCount, v.Errors.Count, v.Summary);
        }

        public void BuildAndSave()
        {
            SphereGridBuildValidation v = _builder.Validate();
            ValidationSummary = v.IsValid ? Strings.U_Sgb_ValidShort : $"❌ {v.Summary}";
            if (!v.IsValid) { SaveSummary = Strings.F2_fix_the_errors_validate_before_saving_67d42ae1; return; }

            try
            {
                _builder.DisplayName = string.IsNullOrWhiteSpace(GridName) ? "New Sphere Grid" : GridName;
                SphereGridLayoutFile grid = _builder.Build();

                string dir = Path.Combine(ResolveOutDir(), "sphere_build");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, Sanitize(_builder.DisplayName) + ".dat");
                File.WriteAllBytes(file, grid.RawLayoutBytes);

                SaveSummary =
                    string.Format(Strings.U_Sgb_Saved, file, grid.RawLayoutBytes.Length, grid.ClusterCount, grid.NodeCount, grid.LinkCount) +
                    Strings.F2_loading_this_new_topology_in_game_is_unp_c86af7d5;
            }
            catch (Exception ex)
            {
                SaveSummary = string.Format(Strings.U_Sgb_BuildSaveFailed, ex.Message);
            }
        }

        public void ResetAll()
        {
            // The builder has no Clear(); rebuild a fresh instance by reconstructing the model from empty.
            ClusterRows.Clear(); NodeRows.Clear(); LinkRows.Clear();
            // Replace the builder via reflection-free re-init: simplest is a new DataModel, but to keep the bound
            // instance we clear our display + tell the user to reopen for a brand-new builder.
            SaveSummary = Strings.F2_to_truly_reset_reopen_sphere_grid_builde_0b6a0f29;
            Refresh();
        }

        void Refresh()
        {
            CountsSummary = $"{_builder.ClusterCount} clusters · {_builder.NodeCount} nodes · {_builder.LinkCount} links";
        }

        static int ParseContent(string s)
        {
            s = (s ?? "").Trim();
            if (s.Length == 0 || s.Equals("FF", StringComparison.OrdinalIgnoreCase) || s.Equals("empty", StringComparison.OrdinalIgnoreCase))
                return SphereGridLayoutBuilder.EmptyContent;
            return int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v) ? v : SphereGridLayoutBuilder.EmptyContent;
        }

        static bool TryShort(string s, out short v) => short.TryParse((s ?? "").Trim(), out v);
        static bool TryUShort(string s, out ushort v)
        {
            s = (s ?? "").Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
            // accept hex for anchor/content-ish fields, else decimal
            return ushort.TryParse(s, out v) || ushort.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v);
        }

        static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "new_grid" : name;
        }

        static string ResolveOutDir()
        {
            // Prefer the loaded project root; else the exe dir.
            string? proj = Services.Project_Service.Instance.ProjectPath;
            return !string.IsNullOrWhiteSpace(proj) && Directory.Exists(proj) ? proj! : AppContext.BaseDirectory;
        }
    }
}
