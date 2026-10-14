using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace FFXProjectEditor.FfxLib.Mods;

internal sealed record WorkshopPiece(int Slot, ulong Id, byte Owner, byte Type, byte Mode, byte GlobalRank,
    bool FifthUnlocked, IReadOnlyList<ushort> Abilities, IReadOnlyList<ulong> AbilityIds, IReadOnlyList<byte> Ranks);
internal sealed record WorkshopInspection(string SaveId, ulong Revision, ulong SuccessfulRolls,
    IReadOnlyList<WorkshopPiece> Pieces, IReadOnlyList<ushort> Materials);

/// <summary>Read-only consumer of the Hooks v1 host protocol; never a native save writer.</summary>
internal static class WorkshopInspector
{
    internal const int StateSize = 16260;
    private const int GearBase = 0x44DC, ItemBase = 0x3F0C, QuantityBase = 0x410C;

    public static WorkshopInspection ReadDirectory(string directory)
    {
        directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        ModAuthoringFiles.RejectLinks(directory);
        if (File.Exists(Path.Combine(directory, "pending.json")))
            throw new InvalidDataException("Unresolved Workshop journal; recover with the owning host before inspection.");
        byte[] native = ModAuthoringFiles.Read(Path.Combine(directory, "native.bin"), 0x6900);
        byte[] sidecar = ModAuthoringFiles.Read(Path.Combine(directory, "sidecar.json"), 256_000);
        using var origin = Envelope(ModAuthoringFiles.Read(Path.Combine(directory, "origin.json"), 256_000));
        var originPayload = origin.RootElement.GetProperty("payload");
        Names(originPayload, "save_id", "source", "source_sha256");
        string saveId = Hex(originPayload, "save_id", 32);
        _ = Hex(originPayload, "source_sha256", 64);
        if (string.IsNullOrWhiteSpace(originPayload.GetProperty("source").GetString())) Fail("Missing source identity.");
        var inspection = Read(native, sidecar, directory, saveId);
        if (File.Exists(Path.Combine(directory, "pending.json"))
            || !native.SequenceEqual(ModAuthoringFiles.Read(Path.Combine(directory, "native.bin"), 0x6900))
            || !sidecar.SequenceEqual(ModAuthoringFiles.Read(Path.Combine(directory, "sidecar.json"), 256_000)))
            Fail("Workshop changed during inspection; reload a stable host workspace.");
        return inspection;
    }

