using System.Text;
using System.Text.RegularExpressions;
using PhyreModelExportLab;

namespace PhyreMapExportLab;

public static class MapTextureBinder
{
    // field172/0xAC = Phyre reflection member offset (PClassDescriptor, data-driven 16-byte member table;
    // generic walker sub_4D6D40), ida-structural-confirmed via FFX.exe.i64 — see
    // docs/history/FFX_PHYRE_MAP_MATERIAL_IDA_FINDINGS_2026-06-03.md. The per-material PParameterBuffer
    // asset ref at +172 is bound by the runtime to the PhyreDefaultLitShader base-color sampler
    // "TextureSampler" by name-hash (djb2 sub_67B980 / lookup sub_56CD50), NOT by reading +0xAC at draw time.
    // Exact per-buffer asset<->named-param wiring and in-game draw use stay unproven => wording remains "candidate".
    private const string Field172BoundStatus =
        "bound_by_pmesh_pmaterial_pparameterbuffer_field172_texturesampler_base_ida_structural_candidate";
    private const string SingleTextureImportBoundStatus =
        "bound_by_pmesh_pmaterial_pparameterbuffer_single_texture_import_link_structural_candidate";
    // Lowest-field-offset disambiguation of a multi-texture PParameterBuffer. When a buffer carries
    // 2+ DDS texture PAssetReference imports (e.g. base + normal/detail samplers) and NONE sit at the
    // 172/348 base-color offsets, the base-color slot is the LOWEST field offset texture ref — the same
    // slot the engine binds to the named "TextureSampler" param first (ida descriptor walk 0x4D6D40,
    // FFX_PHYRE_MAP_TEXTURE_RESOLUTION_RE_2026-06-06.md). Offline-proved corpus-wide: of 242 multi-texture
    // buffers the lowest-offset ref is a real per-map base-color atlas .dds for 197, a PhyreDefaultLitShader
    // .fx# (kept blocked by the IsDdsImportPath gate) for 45, and ZERO reflection/water — so lowest-offset
    // never grabs a special-lane texture. pfo=0 is already a trusted base-color slot (404 single-import
    // binds resolve at pfo=0), so this is the same base-color rule, just disambiguated by offset rather
    // than by "exactly one". Strictly additive: only fires for buffers that previously bailed as
    // multiple_texture_import_links; never displaces a 172/348 or single-import selection.
    private const string LowestOffsetTextureImportBoundStatus =
        "bound_by_pmesh_pmaterial_pparameterbuffer_lowest_offset_texture_import_link_structural_candidate";
    // Odd PMesh object ids often lack their own PMaterial edge at parentFieldOffset=52; the paired even
    // PMesh (meshId - 1) carries the material table for the same fileMaterialId slot. Offline-proved
    // corpus-wide on 80 maps: 979/979 blocked_missing_pmesh_to_pmaterial_link rows resolve via meshId-1
    // (mihn00_a: 27/27). Additive: only fires when the direct (meshId, fileMaterialId) lookup misses.
    private const string PairedEvenMeshMaterialBoundStatus =
        "bound_by_pmesh_paired_even_mesh_pmaterial_link_structural_candidate";

