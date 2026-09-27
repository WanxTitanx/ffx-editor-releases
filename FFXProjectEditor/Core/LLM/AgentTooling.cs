using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FFXProjectEditor.Diagnostics;

namespace FFXProjectEditor.Core.LLM;

/// <summary>
/// Agent-mode tool surface (Jarvis-UI, 2026-09-15). Every tool is READ-ONLY and
/// PathGuard-confined to the workspace root — the model can inspect files but
/// cannot write. The single "mutating" tool, <c>submit_proposal</c>, only parses
/// + validates a <see cref="PatchProposal"/> through <see cref="LlmGuard"/> and
/// raises <see cref="AgentToolContext.ProposalSubmitted"/>; the real write path
/// stays ProposalToPlanMapper → human approval → OperationExecutorV2.
///
/// Budgets per agent turn (prompt-injection containment): tool calls, aggregate
/// tool output, and per-read byte counts are all capped — a hostile or looping
/// model burns a bounded amount of work, then gets a plain "budget exceeded".
/// </summary>
public sealed class AgentToolContext
{
    /// <summary>Workspace root (extracted master). Every path arg resolves under it.</summary>
    public required string WorkspaceRoot { get; init; }

    /// <summary>Output root (deploy target) — surfaced in system context only.</summary>
    public string? OutputRoot { get; init; }

    /// <summary>Session settings — submit_proposal validates against them via LlmGuard.</summary>
    public required LlmSessionSettings Settings { get; init; }

    // ── Per-turn budgets (reset by the runner before each user command) ──
    public const int MaxToolCallsPerTurn = 24;
    public const int MaxToolOutputCharsPerTurn = 48_000;
    public const int MaxReadBytesPerCall = 1024;
    public const int MaxEntriesPerList = 200;
    public int ToolCallsUsed;
    public int ToolOutputChars;

    /// <summary>Raised when a submitted proposal passes LlmGuard — the UI enqueues it for review.</summary>
    public event Action<PatchProposal>? ProposalAccepted;

    internal void RaiseProposalAccepted(PatchProposal p) => ProposalAccepted?.Invoke(p);
}

/// <summary>Registry of agent tools: specs for the request + local executors.</summary>
public sealed class AgentToolRegistry
{
    const string Area = "AiAssistant.Tools";

