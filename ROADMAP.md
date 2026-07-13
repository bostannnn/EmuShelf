# Roadmap to version 1

Derived from the design doc's milestones (docs/design-document.pdf §13), split so each milestone is a self-contained work session. Check items off as they land. Definition of done for v1 is the doc's §14.

## M1 — App shell ✅ (2026-07-12)

Avalonia shell: system sidebar fed from Integrations, toolbar (grid/list toggle, search, add, settings), empty-state content area, status bar. Builds and runs on macOS, zero warnings.

## M2 — Portable storage ✅ (2026-07-12)

- [x] Settings service: JSON in `Settings/` beside the executable, loaded at startup.
- [x] SQLite database in `Data/library.db` (Microsoft.Data.Sqlite): schema for games, library folders, per-system emulator config. Schema versioning table from day one.
- [x] Relative-path handling: store paths relative to the app directory when on the same volume, absolute otherwise.
- [x] App startup wiring: create data directories on first run.

## M3 — Library views and import plumbing ✅ (2026-07-12)

- [x] Game grid view (virtualized) and list view, switched by the existing toggle; search filtering with debounce.
- [x] "Add games" flow: pick files or a folder, assign to system (suggest by extension, user confirms), persist to DB.
- [x] Recursive folder scanning off the UI thread with progress in the status bar.
- [x] Startup availability check (background stat of known paths); unavailable games marked and not launchable.
- [x] Manual rescan action (per system and global).

## M4 — Format rules for file-based systems

- [ ] Extension maps: PS1 (.cue/.chd/.m3u/.pbp/.iso), PS2 (.iso/.chd/.cso/.m3u), GC/Wii (.iso/.rvz/.wbfs/.gcm/.ciso).
- [ ] .cue parsing: referenced .bin files never appear as separate games.
- [ ] .m3u playlists: playlist is the game entry; referenced discs hidden.
- [ ] GC vs Wii disambiguation by disc-header magic words (plain and within .rvz/.wbfs containers).

## M5 — PS3 importing → moved to Backlog (2026-07-12)

Deferred; see **Backlog** at the end of this file. Milestone numbers M6–M8 are kept
as-is so references don't shift. The next milestone to work is M4, then M6.

## M6 — Emulator configuration and launching

- [ ] Settings UI: per-emulator executable picker and editable global launch arguments.
- [ ] Argument templates with {GamePath}, {GameDirectory}, {GameFileName}, {EmulatorDirectory}; args passed as an array, never a shell string.
- [ ] Launch flow: validate game + emulator, minimize frontend, start process, track exit, restore. Double-click and context menu.
- [ ] Launch-failure feedback in the status area.
- [ ] Verify on Windows with real emulators (DuckStation, PCSX2, RPCS3, Dolphin).

## M7 — Titles, covers, and editing

- [ ] Manual cover assignment: copy the chosen image into `Covers/`, generate cached thumbnails in `Cache/` off the UI thread.
- [ ] System-branded placeholder covers (original art, generated — no OpenEmu assets) showing the game title.
- [ ] Title editing via compact popover; right-click context menu (launch, edit, set cover, remove).
- [ ] Remove flow: DB-only, confirmation states files are untouched.

## M8 — Polish and packaging

- [ ] Light/dark theme toggle (and follow-system default).
- [ ] Performance pass: cold start, large-library scroll, reduced UI work while minimized during play.
- [ ] Error handling and logging to `Logs/`.
- [ ] Portable win-x64 zip via CI; test the full §14 checklist on Windows.

## Backlog (deferred, not in the current v1 sequence)

### PS3 importing (was M5) — deferred 2026-07-12

- [ ] Recognize PS3 game directories (PS3_GAME/USRDIR/EBOOT.BIN layout and RPCS3-installed games).
- [ ] Parse PARAM.SFO for default titles.
- [ ] Scan a folder of many game dirs, or add one game dir; each recognized dir = one entry.

Directory-based importing is the one system that needs custom scanning code (design doc
§6/§9). It slots in behind the existing `IGameImportRules` / directory-aware
`IAvailabilityChecker` seams from M3 without reworking the shared scanner. Note the
knock-on effects while this is parked: the PlayStation 3 sidebar system stays empty, and
M6's RPCS3 launch verification has no PS3 entries to launch until this is picked up. The
design doc's §14 "first usable version" includes PS3/RPCS3, so shipping v1 with this in the
backlog is a deliberate scope reduction of that definition.
