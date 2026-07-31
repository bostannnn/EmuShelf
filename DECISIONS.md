# Architectural and product decisions

Append-only log. Newest at the bottom. Each entry: what was decided and why. The product spec itself lives in [docs/design-document.pdf](docs/design-document.pdf).

## 2026-07-12 — Name: EmuShelf

Chosen over "OpenEmu Manual" to avoid confusion with, and implied affiliation to, the unrelated OpenEmu project. Used as solution name, root namespace, and portable folder name.

## 2026-07-12 — Stack

- Current LTS .NET (10.0), Avalonia 12 for UI.
- MVVM via CommunityToolkit.Mvvm (source-generated observables/commands; simpler than ReactiveUI).
- SQLite via `Microsoft.Data.Sqlite` with a thin data layer — schema is too small to justify EF Core.
- Four projects under `src/`: App (UI), Core (domain, no dependencies), Infrastructure (persistence, scanning, launching), Integrations (per-system / per-emulator definitions as feature folders — split into separate projects only if one grows substantially).

## 2026-07-12 — macOS is a first-class dev platform; v1 release is Windows-only

Development happens on macOS, so the app must build and run there at all times — this also enforces the design doc's "no unnecessary Windows coupling" rule. Only the Windows portable zip ships in v1; a packaged macOS release (.app bundle, signing, non-portable data location) is deferred.

## 2026-07-12 — Minimize, not hide, during gameplay

If emulator process-exit tracking ever misfires, a minimized window is recoverable from the taskbar; a hidden window looks like a crash.

## 2026-07-12 — Game identity is the absolute file path

No content hashing in v1. Fast, simple, and consistent with "missing games stay in the library as unavailable."

## 2026-07-12 — Scan policy

- **Availability check** (stat known paths, update available/missing): automatic at every startup, in the background after the UI paints.
- **Discovery scan** (recursive walk for new files): manual rescan action (per system and global) plus automatic scan when a folder is added. No full discovery at startup (design doc §11); an opt-in setting can be added later.

## 2026-07-12 — GameCube/Wii formats

`.iso`, `.rvz`, `.wbfs`, `.gcm`, `.ciso`. A bare `.iso` cannot be attributed to GameCube vs Wii by extension; the scanner reads the disc-header magic words to distinguish them.

## 2026-07-12 — License: GPL-3.0

Copyleft, matching the surrounding emulation ecosystem (Dolphin, PCSX2, RPCS3 are GPL). Canonical text in `LICENSE`.

## 2026-07-12 — OpenEmu is a design reference only

