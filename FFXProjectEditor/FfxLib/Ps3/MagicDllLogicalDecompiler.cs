using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.FfxLib.Ps3
{
    /// <summary>
    /// Static "logical decompile" of magic DLL overlay callbacks: PE byte-scan per slot,
    /// host-offset fingerprint, stub detection, template pseudocode — no Hex-Rays required.
    /// </summary>
    internal static class MagicDllLogicalDecompiler
    {
        public const int DefaultScanWindowBytes = 2048;

        static readonly int[] KnownHostOffsets =
        [
            672, 676, 680, 844, 856, 884, 888, 900, 904, 908, 1000, 1012, 1040, 1044,
            1120, 1152, 1156, 1160, 1172, 1176, 1184, 1228, 1244, 1248, 1252, 1260, 1300,
            1324, 1328, 1412, 1556, 1588, 2172, 2196, 2220, 2224, 2840, 2844, 2852, 2860,
            2864, 2872, 2876, 2880, 2884, 2892, 2896, 2908, 3140, 3160, 3212, 3216, 3732, 3764
        ];

        public static MagicDllLogicalDecompileResult Decompile(MagicDllInspection inspection, int scanWindowBytes = DefaultScanWindowBytes)
        {
            byte[] bytes = File.ReadAllBytes(inspection.FilePath);
            MagicDllFamilyClassification classification = MagicDllFamilyClassifier.Classify(inspection);
            MagicDllEffectFamily family = classification.Family;
            IReadOnlyList<MagicDllSlotSemanticCandidate> templateRoles = MagicDllSemanticAnalyzer.AnalyzeOverlaySlots(inspection);

            List<MagicDllLogicalSlotDecompile> slots = [];
            if (inspection.OverlayEvidence != null)
            {
                foreach (MagicDllOverlaySlot slot in inspection.OverlayEvidence.Slots.OrderBy(s => s.Index))
                    slots.Add(AnalyzeSlot(inspection, bytes, slot, family, templateRoles, scanWindowBytes));
            }

            string slot0Hash = slots.FirstOrDefault(s => s.SlotIndex == 0)?.CodeSha256 ?? string.Empty;
            string slot1Hash = slots.FirstOrDefault(s => s.SlotIndex == 1)?.CodeSha256 ?? string.Empty;
            int codeSlots = slots.Count(s => s.Kind.Equals("code", StringComparison.OrdinalIgnoreCase) && !s.IsStub);
            int stubSlots = slots.Count(s => s.IsStub);

            return new MagicDllLogicalDecompileResult(
                inspection.FilePath,
                inspection.FileName,
                inspection.MagicId,
                inspection.Sha256,
                family.ToString(),
                classification.Method.ToString(),
                classification.PreclassifiedBucket,
                FormatOffsetList(classification.Slot0DiscriminantOffsets),
                inspection.OverlayEvidence?.SlotKindSignature ?? string.Empty,
                slot0Hash,
                slot1Hash,
                codeSlots,
                stubSlots,
                slots,
                BuildSummaryLines(family, slots),
                inspection.Strings.Select(s => s.Value).Where(LooksLikeEngineIdentifier).Distinct(StringComparer.Ordinal).Take(32).ToList());
        }

        public static void WritePerDllReport(MagicDllLogicalDecompileResult result, string outputDir)
        {
            string id = result.MagicId?.ToString("D4", CultureInfo.InvariantCulture) ?? "unknown";
            string dllDir = Path.Combine(outputDir, $"magic_{id}");
            Directory.CreateDirectory(dllDir);

            string mdPath = Path.Combine(dllDir, "LOGICAL_DECOMPILE.md");
            File.WriteAllText(mdPath, BuildMarkdown(result), Encoding.UTF8);

            string pseudoPath = Path.Combine(dllDir, "overlay_pseudocode.c");
            File.WriteAllText(pseudoPath, BuildPseudocodeFile(result), Encoding.UTF8);
        }

        static MagicDllLogicalSlotDecompile AnalyzeSlot(
            MagicDllInspection inspection,
            byte[] bytes,
            MagicDllOverlaySlot slot,
            MagicDllEffectFamily family,
            IReadOnlyList<MagicDllSlotSemanticCandidate> templateRoles,
            int scanWindowBytes)
        {
            MagicDllSlotSemanticCandidate template = templateRoles.FirstOrDefault(r => r.SlotIndex == slot.Index)
                ?? new MagicDllSlotSemanticCandidate(slot.Index, "OverlayUnknown", "Unknown slot.", "low", string.Empty);

            if (string.Equals(slot.Kind, "null", StringComparison.OrdinalIgnoreCase)
                || string.Equals(slot.VirtualAddress, "0x0", StringComparison.OrdinalIgnoreCase))
            {
                return new MagicDllLogicalSlotDecompile(
                    slot.Index, slot.Kind, slot.Rva, 0, 0, false, string.Empty, [],
                    template.CandidateName, "high", "unused",
                    ["// null overlay slot"]);
            }

            if (string.Equals(slot.Kind, "data", StringComparison.OrdinalIgnoreCase))
            {
                return new MagicDllLogicalSlotDecompile(
                    slot.Index, slot.Kind, slot.Rva, 0, 0, false, string.Empty, [],
                    template.CandidateName, template.Confidence, "data_table",
                    [$"// data slot @ {slot.Rva}: {template.Meaning}"]);
            }

            if (!TryParseHex(slot.Rva, out int rva))
            {
                return new MagicDllLogicalSlotDecompile(
                    slot.Index, slot.Kind, slot.Rva, 0, 0, false, string.Empty, [],
                    template.CandidateName, "low", "parse_error",
                    [$"// could not parse RVA {slot.Rva}"]);
            }

            int fileOffset;
            try
            {
                fileOffset = inspection.RvaToFileOffset(rva);
            }
            catch
            {
                return new MagicDllLogicalSlotDecompile(
                    slot.Index, slot.Kind, slot.Rva, rva, 0, false, string.Empty, [],
                    template.CandidateName, "low", "rva_oob",
                    [$"// RVA 0x{rva:X} outside PE sections"]);
            }

            int window = Math.Min(scanWindowBytes, Math.Max(0, bytes.Length - fileOffset));
            byte[] slice = bytes.AsSpan(fileOffset, window).ToArray();
            int estSize = EstimateFunctionSize(slice);
            bool isStub = DetectStub(slice, estSize);
            IReadOnlyList<int> hostOffsets = ScanHostOffsets(slice);
            string codeHash = Sha256Hex(slice.AsSpan(0, Math.Min(estSize, slice.Length)).ToArray());

            string matchLevel = ScoreTemplateMatch(template.CandidateName, hostOffsets, isStub, family, slot.Index);
            IReadOnlyList<string> pseudo = BuildSlotPseudocode(family, slot.Index, template, hostOffsets, isStub, inspection.Strings);

            return new MagicDllLogicalSlotDecompile(
                slot.Index,
                slot.Kind,
                slot.Rva,
                rva,
                estSize,
                isStub,
                codeHash,
                hostOffsets,
                template.CandidateName,
                matchLevel switch
                {
                    "proven_fingerprint" => "high",
                    "partial_fingerprint" => "medium",
                    _ => template.Confidence
                },
                matchLevel,
                pseudo);
        }

        static int EstimateFunctionSize(byte[] slice)
        {
            if (slice.Length == 0)
                return 0;

            int limit = Math.Min(slice.Length, 512);
            for (int i = 0; i < limit; i++)
            {
                if (slice[i] == 0xC3 && i >= 2)
                    return i + 1;
                if (slice[i] == 0xCC && i > 0 && slice[i - 1] == 0xCC)
                    return i;
            }

            return limit;
        }

        static bool DetectStub(byte[] slice, int estSize)
        {
            if (estSize <= 0)
                return true;
            if (estSize <= 6 && slice.Length >= 2 && slice[0] == 0x33 && slice[1] == 0xC0)
                return true;
            if (estSize <= 8 && slice.Length >= 3 && slice[0] == 0x31 && slice[1] == 0xC0 && slice[2] == 0xC3)
                return true;
            if (estSize <= 16 && !ScanHostOffsets(slice).Any())
                return true;
            return false;
        }

        static IReadOnlyList<int> ScanHostOffsets(byte[] slice)
        {
            HashSet<int> found = [];
            int end = Math.Max(0, slice.Length - 3);
            for (int i = 0; i < end; i++)
            {
                int imm = BitConverter.ToInt32(slice, i);
                if (KnownHostOffsets.Contains(imm))
                    found.Add(imm);
            }

            return found.OrderBy(v => v).ToList();
        }

        static string ScoreTemplateMatch(string templateName, IReadOnlyList<int> hostOffsets, bool isStub, MagicDllEffectFamily family, int slotIndex)
        {
            if (isStub && templateName.Contains("Stub", StringComparison.OrdinalIgnoreCase))
                return "proven_fingerprint";

            int[] expected = ExpectedOffsets(family, slotIndex);
            if (expected.Length == 0)
                return hostOffsets.Count > 0 ? "partial_fingerprint" : "template_only";

            int hits = expected.Count(hostOffsets.Contains);
            if (hits >= Math.Max(2, expected.Length / 2))
                return hits >= expected.Length - 1 ? "proven_fingerprint" : "partial_fingerprint";
            return hostOffsets.Count > 0 ? "partial_fingerprint" : "template_only";
        }

        static int[] ExpectedOffsets(MagicDllEffectFamily family, int slotIndex) => (family, slotIndex) switch
        {
            (MagicDllEffectFamily.A_ParticleSelfContained, 0) => [884, 888, 2220, 2224, 2872],
            (MagicDllEffectFamily.A_ParticleSelfContained, 1) => [1000, 1412, 1588, 2172, 3140, 3732],
            (MagicDllEffectFamily.A_ParticleSelfContained, 4) => [900, 904, 908],
            (MagicDllEffectFamily.B_RootRecordInterpreter, 0) => [2860, 2872, 2876, 3212],
            (MagicDllEffectFamily.B_RootRecordInterpreter, 1) => [672, 676, 680, 3216],
            (MagicDllEffectFamily.B_RootRecordInterpreter, 3) => [2864, 2908],
            (MagicDllEffectFamily.B_RootRecordInterpreter, 4) => [2864, 2884, 2892, 2896],
            (MagicDllEffectFamily.C_RootSelfGovernedParam, 0) => [2840, 2844, 2872, 2876, 3212],
            (MagicDllEffectFamily.C_RootSelfGovernedParam, 1) => [3216],
            (MagicDllEffectFamily.C_RootSelfGovernedParam, 3) => [900, 904, 908],
            (MagicDllEffectFamily.C_RootSelfGovernedParam, 4) => [856, 1556, 2892, 2896],
            (MagicDllEffectFamily.D_EgoTasklist, 0) => [884, 888, 2840, 2844, 2872],
            (MagicDllEffectFamily.D_EgoTasklist, 3) => [900, 904, 908, 1252, 2196],
            (MagicDllEffectFamily.D_EgoTasklist, 4) => [856],
            _ => []
        };

        static IReadOnlyList<string> BuildSlotPseudocode(
            MagicDllEffectFamily family,
            int slotIndex,
            MagicDllSlotSemanticCandidate template,
            IReadOnlyList<int> hostOffsets,
            bool isStub,
            IReadOnlyList<MagicDllAsciiString> strings)
        {
            List<string> lines = [$"// slot {slotIndex:D2}: {template.CandidateName} — {template.Meaning}"];
            if (isStub)
            {
                lines.Add("return 0;");
                return lines;
            }

            foreach (int off in hostOffsets)
                lines.Add(HostOffsetToPseudoLine(off));

            foreach (string s in strings.Select(st => st.Value).Where(v => v.StartsWith("ppp", StringComparison.Ordinal) || v.StartsWith("Ego", StringComparison.Ordinal)).Take(4))
                lines.Add($"// string ref: \"{s}\"");

            if (hostOffsets.Count == 0)
                lines.Add($"// no host-offset immediates in scan window; template: {template.CandidateName}");

            return lines;
        }

        static string HostOffsetToPseudoLine(int offset) => offset switch
        {
            672 => "ownerId = host+672(actorOrEffectId);",
            676 => "host+676(getActorFacing);",
            680 => "host+680(setActorFacing);",
            884 => "host+884(setEffectTimerRangeA);",
            888 => "host+888(setEffectTimerRangeB);",
            900 => "if (!host+900(phaseGate)) return;",
            904 => "host+904(startOrSignalEffect);",
            908 => "progress = host+908(getEffectProgress);",
            1000 => "host+1000(drawOrSubmit);",
            1012 => "host+1012(aliveCheck);",
            1040 => "host+1040(dispatchResourceAlloc);",
            1044 => "host+1044(dispatchCheck);",
            1252 => "host+1252(waitGate);",
            1412 => "host+1412(submitTransformA);",
            1556 => "host+1556(setEffectParam);",
            1588 => "host+1588(submitTransformB);",
            2172 => "host+2172(setBlendOrDrawMode);",
            2196 => "host+2196(checkObjectDone);",
            2220 => "host+2220(resetMatrixA);",
            2224 => "host+2224(resetMatrixB);",
            2840 => "host+2840(bindResourceToBuffer);",
            2844 => "host+2844(parseEgoPppResource);",
            2860 => "host+2860(materializeRuntimeRoot);  // sub_817200 path",
            2864 => "host+2864(runPhaseRecordInterpreter);  // sub_80CD60",
            2872 => "host+2872(initEgoOrPppObject);",
            2876 => "handle = host+2876(createEgoOrPppHandle);",
            2884 => "host+2884(runPhase1SidePass);  // sub_80BEA0",
            2892 => "host+2892(prePhaseResourceA);",
            2896 => "host+2896(prePhaseResourceB);",
            2908 => "host+2908(makePacketBeforeRoutineSignal);",
            3140 => "host+3140(submitScale);",
            3160 => "baseScale = host+3160(getBaseScale);",
            3212 => "root = host+3212(allocateEffectMemory);",
            3216 => "host+3216(freeEffectMemory, root);",
            3732 => "host+3732(setRgbaColor);",
            3764 => "host+3764(readIndirectOffset);",
            856 => "host+856(releaseOrDestroyTask);",
            _ => $"host+{offset}(/* candidate — see HostFieldRoles */);"
        };

        static IReadOnlyList<string> BuildSummaryLines(MagicDllEffectFamily family, IReadOnlyList<MagicDllLogicalSlotDecompile> slots)
        {
            int proven = slots.Count(s => s.MatchLevel == "proven_fingerprint");
            int partial = slots.Count(s => s.MatchLevel == "partial_fingerprint");
            return
            [
                $"family={family}",
                $"slots_proven_fingerprint={proven}",
                $"slots_partial_fingerprint={partial}",
                $"active_code_slots={slots.Count(s => s.Kind == "code" && !s.IsStub)}",
                $"stub_slots={slots.Count(s => s.IsStub)}"
            ];
        }

        static string BuildMarkdown(MagicDllLogicalDecompileResult r)
        {
            StringBuilder sb = new();
            sb.AppendLine($"# Logical decompile — `{r.FileName}`");
            sb.AppendLine();
            sb.AppendLine($"- Magic id: **{r.MagicIdText}**");
            sb.AppendLine($"- Family: **{r.Family}** ({r.DetectionMethod}, bucket `{r.PreclassifiedBucket}`)");
            sb.AppendLine($"- Slot-kind: `{r.SlotKindSignature}`");
            sb.AppendLine($"- Slot0 discriminant offsets: `{r.Slot0HostOffsets}`");
            sb.AppendLine($"- SHA-256: `{r.Sha256}`");
            sb.AppendLine($"- Slot0 code hash: `{r.Slot0CodeSha256}`");
            sb.AppendLine($"- Slot1 code hash: `{r.Slot1CodeSha256}`");
            sb.AppendLine();
            sb.AppendLine("## Overlay slots");
            sb.AppendLine();
            sb.AppendLine("| Slot | Kind | RVA | Size | Stub | Match | Role | Host offsets |");
            sb.AppendLine("| ---: | --- | --- | ---: | :---: | --- | --- | --- |");
            foreach (MagicDllLogicalSlotDecompile s in r.Slots)
            {
                sb.AppendLine($"| {s.SlotIndex:D2} | {s.Kind} | {s.Rva} | {s.EstimatedSize} | {(s.IsStub ? "yes" : "no")} | {s.MatchLevel} | `{s.RoleName}` | `{FormatOffsetList(s.HostOffsets)}` |");
            }

            sb.AppendLine();
            sb.AppendLine("## Pseudocode");
            sb.AppendLine();
            foreach (MagicDllLogicalSlotDecompile s in r.Slots.Where(x => x.Kind == "code"))
            {
                sb.AppendLine($"### Slot {s.SlotIndex:D2} — `{s.RoleName}`");
                sb.AppendLine("```c");
                foreach (string line in s.Pseudocode)
                    sb.AppendLine(line);
                sb.AppendLine("```");
                sb.AppendLine();
            }

            if (r.EngineStrings.Count > 0)
            {
                sb.AppendLine("## Engine strings");
                sb.AppendLine();
                foreach (string s in r.EngineStrings)
                    sb.AppendLine($"- `{s}`");
            }

            return sb.ToString();
        }

        static string BuildPseudocodeFile(MagicDllLogicalDecompileResult r)
        {
            StringBuilder sb = new();
            sb.AppendLine($"// Auto logical decompile — {r.FileName} — family {r.Family}");
            sb.AppendLine($"// NOT original Square/Virtuos source — overlay callback reconstruction.");
            sb.AppendLine();
            foreach (MagicDllLogicalSlotDecompile s in r.Slots.Where(x => x.Kind == "code"))
            {
                sb.AppendLine($"void overlay_slot_{s.SlotIndex:D2}_{SanitizeIdent(s.RoleName)}(void) {{");
                foreach (string line in s.Pseudocode)
                    sb.AppendLine("    " + line);
                sb.AppendLine("}");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        static string SanitizeIdent(string name) =>
            new string(name.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());

        static string FormatOffsetList(IEnumerable<int> offsets) =>
            string.Join(",", offsets.Select(o => "0x" + o.ToString("X", CultureInfo.InvariantCulture)));

        static bool TryParseHex(string text, out int value)
        {
            string trimmed = text.Trim();
            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed[2..];
            return int.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        static string Sha256Hex(byte[] bytes)
        {
            byte[] hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        static bool LooksLikeEngineIdentifier(string value) =>
            value.Length is >= 4 and <= 64
            && value.All(ch => char.IsLetterOrDigit(ch) || ch == '_');
    }

    internal sealed record MagicDllLogicalSlotDecompile(
        int SlotIndex,
        string Kind,
        string Rva,
        int RvaValue,
        int EstimatedSize,
        bool IsStub,
        string CodeSha256,
        IReadOnlyList<int> HostOffsets,
        string RoleName,
        string Confidence,
        string MatchLevel,
        IReadOnlyList<string> Pseudocode);

    internal sealed record MagicDllLogicalDecompileResult(
        string FilePath,
        string FileName,
        int? MagicId,
        string Sha256,
        string Family,
        string DetectionMethod,
        string PreclassifiedBucket,
        string Slot0HostOffsets,
        string SlotKindSignature,
        string Slot0CodeSha256,
        string Slot1CodeSha256,
        int ActiveCodeSlots,
        int StubSlots,
        IReadOnlyList<MagicDllLogicalSlotDecompile> Slots,
        IReadOnlyList<string> SummaryLines,
        IReadOnlyList<string> EngineStrings)
    {
        public string MagicIdText => MagicId?.ToString("D4", CultureInfo.InvariantCulture) ?? "-";
        public bool HasProvenFingerprint => Slots.Any(s => s.MatchLevel == "proven_fingerprint");
    }
}
