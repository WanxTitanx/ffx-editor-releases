using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FFXProjectEditor.FfxLib.Save;

namespace FFXProjectEditor.FfxLib.Mods;

internal sealed record WorkshopProductionInspection(string Format, string Sha256, bool Pending, byte[]? State, string Report);
internal sealed record AscensionRecipe(string Key, int OwnerMinimum, int OwnerMaximum, int GearKind, int Gil, IReadOnlyDictionary<int, int> Items);

internal static class WorkshopProductionInspector
{
    private const int StateBytes = 16260, SaveBytes = 0x6900;
    public static IReadOnlyList<AscensionRecipe> AscensionRecipes { get; } = new[]
    {
        new AscensionRecipe("aeon_break_hp_mp_limit", 8, 17, 1, 10000000, new Dictionary<int,int> { [108]=60,[69]=60,[110]=30,[80]=2 }),
        new AscensionRecipe("aeon_break_damage_limit", 8, 17, 0, 15000000, new Dictionary<int,int> { [53]=99,[111]=30,[109]=20,[80]=3 })
    };
    private static string Hash(ReadOnlySpan<byte> bytes) => ModAuthoringFiles.Sha256(bytes.ToArray());
    private static bool MatchesHash(byte[] bytes, int offset, ReadOnlySpan<byte> data) => Convert.ToHexString(bytes.AsSpan(offset, 32)).ToLowerInvariant() == Hash(data);

