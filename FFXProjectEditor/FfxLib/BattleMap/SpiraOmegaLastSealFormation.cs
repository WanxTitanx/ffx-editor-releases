using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace FFXProjectEditor.FfxLib.BattleMap;

public static class SpiraOmegaLastSealFormation
{
    public const string NativeSha256 = "67783f9e27de6e729c53b36edd05e9acfdccae28727aaf38f3e2aa97abe97891";

    public static byte[] Build(byte[] original)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant() != NativeSha256)
            throw new InvalidDataException("Last Seal formation requires the frozen native omeg01_10.");
        int start = Read(original, 16), slots = Read(original, 12) + 12;
        var before = BattleArenaAnchors_File.ReadFromBattleBin("omeg01_10", original);
        if (before.AreaCount != 2 || before.PrimaryMonsterAnchors.Count != 3 || Read(original, 32) != original.Length
            || Enumerable.Range(0, 8).Any(i => BinaryPrimitives.ReadUInt16LittleEndian(original.AsSpan(slots + 2 * i)) != (i == 0 ? 0x1064 : 0xFFFF)))
            throw new InvalidDataException("Unexpected Omega area/slot directory.");

        // The second native area has five live coordinates but only one physical
        // staging-A entry. The general two-area grower correctly refuses it.
        // Preserve every original array byte and all five area-1 live positions.
        // Add private area-0 arrays plus a real five-entry area-1 staging-A array:
        // new helpers must not index through that one-entry reserve into live data.
        const int count = 5, stride = 16, arraySize = count * stride;
        byte[] result = new byte[original.Length + 4 * arraySize];
        original.CopyTo(result, 0);
        int appended = original.Length;
        foreach (int field in new[] { 0x1C, 0x20, 0x24 })
        {
            int old = start + Read(original, start + field);
            original.AsSpan(old, 3 * stride).CopyTo(result.AsSpan(appended));
            if (field != 0x20)
                for (int index = 3; index < count; index++)
                    original.AsSpan(old + 2 * stride, stride).CopyTo(result.AsSpan(appended + index * stride));
            Write(result, start + field, appended - start);
            appended += arraySize;
        }
        int areaOneStaging = start + BattleArenaAnchors_File.AreaRecordStride + 0x1C;
        int oldAreaOneStaging = start + Read(original, areaOneStaging);
        for (int index = 0; index < count; index++)
            original.AsSpan(oldAreaOneStaging, stride).CopyTo(result.AsSpan(appended + index * stride));
        Write(result, areaOneStaging, appended - start);
        result[start + 6] = count;
        Write(result, 32, result.Length);
        var boss = before.PrimaryMonsterAnchors[0];
        var positions = new[] { (boss.X, boss.Y, boss.Z), (12f, 0f, 25f), (-75f, 0f, -5f), (10f, 0f, 85f), (-70f, 0f, 70f) };
        int live = start + Read(result, start + 0x20);
        for (int i = 0; i < count; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan(live + 16 * i), positions[i].Item1);
            BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan(live + 16 * i + 4), positions[i].Item2);
            BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan(live + 16 * i + 8), positions[i].Item3);
            BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan(live + 16 * i + 12), 0);
        }
        ushort[] ids = { 0x1064, 0x138F, 0x1390, 0x1391, 0x1392, 0xFFFF, 0xFFFF, 0xFFFF };
        for (int i = 0; i < ids.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(slots + 2 * i), ids[i]);
        if (!IsConfined(original, result)) throw new InvalidDataException("Omega formation escaped its field allowlist.");
        var after = BattleArenaAnchors_File.ReadFromBattleBin("omeg01_10", result);
        if (after.AreaCount != 2 || after.PrimaryMonsterAnchors.Count != count
            || !before.Notes.SequenceEqual(after.Notes))
            throw new InvalidDataException("Omega formation reread introduced diagnostics.");
        return result;
    }

    public static bool IsConfined(byte[] original, byte[] result)
    {
        if (original.Length != 4132 || result.Length != 4452) return false;
        int start = Read(original, 16), slots = Read(original, 12) + 12;
        for (int i = 0; i < original.Length; i++)
        {
            bool allowed = i >= 32 && i < 36 || i >= slots && i < slots + 16 || i == start + 6
                || i >= start + 0x1C && i < start + 0x28
                || i >= start + 0x60 + 0x1C && i < start + 0x60 + 0x20;
            if (!allowed && original[i] != result[i]) return false;
        }
        return true;
    }

    static int Read(byte[] bytes, int at) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at, 4));
    static void Write(byte[] bytes, int at, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at, 4), value);
}
