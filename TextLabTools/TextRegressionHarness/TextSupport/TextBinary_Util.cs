using FFXProjectEditor.Utils.Encoding;
using System.Text;

namespace TextRegressionHarness.TextSupport;

public sealed class BinaryChunk
{
    public required int Index { get; init; }
    public required int Offset { get; init; }
    public required int Length { get; init; }
    public required byte[] Bytes { get; init; }
    public required string Label { get; init; }

    public bool IsPresent => Offset > 0 && Length > 0;
    public string RangeLabel => IsPresent
        ? $"{Offset:X8}h..{Offset + Length - 1:X8}h"
        : "(empty)";
}

internal static class TextBinary_Util
{
    public static List<BinaryChunk> ParseChunks(byte[] bytes, int assumedChunkCount, int chunkOffset, Func<int, string>? labelFactory = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        int chunkCount = assumedChunkCount;
        int[] offsets = new int[chunkCount + 1];

        for (int i = 0; i <= chunkCount; i++)
        {
            int offset = ReadInt32(bytes, chunkOffset + i * 4);
            if (offset == unchecked((int)0xFFFFFFFF))
            {
                chunkCount = i - 1;
                break;
            }

            offsets[i] = offset;
        }

        List<BinaryChunk> chunks = [];
        for (int i = 0; i < chunkCount; i++)
        {
            int offset = offsets[i];
            if (offset == 0)
            {
                chunks.Add(new BinaryChunk
                {
                    Index = i,
                    Offset = 0,
                    Length = 0,
                    Bytes = Array.Empty<byte>(),
                    Label = labelFactory?.Invoke(i) ?? $"Chunk {i:00}"
                });
                continue;
            }

            int endOffset = -1;
            for (int j = i + 1; j <= chunkCount; j++)
            {
                if (offsets[j] >= offset)
                {
                    endOffset = offsets[j];
                    break;
                }
            }

            if (endOffset < 0 || endOffset > bytes.Length)
            {
                endOffset = bytes.Length;
            }

            if (offset > bytes.Length)
            {
                throw new InvalidOperationException($"Chunk {i} starts past EOF: {offset:X8}");
            }

            int length = Math.Max(0, endOffset - offset);
            byte[] chunkBytes = new byte[length];
            Array.Copy(bytes, offset, chunkBytes, 0, length);

            chunks.Add(new BinaryChunk
            {
                Index = i,
                Offset = offset,
                Length = length,
                Bytes = chunkBytes,
                Label = labelFactory?.Invoke(i) ?? $"Chunk {i:00}"
            });
        }

        return chunks;
    }

