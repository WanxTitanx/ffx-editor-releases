using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using FFXProjectEditor.FfxLib.SphereGrid;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.Core;

/// <summary>
/// Repro do fluxo de adicionar nó (True New Node clean): carrega o Standard vanilla real do jogo,
/// adiciona um nó com AddNodeNear (como o canvas faz) + um link, re-serializa e inspeciona o record.
/// Também inspeciona o mapeamento nome → content index do panel.bin do jogo (o "LUCK").
/// </summary>
public class SphereGridAddNodeReproTests
{
    private static string Abmap => Path.Combine(TestDataPaths.MasterRoot, "jppc", "menu", "abmap");
    private static string KernelJp => Path.Combine(TestDataPaths.MasterRoot, "jppc", "battle", "kernel");
    private static string KernelUs => Path.Combine(TestDataPaths.MasterRoot, "new_uspc", "battle", "kernel");

    [Fact]
    public void AddNodeNear_OnVanillaStandard_ProducesSaneRecord()
    {
        string layoutPath = Path.Combine(Abmap, "dat02.dat");
        string contentsPath = Path.Combine(Abmap, "dat10.dat");
        Assert.True(File.Exists(layoutPath), $"missing {layoutPath}");
        Assert.True(File.Exists(contentsPath), $"missing {contentsPath}");
        AssertVanillaStandardCorpus(layoutPath, contentsPath);

        SphereGridLayoutFile grid = SphereGrid_File.ReadLayout(layoutPath, contentsPath, "StandardRepro");
        var builder = SphereGridLayoutBuilder.FromExisting(grid);
        Assert.Equal(860, builder.NodeCount);
        Assert.Equal(881, builder.LinkCount);

        // Simula o clique do canvas: posição world (-1, 0) — sem snap — e content LUCK (0x16).
        int idx = builder.AddNodeNear(-1, 0, 0x16);
        Assert.Equal(860, idx);
        builder.AddLink(860, 639);

        SphereGridLayoutFile rebuilt = builder.Build();
        byte[] bytes = SphereGrid_File.WriteLayout(rebuilt);

        // Header: ClusterCount/NodeCount/LinkCount
        Assert.Equal((ushort)98, BitConverter.ToUInt16(bytes, 2));
        Assert.Equal((ushort)861, BitConverter.ToUInt16(bytes, 4));
        Assert.Equal((ushort)882, BitConverter.ToUInt16(bytes, 6));

        // Acha o record do nó 860 (o adicionado)
        int clusterBase = 0x10;
        int nodeBase = clusterBase + 98 * 0x10;
        int off = nodeBase + 860 * 0x0C;
        short posX = unchecked((short)BitConverter.ToUInt16(bytes, off + 0x00));
        short posY = unchecked((short)BitConverter.ToUInt16(bytes, off + 0x02));
        ushort unused3 = BitConverter.ToUInt16(bytes, off + 0x04);
        ushort redundant = BitConverter.ToUInt16(bytes, off + 0x06);
        ushort cluster = BitConverter.ToUInt16(bytes, off + 0x08);
        ushort unknown6 = BitConverter.ToUInt16(bytes, off + 0x0A);

        // O record deve ter: PosX/PosY do clique; Unused3 herdado (0 no vanilla); Redundant=content; Cluster válido (0..97); Unknown6 = bucket.
        Assert.Equal((short)-1, posX);
        Assert.Equal((short)0, posY);
        Assert.Equal((ushort)0, unused3);
        Assert.Equal((ushort)0x16, redundant);   // RedundantContentFor(LUCK)
        Assert.InRange(cluster, 0, 97);          // cluster herdado do template (válido)
        Assert.Equal((ushort)189, unknown6);     // ComputeUnknown6(-1, 0) = 9 + 20*9
    }

