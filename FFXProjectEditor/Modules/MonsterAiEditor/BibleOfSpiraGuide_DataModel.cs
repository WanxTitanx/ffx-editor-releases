using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.Resources;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FFXProjectEditor.Modules.MonsterAiEditor
{
    internal sealed partial class BibleOfSpiraGuide_DataModel : ObservableObject
    {
        public ObservableCollection<BibleKindFilter> KindFilters { get; } = new();
        public ObservableCollection<BibleDomainFilter> DomainFilters { get; } = new();
        public ObservableCollection<BibleEvidenceFilter> EvidenceFilters { get; } = new();
        public ObservableCollection<BibleEntryVm> Entries { get; } = new();

        [ObservableProperty] private string searchText = "";
        [ObservableProperty] private BibleKindFilter? selectedKindFilter;
        [ObservableProperty] private BibleDomainFilter? selectedDomainFilter;
        [ObservableProperty] private BibleEvidenceFilter? selectedEvidenceFilter;
        [ObservableProperty] private BibleEntryVm? selectedEntry;
        [ObservableProperty] private string searchSummary = "";
        [ObservableProperty] private string contextSummary = "";
        bool updatingDomainFilters;

        public string OverviewText =>
            Strings.F2_atel_is_the_stack_vm_for_monster_ai_disa_853ee04d;
        public bool HasSelection => SelectedEntry != null;
        public bool HasNoResults => Entries.Count == 0;

        public BibleOfSpiraGuide_DataModel(AiBibleEntry? initialEntry, string? initialSearch, string? initialContext)
        {
            KindFilters.Add(new BibleKindFilter(Strings.F2_all_6a720856, null));
            KindFilters.Add(new BibleKindFilter("Patterns", AiBibleEntryKind.Pattern));
            KindFilters.Add(new BibleKindFilter("Commands", AiBibleEntryKind.Command));
            KindFilters.Add(new BibleKindFilter("Functions", AiBibleEntryKind.Function));
            KindFilters.Add(new BibleKindFilter("Fields", AiBibleEntryKind.Field));
            KindFilters.Add(new BibleKindFilter("Targets", AiBibleEntryKind.Target));
            KindFilters.Add(new BibleKindFilter("Opcodes", AiBibleEntryKind.Opcode));
            KindFilters.Add(new BibleKindFilter("Atlas", AiBibleEntryKind.Atlas));
            KindFilters.Add(new BibleKindFilter("Guardrails", AiBibleEntryKind.Guardrail));
            DomainFilters.Add(new BibleDomainFilter(Strings.F2_all_domains_08cbf6a3, null));

            EvidenceFilters.Add(new BibleEvidenceFilter(Strings.F2_all_6a720856, BibleEvidenceFilterKind.All));
            EvidenceFilters.Add(new BibleEvidenceFilter("Proved/RT0", BibleEvidenceFilterKind.ProvedRt0));
            EvidenceFilters.Add(new BibleEvidenceFilter("Parser corpus", BibleEvidenceFilterKind.ParserCorpus));
            EvidenceFilters.Add(new BibleEvidenceFilter("Metadata-only", BibleEvidenceFilterKind.MetadataOnly));
            EvidenceFilters.Add(new BibleEvidenceFilter("Blocked", BibleEvidenceFilterKind.Blocked));
            EvidenceFilters.Add(new BibleEvidenceFilter("RT2 pending", BibleEvidenceFilterKind.Rt2Pending));

            selectedKindFilter = KindFilters[0];
            selectedDomainFilter = DomainFilters[0];
            selectedEvidenceFilter = EvidenceFilters[0];
            searchText = initialSearch ?? "";
            contextSummary = string.IsNullOrWhiteSpace(initialContext)
                ? Strings.F2_opened_without_line_context_choose_an_en_829ef6ff
                : initialContext;

            RefreshEntries(initialEntry);
        }

        partial void OnSearchTextChanged(string value) => RefreshEntries(SelectedEntry?.Entry);
        partial void OnSelectedKindFilterChanged(BibleKindFilter? value) => RefreshEntries(SelectedEntry?.Entry);
        partial void OnSelectedDomainFilterChanged(BibleDomainFilter? value)
        {
            if (!updatingDomainFilters)
                RefreshEntries(SelectedEntry?.Entry);
        }
        partial void OnSelectedEvidenceFilterChanged(BibleEvidenceFilter? value) => RefreshEntries(SelectedEntry?.Entry);
        partial void OnSelectedEntryChanged(BibleEntryVm? value) => OnPropertyChanged(nameof(HasSelection));

        void RefreshEntries(AiBibleEntry? preferredEntry)
        {
            string[] terms = HighlightTerms(SearchText);
            IReadOnlyList<AiBibleEntry> ranked = AiBibleCatalog.Search(SearchText, AiBibleCatalog.All.Count);
            foreach (BibleKindFilter filter in KindFilters)
                filter.Count = filter.Kind == null ? ranked.Count : ranked.Count(e => e.Kind == filter.Kind.Value);

            IEnumerable<AiBibleEntry> filtered = ranked;
            if (SelectedKindFilter?.Kind is AiBibleEntryKind kind)
                filtered = filtered.Where(e => e.Kind == kind);

            List<AiBibleEntry> kindFiltered = filtered.ToList();
            RefreshDomainFilters(kindFiltered);
            List<AiBibleEntry> domainFiltered = (string.IsNullOrWhiteSpace(SelectedDomainFilter?.Domain)
                ? kindFiltered
                : kindFiltered.Where(e => string.Equals(e.Domain, SelectedDomainFilter.Domain, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            RefreshEvidenceFilters(domainFiltered);
            BibleEvidenceFilterKind evidenceKind = SelectedEvidenceFilter?.Kind ?? BibleEvidenceFilterKind.All;
            IEnumerable<AiBibleEntry> finalFiltered = evidenceKind == BibleEvidenceFilterKind.All
                ? domainFiltered
                : domainFiltered.Where(e => BibleEntryVm.MatchesEvidence(e, evidenceKind));

            Entries.Clear();
            foreach (AiBibleEntry entry in finalFiltered)
                Entries.Add(new BibleEntryVm(entry, terms));

            string? preferredId = preferredEntry?.Id;
            SelectedEntry = Entries.FirstOrDefault(e => string.Equals(e.Entry.Id, preferredId, StringComparison.OrdinalIgnoreCase))
                ?? Entries.FirstOrDefault();

            string query = string.IsNullOrWhiteSpace(SearchText) ? "catalogo completo" : $"busca \"{SearchText.Trim()}\"";
            string filterLabel = SelectedKindFilter?.Label ?? Strings.F2_all_6a720856;
            string domainLabel = SelectedDomainFilter?.Label ?? Strings.F2_all_domains_08cbf6a3;
            string evidenceLabel = SelectedEvidenceFilter?.Label ?? Strings.F2_all_6a720856;
            SearchSummary = Entries.Count == 0
                ? $"Nenhum verbete em {filterLabel} / {domainLabel} / evidencia {evidenceLabel} para {query}. Tente metadata-only, blocked, RT2-pending, domain:monster-presence m337 ou domain:gear-name-model Brotherhood."
                : $"{Entries.Count} verbete(s) em {filterLabel} / {domainLabel} / evidencia {evidenceLabel} para {query}.";
            OnPropertyChanged(nameof(HasNoResults));
        }

        void RefreshEvidenceFilters(IReadOnlyList<AiBibleEntry> entries)
        {
            int provedRt0 = 0, parserCorpus = 0, metadataOnly = 0, blocked = 0, rt2Pending = 0;
            foreach (AiBibleEntry entry in entries)
            {
                if (BibleEntryVm.MatchesEvidence(entry, BibleEvidenceFilterKind.ProvedRt0)) provedRt0++;
                if (BibleEntryVm.MatchesEvidence(entry, BibleEvidenceFilterKind.ParserCorpus)) parserCorpus++;
                if (BibleEntryVm.MatchesEvidence(entry, BibleEvidenceFilterKind.MetadataOnly)) metadataOnly++;
                if (BibleEntryVm.MatchesEvidence(entry, BibleEvidenceFilterKind.Blocked)) blocked++;
                if (BibleEntryVm.MatchesEvidence(entry, BibleEvidenceFilterKind.Rt2Pending)) rt2Pending++;
            }

            foreach (BibleEvidenceFilter filter in EvidenceFilters)
            {
                filter.Count = filter.Kind switch
                {
                    BibleEvidenceFilterKind.All => entries.Count,
                    BibleEvidenceFilterKind.ProvedRt0 => provedRt0,
                    BibleEvidenceFilterKind.ParserCorpus => parserCorpus,
                    BibleEvidenceFilterKind.MetadataOnly => metadataOnly,
                    BibleEvidenceFilterKind.Blocked => blocked,
                    BibleEvidenceFilterKind.Rt2Pending => rt2Pending,
                    _ => filter.Count,
                };
            }
        }

        void RefreshDomainFilters(IReadOnlyList<AiBibleEntry> entries)
        {
            string? previousDomain = SelectedDomainFilter?.Domain;
            Dictionary<string, int> domainCounts = entries
                .Where(e => !string.IsNullOrWhiteSpace(e.Domain))
                .GroupBy(e => e.Domain, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            updatingDomainFilters = true;
            DomainFilters.Clear();
            DomainFilters.Add(new BibleDomainFilter(Strings.F2_all_domains_08cbf6a3, null) { Count = entries.Count });
            foreach (KeyValuePair<string, int> pair in domainCounts
                         .OrderByDescending(p => p.Value)
                         .ThenBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            {
                DomainFilters.Add(new BibleDomainFilter(pair.Key, pair.Key) { Count = pair.Value });
            }

            SelectedDomainFilter =
                DomainFilters.FirstOrDefault(f => string.Equals(f.Domain, previousDomain, StringComparison.OrdinalIgnoreCase))
                ?? DomainFilters[0];
            updatingDomainFilters = false;
        }

        static string[] HighlightTerms(string? query) =>
            (query ?? "")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => t.StartsWith("kind:", StringComparison.OrdinalIgnoreCase) ? t["kind:".Length..] : t)
                .Select(t => t.StartsWith("domain:", StringComparison.OrdinalIgnoreCase) ? t["domain:".Length..] : t)
                .Select(t => t.StartsWith("category:", StringComparison.OrdinalIgnoreCase) ? t["category:".Length..] : t)
                .Where(t => t.Length > 1 && !t.All(c => c == ':'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToArray();
    }

    internal sealed partial class BibleKindFilter : ObservableObject
    {
        [ObservableProperty] private int count;

        public BibleKindFilter(string label, AiBibleEntryKind? kind)
        {
            Label = label;
            Kind = kind;
        }

        public string Label { get; }
        public AiBibleEntryKind? Kind { get; }
        public string Display => $"{Label} ({Count})";

        partial void OnCountChanged(int value) => OnPropertyChanged(nameof(Display));
    }

    internal sealed partial class BibleDomainFilter : ObservableObject
    {
        [ObservableProperty] private int count;

        public BibleDomainFilter(string label, string? domain)
        {
            Label = label;
            Domain = domain;
        }

        public string Label { get; }
        public string? Domain { get; }
        public string Display => $"{Label} ({Count})";

        partial void OnCountChanged(int value) => OnPropertyChanged(nameof(Display));
    }

    internal enum BibleEvidenceFilterKind
    {
        All,
        ProvedRt0,
        ParserCorpus,
        MetadataOnly,
        Blocked,
        Rt2Pending,
    }

    internal sealed partial class BibleEvidenceFilter : ObservableObject
    {
        [ObservableProperty] private int count;

        public BibleEvidenceFilter(string label, BibleEvidenceFilterKind kind)
        {
            Label = label;
            Kind = kind;
        }

        public string Label { get; }
        public BibleEvidenceFilterKind Kind { get; }
        public string Display => $"{Label} ({Count})";

        partial void OnCountChanged(int value) => OnPropertyChanged(nameof(Display));
    }

    internal sealed class EvidenceBadgeVm
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

        public EvidenceBadgeVm(AiBibleBadge badge)
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

    internal sealed class BibleEntryVm
    {
        static readonly IBrush FunctionBrush = new SolidColorBrush(Color.Parse("#203E3A"));
        static readonly IBrush FieldBrush = new SolidColorBrush(Color.Parse("#3B321F"));
        static readonly IBrush TargetBrush = new SolidColorBrush(Color.Parse("#1E3349"));
        static readonly IBrush OpcodeBrush = new SolidColorBrush(Color.Parse("#2E2844"));
        static readonly IBrush CommandBrush = new SolidColorBrush(Color.Parse("#263A56"));
        static readonly IBrush AtlasBrush = new SolidColorBrush(Color.Parse("#21443D"));
        static readonly IBrush PatternBrush = new SolidColorBrush(Color.Parse("#3B2F20"));
        static readonly IBrush GuardrailBrush = new SolidColorBrush(Color.Parse("#44252A"));
        static readonly IBrush HighlightBrush = new SolidColorBrush(Color.Parse("#4B3F22"));
        static readonly IBrush NormalTextBrush = new SolidColorBrush(Color.Parse("#F1F5F9"));
        static readonly IBrush MutedTextBrush = new SolidColorBrush(Color.Parse("#9EB0C2"));
        static readonly IBrush TransparentBrush = Brushes.Transparent;

        public BibleEntryVm(AiBibleEntry entry, IReadOnlyList<string> highlightTerms)
        {
            Entry = entry;
            TitleRuns = BuildRuns(entry.Title, highlightTerms, NormalTextBrush);
            SummaryRuns = BuildRuns(entry.Summary, highlightTerms, MutedTextBrush);
            Badges = AiBibleEvidence.Badges(entry).Select(b => new EvidenceBadgeVm(b)).ToList();
        }

        public AiBibleEntry Entry { get; }
        public string Id => Entry.Id;
        public string Title => Entry.Title;
        public string Summary => Entry.Summary;
        public string DomainLabel => string.IsNullOrWhiteSpace(Entry.Domain) ? "global" : Entry.Domain;
        public bool HasDomain => !string.IsNullOrWhiteSpace(Entry.Domain);
        public string GuideRole => Entry.Kind switch
        {
            AiBibleEntryKind.Function => Strings.F2_native_function_called_by_atel_bytecode_28bfce29,
            AiBibleEntryKind.Field => Strings.F2_state_field_used_by_actor_read_write_or__c2dccdc7,
            AiBibleEntryKind.Target => Strings.F2_target_sentinel_used_before_battle_comma_65753cbf,
            AiBibleEntryKind.Opcode => Strings.F2_atel_vm_instruction_changing_this_alters_d02afa8a,
            AiBibleEntryKind.Command => Strings.U_Ai_BibleCommandKind,
            AiBibleEntryKind.Atlas => Strings.F2_read_only_corpus_map_where_data_names_an_bd772a57,
            AiBibleEntryKind.Pattern => Strings.U_Ai_BiblePatternKind,
            AiBibleEntryKind.Guardrail => Strings.F2_product_safety_rule_prevents_promoting_p_3156ffe9,
            _ => Strings.U_Ai_BibleReadOnlyEntry,
        };
        public string SafeUseText => Entry.Kind switch
        {
            AiBibleEntryKind.Pattern => Strings.F2_use_to_understand_the_script_and_guide_r_c43325d7,
            AiBibleEntryKind.Guardrail => Strings.F2_use_as_a_decision_lock_if_this_alert_app_f5bb7f19,
            AiBibleEntryKind.Opcode => Strings.U_Ai_BibleOpcodeSafeUse,
            AiBibleEntryKind.Command => Strings.F2_use_to_select_payloads_with_evidence_app_dd7a5783,
            AiBibleEntryKind.Atlas => Strings.F2_use_to_navigate_the_corpus_and_locate_so_1e858267,
            AiBibleEntryKind.Target when Entry.Id.Equals("target:FFF3", StringComparison.OrdinalIgnoreCase) =>
                Strings.F2_self_is_the_most_predictable_target_broa_75c30611,
            AiBibleEntryKind.Target => Strings.U_Ai_BibleTargetSafeUse,
            AiBibleEntryKind.Field => Strings.U_Ai_BibleFieldSafeUse,
            AiBibleEntryKind.Function => Strings.U_Ai_BibleFunctionSafeUse,
            _ => Strings.U_Ai_BibleReadFirst,
        };
        public string MinuteWhatText => Entry.Kind switch
        {
            AiBibleEntryKind.Pattern => Entry.Title.Contains("Shiva", StringComparison.OrdinalIgnoreCase)
                ? Strings.U_Ai_BibleShivaOverdrive
                : Strings.U_Ai_BiblePatternStudy,
            AiBibleEntryKind.Function => Entry.Id.Equals("func:70AB", StringComparison.OrdinalIgnoreCase)
                ? Strings.U_Ai_BibleStatContextSetter
                : Strings.U_Ai_BibleNativeCall,
            AiBibleEntryKind.Field => Strings.U_Ai_BibleStateField,
            AiBibleEntryKind.Target => Strings.U_Ai_BibleTargetSentinel,
            AiBibleEntryKind.Opcode => Strings.U_Ai_BibleVmInstruction,
            AiBibleEntryKind.Command => Strings.U_Ai_BibleCommandMagic,
            AiBibleEntryKind.Atlas => Strings.U_Ai_BibleAtlas,
            AiBibleEntryKind.Guardrail => Strings.U_Ai_BibleGuardrail,
            _ => Strings.U_Ai_BibleReadOnly,
        };
        public string MinuteRiskText => Entry.Kind switch
        {
            AiBibleEntryKind.Pattern => Entry.Title.Contains("Shiva", StringComparison.OrdinalIgnoreCase)
                ? Strings.U_Ai_BibleShivaRisk
                : Strings.U_Ai_BibleMediumRisk,
            AiBibleEntryKind.Guardrail => Strings.U_Ai_BibleGuardrailRisk,
            AiBibleEntryKind.Opcode => Strings.U_Ai_BibleOpcodeRisk,
            AiBibleEntryKind.Command => Entry.Guardrail.Contains("Metadata-only", StringComparison.OrdinalIgnoreCase)
                ? Strings.U_Ai_BibleCommandMetaRisk
                : Strings.U_Ai_BibleCommandRisk,
            AiBibleEntryKind.Atlas => Entry.Guardrail.Contains("blocked", StringComparison.OrdinalIgnoreCase)
                ? Strings.U_Ai_BibleAtlasWriterRisk
                : Strings.U_Ai_BibleAtlasRisk,
            AiBibleEntryKind.Target when Entry.Id.Equals("target:FFF3", StringComparison.OrdinalIgnoreCase) => Strings.U_Ai_BibleSelfTargetRisk,
            AiBibleEntryKind.Target => Strings.U_Ai_BibleTargetRisk,
            AiBibleEntryKind.Field => Strings.U_Ai_BibleFieldRisk,
            AiBibleEntryKind.Function => Strings.U_Ai_BibleFunctionRisk,
            _ => Strings.U_Ai_BibleUnknownRisk,
        };
        public string MinuteNextText => Entry.Kind switch
        {
            AiBibleEntryKind.Pattern => Entry.Title.Contains("Shiva", StringComparison.OrdinalIgnoreCase)
                ? Strings.U_Ai_BibleShivaNext
                : Strings.U_Ai_BiblePatternNext,
            AiBibleEntryKind.Function => Strings.U_Ai_BibleFunctionNext,
            AiBibleEntryKind.Field => Strings.U_Ai_BibleFieldNext,
            AiBibleEntryKind.Target => "Validate final RT2 effect per case.",
            AiBibleEntryKind.Opcode => "Read along with the following instruction in the assembler.",
            AiBibleEntryKind.Command => "Cross-reference category, ATEL sites, and AEON preview before applying as payload.",
            AiBibleEntryKind.Atlas => "Open the indicated dataset/crosslink and use as evidence, not as writer.",
            AiBibleEntryKind.Guardrail => "Audit corpus/IDA before removing the alert.",
            _ => "Read the guide and check the DEV VIEWER if authoring.",
        };
        public string StackShape => string.IsNullOrWhiteSpace(Entry.StackShape) ? Strings.U_Ai_BibleNoStackShape : Entry.StackShape;
        public string Guardrail => Entry.Guardrail;
        public string Evidence => string.IsNullOrWhiteSpace(Entry.Evidence) ? "Evidence not registered in the catalog." : Entry.Evidence;
        public string TagsText => string.IsNullOrWhiteSpace(Entry.Tags) ? "read-only corpus" : Entry.Tags;
        public string KindLabel => Entry.KindLabel;
        public bool HasGuardrail => !string.IsNullOrWhiteSpace(Entry.Guardrail);
        public bool HasTags => !string.IsNullOrWhiteSpace(Entry.Tags);
        public bool HasWhereAppears => !string.IsNullOrWhiteSpace(WhereAppearsText);
        public IReadOnlyList<HighlightRun> TitleRuns { get; }
        public IReadOnlyList<HighlightRun> SummaryRuns { get; }

        public IReadOnlyList<EvidenceBadgeVm> Badges { get; }
        public IReadOnlyList<EvidenceBadgeVm> BadgesTop => Badges.Count <= 4 ? Badges : Badges.Take(4).ToList();

        // Atlas-derived metadata, surfaced read-only for the BIBLE detail panel. Only atlas:* entries fill these.
        public bool IsAtlasEntry => Entry.Id.StartsWith("atlas:", StringComparison.OrdinalIgnoreCase);
        public string DetailKindLabel => string.IsNullOrWhiteSpace(Entry.DetailKind) ? Entry.KindLabel : Entry.DetailKind;
        public bool HasDetailKind => !string.IsNullOrWhiteSpace(Entry.DetailKind);
        public bool HasSourcePath => !string.IsNullOrWhiteSpace(Entry.SourcePath);
        public string SourcePathText => HasSourcePath ? Entry.SourcePath : "No source path registered.";
        public string WriterPolicyText => string.IsNullOrWhiteSpace(Entry.WriterPolicy) ? Guardrail : Entry.WriterPolicy;

        public static bool MatchesEvidence(AiBibleEntry entry, BibleEvidenceFilterKind kind)
        {
            if (kind == BibleEvidenceFilterKind.All)
                return true;

            IReadOnlyList<string> tokens = entry.EvidenceTokens;
            bool Any(params string[] candidates) =>
                candidates.Any(c => tokens.Contains(c, StringComparer.OrdinalIgnoreCase));

            return kind switch
            {
                BibleEvidenceFilterKind.ProvedRt0 => Any("RT2", "IDA", "proved", "RT0"),
                BibleEvidenceFilterKind.ParserCorpus => Any("parser-corpus", "presence-index"),
                BibleEvidenceFilterKind.MetadataOnly => Any("metadata-only"),
                BibleEvidenceFilterKind.Blocked => Any("blocked"),
                BibleEvidenceFilterKind.Rt2Pending => Any("RT2-pending"),
                _ => true,
            };
        }

        // "Where it appears in the game" — delegated to the shared AiBibleWhereAppears helper so the BIBLE window and
        // the read-only Atlas module badges speak with the exact same honest wording.
        public string WhereAppearsText => AiBibleWhereAppears.For(Entry);

        public IBrush KindBrush => Entry.Kind switch
        {
            AiBibleEntryKind.Function => FunctionBrush,
            AiBibleEntryKind.Field => FieldBrush,
            AiBibleEntryKind.Target => TargetBrush,
            AiBibleEntryKind.Opcode => OpcodeBrush,
            AiBibleEntryKind.Command => CommandBrush,
            AiBibleEntryKind.Atlas => AtlasBrush,
            AiBibleEntryKind.Pattern => PatternBrush,
            AiBibleEntryKind.Guardrail => GuardrailBrush,
            _ => FunctionBrush,
        };

        public string NormalizedSignature
        {
            get
            {
                string name = Title.Split(" (0x", StringSplitOptions.None)[0];
                return Entry.Id.ToLowerInvariant() switch
                {
                    "func:700b" => "performCommand(targetRef, commandId)",
                    "func:705a" => "forcePerformCommand(targetRef, commandId)",
                    "func:700f" => "readChrProperty(actorRef, fieldId) -> value",
                    "func:7018" => "writeChrProperty(actorRef, fieldId, value)",
                    "func:70aa" => "getStatField(fieldId) -> value",
                    "func:70ab" => "setStatField(fieldId, value)",
                    _ when Entry.Kind == AiBibleEntryKind.Function => $"{name}(...)",
                    _ when Entry.Kind == AiBibleEntryKind.Field => $"{name}: btlActorProperty field id",
                    _ when Entry.Kind == AiBibleEntryKind.Target => $"{name}: target sentinel",
                    _ when Entry.Kind == AiBibleEntryKind.Opcode => $"{name}: ATEL VM opcode",
                    _ when Entry.Kind == AiBibleEntryKind.Command => $"performCommand(targetRef, {ExtractHexOrId()})",
                    _ when Entry.Kind == AiBibleEntryKind.Atlas && Entry.Id.StartsWith("atlas:parsed-file:", StringComparison.OrdinalIgnoreCase) =>
                        "spiraAtlas.SearchDetails(\"domain:parser-target-text\") -> rawParsedFile",
                    _ when Entry.Kind == AiBibleEntryKind.Atlas => "spiraAtlas.SearchDetails(query) -> evidenceSummary",
                    _ when Entry.Kind == AiBibleEntryKind.Pattern => "readOnlyPattern(context) -> understanding",
                    _ => "productGuardrail(context) -> doNotAuthorYet",
                };
            }
        }

        public string Pseudocode
        {
            get
            {
                string name = Title.Split(" (0x", StringSplitOptions.None)[0];
                return Entry.Kind switch
                {
                    AiBibleEntryKind.Function => $"// native ATEL call\n{NormalizedSignature};",
                    AiBibleEntryKind.Field => $"fieldId = {ExtractHexOrId()};\nvalue = readChrProperty(Self, fieldId);",
                    AiBibleEntryKind.Target => $"targetRef = {ExtractHexOrId()};\n// use before performCommand/forcePerformCommand",
                    AiBibleEntryKind.Opcode => $"emit({name}, operand);\n// validate stack and jumps before save",
                    AiBibleEntryKind.Command => $"targetRef = Self;\ncommandId = {ExtractHexOrId()};\nperformCommand(targetRef, commandId);",
                    AiBibleEntryKind.Atlas => $"// read-only corpus map\nentry = SpiraDataAtlas.SearchDetails(\"{name}\");",
                    AiBibleEntryKind.Pattern => $"// pattern, not a public button yet\ninspect(\"{name}\");\nvalidateWithAiScriptLab();",
                    _ => $"// guardrail\nblockAuthoringUntilEvidenceCloses(\"{name}\");",
                };
            }
        }

        string ExtractHexOrId()
        {
            int at = Title.IndexOf("0x", StringComparison.OrdinalIgnoreCase);
            if (at >= 0 && at + 6 <= Title.Length)
                return Title.Substring(at, 6);
            return Id;
        }

        static IReadOnlyList<HighlightRun> BuildRuns(string text, IReadOnlyList<string> terms, IBrush normalBrush)
        {
            if (string.IsNullOrEmpty(text))
                return Array.Empty<HighlightRun>();
            if (terms.Count == 0)
                return new[] { new HighlightRun(text, TransparentBrush, normalBrush) };

            List<HighlightRun> runs = new();
            int index = 0;
            while (index < text.Length)
            {
                int nextIndex = -1;
                string? nextTerm = null;
                foreach (string term in terms)
                {
                    int found = text.IndexOf(term, index, StringComparison.OrdinalIgnoreCase);
                    if (found < 0) continue;
                    if (nextIndex < 0 || found < nextIndex || (found == nextIndex && term.Length > (nextTerm?.Length ?? 0)))
                    {
                        nextIndex = found;
                        nextTerm = term;
                    }
                }

                if (nextIndex < 0 || nextTerm == null)
                {
                    runs.Add(new HighlightRun(text[index..], TransparentBrush, normalBrush));
                    break;
                }

                if (nextIndex > index)
                    runs.Add(new HighlightRun(text[index..nextIndex], TransparentBrush, normalBrush));

                runs.Add(new HighlightRun(text.Substring(nextIndex, nextTerm.Length), HighlightBrush, NormalTextBrush));
                index = nextIndex + nextTerm.Length;
            }

            return runs;
        }
    }

    internal sealed class HighlightRun
    {
        public HighlightRun(string text, IBrush background, IBrush foreground)
        {
            Text = text;
            Background = background;
            Foreground = foreground;
        }

        public string Text { get; }
        public IBrush Background { get; }
        public IBrush Foreground { get; }
    }
}
