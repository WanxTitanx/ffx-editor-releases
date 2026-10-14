using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.SphereGrid;

namespace FFXProjectEditor.Tools
{
    // Insert ONE new node (the 861st) into a shipped sphere-grid layout and prove it offline.
    // RT2 2026-07-11: the generated 861/882 layout opens the menu but crashes on exit;
    // this tool proves byte layout only and does not establish deploy safety.
    //
    // The grid TOPOLOGY (which nodes exist, where, links, Lv-locks) lives in the layout asset (abmap
    // dat01/02/03), NOT in the save. RE (FFX_SPHEREGRID_SAVE_ORCHESTRATOR_RE, Session 4) proved the menu
    // loader A45570 only builds node records when the dat0X header magic byte == '1' (0x31) and uses the
    // header NodeCount (file +0x04). A malformed asset (magic 0x00 / wrong count) yields the empty/unmovable
    // grid we hit in RT2-03. So adding a node = emit a VALID layout: 861 nodes / 882 links, magic intact.
    //
    // This reuses the RT0-proven SphereGridLayoutBuilder (FromExisting + AddNodeLike + AddLink + WriteLayout),
    // so the only new bytes are the appended node/link records and the bumped header counts; everything else
    // is byte-identical to vanilla. The new node clones an existing node of the SAME content (so its content
    // index is a guaranteed-valid shipped value) and is linked to that neighbor so it is reachable.
    //
    // Run:
    //   --spheregrid-insert-node <inDat02> <inDat10> <outDat02> <outDat10>
    //        (--content <byte> | --content-name "HP +300" --panel-jp <panel.bin> [--panel-us <panel.bin>])
    //        [--near <existingNodeIndex>] [--dx <delta>]
    internal static class SphereGridInsertNodeRt0
    {
        public static int Run(string[] args)
        {
            // Diagnostic: --spheregrid-insert-node --list-types <panelJp> [panelUs] [filterSubstr]
            // Dumps node-type index + US/JP name + effect/amount so the right content index can be picked.
            if (args.Length >= 3 && args[1] == "--list-types")
            {
                string jp = args[2];
                string? us = args.Length > 3 && !args[3].StartsWith("--") ? args[3] : null;
                string? filter = args.Length > 4 ? args[4] : null;
                SphereGridNodeTypeTable tbl = SphereGrid_File.ReadNodeTypes(jp, us);
                foreach (SphereGridNodeTypeEntry e in tbl.Entries)
                {
                    string nm = e.Name.UsText ?? "";
                    string jpn = e.Name.JpText ?? "";
                    if (filter != null && !nm.Contains(filter, StringComparison.OrdinalIgnoreCase)
                                       && !jpn.Contains(filter, StringComparison.OrdinalIgnoreCase))
                        continue;
                    Console.WriteLine($"0x{e.Index:X2}  us=\"{nm}\"  jp=\"{jpn}\"  effect=0x{e.NodeEffectBitfield:X4} move=0x{e.LearnedMove:X4} amt={e.IncreaseAmount}");
                }
                return 0;
            }

            if (args.Length < 5)
            {
                Console.WriteLine("usage: --spheregrid-insert-node <inDat02> <inDat10> <outDat02> <outDat10> " +
                                  "(--content <byte> | --content-name \"HP +300\" --panel-jp <panel.bin> [--panel-us <panel.bin>]) " +
                                  "[--near <nodeIdx>] [--dx <delta>]");
                return 2;
            }

            string inLayout = args[1];
            string inContents = args[2];
            string outLayout = args[3];
            string outContents = args[4];

            int? explicitContent = null;
            string? contentName = null;
            string? panelJp = null;
            string? panelUs = null;
            int? nearIndex = null;
            short dx = 60;
            bool noLink = false;

            for (int i = 5; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--content": explicitContent = ParseInt(args[++i]); break;
                    case "--content-name": contentName = args[++i]; break;
                    case "--panel-jp": panelJp = args[++i]; break;
                    case "--panel-us": panelUs = args[++i]; break;
                    case "--near": nearIndex = ParseInt(args[++i]); break;
                    case "--dx": dx = (short)ParseInt(args[++i]); break;
                    // Diagnostic: add the node WITHOUT a link (861 nodes / 881 links) to isolate
                    // node-indexed vs link-indexed heap overflow on the SGM exit crash.
                    case "--no-link": noLink = true; break;
                }
            }

