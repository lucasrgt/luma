using Luma.ModelLib.Animation;
using Luma.ModelLib.Model;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Nodes;

const int AllumeriaEntityShaderBoneLimit = 20;
const double CoordinateEpsilon = 0.0001d;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    PrintHelp();
    return 0;
}

try
{
    return args[0] switch
    {
        "model" => RunModelCommand(args[1..]),
        "bbmodel" => BakeBbModel(args[1..]),
        "validate-allumeria-bbmodel" => ValidateAllumeriaBbModel(args[1..]),
        _ => Fail($"Unknown command: {args[0]}")
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    if (Environment.GetEnvironmentVariable("LUMA_PIPELINE_DEBUG") is "1" or "true" or "TRUE")
    {
        Console.Error.WriteLine(ex);
    }

    return 1;
}

static int RunModelCommand(string[] args)
{
    if (args.Length == 0 || args[0] is "-h" or "--help")
    {
        PrintModelHelp();
        return 0;
    }

    return args[0] switch
    {
        "convert" => ConvertModel(args[1..]),
        _ => Fail($"Unknown model command: {args[0]}")
    };
}

static int ConvertModel(string[] args)
{
    if (args.Length == 0 || args[0] is "-h" or "--help")
    {
        PrintModelConvertHelp();
        return 0;
    }

    string[] valueOptions =
    [
        "--target",
        "--animation",
        "--anim",
        "--texture",
        "--output",
        "-o",
        "--game-dir",
        "--light-chunks",
        "--spatial-chunks",
        "--report"
    ];
    string[] flagOptions =
    [
        "--flat",
        "--no-animations",
        "--partial-rig",
        "--chunks",
        "--chunked",
        "--validate"
    ];

    ValidateKnownOptions(args[1..], valueOptions, flagOptions);

    string inputPath = args[0];
    string target = ReadStringOption(args, "--target") ?? "allumeria";
    if (!target.Equals("allumeria", StringComparison.OrdinalIgnoreCase))
    {
        return Fail($"Unsupported target '{target}'. Supported target: allumeria.");
    }

    string? animationPath = ReadStringOption(args, "--animation", "--anim");
    if (animationPath is null)
    {
        return Fail("Missing --animation <anim.json>. Allumeria conversion needs the Luma animation metadata for pivots and hierarchy.");
    }

    string? texturePath = ReadStringOption(args, "--texture");
    if (texturePath is null)
    {
        return Fail("Missing --texture <texture.png>. The converter uses the PNG size to write Blockbench UV coordinates.");
    }

    bool chunked = HasFlag(args, "--chunks", "--chunked");
    string outputPath = ReadStringOption(args, "--output", "-o") ?? DeriveOutputPath(inputPath, chunked);
    bool flattenHierarchy = HasFlag(args, "--flat");
    bool includeAnimations = !HasFlag(args, "--no-animations");
    bool partialAnimationRig = !flattenHierarchy && HasFlag(args, "--partial-rig");
    int minimumChunkCount = ReadIntOption(args, 1, "--light-chunks", "--spatial-chunks");
    if (minimumChunkCount < 1)
    {
        return Fail("--light-chunks must be 1 or greater.");
    }

    bool validate = HasFlag(args, "--validate");
    string? gameDir = ReadStringOption(args, "--game-dir");
    if (validate && gameDir is null)
    {
        string defaultGameDir = @"C:\Program Files (x86)\Steam\steamapps\common\Allumeria Demo";
        gameDir = Directory.Exists(defaultGameDir) ? defaultGameDir : null;
    }

    if (validate && gameDir is null)
    {
        return Fail("Missing --game-dir <path>. Validation needs the Allumeria install directory.");
    }

    string? reportPath = ReadOptionalStringOption(args, "--report");
    if (HasFlag(args, "--report") && reportPath is null)
    {
        reportPath = Path.ChangeExtension(outputPath, ".report.json");
    }

    var options = new ModelConvertOptions(
        InputPath: inputPath,
        AnimationPath: animationPath,
        TexturePath: texturePath,
        OutputPath: outputPath,
        Target: "allumeria",
        FlattenHierarchy: flattenHierarchy,
        IncludeAnimations: includeAnimations,
        PartialAnimationRig: partialAnimationRig,
        Chunked: chunked,
        MinimumChunkCount: minimumChunkCount);

    ModelExportReport report = ConvertObjToAllumeria(options);
    PrintExportReport(report);

    if (reportPath is not null)
    {
        WriteExportReport(reportPath, report);
        Console.WriteLine($"  report: {Path.GetFullPath(reportPath)}");
    }

    if (!validate)
    {
        return 0;
    }

    Console.WriteLine();
    Console.WriteLine("Validating generated Allumeria model...");
    return ValidateAllumeriaBbModel([gameDir!, outputPath]);
}

static int ValidateAllumeriaBbModel(string[] args)
{
    if (args.Length < 2)
    {
        return Fail("Usage: Luma.AssetPipeline validate-allumeria-bbmodel <game-dir> <model.json>");
    }

    string gameDir = Path.GetFullPath(args[0]);
    string modelPath = Path.GetFullPath(args[1]);
    string allumeriaAssembly = Path.Combine(gameDir, "Allumeria.dll");

    AssemblyLoadContext.Default.Resolving += (_, assemblyName) =>
    {
        string candidate = Path.Combine(gameDir, $"{assemblyName.Name}.dll");
        return File.Exists(candidate)
            ? AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate)
            : null;
    };

    Assembly assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(allumeriaAssembly);
    Type bbModelType = assembly.GetType("Allumeria.DataManagement.ModelParsing.BBModel", throwOnError: true)!;

    string[]? chunkModelPaths = TryReadChunkModelPaths(modelPath);
    if (chunkModelPaths is not null)
    {
        Console.WriteLine($"Allumeria BBModel chunk manifest OK: {modelPath}");
        Console.WriteLine($"  chunks: {chunkModelPaths.Length}");

        int failureCount = 0;
        foreach (string chunkModelPath in chunkModelPaths)
        {
            if (ValidateSingleAllumeriaBbModel(bbModelType, chunkModelPath) != 0)
            {
                failureCount++;
            }
        }

        return failureCount == 0 ? 0 : 1;
    }

    return ValidateSingleAllumeriaBbModel(bbModelType, modelPath);
}

