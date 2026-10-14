// WHY: A persistence result reports what was last observed, including uncertain locations, without replay authority.
// MAINT: LastVerified is a past observation, not a promise that another process cannot change that path.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using D = FFXProjectEditor.Modules.Common.ViewerHub.LinuxOwnedOutputDirectory;
using H = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryHistory;
using I = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryInspection;
using R = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryRecords;

namespace FFXProjectEditor.Modules.Common.ViewerHub;

internal static partial class LinuxRecoveryExecutor
{
    internal enum Status { Completed, RefusedBeforeIntent, NeedsInspection, CapacityExceeded,
        SequenceExhausted, CancelledBeforeIntent, RecoveryRequired }
    internal enum LocationRole { Root, Journal, Header, Intent, Completion, Target, Retention,
        ReadDependency, OtherPossibleArtifact }
    internal enum LocationEvidence { NotAttempted, Possible, LastVerified }
    internal sealed record Location(string Path, LocationRole Role, int? Step, LocationEvidence Evidence);
    internal sealed record CompletedOperation(R.Intent Intent, R.Completion Completion);
    internal sealed record Outcome(Status State, string RootPath, string? Detail, H.Status? LastHistoryState,
        ReadOnlyCollection<Location> Locations, CompletedOperation? Receipt,
        bool? ExchangeReportedCompleted = null);

    private sealed class Manifest
    {
        private readonly List<Location> _locations = new();
        internal Manifest(string root)
        {
            Add(root, LocationRole.Root);
            Add(Path.Combine(root, R.JournalLeaf), LocationRole.Journal);
        }
        internal void Add(string path, LocationRole role, int? step = null)
        {
            if (!_locations.Any(item => item.Path == path && item.Role == role && item.Step == step))
                _locations.Add(new(path, role, step, LocationEvidence.NotAttempted));
        }
        internal void Mark(string path, LocationEvidence evidence)
        {
            int index = _locations.FindIndex(item => string.Equals(item.Path, path, StringComparison.Ordinal));
            if (index < 0) _locations.Add(new(path, LocationRole.OtherPossibleArtifact, null, evidence));
            else _locations[index] = _locations[index] with { Evidence = evidence };
        }
        internal ReadOnlyCollection<Location> Snapshot() => Array.AsReadOnly(_locations.ToArray());
    }

    private sealed record LiveStep(string Leaf, D.Snapshot? Expected, byte[] Bytes, R.Step Record);
    private sealed record ReadDependency(string Leaf, D.Snapshot Snapshot);
    private sealed record PreparedOperation(R.Intent Intent, LiveStep[] Steps, ReadDependency? Dependency);
    private sealed record VerifiedStep(D.Snapshot Target, D.Snapshot? Retained)
    {
        internal R.StepReceipt Receipt => new(Artifact(Target), Retained is null ? null : Artifact(Retained));
    }

    // ── State of ONE live attempt ──
    // It is constructed only from fresh native snapshots under the current lease, never read from a journal.
    // Done and control snapshots advance only after their native receipts have been independently re-read.
    private sealed class Attempt
    {
        internal Attempt(D root, D journal, H.Analysis initial, PreparedOperation prepared, Manifest manifest)
        {
            Root = root; Journal = journal; Initial = initial; Prepared = prepared; Locations = manifest;
            LastInspection = initial.Inspection;
            IntentBytes = R.Encode(prepared.Intent);
            IntentLeaf = R.IntentLeaf(prepared.Intent.Scope, prepared.Intent.Sequence, prepared.Intent.Operation);
            CompletionLeaf = R.CompletionLeaf(prepared.Intent.Scope, prepared.Intent.Sequence, prepared.Intent.Operation);
            Done = new VerifiedStep?[prepared.Steps.Length];
            manifest.Add(Path.Combine(journal.FullPath, IntentLeaf), LocationRole.Intent);
            manifest.Add(Path.Combine(journal.FullPath, CompletionLeaf), LocationRole.Completion);
            for (int index = 0; index < prepared.Steps.Length; index++)
            {
                LiveStep step = prepared.Steps[index];
                manifest.Add(Path.Combine(root.FullPath, step.Leaf), LocationRole.Target, index);
                if (step.Record.RetentionLeaf is { } leaf)
                    manifest.Add(Path.Combine(root.FullPath, leaf), LocationRole.Retention, index);
            }
            if (prepared.Dependency is { } dependency)
            {
                string path = Path.Combine(root.FullPath, dependency.Leaf);
                manifest.Add(path, LocationRole.ReadDependency);
                manifest.Mark(path, LocationEvidence.LastVerified);
            }
        }
        internal D Root { get; }
        internal D Journal { get; }
        internal H.Analysis Initial { get; }
        internal PreparedOperation Prepared { get; }
        internal Manifest Locations { get; }
        internal string IntentLeaf { get; }
        internal string CompletionLeaf { get; }
        internal byte[] IntentBytes { get; }
        internal D.Snapshot? IntentSnapshot { get; set; }
        internal D.Snapshot? CompletionSnapshot { get; set; }
        internal VerifiedStep?[] Done { get; }
        internal int CompletedSteps { get; set; }
        internal I.Snapshot LastInspection { get; set; }
    }
}
