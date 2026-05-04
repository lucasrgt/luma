# Luma

Luma is an experimental, loader-agnostic modding stack for Allumeria.

## Projects

```text
src/
  Luma.Abstractions  Public API consumed by mods.
  Luma.Runtime       Runtime loaded by the patched game.
  Luma.Patcher       Mono.Cecil CLI for inspection and IL hook injection.
  Luma.ModelLib      Future model, animation, and renderer library.
  Luma.AllumeriaLoader
                     External-loader bootstrap installed as Loader.dll.
  Luma.DevHost       Local runtime harness without the game.

samples/
  Luma.SampleMod     Clean animated-block sample using the public API.
  Luma.MegaCrusherProbe
                     Loads the MegaCrusher OBJ, texture, and animation.

patches/
  example-manifest.json
```

## Build

```powershell
dotnet build
```

## Install the Allumeria Loader

```powershell
.\scripts\install-allumeria-loader.ps1
```

Restart Allumeria after installing so the game loads the external loader.

To install the clean sample mod as well:

```powershell
.\scripts\install-allumeria-loader.ps1 -IncludeSampleMod
```

The sample registers a craftable `Sample Rotor` block through
`ILumaContentService`. Its recipe is `1x any planks -> 1x Sample Rotor`.

The old Mega Crusher/Luma Rotor preview recipes are now opt-in debug content.
Set `LUMA_PREVIEW_CONTENT=1` before launching Allumeria if you want those
temporary recipes.

## Allumeria Model Export

Runtime model loading/rendering contracts are documented in
[`docs/MODEL_API.md`](docs/MODEL_API.md).

Allumeria's entity shader currently exposes `boneMatrices[20]`, so Luma treats
20 bones as a hard compatibility limit for exported entity `.bbmodel` files.
The asset pipeline enforces that limit during export and validation.

Use `--partial-rig` for large mechanical models. It keeps only the top animated
bones and parents each moving subtree's parts directly under those bones, which
avoids silent shader failures when source art contains many helper groups.

Use `--chunks` when a model really needs more than 20 bones, or when a large
model needs more local lighting. It writes a `.chunks.json` manifest plus
multiple `.chunk_XX.bbmodel.json` files. Each chunk stays under Allumeria's
20-bone shader limit and the runtime renders all chunks at the same block
position with the same animation.

Use `--light-chunks N` with `--chunks` to force spatial chunks even when the
model already fits the bone limit. This keeps native Allumeria rendering while
letting the front/back/sides sample different world light values.

Preferred conversion command:

```powershell
.\tools\Luma.AssetPipeline\bin\Debug\net10.0\Luma.AssetPipeline.exe model convert `
  .\samples\Luma.MegaCrusherProbe\assets\models\MegaCrusher.obj `
  --target allumeria `
  --animation .\samples\Luma.MegaCrusherProbe\assets\models\MegaCrusher.anim.json `
  --texture .\samples\Luma.MegaCrusherProbe\assets\models\retronism_megacrusher.png `
  --output .\samples\Luma.MegaCrusherProbe\assets\models\mega_crusher.chunks.json `
  --partial-rig `
  --chunks `
  --light-chunks 9 `
  --report `
  --validate `
  --game-dir "C:\Program Files (x86)\Steam\steamapps\common\Allumeria Demo"
```

`model convert` prints an export report with groups, polygons, triangles,
texture size, animation count, chunks, bones/chunk, and parts/chunk. Add
`--report` to also write that report as JSON next to the output model, or pass
`--report path\to\report.json` for an explicit path.

Legacy positional command:

```powershell
.\tools\Luma.AssetPipeline\bin\Debug\net10.0\Luma.AssetPipeline.exe bbmodel `
  .\samples\Luma.MegaCrusherProbe\assets\models\MegaCrusher.obj `
  .\samples\Luma.MegaCrusherProbe\assets\models\MegaCrusher.anim.json `
  .\samples\Luma.MegaCrusherProbe\assets\models\retronism_megacrusher.png `
  .\samples\Luma.MegaCrusherProbe\assets\models\mega_crusher.bbmodel.json `
  --partial-rig
```

Legacy chunk export:

