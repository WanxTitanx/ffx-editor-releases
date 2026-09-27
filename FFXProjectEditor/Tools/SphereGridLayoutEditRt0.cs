using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.SphereGrid;

namespace FFXProjectEditor.Tools
{
    // Headless gate: proves the EDIT-EXISTING path of SphereGridLayoutBuilder (v2 topology editing).
    //
    // v1 (--spheregrid-build-rt0) proved FROM-SCRATCH authoring; this gate proves you can LOAD a shipped grid,
    // edit its TOPOLOGY, and re-serialize byte-faithfully. The model is init-only, so editing goes through
    // SphereGridLayoutBuilder.FromExisting(grid) (full field-preserving clone + real header/contents) plus the
    // Update*/Move*/Set*/Remove* mutators. Assertions:
    //   (1) NO-EDIT IDENTITY: ReadLayout -> FromExisting -> Build re-emits BOTH the layout file (dat01/02/03)
    //       AND the contents file (dat09/10/11) byte-for-byte. This is the strongest proof the seed is lossless
    //       (Unused*/Unknown6/RedundantContent and the real header words all survive).
    //   (2) SINGLE-FIELD ISOLATION: moving exactly one node within the same spatial bucket changes ONLY that
    //       node's 2 PosX bytes in the layout — every other byte is untouched (no field bleed, no drift).
    //   (3) STRUCTURAL EDITS: MoveNode/SetNodeContent/RemoveLink/RemoveNode produce a grid that still Validates
    //       and round-trips (re-read sees the edit), so a canvas UI can edit safely on top of this.
    // A synthetic from-scratch grid runs the same checks so the gate is meaningful with NO corpus; the three
    // shipped grids run too when the abmap dir is present.
    //
    // Run via: FFXProjectEditor.exe --spheregrid-edit-rt0 [abmapDir]
    internal static class SphereGridLayoutEditRt0
    {
        static readonly (string layout, string contents)[] Grids =
        {
            ("dat01", "dat09"), // Original
            ("dat02", "dat10"), // Standard
            ("dat03", "dat11"), // Expert
        };

        public static int Run(string abmapDir)
        {
            Console.WriteLine("=== SphereGridLayoutBuilder EDIT-EXISTING (FromExisting + mutators) RT0 ===");
            int worst = 0;

            // ---- Synthetic (no corpus): build -> FromExisting -> edit, exercising the full mutator surface. ----
            worst = Math.Max(worst, RunSynthetic());

            // ---- Corpus: the three shipped grids, when the abmap dir is reachable. ----
            string? resolved = ResolveDir(abmapDir);
            if (resolved == null)
            {
                Console.WriteLine($"corpus: abmap dir not found ({abmapDir}); synthetic-only run.");
            }
            else
            {
                Console.WriteLine($"corpus: {resolved}");
                int checkedCount = 0;
                foreach ((string layoutName, string contentsName) in Grids)
                {
                    string layoutPath = Path.Combine(resolved, layoutName + ".dat");
                    string contentsPath = Path.Combine(resolved, contentsName + ".dat");
                    if (!File.Exists(layoutPath) || new FileInfo(layoutPath).Length < 0x10)
                    {
                        Console.WriteLine($"[{layoutName}] skip (missing / placeholder stub)");
                        continue;
                    }
                    if (!File.Exists(contentsPath))
                        contentsPath = layoutPath;

                    checkedCount++;
                    worst = Math.Max(worst, RunCorpusGrid(layoutName, layoutPath, contentsPath));
                }
                if (checkedCount == 0)
                    Console.WriteLine("corpus: no real grids found in the abmap dir.");
            }

            Console.WriteLine(worst == 0
                ? "VERDICT: PASS - FromExisting is lossless (layout+contents byte-identical) and single edits stay isolated."
                : "VERDICT: FAIL - see above.");
            return worst;
        }