static int ValidateSingleAllumeriaBbModel(Type bbModelType, string modelPath)
{
    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(modelPath));
    int jsonValidationResult = ValidateBbModelJson(document.RootElement, modelPath);
    if (jsonValidationResult != 0)
    {
        return jsonValidationResult;
    }

    object model = Activator.CreateInstance(bbModelType, document.RootElement)
        ?? throw new InvalidOperationException("BBModel constructor returned null.");

    FieldInfo partsField = bbModelType.GetField("parts", BindingFlags.Public | BindingFlags.Instance)
        ?? throw new MissingFieldException(bbModelType.FullName, "parts");
    FieldInfo animationsField = bbModelType.GetField("animations", BindingFlags.Public | BindingFlags.Instance)
        ?? throw new MissingFieldException(bbModelType.FullName, "animations");
    FieldInfo boneCountField = bbModelType.GetField("boneCount", BindingFlags.Public | BindingFlags.Instance)
        ?? throw new MissingFieldException(bbModelType.FullName, "boneCount");

    int partCount = CountList(partsField.GetValue(model));
    int animationCount = CountList(animationsField.GetValue(model));
    int boneCount = (int)(boneCountField.GetValue(model) ?? -1);

    Console.WriteLine($"Allumeria BBModel parse OK: {modelPath}");
    Console.WriteLine($"  parts: {partCount}");
    Console.WriteLine($"  bones: {boneCount}/{AllumeriaEntityShaderBoneLimit}");
    Console.WriteLine($"  animations: {animationCount}");
    if (boneCount > AllumeriaEntityShaderBoneLimit)
    {
        return Fail($"Allumeria entity shader supports at most {AllumeriaEntityShaderBoneLimit} bones; model has {boneCount}.");
    }

    MethodInfo convertToMesh = bbModelType.GetMethod("ConvertToMesh", BindingFlags.Public | BindingFlags.Instance)
        ?? throw new MissingMethodException(bbModelType.FullName, "ConvertToMesh");

    try
    {
        object mesh = convertToMesh.Invoke(model, null)
            ?? throw new InvalidOperationException("ConvertToMesh returned null.");

        Type meshType = mesh.GetType();
        int vertexFloatCount = CountArray(meshType.GetField("vertices")?.GetValue(mesh));
        int indexCount = CountArray(meshType.GetField("indices")?.GetValue(mesh));
        int texCoordFloatCount = CountArray(meshType.GetField("texCoords")?.GetValue(mesh));

        Console.WriteLine("Allumeria ConvertToMesh OK");
        Console.WriteLine($"  vertex floats: {vertexFloatCount}");
        Console.WriteLine($"  indices: {indexCount}");
        Console.WriteLine($"  texcoord floats: {texCoordFloatCount}");
    }
    catch (TargetInvocationException ex) when (ex.InnerException?.Message.Contains("OpenGL", StringComparison.OrdinalIgnoreCase) == true)
    {
        Console.WriteLine("Allumeria ConvertToMesh skipped: OpenGL context is required.");
    }

    return 0;
}

static string[]? TryReadChunkModelPaths(string manifestPath)
{
    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestPath));
    if (!document.RootElement.TryGetProperty("chunks", out JsonElement chunksElement) ||
        chunksElement.ValueKind != JsonValueKind.Array)
    {
        return null;
    }

    string manifestDirectory = Path.GetDirectoryName(manifestPath)
        ?? Directory.GetCurrentDirectory();
    var chunkPaths = new List<string>();
    foreach (JsonElement chunkElement in chunksElement.EnumerateArray())
    {
        string chunkPath = chunkElement.GetString()
            ?? throw new InvalidDataException($"Chunk manifest entry is not a string: {manifestPath}");
        chunkPaths.Add(Path.GetFullPath(Path.Combine(manifestDirectory, chunkPath)));
    }

    return chunkPaths.ToArray();
}

static int ValidateBbModelJson(JsonElement root, string modelPath)
{
    var issues = new List<ValidationIssue>();
    ValidateTextureCoordinates(root, issues);
    ValidateOriginsAndPivots(root, issues);

    int warningCount = issues.Count(issue => issue.Severity == ValidationSeverity.Warning);
    int errorCount = issues.Count(issue => issue.Severity == ValidationSeverity.Error);
    foreach (ValidationIssue issue in issues)
    {
        string label = issue.Severity == ValidationSeverity.Error ? "ERROR" : "WARN";
        TextWriter writer = issue.Severity == ValidationSeverity.Error ? Console.Error : Console.Out;
        writer.WriteLine($"{label}: {issue.Path}: {issue.Message}");
    }

    if (errorCount > 0)
    {
        return Fail($"Allumeria BBModel asset validation failed for {modelPath}: {errorCount} error(s), {warningCount} warning(s).");
    }

    Console.WriteLine($"Allumeria BBModel asset validation OK: {modelPath}");
    Console.WriteLine($"  warnings: {warningCount}");
    return 0;
}

