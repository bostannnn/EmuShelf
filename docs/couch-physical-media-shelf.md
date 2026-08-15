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

## Direction after the real-hardware prototype (2026-08-13)

The first running prototype proved that Avalonia can host the Silk.NET renderer, but it also exposed
the limit of the original "one 3D hero over a 2D strip" design. That design can demonstrate a shell;
it cannot produce the intended experience of rummaging through physical media. The product target is
therefore a **single physical-media scene** containing every visible shelf item. Moving focus changes a
continuous shelf position, so the media travel through space rather than a centred object being swapped.

The scene follows four rules:

1. **Physical scale is data.** Each medium records real dimensions in millimetres plus a small optional
   presentation correction. The keep case is the reference size; SNES is naturally medium and GBA
   naturally small. The camera is shared by the scene and never auto-fits each item independently.
2. **One renderer owns the visible row.** It draws a bounded window around focus (normally two or three
   games on each side), reusing shell meshes and cached per-game textures. Unsupported media use a thin
   cover card in the same 3D coordinate system, so mixed-platform rows still move continuously.
3. **Selection is an animation target, not an immediate replacement.** The view model owns a continuous
   shelf offset and a focused index. A critically damped animation advances the offset every frame; the
   renderer derives every item's transform from that one value. D-pad repeat changes the target without
   restarting or stacking animations.
4. **Launch is an explicit state machine.** After launch preflight succeeds, the selected medium lifts,
   spins/aligns, and travels down through a platform-specific insertion path. The emulator starts at the
   animation's commit point. Failure before commit leaves the shelf untouched; failure at start animates
   the medium back instead of stranding the interface in a half-launched pose.

This direction is inspired by Socket's tactile, platform-specific physical-media presentation, not by
its branding, assets, or source. EmuShelf keeps its Avalonia application shell and external-emulator
architecture; the rendering project grows into a deliberately small scene renderer rather than embedding
or switching the whole application to a game engine.

## Scope

The prototype supplies three authored geometry families: SNES cartridge, GBA cartridge and a keep
case. Metric profiles distinguish the 135x190x14mm PS2/GameCube/Wii package from the shorter PS3
Blu-ray case even while they temporarily share geometry. Every unauthored system renders as a thin
cover-art card inside the same scene; the whole mode falls back to its original flat cover strip only
when OpenGL is unavailable. Arcade deliberately remains a cover card (no boxed retail medium exists).

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
- **One bounded scene, not a hero overlay.** `MediaShelf3DControl` owns the focused medium and up to
  three neighbours on each side. A continuous selection coordinate translates their physically
  spaced world centres through one fixed camera. The earlier one-hero/2D-strip composition survives
  only as the no-GL compatibility fallback.
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

- **Shells.** `MediaShell` — eight of them as of 2026-08-15: the SNES, NES, Mega Drive, Game Boy and
  Game Boy Advance cartridges, the DS card, the shared disc keep case, and the arcade cabinet, plus
  a flat cover card for systems with no authored medium. Each is a `.glb` embedded in the assembly
  with a `MediaShellDefinition` giving its orientation into canonical space (Y up, +Z front, one
  unit tall, centred), its panels, and its material knobs. `MediaShellMap` in the app layer maps a
  system id to a shell; the renderer never hears about consoles. This list was three when the
  document was written and will date again: the enum is the authority, and `THIRD-PARTY-NOTICES.md`
  is the roster of what each shell is and where it came from.
- **Lighting.** A procedural studio — a dim room plus rectangular softboxes — rendered to a
  cubemap, then convolved to a 32px diffuse irradiance cube and a 5-mip GGX-prefiltered specular
  chain, with Karis' analytic environment BRDF in place of a lookup texture. The neutral studio is
  baked once per GL context; a lightweight shader uniform tints its dim ambient room when the accent
  changes, without recolouring the softboxes or interrupting navigation. **The softboxes sit in
  front of the subject, not above it**: a flat vertical face reflects the hemisphere in front of
  it, so an overhead rig hides every highlight where the shell can never show one. A direct GGX
  key supplies local bevel/groove contrast. Each visible medium also contributes a bounded analytic
  two-lobe shadow (tight contact plus soft offset cast) to a transparent horizontal receiving plane;
  only the premultiplied shadow is composited, so the themed Avalonia background remains the floor.
- **Artwork.** Located on faces in object space rather than relying on a downloaded model's label UVs.
  The production SNES shell uses its UVs for authored body PBR maps, then blends game art into the real
  label region as a body-attached decal. Its signed-distance mask keeps rounded corners circular on a
  landscape panel, uses derivative antialiasing at every edge, and blends to independent paper roughness
  plus a flat label normal without adding geometry or a visible gap. GBA and keep-case panels use the same
  projection foundation until their asset passes arrive. An `ArtPanel` is a rectangle on a face in
  fractions of the shell's half-extent;
  `MediaShellCatalog.Place` resolves it against the loaded model's real bounds into an origin and
  two edge vectors, and the fragment shader keeps a fragment only if it lies inside that rectangle
  *and* its object-space normal agrees with the face. Panels carry their own roughness, aspect
  fitting (`Stretch` for a case's sleeve, `Cover` for a cartridge's landscape label), and whether
  printed art flattens the moulding beneath it.