        static int RunCorpusGrid(string name, string layoutPath, string contentsPath)
        {
            int worst = 0;
            byte[] origLayout = File.ReadAllBytes(layoutPath);
            byte[] origContents = File.ReadAllBytes(contentsPath);

            SphereGridLayoutFile read = SphereGrid_File.ReadLayout(layoutPath, contentsPath, name);
            Console.WriteLine($"[{name}] clusters={read.ClusterCount} nodes={read.NodeCount} links={read.LinkCount} layout={origLayout.Length}B contents={origContents.Length}B");

            // (1) NO-EDIT IDENTITY — layout AND contents.
            SphereGridLayoutFile rebuilt = SphereGridLayoutBuilder.FromExisting(read).Build();
            bool layoutId = rebuilt.RawLayoutBytes.AsSpan().SequenceEqual(origLayout);
            bool contentsId = rebuilt.RawContentsBytes.AsSpan().SequenceEqual(origContents);
            if (!layoutId) { Console.WriteLine($"[{name}] FAIL: no-edit FromExisting layout NOT byte-identical (drift @0x{FirstDiff(origLayout, rebuilt.RawLayoutBytes):X})."); worst = 1; }
            else Console.WriteLine($"[{name}] PASS: no-edit FromExisting -> Build layout byte-identical.");
            if (!contentsId) { Console.WriteLine($"[{name}] FAIL: no-edit FromExisting contents NOT byte-identical (orig={origContents.Length} re={rebuilt.RawContentsBytes.Length})."); worst = 1; }
            else Console.WriteLine($"[{name}] PASS: no-edit FromExisting -> Build contents byte-identical.");

            if (read.NodeCount == 0) return worst;

            // (2) SINGLE-FIELD ISOLATION — move one node's X only; only its 2 PosX bytes may differ.
            int k = read.NodeCount / 2;
            int nodeBase = 0x10 + read.ClusterCount * 0x10;
            int nodePosXOffset = nodeBase + k * 0x0C; // PosX is at +0x00 within the 12-byte node record
            SphereGridNodeEntry orig = read.Nodes[k];
            short newX = PickSameBucketX(orig.PosX, orig.PosY);

            SphereGridLayoutBuilder eb = SphereGridLayoutBuilder.FromExisting(read);
            eb.MoveNode(k, newX, orig.PosY);
            byte[] edited = eb.Build().RawLayoutBytes;

            List<int> diffs = DiffOffsets(origLayout, edited);
            bool isolated = edited.Length == origLayout.Length
                && diffs.All(o => o == nodePosXOffset || o == nodePosXOffset + 1)
                && diffs.Count > 0;
            if (!isolated)
            {
                Console.WriteLine($"[{name}] FAIL: MoveNode({k}) touched {diffs.Count} byte(s) at [{string.Join(",", diffs.Take(8).Select(d => "0x" + d.ToString("X")))}] (expected only 0x{nodePosXOffset:X}..0x{nodePosXOffset + 1:X}).");
                worst = 1;
            }
            else Console.WriteLine($"[{name}] PASS: MoveNode({k}) changed ONLY that node's {diffs.Count} PosX byte(s) @0x{nodePosXOffset:X}.");

            // (3) STRUCTURAL EDITS — content swap + a node removal both re-read consistently.
            SphereGridLayoutBuilder cb = SphereGridLayoutBuilder.FromExisting(read);
            int newContent = orig.ContentIndex == 7 ? 8 : 7;
            cb.SetNodeContent(k, newContent);
            SphereGridLayoutFile cf = cb.Build();
            SphereGridLayoutFile cr = SphereGrid_File.ReadLayout(cf.RawLayoutBytes, cf.RawContentsBytes, "(edit)", "(edit)", name);
            if (cr.Nodes[k].ContentIndex != newContent) { Console.WriteLine($"[{name}] FAIL: SetNodeContent did not persist (got {cr.Nodes[k].ContentIndex}, want {newContent})."); worst = 1; }
            else Console.WriteLine($"[{name}] PASS: SetNodeContent({k}) persisted through layout+contents re-read.");

            // EMPTY-NODE FIDELITY — clearing node k (originally filled) must write RedundantContent 0xFFFF at
            // node+0x06, matching shipped empty nodes (NOT the old 0x00FF). Re-read must also see ContentIndex 0xFF.
            worst = Math.Max(worst, CheckEmptyNodeRedundant(name, read, k));

            SphereGridLayoutBuilder rb = SphereGridLayoutBuilder.FromExisting(read);
            rb.RemoveNode(k);
            SphereGridLayoutFile rf;
            try { rf = rb.Build(); }
            catch (Exception ex) { Console.WriteLine($"[{name}] FAIL: RemoveNode produced an invalid grid: {ex.Message}"); return 1; }
            SphereGridLayoutFile rr = SphereGrid_File.ReadLayout(rf.RawLayoutBytes, rf.RawContentsBytes, "(rm)", "(rm)", name);
            if (rr.NodeCount != read.NodeCount - 1) { Console.WriteLine($"[{name}] FAIL: RemoveNode left nodeCount={rr.NodeCount} (want {read.NodeCount - 1})."); worst = 1; }
            else Console.WriteLine($"[{name}] PASS: RemoveNode -> {rr.NodeCount} nodes, {rr.LinkCount} links, still valid + round-trips.");

            return worst;
        }

