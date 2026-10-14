using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Battle;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Modules.SpiraForgeHub
{
    /// <summary>
    /// SPIRA FORGE — Field Hub v0.1 (Fase 0).
    ///
    /// Espinha de navegacao keyada por field_token. Alimentado 100% offline pelo bridge CSV
    /// (docs/reverse/.../ps3data-map-btlmap-fieldid-bridge.csv, decision_band = field_token_bridge_candidate
    /// -> STRUCTURAL-CANDIDATE, nao confirmado). Selecionar um field publica no FieldContext e dois
    /// consumidores reagem (read-only):
    ///   1) Encounter peek via EncounterTable_File (btl.bin) — join candidato area &lt;-&gt; btl.bin map bucket;
    ///   2) Map deep-link — monta a URL ?map=map/&lt;area&gt;/&lt;field&gt; pro FFXMapViewerWeb.
    /// Nenhum writer, nenhum probe, nenhum jogo vivo.
    /// </summary>
    internal partial class SpiraForgeHub_DataModel : ObservableObject
    {
        // CSV / repo-relative anchors
        const string BridgeCsvFileName = "ps3data-map-btlmap-fieldid-bridge.csv";
        static readonly string BridgeCsvRepoRelative =
            Path.Combine("docs", "reverse", "ps3data_graphs_2026-06-03", BridgeCsvFileName);
        static readonly string MapViewerRepoRelative =
            Path.Combine("RuntimeTools", "FFXMapViewerWeb", "index.html");

        readonly List<FieldRow> allFields = new();

        // Encounter-peek cache (btl.bin lido uma vez, candidato a join por area)
        EncounterTable_File? encounterTable;
        List<string> availableMapBuckets = new();
        bool encounterLoadAttempted;

        public ObservableCollection<FieldRow> Fields { get; } = new();

        [ObservableProperty] private FieldRow? selectedField;
        [ObservableProperty] private string filterText = string.Empty;

        [ObservableProperty]
        private string honestyBanner =
            "STRUCTURAL-CANDIDATE. O bridge field_token -> map/<area>/<field> e candidato (decision_band = " +
            "field_token_bridge_candidate), nao um mapeamento confirmado. Encounter peek e leitura do btl.bin " +
            "via join candidato area <-> map bucket. Tudo offline, read-only — nenhum writer aqui.";

        [ObservableProperty] private string bridgeSummary = "Carregando bridge CSV...";

        [ObservableProperty] private string selectedFieldHeader = "Nenhum field selecionado";
        [ObservableProperty] private string selectedFieldDetail =
            "Escolha um field na lista. Ele vira a selecao compartilhada (FieldContext) e os consumidores reagem.";

        [ObservableProperty] private string mapDeepLinkUrl = "-";
        [ObservableProperty] private string mapDeepLinkStatus =
            "Selecione um field pra montar o deep-link do MapViewer.";

        [ObservableProperty] private string encounterPeekSummary =
            "Selecione um field pra espiar a tabela de encontro (btl.bin).";
        [ObservableProperty] private string encounterPeekDetail = "-";

        // Battles do field (que existem como btl_* no disco) — selecionáveis -> handoff pro Formation Editor.
        public ObservableCollection<string> EncounterBattles { get; } = new();
        [ObservableProperty] private string? selectedEncounterBattle;

        partial void OnSelectedEncounterBattleChanged(string? value)
        {
            // publica na espinha; o Formation Editor lê isto pra pular direto pro battle.
            FieldContext.Instance.SelectBattle(value);
        }

        public SpiraForgeHub_DataModel()
        {
            LoadBridge();
        }

        /******************************************
         * Bridge CSV (field picker)
         ******************************************/

        public void RefreshFromDisk()
        {
            // re-le o bridge E reseta o cache do btl.bin (pega projeto recem-carregado).
            encounterTable = null;
            availableMapBuckets = new();
            encounterLoadAttempted = false;
            LoadBridge();
            if (SelectedField != null)
                ApplyField(SelectedField);
        }

        private void LoadBridge()
        {
            allFields.Clear();

            string? csvPath = ResolveBridgeCsvPath();
            if (csvPath == null)
            {
                BridgeSummary = $"Bridge CSV nao encontrado ({BridgeCsvFileName}). Esperado em SpiraForge\\ ao lado do exe " +
                                "ou em docs/reverse/ps3data_graphs_2026-06-03/.";
                ApplyFilter();
                return;
            }

            int skipped = 0;
            try
            {
                string[] lines = File.ReadAllLines(csvPath);
                // linha 0 = header (domain,entity,area,field_token_base,variant,exact_map_entity,...)
                for (int i = 1; i < lines.Length; i++)
                {
                    FieldRow? row = FieldRow.TryParse(lines[i]);
                    if (row == null) { skipped++; continue; }
                    allFields.Add(row);
                }

                allFields.Sort((a, b) => string.CompareOrdinal(a.FieldToken, b.FieldToken));

                string skippedNote = skipped > 0 ? $" ({skipped} linha(s) degenerada(s) ignorada(s))" : string.Empty;
                BridgeSummary = $"{allFields.Count} fields do bridge{skippedNote} — structural-candidate. " +
                                $"Fonte: {Path.GetFileName(csvPath)}.";
            }
            catch (Exception ex)
            {
                BridgeSummary = $"Falha lendo o bridge CSV: {ex.Message}";
            }

            ApplyFilter();
        }

        partial void OnFilterTextChanged(string value) => ApplyFilter();

        private void ApplyFilter()
        {
            string filter = (FilterText ?? string.Empty).Trim();
            FieldRow? previous = SelectedField;

            IEnumerable<FieldRow> view = allFields;
            if (filter.Length > 0)
            {
                view = allFields.Where(f =>
                    f.FieldToken.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    f.Area.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    f.MapEntity.Contains(filter, StringComparison.OrdinalIgnoreCase));
            }

            Fields.Clear();
            foreach (FieldRow row in view)
                Fields.Add(row);

            // preserva a selecao se ainda visivel
            if (previous != null && Fields.Contains(previous))
                SelectedField = previous;
        }

        /******************************************
         * Selecao -> FieldContext + consumidores
         ******************************************/

        partial void OnSelectedFieldChanged(FieldRow? value)
        {
            if (value == null)
            {
                FieldContext.Instance.ClearField();
                SelectedFieldHeader = "Nenhum field selecionado";
                SelectedFieldDetail = "Escolha um field na lista.";
                MapDeepLinkUrl = "-";
                MapDeepLinkStatus = "Selecione um field pra montar o deep-link do MapViewer.";
                EncounterPeekSummary = "Selecione um field pra espiar a tabela de encontro (btl.bin).";
                EncounterPeekDetail = "-";
                EncounterBattles.Clear();
                SelectedEncounterBattle = null;
                return;
            }

            ApplyField(value);
        }

        private void ApplyField(FieldRow field)
        {
            // 1) publica na espinha compartilhada
            FieldContext.Instance.SelectField(field.FieldToken, field.Area, field.MapEntity);

            SelectedFieldHeader = field.FieldToken;
            SelectedFieldDetail =
                $"area={field.Area} · base={field.FieldTokenBase} · variant={field.Variant} · " +
                $"map={field.MapEntity} · decision_band={field.DecisionBand} (structural-candidate).";

            // 2) consumidor A — map deep-link
            BuildMapDeepLink(field);

            // 3) consumidor B — encounter peek (btl.bin)
            RunEncounterPeek(field);
        }

        /******************************************
         * Consumidor A — Map deep-link (FFXMapViewerWeb)
         ******************************************/

        private void BuildMapDeepLink(FieldRow field)
        {
            MapDeepLinkUrl = $"?map={field.MapEntity}";
            string? index = ResolveMapViewerIndex();
            MapDeepLinkStatus = index != null
                ? "MapViewer encontrado. 'Abrir no MapViewer' carrega o field na geometria HD (read-only)."
                : "MapViewer (RuntimeTools/FFXMapViewerWeb/index.html) nao encontrado — o deep-link continua valido como referencia.";
        }

        /// <summary>Tenta abrir o field no FFXMapViewerWeb. Retorna uma mensagem de status.</summary>
        public string TryOpenInMapViewer()
        {
            if (SelectedField == null)
                return "Nenhum field selecionado.";

            string? index = ResolveMapViewerIndex();
            if (index == null)
                return "MapViewer index.html nao encontrado em RuntimeTools/FFXMapViewerWeb.";

            string url = new Uri(index).AbsoluteUri + $"?map={SelectedField.MapEntity}";
            if (Modules.Common.ViewerShell.ExternalBrowserLauncher.Open(url))
            {
                MapDeepLinkStatus = $"Aberto: {url}";
            }
            else
            {
                MapDeepLinkStatus = $"Falha abrindo o MapViewer: nenhum navegador disponivel.";
            }
            return MapDeepLinkStatus;
        }

        /******************************************
         * Consumidor B — Encounter peek (btl.bin, read-only)
         ******************************************/

        private void RunEncounterPeek(FieldRow field)
        {
            EncounterBattles.Clear();
            SelectedEncounterBattle = null; // reset o handoff de battle quando o field muda

            if (!Project_Service.Instance.IsProjectLoaded)
            {
                EncounterPeekSummary = "Projeto nao carregado.";
                EncounterPeekDetail = "Carregue um master/ pra espiar btl.bin. O field picker (bridge CSV) " +
                                      "funciona offline sem projeto.";
                return;
            }

            if (!EnsureEncounterTableLoaded(out string loadError))
            {
                EncounterPeekSummary = "btl.bin indisponivel.";
                EncounterPeekDetail = loadError;
                return;
            }

            // Join CANDIDATO: bridge area (ex.: "azit") <-> EncounterTable_Entry.Map (bucket do btl.bin).
            // Tambem tenta field_token_base por garantia. Nao e confirmado — e structural-candidate.
            List<EncounterTable_Entry> matches = encounterTable!.Tables.Where(t =>
                MatchesBucket(t.Map, field.Area) || MatchesBucket(t.Map, field.FieldTokenBase)).ToList();

            if (matches.Count == 0)
            {
                EncounterPeekSummary =
                    $"Nenhum bucket de encontro do btl.bin casou com '{field.Area}' (join candidato area <-> map bucket).";
                string sample = availableMapBuckets.Count == 0
                    ? "(btl.bin nao expos nenhum map bucket)"
                    : string.Join(", ", availableMapBuckets.Take(24));
                EncounterPeekDetail = $"Map buckets disponiveis no btl.bin ({availableMapBuckets.Count}): {sample}" +
                                      (availableMapBuckets.Count > 24 ? " ..." : string.Empty);
                return;
            }

            int groups = matches.Sum(t => t.Groups.Count);
            int formations = matches.Sum(t => t.Groups.Sum(g => g.Formations.Count));
            int totalWeight = matches.Sum(t => t.Groups.Sum(g => g.TotalWeight));

            List<int> dangers = matches
                .SelectMany(t => t.Groups.Select(g => g.Danger))
                .Distinct()
                .OrderBy(d => d)
                .ToList();
            string dangerRange = dangers.Count == 0
                ? "-"
                : (dangers.Count == 1 ? $"{dangers[0]}" : $"{dangers.First()}..{dangers.Last()}");

            List<string> battleIds = matches
                .SelectMany(t => t.Groups.SelectMany(g => g.Formations.Select(f => f.BattleId)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(b => b, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Battles que existem como btl_* no disco -> selecionáveis pro handoff Formation Editor.
            // (EncounterIdMapLab: 826/863 BattleIds casam 1:1 com pasta btl_*; os sem pasta ficam de fora.)
            foreach (string bid in battleIds)
            {
                if (File.Exists(Project_Service.Instance.GetPathBattle(bid)))
                    EncounterBattles.Add(bid);
            }

            string buckets = string.Join(", ", matches.Select(t => t.Map).Distinct(StringComparer.OrdinalIgnoreCase));

            EncounterPeekSummary =
                $"{matches.Count} tabela(s) · {groups} grupo(s) · {formations} formacao(oes) — " +
                $"join candidato area <-> btl.bin map.";

            string battleSample = string.Join(", ", battleIds.Take(12)) + (battleIds.Count > 12 ? " ..." : string.Empty);
            EncounterPeekDetail =
                $"bucket(s): {buckets}\n" +
                $"danger: {dangerRange} · peso total: {totalWeight}\n" +
                $"battle ids ({battleIds.Count}): {battleSample}";
        }

        private bool EnsureEncounterTableLoaded(out string error)
        {
            error = string.Empty;
            if (encounterTable != null)
                return true;
            if (encounterLoadAttempted)
            {
                error = "btl.bin nao pode ser lido nesta sessao.";
                return false;
            }

            encounterLoadAttempted = true;
            try
            {
                string path = Project_Service.Instance.Path_KernelEncounterTable;
                if (!File.Exists(path))
                {
                    error = $"btl.bin nao encontrado em {path}.";
                    return false;
                }

                encounterTable = EncounterTable_File.Read(File.ReadAllBytes(path));
                availableMapBuckets = encounterTable.Tables
                    .Select(t => t.Map)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return true;
            }
            catch (Exception ex)
            {
                error = $"Falha lendo btl.bin: {ex.Message}";
                encounterTable = null;
                return false;
            }
        }

        private static bool MatchesBucket(string bucket, string token)
        {
            if (string.IsNullOrEmpty(bucket) || string.IsNullOrEmpty(token))
                return false;
            return bucket.Equals(token, StringComparison.OrdinalIgnoreCase)
                || token.StartsWith(bucket, StringComparison.OrdinalIgnoreCase)
                || bucket.StartsWith(token, StringComparison.OrdinalIgnoreCase);
        }

        /******************************************
         * Resolucao de arquivos repo-relativos
         ******************************************/

        private static string? ResolveBridgeCsvPath()
        {
            // 1) copiado pro output (csproj Content -> SpiraForge\...)
            string copied = Path.Combine(AppContext.BaseDirectory, "SpiraForge", BridgeCsvFileName);
            if (File.Exists(copied))
                return copied;
            // 2) walk-up no dev (acha o repo a partir do bin/)
            return FindUpwards(BridgeCsvRepoRelative);
        }

        private static string? ResolveMapViewerIndex() => FindUpwards(MapViewerRepoRelative);

        private static string? FindUpwards(string relativePath)
        {
            foreach (string start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                if (string.IsNullOrEmpty(start))
                    continue;
                DirectoryInfo? dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, relativePath);
                    if (File.Exists(candidate))
                        return candidate;
                    dir = dir.Parent;
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Uma linha do bridge CSV = um field candidato.
    /// Colunas (10): domain, entity, area, field_token_base, variant, exact_map_entity,
    /// same_area_map_count, same_area_map_samples (pipe-list), assetlinker_functions_sample, decision_band.
    /// </summary>
    internal sealed class FieldRow
    {
        public required string FieldToken { get; init; }      // ex.: "azit03_a"
        public required string Entity { get; init; }          // ex.: "btlmap/azit/azit03_a"
        public required string Area { get; init; }            // ex.: "azit"
        public required string FieldTokenBase { get; init; }  // ex.: "azit03"
        public required string Variant { get; init; }         // ex.: "a"
        public required string MapEntity { get; init; }       // ex.: "map/azit/azit03"
        public required int SameAreaMapCount { get; init; }
        public required string DecisionBand { get; init; }    // ex.: "field_token_bridge_candidate"

        public string DisplayName => FieldToken;
        public string Summary => $"area {Area} · {MapEntity}";
        public string DetailSummary =>
            $"{Entity} · same-area maps: {SameAreaMapCount} · {DecisionBand}";

        public static FieldRow? TryParse(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;

            // CSV sem aspas: split simples em ','. (same_area_map_samples usa '|' internamente, nao ',')
            string[] c = line.Split(',');
            if (c.Length < 10)
                return null;

            string fieldTokenBase = c[3].Trim();
            string variant = c[4].Trim();
            string mapEntity = c[5].Trim();

            // pula linhas degeneradas (ex.: btlmap/hdao_settings.bin sem field/map)
            if (fieldTokenBase.Length == 0 || mapEntity.Length == 0)
                return null;

            string token = variant.Length > 0 ? $"{fieldTokenBase}_{variant}" : fieldTokenBase;
            int.TryParse(c[6].Trim(), out int sameAreaCount);

            return new FieldRow
            {
                FieldToken = token,
                Entity = c[1].Trim(),
                Area = c[2].Trim(),
                FieldTokenBase = fieldTokenBase,
                Variant = variant,
                MapEntity = mapEntity,
                SameAreaMapCount = sameAreaCount,
                DecisionBand = c[9].Trim(),
            };
        }
    }
}
