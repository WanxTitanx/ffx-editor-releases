using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

using FFXProjectEditor.Resources;
using FFXProjectEditor.Services;

namespace FFXProjectEditor.FfxLib.Ps3
{
    /// <summary>
    /// ⚠ REQUER MAIS ANÁLISES: Classification proved by Hex-Rays decompile for 7 DLLs
    /// across all 5 slot-kind buckets. The 581-DLL mass classification via
    /// <c>scripts/scan_ab.py</c> is functional (byte-scan of slot 0) but NOT validated
    /// by decompilation on a random sample per family. See
    /// <see cref="docs/reverse/FFX_MAGIC_DLL_BUCKET_COMPLETE_CLASSIFICATION_2026-06-13.md"/>
    /// for the provisory distribution and pending validation steps.
    /// </summary>
    internal enum MagicDllEffectFamily
    {
        Unknown = 0,
        /// <summary>Particle self-contained. Pool 1023, no root, slot1=draw tick. ~261 DLLs (pending validation).</summary>
        A_ParticleSelfContained,
        /// <summary>Root/record-interpreter via host+2860/2864 (sub_80CD60). ~141 DLLs total: 130 via byte-scan + 11 via slot-kind.</summary>
        B_RootRecordInterpreter,
        /// <summary>Root self-governed params: host+2844/2840/1556/1040. No EXE interpreter. ~63 DLLs: 13 infiltrated in bucket 404x + 9+41 via slot-kind.</summary>
        C_RootSelfGovernedParam,
        /// <summary>Ego tasklist: no root, EgoTask/Ctrl/ListItm strings, FSM dispatch. 116 DLLs via slot-kind.</summary>
        D_EgoTasklist
    }

    internal static class MagicDllSemanticAnalyzer
    {
#if FFX_INCLUDE_DEVTOOLS
        public const string DefaultFfxMagicFilesRoot = @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\magicFiles\FFX";
        public const string DefaultFfx2MagicFilesRoot = @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\magicFiles\FFX-2";
#else
        public static string DefaultFfxMagicFilesRoot => PortablePathResolver.MagicFilesRoot("FFX") ?? string.Empty;
        public static string DefaultFfx2MagicFilesRoot => PortablePathResolver.MagicFilesRoot("FFX-2") ?? string.Empty;
#endif

        /// <summary>
        /// Detect effect family via <see cref="MagicDllFamilyClassifier"/> (slot-kind buckets
        /// + slot-0 byte scan ported from <c>scripts/scan_ab.py</c>).
        /// </summary>
        public static MagicDllEffectFamily DetectFamily(MagicDllInspection inspection) =>
            MagicDllFamilyClassifier.DetectFamily(inspection);

        public static MagicDllFamilyClassification ClassifyFamily(MagicDllInspection inspection) =>
            MagicDllFamilyClassifier.Classify(inspection);

        public static IReadOnlyList<MagicDllSlotSemanticCandidate> AnalyzeOverlaySlots(MagicDllInspection inspection)
        {
            if (inspection.OverlayEvidence == null)
                return [];

            MagicDllEffectFamily family = DetectFamily(inspection);
            List<MagicDllSlotSemanticCandidate> rows = [];
            foreach (MagicDllOverlaySlot slot in inspection.OverlayEvidence.Slots)
                rows.Add(BuildSlotCandidate(slot, family));
            return rows;
        }

        public static IReadOnlyList<MagicDllStringFamilyStat> AnalyzeStringFamilies(MagicDllInspection inspection)
        {
            Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<string>> examples = new(StringComparer.OrdinalIgnoreCase);
            foreach (MagicDllAsciiString s in inspection.Strings)
            {
                string family = ClassifyStringFamily(s.Value);
                counts[family] = counts.GetValueOrDefault(family) + 1;
                if (!examples.TryGetValue(family, out List<string>? list))
                {
                    list = [];
                    examples[family] = list;
                }
                if (list.Count < 12 && !list.Contains(s.Value, StringComparer.Ordinal))
                    list.Add(s.Value);
            }

            return counts
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => new MagicDllStringFamilyStat(kv.Key, kv.Value, string.Join(", ", examples[kv.Key].Take(8))))
                .ToList();
        }

