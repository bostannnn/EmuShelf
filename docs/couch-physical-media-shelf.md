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

**v1 ships two media types: SNES (cartridge) and PS2 (DVD keep case).** They are the two
parametric archetypes (a solid cartridge and a thin disc case) and the two most iconic
shapes; proving both proves the system. **Every other system falls back to its flat cover**
in this mode — the shelf and rotation still work, the focused item is just a flat card until
its shell is authored. Arcade always falls back (no boxed retail media exists).

Settled design decisions from the research:

- **Rendering: Skia software 3D in a custom Avalonia control** — no OpenGL, no new native
  dependency. Verified end-to-end on macOS arm64 (a throwaway spike rendered a textured PS2
  case rotating front→spine→back). Reuses the Skia the app already ships, via
  `Avalonia.Skia.ISkiaSharpApiLeaseFeature`, so it is identical on Windows and Linux.
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

### 2. `Media3DControl` (the Skia 3D object)

`Media3DControl : Control` in `src/EmuShelf.App` (a `Controls/` home is fine). Bindable
inputs:

- `double Yaw`, `double Pitch` — radians, driven by the rotation model (§3). Test with the
  keyboard / left stick before the right stick exists.
- Face bitmaps — the async-loaded cover plus, where scraped, the back / spine / wrap
  images (see §2a), each converted to `SKImage` once and cached (rebuilt only when the
  source changes), never per frame.
- `MediaType Media` — enum, v1 = `{ SnesCartridge, Ps2KeepCase }`, extensible.
- Accent / face colours from `GameSystem` (`AccentColor`, `CoverAspectRatio`) — the base
  layer under any scraped face texture.

`Render(DrawingContext)` → `ctx.Custom(new Media3DDraw(...))`. Inside
`ICustomDrawOperation.Render`, lease the live canvas:

```csharp
var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>()?.Lease();
if (lease is null) { /* fallback: draw the flat cover */ return; }
using (lease) { var canvas = lease.SkCanvas; /* projection + faces */ }
```

The draw core (from the verified spike): build the box from per-`MediaType` half-extents,
project each face with a manual perspective divide (**note:** `SK3dView` was removed in
SkiaSharp 3.x — do the projection by hand with `SKMatrix44`), back-face cull (a
front-facing quad in y-down screen space has a **positive** cross product), painter's
z-sort, then texture each face with `SKCanvas.DrawVertices` over a ~10×10 subdivided grid
(a bitmap-shader homography collapses the sampling — use `DrawVertices`). Add a Lambert
tint, a sheen gradient, and a blurred ground shadow. Geometry table per type: PS2 keep
case ≈ 0.72 : 1.0 : 0.11 (w:h:d); SNES cartridge chunkier and near-square with a faked
front label recess. Front face gets the cover; spine gets the system tint + optional title
text.

Redraw by `InvalidateVisual()` on a render tick while the item is focused and rotating.

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
- [ ] **Phase 2 — `Media3DControl`.** The Skia control, focused-item-only, driven by a
      plain `Yaw`/`Pitch` binding (exercise via keyboard first). SNES cartridge + PS2 keep
      case geometry; flat-cover fallback for every other system, on `null` lease, and on
      no-cover games. First light textures the front from the existing cover + accent tint;
      then add the ScreenScraper box/support media kinds (§2a) and the layered
      texture→per-face→accent fallback, fetching the non-front faces lazily for the focused
      game. *Ships the rotatable case.*
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
- **Non-Skia backend / null lease → flat cover.** Couch mode stays functional everywhere.

## Testing

- `MediaRotationModel`: deadzone rejection, velocity integration over `dt`, full-360° yaw,
  pitch clamp, rest-where-released (no auto-return), `Recenter()` on R3 and focus change.
- `GamepadNavigationControllerTests`: add R3 edge → `ResetRotation`; confirm existing
  left-stick / digital nav is unchanged (the appended defaulted reading fields keep the
  `Connected(...)` helper compiling verbatim).
- View-model: layout persists across launches (extend the existing
  `GamepadSpotlightView_TogglesLayout…` pattern to the 3-way selector).
- `Media3DControl` is native-Skia interop (like `SdlGamepadReader`) so it stays
  screenshot/manual-verified rather than unit-tested; keep the geometry/projection math in a
  pure helper where practical so *that* can be tested headless.
- Full `dotnet build` / `dotnet test` green on macOS at every phase (visual snapshot tests
  assert overlay pixel heights — run the whole App suite in Release for UI changes).

## Later / possible extensions

- **CRT-TV look.** An optional post-process to make the couch shelf feel like a CRT.
  Literal CRT-Royale is a multi-pass libretro GPU shader (slang/Cg) and can't be dropped
  into the Skia pipeline as-is; the realistic route is a Skia `SKRuntimeEffect` (SkSL)
  post-process over the composited frame doing a simplified CRT pass — scanlines, an
  aperture/slot mask, slight barrel curvature, vignette. Cosmetic only, opt-in, gated
  behind the reduce-motion / legibility toggle (scanlines degrade small-text reading).
  Depends on the §2 Skia lease already being in place.

## Ownership note

Shell **geometry** must be wholly original (no OpenEmu code or art; OpenEmu's own Mac app
has a similar 3D-box view — do not use it for reference). Front-face texturing reuses the
same scraped cover already shown flat today, so it introduces no new licensing category.

A `DECISIONS.md` entry lands per phase as it's implemented (per the CLAUDE.md rule), not
pre-committed here.
