# Luma

Luma is an experimental, loader-agnostic modding stack for Allumeria. The repo
is organized so modders can start from the public SDK and sample without touching
the Allumeria adapter internals or old patching experiments.

## Projects

```text
src/
  Luma.Abstractions       Public SDK consumed by mods.
  Luma.ModelLib           Model/export core and portable animation data.
  Luma.Runtime            Internal mod discovery and lifecycle host.
  Luma.AllumeriaLoader    Allumeria external-loader adapter installed as Loader.dll.

tools/
  Luma.AssetPipeline      OBJ/animation/texture to Allumeria BBModel converter.
  Luma.DevHost            Local runtime smoke harness without launching the game.
  experimental/Luma.Patcher
                          Archived Mono.Cecil patching research.

samples/
  Luma.SampleMod          Clean template mod using only the public SDK.

showcase/
  Luma.MegaCrusherShowcase
                          Large visual stress test for chunking, animation, and lighting.
```

## Build

```powershell
dotnet build Luma.slnx
```

## Install the Allumeria Loader

```powershell
.\scripts\install-allumeria-loader.ps1
```

Restart Allumeria after installing so the game loads the external loader.

To install the clean sample mod:

```powershell
.\scripts\install-allumeria-loader.ps1 -IncludeSampleMod
```

The sample registers a craftable `Sample Rotor` block through
`ILumaContentService`. Its recipe is `1x any planks -> 1x Sample Rotor`.
Its animation is declared with `LumaAnimatedModelSpec.AnimationGraph`, the same
state/transition style used by the larger showcase.

To install the heavier Mega Crusher showcase as well:

```powershell
.\scripts\install-allumeria-loader.ps1 -IncludeSampleMod -IncludeShowcaseMod
```

The showcase is intentionally not the template. It exists to validate large
models, chunk manifests, animation pivots, and spatial lighting.

## Allumeria Model Export

Runtime model loading/rendering contracts are documented in
[`docs/MODEL_API.md`](docs/MODEL_API.md).

Allumeria's entity shader currently exposes `boneMatrices[20]`, so Luma treats
20 bones as a hard compatibility limit for exported entity `.bbmodel` files.
The asset pipeline enforces that limit during export and validation.

Use `--partial-rig` for large mechanical models. It keeps only the top animated
bones and parents each moving subtree's parts directly under those bones.

Use `--chunks` when a model needs more than 20 bones, or when a large model
needs more local lighting. It writes a `.chunks.json` manifest plus multiple
`.chunk_XX.bbmodel.json` files. Each chunk stays under Allumeria's 20-bone
shader limit and the runtime renders all chunks at the same block position.

Use `--light-chunks N` with `--chunks` to force spatial chunks even when the
model already fits the bone limit. This keeps native Allumeria rendering while
letting the front/back/sides sample different world light values.

Preferred conversion command:

```powershell
.\tools\Luma.AssetPipeline\bin\Debug\net10.0\Luma.AssetPipeline.exe model convert `
  .\showcase\Luma.MegaCrusherShowcase\assets\models\MegaCrusher.obj `
  --target allumeria `
  --animation .\showcase\Luma.MegaCrusherShowcase\assets\models\MegaCrusher.anim.json `
  --texture .\showcase\Luma.MegaCrusherShowcase\assets\models\retronism_megacrusher.png `
  --output .\showcase\Luma.MegaCrusherShowcase\assets\models\mega_crusher.chunks.json `
  --partial-rig `
  --chunks `
  --light-chunks 9 `
  --report `
  --validate `
  --game-dir "C:\Program Files (x86)\Steam\steamapps\common\Allumeria Demo"
```

Validation checks Luma-side asset safety first, then parses the file with
Allumeria's native `BBModel` loader:

```powershell
.\tools\Luma.AssetPipeline\bin\Debug\net10.0\Luma.AssetPipeline.exe validate-allumeria-bbmodel `
  "C:\Program Files (x86)\Steam\steamapps\common\Allumeria Demo" `
  .\showcase\Luma.MegaCrusherShowcase\assets\models\mega_crusher.chunks.json
```

Small converter fixtures live under `tests/fixtures/asset-pipeline`. Run the
fixture smoke test with:

```powershell
.\scripts\test-asset-pipeline-fixtures.ps1
```

## In-Game Model Lighting

The Allumeria adapter uses a separate Luma entity shader that accepts spatial
light samples around each rendered model chunk. Native Allumeria shader files are
left untouched; Luma stages its shader under `mods/luma/shaders/` and uses it
only for Luma-rendered models.

At render time the adapter samples native RGB/S world light near the model
bounds and the shader blends those samples per vertex. Large animated models can
react to brighter and darker surroundings instead of being painted by one
averaged light value.

Lighting diagnostics can be enabled before launching the game:

```powershell
$env:LUMA_LIGHT_DEBUG = "1"
$env:LUMA_LIGHT_DEBUG_FRAMES = "24"
$env:LUMA_LIGHT_TINT_STRENGTH = "0.45"
```

Set `LUMA_LIGHT_BALANCE=off` to compare against raw Allumeria light values.

For the in-game Mega Crusher smoke test:

```powershell
.\scripts\bake-megacrusher-showcase.ps1 -Chunked
.\scripts\install-allumeria-loader.ps1 -IncludeShowcaseMod
```

The current lighting comparison notes and screenshots are tracked in
[`docs/LIGHTING_VALIDATION.md`](docs/LIGHTING_VALIDATION.md).

## DevHost Smoke Test

```powershell
.\scripts\run-devhost-smoke.ps1
```

This builds the solution, stages `Luma.SampleMod` and
`Luma.MegaCrusherShowcase` into the DevHost `mods/` directory, starts the
runtime harness, and tails `luma.log`.

## Runtime Contract

Mods implement `IAllumeriaMod`:

```csharp
[LumaMod("example.mod", "Example Mod", "0.1.0")]
public sealed class ExampleMod : IAllumeriaMod
{
    public void Init(IModContext context)
    {
        context.Logger.Info("Hello from a Luma mod.");
    }
}
```

At runtime, Luma creates a `mods` directory next to the game executable, loads
each DLL, discovers `IAllumeriaMod` implementations, and calls:

```text
Init
Tick
Render
Shutdown
```

## Current Status

The public SDK, external-loader adapter, asset pipeline, sample mod, showcase,
chunked model loading, and Luma-only lighting shader are working. The patcher is
kept under `tools/experimental` as historical research and is not part of the
normal modder path.
