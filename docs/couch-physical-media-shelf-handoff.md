# Handoff — couch physical-media shelf (Windows continuation)

Start here, then read the full design in [`couch-physical-media-shelf.md`](couch-physical-media-shelf.md).

## What this is

A third Gamepad (couch) library layout — a calm, monochrome **coverflow shelf** where the
focused game sits enlarged at centre and neighbours recede, reached from **Start ▸ menu ▸ View
mode ▸ Shelf** (beside Grid and Spotlight). Long-term goal: the focused cover becomes a
**rotatable 3D model of the media** (SNES cartridge, PS2 case) spun with the right stick. This
branch delivers **Phase 1 only** (flat covers); 3D + right-stick are later phases.

## Branch / how to get it

- Branch: `claude/gamepad-physical-media-view-abe3db` (pushed to origin).
- `git fetch && git switch claude/gamepad-physical-media-view-abe3db`.

## Status

**Phase 1 (shelf layout) is done and green** — `dotnet test` App suite: 598 passing (Release).

Delivered:
- 3-way couch layout selector (`GamepadLibraryLayout { Grid, Spotlight, Shelf }`), persisted by
  name, back-compatible with the old spotlight bool.
- Horizontal coverflow shelf: uniform-slot strip translated in code-behind so the focused game
  is always dead-centre (works for any count, incl. ends); focused cover enlarges, neighbours
  scale/dim; per-system accent-tinted background; title + platform below. No focus ring.
- Flat covers only (grid covers reused at a larger fixed shelf height).

Not started: **Phase 2** (`Media3DControl` — Skia software 3D, the actual cartridge/case),
**Phase 3** (right-stick + R3 input plumbing), **Phase 4** (polish, ScreenScraper box/support
textures, reduce-motion). See the design doc's build-order + §2/§2a/§3 for the plan; the
feasibility research (rendering=Skia lease, input=SDL already exposes right stick + R3) is
summarised there and in `DECISIONS.md` (2026-08-10 entry).

## Open item — margins/spacing are being dialled in by feel

The user is iterating on the shelf composition against a "calm, minimal, Super Mario All-Stars"
target. Current values (last change: pull neighbours in from the screen edges, tighten vertical):

- `MainWindow.axaml` shelf item slot `Width="410"`, viewport `Height="360"`, StackPanel
  `Spacing="22"`; neighbour style `Panel.shelf-media` scale `0.66` / opacity `0.5`.
- `MainWindow.axaml.cs` `public const double ShelfSlotWidth = 410` — **MUST equal the item slot
  Width in the XAML** (centring math depends on it). Keep them in sync when tuning.
- Cover size: `GameViewModel.GamepadShelfCoverTargetHeight = 300` (fixed height, width follows
  aspect).

These are the knobs to turn for the look: slot width (neighbour distance), neighbour
scale/opacity, viewport height + spacing (vertical tightness), cover target height (size). Expect
another round of feedback from the user on exact feel.

## Files (Phase 1)

- `src/EmuShelf.App/ViewModels/GamepadLibraryLayout.cs` — the layout enum (new).
- `src/EmuShelf.App/ViewModels/MainViewModel.cs` — layout state, show-gates
  (`ShowGamepadShelf`), picker commands + clamped Left/Right stepping, shelf d-pad nav
  (Left/Right step one game, Up/Down inert), restore/persist.
- `src/EmuShelf.App/ViewModels/GameViewModel.cs` — `GamepadShelfCoverWidth/Height`.
- `src/EmuShelf.App/Views/MainWindow.axaml` — the shelf panel + coverflow strip, picker Shelf
  tile, shelf styles; focused dock now gated on `IsGamepadGridLayout`.
- `src/EmuShelf.App/Views/MainWindow.axaml.cs` — `CentreShelf`/`OnGamepadShelfViewportSizeChanged`
  (translate-to-centre with snap-vs-glide), triggers on focus/layout/scope change; a
  zero-width guard added to `OnGamepadLibrarySizeChanged`.
- `src/EmuShelf.Core/Settings/LibraryViewSettings.cs` — `GamepadLayout` string field.
- Tests: `MainViewModelTests.cs` (shelf + 3-way stepping), `MainWindowVisualSnapshotTests.cs`
  (picker card count 6→7, spotlight write updated).

## Windows build / run / see the shelf

```
dotnet build
dotnet test tests\EmuShelf.App.Tests\EmuShelf.App.Tests.csproj -c Release   # run the App suite for UI changes
src\EmuShelf.App\bin\Release\net10.0\EmuShelf.exe --gamepad-ui              # launch couch mode
```

To land on the shelf: open the menu (**F10** — on Windows it isn't hijacked by media keys like
on macOS), D-pad/arrow **Up** to the View mode row, **Right** to the Shelf tile. Or set the
persisted layout: in the app's settings JSON set `LibraryView.GamepadLayout` to `"Shelf"` (the
file is the portable `Settings/settings.json` beside the exe, or the per-user app-data copy).

## Gotchas learned (mostly macOS-only — why the user moved to Windows)

- **macOS dual-copy trap (not a Windows problem):** an installed `EmuShelf.app` and the worktree
  build share bundle id `com.emushelf.app`; `open_application` fronts the installed one, and
  couch mode runs fullscreen on its own Space — made it very hard to screenshot the right window.
  On Windows the worktree `EmuShelf.exe` is a plain window; just look at it.
- The coverflow strip is **not virtualized** (ItemsControl + StackPanel, translated). Fine for a
  single system's covers (lazy-loaded async); if a huge "All Games" shelf janks, windowing the
  strip is the future fix (noted in the design doc).
- `ShelfSlotWidth` duplication (XAML ↔ code-behind) noted above.
- Full App suite in Release is the gate for any UI change (visual snapshot tests assert overlay
  pixel heights; they vary by OS, so re-baseline expectations on Windows if a snapshot differs).
