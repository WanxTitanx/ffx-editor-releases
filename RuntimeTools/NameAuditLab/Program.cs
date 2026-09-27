using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

string root = Directory.GetCurrentDirectory();
string? jsonOut = null;
string? mdOut = null;
bool failUi = args.Contains("--fail-ui");

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--root" && i + 1 < args.Length) root = args[++i];
    else if (args[i] == "--json" && i + 1 < args.Length) jsonOut = args[++i];
    else if (args[i] == "--md" && i + 1 < args.Length) mdOut = args[++i];
}

root = Path.GetFullPath(root);

var rules = new[]
{
    new Rule("raw-field-pt", @"\bcampo\s+0x[0-9A-Fa-f{]", Severity.UiCandidate),
    new Rule("raw-stat-pt", @"\bstat\s+0x[0-9A-Fa-f{]", Severity.UiCandidate),
    new Rule("raw-command-pt", @"\bcomando\s+0x[0-9A-Fa-f{]", Severity.UiCandidate),
    new Rule("raw-command-en", @"\braw\s+\{?[A-Za-z0-9_.]+\:X[0-9]?\}?h\b", Severity.UiCandidate),
    new Rule("hex-h-fallback", @"\{[A-Za-z0-9_.]+\:X[0-9]?\}h", Severity.Review),
    new Rule("hex-label", @"0x\{[A-Za-z0-9_.]+\:X[0-9]?\}", Severity.Review),
    new Rule("unknown-label", @"\bUnknown\b|\bunknown\b|\bUnk[A-Za-z0-9_]*\b", Severity.Review),
    new Rule("generic-id-label", @"\bid\s+\{[A-Za-z0-9_.]+", Severity.Review),
};

var findings = new List<Finding>();
foreach (string file in EnumerateFiles(root))
{
    string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
    string[] lines;
    try { lines = File.ReadAllLines(file); }
    catch { continue; }

    for (int i = 0; i < lines.Length; i++)
    {
        string line = lines[i];
        foreach (Rule rule in rules)
        {
            if (!rule.Regex.IsMatch(line)) continue;
            Category category = Classify(rel, line);
            bool actionable = IsActionable(category, rule.Severity, line);
            findings.Add(new Finding(
                Path: rel,
                Line: i + 1,
                Rule: rule.Id,
                Category: category.ToString(),
                Actionable: actionable,
                Text: line.Trim()));
        }
    }
}

int uiActionable = findings.Count(f => f.Actionable);
int uiCandidate = findings.Count(f => f.Category is "EditorUi" or "CoreDisplay");
int labOrDocs = findings.Count - uiCandidate;

Console.WriteLine("=== NameAuditLab (raw-name burn-down inventory) ===");
Console.WriteLine($"root              : {root}");
Console.WriteLine($"findings          : {findings.Count}");
Console.WriteLine($"editor/display    : {uiCandidate}");
Console.WriteLine($"review now        : {uiActionable}");
Console.WriteLine($"lab/docs/blocked  : {labOrDocs}");

foreach (var group in findings.GroupBy(f => f.Category).OrderBy(g => g.Key))
    Console.WriteLine($"  {group.Key,-16}: {group.Count(),4}  review {group.Count(f => f.Actionable),4}");

if (jsonOut != null)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(jsonOut))!);
    File.WriteAllText(jsonOut, JsonSerializer.Serialize(new
    {
        root,
        generatedAt = DateTimeOffset.Now,
        counts = new
        {
            findings = findings.Count,
            editorDisplay = uiCandidate,
            reviewNow = uiActionable,
            labDocsBlocked = labOrDocs,
        },
        findings,
    }, new JsonSerializerOptions { WriteIndented = true }));
}

if (mdOut != null)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(mdOut))!);
    File.WriteAllText(mdOut, BuildMarkdown(root, findings));
}

return failUi && uiActionable > 0 ? 2 : 0;

IEnumerable<string> EnumerateFiles(string start)
{
    var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".axaml", ".md", ".ps1",
    };
    var skipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".claude", "bin", "obj", "node_modules", "Assets", "work",
        "FFXMagicViewerWeb", "FFXMapViewerWeb", "FFXModelViewerWeb",
    };

    var stack = new Stack<string>();
    stack.Push(start);
    while (stack.Count > 0)
    {
        string dir = stack.Pop();
        IEnumerable<string> subdirs;
        try { subdirs = Directory.EnumerateDirectories(dir); }
        catch { continue; }
        foreach (string sub in subdirs)
        {
            if (skipDirs.Contains(Path.GetFileName(sub))) continue;
            stack.Push(sub);
        }

        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(dir); }
        catch { continue; }
        foreach (string file in files)
            if (exts.Contains(Path.GetExtension(file)))
                yield return file;
    }
}

