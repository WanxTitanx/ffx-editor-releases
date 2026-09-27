using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using FFXProjectEditor.Diagnostics;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Services;

namespace FFXProjectEditor.Modules.MagicDllEditor
{
    /// <summary>
    /// Núcleo técnico do Magic DLL Editor (padrão MonsterStatSheet_Wrapper: DataModel orquestra,
    /// wrapper concentra parse/bytes). Responsabilidades:
    ///
    ///   1. Parse via <see cref="MagicDllParser"/> (PE + .data + roots + programs + slots + fields),
    ///      com fp.h local do efeito (Yonishi) quando acessível, senão catálogo embutido.
    ///   2. Working bytes em memória (o documento editável) — o arquivo original nunca é tocado.
    ///   3. RT0: re-parse → serialize byte-idêntico (SHA antes/depois iguais).
    ///   4. Write-back de campo CONFINADO à janela provada do schema (WRITEBACK_SPEC §1.2/§1.3).
    ///   5. Grow de record (adicionar campo) — receita WRITEBACK_SPEC §2.2 Estratégia A (shift
    ///      + ponteiros) com verificação pós-grow (re-parse + RT0 por record + diff de ponteiros).
    ///   6. Salvar cópia com backup .bak + SHA; restore hash-gated (padrão T3).
    ///
    /// Segurança: NUNCA escreve no jogo instalado (<see cref="IsSteamLibraryPath"/> bloqueia);
    /// grow = RISCO ALTO (pointer-trust) — somente em cópia, RT2 pendente (WRITEBACK_SPEC §3/§6).
    /// </summary>
    public sealed class MagicDllDocument_Wrapper
    {
        private MagicDllParser? _parser;
        private MagicFieldMap? _fieldMap;
        private MagicDllFile? _file;
        private byte[]? _originalBytes;
        private byte[]? _workingBytes;
        private string? _sourcePath;
        private string? _lastSavedTargetSha;
        private string? _lastSavedBackupSha;
        private FileSystemReparseGuard.FileIdentity? _lastSavedBackupIdentity;
        private Dictionary<string, MagicFamilySchema>? _writerSchemas;
        private HashSet<WriterRecordAnchor>? _writerRecordAnchors;
        private int _writerDataSectionRawPtr = -1;

        private readonly record struct WriterRecordAnchor(
            int RecordOffset,
            string OpcodeName,
            string RecordSha256);

        internal static string? StagingRootOverrideForTests { get; set; }

        /// <summary>Documento parseado (reconstruído após grow para refletir os working bytes).</summary>
        internal MagicDllFile? ParsedFile => _file;

        /// <summary>Mapa de famílias usado pelo parser (para resolver campos adicionais).</summary>
        internal MagicFieldMap? FieldMap => _fieldMap;

        /// <summary>Snapshot defensivo dos bytes atuais (null sem documento).</summary>
        public byte[]? WorkingBytes => _workingBytes == null ? null : (byte[])_workingBytes.Clone();

        /// <summary>Snapshot defensivo dos bytes originais (referência vanilla — âncora do diff).</summary>
        public byte[]? SourceBytes => _originalBytes == null ? null : (byte[])_originalBytes.Clone();

        /// <summary>Caminho do arquivo aberto.</summary>
        public string? SourcePath => _sourcePath;

        /// <summary>Id do efeito (21 para magic_0021.dll; -1 desconhecido).</summary>
        public int MagicId => _file?.MagicId ?? -1;

        /// <summary>Nome do arquivo ("magic_0021.dll").</summary>
        public string? DllName => _file?.DllName;

        /// <summary>SHA-256 (hex) dos bytes originais lidos do arquivo.</summary>
        public string ShaBefore { get; private set; } = string.Empty;

        /// <summary>SHA-256 (hex) dos working bytes atuais.</summary>
        public string ShaAfter { get; private set; } = string.Empty;

        /// <summary>Último caminho salvo via <see cref="TrySaveCopy"/> (para Reverter).</summary>
        public string? LastSavedPath { get; private set; }

        /// <summary>true only while this document owns a verified target/backup save pair.</summary>
        public bool HasRestorableBackup =>
            LastSavedPath != null &&
            _lastSavedTargetSha != null &&
            _lastSavedBackupSha != null &&
            _lastSavedBackupIdentity.HasValue;

        /// <summary>true quando os working bytes diferem do original (edições/grow em memória).</summary>
        public bool IsDirty =>
            _workingBytes != null && _originalBytes != null &&
            !_workingBytes.AsSpan().SequenceEqual(_originalBytes);

        /// <summary>true quando há um documento carregado.</summary>
        public bool HasDocument => _file != null && _workingBytes != null;

        // --- Carga -------------------------------------------------------------------

        /// <summary>
        /// Abre e parseia uma magic DLL. Nunca lança: falhas viram <paramref name="error"/>.
        /// </summary>
        public bool TryLoad(string path, out string error)
        {
            error = string.Empty;
            ClearDocument();
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
            {
                error = $"File not found: {path}";
                return false;
            }

            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                byte[] sourceBytes = System.IO.File.ReadAllBytes(path);
                if (!TryValidateMagicPe(sourceBytes, out error))
                {
                    DebugLog.Error(
                        "Magic.Parse",
                        $"TryLoad rejected {System.IO.Path.GetFileName(path)}: {error}");
                    return false;
                }
                // Product editing trusts only the reviewed embedded catalog. Runtime CWD/repository
                // research maps remain parser inputs for explicit developer tooling, never writers.
                _fieldMap = MagicFieldMap.LoadEmbedded();
                string? fpDir = FindYonishiFpDirectory();
                var options = fpDir != null
                    ? new MagicDllParserOptions { FieldMap = _fieldMap, FpDirectoryPath = fpDir }
                    : new MagicDllParserOptions { FieldMap = _fieldMap };

                var parser = new MagicDllParser(options);
                string logicalPath = Path.GetFullPath(path);
                if (!TryParseCapturedBytes(
                        parser,
                        sourceBytes,
                        logicalPath,
                        out MagicDllFile? file,
                        out string parseError))
                {
                    error = parseError;
                    DebugLog.Error("Magic.Parse", $"TryLoad failed for {System.IO.Path.GetFileName(path)}: {parseError}");
                    ClearDocument();
                    return false;
                }

                _parser = parser;
                _file = file;
                CaptureWriterTrustSnapshots(file!, _fieldMap!);
                _sourcePath = logicalPath;
                _originalBytes = (byte[])sourceBytes.Clone();
                _workingBytes = (byte[])sourceBytes.Clone();
                ShaBefore = ComputeSha256Hex(_originalBytes);
                ShaAfter = ShaBefore;
                sw.Stop();
                DebugLog.Info("Magic.Parse",
                    $"TryLoad OK {System.IO.Path.GetFileName(path)}: {file.Roots.Count} root(s), {file.Roots.Sum(r => r.Programs.Count)} program(s), {file.Roots.Sum(r => r.Programs.Sum(p => p.Slots.Count))} slot(s) in {sw.ElapsedMilliseconds}ms");
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                InvalidDataException or ArgumentException or NotSupportedException or
                System.Text.Json.JsonException or OverflowException)
            {
                error = $"Failed to read {path}: {ex.Message}";
                DebugLog.Error("Magic.Parse", $"TryLoad rejected {path}: {ex.Message}", ex);
                ClearDocument();
                return false;
            }
        }

        private void ClearDocument()
        {
            _parser = null;
            _fieldMap = null;
            _file = null;
            _originalBytes = null;
            _workingBytes = null;
            _sourcePath = null;
            ShaBefore = string.Empty;
            ShaAfter = string.Empty;
            LastSavedPath = null;
            _lastSavedTargetSha = null;
            _lastSavedBackupSha = null;
            _lastSavedBackupIdentity = null;
            _writerSchemas = null;
            _writerRecordAnchors = null;
            _writerDataSectionRawPtr = -1;
        }

        /// <summary>
        /// Deep-copies every writer decision input at load/reparse. UI and diagnostic consumers may
        /// inspect their parsed graph, but no later collection cast can forge or erase a write anchor.
        /// Assignments happen only after both snapshots are complete so a failed capture stays closed.
        /// </summary>
        private void CaptureWriterTrustSnapshots(MagicDllFile file, MagicFieldMap fieldMap)
        {
            var schemas = new Dictionary<string, MagicFamilySchema>(StringComparer.Ordinal);
            foreach ((string opcode, MagicFamilySchema schema) in fieldMap.Families)
            {
                schemas.Add(opcode, new MagicFamilySchema(
                    schema.Opcode,
                    schema.HandlerAddr,
                    schema.PayloadConsumer,
                    schema.Editable,
                    schema.Window,
                    schema.MatchWord,
                    schema.Guard,
                    schema.Fields.ToArray(),
                    schema.Status,
                    schema.Usage,
                    schema.RawWidthYonishi,
                    schema.WidthReconciled,
                    schema.SemanticsCategory,
                    schema.RealFunc));
            }

            HashSet<WriterRecordAnchor> anchors = BuildWriterRecordAnchors(file);

            _writerSchemas = schemas;
            _writerRecordAnchors = anchors;
            _writerDataSectionRawPtr = file.DataSectionRawPtr;
        }

        private static HashSet<WriterRecordAnchor> BuildWriterRecordAnchors(MagicDllFile file)
        {
            var anchors = new HashSet<WriterRecordAnchor>();
            foreach (MagicSlot slot in file.Roots
                .SelectMany(root => root.Programs)
                .SelectMany(program => program.Slots))
            {
                if (slot.OpcodeName != null)
                    anchors.Add(new WriterRecordAnchor(
                        slot.RecordOffset,
                        slot.OpcodeName,
                        slot.RecordSha256.ToUpperInvariant()));
            }
            return anchors;
        }

