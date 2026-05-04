# Milestones

## M0: Repo Bootstrap

- Buildable .NET solution
- Public mod API
- Runtime loader
- Mono.Cecil patcher
- Sample mod

## M1: Mega Crusher Asset Probe

- Load the Retronism Mega Crusher OBJ
- Preserve OBJ object/group names for animated bones
- Load `MegaCrusher.anim.json`
- Confirm the `working` clip, pivots, child map, and animated bone count
- Run through `Luma.DevHost`

Status: done locally. DevHost reports 1216 vertices, 1824 triangles, 153
groups, 38 pivots, 174 child links, and the `working` clip with 4 animated
bones.

## M2: Real Allumeria Inspection

- Run `inspect` against the current Allumeria game DLL
- Record assembly name, version, and MVID
- Identify init, tick, render, and content registry candidates
- Create the first versioned patch manifest

## M3: Bootstrap Patch

- Inject `LumaEntrypoints.OnGameInit`
- Launch patched copy
- Confirm `luma.log` is written
- Confirm `Luma.MegaCrusherProbe` loads from `mods/`

## M4: Tick and Render Hooks

- Inject tick hook
- Inject render hook
- Pass game or renderer instance when safe
- Validate no obvious frame-time overhead

## M5: First In-Game Mega Crusher Draw

- Bind texture
- Upload OBJ mesh into OpenGL buffers
- Draw the static Mega Crusher mesh from a render hook
- Pick a deliberately obvious placement/scale in front of the camera

## M6: Animated Mega Crusher Draw

- Sample the `working` clip
- Rotate `turbine_l`, `turbine_r`, `shredder_L`, and `shredder_R`
- Preserve static group rendering
- Validate it runs in-game without visible hitching

## M7: Content Hook

- Find item/block/model registration flow
- Add Luma-side content registration API
- Create a sample custom item or block

## M8: Blockbench Pipeline

- Convert `.bbmodel` to Luma animation data
- Preserve pivots and child map
- Add a sample animated block/entity