static void ValidateTextureCoordinates(JsonElement root, List<ValidationIssue> issues)
{
    if (!TryGetTextureResolution(root, out double width, out double height))
    {
        issues.Add(ValidationIssue.Error("$.resolution", "Missing numeric texture resolution."));
        return;
    }

    if (!root.TryGetProperty("elements", out JsonElement elements) ||
        elements.ValueKind != JsonValueKind.Array)
    {
        issues.Add(ValidationIssue.Error("$.elements", "Missing elements array."));
        return;
    }

    int elementIndex = 0;
    foreach (JsonElement element in elements.EnumerateArray())
    {
        string elementName = OptionalString(element, "name") ?? $"#{elementIndex}";
        if (element.TryGetProperty("faces", out JsonElement faces) &&
            faces.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty face in faces.EnumerateObject())
            {
                string facePath = $"$.elements[{elementIndex}]('{elementName}').faces.{face.Name}.uv";
                if (!face.Value.TryGetProperty("uv", out JsonElement uv))
                {
                    issues.Add(ValidationIssue.Error(facePath, "Missing uv."));
                    continue;
                }

                ValidateUvElement(uv, width, height, facePath, issues);
            }
        }

        elementIndex++;
    }
}

static void ValidateUvElement(
    JsonElement uv,
    double width,
    double height,
    string path,
    List<ValidationIssue> issues)
{
    if (uv.ValueKind == JsonValueKind.Array)
    {
        double[] values = ReadNumberArray(uv, path, issues);
        if (values.Length == 0)
        {
            return;
        }

        if (values.Length % 2 != 0)
        {
            issues.Add(ValidationIssue.Error(path, $"Expected an even number of UV values, got {values.Length}."));
            return;
        }

        for (int i = 0; i < values.Length; i += 2)
        {
            ValidateUvPoint(values[i], values[i + 1], width, height, $"{path}[{i / 2}]", issues);
        }

        return;
    }

    if (uv.ValueKind == JsonValueKind.Object)
    {
        foreach (JsonProperty vertexUv in uv.EnumerateObject())
        {
            double[] values = ReadNumberArray(vertexUv.Value, $"{path}.{vertexUv.Name}", issues);
            if (values.Length != 2)
            {
                issues.Add(ValidationIssue.Error($"{path}.{vertexUv.Name}", $"Expected [u, v], got {values.Length} value(s)."));
                continue;
            }

            ValidateUvPoint(values[0], values[1], width, height, $"{path}.{vertexUv.Name}", issues);
        }

        return;
    }

    issues.Add(ValidationIssue.Error(path, $"Expected UV array/object, got {uv.ValueKind}."));
}

static void ValidateUvPoint(
    double u,
    double v,
    double width,
    double height,
    string path,
    List<ValidationIssue> issues)
{
    if (!IsFinite(u) || !IsFinite(v))
    {
        issues.Add(ValidationIssue.Error(path, $"UV contains non-finite value [{Format(u)}, {Format(v)}]."));
        return;
    }

    if (u < -CoordinateEpsilon || u > width + CoordinateEpsilon ||
        v < -CoordinateEpsilon || v > height + CoordinateEpsilon)
    {
        issues.Add(ValidationIssue.Error(
            path,
            $"UV [{Format(u)}, {Format(v)}] is outside texture bounds 0..{Format(width)}, 0..{Format(height)}."));
    }
}

static void ValidateOriginsAndPivots(JsonElement root, List<ValidationIssue> issues)
{
    if (!root.TryGetProperty("elements", out JsonElement elements) ||
        elements.ValueKind != JsonValueKind.Array)
    {
        return;
    }

    var boundsByUuid = new Dictionary<string, Bounds3>(StringComparer.Ordinal);
    int elementIndex = 0;
    foreach (JsonElement element in elements.EnumerateArray())
    {
        string elementName = OptionalString(element, "name") ?? $"#{elementIndex}";
        string elementPath = $"$.elements[{elementIndex}]('{elementName}')";
        string? type = OptionalString(element, "type");
        Bounds3? bounds = TryGetElementBounds(element, elementPath, issues);

        if (bounds is not null &&
            element.TryGetProperty("uuid", out JsonElement uuidElement) &&
            uuidElement.ValueKind == JsonValueKind.String &&
            uuidElement.GetString() is { Length: > 0 } uuid)
        {
            boundsByUuid[uuid] = bounds.Value;
        }

        if (bounds is not null &&
            element.TryGetProperty("origin", out JsonElement originElement) &&
            TryReadVector3(originElement, $"{elementPath}.origin", issues, out Vector3d origin))
        {
            if (type == "mesh" &&
                !origin.IsNearlyZero() &&
                !bounds.Value.Inflate(0.25d).Contains(origin))
            {
                issues.Add(ValidationIssue.Error(
                    $"{elementPath}.origin",
                    $"Mesh origin {origin} is outside vertex bounds {bounds.Value}. Mesh origins should normally stay [0, 0, 0] in Luma exports; a moved mesh origin can make animated parts orbit their pivot."));
            }
            else if (type == "cube" &&
                HasNonZeroRotation(element) &&
                !bounds.Value.Inflate(16d).Contains(origin))
            {
                issues.Add(ValidationIssue.Warning(
                    $"{elementPath}.origin",
                    $"Rotated cube origin {origin} is far from cube bounds {bounds.Value}; verify the pivot in Blockbench."));
            }
        }

        elementIndex++;
    }

    if (root.TryGetProperty("outliner", out JsonElement outliner) &&
        outliner.ValueKind == JsonValueKind.Array)
    {
        ValidateOutlinerPivots(outliner, boundsByUuid, "$.outliner", issues);
    }
}

