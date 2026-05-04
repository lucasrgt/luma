# Architecture

Luma is split around ownership boundaries.

## Stable API

`Luma.Abstractions` is the only project normal mods should reference. It
contains lifecycle interfaces, logging, assets, and a small service registry.
It must stay small and stable.

## Runtime

`Luma.Runtime` is loaded by the patched game assembly. It owns:

- mod discovery from `mods/*.dll`
- lifecycle dispatch
- logging to `luma.log`
- shared service registration

The runtime does not know about any third-party modloader.

## Patcher

`Luma.Patcher` uses Mono.Cecil to inject direct IL calls into the game
assembly. Patches are manifest-driven so each Allumeria version can have a
small reviewed manifest instead of hard-coded type names.

## Model Library

`Luma.ModelLib` will hold the new model and animation system. It should remain
independent from Allumeria-specific classes for as long as possible:

- OBJ and Blockbench parsing
- animation clips, easing, state machines
- renderer-facing mesh data
- later: OpenTK/OpenGL backend

## Optional Hosts

Ignitron or ModMeria support can be added later as hosts/adapters. They should
reference Luma APIs, not the other way around.
