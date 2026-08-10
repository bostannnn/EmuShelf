# Couch mode: the physical-media shelf (3D)

This is the working plan for a **third** Gamepad (couch) library layout, alongside the
cover **grid** and the fanart **spotlight**. It captures the design so the pieces can be
built in order without re-deriving them, and records the trade-offs settled during the
feasibility research (three parallel investigation agents: rendering, input, red-team).

## The goal

A calm, minimal shelf — the reference is the *Super Mario All-Stars* "Select Game"
screen. One horizontal row of games on a flat, per-system-tinted **monochromatic**
background, the focused game centred, scroll left/right through the library.

The focused game is not a flat cover but the **physical medium it shipped on**, rendered
as a real **3D object** with the cover art textured on the front face — a SNES cartridge,
a PS2 DVD keep case — that the player **rotates live with the right analog stick** to see
the spine, back, and edges. Turn it in your hands like picking a box off a shelf.

## Scope

**v1 ships three shells: a SNES cartridge, a GBA cartridge, and the DVD keep case** — the last
serving PS2, PS3, GameCube and Wii, which all shipped in the same 135x190x14mm case. They are the two
parametric archetypes (a solid cartridge and a thin disc case) and the two most iconic
shapes; proving both proves the system. **Every other system falls back to its flat cover**
in this mode — the shelf and rotation still work, the focused item is just a flat card until
its shell is authored. Arcade always falls back (no boxed retail media exists).

Settled design decisions from the research:

- **Rendering: a GPU renderer (Silk.NET + OpenGL) in `src/EmuShelf.Rendering`.** ~~Skia software
  3D~~ — superseded 2026-08-10, see `DECISIONS.md`. The research optimised for "can we rotate a
  textured box with no new dependency", and the answer was yes; but the shelf's goal is that a case
  reads as *plastic*, which needs a prefiltered environment sampled per fragment, and a
  painter-sorted CPU rasteriser has no path to that. Silk.NET is bindings only — no native payload
  — and the renderer binds to whatever context Avalonia's `OpenGlControlBase` makes current.
- **Rotation: velocity model** (stick deflection = angular speed), not absolute, so the
  player can reach the *back* of the case (absolute stick throw caps at ~±90°).
- **Release behaviour: rest where released.** The case keeps whatever angle you left it at.
  It re-centres to face-on only when focus moves to another game, or on **R3** (right-stick
  click) as an explicit snap-back. No spring-back on release — that would fight a player who
  rotated deliberately to read the spine.
- **Only the focused item is 3D.** Every off-centre shelf item is the existing flat cover
  `Image`. Exactly one live 3D control at a time — trivially cheap, and it slots into the
  existing layout-toggle machinery.
- **Reduce-motion:** a new setting (the app has none today) that disables idle/parallax
  motion in this mode; rotation stays user-driven.

## Architecture

Three independent pieces; each phase below is shippable on its own.

### 1. The shelf layout (2D, no 3D required)

A third couch layout beside `ShowGamepadGrid` / `ShowGamepadSpotlight` in
`MainViewModel`. Today the couch layout is a single bool
(`LibraryViewSettings.GamepadSpotlightView`, grid vs spotlight); a third state means
promoting that to a small **layout selector** — either a by-name enum field on
`LibraryViewSettings` (grid / spotlight / shelf), matching how `Scope` and `SortColumn`
are stored by name for forward-compatibility, or a second bool. Enum-by-name is preferred.

The view is a horizontally-scrolling, virtualized row of games (reuse the couch focus
model and cover loading), one centred, on a `Panel` whose background is the focused
system's `AccentColor` flattened to a quiet monochrome tone (neutral fallback for systems
with no accent). Left-stick / d-pad Left+Right scroll; the existing `DispatchLibraryAction`
gains a shelf branch (single-row horizontal stepping, like the spotlight list's single
column but on the other axis).

### 2. The 3D hero (GPU)

Two pieces: `EmuShelf.Rendering`, which knows how to draw a medium and nothing about Avalonia, and
a thin control that hosts it.

**`MediaShellRenderer`** (`src/EmuShelf.Rendering`) takes a Silk.NET `GL` whose context somebody
else has made current, plus a framebuffer id to draw into. That signature is the point: the same
renderer serves the app and the headless preview tool, so what ships is what was looked at. It
owns the loaded shells, the baked studio, and the cover texture.