        static int RunSynthetic()
        {
            int worst = 0;

            SphereGridLayoutBuilder b = new() { DisplayName = "Edit RT0 Synthetic" };
            int c0 = b.AddCluster(0, 0, radiusType: 1);
            int c1 = b.AddCluster(100, 0, radiusType: 2);
            int n0 = b.AddNode(0, 0, (ushort)c0, contentIndex: 5);
            int n1 = b.AddNode(20, 0, (ushort)c0, contentIndex: SphereGridLayoutBuilder.EmptyContent);
            int n2 = b.AddNode(100, 0, (ushort)c1, contentIndex: 12);
            int n3 = b.AddNode(120, 0, (ushort)c1, contentIndex: 200);
            b.AddLink((ushort)n0, (ushort)n1);
            b.AddLink((ushort)n1, (ushort)n2);
            b.AddLink((ushort)n2, (ushort)n3, anchorNode: (ushort)n0);
            SphereGridLayoutFile baseGrid = b.Build();

            // FromExisting on a built grid must round-trip both files byte-identically (no-edit identity).
            SphereGridLayoutFile seeded = SphereGridLayoutBuilder.FromExisting(baseGrid).Build();
            bool seedOk = seeded.RawLayoutBytes.AsSpan().SequenceEqual(baseGrid.RawLayoutBytes)
                       && seeded.RawContentsBytes.AsSpan().SequenceEqual(baseGrid.RawContentsBytes);
            if (!seedOk) { Console.WriteLine("synthetic FAIL: FromExisting(built) not byte-identical (layout/contents)."); worst = 1; }
            else Console.WriteLine("synthetic PASS: FromExisting(built) -> Build byte-identical (layout+contents).");

            // Single-field isolation on the synthetic grid (node n2's PosX only).
            int nodeBase = 0x10 + baseGrid.ClusterCount * 0x10;
            int nodePosXOffset = nodeBase + n2 * 0x0C;
            SphereGridLayoutBuilder eb = SphereGridLayoutBuilder.FromExisting(baseGrid);
            short syntheticNewX = PickSameBucketX(baseGrid.Nodes[n2].PosX, baseGrid.Nodes[n2].PosY);
            eb.MoveNode(n2, syntheticNewX, baseGrid.Nodes[n2].PosY);
            List<int> diffs = DiffOffsets(baseGrid.RawLayoutBytes, eb.Build().RawLayoutBytes);
            bool isolated = diffs.Count > 0 && diffs.All(o => o == nodePosXOffset || o == nodePosXOffset + 1);
            if (!isolated) { Console.WriteLine($"synthetic FAIL: MoveNode isolation touched [{string.Join(",", diffs.Select(d => "0x" + d.ToString("X")))}] (want 0x{nodePosXOffset:X}..)."); worst = 1; }
            else Console.WriteLine($"synthetic PASS: MoveNode isolated to PosX bytes @0x{nodePosXOffset:X}.");

            // Mutator surface: content/cluster/link edits + RemoveLink + RemoveNode (with index remap) stay valid.
            SphereGridLayoutBuilder mb = SphereGridLayoutBuilder.FromExisting(baseGrid);
            mb.SetNodeContent(n0, 42);
            mb.SetNodeCluster(n3, (ushort)c0);
            mb.UpdateCluster(c1, 200, 50, radiusType: 3);
            mb.RemoveLink(0);            // drop n0<->n1
            mb.RemoveNode(n1);           // remaps n2->1, n3->2; link n1<->n2 dropped, anchor n0 stays
            SphereGridLayoutFile mf;
            try { mf = mb.Build(); }
            catch (Exception ex) { Console.WriteLine($"synthetic FAIL: mutator chain produced invalid grid: {ex.Message}"); return 1; }
            SphereGridLayoutFile mr = SphereGrid_File.ReadLayout(mf.RawLayoutBytes, mf.RawContentsBytes, "(mut)", "(mut)", "synthetic");
            bool mutOk = mr.NodeCount == 3 && mr.Nodes[0].ContentIndex == 42 && mr.Clusters[1].PosX == 200 && mr.Clusters[1].RadiusType == 3;
            if (!mutOk) { Console.WriteLine($"synthetic FAIL: mutator re-read mismatch (nodes={mr.NodeCount}, n0.content={mr.Nodes[0].ContentIndex}, c1.posX={mr.Clusters[1].PosX}, c1.rt={mr.Clusters[1].RadiusType})."); worst = 1; }
            else Console.WriteLine("synthetic PASS: content/cluster/link/remove edits round-trip (nodes 4->3, content/cluster applied).");

            // Seeded AddNode must still honor the explicit cluster argument. AddNodeNear/AddNodeLike are the
            // intentional metadata-inheriting APIs; the old AddNode API stays literal for older callers.
            SphereGridLayoutBuilder ab = SphereGridLayoutBuilder.FromExisting(baseGrid);
            int appended = ab.AddNode(50, 50, (ushort)c1, contentIndex: 9);
            SphereGridLayoutFile af = ab.Build();
            bool addNodeOk = af.Nodes[appended].Cluster == c1
                && af.Nodes[appended].Unknown6 == SphereGridLayoutBuilder.ComputeUnknown6(50, 50)
                && ab.AddedNodeCount == 1
                && ab.HasRuntimeUnprovenTopologyAppend;
            if (!addNodeOk)
            {
                Console.WriteLine($"synthetic FAIL: seeded AddNode did not preserve explicit cluster/bucket (cluster={af.Nodes[appended].Cluster}, u6=0x{af.Nodes[appended].Unknown6:X4}).");
                worst = 1;
            }
            else Console.WriteLine("synthetic PASS: seeded AddNode preserves explicit cluster and recomputes Unknown6.");

            // EMPTY-NODE FIDELITY — clearing a node's content must write RedundantContent 0xFFFF at node+0x06
            // (shipped convention: filled = 0x00<content>, empty = 0xFFFF; AURON scout 3.444/3.444), NOT 0x00FF.
            // n0 starts filled (content 5), so this is a genuine fill->empty transition.
            worst = Math.Max(worst, CheckEmptyNodeRedundant("synthetic", baseGrid, n0));

            // Out-of-range mutators must throw (no silent corruption).
            bool threw = false;
            try { SphereGridLayoutBuilder.FromExisting(baseGrid).MoveNode(999, 0, 0); }
            catch (ArgumentOutOfRangeException) { threw = true; }
            if (!threw) { Console.WriteLine("synthetic FAIL: MoveNode(out-of-range) did not throw."); worst = 1; }
            else Console.WriteLine("synthetic PASS: out-of-range mutator throws (positive-catch).");

            return worst;
        }

