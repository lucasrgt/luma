using System.Text.Json;
using Allumeria.DataManagement.ModelParsing;
using Allumeria.EntitySystem.Models;
using Allumeria.Rendering;
using Luma.Abstractions.Models;
using OpenTK.Mathematics;
using System.Globalization;

namespace Luma.AllumeriaLoader;

internal sealed class AllumeriaAnimatedModel : ILumaAnimatedModel
{
    private const int MaxShaderLightSamples = 16;
    private const float LightSampleSideOutset = 0.35f;
    private const float LightSampleVerticalOutset = 0.05f;

    private readonly AllumeriaAnimatedModelOptions options;
    private readonly List<JsonDocument> modelDocuments = [];
    private readonly List<BBModel> bbModels = [];
    private readonly List<EntityModel> entityModels = [];
    private readonly List<ModelBounds> modelBounds = [];
    private readonly List<Vector3> lightSampleOffsets = [];
    private Texture? texture;
    private bool loadAttempted;
    private int debugUpdateLogCount;
    private int debugLightLogCount;
    private string? currentAnimationName;
    private bool loopAnimation;
    private float animationStepSeconds;

    public AllumeriaAnimatedModel(AllumeriaAnimatedModelOptions options)
    {
        this.options = options;
        currentAnimationName = options.AnimationName;
        loopAnimation = options.LoopAnimation;
        animationStepSeconds = options.AnimationStepSeconds;
    }

    public string Name => options.Name;

    public void Update()
    {
        foreach (EntityModel entityModel in entityModels)
        {
            entityModel.UpdateBones(Matrix4.Identity);
            LogDebugAnimationState(entityModel);
        }
    }

    public void RenderBlock(Vector3 position)
    {
        Render(position, 0f);
    }

    public void RenderBlock(LumaVector3 position)
    {
        RenderBlock(ToVector3(position));
    }

    public void Render(LumaModelRenderRequest request)
    {
        Vector3 position = ToVector3(request.Position);
        Render(position, request.Yaw);
    }

    public void Render(Vector3 position, float yaw, Vector4 light)
    {
        if (!EnsureLoaded())
        {
            return;
        }

        foreach (EntityModel entityModel in entityModels)
        {
            RenderModel(entityModel, position, yaw, light);
        }
    }

    private void Render(Vector3 position, float yaw)
    {
        if (!EnsureLoaded())
        {
            return;
        }

        for (int i = 0; i < entityModels.Count; i++)
        {
            Vector3 lightPosition = position + GetLightSampleOffset(i);
            Vector4 light = SampleLight(lightPosition);
            ModelLightSampleSet lightSamples = BuildModelLightSamples(i, position, yaw);
            RenderModel(entityModels[i], position, yaw, light, lightSamples);
        }
    }

    private static void RenderModel(
        EntityModel entityModel,
        Vector3 position,
        float yaw,
        Vector4 light,
        ModelLightSampleSet? lightSamples = null)
    {
        entityModel.UpdateBonesLerped(Matrix4.Identity);

        Shader? lumaShader = LumaModelShader.Get();
        if (lumaShader is null)
        {
            entityModel.Render(position, yaw, light);
            return;
        }

        LumaModelShader.ApplyFrameUniforms(lumaShader);
        lumaShader.SetUniformVec4("light", light);
        ApplyModelLightSamples(lumaShader, lightSamples);
        entityModel.model.mesh.Draw(
            lumaShader,
            entityModel.texture,
            position,
            yaw,
            is3D: true,
            entityModel.emissionTexture,
            entityModel.boneMatrices);
    }

    public bool SetAnimation(string animationName, bool loop = true, float stepSeconds = 1f / 60f)
    {
        currentAnimationName = animationName;
        loopAnimation = loop;
        animationStepSeconds = stepSeconds;

        if (entityModels.Count == 0)
        {
            return true;
        }

        return ApplyAnimationToLoadedModels();
    }

    public void PauseAnimation()
    {
        foreach (EntityModel entityModel in entityModels)
        {
            entityModel.animator.Pause();
        }
    }

    public void RestartAnimation(float stepSeconds = 1f / 60f)
    {
        animationStepSeconds = stepSeconds;
        foreach (EntityModel entityModel in entityModels)
        {
            entityModel.animator.Restart(animationStepSeconds);
        }
    }

