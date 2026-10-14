using System.Text;
using System.Text.Json;

namespace PhyreModelExportLab;

public static class DescriptorStaticGltfWriter
{
    private static readonly double[][] DebugColors =
    {
        new[] { 0.95, 0.42, 0.31, 1.0 },
        new[] { 0.28, 0.62, 0.91, 1.0 },
        new[] { 0.36, 0.75, 0.48, 1.0 },
        new[] { 0.93, 0.74, 0.27, 1.0 },
        new[] { 0.65, 0.47, 0.86, 1.0 },
        new[] { 0.35, 0.78, 0.78, 1.0 },
        new[] { 0.88, 0.51, 0.72, 1.0 },
        new[] { 0.58, 0.64, 0.70, 1.0 },
    };

    public static DescriptorStaticGltfExportReport Write(
        string path,
        string monsterId,
        byte[] source,
        PhyreDescriptorReport descriptor,
        DescriptorGltfTexture? texture = null,
        IReadOnlyList<DescriptorGltfMaterialTexture>? materialTextures = null,
        bool includeVertexColors = false,
        bool nodePerObject = false,
        bool groupByMeshInstance = false,
        IReadOnlyDictionary<int, int>? submeshToInstance = null,
        IReadOnlyDictionary<int, string?>? instanceNames = null,
        bool unboundUseVertexColor = false,
        IReadOnlySet<int>? unboundVertexColorSubmeshIds = null,
        bool unboundNeutralGrayFallback = false)
    {
        if (descriptor.DecisionBand != "mesh_descriptor_decode_candidate")
        {
            return DescriptorStaticGltfExportReport.Blocked(monsterId, descriptor.DecisionBand, "descriptor report did not pass mesh candidate gates");
        }

        var buffer = new List<byte>();
        var bufferViews = new List<object>();
        var accessors = new List<object>();
        var primitives = new List<object>();
        var primitiveIdentities = new List<(int SubmeshId, int? MeshId, int FileMaterialId)>();
        var materials = new List<object>();
        var totalVertices = 0;
        var totalIndices = 0;
        var primitiveVertexColorCount = 0;
        var textureSet = BuildTextureSet(path, monsterId, texture, materialTextures);

        for (var primitiveIndex = 0; primitiveIndex < descriptor.Submeshes.Length; primitiveIndex++)
        {
            var submesh = descriptor.Submeshes[primitiveIndex];
            var position = submesh.Components.FirstOrDefault(component =>
                component.ComponentType is "Vertex" or "SkinnableVertex"
                && component.PrimType == "Float"
                && component.PrimElementCount >= 3
                && component.PositionBounds is not null);

            if (position is null)
            {
                throw new InvalidOperationException($"Submesh {submesh.SubmeshId} has no descriptor-backed POSITION stream.");
            }

            if (submesh.IndexOutOfRangeCount is null or > 0)
            {
                throw new InvalidOperationException($"Submesh {submesh.SubmeshId} has invalid indices: outOfRange={submesh.IndexOutOfRangeCount?.ToString() ?? "unknown"}.");
            }

            var positionBytes = CopyTightFloat3(source, position.VertexDataOffset, checked((int)position.ElementCount), checked((int)position.ElementSize));
            var positionOffset = buffer.Count;
            buffer.AddRange(positionBytes);
            Align(buffer, 4);
            var positionBufferView = bufferViews.Count;
            bufferViews.Add(new { buffer = 0, byteOffset = positionOffset, byteLength = positionBytes.Length, target = 34962 });
            var positionAccessor = accessors.Count;
            accessors.Add(new
            {
                bufferView = positionBufferView,
                byteOffset = 0,
                componentType = 5126,
                count = checked((int)position.ElementCount),
                type = "VEC3",
                min = submesh.PositionBounds!.Min,
                max = submesh.PositionBounds.Max,
            });

            int? texcoordAccessor = null;
            if (textureSet.HasTextures)
            {
                var texcoord = submesh.Components.FirstOrDefault(component =>
                    component.ComponentType == "ST"
                    && component.PrimType == "Float"
                    && component.PrimElementCount >= 2
                    && component.ElementCount == position.ElementCount);

                if (texcoord is not null)
                {
                    var texcoordBytes = CopyTightFloat2(source, texcoord.VertexDataOffset, checked((int)texcoord.ElementCount), checked((int)texcoord.ElementSize), textureSet.FlipV);
                    var texcoordOffset = buffer.Count;
                    buffer.AddRange(texcoordBytes);
                    Align(buffer, 4);
                    var texcoordBufferView = bufferViews.Count;
                    bufferViews.Add(new { buffer = 0, byteOffset = texcoordOffset, byteLength = texcoordBytes.Length, target = 34962 });
                    texcoordAccessor = accessors.Count;
                    accessors.Add(new
                    {
                        bufferView = texcoordBufferView,
                        byteOffset = 0,
                        componentType = 5126,
                        count = checked((int)texcoord.ElementCount),
                        type = "VEC2",
                    });
                }
            }

            // Resolve the base-color texture binding up-front so the vertex-color path below can decide whether
            // this submesh is UNBOUND (no decoded DDS). UNBOUND-with-real-Color is the azit03 "18 shader
            // materials" case: PhyreDefaultLitShader surfaces whose color is the per-vertex Color/VtxColor
            // stream, not a flat MaterialColour constant (MaterialColour's GPU CB offset 1008/1168 is BEYOND the
            // serialized 400/608-byte PParameterBuffer — it is genuinely NOT in the file). See
            // docs/reverse/FFX_PHYRE_MAP_SHADER_MATERIAL_VERTEXCOLOR_AZIT03_2026-06-06.md.
            var boundTextureIndex = textureSet.ResolveTextureIndex(submesh.SubmeshId, submesh.FileMaterialId);
            // Gate the unbound-vertex-color path to an explicit allow-set when one is supplied (azit03: the 18
            // PhyreDefaultLitShader .fx# materials only). Unbound submeshes NOT in the set (e.g. the 23 local
            // object-ref materials) stay on the honest pastel placeholder — deferred to the in-game RenderDoc
            // ground-truth protocol, not guessed offline. A null set means "all unbound" (legacy/other maps).
            var unboundVertexColorAllowed = unboundUseVertexColor
                && (unboundVertexColorSubmeshIds is null || unboundVertexColorSubmeshIds.Contains(submesh.SubmeshId));
            var emitVertexColor = includeVertexColors || (unboundVertexColorAllowed && boundTextureIndex is null);

            int? colorAccessor = null;
            if (emitVertexColor)
            {
                var color = submesh.Components.FirstOrDefault(component =>
                    component.ComponentType == "Color"
                    && component.PrimType == "Float"
                    && component.PrimElementCount >= 4
                    && component.ElementCount == position.ElementCount);

                if (color is not null)
                {
                    var colorBytes = CopyTightFloat4(source, color.VertexDataOffset, checked((int)color.ElementCount), checked((int)color.ElementSize));
                    var colorOffset = buffer.Count;
                    buffer.AddRange(colorBytes);
                    Align(buffer, 4);
                    var colorBufferView = bufferViews.Count;
                    bufferViews.Add(new { buffer = 0, byteOffset = colorOffset, byteLength = colorBytes.Length, target = 34962 });
                    colorAccessor = accessors.Count;
                    accessors.Add(new
                    {
                        bufferView = colorBufferView,
                        byteOffset = 0,
                        componentType = 5126,
                        count = checked((int)color.ElementCount),
                        type = "VEC4",
                    });
                    primitiveVertexColorCount++;
                }
            }

            var indexBytes = CopyBytes(source, submesh.IndexDataOffset, checked(submesh.IndexCount * 2));
            var indexOffset = buffer.Count;
            buffer.AddRange(indexBytes);
            Align(buffer, 4);
            var indexBufferView = bufferViews.Count;
            bufferViews.Add(new { buffer = 0, byteOffset = indexOffset, byteLength = indexBytes.Length, target = 34963 });
            var indexAccessor = accessors.Count;
            accessors.Add(new
            {
                bufferView = indexBufferView,
                byteOffset = 0,
                componentType = 5123,
                count = submesh.IndexCount,
                type = "SCALAR",
                min = new[] { 0 },
                max = new[] { submesh.MaxIndex ?? 0 },
            });

            var materialIndex = materials.Count;
            if (boundTextureIndex is null)
            {
                if (colorAccessor is not null && (unboundVertexColorAllowed || includeVertexColors))
                {
                    // UNBOUND but carries a real per-vertex Color stream: render the ACTUAL baked color
                    // (white baseColorFactor so COLOR_0 passes through unmodified — NO pastel tint). This is
                    // the engine-faithful albedo for PhyreDefaultLitShader vertex-color surfaces and replaces
                    // the garish DebugColors placeholder with real file data. Honest: candidate until draw-time.
                    materials.Add(new
                    {
                        name = $"submesh_{submesh.SubmeshId}_vertexcolor_candidate",
                        pbrMetallicRoughness = new
                        {
                            baseColorFactor = new[] { 1.0, 1.0, 1.0, 1.0 },
                            metallicFactor = 0,
                            roughnessFactor = 0.75,
                        },
                        doubleSided = true,
                    });
                }
                else
                {
                    var fallback = unboundNeutralGrayFallback
                        ? new[] { 0.42, 0.42, 0.42, 1.0 }
                        : DebugColors[primitiveIndex % DebugColors.Length];
                    materials.Add(new
                    {
                        name = $"submesh_{submesh.SubmeshId}_debug",
                        pbrMetallicRoughness = new
                        {
                            baseColorFactor = fallback,
                            metallicFactor = 0,
                            roughnessFactor = 0.75,
                        },
                        doubleSided = true,
                        extensions = new Dictionary<string, object> { ["KHR_materials_unlit"] = new { } },
                    });
                }
            }
            else
            {
                var normalTextureIndex = textureSet.ResolveNormalIndex(submesh.SubmeshId, submesh.FileMaterialId);
                materials.Add(new
                {
                    name = $"submesh_{submesh.SubmeshId}_material_{submesh.FileMaterialId}_texture_candidate",
                    pbrMetallicRoughness = new
                    {
                        baseColorTexture = new { index = boundTextureIndex.Value, texCoord = 0 },
                        metallicFactor = 0,
                        roughnessFactor = 0.75,
                    },
                    normalTexture = normalTextureIndex is not null ? new { index = normalTextureIndex.Value, texCoord = 0 } : null,
                    alphaMode = "OPAQUE",
                    doubleSided = true,
                });
            }

            var attributes = new Dictionary<string, int> { ["POSITION"] = positionAccessor };
            if (texcoordAccessor is not null)
            {
                attributes["TEXCOORD_0"] = texcoordAccessor.Value;
            }
            if (colorAccessor is not null)
            {
                attributes["COLOR_0"] = colorAccessor.Value;
            }

            primitives.Add(new
            {
                attributes,
                indices = indexAccessor,
                mode = 4,
                material = materialIndex,
                extras = new
                {
                    submesh.SubmeshId,
                    submesh.MeshId,
                    submesh.FileMaterialId,
                    submesh.IndexCount,
                    positionVertexCount = position.ElementCount,
                    hasTexcoord0 = texcoordAccessor is not null,
                    hasColor0 = colorAccessor is not null,
                    source = "PMeshSegment -> PDataBlock -> PVertexStream",
                },
            });
            primitiveIdentities.Add((submesh.SubmeshId, submesh.MeshId, submesh.FileMaterialId));

            totalVertices += checked((int)position.ElementCount);
            totalIndices += submesh.IndexCount;
        }

        // Scene-graph layout. Default (nodePerObject == false) is the legacy
        // 1 scene / 1 node / 1 mesh / N primitives layout and MUST stay
        // byte-identical for the shared monster + map export callers.
        // Opt-in (nodePerObject == true) emits one node + one single-primitive
        // mesh per submesh batch, giving each object-batch its own addressable
        // glTF node with a stable, identity-carrying name. See
        // docs/ai/FFX_MAP_NODE_PER_OBJECT_WRITER_SPEC_2026-06-06.md.
        object[] sceneNodes;
        object[] sceneMeshes;
        int[] sceneRootNodeIndices;
        if (groupByMeshInstance && submeshToInstance is not null && submeshToInstance.Count > 0 && primitives.Count > 0)
        {
            var byInstance = new SortedDictionary<int, List<int>>();
            for (var i = 0; i < primitives.Count; i++)
            {
                var submeshId = primitiveIdentities[i].SubmeshId;
                var instanceId = submeshToInstance.TryGetValue(submeshId, out var mapped) ? mapped : submeshId;
                if (!byInstance.TryGetValue(instanceId, out var list))
                {
                    list = new List<int>();
                    byInstance[instanceId] = list;
                }

                list.Add(i);
            }

            var nodeList = new List<object>(byInstance.Count);
            var meshList = new List<object>(byInstance.Count);
            var rootList = new List<int>(byInstance.Count);
            var meshIndex = 0;
            foreach (var (instanceId, primitiveIndices) in byInstance)
            {
                string? instanceName = null;
                instanceNames?.TryGetValue(instanceId, out instanceName);
                var label = string.IsNullOrWhiteSpace(instanceName)
                    ? $"{monsterId}_mesh_instance_{instanceId}"
                    : $"{monsterId}_{instanceName}";
                var instancePrimitives = primitiveIndices.Select(index => primitives[index]).ToArray();
                var submeshIds = primitiveIndices.Select(index => primitiveIdentities[index].SubmeshId).ToArray();
                meshList.Add(new
                {
                    name = label,
                    primitives = instancePrimitives,
                });
                nodeList.Add(new
                {
                    name = label,
                    mesh = meshIndex,
                    extras = new
                    {
                        meshInstanceId = instanceId,
                        submeshIds,
                        objectIdentity = "pmesh_instance_group_candidate",
                    },
                });
                rootList.Add(meshIndex);
                meshIndex++;
            }

            sceneNodes = nodeList.ToArray();
            sceneMeshes = meshList.ToArray();
            sceneRootNodeIndices = rootList.ToArray();
        }
        else if (nodePerObject && primitives.Count > 0)
        {
            var nodeList = new List<object>(primitives.Count);
            var meshList = new List<object>(primitives.Count);
            var rootList = new List<int>(primitives.Count);
            for (var i = 0; i < primitives.Count; i++)
            {
                var identity = primitiveIdentities[i];
                var meshSuffix = identity.MeshId is { } meshId ? $"_mesh_{meshId}" : string.Empty;
                var nodeName = $"{monsterId}_object_submesh_{identity.SubmeshId}{meshSuffix}_material_{identity.FileMaterialId}";
                meshList.Add(new
                {
                    name = nodeName,
                    primitives = new[] { primitives[i] },
                });
                nodeList.Add(new
                {
                    name = nodeName,
                    mesh = i,
                    extras = new
                    {
                        identity.SubmeshId,
                        identity.MeshId,
                        identity.FileMaterialId,
                        objectIdentity = "submesh_batch_node_per_object_candidate",
                    },
                });
                rootList.Add(i);
            }

            sceneNodes = nodeList.ToArray();
            sceneMeshes = meshList.ToArray();
            sceneRootNodeIndices = rootList.ToArray();
        }
        else
        {
            sceneNodes = new object[] { new { name = $"{monsterId}_descriptor_static", mesh = 0 } };
            sceneMeshes = new object[]
            {
                new
                {
                    name = $"{monsterId}_descriptor_static",
                    primitives = primitives.ToArray(),
                },
            };
            sceneRootNodeIndices = new[] { 0 };
        }

        var gltf = new
        {
            asset = new
            {
                version = "2.0",
                generator = textureSet.HasTextures
                    ? "FFX Mod Studio Phyre descriptor exporter with local texture"
                    : "FFX Mod Studio Phyre descriptor exporter",
                copyright = "Read-only lab export from descriptor-decoded Phyre mesh streams. No game files were modified.",
            },
            extensionsUsed = Array.Empty<string>(),
            scene = 0,
            scenes = new[] { new { nodes = sceneRootNodeIndices, name = $"{monsterId} descriptor static mesh" } },
            nodes = sceneNodes,
            meshes = sceneMeshes,
            materials = materials.ToArray(),
            samplers = textureSet.HasTextures ? new[] { new { magFilter = 9729, minFilter = 9987, wrapS = 10497, wrapT = 10497 } } : null,
            images = textureSet.HasTextures ? textureSet.Images : null,
            textures = textureSet.HasTextures ? textureSet.Textures : null,
            buffers = new[]
            {
                new
                {
                    uri = "data:application/octet-stream;base64," + Convert.ToBase64String(buffer.ToArray()),
                    byteLength = buffer.Count,
                },
            },
            bufferViews = bufferViews.ToArray(),
            accessors = accessors.ToArray(),
            extras = new
            {
                monsterId,
                decisionBand = "mesh_descriptor_static_gltf_candidate",
                promotionStatus = "not_promoted_external_visual_validation_required",
                descriptorDecisionBand = descriptor.DecisionBand,
                meshSegmentCount = descriptor.MeshSegmentCount,
                totalVertexCount = descriptor.TotalVertexCount,
                totalIndexCount = descriptor.TotalIndexCount,
                positionBounds = descriptor.PositionBounds,
                texture = textureSet.HasTextures ? new
                {
                    count = textureSet.TextureCount,
                    binding = textureSet.Binding,
                    uvSource = "ST Float2 -> TEXCOORD_0",
                    textures = textureSet.ReportTextures,
                } : null,
                vertexColor = includeVertexColors ? new
                {
                    status = primitiveVertexColorCount > 0
                        ? "Color Float4 -> COLOR_0 candidate"
                        : "Color Float4 stream not found",
                    primitiveCount = primitiveVertexColorCount,
                } : null,
                note = textureSet.HasTextures
                    ? "Static descriptor export with decoded PNG texture candidates and ST UVs. Per-material texture slots, shader state, skin, and animation are still pending."
                    : "Static untextured descriptor export. Texture, material semantics, skin, and animation are still pending.",
            },
        };

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, JsonSerializer.Serialize(gltf, Program.JsonOptions) + Environment.NewLine, new UTF8Encoding(false));

