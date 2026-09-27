using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.Utils.Editing
{
    public partial class ByteSnapshotEditorSession : ObservableObject, IDisposable
    {
        readonly Func<byte[]> captureSnapshot;
        readonly Action<byte[]> restoreSnapshot;
        readonly Action<byte[]> persistSnapshot;
        readonly string domainLabel;
        readonly DispatcherTimer mutationTimer;
        readonly DispatcherTimer saveIndicatorTimer;
        readonly Stack<byte[]> undoStack = new();

        bool suppressTracking;
        byte[] savedSnapshot;
        byte[] lastObservedSnapshot;
        readonly byte[] pristineSnapshot;

        // When true, Undo and Discard also write the resulting state to disk so the file always matches
        // the screen (Discard restores the original as-loaded state). Used by in-place file editors that
        // expose Save/Undo/Discard. Default false preserves the staging-only behavior other modules rely on.
        public bool RevertWritesToDisk { get; set; }

        [ObservableProperty] private bool autoSaveEnabled = true;
        [ObservableProperty] private bool hasPendingChanges;
        [ObservableProperty] private bool differsFromOriginal;
        [ObservableProperty] private bool canUndo;
        [ObservableProperty] private string sessionSummary;
        [ObservableProperty] private string behaviorSummary;
        [ObservableProperty] private bool isSaveIndicatorVisible;
        [ObservableProperty] private string saveIndicatorText;

        public ByteSnapshotEditorSession(
            Func<byte[]> captureSnapshot,
            Action<byte[]> restoreSnapshot,
            Action<byte[]> persistSnapshot,
            string domainLabel,
            byte[]? initialSnapshot = null)
        {
            this.captureSnapshot = captureSnapshot;
            this.restoreSnapshot = restoreSnapshot;
            this.persistSnapshot = persistSnapshot;
            this.domainLabel = domainLabel;

            savedSnapshot = Clone(initialSnapshot ?? captureSnapshot());
            lastObservedSnapshot = Clone(savedSnapshot);
            pristineSnapshot = Clone(savedSnapshot);
            sessionSummary = $"No pending {domainLabel} changes.";
            behaviorSummary = $"Manual save mode. {domainLabel} edits stay in memory until Save is clicked.";
            saveIndicatorText = "Auto-save";

            mutationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(260)
            };
            mutationTimer.Tick += MutationTimer_Tick;

            saveIndicatorTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1850)
            };
            saveIndicatorTimer.Tick += SaveIndicatorTimer_Tick;

            UpdateState();
        }

        public void NotifyPotentialMutation()
        {
            if (suppressTracking)
                return;

            mutationTimer.Stop();
            mutationTimer.Start();
        }

        public void Save()
        {
            SaveInternal(isAutoSave: false);
        }

        public void Discard()
        {
            mutationTimer.Stop();

            // RevertWritesToDisk editors discard ALL changes back to the original as-loaded state and
            // rewrite the file; staging-only editors just drop pending edits back to the last save.
            byte[] target = RevertWritesToDisk ? pristineSnapshot : savedSnapshot;

            suppressTracking = true;
            try
            {
                restoreSnapshot(Clone(target));
            }
            finally
            {
                suppressTracking = false;
            }

            lastObservedSnapshot = Clone(target);
            undoStack.Clear();

            if (RevertWritesToDisk)
            {
                persistSnapshot(Clone(target));
                savedSnapshot = Clone(target);
                UpdateState($"Discarded all {domainLabel} changes and restored the original on disk.");
                TriggerSaveIndicator("Saved");
                return;
            }

            UpdateState($"Discarded pending {domainLabel} changes.");
        }

        public void Undo()
        {
            mutationTimer.Stop();
            CaptureCurrentStateIfChanged();

            if (undoStack.Count == 0)
            {
                UpdateState($"No {domainLabel} edit left to undo.");
                return;
            }

            byte[] previousSnapshot = undoStack.Pop();

            suppressTracking = true;
            try
            {
                restoreSnapshot(Clone(previousSnapshot));
            }
            finally
            {
                suppressTracking = false;
            }

            lastObservedSnapshot = Clone(previousSnapshot);

            if (AutoSaveEnabled || RevertWritesToDisk)
            {
                persistSnapshot(Clone(previousSnapshot));
                savedSnapshot = Clone(previousSnapshot);
                UpdateState(AutoSaveEnabled
                    ? $"Reverted the last {domainLabel} edit and auto-saved it."
                    : $"Reverted the last {domainLabel} edit and wrote it to disk.");
                TriggerSaveIndicator(AutoSaveEnabled ? "Auto-save" : "Saved");
                return;
            }

            UpdateState($"Reverted the last {domainLabel} edit.");
        }

        public void ReplaceBaseline(byte[] freshSnapshot, string? summaryOverride = null)
        {
            mutationTimer.Stop();
            savedSnapshot = Clone(freshSnapshot);
            lastObservedSnapshot = Clone(freshSnapshot);
            undoStack.Clear();
            UpdateState(summaryOverride ?? $"No pending {domainLabel} changes.");
        }

        void MutationTimer_Tick(object? sender, EventArgs e)
        {
            mutationTimer.Stop();
            CaptureCurrentStateIfChanged();

            if (AutoSaveEnabled && HasPendingChanges)
            {
                SaveInternal(isAutoSave: true);
                return;
            }

            UpdateState();
        }

        void SaveInternal(bool isAutoSave)
        {
            mutationTimer.Stop();
            CaptureCurrentStateIfChanged();

            byte[] snapshotToPersist = Clone(lastObservedSnapshot);
            persistSnapshot(snapshotToPersist);
            savedSnapshot = Clone(snapshotToPersist);

            UpdateState(isAutoSave
                ? $"Auto-saved {domainLabel} changes."
                : $"Saved {domainLabel} changes.");
            TriggerSaveIndicator(isAutoSave ? "Auto-save" : "Saved");
        }

        void CaptureCurrentStateIfChanged()
        {
            byte[] currentSnapshot = Clone(captureSnapshot());
            if (SnapshotsEqual(currentSnapshot, lastObservedSnapshot))
                return;

            undoStack.Push(Clone(lastObservedSnapshot));
            lastObservedSnapshot = currentSnapshot;
        }

        void UpdateState(string? summaryOverride = null)
        {
            HasPendingChanges = !SnapshotsEqual(lastObservedSnapshot, savedSnapshot);
            DiffersFromOriginal = !SnapshotsEqual(lastObservedSnapshot, pristineSnapshot);
            CanUndo = undoStack.Count > 0;

            if (summaryOverride != null)
            {
                SessionSummary = summaryOverride;
            }
            else
            {
                SessionSummary = HasPendingChanges
                    ? $"Unsaved {domainLabel} changes are staged in memory."
                    : $"No pending {domainLabel} changes.";
            }

            BehaviorSummary = AutoSaveEnabled
                ? $"Auto-save mode is armed. After edits settle, Jarvis writes {domainLabel} changes to disk."
                : $"Manual save mode. {domainLabel} edits stay in memory until Save is clicked.";
        }

        partial void OnAutoSaveEnabledChanged(bool value)
        {
            if (value && HasPendingChanges)
            {
                SaveInternal(isAutoSave: true);
                return;
            }

            UpdateState();
        }

        void TriggerSaveIndicator(string label)
        {
            SaveIndicatorText = label;
            IsSaveIndicatorVisible = true;
            saveIndicatorTimer.Stop();
            saveIndicatorTimer.Start();
        }

        void SaveIndicatorTimer_Tick(object? sender, EventArgs e)
        {
            saveIndicatorTimer.Stop();
            IsSaveIndicatorVisible = false;
        }

        static byte[] Clone(byte[] bytes)
        {
            return bytes.ToArray();
        }

        static bool SnapshotsEqual(byte[] left, byte[] right)
        {
            return left.SequenceEqual(right);
        }

        public void Dispose()
        {
            mutationTimer.Stop();
            mutationTimer.Tick -= MutationTimer_Tick;
            saveIndicatorTimer.Stop();
            saveIndicatorTimer.Tick -= SaveIndicatorTimer_Tick;
        }
    }
}