static Bounds3? ValidateOutlinerPivots(
    JsonElement nodes,
    IReadOnlyDictionary<string, Bounds3> boundsByUuid,
    string path,
    List<ValidationIssue> issues)
{
    Bounds3? combined = null;
    int index = 0;
    foreach (JsonElement node in nodes.EnumerateArray())
    {
        string nodePath = $"{path}[{index}]";
        if (node.ValueKind == JsonValueKind.String)
        {
            string? uuid = node.GetString();
            if (uuid is not null && boundsByUuid.TryGetValue(uuid, out Bounds3 partBounds))
            {
                combined = combined is null ? partBounds : combined.Value.Include(partBounds);
            }

            index++;
            continue;
        }

        if (node.ValueKind != JsonValueKind.Object)
        {
            issues.Add(ValidationIssue.Error(nodePath, $"Expected outliner node object/string, got {node.ValueKind}."));
            index++;
            continue;
        }

        string nodeName = OptionalString(node, "name") ?? $"#{index}";
        string namedPath = $"{nodePath}('{nodeName}')";
        Bounds3? childBounds = null;
        if (node.TryGetProperty("children", out JsonElement children) &&
            children.ValueKind == JsonValueKind.Array)
        {
            childBounds = ValidateOutlinerPivots(children, boundsByUuid, $"{namedPath}.children", issues);
        }

        if (childBounds is not null &&
            node.TryGetProperty("origin", out JsonElement originElement) &&
            TryReadVector3(originElement, $"{namedPath}.origin", issues, out Vector3d origin))
        {
            double padding = Math.Max(16d, childBounds.Value.DiagonalLength * 1.5d);
            if (!childBounds.Value.Inflate(padding).Contains(origin))
            {
                issues.Add(ValidationIssue.Warning(
                    $"{namedPath}.origin",
                    $"Bone origin {origin} is far from descendant bounds {childBounds.Value}; verify the animation pivot."));
            }
        }

        if (childBounds is not null)
        {
            combined = combined is null ? childBounds.Value : combined.Value.Include(childBounds.Value);
        }

        index++;
    }

    return combined;
}

static Bounds3? TryGetElementBounds(JsonElement element, string path, List<ValidationIssue> issues)
{
    string? type = OptionalString(element, "type");
    if (type == "mesh")
    {
        if (!element.TryGetProperty("vertices", out JsonElement vertices) ||
            vertices.ValueKind != JsonValueKind.Object)
        {
            issues.Add(ValidationIssue.Error($"{path}.vertices", "Mesh element is missing vertices."));
            return null;
        }

        Bounds3? bounds = null;
        foreach (JsonProperty vertex in vertices.EnumerateObject())
        {
            if (!TryReadVector3(vertex.Value, $"{path}.vertices.{vertex.Name}", issues, out Vector3d position))
            {
                continue;
            }

            bounds = bounds is null ? Bounds3.FromPoint(position) : bounds.Value.Include(position);
        }

        return bounds;
    }

    if (type == "cube")
    {
        if (!element.TryGetProperty("from", out JsonElement fromElement) ||
            !TryReadVector3(fromElement, $"{path}.from", issues, out Vector3d from) ||
            !element.TryGetProperty("to", out JsonElement toElement) ||
            !TryReadVector3(toElement, $"{path}.to", issues, out Vector3d to))
        {
            issues.Add(ValidationIssue.Error(path, "Cube element is missing valid from/to bounds."));
            return null;
        }

        return Bounds3.FromPoints(from, to);
    }

    return null;
}

static bool TryGetTextureResolution(JsonElement root, out double width, out double height)
{
    width = 0;
    height = 0;
    if (!root.TryGetProperty("resolution", out JsonElement resolution) ||
        resolution.ValueKind != JsonValueKind.Object ||
        !resolution.TryGetProperty("width", out JsonElement widthElement) ||
        !resolution.TryGetProperty("height", out JsonElement heightElement) ||
        !TryGetDouble(widthElement, out width) ||
        !TryGetDouble(heightElement, out height))
    {
        return false;
    }

    return width > 0 && height > 0;
}

static bool TryReadVector3(
    JsonElement element,
    string path,
    List<ValidationIssue> issues,
    out Vector3d value)
{
    value = default;
    double[] values = ReadNumberArray(element, path, issues);
    if (values.Length != 3)
    {
        issues.Add(ValidationIssue.Error(path, $"Expected [x, y, z], got {values.Length} value(s)."));
        return false;
    }

    value = new Vector3d(values[0], values[1], values[2]);
    if (!value.IsFinite)
    {
        issues.Add(ValidationIssue.Error(path, $"Vector contains non-finite value {value}."));
        return false;
    }

    return true;
}

