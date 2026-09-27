using FFXProjectEditor.FfxLib.Text;
using FFXProjectEditor.Utils.Encoding;
using System;
using System.Collections.Generic;
using System.IO;
using Xe.BinaryMapper;

namespace FFXProjectEditor.Files
{
    public partial class MonX_File
    {
        public byte[] Write()
        {
            using MemoryStream stream = new();

            BinaryMapping.WriteObject(stream, ThisHeader);

            List<byte> textBytes = new();
            Dictionary<string, ushort> stringPool = new(StringComparer.Ordinal);

            foreach (Entry entry in Entries)
            {
                entry.NameOfst = AppendString(textBytes, stringPool, ResolveTextBytes(entry.Name, entry.NameScript, entry.NameScriptBytes));
                entry.SensorOfst = AppendString(textBytes, stringPool, ResolveTextBytes(entry.Sensor, entry.SensorScript, entry.SensorScriptBytes));
                entry.UnkOfst = AppendString(textBytes, stringPool, ResolveTextBytes(entry.Unk, entry.UnkScript, entry.UnkScriptBytes));
                entry.ScanOfst = AppendString(textBytes, stringPool, ResolveTextBytes(entry.Scan, entry.ScanScript, entry.ScanScriptBytes));
                entry.Unk2Ofst = AppendString(textBytes, stringPool, ResolveTextBytes(entry.Unk2, entry.Unk2Script, entry.Unk2ScriptBytes));

                BinaryMapping.WriteObject(stream, entry);
            }

            stream.Write(textBytes.ToArray(), 0, textBytes.Count);
            return stream.ToArray();
        }

        static ushort AppendString(List<byte> textBytes, Dictionary<string, ushort> stringPool, byte[] bytes)
        {
            string key = Convert.ToHexString(bytes);
            if (stringPool.TryGetValue(key, out ushort existingOffset))
                return existingOffset;

            if (textBytes.Count > ushort.MaxValue)
                throw new InvalidDataException("Monster localization text table exceeded 64KB.");

            ushort offset = (ushort)textBytes.Count;
            textBytes.AddRange(bytes);
            textBytes.Add(0);
            stringPool[key] = offset;
            return offset;
        }

        static byte[] ResolveTextBytes(string? currentText, FfxEncoding.TextScript? originalScript, byte[]? originalBytes)
        {
            byte[]? baselineBytes = originalBytes;
            if ((baselineBytes == null || baselineBytes.Length == 0) && originalScript != null)
            {
                baselineBytes = TextBinary_Util.EncodeScriptToBytes(
                    TextBinary_Util.NormalizeText(originalScript.GetString(FfxEncoding.UsDecoder, true)),
                    FfxEncoding.UsDecoder);
            }

            return TextBinary_Util.ResolveTextBytes(currentText, baselineBytes, FfxEncoding.UsDecoder);
        }
    }
}
