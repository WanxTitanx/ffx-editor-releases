using System.Linq;
using FFXProjectEditor.FfxLib.SphereGrid;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.Core;

/// <summary>
/// Valida o guardrail game-compatible do Sphere Grid Layout (caps 860 nós / 1021·934 links / 5 por nó)
/// e a política de deploy que, agora, libera topologia alterada DENTRO dos caps testados (ZW + nossa RE).
/// Self-contained: não depende dos arquivos do jogo (usamos o builder from-scratch + FromExisting).
/// </summary>
public class SphereGridGameCompatibilityTests
{
    [Fact]
    public void Caps_AreAsTested()
    {
        Assert.Equal(860, SphereGridGameCompatibility.MaximumGameCompatibleNodes);
        Assert.Equal(1021, SphereGridGameCompatibility.MaximumGameCompatibleStandardLinks);
        Assert.Equal(934, SphereGridGameCompatibility.MaximumGameCompatibleExpertLinks);
        Assert.Equal(5, SphereGridGameCompatibility.MaximumUsableLinksPerNode);
        Assert.Equal(1021, SphereGridGameCompatibility.GetGameCompatibleLinkLimit(isExpert: false));
        Assert.Equal(934, SphereGridGameCompatibility.GetGameCompatibleLinkLimit(isExpert: true));
        Assert.True(SphereGridGameCompatibility.IsWithinNodeCap(860));
        Assert.False(SphereGridGameCompatibility.IsWithinNodeCap(861));
        Assert.True(SphereGridGameCompatibility.IsWithinLinkCap(1021, isExpert: false));
        Assert.False(SphereGridGameCompatibility.IsWithinLinkCap(1022, isExpert: false));
        Assert.True(SphereGridGameCompatibility.IsWithinLinkCap(934, isExpert: true));
        Assert.False(SphereGridGameCompatibility.IsWithinLinkCap(935, isExpert: true));
    }

    [Fact]
    public void LinkDegreeAndCapacity_CountUndirectedLinks()
    {
        // 6 nodes, 0..5; 3 independent links.
        SphereGridLinkEntry[] links =
        {
            MakeLink(0, 1),
            MakeLink(0, 2),
            MakeLink(2, 3),
        };
        Assert.Equal(2, SphereGridGameCompatibility.LinkDegree(links, 0));
        Assert.Equal(1, SphereGridGameCompatibility.LinkDegree(links, 1));
        Assert.Equal(2, SphereGridGameCompatibility.LinkDegree(links, 2));
        Assert.Equal(0, SphereGridGameCompatibility.LinkDegree(links, 5));

        // Node 2 already has 2 links; adding 3 more (→5) is OK, the 6th would break.
        Assert.True(SphereGridGameCompatibility.EndpointsHaveUsableCapacity(links, 2, 4));   // 2→3, 4→0
        Assert.True(SphereGridGameCompatibility.EndpointsHaveUsableCapacity(links.Concat(new[]
        {
            MakeLink(2, 4), MakeLink(2, 5),
        }).ToArray(), 2, 0)); // node 2 now has 4
        var five = links.Concat(new[] { MakeLink(2, 4), MakeLink(2, 5), MakeLink(2, 0) }).ToArray(); // node2 has 5
        Assert.False(SphereGridGameCompatibility.EndpointsHaveUsableCapacity(five, 2, 1));  // would make 6
    }

    [Fact]
    public void CapacityText_ShowsCaps()
    {
        using var language = TestUiCultureScope.English();
        Assert.Equal("Nodes: 860 / 860", SphereGridGameCompatibility.NodeCapacityText(860));
        Assert.Equal("Links: 1000 / 1021", SphereGridGameCompatibility.LinkCapacityText(1000, isExpert: false));
        Assert.Equal("Links: 900 / 934", SphereGridGameCompatibility.LinkCapacityText(900, isExpert: true));
    }

    [Fact]
    public void DeployPolicy_WithinCaps_AllowsTopologyChange_NoHook()
    {
        SphereGridLayoutBuilder seeded = MakeSeededBuilder(4, 3);
        Assert.True(seeded.IsSeededFromExisting);

        // Topology change (add a node+link) but STILL within game-compatible caps → deploy allowed, no hook.
        seeded.AddNode(200, 0, cluster: 0);
        seeded.AddLink((ushort)(seeded.NodeCount - 1), 0);
        Assert.Equal(5, seeded.NodeCount);
        Assert.Equal(4, seeded.LinkCount);
        Assert.True(SphereGridDeployPolicy.HasGameCompatibleCounts(seeded, isExpert: false));
        Assert.True(SphereGridDeployPolicy.CanDeployToProject(seeded, isExpert: false));
    }

    [Fact]
    public void DeployPolicy_SafeTransplantAndEscape()
    {
        // Unchanged seeded grid → Safe Transplant, deploy allowed.
        Assert.True(SphereGridDeployPolicy.CanDeployToProject(MakeSeededBuilder(4, 3), isExpert: false));

        // Explicit RT2 escape still overrides (lab/measurement beyond caps).
        SphereGridLayoutBuilder esc = MakeSeededBuilder(4, 3);
        esc.AllowRuntimeTestDeploy = true;
        Assert.True(SphereGridDeployPolicy.CanDeployToProject(esc, isExpert: false));
    }

    private static SphereGridLinkEntry MakeLink(int a, int b) => new()
    {
        Index = 0, Node1 = (ushort)a, Node2 = (ushort)b, AnchorNode = 0xFFFF, Unused = 0,
    };

    private static SphereGridLayoutBuilder MakeSeededBuilder(int nodeCount, int linkCount)
    {
        var b = new SphereGridLayoutBuilder();
        int c = b.AddCluster(0, 0, 0);
        for (int i = 0; i < nodeCount; i++)
            b.AddNode((short)(i * 50), 0, cluster: (ushort)c);
        for (int i = 0; i < linkCount; i++)
            b.AddLink((ushort)i, (ushort)((i + 1) % nodeCount));
        SphereGridLayoutFile grid = b.Build();
        return SphereGridLayoutBuilder.FromExisting(grid);
    }
}