    private bool EnsureLoaded()
    {
        if (entityModels.Count > 0)
        {
            return true;
        }

        if (loadAttempted)
        {
            return false;
        }

        loadAttempted = true;

        try
        {
            AllumeriaModelAssetSet assets = ResolveModelAssets();
            if (!File.Exists(assets.TexturePath) || assets.ModelPaths.Any(path => !File.Exists(path)))
            {
                Logger.Info($"{options.Name} assets missing: {string.Join(", ", assets.ModelPaths)}, {assets.TexturePath}");
                return false;
            }

            texture = new Texture(
                assets.TexturePath,
                flip: options.TextureFlip,
                clamp: options.TextureClamp,
                mipmaps: options.TextureMipmaps,
                keepImage: options.TextureKeepImage,
                nearest: options.TextureNearest,
                data: null!,
                fixedSize: options.TextureFixedSize);

            foreach (string modelPath in assets.ModelPaths)
            {
                JsonDocument modelDocument = JsonDocument.Parse(File.ReadAllText(modelPath));
                var bbModel = new BBModel(modelDocument.RootElement);
                bbModel.mesh = bbModel.ConvertToMesh();

                var entityModel = new EntityModel(bbModel, texture);
                modelDocuments.Add(modelDocument);
                bbModels.Add(bbModel);
                entityModels.Add(entityModel);
                modelBounds.Add(CalculateModelBounds(bbModel));
            }

            RebuildLightSampleOffsets();

            bool animationStarted = ApplyAnimationToLoadedModels();
            if (currentAnimationName is not null && !animationStarted)
            {
                Logger.Info($"{options.Name} model has no '{currentAnimationName}' animation; rendering static model");
            }

            int partCount = bbModels.Sum(model => model.parts.Count);
            int animationCount = bbModels.Sum(model => model.animations.Count);
            string mode = assets.ModelPaths.Count == 1 ? "single" : "chunked";
            Logger.Info($"{options.Name} model loaded: mode={mode}, chunks={assets.ModelPaths.Count}, parts={partCount}, animations={animationCount}");
            Logger.Info($"{options.Name} light sample offsets: {string.Join(", ", lightSampleOffsets.Select(FormatVector))}");
            return true;
        }
        catch (Exception ex)
        {
            ClearLoadedState();
            Logger.Error($"{options.Name} model failed to load", ex);
            return false;
        }
    }

    private bool ApplyAnimationToLoadedModels()
    {
        if (currentAnimationName is null)
        {
            return false;
        }

        bool animationStarted = false;
        for (int i = 0; i < bbModels.Count; i++)
        {
            BBModel bbModel = bbModels[i];
            EntityModel entityModel = entityModels[i];
            if (!bbModel.animationDictionary.ContainsKey(currentAnimationName))
            {
                continue;
            }

            entityModel.animator.SetAnimation(currentAnimationName);
            entityModel.animator.loop = loopAnimation;
            entityModel.animator.Play(animationStepSeconds);
            animationStarted = true;
        }

        return animationStarted;
    }

    private void LogDebugAnimationState(EntityModel entityModel)
    {
        if (options.DebugBoneName is null || debugUpdateLogCount >= options.DebugUpdateLogFrames)
        {
            return;
        }

        debugUpdateLogCount++;
        EntityModelBone? bone = entityModel.FindBone(options.DebugBoneName);
        Logger.Info(
            $"{options.Name} animation debug {debugUpdateLogCount}/{options.DebugUpdateLogFrames}: " +
            $"animation={entityModel.animator.currentAnimation?.name ?? "<none>"}, " +
            $"time={entityModel.animator.time:0.###}, " +
            $"speed={entityModel.animator.speed:0.###}, " +
            $"ended={entityModel.animator.ended}, " +
            $"bone={options.DebugBoneName}, " +
            $"rotation={bone?.rotation.ToString() ?? "<missing>"}");
    }

