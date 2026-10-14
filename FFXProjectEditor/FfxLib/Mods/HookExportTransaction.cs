using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace FFXProjectEditor.FfxLib.Mods;

internal sealed record HookArtifact(string RelativePath, byte[] Bytes, string Kind, string? Locale,
    string? Capability, string Origin, string Action = "create", string? Rollback = null);
internal sealed record HookSourceSnapshot(string Path, string Sha256, long Bytes, string? SourceRoot = null);
internal sealed record HookExportPreview(string Fingerprint, string Report);

internal static class HookExportTransaction
{
    public static string Resolve(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains('\\') || relative.Contains(':') ||
            relative.Split('/').Any(p => p is "" or "." or "..")) throw new InvalidDataException($"Unsafe artifact path: {relative}.");
        string path = Path.Combine(root, relative);
        if (!ModAuthoringFiles.IsWithin(path, root)) throw new InvalidDataException("Artifact escaped destination.");
        ModAuthoringFiles.RejectLinks(path);
        return path;
    }

    public static HookSourceSnapshot Snapshot(string path, int maximum = 32 << 20, string? sourceRoot = null)
    {
        if (sourceRoot != null)
        {
            ModAuthoringFiles.RejectLinks(sourceRoot);
            if (!ModAuthoringFiles.IsWithin(path,sourceRoot)) throw new InvalidDataException("Source file is outside its declared root.");
            sourceRoot = Path.GetFullPath(sourceRoot);
        }
        byte[] bytes = ModAuthoringFiles.Read(path, maximum);
        return new(Path.GetFullPath(path), ModAuthoringFiles.Sha256(bytes), bytes.Length, sourceRoot);
    }

    public static HookExportPreview Preview(HookProjectProfile profile, string destination,
        IReadOnlyList<HookSourceSnapshot> sources, IReadOnlyList<HookArtifact> artifacts)
    {
        if (profile.Mode != HookProjectMode.OnlyMod) throw new InvalidDataException("OnlyMod profile required for Hook package export.");
        profile.Consumer.Require();
        ModAuthoringFiles.RejectLinks(destination);
        destination = Path.GetFullPath(destination);
        ModAuthoringFiles.RejectLinks(destination);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            string directory = source.SourceRoot ?? Path.GetDirectoryName(source.Path)!;
            if (ModAuthoringFiles.IsWithin(destination, directory) || ModAuthoringFiles.IsWithin(source.Path, destination))
                throw new InvalidDataException("Source and destination trees overlap.");
            if (source.Bytes > 64 << 20 || Snapshot(source.Path, 64 << 20, source.SourceRoot) != source) throw new InvalidDataException($"Source changed or exceeds its limit: {source.Path}.");
        }
        foreach (var artifact in artifacts)
        {
            Resolve(destination, artifact.RelativePath);
            if (!paths.Add(artifact.RelativePath) || new[] { "report.md", "output-inventory.json", "source-inventory.json", "checksums.sha256", "hook-project.json", "rollback.json", "preview.json" }.Contains(artifact.RelativePath,StringComparer.OrdinalIgnoreCase) || artifact.RelativePath.StartsWith("backups-of-authored-inputs/",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Duplicate or reserved package path.");
            if (artifact.Capability != null) profile.Require(artifact.Capability);
        }
        var inventory = artifacts.OrderBy(a => a.RelativePath, StringComparer.Ordinal).Select(a => new
        { path = a.RelativePath, bytes = a.Bytes.Length, sha256 = ModAuthoringFiles.Sha256(a.Bytes), kind = a.Kind,
          locale = a.Locale, capability = a.Capability, origin = a.Origin, action = a.Action, rollback = a.Rollback }).ToArray();
        string manifest = JsonSerializer.Serialize(new { profile, destination, sources, inventory }, HookProjectProfile.JsonOptions);
        return new(ModAuthoringFiles.Sha256(Encoding.UTF8.GetBytes(manifest)), manifest);
    }

    public static void Export(HookProjectProfile profile, string destination, IReadOnlyList<HookSourceSnapshot> sources,
        IReadOnlyList<HookArtifact> artifacts, string confirmedFingerprint, CancellationToken cancellationToken = default)
    {
        // Clone mutable byte arrays before validation so a UI edit cannot change a validated output mid-write.
        var frozen = artifacts.Select(a => a with { Bytes = (byte[])a.Bytes.Clone() }).ToArray();
        var preview = Preview(profile, destination, sources, frozen);
        if (preview.Fingerprint != confirmedFingerprint) throw new InvalidDataException("Preview is stale; review the current changes.");
        destination = Path.GetFullPath(destination);
        if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("Choose a new export directory.");
        string parent = Path.GetDirectoryName(destination)!;
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException(parent);
        string stage = Path.Combine(parent, ".hook-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            var files = frozen.ToList();
            var rollback = new List<object>();
            foreach (var source in sources)
            {
                var output = frozen.FirstOrDefault(a => a.Origin == source.Path && a.Kind is "autoability" or "prices" or "bank" or "monster");
                if (output == null) continue;
                string relative = "backups-of-authored-inputs/" + source.Sha256 + "/" + Path.GetFileName(source.Path);
                byte[] original = ModAuthoringFiles.Read(source.Path, 32 << 20);
                if (ModAuthoringFiles.Sha256(original) != source.Sha256) throw new IOException("Rollback source changed after preview.");
                if (files.All(a => a.RelativePath != relative)) files.Add(new(relative,original,"rollback-data",output.Locale,null,source.Path));
                rollback.Add(new { type = output.Kind, output = output.RelativePath, original = relative, sha256 = source.Sha256,
                    limitation = "Restores authored input bytes only. Does not revert installed DLLs, paid transactions or save checkpoints." });
            }
            files.Add(new("hook-project.json", profile.Serialize(), "editor-metadata", null, null, "Editor"));
            files.Add(new("source-inventory.json", JsonSerializer.SerializeToUtf8Bytes(sources, HookProjectProfile.JsonOptions), "inventory", null, null, "Editor"));
            files.Add(new("rollback.json", JsonSerializer.SerializeToUtf8Bytes(rollback,HookProjectProfile.JsonOptions), "rollback-plan",null,null,"Editor"));
            files.Add(new("preview.json", Encoding.UTF8.GetBytes(preview.Report), "confirmed-preview",null,null,"Editor"));
            files.Add(new("report.md", Encoding.UTF8.GetBytes("# Hook export\n\nContract: " + profile.Consumer.Contract + "\nPreview: " + confirmedFingerprint +
                "\n\nAuthoring validation only. Consumer admission and RT2 are recorded separately.\n\n" +
                "Configuration belongs beside the selected INI; assets beside the DLL; data under the overlay root.\n" +
                "Merge the chosen INI entries while preserving unrelated preferences (including audio); this is not a replacement for all user settings.\n" +
                "Rollback configuration/data from the listed originals. Replacing a DLL does not reverse paid save transactions.\n"), "report", null, null, "Editor"));
            var inventory = files.Select(a => new { path = a.RelativePath, bytes = a.Bytes.Length, sha256 = ModAuthoringFiles.Sha256(a.Bytes),
                kind = a.Kind, locale = a.Locale, capability = a.Capability, origin = a.Origin, action = a.Action, rollback = a.Rollback }).ToArray();
            files.Add(new("output-inventory.json", JsonSerializer.SerializeToUtf8Bytes(new { schema=1, files=inventory,
                selfReferences="output-inventory.json and checksums.sha256 are finalized metadata; checksums cover the inventory itself." },HookProjectProfile.JsonOptions),"inventory",null,null,"Editor"));
            string checksums = string.Join("\n", files.Select(a => ModAuthoringFiles.Sha256(a.Bytes) + "  " + a.RelativePath)) + "\n";
            files.Add(new("checksums.sha256", Encoding.UTF8.GetBytes(checksums), "checksums", null, null, "Editor"));
            foreach (var artifact in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string path = Resolve(stage, artifact.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(artifact.Bytes); stream.Flush(true); }
                if (!ModAuthoringFiles.Read(path, artifact.Bytes.Length).SequenceEqual(artifact.Bytes)) throw new IOException("Export readback failed.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (Preview(profile, destination, sources, frozen).Fingerprint != confirmedFingerprint) throw new IOException("Inputs changed during staging.");
            ModAuthoringFiles.RejectLinks(destination);
            Directory.Move(stage, destination);
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }

    public static string? CurrentHash(string path) => File.Exists(path) ? ModAuthoringFiles.Sha256(ModAuthoringFiles.Read(path, 32 << 20)) : null;

    public static void ReplaceFile(string path, byte[] bytes, string? expectedHash)
    {
        ModAuthoringFiles.RejectLinks(path); path = Path.GetFullPath(path);
        if (CurrentHash(path) != expectedHash) throw new IOException("Destination changed externally; reload before saving.");
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes); stream.Flush(true); }
            ModAuthoringFiles.RejectLinks(path);
            if (CurrentHash(path) != expectedHash) throw new IOException("Destination changed while preparing the save.");
            File.Move(temporary, path, expectedHash != null);
            if (CurrentHash(path) != ModAuthoringFiles.Sha256(bytes)) throw new IOException("Saved file changed or failed readback; reload it.");
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