        // Clear one node to empty (SetNodeContent 0xFF) and prove the layout-side redundant copy at node+0x06 is
        // 0xFFFF (shipped empty convention), the old 0x00FF bug is gone, and the contents byte + re-read see empty.
        static int CheckEmptyNodeRedundant(string name, SphereGridLayoutFile grid, int nodeIndex)
        {
            SphereGridLayoutBuilder b = SphereGridLayoutBuilder.FromExisting(grid);
            b.SetNodeContent(nodeIndex, SphereGridLayoutBuilder.EmptyContent);
            SphereGridLayoutFile built = b.Build();

            int redundantOffset = 0x10 + grid.ClusterCount * 0x10 + nodeIndex * 0x0C + 0x06;
            byte[] layout = built.RawLayoutBytes;
            ushort redundant = (ushort)(layout[redundantOffset] | (layout[redundantOffset + 1] << 8));

            int contentsByteOffset = 0x08 + nodeIndex;
            byte contentByte = contentsByteOffset < built.RawContentsBytes.Length ? built.RawContentsBytes[contentsByteOffset] : (byte)0;
            SphereGridLayoutFile re = SphereGrid_File.ReadLayout(built.RawLayoutBytes, built.RawContentsBytes, "(empty)", "(empty)", name);

            if (redundant != 0xFFFF)
            {
                Console.WriteLine($"[{name}] FAIL: SetNodeContent({nodeIndex}, empty) wrote RedundantContent 0x{redundant:X4} @0x{redundantOffset:X} (want 0xFFFF, NOT 0x00FF).");
                return 1;
            }
            if (contentByte != 0xFF || re.Nodes[nodeIndex].ContentIndex != 0xFF)
            {
                Console.WriteLine($"[{name}] FAIL: cleared node re-read as content 0x{re.Nodes[nodeIndex].ContentIndex:X2} (contents byte 0x{contentByte:X2}); want 0xFF.");
                return 1;
            }
            Console.WriteLine($"[{name}] PASS: SetNodeContent({nodeIndex}, empty) -> RedundantContent 0xFFFF @0x{redundantOffset:X} + contents byte 0xFF (re-read empty).");
            return 0;
        }