- **Output.** Rendered at 2x and resolved down with `glBlitFramebuffer`, which is both the cheapest
  antialiasing for a large slowly-turning silhouette and correct: the supersampled buffer is
  already premultiplied, which is the space filtering must happen in.
- **Texture filtering.** Colour/data maps and dynamic labels have trilinear mipmaps. When the driver
  exposes EXT/ARB anisotropic filtering, EmuShelf uses up to 8× so a cartridge label stays legible
  through the steep controller-driven poses without making support a platform requirement.
- **Portability.** Shaders are written to the intersection of GLSL ES 3.00 and desktop GLSL 1.50,
  with only the `#version` header injected per backend, because Avalonia hands us ANGLE (GLES 3.0)
  on Windows and a core profile on macOS. Attribute locations are bound with
  `glBindAttribLocation`, since `layout(location=)` would demand GLSL 330.

**`MediaShelf3DControl : OpenGlControlBase`** (`src/EmuShelf.App/Controls`) is the live host. Its
inputs are the game list, focused game, continuous shelf position and focused-item yaw/pitch. It
keeps only the focused item plus three neighbours per side subscribed and uploaded, converts
Avalonia bitmaps out of premultiplied BGRA once per change, and submits them to one renderer scene.
If the context cannot be brought up it raises `InitializationFailed` and the library reveals the
original translated flat-cover strip for the session.

### 2a. Face textures from ScreenScraper

The medium's faces are textured, in priority order, from ScreenScraper media the app can
already fetch through its existing pipeline. ScreenScraper serves **two families** — the
box, and the "support" (the physical cartridge/disc). The names do not imply interchangeable
images: inspection of downloaded SNES assets showed `support-2D` is a complete pre-rendered
cartridge image, while `support-texture` is the flattened printable label texture suitable for
the authored model's label recess. Confirmed kinds on a real SNES entry include `box-2D`,
`box-2D-back`, `box-2D-side`, `box-texture`, `support-2D`, and `support-texture`.

Per archetype, texture in this order and stop at the first hit:

- **PS2 keep case:** `box-texture` (UV-wrap the whole case) → `box-2D` + `box-2D-back` +
  `box-2D-side` mapped per face, any missing face filled with the accent tint → `box-2D`
  front only + accent tint → flat placeholder.
- **SNES cartridge:** selected `support-texture` on the authored front-label slot → the authored
  accent-colour blank label when absent, invalid, or still loading. `support-2D` remains available
  as a complete flat physical-media preview; it is not projected into the label recess. `box-2D`
  is the *cardboard box* and is likewise never cropped onto the cartridge—it would only apply to a
  future "boxed" SNES variant.

Coverage varies sharply by title — the top titles have everything, obscure ones often only
`box-2D` — so the accent-tinted parametric shell (§2) is the guaranteed base layer and the
scraped faces are progressive enhancement. The `jeuInfos` response already lists the full
media set in one call, so the extra faces cost image downloads (bandwidth + disk under
`Covers/`), **not** extra API quota. Fetch the non-front faces **lazily** — only for the
focused game while the shelf mode is in use — rather than bloating every scrape.

**Plumbing:** `GameMediaKind.PhysicalMedia` and `PhysicalMediaTexture` map to ScreenScraper's
`support-2D` and `support-texture` respectively. The store bulk-projects selected texture paths
into `GameViewModel`; `MediaShelf3DControl` decodes only the focused-neighbour window off the UI
thread and retains a bounded LRU of decoded images. Because ScreenScraper is already the app's
sanctioned art source, these kinds add no licensing category.

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
- [x] **Phase 2 — the 3D prototype.** *Renderer done 2026-08-10; face textures (§2a) still open.*
  - [x] The renderer and its host, landed as a GPU renderer rather than the Skia control
        originally planned (see `DECISIONS.md`). `EmuShelf.Rendering` draws a shell with
        metallic-roughness PBR under a procedurally baked studio environment. The original
        `Media3DControl` hosted one focused item; M42 Phase 1 replaces that composition with one
        bounded `MediaShelf3DControl` scene. Cover art is located on faces in object space; SNES adds an
        aspect-correct rounded decal mask while temporary shells retain square projection bounds.
        Missing art leaves an empty authored shell; GL failure reveals the complete flat fallback.
  - [ ] Complete the ScreenScraper box/support face pipeline (§2a). Cartridge
        `support-texture` now loads lazily for the visible shelf window and falls back to the authored
        blank label; neither the complete `support-2D` render nor portrait box art is projected into
        the label recess. Case back/spine/wrap mapping remains open.