    private Vector4 SampleLight(Vector3 samplePosition, bool includeDebugLog = true)
    {
        if (options.LightSampler is not null)
        {
            return options.LightSampler(samplePosition);
        }

        bool includeDebugSamples = includeDebugLog && debugLightLogCount < options.DebugLightSampleFrames;
        AllumeriaLightSampleResult result = AllumeriaLightSampler.SampleLargeBlockDetailed(
            samplePosition,
            options.LightSampleSettings,
            includeDebugSamples);

        if (includeDebugSamples)
        {
            debugLightLogCount++;
            Logger.Info(
                $"{options.Name} light debug {debugLightLogCount}/{options.DebugLightSampleFrames}: " +
                $"samplePosition={FormatVector(samplePosition)}, " +
                $"raw={AllumeriaLightSampler.FormatLight(result.RawLight)}, " +
                $"balanced={AllumeriaLightSampler.FormatLight(result.Light)}, " +
                $"samples=[{result.DebugSamples}]");
        }

        return result.Light;
    }

    private ModelLightSampleSet BuildModelLightSamples(int modelIndex, Vector3 position, float yaw)
    {
        if (modelIndex < 0 || modelIndex >= modelBounds.Count)
        {
            return ModelLightSampleSet.Empty;
        }

        Vector3[] localSamples = BuildLocalLightSamplePoints(modelBounds[modelIndex]);
        int sampleCount = Math.Min(localSamples.Length, MaxShaderLightSamples);
        if (sampleCount <= 0)
        {
            return ModelLightSampleSet.Empty;
        }

        var positions = new Vector3[sampleCount];
        var values = new Vector4[sampleCount];
        Matrix4 transform = Matrix4.Mult(Matrix4.CreateRotationY(yaw), Matrix4.CreateTranslation(position));

        for (int i = 0; i < sampleCount; i++)
        {
            Vector3 worldPosition = Vector3.TransformPosition(localSamples[i], transform);
            positions[i] = worldPosition;
            values[i] = SampleLight(worldPosition, includeDebugLog: false);
        }

        return new ModelLightSampleSet(positions, values);
    }

    private static Vector3[] BuildLocalLightSamplePoints(ModelBounds bounds)
    {
        Vector3 min = bounds.Min;
        Vector3 max = bounds.Max;
        Vector3 center = bounds.Center;

        if (Vector3.DistanceSquared(min, max) <= 0.0001f)
        {
            return [center];
        }

        float minX = min.X - LightSampleSideOutset;
        float maxX = max.X + LightSampleSideOutset;
        float minY = min.Y - LightSampleVerticalOutset;
        float maxY = max.Y + LightSampleVerticalOutset;
        float minZ = min.Z - LightSampleSideOutset;
        float maxZ = max.Z + LightSampleSideOutset;
        float centerX = center.X;
        float centerY = center.Y;
        float centerZ = center.Z;

        return
        [
            new(minX, minY, minZ),
            new(minX, minY, maxZ),
            new(maxX, minY, minZ),
            new(maxX, minY, maxZ),
            new(minX, maxY, minZ),
            new(minX, maxY, maxZ),
            new(maxX, maxY, minZ),
            new(maxX, maxY, maxZ),
            new(centerX, centerY, centerZ),
            new(centerX, maxY, centerZ),
            new(centerX, minY, centerZ),
            new(minX, centerY, centerZ),
            new(maxX, centerY, centerZ),
            new(centerX, centerY, minZ),
            new(centerX, centerY, maxZ)
        ];
    }

    private static void ApplyModelLightSamples(Shader shader, ModelLightSampleSet? lightSamples)
    {
        if (lightSamples is null || lightSamples.Count == 0)
        {
            shader.SetUniform1i("modelLightSampleCount", 0);
            return;
        }

        int count = Math.Min(lightSamples.Count, MaxShaderLightSamples);
        shader.SetUniform1i("modelLightSampleCount", count);
        shader.SetUniformVec3Array("modelLightPositions[0]", lightSamples.Positions);
        shader.SetUniformVec4Array("modelLightValues[0]", lightSamples.Values);
    }

    private Vector3 GetLightSampleOffset(int modelIndex)
    {
        return modelIndex >= 0 && modelIndex < lightSampleOffsets.Count
            ? lightSampleOffsets[modelIndex]
            : Vector3.Zero;
    }