    static readonly JsonSerializerOptions ArgsJson = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Spec list sent in the request (OpenAI tools array).</summary>
    public IReadOnlyList<LlmToolSpec> Specs { get; } = new List<LlmToolSpec>
    {
        new()
        {
            Name = "list_dir",
            Description = "List entries of a directory inside the workspace (relative path; '.' = workspace root).",
            ParametersJson = "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"relative dir path, default '.'\"}}}",
        },
        new()
        {
            Name = "find_files",
            Description = "Find files under the workspace whose name contains the pattern (case-insensitive; '*' acts as wildcard).",
            ParametersJson = "{\"type\":\"object\",\"properties\":{\"pattern\":{\"type\":\"string\"},\"path\":{\"type\":\"string\",\"description\":\"relative dir to search, default '.'\"}},\"required\":[\"pattern\"]}",
        },
        new()
        {
            Name = "read_bytes",
            Description = "Read a byte range of a workspace file as hex+ascii (max 1024 bytes per call).",
            ParametersJson = "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"offset\":{\"type\":\"integer\"},\"length\":{\"type\":\"integer\"}},\"required\":[\"path\",\"offset\",\"length\"]}",
        },
        new()
        {
            Name = "sha256_file",
            Description = "Compute the SHA-256 hash and size of a workspace file. REQUIRED before any submit_proposal (the BeforeHash must be the file's real hash).",
            ParametersJson = "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"}},\"required\":[\"path\"]}",
        },
        new()
        {
            Name = "describe_file",
            Description = "Parse a known workspace file into named fields (monster files m###.bin expose stats/loot). Unknown formats fall back to size+sha256+header hex.",
            ParametersJson = "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"}},\"required\":[\"path\"]}",
        },
        new()
        {
            Name = "list_recipes",
            Description = "List the proven patch recipes (recipeId) and which have an executable writer adapter. submit_proposal only works with executable recipes.",
            ParametersJson = "{\"type\":\"object\",\"properties\":{}}",
        },
        new()
        {
            Name = "submit_proposal",
            Description = "Submit a PatchProposal JSON for human review. The target file must exist; beforeHash must equal the real sha256 (use sha256_file first). afterHash is computed locally (send \"pending-stage\") — a model cannot predict SHA-256. Nothing is written — a human approves or rejects it.",
            ParametersJson = "{\"type\":\"object\",\"properties\":{\"proposal\":{\"type\":\"object\",\"description\":\"PatchProposal JSON object\"}},\"required\":[\"proposal\"]}",
        },
    };

    /// <summary>Execute a tool call locally. Always returns bounded, redacted text.</summary>
    public string Execute(LlmToolCall call, AgentToolContext ctx)
    {
        if (ctx.ToolCallsUsed >= AgentToolContext.MaxToolCallsPerTurn)
            return "error: tool-call budget exceeded for this turn";
        ctx.ToolCallsUsed++;

        string result;
        try
        {
            result = call.Name switch
            {
                "list_dir" => ListDir(call.ArgumentsJson, ctx),
                "find_files" => FindFiles(call.ArgumentsJson, ctx),
                "read_bytes" => ReadBytes(call.ArgumentsJson, ctx),
                "sha256_file" => Sha256File(call.ArgumentsJson, ctx),
                "describe_file" => DescribeFile(call.ArgumentsJson, ctx),
                "list_recipes" => ListRecipes(),
                "submit_proposal" => SubmitProposal(call.ArgumentsJson, ctx),
                _ => $"error: unknown tool '{call.Name}'",
            };
        }
        catch (Exception ex)
        {
            result = $"error: {ex.GetType().Name}: {ex.Message}";
            DebugLog.Warn(Area, $"tool {call.Name} failed: {ex.GetType().Name}");
        }

        // Per-turn output budget + redaction (a tool result is model input — never leak secrets).
        result = LlmRedactor.Redact(result);
        int remaining = AgentToolContext.MaxToolOutputCharsPerTurn - ctx.ToolOutputChars;
        if (remaining <= 0) return "error: tool-output budget exceeded for this turn";
        if (result.Length > remaining) result = result[..remaining] + "\n[truncated: turn output budget]";
        ctx.ToolOutputChars += result.Length;
        return result;
    }

    // ── Executors ────────────────────────────────────────────────────────────

    static string? ResolveInside(AgentToolContext ctx, string? rel, out string error)
    {
        var check = PathGuard.ValidateSourcePath(ctx.WorkspaceRoot, string.IsNullOrWhiteSpace(rel) ? "." : rel);
        if (!check.IsValid)
        {
            error = "error: path blocked - " + string.Join("; ", check.Errors);
            return null;
        }
        error = string.Empty;
        return Path.GetFullPath(Path.Combine(ctx.WorkspaceRoot, string.IsNullOrWhiteSpace(rel) ? "." : rel));
    }

    static string ListDir(string argsJson, AgentToolContext ctx)
    {
        var args = ParseArgs(argsJson);
        string? full = ResolveInside(ctx, ArgStr(args, "path"), out string err);
        if (full is null) return err;
        if (!Directory.Exists(full)) return $"error: directory not found: {ArgStr(args, "path") ?? "."}";

        var sb = new StringBuilder();
        var entries = Directory.EnumerateFileSystemEntries(full)
            .OrderBy(e => e, StringComparer.OrdinalIgnoreCase)
            .Take(AgentToolContext.MaxEntriesPerList + 1).ToList();
        foreach (string e in entries.Take(AgentToolContext.MaxEntriesPerList))
        {
            string name = Path.GetFileName(e);
            if (Directory.Exists(e)) sb.AppendLine(name + "/");
            else sb.AppendLine($"{name} ({new FileInfo(e).Length:N0} B)");
        }
        if (entries.Count > AgentToolContext.MaxEntriesPerList)
            sb.AppendLine($"[truncated: more than {AgentToolContext.MaxEntriesPerList} entries]");
        return sb.Length == 0 ? "(empty directory)" : sb.ToString();
    }

    static string FindFiles(string argsJson, AgentToolContext ctx)
    {
        var args = ParseArgs(argsJson);
        string pattern = ArgStr(args, "pattern") ?? "";
        if (pattern.Length == 0) return "error: pattern is required";
        string? full = ResolveInside(ctx, ArgStr(args, "path"), out string err);
        if (full is null) return err;
        if (!Directory.Exists(full)) return $"error: directory not found: {ArgStr(args, "path") ?? "."}";

        // '*' wildcards → simple expression match; otherwise case-insensitive 'contains'.
        bool wildcard = pattern.Contains('*');
        var sb = new StringBuilder();
        int found = 0;
        foreach (string f in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(f);
            bool hit = wildcard
                ? FileSystemName.MatchesSimpleExpression(pattern, name, ignoreCase: true)
                : name.Contains(pattern, StringComparison.OrdinalIgnoreCase);
            if (!hit) continue;
            string rel = Path.GetRelativePath(ctx.WorkspaceRoot, f);
            sb.AppendLine($"{rel} ({new FileInfo(f).Length:N0} B)");
            if (++found >= 100) { sb.AppendLine("[truncated: 100 matches]"); break; }
        }
        return found == 0 ? "(no matches)" : sb.ToString();
    }

    static string ReadBytes(string argsJson, AgentToolContext ctx)
    {
        var args = ParseArgs(argsJson);
        string? full = ResolveInside(ctx, ArgStr(args, "path"), out string err);
        if (full is null) return err;
        if (!File.Exists(full)) return $"error: file not found: {ArgStr(args, "path")}";

        long offset = ArgLong(args, "offset") ?? 0;
        long length = Math.Min(ArgLong(args, "length") ?? 256, AgentToolContext.MaxReadBytesPerCall);
        if (offset < 0 || length <= 0) return "error: offset/length must be positive";

        long size = new FileInfo(full).Length;
        if (offset >= size) return $"error: offset {offset} beyond file size {size}";
        length = Math.Min(length, size - offset);

        var buf = new byte[length];
        using (var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            fs.Seek(offset, SeekOrigin.Begin);
            int read = fs.Read(buf, 0, (int)length);
            if (read < buf.Length) Array.Resize(ref buf, read);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"{ArgStr(args, "path")} — size {size:N0} B, range [{offset}..{offset + buf.Length - 1}]");
        for (int i = 0; i < buf.Length; i += 16)
        {
            var slice = buf.AsSpan(i, Math.Min(16, buf.Length - i));
            sb.Append($"{offset + i:X8}  ");
            sb.Append(Convert.ToHexString(slice).ToLowerInvariant());
            for (int j = slice.Length; j < 16; j++) sb.Append("  ");
            sb.Append("  ");
            foreach (byte b in slice) sb.Append(b is >= 32 and < 127 ? (char)b : '.');
            sb.AppendLine();
        }
        return sb.ToString();
    }

    static string Sha256File(string argsJson, AgentToolContext ctx)
    {
        var args = ParseArgs(argsJson);
        string? full = ResolveInside(ctx, ArgStr(args, "path"), out string err);
        if (full is null) return err;
        if (!File.Exists(full)) return $"error: file not found: {ArgStr(args, "path")}";

        using var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        string hash = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
        return $"{ArgStr(args, "path")}: sha256={hash} size={fs.Length:N0} B";
    }

    static string DescribeFile(string argsJson, AgentToolContext ctx)
    {
        var args = ParseArgs(argsJson);
        string? rel = ArgStr(args, "path");
        string? full = ResolveInside(ctx, rel, out string err);
        if (full is null) return err;
        if (!File.Exists(full)) return $"error: file not found: {rel}";

        byte[] data = File.ReadAllBytes(full);
        string name = Path.GetFileName(full);

        // Monster files m###.bin → structured stats/loot (FfxLib parser, read-only).
        if (System.Text.RegularExpressions.Regex.IsMatch(name, @"^m\d{3}\.bin$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            try
            {
                var mon = FfxLib.Monster.Monster_File.Read(data);
                var s = mon.StatSheetFile;
                var loot = mon.LootFile;
                var view = new
                {
                    format = "monster",
                    file = rel,
                    size = data.Length,
                    stats = new
                    {
                        hp = s.Hp, mp = s.Mp, hpOverkill = s.HpOverkill,
                        strength = s.Strength, defense = s.Defense, magic = s.Magic,
                        magicDefense = s.MagicDefense, agility = s.Agility, luck = s.Luck,
                        evasion = s.Evasion, accuracy = s.Accuracy, poisonDamage = s.PoisonDamage,
                    },
                    loot = new { gil = loot.Gil, ap = loot.Ap, apOverkill = loot.ApOverkill },
                    // Absolute file offsets measured against the REAL serialized layout —
                    // the model must NOT guess these from byte dumps.
                    statOffsets = MeasureMonsterStatOffsets(mon),
                    // Exact contract for a stat change — everything the proposal needs.
                    patchContract = new
                    {
                        recipeId = "byte-patch-t1",
                        capabilityId = "byte-patch",
                        operation = "ByteReplace",
                        howTo = "target.offset = statOffsets.<field>.fileOffset, target.length = statOffsets.<field>.width, " +
                                "target.newBytesBase64 = new value as little-endian bytes (base64), target.beforeHash = sha256_file result",
                    },
                };
                return JsonSerializer.Serialize(view);
            }
            catch (Exception ex)
            {
                return $"error: monster parse failed: {ex.GetType().Name} — use read_bytes";
            }
        }

        // Fallback: identity + header bytes — the model can still navigate with read_bytes.
        using (var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            string hash = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
            int head = (int)Math.Min(64, data.Length);
            return JsonSerializer.Serialize(new
            {
                format = "unknown",
                file = rel,
                size = data.Length,
                sha256 = hash,
                headHex = Convert.ToHexString(data.AsSpan(0, head)).ToLowerInvariant(),
            });
        }
    }

    /// <summary>
    /// Measures each scalar stat field's absolute file offset by flipping every bit of
    /// the field (xor all-1s — every byte differs), re-stamping the in-memory section via
    /// WriteSingle, and diffing against <c>OriginalSectionBytes</c>. WHY measure instead of
    /// hardcoding a layout table: Xe.BinaryMapper owns the real serialization order/widths
    /// (enum backing sizes included) — a hand table rots silently, the diff never lies.
    /// Nothing is written to disk; the flip is self-inverse (xor twice restores).
    /// </summary>
    static object MeasureMonsterStatOffsets(FfxLib.Monster.Monster_File mon)
    {
        var sheet = mon.StatSheetFile;
        var orig = sheet.OriginalSectionBytes;
        long baseOff = mon.OriginalHeader.StatSheetPointer;
        if (orig is null || baseOff <= 0)
            return Array.Empty<object>();

        var rows = new List<object>();

        // (name, flip) — flip toggles every bit of the field; calling it again restores.
        void Measure(string name, Action flip, Func<object> getValue)
        {
            object value = getValue();
            flip();
            byte[] stamped;
            try { stamped = sheet.WriteSingle(); }
            finally { flip(); } // xor is self-inverse — restores the parsed value
            if (stamped.Length != orig.Length) return;

            int first = -1, last = -1;
            for (int i = 0; i < orig.Length; i++)
                if (orig[i] != stamped[i]) { if (first < 0) first = i; last = i; }
            if (first < 0) return;

            rows.Add(new
            {
                name,
                fileOffset = baseOff + first,
                offsetHex = $"0x{baseOff + first:X}",
                width = last - first + 1,
                value,
            });
        }

        Measure("hp",           () => sheet.Hp ^= 0xFFFFFFFFu,           () => sheet.Hp);
        Measure("mp",           () => sheet.Mp ^= 0xFFFFFFFFu,           () => sheet.Mp);
        Measure("hpOverkill",   () => sheet.HpOverkill ^= 0xFFFFFFFFu,   () => sheet.HpOverkill);
        Measure("strength",     () => sheet.Strength ^= 0xFF,            () => sheet.Strength);
        Measure("defense",      () => sheet.Defense ^= 0xFF,             () => sheet.Defense);
        Measure("magic",        () => sheet.Magic ^= 0xFF,               () => sheet.Magic);
        Measure("magicDefense", () => sheet.MagicDefense ^= 0xFF,        () => sheet.MagicDefense);
        Measure("agility",      () => sheet.Agility ^= 0xFF,             () => sheet.Agility);
        Measure("luck",         () => sheet.Luck ^= 0xFF,                () => sheet.Luck);
        Measure("evasion",      () => sheet.Evasion ^= 0xFF,             () => sheet.Evasion);
        Measure("accuracy",     () => sheet.Accuracy ^= 0xFF,            () => sheet.Accuracy);
        Measure("poisonDamage", () => sheet.PoisonDamage ^= 0xFF,        () => sheet.PoisonDamage);
        return rows;
    }

    static string ListRecipes()
    {
        var catalog = new WriterAdapterCatalog();
        var adapters = catalog.GetAll().ToDictionary(a => a.CapabilityId, StringComparer.OrdinalIgnoreCase);
        var rows = LlmCapabilityCatalog.Entries.Select(e => new
        {
            recipeId = e.RecipeId,
            capabilityId = e.CapabilityId,
            executable = e.CapabilityId is { } cap && adapters.ContainsKey(cap),
            adapter = e.CapabilityId is { } c1 && adapters.TryGetValue(c1, out var a) ? a.DisplayName : null,
            risk = e.CapabilityId is { } c2 && adapters.TryGetValue(c2, out var a2) ? a2.Risk.ToString() : null,
            description = e.Description,
        });
        return JsonSerializer.Serialize(rows);
    }

    static string SubmitProposal(string argsJson, AgentToolContext ctx)
    {
        var args = ParseArgs(argsJson);
        if (!args.TryGetProperty("proposal", out JsonElement propEl) || propEl.ValueKind != JsonValueKind.Object)
            return "error: 'proposal' object is required";

        PatchProposal proposal;
        try
        {
            // Envelope metadata the model cannot know (session facts) is filled in
            // BEFORE strict deserialization — provider/modelId/promptTemplateVersion/
            // createdAt/proposalId. Unknown fields are still rejected (Disallow) and
            // schema-required content (target/diff/verifications) stays on the model.
            var node = System.Text.Json.Nodes.JsonNode.Parse(propEl.GetRawText()) as System.Text.Json.Nodes.JsonObject
                ?? throw new JsonException("proposal is not an object");
            node["provider"] ??= ctx.Settings.Provider;
            node["modelId"] ??= ctx.Settings.ModelId;
            node["promptTemplateVersion"] ??= "agent-v1";
            node["createdAt"] ??= DateTimeOffset.UtcNow;
            node["proposalId"] ??= "p-" + Guid.NewGuid().ToString("N")[..10];

            proposal = node.Deserialize<PatchProposal>(new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
            }) ?? throw new JsonException("null proposal");
            _ = Convert.FromBase64String(proposal.Target.NewBytesBase64);
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            return "error: proposal is not valid PatchProposal JSON (strict schema; unknown fields rejected)";
        }

        // Guard path: resolve target inside the workspace + real before-hash + capability
        // descriptor — the same EvaluatePreconditions the executor pipeline relies on.
        var path = PathGuard.ValidateSourcePath(ctx.WorkspaceRoot, proposal.Target.RelativePath);
        if (!path.IsValid)
            return "error: target path blocked - " + string.Join("; ", path.Errors);

        string full = Path.GetFullPath(Path.Combine(ctx.WorkspaceRoot, proposal.Target.RelativePath));
        if (!File.Exists(full))
            return $"error: target file not found: {proposal.Target.RelativePath}";

        byte[] orig = File.ReadAllBytes(full);
        string actualBefore = Convert.ToHexString(SHA256.HashData(orig)).ToLowerInvariant();

        // afterHash é um FATO computável, não algo que o modelo possa prever — sem isso o
        // executor rejeitaria qualquer proposta ("staged hash does not match prediction").
        // O tool aplica o ByteReplace em memória e fixa o hash real: a predição passa a
        // verificar de verdade que o staging produziu os bytes pretendidos.
        if (proposal.Operation == PatchOperationKind.ByteReplace)
        {
            byte[] payload = Convert.FromBase64String(proposal.Target.NewBytesBase64);
            long off = proposal.Target.Offset;
            if (off < 0 || off + payload.Length > orig.Length)
                return $"rejected: target range out of bounds — offset {off} + {payload.Length} B > file {orig.Length} B";
            var patched = (byte[])orig.Clone();
            Array.Copy(payload, 0, patched, off, payload.Length);
            if (patched.AsSpan().SequenceEqual(orig))
                return "rejected: proposal is a no-op — payload already matches the current bytes";
            proposal = proposal with
            {
                AfterHash = Convert.ToHexString(SHA256.HashData(patched)).ToLowerInvariant(),
            };
        }

        if (!LlmCapabilityCatalog.TryGetDescriptor(proposal.CapabilityId, out var capability) || capability is null)
            return $"error: unknown capabilityId '{proposal.CapabilityId}' — call list_recipes";

        var guard = LlmGuard.EvaluatePreconditions(proposal, ctx.Settings, capability, ctx.WorkspaceRoot, actualBefore);
        if (!guard.Allowed)
            return $"rejected: {guard.Reason} — {guard.Message}";

        var catalog = new WriterAdapterCatalog();
        if (catalog.Get(proposal.CapabilityId) is null)
            return $"rejected: capability '{proposal.CapabilityId}' has no registered writer adapter — nothing can apply it";

        ctx.RaiseProposalAccepted(proposal);
        return $"accepted: proposal {proposal.ProposalId} queued for human review — nothing is written until the user approves";
    }

    // ── Arg helpers ─────────────────────────────────────────────────────────

    static JsonElement ParseArgs(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return JsonSerializer.Deserialize<JsonElement>("{}");
        try { return JsonSerializer.Deserialize<JsonElement>(json, ArgsJson); }
        catch (JsonException) { return JsonSerializer.Deserialize<JsonElement>("{}"); }
    }

    static string? ArgStr(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object
        && args.TryGetProperty(name, out JsonElement el)
        && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    static long? ArgLong(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object
        && args.TryGetProperty(name, out JsonElement el)
        && el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out long v) ? v : null;
}