        public static IReadOnlyList<MagicDllHostFieldRole> HostFieldRoles() =>
        [
            new(672, "Host_GetActorOrOwnerIndex_candidate", "medium", "DLL 0611 calls host+672 with the magic actor/effect id and then derives target-side ids; name remains candidate."),
            new(844, "Host_SetActorRenderOrVisibilityFlag_candidate", "low", "Seen in complex effects toggling actor-related state; exact field remains open."),
            new(856, "Host_DestroyOrReleaseEffectTask_candidate", "medium", "Simple DLL 0003 calls host+856 when its EgoTask list drains; likely release/finish for an effect task."),
            new(884, "Host_SetEffectTimerRangeA_candidate", "medium", "Many DLLs pass effect object plus start/end/rate-like integers; paired with host+888."),
            new(888, "Host_SetEffectTimerRangeB_candidate", "medium", "Many DLLs pass effect object plus start/end/rate-like integers; paired with host+884."),
            new(900, "Host_CheckEffectPhaseOrTimer_candidate", "medium", "Simple DLLs use this as a gate before advancing internal phase counters."),
            new(904, "Host_StartOrSignalEffectObject_candidate", "medium", "Simple DLLs call it after timer checks to signal an effect object."),
            new(908, "Host_GetEffectObjectProgress_candidate", "medium", "Simple DLLs compare this result against >= 1 before continuing."),
            new(1120, "Host_GetCurrentBattleContextId_candidate", "medium", "Complex DLL 0611 compares host+1120 return values against fixed ids to branch by context."),
            new(1152, "Host_GetTargetListOrActorSet_candidate", "medium", "Complex DLL 0611 asks for an actor list and count-like output."),
            new(1156, "Host_GetActorHeightY_candidate", "medium", "Complex DLLs use it while computing average target positions; exact semantic still needs naming pass."),
            new(1160, "Host_GetActorPosition_candidate", "medium", "Complex DLLs pass actor id and a vector buffer; used in position math."),
            new(1172, "Host_IsActorSideOrEnemy_candidate", "medium", "Complex DLLs branch owner/target side around this check."),
            new(1176, "Host_GetRelatedActorId_candidate", "low", "Used near side checks; precise role still open."),
            new(1184, "Host_SetActorEffectFlag_candidate", "low", "Used around target/effect transitions in complex DLLs."),
            new(1228, "Host_RandomFloat_candidate", "medium", "Complex DLLs repeatedly call host+1228 and scale the returned float for random offsets."),
            new(1244, "Host_ReleaseRegisteredTask_candidate", "medium", "Complex DLL 0611 calls it in final cleanup state on a handle created through host+1248."),
            new(1248, "Host_RegisterTaskCallback_candidate", "medium", "Complex DLL 0611 registers a callback-like function pointer through this slot."),
            new(1260, "Host_SetPostEffectOrScreenState_candidate", "low", "Seen in visual cleanup/fade paths; exact role open."),
            new(1300, "Host_CopyVectorOrMatrix_candidate", "medium", "Complex DLLs copy 3D/vector-like values through host+1300."),
            new(1324, "Host_ResetMatrixStackA_candidate", "low", "Used with host+1328 and transform setup; exact role open."),
            new(1328, "Host_ResetMatrixStackB_candidate", "low", "Used with host+1324 and transform setup; exact role open."),
            new(1412, "Host_SubmitTransformA_candidate", "low", "Used in complex transform update callbacks."),
            new(1588, "Host_SubmitTransformB_candidate", "low", "Used in complex transform update callbacks."),
            new(2172, "Host_SetBlendOrDrawMode_candidate", "low", "Seen in draw/update setup near host+1000."),
            new(2196, "Host_CheckObjectDone_candidate", "medium", "Simple DLL 0003 uses this as one gate before signaling object state."),
            new(2220, "Host_InitTransformResourceA_candidate", "low", "Used at complex effect bootstrap."),
            new(2224, "Host_InitTransformResourceB_candidate", "low", "Used at complex effect bootstrap."),
            new(2840, "Host_AttachEffectObjectToRuntime_candidate", "medium", "Simple DLLs attach local effect state to a runtime object after creating/registering PPP resources."),
            new(2844, "Host_RegisterPppPrimitiveNames_candidate", "medium", "Simple DLL 0003 passes names like pppAccele through host+2844 during bootstrap."),
            new(2852, "Host_BindDrawResource_candidate", "low", "Complex DLL 0611 repeatedly binds draw/effect resources through host+2852."),
            new(2860, "Host_MaterializeRuntimeRoot_structural", "high", "Pass reports and DLL 0148 create path show host+2860 materializing the magic runtime root/instance."),
            new(2864, "Host_RunPhaseRecordInterpreter_sub80CD60", "high", "IDA decompile 2026-06-12: sub_80CD60 walks phase records, dispatches opcode handlers, and calls overlay function pointers."),
            new(2872, "Host_InitEgoOrPppObject_candidate", "medium", "DLLs call this before host+2876 to initialize Ego/PPP objects."),
            new(2876, "Host_CreateEgoOrPppObjectHandle_candidate", "medium", "DLLs call this after host+2872 and keep the returned object handle."),
            new(2880, "Host_SetGlobalEffectMask_candidate", "low", "Seen in complex setup with large bitmask-like constants."),
            new(2884, "Host_RunPhase1SidePass_opp_main_sub80BEA0", "high", "IDA decompile 2026-06-12: sub_80BEA0 runs an auxiliary phase pass and contains the Virtuos/Yonishi opp_main warning."),
            new(2892, "Host_PrePhaseResourceA_candidate", "medium", "DLL 0148 calls this immediately before the phase-1 interpreter pass."),
            new(2896, "Host_PrePhaseResourceB_candidate", "medium", "DLL 0148 calls this immediately before the phase-1 interpreter pass."),
            new(2908, "Host_MakePacketBeforeRoutineSignal_candidate", "medium", "DLL 0148 calls this after detecting root+88 changed and logging 'make packet in before routine'."),
            new(2964, "HostContext_GlobalMagicWorkPtr_candidate", "low", "Copied by every InitMagicPRX into a DLL global; raw target in EXE host dump is unk_230FFE0."),
            new(2968, "HostContext_Root88FallbackPointer_candidate", "medium", "Copied/dereferenced by InitMagicPRX; pass11 ties dword_2332E8C to root+84/root+88 fallback behavior."),
            new(2972, "HostContext_OverlayFunctionVector16_candidate", "medium", "InitMagicPRX copies 16 dwords from this pointer into a local DLL vector."),
            new(3036, "HostContext_DefaultPacketTable_off_C498DC", "medium", "sub_817200 writes off_C498DC into root clone fields while updating root+84/root+88."),
            new(3212, "Host_AllocateEffectMemory_candidate", "medium", "DLL 0148 allocates a 1024000-byte root/work block through this host slot."),
            new(3264, "Host_PostMaterializeNoopOrFinalize_candidate", "low", "DLL 0148 calls this after materializing runtime root; target was nullsub in host dump."),
            new(3740, "HostContext_LateRuntimeTableA_candidate", "low", "Copied by InitMagicPRX from host context; raw target dword_1940AC4."),
            new(3744, "HostContext_LateRuntimeTableB_candidate", "low", "Copied by InitMagicPRX from host context; raw target dword_12FB790.")
        ];

