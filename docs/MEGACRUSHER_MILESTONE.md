# Mega Crusher Milestone

The first real validation target is the Retronism Mega Crusher machine:

```text
MegaCrusher.obj
MegaCrusher.anim.json
retronism_megacrusher.png
```

It is intentionally uncanny for Allumeria. That is fine. The point is to make
the whole stack prove itself against a large animated machine we already know.

## Success Criteria

1. Luma patches or otherwise enters a real Allumeria build.
2. `Luma.MegaCrusherProbe` loads from `mods/`.
3. The Mega Crusher texture and OBJ are loaded.
4. The model draws in an Allumeria render pass.
5. The `working` animation rotates:
   - `turbine_l`
   - `turbine_r`
   - `shredder_L`
   - `shredder_R`
6. The game remains responsive enough to inspect the result.

## Current Local Probe Result

The DevHost validation already reaches asset ingestion:

```text
Mesh: 1216 vertices, 3648 uvs, 912 normals, 1824 triangles, 153 groups.
Animation bundle: format 1.0, 38 pivots, 174 child links, 1 clips.
Working clip: length 2s, loop=True, animated bones=4, keyframes=28.
```

## Next Work

The next meaningful unknown is not asset parsing anymore. It is the Allumeria
render hook: where to inject, what OpenGL context state is active, and how to
draw without trampling the game's renderer.
