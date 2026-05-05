# Luma Sample Mod

Small public-API sample for an animated Allumeria block.

What it demonstrates:

- loading an animated `.bbmodel.json` through `LumaAnimatedModelSpec`;
- declaring animation state with `AnimationGraph`;
- registering a block through `ILumaContentService`;
- adding the simple sample recipe `1x any planks -> 1x Sample Rotor`;
- keeping assets inside `mods/luma.sample/assets/models` at runtime.

Install it with:

```powershell
.\scripts\install-allumeria-loader.ps1 -IncludeSampleMod
```

Restart Allumeria after installing. The sample block appears as `Sample Rotor`
and starts in the declared `spinning` state, which plays the `spin` animation
from `assets/models/sample_rotor.bbmodel.json`.
