using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Core
{
    public sealed class EditSession
    {
        private readonly WriterAdapterCatalog _catalog;
        private string? _capabilityId;
        private string? _sourcePath;
        private string? _outputPath;
        private string? _displayName;
        private readonly Dictionary<string, object> _edits = new();

        public EditSession(WriterAdapterCatalog catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public EditSession ForCapability(string id)
        {
            _capabilityId = id ?? throw new ArgumentNullException(nameof(id));
            return this;
        }

        public EditSession WithSource(string path)
        {
            _sourcePath = path ?? throw new ArgumentNullException(nameof(path));
            return this;
        }

        public EditSession WithOutput(string path)
        {
            _outputPath = path ?? throw new ArgumentNullException(nameof(path));
            return this;
        }

        public EditSession WithDisplayName(string name)
        {
            _displayName = name;
            return this;
        }

        public EditSession WithEdit(string field, object value)
        {
            if (string.IsNullOrWhiteSpace(field))
                throw new ArgumentException("Field name cannot be null or empty.", nameof(field));
            _edits[field] = value ?? throw new ArgumentNullException(nameof(value));
            return this;
        }

        public EditSession WithEdits(Dictionary<string, object> edits)
        {
            if (edits is null) throw new ArgumentNullException(nameof(edits));
            foreach (var kv in edits)
                _edits[kv.Key] = kv.Value ?? throw new ArgumentNullException($"Value for '{kv.Key}' cannot be null.");
            return this;
        }

        public OperationPlan Build()
        {
            if (string.IsNullOrWhiteSpace(_capabilityId))
                throw new InvalidOperationException("Call ForCapability() before Build().");
            if (string.IsNullOrWhiteSpace(_sourcePath))
                throw new InvalidOperationException("Call WithSource() before Build().");
            if (_edits.Count == 0)
                throw new InvalidOperationException("At least one edit is required. Call WithEdit() or WithEdits().");

            var adapter = _catalog.Get(_capabilityId)
                ?? throw new InvalidOperationException($"No adapter registered for capability '{_capabilityId}'.");

            var errors = adapter.ValidateEdits(_edits);
            if (errors.Count > 0)
                throw new InvalidOperationException(
                    $"Edit validation failed: {string.Join("; ", errors)}");

            var output = _outputPath ?? _sourcePath;
            var root = Path.GetDirectoryName(_sourcePath) ?? ".";
            var ts = DateTimeOffset.UtcNow;
            var opId = Guid.NewGuid().ToString("N");
            var diff = adapter.DescribeChanges(_edits);

            var operation = new FileOperation
            {
                Id = opId,
                Kind = FileOperationKind.Patch,
                SourceRelativePath = _sourcePath,
                OutputRelativePath = output,
                BeforeHash = adapter.ComputeBeforeHash(_sourcePath),
                PredictedAfterHash = "pending-stage",
                EstimatedBytes = 0,
                Description = $"{adapter.DisplayName}: {_capabilityId}",
                Diff = diff,
                Risk = adapter.Risk,
                Edits = new Dictionary<string, object>(_edits)
            };

            return new OperationPlan
            {
                OperationId = opId,
                DisplayName = _displayName ?? $"{adapter.DisplayName} — {Path.GetFileName(_sourcePath)}",
                CreatedAt = ts,
                SourceRoot = root,
                OutputRoot = Path.GetDirectoryName(output) ?? root,
                StagingRoot = Path.Combine(root, ".staging", opId),
                BackupRoot = Path.Combine(root, ".backup", opId),
                Operations = new[] { operation },
                Preconditions = new List<string>(),
                OwnerCapabilityId = _capabilityId
            };
        }
    }
}
