using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Text;
using FFXProjectEditor.Utils.Encoding;
using System;
using System.Collections.Generic;
using System.IO;

namespace FFXProjectEditor.FfxLib.Dictionaries
{
    internal static class KeyItem_Dictionary
    {
        static readonly Dictionary<ushort, string> instance = new();
        static string? loadedPath;

        public static IReadOnlyDictionary<ushort, string> Instance => instance;

        public static void EnsureLoaded(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                instance.Clear();
                loadedPath = null;
                return;
            }

            if (string.Equals(loadedPath, path, StringComparison.OrdinalIgnoreCase) && instance.Count > 0)
            {
                return;
            }

            instance.Clear();
            loadedPath = path;

            byte[] bytes = File.ReadAllBytes(path);
            EntryListFile listFile = EntryListFile.Unpack(bytes);
            if (listFile.FirstFile == null || listFile.SecondFile == null || listFile.Header.EntrySize <= 0)
            {
                return;
            }

            int entrySize = listFile.Header.EntrySize;
            int entryCount = Math.Min(listFile.Header.RealEntryCount, listFile.FirstFile.Length / entrySize);

            for (int i = 0; i < entryCount; i++)
            {
                int baseOffset = i * entrySize;
                if (baseOffset + 8 > listFile.FirstFile.Length)
                {
                    break;
                }

                ushort regularNameOffset = BitConverter.ToUInt16(listFile.FirstFile, baseOffset + 0x00);
                ushort simplifiedNameOffset = BitConverter.ToUInt16(listFile.FirstFile, baseOffset + 0x04);

                string name = DecodeStringAtOffset(listFile.SecondFile, regularNameOffset);
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = DecodeStringAtOffset(listFile.SecondFile, simplifiedNameOffset);
                }

                if (string.IsNullOrWhiteSpace(name))
                {
                    name = $"Key item #{i:X3}";
                }

                instance[(ushort)i] = name;
            }
        }

        public static bool TryGetName(ushort index, out string name)
        {
            return instance.TryGetValue(index, out name!);
        }

        static string DecodeStringAtOffset(byte[] stringBytes, int offset)
        {
            if (stringBytes == null || offset < 0 || offset >= stringBytes.Length)
            {
                return string.Empty;
            }

            byte[] raw = TextBinary_Util.ReadNullTerminatedScript(stringBytes, offset);
            return TextBinary_Util.DecodeScriptToString(raw, FfxEncoding.UsDecoder, true).Trim();
        }
    }
}
