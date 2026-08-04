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

## Not done — next

### 2. Fanart display (dependency for the hero) ⛔

The giant background image in the screenshots is **fan art**, and nothing in the app
displays fan art today (it can be scraped, but is never shown). This is the gate for
the spotlight layout.

- Fetch/display a fan-art asset per game (reuse the existing `GameMediaKind.Fanart`
  scrape path and `GameMediaAssets` storage; add a load path like covers have).
- Fallback for games with no fan art (blurred/darkened cover, or system art).
- Once fan art is on screen, point the colour engine at it instead of the cover
  (one line in `ApplyAmbientThemeForPendingGame`: sample the fan-art asset).

### 3. Gamepad spotlight layout ⛔ (the big piece)

Replace the cover grid in Gamepad mode with the list + hero view:

- Left: virtualized game **list** (text rows), selected row highlighted; favourites
  filter (heart on Y already exists in the shell).
- Right: fan-art hero + title, filename subtitle, star rating, achievements progress,
  Play button. Top toolbar tabs (search / grid / screenshots / achievements / settings).
- Keep the couch focus model (one focus, d-pad) and virtualization intact.

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

## Open question

Are the spotlight screenshots a mockup, or is that layout already running somewhere
(there is a second checkout at `~/Documents/OpenEmu`)? If it exists, #3 builds on it
rather than starting fresh.
