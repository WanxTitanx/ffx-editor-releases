// BattleEncounterOpenerLab — RT0 / read-only gate for the encounter opener / CTB seed reader.
//
// Proves, on the real per-battle corpus:
//   (1) GOLDEN SEYMOUR: mcyt06_00 detects the proven 4 facts:
//       - AllMonsters.FirstStrike = 1
//       - AllMonsters.CurrentTurnDelay = 0
//       - Monster#01 [0x0015].CurrentTurnDelay = 1
//       - party/reserves CurrentTurnDelay += 2
//   (2) CORPUS CLEAN: the reader never throws on any battle bin.
//       Battles without a recognized opener seed surface honest notes, not failures.
//   (3) CATALOG: recognized opener seeds are grouped by exact write signature.
//   (4) READ-ONLY: the reader never writes (no byte modification).
//
// Exit 0 only if (1)-(2) hold. READ-ONLY: never writes/moves/deletes anything.
//
// Self-contained: links only the dependency-free production reader + ATEL codec.
// Usage:
//   BattleEncounterOpenerLab [btlRoot] [--json verdict.json]
//   BattleEncounterOpenerLab [btlRoot] [--catalog-json catalog.json] [--catalog-md catalog.md]

using System.Text;
using System.Text.Json;
using FFXProjectEditor.FfxLib.Battle;

static string DefaultRoot() => @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\battle\btl";

static string BuildShapeKey(IReadOnlyList<EncounterOpenerWriteRow> writes)
{
    return string.Join(" | ", writes.Select(static w =>
        $"{w.TargetOperand:X4}:{w.FieldId:X4}:{w.Operation}:{w.Value}"));
}

static string BuildShapeLabel(IReadOnlyList<EncounterOpenerWriteRow> writes)
{
    string leadTarget = writes.Count > 0 ? writes[0].TargetKind : "unknown";
    bool hasFirstStrike = writes.Any(static w => w.FieldName == "FirstStrike" && w.Value == 1);
    bool hasAllMonstersDelay0 = writes.Any(static w =>
        w.TargetKind == "AllMonsters" && w.FieldName == "CurrentTurnDelay" && w.Operation == "set" && w.Value == 0);

    int monsterOverrides = writes.Count(static w =>
        w.TargetKind.StartsWith("Monster#", StringComparison.Ordinal) &&
        w.FieldName == "CurrentTurnDelay" &&
        w.Operation == "set");
    int partyAdds = writes.Count(static w =>
        w.TargetKind.StartsWith("Character#", StringComparison.Ordinal) &&
        w.FieldName == "CurrentTurnDelay" &&
        w.Operation == "add");
    int reserveAdds = writes.Count(static w =>
        w.TargetKind == "Reserves" &&
        w.FieldName == "CurrentTurnDelay" &&
        w.Operation == "add");

    var parts = new List<string>();
    parts.Add($"Lead:{leadTarget}");
    if (hasFirstStrike) parts.Add("FirstStrike:on");
    if (hasAllMonstersDelay0) parts.Add("AllMonstersDelay:0");
    if (monsterOverrides > 0) parts.Add($"MonsterOverrides:{monsterOverrides}");
    if (partyAdds > 0) parts.Add($"PartyAdds:+{writes.Where(static w => w.TargetKind.StartsWith("Character#", StringComparison.Ordinal) && w.Operation == "add").First().Value}x{partyAdds}");
    if (reserveAdds > 0) parts.Add($"ReserveAdds:+{writes.Where(static w => w.TargetKind == "Reserves" && w.Operation == "add").First().Value}x{reserveAdds}");

    if (parts.Count == 0)
        parts.Add($"Writes:{writes.Count}");

    return string.Join(" | ", parts);
}

static string BuildAxisSummary(IReadOnlyList<EncounterOpenerWriteRow> writes)
{
    string targets = string.Join(", ", writes.Select(static w => w.TargetDisplay).Distinct());
    string fields = string.Join(", ", writes.Select(static w => w.FieldName).Distinct());
    string ops = string.Join(", ", writes.Select(static w => w.Operation).Distinct());
    return $"targets=[{targets}] · fields=[{fields}] · ops=[{ops}] · writes={writes.Count}";
}

static string BuildPreview(IReadOnlyList<EncounterOpenerWriteRow> writes)
{
    return string.Join("; ", writes.Select(static w => $"{w.TargetDisplay}.{w.FieldName} {(w.Operation == "add" ? "+=" : "=")} {w.Value}"));
}

static string BuildCatalogMarkdown(OpenerCatalogReport report)
{
    var sb = new StringBuilder();
    sb.AppendLine("# Encounter opener / CTB seed catalog");
    sb.AppendLine();
    sb.AppendLine($"- Root: `{report.BtlRoot}`");
    sb.AppendLine($"- Battles scanned: `{report.BattlesScanned}`");
    sb.AppendLine($"- Battles with chunk0: `{report.BattlesWithChunk0}`");
    sb.AppendLine($"- Recognized seeds: `{report.BattlesWithSeed}`");
    sb.AppendLine($"- Shapes: `{report.Shapes.Count}`");
    sb.AppendLine();
    sb.AppendLine("## Shapes");
    sb.AppendLine();

    foreach (OpenerShapeReport shape in report.Shapes.OrderByDescending(static s => s.BattleCount).ThenBy(static s => s.ShapeId, StringComparer.Ordinal))
    {
        sb.AppendLine($"### {shape.ShapeId} — {shape.Label} ({shape.BattleCount} battles)");
        sb.AppendLine();
        sb.AppendLine($"- Axes: {shape.AxisSummary}");
        sb.AppendLine($"- Preview: {shape.Preview}");
        sb.AppendLine($"- Battles: {string.Join(", ", shape.BattleIds)}");
        sb.AppendLine($"- Signature: `{shape.Signature}`");
        sb.AppendLine();
    }

    sb.AppendLine("## Battles");
    sb.AppendLine();
    sb.AppendLine("| Battle | Shape | Writes | Label |");
    sb.AppendLine("| --- | --- | ---: | --- |");
    foreach (OpenerBattleReport battle in report.Battles.OrderBy(static b => b.BattleId, StringComparer.Ordinal))
    {
        sb.AppendLine($"| `{battle.BattleId}` | `{battle.ShapeId}` | {battle.WriteCount} | {battle.ShapeLabel} |");
    }

    return sb.ToString();
}

string root = DefaultRoot();
string? jsonOut = null;
string? catalogJsonOut = null;
string? catalogMdOut = null;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--json" && i + 1 < args.Length) jsonOut = args[++i];
    else if (args[i] == "--catalog-json" && i + 1 < args.Length) catalogJsonOut = args[++i];
    else if (args[i] == "--catalog-md" && i + 1 < args.Length) catalogMdOut = args[++i];
    else if (!args[i].StartsWith("--", StringComparison.Ordinal)) root = args[i];
}