        /// <summary>
        /// Validates the native ABI and both host-facing exports before domain parsing. Magic DLLs
        /// are x86 PE32 libraries; accepting another PE shape would make later offsets untrusted.
        /// </summary>
        private static bool TryValidateMagicPe(byte[] bytes, out string error)
        {
            error = string.Empty;
            const ushort DosMagic = 0x5A4D;
            const uint PeMagic = 0x00004550;
            const ushort MachineI386 = 0x014C;
            const ushort OptionalMagicPe32 = 0x010B;
            const ushort FileDll = 0x2000;

            if (!TryReadU16(bytes, 0, out ushort dos) || dos != DosMagic ||
                !TryReadI32(bytes, 0x3C, out int peOffset) || peOffset <= 0 ||
                !TryReadU32(bytes, peOffset, out uint pe) || pe != PeMagic)
            {
                error = "MZ/PE signature missing — not a valid PE DLL.";
                return false;
            }

            int coff = peOffset + 4;
            if (!TryReadU16(bytes, coff, out ushort machine) || machine != MachineI386)
            {
                error = "Magic DLL must be an x86 (I386) PE.";
                return false;
            }
            if (!TryReadU16(bytes, coff + 2, out ushort sectionCount) ||
                sectionCount is 0 or > 96 ||
                !TryReadU16(bytes, coff + 16, out ushort optionalSize) ||
                !TryReadU16(bytes, coff + 18, out ushort characteristics) ||
                (characteristics & FileDll) == 0)
            {
                error = "Invalid PE section table or DLL characteristics.";
                return false;
            }

            int optional = coff + 20;
            if (optionalSize < 104 || !TryReadU16(bytes, optional, out ushort optionalMagic) ||
                optionalMagic != OptionalMagicPe32)
            {
                error = "Magic DLL must use the PE32 optional header.";
                return false;
            }

            int sectionTable = optional + optionalSize;
            if ((long)sectionTable + sectionCount * 40L > bytes.Length ||
                !TryReadU32(bytes, optional + 92, out uint directoryCount) || directoryCount < 1 ||
                !TryReadU32(bytes, optional + 96, out uint exportRva) || exportRva == 0 ||
                !TryReadU32(bytes, optional + 100, out uint exportSize) || exportSize < 40)
            {
                error = "PE export directory is missing or out of bounds.";
                return false;
            }

            var sections = new List<(
                uint Rva,
                uint VirtualSize,
                uint RawSize,
                uint RawOffset,
                uint Characteristics)>(sectionCount);
            for (int index = 0; index < sectionCount; index++)
            {
                int entry = sectionTable + index * 40;
                if (!TryReadU32(bytes, entry + 8, out uint virtualSize) ||
                    !TryReadU32(bytes, entry + 12, out uint rva) ||
                    !TryReadU32(bytes, entry + 16, out uint rawSize) ||
                    !TryReadU32(bytes, entry + 20, out uint rawOffset) ||
                    !TryReadU32(bytes, entry + 36, out uint sectionCharacteristics) ||
                    (rawSize > 0 && (ulong)rawOffset + rawSize > (ulong)bytes.Length))
                {
                    error = "PE section table contains an out-of-bounds raw range.";
                    return false;
                }
                sections.Add((rva, virtualSize, rawSize, rawOffset, sectionCharacteristics));
            }

            if (!TryRvaToFileRange(exportRva, 40, sections, bytes.Length, out int exportOffset, out _) ||
                !TryReadU32(bytes, exportOffset + 20, out uint functionCount) ||
                functionCount is 0 or > 65536 ||
                !TryReadU32(bytes, exportOffset + 24, out uint nameCount) ||
                nameCount is 0 or > 65536 ||
                !TryReadU32(bytes, exportOffset + 28, out uint functionTableRva) ||
                !TryRvaToFileRange(
                    functionTableRva,
                    (ulong)functionCount * 4,
                    sections,
                    bytes.Length,
                    out int functionTableOffset,
                    out _) ||
                !TryReadU32(bytes, exportOffset + 32, out uint nameTableRva) ||
                !TryRvaToFileRange(
                    nameTableRva,
                    (ulong)nameCount * 4,
                    sections,
                    bytes.Length,
                    out int nameTableOffset,
                    out _) ||
                !TryReadU32(bytes, exportOffset + 36, out uint ordinalTableRva) ||
                !TryRvaToFileRange(
                    ordinalTableRva,
                    (ulong)nameCount * 2,
                    sections,
                    bytes.Length,
                    out int ordinalTableOffset,
                    out _))
            {
                error = "PE export name table is malformed.";
                return false;
            }

            bool hasInit = false;
            bool hasOverlayTable = false;
            for (int index = 0; index < (int)nameCount; index++)
            {
                if (!TryReadU32(bytes, nameTableOffset + index * 4, out uint nameRva) ||
                    !TryRvaToFileRange(
                        nameRva,
                        1,
                        sections,
                        bytes.Length,
                        out int nameOffset,
                        out int nameSectionEnd) ||
                    !TryReadAsciiZ(
                        bytes,
                        nameOffset,
                        Math.Min(256, nameSectionEnd - nameOffset),
                        out string exportName) ||
                    !TryReadU16(bytes, ordinalTableOffset + index * 2, out ushort ordinal) ||
                    ordinal >= functionCount)
                {
                    error = "PE export name or ordinal is malformed.";
                    return false;
                }
                bool isInit = string.Equals(exportName, "InitMagicPRX", StringComparison.Ordinal);
                bool isOverlayTable = string.Equals(
                    exportName,
                    "GetEffectOverlayTable",
                    StringComparison.Ordinal);
                if ((isInit || isOverlayTable) &&
                    (!TryReadU32(bytes, functionTableOffset + ordinal * 4, out uint functionRva) ||
                     functionRva == 0 ||
                     ((ulong)functionRva >= exportRva &&
                      (ulong)functionRva < (ulong)exportRva + exportSize) ||
                     !IsFileBackedExecutableRva(functionRva, sections, bytes.Length)))
                {
                    error = $"Required Magic DLL export {exportName} has no valid function target.";
                    return false;
                }
                hasInit |= isInit;
                hasOverlayTable |= isOverlayTable;
            }

            if (!hasInit || !hasOverlayTable)
            {
                var missing = new List<string>(2);
                if (!hasInit) missing.Add("InitMagicPRX");
                if (!hasOverlayTable) missing.Add("GetEffectOverlayTable");
                error = $"Required Magic DLL export(s) missing: {string.Join(", ", missing)}.";
                return false;
            }
            return true;
        }

        private static bool TryRvaToFileOffset(
            uint rva,
            IReadOnlyList<(
                uint Rva,
                uint VirtualSize,
                uint RawSize,
                uint RawOffset,
                uint Characteristics)> sections,
            int fileLength,
            out int fileOffset)
        {
            return TryRvaToFileRange(
                rva,
                1,
                sections,
                fileLength,
                out fileOffset,
                out _);
        }

        private static bool TryRvaToFileRange(
            uint rva,
            ulong length,
            IReadOnlyList<(
                uint Rva,
                uint VirtualSize,
                uint RawSize,
                uint RawOffset,
                uint Characteristics)> sections,
            int fileLength,
            out int fileOffset,
            out int sectionFileEnd)
        {
            foreach (var section in sections)
            {
                uint span = Math.Max(section.VirtualSize, section.RawSize);
                if (rva < section.Rva || (ulong)rva >= (ulong)section.Rva + span)
                    continue;
                ulong delta = (ulong)rva - section.Rva;
                if (length == 0 ||
                    delta >= section.RawSize ||
                    delta + length > section.RawSize ||
                    (ulong)section.RawOffset + delta + length > (ulong)fileLength)
                    break;
                fileOffset = checked((int)((ulong)section.RawOffset + delta));
                sectionFileEnd = checked((int)((ulong)section.RawOffset + section.RawSize));
                return true;
            }
            fileOffset = -1;
            sectionFileEnd = -1;
            return false;
        }

        private static bool IsFileBackedExecutableRva(
            uint rva,
            IReadOnlyList<(
                uint Rva,
                uint VirtualSize,
                uint RawSize,
                uint RawOffset,
                uint Characteristics)> sections,
            int fileLength)
        {
            const uint ImageScnMemExecute = 0x20000000;
            foreach (var section in sections)
            {
                uint span = Math.Max(section.VirtualSize, section.RawSize);
                if (rva < section.Rva || (ulong)rva >= (ulong)section.Rva + span)
                    continue;
                uint delta = rva - section.Rva;
                return (section.Characteristics & ImageScnMemExecute) != 0 &&
                    delta < section.RawSize &&
                    (ulong)section.RawOffset + delta < (ulong)fileLength;
            }
            return false;
        }

        private static bool TryReadAsciiZ(
            byte[] bytes,
            int offset,
            int maximumLength,
            out string value)
        {
            value = string.Empty;
            if (offset < 0 || offset >= bytes.Length)
                return false;
            int end = offset;
            int maximumEnd = (int)Math.Min(bytes.LongLength, (long)offset + maximumLength);
            while (end < maximumEnd && bytes[end] != 0)
            {
                if (bytes[end] is < 0x20 or > 0x7E)
                    return false;
                end++;
            }
            if (end == offset || end >= maximumEnd || bytes[end] != 0)
                return false;
            value = Encoding.ASCII.GetString(bytes, offset, end - offset);
            return true;
        }

        /// <summary>
        /// Re-parses captured bytes without exposing a mutable parser pathname. Windows retains
        /// its pinned LocalAppData staging lease; Linux parses an owned byte snapshot directly.
        /// This ownership begins after capture and does not imply mandatory Linux file locks.
        /// </summary>
        private bool TryParseBytes(byte[] bytes, out MagicDllFile? file, out string error)
        {
            file = null;
            error = string.Empty;
            if (_parser == null)
            {
                error = "Parser not initialized (load a document first).";
                return false;
            }
            string logicalPath = _sourcePath ?? _file?.SourcePath ?? $"magic_{MagicId:D4}.dll";
            return TryParseCapturedBytes(_parser, bytes, logicalPath, out file, out error);
        }

