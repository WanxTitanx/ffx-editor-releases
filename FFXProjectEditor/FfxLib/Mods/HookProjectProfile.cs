using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FFXProjectEditor.FfxLib.Mods;

internal enum HookProjectMode { Vanilla, OnlyMod }

internal sealed record HookCompatibilityProfile
{
    public const string SupportedExecutable = "78ce34397da5e6f49b72c2aebadedaf4cd3f6720e1949d46a1b8ed67d3db5ced";
    public const string SourceRevision = "dda5cb45305448d761e5412f3b265e58213d8ce2";
    public string Contract { get; init; } = "ffx.hooks.2026-09-28";
    public string ExeSha256 { get; init; } = SupportedExecutable;
    public string Machine { get; init; } = "PE32/i386";
    public string Revision { get; init; } = SourceRevision;
    public string[] Capabilities { get; init; } =
    {
        "vanguard.v1", "spira.v1", "monster-rewards.v1", "workshop.plan.v5", "aeon-ascension.v1",
        "ffx.text-locale.api2", "arcana.compiled.v1", "mod007.registry.v1", "mod007.affinity.v1",
        "mod007.context.v1", "mod007.spell-cap.v1", "mod007.tactics.v1", "mod007.gravity.v1", "mod007.equipment.v1"
    };

    public void Require(params string[] capabilities)
    {
        if (Contract != "ffx.hooks.2026-09-28" || Revision != SourceRevision ||
            ExeSha256 != SupportedExecutable || Machine != "PE32/i386")
            throw new InvalidDataException("The target Hook contract or executable profile is incompatible.");
        foreach (string capability in capabilities)
            if (!Capabilities.Contains(capability, StringComparer.Ordinal) || capability == "mod007.presentation.v1")
                throw new InvalidDataException($"Consumer capability unavailable: {capability}.");
    }

    public string InspectExecutable(string path)
    {
        Require();
        byte[] bytes = ModAuthoringFiles.Read(path, 64 * 1024 * 1024);
        if (bytes.Length < 64 || bytes[0] != 'M' || bytes[1] != 'Z') throw new InvalidDataException("Invalid DOS header.");
        int pe = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(60));
        if (pe < 64 || pe > bytes.Length - 56 || !bytes.AsSpan(pe, 4).SequenceEqual(new byte[] { 80, 69, 0, 0 }) ||
            System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(pe + 4)) != 0x14c ||
            System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(pe + 24)) != 0x10b ||
            System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(pe + 52)) != 0x400000 ||
            ModAuthoringFiles.Sha256(bytes) != ExeSha256)
            throw new InvalidDataException("Executable PE identity/SHA-256 does not match the target consumer.");
        return ExeSha256;
    }
}

internal sealed record HookProjectProfile
{
    public int Schema { get; init; } = 1;
    public HookProjectMode Mode { get; init; }
    public HookCompatibilityProfile Consumer { get; init; } = new();
    public Dictionary<string, string> BankLocales { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> AbilityIds { get; init; } = new(StringComparer.Ordinal);

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public void Require(string capability)
    {
        if (Schema != 1 || Mode != HookProjectMode.OnlyMod) throw new InvalidDataException("Select an explicit OnlyMod project profile first.");
        Consumer.Require(capability);
    }

    public byte[] Serialize() => JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions);
    public static HookProjectProfile Parse(byte[] bytes)
    {
        HookJson.RejectDuplicates(bytes);
        var value = JsonSerializer.Deserialize<HookProjectProfile>(bytes, JsonOptions) ?? throw new InvalidDataException("Missing project profile.");
        if (value.Schema != 1 || value.Consumer == null || value.AbilityIds == null || value.BankLocales == null)
            throw new InvalidDataException("Unsupported project profile.");
        return value;
    }

    // A project is an extracted input tree. Metadata lives in the user's Editor settings,
    // so opening a vanilla tree never creates sidecars among original game files.
    public static string ProfilePath(string projectRoot, string metadataRoot) => Path.Combine(metadataRoot,
        ModAuthoringFiles.Sha256(Encoding.UTF8.GetBytes(Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot)))) + ".json");
    public static HookProjectProfile Load(string projectRoot, string metadataRoot)
    {
        string path = ProfilePath(projectRoot, metadataRoot);
        ModAuthoringFiles.RejectLinks(path);
        return File.Exists(path) ? Parse(ModAuthoringFiles.Read(path, 1 << 20)) : new();
    }
    public void Save(string projectRoot, string metadataRoot, string? expectedHash)
    {
        string path = ProfilePath(projectRoot, metadataRoot);
        if (Mode == HookProjectMode.Vanilla && !File.Exists(path)) return;
        ModAuthoringFiles.RejectLinks(path);
        Directory.CreateDirectory(metadataRoot);
        HookExportTransaction.ReplaceFile(path, Serialize(), expectedHash);
    }
}

internal static class HookJson
{
    public static void RejectDuplicates(byte[] bytes)
    {
        using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 48 });
        Visit(json.RootElement);
    }
    private static void Visit(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException($"Duplicate field: {property.Name}.");
                Visit(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var child in element.EnumerateArray()) Visit(child);
    }
}
