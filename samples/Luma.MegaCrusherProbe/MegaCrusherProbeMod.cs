using System.Reflection;
using Luma.Abstractions;
using Luma.ModelLib.Animation;
using Luma.ModelLib.Model;

namespace Luma.MegaCrusherProbe;

[LumaMod("luma.megacrusher_probe", "Mega Crusher Probe", "0.1.0")]
public sealed class MegaCrusherProbeMod : IAllumeriaMod
{
    private const string ModelFileName = "MegaCrusher.obj";
    private const string AnimationFileName = "MegaCrusher.anim.json";
    private const string TextureFileName = "retronism_megacrusher.png";

    private IModLogger? logger;
    private ObjMesh? mesh;
    private AnimationBundle? animationBundle;

    public void Init(IModContext context)
    {
        logger = context.Logger;
        string modelDirectory = ResolveModelDirectory();
        string modelPath = Path.Combine(modelDirectory, ModelFileName);
        string animationPath = Path.Combine(modelDirectory, AnimationFileName);
        string texturePath = Path.Combine(modelDirectory, TextureFileName);

        mesh = ObjParser.Parse(File.ReadAllText(modelPath));
        animationBundle = AnimationJsonLoader.Load(File.ReadAllText(animationPath));

        logger.Info("Mega Crusher probe initialized.");
        logger.Info($"Model: {modelPath}");
        logger.Info($"Texture: {texturePath} ({new FileInfo(texturePath).Length} bytes)");
        logger.Info($"Mesh: {mesh.Positions.Count} vertices, {mesh.TexCoords.Count} uvs, {mesh.Normals.Count} normals, {mesh.Polygons.Count} polygons, {mesh.Faces.Count} triangles, {mesh.Groups.Count} groups.");
        logger.Info($"Animation bundle: format {animationBundle.FormatVersion}, {animationBundle.Pivots.Count} pivots, {animationBundle.ChildMap.Count} child links, {animationBundle.Clips.Count} clips.");

        if (animationBundle.Clips.TryGetValue("working", out AnimationClip? working))
        {
            int keyframes = working.Bones.Values.Sum(ch => ch.Rotation.Count + ch.Position.Count + ch.Scale.Count);
            logger.Info($"Working clip: length {working.LengthSeconds:0.###}s, loop={working.Loop}, animated bones={working.Bones.Count}, keyframes={keyframes}.");
        }
        else
        {
            logger.Warn("Working clip was not found in MegaCrusher.anim.json.");
        }
    }

    public void Render(IModRenderContext context)
    {
        if (context.FrameIndex == 1)
        {
            logger?.Info("Mega Crusher probe reached Render. Next milestone: bind renderer hook to draw the mesh.");
        }
    }

    private static string ResolveModelDirectory()
    {
        string assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? AppContext.BaseDirectory;

        return Path.Combine(assemblyDirectory, "assets", "models");
    }
}
