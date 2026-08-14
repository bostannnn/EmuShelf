# Integrating the remaining supplied models

Measurements and blockers for the four models in `models/` that are not yet shells. Written so the
next pass is a short by-eye session rather than a re-derivation. Inventory and licensing are in
[`model-sourcing-inventory-2026-08-14.md`](model-sourcing-inventory-2026-08-14.md).

## The tool that unblocks this

```
dotnet run --project tools/EmuShelf.Rendering.Preview -- --dump-atlas <model.glb> --out <dir>
```

Writes the base-colour atlas as PNG, plus a copy with the front face's UV triangles in green and the
back's in red. It needs no GPU and returns before any GL setup, so it runs anywhere.

This exists because locating the printed game label is the one step that measurement could not
settle. A variance sweep of the Sonic 2 atlas nominated two candidates and the stronger one was the
contact pins, not the label. Seeing it takes a second. Dumps for all four are in
`artifacts/model-atlases/`.

## Why these are not wired yet

Two blockers, and neither is about effort.

**Every one of these carries a specific game's artwork** — Sonic 2, Battletoads, Pokémon, Mario 64.
The modeller's CC BY licence covers the model, not Sega's or Nintendo's art, so the label has to be
neutralized before the derivative can be committed, exactly as `SnesModelPrep` does for SNES. That
is now possible per model (see the rectangles below) but it must be verified by looking, because
neutralizing the wrong island either leaves the artwork in a public build or wipes the moulding.

**Real-world dimensions are not known well enough to record.** The SNES profile shipped with an 87mm
height against a true 77.5mm, and nobody saw it for a day because the scene absorbs the error as a
12% stretch rather than a size change. Guessing dimensions for four more platforms is how four more
distortions ship. Each needs its measured figures, and then
`MetricProfiles_MatchTheProportionsOfTheirAuthoredAsset` will hold them honest.

## Per model

### Mega Drive / Genesis — closest to ready

- Single mesh, 334 triangles, already upright: Y is height, the large faces are +Z and -Z.
- Front is **+Z**, so the canonical orientation is identity — no rotation needed.
- Front and back share **0%** of their UV cells, so the label can be neutralized without touching
  the back. This is the good case, and not true of every model here.
- **Label island: roughly u 0.12–0.70, v 0.60–0.965** on the atlas, read off the dump. The tall
  striped island at u 0.75–0.90 is the contact pins — do not neutralize it.
- Proportions W:H:D = 1.553 : 1 : 0.168. Needs real millimetres.
- **Caveat: 334 triangles means no bevelled edges at all.** The studio lights this shell with a
  raking key that exists to catch bevels; with none, it will read as a flat printed card however
  well the label is placed. It is worth a look before investing in it, and probably worth re-sourcing.

### NES — best proportions, needs an axis permutation

- Proportions match a real cartridge to 0.1%: W/H 1.124 against a true 1.125.
- Four meshes and two material sets, 1,266 triangles.
- **Lying on its side.** Width runs along Y, height along Z, depth along X, so canonical space needs
  the cyclic permutation Y→X, Z→Y, X→Z rather than a simple rotation. Whether the label ends up
  upright or inverted is a coin toss until it is rendered — check that first.
- Its atlas is busier than the others; identify the label from the dump rather than by variance.

### Game Boy — mislabelled, and the risky one

- Filed under `gbc/`, but it is a **GBA cartridge**: mesh named `GBA_SP_Cartridge`, W/H 1.748, where
  a GB/GBC cart is taller than wide (~0.88).
- Front and back UV ranges are nearly identical (0.026–0.978 against 0.046–0.983), which suggests
  overlapping or mirrored UVs. If they do overlap, **neutralizing the label damages the back**, and
  masking cannot make this model redistribution-safe. Confirm before any other work.
- Since a GBA shell already ships, the useful framing is an asset upgrade plus the GBA profile fix
  (85x60mm is not a Game Pak either — it is the remaining exclusion in the proportion test), not a
  new platform. A real GBC shell still needs sourcing.

### DS — needs cleaning before anything else

- The file contains **four identical copies** of one card, placed in a row by node matrices, at
  mixed orientations — the +Y and ±Z faces all carry the same surface area.
- Loading it as-is draws four cartridges. Prep has to keep a single node.
- Each card is 33.7 x 35.1 x 1.8, so it lies flat and needs a canonical rotation, and it is about
  half a real DS card's relative thickness (a real card is 33.4 x 35 x 3.8mm).
- Lowest confidence of the four; re-sourcing may be cheaper than repairing.

## Suggested order

1. **GBA profile fix** using the existing shell — closes the last exclusion in the proportion test
   and needs no new asset or licensing work.
2. **NES** — best asset, and a clean end-to-end run of the pipeline SNES proved.
3. **Genesis** — only if it survives a look at how flat 334 triangles render.
4. **DS and GBC** — re-source rather than repair.