if (!Directory.Exists(root))
{
    Console.Error.WriteLine($"btl root not found: {root}");
    return 2;
}

var fails = new List<string>();
var recognizedBattles = new List<OpenerBattleReport>();

// ---------- (1) GOLDEN SEYMOUR — mcyt06_00 must detect the proven 4 facts ----------
string goldenId = "mcyt06_00";
string goldenPath = Path.Combine(root, goldenId, goldenId + ".bin");
bool goldenPresent = false;
bool goldenPass = false;
int goldenWrites = 0;
string goldenSummary = "(not checked)";

if (!File.Exists(goldenPath))
{
    fails.Add($"GOLDEN {goldenId}: battle bin not found at {goldenPath}");
}
else
{
    goldenPresent = true;
    int goldenFailCountBefore = fails.Count;

    try
    {
        var opener = BattleEncounterOpener_File.ReadFromBattleBin(goldenId, File.ReadAllBytes(goldenPath));
        goldenWrites = opener.Writes.Count;
        goldenSummary = opener.HumanSummary;

        if (opener.Writes.Count < 4)
        {
            fails.Add($"GOLDEN {goldenId}: expected >=4 recognized writes, got {opener.Writes.Count}");
        }
        else
        {
            bool hasAllMonstersFirstStrike = opener.Writes.Any(static w =>
                w.TargetKind == "AllMonsters" && w.FieldName == "FirstStrike" && w.Value == 1 && w.Operation == "set");
            bool hasAllMonstersDelay0 = opener.Writes.Any(static w =>
                w.TargetKind == "AllMonsters" && w.FieldName == "CurrentTurnDelay" && w.Value == 0 && w.Operation == "set");
            bool hasSeymourDelay1 = opener.Writes.Any(static w =>
                w.TargetOperand == 0x0015 && w.TargetKind == "Monster#01" &&
                w.FieldName == "CurrentTurnDelay" && w.Value == 1 && w.Operation == "set");
            bool hasPartyReserveDelayAdd = opener.Writes.Any(static w =>
                w.FieldName == "CurrentTurnDelay" && w.Operation == "add" && w.Value == 2);

            if (!hasAllMonstersFirstStrike)
                fails.Add($"GOLDEN {goldenId}: missing AllMonsters.FirstStrike = 1");
            if (!hasAllMonstersDelay0)
                fails.Add($"GOLDEN {goldenId}: missing AllMonsters.CurrentTurnDelay = 0");
            if (!hasSeymourDelay1)
                fails.Add($"GOLDEN {goldenId}: missing Monster#01 [0x0015].CurrentTurnDelay = 1");
            if (!hasPartyReserveDelayAdd)
                fails.Add($"GOLDEN {goldenId}: missing party/reserves CurrentTurnDelay += 2 (RMW pattern)");
        }
    }
    catch (Exception ex)
    {
        fails.Add($"GOLDEN {goldenId}: reader threw {ex.GetType().Name}: {ex.Message}");
    }

    goldenPass = fails.Count == goldenFailCountBefore;
}