        public static MagicDllCorpusSemanticAnalysis AnalyzeRoot(string gameLabel, string rootPath, string repoRoot, bool attachFfxOverlayEvidence)
        {
            List<MagicDllSemanticDllSummary> dlls = [];
            List<string> notes = [];
            if (!Directory.Exists(rootPath))
            {
                notes.Add($"{gameLabel}: root not found: {rootPath}");
                return new MagicDllCorpusSemanticAnalysis(gameLabel, rootPath, DateTimeOffset.UtcNow, 0, 0, 0, [], [], [], [], HostFieldRoles(), notes);
            }

            string overlayRoot = attachFfxOverlayEvidence ? repoRoot : Path.Combine(repoRoot, "__no_overlay_for_" + gameLabel);
            List<string> files = Directory.EnumerateFiles(rootPath, "magic_*.dll", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int inspected = 0;
            int withOverlay = 0;
            foreach (string file in files)
            {
                try
                {
                    MagicDllInspection inspection = MagicDllDecompiler.Inspect(file, overlayRoot);
                    inspected++;
                    if (inspection.OverlayEvidence != null)
                        withOverlay++;
                    dlls.Add(BuildDllSummary(gameLabel, inspection));
                }
                catch (Exception ex)
                {
                    dlls.Add(new MagicDllSemanticDllSummary(
                        gameLabel,
                        file,
                        Path.GetFileName(file),
                        null,
                        0,
                        string.Empty,
                        string.Empty,
                        false,
                        MagicDllEffectFamily.Unknown.ToString(),
                        string.Empty,
                        string.Empty,
                        0,
                        0,
                        0,
                        0,
                        [],
                        [],
                        [],
                        [$"Inspect failed: {ex.GetType().Name}: {ex.Message}"]));
                }
            }

            return new MagicDllCorpusSemanticAnalysis(
                gameLabel,
                rootPath,
                DateTimeOffset.UtcNow,
                files.Count,
                inspected,
                withOverlay,
                dlls,
                BuildSlotStats(dlls),
                BuildSignatureStats(dlls),
                BuildStringFamilyStats(dlls),
                HostFieldRoles(),
                notes);
        }

        public static MagicDllSemanticReportFiles WriteReport(MagicDllCorpusSemanticAnalysis ffx, MagicDllCorpusSemanticAnalysis? ffx2, string outputDir)
        {
            Directory.CreateDirectory(outputDir);
            string jsonPath = Path.Combine(outputDir, "magic_dll_semantics.json");
            string dllCsvPath = Path.Combine(outputDir, "magic_dll_semantics_dlls.csv");
            string slotCsvPath = Path.Combine(outputDir, "magic_dll_semantics_slots.csv");
            string hostCsvPath = Path.Combine(outputDir, "magic_dll_host_fields.csv");
            string markdownPath = Path.Combine(outputDir, "MAGIC_DLL_SEMANTIC_RE_REPORT.md");

            object payload = ffx2 == null ? new { ffx } : new { ffx, ffx2 };
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            MagicDllCorpusSemanticAnalysis[] corpora = ffx2 == null ? [ffx] : [ffx, ffx2];
            File.WriteAllText(dllCsvPath, BuildDllCsv(corpora));
            File.WriteAllText(slotCsvPath, BuildSlotCsv(corpora));
            File.WriteAllText(hostCsvPath, BuildHostCsv(HostFieldRoles()));
            File.WriteAllText(markdownPath, BuildMarkdown(ffx, ffx2));

            return new MagicDllSemanticReportFiles(jsonPath, dllCsvPath, slotCsvPath, hostCsvPath, markdownPath);
        }

        static MagicDllSemanticDllSummary BuildDllSummary(string gameLabel, MagicDllInspection inspection)
        {
            IReadOnlyList<MagicDllSlotSemanticCandidate> slots = AnalyzeOverlaySlots(inspection);
            IReadOnlyList<MagicDllStringFamilyStat> stringFamilies = AnalyzeStringFamilies(inspection);
            int pppCount = inspection.Strings.Count(s => s.Value.StartsWith("ppp", StringComparison.Ordinal) && LooksLikeEngineIdentifier(s.Value));
            int egoCount = inspection.Strings.Count(s => s.Value.StartsWith("Ego", StringComparison.Ordinal) && LooksLikeEngineIdentifier(s.Value));
            int diagCount = inspection.Strings.Count(s => s.Value.Contains("Virtuos", StringComparison.OrdinalIgnoreCase)
                || s.Value.Contains("Yonishi", StringComparison.OrdinalIgnoreCase));

            MagicDllEffectFamily family = DetectFamily(inspection);
            return new MagicDllSemanticDllSummary(
                gameLabel,
                inspection.FilePath,
                inspection.FileName,
                inspection.MagicId,
                inspection.FileSize,
                inspection.Sha256,
                inspection.MachineName,
                inspection.OverlayEvidence != null,
                family.ToString(),
                inspection.OverlayEvidence?.SlotKindSignature ?? string.Empty,
                inspection.OverlayEvidence?.HostOffsetSignature ?? string.Empty,
                inspection.Exports.Count,
                inspection.Imports.Sum(lib => lib.Imports.Count),
                inspection.Strings.Count,
                pppCount + egoCount + diagCount,
                slots,
                stringFamilies,
                inspection.Imports.Select(i => i.Library).ToList(),
                inspection.Warnings);
        }

        static MagicDllSlotSemanticCandidate BuildSlotCandidate(MagicDllOverlaySlot slot, MagicDllEffectFamily family)
        {
            if (string.Equals(slot.Kind, "null", StringComparison.OrdinalIgnoreCase) || string.Equals(slot.VirtualAddress, "0x0", StringComparison.OrdinalIgnoreCase))
                return new MagicDllSlotSemanticCandidate(slot.Index, "OverlaySlotUnused", "Unused/null slot.", "high", $"Slot {slot.Index:D2} is null for {family}.");

            if (string.Equals(slot.Kind, "data", StringComparison.OrdinalIgnoreCase))
            {
                string dataRole = family switch
                {
                    MagicDllEffectFamily.B_RootRecordInterpreter => "Phase record table used by the EXE interpreter (host+2864).",
                    MagicDllEffectFamily.C_RootSelfGovernedParam => "Param/resource table used by slot 4 dispatch.",
                    _ => "Resource/record/table metadata."
                };
                return new MagicDllSlotSemanticCandidate(slot.Index, "OverlayResourceOrRecordTable", dataRole, "medium", $"Slot {slot.Index:D2} data at {slot.Section}.");
            }

            return family switch
            {
                MagicDllEffectFamily.A_ParticleSelfContained => slot.Index switch
                {
                    0 => new(slot.Index, "OverlayA_InitPoolAndEgo", "Bootstrap: zero particle pool (1023), init matrices, create Ego, set timer [0,1000].", "high", "Decompiled: magic_0084/0688 slot0."),
                    1 => new(slot.Index, "OverlayA_DrawTick", "Per-frame draw: submit transform, RGBA, scale, blend, draw via host+1000.", "high", "Decompiled: magic_0084/0688 slot1."),
                    3 => new(slot.Index, "OverlayA_PhaseGateStub", "Phase gate stub or variant; may auto-rewrite overlay to advance phase.", "medium", "Decompiled: magic_0688 slot3 rewrites slot3/4 pointers."),
                    4 => new(slot.Index, "OverlayA_PhaseGateAdvance", "Phase gate: host+900/904/908 checks, then auto-rewrite overlay to advance phase.", "high", "Decompiled: magic_0084/0688 slot4."),
                    _ => new(slot.Index, $"OverlayA_Stub{slot.Index:D2}", "Stub callback in family A.", "medium", "Most family A DLLs reuse a 3-byte return-0.")
                },
                MagicDllEffectFamily.B_RootRecordInterpreter => slot.Index switch
                {
                    0 => new(slot.Index, "OverlayB_MaterializeRoot", "Materialize runtime root: alloc (host+3212), init Ego (host+2872), create handle (host+2876), MaterializeRuntimeRoot (host+2860).", "high", "Decompiled: magic_0148/0208 slot0."),
                    1 => new(slot.Index, "OverlayB_ResolveActorAndFree", "Resolve actor (host+672), get/set facing (host+676/680), free root (host+3216), zero g_RuntimeRoot.", "high", "Decompiled: magic_0148/0208 slot1."),
                    3 => new(slot.Index, "OverlayB_RunInterpreterPhase0", "RunPhaseInterpreter phase 0: root+84=88, host+2864(1,0), log 'make packet in before routine', host+2908.", "high", "Decompiled: magic_0148/0208 slot3."),
                    4 => new(slot.Index, "OverlayB_RunInterpreterPhase1", "PrePhaseResource (host+2892/2896), host+2864(1,1), RunPhase1SidePass (host+2884).", "high", "Decompiled: magic_0148/0208 slot4."),
                    _ => new(slot.Index, $"OverlayB_FamilyCallback{slot.Index:D2}", $"Family B optional callback at slot {slot.Index:D2}.", "medium", "Real callbacks in bucket 11x, stubs elsewhere.")
                },
                MagicDllEffectFamily.C_RootSelfGovernedParam => slot.Index switch
                {
                    0 => new(slot.Index, "OverlayC_AllocRootAndParsePpp", "Alloc root (host+3212), init Ego (host+2872), create handles×N (host+2876), ParseEgoPppResource (host+2844), BindResourceToBuffer (host+2840).", "high", "Decompiled: magic_0098/0045 slot0."),
                    1 => new(slot.Index, "OverlayC_FreeRoot", "Free root: host+3216(root) cleanup.", "high", "Decompiled: magic_0098/0045 slot1."),
                    3 => new(slot.Index, "OverlayC_PhaseGateOwn", "Self-governed phase gate: host+900/904/908 checks.", "high", "Decompiled: magic_0098/0045 slot3."),
                    4 => new(slot.Index, "OverlayC_PhaseAdvanceAndDispatch", "PrePhaseResource (host+2892/2896) + param dispatch (host+1556/1040/1044) + release (host+856).", "high", "Decompiled: magic_0098/0045 slot4."),
                    _ => new(slot.Index, $"OverlayC_FamilyCallback{slot.Index:D2}", $"Family C optional at slot {slot.Index:D2}.", "medium", "May be stub or real callback.")
                },
                MagicDllEffectFamily.D_EgoTasklist => slot.Index switch
                {
                    0 => new(slot.Index, "OverlayD_InitEgoTasklist", "Setup EgoTask/EgoCtrl/EgoListItm, create PPP handle (host+2844/2840), timer [0,1000].", "high", "Decompiled: magic_0003 slot0."),
                    1 => new(slot.Index, "OverlayD_Stub", "Stub — no per-frame draw in family D.", "high", "Decompiled: magic_0003 slot1 returns 0."),
                    3 => new(slot.Index, "OverlayD_TaskAdvanceFsm", "Task-advance FSM: host+900(2) timer, host+1252(2) wait, host+2196(2) done, host+904 start, host+908 progress.", "high", "Decompiled: magic_0003 slot3."),
                    4 => new(slot.Index, "OverlayD_ExecuteTaskCycle", "Execute EgoTask list: iterate items, call fn+32 dispatch, release via host+856 when drained.", "high", "Decompiled: magic_0003 slot4."),
                    _ => new(slot.Index, $"OverlayD_FamilyCallback{slot.Index:D2}", $"Family D optional at slot {slot.Index:D2}.", "medium", "May be stub or real callback.")
                },
                _ => slot.Index switch
                {
                    0 => new(slot.Index, "OverlayEntryInitCreate", "Primary DLL entry callback.", "low", "Family unknown."),
                    1 => new(slot.Index, "OverlayEarlyTickOrNoopA", "Early phase callback.", "low", "Family unknown."),
                    3 => new(slot.Index, "OverlayPhaseGateOrPreRoutine", "Phase gate.", "low", "Family unknown."),
                    4 => new(slot.Index, "OverlayMainUpdateOrRoutine", "Main update.", "low", "Family unknown."),
                    _ => new(slot.Index, $"OverlaySlot{slot.Index:D2}Callback", "Generic overlay slot.", "low", $"Slot {slot.Index:D2}.")
                }
            };
        }

        static IReadOnlyList<MagicDllSlotCorpusStat> BuildSlotStats(IReadOnlyList<MagicDllSemanticDllSummary> dlls)
        {
            List<MagicDllSlotCorpusStat> stats = [];
            for (int i = 0; i < 16; i++)
            {
                List<MagicDllSlotSemanticCandidate> rows = dlls.SelectMany(d => d.SlotCandidates.Where(s => s.SlotIndex == i)).ToList();
                int nonNull = rows.Count(r => !r.CandidateName.Equals("OverlaySlotUnused", StringComparison.OrdinalIgnoreCase));
                string topName = rows.GroupBy(r => r.CandidateName).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? string.Empty;
                stats.Add(new MagicDllSlotCorpusStat(i, rows.Count, nonNull, topName));
            }
            return stats;
        }

        static IReadOnlyList<MagicDllSignatureStat> BuildSignatureStats(IReadOnlyList<MagicDllSemanticDllSummary> dlls) =>
            dlls
                .Where(d => !string.IsNullOrWhiteSpace(d.OverlaySlotKindSignature))
                .GroupBy(d => d.OverlaySlotKindSignature)
                .OrderByDescending(g => g.Count())
                .Select(g => new MagicDllSignatureStat(g.Key, g.Count(), string.Join(", ", g.Take(10).Select(d => d.MagicIdText))))
                .ToList();

        static IReadOnlyList<MagicDllStringFamilyStat> BuildStringFamilyStats(IReadOnlyList<MagicDllSemanticDllSummary> dlls)
        {
            Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<string>> examples = new(StringComparer.OrdinalIgnoreCase);
            foreach (MagicDllSemanticDllSummary dll in dlls)
            {
                foreach (MagicDllStringFamilyStat family in dll.StringFamilies)
                {
                    counts[family.Family] = counts.GetValueOrDefault(family.Family) + family.Count;
                    if (!examples.TryGetValue(family.Family, out List<string>? list))
                    {
                        list = [];
                        examples[family.Family] = list;
                    }
                    foreach (string sample in family.Examples.Split(", ", StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (list.Count < 12 && !list.Contains(sample, StringComparer.Ordinal))
                            list.Add(sample);
                    }
                }
            }

            return counts
                .OrderByDescending(kv => kv.Value)
                .Select(kv => new MagicDllStringFamilyStat(kv.Key, kv.Value, string.Join(", ", examples[kv.Key].Take(8))))
                .ToList();
        }

        static string ClassifyStringFamily(string value)
        {
            if (value.StartsWith("ppp", StringComparison.Ordinal))
            {
                if (!LooksLikeEngineIdentifier(value))
                    return "Other ASCII";
                if (value.Contains("Move", StringComparison.Ordinal) || value.Contains("Point", StringComparison.Ordinal) || value.Contains("Angle", StringComparison.Ordinal) || value.Contains("Scale", StringComparison.Ordinal))
                    return "PPP transform/motion primitive";
                if (value.Contains("Col", StringComparison.Ordinal) || value.Contains("Color", StringComparison.Ordinal))
                    return "PPP color primitive";
                if (value.Contains("Draw", StringComparison.Ordinal) || value.Contains("Mdl", StringComparison.Ordinal) || value.Contains("Shape", StringComparison.Ordinal) || value.Contains("Vertex", StringComparison.Ordinal))
                    return "PPP draw/model primitive";
                if (value.Contains("Rand", StringComparison.Ordinal))
                    return "PPP random primitive";
                if (value.Contains("Matrix", StringComparison.Ordinal) || value.Contains("DMat", StringComparison.Ordinal))
                    return "PPP matrix primitive";
                return "PPP particle primitive";
            }
            if (value.StartsWith("Ego", StringComparison.Ordinal))
            {
                if (!LooksLikeEngineIdentifier(value))
                    return "Other ASCII";
                if (value.Contains("Bone", StringComparison.Ordinal) || value.Contains("Chr", StringComparison.Ordinal))
                    return "Ego actor/bone runtime";
                if (value.Contains("Task", StringComparison.Ordinal) || value.Contains("Ctrl", StringComparison.Ordinal) || value.Contains("List", StringComparison.Ordinal))
                    return "Ego task/control runtime";
                if (value.Contains("Se", StringComparison.Ordinal))
                    return "Ego sound runtime";
                return "Ego runtime object";
            }
            if (value.Contains("SeSep", StringComparison.Ordinal) || value.Contains("sound", StringComparison.OrdinalIgnoreCase))
                return "Sound/cue diagnostic";
            if (value.Contains("Virtuos", StringComparison.OrdinalIgnoreCase) || value.Contains("Yonishi", StringComparison.OrdinalIgnoreCase))
                return "Virtuos/Yonishi diagnostic";
            if (value.Contains("shader", StringComparison.OrdinalIgnoreCase) || value.Contains("texture", StringComparison.OrdinalIgnoreCase) || value.Contains("PTexture", StringComparison.Ordinal))
                return "Render/material string";
            return "Other ASCII";
        }

        static bool LooksLikeEngineIdentifier(string value)
        {
            if (value.Length is < 4 or > 64)
                return false;
            return value.All(ch => (ch >= 'A' && ch <= 'Z')
                || (ch >= 'a' && ch <= 'z')
                || (ch >= '0' && ch <= '9')
                || ch == '_');
        }

        static string BuildMarkdown(MagicDllCorpusSemanticAnalysis ffx, MagicDllCorpusSemanticAnalysis? ffx2)
        {
            List<string> lines = [];
            lines.Add("# Magic DLL Semantic RE Report");
            lines.Add("");
            lines.Add($"Generated UTC: `{DateTimeOffset.UtcNow:O}`");
            lines.Add("");
            AppendCorpusSummary(lines, ffx);
            if (ffx2 != null)
                AppendCorpusSummary(lines, ffx2);
            lines.Add("## Current Naming Conclusions");
            lines.Add("");
            lines.Add("- `GetEffectOverlayTable` returns a callback/data interface, not a texture playlist.");
            lines.Add("- `InitMagicPRX` copies a host context table from `FFX.exe` into DLL globals. The DLL field names therefore come from the EXE-side contract.");
            lines.Add("- `host+2864` is now named candidate `Host_RunPhaseRecordInterpreter_sub80CD60`: IDA shows it walks phase records and dispatches overlay/opcode function pointers.");
            lines.Add("- `host+2884` is now named candidate `Host_RunPhase1SidePass_opp_main_sub80BEA0`: IDA shows the auxiliary `opp_main` pass and Virtuos/Yonishi warning string.");
            lines.Add("- `sub_817200` remains the strongest structural writer of `root+84/root+88` for opcode family `0x1000`.");
            lines.Add("");
            lines.Add("## Overlay Slot Role Candidates");
            lines.Add("");
            lines.Add("| Slot | Candidate | Confidence | Evidence |");
            lines.Add("| --- | --- | --- | --- |");
            foreach (MagicDllSlotSemanticCandidate c in Enumerable.Range(0, 16).Select(i => BuildSlotCandidate(new MagicDllOverlaySlot(i, "sample", "0xSAMPLE", i == 7 ? "data" : "code", i == 7 ? ".data" : ".text"), MagicDllEffectFamily.Unknown)))
                lines.Add($"| {c.SlotIndex:D2} | `{c.CandidateName}` | {c.Confidence} | {Escape(c.Evidence)} |");
            lines.Add("");
            lines.Add("## Host Field Role Candidates");
            lines.Add("");
            lines.Add("| Offset | Candidate | Confidence | Evidence |");
            lines.Add("| ---: | --- | --- | --- |");
            foreach (MagicDllHostFieldRole h in HostFieldRoles())
                lines.Add($"| {h.Offset} / 0x{h.Offset:X} | `{h.CandidateName}` | {h.Confidence} | {Escape(h.Evidence)} |");
            lines.Add("");
            lines.Add("## Guardrail");
            lines.Add("");
            lines.Add("This report is a naming pass. `high` means strong structural/IDA evidence, not a final source-level name from Square/Virtuos. Patch UI must keep these as role candidates until RT2/gameplay or deeper IDA naming proves exact semantics.");
            lines.Add("");
            return string.Join(Environment.NewLine, lines);
        }

        static void AppendCorpusSummary(List<string> lines, MagicDllCorpusSemanticAnalysis corpus)
        {
            lines.Add($"## {corpus.GameLabel} Corpus");
            lines.Add("");
            lines.Add($"- Root: `{corpus.RootPath}`");
            lines.Add($"- DLLs found: `{corpus.TotalDlls}`");
            lines.Add($"- DLLs inspected: `{corpus.InspectedDlls}`");
            lines.Add($"- With FFX overlay CSV evidence: `{corpus.WithOverlayEvidence}`");
            lines.Add("");
            if (corpus.SignatureStats.Count > 0)
            {
                lines.Add("Top overlay signatures:");
                foreach (MagicDllSignatureStat s in corpus.SignatureStats.Take(8))
                    lines.Add($"- `{s.Count}` x `{s.Signature}` (examples: {s.ExampleMagicIds})");
                lines.Add("");
            }
            if (corpus.StringFamilyStats.Count > 0)
            {
                lines.Add("String families:");
                foreach (MagicDllStringFamilyStat f in corpus.StringFamilyStats.Take(10))
                    lines.Add($"- `{f.Family}`: {f.Count} samples ({f.Examples})");
                lines.Add("");
            }
        }

        static string BuildDllCsv(IEnumerable<MagicDllCorpusSemanticAnalysis> corpora)
        {
            List<string> lines = ["game,file,magic_id,size,sha256,machine,has_overlay,slot_signature,host_offsets,exports,imports,strings,semantic_string_hits,warnings"];
            foreach (MagicDllSemanticDllSummary d in corpora.SelectMany(c => c.Dlls))
            {
                lines.Add(string.Join(",",
                    Csv(d.GameLabel),
                    Csv(d.FileName),
                    Csv(d.MagicIdText),
                    d.FileSize.ToString(CultureInfo.InvariantCulture),
                    Csv(d.Sha256),
                    Csv(d.MachineName),
                    d.HasOverlayEvidence ? "true" : "false",
                    Csv(d.OverlaySlotKindSignature),
                    Csv(d.HostOffsetSignature),
                    d.ExportCount.ToString(CultureInfo.InvariantCulture),
                    d.ImportCount.ToString(CultureInfo.InvariantCulture),
                    d.StringCount.ToString(CultureInfo.InvariantCulture),
                    d.SemanticStringHitCount.ToString(CultureInfo.InvariantCulture),
                    Csv(string.Join(" | ", d.Warnings))));
            }
            return string.Join(Environment.NewLine, lines) + Environment.NewLine;
        }

        static string BuildSlotCsv(IEnumerable<MagicDllCorpusSemanticAnalysis> corpora)
        {
            List<string> lines = ["game,file,magic_id,slot,candidate,confidence,meaning,evidence"];
            foreach (MagicDllSemanticDllSummary d in corpora.SelectMany(c => c.Dlls))
            {
                foreach (MagicDllSlotSemanticCandidate s in d.SlotCandidates)
                {
                    lines.Add(string.Join(",",
                        Csv(d.GameLabel),
                        Csv(d.FileName),
                        Csv(d.MagicIdText),
                        s.SlotIndex.ToString(CultureInfo.InvariantCulture),
                        Csv(s.CandidateName),
                        Csv(s.Confidence),
                        Csv(s.Meaning),
                        Csv(s.Evidence)));
                }
            }
            return string.Join(Environment.NewLine, lines) + Environment.NewLine;
        }

        static string BuildHostCsv(IEnumerable<MagicDllHostFieldRole> roles)
        {
            List<string> lines = ["offset,offset_hex,candidate,confidence,evidence"];
            foreach (MagicDllHostFieldRole h in roles)
                lines.Add(string.Join(",", h.Offset.ToString(CultureInfo.InvariantCulture), Csv("0x" + h.Offset.ToString("X", CultureInfo.InvariantCulture)), Csv(h.CandidateName), Csv(h.Confidence), Csv(h.Evidence)));
            return string.Join(Environment.NewLine, lines) + Environment.NewLine;
        }

        static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);

        static string Csv(string value)
        {
            value ??= string.Empty;
            if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
                return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
            return value;
        }
    }

