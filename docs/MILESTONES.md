# Milestones

## M0: Repo Bootstrap

- Buildable .NET solution
- Public mod API
- Runtime host
- External-loader adapter foundation
- Sample mod

Status: done.

## M1: Mega Crusher Asset Probe

- Load the Retronism Mega Crusher OBJ
- Preserve OBJ object/group names for animated bones
- Load `MegaCrusher.anim.json`
- Confirm the `working` clip, pivots, child map, and animated bone count
- Run through `Luma.DevHost`

Status: done locally. DevHost reported 1216 vertices, 1824 triangles, 153
groups, 38 pivots, 174 child links, and the `working` clip with 4 animated
bones.

## M2: External Loader Bootstrap

- Install `Luma.AllumeriaLoader` as `Loader.dll`
- Confirm `luma-loader.log` is written
- Register `Luma.Runtime`
- Load mods from `mods/`

Status: done.

## M3: Tick and Render Bridge

- Subscribe to Allumeria frame events
- Dispatch runtime tick/render lifecycle
- Validate no obvious frame-time overhead

Status: done.

## M4: First In-Game Animated Block

- Register a custom item/block through Allumeria
- Create a block entity renderer
- Load and render a `.bbmodel`
- Animate a craftable sample block

Status: done.

## M5: Blockbench Pipeline

- Convert `.obj` plus animation JSON to Allumeria `.bbmodel`
- Preserve pivots and child relationships
- Validate UVs, pivots, animation data, and native parser compatibility
- Emit conversion reports

Status: done.

## M6: Large Model Chunking

- Export chunk manifests
- Keep each chunk under Allumeria's 20-bone shader limit
- Support partial rigs
- Render all chunks at the same block position

Status: done.

## M7: Spatial Lighting Shader

- Stage a Luma-only shader without modifying Allumeria defaults
- Sample native world light around model bounds
- Blend light per vertex
- Keep colored lights local instead of flooding the whole model

Status: done.

## M8: Clean Modder Path

- Keep `samples/Luma.SampleMod` as the small public-API template
- Move Mega Crusher to `showcase/Luma.MegaCrusherShowcase`
- Move patching research to `tools/experimental`
- Keep the main solution focused on SDK, adapter, runtime, tools, sample, and showcase

Status: done.

## Next Target: Animation Runtime

- Public animation controllers
- Transitions and blend states
- Triggerable animation events
- Keyframe callbacks
- Public bone manipulation API