    private AllumeriaModelAssetSet ResolveModelAssets()
    {
        string texturePath = AssetPath(options.TextureFileName);
        if (options.ChunkManifestFileName is null)
        {
            return new AllumeriaModelAssetSet(texturePath, [AssetPath(options.ModelFileName)]);
        }

        string manifestPath = AssetPath(options.ChunkManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return new AllumeriaModelAssetSet(texturePath, [AssetPath(options.ModelFileName)]);
        }

        using JsonDocument manifestDocument = JsonDocument.Parse(File.ReadAllText(manifestPath));
        JsonElement root = manifestDocument.RootElement;
        string manifestDirectory = Path.GetDirectoryName(manifestPath)
            ?? options.AssetDirectory;

        string textureFileName = root.TryGetProperty("texture", out JsonElement textureElement)
            ? textureElement.GetString() ?? options.TextureFileName
            : options.TextureFileName;

        if (!root.TryGetProperty("chunks", out JsonElement chunksElement) ||
            chunksElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"{options.Name} chunk manifest has no chunks array: {manifestPath}");
        }

        var modelPaths = new List<string>();
        foreach (JsonElement chunkElement in chunksElement.EnumerateArray())
        {
            string chunkFileName = chunkElement.GetString()
                ?? throw new InvalidDataException($"{options.Name} chunk manifest contains a non-string chunk: {manifestPath}");
            modelPaths.Add(Path.GetFullPath(Path.Combine(manifestDirectory, chunkFileName)));
        }

