# Mega Crusher Showcase

Large visual showcase for Luma's Allumeria adapter.

This is not the modder template. Use `samples/Luma.SampleMod` for the smallest
public-API example. The showcase exists to keep the big Mega Crusher assets,
chunked model output, and visual lighting checks out of the loader project.

Install it with:

```powershell
.\scripts\install-allumeria-loader.ps1 -IncludeShowcaseMod
```

The showcase registers `Mega Crusher` and `Luma Rotor` through
`ILumaContentService`, the same public content API used by the clean sample.
