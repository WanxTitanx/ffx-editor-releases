using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace FFXProjectEditor.FfxLib.MagicDll;

/// <summary>DrawShape indexes the root's flipbook pointer table, not geometry descriptors.
/// Signatures retain animation entries, packet headers and texture/geometry/color bytes;
/// only structurally identical validated resources can be exchanged.</summary>
internal static class MagicShapeResourceLayouts
{
    internal static IReadOnlyDictionary<(int Root, uint Index), string> Read(
        byte[] data, IReadOnlyList<MagicDllRoot> roots, List<(int Start, int Size)> anchors,
        List<(int Start, long Size)> protectedRanges)
    {
        var layouts = new Dictionary<(int Root, uint Index), string>();
        var cache = new Dictionary<int, (string Signature, (int Start, int Size)[] Spans)>();
        long hashedBytes = 0;
        const long maxHashedBytes = 64L * 1024 * 1024;
        bool Fits(long at, long size) => at >= 0 && size >= 0 && at <= data.Length - size;
        ushort U16(int at) => BitConverter.ToUInt16(data, at);
        uint U32(int at) => BitConverter.ToUInt32(data, at);
        foreach (var root in roots)
        {
            long table = (long)root.RootAbs + root.Table3Rel;
            if (!Fits(table, 4L * root.Aux3Count)) continue;
            for (uint index = 0; index < root.Aux3Count; index++)
            {
                uint relative = U32((int)table + (int)index * 4);
                long start = (long)root.RootAbs + relative;
                if (relative == 0 || !Fits(start, 16)) continue;
                int at = (int)start;
                if (cache.TryGetValue(at, out var cached))
                {
                    layouts[(root.RootAbs, index)] = cached.Signature;
                    continue;
                }
                int uniqueFrames = U16(at + 4), nativeFrames = BitConverter.ToInt16(data, at + 6);
                // Native consumers index 8-byte entries and compare against signed count +6.
                // Differing unique/logical frame counts need a separate aliasing contract.
                if (uniqueFrames < 1 || uniqueFrames > 4096 || nativeFrames != uniqueFrames ||
                    !Fits(start, 16L + nativeFrames * 8L)) continue;
                var spans = new List<(int Start, int Size)> { (at, 16 + nativeFrames * 8) };
                long signatureBytes = spans[0].Size;
                bool valid = true;
                for (int frame = 0; frame < nativeFrames && valid; frame++)
                {
                    int entry = at + 16 + frame * 8;
                    int offset = U16(entry);
                    long packet = start + offset;
                    if (offset < 16 + nativeFrames * 8 || !Fits(packet, 32)) { valid = false; break; }
                    int p = (int)packet;
                    ushort flags = U16(p), rectangles = U16(p + 2);
                    long colors = packet + U32(p + 4);
                    int stride = (flags & 8) != 0 ? 24 : 16;
                    long payloadSize = 32L + rectangles * stride;
                    if (rectangles == 0 || rectangles > 16384 || !Fits(packet, payloadSize) ||
                        !Fits(colors, 8L * rectangles) || colors < packet + payloadSize)
                    { valid = false; break; }
                    spans.Add((p, (int)payloadSize));
                    spans.Add(((int)colors, rectangles * 8));
                    signatureBytes += payloadSize + rectangles * 8L;
                    if (signatureBytes > maxHashedBytes - hashedBytes) valid = false;
                }
                if (!valid) continue;
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                foreach (var span in spans)
                {
                    hash.AppendData(BitConverter.GetBytes(span.Size));
                    hash.AppendData(data.AsSpan(span.Start, span.Size));
                }
                string signature = Convert.ToHexString(hash.GetHashAndReset());
                hashedBytes += signatureBytes;
                layouts[(root.RootAbs, index)] = signature;
                cache[at] = (signature, spans.ToArray());
                foreach (var span in spans)
                {
                    anchors.Add(span);
                    protectedRanges.Add((span.Start, span.Size));
                }
            }
        }
        return layouts;
    }
}
