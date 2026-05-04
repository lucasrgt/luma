using System.Text.Json;
using System.Text.Json.Serialization;

namespace Luma.Patcher;

internal sealed class PatchManifest
{
    public string? ExpectedModuleMvid { get; set; }

    public string RuntimeAssemblyName { get; set; } = "Luma.Runtime";

    public string RuntimeNamespace { get; set; } = "Luma.Runtime";

    public string RuntimeTypeName { get; set; } = "LumaEntrypoints";

    public List<HookSpec> Hooks { get; set; } = [];

    public static PatchManifest Load(string path)
    {
        string json = File.ReadAllText(path);
        PatchManifest? manifest = JsonSerializer.Deserialize<PatchManifest>(json, JsonOptions);
        if (manifest is null)
        {
            throw new InvalidOperationException($"Patch manifest is empty: {path}");
        }

        return manifest;
    }

    public static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

internal sealed class HookSpec
{
    public string Name { get; set; } = "";

    public string TargetType { get; set; } = "";

    public string TargetMethod { get; set; } = "";

    public int? TargetParameterCount { get; set; }

    public string RuntimeMethod { get; set; } = "";

    public HookInsertMode Insert { get; set; } = HookInsertMode.Start;

    public HookArgumentMode ArgumentMode { get; set; } = HookArgumentMode.None;
}

internal enum HookInsertMode
{
    Start,
    BeforeReturn
}

internal enum HookArgumentMode
{
    None,
    This,
    FirstArgument
}
