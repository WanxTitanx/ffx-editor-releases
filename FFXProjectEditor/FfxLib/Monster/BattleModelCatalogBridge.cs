// ============================================================================
// BattleModelCatalogBridge — maps Monster_StatSheet.ModelId to Model Viewer catalog ids
// PURPOSE : best-effort ModelId -> catalog id (m###/c001/s001...) for party, aeons and monsters from the
//           loaded project corpus, with a 2-minute cached rebuild.
// WHY     : the Model Viewer needs a catalog id to display a model, but battle only stores a raw ModelId;
//           party/aeon ids are fixed (c00x / s00x), monsters are resolved by scanning the project's m### files.
// EVIDENCE: known fixed table for party (1..8) + aeons (0x3001..0x3017) compiled from the viewer catalog.
// MAINT   : RebuildMonsterIndex scans slots 0..999 via Project_Service.GetPathMon; it's cheap-cached (2 min) —
//           call with force=true after the corpus changes. Unknown ModelId => not mapped (caller falls back).
// ============================================================================
using FFXProjectEditor.Files;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.IO;

namespace FFXProjectEditor.FfxLib.Monster
{
    /// <summary>
    /// Maps battle <see cref="Monster_StatSheet.ModelId"/> values to Model Viewer catalog ids (<c>m020</c>, <c>c001</c>, <c>s001</c>).
    /// Best-effort: party/aeon ids are fixed; monsters are indexed from the loaded project corpus.
    /// </summary>
    public static class BattleModelCatalogBridge
    {
        static readonly Dictionary<int, string> FixedCatalog = new()
        {
            [1] = "c001",
            [2] = "c002",
            [3] = "c003",
            [4] = "c004",
            [5] = "c005",
            [6] = "c006",
            [7] = "c007",
            [8] = "c008",
            [0x3001] = "s001", // Valefor
            [0x3002] = "s002", // Ifrit
            [0x3003] = "s003", // Ixion
            [0x3004] = "s004", // Shiva
            [0x3006] = "s006", // Bahamut
            [0x3007] = "s007", // Anima
            [0x3008] = "s008", // Yojimbo
            [0x3009] = "s010", // Cindy
            [0x300A] = "s011", // Sandy
            [0x300B] = "s013", // Mindy
            [0x3017] = "s017", // Daigoro
        };

        static readonly Dictionary<int, string> MonsterSlotByModelId = new();
        static DateTime _lastRebuildUtc = DateTime.MinValue;

        public static void RebuildMonsterIndex(bool force = false)
        {
            if (!force && (DateTime.UtcNow - _lastRebuildUtc) < TimeSpan.FromMinutes(2) && MonsterSlotByModelId.Count > 0)
                return;

            MonsterSlotByModelId.Clear();
            if (!Project_Service.Instance.IsProjectLoaded)
            {
                _lastRebuildUtc = DateTime.UtcNow;
                return;
            }

            for (int slot = 0; slot <= 999; slot++)
            {
                try
                {
                    string path = Project_Service.Instance.GetPathMon(slot);
                    if (!File.Exists(path))
                        continue;

                    Monster_File monster = Monster_File.Read(File.ReadAllBytes(path));
                    int modelId = monster.StatSheetFile.ModelId;
                    string catalogId = $"m{slot:D3}";
                    if (!MonsterSlotByModelId.ContainsKey(modelId))
                        MonsterSlotByModelId[modelId] = catalogId;
                }
                catch
                {
                    // Skip unreadable slots — preview bridge stays best-effort.
                }
            }

            _lastRebuildUtc = DateTime.UtcNow;
        }

        public sealed class ModelPreviewPlan
        {
            public required string Model1BattleLabel { get; init; }
            public required string Model2BattleLabel { get; init; }
            public string? CatalogId1 { get; init; }
            public string? CatalogId2 { get; init; }
            public bool UsesFusionPair => !string.IsNullOrWhiteSpace(CatalogId1) && !string.IsNullOrWhiteSpace(CatalogId2);
            public bool SameCatalogAsset => string.Equals(CatalogId1, CatalogId2, StringComparison.OrdinalIgnoreCase);
        }

        public static string? ResolveCatalogId(short modelId, short model2Id, short monsterSlotId)
        {
            ModelPreviewPlan plan = BuildPreviewPlan(modelId, model2Id, monsterSlotId);
            if (!string.IsNullOrWhiteSpace(plan.CatalogId2))
                return plan.CatalogId2;
            if (!string.IsNullOrWhiteSpace(plan.CatalogId1))
                return plan.CatalogId1;
            return null;
        }

        public static ModelPreviewPlan BuildPreviewPlan(short modelId, short model2Id, short monsterSlotId)
        {
            RebuildMonsterIndex();

            string model1Label = FormatBattleLabel(modelId);
            string model2Label = FormatBattleLabel(model2Id);
            string? catalog1 = ResolveSingleCatalogId(modelId);
            string? catalog2 = ResolveSingleCatalogId(model2Id);

            // Prefer the edited monster slot when it likely carries the battle composite mesh.
            if (monsterSlotId >= 0)
            {
                string slotCatalog = $"m{monsterSlotId:D3}";
                if (catalog2 == null)
                    catalog2 = slotCatalog;
                if (catalog1 == null)
                    catalog1 = slotCatalog;
            }

            return new ModelPreviewPlan
            {
                Model1BattleLabel = model1Label,
                Model2BattleLabel = model2Label,
                CatalogId1 = catalog1,
                CatalogId2 = catalog2,
            };
        }

        static string FormatBattleLabel(short modelId)
        {
            if (modelId <= 0)
                return "—";

            string name = BattleModel_Dictionary.ResolveName(modelId);
            return $"0x{modelId:X4} {name}";
        }

        static string? ResolveSingleCatalogId(short modelId)
        {
            if (modelId <= 0)
                return null;

            if (FixedCatalog.TryGetValue(modelId, out string? fixedId))
                return fixedId;

            if (MonsterSlotByModelId.TryGetValue(modelId, out string? monsterId))
                return monsterId;

            return null;
        }

        public static string BuildModelGuidance(short modelId, short model2Id)
        {
            List<string> notes = [];

            if (BattleModel_Dictionary.TryGet(modelId, out BattleModel_Dictionary.Entry? model1))
            {
                if (model1.CrashesGame)
                    notes.Add($"Model1 0x{modelId:X4} ({model1.Name}) is documented as crash-prone.");
                if (model1.IsInvisible)
                    notes.Add($"Model1 0x{modelId:X4} ({model1.Name}) is invisible in battle.");
            }

            if (BattleModel_Dictionary.TryGet(model2Id, out BattleModel_Dictionary.Entry? model2))
            {
                if (model2.IsVariant && model2.MainModelId.HasValue)
                {
                    int mainId = model2.MainModelId.Value;
                    if (modelId != mainId)
                    {
                        string mainName = BattleModel_Dictionary.ResolveName(mainId);
                        notes.Add($"Model2 0x{model2Id:X4} ({model2.Name}) is a variant — set Model1 to 0x{mainId:X4} ({mainName}) and Model2 to 0x{model2Id:X4}.");
                    }
                }

                if (model2.CrashesGame)
                    notes.Add($"Model2 0x{model2Id:X4} ({model2.Name}) is documented as crash-prone.");
            }

            return notes.Count == 0
                ? "Model pair looks consistent with the community catalog."
                : string.Join(" ", notes);
        }
    }
}