            Console.WriteLine("=== SphereGrid insert-node (861st) RT0 ===");
            SphereGridLayoutFile grid = SphereGrid_File.ReadLayout(inLayout, inContents, "insert");
            Console.WriteLine($"in: clusters={grid.ClusterCount} nodes={grid.NodeCount} links={grid.LinkCount} " +
                              $"layout={grid.FileSize}B contents={grid.ContentsFileSize}B magic=0x{grid.RawLayoutBytes[0]:X2}");

            if (grid.RawLayoutBytes[0] != 0x31)
            {
                Console.WriteLine($"FAIL: source layout magic byte is 0x{grid.RawLayoutBytes[0]:X2}, not 0x31 ('1'). Refusing to build on a broken asset.");
                return 1;
            }

            int content = ResolveContent(explicitContent, contentName, panelJp, panelUs);
            if (content < 0)
                return 1;
            Console.WriteLine($"target content index = 0x{content:X2} ({content})");

            int template = ResolveTemplate(grid, nearIndex, content);
            if (template < 0)
            {
                Console.WriteLine($"FAIL: no existing node uses content 0x{content:X2} to clone, and --near was not given. " +
                                  "Pass --near <nodeIdx> to pick a neighbor explicitly.");
                return 1;
            }
            SphereGridNodeEntry t = grid.Nodes[template];
            Console.WriteLine($"template node {template}: pos=({t.PosX},{t.PosY}) cluster={t.Cluster} content=0x{t.ContentIndex:X2}");

            SphereGridLayoutBuilder b = SphereGridLayoutBuilder.FromExisting(grid);
            short newX = unchecked((short)(t.PosX + dx));
            short newY = t.PosY;
            int newIdx = b.AddNodeLike(template, newX, newY, content);
            int newLink = noLink ? -1 : b.AddLink((ushort)template, (ushort)newIdx);
            Console.WriteLine(noLink
                ? $"appended node {newIdx} at ({newX},{newY}) NO LINK (diagnostic 861/881)"
                : $"appended node {newIdx} at ({newX},{newY}) + link {newLink} ({template}<->{newIdx})");

            SphereGridBuildValidation v = b.Validate();
            if (!v.IsValid)
            {
                Console.WriteLine($"FAIL: builder rejected the grid: {v.Summary}");
                return 1;
            }

            SphereGridLayoutFile built = b.Build();

            int expectLinks = grid.LinkCount + (noLink ? 0 : 1);
            int worst = 0;
            worst |= Assert("node count 861", built.NodeCount == grid.NodeCount + 1, $"got {built.NodeCount}");
            worst |= Assert($"link count {expectLinks}", built.LinkCount == expectLinks, $"got {built.LinkCount}");
            worst |= Assert("layout magic 0x31", built.RawLayoutBytes[0] == 0x31, $"got 0x{built.RawLayoutBytes[0]:X2}");

            int expectLayout = 0x10 + built.ClusterCount * 0x10 + built.NodeCount * 0x0C + built.LinkCount * 0x08;
            worst |= Assert($"layout size {expectLayout}", built.RawLayoutBytes.Length == expectLayout, $"got {built.RawLayoutBytes.Length}");
            worst |= Assert($"contents size {0x08 + built.NodeCount}", built.RawContentsBytes.Length == 0x08 + built.NodeCount, $"got {built.RawContentsBytes.Length}");

            byte contentByte = built.RawContentsBytes[0x08 + newIdx];
            worst |= Assert($"contents[{newIdx}]=0x{content:X2}", contentByte == (byte)content, $"got 0x{contentByte:X2}");

            // Header NodeCount/LinkCount live at layout file +0x04 / +0x06 (the words A45570 reads).
            ushort hdrNodes = (ushort)(built.RawLayoutBytes[0x04] | (built.RawLayoutBytes[0x05] << 8));
            ushort hdrLinks = (ushort)(built.RawLayoutBytes[0x06] | (built.RawLayoutBytes[0x07] << 8));
            worst |= Assert("header NodeCount=861", hdrNodes == 861, $"got {hdrNodes}");
            worst |= Assert($"header LinkCount={expectLinks}", hdrLinks == expectLinks, $"got {hdrLinks}");

