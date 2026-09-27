// BattleActorOverlayScout
//
// Lab-only analyzer for ffxprobectl dump-bones JSON files. It turns live
// skeleton matrices into actor-space/world-space coordinate candidates for a
// future "label above head" overlay. It does not read or write the game.

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    PrintUsage();
    return args.Length == 0 ? 1 : 0;
}

var inputs = new List<string>();
string? markdownOut = null;
string? jsonOut = null;
float labelLift = 0.35f;

for (int i = 0; i < args.Length; i++)
{
    string a = args[i];
    if (a == "--out" && i + 1 < args.Length) markdownOut = args[++i];
    else if (a == "--json" && i + 1 < args.Length) jsonOut = args[++i];
    else if (a == "--label-lift" && i + 1 < args.Length) labelLift = float.Parse(args[++i], CultureInfo.InvariantCulture);
    else if (a.StartsWith("-", StringComparison.Ordinal))
    {
        Console.Error.WriteLine($"unknown arg: {a}");
        PrintUsage();
        return 1;
    }
    else
    {
        inputs.Add(a);
    }
}

var files = ExpandInputs(inputs).ToList();
if (files.Count == 0)
{
    Console.Error.WriteLine("no dump-bones JSON files found.");
    return 2;
}

var reports = new List<ActorOverlayReport>();
foreach (string file in files)
{
    try
    {
        reports.Add(Analyze(file, labelLift));
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"failed to analyze {file}: {ex.Message}");
        return 3;
    }
}

var corpus = new ActorOverlayCorpusReport(
    DateTimeOffset.Now,
    "lab-only; head anchors are candidates until visually confirmed",
    labelLift,
    reports);

string md = BuildMarkdown(corpus);
Console.WriteLine(md);

if (!string.IsNullOrWhiteSpace(markdownOut))
{
    string? dir = Path.GetDirectoryName(markdownOut);
    if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
    File.WriteAllText(markdownOut, md);
    Console.WriteLine($"wrote {markdownOut}");
}

if (!string.IsNullOrWhiteSpace(jsonOut))
{
    string? dir = Path.GetDirectoryName(jsonOut);
    if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
    File.WriteAllText(jsonOut, JsonSerializer.Serialize(corpus, JsonOptions()));
    Console.WriteLine($"wrote {jsonOut}");
}

return 0;

static ActorOverlayReport Analyze(string path, float labelLift)
{
    using FileStream fs = File.OpenRead(path);
    BoneDump dump = JsonSerializer.Deserialize<BoneDump>(fs, JsonOptions())
        ?? throw new InvalidOperationException("invalid dump JSON");
    if (dump.Frames == null || dump.Frames.Count == 0)
        throw new InvalidOperationException("dump has no frames");

    var boneStats = new List<BonePositionStats>();
    Bounds3 global = Bounds3.Empty;

    for (int bone = 0; bone < dump.BoneCount; bone++)
    {
        var positions = new List<Vec3>();
        foreach (List<List<float>> frame in dump.Frames)
        {
            if (bone >= frame.Count) continue;
            List<float> m = frame[bone];
            if (m.Count < 16) continue;
            Vec3 row = new(m[12], m[13], m[14]);
            Vec3 col = new(m[3], m[7], m[11]);
            Vec3? maybePos = LooksLikePosition(row) ? row : LooksLikePosition(col) ? col : null;
            if (maybePos == null) continue;
            Vec3 pos = maybePos.Value;
            if (!pos.Finite) continue;
            positions.Add(pos);
            global = global.Add(pos);
        }

        if (positions.Count == 0) continue;
        Vec3 avg = Vec3.Average(positions);
        float maxDrift = positions.Max(p => p.DistanceTo(avg));
        boneStats.Add(new BonePositionStats(
            bone,
            positions.Count,
            avg.X, avg.Y, avg.Z,
            positions.Min(p => p.X), positions.Max(p => p.X),
            positions.Min(p => p.Y), positions.Max(p => p.Y),
            positions.Min(p => p.Z), positions.Max(p => p.Z),
            maxDrift));
    }

    if (boneStats.Count == 0)
        throw new InvalidOperationException("no finite bone positions found");

    List<BonePositionStats> top = boneStats
        .OrderByDescending(b => b.AvgY)
        .ThenBy(b => b.MaxDrift)
        .Take(8)
        .ToList();

    BonePositionStats head = top[0];
    Vec3 label = new(head.AvgX, head.AvgY + labelLift, head.AvgZ);
    Vec3 center = global.Center;

    return new ActorOverlayReport(
        Path.GetFullPath(path),
        dump.ModuleBase ?? "",
        dump.InstAddr ?? "",
        dump.Id,
        $"0x{dump.Id:X4}",
        dump.BoneCount,
        dump.Frames.Count,
        global.MinX, global.MinY, global.MinZ,
        global.MaxX, global.MaxY, global.MaxZ,
        center.X, center.Y, center.Z,
        head,
        new LabelAnchor(head.BoneIndex, label.X, label.Y, label.Z, labelLift),
        top);
}

