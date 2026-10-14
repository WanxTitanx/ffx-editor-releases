using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

using FFXProjectEditor.Diagnostics;

namespace FFXProjectEditor.Modules.Common.ViewerHub
{
    /// <summary>
    /// 🐉 NOCLIP DATA REPAIR — reparo automático dos dados do noclip.website (FFX).
    /// A extração local de `/data/FinalFantasyX/` pode conter bins corrompidos (truncados ou com
    /// headers zerados/lixo). Quando o RealGame (viewer noclip) parseia um bin corrompido, o
    /// DataFetcher/parsers estouram com RangeError e a batalha não renderiza.
    /// Este helper valida os arquivos críticos compartilhados (header local, sem rede) e, se
    /// corrompidos, baixa a versão correta do CDN oficial do noclip (z.noclip.website) e
    /// sobrescreve. Chamado no EnsureServer do hub — o usuário não precisa limpar nada.
    /// MAINT: o serve-dir tem junction `data/` → raiz do noclip; reparar o arquivo do serve-dir
    /// repara o arquivo da extração do usuário (mesmo arquivo físico).
    /// </summary>
    public static class NoclipDataRepair
    {
        private const string CdnBase = "https://z.noclip.website/FinalFantasyX";

        // Arquivos críticos compartilhados: mesmo tamanho pode ter conteúdo lixo (já visto:
        // common_textures.bin com spriteSpecsOffset=0xb4ffae9, screen_shatter/env_map com header zerado).
        // Validação de header espelhando o parser real do noclip:
        //   common_textures/screen_shatter → uploadSpriteTextures/parseActorParticles leem u32@0x3C (dataOffset).
        //   env_map_texture → parseActorTextures lê u32@0x18 (texDataOffset). ATENÇÃO: u32@0x3C do env_map
        //   é 0x77777777 (padrão válido, NÃO offset) — não usar 0x3C nele.
        private static readonly (string Name, Func<byte[], bool> Valid)[] CriticalFiles =
        {
            ("common_textures.bin", b => OffsetPlausible(b, 0x3C)),
            ("screen_shatter.bin", b => OffsetPlausible(b, 0x3C)),
            ("env_map_texture.bin", b => OffsetPlausible(b, 0x18)),
        };

        private static bool OffsetPlausible(byte[] b, int off)
        {
            if (b.Length < off + 4) return false;
            uint v = BitConverter.ToUInt32(b, off);
            return v > 0 && v < (uint)b.Length;
        }

        // Modelos .chr (1d/): boneCount (u32@0x4) e meshCount (u32@0x8) plausíveis.
        // meshCount pode ser 0 (modelos válidos têm boneCount>=1, meshCount 0..N).
        // Range largo para pegar só lixo (valores gigantes), sem falsos positivos.
        private static bool ModelHeaderPlausible(byte[] b)
        {
            if (b.Length < 0x20) return false;
            uint bone = BitConverter.ToUInt32(b, 0x4);
            uint mesh = BitConverter.ToUInt32(b, 0x8);
            return bone >= 1 && bone <= 500 && mesh <= 50;
        }

        /// <summary>Valida os arquivos críticos do serve-dir e repara via CDN se corrompidos.
        /// Retorna true se tudo OK (ou reparado); false se houve falha de rede no download.</summary>
        public static bool CheckAndRepair(string serveDir)
        {
            string dataDir = Path.Combine(serveDir, "data", "FinalFantasyX");
            if (!Directory.Exists(dataDir))
            {
                DebugLog.Warn("Hub.DataRepair", $"data dir não existe: {dataDir}");
                return true;
            }

            bool allOk = true;
            foreach (var (name, valid) in CriticalFiles)
            {
                string path = Path.Combine(dataDir, name);
                if (!File.Exists(path))
                {
                    DebugLog.Warn("Hub.DataRepair", $"arquivo crítico ausente: {name}");
                    allOk = false;
                    continue;
                }

                byte[] full = File.ReadAllBytes(path); // check compara off < len(b)
                if (valid(full))
                    continue; // OK, sem rede.

                allOk &= RepairFromCdn(name, path);
            }

            // Modelos .chr (1d/): valida header localmente (rápido, sem rede). Só baixa se corrompido.
            string modelDir = Path.Combine(dataDir, "1d");
            if (Directory.Exists(modelDir))
            {
                foreach (string file in Directory.EnumerateFiles(modelDir, "*.bin"))
                {
                    byte[] head = ReadHead(file, 0x20);
                    if (ModelHeaderPlausible(head))
                        continue;
                    string name = Path.GetFileName(file);
                    DebugLog.Warn("Hub.DataRepair", $"{name} corrompido (header de modelo implausível) — baixando do CDN...");
                    allOk &= RepairFromCdn($"1d/{name}", file);
                }
            }
            return allOk;
        }

        private static byte[] ReadHead(string path, int count)
        {
            using var fs = File.OpenRead(path);
            byte[] buf = new byte[count];
            int n = fs.Read(buf, 0, count);
            if (n < count) Array.Resize(ref buf, n);
            return buf;
        }

        private static bool RepairFromCdn(string relPath, string dest)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                // O CDN (Cloudflare) bloqueia User-Agent padrão do .NET (403). UA de browser resolve.
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
                byte[] data = client.GetByteArrayAsync($"{CdnBase}/{relPath}").GetAwaiter().GetResult();
                File.WriteAllBytes(dest, data);
                DebugLog.Info("Hub.DataRepair", $"{relPath} reparado via CDN.");
                return true;
            }
            catch (Exception ex)
            {
                DebugLog.Error("Hub.DataRepair", $"download {relPath} falhou: {ex.Message}");
                return false;
            }
        }
    }
}