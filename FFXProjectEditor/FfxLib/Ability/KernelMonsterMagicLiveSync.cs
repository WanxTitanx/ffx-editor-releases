using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.Modules.BattleKernel.Commands;
using FFXProjectEditor.Services;
using FFXProjectEditor.Utils.Encoding;

namespace FFXProjectEditor.FfxLib.Ability
{
    /// <summary>
    /// Keeps monster magic dictionaries and AI metadata aligned with the project's live
    /// <c>monmagic1.bin</c> / <c>monmagic2.bin</c> as rows are added or renamed in the kernel editor.
    /// </summary>
    public static class KernelMonsterMagicLiveSync
    {
        static readonly object Gate = new();
        static readonly Dictionary<ushort, string> MonMagic1Baseline = Snapshot(CommandMonster1_Dictionary.Instance);
        static readonly Dictionary<ushort, string> MonMagic2Baseline = Snapshot(CommandMonster2_Dictionary.Instance);

        public static event Action? Changed;

        public static void SyncProject()
        {
            if (!Project_Service.Instance.IsProjectLoaded)
            {
                Clear();
                return;
            }

            Project_Service project = Project_Service.Instance;
            SyncFile(CommandFile_enum.MonMagic1, project.Path_KernelMonMagic1Us);
            SyncFile(CommandFile_enum.MonMagic2, project.Path_KernelMonMagic2Us);
        }

        public static void SyncFile(CommandFile_enum file, string path)
        {
            if (!File.Exists(path))
                return;

            SyncBytes(file, File.ReadAllBytes(path));
        }

        public static void SyncBytes(CommandFile_enum file, byte[] bytes)
        {
            if (file is not (CommandFile_enum.MonMagic1 or CommandFile_enum.MonMagic2))
                return;

            List<Ability_Command> commands = Ability_Command.ReadList(bytes, hasExtraInfo: false);
            Dictionary<ushort, string> dictionary = DictionaryFor(file);
            Dictionary<ushort, string> baseline = BaselineFor(file);
            List<AiCommandMetadataEntry> liveMetadata = new();

            lock (Gate)
            {
                RestoreDictionary(dictionary, baseline);

                for (int index = 0; index < commands.Count; index++)
                {
                    Ability_Command command = commands[index];
                    ushort localId = (ushort)index;
                    string displayName = DecodeDisplayName(command, localId);
                    dictionary[localId] = displayName;

                    ushort operand = EncodeOperand(file, localId);
                    if (!AiCommandMetadataCatalog.HasBaseEntry(operand))
                        liveMetadata.Add(BuildLiveMetadata(file, localId, command, displayName));
                }

                AiCommandMetadataCatalog.ApplyLiveRows(liveMetadata);
            }

            AiCommandId.InvalidateOptionsCache();
            Changed?.Invoke();
        }

        public static void Clear()
        {
            lock (Gate)
            {
                RestoreDictionary(CommandMonster1_Dictionary.Instance, MonMagic1Baseline);
                RestoreDictionary(CommandMonster2_Dictionary.Instance, MonMagic2Baseline);
                AiCommandMetadataCatalog.ClearLiveRows();
            }

            AiCommandId.InvalidateOptionsCache();
            Changed?.Invoke();
        }

        static Dictionary<ushort, string> Snapshot(Dictionary<ushort, string> source) =>
            source.ToDictionary(static kv => kv.Key, static kv => kv.Value);

        static void RestoreDictionary(Dictionary<ushort, string> target, Dictionary<ushort, string> baseline)
        {
            target.Clear();
            foreach (KeyValuePair<ushort, string> entry in baseline)
                target[entry.Key] = entry.Value;
        }

        static Dictionary<ushort, string> DictionaryFor(CommandFile_enum file) => file switch
        {
            CommandFile_enum.MonMagic1 => CommandMonster1_Dictionary.Instance,
            CommandFile_enum.MonMagic2 => CommandMonster2_Dictionary.Instance,
            _ => throw new ArgumentOutOfRangeException(nameof(file)),
        };

