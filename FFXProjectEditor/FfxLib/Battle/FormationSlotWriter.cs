// ============================================================================
// FormationSlotWriter — byte-safe loose-file write for a battle formation (SPIRA FORGE save path)
// PURPOSE : shared slot-only save used by the Formation Editor AND the headless FormationSlotLab gate: guard
//           that only the 16 formation-slot bytes change, backup-once, then File.WriteAllBytes.
// WHY     : guarantees a formation patch NEVER touches bytes outside the slot window (anti-regression for
//           battle files) — the same write path is what the runtime test exercises.
// EVIDENCE: slots window = [battle.FormationSlotsOffset, +FormationSlotsLength); battle RT0 baseline.
// MAINT   : SPIRA FORGE is PAUSED (regra) — this writer is still the active safety path for formation saves.
//           All messages/semantic comments are PT (historically); keep the slot-only guard authoritative.
// ============================================================================
using System;
using System.IO;

namespace FFXProjectEditor.FfxLib.Battle
{
    /// <summary>
    /// SPIRA FORGE — caminho de SAVE byte-safe da formação (loose-file no workspace).
    ///
    /// Lógica de produção compartilhada pelo Formation Editor (UI) E pelo gate headless
    /// (RuntimeTools/FormationSlotLab), pra que o exato caminho que grava no disco seja testado:
    ///   1) GUARD slot-only (AssertSlotOnlyDiff): recusa o write se mudaria qualquer byte fora dos 16 dos slots;
    ///   2) backup-once (`<path><suffix>`) preservando o arquivo como-carregado;
    ///   3) File.WriteAllBytes.
    /// Avalonia-free de propósito (só Battle_File + System.IO).
    /// </summary>
    public static class FormationSlotWriter
    {
        public const string DefaultBackupSuffix = ".spiraforge.bak";

        public enum SaveStatus { Saved, AbortedNotSlotOnly, Error }

        public sealed class SaveResult
        {
            public required SaveStatus Status { get; init; }
            public required string Message { get; init; }
            public string? BackupPath { get; init; }
            public bool Ok => Status == SaveStatus.Saved;
        }

        /// <summary>true se todo byte que difere entre a/b está dentro de [off, off+len) (e os tamanhos batem).</summary>
        public static bool IsSlotOnly(byte[] original, byte[] candidate, int off, int len)
        {
            if (original == null || candidate == null) return false;
            if (original.Length != candidate.Length) return false;
            for (int i = 0; i < original.Length; i++)
                if (original[i] != candidate[i] && (i < off || i >= off + len)) return false;
            return true;
        }

        /// <summary>
        /// Grava <paramref name="newBytes"/> em <paramref name="path"/> como loose-file, com guard slot-only +
        /// backup-once. <paramref name="originalBytes"/> = arquivo como-carregado (base do diff). Nunca grava se
        /// o diff sair da região dos slots.
        /// </summary>
        public static SaveResult WriteLooseFile(
            string path,
            byte[] originalBytes,
            byte[] newBytes,
            int slotsOffset,
            int slotsLength,
            string backupSuffix = DefaultBackupSuffix)
        {
            if (slotsOffset < 0)
                return new SaveResult { Status = SaveStatus.Error, Message = "Invalid slots offset (formation not writable)." };

            if (!IsSlotOnly(originalBytes, newBytes, slotsOffset, slotsLength))
                return new SaveResult
                {
                    Status = SaveStatus.AbortedNotSlotOnly,
                    Message = "ABORTED: the write would change bytes outside the 16 slots — slot-only guard blocked. Nothing written."
                };

            try
            {
                string backupPath = path + backupSuffix;
                if (!File.Exists(backupPath))
                    File.Copy(path, backupPath);
                File.WriteAllBytes(path, newBytes);
                return new SaveResult
                {
                    Status = SaveStatus.Saved,
                    Message = $"Saved to {Path.GetFileName(path)} (backup: {Path.GetFileName(backupPath)}). Slot-only confirmed.",
                    BackupPath = backupPath
                };
            }
            catch (Exception ex)
            {
                return new SaveResult { Status = SaveStatus.Error, Message = $"Falha gravando: {ex.Message}" };
            }
        }
    }
}
