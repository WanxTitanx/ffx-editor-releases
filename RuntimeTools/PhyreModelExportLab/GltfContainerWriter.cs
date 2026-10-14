using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace PhyreModelExportLab
{
    /// <summary>
    /// 📦 GLB container writer (F5 asset-loading): converts a self-contained glTF JSON (single data-URI buffer,
    /// which is exactly what DescriptorStaticGltfWriter emits) into a binary .glb container.
    ///
    /// GLB is ~25-33% smaller on the wire than the base64 data-URI JSON (base64 overhead) and parses faster in
    /// the browser (single binary fetch, no base64 decode). The conversion is byte-safe: it only repackages the
    /// existing JSON + buffer, never re-encodes geometry. Honest limits: only handles the single-buffer
    /// data-URI shape; anything else (external .bin, multiple buffers) returns false without touching the file.
    ///
    /// GLB layout (glTF 2.0 spec): 12-byte header + JSON chunk (0x4E4F534A "JSON") + BIN chunk (0x004E4942 "BIN\0"),
    /// each chunk padded to 4 bytes with 0x20/0x00.
    /// </summary>
    public static class GltfContainerWriter
    {
        public const uint GlbMagic = 0x46546C67; // "glTF"

        /// <summary>Convert a single-buffer data-URI glTF JSON to a .glb. Returns false (no writes) when the
        /// glTF uses external buffers or multiple buffers — the caller keeps the .gltf and can say so honestly.</summary>
        public static bool TryConvertToGlb(string gltfPath, string glbOutPath)
        {
            if (!File.Exists(gltfPath))
                return false;
            try
            {
                string json = File.ReadAllText(gltfPath);
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                if (!root.TryGetProperty("buffers", out JsonElement buffers) || buffers.ValueKind != JsonValueKind.Array)
                    return false;
                if (buffers.GetArrayLength() != 1)
                    return false;

                JsonElement buffer = buffers[0];
                if (!buffer.TryGetProperty("uri", out JsonElement uri) || uri.ValueKind != JsonValueKind.String)
                    return false;
                string uriStr = uri.GetString() ?? "";
                const string prefix = "data:application/octet-stream;base64,";
                if (!uriStr.StartsWith(prefix, StringComparison.Ordinal))
                    return false;

                byte[] bin = Convert.FromBase64String(uriStr[prefix.Length..]);

                byte[] jsonBytes = Encoding.UTF8.GetBytes(StripBufferUri(root));

                uint jsonLen = (uint)jsonBytes.Length;
                uint binLen = (uint)bin.Length;
                uint jsonPadded = Pad4(jsonLen);
                uint binPadded = Pad4(binLen);
                // header(12) + JSON chunk header(8) + BIN chunk header(8) + padded payloads
                uint total = 12 + 8 + jsonPadded + 8 + binPadded;

                using var fs = new FileStream(glbOutPath, FileMode.Create, FileAccess.Write);
                using var bw = new BinaryWriter(fs);
                bw.Write(GlbMagic);
                bw.Write((uint)2);          // glTF version
                bw.Write(total);            // total length
                bw.Write(jsonLen);          // JSON chunk length
                bw.Write(0x4E4F534Au);      // "JSON"
                bw.Write(jsonBytes);
                for (int i = 0; i < jsonPadded - jsonLen; i++) bw.Write((byte)0x20);
                bw.Write(binLen);           // BIN chunk length
                bw.Write(0x004E4942u);      // "BIN\0"
                bw.Write(bin);
                for (int i = 0; i < binPadded - binLen; i++) bw.Write((byte)0x00);
                return true;
            }
            catch
            {
                try { if (File.Exists(glbOutPath)) File.Delete(glbOutPath); } catch { /* best-effort */ }
                return false;
            }
        }

        private static uint Pad4(uint len) => (len + 3u) & ~3u;

        /// <summary>Serializes the glTF root with buffers[0].uri removed (GLB buffers are implicit). Keeps every
        /// other property byte-identical in meaning; uses the same serialization shape as the writer.</summary>
        private static string StripBufferUri(JsonElement root)
        {
            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
            {
                WriteRoot(writer, root);
            }
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        private static void WriteRoot(Utf8JsonWriter w, JsonElement root)
        {
            w.WriteStartObject();
            foreach (JsonProperty prop in root.EnumerateObject())
            {
                if (prop.NameEquals("buffers"))
                {
                    w.WritePropertyName("buffers");
                    w.WriteStartArray();
                    bool first = true;
                    foreach (JsonElement b in prop.Value.EnumerateArray())
                    {
                        w.WriteStartObject();
                        foreach (JsonProperty bp in b.EnumerateObject())
                        {
                            if (first && bp.NameEquals("uri")) continue; // buffer 0 = implicit GLB bin
                            WriteValue(w, bp.Name, bp.Value);
                        }
                        w.WriteEndObject();
                        first = false;
                    }
                    w.WriteEndArray();
                }
                else
                {
                    WriteValue(w, prop.Name, prop.Value);
                }
            }
            w.WriteEndObject();
        }

        private static void WriteValue(Utf8JsonWriter w, string name, JsonElement v)
        {
            w.WritePropertyName(name);
            switch (v.ValueKind)
            {
                case JsonValueKind.Object:
                    w.WriteStartObject();
                    foreach (JsonProperty p in v.EnumerateObject()) WriteValue(w, p.Name, p.Value);
                    w.WriteEndObject();
                    break;
                case JsonValueKind.Array:
                    w.WriteStartArray();
                    foreach (JsonElement e in v.EnumerateArray())
                        switch (e.ValueKind)
                        {
                            case JsonValueKind.Object:
                                w.WriteStartObject();
                                foreach (JsonProperty p in e.EnumerateObject()) WriteValue(w, p.Name, p.Value);
                                w.WriteEndObject();
                                break;
                            default: e.WriteTo(w); break;
                        }
                    w.WriteEndArray();
                    break;
                default:
                    v.WriteTo(w);
                    break;
            }
        }
    }
}

