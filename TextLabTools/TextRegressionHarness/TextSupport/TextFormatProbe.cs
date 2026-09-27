using FFXProjectEditor.FfxLib.Text;

namespace TextRegressionHarness.TextSupport;

public enum TextFormatProbeFamily
{
    NameDescription,
    BattleText,
    FieldString,
    AlBhedDictionary,
    PointerScriptTable,
    LegacyMenuMainResource,
    Unsupported
}

public sealed class TextFormatProbeResult
{
    public required TextFormatProbeFamily Family { get; init; }
    public required string ParserModeLabel { get; init; }
    public required string Summary { get; init; }
    public required string ProbeEvidence { get; init; }
}

public static class TextFormatProbe
{
    public static TextFormatProbeResult ProbeFile(string path, Dictionary<byte, char> decoder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return ProbeBytes(File.ReadAllBytes(path), decoder);
    }

    public static TextFormatProbeResult ProbeBytes(byte[] bytes, Dictionary<byte, char> decoder)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(decoder);

        List<string> failures = [];

        if (TryReadNameDescription(bytes, decoder, out NameDescriptionTextTable_File? nameDescription, out string? nameDescriptionFailure))
        {
            return new TextFormatProbeResult
            {
                Family = TextFormatProbeFamily.NameDescription,
                ParserModeLabel = "AUTO PROBE · NAME/DESCRIPTION · PROVEN",
                Summary = "Auto-probed as the proven 4-field name/description family. Safe writer path can reuse the proven serializer with automatic offset rebuild and round-trip validation.",
                ProbeEvidence = $"Entry length {nameDescription!.EntryLength:X2}h · range {nameDescription.MinIndex:X2}h..{nameDescription.MaxIndex:X2}h · {nameDescription.EntryCount} entries · data block {nameDescription.DataBlockLength:X4}h."
            };
        }

        failures.Add($"Name/Description: {nameDescriptionFailure}");

        if (TryReadBattleText(bytes, decoder, out BattleTextTable_File? battleText, out string? battleTextFailure))
        {
            return new TextFormatProbeResult
            {
                Family = TextFormatProbeFamily.BattleText,
                ParserModeLabel = "AUTO PROBE · BATTLE TEXT · PROVEN",
                Summary = "Auto-probed as the proven 8-byte battle-text family. The 4-word layout is structurally proven, but decoded string candidates remain heuristic and the writer stays locked.",
                ProbeEvidence = $"Entry length {battleText!.EntryLength:X2}h · range {battleText.MinIndex:X2}h..{battleText.MaxIndex:X2}h · {battleText.EntryCount} entries · {battleText.TaxonomySummary}"
            };
        }

        failures.Add($"Battle Text: {battleTextFailure}");

        if (TryReadFieldString(bytes, decoder, out TextTable_File? fieldString, out string? fieldStringFailure))
        {
            int firstOffset = fieldString!.Entries.Count == 0
                ? 0
                : fieldString.Entries.Min(entry => Math.Min(entry.RegularOffset, entry.SimplifiedOffset));

            return new TextFormatProbeResult
            {
                Family = TextFormatProbeFamily.FieldString,
                ParserModeLabel = "AUTO PROBE · FIELD STRING · PROVEN",
                Summary = "Auto-probed as the proven 8-byte field-string family. Reader proof is structural, but the standalone write path stays locked until a concrete family is routed through the lab serializer.",
                ProbeEvidence = $"Header length {fieldString.HeaderLength:X4}h ({fieldString.EntryCount} x 8-byte headers) · first string offset {firstOffset:X4}h · {fieldString.EntryCount} decoded entries."
            };
        }

        failures.Add($"Field String: {fieldStringFailure}");

        if (TryReadAlBhedDictionary(bytes, decoder, out AlBhedDictionary_File? alBhedDictionary, out string? alBhedFailure))
        {
            return new TextFormatProbeResult
            {
                Family = TextFormatProbeFamily.AlBhedDictionary,
                ParserModeLabel = "AUTO PROBE · AL BHED DICTIONARY · PROVEN",
                Summary = "Auto-probed as the proven 4-byte Al Bhed dictionary family. Reader proof is structural and mapping-oriented only; no writer is exposed.",
                ProbeEvidence = alBhedDictionary!.Summary
            };
        }