    public static ushort ReadUInt16(byte[] bytes, int offset)
    {
        if (offset < 0 || offset + 2 > bytes.Length)
        {
            return 0;
        }

        return (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
    }

    public static int ReadInt32(byte[] bytes, int offset)
    {
        if (offset < 0 || offset + 4 > bytes.Length)
        {
            return 0;
        }

        return bytes[offset]
            | (bytes[offset + 1] << 8)
            | (bytes[offset + 2] << 16)
            | (bytes[offset + 3] << 24);
    }

    public static byte[] ReadNullTerminatedScript(byte[] bytes, int offset)
    {
        if (bytes == null || offset < 0 || offset >= bytes.Length)
        {
            return Array.Empty<byte>();
        }

        int endIndex = offset;
        while (endIndex < bytes.Length && bytes[endIndex] != 0)
        {
            endIndex++;
        }

        int length = Math.Max(0, endIndex - offset);
        byte[] result = new byte[length];
        Array.Copy(bytes, offset, result, 0, length);
        return result;
    }

    public static int FindNullTerminator(byte[] bytes, int offset)
    {
        if (bytes == null || offset < 0 || offset >= bytes.Length)
        {
            return -1;
        }

        for (int i = offset; i < bytes.Length; i++)
        {
            if (bytes[i] == 0)
            {
                return i;
            }
        }

        return -1;
    }

    public static string DecodeScriptToString(byte[] scriptBytes, Dictionary<byte, char> decoder, bool withControlCodes = true)
    {
        if (scriptBytes == null || scriptBytes.Length == 0)
        {
            return string.Empty;
        }

        return FfxEncoding.DecodeScript(scriptBytes).GetString(decoder, withControlCodes);
    }

    public static byte[] EncodeScriptToBytes(string text, Dictionary<byte, char> decoder)
    {
        if (!TryEncodeScriptToBytes(text, decoder, out byte[] bytes, out string? error))
        {
            throw new InvalidDataException(error ?? "Unsupported text script.");
        }

        return bytes;
    }

    public static bool TryEncodeScriptToBytes(string text, Dictionary<byte, char> decoder, out byte[] bytes, out string? error)
    {
        ArgumentNullException.ThrowIfNull(decoder);

        error = null;
        bytes = Array.Empty<byte>();
        Dictionary<char, byte> encoder = BuildEncoder(decoder);
        List<byte> result = [];
        string normalized = NormalizeText(text);

        for (int i = 0; i < normalized.Length; i++)
        {
            char c = normalized[i];
            if (c == '\0')
            {
                error = "NUL character is not allowed inside text scripts.";
                return false;
            }

            if (c == '\n')
            {
                result.Add(FfxEncoding.C_NEW_LINE);
                continue;
            }

            if (TryMatchToken(normalized, i, FfxEncoding.FormatCodes, out byte formatCode, out int formatLength))
            {
                result.Add(FfxEncoding.C_FORMAT);
                result.Add(formatCode);
                i += formatLength - 1;
                continue;
            }

            if (TryMatchToken(normalized, i, FfxEncoding.CharacterNameCodes, out byte characterCode, out int characterLength))
            {
                result.Add(FfxEncoding.C_CHAR_NAME);
                result.Add(characterCode);
                i += characterLength - 1;
                continue;
            }

            if (TryReadUnsupportedToken(normalized, i, out string? unsupportedToken))
            {
                error = DescribeUnsupportedToken(unsupportedToken!);
                return false;
            }

            if (!encoder.TryGetValue(c, out byte encodedChar))
            {
                error = $"Unsupported character/token: '{c}'";
                return false;
            }

            result.Add(encodedChar);
        }

        bytes = result.ToArray();
        return true;
    }

    public static string NormalizeText(string? text)
    {
        return (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
    }

    public static byte[] ResolveTextBytes(string? currentText, byte[]? originalBytes, Dictionary<byte, char> decoder)
    {
        ArgumentNullException.ThrowIfNull(decoder);

        string normalizedCurrent = NormalizeText(currentText);
        string originalText = originalBytes == null || originalBytes.Length == 0
            ? string.Empty
            : DecodeScriptToString(originalBytes, decoder, true);

        if (originalBytes != null && string.Equals(normalizedCurrent, NormalizeText(originalText), StringComparison.Ordinal))
        {
            return originalBytes;
        }

        return EncodeScriptToBytes(normalizedCurrent, decoder);
    }

    public static void WriteUInt16(byte[] bytes, int offset, ushort value)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (offset < 0 || offset + 2 > bytes.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        bytes[offset] = (byte)(value & 0xFF);
        bytes[offset + 1] = (byte)(value >> 8);
    }

    static Dictionary<char, byte> BuildEncoder(Dictionary<byte, char> decoder)
    {
        Dictionary<char, byte> encoder = [];
        foreach ((byte key, char value) in decoder)
        {
            encoder.TryAdd(value, key);
        }

        return encoder;
    }

    static bool TryMatchToken(string input, int offset, IReadOnlyDictionary<byte, string> tokenMap, out byte code, out int length)
    {
        foreach ((byte candidateCode, string token) in tokenMap.OrderByDescending(pair => pair.Value.Length))
        {
            if (string.IsNullOrEmpty(token) || offset + token.Length > input.Length)
            {
                continue;
            }

            if (string.CompareOrdinal(input, offset, token, 0, token.Length) == 0)
            {
                code = candidateCode;
                length = token.Length;
                return true;
            }
        }

        code = 0;
        length = 0;
        return false;
    }

    static bool TryReadUnsupportedToken(string input, int offset, out string? token)
    {
        token = null;
        if (offset < 0 || offset >= input.Length || input[offset] != '<')
        {
            return false;
        }

        int endIndex = input.IndexOf('>', offset + 1);
        if (endIndex <= offset)
        {
            return false;
        }

        token = input[offset..(endIndex + 1)];
        return true;
    }

    static string DescribeUnsupportedToken(string token)
    {
        if (token.StartsWith("<MISS:", StringComparison.Ordinal))
        {
            return $"Decoded script still contains unresolved byte markers: {token}";
        }

        if (token.StartsWith("<C_ERROR:", StringComparison.Ordinal))
        {
            return $"Decoded script still contains unresolved control markers: {token}";
        }

        if (token.StartsWith("<C", StringComparison.Ordinal))
        {
            return $"Raw control token is not writable yet: {token}";
        }

        return $"Unsupported control token: {token}";
    }
}