- [x] **Windows hardware verification.** The prototype was exercised in the real Avalonia/ANGLE
      host on 2026-08-13. That testing exposed and fixed the matrix-upload, HiDPI viewport,
      orientation, framing and centring defects recorded in `DECISIONS.md`.
- [x] **Phase 3 — Right-stick wiring.** Landed 2026-08-10 and exercised on Windows hardware.
      `GamepadReading` gained defaulted `RightStickX/Y` and `GamepadButtons`
      gained `RightStick`; `SdlGamepadReader` reads axes 2/3 and button 8;
      `MediaRotationModel` (pure, 15 unit tests) integrates them into yaw/pitch;
      `MainViewModel.ApplyRightStickRotation` drives the bound pose and is gated to the shelf.
      R3 and any change of focus recentre.
      **Deviation from §3:** recentre returns to a slight three-quarter pose, not face-on. At yaw 0
      a keep case is a flat rectangle and reads as the flat cover it just replaced — the thickness,
      spine and highlight sweep all vanish. `MediaRotationModel.RestYaw/RestPitch` hold the pose.
- [ ] **Prototype Phase 4 — Polish.** Reduce-motion setting, spine title, shading/shadow tuning,
      more media types (GameCube/Wii disc-case reuse the PS2 archetype; PS1 jewel case;
      handheld carts reuse the SNES archetype).

## Next implementation sequence — physical-media scene

This sequence replaces further one-off camera/layout tuning. Each phase has a visible acceptance gate
and can be reviewed on real hardware before the next one begins.

Implementation should take a vertical slice through these workstreams: land A plus the smallest D scene
using the current assets, bring one SNES item to the B/C quality gate, then apply that proven asset and
lighting pipeline to GBA and the case family before E. Do not polish the one-hero overlay further.

### A. Metric presentation profiles

- Add `PhysicalMediaProfile`: shell, real `Width/Height/DepthMm`, canonical orientation, material
  variant, artwork slots, insertion-animation id, and an optional presentation scale defaulting to 1.
- Stop normalizing and auto-fitting every shell to the viewport. Normalize model geometry to its profile's
  physical dimensions, use one shelf camera, align every medium to a common bottom baseline, and compute
  horizontal centres from physical projected width plus a constant gap.
- Initial calibration gate: a keep case reads large, SNES medium, and GBA small in the same screenshot;
  changing focus does not make the world scale breathe.

### B. Purpose-built asset and material pipeline

- Replace inconsistent showcase/download models with clean, redistribution-safe production assets,
  preferably authored for EmuShelf from measured references. Keep editable source, attribution, units,
  pivots, UVs, tangents, named material slots, and LODs beside each exported GLB.
- Require bevelled silhouette edges and weighted/split normals. Small bevels catch the studio lights and
  are more important to realism than indiscriminately increasing polygon count.
- Support base-colour, normal, metallic/roughness and ambient-occlusion maps, mipmaps, anisotropic label
  filtering, and material variants (for example black PS2, translucent/clear PS3, and white Wii cases).
- Use ScreenScraper `support-texture` for authored cartridge label/material slots; retain `support-2D`
  as a complete flat physical-media preview. Implement box front/back/spine or wrap textures for cases.
  Never crop portrait box art or a complete cartridge render onto a cartridge label.
- Asset gate: front, back, spine, top and edge close-ups pass at 1080p with no open seams, inverted normals,
  stretched labels, faceted bevels, or borrowed game artwork baked into the shell.

### C. Lighting, contact and image quality

- Keep the existing image-based studio for soft plastic reflections, add a direct key with a filtered
  geometry depth map for self-shadowing, and retain the transparent receiving shelf plane with analytic
  soft contact footprints. Every object must visibly contact the same shelf surface even when direct
  key shadows are unavailable on a future low-end quality tier.
- Add per-material clear-sleeve treatment for keep cases instead of representing paper and clear plastic
  as one roughness value. Add subtle plastic grain through normal/roughness maps, not geometric noise.
- Generate mipmaps for all imported maps, preserve linear/sRGB roles, and add an optional quality tier for
  supersampling/shadow resolution. Avoid screen-space effects until the material and shadow fundamentals
  pass review.
- Lighting gate: silhouettes remain readable on dark themes, labels retain their colour, highlights move
  smoothly during rotation, and contact shadows do not swim or detach while scrolling.

### D. Continuous multi-item shelf

- Replace the translated Avalonia `ItemsControl` plus hero overlay with one `MediaShelf3DControl`. Feed it
  immutable render items for the visible window and a continuous `ShelfPosition` measured in shelf units.