```powershell
.\tools\Luma.AssetPipeline\bin\Debug\net10.0\Luma.AssetPipeline.exe bbmodel `
  .\samples\Luma.MegaCrusherProbe\assets\models\MegaCrusher.obj `
  .\samples\Luma.MegaCrusherProbe\assets\models\MegaCrusher.anim.json `
  .\samples\Luma.MegaCrusherProbe\assets\models\retronism_megacrusher.png `
  .\samples\Luma.MegaCrusherProbe\assets\models\mega_crusher.chunks.json `
  --chunks --partial-rig --light-chunks 9
```

Validation checks Luma-side asset safety first, then parses the file with
Allumeria's native `BBModel` loader. It currently catches UVs outside the
declared texture resolution, mesh origins that drift away from their vertex
bounds, suspicious animation pivots, and bone counts above the shader limit:

```powershell
.\tools\Luma.AssetPipeline\bin\Debug\net10.0\Luma.AssetPipeline.exe validate-allumeria-bbmodel `
  "C:\Program Files (x86)\Steam\steamapps\common\Allumeria Demo" `
  .\samples\Luma.MegaCrusherProbe\assets\models\mega_crusher.bbmodel.json
```

The same validation command accepts a chunk manifest and checks every chunk:

```powershell
.\tools\Luma.AssetPipeline\bin\Debug\net10.0\Luma.AssetPipeline.exe validate-allumeria-bbmodel `
  "C:\Program Files (x86)\Steam\steamapps\common\Allumeria Demo" `
  .\samples\Luma.MegaCrusherProbe\assets\models\mega_crusher.chunks.json
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
only for Luma-rendered models. At render time the adapter samples
native RGB/S world light near the model bounds and the shader blends those
samples per vertex, so large animated models can react to brighter/darker
surroundings instead of being painted by one averaged light value.

Strongly colored block light is still balanced so one lamp does not tint the
entire model too aggressively. Chunked models continue to help because each
chunk gets its own set of shader samples.

Lighting diagnostics can be enabled before launching the game:

```powershell
$env:LUMA_LIGHT_DEBUG = "1"
$env:LUMA_LIGHT_DEBUG_FRAMES = "24"
$env:LUMA_LIGHT_TINT_STRENGTH = "0.45"
```

Set `LUMA_LIGHT_BALANCE=off` to compare against raw Allumeria light values.

For the in-game Mega Crusher smoke test, the bake script can install either
the compact partial rig or the full rig chunked version:

```powershell
.\scripts\bake-megacrusher-preview.ps1
.\scripts\bake-megacrusher-preview.ps1 -Chunked
```

The current lighting comparison notes and screenshots are tracked in
[`docs/LIGHTING_VALIDATION.md`](docs/LIGHTING_VALIDATION.md).

## Run the Mega Crusher Probe

```powershell
.\scripts\run-megacrusher-probe.ps1
```

This builds the solution, stages `Luma.MegaCrusherProbe` into the DevHost
`mods/` directory, starts the runtime harness, and tails `luma.log`.

## Inspect a Game DLL

```powershell
dotnet run --project src/Luma.Patcher -- inspect C:\Path\To\PocketBlocks.dll
```

This prints the assembly version, module MVID, and candidate methods whose
names look useful for bootstrap, tick, render, or content registration hooks.

## Patch a Copy

```powershell
dotnet run --project src/Luma.Patcher -- patch `
  C:\Path\To\PocketBlocks.dll `
  patches\example-manifest.json `
  C:\Path\To\Patched\PocketBlocks.dll
```

The example manifest is intentionally a placeholder until we inspect a real
Allumeria build. The patcher refuses unknown MVIDs once `expectedModuleMvid`
is set, which is the safety gate we want before distributing patches.

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

At runtime, Luma creates a `mods` directory next to the game executable,
loads each DLL, discovers `IAllumeriaMod` implementations, and calls:

```text
Init
Tick
Render
Shutdown
```

## Current Status

This repo is a scaffold plus a working patcher foundation and a first
external-loader bootstrap. The current north star is rendering the animated
Mega Crusher model in Allumeria, even if it looks uncanny there. The local
probe already validates asset ingestion; next we need the real Allumeria
content and render hooks.
