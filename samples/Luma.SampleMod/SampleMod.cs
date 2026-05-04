using Luma.Abstractions;
using Luma.Abstractions.Content;
using Luma.Abstractions.Models;

namespace Luma.SampleMod;

[LumaMod("luma.sample", "Luma Sample Mod", "0.1.0")]
public sealed class SampleMod : IAllumeriaMod
{
    private IModLogger? logger;
    private bool warnedMissingContentService;

    public void Init(IModContext context)
    {
        logger = context.Logger;

        ILumaContentService? content = context.Services.Get<ILumaContentService>();
        if (content is null)
        {
            warnedMissingContentService = true;
            logger.Info("Sample mod initialized without ILumaContentService. DevHost can load the mod, but Allumeria is needed to register the sample block.");
            return;
        }

        string assetRoot = Path.Combine(context.ModsDirectory, "luma.sample", "assets", "models");
        content.RegisterAnimatedBlock(new LumaAnimatedBlockSpec
        {
            BlockId = "luma.sample_rotor",
            DisplayName = "Sample Rotor",
            Description = "Small Luma animated block sample.",
            Model = new LumaAnimatedModelSpec
            {
                Name = "Sample Rotor",
                AssetRoot = assetRoot,
                ModelPath = "sample_rotor.bbmodel.json",
                TexturePath = "sample_rotor.png",
                InitialAnimation = "spin"
            },
            AddWoodRecipe = true,
            RecipeOutputCount = 1
        });

        logger.Info("Sample mod queued Sample Rotor. Recipe: 1x any planks -> 1x Sample Rotor.");
    }

    public void Tick(IModTickContext context)
    {
        if (warnedMissingContentService && context.TickIndex == 1)
        {
            logger?.Info("Sample mod ticked in host-only mode.");
        }
    }

    public void Render(IModRenderContext context)
    {
    }

    public void Shutdown()
    {
        logger?.Info("Sample mod shutdown.");
    }
}