No code (Objective-C/AppKit, nothing reusable in C#/Avalonia) and no artwork (OpenEmu's images are not openly licensed) is copied. System badges and placeholder covers are original.

## 2026-07-12 — M2 library.db schema

`Games(Id, SystemId, Path, Title, CoverPath, IsAvailable, DateAdded)`, `LibraryFolders(Id, SystemId, Path)`, `EmulatorConfigs(SystemId PK, ExecutablePath, LaunchArguments)`, plus a single-row `SchemaVersion(Version)` table. `EmulatorConfigs` is keyed by `SystemId` rather than emulator id, per the roadmap's "per-system emulator config" wording — GameCube and Wii get independent rows even though both default to Dolphin, so a user can point them at different builds/args later without a schema change. No `IsPathRelative`/`IsExecutablePathRelative` flag columns: whether a stored path is relative is always derivable from the string itself (`Path.IsPathRooted`), so a separate flag would just be a second, potentially-inconsistent source of truth. `Games.Path` has a unique index — this is the DB-level enforcement of the existing "game identity is the absolute file path" decision; it holds because `ToStorablePath` is a deterministic, injective function of the absolute path for a fixed app base directory, so uniqueness of the stored string implies uniqueness of the resolved absolute path.

## 2026-07-12 — Settings persisted with defaults on first run

`AppBootstrapper` calls `Save()` immediately after `Load()` if `Settings/settings.json` didn't already exist, so first launch leaves a real, inspectable file instead of an empty folder. Enum values (e.g. `Theme`) serialize as strings (`JsonStringEnumConverter`) rather than numbers, since this file is meant to be portable and human-readable/editable.

## 2026-07-12 — Added a test project ahead of schedule

CLAUDE.md lists `dotnet test` as a project command but no test project existed yet. Added `tests/EmuShelf.Infrastructure.Tests` (xUnit) with M2's own coverage (path resolution, schema creation/idempotency, settings round-trip) so "verified the behavior actually works" has something more than manual runs behind it, and wired `dotnet test` into `.github/workflows/build.yml` right after `dotnet build` so it isn't dead weight.

## 2026-07-12 — Pinned SQLitePCLRaw.bundle_e_sqlite3 to 3.0.3

`Microsoft.Data.Sqlite` 10.0.9 transitively pulls `SQLitePCLRaw.bundle_e_sqlite3` 2.1.11, which bundles a SQLite build affected by CVE-2025-6965 (NU1903, high severity). Added a direct `PackageReference` to 3.0.3 (bundles SQLite 3.50.4.5, above the fixed 3.50.2 threshold) in `EmuShelf.Infrastructure.csproj` so NuGet resolves the higher version; no source changes needed since the API is unchanged.

## 2026-07-12 — Code-review fixes: settings resilience and case-insensitive game identity

A medium-effort review of the M2 diff surfaced two real gaps, both fixed: (1) `JsonSettingsService.Load()` now catches `JsonException` and falls back to defaults — `Settings/settings.json` is meant to be hand-editable, so a syntax mistake there shouldn't block app startup. (2) `Games.Path` is now `COLLATE NOCASE` — v1's target file systems (Windows NTFS, macOS APFS/HFS+) are case-insensitive, so `game.cue` and `GAME.CUE` are the same file and must collide under the "game identity is the absolute file path" decision; a case-sensitive index would have silently let both in as separate library entries.

## 2026-07-12 — Code-review fixes (xhigh pass): cross-OS paths, DB/settings robustness

A second, deeper (xhigh) review of the M2 code drove five more fixes to the storage layer:

- **Stored relative paths are canonical forward-slash.** `RelativePathResolver.ToStorablePath` now replaces the native separator with `/`. A `library.db` written on Windows stored `Games\PS1\game.cue`; opened on macOS (the required dev platform) `\` is an ordinary filename char, so the path resolved to one bogus segment and the game never launched. Reading tolerates `/` on every OS (`Path.Combine`/`GetFullPath` normalize it), so canonicalizing on write makes the portable library genuinely OS-portable — the point of M2's relative-path work.
- **Migrations are idempotent.** `ApplyMigrationV1` uses `CREATE TABLE/INDEX IF NOT EXISTS` and inserts the version row only when absent. Previously a present-but-empty `SchemaVersion` table (external tooling, an interrupted edit) read as version 0 via `Convert.ToInt32(null)`, re-ran the plain `CREATE TABLE`, and threw "already exists" on every launch — an unrecoverable startup loop. (The app's own migrations are atomic and never produce that state, but a portable DB is exposed to outside tampering.)
- **Connection string built via `SqliteConnectionStringBuilder`.** String interpolation of `Data Source={path}` broke when the portable app sat in a folder whose path contained `;` or `=` (both legal filename chars).
- **`JsonSettingsService.Load` also catches `IOException`/`UnauthorizedAccessException`**, not just `JsonException` — a transient lock (AV/backup/second instance) or permissions hiccup on the settings file no longer crashes startup.
- **`Save` is write-then-rename** (temp file + `File.Move(overwrite)`) so a crash or removed drive mid-write can't truncate the live `settings.json`.

Deliberately deferred (reported, not fixed here): no try/catch around storage init means a locked/read-only/full-disk failure still crashes before any window shows — this belongs with M8's error-handling + `Logs/` work, since swallowing it without user feedback would be worse. Bootstrap also runs synchronously on the UI thread; negligible for M2's payload but the natural place to move off-thread once M3+ adds real query load. The `COLLATE NOCASE` index folds only ASCII, so Unicode case-variant filenames can still slip through as duplicates — full coverage needs app-layer case normalization (SQLite has no built-in Unicode collation without ICU).

## 2026-07-12 — M3 import recognition is a stubbed extension table behind `IGameImportRules`

The add/scan flow needs to know which system a file belongs to, but the authoritative format rules — .cue/.bin de-duplication, .m3u playlists, GameCube/Wii disc-header disambiguation — are M4's job. So M3 defines `IGameImportRules` (in Core) and ships a minimal `ExtensionImportRules` (in Integrations) that only maps extensions to systems. A bare `.iso` therefore suggests all four ISO-using systems — PlayStation (".iso where applicable" per design §6 and the M4 map), PS2, GameCube, and Wii — and the user confirms; M4 replaces the implementation behind the same interface without touching the scanner or view model. PS3 has no file extensions here (it is directory-based, M5), so file scanning never mis-attributes discs to it.

## 2026-07-12 — Grid virtualization uses ItemsRepeater + UniformGridLayout

Avalonia has no built-in *virtualizing wrap* panel: `ListBox` virtualizes only a stack. The list view is a plain `ListBox` (free virtualization + selection); the cover grid uses `ItemsRepeater` + `UniformGridLayout` in a `ScrollViewer`, which virtualizes and wraps. That needed the separate `Avalonia.Controls.ItemsRepeater` package (12.0.0 — the newest published; it resolves cleanly against Avalonia 12.1.0). Grid selection/keyboard-nav isn't built into ItemsRepeater; M3 doesn't require selection, so the grid is display-only for now and the list carries selection.

## 2026-07-12 — Add flow: two entry points, always confirm the system

"Add Files…" and "Add Folder…" (a menu under the toolbar `+`). Files suggest a system from their extensions (most common wins) and the user always confirms via a small modal, per design §6 "asks the user to confirm". A folder is treated as dedicated to one system: the user confirms the system, the folder is scanned recursively off the UI thread with live status-bar progress, all candidates are imported, and the folder is remembered in `LibraryFolders` so rescan can re-walk it. Import is `INSERT OR IGNORE` by path, so re-adding or rescanning never duplicates. Titles default to the filename without extension (editing is M7).

## 2026-07-12 — Search debounce via DispatcherTimer in the view model

250 ms `DispatcherTimer` restarted on each `SearchText` change; on tick it re-filters the in-memory list for the selected system (case-insensitive `Contains`). Filtering an already-loaded per-system list in memory avoids a DB round-trip per keystroke. Coupling the view model to `Avalonia.Threading` is acceptable — it is an Avalonia app's view model — and keeps the debounce trivial.

## 2026-07-12 — App project gained ImplicitUsings and a headless test project

`EmuShelf.App` now sets `ImplicitUsings=enable` to match the other three projects (M3 added enough `System.*` usage that explicit usings were pure noise). Added `tests/EmuShelf.App.Tests` using `Avalonia.Headless.XUnit` (which requires **xunit v3** and an `Exe` output — unlike the v2 `Infrastructure.Tests`) to drive `MainViewModel` on a headless UI thread: it exercises add-folder → scan → populate, search filtering, and the availability pass against the real services (only dialogs faked), which is the closest thing to clicking through the app in an automated run. `ReloadGamesAsync`/`ApplyFilter` are `internal` with `InternalsVisibleTo` so those tests can drive them deterministically instead of racing the debounce timer.

## 2026-07-12 — M3 code-review fixes (medium pass)

A medium review of the M3 diff drove several correctness fixes: (1) **reloads are generation-guarded** — a monotonic counter means a slow `GetGames` that finishes after a newer reload is discarded, so fast system-switching can't bind one system's games under another's selection; (2) the **startup availability pass now reloads the current system from the updated DB** instead of patching view-models in place, which removes its ordering dependency on the initial fire-and-forget load (a missing game no longer risks showing as available until the next switch); (3) **grid covers now dim when unavailable** — the `unavailable` class was on a `StackPanel` but the style only matched `Border`/`Grid`; (4) the **"add your first game" empty state is gated on `IsLibraryEmpty`** (the system truly has no games) rather than the filtered `HasGames`, so a search that matches nothing no longer shows the misleading empty prompt; (5) **`AddGamesAsync` sets `IsBusy`** like the other long operations, preventing overlap; (6) **`DateTimeOffset` is parsed with `InvariantCulture`+`RoundtripKind`** to match the invariant `"O"` write (a non-Gregorian current culture could otherwise shift the year or throw and fail the whole library load); (7) corrected the `.iso` decision text (it suggests all four ISO systems incl. PlayStation) and locked it with an exact-set test. Also removed a dead `_systemsById` field and replaced the hand-rolled availability converter with Avalonia's `FuncValueConverter`.

Deferred as perf follow-ups (reported, not fixed — they only bite very large libraries): the availability sweep writes one autocommit `UPDATE` per changed row (a batch update in a single transaction would cut fsyncs), and after a single-system rescan it re-stats every system's games rather than just the rescanned one. Both are worth revisiting in M8's performance pass.

## 2026-07-12 — PS3 importing (M5) moved to the backlog

Per user decision, directory-based PS3 importing is deferred out of the v1 milestone sequence into `ROADMAP.md`'s Backlog. Milestone numbers M6–M8 are kept unchanged so existing references (the `/milestone` command's section map, prior decisions) don't shift; the working order becomes M4 → M6 → M7 → M8, with PS3 pulled from the backlog whenever chosen. It lands behind the M3 seams (`IGameImportRules`, the directory-aware `IAvailabilityChecker`) without reworking the shared scanner. Consequence: v1 as first shipped narrows the design doc's §14 definition (which lists PS3/RPCS3) — the PlayStation 3 sidebar entry stays empty and M6's RPCS3 launch path has nothing to exercise until PS3 import is done.

## 2026-07-12 — macOS: run via `dotnet run`, not the built binary directly

Symptom investigated: double-clicking / directly executing the built `EmuShelf.App` on macOS reports the app as broken. Cause: the build is framework-dependent, and .NET here lives at `$HOME/.dotnet` (user-local), which the apphost does not auto-discover (it searches `/usr/local/share/dotnet`, `DOTNET_ROOT`, etc.), so the host aborts with "You must install .NET to run this application" — surfaced by the OS as a broken app. The binary itself is fine (arm64, ad-hoc signed, not quarantined). Supported dev launch is `dotnet run --project src/EmuShelf.App` (or `DOTNET_ROOT=$HOME/.dotnet ./EmuShelf.App`). This is consistent with the existing decision that a signed, double-clickable macOS `.app` bundle is out of v1 scope (Windows-only release); no code change needed.

## 2026-07-13 — M4 file rules use relationship filtering and header-only Nintendo detection

The M3 extension stub was replaced by `FileImportRules`. `IGameImportRules.SelectGameEntries` is the batch-level seam used by both folder scans and multi-file adds: individually recognized paths are collected first, then `.cue` and `.m3u` references remove component entries. References resolve relative to the descriptor, accept either slash style, and compare case-insensitively to match the v1 Windows/macOS filesystem policy. A raw unreferenced `.bin` is accepted as a PS1 fallback; a `.bin` named by any scanned CUE is hidden, which gives the design document's CUE rule observable behavior without rejecting valid standalone raw images.

GameCube and Wii both expose the roadmap's full `.iso/.rvz/.wbfs/.gcm/.ciso` set; the extension never decides between them. The detector reads the Nintendo magic words from the logical disc header and understands the small uncompressed wrapper headers described by Dolphin's [`CISOBlob`](https://github.com/dolphin-emu/dolphin/blob/master/Source/Core/DiscIO/CISOBlob.h), [`WbfsBlob`](https://github.com/dolphin-emu/dolphin/blob/master/Source/Core/DiscIO/WbfsBlob.h), and [`WIABlob`](https://github.com/dolphin-emu/dolphin/blob/master/Source/Core/DiscIO/WIABlob.h) implementations. It does not decompress an image or take a Dolphin dependency. Invalid or unreadable Nintendo images are skipped instead of guessed; an unrecognized `.iso` remains eligible for PS1/PS2 confirmation because those formats do not share Nintendo's header.

## 2026-07-13 — M4 review fixes: single-pass explicit-file analysis and confirmed overrides

Review found that Add Files performed both suggestion and post-confirmation file reads synchronously on the UI thread, reopening Nintendo images and silently discarding explicit picks that could not be classified. `IGameImportRules.AnalyzeFile` now returns one reusable `GameFileAnalysis` per path with four outcomes per system: compatible, incompatible, supported-but-unrecognized, or unsupported. The complete batch analysis and later CUE/M3U entry selection run on worker threads. A definitive Nintendo header also rules out PS1/PS2 for the same `.iso`, rather than merely ranking Nintendo first.

For explicit files, the user's confirmed system is authoritative when the extension is supported but content recognition is inconclusive; definite mismatches and unsupported extensions are skipped with a count and reason in the status area. Folder discovery remains strict and skips inconclusive Nintendo images. Raw `.bin` support is narrowed from the previous entry: orphan BINs never appear during folder scans, but a BIN the user explicitly selected may be imported as PS1 after confirmation, and a simultaneously selected CUE still suppresses its referenced BIN tracks. The same review also removed redundant path canonicalization and made `ShowSystemAsync` await the reload triggered by changing systems, so an add command cannot complete before its destination library is populated.

## 2026-07-13 — M4 relationship suppression reconciles persisted library rows

Descriptor filtering cannot stop at the current scan batch: discs may already be in the database when a user later creates an M3U or adds its CUE. `GameEntrySelection` therefore carries both retained entry paths and resolved component paths through explicit imports and folder scans. `GameLibrary.ReconcileImport` applies both sides in one SQLite transaction: it inserts retained entries and deletes matching component rows only from the confirmed system. Suppression changes EmuShelf's database only and never modifies the referenced game files. Eligible components can be discovered again by a later scan if the descriptor is removed.

## 2026-07-13 — M6 launch templates produce argv directly and configuration stays per system

The M2 schema decision remains authoritative: emulator executable and argument settings are keyed by system, not emulator id. Dolphin supplies the shared integration defaults for both GameCube and Wii, while the two systems retain independent configuration rows. That slightly broadens the design document's "one installation per emulator" simplification, but preserves the already-migrated schema and lets portable users intentionally use different Dolphin builds or arguments without adding per-game overrides.

Argument templates are a deliberately small, shell-free language. Double quotes only group one argument; the documented `{GamePath}`, `{GameDirectory}`, `{GameFileName}`, and `{EmulatorDirectory}` placeholders are expanded after tokenization; malformed or unknown placeholders stop launch with status feedback. `TrackedProcessRunner` sets `UseShellExecute = false` and appends every result through `ProcessStartInfo.ArgumentList`, so spaces and shell metacharacters stay inside their argument instead of becoming executable syntax. The generic launch service performs all validation before minimizing, waits for the directly-started emulator process, and restores EmuShelf in `finally`, including start failures.

Default templates follow the emulators' documented command-line modes: DuckStation and PCSX2 use `-batch -- "{GamePath}"`, Dolphin uses `-b -e "{GamePath}"`, and RPCS3 uses `--no-gui "{GamePath}"`. These are editable per-system defaults rather than hard-coded launch behavior; actual Windows verification remains the final unchecked M6 roadmap item.

## 2026-07-13 — M6 review fixes: transactional settings and exact window restoration

The settings dialog saves its complete per-system configuration set through one SQLite transaction. A failed row now rolls back every row instead of leaving a partially-applied Save operation. The single-row `Save` API delegates to that same batch path so there is only one persistence implementation.

Frontend restoration records the window's state immediately before launch and restores that exact state after the emulator exits; maximized and fullscreen windows no longer return as normal windows. Configuration-load exceptions from opening Settings are caught at the main view-model command boundary and shown in the existing status area, matching the launch/import failure behavior instead of escaping through an async relay command.

## 2026-07-13 — M6 launch preflight runs off the UI thread

Launch preparation now performs the configuration-store read, game/executable existence probes, and argument-template expansion in one worker-thread operation before minimizing the frontend. Portable libraries and emulator paths commonly live on removable drives, so a slow drive wake-up or disconnect can no longer freeze the window while it is trying to show launch status. Frontend minimize/restore calls still resume on the captured UI context; a headless UI-thread test locks down both sides of that boundary.

## 2026-07-13 — M7 copies covers, caches bounded thumbnails, and keeps removal DB-only

Manual cover assignment never binds the library to the user's selected source image. `GameCoverService` copies supported PNG/JPEG/WebP/BMP files to `Covers/{GameId}.{ext}` and writes a high-quality, aspect-preserving thumbnail no larger than 300×400 to `Cache/Covers/{GameId}.png`; decode, scaling, and file I/O all run off the UI thread. `Games.CoverPath` goes through the existing relative-path resolver, so the copied cover survives moving the portable installation. Reassigning a cover removes only older EmuShelf-owned variants for that game, never the selected source. Thumbnail loading is driven by a tile/row entering the visual tree, not by a system reload, so virtualization also limits image I/O and decoding; generation/revision guards discard stale results after system switches or cover reassignment.

Removing a game deletes its `Games` row only. The confirmation explicitly says both the game file and copied cover remain on disk. Retaining copied covers follows the project's stronger DB-only removal rule and also avoids destructive cleanup when a user removes an entry accidentally; reclaiming orphaned covers can be a separate, explicit maintenance feature later.

Title edits use a compact per-game popover and persist through `IGameLibrary.UpdateTitle`; the in-memory record and placeholder initials update together, and the current system reloads so SQLite's title ordering remains reflected in the UI. Both grid tiles and list rows expose the same launch/edit/set-cover/remove context menu.

## 2026-07-13 — M7 review fix: cover assignment uses a staged DB switch

This supersedes the fixed `Covers/{GameId}.{ext}` filename described above. Each assignment now creates a versioned `Covers/{GameId}-{Version}.{ext}` file and a matching versioned cached thumbnail without replacing the current assets. Only after both staged files are valid does the app update `Games.CoverPath`; a failed database write discards the stage and leaves the previous cover intact. After a successful database switch, the old EmuShelf-owned cover and its thumbnail are deleted under the same per-game lock. Cleanup rejects paths outside `Covers/` and filenames that do not belong to that game, so the user's selected source image is never eligible for deletion.

Cover-command completions resolve the current game view model by database id rather than retaining the tile instance across awaits. This keeps the visible library correct when an availability refresh or system reload replaces its view models during image work. Thumbnail existence is also checked only after acquiring the per-game lock, and stale load failures are revision-guarded so an old request cannot overwrite the status of a newer assignment.

## 2026-07-13 — M6/M7 second-review fixes preserve UI responsiveness and committed cover state

The tracked process runner schedules the operating-system process creation itself on a worker thread, not only the preceding launch validation. `Process.Start` can synchronously wake or inspect an executable on removable storage, so the frontend UI thread is reserved for the intentional minimize and restore operations while the runner owns both off-thread start and asynchronous exit tracking.

Once a staged cover path has been written to SQLite, that database switch is the authoritative successful assignment. The current game view model adopts the new path and clears its old bitmap before preview loading; a missing or unreadable cache is regenerated through `GameCoverService` when possible and otherwise becomes a success warning rather than a false assignment failure. Previous owned-cover cleanup still runs after a preview failure, preventing the UI, database, and staged-file lifecycle from disagreeing about which cover is current.

## 2026-07-13 — Real-library verification expands PS2 importing to BIN/CUE

The supplied PCSX2 game is a CD image represented by a CUE descriptor and BIN payload, a format omitted by the original design document's short PS2 list. Real-library verification showed that this left a valid user game impossible to add, so the implemented format map deliberately expands beyond that list: PS2 CUE files are normal folder candidates and their referenced BIN tracks are suppressed exactly like PS1; standalone BIN files remain explicit-pick-only and require system confirmation because their contents do not distinguish PS1 from PS2. This preserves the no-junk folder rule while making the confirmed real game importable.

## 2026-07-13 — M8 uses OpenEmu's visual hierarchy without copying its assets

The official OpenEmu screenshots remain a visual reference, not an implementation source. EmuShelf now adopts the recognizable hierarchy that matters for a game library — a persistent system rail, compact integrated toolbar, quiet content surface, spacious cover-first grid, and strong selection feedback — while all XAML, badges, placeholder geometry, colors, and typography are original. The same theme resources style the emulator-settings and confirmation windows so the app reads as one product instead of a polished main window surrounding generic dialogs.

Appearance is a three-way portable preference: follow the operating system by default, or explicitly choose light or dark. The preference is saved through the existing `Settings/settings.json` service, and theme-aware dynamic resources update the active Avalonia window tree without restart. Grid tiles gained pointer selection despite using `ItemsRepeater`; list selection remains native `ListBox` behavior. Keyboard navigation for the virtualized cover grid is still a future accessibility enhancement rather than being simulated with non-virtualized controls.

## 2026-07-13 — Licensed OpenEmu artwork supersedes the asset-free M8 policy

At the user's direction, the preceding M8 decision's no-asset restriction is superseded for
artwork whose redistribution terms have been verified. EmuShelf now bundles OpenEmu's small
platform-library and collection icons under the supplied BSD 2-Clause license. The complete
license and `THIRD-PARTY-NOTICES.md` are copied beside every build, crediting the OpenEmu Team
and contributors; the source assets also retain their license in the repository. No OpenEmu
code, branding, emulator cores, controller illustrations, or game artwork is imported.

The platform resolver uses stable EmuShelf system ids and caches the tiny shared bitmaps for
the application lifetime. It includes the current PS1/PS2/PS3/GameCube/Wii navigation plus
available future-facing artwork for DS, PSP, Nintendo, Sega, Atari, and other systems. The
supplied catalog has no dedicated PS3 or PS4 library icon, so those ids deliberately use the
nearest licensed PlayStation-family image until suitable specifically licensed art is added;
EmuShelf does not fabricate an upstream asset. The library UI also gains functional All Games
and Recently Added scopes so the new collection presentation is behavior, not decoration.

## 2026-07-13 — Visual QA replaces persistent chrome with contextual controls

Real-window screenshots exposed defects hidden by the initial homogeneous PS1 snapshot. Mixed
collections no longer let each tile choose its layout-row height: every tile owns a fixed
250-point artwork slot, with square PS1/GameCube and portrait PS2/Wii covers bottom-aligned
inside it, so titles and format labels share one baseline. An unavailable game is labelled
"File missing" with an explanation instead of the ambiguous "Offline".

The OpenEmu navigation icons are `@2x` pixel assets intended for roughly half their bitmap
dimensions in logical points; the sidebar now renders them at that scale instead of enlarging
them into coarse pixel art. OpenEmu's supplied PS2 image is visually almost identical to its
PS1 image, so EmuShelf composes a small generation badge for PS2/PS3/PS4 rather than pretending
a different upstream asset exists.

The toolbar now contains only view selection, a collapsed magnifier that expands into search,
appearance, and Settings. Rescan-current and rescan-all are library-maintenance actions at the
top of Settings. The permanent status bar and its meaningless idle "Ready" message are removed;
operation progress/results appear as a dismissible contextual notification only when present.

## 2026-07-13 — Settings scales by platform with local maintenance actions

Emulator configuration is presented as one collapsible section per platform, with the first
section expanded by default and the licensed platform artwork used in each header. This keeps
Settings navigable as more systems and options arrive without splitting closely related launch
and library controls across separate screens.

Each platform section owns a contextual "Rescan library" action, while the top-level maintenance
card retains only the global rescan. The Settings-to-library contract identifies a system by its
stable id instead of capturing whichever sidebar selection opened the dialog. A rescan preserves
the user's current main-library scope, and collection scopes are explicitly reloaded after scan
reconciliation so newly inserted or suppressed entries appear immediately.

## 2026-07-13 — M8 performance work is bounded by database and realization snapshots

Large-library work is reduced at the two boundaries that previously amplified it. SQLite schema
version 2 adds an indexed UTC-millisecond `DateAddedUnixMilliseconds` value so Recently Added asks
the database for only its newest 30 rows instead of loading and sorting the entire library. The
availability pass still stats known paths off the UI thread, but writes every changed flag in one
transaction. Search and reload results replace the bound collection with one reset notification,
and game presentation view models are assembled on a worker thread after shared platform artwork
has been resolved.

Cover work remains driven by realized controls. While an emulator owns the foreground, new cover
requests and completed cover UI assignments are deferred by game id; only those realized requests
are replayed after the frontend restores. The Windows CI publish is self-contained and ReadyToRun
to improve cold startup without trimming Avalonia's reflection-dependent UI.

## 2026-07-13 — Portable daily logs are diagnostic and never a new failure mode

Operational warnings, caught failures, startup problems, and unhandled process/UI/task exceptions
are written to `Logs/EmuShelf-YYYY-MM-DD.log` beside the executable. The logger serializes concurrent
appends and includes full exception details, but deliberately swallows its own directory and file
errors: a read-only or disconnected portable drive must not replace the original user-facing error
with a logging crash. Contextual UI messages remain concise; logs carry the diagnostic detail.

## 2026-07-13 — Windows artifacts and validation are separate completion gates

Every CI run builds and tests on Windows and macOS, then the Windows job produces a self-contained
`EmuShelf-win-x64.zip` plus SHA-256 checksum after verifying that the executable, third-party notice,
and OpenEmu BSD license are present. Producing the package is automated; accepting it on a real
Windows machine remains a manual roadmap item tracked by `docs/windows-test-checklist.md`.

That checklist explicitly reflects the earlier PS3 scope reduction. DuckStation, PCSX2, and both
Dolphin systems are current launch gates; RPCS3 import/launch cannot honestly satisfy the design
document's original five-system section 14 until PS3 importing returns from the backlog.

## 2026-07-13 — RetroAchievements is an EmuShelf-owned, read-only display integration

External emulators remain the only components allowed to monitor memory, unlock achievements,
or submit data to RetroAchievements. EmuShelf will use the read-only Web API to display whether
the exact local game image has an achievement set and to cache the connected user's progress.
It will not import emulator credentials or scrape emulator settings, logs, or private caches.

DuckStation, PCSX2, and Dolphin already calculate RetroAchievements hashes and game ids inside
their own processes, but they do not expose a stable cross-process CLI or IPC contract for a
frontend. EmuShelf therefore owns local identification behind a Core interface: calculate the
canonical RA hash, look it up in an aggressively cached achievement-bearing hash catalogue, and
persist the resulting local-game-to-RA-game link. Title or filename matching is not accepted
because regions, revisions, hacks, and incompatible hashes may map to different support.

The initial system scope is PlayStation, PlayStation 2, GameCube, and Wii (RA console ids 12, 21,
16, and 19). RetroAchievements does not define a PlayStation 3 console id. Format coverage is a
feasibility gate rather than an assumption: the official [`rcheevos`](https://github.com/RetroAchievements/rcheevos)
hash code provides the algorithms and reader callbacks, while compressed CHD/RVZ/WBFS/CSO/PBP
media may require logical-disc adapters equivalent to those used inside the emulators. An
unverified or failed format remains `Unknown`; it is never reported as having no achievements.

Refresh is deliberately event- and cache-driven. Static game/hash catalogues have a seven-day
TTL, account progress summaries have a 15-minute startup TTL, popup details have a five-minute
TTL, and the launched game is refreshed once shortly after its tracked emulator exits. There is
no gameplay polling. A single paced request coordinator honors `Retry-After` and retains stale
cache data on network or server failures, following RetroAchievements' official guidance to
cache static data and keep API use reasonable.

## 2026-07-13 — Game metadata enrichment is exact, opt-in, and provider-composed

Library scanning remains entirely local and commits new games before any network prompt or work.
The first successful import offers `Not now`, `Fetch once`, and `Always after import`; automatic
fetching is off by default and can later be changed in Settings. Explicit per-platform and
all-library fetch actions are also available. EmuShelf bundles only provider endpoint knowledge,
not game artwork or a metadata catalogue. Requested DAT files live under `Cache/Metadata/`, while
accepted cover files enter the portable `Covers/` store and its existing thumbnail cache.

Identification, catalogue matching, artwork URL construction, downloading, and persistence are
separate interfaces. A platform profile composes one typed, read-only identifier extractor with a
catalogue key and an ordered list of artwork providers. PS1/PS2 use normalized disc product codes;
GameCube/Wii use six-character disc ids. Matching is exact: filename or fuzzy title similarity is
not evidence. Compressed PlayStation containers without a logical-disc reader may use only a
product code explicitly present in their filename and otherwise remain unmatched.

The database records extracted evidence, canonical-match provenance, provider/source URI, attempt
status, and whether each displayed title or cover came from a filename, download, embedded data,
or the user. Catalogue titles may replace filename-derived or previous catalogue titles only;
downloaded covers may fill an empty cover only. Existing pre-migration covers are classified as
user-owned. Manual edits always win, library identity remains the local path, and no provider
failure can change or remove a game entry.

## 2026-07-13 — M10 pins canonical hashing to a reviewed rcheevos baseline

Local RetroAchievements identification is implemented behind Core interfaces as a read-only C#
compatibility layer pinned to `rcheevos` commit
`2ac45d357bce2906bb0f1438f3eaf8ce6e78e3c4`. A native rcheevos binary is not bundled: doing so
would add a native build and deployment matrix to both Windows and macOS before its compressed
disc callbacks solve the actual format problem. The upstream algorithms and fixture shapes are
MIT-licensed; EmuShelf ships that license and credits RetroAchievements in
`THIRD-PARTY-NOTICES.md`.

The first implemented format scope is deliberately narrow: PlayStation and PlayStation 2
cooked ISO/BIN, ordinary CUE/BIN with 2048- or 2352-byte sectors, M3U playlists whose first game
entry resolves to one of those media, and GameCube ISO/GCM. CHD, CSO, PBP, RVZ, WBFS, CISO, all
Wii images, and PlayStation 3 are explicit `UnsupportedFormat`/unknown results. They never fall
back to whole-file MD5 or filename/title matching. Expanding that set requires a logical-disc
reader and an exact parity fixture for the container.

Schema version 4 stores the calculated hash separately from a future catalogue match, along with
the pinned algorithm version, attempt status, and a SHA-256 fingerprint of path, size, modified
time, and selected CUE/M3U dependencies. Re-identification clears any old catalogue resolution.
A single serialized worker reuses unchanged hashed, unsupported, or invalid-media results;
temporarily unreadable media is eligible for retry. The worker is composed at startup but is not
run as a full-library startup pass.

## 2026-07-16 — M11 Phase 1 makes PlayStation matching targeted, cached, and staged

The first enrichment implementation (M9) re-derived each PlayStation serial by scanning up to
32 MiB of every disc file — with no early exit — on every enrichment pass, at a two-wide gate that
made cover downloads wait behind disc reads. That is why matching was slow and incomplete relative
to DuckStation/PCSX2, which read the serial from a known location (or decode the container) and
fan cover downloads out widely.

Phase 1 replaces the scan with a targeted `SYSTEM.CNF` read that reuses the `CdSectorReader` and
ISO9660 walk already written for RetroAchievements hashing. The shared directory lookup was
extracted into `Iso9660Directory` so both the hasher and the new `PlayStationDiscSerialReader` use
one implementation rather than a second, weaker scanner. A bounded, early-exit ASCII fallback and
the existing filename fallback remain for images with no readable layout; compressed containers
(CHD/CSO/ZSO/PBP) still return no serial here and are deferred to Phases 2–4. Extracted identifiers
are now reused from the database (`IGameMetadataStore.GetIdentifiers`); a re-run never re-reads a
disc whose serial is already known. Enrichment is split into a disk-bound identification stage and
a network-bound download stage with independent concurrency, and the metadata `HttpClient` uses a
pooled handler with a raised per-server connection limit so many small covers download at once.

This changes performance and internal structure only. Identity is still the local path, matching is
still exact, manual edits still win, and no provider failure changes a game entry.

## 2026-07-16 — M11 Phase 2 reads the PBP serial from PARAM.SFO, not the disc image

A PlayStation EBOOT (`.pbp`) carries its serial twice: in the uncompressed embedded PARAM.SFO
(`DISC_ID`) and inside the compressed PS1 disc image in DATA.PSAR. EmuShelf reads `DISC_ID`. It is a
small, targeted, uncompressed read that reuses the existing serial normalizer, and it avoids
decompressing the PSAR disc image purely to recover a code the header already states. A PBP with a
missing, malformed, or non-string `DISC_ID` falls back to the filename serial, consistent with the
other containers. The disc image inside DATA.PSAR is never read or modified.

## 2026-07-16 — M11 Phase 3 decompresses CSO/ZSO on demand behind one sector reader

CSO (`.cso`, deflate) and ZSO (`.zso`, lz4) share the CSOv1 header and block index; only the block
codec differs. Rather than add a second SYSTEM.CNF parser, both are exposed through a shared
`ILogicalSectorReader` — the same interface the raw `CdSectorReader` implements — so the existing
ISO9660 walk and boot-serial reader are container-agnostic. `CompressedIsoSectorSource`
decompresses only the blocks that back the sectors the walk actually reads (PVD, root directory,
SYSTEM.CNF), never the whole image, and caches the last block.

CSO blocks use raw deflate, decoded with the framework `DeflateStream`. ZSO blocks use the LZ4
block format, for which .NET has no built-in decoder. EmuShelf ships a small hand-rolled LZ4 block
decoder (~60 lines) instead of taking a NuGet dependency: only a few early blocks are ever decoded,
the block format is stable and simple, and avoiding a native/third-party compression dependency
keeps the portable build and its license surface unchanged. The decoder is bounded and returns
false on any malformed input, falling back to the filename serial. Only CSOv1/ZSO v1 are accepted;
other versions and unknown magic fall back rather than guess.

## 2026-07-17 — M11 Phase 4 ports a bounded CHD reader, verified against chdman

CHD is the last and hardest container. A correct reader needs the compressed v5 hunk map
(MAME's canonical Huffman coding), a crc16 self-check, per-hunk `zlib`/`lzma` decode, and — for
CD geometry — `cdzl`/`cdlz` with frame reassembly. There is no maintained, permissively licensed
pure-C# CHD library (RomVault/CHDSharp ships without any license), so EmuShelf ports the format
from MAME/libchdr (BSD-3-Clause) and vendors a minimal LZMA decoder derived from the public-domain
LZMA SDK rather than taking a native dependency (which the M10 RetroAchievements decision already
rejected for the same portability reasons). Both are credited in `THIRD-PARTY-NOTICES.md`.

Because a from-scratch decoder is unverifiable by inspection, correctness was established against
`chdman` 0.288: `createdvd`/`createcd` produce reference vectors and `extractcd` produces the
expected bytes. The committed CI fixtures are tiny DVD `zlib`/`lzma` CHDs that decode byte-for-byte
to the source ISO and yield the boot serial. The CD path (`cdzl`/`cdlz` + Mode 2 Form 1 frame
reassembly) was verified byte-for-byte against a real CD CHD (20k frames) and the map crc matched
on real CD and DVD CHDs; an opt-in test (`EMUSHELF_TEST_CHD_DIR`) re-runs that check on real files.

Scope and safety: the reader only decompresses the few hunks that back SYSTEM.CNF, never the whole
image; it regenerates no sync/ECC (the 2048 user bytes it returns do not depend on them); and
`huff`, `flac`, and `cdfl` (audio) hunks are unsupported and fall back to the filename serial. The
crc16 map self-check makes a mis-decode fail closed rather than return a wrong serial. Reads are
bounded and never modify the source file.

## 2026-07-17 — M11 Phase 5 adds an id-addressed GameCube/Wii cover route

GameCube and Wii covers were previously title-addressed only (Libretro `Named_Boxarts`), which
needs an exact catalog-title match against filenames derived from a different naming scheme and so
404s often. Phase 5 adds `GameTdbArtworkProvider`, keyed by the six-character disc id the Nintendo
extractor already produces, ordered before the Libretro title fallback. It uses GameTDB — the same
community source Dolphin uses — at `https://art.gametdb.com/wii/cover/<region>/<id>.png`; the
`/wii/` path serves GameCube covers too. The disc id's fourth character selects the region/language
folder using Dolphin's mapping (E→US, J→JA, W→ZH, K→KO, PAL→DE/FR/ES/IT/NL/EN), and `EN` then `US`
are tried as fallbacks because a given cover may exist under only one folder. Because it is
id-addressed, it succeeds without a catalog title match. GameTDB is recorded in
`THIRD-PARTY-NOTICES.md` as an opt-in, not-distributed source subject to publisher rights.

## 2026-07-17 — Covers shrink-wrap their art on a shared shelf

Cover framing was hardcoded in the view model as two buckets: `playstation` and `gamecube`
got a 1:1 square, everything else a 188×250 portrait. GameCube was misfiled (its retail art is
portrait disc-case), and — more fundamentally — any fixed frame ratio letterboxes covers whose
scanned art doesn't match it. Measuring the actual downloaded covers settled the real ratios:
PlayStation jewel-case scans are square (500×500 → 1.0); disc-case scans (PS2/PS3/GameCube/Wii)
are ~0.70 (512×723, 512×736…), not the 0.752 the frame assumed — hence the grey bands on the
left/right of PS2 covers.

An earlier revision let each cover shrink-wrap to its own bitmap, which removed letterbox but made
same-platform covers different sizes (scans vary ~±1%), and the placeholder — sized off a nominal
ratio — didn't match. OpenEmu itself preserves real ratios (aspect-fit, bottom-aligned) and only
looks uniform because its art comes from one curated source (OpenVGDB); EmuShelf pulls from mixed
providers, so that drift shows. The fix is to **formalize one canonical frame per platform** and
draw every cover into it.

`GameSystem.CoverAspectRatio` (width÷height) is that canonical frame, applied to the real cover
*and* the placeholder so a system's covers are all identical in size: PlayStation 1.0 (square
jewel case), disc systems 0.708 (the measured mode of the scans, ≈ the physical DVD/BD case, and a
correction of the original 0.752 that caused the first letterbox report). `GameViewModel` sets a
fixed 188×round(188÷ratio) frame; PS2/PS3/GameCube/Wii → 188×266, PlayStation → 188×188.

Real covers **fill** the frame (`Stretch="UniformToFill"`, clipped to the rounded card). Because
the frame equals the platform's true scan ratio, fill crops at most ~2px of outer bleed on an
off-ratio scan — never a visible letterbox and never the meaningful crop the wrong-ratio 0.752
frame once produced (square art forced into portrait). Frames bottom-align on a fixed 266-tall
shelf so a square PlayStation cover shares a baseline with taller disc covers and the titles below
line up; the 1px `EmuCoverBorderBrush` frame and 3px accent selection border stay. Grid
`MinItemHeight` is 326 / `MinRowSpacing` 20 for the 266 shelf plus title/format rows. A new
platform adds a `KnownSystems` row with its canonical ratio; if it is taller than the current disc
frame (e.g. a PSP UMD at ~0.6 → ~313 tall) the shelf height must grow to match.

## 2026-07-17 — Cover repos fetched via jsDelivr; downloader retries throttling

Enriching a large library (≈100 PS2 ROMs) downloaded no covers, while PCSX2 pulls hundreds fine.
Cause: the xlenore cover repos were fetched from `raw.githubusercontent.com`, which now enforces a
per-IP anonymous rate limit (tightened by GitHub in 2025). A burst of ~100 cover requests at 12-way
concurrency trips it, GitHub returns HTTP 429, and `RemoteArtworkDownloader` treated any non-success
(including 429) as a permanent skip — so the whole batch produced nothing. The Libretro DAT catalog
was not the culprit: it fetches one file per system, cached 30 days.

Two fixes: (1) cover repos are now fetched through the **jsDelivr CDN**
(`https://cdn.jsdelivr.net/gh/<user>/<repo>@main/…`) instead of GitHub raw — jsDelivr is built to
serve repo files in bulk and isn't subject to that per-IP limit (verified: valid PS2/PSX serials
return 200 image/jpeg). (2) `RemoteArtworkDownloader` now **retries HTTP 429/503** with a short,
`Retry-After`-aware backoff capped at 5s (max 3 attempts), so transient throttling from any host no
longer drops a cover. Download concurrency (12) is unchanged; jsDelivr handles it.