static IEnumerable<string> ExpandInputs(IEnumerable<string> inputs)
{
    foreach (string input in inputs)
    {
        if (Directory.Exists(input))
        {
            foreach (string file in Directory.EnumerateFiles(input, "*.json").OrderBy(p => p))
                yield return file;
        }
        else
        {
            foreach (string file in Directory.EnumerateFiles(
                Path.GetDirectoryName(input) is { Length: > 0 } d ? d : ".",
                Path.GetFileName(input)))
            {
                yield return file;
            }
        }
    }
}

static bool LooksLikePosition(Vec3 v)
{
    if (!v.Finite) return false;
    return Math.Abs(v.X) + Math.Abs(v.Y) + Math.Abs(v.Z) > 0.0001f;
}

static string BuildMarkdown(ActorOverlayCorpusReport corpus)
{
    var sb = new StringBuilder();
    sb.AppendLine("# Battle Actor Overlay Scout");
    sb.AppendLine();
    sb.AppendLine($"Status: {corpus.Status}");
    sb.AppendLine($"Generated: {corpus.GeneratedAt:yyyy-MM-dd HH:mm:ss zzz}");
    sb.AppendLine($"Label lift: {corpus.LabelLift.ToString("0.###", CultureInfo.InvariantCulture)}");
    sb.AppendLine();
    sb.AppendLine("| Actor | Inst | Bones | Frames | BBox center | Head candidate | Label anchor | Drift |");
    sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");
    foreach (ActorOverlayReport r in corpus.Actors)
    {
        sb.Append("| ")
            .Append(r.ActorHex).Append(" | ")
            .Append(r.InstAddr).Append(" | ")
            .Append(r.BoneCount).Append(" | ")
            .Append(r.FrameCount).Append(" | ")
            .Append(FormatVec(r.CenterX, r.CenterY, r.CenterZ)).Append(" | ")
            .Append($"bone {r.HeadCandidate.BoneIndex} {FormatVec(r.HeadCandidate.AvgX, r.HeadCandidate.AvgY, r.HeadCandidate.AvgZ)}").Append(" | ")
            .Append(FormatVec(r.LabelAnchor.X, r.LabelAnchor.Y, r.LabelAnchor.Z)).Append(" | ")
            .Append(r.HeadCandidate.MaxDrift.ToString("0.###", CultureInfo.InvariantCulture))
            .AppendLine(" |");
    }
    sb.AppendLine();
    sb.AppendLine("Notes:");
    sb.AppendLine("- Head candidates are the highest average-Y bones in the captured matrices.");
    sb.AppendLine("- This is enough for 3D label anchors, but not yet enough for screen-space labels.");
    sb.AppendLine("- Screen-space labels still need the runtime view/projection matrix or an equivalent world-to-screen hook.");
    sb.AppendLine();
    foreach (ActorOverlayReport r in corpus.Actors)
    {
        sb.AppendLine($"## {r.ActorHex}");
        sb.AppendLine();
        sb.AppendLine($"Source: `{r.SourcePath}`");
        sb.AppendLine();
        sb.AppendLine("| Rank | Bone | Avg pos | Y range | Max drift |");
        sb.AppendLine("|---:|---:|---:|---:|---:|");
        for (int i = 0; i < r.TopCandidates.Count; i++)
        {
            BonePositionStats b = r.TopCandidates[i];
            sb.Append("| ")
                .Append(i + 1).Append(" | ")
                .Append(b.BoneIndex).Append(" | ")
                .Append(FormatVec(b.AvgX, b.AvgY, b.AvgZ)).Append(" | ")
                .Append(b.MinY.ToString("0.###", CultureInfo.InvariantCulture)).Append("..")
                .Append(b.MaxY.ToString("0.###", CultureInfo.InvariantCulture)).Append(" | ")
                .Append(b.MaxDrift.ToString("0.###", CultureInfo.InvariantCulture))
                .AppendLine(" |");
        }
        sb.AppendLine();
    }
    return sb.ToString();
}

static string FormatVec(float x, float y, float z) =>
    $"({x.ToString("0.###", CultureInfo.InvariantCulture)}, {y.ToString("0.###", CultureInfo.InvariantCulture)}, {z.ToString("0.###", CultureInfo.InvariantCulture)})";

static JsonSerializerOptions JsonOptions() => new()
{
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
};

static void PrintUsage()
{
    Console.WriteLine("usage:");
    Console.WriteLine("  BattleActorOverlayScout <dump-bones.json|dir|glob>... [--out report.md] [--json report.json] [--label-lift 0.35]");
    Console.WriteLine();
    Console.WriteLine("example:");
    Console.WriteLine("  dotnet run --project RuntimeTools/BattleActorOverlayScout/BattleActorOverlayScout.csproj -c Release -- work/actor_overlay/bones_*_3frames.json --out work/actor_overlay/overlay_report.md");
}

sealed class BoneDump
{
    public string? ModuleBase { get; set; }
    public string? InstAddr { get; set; }
    public int Id { get; set; }
    public int BoneCount { get; set; }
    public List<List<List<float>>>? Frames { get; set; }
}

readonly record struct Vec3(float X, float Y, float Z)
{
    public bool Finite => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z);
    public float DistanceTo(Vec3 other)
    {
        float dx = X - other.X;
        float dy = Y - other.Y;
        float dz = Z - other.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }
    public static Vec3 Average(IReadOnlyList<Vec3> values) =>
        new(values.Average(v => v.X), values.Average(v => v.Y), values.Average(v => v.Z));
}