        private static bool TryParseCapturedBytes(
            MagicDllParser parser,
            byte[] bytes,
            string logicalPath,
            out MagicDllFile? file,
            out string error)
        {
            file = null;
            error = string.Empty;
            if (OperatingSystem.IsLinux())
                return TryParseLinuxSnapshot(parser, bytes, logicalPath, out file, out error);
            string stagingRoot = StagingRootOverrideForTests ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FFXProjectEditor",
                "MagicDll",
                "Staging");
            int logicalId = MagicDllFile.ParseMagicIdFromName(logicalPath);
            string idSuffix = logicalId >= 0 ? logicalId.ToString("D4", CultureInfo.InvariantCulture) : "xxxx";
            string stagingLeaf = $"magic_staging_{Guid.NewGuid():N}_{idSuffix}.dll";
            FileSystemReparseGuard.VerifiedDirectory? directory = null;
            FileSystemReparseGuard.VerifiedReadFile? lease = null;
            FileSystemReparseGuard.FileIdentity createdIdentity = default;
            bool created = false;
            bool success = false;
            try
            {
                directory = FileSystemReparseGuard.OpenOrCreateVerifiedStableDirectory(stagingRoot);
                using (FileStream staging = FileSystemReparseGuard.CreateNewVerifiedFile(
                           directory,
                           stagingLeaf,
                           out createdIdentity))
                {
                    created = true;
                    staging.Write(bytes, 0, bytes.Length);
                    staging.Flush(flushToDisk: true);
                }

                FileSystemReparseGuard.VerifiedOpenResult opened =
                    FileSystemReparseGuard.TryOpenVerifiedStagingRead(directory, stagingLeaf, out lease);
                if (opened != FileSystemReparseGuard.VerifiedOpenResult.Success ||
                    lease == null || lease.Identity != createdIdentity)
                {
                    error = "The immutable Magic parser snapshot changed before it could be pinned.";
                    return false;
                }

                string stagingPath = Path.Combine(directory.FullPath, stagingLeaf);
                FileSystemReparseGuard.BeforeHandleOperationForTests?.Invoke(
                    "staging-before-parse",
                    stagingPath);
                bool parsed;
                MagicDllFile? staged;
                string? parseError;
                try
                {
                    parsed = parser.TryParse(stagingPath, out staged, out parseError);
                }
                finally
                {
                    FileSystemReparseGuard.BeforeHandleOperationForTests?.Invoke(
                        "staging-after-parse",
                        stagingPath);
                }
                if (!parsed || staged == null)
                {
                    error = parseError ?? "The immutable Magic parser snapshot was rejected.";
                    return false;
                }
                if (!staged.FileBytes.AsSpan().SequenceEqual(bytes))
                {
                    error = "The immutable Magic parser snapshot did not match the captured bytes.";
                    return false;
                }

                file = new MagicDllFile(
                    Path.GetFullPath(logicalPath),
                    (byte[])bytes.Clone(),
                    (byte[])staged.Data.Clone(),
                    staged.DataSectionRawPtr,
                    staged.DataSectionSize,
                    staged.DataSectionRva,
                    staged.Sections,
                    staged.Roots);
                success = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                InvalidDataException or ArgumentException or NotSupportedException)
            {
                error = ex.Message;
            }
            finally
            {
                lease?.Dispose();
                if (created && directory != null &&
                    !FileSystemReparseGuard.TryDeleteVerifiedFile(
                        directory,
                        stagingLeaf,
                        createdIdentity) && success)
                {
                    success = false;
                    file = null;
                    error = "The immutable Magic parser snapshot could not be deleted by its captured identity.";
                }
                directory?.Dispose();
            }
            return success;
        }

        // ── Linux snapshot consumption without filesystem staging ──
        // The source label remains useful for identity/diagnostics but is never opened by this route.
        // MAINT: do not reuse Windows staging hook names or claim that Linux has sharing-mode locks.
        private static bool TryParseLinuxSnapshot(MagicDllParser parser, byte[] bytes, string logicalPath,
            out MagicDllFile? file, out string error)
        {
            file = null;
            error = string.Empty;
            try
            {
                string label = Path.GetFullPath(logicalPath);
                bool parsed;
                MagicDllFile? captured;
                string? parseError;
                try
                {
                    FileSystemReparseGuard.BeforeHandleOperationForTests?.Invoke("snapshot-before-parse", label);
                    parsed = parser.TryParseSnapshot(bytes, label, out captured, out parseError);
                }
                finally
                {
                    FileSystemReparseGuard.BeforeHandleOperationForTests?.Invoke("snapshot-after-parse", label);
                }
                if (!parsed || captured == null)
                {
                    error = parseError ?? "The immutable Magic parser snapshot was rejected.";
                    return false;
                }
                if (!captured.FileBytes.AsSpan().SequenceEqual(bytes))
                {
                    error = "The immutable Magic parser snapshot did not match the captured bytes.";
                    return false;
                }
                file = captured;
                DebugLog.Info("Magic.Parse", "Parsed the owned Linux byte snapshot without filesystem staging.");
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                InvalidDataException or ArgumentException or NotSupportedException)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Resolve TODOS os campos do schema da família de um slot (o parser resolve apenas
        /// o primeiro). Usado para popular os nós de campo da TreeView.
        /// </summary>
        public IReadOnlyList<MagicField> ResolveAllFields(MagicSlot slot)
        {
            if (slot.OpcodeName == null || _fieldMap == null ||
                !_fieldMap.TryGet(slot.OpcodeName, out MagicFamilySchema? schema))
            {
                return Array.Empty<MagicField>();
            }

            var fields = new List<MagicField>(schema.Fields.Count);
            foreach (MagicFieldSpec spec in schema.Fields)
            {
                fields.Add(MagicField.CreateResolved(
                    spec, slot.OpcodeName, slot.Record, slot.RecordOffset, slot.RecordSha256));
            }
            return fields;
        }

        // --- RT0 ---------------------------------------------------------------------

        /// <summary>
        /// Gate RT0 (WRITEBACK_SPEC §4.1): re-parse dos bytes atuais (sem edição) via parser
        /// e re-serialize — o SHA do resultado deve ser idêntico ao original. Prova que o
        /// pipeline parse→serialize é byte-idêntico (abrir→salvar sem editar = SHA igual).
        /// </summary>
        public bool Rt0Check(out string message)
        {
            message = string.Empty;
            if (!HasDocument)
            {
                message = "RT0: no document loaded.";
                return false;
            }

            if (IsDirty)
            {
                message = "RT0: there are edits/grow in memory — the gate validates the clean pipeline; revert first.";
                return false;
            }

            if (!TryParseBytes(_workingBytes!, out MagicDllFile? reparsed, out string error))
            {
                message = $"RT0 FAILED: re-parse of the unedited file failed — {error}";
                return false;
            }

            string shaAfter = ComputeSha256Hex(reparsed!.Serialize());
            bool pass = string.Equals(shaAfter, ShaBefore, StringComparison.OrdinalIgnoreCase);
            message = pass
                ? string.Format(Strings.U_Md_Rt0Pass, ShaBefore[..Math.Min(16, ShaBefore.Length)])
                : string.Format(Strings.U_Md_Rt0Fail, ShaBefore[..Math.Min(16, ShaBefore.Length)], shaAfter[..Math.Min(16, shaAfter.Length)]);
            return pass;
        }

        // --- Write-back de campo (janela provada) ------------------------------------

        /// <summary>
        /// Aplica a edição de um campo nos working bytes. Confinado à JANELA provada do
        /// schema (WRITEBACK_SPEC §1.2): campo fora da janela é rejeitado; escrita no
        /// prefixo [+0,+8) do record (match word) é bloqueada (R6). Diff do arquivo =
        /// somente os bytes do campo.
        /// </summary>
        public bool TryApplyFieldEdit(MagicField field, byte[] newValueBytes, out string error)
        {
            error = string.Empty;
            if (!HasDocument || _writerSchemas == null || _writerRecordAnchors == null)
            {
                error = Strings.F2_no_document_loaded_3bc35785;
                return false;
            }
            if (field.OpcodeName == null ||
                !_writerSchemas.TryGetValue(field.OpcodeName, out MagicFamilySchema? schema))
            {
                error = $"Field without a catalogued schema: {field.Name}.";
                return false;
            }
            if (!schema.PayloadConsumer || !schema.Editable)
            {
                error = $"Family {field.OpcodeName} is not an editable payload consumer.";
                return false;
            }
            if (schema.Window is not { } win)
            {
                error = $"Family {field.OpcodeName} has no runtime window — field not editable.";
                return false;
            }

            // R6: bloqueia escrita no prefixo do record (+0..+7 — match word/estado).
            if (field.Offset < 8)
            {
                error = $"Field {field.Name} at +0x{field.Offset:X2}: writing to the prefix [+0,+8) is blocked (match word — WRITEBACK_SPEC R6).";
                return false;
            }
            // Janela: campo contido na janela provada do schema.
            if (!IsSchemaFieldWritable(schema, field.Offset, field.Width))
            {
                error = $"Field {field.Name} outside the record window [{win.Start}..{win.Start + win.Width}) — editable only inside the proven window.";
                return false;
            }
            bool fieldDeclared = schema.Fields.Any(spec =>
                string.Equals(spec.Name, field.Name, StringComparison.Ordinal) &&
                spec.Offset == field.Offset && spec.Width == field.Width && spec.Type == field.Type);
            bool anchorKnown = _writerRecordAnchors.Contains(new WriterRecordAnchor(
                field.RecordOffset,
                field.OpcodeName,
                field.RecordSha256.ToUpperInvariant()));
            if (!fieldDeclared || !anchorKnown)
            {
                error = $"Field {field.Name} is not an exact schema-declared field on this document anchor.";
                return false;
            }
            if (newValueBytes.Length != field.Width)
            {
                error = "Value bytes do not match the field width.";
                return false;
            }

            int fileOffset = _writerDataSectionRawPtr + field.RecordOffset + field.Offset;
            if (fileOffset < 0 || fileOffset + field.Width > _workingBytes!.Length)
            {
                error = "Field out of file bounds.";
                return false;
            }

            Buffer.BlockCopy(newValueBytes, 0, _workingBytes, fileOffset, field.Width);

            // Verificação pós-escrita (re-leitura confere o valor escrito).
            byte[] check = _workingBytes.AsSpan(fileOffset, field.Width).ToArray();
            if (!check.AsSpan().SequenceEqual(newValueBytes))
            {
                error = "Internal post-write verification failure.";
                return false;
            }

            ShaAfter = ComputeSha256Hex(_workingBytes);
            return true;
        }

        /// <summary>
        /// Shared UI/writer gate for a field declared by the embedded family schema. A field is
        /// writable only when all provenance flags are positive and its complete byte range is
        /// inside the proven runtime window. Long arithmetic keeps malformed offsets fail-closed.
        /// </summary>
        internal static bool IsSchemaFieldWritable(
            MagicFamilySchema? schema,
            int offset,
            int width)
        {
            if (schema is not { PayloadConsumer: true, Editable: true, Window: { } window } ||
                offset < 8 || width <= 0 || window.Start < 0 || window.Width <= 0)
                return false;

            long fieldEnd = (long)offset + width;
            long windowEnd = (long)window.Start + window.Width;
            return offset >= window.Start && fieldEnd <= windowEnd;
        }

        // --- Salvar cópia / backup / restore -----------------------------------------

        /// <summary>
        /// Salva os working bytes numa CÓPIA escolhida pelo usuário. Nunca sobre o jogo
        /// instalado (<see cref="IsInstalledGamePath"/> bloqueia). Se o destino já existe,
        /// gera backup <c>.bak</c> ao lado (padrão T3) e atualiza SHA depois.
        /// </summary>
        public bool TrySaveCopy(string destPath, out string error)
        {
            error = string.Empty;
            if (!HasDocument)
            {
                error = Strings.F2_no_document_loaded_3bc35785;
                return false;
            }
            if (string.IsNullOrWhiteSpace(destPath))
            {
                error = "Destination path is empty.";
                return false;
            }
            if (IsInstalledGamePath(destPath))
            {
                error = Strings.F2_destination_in_steam_library_blocked_sav_0e40284e;
                return false;
            }

            // Native Linux save-copy goes through the exact-descriptor owned-output layer; the Win32
            // verified handles below do not exist there. Same contract: backup the existing file
            // (or the original bytes) as .bak, publish the working bytes, verify exact bytes.
            if (OperatingSystem.IsLinux())
            {
                try
                {
                    string fullDestination = Path.GetFullPath(destPath);
                    if (_sourcePath != null && PathsEqual(fullDestination, _sourcePath))
                    {
                        error = "The opened source DLL is read-only; save to a separate copy.";
                        return false;
                    }
                    string outputDirectory = Path.GetDirectoryName(fullDestination) ??
                        throw new IOException("Destination directory is unavailable.");
                    string destinationName = Path.GetFileName(fullDestination);
                    using var output = LinuxOwnedOutputDirectory.OpenOrCreate(outputDirectory);
                    using var writeLock = output.AcquireWriteLock(TimeSpan.FromSeconds(10));
                    byte[] backupBytes = output.ReadSnapshot(destinationName)?.Bytes.ToArray()
                        ?? _originalBytes
                        ?? throw new IOException("Original document bytes are unavailable for the copy backup.");
                    string bakName = destinationName + ".bak";
                    LinuxOwnedOutputDirectory.Snapshot? bakExisting = output.ReadSnapshot(bakName);
                    LinuxOwnedOutputDirectory.Snapshot? target = output.ReadSnapshot(destinationName);
                    if (target != null)
                    {
                        if (bakExisting != null)
                        {
                            // RENAME_EXCHANGE semantics: the fresh retention leaf receives the old
                            // bak content while bakName receives the current destination content.
                            // A unique temp name is required each pass (linkat is create-only).
                            string tmp = bakName + ".tmp-" + Guid.NewGuid().ToString("N");
                            output.ReplaceRetainingDisplaced(bakName, bakExisting, tmp, target.Bytes);
                        }
                        // Now bakName carries the current destination content; the destination
                        // receives the working bytes and its old content moves into the temp leaf.
                        string tmp2 = destinationName + ".tmp-" + Guid.NewGuid().ToString("N");
                        output.ReplaceRetainingDisplaced(destinationName, target, tmp2, _workingBytes!);
                    }
                    else
                    {
                        output.PublishNew(bakName, backupBytes);
                        output.PublishNew(destinationName, _workingBytes!);
                    }
                    var saved = output.ReadSnapshot(destinationName)
                        ?? throw new IOException("The save copy failed: destination is absent after publish.");
                    if (!saved.Bytes.SequenceEqual(_workingBytes!))
                        throw new IOException("The save copy failed its exact-byte verification.");
                    ShaAfter = ComputeSha256Hex(_workingBytes!);
                    LastSavedPath = fullDestination;
                    _lastSavedTargetSha = ShaAfter;
                    _lastSavedBackupSha = ComputeSha256Hex(backupBytes);
                    _lastSavedBackupIdentity = output.ReadSnapshot(destinationName + ".bak")!.Identity;
                    return true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    error = $"Failed to save {destPath}: {ex.Message}";
                    return false;
                }
            }

            FileSystemReparseGuard.VerifiedReadFile? existingLease = null;
            FileSystemReparseGuard.VerifiedReadFile? backupLease = null;
            try
            {
                string fullDestination = Path.GetFullPath(destPath);
                if (_sourcePath != null && PathsEqual(fullDestination, _sourcePath))
                {
                    error = "The opened source DLL is read-only; save to a separate copy.";
                    return false;
                }

                string outputDirectory = Path.GetDirectoryName(fullDestination) ??
                    throw new IOException("Destination directory is unavailable.");
                string destinationName = Path.GetFileName(fullDestination);
                using FileSystemReparseGuard.VerifiedDirectory directory =
                    FileSystemReparseGuard.OpenOrCreateVerifiedStableDirectory(outputDirectory);
                using IDisposable writeLock = AcquireWriteLock(directory);

                byte[] backupBytes;
                FileSystemReparseGuard.VerifiedOpenResult existing = ReadVerifiedMutationFile(
                    directory,
                    destinationName,
                    out byte[]? existingBytes,
                    out FileSystemReparseGuard.FileIdentity existingIdentity,
                    out existingLease);
                if (existing == FileSystemReparseGuard.VerifiedOpenResult.Rejected)
                    throw new IOException("The destination did not resolve to a regular file in its selected directory.");
                if (existing == FileSystemReparseGuard.VerifiedOpenResult.Success)
                    backupBytes = existingBytes!;
                else if (_originalBytes != null)
                {
                    backupBytes = _originalBytes;
                }
                else
                {
                    throw new IOException("Original document bytes are unavailable for the copy backup.");
                }

                string backupName = destinationName + ".bak";
                FileSystemReparseGuard.VerifiedOpenResult backupState = ReadVerifiedMutationFile(
                    directory,
                    backupName,
                    out _,
                    out FileSystemReparseGuard.FileIdentity backupIdentity,
                    out backupLease);
                if (backupState == FileSystemReparseGuard.VerifiedOpenResult.Rejected)
                    throw new IOException("The backup leaf was rejected by the selected directory capability.");
                if (existing == FileSystemReparseGuard.VerifiedOpenResult.Success &&
                    !FileSystemReparseGuard.IsOpenedFileIdentityCurrent(
                        directory,
                        destinationName,
                        existingIdentity))
                    throw new IOException("The destination changed before its backup was promoted.");

                FileSystemReparseGuard.FileIdentity promotedBackupIdentity = WriteVerifiedReplace(
                    directory,
                    backupName,
                    backupBytes,
                    backupState,
                    backupIdentity);
                _ = WriteVerifiedReplace(
                    directory,
                    destinationName,
                    _workingBytes!,
                    existing,
                    existingIdentity);
                ShaAfter = ComputeSha256Hex(_workingBytes!);
                LastSavedPath = fullDestination;
                _lastSavedTargetSha = ShaAfter;
                _lastSavedBackupSha = ComputeSha256Hex(backupBytes);
                _lastSavedBackupIdentity = promotedBackupIdentity;
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error = $"Failed to save {destPath}: {ex.Message}";
                return false;
            }
            finally
            {
                backupLease?.Dispose();
                existingLease?.Dispose();
            }
        }

        /// <summary>
        /// Clona a magic DLL atual para um novo arquivo com um novo id (FASE 1 — clonagem).
        /// Copia os working bytes (com as edições aplicadas) para <c>magic_&lt;newId&gt;.dll</c>.
        /// O id é derivado do nome do destino (mesma regra do ParseMagicIdFromName). Não altera
        /// o documento aberto. The clone is create-only: an occupied file or logical Magic id is
        /// refused and is never converted into an implicit overwrite target.
        /// </summary>
        public bool TryCloneMagic(string destPath, out string error)
        {
            error = string.Empty;
            if (!HasDocument)
            {
                error = Strings.F2_no_document_loaded_3bc35785;
                return false;
            }
            if (string.IsNullOrWhiteSpace(destPath))
            {
                error = "Destination path is empty.";
                return false;
            }
            if (IsInstalledGamePath(destPath))
            {
                error = Strings.F2_destination_in_steam_library_blocked_sav_0e40284e;
                return false;
            }
            if (!TryParseCanonicalCloneName(destPath, out int newId))
            {
                error = $"Invalid clone filename: {destPath} — use exactly magic_0000.dll through magic_9999.dll.";
                return false;
            }
            if (MagicId < 0)
            {
                error = "Clone refused: the opened source has no provable canonical Magic identity.";
                return false;
            }

            try
            {
                string fullDestination = Path.GetFullPath(destPath);
                string outputDirectory = Path.GetDirectoryName(fullDestination) ??
                    throw new IOException("Clone directory is unavailable.");
                string destinationName = Path.GetFileName(fullDestination);

                // Fase 3 (RE 2026-08-04): reescreve as strings UTF-16LE "magic_<oldId>" /
                // "magic_<oldId>.dll" embutidas no .data (3 ocorrências em magic_0140.dll vanilla)
                // para o novo id — alinha as strings internas com o nome do arquivo do clone.
                // Safe (mesma length, 4 dígitos zero-padded) — não desloca offsets do .data.
                byte[] cloneBytes = (byte[])_workingBytes!.Clone();
                if (HasDocument && MagicId >= 0 && newId != MagicId)
                {
                    try
                    {
                        cloneBytes = FFXProjectEditor.FfxLib.Magic.MagicDllNameRewriter.RewriteMagicNameStrings(
                            cloneBytes, MagicId, newId);
                    }
                    catch (Exception rex)
                    {
                        error = $"Clone name rewrite failed: {rex.Message}";
                        DebugLog.Error("Magic.Writer", error, rex);
                        return false;
                    }
                }

                // Native create-only output shares validation/bytes above, never restore ownership.
                if (OperatingSystem.IsLinux())
                {
                    LinuxMagicCloneOutput.Publish(outputDirectory, destinationName, newId, cloneBytes);
                    return true;
                }

                using FileSystemReparseGuard.VerifiedDirectory directory =
                    FileSystemReparseGuard.OpenOrCreateVerifiedStableDirectory(outputDirectory);
                using IDisposable writeLock = AcquireWriteLock(directory);
                string? collision = FindExistingMagicIdDll(directory, newId);
                if (collision != null)
                {
                    error = $"Magic id {newId} already exists at {collision}.";
                    return false;
                }

                WriteVerifiedCreateOnly(directory, destinationName, cloneBytes);
                FileSystemReparseGuard.VerifiedOpenResult written = ReadVerifiedFile(
                    directory,
                    destinationName,
                    out byte[]? writtenBytes,
                    out _);
                if (written != FileSystemReparseGuard.VerifiedOpenResult.Success ||
                    writtenBytes == null || !writtenBytes.AsSpan().SequenceEqual(cloneBytes))
                {
                    throw new IOException("The create-only clone failed its exact-byte verification.");
                }

                DebugLog.Info("Magic.Writer",
                    $"TryCloneMagic OK: {System.IO.Path.GetFileName(destPath)} (id {newId}) from {DllName} — {cloneBytes.Length} bytes");
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error = $"Failed to clone {destPath}: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Restores the promoted <c>.bak</c> only while both target and backup still match the
        /// immutable hash/identity provenance captured by the last successful save-copy pair.
        /// </summary>
        public bool TryRestoreBackup(string targetPath, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(targetPath) || !HasRestorableBackup)
            {
                error = "Restore is available only for the last copy written by this document.";
                return false;
            }
            string fullTarget;
            try { fullTarget = Path.GetFullPath(targetPath); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                error = $"Invalid restore target: {ex.Message}";
                return false;
            }
            if (!PathsEqual(fullTarget, LastSavedPath) ||
                (_sourcePath != null && PathsEqual(fullTarget, _sourcePath)) ||
                IsInstalledGamePath(fullTarget))
            {
                error = "Restore target is not the app-written copy owned by this document.";
                return false;
            }

            // Native Linux restore goes through the exact-descriptor owned-output layer; the Win32
            // verified handles below do not exist there. Same hash-gated contract: verify the current
            // target SHA and the backup provenance before swapping, verify exact bytes after.
            if (OperatingSystem.IsLinux())
            {
                try
                {
                    string outputDirectory = Path.GetDirectoryName(fullTarget) ??
                        throw new IOException("Restore directory is unavailable.");
                    string targetName = Path.GetFileName(fullTarget);
                    using var output = LinuxOwnedOutputDirectory.OpenOrCreate(outputDirectory);
                    using var writeLock = output.AcquireWriteLock(TimeSpan.FromSeconds(10));
                    var current = output.ReadSnapshot(targetName)
                        ?? throw new IOException($"Target file not found or rejected: {fullTarget}");
                    if (!string.Equals(ComputeSha256Hex(current.Bytes.ToArray()), _lastSavedTargetSha, StringComparison.OrdinalIgnoreCase))
                    {
                        error = "Restore aborted: current target SHA does not match expected (hash-gated).";
                        return false;
                    }
                    var backup = output.ReadSnapshot(targetName + ".bak")
                        ?? throw new IOException($"Backup not found or rejected: {fullTarget}.bak");
                    if (backup.Identity != _lastSavedBackupIdentity!.Value ||
                        !string.Equals(ComputeSha256Hex(backup.Bytes.ToArray()), _lastSavedBackupSha, StringComparison.OrdinalIgnoreCase))
                    {
                        error = "Restore aborted: backup provenance no longer matches the promoted save pair.";
                        return false;
                    }
                    string retention = targetName + ".displaced-" + Guid.NewGuid().ToString("N");
                    output.ReplaceRetainingDisplaced(targetName, current, retention, backup.Bytes);
                    var restored = output.ReadSnapshot(targetName)
                        ?? throw new IOException("Restore failed: target absent after swap.");
                    if (!restored.Bytes.SequenceEqual(backup.Bytes))
                        throw new IOException("Restore failed its exact-byte verification.");
                    // The save/restore pair is consumed exactly once: drop the backup leaf and the
                    // in-memory provenance so HasRestorableBackup turns false, mirroring Windows.
                    LinuxOwnedOutputDirectory.Snapshot? consumed = output.ReadSnapshot(targetName + ".bak");
                    if (consumed != null)
                    {
                        string bakRetention = targetName + ".bak.displaced-" + Guid.NewGuid().ToString("N");
                        output.ReplaceRetainingDisplaced(targetName + ".bak", consumed, bakRetention, Array.Empty<byte>());
                    }
                    _lastSavedTargetSha = null;
                    _lastSavedBackupSha = null;
                    _lastSavedBackupIdentity = null;
                    return true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    error = $"Failed to restore {targetPath}: {ex.Message}";
                    return false;
                }
            }

            FileSystemReparseGuard.VerifiedReadFile? currentLease = null;
            FileSystemReparseGuard.VerifiedReadFile? backupLease = null;
            try
            {
                string outputDirectory = Path.GetDirectoryName(fullTarget) ??
                    throw new IOException("Restore directory is unavailable.");
                string targetName = Path.GetFileName(fullTarget);
                string backupPath = fullTarget + ".bak";
                using FileSystemReparseGuard.VerifiedDirectory directory =
                    FileSystemReparseGuard.OpenOrCreateVerifiedStableDirectory(outputDirectory);
                using IDisposable writeLock = AcquireWriteLock(directory);

                FileSystemReparseGuard.VerifiedOpenResult currentResult = ReadVerifiedMutationFile(
                    directory,
                    targetName,
                    out byte[]? current,
                    out FileSystemReparseGuard.FileIdentity currentIdentity,
                    out currentLease);
                if (currentResult != FileSystemReparseGuard.VerifiedOpenResult.Success || current == null)
                {
                    error = $"Target file not found or rejected: {fullTarget}";
                    return false;
                }
                if (!string.Equals(
                        ComputeSha256Hex(current),
                        _lastSavedTargetSha,
                        StringComparison.OrdinalIgnoreCase))
                {
                    error = "Restore aborted: current target SHA does not match expected (hash-gated).";
                    return false;
                }
                FileSystemReparseGuard.VerifiedOpenResult backupResult = ReadVerifiedMutationFile(
                    directory,
                    targetName + ".bak",
                    out byte[]? backup,
                    out FileSystemReparseGuard.FileIdentity backupIdentity,
                    out backupLease);
                if (backupResult != FileSystemReparseGuard.VerifiedOpenResult.Success || backup == null)
                {
                    error = $"Backup not found or rejected: {backupPath}";
                    return false;
                }
                if (backupIdentity != _lastSavedBackupIdentity!.Value ||
                    !string.Equals(
                        ComputeSha256Hex(backup),
                        _lastSavedBackupSha,
                        StringComparison.OrdinalIgnoreCase))
                {
                    error = "Restore aborted: backup provenance no longer matches the promoted save pair.";
                    return false;
                }

                if (!FileSystemReparseGuard.IsOpenedFileIdentityCurrent(
                        directory,
                        targetName + ".bak",
                        backupIdentity))
                {
                    error = "Restore aborted: backup identity changed before promotion.";
                    return false;
                }
                _ = WriteVerifiedReplace(
                    directory,
                    targetName,
                    backup,
                    currentResult,
                    currentIdentity);
                LastSavedPath = null;
                _lastSavedTargetSha = null;
                _lastSavedBackupSha = null;
                _lastSavedBackupIdentity = null;
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error = $"Failed to restore backup: {ex.Message}";
                return false;
            }
            finally
            {
                backupLease?.Dispose();
                currentLease?.Dispose();
            }
        }

        /// <summary>Descarta edições/grow em memória: working bytes voltam ao original.</summary>
        public void DiscardWorkingChanges()
        {
            if (_originalBytes == null)
                return;
            _workingBytes = (byte[])_originalBytes.Clone();
            ShaAfter = ShaBefore;
        }

#if FFX_INCLUDE_DEVTOOLS
        // --- Grow de record (WRITEBACK_SPEC §2.2 Estratégia A) ------------------------

        /// <summary>
        /// Grow de record: adiciona um campo de <paramref name="width"/> bytes (zero-fill)
        /// no fim do record do slot (insertion_point = record_abs + callback_record_width),
        /// deslocando tudo depois (+W) e atualizando TODOS os ponteiros do recurso
        /// (WRITEBACK_SPEC §2.2/§2.3): slots +8/+12, programs next +0, table1 entries,
        /// descriptors w20/w24/w28, table3/table4 entries, root table offsets e
        /// section_size. O header PE também é ajustado (SizeOfRawData do .data +W e
        /// PointerToRawData das seções seguintes +W — extensão obrigatória do shift físico).
        ///
        /// Verificação pós-grow (gates, §4.3): re-parse completo (walker/sanidade do root),
        /// RT0 por record (todo record exceto o editado byte-idêntico) e diff de ponteiros
        /// (conjunto ajustado == conjunto calculado). Falhou → rollback do snapshot.
        ///
        /// RISCO ALTO (pointer-trust): somente em cópia; RT2 pendente. W ∈ {4,8,12,16}.
        /// </summary>
        public bool TryGrowRecord(MagicSlot slot, int width, out string report, out string error)
        {
            report = string.Empty;
            error = string.Empty;
            if (!HasDocument || _fieldMap == null)
            {
                error = Strings.F2_no_document_loaded_3bc35785;
                DebugLog.Warn("Magic.Writer", "TryGrowRecord blocked: no document loaded");
                return false;
            }
            if (width is not (4 or 8 or 12 or 16))
            {
                error = $"Invalid grow width ({width}) — W ∈ {{4,8,12,16}} (WRITEBACK_SPEC §2.2).";
                return false;
            }
            if (slot.OpcodeName == null || !_fieldMap.TryGet(slot.OpcodeName, out MagicFamilySchema? schema) ||
                !schema.PayloadConsumer)
            {
                error = "Grow blocked: handler_table_index does not resolve to a catalogued payload family (R7 — resolve the effect's fp.h first).";
                return false;
            }

            byte[] snapshot = (byte[])_workingBytes!.Clone();

            // Passo 1 — ancoragem: re-parse dos working bytes (estado fresco) e
            // localização do slot pela posição absoluta (SlotAbs é estável sob edição).
            if (!TryParseBytes(_workingBytes, out MagicDllFile? freshFile, out string parseErr))
            {
                error = $"Pre-grow re-parse failed: {parseErr}";
                return false;
            }
            MagicDllRoot? rootCandidate = freshFile!.Roots.FirstOrDefault();
            if (rootCandidate == null)
            {
                error = "No PPP root in the document.";
                return false;
            }
            MagicDllRoot root = rootCandidate;
            MagicSlot? freshSlot = FindSlot(root, slot.SlotAbs, slot.HandlerTableIndex);
            if (freshSlot == null)
            {
                error = "Slot not found in re-parse (canonical key changed?).";
                return false;
            }
            slot = freshSlot;

            int sectionAbs = slot.RecordOffset - (int)slot.PrimaryCallbackRel;
            // insertion_point: usa a largura NATIVA do slot (+4 u16 baixo — descoberta
            // 2026-08-02) quando >= 16: o record real de handlers draw-leve tem 16B
            // (não 32 do max(32, janela)) — inserir em record+32 corromperia dados
            // entre records (nodes/cópias). RecordWidth fica como fallback.
            int nativeWidth = slot.NativeRecordWidth;
            int insertionPoint = slot.RecordOffset +
                (nativeWidth >= 16 ? nativeWidth : slot.RecordWidth);
            if (insertionPoint <= slot.RecordOffset || insertionPoint > _workingBytes.Length)
            {
                error = $"Invalid insertion_point ({insertionPoint:X}) — out of .data bounds.";
                return false;
            }

            // Safety extra (extensão da receita): o insertion_point NÃO pode atravessar
            // estruturas derivadas por contiguidade (arrays de slots, tabelas do root,
            // headers) — estas não têm ponteiros para ajustar (o walker as deriva por
            // aritmética). Atravessou → grow inseguro nesta posição.
            if (InsertionPointCrossesDerivedStructures(root, insertionPoint, out string crossed))
            {
                error = $"Grow unsafe at this position: insertion_point 0x{insertionPoint:X} crosses {crossed}. Skiolha outro slot/campo.";
                return false;
            }

            // ACHADO 2026-08-02 (auditoria 161 slots do 0021 — 22/161 = 14%): o
            // insertion_point pode cair DENTRO do node da cadeia apontada pelo +12 do
            // slot (SecondaryCallbackRel — cadeia PppMem: header u32 = tamanho, múltiplo
            // de 16). Esse node NÃO é coberto pelo InsertionPointCrossesDerivedStructures
            // e o VerifyGrow não o detecta (records vizinhos têm ponteiro próprio e
            // permanecem intactos) — o grow corromperia a cadeia de nodes SILENCIOSAMENTE.
            if (InsertionPointCrossesSlotNode(
                    _workingBytes, _file!.DataSectionRawPtr, slot, insertionPoint, out string nodeCrossed))
            {
                error = $"Grow unsafe at this position: insertion_point 0x{insertionPoint:X} crosses {nodeCrossed}. Escolha outro slot/campo.";
                return false;
            }

            var adjusted = new List<string>();

            // Passo 2 — coleta + Passo 4 — aplicação (regra: destino >= insertion_point → += W).
            void Adjust(int dataAbs, uint stored, int baseAbs, string label)
            {
                int dest = baseAbs + (int)stored;
                if (dest >= insertionPoint)
                {
                    uint next = stored + (uint)width;
                    WriteU32(_workingBytes, _file!.DataSectionRawPtr + dataAbs, next);
                    adjusted.Add($"{label} 0x{dataAbs:X} {stored:X}→{next:X} (+{width})");
                }
            }

            // slots: +8 primary / +12 secondary (rel. SECTION)
            foreach (MagicProgram p in root.Programs)
            {
                foreach (MagicSlot s in p.Slots)
                {
                    Adjust(s.SlotAbs + 8, s.PrimaryCallbackRel, s.RecordOffset - (int)s.PrimaryCallbackRel, $"slot#{s.SlotAbs:X}+8");
                    Adjust(s.SlotAbs + 12, s.SecondaryCallbackRel, s.RecordOffset - (int)s.PrimaryCallbackRel, $"slot#{s.SlotAbs:X}+12");
                }
                // programs: next_relative +0 (rel. SECTION; 0 = sentinela — regra genérica cobre)
                if (p.NextRelative != 0)
                    Adjust(p.ProgramAbs, (uint)p.NextRelative, p.SectionAbs, $"next@{p.ProgramAbs:X}");
            }

            // table1: primary sections (rel. ROOT)
            for (int i = 0; i < root.PrimarySections.Count; i++)
            {
                int entryAbs = root.RootAbs + root.Table1Rel + 4 * i;
                Adjust(entryAbs, (uint)root.PrimarySections[i], root.RootAbs, $"tbl1[{i}]");
            }

            // table2 descriptors: w20/w24/w28 (rel. ROOT; 0 = ausente — regra genérica cobre)
            foreach (MagicDescriptor d in root.Descriptors)
            {
                Adjust(d.EntryAbs + 20, d.W20Rel, root.RootAbs, $"desc#{d.Index}.w20");
                Adjust(d.EntryAbs + 24, d.W24Rel, root.RootAbs, $"desc#{d.Index}.w24");
                Adjust(d.EntryAbs + 28, d.W28Rel, root.RootAbs, $"desc#{d.Index}.w28");
            }

            // table3: entries u32 (rel. ROOT)
            int table3Base = root.RootAbs + root.Table3Rel;
            for (int i = 0; i < root.Aux3Count; i++)
            {
                int entryAbs = table3Base + 4 * i;
                Adjust(entryAbs, ReadU32(_workingBytes, _file!.DataSectionRawPtr + entryAbs), root.RootAbs, $"tbl3[{i}]");
            }

            // table4: entries 8B com u32@+4 (rel. ROOT)
            int table4Base = root.RootAbs + root.Table4Rel;
            for (int i = 0; i < root.Aux4Count; i++)
            {
                int entryAbs = table4Base + 8 * i + 4;
                Adjust(entryAbs, ReadU32(_workingBytes, _file!.DataSectionRawPtr + entryAbs), root.RootAbs, $"tbl4[{i}]");
            }

            // root table offsets +16/+20/+24/+28 (rel. ROOT) — base desloca ⇒ valor += W
            Adjust(root.RootAbs + 16, (uint)root.Table1Rel, root.RootAbs, "root.tbl1");
            Adjust(root.RootAbs + 20, (uint)root.Table2Rel, root.RootAbs, "root.tbl2");
            Adjust(root.RootAbs + 24, (uint)root.Table3Rel, root.RootAbs, "root.tbl3");
            Adjust(root.RootAbs + 28, (uint)root.Table4Rel, root.RootAbs, "root.tbl4");

            // section_size @section+0 (u32; cresce quando o record terminava dentro da section)
            int sectionSizeAbs = _file!.DataSectionRawPtr + sectionAbs;
            uint sectionSize = ReadU32(_workingBytes, sectionSizeAbs);
            if (sectionAbs + (int)sectionSize >= insertionPoint)
            {
                uint nextSize = sectionSize + (uint)width;
                WriteU32(_workingBytes, sectionSizeAbs, nextSize);
                adjusted.Add($"section_size 0x{sectionAbs:X} {sectionSize:X}→{nextSize:X} (+{width})");
            }
            else
            {
                report += Strings.U_Md_GrowRecordOutside;
            }

            // ACHADO 2026-08-02 (evidência numérica do 0021: rel8/rel12 em 0x1C444/0x1C4E4
            // > insertion 0x193E0): as DUAS counted-u32 tables do header da section (+8 e
            // +12) não eram ajustadas pela receita original — com insertion_point antes
            // delas, o shift físico as deslocava +W sem corrigir o rel field nem as
            // entries → IsValidCountedU32Table lia count lixo → walker divergia
            // ("programs 16→0") → grow rejeitado. Correção: ajustar o rel field (se a
            // própria tabela estava >= insertion) e cada entry (se o destino >= insertion).
            foreach (int relFieldOff in new[] { 8, 12 })
            {
                int relFieldAbs = sectionAbs + relFieldOff;
                uint rel = ReadU32(_workingBytes, _file.DataSectionRawPtr + relFieldAbs);
                if (rel < 16)
                    continue; // sem tabela (o parser rejeitaria a section de qualquer forma)

                // 1) rel field: a tabela física andou se estava >= insertion_point.
                if (sectionAbs + (int)rel >= insertionPoint)
                {
                    uint nextRel = rel + (uint)width;
                    WriteU32(_workingBytes, _file.DataSectionRawPtr + relFieldAbs, nextRel);
                    adjusted.Add($"sec+{relFieldOff} 0x{relFieldAbs:X} {rel:X}→{nextRel:X} (+{width})");
                }

                // 2) entries (rel. SECTION; destino = section + entry).
                int tableAbs = sectionAbs + (int)rel;
                uint count = ReadU32(_workingBytes, _file.DataSectionRawPtr + tableAbs);
                if (count > 4096)
                    continue; // tabela inválida — o VerifyGrow falharia; nada a ajustar aqui
                for (uint i = 0; i < count; i++)
                {
                    int entryAbs = tableAbs + 4 + (int)(4 * i);
                    uint entry = ReadU32(_workingBytes, _file.DataSectionRawPtr + entryAbs);
                    if (sectionAbs + (int)entry >= insertionPoint)
                    {
                        uint nextEntry = entry + (uint)width;
                        WriteU32(_workingBytes, _file.DataSectionRawPtr + entryAbs, nextEntry);
                        adjusted.Add($"sec+{relFieldOff}[{i}] 0x{entryAbs:X} {entry:X}→{nextEntry:X} (+{width})");
                    }
                }
            }

            // Passo 3 — shift físico de W bytes zero em insertion_point (.data)
            int fileInsert = _file.DataSectionRawPtr + insertionPoint;
            byte[] grown = new byte[_workingBytes.Length + width];
            Buffer.BlockCopy(_workingBytes, 0, grown, 0, fileInsert);
            Buffer.BlockCopy(_workingBytes, fileInsert, grown, fileInsert + width, _workingBytes.Length - fileInsert);

            // Extensão obrigatória: header PE — SizeOfRawData(.data) += W;
            // PointerToRawData das seções seguintes += W (o shift físico as deslocou).
            AdjustPeSectionPointers(grown, width, out string peReport);
            report += peReport;



            // Passo 6 — verificação (gates) sobre o arquivo grown.
            if (!VerifyGrow(freshFile, grown, slot, width, insertionPoint, adjusted, out string verifyError))
            {
                _workingBytes = snapshot;
                ShaAfter = ComputeSha256Hex(snapshot);
                error = $"Grow FAILED verification — rollback applied: {verifyError}";
                return false;
            }

            // Passo 7 — commit: working bytes = grown; documento re-parseado (árvore atualizada).
            _workingBytes = grown;
            ShaAfter = ComputeSha256Hex(grown);
            MagicDllFile committedFile = _lastVerifiedReparse ?? freshFile;
            HashSet<WriterRecordAnchor> committedAnchors = BuildWriterRecordAnchors(committedFile);
            _file = committedFile;
            _writerRecordAnchors = committedAnchors;
            _writerDataSectionRawPtr = committedFile.DataSectionRawPtr;

            report = string.Format(Strings.U_Md_GrowReport,
                width, slot.RecordOffset, insertionPoint, sectionAbs,
                adjusted.Count, string.Join(", ", adjusted));
            DebugLog.Info("Magic.Writer",
                $"TryGrowRecord OK +{width}B on record 0x{slot.RecordOffset:X} (insertion 0x{insertionPoint:X}): {adjusted.Count} pointer(s) adjusted, re-parse + RT0 OK");
            return true;
        }
#endif

        // --- Grow: helpers de verificação ---------------------------------------------

        private MagicDllFile? _lastVerifiedReparse;

        /// <summary>
        /// Gates pós-grow (WRITEBACK_SPEC §4.3): re-parse completo do grown + RT0 por record
        /// (todo record exceto o editado byte-idêntico; o editado preserva os bytes originais
        /// antes do insertion_point) + diff de ponteiros (valores lidos == esperados).
        /// </summary>
        private bool VerifyGrow(
            MagicDllFile before,
            byte[] grown,
            MagicSlot editedSlot,
            int width,
            int insertionPoint,
            List<string> adjustedPointers,
            out string error)
        {
            error = string.Empty;

            if (!TryParseBytes(grown, out MagicDllFile? after, out string parseError))
            {
                error = $"re-parse do grown falhou: {parseError}";
                return false;
            }
            _lastVerifiedReparse = after;

            // (ii) walker completo: mesmo nº de programs/slots.
            int programsBefore = before.Roots.Sum(r => r.ProgramCount);
            int programsAfter = after!.Roots.Sum(r => r.ProgramCount);
            int slotsBefore = before.Roots.Sum(r => r.TotalSlots);
            int slotsAfter = after.Roots.Sum(r => r.TotalSlots);
            if (programsBefore != programsAfter || slotsBefore != slotsAfter)
            {
                error = $"walker divergiu: programs {programsBefore}→{programsAfter}, slots {slotsBefore}→{slotsAfter}.";
                return false;
            }

            // (iii) RT0 por record: chave ordinal (índice do program na árvore + índice do
            // slot no program) — ESTÁVEL sob grow. A chave antiga (SlotAbs) não é estável:
            // com insertion_point antes do próprio slot, o shift desloca o slot (+W) e o
            // re-parse o registra com SlotAbs novo ("slot 0x196C8 sumiu").
            var beforeBySlot = new List<(int ProgramOrdinal, int SlotIndex, MagicSlot Slot)>();
            int programOrdinal = 0;
            foreach (MagicDllRoot r in before.Roots)
            {
                foreach (MagicProgram p in r.Programs)
                {
                    for (int si = 0; si < p.Slots.Count; si++)
                        beforeBySlot.Add((programOrdinal, si, p.Slots[si]));
                    programOrdinal++;
                }
            }
            var afterBySlot = new Dictionary<(int, int), MagicSlot>();
            programOrdinal = 0;
            foreach (MagicDllRoot r in after!.Roots)
            {
                foreach (MagicProgram p in r.Programs)
                {
                    for (int si = 0; si < p.Slots.Count; si++)
                        afterBySlot[(programOrdinal, si)] = p.Slots[si];
                    programOrdinal++;
                }
            }
            foreach ((int po, int si, MagicSlot sBefore) in beforeBySlot)
            {
                if (!afterBySlot.TryGetValue((po, si), out MagicSlot? sAfter))
                {
                    error = $"slot ordinal ({po},{si}) sumiu no re-parse.";
                    return false;
                }
                if (sBefore.SlotAbs == editedSlot.SlotAbs)
                {
                    // Editado: bytes originais antes do insertion_point devem estar intactos.
                    int keep = Math.Min(sBefore.Record.Length, insertionPoint - sBefore.RecordOffset);
                    if (keep < 0)
                        keep = 0;
                    if (keep > sAfter.Record.Length || !sAfter.Record.AsSpan(0, keep).SequenceEqual(sBefore.Record.AsSpan(0, keep)))
                    {
                        error = $"record editado 0x{sBefore.SlotAbs:X}: bytes originais antes do insertion_point alterados.";
                        return false;
                    }
                }
                else if (!string.Equals(sBefore.RecordSha256, sAfter.RecordSha256, StringComparison.OrdinalIgnoreCase))
                {
                    error = $"RT0 per record FAILED on slot 0x{sBefore.SlotAbs:X} (neighbor changed by the shift).";
                    return false;
                }
            }

            // (v) diff de ponteiros: o conjunto ajustado foi contabilizado pelo caller;
            // aqui conferimos que NENHUM slot/program perdeu o destino (ponteiro aponta
            // para offset dentro do .data — sanidade do walker já garante limites).
            _ = width;
            _ = adjustedPointers;
            return true;
        }

        /// <summary>
        /// Verifica se o insertion_point cai DENTRO do node da cadeia do slot (+12 —
        /// SecondaryCallbackRel, cadeia PppMem). O node tem header u32 = tamanho
        /// (múltiplo de 16, 16..4096). ACHADO 2026-08-02: 22/161 slots do 0021 (14%)
        /// teriam o insertion dentro do node — corrupção silenciosa da cadeia (o
        /// VerifyGrow não detecta: records vizinhos têm ponteiro próprio).
        /// </summary>
        private static bool InsertionPointCrossesSlotNode(
            byte[] bytes, int dataSectionRawPtr, MagicSlot slot, int insertionPoint, out string crossed)
        {
            crossed = string.Empty;
            if (slot.SecondaryCallbackRel == 0)
                return false;

            int sectionAbs = slot.RecordOffset - (int)slot.PrimaryCallbackRel;
            int nodeAbs = sectionAbs + (int)slot.SecondaryCallbackRel;

            uint nodeSize = ReadU32(bytes, dataSectionRawPtr + nodeAbs);
            if (nodeSize < 16 || nodeSize > 4096 || nodeSize % 16 != 0)
                return false; // tamanho não plausível — não bloqueia

            if (insertionPoint >= nodeAbs && insertionPoint < nodeAbs + (int)nodeSize)
            {
                crossed = $"node da cadeia do slot +0xC @0x{nodeAbs:X} (size 0x{nodeSize:X})";
                return true;
            }
            return false;
        }
        private static bool InsertionPointCrossesDerivedStructures(
            MagicDllRoot root, int insertionPoint, out string crossed)
        {
            crossed = string.Empty;
            int rootAbs = root.RootAbs;

            if (insertionPoint >= rootAbs && insertionPoint < rootAbs + 32)
            {
                crossed = "header do root";
                return true;
            }

            // table1 (primary sections)
            int t1Start = rootAbs + root.Table1Rel;
            if (insertionPoint >= t1Start && insertionPoint < t1Start + 4 * root.PrimaryCount)
            {
                crossed = "table1 (primary sections)";
                return true;
            }
            // table2 (descriptors 32B)
            int t2Start = rootAbs + root.Table2Rel;
            if (insertionPoint >= t2Start && insertionPoint < t2Start + 32 * root.DescriptorCount)
            {
                crossed = "table2 (descriptors)";
                return true;
            }
            // table3 (u32)
            int t3Start = rootAbs + root.Table3Rel;
            if (insertionPoint >= t3Start && insertionPoint < t3Start + 4 * root.Aux3Count)
            {
                crossed = "table3";
                return true;
            }
            // table4 (8B)
            int t4Start = rootAbs + root.Table4Rel;
            if (insertionPoint >= t4Start && insertionPoint < t4Start + 8 * root.Aux4Count)
            {
                crossed = "table4";
                return true;
            }

            // programs: header (40B) + array de slots (16B cada)
            foreach (MagicProgram p in root.Programs)
            {
                if (insertionPoint >= p.ProgramAbs && insertionPoint < p.ProgramAbs + 40)
                {
                    crossed = $"header do program @0x{p.ProgramAbs:X}";
                    return true;
                }
                int slotsStart = p.ProgramAbs + 40;
                int slotsEnd = slotsStart + 16 * p.Slots.Count;
                if (insertionPoint >= slotsStart && insertionPoint < slotsEnd)
                {
                    crossed = $"array de slots do program @0x{p.ProgramAbs:X}";
                    return true;
                }
                // header da section (56B)
                if (insertionPoint >= p.SectionAbs && insertionPoint < p.SectionAbs + 56)
                {
                    crossed = $"header da section @0x{p.SectionAbs:X}";
                    return true;
                }
            }

            return false;
        }

        /// <summary>Localiza um slot pela posição absoluta + handler (estável sob edição).</summary>
        private static MagicSlot? FindSlot(MagicDllRoot root, int slotAbs, uint handlerTableIndex)
        {
            foreach (MagicProgram p in root.Programs)
            foreach (MagicSlot s in p.Slots)
            {
                if (s.SlotAbs == slotAbs && s.HandlerTableIndex == handlerTableIndex)
                    return s;
            }
            return null;
        }

        // --- Utilitários ---------------------------------------------------------------

        /// <summary>
        /// Diretório Yonishi com os fp.h locais por efeito (resolução de opcode por
        /// handler_table_index LOCAL — regra de ouro PARSER_SPEC §5.3). Null quando
        /// a extração do jogo não está acessível (fallback: catálogo embutido).
        /// </summary>
        private static string? FindYonishiFpDirectory()
        {
            string? ffxPs2 = Project_Service.Instance.Path_FfxPs2Root;
            string? master = Project_Service.Instance.ProjectPath;
            string? ffxRoot = string.IsNullOrWhiteSpace(master)
                ? null
                : Directory.GetParent(master)?.FullName;
            string?[] candidates =
            {
                Environment.GetEnvironmentVariable("FFX_YONISHI_DAT_OV"),
                string.IsNullOrWhiteSpace(ffxPs2)
                    ? null
                    : Path.Combine(ffxPs2, "ffx", "yonishi_data", "dat_ov"),
                string.IsNullOrWhiteSpace(ffxRoot)
                    ? null
                    : Path.Combine(ffxRoot, "yonishi_data", "dat_ov"),
                Path.Combine(AppContext.BaseDirectory, "yonishi_data", "dat_ov"),
                Path.Combine(Environment.CurrentDirectory, "yonishi_data", "dat_ov"),
            };
            return candidates.FirstOrDefault(path =>
                !string.IsNullOrWhiteSpace(path) && Directory.Exists(path));
        }

        /// <summary>
        /// Blocks writes anywhere below the configured/discovered game installation. When no game
        /// root can be resolved, a structural Steam layout guard remains as the safe fallback.
        /// </summary>
        public static bool IsInstalledGamePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                string full = Path.GetFullPath(path);
                string? gameRoot = Project_Service.Instance.Path_GameInstallRoot;
                if (!string.IsNullOrWhiteSpace(gameRoot) && IsWithinDirectory(full, gameRoot))
                    return true;

                string normalized = full.Replace(
                    Path.AltDirectorySeparatorChar,
                    Path.DirectorySeparatorChar);
                string separator = Path.DirectorySeparatorChar.ToString();
                string steamLayout = separator + "steamapps" + separator + "common" + separator;
                bool hasSteamLibrarySegment = normalized
                    .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
                    .Any(segment => string.Equals(
                        segment,
                        "SteamLibrary",
                        StringComparison.OrdinalIgnoreCase));
                return hasSteamLibrarySegment ||
                    normalized.Contains(steamLayout, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return false;
            }
        }

        /// <summary>Compatibility alias retained for existing developer gates.</summary>
        [Obsolete("Use IsInstalledGamePath; it protects any configured game installation.")]
        public static bool IsSteamLibraryPath(string path) => IsInstalledGamePath(path);

        private static bool IsWithinDirectory(string path, string directory)
        {
            string fullPath = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fullDirectory = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            StringComparison comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return string.Equals(fullPath, fullDirectory, comparison) ||
                fullPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, comparison);
        }

        private static bool PathsEqual(string left, string right)
        {
            StringComparison comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
        }

        private static bool TryParseCanonicalCloneName(string destinationPath, out int magicId)
        {
            magicId = -1;
            string fileName;
            try { fileName = Path.GetFileName(destinationPath); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                return false;
            }

            const string Prefix = "magic_";
            const string Extension = ".dll";
            if (fileName.Length != Prefix.Length + 4 + Extension.Length ||
                !fileName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ||
                !fileName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
                return false;

            ReadOnlySpan<char> digits = fileName.AsSpan(Prefix.Length, 4);
            foreach (char digit in digits)
                if (digit is < '0' or > '9')
                    return false;
            return int.TryParse(
                digits,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out magicId);
        }

        private static string? FindExistingMagicIdDll(
            FileSystemReparseGuard.VerifiedDirectory directory,
            int magicId)
        {
            // Collision compatibility is deliberately finite: the canonical four-digit spelling and
            // the historical unpadded decimal spelling. ObjCaseInsensitive in the handle-relative NT
            // primitive preserves legacy casing without reopening or enumerating the directory path.
            string canonical = $"magic_{magicId:D4}.dll";
            string legacy = $"magic_{magicId}.dll";
            string[] candidates = canonical.Equals(legacy, StringComparison.Ordinal)
                ? new[] { canonical }
                : new[] { canonical, legacy };
            foreach (string candidate in candidates)
            {
                FileSystemReparseGuard.VerifiedOpenResult result =
                    FileSystemReparseGuard.TryOpenVerifiedRead(directory, candidate, out var existing);
                using (existing)
                {
                    if (result == FileSystemReparseGuard.VerifiedOpenResult.Success)
                        return Path.Combine(directory.FullPath, candidate);
                    if (result == FileSystemReparseGuard.VerifiedOpenResult.Rejected)
                        throw new IOException($"The Magic collision leaf {candidate} was rejected.");
                }
            }
            return null;
        }

        private static FileSystemReparseGuard.VerifiedOpenResult ReadVerifiedFile(
            FileSystemReparseGuard.VerifiedDirectory directory,
            string fileName,
            out byte[]? bytes,
            out FileSystemReparseGuard.FileIdentity identity)
        {
            bytes = null;
            identity = default;
            FileSystemReparseGuard.VerifiedOpenResult result =
                FileSystemReparseGuard.TryOpenVerifiedRead(directory, fileName, out var verified);
            if (result != FileSystemReparseGuard.VerifiedOpenResult.Success || verified == null)
                return result;
            using (verified)
            using (var buffer = new MemoryStream())
            {
                verified.Stream.CopyTo(buffer);
                bytes = buffer.ToArray();
                identity = verified.Identity;
            }
            return result;
        }

        private static FileSystemReparseGuard.VerifiedOpenResult ReadVerifiedMutationFile(
            FileSystemReparseGuard.VerifiedDirectory directory,
            string fileName,
            out byte[]? bytes,
            out FileSystemReparseGuard.FileIdentity identity,
            out FileSystemReparseGuard.VerifiedReadFile? lease)
        {
            bytes = null;
            identity = default;
            FileSystemReparseGuard.VerifiedOpenResult result =
                FileSystemReparseGuard.TryOpenVerifiedMutationRead(directory, fileName, out lease);
            if (result != FileSystemReparseGuard.VerifiedOpenResult.Success || lease == null)
                return result;
            using var buffer = new MemoryStream();
            lease.Stream.CopyTo(buffer);
            bytes = buffer.ToArray();
            identity = lease.Identity;
            return result;
        }

        private static FileSystemReparseGuard.FileIdentity WriteVerifiedReplace(
            FileSystemReparseGuard.VerifiedDirectory directory,
            string destinationName,
            byte[] content,
            FileSystemReparseGuard.VerifiedOpenResult expectedState,
            FileSystemReparseGuard.FileIdentity expectedIdentity)
        {
            FileStream? temporary = null;
            try
            {
                temporary = FileSystemReparseGuard.CreateNewVerifiedFile(
                    directory,
                    $".{Guid.NewGuid():N}.tmp");
                temporary.Write(content, 0, content.Length);
                temporary.Flush(flushToDisk: true);
                return FileSystemReparseGuard.PromoteOpenedFileIfUnchanged(
                    directory,
                    temporary,
                    destinationName,
                    expectedState,
                    expectedIdentity);
            }
            catch
            {
                if (temporary != null)
                {
                    try { FileSystemReparseGuard.DeleteOpenedFile(temporary); } catch { }
                }
                throw;
            }
            finally { temporary?.Dispose(); }
        }

        private static void WriteVerifiedCreateOnly(
            FileSystemReparseGuard.VerifiedDirectory directory,
            string destinationName,
            byte[] content)
        {
            FileStream? destination = null;
            try
            {
                destination = FileSystemReparseGuard.CreateNewVerifiedFile(directory, destinationName);
                destination.Write(content, 0, content.Length);
                destination.Flush(flushToDisk: true);
            }
            catch
            {
                if (destination != null)
                {
                    try { FileSystemReparseGuard.DeleteOpenedFile(destination); } catch { }
                }
                throw;
            }
            finally { destination?.Dispose(); }
        }

        private static IDisposable AcquireWriteLock(
            FileSystemReparseGuard.VerifiedDirectory directory)
        {
            // A named kernel mutex coordinates every Studio process without persisting a sidecar
            // in a user-selected directory. Hashing the canonical directory keeps the public name
            // bounded while preserving one collision domain for sibling Magic ids.
            string normalized = Path.TrimEndingDirectorySeparator(directory.FullPath).ToUpperInvariant();
            string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
            Mutex mutex;
            try
            {
                mutex = new Mutex(initiallyOwned: false, name: $@"Local\FFXMS-MagicWriter-{digest}");
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
            {
                throw new IOException("The cross-process Magic writer mutex could not be opened.", ex);
            }

            bool acquired;
            try
            {
                try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(2)); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired)
                    throw new IOException("Timed out waiting for another Magic writer to finish.");
                return new MutexLease(mutex);
            }
            catch
            {
                mutex.Dispose();
                throw;
            }
        }

        private sealed class MutexLease : IDisposable
        {
            private Mutex? _mutex;

            internal MutexLease(Mutex mutex) => _mutex = mutex;

            public void Dispose()
            {
                Mutex? mutex = Interlocked.Exchange(ref _mutex, null);
                if (mutex == null)
                    return;
                try { mutex.ReleaseMutex(); }
                finally { mutex.Dispose(); }
            }
        }

        /// <summary>SHA-256 em hex (minúsculo) dos bytes (padrão T3/executor).</summary>
        public static string ComputeSha256Hex(byte[] bytes)
        {
            if (bytes.Length == 0)
                return string.Empty;
            byte[] hash = SHA256.HashData(bytes);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static uint ReadU32(byte[] bytes, int offset) =>
            offset >= 0 && offset + 4 <= bytes.Length ? BitConverter.ToUInt32(bytes, offset) : 0;

        private static bool TryReadU16(byte[] bytes, int offset, out ushort value)
        {
            if (offset < 0 || (long)offset + sizeof(ushort) > bytes.Length)
            {
                value = 0;
                return false;
            }
            value = BitConverter.ToUInt16(bytes, offset);
            return true;
        }

        private static bool TryReadI32(byte[] bytes, int offset, out int value)
        {
            if (offset < 0 || (long)offset + sizeof(int) > bytes.Length)
            {
                value = 0;
                return false;
            }
            value = BitConverter.ToInt32(bytes, offset);
            return true;
        }

        private static bool TryReadU32(byte[] bytes, int offset, out uint value)
        {
            if (offset < 0 || (long)offset + sizeof(uint) > bytes.Length)
            {
                value = 0;
                return false;
            }
            value = BitConverter.ToUInt32(bytes, offset);
            return true;
        }

        private static void WriteU32(byte[] bytes, int offset, uint value) =>
            BitConverter.GetBytes(value).CopyTo(bytes, offset);

        /// <summary>
        /// Ajusta o header PE após o shift físico: SizeOfRawData da seção .data += W e
        /// PointerToRawData de todas as seções seguintes += W (RVAs não mudam — o layout
        /// virtual da imagem permanece; as relocações .reloc continuam válidas).
        /// </summary>
        private static void AdjustPeSectionPointers(byte[] bytes, int width, out string report)
        {
            report = string.Empty;
            if (bytes.Length < 64)
                return;

            int peOffset = BitConverter.ToInt32(bytes, 0x3C);
            int coff = peOffset + 4;
            int numSections = BitConverter.ToUInt16(bytes, coff + 2);
            int optSize = BitConverter.ToUInt16(bytes, coff + 16);
            int sectionTable = coff + 20 + optSize;

            int dataRawSizeOffset = -1;
            var shifted = new List<string>();

            for (int i = 0; i < numSections; i++)
            {
                int so = sectionTable + i * 40;
                string name = Encoding.ASCII.GetString(bytes, so, 8).TrimEnd('\0');
                int rawPtr = BitConverter.ToInt32(bytes, so + 20);
                if (name == ".data" || name == "DATA")
                {
                    dataRawSizeOffset = so + 16;
                }
                else if (rawPtr > 0)
                {
                    shifted.Add($"'{name}' rawptr 0x{rawPtr:X}→0x{rawPtr + width:X}");
                    BitConverter.GetBytes(rawPtr + width).CopyTo(bytes, so + 20);
                }
            }

            if (dataRawSizeOffset >= 0)
            {
                int rawSize = BitConverter.ToInt32(bytes, dataRawSizeOffset);
                BitConverter.GetBytes(rawSize + width).CopyTo(bytes, dataRawSizeOffset);
                report = string.Format(Strings.U_Md_PeHeaderReport, rawSize, rawSize + width, shifted.Count > 0 ? string.Join(", ", shifted) : Strings.U_Md_NoFollowingSections);
            }
            else
            {
                report = Strings.U_Md_PeHeaderNotFound;
            }
        }

        }

        }
