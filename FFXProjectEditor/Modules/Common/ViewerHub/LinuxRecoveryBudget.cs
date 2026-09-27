// WHY: Admission must reject peak byte/entry overflow before intent or payload publication.
// MAINT: Usage is observed under the caller's root lease. These pure formulas do not verify inventory.
using System;
using System.IO;

namespace FFXProjectEditor.Modules.Common.ViewerHub;

internal static class LinuxRecoveryBudget
{
    internal const long MaximumBytes = 512L * 1024 * 1024;
    internal const int MaximumEntries = 2048;
    internal const int MaximumDirectoryEntries = 8192;
    private const int RecordBytes = LinuxRecoveryRecords.MaximumRecordBytes;

    internal readonly record struct Usage(long HistoryBytes, int ManagedEntries,
        int RootEntries, int JournalEntries);
    internal readonly record struct Reservation(long PeakBytes, int PeakManagedEntries,
        int PeakRootEntries, int PeakJournalEntries);

    internal static Reservation ForOperation(Usage observed, LinuxRecoveryRecords.Intent intent)
    {
        LinuxRecoveryRecords.Validate(intent);
        int steps = intent.Second is null ? 1 : 2;
        long payloadBytes = intent.First.PlannedLength + (long)(intent.Second?.PlannedLength ?? 0);
        long displacedBytes = (intent.First.Expected?.Length ?? 0) + (intent.Second?.Expected?.Length ?? 0);
        // Intent + completion + future ACK headroom. Never remove that headroom from observed H.
        return Reserve(observed, payloadBytes + displacedBytes + 3L * RecordBytes,
            steps + 3, steps, 3);
    }

    internal static Reservation ForAcknowledgement(Usage observed) =>
        Reserve(observed, RecordBytes, 1, 0, 1);

    private static Reservation Reserve(Usage observed, long bytes, int managed, int root, int journal)
    {
        if (observed.HistoryBytes < 0 || observed.ManagedEntries < 0 ||
            observed.RootEntries < 0 || observed.JournalEntries < 0)
            throw new InvalidDataException("Recovery usage cannot be negative.");
        try
        {
            var result = new Reservation(
                checked(observed.HistoryBytes + bytes),
                checked(observed.ManagedEntries + managed),
                checked(observed.RootEntries + root),
                checked(observed.JournalEntries + journal));
            if (result.PeakBytes > MaximumBytes || result.PeakManagedEntries > MaximumEntries ||
                result.PeakRootEntries > MaximumDirectoryEntries ||
                result.PeakJournalEntries > MaximumDirectoryEntries)
                throw new InvalidDataException("Recovery admission exceeds available capacity.");
            return result;
        }
        catch (OverflowException error)
        {
            throw new InvalidDataException("Recovery admission arithmetic overflowed.", error);
        }
    }
}