- **Shells.** `MediaShell { SnesCartridge, GbaCartridge, DiscKeepCase }`, each a `.glb` embedded in
  the assembly with a `MediaShellDefinition` giving its orientation into canonical space (Y up, +Z
  front, one unit tall, centred), its panels, and its material knobs. `MediaShellMap` in the app
  layer maps a system id to a shell; the renderer never hears about consoles.
- **Lighting.** A procedural studio — a dim room plus rectangular softboxes — rendered to a
  cubemap, then convolved to a 32px diffuse irradiance cube and a 5-mip GGX-prefiltered specular
  chain, with Karis' analytic environment BRDF in place of a lookup texture. Baked at load and
  re-baked when the accent changes (a console switch), never per frame. **The softboxes sit in
  front of the subject, not above it**: a flat vertical face reflects the hemisphere in front of
  it, so an overhead rig hides every highlight where the shell can never show one.
- **Artwork.** Projected onto faces in object space, not through the models' UVs — the SNES
  cartridge's span -93 to 1.7 and the GBA's label is packed rotated into a shared atlas, so neither
  can carry a decal. An `ArtPanel` is a rectangle on a face in fractions of the shell's half-extent;
  `MediaShellCatalog.Place` resolves it against the loaded model's real bounds into an origin and
  two edge vectors, and the fragment shader keeps a fragment only if it lies inside that rectangle
  *and* its object-space normal agrees with the face. Panels carry their own roughness, aspect
  fitting (`Stretch` for a case's sleeve, `Cover` for a cartridge's landscape label), and whether
  printed art flattens the moulding beneath it.
- **Output.** Rendered at 2x and resolved down with `glBlitFramebuffer`, which is both the cheapest
  antialiasing for a large slowly-turning silhouette and correct: the supersampled buffer is
  already premultiplied, which is the space filtering must happen in.
- **Portability.** Shaders are written to the intersection of GLSL ES 3.00 and desktop GLSL 1.50,
  with only the `#version` header injected per backend, because Avalonia hands us ANGLE (GLES 3.0)
  on Windows and a core profile on macOS. Attribute locations are bound with
  `glBindAttribLocation`, since `layout(location=)` would demand GLSL 330.

**`Media3DControl : OpenGlControlBase`** (`src/EmuShelf.App/Controls`) is the host. Bindable inputs:
`MediaShell? Shell`, `IImage? Cover`, `Color Accent`, and `double Yaw` / `double Pitch` in radians,
driven by the rotation model (§3) — exercise them from the keyboard before the right stick exists.
It binds Silk.NET to Avalonia's context, converts the cover bitmap out of premultiplied BGRA once
per change, and renders. Exactly one live instance exists, bound to `FocusedGame`; every off-centre
shelf item stays a flat cover. If the context cannot be brought up it raises `InitializationFailed`
and the library sends the whole shelf back to flat covers for the session.

### 2a. Face textures from ScreenScraper

The medium's faces are textured, in priority order, from ScreenScraper media the app can
already fetch through its existing pipeline. ScreenScraper serves **two families** — the
box, and the "support" (the physical cartridge/disc) — each with per-face 2D art *and* a
single "texture" wrap image intended to be mapped onto a 3D mesh (the same media
EmulationStation-DE / Batocera use for their 3D boxes). Confirmed present on a real SNES
entry (Super Mario World, gameid 2144, plateforme 4): `box-2D`, `box-2D-back`,
`box-2D-side`, `box-texture`, `support-2D`, `support-texture`.

Per archetype, texture in this order and stop at the first hit:

- **PS2 keep case:** `box-texture` (UV-wrap the whole case) → `box-2D` + `box-2D-back` +
  `box-2D-side` mapped per face, any missing face filled with the accent tint → `box-2D`
  front only + accent tint → flat placeholder.
- **SNES cartridge:** `support-texture` (UV-wrap the cart) → `support-2D` on the front
  label recess + molded-plastic (accent/grey) body → flat placeholder. (`box-2D` here is
  the *cardboard box*, not the cart, so it is **not** used for the cartridge archetype —
  it would only apply to a future "boxed" SNES variant.)

Coverage varies sharply by title — the top titles have everything, obscure ones often only
`box-2D` — so the accent-tinted parametric shell (§2) is the guaranteed base layer and the
scraped faces are progressive enhancement. The `jeuInfos` response already lists the full
media set in one call, so the extra faces cost image downloads (bandwidth + disk under
`Covers/`), **not** extra API quota. Fetch the non-front faces **lazily** — only for the
focused game while the shelf mode is in use — rather than bloating every scrape.

