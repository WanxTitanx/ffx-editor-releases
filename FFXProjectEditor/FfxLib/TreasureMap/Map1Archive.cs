using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FFXProjectEditor.FfxLib.TreasureMap;

// ── Map1Archive (mapout.vpa / MAP1) ────────────────────────────────────────────────────
// Splits a field's mapout.vpa into its 16 MAP1 sections. Section 11 holds the guide-map
// geometry (YNGM, see GuideMapGeometry); other sections hold collision/lighting/etc.
// Header check is in Map1Header.
// ──────────────────────────────────────────────────────────────────────────────────────

public sealed record Map1Section(int Index, int Offset, int Length, byte[] Bytes);

public sealed class Map1Archive
{
    public const int SectionCount = 16;
    public string Path { get; }
    public IReadOnlyList<Map1Section> Sections { get; }

    private Map1Archive(string path, IReadOnlyList<Map1Section> sections)
    {
        Path = path;
        Sections = sections;
    }

    public Map1Section? FindSection(int index) => Sections.FirstOrDefault(s => s.Index == index);

    public static Map1Archive Read(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length < 4 || Encoding.ASCII.GetString(bytes, 0, 4) != Map1Header.Magic)
            throw new InvalidDataException($"'{path}' is not a valid MAP1 archive.");
        if (bytes.Length < 0x50)
            return new Map1Archive(System.IO.Path.GetFullPath(path), []);

        int[] offsets = Enumerable.Range(0, SectionCount)
            .Select(i => BitConverter.ToInt32(bytes, 0x10 + i * 4)).ToArray();
        var sections = new List<Map1Section>();
        for (int i = 0; i < offsets.Length; i++)
        {
            int offset = offsets[i];
            if (offset == 0) continue;
            if (offset < 0x50 || offset >= bytes.Length)
                throw new InvalidDataException($"MAP1 section {i} has invalid offset 0x{offset:X}.");
            int end = offsets.Where(o => o > offset).DefaultIfEmpty(bytes.Length).Min();
            if (end > bytes.Length || end <= offset)
                throw new InvalidDataException($"MAP1 section {i} has an invalid extent.");
            byte[] sectionBytes = new byte[end - offset];
            Array.Copy(bytes, offset, sectionBytes, 0, sectionBytes.Length);
            sections.Add(new Map1Section(i, offset, sectionBytes.Length, sectionBytes));
        }
        return new Map1Archive(System.IO.Path.GetFullPath(path), sections);
    }
}
