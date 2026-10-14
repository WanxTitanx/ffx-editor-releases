using System;
using System.IO;
using System.Text;

namespace FFXProjectEditor.FfxLib.Ps3
{
    internal sealed class Ps3PhyreMip0Layout
    {
        public required string Format { get; init; }
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required int PTexture2DOffset { get; init; }
        public required int BufferStart { get; init; }
        public required int Mip0Size { get; init; }
        public required long FileSize { get; init; }

        public string Summary =>
            $"{Format} {Width}x{Height} mip0@0x{BufferStart:X} len=0x{Mip0Size:X} file=0x{FileSize:X}";
    }

    internal sealed class Ps3PhyreWriteResult
    {
        public required string SourcePath { get; init; }
        public required string OutputPath { get; init; }
        public required Ps3PhyreMip0Layout SourceLayout { get; init; }
        public required Ps3PhyreMip0Layout OutputLayout { get; init; }
        public required int ReplacedBytes { get; init; }

        public bool SameLayout =>
            SourceLayout.Format == OutputLayout.Format
            && SourceLayout.Width == OutputLayout.Width
            && SourceLayout.Height == OutputLayout.Height
            && SourceLayout.BufferStart == OutputLayout.BufferStart
            && SourceLayout.Mip0Size == OutputLayout.Mip0Size
            && SourceLayout.FileSize == OutputLayout.FileSize;
    }

    internal sealed class Ps3PhyreExtractResult
    {
        public required string SourcePath { get; init; }
        public required string OutputPath { get; init; }
        public required Ps3PhyreMip0Layout SourceLayout { get; init; }
        public required int ExtractedBytes { get; init; }
        public required int DdsBytes { get; init; }
    }

    /// <summary>
    /// Conservative .dds.phyre writer for ps3data magic textures.
    /// Scope: replace only the already-sized mip0 payload inside an existing RYHPT/PTexture2D
    /// container. It does not author Phyre headers, repack mip chains, or encode PNG -> DXT.
    /// </summary>
    internal static class Ps3MagicTextureWriter
    {
        public static bool TryReadMip0Layout(string path, out Ps3PhyreMip0Layout? layout, out string note)
        {
            layout = null;
            try
            {
                return TryParseMip0Layout(File.ReadAllBytes(path), out layout, out note);
            }
            catch (Exception ex)
            {
                note = $"Read failed: {ex.Message}";
                return false;
            }
        }

        public static byte[] ReadMip0Payload(string sourcePath)
        {
            byte[] file = File.ReadAllBytes(sourcePath);
            if (!TryParseMip0Layout(file, out Ps3PhyreMip0Layout? layout, out string note) || layout == null)
                throw new InvalidDataException(note);

            byte[] mip0 = new byte[layout.Mip0Size];
            Array.Copy(file, layout.BufferStart, mip0, 0, mip0.Length);
            return mip0;
        }

        public static Ps3PhyreExtractResult ExtractMip0Dds(string sourcePath, string outputPath)
        {
            byte[] file = File.ReadAllBytes(sourcePath);
            if (!TryParseMip0Layout(file, out Ps3PhyreMip0Layout? layout, out string note) || layout == null)
                throw new InvalidDataException(note);

            byte[] mip0 = new byte[layout.Mip0Size];
            Array.Copy(file, layout.BufferStart, mip0, 0, mip0.Length);
            byte[] dds = BuildDds(layout, mip0);

            string? dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllBytes(outputPath, dds);

            return new Ps3PhyreExtractResult
            {
                SourcePath = sourcePath,
                OutputPath = outputPath,
                SourceLayout = layout,
                ExtractedBytes = mip0.Length,
                DdsBytes = dds.Length
            };
        }

        public static Ps3PhyreWriteResult WriteSameShapeMip0(string sourcePath, byte[] mip0Bytes, string outputPath)
        {
            byte[] output = File.ReadAllBytes(sourcePath);
            if (!TryParseMip0Layout(output, out Ps3PhyreMip0Layout? sourceLayout, out string note) || sourceLayout == null)
                throw new InvalidDataException(note);

            if (mip0Bytes.Length != sourceLayout.Mip0Size)
            {
                throw new InvalidDataException(
                    $"Payload length 0x{mip0Bytes.Length:X} does not match mip0 length 0x{sourceLayout.Mip0Size:X} for {sourceLayout.Summary}.");
            }

            Array.Copy(mip0Bytes, 0, output, sourceLayout.BufferStart, mip0Bytes.Length);
            string? dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllBytes(outputPath, output);

            byte[] reread = File.ReadAllBytes(outputPath);
            if (!TryParseMip0Layout(reread, out Ps3PhyreMip0Layout? outputLayout, out string outputNote) || outputLayout == null)
                throw new InvalidDataException($"Output layout became unreadable: {outputNote}");

            return new Ps3PhyreWriteResult
            {
                SourcePath = sourcePath,
                OutputPath = outputPath,
                SourceLayout = sourceLayout,
                OutputLayout = outputLayout,
                ReplacedBytes = mip0Bytes.Length
            };
        }

