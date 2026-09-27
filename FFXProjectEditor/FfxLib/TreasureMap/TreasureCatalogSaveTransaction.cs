using FFXProjectEditor.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.TreasureMap;

// ── TreasureCatalogWriter / TreasureCatalogSaveTransaction ─────────────────────────────
// Byte-safe writer + atomic save for takara.bin.
//   Write():      re-writes only the 4-byte records in place (count is fixed); validates
//                 identity/offset per record and that header + length stay untouched.
//   Save():       stage to .zwtmp -> re-read + byte-compare verify -> atomically move over
//                 the original; on failure restores the original bytes (rollback). This is
//                 the same safe pattern used by the rest of the editor's writers.
// ──────────────────────────────────────────────────────────────────────────────────────

public static class TreasureCatalogWriter
{
    public static byte[] Write(TreasureCatalog source, IEnumerable<TreasureRecord> records)
    {
        byte[] output = File.ReadAllBytes(source.Path);
        TreasureRecord[] edited = records.OrderBy(r => r.Id).ToArray();
        if (edited.Length != source.Records.Count)
            throw new InvalidDataException("Treasure record count cannot be changed.");
        for (int id = 0; id < edited.Length; id++)
        {
            TreasureRecord record = edited[id];
            if (record.Id != id || record.FileOffset != TreasureCatalog.HeaderLength + id * 4)
                throw new InvalidDataException($"Treasure record {id} has invalid identity or offset.");
            int off = record.FileOffset;
            output[off] = record.RawKind;
            output[off + 1] = record.Quantity;
            output[off + 2] = (byte)record.Type;
            output[off + 3] = (byte)(record.Type >> 8);
        }
        ValidateOutput(source, output);
        return output;
    }

    private static void ValidateOutput(TreasureCatalog source, byte[] output)
    {
        byte[] original = File.ReadAllBytes(source.Path);
        if (output.Length != original.Length)
            throw new InvalidDataException("Staged treasure catalog changed file length.");
        if (!output.AsSpan(0, TreasureCatalog.HeaderLength).SequenceEqual(original.AsSpan(0, TreasureCatalog.HeaderLength)))
            throw new InvalidDataException("Staged treasure catalog changed takara.bin header.");
    }
}

public static class TreasureCatalogSaveTransaction
{
    public static TreasureCatalog Save(TreasureCatalog source, byte[] output)
    {
        string tmp = source.Path + ".zwtmp";
        try
        {
            File.WriteAllBytes(tmp, output);
        DebugLog.Info("TreasureMap.Save", $"Staged {output.Length} bytes to {Path.GetFileName(tmp)}");
            TreasureCatalog verified = TreasureCatalog.Read(tmp);
            if (verified.Records.Count != source.Records.Count)
                throw new InvalidDataException("Staged treasure catalog changed record count.");
            if (!File.ReadAllBytes(tmp).SequenceEqual(output))
                throw new InvalidDataException("Staged treasure catalog did not verify byte-for-byte.");
            byte[] original = File.ReadAllBytes(source.Path);
            try { DebugLog.Info("TreasureMap.Save", $"Committing {Path.GetFileName(source.Path)}");
            File.Move(tmp, source.Path, true); }
            catch (Exception saveError)
            {
                try { File.WriteAllBytes(source.Path, original); }
                catch (Exception rollbackError)
                {
                    DebugLog.Error("TreasureMap.Save", "Save + rollback both failed", new AggregateException(saveError, rollbackError));
                throw new AggregateException("Treasure save and rollback both failed. Use Recovery.", saveError, rollbackError);
                }
                DebugLog.Error("TreasureMap.Save", "Save failed, rolling back", saveError);
                throw new IOException("Treasure save failed. Project file was restored.", saveError);
            }
            return TreasureCatalog.Read(source.Path);
        }
        finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
    }
}
