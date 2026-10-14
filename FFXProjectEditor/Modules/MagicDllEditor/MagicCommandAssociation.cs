using FFXProjectEditor.Resources;
using FFXProjectEditor.Diagnostics;
using FFXProjectEditor.FfxLib.Ability;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Modules.MagicDllEditor
{
    /// <summary>
    /// FASE 4 — Associa um clone magic_&lt;novoId&gt;.dll a uma skill (command.bin / monmagic*.bin).
    ///
    /// Cadeia provada (CommandGrowWriter.cs:48-49 + FFX_MagicFile_LoadDllByMagicId @ 0x9DA420):
    ///   command.Anim1Id (short) → magic_%04d.dll (provado: anim 146 → magic_0146).
    /// Logo, setar `Anim1Id = novoId` numa row de command.bin faz a skill chamar o clone.
    ///
    /// Esta é uma edição de CAMPO (não grow): o tamanho do command.bin é inalterado, só o valor
    /// de Anim1Id da row escolhida muda. Usa `Ability_Command.WriteList` que preserva o text pool
    /// byte-a-byte (RT0-proven). Safe.
    ///
    /// Limitação honesta: isto SUBSTITUI o efeito visual da skill original (a skill passa a chamar
    /// o clone em vez da magic vanilla). Para criar uma NOVA skill (preservando a original),
    /// seria grow de command.bin (já existe `CommandGrowWriter.AppendElementWards` como referência
    /// para wards 320/321), mas isso é trabalho da lane de batalha/ability + RT2 in-game.
    ///
    /// Uso típico: clonar magic_0140 → magic_9999.dll (Fase 1), editar campos (Fase 2/3), e então
    /// associar: escolher uma skill existente (ex.: Fire, Anim1Id=82), setar Anim1Id=9999. A skill
    /// Fire passa a chamar magic_9999.dll (visual do clone) — sem mudar nome/stats da skill.
    /// </summary>
    internal static class MagicCommandAssociation
    {
        /// <summary>
        /// Associa uma row de command.bin à magic_&lt;novoId&gt;.dll setando Anim1Id = novoId.
        /// Re-serializa preservando o text pool (RT0 byte-faithful fora do Anim1Id).
        /// Retorna os bytes modificados (clone dos originais) + relatório.
        /// </summary>
        public static AssociationResult AssociateByIndex(
            byte[] commandBinBytes, bool hasExtraInfo, int commandIndex, short newAnim1Id)
        {
            if (commandBinBytes == null || commandBinBytes.Length == 0)
                return new AssociationResult(false, null, Strings.U_Md_AssocEmptyBin, 0, 0, 0, 0);

            List<Ability_Command> commands = Ability_Command.ReadList(commandBinBytes, hasExtraInfo);
            if (commandIndex < 0 || commandIndex >= commands.Count)
                return new AssociationResult(false, null,
                    string.Format(Strings.U_Md_AssocIndexRange, commandIndex, commands.Count - 1), 0, 0, 0, 0);

            Ability_Command target = commands[commandIndex];
            short oldAnim1 = target.Anim1Id;
            target.Anim1Id = newAnim1Id;

            byte[] result = Ability_Command.WriteList(commands, hasExtraInfo);

            int sizeDelta = result.Length - commandBinBytes.Length;
            DebugLog.Info("Magic.Assoc",
                $"AssociateByIndex: row {commandIndex} Anim1Id {oldAnim1} → {newAnim1Id}; " +
                $"size {commandBinBytes.Length} → {result.Length} (Δ{sizeDelta:+0;-0;0}); rows={commands.Count}.");

            bool ok = sizeDelta == 0; // edição de campo: tamanho inalterado
            string report = ok
                ? string.Format(Strings.U_Md_AssocOk, commandIndex, oldAnim1, newAnim1Id)
                : string.Format(Strings.U_Md_AssocWarnSize, commandBinBytes.Length, result.Length, sizeDelta);

            return new AssociationResult(ok, result, report, commands.Count, commandIndex, oldAnim1, newAnim1Id);
        }

        /// <summary>
        /// Lista as rows de command.bin com Anim1Id = magicId (para o usuário escolher qual skill
        /// apontar para o clone). Retorna lista de (index, anim1Id, anim2Id) para as rows que batem.
        /// </summary>
        public static List<(int Index, short Anim1Id, short Anim2Id)> FindRowsByMagicId(
            byte[] commandBinBytes, bool hasExtraInfo, short magicId)
        {
            var result = new List<(int, short, short)>();
            if (commandBinBytes == null) return result;

            List<Ability_Command> commands = Ability_Command.ReadList(commandBinBytes, hasExtraInfo);
            for (int i = 0; i < commands.Count; i++)
            {
                if (commands[i].Anim1Id == magicId)
                    result.Add((i, commands[i].Anim1Id, commands[i].Anim2Id));
            }
            return result;
        }

        public sealed record AssociationResult(
            bool Success,
            byte[]? ModifiedBytes,
            string Report,
            int TotalRows,
            int EditedRowIndex,
            short OldAnim1Id,
            short NewAnim1Id);
    }
}
