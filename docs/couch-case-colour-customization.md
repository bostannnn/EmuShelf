# Research — user-chosen colours for physical media

Question asked: can the DVD boxes be wired for customization, so they can be coloured differently?

**Short answer: yes, and most of it already exists. But the measurement below changes what you would
actually get, and it is worth deciding on before any UI is built.**

## What is already there

The shader has taken an arbitrary body colour since the first GPU pass. `pbr.frag` carries
`uBodyTint` and `uBodyTintMix`, and `MediaShellRenderer.MaterialVariantAppearance` already resolves
four finishes over the one shared keep-case mesh:

| Variant | Tint | Mix | Roughness | Reflectance |
|---|---|---|---|---|
| `ps2-black` | near-black | 0.82 | 1.06 | 1.00 |
| `gamecube-black` | near-black | 0.80 | 1.04 | 1.00 |
| `ps3-clear` | pale blue | 0.28 | 0.76 | 1.35 |
| `wii-white` | off-white | 0.78 | 0.92 | 1.08 |

So "colour a case" is not a new capability. What is missing is only that the colour comes from a
hard-coded string on the profile (`PhysicalMediaProfile.MaterialVariant`) rather than from the user.

## The measurement that matters

**89.3% of a keep case's surface is covered by its three artwork panels. Only 10.7% is body plastic.**

The front, back and spine panels are each inset just 2% from their faces, so on a fully scraped game
almost the whole case is printed sleeve. A body colour therefore paints the *frame*: the edges, the
rim, the hinge side. That is not a flaw — it is exactly what distinguishes a real black PS2 case
from a white Wii one, and it is why the four variants above read as different objects today. But it
does mean "make my cases red" produces a red edge around box art, not a red box.

Cartridges are the opposite case, and by a wide margin:

| Medium | Printed area | Body plastic |
|---|---|---|
| Keep case | 89.3% | **10.7%** |
| SNES cartridge | 12.0% | **88.0%** |

If the goal is visible colour, cartridges are where it lands. A per-system colour on SNES would
recolour nearly the whole object; the same feature on PS2 recolours a border.

## What wiring it would take

Three decisions first, because they change the work:

1. **Scope — per system, or per game?** Per system is much cheaper and matches the real world (all
   PS2 cases were black). Per game is the one people actually ask for, and needs per-game storage.
2. **Where the colour lives.** Today `MediaShelfRenderItem` carries the profile and the renderer
   resolves the variant string. A user colour has to arrive as data instead: the cleanest shape is
   an optional `Vector3? BodyTint` on the render item, resolved by the app layer, with the variant
   string kept as the default when it is null. That keeps `EmuShelf.Rendering` free of settings.
3. **Whether it also changes finish.** Colour alone is one float3. Matte-vs-gloss and the clear
   treatment are the other two knobs already in `MaterialVariantAppearance`, and they are most of
   what sells PS3's clear case. Exposing colour but not gloss will feel incomplete.

Sketch, per-system, smallest useful version:

- `LibraryViewSettings` (or a new `ShelfAppearanceSettings`) gains a `Dictionary<string, string>` of
  system id to hex colour, persisted by name like every other setting here.
- `MediaShellMap.ProfileForSystem` stays as it is; the app layer looks the override up when it
  builds render items, so the profile keeps meaning "what this medium really is".
- `MediaShelfRenderItem` gains `Vector3? BodyTint`; `DrawShelfItem` prefers it over
  `MaterialVariantAppearance.For(...)`, converting through the existing `ToLinear`.
- UI: the couch Settings screen already has a themes section with swatches, which is the natural
  place and the natural vocabulary.

Per-game instead of per-system would replace the settings dictionary with a column on the game
details store and a per-game action, and is otherwise the same rendering change.

Rough size: the rendering and plumbing are small — an afternoon. The UI and persistence are most of
it, and the design decision above is most of *that*.

## Caveats worth knowing before deciding

- **Tint is applied in linear space** and mixed with the asset's own base colour, so a saturated
  choice at a high mix reads as coloured plastic, while a low mix reads as a stain over the moulding.
  `ps3-clear` deliberately uses a low mix for that reason.
- **A case with no scraped art is already coloured** — its panels take the platform accent. Adding a
  body colour on top of that risks two different colours on one object unless the panel tint follows
  the same choice.
- **Strong saturated colours on a glossy case read as toy plastic** under this studio. The existing
  variants are all near-neutral, which is why they look like packaging. A colour picker with no
  guard rails will mostly produce toys; a curated palette will not.
- **The clear variant is not a colour**, it is a different material. It should stay a finish choice
  rather than becoming a swatch.
