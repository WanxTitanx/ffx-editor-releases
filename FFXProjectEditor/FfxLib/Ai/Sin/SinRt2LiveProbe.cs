using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Memory;
using FFXProjectEditor.Services;
using FFXProjectEditor.Utils;
using Xe.BinaryMapper;

namespace FFXProjectEditor.FfxLib.Ai.Sin
{
    // SIN Chain Builder — Gate 6 LIVE glue, READ-ONLY side. Never writes RAM, never writes a file.
    //
    // The honest division of labor for a SIN-006 live pilot (per the grow assessment in the RT2 research):
    // SIN-006 GROWS the AiFile (+15 instructions, +2 jump slots), and a grown script CANNOT be poked in place into
    // the live region — the whole-monster buffer is allocated at exact on-disk file size (WorkerFile sits right
    // behind the AiFile) and the VM chunk/data buffers were pre-sized from the ORIGINAL worker counts
    // (FFX_Atel_ComputeScriptChunksSize @0x86A220 / ComputeScriptDataSize @0x86C050). So the grown file must enter
    // via the RELOAD path (a re-allocated whole-file load), and what the probe glue can honestly do is VERIFY:
    // find the loaded monster in battle and prove byte-exactly whether its live AiFile is the ORIGINAL or the
    // EDITED (SIN-006) version. That proof is exactly what an operator cannot do by eye.
    //
    // This class reuses the RT2-PROVEN locate chain from MonsterAiEditor (Skoll m014, 2026-06-04/05):
    //   ADDR_BATTLE_ACTIVE (0xD2A8E0) == 1  ->  POINTER_BATTLE_ENEMY_LIST (0xD34460)  ->  slots stride 0xF90
    //   ->  MemoryChr.Id - 0x1000 == monsterNumber  ->  Ptr_script_chunks (+0xF78) = live AiFile base.
    // Reads go through MemSharp (the proven read path); the dinput8 probe is consulted only for liveness flags.

    public sealed record SinRt2LiveStatus(
        bool GameAlive, bool RamReadable, bool ProbeAttached, bool ProbeHooked, bool BattleActive, string Summary)
    {
        public bool ReadyForVerify => GameAlive && RamReadable && BattleActive;
    }

    /// <summary>One live battle slot whose Id matches the target monster.</summary>
    public sealed record SinRt2LiveTarget(int Slot, ushort RawId, uint ScriptChunks, uint ScriptData, int CurrentHp, int MaxHp);

    public enum SinRt2LiveAiState
    {
        /// <summary>Live AiFile is byte-identical to the ORIGINAL disk AI slice — the vanilla file is loaded.</summary>
        MatchesOriginal,
        /// <summary>Live AiFile is byte-identical to the EDITED (SIN-006) AI slice — the grown script LOADED.</summary>
        MatchesEdited,
        /// <summary>Live AiFile matches neither — report the divergence honestly, do not guess.</summary>
        Diverges,
        /// <summary>Could not read the live bytes (no battle / pointer null / read failed).</summary>
        Unreadable,
    }

    public sealed record SinRt2LiveAiClassification(SinRt2LiveAiState State, string Detail);

    public static class SinRt2LiveProbe
    {
        /// <summary>Read-only liveness snapshot: game process, RAM reader, probe attach/hook, battle flag.</summary>
        public static SinRt2LiveStatus Status()
        {
            bool gameAlive = false, ramReadable = false, probeAttached = false, probeHooked = false, battleActive = false;
            // CLI one-shot runs exit before the 1s auto-detect timer ticks — force a synchronous detect first.
            try { Process_Service.Instance.AutoDetect(); } catch { }
            try { gameAlive = Process_Service.Instance.IsAlive; } catch { /* honest false */ }
            if (gameAlive)
            {
                try { ramReadable = MemSharp_Service.Instance.IsAvailable(); } catch { }
                try
                {
                    probeAttached = FfxProbe_Service.Instance.IsAttached;
                    probeHooked = probeAttached && FfxProbe_Service.Instance.IsHooked;
                }
                catch { }
                if (ramReadable)
                {
                    try { battleActive = MemSharp_Service.Instance.Read<byte>(MemoryMap.ADDR_BATTLE_ACTIVE) == 1; } catch { }
                }
            }

            string summary =
                !gameAlive ? "FFX is not running — live verification unavailable (honest skip)."
                : !ramReadable ? "FFX detected but RAM reads are not live yet."
                : !battleActive ? "No active battle — live AI verification is only honest in battle."
                : probeHooked ? "Game + RAM + battle + probe hook all live."
                : "Game + RAM + battle live (dinput8 probe not hooked — verify is read-only and still possible).";

            return new SinRt2LiveStatus(gameAlive, ramReadable, probeAttached, probeHooked, battleActive, summary);
        }

