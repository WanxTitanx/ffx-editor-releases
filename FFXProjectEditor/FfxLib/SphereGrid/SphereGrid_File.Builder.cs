using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.SphereGrid
{
    // FROM-SCRATCH authoring layer for the sphere-grid LAYOUT (abmap dat01/02/03).
    //
    // The byte-faithful serializer (SphereGrid_File.WriteLayout) is proven by --spheregrid-layout-rt0
    // (read->write == original on all 3 grids). This builder is the AUTHORING half: assemble a brand-new
    // SphereGridLayoutFile in memory (AddCluster/AddNode/AddLink), Validate() it (range-check every index so
    // a dangling link can't silently produce a game-crashing file), and Build() it into a serializable model.
    // "Create a sphere grid from scratch" = new SphereGridLayoutBuilder(); add...; var grid = b.Build();
    public sealed class SphereGridLayoutBuilder
    {
        // Header words observed CONSTANT across all shipped grids (jppc+uspc dat01/02/03), per the layout RE.
        public const ushort HeaderUnknown1 = 0x0031;
        public const ushort HeaderUnknown5 = 0x0000;
        public const ushort HeaderUnknown6 = 0x0000;
        public const ushort HeaderUnknown7 = 0x0003;  // version/magic-ish
        public const ushort HeaderUnknown8 = 0x5000;  // version/magic-ish
        public const byte EmptyContent = 0xFF;        // node with no content sphere

        private readonly List<SphereGridClusterEntry> _clusters = new();
        private readonly List<SphereGridNodeEntry> _nodes = new();
        private readonly List<SphereGridLinkEntry> _links = new();

        public string DisplayName { get; set; } = "New Sphere Grid";

        // Header words at file offsets 0x00 / 0x08 / 0x0A / 0x0C / 0x0E. Default to the values observed
        // CONSTANT across every shipped grid (from-scratch authoring). FromExisting overrides them with the
        // loaded grid's real words so an EDIT round-trip preserves the header verbatim. The count words at
        // 0x02/0x04/0x06 are never stored here — Build always recomputes them from the list lengths.
        private ushort _headerWord1 = HeaderUnknown1;
        private ushort _headerWord5 = HeaderUnknown5;
        private ushort _headerWord6 = HeaderUnknown6;
        private ushort _headerWord7 = HeaderUnknown7;
        private ushort _headerWord8 = HeaderUnknown8;

        // When seeded from an existing grid, the original contents-file bytes (dat09/10/11) are kept so a
        // no-edit FromExisting -> Build re-emits the contents file byte-for-byte and a content edit re-stamps
        // only the affected byte. Null for from-scratch (Build synthesizes an 8-byte header + one byte/node).
        private byte[]? _contentsTemplate;
        private int _seededClusterCount;
        private int _seededNodeCount;
        private int _seededLinkCount;

        public int ClusterCount => _clusters.Count;
        public int NodeCount => _nodes.Count;
        public int LinkCount => _links.Count;
        public bool IsSeededFromExisting => _contentsTemplate != null;
        public int SeededClusterCount => _seededClusterCount;
        public int SeededNodeCount => _seededNodeCount;
        public int SeededLinkCount => _seededLinkCount;
        public int AddedClusterCount => IsSeededFromExisting ? Math.Max(0, _clusters.Count - _seededClusterCount) : 0;
        public int AddedNodeCount => IsSeededFromExisting ? Math.Max(0, _nodes.Count - _seededNodeCount) : 0;
        public int AddedLinkCount => IsSeededFromExisting ? Math.Max(0, _links.Count - _seededLinkCount) : 0;
        public bool HasRuntimeUnprovenTopologyCountChange => IsSeededFromExisting &&
            (_clusters.Count != _seededClusterCount || _nodes.Count != _seededNodeCount || _links.Count != _seededLinkCount);
        public bool HasRuntimeUnprovenTopologyAppend => AddedClusterCount > 0 || AddedNodeCount > 0 || AddedLinkCount > 0;

        /// <summary>
        /// Opt-in override for a CLEAN no-hook runtime test (RT2): permits SaveToProject/SaveSquareToProject
        /// with changed counts. Default OFF. Only meaningful with a disposable save; the engine is
        /// header-driven and the static LpAbilityMapEngine holds 1024/1024/128 — see SphereGridDeployPolicy.
        /// </summary>
        public bool AllowRuntimeTestDeploy { get; set; }

        // Live read-only views of the authored tables (entries are init-only, so callers mutate through the
        // Add*/Update*/Move*/Set*/Remove* methods). Handy for a canvas UI that renders current builder state.
        public IReadOnlyList<SphereGridClusterEntry> Clusters => _clusters;
        public IReadOnlyList<SphereGridNodeEntry> Nodes => _nodes;
        public IReadOnlyList<SphereGridLinkEntry> Links => _links;

        /// <summary>
        /// Seed a builder FROM an already-read grid so it can be EDITED and re-serialized byte-faithfully.
        /// Every field of every cluster/node/link is cloned verbatim (NOT funneled through the lossy Add*,
        /// which would zero Unused*/Unknown6 and recompute RedundantContent), and the real header words +
        /// contents bytes are preserved. So a no-edit FromExisting -> Build -> WriteLayout is byte-identical to
        /// the original, and a single mutation changes only that field's bytes (proven by --spheregrid-edit-rt0).
        /// </summary>
        public static SphereGridLayoutBuilder FromExisting(SphereGridLayoutFile grid)
        {
            ArgumentNullException.ThrowIfNull(grid);
            SphereGridLayoutBuilder b = new()
            {
                DisplayName = grid.DisplayName,
                _headerWord1 = grid.Unknown1,
                _headerWord5 = grid.Unknown5,
                _headerWord6 = grid.Unknown6,
                _headerWord7 = grid.Unknown7,
                _headerWord8 = grid.Unknown8,
                _contentsTemplate = grid.RawContentsBytes?.ToArray(),
                _seededClusterCount = grid.Clusters.Count,
                _seededNodeCount = grid.Nodes.Count,
                _seededLinkCount = grid.Links.Count,
            };
            foreach (SphereGridClusterEntry c in grid.Clusters) b._clusters.Add(Clone(c, b._clusters.Count));
            foreach (SphereGridNodeEntry n in grid.Nodes) b._nodes.Add(Clone(n, b._nodes.Count));
            foreach (SphereGridLinkEntry l in grid.Links) b._links.Add(Clone(l, b._links.Count));
            return b;
        }

        /// <summary>Append a cluster (a circle/region). Returns its index.</summary>
        public int AddCluster(short posX, short posY, ushort radiusType = 0)
        {
            int i = _clusters.Count;
            _clusters.Add(new SphereGridClusterEntry
            {
                Index = i, PosX = posX, PosY = posY, Unused3 = 0, RadiusType = radiusType,
                Unused5 = 0, Unused6 = 0, Unused7 = 0, Unused8 = 0,
            });
            return i;
        }

        /// <summary>Append a node (a sphere slot). Returns its index. contentIndex=0xFF for an empty slot.</summary>
        public int AddNode(short posX, short posY, ushort cluster, int contentIndex = EmptyContent, ushort unknown6 = 0)
        {
            int i = _nodes.Count;
            _nodes.Add(new SphereGridNodeEntry
            {
                Index = i, PosX = posX, PosY = posY, Unused3 = 0,
                RedundantContent = RedundantContentFor(contentIndex), Cluster = cluster,
                Unknown6 = unknown6 != 0 ? unknown6 : ComputeUnknown6(posX, posY),
                ContentIndex = contentIndex,
            });
            return i;
        }

        /// <summary>
        /// Append a node that inherits non-positional metadata from an existing node.
        /// <c>Unknown6</c> is not inherited: it is the grid-cell bucket computed from the new position.
        /// </summary>
        public int AddNodeLike(int templateNodeIndex, short posX, short posY, int contentIndex = EmptyContent)
        {
            RequireNode(templateNodeIndex);
            SphereGridNodeEntry template = _nodes[templateNodeIndex];
            int i = _nodes.Count;
            _nodes.Add(new SphereGridNodeEntry
            {
                Index = i,
                PosX = posX,
                PosY = posY,
                Unused3 = template.Unused3,
                RedundantContent = RedundantContentFor(contentIndex),
                Cluster = template.Cluster,
                Unknown6 = ComputeUnknown6(posX, posY),
                ContentIndex = contentIndex,
            });
            return i;
        }

        /// <summary>
        /// Append a node near the closest existing node, inheriting cluster/unused metadata and computing its cell bucket.
        /// Falls back to <see cref="AddNode"/> only for a truly from-scratch empty builder.
        /// </summary>
        public int AddNodeNear(short posX, short posY, int contentIndex = EmptyContent)
        {
            if (_nodes.Count == 0)
            {
                if (_clusters.Count == 0)
                    AddCluster(posX, posY);
                return AddNode(posX, posY, 0, contentIndex);
            }

            int nearest = FindNearestNode(posX, posY);
            return AddNodeLike(nearest, posX, posY, contentIndex);
        }

        /// <summary>Append a link between node1 and node2; anchorNode = 0xFFFF for a straight link, else a curve control node.</summary>
        public int AddLink(ushort node1, ushort node2, ushort anchorNode = 0xFFFF)
        {
            int i = _links.Count;
            _links.Add(new SphereGridLinkEntry { Index = i, Node1 = node1, Node2 = node2, AnchorNode = anchorNode, Unused = 0 });
            return i;
        }

        /// <summary>Replace a node's position, cluster and content in one call (other fields preserved).</summary>
        public void UpdateNode(int index, short posX, short posY, ushort cluster, int contentIndex)
        {
            RequireNode(index);
            _nodes[index] = NodeWith(_nodes[index], posX, posY, cluster, contentIndex);
        }

        /// <summary>Move a node to a new position (cluster/content/other fields preserved). Drives canvas drag.</summary>
        public void MoveNode(int index, short posX, short posY)
        {
            RequireNode(index);
            SphereGridNodeEntry n = _nodes[index];
            _nodes[index] = NodeWith(n, posX, posY, n.Cluster, n.ContentIndex);
        }

        /// <summary>Set a node's content sphere (0xFF = empty). Also re-stamps the layout's redundant copy,
        /// which the shipped grids keep equal to the contents byte (verified 828/828 on the Original grid).</summary>
        public void SetNodeContent(int index, int contentIndex)
        {
            RequireNode(index);
            SphereGridNodeEntry n = _nodes[index];
            _nodes[index] = NodeWith(n, n.PosX, n.PosY, n.Cluster, contentIndex);
        }

        /// <summary>Reassign a node to another cluster (position/content/other fields preserved).</summary>
        public void SetNodeCluster(int index, ushort cluster)
        {
            RequireNode(index);
            SphereGridNodeEntry n = _nodes[index];
            _nodes[index] = NodeWith(n, n.PosX, n.PosY, cluster, n.ContentIndex);
        }

        /// <summary>Update a cluster's position and radius/design type (unused fields preserved).</summary>
        public void UpdateCluster(int index, short posX, short posY, ushort radiusType)
        {
            RequireCluster(index);
            SphereGridClusterEntry c = _clusters[index];
            _clusters[index] = new SphereGridClusterEntry
            {
                Index = c.Index, PosX = posX, PosY = posY, Unused3 = c.Unused3, RadiusType = radiusType,
                Unused5 = c.Unused5, Unused6 = c.Unused6, Unused7 = c.Unused7, Unused8 = c.Unused8,
            };
        }

        /// <summary>Move a cluster to a new position (radius/other fields preserved).</summary>
        public void MoveCluster(int index, short posX, short posY)
        {
            RequireCluster(index);
            UpdateCluster(index, posX, posY, _clusters[index].RadiusType);
        }

        /// <summary>Repoint or straighten an existing link (anchorNode 0xFFFF = straight, else a curve node).</summary>
        public void UpdateLink(int index, ushort node1, ushort node2, ushort anchorNode = 0xFFFF)
        {
            RequireLink(index);
            _links[index] = new SphereGridLinkEntry { Index = index, Node1 = node1, Node2 = node2, AnchorNode = anchorNode, Unused = _links[index].Unused };
        }

        /// <summary>Remove the link at the given index (later links renumber down by one).</summary>
        public void RemoveLink(int index)
        {
            RequireLink(index);
            _links.RemoveAt(index);
            ReindexLinks();
        }

        /// <summary>
        /// Remove a node and keep the graph consistent: links incident to it are dropped, links that merely use
        /// it as a curve anchor become straight, and every node/link index above it renumbers down by one so the
        /// result stays a valid grid (Validate green). Cluster references are unaffected.
        /// </summary>
        public void RemoveNode(int index)
        {
            RequireNode(index);
            _nodes.RemoveAt(index);

            List<SphereGridLinkEntry> kept = new(_links.Count);
            foreach (SphereGridLinkEntry l in _links)
            {
                if (l.Node1 == index || l.Node2 == index)
                    continue; // a link to the removed node cannot survive
                ushort n1 = l.Node1 > index ? (ushort)(l.Node1 - 1) : l.Node1;
                ushort n2 = l.Node2 > index ? (ushort)(l.Node2 - 1) : l.Node2;
                ushort anc = l.AnchorNode;
                if (anc != 0xFFFF) anc = anc == index ? (ushort)0xFFFF : (anc > index ? (ushort)(anc - 1) : anc);
                kept.Add(new SphereGridLinkEntry { Index = 0, Node1 = n1, Node2 = n2, AnchorNode = anc, Unused = l.Unused });
            }
            _links.Clear();
            _links.AddRange(kept);
            ReindexLinks();
            ReindexNodes();
        }

        /// <summary>
        /// Remove an EMPTY cluster (no node assigned to it) and renumber node cluster references above it down by
        /// one. Throws if any node still belongs to the cluster — reassign those nodes first (SetNodeCluster).
        /// </summary>
        public void RemoveCluster(int index)
        {
            RequireCluster(index);
            for (int i = 0; i < _nodes.Count; i++)
                if (_nodes[i].Cluster == index)
                    throw new InvalidOperationException($"cluster {index} still has node {i}; reassign its nodes before removing it.");

            _clusters.RemoveAt(index);
            for (int i = 0; i < _nodes.Count; i++)
            {
                SphereGridNodeEntry n = _nodes[i];
                if (n.Cluster > index)
                    _nodes[i] = NodeWith(n, n.PosX, n.PosY, (ushort)(n.Cluster - 1), n.ContentIndex);
            }
            ReindexClusters();
        }

        private void RequireNode(int i) { if (i < 0 || i >= _nodes.Count) throw new ArgumentOutOfRangeException(nameof(i), $"node index {i} out of [0,{_nodes.Count - 1}]."); }
        private void RequireCluster(int i) { if (i < 0 || i >= _clusters.Count) throw new ArgumentOutOfRangeException(nameof(i), $"cluster index {i} out of [0,{_clusters.Count - 1}]."); }
        private void RequireLink(int i) { if (i < 0 || i >= _links.Count) throw new ArgumentOutOfRangeException(nameof(i), $"link index {i} out of [0,{_links.Count - 1}]."); }

        private int FindNearestNode(short posX, short posY)
        {
            if (_nodes.Count == 0)
                throw new InvalidOperationException("Cannot find nearest node in an empty grid.");

            int best = 0;
            long bestD2 = long.MaxValue;
            for (int i = 0; i < _nodes.Count; i++)
            {
                long dx = _nodes[i].PosX - (long)posX;
                long dy = _nodes[i].PosY - (long)posY;
                long d2 = dx * dx + dy * dy;
                if (d2 < bestD2)
                {
                    best = i;
                    bestD2 = d2;
                }
            }
            return best;
        }

        private void ReindexNodes() { for (int i = 0; i < _nodes.Count; i++) _nodes[i] = Clone(_nodes[i], i); }
        private void ReindexLinks() { for (int i = 0; i < _links.Count; i++) _links[i] = Clone(_links[i], i); }
        private void ReindexClusters() { for (int i = 0; i < _clusters.Count; i++) _clusters[i] = Clone(_clusters[i], i); }

        // Replace a node's editable fields, keeping Unused3 verbatim. Unknown6 is the coordinate-derived grid-cell
        // bucket, so moving a node must recompute it. RedundantContent (the layout-side copy of the content byte)
        // is re-stamped to match contentIndex via RedundantContentFor, mirroring the shipped convention.
        private static SphereGridNodeEntry NodeWith(SphereGridNodeEntry n, short posX, short posY, ushort cluster, int contentIndex) => new()
        {
            Index = n.Index, PosX = posX, PosY = posY, Unused3 = n.Unused3,
            RedundantContent = RedundantContentFor(contentIndex), Cluster = cluster, Unknown6 = ComputeUnknown6(posX, posY),
            ContentIndex = contentIndex,
        };

        // The layout-side redundant copy of the content byte (node +0x06) mirrors the shipped grids: a FILLED node
        // stores 0x00<content>, an EMPTY node (content 0xFF) stores 0xFFFF (high byte set), NOT 0x00FF. Proven by the
        // AURON scout (low byte == ContentIndex in 3.444/3.444 nodes; high byte set only on the 23 empty Expert nodes
        // => empty == 0xFFFF). FromExisting/Clone still copies RedundantContent verbatim, so a no-edit round-trip is
        // byte-identical; this only governs the AUTHORING/EDIT path (AddNode / NodeWith) so a node CLEARED to empty
        // matches shipped data instead of the old 0x00FF. Asserted by --spheregrid-edit-rt0.
        private static ushort RedundantContentFor(int contentIndex) =>
            (contentIndex & 0xFF) == EmptyContent ? (ushort)0xFFFF : (ushort)(contentIndex & 0xFF);

        // Node.Unknown6 (layout node +0x0A) is the ABMAP spatial cell id, regenerated by the menu loader as:
        //   ((PosX + 2560) / 256) + 20 * ((PosY + 2336) / 256)
        // Vanilla dat01/02/03 match this formula for every shipped node. C# integer division matches the PC
        // runtime for the coordinate range used by the shipped grids.
        public static ushort ComputeUnknown6(short posX, short posY)
        {
            int cell = ((int)posX + 2560) / 256 + 20 * (((int)posY + 2336) / 256);
            return unchecked((ushort)cell);
        }

        /// <summary>Range-check every index so a built grid can never reference a non-existent node/cluster.</summary>
        public SphereGridBuildValidation Validate()
        {
            List<string> errors = new();
            int nc = _nodes.Count, cc = _clusters.Count;
            if (nc == 0) errors.Add("grid has 0 nodes.");
            for (int i = 0; i < _nodes.Count; i++)
            {
                SphereGridNodeEntry n = _nodes[i];
                if (n.Cluster >= cc) errors.Add($"node {i}: cluster {n.Cluster} >= clusterCount {cc}.");
                if (n.ContentIndex != EmptyContent && (n.ContentIndex < 0 || n.ContentIndex > 0xFF))
                    errors.Add($"node {i}: contentIndex {n.ContentIndex} out of [0,255]/0xFF.");
                ushort expectedUnknown6 = ComputeUnknown6(n.PosX, n.PosY);
                if (n.Unknown6 != expectedUnknown6)
                    errors.Add($"node {i}: Unknown6 {n.Unknown6:X4} does not match coordinate bucket {expectedUnknown6:X4}.");
            }
            for (int i = 0; i < _links.Count; i++)
            {
                SphereGridLinkEntry l = _links[i];
                if (l.Node1 >= nc) errors.Add($"link {i}: node1 {l.Node1} >= nodeCount {nc}.");
                if (l.Node2 >= nc) errors.Add($"link {i}: node2 {l.Node2} >= nodeCount {nc}.");
                if (l.AnchorNode != 0xFFFF && l.AnchorNode >= nc) errors.Add($"link {i}: anchor {l.AnchorNode} >= nodeCount {nc}.");
            }
            return new SphereGridBuildValidation(errors);
        }

        /// <summary>
        /// Materialize the authored tables into a SphereGridLayoutFile (counts from list lengths; RawLayoutBytes
        /// produced by the proven WriteLayout, so the result is immediately round-trippable). Throws if invalid.
        /// </summary>
        public SphereGridLayoutFile Build()
        {
            SphereGridBuildValidation v = Validate();
            if (!v.IsValid)
                throw new InvalidOperationException("Cannot build an invalid sphere-grid layout: " + v.Summary);

            List<SphereGridClusterEntry> clusters = _clusters.Select((c, i) => Clone(c, i)).ToList();
            List<SphereGridNodeEntry> nodes = _nodes.Select((n, i) => Clone(n, i)).ToList();
            List<SphereGridLinkEntry> links = _links.Select((l, i) => Clone(l, i)).ToList();

            // Wire the navigation lists exactly like ReadLayout, so a built grid exposes the same connectivity as a read one.
            foreach (SphereGridLinkEntry link in links)
            {
                if (link.Node1 < nodes.Count) { nodes[link.Node1].ConnectedNodeIndices.Add(link.Node2); nodes[link.Node1].ConnectedLinkIndices.Add(link.Index); }
                if (link.Node2 < nodes.Count) { nodes[link.Node2].ConnectedNodeIndices.Add(link.Node1); nodes[link.Node2].ConnectedLinkIndices.Add(link.Index); }
                if (link.AnchorNode < nodes.Count) { nodes[link.AnchorNode].AnchorLinkIndices.Add(link.Index); }
            }

            // contents payload = 8-byte header + one content byte per node (matches ReadLayout's contents[0x08..]).
            // When seeded from an existing grid, start from a clone of the ORIGINAL contents file so the header
            // (and any trailing bytes within length) survive verbatim; for from-scratch, synthesize the minimal
            // 8-byte header observed on the shipped grids. Either way the file is sized to exactly 8 + nodeCount
            // (the shipped layout), so a no-edit round-trip is byte-identical and add/remove-node stays tight.
            byte[] contents;
            if (_contentsTemplate != null)
            {
                contents = _contentsTemplate.ToArray();
            }
            else
            {
                contents = new byte[0x08 + nodes.Count];
                contents[0] = 0x31; contents[1] = 0x00; // observed header bytes; rest zero
            }
            Array.Resize(ref contents, 0x08 + nodes.Count);
            for (int i = 0; i < nodes.Count; i++)
                contents[0x08 + i] = unchecked((byte)nodes[i].ContentIndex);

            SphereGridLayoutFile model = MakeFile(clusters, nodes, links, contents, Array.Empty<byte>());
            byte[] layoutBytes = SphereGrid_File.WriteLayout(model);
            return MakeFile(clusters, nodes, links, contents, layoutBytes);
        }

        private SphereGridLayoutFile MakeFile(
            List<SphereGridClusterEntry> clusters, List<SphereGridNodeEntry> nodes,
            List<SphereGridLinkEntry> links, byte[] contents, byte[] layoutBytes) => new()
        {
            DisplayName = DisplayName,
            LayoutPath = string.Empty,
            ContentsPath = string.Empty,
            RawLayoutBytes = layoutBytes,
            RawContentsBytes = contents,
            FileSize = layoutBytes.Length,
            ContentsFileSize = contents.Length,
            Unknown1 = _headerWord1,
            ClusterCount = (ushort)clusters.Count,
            NodeCount = (ushort)nodes.Count,
            LinkCount = (ushort)links.Count,
            Unknown5 = _headerWord5,
            Unknown6 = _headerWord6,
            Unknown7 = _headerWord7,
            Unknown8 = _headerWord8,
            Clusters = clusters,
            Nodes = nodes,
            Links = links,
        };

        private static SphereGridClusterEntry Clone(SphereGridClusterEntry c, int i) => new()
        { Index = i, PosX = c.PosX, PosY = c.PosY, Unused3 = c.Unused3, RadiusType = c.RadiusType, Unused5 = c.Unused5, Unused6 = c.Unused6, Unused7 = c.Unused7, Unused8 = c.Unused8 };

        private static SphereGridNodeEntry Clone(SphereGridNodeEntry n, int i) => new()
        { Index = i, PosX = n.PosX, PosY = n.PosY, Unused3 = n.Unused3, RedundantContent = n.RedundantContent, Cluster = n.Cluster, Unknown6 = n.Unknown6, ContentIndex = n.ContentIndex };

        private static SphereGridLinkEntry Clone(SphereGridLinkEntry l, int i) => new()
        { Index = i, Node1 = l.Node1, Node2 = l.Node2, AnchorNode = l.AnchorNode, Unused = l.Unused };
    }

    public sealed class SphereGridBuildValidation
    {
        public SphereGridBuildValidation(IReadOnlyList<string> errors) { Errors = errors; }
        public IReadOnlyList<string> Errors { get; }
        public bool IsValid => Errors.Count == 0;
        public string Summary => IsValid ? "valid" : string.Join("; ", Errors);
    }
}
