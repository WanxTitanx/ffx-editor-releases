// ── Bounded Linux sidecar JSON preparation ─────────────────────────────────────────────
// WHY: The future native persistence coordinator needs final immutable bytes before it may
// publish an intent. This helper transforms only an already bounded, stable snapshot in memory.
// MAINT: Preserve the frozen SidecarEditsWriter ApplySlot semantics until Windows ownership
// permits a separately reviewed shared extraction. C bounds only this helper's byte buffer.
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FFXProjectEditor.Diagnostics;

namespace FFXProjectEditor.Modules.Common.ViewerHub;

internal static partial class LinuxSidecarJson
{
    internal const int LogicalByteLimit = 64 * 1024 * 1024;
    internal const int WorkingCapacityLimit = checked(3 * LogicalByteLimit + 4096);

    internal static byte[] Prepare(
        byte[]? existingSnapshotBytes,
        int slot,
        double? dx,
        double? dy,
        double? dz,
        double? heading,
        double? scale)
    {
        BoundedBufferWriter? sink = null;
        Utf8JsonWriter? writer = null;
        try
        {
            if (existingSnapshotBytes is { Length: > LogicalByteLimit })
                throw new InvalidDataException("The existing sidecar snapshot exceeds the 64 MiB input limit.");

            string existingJson = existingSnapshotBytes is null
                ? "{}"
                : DecodeLikeExistingStore(existingSnapshotBytes);

            JsonObject root = JsonNode.Parse(existingJson) as JsonObject ?? new JsonObject();
            JsonObject actors = root["actors"] as JsonObject ?? new JsonObject();
            string slotKey = slot.ToString(CultureInfo.InvariantCulture);
            JsonObject selectedSlot = actors[slotKey] as JsonObject ?? new JsonObject();

            if (dx.HasValue && dy.HasValue && dz.HasValue)
                selectedSlot["position"] = new JsonArray(dx.Value, dy.Value, dz.Value);
            if (heading.HasValue)
                selectedSlot["heading"] = heading.Value;
            if (scale.HasValue)
                selectedSlot["scale"] = scale.Value;

            actors[slotKey] = selectedSlot;
            root["actors"] = actors;

            sink = new BoundedBufferWriter(LogicalByteLimit, WorkingCapacityLimit);
            var serializerOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                MaxDepth = 64
            };
            writer = new Utf8JsonWriter(sink, new JsonWriterOptions
            {
                Indented = true,
                MaxDepth = 64
            });
            root.WriteTo(writer, serializerOptions);
            writer.Flush();
            writer.Dispose();
            writer = null;

            byte[] result = sink.ExportExact();
            try
            {
                DebugLog.Info("ViewerHub.SidecarJson",
                    $"Prepared {result.Length} UTF-8 bytes from a bounded in-memory sidecar snapshot.");
            }
            catch { /* Diagnostics must not turn a verified payload into a failed preparation. */ }
            return result;
        }
        catch (Exception error)
        {
            sink?.LatchFailure();
            if (writer is not null)
            {
                try
                {
                    writer.Reset();
                }
                catch (Exception cleanupError)
                {
                    try
                    {
                        DebugLog.Warn("ViewerHub.SidecarJson",
                            $"Writer reset after a failed sidecar serialization also failed: {cleanupError.Message}");
                    }
                    catch { /* Formatting/logging failure must not replace the original exception. */ }
                }

                try
                {
                    writer.Dispose();
                }
                catch (Exception cleanupError)
                {
                    try
                    {
                        DebugLog.Warn("ViewerHub.SidecarJson",
                            $"Writer disposal after a failed sidecar serialization also failed: {cleanupError.Message}");
                    }
                    catch { /* Formatting/logging failure must not replace the original exception. */ }
                }
            }

            try
            {
                DebugLog.Error("ViewerHub.SidecarJson",
                    "Bounded sidecar JSON preparation failed; no payload was accepted.", error);
            }
            catch { /* Keep the primary exception and stack even if diagnostic allocation fails. */ }
            throw;
        }
    }

    private static string DecodeLikeExistingStore(byte[] existingSnapshotBytes)
    {
        using var stream = new MemoryStream(existingSnapshotBytes, writable: false);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 16 * 1024,
            leaveOpen: true);
        return reader.ReadToEnd();
    }
}
