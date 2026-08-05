# Couch mode: artwork-driven theming and the spotlight layout

This is the working plan for making Gamepad (couch) mode look like the target
spotlight screenshots. It tracks what has shipped and what is still to do, so the
pieces can be picked up in order without re-deriving the design each time.

The end goal is the screenshot design: a scrolling game **list** on the left, a
large **fanart hero** on the right with title / rating / achievements / Play, and
the whole interface **recoloured from the selected game's artwork**.

## Done

### 1. Ambient artwork theming (the colour engine) ✅

"Match colours to artwork": in Gamepad mode, the accent **and** panel tone recolour
live from the focused game's cover as selection moves.

- `EmuShelf.Core/Settings/ArtworkColor.cs` — WCAG luminance/contrast + HSL maths (pure).
- `EmuShelf.Core/Settings/ArtworkPalette.cs` — `ArtworkPaletteFactory` derives a full
  palette from a vibrant swatch + average brightness. Hue comes from the art; every
  surface/text lightness is **forced** into a safe band; body text meets a 4.5:1
  contrast floor; the dark/light decision has a hysteresis dead-zone so scrolling a
  run of mid-brightness covers never strobes.
- `EmuShelf.App/Services/ArtworkPaletteExtractor.cs` — copies the on-screen cover's
  pixels (UI thread) and finds the dominant vivid hue + brightness (worker thread).
  Grayscale/low-saturation art returns null → the chosen theme shows through.
- `EmuShelf.App/Services/AppThemeService.cs` — `ApplyArtworkPalette` / `ClearArtworkPalette`
  generate a token dictionary and layer it **above** the chosen theme (the theme is
  the fallback); `RequestedThemeVariant` follows the artwork's brightness.
- `EmuShelf.App/ViewModels/MainViewModel.cs` — drives it off the Gamepad `FocusedGame`
  (debounced 120 ms), cached per cover, cleared on return to Desktop.
- Toggle: `AppSettings.AmbientThemeFromArtwork`, in Desktop **Settings → Appearance**.
- Tests: `ArtworkThemingTests` (factory + extractor) and `AppThemeServiceTests`
  (persist, live apply/clear). Full suite green.

Decision record: see `DECISIONS.md` (2026-08-04 entry).

## Done

### 2. Fanart display (dependency for the hero) ✅

Fan art now renders in the spotlight hero — it was scrapeable (`GameMediaKind.Fanart`)
but never shown. Implemented in the library view model, not per-tile:

- `GameViewModel` gained `FanartImage`/`HasFanartImage`, `FanartPath`, `HasFanart`,
  `RatingText`/`HasRating`, and `ApplySpotlightDetails(...)`.
- `MainViewModel.LoadSpotlightHero` reads the focused game's scraped details once per
  game off the UI thread (`IGameDetailsStore`, threaded in via the constructor), caches
  the fan-art path + rating on the view model, then decodes the fan-art bitmap
  (`SafeImageDecoder`, ≤1920×1080). Only the current hero keeps a decoded bitmap; it is
  released as focus moves (generation-guarded), so a long list never accumulates images.
