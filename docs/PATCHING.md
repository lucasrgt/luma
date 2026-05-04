# Patching Rules

Patch copies, not originals.

Recommended layout:

```text
Allumeria/
  original/
    PocketBlocks.dll
  luma/
    PocketBlocks.dll
    Luma.Runtime.dll
    Luma.Abstractions.dll
    mods/
```

Each patch manifest should be tied to a single game DLL MVID. The patcher can
run with `expectedModuleMvid` unset during research, but release manifests
should always set it.

## Hook Modes

`insert`:

- `Start`: inject before the first instruction
- `BeforeReturn`: inject before every `ret`

`argumentMode`:

- `None`: call a zero-argument runtime entrypoint
- `This`: pass the target method instance as `object`
- `FirstArgument`: pass the first target argument as `object`

Keep early hooks simple. Use `None` for bootstrap until the exact game object
lifetime is understood.
