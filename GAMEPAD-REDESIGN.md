# Gamepad Mode Redesign — Goal, Status, and Handoff

Last updated: 2026-08-01
Current branch: `feat/save-sync-platforms-and-latency`

This is the durable handoff for EmuShelf's controller-first redesign. `ROADMAP.md` remains the
checkbox-level project plan and `DECISIONS.md` remains the append-only record of non-obvious
choices. This document explains the product intent that connects those individual entries.

## Product goal

Make Gamepad mode a complete, polished living-room interface that can be comfortably read and
operated from a couch. It must feel intentionally designed for a controller, not like Desktop mode
made larger or placed behind controller shortcuts.

NeoStation is the main interaction reference:

- <https://github.com/misobadev/neostation-frontend>
- <https://neostation.dev/>

Copy the best ideas, not the product: clear visual hierarchy, large readable targets, a calm modern
palette, strong focused-game presence, compact achievement progress, and complete controller
workflows. Do not copy NeoStation source, branding, artwork, exact layouts, or square game cards.
EmuShelf keeps its existing per-platform cover ratios and its own visual identity.

The screenshots and HTML mockup supplied during design are directional references, not pixel-level
specifications or sources of truth. When they conflict, use this priority order:

1. Real application behavior and screenshots at the target viewport.
2. The product goal and constraints in this document.
3. Existing EmuShelf design language and data semantics.
4. Reference screenshots.
5. The HTML mockup.

## Fixed product decisions

- Do not add a clock. It does not help navigation or launch flow; the host device already provides
  time and status UI.
- Keep the virtualized shelf and platform-specific cover aspect ratios. Improve spacing, shadows,
  focus, and information hierarchy without replacing covers with NeoStation-style squares.
- Keep the persistent bottom dock simple. It contains focused-game identity, a compact achievement
  track when applicable, and one prominent **A Play** action. Counts and shortcut legends belong in
  Menu or contextual overlays, not in the persistent dock.
- Gamepad Settings must work inside Gamepad mode. A Desktop/native picker handoff is acceptable
  only when the operating system must select a file, folder, or executable.
- The first controller artwork picker uses EmuShelf's existing bounded DuckDuckGo image-search
  provider. Results are always reviewed and selected by the user; they never become automatic
  metadata.
- ScreenScraper.fr is Phase 5: a separate authenticated provider project with provenance, cache,
  quota, locale, and failure isolation. Do not block the initial controller artwork picker on it.
- RetroAchievements remains display-only inside EmuShelf. Emulators own unlocking and submission.
- A/B/X/Y semantic colors stay stable across themes. Never rely on color alone for meaning.
- Desktop behavior should remain unchanged unless a phase explicitly requires shared behavior.

## What has been completed

### Foundation — hardened controller shell

The earlier M31 work established the controller-first foundation: native SDL plus Steam Input
routing, spatial shelf navigation, safe Back behavior, platform rail, controller global menu,
game-action sheet, focus/input-modality synchronization, hidden empty platforms, portable interface
mode handling, and controller-readable empty/error/status states. See `ROADMAP.md`, M31 Phases 1–3.

### Phase 1 — focused-game presence and achievement experience (complete in code)

The first couch-first visual phase is implemented:

- The library has a clean single-row bottom dock rather than an overloaded desktop-like footer.
- Platform, title, launch filename, compact achievement progress, and **A Play** form one hierarchy.
- Achievement and Play surfaces share a consistent height; redundant percentage, availability,
  library count, and global shortcut clutter were removed from the dock.
- Covers keep their platform ratios and now have subtle depth plus a stronger focused state.
- Controller prompts use stable semantic A/B/X/Y colors.
- The achievement screen is an in-window controller overlay with a selected-achievement detail
  panel and virtualized square-badge grid.
- Achievement tabs are **All**, **Locked**, and **Unlocked**, controlled by LB/RB.
- Y cycles **Default**, **Points**, **Unlocked first**, and **Recently unlocked** sorting. Community
  unlock-percentage sorting is intentionally deferred until that value has an explicit API and
  portable-cache field.
- Sorting keeps the selector in its physical grid slot instead of chasing the previously selected
  achievement around the screen.