    public static MapTextureBindingResult ExtractAndBind(
        string ps3Root,
        string outputRoot,
        string area,
        string assetId,
        string primaryAssetLayer,
        PhyreDescriptorReport descriptor,
        bool portable,
        bool flipV)
    {
        var materialIds = descriptor.Submeshes
            .Select(static item => item.FileMaterialId)
            .Distinct()
            .Order()
            .ToArray();

        var rootTextures = EnumerateTextureBucket(ps3Root, area, "tex/d3d11");
        var twoDTextures = EnumerateTextureBucket(ps3Root, area, "2d/tex/d3d11");
        var fpTextures = EnumerateTextureBucket(ps3Root, area, "fp/tex/d3d11");
        var primaryTextureLayer = string.Equals(primaryAssetLayer, "2d_prerendered", StringComparison.OrdinalIgnoreCase)
            || (rootTextures.Length == 0 && twoDTextures.Length > 0)
                ? "2d_prerendered"
                : "root_3d";
        var decoded = new List<DecodedMapTexture>();
        decoded.AddRange(ExtractBucket(rootTextures, outputRoot, assetId, "root_3d", portable));
        decoded.AddRange(ExtractBucket(twoDTextures, outputRoot, assetId, "2d_prerendered", portable));
        decoded.AddRange(ExtractBucket(fpTextures, outputRoot, assetId, "fp", portable));

        var decodedPrimary = decoded
            .Where(item => item.Bucket == primaryTextureLayer && item.Report.DecisionBand == "texture_png_extracted_candidate" && item.Report.Format is not null)
            .OrderBy(static item => item.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var materialTextures = Array.Empty<DescriptorGltfMaterialTexture>();
        var boundMaterials = Array.Empty<MapBoundMaterialTexture>();
        var decisionBand = "blocked_texture_material_count_mismatch";
        var nextStrike = "decode PMaterial/PParameterBuffer texture slots or refine map texture bucket selection before textured GLTF promotion";

        if (primaryTextureLayer == "2d_prerendered" && decodedPrimary.Length == descriptor.Submeshes.Length)
        {
            var orderedSubmeshes = descriptor.Submeshes.OrderBy(static item => item.SubmeshId).ToArray();
            materialTextures = orderedSubmeshes
                .Select((submesh, index) =>
                {
                    var texture = decodedPrimary[index];
                    return new DescriptorGltfMaterialTexture(
                        submesh.FileMaterialId,
                        texture.LocalPngPath,
                        texture.Report.Format!,
                        texture.Report.Width,
                        texture.Report.Height,
                        flipV,
                        portable ? Program.ToPortable(texture.SourcePath) : texture.SourcePath,
                        submesh.SubmeshId);
                })
                .ToArray();

            boundMaterials = orderedSubmeshes
                .Select((submesh, index) =>
                {
                    var texture = decodedPrimary[index];
                    return new MapBoundMaterialTexture(
                        submesh.FileMaterialId,
                        texture.Report.Source.Path,
                        texture.Report.PngPath,
                        texture.Report.Format,
                        texture.Report.Width,
                        texture.Report.Height,
                        "2d_prerendered_submesh_order_texture_candidate",
                        submesh.SubmeshId);
                })
                .ToArray();

            decisionBand = "map_2d_prerendered_submesh_textures_bound_candidate";
            nextStrike = "validate 2d prerendered submesh-order texture binding visually; textureanimation.ags.phyre and runtime composition remain separate before exact promotion";
        }
        else if (decodedPrimary.Length == materialIds.Length)
        {
            materialTextures = materialIds
                .Select((materialId, index) =>
                {
                    var texture = decodedPrimary[index];
                    return new DescriptorGltfMaterialTexture(
                        materialId,
                        texture.LocalPngPath,
                        texture.Report.Format!,
                        texture.Report.Width,
                        texture.Report.Height,
                        flipV,
                        portable ? Program.ToPortable(texture.SourcePath) : texture.SourcePath);
                })
                .ToArray();

            boundMaterials = materialIds
                .Select((materialId, index) =>
                {
                    var texture = decodedPrimary[index];
                    return new MapBoundMaterialTexture(
                        materialId,
                        texture.Report.Source.Path,
                        texture.Report.PngPath,
                        texture.Report.Format,
                        texture.Report.Width,
                        texture.Report.Height,
                        "sorted_primary_texture_to_file_material_id_candidate");
                })
                .ToArray();

            decisionBand = "map_material_textures_bound_candidate";
            nextStrike = "validate textured GLTF visually, then confirm the same fileMaterialId->texture order against PMaterial/PParameterBuffer in IDA/runtime";
        }

        var report = new MapTextureBindingReport(
            DateTimeOffset.UtcNow,
            area,
            assetId,
            primaryAssetLayer,
            primaryTextureLayer,
            materialIds,
            rootTextures.Select(path => portable ? Program.ToPortable(path) : path).ToArray(),
            twoDTextures.Select(path => portable ? Program.ToPortable(path) : path).ToArray(),
            fpTextures.Select(path => portable ? Program.ToPortable(path) : path).ToArray(),
            decoded.Select(static item => item.Report).ToArray(),
            boundMaterials,
            decisionBand,
            nextStrike);

        var root3DTextures = decodedPrimary
            .Select((texture, index) => new MapDecodedRootTexture(
                index,
                portable ? Program.ToPortable(texture.SourcePath) : texture.SourcePath,
                texture.LocalPngPath,
                texture.Report.Format,
                texture.Report.Width,
                texture.Report.Height,
                texture.Report.DecisionBand))
            .ToArray();

        return new MapTextureBindingResult(report, materialTextures, primaryTextureLayer, root3DTextures);
    }

    public static MapMaterialSlotAnalysisResult BuildPmeshMaterialSlotCandidate(
        PhyreDescriptorReport descriptor,
        PhyreLinkDumpReport linkDump,
        string daePath,
        MapTextureBindingResult textureBinding,
        bool flipV)
    {
        var orderedRootTextures = textureBinding.Root3DTextures
            .Where(static item => item.DecisionBand == "texture_png_extracted_candidate" && item.Format is not null)
            .OrderBy(static item => item.SlotIndex)
            .ToArray();
        var rootTexturesBySlot = orderedRootTextures
            .ToDictionary(static item => item.SlotIndex);
        var rootTexturesByFileName = orderedRootTextures
            .GroupBy(static item => FileNameFromPath(item.SourcePath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        var textureImportsBySharedDataId = BuildTextureImportMap(daePath, linkDump, rootTexturesByFileName);

        // Reference-space split (ida-proven, docs/reverse/FFX_PHYRE_MAP_TEXTURE_RESOLUTION_RE_2026-06-06.md):
        // a serialized PAssetReference id is a single 0-based token, NOT a texture slot index. id < importCount =>
        // EXTERNAL import resolved against the PAssetReferenceImport table by name/path; id >= importCount =>
        // LOCAL in-file object (localIndex = id - importCount). For azit03 importCount = 84 and every map texture
        // is an external import (ids 39..83); the "unmapped" ids 84..92 are local PMaterial/PMesh/PMeshInstance
        // refs (offline-proven: PAssetReference outgoing object-links target PMaterial/PMesh/PMeshInstance and the
        // file contains ZERO PTexture2D object blocks). They reference no texture and must NOT be force-bound.
        var importBlock = linkDump.ObjectBlocks.FirstOrDefault(static block => block.Name == "PAssetReferenceImport");
        var importCount = importBlock?.ArrayLinks
            .Where(static link => link.ParentFieldOffset == 4)
            .Select(static link => (int)link.ParentObjId)
            .DefaultIfEmpty(-1)
            .Max() + 1 ?? 0;
        var hasLocalTexture2DObjects = linkDump.ObjectBlocks
            .Any(static block => block.Name == "PTexture2D");

        var pmeshBlock = linkDump.ObjectBlocks.FirstOrDefault(static block => block.Name == "PMesh");
        var pmaterialBlock = linkDump.ObjectBlocks.FirstOrDefault(static block => block.Name == "PMaterial");

        var pmeshMaterialByMeshAndFileMaterial = (pmeshBlock?.ObjectLinks ?? Array.Empty<PhyreObjectLinkReport>())
            .Where(static link => link.TargetBlockName == "PMaterial" && link.ParentFieldOffset == 52)
            .GroupBy(static link => ((int)link.ParentObjId, (int)link.TargetArrayCount))
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderBy(static link => link.TargetObjId).First());

        var materialToParameterBuffer = (pmaterialBlock?.ObjectLinks ?? Array.Empty<PhyreObjectLinkReport>())
            .Where(static link => link.TargetBlockName == "PParameterBuffer")
            .GroupBy(static link => (int)link.ParentObjId)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderBy(static link => link.TargetBlockId).First());

        var parameterBufferTextureByBlock = linkDump.ObjectBlocks
            .Where(static block => block.Name == "PParameterBuffer")
            .Select(block => BuildParameterBufferTextureCandidate(block, textureImportsBySharedDataId))
            .ToDictionary(static item => item.BlockId);
        var field172SharedDataIds = parameterBufferTextureByBlock.Values
            .Select(static item => item.SelectedLink?.SharedDataId)
            .Where(static id => id is not null)
            .Select(static id => id!.Value)
            .Distinct()
            .Order()
            .ToArray();
        var field172SharedDataIdSet = field172SharedDataIds.ToHashSet();
        var rootTexturesBySharedDataId = field172SharedDataIds.Length == orderedRootTextures.Length
            ? field172SharedDataIds
                .Zip(orderedRootTextures)
                .ToDictionary(static pair => pair.First, static pair => pair.Second)
            : new Dictionary<int, MapDecodedRootTexture>();
        var sharedDataTextureMap = rootTexturesBySharedDataId
            .OrderBy(static pair => pair.Key)
            .Select(static pair => new MapMaterialSlotSharedDataTextureMap(
                pair.Key,
                pair.Value.SlotIndex,
                pair.Value.SourcePath,
                pair.Value.PngPath,
                null,
                null))
            .Concat(textureImportsBySharedDataId.Values
                .Where(item => item.Texture is not null && field172SharedDataIdSet.Contains(item.SharedDataId))
                .OrderBy(static item => item.SharedDataId)
                .Select(static item => new MapMaterialSlotSharedDataTextureMap(
                    item.SharedDataId,
                    item.Texture!.SlotIndex,
                    item.Texture.SourcePath,
                    item.Texture.PngPath,
                    item.ImportType,
                    item.ImportPath)))
            .GroupBy(static item => item.SharedDataId)
            .Select(static group => group.First())
            .OrderBy(static item => item.SharedDataId)
            .ToArray();

        var rows = new List<MapMaterialSlotAnalysisRow>();
        var materialTextures = new List<DescriptorGltfMaterialTexture>();
        var namedSignalSummary = BuildNamedSignalSummary(daePath);

        foreach (var submesh in descriptor.Submeshes.OrderBy(static item => item.SubmeshId))
        {
            var status = "blocked_missing_mesh_id";
            int? globalMaterialId = null;
            int? parameterBufferBlockId = null;
            int? textureSharedDataId = null;
            int? textureSlotIndex = null;
            string? textureImportType = null;
            string? textureImportPath = null;
            MapDecodedRootTexture? texture = null;
            PhyreObjectLinkReport? textureLink = null;
            string? textureLinkTargetBlockName = null;
            string? textureSelectionRoute = null;
            PhyreObjectLinkReport[]? normalMapLinks = null;

            if (submesh.MeshId is int meshId)
            {
                var materialLinkRoute = "direct_pmesh_material_link";
                if (!pmeshMaterialByMeshAndFileMaterial.TryGetValue((meshId, submesh.FileMaterialId), out var materialLink)
                    && meshId > 0
                    && pmeshMaterialByMeshAndFileMaterial.TryGetValue((meshId - 1, submesh.FileMaterialId), out materialLink))
                {
                    materialLinkRoute = "paired_even_pmesh_material_link";
                }

                if (materialLink is null)
                {
                    status = "blocked_missing_pmesh_to_pmaterial_link";
                }
                else
                {
                    globalMaterialId = (int)materialLink.TargetObjId;
                    if (!materialToParameterBuffer.TryGetValue(globalMaterialId.Value, out var parameterBufferLink))
                    {
                        status = "blocked_missing_pmaterial_to_pparameterbuffer_link";
                    }
                    else
                    {
                        parameterBufferBlockId = (int)parameterBufferLink.TargetBlockId;
                        if (!parameterBufferTextureByBlock.TryGetValue(parameterBufferBlockId.Value, out var textureCandidate)
                            || textureCandidate.SelectedLink is null)
                        {
                            status = textureCandidate?.SelectionRoute == "multiple_texture_import_links"
                                ? "blocked_multiple_pparameterbuffer_texture_import_links"
                                : "blocked_missing_pparameterbuffer_texture_import_link";
                        }
                        else if (textureCandidate.SelectedLink.SharedDataId is not int sharedDataId)
                        {
                            status = "blocked_missing_texture_slot_shared_data_id";
                        }
                        else
                        {
                            textureLink = textureCandidate.SelectedLink;
                            textureLinkTargetBlockName = textureLink.TargetBlockName;
                            textureSelectionRoute = textureCandidate.SelectionRoute;
                            normalMapLinks = textureCandidate.AdditionalLinks;
                            textureSharedDataId = sharedDataId;
                            textureSlotIndex = sharedDataId;
                            if (textureImportsBySharedDataId.TryGetValue(sharedDataId, out var importReference))
                            {
                                textureImportType = importReference.ImportType;
                                textureImportPath = importReference.ImportPath;
                                texture = importReference.Texture;
                                if (textureImportType is not null
                                    && !string.Equals(textureImportType, "PTexture2D", StringComparison.Ordinal))
                                {
                                    status = "blocked_texture_import_type_not_ptexture2d";
                                }
                                else if (!IsDdsImportPath(textureImportPath))
                                {
                                    status = "blocked_texture_import_path_not_dds";
                                }
                                else if (texture is null)
                                {
                                    status = "blocked_texture_import_unmatched_to_root_3d_texture";
                                }
                                else
                                {
                                    textureSlotIndex = texture.SlotIndex;
                                }
                            }
                            else if (!rootTexturesBySlot.TryGetValue(sharedDataId, out texture))
                            {
                                if (!rootTexturesBySharedDataId.TryGetValue(sharedDataId, out texture))
                                {
                                    // Reference-space-aware honest classification: an id >= importCount is a LOCAL
                                    // in-file object reference, not an import-table texture slot. With no local
                                    // PTexture2D object in the file it cannot resolve to any decoded DDS, so report
                                    // it as a local non-texture ref instead of the misleading "unmapped_to_root_3d"
                                    // status (which implied a decoded texture was being missed). This is honest, not
                                    // a new bind: forcing a DDS here would fabricate albedo the engine never samples.
                                    status = importCount > 0 && sharedDataId >= importCount && !hasLocalTexture2DObjects
                                        ? "blocked_field172_local_object_ref_not_a_texture"
                                        : "blocked_texture_shared_data_id_unmapped_to_root_3d_texture";
                                }
                                else
                                {
                                    textureSlotIndex = texture.SlotIndex;
                                }
                            }

                            if (texture is null)
                            {
                                /* Special lane fallback: blocked rows with known water/reflection/cube
                                   import paths get a second chance via COLLADA source-path scanning */
                                if (status.StartsWith("blocked_", StringComparison.Ordinal)
                                    && textureImportPath is not null
                                    && textureImportPath.Contains(".dds", StringComparison.OrdinalIgnoreCase))
                                {
                                    var stem = FileNameFromPath(textureImportPath);
                                    if (rootTexturesByFileName.TryGetValue(stem, out var slTexture))
                                    {
                                        texture = slTexture;
                                        status = stem.Contains("water", StringComparison.OrdinalIgnoreCase)
                                            ? "bound_by_special_lane_water_texture_fallback_candidate"
                                            : stem.Contains("reflection", StringComparison.OrdinalIgnoreCase)
                                                ? "bound_by_special_lane_reflection_texture_fallback_candidate"
                                                : "bound_by_special_lane_texture_fallback_candidate";
                                    }
                                }
                            }
                            else if (texture.Format is null)
                            {
                                status = "blocked_texture_slot_decode_missing_format";
                            }
                            else
                            {
                                status = materialLinkRoute switch
                                {
                                    "paired_even_pmesh_material_link" => PairedEvenMeshMaterialBoundStatus,
                                    _ => textureSelectionRoute switch
                                    {
                                        "field172_asset_reference" => Field172BoundStatus,
                                        "lowest_offset_texture_import_link" => LowestOffsetTextureImportBoundStatus,
                                        _ => SingleTextureImportBoundStatus,
                                    },
                                };
                                /* Resolve normal map from additional texture links (second+ DDS import) */
                                string? normalMapPngPath = null;
                                int? normalMapWidth = null;
                                int? normalMapHeight = null;
                                if (normalMapLinks is not null)
                                {
                                    foreach (var nmLink in normalMapLinks)
                                    {
                                        if (nmLink.SharedDataId is int nmSdId
                                            && textureImportsBySharedDataId.TryGetValue(nmSdId, out var nmImport)
                                            && nmImport.Texture is not null
                                            && IsDdsImportPath(nmImport.ImportPath))
                                        {
                                            normalMapPngPath = nmImport.Texture.PngPath;
                                            normalMapWidth = nmImport.Texture.Width;
                                            normalMapHeight = nmImport.Texture.Height;
                                            break;
                                        }
                                    }
                                }
                                materialTextures.Add(new DescriptorGltfMaterialTexture(
                                    submesh.FileMaterialId,
                                    texture.PngPath,
                                    texture.Format,
                                    texture.Width,
                                    texture.Height,
                                    flipV,
                                    texture.SourcePath,
                                    submesh.SubmeshId,
                                    normalMapPngPath,
                                    normalMapWidth,
                                    normalMapHeight));
                            }
                        }
                    }
                }
            }

            rows.Add(new MapMaterialSlotAnalysisRow(
                submesh.SubmeshId,
                submesh.MeshId,
                submesh.FileMaterialId,
                globalMaterialId,
                parameterBufferBlockId,
                textureLink?.ParentFieldOffset,
                textureLinkTargetBlockName,
                textureSharedDataId,
                textureImportType,
                textureImportPath,
                textureSlotIndex,
                texture?.SourcePath,
                texture?.PngPath,
                status,
                BuildRoleCandidateTags(status, textureImportPath, textureLinkTargetBlockName)));
        }

        var allBound = rows.Count > 0
            && rows.All(static row => row.Status == Field172BoundStatus
                || row.Status == SingleTextureImportBoundStatus
                || row.Status == LowestOffsetTextureImportBoundStatus
                || row.Status == PairedEvenMeshMaterialBoundStatus);
        var report = new MapMaterialSlotAnalysisReport(
            DateTimeOffset.UtcNow,
            linkDump.AssetId,
            descriptor.Submeshes.Length,
            materialTextures.Count,
            172,
            "PMesh.parentFieldOffset=52 -> PMaterial, PMaterial -> PParameterBuffer; primary route: PParameterBuffer base-color PAssetReference at parentFieldOffset=172 (400-byte layout) OR 348 (608/616-byte layout, data-driven per PClassDescriptor member table) -> sharedData ref token; secondary route: a single PParameterBuffer object link whose sharedDataId resolves through PAssetReferenceImport to map tex/*.dds. Reference-space split (ida-proven, docs/reverse/FFX_PHYRE_MAP_TEXTURE_RESOLUTION_RE_2026-06-06.md): the ref token is unified, id<importCount => external import resolved by name/path, id>=importCount => LOCAL in-file object (localIndex=id-importCount), NOT a texture slot index. A local ref binds a texture only if the file holds a local PTexture2D object; otherwise it is honestly reported as blocked_field172_local_object_ref_not_a_texture rather than unmapped. IDA (FFX.exe.i64): field172/0xAC is a real Phyre reflection member; runtime binds base color by named shader param 'TextureSampler' (sub_69FB10->sub_56CD50->sub_66E680), not by hardcoded +0xAC. Adjacent named lanes such as TextureSamplerState/ShadowMapSampler, framebuffer-style params, and TextureSamplerY/U/V are treated as separate families, not automatic base-color proof.",
            sharedDataTextureMap,
            rows.ToArray(),
            BuildStatusBreakdown(rows),
            BuildClassificationSummary(rows),
            BuildRoleCandidateSummary(rows),
            namedSignalSummary,
            allBound ? "map_material_slots_texture_import_links_structural_candidate" : "blocked_incomplete_map_material_slot_binding",
            allBound
                ? "map each PParameterBuffer asset ref to the named lit-shader sampler (TextureSampler base vs sampler-state/shadow, framebuffer/post-process, and YUV/video side lanes) and optionally confirm by in-game draw watchpoint before engine_exact promotion"
                : "inspect missing or multiple texture-link rows before using Phyre slot binding");

        return new MapMaterialSlotAnalysisResult(report, materialTextures.ToArray());
    }

    // Base-color PAssetReference field offset is data-driven (PClassDescriptor 16-byte member table, desc+8 =
    // member byte offset; ida-proven generic walker sub_4D6D40 / named "TextureSampler" bind path
    // sub_69FB10->sub_56CD50->sub_66E680, see docs/reverse/FFX_PHYRE_SCENELOAD_MATERIAL_RE_2026-06-06.md).
    // azit03 carries two PParameterBuffer layouts: 400-byte buffers hold the ref at +172, 608/616-byte buffers
    // at +348 (offline-proven: phyre-link-dump.json has 118 pfo=172 + 32 pfo=348 texture PAssetReference links,
    // zero at any other non-zero offset). Both are the same "base color" family; accepting either is additive
    // (it never changes the +172 rows already bound) and lets the +348 layout resolve consistently.
    private static readonly uint[] BaseTextureAssetRefFieldOffsets = { 172, 348 };

    private static MapParameterBufferTextureCandidate BuildParameterBufferTextureCandidate(
        PhyreObjectLinkBlockReport block,
        IReadOnlyDictionary<int, MapTextureImportReference> textureImportsBySharedDataId)
    {
        // Prefer +172 (400-byte layout) then +348 (608/616-byte layout) so the historical +172 selection is
        // never displaced for a buffer that has both — preserving every already-bound row.
        foreach (var fieldOffset in BaseTextureAssetRefFieldOffsets)
        {
            var baseTextureRef = block.ObjectLinks.FirstOrDefault(link =>
                link.TargetBlockName == "PAssetReference"
                && link.ParentFieldOffset == fieldOffset
                && link.SharedDataId is not null);
            if (baseTextureRef is not null)
            {
                /* Also capture additional PAssetReference links at other offsets (potential normal/detail maps) */
                var additionalPAssetRefs = block.ObjectLinks
                    .Where(link => link.TargetBlockName == "PAssetReference"
                        && link.ParentFieldOffset != fieldOffset
                        && link.SharedDataId is not null
                        && textureImportsBySharedDataId.ContainsKey(link.SharedDataId.Value))
                    .ToArray();
                return new MapParameterBufferTextureCandidate(
                    block.Id, baseTextureRef, "field172_asset_reference",
                    additionalPAssetRefs.Length > 0 ? additionalPAssetRefs : null);
            }
        }

        var textureImportLinks = block.ObjectLinks
            .Where(link => link.SharedDataId is int sharedDataId
                && textureImportsBySharedDataId.TryGetValue(sharedDataId, out var importReference)
                && IsDdsImportPath(importReference.ImportPath))
            .ToArray();

        return textureImportLinks.Length switch
        {
            1 => new MapParameterBufferTextureCandidate(block.Id, textureImportLinks[0], "single_texture_import_link"),
            // Multiple DDS texture imports and no 172/348 base-color ref: the base color is the lowest
            // field-offset texture ref (TextureSampler bound first; higher offsets are normal/detail
            // sampler lanes). Lowest-offset disambiguation, additive — see route comment above.
            > 1 => new MapParameterBufferTextureCandidate(
                block.Id,
                textureImportLinks
                    .OrderBy(static link => link.ParentFieldOffset)
                    .ThenBy(static link => link.SharedDataId ?? int.MaxValue)
                    .First(),
                "lowest_offset_texture_import_link",
                textureImportLinks
                    .OrderBy(static link => link.ParentFieldOffset)
                    .ThenBy(static link => link.SharedDataId ?? int.MaxValue)
                    .Skip(1)
                    .ToArray()),
            _ => new MapParameterBufferTextureCandidate(block.Id, null, "missing_texture_import_link"),
        };
    }

    private static MapMaterialSlotStatusBreakdown[] BuildStatusBreakdown(IEnumerable<MapMaterialSlotAnalysisRow> rows) =>
        rows.GroupBy(static row => row.Status)
            .Select(group => new MapMaterialSlotStatusBreakdown(
                group.Key,
                ClassifyStatusFamily(group.Key),
                group.Count()))
            .OrderByDescending(static item => item.Count)
            .ThenBy(static item => item.Status, StringComparer.Ordinal)
            .ToArray();

    private static MapMaterialSlotClassificationSummary BuildClassificationSummary(IReadOnlyList<MapMaterialSlotAnalysisRow> rows)
    {
        static int CountStatus(IReadOnlyList<MapMaterialSlotAnalysisRow> source, params string[] statuses) =>
            source.Count(row => statuses.Contains(row.Status, StringComparer.Ordinal));

        return new MapMaterialSlotClassificationSummary(
            rows.Count,
            CountStatus(rows, Field172BoundStatus),
            CountStatus(rows, SingleTextureImportBoundStatus, LowestOffsetTextureImportBoundStatus),
            CountStatus(rows, "blocked_missing_mesh_id"),
            CountStatus(rows, "blocked_missing_pmesh_to_pmaterial_link"),
            CountStatus(rows, "blocked_missing_pmaterial_to_pparameterbuffer_link"),
            CountStatus(rows, "blocked_missing_pparameterbuffer_texture_import_link"),
            CountStatus(rows, "blocked_multiple_pparameterbuffer_texture_import_links"),
            CountStatus(rows, "blocked_missing_texture_slot_shared_data_id"),
            CountStatus(rows, "blocked_texture_import_type_not_ptexture2d", "blocked_texture_import_path_not_dds"),
            CountStatus(
                rows,
                "blocked_texture_import_unmatched_to_root_3d_texture",
                "blocked_texture_shared_data_id_unmapped_to_root_3d_texture",
                "blocked_field172_local_object_ref_not_a_texture"),
            CountStatus(rows, "blocked_texture_slot_decode_missing_format"),
            rows.Count(row => IsReflectionImportPath(row.TextureImportPath)),
            rows.Count(row => IsWaterTextureImportPath(row.TextureImportPath)),
            rows.Count(row => IsShaderDrivenImportPath(row.TextureImportPath)),
            rows.Count(row => row.Status.StartsWith("blocked_", StringComparison.Ordinal))
                - CountStatus(rows,
                    "blocked_missing_mesh_id",
                    "blocked_missing_pmesh_to_pmaterial_link",
                    "blocked_missing_pmaterial_to_pparameterbuffer_link",
                    "blocked_missing_pparameterbuffer_texture_import_link",
                    "blocked_multiple_pparameterbuffer_texture_import_links",
                    "blocked_missing_texture_slot_shared_data_id",
                    "blocked_texture_import_type_not_ptexture2d",
                    "blocked_texture_import_path_not_dds",
                    "blocked_texture_import_unmatched_to_root_3d_texture",
                    "blocked_texture_shared_data_id_unmapped_to_root_3d_texture",
                    "blocked_field172_local_object_ref_not_a_texture",
                    "blocked_texture_slot_decode_missing_format"));
    }

    private static MapMaterialNamedSignalSummary BuildNamedSignalSummary(string daePath)
    {
        if (!File.Exists(daePath))
        {
            return new MapMaterialNamedSignalSummary(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        var text = Encoding.Latin1.GetString(File.ReadAllBytes(daePath));
        return new MapMaterialNamedSignalSummary(
            CountSignalOccurrences(text, "TextureSampler"),
            CountSignalOccurrences(text, "TextureSamplerState"),
            CountSignalOccurrences(text, "ShadowMapSampler"),
            CountSignalOccurrences(text, "FrameBuffer")
                + CountSignalOccurrences(text, "RealFrameBuffer")
                + CountSignalOccurrences(text, "D3D11FrameBuffer"),
            CountSignalOccurrences(text, "TargetTexture"),
            CountSignalOccurrences(text, "CCTexture"),
            CountSignalOccurrences(text, "UvScaleBias"),
            CountSignalOccurrences(text, "TextureSamplerY")
                + CountSignalOccurrences(text, "TextureSamplerU")
                + CountSignalOccurrences(text, "TextureSamplerV"),
            CountSignalOccurrences(text, "zCrct")
                + CountSignalOccurrences(text, "zMatrix")
                + CountSignalOccurrences(text, "ScreenShift"),
            CountSignalOccurrences(text, ".fx#"));
    }

    private static int CountOrdinalOccurrences(string text, string token)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(token))
        {
            return 0;
        }

        var count = 0;
        var start = 0;
        while (start < text.Length)
        {
            var index = text.IndexOf(token, start, StringComparison.Ordinal);
            if (index < 0)
            {
                break;
            }

            count += 1;
            start = index + token.Length;
        }

        return count;
    }

    private static int CountSignalOccurrences(string text, string token)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(token))
        {
            return 0;
        }

