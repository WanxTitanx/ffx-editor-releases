using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.Services.Extras;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.Modules.Extras
{
    internal partial class Ps2KnowledgeHub_DataModel : ObservableObject
    {
        public ObservableCollection<ExtrasSourceRootDescriptor> SourceRoots { get; } = new();
        public ObservableCollection<ExtrasFrameworkContractRow> FrameworkContracts { get; } = new();
        public ObservableCollection<ExtrasPromotionWaveRow> PromotionWave { get; } = new();
        public ObservableCollection<ExtrasFamilySurfaceRow> FamilySurfaces { get; } = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectedFamilyTitle))]
        [NotifyPropertyChangedFor(nameof(SelectedFamilyStatus))]
        [NotifyPropertyChangedFor(nameof(SelectedFamilyMode))]
        [NotifyPropertyChangedFor(nameof(SelectedFamilySummary))]
        [NotifyPropertyChangedFor(nameof(SelectedFamilyBadgeSummary))]
        [NotifyPropertyChangedFor(nameof(SelectedFamilyReaderDependencies))]
        [NotifyPropertyChangedFor(nameof(SelectedFamilySharedDependencies))]
        [NotifyPropertyChangedFor(nameof(SelectedFamilyAllowedNow))]
        [NotifyPropertyChangedFor(nameof(SelectedFamilyBlockedNow))]
        [NotifyPropertyChangedFor(nameof(SelectedFamilyProvenanceSummary))]
        private ExtrasFamilySurfaceRow? selectedFamily;

        public string HeaderSummary => "Extras read-only shell: unify the next promotion wave through shared provenance, guardrails, and companion roots before any decoder or writer fantasy.";
        public string SelectedFamilyTitle => SelectedFamily?.Title ?? "Select an Extras family";
        public string SelectedFamilyStatus => SelectedFamily?.Status ?? "-";
        public string SelectedFamilyMode => SelectedFamily?.Mode ?? "-";
        public string SelectedFamilySummary => SelectedFamily?.Summary ?? "Pick a surface on the left rail to inspect its read-only contract.";
        public string SelectedFamilyBadgeSummary => SelectedFamily?.BadgeSummary ?? "-";
        public string SelectedFamilyReaderDependencies => SelectedFamily?.ReaderDependencies ?? "-";
        public string SelectedFamilySharedDependencies => SelectedFamily?.SharedDependencies ?? "-";
        public string SelectedFamilyAllowedNow => SelectedFamily?.Boundary.AllowedNow ?? "-";
        public string SelectedFamilyBlockedNow => SelectedFamily?.Boundary.BlockedNow ?? "-";
        public string SelectedFamilyProvenanceSummary => SelectedFamily == null
            ? "-"
            : $"{SelectedFamily.Provenance.Lineage} · {SelectedFamily.Provenance.SourceScope} · {SelectedFamily.Provenance.TrustSummary}";

        public Ps2KnowledgeHub_DataModel()
        {
            Refresh();
        }

        public void Refresh()
        {
            Replace(SourceRoots, ExtrasSourceResolver_Service.BuildSourceRoots());
            Replace(FrameworkContracts, BuildFrameworkContracts());
            Replace(PromotionWave, BuildPromotionWave());
            Replace(FamilySurfaces, BuildFamilySurfaces());
            SelectedFamily = FamilySurfaces.FirstOrDefault();
        }

        static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
        {
            target.Clear();
            foreach (T item in source)
                target.Add(item);
        }

        static IReadOnlyList<ExtrasFrameworkContractRow> BuildFrameworkContracts()
        {
            return
            [
                new ExtrasFrameworkContractRow(
                    "ExtrasSourceResolver",
                    "Implemented now",
                    "Resolves master, ffx_ps2, and ps3data companion roots from the current workspace without forcing fake availability.",
                    "Shell / source roots"),
                new ExtrasFrameworkContractRow(
                    "ExtrasEvidenceBadgeModel",
                    "Implemented now",
                    "Shared badge language for proved, structural, blocked, read-only, do-not-promote, and pipeline-support claims.",
                    "Claim discipline"),
                new ExtrasFrameworkContractRow(
                    "ExtrasProvenanceModel",
                    "Implemented now",
                    "Keeps lineage, source scope, and trust summary visible in every Extras surface.",
                    "Provenance"),
                new ExtrasFrameworkContractRow(
                    "ExtrasReadonlyBoundaryModel",
                    "Implemented now",
                    "Forces every surface to say what is allowed now and what stays blocked now.",
                    "Safety boundary"),
                new ExtrasFrameworkContractRow(
                    "ExtrasFileOpenService",
                    "Implemented now",
                    "Lets read-only modules open source roots or companion files without pretending they are editable.",
                    "Utility service"),
                new ExtrasFrameworkContractRow(
                    "ExtrasHexMetadataPanel",
                    "UI next",
                    "Shared metadata/hex pane contract is now defined by the hub, but the generic panel still needs a dedicated control.",
                    "Shared panel"),
                new ExtrasFrameworkContractRow(
                    "ExtrasFamilyStatusBadge",
                    "Implemented now",
                    "Family surfaces expose ready/read-only/blocked state before any parser-specific widgets land.",
                    "Family registry")
            ];
        }

        static IReadOnlyList<ExtrasPromotionWaveRow> BuildPromotionWave()
        {
            return
            [
                new ExtrasPromotionWaveRow(
                    "Runtime AI owner-candidate port",
                    "Manual port next",
                    "LiveBattleLab / AI Probe",
                    "Bring the owner-candidate ranking idea from the old ai-probe slice into the current NaturalCapture-based runtime surface without dragging old write entanglement back in.",
                    "No blind merge. Preserve the current NaturalCapture and runtime guardrail flow; promote read-only sequencing truth only."),
                new ExtrasPromotionWaveRow(
                    "Monster AI diff corpus",
                    "Reader next",
                    "Monster AI Explorer / AEON",
                    "Turn vanilla-vs-mod monster corpora into a byte-range and segment diff surface so AI edits stop hiding inside raw mon blobs.",
                    "Keep it read-only first. Stats churn alone is low value; highlight AiFile, script, and behavior-adjacent deltas."),
                new ExtrasPromotionWaveRow(
                    "MGRP motion inspector",
                    "Read-only inspector next",
                    "ModelViewer / MotionLinker bridge",
                    "Expose the new MGRP/MSEQ decode findings as structural motion evidence, channel topology, and payload inspection instead of claiming playback is solved.",
                    "Do not sell body playback, exporter truth, or exact runtime equivalence before the last bridge lands."),
                new ExtrasPromotionWaveRow(
                    "PS2 audio consolidation",
                    "Polish now",
                    "Extras / PS2 Audio (.wd)",
                    "Consolidate the WD readers, vgmstream fallback, and sample/bank metadata into a cleaner read-only browser with stronger smoke coverage.",
                    "Keep the lane read-only. No fake codec ownership, no writer, and no unsafe repack promotion."),
                new ExtrasPromotionWaveRow(
                    "Curated knowledge and graph surfaces",
                    "Hub-driven next",
                    "PS2 Knowledge / PS3 companion curation",
                    "Use the existing Extras shell to surface graph, provenance, and cross-family navigation from ps3data, magicfiles, Pt52, Pt57, Pt58, and Pt67 without dumping raw research sludge into the UI.",
                    "Curate only. No giant raw dump, no fake semantics, and no promoted graph edge without provenance.")
            ];
        }

        static IReadOnlyList<ExtrasFamilySurfaceRow> BuildFamilySurfaces()
        {
            return
            [
                new ExtrasFamilySurfaceRow(
                    "PS2 Knowledge / Pt58",
                    "Ready now",
                    "Read-only hub",
                    "Family registry, connection graph, sensitivity atlas, badges, provenance, and integration order for the whole PS2 campaign.",
                    "No new parser required for the first cut. This surface is backed by already classified knowledge and companion-root discovery.",
                    "Depends on Extras source roots, badge/provenance models, and the shared read-only shell.",
                    new ExtrasProvenanceModel("Pt58", "PS2 campaign closeout", "Concrete-value support for knowledge, scope gates, and read-only navigation."),
                    new ExtrasReadonlyBoundaryModel(
                        "Expose family registry, graph, sensitivity, and do-not-promote badges.",
                        "Do not claim decoder-final, playback, writer, or runtime-proof authority."),
                    BuildBadges("PROVED", "READ-ONLY", "DO NOT PROMOTE")),
                new ExtrasFamilySurfaceRow(
                    "TM2 Preview / Pt56",
                    "Initial module present",
                    "Texture preview",
                    "Best quick win for a visual Extras surface. The first read-only TIM2 browser/inspector is live, and the support lane now also exposes TXC / CLT / FMT / SPS2 metadata and pairing clues.",
                    "TIM2 reader is present now. TXC/CLT pairing and support metadata are already exposed as the second tab in the same module.",
                    "Depends on shared source roots, file-open service, badges, and the still-missing generic metadata/hex pane.",
                    new ExtrasProvenanceModel("Pt56", "PS2 texture/palette decode", "Strongest candidate for first visual read-only win."),
                    new ExtrasReadonlyBoundaryModel(
                        "Preview TM2 assets, list basic metadata, and keep the preview labels brutally honest.",
                        "Do not promise full palette editor, writer, or universal texture decode."),
                    BuildBadges("PROVED", "READ-ONLY", "NO RUNTIME PROOF")),
                new ExtrasFamilySurfaceRow(
                    "Binary Atlas / Pt52",
                    "Initial module present",
                    "Binary navigator",
                    "Turns the giant `.bin/.ftc` forest into buckets, sidecar pairs, FTC header lanes, sensitivity panels, and selected-file inspectors instead of one fake mega-format.",
                    "Bucket scanner, FTC lane, first64 dwords, signature group, FTC header inspector, and same-basename sidecar wiring are already present. Regional diff stays for the next wave.",
                    "Depends on shared source roots, file-open service, badge system, and family-level navigation shell.",
                    new ExtrasProvenanceModel("Pt52", "PS2 master bin taxonomy", "Ready for a real atlas once the shared shell exists."),
                    new ExtrasReadonlyBoundaryModel(
                        "Navigate bucket -> signature -> sidecar -> file and expose structural/blocked/dangerous labels.",
                        "Do not promote bucket clustering into parser, writer, or semantic decoder claims."),
                    BuildBadges("STRUCTURAL", "READ-ONLY", "DO NOT PROMOTE")),
                new ExtrasFamilySurfaceRow(
                    "Presentation Containers / Pt57",
                    "Initial module present",
                    "Container browser",
                    "Read-only browser for `.vpa`, `.ebp`, `.omd`, and `.sps2` cohorts so the cold presentation families stop living only in docs.",
                    "Signature, cohort, companion hints, and guardrails are already exposed in a dedicated shell module.",
                    "Depends on shared source roots, file-open service, and badge/provenance framing.",
                    new ExtrasProvenanceModel("Pt57", "PS2 presentation container atlas", "Cold family browser with honest boundaries, not a promoted deep decoder."),
                    new ExtrasReadonlyBoundaryModel(
                        "Browse presentation cohorts, signatures, and companion lanes with explicit read-only labeling.",
                        "Do not claim timeline/script/deep semantic decode authority."),
                    BuildBadges("STRUCTURAL", "READ-ONLY", "DO NOT PROMOTE")),
                new ExtrasFamilySurfaceRow(
                    "Battle Corpus Crosswalk / Pt44",
                    "Initial module present",
                    "Crosswalk explorer",
                    "Read-only `formation -> actor row -> corpus overlay` bridge so battle composition truth can be checked against actor-surface rows without pretending runtime proof is solved.",
                    "Formation slots, actor rows, encounter references, and corpus overlays are already exposed in the shell.",
                    "Depends on battle readers, encounter table readers, corpus services, and shared Extras framing.",
                    new ExtrasProvenanceModel("Pt44", "Battle corpus runtime crosswalk", "Useful read-only truth surface with explicit actor-vs-composition limits."),
                    new ExtrasReadonlyBoundaryModel(
                        "Inspect composition truth, actor rows, and encounter references side by side.",
                        "Do not promote it into live runtime ownership or target-resolution proof."),
                    BuildBadges("PROVED", "READ-ONLY", "NO RUNTIME PROOF")),
                new ExtrasFamilySurfaceRow(
                    "Project / ABMap / Pt54",
                    "Initial module present",
                    "Pipeline explorer",
                    "Exposes proj and eiichi_abmap_data as a build/index/pipeline graph instead of pretending those files are final runtime assets.",
                    "cdrom index triplets, descriptor rows, ABMap support inventory, and graph edges are present in the first read-only cut.",
                    "Depends on shared source roots, provenance/warning badges, and graph-style navigation.",
                    new ExtrasProvenanceModel("Pt54", "Project and ABMap support line", "Strong pipeline/support product candidate, not an asset editor."),
                    new ExtrasReadonlyBoundaryModel(
                        "Show source -> descriptor -> index -> payload flow with hard warnings.",
                        "Do not promote support/pipeline files as final assets or writer-safe surfaces."),
                    BuildBadges("PIPELINE", "READ-ONLY", "DO NOT PROMOTE")),
                new ExtrasFamilySurfaceRow(
                    "Magic Effects / Pt67",
                    "Initial module present",
                    "Effect crosswalk",
                    "Specialized viewer for `mag_*`, `bat_eff`, `et_battle`, and kernel-side battle assets, with crosswalk rows kept brutally honest about what is still blocked.",
                    "Kernel lane, package lane, and crosswalk status are already exposed as a first read-only product cut.",
                    "Depends on source roots, badge/provenance framework, graph shell, and likely reader widgets from the shared panel.",
                    new ExtrasProvenanceModel("Pt67", "Magic/effect read-only product line", "Promising, but still reader-dependent and guardrail-heavy."),
                    new ExtrasReadonlyBoundaryModel(
                        "Show package membership, side-by-side lanes, and cosmetic-color backlog honestly.",
                        "Do not claim magic constructor, writer, or runtime consumer resolution."),
                    BuildBadges("STRUCTURAL", "READ-ONLY", "NO RUNTIME PROOF")),
                new ExtrasFamilySurfaceRow(
                    "PS2 Audio / WD",
                    "Initial module present",
                    "Audio bank browser",
                    "Read-only WD bank browser with sample descriptors, offset/size facts, program tables, and vgmstream-assisted preview/export hooks kept honest about external dependency boundaries.",
                    "WD header/program/sample descriptor reader is present now. Playback and export stay delegated to vgmstream instead of pretending the editor owns codec-final decode.",
                    "Depends on Extras source roots, file-open service, badge/provenance models, and the external vgmstream bridge for preview/export convenience.",
                    new ExtrasProvenanceModel("PS2 sound lane", "WD banks plus vgmstream bridge", "Concrete read-only audio surface with explicit external-tool boundary."),
                    new ExtrasReadonlyBoundaryModel(
                        "Inspect WD banks, list samples, preview/export through the external oracle, and keep bank metadata visible.",
                        "Do not promote native codec-final decode, repack, or writer-safe audio editing."),
                    BuildBadges("PROVED", "READ-ONLY", "DO NOT PROMOTE")),
                new ExtrasFamilySurfaceRow(
                    "PS3 Magic / HD",
                    "Initial module present",
                    "HD texture browser",
                    "Read-only HD magic texture surface for ps3data `magic_####` folders, with decoded mip0 previews, metadata, and optional synthetic composite/cycle views kept clearly separate from real in-engine effects.",
                    "DDS.PHYRE reader and browser are present now. Effect recipes, consumer runtime truth, and engine-accurate playback stay blocked behind live observation.",
                    "Depends on ps3data companion roots, shared Extras provenance/badges, and the PS3 texture reader lane.",
                    new ExtrasProvenanceModel("ps3data magic lane", "HD texture decode plus observation backlog", "Useful viewer, but not an effect-runtime truth surface."),
                    new ExtrasReadonlyBoundaryModel(
                        "Preview decoded texture layers, inspect package membership, and keep synthetic views labeled synthetic.",
                        "Do not promote real effect timing, composition, or writer-safe PS3 magic editing."),
                    BuildBadges("PROVED", "READ-ONLY", "NO RUNTIME PROOF"))
            ];
        }

        static IReadOnlyList<ExtrasEvidenceBadgeModel> BuildBadges(params string[] labels)
        {
            List<ExtrasEvidenceBadgeModel> badges = new();

            foreach (string label in labels)
            {
                badges.Add(label switch
                {
                    "PROVED" => new ExtrasEvidenceBadgeModel("PROVED", ExtrasEvidenceTone.Proven, "The surface has a strong knowledge contract or proven product role."),
                    "STRUCTURAL" => new ExtrasEvidenceBadgeModel("STRUCTURAL", ExtrasEvidenceTone.Structural, "The surface is grounded in structural classification, not semantic decode."),
                    "READ-ONLY" => new ExtrasEvidenceBadgeModel("READ-ONLY", ExtrasEvidenceTone.ReadOnly, "This module is intentionally read-only."),
                    "DO NOT PROMOTE" => new ExtrasEvidenceBadgeModel("DO NOT PROMOTE", ExtrasEvidenceTone.DoNotPromote, "Do not sell this surface as parser-final, writer-safe, or runtime-proven."),
                    "NO RUNTIME PROOF" => new ExtrasEvidenceBadgeModel("NO RUNTIME PROOF", ExtrasEvidenceTone.Blocked, "Useful surface, but it still lacks runtime authority."),
                    "PIPELINE" => new ExtrasEvidenceBadgeModel("PIPELINE", ExtrasEvidenceTone.PipelineSupport, "This surface is pipeline/support side, not final asset truth."),
                    _ => new ExtrasEvidenceBadgeModel(label, ExtrasEvidenceTone.Guess, "Unclassified badge.")
                });
            }

            return badges;
        }
    }
}
