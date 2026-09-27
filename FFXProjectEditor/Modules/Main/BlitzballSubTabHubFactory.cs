using FFXProjectEditor.Resources;
using Avalonia.Controls;
using FFXProjectEditor;

namespace FFXProjectEditor.Modules.Main;

// Jarvis-UI (Sprint B 2026-06-20, OPT-B3): extrai a construção das 5 abas do Blitzball Hub num factory
// estático, pra o MenuItem_Blitzball do Main_Window virar ≤5 linhas e a definição das tabs (label/modo/pill)
// viver num lugar só. Pura apresentação/navegação — os 5 sub-editores continuam lazy-load e intocados.
//
// Tabs (espelham o que antes estava inline em Main_Window.MenuItem_Blitzball):
//   Roster / Recruits / Prize Pool / Prize Table = Writer LAB · Prize Atlas = READ-ONLY.
// requiresProject=false: o Atlas read-only funciona sem projeto e os 4 writers têm seus próprios empty states.
internal static class BlitzballSubTabHubFactory
{
    public static SubTabHub_Control Create()
    {
        var hub = new SubTabHub_Control();
        hub.AddTab("Roster", static () => new BlitzballRosterEditor_Control(), SubTabHub_Control.TabMode.Writer, "WRITER LAB · bltz0002.ebp · RT0 proven · RT2 pending",
                   a11yName: Strings.U_Bl_RosterA11y)
           .AddTab("Recruits", static () => new BlitzballRecruitEditor_Control(), SubTabHub_Control.TabMode.Writer, "WRITER LAB · recruit .ebp · RT0 proven · RT2 pending",
                   a11yName: Strings.U_Bl_RecruitsA11y)
           .AddTab("Prize Pool", static () => new BlitzballPrizesEditor_Control(), SubTabHub_Control.TabMode.Writer, "WRITER LAB · takara.bin · RT0 proven · RT2 pending",
                   a11yName: Strings.U_Bl_PrizePoolA11y)
           .AddTab("Prize Table", static () => new BlitzballPrizeStructEditor_Control(), SubTabHub_Control.TabMode.Writer, "WRITER LAB · bltz0200.ebp · RT0 proven · RT2 pending",
                   a11yName: Strings.U_Bl_PrizeTableA11y)
           .AddTab("Prize Atlas", static () => new BlitzballPrizeExplorer_Control(), SubTabHub_Control.TabMode.ReadOnly, "READ-ONLY · Spira Data Atlas · no game-file writes",
                   a11yName: Strings.U_Bl_PrizeAtlasA11y);
        return hub;
    }
}