    [Fact]
    public void PanelOfTheGame_MapsLuckToContentIndex()
    {
        string jp = Path.Combine(KernelJp, "panel.bin");
        string us = Path.Combine(KernelUs, "panel.bin");
        Assert.True(File.Exists(jp), $"missing {jp}");
        Assert.True(File.Exists(us), $"missing {us}");

        SphereGridNodeTypeTable table = SphereGrid_File.ReadNodeTypes(jp, us);
        Assert.True(table.Entries.Count > 100, $"panel entries {table.Entries.Count}");

        var luck = table.Entries.FirstOrDefault(e =>
            !string.IsNullOrWhiteSpace(e.Name.UsText) &&
            e.Name.UsText.Contains("Luck", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(luck);
        Assert.Equal(0x16, luck.Index); // LUCK_1 = 0x16 no enum NodeType (ordem do painel == enum)
    }

    [Fact]
    public void RemoveNode_WithAnchorLinks_KeepsGraphValid()
    {
        // Conversa Discord fahrenheit-crew (2026-08-01): "anchor idx corresponde a um node específico e o link
        // tenta curvar ao redor dele; mudei o anchor idx e quebrou o nó". O RemoveNode reindexa nós E ajusta
        // anchors: link que usava o nó removido como curva vira reta (0xFFFF); anchors acima decrescem.
        string layoutPath = Path.Combine(Abmap, "dat02.dat");
        string contentsPath = Path.Combine(Abmap, "dat10.dat");
        Assert.True(File.Exists(layoutPath), $"missing {layoutPath}");
        Assert.True(File.Exists(contentsPath), $"missing {contentsPath}");
        AssertVanillaStandardCorpus(layoutPath, contentsPath);
        SphereGridLayoutFile grid = SphereGrid_File.ReadLayout(layoutPath, contentsPath, "StandardAnchor");
        Assert.True(grid.Links.Any(l => l.AnchorNode != 0xFFFF), "vanilla Standard tem links curvos (anchor)");

        int anchorIdx = grid.Links.First(l => l.AnchorNode != 0xFFFF).AnchorNode;
        int curvedBefore = grid.Links.Count(l =>
            l.AnchorNode != 0xFFFF && l.AnchorNode != anchorIdx && l.Node1 != anchorIdx && l.Node2 != anchorIdx);

        var builder = SphereGridLayoutBuilder.FromExisting(grid);
        builder.RemoveNode(anchorIdx);

        Assert.Equal(grid.Nodes.Count - 1, builder.NodeCount);
        SphereGridBuildValidation v = builder.Validate();
        Assert.True(v.IsValid, "remover o nó âncora deve manter o grid válido: " + v.Summary);
        Assert.DoesNotContain(builder.Links, l => l.AnchorNode != 0xFFFF && l.AnchorNode >= builder.NodeCount);
        int curvedAfter = builder.Links.Count(l => l.AnchorNode != 0xFFFF);
        Assert.Equal(curvedBefore, curvedAfter);

        // round-trip byte-safe continua válido após a remoção com anchors
        byte[] bytes = SphereGrid_File.WriteLayout(builder.Build());
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public void Validate_CatchesOutOfRangeAnchor()
    {
        // Mesma conversa: um anchor idx apontando para nó inexistente "quebra o nó" — o Validate precisa pegar.
        var builder = new SphereGridLayoutBuilder();
        builder.AddCluster(0, 0);
        builder.AddNode(0, 0, 0);
        builder.AddNode(43, 0, 0);
        builder.AddLink(0, 1, anchorNode: 5); // anchor inexistente
        SphereGridBuildValidation v = builder.Validate();
        Assert.False(v.IsValid);
        Assert.Contains(v.Errors, e => e.Contains("anchor", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void VanillaGate_RejectsSameShapeModifiedCorpus()
    {
        string sourceLayout = Path.Combine(Abmap, "dat02.dat");
        string sourceContents = Path.Combine(Abmap, "dat10.dat");
        Assert.True(File.Exists(sourceLayout), $"missing {sourceLayout}");
        Assert.True(File.Exists(sourceContents), $"missing {sourceContents}");

        string tempRoot = Path.Combine(Path.GetTempPath(), $"ffx-sphere-corpus-{Guid.NewGuid():N}");
        string layoutPath = Path.Combine(tempRoot, "dat02.dat");
        string contentsPath = Path.Combine(tempRoot, "dat10.dat");
        Directory.CreateDirectory(tempRoot);
        try
        {
            byte[] layout = File.ReadAllBytes(sourceLayout);
            byte[] contents = File.ReadAllBytes(sourceContents);

            layout[^1] ^= 0x01;
            File.WriteAllBytes(layoutPath, layout);
            File.WriteAllBytes(contentsPath, contents);
            Exception layoutFailure = Assert.ThrowsAny<Exception>(
                () => AssertVanillaStandardCorpus(layoutPath, contentsPath));
            Assert.Contains("dat02 SHA-256", layoutFailure.Message, StringComparison.Ordinal);

            layout[^1] ^= 0x01;
            contents[^1] ^= 0x01;
            File.WriteAllBytes(layoutPath, layout);
            File.WriteAllBytes(contentsPath, contents);
            Exception contentsFailure = Assert.ThrowsAny<Exception>(
                () => AssertVanillaStandardCorpus(layoutPath, contentsPath));
            Assert.Contains("dat10 SHA-256", contentsFailure.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static void AssertVanillaStandardCorpus(string layoutPath, string contentsPath)
    {
        const string expectedLayoutSha256 = "1303519cfc2dc4a6eb3975392ea2639f147272d816dcceea877e4157d77ba990";
        const string expectedContentsSha256 = "542ce37678fe4ec97d7c3ffd2403061de7ade1bc5a4b6b2de2a7cfdf90745206";
        byte[] layout = File.ReadAllBytes(layoutPath);
        byte[] contents = File.ReadAllBytes(contentsPath);
        string layoutSha256 = Convert.ToHexString(SHA256.HashData(layout));
        string contentsSha256 = Convert.ToHexString(SHA256.HashData(contents));
        string header = layout.Length >= 8
            ? $"clusters={BitConverter.ToUInt16(layout, 2)}, nodes={BitConverter.ToUInt16(layout, 4)}, links={BitConverter.ToUInt16(layout, 6)}"
            : $"<truncated:{layout.Length}-bytes>";
        string diagnostic =
            $"dat02='{layoutPath}', size={layout.Length}, header=[{header}], sha256={layoutSha256}; " +
            $"dat10='{contentsPath}', size={contents.Length}, sha256={contentsSha256}.";

        Assert.True(layout.Length == 18_952, $"Expected vanilla Standard dat02 size 18952. {diagnostic}");
        Assert.True(contents.Length == 868, $"Expected vanilla Standard dat10 size 868. {diagnostic}");
        Assert.True(layout.Length >= 8, $"Vanilla Standard dat02 header is truncated. {diagnostic}");
        Assert.True(BitConverter.ToUInt16(layout, 2) == 98, $"Expected vanilla Standard cluster count 98. {diagnostic}");
        Assert.True(BitConverter.ToUInt16(layout, 4) == 860, $"Expected vanilla Standard node count 860. {diagnostic}");
        Assert.True(BitConverter.ToUInt16(layout, 6) == 881, $"Expected vanilla Standard link count 881. {diagnostic}");
        Assert.True(
            string.Equals(layoutSha256, expectedLayoutSha256, StringComparison.OrdinalIgnoreCase),
            $"Expected vanilla Standard dat02 SHA-256 {expectedLayoutSha256}. {diagnostic}");
        Assert.True(
            string.Equals(contentsSha256, expectedContentsSha256, StringComparison.OrdinalIgnoreCase),
            $"Expected vanilla Standard dat10 SHA-256 {expectedContentsSha256}. {diagnostic}");
    }
}
