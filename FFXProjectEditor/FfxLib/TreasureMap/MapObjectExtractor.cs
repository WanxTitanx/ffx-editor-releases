using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FFXProjectEditor.FfxLib.TreasureMap;

// ── MapObjectExtractor ─────────────────────────────────────────────────────────────────
// Extrai os objetos de campo da seção 2 do MAP1 (mapout.vpa).
//
// Estrutura da seção 2:
//   Header (0x00-0x20) + node table (0x20..object table) + object table.
//   Cada record de objeto: 8 bytes = (u32 model_id, u32 packed_position).
//   packed_position = (z << 16) | x  (lo = X unsigned 16-bit, hi = Z unsigned 16-bit).
//
// A faixa 0x5000-0x50C7 corresponde aos slots de tesouro (200 records, 22 bytes cada no
// runtime — FFX_Field_GetModelRecordByCode@0x7ABBF0). O model id do baú = slot + 20480.
// Os objetos da seção 2 SÃO os baús (e outros objetos de campo que compartilham a faixa).
//
// Coordenadas: os objetos usam unsigned 16-bit (0-65535). A conversão para o guide map
// (world coords, ex. bika02 X[-89..72] Z[-100..98]) é:
//   guide_x = obj_x / 65535 * (maxX - minX) + minX
//   guide_z = obj_z / 65535 * (maxZ - minZ) + minZ
// onde minX/maxX/minZ/maxZ são os bounds do guide map (GuideMapModel.BoundsMin/Max).
//
// MAINT: a extração por varredura da seção inteira acha FALSOS POSITIVOS (padrões 0x50xx
// na node table). A tabela de objetos começa após a node table; use ExtractFromObjectTable
// para pegar só os objetos reais (offset da object table = offsets[2] + objectTableOffset).
// ──────────────────────────────────────────────────────────────────────────────────────
public sealed record MapObject(int ModelId, int X, int Z, int Offset)
{
    public int Slot => ModelId - 0x5000;
    public bool IsChestRange => ModelId >= 0x5000 && ModelId <= 0x50C7;
}

public static class MapObjectExtractor
{
    private const string MapMagic = "MAP1";

    /// <summary>Extrai todos os objetos da seção 2 (varredura completa — inclui falsos positivos da node table).</summary>
    public static IReadOnlyList<MapObject> ExtractAll(string mapPath)
    {
        byte[] b = ReadFile(mapPath);
        if (b is null) return [];
        int sec2 = GetSectionOffset(b, 2);
        if (sec2 <= 0) return [];
        int sec2End = GetSectionEnd(b, sec2);
        var objs = new List<MapObject>();
        int i = 0;
        while (i + 8 <= sec2End - sec2)
        {
            int modelId = BitConverter.ToInt32(b, sec2 + i) & 0xFFFF;
            if (modelId >= 0x5000 && modelId <= 0x60FF)
            {
                int pos = BitConverter.ToInt32(b, sec2 + i + 4);
                objs.Add(new MapObject(modelId, pos & 0xFFFF, (pos >> 16) & 0xFFFF, sec2 + i));
                i += 8;
            }
            else i += 4;
        }
        return objs;
    }

    /// <summary>Extrai os objetos da object table (após a node table) — sem falsos positivos da node table.</summary>
    public static IReadOnlyList<MapObject> ExtractFromObjectTable(string mapPath)
    {
        byte[] b = ReadFile(mapPath);
        if (b is null) return [];
        int sec2 = GetSectionOffset(b, 2);
        if (sec2 <= 0) return [];
        int sec2End = GetSectionEnd(b, sec2);

        // Acha o início da object table: primeiro 0x50xx que NÃO está na node table.
        // A node table tem records de 8 bytes (packed_position, index) com Z=0 (caminho).
        // A object table tem records de 8 bytes (model_id, packed_position) com Z variável.
        // Heurística: procura o primeiro 0x50xx cujo próximo record também é 0x50xx
        // (início de uma sequência de objetos).
        int start = -1;
        int i = 0;
        while (i + 16 <= sec2End - sec2)
        {
            int m1 = BitConverter.ToInt32(b, sec2 + i) & 0xFFFF;
            int m2 = BitConverter.ToInt32(b, sec2 + i + 8) & 0xFFFF;
            if (m1 >= 0x5000 && m1 <= 0x60FF && m2 >= 0x5000 && m2 <= 0x60FF)
            {
                start = i;
                break;
            }
            i += 4;
        }
        if (start < 0) return [];

        var objs = new List<MapObject>();
        i = start;
        while (i + 8 <= sec2End - sec2)
        {
            int modelId = BitConverter.ToInt32(b, sec2 + i) & 0xFFFF;
            if (modelId >= 0x5000 && modelId <= 0x60FF)
            {
                int pos = BitConverter.ToInt32(b, sec2 + i + 4);
                objs.Add(new MapObject(modelId, pos & 0xFFFF, (pos >> 16) & 0xFFFF, sec2 + i));
                i += 8;
            }
            else i += 4;
        }
        return objs;
    }

    /// <summary>Projeta um objeto do mapa para o espaço do guide map (world coords).</summary>
    public static (float X, float Z) ProjectToGuide(MapObject obj, GuideMapModel model)
    {
        float minX = model.BoundsMin.X, maxX = model.BoundsMax.X;
        float minZ = model.BoundsMin.Z, maxZ = model.BoundsMax.Z;
        float gx = obj.X / 65535f * (maxX - minX) + minX;
        float gz = obj.Z / 65535f * (maxZ - minZ) + minZ;
        return (gx, gz);
    }

    private static byte[]? ReadFile(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try { return File.ReadAllBytes(path); }
        catch { return null; }
    }

    private static int GetSectionOffset(byte[] b, int index)
    {
        if (b.Length < 0x50 || Encoding.ASCII.GetString(b, 0, 4) != MapMagic) return 0;
        return BitConverter.ToInt32(b, 0x10 + index * 4);
    }

    private static int GetSectionEnd(byte[] b, int secOffset)
    {
        int end = b.Length;
        for (int i = 0; i < 16; i++)
        {
            int o = BitConverter.ToInt32(b, 0x10 + i * 4);
            if (o > secOffset && o < end) end = o;
        }
        return end;
    }
}