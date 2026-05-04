# Architecture

Luma is split around ownership boundaries. The main rule is simple: normal mods
reference the public SDK, not the Allumeria adapter or old research tooling.

## Stable API

`Luma.Abstractions` is the only project normal mods should reference. It
contains lifecycle interfaces, logging, assets, model specs, content services,
and a small service registry. It must stay small and stable.

## Runtime

`Luma.Runtime` is the internal host loaded by the game adapter. It owns:

- mod discovery from `mods/*.dll`
- lifecycle dispatch
- logging to `luma.log`
- shared service registration

The runtime does not know about any third-party modloader or Allumeria-specific
rendering class.

## Allumeria Adapter

`Luma.AllumeriaLoader` is the Allumeria-specific bridge. It is installed as
`Loader.dll`, registers Luma services, creates Allumeria blocks and block
entities, and adapts Luma model specs to native Allumeria `BBModel` rendering.

This adapter may reference Allumeria internals. Public sample mods should not.

## Model Library

`Luma.ModelLib` holds the portable model and animation work:

- OBJ and Blockbench parsing
- animation clips, easing, pivots, and chunk manifests
- renderer-facing mesh/model data
- validation helpers shared by tools and adapters

## Tools

`tools/Luma.AssetPipeline` converts and validates model assets.
`tools/Luma.DevHost` runs a local smoke test without launching Allumeria.

`tools/experimental/Luma.Patcher` is archived Mono.Cecil research. It is useful
for investigation, but it is not part of the current modder workflow.

## Samples and Showcase

`samples/Luma.SampleMod` is the clean template for new mods.
`showcase/Luma.MegaCrusherShowcase` is a larger validation target for chunking,
animation, and spatial lighting.

Optional hosts such as Ignitron or ModMeria can be added later as adapters. They
should reference Luma APIs, not the other way around.
