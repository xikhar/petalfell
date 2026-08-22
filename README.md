# Petalfell (Godot)

A calm, atmospheric exploration adventure in a soft pastel block world. This is
the Godot rebuild of the Three.js prototype in `~/Projects/pastel-game`.

Project documentation is deliberately split between goals, engineering decisions,
and the live implementation state:

| | |
|---|---|
| [`plan.md`](plan.md) | Product and creative plan. It describes the game Petalfell is intended to become. |
| [`ARCHITECTURE.md`](ARCHITECTURE.md) | Engineering decisions — camera, ink, world model, collision, language. |
| [`CURRENT_STATE.md`](CURRENT_STATE.md) | Factual snapshot of what is implemented, partial, and absent today. |
| `~/Projects/pastel-game/WORLDGEN.md` | The generator's design and its documented failure modes. Required reading before touching `src/World`. |

## Running

```bash
godot-mono --path .
```

Controls: **WASD** move (camera-relative), **Space** jump / swim up,
**left click** travel to a point, **Q/E** orbit in 45° steps, **wheel** zoom,
**tilde/backtick** developer settings.

## The capture rig

The reference project's look got as far as it did because every change was
judged through a fixed set of screenshots. Same loop here:

```bash
godot-mono --path . -- --shots /tmp/shots
```

```bash
godot-mono --path . -- --shots /tmp/shots --only hero,close
```

Writes `hero, wide, close, cliffs, water, canopy` plus `map.png`, a top-down
view of the finished heightfield. Fixed seed, fixed camera values, fixed frame
counts — it must stay deterministic or it is not a review tool.

`map.png` is the fastest way to tell "the generator is flat" from "the camera
happens to be standing on a shelf".

## Building

C# is the primary language (measured 21–27x faster than GDScript on the mesher
and generator loops — see `ARCHITECTURE.md` §6.1).

```bash
dotnet build
```

On NixOS the engine's bundled `Godot.NET.Sdk` lives in the nix store under a
content hash that changes on every system rebuild. If the build fails with
`MSB4236: The SDK 'Godot.NET.Sdk/4.7.1' could not be found`:

```bash
bash tools/setup-nuget.sh
```

## Layout

```
src/Core/      Rng + value noise, palette and block registry, voxel grid,
               chunk mesher and the ink edge graph, ink for non-voxel meshes
src/World/     planner, terrain shaping, terrain, vegetation, chunk streaming
src/Render/    camera rig, lighting and atmosphere
src/Player/    controller, traveller, dog, navigation and click-to-move
src/Tools/     capture rig
shaders/       voxel, ink, water, sky, grade, character, pulse
```

## Two things that will bite you

**The colour pipeline.** Every authored colour is sRGB and is converted to
linear exactly once, at definition (`Palette.C`, `Character.Tone`). Skip it and
the pale end of the palette is decoded as if it were already linear, lifts
through the ACES shoulder, and sage grass, cream sand and blossom all render as
white. The pale/dark ink classification is judged on the *sRGB* value, because
the 0.61 luminance threshold was tuned against the hex.

**The noise hash must be unsigned.** The reference uses JavaScript's `>>>`.
Porting that to C#'s `>>` on an `int` sign-extends, which clears the top bit of
every negative value, and the field never reaches the upper half of its range —
mean 0.25 instead of 0.5. Every threshold downstream then sits above everything
the field can produce, and the world comes out with one grass tone, no biome
variety and almost no trees.