        return Regex.Matches(
            text,
            $@"(?<![A-Za-z0-9_]){Regex.Escape(token)}(?![A-Za-z0-9_])",
            RegexOptions.CultureInvariant).Count;
    }

    private static string[] BuildRoleCandidateTags(
        string status,
        string? textureImportPath,
        string? textureLinkTargetBlockName)
    {
        var tags = new List<string>();

        if (string.Equals(status, Field172BoundStatus, StringComparison.Ordinal))
        {
            tags.Add("base_texture_sampler_candidate");
        }

        if (string.Equals(status, PairedEvenMeshMaterialBoundStatus, StringComparison.Ordinal))
        {
            tags.Add("paired_even_pmesh_material_candidate");
            tags.Add("base_texture_sampler_candidate");
        }

        if (string.Equals(status, SingleTextureImportBoundStatus, StringComparison.Ordinal)
            || string.Equals(status, LowestOffsetTextureImportBoundStatus, StringComparison.Ordinal))
        {
            tags.Add("single_import_candidate");
            if (string.Equals(status, LowestOffsetTextureImportBoundStatus, StringComparison.Ordinal))
            {
                tags.Add("lowest_offset_import_candidate");
            }

            if (!string.IsNullOrWhiteSpace(textureLinkTargetBlockName)
                && !string.Equals(textureLinkTargetBlockName, "PAssetReference", StringComparison.Ordinal))
            {
                tags.Add("non_base_target_block_candidate");
            }
        }

        if (IsReflectionImportPath(textureImportPath))
        {
            tags.Add("reflection_texture_candidate");
        }

        if (IsWaterTextureImportPath(textureImportPath))
        {
            tags.Add("water_texture_candidate");
        }

        if (IsShaderDrivenImportPath(textureImportPath))
        {
            tags.Add("shader_driven_candidate");
        }

        return tags.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static MapMaterialRoleCandidateSummary BuildRoleCandidateSummary(IReadOnlyList<MapMaterialSlotAnalysisRow> rows)
    {
        static int CountTag(IReadOnlyList<MapMaterialSlotAnalysisRow> source, string tag) =>
            source.Count(row => row.RoleCandidateTags.Contains(tag, StringComparer.Ordinal));

        return new MapMaterialRoleCandidateSummary(
            CountTag(rows, "base_texture_sampler_candidate"),
            CountTag(rows, "single_import_candidate"),
            CountTag(rows, "non_base_target_block_candidate"),
            CountTag(rows, "reflection_texture_candidate"),
            CountTag(rows, "water_texture_candidate"),
            CountTag(rows, "shader_driven_candidate"));
    }

    private static string ClassifyStatusFamily(string status) => status switch
    {
        Field172BoundStatus => "bound_base_texture_sampler",
        SingleTextureImportBoundStatus => "bound_import_link_candidate",
        LowestOffsetTextureImportBoundStatus => "bound_lowest_offset_import_link_candidate",
        PairedEvenMeshMaterialBoundStatus => "bound_paired_even_pmesh_material_link",
        "blocked_missing_mesh_id" => "missing_mesh",
        "blocked_missing_pmesh_to_pmaterial_link" => "missing_material_link",
        "blocked_missing_pmaterial_to_pparameterbuffer_link" => "missing_parameter_buffer_link",
        "blocked_missing_pparameterbuffer_texture_import_link" => "missing_texture_link",
        "blocked_multiple_pparameterbuffer_texture_import_links" => "multiple_texture_links",
        "blocked_missing_texture_slot_shared_data_id" => "missing_shared_data_id",
        "blocked_texture_import_type_not_ptexture2d" => "special_or_wrong_texture_type",
        "blocked_texture_import_path_not_dds" => "special_or_wrong_texture_type",
        "blocked_texture_import_unmatched_to_root_3d_texture" => "unmapped_texture_reference",
        "blocked_texture_shared_data_id_unmapped_to_root_3d_texture" => "unmapped_texture_reference",
        "blocked_field172_local_object_ref_not_a_texture" => "local_object_reference_not_texture",
        "blocked_texture_slot_decode_missing_format" => "decode_issue",
        _ when status.StartsWith("blocked_", StringComparison.Ordinal) => "other_blocked",
        _ => "other",
    };

    private static bool IsReflectionImportPath(string? importPath) =>
        !string.IsNullOrWhiteSpace(importPath)
        && importPath.Contains("reflection", StringComparison.OrdinalIgnoreCase);

    private static bool IsWaterTextureImportPath(string? importPath) =>
        !string.IsNullOrWhiteSpace(importPath)
        && (importPath.Contains("Water_", StringComparison.OrdinalIgnoreCase)
            || importPath.Contains("_Water_", StringComparison.OrdinalIgnoreCase)
            || importPath.Contains("/water", StringComparison.OrdinalIgnoreCase)
            || importPath.Contains("\\water", StringComparison.OrdinalIgnoreCase));

    private static bool IsShaderDrivenImportPath(string? importPath) =>
        !string.IsNullOrWhiteSpace(importPath)
        && (importPath.Contains("PS3Data/Shaders/", StringComparison.OrdinalIgnoreCase)
            || importPath.Contains("Shaders/", StringComparison.OrdinalIgnoreCase)
            || importPath.Contains(".fx#", StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyDictionary<int, MapTextureImportReference> BuildTextureImportMap(
        string daePath,
        PhyreLinkDumpReport linkDump,
        IReadOnlyDictionary<string, MapDecodedRootTexture> rootTexturesByFileName)
    {
        var imports = new Dictionary<int, MapTextureImportReference>();
        if (!File.Exists(daePath))
        {
            return imports;
        }

        var importBlock = linkDump.ObjectBlocks.FirstOrDefault(static block => block.Name == "PAssetReferenceImport");
        if (importBlock is null)
        {
            return imports;
        }

        var bytes = File.ReadAllBytes(daePath);
        var importTypes = importBlock.ObjectLinks
            .Where(static link => link.TargetBlockName == "PAssetReference" && link.SharedDataText is not null)
            .GroupBy(static link => (int)link.ParentObjId)
            .ToDictionary(static group => group.Key, static group => group.First().SharedDataText);
        var dataStart = importBlock.DataOffset;
        var dataEnd = importBlock.DataOffset + importBlock.DataSize;

        foreach (var arrayLink in importBlock.ArrayLinks.Where(static link => link.ParentFieldOffset == 4))
        {
            var sharedDataId = (int)arrayLink.ParentObjId;
            var importPath = ReadEmbeddedImportString(bytes, dataStart + arrayLink.Offset, dataStart, dataEnd);
            var importType = importTypes.TryGetValue(sharedDataId, out var type) ? type : null;
            MapDecodedRootTexture? texture = null;
            var textureFileName = TextureFileNameFromImportPath(importPath);
            if (textureFileName is not null)
            {
                rootTexturesByFileName.TryGetValue(textureFileName, out texture);
            }

            imports[sharedDataId] = new MapTextureImportReference(
                sharedDataId,
                importType,
                importPath,
                texture);
        }

        return imports;
    }

    private static string ReadEmbeddedImportString(byte[] bytes, long rawOffset, long minOffset, long maxOffset)
    {
        if (rawOffset < 0 || rawOffset >= bytes.Length)
        {
            return "";
        }

        var start = checked((int)rawOffset);
        var min = checked((int)Math.Clamp(minOffset, 0, bytes.Length));
        while (start > min && bytes[start - 1] != 0)
        {
            start--;
        }

        var endLimit = checked((int)Math.Clamp(maxOffset, start, bytes.Length));
        var end = start;
        while (end < endLimit && bytes[end] != 0)
        {
            end++;
        }

        return Encoding.ASCII.GetString(bytes, start, end - start).Replace('\\', '/');
    }

    private static string? TextureFileNameFromImportPath(string importPath)
    {
        var fileName = FileNameFromPath(importPath);
        if (fileName.EndsWith(".dds.phyre", StringComparison.OrdinalIgnoreCase))
        {
            return fileName;
        }

        return fileName.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)
            ? fileName + ".phyre"
            : null;
    }

    private static bool IsDdsImportPath(string? importPath) =>
        !string.IsNullOrWhiteSpace(importPath)
        && importPath.EndsWith(".dds", StringComparison.OrdinalIgnoreCase);

    private static string FileNameFromPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        var index = normalized.LastIndexOf('/');
        return index >= 0 ? normalized[(index + 1)..] : normalized;
    }

    private static string[] EnumerateTextureBucket(string ps3Root, string area, string bucket)
    {
        var directory = Path.Combine(ps3Root, area.Replace('/', Path.DirectorySeparatorChar), bucket.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(directory, "*.dds.phyre", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /* Scan .dae.phyre for special lane texture references (water, reflection, screen, cube) that exist
       on disk as .dds.phyre but are NOT in the PAssetReferenceImport table (they are referenced by
       COLLADA source-path only, not by structured import link). Returns paths found in the binary. */
    private static IReadOnlySet<string> ScanSpecialLaneTextureRefs(string daePath)
    {
        if (!File.Exists(daePath)) return new HashSet<string>();
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var bytes = File.ReadAllBytes(daePath);
            /* Scan for tex/<name>.dds ASCII substrings — COLLADA source path references */
            for (int i = 0; i < bytes.Length - 32; i++)
            {
                if (bytes[i] == (byte)'t' && i + 3 < bytes.Length
                    && bytes[i + 1] == (byte)'e' && bytes[i + 2] == (byte)'x'
                    && bytes[i + 3] == (byte)'/')
                {
                    int end = i + 4;
                    while (end < bytes.Length && bytes[end] >= 32 && bytes[end] != 0) end++;
                    if (end > i + 4)
                    {
                        var name = System.Text.Encoding.ASCII.GetString(bytes, i, end - i);
                        if (name.Contains(".dds", StringComparison.OrdinalIgnoreCase))
                        {
                            var fileName = Path.GetFileNameWithoutExtension(name);
                            if (!string.IsNullOrEmpty(fileName))
                                result.Add(fileName);
                        }
                    }
                }
            }
        }
        catch { }
        return result;
    }

    /* Scan the tex bucket for special-lane .dds.phyre files matching names seen in the DAE. */
    private static IReadOnlyDictionary<string, string> FindSpecialLaneTextures(
        string ps3Root, string area, string daePath)
    {
        var daeRefs = ScanSpecialLaneTextureRefs(daePath);
        if (daeRefs.Count == 0) return new Dictionary<string, string>();

        var bucketDir = Path.Combine(ps3Root, area.Replace('/', Path.DirectorySeparatorChar), "tex/d3d11");
        if (!Directory.Exists(bucketDir)) return new Dictionary<string, string>();

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(bucketDir, "*.dds.phyre", SearchOption.TopDirectoryOnly))
        {
            var stem = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(file)) ?? "";
            if (daeRefs.Contains(stem))
            {
                result[stem] = file;
            }
        }
        return result;
    }

    private static IEnumerable<DecodedMapTexture> ExtractBucket(IEnumerable<string> sourcePaths, string outputRoot, string assetId, string bucket, bool portable)
    {
        foreach (var sourcePath in sourcePaths)
        {
            var stem = StripDdsPhyreExtension(Path.GetFileName(sourcePath));
            var pngPath = Path.Combine(outputRoot, "textures", bucket, $"{stem}.png");
            PhyreTextureFileExtractionReport report;
            try
            {
                report = PhyreTextureExtractor.ExtractTextureFile(
                    sourcePath,
                    pngPath,
                    assetId,
                    portable,
                    "texture_png_extracted_candidate",
                    "map .dds.phyre decoded to PNG mip0; material binding remains candidate until Phyre material slots are decoded");
            }
            catch (Exception exception)
            {
                report = PhyreTextureFileExtractionReport.Blocked(
                    assetId,
                    FileSnapshot.FromPath(sourcePath, portable),
                    "blocked_texture_decode_exception",
                    exception.Message);
            }

            yield return new DecodedMapTexture(bucket, sourcePath, pngPath, report);
        }
    }

    private static string StripDdsPhyreExtension(string fileName) =>
        fileName.EndsWith(".dds.phyre", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^".dds.phyre".Length]
            : Path.GetFileNameWithoutExtension(fileName);

    private sealed record DecodedMapTexture(
        string Bucket,
        string SourcePath,
        string LocalPngPath,
        PhyreTextureFileExtractionReport Report);

    private sealed record MapTextureImportReference(
        int SharedDataId,
        string? ImportType,
        string ImportPath,
        MapDecodedRootTexture? Texture);

    private sealed record MapParameterBufferTextureCandidate(
        int BlockId,
        PhyreObjectLinkReport? SelectedLink,
        string SelectionRoute,
        PhyreObjectLinkReport[]? AdditionalLinks = null);
}