    internal static WorkshopInspection Read(byte[] native, byte[] sidecar, string workspace, string expectedSaveId)
    {
        if (native.Length != 0x6900) Fail("Expected a 0x6900-byte experimental host snapshot.");
        using var document = Envelope(sidecar);
        var payload = document.RootElement.GetProperty("payload");
        Names(payload, "native_sha256", "save_id", "schema", "state", "workspace_key");
        if (payload.GetProperty("schema").GetInt32() != 1) Fail("Unsupported sidecar schema.");
        string saveId = Hex(payload, "save_id", 32);
        string directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace));
        if (saveId != expectedSaveId || Hex(payload, "workspace_key", 64) != ModAuthoringFiles.Sha256(Encoding.UTF8.GetBytes(directory))
            || Hex(payload, "native_sha256", 64) != ModAuthoringFiles.Sha256(native)) Fail("Foreign or changed save/workspace association.");
        string encoded = payload.GetProperty("state").GetString() ?? "";
        byte[] state;
        try { state = Convert.FromBase64String(encoded); }
        catch (FormatException) { throw new InvalidDataException("Invalid Workshop state encoding."); }
        if (state.Length != StateSize || Convert.ToBase64String(state) != encoded) Fail("Invalid Workshop state size/encoding.");
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(state);
        ulong revision = U64(state, 4), nextId = U64(state, 12), rolls = U64(state, 28);
        if (version != 1 || nextId == 0 || nextId > ulong.MaxValue - 1024 || revision == ulong.MaxValue || rolls == ulong.MaxValue)
            Fail("Unsupported or exhausted Workshop state.");
        var identities = new HashSet<ulong>();
        void Identity(ulong id) { if (id == 0 || id >= nextId || !identities.Add(id)) Fail("Duplicate or out-of-range piece/ability identity."); }
        var pieces = new List<WorkshopPiece>();
        for (int slot = 0; slot < 200; slot++)
        {
            int p = 36 + slot * 80;
            if (!state.AsSpan(p, 22).SequenceEqual(native.AsSpan(GearBase + slot * 22, 22))) Fail("Native equipment differs from its sidecar.");
            ulong id = U64(state, p + 22);
            byte mode = state[p + 30], rank = state[p + 31], unlocked = state[p + 32];
            byte capacity = state[p + 11];
            ushort fifth = U16(state, p + 78);
            byte[] ranks = state.AsSpan(p + 33, 5).ToArray();
            ulong[] ids = Enumerable.Range(0, 5).Select(i => U64(state, p + 38 + i * 8)).ToArray();
            if (id == 0)
            {
                if (state[p + 2] != 0 || mode != 0 || rank != 0 || unlocked != 0 || fifth != 0xFF
                    || ranks.Any(r => r != 0) || ids.Any(i => i != 0)) Fail("Vacant slot has extension metadata.");
                continue;
            }
            Identity(id);
            if (state[p + 2] == 0 || capacity > 4 || mode > 2 || rank > 10 || unlocked > 1
                || (unlocked == 1 && capacity != 4) || (mode != 1 && rank != 0)) Fail("Invalid piece mode/rank/capacity.");
            var abilities = new ushort[5];
            for (int i = 0; i < 5; i++)
            {
                ushort word = i == 4 ? fifth : U16(state, p + 14 + i * 2);
                if (word == 0) word = 0xFF;
                abilities[i] = word;
                bool empty = word == 0xFF;
                if ((!empty && (word < 0x8000 || word >= 0x8400)) || ranks[i] > 10 || (mode != 2 && ranks[i] != 0)
                    || (i < 4 && i >= capacity && !empty) || (i == 4 && unlocked == 0 && !empty)
                    || (i == 4 && !empty && !Supported(word)) || empty != (ids[i] == 0) || (empty && ranks[i] != 0))
                    Fail("Invalid ability identity/rank or unsupported fifth ability.");
                if (!empty) Identity(ids[i]);
            }
            pieces.Add(new WorkshopPiece(slot, id, state[p + 4], state[p + 5], mode, rank, unlocked == 1,
                Array.AsReadOnly(abilities), Array.AsReadOnly(ids), Array.AsReadOnly(ranks)));
        }
        var nativeItems = new ushort[112];
        var seen = new HashSet<int>();
        for (int slot = 0; slot < 256; slot++)
        {
            ushort word = U16(native, ItemBase + slot * 2);
            if (word < 0x2000 || word >= 0x2070) continue;
            int item = word - 0x2000;
            if (!seen.Add(item)) Fail("Ambiguous duplicate native material.");
            nativeItems[item] = native[QuantityBase + slot];
        }
        var materials = new ushort[112];
        for (int i = 0; i < materials.Length; i++)
        {
            materials[i] = U16(state, 16036 + i * 2);
            if (materials[i] > 255 || materials[i] != nativeItems[i]) Fail("Native materials differ from the sidecar.");
        }
        return new WorkshopInspection(saveId, revision, rolls, pieces.AsReadOnly(), Array.AsReadOnly(materials));
    }

    private static bool Supported(ushort word) => word is >= 0x8062 and <= 0x8079 or 0x8054 or 0x8055;
    private static ushort U16(byte[] data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
    private static ulong U64(byte[] data, int offset) => BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(offset));
    private static void Fail(string message) => throw new InvalidDataException(message);

    private static string Hex(JsonElement obj, string key, int length)
    {
        string value = obj.GetProperty(key).GetString() ?? "";
        if (value.Length != length || value.Any(c => !((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))) Fail("Invalid identity/hash field.");
        return value;
    }

    private static void Names(JsonElement obj, params string[] expected)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.EnumerateObject().Select(p => p.Name).OrderBy(x => x, StringComparer.Ordinal)
                .SequenceEqual(expected.OrderBy(x => x, StringComparer.Ordinal))) Fail("Unexpected or duplicate contract fields.");
    }

    private static JsonDocument Envelope(byte[] bytes)
    {
        if (bytes.Length > 256_000) Fail("Oversized sidecar document.");
        var doc = JsonDocument.Parse(bytes);
        try
        {
            Names(doc.RootElement, "payload", "sha256");
            string hash = Hex(doc.RootElement, "sha256", 64);
            if (hash != ModAuthoringFiles.Sha256(Encoding.ASCII.GetBytes(Canonical(doc.RootElement.GetProperty("payload")))))
                Fail("Sidecar integrity mismatch.");
            return doc;
        }
        catch { doc.Dispose(); throw; }
    }

    // Python host uses sorted keys, compact separators and ensure_ascii=True (lowercase hex).
    // .NET's default escaping differs for '+' and non-ASCII paths; do not hash serializer output.
    internal static string Canonical(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(",", value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => Quote(p.Name) + ":" + Canonical(p.Value))) + "}",
        JsonValueKind.String => Quote(value.GetString()!),
        JsonValueKind.Number when value.TryGetUInt64(out ulong number) => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => throw new InvalidDataException("Unsupported sidecar JSON value.")
    };

    private static string Quote(string text)
    {
        var result = new StringBuilder("\"");
        foreach (char c in text)
            result.Append(c switch { '"' => "\\\"", '\\' => "\\\\", '\b' => "\\b", '\f' => "\\f", '\n' => "\\n", '\r' => "\\r", '\t' => "\\t",
                < ' ' or > '~' => "\\u" + ((int)c).ToString("x4"), _ => c.ToString() });
        return result.Append('"').ToString();
    }
}
