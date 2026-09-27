using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FFXProjectEditor.FfxLib.MagicDll
{
    /// <summary>
    /// Mapa de famílias PPP (opcode → <see cref="MagicFamilySchema"/>).
    /// Carregado de um JSON em runtime (field_map.json ou diretório families\*.json,
    /// caminho configurável) com fallback para o mapa embutido
    /// (<see cref="MagicFieldMapEmbeddedData"/>) quando nenhum arquivo é encontrado.
    /// </summary>
    public sealed class MagicFieldMap
    {
        private readonly IReadOnlyDictionary<string, MagicFamilySchema> _families;

        /// <summary>Todas as famílias conhecidas (por opcode).</summary>
        public IReadOnlyDictionary<string, MagicFamilySchema> Families => _families;

        /// <summary>Quantidade de famílias no mapa.</summary>
        public int Count => _families.Count;

        /// <summary>Descrição da origem dos dados (caminho do arquivo ou "embutido").</summary>
        public string SourceDescription { get; }

        private MagicFieldMap(
            IReadOnlyDictionary<string, MagicFamilySchema> families,
            string sourceDescription)
        {
            _families = families;
            SourceDescription = sourceDescription;
        }

        /// <summary>Tenta obter o schema de um opcode. Ex.: TryGet("pppSclMove", out schema).</summary>
        public bool TryGet(string opcode, out MagicFamilySchema schema)
        {
            if (_families.TryGetValue(opcode, out MagicFamilySchema? s))
            {
                schema = s;
                return true;
            }
            schema = null!;
            return false;
        }

        /// <summary>
        /// Carrega o mapa de famílias.
        /// </summary>
        /// <param name="explicitPath">
        /// Caminho configurável: arquivo field_map.json OU diretório com families\*.json.
        /// Null = default: tenta field_map.json nos caminhos conhecidos (cwd, base dir,
        /// raiz do repo), depois o diretório families\*, e por fim o mapa embutido.
        /// </param>
        /// <exception cref="FileNotFoundException">Quando explicitPath é informado e não existe.</exception>
        /// <exception cref="JsonException">Quando o JSON informado é inválido.</exception>
        public static MagicFieldMap Load(string? explicitPath = null)
        {
            if (!string.IsNullOrWhiteSpace(explicitPath))
            {
                if (Directory.Exists(explicitPath))
                    return LoadFamiliesDirectory(explicitPath);
                if (File.Exists(explicitPath))
                    return LoadFile(explicitPath);
                throw new FileNotFoundException($"Family map not found: {explicitPath}");
            }

            // 1) field_map.json nos caminhos conhecidos
            foreach (string candidate in DefaultFieldMapCandidates())
            {
                if (File.Exists(candidate))
                    return LoadFile(candidate);
            }

            // 2) diretório families\*.json
            foreach (string candidate in DefaultFamiliesDirectoryCandidates())
            {
                if (Directory.Exists(candidate) && Directory.EnumerateFiles(candidate, "*.json").Any())
                    return LoadFamiliesDirectory(candidate);
            }

            // 3) mapa embutido (offline)
            return LoadEmbedded();
        }

        /// <summary>Loads the complete canonical family map embedded for offline release use.</summary>
        public static MagicFieldMap LoadEmbedded()
        {
            var families = ParseFamiliesObject(
                JsonDocument.Parse(MagicFieldMapEmbeddedData.Json).RootElement,
                requirePayloadConsumer: false);
            foreach (string opcode in MagicFieldMapEmbeddedData.KnownReadOnlyFamilies)
            {
                if (families.ContainsKey(opcode))
                    continue;
                families.Add(opcode, new MagicFamilySchema(
                    opcode,
                    handlerAddr: null,
                    payloadConsumer: false,
                    editable: false,
                    window: null,
                    matchWord: null,
                    guard: null,
                    fields: Array.Empty<MagicFieldSpec>(),
                    status: "READ_ONLY_CATALOG",
                    usage: "Known handler name without a reviewed payload/write schema.",
                    rawWidthYonishi: null,
                    widthReconciled: null,
                    semanticsCategory: null,
                    realFunc: null));
            }
            return new MagicFieldMap(families, "embutido (MagicFieldMapEmbeddedData)");
        }

        /// <summary>
        /// Carrega um arquivo JSON com o mapa de famílias. Aceita os dois formatos:
        /// field_map.json ({"meta":..., "families": {opcode: schema}}) ou um objeto
        /// direto {opcode: schema} (formato dos families\*.json consolidados).
        /// </summary>
        public static MagicFieldMap LoadFile(string path)
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(path));
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new JsonException($"Unexpected format in {path}: expected a JSON object.");

            JsonElement familiesElement = root;
            if (root.TryGetProperty("families", out JsonElement familiesProp) &&
                familiesProp.ValueKind == JsonValueKind.Object)
            {
                familiesElement = familiesProp;
            }

            var families = ParseFamiliesObject(familiesElement, requirePayloadConsumer: false);
            if (families.Count == 0)
                throw new JsonException($"No family found in {path}.");
            return new MagicFieldMap(families, path);
        }
        /// <summary>
        /// Carrega um diretório de families\*.json (um arquivo por família, com "opcode" no topo).
        /// </summary>
        public static MagicFieldMap LoadFamiliesDirectory(string directory)
        {
            var families = new Dictionary<string, MagicFamilySchema>(StringComparer.Ordinal);
            foreach (string file in Directory.EnumerateFiles(directory, "*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(file));
                MagicFamilySchema? schema = TryParseSchema(doc.RootElement, file);
                if (schema != null)
                    families[schema.Opcode] = schema;
            }
            if (families.Count == 0)
                throw new JsonException($"No valid family in {directory}.");
            return new MagicFieldMap(families, directory);
        }

        private static Dictionary<string, MagicFamilySchema> ParseFamiliesObject(
            JsonElement familiesElement,
            bool requirePayloadConsumer)
        {
            var families = new Dictionary<string, MagicFamilySchema>(StringComparer.Ordinal);
            foreach (JsonProperty property in familiesElement.EnumerateObject())
            {
                MagicFamilySchema? schema = TryParseSchema(property.Value, property.Name);
                if (schema == null)
                    continue;
                if (requirePayloadConsumer && !schema.PayloadConsumer)
                    continue;
                families[schema.Opcode] = schema;
            }
            return families;
        }

        private static MagicFamilySchema? TryParseSchema(JsonElement element, string sourceName)
        {
            if (element.ValueKind != JsonValueKind.Object)
                return null;
            string opcode = GetString(element, "opcode") ?? Path.GetFileNameWithoutExtension(sourceName);
            if (string.IsNullOrWhiteSpace(opcode))
                return null;

            MagicWindow? window = null;
            if (element.TryGetProperty("window", out JsonElement windowElement) &&
                windowElement.ValueKind == JsonValueKind.Object &&
                windowElement.TryGetProperty("start", out JsonElement startElement) &&
                windowElement.TryGetProperty("width", out JsonElement widthElement))
            {
                window = new MagicWindow(startElement.GetInt32(), widthElement.GetInt32());
            }

            var fields = new List<MagicFieldSpec>();
            if (element.TryGetProperty("fields", out JsonElement fieldsElement) &&
                fieldsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement fieldElement in fieldsElement.EnumerateArray())
                {
                    if (fieldElement.ValueKind != JsonValueKind.Object)
                        continue;
                    string name = GetString(fieldElement, "name") ?? "";
                    if (!fieldElement.TryGetProperty("offset", out JsonElement offsetElement) ||
                        !fieldElement.TryGetProperty("width", out JsonElement widthElement2))
                        continue;
                    string typeName = GetString(fieldElement, "type");
                    if (!MagicField.TryParseType(typeName, out MagicFieldType type))
                        continue;
                    string semantics = GetString(fieldElement, "semantics") ?? "";
                    fields.Add(new MagicFieldSpec(name, offsetElement.GetInt32(), widthElement2.GetInt32(), type, semantics));
                }
            }

            return new MagicFamilySchema(
                opcode,
                GetString(element, "handler_addr"),
                GetBool(element, "payload_consumer"),
                GetBool(element, "editable"),
                window,
                GetString(element, "match_word"),
                GetString(element, "guard"),
                fields,
                GetString(element, "status"),
                GetString(element, "usage"),
                GetInt(element, "raw_width_yonishi"),
                GetInt(element, "width_reconciled"),
                GetString(element, "semantics_category"),
                GetString(element, "real_func"));
        }
        // --- Candidatos de caminho ----------------------------------------------------

        private static IEnumerable<string> DefaultFieldMapCandidates()
        {
            string baseDir = AppContext.BaseDirectory;
            string cwd = Environment.CurrentDirectory;
            string? repoRoot = FindRepoRoot(baseDir);

            var candidates = new List<string>
            {
                Path.Combine(cwd, "work", "magic_editor", "field_map.json"),
                Path.Combine(cwd, "field_map.json"),
                Path.Combine(baseDir, "work", "magic_editor", "field_map.json"),
                Path.Combine(baseDir, "field_map.json"),
            };
            if (repoRoot != null)
                candidates.Add(Path.Combine(repoRoot, "work", "magic_editor", "field_map.json"));
            return candidates;
        }

        private static IEnumerable<string> DefaultFamiliesDirectoryCandidates()
        {
            string cwd = Environment.CurrentDirectory;
            string? repoRoot = FindRepoRoot(AppContext.BaseDirectory);
            var candidates = new List<string>
            {
                Path.Combine(cwd, "work", "ppp_c2", "families"),
            };
            if (repoRoot != null)
                candidates.Add(Path.Combine(repoRoot, "work", "ppp_c2", "families"));
            return candidates;
        }

        /// <summary>Sobe do baseDir até achar a raiz do repo (onde vive work/magic_editor).</summary>
        private static string? FindRepoRoot(string startDir)
        {
            DirectoryInfo? dir = new DirectoryInfo(startDir);
            for (int i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "work", "magic_editor")) ||
                    File.Exists(Path.Combine(dir.FullName, "FFXProjectEditor.sln")))
                    return dir.FullName;
            }
            return null;
        }

        private static string? GetString(JsonElement element, string property)
        {
            return element.TryGetProperty(property, out JsonElement value) &&
                   value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static bool GetBool(JsonElement element, string property)
        {
            return element.TryGetProperty(property, out JsonElement value) &&
                   value.ValueKind == JsonValueKind.True;
        }

        private static int? GetInt(JsonElement element, string property)
        {
            return element.TryGetProperty(property, out JsonElement value) &&
                   value.ValueKind is JsonValueKind.Number
                ? value.GetInt32()
                : null;
        }
    }
}
