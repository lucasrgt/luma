using Luma.Abstractions;
using Luma.Abstractions.Content;
using Luma.Abstractions.Models;

namespace Luma.MegaCrusherShowcase;

[LumaMod("luma.megacrusher_showcase", "Mega Crusher Showcase", "0.1.0")]
public sealed class MegaCrusherShowcaseMod : IAllumeriaMod
{
    private IModLogger? logger;

    public void Init(IModContext context)
    {
        logger = context.Logger;

        ILumaContentService? content = context.Services.Get<ILumaContentService>();
        if (content is null)
        {
            logger.Info("Mega Crusher showcase loaded without ILumaContentService. DevHost will log registrations only when the test service is enabled.");
            return;
        }

        string assetRoot = Path.Combine(context.ModsDirectory, "luma.showcase", "assets", "models");
        content.RegisterAnimatedBlock(new LumaAnimatedBlockSpec
        {
            BlockId = "luma.showcase_mega_crusher",
            DisplayName = "Mega Crusher",
            Description = "Large chunked animated model showcase.",
            Model = new LumaAnimatedModelSpec
            {
                Name = "Mega Crusher",
                AssetRoot = assetRoot,
                ModelPath = "mega_crusher.bbmodel.json",
                ChunkManifestPath = "mega_crusher.chunks.json",
                TexturePath = "retronism_megacrusher.png",
                AnimationGraph = new LumaAnimationGraphSpec
                {
                    InitialState = "working",
                    States =
                    [
                        new LumaAnimationStateSpec
                        {
                            Name = "working",
                            Animation = "working",
                            Loop = true,
                            Events =
                            [
                                new LumaAnimationEventSpec
                                {
                                    Name = "crusher-cycle",
                                    TimeSeconds = 1.0f,
                                    Payload = "mechanical-impact"
                                }
                            ]
                        },
                        new LumaAnimationStateSpec
                        {
                            Name = "idle",
                            Animation = "working",
                            Loop = true,
                            AutoPlay = false
                        }
                    ],
                    Transitions =
                    [
                        new LumaAnimationTransitionSpec
                        {
                            Trigger = "pause",
                            From = "working",
                            To = "idle",
                            TransitionSeconds = 0.2f
                        },
                        new LumaAnimationTransitionSpec
                        {
                            Trigger = "work",
                            From = "idle",
                            To = "working",
                            TransitionSeconds = 0.2f
                        }
                    ]
                }
            },
            AddWoodRecipe = false,
            Recipes =
            [
                new LumaRecipeSpec
                {
                    Station = "work_bench",
                    OutputCount = 1,
                    Ingredients =
                    [
                        new LumaRecipeIngredientSpec
                        {
                            AliasId = "any_planks",
                            Amount = 8
                        },
                        new LumaRecipeIngredientSpec
                        {
                            ItemId = "stone",
                            Amount = 4
                        }
                    ]
                }
            ]
        });

        content.RegisterAnimatedBlock(new LumaAnimatedBlockSpec
        {
            BlockId = "luma.showcase_rotor",
            DisplayName = "Luma Rotor",
            Description = "Small animated model portability showcase.",
            Model = new LumaAnimatedModelSpec
            {
                Name = "Luma Rotor",
                AssetRoot = assetRoot,
                ModelPath = "test_rotor.bbmodel.json",
                TexturePath = "retronism_megacrusher.png",
                AnimationGraph = new LumaAnimationGraphSpec
                {
                    InitialState = "spinning",
                    States =
                    [
                        new LumaAnimationStateSpec
                        {
                            Name = "spinning",
                            Animation = "spin",
                            Loop = true
                        }
                    ]
                }
            },
            AddWoodRecipe = false,
            Recipes =
            [
                new LumaRecipeSpec
                {
                    Station = "inventory",
                    OutputCount = 1,
                    Ingredients =
                    [
                        new LumaRecipeIngredientSpec
                        {
                            AliasId = "any_planks",
                            Amount = 1
                        }
                    ]
                }
            ]
        });

        logger.Info("Mega Crusher showcase queued animated blocks through the public content API.");
    }
}
