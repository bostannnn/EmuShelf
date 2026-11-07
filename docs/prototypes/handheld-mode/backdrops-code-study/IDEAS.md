# Console backdrops: rendering languages, then scenes

2026-11-07. Design proposal, not an approved set. This follows the owner's rejection of the generated-image study and re-reads Claude's original code lab. No application source changes.

## The idea

Make a small original game environment for each system. Its construction should help identify the console: tile vocabulary, silhouette, perspective, surface treatment and lighting. A console accent colour alone cannot do that. A shader-like filter applied to the same landscape cannot do it either.

Compose the environment **around the Stage**, from the beginning. Use strong foreground forms on the sides, distant detail across the lower middle, and a quiet opening behind the folder. The folder should feel deliberately framed. Keep the lower centre simple enough for the name plate. The backdrop still has to work when the central object changes from a folder to a case.

The first study has three complete code-built scenes. They test three different construction methods, rather than committing to sixteen variations before the owner judges the approach. No image model, stock art, real covers, game assets or external libraries are involved.

## Three working proofs

### NES — Rooftop garden

**Idea:** an 8-bit adventure town seen across a canal: terracotta roofs, inset windows, flower boxes, a glass roof and a washing line. The foreground buildings frame a gap of sky and distant town.

**Why this direction:** the identity comes from repeatable tile patterns, hard clusters and a constrained palette. It gives the picture architectural character without using a familiar platformer's terrain or objects.

**How the mockup is made:** nine explicitly authored 8×8 tile patterns, hand-placed silhouettes and details, and a 15-colour palette. The finished scene is 240×135, scaled eight times at 1080p. Show construction displays the tile grid and actual tile atlas. Water has two transparent overlay frames.

**What needs judgment:** this is an original 8-bit interpretation, not an NES-accurate renderer. The muted colours may feel less immediately NES than a more graphic, high-contrast palette. The rooftop subject is intentionally different from the rejected brick kingdom.

### PlayStation — Seaside plaza

**Idea:** a sunlit, slightly angular coastal piazza: low-poly buildings, a striped canopy, a balcony, polygonal trees, stone steps and a blue sea opening between the buildings.

**Why this direction:** early textured 3D is an opportunity for a real architectural scene, not just a wobbly triangular landscape. Flat planes, visibly coarse texture mapping and dither give the surfaces their identity.

**How the mockup is made:** actual 3D vertices and faces, a camera projection, depth-buffered triangles and affine-mapped original texture tiles. The result is rendered once at 320×180, quantized to 5 bits per colour channel with ordered dither, and scaled six times. Show construction exposes the mesh edges. The camera stays fixed; there is no gratuitous vertex wobble.

**What needs judgment:** a generic low-poly scene is not uniquely PlayStation. It is the strongest test here of whether texture, hard silhouette and composition together provide enough identity. This renderer is an authoring tool, not hardware emulation: it uses a simple software depth buffer and code-authored texture patterns rather than reproducing hardware behaviour.

### PlayStation 2 — Sunlit reservoir

**Idea:** a flooded stone structure beneath an enormous arch, with a distant aqueduct, worn steps, hanging vegetation and pale green reflections. The foreground opening frames the central folder.

**Why this direction:** monumental space and restrained light can evoke atmospheric early-2000s adventures while preserving a light Stage. This develops the promising *subject* of the rejected aqueduct painting using actual constructed geometry.

**How the mockup is made:** an extruded arch made of separate wedge-shaped stones, blocks for the piers and steps, low-resolution original stone textures, face lighting, distance haze and an inverted mesh pass for the reflections. The result is baked once at 960×540. A low-contrast light veil is baked into the scene; no blur or live lighting runs during motion. Show construction exposes the projected triangles.

**What needs judgment:** the broad stone forms and calm water work as framing, but the material detail is still a prototype. This is an art direction to judge, not an attempt to pass off a production-quality PS2 screenshot. No particular game's location or architecture is reproduced.

## Ideas for the full library

These are proposals, not secretly selected backdrops. Only the three above are implemented. The point of the table is to choose a meaningful visual mechanism before spending time on every scene.