        failures.Add($"Al Bhed Dictionary: {alBhedFailure}");

        if (TryReadPointerScriptTable(bytes, decoder, out PointerScriptTable_File? pointerScriptTable, out string? pointerScriptFailure))
        {
            return new TextFormatProbeResult
            {
                Family = TextFormatProbeFamily.PointerScriptTable,
                ParserModeLabel = "AUTO PROBE · POINTER SCRIPT TABLE · PROVEN",
                Summary = "Auto-probed as the proven 52-slot pointer-script table family. Reader proof is structural only; script writers remain locked.",
                ProbeEvidence = pointerScriptTable!.Summary
            };
        }

        failures.Add($"Pointer Script Table: {pointerScriptFailure}");

        if (TryReadLegacyMenuMainResource(bytes, out LegacyMenuMainResource_File? legacyMenuMain, out string? legacyMenuMainFailure))
        {
            return new TextFormatProbeResult
            {
                Family = TextFormatProbeFamily.LegacyMenuMainResource,
                ParserModeLabel = "AUTO PROBE · LEGACY MENUMAIN RESOURCE · PROVEN",
                Summary = "Auto-probed as the proven legacy menumain non-text container. Metadata and layout are surfaced read-only only.",
                ProbeEvidence = legacyMenuMain!.Summary
            };
        }

        failures.Add($"Legacy MenuMain Resource: {legacyMenuMainFailure}");

        return new TextFormatProbeResult
        {
            Family = TextFormatProbeFamily.Unsupported,
            ParserModeLabel = "AUTO PROBE · DIFFERENT FORMAT · HEURISTIC",
            Summary = "No proven text reader matched this bin yet. The explorer keeps it explicit and read-only instead of faking an empty decode.",
            ProbeEvidence = string.Join(Environment.NewLine, failures)
        };
    }

    static bool TryReadNameDescription(
        byte[] bytes,
        Dictionary<byte, char> decoder,
        out NameDescriptionTextTable_File? file,
        out string failure)
    {
        try
        {
            file = NameDescriptionTextTable_File.Read(bytes, decoder);
            failure = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            file = null;
            failure = ex.Message;
            return false;
        }
    }

    static bool TryReadBattleText(
        byte[] bytes,
        Dictionary<byte, char> decoder,
        out BattleTextTable_File? file,
        out string failure)
    {
        try
        {
            file = BattleTextTable_File.Read(bytes, decoder);
            failure = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            file = null;
            failure = ex.Message;
            return false;
        }
    }

    static bool TryReadFieldString(
        byte[] bytes,
        Dictionary<byte, char> decoder,
        out TextTable_File? file,
        out string failure)
    {
        try
        {
            file = TextTable_File.Read(bytes, decoder);
            failure = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            file = null;
            failure = ex.Message;
            return false;
        }
    }

    static bool TryReadAlBhedDictionary(
        byte[] bytes,
        Dictionary<byte, char> decoder,
        out AlBhedDictionary_File? file,
        out string failure)
    {
        try
        {
            file = AlBhedDictionary_File.Read(bytes, decoder);
            failure = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            file = null;
            failure = ex.Message;
            return false;
        }
    }

    static bool TryReadPointerScriptTable(
        byte[] bytes,
        Dictionary<byte, char> decoder,
        out PointerScriptTable_File? file,
        out string failure)
    {
        try
        {
            file = PointerScriptTable_File.Read(bytes, decoder);
            failure = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            file = null;
            failure = ex.Message;
            return false;
        }
    }

    static bool TryReadLegacyMenuMainResource(
        byte[] bytes,
        out LegacyMenuMainResource_File? file,
        out string failure)
    {
        try
        {
            file = LegacyMenuMainResource_File.Read(bytes);
            failure = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            file = null;
            failure = ex.Message;
            return false;
        }
    }
}
