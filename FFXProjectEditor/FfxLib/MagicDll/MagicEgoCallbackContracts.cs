using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;

namespace FFXProjectEditor.FfxLib.MagicDll;

internal static class MagicEgoCallbackContracts
{
    private sealed record ConstantAnchor(uint Rva, byte[] Bytes);
    private sealed record CellContract(MagicEgoCallbackKind Kind, uint? DataRva, byte[]? DataBytes,
        IReadOnlyList<ConstantAnchor> Constants);

    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyDictionary<(string Name, int Offset, uint Rva), CellContract>>>
        Profiles = new(ReadProfiles);

    public static IReadOnlyList<MagicEgoDescriptorInfo> Annotate(MagicDllFile file,
        IReadOnlyList<MagicEgoDescriptorInfo> types, IReadOnlySet<uint> relocations, uint imageBase)
    {
        var result = types.Select(t => t with
            { CallbackKinds = Array.AsReadOnly(new MagicEgoCallbackKind[t.CallbackRvas.Count]) }).ToArray();
        if (result.Length == 0) return Array.AsReadOnly(result);
        var textSections = file.Sections.Where(s => s.Name == ".text").Take(2).ToArray();
        var dataSections = file.Sections.Where(s => s.Name == ".data").Take(2).ToArray();
        if (textSections.Length != 1 || dataSections.Length != 1) return Array.AsReadOnly(result);
        var text = textSections[0];
        if (text.RawPtr < 0 || text.VirtualAddress < 0 || text.VirtualSize <= 0 ||
            text.RawSize is <= 0 or > 64 * 1024 * 1024 ||
            (long)text.RawPtr + text.RawSize > file.FileBytes.Length)
            return Array.AsReadOnly(result);

        // Payload edits and file renames preserve code identity. Rebased headers,
        // modified code, missing code relocations and substituted callback cells
        // must not inherit a meaning merely because an Ego name still matches.
        uint[] codeRelocations = relocations.Where(r => r >= text.VirtualAddress &&
            (long)r < (long)text.VirtualAddress + Math.Min(text.RawSize, text.VirtualSize)).OrderBy(r => r).ToArray();
        var relocationBytes = new byte[codeRelocations.Length * 4];
        for (int i = 0; i < codeRelocations.Length; i++)
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(relocationBytes.AsSpan(i * 4, 4), codeRelocations[i]);
        string hash = Convert.ToHexString(SHA256.HashData(file.FileBytes.AsSpan(text.RawPtr, text.RawSize))).ToLowerInvariant();
        string relocationHash = Convert.ToHexString(SHA256.HashData(relocationBytes)).ToLowerInvariant();
        string identity = string.Create(CultureInfo.InvariantCulture,
            $"{hash}|{relocationHash}|{text.VirtualAddress:x}|{dataSections[0].VirtualAddress:x}|{imageBase:x}");
        if (!Profiles.Value.TryGetValue(identity, out var cells)) return Array.AsReadOnly(result);
        for (int type = 0; type < result.Length; type++)
        {
            var descriptor = result[type];
            var kinds = new MagicEgoCallbackKind[descriptor.CallbackRvas.Count];
            for (int index = 0; index < kinds.Length; index++)
                if (cells.TryGetValue((descriptor.Name, 24 + 4 * index, descriptor.CallbackRvas[index]), out var contract) &&
                    MatchesDataAnchor(contract))
                    kinds[index] = contract.Kind;
            result[type] = descriptor with { CallbackKinds = Array.AsReadOnly(kinds) };
        }
        return Array.AsReadOnly(result);

        bool MatchesDataAnchor(CellContract contract)
        {
            if (contract.DataRva is not { } rva) return true;
            if (contract.DataBytes is not { Length: 20 } bytes) return false;
            var data = dataSections[0];
            if ((long)rva < data.VirtualAddress || (long)rva + bytes.Length >
                (long)data.VirtualAddress + Math.Min(data.RawSize, data.VirtualSize)) return false;
            long at = (long)data.RawPtr + rva - data.VirtualAddress;
            if (at < 0 || at + bytes.Length > file.FileBytes.Length) return false;
            for (uint cell = 0; cell < bytes.Length; cell += 4)
                if (!relocations.Contains(rva + cell)) return false;
            if (!file.FileBytes.AsSpan((int)at, bytes.Length).SequenceEqual(bytes)) return false;
            foreach (var constant in contract.Constants)
            {
                var section = file.Sections.FirstOrDefault(s => s.Name == ".rdata" &&
                    constant.Rva >= s.VirtualAddress && (long)constant.Rva + constant.Bytes.Length <=
                    (long)s.VirtualAddress + Math.Min(s.RawSize, s.VirtualSize));
                if (section == null) return false;
                long offset = (long)section.RawPtr + constant.Rva - section.VirtualAddress;
                if (offset < 0 || offset + constant.Bytes.Length > file.FileBytes.Length ||
                    !file.FileBytes.AsSpan((int)offset, constant.Bytes.Length).SequenceEqual(constant.Bytes)) return false;
            }
            return true;
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<(string Name, int Offset, uint Rva), CellContract>> ReadProfiles()
    {
        var profiles = new Dictionary<string, IReadOnlyDictionary<(string, int, uint), CellContract>>(StringComparer.Ordinal);
        foreach (string line in MagicEgoCallbackProfiles.Data.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = line.Split('|');
            var cells = new Dictionary<(string, int, uint), CellContract>();
            foreach (string entry in parts[5].Split(';'))
            {
                string[] values = entry.Split(',');
                cells.Add((values[0], int.Parse(values[1], CultureInfo.InvariantCulture),
                    uint.Parse(values[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture)),
                    new CellContract(Enum.Parse<MagicEgoCallbackKind>(values[3]),
                        values.Length >= 6 ? uint.Parse(values[4], NumberStyles.HexNumber, CultureInfo.InvariantCulture) : null,
                        values.Length >= 6 ? Convert.FromHexString(values[5]) : null,
                        values.Length == 7 ? values[6].Split('/').Select(anchor =>
                        {
                            string[] pair = anchor.Split(':');
                            return new ConstantAnchor(uint.Parse(pair[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                                Convert.FromHexString(pair[1]));
                        }).ToArray() : Array.Empty<ConstantAnchor>()));
            }
            profiles.Add(string.Join('|', parts.Take(5)), cells);
        }
        return profiles;
    }
}
