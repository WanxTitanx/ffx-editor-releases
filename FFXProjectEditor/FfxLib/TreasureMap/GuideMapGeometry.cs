using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;

namespace FFXProjectEditor.FfxLib.TreasureMap;

// ── GuideMapGeometry (MAP1 section 11 / YNGM) ─────────────────────────────────────────
// Parses the guide-map geometry used to render the field overview. Section 11 contains
// YNGM chunks (one per scene state). Each YNGM model has: a vertex table (int16 x/y/z
// scaled by the model's local scale / 10), a triangle primitive table, bounds (float) and
// a local transform. The per-model scale (LocalTransform.M11) is what ChestLocationIndex
// uses to convert ATEL world coordinates into guide space.
// ──────────────────────────────────────────────────────────────────────────────────────

public sealed record GuideMapVertex(float X, float Y, float Z);
public sealed record GuideMapTriangle(ushort A, ushort B, ushort C);

public sealed record GuideMapModel(
    int SceneIndex, int Flags,
    IReadOnlyList<GuideMapVertex> Vertices,
    IReadOnlyList<GuideMapTriangle> Triangles,
    Vector3 BoundsMin, Vector3 BoundsMax,
    Matrix4x4 LocalTransform);

public sealed record GuideMapGeometry(IReadOnlyList<GuideMapModel> Models)
{
    public static GuideMapGeometry Read(Map1Archive archive)
    {
        Map1Section? section = archive.FindSection(11);
        if (section == null) return new GuideMapGeometry([]);
        byte[] bytes = section.Bytes;
        var models = new List<GuideMapModel>();
        int cursor = 0;
        while (cursor + 16 <= bytes.Length)
        {
            string tag = Encoding.ASCII.GetString(bytes, cursor, 4);
            int blocks = BitConverter.ToInt32(bytes, cursor + 4);
            int sceneIndex = BitConverter.ToUInt16(bytes, cursor + 10);
            if (blocks < 0 || blocks > (bytes.Length - cursor - 16) / 16)
                throw new InvalidDataException($"Guide-map chunk '{tag}' invalid block count.");
            int payloadOffset = cursor + 16;
            int payloadLength = checked(blocks * 16);
            if (tag == "YNGM")
                models.Add(ReadModel(bytes, payloadOffset, payloadLength, sceneIndex));
            cursor = checked(payloadOffset + payloadLength);
            if (tag == "YNED") break;
        }
        return new GuideMapGeometry(models);
    }

    private static GuideMapModel ReadModel(byte[] bytes, int offset, int length, int sceneIndex)
    {
        Require(length >= 8, "YNGM payload too short.");
        int flags = BitConverter.ToInt32(bytes, offset);
        int blobLength = BitConverter.ToInt32(bytes, offset + 4);
        Require(blobLength >= 0x20 && 8 + blobLength + 0xE0 <= length, "YNGM blob invalid length.");
        int blob = offset + 8;
        int vertexOffset = BitConverter.ToInt32(bytes, blob + 8);
        int vertexCount = BitConverter.ToUInt16(bytes, blob + 0x12);
        Require(vertexOffset >= 0x20 && vertexOffset + vertexCount * 6 <= blobLength, "YNGM vertex table outside blob.");

        int fixedData = blob + blobLength;
        int transformOffset = 0xA0;
        Matrix4x4 transform = ReadMatrix(bytes, fixedData + transformOffset);
        if (!IsScaleMatrix(transform)) { transformOffset = 0x90; transform = ReadMatrix(bytes, fixedData + transformOffset); }
        Vector3 boundsMin = ReadVector3(bytes, fixedData + transformOffset - 0x30);
        Vector3 boundsMax = ReadVector3(bytes, fixedData + transformOffset - 0x20);
        float encodedScale = transform.M11;
        Require(IsScaleMatrix(transform), "YNGM model invalid vertex scale.");

        var vertices = new GuideMapVertex[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            int pos = blob + vertexOffset + i * 6;
            vertices[i] = new GuideMapVertex(
                BitConverter.ToInt16(bytes, pos) * encodedScale / 10f,
                BitConverter.ToInt16(bytes, pos + 2) * transform.M22 / 10f,
                BitConverter.ToInt16(bytes, pos + 4) * transform.M33 / 10f);
        }

        var triangles = new List<GuideMapTriangle>();
        int prim = blob + 0x20, primEnd = blob + vertexOffset;
        while (prim + 16 <= primEnd)
        {
            if (BitConverter.ToUInt16(bytes, prim) == 0xFFFF) break;
            int primType = bytes[prim + 1];
            int count = BitConverter.ToUInt16(bytes, prim + 2);
            if (primType != 0) throw new InvalidDataException($"Unsupported YNGM primitive type {primType}.");
            int recs = prim + 16;
            Require(recs + count * 20 <= primEnd, "YNGM triangle records exceed primitive table.");
            for (int i = 0; i < count; i++)
            {
                int r = recs + i * 20;
                ushort a = BitConverter.ToUInt16(bytes, r + 12), b = BitConverter.ToUInt16(bytes, r + 14), c = BitConverter.ToUInt16(bytes, r + 16);
                Require(a < vertexCount && b < vertexCount && c < vertexCount, "YNGM triangle references missing vertex.");
                triangles.Add(new GuideMapTriangle(a, b, c));
            }
            prim = recs + count * 20;
            prim = (prim + 15) & ~15;
        }
        return new GuideMapModel(sceneIndex, flags, vertices, triangles, boundsMin, boundsMax, transform);
    }

    private static Vector3 ReadVector3(byte[] b, int o) => new(BitConverter.ToSingle(b, o), BitConverter.ToSingle(b, o + 4), BitConverter.ToSingle(b, o + 8));
    private static Matrix4x4 ReadMatrix(byte[] b, int o) => new(
        BitConverter.ToSingle(b, o), BitConverter.ToSingle(b, o + 4), BitConverter.ToSingle(b, o + 8), BitConverter.ToSingle(b, o + 12),
        BitConverter.ToSingle(b, o + 16), BitConverter.ToSingle(b, o + 20), BitConverter.ToSingle(b, o + 24), BitConverter.ToSingle(b, o + 28),
        BitConverter.ToSingle(b, o + 32), BitConverter.ToSingle(b, o + 36), BitConverter.ToSingle(b, o + 40), BitConverter.ToSingle(b, o + 44),
        BitConverter.ToSingle(b, o + 48), BitConverter.ToSingle(b, o + 52), BitConverter.ToSingle(b, o + 56), BitConverter.ToSingle(b, o + 60));
    private static bool IsScaleMatrix(Matrix4x4 m) => float.IsFinite(m.M11) && m.M11 != 0 && float.IsFinite(m.M22) && m.M22 != 0 && float.IsFinite(m.M33) && m.M33 != 0 && Math.Abs(m.M44 - 1f) < 0.001f;
    private static void Require(bool c, string msg) { if (!c) throw new InvalidDataException(msg); }
}