        static Dictionary<ushort, string> BaselineFor(CommandFile_enum file) => file switch
        {
            CommandFile_enum.MonMagic1 => MonMagic1Baseline,
            CommandFile_enum.MonMagic2 => MonMagic2Baseline,
            _ => throw new ArgumentOutOfRangeException(nameof(file)),
        };

        static ushort EncodeOperand(CommandFile_enum file, ushort localId) => file switch
        {
            CommandFile_enum.MonMagic1 => AiCommandId.EncodeMonster(localId),
            CommandFile_enum.MonMagic2 => AiCommandId.EncodeMonster2(localId),
            _ => throw new ArgumentOutOfRangeException(nameof(file)),
        };

        static string DecodeDisplayName(Ability_Command command, ushort localId)
        {
            string decoded = FfxEncoding.DecodeScript(command.NameScriptBytes).GetString(FfxEncoding.UsDecoder).Trim();
            return string.IsNullOrWhiteSpace(decoded) ? $"Command {localId}" : decoded;
        }

        static string DecodeDescription(Ability_Command command)
        {
            string decoded = FfxEncoding.DecodeScript(command.DescriptionScriptBytes).GetString(FfxEncoding.UsDecoder).Trim();
            return string.IsNullOrWhiteSpace(decoded) ? string.Empty : decoded;
        }

        static AiCommandMetadataEntry BuildLiveMetadata(
            CommandFile_enum file,
            ushort localId,
            Ability_Command command,
            string displayName)
        {
            AiCommandMetadataCategory category = file switch
            {
                CommandFile_enum.MonMagic1 => AiCommandMetadataCategory.MonsterMagic1,
                CommandFile_enum.MonMagic2 => AiCommandMetadataCategory.MonsterMagic2,
                _ => throw new ArgumentOutOfRangeException(nameof(file)),
            };

            string sourceFile = file switch
            {
                CommandFile_enum.MonMagic1 => "monmagic1.bin",
                CommandFile_enum.MonMagic2 => "monmagic2.bin",
                _ => throw new ArgumentOutOfRangeException(nameof(file)),
            };

            ushort operand = EncodeOperand(file, localId);
            string description = DecodeDescription(command);
            string roleText = InferRoleText(command);
            string formula = command.DamageFormula.ToString();
            string formulaHex = $"0x{(byte)command.DamageFormula:X2}";
            string hitPercent = command.AttackAccuracy == 0 ? "Always [00h]" : $"{command.AttackAccuracy}%";
            string targetText = command.TargetFlgs.ToString();
            string elementText = FormatElements(command);
            string rawProperties =
                $"live-kernel-sync row #{localId}, Formula={formula} [{formulaHex}], Power={command.AttackPower}, " +
                $"Rank={command.MoveRank}, MP={command.CostMp}, Hits={command.HitCount}, Target={targetText}, " +
                $"Element={elementText}, userAnim={command.CasterAnimId}, moveAnim={command.Anim1Id}/{command.Anim2Id}";

            return new AiCommandMetadataEntry(
                operand,
                category,
                sourceFile,
                displayName,
                description,
                roleText,
                command.HitCount,
                formula,
                formulaHex,
                command.AttackPower,
                command.MoveRank,
                command.CostMp,
                hitPercent,
                targetText,
                string.Empty,
                elementText,
                rawProperties,
                "live-kernel-sync");
        }

        static string InferRoleText(Ability_Command command)
        {
            if (command.FlagDamageTypeMp)
                return "mp-damage";
            if (command.FlagDamageMagical || command.FlagDamagePhysical)
                return "hp-damage";
            return "special";
        }

        static string FormatElements(Ability_Command command)
        {
            List<string> elements = new();
            if (command.FlagElementFire) elements.Add("Fire");
            if (command.FlagElementBlizzard) elements.Add("Ice");
            if (command.FlagElementThunder) elements.Add("Thunder");
            if (command.FlagElementWater) elements.Add("Water");
            if (command.FlagElementHoly) elements.Add("Holy");
            if (command.FlagElementDark) elements.Add("Dark");
            return elements.Count == 0 ? string.Empty : string.Join(";", elements);
        }
    }
}
