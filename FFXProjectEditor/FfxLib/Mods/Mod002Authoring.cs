using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Text;
using FFXProjectEditor.Utils.Encoding;

namespace FFXProjectEditor.FfxLib.Mods;

internal sealed record Mod002Edit(int Id, string Name, string Description, int Price);
internal sealed record Mod002Preview(byte[] Abilities, byte[] Prices, string Fingerprint, string Report);

/// <summary>Offline overlay authoring. Source bytes are immutable; only mapped text refs/prices may change.</summary>
internal sealed class Mod002Authoring
{
    public static readonly string[] Locales = { "new_uspc", "inpc", "new_depc", "new_frpc", "new_itpc", "new_sppc", "jppc", "new_jppc", "new_chpc", "new_krpc" };
    private readonly byte[] originalAbilities, originalPrices;
    private readonly AutoAbilityTable table;
    public Mod002Manifest Manifest { get; }
    public string Locale { get; }
    public bool CanEditText => Array.IndexOf(Locales, Locale) is >= 0 and < 6;
    public IReadOnlyList<Mod002Edit> Rows { get; }
    public string AbilityHash => ModAuthoringFiles.Sha256(originalAbilities);
    public string PriceHash => ModAuthoringFiles.Sha256(originalPrices);

    public Mod002Authoring(byte[] abilities, byte[] prices, Mod002Manifest manifest, string locale)
    {
        if (!Locales.Contains(locale, StringComparer.Ordinal)) throw new InvalidDataException("Unknown locale.");
        if (abilities.Length > 65535 || prices.Length > 100_000) throw new InvalidDataException("Oversized kernel input (native autoability limit: 65535 bytes).");
        HookAbilityRegistry.ValidateLayout(abilities);
        if (prices.Length < 20 || BinaryPrimitives.ReadUInt16LittleEndian(prices.AsSpan(8)) != 0
            || BinaryPrimitives.ReadInt32LittleEndian(prices.AsSpan(16)) != 20)
            throw new InvalidDataException("Unsupported arms_rate.bin table layout.");
        originalAbilities = (byte[])abilities.Clone(); originalPrices = (byte[])prices.Clone();
        Manifest = manifest; Locale = locale;
        table = HookAbilityRegistry.ReadBankView(originalAbilities, originalPrices);
        if (!table.HasFullPriceCoverage) throw new InvalidDataException("The price table must cover every ability ID.");
        int pool = 20 + table.AbilityHeader.TotalDataLength;
        foreach (var entry in table.Entries)
            foreach (int field in new[] { 0, 4, 8, 12 })
            {
                int offset = pool + BinaryPrimitives.ReadUInt16LittleEndian(entry.RawBytes.AsSpan(field));
                if (offset >= originalAbilities.Length || Array.IndexOf(originalAbilities, (byte)0, offset) < 0)
                    throw new InvalidDataException("Invalid or unterminated text reference.");
            }
        var rows = new List<Mod002Edit>();
        foreach (var effect in manifest.Abilities)
        {
            var row = table.Entries.SingleOrDefault(r => r.Index == effect.Id)
                ?? throw new InvalidDataException($"Missing MOD-002 ID {effect.Id} ({effect.Key}).");
            var definition = HookAbilityRegistry.Get(effect.Key);
            if (!HookAbilityRegistry.PayloadMatches(definition, row.RawBytes.AsSpan(0x10)))
                throw new InvalidDataException($"ID {effect.Id} has a native payload; refusing a mod/vanilla collision.");
            byte[] expectedName = HookAbilityRegistry.CanonicalName(definition, !CanEditText);
            int nameAt = pool + BinaryPrimitives.ReadUInt16LittleEndian(row.RawBytes);
            if (nameAt > originalAbilities.Length - expectedName.Length - 1 ||
                !originalAbilities.AsSpan(nameAt, expectedName.Length).SequenceEqual(expectedName) || originalAbilities[nameAt + expectedName.Length] != 0)
                throw new InvalidDataException($"ID {effect.Id} does not carry the canonical identity for {effect.Key}.");
            // No unproved US-to-Asian translation: those rows remain byte-preserved and price-only.
            rows.Add(new Mod002Edit(effect.Id, CanEditText ? row.NameText : RawText(effect.Id, 0),
                CanEditText ? row.DescriptionText : RawText(effect.Id, 8), row.GilPrice));
        }
        Rows = rows.AsReadOnly();
    }

