using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.Modules.Extras
{
    internal enum ExtrasEvidenceTone
    {
        Proven,
        Structural,
        Guess,
        Blocked,
        ReadOnly,
        DoNotPromote,
        PipelineSupport
    }

    internal sealed class ExtrasEvidenceBadgeModel
    {
        public ExtrasEvidenceBadgeModel(string label, ExtrasEvidenceTone tone, string tooltip)
        {
            Label = label;
            Tone = tone;
            Tooltip = tooltip;
        }

        public string Label { get; }
        public ExtrasEvidenceTone Tone { get; }
        public string Tooltip { get; }
    }

    internal sealed class ExtrasProvenanceModel
    {
        public ExtrasProvenanceModel(string lineage, string sourceScope, string trustSummary)
        {
            Lineage = lineage;
            SourceScope = sourceScope;
            TrustSummary = trustSummary;
        }

        public string Lineage { get; }
        public string SourceScope { get; }
        public string TrustSummary { get; }
    }

    internal sealed class ExtrasReadonlyBoundaryModel
    {
        public ExtrasReadonlyBoundaryModel(string allowedNow, string blockedNow)
        {
            AllowedNow = allowedNow;
            BlockedNow = blockedNow;
        }

        public string AllowedNow { get; }
        public string BlockedNow { get; }
    }

    internal sealed class ExtrasSourceRootDescriptor
    {
        public ExtrasSourceRootDescriptor(string title, string role, string? resolvedPath, bool exists, string statusSummary)
        {
            Title = title;
            Role = role;
            ResolvedPath = resolvedPath ?? "-";
            Exists = exists;
            StatusSummary = statusSummary;
        }

        public string Title { get; }
        public string Role { get; }
        public string ResolvedPath { get; }
        public bool Exists { get; }
        public string StatusSummary { get; }
    }

    internal sealed class ExtrasFrameworkContractRow
    {
        public ExtrasFrameworkContractRow(string title, string status, string summary, string dependencyClass)
        {
            Title = title;
            Status = status;
            Summary = summary;
            DependencyClass = dependencyClass;
        }

        public string Title { get; }
        public string Status { get; }
        public string Summary { get; }
        public string DependencyClass { get; }
    }

    internal sealed class ExtrasPromotionWaveRow
    {
        public ExtrasPromotionWaveRow(string title, string status, string landingZone, string summary, string guardrail)
        {
            Title = title;
            Status = status;
            LandingZone = landingZone;
            Summary = summary;
            Guardrail = guardrail;
        }

        public string Title { get; }
        public string Status { get; }
        public string LandingZone { get; }
        public string Summary { get; }
        public string Guardrail { get; }
    }

    internal sealed class ExtrasFamilySurfaceRow
    {
        public ExtrasFamilySurfaceRow(
            string title,
            string status,
            string mode,
            string summary,
            string readerDependencies,
            string sharedDependencies,
            ExtrasProvenanceModel provenance,
            ExtrasReadonlyBoundaryModel boundary,
            IReadOnlyList<ExtrasEvidenceBadgeModel> badges)
        {
            Title = title;
            Status = status;
            Mode = mode;
            Summary = summary;
            ReaderDependencies = readerDependencies;
            SharedDependencies = sharedDependencies;
            Provenance = provenance;
            Boundary = boundary;
            Badges = badges;
        }

        public string Title { get; }
        public string Status { get; }
        public string Mode { get; }
        public string Summary { get; }
        public string ReaderDependencies { get; }
        public string SharedDependencies { get; }
        public ExtrasProvenanceModel Provenance { get; }
        public ExtrasReadonlyBoundaryModel Boundary { get; }
        public IReadOnlyList<ExtrasEvidenceBadgeModel> Badges { get; }
        public string BadgeSummary => string.Join(" · ", Badges.Select(badge => badge.Label));
    }
}
