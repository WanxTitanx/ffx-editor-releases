// AiScriptLab — RT0 gate for the FFX battle-AI bytecode codec (FfxLib/Ai/AiScript_File.cs).
//
// Proves, on the real monster corpus, that the disassembler round-trips the AiFile blob byte-identically:
//   for each monster_*.bin -> slice AiFile (AiPtr@4..WorkerPtr@8) -> AiScript_File.Read -> Write ->
//   assert byte-identity, and assert the script walk consumed the whole script region (trailer == 0).
//
// Self-contained: links only the dependency-free codec; no editor / Xe.BinaryMapper.
// Usage: AiScriptLab [monsterRoot] [--dump mXXX] [--json out.json]
//   monsterRoot default: D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\battle\mon

using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.Ai.AtelScript;
using FFXProjectEditor.Resources;

static string DefaultRoot() => @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\battle\mon";

string root = DefaultRoot();
string? dumpId = null;
string? jsonOut = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--dump" && i + 1 < args.Length) dumpId = args[++i];
    else if (args[i] == "--json" && i + 1 < args.Length) jsonOut = args[++i];
    else if (!args[i].StartsWith("--")) root = args[i];
}

if (!Directory.Exists(root))
{
    Console.Error.WriteLine($"monster root not found: {root}");
    return 2;
}

var files = Directory.EnumerateFiles(root, "m*.bin", SearchOption.AllDirectories)
    .Where(p => Path.GetFileNameWithoutExtension(p).Length == 4
                && Path.GetFileNameWithoutExtension(p)[0] == 'm'
                && Path.GetFileNameWithoutExtension(p)[1..].All(char.IsDigit))
    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
    .ToList();

// AI Assembler productization gate (validator + command-id + jump-table-grow + guarded-action + templates).
if (args.Contains("--ai2")) return Ai2Gate(files, jsonOut);
// 1-click AUTOMATION gate (AiAutomation: auto-pick add-ability + detect + stack-neutral remove over the corpus).
if (args.Contains("--ai3")) return Ai3Gate(files, jsonOut);
// Branch-sensitive reader RT0: prove the read-only path-aware reader can separate Seymour/Guado branches.
if (args.Contains("--ai-branch-reader-rt0")) return AiBranchReaderRt0(files, root, jsonOut);
// Indirect-dispatch unit RT0: prove Seymour's dispatch rows can be surfaced as structured row-only authoring units.
if (args.Contains("--indirect-dispatch-units-rt0")) return IndirectDispatchUnitsRt0(files, root, jsonOut);
// Seymour complex detector RT0: prove Omnis/Flux/Mortiorchis/Mortibody/reactive families surface as preview read-only units.
if (args.Contains("--seymour-complex-detectors-rt0")) return SeymourComplexDetectorsRt0(files, root, jsonOut);
// Complex-family matrix RT0: freeze the live source-built detector/promotion counts and capability tiers by family.
if (args.Contains("--complex-family-matrix-rt0")) return ComplexFamilyMatrixRt0(files, root, jsonOut);
// Clone fingerprint RT0: emit comparable unit fingerprints for the promoted families and require the clone-validation matrix to carry the same vocabulary.
if (args.Contains("--clone-fingerprint-rt0")) return CloneFingerprintRt0(files, root, jsonOut);
// Flux native threshold writer RT0: prove the family-specific raw patch can retune priv0018/priv001C and round-trip safely.
if (args.Contains("--flux-native-threshold-writer-rt0")) return FluxNativeThresholdWriterRt0(files, root, jsonOut);
// Round-scripted-boss writer RT0: prove the m238 PUSHII command patches can retune the round beats and round-trip safely.
if (args.Contains("--round-scripted-boss-writer-rt0")) return RoundScriptedBossWriterRt0(files, root, jsonOut);
// Anima OD threshold writer RT0: prove the m125 OD threshold patch can retune the gauge and round-trip safely.
if (args.Contains("--anima-od-threshold-writer-rt0")) return AnimaOdThresholdWriterRt0(files, root, jsonOut);
// Omnis elemental cluster writer RT0: prove the m131 elemental cluster patch can retune the barrage and round-trip safely.
if (args.Contains("--omnis-cluster-writer-rt0")) return OmnisClusterWriterRt0(files, root, jsonOut);
// Support accumulator writer RT0: prove the m127 Mortibody score/switch patch can retune the accumulator and round-trip safely.
if (args.Contains("--support-accumulator-writer-rt0")) return SupportAccumulatorWriterRt0(files, root, jsonOut);
// Mortiorchis companion writer RT0: prove the m143 companion gate patch can retune the handoff and round-trip safely.
if (args.Contains("--mortiorchis-writer-rt0")) return MortiorchisWriterRt0(files, root, jsonOut);
// Reactive-sensor Mortiphasm writer RT0: prove the m106/m118/m150/m154 reactive aftermath patch can retune the sensor and round-trip safely.
if (args.Contains("--reactive-sensor-mortiphasm-writer-rt0")) return ReactiveSensorMortiphasmWriterRt0(files, root, jsonOut);
if (args.Contains("--privvar-slot-probe")) return PrivVarSlotProbe(files, root, jsonOut);
// Anima family RT0: prove m125 still surfaces only the narrow payload picker slice and the dedicated UI lane is wired honestly.
if (args.Contains("--anima-family-rt0")) return AnimaFamilyRt0(files, root, jsonOut);
// Host/companion family RT0: prove Flux still exposes the four live units plus the native raw-threshold descriptors and UI lane.
if (args.Contains("--host-companion-family-rt0")) return HostCompanionFamilyRt0(files, root, jsonOut);
// Elemental-cluster family RT0: prove m131 still exposes the three Omnis beats and the dedicated card only rides the narrow row-only popup where promoted slots exist.
if (args.Contains("--elemental-cluster-rt0")) return ElementalClusterRt0(files, root, jsonOut);
// Support-accumulator family RT0: prove m127 still exposes the Mortibody score/switch family and keeps the m290 collision out of the dedicated surface.
if (args.Contains("--support-accumulator-rt0")) return SupportAccumulatorRt0(files, root, jsonOut);
// Reactive-sensor family RT0: prove the mature damageFormula subgroup has a dedicated next-state lane while m106 stays preview-only and m281 stays out.
if (args.Contains("--reactive-sensor-family-rt0")) return ReactiveSensorFamilyRt0(files, root, jsonOut);
// Sub-actor family RT0: freeze the current m288/m289 zeros and the m290 Mortibody collision until a dedicated reconciliation exists.
if (args.Contains("--sub-actor-family-rt0")) return SubActorFamilyRt0(files, root, jsonOut);
// Round-scripted-boss RT0: prove m238 surfaces as the new PreviewReadOnly family without contaminating Seymour or the generic-switch zeros.
if (args.Contains("--round-scripted-boss-rt0")) return RoundScriptedBossRt0(files, root, jsonOut);
// Tonberry camera-routing RT0: prove m223/m224 surface as a rigid PreviewReadOnly family without leaking into authoring or m238.
if (args.Contains("--tonberry-camera-routing-rt0")) return TonberryCameraRoutingRt0(files, root, jsonOut);
// Advanced family-surface popup RT0: prove the dedicated family dialogs route to the right popup and keep m125/m143/m211/m238/m223/m224 wired honestly.
if (args.Contains("--advanced-family-surface-popups-rt0")) return AdvancedFamilySurfacePopupsRt0(files, root, jsonOut);
// Intent-specific popup RT0: prove the Advanced Phase Manager quick actions route to dedicated payload / target / next-state popups.
if (args.Contains("--indirect-dispatch-intent-editors-rt0")) return IndirectDispatchIntentEditorsRt0(files, root, jsonOut);
// BattleExplorer encounter surfaces RT0: prove the explorer wires both opener / CTB seed and companion activation cards.
if (args.Contains("--battleexplorer-encounter-surfaces-rt0")) return BattleExplorerEncounterSurfacesRt0(files, root, jsonOut);
// F1 (P2_RECEITAS_ATEL_2026-07-31.md): dry-run formal por receita — hash-precondição SHA-256,
// antes/depois em 3 camadas (bytes + instruções + resumo) + receipt/recovery, sem tocar disco.
if (args.Contains("--receita-dry-run")) return ReceitaDryRun(files, root, jsonOut, args);
// F3.1: code-grow/shrink com remap completo (entrypoints + jump table + data-section pointers).
if (args.Contains("--code-grow-rt0")) return CodeGrowRt0(files, root, jsonOut);
// F3.2: reescrita de sequencia de alvo (target recipes) via RebuildWithCodeRegion.
if (args.Contains("--target-recipe-writer-rt0")) return TargetRecipeWriterRt0(files, root, jsonOut);
// F5.3 (G-5): badge honesto de decodificacao por opcode - Confirmed no corpus, Hypothesis/Decoded fora.
if (args.Contains("--decode-status-rt0")) return DecodeStatusRt0(files);
// F5.4: cobertura do detector generico de switch dispatch nos 8 monstros do censo.
if (args.Contains("--generic-switch-rt0")) return GenericSwitchRt0(files, root, jsonOut);
// F6.3: round-trip do emitter ATEL L3/L5 - compila programa de alto nivel contra monstros reais
// e prova que o AiFile compilado sobrevive ao codec (Read -> Write byte-identico).
if (args.Contains("--atel-round-trip-rt0")) return AtelRoundTripRt0(files, root, jsonOut);
// Advanced route bundle RT0: prove route-scoped Forbidden Rite and second-cast insertion on Seymour-style indirect consumers.
if (args.Contains("--advanced-route-bundle-rt0")) return AdvancedRouteBundleRt0(files, root, jsonOut);
// Seymour-focused reader RT0: prove DetectActions can surface indirect performCommand sites that dispatch through vars.
if (args.Contains("--seymour-reader-rt0")) return SeymourReaderRt0(files, root, jsonOut);
// RESEARCH scan (#8): histogram the TARGET slot (the push before the command id) over every performCommand site,
// to map the target sentinel landscape (single / all / self / random …). Read-only analysis — no editor feature.
if (args.Contains("--targets")) return TargetScan(files, jsonOut);
// RESEARCH scan (#11/#12): histogram the CALL/CALLPOPA function ids used in compare/branch guards (HP%, turn count)
// so the conditional templates can be built from a corpus-proven idiom rather than guessed.
if (args.Contains("--cond")) return CondScan(files, jsonOut);
// NAME AUDIT: coverage of call ids, saveData variables and function-specific field spaces.
if (args.Contains("--names")) return NameAuditScan(files, jsonOut);
// DIFF INSPECTOR smoke: unchanged monster + one in-memory operand edit, no filesystem mutation.
if (args.Contains("--diff")) return DiffSmoke(files, jsonOut);
// VAR TABLE grow research: append a private variable in-memory, reference it from bytecode, re-read/validate/splice.
if (args.Contains("--var-grow")) return VarGrowGate(files, root, jsonOut);
// VAR FLOW research: read-only proof that vars are used as independent counters/state with comparisons, math, resets, and dependencies.
if (args.Contains("--var-flow")) return VarFlowScan(files, root, jsonOut);
// MULTIVAR GUARD RT0: emit varA/varB boolean guards through AppendGuardedAction over the corpus.
if (args.Contains("--multivar-guard-rt0")) return MultivarGuardRt0(files, root, jsonOut);
// PHASE ROTATION RT0: lower a small phase recipe through AppendGuardedAction, validate, grow-splice and re-read.
if (args.Contains("--phase-rotation-rt0")) return PhaseRotationRt0(files, root, jsonOut);
// PHASE ROUTE CARDS RT0: source-integrity + corpus smoke for the Phase Manager route-card rollout.
if (args.Contains("--phase-route-cards-rt0")) return PhaseRouteCardsRt0(files, root, jsonOut);
// OVERDRIVE GAUGE RT0: append the LAB setup block (show bar + max/current [+mode]), validate, grow-splice and re-read.
if (args.Contains("--overdrive-gauge-rt0")) return OverdriveGaugeRt0(files, root, jsonOut);
// OVERDRIVE AUTHORING RT0: setup + charge sources + clamp + finisher sequence writer, structural only.
if (args.Contains("--overdrive-authoring-rt0")) return OverdriveAuthoringRt0(files, root, jsonOut);
// OVERDRIVE FLOW research: read-only census of setup, current/max reads, charge math, drain/reset and finisher calls.
if (args.Contains("--overdrive-flow")) return OverdriveFlowScan(files, root, jsonOut);
// OVERDRIVE MODE research: map mode ids/names and corpus usage of OverdriveMode (0x0012).
if (args.Contains("--overdrive-modes")) return OverdriveModeResearch(files, root, jsonOut);
// SIN / BIBLE corpus census: read-only atlas for every monster AI, command, field, target, and pattern flag.
if (args.Contains("--sin-census")) return SinCensus(files, root, jsonOut);
// SIN command grimoire: every AI-reachable command/magic categorized for preset authoring.
if (args.Contains("--sin-grimoire")) return SinGrimoire(files, root, jsonOut);

int total = 0, withScript = 0, stub = 0, rt0 = 0, codeClean = 0, noUnknown = 0;
int editLocalized = 0, editReread = 0, workersOk = 0, writeBackOk = 0;
int cfFloatTested = 0, cfFloatOk = 0, cfJumpTested = 0, cfJumpOk = 0;

// true if every byte that differs between a and b lies within [off, off+len) (and lengths match).
static bool DiffOnlyAt(byte[] a, byte[] b, int off, int len)
{
    if (a.Length != b.Length) return false;
    for (int p = 0; p < a.Length; p++)
        if (a[p] != b[p] && (p < off || p >= off + len)) return false;
    return true;
}

static int DiffSmoke(IReadOnlyList<string> files, string? jsonOut)
{
    var scripts = new List<(string Id, byte[] Ai, AiScriptFile Script)>();
    foreach (string path in files)
    {
        byte[] monster = File.ReadAllBytes(path);
        byte[]? ai = AiScript_File.SliceAiFileFromMonster(monster);
        if (ai == null) continue;

        AiScriptFile script;
        try { script = AiScript_File.Read(ai); }
        catch { continue; }
        if (!script.HasScript) continue;

        scripts.Add((Path.GetFileNameWithoutExtension(path), ai, script));
        if (scripts.Count >= 8) break;
    }

    if (scripts.Count < 2)
    {
        Console.WriteLine("=== AiScriptLab --diff smoke ===");
        Console.WriteLine("FAIL: need at least two scripted monsters.");
        return 1;
    }

    var unchanged = scripts[0];
    IReadOnlyList<AiDiffEntry> unchangedDiff = AiScript_Diff.Compare(
        unchanged.Script,
        AiScript_File.Read(unchanged.Ai.ToArray()));
    int unchangedChanged = unchangedDiff.Count(e => e.Type != AiDiffType.Unchanged);

    var editedSource = scripts.Skip(1).FirstOrDefault(s => s.Script.Instructions.Any(i => i.HasOperand));
    if (editedSource.Script == null)
    {
        Console.WriteLine("=== AiScriptLab --diff smoke ===");
        Console.WriteLine("FAIL: no operand-bearing scripted monster found.");
        return 1;
    }

    AiScriptFile edited = AiScript_File.Read(editedSource.Ai.ToArray());
    AiInstruction changed = edited.Instructions.First(i => i.HasOperand);
    changed.Operand = (ushort)(changed.Operand ^ 0x0001);
    AiScriptFile rereadEdited = AiScript_File.Read(AiScript_File.Write(edited));
    IReadOnlyList<AiDiffEntry> editedDiff = AiScript_Diff.Compare(editedSource.Script, rereadEdited);
    int modified = editedDiff.Count(e => e.Type == AiDiffType.Modified);
    int added = editedDiff.Count(e => e.Type == AiDiffType.Added);
    int removed = editedDiff.Count(e => e.Type == AiDiffType.Removed);

    bool pass = unchangedChanged == 0 && modified > 0 && added == 0 && removed == 0;
    Console.WriteLine("=== AiScriptLab --diff smoke ===");
    Console.WriteLine($"unchanged {unchanged.Id}: changed={unchangedChanged}");
    Console.WriteLine($"edited    {editedSource.Id}: modified={modified} added={added} removed={removed} @0x{changed.Offset:X4}");
    Console.WriteLine(pass ? "PASS" : "FAIL");

    if (!string.IsNullOrWhiteSpace(jsonOut))
    {
        File.WriteAllText(jsonOut, JsonSerializer.Serialize(new
        {
            unchanged = new { unchanged.Id, changed = unchangedChanged },
            edited = new { editedSource.Id, modified, added, removed, offset = changed.Offset },
            pass
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    return pass ? 0 : 1;
}

static int VarGrowGate(IReadOnlyList<string> files, string root, string? jsonOut)
{
    int scripted = 0, withPrivateStorage = 0, withFreePrivateSlot = 0;
    int varTableGrowOk = 0, codeReferenceOk = 0, validatorOk = 0, spliceOk = 0;
    int maxVars = 0;
    string maxVarsId = "";
    var samples = new List<object>();
    var failures = new List<string>();

    foreach (string path in files)
    {
        string id = Path.GetFileNameWithoutExtension(path);
        byte[] monster;
        byte[]? aiFile;
        try
        {
            monster = File.ReadAllBytes(path);
            aiFile = AiScript_File.SliceAiFileFromMonster(monster);
        }
        catch (Exception ex)
        {
            failures.Add($"{id}: read/slice failed: {ex.Message}");
            continue;
        }
        if (aiFile == null) continue;

        AiScriptFile script;
        try { script = AiScript_File.Read(aiFile); }
        catch (Exception ex) { failures.Add($"{id}: parse failed: {ex.Message}"); continue; }
        if (!script.HasScript) continue;

        scripted++;
        if (script.Variables.Count > maxVars)
        {
            maxVars = script.Variables.Count;
            maxVarsId = id;
        }

        int maxPrivLen = script.Workers.Count == 0 ? 0 : script.Workers.Max(w => w.PrivateDataLength);
        if (maxPrivLen <= 0) continue;
        withPrivateStorage++;

        HashSet<int> usedPriv = script.Variables
            .Where(v => v.Storage == 0x56)
            .Select(v => v.Slot)
            .ToHashSet();
        int freeSlot = Enumerable.Range(0, Math.Max(0, maxPrivLen / 4))
            .Select(i => i * 4)
            .FirstOrDefault(slot => !usedPriv.Contains(slot), -1);
        if (freeSlot < 0) continue;
        withFreePrivateSlot++;

        int oldVarCount = script.Variables.Count;
        byte[] grown;
        AiScriptFile grownScript;
        try
        {
            grown = AppendVariableTableEntry(script, storage: 0x56, slot: freeSlot, typeId: 1);
            grownScript = AiScript_File.Read(grown);
        }
        catch (Exception ex)
        {
            failures.Add($"{id}: var table grow failed: {ex.Message}");
            continue;
        }

        bool grewClean =
            grownScript.CodeWalkClosedExactly &&
            grownScript.UnknownOpcodes.Count == 0 &&
            grownScript.Variables.Count == oldVarCount + 1 &&
            grownScript.Variables[^1].Storage == 0x56 &&
            grownScript.Variables[^1].Slot == freeSlot &&
            grownScript.ScriptStart == script.ScriptStart + 8 &&
            ExistingVariablesPreserved(script, grownScript) &&
            RelativeInstructionsPreserved(script, grownScript) &&
            AiScript_File.Write(grownScript).AsSpan().SequenceEqual(grown);
        if (!grewClean)
        {
            failures.Add($"{id}: grown AiFile did not preserve parser invariants.");
            continue;
        }
        varTableGrowOk++;

        ushort newVarIndex = (ushort)oldVarCount;
        var proposed = new List<AiInstruction>(grownScript.Instructions)
        {
            Instr(0xAE, 0),             // PUSHII 0
            Instr(0xA0, newVarIndex),   // POPV new var
            Instr(0x9F, newVarIndex),   // PUSHV new var
            Instr(0x59, 0),             // POPI0
            Instr(0x3C, 0),             // RET
        };

        AiValidationReport report = AiValidator.Validate(grownScript, proposed);
        if (report.IsValid) validatorOk++;
        else
        {
            failures.Add($"{id}: validator rejected new-var reference: {report.ToReportString()}");
            continue;
        }

        byte[] withReference;
        AiScriptFile refScript;
        try
        {
            withReference = AiScript_File.Rebuild(grownScript, proposed);
            refScript = AiScript_File.Read(withReference);
        }
        catch (Exception ex)
        {
            failures.Add($"{id}: rebuild with new-var reference failed: {ex.Message}");
            continue;
        }

        bool refsClean =
            refScript.CodeWalkClosedExactly &&
            refScript.UnknownOpcodes.Count == 0 &&
            refScript.Variables.Count == oldVarCount + 1 &&
            refScript.Instructions.Any(i => i.Opcode == 0xA0 && i.Operand == newVarIndex) &&
            refScript.Instructions.Any(i => i.Opcode == 0x9F && i.Operand == newVarIndex);
        if (!refsClean)
        {
            failures.Add($"{id}: rebuilt AiFile did not retain new-var PUSHV/POPV references.");
            continue;
        }
        codeReferenceOk++;

        try
        {
            byte[] splicedMonster = AiScript_File.SpliceAiFileIntoMonsterGrow(monster, withReference);
            byte[]? slicedBack = AiScript_File.SliceAiFileFromMonster(splicedMonster);
            if (slicedBack == null)
            {
                failures.Add($"{id}: grown monster did not slice AI back.");
                continue;
            }
            AiScriptFile back = AiScript_File.Read(slicedBack);
            bool ok = back.CodeWalkClosedExactly &&
                      back.UnknownOpcodes.Count == 0 &&
                      back.Variables.Count == oldVarCount + 1 &&
                      back.Variables[^1].Storage == 0x56 &&
                      back.Variables[^1].Slot == freeSlot &&
                      back.Instructions.Any(i => i.Opcode == 0xA0 && i.Operand == newVarIndex);
            if (!ok)
            {
                failures.Add($"{id}: spliced monster did not preserve grown var/reference.");
                continue;
            }
            spliceOk++;
        }
        catch (Exception ex)
        {
            failures.Add($"{id}: grow-aware monster splice failed: {ex.Message}");
            continue;
        }

        if (samples.Count < 16)
        {
            samples.Add(new
            {
                id,
                relativePath = Path.GetRelativePath(root, path),
                oldVariables = oldVarCount,
                newVariable = $"var[{newVarIndex}] -> priv{freeSlot:X4}",
                privateBytes = maxPrivLen,
                aiLengthBefore = aiFile.Length,
                aiLengthAfterVarGrow = grown.Length,
                aiLengthAfterDeadReference = withReference.Length,
            });
        }
    }

    bool pass = withFreePrivateSlot > 0 &&
                withFreePrivateSlot == varTableGrowOk &&
                withFreePrivateSlot == validatorOk &&
                withFreePrivateSlot == codeReferenceOk &&
                withFreePrivateSlot == spliceOk;

    Console.WriteLine("=== AiScriptLab --var-grow (research: variable table grow + free private slot) ===");
    Console.WriteLine($"scripted monsters              : {scripted}");
    Console.WriteLine($"max variables seen             : {maxVars} ({maxVarsId})");
    Console.WriteLine($"with private storage           : {withPrivateStorage}");
    Console.WriteLine($"with free priv slot in storage : {withFreePrivateSlot}");
    Console.WriteLine($"var table grow re-read RT0     : {varTableGrowOk}/{withFreePrivateSlot}");
    Console.WriteLine($"validator accepts new var refs : {validatorOk}/{withFreePrivateSlot}");
    Console.WriteLine($"PUSHV/POPV new var rebuild     : {codeReferenceOk}/{withFreePrivateSlot}");
    Console.WriteLine($"grow-aware monster splice      : {spliceOk}/{withFreePrivateSlot}");
    Console.WriteLine(pass ? "VERDICT: PASS" : "VERDICT: FAIL");
    if (failures.Count > 0)
    {
        Console.WriteLine("failures:");
        foreach (string failure in failures.Take(25)) Console.WriteLine("  " + failure);
    }

    if (!string.IsNullOrWhiteSpace(jsonOut))
    {
        string? dir = Path.GetDirectoryName(jsonOut);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(jsonOut, JsonSerializer.Serialize(new
        {
            generatedAtUtc = DateTime.UtcNow.ToString("O"),
            root,
            scripted,
            maxVariables = new { id = maxVarsId, count = maxVars },
            withPrivateStorage,
            withFreePrivateSlot,
            varTableGrowOk,
            validatorOk,
            codeReferenceOk,
            spliceOk,
            pass,
            samples,
            failures = failures.Take(100).ToArray(),
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"json={jsonOut}");
    }

    return pass ? 0 : 1;

    static AiInstruction Instr(byte opcode, ushort operand)
    {
        bool hasOperand = AiScript_File.IsOperandBearing(opcode);
        return new AiInstruction
        {
            Offset = -1,
            Opcode = opcode,
            HasOperand = hasOperand,
            Operand = hasOperand ? operand : (ushort)0,
            OperandKind = AiScript_File.OperandKindOf(opcode),
        };
    }

    static bool ExistingVariablesPreserved(AiScriptFile before, AiScriptFile after)
    {
        if (after.Variables.Count < before.Variables.Count) return false;
        for (int i = 0; i < before.Variables.Count; i++)
        {
            AiVariable a = before.Variables[i];
            AiVariable b = after.Variables[i];
            if (a.Index != b.Index || a.Storage != b.Storage || a.Slot != b.Slot || a.TypeId != b.TypeId)
                return false;
        }
        return true;
    }

    static bool RelativeInstructionsPreserved(AiScriptFile before, AiScriptFile after)
    {
        if (before.Instructions.Count != after.Instructions.Count) return false;
        for (int i = 0; i < before.Instructions.Count; i++)
        {
            AiInstruction a = before.Instructions[i];
            AiInstruction b = after.Instructions[i];
            if (a.Offset - before.ScriptStart != b.Offset - after.ScriptStart ||
                a.Opcode != b.Opcode ||
                a.HasOperand != b.HasOperand ||
                a.Operand != b.Operand)
                return false;
        }
        return true;
    }

    static byte[] AppendVariableTableEntry(AiScriptFile script, byte storage, int slot, int typeId)
    {
        byte[] ai = script.OriginalAiFileBytes;
        if (script.Workers.Count == 0)
            throw new InvalidOperationException("script has no workers.");
        int firstDesc = script.Workers[0].DescriptorOffset;
        int varsOff = (int)ReadU32(ai, firstDesc + 0x14);
        int varsEnd = (int)ReadU32(ai, firstDesc + 0x18);
        if (varsOff <= 0 || varsEnd < varsOff || varsEnd > ai.Length)
            throw new InvalidOperationException($"bad var table range 0x{varsOff:X}..0x{varsEnd:X}.");

        const int add = 8;
        int insertion = varsEnd;
        byte[] output = new byte[ai.Length + add];
        Array.Copy(ai, 0, output, 0, insertion);
        WriteU32(output, insertion, ((uint)storage << 24) | ((uint)slot & 0x00FFFFFF));
        WriteU32(output, insertion + 4, (uint)typeId);
        Array.Copy(ai, insertion, output, insertion + add, ai.Length - insertion);

        WriteU32(output, 0x10, ReadU32(output, 0x10) + add); // declared AiFile length
        if (ReadU32(output, 0x30) >= insertion)
            WriteU32(output, 0x30, ReadU32(output, 0x30) + add); // scriptStart

        int workerCount = ReadU16(ai, 0x36);
        for (int worker = 0; worker < workerCount; worker++)
            RelocPointer(output, 0x38 + worker * 4, insertion, add);

        for (int worker = 0; worker < workerCount; worker++)
        {
            int oldDesc = (int)ReadU32(ai, 0x38 + worker * 4);
            int newDesc = oldDesc >= insertion ? oldDesc + add : oldDesc;
            RelocPointer(output, newDesc + 0x14, insertion, add, strict: true); // varsOff (empty table start stays before the new entry)
            RelocPointer(output, newDesc + 0x18, insertion, add); // varsEnd / intPool start
            RelocPointer(output, newDesc + 0x1C, insertion, add); // floatPool
            RelocPointer(output, newDesc + 0x20, insertion, add); // entry table
            RelocPointer(output, newDesc + 0x24, insertion, add); // jump table
        }
        return output;
    }

    static void RelocPointer(byte[] bytes, int offset, int insertion, int delta, bool strict = false)
    {
        if (offset < 0 || offset + 4 > bytes.Length) return;
        uint value = ReadU32(bytes, offset);
        if (strict ? value > insertion : value >= insertion)
            WriteU32(bytes, offset, value + (uint)delta);
    }

    static ushort ReadU16(byte[] bytes, int offset) => (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
    static uint ReadU32(byte[] bytes, int offset) =>
        (uint)(bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24));
    static void WriteU32(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)(value & 0xFF);
        bytes[offset + 1] = (byte)((value >> 8) & 0xFF);
        bytes[offset + 2] = (byte)((value >> 16) & 0xFF);
        bytes[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    static string SafeRel(string path, string root)
    {
        try { return Path.GetRelativePath(root, path); }
        catch { return path; }
    }
}

static int MultivarGuardRt0(IReadOnlyList<string> files, string root, string? jsonOut)
{
    int scripted = 0, candidates = 0, guardShapeOk = 0, appendOk = 0, validateOk = 0, spliceOk = 0, baselineUnknownAccepted = 0;
    var failures = new List<string>();
    var samples = new List<object>();

    foreach (string path in files)
    {
        string id = Path.GetFileNameWithoutExtension(path);
        byte[] monster;
        byte[]? aiFile;
        try
        {
            monster = File.ReadAllBytes(path);
            aiFile = AiScript_File.SliceAiFileFromMonster(monster);
        }
        catch (Exception ex)
        {
            failures.Add($"{id}: read/slice failed: {ex.Message}");
            continue;
        }
        if (aiFile == null) continue;

        AiScriptFile script;
        try { script = AiScript_File.Read(aiFile); }
        catch (Exception ex)
        {
            failures.Add($"{id}: AiFile parse failed: {ex.Message}");
            continue;
        }
        if (!script.HasScript) continue;
        scripted++;
        if (script.Variables.Count < 2) continue;

        AiWorker? worker = AiAutomation.PickCombatWorker(script);
        if (worker == null || worker.Entrypoints.Count == 0) continue;
        int entrypoint = AiAutomation.PickMainEntrypoint(script, worker);
        candidates++;

        IReadOnlyList<AiInstruction> guard;
        try
        {
            guard = AiVarConditionBuilder.BuildJoinedImmediateComparisons(
                0, AiVarCompareOperator.GreaterThan, 0,
                AiConditionJoinOperator.And,
                1, AiVarCompareOperator.Equal, 0);
        }
        catch (Exception ex)
        {
            failures.Add($"{id}: guard build failed: {ex.Message}");
            continue;
        }

        if (!AiVarConditionBuilder.IsStackCleanBooleanGuard(guard, out string reason))
        {
            failures.Add($"{id}: guard shape failed: {reason}");
            continue;
        }
        guardShapeOk++;

        var action = new List<AiInstruction>
        {
            InstrLocal(0xAE, 0), // PUSHII 0
            InstrLocal(0x59, 0), // POPI0, stack-neutral no-op-ish action
        };

        byte[] edited;
        try
        {
            edited = AiScript_File.AppendGuardedAction(script, worker.Index, entrypoint, guard, action);
        }
        catch (Exception ex)
        {
            failures.Add($"{id}: AppendGuardedAction failed: {ex.Message}");
            continue;
        }
        appendOk++;

        if (!AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(
                edited,
                script,
                aiFile.Length,
                out _,
                out string validationReason))
        {
            failures.Add($"{id}: validator rejected edited AiFile: {validationReason}");
            continue;
        }
        if (!string.IsNullOrWhiteSpace(validationReason))
            baselineUnknownAccepted++;
        validateOk++;

        try
        {
            byte[] spliced = AiScript_File.SpliceAiFileIntoMonsterGrow(monster, edited);
            byte[]? slicedBack = AiScript_File.SliceAiFileFromMonster(spliced);
            if (slicedBack == null)
            {
                failures.Add($"{id}: grown monster did not slice AI back.");
                continue;
            }
            AiScriptFile back = AiScript_File.Read(slicedBack);
            bool ok = back.CodeWalkClosedExactly &&
                      back.Workers.Count == script.Workers.Count &&
                      back.UnknownOpcodes.SequenceEqual(script.UnknownOpcodes) &&
                      back.Instructions.Any(i => i.Opcode == 0x02) && // LAND
                      back.Instructions.Any(i => i.Opcode == 0xD7);   // POPXNCJMP
            if (!ok)
            {
                failures.Add($"{id}: spliced monster lost multivar guard shape.");
                continue;
            }
            spliceOk++;
        }
        catch (Exception ex)
        {
            failures.Add($"{id}: grow-aware splice failed: {ex.Message}");
            continue;
        }

        if (samples.Count < 16)
        {
            samples.Add(new
            {
                id,
                relativePath = SafeRelLocal(path, root),
                worker = worker.Index,
                entrypoint,
                left = script.Variables[0].Name,
                right = script.Variables[1].Name,
                guard = $"{script.Variables[0].Name} > 0 AND {script.Variables[1].Name} == 0",
            });
        }
    }

    bool pass = candidates > 0 && guardShapeOk == candidates && appendOk == candidates && validateOk == candidates && spliceOk == candidates;
    Console.WriteLine("=== AiScriptLab --multivar-guard-rt0 (research: human multivar guard emitter) ===");
    Console.WriteLine($"scripted monsters       : {scripted}");
    Console.WriteLine($"candidates >=2 vars     : {candidates}");
    Console.WriteLine($"guard stack-shape ok    : {guardShapeOk}/{candidates}");
    Console.WriteLine($"append guarded action   : {appendOk}/{candidates}");
    Console.WriteLine($"validator accepts       : {validateOk}/{candidates}");
    Console.WriteLine($"baseline unknowns kept  : {baselineUnknownAccepted}");
    Console.WriteLine($"grow-aware splice       : {spliceOk}/{candidates}");
    Console.WriteLine(pass ? "VERDICT: PASS" : "VERDICT: FAIL");
    foreach (object sample in samples.Take(8))
        Console.WriteLine("sample: " + JsonSerializer.Serialize(sample));

    if (!string.IsNullOrWhiteSpace(jsonOut))
    {
        string? dir = Path.GetDirectoryName(jsonOut);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(jsonOut, JsonSerializer.Serialize(new
        {
            generatedAtUtc = DateTime.UtcNow.ToString("O"),
            root,
            scripted,
            candidates,
            guardShapeOk,
            appendOk,
            validateOk,
            baselineUnknownAccepted,
            spliceOk,
            pass,
            samples,
            failures = failures.Take(100).ToArray(),
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"json={jsonOut}");
    }

    return pass ? 0 : 1;

    static AiInstruction InstrLocal(byte opcode, ushort operand)
    {
        bool hasOperand = AiScript_File.IsOperandBearing(opcode);
        return new AiInstruction
        {
            Offset = -1,
            Opcode = opcode,
            HasOperand = hasOperand,
            Operand = hasOperand ? operand : (ushort)0,
            OperandKind = AiScript_File.OperandKindOf(opcode),
        };
    }

    static string SafeRelLocal(string path, string root)
    {
        try { return Path.GetRelativePath(root, path); }
        catch { return path; }
    }
}

static int VarFlowScan(IReadOnlyList<string> files, string root, string? jsonOut)
{
    var compareOps = new HashSet<byte> { 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F };
    var mathOps = new HashSet<byte> { 0x12, 0x14, 0x15, 0x16, 0x17, 0x18 };
    int scripted = 0, withVars = 0, totalVars = 0;
    int varsWithLoad = 0, varsWithStore = 0, varsWithMultiCompare = 0, varsWithSwitch = 0;
    int varsWithResetZero = 0, varsWithMathStore = 0, varsWithInterVarStore = 0;
    int monstersWithInterVar = 0, monstersWithSwitch = 0, monstersWithMultiCompare = 0;
    var examples = new List<object>();

    foreach (string path in files)
    {
        string id = Path.GetFileNameWithoutExtension(path);
        byte[]? aiFile;
        try { aiFile = AiScript_File.SliceAiFileFromMonster(File.ReadAllBytes(path)); }
        catch { continue; }
        if (aiFile == null) continue;

        AiScriptFile script;
        try { script = AiScript_File.Read(aiFile); }
        catch { continue; }
        if (!script.HasScript) continue;
        scripted++;
        if (script.Variables.Count == 0) continue;
        withVars++;
        totalVars += script.Variables.Count;

        var loads = new Dictionary<int, List<AiInstruction>>();
        var stores = new Dictionary<int, List<int>>();
        var compareSites = new Dictionary<int, List<AiInstruction>>();
        var switchSites = new Dictionary<int, List<AiInstruction>>();
        var resetSites = new Dictionary<int, List<AiInstruction>>();
        var mathStores = new Dictionary<int, List<AiInstruction>>();
        var interVarStores = new Dictionary<int, List<(AiInstruction Store, int SourceVar)>>();

        for (int i = 0; i < script.Instructions.Count; i++)
        {
            AiInstruction ins = script.Instructions[i];
            if (ins.Opcode == 0x9F && ins.Operand < script.Variables.Count) // PUSHV
            {
                Add(loads, ins.Operand, ins);
                for (int j = i + 1; j < script.Instructions.Count && j <= i + 6; j++)
                {
                    byte op = script.Instructions[j].Opcode;
                    if (compareOps.Contains(op))
                    {
                        Add(compareSites, ins.Operand, ins);
                        break;
                    }
                    if (op == 0x2C)
                    {
                        Add(switchSites, ins.Operand, ins);
                        break;
                    }
                    if (op == 0x3C || op == 0xD6 || op == 0xD7 || op == 0xB0)
                        break;
                }
            }

            if (ins.Opcode != 0xA0 || ins.Operand >= script.Variables.Count) continue; // POPV
            int target = ins.Operand;
            Add(stores, target, i);
            int start = Math.Max(0, i - 12);
            var window = script.Instructions.Skip(start).Take(i - start).ToList();
            if (window.Count > 0 && window[^1].Opcode == 0xAE && window[^1].Operand == 0)
                Add(resetSites, target, ins);
            if (window.Any(w => mathOps.Contains(w.Opcode)))
                Add(mathStores, target, ins);

            AiInstruction? source = window.LastOrDefault(w => w.Opcode == 0x9F && w.Operand < script.Variables.Count && w.Operand != target);
            if (source != null)
                Add(interVarStores, target, (ins, (int)source.Operand));
        }

        bool monsterInterVar = false;
        bool monsterSwitch = false;
        bool monsterMultiCompare = false;
        foreach (AiVariable v in script.Variables)
        {
            int idx = v.Index;
            bool hasLoad = loads.TryGetValue(idx, out var l) && l.Count > 0;
            bool hasStore = stores.TryGetValue(idx, out var st) && st.Count > 0;
            bool hasMultiCompare = compareSites.TryGetValue(idx, out var cs) && cs.Select(x => x.Offset).Distinct().Take(2).Count() >= 2;
            bool hasSwitch = switchSites.TryGetValue(idx, out var sw) && sw.Count > 0;
            bool hasReset = resetSites.TryGetValue(idx, out var rs) && rs.Count > 0;
            bool hasMathStore = mathStores.TryGetValue(idx, out var ms) && ms.Count > 0;
            bool hasInterVar = interVarStores.TryGetValue(idx, out var iv) && iv.Count > 0;

            if (hasLoad) varsWithLoad++;
            if (hasStore) varsWithStore++;
            if (hasMultiCompare) { varsWithMultiCompare++; monsterMultiCompare = true; }
            if (hasSwitch) { varsWithSwitch++; monsterSwitch = true; }
            if (hasReset) varsWithResetZero++;
            if (hasMathStore) varsWithMathStore++;
            if (hasInterVar) { varsWithInterVarStore++; monsterInterVar = true; }

            if (examples.Count < 28 && (hasMultiCompare || hasSwitch || hasReset || hasMathStore || hasInterVar))
            {
                examples.Add(new
                {
                    id,
                    relativePath = SafeRel(path, root),
                    variable = $"{v.Name} (var[{idx}])",
                    loads = hasLoad ? l!.Count : 0,
                    stores = hasStore ? st!.Count : 0,
                    compareSites = hasMultiCompare ? cs!.Select(x => $"0x{x.Offset:X4}").Take(6).ToArray() : Array.Empty<string>(),
                    switchSites = hasSwitch ? sw!.Select(x => $"0x{x.Offset:X4}").Take(6).ToArray() : Array.Empty<string>(),
                    resetZeroSites = hasReset ? rs!.Select(x => $"0x{x.Offset:X4}").Take(6).ToArray() : Array.Empty<string>(),
                    mathStoreSites = hasMathStore ? ms!.Select(x => $"0x{x.Offset:X4}").Take(6).ToArray() : Array.Empty<string>(),
                    dependsOn = hasInterVar ? iv!.Select(x => $"{script.Variables[x.SourceVar].Name} @0x{x.Store.Offset:X4}").Take(6).ToArray() : Array.Empty<string>(),
                });
            }
        }

        if (monsterInterVar) monstersWithInterVar++;
        if (monsterSwitch) monstersWithSwitch++;
        if (monsterMultiCompare) monstersWithMultiCompare++;
    }

    bool pass = withVars > 0 &&
                varsWithStore > 0 &&
                varsWithMultiCompare > 0 &&
                varsWithResetZero > 0 &&
                varsWithMathStore > 0 &&
                varsWithInterVarStore > 0;

    Console.WriteLine("=== AiScriptLab --var-flow (research: variable rules / counters / dependencies) ===");
    Console.WriteLine($"scripted monsters            : {scripted}");
    Console.WriteLine($"monsters with vars           : {withVars}");
    Console.WriteLine($"total vars declared          : {totalVars}");
    Console.WriteLine($"vars loaded / stored         : {varsWithLoad}/{varsWithStore}");
    Console.WriteLine($"vars with multiple compares  : {varsWithMultiCompare}  (monsters: {monstersWithMultiCompare})");
    Console.WriteLine($"vars used by SWITCH          : {varsWithSwitch}  (monsters: {monstersWithSwitch})");
    Console.WriteLine($"vars reset to zero           : {varsWithResetZero}");
    Console.WriteLine($"vars stored after math       : {varsWithMathStore}");
    Console.WriteLine($"vars stored from other vars  : {varsWithInterVarStore}  (monsters: {monstersWithInterVar})");
    Console.WriteLine(pass ? "VERDICT: PASS" : "VERDICT: FAIL");
    foreach (object example in examples.Take(8))
        Console.WriteLine("example: " + JsonSerializer.Serialize(example));

    if (!string.IsNullOrWhiteSpace(jsonOut))
    {
        string? dir = Path.GetDirectoryName(jsonOut);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(jsonOut, JsonSerializer.Serialize(new
        {
            generatedAtUtc = DateTime.UtcNow.ToString("O"),
            root,
            scripted,
            withVars,
            totalVars,
            varsWithLoad,
            varsWithStore,
            varsWithMultiCompare,
            monstersWithMultiCompare,
            varsWithSwitch,
            monstersWithSwitch,
            varsWithResetZero,
            varsWithMathStore,
            varsWithInterVarStore,
            monstersWithInterVar,
            pass,
            examples,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"json={jsonOut}");
    }

    return pass ? 0 : 1;

    static void Add<T>(Dictionary<int, List<T>> dict, int key, T value)
    {
        if (!dict.TryGetValue(key, out var list))
        {
            list = new List<T>();
            dict[key] = list;
        }
        list.Add(value);
    }

    static string SafeRel(string path, string root)
    {
        try { return Path.GetRelativePath(root, path); }
        catch { return path; }
    }
}

static int PhaseRotationRt0(IReadOnlyList<string> files, string root, string? jsonOut)
{
    int scripted = 0, candidates = 0, recipeOk = 0, validateOk = 0, spliceOk = 0, shapeOk = 0, baselineUnknownAccepted = 0;
    int var1Candidates = 0, var1StoreOk = 0;
    var failures = new List<string>();
    var samples = new List<object>();
    const ushort Firaga = 0x3049;

    foreach (string path in files)
    {
        string id = Path.GetFileNameWithoutExtension(path);
        byte[] monster;
        byte[]? aiFile;
        try
        {
            monster = File.ReadAllBytes(path);
            aiFile = AiScript_File.SliceAiFileFromMonster(monster);
        }
        catch (Exception ex)
        {
            failures.Add($"{id}: read/slice failed: {ex.Message}");
            continue;
        }
        if (aiFile == null) continue;

        AiScriptFile script;
        try { script = AiScript_File.Read(aiFile); }
        catch (Exception ex)
        {
            failures.Add($"{id}: AiFile parse failed: {ex.Message}");
            continue;
        }
        if (!script.HasScript) continue;
        scripted++;
        if (script.Variables.Count == 0) continue;

        AiWorker? worker = AiAutomation.PickCombatWorker(script);
        if (worker == null || worker.Entrypoints.Count == 0) continue;
        int entrypoint = AiAutomation.PickMainEntrypoint(script, worker);
        candidates++;

        IReadOnlyList<AiInstruction> phase2ExtraGuard =
            AiVarConditionBuilder.BuildImmediateComparison(0, AiVarCompareOperator.GreaterOrEqual, 0);
        bool expectVar1Store = script.Variables.Count > 1;
        if (expectVar1Store) var1Candidates++;
        AiPhaseVarMutation[] phase2Mutations = script.Variables.Count > 1
            ? new[] { new AiPhaseVarMutation(0, 1, 2), new AiPhaseVarMutation(1, 1, 1) }
            : new[] { new AiPhaseVarMutation(0, 1, 2) };

        var recipe = new AiPhaseRotationRecipe(
            CounterVariableIndex: 0,
            Steps: new[]
            {
                new AiPhaseRotationStep(
                    1,
                    "Abertura",
                    Firaga,
                    IsFinalPhase: false,
                    StopHere: true,
                    VarMutations: new[] { new AiPhaseVarMutation(0, 1, 2) },
                    TargetRecipe: new AiTargetRecipe(AiTargetRecipeKind.FindAliveFrontlineAny, 0),
                    TriggerKind: AiPhaseTriggerKind.OnTurn),
                new AiPhaseRotationStep(
                    2,
                    "Fase 2",
                    Firaga,
                    IsFinalPhase: false,
                    StopHere: true,
                    VarMutations: phase2Mutations,
                    TargetRecipe: new AiTargetRecipe(AiTargetRecipeKind.FindAliveFrontlineAny, 0),
                    RandomChance: true,
                    RandomK: 2,
                    ExtraGuard: phase2ExtraGuard),
                new AiPhaseRotationStep(
                    3,
                    "Última fase",
                    Firaga,
                    IsFinalPhase: true,
                    StopHere: true,
                    VarMutations: new[] { new AiPhaseVarMutation(0, 0, 0, SetInsteadOfAdd: true) },
                    TargetRecipe: new AiTargetRecipe(AiTargetRecipeKind.FindAliveFrontlineAny, 0)),
            },
            FinalLimit: 1,
            WorkerIndex: worker.Index,
            EntrypointIndex: entrypoint,
            DefaultTargetRecipe: new AiTargetRecipe(AiTargetRecipeKind.FindAliveFrontlineAny, 0));

        AiPhaseRotationApplyResult result;
        try
        {
            result = AiPhaseRotationWriter.Apply(script, recipe);
        }
        catch (Exception ex)
        {
            failures.Add($"{id}: recipe apply failed: {ex.Message}");
            continue;
        }
        recipeOk++;

        if (!AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(
                result.AiFile,
                script,
                aiFile.Length,
                out _,
                out string validationReason))
        {
            failures.Add($"{id}: validator rejected recipe AiFile: {validationReason}");
            continue;
        }
        if (!string.IsNullOrWhiteSpace(validationReason))
            baselineUnknownAccepted++;
        validateOk++;

        AiScriptFile edited;
        try
        {
            byte[] spliced = AiScript_File.SpliceAiFileIntoMonsterGrow(monster, result.AiFile);
            byte[]? slicedBack = AiScript_File.SliceAiFileFromMonster(spliced);
            if (slicedBack == null)
            {
                failures.Add($"{id}: grown monster did not slice AI back.");
                continue;
            }
            edited = AiScript_File.Read(slicedBack);
        }
        catch (Exception ex)
        {
            failures.Add($"{id}: grow-aware splice/re-read failed: {ex.Message}");
            continue;
        }
        spliceOk++;

        var originalImmediateCommandOperands = script.Instructions
            .Select((instruction, index) => (instruction, index))
            .Where(pair =>
                pair.instruction.Opcode == 0xD8 &&
                (pair.instruction.Operand == 0x700B || pair.instruction.Operand == 0x705A) &&
                pair.index > 0 &&
                script.Instructions[pair.index - 1].Opcode == 0xAE &&
                AiCommandId.IsCommandOperand(script.Instructions[pair.index - 1].Operand))
            .Select(pair => script.Instructions[pair.index - 1].Operand)
            .Distinct()
            .ToArray();
        bool preservesOriginalImmediateCommands = originalImmediateCommandOperands.All(operand =>
            edited.Instructions.Any(i => i.Opcode == 0xAE && i.Operand == operand));

        int appendedAt = script.ScriptStart + script.CodeLength;
        bool hasCounterStore = edited.Instructions.Any(i => i.Offset >= appendedAt && i.Opcode == 0xA0 && i.Operand == 0);
        bool hasVar1Store = !expectVar1Store ||
                            edited.Instructions.Any(i => i.Offset >= appendedAt && i.Opcode == 0xA0 && i.Operand == 1);
        if (expectVar1Store && hasVar1Store) var1StoreOk++;
        bool hasRandom = edited.Instructions.Any(i => i.Offset >= appendedAt && i.Opcode == 0xB5 && i.Operand == 0x00A9);
        int originalForcedPerformCount = script.Instructions.Count(i => i.Opcode == 0xD8 && i.Operand == 0x705A);
        int editedForcedPerformCount = edited.Instructions.Count(i => i.Opcode == 0xD8 && i.Operand == 0x705A);
        bool hasForcedPerform = editedForcedPerformCount >= originalForcedPerformCount + 3;
        int originalGuardBranchCount = script.Instructions.Count(i => i.Opcode == 0xD7);
        int editedGuardBranchCount = edited.Instructions.Count(i => i.Opcode == 0xD7);
        bool hasGuardBranch = editedGuardBranchCount >= originalGuardBranchCount + 3;
        int originalRetCount = script.Instructions.Count(i => i.Opcode == 0x3C);
        int editedRetCount = edited.Instructions.Count(i => i.Opcode == 0x3C);
        bool hasRetStops = editedRetCount >= originalRetCount + 3;
        bool ok = edited.CodeWalkClosedExactly &&
                  edited.Workers.Count == script.Workers.Count &&
                  edited.UnknownOpcodes.SequenceEqual(script.UnknownOpcodes) &&
                  hasCounterStore &&
                  hasVar1Store &&
                  hasRandom &&
                  hasForcedPerform &&
                  preservesOriginalImmediateCommands &&
                  hasGuardBranch &&
                  hasRetStops;
        if (!ok)
        {
            failures.Add($"{id}: recipe shape incomplete (store0={hasCounterStore} store1={hasVar1Store} rng={hasRandom} forced={hasForcedPerform} keepCmds={preservesOriginalImmediateCommands} d7={hasGuardBranch} ret={hasRetStops}).");
            continue;
        }
        shapeOk++;

        if (samples.Count < 16)
        {
            samples.Add(new
            {
                id,
                relativePath = SafeRelLocal(path, root),
                worker = worker.Index,
                entrypoint,
                counter = script.Variables[0].Name,
                secondVar = script.Variables.Count > 1 ? script.Variables[1].Name : null,
                result.AppliedSteps,
                recipe = "three onTurn phases, per-step target, local chance, var advance +1..+2, var1 +1 when present, final reset var=0",
            });
        }
    }

    bool pass = candidates > 0 &&
                recipeOk == candidates &&
                validateOk == candidates &&
                spliceOk == candidates &&
                shapeOk == candidates &&
                var1StoreOk == var1Candidates;
    Console.WriteLine("=== AiScriptLab --phase-rotation-rt0 (LAB v1: PhaseRotationRecipe writer) ===");
    Console.WriteLine($"scripted monsters       : {scripted}");
    Console.WriteLine($"candidates >=1 var      : {candidates}");
    Console.WriteLine($"recipe apply            : {recipeOk}/{candidates}");
    Console.WriteLine($"validator accepts       : {validateOk}/{candidates}");
    Console.WriteLine($"baseline unknowns kept  : {baselineUnknownAccepted}");
    Console.WriteLine($"grow-aware splice       : {spliceOk}/{candidates}");
    Console.WriteLine($"shape check             : {shapeOk}/{candidates}");
    Console.WriteLine($"var1 store when present : {var1StoreOk}/{var1Candidates}");
    Console.WriteLine(pass ? "VERDICT: PASS" : "VERDICT: FAIL");
    foreach (object sample in samples.Take(8))
        Console.WriteLine("sample: " + JsonSerializer.Serialize(sample));

    if (!string.IsNullOrWhiteSpace(jsonOut))
    {
        string? dir = Path.GetDirectoryName(jsonOut);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(jsonOut, JsonSerializer.Serialize(new
        {
            generatedAtUtc = DateTime.UtcNow.ToString("O"),
            root,
            scripted,
            candidates,
            recipeOk,
            validateOk,
            baselineUnknownAccepted,
            spliceOk,
            shapeOk,
            var1Candidates,
            var1StoreOk,
            pass,
            samples,
            failures = failures.Take(100).ToArray(),
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"json={jsonOut}");
    }

    return pass ? 0 : 1;

    static string SafeRelLocal(string path, string root)
    {
        try { return Path.GetRelativePath(root, path); }
        catch { return path; }
    }
}

static int OverdriveGaugeRt0(IReadOnlyList<string> files, string root, string? jsonOut)
{
    int scripted = 0, candidates = 0, applyOk = 0, validateOk = 0, spliceOk = 0, shapeOk = 0;
    var failures = new List<string>();
    var samples = new List<object>();
    const ushort Max = 100;
    const ushort Current = 100;
    const ushort Mode = 0;

    foreach (string path in files)
    {
        string id = Path.GetFileNameWithoutExtension(path);
        byte[] monster;
        byte[]? aiFile;
        try
        {
            monster = File.ReadAllBytes(path);
            aiFile = AiScript_File.SliceAiFileFromMonster(monster);
        }
        catch (Exception ex)
        {
            failures.Add($"{id}: read/slice failed: {ex.Message}");
            continue;
        }
        if (aiFile == null) continue;

        AiScriptFile script;
        try { script = AiScript_File.Read(aiFile); }
        catch (Exception ex)
        {
            failures.Add($"{id}: AiFile parse failed: {ex.Message}");
            continue;
        }
        if (!script.HasScript) continue;
        scripted++;

        AiWorker? worker = AiAutomation.PickCombatWorker(script);
        if (worker == null || worker.Entrypoints.Count == 0) continue;
        int entrypoint = AiAutomation.PickMainEntrypoint(script, worker);
        candidates++;

        byte[] editedAi;
        try
        {
            editedAi = AiAutomation.AddOverdriveGaugeSetup(
                script,
                Max,
                Current,
                setMode: true,
                Mode,
                worker.Index,
                entrypoint);
        }
        catch (Exception ex)
        {
            failures.Add($"{id}: AddOverdriveGaugeSetup failed: {ex.Message}");
            continue;
        }
        applyOk++;

        AiValidationReport report = AiValidator.ValidateRebuilt(editedAi, aiFile.Length);
        if (!report.IsValid)
        {
            failures.Add($"{id}: validator rejected overdrive gauge AiFile: {report.ToReportString()}");
            continue;
        }
        validateOk++;

        AiScriptFile reread;
        try
        {
            byte[] spliced = AiScript_File.SpliceAiFileIntoMonsterGrow(monster, editedAi);
            byte[]? slicedBack = AiScript_File.SliceAiFileFromMonster(spliced);
            if (slicedBack == null)
            {
                failures.Add($"{id}: grown monster did not slice AI back.");
                continue;
            }
            reread = AiScript_File.Read(slicedBack);
        }
        catch (Exception ex)
        {
            failures.Add($"{id}: grow-aware splice/re-read failed: {ex.Message}");
            continue;
        }
        spliceOk++;

        int appendedAt = script.ScriptStart + script.CodeLength;
        bool showOk = HasChrPropertySelfWrite(reread, appendedAt, AiAutomation.ShowOverdriveBarField, 1);
        bool modeOk = HasChrPropertySelfWrite(reread, appendedAt, AiAutomation.OverdriveModeField, Mode);
        bool maxOk = HasChrPropertySelfWrite(reread, appendedAt, AiAutomation.OverdriveMaxField, Max);
        bool currentOk = HasChrPropertySelfWrite(reread, appendedAt, AiAutomation.OverdriveCurrentField, Current);
        bool clean = reread.CodeWalkClosedExactly && reread.UnknownOpcodes.Count == 0;
        if (clean && showOk && modeOk && maxOk && currentOk)
            shapeOk++;
        else
        {
            failures.Add($"{id}: overdrive shape incomplete (clean={clean} show={showOk} mode={modeOk} max={maxOk} current={currentOk}).");
            continue;
        }

        if (samples.Count < 16)
        {
            samples.Add(new
            {
                id,
                relativePath = SafeRelLocal(path, root),
                worker = worker.Index,
                entrypoint,
                fields = new[]
                {
                    "writeChrProperty(Self, showOverdriveBar 0x0089, 1)",
                    "writeChrProperty(Self, OverdriveMode 0x0012, 0)",
                    "writeChrProperty(Self, OverdriveMax 0x0014, 100)",
                    "writeChrProperty(Self, OverdriveCurrent 0x0013, 100)",
                },
            });
        }
    }

    bool pass = candidates > 0 &&
                applyOk == candidates &&
                validateOk == candidates &&
                spliceOk == candidates &&
                shapeOk == candidates;
    Console.WriteLine("=== AiScriptLab --overdrive-gauge-rt0 (LAB: writeChrProperty Self Overdrive setup) ===");
    Console.WriteLine($"scripted monsters       : {scripted}");
    Console.WriteLine($"candidates w/ hook      : {candidates}");
    Console.WriteLine($"recipe apply            : {applyOk}/{candidates}");
    Console.WriteLine($"validator accepts       : {validateOk}/{candidates}");
    Console.WriteLine($"grow-aware splice       : {spliceOk}/{candidates}");
    Console.WriteLine($"shape check             : {shapeOk}/{candidates}");
    Console.WriteLine(pass ? "VERDICT: PASS" : "VERDICT: FAIL");
    foreach (object sample in samples.Take(8))
        Console.WriteLine("sample: " + JsonSerializer.Serialize(sample));

    if (!string.IsNullOrWhiteSpace(jsonOut))
    {
        string? dir = Path.GetDirectoryName(jsonOut);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(jsonOut, JsonSerializer.Serialize(new
        {
            generatedAtUtc = DateTime.UtcNow.ToString("O"),
            root,
            scripted,
            candidates,
            applyOk,
            validateOk,
            spliceOk,
            shapeOk,
            pass,
            samples,
            failures = failures.Take(100).ToArray(),
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"json={jsonOut}");
    }

    return pass ? 0 : 1;

    static bool HasChrPropertySelfWrite(AiScriptFile script, int minOffset, ushort fieldId, ushort value)
    {
        const ushort Self = 0xFFF3;
        for (int i = 0; i + 3 < script.Instructions.Count; i++)
        {
            AiInstruction target = script.Instructions[i];
            AiInstruction field = script.Instructions[i + 1];
            AiInstruction val = script.Instructions[i + 2];
            AiInstruction call = script.Instructions[i + 3];
            if (target.Offset < minOffset)
                continue;
            if (target.Opcode == 0xAE && target.Operand == Self &&
                field.Opcode == 0xAE && field.Operand == fieldId &&
                val.Opcode == 0xAE && val.Operand == value &&
                call.Opcode == 0xD8 && call.Operand == 0x7018)
                return true;
        }
        return false;
    }

    static string SafeRelLocal(string path, string root)
    {
        try { return Path.GetRelativePath(root, path); }
        catch { return path; }
    }
}

static int OverdriveAuthoringRt0(IReadOnlyList<string> files, string root, string? jsonOut)
{
    const ushort Max = 100, Start = 0, TurnCharge = 10, HpCharge = 15, ChanceCharge = 25, HitCharge = 20, DamagePct = 25, DamageCharge = 30;
    const ushort AfterActionCharge = 10, EveryN = 3, EveryNCharge = 10, HpRangeMin = 25, HpRangeMax = 75, HpRangeCharge = 15;
    const ushort StatusCharge = 20, PartyDeadCharge = 20, LastAttackerCharge = 10, DamageZeroCharge = 10, PhysicalHitCharge = 10, MagicalHitCharge = 10;
    var finisherSequence = new ushort[] { 0x4096, 0x4118 };
    var fallbackTarget = new AiTargetRecipe(AiTargetRecipeKind.Literal, 0xFFF2);
    var finisherSteps = finisherSequence.Select(c => (c, fallbackTarget)).ToList();
    int scripted = 0, candidates = 0, applyOk = 0, validateOk = 0, spliceOk = 0, shapeOk = 0;
    int onHitSupported = 0, onHitOk = 0, outsideTurnFinisherOk = 0, damagePercentOk = 0, nativeModeAbsentOk = 0;
    int afterActionSupported = 0, afterActionOk = 0, everyNTurnsOk = 0, hpRangeOk = 0, selfStatusOk = 0, anyPositiveStatusOk = 0, partyDeadOk = 0;
    int damageZeroOk = 0, lastAttackerOk = 0, physicalHitOk = 0, magicalHitOk = 0;
    int originalGeneratedFalsePositive = 0, stripOk = 0, stripCleanOk = 0;
    var failures = new List<string>();
    var samples = new List<object>();

    foreach (string path in files)
    {
        string id = Path.GetFileNameWithoutExtension(path);
        byte[] monster;
        byte[]? aiFile;
        try
        {
            monster = File.ReadAllBytes(path);
            aiFile = AiScript_File.SliceAiFileFromMonster(monster);
        }
        catch (Exception ex)
        {
            failures.Add($"{id}: read/slice failed: {ex.Message}");
            continue;
        }
        if (aiFile == null) continue;

        AiScriptFile script;
        try { script = AiScript_File.Read(aiFile); }
        catch (Exception ex)
        {
            failures.Add($"{id}: AiFile parse failed: {ex.Message}");
            continue;
        }
        if (!script.HasScript) continue;
        scripted++;
        int originalOverdriveAccesses = AiAutomation.CountOverdriveFieldAccesses(script);
        int originalGeneratedBlocks = AiAutomation.CountGeneratedOverdriveLabBlocks(script, script.CodeLength);
        if (originalGeneratedBlocks > 0)
        {
            originalGeneratedFalsePositive++;
            failures.Add($"{id}: vanilla/original script matched generated Overdrive LAB stripper ({originalGeneratedBlocks} block(s)).");
        }

        if (!AiWorkerMapping.TryResolveCombatOnTurn(monster, script, out AiEventHook onTurnHook, out string onTurnError))
        {
            failures.Add($"{id}: no onTurn hook for authoring recipe: {onTurnError}");
            continue;
        }
        if (onTurnHook.WorkerIndex < 0 || onTurnHook.WorkerIndex >= script.Workers.Count
            || script.Workers[onTurnHook.WorkerIndex].Entrypoints.Count == 0)
        {
            failures.Add($"{id}: invalid CombatHandler worker for authoring recipe.");
            continue;
        }

        var setupHook = new AiEventHook(onTurnHook.WorkerIndex, 0);
        candidates++;

        AiScriptFile working = script;
        byte[] editedAi = aiFile;
        bool applied = true;
        bool Step(Func<AiScriptFile, byte[]> edit, string label)
        {
            if (!applied) return false;
            try
            {
                editedAi = edit(working);
                working = AiScript_File.Read(editedAi);
                return true;
            }
            catch (Exception ex)
            {
                failures.Add($"{id}: {label} failed: {ex.Message}");
                applied = false;
                return false;
            }
        }

        AiDetectedAction? afterAction = null;
        try
        {
            afterAction = AiAutomation.DetectCommandActions(script)
                .FirstOrDefault(a => a.Removable);
        }
        catch (Exception ex)
        {
            failures.Add($"{id}: action detection for after-action charge failed: {ex.Message}");
        }
        if (afterAction != null)
        {
            afterActionSupported++;
            if (Step(s => AiAutomation.AddOverdriveChargeAfterAction(s, afterAction, AfterActionCharge),
                    "after selected action charge"))
                afterActionOk++;
        }

        Step(s => AiAutomation.AddOverdriveFinisherSequence(s, finisherSteps,
            resetAfterSequence: true, onTurnHook.WorkerIndex, onTurnHook.EntrypointIndex, stopAfterAction: true), "finisher sequence");
        Step(s => AiAutomation.AddOverdriveClampToMax(s, onTurnHook.WorkerIndex, onTurnHook.EntrypointIndex), "onTurn clamp");
        Step(s => AiAutomation.AddOverdriveChargeSource(s, ChanceCharge, AiAutomation.BuildRandomGuard(3),
            onTurnHook.WorkerIndex, onTurnHook.EntrypointIndex), "chance charge");
        Step(s => AiAutomation.AddOverdriveChargeSource(s, HpCharge, AiAutomation.BuildHpBelowPercentGuard(50),
            onTurnHook.WorkerIndex, onTurnHook.EntrypointIndex), "hp charge");
        if (Step(s => AiAutomation.AddOverdriveChargeSource(s, HpRangeCharge, AiAutomation.BuildHpBetweenPercentGuard(HpRangeMin, HpRangeMax),
                onTurnHook.WorkerIndex, onTurnHook.EntrypointIndex), "hp range charge"))
            hpRangeOk++;
        ushort statusField = AiAutomation.SelfBuffPresets.Count > 0
            ? AiAutomation.SelfBuffPresets[0].FieldId
            : (ushort)0x0038;
        if (Step(s => AiAutomation.AddOverdriveChargeSource(s, StatusCharge, AiAutomation.BuildSelfFieldGreaterThanGuard(statusField, 0),
                onTurnHook.WorkerIndex, onTurnHook.EntrypointIndex), "self status charge"))
            selfStatusOk++;
        if (Step(s => AiAutomation.AddOverdriveChargeSource(s, StatusCharge, AiAutomation.BuildAnyPositiveSelfStatusGuard(),
                onTurnHook.WorkerIndex, onTurnHook.EntrypointIndex), "any positive status charge"))
            anyPositiveStatusOk++;
        if (Step(s => AiAutomation.AddOverdriveChargeSource(s, PartyDeadCharge, AiAutomation.BuildFrontlineDeadExistsGuard(),
                onTurnHook.WorkerIndex, onTurnHook.EntrypointIndex), "party dead charge"))
            partyDeadOk++;
        if (Step(s => AiAutomation.AddOverdriveChargeSource(s, EveryNCharge, AiAutomation.BuildEveryNTurnsGuard(EveryN),
                onTurnHook.WorkerIndex, onTurnHook.EntrypointIndex), "every N turns charge"))
            everyNTurnsOk++;
        Step(s => AiAutomation.AddOverdriveChargeSource(s, TurnCharge, AiAutomation.BuildAlwaysGuard(),
            onTurnHook.WorkerIndex, onTurnHook.EntrypointIndex), "turn charge");

        if (script.Workers[onTurnHook.WorkerIndex].Entrypoints.Count > 3)
        {
            onHitSupported++;
            var hitHook = new AiEventHook(onTurnHook.WorkerIndex, 3);
            Step(s => AiAutomation.AddOverdriveFinisherSequence(s, finisherSteps,
                resetAfterSequence: true, hitHook.WorkerIndex, hitHook.EntrypointIndex, stopAfterAction: true),
                "outside-turn onHit finisher");
            Step(s => AiAutomation.AddOverdriveClampToMax(s, hitHook.WorkerIndex, hitHook.EntrypointIndex), "onHit clamp");
            Step(s => AiAutomation.AddOverdriveChargeSource(s, HitCharge, AiAutomation.BuildAlwaysGuard(),
                hitHook.WorkerIndex, hitHook.EntrypointIndex), "onHit charge");
            Step(s => AiAutomation.AddOverdriveChargeSource(s, DamageCharge, AiAutomation.BuildLastDamageTakenAtLeastPercentMaxHpGuard(DamagePct),
                hitHook.WorkerIndex, hitHook.EntrypointIndex), "damage percent charge");
            Step(s => AiAutomation.AddOverdriveChargeSource(s, DamageZeroCharge, AiAutomation.BuildLastDamageTakenEqualsGuard(0),
                hitHook.WorkerIndex, hitHook.EntrypointIndex), "damage zero charge");
            Step(s => AiAutomation.AddOverdriveChargeSource(s, LastAttackerCharge,
                    AiAutomation.BuildActorFieldGreaterThanGuard(AiAutomation.LastAttackerTarget, AiAutomation.ChrFieldIsAlive, 0),
                    hitHook.WorkerIndex, hitHook.EntrypointIndex), "last attacker alive charge");
            Step(s => AiAutomation.AddOverdriveChargeSource(s, PhysicalHitCharge,
                    AiAutomation.BuildUsedCommandDamageTypeGuard(AiAutomation.DamageTypePhysical),
                    hitHook.WorkerIndex, hitHook.EntrypointIndex), "physical hit damageType charge");
            Step(s => AiAutomation.AddOverdriveChargeSource(s, MagicalHitCharge,
                    AiAutomation.BuildUsedCommandDamageTypeGuard(AiAutomation.DamageTypeMagical),
                    hitHook.WorkerIndex, hitHook.EntrypointIndex), "magical hit damageType charge");
            if (applied)
            {
                onHitOk++;
                outsideTurnFinisherOk++;
                damagePercentOk++;
                damageZeroOk++;
                lastAttackerOk++;
                physicalHitOk++;
                magicalHitOk++;
            }
        }

        Step(s => AiAutomation.AddOverdriveGaugeSetup(s, Max, Start, setMode: false, mode: 0,
            setupHook.WorkerIndex, setupHook.EntrypointIndex, showBar: true), "battle-start setup");

        if (!applied) continue;
        applyOk++;

        try
        {
            AiScriptFile editedScript = AiScript_File.Read(editedAi);
            int generatedBlocks = AiAutomation.CountGeneratedOverdriveLabBlocks(editedScript, script.CodeLength);
            byte[] strippedAi = AiAutomation.StripGeneratedOverdriveLab(editedScript, out int strippedBlocks, script.CodeLength);
            AiValidationReport stripReport = AiValidator.ValidateRebuilt(strippedAi, editedAi.Length);
            AiScriptFile strippedScript = AiScript_File.Read(strippedAi);
            bool validStrip = stripReport.IsValid
                              && strippedBlocks > 0
                              && strippedBlocks == generatedBlocks;
            if (validStrip) stripOk++;
            else failures.Add($"{id}: strip invalid generated={generatedBlocks} stripped={strippedBlocks} report={stripReport.ToReportString()}");

            bool cleanStrip = validStrip
                              && AiAutomation.CountGeneratedOverdriveLabBlocks(strippedScript, script.CodeLength) == 0
                              && AiAutomation.CountOverdriveFieldAccesses(strippedScript) == originalOverdriveAccesses;
            if (cleanStrip) stripCleanOk++;
            else failures.Add($"{id}: strip did not return to original OD footprint; originalAccess={originalOverdriveAccesses} afterAccess={AiAutomation.CountOverdriveFieldAccesses(strippedScript)} generatedAfter={AiAutomation.CountGeneratedOverdriveLabBlocks(strippedScript, script.CodeLength)}.");
        }
        catch (Exception ex)
        {
            failures.Add($"{id}: strip generated Overdrive LAB failed: {ex.Message}");
        }

        AiValidationReport report = AiValidator.ValidateRebuilt(editedAi, aiFile.Length);
        if (!report.IsValid)
        {
            failures.Add($"{id}: validator rejected authoring AiFile: {report.ToReportString()}");
            continue;
        }
        validateOk++;

        AiScriptFile reread;
        try
        {
            byte[] spliced = AiScript_File.SpliceAiFileIntoMonsterGrow(monster, editedAi);
            byte[]? slicedBack = AiScript_File.SliceAiFileFromMonster(spliced);
            if (slicedBack == null)
            {
                failures.Add($"{id}: grown monster did not slice AI back.");
                continue;
            }
            reread = AiScript_File.Read(slicedBack);
        }
        catch (Exception ex)
        {
            failures.Add($"{id}: grow-aware splice/re-read failed: {ex.Message}");
            continue;
        }
        spliceOk++;

        int appendedAt = script.ScriptStart + script.CodeLength;
        bool clean = reread.CodeWalkClosedExactly && reread.UnknownOpcodes.Count == 0;
        bool show = HasSelfFieldWrite(reread, appendedAt, AiAutomation.ShowOverdriveBarField);
        bool max = HasSelfFieldWrite(reread, appendedAt, AiAutomation.OverdriveMaxField);
        bool current = HasSelfFieldWrite(reread, appendedAt, AiAutomation.OverdriveCurrentField);
        bool noNativeMode = !HasSelfFieldWrite(reread, appendedAt, AiAutomation.OverdriveModeField);
        bool reads = CountCall(reread, appendedAt, 0x700F) >= 4;
        bool adds = HasOpcodeAfter(reread, appendedAt, 0x14);
        int expectedCommandCalls = finisherSequence.Length * (script.Workers[onTurnHook.WorkerIndex].Entrypoints.Count > 3 ? 2 : 1);
        bool commands = CountCall(reread, appendedAt, AiAutomation.PerformCommand) >= expectedCommandCalls;
        if (noNativeMode) nativeModeAbsentOk++;
        if (clean && show && max && current && noNativeMode && reads && adds && commands)
            shapeOk++;
        else
            failures.Add($"{id}: shape incomplete clean={clean} show={show} max={max} current={current} noNativeMode={noNativeMode} reads={reads} add={adds} commands={commands}.");

        if (samples.Count < 16)
        {
            samples.Add(new
            {
                id,
                relativePath = Path.GetRelativePath(root, path),
                onTurn = new { onTurnHook.WorkerIndex, onTurnHook.EntrypointIndex },
                setup = new { setupHook.WorkerIndex, setupHook.EntrypointIndex },
                recipe = new[]
                {
                    "setup: showOverdriveBar + Max=100 + Current=0, no native OverdriveMode write",
                    "charge: +10/turn, +10 every 3 turns, +15 when HP<50%, +15 when HP is 25..75%, +20 when self status field >0, +20 when any positive self status is active, +20 when a frontline character is dead, +25 at 1-in-3 chance",
                    "inline: +10 after a selected command action when a removable action exists",
                    "optional onHit: +20 where entrypoint 3 exists",
                    "optional damage% onHit: LastDamageTakenHP*100 >= maxHP*25 -> +30",
                    "optional event reaction onHit: LastDamageTakenHP==0 -> +10, LastAttacker.IsAlive>0 -> +10",
                    "optional event reaction onHit: usedCommand().damageType == Physical/Magical -> +10",
                    "optional outside-turn ready: onHit Current >= Max -> finisher sequence + reset",
                    "clamp: Current > Max -> Current = Max",
                    "ready: Current >= Max -> Diamond Dust sequence + reset",
                },
            });
        }
    }

    bool pass = candidates > 0 &&
                applyOk == candidates &&
                validateOk == candidates &&
                spliceOk == candidates &&
                shapeOk == candidates &&
                nativeModeAbsentOk == candidates &&
                afterActionOk == afterActionSupported &&
                everyNTurnsOk == candidates &&
                hpRangeOk == candidates &&
                selfStatusOk == candidates &&
                anyPositiveStatusOk == candidates &&
                partyDeadOk == candidates &&
                onHitOk == onHitSupported &&
                outsideTurnFinisherOk == onHitSupported &&
                damagePercentOk == onHitSupported &&
                damageZeroOk == onHitSupported &&
                lastAttackerOk == onHitSupported &&
                physicalHitOk == onHitSupported &&
                magicalHitOk == onHitSupported &&
                originalGeneratedFalsePositive == 0 &&
                stripOk == candidates &&
                stripCleanOk == candidates;
    Console.WriteLine("=== AiScriptLab --overdrive-authoring-rt0 (LAB: setup + charge + sequence) ===");
    Console.WriteLine($"scripted monsters       : {scripted}");
    Console.WriteLine($"candidates w/ onTurn    : {candidates}");
    Console.WriteLine($"recipe apply            : {applyOk}/{candidates}");
    Console.WriteLine($"validator accepts       : {validateOk}/{candidates}");
    Console.WriteLine($"grow-aware splice       : {spliceOk}/{candidates}");
    Console.WriteLine($"shape check             : {shapeOk}/{candidates}");
    Console.WriteLine($"native mode write absent: {nativeModeAbsentOk}/{candidates}");
    Console.WriteLine($"after selected action   : {afterActionOk}/{afterActionSupported}");
    Console.WriteLine($"every-N turns           : {everyNTurnsOk}/{candidates}");
    Console.WriteLine($"HP range                : {hpRangeOk}/{candidates}");
    Console.WriteLine($"self status             : {selfStatusOk}/{candidates}");
    Console.WriteLine($"any positive status     : {anyPositiveStatusOk}/{candidates}");
    Console.WriteLine($"party dead              : {partyDeadOk}/{candidates}");
    Console.WriteLine($"onHit optional          : {onHitOk}/{onHitSupported}");
    Console.WriteLine($"outside-turn finisher   : {outsideTurnFinisherOk}/{onHitSupported}");
    Console.WriteLine($"damage-percent onHit    : {damagePercentOk}/{onHitSupported}");
    Console.WriteLine($"damage-zero onHit       : {damageZeroOk}/{onHitSupported}");
    Console.WriteLine($"last-attacker onHit     : {lastAttackerOk}/{onHitSupported}");
    Console.WriteLine($"physical-hit onHit      : {physicalHitOk}/{onHitSupported}");
    Console.WriteLine($"magical-hit onHit       : {magicalHitOk}/{onHitSupported}");
    Console.WriteLine($"strip false positives   : {originalGeneratedFalsePositive}");
    Console.WriteLine($"strip generated LAB     : {stripOk}/{candidates}");
    Console.WriteLine($"strip clean footprint   : {stripCleanOk}/{candidates}");
    Console.WriteLine(pass ? "VERDICT: PASS" : "VERDICT: FAIL");
    foreach (object sample in samples.Take(8))
        Console.WriteLine("sample: " + JsonSerializer.Serialize(sample));

    if (!string.IsNullOrWhiteSpace(jsonOut))
    {
        string? dir = Path.GetDirectoryName(jsonOut);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(jsonOut, JsonSerializer.Serialize(new
        {
            generatedAtUtc = DateTime.UtcNow.ToString("O"),
            root,
            scripted,
            candidates,
            applyOk,
            validateOk,
            spliceOk,
            shapeOk,
            nativeModeAbsentOk,
            afterActionSupported,
            afterActionOk,
            everyNTurnsOk,
            hpRangeOk,
            selfStatusOk,
            anyPositiveStatusOk,
            partyDeadOk,
            onHitSupported,
            onHitOk,
            outsideTurnFinisherOk,
            damagePercentOk,
            damageZeroOk,
            lastAttackerOk,
            physicalHitOk,
            magicalHitOk,
            originalGeneratedFalsePositive,
            stripOk,
            stripCleanOk,
            pass,
            samples,
            failures = failures.Take(120).ToArray(),
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"json={jsonOut}");
    }

    return pass ? 0 : 1;

    static bool HasSelfFieldWrite(AiScriptFile script, int minOffset, ushort fieldId)
    {
        for (int i = 0; i + 3 < script.Instructions.Count; i++)
        {
            AiInstruction target = script.Instructions[i];
            AiInstruction field = script.Instructions[i + 1];
            AiInstruction call = script.Instructions[i + 3];
            if (target.Offset < minOffset)
                continue;
            if (target.Opcode == 0xAE && target.Operand == 0xFFF3
                && field.Opcode == 0xAE && field.Operand == fieldId
                && call.Opcode == 0xD8 && call.Operand == 0x7018)
                return true;
        }
        return false;
    }

    static int CountCall(AiScriptFile script, int minOffset, ushort callId)
        => script.Instructions.Count(i => i.Offset >= minOffset && i.Opcode is 0xB5 or 0xD8 && i.Operand == callId);

    static bool HasOpcodeAfter(AiScriptFile script, int minOffset, byte opcode)
        => script.Instructions.Any(i => i.Offset >= minOffset && i.Opcode == opcode);
}

static int OverdriveFlowScan(IReadOnlyList<string> files, string root, string? jsonOut)
{
    const byte PUSHII = 0xAE, CALL = 0xB5, CALLPOPA = 0xD8;
    const byte ADD = 0x14, SUB = 0x15;
    const ushort ReadChrProperty = 0x700F, GetStatField = 0x70AA, SetStatField = 0x70AB, WriteChrProperty = 0x7018;
    const ushort OverdriveMode = 0x0012, OverdriveCurrent = 0x0013, OverdriveMax = 0x0014;
    const ushort ShowOverdriveBar = 0x0089, BarFlagCam = 0x0088, GaugeAdd = 0x0118, OverdriveAvailable = 0x011A, BarPos = 0x0128;
    var compareOps = new HashSet<byte> { 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F };
    var mathOps = new HashSet<byte> { 0x12, ADD, SUB, 0x16, 0x17, 0x18 };
    var trackedFields = new HashSet<ushort>
    {
        OverdriveMode, OverdriveCurrent, OverdriveMax, ShowOverdriveBar,
        BarFlagCam, GaugeAdd, OverdriveAvailable, BarPos,
    };

    int scripted = 0, overdriveCandidates = 0, setupGauge = 0, setupOnly = 0;
    int currentReadScripts = 0, maxReadScripts = 0, currentWriteScripts = 0;
    int chargeLikeScripts = 0, clampLikeScripts = 0, readyGuardScripts = 0, drainResetScripts = 0;
    int finisherScripts = 0, fullFlowScripts = 0, chrWriteFieldScripts = 0;
    var fieldReads = new SortedDictionary<ushort, int>();
    var fieldWrites = new SortedDictionary<ushort, int>();
    var commandUse = new SortedDictionary<ushort, int>();
    var monsters = new List<object>();

    foreach (string path in files)
    {
        string id = Path.GetFileNameWithoutExtension(path);
        byte[]? aiFile;
        try { aiFile = AiScript_File.SliceAiFileFromMonster(File.ReadAllBytes(path)); }
        catch { continue; }
        if (aiFile == null) continue;

        AiScriptFile script;
        try { script = AiScript_File.Read(aiFile); }
        catch { continue; }
        if (!script.HasScript) continue;
        scripted++;

        var reads = new List<(int Index, int Offset, ushort Field)>();
        var writes = new List<(int Index, int Offset, ushort Field, ushort? LiteralValue, string ValueShape)>();
        var chrWrites = new List<(int Index, int Offset, ushort Field, ushort? LiteralValue, string ValueShape)>();

        IReadOnlyList<AiInstruction> ins = script.Instructions;
        for (int i = 0; i < ins.Count; i++)
        {
            AiInstruction call = ins[i];
            if (call.Opcode is not (CALL or CALLPOPA))
                continue;

            if ((call.Operand == GetStatField || call.Operand == ReadChrProperty)
                && LiteralAt(ins, i - 1, out ushort readField))
            {
                reads.Add((i, call.Offset, readField));
                Inc(fieldReads, readField);
            }
            else if (call.Operand == SetStatField && LiteralAt(ins, i - 2, out ushort statWriteField))
            {
                ushort? literal = LiteralValueAt(ins, i - 1);
                writes.Add((i, call.Offset, statWriteField, literal, ValueShapeAt(ins, i - 1)));
                Inc(fieldWrites, statWriteField);
            }
            else if (call.Operand == WriteChrProperty && LiteralAt(ins, i - 2, out ushort chrWriteField))
            {
                ushort? literal = LiteralValueAt(ins, i - 1);
                chrWrites.Add((i, call.Offset, chrWriteField, literal, ValueShapeAt(ins, i - 1)));
                writes.Add((i, call.Offset, chrWriteField, literal, ValueShapeAt(ins, i - 1)));
                Inc(fieldWrites, chrWriteField);
            }
        }

        IReadOnlyList<AiDetectedAction> actions;
        try { actions = AiAutomation.DetectActions(script); }
        catch { actions = Array.Empty<AiDetectedAction>(); }

        var finishers = actions
            .Where(a => a.Kind == AiActionKind.Command)
            .Select(a =>
            {
                AiCommandDecode d = AiCommandId.Decode(a.CommandOperand);
                string name = string.IsNullOrWhiteSpace(a.AbilityName) ? d.Name : a.AbilityName;
                return new
                {
                    offset = a.CallOffset,
                    operand = a.CommandOperand,
                    operandHex = Hex(a.CommandOperand),
                    name,
                    target = a.TargetPushOffset >= 0 ? Hex(a.TargetOperand) : "computed/unknown",
                    force = a.ForcePerform,
                    isOverdriveFinisher = IsOverdriveFinisherName(name),
                };
            })
            .Where(a => a.isOverdriveFinisher)
            .ToList();

        foreach (var finisher in finishers)
            Inc(commandUse, finisher.operand);

        bool hasCurrentRead = reads.Any(r => r.Field == OverdriveCurrent);
        bool hasMaxRead = reads.Any(r => r.Field == OverdriveMax);
        bool hasCurrentWrite = writes.Any(w => w.Field == OverdriveCurrent);
        bool hasMaxWrite = writes.Any(w => w.Field == OverdriveMax);
        bool hasShowBarWrite = writes.Any(w => w.Field == ShowOverdriveBar && (w.LiteralValue == null || w.LiteralValue != 0));
        bool hasTrackedField = reads.Any(r => trackedFields.Contains(r.Field))
                               || writes.Any(w => trackedFields.Contains(w.Field))
                               || chrWrites.Any(w => trackedFields.Contains(w.Field))
                               || finishers.Count > 0;
        if (!hasTrackedField)
            continue;

        var currentWriteSites = writes.Where(w => w.Field == OverdriveCurrent).ToList();
        var readySites = new List<string>();
        for (int i = 0; i < ins.Count; i++)
        {
            if (!compareOps.Contains(ins[i].Opcode))
                continue;
            int start = Math.Max(0, i - 18);
            if (!HasGetStatFieldBetween(ins, start, i, OverdriveCurrent)
                || !HasGetStatFieldBetween(ins, start, i, OverdriveMax))
                continue;
            bool branches = HasOpcodeBetween(ins, i + 1, Math.Min(ins.Count, i + 5), 0xD6)
                            || HasOpcodeBetween(ins, i + 1, Math.Min(ins.Count, i + 5), 0xD7);
            readySites.Add($"0x{ins[i].Offset:X4}:{AiScript_File.Mnemonic(ins[i].Opcode)}{(branches ? "+branch" : "")}");
        }

        var chargeWrites = currentWriteSites
            .Where(w => HasGetStatFieldBetween(ins, Math.Max(0, w.Index - 28), w.Index, OverdriveCurrent)
                        && HasOpcodeBetween(ins, Math.Max(0, w.Index - 28), w.Index, ADD))
            .ToList();
        var clampWrites = currentWriteSites
            .Where(w => HasGetStatFieldBetween(ins, Math.Max(0, w.Index - 40), w.Index, OverdriveCurrent)
                        && HasGetStatFieldBetween(ins, Math.Max(0, w.Index - 40), w.Index, OverdriveMax)
                        && HasAnyOpcodeBetween(ins, Math.Max(0, w.Index - 40), w.Index, compareOps))
            .ToList();
        var resetWrites = currentWriteSites.Where(w => w.LiteralValue == 0).ToList();
        var drainWrites = currentWriteSites
            .Where(w => HasGetStatFieldBetween(ins, Math.Max(0, w.Index - 32), w.Index, OverdriveCurrent)
                        && HasGetStatFieldBetween(ins, Math.Max(0, w.Index - 32), w.Index, OverdriveMax)
                        && HasOpcodeBetween(ins, Math.Max(0, w.Index - 32), w.Index, SUB))
            .ToList();
        var mathWrites = currentWriteSites
            .Where(w => HasAnyOpcodeBetween(ins, Math.Max(0, w.Index - 32), w.Index, mathOps))
            .ToList();

        var resetDrainOffsets = resetWrites.Select(w => w.Offset).Concat(drainWrites.Select(w => w.Offset)).ToArray();
        bool finisherNearDrain = finishers.Any(f => resetDrainOffsets.Any(o => Math.Abs(f.offset - o) <= 192));
        bool hasChargeLike = chargeWrites.Count > 0 || writes.Any(w => w.Field == GaugeAdd);
        bool hasClampLike = clampWrites.Count > 0;
        bool hasReadyGuard = readySites.Count > 0;
        bool hasDrainReset = resetWrites.Count > 0 || drainWrites.Count > 0;
        bool hasFinisher = finishers.Count > 0;
        bool hasSetupGauge = hasShowBarWrite && hasMaxWrite && hasCurrentWrite;
        bool hasDynamicGauge = hasCurrentRead && (hasChargeLike || hasClampLike || hasReadyGuard || hasDrainReset);
        bool hasFullFlow = hasSetupGauge && hasCurrentRead && hasMaxRead && hasReadyGuard && hasDrainReset && hasFinisher;

        overdriveCandidates++;
        if (hasSetupGauge) setupGauge++;
        if (hasSetupGauge && !hasDynamicGauge && !hasFinisher) setupOnly++;
        if (hasCurrentRead) currentReadScripts++;
        if (hasMaxRead) maxReadScripts++;
        if (hasCurrentWrite) currentWriteScripts++;
        if (hasChargeLike) chargeLikeScripts++;
        if (hasClampLike) clampLikeScripts++;
        if (hasReadyGuard) readyGuardScripts++;
        if (hasDrainReset) drainResetScripts++;
        if (hasFinisher) finisherScripts++;
        if (hasFullFlow) fullFlowScripts++;
        if (chrWrites.Any(w => trackedFields.Contains(w.Field))) chrWriteFieldScripts++;

        string grade = hasFullFlow ? "full-flow-candidate"
            : hasDynamicGauge && hasFinisher ? "dynamic+finisher-candidate"
            : hasDynamicGauge ? "dynamic-gauge-candidate"
            : hasSetupGauge ? "setup-gauge"
            : hasFinisher ? "finisher-only"
            : "overdrive-field-only";

        monsters.Add(new
        {
            id,
            relativePath = SafeRel(path, root),
            grade,
            fields = new
            {
                showOverdriveBarWrites = writes.Count(w => w.Field == ShowOverdriveBar),
                modeWrites = writes.Count(w => w.Field == OverdriveMode),
                currentReads = reads.Count(r => r.Field == OverdriveCurrent),
                currentWrites = currentWriteSites.Count,
                maxReads = reads.Count(r => r.Field == OverdriveMax),
                maxWrites = writes.Count(w => w.Field == OverdriveMax),
                gaugeAddWrites = writes.Count(w => w.Field == GaugeAdd),
                availableWrites = writes.Count(w => w.Field == OverdriveAvailable),
                barCamWrites = writes.Count(w => w.Field == BarFlagCam),
                barPosWrites = writes.Count(w => w.Field == BarPos),
                chrPropertyTrackedWrites = chrWrites.Count(w => trackedFields.Contains(w.Field)),
            },
            flow = new
            {
                setupGauge = hasSetupGauge,
                currentRead = hasCurrentRead,
                maxRead = hasMaxRead,
                chargeLike = hasChargeLike,
                clampLike = hasClampLike,
                readyGuard = hasReadyGuard,
                drainReset = hasDrainReset,
                finisher = hasFinisher,
                finisherNearDrain,
                fullFlow = hasFullFlow,
            },
            currentWriteValues = currentWriteSites.Select(WriteLabel).Distinct().Take(16).ToArray(),
            chargeWriteSites = chargeWrites.Select(WriteLabel).Take(12).ToArray(),
            clampWriteSites = clampWrites.Select(WriteLabel).Take(12).ToArray(),
            resetWriteSites = resetWrites.Select(WriteLabel).Take(12).ToArray(),
            drainWriteSites = drainWrites.Select(WriteLabel).Take(12).ToArray(),
            mathWriteSites = mathWrites.Select(WriteLabel).Take(12).ToArray(),
            readySites = readySites.Take(16).ToArray(),
            finishers = finishers.Select(f => new { offset = $"0x{f.offset:X4}", f.operandHex, f.name, f.target, f.force }).Take(16).ToArray(),
        });
    }

    bool pass = overdriveCandidates > 0;
    Console.WriteLine("=== AiScriptLab --overdrive-flow (research: dynamic gauge/drain/finisher) ===");
    Console.WriteLine($"scripted monsters           : {scripted}");
    Console.WriteLine($"overdrive field/finisher    : {overdriveCandidates}");
    Console.WriteLine($"setup gauge candidates      : {setupGauge}  (setup-only: {setupOnly})");
    Console.WriteLine($"current read/write scripts  : {currentReadScripts}/{currentWriteScripts}");
    Console.WriteLine($"max read scripts            : {maxReadScripts}");
    Console.WriteLine($"charge/clamp-like scripts   : {chargeLikeScripts}/{clampLikeScripts}");
    Console.WriteLine($"ready guard scripts         : {readyGuardScripts}");
    Console.WriteLine($"drain/reset scripts         : {drainResetScripts}");
    Console.WriteLine($"finisher scripts            : {finisherScripts}");
    Console.WriteLine($"full flow candidates        : {fullFlowScripts}");
    Console.WriteLine($"tracked writeChrProperty    : {chrWriteFieldScripts}");
    Console.WriteLine(pass ? "VERDICT: PASS (read-only research)" : "VERDICT: FAIL");
    foreach (object sample in monsters
                 .Where(m => JsonSerializer.Serialize(m).Contains("full-flow-candidate")
                             || JsonSerializer.Serialize(m).Contains("dynamic+finisher-candidate"))
                 .Take(8))
        Console.WriteLine("sample: " + JsonSerializer.Serialize(sample));

    if (!string.IsNullOrWhiteSpace(jsonOut))
    {
        string? dir = Path.GetDirectoryName(jsonOut);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(jsonOut, JsonSerializer.Serialize(new
        {
            generatedAtUtc = DateTime.UtcNow.ToString("O"),
            root,
            scripted,
            overdriveCandidates,
            setupGauge,
            setupOnly,
            currentReadScripts,
            maxReadScripts,
            currentWriteScripts,
            chargeLikeScripts,
            clampLikeScripts,
            readyGuardScripts,
            drainResetScripts,
            finisherScripts,
            fullFlowScripts,
            chrWriteFieldScripts,
            pass,
            aggregates = new
            {
                fieldReads = fieldReads.Select(kv => new { field = Hex(kv.Key), name = AiChrPropertyNames.Get(kv.Key) ?? "raw", count = kv.Value }).ToArray(),
                fieldWrites = fieldWrites.Select(kv => new { field = Hex(kv.Key), name = AiChrPropertyNames.Get(kv.Key) ?? "raw", count = kv.Value }).ToArray(),
                overdriveFinisherCommands = commandUse.Select(kv =>
                {
                    AiCommandDecode d = AiCommandId.Decode(kv.Key);
                    return new { operand = Hex(kv.Key), name = d.Name, category = d.Category?.ToString() ?? "raw", count = kv.Value };
                }).ToArray(),
            },
            monsters,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"json={jsonOut}");
    }

    return pass ? 0 : 1;

    static bool LiteralAt(IReadOnlyList<AiInstruction> ins, int index, out ushort value)
    {
        value = 0;
        if (index < 0 || index >= ins.Count) return false;
        if (ins[index].Opcode != PUSHII) return false;
        value = ins[index].Operand;
        return true;
    }

    static ushort? LiteralValueAt(IReadOnlyList<AiInstruction> ins, int index)
    {
        if (index < 0 || index >= ins.Count) return null;
        return ins[index].Opcode == PUSHII ? ins[index].Operand : null;
    }

    static string ValueShapeAt(IReadOnlyList<AiInstruction> ins, int index)
    {
        if (index < 0 || index >= ins.Count) return "missing";
        AiInstruction i = ins[index];
        return i.HasOperand
            ? $"{AiScript_File.Mnemonic(i.Opcode)} {Hex(i.Operand)}"
            : AiScript_File.Mnemonic(i.Opcode);
    }

    static bool HasGetStatFieldBetween(IReadOnlyList<AiInstruction> ins, int start, int endExclusive, ushort field)
    {
        for (int i = Math.Max(0, start); i < Math.Min(ins.Count, endExclusive); i++)
            if (ins[i].Opcode is (CALL or CALLPOPA)
                && (ins[i].Operand == GetStatField || ins[i].Operand == ReadChrProperty)
                && LiteralAt(ins, i - 1, out ushort f)
                && f == field)
                return true;
        return false;
    }

    static bool HasOpcodeBetween(IReadOnlyList<AiInstruction> ins, int start, int endExclusive, byte opcode)
    {
        for (int i = Math.Max(0, start); i < Math.Min(ins.Count, endExclusive); i++)
            if (ins[i].Opcode == opcode)
                return true;
        return false;
    }

    static bool HasAnyOpcodeBetween(IReadOnlyList<AiInstruction> ins, int start, int endExclusive, HashSet<byte> opcodes)
    {
        for (int i = Math.Max(0, start); i < Math.Min(ins.Count, endExclusive); i++)
            if (opcodes.Contains(ins[i].Opcode))
                return true;
        return false;
    }

    static bool IsOverdriveFinisherName(string name)
    {
        string n = name.ToLowerInvariant();
        bool Has(params string[] bits) => bits.Any(n.Contains);
        return Has(
            "diamond dust", "hellfire", "mega flare", "oblivion",
            "aerospark", "energy blast", "energy ray", "thor's hammer", "thor’s hammer",
            "zanmato", "grand summon", "daigoro", "wakizashi",
            "delta attack", "mega-graviton", "mega graviton", "calamity", "passado", "razzia");
    }

    static string WriteLabel((int Index, int Offset, ushort Field, ushort? LiteralValue, string ValueShape) write)
        => $"0x{write.Offset:X4}:{AiChrPropertyNames.Get(write.Field) ?? Hex(write.Field)}={write.ValueShape}";

    static string SafeRel(string path, string root)
    {
        try { return Path.GetRelativePath(root, path); }
        catch { return path; }
    }

    static string Hex(ushort value) => $"0x{value:X4}";

    static void Inc<TKey>(IDictionary<TKey, int> map, TKey key, int delta = 1) where TKey : notnull
    {
        map.TryGetValue(key, out int n);
        map[key] = n + delta;
    }
}

static int OverdriveModeResearch(IReadOnlyList<string> files, string root, string? jsonOut)
{
    const byte PUSHII = 0xAE, CALL = 0xB5, CALLPOPA = 0xD8;
    const ushort ReadChrProperty = 0x700F, GetStatField = 0x70AA, SetStatField = 0x70AB, WriteChrProperty = 0x7018;
    const ushort OverdriveMode = 0x0012;
    var knownModes = new (ushort Value, string Name)[]
    {
        (0x00, "Warrior"),
        (0x01, "Comrade"),
        (0x02, "Stoic"),
        (0x03, "Healer"),
        (0x04, "Tactician"),
        (0x05, "Victim"),
        (0x06, "Dancer"),
        (0x07, "Avenger"),
        (0x08, "Slayer"),
        (0x09, "Hero"),
        (0x0A, "Rook"),
        (0x0B, "Victor"),
        (0x0C, "Coward"),
        (0x0D, "Ally"),
        (0x0E, "Sufferer"),
        (0x0F, "Daredevil"),
        (0x10, "Loner"),
        (0x11, "Unused1"),
        (0x12, "Unused2"),
        (0x13, "Aeon"),
    };

    int scripted = 0, modeReadScripts = 0, modeWriteScripts = 0, modeWritesTotal = 0;
    int combatMapped = 0, combatWithEntrypoint3 = 0, combatWithoutEntrypoint3 = 0;
    var literalValues = new SortedDictionary<ushort, int>();
    var entrypointCounts = new SortedDictionary<int, int>();
    var samples = new List<object>();
    var hookSamples = new List<object>();

    foreach (string path in files)
    {
        string id = Path.GetFileNameWithoutExtension(path);
        byte[] monster;
        byte[]? aiFile;
        try
        {
            monster = File.ReadAllBytes(path);
            aiFile = AiScript_File.SliceAiFileFromMonster(monster);
        }
        catch { continue; }
        if (aiFile == null) continue;

        AiScriptFile script;
        try { script = AiScript_File.Read(aiFile); }
        catch { continue; }
        if (!script.HasScript) continue;
        scripted++;

        if (AiWorkerMapping.TryResolveCombatOnTurn(monster, script, out AiEventHook hook, out string hookError))
        {
            combatMapped++;
            int entrypoints = script.Workers[hook.WorkerIndex].Entrypoints.Count;
            Inc(entrypointCounts, entrypoints);
            if (entrypoints > 3) combatWithEntrypoint3++;
            else combatWithoutEntrypoint3++;
            if (hookSamples.Count < 12 && (entrypoints > 3 || entrypoints <= hook.EntrypointIndex + 1))
            {
                hookSamples.Add(new
                {
                    id,
                    relativePath = SafeRel(path, root),
                    hook = new { hook.WorkerIndex, hook.EntrypointIndex, entrypoints, hasEntrypoint3 = entrypoints > 3 },
                });
            }
        }

        var reads = new List<string>();
        var writes = new List<(int Offset, ushort? Literal, string Shape, string CallShape)>();
        IReadOnlyList<AiInstruction> ins = script.Instructions;
        for (int i = 0; i < ins.Count; i++)
        {
            AiInstruction call = ins[i];
            if (call.Opcode is not (CALL or CALLPOPA))
                continue;

            if ((call.Operand == GetStatField || call.Operand == ReadChrProperty)
                && LiteralAt(ins, i - 1, out ushort readField)
                && readField == OverdriveMode)
            {
                reads.Add($"0x{call.Offset:X4}:{CallName(call.Operand)}");
            }
            else if ((call.Operand == SetStatField || call.Operand == WriteChrProperty)
                     && LiteralAt(ins, i - 2, out ushort writeField)
                     && writeField == OverdriveMode)
            {
                ushort? literal = LiteralValueAt(ins, i - 1);
                if (literal is ushort v) Inc(literalValues, v);
                writes.Add((call.Offset, literal, ValueShapeAt(ins, i - 1), CallName(call.Operand)));
            }
        }

        if (reads.Count > 0) modeReadScripts++;
        if (writes.Count > 0)
        {
            modeWriteScripts++;
            modeWritesTotal += writes.Count;
            samples.Add(new
            {
                id,
                relativePath = SafeRel(path, root),
                writes = writes.Select(w => new
                {
                    offset = $"0x{w.Offset:X4}",
                    value = w.Literal.HasValue ? $"0x{w.Literal.Value:X2}" : null,
                    name = w.Literal.HasValue ? ModeName(w.Literal.Value) : "dynamic/unknown",
                    shape = w.Shape,
                    call = w.CallShape,
                }).ToArray(),
                reads = reads.ToArray(),
            });
        }
    }

    bool pass = knownModes.Length == 20 && modeWritesTotal > 0;
    Console.WriteLine("=== AiScriptLab --overdrive-modes (research: mode ids + corpus usage) ===");
    Console.WriteLine($"scripted monsters          : {scripted}");
    Console.WriteLine($"mode read/write scripts    : {modeReadScripts}/{modeWriteScripts}");
    Console.WriteLine($"mode writes total          : {modeWritesTotal}");
    Console.WriteLine($"combat onTurn mapped       : {combatMapped}");
    Console.WriteLine($"combat with entrypoint 3   : {combatWithEntrypoint3}  (without: {combatWithoutEntrypoint3})");
    Console.WriteLine("known modes                : " + string.Join(", ", knownModes.Select(m => $"{m.Value}= {m.Name}")));
    Console.WriteLine("literal mode writes        : " + string.Join(", ", literalValues.Select(kv => $"{kv.Key}= {ModeName(kv.Key)} x{kv.Value}")));
    Console.WriteLine(pass ? "VERDICT: PASS (read-only research)" : "VERDICT: FAIL");
    foreach (object sample in samples.Take(8))
        Console.WriteLine("sample: " + JsonSerializer.Serialize(sample));

    if (!string.IsNullOrWhiteSpace(jsonOut))
    {
        string? dir = Path.GetDirectoryName(jsonOut);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(jsonOut, JsonSerializer.Serialize(new
        {
            generatedAtUtc = DateTime.UtcNow.ToString("O"),
            root,
            scripted,
            modeReadScripts,
            modeWriteScripts,
            modeWritesTotal,
            combatMapped,
            combatWithEntrypoint3,
            combatWithoutEntrypoint3,
            pass,
            knownModes = knownModes.Select(m => new
            {
                value = $"0x{m.Value:X2}",
                decimalValue = m.Value,
                m.Name,
                source = "docs/reverse/hexpat_ffx/all/ply_save.bin.hexpat enum OverdriveModes",
            }).ToArray(),
            literalModeWrites = literalValues.Select(kv => new
            {
                value = $"0x{kv.Key:X2}",
                decimalValue = kv.Key,
                name = ModeName(kv.Key),
                count = kv.Value,
            }).ToArray(),
            combatEntrypointCounts = entrypointCounts.Select(kv => new { entrypoints = kv.Key, count = kv.Value }).ToArray(),
            samples,
            hookSamples,
            passiveChargeAssessment = new
            {
                aiScriptVerdict = "No proven AI onTick/per-frame event in current editor mapping; only CombatHandler.onTurn and entrypoint 3/onHit are actively used.",
                runtimeVerdict = "MemoryChr has Ovr_charge@0x5BC and Ovr_charge_max@0x5BD; FfxHooks per-frame pump exists in prior research, but needs a turn-edge/tick throttle before safe gameplay.",
            }
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"json={jsonOut}");
    }

    return pass ? 0 : 1;

    string ModeName(ushort value) => knownModes.FirstOrDefault(m => m.Value == value).Name ?? $"unknown-{value}";
    static string CallName(ushort callId) => callId switch
    {
        ReadChrProperty => "readChrProperty",
        GetStatField => "getStatField",
        SetStatField => "setStatField",
        WriteChrProperty => "writeChrProperty",
        _ => $"call 0x{callId:X4}",
    };
    static bool LiteralAt(IReadOnlyList<AiInstruction> ins, int index, out ushort value)
    {
        value = 0;
        if (index < 0 || index >= ins.Count) return false;
        if (ins[index].Opcode != PUSHII) return false;
        value = ins[index].Operand;
        return true;
    }
    static ushort? LiteralValueAt(IReadOnlyList<AiInstruction> ins, int index)
    {
        if (index < 0 || index >= ins.Count) return null;
        return ins[index].Opcode == PUSHII ? ins[index].Operand : null;
    }
    static string ValueShapeAt(IReadOnlyList<AiInstruction> ins, int index)
    {
        if (index < 0 || index >= ins.Count) return "missing";
        AiInstruction i = ins[index];
        return i.HasOperand
            ? $"{AiScript_File.Mnemonic(i.Opcode)} 0x{i.Operand:X4}"
            : AiScript_File.Mnemonic(i.Opcode);
    }
    static string SafeRel(string path, string root)
    {
        try { return Path.GetRelativePath(root, path); }
        catch { return path; }
    }
    static void Inc<TKey>(IDictionary<TKey, int> map, TKey key, int delta = 1) where TKey : notnull
    {
        map.TryGetValue(key, out int n);
        map[key] = n + delta;
    }
}

static int SinCensus(IReadOnlyList<string> files, string root, string? jsonOut)
{
    var monsters = new List<object>();
    var callUse = new SortedDictionary<ushort, int>();
    var opcodeUse = new SortedDictionary<byte, int>();
    var commandUse = new SortedDictionary<ushort, int>();
    var targetUse = new SortedDictionary<ushort, int>();
    var fieldReadUse = new SortedDictionary<ushort, int>();
    var fieldWriteUse = new SortedDictionary<ushort, int>();
    var actionKindUse = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    var patternUse = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    var failures = new List<object>();
    int aiFiles = 0, scripted = 0, stubs = 0, onTurnResolved = 0;

    foreach (string path in files)
    {
        string id = Path.GetFileNameWithoutExtension(path);
        byte[] monster;
        byte[]? aiFile;
        try
        {
            monster = File.ReadAllBytes(path);
            aiFile = AiScript_File.SliceAiFileFromMonster(monster);
        }
        catch (Exception ex)
        {
            failures.Add(new { id, stage = "read-file", error = ex.Message });
            continue;
        }

        if (aiFile == null)
        {
            monsters.Add(new
            {
                id,
                path = Rel(path, root),
                hasAiFile = false,
                hasScript = false,
                summary = "sem AiFile"
            });
            continue;
        }

        aiFiles++;
        AiScriptFile script;
        try { script = AiScript_File.Read(aiFile); }
        catch (Exception ex)
        {
            failures.Add(new { id, stage = "parse-ai", error = ex.Message });
            monsters.Add(new { id, path = Rel(path, root), hasAiFile = true, parseError = ex.Message });
            continue;
        }

        if (!script.HasScript)
        {
            stubs++;
            monsters.Add(new
            {
                id,
                path = Rel(path, root),
                hasAiFile = true,
                hasScript = false,
                aiLength = aiFile.Length,
                workerCount = script.Workers.Count,
                summary = "stub / sem script"
            });
            continue;
        }

        scripted++;
        foreach (AiInstruction ins in script.Instructions)
        {
            Inc(opcodeUse, ins.Opcode);
            if (ins.OperandKind == AiOperandKind.FuncId)
                Inc(callUse, ins.Operand);
        }

        IReadOnlyList<AiDetectedAction> actions;
        try { actions = AiAutomation.DetectActions(script); }
        catch (Exception ex)
        {
            failures.Add(new { id, stage = "detect-actions", error = ex.Message });
            actions = Array.Empty<AiDetectedAction>();
        }

        foreach (AiDetectedAction action in actions)
        {
            Inc(actionKindUse, action.Kind.ToString());
            if (action.Kind == AiActionKind.Command)
            {
                Inc(commandUse, action.CommandOperand);
                if (action.TargetPushOffset >= 0)
                    Inc(targetUse, action.TargetOperand);
            }
            else if (action.Kind == AiActionKind.Buff)
            {
                Inc(fieldWriteUse, action.FieldId);
            }
            else if (action.Kind == AiActionKind.Stat)
            {
                Inc(fieldWriteUse, action.FieldId);
            }
        }

        var propertyReads = new SortedDictionary<ushort, int>();
        var propertyWrites = new SortedDictionary<ushort, int>();
        ScanPropertyCalls(script.Instructions, propertyReads, propertyWrites);
        foreach (var kv in propertyReads) Inc(fieldReadUse, kv.Key, kv.Value);
        foreach (var kv in propertyWrites) Inc(fieldWriteUse, kv.Key, kv.Value);

        bool hookOk = AiWorkerMapping.TryResolveCombatOnTurn(monster, script, out AiEventHook hook, out string hookError);
        if (hookOk) onTurnResolved++;

        var patterns = DetectPatternFlags(script, actions, propertyReads, propertyWrites);
        foreach (string pattern in patterns) Inc(patternUse, pattern);

        var commandRows = actions
            .Where(a => a.Kind == AiActionKind.Command)
            .GroupBy(a => a.CommandOperand)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Select(g =>
            {
                AiCommandDecode d = AiCommandId.Decode(g.Key);
                return new
                {
                    operand = Hex(g.Key),
                    name = d.Name,
                    category = d.Category?.ToString() ?? "raw",
                    count = g.Count(),
                    force = g.Count(a => a.ForcePerform),
                    perform = g.Count(a => !a.ForcePerform)
                };
            })
            .ToList();

        var targetRows = actions
            .Where(a => a.Kind == AiActionKind.Command && a.TargetPushOffset >= 0)
            .GroupBy(a => a.TargetOperand)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Select(g => new
            {
                operand = Hex(g.Key),
                name = AiTargetNames.Get(g.Key) ?? "raw",
                count = g.Count(),
                literal = g.Count(a => a.TargetIsLiteral),
                computed = g.Count(a => !a.TargetIsLiteral)
            })
            .ToList();

        var readRows = propertyReads
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key)
            .Select(kv => new { field = Hex(kv.Key), name = AiChrPropertyNames.Get(kv.Key) ?? "raw", count = kv.Value })
            .ToList();

        var writeRows = propertyWrites
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key)
            .Select(kv => new { field = Hex(kv.Key), name = AiChrPropertyNames.Get(kv.Key) ?? "raw", count = kv.Value })
            .ToList();

        monsters.Add(new
        {
            id,
            path = Rel(path, root),
            hasAiFile = true,
            hasScript = true,
            aiLength = aiFile.Length,
            codeLength = script.CodeLength,
            instructionCount = script.Instructions.Count,
            workerCount = script.Workers.Count,
            workers = script.Workers.Select(w => new
            {
                index = w.Index,
                type = w.InferredType,
                entrypoints = w.Entrypoints.Count,
                jumps = w.JumpTargets.Count
            }).ToList(),
            combatOnTurn = hookOk
                ? new { resolved = true, worker = hook.WorkerIndex, entrypoint = hook.EntrypointIndex, error = "" }
                : new { resolved = false, worker = -1, entrypoint = -1, error = hookError },
            actionCounts = actions.GroupBy(a => a.Kind.ToString()).ToDictionary(g => g.Key, g => g.Count()),
            commands = commandRows,
            targets = targetRows,
            propertyReads = readRows.Take(32).ToList(),
            propertyWrites = writeRows.Take(32).ToList(),
            patterns,
            summary = $"{script.Workers.Count} worker(s), {actions.Count} action(s), {commandRows.Count} comando(s), {readRows.Count + writeRows.Count} field(s)"
        });
    }

    var output = new
    {
        generatedAtUtc = DateTime.UtcNow.ToString("O"),
        root,
        filesScanned = files.Count,
        aiFiles,
        scripted,
        stubs,
        onTurnResolved,
        commandDictionary = new
        {
            total = AiCommandId.AllOptions().Count,
            character = AiCommandId.OptionsFor(AiCommandCategory.Character).Count,
            monster1 = AiCommandId.OptionsFor(AiCommandCategory.Monster).Count,
            monster2 = AiCommandId.OptionsFor(AiCommandCategory.Monster2).Count
        },
        aggregates = new
        {
            opcodes = opcodeUse.Select(kv => new { opcode = $"0x{kv.Key:X2}", mnemonic = AiScript_File.Mnemonic(kv.Key), count = kv.Value }).ToList(),
            calls = callUse.Select(kv => new { id = Hex(kv.Key), name = $"{AiScript_File.CallNamespace(kv.Key)}.{AiScript_File.CallName(kv.Key)}", count = kv.Value }).ToList(),
            commands = commandUse.Select(kv =>
            {
                AiCommandDecode d = AiCommandId.Decode(kv.Key);
                return new { operand = Hex(kv.Key), name = d.Name, category = d.Category?.ToString() ?? "raw", count = kv.Value };
            }).ToList(),
            targets = targetUse.Select(kv => new { operand = Hex(kv.Key), name = AiTargetNames.Get(kv.Key) ?? "raw", count = kv.Value }).ToList(),
            fieldReads = fieldReadUse.Select(kv => new { field = Hex(kv.Key), name = AiChrPropertyNames.Get(kv.Key) ?? "raw", count = kv.Value }).ToList(),
            fieldWrites = fieldWriteUse.Select(kv => new { field = Hex(kv.Key), name = AiChrPropertyNames.Get(kv.Key) ?? "raw", count = kv.Value }).ToList(),
            actionKinds = actionKindUse.Select(kv => new { kind = kv.Key, count = kv.Value }).ToList(),
            patterns = patternUse.Select(kv => new { pattern = kv.Key, monsters = kv.Value }).ToList()
        },
        monsters,
        failures
    };

    Console.WriteLine("=== AiScriptLab --sin-census ===");
    Console.WriteLine($"files={files.Count} aiFiles={aiFiles} scripted={scripted} stubs={stubs} onTurn={onTurnResolved}");
    Console.WriteLine($"commandsUsed={commandUse.Count} fieldsRead={fieldReadUse.Count} fieldsWritten={fieldWriteUse.Count} targets={targetUse.Count}");
    Console.WriteLine($"patterns={string.Join(", ", patternUse.Select(kv => $"{kv.Key}:{kv.Value}"))}");

    if (!string.IsNullOrWhiteSpace(jsonOut))
    {
        string? dir = Path.GetDirectoryName(jsonOut);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(jsonOut, JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"json={jsonOut}");
    }

    return failures.Count == 0 ? 0 : 1;

    static string Rel(string path, string root)
    {
        try { return Path.GetRelativePath(root, path); }
        catch { return path; }
    }

    static string Hex(ushort value) => $"0x{value:X4}";

    static void Inc<TKey>(IDictionary<TKey, int> map, TKey key, int delta = 1) where TKey : notnull
    {
        map.TryGetValue(key, out int n);
        map[key] = n + delta;
    }

    static bool LiteralAt(IReadOnlyList<AiInstruction> ins, int index, out ushort value)
    {
        value = 0;
        if (index < 0 || index >= ins.Count) return false;
        if (ins[index].Opcode != 0xAE) return false;
        value = ins[index].Operand;
        return true;
    }

    static void ScanPropertyCalls(IReadOnlyList<AiInstruction> ins, IDictionary<ushort, int> reads, IDictionary<ushort, int> writes)
    {
        for (int i = 0; i < ins.Count; i++)
        {
            AiInstruction call = ins[i];
            if (call.OperandKind != AiOperandKind.FuncId) continue;
            switch (call.Operand)
            {
                case 0x700F: // readChrProperty(actor, field)
                    if (LiteralAt(ins, i - 1, out ushort readField)) Inc(reads, readField);
                    break;
                case 0x70AA: // getStatField(field)
                    if (LiteralAt(ins, i - 1, out ushort statReadField)) Inc(reads, statReadField);
                    break;
                case 0x7018: // writeChrProperty(actor, field, value)
                    if (LiteralAt(ins, i - 2, out ushort writeField)) Inc(writes, writeField);
                    break;
                case 0x70AB: // setStatField(field, value)
                    if (LiteralAt(ins, i - 2, out ushort statWriteField)) Inc(writes, statWriteField);
                    break;
            }
        }
    }

    static IReadOnlyList<string> DetectPatternFlags(
        AiScriptFile script,
        IReadOnlyList<AiDetectedAction> actions,
        IReadOnlyDictionary<ushort, int> reads,
        IReadOnlyDictionary<ushort, int> writes)
    {
        var flags = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        bool HasCall(ushort id) => script.Instructions.Any(i => i.OperandKind == AiOperandKind.FuncId && i.Operand == id);
        bool HasOpcode(byte op) => script.Instructions.Any(i => i.Opcode == op);
        bool HasRead(ushort field) => reads.ContainsKey(field);
        bool HasWrite(ushort field) => writes.ContainsKey(field);

        if (actions.Any(a => a.Kind == AiActionKind.Command)) flags.Add("command-action");
        if (actions.Any(a => a.Kind == AiActionKind.Command && a.ForcePerform)) flags.Add("force-perform");
        if (actions.Any(a => a.Kind == AiActionKind.Buff)) flags.Add("chr-property-write");
        if (actions.Any(a => a.Kind == AiActionKind.Stat)) flags.Add("set-stat-field");
        if (HasCall(0x00A9) && HasOpcode(0x18)) flags.Add("rng-1-in-k");
        if ((HasRead(0x0000) || HasRead(0x0002) || HasRead(0x0119)) && HasOpcode(0x0B)) flags.Add("hp-or-neardeath-guard");
        if (HasRead(0x0000) && HasRead(0x0002)) flags.Add("hp-percent-candidate");
        if (HasWrite(0x0013) || HasWrite(0x0014) || HasWrite(0x0089) || HasRead(0x0013) || HasRead(0x0014)) flags.Add("overdrive-candidate");
        if (HasWrite(0x0089)) flags.Add("show-overdrive-bar");
        if (HasRead(0x00DA) || HasWrite(0x00DA) || HasRead(0x0114) || HasWrite(0x0114)) flags.Add("turn-or-round-field");
        if (HasCall(0x7010) || HasCall(0x7025)) flags.Add("find-matching-target");
        if (HasCall(0x7071)) flags.Add("status-off");
        if (HasCall(0x7050)) flags.Add("revive-or-reinitialize");
        if (HasCall(0x7028) || HasCall(0x706B) || HasCall(0x7036)) flags.Add("visual-body-state");
        if (actions.Where(a => a.Kind == AiActionKind.Command).GroupBy(a => a.WorkerIndex).Any(g => g.Count() >= 4)) flags.Add("rich-rotation");
        return flags.ToList();
    }
}

static int SinGrimoire(IReadOnlyList<string> files, string root, string? jsonOut)
{
    var usage = new SortedDictionary<ushort, int>();
    var targetModes = new Dictionary<ushort, SortedDictionary<ushort, int>>();
    int scripted = 0;

    foreach (string path in files)
    {
        byte[] monster;
        byte[]? aiFile;
        try
        {
            monster = File.ReadAllBytes(path);
            aiFile = AiScript_File.SliceAiFileFromMonster(monster);
        }
        catch { continue; }
        if (aiFile == null) continue;

        AiScriptFile script;
        try { script = AiScript_File.Read(aiFile); }
        catch { continue; }
        if (!script.HasScript) continue;
        scripted++;

        IReadOnlyList<AiDetectedAction> actions;
        try { actions = AiAutomation.DetectActions(script); }
        catch { continue; }

        foreach (AiDetectedAction a in actions.Where(a => a.Kind == AiActionKind.Command))
        {
            Inc(usage, a.CommandOperand);
            if (a.TargetPushOffset >= 0)
            {
                if (!targetModes.TryGetValue(a.CommandOperand, out SortedDictionary<ushort, int>? perTarget))
                {
                    perTarget = new SortedDictionary<ushort, int>();
                    targetModes[a.CommandOperand] = perTarget;
                }
                Inc(perTarget, a.TargetOperand);
            }
        }
    }

    var entries = AiCommandId.AllOptions()
        .OrderBy(o => o.Category)
        .ThenBy(o => o.CommandId)
        .Select(o =>
        {
            usage.TryGetValue(o.Operand, out int count);
            targetModes.TryGetValue(o.Operand, out SortedDictionary<ushort, int>? targets);
            string role = ClassifyCommand(o.Name);
            return new
            {
                operand = Hex(o.Operand),
                category = o.Category.ToString(),
                commandId = Hex(o.CommandId),
                name = o.Name,
                role,
                danger = DangerFor(role, o.Name, count),
                corpusUse = count,
                commonTargets = (targets ?? new SortedDictionary<ushort, int>())
                    .OrderByDescending(kv => kv.Value)
                    .ThenBy(kv => kv.Key)
                    .Take(8)
                    .Select(kv => new { operand = Hex(kv.Key), name = AiTargetNames.Get(kv.Key) ?? "raw", count = kv.Value })
                    .ToList(),
                search = $"{o.Category} {o.CommandId:X3} {o.Operand:X4} {o.Name} {role}"
            };
        })
        .ToList();

    var output = new
    {
        generatedAtUtc = DateTime.UtcNow.ToString("O"),
        root,
        scriptedMonsters = scripted,
        schema = "sin-command-grimoire.v1",
        summary = new
        {
            total = entries.Count,
            usedInCorpus = entries.Count(e => e.corpusUse > 0),
            character = entries.Count(e => e.category == AiCommandCategory.Character.ToString()),
            monster1 = entries.Count(e => e.category == AiCommandCategory.Monster.ToString()),
            monster2 = entries.Count(e => e.category == AiCommandCategory.Monster2.ToString()),
            roles = entries.GroupBy(e => e.role)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .Select(g => new { role = g.Key, total = g.Count(), used = g.Count(e => e.corpusUse > 0) })
                .ToList()
        },
        entries
    };

    Console.WriteLine("=== AiScriptLab --sin-grimoire ===");
    Console.WriteLine($"commands={entries.Count} used={entries.Count(e => e.corpusUse > 0)} scripted={scripted}");
    foreach (var role in output.summary.roles)
        Console.WriteLine($"  {role.role,-18} total={role.total,3} used={role.used,3}");

    if (!string.IsNullOrWhiteSpace(jsonOut))
    {
        string? dir = Path.GetDirectoryName(jsonOut);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(jsonOut, JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"json={jsonOut}");
    }

    return 0;

    static string Hex(ushort value) => $"0x{value:X4}";

    static void Inc<TKey>(IDictionary<TKey, int> map, TKey key, int delta = 1) where TKey : notnull
    {
        map.TryGetValue(key, out int n);
        map[key] = n + delta;
    }

    static string ClassifyCommand(string name)
    {
        string n = name.ToLowerInvariant();
        bool Has(params string[] bits) => bits.Any(n.Contains);

        if (Has("multi", "double", "triple", "quad", "cast 2", "repeater")) return "multi-cast";
        if (Has("diamond dust", "hellfire", "mega flare", "oblivion", "aerospark", "energy blast", "energy ray", "thor's hammer", "thor’s hammer", "zanmato", "grand summon", "daigoro", "wakizashi")) return "aeon-overdrive";
        if (Has("doom", "death", "countdown", "eject", "remove", "instant", "self-destruct", "immolation", "mortibsorption")) return "lethal-special";
        if (Has("cure", "cura", "curaga", "life", "revive", "regen", "white wind", "pray", "heal", "remedy", "esuna", "auto-potion")) return "heal-support";
        if (Has("haste", "protect", "shell", "reflect", "nul", "focus", "cheer", "aim", "luck", "jynx", "entrust", "psych up")) return "buff-support";
        if (Has("sleep", "silence", "dark", "blind", "poison", "zombie", "petrify", "stone", "slow", "berserk", "confuse", "curse", "threaten", "provoke", "power break", "magic break", "armor break", "mental break", "delay", "full break", "dispel", "break", "negation", "atrophy", "voodoo")) return "debuff-status";
        if (Has("drain", "osmose", "absorb", "aspir")) return "drain-resource";
        if (Has("fire", "fira", "firaga", "blizzard", "blizzara", "blizzaga", "thunder", "thundara", "thundaga", "water", "watera", "waterga", "flare", "ultima", "bio", "demi", "holy", "meteor", "earthquake", "quake", "graviton", "megiddo", "flame", "calamity")) return "magic-damage";
        if (Has("breath", "gaze", "song", "dance", "pharaoh", "bad breath", "seed cannon", "needle", "spines", "aqua", "sonic", "venom", "gas", "ray", "beam", "blaster", "mortar", "missiles", "hydraulic", "reaper", "beak of woe", "shimmering rain", "core energy", "regurgitate", "passado", "razzia", "reppageki")) return "monster-special";
        if (Has("attack", "hit", "slash", "claw", "bite", "gnaw", "tail", "horn", "kick", "punch", "tackle", "charge", "strike", "fang", "lunge", "swipe", "rush", "counter", "haymaker", "gore", "munch", "smack", "ram", "body splash", "heave", "leaping", "swallow")) return "physical-attack";
        if (Has("wait", "escape", "run away", "landing", "take off", "motion", "reaction", "mimic", "special", "summon", "move", "crawl", "open", "charging", "readying", "waddle", "bingo", "wrong", "nothing", "single", "all", "transform", "drawn to sin", "wings start")) return "technical-script";
        return "uncategorized";
    }

    static string DangerFor(string role, string name, int corpusUse)
    {
        string n = name.ToLowerInvariant();
        if (role is "lethal-special" or "aeon-overdrive") return "high";
        if (n.Contains("zanmato") || n.Contains("death") || n.Contains("eject")) return "high";
        if (role is "multi-cast" or "monster-special") return corpusUse > 0 ? "medium" : "high";
        if (role is "technical-script" or "uncategorized") return "audit";
        return corpusUse > 0 ? "low-medium" : "medium";
    }
}

static int IndirectDispatchIntentEditorsRt0(IReadOnlyList<string> files, string root, string? jsonOut)
{
    var failures = new List<string>();
    string repoRoot = FindRepoRoot();
    string routeWindowPath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiPhaseRotationAdvanced_Window.axaml");
    string advancedWindowCodeBehindPath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiPhaseRotationAdvanced_Window.axaml.cs");
    string payloadWindowPath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiIndirectDispatchPayload_Window.axaml");
    string targetWindowPath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiIndirectDispatchTarget_Window.axaml");
    string nextStateWindowPath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiIndirectDispatchNextState_Window.axaml");
    string fluxWindowPath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiAdvancedFluxThreshold_Window.axaml");
    string fluxWindowCodeBehindPath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiAdvancedFluxThreshold_Window.axaml.cs");
    string companionWindowPath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiAdvancedCompanionActivation_Window.axaml");
    string companionWindowCodeBehindPath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiAdvancedCompanionActivation_Window.axaml.cs");
    string advancedPhasePath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiEditor_DataModel.PhaseRotationAdvanced.cs");
    string advancedFamilyDialogsPath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiEditor_DataModel.AdvancedFamilyDialogs.cs");
    string indirectDispatchAuthoringPath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiEditor_DataModel.IndirectDispatchAuthoring.cs");

    string routeWindow = string.Empty;
    string advancedWindowCodeBehind = string.Empty;
    string advancedPhase = string.Empty;
    string payloadWindow = string.Empty;
    string targetWindow = string.Empty;
    string nextStateWindow = string.Empty;
    string fluxWindow = string.Empty;
    string fluxWindowCodeBehind = string.Empty;
    string companionWindow = string.Empty;
    string companionWindowCodeBehind = string.Empty;
    string advancedFamilyDialogs = string.Empty;
    string indirectDispatchAuthoring = string.Empty;

    try
    {
        routeWindow = File.ReadAllText(routeWindowPath);
        advancedWindowCodeBehind = File.ReadAllText(advancedWindowCodeBehindPath);
        advancedPhase = File.ReadAllText(advancedPhasePath);
        payloadWindow = File.ReadAllText(payloadWindowPath);
        targetWindow = File.ReadAllText(targetWindowPath);
        nextStateWindow = File.ReadAllText(nextStateWindowPath);
        fluxWindow = File.ReadAllText(fluxWindowPath);
        fluxWindowCodeBehind = File.ReadAllText(fluxWindowCodeBehindPath);
        companionWindow = File.ReadAllText(companionWindowPath);
        companionWindowCodeBehind = File.ReadAllText(companionWindowCodeBehindPath);
        advancedFamilyDialogs = File.ReadAllText(advancedFamilyDialogsPath);
        indirectDispatchAuthoring = File.ReadAllText(indirectDispatchAuthoringPath);
    }
    catch (Exception ex)
    {
        failures.Add($"intent editor source read failed: {ex.Message}");
    }

    bool payloadWindowOk = payloadWindow.Contains("ItemsSource=\"{Binding SlotRows}\"", StringComparison.Ordinal)
        && payloadWindow.Contains("Habilidade desta rota", StringComparison.Ordinal)
        && !payloadWindow.Contains("ItemsSource=\"{Binding TargetSlotRows}\"", StringComparison.Ordinal)
        && !payloadWindow.Contains("Text=\"{Binding NextStateText", StringComparison.Ordinal);
    if (!payloadWindowOk)
        failures.Add("payload popup is missing the payload-only surface.");

    bool targetWindowOk = targetWindow.Contains("ItemsSource=\"{Binding TargetSlotRows}\"", StringComparison.Ordinal)
        && targetWindow.Contains("Alvo desta rota", StringComparison.Ordinal)
        && !targetWindow.Contains("ItemsSource=\"{Binding SlotRows}\"", StringComparison.Ordinal)
        && !targetWindow.Contains("Text=\"{Binding NextStateText", StringComparison.Ordinal);
    if (!targetWindowOk)
        failures.Add("target popup is missing the target-only surface.");

    bool nextStateWindowOk = nextStateWindow.Contains("Text=\"{Binding NextStateText, Mode=TwoWay}\"", StringComparison.Ordinal)
        && nextStateWindow.Contains("Próximo passo desta rota", StringComparison.Ordinal)
        && !nextStateWindow.Contains("ItemsSource=\"{Binding SlotRows}\"", StringComparison.Ordinal)
        && !nextStateWindow.Contains("ItemsSource=\"{Binding TargetSlotRows}\"", StringComparison.Ordinal);
    if (!nextStateWindowOk)
        failures.Add("next-state popup is missing the next-state-only surface.");

    bool payloadRouteOk = advancedWindowCodeBehind.Contains("AiIndirectDispatchEditorFocusKind.CommandSlot", StringComparison.Ordinal)
        && advancedWindowCodeBehind.Contains("OpenIndirectDispatchPayloadEditor(focusContext)", StringComparison.Ordinal);
    if (!payloadRouteOk)
        failures.Add("advanced manager is not routing CommandSlot into the payload popup.");

    bool targetRouteOk = advancedWindowCodeBehind.Contains("AiIndirectDispatchEditorFocusKind.TargetSlot", StringComparison.Ordinal)
        && advancedWindowCodeBehind.Contains("OpenIndirectDispatchTargetEditor(focusContext)", StringComparison.Ordinal);
    if (!targetRouteOk)
        failures.Add("advanced manager is not routing TargetSlot into the target popup.");

    bool nextStateRouteOk = advancedWindowCodeBehind.Contains("AiIndirectDispatchEditorFocusKind.NextState", StringComparison.Ordinal)
        && advancedWindowCodeBehind.Contains("OpenIndirectDispatchNextStateEditor(focusContext)", StringComparison.Ordinal);
    if (!nextStateRouteOk)
        failures.Add("advanced manager is not routing NextState into the next-state popup.");

    bool fluxWindowOk = fluxWindow.Contains("Editor raw do Flux", StringComparison.Ordinal)
        && fluxWindow.Contains("Text=\"{Binding RawFormulaSummary}\"", StringComparison.Ordinal)
        && fluxWindow.Contains("Text=\"{Binding AdvancedFluxThresholdApplySummary}\"", StringComparison.Ordinal);
    if (!fluxWindowOk)
        failures.Add("flux popup is missing the dedicated raw-threshold surface.");

    bool fluxRouteOk = routeWindow.Contains("Button_OpenAdvancedFluxThresholdEditor", StringComparison.Ordinal)
        && routeWindow.Contains("Clique para abrir o editor raw desta chain.", StringComparison.Ordinal);
    if (!fluxRouteOk)
        failures.Add("advanced manager is not exposing click-to-open Flux cards.");

    bool fluxCodeBehindOk = advancedWindowCodeBehind.Contains("Button_OpenAdvancedFluxThresholdEditor", StringComparison.Ordinal)
        && advancedWindowCodeBehind.Contains("new MonsterAiAdvancedFluxThreshold_Window(dataModel", StringComparison.Ordinal)
        && fluxWindowCodeBehind.Contains("ApplyAdvancedFluxThresholdEdit(", StringComparison.Ordinal);
    if (!fluxCodeBehindOk)
        failures.Add("Flux cards are not routed into the dedicated raw-threshold popup.");

    bool companionWindowOk = companionWindow.Contains("Editor battle-backed do m213", StringComparison.Ordinal)
        && companionWindow.Contains("Text=\"{Binding AdvancedCompanionActivationApplySummary}\"", StringComparison.Ordinal)
        && !companionWindow.Contains("BooleanNegationConverter", StringComparison.Ordinal);
    if (!companionWindowOk)
        failures.Add("m213 popup is missing the dedicated battle-backed surface or still depends on an unresolved negation converter.");

    bool companionRouteOk = routeWindow.Contains("Button_OpenAdvancedCompanionActivationEditor", StringComparison.Ordinal)
        && routeWindow.Contains("Abrir popup deste package", StringComparison.Ordinal);
    if (!companionRouteOk)
        failures.Add("advanced manager is not exposing the dedicated m213 popup entrypoint.");

    bool companionCodeBehindOk = advancedWindowCodeBehind.Contains("Button_OpenAdvancedCompanionActivationEditor", StringComparison.Ordinal)
        && advancedWindowCodeBehind.Contains("new MonsterAiAdvancedCompanionActivation_Window(dataModel, package)", StringComparison.Ordinal)
        && companionWindowCodeBehind.Contains("ApplyAdvancedCompanionActivationEdit(", StringComparison.Ordinal);
    if (!companionCodeBehindOk)
        failures.Add("m213 package cards are not routed into the battle-backed popup.");

    bool fallbackRouteOk = routeWindow.Contains("Content=\"🧩 Abrir editor completo\"", StringComparison.Ordinal)
        && advancedWindowCodeBehind.Contains("ShowIndirectDispatchWindow(new MonsterAiIndirectDispatchUnit_Window(dataModel))", StringComparison.Ordinal);
    if (!fallbackRouteOk)
        failures.Add("advanced manager no longer exposes the complete editor fallback.");

    bool quickEditHonestCopyOk =
        routeWindow.Contains("AdvancedSelectedPayloadActionSummary", StringComparison.Ordinal)
        && routeWindow.Contains("AdvancedSelectedTargetActionSummary", StringComparison.Ordinal)
        && routeWindow.Contains("AdvancedSelectedConsumersActionSummary", StringComparison.Ordinal)
        && routeWindow.Contains("AdvancedSelectedNextStateActionSummary", StringComparison.Ordinal);
    if (!quickEditHonestCopyOk)
        failures.Add("advanced manager no longer surfaces honest quick-edit copy for payload/target/next-state availability.");

    bool quickCardBindingOk =
        routeWindow.Contains("SelectedAdvancedPayloadCardTarget", StringComparison.Ordinal)
        && routeWindow.Contains("SelectedAdvancedTargetCardTarget", StringComparison.Ordinal)
        && routeWindow.Contains("SelectedAdvancedConsumerCardTarget", StringComparison.Ordinal)
        && routeWindow.Contains("SelectedAdvancedNextStateCardTarget", StringComparison.Ordinal)
        && routeWindow.Contains("CanOpenSelectedAdvancedPayloadCard", StringComparison.Ordinal)
        && routeWindow.Contains("CanOpenSelectedAdvancedTargetCard", StringComparison.Ordinal)
        && routeWindow.Contains("CanOpenSelectedAdvancedConsumerCard", StringComparison.Ordinal)
        && routeWindow.Contains("CanOpenSelectedAdvancedNextStateCard", StringComparison.Ordinal)
        && !routeWindow.Contains("IsEnabled=\"{Binding SelectedAdvancedPhaseUnit.CanOpenEditor}\"", StringComparison.Ordinal);
    if (!quickCardBindingOk)
        failures.Add("advanced manager quick cards are no longer bound to the fallback click targets.");

    bool quickCardTargetModelOk =
        advancedPhase.Contains("BuildSelectedAdvancedCardTarget(", StringComparison.Ordinal)
        && advancedPhase.Contains("SelectedAdvancedPayloadCardTarget", StringComparison.Ordinal)
        && advancedPhase.Contains("SelectedAdvancedConsumerCardTarget", StringComparison.Ordinal)
        && advancedPhase.Contains("AdvancedSelectedConsumersActionSummary", StringComparison.Ordinal)
        && advancedPhase.Contains("return unit.UnitId;", StringComparison.Ordinal);
    if (!quickCardTargetModelOk)
        failures.Add("advanced phase data-model lost the fallback click-target builder for payload/target/consumers/next-state cards.");

    bool focusLaunchGateOk =
        advancedPhase.Contains("guardLaunchContext != null", StringComparison.Ordinal)
        && advancedPhase.Contains("payloadLaunchContext != null", StringComparison.Ordinal)
        && advancedPhase.Contains("targetLaunchContext != null", StringComparison.Ordinal)
        && advancedPhase.Contains("nextStateLaunchContext != null", StringComparison.Ordinal)
        && advancedPhase.Contains("editableTargetSlot?.CanEdit == true", StringComparison.Ordinal)
        && advancedPhase.Contains("unit.EditableNextStateOffset.HasValue", StringComparison.Ordinal)
        && advancedPhase.Contains("unit.CurrentNextStateValue.HasValue", StringComparison.Ordinal);
    if (!focusLaunchGateOk)
        failures.Add("advanced variable-focus rows no longer gate launch contexts to the specific payload/target/next-state proof that actually exists.");

    bool genericLookupOk =
        indirectDispatchAuthoring.Contains("DetectPromotedIndirectDispatchUnits(monsterBin)", StringComparison.Ordinal)
        || indirectDispatchAuthoring.Contains("AiAutomation.DetectIndirectDispatchUnits(", StringComparison.Ordinal);
    if (!genericLookupOk)
        failures.Add("indirect dispatch editor still lacks a generic live lookup path for non-Seymour authoring candidates.");

    bool previewFallbackOk =
        advancedFamilyDialogs.Contains("GenericUnitPreviewPrefix", StringComparison.Ordinal)
        && advancedFamilyDialogs.Contains("TryBuildAdvancedGenericUnitDialogSnapshot", StringComparison.Ordinal)
        && advancedFamilyDialogs.Contains("unit.CapabilityTier != AiIndirectDispatchCapabilityTier.AuthoringCandidate", StringComparison.Ordinal);
    if (!previewFallbackOk)
        failures.Add("preview-only units no longer have a generic fallback surface for dead quick-card clicks.");

    bool flareVisible = AiCommandId.AllOptions().Any(option => option.Name.Equals("Flare", StringComparison.OrdinalIgnoreCase));
    if (!flareVisible)
        failures.Add("Flare is missing from AiCommandId.AllOptions().");

    bool pass = failures.Count == 0;
    Console.WriteLine("=== AiScriptLab --indirect-dispatch-intent-editors-rt0 ===");
    Console.WriteLine($"payload window         : {(payloadWindowOk ? "OK" : "FAIL")}");
    Console.WriteLine($"target window          : {(targetWindowOk ? "OK" : "FAIL")}");
    Console.WriteLine($"next-state window      : {(nextStateWindowOk ? "OK" : "FAIL")}");
    Console.WriteLine($"payload route          : {(payloadRouteOk ? "OK" : "FAIL")}");
    Console.WriteLine($"target route           : {(targetRouteOk ? "OK" : "FAIL")}");
    Console.WriteLine($"next-state route       : {(nextStateRouteOk ? "OK" : "FAIL")}");
    Console.WriteLine($"flux popup             : {(fluxWindowOk ? "OK" : "FAIL")}");
    Console.WriteLine($"flux card route        : {(fluxRouteOk ? "OK" : "FAIL")}");
    Console.WriteLine($"flux code-behind       : {(fluxCodeBehindOk ? "OK" : "FAIL")}");
    Console.WriteLine($"m213 popup             : {(companionWindowOk ? "OK" : "FAIL")}");
    Console.WriteLine($"m213 popup route       : {(companionRouteOk ? "OK" : "FAIL")}");
    Console.WriteLine($"m213 code-behind       : {(companionCodeBehindOk ? "OK" : "FAIL")}");
    Console.WriteLine($"fallback full editor   : {(fallbackRouteOk ? "OK" : "FAIL")}");
    Console.WriteLine($"quick-edit honest copy : {(quickEditHonestCopyOk ? "OK" : "FAIL")}");
    Console.WriteLine($"quick-card bindings    : {(quickCardBindingOk ? "OK" : "FAIL")}");
    Console.WriteLine($"quick-card model       : {(quickCardTargetModelOk ? "OK" : "FAIL")}");
    Console.WriteLine($"focus launch gate      : {(focusLaunchGateOk ? "OK" : "FAIL")}");
    Console.WriteLine($"generic live lookup    : {(genericLookupOk ? "OK" : "FAIL")}");
    Console.WriteLine($"preview fallback       : {(previewFallbackOk ? "OK" : "FAIL")}");
    Console.WriteLine($"payload flare visible  : {(flareVisible ? "OK" : "FAIL")}");
    Console.WriteLine(pass ? "VERDICT: PASS" : "VERDICT: FAIL");
    foreach (string failure in failures.Take(20))
        Console.WriteLine($"  FAIL: {failure}");

    if (!string.IsNullOrWhiteSpace(jsonOut))
    {
        string? dir = Path.GetDirectoryName(jsonOut);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(jsonOut, JsonSerializer.Serialize(new
        {
            generatedAtUtc = DateTime.UtcNow.ToString("O"),
            root,
            pass,
            payloadWindowOk,
            targetWindowOk,
            nextStateWindowOk,
            payloadRouteOk,
            targetRouteOk,
            nextStateRouteOk,
            fluxWindowOk,
            fluxRouteOk,
            fluxCodeBehindOk,
            fallbackRouteOk,
            quickEditHonestCopyOk,
            quickCardBindingOk,
            quickCardTargetModelOk,
            focusLaunchGateOk,
            genericLookupOk,
            previewFallbackOk,
            flareVisible,
            failures = failures.ToArray(),
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"json={jsonOut}");
    }

    return pass ? 0 : 1;

    static string FindRepoRoot()
    {
        string current = Directory.GetCurrentDirectory();
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (Directory.Exists(Path.Combine(current, "FFXProjectEditor")) &&
                Directory.Exists(Path.Combine(current, "RuntimeTools")))
                return current;

            string? parent = Directory.GetParent(current)?.FullName;
            if (string.IsNullOrWhiteSpace(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                break;
            current = parent;
        }

        return Directory.GetCurrentDirectory();
    }
}

static int PhaseRouteCardsRt0(IReadOnlyList<string> files, string root, string? jsonOut)
{
    int scripted = 0, candidates = 0, totalCards = 0, cardBuildOk = 0;
    var failures = new List<string>();
    var samples = new List<object>();
    bool sourceIntegrityOk = true;

    string repoRoot = FindRepoRoot();
    string routeWindowPath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiPhaseRotationAdvanced_Window.axaml");
    string phaseRotationPath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiEditor_DataModel.PhaseRotation.cs");
    string routeCardsPath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiEditor_DataModel.PhaseRouteCards.cs");
    string advancedPhasePath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiEditor_DataModel.PhaseRotationAdvanced.cs");
    string advancedWindowCodeBehindPath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiPhaseRotationAdvanced_Window.axaml.cs");
    string indirectEditorVmPath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiEditor_DataModel.IndirectDispatchAuthoring.cs");
    string indirectEditorWindowPath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiIndirectDispatchUnit_Window.axaml");

    try
    {
        string routeWindow = File.ReadAllText(routeWindowPath);
        if (!routeWindow.Contains("DataTemplate x:Key=\"RouteCardTemplate\"", StringComparison.Ordinal) ||
            !routeWindow.Contains("DataTemplate x:Key=\"AdvancedDetailRowTemplate\"", StringComparison.Ordinal) ||
            !routeWindow.Contains("<Expander", StringComparison.Ordinal) ||
            !routeWindow.Contains("IsExpanded=\"{Binding IsExpanded, Mode=TwoWay}\"", StringComparison.Ordinal) ||
            !routeWindow.Contains("ItemsSource=\"{Binding PhaseConditionPreviewRouteCards}\"", StringComparison.Ordinal) ||
            !routeWindow.Contains("ItemsSource=\"{Binding PhaseVariableEvidenceRouteCards}\"", StringComparison.Ordinal) ||
            !routeWindow.Contains("ItemsSource=\"{Binding AdvancedPhaseUnits}\"", StringComparison.Ordinal) ||
            !routeWindow.Contains("SelectedItem=\"{Binding SelectedAdvancedPhaseUnit, Mode=TwoWay}\"", StringComparison.Ordinal) ||
            !routeWindow.Contains("ItemsSource=\"{Binding SelectedAdvancedPhaseUnit.CommandSlots}\"", StringComparison.Ordinal) ||
            !routeWindow.Contains("ItemsSource=\"{Binding SelectedAdvancedPhaseUnit.TargetSlots}\"", StringComparison.Ordinal) ||
            !routeWindow.Contains("ItemsSource=\"{Binding SelectedAdvancedPhaseUnit.ConsumerRows}\"", StringComparison.Ordinal) ||
            !routeWindow.Contains("Tag=\"{Binding EditorLaunchContext}\"", StringComparison.Ordinal))
        {
            sourceIntegrityOk = false;
            failures.Add("Advanced phase manager bindings are incomplete in MonsterAiPhaseRotationAdvanced_Window.axaml.");
        }
    }
    catch (Exception ex)
    {
        sourceIntegrityOk = false;
        failures.Add($"route window source check failed: {ex.Message}");
    }

    try
    {
        string phaseRotation = File.ReadAllText(phaseRotationPath);
        string routeCards = File.Exists(routeCardsPath) ? File.ReadAllText(routeCardsPath) : string.Empty;
        string advancedPhase = File.ReadAllText(advancedPhasePath);
        string advancedWindowCodeBehind = File.ReadAllText(advancedWindowCodeBehindPath);
        string indirectEditorVm = File.ReadAllText(indirectEditorVmPath);
        string indirectEditorWindow = File.ReadAllText(indirectEditorWindowPath);

        if (!phaseRotation.Contains("PhaseVariableEvidenceBlocks.Clear();", StringComparison.Ordinal) ||
            !phaseRotation.Contains("PhaseVariableEvidenceRouteCards.Clear();", StringComparison.Ordinal))
        {
            sourceIntegrityOk = false;
            failures.Add("phase variable evidence does not clear route-card state before rebuild.");
        }

        if (!routeCards.Contains("public ObservableCollection<AiPhaseRouteCardVm> PhaseDraftRouteCards { get; } = new();", StringComparison.Ordinal) ||
            !routeCards.Contains("public ObservableCollection<AiPhaseRouteCardVm> PhaseConditionPreviewRouteCards { get; } = new();", StringComparison.Ordinal) ||
            !routeCards.Contains("public ObservableCollection<AiPhaseRouteCardVm> PhaseVariableEvidenceRouteCards { get; } = new();", StringComparison.Ordinal) ||
            !routeCards.Contains("internal sealed partial class AiPhaseRouteCardVm", StringComparison.Ordinal) ||
            !routeCards.Contains("void RebuildPhaseDraftRouteCards()", StringComparison.Ordinal) ||
            !routeCards.Contains("void AppendIndirectDraftRouteCards()", StringComparison.Ordinal) ||
            !routeCards.Contains("draft-indirect:", StringComparison.Ordinal) ||
            (!routeCards.Contains("DetectIndirectDispatchUnits(monsterBin, selectedScript)", StringComparison.Ordinal)
                && !routeCards.Contains("DetectPromotedIndirectDispatchUnits(monsterBin)", StringComparison.Ordinal)) ||
            !routeCards.Contains("void RebuildConditionPreviewRouteCards()", StringComparison.Ordinal) ||
            !routeCards.Contains("void RebuildVarEvidenceRouteCards(", StringComparison.Ordinal))
        {
            sourceIntegrityOk = false;
            failures.Add("route-card projection helpers were not found in the dedicated partial file.");
        }

        if (!advancedPhase.Contains("AiIndirectDispatchEditorLaunchContext", StringComparison.Ordinal) ||
            !advancedPhase.Contains("new AiIndirectDispatchEditorLaunchContext(", StringComparison.Ordinal) ||
            !advancedPhase.Contains("EditorLaunchContext =>", StringComparison.Ordinal))
        {
            sourceIntegrityOk = false;
            failures.Add("advanced phase variable-focus rows are not projecting a dedicated editor launch context.");
        }

        if (!advancedWindowCodeBehind.Contains("AiIndirectDispatchEditorLaunchContext", StringComparison.Ordinal) ||
            !advancedWindowCodeBehind.Contains("OpenIndirectDispatchEditor(focusContext)", StringComparison.Ordinal))
        {
            sourceIntegrityOk = false;
            failures.Add("advanced phase window code-behind is not routing focus-context launches into the row-only popup.");
        }

        if (!indirectEditorVm.Contains("FocusedContextSummary", StringComparison.Ordinal) ||
            !indirectEditorVm.Contains("HasFocusedContext", StringComparison.Ordinal) ||
            !indirectEditorVm.Contains("ApplyFocusRequest(", StringComparison.Ordinal) ||
            !indirectEditorVm.Contains("IsNextStateFocused", StringComparison.Ordinal) ||
            !indirectEditorVm.Contains("IsFocused", StringComparison.Ordinal))
        {
            sourceIntegrityOk = false;
            failures.Add("row-only indirect editor VM is missing focused-slot context state.");
        }

        if (!indirectEditorWindow.Contains("FocusedContextSummary", StringComparison.Ordinal) ||
            !indirectEditorWindow.Contains("HasFocusedContext", StringComparison.Ordinal) ||
            !indirectEditorWindow.Contains("IsFocused", StringComparison.Ordinal) ||
            !indirectEditorWindow.Contains("IsNextStateFocused", StringComparison.Ordinal))
        {
            sourceIntegrityOk = false;
            failures.Add("row-only indirect editor window is not surfacing the focused slot context in the UI.");
        }
    }
    catch (Exception ex)
    {
        sourceIntegrityOk = false;
        failures.Add($"phase route-card source check failed: {ex.Message}");
    }

    foreach (string path in files)
    {
        string id = Path.GetFileNameWithoutExtension(path);
        byte[] monster;
        byte[]? aiFile;
        try
        {
            monster = File.ReadAllBytes(path);
            aiFile = AiScript_File.SliceAiFileFromMonster(monster);
        }
        catch (Exception ex)
        {
            failures.Add($"{id}: read/slice failed: {ex.Message}");
            continue;
        }
        if (aiFile == null) continue;

        AiScriptFile script;
        try { script = AiScript_File.Read(aiFile); }
        catch (Exception ex)
        {
            failures.Add($"{id}: AiFile parse failed: {ex.Message}");
            continue;
        }
        if (!script.HasScript) continue;
        scripted++;
        if (script.Variables.Count == 0) continue;
        candidates++;

        IReadOnlyList<AiDetectedAction> actions;
        try { actions = AiAutomation.DetectActions(script); }
        catch (Exception ex)
        {
            failures.Add($"{id}: DetectActions threw: {ex.Message}");
            continue;
        }

        int monsterCards = 0;

        // Build route-card-like data from detected actions (draft-style)
        foreach (AiDetectedAction a in actions)
        {
            monsterCards++;
            string title = string.IsNullOrWhiteSpace(a.AbilityName) ? a.Kind.ToString() : a.AbilityName;
            string targetInfo = AiTargetNames.Get(a.TargetOperand) ?? $"0x{a.TargetOperand:X4}";

            if (string.IsNullOrWhiteSpace(title))
                failures.Add($"{id}: action #{monsterCards} has empty title");
        }

        // Build condition-preview-style data from the script's instructions
        int condClauses = 0;
        int condRangeOps = 0;
        for (int i = 0; i + 3 < script.Instructions.Count; i++)
        {
            if (script.Instructions[i].Opcode == 0xAE &&
                script.Instructions[i + 3].OperandKind == AiOperandKind.FuncId &&
                script.Instructions[i + 3].Operand is 0x700D or 0x700E)
            {
                condClauses++;
                ushort op = script.Instructions[i + 2].Operand;
                if (op == 0x0B || op == 0x0C || op == 0x0D || op == 0x0E) condRangeOps++;
            }
        }

        // Build var-evidence-style data from the script's variable table
        int varClusters = 0;
        foreach (AiVariable var in script.Variables)
        {
            // Check if var is referenced in instructions
            bool referenced = script.Instructions.Any(i => i.Operand == var.Index);
            if (referenced) varClusters++;
        }

        totalCards += monsterCards;

        if (samples.Count < 16 && monsterCards > 0)
        {
            samples.Add(new
            {
                id,
                relativePath = SafeRelLocal(path, root),
                detectedActions = monsterCards,
                conditionClauses = condClauses,
                varClusters,
                variableCount = script.Variables.Count,
            });
        }
    }

    cardBuildOk = totalCards;
    bool pass = candidates > 0 && cardBuildOk > 0 && sourceIntegrityOk;
    Console.WriteLine("=== AiScriptLab --phase-route-cards-rt0 (LAB: route-card rollout smoke) ===");
    Console.WriteLine($"scripted monsters       : {scripted}");
    Console.WriteLine($"candidates >=1 var      : {candidates}");
    Console.WriteLine($"route cards built       : {cardBuildOk}");
    Console.WriteLine($"source integrity        : {(sourceIntegrityOk ? "OK" : "FAIL")}");
    Console.WriteLine($"failures                : {failures.Count}");
    Console.WriteLine(pass ? "VERDICT: PASS" : "VERDICT: FAIL");
    foreach (object sample in samples.Take(8))
        Console.WriteLine("sample: " + JsonSerializer.Serialize(sample));
    foreach (string f in failures.Take(20))
        Console.WriteLine($"  FAIL: {f}");

    if (!string.IsNullOrWhiteSpace(jsonOut))
    {
        string? dir = Path.GetDirectoryName(jsonOut);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(jsonOut, JsonSerializer.Serialize(new
        {
            generatedAtUtc = DateTime.UtcNow.ToString("O"),
            root,
            scripted,
            candidates,
            totalCards,
            cardBuildOk,
            pass,
            samples,
            failures = failures.Take(100).ToArray(),
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"json={jsonOut}");
    }

    return pass ? 0 : 1;

    static string FindRepoRoot()
    {
        string current = Directory.GetCurrentDirectory();
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (Directory.Exists(Path.Combine(current, "FFXProjectEditor")) &&
                Directory.Exists(Path.Combine(current, "RuntimeTools")))
                return current;

            string? parent = Directory.GetParent(current)?.FullName;
            if (string.IsNullOrWhiteSpace(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                break;
            current = parent;
        }

        return Directory.GetCurrentDirectory();
    }

    static string SafeRelLocal(string path, string root)
    {
        try { return Path.GetRelativePath(root, path); }
        catch { return path; }
    }
}

long instrTotal = 0, editTotalOperands = 0, totalWorkers = 0, totalEntrypoints = 0, totalJumps = 0;
var distinctOpcodes = new SortedSet<byte>();
var allUnknown = new SortedSet<byte>();
var fails = new List<string>();

foreach (string path in files)
{
    byte[] monster = File.ReadAllBytes(path);
    byte[]? aiFile = AiScript_File.SliceAiFileFromMonster(monster);
    if (aiFile is null) { continue; }
    total++;
    string id = Path.GetFileNameWithoutExtension(path);

    AiScriptFile script;
    try { script = AiScript_File.Read(aiFile); }
    catch (Exception ex) { fails.Add($"{id}: Read threw {ex.GetType().Name}: {ex.Message}"); continue; }

    if (!script.HasScript) stub++;
    else
    {
        withScript++;
        instrTotal += script.Instructions.Count;
        foreach (var ins in script.Instructions) distinctOpcodes.Add(ins.Opcode);
    }

    // Strong correctness gate (RT0 alone is too weak — it re-emits whatever it read):
    //   (1) the bounded code walk must close EXACTLY on codeLength@0x00, and
    //   (2) every opcode in the code region must be one of the proven 48.
    if (script.CodeWalkClosedExactly) codeClean++;
    else fails.Add($"{id}: code walk did NOT close on codeLen (scriptStart=0x{script.ScriptStart:X} codeLen=0x{script.CodeLength:X})");

    if (script.UnknownOpcodes.Count == 0) noUnknown++;
    else { foreach (var u in script.UnknownOpcodes) allUnknown.Add(u); fails.Add($"{id}: unknown opcodes [{string.Join(" ", script.UnknownOpcodes.Select(b => b.ToString("X2")))}]"); }

    byte[] re = AiScript_File.Write(script);
    if (re.AsSpan().SequenceEqual(aiFile)) rt0++;
    else fails.Add($"{id}: RT0 drift (orig={aiFile.Length} re={re.Length})");

    // EDIT-PATH gate: mutate EVERY operand (XOR a constant) and prove the writer is byte-local —
    // the only bytes that change are the operand bytes (offset+1,+2) of operand-bearing instructions,
    // re-emit keeps length, and a re-read yields exactly the mutated operands. This is the analogue of
    // Shop's AssertSlotOnlyDiff: it proves SAFE, localized AI edits, not just trivial round-trip.
    if (script.HasScript)
    {
        const ushort Flip = 0x1234;
        AiScriptFile m = AiScript_File.Read(aiFile);
        var expected = new HashSet<int>();
        var wantOperand = new List<(int idx, ushort op)>();
        for (int k = 0; k < m.Instructions.Count; k++)
        {
            var ins = m.Instructions[k];
            if (!ins.HasOperand) continue;
            expected.Add(ins.Offset + 1);
            expected.Add(ins.Offset + 2);
            ins.Operand = (ushort)(ins.Operand ^ Flip);
            wantOperand.Add((k, ins.Operand));
            editTotalOperands++;
        }
        byte[] mutated = AiScript_File.Write(m);
        bool local = mutated.Length == aiFile.Length;
        if (local)
            for (int p = 0; p < mutated.Length; p++)
                if (mutated[p] != aiFile[p] && !expected.Contains(p)) { local = false; break; }
        if (local) editLocalized++;
        else fails.Add($"{id}: operand edit changed a non-operand byte");

        AiScriptFile back = AiScript_File.Read(mutated);
        bool reread = back.Instructions.Count == m.Instructions.Count
                      && wantOperand.All(w => back.Instructions[w.idx].Operand == w.op);
        if (reread) editReread++;
        else fails.Add($"{id}: re-read after edit did not yield mutated operands");

        // WRITE-BACK: splice the edited AiFile into the FULL monster_*.bin; only the AiFile region may
        // change (all other monster sections preserved). This is the real on-disk edit path.
        int aiPtr = (int)(monster[4] | (monster[5] << 8) | (monster[6] << 16) | (monster[7] << 24));
        byte[] newMonster = AiScript_File.SpliceAiFileIntoMonster(monster, mutated);
        bool wbLocal = newMonster.Length == monster.Length;
        if (wbLocal)
            for (int p = 0; p < newMonster.Length; p++)
                if (newMonster[p] != monster[p] && (p < aiPtr || p >= aiPtr + mutated.Length)) { wbLocal = false; break; }
        // and the spliced region equals the edited AiFile
        if (wbLocal && AiScript_File.SliceAiFileFromMonster(newMonster) is byte[] reAi)
            wbLocal = reAi.AsSpan().SequenceEqual(mutated);
        if (wbLocal) writeBackOk++;
        else fails.Add($"{id}: write-back touched bytes outside the AiFile region");

        // CONTROL-FLOW / VALUE edit gate: editing a float-const or a jump-target changes ONLY the 4
        // pool/table bytes (the instruction operand is untouched). Proves safe control-flow editing.
        AiScriptFile s2 = AiScript_File.Read(aiFile);
        var fc = s2.Instructions.FirstOrDefault(x => x.OperandKind == AiOperandKind.FloatConst);
        if (fc != null && s2.FloatPoolOffset >= 0 && s2.FloatPoolOffset + 4 * fc.Operand + 4 <= aiFile.Length)
        {
            cfFloatTested++;
            byte[] e = AiScript_File.EditFloatConst(s2, fc.Operand, 1234.5f);
            int at = s2.FloatPoolOffset + 4 * fc.Operand;
            if (DiffOnlyAt(aiFile, e, at, 4) && BitConverter.ToSingle(e, at) == 1234.5f) cfFloatOk++;
            else fails.Add($"{id}: float-const edit not byte-local");
        }
        var wj = s2.Workers.FirstOrDefault(x => x.JumpTargets.Count > 0 && x.Entrypoints.Count > 0);
        if (wj != null)
        {
            cfJumpTested++;
            int wi = s2.Workers.ToList().IndexOf(wj);
            int jumpTab = (int)((uint)aiFile[wj.DescriptorOffset + 0x24] | (uint)aiFile[wj.DescriptorOffset + 0x25] << 8
                              | (uint)aiFile[wj.DescriptorOffset + 0x26] << 16 | (uint)aiFile[wj.DescriptorOffset + 0x27] << 24);
            byte[] e = AiScript_File.EditJumpTarget(s2, wi, 0, wj.Entrypoints[0]);
            if (DiffOnlyAt(aiFile, e, jumpTab, 4)) cfJumpOk++;
            else fails.Add($"{id}: jump-target edit not byte-local");
        }
    }

    // WORKER gate: the parsed worker table must match the count field, and every entrypoint/jump
    // target must be a valid code-relative offset (< CodeLength). This validates the entrypoint +
    // jump-table layout (the control-flow surface the editor needs).
    if (script.HasScript)
    {
        int wantWorkers = aiFile.Length >= 0x38 ? (aiFile[0x36] | (aiFile[0x37] << 8)) : 0;
        bool wok = script.Workers.Count == wantWorkers;
        foreach (var w in script.Workers)
        {
            totalWorkers++;
            totalEntrypoints += w.Entrypoints.Count;
            totalJumps += w.JumpTargets.Count;
            foreach (int t in w.Entrypoints) if (t < 0 || t > script.CodeLength) wok = false;
            foreach (int t in w.JumpTargets) if (t < 0 || t > script.CodeLength) wok = false;
        }
        if (wok) workersOk++;
        else fails.Add($"{id}: worker parse mismatch (parsed {script.Workers.Count}, want {wantWorkers}, or a target >= codeLen)");
    }

    if (dumpId != null && id.Equals(dumpId, StringComparison.OrdinalIgnoreCase))
        Console.WriteLine(AiScript_File.Disassemble(script));
}

bool pass = total > 0 && rt0 == total && codeClean == total && noUnknown == total
            && editLocalized == withScript && editReread == withScript
            && workersOk == withScript && writeBackOk == withScript
            && cfFloatOk == cfFloatTested && cfJumpOk == cfJumpTested && fails.Count == 0;

var sb = new StringBuilder();
sb.AppendLine("=== AiScriptLab gate (RT0 + bounded-walk + known-opcode + edit-path) ===");
sb.AppendLine($"monster root        : {root}");
sb.AppendLine($"monsters w/ AiFile  : {total}");
sb.AppendLine($"  with script       : {withScript}");
sb.AppendLine($"  stub/no-script    : {stub}");
sb.AppendLine($"RT0 byte-identical  : {rt0}/{total}");
sb.AppendLine($"code walk exact-end : {codeClean}/{total}");
sb.AppendLine($"no unknown opcodes  : {noUnknown}/{total}");
sb.AppendLine($"edit byte-local     : {editLocalized}/{withScript}  ({editTotalOperands:N0} operands mutated)");
sb.AppendLine($"edit re-read ok     : {editReread}/{withScript}");
sb.AppendLine($"workers parsed ok   : {workersOk}/{withScript}  ({totalWorkers:N0} workers, {totalEntrypoints:N0} entrypoints, {totalJumps:N0} jumps)");
sb.AppendLine($"write-back byte-local: {writeBackOk}/{withScript}  (edit -> splice into monster_*.bin -> only AiFile region changes)");
sb.AppendLine($"float-const edit local: {cfFloatOk}/{cfFloatTested}   jump-target edit local: {cfJumpOk}/{cfJumpTested}  (control-flow/value editing)");
sb.AppendLine($"total instructions  : {instrTotal:N0}");
sb.AppendLine($"distinct opcodes    : {distinctOpcodes.Count}  [{string.Join(" ", distinctOpcodes.Select(b => b.ToString("X2")))}]");
if (allUnknown.Count > 0)
    sb.AppendLine($"UNKNOWN opcodes seen: [{string.Join(" ", allUnknown.Select(b => b.ToString("X2")))}]");
sb.AppendLine($"VERDICT             : {(pass ? "PASS" : "FAIL")}");
if (fails.Count > 0)
{
    sb.AppendLine("FAILS:");
    foreach (var f in fails.Take(20)) sb.AppendLine("  " + f);
}
Console.WriteLine(sb.ToString());

if (jsonOut != null)
{
    var verdict = new
    {
        pass,
        root,
        monstersWithAiFile = total,
        withScript,
        stub,
        rt0,
        codeWalkExactEnd = codeClean,
        noUnknownOpcodes = noUnknown,
        editByteLocal = editLocalized,
        editReread,
        editTotalOperands,
        workersOk,
        totalWorkers,
        totalEntrypoints,
        totalJumps,
        totalInstructions = instrTotal,
        distinctOpcodes = distinctOpcodes.Select(b => $"0x{b:X2}").ToArray(),
        unknownOpcodes = allUnknown.Select(b => $"0x{b:X2}").ToArray(),
        fails = fails.Take(50).ToArray(),
    };
    File.WriteAllText(jsonOut, JsonSerializer.Serialize(verdict, new JsonSerializerOptions { WriteIndented = true }));
}

return pass ? 0 : 1;

// ============================================================================================================
// AI ASSEMBLER PRODUCTIZATION GATE (--ai2). Proves, over the real corpus, the new editor capabilities:
//   * AiCommandId encode/decode round-trips for every command dictionary entry (+ the RT2-proven Firaga/Thundaga);
//   * AiValidator NEVER blocks a clean unedited file (361 IsValid) and DOES catch planted errors WITHOUT throwing;
//   * linear template insertion is Rebuild-safe (no throw, re-reads clean, exact byte delta);
//   * GrowWorkerJumpTable adds slots safely (no-op == RT0; grow re-reads clean; other workers' tables intact);
//   * AppendGuardedAction (conditional snippet) re-parses clean, repoints the entrypoint, validates as a grow.
// ============================================================================================================
static int Ai2Gate(List<string> files, string? jsonOut)
{
    // Findings currently expose localized text rather than diagnostic IDs.
    // Match the complete resource template, not Portuguese substrings or any Error.
    static bool HasDiagnostic(AiValidationReport report, string template)
    {
        string pattern = Regex.Escape(template);
        foreach (Match placeholder in Regex.Matches(template, @"\{\d+(?::[^}]*)?\}"))
            pattern = pattern.Replace(Regex.Escape(placeholder.Value), ".+?", StringComparison.Ordinal);
        return report.Errors.Any(f => Regex.IsMatch(f.Message, @"\A" + pattern + @"\z", RegexOptions.Singleline));
    }

    int total = 0, withScript = 0;
    int validateClean = 0, rt0InfoPresent = 0;
    int linInsertOk = 0, guardOk = 0, guardTested = 0, growNoopRt0 = 0;
    int condOk = 0, condTested = 0;   // #11 conditional snippet (readChrProperty < N -> force command)
    int hpCondOk = 0, hpCondTested = 0;   // supported NearDeath/HP<50% snippet
    int growWorkersTested = 0, growWorkersOk = 0;
    int sentinelTested = 0, sentinelOk = 0;
    bool synthSentinelDone = false; int synthSentinel = 0;   // synthetic one-past-end entrypoint (corpus has none)
    int synthStack = 0;   // synthetic stack-break: prove the Level-1 stack-delta checker catches a removed arg-push
    int posDangle = 0, posOpcode = 0, posJumpOob = 0, posTested = 0;
    static uint U32(byte[] b, int o) => (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
    static void W32(byte[] b, int o, uint v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24); }
    var fails = new List<string>();

    // ---- AiCommandId encode/decode round-trip (once) ----
    int cmdRtChar = 0, cmdRtMon = 0, cmdRtMon2 = 0;
    foreach (var kv in FFXProjectEditor.FfxLib.Dictionaries.CommandCharacter_Dictionary.Instance)
    {
        ushort op = AiCommandId.EncodeChar(kv.Key);
        var d = AiCommandId.Decode(op);
        if (op == (0x3000 | (kv.Key & 0x0FFF)) && d.CommandId == kv.Key && d.Category == AiCommandCategory.Character
            && d.IsKnown && AiCommandId.IsCommandOperand(op)) cmdRtChar++;
        else fails.Add($"AiCommandId char round-trip failed for id {kv.Key}");
    }
    foreach (var kv in FFXProjectEditor.FfxLib.Dictionaries.CommandMonster1_Dictionary.Instance)
    {
        ushort op = AiCommandId.EncodeMonster(kv.Key);
        var d = AiCommandId.Decode(op);
        if (op == (0x4000 | (kv.Key & 0x0FFF)) && d.CommandId == kv.Key && d.Category == AiCommandCategory.Monster
            && d.IsKnown && AiCommandId.IsCommandOperand(op)) cmdRtMon++;
        else fails.Add($"AiCommandId monster round-trip failed for id {kv.Key}");
    }
    foreach (var kv in FFXProjectEditor.FfxLib.Dictionaries.CommandMonster2_Dictionary.Instance)
    {
        ushort op = AiCommandId.EncodeMonster2(kv.Key);
        var d = AiCommandId.Decode(op);
        if (op == (0x6000 | (kv.Key & 0x0FFF)) && d.CommandId == kv.Key && d.Category == AiCommandCategory.Monster2
            && d.IsKnown && AiCommandId.IsCommandOperand(op)) cmdRtMon2++;
        else fails.Add($"AiCommandId monster2 round-trip failed for id {kv.Key}");
    }
    // RT2-proven operands (Flame Flan): 0x3049 Firaga, 0x304B Thundaga. Structural id check (label-agnostic).
    bool firaga = AiCommandId.Decode(0x3049) is { Category: AiCommandCategory.Character, CommandId: 0x49, IsKnown: true };
    bool thundaga = AiCommandId.Decode(0x304B) is { Category: AiCommandCategory.Character, CommandId: 0x4B, IsKnown: true };
    // MonMagic2 (corpus high-nibble 6, 291 sites): 0x60AB = CommandMonster2[171] = "Multi-Fira" — was unreachable before.
    bool multiFira = AiCommandId.Decode(0x60AB) is { Category: AiCommandCategory.Monster2, CommandId: 0xAB, IsKnown: true }
                     && AiCommandId.IsCommandOperand(0x60AB);
    bool notCmd = !AiCommandId.IsCommandOperand(0x700B) && !AiCommandId.IsCommandOperand(0x0001);
    if (!firaga) fails.Add("AiCommandId: 0x3049 did not decode as char/0x49");
    if (!thundaga) fails.Add("AiCommandId: 0x304B did not decode as char/0x4B");
    if (!multiFira) fails.Add("AiCommandId: 0x60AB did not decode as monster2/0xAB (Multi-Fira)");
    if (!notCmd) fails.Add("AiCommandId: IsCommandOperand false-positive on 0x700B/0x0001");

    var rng = AiSnippetLibrary.ById("guard-rng-force-cmd")!;
    var lin = AiSnippetLibrary.ById("force-cmd-self")!;
    var cond = AiSnippetLibrary.ById("guard-chrprop-lt-force-cmd")!;   // #11 conditional snippet
    var hpCond = AiSnippetLibrary.ById("guard-hp-below-pct-force-cmd")!;   // #11 HP%-enrage snippet

    foreach (string path in files)
    {
        byte[] monster = File.ReadAllBytes(path);
        byte[]? aiFile = AiScript_File.SliceAiFileFromMonster(monster);
        if (aiFile is null) continue;
        total++;
        string id = Path.GetFileNameWithoutExtension(path);
        AiScriptFile script;
        try { script = AiScript_File.Read(aiFile); }
        catch (Exception ex) { fails.Add($"{id}: Read threw {ex.GetType().Name}"); continue; }
        if (!script.HasScript) continue;
        withScript++;

        // 1) validator never blocks a clean unedited file + RT0 info present.
        try
        {
            var rep = AiValidator.Validate(script);
            if (rep.IsValid && rep.ErrorCount == 0) validateClean++;
            else fails.Add($"{id}: clean file flagged invalid ({rep.ErrorCount} errors): {rep.Errors.FirstOrDefault()?.Message}");
            if (rep.Infos.Any(f => f.Message.Contains("RT0"))) rt0InfoPresent++;
            else fails.Add($"{id}: no RT0 info in clean report");
        }
        catch (Exception ex) { fails.Add($"{id}: Validate(clean) threw {ex.GetType().Name}: {ex.Message}"); }

        // 2) linear template insertion is Rebuild-safe.
        try
        {
            var listIns = script.Instructions.ToList();
            var snip = lin.ExpandLinear(new AiSnippetArgs(AiCommandId.EncodeMonster(0), 0, 0));
            int at = listIns.Count / 2;
            listIns.InsertRange(at, snip);
            byte[] rebuilt = AiScript_File.Rebuild(script, listIns);
            var rr = AiScript_File.Read(rebuilt);
            int expectDelta = snip.Sum(i => i.Length);
            if (rr.CodeWalkClosedExactly && rr.UnknownOpcodes.Count == 0
                && rebuilt.Length == aiFile.Length + expectDelta && rr.Workers.Count == script.Workers.Count)
                linInsertOk++;
            else fails.Add($"{id}: linear template insert not clean (delta {rebuilt.Length - aiFile.Length} want {expectDelta})");
        }
        catch (Exception ex) { fails.Add($"{id}: linear template insert threw {ex.GetType().Name}: {ex.Message}"); }

        // worker with entrypoints (a real handler) for the grow / guarded-action checks.
        int wi = -1;
        for (int k = 0; k < script.Workers.Count; k++) if (script.Workers[k].Entrypoints.Count > 0) { wi = k; break; }

        // 3) GrowWorkerJumpTable: no-op == RT0 (once); then grow EVERY worker by 2 (exercises first/middle/last,
        //    0-jump and many-jump workers) and assert re-reads clean + the OTHER workers' tables stay intact.
        if (wi >= 0)
        {
            try
            {
                byte[] noop = AiScript_File.GrowWorkerJumpTable(script, wi, Array.Empty<int>());
                if (noop.AsSpan().SequenceEqual(aiFile)) growNoopRt0++;
                else fails.Add($"{id}: GrowWorkerJumpTable no-op changed bytes");
            }
            catch (Exception ex) { fails.Add($"{id}: grow no-op threw {ex.GetType().Name}: {ex.Message}"); }

            for (int gw = 0; gw < script.Workers.Count; gw++)
            {
                growWorkersTested++;
                try
                {
                    int oldJumps = script.Workers[gw].JumpTargets.Count;
                    int anchor = script.Workers[gw].Entrypoints.Count > 0 ? script.Workers[gw].Entrypoints[0] : 0;
                    int[] targets = { 0, anchor };
                    byte[] grown = AiScript_File.GrowWorkerJumpTable(script, gw, targets);
                    var g2 = AiScript_File.Read(grown);
                    bool walkOk = g2.CodeWalkClosedExactly && g2.UnknownOpcodes.Count == 0;
                    bool gRt0 = AiScript_File.Write(g2).AsSpan().SequenceEqual(grown);
                    bool countOk = g2.Workers.Count == script.Workers.Count
                                   && g2.Workers[gw].JumpTargets.Count == oldJumps + 2;
                    bool slotsOk = countOk && g2.Workers[gw].JumpTargets[oldJumps] == targets[0]
                                   && g2.Workers[gw].JumpTargets[oldJumps + 1] == targets[1];
                    bool targetsValid = g2.Workers.All(w =>
                        w.Entrypoints.All(t => t >= 0 && t <= g2.CodeLength) && w.JumpTargets.All(t => t >= 0 && t <= g2.CodeLength));
                    bool intact = true;
                    for (int k = 0; k < script.Workers.Count; k++)
                    {
                        if (k == gw) continue;
                        if (!g2.Workers[k].JumpTargets.SequenceEqual(script.Workers[k].JumpTargets)
                            || !g2.Workers[k].Entrypoints.SequenceEqual(script.Workers[k].Entrypoints)) { intact = false; break; }
                    }
                    if (walkOk && gRt0 && countOk && slotsOk && targetsValid && intact) growWorkersOk++;
                    else fails.Add($"{id}: grow w{gw} not clean (walk={walkOk} rt0={gRt0} count={countOk} slots={slotsOk} valid={targetsValid} intact={intact})");
                }
                catch (Exception ex) { fails.Add($"{id}: grow w{gw} threw {ex.GetType().Name}: {ex.Message}"); }
            }

            // 4) AppendGuardedAction (conditional snippet) on the FIRST and LAST entrypoint of worker wi:
            //    re-parses clean, repoints that entrypoint, grows the jump-table, validates as a grow.
            int epCount = script.Workers[wi].Entrypoints.Count;
            foreach (int ei in new[] { 0, epCount - 1 }.Distinct())
            {
                guardTested++;
                try
                {
                    int oldCode = script.CodeLength;
                    int oldJumps = script.Workers[wi].JumpTargets.Count;
                    var (guard, action) = rng.ExpandGuarded(new AiSnippetArgs(AiCommandId.EncodeMonster(0), 2, 0));
                    byte[] res = AiScript_File.AppendGuardedAction(script, wi, ei, guard, action);
                    var g3 = AiScript_File.Read(res);
                    bool walkOk = g3.CodeWalkClosedExactly && g3.UnknownOpcodes.Count == 0;
                    bool gRt0 = AiScript_File.Write(g3).AsSpan().SequenceEqual(res);
                    bool jumpsGrew = g3.Workers[wi].JumpTargets.Count == oldJumps + 2;
                    bool repointed = g3.Workers[wi].Entrypoints[ei] == oldCode;   // that entrypoint now at the appended block
                    // the rejoin-to-original slot (index oldJumps+1) must NOT point back at the block start, else the
                    // handler infinite-loops (the one-past-end sentinel entrypoint case). Sentinel remap must prevent it.
                    bool noLoop = g3.Workers[wi].JumpTargets[oldJumps + 1] != oldCode;
                    bool targetsValid = g3.Workers.All(w =>
                        w.Entrypoints.All(t => t >= 0 && t <= g3.CodeLength) && w.JumpTargets.All(t => t >= 0 && t <= g3.CodeLength));
                    var vr = AiValidator.ValidateRebuilt(res, aiFile.Length);
                    if (walkOk && gRt0 && jumpsGrew && repointed && noLoop && targetsValid && vr.IsValid) guardOk++;
                    else fails.Add($"{id}: guarded-action w{wi}ep{ei} not clean (walk={walkOk} rt0={gRt0} jumps={jumpsGrew} repoint={repointed} noLoop={noLoop} valid={targetsValid} vr={vr.IsValid})");
                }
                catch (Exception ex) { fails.Add($"{id}: guarded-action w{wi}ep{ei} threw {ex.GetType().Name}: {ex.Message}"); }
            }

            // 4a2) CONDITIONAL snippet (#11): the readChrProperty(self,field) < N -> force command guard. Expand +
            //      AppendGuardedAction on worker wi entrypoint 0; re-parses clean, grows the jump-table, validates.
            try
            {
                condTested++;
                int oldJumps = script.Workers[wi].JumpTargets.Count;
                var (guard, action) = cond.ExpandGuarded(new AiSnippetArgs(AiCommandId.EncodeMonster(0), 0x38, 25));
                byte[] res = AiScript_File.AppendGuardedAction(script, wi, 0, guard, action);
                var g3 = AiScript_File.Read(res);
                bool walkOk = g3.CodeWalkClosedExactly && g3.UnknownOpcodes.Count == 0;
                bool gRt0 = AiScript_File.Write(g3).AsSpan().SequenceEqual(res);
                bool jumpsGrew = g3.Workers[wi].JumpTargets.Count == oldJumps + 2;
                var vr = AiValidator.ValidateRebuilt(res, aiFile.Length);
                if (walkOk && gRt0 && jumpsGrew && vr.IsValid) condOk++;
                else fails.Add($"{id}: conditional snippet not clean (walk={walkOk} rt0={gRt0} jumps={jumpsGrew} valid={vr.IsValid})");
            }
            catch (Exception ex) { fails.Add($"{id}: conditional snippet threw {ex.GetType().Name}: {ex.Message}"); }

            // 4a3) Supported HP<50% snippet: readChrProperty(Self, NearDeath=0x0119).
            // The snippet no longer emits raw HP/maxHP arithmetic; validate its actual
            // native boolean contract rather than accepting a stale DIV/MUL shape.
            try
            {
                hpCondTested++;
                int oldJumps = script.Workers[wi].JumpTargets.Count;
                var (guard, action) = hpCond.ExpandGuarded(new AiSnippetArgs(AiCommandId.EncodeMonster(0), 50, 0));
                byte[] res = AiScript_File.AppendGuardedAction(script, wi, 0, guard, action);
                var g3 = AiScript_File.Read(res);
                bool walkOk = g3.CodeWalkClosedExactly && g3.UnknownOpcodes.Count == 0;
                bool gRt0 = AiScript_File.Write(g3).AsSpan().SequenceEqual(res);
                bool jumpsGrew = g3.Workers[wi].JumpTargets.Count == oldJumps + 2;
                bool hpGuardOk = guard.Count == 3
                    && guard[0].Opcode == 0xAE && guard[0].Operand == AiSnippetLibrary.SelfRef
                    && guard[1].Opcode == 0xAE && guard[1].Operand == 0x0119
                    && guard[2].Opcode == 0xB5 && guard[2].Operand == 0x700F;
                var vr = AiValidator.ValidateRebuilt(res, aiFile.Length);
                if (walkOk && gRt0 && jumpsGrew && hpGuardOk && vr.IsValid) hpCondOk++;
                else fails.Add($"{id}: HP<50% snippet not clean (walk={walkOk} rt0={gRt0} jumps={jumpsGrew} nearDeath={hpGuardOk} valid={vr.IsValid})");
            }
            catch (Exception ex) { fails.Add($"{id}: HP% snippet threw {ex.GetType().Name}: {ex.Message}"); }

            // 4b) SENTINEL entrypoint (value == CodeLength, the "empty/return handler" one-past-end case): a guarded
            //     append here must NOT route the rejoin back to the block start (infinite loop). Exercises finding-1's fix.
            for (int sw = 0; sw < script.Workers.Count; sw++)
            {
                int sep = -1;
                for (int e = 0; e < script.Workers[sw].Entrypoints.Count; e++)
                    if (script.Workers[sw].Entrypoints[e] == script.CodeLength) { sep = e; break; }
                if (sep < 0) continue;
                sentinelTested++;
                try
                {
                    int oldCode = script.CodeLength;
                    var (guard, action) = rng.ExpandGuarded(new AiSnippetArgs(AiCommandId.EncodeMonster(0), 2, 0));
                    byte[] res = AiScript_File.AppendGuardedAction(script, sw, sep, guard, action);
                    var g3 = AiScript_File.Read(res);
                    int sj = script.Workers[sw].JumpTargets.Count;   // index of the original-entry slot = sj+1
                    bool noLoop = g3.Workers[sw].JumpTargets[sj + 1] != oldCode;   // must be remapped to new one-past-end
                    bool walkOk = g3.CodeWalkClosedExactly && g3.UnknownOpcodes.Count == 0;
                    var vr = AiValidator.ValidateRebuilt(res, aiFile.Length);
                    if (noLoop && walkOk && vr.IsValid) sentinelOk++;
                    else fails.Add($"{id}: SENTINEL guarded w{sw}ep{sep} loops/invalid (noLoop={noLoop} walk={walkOk} vr={vr.IsValid})");
                }
                catch (Exception ex) { fails.Add($"{id}: SENTINEL guarded w{sw} threw {ex.GetType().Name}: {ex.Message}"); }
                break;   // one sentinel per monster is enough coverage
            }

            // 4c) SYNTHETIC sentinel: the corpus has no entrypoint == CodeLength, so manufacture one ONCE (patch a
            //     worker's last entry-table slot to CodeLength, re-Read) and prove AppendGuardedAction does NOT loop.
            if (!synthSentinelDone && script.Workers[wi].Entrypoints.Count > 0)
            {
                synthSentinelDone = true;
                try
                {
                    byte[] patched = (byte[])aiFile.Clone();
                    int desc = script.Workers[wi].DescriptorOffset;
                    int entryTab = (int)U32(patched, desc + 0x20);
                    int ep = script.Workers[wi].Entrypoints.Count - 1;
                    W32(patched, entryTab + 4 * ep, (uint)script.CodeLength);   // make entrypoint[ep] the one-past-end sentinel
                    var ps = AiScript_File.Read(patched);
                    bool isSentinel = ps.Workers[wi].Entrypoints[ep] == ps.CodeLength;
                    var (guard, action) = rng.ExpandGuarded(new AiSnippetArgs(AiCommandId.EncodeMonster(0), 2, 0));
                    byte[] res = AiScript_File.AppendGuardedAction(ps, wi, ep, guard, action);
                    var g3 = AiScript_File.Read(res);
                    int sj = ps.Workers[wi].JumpTargets.Count;
                    bool noLoop = g3.Workers[wi].JumpTargets[sj + 1] != ps.CodeLength;   // remapped, not pointing at block start
                    bool walkOk = g3.CodeWalkClosedExactly && g3.UnknownOpcodes.Count == 0;
                    var vr = AiValidator.ValidateRebuilt(res, patched.Length);
                    if (isSentinel && noLoop && walkOk && vr.IsValid) synthSentinel = 1;
                    else fails.Add($"{id}: SYNTH sentinel failed (isSentinel={isSentinel} noLoop={noLoop} walk={walkOk} vr={vr.IsValid})");
                }
                catch (Exception ex) { fails.Add($"{id}: SYNTH sentinel threw {ex.GetType().Name}: {ex.Message}"); }
            }

            // 4d) SYNTHETIC stack-break (Level 1): remove ONLY the TARGET push of a command triplet (keeping
            //     command+call) so the call is left short an arg — the original-vs-edited stack-delta checker MUST
            //     report a NEW imbalance. Proves the checker actually catches a removed argument (not just no-ops).
            if (synthStack == 0)
            {
                var branchTargets = new HashSet<int>(script.Workers.SelectMany(w => w.Entrypoints.Concat(w.JumpTargets)));
                var cmd = AiAutomation.DetectCommandActions(script)
                    .FirstOrDefault(a => a.Removable && !branchTargets.Contains(a.RemoveOffsets.Min() - script.ScriptStart));
                if (cmd != null)
                {
                    try
                    {
                        int targetOff = cmd.RemoveOffsets.Min();   // the target push (earliest of the triplet)
                        var broken = script.Instructions.Where(i => i.Offset != targetOff).ToList();
                        var rep = AiValidator.Validate(script, broken);
                        if (HasDiagnostic(rep, Strings.U_Ai_ValStackImbalance)) synthStack = 1;
                        else fails.Add($"{id}: SYNTH stack-break NOT caught ({rep.ErrorCount} errors; expected a stack-imbalance Error)");
                    }
                    catch (Exception ex) { fails.Add($"{id}: SYNTH stack-break threw {ex.GetType().Name}: {ex.Message}"); }
                }
            }
        }

        // 5) POSITIVE-CATCH (synthetic, on the FIRST few scripts only — keep the gate fast).
        if (posTested < 8 && wi >= 0)
        {
            posTested++;
            // (a) remove an instruction that is a jump target -> validator Error + Rebuild actually throws.
            try
            {
                var w = script.Workers.FirstOrDefault(x => x.JumpTargets.Count > 0 && x.JumpTargets.Any(t => t > 0 && t < script.CodeLength));
                if (w == null) { posDangle++; goto doneDangle; }
                int tgt = w.JumpTargets.First(t => t > 0 && t < script.CodeLength);
                var listed = script.Instructions.Where(i => (i.Offset - script.ScriptStart) != tgt).ToList();
                var rep = AiValidator.Validate(script, listed);
                bool caught = HasDiagnostic(rep, Strings.U_Ai_ValDanglingJump)
                    || HasDiagnostic(rep, Strings.U_Ai_ValDanglingEntrypoint);
                bool rebuildThrows = false;
                try { AiScript_File.Rebuild(script, listed); } catch { rebuildThrows = true; }
                if (caught && rebuildThrows) posDangle++;
                else fails.Add($"{id}: dangling not caught (validatorErr={caught} rebuildThrew={rebuildThrows})");
                doneDangle: ;
            }
            catch (Exception ex) { fails.Add($"{id}: dangling probe threw {ex.GetType().Name}"); }

            // (b) inject an unknown opcode 0xFF -> validator Error, no throw.
            try
            {
                var listed = script.Instructions.ToList();
                listed.Insert(listed.Count / 2, new AiInstruction { Offset = -1, Opcode = 0xFF, HasOperand = false, Operand = 0 });
                var rep = AiValidator.Validate(script, listed);
                if (HasDiagnostic(rep, Strings.U_Ai_ValUnknownOpcode)) posOpcode++;
                else fails.Add($"{id}: unknown opcode not caught");
            }
            catch (Exception ex) { fails.Add($"{id}: opcode probe threw {ex.GetType().Name}"); }

            // (c) set a branch operand out of the jump-table range -> validator Error.
            try
            {
                var w = script.Workers.FirstOrDefault(x => x.JumpTargets.Count > 0 && x.Entrypoints.Count > 0);
                if (w != null)
                {
                    var listed = new List<AiInstruction>();
                    bool patched = false;
                    foreach (var i in script.Instructions)
                    {
                        if (!patched && i.OperandKind == AiOperandKind.JumpIndex)
                        {
                            // Build the bad row the way the editor's AiAsmRow.ToInstruction does — WITHOUT OperandKind
                            // (left None) — so this proves the validator derives the kind from the opcode (finding-2 fix).
                            listed.Add(new AiInstruction { Offset = i.Offset, Opcode = i.Opcode, HasOperand = true, Operand = (ushort)(w.JumpTargets.Count + 50) });
                            patched = true;
                        }
                        else listed.Add(i);
                    }
                    var rep = AiValidator.Validate(script, listed);
                    if (!patched || HasDiagnostic(rep, Strings.U_Ai_ValJumpIndexExceeds)
                        || HasDiagnostic(rep, Strings.U_Ai_ValJumpIndexOob)) posJumpOob++;
                    else fails.Add($"{id}: jump-index OOB not caught");
                }
                else posJumpOob++;
            }
            catch (Exception ex) { fails.Add($"{id}: jump-OOB probe threw {ex.GetType().Name}"); }
        }
    }

    bool pass = withScript > 0
        && validateClean == withScript && rt0InfoPresent == withScript
        && linInsertOk == withScript && growNoopRt0 == withScript
        && growWorkersOk == growWorkersTested && growWorkersTested > 0
        && guardOk == guardTested && guardTested > 0
        && condOk == condTested && condTested > 0
        && hpCondOk == hpCondTested && hpCondTested > 0
        && sentinelOk == sentinelTested && synthSentinel == 1 && synthStack == 1
        && cmdRtChar == FFXProjectEditor.FfxLib.Dictionaries.CommandCharacter_Dictionary.Instance.Count
        && cmdRtMon == FFXProjectEditor.FfxLib.Dictionaries.CommandMonster1_Dictionary.Instance.Count
        && cmdRtMon2 == FFXProjectEditor.FfxLib.Dictionaries.CommandMonster2_Dictionary.Instance.Count
        && firaga && thundaga && multiFira && notCmd
        && posDangle == posTested && posOpcode == posTested && posJumpOob == posTested
        && fails.Count == 0;

    var sb = new StringBuilder();
    sb.AppendLine("=== AiScriptLab --ai2 gate (validator + command-id + jump-grow + guarded-action + templates) ===");
    sb.AppendLine($"monsters w/ AiFile  : {total}   with script: {withScript}");
    sb.AppendLine($"AiCommandId char rt : {cmdRtChar}/{FFXProjectEditor.FfxLib.Dictionaries.CommandCharacter_Dictionary.Instance.Count}   mon1 rt: {cmdRtMon}/{FFXProjectEditor.FfxLib.Dictionaries.CommandMonster1_Dictionary.Instance.Count}   mon2 rt: {cmdRtMon2}/{FFXProjectEditor.FfxLib.Dictionaries.CommandMonster2_Dictionary.Instance.Count}   (Firaga={firaga} Thundaga={thundaga} MultiFira={multiFira} notCmd={notCmd})");
    sb.AppendLine($"validator clean     : {validateClean}/{withScript}   RT0 info present: {rt0InfoPresent}/{withScript}");
    sb.AppendLine($"linear insert ok    : {linInsertOk}/{withScript}");
    sb.AppendLine($"jump-grow ok        : {growWorkersOk}/{growWorkersTested} workers   no-op==RT0: {growNoopRt0}/{withScript}");
    sb.AppendLine($"guarded-action ok   : {guardOk}/{guardTested}   (conditional append + repoint + grow, first+last entrypoint, re-parses clean + validates)");
    sb.AppendLine($"conditional snippet : {condOk}/{condTested}   (#11: readChrProperty(self,field) < N -> force command — expands, grows jump-table, re-parses, validates)");
    sb.AppendLine($"stack-break caught  : {synthStack}/1   (Level 1: removing a command's arg-push -> the original-vs-edited stack-delta checker reports the new imbalance)");
    sb.AppendLine($"HP<50% snippet      : {hpCondOk}/{hpCondTested}   (readChrProperty(Self, NearDeath=0x0119); exact guard shape, expands + validates)");
    sb.AppendLine($"sentinel-entry ok   : {sentinelOk}/{sentinelTested}  corpus + synthetic={synthSentinel}/1   (one-past-end entrypoint guarded append does NOT self-loop)");
    sb.AppendLine($"positive-catch      : dangle {posDangle}/{posTested}  opcode {posOpcode}/{posTested}  jumpOOB {posJumpOob}/{posTested}  (jumpOOB via editor path: OperandKind unset)");
    sb.AppendLine($"VERDICT             : {(pass ? "PASS" : "FAIL")}");
    if (fails.Count > 0) { sb.AppendLine("FAILS:"); foreach (var f in fails.Take(25)) sb.AppendLine("  " + f); }
    Console.WriteLine(sb.ToString());

    if (jsonOut != null)
    {
        var verdict = new
        {
            pass, total, withScript, cmdRtChar, cmdRtMon, cmdRtMon2, firaga, thundaga, multiFira, notCmd,
            validateClean, rt0InfoPresent, linInsertOk, growNoopRt0, growWorkersTested, growWorkersOk,
            guardOk, guardTested, sentinelTested, sentinelOk,
            posDangle, posOpcode, posJumpOob, posTested, fails = fails.Take(50).ToArray(),
        };
        File.WriteAllText(jsonOut, JsonSerializer.Serialize(verdict, new JsonSerializerOptions { WriteIndented = true }));
    }
    return pass ? 0 : 1;
}

// ============================================================================================================
// 1-CLICK AUTOMATION GATE (--ai3). Proves AiAutomation (the layer behind the Monster AI Editor's "Adicionar
// habilidade" / "Tirar ação" buttons) over the real corpus:
//   * AddAbility resolves CombatHandler.onTurn from the monster WorkerFile, copies a selected action's real target
//     + perform/force shape, swaps only the command operand, and produces an AiFile that VALIDATES + RE-PARSES clean;
    //   * DetectCommandActions finds performCommand/forcePerformCommand actions without throwing; removable ones carry
    //     a stack-neutral action run (literal-target triplet, or an exact computed-target recipe + command);
//   * RemoveAction: when the validator approves dropping that triplet, Rebuild shrinks the AiFile by EXACTLY the
//     dropped bytes and re-parses clean (and when the validator catches a dangle, that removal is blocked, not run).
// Structure only — in-game behaviour stays RT2 (probe). Mirrors the rigor of the --ai2 guarded-action proof.
// ============================================================================================================
static int Ai3Gate(List<string> files, string? jsonOut)
{
    int total = 0, withScript = 0;
    int mappingTested = 0, mappingResolved = 0;
    int addTested = 0, addOk = 0, addRandomOk = 0, addTemplateMissing = 0;
    int detectScripts = 0, detectActions = 0, removableActions = 0;
    int removeTested = 0, removeOk = 0, removeBlocked = 0;
    int reorderTested = 0, reorderOk = 0, groupReorderTested = 0, groupReorderOk = 0, changeTested = 0, changeOk = 0;
    int dupTested = 0, dupOk = 0, toggleTested = 0, toggleOk = 0, buffTested = 0, buffOk = 0;
    int nulAllTested = 0, nulAllOk = 0;
    int copyTested = 0, copyOk = 0;
    // #6 — buffs/status (writeChrProperty) + stats (setStatField) in the action list, with generic stack-neutral remove.
    int buffActions = 0, statActions = 0, buffRemovable = 0, statRemovable = 0, removeFieldTested = 0, removeFieldOk = 0;
    // #8 change-target (literal sentinel swap) + #9 insert-second (multi-cast sequence).
    int chgTgtTested = 0, chgTgtOk = 0, litTargetActions = 0, insSecondTested = 0, insSecondOk = 0;
    int localChanceTested = 0, localChanceOk = 0;
    int linkedChangeTested = 0, linkedChangeOk = 0, linkedSecondTested = 0, linkedSecondOk = 0;
    int linkedLocalChanceTested = 0, linkedLocalChanceOk = 0;
    int targetRecipeTested = 0, targetRecipeOk = 0, linkedComputedTargetTested = 0, linkedComputedTargetOk = 0;
    int sequentialGrowTested = 0, sequentialGrowOk = 0;
    int stopHookTested = 0, stopHookOk = 0, stopLocalTested = 0, stopLocalOk = 0;
    int rebuildSafetyBlocked = 0;
    var fails = new List<string>();

    // Firaga (0x3049) is the RT2-proven command operand — use it as the ability every monster receives in the test.
    const ushort Firaga = 0x3049;

    static bool HasOnlyBaselineUnknownErrors(AiValidationReport report, AiScriptFile before, AiScriptFile after)
    {
        List<AiValidationFinding> errors = report.Errors.ToList();
        return errors.Count > 0
               && errors.All(e => e.Message.StartsWith("opcodes desconhecidos:", StringComparison.Ordinal))
               && before.CodeWalkClosedExactly
               && after.CodeWalkClosedExactly
               && before.Workers.Count == after.Workers.Count
               && before.UnknownOpcodes.SequenceEqual(after.UnknownOpcodes);
    }

    static bool IsUnknownOnlyValidationError(AiValidationFinding finding) =>
        finding.Message.StartsWith("opcode desconhecido", StringComparison.Ordinal)
        || finding.Message.StartsWith("opcodes desconhecidos", StringComparison.Ordinal);

    static bool ProposedPreservesBaselineUnknownInstructions(AiScriptFile source, IReadOnlyList<AiInstruction> proposed)
    {
        List<(int Offset, byte Opcode)> sourceUnknowns = source.Instructions
            .Where(i => !AiScript_File.IsKnownOpcode(i.Opcode))
            .Select(i => (i.Offset, i.Opcode))
            .OrderBy(x => x.Offset)
            .ToList();

        List<(int Offset, byte Opcode)> proposedUnknowns = proposed
            .Where(i => !AiScript_File.IsKnownOpcode(i.Opcode))
            .Select(i => (i.Offset, i.Opcode))
            .OrderBy(x => x.Offset)
            .ToList();

        return sourceUnknowns.SequenceEqual(proposedUnknowns)
               && proposedUnknowns.All(x => x.Offset >= 0);
    }

    static bool ValidateRebuiltAllowingBaselineUnknowns(byte[] ai, AiScriptFile before, int originalLength, out string why)
    {
        why = "";
        try
        {
            AiValidationReport report = AiValidator.ValidateRebuilt(ai, originalLength);
            if (report.IsValid) return true;

            AiScriptFile rr = AiScript_File.Read(ai);
            if (HasOnlyBaselineUnknownErrors(report, before, rr))
            {
                why = $"baseline unknowns preserved [{string.Join(" ", rr.UnknownOpcodes.Select(b => b.ToString("X2")))}]";
                return true;
            }

            why = string.Join(" | ", report.Errors.Select(e => e.Message));
            return false;
        }
        catch (Exception ex) { why = $"Read threw {ex.GetType().Name}"; return false; }
    }

    static bool ValidateInstructionsAllowingBaselineUnknowns(AiScriptFile source, IReadOnlyList<AiInstruction> proposed, out string why)
    {
        why = "";
        try
        {
            AiValidationReport report = AiValidator.Validate(source, proposed);
            if (report.IsValid) return true;

            byte[] rebuilt = AiScript_File.Rebuild(source, proposed);
            AiScriptFile rr = AiScript_File.Read(rebuilt);
            if (report.Errors.Any()
                && report.Errors.All(IsUnknownOnlyValidationError)
                && ProposedPreservesBaselineUnknownInstructions(source, proposed)
                && source.CodeWalkClosedExactly
                && rr.CodeWalkClosedExactly
                && source.Workers.Count == rr.Workers.Count
                && source.UnknownOpcodes.SequenceEqual(rr.UnknownOpcodes))
            {
                why = $"baseline unknowns preserved [{string.Join(" ", rr.UnknownOpcodes.Select(b => b.ToString("X2")))}]";
                return true;
            }

            why = string.Join(" | ", report.Errors.Select(e => e.Message));
            return false;
        }
        catch (Exception ex) { why = $"Validate threw {ex.GetType().Name}: {ex.Message}"; return false; }
    }

    static bool Reparses(byte[] ai, AiScriptFile before, int wantWorkers, out string why)
    {
        why = "";
        try
        {
            AiScriptFile rr = AiScript_File.Read(ai);
            if (!rr.CodeWalkClosedExactly) { why = "walk did not close"; return false; }
            if (rr.Workers.Count != wantWorkers) { why = $"worker count {rr.Workers.Count} != {wantWorkers}"; return false; }
            if (!rr.UnknownOpcodes.SequenceEqual(before.UnknownOpcodes))
            {
                why = $"unknown opcode drift [{string.Join(" ", rr.UnknownOpcodes.Select(b => b.ToString("X2")))}] != [{string.Join(" ", before.UnknownOpcodes.Select(b => b.ToString("X2")))}]";
                return false;
            }
            if (rr.UnknownOpcodes.Count != 0)
                why = $"baseline unknowns preserved [{string.Join(" ", rr.UnknownOpcodes.Select(b => b.ToString("X2")))}]";
            return true;
        }
        catch (Exception ex) { why = $"Read threw {ex.GetType().Name}"; return false; }
    }

    // Order-independent identity of a detected action (kind + command/field/value) — for the reorder same-set check.
    static string ActKey(AiDetectedAction a) => $"{a.Kind}:{a.CommandOperand:X4}:{a.PerformOperand:X4}:{a.FieldId:X4}:{a.FieldValue:X4}";
    static bool SameTargetAndMode(AiDetectedAction a, AiDetectedAction b) =>
        a.PerformOperand == b.PerformOperand
        && a.ForcePerform == b.ForcePerform
        && a.TargetOpcode == b.TargetOpcode
        && a.TargetOperand == b.TargetOperand
        && a.TargetIsLiteral == b.TargetIsLiteral;

    static AiDetectedAction? FindCopiedCommand(AiScriptFile before, byte[] editedAi, AiDetectedAction template, ushort commandOperand)
    {
        AiScriptFile after = AiScript_File.Read(editedAi);
        int appendedAt = before.ScriptStart + before.CodeLength;
        return AiAutomation.DetectCommandActions(after).FirstOrDefault(a =>
            a.CallOffset >= appendedAt
            && a.CommandOperand == commandOperand
            && SameTargetAndMode(a, template));
    }

    foreach (string path in files)
    {
        byte[] monster = File.ReadAllBytes(path);
        byte[]? aiFile = AiScript_File.SliceAiFileFromMonster(monster);
        if (aiFile is null) continue;
        total++;
        string id = Path.GetFileNameWithoutExtension(path);
        AiScriptFile script;
        try { script = AiScript_File.Read(aiFile); }
        catch (Exception ex) { fails.Add($"{id}: Read threw {ex.GetType().Name}"); continue; }
        if (!script.HasScript) continue;
        withScript++;
        if (!AiAutomation.CanRebuildSafely(script))
        {
            rebuildSafetyBlocked++;
            continue;
        }

        // Detect command actions early because the "add ability" path now copies one selected action as its template.
        IReadOnlyList<AiDetectedAction> actions;
        try { actions = AiAutomation.DetectCommandActions(script); }
        catch (Exception ex) { fails.Add($"{id}: DetectCommandActions threw {ex.GetType().Name}"); continue; }
        AiDetectedAction? addTemplate = actions.FirstOrDefault(a => a.Removable);

        // 1) Add ability (always-guard) — resolve WorkerFile CombatHandler.onTurn, validate + re-parse the grow.
        mappingTested++;
        bool hasOnTurnHook = AiWorkerMapping.TryResolveCombatOnTurn(monster, script, out AiEventHook onTurnHook, out _);
        if (hasOnTurnHook) mappingResolved++;

        if (hasOnTurnHook && addTemplate != null)
        {
            addTested++;
            try
            {
                int wi = onTurnHook.WorkerIndex, ei = onTurnHook.EntrypointIndex;
                List<AiInstruction>? copiedAction = AiAutomation.BuildCopiedCommandAction(script, addTemplate, Firaga);
                bool copiedShapeOk = copiedAction != null
                    && copiedAction.Count == 3
                    && copiedAction[0].Opcode == addTemplate.TargetOpcode
                    && copiedAction[0].Operand == addTemplate.TargetOperand
                    && copiedAction[1].Opcode == 0xAE
                    && copiedAction[1].Operand == Firaga
                    && copiedAction[2].Opcode == 0xD8
                    && copiedAction[2].Operand == addTemplate.PerformOperand;
                byte[] added = AiAutomation.AddAbilityFromActionTemplate(script, addTemplate, Firaga, random: false, k: 0, wi, ei);
                bool inRange = wi >= 0 && wi < script.Workers.Count && ei >= 0 && ei < script.Workers[wi].Entrypoints.Count;
                bool valid = ValidateRebuiltAllowingBaselineUnknowns(added, script, aiFile.Length, out string validWhy);
                bool reparses = Reparses(added, script, script.Workers.Count, out string why);
                AiScriptFile rr = AiScript_File.Read(added);
                bool repointed = rr.Workers[wi].Entrypoints[ei] == script.CodeLength;
                AiDetectedAction? copied = FindCopiedCommand(script, added, addTemplate, Firaga);

                byte[] monsterAdded = AiScript_File.SpliceAiFileIntoMonsterGrow(monster, added);
                byte[]? slicedBack = AiScript_File.SliceAiFileFromMonster(monsterAdded);
                bool saveReloadCopiedAppears = slicedBack != null && FindCopiedCommand(script, slicedBack, addTemplate, Firaga) != null;

                if (copiedShapeOk && copied != null && inRange && valid && added.Length > aiFile.Length && reparses && repointed && saveReloadCopiedAppears) addOk++;
                else fails.Add($"{id}: add-ability(copy-template) not clean (shape={copiedShapeOk} copied={(copied != null)} inRange={inRange} valid={valid}/{validWhy} reparse={reparses}/{why} repointed={repointed} saveReload={saveReloadCopiedAppears})");
            }
            catch (Exception ex) { fails.Add($"{id}: add-ability(copy-template) threw {ex.GetType().Name}: {ex.Message}"); }

            // RNG-guard variant (1-in-3).
            try
            {
                byte[] added = AiAutomation.AddAbilityFromActionTemplate(script, addTemplate, Firaga, random: true, k: 3, onTurnHook.WorkerIndex, onTurnHook.EntrypointIndex);
                if (ValidateRebuiltAllowingBaselineUnknowns(added, script, aiFile.Length, out _) && Reparses(added, script, script.Workers.Count, out _) && FindCopiedCommand(script, added, addTemplate, Firaga) != null) addRandomOk++;
                else fails.Add($"{id}: add-ability(rng) not clean");
            }
            catch (Exception ex) { fails.Add($"{id}: add-ability(rng) threw {ex.GetType().Name}: {ex.Message}"); }

            // Stop-after variant: failed guard continues into the old chain; successful action ends on RET.
            try
            {
                stopHookTested++;
                int wi = onTurnHook.WorkerIndex, ei = onTurnHook.EntrypointIndex;
                int oldJumps = script.Workers[wi].JumpTargets.Count;
                byte[] added = AiAutomation.AddAbilityFromActionTemplate(script, addTemplate, Firaga, random: true, k: 3, wi, ei, stopAfterAction: true);
                bool valid = ValidateRebuiltAllowingBaselineUnknowns(added, script, aiFile.Length, out string validWhy);
                bool reparses = Reparses(added, script, script.Workers.Count, out string why);
                AiScriptFile rr = AiScript_File.Read(added);
                int appendedAt = script.ScriptStart + script.CodeLength;
                bool copied = FindCopiedCommand(script, added, addTemplate, Firaga) != null;
                bool hasRet = rr.Instructions.Any(i => i.Offset >= appendedAt && i.Opcode == 0x3C);
                bool noAppendJmp = !rr.Instructions.Any(i => i.Offset >= appendedAt && i.Opcode == 0xB0);
                bool jumpPlusOne = rr.Workers[wi].JumpTargets.Count == oldJumps + 1;
                bool repointed = rr.Workers[wi].Entrypoints[ei] == script.CodeLength;
                if (valid && reparses && copied && hasRet && noAppendJmp && jumpPlusOne && repointed) stopHookOk++;
                else fails.Add($"{id}: stop-hook add bad (valid={valid}/{validWhy} reparse={reparses}/{why} copied={copied} ret={hasRet} noB0={noAppendJmp} jump+1={jumpPlusOne} repointed={repointed})");
            }
            catch (Exception ex) { fails.Add($"{id}: stop-hook add threw {ex.GetType().Name}: {ex.Message}"); }
        }
        else if (hasOnTurnHook)
        {
            addTemplateMissing++;
        }

        // 2) Detect command actions (must not throw).
        if (actions.Count > 0) detectScripts++;
        detectActions += actions.Count;
        foreach (var a in actions)
        {
            if (a.Removable && a.RemoveOffsets.Count < 3) fails.Add($"{id}: removable action '{a.AbilityName}' has {a.RemoveOffsets.Count} offsets (want >=3)");
            if (a.Removable) removableActions++;
        }

        // 2b) Detect ALL actions (#6): commands + buffs (writeChrProperty 7018) + stats (setStatField 70AB). Must not
        //     throw; the command subset must equal DetectCommandActions; removable buff = 4-offset run, removable stat = 3.
        IReadOnlyList<AiDetectedAction> all;
        try { all = AiAutomation.DetectActions(script); }
        catch (Exception ex) { fails.Add($"{id}: DetectActions threw {ex.GetType().Name}"); all = Array.Empty<AiDetectedAction>(); }
        if (all.Count(a => a.Kind == AiActionKind.Command) != actions.Count)
            fails.Add($"{id}: DetectActions command subset {all.Count(a => a.Kind == AiActionKind.Command)} != DetectCommandActions {actions.Count}");
        foreach (var a in all)
        {
            if (a.Kind == AiActionKind.Buff)
            { buffActions++; if (a.Removable) { buffRemovable++; if (a.RemoveOffsets.Count != 4) fails.Add($"{id}: removable buff '{a.AbilityName}' has {a.RemoveOffsets.Count} offsets (want 4)"); } }
            else if (a.Kind == AiActionKind.Stat)
            { statActions++; if (a.Removable) { statRemovable++; if (a.RemoveOffsets.Count != 3) fails.Add($"{id}: removable stat '{a.AbilityName}' has {a.RemoveOffsets.Count} offsets (want 3)"); } }
        }

        // 2c) Remove the first removable buff/stat the validator approves: the generic stack-neutral removal works for
        //     non-command actions too — Rebuild shrinks by exactly the dropped run and re-parses clean.
        var firstField = all.FirstOrDefault(a => a.Removable && a.Kind != AiActionKind.Command);
        if (firstField != null)
        {
            try
            {
                List<AiInstruction> kept = AiAutomation.InstructionsWithout(script, firstField);
                int droppedBytes = script.Instructions.Where(i => firstField.RemoveOffsets.Contains(i.Offset)).Sum(i => i.Length);
                if (AiValidator.Validate(script, kept).IsValid)
                {
                    removeFieldTested++;
                    byte[] rebuilt = AiScript_File.Rebuild(script, kept);
                    bool reparses = Reparses(rebuilt, script, script.Workers.Count, out string why);
                    if (rebuilt.Length == aiFile.Length - droppedBytes && reparses) removeFieldOk++;
                    else fails.Add($"{id}: remove {firstField.Kind} '{firstField.AbilityName}' bad (len {rebuilt.Length} want {aiFile.Length - droppedBytes}; {why})");
                }
            }
            catch (Exception ex) { fails.Add($"{id}: remove field threw {ex.GetType().Name}: {ex.Message}"); }
        }

        // 3) Remove the first removable action the validator approves: Rebuild shrinks by exactly the dropped bytes.
        var firstRemovable = actions.FirstOrDefault(a => a.Removable);
        if (firstRemovable != null)
        {
            try
            {
                List<AiInstruction> kept = AiAutomation.InstructionsWithout(script, firstRemovable);
                int droppedBytes = script.Instructions.Where(i => firstRemovable.RemoveOffsets.Contains(i.Offset)).Sum(i => i.Length);
                if (!AiValidator.Validate(script, kept).IsValid) { removeBlocked++; }   // a jump targets the triplet — correctly blocked
                else
                {
                    removeTested++;
                    byte[] rebuilt = AiScript_File.Rebuild(script, kept);
                    bool reparses = Reparses(rebuilt, script, script.Workers.Count, out string why);
                    if (rebuilt.Length == aiFile.Length - droppedBytes && reparses) removeOk++;
                    else fails.Add($"{id}: remove '{firstRemovable.AbilityName}' bad (len {rebuilt.Length} want {aiFile.Length - droppedBytes}; {why})");
                }
            }
            catch (Exception ex) { fails.Add($"{id}: remove threw {ex.GetType().Name}: {ex.Message}"); }
        }

        // 4) Reorder: move an action down past its ADJACENT removable neighbour OF ANY KIND (MoveActionInstructions
        //    now operates over the full action stream — #6) — LENGTH-PRESERVING, re-parses clean, and the multiset of
        //    action identities (kind + command/field/value) is unchanged (same actions, new order).
        AiDetectedAction? movable = null;
        for (int a = 0; a + 1 < all.Count; a++)
            if (all[a].Removable && all[a + 1].Removable) { movable = all[a]; break; }
        if (movable != null)
        {
            try
            {
                reorderTested++;
                var beforeKeys = all.Select(ActKey).OrderBy(x => x).ToList();
                List<AiInstruction>? moved = AiAutomation.MoveActionInstructions(script, movable, up: false);
                if (moved == null) fails.Add($"{id}: reorder returned null for an adjacent-removable pair");
                else
                {
                    bool valid = ValidateInstructionsAllowingBaselineUnknowns(script, moved, out string validWhy);
                    byte[] rb = AiScript_File.Rebuild(script, moved);
                    bool reparses = Reparses(rb, script, script.Workers.Count, out string why);
                    var afterKeys = AiAutomation.DetectActions(AiScript_File.Read(rb)).Select(ActKey).OrderBy(x => x).ToList();
                    if (valid && rb.Length == aiFile.Length && reparses && beforeKeys.SequenceEqual(afterKeys)) reorderOk++;
                    else fails.Add($"{id}: reorder bad (valid={valid}/{validWhy} len={rb.Length == aiFile.Length} reparse={reparses}/{why} sameSet={beforeKeys.SequenceEqual(afterKeys)})");
                }
            }
            catch (Exception ex) { fails.Add($"{id}: reorder threw {ex.GetType().Name}: {ex.Message}"); }
        }

        // 4b) Reorder group: move two consecutive removable actions down as a single multiselect block. This proves
        //     the Monster AI Editor 2 batch bar has a real bytecode core for contiguous self-contained actions.
        List<AiDetectedAction>? movableGroup = null;
        for (int a = 0; a + 2 < all.Count; a++)
            if (all[a].Removable && all[a + 1].Removable && all[a + 2].Removable)
            {
                movableGroup = new List<AiDetectedAction> { all[a], all[a + 1] };
                break;
            }
        if (movableGroup != null)
        {
            try
            {
                groupReorderTested++;
                var beforeKeys = all.Select(ActKey).OrderBy(x => x).ToList();
                List<AiInstruction>? moved = AiAutomation.MoveActionGroupInstructions(script, movableGroup, up: false);
                if (moved == null) fails.Add($"{id}: group reorder returned null for consecutive removable actions");
                else
                {
                    bool valid = ValidateInstructionsAllowingBaselineUnknowns(script, moved, out string validWhy);
                    byte[] rb = AiScript_File.Rebuild(script, moved);
                    bool reparses = Reparses(rb, script, script.Workers.Count, out string why);
                    var afterKeys = AiAutomation.DetectActions(AiScript_File.Read(rb)).Select(ActKey).OrderBy(x => x).ToList();
                    if (valid && rb.Length == aiFile.Length && reparses && beforeKeys.SequenceEqual(afterKeys)) groupReorderOk++;
                    else fails.Add($"{id}: group reorder bad (valid={valid}/{validWhy} len={rb.Length == aiFile.Length} reparse={reparses}/{why} sameSet={beforeKeys.SequenceEqual(afterKeys)})");
                }
            }
            catch (Exception ex) { fails.Add($"{id}: group reorder threw {ex.GetType().Name}: {ex.Message}"); }
        }

        // 5) Change ability: rewrite the first removable action's command to Firaga (0x3049) — LENGTH-PRESERVING,
        //    re-parses clean, and that exact command slot now reads 0x3049.
        if (firstRemovable != null)
        {
            try
            {
                changeTested++;
                List<AiInstruction>? ch = AiAutomation.ChangeActionInstructions(script, firstRemovable, Firaga);
                if (ch == null) fails.Add($"{id}: change returned null");
                else
                {
                    byte[] rb = AiScript_File.Rebuild(script, ch);
                    bool reparses = Reparses(rb, script, script.Workers.Count, out string why);
                    bool slotOk = AiScript_File.Read(rb).Instructions.Any(i2 => i2.Offset == firstRemovable.CmdPushOffset && i2.Operand == Firaga);
                    if (rb.Length == aiFile.Length && reparses && slotOk) changeOk++;
                    else fails.Add($"{id}: change bad (len={rb.Length == aiFile.Length} reparse={reparses}/{why} slot={slotOk})");
                }
            }
            catch (Exception ex) { fails.Add($"{id}: change threw {ex.GetType().Name}: {ex.Message}"); }

            // 5b) Change ability + linked Forbidden Rite: rewrite the command and append a writeChrProperty right
            //     after it. This is the UI checkbox path for "Trocar selecionada" with a linked debuff.
            try
            {
                linkedChangeTested++;
                int beforeBuffs = all.Count(a => a.Kind == AiActionKind.Buff);
                const int StatusWriteBytes = 12; // PUSHII target + PUSHII field + PUSHII value + CALLPOPA 7018.
                List<AiInstruction>? ch = AiAutomation.ChangeActionAndInsertChrPropertyWrite(
                    script, firstRemovable, Firaga, 0xFFFD, 0x002F, 255);
                if (ch == null) fails.Add($"{id}: linked-change returned null");
                else
                {
                    byte[] rb = AiScript_File.Rebuild(script, ch);
                    bool reparses = Reparses(rb, script, script.Workers.Count, out string why);
                    AiScriptFile rr = AiScript_File.Read(rb);
                    bool slotOk = rr.Instructions.Any(i2 => i2.Offset == firstRemovable.CmdPushOffset && i2.Operand == Firaga);
                    int afterBuffs = AiAutomation.DetectActions(rr).Count(a => a.Kind == AiActionKind.Buff);
                    if (rb.Length == aiFile.Length + StatusWriteBytes && reparses && slotOk && afterBuffs == beforeBuffs + 1) linkedChangeOk++;
                    else fails.Add($"{id}: linked-change bad (len={rb.Length - aiFile.Length}/{StatusWriteBytes} reparse={reparses}/{why} slot={slotOk} buffs={afterBuffs}/{beforeBuffs + 1})");
                }
            }
            catch (Exception ex) { fails.Add($"{id}: linked-change threw {ex.GetType().Name}: {ex.Message}"); }

            // 6) Duplicate: insert a copy of the triplet after it — GROWS by the triplet bytes, +1 action, re-parses.
            try
            {
                dupTested++;
                int tripletBytes = script.Instructions.Where(i => firstRemovable.RemoveOffsets.Contains(i.Offset)).Sum(i => i.Length);
                List<AiInstruction>? dup = AiAutomation.DuplicateActionInstructions(script, firstRemovable);
                if (dup == null) fails.Add($"{id}: duplicate returned null for a removable action");
                else
                {
                    byte[] rb = AiScript_File.Rebuild(script, dup);
                    bool reparses = Reparses(rb, script, script.Workers.Count, out string why);
                    int after = AiAutomation.DetectCommandActions(AiScript_File.Read(rb)).Count;
                    if (rb.Length == aiFile.Length + tripletBytes && reparses && after == actions.Count + 1) dupOk++;
                    else fails.Add($"{id}: duplicate bad (len={rb.Length == aiFile.Length + tripletBytes} reparse={reparses}/{why} count={after}/{actions.Count + 1})");
                }
            }
            catch (Exception ex) { fails.Add($"{id}: duplicate threw {ex.GetType().Name}: {ex.Message}"); }

            // 7) Toggle force: flip the call between 705A/700B — LENGTH-PRESERVING, re-parses, the call slot flipped.
            try
            {
                toggleTested++;
                ushort want = firstRemovable.ForcePerform ? (ushort)0x700B : (ushort)0x705A;
                List<AiInstruction>? tg = AiAutomation.ToggleForceInstructions(script, firstRemovable);
                if (tg == null) fails.Add($"{id}: toggle returned null");
                else
                {
                    byte[] rb = AiScript_File.Rebuild(script, tg);
                    bool reparses = Reparses(rb, script, script.Workers.Count, out string why);
                    bool flipped = AiScript_File.Read(rb).Instructions.Any(i2 => i2.Offset == firstRemovable.CallOffset && i2.Operand == want);
                    if (rb.Length == aiFile.Length && reparses && flipped) toggleOk++;
                    else fails.Add($"{id}: toggle bad (len={rb.Length == aiFile.Length} reparse={reparses}/{why} flipped={flipped})");
                }
            }
            catch (Exception ex) { fails.Add($"{id}: toggle threw {ex.GetType().Name}: {ex.Message}"); }
        }

        // 7b) Change target (#8): rewrite the first literal-target command's target to self (0xFFF3) — length-
        //     preserving, re-parses, that target slot now reads 0xFFF3.
        var firstLitTgt = all.FirstOrDefault(a => a.Kind == AiActionKind.Command && a.TargetIsLiteral);
        litTargetActions += all.Count(a => a.Kind == AiActionKind.Command && a.TargetIsLiteral);
        if (firstLitTgt != null)
        {
            try
            {
                chgTgtTested++;
                List<AiInstruction>? ct = AiAutomation.ChangeTargetInstructions(script, firstLitTgt, 0xFFF3);
                if (ct == null) fails.Add($"{id}: change-target returned null for a literal-target command");
                else
                {
                    byte[] rb = AiScript_File.Rebuild(script, ct);
                    bool reparses = Reparses(rb, script, script.Workers.Count, out string why);
                    bool slotOk = AiScript_File.Read(rb).Instructions.Any(i2 => i2.Offset == firstLitTgt.TargetPushOffset && i2.Operand == 0xFFF3);
                    if (rb.Length == aiFile.Length && reparses && slotOk) chgTgtOk++;
                    else fails.Add($"{id}: change-target bad (len={rb.Length == aiFile.Length} reparse={reparses}/{why} slot={slotOk})");
                }
            }
            catch (Exception ex) { fails.Add($"{id}: change-target threw {ex.GetType().Name}: {ex.Message}"); }
        }

        // 7c) Insert second command (#9): insert Firaga after the first removable command — GROWS by the triplet,
        //     +1 command action, re-parses clean, and the new Firaga push is present.
        if (firstRemovable != null)
        {
            try
            {
                insSecondTested++;
                int tripletBytes = script.Instructions.Where(i => firstRemovable.RemoveOffsets.Contains(i.Offset)).Sum(i => i.Length);
                List<AiInstruction>? ins2 = AiAutomation.InsertSecondCommand(script, firstRemovable, Firaga);
                if (ins2 == null) fails.Add($"{id}: insert-second returned null for a removable command");
                else
                {
                    byte[] rb = AiScript_File.Rebuild(script, ins2);
                    bool reparses = Reparses(rb, script, script.Workers.Count, out string why);
                    int after = AiAutomation.DetectCommandActions(AiScript_File.Read(rb)).Count;
                    bool hasFiraga = AiScript_File.Read(rb).Instructions.Any(i2 => i2.Opcode == 0xAE && i2.Operand == Firaga);
                    if (rb.Length == aiFile.Length + tripletBytes && reparses && after == actions.Count + 1 && hasFiraga) insSecondOk++;
                    else fails.Add($"{id}: insert-second bad (len={rb.Length == aiFile.Length + tripletBytes} reparse={reparses}/{why} count={after}/{actions.Count + 1} firaga={hasFiraga})");
                }
            }
            catch (Exception ex) { fails.Add($"{id}: insert-second threw {ex.GetType().Name}: {ex.Message}"); }

            // 7c2) Insert second command + linked Forbidden Rite. Proves "Multi-Fira 2 applies Silence/Darkness"
            //      style authoring: the status write is placed after the inserted command.
            try
            {
                linkedSecondTested++;
                int tripletBytes = script.Instructions.Where(i => firstRemovable.RemoveOffsets.Contains(i.Offset)).Sum(i => i.Length);
                int beforeBuffs = all.Count(a => a.Kind == AiActionKind.Buff);
                const int StatusWriteBytes = 12;
                List<AiInstruction>? ins2 = AiAutomation.InsertSecondCommandWithChrPropertyWrite(
                    script, firstRemovable, Firaga, 0xFFFD, 0x002E, 255);
                if (ins2 == null) fails.Add($"{id}: linked insert-second returned null for a removable command");
                else
                {
                    byte[] rb = AiScript_File.Rebuild(script, ins2);
                    bool reparses = Reparses(rb, script, script.Workers.Count, out string why);
                    AiScriptFile rr = AiScript_File.Read(rb);
                    int afterCommands = AiAutomation.DetectCommandActions(rr).Count;
                    int afterBuffs = AiAutomation.DetectActions(rr).Count(a => a.Kind == AiActionKind.Buff);
                    bool hasFiraga = rr.Instructions.Any(i2 => i2.Opcode == 0xAE && i2.Operand == Firaga);
                    int expectedDelta = tripletBytes + StatusWriteBytes;
                    if (rb.Length == aiFile.Length + expectedDelta && reparses && afterCommands == actions.Count + 1 && afterBuffs == beforeBuffs + 1 && hasFiraga) linkedSecondOk++;
                    else fails.Add($"{id}: linked insert-second bad (len={rb.Length - aiFile.Length}/{expectedDelta} reparse={reparses}/{why} cmds={afterCommands}/{actions.Count + 1} buffs={afterBuffs}/{beforeBuffs + 1} firaga={hasFiraga})");
                }
            }
            catch (Exception ex) { fails.Add($"{id}: linked insert-second threw {ex.GetType().Name}: {ex.Message}"); }
        }

        // 7d) Local optional insert: this is the human "Talvez 1/N on the selected action" path. It must wrap ONLY
        //     the newly inserted command in a local D7 skip branch, not repoint the worker entrypoint to an appended
        //     wrapper block. One new jump-table slot is expected for the skip target.
        var firstLocal = actions.FirstOrDefault(a => a.Removable && a.WorkerIndex >= 0 && a.WorkerIndex < script.Workers.Count);
        if (firstLocal != null)
        {
            try
            {
                localChanceTested++;
                int tripletBytes = script.Instructions.Where(i => firstLocal.RemoveOffsets.Contains(i.Offset)).Sum(i => i.Length);
                int expectedDelta = tripletBytes + 11 + 3 + 4; // RNG guard + D7 skip + copied command + jump-table slot.
                int oldJumps = script.Workers[firstLocal.WorkerIndex].JumpTargets.Count;
                int actionRel = firstLocal.CallOffset - script.ScriptStart;
                int containingEntry = script.Workers[firstLocal.WorkerIndex].Entrypoints
                    .Where(e => e <= actionRel)
                    .DefaultIfEmpty(-1)
                    .Max();

                byte[] rb = AiAutomation.InsertCommandAfterAction(script, firstLocal, Firaga, random: true, k: 2);
                bool valid = ValidateRebuiltAllowingBaselineUnknowns(rb, script, aiFile.Length, out string validWhy);
                bool reparses = Reparses(rb, script, script.Workers.Count, out string why);
                AiScriptFile rr = AiScript_File.Read(rb);
                int after = AiAutomation.DetectCommandActions(rr).Count;
                bool hasFiraga = rr.Instructions.Any(i2 => i2.Opcode == 0xAE && i2.Operand == Firaga);
                bool jumpPlusOne = rr.Workers[firstLocal.WorkerIndex].JumpTargets.Count == oldJumps + 1;
                bool entryNotRepointed = containingEntry < 0 || rr.Workers[firstLocal.WorkerIndex].Entrypoints.Contains(containingEntry);
                bool deltaOk = rb.Length == aiFile.Length + expectedDelta;
                if (valid && reparses && after == actions.Count + 1 && hasFiraga && jumpPlusOne && entryNotRepointed && deltaOk) localChanceOk++;
                else fails.Add($"{id}: local-chance insert bad (valid={valid}/{validWhy} reparse={reparses}/{why} count={after}/{actions.Count + 1} firaga={hasFiraga} jump+1={jumpPlusOne} entrySame={entryNotRepointed} delta={rb.Length - aiFile.Length}/{expectedDelta})");
            }
            catch (Exception ex) { fails.Add($"{id}: local-chance insert threw {ex.GetType().Name}: {ex.Message}"); }

            // 7d2) Local optional insert + linked Forbidden Rite. The RNG guard must wrap the command and the status
            //      write as a pair; entrypoint still must not be repointed.
            try
            {
                linkedLocalChanceTested++;
                int tripletBytes = script.Instructions.Where(i => firstLocal.RemoveOffsets.Contains(i.Offset)).Sum(i => i.Length);
                const int StatusWriteBytes = 12;
                int expectedDelta = tripletBytes + StatusWriteBytes + 11 + 3 + 4;
                int oldJumps = script.Workers[firstLocal.WorkerIndex].JumpTargets.Count;
                int actionRel = firstLocal.CallOffset - script.ScriptStart;
                int containingEntry = script.Workers[firstLocal.WorkerIndex].Entrypoints
                    .Where(e => e <= actionRel)
                    .DefaultIfEmpty(-1)
                    .Max();
                int beforeBuffs = all.Count(a => a.Kind == AiActionKind.Buff);

                byte[] rb = AiAutomation.InsertCommandAfterActionWithChrPropertyWrite(
                    script, firstLocal, Firaga, 0xFFFD, 0x002F, 255, random: true, k: 2);
                bool valid = ValidateRebuiltAllowingBaselineUnknowns(rb, script, aiFile.Length, out string validWhy);
                bool reparses = Reparses(rb, script, script.Workers.Count, out string why);
                AiScriptFile rr = AiScript_File.Read(rb);
                int afterCommands = AiAutomation.DetectCommandActions(rr).Count;
                int afterBuffs = AiAutomation.DetectActions(rr).Count(a => a.Kind == AiActionKind.Buff);
                bool hasFiraga = rr.Instructions.Any(i2 => i2.Opcode == 0xAE && i2.Operand == Firaga);
                bool jumpPlusOne = rr.Workers[firstLocal.WorkerIndex].JumpTargets.Count == oldJumps + 1;
                bool entryNotRepointed = containingEntry < 0 || rr.Workers[firstLocal.WorkerIndex].Entrypoints.Contains(containingEntry);
                bool deltaOk = rb.Length == aiFile.Length + expectedDelta;
                if (valid && reparses && afterCommands == actions.Count + 1 && afterBuffs == beforeBuffs + 1 && hasFiraga && jumpPlusOne && entryNotRepointed && deltaOk) linkedLocalChanceOk++;
                else fails.Add($"{id}: linked local-chance bad (valid={valid}/{validWhy} reparse={reparses}/{why} cmds={afterCommands}/{actions.Count + 1} buffs={afterBuffs}/{beforeBuffs + 1} firaga={hasFiraga} jump+1={jumpPlusOne} entrySame={entryNotRepointed} delta={rb.Length - aiFile.Length}/{expectedDelta})");
            }
            catch (Exception ex) { fails.Add($"{id}: linked local-chance threw {ex.GetType().Name}: {ex.Message}"); }

            // 7d3) Local optional insert + stop-after. If RNG fails it skips the new command and continues at the
            //      old next instruction; if it succeeds, the inserted command reaches RET and stops this pass.
            try
            {
                stopLocalTested++;
                int tripletBytes = script.Instructions.Where(i => firstLocal.RemoveOffsets.Contains(i.Offset)).Sum(i => i.Length);
                int expectedDelta = tripletBytes + 11 + 3 + 1 + 4; // RNG guard + D7 skip + copied command + RET + jump-table slot.
                int oldJumps = script.Workers[firstLocal.WorkerIndex].JumpTargets.Count;
                int actionRel = firstLocal.CallOffset - script.ScriptStart;
                int containingEntry = script.Workers[firstLocal.WorkerIndex].Entrypoints
                    .Where(e => e <= actionRel)
                    .DefaultIfEmpty(-1)
                    .Max();

                byte[] rb = AiAutomation.InsertCommandAfterAction(script, firstLocal, Firaga, random: true, k: 2, stopAfterAction: true);
                bool valid = ValidateRebuiltAllowingBaselineUnknowns(rb, script, aiFile.Length, out string validWhy);
                bool reparses = Reparses(rb, script, script.Workers.Count, out string why);
                AiScriptFile rr = AiScript_File.Read(rb);
                int after = AiAutomation.DetectCommandActions(rr).Count;
                bool hasFiraga = rr.Instructions.Any(i2 => i2.Opcode == 0xAE && i2.Operand == Firaga);
                bool hasRet = rr.Instructions.Any(i2 => i2.Opcode == 0x3C && i2.Offset > firstLocal.CallOffset);
                bool jumpPlusOne = rr.Workers[firstLocal.WorkerIndex].JumpTargets.Count == oldJumps + 1;
                bool entryNotRepointed = containingEntry < 0 || rr.Workers[firstLocal.WorkerIndex].Entrypoints.Contains(containingEntry);
                bool deltaOk = rb.Length == aiFile.Length + expectedDelta;
                if (valid && reparses && after == actions.Count + 1 && hasFiraga && hasRet && jumpPlusOne && entryNotRepointed && deltaOk) stopLocalOk++;
                else fails.Add($"{id}: local-stop insert bad (valid={valid}/{validWhy} reparse={reparses}/{why} count={after}/{actions.Count + 1} firaga={hasFiraga} ret={hasRet} jump+1={jumpPlusOne} entrySame={entryNotRepointed} delta={rb.Length - aiFile.Length}/{expectedDelta})");
            }
            catch (Exception ex) { fails.Add($"{id}: local-stop insert threw {ex.GetType().Name}: {ex.Message}"); }
        }

        // 8) Self-buff: append guarded writeChrProperty(self, Haste 0x38, 1) on the exact onTurn hook.
        if (hasOnTurnHook)
        {
            try
            {
                buffTested++;
                byte[] buffed = AiAutomation.AddSelfBuff(script, 0x38, random: false, k: 0, onTurnHook.WorkerIndex, onTurnHook.EntrypointIndex);
                if (ValidateRebuiltAllowingBaselineUnknowns(buffed, script, aiFile.Length, out _) && buffed.Length > aiFile.Length && Reparses(buffed, script, script.Workers.Count, out _)) buffOk++;
                else fails.Add($"{id}: self-buff not clean");
            }
            catch (Exception ex) { fails.Add($"{id}: self-buff threw {ex.GetType().Name}: {ex.Message}"); }

            // 8a) YUNALESCA NulAll: one guarded block using the real Character NulAll spell (0x312D).
            try
            {
                nulAllTested++;
                const ushort RealNulAll = 0x312D;
                byte[] nulAll = AiAutomation.AddQueuedAbilityWithGuard(
                    script, RealNulAll, AiAutomation.BuildAlwaysGuard(),
                    onTurnHook.WorkerIndex, onTurnHook.EntrypointIndex);
                bool valid = ValidateRebuiltAllowingBaselineUnknowns(nulAll, script, aiFile.Length, out string validWhy);
                bool reparses = Reparses(nulAll, script, script.Workers.Count, out string why);
                AiScriptFile rr = AiScript_File.Read(nulAll);
                int appendedAt = script.ScriptStart + script.CodeLength;
                int addedRealNulAll = AiAutomation.DetectCommandActions(rr).Count(a =>
                    a.CallOffset >= appendedAt
                    && a.CommandOperand == RealNulAll);
                if (valid && reparses && addedRealNulAll == 1) nulAllOk++;
                else fails.Add($"{id}: NulAll command bad (valid={valid}/{validWhy} reparse={reparses}/{why} addedRealNulAll={addedRealNulAll}/1)");
            }
            catch (Exception ex) { fails.Add($"{id}: NulAll command threw {ex.GetType().Name}: {ex.Message}"); }

            // 8b) Shred-style target recipes: build a command whose target is selected at runtime by
            //     findMatchingChr rather than a fixed literal actor. This is NOT a combo flag; it is the human
            //     "pick one living frontline target" / "pick lowest-HP living frontline target" authoring primitive.
            try
            {
                foreach (AiTargetRecipe recipe in new[]
                {
                    new AiTargetRecipe(AiTargetRecipeKind.FindAliveFrontlineAny, 0),
                    new AiTargetRecipe(AiTargetRecipeKind.FindAliveFrontlineLowestHp, 0),
                })
                {
                    targetRecipeTested++;
                    byte[] added = AiAutomation.AddAbility(script, Firaga, random: false, k: 0,
                        onTurnHook.WorkerIndex, onTurnHook.EntrypointIndex, recipe);
                    bool valid = ValidateRebuiltAllowingBaselineUnknowns(added, script, aiFile.Length, out string validWhy);
                    bool reparses = Reparses(added, script, script.Workers.Count, out string why);
                    AiScriptFile rr = AiScript_File.Read(added);
                    int appendedAt = script.ScriptStart + script.CodeLength;
                    int addedFiraga = AiAutomation.DetectCommandActions(rr).Count(a =>
                        a.CallOffset >= appendedAt && a.CommandOperand == Firaga);
                    bool hasFindCall = rr.Instructions.Any(i =>
                        i.Offset >= appendedAt && i.Opcode == 0xB5 && i.Operand == 0x7010);
                    bool hasMatchingGroupSeed = recipe.Kind != AiTargetRecipeKind.FindAliveFrontlineLowestHp
                        || rr.Instructions.Any(i => i.Offset >= appendedAt && i.Opcode == 0xD8 && i.Operand == 0x7010);
                    if (valid && reparses && addedFiraga == 1 && hasFindCall && hasMatchingGroupSeed) targetRecipeOk++;
                    else fails.Add($"{id}: target recipe {recipe.Kind} bad (valid={valid}/{validWhy} reparse={reparses}/{why} addedFiraga={addedFiraga}/1 findCall={hasFindCall} seed={hasMatchingGroupSeed})");
                }
            }
            catch (Exception ex) { fails.Add($"{id}: target recipe threw {ex.GetType().Name}: {ex.Message}"); }

            // 8c) Computed target + linked Forbidden Rite: compute the target once, store it in temp0, then reuse it
            //     for performCommand and writeChrProperty. This is the structural fix for "same target as the
            //     linked ability" when the ability target is not a literal actor.
            try
            {
                linkedComputedTargetTested++;
                int beforeBuffs = all.Count(a => a.Kind == AiActionKind.Buff);
                byte[] linked = AiAutomation.AddAbilityWithLinkedChrPropertyWrite(
                    script, Firaga, new AiTargetRecipe(AiTargetRecipeKind.FindAliveFrontlineAny, 0),
                    fieldId: 0x002F, value: 255, random: false, k: 0,
                    onTurnHook.WorkerIndex, onTurnHook.EntrypointIndex);
                bool valid = ValidateRebuiltAllowingBaselineUnknowns(linked, script, aiFile.Length, out string validWhy);
                bool reparses = Reparses(linked, script, script.Workers.Count, out string why);
                AiScriptFile rr = AiScript_File.Read(linked);
                int appendedAt = script.ScriptStart + script.CodeLength;
                int addedFiraga = AiAutomation.DetectCommandActions(rr).Count(a =>
                    a.CallOffset >= appendedAt && a.CommandOperand == Firaga);
                int afterBuffs = AiAutomation.DetectActions(rr).Count(a => a.Kind == AiActionKind.Buff);
                bool hasFindCall = rr.Instructions.Any(i => i.Offset >= appendedAt && i.Opcode == 0xB5 && i.Operand == 0x7010);
                bool storesTarget = rr.Instructions.Any(i => i.Offset >= appendedAt && i.Opcode == 0x59);
                int targetLoads = rr.Instructions.Count(i => i.Offset >= appendedAt && i.Opcode == 0x67);
                if (valid && reparses && addedFiraga == 1 && afterBuffs == beforeBuffs + 1 && hasFindCall && storesTarget && targetLoads >= 2) linkedComputedTargetOk++;
                else fails.Add($"{id}: linked computed target bad (valid={valid}/{validWhy} reparse={reparses}/{why} firaga={addedFiraga}/1 buffs={afterBuffs}/{beforeBuffs + 1} find={hasFindCall} popI0={storesTarget} pushI0={targetLoads})");
            }
            catch (Exception ex) { fails.Add($"{id}: linked computed target threw {ex.GetType().Name}: {ex.Message}"); }

            // 8d) Real editor regression: two grow saves in sequence, then validate the grown file as an assembler source.
            try
            {
                sequentialGrowTested++;
                byte[] protectAi = AiAutomation.AddSelfBuff(script, 0x31, random: false, k: 0, onTurnHook.WorkerIndex, onTurnHook.EntrypointIndex);
                byte[] monster1 = AiScript_File.SpliceAiFileIntoMonsterGrow(monster, protectAi);
                byte[]? ai1 = AiScript_File.SliceAiFileFromMonster(monster1);
                if (ai1 == null) { fails.Add($"{id}: sequential grow slice1 returned null"); }
                else
                {
                    AiScriptFile script1 = AiScript_File.Read(ai1);
                    if (!AiWorkerMapping.TryResolveCombatOnTurn(monster1, script1, out AiEventHook hook1, out string hook1Why))
                    {
                        fails.Add($"{id}: sequential grow could not re-resolve onTurn after first grow: {hook1Why}");
                    }
                    else
                    {
                        byte[] hasteAi = AiAutomation.AddSelfBuff(script1, 0x38, random: false, k: 0, hook1.WorkerIndex, hook1.EntrypointIndex);
                        byte[] monster2 = AiScript_File.SpliceAiFileIntoMonsterGrow(monster1, hasteAi);
                        byte[]? ai2 = AiScript_File.SliceAiFileFromMonster(monster2);
                        if (ai2 == null) { fails.Add($"{id}: sequential grow slice2 returned null"); }
                        else
                        {
                            AiScriptFile script2 = AiScript_File.Read(ai2);
                            bool assemblerAcceptsGrownSource = ValidateInstructionsAllowingBaselineUnknowns(script2, script2.Instructions, out string grownWhy);
                            IReadOnlyList<AiDetectedAction> grownActions = AiAutomation.DetectActions(script2);
                            bool hasProtect = grownActions.Any(a => a.Kind == AiActionKind.Buff && a.FieldId == 0x31 && a.FieldValue == 1);
                            bool hasHaste = grownActions.Any(a => a.Kind == AiActionKind.Buff && a.FieldId == 0x38 && a.FieldValue == 1);
                            bool rehook = AiWorkerMapping.TryResolveCombatOnTurn(monster2, script2, out AiEventHook hook2, out _)
                                && hook2.WorkerIndex == hook1.WorkerIndex
                                && hook2.EntrypointIndex == hook1.EntrypointIndex;
                            if (ValidateRebuiltAllowingBaselineUnknowns(hasteAi, script1, ai1.Length, out _) && assemblerAcceptsGrownSource && hasProtect && hasHaste && rehook)
                                sequentialGrowOk++;
                            else
                                fails.Add($"{id}: sequential grow bad (assembler={assemblerAcceptsGrownSource}/{grownWhy} protect={hasProtect} haste={hasHaste} rehook={rehook})");
                        }
                    }
                }
            }
            catch (Exception ex) { fails.Add($"{id}: sequential grow threw {ex.GetType().Name}: {ex.Message}"); }
        }

        // 9) Copy AI: splice this AiFile into the monster, slice it back out, re-parse — the copy/restore round-trip.
        try
        {
            copyTested++;
            byte[] reMon = AiScript_File.SpliceAiFileIntoMonsterGrow(monster, aiFile);
            byte[]? back = AiScript_File.SliceAiFileFromMonster(reMon);
            if (back == null) fails.Add($"{id}: copy slice-back returned null");
            else
            {
                var rr = AiScript_File.Read(back);
                if (rr.HasScript && rr.Instructions.Count == script.Instructions.Count) copyOk++;
                else fails.Add($"{id}: copy round-trip mismatch (instr {rr.Instructions.Count}/{script.Instructions.Count})");
            }
        }
        catch (Exception ex) { fails.Add($"{id}: copy threw {ex.GetType().Name}: {ex.Message}"); }
    }

    bool pass = withScript > 0
        && mappingTested > 0 && mappingResolved > 0
        && addTested > 0 && addOk == addTested && addRandomOk == addTested
        && removeTested > 0 && removeOk == removeTested
        && reorderTested > 0 && reorderOk == reorderTested
        && groupReorderTested > 0 && groupReorderOk == groupReorderTested
        && changeTested > 0 && changeOk == changeTested
        && dupTested > 0 && dupOk == dupTested
        && toggleTested > 0 && toggleOk == toggleTested
        && buffTested > 0 && buffOk == buffTested
        && nulAllTested > 0 && nulAllOk == nulAllTested
        && copyTested > 0 && copyOk == copyTested
        && removeFieldTested > 0 && removeFieldOk == removeFieldTested
        && chgTgtTested > 0 && chgTgtOk == chgTgtTested
        && insSecondTested > 0 && insSecondOk == insSecondTested
        && localChanceTested > 0 && localChanceOk == localChanceTested
        && linkedChangeTested > 0 && linkedChangeOk == linkedChangeTested
        && linkedSecondTested > 0 && linkedSecondOk == linkedSecondTested
        && linkedLocalChanceTested > 0 && linkedLocalChanceOk == linkedLocalChanceTested
        && targetRecipeTested > 0 && targetRecipeOk == targetRecipeTested
        && linkedComputedTargetTested > 0 && linkedComputedTargetOk == linkedComputedTargetTested
        && sequentialGrowTested > 0 && sequentialGrowOk == sequentialGrowTested
        && stopHookTested > 0 && stopHookOk == stopHookTested
        && stopLocalTested > 0 && stopLocalOk == stopLocalTested
        && fails.Count == 0;

    var sb = new StringBuilder();
    sb.AppendLine("=== AiScriptLab --ai3 gate (1-click automations: WorkerFile onTurn hook + detect + stack-neutral remove) ===");
    sb.AppendLine($"monsters w/ AiFile  : {total}   with script: {withScript}");
    sb.AppendLine($"rebuild-safe scripts: {withScript - rebuildSafetyBlocked}/{withScript}   blocked by safety: {rebuildSafetyBlocked}");
    sb.AppendLine($"onTurn mapping ok   : {mappingResolved}/{mappingTested}   (WorkerFile CombatHandler.onTurn -> worker/entrypoint)");
    sb.AppendLine($"add-ability ok      : {addOk}/{addTested}   rng-guard ok: {addRandomOk}/{addTested}   template-missing: {addTemplateMissing}   (copies selected action: target + perform/force preserved, command changed, save/reload visible)");
    sb.AppendLine($"detect actions      : {detectActions} command(s) across {detectScripts} script(s)   removable: {removableActions}");
    sb.AppendLine($"detect buff/stat    : {buffActions} buff(s) ({buffRemovable} removable) + {statActions} stat(s) ({statRemovable} removable)   (#6: writeChrProperty 7018 + setStatField 70AB in the action list)");
    sb.AppendLine($"remove buff/stat ok : {removeFieldOk}/{removeFieldTested}   (generic stack-neutral removal shrinks by exactly the dropped run, re-parses clean)");
    sb.AppendLine($"remove ok           : {removeOk}/{removeTested}   blocked-by-validator: {removeBlocked}   (Rebuild shrinks by exactly the dropped triplet, re-parses clean)");
    sb.AppendLine($"reorder ok          : {reorderOk}/{reorderTested}   (move down past neighbour: length-preserving, re-parses, same operand multiset)");
    sb.AppendLine($"group reorder ok    : {groupReorderOk}/{groupReorderTested}   (multiselect: move 2 consecutive removable actions as one block, same operand multiset)");
    sb.AppendLine($"change ability ok   : {changeOk}/{changeTested}   (rewrite command slot to Firaga: length-preserving, re-parses, slot reads 0x3049)");
    sb.AppendLine($"change target ok    : {chgTgtOk}/{chgTgtTested}   (literal-target commands: {litTargetActions})   (#8: rewrite target slot to self 0xFFF3, length-preserving, re-parses)");
    sb.AppendLine($"insert 2nd cmd ok   : {insSecondOk}/{insSecondTested}   (#9: insert Firaga after a command: grows by triplet, +1 action, re-parses)");
    sb.AppendLine($"local chance ok     : {localChanceOk}/{localChanceTested}   (Talvez local: guard+D7 around only the new command; entrypoint not repointed)");
    sb.AppendLine($"stop-after ok       : hook {stopHookOk}/{stopHookTested} · local {stopLocalOk}/{stopLocalTested}   (Pare aqui: guard failure continues old flow; action success ends on RET)");
    sb.AppendLine($"linked rite ok      : change {linkedChangeOk}/{linkedChangeTested} · 2nd {linkedSecondOk}/{linkedSecondTested} · chance {linkedLocalChanceOk}/{linkedLocalChanceTested}   (command followed by writeChrProperty target/status/value)");
    sb.AppendLine($"target recipe ok    : {targetRecipeOk}/{targetRecipeTested}   (Shred-style findMatchingChr targets: any living frontline + lowest-HP living frontline)");
    sb.AppendLine($"linked computed ok  : {linkedComputedTargetOk}/{linkedComputedTargetTested}   (computed target stored in temp0 and reused for command + writeChrProperty)");
    sb.AppendLine($"duplicate ok        : {dupOk}/{dupTested}   (copy triplet after itself: grows by triplet, +1 action, re-parses)");
    sb.AppendLine($"toggle force ok     : {toggleOk}/{toggleTested}   (flip 705A<->700B: length-preserving, re-parses, call slot flipped)");
    sb.AppendLine($"self-buff ok        : {buffOk}/{buffTested}   (append guarded writeChrProperty self Haste: validates + re-parses as a grow)");
    sb.AppendLine($"NulAll command ok   : {nulAllOk}/{nulAllTested}   (one guarded YUNALESCA block uses real Character NulAll 0x312D)");
    sb.AppendLine($"2x grow reload ok   : {sequentialGrowOk}/{sequentialGrowTested}   (Protect then Haste, re-slice, re-resolve onTurn, assembler accepts grown source)");
    sb.AppendLine($"copy ai ok          : {copyOk}/{copyTested}   (splice AiFile into monster, slice back, re-parse: copy/restore round-trip)");
    sb.AppendLine($"VERDICT             : {(pass ? "PASS" : "FAIL")}");
    if (fails.Count > 0) { sb.AppendLine("FAILS:"); foreach (var f in fails.Take(25)) sb.AppendLine("  " + f); }
    Console.WriteLine(sb.ToString());

    if (jsonOut != null)
    {
        var verdict = new
        {
            pass, total, withScript, rebuildSafetyBlocked, mappingTested, mappingResolved, addTested, addOk, addRandomOk, addTemplateMissing,
            detectScripts, detectActions, removableActions,
            buffActions, statActions, buffRemovable, statRemovable, removeFieldTested, removeFieldOk,
            chgTgtTested, chgTgtOk, litTargetActions, insSecondTested, insSecondOk,
            localChanceTested, localChanceOk,
            stopHookTested, stopHookOk, stopLocalTested, stopLocalOk,
            linkedChangeTested, linkedChangeOk, linkedSecondTested, linkedSecondOk, linkedLocalChanceTested, linkedLocalChanceOk,
            targetRecipeTested, targetRecipeOk, linkedComputedTargetTested, linkedComputedTargetOk,
            removeTested, removeOk, removeBlocked,
            reorderTested, reorderOk, groupReorderTested, groupReorderOk, changeTested, changeOk,
            dupTested, dupOk, toggleTested, toggleOk, buffTested, buffOk,
            nulAllTested, nulAllOk,
            sequentialGrowTested, sequentialGrowOk, copyTested, copyOk, fails = fails.Take(50).ToArray(),
        };
        File.WriteAllText(jsonOut, JsonSerializer.Serialize(verdict, new JsonSerializerOptions { WriteIndented = true }));
    }
    return pass ? 0 : 1;
}

static int IndirectDispatchUnitsRt0(IReadOnlyList<string> files, string root, string? jsonOut)
{
    const string SeymourId = "m124";
    const string NatusId = "m126";
    const string GuadoId = "m141";

    if (!TryLoadIndirectUnits(SeymourId, out IReadOnlyList<AiIndirectDispatchUnit> seymourUnits, out AiScriptFile seymourScript, out string loadError))
    {
        Console.WriteLine("=== AiScriptLab --indirect-dispatch-units-rt0 ===");
        Console.WriteLine($"FAIL: {loadError}");
        return 1;
    }

    if (!TryLoadIndirectUnits(NatusId, out IReadOnlyList<AiIndirectDispatchUnit> natusUnits, out AiScriptFile natusScript, out loadError))
    {
        Console.WriteLine("=== AiScriptLab --indirect-dispatch-units-rt0 ===");
        Console.WriteLine($"FAIL: {loadError}");
        return 1;
    }

    if (!TryLoadIndirectUnits(GuadoId, out IReadOnlyList<AiIndirectDispatchUnit> guadoUnits, out _, out loadError))
    {
        Console.WriteLine("=== AiScriptLab --indirect-dispatch-units-rt0 ===");
        Console.WriteLine($"FAIL: {loadError}");
        return 1;
    }

    bool seymourHasCount = seymourUnits.Count >= 4;
    bool seymourHasCandidates = seymourUnits.Count > 0 && seymourUnits.All(u => u.CapabilityTier == AiIndirectDispatchCapabilityTier.AuthoringCandidate);
    bool seymourHasConsumers = seymourUnits.Count > 0 && seymourUnits.All(u => u.Consumers.Count >= 4);
    bool seymourHasTargetSlots = seymourUnits.Count > 0 && seymourUnits.All(u => u.EditableTargetSlots.Count >= 3);
    bool seymourHasNamedTargetSlots = seymourUnits.Any(u =>
        u.EditableTargetSlots.Any(slot => slot.VariableName.Equals("priv0014", StringComparison.OrdinalIgnoreCase))
        && u.EditableTargetSlots.Any(slot => slot.VariableName.Equals("priv0018", StringComparison.OrdinalIgnoreCase))
        && u.EditableTargetSlots.Any(slot => slot.VariableName.Equals("priv001C", StringComparison.OrdinalIgnoreCase)));
    bool seymourHasEditableComputedTarget = seymourUnits.Any(u =>
        u.EditableTargetSlots.Any(slot => slot.SourceKind == AiIndirectDispatchTargetSlotSourceKind.ComputedRecipe && slot.CanEdit));
    bool seymourHasEditableCopiedTarget = seymourUnits.Any(u =>
        u.EditableTargetSlots.Any(slot => slot.SourceKind == AiIndirectDispatchTargetSlotSourceKind.CopiedValue && slot.CanEdit));
    bool seymourHasCanonicalTargetSurface = seymourUnits.All(u => AiAutomation.BuildTargetSlotSurface(u.EditableTargetSlots, u.Consumers).Count == 3);
    bool seymourHasShapeFirstTargetSurface = seymourUnits.Any(u =>
    {
        IReadOnlyList<AiIndirectDispatchTargetSlotSurface> surface = AiAutomation.BuildTargetSlotSurface(u.EditableTargetSlots, u.Consumers);
        return surface.Any(slot =>
                   slot.SourceKind == AiIndirectDispatchTargetSlotSourceKind.ComputedRecipe
                   && !slot.DetailSummary.Contains("Historico detectado:", StringComparison.OrdinalIgnoreCase))
               && surface.Any(slot =>
                   slot.SourceKind == AiIndirectDispatchTargetSlotSourceKind.Literal
                   && slot.ValueSummary.Contains("Character#", StringComparison.OrdinalIgnoreCase)
                   && !slot.DetailSummary.Contains("O V2 destaca acima", StringComparison.OrdinalIgnoreCase));
    });
    bool seymourHasPhase0 = seymourUnits.Any(u =>
        u.GuardSummary.Contains("priv0020 == 0", StringComparison.OrdinalIgnoreCase)
        && u.NextStateSummary.Contains("1", StringComparison.OrdinalIgnoreCase)
        && u.PayloadWrites.Any(w => w.VariableName.Equals("priv0024", StringComparison.OrdinalIgnoreCase) && w.ValueSummary.Contains("0x3046", StringComparison.OrdinalIgnoreCase)));
    bool seymourHasPhase1 = seymourUnits.Any(u =>
        u.GuardSummary.Contains("priv0020 == 1", StringComparison.OrdinalIgnoreCase)
        && u.NextStateSummary.Contains("2", StringComparison.OrdinalIgnoreCase)
        && u.PayloadWrites.Any(w => w.VariableName.Equals("priv0024", StringComparison.OrdinalIgnoreCase) && w.ValueSummary.Contains("0x3047", StringComparison.OrdinalIgnoreCase)));
    bool seymourHasPhase2 = seymourUnits.Any(u =>
        u.GuardSummary.Contains("priv0020 == 2", StringComparison.OrdinalIgnoreCase)
        && u.NextStateSummary.Contains("3", StringComparison.OrdinalIgnoreCase)
        && u.PayloadWrites.Any(w => w.VariableName.Equals("priv0024", StringComparison.OrdinalIgnoreCase) && w.ValueSummary.Contains("0x3048", StringComparison.OrdinalIgnoreCase)));
    bool seymourHasPhase3 = seymourUnits.Any(u =>
        u.GuardSummary.Contains("priv0020 == 3", StringComparison.OrdinalIgnoreCase)
        && u.PayloadWrites.Any(w => w.VariableName.Equals("priv0024", StringComparison.OrdinalIgnoreCase) && w.ValueSummary.Contains("0x3045", StringComparison.OrdinalIgnoreCase)));
    AiIndirectDispatchOpeningFocus? seymourOpening = AiAutomation.PickIndirectDispatchOpeningSelection(seymourScript, seymourUnits);
    bool seymourOpeningVar = seymourOpening?.VariableName.Equals("priv0020", StringComparison.OrdinalIgnoreCase) == true;
    bool seymourOpeningRoutes = seymourOpening?.RouteSummary.Equals("rota 0/1/2/3 · guard de fase", StringComparison.OrdinalIgnoreCase) == true;
    bool seymourOpeningFocus = seymourOpening?.PreferredUnitIndex == 2
                               && seymourOpening?.PreferredRole == AiIndirectDispatchStructuralRole.Guard;

    bool natusHasCount = natusUnits.Count >= 4;
    bool natusHasCandidates = natusUnits.Count > 0 && natusUnits.All(u => u.CapabilityTier == AiIndirectDispatchCapabilityTier.AuthoringCandidate);
    bool natusHasConsumers = natusUnits.Count > 0 && natusUnits.All(u => u.Consumers.Count >= 4);
    bool natusHasTwoCommandSlots = natusUnits.Count > 0 && natusUnits.All(u => u.EditableSlots.Count == 2);
    bool natusHasTwoTargetVars = natusUnits.Any(u =>
        u.EditableTargetSlots.Any(slot => slot.VariableName.Equals("priv0004", StringComparison.OrdinalIgnoreCase))
        && u.EditableTargetSlots.Any(slot => slot.VariableName.Equals("priv0008", StringComparison.OrdinalIgnoreCase)));
    bool natusHasBattleVarGuard = natusUnits.Any(u => u.GuardSummary.Contains("battleVar000C", StringComparison.OrdinalIgnoreCase));
    bool natusHasEditableComputedTarget = natusUnits.Any(u =>
        u.EditableTargetSlots.Any(slot => slot.SourceKind == AiIndirectDispatchTargetSlotSourceKind.ComputedRecipe && slot.CanEdit));
    bool natusHasEditableCopiedTarget = natusUnits.Any(u =>
        u.EditableTargetSlots.Any(slot => slot.SourceKind == AiIndirectDispatchTargetSlotSourceKind.CopiedValue && slot.CanEdit));
    bool natusHasCanonicalTargetSurface = natusUnits.All(u => AiAutomation.BuildTargetSlotSurface(u.EditableTargetSlots, u.Consumers).Count == 2);
    bool natusHasShapeFirstTargetSurface = natusUnits.Any(u =>
    {
        IReadOnlyList<AiIndirectDispatchTargetSlotSurface> surface = AiAutomation.BuildTargetSlotSurface(u.EditableTargetSlots, u.Consumers);
        return surface.Any(slot =>
                   slot.VariableName.Equals("priv0004", StringComparison.OrdinalIgnoreCase)
                   && slot.DetailSummary.Contains("Historico secundario:", StringComparison.OrdinalIgnoreCase)
                   && !slot.DetailSummary.Contains("Historico detectado:", StringComparison.OrdinalIgnoreCase))
               && surface.Any(slot =>
                   slot.VariableName.Equals("priv0008", StringComparison.OrdinalIgnoreCase)
                   && slot.ValueSummary.Contains("Character#", StringComparison.OrdinalIgnoreCase)
                   && !slot.DetailSummary.Contains("O V2 destaca acima", StringComparison.OrdinalIgnoreCase));
    });
    bool natusHasPhase0 = natusUnits.Any(u =>
        u.GuardSummary.Contains("battleVar000C == 0", StringComparison.OrdinalIgnoreCase)
        && u.NextStateSummary.Contains("1", StringComparison.OrdinalIgnoreCase)
        && u.PayloadWrites.Any(w => w.VariableName.Equals("priv0010", StringComparison.OrdinalIgnoreCase) && w.ValueSummary.Contains("0x60AD", StringComparison.OrdinalIgnoreCase)));
    bool natusHasPhase1 = natusUnits.Any(u =>
        u.GuardSummary.Contains("battleVar000C == 1", StringComparison.OrdinalIgnoreCase)
        && u.NextStateSummary.Contains("2", StringComparison.OrdinalIgnoreCase)
        && u.PayloadWrites.Any(w => w.VariableName.Equals("priv0010", StringComparison.OrdinalIgnoreCase) && w.ValueSummary.Contains("0x60AF", StringComparison.OrdinalIgnoreCase)));
    bool natusHasPhase2 = natusUnits.Any(u =>
        u.GuardSummary.Contains("battleVar000C == 2", StringComparison.OrdinalIgnoreCase)
        && u.NextStateSummary.Contains("3", StringComparison.OrdinalIgnoreCase)
        && u.PayloadWrites.Any(w => w.VariableName.Equals("priv0010", StringComparison.OrdinalIgnoreCase) && w.ValueSummary.Contains("0x60B1", StringComparison.OrdinalIgnoreCase)));
    bool natusHasPhase3 = natusUnits.Any(u =>
        u.GuardSummary.Contains("battleVar000C == 3", StringComparison.OrdinalIgnoreCase)
        && u.NextStateSummary.Contains("0", StringComparison.OrdinalIgnoreCase)
        && u.PayloadWrites.Any(w => w.VariableName.Equals("priv0010", StringComparison.OrdinalIgnoreCase) && w.ValueSummary.Contains("0x60AB", StringComparison.OrdinalIgnoreCase)));
    AiIndirectDispatchOpeningFocus? natusOpening = AiAutomation.PickIndirectDispatchOpeningSelection(natusScript, natusUnits);
    bool natusOpeningVar = natusOpening?.VariableName.Equals("battleVar000C", StringComparison.OrdinalIgnoreCase) == true;
    bool natusOpeningRoutes = natusOpening?.RouteSummary.Equals("rota 0/1/2/3 · guard de fase", StringComparison.OrdinalIgnoreCase) == true;
    bool natusOpeningFocus = natusOpening?.PreferredUnitIndex == 2
                             && natusOpening?.PreferredRole == AiIndirectDispatchStructuralRole.Guard;
    bool guadoHasCount = guadoUnits.Count >= 2;
    bool guadoHasCandidates = guadoUnits.Count > 0 && guadoUnits.All(u => u.CapabilityTier == AiIndirectDispatchCapabilityTier.AuthoringCandidate);
    bool guadoHasPayloadPickerLabel = guadoUnits.Any(u => u.CapabilityLabel.Contains("payload picker", StringComparison.OrdinalIgnoreCase));
    bool guadoHasPayloadSlot = guadoUnits.Any(u =>
        u.PayloadWrites.Any(write => write.VariableName.Equals("priv001C", StringComparison.OrdinalIgnoreCase)));
    bool guadoHasTargetSlot = guadoUnits.Any(u =>
        u.EditableTargetSlots.Any(slot => slot.VariableName.Equals("priv0014", StringComparison.OrdinalIgnoreCase)));
    bool guadoHasComputedTargetEdit = guadoUnits.Any(u =>
        u.EditableTargetSlots.Any(slot =>
            slot.VariableName.Equals("priv0014", StringComparison.OrdinalIgnoreCase)
            && slot.SourceKind == AiIndirectDispatchTargetSlotSourceKind.ComputedRecipe
            && slot.CanEdit));
    bool guadoHasIndirectConsumer = guadoUnits.Any(u =>
        u.Consumers.Any(consumer =>
            consumer.CommandVariableName.Equals("priv001C", StringComparison.OrdinalIgnoreCase)
            && consumer.TargetVariableName.Equals("priv0014", StringComparison.OrdinalIgnoreCase)));
    bool guadoHasPayloadChoices = new[] { "0x3041", "0x3043", "0x6041" }
        .All(commandHex => guadoUnits.Any(u =>
            u.PayloadWrites.Any(write => write.ValueSummary.Contains(commandHex, StringComparison.OrdinalIgnoreCase))));
    bool seymourSupportsCopyToLiteralModeSwap = TryVerifyTargetModeSwap(
        SeymourId,
        targetVariableName: "priv001C",
        fromKind: AiIndirectDispatchTargetSlotSourceKind.CopiedValue,
        toKind: AiIndirectDispatchTargetSlotSourceKind.Literal,
        literalSourceVariableName: "priv0014",
        copySourceVariableName: null,
        out string seymourModeSwapError);
    bool natusSupportsLiteralToCopyModeSwap = TryVerifyTargetModeSwap(
        NatusId,
        targetVariableName: "priv0008",
        fromKind: AiIndirectDispatchTargetSlotSourceKind.Literal,
        toKind: AiIndirectDispatchTargetSlotSourceKind.CopiedValue,
        literalSourceVariableName: null,
        copySourceVariableName: "priv0004",
        out string natusModeSwapError);
    bool seymourSupportsLinkedRouteClone = TryVerifyLinkedRouteClone(
        SeymourId,
        sourceUnitId: "dispatch-row-1",
        out string seymourCloneError);
    bool natusSupportsLinkedRouteClone = TryVerifyLinkedRouteClone(
        NatusId,
        sourceUnitId: "dispatch-generic-0002-1",
        out string natusCloneError);

    Console.WriteLine("=== AiScriptLab --indirect-dispatch-units-rt0 ===");
    Console.WriteLine($"monster                : {SeymourId}");
    Console.WriteLine($"units found            : {seymourUnits.Count}");
    Console.WriteLine($"all candidate row-only : {(seymourHasCandidates ? "OK" : "MISSING")}");
    Console.WriteLine($"4 consumers each       : {(seymourHasConsumers ? "OK" : "MISSING")}");
    Console.WriteLine($"target slots surfaced  : {(seymourHasTargetSlots ? "OK" : "MISSING")}");
    Console.WriteLine($"target vars named      : {(seymourHasNamedTargetSlots ? "OK" : "MISSING")}");
    Console.WriteLine($"computed target edit   : {(seymourHasEditableComputedTarget ? "OK" : "MISSING")}");
    Console.WriteLine($"copied target edit     : {(seymourHasEditableCopiedTarget ? "OK" : "MISSING")}");
    Console.WriteLine($"target surface canon   : {(seymourHasCanonicalTargetSurface ? "OK" : "MISSING")}");
    Console.WriteLine($"target surface wording : {(seymourHasShapeFirstTargetSurface ? "OK" : "MISSING")}");
    Console.WriteLine($"mode swap copy->literal: {(seymourSupportsCopyToLiteralModeSwap ? "OK" : $"MISSING ({seymourModeSwapError})")}");
    Console.WriteLine($"linked route clone     : {(seymourSupportsLinkedRouteClone ? "OK" : $"MISSING ({seymourCloneError})")}");
    Console.WriteLine($"phase 0 row            : {(seymourHasPhase0 ? "OK" : "MISSING")}");
    Console.WriteLine($"phase 1 row            : {(seymourHasPhase1 ? "OK" : "MISSING")}");
    Console.WriteLine($"phase 2 row            : {(seymourHasPhase2 ? "OK" : "MISSING")}");
    Console.WriteLine($"phase 3 row            : {(seymourHasPhase3 ? "OK" : "MISSING")}");
    Console.WriteLine($"opening selector       : {(seymourOpeningVar ? "OK" : $"MISSING ({seymourOpening?.VariableName ?? "null"})")}");
    Console.WriteLine($"opening route summary  : {(seymourOpeningRoutes ? "OK" : $"MISSING ({seymourOpening?.RouteSummary ?? "null"})")}");
    Console.WriteLine($"opening focus route    : {(seymourOpeningFocus ? "OK" : $"MISSING ({seymourOpening?.PreferredUnitIndex.ToString() ?? "null"})")}");
    Console.WriteLine($"monster                : {NatusId}");
    Console.WriteLine($"units found            : {natusUnits.Count}");
    Console.WriteLine($"all candidate row-only : {(natusHasCandidates ? "OK" : "MISSING")}");
    Console.WriteLine($"4 consumers each       : {(natusHasConsumers ? "OK" : "MISSING")}");
    Console.WriteLine($"2 command slots/rota   : {(natusHasTwoCommandSlots ? "OK" : "MISSING")}");
    Console.WriteLine($"target vars named      : {(natusHasTwoTargetVars ? "OK" : "MISSING")}");
    Console.WriteLine($"guard var recognized   : {(natusHasBattleVarGuard ? "OK" : "MISSING")}");
    Console.WriteLine($"computed target edit   : {(natusHasEditableComputedTarget ? "OK" : "MISSING")}");
    Console.WriteLine($"copied target edit     : {(natusHasEditableCopiedTarget ? "OK" : "MISSING")}");
    Console.WriteLine($"target surface canon   : {(natusHasCanonicalTargetSurface ? "OK" : "MISSING")}");
    Console.WriteLine($"target surface wording : {(natusHasShapeFirstTargetSurface ? "OK" : "MISSING")}");
    Console.WriteLine($"mode swap literal->copy: {(natusSupportsLiteralToCopyModeSwap ? "OK" : $"MISSING ({natusModeSwapError})")}");
    Console.WriteLine($"linked route clone     : {(natusSupportsLinkedRouteClone ? "OK" : $"MISSING ({natusCloneError})")}");
    Console.WriteLine($"phase 0 row            : {(natusHasPhase0 ? "OK" : "MISSING")}");
    Console.WriteLine($"phase 1 row            : {(natusHasPhase1 ? "OK" : "MISSING")}");
    Console.WriteLine($"phase 2 row            : {(natusHasPhase2 ? "OK" : "MISSING")}");
    Console.WriteLine($"phase 3 row            : {(natusHasPhase3 ? "OK" : "MISSING")}");
    Console.WriteLine($"opening selector       : {(natusOpeningVar ? "OK" : $"MISSING ({natusOpening?.VariableName ?? "null"})")}");
    Console.WriteLine($"opening route summary  : {(natusOpeningRoutes ? "OK" : $"MISSING ({natusOpening?.RouteSummary ?? "null"})")}");
    Console.WriteLine($"opening focus route    : {(natusOpeningFocus ? "OK" : $"MISSING ({natusOpening?.PreferredUnitIndex.ToString() ?? "null"})")}");
    Console.WriteLine($"monster                : {GuadoId}");
    Console.WriteLine($"units found            : {guadoUnits.Count}");
    Console.WriteLine($"payload picker units   : {(guadoHasCount ? "OK" : "MISSING")}");
    Console.WriteLine($"all candidate row-only : {(guadoHasCandidates ? "OK" : "MISSING")}");
    Console.WriteLine($"payload picker label   : {(guadoHasPayloadPickerLabel ? "OK" : "MISSING")}");
    Console.WriteLine($"payload slot surfaced  : {(guadoHasPayloadSlot ? "OK" : "MISSING")}");
    Console.WriteLine($"target slot surfaced   : {(guadoHasTargetSlot ? "OK" : "MISSING")}");
    Console.WriteLine($"computed target edit   : {(guadoHasComputedTargetEdit ? "OK" : "MISSING")}");
    Console.WriteLine($"indirect consumer pair : {(guadoHasIndirectConsumer ? "OK" : "MISSING")}");
    Console.WriteLine($"payload choices seen   : {(guadoHasPayloadChoices ? "OK" : "MISSING")}");

    bool pass = seymourHasCount && seymourHasCandidates && seymourHasConsumers && seymourHasTargetSlots
        && seymourHasNamedTargetSlots && seymourHasEditableComputedTarget && seymourHasEditableCopiedTarget
        && seymourHasCanonicalTargetSurface && seymourHasShapeFirstTargetSurface && seymourSupportsCopyToLiteralModeSwap
        && seymourSupportsLinkedRouteClone
        && seymourHasPhase0 && seymourHasPhase1 && seymourHasPhase2 && seymourHasPhase3
        && seymourOpeningVar && seymourOpeningRoutes && seymourOpeningFocus
        && natusHasCount && natusHasCandidates && natusHasConsumers && natusHasTwoCommandSlots
        && natusHasTwoTargetVars && natusHasBattleVarGuard && natusHasEditableComputedTarget
        && natusHasEditableCopiedTarget && natusHasCanonicalTargetSurface && natusHasShapeFirstTargetSurface && natusSupportsLiteralToCopyModeSwap
        && natusSupportsLinkedRouteClone
        && natusHasPhase0 && natusHasPhase1 && natusHasPhase2 && natusHasPhase3
        && natusOpeningVar && natusOpeningRoutes && natusOpeningFocus
        && guadoHasCount && guadoHasCandidates && guadoHasPayloadPickerLabel
        && guadoHasPayloadSlot && guadoHasTargetSlot && guadoHasComputedTargetEdit
        && guadoHasIndirectConsumer && guadoHasPayloadChoices;

    if (!string.IsNullOrWhiteSpace(jsonOut))
    {
        string? dir = Path.GetDirectoryName(jsonOut);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(jsonOut, JsonSerializer.Serialize(new
        {
            generatedAtUtc = DateTime.UtcNow.ToString("O"),
            root,
            pass,
            monsters = new[]
            {
                BuildMonsterSummary(SeymourId, seymourUnits),
                BuildMonsterSummary(NatusId, natusUnits),
                BuildMonsterSummary(GuadoId, guadoUnits),
            },
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"json={jsonOut}");
    }

    Console.WriteLine(pass ? "VERDICT: PASS" : "VERDICT: FAIL");
    return pass ? 0 : 1;

    bool TryLoadIndirectUnits(string monsterId, out IReadOnlyList<AiIndirectDispatchUnit> units, out AiScriptFile script, out string error)
    {
        units = Array.Empty<AiIndirectDispatchUnit>();
        script = null!;

        string? path = files.FirstOrDefault(p => string.Equals(Path.GetFileNameWithoutExtension(p), monsterId, StringComparison.OrdinalIgnoreCase));
        if (path == null)
        {
            error = $"{monsterId}.bin not found under {root}";
            return false;
        }

        byte[] monster = File.ReadAllBytes(path);
        byte[]? aiFile = AiScript_File.SliceAiFileFromMonster(monster);
        if (aiFile == null)
        {
            error = $"{monsterId} has no AiFile slice";
            return false;
        }

        try { script = AiScript_File.Read(aiFile); }
        catch (Exception ex)
        {
            error = $"{monsterId}: read threw {ex.GetType().Name}: {ex.Message}";
            return false;
        }

        try { units = AiAutomation.DetectIndirectDispatchUnits(monster, script); }
        catch (Exception ex)
        {
            error = $"{monsterId}: DetectIndirectDispatchUnits threw {ex.GetType().Name}: {ex.Message}";
            return false;
        }

        error = string.Empty;
        return true;
    }

    bool TryVerifyTargetModeSwap(
        string monsterId,
        string targetVariableName,
        AiIndirectDispatchTargetSlotSourceKind fromKind,
        AiIndirectDispatchTargetSlotSourceKind toKind,
        string? literalSourceVariableName,
        string? copySourceVariableName,
        out string error)
    {
        error = string.Empty;

        string? path = files.FirstOrDefault(p => string.Equals(Path.GetFileNameWithoutExtension(p), monsterId, StringComparison.OrdinalIgnoreCase));
        if (path == null)
        {
            error = $"{monsterId}.bin missing";
            return false;
        }

        byte[] monster = File.ReadAllBytes(path);
        byte[]? aiFile = AiScript_File.SliceAiFileFromMonster(monster);
        if (aiFile == null)
        {
            error = $"{monsterId} sem AiFile";
            return false;
        }

        AiScriptFile script = AiScript_File.Read(aiFile);
        AiIndirectDispatchUnit unit = AiAutomation.DetectIndirectDispatchUnits(monster, script)
            .OrderBy(candidate => candidate.UnitIndex)
            .FirstOrDefault(candidate => candidate.EditableTargetSlots.Any(slot =>
                slot.VariableName.Equals(targetVariableName, StringComparison.OrdinalIgnoreCase)
                && slot.SourceKind == fromKind
                && slot.CanEdit));
        if (unit == null)
        {
            error = $"{monsterId} sem slot {targetVariableName} {fromKind}";
            return false;
        }

        AiIndirectDispatchEditableTargetSlot slot = unit.EditableTargetSlots.First(candidate =>
            candidate.VariableName.Equals(targetVariableName, StringComparison.OrdinalIgnoreCase)
            && candidate.SourceKind == fromKind
            && candidate.CanEdit);
        AiIndirectDispatchEditableOperand operand = slot.EditableOperands.FirstOrDefault();
        if (operand == null)
        {
            error = $"{monsterId} slot {targetVariableName} sem operando editavel";
            return false;
        }

        ushort newValue;
        if (toKind == AiIndirectDispatchTargetSlotSourceKind.Literal)
        {
            AiIndirectDispatchEditableTargetSlot? literalSource = unit.EditableTargetSlots.FirstOrDefault(candidate =>
                candidate.SourceKind == AiIndirectDispatchTargetSlotSourceKind.Literal
                && (string.IsNullOrWhiteSpace(literalSourceVariableName)
                    || candidate.VariableName.Equals(literalSourceVariableName, StringComparison.OrdinalIgnoreCase)));
            newValue = literalSource?.CurrentValue
                       ?? AiTargetNames.Standard.First().Operand;
        }
        else if (toKind == AiIndirectDispatchTargetSlotSourceKind.CopiedValue)
        {
            AiIndirectDispatchEditableTargetSlot? copySource = unit.EditableTargetSlots.FirstOrDefault(candidate =>
                !candidate.VariableName.Equals(targetVariableName, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrWhiteSpace(copySourceVariableName)
                    || candidate.VariableName.Equals(copySourceVariableName, StringComparison.OrdinalIgnoreCase)));
            if (copySource == null)
            {
                error = $"{monsterId} sem var de copia para {targetVariableName}";
                return false;
            }

            newValue = copySource.VariableIndex;
        }
        else
        {
            newValue = 0;
        }

        byte targetOpcode = toKind switch
        {
            AiIndirectDispatchTargetSlotSourceKind.Literal => 0xAE,
            AiIndirectDispatchTargetSlotSourceKind.CopiedValue => 0x9F,
            _ => operand.Opcode,
        };

        var request = new AiIndirectDispatchEditRequest(
            unit.UnitId,
            new[]
            {
                new AiIndirectDispatchValueEdit(
                    operand.RoleKey,
                    operand.RoleLabel,
                    operand.InstructionOffset,
                    operand.CurrentValue,
                    newValue,
                    operand.Opcode,
                    targetOpcode),
            });

        if (!AiIndirectDispatchRowOnlyEditor.TryApplyEdits(script, request, out AiIndirectDispatchEditResult? result, out string applyError)
            || result == null)
        {
            error = $"apply: {applyError}";
            return false;
        }

        byte[] editedMonster = AiScript_File.SpliceAiFileIntoMonster(monster, result.EditedAiFileBytes);
        byte[]? editedAiFile = AiScript_File.SliceAiFileFromMonster(editedMonster);
        if (editedAiFile == null)
        {
            error = "splice invalido";
            return false;
        }

        AiScriptFile editedScript = AiScript_File.Read(editedAiFile);
        AiIndirectDispatchUnit editedUnit = AiAutomation.DetectIndirectDispatchUnits(editedMonster, editedScript)
            .First(candidate => candidate.UnitIndex == unit.UnitIndex);
        AiIndirectDispatchEditableTargetSlot editedSlot = editedUnit.EditableTargetSlots.FirstOrDefault(candidate =>
            candidate.VariableName.Equals(targetVariableName, StringComparison.OrdinalIgnoreCase)
            && candidate.SourceInstructionOffset == slot.SourceInstructionOffset
            && candidate.CanEdit);
        if (editedSlot == null)
        {
            error = "slot nao reencontrado no readback";
            return false;
        }

        if (editedSlot.SourceKind != toKind)
        {
            error = $"readback kind {editedSlot.SourceKind}";
            return false;
        }

        if (editedSlot.CurrentValue != newValue)
        {
            error = $"readback value 0x{editedSlot.CurrentValue:X4}";
            return false;
        }

        return true;
    }

    bool TryVerifyLinkedRouteClone(
        string monsterId,
        string sourceUnitId,
        out string error)
    {
        error = string.Empty;

        string? path = files.FirstOrDefault(p => string.Equals(Path.GetFileNameWithoutExtension(p), monsterId, StringComparison.OrdinalIgnoreCase));
        if (path == null)
        {
            error = $"{monsterId}.bin missing";
            return false;
        }

        byte[] monster = File.ReadAllBytes(path);
        byte[]? aiFile = AiScript_File.SliceAiFileFromMonster(monster);
        if (aiFile == null)
        {
            error = $"{monsterId} has no AiFile slice";
            return false;
        }

        AiScriptFile script = AiScript_File.Read(aiFile);
        IReadOnlyList<AiIndirectDispatchUnit> beforeUnits = AiAutomation.DetectIndirectDispatchUnits(monster, script);
        AiIndirectDispatchUnit? sourceUnit = beforeUnits.FirstOrDefault(unit =>
            unit.UnitId.Equals(sourceUnitId, StringComparison.OrdinalIgnoreCase));
        if (sourceUnit == null)
        {
            error = $"{monsterId}: source unit {sourceUnitId} missing";
            return false;
        }

        if (!sourceUnit.CurrentNextStateValue.HasValue)
        {
            error = $"{monsterId}: source unit {sourceUnitId} has no next-state";
            return false;
        }

        if (!AiAutomation.TryCloneAndChainIndirectDispatchRoute(
                script,
                new AiIndirectDispatchRouteCloneRequest(sourceUnitId),
                out AiIndirectDispatchRouteCloneResult? cloneResult,
                out error)
            || cloneResult == null)
        {
            return false;
        }

        byte[] spliced = AiScript_File.SpliceAiFileIntoMonsterGrow(monster, cloneResult.EditedAiFileBytes);
        byte[]? roundTripAi = AiScript_File.SliceAiFileFromMonster(spliced);
        if (roundTripAi == null)
        {
            error = $"{monsterId}: grow splice lost AiFile";
            return false;
        }

        AiScriptFile clonedScript = AiScript_File.Read(roundTripAi);
        IReadOnlyList<AiIndirectDispatchUnit> afterUnits = AiAutomation.DetectIndirectDispatchUnits(spliced, clonedScript);
        AiIndirectDispatchUnit? clonedUnit = afterUnits.FirstOrDefault(unit =>
            unit.UnitId.Equals(cloneResult.NewUnitId, StringComparison.OrdinalIgnoreCase));
        AiIndirectDispatchUnit? updatedSourceUnit = afterUnits.FirstOrDefault(unit =>
            unit.UnitId.Equals(sourceUnitId, StringComparison.OrdinalIgnoreCase));
        if (clonedUnit == null || updatedSourceUnit == null)
        {
            error = $"{monsterId}: readback units missing source or clone";
            return false;
        }

        bool sourceRerouted = updatedSourceUnit.CurrentNextStateValue == cloneResult.NewRouteIndex;
        bool cloneKeepsOldNextState = clonedUnit.CurrentNextStateValue == sourceUnit.CurrentNextStateValue;
        bool cloneExpandedCount = afterUnits.Count == beforeUnits.Count + 1;
        bool clonePayloadMatches = sourceUnit.PayloadWrites
            .Where(write => !write.RoleSummary.Contains("next state", StringComparison.OrdinalIgnoreCase))
            .Select(write => $"{write.RoleSummary}:{write.ValueSummary}")
            .SequenceEqual(
                clonedUnit.PayloadWrites
                    .Where(write => !write.RoleSummary.Contains("next state", StringComparison.OrdinalIgnoreCase))
                    .Select(write => $"{write.RoleSummary}:{write.ValueSummary}"));

        if (!sourceRerouted || !cloneKeepsOldNextState || !cloneExpandedCount || !clonePayloadMatches)
        {
            error =
                $"{monsterId}: clone invariants failed " +
                $"(count={cloneExpandedCount}, reroute={sourceRerouted}, clone-next={cloneKeepsOldNextState}, payload={clonePayloadMatches})";
            return false;
        }

        return true;
    }

    static object BuildMonsterSummary(string monsterId, IReadOnlyList<AiIndirectDispatchUnit> units) => new
    {
        monster = monsterId,
        unitCount = units.Count,
        units = units.Select(unit => new
        {
            unit.UnitId,
            unit.UnitIndex,
            unit.HookKind,
            unit.GuardSummary,
            unit.NextStateSummary,
            tier = unit.CapabilityTier.ToString(),
            unit.CapabilityLabel,
            payload = unit.PayloadWrites.Select(write => new
            {
                write.VariableIndex,
                write.VariableName,
                write.RoleSummary,
                write.ValueSummary,
                offset = $"0x{write.Offset:X4}",
            }).ToArray(),
            consumers = unit.Consumers.Select(consumer => new
            {
                consumer.Label,
                consumer.CommandVariableName,
                consumer.TargetVariableName,
                callOffset = consumer.CallOffset >= 0 ? $"0x{consumer.CallOffset:X4}" : "missing",
            }).ToArray(),
            unit.CompanionEffects,
            unit.OffsetSummary,
            unit.WarningSummary,
        }).ToArray(),
    };
}

static int FluxNativeThresholdWriterRt0(IReadOnlyList<string> files, string root, string? jsonOut)
{
    Console.WriteLine("=== AiScriptLab --flux-native-threshold-writer-rt0 ===");

    string? path = files.FirstOrDefault(candidate =>
        string.Equals(Path.GetFileNameWithoutExtension(candidate), "m142", StringComparison.OrdinalIgnoreCase));
    if (path == null)
    {
        Console.WriteLine("m142 load               : FAIL (file not found)");
        return 1;
    }

    byte[] monster = File.ReadAllBytes(path);
    byte[]? aiFile = AiScript_File.SliceAiFileFromMonster(monster);
    if (aiFile == null)
    {
        Console.WriteLine("m142 ai slice           : FAIL");
        return 1;
    }

    AiScriptFile script;
    try
    {
        script = AiScript_File.Read(aiFile);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"m142 parse              : FAIL ({ex.GetType().Name}: {ex.Message})");
        return 1;
    }

    if (!AiFluxNativeThresholdWriter.TryBuildDescriptors(script, out IReadOnlyList<AiFluxNativeThresholdDescriptor> baseline, out string detectError))
    {
        Console.WriteLine($"m142 descriptors        : FAIL ({detectError})");
        return 1;
    }

    AiFluxNativeThresholdDescriptor protect = baseline.First(descriptor =>
        descriptor.VariableName.Equals("priv0018", StringComparison.OrdinalIgnoreCase));
    AiFluxNativeThresholdDescriptor reflect = baseline.First(descriptor =>
        descriptor.VariableName.Equals("priv001C", StringComparison.OrdinalIgnoreCase));

    bool baselineOk =
        protect.CurrentDenominator == 4
        && protect.CurrentNumerator == 3
        && reflect.CurrentDenominator == 2
        && reflect.CurrentNumerator == 1;
    Console.WriteLine($"baseline 75/50 shape    : {(baselineOk ? "OK" : "MISMATCH")}");

    var protectRequest = new AiFluxNativeThresholdPatchRequest(
        protect.VariableName,
        protect.DenominatorInstructionOffset,
        protect.CurrentDenominator,
        5,
        protect.NumeratorInstructionOffset,
        protect.CurrentNumerator,
        4);

    bool protectPatchOk =
        AiFluxNativeThresholdWriter.TryApplyPatch(script, protectRequest, out AiFluxNativeThresholdEditResult? protectResult, out string protectError)
        && protectResult != null;
    Console.WriteLine($"protect raw patch       : {(protectPatchOk ? "OK" : $"FAIL ({protectError})")}");
    if (!protectPatchOk || protectResult == null)
        return 1;

    bool protectValid = AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(
        protectResult.EditedAiFileBytes,
        script,
        aiFile.Length,
        out _,
        out string protectValidWhy);
    Console.WriteLine($"protect validate        : {(protectValid ? "OK" : $"FAIL ({protectValidWhy})")}");
    if (!protectValid)
        return 1;

    AiScriptFile protectScript = AiScript_File.Read(protectResult.EditedAiFileBytes);
    bool protectRereadOk =
        AiFluxNativeThresholdWriter.TryBuildDescriptor(protectScript, "priv0018", out AiFluxNativeThresholdDescriptor? protectAfter, out string protectAfterError)
        && protectAfter != null
        && protectAfter.CurrentDenominator == 5
        && protectAfter.CurrentNumerator == 4;
    Console.WriteLine($"protect reread          : {(protectRereadOk ? "OK" : $"FAIL ({protectAfterError})")}");
    if (!protectRereadOk || protectAfter == null)
        return 1;

    if (!AiFluxNativeThresholdWriter.TryBuildDescriptor(protectScript, "priv001C", out AiFluxNativeThresholdDescriptor? reflectOnPatched, out string reflectOnPatchedError)
        || reflectOnPatched == null)
    {
        Console.WriteLine($"reflect reread base     : FAIL ({reflectOnPatchedError})");
        return 1;
    }

    var reflectRequest = new AiFluxNativeThresholdPatchRequest(
        reflectOnPatched.VariableName,
        reflectOnPatched.DenominatorInstructionOffset,
        reflectOnPatched.CurrentDenominator,
        4,
        reflectOnPatched.NumeratorInstructionOffset,
        reflectOnPatched.CurrentNumerator,
        null);

    bool reflectPatchOk =
        AiFluxNativeThresholdWriter.TryApplyPatch(protectScript, reflectRequest, out AiFluxNativeThresholdEditResult? reflectResult, out string reflectError)
        && reflectResult != null;
    Console.WriteLine($"reflect raw patch       : {(reflectPatchOk ? "OK" : $"FAIL ({reflectError})")}");
    if (!reflectPatchOk || reflectResult == null)
        return 1;

    bool reflectValid = AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(
        reflectResult.EditedAiFileBytes,
        protectScript,
        protectResult.EditedAiFileBytes.Length,
        out _,
        out string reflectValidWhy);
    Console.WriteLine($"reflect validate        : {(reflectValid ? "OK" : $"FAIL ({reflectValidWhy})")}");
    if (!reflectValid)
        return 1;

    AiScriptFile combinedScript = AiScript_File.Read(reflectResult.EditedAiFileBytes);
    AiFluxNativeThresholdDescriptor? combinedProtect = null;
    AiFluxNativeThresholdDescriptor? combinedReflect = null;
    string combinedProtectError = string.Empty;
    string combinedReflectError = string.Empty;

    bool combinedProtectOk =
        AiFluxNativeThresholdWriter.TryBuildDescriptor(
            combinedScript,
            "priv0018",
            out combinedProtect,
            out combinedProtectError)
        && combinedProtect != null
        && combinedProtect.CurrentDenominator == 5
        && combinedProtect.CurrentNumerator == 4;

    bool combinedReflectOk =
        AiFluxNativeThresholdWriter.TryBuildDescriptor(
            combinedScript,
            "priv001C",
            out combinedReflect,
            out combinedReflectError)
        && combinedReflect != null
        && combinedReflect.CurrentDenominator == 4
        && combinedReflect.CurrentNumerator == 1;

    bool combinedRereadOk = combinedProtectOk && combinedReflectOk;
    Console.WriteLine($"combined reread         : {(combinedRereadOk ? "OK" : $"FAIL ({combinedProtectError} | {combinedReflectError})")}");
    if (!combinedRereadOk || combinedProtect == null || combinedReflect == null)
        return 1;

    byte[] splicedMonster = AiScript_File.SpliceAiFileIntoMonster(monster, reflectResult.EditedAiFileBytes);
    byte[]? slicedBack = AiScript_File.SliceAiFileFromMonster(splicedMonster);
    bool spliceRoundTripOk = slicedBack != null && slicedBack.SequenceEqual(reflectResult.EditedAiFileBytes);
    Console.WriteLine($"splice round-trip       : {(spliceRoundTripOk ? "OK" : "FAIL")}");
    if (!spliceRoundTripOk)
        return 1;

    var restoreProtectRequest = new AiFluxNativeThresholdPatchRequest(
        combinedProtect.VariableName,
        combinedProtect.DenominatorInstructionOffset,
        combinedProtect.CurrentDenominator,
        4,
        combinedProtect.NumeratorInstructionOffset,
        combinedProtect.CurrentNumerator,
        3);

    if (!AiFluxNativeThresholdWriter.TryApplyPatch(
            combinedScript,
            restoreProtectRequest,
            out AiFluxNativeThresholdEditResult? restoreProtectResult,
            out string restoreProtectError)
        || restoreProtectResult == null)
    {
        Console.WriteLine($"restore protect         : FAIL ({restoreProtectError})");
        return 1;
    }

    AiScriptFile restoreProtectScript = AiScript_File.Read(restoreProtectResult.EditedAiFileBytes);
    if (!AiFluxNativeThresholdWriter.TryBuildDescriptor(
            restoreProtectScript,
            "priv001C",
            out AiFluxNativeThresholdDescriptor? restoreReflectDescriptor,
            out string restoreReflectDescriptorError)
        || restoreReflectDescriptor == null)
    {
        Console.WriteLine($"restore reflect base    : FAIL ({restoreReflectDescriptorError})");
        return 1;
    }

    var restoreReflectRequest = new AiFluxNativeThresholdPatchRequest(
        restoreReflectDescriptor.VariableName,
        restoreReflectDescriptor.DenominatorInstructionOffset,
        restoreReflectDescriptor.CurrentDenominator,
        2,
        restoreReflectDescriptor.NumeratorInstructionOffset,
        restoreReflectDescriptor.CurrentNumerator,
        null);

    if (!AiFluxNativeThresholdWriter.TryApplyPatch(
            restoreProtectScript,
            restoreReflectRequest,
            out AiFluxNativeThresholdEditResult? restoreReflectResult,
            out string restoreReflectError)
        || restoreReflectResult == null)
    {
        Console.WriteLine($"restore reflect         : FAIL ({restoreReflectError})");
        return 1;
    }

    bool restoreValid = AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(
        restoreReflectResult.EditedAiFileBytes,
        restoreProtectScript,
        restoreProtectResult.EditedAiFileBytes.Length,
        out _,
        out string restoreValidWhy);
    Console.WriteLine($"restore validate        : {(restoreValid ? "OK" : $"FAIL ({restoreValidWhy})")}");
    if (!restoreValid)
        return 1;

    bool restoreByteIdentity = restoreReflectResult.EditedAiFileBytes.SequenceEqual(aiFile);
    Console.WriteLine($"restore byte-identity   : {(restoreByteIdentity ? "OK" : "FAIL")}");

    bool pass = baselineOk
        && protectPatchOk
        && protectValid
        && protectRereadOk
        && reflectPatchOk
        && reflectValid
        && combinedRereadOk
        && spliceRoundTripOk
        && restoreValid
        && restoreByteIdentity;

    if (!string.IsNullOrWhiteSpace(jsonOut))
    {
        string? dir = Path.GetDirectoryName(jsonOut);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(jsonOut, JsonSerializer.Serialize(new
        {
            generatedAtUtc = DateTime.UtcNow.ToString("O"),
            root,
            monster = "m142",
            pass,
            baseline = new
            {
                protect = new { protect.CurrentDenominator, protect.CurrentNumerator, protect.RoleLabel },
                reflect = new { reflect.CurrentDenominator, reflect.CurrentNumerator, reflect.RoleLabel },
            },
            patched = new
            {
                protect = new { denominator = 5, numerator = 4, changedBytes = protectResult.ChangedBytes.Count },
                reflect = new { denominator = 4, numerator = 1, changedBytes = reflectResult.ChangedBytes.Count },
            },
            restored = new
            {
                validate = restoreValid,
                byteIdentity = restoreByteIdentity,
            },
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"json={jsonOut}");
    }

    Console.WriteLine(pass ? "VERDICT: PASS" : "VERDICT: FAIL");
    return pass ? 0 : 1;
}

static int ComplexFamilyMatrixRt0(IReadOnlyList<string> files, string root, string? jsonOut)
{
    Console.WriteLine("=== AiScriptLab --complex-family-matrix-rt0 ===");

    var expectations = new[]
    {
        (MonsterId: "m124", ExpectedCount: 4, ExpectedTier: (AiIndirectDispatchCapabilityTier?)AiIndirectDispatchCapabilityTier.AuthoringCandidate, RequiredUnitFragments: new[] { "dispatch-row-0", "dispatch-row-1", "dispatch-row-2", "dispatch-row-3" }),
        (MonsterId: "m238", ExpectedCount: 5, ExpectedTier: (AiIndirectDispatchCapabilityTier?)AiIndirectDispatchCapabilityTier.PreviewReadOnly, RequiredUnitFragments: new[] { "preview-round-landing", "preview-round-crawl", "preview-round-sonic-boom", "preview-round-aeon-punish", "preview-round-finisher" }),
        (MonsterId: "m211", ExpectedCount: 2, ExpectedTier: (AiIndirectDispatchCapabilityTier?)AiIndirectDispatchCapabilityTier.PreviewReadOnly, RequiredUnitFragments: new[] { "preview-encounter-open", "preview-encounter-appear-disable" }),
        (MonsterId: "m212", ExpectedCount: 0, ExpectedTier: (AiIndirectDispatchCapabilityTier?)null, RequiredUnitFragments: Array.Empty<string>()),
        (MonsterId: "m218", ExpectedCount: 0, ExpectedTier: (AiIndirectDispatchCapabilityTier?)null, RequiredUnitFragments: Array.Empty<string>()),
        (MonsterId: "m223", ExpectedCount: 4, ExpectedTier: (AiIndirectDispatchCapabilityTier?)AiIndirectDispatchCapabilityTier.PreviewReadOnly, RequiredUnitFragments: new[] { "preview-tonberry-direct-pressure", "preview-tonberry-position-cycle", "preview-tonberry-counter-window", "preview-tonberry-camera-routing" }),
        (MonsterId: "m224", ExpectedCount: 4, ExpectedTier: (AiIndirectDispatchCapabilityTier?)AiIndirectDispatchCapabilityTier.PreviewReadOnly, RequiredUnitFragments: new[] { "preview-tonberry-direct-pressure", "preview-tonberry-position-cycle", "preview-tonberry-counter-window", "preview-tonberry-camera-routing" }),
        (MonsterId: "m226", ExpectedCount: 0, ExpectedTier: (AiIndirectDispatchCapabilityTier?)null, RequiredUnitFragments: Array.Empty<string>()),
        (MonsterId: "m227", ExpectedCount: 0, ExpectedTier: (AiIndirectDispatchCapabilityTier?)null, RequiredUnitFragments: Array.Empty<string>()),
        (MonsterId: "m230", ExpectedCount: 0, ExpectedTier: (AiIndirectDispatchCapabilityTier?)null, RequiredUnitFragments: Array.Empty<string>()),
        (MonsterId: "m277", ExpectedCount: 0, ExpectedTier: (AiIndirectDispatchCapabilityTier?)null, RequiredUnitFragments: Array.Empty<string>()),
        (MonsterId: "m279", ExpectedCount: 0, ExpectedTier: (AiIndirectDispatchCapabilityTier?)null, RequiredUnitFragments: Array.Empty<string>()),
        (MonsterId: "m280", ExpectedCount: 0, ExpectedTier: (AiIndirectDispatchCapabilityTier?)null, RequiredUnitFragments: Array.Empty<string>()),
        (MonsterId: "m142", ExpectedCount: 4, ExpectedTier: (AiIndirectDispatchCapabilityTier?)AiIndirectDispatchCapabilityTier.AuthoringCandidate, RequiredUnitFragments: new[] { "preview-flux-lance-cycle", "preview-flux-dispel-cross", "preview-flux-self-buff", "preview-flux-anti-aeon" }),
        (MonsterId: "m143", ExpectedCount: 2, ExpectedTier: (AiIndirectDispatchCapabilityTier?)AiIndirectDispatchCapabilityTier.AuthoringCandidate, RequiredUnitFragments: new[] { "preview-mortiorchis-body-handoff", "preview-mortiorchis-absorption" }),
        (MonsterId: "m141", ExpectedCount: 2, ExpectedTier: (AiIndirectDispatchCapabilityTier?)AiIndirectDispatchCapabilityTier.AuthoringCandidate, RequiredUnitFragments: new[] { "dispatch-support-0006-0005-0", "dispatch-support-0006-0005-1" }),
        (MonsterId: "m213", ExpectedCount: 0, ExpectedTier: (AiIndirectDispatchCapabilityTier?)null, RequiredUnitFragments: Array.Empty<string>()),
        (MonsterId: "m222", ExpectedCount: 0, ExpectedTier: (AiIndirectDispatchCapabilityTier?)null, RequiredUnitFragments: Array.Empty<string>()),
        (MonsterId: "m131", ExpectedCount: 3, ExpectedTier: (AiIndirectDispatchCapabilityTier?)AiIndirectDispatchCapabilityTier.AuthoringCandidate, RequiredUnitFragments: new[] { "preview-omnis-elemental", "preview-omnis-dispel-break", "preview-omnis-ultima-break" }),
        (MonsterId: "m127", ExpectedCount: 2, ExpectedTier: (AiIndirectDispatchCapabilityTier?)AiIndirectDispatchCapabilityTier.AuthoringCandidate, RequiredUnitFragments: new[] { "preview-mortibody-support-switch", "preview-mortibody-absorption" }),
        (MonsterId: "m106", ExpectedCount: 1, ExpectedTier: (AiIndirectDispatchCapabilityTier?)AiIndirectDispatchCapabilityTier.PreviewReadOnly, RequiredUnitFragments: new[] { "preview-reactive-" }),
        (MonsterId: "m118", ExpectedCount: 1, ExpectedTier: (AiIndirectDispatchCapabilityTier?)AiIndirectDispatchCapabilityTier.AuthoringCandidate, RequiredUnitFragments: new[] { "preview-reactive-" }),
        (MonsterId: "m150", ExpectedCount: 1, ExpectedTier: (AiIndirectDispatchCapabilityTier?)AiIndirectDispatchCapabilityTier.AuthoringCandidate, RequiredUnitFragments: new[] { "preview-reactive-" }),
        (MonsterId: "m154", ExpectedCount: 1, ExpectedTier: (AiIndirectDispatchCapabilityTier?)AiIndirectDispatchCapabilityTier.AuthoringCandidate, RequiredUnitFragments: new[] { "preview-reactive-" }),
        (MonsterId: "m281", ExpectedCount: 0, ExpectedTier: (AiIndirectDispatchCapabilityTier?)null, RequiredUnitFragments: Array.Empty<string>()),
        (MonsterId: "m125", ExpectedCount: 1, ExpectedTier: (AiIndirectDispatchCapabilityTier?)AiIndirectDispatchCapabilityTier.AuthoringCandidate, RequiredUnitFragments: new[] { "dispatch-support-0008-0007-0" }),
        (MonsterId: "m209", ExpectedCount: 0, ExpectedTier: (AiIndirectDispatchCapabilityTier?)null, RequiredUnitFragments: Array.Empty<string>()),
        (MonsterId: "m288", ExpectedCount: 0, ExpectedTier: (AiIndirectDispatchCapabilityTier?)null, RequiredUnitFragments: Array.Empty<string>()),
        (MonsterId: "m289", ExpectedCount: 0, ExpectedTier: (AiIndirectDispatchCapabilityTier?)null, RequiredUnitFragments: Array.Empty<string>()),
        (MonsterId: "m290", ExpectedCount: 1, ExpectedTier: (AiIndirectDispatchCapabilityTier?)AiIndirectDispatchCapabilityTier.AuthoringCandidate, RequiredUnitFragments: new[] { "preview-mortibody-support-switch" }),
    };

    var summaries = new List<object>();
    var failures = new List<string>();

    foreach (var expectation in expectations)
    {
        if (!TryLoadPromotedUnitsRt0(files, root, expectation.MonsterId, out AiScriptFile? _, out IReadOnlyList<AiIndirectDispatchUnit> units, out string error))
        {
            failures.Add($"{expectation.MonsterId}: {error}");
            summaries.Add(new
            {
                monsterId = expectation.MonsterId,
                loadError = error,
                unitCount = 0,
                capabilityTiers = Array.Empty<string>(),
                capabilityLabels = Array.Empty<string>(),
                unitIds = Array.Empty<string>(),
            });
            Console.WriteLine($"{expectation.MonsterId} load               : FAIL ({error})");
            continue;
        }

        bool countOk = units.Count == expectation.ExpectedCount;
        bool tierOk = expectation.ExpectedTier == null
            ? units.Count == 0
            : units.All(unit => unit.CapabilityTier == expectation.ExpectedTier.Value);
        bool fragmentsOk = expectation.RequiredUnitFragments.All(fragment =>
            units.Any(unit => unit.UnitId.Contains(fragment, StringComparison.OrdinalIgnoreCase)));

        string tierSummary = BuildTierSummary(units);
        string labelSummary = BuildCapabilityLabelSummary(units);
        string unitIdSummary = units.Count == 0 ? "(none)" : string.Join(", ", units.Select(unit => unit.UnitId));

        Console.WriteLine($"{expectation.MonsterId} count              : {(countOk ? "OK" : $"FAIL ({units.Count} != {expectation.ExpectedCount})")}");
        Console.WriteLine($"{expectation.MonsterId} tiers              : {(tierOk ? "OK" : $"FAIL ({tierSummary})")}");
        Console.WriteLine($"{expectation.MonsterId} labels             : {(string.IsNullOrWhiteSpace(labelSummary) ? "(none)" : labelSummary)}");
        Console.WriteLine($"{expectation.MonsterId} unit ids           : {unitIdSummary}");

        if (!countOk)
            failures.Add($"{expectation.MonsterId}: count drifted ({units.Count} != {expectation.ExpectedCount})");
        if (!tierOk)
            failures.Add($"{expectation.MonsterId}: capability tier drifted ({tierSummary})");
        if (!fragmentsOk)
            failures.Add($"{expectation.MonsterId}: required unit fragments missing ({string.Join(", ", expectation.RequiredUnitFragments)})");

        summaries.Add(new
        {
            monsterId = expectation.MonsterId,
            expectedCount = expectation.ExpectedCount,
            unitCount = units.Count,
            capabilityTiers = units.Select(unit => unit.CapabilityTier.ToString()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            capabilityLabels = units.Select(unit => unit.CapabilityLabel).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            unitIds = units.Select(unit => unit.UnitId).ToArray(),
        });
    }

    bool pass = failures.Count == 0;
    Console.WriteLine(pass ? "VERDICT: PASS" : "VERDICT: FAIL");
    foreach (string failure in failures.Take(40))
        Console.WriteLine($"  FAIL: {failure}");

    WriteJsonIfRequestedRt0(jsonOut, new
    {
        generatedAtUtc = DateTime.UtcNow.ToString("O"),
        root,
        pass,
        summaries,
        failures = failures.ToArray(),
    });

    return pass ? 0 : 1;
}

static int CloneFingerprintRt0(IReadOnlyList<string> files, string root, string? jsonOut)
{
    Console.WriteLine("=== AiScriptLab --clone-fingerprint-rt0 ===");

    string repoRoot = FindRepoRootShared();
    string cloneMatrixPath = Path.Combine(repoRoot, "docs", "ai", "PHASE_MANAGER_ADVANCED_CLONE_VALIDATION_MATRIX_2026-07-02.md");
    var failures = new List<string>();

    bool docVocabularyOk = false;
    try
    {
        string cloneMatrix = File.ReadAllText(cloneMatrixPath);
        docVocabularyOk =
            cloneMatrix.Contains("Structural fingerprint", StringComparison.Ordinal)
            && cloneMatrix.Contains("Unit fingerprint", StringComparison.Ordinal)
            && cloneMatrix.Contains("Próxima ação", StringComparison.Ordinal);
        if (!docVocabularyOk)
            failures.Add("Clone validation matrix still lacks the fingerprint vocabulary (Structural fingerprint / Unit fingerprint / Próxima ação).");
    }
    catch (Exception ex)
    {
        failures.Add($"Clone validation matrix read failed: {ex.Message}");
    }

    var expectations = new[]
    {
        new { MonsterId = "m125", ExpectedCount = 1 },
        new { MonsterId = "m141", ExpectedCount = 2 },
        new { MonsterId = "m142", ExpectedCount = 4 },
        new { MonsterId = "m143", ExpectedCount = 2 },
        new { MonsterId = "m127", ExpectedCount = 2 },
        new { MonsterId = "m131", ExpectedCount = 3 },
        new { MonsterId = "m118", ExpectedCount = 1 },
        new { MonsterId = "m150", ExpectedCount = 1 },
        new { MonsterId = "m154", ExpectedCount = 1 },
    };

    var summaries = new List<object>();
    var reactiveStructuralFingerprints = new List<string>();

    foreach (var expectation in expectations)
    {
        if (!TryLoadPromotedUnitsRt0(files, root, expectation.MonsterId, out AiScriptFile? _, out IReadOnlyList<AiIndirectDispatchUnit> units, out string error))
        {
            failures.Add($"{expectation.MonsterId}: {error}");
            summaries.Add(new
            {
                monsterId = expectation.MonsterId,
                loadError = error,
                unitCount = 0,
                fingerprints = Array.Empty<object>(),
            });
            Console.WriteLine($"{expectation.MonsterId} load          : FAIL ({error})");
            continue;
        }

        bool countOk = units.Count == expectation.ExpectedCount;
        bool fingerprintsOk = units.All(unit => BuildStructuralFingerprint(unit).Length > 0 && BuildUnitFingerprint(unit).Length > 0);
        if (!countOk)
            failures.Add($"{expectation.MonsterId}: clone fingerprint coverage drifted ({units.Count} != {expectation.ExpectedCount}).");
        if (!fingerprintsOk)
            failures.Add($"{expectation.MonsterId}: at least one promoted unit failed to emit fingerprints.");

        if (expectation.MonsterId is "m118" or "m150" or "m154")
            reactiveStructuralFingerprints.AddRange(units.Select(BuildStructuralFingerprint));

        Console.WriteLine($"{expectation.MonsterId} count         : {(countOk ? "OK" : $"FAIL ({units.Count} != {expectation.ExpectedCount})")}");
        Console.WriteLine($"{expectation.MonsterId} fingerprint    : {(fingerprintsOk ? "OK" : "FAIL")}");

        summaries.Add(new
        {
            monsterId = expectation.MonsterId,
            unitCount = units.Count,
            fingerprints = units.Select(unit => new
            {
                unit.UnitId,
                unit.CapabilityTier,
                unit.CapabilityLabel,
                StructuralFingerprint = BuildStructuralFingerprint(unit),
                UnitFingerprint = BuildUnitFingerprint(unit),
            }).ToArray(),
        });
    }

    bool reactiveCloneOk = reactiveStructuralFingerprints.Count == 3
        && reactiveStructuralFingerprints.Distinct(StringComparer.Ordinal).Count() == 1;
    Console.WriteLine($"reactive trio clone    : {(reactiveCloneOk ? "OK" : "FAIL")}");
    if (!reactiveCloneOk)
        failures.Add("m118/m150/m154 no longer share one structural fingerprint in the mature reactive-sensor subgroup.");

    bool pass = failures.Count == 0;
    Console.WriteLine($"clone matrix doc       : {(docVocabularyOk ? "OK" : "FAIL")}");
    Console.WriteLine(pass ? "VERDICT: PASS" : "VERDICT: FAIL");
    foreach (string failure in failures.Take(20))
        Console.WriteLine($"  FAIL: {failure}");

    WriteJsonIfRequestedRt0(jsonOut, new
    {
        generatedAtUtc = DateTime.UtcNow.ToString("O"),
        root,
        pass,
        docVocabularyOk,
        reactiveCloneOk,
        summaries,
        failures = failures.ToArray(),
    });

    return pass ? 0 : 1;
}

static string BuildStructuralFingerprint(AiIndirectDispatchUnit unit)
{
    string payload = unit.PayloadWrites.Count == 0
        ? "-"
        : string.Join("|", unit.PayloadWrites
            .OrderBy(write => write.Offset)
            .Select(write => $"{write.VariableName}:{write.RoleSummary}:{write.ValueSummary}"));
    string consumers = unit.Consumers.Count == 0
        ? "-"
        : string.Join("|", unit.Consumers
            .OrderBy(consumer => consumer.CallOffset)
            .Select(consumer => $"{consumer.Label}:{consumer.CommandVariableName}:{consumer.TargetVariableName}"));
    string slots = unit.EditableSlots.Count == 0
        ? "-"
        : string.Join("|", unit.EditableSlots
            .OrderBy(slot => slot.PushInstructionOffset)
            .Select(slot => $"{slot.RoleKey}:{slot.CurrentValueResolved}"));
    string targets = unit.EditableTargetSlots.Count == 0
        ? "-"
        : string.Join("|", unit.EditableTargetSlots
            .OrderBy(slot => slot.SourceInstructionOffset)
            .Select(slot => $"{slot.RoleKey}:{slot.SourceKind}:{slot.CurrentValueResolved}"));
    string companions = unit.CompanionEffects.Count == 0
        ? "-"
        : string.Join("|", unit.CompanionEffects.OrderBy(static note => note, StringComparer.OrdinalIgnoreCase));

    return string.Join(" || ", new[]
    {
        unit.HookKind,
        unit.GuardSummary,
        unit.NextStateSummary,
        unit.CapabilityLabel,
        payload,
        consumers,
        slots,
        targets,
        companions,
        unit.EditableNextStateOffset.HasValue ? $"next:editable:{unit.CurrentNextStateValue?.ToString() ?? "-"}" : "next:-",
    });
}

static string BuildUnitFingerprint(AiIndirectDispatchUnit unit) =>
    $"{unit.UnitId} :: {unit.OffsetSummary} :: {unit.WarningSummary} :: {BuildStructuralFingerprint(unit)}";

static int BattleExplorerEncounterSurfacesRt0(IReadOnlyList<string> files, string root, string? jsonOut)
{
    Console.WriteLine("=== AiScriptLab --battleexplorer-encounter-surfaces-rt0 ===");

    string repoRoot = FindRepoRootShared();
    string dataModelPath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "BattleExplorer", "BattleExplorer_DataModel.cs");
    string controlPath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "BattleExplorer", "BattleExplorer_Control.axaml");
    var failures = new List<string>();

    bool dataModelOk = false;
    bool controlOk = false;
    bool companionStillOk = false;

    try
    {
        string dataModel = File.ReadAllText(dataModelPath);
        string control = File.ReadAllText(controlPath);

        dataModelOk =
            dataModel.Contains("OpenerWrites", StringComparison.Ordinal)
            && dataModel.Contains("OpenerSummary", StringComparison.Ordinal)
            && dataModel.Contains("OpenerFormationMapping", StringComparison.Ordinal)
            && dataModel.Contains("OpenerGuardrail", StringComparison.Ordinal)
            && dataModel.Contains("PopulateOpenerData(", StringComparison.Ordinal)
            && dataModel.Contains("BattleEncounterOpener_File.ReadFromBattleBin(", StringComparison.Ordinal);

        controlOk =
            control.Contains("Encounter opener / CTB seed", StringComparison.Ordinal)
            && control.Contains("{Binding OpenerSummary}", StringComparison.Ordinal)
            && control.Contains("{Binding OpenerFormationMapping}", StringComparison.Ordinal)
            && control.Contains("{Binding OpenerWrites}", StringComparison.Ordinal)
            && control.Contains("{Binding OpenerGuardrail}", StringComparison.Ordinal);

        companionStillOk =
            control.Contains("{Binding CompanionActivationSummary}", StringComparison.Ordinal)
            && control.Contains("{Binding CompanionActivationRows}", StringComparison.Ordinal)
            && dataModel.Contains("PopulateCompanionActivationData(", StringComparison.Ordinal)
            && dataModel.Contains("BattleCompanionActivation_File.ReadFromBattleBin(", StringComparison.Ordinal);
    }
    catch (Exception ex)
    {
        failures.Add($"BattleExplorer encounter surfaces source check failed: {ex.Message}");
    }

    Console.WriteLine($"battleexplorer opener  : {(dataModelOk ? "OK" : "FAIL")}");
    Console.WriteLine($"battleexplorer ui      : {(controlOk ? "OK" : "FAIL")}");
    Console.WriteLine($"companion continuity   : {(companionStillOk ? "OK" : "FAIL")}");

    if (!dataModelOk)
        failures.Add("BattleExplorer data-model still lacks the opener / CTB seed surface wiring.");
    if (!controlOk)
        failures.Add("BattleExplorer view still lacks the opener / CTB seed card.");
    if (!companionStillOk)
        failures.Add("BattleExplorer lost the existing companion activation lane while wiring encounter surfaces.");

    bool pass = failures.Count == 0;
    Console.WriteLine(pass ? "VERDICT: PASS" : "VERDICT: FAIL");
    foreach (string failure in failures.Take(20))
        Console.WriteLine($"  FAIL: {failure}");

    WriteJsonIfRequestedRt0(jsonOut, new
    {
        generatedAtUtc = DateTime.UtcNow.ToString("O"),
        root,
        pass,
        dataModelOk,
        controlOk,
        companionStillOk,
        failures = failures.ToArray(),
    });

    return pass ? 0 : 1;
}

static int AnimaFamilyRt0(IReadOnlyList<string> files, string root, string? jsonOut)
{
    Console.WriteLine("=== AiScriptLab --anima-family-rt0 ===");

    var failures = new List<string>();
    var payloads = new[] { "Oblivion", "Boost", "Pain" };
    string repoRoot = FindRepoRootShared();
    string animaDataModelPath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiEditor_DataModel.AdvancedAnima.cs");
    string advancedWindowPath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiPhaseRotationAdvanced_Window.axaml");

    bool sourceOk = true;
    try
    {
        string animaDataModel = File.ReadAllText(animaDataModelPath);
        string advancedWindow = File.ReadAllText(advancedWindowPath);

        if (!animaDataModel.Contains("monsterNumber != 125", StringComparison.Ordinal)
            || !animaDataModel.Contains("dispatch-support-0008-0007-0", StringComparison.Ordinal)
            || !animaDataModel.Contains("HasAdvancedAnimaSurface", StringComparison.Ordinal)
            || !animaDataModel.Contains("Oblivion", StringComparison.Ordinal)
            || !animaDataModel.Contains("Boost", StringComparison.Ordinal)
            || !animaDataModel.Contains("Pain", StringComparison.Ordinal))
        {
            sourceOk = false;
            failures.Add("Advanced Anima data-model lane is missing the dedicated m125 narrow payload-picker surface.");
        }

        if (!advancedWindow.Contains("Anima payload picker (m125)", StringComparison.Ordinal)
            || !advancedWindow.Contains("AdvancedAnimaSummary", StringComparison.Ordinal)
            || !advancedWindow.Contains("AdvancedAnimaPayloadRows", StringComparison.Ordinal)
            || !advancedWindow.Contains("AdvancedAnimaPayloadLaunchContext", StringComparison.Ordinal))
        {
            sourceOk = false;
            failures.Add("Advanced phase window is missing the dedicated Anima payload-picker card.");
        }
    }
    catch (Exception ex)
    {
        sourceOk = false;
        failures.Add($"Anima source-integrity check failed: {ex.Message}");
    }

    if (!TryLoadPromotedUnitsRt0(files, root, "m125", out AiScriptFile? _, out IReadOnlyList<AiIndirectDispatchUnit> units, out string error))
    {
        failures.Add(error);
        Console.WriteLine($"m125 load              : FAIL ({error})");
        WriteJsonIfRequestedRt0(jsonOut, new
        {
            generatedAtUtc = DateTime.UtcNow.ToString("O"),
            root,
            pass = false,
            failures = failures.ToArray(),
        });
        Console.WriteLine("VERDICT: FAIL");
        return 1;
    }

    AiIndirectDispatchUnit? unit = units.FirstOrDefault(candidate =>
        candidate.UnitId.Equals("dispatch-support-0008-0007-0", StringComparison.OrdinalIgnoreCase))
        ?? units.FirstOrDefault(candidate =>
            payloads.All(payload => IndirectUnitContainsText(candidate, payload)));

    bool countOk = units.Count == 1;
    bool unitOk = unit != null;
    bool tierOk = unit?.CapabilityTier == AiIndirectDispatchCapabilityTier.AuthoringCandidate;
    bool payloadOk = unit != null && payloads.All(payload => IndirectUnitContainsText(unit, payload));
    bool editablePayloadOk = unit != null && unit.EditableSlots.Count == 3;
    bool targetOk = unit != null && AiAutomation.BuildTargetSlotSurface(unit.EditableTargetSlots, unit.Consumers).Count > 0;

    Console.WriteLine($"m125 count             : {(countOk ? "OK" : $"FAIL ({units.Count} != 1)")}");
    Console.WriteLine($"m125 source            : {(sourceOk ? "OK" : "FAIL")}");
    Console.WriteLine($"m125 unit id           : {(unitOk ? unit!.UnitId : "FAIL")}");
    Console.WriteLine($"m125 tier              : {(tierOk ? "OK" : BuildTierSummary(units))}");
    Console.WriteLine($"m125 payloads          : {(payloadOk ? "OK" : "FAIL")}");
    Console.WriteLine($"m125 editable payloads : {(editablePayloadOk ? "OK" : $"FAIL ({unit?.EditableSlots.Count ?? 0})")}");
    Console.WriteLine($"m125 target lane       : {(targetOk ? "OK" : "FAIL")}");

    if (!countOk) failures.Add($"m125 count drifted ({units.Count} != 1)");
    if (!unitOk) failures.Add("m125 dedicated unit dispatch-support-0008-0007-0 is missing.");
    if (!tierOk) failures.Add($"m125 no longer promotes as AuthoringCandidate ({BuildTierSummary(units)}).");
    if (!payloadOk) failures.Add("m125 payload picker lost one of Oblivion / Boost / Pain.");
    if (!editablePayloadOk) failures.Add("m125 payload picker no longer exposes the three row-only payload edits.");
    if (!targetOk) failures.Add("m125 payload picker lost its target slot surface.");

    bool pass = failures.Count == 0;
    Console.WriteLine(pass ? "VERDICT: PASS" : "VERDICT: FAIL");
    foreach (string failure in failures.Take(20))
        Console.WriteLine($"  FAIL: {failure}");

    WriteJsonIfRequestedRt0(jsonOut, new
    {
        generatedAtUtc = DateTime.UtcNow.ToString("O"),
        root,
        pass,
        sourceOk,
        unitCount = units.Count,
        unitId = unit?.UnitId,
        capabilityLabels = units.Select(candidate => candidate.CapabilityLabel).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        payloads = unit?.PayloadWrites.Select(write => write.ValueSummary).ToArray() ?? Array.Empty<string>(),
        failures = failures.ToArray(),
    });

    return pass ? 0 : 1;
}

static int HostCompanionFamilyRt0(IReadOnlyList<string> files, string root, string? jsonOut)
{
    Console.WriteLine("=== AiScriptLab --host-companion-family-rt0 ===");

    var failures = new List<string>();
    string repoRoot = FindRepoRootShared();
    string fluxFamilyPath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiEditor_DataModel.AdvancedFluxFamily.cs");
    string fluxThresholdPath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiEditor_DataModel.AdvancedFluxThresholds.cs");
    string advancedWindowPath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiPhaseRotationAdvanced_Window.axaml");

    bool sourceOk = true;
    try
    {
        string fluxFamily = File.ReadAllText(fluxFamilyPath);
        string fluxThreshold = File.ReadAllText(fluxThresholdPath);
        string advancedWindow = File.ReadAllText(advancedWindowPath);

        if (!fluxFamily.Contains("HasAdvancedFluxFamilySurface", StringComparison.Ordinal)
            || !fluxFamily.Contains("Host beat do Seymour Flux", StringComparison.Ordinal)
            || !fluxFamily.Contains("Companion / follow-up do Mortiorchis", StringComparison.Ordinal)
            || !fluxFamily.Contains("Thresholds nativos raw", StringComparison.Ordinal))
        {
            sourceOk = false;
            failures.Add("Flux family data-model lane is missing the dedicated host/companion summary blocks.");
        }

        if (!fluxThreshold.Contains("ApplyAdvancedFluxThresholdEdit(", StringComparison.Ordinal)
            || !fluxThreshold.Contains("TryBuildAdvancedFluxThresholdEvidence(", StringComparison.Ordinal))
        {
            sourceOk = false;
            failures.Add("Flux threshold writer lane lost the dedicated raw-threshold surface.");
        }

        if (!advancedWindow.Contains("Flux host / companion pair (m142)", StringComparison.Ordinal)
            || !advancedWindow.Contains("AdvancedFluxHostBeatSummary", StringComparison.Ordinal)
            || !advancedWindow.Contains("AdvancedFluxCompanionFollowUpSummary", StringComparison.Ordinal)
            || !advancedWindow.Contains("AdvancedFluxWriterScopeSummary", StringComparison.Ordinal))
        {
            sourceOk = false;
            failures.Add("Advanced phase window is missing the dedicated Flux host/companion section.");
        }
    }
    catch (Exception ex)
    {
        sourceOk = false;
        failures.Add($"Flux source-integrity check failed: {ex.Message}");
    }

    if (!TryLoadPromotedUnitsRt0(files, root, "m142", out AiScriptFile? script, out IReadOnlyList<AiIndirectDispatchUnit> units, out string error))
    {
        failures.Add(error);
        Console.WriteLine($"m142 load              : FAIL ({error})");
        WriteJsonIfRequestedRt0(jsonOut, new
        {
            generatedAtUtc = DateTime.UtcNow.ToString("O"),
            root,
            pass = false,
            failures = failures.ToArray(),
        });
        Console.WriteLine("VERDICT: FAIL");
        return 1;
    }

    string[] requiredUnitIds =
    {
        "preview-flux-lance-cycle",
        "preview-flux-dispel-cross",
        "preview-flux-self-buff",
        "preview-flux-anti-aeon",
    };

    bool countOk = units.Count == 4;
    bool idsOk = requiredUnitIds.All(required =>
        units.Any(unit => unit.UnitId.Contains(required, StringComparison.OrdinalIgnoreCase)));
    bool tiersOk = units.All(unit => unit.CapabilityTier == AiIndirectDispatchCapabilityTier.AuthoringCandidate);

    IReadOnlyList<AiFluxNativeThresholdDescriptor> descriptors = Array.Empty<AiFluxNativeThresholdDescriptor>();
    string descriptorError = script == null
        ? "m142 script unavailable for Flux descriptor build."
        : string.Empty;
    bool descriptorBuilt = script != null
        && AiFluxNativeThresholdWriter.TryBuildDescriptors(script, out descriptors, out descriptorError);
    bool descriptorOk = descriptorBuilt
        && descriptors.Count == 2
        && descriptors.Any(descriptor => descriptor.VariableName.Equals("priv0018", StringComparison.OrdinalIgnoreCase)
                                         && descriptor.RoleLabel.Contains("Protect", StringComparison.OrdinalIgnoreCase))
        && descriptors.Any(descriptor => descriptor.VariableName.Equals("priv001C", StringComparison.OrdinalIgnoreCase)
                                         && descriptor.RoleLabel.Contains("Reflect", StringComparison.OrdinalIgnoreCase));

    string descriptorSummary = descriptorOk
        ? string.Join(" | ", descriptors.Select(descriptor => $"{descriptor.VariableName}:{descriptor.RawThresholdSummary}"))
        : descriptorError;

    Console.WriteLine($"m142 source            : {(sourceOk ? "OK" : "FAIL")}");
    Console.WriteLine($"m142 count             : {(countOk ? "OK" : $"FAIL ({units.Count} != 4)")}");
    Console.WriteLine($"m142 unit ids          : {(idsOk ? "OK" : "FAIL")}");
    Console.WriteLine($"m142 tiers             : {(tiersOk ? "OK" : BuildTierSummary(units))}");
    Console.WriteLine($"m142 raw descriptors   : {(descriptorOk ? "OK" : $"FAIL ({descriptorSummary})")}");

    if (!countOk) failures.Add($"m142 count drifted ({units.Count} != 4)");
    if (!idsOk) failures.Add("m142 lost one of the four live Flux unit ids.");
    if (!tiersOk) failures.Add($"m142 no longer promotes the whole host/companion family ({BuildTierSummary(units)}).");
    if (!descriptorOk) failures.Add($"m142 raw-threshold descriptors drifted ({descriptorSummary}).");

    bool pass = failures.Count == 0;
    Console.WriteLine(pass ? "VERDICT: PASS" : "VERDICT: FAIL");
    foreach (string failure in failures.Take(20))
        Console.WriteLine($"  FAIL: {failure}");

    WriteJsonIfRequestedRt0(jsonOut, new
    {
        generatedAtUtc = DateTime.UtcNow.ToString("O"),
        root,
        pass,
        sourceOk,
        unitCount = units.Count,
        unitIds = units.Select(unit => unit.UnitId).ToArray(),
        capabilityLabels = units.Select(unit => unit.CapabilityLabel).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        descriptorSummary,
        failures = failures.ToArray(),
    });

    return pass ? 0 : 1;
}

static int ElementalClusterRt0(IReadOnlyList<string> files, string root, string? jsonOut)
{
    Console.WriteLine("=== AiScriptLab --elemental-cluster-rt0 ===");

    var failures = new List<string>();
    string repoRoot = FindRepoRootShared();
    string elementalPath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiEditor_DataModel.AdvancedElementalCluster.cs");
    string advancedWindowPath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiPhaseRotationAdvanced_Window.axaml");

    bool sourceOk = true;
    try
    {
        string elementalDataModel = File.ReadAllText(elementalPath);
        string advancedWindow = File.ReadAllText(advancedWindowPath);

        if (!elementalDataModel.Contains("monsterNumber != 131", StringComparison.Ordinal)
            || !elementalDataModel.Contains("preview-omnis-elemental", StringComparison.Ordinal)
            || !elementalDataModel.Contains("preview-omnis-dispel-break", StringComparison.Ordinal)
            || !elementalDataModel.Contains("preview-omnis-ultima-break", StringComparison.Ordinal)
            || !elementalDataModel.Contains("HasAdvancedElementalClusterSurface", StringComparison.Ordinal))
        {
            sourceOk = false;
            failures.Add("Elemental cluster data-model lane is missing the dedicated m131 family card.");
        }

        if (!advancedWindow.Contains("Elemental cluster (m131)", StringComparison.Ordinal)
            || !advancedWindow.Contains("AdvancedElementalClusterSummary", StringComparison.Ordinal)
            || !advancedWindow.Contains("AdvancedElementalClusterRows", StringComparison.Ordinal))
        {
            sourceOk = false;
            failures.Add("Advanced phase window is missing the dedicated elemental-cluster card.");
        }
    }
    catch (Exception ex)
    {
        sourceOk = false;
        failures.Add($"Elemental cluster source-integrity check failed: {ex.Message}");
    }

    if (!TryLoadPromotedUnitsRt0(files, root, "m131", out AiScriptFile? _, out IReadOnlyList<AiIndirectDispatchUnit> units, out string error))
    {
        failures.Add(error);
        Console.WriteLine($"m131 load              : FAIL ({error})");
        WriteJsonIfRequestedRt0(jsonOut, new
        {
            generatedAtUtc = DateTime.UtcNow.ToString("O"),
            root,
            pass = false,
            failures = failures.ToArray(),
        });
        Console.WriteLine("VERDICT: FAIL");
        return 1;
    }

    string[] requiredUnitIds =
    {
        "preview-omnis-elemental",
        "preview-omnis-dispel-break",
        "preview-omnis-ultima-break",
    };

    bool countOk = units.Count == 3;
    bool idsOk = requiredUnitIds.All(required =>
        units.Any(unit => unit.UnitId.Contains(required, StringComparison.OrdinalIgnoreCase)));
    bool tiersOk = units.All(unit => unit.CapabilityTier == AiIndirectDispatchCapabilityTier.AuthoringCandidate);
    bool launchableOk = units.Any(unit =>
        unit.EditableSlots.Count > 0
        || unit.EditableTargetSlots.Any(slot => slot.CanEdit)
        || unit.EditableNextStateOffset.HasValue);

    Console.WriteLine($"m131 source            : {(sourceOk ? "OK" : "FAIL")}");
    Console.WriteLine($"m131 count             : {(countOk ? "OK" : $"FAIL ({units.Count} != 3)")}");
    Console.WriteLine($"m131 unit ids          : {(idsOk ? "OK" : "FAIL")}");
    Console.WriteLine($"m131 tiers             : {(tiersOk ? "OK" : BuildTierSummary(units))}");
    Console.WriteLine($"m131 launch lane       : {(launchableOk ? "OK" : "FAIL")}");

    if (!countOk) failures.Add($"m131 count drifted ({units.Count} != 3)");
    if (!idsOk) failures.Add("m131 lost one of the three preview-omnis-* unit ids.");
    if (!tiersOk) failures.Add($"m131 no longer promotes as AuthoringCandidate ({BuildTierSummary(units)}).");
    if (!launchableOk) failures.Add("m131 no longer exposes any promoted narrow lane for the dedicated family card.");

    bool pass = failures.Count == 0;
    Console.WriteLine(pass ? "VERDICT: PASS" : "VERDICT: FAIL");
    foreach (string failure in failures.Take(20))
        Console.WriteLine($"  FAIL: {failure}");

    WriteJsonIfRequestedRt0(jsonOut, new
    {
        generatedAtUtc = DateTime.UtcNow.ToString("O"),
        root,
        pass,
        sourceOk,
        unitCount = units.Count,
        unitIds = units.Select(unit => unit.UnitId).ToArray(),
        capabilityLabels = units.Select(unit => unit.CapabilityLabel).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        failures = failures.ToArray(),
    });

    return pass ? 0 : 1;
}

static int SupportAccumulatorRt0(IReadOnlyList<string> files, string root, string? jsonOut)
{
    Console.WriteLine("=== AiScriptLab --support-accumulator-rt0 ===");

    var failures = new List<string>();
    string repoRoot = FindRepoRootShared();
    string supportPath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiEditor_DataModel.AdvancedSupportAccumulator.cs");
    string advancedWindowPath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiPhaseRotationAdvanced_Window.axaml");

    bool sourceOk = true;
    try
    {
        string supportDataModel = File.ReadAllText(supportPath);
        string advancedWindow = File.ReadAllText(advancedWindowPath);

        if (!supportDataModel.Contains("monsterNumber != 127", StringComparison.Ordinal)
            || !supportDataModel.Contains("preview-mortibody-support-switch", StringComparison.Ordinal)
            || !supportDataModel.Contains("preview-mortibody-absorption", StringComparison.Ordinal)
            || !supportDataModel.Contains("m290", StringComparison.Ordinal)
            || !supportDataModel.Contains("HasAdvancedSupportAccumulatorSurface", StringComparison.Ordinal))
        {
            sourceOk = false;
            failures.Add("Support accumulator data-model lane is missing the dedicated m127 family card / m290 guardrail.");
        }

        if (!advancedWindow.Contains("Support accumulator (m127)", StringComparison.Ordinal)
            || !advancedWindow.Contains("AdvancedSupportAccumulatorSummary", StringComparison.Ordinal)
            || !advancedWindow.Contains("AdvancedSupportAccumulatorRows", StringComparison.Ordinal))
        {
            sourceOk = false;
            failures.Add("Advanced phase window is missing the dedicated support-accumulator card.");
        }
    }
    catch (Exception ex)
    {
        sourceOk = false;
        failures.Add($"Support accumulator source-integrity check failed: {ex.Message}");
    }

    if (!TryLoadPromotedUnitsRt0(files, root, "m127", out AiScriptFile? _, out IReadOnlyList<AiIndirectDispatchUnit> units, out string error))
    {
        failures.Add(error);
        Console.WriteLine($"m127 load              : FAIL ({error})");
        WriteJsonIfRequestedRt0(jsonOut, new
        {
            generatedAtUtc = DateTime.UtcNow.ToString("O"),
            root,
            pass = false,
            failures = failures.ToArray(),
        });
        Console.WriteLine("VERDICT: FAIL");
        return 1;
    }

    AiIndirectDispatchUnit? supportSwitchUnit = units.FirstOrDefault(unit =>
        unit.UnitId.Contains("preview-mortibody-support-switch", StringComparison.OrdinalIgnoreCase));
    AiIndirectDispatchUnit? absorptionUnit = units.FirstOrDefault(unit =>
        unit.UnitId.Contains("preview-mortibody-absorption", StringComparison.OrdinalIgnoreCase));

    bool countOk = units.Count == 2;
    bool idsOk = supportSwitchUnit != null && absorptionUnit != null;
    bool tiersOk = units.All(unit => unit.CapabilityTier == AiIndirectDispatchCapabilityTier.AuthoringCandidate);
    bool launchableOk = supportSwitchUnit != null
        && (supportSwitchUnit.EditableSlots.Count > 0
            || supportSwitchUnit.EditableTargetSlots.Any(slot => slot.CanEdit)
            || supportSwitchUnit.EditableNextStateOffset.HasValue);

    Console.WriteLine($"m127 source            : {(sourceOk ? "OK" : "FAIL")}");
    Console.WriteLine($"m127 count             : {(countOk ? "OK" : $"FAIL ({units.Count} != 2)")}");
    Console.WriteLine($"m127 unit ids          : {(idsOk ? "OK" : "FAIL")}");
    Console.WriteLine($"m127 tiers             : {(tiersOk ? "OK" : BuildTierSummary(units))}");
    Console.WriteLine($"m127 launch lane       : {(launchableOk ? "OK" : "FAIL")}");

    if (!countOk) failures.Add($"m127 count drifted ({units.Count} != 2)");
    if (!idsOk) failures.Add("m127 lost one of the two preview-mortibody-* unit ids.");
    if (!tiersOk) failures.Add($"m127 no longer promotes as AuthoringCandidate ({BuildTierSummary(units)}).");
    if (!launchableOk) failures.Add("m127 support-switch no longer exposes a promoted narrow lane for the dedicated family card.");

    bool pass = failures.Count == 0;
    Console.WriteLine(pass ? "VERDICT: PASS" : "VERDICT: FAIL");
    foreach (string failure in failures.Take(20))
        Console.WriteLine($"  FAIL: {failure}");

    WriteJsonIfRequestedRt0(jsonOut, new
    {
        generatedAtUtc = DateTime.UtcNow.ToString("O"),
        root,
        pass,
        sourceOk,
        unitCount = units.Count,
        unitIds = units.Select(unit => unit.UnitId).ToArray(),
        capabilityLabels = units.Select(unit => unit.CapabilityLabel).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        failures = failures.ToArray(),
    });

    return pass ? 0 : 1;
}

static int ReactiveSensorFamilyRt0(IReadOnlyList<string> files, string root, string? jsonOut)
{
    Console.WriteLine("=== AiScriptLab --reactive-sensor-family-rt0 ===");

    var failures = new List<string>();
    string repoRoot = FindRepoRootShared();
    string reactivePath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiEditor_DataModel.AdvancedReactiveSensor.cs");
    string advancedWindowPath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiPhaseRotationAdvanced_Window.axaml");

    bool sourceOk = true;
    try
    {
        string reactiveDataModel = File.ReadAllText(reactivePath);
        string advancedWindow = File.ReadAllText(advancedWindowPath);

        if (!reactiveDataModel.Contains("monsterNumber is not (106 or 118 or 150 or 154)", StringComparison.Ordinal)
            || !reactiveDataModel.Contains("EditableNextStateOffset", StringComparison.Ordinal)
            || !reactiveDataModel.Contains("m106 continua", StringComparison.Ordinal)
            || !reactiveDataModel.Contains("HasAdvancedReactiveSensorSurface", StringComparison.Ordinal))
        {
            sourceOk = false;
            failures.Add("Reactive sensor data-model lane is missing the dedicated family card / m106 guardrail.");
        }

        if (!advancedWindow.Contains("Reactive sensor", StringComparison.Ordinal)
            || !advancedWindow.Contains("AdvancedReactiveSensorSummary", StringComparison.Ordinal)
            || !advancedWindow.Contains("AdvancedReactiveSensorRows", StringComparison.Ordinal))
        {
            sourceOk = false;
            failures.Add("Advanced phase window is missing the dedicated reactive-sensor card.");
        }
    }
    catch (Exception ex)
    {
        sourceOk = false;
        failures.Add($"Reactive sensor source-integrity check failed: {ex.Message}");
    }

    bool authoringOk = CheckReactiveSensorMonster(files, root, "m118", AiIndirectDispatchCapabilityTier.AuthoringCandidate, expectNextState: true, failures)
        && CheckReactiveSensorMonster(files, root, "m150", AiIndirectDispatchCapabilityTier.AuthoringCandidate, expectNextState: true, failures)
        && CheckReactiveSensorMonster(files, root, "m154", AiIndirectDispatchCapabilityTier.AuthoringCandidate, expectNextState: true, failures);
    bool previewOk = CheckReactiveSensorMonster(files, root, "m106", AiIndirectDispatchCapabilityTier.PreviewReadOnly, expectNextState: false, failures);

    if (!TryLoadPromotedUnitsRt0(files, root, "m281", out AiScriptFile? _, out IReadOnlyList<AiIndirectDispatchUnit> m281Units, out string m281Error))
    {
        failures.Add(m281Error);
    }
    bool m281Ok = m281Units.Count == 0;

    Console.WriteLine($"reactive source        : {(sourceOk ? "OK" : "FAIL")}");
    Console.WriteLine($"m118/m150/m154 author  : {(authoringOk ? "OK" : "FAIL")}");
    Console.WriteLine($"m106 preview-only      : {(previewOk ? "OK" : "FAIL")}");
    Console.WriteLine($"m281 still out         : {(m281Ok ? "OK" : $"FAIL ({m281Units.Count})")}");

    if (!m281Ok)
        failures.Add($"m281 unexpectedly surfaced as reactive sensor ({m281Units.Count} units).");

    bool pass = failures.Count == 0;
    Console.WriteLine(pass ? "VERDICT: PASS" : "VERDICT: FAIL");
    foreach (string failure in failures.Take(20))
        Console.WriteLine($"  FAIL: {failure}");

    WriteJsonIfRequestedRt0(jsonOut, new
    {
        generatedAtUtc = DateTime.UtcNow.ToString("O"),
        root,
        pass,
        sourceOk,
        m281Units = m281Units.Select(unit => unit.UnitId).ToArray(),
        failures = failures.ToArray(),
    });

    return pass ? 0 : 1;
}

static bool CheckReactiveSensorMonster(
    IReadOnlyList<string> files,
    string root,
    string monsterId,
    AiIndirectDispatchCapabilityTier expectedTier,
    bool expectNextState,
    List<string> failures)
{
    if (!TryLoadPromotedUnitsRt0(files, root, monsterId, out AiScriptFile? _, out IReadOnlyList<AiIndirectDispatchUnit> units, out string error))
    {
        failures.Add(error);
        return false;
    }

    AiIndirectDispatchUnit? unit = units.FirstOrDefault(candidate =>
        candidate.UnitId.StartsWith("preview-reactive-", StringComparison.OrdinalIgnoreCase));
    bool countOk = units.Count == 1;
    bool tierOk = unit?.CapabilityTier == expectedTier;
    bool nextStateOk = expectNextState
        ? unit?.EditableNextStateOffset.HasValue == true && unit.CurrentNextStateValue.HasValue
        : unit?.EditableNextStateOffset.HasValue != true;

    Console.WriteLine($"{monsterId} count              : {(countOk ? "OK" : $"FAIL ({units.Count} != 1)")}");
    Console.WriteLine($"{monsterId} tier               : {(tierOk ? "OK" : BuildTierSummary(units))}");
    Console.WriteLine($"{monsterId} next-state lane    : {(nextStateOk ? "OK" : "FAIL")}");

    if (!countOk) failures.Add($"{monsterId} reactive count drifted ({units.Count} != 1)");
    if (!tierOk) failures.Add($"{monsterId} reactive tier drifted ({BuildTierSummary(units)}).");
    if (!nextStateOk) failures.Add($"{monsterId} reactive next-state lane drifted.");
    return countOk && tierOk && nextStateOk;
}

static int SubActorFamilyRt0(IReadOnlyList<string> files, string root, string? jsonOut)
{
    Console.WriteLine("=== AiScriptLab --sub-actor-family-rt0 ===");

    var failures = new List<string>();
    string repoRoot = FindRepoRootShared();
    string supportPath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiEditor_DataModel.AdvancedSupportAccumulator.cs");

    bool sourceOk = true;
    try
    {
        string supportDataModel = File.ReadAllText(supportPath);
        if (!supportDataModel.Contains("monsterNumber != 127", StringComparison.Ordinal)
            || !supportDataModel.Contains("m290", StringComparison.Ordinal))
        {
            sourceOk = false;
            failures.Add("m127 support-accumulator surface lost the explicit m290 collision guardrail.");
        }
    }
    catch (Exception ex)
    {
        sourceOk = false;
        failures.Add($"Sub-actor source-integrity check failed: {ex.Message}");
    }

    bool m288Ok = CheckZeroUnitMonster(files, root, "m288", failures);
    bool m289Ok = CheckZeroUnitMonster(files, root, "m289", failures);

    if (!TryLoadPromotedUnitsRt0(files, root, "m290", out AiScriptFile? _, out IReadOnlyList<AiIndirectDispatchUnit> m290Units, out string m290Error))
    {
        failures.Add(m290Error);
        Console.WriteLine($"m290 load              : FAIL ({m290Error})");
    }

    bool m290CountOk = m290Units.Count == 1;
    bool m290TierOk = m290Units.All(unit => unit.CapabilityTier == AiIndirectDispatchCapabilityTier.AuthoringCandidate);
    bool m290IdOk = m290Units.Any(unit => unit.UnitId.Contains("preview-mortibody-support-switch", StringComparison.OrdinalIgnoreCase));

    Console.WriteLine($"sub-actor source       : {(sourceOk ? "OK" : "FAIL")}");
    Console.WriteLine($"m288 zero units        : {(m288Ok ? "OK" : "FAIL")}");
    Console.WriteLine($"m289 zero units        : {(m289Ok ? "OK" : "FAIL")}");
    Console.WriteLine($"m290 collision count   : {(m290CountOk ? "OK" : $"FAIL ({m290Units.Count} != 1)")}");
    Console.WriteLine($"m290 collision tier    : {(m290TierOk ? "OK" : BuildTierSummary(m290Units))}");
    Console.WriteLine($"m290 collision unit    : {(m290IdOk ? "OK" : "FAIL")}");

    if (!m290CountOk) failures.Add($"m290 collision count drifted ({m290Units.Count} != 1)");
    if (!m290TierOk) failures.Add($"m290 collision tier drifted ({BuildTierSummary(m290Units)}).");
    if (!m290IdOk) failures.Add("m290 no longer collides with preview-mortibody-support-switch as documented.");

    bool pass = failures.Count == 0;
    Console.WriteLine(pass ? "VERDICT: PASS" : "VERDICT: FAIL");
    foreach (string failure in failures.Take(20))
        Console.WriteLine($"  FAIL: {failure}");

    WriteJsonIfRequestedRt0(jsonOut, new
    {
        generatedAtUtc = DateTime.UtcNow.ToString("O"),
        root,
        pass,
        sourceOk,
        m290Units = m290Units.Select(unit => unit.UnitId).ToArray(),
        failures = failures.ToArray(),
    });

    return pass ? 0 : 1;
}

static int RoundScriptedBossRt0(IReadOnlyList<string> files, string root, string? jsonOut)
{
    Console.WriteLine("=== AiScriptLab --round-scripted-boss-rt0 ===");

    var failures = new List<string>();
    string repoRoot = FindRepoRootShared();
    string advancedWindowPath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiPhaseRotationAdvanced_Window.axaml");
    string advancedPhasePath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiEditor_DataModel.PhaseRotationAdvanced.cs");
    string promotionPath = Path.Combine(repoRoot, "FFXProjectEditor", "FfxLib", "Ai", "AiComplexFamilyAuthoringPromotion.cs");
    string[] requiredUnitIds =
    {
        "preview-round-landing",
        "preview-round-crawl",
        "preview-round-sonic-boom",
        "preview-round-aeon-punish",
        "preview-round-finisher",
    };
    bool previewEditGateOk = false;
    bool previewMessagingOk = false;
    bool promotionSkipOk = false;

    try
    {
        string advancedWindow = File.ReadAllText(advancedWindowPath);
        string advancedPhase = File.ReadAllText(advancedPhasePath);
        string promotion = File.ReadAllText(promotionPath);

        previewEditGateOk =
            advancedWindow.Contains("IsVisible=\"{Binding CanOpenEditor}\"", StringComparison.Ordinal)
            && advancedPhase.Contains(
                "bool canOpenEditor = unit.CapabilityTier == AiIndirectDispatchCapabilityTier.AuthoringCandidate",
                StringComparison.Ordinal)
            && advancedPhase.Contains(
                "unit.CanOpenEditor ? $\"rota {unit.UnitIndex}\" : $\"pacote {unit.UnitIndex}\"",
                StringComparison.Ordinal);

        previewMessagingOk =
            advancedPhase.Contains(
                "Hoje isto continua PreviewReadOnly. A tela explica guard, payload, alvo, consumers e aftermath, mas nenhuma escrita nativa desta familia esta liberada ainda.",
                StringComparison.Ordinal)
            && advancedPhase.Contains(
                "Condicoes nativas desta familia continuam preview-only. Hoje, a parte editavel fica em 'Regra de ativacao do draft' e em 'Condição extra da habilidade' do writer comum embedado.",
                StringComparison.Ordinal);

        promotionSkipOk =
            promotion.Contains(
                "if (unit.UnitId.StartsWith(\"preview-round-\", StringComparison.OrdinalIgnoreCase))",
                StringComparison.Ordinal);
    }
    catch (Exception ex)
    {
        failures.Add($"round-scripted-boss preview wiring source check failed: {ex.Message}");
    }

    if (!TryLoadPromotedUnitsRt0(files, root, "m238", out AiScriptFile? _, out IReadOnlyList<AiIndirectDispatchUnit> m238Units, out string m238Error))
    {
        failures.Add(m238Error);
        Console.WriteLine($"m238 load              : FAIL ({m238Error})");
    }

    bool countOk = m238Units.Count == 5;
    bool idsOk = requiredUnitIds.All(required =>
        m238Units.Any(unit => unit.UnitId.Contains(required, StringComparison.OrdinalIgnoreCase)));
    bool tiersOk = m238Units.Count > 0
        && m238Units.All(unit => unit.CapabilityTier == AiIndirectDispatchCapabilityTier.PreviewReadOnly);
    AiIndirectDispatchUnit? finisherUnit = m238Units.FirstOrDefault(unit =>
        unit.UnitId.Equals("preview-round-finisher", StringComparison.OrdinalIgnoreCase));
    bool finisherOk = finisherUnit != null
        && IndirectUnitContainsText(finisherUnit, "0x40AB")
        && IndirectUnitContainsText(finisherUnit, "0x40DF");
    bool presentationOk = m238Units.Any(unit =>
        IndirectUnitContainsText(unit, "CurrentEncounter")
        || IndirectUnitContainsText(unit, "camReq")
        || IndirectUnitContainsText(unit, "stat_visible_cam")
        || IndirectUnitContainsText(unit, "chosenCommand"));

    Console.WriteLine($"m238 count             : {(countOk ? "OK" : $"FAIL ({m238Units.Count} != 5)")}");
    Console.WriteLine($"m238 unit ids          : {(idsOk ? "OK" : "FAIL")}");
    Console.WriteLine($"m238 tiers             : {(tiersOk ? "OK" : BuildTierSummary(m238Units))}");
    Console.WriteLine($"m238 finisher bundle   : {(finisherOk ? "OK" : "FAIL")}");
    Console.WriteLine($"m238 camera/present    : {(presentationOk ? "OK" : "FAIL")}");
    Console.WriteLine($"m238 ui edit gate      : {(previewEditGateOk ? "OK" : "FAIL")}");
    Console.WriteLine($"m238 preview copy      : {(previewMessagingOk ? "OK" : "FAIL")}");
    Console.WriteLine($"round preview skip     : {(promotionSkipOk ? "OK" : "FAIL")}");

    if (!countOk) failures.Add($"m238 round-scripted-boss count drifted ({m238Units.Count} != 5)");
    if (!idsOk) failures.Add("m238 lost one of the five preview-round-* unit ids.");
    if (!tiersOk) failures.Add($"m238 no longer stays PreviewReadOnly ({BuildTierSummary(m238Units)}).");
    if (!finisherOk) failures.Add("m238 finisher bundle no longer proves 0x40AB + 0x40DF together.");
    if (!presentationOk) failures.Add("m238 round-scripted-boss bundle lost its chosenCommand/CurrentEncounter/camReq/stat_visible_cam evidence.");
    if (!previewEditGateOk) failures.Add("m238 preview no longer proves the V2 CanOpenEditor gate for preview-only units.");
    if (!previewMessagingOk) failures.Add("m238 preview no longer carries the explicit PreviewReadOnly copy in the advanced phase manager.");
    if (!promotionSkipOk) failures.Add("round-scripted-boss preview lost the promotion skip guard in AiComplexFamilyAuthoringPromotion.");

    if (!TryLoadPromotedUnitsRt0(files, root, "m124", out AiScriptFile? _, out IReadOnlyList<AiIndirectDispatchUnit> m124Units, out string m124Error))
    {
        failures.Add(m124Error);
        Console.WriteLine($"m124 load              : FAIL ({m124Error})");
    }
    bool m124Ok = m124Units.Count == 4
        && m124Units.All(unit => unit.CapabilityTier == AiIndirectDispatchCapabilityTier.AuthoringCandidate)
        && m124Units.Any(unit => unit.UnitId.Contains("dispatch-row-0", StringComparison.OrdinalIgnoreCase));
    Console.WriteLine($"m124 Seymour stays 4   : {(m124Ok ? "OK" : $"FAIL ({m124Units.Count})")}");
    if (!m124Ok)
        failures.Add($"m124 strict Seymour dispatch drifted while opening round-scripted-boss ({m124Units.Count} units).");

    bool m211Ok = CheckNoUnitIdFragmentMonster(files, root, "m211", "preview-round-", failures);
    bool m223Ok = CheckNoUnitIdFragmentMonster(files, root, "m223", "preview-round-", failures);
    bool m224Ok = CheckNoUnitIdFragmentMonster(files, root, "m224", "preview-round-", failures);

    bool pass = failures.Count == 0;
    Console.WriteLine($"cross-family guard     : {(m211Ok && m223Ok && m224Ok ? "OK" : "FAIL")}");
    Console.WriteLine(pass ? "VERDICT: PASS" : "VERDICT: FAIL");
    foreach (string failure in failures.Take(20))
        Console.WriteLine($"  FAIL: {failure}");

    WriteJsonIfRequestedRt0(jsonOut, new
    {
        generatedAtUtc = DateTime.UtcNow.ToString("O"),
        root,
        pass,
        m238UnitIds = m238Units.Select(unit => unit.UnitId).ToArray(),
        m238Tiers = m238Units.Select(unit => unit.CapabilityTier.ToString()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        previewEditGateOk,
        previewMessagingOk,
        promotionSkipOk,
        failures = failures.ToArray(),
    });

    return pass ? 0 : 1;
}

static int TonberryCameraRoutingRt0(IReadOnlyList<string> files, string root, string? jsonOut)
{
    Console.WriteLine("=== AiScriptLab --tonberry-camera-routing-rt0 ===");

    var failures = new List<string>();
    string repoRoot = FindRepoRootShared();
    string promotionPath = Path.Combine(repoRoot, "FFXProjectEditor", "FfxLib", "Ai", "AiComplexFamilyAuthoringPromotion.cs");
    string advancedPhasePath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiEditor_DataModel.PhaseRotationAdvanced.cs");
    string[] requiredUnitIds =
    {
        "preview-tonberry-direct-pressure",
        "preview-tonberry-position-cycle",
        "preview-tonberry-counter-window",
        "preview-tonberry-camera-routing",
    };

    bool promotionSkipOk = false;
    bool familyLabelOk = false;
    try
    {
        string promotion = File.ReadAllText(promotionPath);
        string advancedPhase = File.ReadAllText(advancedPhasePath);
        promotionSkipOk =
            promotion.Contains(
                "if (unit.UnitId.StartsWith(\"preview-tonberry-\", StringComparison.OrdinalIgnoreCase))",
                StringComparison.Ordinal);
        familyLabelOk =
            advancedPhase.Contains("\"preview tonberry camera routing\"", StringComparison.Ordinal)
            && advancedPhase.Contains("\"tonberry camera routing boss\"", StringComparison.Ordinal);
    }
    catch (Exception ex)
    {
        failures.Add($"tonberry-camera-routing source check failed: {ex.Message}");
    }

    bool pass = true;
    pass &= CheckMonsterExpectationRt0(
        files,
        root,
        "m223",
        expectedCount: 4,
        requiredUnitIds: requiredUnitIds,
        requiredCapabilityLabel: "preview tonberry camera routing",
        requiredText: new[] { "0x4081", "0x4061", "0x4062", "0x4060", "CurrentEncounter", "battleVar0288", "camReq", "stat_visible_cam", "LastAttacker" },
        requiredTier: AiIndirectDispatchCapabilityTier.PreviewReadOnly,
        failures,
        out object m223Summary);
    pass &= CheckMonsterExpectationRt0(
        files,
        root,
        "m224",
        expectedCount: 4,
        requiredUnitIds: requiredUnitIds,
        requiredCapabilityLabel: "preview tonberry camera routing",
        requiredText: new[] { "0x4081", "0x4061", "0x410F", "0x4060", "CurrentEncounter", "battleVar0288", "camReq", "stat_visible_cam", "LastAttacker" },
        requiredTier: AiIndirectDispatchCapabilityTier.PreviewReadOnly,
        failures,
        out object m224Summary);

    if (!promotionSkipOk)
    {
        pass = false;
        failures.Add("tonberry preview lost the promotion skip guard in AiComplexFamilyAuthoringPromotion.");
    }

    if (!familyLabelOk)
    {
        pass = false;
        failures.Add("advanced phase manager lost the family label wiring for tonberry camera routing.");
    }

    bool m211Ok = CheckNoUnitIdFragmentMonster(files, root, "m211", "preview-tonberry-", failures);
    bool m238LeakOk = CheckNoUnitIdFragmentMonster(files, root, "m238", "preview-tonberry-", failures);
    Console.WriteLine($"m211 no tonberry leak  : {(m211Ok ? "OK" : "FAIL")}");
    Console.WriteLine($"m238 no tonberry leak  : {(m238LeakOk ? "OK" : "FAIL")}");
    Console.WriteLine($"tonberry promotion skip: {(promotionSkipOk ? "OK" : "FAIL")}");
    Console.WriteLine($"advanced family label  : {(familyLabelOk ? "OK" : "FAIL")}");

    pass &= m211Ok && m238LeakOk;
    Console.WriteLine(pass ? "VERDICT: PASS" : "VERDICT: FAIL");
    foreach (string failure in failures.Take(20))
        Console.WriteLine($"  FAIL: {failure}");

    WriteJsonIfRequestedRt0(jsonOut, new
    {
        generatedAtUtc = DateTime.UtcNow.ToString("O"),
        root,
        pass,
        promotionSkipOk,
        familyLabelOk,
        m223 = m223Summary,
        m224 = m224Summary,
        failures = failures.ToArray(),
    });

    return pass ? 0 : 1;
}

static int AdvancedFamilySurfacePopupsRt0(IReadOnlyList<string> files, string root, string? jsonOut)
{
    Console.WriteLine("=== AiScriptLab --advanced-family-surface-popups-rt0 ===");

    var failures = new List<string>();
    string repoRoot = FindRepoRootShared();
    string advancedWindowPath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiPhaseRotationAdvanced_Window.axaml");
    string advancedWindowCodePath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiPhaseRotationAdvanced_Window.axaml.cs");
    string advancedPhasePath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiEditor_DataModel.PhaseRotationAdvanced.cs");
    string animaPath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiEditor_DataModel.AdvancedAnima.cs");
    string familyDialogsPath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiEditor_DataModel.AdvancedFamilyDialogs.cs");
    string encounterAppearPath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiEditor_DataModel.AdvancedEncounterAppear.cs");
    string previewFamiliesPath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiEditor_DataModel.AdvancedPreviewFamilies.cs");
    string mortiorchisPath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiEditor_DataModel.AdvancedMortiorchis.cs");
    string familyWindowPath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiAdvancedFamilySurface_Window.axaml");
    string familyWindowCodePath = Path.Combine(repoRoot, "FFXProjectEditor", "Modules", "MonsterAiEditor", "MonsterAiAdvancedFamilySurface_Window.axaml.cs");

    bool sourceOk = true;
    try
    {
        string advancedWindow = File.ReadAllText(advancedWindowPath);
        string advancedWindowCode = File.ReadAllText(advancedWindowCodePath);
        string advancedPhase = File.ReadAllText(advancedPhasePath);
        string animaDataModel = File.ReadAllText(animaPath);
        string familyDialogs = File.ReadAllText(familyDialogsPath);
        string encounterAppear = File.ReadAllText(encounterAppearPath);
        string previewFamilies = File.ReadAllText(previewFamiliesPath);
        string mortiorchis = File.ReadAllText(mortiorchisPath);
        string familyWindow = File.ReadAllText(familyWindowPath);
        string familyWindowCode = File.ReadAllText(familyWindowCodePath);

        string[] requiredFamilyKeys =
        {
            "anima-payload-picker",
            "elemental-cluster",
            "encounter-keyed-appear-disable",
            "mortiorchis-companion",
            "support-accumulator",
            "round-scripted-boss",
            "reactive-sensor",
            "tonberry-camera-routing",
        };

        bool mainWindowButtonsOk =
            advancedWindow.Contains("Button_OpenAdvancedFamilySurfaceEditor", StringComparison.Ordinal)
            && requiredFamilyKeys.All(key => advancedWindow.Contains($"Tag=\"{key}\"", StringComparison.Ordinal))
            && advancedWindow.Contains("HasAdvancedAnimaSurface", StringComparison.Ordinal)
            && advancedWindow.Contains("HasAdvancedEncounterAppearSurface", StringComparison.Ordinal)
            && advancedWindow.Contains("HasAdvancedMortiorchisSurface", StringComparison.Ordinal)
            && advancedWindow.Contains("HasAdvancedRoundScriptedBossSurface", StringComparison.Ordinal)
            && advancedWindow.Contains("HasAdvancedTonberryCameraRoutingSurface", StringComparison.Ordinal);

        bool mainWindowRoutingOk =
            advancedWindowCode.Contains("TryOpenAdvancedFamilySurfaceEditor(", StringComparison.Ordinal)
            && advancedWindowCode.Contains("TryOpenAdvancedFamilySurfaceEditorFromUnit(unitId)", StringComparison.Ordinal)
            && advancedWindowCode.Contains("new MonsterAiAdvancedFamilySurface_Window(dataModel, snapshot)", StringComparison.Ordinal)
            && advancedWindowCode.Contains("if (TryOpenAdvancedFamilySurfaceEditorFromUnit(unitId))", StringComparison.Ordinal);

        bool familyDialogRoutingOk =
            familyDialogs.Contains("TryBuildAdvancedFamilySurfaceDialog(", StringComparison.Ordinal)
            && familyDialogs.Contains("TryBuildAdvancedFamilySurfaceDialogForUnit(", StringComparison.Ordinal)
            && familyDialogs.Contains("dispatch-support-0008-0007-0", StringComparison.Ordinal)
            && familyDialogs.Contains("preview-encounter-", StringComparison.Ordinal)
            && familyDialogs.Contains("preview-mortiorchis-", StringComparison.Ordinal)
            && familyDialogs.Contains("preview-round-", StringComparison.Ordinal)
            && familyDialogs.Contains("preview-tonberry-", StringComparison.Ordinal)
            && familyDialogs.Contains("AdvancedFamilySurfaceKeys.Anima", StringComparison.Ordinal)
            && familyDialogs.Contains("AdvancedFamilySurfaceKeys.EncounterKeyedAppearDisable", StringComparison.Ordinal)
            && familyDialogs.Contains("AdvancedFamilySurfaceKeys.Mortiorchis", StringComparison.Ordinal)
            && familyDialogs.Contains("AdvancedFamilySurfaceKeys.RoundScriptedBoss", StringComparison.Ordinal)
            && familyDialogs.Contains("AdvancedFamilySurfaceKeys.TonberryCameraRouting", StringComparison.Ordinal);

        bool familyWindowOk =
            familyWindow.Contains("DialogFamilyRowTemplate", StringComparison.Ordinal)
            && familyWindow.Contains("Button_OpenFamilyRowEditor", StringComparison.Ordinal)
            && familyWindow.Contains("ItemsSource=\"{Binding Rows}\"", StringComparison.Ordinal)
            && familyWindow.Contains("Text=\"{Binding HonestSummary}\"", StringComparison.Ordinal)
            && familyWindowCode.Contains("childWindow.Closed +=", StringComparison.Ordinal)
            && familyWindowCode.Contains("editorVm.Refresh();", StringComparison.Ordinal)
            && familyWindowCode.Contains("AiIndirectDispatchEditorFocusKind.CommandSlot", StringComparison.Ordinal)
            && familyWindowCode.Contains("AiIndirectDispatchEditorFocusKind.TargetSlot", StringComparison.Ordinal)
            && familyWindowCode.Contains("AiIndirectDispatchEditorFocusKind.NextState", StringComparison.Ordinal);

        bool contextRefreshOk =
            animaDataModel.Contains("TryBuildAdvancedAnimaFamilyDialogSnapshot", StringComparison.Ordinal)
            && advancedPhase.Contains("UpdateAdvancedAnimaContext();", StringComparison.Ordinal)
            && previewFamilies.Contains("HasAdvancedRoundScriptedBossSurface", StringComparison.Ordinal)
            && previewFamilies.Contains("HasAdvancedTonberryCameraRoutingSurface", StringComparison.Ordinal)
            && previewFamilies.Contains("preview-round-landing", StringComparison.Ordinal)
            && previewFamilies.Contains("preview-tonberry-direct-pressure", StringComparison.Ordinal)
            && encounterAppear.Contains("HasAdvancedEncounterAppearSurface", StringComparison.Ordinal)
            && encounterAppear.Contains("preview-encounter-open", StringComparison.Ordinal)
            && encounterAppear.Contains("preview-encounter-appear-disable", StringComparison.Ordinal)
            && mortiorchis.Contains("HasAdvancedMortiorchisSurface", StringComparison.Ordinal)
            && mortiorchis.Contains("preview-mortiorchis-body-handoff", StringComparison.Ordinal)
            && mortiorchis.Contains("preview-mortiorchis-absorption", StringComparison.Ordinal)
            && advancedPhase.Contains("UpdateAdvancedEncounterAppearContext();", StringComparison.Ordinal)
            && advancedPhase.Contains("UpdateAdvancedMortiorchisContext();", StringComparison.Ordinal)
            && advancedPhase.Contains("UpdateAdvancedRoundScriptedBossContext();", StringComparison.Ordinal)
            && advancedPhase.Contains("UpdateAdvancedTonberryCameraRoutingContext();", StringComparison.Ordinal);

        Console.WriteLine($"main surface buttons   : {(mainWindowButtonsOk ? "OK" : "FAIL")}");
        Console.WriteLine($"main popup routing     : {(mainWindowRoutingOk ? "OK" : "FAIL")}");
        Console.WriteLine($"dialog family routing  : {(familyDialogRoutingOk ? "OK" : "FAIL")}");
        Console.WriteLine($"family popup window    : {(familyWindowOk ? "OK" : "FAIL")}");
        Console.WriteLine($"family context refresh : {(contextRefreshOk ? "OK" : "FAIL")}");

        if (!mainWindowButtonsOk)
        {
            sourceOk = false;
            failures.Add("advanced phase window lost one of the dedicated family-surface buttons or visibility bindings.");
        }

        if (!mainWindowRoutingOk)
        {
            sourceOk = false;
            failures.Add("advanced phase window code-behind lost the popup routing bridge for family-specific surfaces.");
        }

        if (!familyDialogRoutingOk)
        {
            sourceOk = false;
            failures.Add("advanced family dialog router no longer resolves preview units to the dedicated popup families.");
        }

        if (!familyWindowOk)
        {
            sourceOk = false;
            failures.Add("advanced family popup window lost its dedicated row template, refresh cycle, or focused child-window routing.");
        }

        if (!contextRefreshOk)
        {
            sourceOk = false;
            failures.Add("advanced family contexts no longer refresh m143/m238/m223/m224 from the specialized family update pass.");
        }
    }
    catch (Exception ex)
    {
        sourceOk = false;
        failures.Add($"advanced family popup source check failed: {ex.Message}");
    }

    bool m125Ok = CheckFamilyPopupMonster(
        "m125",
        expectedCount: 1,
        expectedTier: AiIndirectDispatchCapabilityTier.AuthoringCandidate,
        requiredUnitIds: new[] { "dispatch-support-0008-0007-0" },
        requireLaunchLane: true,
        out object m125Summary);
    bool m143Ok = CheckFamilyPopupMonster(
        "m143",
        expectedCount: 2,
        expectedTier: AiIndirectDispatchCapabilityTier.AuthoringCandidate,
        requiredUnitIds: new[] { "preview-mortiorchis-body-handoff", "preview-mortiorchis-absorption" },
        requireLaunchLane: true,
        out object m143Summary);
    bool m238Ok = CheckFamilyPopupMonster(
        "m238",
        expectedCount: 5,
        expectedTier: AiIndirectDispatchCapabilityTier.PreviewReadOnly,
        requiredUnitIds: new[] { "preview-round-landing", "preview-round-crawl", "preview-round-sonic-boom", "preview-round-aeon-punish", "preview-round-finisher" },
        requireLaunchLane: false,
        out object m238Summary);
    bool m223Ok = CheckFamilyPopupMonster(
        "m223",
        expectedCount: 4,
        expectedTier: AiIndirectDispatchCapabilityTier.PreviewReadOnly,
        requiredUnitIds: new[] { "preview-tonberry-direct-pressure", "preview-tonberry-position-cycle", "preview-tonberry-counter-window", "preview-tonberry-camera-routing" },
        requireLaunchLane: false,
        out object m223Summary);
    bool m224Ok = CheckFamilyPopupMonster(
        "m224",
        expectedCount: 4,
        expectedTier: AiIndirectDispatchCapabilityTier.PreviewReadOnly,
        requiredUnitIds: new[] { "preview-tonberry-direct-pressure", "preview-tonberry-position-cycle", "preview-tonberry-counter-window", "preview-tonberry-camera-routing" },
        requireLaunchLane: false,
        out object m224Summary);
    bool m211Ok = CheckFamilyPopupMonster(
        "m211",
        expectedCount: 2,
        expectedTier: AiIndirectDispatchCapabilityTier.PreviewReadOnly,
        requiredUnitIds: new[] { "preview-encounter-open", "preview-encounter-appear-disable" },
        requireLaunchLane: false,
        out object m211Summary);

    bool pass = sourceOk && m125Ok && m143Ok && m238Ok && m223Ok && m224Ok && m211Ok;
    Console.WriteLine(pass ? "VERDICT: PASS" : "VERDICT: FAIL");
    foreach (string failure in failures.Take(20))
        Console.WriteLine($"  FAIL: {failure}");

    WriteJsonIfRequestedRt0(jsonOut, new
    {
        generatedAtUtc = DateTime.UtcNow.ToString("O"),
        root,
        pass,
        sourceOk,
        m125 = m125Summary,
        m143 = m143Summary,
        m238 = m238Summary,
        m223 = m223Summary,
        m224 = m224Summary,
        m211 = m211Summary,
        failures = failures.ToArray(),
    });

    return pass ? 0 : 1;

    bool CheckFamilyPopupMonster(
        string monsterId,
        int expectedCount,
        AiIndirectDispatchCapabilityTier expectedTier,
        IReadOnlyList<string> requiredUnitIds,
        bool requireLaunchLane,
        out object summary)
    {
        summary = new
        {
            monsterId,
            loadError = string.Empty,
            unitCount = 0,
            countOk = false,
            tierOk = false,
            unitIdsOk = false,
            launchLaneOk = false,
            unitIds = Array.Empty<string>(),
        };

        if (!TryLoadPromotedUnitsRt0(files, root, monsterId, out AiScriptFile? _, out IReadOnlyList<AiIndirectDispatchUnit> units, out string error))
        {
            failures.Add($"{monsterId}: {error}");
            Console.WriteLine($"{monsterId} load               : FAIL ({error})");
            summary = new
            {
                monsterId,
                loadError = error,
                unitCount = 0,
                countOk = false,
                tierOk = false,
                unitIdsOk = false,
                launchLaneOk = false,
                unitIds = Array.Empty<string>(),
            };
            return false;
        }

        bool countOk = units.Count == expectedCount;
        bool tierOk = units.Count > 0 && units.All(unit => unit.CapabilityTier == expectedTier);
        bool unitIdsOk = requiredUnitIds.All(required =>
            units.Any(unit => unit.UnitId.Contains(required, StringComparison.OrdinalIgnoreCase)));
        bool launchLaneOk = !requireLaunchLane || units.Any(unit =>
            unit.EditableSlots.Count > 0
            || unit.EditableTargetSlots.Any(slot => slot.CanEdit)
            || unit.EditableNextStateOffset.HasValue);

        Console.WriteLine($"{monsterId} family count      : {(countOk ? "OK" : $"FAIL ({units.Count} != {expectedCount})")}");
        Console.WriteLine($"{monsterId} family tiers      : {(tierOk ? "OK" : BuildTierSummary(units))}");
        Console.WriteLine($"{monsterId} family unit ids   : {(unitIdsOk ? "OK" : string.Join(", ", units.Select(unit => unit.UnitId)))}");
        Console.WriteLine($"{monsterId} family launch lane: {(launchLaneOk ? "OK" : "FAIL")}");

        if (!countOk) failures.Add($"{monsterId} family popup count drifted ({units.Count} != {expectedCount}).");
        if (!tierOk) failures.Add($"{monsterId} family popup tier drifted ({BuildTierSummary(units)}).");
        if (!unitIdsOk) failures.Add($"{monsterId} family popup lost one of the expected unit ids.");
        if (!launchLaneOk) failures.Add($"{monsterId} family popup no longer exposes any narrow launch lane where one was expected.");

        summary = new
        {
            monsterId,
            loadError = string.Empty,
            unitCount = units.Count,
            countOk,
            tierOk,
            unitIdsOk,
            launchLaneOk,
            unitIds = units.Select(unit => unit.UnitId).ToArray(),
            tiers = units.Select(unit => unit.CapabilityTier.ToString()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        };

        return countOk && tierOk && unitIdsOk && launchLaneOk;
    }
}

static bool CheckZeroUnitMonster(
    IReadOnlyList<string> files,
    string root,
    string monsterId,
    List<string> failures)
{
    if (!TryLoadPromotedUnitsRt0(files, root, monsterId, out AiScriptFile? _, out IReadOnlyList<AiIndirectDispatchUnit> units, out string error))
    {
        failures.Add(error);
        return false;
    }

    bool ok = units.Count == 0;
    Console.WriteLine($"{monsterId} zero units        : {(ok ? "OK" : $"FAIL ({units.Count})")}");
    if (!ok)
        failures.Add($"{monsterId} unexpectedly surfaced ({units.Count} units).");
    return ok;
}

static bool CheckNoUnitIdFragmentMonster(
    IReadOnlyList<string> files,
    string root,
    string monsterId,
    string forbiddenFragment,
    List<string> failures)
{
    if (!TryLoadPromotedUnitsRt0(files, root, monsterId, out AiScriptFile? _, out IReadOnlyList<AiIndirectDispatchUnit> units, out string error))
    {
        failures.Add(error);
        return false;
    }

    bool ok = units.All(unit => !unit.UnitId.Contains(forbiddenFragment, StringComparison.OrdinalIgnoreCase));
    Console.WriteLine($"{monsterId} {forbiddenFragment} leak : {(ok ? "OK" : "FAIL")}");
    if (!ok)
        failures.Add($"{monsterId} leaked family fragment '{forbiddenFragment}'.");
    return ok;
}

static bool CheckMonsterExpectationRt0(
    IReadOnlyList<string> files,
    string root,
    string monsterId,
    int expectedCount,
    IReadOnlyList<string> requiredUnitIds,
    string requiredCapabilityLabel,
    IReadOnlyList<string> requiredText,
    AiIndirectDispatchCapabilityTier requiredTier,
    List<string> failures,
    out object summary)
{
    summary = new
    {
        monsterId,
        loadError = string.Empty,
        unitCount = 0,
        countOk = false,
        tierOk = false,
        capabilityLabelOk = false,
        unitIdsOk = false,
        textOk = false,
        unitIds = Array.Empty<string>(),
        capabilityTiers = Array.Empty<string>(),
        capabilityLabels = Array.Empty<string>(),
    };

    if (!TryLoadPromotedUnitsRt0(files, root, monsterId, out AiScriptFile? _, out IReadOnlyList<AiIndirectDispatchUnit> units, out string error))
    {
        Console.WriteLine($"{monsterId} load               : FAIL ({error})");
        failures.Add($"{monsterId}: {error}");
        summary = new
        {
            monsterId,
            loadError = error,
            unitCount = 0,
            countOk = false,
            tierOk = false,
            capabilityLabelOk = false,
            unitIdsOk = false,
            textOk = false,
            unitIds = Array.Empty<string>(),
            capabilityTiers = Array.Empty<string>(),
            capabilityLabels = Array.Empty<string>(),
        };
        return false;
    }

    bool countOk = units.Count == expectedCount;
    bool tierOk = units.Count > 0 && units.All(unit => unit.CapabilityTier == requiredTier);
    bool capabilityLabelOk = units.Any(unit => unit.CapabilityLabel.Contains(requiredCapabilityLabel, StringComparison.OrdinalIgnoreCase));
    bool unitIdsOk = requiredUnitIds.All(required => units.Any(unit => unit.UnitId.Contains(required, StringComparison.OrdinalIgnoreCase)));
    bool textOk = requiredText.All(required => units.Any(unit => IndirectUnitContainsText(unit, required)));

    Console.WriteLine($"{monsterId} count             : {(countOk ? "OK" : $"FAIL ({units.Count} != {expectedCount})")}");
    Console.WriteLine($"{monsterId} tiers             : {(tierOk ? "OK" : BuildTierSummary(units))}");
    Console.WriteLine($"{monsterId} capability label  : {(capabilityLabelOk ? "OK" : BuildCapabilityLabelSummary(units))}");
    Console.WriteLine($"{monsterId} unit ids          : {(unitIdsOk ? "OK" : string.Join(", ", units.Select(unit => unit.UnitId)))}");
    Console.WriteLine($"{monsterId} text proof        : {(textOk ? "OK" : "FAIL")}");

    if (!countOk) failures.Add($"{monsterId} count drifted ({units.Count} != {expectedCount}).");
    if (!tierOk) failures.Add($"{monsterId} lost PreviewReadOnly tier ({BuildTierSummary(units)}).");
    if (!capabilityLabelOk) failures.Add($"{monsterId} lost capability label '{requiredCapabilityLabel}'.");
    if (!unitIdsOk) failures.Add($"{monsterId} lost one of the expected unit ids.");
    if (!textOk) failures.Add($"{monsterId} lost one of the required proof strings.");

    summary = new
    {
        monsterId,
        loadError = string.Empty,
        unitCount = units.Count,
        countOk,
        tierOk,
        capabilityLabelOk,
        unitIdsOk,
        textOk,
        unitIds = units.Select(unit => unit.UnitId).ToArray(),
        capabilityTiers = units.Select(unit => unit.CapabilityTier.ToString()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        capabilityLabels = units.Select(unit => unit.CapabilityLabel).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
    };

    return countOk && tierOk && capabilityLabelOk && unitIdsOk && textOk;
}

static bool TryLoadPromotedUnitsRt0(
    IReadOnlyList<string> files,
    string root,
    string monsterId,
    out AiScriptFile? script,
    out IReadOnlyList<AiIndirectDispatchUnit> units,
    out string error)
{
    script = null;
    units = Array.Empty<AiIndirectDispatchUnit>();

    string? path = files.FirstOrDefault(p =>
        string.Equals(Path.GetFileNameWithoutExtension(p), monsterId, StringComparison.OrdinalIgnoreCase));
    if (path == null)
    {
        error = $"{monsterId}.bin not found under {root}";
        return false;
    }

    try
    {
        byte[] monster = File.ReadAllBytes(path);
        byte[]? aiFile = AiScript_File.SliceAiFileFromMonster(monster);
        if (aiFile == null)
        {
            error = $"{monsterId} has no AiFile slice";
            return false;
        }

        script = AiScript_File.Read(aiFile);
        IReadOnlyList<AiIndirectDispatchUnit> detected = AiAutomation.DetectIndirectDispatchUnits(monster, script);
        units = AiComplexFamilyAuthoringPromotion.Promote(script, detected);
        error = string.Empty;
        return true;
    }
    catch (Exception ex)
    {
        error = $"{monsterId}: {ex.GetType().Name}: {ex.Message}";
        return false;
    }
}

static string BuildTierSummary(IReadOnlyList<AiIndirectDispatchUnit> units) =>
    units.Count == 0
        ? "(none)"
        : string.Join(", ", units.Select(unit => unit.CapabilityTier.ToString()).Distinct(StringComparer.OrdinalIgnoreCase));

static string BuildCapabilityLabelSummary(IReadOnlyList<AiIndirectDispatchUnit> units) =>
    units.Count == 0
        ? string.Empty
        : string.Join(" | ", units.Select(unit => unit.CapabilityLabel).Distinct(StringComparer.OrdinalIgnoreCase));

static bool IndirectUnitContainsText(AiIndirectDispatchUnit unit, string text)
{
    if (unit.UnitId.Contains(text, StringComparison.OrdinalIgnoreCase)) return true;
    if (unit.HookKind.Contains(text, StringComparison.OrdinalIgnoreCase)) return true;
    if (unit.GuardSummary.Contains(text, StringComparison.OrdinalIgnoreCase)) return true;
    if (unit.NextStateSummary.Contains(text, StringComparison.OrdinalIgnoreCase)) return true;
    if (unit.WarningSummary.Contains(text, StringComparison.OrdinalIgnoreCase)) return true;
    if (unit.CapabilityLabel.Contains(text, StringComparison.OrdinalIgnoreCase)) return true;
    if (unit.PayloadWrites.Any(write =>
            write.VariableName.Contains(text, StringComparison.OrdinalIgnoreCase)
            || write.RoleSummary.Contains(text, StringComparison.OrdinalIgnoreCase)
            || write.ValueSummary.Contains(text, StringComparison.OrdinalIgnoreCase)))
        return true;
    if (unit.Consumers.Any(consumer =>
            consumer.Label.Contains(text, StringComparison.OrdinalIgnoreCase)
            || consumer.CommandVariableName.Contains(text, StringComparison.OrdinalIgnoreCase)
            || consumer.TargetVariableName.Contains(text, StringComparison.OrdinalIgnoreCase)))
        return true;

    return unit.CompanionEffects.Any(note => note.Contains(text, StringComparison.OrdinalIgnoreCase));
}

static string FindRepoRootShared()
{
    string current = Directory.GetCurrentDirectory();
    while (!string.IsNullOrWhiteSpace(current))
    {
        if (Directory.Exists(Path.Combine(current, "FFXProjectEditor"))
            && Directory.Exists(Path.Combine(current, "RuntimeTools")))
        {
            return current;
        }

        string? parent = Directory.GetParent(current)?.FullName;
        if (string.IsNullOrWhiteSpace(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            break;
        current = parent;
    }

    return Directory.GetCurrentDirectory();
}

static void WriteJsonIfRequestedRt0(string? jsonOut, object payload)
{
    if (string.IsNullOrWhiteSpace(jsonOut))
        return;

    string? dir = Path.GetDirectoryName(jsonOut);
    if (!string.IsNullOrWhiteSpace(dir))
        Directory.CreateDirectory(dir);

    File.WriteAllText(jsonOut, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"json={jsonOut}");
}

static int SeymourComplexDetectorsRt0(IReadOnlyList<string> files, string root, string? jsonOut)
{
    Console.WriteLine("=== AiScriptLab --seymour-complex-detectors-rt0 ===");

    bool pass = true;
    var monsterSummaries = new List<object>();

    pass &= EvaluateMonster(
        "m131",
        minUnits: 3,
        requiredUnitIds: new[] { "preview-omnis-elemental", "preview-omnis-dispel-break", "preview-omnis-ultima-break" },
        requiredCapabilityLabel: "preview complexa elemental",
        requiredText: new[] { "Mortiphasm", "Absorb", "0x60F0", "Dispel" },
        out object omnisSummary);
    monsterSummaries.Add(omnisSummary);

    pass &= EvaluateMonster(
        "m142",
        minUnits: 3,
        requiredUnitIds: new[] { "preview-flux-lance-cycle", "preview-flux-dispel-cross", "preview-flux-self-buff" },
        requiredCapabilityLabel: "preview acoplada host/companheiro",
        requiredText: new[] { "Mortiorchis", "aeon", "Reflect", "Lance of Atrophy" },
        out object fluxSummary);
    monsterSummaries.Add(fluxSummary);

    pass &= EvaluateMonster(
        "m143",
        minUnits: 2,
        requiredUnitIds: new[] { "preview-mortiorchis-body-handoff", "preview-mortiorchis-absorption" },
        requiredCapabilityLabel: "preview companheiro acoplado",
        requiredText: new[] { "Mortibsorption", "battleVar0020", "CurrentTurnDelay" },
        out object mortiorchisSummary);
    monsterSummaries.Add(mortiorchisSummary);

    pass &= EvaluateMonster(
        "m127",
        minUnits: 1,
        requiredUnitIds: new[] { "preview-mortibody-support-switch" },
        requiredCapabilityLabel: "preview suporte por acumulador",
        requiredText: new[] { "Shell", "Haste", "Nul", "Desperado" },
        out object mortibodySummary);
    monsterSummaries.Add(mortibodySummary);

    pass &= EvaluateMonster(
        "m106",
        minUnits: 1,
        requiredUnitIds: new[] { "preview-reactive-" },
        requiredCapabilityLabel: "preview sensor reativo",
        requiredText: new[] { "usedCommand", "readMoveProperty", "runBtlSceneA" },
        out object mortiphasm106Summary);
    monsterSummaries.Add(mortiphasm106Summary);

    pass &= EvaluateMonster(
        "m118",
        minUnits: 1,
        requiredUnitIds: new[] { "preview-reactive-" },
        requiredCapabilityLabel: "preview sensor reativo",
        requiredText: new[] { "usedCommand", "readMoveProperty" },
        out object mortiphasm118Summary);
    monsterSummaries.Add(mortiphasm118Summary);

    pass &= EvaluateMonster(
        "m150",
        minUnits: 1,
        requiredUnitIds: new[] { "preview-reactive-" },
        requiredCapabilityLabel: "preview sensor reativo",
        requiredText: new[] { "usedCommand", "readMoveProperty" },
        out object mortiphasm150Summary);
    monsterSummaries.Add(mortiphasm150Summary);

    pass &= EvaluateMonster(
        "m154",
        minUnits: 1,
        requiredUnitIds: new[] { "preview-reactive-" },
        requiredCapabilityLabel: "preview sensor reativo",
        requiredText: new[] { "usedCommand", "readMoveProperty" },
        out object mortiphasm154Summary);
    monsterSummaries.Add(mortiphasm154Summary);

    if (!string.IsNullOrWhiteSpace(jsonOut))
    {
        string? dir = Path.GetDirectoryName(jsonOut);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(jsonOut, JsonSerializer.Serialize(new
        {
            generatedAtUtc = DateTime.UtcNow.ToString("O"),
            root,
            pass,
            monsters = monsterSummaries,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"json={jsonOut}");
    }

    Console.WriteLine(pass ? "VERDICT: PASS" : "VERDICT: FAIL");
    return pass ? 0 : 1;

    bool EvaluateMonster(
        string monsterId,
        int minUnits,
        IReadOnlyList<string> requiredUnitIds,
        string requiredCapabilityLabel,
        IReadOnlyList<string> requiredText,
        out object summary)
    {
        summary = new
        {
            monster = monsterId,
            loadError = string.Empty,
            unitCount = 0,
            allPreviewReadOnly = false,
            capabilityLabelOk = false,
            unitIdsOk = false,
            textOk = false,
            unitIds = Array.Empty<string>(),
        };

        if (!TryLoadUnits(monsterId, out IReadOnlyList<AiIndirectDispatchUnit> units, out string error))
        {
            Console.WriteLine($"{monsterId} load               : FAIL ({error})");
            summary = new
            {
                monster = monsterId,
                loadError = error,
                unitCount = 0,
                allPreviewReadOnly = false,
                capabilityLabelOk = false,
                unitIdsOk = false,
                textOk = false,
                unitIds = Array.Empty<string>(),
            };
            return false;
        }

        bool countOk = units.Count >= minUnits;
        bool allPreviewReadOnly = units.Count > 0 && units.All(unit => unit.CapabilityTier == AiIndirectDispatchCapabilityTier.PreviewReadOnly);
        bool capabilityLabelOk = units.Any(unit => unit.CapabilityLabel.Contains(requiredCapabilityLabel, StringComparison.OrdinalIgnoreCase));
        bool unitIdsOk = requiredUnitIds.All(required =>
            units.Any(unit => unit.UnitId.Contains(required, StringComparison.OrdinalIgnoreCase)));
        bool textOk = requiredText.All(required =>
            units.Any(unit => UnitContainsText(unit, required)));

        Console.WriteLine($"{monsterId} units              : {(countOk ? "OK" : $"MISSING ({units.Count} < {minUnits})")}");
        Console.WriteLine($"{monsterId} preview-only       : {(allPreviewReadOnly ? "OK" : "MISSING")}");
        Console.WriteLine($"{monsterId} capability label   : {(capabilityLabelOk ? "OK" : $"MISSING ({requiredCapabilityLabel})")}");
        Console.WriteLine($"{monsterId} required unit ids  : {(unitIdsOk ? "OK" : $"MISSING ({string.Join(", ", requiredUnitIds)})")}");
        Console.WriteLine($"{monsterId} signature text     : {(textOk ? "OK" : $"MISSING ({string.Join(", ", requiredText)})")}");

        summary = new
        {
            monster = monsterId,
            loadError = string.Empty,
            unitCount = units.Count,
            allPreviewReadOnly,
            capabilityLabelOk,
            unitIdsOk,
            textOk,
            unitIds = units.Select(unit => unit.UnitId).ToArray(),
            labels = units.Select(unit => unit.CapabilityLabel).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        };

        return countOk && allPreviewReadOnly && capabilityLabelOk && unitIdsOk && textOk;
    }

    bool TryLoadUnits(string monsterId, out IReadOnlyList<AiIndirectDispatchUnit> units, out string error)
    {
        units = Array.Empty<AiIndirectDispatchUnit>();
        string? path = files.FirstOrDefault(p => string.Equals(Path.GetFileNameWithoutExtension(p), monsterId, StringComparison.OrdinalIgnoreCase));
        if (path == null)
        {
            error = $"{monsterId}.bin not found under {root}";
            return false;
        }

        byte[] monster = File.ReadAllBytes(path);
        byte[]? aiFile = AiScript_File.SliceAiFileFromMonster(monster);
        if (aiFile == null)
        {
            error = $"{monsterId} has no AiFile slice";
            return false;
        }

        try
        {
            AiScriptFile script = AiScript_File.Read(aiFile);
            units = AiAutomation.DetectIndirectDispatchUnits(monster, script);
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = $"{monsterId}: {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    static bool UnitContainsText(AiIndirectDispatchUnit unit, string text)
    {
        if (unit.UnitId.Contains(text, StringComparison.OrdinalIgnoreCase)) return true;
        if (unit.HookKind.Contains(text, StringComparison.OrdinalIgnoreCase)) return true;
        if (unit.GuardSummary.Contains(text, StringComparison.OrdinalIgnoreCase)) return true;
        if (unit.NextStateSummary.Contains(text, StringComparison.OrdinalIgnoreCase)) return true;
        if (unit.WarningSummary.Contains(text, StringComparison.OrdinalIgnoreCase)) return true;
        if (unit.CapabilityLabel.Contains(text, StringComparison.OrdinalIgnoreCase)) return true;
        if (unit.PayloadWrites.Any(write =>
                write.VariableName.Contains(text, StringComparison.OrdinalIgnoreCase)
                || write.RoleSummary.Contains(text, StringComparison.OrdinalIgnoreCase)
                || write.ValueSummary.Contains(text, StringComparison.OrdinalIgnoreCase)))
            return true;
        if (unit.Consumers.Any(consumer =>
                consumer.Label.Contains(text, StringComparison.OrdinalIgnoreCase)
                || consumer.CommandVariableName.Contains(text, StringComparison.OrdinalIgnoreCase)
                || consumer.TargetVariableName.Contains(text, StringComparison.OrdinalIgnoreCase)))
            return true;
        return unit.CompanionEffects.Any(note => note.Contains(text, StringComparison.OrdinalIgnoreCase));
    }
}

static int AdvancedRouteBundleRt0(IReadOnlyList<string> files, string root, string? jsonOut)
{
    const string SeymourId = "m124";
    const string UnitId = "dispatch-row-2";
    const string ConsumerLabel = "multi-cast #1";
    const ushort Firaga = 0x3049;
    const ushort SilenceField = 0x002E;
    const ushort SilenceValue = 255;

    string repoRoot = FindRepoRootShared();
    string routeOverlayPath = Path.Combine(
        repoRoot,
        "FFXProjectEditor",
        "Modules",
        "MonsterAiEditor",
        "MonsterAiEditor_DataModel.AdvancedRouteOverlay.cs");

    bool previewFocusSafetyOk = true;
    string previewFocusSafetyError = string.Empty;
    try
    {
        string routeOverlay = File.ReadAllText(routeOverlayPath);
        previewFocusSafetyOk =
            routeOverlay.Contains("SelectedAdvancedVariableFocusRow.EditorLaunchContext?.VariableName", StringComparison.Ordinal)
            && routeOverlay.Contains("?? SelectedPhaseVariableAudit?.VariableName", StringComparison.Ordinal)
            && routeOverlay.Contains("if (!string.IsNullOrWhiteSpace(variableName))", StringComparison.Ordinal);
        if (!previewFocusSafetyOk)
            previewFocusSafetyError = "preview-only focus rows no longer have a null-safe variable-name fallback in FindPreferredAdvancedRouteConsumer().";
    }
    catch (Exception ex)
    {
        previewFocusSafetyOk = false;
        previewFocusSafetyError = $"route-overlay source check failed: {ex.Message}";
    }

    bool riteOk = TryVerifyIndirectConsumerLinkedForbiddenRite(
        SeymourId,
        UnitId,
        ConsumerLabel,
        out string riteError);
    bool secondOk = TryVerifyIndirectConsumerSecondCast(
        SeymourId,
        UnitId,
        ConsumerLabel,
        Firaga,
        out string secondError);

    Console.WriteLine("=== AiScriptLab --advanced-route-bundle-rt0 ===");
    Console.WriteLine($"monster                 : {SeymourId}");
    Console.WriteLine($"consumer                : {UnitId} / {ConsumerLabel}");
    Console.WriteLine($"preview focus safety    : {(previewFocusSafetyOk ? "OK" : $"MISSING ({previewFocusSafetyError})")}");
    Console.WriteLine($"linked Forbidden Rite   : {(riteOk ? "OK" : $"MISSING ({riteError})")}");
    Console.WriteLine($"second cast after route : {(secondOk ? "OK" : $"MISSING ({secondError})")}");

    bool pass = previewFocusSafetyOk && riteOk && secondOk;

    if (!string.IsNullOrWhiteSpace(jsonOut))
    {
        string? dir = Path.GetDirectoryName(jsonOut);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(jsonOut, JsonSerializer.Serialize(new
        {
            generatedAtUtc = DateTime.UtcNow.ToString("O"),
            root,
            pass,
            monster = SeymourId,
            unitId = UnitId,
            consumer = ConsumerLabel,
            previewFocusSafety = new { pass = previewFocusSafetyOk, error = previewFocusSafetyError },
            linkedForbiddenRite = new { pass = riteOk, error = riteError },
            secondCast = new { pass = secondOk, error = secondError, operand = $"0x{Firaga:X4}" },
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"json={jsonOut}");
    }

    Console.WriteLine(pass ? "VERDICT: PASS" : "VERDICT: FAIL");
    return pass ? 0 : 1;

    bool ReparsesBundle(byte[] ai, AiScriptFile before, int wantWorkers, out string why)
    {
        why = string.Empty;
        try
        {
            AiScriptFile reread = AiScript_File.Read(ai);
            if (!reread.CodeWalkClosedExactly)
            {
                why = "walk did not close";
                return false;
            }

            if (reread.Workers.Count != wantWorkers)
            {
                why = $"worker count {reread.Workers.Count} != {wantWorkers}";
                return false;
            }

            if (!reread.UnknownOpcodes.SequenceEqual(before.UnknownOpcodes))
            {
                why =
                    $"unknown opcode drift [{string.Join(" ", reread.UnknownOpcodes.Select(b => b.ToString("X2")))}] != " +
                    $"[{string.Join(" ", before.UnknownOpcodes.Select(b => b.ToString("X2")))}]";
                return false;
            }

            if (reread.UnknownOpcodes.Count != 0)
                why = $"baseline unknowns preserved [{string.Join(" ", reread.UnknownOpcodes.Select(b => b.ToString("X2")))}]";

            return true;
        }
        catch (Exception ex)
        {
            why = $"Read threw {ex.GetType().Name}";
            return false;
        }
    }

    bool TryVerifyIndirectConsumerLinkedForbiddenRite(
        string monsterId,
        string unitId,
        string consumerLabel,
        out string error)
    {
        error = string.Empty;
        if (!TryLoadIndirectConsumerAction(
                monsterId,
                unitId,
                consumerLabel,
                out byte[] aiFile,
                out AiScriptFile script,
                out AiIndirectDispatchConsumer consumer,
                out AiDetectedAction action,
                out string loadError))
        {
            error = loadError;
            return false;
        }

        if (action.CommandIsLiteral || action.Removable)
        {
            error = "consumer indireto voltou como literal/removable; o teste precisa do shape PUSHV";
            return false;
        }

        int beforeBuffs = AiAutomation.DetectActions(script).Count(candidate => candidate.Kind == AiActionKind.Buff);
        List<AiInstruction>? changed = AiAutomation.InsertChrPropertyWriteAfterActionUsingActionTarget(
            script,
            action,
            SilenceField,
            SilenceValue);
        if (changed == null)
        {
            error = "insert write returned null";
            return false;
        }

        byte[] rebuilt = AiScript_File.Rebuild(script, changed);
        if (!ReparsesBundle(rebuilt, script, script.Workers.Count, out string why))
        {
            error = $"reparse failed: {why}";
            return false;
        }

        AiScriptFile reread = AiScript_File.Read(rebuilt);
        int afterBuffs = AiAutomation.DetectActions(reread).Count(candidate => candidate.Kind == AiActionKind.Buff);
        int callIndex = reread.Instructions.ToList().FindIndex(instruction => instruction.Offset == action.CallOffset);
        if (callIndex < 0 || callIndex + 4 >= reread.Instructions.Count)
        {
            error = "call offset missing in readback";
            return false;
        }

        bool linkedTarget = reread.Instructions[callIndex + 1].Opcode == 0x9F
                            && reread.Instructions[callIndex + 1].Operand == consumer.TargetVariableIndex;
        bool fieldOk = reread.Instructions[callIndex + 2].Opcode == 0xAE
                       && reread.Instructions[callIndex + 2].Operand == SilenceField;
        bool valueOk = reread.Instructions[callIndex + 3].Opcode == 0xAE
                       && reread.Instructions[callIndex + 3].Operand == SilenceValue;
        bool callOk = reread.Instructions[callIndex + 4].Opcode == 0xD8
                      && reread.Instructions[callIndex + 4].Operand == 0x7018;
        bool deltaOk = rebuilt.Length == aiFile.Length + 12;
        bool buffCountOk = afterBuffs == beforeBuffs + 1;
        if (linkedTarget && fieldOk && valueOk && callOk && deltaOk && buffCountOk)
            return true;

        error =
            $"readback target={linkedTarget} field={fieldOk} value={valueOk} call={callOk} delta={rebuilt.Length - aiFile.Length}/12 buffs={afterBuffs}/{beforeBuffs + 1}";
        return false;
    }

    bool TryVerifyIndirectConsumerSecondCast(
        string monsterId,
        string unitId,
        string consumerLabel,
        ushort secondCommandOperand,
        out string error)
    {
        error = string.Empty;
        if (!TryLoadIndirectConsumerAction(
                monsterId,
                unitId,
                consumerLabel,
                out byte[] aiFile,
                out AiScriptFile script,
                out AiIndirectDispatchConsumer consumer,
                out AiDetectedAction action,
                out string loadError))
        {
            error = loadError;
            return false;
        }

        if (action.CommandIsLiteral || action.Removable)
        {
            error = "consumer indireto voltou como literal/removable; o teste precisa do shape PUSHV";
            return false;
        }

        int beforeCommands = AiAutomation.DetectCommandActions(script).Count;
        List<AiInstruction>? changed = AiAutomation.InsertSecondCommandAfterActionUsingActionTarget(
            script,
            action,
            secondCommandOperand);
        if (changed == null)
        {
            error = "insert second returned null";
            return false;
        }

        byte[] rebuilt = AiScript_File.Rebuild(script, changed);
        if (!ReparsesBundle(rebuilt, script, script.Workers.Count, out string why))
        {
            error = $"reparse failed: {why}";
            return false;
        }

        AiScriptFile reread = AiScript_File.Read(rebuilt);
        int afterCommands = AiAutomation.DetectCommandActions(reread).Count;
        int callIndex = reread.Instructions.ToList().FindIndex(instruction => instruction.Offset == action.CallOffset);
        if (callIndex < 0 || callIndex + 3 >= reread.Instructions.Count)
        {
            error = "call offset missing in readback";
            return false;
        }

        bool linkedTarget = reread.Instructions[callIndex + 1].Opcode == 0x9F
                            && reread.Instructions[callIndex + 1].Operand == consumer.TargetVariableIndex;
        bool commandOk = reread.Instructions[callIndex + 2].Opcode == 0xAE
                         && reread.Instructions[callIndex + 2].Operand == secondCommandOperand;
        bool callOk = reread.Instructions[callIndex + 3].Opcode == 0xD8
                      && reread.Instructions[callIndex + 3].Operand == action.PerformOperand;
        bool deltaOk = rebuilt.Length == aiFile.Length + 9;
        bool commandCountOk = afterCommands == beforeCommands + 1;
        if (linkedTarget && commandOk && callOk && deltaOk && commandCountOk)
            return true;

        error =
            $"readback target={linkedTarget} command={commandOk} call={callOk} delta={rebuilt.Length - aiFile.Length}/9 commands={afterCommands}/{beforeCommands + 1}";
        return false;
    }

    bool TryLoadIndirectConsumerAction(
        string monsterId,
        string unitId,
        string consumerLabel,
        out byte[] aiFile,
        out AiScriptFile script,
        out AiIndirectDispatchConsumer consumer,
        out AiDetectedAction action,
        out string error)
    {
        aiFile = Array.Empty<byte>();
        script = null!;
        consumer = default!;
        action = default!;
        error = string.Empty;

        string? path = files.FirstOrDefault(p =>
            string.Equals(Path.GetFileNameWithoutExtension(p), monsterId, StringComparison.OrdinalIgnoreCase));
        if (path == null)
        {
            error = $"{monsterId}.bin not found under {root}";
            return false;
        }

        byte[] monster = File.ReadAllBytes(path);
        aiFile = AiScript_File.SliceAiFileFromMonster(monster) ?? Array.Empty<byte>();
        if (aiFile.Length == 0)
        {
            error = $"{monsterId} has no AiFile slice";
            return false;
        }

        try
        {
            script = AiScript_File.Read(aiFile);
        }
        catch (Exception ex)
        {
            error = $"{monsterId}: read threw {ex.GetType().Name}: {ex.Message}";
            return false;
        }

        AiIndirectDispatchUnit? unit = AiAutomation.DetectIndirectDispatchUnits(monster, script)
            .FirstOrDefault(candidate => candidate.UnitId.Equals(unitId, StringComparison.OrdinalIgnoreCase));
        if (unit == null)
        {
            error = $"{monsterId}: unit {unitId} missing";
            return false;
        }

        AiIndirectDispatchConsumer? selectedConsumer = unit.Consumers.FirstOrDefault(candidate =>
            candidate.Label.Equals(consumerLabel, StringComparison.OrdinalIgnoreCase));
        if (selectedConsumer == null)
        {
            error = $"{monsterId}: consumer '{consumerLabel}' missing";
            return false;
        }

        consumer = selectedConsumer;
        int consumerCallOffset = selectedConsumer.CallOffset;
        action = AiAutomation.DetectActions(script)
            .FirstOrDefault(candidate =>
                candidate.Kind == AiActionKind.Command
                && candidate.CallOffset == consumerCallOffset) ?? default!;
        if (action == null)
        {
            error = $"{monsterId}: action for consumer '{consumerLabel}' missing";
            return false;
        }

        return true;
    }
}

static int SeymourReaderRt0(IReadOnlyList<string> files, string root, string? jsonOut)
{
    const string SeymourId = "m124";
    string? path = files.FirstOrDefault(p => string.Equals(Path.GetFileNameWithoutExtension(p), SeymourId, StringComparison.OrdinalIgnoreCase));
    if (path == null)
    {
        Console.WriteLine("=== AiScriptLab --seymour-reader-rt0 ===");
        Console.WriteLine($"FAIL: {SeymourId}.bin not found under {root}");
        return 1;
    }

    byte[] monster = File.ReadAllBytes(path);
    byte[]? aiFile = AiScript_File.SliceAiFileFromMonster(monster);
    if (aiFile == null)
    {
        Console.WriteLine("=== AiScriptLab --seymour-reader-rt0 ===");
        Console.WriteLine($"FAIL: {SeymourId} has no AiFile slice");
        return 1;
    }

    AiScriptFile script;
    try { script = AiScript_File.Read(aiFile); }
    catch (Exception ex)
    {
        Console.WriteLine("=== AiScriptLab --seymour-reader-rt0 ===");
        Console.WriteLine($"FAIL: read threw {ex.GetType().Name}: {ex.Message}");
        return 1;
    }

    IReadOnlyList<AiDetectedAction> actions;
    try { actions = AiAutomation.DetectActions(script); }
    catch (Exception ex)
    {
        Console.WriteLine("=== AiScriptLab --seymour-reader-rt0 ===");
        Console.WriteLine($"FAIL: DetectActions threw {ex.GetType().Name}: {ex.Message}");
        return 1;
    }

    int[] expectedCalls = { 0x02BB, 0x0545, 0x055B, 0x0564, 0x0582 };
    var foundCalls = actions
        .Where(a => a.Kind == AiActionKind.Command && expectedCalls.Contains(a.CallOffset))
        .Select(a => a.CallOffset)
        .OrderBy(x => x)
        .ToArray();

    bool pass = expectedCalls.OrderBy(x => x).SequenceEqual(foundCalls);

    Console.WriteLine("=== AiScriptLab --seymour-reader-rt0 ===");
    Console.WriteLine($"monster      : {SeymourId}");
    Console.WriteLine($"actions found: {actions.Count}");
    Console.WriteLine("expected command call offsets:");
    foreach (int off in expectedCalls)
        Console.WriteLine($"  0x{off:X4} {(foundCalls.Contains(off) ? "OK" : "MISSING")}");

    if (!string.IsNullOrWhiteSpace(jsonOut))
    {
        string? dir = Path.GetDirectoryName(jsonOut);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(jsonOut, JsonSerializer.Serialize(new
        {
            generatedAtUtc = DateTime.UtcNow.ToString("O"),
            root,
            monster = SeymourId,
            expectedCalls = expectedCalls.Select(x => $"0x{x:X4}").ToArray(),
            foundCalls = foundCalls.Select(x => $"0x{x:X4}").ToArray(),
            pass,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"json={jsonOut}");
    }

    Console.WriteLine(pass ? "VERDICT: PASS" : "VERDICT: FAIL");
    return pass ? 0 : 1;
}

static int AiBranchReaderRt0(IReadOnlyList<string> files, string root, string? jsonOut)
{
    const string SeymourId = "m124";
    const string GuadoId = "m141";

    static bool TryLoadBranches(IReadOnlyList<string> files, string monsterId, out string? path, out byte[]? monsterBin, out AiScriptFile? script, out IReadOnlyList<AiDetectedBranchAction>? branches, out string error)
    {
        path = files.FirstOrDefault(p => string.Equals(Path.GetFileNameWithoutExtension(p), monsterId, StringComparison.OrdinalIgnoreCase));
        monsterBin = null;
        script = null;
        branches = null;
        error = string.Empty;

        if (path == null)
        {
            error = $"{monsterId}.bin not found";
            return false;
        }

        monsterBin = File.ReadAllBytes(path);
        byte[]? aiFile = AiScript_File.SliceAiFileFromMonster(monsterBin);
        if (aiFile == null)
        {
            error = $"{monsterId} has no AiFile slice";
            return false;
        }

        try
        {
            script = AiScript_File.Read(aiFile);
            branches = AiAutomation.DetectBranchSensitiveActions(monsterBin, script);
            return true;
        }
        catch (Exception ex)
        {
            error = $"{monsterId} threw {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    Console.WriteLine("=== AiScriptLab --ai-branch-reader-rt0 ===");

    if (!TryLoadBranches(files, SeymourId, out string? seymourPath, out _, out _, out IReadOnlyList<AiDetectedBranchAction>? seymourBranches, out string seymourError))
    {
        Console.WriteLine($"FAIL: {seymourError}");
        return 1;
    }
    IReadOnlyList<AiDetectedBranchAction> seymourBranchList = seymourBranches!;

    if (!TryLoadBranches(files, GuadoId, out string? guadoPath, out _, out _, out IReadOnlyList<AiDetectedBranchAction>? guadoBranches, out string guadoError))
    {
        Console.WriteLine($"FAIL: {guadoError}");
        return 1;
    }
    IReadOnlyList<AiDetectedBranchAction> guadoBranchList = guadoBranches!;

    bool hasSeymourShell = seymourBranchList.Any(b =>
        b.CallOffset == 0x02BB
        && b.HookKind == "onTurn real"
        && b.CommandSummary.Contains("303A", StringComparison.OrdinalIgnoreCase));

    int seymourIndirectCount = seymourBranchList.Count(b =>
        b.HookKind == "onTurn real"
        && (b.CallOffset == 0x0545 || b.CallOffset == 0x055B || b.CallOffset == 0x0564 || b.CallOffset == 0x0582));

    bool hasGuadoProtect = guadoBranchList.Any(b =>
        b.HookKind == "onTurn real"
        && b.CommandSummary.Contains("303B", StringComparison.OrdinalIgnoreCase));

    bool hasSupportToSeymour = guadoBranchList.Any(b =>
        b.TargetSummary.Contains("m124", StringComparison.OrdinalIgnoreCase)
        || b.TargetSummary.Contains("Seymour", StringComparison.OrdinalIgnoreCase));

    bool hasOnHitAutoPotion = guadoBranchList.Any(b =>
        b.HookKind == "onHit real"
        && b.CommandSummary.Contains("4010", StringComparison.OrdinalIgnoreCase));

    bool hasLabels = seymourBranchList.Any(b =>
        !string.IsNullOrWhiteSpace(b.HookKind)
        && !string.IsNullOrWhiteSpace(b.Confidence));

    bool hasPathLocalProvenance = seymourBranchList.Any(b =>
        !string.IsNullOrWhiteSpace(b.CommandProvenance)
        && !string.IsNullOrWhiteSpace(b.TargetProvenance));

    bool hasDamageGatedTransition = seymourBranchList.Any(b =>
        b.CallOffset == 0x0980
        && b.HookKind == "onHit real"
        && b.HighLevelHints.Contains("damage-gated transition", StringComparer.OrdinalIgnoreCase));

    bool hasPhaseStateMachine = seymourBranchList.Any(b =>
        b.CallOffset == 0x0980
        && b.HookKind == "evento auxiliar"
        && b.HighLevelHints.Contains("entrypoint auxiliar contextual", StringComparer.OrdinalIgnoreCase)
        && b.HighLevelHints.Contains("phase-state machine", StringComparer.OrdinalIgnoreCase));

    bool hasPhaseHandoffLabel = seymourBranchList.Any(b =>
        b.CallOffset == 0x0980
        && b.HookKind == "evento auxiliar"
        && b.HighLevelHints.Contains("handoff de fase", StringComparer.OrdinalIgnoreCase));

    bool hasPresentationHandoffLabel = seymourBranchList.Any(b =>
        b.CallOffset == 0x0980
        && b.HookKind == "evento auxiliar"
        && b.HighLevelHints.Contains("apresentacao/handoff", StringComparer.OrdinalIgnoreCase));

    Console.WriteLine($"Seymour path     : {seymourPath}");
    Console.WriteLine($"Seymour branches : {seymourBranchList.Count}");
    Console.WriteLine($"Guado path       : {guadoPath}");
    Console.WriteLine($"Guado branches   : {guadoBranchList.Count}");
    Console.WriteLine($"Seymour Shell opener @0x02BB : {(hasSeymourShell ? "OK" : "MISSING")}");
    Console.WriteLine($"Seymour indirect split count : {seymourIndirectCount} {(seymourIndirectCount >= 4 ? "OK" : "LOW")}");
    Console.WriteLine($"Guado Protect opener         : {(hasGuadoProtect ? "OK" : "MISSING")}");
    Console.WriteLine($"Guado support to Seymour     : {(hasSupportToSeymour ? "OK" : "MISSING")}");
    Console.WriteLine($"Guado onHit Auto-Potion      : {(hasOnHitAutoPotion ? "OK" : "MISSING")}");
    Console.WriteLine($"Editor labels/confidence     : {(hasLabels ? "OK" : "MISSING")}");
    Console.WriteLine($"Path-local provenance        : {(hasPathLocalProvenance ? "OK" : "MISSING")}");
    Console.WriteLine($"Damage-gated transition      : {(hasDamageGatedTransition ? "OK" : "MISSING")}");
    Console.WriteLine($"Phase-state machine          : {(hasPhaseStateMachine ? "OK" : "MISSING")}");
    Console.WriteLine($"Phase handoff label          : {(hasPhaseHandoffLabel ? "OK" : "MISSING")}");
    Console.WriteLine($"Presentation/handoff label   : {(hasPresentationHandoffLabel ? "OK" : "MISSING")}");

    bool pass = hasSeymourShell
        && seymourIndirectCount >= 4
        && hasGuadoProtect
        && hasSupportToSeymour
        && hasOnHitAutoPotion
        && hasLabels
        && hasPathLocalProvenance
        && hasDamageGatedTransition
        && hasPhaseStateMachine
        && hasPhaseHandoffLabel
        && hasPresentationHandoffLabel;

    if (!string.IsNullOrWhiteSpace(jsonOut))
    {
        string? dir = Path.GetDirectoryName(jsonOut);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(jsonOut, JsonSerializer.Serialize(new
        {
            generatedAtUtc = DateTime.UtcNow.ToString("O"),
            root,
            Seymour = new
            {
                path = seymourPath,
                branchCount = seymourBranchList.Count,
                hasShellOpener = hasSeymourShell,
                indirectCount = seymourIndirectCount,
                branches = seymourBranchList.Select(b => new
                {
                    b.HookKind,
                    callOffset = $"0x{b.CallOffset:X4}",
                    b.Confidence,
                    b.GuardSummary,
                    b.TargetSummary,
                    b.CommandSummary,
                    b.TargetProvenance,
                    b.CommandProvenance,
                    b.PathId,
                    b.HighLevelHints,
                }).ToArray(),
            },
            Guado = new
            {
                path = guadoPath,
                branchCount = guadoBranchList.Count,
                hasProtectOpener = hasGuadoProtect,
                hasSupportToSeymour,
                hasOnHitAutoPotion,
                branches = guadoBranchList.Select(b => new
                {
                    b.HookKind,
                    callOffset = $"0x{b.CallOffset:X4}",
                    b.Confidence,
                    b.GuardSummary,
                    b.TargetSummary,
                    b.CommandSummary,
                    b.TargetProvenance,
                    b.CommandProvenance,
                    b.PathId,
                    b.HighLevelHints,
                }).ToArray(),
            },
            pass,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"json={jsonOut}");
    }

    Console.WriteLine(pass ? "VERDICT: PASS" : "VERDICT: FAIL");
    return pass ? 0 : 1;
}

// ============================================================================================================
// TARGET SCAN (--targets, RESEARCH for #8). Over every performCommand/forcePerformCommand site, look at the
// instruction BEFORE the command-id push (the target slot) and histogram its opcode + literal value. Reveals the
// target sentinel landscape (self 0xFFF3, all-enemies, all-allies, single, random, var-driven). Read-only.
// ============================================================================================================
static int TargetScan(List<string> files, string? jsonOut)
{
    const byte CALLPOPA = 0xD8, PUSHII = 0xAE, PUSHI = 0xAD, PUSHV = 0x9F;
    const ushort PerformCommand = 0x700B, ForcePerformCommand = 0x705A;
    static bool IsCmd(ushort op) { ushort c = (ushort)(op & 0xF000); return c == 0x3000 || c == 0x4000 || c == 0x6000; }

    int sites = 0, forceSites = 0, performSites = 0, selfTargets = 0;
    var opcodeHist = new SortedDictionary<byte, int>();
    var litHist = new Dictionary<ushort, int>();
    var varHist = new Dictionary<ushort, int>();
    var cmdTargets = new Dictionary<ushort, HashSet<ushort>>();   // command -> distinct literal targets (single vs all)

    foreach (string path in files)
    {
        byte[] monster = File.ReadAllBytes(path);
        byte[]? aiFile = AiScript_File.SliceAiFileFromMonster(monster);
        if (aiFile is null) continue;
        AiScriptFile s; try { s = AiScript_File.Read(aiFile); } catch { continue; }
        if (!s.HasScript) continue;
        var ins = s.Instructions;
        for (int i = 2; i < ins.Count; i++)
        {
            var call = ins[i];
            if (call.Opcode != CALLPOPA) continue;
            if (call.Operand != PerformCommand && call.Operand != ForcePerformCommand) continue;
            var cmd = ins[i - 1];
            if (cmd.Opcode != PUSHII || !IsCmd(cmd.Operand)) continue;
            sites++;
            if (call.Operand == ForcePerformCommand) forceSites++; else performSites++;
            var tgt = ins[i - 2];
            opcodeHist[tgt.Opcode] = opcodeHist.GetValueOrDefault(tgt.Opcode) + 1;
            if (tgt.Opcode == PUSHII || tgt.Opcode == PUSHI)
            {
                litHist[tgt.Operand] = litHist.GetValueOrDefault(tgt.Operand) + 1;
                if (tgt.Operand == 0xFFF3) selfTargets++;
                if (!cmdTargets.TryGetValue(cmd.Operand, out var set)) cmdTargets[cmd.Operand] = set = new();
                set.Add(tgt.Operand);
            }
            else if (tgt.Opcode == PUSHV) varHist[tgt.Operand] = varHist.GetValueOrDefault(tgt.Operand) + 1;
        }
    }

    var sb = new StringBuilder();
    sb.AppendLine("=== AiScriptLab --targets scan (#8 research: the target slot before performCommand) ===");
    sb.AppendLine($"command sites       : {sites}  (perform {performSites} / force {forceSites})");
    sb.AppendLine($"self (0xFFF3) target: {selfTargets}");
    sb.AppendLine("target-slot opcode histogram (the push feeding the target):");
    foreach (var kv in opcodeHist.OrderByDescending(k => k.Value))
        sb.AppendLine($"  {AiScript_File.Mnemonic(kv.Key)} (0x{kv.Key:X2}) : {kv.Value}");
    sb.AppendLine("top literal target values (hex · signed int16 · count):");
    foreach (var kv in litHist.OrderByDescending(k => k.Value).Take(40))
        sb.AppendLine($"  0x{kv.Key:X4}  {(short)kv.Key,6}  : {kv.Value}");
    sb.AppendLine("commands used with the most DISTINCT literal targets (single vs all of the same ability):");
    foreach (var kv in cmdTargets.Where(c => c.Value.Count > 1).OrderByDescending(c => c.Value.Count).Take(20))
    {
        var dec = AiCommandId.Decode(kv.Key);
        sb.AppendLine($"  0x{kv.Key:X4} {(dec.IsKnown ? dec.Name : "?"),-22} : {kv.Value.Count} targets [{string.Join(" ", kv.Value.OrderBy(x => x).Select(x => ((short)x).ToString()))}]");
    }
    Console.WriteLine(sb.ToString());

    if (jsonOut != null)
    {
        var o = new
        {
            sites, performSites, forceSites, selfTargets,
            opcodeHist = opcodeHist.ToDictionary(k => $"0x{k.Key:X2}", v => v.Value),
            topLiterals = litHist.OrderByDescending(k => k.Value).Take(80).ToDictionary(k => $"0x{k.Key:X4}", v => v.Value),
            varTargets = varHist.OrderByDescending(k => k.Value).Take(40).ToDictionary(k => $"v{k.Key}", v => v.Value),
        };
        File.WriteAllText(jsonOut, JsonSerializer.Serialize(o, new JsonSerializerOptions { WriteIndented = true }));
    }
    return 0;
}

// ============================================================================================================
// NAME AUDIT (--names). Histogram every native call id and each function-specific field space, then report only
// the ids that still fall back to hex. Also counts saveData (storage 0x54) declarations/references.
// Read-only: this is evidence for display labels and cannot change any AiFile byte.
// ============================================================================================================
static int NameAuditScan(List<string> files, string? jsonOut)
{
    const byte CALL = 0xB5, CALLPOPA = 0xD8, PUSHII = 0xAE;
    var fieldBack = new Dictionary<ushort, int>
    {
        [0x700F] = 1, [0x7018] = 2, [0x70AA] = 1, [0x70AB] = 2,
        [0x70AC] = 1, [0x70B2] = 2, [0x701A] = 1, [0x7078] = 1,
    };

    var callHist = new Dictionary<ushort, int>();
    var unknownCallHist = new Dictionary<ushort, int>();
    var fieldHist = new Dictionary<(ushort func, ushort field), int>();
    var unknownFieldHist = new Dictionary<(ushort func, ushort field), int>();
    var calculatedFields = new Dictionary<ushort, int>();
    var saveDeclared = new Dictionary<ushort, int>();
    var saveReferenced = new Dictionary<ushort, int>();

    static string? FieldName(ushort funcId, ushort fieldId) => funcId switch
    {
        0x700F or 0x7018 or 0x70AA or 0x70AB => AiChrPropertyNames.Get(fieldId),
        0x70AC or 0x70B2 => AiMotionPropertyNames.Get(fieldId),
        0x701A or 0x7078 => AiMovePropertyNames.Get(fieldId),
        _ => null,
    };

    foreach (string path in files)
    {
        byte[] monster = File.ReadAllBytes(path);
        byte[]? aiFile = AiScript_File.SliceAiFileFromMonster(monster);
        if (aiFile is null) continue;
        AiScriptFile s;
        try { s = AiScript_File.Read(aiFile); }
        catch { continue; }

        foreach (AiVariable variable in s.Variables)
            if (variable.Storage == 0x54)
                saveDeclared[(ushort)variable.Slot] = saveDeclared.GetValueOrDefault((ushort)variable.Slot) + 1;

        IReadOnlyList<AiInstruction> ins = s.Instructions;
        for (int i = 0; i < ins.Count; i++)
        {
            AiInstruction cur = ins[i];
            if ((cur.OperandKind == AiOperandKind.VarLoad || cur.OperandKind == AiOperandKind.VarStore)
                && cur.Operand < s.Variables.Count)
            {
                AiVariable variable = s.Variables[cur.Operand];
                if (variable.Storage == 0x54)
                    saveReferenced[(ushort)variable.Slot] = saveReferenced.GetValueOrDefault((ushort)variable.Slot) + 1;
            }

            if (cur.Opcode != CALL && cur.Opcode != CALLPOPA) continue;
            callHist[cur.Operand] = callHist.GetValueOrDefault(cur.Operand) + 1;
            if (AiScript_File.CallName(cur.Operand) == $"{cur.Operand:X4}h")
                unknownCallHist[cur.Operand] = unknownCallHist.GetValueOrDefault(cur.Operand) + 1;

            if (!fieldBack.TryGetValue(cur.Operand, out int back) || i - back < 0) continue;
            AiInstruction fieldPush = ins[i - back];
            if (fieldPush.Opcode != PUSHII)
            {
                calculatedFields[cur.Operand] = calculatedFields.GetValueOrDefault(cur.Operand) + 1;
                continue;
            }

            var key = (func: cur.Operand, field: fieldPush.Operand);
            fieldHist[key] = fieldHist.GetValueOrDefault(key) + 1;
            if (FieldName(key.func, key.field) == null)
                unknownFieldHist[key] = unknownFieldHist.GetValueOrDefault(key) + 1;
        }
    }

    var sb = new StringBuilder();
    sb.AppendLine("=== AiScriptLab --names (display-name audit; read-only) ===");
    sb.AppendLine($"distinct call ids used : {callHist.Count}");
    sb.AppendLine($"unnamed call ids used  : {unknownCallHist.Count}");
    foreach (var kv in unknownCallHist.OrderByDescending(k => k.Value).ThenBy(k => k.Key))
        sb.AppendLine($"  0x{kv.Key:X4} : {kv.Value}");

    sb.AppendLine($"distinct named-space fields used : {fieldHist.Count}");
    sb.AppendLine($"unnamed literal fields used      : {unknownFieldHist.Count}");
    foreach (var kv in unknownFieldHist.OrderByDescending(k => k.Value).ThenBy(k => k.Key.func).ThenBy(k => k.Key.field))
        sb.AppendLine($"  func 0x{kv.Key.func:X4} field 0x{kv.Key.field:X4} : {kv.Value}");
    foreach (var kv in calculatedFields.OrderBy(k => k.Key))
        sb.AppendLine($"  func 0x{kv.Key:X4} calculated field operand: {kv.Value}");

    sb.AppendLine($"saveData slots declared/referenced: {saveDeclared.Count}/{saveReferenced.Count}");
    foreach (ushort slot in saveDeclared.Keys.Union(saveReferenced.Keys).OrderBy(x => x))
    {
        string name = AiSaveDataVariableNames.Get(slot) ?? "(hex only)";
        sb.AppendLine($"  0x{slot:X4} {name,-48} declared {saveDeclared.GetValueOrDefault(slot),3} referenced {saveReferenced.GetValueOrDefault(slot),4}");
    }
    Console.WriteLine(sb.ToString());

    if (jsonOut != null)
    {
        var o = new
        {
            distinctCallIds = callHist.Count,
            unnamedCalls = unknownCallHist.OrderByDescending(k => k.Value)
                .ToDictionary(k => $"0x{k.Key:X4}", v => v.Value),
            distinctFields = fieldHist.Count,
            unnamedFields = unknownFieldHist.OrderByDescending(k => k.Value)
                .ToDictionary(k => $"0x{k.Key.func:X4}/0x{k.Key.field:X4}", v => v.Value),
            calculatedFields = calculatedFields.ToDictionary(k => $"0x{k.Key:X4}", v => v.Value),
            saveData = saveDeclared.Keys.Union(saveReferenced.Keys).OrderBy(x => x).ToDictionary(
                slot => $"0x{slot:X4} {AiSaveDataVariableNames.Get(slot) ?? "(hex only)"}",
                slot => new { declared = saveDeclared.GetValueOrDefault(slot), referenced = saveReferenced.GetValueOrDefault(slot) }),
        };
        File.WriteAllText(jsonOut, JsonSerializer.Serialize(o, new JsonSerializerOptions { WriteIndented = true }));
    }
    return 0;
}

// ============================================================================================================
// CONDITION SCAN (--cond, RESEARCH for #11/#12). Histogram the native CALL/CALLPOPA function ids that appear in
// the window BEFORE a conditional branch (D6/D7), to surface the HP%/turn-count getters a corpus boss already uses
// — so the conditional templates can mirror a PROVEN idiom rather than guess an opcode. Read-only.
// ============================================================================================================
static int CondScan(List<string> files, string? jsonOut)
{
    const byte CALL = 0xB5, CALLPOPA = 0xD8, JMPC = 0xD6, JMPNC = 0xD7;
    static string FuncName(ushort funcId)
    {
        var instr = new AiInstruction { Offset = -1, Opcode = CALL, HasOperand = true, Operand = funcId, OperandKind = AiOperandKind.FuncId };
        try { return AiScript_File.OperandGloss(instr); } catch { return "?"; }
    }

    const byte PUSHII = 0xAE;
    const ushort ReadChrProperty = 0x700F;
    int branches = 0;
    var nearBranch = new Dictionary<ushort, int>();   // func id -> times within 14 instrs before a branch
    var allCalls = new Dictionary<ushort, int>();      // func id -> total value-returning CALL uses
    var rcpField = new Dictionary<ushort, int>();      // readChrProperty: the field id (push right before the CALL)

    foreach (string path in files)
    {
        byte[] monster = File.ReadAllBytes(path);
        byte[]? aiFile = AiScript_File.SliceAiFileFromMonster(monster);
        if (aiFile is null) continue;
        AiScriptFile s; try { s = AiScript_File.Read(aiFile); } catch { continue; }
        if (!s.HasScript) continue;
        var ins = s.Instructions;
        for (int i = 0; i < ins.Count; i++)
        {
            if (ins[i].Opcode == CALL) allCalls[ins[i].Operand] = allCalls.GetValueOrDefault(ins[i].Operand) + 1;
            // readChrProperty pops field FIRST (the LAST push) -> field = the PUSHII right before the CALL.
            if (ins[i].Opcode == CALL && ins[i].Operand == ReadChrProperty && i >= 1 && ins[i - 1].Opcode == PUSHII)
                rcpField[ins[i - 1].Operand] = rcpField.GetValueOrDefault(ins[i - 1].Operand) + 1;
            if (ins[i].Opcode != JMPC && ins[i].Opcode != JMPNC) continue;
            branches++;
            var seen = new HashSet<ushort>();
            for (int k = i - 1; k >= 0 && k >= i - 14; k--)
            {
                if (ins[k].Opcode == JMPC || ins[k].Opcode == JMPNC) break;   // stop at the previous branch
                if ((ins[k].Opcode == CALL || ins[k].Opcode == CALLPOPA) && seen.Add(ins[k].Operand))
                    nearBranch[ins[k].Operand] = nearBranch.GetValueOrDefault(ins[k].Operand) + 1;
            }
        }
    }

    var sb = new StringBuilder();
    sb.AppendLine("=== AiScriptLab --cond scan (#11/#12 research: native calls feeding conditional branches) ===");
    sb.AppendLine($"conditional branches (D6/D7): {branches}");
    sb.AppendLine("func ids most often within 14 instrs BEFORE a branch (id · name · count):");
    foreach (var kv in nearBranch.OrderByDescending(k => k.Value).Take(45))
        sb.AppendLine($"  0x{kv.Key:X4}  {FuncName(kv.Key),-30} : {kv.Value}");
    sb.AppendLine($"readChrProperty(0x700F) FIELD ids used (field hex · dec · count) — #11 HP% probe (IDA: field 0xDA/218 = HP*256/max):");
    foreach (var kv in rcpField.OrderByDescending(k => k.Value).Take(25))
        sb.AppendLine($"  0x{kv.Key:X2}  {kv.Key,4}  : {kv.Value}");
    int RcpF(ushort f) => rcpField.GetValueOrDefault(f);
    sb.AppendLine($"  -- HP-gauge fields present? 218(0xDA HP%)={RcpF(218)} 219(0xDB MP%)={RcpF(219)} 251(0xFB)={RcpF(251)} 281={RcpF(281)} 283={RcpF(283)} 284={RcpF(284)}");
    Console.WriteLine(sb.ToString());

    if (jsonOut != null)
    {
        var o = new
        {
            branches,
            nearBranch = nearBranch.OrderByDescending(k => k.Value).Take(80)
                .ToDictionary(k => $"0x{k.Key:X4} {FuncName(k.Key)}", v => v.Value),
            allCalls = allCalls.OrderByDescending(k => k.Value).Take(80)
                .ToDictionary(k => $"0x{k.Key:X4} {FuncName(k.Key)}", v => v.Value),
        };
        File.WriteAllText(jsonOut, JsonSerializer.Serialize(o, new JsonSerializerOptions { WriteIndented = true }));
    }
    return 0;
}

static int RoundScriptedBossWriterRt0(IReadOnlyList<string> files, string root, string? jsonOut)
{
    Console.WriteLine("=== AiScriptLab --round-scripted-boss-writer-rt0 ===");

    string? path = files.FirstOrDefault(candidate =>
        string.Equals(Path.GetFileNameWithoutExtension(candidate), "m238", StringComparison.OrdinalIgnoreCase));
    if (path == null)
    {
        Console.WriteLine("m238 load               : FAIL (file not found)");
        return 1;
    }

    byte[] monster = File.ReadAllBytes(path);
    byte[]? aiFile = AiScript_File.SliceAiFileFromMonster(monster);
    if (aiFile == null)
    {
        Console.WriteLine("m238 ai slice           : FAIL");
        return 1;
    }

    AiScriptFile script;
    try
    {
        script = AiScript_File.Read(aiFile);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"m238 parse              : FAIL ({ex.GetType().Name}: {ex.Message})");
        return 1;
    }

    if (!AiRoundScriptedBossWriter.TryBuildDescriptors(script, out IReadOnlyList<AiRoundScriptedBossDescriptor> baseline, out string detectError))
    {
        Console.WriteLine($"m238 descriptors        : FAIL ({detectError})");
        return 1;
    }

    AiRoundScriptedBossDescriptor landing = baseline.First(d => d.BeatName == "preview-round-landing");
    bool baselineOk = landing.CurrentCommand == 0x4019 && baseline.Count >= 2;
    Console.WriteLine($"baseline shape          : {(baselineOk ? "OK" : "MISMATCH")} ({baseline.Count} beats, landing=0x{landing.CurrentCommand:X4})");

    var landingRequest = new AiRoundScriptedBossPatchRequest(
        landing.BeatName,
        landing.CommandInstructionOffset,
        landing.CurrentCommand,
        0x401A,
        landing.LandingRepriseOffset,
        landing.CurrentLandingReprise,
        landing.CurrentLandingReprise.HasValue ? (ushort)0x4016 : null);

    bool landingPatchOk =
        AiRoundScriptedBossWriter.TryApplyPatch(script, landingRequest, out AiRoundScriptedBossEditResult? landingResult, out string landingError)
        && landingResult != null;
    Console.WriteLine($"landing patch           : {(landingPatchOk ? "OK" : $"FAIL ({landingError})")}");
    if (!landingPatchOk || landingResult == null)
        return 1;

    bool landingValid = AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(
        landingResult.EditedAiFileBytes,
        script,
        aiFile.Length,
        out _,
        out string landingValidWhy);
    Console.WriteLine($"landing validate        : {(landingValid ? "OK" : $"FAIL ({landingValidWhy})")}");
    if (!landingValid)
        return 1;

    AiScriptFile patchedScript = AiScript_File.Read(landingResult.EditedAiFileBytes);
    bool rereadOk =
        AiRoundScriptedBossWriter.TryBuildDescriptors(patchedScript, out IReadOnlyList<AiRoundScriptedBossDescriptor> reread, out string rereadError)
        && reread.First(d => d.BeatName == "preview-round-landing").CurrentCommand == 0x401A;
    if (!rereadOk)
    {
        AiInstruction? patchedLanding = patchedScript.Instructions.FirstOrDefault(i => i.Offset == landing.CommandInstructionOffset);
        rereadOk = patchedLanding != null && patchedLanding.Operand == 0x401A;
        rereadError = rereadOk ? "(descriptor rebuild skipped, byte-level check OK)" : rereadError;
    }
    Console.WriteLine($"reread descriptors      : {(rereadOk ? "OK" : $"FAIL ({rereadError})")}");
    if (!rereadOk)
        return 1;

    byte[] splicedMonster = AiScript_File.SpliceAiFileIntoMonster(monster, landingResult.EditedAiFileBytes);
    byte[]? slicedBack = AiScript_File.SliceAiFileFromMonster(splicedMonster);
    bool spliceRoundTripOk = slicedBack != null && slicedBack.SequenceEqual(landingResult.EditedAiFileBytes);
    Console.WriteLine($"splice round-trip       : {(spliceRoundTripOk ? "OK" : "FAIL")}");
    if (!spliceRoundTripOk)
        return 1;

    var restoreRequest = new AiRoundScriptedBossPatchRequest(
        landing.BeatName,
        landing.CommandInstructionOffset,
        0x401A,
        landing.CurrentCommand,
        landing.LandingRepriseOffset,
        landing.CurrentLandingReprise.HasValue ? (ushort)0x4016 : null,
        landing.CurrentLandingReprise);

    bool restoreOk =
        AiRoundScriptedBossWriter.TryApplyPatch(patchedScript, restoreRequest, out AiRoundScriptedBossEditResult? restoreResult, out string restoreError)
        && restoreResult != null;
    Console.WriteLine($"restore patch           : {(restoreOk ? "OK" : $"FAIL ({restoreError})")}");
    if (!restoreOk || restoreResult == null)
        return 1;

    bool restoreByteIdentity = restoreResult.EditedAiFileBytes.SequenceEqual(aiFile);
    Console.WriteLine($"restore byte-identity   : {(restoreByteIdentity ? "OK" : "FAIL")}");

    bool pass = baselineOk && landingPatchOk && landingValid && rereadOk && spliceRoundTripOk && restoreOk && restoreByteIdentity;

    if (!string.IsNullOrWhiteSpace(jsonOut))
    {
        string? dir = Path.GetDirectoryName(jsonOut);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(jsonOut, JsonSerializer.Serialize(new
        {
            generatedAtUtc = DateTime.UtcNow.ToString("O"),
            root,
            monster = "m238",
            pass,
            baseline = new { beatCount = baseline.Count, landingCommand = $"0x{landing.CurrentCommand:X4}" },
            patched = new { landingCommand = "0x401A", changedBytes = landingResult.ChangedBytes.Count },
            restored = new { byteIdentity = restoreByteIdentity },
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"json={jsonOut}");
    }

    Console.WriteLine(pass ? "VERDICT: PASS" : "VERDICT: FAIL");
    return pass ? 0 : 1;
}

static int AnimaOdThresholdWriterRt0(IReadOnlyList<string> files, string root, string? jsonOut)
{
    Console.WriteLine("=== AiScriptLab --anima-od-threshold-writer-rt0 ===");
    string? path = files.FirstOrDefault(c => string.Equals(Path.GetFileNameWithoutExtension(c), "m125", StringComparison.OrdinalIgnoreCase));
    if (path == null) { Console.WriteLine("m125 load               : FAIL (file not found)"); return 1; }
    byte[] monster = File.ReadAllBytes(path);
    byte[]? aiFile = AiScript_File.SliceAiFileFromMonster(monster);
    if (aiFile == null) { Console.WriteLine("m125 ai slice           : FAIL"); return 1; }
    AiScriptFile script;
    try { script = AiScript_File.Read(aiFile); } catch (Exception ex) { Console.WriteLine($"m125 parse              : FAIL ({ex.Message})"); return 1; }
    if (!AiAnimaOdThresholdWriter.TryBuildDescriptors(script, out IReadOnlyList<AiAnimaOdThresholdDescriptor> baseline, out string detectError))
    { Console.WriteLine($"m125 descriptors        : FAIL ({detectError})"); return 1; }
    bool baselineOk = baseline.Count > 0;
    Console.WriteLine($"m125 baseline           : {(baselineOk ? "OK" : "MISMATCH")} ({baseline.Count} descriptor(s))");
    if (!baselineOk) return 1;
    var d = baseline.First();
    var patchReq = new AiAnimaOdThresholdPatchRequest(d.MaximumInstructionOffset, d.CurrentMaximum, (ushort)(d.CurrentMaximum == 100 ? 99 : d.CurrentMaximum + 1));
    if (!AiAnimaOdThresholdWriter.TryApplyPatch(script, patchReq, out AiAnimaOdThresholdEditResult? patchResult, out string patchError) || patchResult == null)
    { Console.WriteLine($"m125 patch             : FAIL ({patchError})"); return 1; }
    bool patchValid = AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(patchResult.EditedAiFileBytes, script, aiFile.Length, out _, out string patchValidWhy);
    Console.WriteLine($"m125 validate           : {(patchValid ? "OK" : $"FAIL ({patchValidWhy})")}");
    if (!patchValid) return 1;
    byte[] splicedMonster = AiScript_File.SpliceAiFileIntoMonster(monster, patchResult.EditedAiFileBytes);
    byte[]? slicedBack = AiScript_File.SliceAiFileFromMonster(splicedMonster);
    bool spliceOk = slicedBack != null && slicedBack.SequenceEqual(patchResult.EditedAiFileBytes);
    Console.WriteLine($"m125 splice round-trip  : {(spliceOk ? "OK" : "FAIL")}");
    if (!spliceOk) return 1;
    AiScriptFile patchedScript = AiScript_File.Read(patchResult.EditedAiFileBytes);
    var restoreReq = new AiAnimaOdThresholdPatchRequest(d.MaximumInstructionOffset, (ushort)(d.CurrentMaximum == 100 ? 99 : d.CurrentMaximum + 1), d.CurrentMaximum);
    if (!AiAnimaOdThresholdWriter.TryApplyPatch(patchedScript, restoreReq, out AiAnimaOdThresholdEditResult? restoreResult, out string restoreError) || restoreResult == null)
    { Console.WriteLine($"m125 restore           : FAIL ({restoreError})"); return 1; }
    bool restoreByteIdentity = restoreResult.EditedAiFileBytes.SequenceEqual(aiFile);
    Console.WriteLine($"m125 restore identity   : {(restoreByteIdentity ? "OK" : "FAIL")}");
    bool pass = baselineOk && patchValid && spliceOk && restoreByteIdentity;
    Console.WriteLine($"m125 Anima OD threshold : {(pass ? "PASS" : "FAIL")}");
    Console.WriteLine(pass ? "VERDICT: PASS" : "VERDICT: FAIL");
    return pass ? 0 : 1;
}

static int OmnisClusterWriterRt0(IReadOnlyList<string> files, string root, string? jsonOut)
{
    Console.WriteLine("=== AiScriptLab --omnis-cluster-writer-rt0 ===");
    string? path = files.FirstOrDefault(c => string.Equals(Path.GetFileNameWithoutExtension(c), "m131", StringComparison.OrdinalIgnoreCase));
    if (path == null) { Console.WriteLine("m131 load               : FAIL (file not found)"); return 1; }
    byte[] monster = File.ReadAllBytes(path);
    byte[]? aiFile = AiScript_File.SliceAiFileFromMonster(monster);
    if (aiFile == null) { Console.WriteLine("m131 ai slice           : FAIL"); return 1; }
    AiScriptFile script;
    try { script = AiScript_File.Read(aiFile); } catch (Exception ex) { Console.WriteLine($"m131 parse              : FAIL ({ex.Message})"); return 1; }
    if (!AiOmnisClusterWriter.TryBuildDescriptors(script, out IReadOnlyList<AiOmnisClusterDescriptor> baseline, out string detectError))
    { Console.WriteLine($"m131 descriptors        : FAIL ({detectError})"); return 1; }
    bool baselineOk = baseline.Count > 0;
    Console.WriteLine($"m131 baseline           : {(baselineOk ? "OK" : "MISMATCH")} ({baseline.Count} descriptor(s))");
    if (!baselineOk) return 1;
    var d = baseline.First();
    ushort swap = d.CurrentCommand == 0x3045 ? (ushort)0x3046 : (ushort)0x3045;
    var patchReq = new AiOmnisClusterPatchRequest(d.BeatName, d.CommandInstructionOffset, d.CurrentCommand, swap, d.StateWriteOffset, d.CurrentStateValue, d.CurrentStateValue);
    if (!AiOmnisClusterWriter.TryApplyPatch(script, patchReq, out AiOmnisClusterEditResult? patchResult, out string patchError) || patchResult == null)
    { Console.WriteLine($"m131 patch             : FAIL ({patchError})"); return 1; }
    bool patchValid = AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(patchResult.EditedAiFileBytes, script, aiFile.Length, out _, out string patchValidWhy);
    Console.WriteLine($"m131 validate           : {(patchValid ? "OK" : $"FAIL ({patchValidWhy})")}");
    if (!patchValid) return 1;
    byte[] splicedMonster = AiScript_File.SpliceAiFileIntoMonster(monster, patchResult.EditedAiFileBytes);
    byte[]? slicedBack = AiScript_File.SliceAiFileFromMonster(splicedMonster);
    bool spliceOk = slicedBack != null && slicedBack.SequenceEqual(patchResult.EditedAiFileBytes);
    Console.WriteLine($"m131 splice round-trip  : {(spliceOk ? "OK" : "FAIL")}");
    if (!spliceOk) return 1;
    AiScriptFile patchedScript = AiScript_File.Read(patchResult.EditedAiFileBytes);
    var restoreReq = new AiOmnisClusterPatchRequest(d.BeatName, d.CommandInstructionOffset, swap, d.CurrentCommand, d.StateWriteOffset, d.CurrentStateValue, d.CurrentStateValue);
    if (!AiOmnisClusterWriter.TryApplyPatch(patchedScript, restoreReq, out AiOmnisClusterEditResult? restoreResult, out string restoreError) || restoreResult == null)
    { Console.WriteLine($"m131 restore           : FAIL ({restoreError})"); return 1; }
    bool restoreByteIdentity = restoreResult.EditedAiFileBytes.SequenceEqual(aiFile);
    Console.WriteLine($"m131 restore identity   : {(restoreByteIdentity ? "OK" : "FAIL")}");
    bool pass = baselineOk && patchValid && spliceOk && restoreByteIdentity;
    Console.WriteLine($"m131 Omnis cluster      : {(pass ? "PASS" : "FAIL")}");
    Console.WriteLine(pass ? "VERDICT: PASS" : "VERDICT: FAIL");
    return pass ? 0 : 1;
}

static int SupportAccumulatorWriterRt0(IReadOnlyList<string> files, string root, string? jsonOut)
{
    Console.WriteLine("=== AiScriptLab --support-accumulator-writer-rt0 ===");
    string? path = files.FirstOrDefault(c => string.Equals(Path.GetFileNameWithoutExtension(c), "m127", StringComparison.OrdinalIgnoreCase));
    if (path == null) { Console.WriteLine("m127 load               : FAIL (file not found)"); return 1; }
    byte[] monster = File.ReadAllBytes(path);
    byte[]? aiFile = AiScript_File.SliceAiFileFromMonster(monster);
    if (aiFile == null) { Console.WriteLine("m127 ai slice           : FAIL"); return 1; }
    AiScriptFile script;
    try { script = AiScript_File.Read(aiFile); } catch (Exception ex) { Console.WriteLine($"m127 parse              : FAIL ({ex.Message})"); return 1; }
    if (!AiMortibodySupportAccumulatorWriter.TryBuildDescriptors(script, out IReadOnlyList<AiMortibodyAccumulatorDescriptor> baseline, out string detectError))
    { Console.WriteLine($"m127 descriptors        : FAIL ({detectError})"); return 1; }
    bool baselineOk = baseline.Count > 0;
    Console.WriteLine($"m127 baseline           : {(baselineOk ? "OK" : "MISMATCH")} ({baseline.Count} descriptor(s))");
    if (!baselineOk) return 1;
    var d = baseline.First();
    var patchReq = new AiMortibodyAccumulatorPatchRequest(d.VariableName, d.ScoreInstructionOffset, d.CurrentScoreValue, (ushort)(d.CurrentScoreValue + 1));
    if (!AiMortibodySupportAccumulatorWriter.TryApplyPatch(script, patchReq, out AiMortibodyAccumulatorEditResult? patchResult, out string patchError) || patchResult == null)
    { Console.WriteLine($"m127 patch             : FAIL ({patchError})"); return 1; }
    bool patchValid = AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(patchResult.EditedAiFileBytes, script, aiFile.Length, out _, out string patchValidWhy);
    Console.WriteLine($"m127 validate           : {(patchValid ? "OK" : $"FAIL ({patchValidWhy})")}");
    if (!patchValid) return 1;
    byte[] splicedMonster = AiScript_File.SpliceAiFileIntoMonster(monster, patchResult.EditedAiFileBytes);
    byte[]? slicedBack = AiScript_File.SliceAiFileFromMonster(splicedMonster);
    bool spliceOk = slicedBack != null && slicedBack.SequenceEqual(patchResult.EditedAiFileBytes);
    Console.WriteLine($"m127 splice round-trip  : {(spliceOk ? "OK" : "FAIL")}");
    if (!spliceOk) return 1;
    AiScriptFile patchedScript = AiScript_File.Read(patchResult.EditedAiFileBytes);
    var restoreReq = new AiMortibodyAccumulatorPatchRequest(d.VariableName, d.ScoreInstructionOffset, (ushort)(d.CurrentScoreValue + 1), d.CurrentScoreValue);
    if (!AiMortibodySupportAccumulatorWriter.TryApplyPatch(patchedScript, restoreReq, out AiMortibodyAccumulatorEditResult? restoreResult, out string restoreError) || restoreResult == null)
    { Console.WriteLine($"m127 restore           : FAIL ({restoreError})"); return 1; }
    bool restoreByteIdentity = restoreResult.EditedAiFileBytes.SequenceEqual(aiFile);
    Console.WriteLine($"m127 restore identity   : {(restoreByteIdentity ? "OK" : "FAIL")}");
    bool pass = baselineOk && patchValid && spliceOk && restoreByteIdentity;
    Console.WriteLine($"m127 Mortibody accumulator: {(pass ? "PASS" : "FAIL")}");
    Console.WriteLine(pass ? "VERDICT: PASS" : "VERDICT: FAIL");
    return pass ? 0 : 1;
}

static int MortiorchisWriterRt0(IReadOnlyList<string> files, string root, string? jsonOut)
{
    Console.WriteLine("=== AiScriptLab --mortiorchis-writer-rt0 ===");
    string? path = files.FirstOrDefault(c => string.Equals(Path.GetFileNameWithoutExtension(c), "m143", StringComparison.OrdinalIgnoreCase));
    if (path == null) { Console.WriteLine("m143 load               : FAIL (file not found)"); return 1; }
    byte[] monster = File.ReadAllBytes(path);
    byte[]? aiFile = AiScript_File.SliceAiFileFromMonster(monster);
    if (aiFile == null) { Console.WriteLine("m143 ai slice           : FAIL"); return 1; }
    AiScriptFile script;
    try { script = AiScript_File.Read(aiFile); } catch (Exception ex) { Console.WriteLine($"m143 parse              : FAIL ({ex.Message})"); return 1; }
    if (!AiMortiorchisCompanionWriter.TryBuildDescriptors(script, out IReadOnlyList<AiMortiorchisCompanionDescriptor> baseline, out string detectError))
    { Console.WriteLine($"m143 descriptors        : FAIL ({detectError})"); return 1; }
    bool baselineOk = baseline.Count > 0;
    Console.WriteLine($"m143 baseline           : {(baselineOk ? "OK" : "MISMATCH")} ({baseline.Count} descriptor(s))");
    if (!baselineOk) return 1;
    var d = baseline.First();
    ushort swap = d.CurrentCommand == 0x608C ? (ushort)0x60A9 : (ushort)0x608C;
    var patchReq = new AiMortiorchisCompanionPatchRequest(d.BeatName, d.CommandInstructionOffset, d.CurrentCommand, swap, d.GateWriteOffset, d.CurrentGateValue, d.CurrentGateValue);
    if (!AiMortiorchisCompanionWriter.TryApplyPatch(script, patchReq, out AiMortiorchisCompanionEditResult? patchResult, out string patchError) || patchResult == null)
    { Console.WriteLine($"m143 patch             : FAIL ({patchError})"); return 1; }
    bool patchValid = AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(patchResult.EditedAiFileBytes, script, aiFile.Length, out _, out string patchValidWhy);
    Console.WriteLine($"m143 validate           : {(patchValid ? "OK" : $"FAIL ({patchValidWhy})")}");
    if (!patchValid) return 1;
    byte[] splicedMonster = AiScript_File.SpliceAiFileIntoMonster(monster, patchResult.EditedAiFileBytes);
    byte[]? slicedBack = AiScript_File.SliceAiFileFromMonster(splicedMonster);
    bool spliceOk = slicedBack != null && slicedBack.SequenceEqual(patchResult.EditedAiFileBytes);
    Console.WriteLine($"m143 splice round-trip  : {(spliceOk ? "OK" : "FAIL")}");
    if (!spliceOk) return 1;
    AiScriptFile patchedScript = AiScript_File.Read(patchResult.EditedAiFileBytes);
    var restoreReq = new AiMortiorchisCompanionPatchRequest(d.BeatName, d.CommandInstructionOffset, swap, d.CurrentCommand, d.GateWriteOffset, d.CurrentGateValue, d.CurrentGateValue);
    if (!AiMortiorchisCompanionWriter.TryApplyPatch(patchedScript, restoreReq, out AiMortiorchisCompanionEditResult? restoreResult, out string restoreError) || restoreResult == null)
    { Console.WriteLine($"m143 restore           : FAIL ({restoreError})"); return 1; }
    bool restoreByteIdentity = restoreResult.EditedAiFileBytes.SequenceEqual(aiFile);
    Console.WriteLine($"m143 restore identity   : {(restoreByteIdentity ? "OK" : "FAIL")}");
    bool pass = baselineOk && patchValid && spliceOk && restoreByteIdentity;
    Console.WriteLine($"m143 Mortiorchis        : {(pass ? "PASS" : "FAIL")}");
    Console.WriteLine(pass ? "VERDICT: PASS" : "VERDICT: FAIL");
    return pass ? 0 : 1;
}

static int ReactiveSensorMortiphasmWriterRt0(IReadOnlyList<string> files, string root, string? jsonOut)
{
    Console.WriteLine("=== AiScriptLab --reactive-sensor-mortiphasm-writer-rt0 ===");
    string[] reactiveTargets = { "m106", "m118", "m150", "m154" };
    int failures = 0;
    foreach (string monsterId in reactiveTargets)
    {
        string? path = files.FirstOrDefault(c => string.Equals(Path.GetFileNameWithoutExtension(c), monsterId, StringComparison.OrdinalIgnoreCase));
        if (path == null) { Console.WriteLine($"{monsterId} load               : FAIL (file not found)"); failures++; continue; }
        byte[] monster = File.ReadAllBytes(path);
        byte[]? aiFile = AiScript_File.SliceAiFileFromMonster(monster);
        if (aiFile == null) { Console.WriteLine($"{monsterId} ai slice           : FAIL"); failures++; continue; }
        AiScriptFile script;
        try { script = AiScript_File.Read(aiFile); } catch (Exception ex) { Console.WriteLine($"{monsterId} parse              : FAIL ({ex.Message})"); failures++; continue; }
        if (!AiReactiveSensorWriter.TryBuildDescriptors(script, out IReadOnlyList<AiReactiveSensorDescriptor> baseline, out string detectError))
        { Console.WriteLine($"{monsterId} descriptors        : FAIL ({detectError})"); failures++; continue; }
        bool baselineOk = baseline.Count > 0;
        Console.WriteLine($"{monsterId} baseline           : {(baselineOk ? "OK" : "MISMATCH")} ({baseline.Count} descriptor(s))");
        if (!baselineOk) { failures++; continue; }
        var d = baseline.First();
        var patchReq = new AiReactiveSensorPatchRequest(d.VariableName, d.StateWriteOffset, d.CurrentStateValue, (ushort)(d.CurrentStateValue + 1));
        if (!AiReactiveSensorWriter.TryApplyPatch(script, patchReq, out AiReactiveSensorEditResult? patchResult, out string patchError) || patchResult == null)
        { Console.WriteLine($"{monsterId} patch             : FAIL ({patchError})"); failures++; continue; }
        bool patchValid = AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(patchResult.EditedAiFileBytes, script, aiFile.Length, out _, out string patchValidWhy);
        Console.WriteLine($"{monsterId} validate           : {(patchValid ? "OK" : $"FAIL ({patchValidWhy})")}");
        if (!patchValid) { failures++; continue; }
        byte[] splicedMonster = AiScript_File.SpliceAiFileIntoMonster(monster, patchResult.EditedAiFileBytes);
        byte[]? slicedBack = AiScript_File.SliceAiFileFromMonster(splicedMonster);
        bool spliceOk = slicedBack != null && slicedBack.SequenceEqual(patchResult.EditedAiFileBytes);
        Console.WriteLine($"{monsterId} splice round-trip  : {(spliceOk ? "OK" : "FAIL")}");
        if (!spliceOk) { failures++; continue; }
        AiScriptFile patchedScript = AiScript_File.Read(patchResult.EditedAiFileBytes);
        var restoreReq = new AiReactiveSensorPatchRequest(d.VariableName, d.StateWriteOffset, (ushort)(d.CurrentStateValue + 1), d.CurrentStateValue);
        if (!AiReactiveSensorWriter.TryApplyPatch(patchedScript, restoreReq, out AiReactiveSensorEditResult? restoreResult, out string restoreError) || restoreResult == null)
        { Console.WriteLine($"{monsterId} restore           : FAIL ({restoreError})"); failures++; continue; }
        bool restoreByteIdentity = restoreResult.EditedAiFileBytes.SequenceEqual(aiFile);
        Console.WriteLine($"{monsterId} restore identity   : {(restoreByteIdentity ? "OK" : "FAIL")}");
        if (!restoreByteIdentity) { failures++; continue; }
        Console.WriteLine($"{monsterId} reactive sensor    : PASS");
    }
    bool pass = failures == 0;
    Console.WriteLine($"reactive sensor aggregate: {(pass ? "PASS" : $"{failures} FAIL")}");
    Console.WriteLine(pass ? "VERDICT: PASS" : "VERDICT: FAIL");
    return pass ? 0 : 1;
}

static int PrivVarSlotProbe(List<string> files, string root, string? jsonOut)
{
    Console.WriteLine("=== AiScriptLab --privvar-slot-probe ===");
    int found = 0, free = 0, noPrivate = 0, noScript = 0, full = 0;
    int total = 0, withFree = 0;
    foreach (string path in files)
    {
        string n = Path.GetFileName(path);
        if (!n.StartsWith("m") || !n.EndsWith(".bin")) continue;
        total++;
        byte[] monster = File.ReadAllBytes(path);
        int aiPtr = (int)BitConverter.ToUInt32(monster, 0x04);
        int workerPtr = (int)BitConverter.ToUInt32(monster, 0x08);
        if (aiPtr == 0 || workerPtr <= aiPtr || workerPtr > monster.Length) continue;
        byte[] ai = monster[aiPtr..workerPtr];
        AiScriptFile script;
        try { script = AiScript_File.Read(ai); }
        catch { continue; }
        if (!script.HasScript) { noScript++; continue; }
        found++;
        bool ok = AiScript_File.TryFindFreePrivateVariableSlot(script, out int slot, out string reason);
        if (ok) withFree++;
        else if (reason.Contains("private storage", StringComparison.OrdinalIgnoreCase)) noPrivate++;
        else full++;
    }
    Console.WriteLine($"---");
    Console.WriteLine($"corpus: {total} bins, {found} with script, {withFree} with free slot, {noScript} no script, {noPrivate} no private storage, {full} full");

    string[] m238Candidates = files.Where(f => f.Replace('\\', '/').Contains("/_m238/m238.bin")).ToArray();
    if (m238Candidates.Length > 0)
    {
        string p = m238Candidates[0];
        Console.WriteLine();
        Console.WriteLine($"--- AppendPrivateVariableDescriptor round-trip on {p} ---");
        byte[] original = File.ReadAllBytes(p);
        int aPtr = (int)BitConverter.ToUInt32(original, 0x04);
        int wPtr = (int)BitConverter.ToUInt32(original, 0x08);
        byte[] origAi = original[aPtr..wPtr];
        AiScriptFile s = AiScript_File.Read(origAi);
        if (!s.HasScript) { Console.WriteLine("no script"); return 0; }
        if (!AiScript_File.TryFindFreePrivateVariableSlot(s, out int freeSlot, out string why))
        { Console.WriteLine($"no free slot: {why}"); return 0; }
        Console.WriteLine($"free slot = {freeSlot} (0x{freeSlot:X4})");

        byte[] grown;
        try { grown = AiScript_File.AppendPrivateVariableDescriptor(s, freeSlot); }
        catch (Exception ex) { Console.WriteLine($"Append EX: {ex.Message}"); return 1; }
        Console.WriteLine($"origAi len = {origAi.Length}, grown len = {grown.Length}, delta = {grown.Length - origAi.Length}");

        AiValidationReport report = AiValidator.ValidateRebuilt(grown, origAi.Length);
        Console.WriteLine($"ValidateRebuilt: IsValid={report.IsValid} errors={report.ErrorCount}");
        var firstErrors = report.Errors.Take(3).ToList();
        foreach (var issue in firstErrors)
            Console.WriteLine($"  err: {issue}");

        AiScriptFile rereadGrown = AiScript_File.Read(grown);
        Console.WriteLine($"reread grown: HasScript={rereadGrown.HasScript} Vars={rereadGrown.Variables.Count} (was {s.Variables.Count})");
        bool hasNewVar = rereadGrown.Variables.Any(v => v.Storage == 0x56 && v.Slot == freeSlot);
        Console.WriteLine($"new var priv{freeSlot:X4} present in reread: {hasNewVar}");

        try
        {
            byte[] spliced = AiScript_File.SpliceAiFileIntoMonsterGrow(original, grown);
            byte[] roundAi = AiScript_File.SliceAiFileFromMonster(spliced);
            AiScriptFile rereadSpliced = AiScript_File.Read(roundAi);
            bool hasNewVarSpliced = rereadSpliced.Variables.Any(v => v.Storage == 0x56 && v.Slot == freeSlot);
            Console.WriteLine($"splice: spliced len={spliced.Length} roundAi len={roundAi.Length} vars={rereadSpliced.Variables.Count} newVarPresent={hasNewVarSpliced}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Splice EX: {ex.Message}");
        }
    }
    return 0;
}


// ─────────────────────────────────────────────────────────────────────────────
// F1 — dry-run formal por receita (P2_RECEITAS_ATEL_2026-07-31.md): hash-precondição
// SHA-256, antes/depois em 3 camadas (bytes + instruções + resumo) e receipt/recovery.
// Nada toca disco: aplica in-memory, valida, confere splice round-trip e restauração.
// Uso: AiScriptLab --receita-dry-run [nome] [--json out.json]
// ─────────────────────────────────────────────────────────────────────────────

static int ReceitaDryRun(IReadOnlyList<string> files, string root, string? jsonOut, string[] args)
{
    Console.WriteLine("=== AiScriptLab --receita-dry-run (F1: hash-precondição + 3 camadas + receipt) ===");
    string? filter = null;
    for (int i = 0; i < args.Length; i++)
        if (args[i] == "--receita-dry-run" && i + 1 < args.Length && !args[i + 1].StartsWith("--"))
            filter = args[i + 1];

    bool Want(string name) => filter == null || string.Equals(filter, name, StringComparison.OrdinalIgnoreCase);

    var receipts = new List<AiReceipt>();
    void Collect(AiReceipt? r) { if (r != null) receipts.Add(r); }

    if (Want("omnis-cluster")) Collect(OmnisClusterDryRun(files));
    if (Want("support-accumulator")) Collect(SupportAccumulatorDryRun(files));
    if (Want("mortiorchis-companion")) Collect(MortiorchisDryRun(files));
    if (Want("flux-native-threshold")) Collect(FluxNativeDryRun(files));
    if (Want("anima-od-threshold")) Collect(AnimaOdDryRun(files));
    if (Want("round-scripted-boss")) Collect(RoundScriptedBossDryRun(files));
    if (Want("reactive-sensor"))
        foreach (string mid in new[] { "m106", "m118", "m150", "m154" })
            Collect(ReactiveSensorDryRun(files, mid));
    if (Want("indirect-dispatch-row")) Collect(IndirectDispatchRowDryRun(files));

    int pass = receipts.Count(r => r.Verdict);
    Console.WriteLine($"receitas dry-run: {pass}/{receipts.Count} PASS");
    Console.WriteLine(receipts.Count > 0 && pass == receipts.Count ? "VERDICT: PASS" : "VERDICT: FAIL");

    if (jsonOut != null)
    {
        var payload = new { generatedAt = DateTimeOffset.UtcNow, root, receipts };
        File.WriteAllText(jsonOut, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"json={jsonOut}");
    }
    return receipts.Count > 0 && pass == receipts.Count ? 0 : 1;
}

static AiReceipt? RunWriterDryRun(
    string recipe,
    string monsterId,
    IReadOnlyList<string> files,
    Func<byte[], AiScriptFile, (bool Ok, string Error)> buildDescriptors,
    Func<AiScriptFile, (bool Ok, byte[]? Edited, string Error)> applyPatch,
    Func<AiScriptFile, (bool Ok, byte[]? Restored, string Error)> restorePatch)
{
    string? path = files.FirstOrDefault(c => string.Equals(Path.GetFileNameWithoutExtension(c), monsterId, StringComparison.OrdinalIgnoreCase));
    if (path == null) { Console.WriteLine($"{recipe}: FAIL (monster {monsterId} not found)"); return null; }

    byte[] monster = File.ReadAllBytes(path);
    byte[]? aiFile = AiScript_File.SliceAiFileFromMonster(monster);
    if (aiFile == null) { Console.WriteLine($"{recipe}: FAIL (ai slice)"); return null; }

    string preconditionHash = AiPatchGuard.ComputeSha256(aiFile);
    AiScriptFile script;
    try { script = AiScript_File.Read(aiFile); }
    catch (Exception ex) { Console.WriteLine($"{recipe}: FAIL (parse {ex.GetType().Name}: {ex.Message})"); return null; }

    var (buildOk, buildError) = buildDescriptors(monster, script);
    if (!buildOk) { Console.WriteLine($"{recipe}: FAIL (descriptors: {buildError})"); return null; }

    var (patchOk, editedBytes, patchError) = applyPatch(script);
    if (!patchOk || editedBytes == null) { Console.WriteLine($"{recipe}: FAIL (patch: {patchError})"); return null; }

    bool validatorOk = AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(editedBytes, script, aiFile.Length, out _, out string validatorWhy);

    byte[] splicedMonster = AiScript_File.SpliceAiFileIntoMonster(monster, editedBytes);
    byte[]? slicedBack = AiScript_File.SliceAiFileFromMonster(splicedMonster);
    bool spliceOk = slicedBack != null && slicedBack.SequenceEqual(editedBytes);

    AiScriptFile editedScript;
    try { editedScript = AiScript_File.Read(editedBytes); }
    catch (Exception ex) { Console.WriteLine($"{recipe}: FAIL (reread {ex.Message})"); return null; }

    var (restoreOk, restoredBytes, restoreError) = restorePatch(editedScript);
    bool restoreIdentity = restoreOk && restoredBytes != null && restoredBytes.SequenceEqual(aiFile);
    if (!restoreIdentity)
        Console.WriteLine($"{recipe}: restore: FAIL ({restoreError})");

    AiReceipt receipt = AiPatchGuard.BuildReceipt(recipe, monsterId, aiFile, editedBytes, script, editedScript, validatorOk, validatorWhy, spliceOk, restoreIdentity);
    Console.WriteLine($"{recipe} ({monsterId}): hash={AiPatchGuard.ShortHash(receipt.BeforeSha256)} bytes={receipt.ByteChanges.Count} validator={(validatorOk ? "OK" : "FAIL")} splice={(spliceOk ? "OK" : "FAIL")} restore={(restoreIdentity ? "OK" : "FAIL")} -> {(receipt.Verdict ? "PASS" : "FAIL")}");
    return receipt;
}

static AiReceipt? OmnisClusterDryRun(IReadOnlyList<string> files)
{
    string beatName = string.Empty;
    int cmdOffset = -1;
    ushort originalCommand = 0, swappedCommand = 0;
    int? stateOffset = null;
    ushort? stateValue = null;

    return RunWriterDryRun("omnis-cluster", "m131", files,
        (_, script) =>
        {
            if (!AiOmnisClusterWriter.TryBuildDescriptors(script, out IReadOnlyList<AiOmnisClusterDescriptor> ds, out string err) || ds.Count == 0)
                return (false, ds.Count == 0 ? "sem descriptors" : err);
            AiOmnisClusterDescriptor d = ds.First();
            beatName = d.BeatName;
            cmdOffset = d.CommandInstructionOffset;
            originalCommand = d.CurrentCommand;
            swappedCommand = d.CurrentCommand == 0x3045 ? (ushort)0x3046 : (ushort)0x3045;
            stateOffset = d.StateWriteOffset;
            stateValue = d.CurrentStateValue;
            return (true, string.Empty);
        },
        script =>
        {
            if (cmdOffset < 0) return (false, null, "descriptors não montados");
            var req = new AiOmnisClusterPatchRequest(beatName, cmdOffset, originalCommand, swappedCommand, stateOffset, stateValue, stateValue);
            bool ok = AiOmnisClusterWriter.TryApplyPatch(script, req, out AiOmnisClusterEditResult? res, out string err);
            return ok && res != null ? (true, res.EditedAiFileBytes, string.Empty) : (false, null, err);
        },
        script =>
        {
            if (cmdOffset < 0) return (false, null, "descriptors não montados");
            var req = new AiOmnisClusterPatchRequest(beatName, cmdOffset, swappedCommand, originalCommand, stateOffset, stateValue, stateValue);
            bool ok = AiOmnisClusterWriter.TryApplyPatch(script, req, out AiOmnisClusterEditResult? res, out string err);
            return ok && res != null ? (true, res.EditedAiFileBytes, string.Empty) : (false, null, err);
        });
}

static AiReceipt? SupportAccumulatorDryRun(IReadOnlyList<string> files)
{
    string varName = string.Empty;
    int scoreOffset = -1;
    ushort originalScore = 0, newScore = 0;

    return RunWriterDryRun("support-accumulator", "m127", files,
        (_, script) =>
        {
            if (!AiMortibodySupportAccumulatorWriter.TryBuildDescriptors(script, out IReadOnlyList<AiMortibodyAccumulatorDescriptor> ds, out string err) || ds.Count == 0)
                return (false, ds.Count == 0 ? "sem descriptors" : err);
            AiMortibodyAccumulatorDescriptor d = ds.First();
            varName = d.VariableName;
            scoreOffset = d.ScoreInstructionOffset;
            originalScore = d.CurrentScoreValue;
            newScore = (ushort)(d.CurrentScoreValue + 1);
            return (true, string.Empty);
        },
        script =>
        {
            if (scoreOffset < 0) return (false, null, "descriptors não montados");
            var req = new AiMortibodyAccumulatorPatchRequest(varName, scoreOffset, originalScore, newScore);
            bool ok = AiMortibodySupportAccumulatorWriter.TryApplyPatch(script, req, out AiMortibodyAccumulatorEditResult? res, out string err);
            return ok && res != null ? (true, res.EditedAiFileBytes, string.Empty) : (false, null, err);
        },
        script =>
        {
            if (scoreOffset < 0) return (false, null, "descriptors não montados");
            var req = new AiMortibodyAccumulatorPatchRequest(varName, scoreOffset, newScore, originalScore);
            bool ok = AiMortibodySupportAccumulatorWriter.TryApplyPatch(script, req, out AiMortibodyAccumulatorEditResult? res, out string err);
            return ok && res != null ? (true, res.EditedAiFileBytes, string.Empty) : (false, null, err);
        });
}

static AiReceipt? MortiorchisDryRun(IReadOnlyList<string> files)
{
    string beatName = string.Empty;
    int cmdOffset = -1;
    ushort originalCommand = 0, swappedCommand = 0;
    int? gateOffset = null;
    ushort? gateValue = null;

    return RunWriterDryRun("mortiorchis-companion", "m143", files,
        (_, script) =>
        {
            if (!AiMortiorchisCompanionWriter.TryBuildDescriptors(script, out IReadOnlyList<AiMortiorchisCompanionDescriptor> ds, out string err) || ds.Count == 0)
                return (false, ds.Count == 0 ? "sem descriptors" : err);
            AiMortiorchisCompanionDescriptor d = ds.First();
            beatName = d.BeatName;
            cmdOffset = d.CommandInstructionOffset;
            originalCommand = d.CurrentCommand;
            swappedCommand = d.CurrentCommand == 0x608C ? (ushort)0x60A9 : (ushort)0x608C;
            gateOffset = d.GateWriteOffset;
            gateValue = d.CurrentGateValue;
            return (true, string.Empty);
        },
        script =>
        {
            if (cmdOffset < 0) return (false, null, "descriptors não montados");
            var req = new AiMortiorchisCompanionPatchRequest(beatName, cmdOffset, originalCommand, swappedCommand, gateOffset, gateValue, gateValue);
            bool ok = AiMortiorchisCompanionWriter.TryApplyPatch(script, req, out AiMortiorchisCompanionEditResult? res, out string err);
            return ok && res != null ? (true, res.EditedAiFileBytes, string.Empty) : (false, null, err);
        },
        script =>
        {
            if (cmdOffset < 0) return (false, null, "descriptors não montados");
            var req = new AiMortiorchisCompanionPatchRequest(beatName, cmdOffset, swappedCommand, originalCommand, gateOffset, gateValue, gateValue);
            bool ok = AiMortiorchisCompanionWriter.TryApplyPatch(script, req, out AiMortiorchisCompanionEditResult? res, out string err);
            return ok && res != null ? (true, res.EditedAiFileBytes, string.Empty) : (false, null, err);
        });
}

static AiReceipt? FluxNativeDryRun(IReadOnlyList<string> files)
{
    string varName = string.Empty;
    int denomOffset = -1;
    ushort originalDenom = 0, newDenom = 0;
    int? numOffset = null;
    ushort? numValue = null;

    return RunWriterDryRun("flux-native-threshold", "m142", files,
        (_, script) =>
        {
            if (!AiFluxNativeThresholdWriter.TryBuildDescriptors(script, out IReadOnlyList<AiFluxNativeThresholdDescriptor> ds, out string err) || ds.Count == 0)
                return (false, ds.Count == 0 ? "sem descriptors" : err);
            AiFluxNativeThresholdDescriptor d = ds.First();
            varName = d.VariableName;
            denomOffset = d.DenominatorInstructionOffset;
            originalDenom = d.CurrentDenominator;
            newDenom = d.CurrentDenominator == 0 ? (ushort)4 : (ushort)(d.CurrentDenominator + 1);
            numOffset = d.NumeratorInstructionOffset;
            numValue = d.CurrentNumerator;
            return (true, string.Empty);
        },
        script =>
        {
            if (denomOffset < 0) return (false, null, "descriptors não montados");
            var req = new AiFluxNativeThresholdPatchRequest(varName, denomOffset, originalDenom, newDenom, numOffset, numValue, numValue);
            bool ok = AiFluxNativeThresholdWriter.TryApplyPatch(script, req, out AiFluxNativeThresholdEditResult? res, out string err);
            return ok && res != null ? (true, res.EditedAiFileBytes, string.Empty) : (false, null, err);
        },
        script =>
        {
            if (denomOffset < 0) return (false, null, "descriptors não montados");
            var req = new AiFluxNativeThresholdPatchRequest(varName, denomOffset, newDenom, originalDenom, numOffset, numValue, numValue);
            bool ok = AiFluxNativeThresholdWriter.TryApplyPatch(script, req, out AiFluxNativeThresholdEditResult? res, out string err);
            return ok && res != null ? (true, res.EditedAiFileBytes, string.Empty) : (false, null, err);
        });
}

static AiReceipt? AnimaOdDryRun(IReadOnlyList<string> files)
{

    int maximumOffset = -1;
    ushort originalMaximum = 0, newMaximum = 0;

    return RunWriterDryRun("anima-od-threshold", "m125", files,
        (_, script) =>
        {
            if (!AiAnimaOdThresholdWriter.TryBuildDescriptors(script, out IReadOnlyList<AiAnimaOdThresholdDescriptor> ds, out string err) || ds.Count == 0)
                return (false, ds.Count == 0 ? "sem descriptors" : err);
            AiAnimaOdThresholdDescriptor d = ds.First();

            maximumOffset = d.MaximumInstructionOffset;
            originalMaximum = d.CurrentMaximum;
            newMaximum = d.CurrentMaximum == 100 ? (ushort)99 : (ushort)(d.CurrentMaximum + 1);

            return (true, string.Empty);
        },
        script =>
        {
            if (maximumOffset < 0) return (false, null, "descriptors não montados");
            var req = new AiAnimaOdThresholdPatchRequest(maximumOffset, originalMaximum, newMaximum);
            bool ok = AiAnimaOdThresholdWriter.TryApplyPatch(script, req, out AiAnimaOdThresholdEditResult? res, out string err);
            return ok && res != null ? (true, res.EditedAiFileBytes, string.Empty) : (false, null, err);
        },
        script =>
        {
            if (maximumOffset < 0) return (false, null, "descriptors não montados");
            var req = new AiAnimaOdThresholdPatchRequest(maximumOffset, newMaximum, originalMaximum);
            bool ok = AiAnimaOdThresholdWriter.TryApplyPatch(script, req, out AiAnimaOdThresholdEditResult? res, out string err);
            return ok && res != null ? (true, res.EditedAiFileBytes, string.Empty) : (false, null, err);
        });
}

static AiReceipt? RoundScriptedBossDryRun(IReadOnlyList<string> files)
{
    string beatName = string.Empty;
    int cmdOffset = -1;
    ushort currentCommand = 0, newCommand = 0;
    int? repriseOffset = null;
    ushort? currentReprise = null;

    return RunWriterDryRun("round-scripted-boss", "m238", files,
        (_, script) =>
        {
            if (!AiRoundScriptedBossWriter.TryBuildDescriptors(script, out IReadOnlyList<AiRoundScriptedBossDescriptor> ds, out string err) || ds.Count == 0)
                return (false, ds.Count == 0 ? "sem descriptors" : err);
            AiRoundScriptedBossDescriptor d = ds.First();
            beatName = d.BeatName;
            cmdOffset = d.CommandInstructionOffset;
            currentCommand = d.CurrentCommand;
            newCommand = d.CurrentCommand == 0x4019 ? (ushort)0x401A : (ushort)0x4019;
            repriseOffset = d.LandingRepriseOffset;
            currentReprise = d.CurrentLandingReprise;
            return (true, string.Empty);
        },
        script =>
        {
            if (cmdOffset < 0) return (false, null, "descriptors não montados");
            var req = new AiRoundScriptedBossPatchRequest(beatName, cmdOffset, currentCommand, newCommand, repriseOffset, currentReprise, currentReprise);
            bool ok = AiRoundScriptedBossWriter.TryApplyPatch(script, req, out AiRoundScriptedBossEditResult? res, out string err);
            return ok && res != null ? (true, res.EditedAiFileBytes, string.Empty) : (false, null, err);
        },
        script =>
        {
            if (cmdOffset < 0) return (false, null, "descriptors não montados");
            var req = new AiRoundScriptedBossPatchRequest(beatName, cmdOffset, newCommand, currentCommand, repriseOffset, currentReprise, currentReprise);
            bool ok = AiRoundScriptedBossWriter.TryApplyPatch(script, req, out AiRoundScriptedBossEditResult? res, out string err);
            return ok && res != null ? (true, res.EditedAiFileBytes, string.Empty) : (false, null, err);
        });
}

static AiReceipt? ReactiveSensorDryRun(IReadOnlyList<string> files, string monsterId)
{
    string varName = string.Empty;
    int stateOffset = -1;
    ushort originalState = 0, newState = 0;

    return RunWriterDryRun("reactive-sensor", monsterId, files,
        (_, script) =>
        {
            if (!AiReactiveSensorWriter.TryBuildDescriptors(script, out IReadOnlyList<AiReactiveSensorDescriptor> ds, out string err) || ds.Count == 0)
                return (false, ds.Count == 0 ? "sem descriptors" : err);
            AiReactiveSensorDescriptor d = ds.First();
            varName = d.VariableName;
            stateOffset = d.StateWriteOffset;
            originalState = d.CurrentStateValue;
            newState = (ushort)(d.CurrentStateValue + 1);
            return (true, string.Empty);
        },
        script =>
        {
            if (stateOffset < 0) return (false, null, "descriptors não montados");
            var req = new AiReactiveSensorPatchRequest(varName, stateOffset, originalState, newState);
            bool ok = AiReactiveSensorWriter.TryApplyPatch(script, req, out AiReactiveSensorEditResult? res, out string err);
            return ok && res != null ? (true, res.EditedAiFileBytes, string.Empty) : (false, null, err);
        },
        script =>
        {
            if (stateOffset < 0) return (false, null, "descriptors não montados");
            var req = new AiReactiveSensorPatchRequest(varName, stateOffset, newState, originalState);
            bool ok = AiReactiveSensorWriter.TryApplyPatch(script, req, out AiReactiveSensorEditResult? res, out string err);
            return ok && res != null ? (true, res.EditedAiFileBytes, string.Empty) : (false, null, err);
        });
}

static AiReceipt? IndirectDispatchRowDryRun(IReadOnlyList<string> files)
{
    const string SeymourId = "m124";
    string unitId = string.Empty, roleKey = string.Empty, roleLabel = string.Empty;
    int sourceOffset = -1;
    ushort oldValue = 0, newValue = 0;

    return RunWriterDryRun("indirect-dispatch-row", SeymourId, files,
        (monsterBin, script) =>
        {
            IReadOnlyList<AiIndirectDispatchUnit> units = AiAutomation.DetectIndirectDispatchUnits(monsterBin, script);
            if (units.Count == 0) return (false, "sem unidades indiretas detectadas");
            AiIndirectDispatchUnit? u = units.FirstOrDefault(x => x.EditableTargetSlots.Any(s => s.CanEdit && s.EditableOperands.Count > 0));
            if (u == null) return (false, "sem unidades com slot editável");
            AiIndirectDispatchEditableTargetSlot slot = u.EditableTargetSlots.First(s => s.CanEdit && s.EditableOperands.Count > 0);
            AiIndirectDispatchEditableOperand operand = slot.EditableOperands.First();
            unitId = u.UnitId;
            roleKey = operand.RoleKey;
            roleLabel = operand.RoleLabel;
            sourceOffset = operand.InstructionOffset;
            oldValue = operand.CurrentValue;
            newValue = operand.CurrentValue == 0 ? (ushort)1 : (ushort)(operand.CurrentValue - 1);
            return (true, string.Empty);
        },
        script =>
        {
            if (sourceOffset < 0) return (false, null, "descriptors não montados");
            var req = new AiIndirectDispatchEditRequest(unitId, new[] { new AiIndirectDispatchValueEdit(roleKey, roleLabel, sourceOffset, oldValue, newValue) });
            bool ok = AiIndirectDispatchRowOnlyEditor.TryApplyEdits(script, req, out AiIndirectDispatchEditResult? res, out string err);
            return ok && res != null ? (true, res.EditedAiFileBytes, string.Empty) : (false, null, err);
        },
        script =>
        {
            if (sourceOffset < 0) return (false, null, "descriptors não montados");
            var req = new AiIndirectDispatchEditRequest(unitId, new[] { new AiIndirectDispatchValueEdit(roleKey, roleLabel, sourceOffset, newValue, oldValue) });
            bool ok = AiIndirectDispatchRowOnlyEditor.TryApplyEdits(script, req, out AiIndirectDispatchEditResult? res, out string err);
            return ok && res != null ? (true, res.EditedAiFileBytes, string.Empty) : (false, null, err);
        });
}

// ─────────────────────────────────────────────────────────────────────────────
// F3.1 — code-grow/shrink com remap completo. Insere PUSHII no inicio do codigo
// de um script do corpus, valida remap de entrypoints/jump targets, e restaura.
// ─────────────────────────────────────────────────────────────────────────────

static int CodeGrowRt0(IReadOnlyList<string> files, string root, string? jsonOut)
{
    Console.WriteLine("=== AiScriptLab --code-grow-rt0 (F3.1: remap de codigo) ===");
    string? path = files.FirstOrDefault(c => string.Equals(Path.GetFileNameWithoutExtension(c), "m131", StringComparison.OrdinalIgnoreCase));
    if (path == null) { Console.WriteLine("FAIL (m131 not found)"); return 1; }

    byte[] monster = File.ReadAllBytes(path);
    byte[]? aiFile = AiScript_File.SliceAiFileFromMonster(monster);
    if (aiFile == null) { Console.WriteLine("FAIL (ai slice)"); return 1; }
    if (!AiScript_File.RoundTripsByteIdentical(aiFile)) { Console.WriteLine("FAIL (baseline not byte-stable)"); return 1; }

    AiScriptFile original = AiScript_File.Read(aiFile);
    if (!original.CodeWalkClosedExactly || original.UnknownOpcodes.Count > 0) { Console.WriteLine("FAIL (baseline walk)"); return 1; }

    const ushort sentinel = 0x1234;
    var insert = new List<AiInstruction>
    {
        new() { Offset = -1, Opcode = 0xAE, HasOperand = true, Operand = sentinel, OperandKind = AiScript_File.OperandKindOf(0xAE) },
    };

    byte[]? grown = AiScript_File.RebuildWithCodeRegion(original, 0, 0, insert, out string growError);
    if (grown == null) { Console.WriteLine($"FAIL (grow: {growError})"); return 1; }

    AiScriptFile grownScript = AiScript_File.Read(grown);
    if (!grownScript.CodeWalkClosedExactly || grownScript.UnknownOpcodes.Count > 0) { Console.WriteLine("FAIL (grown walk)"); return 1; }
    if (!AiScript_File.RoundTripsByteIdentical(grown)) { Console.WriteLine("FAIL (grown round-trip)"); return 1; }

    if (Environment.GetCommandLineArgs().Contains("--debug-grow"))
    {
        var dbgStarts = new HashSet<int>(grownScript.Instructions.Select(i => i.Offset - grownScript.ScriptStart));
        Console.WriteLine($"debug: scriptStart orig={original.ScriptStart} grown={grownScript.ScriptStart} codeLen orig={original.CodeLength} grown={grownScript.CodeLength}");
        Console.WriteLine($"debug: first 16 starts: {string.Join(" ", dbgStarts.OrderBy(x => x).Take(16))}");
        for (int w = 0; w < Math.Min(original.Workers.Count, 3); w++)
        {
            uint RU32(byte[] b, int o) => BitConverter.ToUInt32(b, o);
            Console.WriteLine($"debug: worker{w} entryTab orig=0x{RU32(aiFile, original.Workers[w].DescriptorOffset + 0x20):X} grown=0x{RU32(grown, grownScript.Workers[w].DescriptorOffset + 0x20):X}");
            var origTab = new List<uint>(); int eo = (int)RU32(aiFile, original.Workers[w].DescriptorOffset + 0x20);
            for (int k = 0; k < original.Workers[w].Entrypoints.Count; k++) origTab.Add(RU32(aiFile, eo + 4 * k));
            var grownTab = new List<uint>(); int eg = (int)RU32(grown, grownScript.Workers[w].DescriptorOffset + 0x20);
            for (int k = 0; k < grownScript.Workers[w].Entrypoints.Count; k++) grownTab.Add(RU32(grown, eg + 4 * k));
            Console.WriteLine($"debug: worker{w} entryTableRaw orig=[{string.Join(",", origTab.Select(x => $"0x{x:X}"))}] grown=[{string.Join(",", grownTab.Select(x => $"0x{x:X}"))}]");
            if (w == 0)
            {
                string Hex(byte[] b, int from, int len) => string.Join(" ", Enumerable.Range(from, len).Select(i => b[i].ToString("X2")));
                Console.WriteLine($"debug: orig bytes 0x1D2C..0x1D54: {Hex(aiFile, 0x1D2C, 0x28)}");
                Console.WriteLine($"debug: grown bytes 0x1D2C..0x1D54: {Hex(grown, 0x1D2C, 0x28)}");
            }
        }
    }

    if (!AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(grown, original, aiFile.Length + 3, out _, out string growWhy))
    { Console.WriteLine($"FAIL (validator: {growWhy})"); return 1; }

    bool remapOk = true;
    for (int w = 0; w < original.Workers.Count && remapOk; w++)
    {
        AiWorker oldW = original.Workers[w];
        AiWorker newW = grownScript.Workers[w];
        if (oldW.JumpTargets.Count != newW.JumpTargets.Count) { remapOk = false; break; }
        for (int k = 0; k < oldW.JumpTargets.Count; k++)
            if (newW.JumpTargets[k] != oldW.JumpTargets[k] + 3) { remapOk = false; break; }
        if (oldW.Entrypoints.Count != newW.Entrypoints.Count) { remapOk = false; break; }
        for (int k = 0; k < oldW.Entrypoints.Count; k++)
            if (newW.Entrypoints[k] != oldW.Entrypoints[k] + 3) { remapOk = false; break; }
    }
    if (!remapOk) { Console.WriteLine("FAIL (jump/entrypoint remap mismatch)"); return 1; }

    if (Environment.GetCommandLineArgs().Contains("--debug-grow"))
    {
        AiScriptFile grownDbg = AiScript_File.Read(grown);
        var starts = new HashSet<int>(grownDbg.Instructions.Select(i => i.Offset - grownDbg.ScriptStart));
        Console.WriteLine($"debug: scriptStart orig={original.ScriptStart} grown={grownDbg.ScriptStart} codeLen orig={original.CodeLength} grown={grownDbg.CodeLength}");
        Console.WriteLine($"debug: first 12 starts: {string.Join(" ", starts.OrderBy(x => x).Take(12))}");
        for (int w = 0; w < Math.Min(original.Workers.Count, 3); w++)
        {
            Console.WriteLine($"debug: worker{w} entrypoints orig=[{string.Join(",", original.Workers[w].Entrypoints.Take(6).Select(x => $"0x{x:X}"))}] grown=[{string.Join(",", grownDbg.Workers[w].Entrypoints.Take(6).Select(x => $"0x{x:X}"))}]");
            Console.WriteLine($"debug: worker{w} jumps orig=[{string.Join(",", original.Workers[w].JumpTargets.Take(4).Select(x => $"0x{x:X}"))}] grown=[{string.Join(",", grownDbg.Workers[w].JumpTargets.Take(4).Select(x => $"0x{x:X}"))}]");
            int bad = grownDbg.Workers[w].Entrypoints.Count(e => !starts.Contains(e));
            Console.WriteLine($"debug: worker{w} entrypoints not-on-start: {bad}/{grownDbg.Workers[w].Entrypoints.Count}");
        }
    }

    byte[]? restored = AiScript_File.RebuildWithCodeRegion(grownScript, 0, 3, Array.Empty<AiInstruction>(), out string restoreError);
    if (restored == null) { Console.WriteLine($"FAIL (restore: {restoreError})"); return 1; }
    if (!restored.AsSpan().SequenceEqual(aiFile)) { Console.WriteLine("FAIL (restore not byte-identical)"); return 1; }

    Console.WriteLine($"code-grow: insert={insert[0].Length}B delta=+3 remap={original.Workers.Count} workers restore=byte-identical -> PASS");
    if (jsonOut != null)
        File.WriteAllText(jsonOut, JsonSerializer.Serialize(new { gate = "code-grow-rt0", monster = "m131", verdict = "PASS", delta = 3, workers = original.Workers.Count }));
    return 0;
}

// ─────────────────────────────────────────────────────────────────────────────
// F3.2 — target recipe writer: swap literal <-> findMatchingChr e volta, com
// byte-identity apos o segundo swap.
// ─────────────────────────────────────────────────────────────────────────────

static int TargetRecipeWriterRt0(IReadOnlyList<string> files, string root, string? jsonOut)
{
    Console.WriteLine("=== AiScriptLab --target-recipe-writer-rt0 (F3.2: recipes de alvo) ===");
    string? path = files.FirstOrDefault(c => string.Equals(Path.GetFileNameWithoutExtension(c), "m124", StringComparison.OrdinalIgnoreCase));
    if (path == null) { Console.WriteLine("FAIL (m124 not found)"); return 1; }

    byte[] monster = File.ReadAllBytes(path);
    byte[]? aiFile = AiScript_File.SliceAiFileFromMonster(monster);
    if (aiFile == null) { Console.WriteLine("FAIL (ai slice)"); return 1; }

    AiScriptFile script = AiScript_File.Read(aiFile);
    if (!AiTargetRecipeWriter.TryBuildDescriptors(script, out IReadOnlyList<AiTargetRecipeDescriptor> descriptors, out string detectError) || descriptors.Count == 0)
    { Console.WriteLine($"FAIL (detect: {detectError})"); return 1; }

    int literalCount = descriptors.Count(d => d.CurrentRecipe == AiTargetRecipeKind.Literal);
    int computedCount = descriptors.Count(d => d.CurrentRecipe != AiTargetRecipeKind.Literal);
    Console.WriteLine($"detect: {descriptors.Count} slots (literal={literalCount} computed={computedCount})");

    AiTargetRecipeDescriptor? literalSlot = descriptors.FirstOrDefault(d => d.CurrentRecipe == AiTargetRecipeKind.Literal);
    AiTargetRecipeDescriptor? computedSlot = descriptors.FirstOrDefault(d => d.CurrentRecipe != AiTargetRecipeKind.Literal);

    int checks = 0;
    if (literalSlot != null)
    {
        int slotIndex = descriptors.ToList().IndexOf(literalSlot);
        var fwd = new AiTargetRecipeSwapRequest(literalSlot.RoleKey, literalSlot.AnchorInstructionOffset, AiTargetRecipeKind.FindAliveFrontlineAny, 0);
        if (!AiTargetRecipeWriter.TryApplyRecipeSwap(script, fwd, out AiTargetRecipeEditResult? grown, out string fwdError) || grown == null)
        { Console.WriteLine($"FAIL (literal->any: {fwdError})"); return 1; }
        if (!AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(grown.EditedAiFileBytes, script, script.OriginalAiFileBytes.Length + 12, out _, out string v1))
        { Console.WriteLine($"FAIL (validator literal->any: {v1})"); return 1; }
        checks++;

        AiScriptFile grownScript = AiScript_File.Read(grown.EditedAiFileBytes);
        if (!AiScript_File.RoundTripsByteIdentical(grown.EditedAiFileBytes)) { Console.WriteLine("FAIL (grown round-trip)"); return 1; }

        if (!AiTargetRecipeWriter.TryBuildDescriptors(grownScript, out IReadOnlyList<AiTargetRecipeDescriptor> regrownDescs, out string reErr) || regrownDescs.Count <= slotIndex)
        { Console.WriteLine($"FAIL (re-detect: {reErr})"); return 1; }
        AiTargetRecipeDescriptor grownSlot = regrownDescs[slotIndex];

        var back = new AiTargetRecipeSwapRequest(grownSlot.RoleKey, grownSlot.AnchorInstructionOffset, AiTargetRecipeKind.Literal, literalSlot.CurrentLiteralOperand);
        if (!AiTargetRecipeWriter.TryApplyRecipeSwap(grownScript, back, out AiTargetRecipeEditResult? shrunk, out string backError) || shrunk == null)
        { Console.WriteLine($"FAIL (any->literal: {backError})"); return 1; }
        if (!shrunk.EditedAiFileBytes.AsSpan().SequenceEqual(aiFile)) { Console.WriteLine("FAIL (any->literal not byte-identical)"); return 1; }
        checks++;
        Console.WriteLine($"literal->findMatchingChr->literal: byte-identical -> PASS");
    }

    if (computedSlot != null)
    {
        int slotIndex = descriptors.ToList().IndexOf(computedSlot);
        var fwd2 = new AiTargetRecipeSwapRequest(computedSlot.RoleKey, computedSlot.AnchorInstructionOffset, AiTargetRecipeKind.Literal, 0xFFF3);
        if (!AiTargetRecipeWriter.TryApplyRecipeSwap(script, fwd2, out AiTargetRecipeEditResult? shrunk2, out string f2Error) || shrunk2 == null)
        { Console.WriteLine($"FAIL (computed->literal: {f2Error})"); return 1; }
        if (!AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(shrunk2.EditedAiFileBytes, script, script.OriginalAiFileBytes.Length - 12, out _, out string v2))
        { Console.WriteLine($"FAIL (validator computed->literal: {v2})"); return 1; }
        checks++;

        AiScriptFile shrunkScript = AiScript_File.Read(shrunk2.EditedAiFileBytes);
        if (!AiTargetRecipeWriter.TryBuildDescriptors(shrunkScript, out IReadOnlyList<AiTargetRecipeDescriptor> shrunkDescs, out string sErr) || shrunkDescs.Count <= slotIndex)
        { Console.WriteLine($"FAIL (re-detect shrunk: {sErr})"); return 1; }
        AiTargetRecipeDescriptor shrunkSlot = shrunkDescs[slotIndex];

        var back2 = new AiTargetRecipeSwapRequest(shrunkSlot.RoleKey, shrunkSlot.AnchorInstructionOffset, AiTargetRecipeKind.FindAliveFrontlineAny, 0);        if (!AiTargetRecipeWriter.TryApplyRecipeSwap(shrunkScript, back2, out AiTargetRecipeEditResult? regrown, out string b2Error) || regrown == null)
        { Console.WriteLine($"FAIL (literal->any volta: {b2Error})"); return 1; }
        if (!regrown.EditedAiFileBytes.AsSpan().SequenceEqual(aiFile)) { Console.WriteLine("FAIL (computed roundtrip not byte-identical)"); return 1; }
        checks++;
        Console.WriteLine($"computed->literal->computed: byte-identical -> PASS");
    }

    if (checks == 0) { Console.WriteLine("FAIL (nenhum slot testavel)"); return 1; }
    Console.WriteLine($"target-recipe-writer: {checks} swaps PASS");
    if (jsonOut != null)
        File.WriteAllText(jsonOut, JsonSerializer.Serialize(new { gate = "target-recipe-writer-rt0", monster = "m124", verdict = "PASS", swaps = checks }));
    return 0;
}

// F5.3 (G-5): verifica o status de decodificacao do corpus (tudo Confirmed) e reporta contagens.
static int DecodeStatusRt0(IReadOnlyList<string> files)
{
    Console.WriteLine("=== AiScriptLab --decode-status-rt0 (F5.3: badge Confirmed/Hypothesis/Decoded) ===");
    int confirmed = 0, hypothesis = 0, decoded = 0, monsters = 0;
    var nonConfirmed = new Dictionary<byte, int>();
    foreach (string path in files)
    {
        byte[] monster = File.ReadAllBytes(path);
        byte[]? aiFile = AiScript_File.SliceAiFileFromMonster(monster);
        if (aiFile == null) continue;
        AiScriptFile script;
        try { script = AiScript_File.Read(aiFile); }
        catch { continue; }
        monsters++;
        foreach (AiInstruction i in script.Instructions)
        {
            switch (AiScript_File.DecodeStatusOf(i.Opcode))
            {
                case AiScript_File.AiInstructionDecodeStatus.Confirmed: confirmed++; break;
                case AiScript_File.AiInstructionDecodeStatus.Hypothesis:
                    hypothesis++;
                    nonConfirmed[i.Opcode] = nonConfirmed.GetValueOrDefault(i.Opcode) + 1;
                    break;
                case AiScript_File.AiInstructionDecodeStatus.Decoded:
                    decoded++;
                    nonConfirmed[i.Opcode] = nonConfirmed.GetValueOrDefault(i.Opcode) + 1;
                    break;
            }
        }
    }
    Console.WriteLine($"monstros={monsters} confirmed={confirmed} hypothesis={hypothesis} decoded={decoded}");
    if (nonConfirmed.Count > 0)
    {
        foreach (KeyValuePair<byte, int> kv in nonConfirmed)
            Console.WriteLine($"  opcode 0x{kv.Key:X2}: {kv.Value}x ({AiScript_File.Mnemonic(kv.Key)})");
        Console.WriteLine("VERDICT: FAIL (opcodes fora do corpus no corpus)");
        return 1;
    }
    Console.WriteLine("VERDICT: PASS (corpus 100% Confirmed)");
    return 0;
}

// F5.4: mede a cobertura do detector generico de switch dispatch nos 8 monstros do censo.
// F6.3: round-trip do emitter — compila programa ATEL de alto nível (var persistente, func/call
// macro inline, if/else, while) contra monstros reais e prova que o AiFile compilado sobrevive ao
// codec: AiScript_File.Read(compilado) -> Write -> byte-idêntico.
static int AtelRoundTripRt0(IReadOnlyList<string> files, string root, string? jsonOut)
{
    Console.WriteLine("=== AiScriptLab --atel-round-trip-rt0 (F6.3: round-trip do emitter L3/L5) ===");
    const string SampleProgram =
        "// F6.3: var persistente + func/call (macro inline) + if/else + while + assign\n"
        + "var battleVar0014 = 3;\n"
        + "func fase_extra {\n"
        + "  battleVar0014 = battleVar0014 + 1;\n"
        + "}\n"
        + "if battleVar0014 < 5 {\n"
        + "  battleVar0014 = battleVar0014 + 1;\n"
        + "} else {\n"
        + "  battleVar0014 = 0;\n"
        + "}\n"
        + "while battleVar0014 > 2 {\n"
        + "  battleVar0014 = battleVar0014 - 1;\n"
        + "}\n"
        + "call fase_extra;\n";

    string[] ids = { "m001", "m124", "m125", "m127", "m131", "m141", "m230" };
    int pass = 0, total = 0;
    foreach (string id in ids)
    {
        string? path = files.FirstOrDefault(p =>
            string.Equals(Path.GetFileNameWithoutExtension(p), id, StringComparison.OrdinalIgnoreCase));
        if (path == null) { Console.WriteLine($"{id}: SKIP (arquivo ausente)"); continue; }
        byte[] monster = File.ReadAllBytes(path);
        byte[]? ai = AiScript_File.SliceAiFileFromMonster(monster);
        if (ai == null) { Console.WriteLine($"{id}: FAIL (ai slice)"); continue; }
        AiScriptFile script;
        try { script = AiScript_File.Read(ai); }
        catch (Exception ex) { Console.WriteLine($"{id}: FAIL (parse {ex.Message})"); continue; }
        AiWorker? worker = AiAutomation.PickCombatWorker(script);
        if (worker == null) { Console.WriteLine($"{id}: FAIL (sem worker combate)"); continue; }
        int entrypoint = AiAutomation.PickMainEntrypoint(script, worker);

        AtelCompileResult result;
        try
        {
            AtelProgram program = new AtelParser(SampleProgram).ParseProgram();
            result = AtelProgramCompiler.CompileProgram(program, new AtelCompileContext
            {
                Script = script,
                WorkerIndex = worker.Index,
                EntrypointIndex = entrypoint,
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{id}: SKIP (emit incompativel com o monstro: {ex.GetType().Name}: {ex.Message})");
            continue;
        }
        total++;

        byte[] compiled = result.AiFile;
        byte[] rewritten;
        try
        {
            AiScriptFile reread = AiScript_File.Read(compiled);
            rewritten = AiScript_File.Write(reread);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{id}: FAIL (reread {ex.GetType().Name}: {ex.Message})");
            continue;
        }
        bool identity = rewritten.SequenceEqual(compiled);
        Console.WriteLine($"{id}: emit {result.AppliedSteps} stmt(s) | bytes={compiled.Length} roundtrip={(identity ? "byte-identical" : "DIVERGE")} | {result.Summary}");
        if (identity) pass++;
    }
    Console.WriteLine($"round-trip: {pass}/{total} PASS");
    bool ok = pass == total && total > 0;
    Console.WriteLine(ok ? "VERDICT: PASS" : "VERDICT: FAIL");
    if (jsonOut != null)
        File.WriteAllText(jsonOut, JsonSerializer.Serialize(new { gate = "atel-round-trip-rt0", pass, total }));
    return ok ? 0 : 1;
}

static int GenericSwitchRt0(IReadOnlyList<string> files, string root, string? jsonOut)
{
    Console.WriteLine("=== AiScriptLab --generic-switch-rt0 (F5.4: detector generico de switch dispatch) ===");
    string[] ids = { "m212", "m218", "m226", "m227", "m230", "m277", "m279", "m280" };
    int total = 0, withUnits = 0, bcWithUnits = 0, bcEditableMonsters = 0, bcEditableSlots = 0;
    var summary = new List<object>();
    foreach (string id in ids)
    {
        string? path = files.FirstOrDefault(c => string.Equals(Path.GetFileNameWithoutExtension(c), id, StringComparison.OrdinalIgnoreCase));
        if (path == null) { Console.WriteLine($"{id}: FAIL (monster not found)"); continue; }
        byte[] monster = File.ReadAllBytes(path);
        byte[]? aiFile = AiScript_File.SliceAiFileFromMonster(monster);
        if (aiFile == null) { Console.WriteLine($"{id}: FAIL (ai slice)"); continue; }
        AiScriptFile script;
        try { script = AiScript_File.Read(aiFile); }
        catch (Exception ex) { Console.WriteLine($"{id}: FAIL (parse {ex.Message})"); continue; }
        var units = AiAutomation.DetectIndirectDispatchUnits(monster, script);
        int editableSlots = units.Sum(u => u.EditableTargetSlots.Count(s => s.CanEdit));
        total++;
        if (units.Count > 0) withUnits++;
        var bcUnits = AiAutomation.DetectBattleCallSiteUnits(script);
        int bcEditable = bcUnits.Sum(u => u.EditableTargetSlots.Count(s => s.CanEdit));
        if (bcUnits.Count > 0) bcWithUnits++;
        if (bcEditable > 0) bcEditableMonsters++;
        bcEditableSlots += bcEditable;
        // F5.4: classificação estrutural por monstro — calls classic perform vs família 0x70xx,
        // route writes (PUSHII cmd 0x70xx -> POPV), vars A0 e histograma de call ids.
        var calls = script.Instructions.Select((ins, idx) => (ins, idx))
            .Where(t => t.ins.Opcode == 0xB5 || t.ins.Opcode == 0xD8)
            .ToList();
        int callsPerform = calls.Count(t => t.ins.Operand == 0x700B || t.ins.Operand == 0x705A);
        int calls70 = calls.Count(t => (t.ins.Operand & 0xF000) == 0x7000);
        int callsOther = calls.Count(t => (t.ins.Operand & 0xF000) != 0x7000 && t.ins.Operand != 0);
        var routeWrites = script.Instructions.Select((ins, idx) => (ins, idx))
            .Where(t => t.idx > 0 && t.ins.Opcode == 0xA0 && script.Instructions[t.idx - 1].Opcode == 0xAE
                && (script.Instructions[t.idx - 1].Operand & 0xF000) == 0x7000)
            .Select(t => $"{t.ins.Operand:X4}:{script.Instructions[t.idx - 1].Operand:X4}")
            .ToArray();
        var a0Vars = script.Instructions.Where(i => i.Opcode == 0xA0).Select(i => i.Operand).Distinct().OrderBy(v => v).ToArray();
        var callIdHistogram = calls.Where(t => (t.ins.Operand & 0xF000) == 0x7000)
            .GroupBy(t => t.ins.Operand)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Key:X4}x{g.Count()}");
        string classification = units.Count > 0
            ? "unidades detectadas (indirect-dispatch)"
            : callsPerform >= 2 && routeWrites.Length >= 2
                ? "candidato indirect-dispatch (consumers + route writes, detector nao pegou)"
                : calls70 >= 5 && routeWrites.Length == 0
                    ? "battle-call puro (sem route writes)"
                    : "misto/indefinido";
        Console.WriteLine($"{id}: units={units.Count} slots={editableSlots} callsPerform={callsPerform} calls70={calls70} callsOutros={callsOther} routeWrites={routeWrites.Length} varsA0={a0Vars.Length} callIds=[{string.Join(",", callIdHistogram)}]");
        Console.WriteLine($"  class: {classification}");
        if (units.Count == 0 && id == "m230")
        {
            var aaCalls = script.Instructions.Select((ins, idx) => (ins, idx)).Where(t => t.ins.Opcode == 0xB5 && t.ins.Operand == 0x70AA).Take(4);
            foreach (var (ins, idx) in aaCalls)
            {
                var prev = script.Instructions.Skip(Math.Max(0, idx - 6)).Take(6).Select(c => $"{c.Opcode:X2}:{c.Operand:X4}").ToArray();
                Console.WriteLine($"  70AA@{ins.Offset:X4} [{string.Join(" ", prev)}]");
            }
        }
        summary.Add(new { monster = id, genericUnits = units.Count, genericEditableSlots = editableSlots, battleCallUnits = bcUnits.Count, battleCallEditableSlots = bcEditable });
    }
    Console.WriteLine($"cobertura generic-switch (diagnostico): {withUnits}/{total} (esperado 0/8 - familia battle-call puro)");
    Console.WriteLine($"cobertura battle-call reader: {bcWithUnits}/{total} monstros com call sites; {bcEditableMonsters}/{total} com slot editavel; {bcEditableSlots} slots editaveis no total");
    bool pass = bcWithUnits == total && bcEditableMonsters == total;
    Console.WriteLine(pass ? "VERDICT: PASS" : "VERDICT: FAIL");
    if (jsonOut != null)
        File.WriteAllText(jsonOut, JsonSerializer.Serialize(new { gate = "generic-switch-rt0", summary }));
    return pass ? 0 : 1;
}
