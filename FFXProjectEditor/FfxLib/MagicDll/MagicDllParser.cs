using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.FfxLib.MagicDll
{
    // ── Path-compatible parsing and owned snapshot parsing ──
    // Both entries retain the same fp/map priority and root decoder. Only the snapshot entry avoids
    // reopening the DLL label. MAINT: configured map/fp files remain separate explicit inputs.
    /// <summary>
    /// Opções do <see cref="MagicDllParser"/>: caminhos configuráveis para o mapa de
    /// famílias e para o fp.h local do efeito (resolvedor handler_table_index → opcode).
    /// </summary>
    public sealed class MagicDllParserOptions
    {
        /// <summary>
        /// Caminho do mapa de famílias: arquivo field_map.json OU diretório families\*.json.
        /// Null = default (tenta field_map.json, depois families\*, depois embutido).
        /// </summary>
        public string? FieldMapPath { get; init; }

        /// <summary>
        /// Diretório com o fp.h local do efeito (yonishi_data/dat_ov, onde vive
        /// mag_XXXX\par\fp.h). Null = catálogo embutido dos efeitos conhecidos.
        /// </summary>
        public string? FpDirectoryPath { get; init; }

        /// <summary>Mapa de famílias pré-carregado (sobrepõe <see cref="FieldMapPath"/>).</summary>
        public MagicFieldMap? FieldMap { get; init; }

        /// <summary>Tabela fp.h pré-carregada (sobrepõe <see cref="FpDirectoryPath"/>).</summary>
        public MagicFpHandlerTable? FpHandlerTable { get; init; }
    }

    /// <summary>
    /// Orquestrador do parse de magic DLLs: File (PE + .data) → Root → Programs → Slots → Fields.
    ///
    /// Fluxo (PARSER_SPEC.md §1): carrega o PE e a seção .data, varre os roots PPP,
    /// caminha as cadeias de programs/slots e resolve campos quando o handler_table_index
    /// casa um opcode catalogado com payload_consumer=true. O resolvedor de opcode usa o
    /// fp.h local do efeito quando disponível; senão o catálogo embutido
    /// (<see cref="MagicKnownEffectHandlers"/>).
    /// </summary>
    public sealed class MagicDllParser
    {
        private readonly MagicDllParserOptions? _options;

        /// <summary>Cria o parser com as opções default (mapa de famílias automático).</summary>
        public MagicDllParser()
        {
        }

        public MagicDllParser(MagicDllParserOptions options)
        {
            _options = options;
        }

        internal bool TryParseSnapshot(ReadOnlySpan<byte> bytes, string logicalPath,
            [NotNullWhen(true)] out MagicDllFile? file, out string? error)
        {
            file = null;
            error = null;
            try
            {
                if (string.IsNullOrWhiteSpace(logicalPath))
                    throw new ArgumentException("A captured Magic DLL requires a logical source label.", nameof(logicalPath));
                // Ownership starts after capture. This does not prevent concurrent writes while a
                // caller obtains the original span; the product wrapper supplies its private capture.
                MagicDllFile decoded = MagicDllFile.LoadSnapshot(logicalPath, bytes);
                IReadOnlyList<MagicDllRoot> roots = ResolveRoots(decoded, _options);
                file = new MagicDllFile(decoded.SourcePath, decoded.FileBytes, decoded.Data,
                    decoded.DataSectionRawPtr, decoded.DataSectionSize, decoded.DataSectionRva,
                    decoded.Sections, roots);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                InvalidDataException or System.Text.Json.JsonException or ArgumentException or
                IndexOutOfRangeException or OverflowException)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Parseia uma magic DLL. Lança <see cref="InvalidDataException"/> (ou IOException)
        /// quando o arquivo é inválido/ilegível; <see cref="System.Text.Json.JsonException"/>
        /// quando o mapa explícito é inválido. Para não lançar, use
        /// <see cref="TryParse(string, out MagicDllFile?, out string?)"/>.
        /// </summary>
        public MagicDllFile Parse(string path)
        {
            if (!TryParse(path, out MagicDllFile? file, out string? error))
                throw new InvalidDataException(error);
            return file!;
        }

        /// <summary>TryParse com as opções default.</summary>
        public bool TryParse(
            string path,
            [NotNullWhen(true)] out MagicDllFile? file,
            out string? error)
        {
            return TryParse(path, _options, out file, out error);
        }

        /// <summary>
        /// TryParse com opções customizadas. Nunca lança: erros de arquivo, PE ou mapa
        /// viram <paramref name="error"/> amigável e retorno false.
        /// </summary>
        public bool TryParse(
            string path,
            MagicDllParserOptions? options,
            [NotNullWhen(true)] out MagicDllFile? file,
            out string? error)
        {
            file = null;
            error = null;
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    error = string.Format(Strings.U_Bb_MagicDllNotFound, path);
                    return false;
                }

                // 1) PE + seção .data
                MagicDllFile dll = MagicDllFile.Load(path);

                IReadOnlyList<MagicDllRoot> roots = ResolveRoots(dll, options);

                file = MagicDllFile.Load(path, roots);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or InvalidDataException or System.Text.Json.JsonException
                or ArgumentOutOfRangeException or IndexOutOfRangeException)
            {
                error = $"Falha ao parsear {path}: {ex.Message}";
                return false;
            }
        }

        private static IReadOnlyList<MagicDllRoot> ResolveRoots(MagicDllFile dll, MagicDllParserOptions? options)
        {
            // 2) Local fp.h table, DLL-local names, then the embedded known-effect catalog.
            IReadOnlyDictionary<int, string>? fpNames = null;
            if (options?.FpHandlerTable != null)
            {
                fpNames = options.FpHandlerTable.Names;
            }
            else if (!string.IsNullOrWhiteSpace(options?.FpDirectoryPath))
            {
                fpNames = MagicFpHandlerTable.TryLoadFromMagicDirectory(
                    options.FpDirectoryPath, dll.MagicId)?.Names;
            }
            fpNames ??= MagicFpHandlerTable.TryExtractFromDll(dll)?.Names;
            fpNames ??= MagicKnownEffectHandlers.TryGet(dll.MagicId);

            // 3) Preserve explicit map priority and the existing automatic fallback contract.
            MagicFieldMap fieldMap = options?.FieldMap
                ?? MagicFieldMap.Load(options?.FieldMapPath);

            // 4) Decode the roots from the same owned .data used to resolve the DLL-local names.
            return MagicDllRoot.FindRoots(dll.Data, fieldMap, fpNames);
        }
    }
}