readonly record struct Bounds3(float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ)
{
    public static Bounds3 Empty => new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity, float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
    public Bounds3 Add(Vec3 p) => new(
        MathF.Min(MinX, p.X), MathF.Min(MinY, p.Y), MathF.Min(MinZ, p.Z),
        MathF.Max(MaxX, p.X), MathF.Max(MaxY, p.Y), MathF.Max(MaxZ, p.Z));
    public Vec3 Center => new((MinX + MaxX) * 0.5f, (MinY + MaxY) * 0.5f, (MinZ + MaxZ) * 0.5f);
}

sealed record BonePositionStats(
    int BoneIndex,
    int Samples,
    float AvgX, float AvgY, float AvgZ,
    float MinX, float MaxX,
    float MinY, float MaxY,
    float MinZ, float MaxZ,
    float MaxDrift);

sealed record LabelAnchor(int BoneIndex, float X, float Y, float Z, float Lift);

sealed record ActorOverlayReport(
    string SourcePath,
    string ModuleBase,
    string InstAddr,
    int ActorId,
    string ActorHex,
    int BoneCount,
    int FrameCount,
    float MinX, float MinY, float MinZ,
    float MaxX, float MaxY, float MaxZ,
    float CenterX, float CenterY, float CenterZ,
    BonePositionStats HeadCandidate,
    LabelAnchor LabelAnchor,
    IReadOnlyList<BonePositionStats> TopCandidates);

sealed record ActorOverlayCorpusReport(
    DateTimeOffset GeneratedAt,
    string Status,
    float LabelLift,
    IReadOnlyList<ActorOverlayReport> Actors);
