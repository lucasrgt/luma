using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Luma.ModelLib.Animation;

namespace Luma.ModelLib.Model;

public sealed class AllumeriaBbModelExportOptions
{
    public string Name { get; init; } = "model";

    public int TextureWidth { get; init; } = 128;

    public int TextureHeight { get; init; } = 128;

    public float ObjToBlockbenchScale { get; init; } = 16f;

    public bool FlattenHierarchy { get; init; }

    public bool IncludeAnimations { get; init; } = true;

    public bool PreferNativeCubes { get; init; } = true;

    public bool PartialAnimationRig { get; init; }

    public int MaxBoneCount { get; init; } = 20;

    public int MinimumChunkCount { get; init; } = 1;

    public bool ReverseMeshWinding { get; init; } = true;

    public string[] DoubleSidedMeshNamePrefixes { get; init; } = [];
}

public sealed record AllumeriaBbModelChunk(string Name, string Json, int BoneCount, int PartCount);

public static class AllumeriaBbModelExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string Export(ObjMesh mesh, AnimationBundle animation, AllumeriaBbModelExportOptions options)
    {
        return BuildModelJson(mesh, animation, options, enforceBoneLimit: true).ToJsonString(JsonOptions);
    }

    public static IReadOnlyList<AllumeriaBbModelChunk> ExportChunks(
        ObjMesh mesh,
        AnimationBundle animation,
        AllumeriaBbModelExportOptions options)
    {
        if (options.MaxBoneCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Chunk export requires a positive MaxBoneCount.");
        }

        JsonObject model = BuildModelJson(mesh, animation, options, enforceBoneLimit: false);
        JsonArray outliner = model["outliner"]?.AsArray()
            ?? throw new InvalidOperationException("Generated model has no outliner.");
        JsonArray elements = model["elements"]?.AsArray()
            ?? throw new InvalidOperationException("Generated model has no elements.");

        Dictionary<string, Vector3> partCenters = BuildPartCenters(elements);
        List<PartPlacement> placements = CollectPartPlacements(outliner, partCenters);
        if (placements.Count == 0)
        {
            string emptyChunkName = $"{options.Name}_chunk_00";
            JsonObject emptyChunk = (JsonObject)model.DeepClone();
            emptyChunk["name"] = emptyChunkName;
            return
            [
                new AllumeriaBbModelChunk(
                    emptyChunkName,
                    emptyChunk.ToJsonString(JsonOptions),
                    CountOutlinerBones(emptyChunk["outliner"]!.AsArray()),
                    elements.Count)
            ];
        }

        List<ChunkBuilder> builders = PackPlacements(placements, options.MaxBoneCount, options.MinimumChunkCount);
        var chunks = new List<AllumeriaBbModelChunk>(builders.Count);
        for (int i = 0; i < builders.Count; i++)
        {
            string chunkName = $"{options.Name}_chunk_{i:00}";
            JsonObject chunkModel = BuildChunkModel(model, elements, builders[i], chunkName);
            JsonArray chunkOutliner = chunkModel["outliner"]!.AsArray();
            JsonArray chunkElements = chunkModel["elements"]!.AsArray();
            chunks.Add(new AllumeriaBbModelChunk(
                chunkName,
                chunkModel.ToJsonString(JsonOptions),
                CountOutlinerBones(chunkOutliner),
                chunkElements.Count));
        }

        return chunks;
    }

    private static JsonObject BuildModelJson(
        ObjMesh mesh,
        AnimationBundle animation,
        AllumeriaBbModelExportOptions options,
        bool enforceBoneLimit)
    {
        Dictionary<string, string> partIds = PartGroups(mesh).ToDictionary(
            group => group,
            group => StableId("part", group),
            StringComparer.Ordinal);

        HashSet<string> boneNames = options.FlattenHierarchy
            ? []
            : options.PartialAnimationRig
                ? BuildPartialBoneNames(mesh, animation)
                : BuildBoneNames(mesh, animation);
        Dictionary<string, string> boneIds = boneNames.ToDictionary(
            bone => bone,
            bone => StableId("bone", bone),
            StringComparer.Ordinal);

        JsonArray elements = BuildElements(mesh, partIds, options);
        JsonArray outliner = options.FlattenHierarchy
            ? BuildFlatOutliner(mesh, partIds)
            : options.PartialAnimationRig
                ? BuildPartialOutliner(mesh, animation, partIds, boneIds, boneNames)
                : BuildOutliner(mesh, animation, partIds, boneIds, boneNames);
        int boneCount = CountOutlinerBones(outliner);
        if (enforceBoneLimit && options.MaxBoneCount > 0 && boneCount > options.MaxBoneCount)
        {
            string mode = options.FlattenHierarchy
                ? "flat"
                : options.PartialAnimationRig ? "partial-rig" : "animated";
            throw new InvalidOperationException(
                $"Allumeria entity shader supports at most {options.MaxBoneCount} bones; exported {boneCount} bones in {mode} hierarchy. " +
                "Use --partial-rig/--flat or reduce animated roots.");
        }

        return new JsonObject
        {
            ["name"] = options.Name,
            ["resolution"] = new JsonObject
            {
                ["width"] = options.TextureWidth,
                ["height"] = options.TextureHeight
            },
            ["elements"] = elements,
            ["outliner"] = outliner,
            ["animations"] = options.IncludeAnimations
                ? BuildAnimations(animation, boneIds)
                : new JsonArray()
        };
    }

    private static HashSet<string> BuildBoneNames(ObjMesh mesh, AnimationBundle animation)
    {
        var meshGroups = PartGroups(mesh).ToHashSet(StringComparer.Ordinal);
        var boneNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (string name in animation.Pivots.Keys)
        {
            boneNames.Add(name);
        }

        foreach ((string child, string parent) in animation.ChildMap)
        {
            boneNames.Add(parent);
            if (!meshGroups.Contains(child) || animation.Pivots.ContainsKey(child))
            {
                boneNames.Add(child);
            }
        }

        foreach (AnimationClip clip in animation.Clips.Values)
        {
            foreach (string name in clip.Bones.Keys)
            {
                boneNames.Add(name);
            }
        }

        foreach (string group in meshGroups)
        {
            if (!animation.ChildMap.ContainsKey(group))
            {
                boneNames.Add(group);
            }
        }

        return boneNames;
    }

    private static IEnumerable<string> PartGroups(ObjMesh mesh)
    {
        HashSet<string> groupsWithPolygons = mesh.Polygons
            .Select(polygon => polygon.GroupName)
            .ToHashSet(StringComparer.Ordinal);

        return mesh.Groups.Where(groupsWithPolygons.Contains);
    }

    private static HashSet<string> BuildPartialBoneNames(ObjMesh mesh, AnimationBundle animation)
    {
        return BuildTopAnimatedBoneNames(animation);
    }

    private static JsonArray BuildElements(
        ObjMesh mesh,
        IReadOnlyDictionary<string, string> partIds,
        AllumeriaBbModelExportOptions options)
    {
        var elements = new JsonArray();
        foreach (string group in mesh.Groups)
        {
            List<ObjPolygonFace> polygons = mesh.Polygons.Where(face => face.GroupName == group).ToList();
            if (polygons.Count == 0)
            {
                continue;
            }

            if (options.PreferNativeCubes &&
                TryBuildNativeCubeElement(mesh, group, polygons, partIds[group], options, out JsonObject? cubeElement))
            {
                elements.Add(cubeElement);
                continue;
            }

            if (options.PreferNativeCubes &&
                TryBuildRotatedXCubeElement(mesh, group, polygons, partIds[group], options, out JsonObject? rotatedCubeElement))
            {
                elements.Add(rotatedCubeElement);
                continue;
            }

            elements.Add(BuildMeshElement(mesh, group, polygons, partIds[group], options));
        }

        return elements;
    }

    private static JsonObject BuildMeshElement(
        ObjMesh mesh,
        string group,
        IReadOnlyList<ObjPolygonFace> polygons,
        string partId,
        AllumeriaBbModelExportOptions options)
    {
        var vertexIds = new Dictionary<int, string>();
        var vertices = new JsonObject();
        var faceObjects = new JsonObject();
        bool doubleSided = IsDoubleSidedMesh(group, options);

        foreach (ObjPolygonFace polygon in polygons)
        {
            foreach (ObjVertex[] face in BuildAllumeriaFaces(polygon))
            {
                ObjVertex[] outputFace = options.ReverseMeshWinding
                    ? [face[3], face[2], face[1], face[0]]
                    : face;

                AddFace(outputFace);

                if (doubleSided)
                {
                    AddFace([outputFace[3], outputFace[2], outputFace[1], outputFace[0]]);
                }
            }
        }

        return new JsonObject
        {
            ["name"] = group,
            ["color"] = 0,
            ["origin"] = Vec3(Vector3.Zero),
            ["rotation"] = Vec3(Vector3.Zero),
            ["render_order"] = "default",
            ["allow_mirror_modeling"] = true,
            ["vertices"] = vertices,
            ["faces"] = faceObjects,
            ["type"] = "mesh",
            ["uuid"] = partId
        };

        void AddFace(ObjVertex[] outputFace)
        {
            foreach (ObjVertex vertex in outputFace)
            {
                AddVertex(vertex.PositionIndex);
            }

            var uv = new JsonObject();
            var verticesForFace = new JsonArray();
            foreach (ObjVertex vertex in outputFace)
            {
                string vertexId = vertexIds[vertex.PositionIndex];
                uv[vertexId] = TextureCoordinate(mesh, vertex, options);
                verticesForFace.Add(vertexId);
            }

            string faceId = StableId("face", $"{group}:{faceObjects.Count}");
            faceObjects[faceId] = new JsonObject
            {
                ["uv"] = uv,
                ["vertices"] = verticesForFace,
                ["texture"] = 0
            };
        }

        void AddVertex(int positionIndex)
        {
            if (vertexIds.ContainsKey(positionIndex))
            {
                return;
            }

            string vertexId = $"v{vertexIds.Count}";
            vertexIds[positionIndex] = vertexId;
            Vector3 position = mesh.Positions[positionIndex] * options.ObjToBlockbenchScale;
            vertices[vertexId] = Vec3(position);
        }
    }

    private static bool IsDoubleSidedMesh(string group, AllumeriaBbModelExportOptions options)
    {
        return options.DoubleSidedMeshNamePrefixes.Any(prefix =>
            group.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static bool TryBuildNativeCubeElement(
        ObjMesh mesh,
        string group,
        IReadOnlyList<ObjPolygonFace> polygons,
        string partId,
        AllumeriaBbModelExportOptions options,
        out JsonObject? element)
    {
        element = null;
        if (polygons.Count != 6 || polygons.Any(polygon => polygon.Vertices.Count != 4))
        {
            return false;
        }

        Vector3[] positions = polygons
            .SelectMany(polygon => polygon.Vertices)
            .Select(vertex => mesh.Positions[vertex.PositionIndex] * options.ObjToBlockbenchScale)
            .Distinct()
            .ToArray();

        if (positions.Length != 8)
        {
            return false;
        }

        float[] xs = DistinctAxis(positions.Select(position => position.X));
        float[] ys = DistinctAxis(positions.Select(position => position.Y));
        float[] zs = DistinctAxis(positions.Select(position => position.Z));
        if (xs.Length != 2 || ys.Length != 2 || zs.Length != 2)
        {
            return false;
        }

        float minX = xs[0];
        float maxX = xs[1];
        float minY = ys[0];
        float maxY = ys[1];
        float minZ = zs[0];
        float maxZ = zs[1];

        foreach (Vector3 position in positions)
        {
            if (!AxisMatches(position.X, minX, maxX) ||
                !AxisMatches(position.Y, minY, maxY) ||
                !AxisMatches(position.Z, minZ, maxZ))
            {
                return false;
            }
        }

        var faces = new JsonObject();
        foreach (ObjPolygonFace polygon in polygons)
        {
            string? faceName = DetectCubeFaceName(mesh, polygon, options, minX, maxX, minY, maxY, minZ, maxZ);
            if (faceName is null)
            {
                return false;
            }

            (float minU, float minV, float maxU, float maxV) = TextureBounds(mesh, polygon, options);
            faces[faceName] = new JsonObject
            {
                ["uv"] = new JsonArray(Round(minU), Round(minV), Round(maxU), Round(maxV)),
                ["texture"] = 0
            };
        }

        element = new JsonObject
        {
            ["name"] = group,
            ["box_uv"] = false,
            ["rescale"] = false,
            ["light_emission"] = 0,
            ["render_order"] = "default",
            ["allow_mirror_modeling"] = true,
            ["from"] = Vec3(new Vector3(minX, minY, minZ)),
            ["to"] = Vec3(new Vector3(maxX, maxY, maxZ)),
            ["color"] = 0,
            ["origin"] = Vec3(Vector3.Zero),
            ["faces"] = faces,
            ["type"] = "cube",
            ["uuid"] = partId
        };
        return true;
    }

    private static bool TryBuildRotatedXCubeElement(
        ObjMesh mesh,
        string group,
        IReadOnlyList<ObjPolygonFace> polygons,
        string partId,
        AllumeriaBbModelExportOptions options,
        out JsonObject? element)
    {
        element = null;
        if (polygons.Count != 6 || polygons.Any(polygon => polygon.Vertices.Count != 4))
        {
            return false;
        }

        Vector3[] positions = DistinctVectors(polygons
            .SelectMany(polygon => polygon.Vertices)
            .Select(vertex => mesh.Positions[vertex.PositionIndex] * options.ObjToBlockbenchScale))
            .ToArray();

        if (positions.Length != 8)
        {
            return false;
        }

        float[] xs = DistinctAxis(positions.Select(position => position.X));
        if (xs.Length != 2)
        {
            return false;
        }

        Vector2[] yzCorners = DistinctVector2(positions.Select(position => new Vector2(position.Y, position.Z))).ToArray();
        if (yzCorners.Length != 4)
        {
            return false;
        }

        if (!TryGetYzFrame(yzCorners, out Vector2 center, out Vector2 axisY, out Vector2 axisZ))
        {
            return false;
        }

        Vector3[] localPositions = positions
            .Select(position => ToRotatedXLocal(position, center, axisY, axisZ))
            .ToArray();

        float minX = xs[0];
        float maxX = xs[1];
        float minY = localPositions.Min(position => position.Y);
        float maxY = localPositions.Max(position => position.Y);
        float minZ = localPositions.Min(position => position.Z);
        float maxZ = localPositions.Max(position => position.Z);

        foreach (Vector3 position in localPositions)
        {
            if (!AxisMatches(position.X, minX, maxX) ||
                !AxisMatches(position.Y, minY, maxY) ||
                !AxisMatches(position.Z, minZ, maxZ))
            {
                return false;
            }
        }

        var faces = new JsonObject();
        foreach (ObjPolygonFace polygon in polygons)
        {
            Vector3[] localFacePositions = polygon.Vertices
                .Select(vertex => mesh.Positions[vertex.PositionIndex] * options.ObjToBlockbenchScale)
                .Select(position => ToRotatedXLocal(position, center, axisY, axisZ))
                .ToArray();
            string? faceName = DetectCubeFaceName(localFacePositions, minX, maxX, minY, maxY, minZ, maxZ);
            if (faceName is null)
            {
                return false;
            }

            (float minU, float minV, float maxU, float maxV) = TextureBounds(mesh, polygon, options);
            faces[faceName] = new JsonObject
            {
                ["uv"] = new JsonArray(Round(minU), Round(minV), Round(maxU), Round(maxV)),
                ["texture"] = 0
            };
        }

        float rotationX = MathF.Atan2(axisY.Y, axisY.X) * 180f / MathF.PI;
        element = new JsonObject
        {
            ["name"] = group,
            ["box_uv"] = false,
            ["rescale"] = false,
            ["light_emission"] = 0,
            ["render_order"] = "default",
            ["allow_mirror_modeling"] = true,
            ["from"] = Vec3(new Vector3(minX, minY, minZ)),
            ["to"] = Vec3(new Vector3(maxX, maxY, maxZ)),
            ["rotation"] = Vec3(new Vector3(rotationX, 0f, 0f)),
            ["color"] = 0,
            ["origin"] = Vec3(new Vector3((minX + maxX) * 0.5f, center.X, center.Y)),
            ["faces"] = faces,
            ["type"] = "cube",
            ["uuid"] = partId
        };
        return true;
    }

    private static string? DetectCubeFaceName(
        ObjMesh mesh,
        ObjPolygonFace polygon,
        AllumeriaBbModelExportOptions options,
        float minX,
        float maxX,
        float minY,
        float maxY,
        float minZ,
        float maxZ)
    {
        Vector3[] positions = polygon.Vertices
            .Select(vertex => mesh.Positions[vertex.PositionIndex] * options.ObjToBlockbenchScale)
            .ToArray();

        return DetectCubeFaceName(positions, minX, maxX, minY, maxY, minZ, maxZ);
    }

    private static string? DetectCubeFaceName(
        IReadOnlyList<Vector3> positions,
        float minX,
        float maxX,
        float minY,
        float maxY,
        float minZ,
        float maxZ)
    {
        if (positions.All(position => NearlyEqual(position.Z, minZ)))
        {
            return "north";
        }

        if (positions.All(position => NearlyEqual(position.Z, maxZ)))
        {
            return "south";
        }

        if (positions.All(position => NearlyEqual(position.X, maxX)))
        {
            return "east";
        }

        if (positions.All(position => NearlyEqual(position.X, minX)))
        {
            return "west";
        }

        if (positions.All(position => NearlyEqual(position.Y, maxY)))
        {
            return "up";
        }

        if (positions.All(position => NearlyEqual(position.Y, minY)))
        {
            return "down";
        }

        return null;
    }

    private static bool TryGetYzFrame(
        IReadOnlyList<Vector2> corners,
        out Vector2 center,
        out Vector2 axisY,
        out Vector2 axisZ)
    {
        center = new Vector2(corners.Average(corner => corner.X), corners.Average(corner => corner.Y));
        axisY = Vector2.Zero;
        axisZ = Vector2.Zero;

        Vector2 p0 = corners[0];
        Vector2[] edges = corners
            .Skip(1)
            .Select(corner => corner - p0)
            .OrderBy(edge => edge.LengthSquared())
            .Take(2)
            .ToArray();

        if (edges.Length != 2 || edges[0].LengthSquared() < 0.000001f || edges[1].LengthSquared() < 0.000001f)
        {
            return false;
        }

        Vector2 first = Vector2.Normalize(edges[0]);
        Vector2 second = Vector2.Normalize(edges[1]);
        if (MathF.Abs(Vector2.Dot(first, second)) > 0.001f)
        {
            return false;
        }

        axisY = MathF.Abs(first.X) >= MathF.Abs(second.X) ? first : second;
        if (axisY.X < 0f)
        {
            axisY = -axisY;
        }

        axisZ = new Vector2(-axisY.Y, axisY.X);
        return true;
    }

    private static Vector3 ToRotatedXLocal(Vector3 position, Vector2 center, Vector2 axisY, Vector2 axisZ)
    {
        Vector2 offset = new(position.Y - center.X, position.Z - center.Y);
        return new Vector3(
            position.X,
            center.X + Vector2.Dot(offset, axisY),
            center.Y + Vector2.Dot(offset, axisZ));
    }

    private static IEnumerable<Vector3> DistinctVectors(IEnumerable<Vector3> values)
    {
        var result = new List<Vector3>();
        foreach (Vector3 value in values)
        {
            if (result.All(existing => Vector3.DistanceSquared(existing, value) > 0.000001f))
            {
                result.Add(value);
            }
        }

        return result;
    }

    private static IEnumerable<Vector2> DistinctVector2(IEnumerable<Vector2> values)
    {
        var result = new List<Vector2>();
        foreach (Vector2 value in values)
        {
            if (result.All(existing => Vector2.DistanceSquared(existing, value) > 0.000001f))
            {
                result.Add(value);
            }
        }

        return result;
    }

    private static (float MinU, float MinV, float MaxU, float MaxV) TextureBounds(
        ObjMesh mesh,
        ObjPolygonFace polygon,
        AllumeriaBbModelExportOptions options)
    {
        Vector2[] uvs = polygon.Vertices
            .Select(vertex => TextureCoordinateVector(mesh, vertex, options))
            .ToArray();

        float minU = uvs.Min(uv => uv.X);
        float minV = uvs.Min(uv => uv.Y);
        float maxU = uvs.Max(uv => uv.X);
        float maxV = uvs.Max(uv => uv.Y);
        return (minU, minV, maxU, maxV);
    }

    private static float[] DistinctAxis(IEnumerable<float> values)
    {
        var result = new List<float>();
        foreach (float value in values.Order())
        {
            if (result.Count == 0 || !NearlyEqual(result[^1], value))
            {
                result.Add(value);
            }
        }

        return result.ToArray();
    }

    private static bool AxisMatches(float value, float min, float max)
    {
        return NearlyEqual(value, min) || NearlyEqual(value, max);
    }

    private static bool NearlyEqual(float left, float right)
    {
        return MathF.Abs(left - right) <= 0.0001f;
    }

    private static Dictionary<string, Vector3> BuildPartCenters(JsonArray elements)
    {
        var centers = new Dictionary<string, Vector3>(StringComparer.Ordinal);
        foreach (JsonNode? node in elements)
        {
            if (node is not JsonObject element ||
                element["uuid"]?.GetValue<string>() is not { Length: > 0 } partId)
            {
                continue;
            }

            if (TryGetElementCenter(element, out Vector3 center))
            {
                centers[partId] = center;
            }
        }

        return centers;
    }

    private static bool TryGetElementCenter(JsonObject element, out Vector3 center)
    {
        center = Vector3.Zero;
        string? type = element["type"]?.GetValue<string>();
        if (type == "cube" &&
            TryReadVector3(element["from"], out Vector3 from) &&
            TryReadVector3(element["to"], out Vector3 to))
        {
            center = (from + to) * 0.5f;
            return true;
        }

        if (type != "mesh" || element["vertices"] is not JsonObject vertices)
        {
            return false;
        }

        bool hasBounds = false;
        Vector3 min = Vector3.Zero;
        Vector3 max = Vector3.Zero;
        foreach ((_, JsonNode? vertexNode) in vertices)
        {
            if (!TryReadVector3(vertexNode, out Vector3 vertex))
            {
                continue;
            }

            if (!hasBounds)
            {
                min = vertex;
                max = vertex;
                hasBounds = true;
                continue;
            }

            min = Vector3.Min(min, vertex);
            max = Vector3.Max(max, vertex);
        }

        if (!hasBounds)
        {
            return false;
        }

        center = (min + max) * 0.5f;
        return true;
    }

    private static bool TryReadVector3(JsonNode? node, out Vector3 vector)
    {
        vector = Vector3.Zero;
        if (node is not JsonArray array || array.Count < 3)
        {
            return false;
        }

        if (!TryReadFloat(array[0], out float x) ||
            !TryReadFloat(array[1], out float y) ||
            !TryReadFloat(array[2], out float z))
        {
            return false;
        }

        vector = new Vector3(x, y, z);
        return true;
    }

    private static bool TryReadFloat(JsonNode? node, out float value)
    {
        value = 0f;
        if (node is null)
        {
            return false;
        }

        if (node is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue(out float floatValue))
            {
                value = floatValue;
                return true;
            }

            if (jsonValue.TryGetValue(out double doubleValue))
            {
                value = (float)doubleValue;
                return true;
            }

            if (jsonValue.TryGetValue(out int intValue))
            {
                value = intValue;
                return true;
            }
        }

        return false;
    }

    private static List<PartPlacement> CollectPartPlacements(
        JsonArray outliner,
        IReadOnlyDictionary<string, Vector3> partCenters)
    {
        var placements = new List<PartPlacement>();
        foreach (JsonNode? node in outliner)
        {
            Visit(node, []);
        }

        return placements;

        void Visit(JsonNode? node, List<BoneTemplate> bonePath)
        {
            if (node is null)
            {
                return;
            }

            if (node is JsonValue)
            {
                string partId = node.GetValue<string>();
                Vector3 center = partCenters.TryGetValue(partId, out Vector3 partCenter)
                    ? partCenter
                    : Vector3.Zero;
                placements.Add(new PartPlacement(partId, bonePath.ToArray(), center));
                return;
            }

            JsonObject bone = node.AsObject();
            var nextBonePath = new List<BoneTemplate>(bonePath)
            {
                new(RequiredString(bone, "uuid"), bone)
            };

            if (bone["children"] is not JsonArray children)
            {
                return;
            }

            foreach (JsonNode? child in children)
            {
                Visit(child, nextBonePath);
            }
        }
    }

    private static List<ChunkBuilder> PackPlacements(
        IReadOnlyList<PartPlacement> placements,
        int maxBoneCount,
        int minimumChunkCount)
    {
        minimumChunkCount = Math.Max(1, minimumChunkCount);
        if (minimumChunkCount <= 1)
        {
            return PackBoneLimitedPlacements(placements, maxBoneCount);
        }

        List<ChunkBuilder> spatialChunks = PackSpatialPlacements(placements, minimumChunkCount);
        var result = new List<ChunkBuilder>();
        foreach (ChunkBuilder spatialChunk in spatialChunks)
        {
            result.AddRange(PackBoneLimitedPlacements(spatialChunk.Placements, maxBoneCount));
        }

        return result;
    }

    private static List<ChunkBuilder> PackBoneLimitedPlacements(
        IReadOnlyList<PartPlacement> placements,
        int maxBoneCount)
    {
        var chunks = new List<ChunkBuilder>();
        foreach (PartPlacement placement in placements)
        {
            string[] requiredBones = placement.BonePath
                .Select(bone => bone.Uuid)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (requiredBones.Length > maxBoneCount)
            {
                throw new InvalidOperationException(
                    $"Part {placement.PartId} requires {requiredBones.Length} ancestor bones, " +
                    $"but Allumeria supports at most {maxBoneCount} bones per model chunk.");
            }

            ChunkBuilder? bestChunk = null;
            int bestAddedBones = int.MaxValue;
            foreach (ChunkBuilder chunk in chunks)
            {
                int addedBones = requiredBones.Count(bone => !chunk.BoneIds.Contains(bone));
                if (chunk.BoneIds.Count + addedBones > maxBoneCount)
                {
                    continue;
                }

                if (addedBones < bestAddedBones)
                {
                    bestChunk = chunk;
                    bestAddedBones = addedBones;
                }
            }

            if (bestChunk is null)
            {
                bestChunk = new ChunkBuilder();
                chunks.Add(bestChunk);
            }

            bestChunk.Add(placement);
        }

        return chunks;
    }

    private static List<ChunkBuilder> PackSpatialPlacements(
        IReadOnlyList<PartPlacement> placements,
        int minimumChunkCount)
    {
        if (placements.Count == 0)
        {
            return [];
        }

        int xBuckets = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(minimumChunkCount)));
        int zBuckets = Math.Max(1, (int)Math.Ceiling(minimumChunkCount / (double)xBuckets));
        int bucketCount = xBuckets * zBuckets;
        var buckets = Enumerable.Range(0, bucketCount)
            .Select(_ => new ChunkBuilder())
            .ToArray();

        float minX = placements.Min(placement => placement.Center.X);
        float maxX = placements.Max(placement => placement.Center.X);
        float minZ = placements.Min(placement => placement.Center.Z);
        float maxZ = placements.Max(placement => placement.Center.Z);

        foreach (PartPlacement placement in placements)
        {
            int x = BucketIndex(placement.Center.X, minX, maxX, xBuckets);
            int z = BucketIndex(placement.Center.Z, minZ, maxZ, zBuckets);
            buckets[(z * xBuckets) + x].Add(placement);
        }

        return buckets
            .Where(bucket => bucket.Placements.Count > 0)
            .ToList();
    }

    private static int BucketIndex(float value, float min, float max, int bucketCount)
    {
        if (bucketCount <= 1 || max - min <= 0.0001f)
        {
            return 0;
        }

        float normalised = (value - min) / (max - min);
        int bucket = (int)MathF.Floor(normalised * bucketCount);
        return Math.Clamp(bucket, 0, bucketCount - 1);
    }

    private static JsonObject BuildChunkModel(
        JsonObject sourceModel,
        JsonArray sourceElements,
        ChunkBuilder chunk,
        string chunkName)
    {
        var elements = new JsonArray();
        foreach (JsonNode? node in sourceElements)
        {
            if (node is not JsonObject element)
            {
                continue;
            }

            string partId = RequiredString(element, "uuid");
            if (chunk.PartIds.Contains(partId))
            {
                elements.Add(element.DeepClone());
            }
        }

        return new JsonObject
        {
            ["name"] = chunkName,
            ["resolution"] = sourceModel["resolution"]?.DeepClone(),
            ["elements"] = elements,
            ["outliner"] = BuildChunkOutliner(chunk),
            ["animations"] = BuildChunkAnimations(
                sourceModel["animations"] as JsonArray,
                chunk.BoneIds)
        };
    }

    private static JsonArray BuildChunkOutliner(ChunkBuilder chunk)
    {
        var outliner = new JsonArray();
        foreach (PartPlacement placement in chunk.Placements)
        {
            JsonArray children = outliner;
            foreach (BoneTemplate bone in placement.BonePath)
            {
                JsonObject boneNode = FindOrAddBoneChild(children, bone);
                if (boneNode["children"] is not JsonArray nextChildren)
                {
                    nextChildren = new JsonArray();
                    boneNode["children"] = nextChildren;
                }

                children = nextChildren;
            }

            if (!ContainsPartChild(children, placement.PartId))
            {
                children.Add(placement.PartId);
            }
        }

        return outliner;
    }

    private static JsonObject FindOrAddBoneChild(JsonArray children, BoneTemplate bone)
    {
        foreach (JsonNode? child in children)
        {
            if (child is JsonObject childObject &&
                childObject["uuid"]?.GetValue<string>() == bone.Uuid)
            {
                return childObject;
            }
        }

        JsonObject added = CloneBoneShell(bone.Node);
        children.Add(added);
        return added;
    }

    private static bool ContainsPartChild(JsonArray children, string partId)
    {
        foreach (JsonNode? child in children)
        {
            if (child is JsonValue && child.GetValue<string>() == partId)
            {
                return true;
            }
        }

        return false;
    }

    private static JsonArray BuildChunkAnimations(JsonArray? sourceAnimations, IReadOnlySet<string> boneIds)
    {
        var animations = new JsonArray();
        if (sourceAnimations is null)
        {
            return animations;
        }

        foreach (JsonNode? node in sourceAnimations)
        {
            if (node is not JsonObject animation)
            {
                continue;
            }

            JsonObject chunkAnimation = (JsonObject)animation.DeepClone();
            var animators = new JsonObject();
            if (animation["animators"] is JsonObject sourceAnimators)
            {
                foreach ((string boneId, JsonNode? animator) in sourceAnimators)
                {
                    if (boneIds.Contains(boneId))
                    {
                        animators[boneId] = animator?.DeepClone();
                    }
                }
            }

            chunkAnimation["animators"] = animators;
            animations.Add(chunkAnimation);
        }

        return animations;
    }

    private static JsonObject CloneBoneShell(JsonObject bone)
    {
        var clone = new JsonObject();
        foreach ((string propertyName, JsonNode? propertyValue) in bone)
        {
            if (propertyName == "children")
            {
                continue;
            }

            clone[propertyName] = propertyValue?.DeepClone();
        }

        clone["children"] = new JsonArray();
        return clone;
    }

    private static string RequiredString(JsonObject node, string propertyName)
    {
        return node[propertyName]?.GetValue<string>()
            ?? throw new InvalidOperationException($"Expected string property '{propertyName}'.");
    }

    private sealed record BoneTemplate(string Uuid, JsonObject Node);

    private sealed record PartPlacement(string PartId, IReadOnlyList<BoneTemplate> BonePath, Vector3 Center);

    private sealed class ChunkBuilder
    {
        public List<PartPlacement> Placements { get; } = [];

        public HashSet<string> BoneIds { get; } = new(StringComparer.Ordinal);

        public HashSet<string> PartIds { get; } = new(StringComparer.Ordinal);

        public void Add(PartPlacement placement)
        {
            Placements.Add(placement);
            PartIds.Add(placement.PartId);
            foreach (BoneTemplate bone in placement.BonePath)
            {
                BoneIds.Add(bone.Uuid);
            }
        }
    }

    private static JsonArray BuildFlatOutliner(
        ObjMesh mesh,
        IReadOnlyDictionary<string, string> partIds)
    {
        var children = new JsonArray();
        foreach (string group in mesh.Groups)
        {
            if (partIds.TryGetValue(group, out string? partId))
            {
                children.Add(partId);
            }
        }

        return new JsonArray(new JsonObject
        {
            ["name"] = "root",
            ["origin"] = Vec3(Vector3.Zero),
            ["uuid"] = StableId("bone", "root"),
            ["mirror_uv"] = false,
            ["children"] = children
        });
    }

    private static IEnumerable<ObjVertex[]> BuildAllumeriaFaces(ObjPolygonFace polygon)
    {
        IReadOnlyList<ObjVertex> vertices = polygon.Vertices;
        if (vertices.Count < 3)
        {
            yield break;
        }

        if (vertices.Count == 3)
        {
            yield return [vertices[0], vertices[1], vertices[2], vertices[2]];
            yield break;
        }

        if (vertices.Count == 4)
        {
            yield return [vertices[0], vertices[1], vertices[2], vertices[3]];
            yield break;
        }

        for (int i = 1; i < vertices.Count - 1; i++)
        {
            yield return [vertices[0], vertices[i], vertices[i + 1], vertices[i + 1]];
        }
    }

    private static JsonArray BuildPartialOutliner(
        ObjMesh mesh,
        AnimationBundle animation,
        IReadOnlyDictionary<string, string> partIds,
        IReadOnlyDictionary<string, string> boneIds,
        IReadOnlySet<string> boneNames)
    {
        Dictionary<string, List<string>> childrenByParent = BuildChildrenByParent(animation);
        HashSet<string> animatedRoots = [.. boneNames];
        HashSet<string> movingNames = BuildMovingNames(animatedRoots, childrenByParent);

        var movingParts = new HashSet<string>(StringComparer.Ordinal);
        foreach (string animatedRoot in animatedRoots)
        {
            foreach (string partName in CollectDescendantPartNames(animatedRoot, childrenByParent, partIds))
            {
                movingParts.Add(partName);
            }
        }

        var rootChildren = new JsonArray();
        foreach (string group in mesh.Groups)
        {
            if (!movingParts.Contains(group) && partIds.TryGetValue(group, out string? partId))
            {
                rootChildren.Add(partId);
            }
        }

        foreach (string animatedRoot in animatedRoots.Order(StringComparer.Ordinal))
        {
            if (boneNames.Contains(animatedRoot) &&
                (!animation.ChildMap.TryGetValue(animatedRoot, out string? parent) || !movingNames.Contains(parent)))
            {
                rootChildren.Add(BuildFlatAnimatedBoneNode(
                    animatedRoot,
                    animation,
                    partIds,
                    boneIds,
                    childrenByParent));
            }
        }

        return new JsonArray(new JsonObject
        {
            ["name"] = "root",
            ["origin"] = Vec3(Vector3.Zero),
            ["uuid"] = StableId("bone", "partial-root"),
            ["mirror_uv"] = false,
            ["children"] = rootChildren
        });
    }

    private static JsonObject BuildFlatAnimatedBoneNode(
        string bone,
        AnimationBundle animation,
        IReadOnlyDictionary<string, string> partIds,
        IReadOnlyDictionary<string, string> boneIds,
        IReadOnlyDictionary<string, List<string>> childrenByParent)
    {
        var children = new JsonArray();
        foreach (string partName in CollectDescendantPartNames(bone, childrenByParent, partIds))
        {
            children.Add(partIds[partName]);
        }

        return new JsonObject
        {
            ["name"] = bone,
            ["origin"] = Vec3(animation.Pivots.TryGetValue(bone, out Vector3 pivot) ? pivot : Vector3.Zero),
            ["uuid"] = boneIds[bone],
            ["mirror_uv"] = false,
            ["children"] = children
        };
    }

    private static List<string> CollectDescendantPartNames(
        string root,
        IReadOnlyDictionary<string, List<string>> childrenByParent,
        IReadOnlyDictionary<string, string> partIds)
    {
        var result = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        Visit(root);
        return result;

        void Visit(string name)
        {
            if (!visited.Add(name))
            {
                return;
            }

            if (partIds.ContainsKey(name))
            {
                result.Add(name);
            }

            if (!childrenByParent.TryGetValue(name, out List<string>? children))
            {
                return;
            }

            foreach (string child in children)
            {
                Visit(child);
            }
        }
    }

    private static Dictionary<string, List<string>> BuildChildrenByParent(AnimationBundle animation)
    {
        var childrenByParent = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach ((string child, string parent) in animation.ChildMap)
        {
            if (!childrenByParent.TryGetValue(parent, out List<string>? children))
            {
                children = [];
                childrenByParent[parent] = children;
            }

            children.Add(child);
        }

        return childrenByParent;
    }

    private static HashSet<string> BuildMovingNames(
        IEnumerable<string> roots,
        IReadOnlyDictionary<string, List<string>> childrenByParent)
    {
        var movingNames = new HashSet<string>(roots, StringComparer.Ordinal);
        var stack = new Stack<string>(movingNames);

        while (stack.Count > 0)
        {
            string parent = stack.Pop();
            if (!childrenByParent.TryGetValue(parent, out List<string>? children))
            {
                continue;
            }

            foreach (string child in children)
            {
                if (movingNames.Add(child))
                {
                    stack.Push(child);
                }
            }
        }

        return movingNames;
    }

    private static HashSet<string> BuildTopAnimatedBoneNames(AnimationBundle animation)
    {
        HashSet<string> animatedBones = BuildAnimatedBoneNames(animation);
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (string boneName in animatedBones)
        {
            if (!HasAnimatedAncestor(boneName, animation.ChildMap, animatedBones))
            {
                result.Add(boneName);
            }
        }

        return result;
    }

    private static HashSet<string> BuildAnimatedRootBoneNames(AnimationBundle animation)
    {
        HashSet<string> animatedBones = BuildAnimatedBoneNames(animation);
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (string boneName in animatedBones)
        {
            if (!animation.ChildMap.TryGetValue(boneName, out string? parent) ||
                !animatedBones.Contains(parent))
            {
                result.Add(boneName);
            }
        }

        return result;
    }

    private static HashSet<string> BuildAnimatedBoneNames(AnimationBundle animation)
    {
        var animatedRoots = new HashSet<string>(StringComparer.Ordinal);
        foreach (AnimationClip clip in animation.Clips.Values)
        {
            foreach ((string boneName, BoneChannels channels) in clip.Bones)
            {
                if (channels.Rotation.Count > 0 || channels.Position.Count > 0 || channels.Scale.Count > 0)
                {
                    animatedRoots.Add(boneName);
                }
            }
        }

        return animatedRoots;
    }

    private static bool HasAnimatedAncestor(
        string boneName,
        IReadOnlyDictionary<string, string> childMap,
        IReadOnlySet<string> animatedBones)
    {
        string current = boneName;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (childMap.TryGetValue(current, out string? parent) && visited.Add(current))
        {
            if (animatedBones.Contains(parent))
            {
                return true;
            }

            current = parent;
        }

        return false;
    }

    private static JsonArray BuildOutliner(
        ObjMesh mesh,
        AnimationBundle animation,
        IReadOnlyDictionary<string, string> partIds,
        IReadOnlyDictionary<string, string> boneIds,
        IReadOnlySet<string> boneNames)
    {
        var childrenByBone = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (string bone in boneNames)
        {
            childrenByBone[bone] = [];
        }

        var parentedBones = new HashSet<string>(StringComparer.Ordinal);
        var parentedParts = new HashSet<string>(StringComparer.Ordinal);

        foreach ((string child, string parent) in animation.ChildMap)
        {
            if (!childrenByBone.ContainsKey(parent))
            {
                childrenByBone[parent] = [];
            }

            childrenByBone[parent].Add(child);
            if (boneNames.Contains(child))
            {
                parentedBones.Add(child);
            }

            if (partIds.ContainsKey(child))
            {
                parentedParts.Add(child);
            }
        }

        foreach (string group in mesh.Groups)
        {
            if (partIds.ContainsKey(group) && boneNames.Contains(group))
            {
                childrenByBone[group].Insert(0, group);
                parentedParts.Add(group);
            }
        }

        var roots = new JsonArray();
        foreach (string bone in boneNames.Order(StringComparer.Ordinal))
        {
            if (!parentedBones.Contains(bone))
            {
                roots.Add(BuildBoneNode(bone, animation, partIds, boneIds, boneNames, childrenByBone, []));
            }
        }

        foreach (string group in mesh.Groups)
        {
            if (!parentedParts.Contains(group) && partIds.TryGetValue(group, out string? partId))
            {
                roots.Add(partId);
            }
        }

        return roots;
    }

    private static JsonObject BuildBoneNode(
        string bone,
        AnimationBundle animation,
        IReadOnlyDictionary<string, string> partIds,
        IReadOnlyDictionary<string, string> boneIds,
        IReadOnlySet<string> boneNames,
        IReadOnlyDictionary<string, List<string>> childrenByBone,
        HashSet<string> path)
    {
        var children = new JsonArray();
        if (path.Add(bone) && childrenByBone.TryGetValue(bone, out List<string>? childNames))
        {
            foreach (string child in childNames.Distinct(StringComparer.Ordinal))
            {
                if (child == bone && partIds.TryGetValue(child, out string? selfPartId))
                {
                    children.Add(selfPartId);
                }
                else if (boneNames.Contains(child))
                {
                    children.Add(BuildBoneNode(child, animation, partIds, boneIds, boneNames, childrenByBone, path));
                }
                else if (partIds.TryGetValue(child, out string? partId))
                {
                    children.Add(partId);
                }
            }

            path.Remove(bone);
        }

        return new JsonObject
        {
            ["name"] = bone,
            ["origin"] = Vec3(animation.Pivots.TryGetValue(bone, out Vector3 pivot) ? pivot : Vector3.Zero),
            ["uuid"] = boneIds[bone],
            ["mirror_uv"] = false,
            ["children"] = children
        };
    }

    private static JsonArray BuildAnimations(AnimationBundle animation, IReadOnlyDictionary<string, string> boneIds)
    {
        var animations = new JsonArray();
        var index = 0;

        foreach (AnimationClip clip in animation.Clips.Values)
        {
            var animators = new JsonObject();
            foreach ((string boneName, BoneChannels channels) in clip.Bones)
            {
                if (!boneIds.TryGetValue(boneName, out string? boneId))
                {
                    continue;
                }

                var keyframes = new JsonArray();
                AddKeyframes(keyframes, "rotation", channels.Rotation);
                AddKeyframes(keyframes, "position", channels.Position);
                AddKeyframes(keyframes, "scale", channels.Scale);

                animators[boneId] = new JsonObject
                {
                    ["name"] = boneName,
                    ["type"] = "bone",
                    ["keyframes"] = keyframes
                };
            }

            animations.Add(new JsonObject
            {
                ["uuid"] = StableId("animation", $"{clip.Name}:{index++}"),
                ["name"] = clip.Name,
                ["loop"] = clip.Loop ? "loop" : "once",
                ["override"] = false,
                ["length"] = clip.LengthSeconds,
                ["snapping"] = 24,
                ["anim_time_update"] = string.Empty,
                ["blend_weight"] = string.Empty,
                ["start_delay"] = string.Empty,
                ["loop_delay"] = string.Empty,
                ["animators"] = animators
            });
        }

        return animations;
    }

    private static void AddKeyframes(JsonArray target, string channel, IReadOnlyList<VectorKeyframe> keyframes)
    {
        foreach (VectorKeyframe keyframe in keyframes)
        {
            target.Add(new JsonObject
            {
                ["channel"] = channel,
                ["data_points"] = new JsonArray(new JsonObject
                {
                    ["x"] = Format(keyframe.Value.X),
                    ["y"] = Format(keyframe.Value.Y),
                    ["z"] = Format(keyframe.Value.Z)
                }),
                ["uuid"] = StableId("keyframe", $"{channel}:{keyframe.TimeSeconds}:{keyframe.Value}"),
                ["time"] = keyframe.TimeSeconds
            });
        }
    }

    private static JsonArray TextureCoordinate(ObjMesh mesh, ObjVertex vertex, AllumeriaBbModelExportOptions options)
    {
        Vector2 uv = TextureCoordinateVector(mesh, vertex, options);
        return new JsonArray(Round(uv.X), Round(uv.Y));
    }

    private static Vector2 TextureCoordinateVector(ObjMesh mesh, ObjVertex vertex, AllumeriaBbModelExportOptions options)
    {
        if (vertex.TexCoordIndex < 0)
        {
            return Vector2.Zero;
        }

        Vector2 uv = mesh.TexCoords[vertex.TexCoordIndex];
        return new Vector2(
            uv.X * options.TextureWidth,
            (1f - uv.Y) * options.TextureHeight);
    }

    private static JsonArray Vec3(Vector3 value)
    {
        return new JsonArray(Round(value.X), Round(value.Y), Round(value.Z));
    }

    private static double Round(float value)
    {
        return Math.Round(value, 6);
    }

    private static string Format(float value)
    {
        return value.ToString("0.######", CultureInfo.InvariantCulture);
    }

    private static string StableId(string kind, string value)
    {
        byte[] bytes = MD5.HashData(Encoding.UTF8.GetBytes($"{kind}:{value}"));
        return new Guid(bytes).ToString();
    }

    private static int CountOutlinerBones(JsonArray outliner)
    {
        int count = 0;
        foreach (JsonNode? node in outliner)
        {
            count += CountOutlinerBones(node);
        }

        return count;
    }

    private static int CountOutlinerBones(JsonNode? node)
    {
        if (node is not JsonObject bone)
        {
            return 0;
        }

        int count = 1;
        if (bone["children"] is JsonArray children)
        {
            foreach (JsonNode? child in children)
            {
                count += CountOutlinerBones(child);
            }
        }

        return count;
    }
}