- Add a pure `PhysicalShelfMotionModel` (target index, position, velocity, elapsed time, reduced-motion
  mode). Render on demand while moving/rotating; stop requesting frames once settled.
- Cache decoded artwork off the UI thread and GPU textures in a bounded LRU. Reuse one mesh/material set
  per profile; asynchronously decode immutable model assets before their first draw, while keeping the
  context-owned upload on the GL thread. Start with ordinary draw calls—five to seven objects do not
  justify instancing complexity.
- The shared renderer uses adaptive supersampling: up to 2× on small outputs, bounded to 2560×1440, and
  never below native output size. Every submitted shelf item receives its isolated 1024px PCF self-shadow
  pass, so moving focus cannot visibly flatten a cartridge that remains on screen. Games outside the
  bounded seven-item scene receive no rendering work.
  Uploaded face textures are held to a fixed budget counted in textures rather than games — a keep
  case uploads three where a cartridge uploads one — so reversing direction does not repeat
  copy/upload/mipmap work without letting a scraped row multiply GPU memory by three. Render targets grow in 256px buckets during resize, panel placements are cached with shell
  resources, and profile material variants tune body tint/roughness/reflectance over shared geometry.
  These are renderer/control policies, inherited by every physical-media profile.
- Interaction gate: one d-pad step visibly carries the old, selected and next media through the same scene;
  held input remains continuous; a 500-game library has bounded memory; 1280x800 and 1920x1080 hold 60 fps
  on the Windows integrated-GPU acceptance machine.

### E. Launch and return choreography

- Introduce a view-model-owned `ShelfLaunchTransition` state machine: `Idle -> Preflight -> Lift -> Spin ->
  Insert -> Committed`, plus `Return` and `Cancelled`. The animation exposes transforms; it never starts a
  process itself.
- Let the existing launch coordinator run validation first, await the transition's commit signal, then start
  the emulator and minimize. On a start failure, restore the medium. On emulator exit, restore the shelf
  with a short reverse/fade transition while preserving the focused game.
- Give each profile an insertion path (cartridge vertical, disc/case stylized downward handoff) and allow
  platform sounds only as optional, licensed assets with a mute/reduce-motion equivalent.
- Launch gate: repeated A presses cannot double-launch, B can cancel before commit, every failure path
  restores input and the selected medium, and reduced motion uses a short fade/translate rather than spin.

### F. Rollout

- Ship behind an experimental physical-shelf setting until SNES, GBA and the keep-case family pass the
  asset, motion, launch-failure and performance gates on Windows plus build/headless checks on macOS.
- Preserve the current flat shelf as the GL-failure fallback. Add new media profiles one at a time; a system
  is never mapped to a vaguely similar shell merely to increase the supported count.

## Fallbacks and guardrails

- **No scraped cover → empty physical shell when a real profile exists.** Its label/sleeve panel uses
  the platform accent until suitable art arrives. Systems with no authored profile keep the flat
  placeholder card. The absence of artwork must not make a known physical medium disappear.
- **Arcade → always flat** (no boxed media; matches the existing "title-screen snap, not
  packaging" treatment).
- **Regional packaging** (e.g. Dreamcast square-vs-portrait, PS1 longbox-vs-jewel): a mesh
  can't use the 2D "let the cover's own aspect ratio decide" dodge, so each system commits
  to **one canonical shell**, documented as a deliberate simplification. Not in v1.
- **No usable GL context → flat cover.** A driver that will not serve GLES 3.0, a remote session,
  a headless run: `MediaShelf3DHost` waits for explicit renderer readiness and sends every game back
  to its flat cover when initialization throws or remains silent. The GL child is absent outside
  shelf mode, so ordinary grid/spotlight use pays no context or model-preparation cost. Couch mode
  stays functional everywhere.

## Testing

- `MediaRotationModel`: deadzone rejection, velocity integration over `dt`, full-360° yaw,
  pitch clamp, rest-where-released (no auto-return), `Recenter()` on R3 and focus change.
- `GamepadNavigationControllerTests`: add R3 edge → `ResetRotation`; confirm existing
  left-stick / digital nav is unchanged (the appended defaulted reading fields keep the
  `Connected(...)` helper compiling verbatim).
- View-model: layout persists across launches (extend the existing
  `GamepadSpotlightView_TogglesLayout…` pattern to the 3-way selector).
- `MediaShelf3DControl` is GPU interop (like `SdlGamepadReader`) so it stays
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
Sketchfab models, credited in `THIRD-PARTY-NOTICES.md`; fixed placeholder packaging is removed from
the runtime asset and game artwork is supplied dynamically, so no third-party game packaging is
displayed. Front-face texturing reuses the
same scraped cover already shown flat today, so it introduces no new licensing category.

A `DECISIONS.md` entry lands per phase as it's implemented (per the CLAUDE.md rule), not
pre-committed here.