## 2026-07-17 — OpenVGDB is deferred until a cartridge system is added

OpenVGDB (the box-art source OpenEmu uses) was considered as the default cover source with the
current providers as fallback. It fits the `IGameMetadataCatalog` + ordered `IGameArtworkProvider`
model cleanly, but it is the wrong tool for the current disc-only lineup: it keys on CRC32/MD5/SHA1
of dumped ROMs (built for cartridge systems), which do not match our compressed disc formats
(CHD/CUE, RVZ) without decompressing and hashing whole discs; its `releaseCoverFront` URLs point to
third-party hosts that now block hotlinking or are dead (OpenEmu mirrors them); and the SQLite is
tens of MB with third-party art whose redistribution terms need clearing. For PS1/PS2/GC/Wii the
existing serial-addressed Redump matching plus serial/disc-id art (xlenore, GameTDB) and the
Libretro title fallback are a better, lighter fit. OpenVGDB becomes worthwhile when the first
cartridge system (SNES/NES/Genesis) is added — the case OpenEmu actually uses it for — at which
point it wires in as the primary catalog and first artwork provider with today's providers behind
it, and the DB-distribution + licensing questions get settled then.

## 2026-07-18 — M10 §2: read-only Web API client with platform credential storage

The RetroAchievements integration authenticates with a username and a **Web API key** (Web API
query keys `z` and `y`), which is credential setup, not a password login, and never reuses an
emulator's stored token. The key is the only secret. It is held behind a Core abstraction
(`IRetroAchievementsCredentialStore`) and never written to `settings.json`, diagnostics, exception
text, or a logged request URI — a test asserts the key is sent in the query yet absent from every
log message. The non-secret identity (username plus the returned stable ULID) lives in
`settings.json`; only that pairing is persisted, so a lost key means reconnect, not data loss.

Storage is platform-specific. Windows (the v1 ship target) uses a DPAPI-protected blob under
portable `Settings/`, encrypted for the current user via `crypt32` `CryptProtectData` reached
through P/Invoke — chosen over the `System.Security.Cryptography.ProtectedData` package to avoid a
new dependency and keep the build self-contained. macOS development uses a session-only in-memory
store because there is no verified portable at-rest protection there yet; it never persists the
secret, so a reconnect is required after each restart. Both sit behind the same Core interface, so
Core stays platform-agnostic and the choice is a single OS-keyed factory.

The `IRetroAchievementsClient` exposes only the read endpoints EmuShelf needs (profile validation,
per-console game/hash catalogue, and batched user progress) and maps authentication, offline,
malformed-response, 429 (with `Retry-After`), and server failures to distinct result states so
callers retain usable cached data and never auto-retry an authentication failure. The pieces are
built and unit-tested; wiring the account service into the app and the Settings connect card land
with the library-presentation UI slice, since that is their first consumer.

## 2026-07-18 — M10 §4 backfills only on an explicit account connection

The initial Stage 4 wiring matched only games that had already been identified at import time. A
library created before RetroAchievements was enabled therefore produced a successful connection
followed by a misleading zero-game match pass. The account connection is now the explicit,
user-approved backfill event: it reads every library entry once through the cached identification
service, then resolves hashes, refreshes progress, and reloads the display. It is deliberately not
a startup scan; unchanged terminal records still avoid reopening their game image.

Identification, catalogue matching, and progress refresh run as one serialized pipeline. New
imports join that pipeline only while an account has a usable key, so a user who never enables
RetroAchievements does not incur disc I/O, and a game that finishes hashing cannot be stranded
without a later catalogue match. Settings receives typed phase progress and displays the currently
identified/matched game plus a determinate bar. Matching/progress failures do not disconnect a
validated account; cached data remains visible and the final status says which follow-up work was
unavailable.

On macOS, the persisted non-secret identity is not a connection: the Web API key remains
session-only. Settings now shows a reconnect-required form after restart instead of a false
connected state. The returned RA ULID is used for account progress requests, while the current
username remains the authenticated identity, so a username rename cannot break cached-account
progress refreshes.

## 2026-07-18 — M10 §4 disconnect is serialized with account-scoped sync work

`RetroAchievementProgress` has no account id by design and is cleared on disconnect. An
import-triggered background sync can otherwise capture credentials, then complete after the clear
and repopulate the disconnected account's progress. Disconnect now acquires the same pipeline lock
as identification/matching/progress, waits for in-flight work to finish, clears the cache, and only
then releases queued work. A queued import rechecks that an account is still connected after
acquiring the lock, so it does not read media or write account data after disconnect. A regression
test holds a progress refresh in flight and proves the cache is cleared only after it completes.

## 2026-07-18 — Expansion uses an RPCS3-owned PS3 list and a core-aware RetroArch launcher

The next platform expansion supersedes the deferred M5 plan to recursively discover PS3 game
directories. PlayStation 3 discovery is instead an explicit, read-only synchronization from the
user-selected RPCS3 data/config location. Only entries already known to RPCS3 may enter
EmuShelf; the integration may read targeted metadata from such an entry to validate or enrich it,
but must never promote an arbitrary directory into a PS3 game. The adapter is versioned and fails
closed if RPCS3's game-list format is unsupported. It neither writes RPCS3 files nor deletes a
user's EmuShelf row when an entry later disappears from RPCS3.

RetroArch remains an external launcher, not an emulator-core frontend. It is configured once as a
shared executable, with one manually chosen installed core file per supported system (Mega Drive /
Genesis, Nintendo DS, and Game Boy Advance). EmuShelf launches the configured core explicitly with
the game path so its behavior is deterministic when multiple cores accept an extension. It does
not download/update cores, choose one per game, enumerate an installation, or change RetroArch's
core options, overrides, playlists, credentials, or achievement settings. That narrow core-path
setting is necessary because the supported command-line launch form requires both a core and
content path; it is deliberately not a rich core-selection UI.

Mega Drive and Genesis share one EmuShelf system/sidebar category rather than duplicating the same
catalogue by regional name. PS3 remains outside RetroAchievements; PSP and the three new
RetroArch systems join it only after their exact local identification is proven against the
pinned hashing baseline. Covers use the existing opt-in, exact-match, ownership-preserving
metadata pipeline; provider and licensing choices are feasibility gates, not assumptions.

## 2026-07-18 — Nintendo RetroAchievements containers are read as logical discs

RetroAchievements identification now opens GameCube and Wii `.ciso`, `.wbfs`, and Dolphin `.rvz`
files through a shared, read-only logical-disc reader rather than treating a container as a raw
file. CISO sparse blocks and WBFS mappings return their logical zeroes; RVZ validates its headers
and metadata hashes, supports the standard uncompressed and Zstandard chunk codecs, and decodes
RVZ packed padding. Wii RVZ partition chunks are restored to the encrypted sectors consumed by
the pinned rcheevos algorithm: the reader regenerates hash blocks from decrypted data, applies
stored hash exceptions, and encrypts the clusters with the partition key. This keeps the existing
algorithm byte-oriented and does not modify source media.

`ZstdSharp.Port` 0.8.8 is the deliberately managed, MIT-licensed dependency for RVZ Zstandard
chunks. It avoids adding a native deployment dependency to the macOS/Windows application. RVZ
files using other codecs (bzip2/LZMA/LZMA2), as well as already-decrypted Wii RVZ images, remain
explicitly unsupported until their logical bytes have matching fixtures. Every malformed/
unsupported container stays `Unknown`; there is no whole-file-MD5 or filename fallback.

## 2026-07-18 — RetroAchievements cache versions follow the affected disc reader

The earlier global `rcheevos-2ac45d3-disc-v2` cache version was too coarse: an improvement to one
container reader caused every unrelated disc to be hashed again. Identification now records a
PlayStation-family or Nintendo reader version and treats the existing global-v2 successful hashes
as compatible during migration. A legacy invalid result is retried, because it may have been
caused by a now-fixed reader; a current-version result remains cached. This preserves valid PS1,
PS2, GameCube, and Wii work while allowing an affected reader to advance independently.

The PlayStation reader advances to v3 for CD CHD. CHD CD hunks can contain cooked user data rather
than raw sectors, so the 16/24-byte CD header is skipped only when the sync pattern is actually
present. `cdfl` uses a managed Shamisen FLAC decoder to reconstruct its sector payload, including
the byte order that libchdr restores on little-endian hosts. This adds no native deployment
dependency; the package and its MIT/BSD notices ship with the app. A real `chdman`-generated
`cdfl` fixture proves the recovered `SYSTEM.CNF` bytes.

## 2026-07-18 — M10 §5 caches game detail in SQLite and public badges separately

The compact achievement window uses the current official
`API_GetGameInfoAndUserProgress` endpoint with `u` (the stored stable ULID when available),
`g` (the resolved RA game id), and `a=1`. The award-metadata flag is essential: it returns each
achievement's earned and hardcore-earned timestamps, along with display order, badge id, title,
description, and points. The existing Web API key remains only in the request URI constructed in
memory; logs contain the endpoint and result only, and macOS continues to hold that key
session-only.

Full detail is account-scoped just like progress, so it lives in schema-v6 SQLite tables and is
cleared on disconnect. A generation guard prevents an old in-flight detail request from
repopulating the cache after disconnect/account switch. The popup reads this cache before opening,
then refreshes on a five-minute TTL or a manual Refresh action. Its primary fraction and earned
points are derived from *any* earned timestamp; `DateEarnedHardcore` adds a Hardcore marker rather
than excluding softcore awards. This keeps a useful offline view without polling while an emulator
is running.

Badge images are public, unauthenticated PNGs fetched only when the detail list needs them. They
are atomically cached under portable `Cache/RetroAchievements/Badges/`, coalesced per badge,
limited to four downloads at once, and bounded to 750 entries / 96 MiB by least-recently-used
access time. A failed, invalid, missing, or evicted badge leaves the local placeholder visible;
no badge fetch or cache write can touch ROMs, emulator configuration, or RA account state.

## 2026-07-18 — Achievement rows reserve a fixed metadata column

Achievement descriptions vary wildly in length, and Avalonia's vertically scrolling content can
otherwise measure a wrapping row beyond the popup's visible width. The achievement list therefore
disables horizontal scrolling and reserves an 88-logical-pixel trailing column for points and the
lock/softcore/hardcore pill; title and description occupy the remaining constrained column and
truncate after the intended visual bounds. This prevents the trailing metadata from escaping under
the scrollbar/window edge while keeping badge, title, description, earned date, and state readable
at the popup's minimum width.

## 2026-07-18 — Correction: reserve 112 pixels and bind rows to the scroll viewport

The first rail pass used 88 logical pixels, but a vertical `StackPanel` can measure item templates
at their preferred width; long descriptions then made individual cards wider despite the fixed
rail. The final layout binds the `ItemsControl` width to the scroll viewport (horizontal scrolling
remains disabled) and reserves 112 logical pixels for the rail. The card itself is 108 logical
pixels tall: points are pinned in the rail's top cell and the state pill in its bottom cell, while
description is capped at two lines in the independent centre column. A headless test supplies a
deliberately long description and asserts every rail has the same width and x-position.

## 2026-07-18 — Correction: achievement cards use three fixed visual regions

The 112-pixel trailing rail made the facts technically stable, but did not give the achievement
card a clear visual hierarchy and its combination with `ScrollViewer` padding could clip the
outer card edges. The final compact card instead follows the established console-list pattern:
an 88-pixel bordered badge bay, a flexible description field, and a 152-pixel bordered
reward/status dock. The dock contains separate, vertically centred `REWARD` and `STATUS` cells;
the point value and locked/softcore/hardcore label cannot move when the description wraps.

The scroll viewport owns the list width with no internal padding; individual cards take an
external 16-pixel horizontal margin. This preserves visible left and right card borders while
keeping the fixed dock inside the viewport. The dark-theme headless test verifies all cards are
inset, every reward dock has the same width and x-position, and a deliberately long description
does not change either property.

## 2026-07-19 — M10 §6 serializes API reads and refreshes only at bounded moments

`RetroAchievementsRequestCoordinator` is the single wrapper around authenticated Web API reads.
It permits one in-flight request across profile, catalogue, summary-progress, and game-detail
endpoints; equal in-flight requests share their task. Automatic work is paced at least one second
apart. A manual request may bypass that pacing, but never a server cooldown. `Retry-After` is a
lower bound (with positive jitter added), while a 429 without that header and 5xx responses use a
bounded exponential backoff with jitter. The coordinator does not retry any result itself, so an
authentication failure cannot turn into automatic credential traffic. Public badge-image downloads
remain under their separate bounded cache because they are unauthenticated image assets rather
than Web API reads.

Schema v7 adds a one-row, account-scoped `RetroAchievementProgressSync` marker. It is written
only after all linked progress batches succeed and is cleared with progress at disconnect; this
distinguishes a complete summary sync from partial per-game rows after a failed batch. Startup
consults that marker and makes no progress call until it is more than fifteen minutes old.

After a process has actually been tracked to exit, EmuShelf waits eight seconds for the external
emulator's own submission to settle, then performs one automatic full-detail request for the
launched RA game. That result updates the summary cache and notifies an open achievement window;
there is no timer or gameplay polling. The existing popup Refresh button remains the explicit
manual path.

## 2026-07-19 — M12 separates executable installations from per-system launch settings

An emulator installation is now a portable, named record containing one executable path; every
system still owns its launch template and, for RetroArch systems, a manually selected core path.
This lets Mega Drive / Genesis, Nintendo DS, and Game Boy Advance share exactly one RetroArch
executable while invoking an explicit different core for each system. The `{CorePath}` template
placeholder is expanded into the same argv array as `{GamePath}`, and both a missing core setting
and a missing core file fail before EmuShelf minimizes. The settings picker selects one existing
file only; EmuShelf never discovers, downloads, updates, or alters RetroArch cores or settings.

Schema-v8 migrates every pre-existing system configuration into a private named installation,
including GameCube and Wii's old separate Dolphin paths. That preserves prior behavior exactly;
only deliberately shared mappings (the new RetroArch default) use a common installation id. All
installation and core paths use the existing relative-path resolver, so an EmuShelf/RetroArch/core
bundle can move together on a portable drive.

PSP, Mega Drive / Genesis, Nintendo DS, and Game Boy Advance are now stable navigation and
configuration systems with the existing licensed platform icons and portrait cover frames. Their
file discovery remains disabled until M14/M16/M17/M18 establishes each format's exact read-only
import and identity contract; merely displaying a platform never turns an arbitrary file into a
game entry.

## 2026-07-19 — library.db opens without connection pooling

`LibraryDatabase.CreateConnection` sets `Pooling = false` on the Microsoft.Data.Sqlite
connection string. EmuShelf is portable: the `Data/` folder must be safe to move, back up, or
sync while the app is idle, but the default connection pool keeps the OS handle on `library.db`
open between operations. That is harmless on macOS/Linux (open files can still be unlinked) but
on Windows it blocks moving or deleting the folder — the portable-relocation behavior the app
promises. Pooling saves only microseconds for this occasional-write desktop workload, so
disabling it to release the handle deterministically is the right trade. This also let the full
`dotnet test` suite run green on Windows (the file-locked teardown/relocation failures were the
first thing the macOS→Windows source copy surfaced).

Test-harness portability was hardened at the same time, all test-only: `ChdSectorSourceTests`
now resolves `chdman` through a real PATH lookup instead of returning the bare command name, so a
machine without chdman skips the opt-in test cleanly rather than failing `Process.Start`;
`TrackedProcessRunnerTests` launches a shell `exit 0` instead of `Environment.ProcessPath
--version`, which is not a launchable dotnet muxer under the Windows `testhost.exe` apphost; and
`MainViewModelTests.Dispose` retries the temp-tree delete, because the view model's constructor
starts a fire-and-forget `ReloadGamesAsync` whose background `library.db` read can outlive a very
fast test.

## 2026-07-19 — M12 external library sources reconcile by source entry, never deletion

The generic `IExternalLibrarySource` contract reads a user-selected emulator catalogue only on an
explicit sync. Its reader must return the source's own stable entry id, path, title, and
availability; it has no folder-walk API and no write operation. SQLite schema-v9 stores the source
definition and a source-id/source-entry-id provenance pair on imported games. A refresh first
marks that source's existing entries unavailable, then reactivates or updates only entries returned
by the source. No row is deleted, and local/manual rows are outside that reconciliation.

An external title is treated as embedded metadata: it can replace filename, legacy, or previous
embedded presentation, but not a user-edited title. A source move retains the same game row through
the stable external entry id; a path collision with an unrelated local row fails closed rather than
silently claiming the local row. The read completes before the database transaction begins, so a
cancelled or unsupported adapter imports nothing. M13 will supply the RPCS3-specific, versioned
read-only adapter and its explicit Sync action on top of this contract.

## 2026-07-19 — M13 accepts only RPCS3's explicit `games.yml` title-id map

RPCS3's current source writes `games.yml` as a top-level mapping from an exact nine-character
title id to a game root. EmuShelf's version-1 adapter therefore reads only `games.yml` in the
configuration directory deliberately selected for each sync; it neither auto-detects RPCS3 nor
walks PS3 folders. It rejects every other YAML shape, invalid title id, or relative path before
the source reconciliation starts, with an actionable error and no import. The adapter opens its
list and optional direct `PARAM.SFO`/`PS3_GAME/PARAM.SFO` only for read sharing; it never writes
RPCS3 data or game files.

The source title id is retained as the external entry id, providing exact evidence for a later PS3
cover route rather than a similarity match. A matching listed `PARAM.SFO` can improve the filename
fallback title as embedded metadata, while the existing source reconciliation continues to protect
user-edited titles and covers. A later absent source entry is `Source missing`; ordinary path
availability checks never revive it. Because this is an externally curated source, normal PS3
file/folder import and folder rescans are disabled. PlayStation 3 also remains outside the
RetroAchievements console mapping and displays its explicit unsupported state.

## 2026-07-19 — M13 tracks source presence separately from path availability

An entry which remains listed by RPCS3 can still be unavailable because its drive or directory is
offline; that must display as a missing file rather than falsely claiming that RPCS3 removed it.
Schema-v10 therefore stores external-source presence separately from path availability. A
successful source refresh marks returned records present and applies their reported availability;
only records absent from the returned list become both source-missing and unavailable. The v9
migration preserves the former display as closely as possible from its single stored availability
bit, after which the next sync establishes the precise state.

Before a reconciliation writes anything, EmuShelf rejects any source path already owned by a
different library record. A source path collision is actionable and leaves the existing records
untouched; silently retaining an old source path would make an outdated game appear launchable.
Finally, a blank `games.yml` is a valid empty RPCS3 library (as upstream accepts it), not an
unsupported format. It reconciles as an empty source list while all non-empty unsupported shapes
continue to fail closed.

## 2026-07-19 — M14 accepts only SFO-validated PSP ISO and CSO images

M14 uses PPSSPP 1.20.4 as its compatibility floor. PPSSPP's desktop documentation explicitly
describes loading standalone ISO and CSO images, so EmuShelf accepts only those two extensions in
this first PSP profile. A candidate must also contain a parseable `PSP_GAME/PARAM.SFO` found through
the ISO9660 filesystem (with the existing read-only CSO logical-sector adapter); a generic ISO/CSO
cannot become a PSP game merely because the user selected the PSP system. ZIP/7z archives, CHD,
PBP, and other compressed-image variants stay unsupported: each needs a separately verified content
boundary and identity reader instead of being treated as opaque launchable files.

The reader is deliberately bounded to the small PARAM.SFO file and validates SFO table offsets,
UTF-8 text, a nine-character PSP `DISC_ID`, and a display-safe title. A valid SFO with an absent or
untrustworthy field is still a PSP container, but title presentation falls back to the filename and
no guessed identifier is stored. A valid `DISC_ID` is persisted as `GameIdentifierKind.Serial` with
`PSP PARAM.SFO` provenance for M19's future exact metadata route; there is no title-similarity
lookup. Library identity remains the file path, so regional and revision variants stay separate.
All reads use sharing-compatible read handles and tests assert that source bytes and timestamps are
unchanged.

## 2026-07-19 — M14 retries embedded PSP evidence after a transient metadata-store failure

The game-row import and identifier store are deliberately separate persistence interfaces, so an
identifier write cannot be part of the library transaction without broadening the generic library
contract. If that second write fails, the game row remains safely imported and the operation reports
the failure. A later import of the same path revisits only entries whose new embedded evidence has
identifiers and whose stored identifier set is still empty; it persists the evidence then. This
recovers transient SQLite failures without overwriting catalog or extractor evidence from another
source, and keeps the exact `PARAM.SFO` `DISC_ID` recoverable by an explicit retry.

## 2026-07-19 — RetroAchievements retries rematch hashes, rather than rescanning ROMs

A remembered-folder rescan is an import path, so only its newly created library rows join the
existing connected-account identification → matching → progress pipeline. It preserves the
existing independent metadata preference: an automatic cover/title fetch may run if the user
enabled it, but a rescan does not introduce a new metadata-consent prompt.

The connected RetroAchievements Settings card also exposes **Refresh matches**. It explicitly
refreshes the relevant per-console catalogues and rematches every locally hashed game, including a
previous fresh-catalogue `No achievements` result. Identification still first compares the stored
source fingerprint and reader version, so unchanged ROMs are reused rather than read or hashed
again. This means a newly published RA set can be found without turning the seven-day catalogue
TTL into background traffic or treating ordinary rescans as a full disc-I/O pass. The manual path
remains serialized with connection/import work and uses only existing read-only API operations;
it cannot alter ROMs, emulator configuration, or RA account state.

## 2026-07-19 — M15 validates RetroArch content as a file, not a guessed format