        /// <summary>Find every live battle slot whose monster id matches (Id - 0x1000 == monsterNumber). Read-only;
        /// returns an empty list when out of battle / nothing matches.</summary>
        public static IReadOnlyList<SinRt2LiveTarget> LocateMonster(int monsterNumber)
        {
            var hits = new List<SinRt2LiveTarget>();
            SinRt2LiveStatus st = Status();
            if (!st.ReadyForVerify) return hits;

            int listAddress = MemoryBattle_Util.GetMonsterListPointer();
            if (listAddress == 0) return hits;

            for (int slot = 0; slot < MemoryBattle_Util.MonsterListCount; slot++)
            {
                try
                {
                    int actorAddress = listAddress + slot * MemoryMap.SIZE_BATTLE_CHR_ENTRY;
                    byte[] actorBytes = MemSharp_Service.Instance.Read<byte>(actorAddress, MemoryMap.SIZE_BATTLE_CHR_ENTRY, false);
                    if (actorBytes == null || actorBytes.Length < MemoryMap.SIZE_BATTLE_CHR_ENTRY) continue;

                    using MemoryStream stream = new(actorBytes, writable: false);
                    MemoryChr actor = BinaryMapping.ReadObject<MemoryChr>(stream);
                    if (!actor.Stat_exist_flag || actor.Id <= 0) continue;
                    if (actor.Id - 0x1000 != monsterNumber) continue;

                    hits.Add(new SinRt2LiveTarget(
                        slot, actor.Id,
                        unchecked((uint)actor.Ptr_script_chunks),
                        unchecked((uint)actor.Ptr_script_data),
                        actor.Current_hp, actor.Max_hp));
                }
                catch { /* skip unreadable slot, keep scanning */ }
            }
            return hits;
        }

        /// <summary>Classify the live AiFile at <paramref name="scriptBase"/> against the ORIGINAL and the EDITED
        /// (SIN-006) AI slices: which one is loaded? Read-only — never writes a byte.</summary>
        public static SinRt2LiveAiClassification ClassifyLiveAi(uint scriptBase, byte[] originalAi, byte[] editedAi)
        {
            ArgumentNullException.ThrowIfNull(originalAi);
            ArgumentNullException.ThrowIfNull(editedAi);
            if (scriptBase == 0)
                return new SinRt2LiveAiClassification(SinRt2LiveAiState.Unreadable, "Ptr_script_chunks is null.");

            byte[]? liveEditedLen;
            try
            {
                // one read at the longer (edited) length covers both comparisons; reading past the original AiFile
                // end only touches the adjacent WorkerFile bytes, read-only.
                liveEditedLen = MemSharp_Service.Instance.Read<byte>(unchecked((int)scriptBase), editedAi.Length, false);
            }
            catch (Exception ex)
            {
                return new SinRt2LiveAiClassification(SinRt2LiveAiState.Unreadable, $"live read failed: {ex.GetType().Name}");
            }
            if (liveEditedLen == null || liveEditedLen.Length != editedAi.Length)
                return new SinRt2LiveAiClassification(SinRt2LiveAiState.Unreadable,
                    $"could not read 0x{editedAi.Length:X} bytes at 0x{scriptBase:X8}.");

            if (liveEditedLen.AsSpan(0, originalAi.Length).SequenceEqual(originalAi))
                return new SinRt2LiveAiClassification(SinRt2LiveAiState.MatchesOriginal,
                    $"live AiFile == ORIGINAL disk slice (byte-exact over 0x{originalAi.Length:X}). The vanilla script is loaded; the SIN-006 grow has NOT entered RAM.");

            if (liveEditedLen.SequenceEqual(editedAi))
                return new SinRt2LiveAiClassification(SinRt2LiveAiState.MatchesEdited,
                    $"live AiFile == EDITED (SIN-006) slice (byte-exact over 0x{editedAi.Length:X}). The grown script IS loaded — structural load proof; on-screen behaviour still needs the operator's eyes.");

            int firstDiff = -1;
            int max = Math.Min(liveEditedLen.Length, originalAi.Length);
            for (int i = 0; i < max && firstDiff < 0; i++)
                if (liveEditedLen[i] != originalAi[i]) firstDiff = i;
            return new SinRt2LiveAiClassification(SinRt2LiveAiState.Diverges,
                $"live AiFile matches NEITHER original NOR edited (first delta vs original at +0x{firstDiff:X}). Do not proceed — identify the loaded file first.");
        }
    }
}
