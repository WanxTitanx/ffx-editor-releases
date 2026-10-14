using System;
using System.Collections.Generic;
using System.IO;
using FFXProjectEditor.FfxLib.Save;

namespace FFXProjectEditor.Tools
{
    /// <summary>
    /// RT0 for native extra-node persistence (Phase S2) and the empirical offset
    /// prover (Phase S1). The round-trip verb proves the editor can read/write the
    /// 861st node (index 860) at the native runtime-table offset (save+10404) and
    /// that doing so leaves the rest of the SG table byte-identical. The diff verb
    /// compares two saves and maps each changed SG byte to its runtime-table
    /// meaning — used in-game to prove where node 860 actually lands after the
    /// game saves it (sidecar OFF).
    /// </summary>
    internal static class SphereGridExtraNodeRt0
    {
        public static int RunRoundTrip(string[] args)
        {
            if (args.Length < 2 || args[0] != "--spheregrid-extra-node-rt0")
            {
                Console.WriteLine("usage: --spheregrid-extra-node-rt0 <save> [--node N] [--content C] [--status S]");
                return 2;
            }

            int node = ArgInt(args, "--node", 860);
            int content = ArgInt(args, "--content", 9);
            int status = ArgInt(args, "--status", 0);

            try
            {
                FfxSaveFile save = FfxSaveFile.Load(args[1]);
                byte[] sgBefore = SliceSg(save.Core);

                if (!FfxSaveSphereGridRuntimeTable.NodeFitsNatively(node))
                {
                    Console.WriteLine($"FAIL: node {node} collides with link region (max {FfxSaveSphereGridRuntimeTable.MaxNodeIndexBeforeLinks}).");
                    return 3;
                }

                (int c0, int s0) = FfxSaveSphereGridRuntimeTable.ReadNode(save.Core, node);
                Console.WriteLine($"native_offset: content=0x{FfxSaveSphereGridRuntimeTable.NodeContentOffset(node):X4} status=0x{FfxSaveSphereGridRuntimeTable.NodeStatusOffset(node):X4}");
                Console.WriteLine($"before: node[{node}] = {c0:X2}/{s0:X2} ({FfxSaveSphereGridRuntimeTable.DescribeOffset(FfxSaveSphereGridRuntimeTable.NodeContentOffset(node))})");

                FfxSaveSphereGridRuntimeTable.WriteNode(save.Core, node, content, status);

                string tmp = Path.Combine(Path.GetTempPath(), $"sgm_extra_node_{Guid.NewGuid():N}.bin");
                try
                {
                    save.SaveAs(tmp, FfxSaveFormat.RawPs2);
                    FfxSaveFile reload = FfxSaveFile.Load(tmp);
                    (int c1, int s1) = FfxSaveSphereGridRuntimeTable.ReadNode(reload.Core, node);
                    Console.WriteLine($"after_reload: node[{node}] = {c1:X2}/{s1:X2}");

                    bool roundTrip = c1 == (content & 0xFF) && s1 == (status & 0xFF);

                    // Byte-identity of the SG region except the two bytes we changed.
                    byte[] sgAfter = SliceSg(reload.Core);
                    var changed = new List<int>();
                    for (int i = 0; i < sgAfter.Length; i++)
                        if (sgAfter[i] != sgBefore[i]) changed.Add(FfxSaveSphereGridRuntimeTable.TableBaseOffset + i);

                    int expA = FfxSaveSphereGridRuntimeTable.NodeContentOffset(node);
                    int expB = FfxSaveSphereGridRuntimeTable.NodeStatusOffset(node);
                    bool onlyExpected = changed.TrueForAll(o => o == expA || o == expB);

                    Console.WriteLine($"sg_region_changed_bytes: {changed.Count}");
                    foreach (int o in changed)
                        Console.WriteLine($"  0x{o:X4} -> {FfxSaveSphereGridRuntimeTable.DescribeOffset(o)}");

                    bool pass = roundTrip && onlyExpected;
                    Console.WriteLine($"verdict: roundTrip={roundTrip} onlyExpectedBytes={onlyExpected} => {(pass ? "PASS" : "FAIL")}");
                    return pass ? 0 : 3;
                }
                finally
                {
                    try { File.Delete(tmp); } catch { /* best effort */ }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        public static int RunDiff(string[] args)
        {
            if (args.Length < 3 || args[0] != "--spheregrid-save-diff")
            {
                Console.WriteLine("usage: --spheregrid-save-diff <saveBefore> <saveAfter>");
                return 2;
            }

            try
            {
                FfxSaveFile a = FfxSaveFile.Load(args[1]);
                FfxSaveFile b = FfxSaveFile.Load(args[2]);
                byte[] sgA = SliceSg(a.Core);
                byte[] sgB = SliceSg(b.Core);

                int changed = 0;
                for (int i = 0; i < sgA.Length; i++)
                {
                    if (sgA[i] == sgB[i]) continue;
                    int off = FfxSaveSphereGridRuntimeTable.TableBaseOffset + i;
                    Console.WriteLine($"0x{off:X4}: {sgA[i]:X2} -> {sgB[i]:X2}  {FfxSaveSphereGridRuntimeTable.DescribeOffset(off)}");
                    changed++;
                }
                Console.WriteLine($"sg_table_changed_bytes: {changed}");
                return changed > 0 ? 0 : 4;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        static byte[] SliceSg(FfxSaveCore core)
        {
            byte[] slice = new byte[FfxSaveSphereGridRuntimeTable.TableSpanBytes];
            Array.Copy(core.Data, FfxSaveSphereGridRuntimeTable.TableBaseOffset, slice, 0, slice.Length);
            return slice;
        }

        static int ArgInt(string[] args, string name, int fallback)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) && int.TryParse(args[i + 1], out int v))
                    return v;
            return fallback;
        }
    }
}