static double[] ReadNumberArray(JsonElement element, string path, List<ValidationIssue> issues)
{
    if (element.ValueKind != JsonValueKind.Array)
    {
        issues.Add(ValidationIssue.Error(path, $"Expected numeric array, got {element.ValueKind}."));
        return [];
    }

    var values = new List<double>();
    int index = 0;
    foreach (JsonElement item in element.EnumerateArray())
    {
        if (!TryGetDouble(item, out double value))
        {
            issues.Add(ValidationIssue.Error($"{path}[{index}]", $"Expected number, got {item.ValueKind}."));
            index++;
            continue;
        }

        values.Add(value);
        index++;
    }

    return values.ToArray();
}

static bool TryGetDouble(JsonElement element, out double value)
{
    if (element.ValueKind == JsonValueKind.Number)
    {
        return element.TryGetDouble(out value);
    }

    if (element.ValueKind == JsonValueKind.String &&
        double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
    {
        return true;
    }

    value = 0;
    return false;
}

static string? OptionalString(JsonElement element, string propertyName)
{
    return element.TryGetProperty(propertyName, out JsonElement property) &&
        property.ValueKind == JsonValueKind.String
        ? property.GetString()
        : null;
}

static bool HasNonZeroRotation(JsonElement element)
{
    if (!element.TryGetProperty("rotation", out JsonElement rotationElement) ||
        rotationElement.ValueKind != JsonValueKind.Array)
    {
        return false;
    }

    foreach (JsonElement item in rotationElement.EnumerateArray())
    {
        if (TryGetDouble(item, out double value) && Math.Abs(value) > CoordinateEpsilon)
        {
            return true;
        }
    }

    return false;
}

static bool IsFinite(double value)
{
    return !double.IsNaN(value) && !double.IsInfinity(value);
}

static string Format(double value)
{
    return value.ToString("0.###", CultureInfo.InvariantCulture);
}

static int BakeBbModel(string[] args)
{
    if (args.Length < 4)
    {
        return Fail("Usage: Luma.AssetPipeline bbmodel <obj> <anim.json> <texture.png> <output.json>");
    }

    string objPath = args[0];
    string animPath = args[1];
    string texturePath = args[2];
    string outputPath = args[3];
    bool flattenHierarchy = args[4..].Contains("--flat", StringComparer.OrdinalIgnoreCase);
    bool includeAnimations = !args[4..].Contains("--no-animations", StringComparer.OrdinalIgnoreCase);
    bool partialAnimationRig = !flattenHierarchy && args[4..].Contains("--partial-rig", StringComparer.OrdinalIgnoreCase);
    bool chunked = args[4..].Contains("--chunks", StringComparer.OrdinalIgnoreCase) ||
        args[4..].Contains("--chunked", StringComparer.OrdinalIgnoreCase);
    int minimumChunkCount = ReadIntOption(args[4..], 1, "--light-chunks", "--spatial-chunks");
    if (minimumChunkCount < 1)
    {
        return Fail("--light-chunks must be 1 or greater.");
    }

    var convertOptions = new ModelConvertOptions(
        InputPath: objPath,
        AnimationPath: animPath,
        TexturePath: texturePath,
        OutputPath: outputPath,
        Target: "allumeria",
        FlattenHierarchy: flattenHierarchy,
        IncludeAnimations: includeAnimations,
        PartialAnimationRig: partialAnimationRig,
        Chunked: chunked,
        MinimumChunkCount: minimumChunkCount);

    ModelExportReport report = ConvertObjToAllumeria(convertOptions);
    PrintExportReport(report);
    return 0;
}

static ModelExportReport ConvertObjToAllumeria(ModelConvertOptions convertOptions)
{
    ValidateInputFile(convertOptions.InputPath, "OBJ model");
    ValidateInputFile(convertOptions.AnimationPath, "animation JSON");
    ValidateInputFile(convertOptions.TexturePath, "texture PNG");

    ObjMesh mesh = ReadObjMesh(convertOptions.InputPath);
    AnimationBundle animation = ReadAnimationBundle(convertOptions.AnimationPath);
    (int width, int height) = ReadTextureSize(convertOptions.TexturePath);

    string modelName = Path.GetFileNameWithoutExtension(convertOptions.OutputPath);
    if (convertOptions.Chunked && modelName.EndsWith(".chunks", StringComparison.OrdinalIgnoreCase))
    {
        modelName = modelName[..^".chunks".Length];
    }

    var options = new AllumeriaBbModelExportOptions
    {
        Name = modelName,
        TextureWidth = width,
        TextureHeight = height,
        FlattenHierarchy = convertOptions.FlattenHierarchy,
        IncludeAnimations = convertOptions.IncludeAnimations,
        PartialAnimationRig = convertOptions.PartialAnimationRig,
        MinimumChunkCount = convertOptions.MinimumChunkCount
    };

    string hierarchy = convertOptions.FlattenHierarchy
        ? "flat"
        : convertOptions.PartialAnimationRig ? "partial-rig" : "animated";

    if (convertOptions.Chunked)
    {
        IReadOnlyList<AllumeriaBbModelChunk> chunks = AllumeriaBbModelExporter.ExportChunks(mesh, animation, options);
        IReadOnlyList<ModelChunkReport> chunkReports = WriteChunkedModel(
            convertOptions.OutputPath,
            convertOptions.TexturePath,
            options.Name,
            chunks);

        return new ModelExportReport(
            Target: convertOptions.Target,
            InputPath: Path.GetFullPath(convertOptions.InputPath),
            AnimationPath: Path.GetFullPath(convertOptions.AnimationPath),
            TexturePath: Path.GetFullPath(convertOptions.TexturePath),
            OutputPath: Path.GetFullPath(convertOptions.OutputPath),
            Mode: "chunked",
            Hierarchy: hierarchy,
            AnimationsIncluded: convertOptions.IncludeAnimations,
            TextureWidth: width,
            TextureHeight: height,
            GroupCount: mesh.Groups.Count,
            PolygonCount: mesh.Polygons.Count,
            TriangleCount: mesh.Faces.Count,
            AnimationCount: animation.Clips.Count,
            MinimumLightChunks: convertOptions.MinimumChunkCount,
            Chunks: chunkReports);
    }

    string json = AllumeriaBbModelExporter.Export(mesh, animation, options);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(convertOptions.OutputPath))!);
    File.WriteAllText(convertOptions.OutputPath, json);
    ModelChunkReport modelReport = BuildSingleModelReport(json, convertOptions.OutputPath, options.Name);

    return new ModelExportReport(
        Target: convertOptions.Target,
        InputPath: Path.GetFullPath(convertOptions.InputPath),
        AnimationPath: Path.GetFullPath(convertOptions.AnimationPath),
        TexturePath: Path.GetFullPath(convertOptions.TexturePath),
        OutputPath: Path.GetFullPath(convertOptions.OutputPath),
        Mode: "single",
        Hierarchy: hierarchy,
        AnimationsIncluded: convertOptions.IncludeAnimations,
        TextureWidth: width,
        TextureHeight: height,
        GroupCount: mesh.Groups.Count,
        PolygonCount: mesh.Polygons.Count,
        TriangleCount: mesh.Faces.Count,
        AnimationCount: animation.Clips.Count,
        MinimumLightChunks: 1,
        Chunks: [modelReport]);
}

