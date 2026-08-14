# SNES model sourcing review — 2026-08-13

This is the detailed sourcing and preparation record. Shipping attribution is in
`THIRD-PARTY-NOTICES.md`; the untouched downloads remain in the user-supplied `models/snes/` area.

## Candidate: SomeKevin — Super Nintendo Cartridge

- Local file: `models/snes/super_nintendo_cartridge.glb`
- Embedded title: `Super Nintendo Cartridge`
- Author: SomeKevin
- Source: <https://sketchfab.com/3d-models/super-nintendo-cartridge-b2076d8a65d648ff99bf51ca9d5fca2a>
- License: CC BY 4.0
- Status: **selected; cleaned runtime derivative integrated**

The local file's embedded attribution and license agree with the live Sketchfab listing. The author
describes the model as eyeballed in Plasticity, UV-unwrapped and textured in Substance Painter. The
download is a conventional metallic-roughness GLB: 33,839 triangles, 27,039 rendered vertices
(17,030 after position welding), one mesh, one material, tangents and three 4096² maps (base colour,
metallic/roughness and normal). Its unscaled proportions are W/H 1.665 and D/H 0.257.

The exact EmuShelf renderer was used for front, three-quarter, side, back, top and bottom inspection.
The moulded silhouette, rounded edges, screws, contact fingers, rear recess and side rails are a clear
improvement over the current prototype shell. The model is authored with the label toward -Z and
needs a 180° canonical Y rotation.

Integration decisions and cleanup:

1. The shell is explicitly the PAL/Super Famicom form, using a 129×77.5×20mm presentation profile.
   (Corrected 2026-08-14 from the 129×87×20mm first recorded here; see `DECISIONS.md`. The model's
   own W/H of 1.665 and D/H of 0.257 agree with 129mm and 20mm, which is what identified the height.)
   North American SNES geometry remains a future regional variant rather than stretching this shell.
2. `SnesModelPrep` deterministically neutralizes the fixed placeholder-label UV island in base-colour,
   metallic/roughness and normal maps. Dynamic game art is a body-attached object-space decal over the
   real label area, with aspect-correct rounded/antialiased edges and independent paper shading. A
   separate sticker plane was tested and rejected after hardware review because its edge visibly floated.
3. The six collapsed triangles are removed and inconsistent triangle winding is corrected against the
   authored vertex normals. The 201 welded boundary edges and 10 welded non-manifold edges remain an
   explicit gate for a future editable-source cleanup; all visible review angles are currently closed.
4. Canonical Y-up/+Z-front orientation is a 180° Y rotation. The renderer centres and normalizes the
   asset, then the 129×77.5×20mm profile restores its physical scale in the shared scene.
5. The authored tangents and base-colour, metallic/roughness and normal maps are retained. The three
   4096² maps are reduced to 1024² for the portable runtime, reducing the GLB from 25.4MB to 3.47MB.
6. SomeKevin, the source URL, CC BY 4.0 and every modification are recorded in
   `THIRD-PARTY-NOTICES.md` and embedded asset metadata. The asset author's license does not itself
   grant rights in Nintendo marks/designs; no downloaded game or Sketchfab branding remains visible.

The Phase 2 asset gate remains open for editable topology cleanup and 1080p real-Windows close-up
review. Those are quality improvements, not blockers for exercising this selected shell in the
experimental shelf.

## Rejected: Laser Design — Super Mario World Game Cartridge

- Local file: `models/snes/super_mario_world_game_cartridge.glb`
- Source: <https://sketchfab.com/3d-models/super-mario-world-game-cartridge-a102d3e7fe5c4770912a56e69b04898a>
- License: CC BY-NC 4.0
- Status: **reference only; never redistribute with EmuShelf**

The non-commercial restriction fails EmuShelf's redistribution gate. Technically it is also a poor
runtime base: an 86,038-triangle photographic scan with one combined material, a single 4096² photo
atlas containing several cartridges and copyrighted retail art, no tangent/normal/roughness maps, and
the older required `KHR_materials_pbrSpecularGlossiness` extension. Its scan noise is expensive while
providing less controllable plastic/label separation than the CC BY candidate.

## Not present locally: Luca Hofmann — Moonsters SNES PAL

- Linked page: <https://sketchfab.com/3d-models/super-nintendo-game-moonsters-snes-pal-7bc7b7a514464faaadd5973472d45ffd>
- Status: **not reviewed as a file; unsuitable for an embedded open-source asset under the store terms**

Neither local GLB identifies this model. The linked page is a paid Sketchfab Store asset. Sketchfab's
standard royalty-free terms prohibit distributing the licensed material as a stand-alone/extractable
file; embedding its GLB in this repository/application would therefore be unsafe. It becomes a viable
candidate only if the author supplies explicit written permission for raw redistribution under an
open-source-compatible license.

## Generated inspection artifacts

The raw, prepared and integrated review renders are under
`artifacts/snes-model-inspection/somekevin-raw/` and
`artifacts/snes-model-inspection/somekevin-rotated/`, with later `prepared-*` directories beside them.
These artifacts are review output, not runtime assets. The preview command supports `--model`,
`--model-yaw`, `--model-raw` and deterministic `--prepare-snes` so future sourced GLBs can be inspected
through the same shaders without first placing them in the product assembly.
