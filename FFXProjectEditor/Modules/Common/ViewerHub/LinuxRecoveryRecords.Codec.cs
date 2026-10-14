// WHY: Bounded strict JSON must reject ambiguous control data before it reaches recovery decisions.
// MAINT: Raw intent bytes, not reserialized formatting, bind a completion. This code performs no I/O.
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FFXProjectEditor.Modules.Common.ViewerHub;

internal static partial class LinuxRecoveryRecords
{
    private static readonly UTF8Encoding StrictRecordUtf8 = new(false, true);
    private static readonly JsonSerializerOptions CodecOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        Converters = { new JsonStringEnumConverter<OperationKind>(null, allowIntegerValues: false) },
    };

    internal static byte[] Encode(Epoch value) { Validate(value); return EncodeCore(value); }
    internal static byte[] Encode(Intent value) { Validate(value); return EncodeCore(value); }
    internal static byte[] Encode(Completion value) { Validate(value); return EncodeCore(value); }

    internal static Epoch ReadEpoch(string leaf, ReadOnlySpan<byte> bytes)
    {
        Epoch result = Decode<Epoch>(bytes);
        Validate(result);
        Need(leaf == EpochLeaf(result.Ordinal, result.Scope), "Epoch filename disagrees with its fields.");
        return result;
    }

    internal static Intent ReadIntent(string leaf, ReadOnlySpan<byte> bytes)
    {
        Intent result = Decode<Intent>(bytes);
        Validate(result);
        Need(leaf == IntentLeaf(result.Scope, result.Sequence, result.Operation),
            "Intent filename disagrees with its fields.");
        return result;
    }

    internal static Completion ReadCompletion(string leaf, ReadOnlySpan<byte> bytes)
    {
        Completion result = Decode<Completion>(bytes);
        Validate(result);
        Need(leaf == CompletionLeaf(result.Scope, result.Sequence, result.Operation),
            "Completion filename disagrees with its fields.");
        return result;
    }

    internal static void ValidateCompletion(string intentLeaf, ReadOnlySpan<byte> exactIntentBytes,
        Completion completion)
    {
        Intent intent = ReadIntent(intentLeaf, exactIntentBytes);
        Validate(completion);
        Need(completion.Scope == intent.Scope && completion.Sequence == intent.Sequence &&
            completion.Operation == intent.Operation, "Completion belongs to another operation.");
        Need(completion.IntentSha256 == Convert.ToHexString(SHA256.HashData(exactIntentBytes)),
            "Completion is not bound to the exact persisted intent bytes.");
        Need((completion.Second is not null) == (intent.Second is not null),
            "Completion step count differs from intent.");
        MatchStep(intent.First, completion.First);
        if (intent.Second is { } second) MatchStep(second, completion.Second!);
    }

    private static byte[] EncodeCore<T>(T value)
    {
        // Validation bounded every string and the fixed record graph before serialization.
        // No arbitrary arrays, extension-data dictionaries or unbounded text fields are accepted.
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, CodecOptions);
        Need(bytes.Length <= MaximumRecordBytes, "Recovery JSON exceeds its byte limit.");
        return bytes;
    }

    private static T Decode<T>(ReadOnlySpan<byte> bytes) where T : class
    {
        Need(bytes.Length > 0 && bytes.Length <= MaximumRecordBytes, "Invalid recovery JSON size.");
        try
        {
            _ = StrictRecordUtf8.GetCharCount(bytes); // Validate without allocating a decoded string.
            using JsonDocument document = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions
            {
                MaxDepth = 8,
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
            });
            Need(document.RootElement.ValueKind == JsonValueKind.Object, "Recovery JSON must be an object.");
            RejectDuplicateProperties(document.RootElement);
            return document.RootElement.Deserialize<T>(CodecOptions)
                ?? throw new InvalidDataException("Recovery JSON is null.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("Malformed or unsupported recovery JSON.", error);
        }
        catch (DecoderFallbackException error)
        {
            throw new InvalidDataException("Recovery JSON is not valid UTF8.", error);
        }
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                Need(names.Add(property.Name), "Repeated recovery JSON property.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in element.EnumerateArray()) RejectDuplicateProperties(child);
        }
    }
}