        string manifestTexturePath = Path.GetFullPath(Path.Combine(manifestDirectory, textureFileName));
        return new AllumeriaModelAssetSet(manifestTexturePath, modelPaths);
    }

    private string AssetPath(string fileName)
    {
        return Path.GetFullPath(Path.Combine(options.AssetDirectory, fileName));
    }

    private static Vector3 ToVector3(LumaVector3 value)
    {
        return new Vector3(value.X, value.Y, value.Z);
    }

    private void RebuildLightSampleOffsets()
    {
        lightSampleOffsets.Clear();
        if (modelBounds.Count == 0)
        {
            return;
        }

        if (modelBounds.Count == 1)
        {
            lightSampleOffsets.Add(CenterSampleOffset(modelBounds[0]));
            return;
        }

        ModelBounds globalBounds = modelBounds[0];
        foreach (ModelBounds bounds in modelBounds.Skip(1))
        {
            globalBounds = globalBounds.Include(bounds);
        }

        int xBuckets = Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt(modelBounds.Count)));
        int zBuckets = Math.Max(1, (int)Math.Ceiling(modelBounds.Count / (double)xBuckets));
        foreach (ModelBounds bounds in modelBounds)
        {
            Vector3 center = bounds.Center;
            int xBucket = BucketIndex(center.X, globalBounds.Min.X, globalBounds.Max.X, xBuckets);
            int zBucket = BucketIndex(center.Z, globalBounds.Min.Z, globalBounds.Max.Z, zBuckets);
            lightSampleOffsets.Add(RegionSampleOffset(globalBounds, center.Y, xBucket, zBucket, xBuckets, zBuckets));
        }
    }

    private static Vector3 CenterSampleOffset(ModelBounds bounds)
    {
        Vector3 center = bounds.Center;
        return new Vector3(center.X, MathF.Max(0.2f, center.Y), center.Z);
    }

    private static Vector3 RegionSampleOffset(
        ModelBounds globalBounds,
        float localCenterY,
        int xBucket,
        int zBucket,
        int xBuckets,
        int zBuckets)
    {
        bool middleX = IsMiddleBucket(xBucket, xBuckets);
        bool middleZ = IsMiddleBucket(zBucket, zBuckets);
        float y = middleX && middleZ
            ? MathF.Ceiling(globalBounds.Max.Y) + 0.05f
            : MathF.Max(0.2f, localCenterY);

        return new Vector3(
            SurfaceSampleCoordinate(globalBounds.Min.X, globalBounds.Max.X, xBucket, xBuckets),
            y,
            SurfaceSampleCoordinate(globalBounds.Min.Z, globalBounds.Max.Z, zBucket, zBuckets));
    }

    private static float SurfaceSampleCoordinate(float min, float max, int bucket, int bucketCount)
    {
        if (bucketCount <= 1)
        {
            return (min + max) * 0.5f;
        }

        if (bucket <= 0)
        {
            return min - 0.35f;
        }

        if (bucket >= bucketCount - 1)
        {
            return MathF.Ceiling(max) + 0.05f;
        }

        return Lerp(min, max, RegionFraction(bucket, bucketCount));
    }

    private static bool IsMiddleBucket(int bucket, int bucketCount)
    {
        return bucketCount > 2 && bucket > 0 && bucket < bucketCount - 1;
    }

    private static float RegionFraction(int bucket, int bucketCount)
    {
        if (bucketCount <= 1)
        {
            return 0.5f;
        }

        const float edgeInset = 0.16f;
        float step = (1f - (edgeInset * 2f)) / (bucketCount - 1);
        return edgeInset + (step * bucket);
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

    private static float Lerp(float a, float b, float amount)
    {
        return a + ((b - a) * amount);
    }

    private static ModelBounds CalculateModelBounds(BBModel bbModel)
    {
        bool hasBounds = false;
        Vector3 min = Vector3.Zero;
        Vector3 max = Vector3.Zero;

        foreach (BBModelPart part in bbModel.parts)
        {
            foreach (Vector3 point in EnumeratePartPoints(part))
            {
                if (!hasBounds)
                {
                    min = point;
                    max = point;
                    hasBounds = true;
                    continue;
                }

                min = Min(min, point);
                max = Max(max, point);
            }
        }

        if (!hasBounds)
        {
            return new ModelBounds(Vector3.Zero, Vector3.Zero);
        }

        return new ModelBounds(min, max);
    }

    private static IEnumerable<Vector3> EnumeratePartPoints(BBModelPart part)
    {
        if (part.typeID == 0)
        {
            Vector3 from = part.cube.from / 16f;
            Vector3 to = part.cube.to / 16f;
            Matrix4 transform =
                Matrix4.CreateTranslation(-part.origin) *
                Matrix4.CreateRotationX(MathHelper.DegreesToRadians(part.rotation.X)) *
                Matrix4.CreateRotationY(MathHelper.DegreesToRadians(part.rotation.Y)) *
                Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(part.rotation.Z)) *
                Matrix4.CreateTranslation(part.origin);

            foreach (Vector3 point in EnumerateBoxCorners(from, to))
            {
                yield return Vector3.TransformPosition(point, transform);
            }

            yield break;
        }

        if (part.typeID != 1)
        {
            yield break;
        }

        Matrix4 meshTransform =
            Matrix4.CreateTranslation(-part.origin) *
            Matrix4.CreateRotationY(MathHelper.DegreesToRadians(part.rotation.Y)) *
            Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(part.rotation.Z)) *
            Matrix4.CreateRotationX(MathHelper.DegreesToRadians(part.rotation.X)) *
            Matrix4.CreateTranslation(part.origin);

        foreach (BBModelMeshFace face in part.mesh.faces)
        {
            foreach (Vector3 point in face.positions)
            {
                yield return Vector3.TransformPosition(point, meshTransform);
            }
        }
    }

    private static IEnumerable<Vector3> EnumerateBoxCorners(Vector3 from, Vector3 to)
    {
        yield return new Vector3(from.X, from.Y, from.Z);
        yield return new Vector3(from.X, from.Y, to.Z);
        yield return new Vector3(from.X, to.Y, from.Z);
        yield return new Vector3(from.X, to.Y, to.Z);
        yield return new Vector3(to.X, from.Y, from.Z);
        yield return new Vector3(to.X, from.Y, to.Z);
        yield return new Vector3(to.X, to.Y, from.Z);
        yield return new Vector3(to.X, to.Y, to.Z);
    }

    private static Vector3 Min(Vector3 a, Vector3 b)
    {
        return new Vector3(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y), MathF.Min(a.Z, b.Z));
    }

    private static Vector3 Max(Vector3 a, Vector3 b)
    {
        return new Vector3(MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y), MathF.Max(a.Z, b.Z));
    }

    private static string FormatVector(Vector3 value)
    {
        return string.Create(CultureInfo.InvariantCulture, $"({value.X:0.##}, {value.Y:0.##}, {value.Z:0.##})");
    }

    private void ClearLoadedState()
    {
        modelDocuments.Clear();
        bbModels.Clear();
        entityModels.Clear();
        modelBounds.Clear();
        lightSampleOffsets.Clear();
        texture = null;
    }
}

