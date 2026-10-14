using System;
using FFXProjectEditor.FfxLib.SphereGrid;

namespace FFXProjectEditor.Tools
{
    // Headless gate: proves the FROM-SCRATCH SphereGridLayoutBuilder authoring layer.
    // No corpus needed — builds a small grid in memory, serializes via the proven WriteLayout, re-reads,
    // and asserts (1) structural equality, (2) second-pass byte-identity (idempotent serialize), and
    // (3) positive-catch: the range validator REJECTS a dangling link / bad cluster so authoring can never
    // silently emit a crash file. Together with --spheregrid-layout-rt0 (byte-faithful serializer) this
    // closes "build a sphere grid from scratch".
    //
    // Run via: FFXProjectEditor.exe --spheregrid-build-rt0
    internal static class SphereGridLayoutBuildRt0
    {
        public static int Run()
        {
            Console.WriteLine("=== SphereGridLayoutBuilder (from-scratch authoring) RT0 ===");
            int worst = 0;

            // 1) Build a small grid: 2 clusters, 4 nodes, 3 links.
            SphereGridLayoutBuilder b = new() { DisplayName = "RT0 Test Grid" };
            int c0 = b.AddCluster(0, 0, radiusType: 1);
            int c1 = b.AddCluster(100, 0, radiusType: 2);
            int n0 = b.AddNode(0, 0, (ushort)c0, contentIndex: 5);
            int n1 = b.AddNode(20, 0, (ushort)c0, contentIndex: SphereGridLayoutBuilder.EmptyContent);
            int n2 = b.AddNode(100, 0, (ushort)c1, contentIndex: 12);
            int n3 = b.AddNode(120, 0, (ushort)c1, contentIndex: 200);
            b.AddLink((ushort)n0, (ushort)n1);
            b.AddLink((ushort)n1, (ushort)n2);
            b.AddLink((ushort)n2, (ushort)n3, anchorNode: (ushort)n0);

            SphereGridBuildValidation validation = b.Validate();
            if (!validation.IsValid)
            {
                Console.WriteLine($"FAIL: Validate() rejected a VALID grid: {validation.Summary}");
                return 1;
            }

            SphereGridLayoutFile built;
            try { built = b.Build(); }
            catch (Exception ex) { Console.WriteLine($"FAIL: Build() threw on a valid grid: {ex.Message}"); return 1; }

            Console.WriteLine($"built: clusters={built.ClusterCount} nodes={built.NodeCount} links={built.LinkCount} " +
                              $"layout={built.RawLayoutBytes.Length}B contents={built.RawContentsBytes.Length}B");

            // 2) Round-trip: ReadLayout(built bytes) and compare structure back.
            SphereGridLayoutFile re = SphereGrid_File.ReadLayout(
                built.RawLayoutBytes, built.RawContentsBytes, "(built)", "(built)", "RT0 Test Grid");

            bool structOk =
                re.ClusterCount == built.ClusterCount && re.NodeCount == built.NodeCount && re.LinkCount == built.LinkCount &&
                re.Clusters.Count == 2 && re.Nodes.Count == 4 && re.Links.Count == 3 &&
                re.Clusters[1].PosX == 100 && re.Clusters[1].RadiusType == 2 &&
                re.Nodes[2].PosX == 100 && re.Nodes[2].Cluster == c1 && re.Nodes[2].ContentIndex == 12 &&
                re.Nodes[3].ContentIndex == 200 && re.Nodes[1].ContentIndex == SphereGridLayoutBuilder.EmptyContent &&
                re.Links[2].Node1 == n2 && re.Links[2].Node2 == n3 && re.Links[2].AnchorNode == n0;

            if (!structOk) { Console.WriteLine("FAIL: round-trip structure mismatch (build -> WriteLayout -> ReadLayout)."); worst = 1; }
            else Console.WriteLine("PASS: round-trip structure equal (clusters/nodes/links/positions/content).");

            // 3) Second-pass byte-identity: re-serialize the read-back grid == first serialization (idempotent).
            byte[] re2 = SphereGrid_File.WriteLayout(re);
            bool byteOk = re2.Length == built.RawLayoutBytes.Length && re2.AsSpan().SequenceEqual(built.RawLayoutBytes);
            if (!byteOk) { Console.WriteLine("FAIL: second-pass WriteLayout not byte-identical (non-idempotent)."); worst = 1; }
            else Console.WriteLine("PASS: second-pass byte-identical (idempotent serialize).");

            // 4) Positive-catch: the validator MUST reject a dangling link + out-of-range cluster.
            SphereGridLayoutBuilder bad = new();
            bad.AddCluster(0, 0);
            bad.AddNode(0, 0, cluster: 0);
            bad.AddLink(0, 9);              // node 9 does not exist
            bad.AddNode(0, 0, cluster: 7);  // cluster 7 does not exist
            SphereGridBuildValidation badV = bad.Validate();
            if (badV.IsValid) { Console.WriteLine("FAIL: validator ACCEPTED an out-of-range grid (no positive-catch)."); worst = 1; }
            else Console.WriteLine($"PASS: validator rejected out-of-range grid ({badV.Errors.Count} error(s)).");

            bool threw = false;
            try { bad.Build(); } catch (InvalidOperationException) { threw = true; }
            if (!threw) { Console.WriteLine("FAIL: Build() did not throw on an invalid grid."); worst = 1; }
            else Console.WriteLine("PASS: Build() throws on an invalid grid.");

            Console.WriteLine(worst == 0
                ? "VERDICT: PASS - from-scratch builder round-trips byte-identically + validator positive-catches."
                : "VERDICT: FAIL - see above.");
            return worst;
        }
    }
}
