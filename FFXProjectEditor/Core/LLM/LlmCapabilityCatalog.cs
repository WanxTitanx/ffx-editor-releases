using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.Core.LLM;

/// <summary>
/// Static catalog bridging proven recipeIds (<see cref="LlmGuard.ProvenRecipes"/>) to the
/// CapabilityDescriptors the guard/mapper pipeline needs (Jarvis-UI, 2026-09-15).
///
/// WHY a separate catalog: modules build CapabilityDescriptors ad-hoc for badges and the
/// WorkspaceInspectionResult.Capabilities list is empty — there was no central place mapping
/// recipeId → capabilityId → descriptor. The agent tool surface (list_recipes) and the
/// proposal gate (submit_proposal → MapToPlan) both resolve through here.
///
/// Only recipes whose capability has a REGISTERED IWriterAdapter are executable end-to-end
/// (guard → plan → OperationExecutorV2). The rest stay listed for transparency but are
/// marked non-executable — the model learns not to propose dead ends.
/// </summary>
public static class LlmCapabilityCatalog
{
    /// <summary>Uma linha do catálogo: receita provada → capability que a executa.</summary>
    public sealed record Entry(string RecipeId, string? CapabilityId, string Description);

    /// <summary>recipeId → capabilityId, alinhado a <see cref="LlmGuard.ProvenRecipes"/>.</summary>
    public static readonly IReadOnlyList<Entry> Entries = new List<Entry>
    {
        new("ppp-sclmove-t3",                 null,                          "PPP 16-byte source-linked window (copy-only proof; no writer adapter)"),
        new("ability-command-t1",             "ability-command",             "command.bin/monmagic2.bin grow — AbilityCommandAdapter (RT0)"),
        new("monster-file-t1",                "monster-file",                "monster m###.bin read/write — MonsterFileAdapter (RT0 185/185)"),
        new("monster-stat-sheet-t1",          "monster-stats",               "monster stat block preserve-write — MonsterStatSheetAdapter (RT0)"),
        new("treasure-edit-t1",               "treasure-editor",             "takara.bin edit — TreasureAdapter (RT0)"),
        new("atel-phase-rotation-t1",         "atel-recipe-phase-rotation",  "ATEL script phase rotation — AtelPhaseRotationAdapter (RT0)"),
        new("monster-magic-grow-t1",          null,                          "monmagic2.bin grow — MonsterMagicGrowWriter (no LLM adapter)"),
        new("command-grow-t1",                null,                          "command.bin grow — CommandGrowWriter (no LLM adapter)"),
        new("encounter-table-rebuild-t1",     null,                          "encounter table rebuild (no LLM adapter)"),
        new("formation-slot-patch-t1",        null,                          "16-byte formation slot patch (no LLM adapter)"),
        new("battle-companion-activation-t1", null,                          "m213 host/companion activation (no LLM adapter)"),
        new("monster-capture-flag-t1",        null,                          "capture flag bit-safe write (no LLM adapter)"),
        new("monster-clone-t1",               null,                          "m###.bin → new slot clone (no LLM adapter)"),
        new("sphere-grid-transplant-t1",      null,                          "sphere grid safe transplant (no LLM adapter)"),
        new("byte-patch-t1",                  "byte-patch",                  "generic byte splice (offset+payload) — BytePatchAdapter (perfil 1)"),
    };

    static readonly Dictionary<string, CapabilityDescriptor> Descriptors = BuildDescriptors();

    /// <summary>Resolve o descriptor de um capabilityId registrado (executável).</summary>
    public static bool TryGetDescriptor(string capabilityId, out CapabilityDescriptor? descriptor) =>
        Descriptors.TryGetValue(capabilityId ?? string.Empty, out descriptor);

    static Dictionary<string, CapabilityDescriptor> BuildDescriptors()
    {
        CapabilityDescriptor Make(string id, string domain, string title, string desc, params string[] preconditions) => new()
        {
            Id = id,
            Domain = domain,
            Title = title,
            Description = desc,
            Mode = CapabilityMode.OfflineWriter,
            Evidence = EvidenceLevel.Production, // all five adapters carry RT0 proof
            Platforms = new[] { Platform.PC },
            RequiredDependencies = new[] { "workspace" },
            OptionalDependencies = System.Array.Empty<string>(),
            Risks = new[] { "byte-level patch — review diff before applying" },
            AllowedOperations = new[] { AllowedOperation.Patch, AllowedOperation.Edit },
            ProhibitedOperations = new[] { AllowedOperation.Clone },
            Preconditions = preconditions,
            DocumentationLinks = System.Array.Empty<string>(),
            OwnerAgent = "Jarvis-UI",
        };

        return new Dictionary<string, CapabilityDescriptor>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["ability-command"] = Make("ability-command", "battle", "Ability / Command table",
                "command.bin / monmagic2.bin grow-safe edits", "source file hash matches proposal"),
            ["monster-file"] = Make("monster-file", "battle", "Monster file",
                "m###.bin full read/write", "source file hash matches proposal"),
            ["monster-stats"] = Make("monster-stats", "battle", "Monster stat sheet",
                "stat block preserve-write", "source file hash matches proposal"),
            ["treasure-editor"] = Make("treasure-editor", "field", "Treasure table",
                "takara.bin slot edits", "source file hash matches proposal"),
            ["atel-recipe-phase-rotation"] = Make("atel-recipe-phase-rotation", "script", "ATEL phase rotation",
                "ATEL script phase recipe", "source file hash matches proposal"),
            ["byte-patch"] = Make("byte-patch", "generic", "Byte patch",
                "raw byte splice — offset + payload (perfil 1)", "source file hash matches proposal"),
        };
    }
}