    private string RawText(int id, int field)
    {
        int offset = BinaryPrimitives.ReadUInt16LittleEndian(originalAbilities.AsSpan(20 + id * 108 + field));
        return Convert.ToHexString(TextBinary_Util.ReadNullTerminatedScript(originalAbilities, 20 + table.AbilityHeader.TotalDataLength + offset));
    }

    public Mod002Preview Preview(IReadOnlyList<Mod002Edit> edits)
    {
        if (edits.Count != Rows.Count || edits.Select(e => e.Id).Distinct().Count() != edits.Count
            || !edits.Select(e => e.Id).OrderBy(x => x).SequenceEqual(Rows.Select(e => e.Id).OrderBy(x => x)))
            throw new InvalidDataException("Edits must match the loaded effect mapping exactly.");
        using var output = new MemoryStream();
        output.Write(originalAbilities);
        byte[] prices = (byte[])originalPrices.Clone();
        var report = new StringBuilder();
        report.AppendLine($"Vanguard Combat Engine [DERIVADO DE MOD-002] RT0 / {Locale}");
        report.AppendLine($"Target contract: {HookCompatibilityProfile.SourceRevision}; PE32/i386 SHA-256 {HookCompatibilityProfile.SupportedExecutable}. Authoring RT0; native admission and gameplay are separate.");
        report.AppendLine($"a_ability.bin SHA-256 {AbilityHash}\narms_rate.bin SHA-256 {PriceHash}");
        foreach (var edit in edits.OrderBy(e => e.Id))
        {
            var before = Rows.Single(r => r.Id == edit.Id);
            if (edit.Name != before.Name) throw new InvalidDataException("Binary identity is fixed by the Hook catalog; localize the UI label separately.");
            if (edit.Price < 0) throw new InvalidDataException("Negative gil price.");
            if (!CanEditText && (edit.Name != before.Name || edit.Description != before.Description))
                throw new InvalidDataException("Text editing requires verified locale encoding/font coverage.");
            AppendText(output, edit.Id, 0, before.Name, edit.Name);
            AppendText(output, edit.Id, 8, before.Description, edit.Description);
            BinaryPrimitives.WriteInt32LittleEndian(prices.AsSpan(table.PriceTable.Header.EntryTableFileOffset + edit.Id * 4), edit.Price);
            report.AppendLine($"{Manifest.Abilities.Single(a => a.Id == edit.Id).Key} -> #{edit.Id}: {before.Name} -> {edit.Name}; gil {before.Price} -> {edit.Price}");
            if (before.Description != edit.Description) report.AppendLine($"  {before.Description} -> {edit.Description}");
        }
        byte[] abilityBytes = output.ToArray();
        if (abilityBytes.Length > 65535) throw new InvalidDataException("Exported autoability bank exceeds the native WORD length.");
        // Validate the real consumer and text readback before offering an export.
        var readback = HookAbilityRegistry.ReadBankView(abilityBytes, prices);
        foreach (var edit in edits)
        {
            var row = readback.Entries.Single(r => r.Index == edit.Id);
            if (row.GilPrice != edit.Price || (CanEditText &&
                (TextBinary_Util.NormalizeText(row.NameText) != TextBinary_Util.NormalizeText(edit.Name)
                || TextBinary_Util.NormalizeText(row.DescriptionText) != TextBinary_Util.NormalizeText(edit.Description))))
                throw new InvalidDataException("MOD-002 readback mismatch.");
        }
        report.AppendLine($"Output: a_ability.bin {originalAbilities.Length} -> {abilityBytes.Length} bytes; arms_rate.bin {prices.Length} bytes.");
        report.AppendLine("Original bytes are backed up inside the new package; the source is never a destination.");
        string fingerprint = ModAuthoringFiles.Sha256(Encoding.UTF8.GetBytes(ModAuthoringFiles.Sha256(abilityBytes) + ModAuthoringFiles.Sha256(prices)));
        return new Mod002Preview(abilityBytes, prices, fingerprint, report.ToString());
    }