- Achievement navigation is spatial and clamps at row edges and missing final-row cells.
- Pointer selection and controller selection use the same logical focus.
- Filtering reliably restores layout even if the same achievement remains selected.
- The real 86-achievement regression is covered: fresh item-source snapshots and delayed realization
  prevent an empty row-1/column-1 cell after filter or sort changes.

Relevant commits:

- `8c84c87 feat: refine gamepad library shell`
- `aacadc4 feat: redesign gamepad achievements`
- `13e26cd fix: stabilize gamepad achievement grid`

Validation at this handoff: 360 App tests plus 656 Infrastructure tests passed (1,016 total),
including the 1280×800 large virtualized achievement-grid render path. Real-controller and Steam
Deck acceptance are still part of the broader M31 exit gate.

## Remaining implementation plan

Work these phases in order. Each phase should be usable and reviewed in the real application before
starting the next one.

### Phase 2 — Settings entirely on the controller (next)

Replace the Settings-to-Desktop explanation with an in-window, sectioned Gamepad Settings surface
backed by the existing settings view model. This is the next implementation task.

Suggested sequence:

1. Inventory every existing Settings field and classify it as toggle, choice, action, text, secret,
   file, folder, or executable path. Define controller behavior before writing the view.
2. Add the Gamepad Settings overlay and a stable focus model. Use LB/RB for sections, D-pad for rows
   and choices, A to activate/edit, and B to return without escaping Gamepad mode.
3. Land **General**, **RetroAchievements**, **Saves**, and **Texture Packs** first. Reuse current view
   models and services; do not create a second settings store or divergent rules.
4. Provide the shared controller-safe text-entry flow for ordinary text and protected credentials.
   Request a platform on-screen keyboard where available and retain hardware-keyboard fallback.
5. Add **Emulators** paths and arguments. Use an explicit native picker handoff only for paths that
   cannot be selected safely in-window; returning from it must restore Gamepad mode and focus.
6. Add loading, validation, success, failure, disconnected-drive, and destructive-confirmation
   states with controller-readable messages.

Phase 2 acceptance criteria:

- A controller user can discover, inspect, change, save, cancel, and revisit every non-picker
  setting without being told to switch to Desktop mode.
- Section and row focus is deterministic after save, cancellation, refresh, and validation errors.
- Credentials are never displayed unmasked, logged, or written to `settings.json`.
- Existing Desktop Settings behavior and portable persistence remain intact.
- Tests cover command routing, persistence, validation, focus restoration, and 1280×800 geometry;
  review a populated real-app screenshot before calling the phase complete.

### Phase 3 — controller artwork search with DuckDuckGo

Replace the current cover-to-Desktop handoff for online artwork with a controller-native overlay
using the existing `CoverSearchViewModel`, `DuckDuckGoArtworkSearchProvider`, and safe preview/
download pipeline.

Required experience:

- Open from the focused game's Y actions without leaving Gamepad mode.
- Prefill a sensible query but keep search explicit and user-driven.
- Show loading, error, no-results, and a virtualized candidate grid with a clear preview/selection.
- Let the user inspect, select, confirm, or cancel before EmuShelf changes its own cover cache.
- Preserve **Use local image** as a secondary, clearly named native file-picker handoff.
- Restore the same game, platform, and controller focus after apply or cancel.
- Keep unverified DuckDuckGo results out of automatic metadata enrichment.

### Phase 4 — complete portable themes

Move from Light/Dark plus a single accent to coherent full palettes. A palette owns background,
surface, raised surface, text, muted text, border, selection, focus, and decorative accents—not just
one highlight color.

Start with a small reviewed set such as Coral, Ocean, Grape, Mint, and Slate. Add a controller-native
theme gallery, persist the selection portably, and define a safe `Themes/` import format only after
the built-in token contract is stable. Verify contrast, disabled/error states, achievement states,
and A/B/X/Y prompt consistency at 1280×800 and a large 16:9 viewport.

### Phase 5 — ScreenScraper.fr integration

Build ScreenScraper as a proper authenticated metadata/media provider, not as brittle web scraping
and not as a shortcut around service quotas.

Required groundwork:

- Secure storage for user credentials and application/developer credentials.
- Explicit platform-id mapping for EmuShelf's supported systems.
- Hash-first identification where supported, with controlled title-search fallback.
- Region, language, and media-type preferences.
- Quota/rate/concurrency handling with cancellation and useful controller-visible errors.
- Portable caching with attribution, source provenance, timestamps, and deterministic fixtures.
- Provider-failure isolation so DuckDuckGo/manual cover selection and library use still work.
- Explicit consent for both single-game and batch operations; no shared accounts or quota
  workarounds.
- Feed returned metadata and media variants into the same controller scraper UI rather than
  creating a second unrelated workflow.

Before implementation, research the current official ScreenScraper API and terms and record the
chosen authentication, attribution, caching, and rate-limit rules in `DECISIONS.md`.

## Deferred ideas

These remain candidates after the five phases and should not expand the next task:

- Hero/detail view with fanart, wheel/logo art, and Info/Media/Achievements tabs.
- Carousel and alternate view modes.
- Controller-adjustable card sizes, sort order, and broader library view settings.
- Achievement sorting by community unlock percentage once the data contract exists.

## Cross-cutting engineering rules

- Follow MVVM with CommunityToolkit.Mvvm. Code-behind is view wiring only.
- Preserve virtualization and asynchronous image loading; do not regress large libraries.
- Keep macOS build compatibility even when a Windows/Linux host integration is platform-specific.
- Never modify game files, emulator settings, achievement state, or external texture packs.
- Keep user data portable beside the executable and support relative paths.
- Append non-obvious choices to `DECISIONS.md`; update the relevant `ROADMAP.md` item when work lands.
- Add view-model tests and visual/geometry regressions for focus, filtering, sorting, resizing,
  empty/error states, and overlay transitions.
- Validate at 1280×800 first, then a large 16:9 living-room viewport. Use real application
  screenshots as the UI review source of truth.
- If EmuShelf is already running and locks normal output files, do not kill the user's app. Use an
  isolated test/build output directory.
- Preserve unrelated local/untracked files. At this handoff those include
  `.claude/settings.local.json`, `arcade/`, `genesis_plus_gx_libretro.so`, and `rclone.exe`.

## Ready-to-paste handoff prompt

```text
Continue the EmuShelf controller-first redesign in C:\Users\Andre\Desktop\OpenEmu.

First read AGENTS.md, GAMEPAD-REDESIGN.md, the M31 section of ROADMAP.md, and the latest relevant
entries in DECISIONS.md. The active branch is feat/save-sync-platforms-and-latency. Phase 1 is
already implemented; do not redesign or revert it. The key commits are 8c84c87, aacadc4, and
13e26cd. The last validation passed 1,016 tests.

Our goal is a complete, beautiful couch-first Gamepad mode inspired by NeoStation, but not a copy.
The real app is the source of truth; supplied HTML/mockups are only general direction. Keep
EmuShelf's per-platform cover ratios, virtualized shelf, simplified bottom dock, achievement UI,
and no-clock decision.

Start Phase 2: replace the Settings-to-Desktop handoff with complete in-window controller Settings.
Begin by auditing the existing settings view model and classifying every field/control. Then write a
concrete implementation plan and implement the first coherent slice: the overlay/focus model plus
General, RetroAchievements, Saves, and Texture Packs. Reuse the existing settings view model and
services. Use LB/RB sections, D-pad rows, A edit/activate, and B back. Design text/secret entry around
the shared controller-safe OSK path; use an explicit native picker handoff only where a file, folder,
or executable genuinely requires it. Keep Desktop behavior unchanged.

Review the actual 1280x800 UI, not just XAML or a synthetic mock. Add controller-routing,
persistence, focus-restoration, and visual/geometry tests. Keep macOS compatibility, MVVM, portable
storage, virtualization, and the rule that EmuShelf never changes game files or emulator-owned
configuration. Update ROADMAP.md and append decisions to DECISIONS.md. If the app is running, do not
kill it; use isolated build/test output. Do not touch the unrelated untracked files listed in
GAMEPAD-REDESIGN.md.

DuckDuckGo controller artwork search is Phase 3, complete themes are Phase 4, and ScreenScraper.fr
is Phase 5. Do not pull those phases into the Settings implementation unless shared infrastructure
is strictly necessary.
```