internal sealed class AllumeriaAnimatedModelOptions
{
    public required string Name { get; init; }
    public required string AssetDirectory { get; init; }
    public required string TextureFileName { get; init; }
    public required string ModelFileName { get; init; }
    public string? ChunkManifestFileName { get; init; }
    public string? AnimationName { get; init; }
    public bool LoopAnimation { get; init; } = true;
    public float AnimationStepSeconds { get; init; } = 1f / 60f;
    public Func<Vector3, Vector4>? LightSampler { get; init; }
    public AllumeriaLightSampleSettings LightSampleSettings { get; init; } = LoadLightSampleSettings();
    public int DebugLightSampleFrames { get; init; } = LoadDebugLightSampleFrames();
    public bool TextureFlip { get; init; }
    public bool TextureClamp { get; init; }
    public bool TextureMipmaps { get; init; } = true;
    public bool TextureKeepImage { get; init; }
    public bool TextureNearest { get; init; } = true;
    public int TextureFixedSize { get; init; }
    public string? DebugBoneName { get; init; }
    public int DebugUpdateLogFrames { get; init; }

    public static AllumeriaAnimatedModelOptions FromSpec(LumaAnimatedModelSpec spec)
    {
        return new AllumeriaAnimatedModelOptions
        {
            Name = spec.Name,
            AssetDirectory = spec.AssetRoot,
            TextureFileName = spec.TexturePath,
            ModelFileName = spec.ModelPath,
            ChunkManifestFileName = spec.ChunkManifestPath,
            AnimationName = spec.InitialAnimation,
            LoopAnimation = spec.LoopInitialAnimation,
            AnimationStepSeconds = spec.AnimationStepSeconds
        };
    }

    private static AllumeriaLightSampleSettings LoadLightSampleSettings()
    {
        AllumeriaLightSampleSettings defaults = AllumeriaLightSampleSettings.Default;
        bool balanceTint = !IsEnvFalse("LUMA_LIGHT_BALANCE");
        float tintStrength = TryReadFloat("LUMA_LIGHT_TINT_STRENGTH") ?? defaults.TintStrength;
        return new AllumeriaLightSampleSettings(balanceTint, Math.Clamp(tintStrength, 0f, 1f));
    }

    private static int LoadDebugLightSampleFrames()
    {
        int? explicitFrames = TryReadInt("LUMA_LIGHT_DEBUG_FRAMES");
        if (explicitFrames is not null)
        {
            return Math.Max(0, explicitFrames.Value);
        }

        return IsEnvTrue("LUMA_LIGHT_DEBUG") ? 24 : 0;
    }

    private static float? TryReadFloat(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
            ? parsed
            : null;
    }

    private static int? TryReadInt(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : null;
    }

    private static bool IsEnvTrue(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return value is not null &&
            (value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
             value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
             value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
             value.Equals("on", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsEnvFalse(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return value is not null &&
            (value.Equals("0", StringComparison.OrdinalIgnoreCase) ||
             value.Equals("false", StringComparison.OrdinalIgnoreCase) ||
             value.Equals("no", StringComparison.OrdinalIgnoreCase) ||
             value.Equals("off", StringComparison.OrdinalIgnoreCase));
    }
}

internal sealed record AllumeriaModelAssetSet(string TexturePath, IReadOnlyList<string> ModelPaths);

internal sealed record ModelLightSampleSet(Vector3[] Positions, Vector4[] Values)
{
    public static ModelLightSampleSet Empty { get; } = new([], []);

    public int Count => Math.Min(Positions.Length, Values.Length);
}

internal readonly record struct ModelBounds(Vector3 Min, Vector3 Max)
{
    public Vector3 Center => (Min + Max) * 0.5f;

    public ModelBounds Include(ModelBounds other)
    {
        return new ModelBounds(
            new Vector3(
                MathF.Min(Min.X, other.Min.X),
                MathF.Min(Min.Y, other.Min.Y),
                MathF.Min(Min.Z, other.Min.Z)),
            new Vector3(
                MathF.Max(Max.X, other.Max.X),
                MathF.Max(Max.Y, other.Max.Y),
                MathF.Max(Max.Z, other.Max.Z)));
    }
}
