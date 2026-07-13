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

## M9 — Opt-in metadata enrichment ✅ (2026-07-13)

- [x] Keep local scanning independent from network work and ask for consent only after the first successful import.
- [x] Support one-time, automatic-after-import, per-platform, and all-library fetch actions.
- [x] Extract exact PS1/PS2 product codes and GameCube/Wii disc ids without modifying game files.
- [x] Resolve canonical titles through cached Libretro DAT catalogs and covers through ordered xlenore/Libretro providers.
- [x] Persist identifiers, match status, source provenance, and title/cover ownership so downloaded data never replaces a manual edit.
- [x] Store downloaded covers and catalog caches in the existing portable directories; bundle no game artwork.
- [x] Document the provider architecture and new-platform checklist in `docs/metadata-enrichment.md`.

## M10 — RetroAchievements display (planned; read-only)

This is the design document's §12 follow-up: EmuShelf shows availability and the
connected user's progress, while external emulators remain solely responsible for
unlocking and submitting achievements. EmuShelf may read game images to identify them,
but it never modifies game files, emulator configuration, or RetroAchievements state.

### 1. Identification feasibility gate

- [ ] Put RetroAchievements game identification behind a Core interface and evaluate the
      official `rcheevos` hash implementation rather than matching by title, filename, or
      emulator-private data. Record the dependency/version/license decision before it lands.
- [ ] Prove exact hash matches with fixtures for every currently imported format: PS1
      (`.cue`, `.chd`, `.m3u`, `.pbp`, `.iso`), PS2 (`.cue`, `.bin`, `.iso`, `.chd`,
      `.cso`, `.m3u`), and GameCube/Wii (`.iso`, `.rvz`, `.wbfs`, `.gcm`, `.ciso`).
      Raw/CUE media can use the stock disc reader; compressed containers need compatible
      logical-disc reader adapters and must not silently fall back to whole-file MD5.
- [ ] Make the supported-format result an explicit gate: ship only formats with verified
      parity on Windows and macOS, and present all other cases as `Unknown`, never `No`.
      PlayStation 3 is out of scope because RetroAchievements has no PS3 console id.
- [ ] Cache each successful or terminal identification by game id plus a source fingerprint
      (size/modified time and descriptor dependencies for CUE/M3U). Re-identify only new or
      changed games, on a single background worker, without a full startup pass.

### 2. Account connection and read-only API client

- [ ] Add a Settings card to connect with RetroAchievements username and **Web API key**;
      this is credential setup, not password login. Validate with `API_GetUserProfile`, save
      the returned stable ULID, and support disconnect/reconnect. Never read or reuse an
      emulator's RetroAchievements password or token.
- [ ] Put the API key behind a platform-specific Core abstraction, never in ordinary
      `settings.json`, diagnostics, exception text, or a logged request URI. Recommended v1
      storage is a DPAPI-protected blob under portable `Settings/` on Windows and a
      session-only provider for macOS development; confirm this portability/security tradeoff
      in `DECISIONS.md` before implementation.
- [ ] Implement a typed, cancellable HTTP client for only the required read endpoints:
      profile validation, system game/hash catalogues, progress for specific local game ids,
      and full game/user progress. Treat authentication, offline, malformed-response, 429,
      and server failures as distinct results while retaining usable cached data.

### 3. Portable catalogue and progress cache

- [ ] Cache achievement-bearing game/hash catalogues for PlayStation (RA 12), GameCube
      (RA 16), Wii (RA 19), and PlayStation 2 (RA 21) under `Cache/RetroAchievements/`.
      Request `API_GetGameList` with achievements and hashes, at most once per system every
      seven days unless the user explicitly refreshes it.
- [ ] Add schema-versioned records for local-game-to-RA links, identification status and
      fingerprint, account-scoped progress summaries, achievement details/unlock dates, and
      last-successful refresh times. Personal progress remains available offline.
- [ ] Download achievement badges on demand, off the UI thread, into a bounded
      `Cache/RetroAchievements/Badges/` cache. Deduplicate concurrent requests and render a
      local placeholder when an image is unavailable.

### 4. Library availability presentation

- [ ] Add a small neutral achievement/trophy mark to a grid tile only after the local image's
      canonical hash matches an achievement-bearing RA game. Do not import RA branding unless
      its redistribution terms have been verified.
- [ ] Add an Achievements column to list view with `Yes`, `No`, or `—`: `No` means a hash was
      computed successfully and is absent from a fresh achievement-bearing catalogue; `—`
      means not connected, pending, unsupported format/system, stale catalogue, or failure.
      Tooltips explain the state and show cached `unlocked / total` progress when available.

### 5. Steam-like achievements popup

- [ ] Open a compact game achievements window from the grid mark, list row, or context menu.
      Show the game title, `unlocked / total`, progress bar, earned points, and last refresh,
      followed by the RA display-ordered achievement list with badge, title, description,
      points, earned date, and locked/unlocked state.
- [ ] Count any earned achievement toward the primary progress bar and additionally mark
      hardcore unlocks, rather than forcing a softcore/hardcore mode choice. The first pass
      displays the resolved game's core data returned by the Web API; leaderboards, rich
      presence, activity feeds, and separate subset/multiset controls are deferred.
- [ ] Render cached content immediately and refresh in the background only when detail data is
      older than five minutes or the user requests it. Keep the popup useful when offline.

### 6. Refresh and rate-limit policy

- [ ] Use one request coordinator with one in-flight request and at least one second between
      automatic calls. Coalesce duplicate work, honor `Retry-After`, back off with jitter after
      429/5xx responses, and never retry authentication failures automatically.
- [ ] When the app starts, refresh summary progress only if the last successful summary sync is
      older than 15 minutes. Query only distinct RA game ids linked to the local library, in
      bounded batches through `API_GetUserProgress`; do not poll recent achievements.
- [ ] After a tracked emulator exits, wait briefly for its submission to settle, then refresh
      that launched game's full progress once and update the open popup/library summary. Do no
      achievement polling while the emulator is running. Also provide an explicit manual
      refresh action.

### 7. Verification and acceptance

- [ ] Add deterministic fixture tests for every promised hash/container format and M3U/CUE
      dependency invalidation. Tests must verify source file bytes and timestamps are unchanged.
- [ ] Add client/cache tests for valid and invalid credentials, redaction, offline startup,
      stale-while-revalidate, cancellation, corrupt cache, 429 `Retry-After`, server errors,
      duplicate requests, and account switching.
- [ ] Add headless UI tests for `Yes`/`No`/`—` semantics, cached progress, popup states, and
      post-session refresh. Keep `dotnet build` and `dotnet test` green on macOS and Windows.
- [ ] On real Windows, connect a test RA account and verify one supported game in DuckStation,
      PCSX2, and Dolphin: EmuShelf identifies it before launch, the emulator performs the
      unlock, and EmuShelf reflects the new progress after process exit without writing to the
      game or emulator data.

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
