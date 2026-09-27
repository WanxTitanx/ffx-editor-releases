using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.SpiraDataAtlas;
using FFXProjectEditor.Modules.Common;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FFXProjectEditor.Modules.BlitzballPrizeExplorer
{
    // Read-only Blitzball prize explorer. It consumes the already-closed Atlas catalog
    // (SpiraDataAtlasCatalog.BlitzballPrizeDetails, domain "blitzball-prize") and shows the
    // prize-index resolution table honestly: treasure rows are the offline proved-candidate rule
    // (prize+220 -> takara 220..320 -> reward), tech is partial, overdrive is metadata-only, and
    // the per-event prize sites are blocked (runtime/save). No writer, no runtime, no project gate.
    internal partial class BlitzballPrizeExplorer_DataModel : ObservableObject
    {
        readonly List<BlitzballPrizeRow> allRows = new();

        public ObservableCollection<BlitzballPrizeRow> DisplayedPrizes { get; } = new();
        public ObservableCollection<string> StatusOptions { get; } = new(new[] { "All", "proved-candidate", "partial", "metadata-only", "blocked" });
        public ObservableCollection<string> KindOptions { get; } = new(new[] { "All", "treasure", "tech", "overdrive", "site" });

        [ObservableProperty] private string filterText = string.Empty;
        [ObservableProperty] private string selectedStatus = "All";
        [ObservableProperty] private string selectedKind = "All";
        [ObservableProperty] private BlitzballPrizeRow? selectedPrize;
        [ObservableProperty] private AtlasEvidenceInfo? selectedPrizeEvidence;
        [ObservableProperty] private string loadSummary = string.Empty;
        [ObservableProperty] private string ruleBanner = string.Empty;
        [ObservableProperty] private string filterSummary = string.Empty;

        public BlitzballPrizeExplorer_DataModel()
        {
            Load();
        }

        partial void OnFilterTextChanged(string value) => ApplyFilter();
        partial void OnSelectedStatusChanged(string value) => ApplyFilter();
        partial void OnSelectedKindChanged(string value) => ApplyFilter();

        partial void OnSelectedPrizeChanged(BlitzballPrizeRow? value)
        {
            SelectedPrizeEvidence = value != null ? AtlasEvidenceInfo.ForDetail(value.Detail) : null;
        }

        void Load()
        {
            allRows.Clear();
            IReadOnlyList<SpiraDataAtlasDetailEntry> details = SpiraDataAtlasCatalog.BlitzballPrizeDetails;

            string ruleText =
                "REGRA (proved-candidate, offline): prize 0..100 -> takara 220..320 -> reward. " +
                "Fechada por 3 fontes (Fahrenheit blitz_prize.cs + bltz0201 obtainTreasure + bltz0200/0201 Treasure-Label) sobre takara RT0. " +
                "NUNCA RT2/in-game. Premio por liga/torneio/evento = blocked (runtime/save). Tech = partial. Overdrive = metadata-only.";

            foreach (SpiraDataAtlasDetailEntry d in details)
            {
                if (d.Kind == SpiraDataAtlasDetailKind.BlitzballPrizeGuardrail)
                {
                    // The guardrail is the standing honesty banner, not a grid row.
                    ruleText = string.IsNullOrWhiteSpace(d.Summary) ? ruleText : d.Summary;
                    continue;
                }

                BlitzballPrizeRow? row = BuildRow(d);
                if (row != null)
                    allRows.Add(row);
            }

            RuleBanner = ruleText;

            int treasure = allRows.Count(r => r.Kind == "treasure");
            int tech = allRows.Count(r => r.Kind == "tech");
            int overdrive = allRows.Count(r => r.Kind == "overdrive");
            int site = allRows.Count(r => r.Kind == "site");
            LoadSummary = allRows.Count == 0
                ? "Nenhum prize de Blitzball no Atlas (dataset dev work/ ausente neste build)."
                : $"{allRows.Count} prize refs · {treasure} treasure (proved-candidate) · {tech} tech (partial) · {overdrive} overdrive (metadata-only) · {site} script-sites (blocked, runtime).";

            ApplyFilter();
        }

        static BlitzballPrizeRow? BuildRow(SpiraDataAtlasDetailEntry d)
        {
            string status = ResolveStatus(d.Evidence);

            // Script sites: per-event prize is a runtime/save variable -> blocked. No prize id / takara / reward.
            if (d.Id.StartsWith("atlas:blitzball-prize-site:", StringComparison.OrdinalIgnoreCase))
            {
                return new BlitzballPrizeRow
                {
                    PrizeId = "—",
                    TakaraIndex = "—",
                    Reward = ShortenSiteReward(d),
                    Status = status,
                    Kind = "site",
                    Title = d.Title,
                    Summary = d.Summary,
                    DetailText = d.Detail,
                    SearchBlob = $"site {d.Title} {d.Summary} {status}",
                    Detail = d,
                };
            }

            // Catalog rows: atlas:blitzball-prize:{kind}:{prizeDec}
            if (d.Id.StartsWith("atlas:blitzball-prize:", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = d.Id.Split(':');
                if (parts.Length < 4)
                    return null;
                string kind = parts[2];
                if (!int.TryParse(parts[3], out int prizeDec))
                    return null;

                string reward = ExtractReward(d.Title);
                string takara = kind == "treasure" ? (prizeDec + 220).ToString() : "—";

                return new BlitzballPrizeRow
                {
                    PrizeId = $"0x{prizeDec:X2} · {prizeDec}",
                    TakaraIndex = takara,
                    Reward = reward,
                    Status = status,
                    Kind = kind,
                    Title = d.Title,
                    Summary = d.Summary,
                    DetailText = d.Detail,
                    SearchBlob = $"{prizeDec:X2} {prizeDec} {kind} {reward} {takara} {status} {d.Title}",
                    Detail = d,
                };
            }

            return null;
        }

        // Priority scan over the honest Atlas vocabulary present in the Evidence string.
        static string ResolveStatus(string evidence)
        {
            string e = evidence ?? string.Empty;
            if (e.Contains("proved-candidate", StringComparison.OrdinalIgnoreCase)) return "proved-candidate";
            if (e.Contains("partial", StringComparison.OrdinalIgnoreCase)) return "partial";
            if (e.Contains("metadata-only", StringComparison.OrdinalIgnoreCase)) return "metadata-only";
            if (e.Contains("blocked", StringComparison.OrdinalIgnoreCase)) return "blocked";
            return "parser-corpus";
        }

        // Title shape: "Blitzball prize 0xNN -> {reward}" / "... -> tech {name}" / "... -> overdrive {name}".
        static string ExtractReward(string title)
        {
            int arrow = title.IndexOf("-> ", StringComparison.Ordinal);
            string reward = arrow >= 0 ? title.Substring(arrow + 3).Trim() : title;
            if (reward.StartsWith("tech ", StringComparison.Ordinal)) reward = reward.Substring(5);
            else if (reward.StartsWith("overdrive ", StringComparison.Ordinal)) reward = reward.Substring(10);
            return reward.Length == 0 ? "—" : reward;
        }

        static string ShortenSiteReward(SpiraDataAtlasDetailEntry d)
        {
            // Detail second line is "Variable: {prize_variable} (...)" — surface the runtime variable family.
            string variable = "";
            foreach (string line in d.Detail.Split('\n'))
            {
                string t = line.Trim();
                if (t.StartsWith("Variable:", StringComparison.OrdinalIgnoreCase))
                {
                    variable = t.Substring("Variable:".Length).Trim();
                    break;
                }
            }
            return string.IsNullOrEmpty(variable)
                ? "per-event (runtime, blocked)"
                : $"{variable} — per-event (runtime, blocked)";
        }

        void ApplyFilter()
        {
            string text = FilterText.Trim();
            string status = SelectedStatus;
            string kind = SelectedKind;

            DisplayedPrizes.Clear();
            foreach (BlitzballPrizeRow row in allRows)
            {
                if (status != "All" && !string.Equals(row.Status, status, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (kind != "All" && !string.Equals(row.Kind, kind, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (text.Length > 0 && !row.SearchBlob.Contains(text, StringComparison.OrdinalIgnoreCase))
                    continue;
                DisplayedPrizes.Add(row);
            }

            FilterSummary = $"{DisplayedPrizes.Count} de {allRows.Count} prize refs.";
            if (SelectedPrize == null || !DisplayedPrizes.Contains(SelectedPrize))
                SelectedPrize = DisplayedPrizes.FirstOrDefault();
        }
    }

    // Read-only row for the prize grid. Plain POCO — no observable surface, no writer.
    internal sealed class BlitzballPrizeRow
    {
        public string PrizeId { get; init; } = "";
        public string TakaraIndex { get; init; } = "";
        public string Reward { get; init; } = "";
        public string Status { get; init; } = "";
        public string Kind { get; init; } = "";
        public string Title { get; init; } = "";
        public string Summary { get; init; } = "";
        public string DetailText { get; init; } = "";
        public string SearchBlob { get; init; } = "";
        public SpiraDataAtlasDetailEntry Detail { get; init; } = null!;
    }
}