RetroArch's core-and-content launch form requires a content file, so M15 rejects a directory before
minimizing the frontend. It deliberately does not add a launch-time extension allow-list: the exact
read-only format contracts for Mega Drive / Genesis, Nintendo DS, and Game Boy Advance belong to
M16–M18. The configured core is always explicit and per system, while the executable remains the
one shared portable installation. Settings presents the core file name plus Replace and Clear
actions; it never scans the RetroArch installation or changes its configuration, overrides,
playlists, achievements settings, or cores.

## 2026-07-19 — M15 pins the RetroArch template's core-and-content sequence

For a core-aware launcher, merely containing `{CorePath}` is not enough: a user template could
otherwise start RetroArch without content or make the core path itself look like content. M15
therefore accepts one `{CorePath}` only when its token is immediately preceded by `-L` and immediately
followed by the one `{GamePath}` token. Other options may surround that three-token sequence, but
cannot duplicate or substitute either path. Invalid, reordered, missing, or syntactically malformed
templates fail before the frontend minimizes.

## 2026-07-19 — M16 uses a bounded, normalized Mega Drive cartridge SHA-1

M16 accepts only individual `.md`, `.gen`, and `.bin` files whose normalized bytes contain the
standard `SEGA` cartridge header at `0x100`, are word-aligned, and are no larger than 10 MiB — the
same cartridge ceiling used by the supported Genesis Plus GX core. A `.smd` file is accepted only
when it has a 512-byte copier header followed by complete 16 KiB interleaved blocks; each block is
deinterleaved using Genesis Plus GX's byte-lane order and the resulting first block must contain
that same header. Byte-swapped, raw-but-renamed `.smd`, archive, headerless, oversized, and other
ambiguous dumps are deliberately unsupported rather than guessed.

The reader streams only that bounded normalized cartridge payload through SHA-1, never writes the
source, and persists the uppercase digest as `GameIdentifierKind.Sha1` with `Mega Drive normalized
ROM` provenance. This is exact evidence for the Libretro No-Intro cartridge DAT, whose hashes live
inside nested `rom` records, so the catalog parser indexes nested SHA-1 fields for SHA-1 profiles.
Filenames remain presentation-only fallback titles; M19 will separately decide cover and
RetroAchievements semantics after its required parity and provider checks.

## 2026-07-19 — M16 separates Mega Drive layout recognition from checksum extraction

Folder discovery and explicit-file system suggestions need only prove the bounded extension/layout
and normalized `SEGA` header; calculating the SHA-1 there would read every accepted ROM in full
before the import stage reads it again to persist evidence. `TryRecognize` therefore reads at most
the raw header or first normalized SMD block, while `TryRead` remains the single full, bounded
SHA-1 extraction path used for import metadata and later on-demand enrichment. A proven Mega Drive
`.bin` is now a definitive mismatch for PlayStation systems rather than falling through the legacy
explicit raw-BIN fallback; only unproven `.bin` files remain eligible for a user-confirmed
PlayStation import.

## 2026-07-19 — M17/M18 retain cartridge header evidence but match only exact raw ROM SHA-1

Nintendo DS and Game Boy Advance import starts with one deliberately raw layout each: a DS `.nds`
file no larger than 512 MiB, and a GBA `.gba` file no larger than the 32 MiB cartridge address
space. The readers validate fixed headers and bounded structural fields before discovery accepts a
file, then stream the unchanged raw bytes through SHA-1 only during evidence extraction. There is
no archive, copier-header, byte-order, trimming, or converted-layout normalization path until it
has dedicated fixture parity; a renamed unsupported layout is therefore rejected rather than
guessed.

For DS, the reader requires valid logo/header CRC-16 values, coherent ARM9/ARM7 ranges, and a
standard or DSi-enhanced unit code. It accepts a structurally valid `####` homebrew header as a
local library entry but deliberately withholds that shared placeholder code. DSi-exclusive input is
outside the DS launch contract. For GBA, the raw header must have the standard boot branch/fixed
byte and complement check. A printable title and commercial four-character game code may improve
local presentation and remain non-primary `TitleId` evidence, but neither code selects catalogue
metadata: the Libretro No-Intro nested ROM SHA-1 is the required exact key. This prevents revisions,
regional variants, altered dumps, and homebrew from being title-guessed or colliding because of a
reused header code. Artwork and RetroAchievements support remain separately gated by M19.

## 2026-07-19 — Strict Nintendo cartridge recognition validates the canonical header logo

The original M17/M18 header checks could accept a fabricated Nintendo DS logo when its recomputed
CRC happened to agree, and a fabricated Game Boy Advance header with a valid complement check. Both
systems now require the canonical 156-byte header-logo SHA-256 digest; DS additionally requires the
format's fixed `0xCF56` logo-CRC field. Keeping a digest rather than logo bytes in production code
rejects self-consistent synthetic headers without bundling the logo as an app asset. The test
fixtures retain their format bytes solely to prove this compatibility gate and source files remain
read-only.

## 2026-07-19 — M19 uses exact Libretro catalog matches before named cover art

The maintained Libretro database and thumbnail service are the one provider pair that covers PS3,
PSP, Mega Drive / Genesis, Nintendo DS, and Game Boy Advance without an EmuShelf API credential or
bundled artwork. The provider was checked against its current Redump/No-Intro identifier semantics,
CC BY-SA database license, periodic thumbnail-server updates, bounded image behavior, and live
`200` image responses for one canonical title on every expansion system plus a `404` miss.

PS3 uses the already source-authoritative RPCS3 title id, normalized to the Redump product serial;
PSP uses its read-only `PARAM.SFO` product serial; the three cartridge systems retain their existing
exact SHA-1 catalog keys. No provider offers a reliable, complete shared id-to-cover mapping for all
five systems, so named art is requested only after that exact catalog match yields a canonical title.
The shared opt-in downloader handles 404, rate-limit, offline, malformed-image, and ownership-race
outcomes; it never replaces a user cover or bundles game art.

## 2026-07-19 — M19 RetroAchievements hashes preserve rcheevos' supplied-byte semantics

The expansion readers remain pinned to rcheevos
`2ac45d357bce2906bb0f1438f3eaf8ce6e78e3c4`: Mega Drive / Genesis and GBA use streamed whole-file
MD5; DS hashes its 0x160-byte header, ARM9, ARM7, and 0xA00 icon/title ranges; PSP hashes
`PSP_GAME/PARAM.SFO` followed by `PSP_GAME/SYSDIR/EBOOT.BIN` through the existing read-only ISO/CSO
logical-disc layer. The M19 fixtures pin each accepted extension's expected MD5, including raw and
SMD Mega Drive layouts and ISO/CSO PSP parity. SMD's normalized SHA-1 remains catalog evidence only;
RetroAchievements must hash the original accepted file bytes, as rcheevos does.

Every expansion reader gets a separate persisted version so earlier global hashes cannot be mistaken
for a supported M19 result. RA console ids are PSP 41, Mega Drive 1, DS 18, and GBA 5. PS3 remains
unmapped and explicitly unsupported. Archives, unrecognized/headered layouts, local `####`
homebrew cartridges, PSP images without a trusted retail serial, and unsupported containers produce
an unknown achievement state rather than a false no-achievements result.

## 2026-07-19 — Nintendo DS achievement matching preserves rcheevos' code-size rejection

The raw DS importer may retain a structurally valid cartridge with large ARM9/ARM7 ranges for local
library use, but pinned rcheevos rejects an image when their combined code size exceeds 16 MiB.
M19 therefore applies that same bound only to achievement hashing and returns `Unknown` rather than
submitting a non-parity MD5 to the DS catalogue. The fixture exercises an import-valid image beyond
the bound, keeping the local recognition and RA contracts deliberately separate.

## 2026-07-19 — PSP achievement matching requires rcheevos' complete initial file sector

Before it clamps `PARAM.SFO` or `EBOOT.BIN` to its declared ISO9660 size, pinned rcheevos requires
the file's first 2048-byte logical sector to be complete. M19 does the same: a disk truncated within
that sector is invalid media, not a candidate hash. Later short reads remain conservatively rejected
as well, so malformed PSP images cannot produce a potentially non-parity catalogue lookup.

## 2026-07-19 — RetroArch core selection lists only adjacent installed cores

The Settings core selector now lists `.dll`, `.dylib`, and `.so` core binaries directly under the
`cores/` directory beside the configured RetroArch executable. The user still explicitly chooses
one default core per system; EmuShelf never guesses a core, searches elsewhere on disk, downloads
or updates a core, or changes RetroArch configuration, overrides, playlists, or achievement settings.
This replaces the manual file-path requirement after Windows acceptance testing showed it was an
unexpected obstacle for a normal portable RetroArch installation.

## 2026-07-19 — Existing downloaded artwork remains partial metadata

Metadata summaries distinguish an actually unmatched game from a game that already has a downloaded
cover but lacks an exact catalog title match. Re-running enrichment must preserve that distinction:
the former is reported as unmatched, while the latter remains partial metadata and is not falsely
reported as a missing cover. This keeps manual/filename artwork fallbacks useful without obscuring
the remaining catalog-title gap.

## 2026-07-19 — Nintendo DS cover frames use the provider's wide case artwork

The shared cover grid keeps one stable width-to-height frame per system, but Nintendo DS
Libretro named boxarts are broad DS-case scans rather than the portrait disc-case images
used by GameCube, Wii, PlayStation, and Mega Drive. Windows acceptance data measured a
1.115 median ratio across 40 downloaded DS covers, so the DS frame uses that ratio rather
than the 0.708 portrait default. Exact catalog titles may replace embedded media headers,
which are evidence rather than user edits and often contain short internal identifiers;
manual titles remain immutable.

## 2026-07-19 — Core filtering only narrows adjacent installed choices

RetroArch installations can contain many cores, so every core-aware settings card exposes an
always-visible case-insensitive filter over the adjacent `cores/` directory listing. The filter
does not infer a core from the system, change the existing selected core, search elsewhere on disk,
or download anything; the user still explicitly chooses the per-system core.

## 2026-07-19 — GBA filenames are the unmatched presentation fallback

GBA cartridge headers remain authoritative local evidence for the exact SHA-1 and four-character
game code, but their short internal titles are not reliable library labels for translations and ROM
hacks. GBA imports therefore present the filename until an exact catalog match supplies a canonical
title. A rescan repairs only prior embedded-header titles; catalog and user-edited titles remain
unchanged.

## 2026-07-19 — Cartridge headers identify DS and GBA ROMs, not their library labels

Nintendo DS and Game Boy Advance header titles can be abbreviated or retain the title of an
unmodified base game. Both integrations therefore persist only their game code and exact checksum
as header evidence, present the filename initially, and let an exact catalog match replace it with
a canonical title. The shared import reconciliation upgrades only old embedded-header titles, never
catalog or user-edited titles. Rich embedded metadata from other formats, such as PSP `PARAM.SFO`,
continues to be presented when available.

Future cartridge integrations use the same shared import helper, which deliberately returns no
presentation title. This makes filename-first display the default for systems such as SNES while
retaining their header identifiers for exact matching; a future format with genuinely rich embedded
metadata must opt in explicitly rather than inheriting abbreviated header-title display.

## 2026-07-19 — PSP cover frames use the provider's tall scan ratio

The shared cover grid keeps one stable width-to-height frame per system. Eleven PSP thumbnails
downloaded during Windows acceptance measured a `0.581` median ratio, so PSP uses that tall case
frame rather than the `0.708` DVD-case default. This is presentation-only; the shared metadata
pipeline continues to rely on exact serial matches before attempting canonical and filename
thumbnail titles.

## 2026-07-19 — Super Nintendo joins as a cartridge system (missed in the expansion pass)

SNES was omitted when the M16–M18 cartridge platforms were added; it is registered now on the same
seams (`FileImportRules`, RetroArch launcher, No-Intro/Libretro metadata, and the read-only
RetroAchievements identification). Four SNES-specific choices were made:

- **Cover ratio is landscape `1.434`, not portrait.** Unlike every prior disc/cartridge system, the
  Libretro SNES named boxarts are the wide North-American cardboard box: ten representative scans
  measured 512×357 (1.434) with a few 512×364, mean 1.425. The frame therefore uses `1.434`, a
  short-and-wide cover that bottom-aligns on the existing 266px shelf (like the DS `1.115` frame),
  so no shelf-height change is needed. This is the counterintuitive but measured value.
- **Recognition is structural, since the SNES has no magic bytes.** `SuperNintendoRomReader` accepts
  `.sfc`/`.smc` between 32 KiB and 8 MiB and validates the internal LoROM (`0x7FC0`) or HiROM
  (`0xFFC0`) header by its checksum/complement consistency (`XOR == 0xFFFF`), an emulation reset
  vector inside `$8000-$FFFF`, and a plausible map-mode byte. The header title is Shift-JIS on
  Japanese carts, so it is read best-effort for display only and never gates recognition — an
  ASCII-title gate would wrongly reject Japanese ROMs. `.fig`/`.swc` copier formats are deferred
  until their normalization has fixtures.
- **The optional 512-byte copier header is normalized away** (present when `size % 0x2000 == 512`),
  matching both the No-Intro sets and the pinned rcheevos algorithm, so a headered `.smc` and a
  headerless `.sfc` of one cartridge share a single SHA-1 (catalogue) and MD5 (RetroAchievements).
- **RetroAchievements console id 3 needs its own hasher.** SNES hashing is *not* the whole-file
  cartridge MD5 used for Mega Drive / GBA: `SuperNintendoRomHasher` strips the copier header first,
  then MD5s the rest (`rcheevos-2ac45d3-snes-v1`). The SNES header carries no reliable commercial
  game code, so SHA-1 is the sole exact identifier; there is no title-id evidence.

This is the first cartridge system whose OpenEmu library icon was already bundled (`snes`), so no
new artwork was added. OpenVGDB is still deferred — the existing No-Intro SHA-1 + Libretro title
route is the same one the other cartridge systems use.

## 2026-07-20 — M25 selection is view-model-owned and bulk removal is transactional

The grid and list report only pointer modifiers to `MainViewModel`; the view model owns the shared
selection set and range anchor through the existing `GameViewModel.IsSelected` and `SelectedGame`
state. This keeps selection identical when switching library layouts and leaves code-behind as
gesture wiring. Ctrl+A selects only the games currently shown by the active collection and search
filter, while text fields retain their native select-all behavior.

Bulk removal uses one SQLite transaction over the selected row ids and confirms the count once.
It deletes only EmuShelf database rows: source games, cover assets, and missing-file state remain
untouched. Reloading the library deliberately clears selection so no stale view models or hidden
rows can be removed by a later Delete keypress.

## 2026-07-20 — User-supplied console illustrations supplement platform artwork

The supplied PS2, PS3, Wii, and PSP pixel-art console illustrations are adapted into
transparent, tightly framed UI assets and map only to their matching system ids. They
replace the generic platform artwork in shared navigation, settings, and missing-cover
presentation while the licensed OpenEmu platform assets continue to cover every other
system. This keeps the visual treatment consistent without importing new third-party
game art or changing any game-cover source.

## 2026-07-20 — Regenerated compact sprites favor sidebar legibility

The first PS2, PS3, and PSP illustration imports lost too much detail at the 18px
navigation size and did not match the existing pixel-art icon language. They are
replaced with custom, console-specific pixel-art sprites: an angular blue-accented
PS2 tower, a rounded silver-trim PS3 tower, and a cyan-screen PSP handheld. Wii
continues to use the supplied illustration because it remains legible at that size.

## 2026-07-21 — Typed launcher targets retain portable ownership boundaries

Schema v11 makes the shared `EmulatorInstallations` record the sole owner of a launch target:
either a direct path (native binary or AppImage) or a Flatpak application id. Existing executable
paths migrate as direct targets, while the old per-system field is read only as an interrupted-
migration fallback. This prevents compatible systems from silently disagreeing about their shared
emulator target and keeps process invocation shell-free.

Flatpak permission inspection is strictly advisory except for a confirmed `none` access result:
EmuShelf never changes permissions. A resolved descriptor tree is required before a Flatpak launch,
because a sandbox cannot be expected to infer inaccessible CUE/M3U dependencies. Flatpak RetroArch
is deliberately deferred: its private core directory is neither inspected nor assumed; direct or
AppImage RetroArch continues to use adjacent core discovery.

## 2026-07-21 — AppImage is portable SteamOS distribution; Steam Input owns controller mapping

EmuShelf's first SteamOS package is a self-contained AppImage, not an EmuShelf Flatpak. During an
AppImage run, `$APPDIR` is read-only, so portable Data, Covers, Cache, Logs, and Settings use the
writable parent of `$APPIMAGE`. The package keeps ICU enabled and validates the documented
`--appimage-extract-and-run` fallback.

Gamepad mode is a fullscreen interface mode rather than a native controller stack. It relies on a
documented Steam Input keyboard contract, keeping ordinary non-Steam Gamepad mode usable by
keyboard/mouse without claiming physical-controller support. Focus is separate from desktop
multi-selection so launching and returning from a game cannot alter desktop bulk actions.

## 2026-07-23 — Gamepad cover selection hands off explicitly to Desktop mode

The host platform file picker is not a reliable controller-owned surface in Steam Game Mode.
Choosing **Set cover** in Gamepad mode therefore opens an in-window explanation and an explicit
**Switch to Desktop mode** action; it never opens a native picker behind the fullscreen library.
Desktop mode retains the existing picker and cover-import behavior. All other Gamepad secondary
workflows use the main-window overlay host so they have one scrim, one focus owner, and no child
window chrome.

## 2026-07-23 — Gamepad All Games uses a normalized artwork well

Desktop library views retain each platform's measured cover frame. Gamepad **All Games** instead
places every cover inside one fixed-height artwork well using `Uniform` scaling: no cover is
cropped or distorted, while mixed portrait, handheld, and wide cartridge artwork no longer creates
an irregular shelf grid. This is Gamepad-only presentation and does not change stored artwork or
the desktop library layout.

## 2026-07-24 — Cloud save sync: rclone transport, manifest merge, save-only, PCSX2 pilot

EmuShelf owns no saves; the emulators do. A new opt-in **M29** cloud sync targets battery/
memory-card saves only. Save states (tied to exact emulator build/arch) and configs (machine-
specific input, video backend, absolute paths) are excluded for now.

**Transport is external rclone**, invoked shell-free like an emulator. One integration reaches
Google Drive plus Dropbox/OneDrive/S3/WebDAV/self-hosted, so "commercial now, own server later"
costs nothing. Chosen over per-provider SDKs specifically so EmuShelf embeds **no OAuth client
secret** — it would be extractable from a GPL binary. rclone owns the token; EmuShelf persists
only the remote name + folder, consistent with the RetroAchievements-key no-logging rule.
Tradeoff: a ~50–70 MB per-OS binary dependency, acceptable under the existing "launch external
tools" model. rclone is MIT — fine to invoke from a GPL app as a separate process. The in-app
**Connect Google Drive** button drives rclone's OAuth from Settings; no terminal.

**Sync logic, not transport, is the hard part.** A per-unit manifest (content hash + mtime +
last-synced revision) distinguishes unchanged / local-changed / remote-changed / both-changed.
This is deliberately **not** a raw creation/modified-date comparison: clocks on a PC and a Deck
drift, and file copies rewrite mtimes, so "newest date wins" silently loses saves. mtime is only
a tie-breaker inside a genuine both-sides conflict. rclone is used only for list/copy/read —
never `sync --delete`, which would delete a one-sided save or clobber the better one. A conflict
keeps the newer copy active and backs the loser up under `Saves/conflicts/`; nothing is deleted
or silently overwritten. Triggers reuse the tracked-emulator-exit hook (push) and app-start /
pre-launch (pull). Forced upload/download and per-conflict resolution are always available; the
auto-detected save folder is user-confirmable and overridable.

**The sync unit is emulator-defined, not per-game, and PCSX2 has two shapes.** A PCSX2 *file*
memory card (`Mcd00N.ps2`) is monolithic — many games on one card — so its unit is the whole
card, and a both-sides conflict can supersede a card that changed for a *different* game
(mitigated by the backup). A PCSX2 *folder* memory card (`Mcd00N/` with a `_pcsx2_index` plus
per-serial subfolders, created by "Automatically manage saves based on running game") stores each
game separately, so each game serial is its own unit and conflicts are per-game. Folder cards are
the safer setup and EmuShelf recommends them in docs but never flips the PCSX2 setting. EmuShelf
discovers the real memcard directory and card mode by reading PCSX2's own `PCSX2.ini` **read-only**
through a versioned adapter (users and EmuDeck relocate it), mirroring the M13 RPCS3 read-only
pattern; it never writes emulator config. Proven end-to-end on PCSX2 first, then generalized
behind `ISaveLocationProvider` without touching the engine.

## 2026-07-24 — PCSX2 folder-card hashes exclude `_pcsx2_index`

Each folder sync unit is a game-serial subdirectory, so the folder-card-root `_pcsx2_index` is
outside both its payload and hash. The index records card ordering and timestamp metadata, so
treating it as save content would manufacture two-machine changes and false conflicts even when
the per-game save files agree. The deterministic per-game hash is SHA-256 over ordinal-sorted
relative file paths and file bytes.

## 2026-07-24 — Cloud sidecars preserve EmuShelf hashes

rclone transports opaque unit payloads with `copyto`/`cat` and has no role in deciding content
identity. Each payload has a sibling JSON sidecar containing the unit id, EmuShelf content hash,
and modified time; listing reconstructs snapshots from those sidecars rather than a provider hash.
This preserves the composite folder-card hash across rclone backends while retaining copy-only
semantics. Folder payloads are deterministic ZIP files supplied by the local endpoint.

## 2026-07-24 — PCSX2 card type is classified from disk, and empty remotes list as empty