        public static byte[] ReadCompatibleMip0Payload(string payloadPath, Ps3PhyreMip0Layout targetLayout, out string payloadKind)
        {
            byte[] payloadFile = File.ReadAllBytes(payloadPath);
            if (LooksLikeDds(payloadFile))
            {
                payloadKind = "DDS mip0";
                return ReadDdsMip0Payload(payloadFile, targetLayout);
            }

            payloadKind = "raw mip0";
            if (payloadFile.Length != targetLayout.Mip0Size)
            {
                throw new InvalidDataException(
                    $"Raw payload length 0x{payloadFile.Length:X} does not match target mip0 length 0x{targetLayout.Mip0Size:X}.");
            }
            return payloadFile;
        }

        static bool TryParseMip0Layout(byte[] buffer, out Ps3PhyreMip0Layout? layout, out string note)
        {
            layout = null;
            if (buffer.Length < 96
                || buffer[0] != (byte)'R' || buffer[1] != (byte)'Y' || buffer[2] != (byte)'H'
                || buffer[3] != (byte)'P' || buffer[4] != (byte)'T')
            {
                note = "Not a RYHPT/.phyre container.";
                return false;
            }

            int scanLimit = Math.Min(buffer.Length, 16384);
            int pidx = -1;
            string format = string.Empty;
            int from = 0;
            while (true)
            {
                int hit = FindAscii(buffer, "PTexture2D", from, scanLimit);
                if (hit < 0)
                    break;

                string token = ReadToken(buffer, hit + 10);
                if (IsKnownFormat(token))
                {
                    pidx = hit;
                    format = token;
                    break;
                }

                from = hit + 1;
            }

            if (pidx < 0)
            {
                note = "No PTexture2D instance with a known pixel format.";
                return false;
            }

            int width = (int)ReadU32(buffer, pidx - 88);
            int height = (int)ReadU32(buffer, pidx - 84);
            if (width is < 1 or > 8192 || height is < 1 or > 8192)
            {
                note = $"Header dims implausible ({width}x{height}).";
                return false;
            }

            int bufferStart = pidx + 11 + format.Length + 38;
            int mip0Size = Mip0Size(width, height, format);
            if (bufferStart < 0 || mip0Size <= 0 || (long)bufferStart + mip0Size > buffer.Length)
            {
                note = $"mip0 region out of range (W={width} H={height} fmt={format}).";
                return false;
            }

            layout = new Ps3PhyreMip0Layout
            {
                Format = format,
                Width = width,
                Height = height,
                PTexture2DOffset = pidx,
                BufferStart = bufferStart,
                Mip0Size = mip0Size,
                FileSize = buffer.Length
            };
            note = "OK";
            return true;
        }

        static byte[] ReadDdsMip0Payload(byte[] dds, Ps3PhyreMip0Layout targetLayout)
        {
            if (dds.Length < 128 || !LooksLikeDds(dds))
                throw new InvalidDataException("Not a supported DDS file.");

            uint headerSize = ReadU32(dds, 4);
            if (headerSize != 124)
                throw new InvalidDataException($"Unsupported DDS header size {headerSize}.");

            int height = (int)ReadU32(dds, 12);
            int width = (int)ReadU32(dds, 16);
            uint pfFlags = ReadU32(dds, 80);
            string fourCc = ReadFourCc(dds, 84);
            uint rgbBits = ReadU32(dds, 88);

            string format;
            if ((pfFlags & 0x4) != 0)
            {
                if (fourCc == "DX10")
                    throw new InvalidDataException("DDS DX10 headers are not supported by this narrow Phyre repacker.");
                format = fourCc;
            }
            else if ((pfFlags & 0x40) != 0 && rgbBits == 32)
            {
                format = "ARGB8";
            }
            else if ((pfFlags & 0x20000) != 0 && rgbBits == 8)
            {
                format = "L8";
            }
            else
            {
                throw new InvalidDataException($"Unsupported DDS pixel format flags=0x{pfFlags:X}, fourCC='{fourCc}', rgbBits={rgbBits}.");
            }

            if (!IsKnownFormat(format))
                throw new InvalidDataException($"Unsupported DDS format '{format}'.");
            if (format != targetLayout.Format)
                throw new InvalidDataException($"DDS format '{format}' does not match target Phyre format '{targetLayout.Format}'.");
            if (width != targetLayout.Width || height != targetLayout.Height)
                throw new InvalidDataException($"DDS dimensions {width}x{height} do not match target Phyre {targetLayout.Width}x{targetLayout.Height}.");

            const int dataStart = 128;
            if (dataStart + targetLayout.Mip0Size > dds.Length)
                throw new InvalidDataException("DDS does not contain enough bytes for mip0.");

            byte[] mip0 = new byte[targetLayout.Mip0Size];
            Array.Copy(dds, dataStart, mip0, 0, mip0.Length);
            return mip0;
        }

