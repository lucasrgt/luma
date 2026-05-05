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
    private static readonly object SharedAssetGate = new();
    private static readonly Dictionary<string, AllumeriaAnimatedModelSharedAssets> SharedAssets = [];

    private readonly AllumeriaAnimatedModelOptions options;
    private readonly List<BBModel> bbModels = [];
    private readonly List<EntityModel> entityModels = [];
    private readonly List<ModelBounds> modelBounds = [];
    private readonly List<Vector3> lightSampleOffsets = [];
    private readonly Dictionary<string, LumaBoneOverrideSpec> boneOverrides = new(StringComparer.Ordinal);
    private bool loadAttempted;
    private int debugUpdateLogCount;
    private int debugLightLogCount;
    private string? currentStateName;
    private string? currentAnimationName;
    private bool loopAnimation;
    private bool animationAutoPlay = true;
    private float animationStepSeconds;
    private IReadOnlyList<EntityModelBonePose[]>? transitionStartPoses;
    private float transitionSeconds;
    private float transitionElapsedSeconds;
    private readonly List<LumaAnimationEvent> pendingAnimationEvents = [];
    private float lastAnimationEventTime;
    private bool hasAnimationEventCursor;

    public AllumeriaAnimatedModel(AllumeriaAnimatedModelOptions options)
    {
        this.options = options;
        Animation = new AllumeriaAnimationController(this);
        ApplyInitialAnimation();
    }

    public string Name => options.Name;

    public ILumaAnimationController Animation { get; }

    public void Update()
    {
        for (int i = 0; i < entityModels.Count; i++)
        {
            EntityModel entityModel = entityModels[i];
            entityModel.UpdateBones(Matrix4.Identity);
            bool poseChanged = ApplyTransitionBlend(i, entityModel);
            poseChanged |= ApplyBoneOverrides(entityModel);
            if (poseChanged)
            {
                UpdateBoneMatrices(entityModel);
            }

            LogDebugAnimationState(entityModel);
        }

        EmitAnimationEvents();
        AdvanceTransition();
        ApplyAutomaticCompletionTransition();
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

        for (int i = 0; i < entityModels.Count; i++)
        {
            RenderModel(i, entityModels[i], position, yaw, light);
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
            RenderModel(i, entityModels[i], position, yaw, light, lightSamples);
        }
    }

    private void RenderModel(
        int modelIndex,
        EntityModel entityModel,
        Vector3 position,
        float yaw,
        Vector4 light,
        ModelLightSampleSet? lightSamples = null)
    {
        entityModel.UpdateBonesLerped(Matrix4.Identity);
        bool poseChanged = ApplyTransitionBlend(modelIndex, entityModel);
        poseChanged |= ApplyBoneOverrides(entityModel);
        if (poseChanged)
        {
            UpdateBoneMatrices(entityModel);
        }

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
        ClearTransition();
        ResetAnimationEventCursor();
        currentStateName = null;
        currentAnimationName = animationName;
        loopAnimation = loop;
        animationAutoPlay = true;
        animationStepSeconds = stepSeconds;

        if (entityModels.Count == 0)
        {
            return true;
        }

        return ApplyAnimationToLoadedModels();
    }

    public void PauseAnimation()
    {
        animationAutoPlay = false;
        foreach (EntityModel entityModel in entityModels)
        {
            entityModel.animator.Pause();
        }
    }

    public void ResumeAnimation()
    {
        animationAutoPlay = true;
        foreach (EntityModel entityModel in entityModels)
        {
            entityModel.animator.Play(animationStepSeconds);
        }
    }

    public void RestartAnimation(float stepSeconds = 1f / 60f)
    {
        animationAutoPlay = true;
        animationStepSeconds = stepSeconds;
        ResetAnimationEventCursor();
        boneOverrides.Clear();
        foreach (EntityModel entityModel in entityModels)
        {
            entityModel.animator.Restart(animationStepSeconds);
        }
    }

    private void ApplyInitialAnimation()
    {
        LumaAnimationStateSpec? initialState = options.AnimationGraph?.GetInitialState();
        if (initialState is not null)
        {
            ApplyAnimationState(initialState);
            return;
        }

        currentAnimationName = options.AnimationName;
        loopAnimation = options.LoopAnimation;
        animationAutoPlay = true;
        animationStepSeconds = options.AnimationStepSeconds;
    }

    private bool SetAnimationState(string stateName, float transitionSeconds = 0f)
    {
        LumaAnimationStateSpec? state = options.AnimationGraph?.FindState(stateName);
        return state is not null && ApplyAnimationState(state, transitionSeconds);
    }

    private bool TriggerAnimation(string triggerName)
    {
        LumaAnimationGraphSpec? graph = options.AnimationGraph;
        if (graph is null)
        {
            return false;
        }

        foreach (LumaAnimationTransitionSpec transition in graph.Transitions)
        {
            if (!transition.Trigger.Equals(triggerName, StringComparison.Ordinal))
            {
                continue;
            }

            if (!MatchesTransitionSource(transition.From))
            {
                continue;
            }

            return SetAnimationState(transition.To, transition.TransitionSeconds);
        }

        return false;
    }

    private bool MatchesTransitionSource(string from)
    {
        return from.Equals("*", StringComparison.Ordinal) ||
            from.Equals(currentStateName, StringComparison.Ordinal);
    }

    private bool ApplyAnimationState(LumaAnimationStateSpec state, float transitionSeconds = 0f)
    {
        IReadOnlyList<EntityModelBonePose[]>? startPoses = transitionSeconds > 0f && entityModels.Count > 0
            ? CaptureCurrentBonePoses()
            : null;

        currentStateName = state.Name;
        currentAnimationName = state.Animation;
        loopAnimation = state.Loop;
        animationAutoPlay = state.AutoPlay;
        animationStepSeconds = state.StepSeconds;
        ResetAnimationEventCursor();
        ApplyDeclaredBoneOverrides(state);

        if (entityModels.Count == 0)
        {
            return true;
        }

        bool applied = ApplyAnimationToLoadedModels();
        if (applied && startPoses is not null)
        {
            BeginTransition(startPoses, transitionSeconds);
        }
        else
        {
            ClearTransition();
        }

        return applied;
    }

    private void ApplyDeclaredBoneOverrides(LumaAnimationStateSpec state)
    {
        boneOverrides.Clear();
        foreach (LumaBoneOverrideSpec boneOverride in state.BoneOverrides)
        {
            if (!string.IsNullOrWhiteSpace(boneOverride.Bone))
            {
                boneOverrides[boneOverride.Bone] = boneOverride;
            }
        }
    }

    private bool SetBoneOverride(string boneName, LumaBoneOverrideSpec boneOverride)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(boneName);
        ArgumentNullException.ThrowIfNull(boneOverride);

        boneOverrides[boneName] = new LumaBoneOverrideSpec
        {
            Bone = boneName,
            RotationDegrees = boneOverride.RotationDegrees,
            PositionOffset = boneOverride.PositionOffset
        };
        return true;
    }

    private bool ClearBoneOverride(string boneName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(boneName);
        return boneOverrides.Remove(boneName);
    }

    private void ClearBoneOverrides()
    {
        boneOverrides.Clear();
    }

    private IReadOnlyList<LumaAnimationEvent> DrainAnimationEvents()
    {
        if (pendingAnimationEvents.Count == 0)
        {
            return [];
        }

        LumaAnimationEvent[] events = [.. pendingAnimationEvents];
        pendingAnimationEvents.Clear();
        return events;
    }

    private void ResetAnimationEventCursor()
    {
        lastAnimationEventTime = 0f;
        hasAnimationEventCursor = true;
        pendingAnimationEvents.Clear();
    }

    private void EmitAnimationEvents()
    {
        if (!hasAnimationEventCursor ||
            string.IsNullOrWhiteSpace(currentStateName) ||
            entityModels.Count == 0)
        {
            return;
        }

        LumaAnimationStateSpec? state = options.AnimationGraph?.FindState(currentStateName);
        if (state is null || state.Events.Count == 0)
        {
            return;
        }

        EntityModelAnimator animator = entityModels[0].animator;
        float currentTime = animator.time;
        foreach (LumaAnimationEventSpec eventSpec in state.Events)
        {
            if (DidCrossEventTime(lastAnimationEventTime, currentTime, eventSpec.TimeSeconds, animator.loop))
            {
                pendingAnimationEvents.Add(new LumaAnimationEvent(
                    state.Name,
                    eventSpec.Name,
                    eventSpec.TimeSeconds,
                    eventSpec.Payload,
                    eventSpec.Effects));
            }
        }

        lastAnimationEventTime = currentTime;
    }

    private static bool DidCrossEventTime(float previousTime, float currentTime, float eventTime, bool loop)
    {
        if (eventTime < 0f)
        {
            return false;
        }

        if (loop && currentTime < previousTime)
        {
            return eventTime > previousTime || eventTime <= currentTime;
        }

        return eventTime > previousTime && eventTime <= currentTime;
    }

    private void ApplyAutomaticCompletionTransition()
    {
        if (transitionStartPoses is not null ||
            string.IsNullOrWhiteSpace(currentStateName) ||
            entityModels.Count == 0)
        {
            return;
        }

        LumaAnimationStateSpec? state = options.AnimationGraph?.FindState(currentStateName);
        if (state is null || string.IsNullOrWhiteSpace(state.OnCompleteState))
        {
            return;
        }

        if (!entityModels.All(entityModel => entityModel.animator.ended))
        {
            return;
        }

        _ = SetAnimationState(state.OnCompleteState, state.OnCompleteTransitionSeconds);
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

            AllumeriaAnimatedModelSharedAssets sharedAssets = GetOrLoadSharedAssets(assets);

            for (int i = 0; i < sharedAssets.Models.Count; i++)
            {
                BBModel bbModel = sharedAssets.Models[i];
                var entityModel = new EntityModel(bbModel, sharedAssets.Texture);
                bbModels.Add(bbModel);
                entityModels.Add(entityModel);
                modelBounds.Add(sharedAssets.Bounds[i]);
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
            if (animationAutoPlay)
            {
                entityModel.animator.Play(animationStepSeconds);
            }
            else
            {
                entityModel.animator.Pause();
            }

            animationStarted = true;
        }

        return animationStarted;
    }

    private IReadOnlyList<EntityModelBonePose[]> CaptureCurrentBonePoses()
    {
        var result = new List<EntityModelBonePose[]>(entityModels.Count);
        foreach (EntityModel entityModel in entityModels)
        {
            var poses = new EntityModelBonePose[entityModel.bonesFlat.Length];
            for (int i = 0; i < entityModel.bonesFlat.Length; i++)
            {
                EntityModelBone bone = entityModel.bonesFlat[i];
                poses[i] = new EntityModelBonePose(bone.position, bone.rotation);
            }

            result.Add(poses);
        }

        return result;
    }

    private void BeginTransition(IReadOnlyList<EntityModelBonePose[]> startPoses, float seconds)
    {
        transitionStartPoses = startPoses;
        transitionSeconds = MathF.Max(0f, seconds);
        transitionElapsedSeconds = 0f;
    }

    private void ClearTransition()
    {
        transitionStartPoses = null;
        transitionSeconds = 0f;
        transitionElapsedSeconds = 0f;
    }

    private void AdvanceTransition()
    {
        if (transitionStartPoses is null)
        {
            return;
        }

        transitionElapsedSeconds += 1f / 60f;
        if (transitionElapsedSeconds >= transitionSeconds)
        {
            ClearTransition();
        }
    }

    private bool ApplyTransitionBlend(int modelIndex, EntityModel entityModel)
    {
        if (modelIndex < 0 || transitionStartPoses is null || transitionSeconds <= 0f)
        {
            return false;
        }

        if (modelIndex >= transitionStartPoses.Count)
        {
            return false;
        }

        EntityModelBonePose[] startPoses = transitionStartPoses[modelIndex];
        float amount = Math.Clamp(transitionElapsedSeconds / transitionSeconds, 0f, 1f);
        for (int i = 0; i < entityModel.bonesFlat.Length && i < startPoses.Length; i++)
        {
            EntityModelBone bone = entityModel.bonesFlat[i];
            EntityModelBonePose start = startPoses[i];
            bone.position = Vector3.Lerp(start.Position, bone.position, amount);
            bone.rotation = Vector3.Lerp(start.Rotation, bone.rotation, amount);
        }

        return true;
    }

    private bool ApplyBoneOverrides(EntityModel entityModel)
    {
        if (boneOverrides.Count == 0)
        {
            return false;
        }

        bool applied = false;
        foreach (LumaBoneOverrideSpec boneOverride in boneOverrides.Values)
        {
            EntityModelBone? bone = entityModel.FindBone(boneOverride.Bone);
            if (bone is null)
            {
                continue;
            }

            if (boneOverride.PositionOffset is LumaVector3 position)
            {
                bone.position = ToVector3(position);
                applied = true;
            }

            if (boneOverride.RotationDegrees is LumaVector3 rotation)
            {
                bone.rotation = ToVector3(rotation);
                applied = true;
            }
        }

        return applied;
    }

    private static void UpdateBoneMatrices(EntityModel entityModel)
    {
        Matrix4 initialMatrix = Matrix4.Identity;
        if (entityModel.externalBaseBone is not null)
        {
            initialMatrix = Matrix4.CreateTranslation(entityModel.externalBaseBone.modelBone.origin) *
                entityModel.externalBaseBone.matrix;
        }

        foreach (EntityModelBone bone in entityModel.boneTree)
        {
            bone.UpdateMatrix(initialMatrix);
        }
    }

    private AllumeriaAnimatedModelSharedAssets GetOrLoadSharedAssets(AllumeriaModelAssetSet assets)
    {
        string cacheKey = BuildSharedAssetCacheKey(assets);
        lock (SharedAssetGate)
        {
            if (SharedAssets.TryGetValue(cacheKey, out AllumeriaAnimatedModelSharedAssets? cached))
            {
                return cached;
            }

            AllumeriaAnimatedModelSharedAssets loaded = LoadSharedAssets(assets);
            SharedAssets.Add(cacheKey, loaded);
            return loaded;
        }
    }

    private AllumeriaAnimatedModelSharedAssets LoadSharedAssets(AllumeriaModelAssetSet assets)
    {
        var texture = new Texture(
            assets.TexturePath,
            flip: options.TextureFlip,
            clamp: options.TextureClamp,
            mipmaps: options.TextureMipmaps,
            keepImage: options.TextureKeepImage,
            nearest: options.TextureNearest,
            data: null!,
            fixedSize: options.TextureFixedSize);

        var modelDocuments = new List<JsonDocument>();
        var models = new List<BBModel>();
        var bounds = new List<ModelBounds>();
        foreach (string modelPath in assets.ModelPaths)
        {
            JsonDocument modelDocument = JsonDocument.Parse(File.ReadAllText(modelPath));
            var bbModel = new BBModel(modelDocument.RootElement);
            bbModel.mesh = bbModel.ConvertToMesh();

            modelDocuments.Add(modelDocument);
            models.Add(bbModel);
            bounds.Add(CalculateModelBounds(bbModel));
        }

        return new AllumeriaAnimatedModelSharedAssets(modelDocuments, models, bounds, texture);
    }

    private string BuildSharedAssetCacheKey(AllumeriaModelAssetSet assets)
    {
        return string.Join(
            "|",
            options.TextureFlip,
            options.TextureClamp,
            options.TextureMipmaps,
            options.TextureKeepImage,
            options.TextureNearest,
            options.TextureFixedSize,
            assets.TexturePath,
            string.Join(";", assets.ModelPaths));
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
        bbModels.Clear();
        entityModels.Clear();
        modelBounds.Clear();
        lightSampleOffsets.Clear();
    }

    private sealed class AllumeriaAnimationController(AllumeriaAnimatedModel model) : ILumaAnimationController
    {
        public string? CurrentState => model.currentStateName;

        public string? CurrentAnimation => model.currentAnimationName;

        public bool SetState(string stateName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(stateName);
            return model.SetAnimationState(stateName);
        }

        public bool Trigger(string triggerName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(triggerName);
            return model.TriggerAnimation(triggerName);
        }

        public void Pause()
        {
            model.PauseAnimation();
        }

        public void Resume()
        {
            model.ResumeAnimation();
        }

        public void Restart(float? stepSeconds = null)
        {
            model.RestartAnimation(stepSeconds ?? model.animationStepSeconds);
        }

        public IReadOnlyList<LumaAnimationEvent> DrainEvents()
        {
            return model.DrainAnimationEvents();
        }

        public bool SetBoneOverride(string boneName, LumaBoneOverrideSpec boneOverride)
        {
            return model.SetBoneOverride(boneName, boneOverride);
        }

        public bool ClearBoneOverride(string boneName)
        {
            return model.ClearBoneOverride(boneName);
        }

        public void ClearBoneOverrides()
        {
            model.ClearBoneOverrides();
        }
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
    public LumaAnimationGraphSpec? AnimationGraph { get; init; }
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
        ValidateSpec(spec);
        return new AllumeriaAnimatedModelOptions
        {
            Name = spec.Name,
            AssetDirectory = spec.AssetRoot,
            TextureFileName = spec.TexturePath,
            ModelFileName = spec.ModelPath,
            ChunkManifestFileName = spec.ChunkManifestPath,
            AnimationName = spec.InitialAnimation,
            LoopAnimation = spec.LoopInitialAnimation,
            AnimationStepSeconds = spec.AnimationStepSeconds,
            AnimationGraph = spec.AnimationGraph
        };
    }

    private static void ValidateSpec(LumaAnimatedModelSpec spec)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.AssetRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.ModelPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.TexturePath);

        if (spec.AnimationStepSeconds <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(spec), "AnimationStepSeconds must be greater than zero.");
        }

        if (spec.AnimationGraph is null)
        {
            return;
        }

        ValidateGraph(spec.Name, spec.AnimationGraph);
    }

    private static void ValidateGraph(string modelName, LumaAnimationGraphSpec graph)
    {
        var states = new HashSet<string>(StringComparer.Ordinal);
        foreach (LumaAnimationStateSpec state in graph.States)
        {
            if (string.IsNullOrWhiteSpace(state.Name))
            {
                throw new ArgumentException($"Animation graph for {modelName} contains a state with no name.");
            }

            if (!states.Add(state.Name))
            {
                throw new ArgumentException($"Animation graph for {modelName} contains duplicate state '{state.Name}'.");
            }

            if (state.StepSeconds <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(graph), $"Animation state '{state.Name}' in {modelName} must have StepSeconds greater than zero.");
            }

            if (state.OnCompleteTransitionSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(graph), $"Animation state '{state.Name}' in {modelName} cannot have a negative OnCompleteTransitionSeconds.");
            }

            ValidateStateEvents(modelName, state);
            ValidateStateBoneOverrides(modelName, state);
        }

        if (!string.IsNullOrWhiteSpace(graph.InitialState) && !states.Contains(graph.InitialState))
        {
            throw new ArgumentException($"Animation graph for {modelName} references missing InitialState '{graph.InitialState}'.");
        }

        foreach (LumaAnimationStateSpec state in graph.States)
        {
            if (!string.IsNullOrWhiteSpace(state.OnCompleteState) && !states.Contains(state.OnCompleteState))
            {
                throw new ArgumentException($"Animation state '{state.Name}' in {modelName} references missing OnCompleteState '{state.OnCompleteState}'.");
            }
        }

        foreach (LumaAnimationTransitionSpec transition in graph.Transitions)
        {
            ValidateTransition(modelName, states, transition);
        }
    }

    private static void ValidateStateEvents(string modelName, LumaAnimationStateSpec state)
    {
        foreach (LumaAnimationEventSpec animationEvent in state.Events)
        {
            if (string.IsNullOrWhiteSpace(animationEvent.Name))
            {
                throw new ArgumentException($"Animation state '{state.Name}' in {modelName} contains an event with no name.");
            }

            if (animationEvent.TimeSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(state), $"Animation event '{animationEvent.Name}' in {modelName} cannot have a negative TimeSeconds.");
            }

            foreach (LumaAnimationEffectSpec effect in animationEvent.Effects)
            {
                ValidateAnimationEffect(modelName, state, animationEvent, effect);
            }
        }
    }

    private static void ValidateAnimationEffect(
        string modelName,
        LumaAnimationStateSpec state,
        LumaAnimationEventSpec animationEvent,
        LumaAnimationEffectSpec effect)
    {
        if (string.IsNullOrWhiteSpace(effect.Kind))
        {
            throw new ArgumentException($"Animation event '{animationEvent.Name}' in state '{state.Name}' for {modelName} contains an effect with no Kind.");
        }

        if (effect.Strength < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(state), $"Animation effect '{effect.Kind}' on event '{animationEvent.Name}' in {modelName} cannot have a negative Strength.");
        }
    }

    private static void ValidateStateBoneOverrides(string modelName, LumaAnimationStateSpec state)
    {
        foreach (LumaBoneOverrideSpec boneOverride in state.BoneOverrides)
        {
            ValidateBoneOverride(modelName, boneOverride);
        }
    }

    private static void ValidateBoneOverride(string modelName, LumaBoneOverrideSpec boneOverride)
    {
        if (string.IsNullOrWhiteSpace(boneOverride.Bone))
        {
            throw new ArgumentException($"Animation graph for {modelName} contains a bone override with no bone name.");
        }

        if (boneOverride.RotationDegrees is null && boneOverride.PositionOffset is null)
        {
            throw new ArgumentException($"Bone override '{boneOverride.Bone}' in {modelName} must set RotationDegrees, PositionOffset, or both.");
        }
    }

    private static void ValidateTransition(string modelName, HashSet<string> states, LumaAnimationTransitionSpec transition)
    {
        if (string.IsNullOrWhiteSpace(transition.Trigger))
        {
            throw new ArgumentException($"Animation graph for {modelName} contains a transition with no trigger.");
        }

        if (string.IsNullOrWhiteSpace(transition.From))
        {
            throw new ArgumentException($"Animation transition '{transition.Trigger}' in {modelName} has no From state.");
        }

        if (string.IsNullOrWhiteSpace(transition.To))
        {
            throw new ArgumentException($"Animation transition '{transition.Trigger}' in {modelName} has no To state.");
        }

        if (!transition.From.Equals("*", StringComparison.Ordinal) && !states.Contains(transition.From))
        {
            throw new ArgumentException($"Animation transition '{transition.Trigger}' in {modelName} references missing From state '{transition.From}'.");
        }

        if (!states.Contains(transition.To))
        {
            throw new ArgumentException($"Animation transition '{transition.Trigger}' in {modelName} references missing To state '{transition.To}'.");
        }

        if (transition.TransitionSeconds < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(transition), $"Animation transition '{transition.Trigger}' in {modelName} cannot have a negative TransitionSeconds.");
        }
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

internal readonly record struct EntityModelBonePose(Vector3 Position, Vector3 Rotation);

internal sealed record AllumeriaAnimatedModelSharedAssets(
    IReadOnlyList<JsonDocument> Documents,
    IReadOnlyList<BBModel> Models,
    IReadOnlyList<ModelBounds> Bounds,
    Texture Texture);

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