Two Phase-1 robustness fixes after review. (1) The PCSX2 provider now decides file-vs-folder per
card from the filesystem — a directory carrying the `_pcsx2_index` marker is a folder card (one
unit per game serial), a `*.ps2` file is a file card — instead of switching on the global
`EmuCore/McdFolderAutoManage` flag. A folder card whose slot filename lacks a `.ps2` extension is
therefore recognized rather than missed, and the INI parser no longer fails closed on a non-`.ps2`
slot filename (only a missing/unsafe filename or a non-`SettingsVersion 1` layout still fails
closed). The exact folder-card INI representation still needs validation against real PCSX2
hardware. (2) The rclone transport treats `lsjson` exit code 3 (directory not found) as an empty
listing, so the first sync against a fresh remote no longer throws before anything is uploaded.

## 2026-07-24 — Flatpak launches get ephemeral, per-launch read-only ROM access

This supersedes the earlier stance (2026-07-21) that EmuShelf never grants a Flatpak emulator
access and leaves it to the user. A Flatpak emulator (e.g. `net.pcsx2.PCSX2`, whose default
manifest exposes no user-file access at all) runs sandboxed and cannot see ROMs under `~/Documents`,
so a launch that passes EmuShelf's own `File.Exists` check still fails *inside* the emulator with a
bare "requested filename does not exist". EmuShelf now injects `--filesystem=<dir>:ro` into the
`flatpak run` invocation for exactly the directories the launch needs — the game plus any resolved
CUE/M3U dependencies, deduplicated — reusing the dependency set already computed for the launch.

The grant is deliberately **ephemeral**: it applies to that single `flatpak run` process and
vanishes on exit, so EmuShelf never edits the emulator's stored overrides (`flatpak override`) and
never persistently widens its sandbox. It is **read-only**, honoring the never-modify-game-files
rule; the emulator's own memory cards/BIOS/save-states live in its private data dir, which it
already owns. Multi-file sets work because the emulator still opens the original paths — file
forwarding via the document portal was rejected as the default since it rewrites paths and breaks
CUE/M3U sibling references.

Consequently the old `flatpak info --file-access` preflight is removed. It was also **buggy**: it
compared against `none`/`read`/`read-write`, but real flatpak prints `hidden`/`read-only`/
`read-write`, so a no-access path (`hidden`) fell through the "unknown state → warn and proceed"
branch instead of blocking — the exact reason the PCSX2 failure surfaced as the emulator's own
cryptic error rather than a clear EmuShelf message. With access now granted proactively, the
inspector only needs to confirm the app is installed.

## 2026-07-24 — Native SDL2 controller input augments Steam Input, never replaces keyboard

Gamepad mode previously relied solely on Steam Input mapping controller buttons to keyboard keys
(2026-07-21). That is fragile: a default non-Steam controller layout emits a virtual gamepad, not
keystrokes, so nothing happens until the user hand-configures a keyboard layout. EmuShelf now reads
the physical controller directly through SDL2's GameController API (`SdlGamepadReader`), which
normalizes any recognized pad to the standard layout and honors SDL's controller database.

Input is unified behind a single `MainViewModel.DispatchGamepadAction` entry point that both the
native poll loop and the Steam-Input keyboard handler feed, so the two paths cannot diverge; the
button→action contract is identical (A confirm, B back, X search, Y actions, LB/RB platform, d-pad/
left-stick navigate with menu-style auto-repeat). Polling runs only in Gamepad mode.

The native layer is strictly additive and fully defensive: SDL is loaded lazily through a
multi-name `DllImportResolver`, every native call is guarded, and any load/init failure sets
`IsAvailable=false` and returns a disconnected reading forever — so keyboard/Steam Input remains the
universal fallback everywhere.

For genuine Linux/Windows/macOS parity the native SDL2 binary is **bundled**, not assumed present.
It comes from the `ppy.SDL2-CS` package with `ExcludeAssets="compile;runtime"` so only the native
libraries ship (win-x64, win-arm64, linux-x64, osx-x64, osx-arm64) — the managed SDL2-CS binding is
not used or redistributed; EmuShelf keeps its own P/Invoke. A self-contained per-RID publish flattens
the native beside the executable, so `NativeLibrary.TryLoad` finds it in the app directory; the
resolver tries the bundled filename first and only then a system soname, so SteamOS still works if
the bundle is ever absent. SDL2 is zlib-licensed and credited in THIRD-PARTY-NOTICES. Chosen over
SDL3 to match the SDL2 that SteamOS itself ships.

## 2026-07-24 — Gamepad covers use each platform's true frame on a shared shelf

This supersedes the 2026-07-23 "Gamepad All Games uses a normalized artwork well" decision. The
fixed 280px well letterboxed every cover into one generic box, so a square PS1 cover and a portrait
GameCube cover rendered at the same size and neither matched its real artwork. Gamepad tiles now use
the same model the desktop grid already had: the frame is the platform's true `CoverAspectRatio`
(`CoverHeight`), and frames bottom-align inside a shared `ShelfCoverHeight` sized to the tallest
cover in the current view.

That keeps the property the well was introduced to protect — mixed-platform All Games rows stay
baseline-aligned and titles line up, because every tile shares one shelf height — while each cover
finally shows at its own shape. Art fills the frame with `UniformToFill`; since the frame is the
platform's real scan ratio this bleeds a hair rather than letterboxing. `GamepadCoverFrameHeight`
is removed rather than left unused.

## 2026-07-24 — Shoulder buttons walk the rail's real order

LB/RB stepped All Games -> systems while the rail renders All Games, Collections, then systems, so
the shoulder buttons silently skipped Collections. Platform cycling now uses the rail's own index
space (0 All Games, 1 Collections, 2+ systems) and still refuses to wrap at either end. The
Collections tab also gains the `selected` styling the other tabs already had, so it looks active
when reached.

## 2026-07-25 — Cover frames follow the actual artwork after loading

System cover ratios are defaults for placeholders, not assertions about every regional release.
The UI updates an individual tile from the cached artwork's width and height after it loads, so
square US/Japanese Dreamcast jewel cases and portrait PAL keep cases retain their distinct shapes.
Dreamcast's unloaded default is the square US case because that is the configured library's primary
region. Libretro artwork fallbacks rank the exact catalogued region first; a regional suffix is
never discarded for candidate ordering.

## 2026-07-25 — Dreamcast v1 accepts validated GDI descriptor sets

Dreamcast support starts with `.gdi` descriptor sets, not loose `.bin`, `.cdi`, or `.chd` files.
The descriptor names every track, and validation requires its primary data track (track 03) to
contain the `SEGA SEGAKATANA` IP.BIN marker. That gives folder scanning, launch, exact Redump
data-track SHA-1 matching, and the rcheevos-compatible hash a single well-defined read-only
source. CDI and CHD remain visibly unsupported until their logical-track readers have parity
fixtures; treating their extension as evidence would allow false imports and false achievement
matches.

Dreamcast uses the existing opt-in Libretro thumbnail provider and the Redump Dreamcast catalogue.
RetroAchievements console 40 is enabled only for the validated GDI representation:
the canonical hash is exactly the 256-byte IP.BIN payload followed by the named boot executable,
matching rcheevos' Dreamcast algorithm.

## 2026-07-25 — GDI descriptor bounds win over padded track files

For high-density Dreamcast tracks, the next descriptor LBA is the authoritative end of the
track. EmuShelf rejects a high-density file that is shorter than that range and caps sector
reads at the same boundary. This prevents incomplete sets from entering the library and avoids
padded track 03 files shadowing later data tracks during RetroAchievements hashing. Low-density
tracks remain existence-checked only because their session lead-in gap is not stored in the
individual track files.

## 2026-07-25 — Dreamcast identifies GDI variants by ordered track hashes, then IP.BIN product number

Dreamcast metadata first tries the SHA-1 of every GDI data track, largest first, because Redump's
catalogue identifies different retail discs by different high-density tracks. The data tracks in a
GDI are separated by the standard 150-sector pregap, which is represented in descriptor LBAs but
not in the files themselves; treating the files as directly contiguous rejected valid multi-track
sets. When a known dump layout differs by padding and no exact track hash exists, EmuShelf may use
the fixed IP.BIN product number as a lower-priority Redump key. This fallback is deliberately
disabled only when the descriptor filename or its own folder explicitly labels a translation,
patch, or hack. Those modifications can keep the retail product number while changing game
content, and must remain unmatched rather than be relabeled as the retail release; unrelated
parent folders do not change identification behavior.

## 2026-07-25 — Cloud sync batches through a staging area and a single remote index

Google Drive charges roughly a second of API overhead per file regardless of size, so the first
per-unit design — one payload plus one `.meta.json` sidecar per save, each uploaded/read with its
own rclone call — made an 81-save PCSX2 folder card take ~10 minutes, and even a no-op sync ~2
minutes (listing alone downloaded 80+ tiny sidecars). Two changes fix this without giving up
per-game conflict granularity:

- **One `index.json` on the remote** describes every unit (id, content hash, modified time), so a
  sync lists with a single file read instead of one request per save. Per-unit `.payload` files
  still hold the save data, so change detection stays per game.
- **Staged, batched transfer.** `RcloneCloudSyncTransport.UploadAsync` stages payloads locally and
  records their index entries; `FlushAsync` (once per sync) writes the merged index and pushes
  everything in a single `rclone copy` session so rclone's pacer respects Drive's rate limits.
  Downloads are served from a one-shot `rclone copy` of the remote into a per-sync inbox. A
  per-call timeout turns a stalled transfer into a failure instead of an apparent hang.

Measured on a real 81-save card against Drive: a full sync ~30s (rclone skips unchanged payloads),
an everyday no-change sync 3.5s (was ~10 minutes); only changed payloads upload thereafter.

## 2026-07-25 — Libretro cover fallbacks are deterministic edition variants, not fuzzy matches

Libretro thumbnail repositories sometimes omit a catalogued regional scan while retaining the same
game's artwork under another region, a revision-free title, or a typography variant. Every
Libretro-backed platform now keeps the exact canonical title as its first request, then tries only
deterministic variants of that title: language/revision tags may be removed and a named region may
be substituted. A small reviewed alias list is reserved for documented regional product renames
where the target file is known. The candidates remain ordered and source-provenanced through the
existing downloader.

This deliberately does not use substring, token, edit-distance, or other fuzzy title matching.
Those approaches can attach the wrong cover (for example a sequel or similarly named regional
release) and violate the app's exact-evidence metadata contract. The fallback is shared by PSP,
Mega Drive, DS, GBA, SNES, Dreamcast, and the title-based fallback portion of existing platforms.

## 2026-07-25 — Title variants require catalog evidence and PSP aliases stay PSP-scoped

The literal filename lookup remains a compatibility candidate for older library entries, but it
does not establish metadata identity. It is therefore never expanded into language, region,
typography, or regional-title alias variants; those require an exact catalog match. The two
reviewed regional aliases and the `Acid` → `Ac!d` typography rule are also restricted to the PSP
Libretro playlist. This preserves the fallback's intended coverage while preventing an arbitrary
same-named file or another platform's catalogue title from receiving PSP artwork.

## 2026-07-25 — Libretro cover recovery resolves against the source index

The deterministic guessed title variants and PSP-specific aliases above are superseded. They
still fabricated URL filenames and therefore missed covers that Libretro actually carries under a
different catalogue convention (for example punctuation, regional product labels, or a revision
suffix). EmuShelf now caches each used Libretro `Named_Boxarts` listing for 14 days under the
portable metadata cache and makes recovery candidates only from its real filenames.

Recovery starts only after an exact local catalog match. It normalizes punctuation and excludes
parenthesized release labels, accepts an exact product-title match, or a single unambiguous
two-or-more-token title-prefix match. It does not index a raw filename fallback and rejects
ambiguous names. This makes covers available when the source has them while retaining the
no-fuzzy-match ownership and provenance guarantees.

## 2026-07-25 — PSP commercial renames are source-index verified aliases

The source-index resolver also retains three narrowly reviewed PSP title spellings that no
normalizer can infer safely: `Lumines - Puzzle Fusion` is indexed as `Lumines`, and `Metal Gear
Ac!d`/`Ac!d 2` use a stylized spelling. These aliases are offered only after an exact PSP catalog
match and must themselves resolve to a real filename in the PSP `Named_Boxarts` index. No alias is
used for a literal filename fallback or another platform.

## 2026-07-25 — Source-index recovery does not use title prefixes

The earlier unique-prefix rule is superseded: even when an artwork index contains only one title
with a shared prefix, it can be a different game or sequel and would silently attach incorrect
artwork. Recovery now accepts only normalized exact source product titles, plus the explicitly
reviewed PSP aliases above. Directory-index fetches run under their own small concurrency limit
before cover downloads, so a large listing cannot occupy every cover-download slot.

## 2026-07-25 — M24 is a release-quality gate before further end-user scope

The project has accumulated expansion systems and optional services while its foundational
experience still has open real-device acceptance work, most notably the Gamepad launch/return
loop and Windows/SteamOS configuration presentation. M24 therefore becomes an ordered
product-hardening gate rather than a broad cosmetic backlog: critical game-session reliability
comes first, then controller flow, high-frequency desktop flow, responsiveness/accessibility,
and finally visual plus real-device release verification.

This does not remove existing milestones or block maintenance fixes. It prevents new end-user
features from being presented as release-ready before the portable direct-launch flow, the
controller-first return path, and the UI quality bar have been demonstrably met.

## 2026-07-25 — Gamepad return suppresses input at both native and command boundaries

Closing an emulator with B/Escape can leave the same physical button held when EmuShelf regains
focus. Suppressing only SDL polling is insufficient because Steam Input may deliver a late keyboard
event; suppressing only command handling still lets the native navigation controller retain held
state. During a tracked game session, EmuShelf therefore pauses native controller routing and
resets its edge state. After return it consumes all Gamepad actions for 500 ms, covering late
Steam-Input events and making the first native reading a no-op.

The guard is deliberately short and exists only for the session transition. It does not change the
normal Gamepad mapping, require a controller release before every interaction, or alter Desktop
input.

## 2026-07-25 — Flatpak configuration is platform-aware, with explicit legacy migration

Flatpak targets are useful only for supported standalone emulators on Linux; showing them in a
Windows executable picker suggests a launch path that cannot work. New Windows configurations
therefore show a direct executable only, while Linux keeps its Direct/AppImage and Flatpak choice.
Core-aware RetroArch remains direct/AppImage-only on every platform because its Flatpak core paths
are private to the sandbox.

An existing Flatpak configuration is not silently rewritten or hidden. On an unsupported platform
Settings presents an explanatory warning and one explicit action to switch that row to a direct
executable. This preserves the stored configuration until the user chooses its migration and keeps
the existing preflight failure as the last line of protection.

## 2026-07-25 — Multi-disc games are one library title with a remembered launch disc

Independently imported discs belonging to one release are represented as an ordered title set, not
as duplicate Grid/List items. The set owns the library presentation and remembers the disc that was
last launched successfully; its card states the disc count and, when applicable, the selected disc.
This keeps continuing a long game to one action while leaving the individual media sources explicit
and inspectable.

The existing `.m3u` entry remains a single canonical library item because it is already the
descriptor through which emulators can manage a disc set. A direct "select disc" launch is enabled
only when the target emulator's invocation has been verified to honour that source; EmuShelf must
not display a choice that silently launches another disc. In Gamepad mode `A` launches the
remembered target, and `Y` → `Select disc` changes it only after ordinary launch preflight and
process handoff succeed. In-session disc swaps remain emulator-specific and outside this library
and launcher flow.

## 2026-07-25 — Selecting a disc never launches it

The preceding multi-disc launch rule is superseded for disc selection. In both Desktop and
Gamepad modes, choosing a disc changes and immediately persists the title's default source; it
does not start an emulator or require launch preflight. Launch remains an explicit, separate
action: Desktop uses the card's normal Launch action and Gamepad uses `A`. This makes the two
interfaces consistent and lets a player prepare the correct disc before committing to a session.

## 2026-07-25 — Gamepad scope, focus, commands, and interface exit use separate surfaces

The M31 audit found that the original Gamepad header made an active platform, pointer hover,
Avalonia focus, and the view-model's focused cover look like competing selections. It also exposed
Launch, Actions, Search, and an inaccurately named `Exit (Esc)` as focusable header controls even
though controller commands already owned those actions. Most seriously, B/Escape on the main
shelf changed interface mode without confirmation.

Gamepad mode therefore keeps the upper rail for library scope only, moves contextual controller
help to a fixed bottom command bar, and opens application-level actions from the controller's
Start/Menu button (`F10` for the keyboard/Steam Input fallback). B is always Back: it closes or
returns from an overlay, returns rail focus to the shelf, or is consumed at the shelf boundary. A
separate named confirmation is the only controller path to Desktop mode. Per-game Y actions no
longer contain redundant Back or Desktop rows.

Mixed input remains supported. The shell records the most recent controller versus meaningful
pointer movement and suppresses stale pointer-hover treatment while controller input is active;
it does not disable mouse input. The selected platform uses a persistent active treatment distinct
from the single strong controller-focus ring. Controller-family-specific glyphs and synchronization
of logical focus with Avalonia accessibility focus remain later M31 phases.

## 2026-07-25 — Gamepad Settings and shutdown are explicit lifecycle handoffs

Settings remains a Desktop configuration window for now. Selecting it from the Gamepad global
menu therefore opens a named confirmation surface, switches interface mode, and only then opens
Settings; it never presents a keyboard-oriented window as though it were controller-native. Quit
is a separate global-menu action with its own confirmation and B always returns to the menu without
shutting down.

The view model requests shutdown through a small application-lifetime interface rather than owning
Avalonia's desktop lifetime. Native Avalonia focus now follows the logical game, rail, achievement,
and overlay selection while controller input is active. When an overlay opens, the inactive shelf
focus ring is hidden so the modal action is the only strong focus treatment. Headless Avalonia does
not accept native focus in its virtual window backend, so real controller/accessibility focus
remains an explicit M31 Phase 4 acceptance check even though logical navigation and rendered focus
geometry are covered automatically.

## 2026-07-26 — Gamepad logical focus owns the visible focus treatment

Gamepad controls still receive real Avalonia focus so keyboard routing, controller navigation, and
accessibility state remain correct. They do not also render Fluent's default focus adorner when the
same control already has an EmuShelf logical-focus ring. Rendering both layers produced doubled
red/white outlines in headless captures and black corner rectangles on the SteamOS/Linux compositor.

The suppression is limited to the custom-focused Gamepad game, platform, modal, footer, and
achievement surfaces. Text fields and Desktop controls retain their ordinary theme focus behavior.
Modal choices stretch across the available sheet width, while the footer Menu prompt stays visually
neutral until genuine pointer hover, leaving one unmistakable strong selection at a time.

All Games still uses one shared shelf height to align mixed cover shapes along a common baseline,
but that shelf cell is layout and hit-target space only. The outer game button stays transparent for
hover, native focus, and pressed states. Pointer hover is drawn by a non-layout-changing overlay
inside the actual cover-sized panel, with the controller focus ring above it. This keeps mouse
feedback without painting a tall grey rectangle around short DS/GBA-style artwork.

## 2026-07-25 — Save providers authorize local destinations; PPSSPP is the first generalized provider

Remote save-unit identifiers are untrusted portable names, not local paths. Each emulator provider
therefore resolves a unit only when it belongs to that provider and maps to an allowed destination
under the emulator's active save root. The filesystem endpoint performs the copy and independently
checks that resolved paths stay within that root. Existing `pcsx2/...` identifiers remain unchanged
so deployed manifests and cloud data continue to work.

A multi-provider sync loads the manifest and remote index once, plans all enabled providers together,
flushes the transport once, and then writes one manifest. PPSSPP is the first additional provider:
each immediate child directory of `PSP/SAVEDATA` is one folder unit, while `PPSSPP_STATE`, settings,
plugins, and all other Memory Stick content are outside the provider boundary. Windows Memory Stick
discovery follows PPSSPP's read-only `installed.txt` modes; explicit overrides are supported without
rewriting PPSSPP configuration. PPSSPP sync is opt-in, and the existing forced upload/download actions
remain PCSX2-specific until the per-system controls described in M29 Phase 2 land.

## 2026-07-25 — Save-provider participation follows emulator configuration

This supersedes the PPSSPP opt-in portion of the preceding save-provider decision. PPSSPP follows
the same participation rule as PCSX2: when EmuShelf has a configured emulator installation or the
user supplies an explicit save-location override, that provider joins the shared sync operation.
There is no PPSSPP-only enable flag or checkbox. This avoids a second source of truth whose state
could disagree with the emulator configuration and establishes the rule future providers follow.

The Saves UI presents supported systems as equal, icon-led rows inside the existing cloud-sync
section. Platform rows do not create their own cards or borders, and overrides use the same path
field plus Browse interaction. The bundled, licensed platform artwork is reused rather than adding
a second icon set.

## 2026-07-26 — Forced save replacement is always platform-scoped

The prior temporary rule that left forced upload/download PCSX2-specific is superseded. A global
overwrite action is too easy to misread and becomes increasingly dangerous as providers are added.
The shared cloud section therefore exposes only reconciled `Sync all now`; each platform row owns
explicit `Replace cloud` and `Replace local` actions that affect only that named platform. Both
directions retain backup-before-overwrite behavior, and the same provider-resolved path boundary
applies to normal and forced synchronization.

## 2026-07-26 — Automatic save sync wraps the shared game-launch command

Desktop and Gamepad launches already converge on `MainViewModel` and the launch service does not
return until an EmuShelf-started emulator exits. Automatic save sync therefore wraps that shared
command: reconcile only the selected game's system before calling the launch service, then
reconcile the same system again only when the result confirms a tracked process exited. This keeps
emulator process ownership unchanged and avoids platform-specific launch hooks.

A pre-launch transport failure is advisory, not a launch veto: EmuShelf warns, retains the local
save, and starts the game. It retries after exit so restored connectivity can upload or reconcile
the session, with ordinary manifest conflict backups still protecting both versions. While the
launch command is active, the existing busy state disables Settings/manual sync; Settings also
states that emulators launched outside EmuShelf must be closed before manual synchronization.

## 2026-07-26 — Launch preflight precedes automatic save synchronization