- No-fanart games fall back to the cover on the themed surface. The focused **cover** is
  also loaded here (the grid isn't realizing tiles in spotlight mode), so the fallback and
  the ambient palette still have an image.

### 3. Gamepad spotlight layout ✅ (switchable, not a replacement)

Added as a **second** couch view alongside the cover grid (grid stays the default;
`LibraryViewSettings.GamepadSpotlightView`, remembered across launches). Toggle via
Start ▸ Menu ▸ "Spotlight view" / "Cover grid view".

- Left: virtualized game **list** (`GamepadSpotlightList`, a passive `ListBox` whose
  `SelectedItem` tracks `FocusedGame` and auto-scrolls into view), platform header.
- Right: fan-art hero + system name, title, filename subtitle, star rating, achievement
  progress, Play — reusing the dock's `FocusedGame.*` bindings.
- Reuses the couch focus model + virtualization. In the single-column list Down/Up step
  one game and Left/Right are inert (branched in `DispatchLibraryAction`); the grid keeps
  its 2-D row-stride movement. Tests: `GamepadSpotlightView_TogglesLayout_StepsOneGame_AndPersists`.

### 3a. Hero restructure ✅ (Phase A — full-bleed backdrop, logo, clean titles)

Turned the boxed hero into a proper console dashboard:

- **Fan art is now the full-window backdrop**, edge to edge behind both the floating list
  and the hero text (not a small right-hand card). Legibility scrims darken the bottom
  (title/actions) and the left (list). The list panel is translucent so the art shows through.
- **No-fanart fallback is a themed gradient** (dark base + an ambient-accent glow that fades),
  so an art-less game still recolours with the palette. (Was the cover; changed per request.)
- **Game logo** (`GameMediaKind.Wheel`) renders large above the title; nothing shows when a
  game has no logo (the title text sits directly below). `GameViewModel.WheelImage`, loaded
  by the same generation-guarded per-hero path as fan art.
- **Normalized titles** — the list and hero show the canonical ScreenScraper name instead of
  the filename (`Pokemon - FireRed Version (USA, Europe) (Rev 1)` → `Pokémon FireRed`).
  Spotlight-only and non-destructive: `GameViewModel.SpotlightDisplayTitle` is filled from a
  new bulk `IGameMetadataStore.GetProviderTitles()` at scope build; `Title`, the grid, and the
  desktop are untouched. Test: `GetProviderTitles_ReturnsScrapedTitles_PreferringTheNeutralLocale`.

### 3b. Ambient palette from the fan art ✅

`ApplyAmbientThemeForPendingGame` now samples the **fan-art backdrop** in the spotlight
(the cover still drives the grid), so the accent matches what fills the screen. It falls
back to the cover until the backdrop decodes, and `LoadSpotlightHero` re-triggers the tint
once the fan art is ready. Cached per art path; requires the "Match colours to artwork"
toggle (Desktop Settings ▸ Appearance) to be on.

## Not done — next

### 3c. Remaining hero polish ⛔

- Favourites hearts/filter in the list rows.
- A display-font / spacing pass to match the mockups' typography.

**Dropped (per user, 2026-08-05):** the left-edge button-hint column and the floating icon
toolbar. Couch navigation stays exactly as it is (top platform rail, Start ▸ Menu).

### 4. Desktop detail-split view mode ⛔

The desktop counterpart discussed earlier: a `Grid | Detail` view switch where Detail
shows the selected game's metadata (description, genre, year, players, rating),
screenshots, playtime, and achievements — surfacing data already scraped but unshown.
Extend the `IsGridView` boolean into a named view-mode enum (settings already store
it by name).

### 5. Gamepad-settings ambient toggle ⛔ (small, deferred)

The toggle is Desktop-only for now. The couch Themes section is a gallery with no row
support, and adding it to General broke the Desktop/Gamepad per-section parity tests.
Add it to the couch Themes-gallery focus model when the spotlight lands.

## Notes / smaller follow-ups

- Extractor assumes BGRA pixel order — correct for Avalonia/Skia on macOS + Windows.
- If a focused cover hasn't decoded yet, the retint waits for the next focus move;
  could hook cover-loaded to retint immediately.

## Suggested order

1. Fan-art display (#2) — unblocks the hero and improves the colour source.
2. Gamepad spotlight layout (#3) — the visible redesign.
3. Desktop detail view (#4) — independent; can run in parallel.
4. Couch ambient toggle (#5) — once the spotlight Themes surface exists.

## Resolved question

The spotlight screenshots were a mockup — the layout did not exist in this tree or the
second checkout at `~/Documents/OpenEmu`, so #3 was built from scratch (2026-08-05).
