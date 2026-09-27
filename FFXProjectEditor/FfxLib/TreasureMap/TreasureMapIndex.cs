using FFXProjectEditor.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.TreasureMap;

// ── TreasureMapIndex / TreasureMapIndexBuilder ─────────────────────────────────────────
// Builds the in-memory treasure model of the whole game master folder:
//   takara.bin catalog (record count) + all field assets (MAP1 + event paths) +
//   per-event ATEL scans (treasure candidates found by obtainTreasure).
//
// ConfirmedChestCandidates is RE-validated (2026-08-06): a treasure chest is proven by
// obtainTreasure/obtainTreasureSilently (ATEL Common 0x015B/0x01A7), NOT by a speculative
// model id. The visual chest model is engine-side (treasure slot table @0x1130F2F) and its
// id is dynamic (0x5000 + slot from FFX_Field_RegisterModelRecord@0x7AB930).
// ──────────────────────────────────────────────────────────────────────────────────────

public sealed record TreasureScanFailure(string Path, string Message);

public sealed record TreasureMapIndex(
    TreasureCatalog Catalog,
    IReadOnlyList<FieldMapAsset> Fields,
    IReadOnlyList<EventTreasureScanResult> EventScans,
    IReadOnlyList<TreasureScanFailure> Failures)
{
    public IReadOnlyList<EventTreasureCandidate> Candidates => EventScans.SelectMany(s => s.Candidates).ToArray();
    // RE-validated (2026-08-06): a treasure chest is proven by obtainTreasure/obtainTreasureSilently
    // (ATEL Common 0x015B/0x01A7 -> FFX_Atel_Common_obtainTreasure@0x85A740, decompiled: LoadTakaraRow +
    // Inventory_AddItem/SpendGil/RegisterModelRecord). The visual chest model is engine-side (treasure slot
    // table @0x1130F2F, 22-byte records with opened/closed flags) — per-field model ids like 0x5002/0x50AA
    // were speculative and are NOT used as confirmation criteria.
    public IReadOnlyList<EventTreasureCandidate> ConfirmedChestCandidates =>
        Candidates.Where(c => c.TreasureIds.Count > 0).ToArray();
}

public static class TreasureMapIndexBuilder
{
    public static TreasureMapIndex BuildField(TreasureCatalog catalog, FieldMapAsset field)
    {
        var scans = new List<EventTreasureScanResult>();
        var failures = new List<TreasureScanFailure>();
        try { _ = Map1Header.Read(field.MapPath); }
        catch (Exception ex) { DebugLog.Warn("TreasureMap.Load", $"MAP1 header failed for {field.FieldId}: {ex.Message}"); failures.Add(new TreasureScanFailure(field.MapPath, ex.Message)); }
        foreach (string ep in field.EventPaths)
        {
            try { scans.Add(EventTreasureScanner.Scan(ep)); }
            catch (Exception ex) { DebugLog.Warn("TreasureMap.Load", $"Scan failed for {Path.GetFileName(ep)}: {ex.Message}"); failures.Add(new TreasureScanFailure(ep, ex.Message)); }
        }
        return new TreasureMapIndex(catalog, [field], scans, failures);
    }

    public static TreasureMapIndex Build(string masterDirectory, Action<string>? progress = null)
    {
        DebugLog.Info("TreasureMap.Load", "Building index from " + masterDirectory);
        string master = System.IO.Path.GetFullPath(masterDirectory);
        if (!Directory.Exists(master)) throw new DirectoryNotFoundException($"FFX master directory not found: {master}");
        TreasureMapPrerequisiteResult pre = TreasureMapPrerequisites.Validate(master);
        if (!pre.IsValid) { DebugLog.Warn("TreasureMap.Load", "Prerequisites missing: " + string.Join("; ", pre.MissingPaths)); throw new InvalidDataException(pre.Message); }
        string treasurePath = Path.Combine(master, "jppc", "battle", "kernel", "takara.bin");
        TreasureCatalog catalog = TreasureCatalog.Read(treasurePath);
        IReadOnlyList<FieldMapAsset> fields = FieldAssetDiscovery.ScanMaster(master);
        DebugLog.Info("TreasureMap.Load", $"Catalog: {catalog.Records.Count} records. Fields: {fields.Count}.");
        var scans = new List<EventTreasureScanResult>();
        var failures = new List<TreasureScanFailure>();

        for (int i = 0; i < fields.Count; i++)
        {
            FieldMapAsset field = fields[i];
            if (i % 20 == 0) progress?.Invoke($"Scanning field events… {i:N0} of {fields.Count:N0}");
            try { _ = Map1Header.Read(field.MapPath); }
            catch (Exception ex) { DebugLog.Warn("TreasureMap.Load", $"MAP1 header failed for {field.FieldId}: {ex.Message}"); failures.Add(new TreasureScanFailure(field.MapPath, ex.Message)); }
            foreach (string ep in field.EventPaths)
            {
                try { scans.Add(EventTreasureScanner.Scan(ep)); }
                catch (Exception ex) { DebugLog.Warn("TreasureMap.Load", $"Scan failed for {Path.GetFileName(ep)}: {ex.Message}"); failures.Add(new TreasureScanFailure(ep, ex.Message)); }
            }
        }
        var result = new TreasureMapIndex(catalog, fields, scans, failures);
        DebugLog.Info("TreasureMap.Load", $"Index done: {scans.Count} scans, {failures.Count} failures, {result.ConfirmedChestCandidates.Count} chest candidates.");
        progress?.Invoke("Building projected chest locations…");
        return result;
    }
}