    internal sealed record MagicDllSlotSemanticCandidate(int SlotIndex, string CandidateName, string Meaning, string Confidence, string Evidence)
    {
        public string SlotDisplay => $"slot {SlotIndex:D2}";
    }

    internal sealed record MagicDllStringFamilyStat(string Family, int Count, string Examples)
    {
        public string CountDisplay => Count.ToString("N0", CultureInfo.CurrentCulture);
    }

    internal sealed record MagicDllHostFieldRole(int Offset, string CandidateName, string Confidence, string Evidence)
    {
        public string OffsetDisplay => $"{Offset} / 0x{Offset:X}";
    }

    internal sealed record MagicDllSlotCorpusStat(int SlotIndex, int Rows, int NonNullRows, string TopCandidateName);
    internal sealed record MagicDllSignatureStat(string Signature, int Count, string ExampleMagicIds);

    internal sealed record MagicDllSemanticDllSummary(
        string GameLabel,
        string FullPath,
        string FileName,
        int? MagicId,
        long FileSize,
        string Sha256,
        string MachineName,
        bool HasOverlayEvidence,
        string EffectFamily,
        string OverlaySlotKindSignature,
        string HostOffsetSignature,
        int ExportCount,
        int ImportCount,
        int StringCount,
        int SemanticStringHitCount,
        IReadOnlyList<MagicDllSlotSemanticCandidate> SlotCandidates,
        IReadOnlyList<MagicDllStringFamilyStat> StringFamilies,
        IReadOnlyList<string> ImportLibraries,
        IReadOnlyList<string> Warnings)
    {
        public string MagicIdText => MagicId.HasValue ? MagicId.Value.ToString("D4", CultureInfo.InvariantCulture) : "-";
    }

    internal sealed record MagicDllCorpusSemanticAnalysis(
        string GameLabel,
        string RootPath,
        DateTimeOffset GeneratedUtc,
        int TotalDlls,
        int InspectedDlls,
        int WithOverlayEvidence,
        IReadOnlyList<MagicDllSemanticDllSummary> Dlls,
        IReadOnlyList<MagicDllSlotCorpusStat> SlotStats,
        IReadOnlyList<MagicDllSignatureStat> SignatureStats,
        IReadOnlyList<MagicDllStringFamilyStat> StringFamilyStats,
        IReadOnlyList<MagicDllHostFieldRole> HostFieldRoles,
        IReadOnlyList<string> Notes);

    internal sealed record MagicDllSemanticReportFiles(string JsonPath, string DllCsvPath, string SlotCsvPath, string HostCsvPath, string MarkdownPath);
}