| System | Proposed scene | What should make it feel different | Concrete way to build it |
|---|---|---|---|
| NES | **Rooftop garden** — a canal town in chunky architectural tiles | Hard 8×8 pattern vocabulary; economical silhouettes | Authored tile atlas, 240×135 bake; two water frames. **Working proof.** |
| SNES | **Cloud atlas** — a richly illustrated archipelago seen from above | An ornate 16-bit world map, with a shallow perspective ground plane | Author terrain tiles and shoreline clusters first; bake several perspective map frames; keep the central sea open. No kart track or copied map. |
| GBC | **Orchard stop** — a little railway halt between an orchard and pond | Compact top-down rooms and bold handheld colour groups | 8×8/16×16 tiles, deliberately small sprite-like props; two water frames. Avoid a monster-route layout, tall grass and familiar houses. |
| GBA | **Greenhouse in the clouds** — glass structures and suspended gardens | Detailed portable pixel art; saturated highlights and deeper sprite layering | Hand-authored foreground leaves, lattice tiles and layered sky; bake at 240×135 or 320×180 after comparing readability. |
| GameCube | **Wind harbour** — a little curved pier, sail workshop and faceted sea | Sculpted, rounded forms with clean bands of cel shading | Low-poly meshes with two or three authored shade values; original cloud silhouettes; wave frames. No familiar islands or boats. |
| DS | **Two views of the town** — a low-poly street below, small pixel skyline above | A visible meeting of small-scale polygonal space and crisp 2D sprites | Deliberately interlock a mesh-rendered town layer with a tile/sprite layer, rather than use an unrelated notebook. Show both views in one composition; no menu reconstruction. |
| Wii | **Resort afternoon** — clean wood decking, a pool and bright landscaping | Friendly proportions, clean materials, soft baked contact shadows | Model a few rounded architectural forms and sparse plants; light them warmly; bake at half resolution. Avoid recreating a named sports resort. |
| 3DS | **Terrace theatre** — little streets and garden walls at three depths | A miniature diorama with unusually legible depth separation | Author three perspective-consistent planes; test a small, one-shot focus parallax. No perpetual swaying of every object. |
| Mega Drive | **Midnight express** — a coastal rail viaduct at sunset | High-contrast 16-bit architecture, saturated ramps and long horizontal layers | Authored rail/city tiles and three parallax strips; short water bands. No chequered cliffs, loops or collectible shapes. |
| Dreamcast | **Print district** — a playful angular city of workshops and overhead rails | Bright, graphic, arcade-like 3D and large flat colour areas | Cel-shaded mesh geometry, original typographic-free posters and baked screenprint patterns; one rail-side light pulse. No swirl or copied graffiti. |
| PlayStation | **Seaside plaza** | Coarse textured polygons, affine mapping and dither | Code-authored meshes/textures baked at 320×180. **Working proof.** |
| PlayStation 2 | **Sunlit reservoir** | Architectural scale, subdued material colour, distance haze | Extruded stone mesh, baked lighting and reflection pass. **Working proof.** |
| PSP | **Night service** — a hillside terminal with illuminated platforms | Dense but compact portable 3D, strong silhouettes and a few luminous accents | A tighter, darker mesh scene; bake light halos into static assets; swap a few tiny signal frames. Avoid a generic modern skyline. |
| PlayStation 3 | **After the rain** — wet glass canopies and an empty botanical courtyard | HD-era material contrast, reflections and softer cinematic light | More detailed geometry and baked reflections/highlights; animate a small rain ripple layer. Atmospheric movement can borrow the calmness the owner liked without reproducing XMB. |
| Arcade | **Neon machine shop** — a dark industrial side-scroller set | Chunky sprite machinery, controlled emissive colour and attract-mode rhythm | Authored pixel machinery atlas; three small light-cycle frames; optional far conveyor. No cabinets or actual title screens needed. |
| Steam | **Workshop at dawn** — a contemporary indie-inspired maker space | Precise geometry with tactile materials and editorial colour | An original isometric model with baked soft lighting and one quiet animated object. Avoid the earlier test-chamber/game trade dress. |

## A useful alternative

If picturesque environments still feel too arbitrary, try **graphic stage sets** instead: tile-built architectural wings for NES, a visibly triangulated sculptural set for PS1, lit architectural planes for PS2. The console's rendering vocabulary remains the theme, with less fictional scenery to identify or dislike. The same authoring pipeline supports this; it would be a different composition, not a colour preset.

## Implementation after visual selection

1. **Keep authoring separate from playback.** Retain tile atlases, mesh data, palettes and texture generators as editable sources. Export the chosen base plus moving overlays as PNGs. Do not put this JavaScript renderer or a game engine in Avalonia. A code-built asset is still a bitmap when the app displays it; the benefit is deliberate, inspectable construction.
2. **Use a manifest per backdrop.** An ID, native dimensions, interpolation mode, base image, overlay frames, timing and explicit layer bounds are sufficient for these proofs. Provisional IDs would be `nes-rooftop-garden`, `ps1-seaside-plaza` and `ps2-sunlit-reservoir`, pending selection. Preserve room for a transform-only strip or one-shot parallax later. Keep the model separate from which console chooses it.
3. **Pass an ID through the existing value record.** Add the chosen string BackdropId to KnownSystems and carry it through HandheldPlatform and HandheldBackdrop. Keep value equality so changing games within a system does not reload or crossfade the backdrop. Until selections are made, the current era motif stays in the app.
4. **One static base, one optional active overlay.** NES and PS1 use nearest-neighbour at 8× and 6× on the 1920×1080 Stage. PS2 uses filtered 2× scaling. These proofs need three asset buffers each: the base and two water frames. Swap frame visibility/opacity; never rebuild the scene on a timer. Motion is off by default in the lab.
5. **Bound the transition and cache.** Keep only the current and incoming decoded scenes in production. Cancel obsolete loads, dispose retired bitmaps safely, and allow one system-change crossfade. Stop overlay clocks when hidden/backgrounded and under reduced motion. The old arbitrary limits are not the design goal, but avoiding constant GPU work still matters.
6. **Measure the actual choice.** The layer readout is base + two overlay buffers at RGBA8: NES 0.37 MiB, PS1 0.66 MiB, PS2 5.93 MiB. It excludes GPU copies, transition surfaces, source meshes/textures and the lab's construction views/cache. Production can crop transparent overlays substantially. Test readability, switching, temperature and memory on the Thor before accepting an animation rate.
7. **Validate behavior and identity.** Check registry coverage/fallback, repeated same-system selection, cancellation/disposal, hidden/background pause and reduced motion. Then judge the scenes on the Thor with ordinary covers. Passing a memory check does not establish visual quality.

## How to judge this study

Start with the **folder visible**. Does the environment frame it well? Can you still tell the systems apart without reading their labels? Switch to a game: is the case still the focal point? Then inspect Backdrop only and Show construction. The latter explains the asset method; it is not an alternate finished art direction.

The owner has not selected any of these. Keep/Maybe/Not this and notes in the lab record feedback only, separately from previous rounds.