This refines the preceding lifecycle decision. The shared launch service completes all read-only
launch validation before invoking the pre-start save-sync callback, so a missing emulator, core,
game dependency, or invalid argument template cannot trigger cloud access or change the active
save state. The callback still runs before the frontend is suspended and before the emulator
process starts.

A failed multi-unit pass is not described as retaining the original local save because earlier
units may already have reconciled safely before a later unit failed; EmuShelf instead launches
with the save state currently on disk, while overwrite backups preserve superseded content.
Completed automatic passes surface conflicts and empty save sets explicitly, including conflicts
resolved before launch that a later pass would no longer contain.

## 2026-07-26 — Filename resolution through the artwork index is shared by every system

The artwork title index was previously consulted only when the catalogue returned a match, so a
game whose checksum appears in no DAT — a translation patch, an undub, a romhack, a trimmed or
scene-renamed dump — fell through to a single URL fabricated from its literal filename, release
tags and all, and 404'd. That is the dominant cover-download failure, and it is not specific to
one platform: it applies wherever a dump can be modified.

`GameMetadataService` therefore resolves the filename against the same index as a catalogue title,
for every profile that registers an `IArtworkTitleIndexProvider`. Candidates are ordered by how
certain they are: index-resolved catalogue title, index-resolved filename, fabricated catalogue
URL, fabricated filename URL, local sidecar. Fabricated URLs stay as a fallback for an index
outage, but no longer consume the first request when the index has a known-good answer.

Title comparison gained two symmetric normalizations rather than a similarity score: a version
suffix ahead of the release tags, and a leading publisher possessive carried by only one source.
Both are applied to each side and the comparison stays whole-title equality, so the matcher still
cannot pair a game with its sequel or a spin-off. Regional ties now prefer a retail release over a
kiosk demo, prototype, or control-scheme hack instead of whichever tag sorted first.

The Nintendo DS profile falls back from the ROM checksum to the cartridge header game code, which
the DAT records in its `serial` field and which a patch or trim leaves untouched. A fallback-key
match applies the canonical title like any other match, so a modified dump is retitled to the
retail release it derives from; the release tags remain in its filename.

## 2026-07-26 — A cartridge game code is never a catalogue key

This supersedes the Nintendo DS fallback-key portion of the preceding decision, which was wrong in
practice. A romhack patches the ROM but never the four-character header game code, so keying on it
resolved `New Super Mario Bros. (USA)`, its `(Deluxe)` hack, and `Newer Super Mario Bros. DS` — all
game code `A2DE` — to one DAT entry, giving three distinct library rows the same title and cover.
The fallback mis-identified precisely the modified dumps it was added to help.

The checksum is therefore the only cartridge catalogue key. A modified dump is matched by filename
through the artwork index instead, which recovers its cover without claiming it is the original
release. The DAT parser keeps no TitleId keying, since nothing consumes it.

Alias tables that map a catalogue title to an artwork filename are keyed by product title and
looked up with the region and language tags removed. A catalogue title always carries those tags,
so the previous whole-title lookup never fired against a real match.

GameTDB's PlayStation 3 `coverHQ` set is partial. Candidates now cover every region in the
high-resolution set and then every region in the standard `cover` set, so a release that only ever
received the standard image is no longer left without a cover.

## 2026-07-26 — A save-provider registry owns all platform knowledge

Adding PPSSPP alongside PCSX2 left platform knowledge in three hand-maintained lists that had to
agree: the system ids `SyncNowAsync` passed, the `CanSyncSystem` switch, and the `CreateTarget`
switch. They agreed, but nothing enforced it, and a disagreement had a silent failure mode: the
pipeline returns `NotConfigured`, the launch path shows no message for that status, and the user
plays believing their saves were pulled.

`SaveProviderRegistry` is now the single source of truth. A `SaveProviderDescriptor` carries the
system id, display name, presentation strings, a `CreateProvider` factory, and a detected-path
resolver. `CanSyncSystem` answers by calling exactly the factory the sync pipeline calls, so the
participation answer and the provider construction cannot diverge. The coordinator, the settings
view model, and the settings view now name no emulator at all; the view renders one row template
over the registry. Adding a platform is a provider class plus one registry entry.

Provider configuration exceptions derive from `SaveProviderConfigurationException`, so the two
catch filters name one base type. Previously each new provider had to be added to both filters by
hand, and omitting one would let its exception escape and fault the whole sync.

`ConnectGoogleDriveAsync` takes overrides keyed by system id rather than one positional string per
emulator. Four positional strings made transposing two paths easy and invisible; each additional
platform made it worse.

## 2026-07-26 — Save locations are per-system, with legacy fields mirrored for rollback

`CloudSaveSyncSettings` now holds `SaveLocations`, a system-id-keyed dictionary of override plus
last-success time plus last error, so Settings reports each platform's own outcome instead of one
shared status line. `NormalizeSaveLocations` folds the legacy `Pcsx2ConfigDirectory` and
`PpssppMemoryStickDirectory` fields in on load, and an entry already in the dictionary always wins
so migration cannot resurrect a stale value over a newer explicit choice. Writes mirror back onto
the two legacy fields, so a user who rolls back to an older build still finds their configuration.

The record defines structural equality explicitly: the synthesized record `Equals` would compare
the dictionary by reference, making a settings object that round-tripped through `settings.json`
compare unequal to the one that produced it.

An empty override box now means "use the configured emulator" rather than being pre-filled with the
derived path. Pre-filling turned a derived location into an explicit override the moment the user
pressed Save, pinning a path that should have kept following the emulator configuration.

## 2026-07-26 — DuckStation sync trusts settings, not guessed save paths

DuckStation save discovery participates only when EmuShelf has a configured DuckStation install,
a Flatpak target, or an explicit user-directory override. The provider then requires a real
`settings.ini`: portable mode wins when `portable.txt` is beside the executable, otherwise the
documented current and legacy user directories are searched in order. An explicit override or
portable marker that lacks `settings.ini` fails closed rather than silently falling through to a
different installation. The configured `[MemoryCards] Directory` is required, so EmuShelf never
guesses where cards live. A shared slot without `CardNPath` uses DuckStation's own
`shared_card_N.mcd` setting default inside that explicitly configured directory.

Shared cards use slot-based cloud identities (`duckstation/shared/card1` and `card2`) so two
machines can point the same logical slot at differently named local files. The UI labels them as
cross-game cards because each is one monolithic conflict domain. `PerGame`, `PerGameTitle`, and
`PerGameFileTitle` cards use their safe card filename as the identity and remain independent file
units. `None` and `NonPersistent` slots, stale shared-card files, unrelated files, and save states
do not participate. Unknown future card types and ambiguous layouts fail closed.

## 2026-07-26 — Save-sync status and DuckStation identities fail closed at their real boundary

This narrows two claims in the preceding save-sync decisions after review. A multi-provider pass is
one staged operation and its exceptions do not identify the provider that failed. EmuShelf therefore
keeps that failure in the global operation status instead of writing the same error onto every
platform row. Single-platform automatic and forced operations still record their platform result.

DuckStation serial and title cards now have distinct cloud namespaces. Serial mode also accepts only
DuckStation's normalized PlayStation product-code shape, so a title card left behind after a mode
change cannot be uploaded or restored as an active serial card. `PerGameFileTitle` is deliberately
unsupported for now: its identity follows the local ROM filename, which is not stable when the same
game has different filenames on two machines. Supporting it would require an emulator-independent
game identity rather than relabelling whatever `_N.mcd` files happen to be present.

Finally, the per-system settings dictionary is sanitized before migration. JSON nulls are legal to
deserialize despite C# nullable annotations, and settings are hand-editable; neither a null dictionary
nor a null entry may crash startup. An existing valid per-system entry remains authoritative even
when its override is intentionally empty, so a stale legacy field cannot restore a cleared path.

## 2026-07-26 — DuckStation discovery mirrors the emulator's data-root precedence

The earlier DuckStation decision described current and legacy directories in documentation order,
but that is not the selection order used by the emulator. EmuShelf now mirrors DuckStation's actual
data-root decision tree: an installation-local `settings.ini` is a portable trigger even without
`portable.txt`; an existing Windows `Documents/DuckStation` directory retains precedence over
LocalAppData; and native Linux honors an absolute `XDG_CONFIG_HOME` before the default
`~/.local/share` root. Flatpak follows the equivalent config root while retaining its data root as a
legacy fallback. Once DuckStation has selected a root, a missing `settings.ini` fails closed instead
of allowing EmuShelf to fall through to a different, inactive profile.

Provider-result attribution follows the same fail-closed rule. An exception raised while a target
is being constructed belongs to that target, and a runtime exception with only one participating
target belongs to that sole platform. Runtime exceptions after multiple targets are staged remain
global because the current engine does not expose which target raised them.

## 2026-07-26 — Flatpak RetroArch uses its host-visible per-app core directory

This supersedes the Flatpak RetroArch portions of **2026-07-21 — Typed launcher targets retain
portable ownership boundaries** and **2026-07-25 — Flatpak configuration is platform-aware, with
explicit legacy migration**. Their rationale that RetroArch cores are private to the sandbox was
wrong, not merely outdated. Empirical testing with flatpak 1.16.6 and Flathub RetroArch 1.22.2
showed that `$HOME/.var/app/<app-id>` is mounted inside the sandbox at the identical host path, and
the Flathub manifest already grants `host` filesystem access. A host `CorePath` can therefore be
validated normally and passed to `-L` without translation.

Linux now offers Direct/AppImage and Flatpak targets for RetroArch. For a Flatpak target, the core
picker derives `$HOME/.var/app/<app-id>/config/retroarch/cores` from the configured application id
and only enumerates core files already installed by the user. EmuShelf does not download or manage
cores and does not write RetroArch configuration, overrides, playlists, or achievement settings.
The existing ephemeral per-launch read-only filesystem grants remain unchanged: they are redundant
for the tested RetroArch manifest but harmless there and still load-bearing for PCSX2.

The validation was performed under Ubuntu WSL2, not on Steam Deck hardware. The open M27
Linux/SteamOS hardware-verification item therefore remains incomplete pending one real-device
launch and permission check.

## 2026-07-26 — Texture-pack inventory mirrors emulator loaders and remains external state

Installed texture packs are discovered through one read-only source per emulator installation and
are not stored as a flag on `Game`. A pack must contain replacement content accepted by that
emulator's loader; an ID-shaped directory containing only dumps or unrelated images is an attention
state, not a library match. Matching follows explicit emulator rules only: exact PlayStation serial,
hyphenless PSP game ID, exact or documented three-character Dolphin ID, and documented shared or
multi-disc behavior. Titles are never used as a fallback.

The library mark means "usable pack installed and matched." Effective global/per-game loading is a
separate versioned configuration result and stays unknown when EmuShelf cannot prove precedence.
Inventory, configuration, pack files, and game files are always read-only; Settings may rescan or
open a folder but never installs, repairs, moves, renames, or deletes a pack.

## 2026-07-26 — Texture inventory records evidence, not exhaustive file counts

Scanners stop after proving that a pack has replacement content instead of recursively counting
every image. Exact counts are neither required for the library mark nor cheap for large packs, and
would make an explicit rescan scale with every texture file. PPSSPP's automatically generated
`textures.ini` is not proof by itself: a directory containing only that file and the `new` dump
folder remains an attention state. A PPSSPP directory pack needs at least one supported image
outside `new`; a `textures.zip` pack needs a root `textures.ini` plus replacement image content.

## 2026-07-26 — A per-game emulator configuration file makes loading status Unknown, not global

`IniTexturePackLoadingResolver` answers Enabled/Disabled from a versioned global setting, but as
soon as a per-game configuration file exists for the game being asked about it returns Unknown
instead. PCSX2, DuckStation, and Dolphin all layer a per-game file over the global switch, and the
layering rules differ per emulator and per version. Reporting the global value while a per-game file
sits on top of it is exactly the confident wrong answer this feature forbids, and an emulator-by-
emulator precedence model is not something a read-only adapter can verify. Absent settings and
unrecognized boolean spellings are Unknown for the same reason — only a present, recognized, and
unlayered setting produces Enabled or Disabled.

## 2026-07-26 — One classification pass feeds both the library marks and the Settings totals

`TexturePackLibraryMap.Build` takes the completed snapshots plus one bulk `GetAllIdentifiers()` read
and produces both the per-pack classification list and the per-game match map. Settings and the
library therefore cannot report different numbers, and no library row performs a database read or a
disc parse of its own. `IGameMetadataStore.GetAllIdentifiers()` was added for this: the existing
per-row `GetIdentifiers(gameId)` would have made the pass N+1 over the library.

Two states exist specifically to avoid overclaiming. A usable pack whose identifier matches nothing
is `NoLibraryMatch` only when the library actually holds identifiers of that kind; otherwise it is
`IdentifierPending`, because identification may simply not have run yet. A Dolphin shared pack is
`SharedPack` and marks no individual game — it applies to everything, so marking every GameCube and
Wii title as "has a texture pack" would be misleading rather than informative.

## 2026-07-26 — PSP accepts CHD through the existing DVD-geometry reader

PSP imports were limited to `.iso` and `.cso`, so a folder of PSP CHDs scanned as empty and an
explicitly picked PSP `.chd` was offered only as a PS1/PS2 candidate. PPSSPP has loaded CHD since
1.15, so the gap was EmuShelf's alone.

No new container work was needed. A PSP CHD is DVD geometry — 2048-byte units, zlib or LZMA hunks —
which `ChdSectorSource` already decodes and which the committed chdman fixtures already prove
byte-exact against a source ISO. `PspGameMetadataReader` and `PspDiscHasher` both consume
`ILogicalSectorReader`, so CHD support is one dispatch branch in each, and the PARAM.SFO evidence
rule is unchanged: a CHD without a valid `PSP_GAME/PARAM.SFO` is never auto-imported as a PSP game.

The RetroAchievements algorithm version is deliberately **not** bumped. The PSP hash is PARAM.SFO
plus EBOOT.BIN read by logical sector, so a CHD and the ISO it was built from produce the same
digest; bumping would recompute every stored PSP hash to the identical value. A test pins that
equality across `.iso`, `.cso`, and `.chd`.

Because every PSP container extension is also a PlayStation one, the PS1/PS2 veto that validated
PSP evidence applies is now keyed off the PSP extension set rather than a hardcoded ISO/CSO pair,
so future PSP containers cannot silently reintroduce a misclassification.

Tests build CHDs with `ChdImageBuilder` rather than requiring a chdman install: it emits a real v5
header and a real Huffman-coded hunk map with a valid CRC-16 self-check, storing every hunk as
COMPRESSION_NONE — a shape chdman itself emits for incompressible hunks. The production decoder
reads it through the same path it uses for chdman output, and a round-trip test asserts every
logical sector matches the source ISO. The committed chdman fixtures remain the byte-exactness
proof for the zlib/LZMA/cd\* codec paths.

The added PARAM.SFO probe costs about 22 ms per DVD-geometry CHD, measured over a real 206-disc PS2
folder: import analysis of that folder went from roughly 0.3 s to 4.9 s. This is accepted rather
than optimized. Import is a user-initiated action that already reports progress, it is not the
startup path the performance rule protects, and every cheaper filter considered (volume descriptor
fields, filename hints) would decide "not PSP" on weaker evidence than the SFO itself — trading a
one-time five seconds for the possibility of silently failing to recognize a real PSP image.
CD-geometry CHDs are unaffected in practice (1–4 ms), since a PS1 disc's root directory is reached
without decompressing large hunks.

## 2026-07-26 — DuckStation file-title cards sync by exact filename

This supersedes the `PerGameFileTitle` exclusion in **2026-07-26 — DuckStation syncs configured
memory-card units, not its whole data directory**. A real portable DuckStation installation used
title-based cards in port 1 and file-title cards in port 2, with live saves in both. Rejecting the
second mode disabled every PlayStation backup and Settings hid the configuration exception as a
missing detected path. Requiring users to reconfigure a valid emulator layout would also make
existing port-2 saves stop being selected by DuckStation.

File-title cards now use a distinct manifest scheme and their exact safe `.mcd` filename as the
unit identity. Matching filenames round-trip normally; differing game filenames on another machine
cannot be mapped safely, so EmuShelf preserves the downloaded card without claiming DuckStation
will select it automatically. Settings shows that limitation as a non-blocking warning beside the
resolved directory. Genuine detection/configuration failures remain visible and disable destructive
replace actions instead of silently presenting an empty row.

## 2026-07-26 — An explicit texture rescan extracts its own matching evidence

Texture matching reads cached `GameIdentifier` rows, but GameCube, Wii, PlayStation, and PS2 write
no identifiers at import: `FileImportRules.ReadImportMetadata` returns evidence only for Mega Drive,
DS, GBA, SNES, and PSP. Their disc ids and serials were therefore written solely by the opt-in
network-metadata pass, so a user who never enabled that saw every Dolphin and DuckStation pack sit
at "identification pending" forever, with no way to resolve it from the texture UI.

An explicit **Rescan** now extracts the missing evidence itself, through the same
`IGameIdentifierExtractor` the metadata profiles already own. The extraction is a local header or
descriptor read and needs no network and no consent — only the catalogue/artwork stage does — so
this does not widen the metadata consent boundary. It stays narrowly scoped: explicit rescans only
(never the startup cached load), only systems whose installation actually holds a usable pack, and
only games with no stored identifier, so it never re-reads a disc whose evidence already exists and
never becomes a startup cost.

Texture-root overrides are also persisted through `ISettingsService` now; they previously lived only
in memory and silently reverted to auto-detection on the next launch.

## 2026-07-26 — Dolphin's texture root comes from Dolphin.ini, not from folder layout

Dolphin does name its Load directory in configuration — `Config/Dolphin.ini`, `[General] LoadPath`
— and its Paths settings let a user move it anywhere. A real ES-DE install has `portable.txt`
beside the binary, so the user directory *is* the adjacent `User` folder per Dolphin's own rule,
while `LoadPath` redirects to a `saves/dolphin/User/Load/` tree elsewhere on the drive, where the 31
packs actually live. Taking the first existing user directory and appending `Load/Textures` found
zero packs, with no visible reason.

A first attempt inferred the root by preferring whichever candidate user directory contained pack
folders. That produced the right answer on this install, but only by coincidence: it reads layout
instead of configuration, so it breaks the moment the folder moves, an old populated copy lingers,
or the packs are not installed yet. The resolver now reads `LoadPath` and appends `Textures`,
normalising the mixed separators and trailing slash Dolphin writes and resolving a relative value
against the user directory. An absent key is not an error — Dolphin then uses `<User>/Load`, so that
is the fallback — and an unreadable ini still resolves to the default while reporting why.
User-directory discovery follows Dolphin's documented rule as well: a `User` folder beside the
binary is authoritative only when `portable.txt` is present.

This puts Dolphin on the same footing as PCSX2 and DuckStation, whose texture roots were already
read from their own settings files rather than guessed. The library mark still means "installed and
matched": loading status stays Unknown unless `GFX.ini` proves otherwise, and the Settings override
remains the escape hatch.

## 2026-07-26 — DuckStation settings are read with DuckStation's own defaults

Reported from a real Steam Deck AppImage install: EmuShelf refused to sync PlayStation saves with
"DuckStation's settings.ini has no supported Card1Type." Current DuckStation only writes settings
that differ from its defaults, so a normal install has a small `settings.ini` with no `[MemoryCards]`
section at all. Treating an absent key as an unknown layout made the common case fail closed for a
configuration DuckStation itself considers fully specified.

An absent key now means the emulator default — memory cards under `<user directory>/memcards`,
slot 1 `PerGameTitle`, slot 2 `None` — and only a key that is present with a value DuckStation would
not accept still fails closed. This supersedes the "explicitly configured directory" requirement in
**2026-07-26 — Save-sync status and DuckStation identities fail closed at their real boundary**: the
fail-closed boundary is a value EmuShelf cannot interpret, not a value the emulator chose not to
write.

This also corrects the Linux root in **2026-07-26 — DuckStation discovery mirrors the emulator's
data-root precedence**. DuckStation roots its Linux user directory at `XDG_DATA_HOME` (default
`~/.local/share/duckstation`), not `XDG_CONFIG_HOME`; the previous branch pointed at the wrong
directory whenever `XDG_CONFIG_HOME` was set. By the same rule, a Flatpak install's user directory
is `~/.var/app/org.duckstation.DuckStation/data/duckstation` — the sandbox's `XDG_DATA_HOME` — with
the `config` path kept only as a fallback for hand-relocated profiles.

## 2026-07-26 — RPCS3 saves are addressed without their local account

RPCS3 is the fourth save-sync platform. Its hard disk is resolved the way RPCS3 resolves it: a
`portable/` directory beside the executable wins on every platform, then `RPCS3_CONFIG_DIR` and the
executable directory on Windows, `$XDG_CONFIG_HOME/rpcs3` (default `~/.config/rpcs3`) on Linux,
`~/Library/Application Support/rpcs3` on macOS, and the sandbox path for Flatpak. `vfs.yml` — read
from `config/` on Windows and the configuration directory elsewhere — supplies `$(EmulatorDir)` and
`/dev_hdd0/`; an absent file or empty entry means RPCS3's own documented default, while an
unreadable file or an unknown `$(…)` placeholder fails closed. Only those two top-level keys are
interpreted, so a future device entry cannot move the hard disk behind EmuShelf's back.

The account is the interesting part. `dev_hdd0/home/00000001` is machine-local: the same person can
be `00000001` on a desktop and `00000002` on a Deck, so embedding the account in the unit id would
make the same save two different cloud objects. Unit ids are therefore `rpcs3/savedata/<save>` and
the account is bound locally — that binding *is* the stable profile key. Binding is automatic while
only one account can be meant (a single account, or the single account holding saves) and fails
closed with an actionable message when several accounts hold saves; the existing per-system save
location override is how the user picks one, and it accepts RPCS3's folder, its `dev_hdd0`, one
account folder, or that account's `savedata`. No new settings field was needed. Those forms are
matched widest-container-first, because `dev_hdd0` has a `savedata` directory of its own — the
PS1/PS2 Classics virtual memory cards — and matching on a `savedata` child first would resolve a
`dev_hdd0` override to the virtual cards instead of the account's saves.