        static short PickSameBucketX(short x, short y)
        {
            ushort bucket = SphereGridLayoutBuilder.ComputeUnknown6(x, y);
            int xCell = (((int)x) + 2560) / 256;
            int minX = xCell * 256 - 2560;
            int maxX = minX + 255;

            for (int candidate = minX + 1; candidate <= Math.Min(maxX, minX + 8); candidate++)
            {
                short sx = unchecked((short)candidate);
                if (sx != x && SphereGridLayoutBuilder.ComputeUnknown6(sx, y) == bucket)
                    return sx;
            }

            for (int candidate = maxX - 1; candidate >= Math.Max(minX, maxX - 8); candidate--)
            {
                short sx = unchecked((short)candidate);
                if (sx != x && SphereGridLayoutBuilder.ComputeUnknown6(sx, y) == bucket)
                    return sx;
            }

            throw new InvalidOperationException($"Could not find a same-bucket X shift for ({x},{y}) bucket={bucket:X4}.");
        }

        static List<int> DiffOffsets(byte[] a, byte[] b)
        {
            List<int> diffs = new();
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++)
                if (a[i] != b[i]) diffs.Add(i);
            for (int i = n; i < Math.Max(a.Length, b.Length); i++) diffs.Add(i);
            return diffs;
        }

        static int FirstDiff(byte[] a, byte[] b)
        {
            int n = Math.Min(a.Length, b.Length);
            int i = 0;
            while (i < n && a[i] == b[i]) i++;
            return i;
        }

        static string? ResolveDir(string dir)
        {
            if (Directory.Exists(dir))
                return dir;

            string[] langs = { "jppc", "uspc", "inpc", "new_uspc" };
            foreach (string lang in langs)
                foreach (string other in langs)
                {
                    string candidate = dir.Replace($"\\{lang}\\", $"\\{other}\\", StringComparison.OrdinalIgnoreCase);
                    if (!string.Equals(candidate, dir, StringComparison.OrdinalIgnoreCase) && Directory.Exists(candidate))
                        return candidate;
                }
            return null;
        }
    }
}
