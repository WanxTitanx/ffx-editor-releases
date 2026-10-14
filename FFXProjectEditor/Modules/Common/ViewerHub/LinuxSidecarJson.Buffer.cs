// ── Finite private byte sink for stock Utf8JsonWriter ──────────────────────────────────
// External-source credit: the IBufferWriter contract and Utf8JsonWriter growth behavior were
// studied in dotnet/runtime Utf8JsonWriter.cs at commit a83db3e0eb2defb6220e15dae2f1a0462fdbf99f:
// https://github.com/dotnet/runtime/blob/a83db3e0eb2defb6220e15dae2f1a0462fdbf99f/src/libraries/System.Text.Json/src/System/Text/Json/Writer/Utf8JsonWriter.cs
// Adapted 2026-09-06 as a clean-room two-limit sink with checked private-array growth and
// failure-latched export; no BCL implementation body is copied and the BCL serializer is unchanged.
using System;
using System.Buffers;
using System.IO;

namespace FFXProjectEditor.Modules.Common.ViewerHub;

internal static partial class LinuxSidecarJson
{
    internal sealed class BoundedBufferWriter : IBufferWriter<byte>
    {
        private readonly int _outputLimit;
        private readonly int _capacityLimit;
        private byte[] _buffer = Array.Empty<byte>();
        private int _written;
        private int _grantedLength;
        private bool _failed;

        internal BoundedBufferWriter(int outputLimit, int capacityLimit)
        {
            if (outputLimit < 0)
                throw new ArgumentOutOfRangeException(nameof(outputLimit));
            if (capacityLimit < 0 || capacityLimit > Array.MaxLength)
                throw new ArgumentOutOfRangeException(nameof(capacityLimit));
            if (outputLimit > capacityLimit)
                throw new ArgumentOutOfRangeException(nameof(outputLimit),
                    "The logical output limit cannot exceed working capacity.");

            _outputLimit = outputLimit;
            _capacityLimit = capacityLimit;
        }

        internal int WrittenCount => _written;
        internal int CurrentCapacity => _buffer.Length;
        internal long RequestCount { get; private set; }
        internal int LargestSizeHint { get; private set; }
        internal int LargestRequiredCapacity { get; private set; }
        internal int AllocationAttemptCount { get; private set; }
        internal int AllocationCount { get; private set; }
        internal bool IsFailed => _failed;

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            ThrowIfFailed();
            RequestCount++;
            if (sizeHint < 0)
                throw Fail(new ArgumentOutOfRangeException(nameof(sizeHint)));

            LargestSizeHint = Math.Max(LargestSizeHint, sizeHint);
            int required;
            try
            {
                required = checked(_written + Math.Max(1, sizeHint));
            }
            catch (OverflowException error)
            {
                throw Fail(new InvalidDataException("The requested sidecar buffer capacity overflowed.", error));
            }

            LargestRequiredCapacity = Math.Max(LargestRequiredCapacity, required);
            if (required > _capacityLimit)
                throw Fail(new InvalidDataException("The sidecar working-buffer capacity limit was exceeded."));

            try
            {
                EnsureCapacity(required);
            }
            catch
            {
                LatchFailure();
                throw;
            }

            _grantedLength = _buffer.Length - _written;
            return _buffer.AsMemory(_written, _grantedLength);
        }

        public Span<byte> GetSpan(int sizeHint = 0) => GetMemory(sizeHint).Span;

        public void Advance(int count)
        {
            ThrowIfFailed();
            if (count < 0)
                throw Fail(new ArgumentOutOfRangeException(nameof(count)));

            int next;
            try
            {
                next = checked(_written + count);
            }
            catch (OverflowException error)
            {
                throw Fail(new InvalidDataException("The cumulative sidecar byte count overflowed.", error));
            }

            if (count > _grantedLength)
                throw Fail(new InvalidOperationException("Advance exceeded the most recently granted memory."));
            if (next > _outputLimit)
                throw Fail(new InvalidDataException("The sidecar output exceeds the 64 MiB logical limit."));

            _written = next;
            _grantedLength = 0;
        }

        internal void LatchFailure()
        {
            _failed = true;
            _grantedLength = 0;
        }

        internal byte[] ExportExact()
        {
            ThrowIfFailed();
            if (_grantedLength != 0)
                throw Fail(new InvalidOperationException("Cannot export while writable memory is still granted."));
            if (_written > _outputLimit)
                throw Fail(new InvalidDataException("The final sidecar output exceeds its logical limit."));

            try
            {
                var exact = new byte[_written];
                if (_written != 0)
                    Buffer.BlockCopy(_buffer, 0, exact, 0, _written);
                return exact;
            }
            catch
            {
                LatchFailure();
                throw;
            }
        }

        private void EnsureCapacity(int required)
        {
            if (required <= _buffer.Length)
                return;

            int geometric = _buffer.Length == 0
                ? Math.Min(256, _capacityLimit)
                : (int)Math.Min((long)_buffer.Length * 2, _capacityLimit);
            int nextLength = Math.Max(required, geometric);

            AllocationAttemptCount++;
            var next = new byte[nextLength];
            if (_written != 0)
                Buffer.BlockCopy(_buffer, 0, next, 0, _written);
            _buffer = next;
            AllocationCount++;
        }

        private T Fail<T>(T error) where T : Exception
        {
            LatchFailure();
            return error;
        }

        private void ThrowIfFailed()
        {
            if (_failed)
                throw new InvalidOperationException("The sidecar buffer is failed and cannot be reused or exported.");
        }
    }
}