A save unit is a `savedata/<save>/` directory containing `PARAM.SFO` — the file that makes the
directory a save RPCS3's own save manager recognizes. Directories without it are partial copies, not
units. Trophies, `exdata` licenses, installed games, caches, configuration, and save states sit
outside `savedata` and are never enumerated. Verified read-only against a real 63-save RPCS3
install: every save name passed the id-safety filter and none of the sibling directories did.

## 2026-07-26 — RPCS3 trophies and virtual memory cards are their own namespaces

Trophy progress and PS1/PS2 Classics saves are things users lose when only `savedata` travels, so
both now sync. A trophy set is `home/<account>/trophy/<NPWR…>/` containing `TROPUSR.DAT` — the
communication id is the same on every machine, so it needs no library lookup — and a virtual memory
card is a `.VM1`/`.VM2` file in the console-wide `dev_hdd0/savedata/vmc`. They take the unit
namespaces `rpcs3/trophy/…` and `rpcs3/vmc/…`, leaving the shipped `rpcs3/savedata/…` ids untouched,
and each resolves under its own root so no namespace can address a sibling of another.

The virtual-card directory is console-wide rather than account-scoped, so it is only in scope when
the resolved location still sits in RPCS3's own `dev_hdd0/home/<account>` shape. A save folder
chosen anywhere else stays account-scoped instead of reaching up into a parent EmuShelf was never
pointed at. Verified read-only against a real install: 63 saves, 23 trophy sets, 2 cards.

## 2026-07-26 — RetroArch resolves per system, and the library decides whose save is whose

RetroArch is one installation serving five EmuShelf rows, so each row resolves its own effective
save directory from `retroarch.cfg`: `:` and `~` expand the way RetroArch expands them (application
directory, home), a per-core override at `<config>/<core>/<core>.cfg` is layered on top, and
`sort_savefiles_enable` appends the core's own folder name. Anything EmuShelf cannot resolve to one
exact directory fails closed with a message naming the setting — saves kept in the content
directory, sorting by content directory, no configured save directory, a missing `retroarch.cfg`,
and RetroArch's own cloud sync being enabled for saves, which EmuShelf refuses to double-manage.

The harder problem is that RetroArch's default is one shared save folder: every core writes `.srm`
into it, so the file name is the only evidence of which system a save belongs to. Claiming all of
them would have each of the four rows syncing the other three's saves under four different unit ids.
EmuShelf therefore matches save names against the library's own file names for that system, which is
the same evidence RetroArch used when it named the file. The rule applies to remote-only units too:
a downloaded save can only land under a system that has the matching game. A directory that is
exclusively this system's — because the core sorts into its own folder, or because the user chose
the folder explicitly — skips matching entirely. The cost is that a save whose game is not in the
library is left alone; the escape hatch is the per-platform save location.

Cores are an explicit verified table (Genesis Plus GX, Snes9x, melonDS DS, mGBA — all `.srm`), and
an unrecognized core fails closed rather than guessing an extension. Dreamcast is deliberately not
registered: Flycast writes VMU images to RetroArch's system directory or the save directory
depending on a core option, and that has not been verified against a real install. Verified against
one: the DS row claimed exactly the five DS saves in a shared folder of nine, leaving the GBA and
SNES saves and two saves whose ROMs are not in the library.

## 2026-07-26 — RetroArch saves are claimed by game name, not by a per-core extension list

This supersedes the verified-core table in **2026-07-26 — RetroArch resolves per system, and the
library decides whose save is whose**. That table paired each core with the extension it writes, and
the premise was wrong twice over. First, testing against a real install showed melonDS DS writing
`.sav`, not the `.srm` the migration docs implied — an allow-list would have synced none of that
user's 31 DS saves. Second, an extension list turns a core swap into silent data loss: switching a
DS core to DeSmuME changes the extension to `.dsv` and sync would have stopped without a word.

What is actually stable is the file *name*: every core names a save after the content file. A unit is
therefore any direct child of the resolved save folder that is not one of RetroArch's own artifacts
(save states by `.state*`/`.auto` shape, replays, screenshots, configuration, and extension-less core
hint files such as "Place NDS saves here"). Whose save it is comes from the same two rules as before:
a folder RetroArch sorts per core — or one the user chose — belongs entirely to that system, and a
shared folder is filtered by the library's own file names. Any core works now, including cores
EmuShelf has never heard of, so Dreamcast/Flycast is registered too; its shared VMU images live in
RetroArch's system directory and are still out of scope, while per-game VMUs land in the save folder
and match like anything else.

The core's *name* is still needed for one thing — the folder RetroArch sorts into — and that name is
libretro's `corename` ("melonDS DS"), read from the installed core's own `info/` entry. It is
explicitly not `display_name`, which names the system ("Nintendo - DS (melonDS DS)") and would
produce a folder that does not exist. A built-in table covers a handful of cores as a fallback, and
sorting with an unreadable core name fails closed rather than guessing a directory.

## 2026-07-26 — Sync latency is measured, and cloud sessions are scoped to what a pass needs

A launch-time sync was reported as taking about a minute. Measured on the real library, the local
half is not the cost: enumerating and hashing all 88 PS3 units — 1,520 files, 175 MB — takes about
270 ms, and 31 DS saves take 55 ms. The time is in rclone, so the log now records the wall clock of
each pass and the duration of every rclone invocation. A user (or a future decision) should not have
to guess which call a launch waited on.

Two rclone patterns were structurally wasteful regardless of what the timings show. Downloading any
single unit opened the session with `rclone copy <remote root> <inbox>`, which fetches *every*
platform's payloads — a one-save DS download could pull the whole PS3 collection. The service now
decides every unit's action before transferring anything and announces the units that need a remote
payload (a download, and either side of a conflict, since the loser is preserved as a backup), so the
session runs with `--files-from` over exactly those paths. A download that was never announced still
succeeds through a single-payload fetch, so scoping can never lose a save. Uploads and the scoped
download now pass `--no-traverse`: the remote holds every save ever synced, and rclone would
otherwise list all of it to decide whether to copy a handful of staged files.

Deciding before transferring also made the two-phase structure explicit. It is equivalent to the
previous interleaved loop because a unit's decision depends only on its own local, remote, and
baseline state — never on another unit's outcome.

## 2026-07-27 — The launch wait is a Drive path lookup, and it is now bounded

The instrumented log answered the question. A launch-time pass for PlayStation with nothing to do —
7 units, no transfers — took 22.2 seconds, of which 22.24 were a single `rclone cat` of `index.json`.
A pass moments later took 3.5 seconds for the same call. EmuShelf's own work is not involved: the
whole PS3 library hashes in 271 ms.

Measured directly against the configured remote, `cat` of the index costs 3.5–6.0 s normally, with
occasional ~20 s outliers. The same read addressed by the folder's own id — `--drive-root-folder-id`
— costs 1.7–2.1 s. Google Drive has no real paths, so `EmuShelf/Saves/index.json` is resolved one
listing request per segment on every call, and those requests are both slow and rate-limited.
EmuShelf now caches the folder's id in settings after one lookup and addresses the folder directly.
The id is not a secret and grants nothing; a failed lookup falls back to the path, and any failed
pass clears the cached id so a moved or recreated folder repairs itself on the next attempt.

