// WHY: A single live coordinator owns root lease, journal, backup-first ordering and uncertain locations.
// MAINT: No parsed Intent executes here; no retry, rollback, unlink, automatic ACK, game write or UI callback.
// Completed is returned only after native receipts, history, inventory and CURRENT public files all agree.
using System;
using System.IO;
using System.Linq;
using System.Threading;
using FFXProjectEditor.Diagnostics;
using B = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryBaseline;
using D = FFXProjectEditor.Modules.Common.ViewerHub.LinuxOwnedOutputDirectory;
using H = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryHistory;
using I = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryInspection;
using R = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryRecords;

namespace FFXProjectEditor.Modules.Common.ViewerHub;

internal static partial class LinuxRecoveryExecutor
{
    private static readonly TimeSpan LeaseTimeout = TimeSpan.FromSeconds(5);
    // Test-only checkpoints share the established serialized filesystem-hook collection.
    // Neither hook is a command argument or product extension point; both are null in product use.
    internal static Guid? OperationIdForTests { get; set; }
    internal static Action<string, string>? BeforePhaseForTests { get; set; }

    internal static Outcome Execute(Command command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var locations = new Manifest(command.RootPath);
        bool intentPossible = false;
        H.Status? historyState = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = CheckRootPath(command.RootPath);
            if (command.Kind != R.OperationKind.SidecarEdit)
                _ = MagicTarget(Path.Combine(command.RootPath, command.TargetLeaf), command.SourcePath, command.GameRoot);
            locations.Mark(command.RootPath, LocationEvidence.Possible);
            using D root = command.Kind == R.OperationKind.MagicRestore
                ? D.OpenExisting(command.RootPath) : D.OpenOrCreate(command.RootPath);
            locations.Mark(root.FullPath, LocationEvidence.LastVerified);
            using var lease = root.AcquireWriteLock(LeaseTimeout, cancellationToken);
            H.Analysis analysis = H.Read(root);
            historyState = analysis.State;
            ObserveHistoryLocations(analysis, locations);
            if (analysis.State == H.Status.EmptyNeedsGenesis)
            {
                cancellationToken.ThrowIfCancellationRequested();
                locations.Mark(Path.Combine(root.FullPath, R.JournalLeaf), LocationEvidence.Possible);
                B.Outcome genesis = B.EnsureGenesis(root); // Borrow the already-locked root; never reacquire it.
                if (genesis.HeaderPath is { } header)
                {
                    locations.Add(header, LocationRole.Header);
                    locations.Mark(header, genesis.State == B.Status.GenesisCreated
                        ? LocationEvidence.LastVerified : LocationEvidence.Possible);
                }
                if (genesis.State is not (B.Status.GenesisCreated or B.Status.AlreadyInitialized))
                {
                    Status state = genesis.State == B.Status.RecoveryRequired ? Status.RecoveryRequired :
                        genesis.State == B.Status.CapacityExceeded ? Status.CapacityExceeded : Status.NeedsInspection;
                    return Result(state, genesis.Detail ?? "The output root needs fresh baseline inspection.");
                }
                analysis = H.Read(root); // A created/existing header is not a reusable Healthy assertion.
                historyState = analysis.State;
                ObserveHistoryLocations(analysis, locations);
            }
            if (analysis.State != H.Status.Healthy)
                return Result(analysis.State == H.Status.HistoryOverBudget ? Status.CapacityExceeded : Status.NeedsInspection,
                    "Recovery history requires inspection before a new write.");
            if (analysis.NextSequence is not { } sequence || sequence == ulong.MaxValue)
                return Result(Status.SequenceExhausted, "Recovery sequence cannot advance safely.");

            using var journal = root.OpenExistingChild(R.JournalLeaf);
            if (!H.SameObservation(analysis.Inspection, I.Capture(root)))
                throw new IOException("Recovery inventory changed after acquiring the journal.");
            Guid operation = OperationIdForTests ?? Guid.NewGuid();
            CheckOperationId(analysis.Inspection, operation);
            cancellationToken.ThrowIfCancellationRequested();
            PreparedOperation prepared = Prepare(command, root, analysis, operation);
            RequireCapacity(analysis.Inspection.Usage, prepared.Intent);
            var attempt = new Attempt(root, journal, analysis, prepared, locations);
            Probe("before-intent", root);
            cancellationToken.ThrowIfCancellationRequested();
            VerifyPrefix(attempt, rootMayChange: false, journalMayChange: false);

            D.Publication intentReceipt = journal.PublishNew(attempt.IntentLeaf, attempt.IntentBytes);
            intentPossible = true; // Immediately, before ANY following callback/read/verification can fail.
            locations.Mark(intentReceipt.FullPath, LocationEvidence.Possible);
            attempt.IntentSnapshot = VerifyPublication(journal, attempt.IntentLeaf, attempt.IntentBytes,
                intentReceipt, requirePrivate: true);
            if (R.ReadIntent(attempt.IntentLeaf, attempt.IntentSnapshot.Bytes) != prepared.Intent)
                throw new IOException("Persisted intent differs from the admitted live operation.");
            locations.Mark(intentReceipt.FullPath, LocationEvidence.LastVerified);
            VerifyPrefix(attempt, rootMayChange: false, journalMayChange: true);
            Probe("intent-verified", root);

            for (int index = 0; index < prepared.Steps.Length; index++)
            {
                Probe("before-step-" + index, root);
                cancellationToken.ThrowIfCancellationRequested();
                VerifyPrefix(attempt, rootMayChange: false, journalMayChange: false);
                LiveStep step = prepared.Steps[index];
                D.Publication targetReceipt;
                D.Publication? retainedReceipt = null;
                if (step.Expected is { } original)
                {
                    D.Replacement replacement = root.ReplaceRetainingDisplaced(step.Leaf, original,
                        step.Record.RetentionLeaf!, step.Bytes);
                    targetReceipt = replacement.Target;
                    retainedReceipt = replacement.Retained;
                    locations.Mark(replacement.Retained.FullPath, LocationEvidence.Possible);
                }
                else targetReceipt = root.PublishNew(step.Leaf, step.Bytes);
                locations.Mark(targetReceipt.FullPath, LocationEvidence.Possible);
                D.Snapshot target = VerifyPublication(root, step.Leaf, step.Bytes, targetReceipt, requirePrivate: true);
                D.Snapshot? retained = null;
                if (retainedReceipt.HasValue)
                {
                    retained = VerifyPublication(root, step.Record.RetentionLeaf!, step.Expected!.Bytes,
                        retainedReceipt.Value, requirePrivate: false);
                    VerifyDisplaced(step.Expected!, retained);
                    locations.Mark(retainedReceipt.Value.FullPath, LocationEvidence.LastVerified);
                }
                attempt.Done[index] = new(target, retained);
                attempt.CompletedSteps = index + 1;
                locations.Mark(targetReceipt.FullPath, LocationEvidence.LastVerified);
                VerifyPrefix(attempt, rootMayChange: true, journalMayChange: false);
                Probe("step-" + index + "-verified", root);
            }

            Probe("before-completion", root);
            cancellationToken.ThrowIfCancellationRequested();
            VerifyPrefix(attempt, rootMayChange: false, journalMayChange: false);
            var completion = new R.Completion(1, prepared.Intent.Scope, sequence, operation,
                Sha(attempt.IntentSnapshot.Bytes), attempt.Done[0]!.Receipt,
                attempt.Done.Length == 2 ? attempt.Done[1]!.Receipt : null);
            R.ValidateCompletion(attempt.IntentLeaf, attempt.IntentSnapshot.Bytes, completion);
            byte[] completionBytes = R.Encode(completion);
            D.Publication completionReceipt = journal.PublishNew(attempt.CompletionLeaf, completionBytes);
            locations.Mark(completionReceipt.FullPath, LocationEvidence.Possible);
            attempt.CompletionSnapshot = VerifyPublication(journal, attempt.CompletionLeaf, completionBytes,
                completionReceipt, requirePrivate: true);
            if (R.ReadCompletion(attempt.CompletionLeaf, attempt.CompletionSnapshot.Bytes) != completion)
                throw new IOException("Persisted completion differs from the verified live receipts.");
            locations.Mark(completionReceipt.FullPath, LocationEvidence.LastVerified);
            VerifyPrefix(attempt, rootMayChange: false, journalMayChange: true);

            H.Analysis reopened = H.Read(root);
            historyState = reopened.State;
            if (reopened.State != H.Status.Healthy || reopened.NextSequence != checked(sequence + 1) ||
                reopened.CompletedOperations != checked(analysis.CompletedOperations + 1) ||
                reopened.Inspection.HeadSelection != analysis.Inspection.HeadSelection ||
                !H.SameObservation(attempt.LastInspection, reopened.Inspection))
                throw new IOException("Completed operation did not reopen with the verified history.");
            Probe("history-reopened", root);
            cancellationToken.ThrowIfCancellationRequested();
            // History deliberately ignores old public versions; do not let it replace this LIVE final gate.
            VerifyPrefix(attempt, rootMayChange: false, journalMayChange: false);
            try
            {
                DebugLog.Info("LinuxRecovery.Executor", "Verified one live persistence operation and its current outputs.");
            }
            catch { /* Diagnostics must not reclassify a verified operation. */ }
            return new(Status.Completed, command.RootPath, null, H.Status.Healthy,
                locations.Snapshot(), new(prepared.Intent, completion));
        }
        catch (LinuxReplacementUncertainException error)
        {
            locations.Mark(error.TargetPath, LocationEvidence.Possible);
            locations.Mark(error.RetentionPath, LocationEvidence.Possible);
            return Uncertain(error, error.ExchangeCompleted);
        }
        catch (LinuxPublicationUncertainException error)
        {
            locations.Mark(error.PossiblyPublishedPath, LocationEvidence.Possible);
            return Uncertain(error);
        }
        catch (Exception error) when (intentPossible)
        {
            return Uncertain(error);
        }
        catch (OperationCanceledException error)
        {
            return Result(Status.CancelledBeforeIntent, error.Message);
        }
        catch (CapacityRefusalException error)
        {
            return Result(Status.CapacityExceeded, error.Message);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            InvalidDataException or System.Text.Json.JsonException or InvalidOperationException or
            ArgumentException or NotSupportedException or TimeoutException)
        {
            try
            {
                DebugLog.Warn("LinuxRecovery.Executor", "Refused before intent: " + error.Message);
            }
            catch { /* Formatting or logging must not replace the typed refusal. */ }
            return Result(Status.RefusedBeforeIntent, error.Message);
        }

