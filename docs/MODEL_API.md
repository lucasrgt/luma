# Luma Model API

The runtime model API lives in `Luma.Abstractions.Models`. Mods ask for an
`ILumaModelService`, load an animated model from a declarative spec, and render
it at a block position.

On Allumeria, `Loader.dll` registers the native adapter as `ILumaModelService`
before mod `Init` runs.

```csharp
using Luma.Abstractions;
using Luma.Abstractions.Models;

public sealed class MachineMod : IAllumeriaMod
{
    private ILumaAnimatedModel? model;

    public void Init(IModContext context)
    {
        ILumaModelService models = context.Services.Get<ILumaModelService>()
            ?? throw new InvalidOperationException("Luma model service is not available.");

        model = models.LoadAnimated(new LumaAnimatedModelSpec
        {
            Name = "Mega Crusher",
            AssetRoot = Path.Combine(context.ModsDirectory, "machine_mod", "assets", "models"),
            ModelPath = "mega_crusher.bbmodel.json",
            TexturePath = "retronism_megacrusher.png",
            ChunkManifestPath = "mega_crusher.chunks.json",
            AnimationGraph = new LumaAnimationGraphSpec
            {
                InitialState = "working",
                States =
                [
                    new LumaAnimationStateSpec
                    {
                        Name = "working",
                        Animation = "working",
                        Loop = true
                    },
                    new LumaAnimationStateSpec
                    {
                        Name = "idle",
                        Animation = "working",
                        AutoPlay = false
                    }
                ],
                Transitions =
                [
                    new LumaAnimationTransitionSpec
                    {
                        Trigger = "pause",
                        From = "working",
                        To = "idle"
                    },
                    new LumaAnimationTransitionSpec
                    {
                        Trigger = "work",
                        From = "idle",
                        To = "working"
                    }
                ]
            }
        });
    }

    public void Render(IModRenderContext context)
    {
        model?.RenderBlock(LumaVector3.FromBlock(10, 64, 10));
    }
}
```

Core contracts:

- `ILumaModelService.LoadAnimated(...)` creates an adapter-backed model.
- `LumaAnimatedModelSpec.AnimationGraph` declares named animation states and
  trigger transitions.
- `ILumaAnimatedModel.Animation.SetState(...)` switches to a declared state.
- `ILumaAnimatedModel.Animation.Trigger(...)` follows a declared transition.
- `ILumaAnimatedModel.SetAnimation(...)` changes and starts an animation.
- `ILumaAnimatedModel.PauseAnimation()` pauses the current animation.
- `ILumaAnimatedModel.RestartAnimation()` restarts the current animation.
- `ILumaAnimatedModel.RenderBlock(...)` renders at a block-space position.

`InitialAnimation`, `LoopInitialAnimation`, and `AnimationStepSeconds` still work
as a compact compatibility path. Prefer `AnimationGraph` for new mods because it
keeps the mod's animation behavior declarative and portable.

Adapter details such as Allumeria `BBModel`, `EntityModel`, OpenTK matrices,
texture instances, and light values stay outside the public mod API.

## Animated Block Registration

`Luma.Abstractions.Content` exposes the first content-level API for sample-sized
mods. It is intentionally narrow: register one animated block backed by a public
model spec, optionally with the simple wood recipe used by the sample.

```csharp
using Luma.Abstractions.Content;
using Luma.Abstractions.Models;

ILumaContentService content = context.Services.Get<ILumaContentService>()
    ?? throw new InvalidOperationException("Luma content service is not available.");

content.RegisterAnimatedBlock(new LumaAnimatedBlockSpec
{
    BlockId = "my_mod.sample_rotor",
    DisplayName = "Sample Rotor",
    Description = "Small animated block.",
    Model = new LumaAnimatedModelSpec
    {
        Name = "Sample Rotor",
        AssetRoot = Path.Combine(context.ModsDirectory, "my_mod", "assets", "models"),
        ModelPath = "sample_rotor.bbmodel.json",
        TexturePath = "sample_rotor.png",
        AnimationGraph = new LumaAnimationGraphSpec
        {
            InitialState = "spinning",
            States =
            [
                new LumaAnimationStateSpec
                {
                    Name = "spinning",
                    Animation = "spin"
                }
            ]
        }
    }
});
```

On Allumeria, `ILumaContentService` queues registration until native block,
item, block-entity, and crafting registries are ready. The block entity renders
through `ILumaAnimatedModel`, so the mod still does not reference native
Allumeria rendering types.

Each placed animated block gets its own animation controller. The Allumeria
adapter shares parsed model assets and textures between instances, but keeps
animator state per block entity.
