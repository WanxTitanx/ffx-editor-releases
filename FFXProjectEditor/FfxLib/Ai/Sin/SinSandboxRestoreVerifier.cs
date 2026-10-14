using System;
using System.IO;
using System.Security.Cryptography;

namespace FFXProjectEditor.FfxLib.Ai.Sin
{
    // SIN Chain Builder — Gate 5 (backup/apply sandbox) hashing + byte-compare helper. SANDBOX-ONLY.
    //
    // Pure, side-effect-free verification primitives the sandbox session uses to PROVE its safety claims:
    //   - the original (pristine reference copy + the real corpus source) is byte-identical before and after;
    //   - the sandbox copy changed only where expected (its hash differs from the original after a commit);
    //   - the restore from the .prev.bak backup is byte-identical to the pre-apply image.
    // It NEVER writes — it only reads files/bytes and compares. "Backup/apply sandbox nao e authoring publico."
    public static class SinSandboxRestoreVerifier
    {
        /// <summary>Uppercase hex SHA-256 of a file's bytes.</summary>
        public static string Sha256(string path) => Sha256Bytes(File.ReadAllBytes(path));

        /// <summary>Uppercase hex SHA-256 of a byte buffer.</summary>
        public static string Sha256Bytes(byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            return Convert.ToHexString(SHA256.HashData(bytes));
        }

        /// <summary>True if two files are byte-for-byte identical.</summary>
        public static bool ByteIdentical(string a, string b) =>
            File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
    }
}