public sealed record MapTextureBindingResult(
    MapTextureBindingReport Report,
    DescriptorGltfMaterialTexture[] MaterialTextures,
    string PrimaryTextureLayer,
    MapDecodedRootTexture[] Root3DTextures);

public sealed record MapDecodedRootTexture(
    int SlotIndex,
    string SourcePath,
    string PngPath,
    string? Format,
    int Width,
    int Height,
    string DecisionBand);

public sealed record MapMaterialSlotAnalysisResult(
    MapMaterialSlotAnalysisReport Report,
    DescriptorGltfMaterialTexture[] MaterialTextures);

public sealed record MapMaterialSlotAnalysisReport(
    DateTimeOffset GeneratedAtUtc,
    string AssetId,
    int SubmeshCount,
    int BoundSubmeshCount,
    int TextureSlotParentFieldOffset,
    string EvidenceRoute,
    MapMaterialSlotSharedDataTextureMap[] SharedDataTextureMap,
    MapMaterialSlotAnalysisRow[] Rows,
    MapMaterialSlotStatusBreakdown[] StatusBreakdown,
    MapMaterialSlotClassificationSummary ClassificationSummary,
    MapMaterialRoleCandidateSummary RoleCandidateSummary,
    MapMaterialNamedSignalSummary NamedSignalSummary,
    string DecisionBand,
    string NextStrike);