    private void AppendText(MemoryStream output, int id, int field, string before, string after)
    {
        if (before == after) return;
        byte[] script = TextBinary_Util.EncodeScriptToBytes(after, FfxEncoding.UsDecoder);
        long poolOffset = output.Length - (20 + table.AbilityHeader.TotalDataLength);
        if (poolOffset > ushort.MaxValue || poolOffset + script.Length + 1 > 65536)
            throw new InvalidDataException("The text pool exceeds its 16-bit offset range.");
        output.Position = output.Length; output.Write(script); output.WriteByte(0);
        output.Position = 20 + id * 108 + field;
        output.WriteByte((byte)poolOffset); output.WriteByte((byte)(poolOffset >> 8));
    }

    public void Export(string sourceRoot, string abilityPath, string pricePath, string destination,
        IReadOnlyList<Mod002Edit> edits, string confirmedFingerprint)
    {
        ModAuthoringFiles.RejectLinks(sourceRoot); ModAuthoringFiles.RejectLinks(destination);
        sourceRoot = Path.GetFullPath(sourceRoot); destination = Path.GetFullPath(destination);
        if (!ModAuthoringFiles.IsWithin(abilityPath, sourceRoot) || !ModAuthoringFiles.IsWithin(pricePath, sourceRoot)
            || ModAuthoringFiles.IsWithin(destination, sourceRoot) || ModAuthoringFiles.IsWithin(sourceRoot, destination))
            throw new InvalidDataException("Choose a separate mod package destination outside the source tree.");
        ModAuthoringFiles.RejectLinks(destination);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("The package destination already exists.");
        if (!originalAbilities.SequenceEqual(ModAuthoringFiles.Read(abilityPath, 65535))
            || !originalPrices.SequenceEqual(ModAuthoringFiles.Read(pricePath, 100_000)))
            throw new InvalidDataException("Source changed after import; reload before exporting.");
        var preview = Preview(edits);
        if (preview.Fingerprint != confirmedFingerprint) throw new InvalidDataException("The preview is stale; review the changes again.");
        string parent = Path.GetDirectoryName(destination) ?? throw new IOException("Missing destination parent.");
        if (!Directory.Exists(parent)) throw new IOException("The destination parent must exist.");
        string stage = Path.Combine(parent, ".mod002-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            void Write(string relative, byte[] bytes)
            {
                string path = Path.Combine(stage, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                file.Write(bytes); file.Flush(true);
            }
            string abilityRelative = $"data/mods/ffx_ps2/ffx/master/{Locale}/battle/kernel/a_ability.bin";
            string rateLocale = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(pricePath)))))!;
            if (!Locales.Contains(rateLocale)) throw new InvalidDataException("Unknown price-table locale.");
            string priceRelative = $"data/mods/ffx_ps2/ffx/master/{rateLocale}/battle/kernel/arms_rate.bin";
            Write(abilityRelative, preview.Abilities); Write(priceRelative, preview.Prices);
            Write("backup/a_ability.bin", originalAbilities); Write("backup/arms_rate.bin", originalPrices);
            Write("effect-ids.json", Encoding.UTF8.GetBytes(Manifest.Json));
            Write("preview.txt", Encoding.UTF8.GetBytes(preview.Report));
            Write("mod-authoring.json", JsonSerializer.SerializeToUtf8Bytes(new {
                schema = 1, mod = "MOD-002", product_name = "Vanguard Combat Engine", evidence = "RT0", locale = Locale, requires_hook = true,
                hook_compatibility = "managed-row-validation-RT0", required_hook_contract = "ffx.hooks.2026-09-28",
                required_hook_revision = HookCompatibilityProfile.SourceRevision, required_pe_sha256 = HookCompatibilityProfile.SupportedExecutable,
                source_ability_sha256 = AbilityHash, source_price_sha256 = PriceHash,
                ability_path = abilityRelative, price_path = priceRelative,
                ability_sha256 = ModAuthoringFiles.Sha256(preview.Abilities), price_sha256 = ModAuthoringFiles.Sha256(preview.Prices)
            }, new JsonSerializerOptions { WriteIndented = true }));
            Directory.Move(stage, destination);
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
}