        return new DescriptorStaticGltfExportReport(
            DateTimeOffset.UtcNow,
            monsterId,
            path,
            "mesh_descriptor_static_gltf_candidate",
            "not_promoted_external_visual_validation_required",
            descriptor.Submeshes.Length,
            totalVertices,
            totalIndices,
            totalIndices / 3,
            descriptor.PositionBounds,
            BuildNextStrike(textureSet.HasTextures, primitiveVertexColorCount),
            textureSet.PrimaryTexturePath,
            textureSet.HasTextures ? textureSet.Status : "texture_not_requested",
            textureSet.TextureCount,
            textureSet.ReportTextures.Select(static item => item.PngPath).ToArray());
    }

    private static string BuildNextStrike(bool hasTextures, int primitiveVertexColorCount)
    {
        if (hasTextures && primitiveVertexColorCount > 0)
        {
            return "descriptor-backed static GLTF emitted with decoded texture mip0 candidates, ST UVs, and Color Float4 -> COLOR_0 candidate; needs screenshot plus external validation before catalog promotion";
        }

        return hasTextures
            ? "descriptor-backed static GLTF emitted with decoded texture mip0 candidates and ST UVs; needs screenshot plus external validation before catalog promotion"
            : "descriptor-backed PMeshSegment/PDataBlock/PVertexStream static GLTF emitted; needs screenshot and external viewer validation before catalog promotion";
    }

    private static DescriptorGltfTextureSet BuildTextureSet(
        string outputPath,
        string assetId,
        DescriptorGltfTexture? texture,
        IReadOnlyList<DescriptorGltfMaterialTexture>? materialTextures)
    {
        var outputDirectory = Path.GetDirectoryName(outputPath) ?? ".";
        if (materialTextures is { Count: > 0 })
        {
            var ordered = materialTextures
                .GroupBy(static item => (item.SubmeshId, item.FileMaterialId))
                .Select(static group => group.First())
                .OrderBy(static item => item.SubmeshId ?? int.MaxValue)
                .ThenBy(static item => item.FileMaterialId)
                .ToArray();
            var images = new List<object>();
            var textures = new List<object>();
            var reportTextures = new List<DescriptorGltfTextureReport>();
            var bySubmesh = new Dictionary<int, int>();
            var byMaterial = new Dictionary<int, int>();
            var flipV = false;
            for (var index = 0; index < ordered.Length; index++)
            {
                var item = ordered[index];
                var imageUri = Path.GetRelativePath(outputDirectory, item.PngPath).Replace('\\', '/');
                var imageName = item.SubmeshId is null
                    ? $"{assetId}_material_{item.FileMaterialId}_mip0"
                    : $"{assetId}_submesh_{item.SubmeshId}_material_{item.FileMaterialId}_mip0";
                var textureName = item.SubmeshId is null
                    ? $"{assetId}_material_{item.FileMaterialId}_texture"
                    : $"{assetId}_submesh_{item.SubmeshId}_material_{item.FileMaterialId}_texture";
                images.Add(new { uri = imageUri, name = imageName });
                textures.Add(new { sampler = 0, source = index, name = textureName });
                reportTextures.Add(new DescriptorGltfTextureReport(
                    item.SubmeshId,
                    item.FileMaterialId,
                    item.PngPath,
                    item.Format,
                    item.Width,
                    item.Height,
                    item.SourcePath));
                if (item.SubmeshId is null)
                {
                    byMaterial[item.FileMaterialId] = index;
                }
                else
                {
                    bySubmesh[item.SubmeshId.Value] = index;
                }
                if (item.FlipV) flipV = true;
            }

            /* Emit normal map textures after base-color textures */
            var bySubmeshNormal = new Dictionary<int, int>();
            var byMaterialNormal = new Dictionary<int, int>();
            foreach (var item in ordered)
            {
                if (string.IsNullOrEmpty(item.NormalMapPngPath)) continue;
                var normalUri = Path.GetRelativePath(outputDirectory, item.NormalMapPngPath).Replace('\\', '/');
                var normalName = item.SubmeshId is null
                    ? $"{assetId}_material_{item.FileMaterialId}_normal_mip0"
                    : $"{assetId}_submesh_{item.SubmeshId}_material_{item.FileMaterialId}_normal_mip0";
                var normalTexName = item.SubmeshId is null
                    ? $"{assetId}_material_{item.FileMaterialId}_normal_texture"
                    : $"{assetId}_submesh_{item.SubmeshId}_material_{item.FileMaterialId}_normal_texture";
                var normalIdx = images.Count;
                images.Add(new { uri = normalUri, name = normalName });
                textures.Add(new { sampler = 0, source = normalIdx, name = normalTexName });
                if (item.SubmeshId is null)
                    byMaterialNormal[item.FileMaterialId] = normalIdx;
                else
                    bySubmeshNormal[item.SubmeshId.Value] = normalIdx;
            }

            return new DescriptorGltfTextureSet(
                true,
                ordered.Length,
                bySubmesh.Count > 0 ? "submesh_texture_candidate" : "material_id_sorted_texture_candidate",
                bySubmesh.Count > 0 ? "decoded_png_bound_by_submesh_candidate" : "decoded_png_bound_by_file_material_id_candidate",
                flipV,
                images.ToArray(),
                textures.ToArray(),
                reportTextures.ToArray(),
                bySubmesh,
                byMaterial,
                null,
                ordered[0].PngPath,
                bySubmeshNormal,
                byMaterialNormal);
        }

        if (texture is not null)
        {
            var imageUri = Path.GetRelativePath(outputDirectory, texture.PngPath).Replace('\\', '/');
            return new DescriptorGltfTextureSet(
                true,
                1,
                "coarse_primary_texture_all_submeshes",
                "official_texture_mip0_bound_to_st_uv_candidate",
                texture.FlipV,
                new object[] { new { uri = imageUri, name = $"{assetId}_official_texture_mip0" } },
                new object[] { new { sampler = 0, source = 0, name = $"{assetId}_official_texture" } },
                new[] { new DescriptorGltfTextureReport(null, null, texture.PngPath, texture.Format, texture.Width, texture.Height, null) },
                new Dictionary<int, int>(),
                new Dictionary<int, int>(),
                0,
                texture.PngPath);
        }

        return DescriptorGltfTextureSet.Empty;
    }

    private sealed record DescriptorGltfTextureSet(
        bool HasTextures,
        int TextureCount,
        string Binding,
        string Status,
        bool FlipV,
        object[] Images,
        object[] Textures,
        DescriptorGltfTextureReport[] ReportTextures,
        IReadOnlyDictionary<int, int> BySubmeshId,
        IReadOnlyDictionary<int, int> ByMaterialId,
        int? FallbackTextureIndex,
        string? PrimaryTexturePath,
        IReadOnlyDictionary<int, int>? BySubmeshNormal = null,
        IReadOnlyDictionary<int, int>? ByMaterialNormal = null)
    {
        public IReadOnlyDictionary<int, int> SubmeshNormals => BySubmeshNormal ?? EmptyDict;
        public IReadOnlyDictionary<int, int> MaterialNormals => ByMaterialNormal ?? EmptyDict;
        private static readonly Dictionary<int, int> EmptyDict = new();

        public int? ResolveNormalIndex(int submeshId, int fileMaterialId) =>
            SubmeshNormals.TryGetValue(submeshId, out var i) ? i
            : MaterialNormals.TryGetValue(fileMaterialId, out var j) ? j
            : null;

        public static DescriptorGltfTextureSet Empty { get; } = new(
            false,
            0,
            "none",
            "texture_not_requested",
            false,
            Array.Empty<object>(),
            Array.Empty<object>(),
            Array.Empty<DescriptorGltfTextureReport>(),
            new Dictionary<int, int>(),
            new Dictionary<int, int>(),
            null,
            null);

        public int? ResolveTextureIndex(int submeshId, int fileMaterialId) =>
            BySubmeshId.TryGetValue(submeshId, out var submeshIndex)
                ? submeshIndex
                : ByMaterialId.TryGetValue(fileMaterialId, out var materialIndex)
                    ? materialIndex
                    : FallbackTextureIndex;
    }

    private sealed record DescriptorGltfTextureReport(
        int? SubmeshId,
        int? FileMaterialId,
        string PngPath,
        string Format,
        int Width,
        int Height,
        string? SourcePath);

    private static byte[] CopyTightFloat3(byte[] source, long sourceOffset, int count, int stride)
    {
        var output = new byte[count * 12];
        for (var index = 0; index < count; index++)
        {
            Buffer.BlockCopy(source, checked((int)(sourceOffset + index * (long)stride)), output, index * 12, 12);
        }

        return output;
    }

    private static byte[] CopyTightFloat2(byte[] source, long sourceOffset, int count, int stride, bool flipV)
    {
        var output = new byte[count * 8];
        for (var index = 0; index < count; index++)
        {
            var inputOffset = checked((int)(sourceOffset + index * (long)stride));
            var outputOffset = index * 8;
            Buffer.BlockCopy(source, inputOffset, output, outputOffset, 8);
            if (flipV)
            {
                var v = BitConverter.ToSingle(output, outputOffset + 4);
                Buffer.BlockCopy(BitConverter.GetBytes(1.0f - v), 0, output, outputOffset + 4, 4);
            }
        }

        return output;
    }

    private static byte[] CopyTightFloat4(byte[] source, long sourceOffset, int count, int stride)
    {
        var output = new byte[count * 16];
        for (var index = 0; index < count; index++)
        {
            Buffer.BlockCopy(source, checked((int)(sourceOffset + index * (long)stride)), output, index * 16, 16);
        }

        return output;
    }

    private static byte[] CopyBytes(byte[] source, long sourceOffset, int count)
    {
        var output = new byte[count];
        Buffer.BlockCopy(source, checked((int)sourceOffset), output, 0, count);
        return output;
    }

    private static void Align(List<byte> buffer, int alignment)
    {
        while (buffer.Count % alignment != 0)
        {
            buffer.Add(0);
        }
    }
}

