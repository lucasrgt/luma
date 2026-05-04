# Lighting Validation

Last checked: 2026-05-04.

The Luma model shader is validated against native Allumeria lighting by using
the same world light source that native blocks use (`GetLightIfExistsRaw`) and
feeding several spatial samples into the Luma-only entity shader. Native
Allumeria shader files remain untouched.

## Current Baseline

Artifacts in `run/`:

- `allumeria-screen.png`: daytime sunlight comparison on the chunked Mega
  Crusher next to native terrain.
- `allumeria-separated-shader-screen.png`: night/block-light comparison with a
  colored lamp and native blocks visible in the same frame.

Runtime log evidence from the same run:

- native `res/shaders/entity/shader.vert` was restored from the old backup;
- Luma staged its own shader at `mods/luma/shaders/entity.vert`;
- Mega Crusher loaded in chunked mode with 9 spatial light samples;
- Luma Rotor loaded in single-model mode with one center light sample.

## Manual Check Matrix

Use a chunked Mega Crusher, a Luma Rotor, native solid blocks, torches, and
colored lamps.

| Scene | Expected result |
| --- | --- |
| Day/sun | Model brightness should match adjacent native blocks without one flat tint across the full mesh. |
| Night | Model should darken with the world while retaining local color from nearby block lights. |
| Cave/shadow | Model should follow torch and lamp falloff instead of staying globally bright. |
| Colored lamps | Color should be visible locally, but not flood the whole model with one saturated color. |

## Debug Run

```powershell
$env:LUMA_LIGHT_DEBUG = "1"
$env:LUMA_LIGHT_DEBUG_FRAMES = "24"
$env:LUMA_LIGHT_TINT_STRENGTH = "0.45"
.\scripts\bake-megacrusher-preview.ps1 -Chunked
.\scripts\install-allumeria-loader.ps1
```

Set `LUMA_PREVIEW_CONTENT=1` before launching Allumeria if you want the old
Mega Crusher, Luma Rotor, and colored-light debug recipes. The clean sample mod
does not need preview content.
