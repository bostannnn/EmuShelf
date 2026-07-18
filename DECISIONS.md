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
