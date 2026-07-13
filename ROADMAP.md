# Roadmap to version 1

Derived from the design doc's milestones (docs/design-document.pdf §13), split so each milestone is a self-contained work session. Check items off as they land. Definition of done for v1 is the doc's §14.

## M1 — App shell ✅ (2026-07-12)

Avalonia shell: system sidebar fed from Integrations, toolbar (grid/list toggle, search, add, settings), empty-state content area, and contextual operation feedback. Builds and runs on macOS, zero warnings.

## M2 — Portable storage ✅ (2026-07-12)

- [x] Settings service: JSON in `Settings/` beside the executable, loaded at startup.
- [x] SQLite database in `Data/library.db` (Microsoft.Data.Sqlite): schema for games, library folders, per-system emulator config. Schema versioning table from day one.
- [x] Relative-path handling: store paths relative to the app directory when on the same volume, absolute otherwise.
- [x] App startup wiring: create data directories on first run.

## M3 — Library views and import plumbing ✅ (2026-07-12)

- [x] Game grid view (virtualized) and list view, switched by the existing toggle; search filtering with debounce.
- [x] "Add games" flow: pick files or a folder, assign to system (suggest by extension, user confirms), persist to DB.
- [x] Recursive folder scanning off the UI thread with contextual progress feedback.
- [x] Startup availability check (background stat of known paths); unavailable games marked and not launchable.
- [x] Manual rescan action (per system and global).

## M4 — Format rules for file-based systems ✅ (2026-07-13)

- [x] Extension maps: PS1 (.cue/.chd/.m3u/.pbp/.iso), PS2 (.cue/.bin/.iso/.chd/.cso/.m3u), GC/Wii (.iso/.rvz/.wbfs/.gcm/.ciso).
- [x] .cue parsing: referenced .bin files never appear as separate games.
- [x] .m3u playlists: playlist is the game entry; referenced discs hidden.
- [x] GC vs Wii disambiguation by disc-header magic words (plain and within .rvz/.wbfs containers).

## M5 — PS3 importing → moved to Backlog (2026-07-12)

Deferred; see **Backlog** at the end of this file. Milestone numbers M6–M8 are kept
as-is so references don't shift. M6's Windows verification remains pending while the
current implementation milestone is M8.

## M6 — Emulator configuration and launching

- [x] Settings UI: per-emulator executable picker and editable global launch arguments.
- [x] Argument templates with {GamePath}, {GameDirectory}, {GameFileName}, {EmulatorDirectory}; args passed as an array, never a shell string.
- [x] Launch flow: validate game + emulator, minimize frontend, start process, track exit, restore. Double-click and context menu.
- [x] Launch-failure feedback in a contextual notification.
- [ ] Verify on Windows with real emulators (DuckStation, PCSX2, RPCS3, Dolphin).

## M7 — Titles, covers, and editing ✅ (2026-07-13)

- [x] Manual cover assignment: copy the chosen image into `Covers/`, generate cached thumbnails in `Cache/` off the UI thread.
- [x] System-branded placeholder covers using the licensed OpenEmu platform-icon set; upstream BSD license and author credit ship with the app.
- [x] Title editing via compact popover; right-click context menu (launch, edit, set cover, remove).
- [x] Remove flow: DB-only, confirmation states files are untouched.

## M8 — Polish and packaging

- [x] Light/dark theme toggle (and follow-system default).
- [x] Cohesive OpenEmu-inspired library shell with console artwork, collection navigation, polished empty/missing-art states, and future-platform asset coverage.
- [x] Performance pass: indexed/limited recent queries, batched availability writes, bulk UI collection refreshes, ReadyToRun Windows startup, and deferred cover UI work during play.
- [x] Error handling and daily diagnostic logging to portable `Logs/` files.
- [x] Self-contained portable win-x64 zip plus SHA-256 checksum via CI.
- [ ] Test the scoped Windows acceptance checklist on a real Windows machine. The original §14 PS3/RPCS3 portion remains deferred with the PS3 backlog.

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
