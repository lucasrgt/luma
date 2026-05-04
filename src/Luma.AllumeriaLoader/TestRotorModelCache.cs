using Luma.Abstractions.Models;
using OpenTK.Mathematics;

namespace Luma.AllumeriaLoader;

internal static class TestRotorModelCache
{
    public static readonly ILumaAnimatedModel Model = AllumeriaModelRegistry.RegisterAnimated(
        new LumaAnimatedModelSpec
        {
            Name = "Luma Rotor",
            AssetRoot = Path.Combine(Directory.GetCurrentDirectory(), "mods", "luma", "assets", "models"),
            TexturePath = "retronism_megacrusher.png",
            ModelPath = "test_rotor.bbmodel.json",
            InitialAnimation = "spin"
        });

    public static void Initialize()
    {
        _ = Model;
    }

    public static void RenderAt(Vector3 position) => Model.RenderBlock(new LumaVector3(position.X, position.Y, position.Z));
}