public sealed record DescriptorStaticGltfExportIndex(
    DateTimeOffset GeneratedAtUtc,
    string Ps3Root,
    int ExportCount,
    int CandidateCount,
    DescriptorStaticGltfExportReport[] Exports);

public sealed record DescriptorStaticGltfExportReport(
    DateTimeOffset GeneratedAtUtc,
    string MonsterId,
    string? AssetPath,
    string DecisionBand,
    string PromotionStatus,
    int PrimitiveCount,
    int VertexCount,
    int IndexCount,
    int TriangleCount,
    PositionBounds? PositionBounds,
    string NextStrike,
    string? TexturePath = null,
    string? TextureStatus = null,
    int TextureCount = 0,
    string[]? TexturePaths = null)
{
    public static DescriptorStaticGltfExportReport Blocked(string monsterId, string decisionBand, string nextStrike) => new(
        DateTimeOffset.UtcNow,
        monsterId,
        null,
        decisionBand,
        "not_promoted_descriptor_gate_failed",
        0,
        0,
        0,
        0,
        null,
        nextStrike,
        null,
        null,
        0,
        null);
}

public sealed record DescriptorGltfTexture(
    string PngPath,
    string Format,
    int Width,
    int Height,
    bool FlipV);

public sealed record DescriptorGltfMaterialTexture(
    int FileMaterialId,
    string PngPath,
    string Format,
    int Width,
    int Height,
    bool FlipV,
    string SourcePath,
    int? SubmeshId = null,
    string? NormalMapPngPath = null,
    int? NormalMapWidth = null,
    int? NormalMapHeight = null);