**Plumbing** (mirrors the existing 4-kind path exactly): add
`BoxBack, BoxSpine, BoxTexture, MediaLabel, MediaTexture` to
[`GameMediaKind`](../src/EmuShelf.Core/Metadata/GameScrapingModels.cs); add their
ScreenScraper type strings in
[`ScreenScraperMetadataMapper.GetTypeRank`](../src/EmuShelf.Infrastructure/Metadata/ScreenScraper/ScreenScraperMetadataMapper.cs);
add `case`s in
[`SqliteGameDetailsStore`](../src/EmuShelf.Infrastructure/Metadata/SqliteGameDetailsStore.cs);
expose the decoded bitmaps on `GameViewModel` alongside `FanartImage`/`WheelImage`. Because
ScreenScraper is already the app's sanctioned art source (it serves `box-2D` today under
Creative-Commons redistribution), the new types add no licensing category.

### 3. Right-stick input → `MediaRotationModel`

The current gamepad stack is digital + left-stick only, and converts every reading into
**discrete `GamepadAction` edges**. Continuous rotation needs a **parallel analog channel**
that bypasses `GamepadNavigationController`. The plumbing is small because SDL and the
bundled Steam Input template already carry the right stick and R3:

- `src/EmuShelf.Core/Input/IGamepadReader.cs` — append two **defaulted** fields to the
  `GamepadReading` record struct: `float RightStickX = 0f, float RightStickY = 0f`
  (source-compatible — every existing 4-arg construction still compiles). Add
  `RightStick = 1 << 11` to `GamepadButtons` for R3.
- `src/EmuShelf.Infrastructure/Input/SdlGamepadReader.cs` — ~3 lines: read
  `SDL_CONTROLLER_AXIS_RIGHTX/RIGHTY` (indices 2/3) and `BUTTON_RIGHTSTICK` (8) using the
  P/Invokes already declared, pass them into the reading. Degrades to `Disconnected` (both
  sticks 0) with no controller, as today.
- `src/EmuShelf.App/Services/GamepadAction.cs` — add `ResetRotation` (R3 edge).
- `src/EmuShelf.App/Services/GamepadInputService.cs` — in the tick, forward the raw
  right-stick axes + real elapsed-ms `dt` to the view model
  (`ApplyRightStickRotation(rx, ry, dtMs)`), **in parallel** with the existing discrete
  routing. Keep `GamepadNavigationController.StickDirections` reading *only* the left stick
  so spinning never scrolls.