The outliers are Google throttling, not something EmuShelf can fix — rclone's shared Google client id
is rate-limited (and, per rclone's own notice, retired during 2026). So the pre-launch pass is now
bounded: it gets 12 seconds, after which the game starts with the saves already on disk, exactly as
it does when a pre-launch sync fails. The post-exit pass keeps running to completion because nothing
is waiting on it. A cloud that is having a bad minute can no longer turn into a launcher that looks
hung.

## 2026-07-27 — A resolved save folder that does not exist is reported, not silently empty

A PSP sync on the Steam Deck "did nothing" with no error. That shape of failure is possible for every
platform: the provider resolves a path, the folder is not there, zero units are enumerated, and the
pass reports success. Detection now checks the resolved directory and says so in the platform row
when it is absent, naming the two things that cause it — the configured emulator is not the one the
user actually plays with, or the save location needs setting explicitly. An existing but empty folder
is normal (the emulator has not written a save yet) and is not flagged.

The same review found PPSSPP resolving `~/.config/ppsspp` on macOS, where PPSSPP actually keeps its
Memory Stick under `~/Library/Application Support/PPSSPP` — the root this repo's own texture resolver
already reads `ppsspp.ini` from. The save provider now agrees with it.

## 2026-07-27 — A personal Google OAuth client is imported as a file, and its secret never lands in settings

rclone's shared Google client is rate-limited — the multi-second `rclone cat` before a launch, and
the ~20 s outliers — and Google retires it during 2026. Using a personal OAuth client fixes both, so
Settings now imports the `client_secret_*.json` the Google Cloud console produces: choose the file,
press Connect, sign in once. Copying two long strings by hand was the only alternative and is the
kind of step users skip.

The split follows the rule the connect flow already had for the OAuth token: EmuShelf stores the
client *id* (public by design, and useful to show which client a remote uses) and never stores the
*secret*. The secret is read from the file the user chose, handed to rclone as one argv entry, and
dropped from memory as soon as the connection attempt returns; rclone's own config holds it beside
the token. The parser accepts either the `installed` or `web` section, and a file without both
values reports what to download from where rather than failing with a JSON error. Nothing about the
file's contents is ever logged or echoed into a status message.

## 2026-07-27 — The folder-id lookup was reading the wrong rclone output

The first attempt at caching the Drive folder id never worked, and the instrumented log is what
showed it: every pass ran `rclone config` + `rclone lsjson` and then still paid full price for
`rclone cat`, because `lsjson --stat` on the folder describes the queried path as its own root and
reports no id at all. The id only appears when the *parent* is listed, so resolution now lists the
parent and matches the folder by name. The resolved id is also adopted by the transport that found
it rather than rebuilding one, so the pass that pays for the lookup benefits from it and all of its
rclone calls are accounted for in one place — the previous split meant the header time excluded the
lookup the user was waiting on.

Measured against the real remote afterwards: lookup 1.5 s once, then a full index read of 255 units
in 1.8–2.2 s by id versus 3.2 s by path. rclone's own process start is 152 ms, so what remains is the
provider's round trip, and no amount of local work removes it: every pass has to ask the cloud
whether another machine changed a save. That is the floor this design has, and the 12-second launch
budget is what keeps a bad minute from reaching the user.

## 2026-07-27 — The cloud index is a commit, and is written after the payloads it describes

A PSP sync on the Steam Deck failed with "the cloud save payload for 'ppsspp/ULES00841' was not
found on the remote", and the remote confirmed it: the index listed thirteen PSP units while only
ten payloads existed. All three of the missing saves were present locally on the machine that had
uploaded them.

The cause was a single `rclone copy` of the whole outbox — payloads and `index.json` together.
rclone transfers concurrently, so a session that failed partway could land the small index while a
payload did not arrive. That is unrecoverable by itself, because the index carries the content hash:
the owning machine then reconciles to "unchanged" and never re-uploads, while every other machine
fails downloading it and, since one failure aborted the pass, loses the sync of every unit behind it.

Three changes, in the order the failure needs them. Payloads are uploaded first and the index second,
in separate sessions, so an interruption can only leave a payload with no index entry — harmless,
re-uploaded next pass — rather than an entry with no payload. A missing payload now raises a typed
per-unit condition that the service records and steps over, so one bad entry costs one unit rather
than the pass. And the transport drops such entries from the index it writes at the end of the pass,
so the machine that still has the save stops seeing "already on the remote" and uploads it. The
damage already on the remote heals on the next pass from either machine.

## 2026-07-27 — Every full sync verifies the cloud against itself

Auditing the remote after the index/payload defect showed the damage was not one platform's:
74 of 255 indexed units had no payload — 71 RPCS3 and 3 PPSSPP. (A first pass reported 79 and
included DuckStation and RetroArch; that was wrong. The index JSON-escapes apostrophes, so five
entries were compared against their unescaped file names and looked missing when they were not.)

The 71 name the second cause. The per-rclone-call timeout was two minutes for every kind of call,
and the first RPCS3 upload is 179 MB of save data and trophy sets. On an ordinary uplink that
session could not finish inside the cap, so it was killed after the small index had already gone up.
Timeouts are now split by what the call does: two minutes for a metadata round trip, thirty for a
transfer, because a transfer's duration is set by how much data there is, not by whether the network
is alive.

Repair could not rely on a failed download. The machine that owns a save never downloads it, so it
would never discover its own broken upload; only a second machine would, one failed download at a
time. A full manual sync therefore lists the remote once and compares it against the index, drops
the entries with no payload, and lets the owning machine upload them again on the same pass. That
listing costs one call on an operation the user already waits on, and it is deliberately not part of
the pre-launch pass, which is optimized for latency.

## 2026-07-27 — Status messages carry a severity, and only some of them expire

The library toast waited for a manual dismissal. Every message did — a rename confirmation sat on
screen as long as an import failure, and in Gamepad mode, where the toast had no dismiss button at
all, neither could be got rid of.

A single timer would have been wrong, because `StatusText` was doing three unrelated jobs. It
carries results ("Removed 1 game"), live commentary on work in flight ("Scanning PlayStation… 41
found"), and failures ("Import failed: …"). Expiring all three on one short countdown discards an
error before it can be read; expiring none of them is where we started.

Messages are therefore set through `SetStatus(text, severity)` rather than by assigning the
property, and the severity picks the lifetime. Results get five seconds. Failures get fifteen —
long enough to read, but still finite, because a stale error on a library that is now working is
its own kind of wrong. Progress gets no countdown at all: the operation that emits it replaces the
text with its own result when it finishes, and a scan that goes quiet for five seconds on a slow
folder must not look like it stopped.

Severity is also what marks a failure visually. The app's accent is red, so a red dot cannot say
"this went wrong" — the toast switches the dot for a warning triangle instead, and the distinction
survives whatever the accent colour is.

## 2026-07-27 — Restored view state is persisted by name, and only restored window bounds are saved

Nothing about the library's presentation survived a restart: view mode, sort, selected platform,
and sidebar state all reset every launch, and the window always reopened at 1240x800, centred.

Two choices in how this is stored. The sort column and library scope are written as **strings**
rather than enums, because both name view-layer concepts that Core deliberately does not model —
`LibrarySortColumn` and `LibraryScope` live in `EmuShelf.App.ViewModels`, and Core takes no
dependency on the UI. Names also keep the portable settings file readable and survive reordering
the enums. Unrecognized names fall back to defaults instead of throwing.

For the window, **only the restored bounds are ever persisted.** Maximized and full-screen bounds
describe the monitor, not a decision the user made about the window, so they are tracked separately
and the maximized flag is stored on its own. This is what lets quitting from Gamepad mode — which
is full screen — still reopen the desktop window at the size it was last dragged to. A saved
position is validated against the attached displays at startup, so a window last closed on a
monitor that is now gone reopens centred rather than somewhere unreachable.

Restoring assigns the same properties the user normally drives, so saving is suppressed while it
runs; otherwise the first launch after adding this would write defaults over a good remembered view.

## 2026-07-27 — What is deliberately not animated

Covers crossfade over their placeholder, the toast rises as it fades in, and desktop tiles lift
under the pointer. All of it is on Opacity or RenderTransform, which composite without triggering
layout, so it stays cheap across a fully realized grid.

The Gamepad hover and focus rings are excluded on purpose. They are the only indication of where
you are when navigating with a controller, and fading them in costs the input a frame of feedback
for no visual gain. A snapshot test that asserts the hover ring's opacity caught the first attempt
to animate them, which is the behaviour working as intended rather than a test to relax.

## 2026-07-27 — A scoped download session reports a broken entry instead of failing on it

Reviewing the repair work found it incomplete in the one place that mattered most. Scoping the
download session with `--files-from` names exactly the payloads a pass needs — including, on a
remote with broken entries, payloads that are not there. rclone fails the whole session over one
absent file, and the transport treated that as a failed download of everything, so a machine facing
the 74 broken entries would still have lost every unit behind the first one. A scoped session now
reports a non-zero exit rather than throwing: anything that did not arrive is caught per unit, where
it is already a recoverable condition. An unscoped session still fails loudly, because there the
exit code is the only signal there is.

Verification also gained a floor. An empty listing against a non-empty index is far more likely to
be a listing that did not work than a remote that lost every payload, and pruning on it would drop
the entire index. That case now reports nothing and leaves the decision to the next verification.

## 2026-07-27 — A repair completes in the pass that finds it

The first verified sync worked: the remote went from 255 index entries against 181 payloads to 181
against 181, with nothing broken. It also showed the repair only half-finishing. Verification marked
the 74 orphaned entries, but the pruned index was written by the pass's own flush at the very end,
so the reconciliation in between still planned against the entries it was about to remove — all 74
looked "unchanged" — and the saves themselves would only have been uploaded by a second sync the
user had no reason to know was needed. Worse, between the two the saves existed on exactly one
machine with nothing in the cloud.

The pruned index is now committed before the reconciliation reads it. The pass that discovers the
breakage is the pass that repairs it: one extra index write, only when something was actually wrong.

## 2026-07-27 — The transfer is a phase of its own, measured by rclone

The repair completed in one pass — the remote went to 255 indexed units against 255 payloads, with
nothing broken and nothing orphaned — and the log showed where the time went: `rclone copy 49517 ms`
for the 179 MB, against about six seconds for everything else. The progress bar had reached 255 of
255 long before that, because uploads are staged locally during reconciliation and transferred once
at the end. A counter that reads "finished" while the work runs is worse than no counter.

Progress now carries a phase. Reconciliation counts units, as before; the transfer reports its own
0-100 percentage, taken from rclone's own `--stats` output on stderr rather than guessed — EmuShelf
cannot infer it, since nothing it does locally correlates with bytes on the wire. Until the provider
has moved enough to report a percentage the bar is indeterminate, which is honest about not knowing
rather than inventing a number. Reading stderr line by line for this also means only its tail is
kept for error messages, which is all an error ever needed.

## 2026-07-27 — A save this machine cannot place is a skipped unit, not a failed sync

The Steam Deck, now on the current build, failed a whole sync on one unit: "the save provider cannot
safely materialize unit 'duckstation/per-game/file-title/Silent Hill …_2.mcd' in its active
configuration". That refusal is correct — the Windows machine writes filename-based cards in slot 2
and the Deck's DuckStation does not enable that scheme, so placing the card there would invent an
active card the emulator would not read. What was wrong is that it aborted everything behind it.

This is the same shape as the missing-payload defect and gets the same treatment: a typed per-unit
condition the reconciliation records and steps over. The unit is reported as left in the cloud
untouched, and the pass syncs the rest. Both machines keep their own configuration, which they are
entitled to; only the units that depend on the difference sit out.

It also has to be caught while planning, not only while applying. The local snapshot resolves the
unit through the same provider, so an unplaceable remote unit throws before any decision is made —
which is exactly where the Deck's pass was dying.

## 2026-07-27 — A skipped save is its own outcome, and says why in the row

Two machines can be configured differently, so a sync can succeed and still leave a save where it
was — and until now the only trace was a line in the activity log. Worse, the per-unit reasons were
recorded as `None`, the same value that means "both sides already agree", so the log counted a save
nobody synced as "unchanged".

`Skipped` is now its own action with the reason attached. The activity log lists them under their own
heading, and the last pass's reason is persisted per platform and shown in that platform's row, so
the answer to "why is this save not on my Deck" is in the place the question gets asked rather than
in a log the user has to know to open.

The two hints that lead to skipped saves are now written to be acted on rather than merely
understood. RetroArch's names the setting and its path — Settings → Saving → Sort Saves Into Folders
By Core Name — and warns that RetroArch does not move existing saves into the new per-core folder and
will not find them until the user does. DuckStation's says that cards are matched per slot and card
type, so a machine using a different type in a slot has no place for the other's cards. The two
Replace buttons gained tooltips naming the direction they overwrite and where the overwritten copies
are kept.

## 2026-07-28 — A failed cloud request is never evidence that a save is absent

The rclone transport keeps its original copy-only layout and stable `<unit-id>.payload` names; this
hardening does not introduce a new index format, immutable-object store, retention policy, or remote
deletion. Only rclone's documented directory-not-found and file-not-found exits establish absence.
Every other non-zero result is an operational failure, and a successful but zero-byte `index.json`
is invalid rather than an empty cloud.

Verification uses the index itself as its authority marker: after successfully reading a non-empty
index, a recursive listing must contain `index.json` before it can classify any payload as missing.
This prevents authentication, throttling, and partial-listing failures from pruning healthy entries.
Caller cancellation also kills and awaits the rclone child process before returning, so canceled
launch passes cannot leave transfers running against staging files that EmuShelf is cleaning up.

The same fail-closed rule applies to structurally ambiguous indexes: JSON `null` and duplicate unit
ids are invalid rather than alternate spellings of an empty or last-entry-wins index. Outbox and
index staging directories contain only files already selected for upload, so those two rclone copies
use `--ignore-times`; matching size and timestamp cannot skip a write that reconciliation committed.

## 2026-07-28 — Dolphin save locations come from Dolphin, and shared GCI folders stay shared

Dolphin does not have one trustworthy default save folder. Its effective user directory can come
from an explicit EmuShelf override, the launcher's `-u`/`--user` argument, portable mode, a Flatpak
sandbox, XDG, the Windows Documents directory, or the macOS application-support directory. The user
directory is only the start: `Dolphin.ini` can redirect raw memory cards, GCI card folders, and the
Wii NAND, while `GameSettings/*.ini` can redirect a particular game's GCI folder. Save sync therefore
reads those files without modifying them and uses Dolphin's defaults only when the corresponding key
is absent. A per-game device/raw-card/NAND layout that cannot be represented is an explicit skip,
never a reason to guess a path.

A GCI-folder card is shared by many games, so replacing its directory would delete unrelated saves.
Each game is instead one file-set unit: every sibling GCI whose embedded game+maker id matches the
unit. Logical filenames and bytes are hashed in ordinal order and transferred as one deterministic
archive. Restore validates every incoming GCI's structure and embedded id, displaces only the old
members of that same unit, and rolls them back if installation does not complete. This keeps unrelated
GCI files untouched while allowing a game with several GCI entries to reconcile atomically.

Raw GameCube cards remain monolithic units because their allocation tables span every save. Wii sync
is limited to each disc title's `title/00010000/<title-id>/data` directory; the rest of the NAND,
including console identity, Miis, channels, and emulator save states, remains local.

## 2026-07-28 — Dolphin card variants keep their filenames, and downloads verify before replacement

Dolphin can select several physical raw-card files for one slot and region by adding its configured
memory-card-size suffix (for example, `SRAM.USA.251.raw`). The suffix is therefore part of the cloud
unit id when present; the legacy unsuffixed id remains unchanged. This lets another machine restore
the payload to the exact filename Dolphin selects instead of silently creating an unused default-size
card. Multiple variants are independent saves. Slots A and B, however, may not resolve to the same
raw-card family or GCI folder: distinct cloud identities writing the same local bytes would make the
last restore win, so that configuration fails closed before any transfer.

Cloud metadata's content hash is now an installation precondition rather than evidence accepted on
trust. Every downloaded file, folder, and file set is staged, hashed using the same semantic hash as
its local snapshot, and compared before the live save is moved or replaced. A mismatch leaves the
live save and manifest baseline unchanged. For Dolphin, Settings validates the configuration and
shows those effective card/NAND locations while retaining the user directory as the configuration
root used by the provider.

## 2026-07-28 — Existing payloads make a missing or incomplete cloud catalog a hard stop

An absent `index.json` means “new cloud” only when the remote contains no save payloads. Payloads
without an index are evidence of an interrupted or collapsed catalog, not permission to rebuild the
global index from whichever platform happens to sync next. The transport therefore lists the remote
before accepting a missing index, and every catalog-changing flush refuses to upload or replace the
index when it can see payloads omitted from the catalog. A pending unit is the sole exception: its
unindexed payload may be the recoverable result of that same unit's interrupted earlier upload, and
the current commit makes it visible again.

Manual verification applies the same check in both directions. It still identifies indexed units
whose payload is missing, but now also stops on payloads the index omitted; automatic reconstruction
is deliberately deferred until the remote stores enough per-unit metadata to rebuild semantic hashes
without guessing. Separately, when local and cloud content already match, that agreement repairs an
older manifest baseline. This closes the interruption window where the cloud commit succeeded but
the local manifest write did not, preventing a later one-sided edit from becoming a false conflict.

## 2026-07-28 — No pending upload may claim an unindexed cloud payload

The earlier interrupted-upload exception is withdrawn. An unindexed payload with the same unit id as
a pending local upload is ambiguous: it may be this machine's incomplete transfer, but it may instead
be a newer save whose catalog entry was lost. Without metadata proving which, overwriting it could
destroy the only copy. Every unindexed payload is therefore a hard stop, including pending units, and
recovery remains manual until the remote format can identify and preserve immutable payload versions.

A catalog-changing flush also requires a previously read folder and its `index.json` marker to remain
visible immediately before the first upload. A missing listing or marker aborts rather than recreating
the remote from cached state. Catalog-integrity failures retain the cached Drive folder id so the next
attempt cannot silently switch to a same-named folder; only operational reachability failures clear it.
The added safety listing makes the former twelve-second launch budget unrealistically tight for known
healthy Drive timings, so pre-launch sync now receives twenty seconds while post-exit sync remains
unbounded by the launch budget.

## 2026-07-28 — Immutable per-save commits replace the global catalog as the source of truth

The global `index.json` protocol is retired as an authority because no ordering of “payload then
index” can make one mutable index safe across interruption and independent PC/Deck writers. Save
sync v2 stores each upload at an immutable, uniquely named version path and commits it with a unique
per-unit head marker uploaded afterward. A payload without a marker is an incomplete upload and is
ignored; retrying creates another safe version. Head markers are never replaced or deleted. Each marker carries a
per-unit Lamport generation and random tie-breaker, so simultaneous writers converge deterministically
from one recursive listing while retaining both immutable payloads. Different units cannot erase one
another, and a marker can never describe bytes from another writer.

Each commit uses a unique top-level remote directory rather than shared `heads/` and `payloads/`
directories. Google Drive permits duplicate folder names, so concurrent creation of a shared protocol
tree could split two first-time writers into different same-named folders. Unique commit roots remove
that distributed directory-creation race; the already pinned Drive save-folder id remains the only
shared directory identity.

Existing `index.json` entries are migrated in place to generation-zero markers that continue to
reference their untouched legacy payloads. A readiness marker is written only after every trustworthy
legacy entry has a v2 marker; after that, normal catalog reads require one listing and ignore the old
index. A damaged legacy index no longer blocks new v2 commits and is never overwritten or used to
guess metadata: its payloads remain preserved for manual recovery. A unit whose newest marker has no
payload is omitted from reconciliation while retaining that marker's generation internally; a machine
holding the save then uploads generation N+1. EmuShelf does not silently roll active cloud state back
to an older version, although every older immutable payload remains preserved.

Closing the main window while cloud sync owns its single-flight gate now defers shutdown until that
operation releases the gate. This keeps the application and rclone lifetime aligned during the
post-emulator-exit commit. The immutable protocol remains safe under forced process termination, but
ordinary shutdown no longer creates that interruption deliberately.

## 2026-07-29 — Revert the folder-per-commit cloud format

The v2 format above is withdrawn. Although its immutable markers handled interruption and concurrent
writers, placing every commit in a unique top-level Google Drive directory made EmuShelf's save root
visibly noisy and caused unbounded folder accumulation. That storage layout is not acceptable for a
user-owned cloud folder.

EmuShelf returns to the previously working v1 layout: one `index.json` and stable per-system
`*.payload` files. The rollback changes no cloud data and performs no cleanup; existing
`.emushelf-v2-commit-*` directories and `.emushelf-v2-ready` remain untouched until a separately
reviewed recovery tool can verify their contents before removal. The restored v1 client ignores those
names and creates no more of them. Any future concurrency design must keep internal state beneath one
clean folder and must be reviewed as a storage layout before implementation.

## 2026-07-29 — Dolphin GCI saves use ordinary file units

The sibling `FileSet` abstraction is withdrawn. A GCI is already a self-contained memory-card file,
while selectively replacing several siblings inside a shared card directory required a second,
provider-specific rollback protocol in the generic filesystem endpoint. That extra transaction path
was not justified by the save format and had a partial-cleanup failure mode that ordinary atomic file
replacement does not share.

Dolphin now exposes each GCI through the existing file path. The common one-file-per-game case keeps
the prior `dolphin/gc/gci/<slot>/<game-id>` identity so the cloud copies already created during local
testing migrate in place on their next upload. If a game owns several GCI files, their internal GCI
save-name fields provide stable suffixes so each remains independent even when physical filenames
differ between machines. Empty Wii title `data` directories are not saves and are no longer
enumerated; existing empty cloud entries remain untouched because save sync does not delete data.

## 2026-07-29 — Desktop selection is view-model-owned and observed before controls consume input

Grid and List use the same `GameViewModel.IsSelected` set, current item, and range anchor. Desktop
pointer presses are observed in the window's tunnel because `ListBox` may consume a right or left
press before an item-level handler runs; this keeps normal click, Ctrl/Cmd toggle, Shift replacement
range, and Ctrl/Cmd+Shift additive range identical in both layouts. A context request independently
targets an unselected game so keyboard- and pointer-opened menus agree. Changing the search query,
clicking the empty library canvas, or pressing Escape clears selection; sorting and switching layouts
do not.

Every game context menu and the contextual selection bar expose the same count-aware removal command.
One selected game is named in its confirmation; multiple games confirm the count once. Removal still
deletes only EmuShelf database records and never game files or covers.

## 2026-07-29 — Dolphin keeps one stable base GCI identity

When a game has several GCI files, the file with the lowest stable internal-name identity keeps the
unsuffixed `dolphin/gc/gci/<slot>/<game-id>` unit and only additional files receive suffixes. Keeping
the base unit at every file count prevents a one-file cloud entry from becoming an orphan when the
game creates another save. It also lets another machine reconcile the expanded card without
downloading the former base file twice under two different local names.

## 2026-07-29 — Web cover search is a user choice, not an automatic Google provider

The requested Grimmory-like flow is implemented as an in-app result picker, but not as a literal
Google provider. Grimmory's current cover picker uses DuckDuckGo Images rather than Google. Google's
official Custom Search JSON API now accepts no new customers and is scheduled to end for existing
customers in 2027; scraping Google Images would replace that dead-end dependency with an unstable,
unapproved HTML contract. EmuShelf therefore follows Grimmory's interaction and provider choice:
an explicit DuckDuckGo search with a bounded grid, plus the existing local-file option.

A web result has title similarity but no trustworthy game identity, region, or artwork license, so
it never enters `MetadataSystemProfile` and is never chosen automatically. The platform cover ratio
only reorders results within small search-rank bands; it cannot filter a legitimate alternate shape
or promote a distant result over the search engine's first page. The user-selected image passes
through the existing content-type, 8 MiB, and signature checks, is imported as a normal user-owned
cover, and leaves no preview or staging file behind. This keeps automatic exact-id metadata
conservative while giving unmatched ROMs a practical cover workflow.

The picker treats search-result hosts as untrusted. It accepts HTTPS only, resolves the initial
image/thumbnail host and every redirect, rejects loopback, private, link-local, and other
non-public addresses, disables proxy and automatic-redirect behavior for this transport, and pins
the validated DNS answer to the outbound socket. Downloaded headers must also declare no more than
40 million pixels or 16,384 pixels on either edge before Avalonia decodes them. Previews use the
codec's scaled decode off the UI thread and enter the ranked grid independently as each bounded
download finishes; the selected original receives the same dimension check during normal cover
import. These checks apply to manual web search without changing trusted automatic metadata hosts.

## 2026-07-29 — Settings updates are atomic, launch shortcuts own context, and navigation reflects the library

Every component that owns one settings section now updates that section through a single serialized
read-modify-write operation in `ISettingsService`. Loading and saving independently was insufficient:
cloud-sync results and texture-pack overrides retained the startup `AppSettings` snapshot and could
write an older theme or interface preference back after the user changed it. Atomic scoped updates
make the JSON file the latest source of truth without introducing a second settings store.

Interface mode has two layers. An unqualified launch uses the persisted Desktop/Gamepad preference;
`--gamepad-ui` and `--desktop-ui` force one launch and never mutate that preference. This lets a Steam
Gaming Mode shortcut and a desktop shortcut share the same portable AppImage and data directory
without whichever context ran last changing the other one's next startup.

Platform navigation represents library contents, not the complete capability catalogue. Desktop and
Gamepad hide platforms with zero database entries by default; import and emulator Settings still use
the complete registered system list. An unavailable entry remains sufficient to show its platform,
because a disconnected removable drive is a recoverable library state rather than an empty library.
General Settings persists a `Show empty platforms` escape hatch for setup and preference.

## 2026-07-29 — Settings serialization crosses process boundaries and navigation refresh preserves intent

The portable settings transaction is guarded by a sibling lock file as well as a process-local lock.
The lock file remains in `Settings/`: deleting it after release would let a third process create a new
file while another process still holds the old file handle, splitting the lock. This makes concurrent
Steam and desktop launches serialize their read-modify-write operations before the existing atomic
rename replaces `settings.json`.

Platform membership is queried with `SELECT DISTINCT SystemId` rather than materializing the entire
game library. Library reload owns the empty-selected-system fallback, so every import, rescan, sync,
and removal path reaches All Games consistently when its platform disappears. A refresh also keeps a
tentative Gamepad rail position while the rail has focus; active-scope synchronization resumes after
focus leaves the rail.

## 2026-07-29 — Desktop chrome, semantic color, and navigation artwork share one visual contract

The main Desktop window uses Avalonia-drawn, theme-owned chrome instead of the native title strip.
Windows 10 cannot officially darken its native caption, while extending under full decorations
causes the system title to overlap EmuShelf's sidebar header. The window therefore keeps its public
`Title` and taskbar identity but draws explicit minimize/maximize/close controls, marks both header
surfaces as native drag regions, and delegates all eight resize edges/corners through Avalonia's
`WindowDecorationProperties` roles. Caption controls are positioned against the live window bounds,
not the library's desired width, so wide list columns cannot push them off-screen at high DPI.
Gamepad mode remains fullscreen and does not show Desktop caption controls.

Color tokens now carry one meaning each: coral remains brand/selection, gold identifies
achievements, blue identifies informational or in-progress feedback, green is reserved for success,
amber for warnings, and red for destructive or failed states. Platform and collection bitmaps keep
their licensed source pixels but render with uniform scaling inside the same neutral 26-point icon
well. This equalizes portrait, landscape, and 32-pixel collection assets without redrawing or
relicensing them; the selected well receives the existing coral focus border.

## 2026-07-29 — Custom title-bar drag regions never own toolbar input

The Desktop headers remain native title-bar drag regions, but every control or control group inside
them is explicitly assigned Avalonia's `User` decoration role. This makes non-client hit testing
redirect input to grid/list mode, search, Gamepad mode, theme, Settings, and navigation controls
before it considers the containing header as a window-drag target. The caption buttons alone occupy
the top-level overlay; no full-width transparent surface may sit above the toolbar. The window-sized
root keeps those caption buttons aligned to the live client edge without widening their hit target.

## 2026-07-29 — Grid state indicators stay inside the cover footprint

Selection and multi-disc state must not change a library tile's measured height. The Desktop
selection ring is inset within the cover frame because `UniformGridLayout` may clip pixels rendered
outside its row boundary. A multi-disc title uses its existing cover badge for both count and active
disc: it reads “2 discs” while disc 1 is current and “Disc 2 of 2” after another disc is selected.
Desktop and Gamepad grids do not add a conditional status row beneath the title; the list uses the
same compact label in its already-present metadata line.

## 2026-07-29 — Multi-disc badges always identify the active disc

The cover badge uses one stable meaning for every multi-disc title: “Disc N of M,” including when
disc 1 is active. A badge that alternates between the collection size (“2 discs”) and the active
position (“Disc 2 of 2”) makes the same surface communicate two different things. The explicit
position also confirms which disc will launch without requiring the user to remember whether a
non-default selection was made.

## 2026-07-29 — Virtualized Desktop tiles do not scale outside their cells

Pointer hover is communicated through the cover's stronger border and shadow, not by scaling the
whole game tile. `UniformGridLayout` clips transformed content at virtualized cell boundaries; when
a selected tile remained under the pointer after a click, scaling it by 1.025 moved the inset
selection ring beyond the cell and removed its top edge. Keeping the tile transform at identity
preserves the complete selection outline while retaining visible hover feedback.

## 2026-07-29 — Desktop hover motion uses reserved in-cell headroom

Desktop cover tiles retain animated motion, but use a three-pixel upward translation instead of
scaling the whole tile. Each repeater item reserves four pixels above the card, so the translated
selection ring remains inside its virtualized cell. Border and shadow changes continue alongside
the lift. This preserves responsive hover feedback without cropping the selected cover or changing
cover width and column spacing.

## 2026-07-29 — Gamepad surfaces keep stable geometry and explicit controller states

Gamepad library rows reserve a fixed title zone beneath a bottom-aligned cover shelf, so mixed cover
ratios do not move or hide titles. Controller guidance is rendered as compact button caps, overlays
use a height appropriate to their workflow, and destructive actions are visually separated from
ordinary choices. The achievements overlay must always render one of its meaningful states—loading,
results, empty, offline, or disconnected—rather than presenting a blank panel while data is absent.

## 2026-07-29 — Gamepad overlays are content-sized with bounded scrolling

Controller menus, prompts, and action sheets grow from their visible content instead of reserving a
fixed dialog height. Achievement-only header fields are collapsed as one group so missing nested
data cannot leave invisible rows in other overlays. Option lists and achievement rows have bounded
scroll regions for unusually long content, while the surrounding sheet remains centered and compact.
Achievement presentation distinguishes a loaded game with no available achievements from loading or
missing cached details, and collection replacement restores focus to a live achievement row.

## 2026-07-29 — Gamepad layout constraints follow visible content and preserve navigation context

The overlay header and footer remain auto-sized, while the middle body owns the flexible height and
scrolls inside the minimum supported window instead of pushing controller hints out of view. Cover
shelf height is recalculated from the filtered games currently on screen, not hidden search results.
Achievement refresh restores focus by achievement id when that achievement still exists, and every
unexpected refresh failure resolves to an explicit cached or uncached state rather than escaping a
fire-and-forget task.

## 2026-07-29 — Optional sync content reuses the stable catalog and stays opt-in

Cheats/patches and save states use per-file namespaces beneath each existing emulator prefix; the
stable `index.json` and payload protocol are unchanged. Both kinds default off. Cheats and patches
may join normal save passes when enabled, while states run only in manual Sync all/replace actions.
The existing index entry records the detected emulator version, libretro core version where
applicable, and CPU architecture; state unit ids stay stable so emulator upgrades do not create a
new generation of orphaned objects. Incompatible remote states remain indexed and are reported
rather than restored.
Retention selects the newest configured number per game across local and remote candidates without
deleting older local or cloud files. Auto, resume, undo, and backup states are excluded.

Dolphin Gecko/Action Replay sections are intentionally not copied: they live inside the same
GameSettings INIs as machine-specific per-game emulator settings, which this milestone explicitly
does not sync. Copying the complete INI would violate that boundary; section-aware merge can be
revisited only with the per-game-settings work.

## 2026-07-29 — Optional sync is manual, observable, and independently detectable

This supersedes the automatic-cheat portion of the preceding decision. Real portable emulator
installs can place thousands of bundled/community database files in the same cheats and patches
roots as user-authored files. Hashing and reconciling those files before every launch would put a
large, optional catalog on the game's critical path. Cheats, patches, and states therefore run only
in manual Sync all/replace actions; ordinary in-game saves remain the only automatic pre/post-launch
content.

Settings reports each optional kind independently with its exact resolved folder, selected/total
file count, size, compatibility identity, and any advisory error. Failure to resolve one optional
folder never invalidates the ordinary save location. A direct PCSX2 memory-card override cannot
identify sibling content safely and reports that limitation instead of inventing paths beneath the
card folder.

State compatibility is provenance attached to content, not a label inferred anew from the emulator
installed today. The local manifest retains the compatibility identity alongside the content hash;
unchanged bytes keep that identity after an emulator upgrade, while genuinely changed bytes receive
the current identity. This prevents an upgrade from silently certifying an old state as compatible.
Executable version strings are normalized across packaging formats, and native executables without
embedded version resources fall back to their bounded `--version` command.

## 2026-07-29 — Manual state sync includes every eligible state

This supersedes the retention rule in the earlier optional-sync decision. Enabling save-state sync
means every manual state currently present in the resolved folder participates in manual Sync all
and replace actions. A newest-N selector made the option's meaning incomplete, could hide a usable
state behind newer incompatible states, and did not reclaim cloud storage because synchronization
never deletes local or remote files. Automatic, resume, undo, and backup states remain excluded,
and compatibility checks still prevent states from being restored by a different emulator build.

## 2026-07-31 — Cheats and patches are not synced; save states still are

This supersedes the cheat/patch portion of the optional-sync decisions above. The optional cheat
and patch namespaces pointed at each emulator's whole cheats/patches folder. On DuckStation and
PCSX2 that folder holds the community database the emulator ships and can redownload, not files
the user wrote: one real library staged 4,496 DuckStation `.cht` files averaging 3.4 KB, 579 PCSX2
cheat files, and 702 `.pnach` patches — 5,917 files per pass, of which 4,496 were one database.

Cost, not principle, decides this. On a per-file-metered provider those thousands of small files
were the entire wall-clock cost of a sync while carrying 24% of its bytes, so the transfer appeared
to freeze at 79% — where the large saves end and the small-file tail begins — for tens of minutes.
The content is identical on every machine and recoverable from the emulator, so syncing it buys
nothing. Save states stay: they are genuine per-machine user data, and the version guard already
governs when they may be restored.

The unit-id namespaces `cheats` and `patches` remain excluded from `ISaveLocationProvider.OwnsUnit`
although nothing writes them any more, so payloads left on a remote by an older build are never
claimed and resolved as local saves. Sync remains copy-only, so those payloads are not deleted.

## 2026-07-31 — A cloud flush commits in batches

The flush uploaded every staged payload and then wrote `index.json` once. Because the index carries
the content hash that decides what changed, that made a pass all-or-nothing: an interrupted run
lost all of its uploads and re-staged the identical set next time, so a large first sync could never
converge. Payloads are now uploaded in batches bounded by both count and size — either bound alone
leaves a bad case — and the index is committed after each batch. Payload-before-index ordering is
unchanged and is what makes a partial commit safe to resume from.

Transfer progress is anchored to saves rather than bytes for the same reason the old percentage
misled: a byte percentage races through the large saves and then appears stalled for the whole
small-file tail. The batch's own byte progress is folded in only to keep the bar moving within a
batch. Missing-payload pruning is applied to the first commit so a later batch's failure cannot
take it down with it.

## 2026-07-31 — A launch-triggered sync declines rather than queues

`SyncSystemAsync` now takes the sync gate with a zero timeout and returns `AlreadyRunning` when a
manual pass holds it. A launch-triggered sync is work the user did not ask for; waiting its turn
spent the whole pre-launch budget on the queue and stalled the post-exit pass, which has no budget,
behind a manual sync indefinitely. The launch proceeds on the saves already on disk exactly as it
does when a pass fails, and the manual pass in flight covers that system anyway. Manual syncs are
now cancellable from Settings, which is safe precisely because the flush commits in batches.