            // Re-read proof: the emitted asset parses back with the new node (+ link unless --no-link).
            SphereGridLayoutFile re = SphereGrid_File.ReadLayout(built.RawLayoutBytes, built.RawContentsBytes, "(re)", "(re)", "insert");
            bool linked = re.Links.Any(l => (l.Node1 == template && l.Node2 == newIdx) || (l.Node1 == newIdx && l.Node2 == template));
            worst |= Assert("re-read node 861", re.NodeCount == 861, $"got {re.NodeCount}");
            worst |= Assert($"re-read content[{newIdx}]", re.Nodes[newIdx].ContentIndex == content, $"got 0x{re.Nodes[newIdx].ContentIndex:X2}");
            if (!noLink)
                worst |= Assert("re-read link present", linked, "new node has no link back to template");

            if (worst != 0)
            {
                Console.WriteLine("VERDICT: FAIL - not writing output.");
                return 1;
            }

            File.WriteAllBytes(outLayout, built.RawLayoutBytes);
            File.WriteAllBytes(outContents, built.RawContentsBytes);
            Console.WriteLine($"wrote: {outLayout} ({built.RawLayoutBytes.Length}B) + {outContents} ({built.RawContentsBytes.Length}B)");
            Console.WriteLine($"VERDICT: PASS - node {newIdx} (content 0x{content:X2}) {(noLink ? "UNLINKED (diag)" : $"linked to {template}")}; 861/{expectLinks}, magic intact, re-read OK.");
            return 0;
        }

        static int ResolveContent(int? explicitContent, string? name, string? panelJp, string? panelUs)
        {
            if (explicitContent is int c)
            {
                // 0xFF = the empty-slot sentinel (a valid, intentional "no content" node).
                if (c < 0 || c > 0xFF)
                {
                    Console.WriteLine($"FAIL: --content 0x{c:X2} out of [0,0xFF].");
                    return -1;
                }
                return c;
            }

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(panelJp))
            {
                Console.WriteLine("FAIL: provide --content <byte> OR --content-name <name> with --panel-jp <panel.bin>.");
                return -1;
            }

            SphereGridNodeTypeTable types = SphereGrid_File.ReadNodeTypes(panelJp, panelUs);
            string target = name.Trim();
            SphereGridNodeTypeEntry? hit = types.Entries.FirstOrDefault(e =>
                string.Equals(e.Name.UsText?.Trim(), target, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(e.PreferredName?.Trim(), target, StringComparison.OrdinalIgnoreCase));
            if (hit == null)
            {
                Console.WriteLine($"FAIL: node-type \"{target}\" not found in panel.bin. Sample HP/STR entries:");
                foreach (SphereGridNodeTypeEntry e in types.Entries
                             .Where(e => !string.IsNullOrWhiteSpace(e.Name.UsText))
                             .Take(20))
                    Console.WriteLine($"   0x{e.Index:X2}  {e.Name.UsText}  (effect=0x{e.NodeEffectBitfield:X4} amt={e.IncreaseAmount})");
                return -1;
            }
            Console.WriteLine($"resolved \"{target}\" -> content 0x{hit.Index:X2} (effect=0x{hit.NodeEffectBitfield:X4} amt={hit.IncreaseAmount})");
            return hit.Index;
        }

        static int ResolveTemplate(SphereGridLayoutFile grid, int? nearIndex, int content)
        {
            if (nearIndex is int n)
            {
                if (n < 0 || n >= grid.NodeCount)
                {
                    Console.WriteLine($"FAIL: --near {n} out of [0,{grid.NodeCount - 1}].");
                    return -1;
                }
                return n;
            }
            for (int i = 0; i < grid.Nodes.Count; i++)
                if (grid.Nodes[i].ContentIndex == content)
                    return i;
            return -1;
        }

        static int Assert(string label, bool ok, string detail)
        {
            Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {label}" + (ok ? "" : $" -> {detail}"));
            return ok ? 0 : 1;
        }

        static int ParseInt(string s)
        {
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return Convert.ToInt32(s[2..], 16);
            return int.Parse(s);
        }
    }
}