static IReadOnlyList<ModelChunkReport> WriteChunkedModel(
    string outputPath,
    string texturePath,
    string modelName,
    IReadOnlyList<AllumeriaBbModelChunk> chunks)
{
    string outputFullPath = Path.GetFullPath(outputPath);
    string outputDirectory = Path.GetDirectoryName(outputFullPath)
        ?? Directory.GetCurrentDirectory();
    string outputBaseName = Path.GetFileNameWithoutExtension(outputFullPath);
    if (outputBaseName.EndsWith(".chunks", StringComparison.OrdinalIgnoreCase))
    {
        outputBaseName = outputBaseName[..^".chunks".Length];
    }

    Directory.CreateDirectory(outputDirectory);

    var chunkFiles = new JsonArray();
    var reports = new List<ModelChunkReport>(chunks.Count);
    for (int i = 0; i < chunks.Count; i++)
    {
        string chunkFileName = $"{outputBaseName}.chunk_{i:00}.bbmodel.json";
        string chunkPath = Path.Combine(outputDirectory, chunkFileName);
        File.WriteAllText(chunkPath, chunks[i].Json);
        chunkFiles.Add(chunkFileName);
        reports.Add(new ModelChunkReport(
            Name: chunks[i].Name,
            Path: Path.GetFullPath(chunkPath),
            BoneCount: chunks[i].BoneCount,
            PartCount: chunks[i].PartCount));
    }

    var manifest = new JsonObject
    {
        ["format"] = "luma.allumeria.modelchunks.v1",
        ["name"] = modelName,
        ["texture"] = Path.GetFileName(texturePath),
        ["chunks"] = chunkFiles
    };

    File.WriteAllText(outputFullPath, manifest.ToJsonString(new JsonSerializerOptions
    {
        WriteIndented = true
    }));

    return reports;
}

static ModelChunkReport BuildSingleModelReport(string json, string outputPath, string modelName)
{
    JsonObject root = JsonNode.Parse(json)?.AsObject()
        ?? throw new InvalidDataException("Generated model JSON was empty.");
    JsonArray outliner = root["outliner"]?.AsArray()
        ?? throw new InvalidDataException("Generated model has no outliner.");
    JsonArray elements = root["elements"]?.AsArray()
        ?? throw new InvalidDataException("Generated model has no elements.");

    return new ModelChunkReport(
        Name: modelName,
        Path: Path.GetFullPath(outputPath),
        BoneCount: CountOutlinerBonesForReport(outliner),
        PartCount: elements.Count);
}

static int CountOutlinerBonesForReport(JsonArray nodes)
{
    int count = 0;
    foreach (JsonNode? node in nodes)
    {
        if (node is not JsonObject obj)
        {
            continue;
        }

        count++;
        if (obj["children"] is JsonArray children)
        {
            count += CountOutlinerBonesForReport(children);
        }
    }

    return count;
}

static void PrintExportReport(ModelExportReport report)
{
    Console.WriteLine($"Wrote {report.OutputPath}");
    Console.WriteLine($"  target: {report.Target}");
    Console.WriteLine($"  mode: {report.Mode}");
    Console.WriteLine($"  hierarchy: {report.Hierarchy}");
    Console.WriteLine($"  groups: {report.GroupCount}");
    Console.WriteLine($"  polygons: {report.PolygonCount}");
    Console.WriteLine($"  triangles: {report.TriangleCount}");
    Console.WriteLine($"  texture: {report.TextureWidth}x{report.TextureHeight}");
    Console.WriteLine($"  animations included: {report.AnimationsIncluded}");
    Console.WriteLine($"  animations: {report.AnimationCount}");
    Console.WriteLine($"  chunks: {report.Chunks.Count}");
    if (report.Mode == "chunked")
    {
        Console.WriteLine($"  minimum light chunks: {report.MinimumLightChunks}");
        Console.WriteLine($"  bones/chunk: {string.Join(", ", report.Chunks.Select(chunk => chunk.BoneCount))}");
        Console.WriteLine($"  parts/chunk: {string.Join(", ", report.Chunks.Select(chunk => chunk.PartCount))}");
    }
    else if (report.Chunks.Count == 1)
    {
        Console.WriteLine($"  bones: {report.Chunks[0].BoneCount}/{AllumeriaEntityShaderBoneLimit}");
        Console.WriteLine($"  parts: {report.Chunks[0].PartCount}");
    }
}

