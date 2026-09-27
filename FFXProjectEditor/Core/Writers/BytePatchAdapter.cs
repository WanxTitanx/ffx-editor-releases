using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

using FFXProjectEditor.Core.LLM;

namespace FFXProjectEditor.Core.Writers
{
    /// <summary>
    /// Generic byte-splice adapter for the profile-1 ByteReplace contract.
    /// Consumes the edit shape ProposalToPlanMapper.BuildEdits emits:
    ///   "offset"          (long)   absolute byte offset in the source file
    ///   "newBytesBase64"  (string) replacement payload (base64)
    ///   "beforeHash"      (string) SHA-256 of the current file (informational;
    ///                              the executor re-verifies the hash itself)
    /// The adapter reads the source, splices the payload at the offset and writes
    /// the staging file — the ONLY writer that turns a PatchProposal into bytes.
    /// Safety lives upstream (LlmGuard: proven recipe + path guard + hash match +
    /// size cap + required verification) and downstream (OperationExecutorV2:
    /// stage → verify → atomic promote + backup).
    /// </summary>
    public sealed class BytePatchAdapter : IWriterAdapter
    {
        public string CapabilityId => "byte-patch";
        public string DisplayName => "Byte Patch";
        public RiskLevel Risk => RiskLevel.High; // raw byte splice — human diff review is the gate

        public string ComputeBeforeHash(string sourcePath)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(sourcePath);
            return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        }

        public async Task<string> StageAsync(
            string sourcePath,
            string stagingPath,
            IReadOnlyDictionary<string, object> edits,
            CancellationToken ct = default)
        {
            long offset = GetLong(edits, ProposalToPlanMapper.EditKeyOffset);
            byte[] payload = GetPayload(edits);

            byte[] original = await File.ReadAllBytesAsync(sourcePath, ct);
            if (offset < 0 || offset + payload.Length > original.Length)
                throw new InvalidDataException(
                    $"byte-patch out of range: offset {offset} + {payload.Length} B > file {original.Length} B");

            byte[] staged = (byte[])original.Clone();
            Array.Copy(payload, 0, staged, offset, payload.Length);

            Directory.CreateDirectory(Path.GetDirectoryName(stagingPath)!);
            await File.WriteAllBytesAsync(stagingPath, staged, ct);

            using var sha = SHA256.Create();
            using var stream = File.OpenRead(stagingPath);
            return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        }

        public FileDiffSummary DescribeChanges(IReadOnlyDictionary<string, object> edits)
        {
            byte[] payload = TryGetPayload(edits) ?? Array.Empty<byte>();
            long offset = TryGetLong(edits, ProposalToPlanMapper.EditKeyOffset);
            return new FileDiffSummary
            {
                FieldsChanged = payload.Length,
                ChangedFieldNames = new[] { $"0x{offset:X} (+{payload.Length} B)" },
                HumanSummary = $"Byte patch: {payload.Length} B at offset 0x{offset:X}",
            };
        }

        public IReadOnlyList<string> ValidateEdits(IReadOnlyDictionary<string, object> edits)
        {
            var errors = new List<string>();
            if (!edits.TryGetValue(ProposalToPlanMapper.EditKeyOffset, out var o) || o is null)
                errors.Add("missing 'offset'");
            else if (TryGetLong(edits, ProposalToPlanMapper.EditKeyOffset) < 0)
                errors.Add("'offset' must be >= 0");

            var payload = TryGetPayload(edits);
            if (payload is null)
                errors.Add("'newBytesBase64' is missing or not valid base64");
            else if (payload.Length == 0)
                errors.Add("'newBytesBase64' decodes to an empty payload");
            return errors;
        }

        static long GetLong(IReadOnlyDictionary<string, object> edits, string key)
        {
            if (!edits.TryGetValue(key, out var v) || v is null)
                throw new InvalidDataException($"byte-patch: missing '{key}'");
            return Convert.ToInt64(v);
        }

        static long TryGetLong(IReadOnlyDictionary<string, object> edits, string key)
            => edits.TryGetValue(key, out var v) && v is not null ? Convert.ToInt64(v) : -1;

        static byte[] GetPayload(IReadOnlyDictionary<string, object> edits)
            => TryGetPayload(edits)
               ?? throw new InvalidDataException($"byte-patch: '{ProposalToPlanMapper.EditKeyNewBytesBase64}' missing or invalid base64");

        static byte[]? TryGetPayload(IReadOnlyDictionary<string, object> edits)
        {
            if (!edits.TryGetValue(ProposalToPlanMapper.EditKeyNewBytesBase64, out var v) || v is null)
                return null;
            try { return Convert.FromBase64String(Convert.ToString(v)!); }
            catch (FormatException) { return null; }
        }
    }
}
