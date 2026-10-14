using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace FFXProjectEditor.FfxLib.Mods;

internal sealed record VanguardCommandBinding(int Command, int? OverdriveOverride, int Owner, int GearKind);
internal sealed record VanguardPreset
{
    public Dictionary<string, bool> Rules { get; init; } = HookAbilityRegistry.Features.ToDictionary(f => f.Key, _ => false, StringComparer.Ordinal);
    public int SingleHitPercent { get; init; } = 100;
    public int MultiHitPercent { get; init; } = 100;
    public int MpZeroMagCap { get; init; }
    public Dictionary<string, int> AbilityIds { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, VanguardCommandBinding> Commands { get; init; } = new(StringComparer.Ordinal);
    public bool MonsterRewardsEnabled { get; init; }
    public bool SpiraEnabled { get; init; }
    public bool AeonAscensionEnabled { get; init; }
    public bool ElementalCoreEnabled { get; init; }
    public bool ElementalTacticsEnabled { get; init; }
    public bool ElementalGravityEnabled { get; init; }
    public bool ElementalMagicBdlEnabled { get; init; }
    public string TextLocale { get; init; } = "native";

    public static VanguardPreset Parse(byte[] bytes)
    {
        HookJson.RejectDuplicates(bytes);
        return JsonSerializer.Deserialize<VanguardPreset>(bytes, HookProjectProfile.JsonOptions) ?? throw new InvalidDataException("Missing Hook preset.");
    }
    public string Serialize() => JsonSerializer.Serialize(this, HookProjectProfile.JsonOptions);
    public static uint EncodeCommand(int command, int? overdriveOverride)
    {
        if (command == 0 && overdriveOverride == null) return 0;
        if (command is < 0x3000 or > 0x313f || overdriveOverride is < 0 or > 255) throw new InvalidDataException("Invalid command/OD override; null means native fee.");
        return (uint)command | (overdriveOverride.HasValue ? (uint)(overdriveOverride.Value + 1) << 16 : 0);
    }
    public string ExportIni(HookProjectProfile project, byte[]? commandBank = null, byte[]? abilities = null, byte[]? prices = null, string locale = "new_uspc")
    {
        project.Require("vanguard.v1");
        if (Rules == null || AbilityIds == null || Commands == null || Rules.Keys.Any(k => !HookAbilityRegistry.Features.Any(f => f.Key == k)) ||
            SingleHitPercent is < 0 or > 400 || MultiHitPercent is < 0 or > 400 || MpZeroMagCap is < 0 or > 1 || TextLocale is not ("native" or "pt-BR"))
            throw new InvalidDataException("Unsupported Vanguard rule/balance parameter.");
        var ids = HookAbilityRegistry.Mapping(AbilityIds); var used = new HashSet<int>();
        var text = new StringBuilder("; Desired configuration. Requires restart and independent runtime acknowledgement.\n[vanguard]\n");
        foreach (var feature in HookAbilityRegistry.Features) text.AppendLine(feature.Key + " = " + (Rules.GetValueOrDefault(feature.Key) ? "1" : "0"));
        text.AppendLine("\n[vanguard_balance]");
        text.Append(CultureInfo.InvariantCulture, $"single_hit_percent = {SingleHitPercent}\nmulti_hit_percent = {MultiHitPercent}\nmp_zero_mag_cap = {MpZeroMagCap}\n");
        foreach (var section in new[] { "vanguard_ids", "spira_ids" })
        {
            text.AppendLine("\n[" + section + "]");
            foreach (var ability in HookAbilityRegistry.Abilities.Where(a => (a.DefaultId < 148) == (section == "vanguard_ids")))
                text.AppendLine(ability.Key + " = " + ids[ability.Key].ToString(CultureInfo.InvariantCulture));
        }
        text.AppendLine("\n[vanguard_commands]");
        foreach (var (key, binding) in Commands)
        {
            var ability = HookAbilityRegistry.Get(key);
            if (ability.DefaultId >= 148) throw new InvalidDataException("Equipped Vanguard commands require a Vanguard identity.");
            if (binding.Command == 0)
            {
                if (binding.OverdriveOverride != null) throw new InvalidDataException("A disabled binding cannot override an OD fee.");
                text.AppendLine(key + " = 0"); continue;
            }
            if (ability.DefaultId >= 148 || !ability.Allows(binding.Owner, binding.GearKind) || !used.Add(binding.Command))
                throw new InvalidDataException("Duplicate command or incompatible equipped ability/owner/type.");
            if (commandBank == null || abilities == null || prices == null) throw new InvalidDataException("Command binding requires current command and autoability bank proof.");
            HookAbilityRegistry.ValidateBank(abilities, prices, ids, locale);
            uint packed = EncodeCommand(binding.Command, binding.OverdriveOverride); int index = binding.Command & 0xfff;
            if (commandBank.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(commandBank) != 1 ||
                BinaryPrimitives.ReadUInt16LittleEndian(commandBank.AsSpan(8)) != 0 ||
                BinaryPrimitives.ReadUInt16LittleEndian(commandBank.AsSpan(12)) != 96 ||
                BinaryPrimitives.ReadUInt32LittleEndian(commandBank.AsSpan(16)) != 20 ||
                BinaryPrimitives.ReadUInt16LittleEndian(commandBank.AsSpan(10)) < index || 20 + (index + 1) * 96 > commandBank.Length)
                throw new InvalidDataException("Command is outside the admitted table.");
            var row = commandBank.AsSpan(20 + index * 96, 96); int menu = row[0x16] & 0xf8;
            if (menu is 8 or 16 || (BinaryPrimitives.ReadUInt32LittleEndian(row[0x1c..]) & 2) == 0 || (row[0x19] != 255 && row[0x19] != binding.Owner))
                throw new InvalidDataException("Command category/owner is not executable.");
            text.AppendLine(key + " = " + packed.ToString(CultureInfo.InvariantCulture));
        }
        text.AppendLine("\n[cheats]\nmonster_rewards = " + (MonsterRewardsEnabled ? "1" : "0"));
        if (TextLocale == "pt-BR") project.Require("ffx.text-locale.api2");
        text.AppendLine("\n[language]\ntext_locale = " + TextLocale);
        text.AppendLine("\n[spira]\nenabled = " + (SpiraEnabled ? "1" : "0"));
        text.AppendLine("\n[aeon_ascension]\nenabled = " + (AeonAscensionEnabled ? "1" : "0"));
        text.AppendLine($"\n[elemental]\ncore = {(ElementalCoreEnabled ? 1 : 0)}\ntactics = {(ElementalTacticsEnabled ? 1 : 0)}\ngravity = {(ElementalGravityEnabled ? 1 : 0)}\nmagic_bdl = {(ElementalMagicBdlEnabled ? 1 : 0)}");
        text.AppendLine("\n[f8_authority]");
        foreach (var feature in HookAbilityRegistry.Features) text.AppendLine("vanguard_" + feature.Key + " = 1");
        foreach (string key in new[] { "monster_rewards", "spira_enabled", "aeon_ascension_enabled", "elemental_core", "elemental_tactics", "elemental_gravity", "elemental_magic_bdl" }) text.AppendLine(key + " = 1");
        return text.ToString();
    }
}