        static byte[] BuildDds(Ps3PhyreMip0Layout layout, byte[] mip0)
        {
            if (mip0.Length != layout.Mip0Size)
                throw new InvalidDataException($"mip0 length 0x{mip0.Length:X} does not match layout 0x{layout.Mip0Size:X}.");

            byte[] dds = new byte[128 + mip0.Length];
            Encoding.ASCII.GetBytes("DDS ").CopyTo(dds, 0);
            WriteU32(dds, 4, 124);

            bool compressed = layout.Format is "DXT1" or "DXT3" or "DXT5";
            const uint ddsdCaps = 0x1;
            const uint ddsdHeight = 0x2;
            const uint ddsdWidth = 0x4;
            const uint ddsdPitch = 0x8;
            const uint ddsdPixelFormat = 0x1000;
            const uint ddsdLinearSize = 0x80000;
            WriteU32(dds, 8, ddsdCaps | ddsdHeight | ddsdWidth | ddsdPixelFormat | (compressed ? ddsdLinearSize : ddsdPitch));
            WriteU32(dds, 12, (uint)layout.Height);
            WriteU32(dds, 16, (uint)layout.Width);
            WriteU32(dds, 20, compressed ? (uint)layout.Mip0Size : (uint)(layout.Format == "L8" ? layout.Width : layout.Width * 4));

            const uint ddpfAlphaPixels = 0x1;
            const uint ddpfFourCc = 0x4;
            const uint ddpfRgb = 0x40;
            const uint ddpfLuminance = 0x20000;
            WriteU32(dds, 76, 32);

            if (compressed)
            {
                WriteU32(dds, 80, ddpfFourCc);
                WriteFourCc(dds, 84, layout.Format);
            }
            else if (layout.Format == "ARGB8")
            {
                WriteU32(dds, 80, ddpfRgb | ddpfAlphaPixels);
                WriteU32(dds, 88, 32);
                WriteU32(dds, 92, 0x00FF0000);
                WriteU32(dds, 96, 0x0000FF00);
                WriteU32(dds, 100, 0x000000FF);
                WriteU32(dds, 104, 0xFF000000);
            }
            else if (layout.Format == "L8")
            {
                WriteU32(dds, 80, ddpfLuminance);
                WriteU32(dds, 88, 8);
                WriteU32(dds, 92, 0x000000FF);
            }
            else
            {
                throw new InvalidDataException($"Unsupported DDS export format '{layout.Format}'.");
            }

            const uint ddsCapsTexture = 0x1000;
            WriteU32(dds, 108, ddsCapsTexture);
            Array.Copy(mip0, 0, dds, 128, mip0.Length);
            return dds;
        }

        static int Mip0Size(int width, int height, string format) => format switch
        {
            "ARGB8" => width * height * 4,
            "DXT1" => ((width + 3) / 4) * ((height + 3) / 4) * 8,
            "DXT3" or "DXT5" => ((width + 3) / 4) * ((height + 3) / 4) * 16,
            "L8" => width * height,
            _ => 0
        };

        static bool IsKnownFormat(string token) =>
            token is "ARGB8" or "DXT1" or "DXT3" or "DXT5" or "L8";

        static bool LooksLikeDds(byte[] buffer) =>
            buffer.Length >= 4
            && buffer[0] == (byte)'D'
            && buffer[1] == (byte)'D'
            && buffer[2] == (byte)'S'
            && buffer[3] == (byte)' ';

        static string ReadFourCc(byte[] buffer, int offset) =>
            offset < 0 || offset + 4 > buffer.Length
                ? string.Empty
                : Encoding.ASCII.GetString(buffer, offset, 4).TrimEnd('\0', ' ');

        static void WriteFourCc(byte[] buffer, int offset, string fourCc)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(fourCc.PadRight(4, '\0')[..4]);
            Array.Copy(bytes, 0, buffer, offset, 4);
        }

        static int FindAscii(byte[] buffer, string needle, int start, int limit)
        {
            byte[] needleBytes = Encoding.ASCII.GetBytes(needle);
            int end = Math.Min(buffer.Length - needleBytes.Length, limit);
            for (int i = Math.Max(0, start); i <= end; i++)
            {
                bool match = true;
                for (int k = 0; k < needleBytes.Length; k++)
                {
                    if (buffer[i + k] != needleBytes[k]) { match = false; break; }
                }
                if (match)
                    return i;
            }
            return -1;
        }

        static string ReadToken(byte[] buffer, int start)
        {
            int o = start;
            while (o < buffer.Length && buffer[o] == 0) o++;
            StringBuilder sb = new();
            while (o < buffer.Length && buffer[o] >= 32 && buffer[o] < 127)
            {
                sb.Append((char)buffer[o]);
                o++;
            }
            return sb.ToString();
        }

        static uint ReadU32(byte[] buffer, int offset) =>
            offset < 0 || offset + 4 > buffer.Length ? 0u : BitConverter.ToUInt32(buffer, offset);

        static void WriteU32(byte[] buffer, int offset, uint value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            Array.Copy(bytes, 0, buffer, offset, 4);
        }
    }
}
