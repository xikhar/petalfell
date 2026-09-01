# Retired implementation archive

This directory preserves code removed from the compiled production source. Files
use non-`.cs` extensions deliberately: Godot and the C# SDK must not discover or
compile them.

The archive is historical evidence, not a second implementation to maintain.
Current architecture and commands live in the root documentation. When another
system is retired, preserve its last coherent implementation here, record why it
left production, and remove every runtime switch that could still activate it.

## `legacy-world/`

The retired 3,456-block circular fixture and its private assembly layer:

- `Main.cs.txt` — complete former scene assembly and `--legacy-world` path;
- `WorldMap.cs.txt` — fixture-only local map;
- `Sanctum.cs.txt` and `Massif.cs.txt` — seed-chosen summit monument.

They were archived on 2026-09-02 because normal play now exclusively uses the
12,288 × 9,216 authored atlas through bounded production windows. The terrain,
water, voxel, render, vegetation, player and camera primitives used by the
production path remain in `src/`; they are shared foundations, not retired code.

The archived `Main.cs.txt` also records the former fixture's gameplay assembly.
That assembly and its pet, fauna, inventory/loadout and related behavior are not
production specifications. Their names remain in future product scope, but their
production designs will be made afresh.