- **`MediaRotationModel`** — a small pure class owned by the view model (yaw/pitch +
  angular velocity; `Update(rx, ry, dtMs)`, `Recenter()`). Radial deadzone ≈ 0.15 on
  `sqrt(rx²+ry²)` (its own value — **not** the left stick's 0.5 direction threshold),
  magnitude rescaled past the deadzone, response curved (square the normalized magnitude),
  gain ~180–270°/s at full deflection, integrated with real `dt`. Yaw free/unbounded;
  pitch clamped to ~±60°. **No spring-back on release** (rest where released); `Recenter()`
  snaps to front, called on **focus change** and on **R3**. Unit-testable with no Avalonia
  and no native input, like `GamepadNavigationController`.
- Steam Input: the bundled `EmuShelf.vdf` already forwards the right stick and R3 (group 8),
  so the native path needs no `.vdf` change. Analog rotation is a **native-SDL feature**;
  the keyboard-only Steam mapping cannot carry an analog axis — document that, as with
  today's left-stick nav.

## Build order

- [x] **Phase 1 — Shelf layout.** Promote the couch layout to a 3-way selector; add the
      horizontal monochrome shelf as `ShowGamepadShelf`, wired into the layout picker and
      persisted. Flat covers only. *Ships the SMAS look with zero 3D.* — Landed 2026-08-10
      (`GamepadLibraryLayout` enum, `GamepadShelfList`, picker Shelf tile; see `DECISIONS.md`).
      Follow-ups still open: per-system background tint (currently a flat calm surface) and
      centring the focused cover (currently auto-scroll-into-view).
- [ ] **Phase 2 — the 3D hero.** *Renderer done 2026-08-10; face textures (§2a) still open.*
  - [x] The renderer and its host, landed as a GPU renderer rather than the Skia control
        originally planned (see `DECISIONS.md`). `EmuShelf.Rendering` draws a shell with
        metallic-roughness PBR under a procedurally baked studio environment; `Media3DControl :
        OpenGlControlBase` hosts exactly one live instance, bound to `FocusedGame`. Cover art is
        projected onto faces in object space. Flat-cover fallback for every other system, for
        no-cover games, and for a GL context that will not come up. Three shells: SNES cartridge,
        GBA cartridge, and the keep case shared by PS2/PS3/GameCube/Wii.
  - [ ] The ScreenScraper box/support media kinds (§2a) and the layered
        texture→per-face→accent fallback, fetched lazily for the focused game. Until this lands a
        cartridge's landscape label is filled by cropping the portrait box scan — the box art is
        the wrong asset for a cartridge, and `support-2D` is the right one — and a case's back and
        spine take a flat accent tint rather than their real printing.
- [ ] **Phase 3 — Right-stick wiring.** `GamepadReading` fields → `MediaRotationModel` →
      the control; R3 = recenter; recenter on focus change. *Ships R3 rotation.*
- [ ] **Phase 4 — Polish.** Reduce-motion setting, spine title, shading/shadow tuning,
      more media types (GameCube/Wii disc-case reuse the PS2 archetype; PS1 jewel case;
      handheld carts reuse the SNES archetype).

## Fallbacks and guardrails

- **No scraped cover → flat placeholder card, no mesh** (reuse `GameViewModel.Initials`).
  A generic-textured box looks worse than the flat placeholder and costs more to draw.
- **Arcade → always flat** (no boxed media; matches the existing "title-screen snap, not
  packaging" treatment).
- **Regional packaging** (e.g. Dreamcast square-vs-portrait, PS1 longbox-vs-jewel): a mesh
  can't use the 2D "let the cover's own aspect ratio decide" dodge, so each system commits
  to **one canonical shell**, documented as a deliberate simplification. Not in v1.
- **No usable GL context → flat cover.** A driver that will not serve GLES 3.0, a remote session,
  a headless run: `Media3DControl` raises `InitializationFailed` and the library sends every game
  back to its flat cover for the session. Couch mode stays functional everywhere.

## Testing

- `MediaRotationModel`: deadzone rejection, velocity integration over `dt`, full-360° yaw,
  pitch clamp, rest-where-released (no auto-return), `Recenter()` on R3 and focus change.
- `GamepadNavigationControllerTests`: add R3 edge → `ResetRotation`; confirm existing
  left-stick / digital nav is unchanged (the appended defaulted reading fields keep the
  `Connected(...)` helper compiling verbatim).
- View-model: layout persists across launches (extend the existing
  `GamepadSpotlightView_TogglesLayout…` pattern to the 3-way selector).
- `Media3DControl` is GPU interop (like `SdlGamepadReader`) so it stays
  screenshot/manual-verified rather than unit-tested; keep the geometry/projection math in a
  pure helper where practical so *that* can be tested headless. In practice this became
  `tools/EmuShelf.Rendering.Preview`, which renders every shell at a spread of poses to PNG over a
  surfaceless EGL context — the shading is judged by looking at its output, while shell orientation
  and artwork placement are covered by `MediaShellTests` with no GPU involved.
- Full `dotnet build` / `dotnet test` green on macOS at every phase (visual snapshot tests
  assert overlay pixel heights — run the whole App suite in Release for UI changes).

## Later / possible extensions

- **CRT-TV look.** An optional post-process to make the couch shelf feel like a CRT.
  Literal CRT-Royale is a multi-pass libretro GPU shader (slang/Cg) and can't be dropped
  in as-is; the realistic route is now a fragment-shader post-process over the hero's resolved
  frame doing a simplified CRT pass — scanlines, an aperture/slot mask, slight barrel curvature,
  vignette — which the GPU pipeline makes considerably more natural than the Skia one would have. Cosmetic only, opt-in, gated
  behind the reduce-motion / legibility toggle (scanlines degrade small-text reading).
  Depends on the §2 GPU pipeline already being in place.

## Ownership note

Shell **geometry** must carry no OpenEmu code or art (OpenEmu's own Mac app has a similar 3D-box
view — do not use it for reference). The three shipped shells are independently authored CC BY 4.0
Sketchfab models, credited in `THIRD-PARTY-NOTICES.md`; the game artwork their authors photographed
onto them is always painted over at render time, so no third-party packaging is displayed. Front-face texturing reuses the
same scraped cover already shown flat today, so it introduces no new licensing category.

A `DECISIONS.md` entry lands per phase as it's implemented (per the CLAUDE.md rule), not
pre-committed here.
