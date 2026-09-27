using FFXProjectEditor.Resources;
using System;

namespace FFXProjectEditor.Modules.AuroraFieldExplorer
{
    /// <summary>
    /// Prefix / packedId category heuristics for Field Scout CHR instances.
    /// RE: FFX_Chr_ResourceTypeCharToNibble @ 0x829E00 — n=2 story, c=0 party, m=1 enemy, f=5 prop.
    /// </summary>
    internal static class AuroraFieldExplorer_ChrClassifier
    {
        public enum WalkLayer
        {
            Unknown,
            StoryNpc,
            Party,
            FieldEnemy,
            FieldProp,
            Summon,
            Weapon,
            Rig,
        }

        public static WalkLayer ClassifyLayer(string chrName, uint chrId)
        {
            if (!string.IsNullOrWhiteSpace(chrName) && chrName.Length >= 2 && char.IsLetter(chrName[0]))
            {
                return char.ToLowerInvariant(chrName[0]) switch
                {
                    'n' => WalkLayer.StoryNpc,
                    'c' => WalkLayer.Party,
                    'm' => WalkLayer.FieldEnemy,
                    'f' => WalkLayer.FieldProp,
                    's' => WalkLayer.Summon,
                    'w' => WalkLayer.Weapon,
                    'k' => WalkLayer.Rig,
                    _ => WalkLayer.Unknown,
                };
            }

            if (chrId == 0)
                return WalkLayer.Unknown;

            return (chrId >> 12) switch
            {
                2 => WalkLayer.StoryNpc,
                0 => WalkLayer.Party,
                1 => WalkLayer.FieldEnemy,
                5 => WalkLayer.FieldProp,
                3 => WalkLayer.Summon,
                4 => WalkLayer.Weapon,
                6 => WalkLayer.Rig,
                _ => WalkLayer.Unknown,
            };
        }

        public static string LayerToCategory(WalkLayer layer) => layer switch
        {
            WalkLayer.StoryNpc => "story_npc",
            WalkLayer.Party => "party",
            WalkLayer.FieldEnemy => "field_enemy",
            WalkLayer.FieldProp => "field_prop",
            WalkLayer.Summon => "summon",
            WalkLayer.Weapon => "weapon",
            WalkLayer.Rig => "rig",
            _ => "unknown",
        };

        public static string LayerDisplayPt(WalkLayer layer) => layer switch
        {
            WalkLayer.StoryNpc => string.Format(Strings.U_Au_ChrLayerNpcStory, "n###"),
            WalkLayer.Party => string.Format(Strings.U_Au_ChrLayerParty, "c###"),
            WalkLayer.FieldEnemy => string.Format(Strings.U_Au_ChrLayerFieldEnemy, "m###"),
            WalkLayer.FieldProp => string.Format(Strings.U_Au_ChrLayerPropObject, "f###"),
            WalkLayer.Summon => string.Format(Strings.U_Au_ChrLayerSummon, "s###"),
            WalkLayer.Weapon => string.Format(Strings.U_Au_ChrLayerWeapon, "w###"),
            WalkLayer.Rig => string.Format(Strings.U_Au_ChrLayerRig, "k###"),
            _ => Strings.U_Au_ChrLayerUnknown,
        };

        /// <summary>Default MapViewer layer visibility (field_prop on for loja/cabana proxies).</summary>
        public static bool DefaultVisible(WalkLayer layer) => layer switch
        {
            WalkLayer.StoryNpc => true,
            WalkLayer.Party => true,
            WalkLayer.FieldEnemy => false,
            WalkLayer.FieldProp => true,
            WalkLayer.Summon => false,
            WalkLayer.Weapon => false,
            WalkLayer.Rig => false,
            _ => false,
        };
    }
}
