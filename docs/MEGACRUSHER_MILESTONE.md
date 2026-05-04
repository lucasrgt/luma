# Mega Crusher Showcase

The Retronism Mega Crusher remains the first large validation target:

```text
showcase/Luma.MegaCrusherShowcase/assets/models/MegaCrusher.obj
showcase/Luma.MegaCrusherShowcase/assets/models/MegaCrusher.anim.json
showcase/Luma.MegaCrusherShowcase/assets/models/retronism_megacrusher.png
```

It is intentionally large for Allumeria. That is useful: it forces the stack to
prove chunking, pivots, animation, texture handling, and spatial lighting.

## Success Criteria

1. `Luma.AllumeriaLoader` enters a real Allumeria build through the external
   loader path.
2. `Luma.MegaCrusherShowcase` loads from `mods/`.
3. The Mega Crusher texture and exported BBModel assets are loaded.
4. The model draws in an Allumeria render pass.
5. The `working` animation rotates:
   - `turbine_l`
   - `turbine_r`
   - `shredder_L`
   - `shredder_R`
6. The game remains responsive enough to inspect the result.
7. Chunked rendering samples different local light around the large model.

## Current Result

The showcase now runs through the public content API instead of special loader
debug classes. It registers the chunked Mega Crusher and a smaller rotor block
from `showcase/Luma.MegaCrusherShowcase`.

## Next Work

The next meaningful milestone is no longer asset ingestion or first rendering.
It is Animation Runtime: controllers, transitions, events, and public bone
manipulation APIs.