if (goldenPresent && goldenWrites > 0)
{
    Console.WriteLine($"  GOLDEN {goldenId}: {goldenWrites} writes — {goldenSummary}");
}

// ---------- (2) CORPUS CLEAN — reader never throws on any battle bin ----------
int battlesScanned = 0;
int battlesWithChunk0 = 0;
int battlesWithSeed = 0;
int corpusFail = 0;
int readerThrows = 0;
var corpusSamples = new List<string>();

foreach (string dir in Directory.EnumerateDirectories(root))
{
    string id = Path.GetFileName(dir);
    string path = Path.Combine(dir, id + ".bin");
    if (!File.Exists(path)) continue;
    battlesScanned++;

    BattleEncounterOpener_File opener;
    try
    {
        opener = BattleEncounterOpener_File.ReadFromBattleBin(id, File.ReadAllBytes(path));
    }
    catch (Exception ex)
    {
        readerThrows++;
        corpusFail++;
        if (corpusSamples.Count < 20)
            corpusSamples.Add($"{id}: reader threw {ex.GetType().Name}");
        continue;
    }

    if (opener.Chunk0Offset >= 0) battlesWithChunk0++;
    if (!opener.HasRecognizedSeed) continue;

    battlesWithSeed++;
    recognizedBattles.Add(new OpenerBattleReport(
        BattleId: id,
        WriteCount: opener.Writes.Count,
        ShapeSignature: BuildShapeKey(opener.Writes),
        ShapeLabel: BuildShapeLabel(opener.Writes),
        AxisSummary: BuildAxisSummary(opener.Writes),
        Preview: BuildPreview(opener.Writes),
        Summary: opener.HumanSummary,
        Writes: opener.Writes.Select(static w => new OpenerWriteReport(
            w.TargetDisplay,
            w.TargetOperand,
            w.FieldName,
            w.FieldId,
            w.Operation,
            w.Value,
            w.SourceOffset)).ToArray()));
}

bool corpusClean = corpusFail == 0;

// ---------- (3) CATALOG ----------
var shapes = recognizedBattles
    .GroupBy(static b => b.ShapeSignature, StringComparer.Ordinal)
    .Select((group, index) =>
    {
        OpenerBattleReport first = group.First();
        return new OpenerShapeReport(
            ShapeId: $"shape-{index + 1:D2}",
            Signature: group.Key,
            Label: first.ShapeLabel,
            AxisSummary: first.AxisSummary,
            Preview: first.Preview,
            BattleCount: group.Count(),
            BattleIds: group.Select(static b => b.BattleId).OrderBy(static id => id, StringComparer.Ordinal).ToArray());
    })
    .OrderByDescending(static s => s.BattleCount)
    .ThenBy(static s => s.Signature, StringComparer.Ordinal)
    .Select((shape, index) => shape with { ShapeId = $"shape-{index + 1:D2}" })
    .ToArray();

