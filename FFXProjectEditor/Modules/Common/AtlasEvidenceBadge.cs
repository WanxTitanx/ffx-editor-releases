using FFXProjectEditor.Resources;
using Avalonia.Media;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.SpiraDataAtlas;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.Modules.Common
{
    // Read-only evidence badge for the editor modules. It wraps one canonical AiBibleBadge token (the same engine the
    // BIBLE OF SPIRA window uses) and maps its severity to the shared Spira/FFX color language. It carries no command,
    // no writer, no apply — it only shows where a piece of data came from and how far it can be trusted.
    public sealed class AtlasEvidenceBadge
    {
        static readonly IBrush ProofBg = new SolidColorBrush(Color.Parse("#16352B"));
        static readonly IBrush ProofFg = new SolidColorBrush(Color.Parse("#73E2AE"));
        static readonly IBrush ProofBorder = new SolidColorBrush(Color.Parse("#2E6B53"));
        static readonly IBrush CorpusBg = new SolidColorBrush(Color.Parse("#16293A"));
        static readonly IBrush CorpusFg = new SolidColorBrush(Color.Parse("#7EC8FF"));
        static readonly IBrush CorpusBorder = new SolidColorBrush(Color.Parse("#2C557A"));
        static readonly IBrush CautionBg = new SolidColorBrush(Color.Parse("#2B2116"));
        static readonly IBrush CautionFg = new SolidColorBrush(Color.Parse("#F4C26B"));
        static readonly IBrush CautionBorder = new SolidColorBrush(Color.Parse("#6D4E25"));
        static readonly IBrush BlockedBg = new SolidColorBrush(Color.Parse("#3A1E22"));
        static readonly IBrush BlockedFg = new SolidColorBrush(Color.Parse("#F2A0A8"));
        static readonly IBrush BlockedBorder = new SolidColorBrush(Color.Parse("#7A3540"));
        static readonly IBrush NeutralBg = new SolidColorBrush(Color.Parse("#172230"));
        static readonly IBrush NeutralFg = new SolidColorBrush(Color.Parse("#9EB0C2"));
        static readonly IBrush NeutralBorder = new SolidColorBrush(Color.Parse("#2A3A4A"));

        public AtlasEvidenceBadge(AiBibleBadge badge)
        {
            Text = badge.Token;
            Tooltip = badge.Tooltip;
            (Background, Foreground, BorderBrush) = badge.Severity switch
            {
                AiBibleBadgeSeverity.Proof => (ProofBg, ProofFg, ProofBorder),
                AiBibleBadgeSeverity.Corpus => (CorpusBg, CorpusFg, CorpusBorder),
                AiBibleBadgeSeverity.Caution => (CautionBg, CautionFg, CautionBorder),
                AiBibleBadgeSeverity.Blocked => (BlockedBg, BlockedFg, BlockedBorder),
                _ => (NeutralBg, NeutralFg, NeutralBorder),
            };
        }

        public string Text { get; }
        public string Tooltip { get; }
        public IBrush Background { get; }
        public IBrush Foreground { get; }
        public IBrush BorderBrush { get; }
    }

    // Bundle consumed by AtlasEvidenceBadgeStrip: the badge list, a short honest origin line, and an optional
    // "where it appears in the game" expander text. Built from a BIBLE entry (AI/ATEL context) or straight from a
    // Spira Data Atlas detail row. Both paths go through the SAME AiBibleEvidence engine, so the inline strip and the
    // full BIBLE window always agree. Returns null when there is nothing to show, so modules can hide the strip.
    public sealed class AtlasEvidenceInfo
    {
        AtlasEvidenceInfo(IReadOnlyList<AtlasEvidenceBadge> badges, string originText, string whereText)
        {
            Badges = badges;
            OriginText = originText;
            WhereText = whereText;
        }

        public IReadOnlyList<AtlasEvidenceBadge> Badges { get; }
        public string OriginText { get; }
        public string WhereText { get; }
        public bool HasBadges => Badges.Count > 0;
        public bool HasOrigin => !string.IsNullOrWhiteSpace(OriginText);
        public bool HasWhere => !string.IsNullOrWhiteSpace(WhereText);

        public static AtlasEvidenceInfo? ForBibleEntry(AiBibleEntry? entry)
        {
            if (entry == null)
                return null;

            // Reuse the entry's own canonical badges so the inline strip is identical to the full BIBLE window.
            List<AtlasEvidenceBadge> badges = entry.EvidenceBadges
                .Select(b => new AtlasEvidenceBadge(b))
                .ToList();

            string kind = string.IsNullOrWhiteSpace(entry.DetailKind) ? entry.KindLabel : entry.DetailKind;
            string domain = string.IsNullOrWhiteSpace(entry.Domain) ? "" : string.Format(Strings.U_Vh_DomainSuffix, entry.Domain);
            string origin = string.Format(Strings.U_Vh_OriginLabel, kind, domain);

            return new AtlasEvidenceInfo(badges, origin, AiBibleWhereAppears.For(entry));
        }

        public static AtlasEvidenceInfo ForDetail(SpiraDataAtlasDetailEntry detail)
        {
            // Same engine as the BIBLE entries: an Atlas detail row is RT0-backed and read-only by construction.
            List<AtlasEvidenceBadge> badges = AiBibleEvidence
                .Derive(detail.Evidence, detail.Tags, detail.WriterPolicy, "", detail.Domain,
                    rt0Backed: true, isGuardrailKind: false, readOnlySource: true)
                .Select(b => new AtlasEvidenceBadge(b))
                .ToList();

            string domain = string.IsNullOrWhiteSpace(detail.Domain) ? "" : string.Format(Strings.U_Vh_DomainSuffix, detail.Domain);
            string origin = string.Format(Strings.U_Vh_OriginLabel, detail.Kind, domain);
            string where = string.IsNullOrWhiteSpace(detail.SourcePath) ? "" : string.Format(Strings.U_Vh_SourceLabel, detail.SourcePath);
            return new AtlasEvidenceInfo(badges, origin, where);
        }
    }
}