    public static WorkshopProductionInspection Read(string path, byte[]? nativeImage, string nativeWindowsSavePath)
    {
        byte[] bytes = ModAuthoringFiles.Read(path, 1 << 20);
        if (bytes.Length < 8) throw new InvalidDataException("Truncated production record.");
        string magic = Encoding.ASCII.GetString(bytes, 0, 8), hash = ModAuthoringFiles.Sha256(bytes);
        if (magic is not ("FFXWKS01" or "FFXWKCP1" or "FFXWKTX1" or "FFXASCP1" or "FFXASTX1"))
            throw new InvalidDataException("This is not a production Workshop/Ascension record. Experimental JSON uses a separate inspector.");
        if (string.IsNullOrWhiteSpace(nativeWindowsSavePath) || nativeWindowsSavePath.Length > 4096 || !nativeWindowsSavePath.Contains('\\'))
            throw new InvalidDataException("Supply the exact native Windows save path used by the producer.");
        byte[] normalizedPath = Encoding.Unicode.GetBytes(nativeWindowsSavePath.ToLowerInvariant());
        if (bytes.Length < 72 || !MatchesHash(bytes, 8, normalizedPath)) throw new InvalidDataException("Foreign save path identity.");
        if (magic is "FFXWKTX1" or "FFXASTX1")
        {
            if (magic == "FFXWKTX1" && bytes.Length != 72 + 32 + SaveBytes + StateBytes + 62 + 188 + 16912)
                throw new InvalidDataException("Workshop v5 transaction intent size mismatch.");
            if (!MatchesHash(bytes, 40, bytes.AsSpan(72))) throw new InvalidDataException("Pending transaction body hash mismatch.");
            return new(magic, hash, true, null, "Opaque pending production intent: envelope hash verified. Recovery belongs to the owning host; inspection does not admit, commit or restore it.");
        }
        if (magic == "FFXASCP1")
        {
            if (bytes.Length != 104 + 680 || !MatchesHash(bytes, 72, bytes.AsSpan(104))) throw new InvalidDataException("Invalid paid receipt record.");
            return new(magic, hash, false, null, "Ascension ledger envelope verified. State hash and save/piece/ability identities still require ledger admission.");
        }
        int stateAt; byte[] image;
        if (magic == "FFXWKS01")
        {
            if (bytes.Length != 104 + StateBytes || nativeImage?.Length != SaveBytes ||
                !MatchesHash(bytes, 40, nativeImage) || !MatchesHash(bytes, 72, bytes.AsSpan(104)))
                throw new InvalidDataException("Workshop image/state fingerprint mismatch.");
            image = nativeImage; stateAt = 104;
        }
        else
        {
            stateAt = 72 + 32 + SaveBytes + 4;
            if (bytes.Length != stateAt + StateBytes || !MatchesHash(bytes, 40, bytes.AsSpan(72))) throw new InvalidDataException("Checkpoint body size/hash mismatch.");
            image = bytes.AsSpan(104, SaveBytes).ToArray();
            byte[] checkedImage = (byte[])image.Clone(); checkedImage.AsSpan(25844,4).Clear();
            ushort checksum = FfxSaveChecksum.Compute(checkedImage);
            if (image[25846] != 0 || image[25847] != 0 || BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(26)) != checksum ||
                BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(25844)) != checksum)
                throw new InvalidDataException("Checkpoint native save checksum mismatch.");
            if (nativeImage != null && (nativeImage.Length != SaveBytes ||
                (Hash(nativeImage) != Convert.ToHexString(bytes.AsSpan(72,32)).ToLowerInvariant() && Hash(nativeImage) != Hash(image))))
                throw new InvalidDataException("Checkpoint anchor does not match the supplied disk image.");
            int pool = stateAt - 4;
            if (bytes[pool] > 1 || (bytes[pool] == 0 && bytes.AsSpan(pool + 1, 3).ToArray().Any(b => b != 0)))
                throw new InvalidDataException("Invalid checkpoint ownership metadata.");
            if (bytes[pool] == 1 && (bytes[pool+1] is 0 or > 200 || bytes[pool+3] != 200 || bytes[pool+2] > 200 ||
                image[22529] != bytes[pool+2] || image[22530] != bytes[pool+3])) throw new InvalidDataException("Checkpoint pool ownership differs from its native image.");
        }
        byte[] state = bytes.AsSpan(stateAt, StateBytes).ToArray();
        for (int slot = 0; slot < 200; slot++)
            if (!state.AsSpan(36 + slot * 80, 22).SequenceEqual(image.AsSpan(0x44dc + slot * 22, 22)))
                throw new InvalidDataException($"Equipment differs from production state at slot {slot}.");
        var materials = new ushort[112]; var seen = new HashSet<int>();
        for (int slot = 0; slot < 256; slot++)
        {
            ushort word = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(0x3f0c + slot * 2));
            if (word is >= 0x2000 and < 0x2070)
            { int item = word - 0x2000; if (!seen.Add(item)) throw new InvalidDataException("Duplicate native item identity."); materials[item] = image[0x410c + slot]; }
        }
        for (int item = 0; item < 112; item++)
            if (materials[item] != BinaryPrimitives.ReadUInt16LittleEndian(state.AsSpan(16036 + item * 2))) throw new InvalidDataException("Workshop material snapshot mismatch.");
        ulong revision = BinaryPrimitives.ReadUInt64LittleEndian(state.AsSpan(4));
        var report = new StringBuilder($"{magic}; revision {revision}; state {Hash(state)}.\n");
        for(int slot=0;slot<200;slot++)
        {
            int at=36+slot*80;if(state[at+2]==0)continue;
            ulong piece=BinaryPrimitives.ReadUInt64LittleEndian(state.AsSpan(at+22));
            var words=Enumerable.Range(0,4).Select(i=>BinaryPrimitives.ReadUInt16LittleEndian(state.AsSpan(at+14+i*2))).ToArray();
            int filled=words.Count(word=>word is not (0 or 255));ushort fifth=BinaryPrimitives.ReadUInt16LittleEndian(state.AsSpan(at+78));
            report.AppendLine($"Slot {slot}: pieceId {piece}; owner {state[at+4]}; kind {state[at+5]}; native capacity {state[at+11]}/4; filled {filled}/4");
            report.AppendLine("Native: "+string.Join(", ",words.Select(word=>$"0x{word:X4}"))+$"; fifth unlocked: {state[at+32]==1}; fifth word: 0x{fifth:X4}");
            report.AppendLine("Ability IDs: "+string.Join(", ",Enumerable.Range(0,5).Select(i=>BinaryPrimitives.ReadUInt64LittleEndian(state.AsSpan(at+38+i*8))))+
                "; ranks: "+string.Join(", ",state.AsSpan(at+33,5).ToArray()));
        }
        report.AppendLine("The logical fifth is independent of native capacity. Consumer/receipt admission is separate. No save or checkpoint was written.");
        return new(magic, hash, magic == "FFXWKCP1", state, report.ToString());
    }

    public static async Task<HookAdmission> AdmitStateAsync(byte[] state, HookOfflineValidator validator, CancellationToken token,
        byte[]? ledger = null, byte[]? saveId = null, byte[]? request = null, byte[]? economy = null)
    {
        if (state.Length != StateBytes) throw new InvalidDataException("Workshop state must be 16260 bytes.");
        string temporary = Path.Combine(Path.GetTempPath(), "ffx-workshop-inspect-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporary);
        try
        {
            string statePath = Path.Combine(temporary, "state.bin"); await File.WriteAllBytesAsync(statePath, state, token);
            var args = new List<string>(); string mode = "workshop-state";
            if (ledger != null || saveId != null)
            {
                if (ledger?.Length != 680 || saveId?.Length != 32) throw new InvalidDataException("Expected a 680-byte ledger and 32-byte producer save identity.");
                mode = "ascension";
                foreach (var (name, data) in new[] { ("ledger.bin", ledger), ("save-id.bin", saveId) })
                { string path = Path.Combine(temporary, name); await File.WriteAllBytesAsync(path, data, token); args.Add(path); }
            }
            else if (request != null || economy != null)
            {
                if (request?.Length != 62 || economy?.Length != 188) throw new InvalidDataException("Workshop v5 request/economy size mismatch.");
                mode = "workshop-plan";
                foreach (var (name, data) in new[] { ("request.bin", request), ("economy.bin", economy) })
                { string path = Path.Combine(temporary, name); await File.WriteAllBytesAsync(path, data, token); args.Add(path); }
            }
            var result = await validator.ValidateAsync(mode, statePath, args, token);
            if (!result.Admitted) throw new InvalidDataException(result.Report);
            return result;
        }
        finally { Directory.Delete(temporary, true); }
    }
}

internal static class ArcanaRecordInspector
{
    public static async Task<string> InspectAsync(string recordPath, string expectedSaveHash, string expectedPackHash, HookOfflineValidator validator, CancellationToken token)
    {
        byte[] bytes = ModAuthoringFiles.Read(recordPath, 276);
        if (bytes.Length != 276 || Encoding.ASCII.GetString(bytes, 0, 8) != "ARCANA01" ||
            Convert.ToHexString(bytes.AsSpan(16, 32)).ToLowerInvariant() != expectedSaveHash ||
            Convert.ToHexString(bytes.AsSpan(48, 32)).ToLowerInvariant() != expectedPackHash)
            throw new InvalidDataException("Foreign or invalid Arcana save/pack identity.");
        var result = await validator.ValidateAsync("arcana", recordPath, System.Array.Empty<string>(), token);
        if (!result.Admitted || HookExportTransaction.CurrentHash(recordPath) != ModAuthoringFiles.Sha256(bytes)) throw new InvalidDataException("Arcana record rejected or changed during inspection.");
        ulong revision = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(80));
        return $"ARCANA01 / revision {revision} / {result.InputSha256}\nCompiled catalog; external assets are presentation only. No ownership changes.\n{result.Report}";
    }
}