public sealed record MapMaterialSlotSharedDataTextureMap(
    int SharedDataId,
    int RootTextureSlotIndex,
    string TextureSourcePath,
    string TexturePngPath,
    string? TextureImportType,
    string? TextureImportPath);

public sealed record MapMaterialSlotAnalysisRow(
    int SubmeshId,
    int? MeshId,
    int FileMaterialId,
    int? GlobalMaterialId,
    int? ParameterBufferBlockId,
    uint? TextureParentFieldOffset,
    string? TextureLinkTargetBlockName,
    int? TextureSharedDataId,
    string? TextureImportType,
    string? TextureImportPath,
    int? TextureSlotIndex,
    string? TextureSourcePath,
    string? TexturePngPath,
    string Status,
    string[] RoleCandidateTags);

public sealed record MapMaterialSlotStatusBreakdown(
    string Status,
    string StatusFamily,
    int Count);

public sealed record MapMaterialSlotClassificationSummary(
    int TotalRows,
    int BoundBaseTextureSamplerRows,
    int BoundImportLinkRows,
    int MissingMeshRows,
    int MissingMaterialLinkRows,
    int MissingParameterBufferLinkRows,
    int MissingTextureLinkRows,
    int MultipleTextureLinkRows,
    int MissingSharedDataIdRows,
    int SpecialOrWrongTextureTypeRows,
    int UnmappedTextureReferenceRows,
    int DecodeIssueRows,
    int ReflectionTextureRows,
    int WaterTextureRows,
    int ShaderDrivenTextureRows,
    int OtherBlockedRows);