var shapeIdBySignature = shapes.ToDictionary(static s => s.Signature, static s => s.ShapeId, StringComparer.Ordinal);
recognizedBattles = recognizedBattles
    .Select(battle => battle with { ShapeId = shapeIdBySignature[battle.ShapeSignature] })
    .OrderBy(static battle => battle.BattleId, StringComparer.Ordinal)
    .ToList();

var catalog = new OpenerCatalogReport(
    BtlRoot: root,
    BattlesScanned: battlesScanned,
    BattlesWithChunk0: battlesWithChunk0,
    BattlesWithSeed: battlesWithSeed,
    ShapeCount: shapes.Length,
    Battles: recognizedBattles.ToArray(),
    Shapes: shapes);

// ---------- VERDICT ----------
bool pass = goldenPass && corpusClean;

Console.WriteLine("BattleEncounterOpenerLab — encounter opener / CTB seed gate");
Console.WriteLine($"  btl root          : {root}");
Console.WriteLine($"  GOLDEN {goldenId}    : {(goldenPresent ? (goldenPass ? "PASS" : "FAIL") : "MISSING")}  ({goldenWrites} writes)");
Console.WriteLine($"  battles scanned   : {battlesScanned}  ({battlesWithChunk0} with chunk0, {battlesWithSeed} with recognized seed)");
Console.WriteLine($"  shapes cataloged  : {shapes.Length}");
Console.WriteLine($"  corpus clean      : {(corpusClean ? "OK" : "FAIL")}  (reader throws {readerThrows})");

if (shapes.Length > 0)
{
    Console.WriteLine("  top shapes:");
    foreach (OpenerShapeReport shape in shapes.Take(10))
        Console.WriteLine($"    {shape.ShapeId}  x{shape.BattleCount}  {shape.Label}");
}

if (corpusSamples.Count > 0)
{
    Console.WriteLine("  samples:");
    foreach (string sample in corpusSamples) Console.WriteLine($"    {sample}");
}

if (fails.Count > 0)
{
    Console.WriteLine($"  FAILS ({fails.Count}):");
    foreach (string fail in fails.Take(40)) Console.WriteLine($"    {fail}");
}

Console.WriteLine(pass
    ? "VERDICT: PASS — golden Seymour seed recognized, corpus scan clean (no reader throws)."
    : "VERDICT: FAIL — see FAILS above.");

if (jsonOut != null)
{
    var verdict = new
    {
        btlRoot = root,
        goldenPresent,
        goldenPass,
        goldenWrites,
        goldenSummary,
        battlesScanned,
        battlesWithChunk0,
        battlesWithSeed,
        shapesCataloged = shapes.Length,
        corpusClean,
        corpusFail,
        readerThrows,
        pass,
        fails = fails.Take(200).ToArray(),
    };
    File.WriteAllText(jsonOut, JsonSerializer.Serialize(verdict, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"wrote {jsonOut}");
}

if (catalogJsonOut != null)
{
    File.WriteAllText(catalogJsonOut, JsonSerializer.Serialize(catalog, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"wrote {catalogJsonOut}");
}

if (catalogMdOut != null)
{
    File.WriteAllText(catalogMdOut, BuildCatalogMarkdown(catalog), Encoding.UTF8);
    Console.WriteLine($"wrote {catalogMdOut}");
}

return pass ? 0 : 1;

sealed record OpenerWriteReport(
    string TargetDisplay,
    ushort TargetOperand,
    string FieldName,
    ushort FieldId,
    string Operation,
    short Value,
    int SourceOffset);

sealed record OpenerBattleReport(
    string BattleId,
    int WriteCount,
    string ShapeSignature,
    string ShapeLabel,
    string AxisSummary,
    string Preview,
    string Summary,
    IReadOnlyList<OpenerWriteReport> Writes)
{
    public string ShapeId { get; init; } = "(unassigned)";
}

sealed record OpenerShapeReport(
    string ShapeId,
    string Signature,
    string Label,
    string AxisSummary,
    string Preview,
    int BattleCount,
    IReadOnlyList<string> BattleIds);

sealed record OpenerCatalogReport(
    string BtlRoot,
    int BattlesScanned,
    int BattlesWithChunk0,
    int BattlesWithSeed,
    int ShapeCount,
    IReadOnlyList<OpenerBattleReport> Battles,
    IReadOnlyList<OpenerShapeReport> Shapes);
