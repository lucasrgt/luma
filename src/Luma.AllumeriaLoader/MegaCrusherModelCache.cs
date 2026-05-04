using Luma.Abstractions.Models;
using OpenTK.Mathematics;

namespace Luma.AllumeriaLoader;

internal static class MegaCrusherModelCache
{
    public static readonly ILumaAnimatedModel Model = AllumeriaModelRegistry.RegisterAnimated(
        new LumaAnimatedModelSpec
        {
            Name = "Mega Crusher",
            AssetRoot = Path.Combine(Directory.GetCurrentDirectory(), "mods", "luma", "assets", "models"),
            TexturePath = "retronism_megacrusher.png",
            ModelPath = "mega_crusher.bbmodel.json",
            ChunkManifestPath = "mega_crusher.chunks.json",
            InitialAnimation = "working"
        });

    public static void Initialize()
    {
        _ = Model;
    }

    public static void RenderAt(Vector3 position) => Model.RenderBlock(new LumaVector3(position.X, position.Y, position.Z));
}
