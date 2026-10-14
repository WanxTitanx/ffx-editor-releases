// ============================================================================
// FfxSaveHash — SHA-256 helpers for save hashing
// PURPOSE : hex SHA-256 of a save blob + a 64-lowercase-hex validator.
// WHY     : keys sidecars / restore-gates by exact save content (byte-identity before promote).
// EVIDENCE: .NET SHA256; consumers FfxSaveSphereGridExtraStateSidecarIO and restore flows.
// MAINT   : keep lowercase hex; some callers compare case-insensitively — prefer lower everywhere.
// ============================================================================
using System;
using System.Linq;
using System.Security.Cryptography;

namespace FFXProjectEditor.FfxLib.Save
{
    public static class FfxSaveHash
    {
        public static string Sha256Hex(byte[] bytes) =>
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        public static bool IsSha256Hex(string? value) =>
            value is { Length: 64 } && value.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    }
}
