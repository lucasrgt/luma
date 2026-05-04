# Luma Model API

The first runtime model API lives in `Luma.Abstractions.Models`. It is deliberately
small: mods ask for an `ILumaModelService`, load an animated model, choose an
animation, and render it at a block position.

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
            InitialAnimation = "working"
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
- `ILumaAnimatedModel.SetAnimation(...)` changes and starts an animation.
- `ILumaAnimatedModel.PauseAnimation()` pauses the current animation.
- `ILumaAnimatedModel.RestartAnimation()` restarts the current animation.
- `ILumaAnimatedModel.RenderBlock(...)` renders at a block-space position.

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
        InitialAnimation = "spin"
    }
});
```

On Allumeria, `ILumaContentService` queues registration until native block,
item, block-entity, and crafting registries are ready. The block entity renders
through `ILumaAnimatedModel`, so the mod still does not reference native
Allumeria rendering types.
