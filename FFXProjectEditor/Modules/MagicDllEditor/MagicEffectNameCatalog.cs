using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace FFXProjectEditor.Modules.MagicDllEditor
{
    /// <summary>
    /// Catálogo de nomes de efeitos ("Power Break", "Cheer", …) lido de
    /// <c>work/noclip_reference/magic_id_names_pc_20260731.json</c> (471 IDs, fonte noclip).
    ///
    /// Formato do JSON: { "magic_0003.dll": { "id": 3, "name_ps2": "Cheer" }, … }.
    /// O arquivo vive em <c>work/</c> (gitignored) — quando inacessível (build de produção
    /// sem a pasta de trabalho), o catálogo simplesmente não resolve e o ViewModel exibe
    /// apenas o nome do arquivo. Nunca lança.
    /// </summary>
    internal static partial class MagicEffectNameCatalog
    {
        private const string FileName = "magic_id_names_pc_20260731.json";

        private static readonly Lazy<IReadOnlyDictionary<string, string>> _names = new(Load);

        /// <summary>true quando o JSON de nomes foi encontrado e carregado.</summary>
        public static bool IsAvailable => _names.Value.Count > 0;

        /// <summary>
        /// Resolve o nome do efeito pela chave do JSON (ex.: "magic_0021.dll" → "Power Break").
        /// Aceita também o id numérico derivado (fallback com zero-pad de 4).
        /// </summary>
        public static bool TryGet(string dllName, out string? name)
        {
            name = null;
            if (string.IsNullOrWhiteSpace(dllName))
                return false;

            if (_names.Value.TryGetValue(dllName, out string? direct))
            {
                name = direct;
                return true;
            }

            // Fallback: normaliza o nome para magic_XXXX.dll (zero-pad de 4).
            string normalized = NormalizeDllName(dllName);
            if (normalized != null && _names.Value.TryGetValue(normalized, out string? padded))
            {
                name = padded;
                return true;
            }

            // Fallback OFFLINE (Onda 8, 2026-08-02): catálogo embutido (471 nomes) — resolve
            // mesmo em produção sem a pasta work/ (JSON gitignored).
            if (normalized != null)
            {
                string idPart = normalized["magic_".Length..^".dll".Length];
                string noPad = idPart.TrimStart('0');
                if (noPad.Length == 0)
                    noPad = "0";
                if (EmbeddedNames.TryGetValue(noPad, out string? embedded))
                {
                    name = embedded;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Normaliza "magic_21.dll"/"magic_0021.dll" → "magic_0021.dll"; null quando não casa.</summary>
        private static string? NormalizeDllName(string dllName)
        {
            string file = Path.GetFileName(dllName);
            const string prefix = "magic_";
            if (!file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return null;
            string idPart = file[prefix.Length..];
            int dot = idPart.IndexOf('.');
            if (dot > 0)
                idPart = idPart[..dot];
            if (!int.TryParse(idPart, out int id) || id < 0)
                return null;
            return $"{prefix}{id:D4}.dll";
        }

        private static IReadOnlyDictionary<string, string> Load()
        {
            string? path = FindJsonPath();
            if (path == null)
                return new Dictionary<string, string>();

            try
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(path));
                var map = new Dictionary<string, string>(256);
                foreach (JsonProperty entry in doc.RootElement.EnumerateObject())
                {
                    if (entry.Value.ValueKind != JsonValueKind.Object)
                        continue;
                    if (entry.Value.TryGetProperty("name_ps2", out JsonElement nameEl) &&
                        nameEl.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(nameEl.GetString()))
                    {
                        map[entry.Name] = nameEl.GetString()!;
                    }
                }
                return map;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // Catálogo é conveniência: falha de leitura ⇒ resolve como não disponível.
                return new Dictionary<string, string>();
            }
        }

        /// <summary>
        /// Procura o JSON nos caminhos conhecidos (cwd, base dir e raiz do repo —
        /// mesmo padrão de localização do MagicFieldMap).
        /// </summary>
        private static string? FindJsonPath()
        {
            string[] candidates =
            {
                Path.Combine(Environment.CurrentDirectory, "work", "noclip_reference", FileName),
                Path.Combine(AppContext.BaseDirectory, "work", "noclip_reference", FileName),
            };
            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            string? repoRoot = FindRepoRoot(Environment.CurrentDirectory);
            if (repoRoot != null)
            {
                string candidate = Path.Combine(repoRoot, "work", "noclip_reference", FileName);
                if (File.Exists(candidate))
                    return candidate;
            }
            return null;
        }

        /// <summary>Sobe do diretório até achar a raiz do repo (onde vive FFXProjectEditor.sln).</summary>
        private static string? FindRepoRoot(string startDir)
        {
            DirectoryInfo? dir = new DirectoryInfo(startDir);
            for (int i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "FFXProjectEditor.sln")))
                    return dir.FullName;
            }
            return null;
        }
    }
}
