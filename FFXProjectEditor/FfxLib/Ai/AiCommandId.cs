using System;
using System.Collections.Generic;
using System.Linq;
using FFXProjectEditor.FfxLib.Dictionaries;

namespace FFXProjectEditor.FfxLib.Ai
{
    // Friendly command/ability selector for AI bytecode operands.
    //
    // A performCommand/forcePerformCommand (Battle.700B / Battle.705A) is driven by a PUSHII (0xAE) whose
    // u16 operand encodes the command as id = (category<<12) | commandId — PROVEN RT2-live 2026-06-04
    // (Flame Flan Firaga 0x3049 -> Thundaga 0x304B was a one-byte change to exactly this operand).
    //   category 0x3000 = character / black-magic command  -> CommandCharacter_Dictionary (keyed by 12-bit id)
    //   category 0x4000 = monster / aeon ability  (monmagic1) -> CommandMonster1_Dictionary  (keyed by 12-bit id)
    //   category 0x6000 = monster ability set 2   (monmagic2) -> CommandMonster2_Dictionary  (keyed by 12-bit id)
    // The high nibble IS the game-category selector (FfxCommon_Util.GetGameCategory / GameCategory_Enum:
    //   3=Commands  4=MonMagic1  6=MonMagic2  2=Items ...). PROVEN over the AI corpus: the operand of a PUSHII before
    //   performCommand/forcePerformCommand has high nibble 3 (369 sites), 4 (985), 6 (291) — nibble 5 never appears;
    //   e.g. 0x60AB = CommandMonster2[171] = "Multi-Fira". So MonMagic2 (the "Monster Commands 2" the kernel editor
    //   shows) IS reachable via performCommand and must be in the selector — it was missing before (only 3/4 mapped).
    // The dictionaries are keyed by the bare 12-bit id (max key < 0x1000), so the IdMask split is lossless.
    //
    // Dependency-free (only System.* + the existing dictionaries): pure lookup, no I/O, never mutates anything.
    // It is ADVISORY — a benign literal like PUSHII 0x3001 used as an arithmetic constant will also decode as a
    // "command"; the hex box stays the source of truth and the operand only changes when the user picks an option.

    public enum AiCommandCategory : ushort
    {
        Character = 0x3000, // CommandCharacter_Dictionary (command.bin)
        Monster = 0x4000,   // CommandMonster1_Dictionary  (monmagic1.bin)
        Monster2 = 0x6000,  // CommandMonster2_Dictionary  (monmagic2.bin) — e.g. Multi-Fira
    }

    public static class AiCommandId
    {
        public const ushort CatMask = 0xF000;
        public const ushort IdMask = 0x0FFF;
        public const ushort CharCat = 0x3000;
        public const ushort MonCat = 0x4000;
        public const ushort Mon2Cat = 0x6000;

        public static ushort EncodeChar(ushort commandId) => (ushort)(CharCat | (commandId & IdMask));
        public static ushort EncodeMonster(ushort commandId) => (ushort)(MonCat | (commandId & IdMask));
        public static ushort EncodeMonster2(ushort commandId) => (ushort)(Mon2Cat | (commandId & IdMask));
        public static ushort Encode(AiCommandCategory cat, ushort commandId)
            => (ushort)(((ushort)cat & CatMask) | (commandId & IdMask));

        static Dictionary<ushort, string>? DictFor(ushort category) => category switch
        {
            CharCat => CommandCharacter_Dictionary.Instance,
            MonCat => CommandMonster1_Dictionary.Instance,
            Mon2Cat => CommandMonster2_Dictionary.Instance,
            _ => null,
        };

        /// <summary>operand -> (category, 12-bit id, friendly name). IsKnown=false when the high nibble is
        /// neither 3 nor 4, or the id is absent from the dictionary (Name falls back to "id NNN" / "raw NNNNh").</summary>
        public static AiCommandDecode Decode(ushort operand)
        {
            ushort cat = (ushort)(operand & CatMask);
            ushort id = (ushort)(operand & IdMask);
            Dictionary<ushort, string>? dict = DictFor(cat);
            if (dict == null)
                return new AiCommandDecode(operand, null, id, $"raw {operand:X4}h", false);
            bool known = dict.TryGetValue(id, out string? n);
            return new AiCommandDecode(operand, (AiCommandCategory)cat, id, known ? n! : $"id {id}", known);
        }

        /// <summary>True when the operand's high nibble is a command category (3 or 4) — i.e. it could be a
        /// performCommand id. Used to decide whether to show the command dropdown next to a PUSHII operand.</summary>
        public static bool IsCommandOperand(ushort operand)
        {
            ushort cat = (ushort)(operand & CatMask);
            return cat == CharCat || cat == MonCat || cat == Mon2Cat;
        }

        static IReadOnlyList<AiCommandOption>? _all;

        public static void InvalidateOptionsCache() => _all = null;

        /// <summary>Flat bindable list of every (category, id, operand, name). Cached. The UI binds this to a
        /// ComboBox (Display carries category + hex + name) or filters by Category for a two-dropdown layout.</summary>
        public static IReadOnlyList<AiCommandOption> AllOptions()
        {
            if (_all != null) return _all;
            var list = new List<AiCommandOption>(CommandCharacter_Dictionary.Instance.Count
                + CommandMonster1_Dictionary.Instance.Count + CommandMonster2_Dictionary.Instance.Count);
            foreach (KeyValuePair<ushort, string> kv in CommandCharacter_Dictionary.Instance)
                list.Add(new AiCommandOption(AiCommandCategory.Character, kv.Key, EncodeChar(kv.Key), kv.Value));
            foreach (KeyValuePair<ushort, string> kv in CommandMonster1_Dictionary.Instance)
                list.Add(new AiCommandOption(AiCommandCategory.Monster, kv.Key, EncodeMonster(kv.Key), kv.Value));
            foreach (KeyValuePair<ushort, string> kv in CommandMonster2_Dictionary.Instance)
                list.Add(new AiCommandOption(AiCommandCategory.Monster2, kv.Key, EncodeMonster2(kv.Key), kv.Value));
            _all = list;
            return _all;
        }

        public static IReadOnlyList<AiCommandOption> OptionsFor(AiCommandCategory cat)
            => AllOptions().Where(o => o.Category == cat).ToList();

        /// <summary>The option matching an operand by value (or null). Match by Operand, not reference.</summary>
        public static AiCommandOption? OptionFor(ushort operand)
            => AllOptions().FirstOrDefault(o => o.Operand == operand);
    }

    public readonly record struct AiCommandDecode(
        ushort Operand, AiCommandCategory? Category, ushort CommandId, string Name, bool IsKnown);

    public sealed record AiCommandOption(
        AiCommandCategory Category, ushort CommandId, ushort Operand, string Name)
    {
        public string Display => $"{(ushort)Category:X4} · 0x{Operand:X4}  {Name}";
        public string Hex => Operand.ToString("X4");
    }
}