public sealed record MapMaterialNamedSignalSummary(
    int TextureSamplerSignals,
    int TextureSamplerStateSignals,
    int ShadowMapSamplerSignals,
    int FrameBufferSignals,
    int TargetTextureSignals,
    int CcTextureSignals,
    int UvScaleBiasSignals,
    int YuvSamplerSignals,
    int ScreenVideoSignals,
    int AssetShaderPathSignals);

public sealed record MapMaterialRoleCandidateSummary(
    int BaseTextureSamplerCandidateRows,
    int SingleImportCandidateRows,
    int NonBaseTargetBlockCandidateRows,
    int ReflectionTextureCandidateRows,
    int WaterTextureCandidateRows,
    int ShaderDrivenCandidateRows);

public sealed record MapTextureBindingReport(
    DateTimeOffset GeneratedAtUtc,
    string Area,
    string AssetId,
    string PrimaryAssetLayer,
    string PrimaryTextureLayer,
    int[] FileMaterialIds,
    string[] Root3DTexturePaths,
    string[] TwoDTexturePaths,
    string[] FpTexturePaths,
    PhyreTextureFileExtractionReport[] ExtractionReports,
    MapBoundMaterialTexture[] BoundMaterials,
    string DecisionBand,
    string NextStrike);

public sealed record MapBoundMaterialTexture(
    int FileMaterialId,
    string? SourcePath,
    string? PngPath,
    string? Format,
    int Width,
    int Height,
    string Binding,
    int? SubmeshId = null);