static void WriteExportReport(string reportPath, ModelExportReport report)
{
    string fullPath = Path.GetFullPath(reportPath);
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
    File.WriteAllText(fullPath, JsonSerializer.Serialize(report, new JsonSerializerOptions
    {
        WriteIndented = true
    }));
}

static void ValidateInputFile(string path, string label)
{
    if (!File.Exists(path))
    {
        throw new FileNotFoundException($"{label} not found: {path}", path);
    }
}

static ObjMesh ReadObjMesh(string path)
{
    try
    {
        return ObjParser.Parse(File.ReadAllText(path));
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
    {
        throw new InvalidDataException($"Failed to read OBJ model '{path}': {ex.Message}", ex);
    }
}

static AnimationBundle ReadAnimationBundle(string path)
{
    try
    {
        return AnimationJsonLoader.Load(File.ReadAllText(path));
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or ArgumentException)
    {
        throw new InvalidDataException($"Failed to read animation JSON '{path}': {ex.Message}", ex);
    }
}

static (int Width, int Height) ReadTextureSize(string path)
{
    try
    {
        return PngSizeReader.Read(path);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
    {
        throw new InvalidDataException($"Failed to read texture PNG '{path}': {ex.Message}", ex);
    }
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

static void PrintHelp()
{
    Console.WriteLine("Luma.AssetPipeline");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  model convert <input.obj> --target allumeria --animation <anim.json> --texture <texture.png> --output <model.json|chunks.json> [--partial-rig] [--chunks] [--light-chunks N] [--validate --game-dir <dir>] [--report [path]]");
    Console.WriteLine("  bbmodel <obj> <anim.json> <texture.png> <output.json> [--flat] [--no-animations] [--partial-rig] [--chunks] [--light-chunks N]");
    Console.WriteLine("  validate-allumeria-bbmodel <game-dir> <model.json|chunks.json>");
}

static void PrintModelHelp()
{
    Console.WriteLine("Luma.AssetPipeline model");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  convert <input.obj> --target allumeria --animation <anim.json> --texture <texture.png> --output <model.json|chunks.json>");
}

static void PrintModelConvertHelp()
{
    Console.WriteLine("Luma.AssetPipeline model convert");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  Luma.AssetPipeline model convert <input.obj> --target allumeria --animation <anim.json> --texture <texture.png> --output <model.json|chunks.json> [options]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --partial-rig          Keep only animated roots needed by the model.");
    Console.WriteLine("  --flat                 Export without bones/animations hierarchy.");
    Console.WriteLine("  --no-animations        Write static model data only.");
    Console.WriteLine("  --chunks               Write a .chunks.json manifest plus chunk files.");
    Console.WriteLine("  --light-chunks N       Force at least N spatial chunks for local lighting.");
    Console.WriteLine("  --validate             Validate generated JSON with the native Allumeria parser.");
    Console.WriteLine("  --game-dir <dir>       Allumeria install directory for --validate.");
    Console.WriteLine("  --report [path]        Write a JSON export report. Defaults next to output.");
}

static int ReadIntOption(string[] args, int defaultValue, params string[] names)
{
    for (int i = 0; i < args.Length; i++)
    {
        if (!names.Contains(args[i], StringComparer.OrdinalIgnoreCase))
        {
            continue;
        }

        if (i + 1 >= args.Length ||
            !int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            throw new ArgumentException($"{args[i]} expects an integer value.");
        }

        return value;
    }

    return defaultValue;
}

static string? ReadStringOption(string[] args, params string[] names)
{
    for (int i = 0; i < args.Length; i++)
    {
        if (!names.Contains(args[i], StringComparer.OrdinalIgnoreCase))
        {
            continue;
        }

        if (i + 1 >= args.Length || LooksLikeOption(args[i + 1]))
        {
            throw new ArgumentException($"{args[i]} expects a value.");
        }

        return args[i + 1];
    }

    return null;
}

static string? ReadOptionalStringOption(string[] args, params string[] names)
{
    for (int i = 0; i < args.Length; i++)
    {
        if (!names.Contains(args[i], StringComparer.OrdinalIgnoreCase))
        {
            continue;
        }

        if (i + 1 >= args.Length || LooksLikeOption(args[i + 1]))
        {
            return null;
        }

        return args[i + 1];
    }

    return null;
}

static bool HasFlag(string[] args, params string[] names)
{
    return args.Any(arg => names.Contains(arg, StringComparer.OrdinalIgnoreCase));
}

static void ValidateKnownOptions(string[] args, string[] valueOptions, string[] flagOptions)
{
    for (int i = 0; i < args.Length; i++)
    {
        string arg = args[i];
        if (!LooksLikeOption(arg))
        {
            continue;
        }

        if (valueOptions.Contains(arg, StringComparer.OrdinalIgnoreCase))
        {
            if (arg.Equals("--report", StringComparison.OrdinalIgnoreCase) &&
                (i + 1 >= args.Length || LooksLikeOption(args[i + 1])))
            {
                continue;
            }

            if (i + 1 >= args.Length || LooksLikeOption(args[i + 1]))
            {
                throw new ArgumentException($"{arg} expects a value.");
            }

            i++;
            continue;
        }

        if (flagOptions.Contains(arg, StringComparer.OrdinalIgnoreCase))
        {
            continue;
        }

        throw new ArgumentException($"Unknown option: {arg}");
    }
}

static bool LooksLikeOption(string value)
{
    return value.StartsWith("-", StringComparison.Ordinal);
}

static string DeriveOutputPath(string inputPath, bool chunked)
{
    string directory = Path.GetDirectoryName(inputPath)
        ?? Directory.GetCurrentDirectory();
    string name = Path.GetFileNameWithoutExtension(inputPath);
    string fileName = chunked ? $"{name}.chunks.json" : $"{name}.bbmodel.json";
    return Path.Combine(directory, fileName);
}

static int CountList(object? value)
{
    return value is System.Collections.ICollection collection ? collection.Count : -1;
}

static int CountArray(object? value)
{
    return value is Array array ? array.Length : -1;
}

internal sealed record ModelConvertOptions(
    string InputPath,
    string AnimationPath,
    string TexturePath,
    string OutputPath,
    string Target,
    bool FlattenHierarchy,
    bool IncludeAnimations,
    bool PartialAnimationRig,
    bool Chunked,
    int MinimumChunkCount);

internal sealed record ModelExportReport(
    string Target,
    string InputPath,
    string AnimationPath,
    string TexturePath,
    string OutputPath,
    string Mode,
    string Hierarchy,
    bool AnimationsIncluded,
    int TextureWidth,
    int TextureHeight,
    int GroupCount,
    int PolygonCount,
    int TriangleCount,
    int AnimationCount,
    int MinimumLightChunks,
    IReadOnlyList<ModelChunkReport> Chunks);

internal sealed record ModelChunkReport(
    string Name,
    string Path,
    int BoneCount,
    int PartCount);

internal enum ValidationSeverity
{
    Warning,
    Error
}

internal readonly record struct ValidationIssue(ValidationSeverity Severity, string Path, string Message)
{
    public static ValidationIssue Warning(string path, string message) => new(ValidationSeverity.Warning, path, message);

    public static ValidationIssue Error(string path, string message) => new(ValidationSeverity.Error, path, message);
}

internal readonly record struct Vector3d(double X, double Y, double Z)
{
    public bool IsFinite => IsValueFinite(X) && IsValueFinite(Y) && IsValueFinite(Z);

    public bool IsNearlyZero()
    {
        return Math.Abs(X) <= 0.0001d &&
            Math.Abs(Y) <= 0.0001d &&
            Math.Abs(Z) <= 0.0001d;
    }

    public override string ToString()
    {
        return $"[{FormatValue(X)}, {FormatValue(Y)}, {FormatValue(Z)}]";
    }

    private static bool IsValueFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static string FormatValue(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}

internal readonly record struct Bounds3(Vector3d Min, Vector3d Max)
{
    public double DiagonalLength
    {
        get
        {
            double dx = Max.X - Min.X;
            double dy = Max.Y - Min.Y;
            double dz = Max.Z - Min.Z;
            return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        }
    }

    public static Bounds3 FromPoint(Vector3d point) => new(point, point);

    public static Bounds3 FromPoints(Vector3d a, Vector3d b)
    {
        return new Bounds3(
            new Vector3d(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z)),
            new Vector3d(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z)));
    }

    public Bounds3 Include(Vector3d point)
    {
        return new Bounds3(
            new Vector3d(Math.Min(Min.X, point.X), Math.Min(Min.Y, point.Y), Math.Min(Min.Z, point.Z)),
            new Vector3d(Math.Max(Max.X, point.X), Math.Max(Max.Y, point.Y), Math.Max(Max.Z, point.Z)));
    }

    public Bounds3 Include(Bounds3 other)
    {
        return Include(other.Min).Include(other.Max);
    }

    public Bounds3 Inflate(double amount)
    {
        return new Bounds3(
            new Vector3d(Min.X - amount, Min.Y - amount, Min.Z - amount),
            new Vector3d(Max.X + amount, Max.Y + amount, Max.Z + amount));
    }

    public bool Contains(Vector3d point)
    {
        return point.X >= Min.X - 0.0001d &&
            point.X <= Max.X + 0.0001d &&
            point.Y >= Min.Y - 0.0001d &&
            point.Y <= Max.Y + 0.0001d &&
            point.Z >= Min.Z - 0.0001d &&
            point.Z <= Max.Z + 0.0001d;
    }

    public override string ToString()
    {
        return $"{Min}..{Max}";
    }
}

internal static class PngSizeReader
{
    public static (int Width, int Height) Read(string path)
    {
        Span<byte> header = stackalloc byte[24];
        using FileStream stream = File.OpenRead(path);
        if (stream.Read(header) != header.Length)
        {
            throw new InvalidDataException($"PNG file is too short: {path}");
        }

        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (!header[..8].SequenceEqual(signature))
        {
            throw new InvalidDataException($"Not a PNG file: {path}");
        }

        int width = ReadBigEndianInt32(header[16..20]);
        int height = ReadBigEndianInt32(header[20..24]);
        return (width, height);
    }

    private static int ReadBigEndianInt32(ReadOnlySpan<byte> bytes)
    {
        return (bytes[0] << 24)
            | (bytes[1] << 16)
            | (bytes[2] << 8)
            | bytes[3];
    }
}