Category Classify(string rel, string line)
{
    if (rel.StartsWith("FFXProjectEditor/Modules/", StringComparison.OrdinalIgnoreCase)
        || rel.StartsWith("FFXProjectEditor/Controls/", StringComparison.OrdinalIgnoreCase))
        return Category.EditorUi;
    if (rel.StartsWith("FFXProjectEditor/FfxLib/", StringComparison.OrdinalIgnoreCase))
        return LooksUserFacing(line) ? Category.CoreDisplay : Category.CoreInternal;
    if (rel.StartsWith("RuntimeTools/", StringComparison.OrdinalIgnoreCase)
        || rel.StartsWith("FFXProjectEditor/Tools/", StringComparison.OrdinalIgnoreCase))
        return Category.LabTool;
    if (rel.StartsWith("docs/", StringComparison.OrdinalIgnoreCase))
        return Category.Documentation;
    return Category.Other;
}

bool LooksUserFacing(string line) =>
    line.Contains('"') || line.Contains("=>") || line.Contains("return", StringComparison.OrdinalIgnoreCase);

bool IsActionable(Category category, Severity severity, string line)
{
    if (category is not (Category.EditorUi or Category.CoreDisplay)) return false;
    string trimmed = line.TrimStart();
    if (trimmed.StartsWith("//") || trimmed.StartsWith("///") || trimmed.StartsWith("*")) return false;
    if (line.Contains("UnknownOpcode", StringComparison.OrdinalIgnoreCase)) return false;
    if (line.Contains("UnknownOpcodes", StringComparison.OrdinalIgnoreCase)) return false;
    if (line.Contains("DRIFT", StringComparison.OrdinalIgnoreCase)) return false;
    if (line.Contains("debug", StringComparison.OrdinalIgnoreCase)) return false;
    if (severity == Severity.UiCandidate) return true;
    if (line.Contains("Header=\"Unk", StringComparison.OrdinalIgnoreCase)) return true;
    if (line.Contains("Content=\"", StringComparison.OrdinalIgnoreCase)
        && line.Contains("Unk", StringComparison.OrdinalIgnoreCase)) return true;
    return false;
}

string BuildMarkdown(string scanRoot, IReadOnlyList<Finding> all)
{
    var sb = new StringBuilder();
    sb.AppendLine("# FFX Project-Wide Name Burn-Down Scan");
    sb.AppendLine();
    sb.AppendLine($"Root: `{scanRoot}`");
    sb.AppendLine($"Generated: `{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}`");
    sb.AppendLine();
    sb.AppendLine("## Summary");
    sb.AppendLine();
    sb.AppendLine("| bucket | count | review now |");
    sb.AppendLine("| --- | ---: | ---: |");
    foreach (var group in all.GroupBy(f => f.Category).OrderBy(g => g.Key))
        sb.AppendLine($"| `{group.Key}` | {group.Count()} | {group.Count(f => f.Actionable)} |");
    sb.AppendLine();
    sb.AppendLine("## Editor/Display Review Candidates");
    sb.AppendLine();
    sb.AppendLine("| file | line | rule | text |");
    sb.AppendLine("| --- | ---: | --- | --- |");
    foreach (Finding f in all.Where(f => f.Actionable).OrderBy(f => f.Path).ThenBy(f => f.Line).Take(250))
        sb.AppendLine($"| `{f.Path}` | {f.Line} | `{f.Rule}` | {Escape(f.Text)} |");
    sb.AppendLine();
    sb.AppendLine("## Blocked Or Intentional Raw Names");
    sb.AppendLine();
    sb.AppendLine("These are kept as inventory, not automatic rename targets. Runtime labs, debug offsets, byte-diff messages, and reverse docs often need raw hex.");
    sb.AppendLine();
    sb.AppendLine("| file | line | category | rule | text |");
    sb.AppendLine("| --- | ---: | --- | --- | --- |");
    foreach (Finding f in all.Where(f => !f.Actionable).OrderBy(f => f.Category).ThenBy(f => f.Path).ThenBy(f => f.Line).Take(350))
        sb.AppendLine($"| `{f.Path}` | {f.Line} | `{f.Category}` | `{f.Rule}` | {Escape(f.Text)} |");
    return sb.ToString();
}

string Escape(string text) => text.Replace("|", "\\|").Replace("<", "&lt;").Replace(">", "&gt;");

sealed record Rule(string Id, string Pattern, Severity Severity)
{
    public Regex Regex { get; } = new(Pattern, RegexOptions.Compiled);
}

enum Severity { Review, UiCandidate }
enum Category { EditorUi, CoreDisplay, CoreInternal, LabTool, Documentation, Other }
sealed record Finding(string Path, int Line, string Rule, string Category, bool Actionable, string Text);
