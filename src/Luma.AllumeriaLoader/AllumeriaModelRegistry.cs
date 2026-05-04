using Luma.Abstractions.Models;

namespace Luma.AllumeriaLoader;

internal static class AllumeriaModelRegistry
{
    private static readonly List<AllumeriaAnimatedModel> AnimatedModels = [];
    private static readonly ILumaModelService Models = new AllumeriaModelService();

    public static ILumaModelService ModelService => Models;

    public static AllumeriaAnimatedModel RegisterAnimated(AllumeriaAnimatedModelOptions options)
    {
        var model = new AllumeriaAnimatedModel(options);
        AnimatedModels.Add(model);
        return model;
    }

    public static ILumaAnimatedModel RegisterAnimated(LumaAnimatedModelSpec spec)
    {
        return RegisterAnimated(AllumeriaAnimatedModelOptions.FromSpec(spec));
    }

    public static void UpdateAll()
    {
        foreach (AllumeriaAnimatedModel model in AnimatedModels)
        {
            model.Update();
        }
    }
}

internal sealed class AllumeriaModelService : ILumaModelService
{
    public ILumaAnimatedModel LoadAnimated(LumaAnimatedModelSpec spec)
    {
        return AllumeriaModelRegistry.RegisterAnimated(spec);
    }
}
