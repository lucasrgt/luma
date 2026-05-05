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
                        Name = "startup",
                        Animation = "startup",
                        Loop = false,
                        OnCompleteState = "working",
                        OnCompleteTransitionSeconds = 0.15f
                    },
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
                                Payload = "mechanical-impact",
                                Effects =
                                [
                                    new LumaAnimationEffectSpec
                                    {
                                        Kind = "particle",
                                        Id = "smoke",
                                        Offset = new LumaVector3(0.5f, 1.3f, 0.5f),
                                        Strength = 0.65f
                                    }
                                ]
                            }
                        ]
                    },
                    new LumaAnimationStateSpec
                    {
                        Name = "idle",
                        Animation = "working",
                        AutoPlay = false,
                        BoneOverrides =
                        [
                            new LumaBoneOverrideSpec
                            {
                                Bone = "turbine_l",
                                RotationDegrees = new LumaVector3(0f, 0f, 0f)
                            }
                        ]
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
- `LumaAnimationTransitionSpec.TransitionSeconds` blends the current bone pose
  into the destination state over a short duration.
- `LumaAnimationStateSpec.OnCompleteState` can move from a non-looping state
  into another declared state when the current animation ends.
- `ILumaAnimatedModel.Animation.SetState(...)` switches to a declared state.
- `ILumaAnimatedModel.Animation.Trigger(...)` follows a declared transition.
- `ILumaAnimatedModel.Animation.DrainEvents()` returns keyframe events emitted
  since the last drain.
- `LumaAnimationEventSpec.Effects` attaches declarative effect requests to a
  keyframe event without requiring mods to reference native game effect types.
- `ILumaAnimatedModel.Animation.SetBoneOverride(...)` can apply runtime bone
  overrides by public bone name.
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
model spec, plus optional declarative recipes for Allumeria crafting stations.

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
```

On Allumeria, `ILumaContentService` queues registration until native block,
item, block-entity, and crafting registries are ready. The block entity renders
through `ILumaAnimatedModel`, so the mod still does not reference native
Allumeria rendering types.

Each placed animated block gets its own animation controller. The Allumeria
adapter shares parsed model assets and textures between instances, but keeps
animator state per block entity.

`LumaRecipeSpec` keeps crafting data declarative:

- `Station` maps to an Allumeria crafting station id, such as `inventory` or
  `work_bench`.
- `OutputCount` controls the number of block items produced.
- Ingredients can use either `ItemId` for a concrete item/block id, or `AliasId`
  for a native recipe alias such as `any_planks`.

`AddWoodRecipe` and `RecipeOutputCount` remain as compatibility defaults for
old sample-sized specs. Prefer `Recipes` for new mods.

Mods can address an animated block instance by position through the public
content service:

```csharp
content.TriggerAnimationAt(x, y, z, "work");

ILumaAnimationController? animation = content.GetAnimationControllerAt(x, y, z);
foreach (LumaAnimationEvent evt in animation?.DrainEvents() ?? [])
{
    context.Logger.Info($"Animation event: {evt.State}/{evt.Name}");
    foreach (LumaAnimationEffectSpec effect in evt.Effects)
    {
        context.Logger.Info($"Effect request: {effect.Kind}/{effect.Id}");
    }
}

animation?.SetBoneOverride("turbine_l", new LumaBoneOverrideSpec
{
    Bone = "turbine_l",
    RotationDegrees = new LumaVector3(0f, 90f, 0f)
});
```

## Animation Graph Patterns

Machine pattern:

```text
idle --work--> startup --complete--> working --pause--> idle
working emits keyframe events such as crush-impact, smoke-puff, consume-input.
```

Use this for blocks with processing state, energy, inventories, or timed work.
Keep one-shot states such as `startup` and `shutdown` non-looping and point them
at the next state with `OnCompleteState`. Attach declarative effects such as
`particle`, `sound`, `log`, or future gameplay actions to keyframe events so
the event timing stays in the animation data.

Entity pattern:

```text
idle --move--> walk
walk --stop--> idle
idle/walk --hurt--> hurt --complete--> idle
```

Use short `TransitionSeconds` values for locomotion and non-looping hit/reaction
states. Drain events from the controller when animation frames should drive
gameplay effects.

Decorative pattern:

```text
spinning
```

Use one looping state with no transitions for simple ambient blocks. Add
declarative `BoneOverrides` when a model needs a fixed pose per state but no
custom code.