        Outcome Result(Status state, string? detail) =>
            new(state, command.RootPath, detail, historyState, locations.Snapshot(), null);
        Outcome Uncertain(Exception error, bool? exchangeCompleted = null)
        {
            try
            {
                DebugLog.Error("LinuxRecovery.Executor", "Persistence is uncertain; all named evidence was preserved.", error);
            }
            catch { /* Preserve recovery reporting even if diagnostic formatting fails. */ }
            return new(Status.RecoveryRequired, command.RootPath, error.Message, historyState,
                locations.Snapshot(), null, exchangeCompleted);
        }
    }

    private static void ObserveHistoryLocations(H.Analysis value, Manifest locations)
    {
        string journal = Path.Combine(value.Inspection.RootPath, R.JournalLeaf);
        if (value.Inspection.Journal is not null) locations.Mark(journal, LocationEvidence.LastVerified);
        if (value.Inspection.HeadSelection.Head is { } head)
        {
            string path = Path.Combine(journal, head.Leaf);
            locations.Add(path, LocationRole.Header);
            locations.Mark(path, LocationEvidence.LastVerified);
        }
    }

    private static void CheckOperationId(I.Snapshot value, Guid operation)
    {
        if (operation == Guid.Empty) throw new ArgumentException("A live operation needs a nonempty id.");
        string scope = value.HeadSelection.Head!.Epoch.Scope.ToString("N");
        string id = operation.ToString("N");
        if (value.Entries.Any(entry => entry.Space == I.EntrySpace.Journal
            ? entry.Leaf.StartsWith(scope + "-", StringComparison.OrdinalIgnoreCase) &&
              entry.Leaf.Contains("-" + id + ".", StringComparison.OrdinalIgnoreCase)
            : entry.Leaf.StartsWith(R.RetentionPrefix + scope + "-" + id + "-", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The proposed live operation id already occurs in the active history.");
    }

    private static void Probe(string phase, D root) => BeforePhaseForTests?.Invoke(phase, root.FullPath);
}
