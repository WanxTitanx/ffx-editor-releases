using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFXProjectEditor.FfxLib.Ps3;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Input;

namespace FFXProjectEditor.Modules.Extras
{
    /// <summary>
    /// MVVM ViewModel for the WD3 Streams tab in the Magic DLL Browser.
    ///
    /// WD3 is a flat container with N overlapping stream views of the same
    /// sprite blob — NOT a sequence of records. This VM exposes each stream
    /// as a bindable item, lets the user select one, and apply a recolor
    /// patch that modifies bytes in-place within the selected stream.
    ///
    /// This replaces the obsolete <c>PrismMagicDllRecolor</c> path (Family A
    /// only) for Thundaga (Family D) — the hierarchical stream awareness is
    /// what fixes the "only the explosion changes" bug.
    /// </summary>
    internal partial class Wd3StreamViewModel : ObservableObject
    {
        const int HexPreviewByteCount = 256;

        /// <summary>
        /// Hardcoded magenta (RGBA: FF 00 FF FF) used by the test recolor command.
        /// Real recolor flows will pass explicit color parameters.
        /// </summary>
        static readonly byte[] MagentaRgba = { 0xFF, 0x00, 0xFF, 0xFF };

        [ObservableProperty]
        private Wd3Blob? _wd3Blob;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectedStream))]
        private int _selectedStreamIndex;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectedStream))]
        [NotifyPropertyChangedFor(nameof(HasStreams))]
        private ObservableCollection<Wd3StreamViewModelItem> _streams = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StatusSeverity))]
        private string _statusText = "Load a magic DLL to parse WD3 streams.";

        [ObservableProperty]
        private string _blobSummary = string.Empty;

        /// <summary>
        /// The currently selected stream item, or null if none selected.
        /// Computed from <see cref="SelectedStreamIndex"/> and <see cref="Streams"/>.
        /// </summary>
        public Wd3StreamViewModelItem? SelectedStream =>
            Streams != null && Streams.Count > 0
                && SelectedStreamIndex >= 0 && SelectedStreamIndex < Streams.Count
                ? Streams[SelectedStreamIndex]
                : null;

        public bool HasStreams => Streams != null && Streams.Count > 0;

        /// <summary>
        /// Semantic severity derived from <see cref="StatusText"/> for header coloring.
        /// Mirrors the pattern in <see cref="MagicDllBrowser_DataModel.StatusSeverity"/>.
        /// </summary>
        public string StatusSeverity => ClassifyStatusSeverity(StatusText);

        /// <summary>
        /// Command wired to <see cref="ApplyRecolor"/> with the selected stream
        /// and a hardcoded magenta color for testing.
        /// </summary>
        public ICommand ApplyRecolorCommand { get; }

        public Wd3StreamViewModel()
        {
            ApplyRecolorCommand = new RelayCommand(ApplyRecolorExecute);
        }

        // Track the data section offset from the last LoadFromDll call for BlobSummary.
        int _lastDataSectionOffset;

        partial void OnStatusTextChanged(string value) =>
            OnPropertyChanged(nameof(StatusSeverity));

        /// <summary>
        /// Load WD3 streams from raw DLL bytes by calling <see cref="Wd3StreamParser.Parse"/>.
        /// Populates <see cref="Streams"/> and <see cref="Wd3Blob"/>.
        /// </summary>
        /// <param name="dllBytes">Full DLL file bytes.</param>
        /// <param name="dataSectionOffset">File offset of the .data section.</param>
        public void LoadFromDll(byte[] dllBytes, int dataSectionOffset)
        {
            if (dllBytes == null || dllBytes.Length == 0)
            {
                StatusText = "WD3 load failed: DLL bytes are empty.";
                ClearStreams();
                return;
            }

            _lastDataSectionOffset = dataSectionOffset;

            try
            {
                Wd3Blob blob = Wd3StreamParser.Parse(dllBytes, dataSectionOffset);
                Wd3Blob = blob;
                PopulateStreams(blob);
                SelectedStreamIndex = Streams.Count > 0 ? 0 : -1;
                BlobSummary = $"WD3 {blob.Header.Magic} · {blob.Header.StreamCount} streams · blob @ 0x{dataSectionOffset:X} · size 0x{blob.Header.TotalSize:X}";
                StatusText = $"Parsed {blob.Header.StreamCount} WD3 streams from blob at 0x{dataSectionOffset:X}.";
            }
            catch (Wd3ParseException ex)
            {
                StatusText = $"WD3 parse failed: {ex.Message}";
                ClearStreams();
            }
            catch (Exception ex)
            {
                StatusText = $"WD3 load error: {ex.Message}";
                ClearStreams();
            }
        }

        /// <summary>
        /// Apply a recolor patch to a specific stream at a byte offset.
        /// Modifies the stream data in-place and raises property changed so
        /// the UI refreshes the hex preview.
        /// </summary>
        /// <param name="streamIdx">Index into <see cref="Streams"/>.</param>
        /// <param name="byteOffset">Offset within the stream's data array.</param>
        /// <param name="newBytes">Replacement bytes to write.</param>
        public void ApplyRecolor(int streamIdx, int byteOffset, byte[] newBytes)
        {
            if (Streams == null || streamIdx < 0 || streamIdx >= Streams.Count)
            {
                StatusText = $"Recolor skipped: stream index {streamIdx} out of range.";
                return;
            }

            Wd3StreamViewModelItem item = Streams[streamIdx];
            if (item.Data == null || item.Data.Length == 0)
            {
                StatusText = $"Recolor skipped: stream {streamIdx} has no data.";
                return;
            }

            if (byteOffset < 0 || byteOffset + newBytes.Length > item.Data.Length)
            {
                StatusText = $"Recolor skipped: byte offset {byteOffset}+{newBytes.Length} exceeds stream data length {item.Data.Length}.";
                return;
            }

            // Modify the stream data in-place
            Buffer.BlockCopy(newBytes, 0, item.Data, byteOffset, newBytes.Length);

            // Sync back to the underlying Wd3Blob stream (same array reference, but
            // raise the preview refresh explicitly)
            item.DataPreview = BuildHexPreview(item.Data);

            StatusText = $"Recolor applied: stream {streamIdx} @ 0x{byteOffset:X}, {newBytes.Length} bytes written.";
        }

        void PopulateStreams(Wd3Blob blob)
        {
            var items = new Wd3StreamViewModelItem[blob.Streams.Length];
            for (int i = 0; i < blob.Streams.Length; i++)
            {
                Wd3Stream s = blob.Streams[i];
                items[i] = new Wd3StreamViewModelItem
                {
                    StartOffset = (int)s.Header.StartOffset,
                    EndOffset = (int)s.Header.EndOffset,
                    Scale = s.Header.Scale,
                    PackedData = $"0x{s.Header.PackedData:X8}",
                    Data = s.Data,
                    DataPreview = BuildHexPreview(s.Data)
                };
            }
            Replace(Streams, items);
        }

        internal void ClearStreams()
        {
            Wd3Blob = null;
            Streams.Clear();
            BlobSummary = string.Empty;
            OnPropertyChanged(nameof(HasStreams));
            OnPropertyChanged(nameof(SelectedStream));
        }

        /// <summary>
        /// Command execute handler: applies hardcoded magenta to the selected stream
        /// at offset 0 for testing. Real recolor flows will call <see cref="ApplyRecolor"/>
        /// directly with explicit parameters.
        /// </summary>
        void ApplyRecolorExecute()
        {
            if (SelectedStream == null)
            {
                StatusText = "Select a stream before applying recolor.";
                return;
            }
            ApplyRecolor(SelectedStreamIndex, 0, MagentaRgba);
        }

        static string BuildHexPreview(byte[]? data)
        {
            if (data == null || data.Length == 0)
                return "(empty)";
            int len = Math.Min(data.Length, HexPreviewByteCount);
            var sb = new StringBuilder(len * 3);
            for (int i = 0; i < len; i++)
            {
                if (i > 0)
                    sb.Append(' ');
                sb.Append(data[i].ToString("X2"));
            }
            if (data.Length > HexPreviewByteCount)
                sb.Append(" …");
            return sb.ToString();
        }

        static string ClassifyStatusSeverity(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return "None";
            if (status.Contains("failed", StringComparison.OrdinalIgnoreCase)
                || status.Contains("not found", StringComparison.OrdinalIgnoreCase)
                || status.Contains("error", StringComparison.OrdinalIgnoreCase))
                return "Danger";
            if (status.Contains("skipped", StringComparison.OrdinalIgnoreCase))
                return "Warning";
            return "Info";
        }

        static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
        {
            target.Clear();
            foreach (T item in source)
                target.Add(item);
        }
    }

    /// <summary>
    /// Bindable item representing a single WD3 stream view.
    /// </summary>
    public sealed partial class Wd3StreamViewModelItem : ObservableObject
    {
        public int StartOffset { get; init; }
        public int EndOffset { get; init; }
        public float Scale { get; init; }
        public string PackedData { get; init; } = string.Empty;

        [ObservableProperty]
        private byte[]? _data;

        [ObservableProperty]
        private string _dataPreview = string.Empty;

        public int DataLength => Data?.Length ?? 0;
        public string RangeSummary => $"0x{StartOffset:X}..0x{EndOffset:X} ({DataLength} bytes, scale {Scale:F2})";

        partial void OnDataChanged(byte[]? value)
        {
            OnPropertyChanged(nameof(DataLength));
            OnPropertyChanged(nameof(RangeSummary));
        }
    }
}
