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

## 2026-07-29 — Settings writes tolerate transient Windows file locks

Settings forms persist their edited cloud-save paths in one transaction before starting a manual
operation. Atomic settings replacement uses a unique sibling temporary file and a short bounded
retry when Windows reports an I/O or access-denied error, since antivirus, indexing, and backup
software can briefly open the destination without delete sharing. Permanent failures still surface
to the user, and the existing settings file remains intact when replacement cannot complete. An
update also fails without writing if the current settings cannot be read or parsed; only a plain
startup load may fall back to defaults.

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

## 2026-07-31 — Dreamcast accepts CHD by reading the container's own track table

CHD joins `.gdi` as a supported Dreamcast packaging. The earlier decision deferred it until a
logical-track reader existed with parity fixtures; this is that reader. A CHD is accepted only when
its metadata declares a track layout and a declared data track really begins with the
`SEGA SEGAKATANA` IP.BIN marker, so the extension — shared with PlayStation, PS2, and PSP — is
never treated as evidence. A validated Dreamcast image now vetoes the PlayStation systems for that
file exactly as PARAM.SFO evidence already vetoed them for PSP.

Three properties of chdman's GD-ROM layout drive the reader, each verified by round-tripping real
discs back to GDI sets with chdman 0.249 and requiring identical reads:

- A track's declared `FRAMES` accumulate into the next track's disc address, which is what lands
  the high-density track on LBA 45000. `PAD` is the tail of that extent the dump never stored.
- Every track is stored on a four-frame boundary, so a track whose extent is not a multiple of four
  shifts each later track's physical position away from its disc address. In the committed fixture,
  as on a real disc, track 03 addresses 45000 but lives at frame 45004.
- The low-density area opens with an IP.BIN copy of its own. The boot header is the first one at or
  past LBA 45000, mirroring the GDI reader's choice of track 03 over track 01; a Dreamcast disc
  pressed as a plain CD has no such area and keeps its single header.

Each frame's user data is located from the track's declared type (`MODE1_RAW` and the rest), not
from the frame's content. The content heuristic the PlayStation reader uses cannot recognize a
sync-stripped frame past 99 minutes, where the encoder's own minutes field stops being valid BCD —
and a GD-ROM is 122 minutes long, so most of every disc is past that point. The heuristic itself is
unchanged, so no PlayStation hash is invalidated.

A Dreamcast CHD is catalogued by its IP.BIN product number alone. Redump's SHA-1 covers a track
file's raw 2352-byte frames, and this reader deliberately does not regenerate the sync and ECC
bytes the CD codecs strip, so a hash taken from a CHD could not match the catalogue. The serial
fallback already exists for GDI sets and stays disabled for images whose own name or folder labels
a translation, patch, or hack. The RetroAchievements algorithm version is unchanged: the hash is
IP.BIN plus the boot executable however the disc is packaged, so a GDI set converted to CHD keeps
its stored hash.

## 2026-07-29 — Each library mode owns its cover sizing, and a scope change empties the grid first

Two faults reported together from a Steam Deck: switching desktop → Gamepad produced crowded,
overlapping tiles with games that "straight up disappear", and LB/RB sometimes showed one
platform's games under another platform's name. They turned out to be independent, and the second
made the first look worse.

Cover sizing was one set of state serving two grids. `LibraryViewportWidth`, `GridCoverWidth` and a
single `GridHorizontalPadding` were shared, and the Gamepad viewport was assigned *into*
`LibraryViewportWidth`, so whichever grid last raised `SizeChanged` defined the size for the grid
that was actually on screen. The inset was wrong for one of them by construction: the desktop
measures a ScrollViewer whose ItemsRepeater carries a 32/28 margin inside it, while the Gamepad
ScrollViewer's own margin is already excluded from its arranged size and its repeater adds none —
so the 60px was subtracted from a width that never contained it.

Each mode now has its own viewport and its own inset, and the cover width is derived from whichever
is active. The consequence that mattered was not the cover width but `GamepadColumnCount`: D-pad
up/down steps a whole row, and the stride was computed from the mismatched numbers. Tests across
four viewport widths show it disagreed with the rendered column count at *every* one, which is why
Up/Down landed on the wrong tile and the reveal scrolled the grid to it.

The second fault was a visible window, not bad data. The rail, title and count move to the new
platform the instant the selection changes; `Games` was only replaced two awaits later, and the
code already said so ("Games still contains the previous scope here"). A scope change now empties
the visible grid before the first await, and the empty-library and no-results panels are suppressed
until the read completes — claiming a platform is empty before reading it is its own wrong answer.
Re-reading the scope already on screen deliberately keeps its tiles, so availability passes and
rescans do not flash.

Related: the cover crossfade now cuts rather than fades when a virtualized tile is recycled onto a
different game. The fade is there to soften a cover arriving from disk; on recycling it dissolved
the previous game's artwork over the incoming tile, which during fast LB/RB read as the wrong
artwork appearing and made the stale window above considerably more visible.

Two things deliberately left alone after checking them. The Gamepad tile's fixed 58px title row
does not clip: the title is already `MaxLines="2"` with ellipsis, which cannot exceed it, and
making the row a minimum would give tiles variable heights that `UniformGridLayout` — which derives
one uniform cell height — cannot represent, reintroducing the overlap. And `RefreshNavigationSystems`
already runs after the load-generation check, while `MovePlatformAsync` reads `NavigationSystems`
synchronously with no await between the lookup and the index, so a rebuild cannot move the rail
under a platform switch.

## 2026-07-31 — Arcade is FinalBurn Neo, identified by set name, synced like any RetroArch core

Arcade support is one platform backed by exactly one emulator, the RetroArch `fbneo_libretro` core.
No MAME, Naomi/Flycast, Atomiswave, or TeknoParrot: each brings its own romset universe, BIOS
matrix, and failure modes, and the goal is a platform a user can populate and launch, not a
romset-management tool. BIOS placement and ROM repair stay the user's job and RetroArch's system
directory, deliberately outside EmuShelf.

A game is identified by the one thing FinalBurn Neo itself keys on — the archive's file name. The
core loads a set by matching the zip basename to a romset short id (`kof94.zip`), so the basename is
the identity, and the FBNeo DAT turns that short id into a human title through its game `name` →
`description` mapping. EmuShelf does not open the zip or hash its contents on the import path:
content-CRC corroboration would buy marginal certainty at the cost of a zip reader and a CRC-32
implementation the app does not otherwise need, and an unknown or renamed zip keeps its filename
title rather than being rejected, because it is the user's file to keep.

The FBNeo DAT is Logiqx XML, not the clrmamepro text the console catalogs use, and it inverts the
convention: the game `name` attribute is the key and the title lives in a `description` element.
Rather than add a second catalog and a routing layer, the existing downloader and cache learned a
streaming XML parse path selected per profile, which skips `isbios`/`isdevice` sets; the catalog
size cap became per-profile because the arcade DAT carries roughly eight thousand sets with full ROM
hashes. BIOS archives such as `neogeo` are excluded twice — a small bundled set-name list hides them
at import, when the DAT is not yet present, and the DAT's `isbios` flag excludes them during
enrichment.

Clones are listed as their own library entries rather than folded under a parent, because EmuShelf
is one-file-one-game and the user physically owns whichever zips they have; each clone carries its
own `description`. Arcade art is landscape because arcade output is 4:3 and box art barely exists for
it, so the card reuses the wide cover ratio SNES already proved and resolves title screens first,
then snaps, then the rare boxart, before the bundled placeholder.

Save sync treats arcade as just another RetroArch core. FinalBurn Neo writes battery and NVRAM data
as `.srm` and its save states as `.state` into RetroArch's save and state folders, named after the
loaded zip, so the generic RetroArch save descriptor covers it unchanged — one file per game,
matched by name, with the same per-core-version gating that refuses to restore a state written by a
different core build. Nothing about arcade needed a bespoke save path, so it reuses the descriptor
the other RetroArch systems already share.

## 2026-07-31 — Gamepad rail is a passive LB/RB indicator, not a D-pad target

The controller library had two focus planes — the cover grid and the platform rail — bridged by
"D-pad Up from the top row enters the rail." That let the grid selector climb out of the grid, and
it exposed a deeper defect: three code paths owned three disagreeing index spaces. LB/RB walked
`[All Games, Recently Added, systems…]`, the rail tabs were `[All Games, Collections, systems…]`,
and Up-into-rail mapped the Recently Added *scope* onto the Collections *tab* (whose highlight even
bound to `IsRecentlyAddedSelected`). So shoulder input could land on a scope with no lit tab, and
Collections was unreachable by the bumpers at all.

The rail is now a **passive indicator**: the d-pad/stick move only inside the cover grid (Up on the
top row clamps), and platforms are switched solely by **LB/RB**, which cycle **one** ordered list —
`[All Games, systems…]` — with wrap at both ends. This eliminates the third (up-into-rail) index
space rather than reconciling it: the rail no longer owns an index, it just reflects
`IsAllGamesSelected` / platform `IsActive`. Collections and Recently Added are not platforms, so
they are off the cycle — Collections lives in the Start menu, Recently Added in the Collections
overlay. From an off-list scope the first bumper press snaps to All Games so a controller can never
dead-end. `IsGamepadRailFocused`, `GamepadRailIndex`, and their reveal/focus plumbing were deleted.

Two selector-disappearance races were fixed alongside: entering Gamepad mode now seeds the gamepad
viewport from the desktop's so `GamepadColumnCount` is never left at its default of 1 (which made
row-wise Up/Down step a single tile), and `RevealFocusedGame` retries on the next layout pass when
the target tile is not yet realized, so the ring is never stranded on an off-screen tile. The left
stick also resolves a diagonal push to its dominant axis so one flick moves a single cell.

The grid column count is now reported by the **view**, which actually lays the grid out, rather than
trusted from width arithmetic alone. The arithmetic (`UpdateCoverLayout`) can be momentarily stale
relative to the real layout, and a too-small count made Left/Right clamp partway across a row —
"stuck at the second column." The window reads the true count from the realized tiles' rows (the
most-populated realized row) after every layout and reports it through
`SetRenderedGamepadColumnCount`; the arithmetic stays as the pre-layout fallback and for headless
tests with no view.

## 2026-07-31 — Library connections get a busy timeout so overlapping read/write don't blank the grid

The library grid could go blank after rapid platform switching and stay blank until relaunch.
Root cause: `LibraryDatabase.CreateConnection` opened SQLite with the default rollback journal and
a `busy_timeout` of 0. The app reads the library on background threads (a platform switch loads the
new scope) while other work writes it (availability passes, RetroAchievements, save sync). With a
zero busy timeout a reader that overlaps a writer fails with `SQLITE_BUSY` immediately; the reload's
`catch` swallowed that and left the grid empty (the outgoing tiles were already dropped for the
scope change). Making Left/Right platform switching wrap around increased switch frequency and made
the collision easy to hit.

Every connection now sets `PRAGMA busy_timeout = 5000`, so a reader waits for a short concurrent
write instead of throwing. Chosen over switching the database to WAL: the busy timeout is a one-line,
side-effect-free change, while WAL adds `-wal`/`-shm` sidecar files that complicate the "portable
Data/ folder is safe to move while idle" rule the no-pooling connection policy exists to protect.

## 2026-07-31 — A Gamepad launch maximizes on its first switch to Desktop

Switching to Desktop mode on a device that launched straight into Gamepad (a Steam Deck / TV) opened
a small floating 1240×800 window — the default restored size — which reads as a "weird size" on a
handheld. `WindowInterfaceModeService` used to capture the transient startup window as the "desktop
state" and restore it.

It now distinguishes a real desktop session from none. A Desktop launch records its window and a
trip through Gamepad restores it exactly (maximized stays maximized — the existing guarantee). A
Gamepad launch records no desktop state, so the first return to Desktop maximizes instead of
restoring the startup window; after that the user's chosen desktop window is remembered normally.
Desktop-PC behavior is unchanged.

## 2026-08-01 — Opted-in save states follow the complete launch lifecycle

The manual-only restriction on save states is superseded. A user who enables state sync expects a
state written on one machine to be available before play on another, so enabled manual states now
reconcile before launch and after an EmuShelf-tracked emulator exits. The emulator/core-version and
CPU-architecture guard remains authoritative, and automatic/resume/undo/backup slots remain
excluded.

There is no application-level pre-launch time budget. Launch waits for synchronization to complete;
an operational failure is still advisory and launches with the data then on disk. Ordinary battery
and memory-card saves commit in a first phase before the state phase, so a later state failure cannot
strand a successfully reconciled critical save in local staging. A launch-triggered pass continues
to decline immediately when another sync owns the single-flight gate.

## 2026-08-01 — Remembered library roots are managed per platform without owning game files

File-based systems can own more than one recursive scan root. Emulator Settings shows those roots
as library configuration, but changing or forgetting a root changes only EmuShelf's database and
never moves or deletes ROMs. A replacement root is scanned before it is committed. Existing game
records beneath the old root keep their ids, titles, covers, and metadata only when a stable
identifier read from the replacement matches persisted evidence for the old entry; a matching
relative filename is not sufficient. Unverified records remain and become unavailable, while the
replacement is imported as a separate game. A destination path already owned by another game
aborts the replacement atomically.

RPCS3 is excluded because its `games.yml` remains the source of truth for the PlayStation 3 library.

## 2026-08-01 — Gamepad redesign has no clock; DuckDuckGo precedes ScreenScraper

The NeoStation-inspired Gamepad work borrows couch-first information hierarchy and complete
controller workflows, not branding, artwork, or source. The reference mockup's clock is excluded
because it consumes persistent navigation space without advancing a library task. The existing
explicit DuckDuckGo cover search is the first Gamepad scraper backend because it is already bounded,
user-driven, and isolated from automatic metadata enrichment. ScreenScraper.fr is a later Phase 5
provider: its application and user credentials, quotas, platform mapping, metadata provenance,
regional media selection, and batch behavior require a separate reviewed integration rather than a
silent replacement of the current cover picker. Fixed A/B/X/Y semantic colors remain independent of
future full-palette themes so controller prompts retain their meaning.

## 2026-08-01 — Reference screenshots govern Gamepad proportions, not the HTML mockup

The interactive HTML artifact remains a list of product ideas only. It is not a dimensional or
styling specification. Phase 1 is reviewed against the actual EmuShelf render and the supplied
NeoStation screenshot: related dock controls share one 60px height, achievement progress is a
compact count-plus-bar pill without a separate percentage, and the focused title shows its real
launch filename instead of redundant format/"Available" labels. EmuShelf keeps its own platform
rail, rectangular per-system artwork, and bottom controller hints rather than copying NeoStation's
square-card grid or left action rail.

## 2026-08-01 — The base Gamepad footer is information, not a controller legend

A populated-library comparison showed that equal control heights alone did not make the footer
clean: the persistent Menu/count/prompt row still competed with the focused-game information and
actions. The base shelf therefore uses one 104px row containing only platform/title/source,
achievement progress when available, and Play. LB/RB remains visible on the platform rail. The
library count and direct X/Y shortcut legend move into the system Menu, while modal overlays keep
their own contextual controls. Achievement progress uses a fixed clipped track rather than the
theme's generic stretching ProgressBar template so its geometry remains stable at couch layouts.

## 2026-08-01 — Achievement sorting is cache-backed; community rarity is deferred

The controller achievement browser offers All, Locked, and Unlocked filters plus Default, Points,
Unlocked first, and Recently unlocked ordering. Every choice is derived from fields already stored
in the portable achievement-detail cache, so it remains deterministic and useful offline. LB/RB
owns the three filters, D-pad owns the badge grid, X refreshes, and Y cycles ordering; the selected
badge's title, description, points, state, and earned date stay visible in one detail card rather
than repeating full text under every grid item.

RetroAchievements community unlock percentage is not currently represented by
`RetroAchievementsAchievement` or the cache schema. EmuShelf will not infer it from the connected
user's progress or label another field as rarity. Percentage/rarity sorting waits for a reviewed API
field, cache migration, stale/offline semantics, and fixtures proving the value's meaning.

## 2026-08-01 — Achievement sorting preserves the selector's slot, not badge identity

Reordering an achievement grid changes what occupies each spatial position. The controller
selector therefore remains at the same visible index and the detail card updates to the newly
occupying achievement; following the old achievement id would make the focus ring jump around the
screen after every Y press. Filtering retains its separate identity-or-first behavior because its
purpose is to remove whole classes of rows rather than reorder the same set.

The visible achievement collection is replaced with one Reset notification. Clearing it and adding
rows individually exposed Avalonia's virtualized `ItemsRepeater` to empty and partially rebuilt
lists, allowing recycled tiles to keep stale positions during repeated sorts. One atomic replacement
gives layout, focus reveal, and column recount a single final ordering to process.

## 2026-08-01 — Fresh achievement sources supersede collection Reset after real-compositor review

The single-Reset approach above removed partial lists but did not fully solve Avalonia's real-window
virtualization state: an 86-achievement grid could retain a stale anchor and reserve row 1, column 1
without realizing its badge. Each filter/sort now publishes a new fixed row snapshot as the
`ItemsRepeater` source. Focus realization also waits until the scroll viewport has non-zero final
geometry before calling `GetOrCreateElement`; requesting an anchor during the overlay's first measure
can itself create the leading hole this change prevents.

Achievement navigation follows the library grid's spatial contract: Left/Right never wrap rows and
Up/Down do nothing when the corresponding cell is absent from a partial final row. A filter that
retains the same badge still issues a layout revision because its position and recycled visual may
have changed even though its object identity did not. Pointer selection enters the same logical
focus path as controller selection, keeping the badge ring and detail card synchronized.

## 2026-08-01 — Rich scraping is capability-aware, provenance-first, and kept off the hot grid

ScreenScraper is not an implementation of the artwork-only DuckDuckGo picker. Built-in exact
enrichment, authenticated ScreenScraper data/media, and unverified DuckDuckGo image search share a
provider registry and settings family, while capability and trust descriptors prevent an
unverified web result from entering automatic metadata work. ScreenScraper remains disabled until
an account is connected; DuckDuckGo remains manual-only.

The library's `Game` record stays the small projection loaded by the virtualized shelf. SQLite v13
stores title, developer, publisher, genre, locale-keyed descriptions, release date, players,
rating, box front, screenshot, wheel, fanart, provider matches, and per-value provenance in
on-demand detail tables. Fill-missing is conservative; a provider refresh may update only values
owned by that same provider. User edits and user media selections are never replaced by provider
refreshes. Selected box front will continue to project into the existing cover path when the apply
coordinator lands.

ScreenScraper automatic lookups require a hash plus byte size unless a confirmed provider game id
is being reapplied; serial-only exceptions wait for written provider approval per system. User
credentials use a separate DPAPI-protected portable blob on Windows and session-only memory on
other platforms. Developer credentials come from the build/development environment and are never
committed or stored in settings/SQLite/logs. The provisional version-1 system mapping and fixture
parser may be developed without secrets, but live activation, attribution/caching behavior, and
release provisioning remain gated on ScreenScraper's written approval.

## 2026-08-01 — ScreenScraper fingerprints describe their byte scope; containers are never guessed

A hash algorithm name is not enough evidence that the hashed bytes match ScreenScraper's ROM
identity. EmuShelf therefore caches provider-scoped fingerprints with their source path, whole-file
scope, byte size, last-write value, CRC32, MD5, SHA-1, and calculation time. All three hashes are
calculated in one cancellable read after explicit consent. A cache entry is reused only while its
portable path, size, timestamp, and scope still match; game files are opened read-only and their
bytes/timestamps are verified unchanged by tests.

Only an explicit per-system extension allowlist enters the whole-file path. Text playlists and
descriptors, CHD/CSO/RVZ/WBFS and similar compressed containers, arcade ZIPs, Dreamcast GDI sets,
and PS3 directories are rejected rather than hashed as if their container bytes were canonical ROM
bytes. Those formats wait for a documented logical-content or approved serial rule. Preview may
persist this fingerprint cache, but it never applies metadata, media, or a provider match.

ScreenScraper requests share one process-wide coordinator. It begins at one active request, adopts
the returned account `maxthreads` only up to EmuShelf's safety ceiling, locally reserves requests so
parallel callers cannot overshoot a known remaining daily allowance, stops before HTTP on exhausted
daily/failed-lookup quotas, and carries a 429 cooldown to queued callers. Single-game and future
batch flows must use this same gate rather than maintaining separate counters.

## 2026-08-03 — ScreenScraper developer access approved; live validation clears the mapping gate

The project owner obtained ScreenScraper developer credentials, unblocking the previously gated
live checks. Developer id/password/softname and the connected account password are provisioned only
through environment variables (`SCREENSCRAPER_DEV_ID`, `SCREENSCRAPER_DEV_PASSWORD`,
`SCREENSCRAPER_SOFTNAME`, plus `SCREENSCRAPER_SSID`/`SCREENSCRAPER_SSPASSWORD` for validation); none
are committed, and the live client still composes only when the developer values are present.

The provisional version-1 system map is now validated: an opt-in live test
(`ScreenScraperLiveValidationTests`, gated on `EMUSHELF_TEST_SCREENSCRAPER` so a normal `dotnet test`
never touches the network) authenticated the account and cross-checked every EmuShelf-to-ScreenScraper
id against the live `systemesListe.php` catalogue (250 systems). All 13 mapped ids resolved to the
expected system, so `ScreenScraperSystemMap` is no longer provisional. `systemesListe.php` gained a
typed client method (`GetSystemsAsync`) to support this audit.

Two operational facts were observed and matter downstream: the returned account `maxthreads` can be
as low as 1 (so the coordinator's adoption of the server value — not an assumed ceiling — is what keeps
batch scraping within the account's real allowance), and the daily request quota is large
(20000/day for the validated account) with a separate failed-lookup quota (2000/day). ScreenScraper's
per-endpoint error wording is unreliable as a per-credential diagnostic: `ssuserInfos.php` reports a
"user credentials" error even when the developer id is wrong, while `systemesListe.php` correctly
reports "developer credentials"; diagnosis should prefer the dev-only `systemesListe.php` probe.

A developer-only debug surface (`ScreenScraperDebugOptions` + `SCREENSCRAPER_DEV_DEBUG_PASSWORD`) was
added to force cache updates, user level, and quota counters for testing the coordinator against the
live API (capped by ScreenScraper at 100 uses/day). The debug password is redacted like every other
secret and is never populated by user-facing flows. Still open from the approval gate: capturing
sanitized real response fixtures, and recording ScreenScraper's written attribution/caching/media-
retention terms — the webapi2 documentation states developer-approval and quota rules but not explicit
attribution or caching terms, so conservative defaults remain in force until confirmed.

## 2026-08-03 — Scrape apply is a provider-neutral orchestrator over the existing store rules

The apply step (Phase 4) is `IGameScrapeApplicationService` in Core with a single Infrastructure
implementation, deliberately provider-neutral: it takes a `GameScrapeApplyRequest` of already-neutral
`GameMetadataValue`s plus `GameMediaImport`s and does not depend on any provider's response type
(`ScreenScraperApplyMapper` converts a preview into that request). All precedence lives in
`SqliteGameDetailsStore` — `TryApplyMetadata` enforces fill-missing / refresh-same-provider /
user-edit, and `SaveMedia` refuses to let provider media overwrite user-owned or another provider's
asset — so the apply service only orchestrates: download, place, project, record. This keeps one copy
of the overwrite rules and lets batch reuse the same service.

Owned media is stored at `Data/Media/{gameId}/{providerId}-{kind}{ext}` — scoped by provider, not just
kind, so ScreenScraper and DuckDuckGo box-fronts are separate files and cannot clobber each other, and
one file backs one (provider, kind) asset. Before any download or file move, the service checks the
existing detail rows and bails out (`SkippedProtected`) if a user-owned or foreign-provider asset
already holds that path, so it can never overwrite a file this provider does not own; a stale
other-extension file for the same asset is removed after a refresh. The selected box-front is projected
into the fast `Games.CoverPath` grid column through the existing `TryApplyDownloadedCover` seam, which
already fills only an empty or previously-downloaded cover and never a user cover. Per-media results
are reported individually (`Imported` / `SkippedExisting` / `SkippedProtected` / `DownloadFailed`);
there is no global "fully scraped" flag, and a download failure still records the provider match.

## 2026-08-03 — The Desktop scraper is one shared view model; account connect is inline for now

`GameScraperViewModel` is the surface-agnostic core both Desktop and (later) Gamepad render: it loads
a non-mutating preview, exposes per-field and per-media rows (current vs. proposed, user-owned rows
locked), maps every preview outcome to a state, and drives the apply service. The Desktop
`ScraperWindow` is opened from a Grid/List context-menu entry ("Scrape with ScreenScraper…") routed
through `GameViewModel.ScrapeCommand` → `MainViewModel.ScrapeGameCommand` →
`IDialogService.ShowScraperAsync`, mirroring the existing cover-picker dialog flow; a successful apply
refreshes the library so the projected cover appears.

Account connect currently lives **inline in the scraper window's not-connected/disabled state**
(username + password → `ScreenScraperAccountService`, which enables the provider and stores the login
in the DPAPI credential store) rather than in a dedicated Settings card. This keeps connect where it
is first needed and avoids threading a parallel account context through the large emulator-settings
view model and its window for the first usable slice; a proper Settings connect card remains a
follow-up. Developer/account credentials still never leave the environment variables and DPAPI store.

## 2026-08-03 — Disc systems match by serial, which unlocks compressed containers (CHD/CSO/…)

The conservative "hash-first only, serial routes wait for provider approval" gate is lifted: a live
check confirmed `jeuInfos.php` returns the correct game from a disc serial in exactly the
`SLUS-xxxxx`/`SCUS-xxxxx` form EmuShelf already extracts (Metal Gear Solid, Final Fantasy VII, Shadow
of the Colossus all resolved). Because that serial is read from *inside* the container, a CHD/CSO/ZSO
image — which cannot be whole-file hashed as canonical ROM bytes — now matches. The client accepts a
serial-only lookup (hash+size OR serial OR game id), and `ScreenScraperPreviewService` prefers the
stored serial for PlayStation-family disc systems (`playstation`, `playstation2`, `psp`), falling back
to the whole-file hash for cartridge and raw-disc systems without a serial. Serial matching is scoped
to systems whose extracted identifier is a disc product code; cartridge header codes are deliberately
excluded so a rom hack is never matched to the original release by a shared code (the no-serial /
no-hash case is what the planned title-search fallback covers). `ScreenScraperGamePreview.FingerprintStatus`
became nullable because the serial route computes no hash. GameCube/Wii (disc id) and PS3 (title id)
serial routes are not enabled until validated the same way.

## 2026-08-03 — Fresh discs extract their serial on demand; no match falls back to title search

Two follow-ups made serial matching actually usable. First, the scraper no longer requires a prior
"Fetch metadata" pass: when a disc system has no stored serial, `ScreenScraperPreviewService` runs the
same `IGameIdentifierExtractor` the metadata pipeline uses (a targeted boot-record read that works
through CHD/CSO/…) and persists the result, so a freshly imported CHD scrapes in one step. The
extractors are injected from `KnownMetadataProfiles`. Opening the scraper is treated as consent to that
read, so the single-game flow fingerprints/extracts immediately instead of gating behind a second
click.

Second, when neither serial nor hash matches — rom hacks, unusual dumps, or anything ScreenScraper does
not index — the scraper falls back to a manual title search. `jeuRecherche.php` (client
`SearchGamesAsync`, preview `SearchAsync`) returns ranked candidates scoped to the game's system; the
`NoMatch` state auto-runs a search seeded with the game title, the user refines the query and picks the
right game, and `PreviewByProviderGameIdAsync` builds a normal preview from the chosen provider game id
with match method `UserSelectedTitleSearch`. This is the only path that applies data from a non-exact
match, and it is always user-confirmed. The scraper window also gained per-media image previews (loaded
off the UI thread through the SSRF-checked downloader) and a larger layout.

## 2026-08-03 — Batch scraping is hash/serial-only and fails safe on quota

Phase 6 batch reuses the single-game preview and apply services rather than duplicating logic
(`ScreenScraperBatchService`). Games are processed one at a time — the shared request coordinator
already paces API calls to the account's real `maxthreads`, so a sequential loop cannot overshoot, and
the code stays simple. The defining rule, from the request-economy review: **batch never title-searches.**
A miss is recorded as `NoMatch` and left for manual single-game scraping; auto-searching every unmatched
game (up to a few `jeuRecherche` calls each, plus the initial `jeuInfos` 404 that spends a failed-lookup)
would let a rom-hack-heavy library exhaust the 431 failed-lookup quota. The batch also **stops early and
fail-safe** the moment the provider reports daily-quota, failed-lookup, rate-limit, not-connected, or
disabled — leaving completed work intact and later games untouched; the summary's stop reason and
not-reached count make the run resumable, and fill-missing idempotence means a re-run naturally skips
games already done. The apply mode (fill-missing vs refresh-owned) and per-field/per-media selection are
chosen once for the whole run. Automatic after-import scraping remains off and separate.

Also fixed in the same review: single-game auto-search was firing up to seven `jeuRecherche` requests
(one per dropped word). It now walks a bounded ladder of at most four distinct queries (full → ~⅔ → ~⅓
→ one word), still reaching a single word but roughly halving the request count.

## 2026-08-03 — ScreenScraper gains a proper Settings connect card

The follow-up promised when connect was first put inline is done: the emulator-settings window now has
a ScreenScraper section (a new `SettingsSection.ScreenScraper` + `ScreenScraperSettingsContext`) that
mirrors the RetroAchievements card — username/password when disconnected, a Connect/Disconnect state
when connected, and status/quota text. It drives the same `ScreenScraperAccountService` (DPAPI credential
store, provider-enable on connect) that the inline scraper-window connect uses; both paths remain, since
connecting where the user first needs it is convenient and the Settings card is the discoverable home.
The context is threaded from `MainViewModel` (which now holds the account service) through the existing
`ShowEmulatorSettingsAsync`, keeping the settings view model unaware of the client or credential store.

## 2026-08-03 — Gamepad scraping hands off to Desktop, matching Set cover and Settings

The controller shell reaches scraping through the focused-game Actions overlay ("Scrape with
ScreenScraper"), which opens a `ScraperDesktopHandoff` overlay whose only action is "Continue to
Desktop mode". This deliberately mirrors the existing `CoverDesktopHandoff` and `SettingsDesktopHandoff`
pattern: the scraper is text- and checkbox-heavy (connect fields, title search, per-field/media
toggles) and is not controller-safe, so — like every other input-heavy feature in this codebase — it
hands off rather than pretending to be controller-native. The overlay renders through the shared
`GamepadOverlayOptions`/title machinery (no bespoke focus code), and B returns to Actions. A
controller-native scraper surface is not attempted; consistent with the whole Gamepad shell, any such
work would be gated on real Deck/controller acceptance, which is out of scope for headless tests.

## 2026-08-03 — Gamepad scraping is controller-native, reversing the Desktop handoff

The earlier `ScraperDesktopHandoff` decision is reversed: the focused-game Actions entry "Scrape with
ScreenScraper" now opens a controller-native overlay (`GamepadOverlayKind.Scraper`) that stays inside
Gamepad mode, matching the Achievements overlay rather than the Set-cover / Settings handoffs. The
handoff was the wrong call — the codebase already had both building blocks the scraper needs
(D-pad-navigable focus from Achievements, controller-safe text entry via the Steam on-screen keyboard
from Search/Rename), so "text- and checkbox-heavy, therefore hand off" did not hold. The
`ScraperDesktopHandoff` overlay kind, its description panel, and `ScrapeFromGamepadCommand` are removed;
Set cover still hands off because it needs the OS file picker, which is genuinely not controller-safe.

The overlay does not re-implement any scrape logic. It reuses the shared `GameScraperViewModel`
(states, `Fields`/`OtherMedia`/`BoxArtRow` rows, `BoxArtPreview`, and every command) and adds only a
thin presentation wrapper, `GamepadScraperViewModel`, that layers a linear D-pad focus model on top:
Up/Down move a ring across a per-state list of targets, A activates the focused one (toggle a
field/media checkbox, run Connect/Search/Compute/Apply, or pick a title-search candidate), and B backs
out. `MainViewModel` builds the scraper view model from the injected preview/apply/account/settings
(the same services the desktop `ShowScraperAsync` uses) and routes controller input through a dedicated
`DispatchScraperOverlayAction`, so native-pad and Steam-Input keyboard paths behave identically. The
two scraper row view models gained an `IsFocused` flag (gamepad presentation only) so the focus ring
binds directly to the reused rows.

Terminal states (Applied / Failure / Unsupported) back out to the library on B; while still working the
overlay backs out to the Actions menu. A successful apply refreshes the library on close, mirroring the
desktop window's post-apply reload. As with the rest of the Gamepad shell, final controller *feel*
(focus hand-off to the Steam keyboard, gamescope) needs real Deck/controller acceptance and is out of
scope for the headless view-model tests that cover the flow.

## 2026-08-01 — Gamepad Settings projects the existing settings model

The controller surface is a navigation adapter over `EmulatorSettingsViewModel`, not a second
settings store. General, RetroAchievements, Saves, and Texture Packs are projected into stable row
keys, with remembered focus per section and the same commands, child view models, validation, and
portable persistence used by Desktop. Save is placed at the start of each virtualized section but
initial focus remains on the first setting, making Save one Up press away without displacing the
normal reading order. B cancels an active edit or confirmation first, then closes Settings without
saving and restores focus to the Settings menu item.

The first slice intentionally excludes emulator executable paths, arguments, cores, library-root
management, and RPCS3 library maintenance. Those fields were audited with the rest of Desktop
Settings but remain the next Phase 2 slice; cover search, themes, and ScreenScraper remain Phases 3,
4, and 5 respectively. Texture-pack operations stay observational and limited to existing rescan,
filter, picker, and clear services; opening the host file manager remains Desktop-only. EmuShelf
still never edits emulator-owned texture configuration or pack contents.

## 2026-08-01 — Controller text entry uses an optional host-keyboard capability

Text and secret rows enter a focused in-window editor whose draft is committed only with A and
discarded with B. Secret values remain masked in both the row and editor, and the draft is cleared
after either outcome; the underlying settings view model continues to own secure persistence.
Opening an editor requests an on-screen keyboard through a Core capability. The Windows adapter
best-effort launches the system touch keyboard or OSK, while unsupported hosts retain an explicit
hardware-keyboard or Steam+X path instead of adding a Steamworks dependency without its required
lifecycle. This interface leaves room for a native Steam/Deck implementation later and keeps all
platform-specific process behavior outside the cross-platform view model.

Native dialogs are used only for values whose meaning is an actual file or folder. All choices,
toggles, actions, text, secrets, and destructive confirmations remain controller-owned inside the
Gamepad window; destructive rows always focus the non-destructive choice first.

## 2026-08-01 — Gamepad Settings control shape communicates behavior

A real populated-library screenshot rejected the initial Settings presentation even though its
focus and containment assertions passed. Giving every field the same variably sized card plus a
small `TOGGLE`, `ACTION`, or `FILE` badge hid how the field worked and allowed virtualized children
to keep their desired widths. Geometry containment alone is therefore not visual acceptance.

The replacement uses a full-height proportional section rail and one equal-width virtualized
content column. Boolean fields render as conventional ON/OFF tracks with a moving checked/cleared
thumb; choices render between directional chevrons; text, secret, file, and folder fields show their
current value beside an explicit A Edit/Choose affordance; ordinary and destructive commands use
distinct action treatments. This follows the reference's interaction vocabulary without copying
its branding, type, icons, clock, or exact composition.

The Save row remains first in the logical D-pad order, so Up from the initial setting still reaches
it, but it is now rendered as a pinned action at the bottom of the section rail rather than as a
generic content card. This supersedes only the visual-placement portion of the earlier projection
decision. The repeater receives an explicit viewport-derived cross-axis width, and viewport resize
re-reveals logical focus. Real-window tests cover 1280x800, 1280x720, and 2048x1152 so a fixed-size
desktop dialog cannot satisfy the Gamepad geometry contract again.

## 2026-08-01 — Desktop and Gamepad Settings parity is executable

Gamepad Settings does not add preferences and does not own a second persistent settings object.
`AutomaticallyFetchMetadataAfterImport`, including its metadata consent and persistence behavior,
was already a Desktop field; both surfaces read and mutate the same `EmulatorSettingsViewModel`
property and use its existing `IMetadataPreferencesService` save path.

Every mutating control in General, RetroAchievements, Saves, and Texture Packs now carries the same
stable field id in the Desktop window and the Gamepad row projection. A real-window test collects
the effectively visible Desktop ids in each connection state and compares them with the complete
controller projection. The controller list remains virtualized, so a second assertion checks that
each realized row exposes its field id without requiring off-screen rows to be materialized.
Read-only status/inventory content and external browser or file-manager links are deliberately not
counted as settings mutations.

The final control vocabulary uses reference-sized two-state tracks with a moving check/clear thumb,
large circular edit/action targets, full-width section selections, and a full-width pinned Save
action. It borrows the reference's conventional behavior without copying its typeface, branding,
clock, theme system, or exact composition. START is a direct Save-and-close route; Up from a
section's initial field followed by A remains an equivalent tested route, and B still exits without
saving. This supersedes the earlier text-labelled action-capsule presentation.

## 2026-08-01 — Full palettes swap an override dictionary; tokens stay `DynamicResource`

The appearance system moved from Light/Dark theme-dictionaries plus a single accent to complete
named palettes. `ThemePreference` gained `Oled`, `Cyberpunk`, and `Nord`; it still serializes as a
string, so existing `System`/`Light`/`Dark` settings files keep parsing and no migration is needed.
`ThemeCatalog` (Core) is the single source of built-in themes — id, display name, dark/light, and
four preview-swatch hex colors — consumed by both the Desktop appearance menu and the controller
theme gallery so a theme added there appears in both modes.

Each extra palette is a flat `ResourceDictionary` under `Styles/Palettes/` that redefines every
`EmuXxxBrush` token plus an `EmuFocusGlow` box-shadow. `AppThemeService` sets the base `ThemeVariant`
(so stock Fluent chrome stays legible) and appends the selected palette last in the application's
merged dictionaries, where its top-level tokens win over the base `EmuShelfTheme` set; System/Light/
Dark append no override. Because every consumer already binds tokens with `DynamicResource`, swapping
the dictionary re-colors the whole UI live. Built-in themes are an enum rather than a `Themes/` import
because the roadmap defers a portable import format until the token contract is proven stable — which
this work does by rendering OLED and Cyberpunk with no hardcoded color leaking through. A/B/X/Y and
the green Play action remain fixed brushes outside every palette.

## 2026-08-01 — Accent is separated from danger; the focused game is raised, not alarmed

A populated-library review found the Gamepad UI read as a wall of alarm-red: the accent (`#EF4855`)
was the same hue as the danger color, so selection, focus, section highlights, toggles, and every
destructive action all looked like errors. The default accent moved to rose (`#F15C93` dark,
`#D23A76` light), distinct from the unchanged red danger brush, so selection and focus read as brand
rather than warning. Destructive settings rows now look ordinary until focused — the danger cue is
the tinted action circle plus the existing confirmation gate, not a red title on every row — and the
toggle thumb lost its busy ×/✓ pair for a plain sliding thumb.

The focused game gained presence to match NeoStation's reference: a thicker accent ring, a
theme-colored `EmuFocusGlow` halo, and a subtle non-layout scale so the selected cover lifts off the
shelf without moving its neighbours. The glow is a themed `BoxShadows` resource, so it takes the
active palette's accent.

## 2026-08-01 — The controller theme gallery is a gamepad-only page with shared choices

Theme selection previously lived only in the Desktop toolbar, so Gamepad mode could not change it —
a parity gap. Gamepad Settings gained a Themes page presenting a NeoStation-style gallery: a
three-column grid of cards, each rendering a miniature app window from that theme's own swatches,
with the applied theme marked and the focused card carrying the strong-focus border. It is a
gamepad-only page rather than a `SettingsSection` because appearance is not part of the settings
model; the executable Desktop/Gamepad parity test therefore still compares only the four model
sections. Both surfaces project the same `ThemeChoice` instances and apply through one path, so a
change in either mode updates the other and persists once.

Read-only texture-pack inventory is now bounded in Gamepad Settings: a large real library rendered
as an endless wall of near-identical cards, so the controller list shows a capped, filterable window
with a count and points at the filters and Desktop for the rest, and read-only rows (inventory,
logs, connected account) render as a flat list instead of solid action-button cards. The full
inventory remains browsable in Desktop; this is inventory display, not a settings field, so it does
not affect executable parity.

## 2026-08-01 — Focus frames the cover from outside; Gamepad Settings is grouped by platform

A real zoomed render showed the earlier focus ring painting on top of the cover's dark edge read as
a faint glow "behind" the art rather than a selection. The focus ring now sits 5px outside the cover
on every side (negative margin, larger corner radius) so it reads unambiguously as a frame around the
artwork, keeps the theme-colored `EmuFocusGlow`, and stays the topmost tile layer. The earlier
non-layout scale-up was removed: the frame plus glow already give couch-distance presence, and the
transform muddied where the border sat. Geometry tests now assert the frame is exactly 10px larger
than the cover frame.

Gamepad Settings gained the Desktop settings hierarchy. Saves and Texture Packs are grouped under
non-focusable platform headers (platform artwork plus name); the platform's rows are indented beneath
their header and carry the same platform artwork as their leading icon, so membership is unmistakable.
Member labels drop the redundant platform-name prefix because the header carries it — the stable field
ids (Keys) are unchanged, so executable Desktop/Gamepad parity still holds. Generic rows (General,
maintenance, filters) show a category glyph in the same leading-icon slot; read-only inventory rows
stay icon-free so a long list reads lightly. D-pad navigation and post-rebuild focus targeting skip
header rows, and the geometry suite covers the indent width and that focus never lands on a header.

## 2026-08-01 — Selector is an on-cover frame; inventories collapse; settings gain a rail column

Rendering the focus frame against a real opaque cover (not the placeholder covers the tests used)
showed the "outside the cover" negative-margin ring was clipped left/right by the tile width while
top/bottom overflowed into the taller shelf cell — so selection looked broken on real artwork. The
selector is now a 5px accent border drawn at the cover bounds (topmost tile layer, no overflow, so it
can never be clipped) plus the themed glow; a real-cover regression test captures this. Lesson:
gamepad-tile visuals must be verified with an opaque cover, not only the missing-artwork placeholder.

The texture-pack inventory is collapsed by default in both modes because a real library holds
hundreds of packs and the matched/attention totals are what a user needs. Gamepad shows an
"N installed packs" control that reveals a bounded list on A; Desktop moves the full list into a
collapsed Expander backed by a virtualizing ListBox so an expanded large library stays responsive.
The gamepad reveal control is a view-state row excluded from executable Desktop/Gamepad parity.

Gamepad Settings navigation added a left-rail focus column so the vertical section list responds to
the D-pad, not only LB/RB. Left steps from the content into the rail; on the rail Up/Down move
sections live and Right/A return to the content; LB/RB remain a shortcut from either column. Values
change with A (or Right), NeoStation-style, which frees Left/Right for column movement; the active
column is shown by filling the selected rail item and dimming the inactive content pane. The theme
gallery integrates the same way — Left from its first column steps out to the rail.

## 2026-08-01 — Themes is a real settings section in both modes; selector hardened

The Desktop settings window now has its own Themes section, not only the toolbar menu, so it matches
Gamepad settings — appearance is selectable from the settings surface in both modes. `SettingsSection`
gained `Themes`; `EmulatorSettingsViewModel` adds it (and exposes the shared `ThemeChoice` list) only
when the host supplies theme choices, and renders the same swatch gallery the Gamepad gallery uses.
Gamepad continues to present themes as its dedicated gallery page and therefore excludes both Themes
and the Desktop-only Emulators slice from its projected row sections. Emulators paths/arguments remain
the one section that is still Desktop-only, pending the deferred Gamepad emulator-settings work.

The read-only pack inventory Expander on Desktop was too small and narrow; it now stretches to the
content width with a much taller virtualized list. The focused-cover selector was thickened to a 6px
accent border, and — because the corner bleed reported on real hardware does not reproduce under the
headless Skia renderer — the cover art is now clipped with a larger corner radius than the focus ring
so the art corners are pulled inside the ring regardless of GPU anti-aliasing.

## 2026-08-01 — Desktop theme selection lives in Settings

The Desktop toolbar theme flyout was removed after Themes became a complete Settings section. Keeping
both entry points duplicated the same catalog and crowded the small group of global toolbar actions;
the gear is now the single Desktop configuration entry point. Gamepad mode retains its controller-native
Themes page, and both settings surfaces continue to share the same theme-choice instances and persistence
path so selection state stays synchronized.

## 2026-08-02 — Covers fill one canonical per-platform frame again (Steam Deck grid fixes)

Steam Deck testing reported three grid faults together: covers rendering at roughly half their
expected height with empty space above, intermittent blank tiles, and the controller selector
sometimes unable to move right / "stuck". They share one cause.

Commit `232229b` (2026-07-25) had quietly reversed the 2026-07-17 "one canonical frame per platform"
decision: `GameViewModel` began adopting each loaded bitmap's own aspect ratio (`SetCoverAspectRatio`,
raising `CoverAspectRatioChanged`). Two consequences followed on a real mixed-provider library:

- The shared cover shelf is `max(cover height)` across the view, and covers bottom-align in it. Once
  tiles adopt their own ratios, a single tall or off-ratio scan balloons the shelf and every other
  cover renders at a fraction of it — the "covers only take half their space" report.
- Every cover finishing its async load fired `CoverAspectRatioChanged → UpdateCoverLayout()`, which
  re-applied layout to all tiles and **recomputed `GamepadColumnCount` from width arithmetic**,
  clobbering the authoritative rendered column count the view reports. A momentarily-low arithmetic
  count made Right/Left clamp partway across a row (the 2026-07-31 "stuck at second column" fix,
  re-broken), and the constant relayout churn as covers streamed in surfaced as blank/again-blank
  tiles.

The fix restores the documented behavior: `CoverAspectRatio` is the platform's canonical frame for
the whole session, set once at construction, and the real cover fills it (`UniformToFill`, already in
the tile templates), cropping at most the ~2px of outer bleed the 2026-07-17 decision accepted. A
cover load now only toggles `HasCoverImage`; it never resizes the shelf or re-runs the column-count
arithmetic. `SetCoverAspectRatio`, the `CoverAspectRatioChanged` event, and its `UpdateCoverLayout`
subscription were removed. Arcade already used a fixed landscape frame, so its behavior is unchanged;
now every platform behaves the same way it does. Regression tests assert an off-ratio cover load
leaves a tile's canonical frame and the shared shelf untouched, and that a cover load no longer
resets the rendered gamepad column count.

## 2026-08-02 — Save states get their own detected folder and override, 1:1 with saves

Steam Deck testing found arcade (RetroArch/FinalBurn Neo) save states were not syncing: a state was
made and the post-exit pass reported "no saves were found to sync". Two gaps, now closed.

- **Silent compatibility drop.** `AuxiliarySyncProvider` only enumerates states when a
  `StateCompatibility` can be built, and for a RetroArch core that requires the core's
  `display_version` from its `info/*.info` file. On a Flatpak RetroArch, or when the core file is
  dropped in beside EmuShelf, that info file is often absent, so compatibility resolved to null and
  every state was silently dropped with no upload. A libretro state is written by the core, so the
  core identity is the compatibility key: when `display_version` is unavailable, EmuShelf now falls
  back to a stable token derived from the core file's byte length (identical for the same build on
  every machine, different across builds). Architecture still comes from the core binary. So state
  sync resolves whenever the core file exists, while the "only restore a state from the same build"
  guard is preserved.

- **No way to correct the folder.** Save states now have their own detected-folder display and manual
  override, mirroring the save folder exactly: `SaveLocationSettings.StateDirectoryOverride`,
  `CloudSaveSyncSettings.GetStateOverride`/`WithStateOverride`, a `StateDirectoryOverride` on
  `SaveProviderContext` that replaces the resolved state root in `AddStateSources`, and a second
  path box + Browse button under the "Automatically sync save states" toggle in Settings. It persists
  the same way the save override does (immediately on Browse, and in the Settings Save batch).

## 2026-08-02 — The focused-game achievement widget follows a details refresh

RetroAchievements progress that arrives from a detail refresh — opening the achievements overlay, or
the one post-exit refresh — writes the account's new unlock count to the progress store, but only a
full library reload re-applied it to a tile. So after unlocking an achievement the Gamepad focused-game
dock widget kept showing the pre-unlock count ("0/9") even though the overlay already reflected it.
`MainViewModel` now subscribes to `IRetroAchievementsDetailsService.DetailsRefreshed` and re-applies
the achievement display for every loaded tile linked to that RA game (re-reading the same local
stores the reload path uses, no network), marshaled to the UI thread. The dock widget and grid mark
now update as soon as fresh progress is cached, whether from the overlay or the post-exit pass.

## 2026-08-02 — State-compatibility versions never launch the emulator; Flatpak falls back to the build commit

A second Steam Deck pass surfaced a modal "Unknown parameter: --version" dialog every time Saves
settings opened. Version detection for save-state compatibility launched the configured emulator
with `--version`; GUI emulators (DuckStation, PCSX2, RPCS3, Dolphin) treat an unknown argument as a
fatal error and pop a dialog rather than printing a version — and the process never exits on its own,
so it also blocked detection until a 5s kill. The subprocess is removed entirely. A binary with no
embedded version resource (a typical Linux build) now keys compatibility off a stable file-length
token (`exelen{bytes}` / `corelen{bytes}` via one shared helper) — identical per build across
machines, different across builds — so nothing is ever run to read a version.

The same pass found Flatpak PCSX2 reporting "the emulator/core version could not be detected, so
states will not be synced": many Flathub emulators publish no `--show-version` string, which
resolved compatibility to null and dropped every state. `flatpak info` now falls back to
`--show-commit` (always present for an installed app, stable per build) when the version is empty, so
compatibility resolves and states sync. Architecture still comes from `--show-arch` / the binary.

## 2026-08-02 — A launch/exit state sync is scoped to the launched game

This supersedes the launch/exit portion of "Manual state sync includes every eligible state"
(2026-07-29). Because states live in one folder per emulator (not per game), launching any PS2 game
made the launch/exit pass hash and sync *every* PS2 game's states — dozens of ~15 MB PCSX2 states,
so the pass read GB on each launch and the first sync uploaded everything. The launch/exit state
phase is now scoped to the launched game: `MainViewModel` builds the game's keys (its ROM file stem,
how RetroArch names states, plus the serials/disc/title/arcade ids the metadata store extracted, how
DuckStation, PCSX2, PPSSPP, Dolphin, and RPCS3 name them) and passes them to `SyncSystemAsync`;
`AuxiliarySyncProvider` includes only states whose normalized file name contains a key, for both
local enumeration and remote selection. Matching is deliberately fuzzy alphanumeric-contains: a
false positive only syncs an extra state, and a game whose id is unknown simply isn't auto-synced on
launch. A **manual Sync all passes no keys and still reconciles every state**, so nothing is ever
permanently excluded — it is the exact escape hatch. Regular battery/memory-card saves are never
scoped.

## 2026-08-02 — Gamepad grid hardening: cover backstop and full-tile reveal

Two Steam Deck grid faults beyond the cover-frame fix. Covers load off per-element
AttachedToVisualTree / DataContextChanged events, which race during rapid LB/RB recycling and could
leave a tile an empty cell; after the grid lays out, the view now requests the cover for every
realized tile as a settle-time backstop (LoadCover is idempotent, so already-loaded/loading tiles
are skipped). And on the Deck's short grid viewport a tall portrait tile (cover + title ≈ 324px)
could be revealed bottom-aligned with its cover clipped ("half the cover"); after BringIntoView the
view nudges the scroll, in content coordinates, so the whole focused tile stays inside the viewport,
scrolling only when it is actually clipped.

## 2026-08-02 — RetroArch save-state compatibility keys on the core, not the frontend

Real cross-machine testing showed states uploaded from a Steam Deck never restored on Windows:
Google Drive held them, but the Windows launch marked each "written by a different emulator version"
and skipped it. Cause: the state-compatibility key mixed in the RetroArch *frontend* version
(`ResolveEmulatorVersion`), and two machines almost never run the identical RetroArch build — so the
Deck key and the Windows key differed even when the core was byte-identical.

A libretro save state is produced by the core, so its portability depends on the core (name +
version) and CPU architecture, not the frontend. The RetroArch key is now
`retroarch:<coreId> | <arch> | <coreVersion> | <coreVersion>` with the frontend version removed. The
core's published `display_version` (from its info file) is platform-independent, so a state made on
Linux restores on Windows for the same core version; the file-length token remains a within-platform
fallback only for when the info file is absent (a `.so` and a `.dll` differ in length, so cross-OS
restore then needs the info file present on both). Standalone emulators (DuckStation, PCSX2, …) keep
keying on their own version — that is the correct guard, since their states are version-specific.

Consequence: states already uploaded under the old key keep it (an unchanged state retains its
recorded compatibility, by design), so a fresh state must be made after both machines update before
cross-machine restore is observable; new states carry the new, matching key.

## 2026-08-02 — State compatibility is a structured, provenance-aware identity, not an opaque hash

Real Deck↔Windows testing showed the core-keying fix above was necessary but not sufficient: states
still did not restore. Two residual causes, both because the compatibility "version" could be an
OS-specific token that two machines never agree on.

- **RetroArch, no info file.** When the core's `display_version` is unavailable (a bare core dropped
  beside EmuShelf, a Flatpak with no `info/` dir — the common Deck setup), the key fell back to a
  core **file-length** token. A `.so` (Deck) and a `.dll` (Windows) of the same core have different
  byte lengths, so the keys differed and Windows rejected every Deck state. It also broke
  *asymmetrically*: one machine reading `display_version` while the other fell back never matched.
- **Standalone (PCSX2), Flatpak.** A Flatpak that publishes no version fell back to a `commit<hash>`
  token, which can never equal a native build's `2.x` version resource — so Deck-Flatpak → Windows
  PCSX2 states were a guaranteed skip.

The compatibility value is now a **structured, parseable** string —
`st1|<emulatorId|retroarch:coreId>|<arch>|<auth|unk>:<version>` — compared component-wise rather than
by exact-string/hash equality. Rule: the emulator/core **id and CPU architecture must always match**;
the build **version is enforced only when BOTH machines recorded an *authoritative* one** (a core
`display_version`, an executable version resource, a Flatpak's published version). When either side's
version is unknown, the state is compatible on id+arch alone. The OS-specific length/commit tokens are
removed entirely — an unavailable version is recorded as *unknown*, not substituted. This lets a
bare-core Deck state restore on a Windows `.dll` of the same core (both x64, unknown version), lets a
Flatpak-PCSX2 state reach a native Windows PCSX2, and still keeps two genuinely different *known*
versions apart. Keys uploaded before this format are opaque slug-hashes: they fall back to exact-match,
so a fresh state is still required to cross machines (as already documented above).

This is safe to be permissive: sync never deletes, `SaveSyncService` backs up the losing copy before
any overwrite, and RetroArch/PCSX2 both validate a state's own embedded version tag on load and refuse
a genuinely incompatible one — so the worst case of an over-permissive match is "the emulator declines
to load it," never data loss. The prior behaviour's worst case was the feature silently doing nothing.

Diagnostics were extended to make a residual mismatch self-evident: the launch/exit state pass now logs
the resolved `compatibilityKey` (two machines must print the same key to restore) and every distinct
skip reason, to `Logs/EmuShelf-YYYY-MM-DD.log`.

## 2026-08-02 — The gamepad grid is deterministic: reserved gutter, overlay selector, geometry reveal

Real Steam Deck use surfaced three grid faults. Investigation (with adversarial verification) placed
each precisely, and the fix makes the grid's focus and sizing deterministic instead of dependent on
virtualization/compositor timing that the headless renderer can't reproduce.

- **The selector sometimes vanished.** The accent focus ring was a `Border` *inside* the virtualized
  item template (`IsVisible="{Binding IsFocused}"`), so it only existed while its tile was realized.
  When focus moved to a row outside the realized window and the reveal stranded (it gave up silently
  after five attempts, or a rapid d-pad repeat pre-empted it), no realized tile carried the ring and
  the selector disappeared entirely. The ring is now a single **overlay** (`GamepadSelectorRing`) that
  lives outside the `ItemsRepeater`, in the shared scroller content, positioned by the view from the
  focused cover's geometry. It can never be virtualized away or occluded, and it scrolls glued to the
  cover because it shares the content. It prefers the realized cover's real bounds and falls back to
  computed geometry, so it is drawn the instant focus moves.
- **The right column's cover/glow clipped.** The cover width was packed to fill the row to the
  sub-pixel remainder with zero side inset, so the edge tile's focus glow (which blurs ~30px past the
  cover) fell into a 0–4px gutter and was shaved by the scroller's clip; a stale cell width right after
  a mode/platform switch could also push a whole column past the edge. The gamepad grid now reserves a
  `GamepadGridSideGutter` (40px, > the glow radius) on each side — mirrored by the repeater's `Margin`
  and subtracted in the column arithmetic — so the focused tile's glow always has room and the column
  count is derived from the true content region.
- **Reveal is now deterministic.** Instead of `BringIntoView` plus a manual nudge that read a possibly
  stale element rectangle, the target scroll offset is computed from the focused index, the column
  count, and the uniform row height, clamped to keep a full glow radius of clearance. This can't be
  lost to a competing layout pass or a fast d-pad repeat — the mechanisms behind the strand.

The `staggered/unpolished` perception was investigated and is **not** a bug: rows are uniform height,
and the ragged tops are covers of different per-platform aspect ratios bottom-aligned on the shared
shelf (the mandated OpenEmu-style framing, DECISIONS 2026-07-17/08-02). It is left as designed.

Virtualization, asynchronous cover loading, and per-platform cover ratios are preserved. Because the
residual timing faults cannot be reproduced off-device, a **fault-only** diagnostic (`LogGamepadGridFault`)
logs to `Logs/EmuShelf-*.log` when the focused tile fails to realize, the selector cannot be placed, or
the arithmetic and rendered column counts disagree — so a single Deck run pinpoints any remainder
without flooding the log on every d-pad move.

## 2026-08-03 — State architecture falls back to the host; grid nav trusts the rendered column count

Real Steam Deck logs (with the diagnostics above) settled two things the RetroArch fix had left open.

- **PCSX2 states never left the Deck** because `ResolveEmulatorArchitecture` returned null: a Deck
  Flatpak/AppImage/wrapper PCSX2 exposes no binary whose header `ReadBinaryArchitecture` can parse, and
  `flatpak --show-arch` wasn't available/usable, so `StateCompatibility.Create` returned null and every
  state was silently dropped (`compatibilityKey=(none)` in the log). The emulator runs on *this*
  machine, so the host's own architecture (`RuntimeInformation.OSArchitecture`) is a sound fallback — a
  machine can't run a foreign-arch emulator natively. Both Deck and Windows now resolve `x64`, and since
  a Flatpak PCSX2 publishes no version (→ `unk`), the Deck key `st1|pcsx2|x64|unk:` matches Windows'
  `st1|pcsx2|x64|auth:2.7.469` via the unknown-version rule, so states cross. (A misconfiguration where
  PCSX2's location points at the *memcards* subfolder still can't derive the state folder — that stays a
  user-facing error with the `StateDirectoryOverride` escape hatch, not a silent drop.)

- **The grid "can't move left from a mid-row tile" while Right/Up/Down work** is the exact signature of
  `GamepadColumnCount` being larger than the rendered columns (so `index % columns == 0` wrongly). The
  count is now refreshed from the realized layout (`SyncGamepadColumnCountFromLayout`) *immediately
  before* each directional move, so nav math matches what's on screen rather than a width estimate that
  can go stale. The same pre-move hook logs the full geometry (index, columns, computed column, the
  focused row's realized width, viewport/cover widths, selector visibility) at Information level — the
  earlier fault-only logging stayed silent while the bug happened, so this is promoted to always-on per
  keypress (user-paced, not per-frame).

- **Reverted: `GamepadColumnCount` is derived by width arithmetic alone, never read back from the
  visual tree.** The two prior decisions above (`SetRenderedGamepadColumnCount`, and the pre-move
  `SyncGamepadColumnCountFromLayout` refresh) tried to make the *rendered* layout the source of truth
  for the column count, on the theory that the arithmetic could go stale. In practice that read-back was
  the bug: it groups realized tiles by `Math.Round(Bounds.Y)` and takes the busiest row, but during fast
  LB/RB the `ItemsRepeater` is mid-recycle (`Games.ReplaceAll`), so the tiles have stale/equal `Bounds.Y`
  — they collapse into one Y bucket (count reads far too large → Up/Down clamp and the selector's
  `index%columns` geometry lands off the right edge, "selector vanishes") or only a partial row is
  realized (count reads too small/1 → `index%columns==0` always, "can't move left"). Reading it
  *immediately before each move* sampled the tree at its least stable instant, which is why that change
  made things worse. The arithmetic (`ColumnsThatFit`, using the same gutters + spacing as
  `UniformGridLayout`) is provably exact — `TheFocusStrideMatchesTheRenderedColumnCount` locks it to the
  layout formula — and, being a pure function of viewport + cover width, cannot race a recycle. So
  `SetRenderedGamepadColumnCount`, `SyncGamepadColumnCountFromLayout`, and `PrepareGamepadNavigation` are
  deleted; reveal and selector placement stay deterministic geometry from `index` + the arithmetic
  column count. Covers still get their settle-time backstop (`RequestVisibleGamepadCovers`); that is a
  separate concern from navigation correctness.

- **The gamepad focus ring is revealed synchronously on `FocusedGame` change, not via a posted job.**
  With the column-count race gone, one symptom survived: on a real controller the ring would freeze on a
  tile while `FocusedGame` kept moving, so correct moves read as "Left does nothing" or "Up jumped a
  column" (the user was steering by a stale ring, and the next quiet frame snapped it to the true focus).
  Root cause: `OnGamepadViewModelPropertyChanged` posted `RevealFocusedGame` at
  `DispatcherPriority.Input` — the *same* priority the SDL poll timer (16 ms) and Steam-Input keys arrive
  on. Under d-pad auto-repeat / fast LB-RB that priority floods, so the reveal was starved and the ring
  lagged (a headless test that pumps only `Render`, never `Input`, reproduces it: focus walks 0→1→2→3
  while the ring stays pinned at x=44). The reveal's scroll offset and the ring's fallback placement are
  both pure `index`/`columns` arithmetic, and `FocusedGame` only changes from an input handler or the
  timer tick (never mid-layout), so the reveal is safe to run inline — placing the ring the instant focus
  moves, no matter how fast the repeat. `GamepadGridSelectorTests` renders the real grid and locks in
  both this (ring tracks focus while `Input` is starved) and that arithmetic columns == rendered columns
  across every width 820–1960.

- **The real "fast LB/RB breaks the grid" cause was a full library rebuild on every platform switch;
  fixed with a per-scope view-model cache plus a switch debounce.** On-device diagnostics (`GPDIAG`
  trace, gated by `EMUSHELF_GAMEPAD_DIAG`, on by default while chasing this) showed the grid logic was
  correct — every `moveUp idx=0 … moved=False` was right — but a burst of `NextPlatform` was reloading
  the library on each press: `games=` lurched 46→18→62→866→…→0 between consecutive events. Each RB/LB
  ran `ReloadGamesAsync`, which synchronously cleared `Games` + nulled `FocusedGame` (`BeginScopeChange`)
  and then re-queried the DB and rebuilt every `GameViewModel`. Fast cycling therefore blanked the grid,
  dropped the selector, and reset focus to the top-left tile between presses — which is what read as
  "Up/Left do nothing" and "covers go blank". Fix: (1) `_scopeCache` keyed by scope
  (`system:{id}`/`AllGames`/`RecentlyAdded`) holds each scope's built view models; navigating to a
  visited scope swaps them in synchronously (no DB, no rebuild, covers already warm). (2) The switch is
  debounced (`PlatformReloadDebounceMs` 180): an *uncached* target clears the grid immediately (so one
  platform's tiles never sit under another's title) and coalesces the heavy build to the platform the
  user settles on; a *cached* target swaps in instantly with no debounce. (3) `ReloadGamesAsync(useCache)`
  defaults to `false` (drop the whole cache and rebuild from the DB) so every mutation/refresh path
  (add/remove/rename/rescan, availability + achievements passes — all of which persist to the DB first)
  stays correct without per-call-site changes; only the navigation hot path opts into `useCache: true`.
  The cache owns the view models: scopes not on screen are disposed on invalidation, and the on-screen
  scope's view models live until their rebuilt replacement is ready. A cache-hit swap bumps
  `_loadGeneration` so an in-flight slow reload cannot land afterward and overwrite the switched-to scope.

- **The gamepad grid is a row-virtualized `ListBox`, not a virtualized cell grid.** After the cache/
  debounce fix, two bugs remained, both traced with the `navProbe` diagnostic (which logged, per move,
  the arithmetic column vs. the *rendered* column of the focused tile). First: `arithCol=0` but
  `renderedCol=1` — every tile shifted one column right. Cause: `RevealFocusedGame` called
  `GetOrCreateElement(index)` to force-realize a far-off tile; on the real compositor that makes
  Avalonia's `UniformGridLayout` reserve cell 0 and place item 0 in cell 1 — a permanent one-column
  "top-left hole" (the same defect the achievements grid already documented). It never reproduced in
  headless. Second, after switching the reveal to scroll-by-offset: `focEl=(NaN,NaN)` — the focused tile
  went off-screen because manually setting `ScrollViewer.Offset` and laying out only the repeater left
  it realizing against the stale viewport. Both are symptoms of the same thing: **Avalonia's
  `ItemsRepeater` + `UniformGridLayout` is unreliable for programmatic scroll-to-item.** A brief
  non-virtualized `ItemsControl`+`UniformGrid` fixed correctness (rendered columns == `GamepadColumnCount`
  by construction) but was too slow at 800+ games. The mature-frontend answer (Steam Big Picture,
  EmulationStation) is to **virtualize rows, not cells**: the grid is a `ListBox` (`GamepadRowList`)
  bound to `GamepadRows` — a projection of the flat `Games` list into rows of `GamepadColumnCount` —
  with a vertical `VirtualizingStackPanel`. Each row template is a horizontal strip of the existing
  tiles. Only the ~visible rows realize (a 300-game library materializes ~10 tiles top *and* bottom —
  flat cost, proven by `GamepadGrid_VirtualizesRows_CostIsFlatFromTopToBottom`), vertical virtualization
  has none of `UniformGridLayout`'s phantom-cell defects, each row holds exactly `GamepadColumnCount`
  tiles so rendered columns can never disagree with the navigation stride, and scroll is native
  `ListBox.ScrollIntoView(rowIndex)` — no manual offset math. Navigation still runs on the flat `Games`
  list + `index % columns`; only rendering is virtualized. `GamepadRows` auto-rebuilds from
  `Games.CollectionChanged` and on `GamepadColumnCount` change, so it can never go stale. The selection
  ring lives inside the tile template (shown via opacity on the `.focused` state), so it is physically
  part of the focused cover and cannot drift — the earlier floating-overlay ring (positioned by reading
  realized bounds) is gone, and with it the "ring on the wrong tile" class. Covers load lazily on
  attach (a tile attaches only when its row scrolls in). A post-`ScrollIntoView` nudge keeps a glow
  radius of clearance from the viewport edge so the focus glow is never shaved.

## 2026-08-03 — Built-in palettes reach NeoStation parity; the base variant follows the catalog

`ThemePreference` and `ThemeCatalog` gained nine more built-in palettes — Valentine, Dracula, Coffee,
Tokyo Night, Retro, Abyss, Aqua, Palenight, Horizon — so the set matches the reference gallery. Each
is another flat `Styles/Palettes/*.axaml` dictionary redefining the full `EmuXxxBrush` token set plus
`EmuFocusGlow`, mapped in `AppThemeService.PaletteUri`; nothing else in the swap machinery changed, so
the addition is pure data. The catalog is ordered to match the reference gallery (System, Dark, Light,
OLED, Valentine, Dracula, …) — the two settings surfaces render in catalog order, so this is the
order users see in both.

Unlike the reference (whose themes differ mostly by accent, so its dark palettes read alike), each
palette here tints its whole surface stack — window, sidebar, toolbar, cards — with real hue and
saturation, and the accents are spread around the wheel so no two dark themes collapse together: the
grid is the dominant visual mass, and cover art is opaque, so pushing chroma into the backgrounds is
what actually gives a theme identity while leaving art untouched. Concretely, the purples are split by
accent (Dracula = violet + its signature hot pink, Palenight = grey-purple + lilac) and the blues by
hue and lightness (OLED = pure black + electric blue, Nord = desaturated blue-grey + frost, Tokyo
Night = indigo + periwinkle, Aqua = teal + cyan, Abyss = inky blue-black + lime). Verified by rendering
every theme to PNG headlessly (`EMUSHELF_SNAPSHOT_DIR`).

A later pass added three EmuShelf-original creative themes beyond the reference set — Matrix (phosphor
green-on-black terminal, green *text* not just a green accent), Synthwave (saturated retro-outrun
purple with hot magenta + cyan), Sunset (warm rust-ember with a tangerine accent) — appended after
Horizon in the catalog so the reference-matched block stays intact and the extras read as extras. In
the same pass Cyberpunk was reworked from deep-violet + magenta to the reference gallery's bolder read:
a Night-City violet-black lit by an electric-yellow accent with a magenta/cyan/green neon triad. That
retunes a pre-existing palette's accent, so the two `#FF3FA4` literals in `ThemeSupportTests`
(the swap test and the construct-from-saved test) moved to `#F5EC1D` — those are the only tests that
pin a specific palette's accent; the parametric `EveryCatalogTheme…` guard reads from the catalog and
needs no per-theme edit.

The one behavioral change: `AppThemeService.BaseVariant` no longer assumes every non-Light/Dark
palette is dark. It now reads `ThemeCatalog.Get(id).IsDark`, so a light palette (Valentine, Retro)
bases on `ThemeVariant.Light` and keeps stock Fluent chrome legible, while dark palettes stay on
`ThemeVariant.Dark`. `IsDark` is thus the single fact deciding both the gallery swatch styling and the
Fluent base. A headless guard (`AppThemeService_EveryCatalogThemeLoadsAndMatchesItsAccentSwatch`)
applies every catalog theme and asserts its accent token equals the advertised swatch, so a mistyped
palette file name or a swatch/palette drift fails a test rather than only surfacing when a user picks
that theme.

## 2026-08-03 — All 15 built-in themes adopt the authoritative NeoStation `themes.json` verbatim

The reference owner supplied the real NeoStation theme table (`themes.json`: nine tokens — bg, surface,
surfaceAlt, border, text, textMuted, accent, accentAlt, badge — plus a `mode` per theme). It replaces
the hand-guessed colours from the passes above, which were wrong in kind, not just in shade. Notably
**Cyberpunk is `mode: light`** — a saturated yellow field (`#F7E733`) with a coral-red accent and dark
text everywhere, *not* a violet dark theme; **Nord is `mode: light`** (arctic snow-storm, not the dark
frost variant); and Aqua is a royal blue (`#0C2C8F`), not teal. Each theme's every colour now comes
straight from the spec.

Because the spec's 9 tokens don't map 1:1 to EmuShelf's 34 `EmuXxxBrush` tokens, the palettes are
**generated** by a script (kept in the scratchpad, `gen_themes.py`) that anchors on bg + surface,
interpolates the neutral surface ramp between them (dark: chrome near bg, cards at surface; light:
airy top bar, slightly-darker chrome), maps text/textMuted/border/accent directly, tints nav-selection
with the accent, and holds the semantic status colours (success/warning/danger/info/achievement)
constant per mode for legibility — the accent + surfaces carry identity, not the status set. It writes
the twelve override files plus the base `EmuShelfTheme.axaml` (Light + Dark theme-dictionaries) from
the same mapping, so base and overrides stay consistent. The fixed gamepad-button brushes are
preserved.

Per an explicit decision from the reference owner, the base **Dark/Light/System/OLED were rebased to
the spec's indigo accent** (`#5B58D9` dark / `#7C6CE0` light), replacing the earlier deliberate rose
accent. This supersedes the "rose accent" entry above for the *hue* only — the intent of that entry
(keep the brand accent distinct from danger-red) still holds, since indigo is even further from red.
The `App.axaml` Fluent `ColorPaletteResources` accents moved in lockstep. Two `ThemeSupportTests`
literals that pin base/palette accents were updated (`#5B58D9` for Oled/Dark; the swap and construct
tests now exercise Dracula rather than the now-light Cyberpunk, since their `ResolveAccentColor` helper
queries the Dark variant). The three EmuShelf-original extras (Matrix, Synthwave, Sunset) are untouched
and remain appended after the 15 spec themes.

`AppThemeService.PaletteUri` was also generalized: instead of one `switch` arm per theme, it returns
`avares://EmuShelf/Styles/Palettes/{preference}.axaml` for everything except System/Light/Dark (which
have no override — they live in the base theme-dictionaries). The palette file is named for the enum
member, so a new theme needs only its enum value, a matching `<Name>.axaml`, and a catalog row — no
wiring edit. The `EveryCatalogTheme…` guard protects that convention.

## 2026-08-03 — Four popular community palettes added as originals (Everforest, Gruvbox, Catppuccin Mocha, Kanagawa)

Beyond the NeoStation set, four widely-used editor schemes were added from their published specs, each
filling a gap the existing set left: Everforest (soft woodland green — the muted-natural-green slot, vs
Matrix's neon and Aqua/Abyss's teal), Gruvbox (warm retro orange-on-earth, distinct from Coffee's
mocha), Catppuccin Mocha (pastel mauve — the soft-pastel slot), Kanagawa (Hokusai ink-blue with warm
parchment *text*, its identity being the warm-text-on-cool-dark contrast). They reuse the same
`gen_themes.py` mapping (their 9-token specs live in an `EXTRAS` list in the script), so their surface
ramps and derivation match the spec themes; each is one `Styles/Palettes/<Name>.axaml` + enum value +
catalog row, appended after the creative trio. Verified by rendering all four to PNG. Note the enum
member `CatppuccinMocha` must stay in sync with `CatppuccinMocha.axaml` for the name-based `PaletteUri`
to resolve — the catalog-coverage guard enforces it.

## 2026-08-03 — Kanagawa recolour + four gap-filling themes (Crimson, Graphite, Mint, Lavender)

Kanagawa's original ink-blue base read too close to Catppuccin Mocha in the cover grid — both cool-dark
surfaces with a cool light accent, and the one distinguishing feature (its warm parchment *text*) is a
tiny share of the screen when opaque cover cards dominate. It was recoloured to warm sumi-ink taupe
surfaces with a calm wave-aqua accent (keeping the parchment text): warm surfaces + a cool accent is a
combination no other theme uses, so it now separates cleanly from both Catppuccin (cool + mauve) and
the warm-orange themes (Coffee/Gruvbox/Sunset, which pair warm surfaces with warm accents).

Four more themes were then added to occupy colour territory nothing held: Crimson (deep wine + vivid
crimson — the first red theme), Graphite (pure neutral grayscale + silver — the only hueless theme;
its semantic status colours stay coloured for legibility, since the identity is the grey surfaces and
accent), Mint (pale light green — the first *light* green, as Matrix/Everforest/Abyss are all dark),
and Lavender (soft light lilac — the first *light* purple, as Dracula/Palenight/Catppuccin/Synthwave
are all dark). Same generation path and guard as the other extras; all verified by PNG render. Total
is now 26 built-in themes.

## 2026-08-03 — Four named developer schemes (Solarized, Rosé Pine, Oxocarbon, Ayu)

The last of the well-known palettes, added from their published specs to round out the gallery:
Solarized (the low-contrast teal-navy classic with a clean blue accent), Rosé Pine (muted plum with a
soft dusty-rose accent), Oxocarbon (IBM-Carbon near-black with vivid magenta), and Ayu (cool near-black
with a warm amber accent — its cool-base/warm-accent contrast mirrors, inverted, Kanagawa's warm-base/
cool-accent). Two placements were chosen to avoid the crowding these near-black/plum schemes risk:
Oxocarbon sits a step darker than Graphite and leads with magenta, so the two carbon themes read as
"neon" vs "silver-mono" rather than as duplicates; Rosé Pine keeps a soft, desaturated dusty-rose
accent so it stays distinct from Horizon's punchier coral on a similar plum base (the closest surviving
pair, but separated by accent chroma). Same `EXTRAS`/`gen_themes.py` path and coverage guard as the
other originals. 30 built-in themes total; the colour wheel is now covered across both light and dark,
so future additions would be variants rather than new hues.

## 2026-08-03 — Web API key now persists on Linux/macOS via an obfuscated portable blob

Revisits the non-Windows half of the 2026-07-18 M10 §2 storage decision. The session-only in-memory
store dropped the RetroAchievements Web API key on every launch, so real Linux use (Steam Deck) had to
re-enter it after each app update — the update was only incidental; any restart lost it. The original
blocker was "no verified portable at-rest protection," but the key is low-value: a read-only Web API
key, never an emulator token, one-click resettable on the RA site, already excluded from settings.json
and logs. That does not warrant keychain-grade protection.

Non-Windows platforms now use `PortableObfuscatedCredentialStore`: the key is AES-GCM wrapped with an
application-embedded 256-bit key and written to the same `Settings/retroachievements.key` blob the
Windows DPAPI store uses (owner-only `0600` on Unix). This is deliberately obfuscation, not
confidentiality — the wrap key ships in the binary — chosen so the blob stays fully portable across
machines (a machine-bound scheme like DPAPI would not survive moving the drive) while keeping the key
off disk as readable plaintext. The OS keychain route (Secret Service/libsecret, KWallet) was rejected:
non-portable, heavy P/Invoke/D-Bus, and unreliable in exactly the target case — Steam Deck Game Mode
with the passwordless `deck` user, where the keyring is often locked or unset. `SessionOnlyCredentialStore`
remains as the non-persisting in-memory implementation used by tests. macOS could later gain a
Keychain-backed store alongside Windows DPAPI, but is not required for the portable bar this sets.

## 2026-08-03 — Nintendo 3DS ships behind Azahar with id-addressed covers and no hashing

3DS support was added as a standalone-emulator handheld cartridge system. Azahar (the maintained
Citra successor) is a new `EmulatorDefinition` alongside PPSSPP/Dolphin — its own executable, the
game path passed as one argv entry — rather than a RetroArch core. The emulator and the `3ds`
system are registered in the same change because `EmulatorSettingsViewModel` resolves each system's
emulator with `emulators.First(e => e.Supports(id))`, which throws if none supports it.

**Launch-all, identify-dumps scope.** `Nintendo3dsRomReader` recognizes every container Azahar
loads by a bounded magic/structure check — NCSD (`.3ds`/`.cci`), NCCH (`.cxi`/`.app`), CIA
(`.cia`), homebrew (`.3dsx`/`.elf`/`.axf`), and the seekable-Zstandard compressed variants
(`.z3ds`/`.zcci`/`.zcxi`/`.zcia`/`.z3dsx`) — so all of them import and launch, while a renamed
arbitrary file is rejected. Exact identity (the plaintext NCCH product code and title id) is read
only from the uncompressed NCSD/NCCH dumps via targeted header reads: 3DS dumps are multi-gigabyte,
so nothing hashes the whole file, and the header stays plaintext even on encrypted dumps (Azahar
rejects encrypted content at launch). Compressed/CIA/homebrew files carry no header identity here
and fall back to the filename for cover matching until a dedicated reader (Zstandard-frame metadata
or CIA ticket/TMD) is added.

**Covers are id-addressed via GameTDB.** No-Intro 3DS catalogues key on a whole-file hash EmuShelf
deliberately never computes, so cover matching uses `GameTdb3dsArtworkProvider`, keyed by the NCCH
product code's four-character game code (region from its fourth character, English/US fallbacks),
mirroring the GameCube/Wii GameTDB route (DECISIONS 2026-07-17). It resolves covers without a
catalogue title match; the No-Intro 3DS DAT and the Libretro title provider are supplied as
best-effort fallbacks only. The `3ds` cover frame is 1.129, measured from GameTDB's fixed 768×680
front-cover canvas.

**Placeholder icon is original.** OpenEmu ships no 3DS asset and both the sidebar and
`PlatformArtworkTests` require non-null platform art, so an original dual-screen clamshell
(`PlatformConsoleArt/3ds.png`, no Nintendo/OpenEmu branding) is bundled and mapped in
`PlatformArtwork.ConsoleAssets`.

**RetroAchievements stays out.** `RetroAchievementsConsoles.ForSystem("3ds")` remains null, so 3DS
displays as unsupported like PS3 — matching the requested "no RA yet" scope. Save sync is likewise
deferred (no Azahar save-location provider yet).

## 2026-08-03 — 3DS reaches save-sync and texture-pack parity with the other standalone emulators

A parity review against the existing platforms found the first 3DS pass shipped the library, launch,
metadata, and cover integration but not the two per-emulator adapters every comparable standalone
emulator carries: a save-location provider (which every system has) and, for the HD-enhancement
emulators, texture-pack adapters. Both are now implemented for Azahar, superseding the "save sync
deferred" note in the entry above.

**Save sync keys by stable id and rebases the console-unique path.** Azahar keeps in-game saves on
its emulated SD card at `sdmc/Nintendo 3DS/<ID0>/<ID1>/title/<hi>/<lo>/data` (and `extdata/00000000/
<id>`), where `<ID0>/<ID1>` is a console-unique pair that differs between installs.
`AzaharSaveLocationProvider` therefore makes each title's save archive and each extdata archive a
sync unit keyed by the machine-independent title id / extdata id, and resolves it under whichever
console folder exists on the local machine — so a save moves between machines despite the differing
on-disk path (a cross-console round-trip test proves this). Installed updates/DLC (the sibling
`content` folder) and build-fragile save states are never synced, matching the M29 battery/memory-
card boundary; a machine that has never created its SD card cannot materialize a remote unit until
it does. The user directory follows Azahar's own `common_paths` (portable `user/` beside the
executable, else `%APPDATA%\Azahar` / `~/.local/share/azahar-emu` / the `org.azahar_emu.Azahar`
Flatpak), with the Settings override as the escape hatch.

**Texture packs match by title id via a new exact rule.** Azahar loads custom textures from
`<user>/load/textures/<title id>` and gates them on `qt-config.ini` `[Utility] custom_textures`. The
texture inventory previously indexed only serials and disc ids, so a new
`TexturePackMatchRule.Nintendo3dsTitleId` (appended last so the cached inventory's rule ordinals stay
stable) plus `GameIdentifierKind.TitleId` indexing were added to `TexturePackMatcher` and
`TexturePackLibraryMap`; a pack folder named by a 16-hex title id matches the game whose extracted
title id equals it.

Both adapters are unit-tested against synthetic directories and were then verified read-only against
a real Windows Azahar install (an opt-in `EMUSHELF_TEST_AZAHAR_DIR` test): the save provider
enumerated every title/data and extdata archive and resolved each to its real SD-card folder, the
texture inventory found usable `load/textures/<title id>` packs keyed by title id, and `qt-config.ini
[Utility] custom_textures` read correctly. The real install confirmed the portable `user/` layout,
that the default console `ID0/ID1` is all zeros (so default installs share it and title-id keying
crosses machines directly), that `title/<hi>/<lo>` folder names are lowercase while `extdata` ids are
uppercase (the case-insensitive hex validation accepts both), and that the texture folders are
uppercase 16-hex title ids matching the extractor's `X16` title id. A live rclone cloud round-trip
and a manual emulator launch/return remain the only unverified paths.

## 2026-08-04 — The gamepad achievements grid is row-virtualized too, matching the library grid

The achievements overlay's badge grid was the last surface still built on `ItemsRepeater` +
`UniformGridLayout` inside a `ScrollViewer` — the exact construct the library grid was torn out of on
2026-08-03 for its phantom-cell defect. On the real Deck compositor it reproduced the same "top-left
hole": force-realizing an anchor tile during reveal made `UniformGridLayout` reserve cell 0 and shift
every tile one column right, so badges went missing from the grid (visible as blank gaps in a
controller screenshot). It never reproduced headless. Worse, this grid derived its column count the
way the library grid explicitly abandoned — `SyncGamepadAchievementColumnCountFromLayout` read the
*realized* tile bounds, which race the compositor mid-recycle and yield a garbage stride.

The fix mirrors the library grid one-for-one. The grid is now a `ListBox` (`GamepadAchievementRowList`)
bound to `GamepadAchievementRows`, a projection of the flat `VisibleAchievements` list into rows of
`GamepadAchievementColumnCount` (`BuildGamepadAchievementRows`, the twin of `BuildGamepadRows`), with a
vertical `VirtualizingStackPanel`; each row is a horizontal strip of the existing 100px badge tiles.
Only the on-screen rows realize (an 86-tile grid materializes ~24 tiles, proven by
`GamepadAchievements_LargeVirtualizedGridStartsInTheTopLeftCell`), each row holds exactly the column
count so rendered columns can never disagree with the navigation stride, and reveal is native
`ScrollIntoView(rowIndex)` — the bounds-reading reveal/column machinery is deleted. The column count is
now derived by pure width arithmetic (`ColumnsThatFit(width, 100, spacing:12)`, gaining a
spacing-parameter overload) from the ListBox's `SizeChanged`, exactly like `GamepadColumnCount`;
navigation still runs on the flat `VisibleAchievements` list + `index % columns`. `GamepadAchievementRows`
auto-rebuilds on the column count changing, on the visible set changing (filter/sort/refresh, via the
existing `HandleGamepadAchievementDetailsPropertyChanged`), and on the overlay opening. The
`VisibleAchievements`-change subscription moved into an `OnGamepadAchievementDetailsChanged` partial so
the projection stays wired whether details are assigned through the open command or directly (as the
snapshot tests do). Because every visible achievement always lands in some row, the count can no longer
be dropped regardless of a transient column-count value —
`GamepadAchievements_RowProjectionSlicesEveryAchievementAndReflowsOnWidth` proves the flattened rows
reproduce the visible list exactly across width and filter changes. The top-left-hole regression and
the scroll-containment behavior are still asserted, now against the row ListBox rather than the removed
repeater/scroller. Correctness is covered headlessly; the on-device reveal timing shares the library
grid's already-verified path.

Controller focus was then unified with the library grid too: each badge tile is wrapped in a
`Panel.gamepad-achievement-tile` holding the same `Border.gamepad-focus-tile-ring` accent pad the covers
use — a solid `EmuAccentBrush` pad 6px behind the tile with a concentric radius (14 + 6) and
`EmuFocusGlow`, revealed by opacity on the shared `.focused` selector. It replaced the old
border-recolour + drop-shadow so selection reads as the same even accent frame everywhere. The
`.focused` class is data-driven from `IsFocused` (independent of keyboard focus), so it lives on the
outer Panel — the required ancestor of the ring — while the tile Border keeps the `gamepad-achievement`
class the tests query; the opaque tile masks the pad centre exactly as the opaque cover does.

## 2026-08-04 — Gamepad grid uses one uniform cover frame, not per-platform frames

The desktop grid gives every tile its platform's true cover frame (`CoverHeight` from
`GameSystem.CoverAspectRatio`) and bottom-aligns them on a shared shelf sized to the tallest cover in
the view. In a single-system view that is uniform, but in a mixed "All Games" view the aspect ratios
span from PSP's `0.581` (tall) to SNES's `1.434` (short and wide), so bottom-aligning them leaves a
large empty void above every short cover and a ragged top edge — chaotic at couch scale where tiles are
~2× bigger with more spacing. The desktop grid keeps this behaviour; the gamepad grid was the complaint.

The gamepad grid now draws every tile into one fixed frame regardless of platform:
`GameViewModel.GamepadCoverHeight = round(CoverWidth / GamepadUniformAspectRatio)` with
`GamepadUniformAspectRatio = 0.708`, the disc-system ratio the library is mostly made of. The
row-`ListBox` tile ([MainWindow.axaml]) dropped its `ShelfCoverHeight` shelf + bottom-aligned
`CoverHeight` panel for a single `GamepadCoverHeight` frame, and the cover `Image` switched from
`UniformToFill` to `Uniform`: disc-system art fills the frame exactly, while off-ratio art (square PS1,
wide SNES) is letterboxed on the cover well rather than cropped, so no box art is ever chopped. The
existing `Border.gamepad-cover-frame` card border + `BoxShadow` now read as an intentional matte behind
the letterbox bars. The tile shape never changes as you switch platforms — the stable-tile look of
OpenEmu and Steam Big Picture. `GamepadCoverHeight` is gamepad-only, so `CoverHeight`/`ShelfCoverHeight`
and the desktop grid are untouched;
`GamepadCoverHeight_IsUniformAcrossPlatforms_WhileDesktopHeightFollowsAspect` locks in that a square and
a portrait platform share one gamepad frame height while their desktop heights still differ. Trade-off:
a single-system view of a landscape platform (SNES/Arcade) now shows matte bars instead of a tight
short frame, accepted for one consistent couch grid across every platform switch.

## 2026-08-04 — Recently Played is a smart collection, stamped at launch

Recently Played ships as a first-class smart collection — a new `LibraryScope.RecentlyPlayed`
alongside `AllGames` and `RecentlyAdded` — rather than a Steam-style multi-shelf "home" view. The
desktop shell has no home surface (it shows one virtualized scope at a time), and a shelf is just the
top-N of a collection rendered as a row, so the collection is the data layer a future home view would
compose from. It reuses the sibling's entire machinery: the scope cache, virtualized grid, empty
states, the desktop COLLECTIONS sidebar list, the Gamepad Collections overlay, and scope persistence
(the restore path already `Enum.TryParse`s the scope). Direction chosen with the user; a Steam-style
home is tracked as a separate future milestone.

Storage is a single nullable `Games.LastPlayedUnixMilliseconds` column (schema v15), not a play-history
table. It mirrors `DateAddedUnixMilliseconds` (nullable = never played, partial index over played rows
only) and keeps the change minimal; a `PlayCount`/"Most Played" or per-session playtime would be a
later column or a history table, added deliberately. `Game.LastPlayedAt` is `DateTimeOffset?`.

The stamp is written in the launch flow's `beforeStart` callback, which `EmulatorLaunchService` invokes
only *after* preflight passes and immediately *before* the emulator process starts. So a launch that
fails validation is never recorded, and one that starts is recorded even if EmuShelf is killed
mid-session — closer to "last played" than stamping on exit, and it never touches the game file. A
recorded play refreshes Recently Played surgically: if the user launched from within it, the current
scope rebuilds so the game jumps to the front on return; otherwise only that scope's cache entry is
evicted so the *next* visit rebuilds — no cover reflow of the scope they returned to.

Recency collections now display in recency order. `SortGames` short-circuits for
`RecentlyAdded`/`RecentlyPlayed` and preserves the load order (newest activity first) instead of
applying the default Title column sort. This also fixes `RecentlyAdded`, whose own
`Collections_…RecentlyAddedNewestFirst` test asserted newest-first but was previously only satisfied by
a coincidental title ordering — the column sort silently overrode the intended recency order. Trade-off:
the list-view column-sort headers are inert within these two collections, accepted because recency *is*
their sort; a deliberate "sort within a collection" affordance can revisit that later.

## 2026-08-04 — Ambient theming: the couch UI recolours from the focused game's artwork

"Match colours to artwork" reuses the existing theme hot-swap seam rather than a new mechanism. Every
UI colour is an `EmuXxxBrush` token consumed via `DynamicResource`, so `AppThemeService` already
re-colours the whole app live by swapping a palette dictionary. Ambient mode generates that dictionary
at runtime from a game's artwork (`ApplyArtworkPalette`) and appends it *above* the chosen theme's
override, so the picked theme becomes the fallback and stays live for artwork with no usable colour.
`RequestedThemeVariant` follows the artwork's brightness, which is what flips a bright cover's panel to
light while a dark cover stays dark — the same dark/light switch the built-in themes already flip.

Colour derivation is a pure Core function (`ArtworkPaletteFactory`, tested without Avalonia); the App
only extracts (`ArtworkPaletteExtractor`). The extractor buckets pixels by hue and picks the most
saturation-weighted swatch, plus the mean WCAG luminance. The factory takes the *hue* from the art but
*forces* every surface/text lightness into a safe band, so no cover can turn the menu into an unreadable
smear; body text is pushed toward pure white/near-black until it clears a 4.5:1 floor, and the
on-accent glyph colour is chosen by contrast. The dark/light decision carries a hysteresis dead zone
(stay dark ≤0.58, stay light ≥0.42) so scrolling a run of mid-brightness covers never strobes the shell.

It samples the on-screen **cover** (the already-decoded `CoverImage`), not fan art. Fan art can be
scraped but is never displayed anywhere yet, and the cover is what the user is actually looking at, so
its colour matches the screen; when a fan-art/hero display lands, the sampler can prefer it with a
one-line source change. Pixels are copied on the UI thread and analysed on a worker, the result is
cached per cover path so re-focus is instant, and grayscale/low-saturation art returns null so the
chosen theme shows through. The effect is a couch-mode feature: it is driven by the Gamepad
`FocusedGame` (debounced 120 ms) and cleared when returning to Desktop.

The toggle (`AppSettings.AmbientThemeFromArtwork`) lives in the Desktop settings Appearance section,
right with the theme gallery. A Gamepad-settings toggle row was deliberately deferred: the couch Themes
section is a gallery with no row support, and adding the toggle to the General section broke the
Desktop/Gamepad per-section parity contract its snapshot tests enforce. Integrating a toggle into the
couch Themes-gallery focus model is a follow-up; until then the setting is reached from Desktop
settings and the live effect is visible in Gamepad mode.
## 2026-08-04 — Application-identity credentials are embedded into the build

The 2026-08-03 decision provisioned the ScreenScraper developer credentials only from runtime
environment variables, and cloud sync fell back to rclone's shared Google client when the user
supplied none. That works when the app is launched from a shell that exports those variables (a
developer's machine) but not from a Steam Deck / desktop / gamescope session, which inherits none of
them — so a shipped install reported "ScreenScraper isn't configured in this build" and cloud sync
ran on the rate-limited shared client. This amends that rule: **application-identity credentials are
now baked into the build.** This is how every comparable frontend (EmulationStation, Skyscraper,
Batocera, Skraper) ships ScreenScraper access, and it is what ScreenScraper's model expects — the
`devid` identifies EmuShelf-the-app, not a user, and is meant to be shared across every install.

What is embedded, and only this: the ScreenScraper `devid`/`devpassword`/`softname`, and the Google
Drive OAuth **client** id + secret. The Google *client* is app identity (Google treats a desktop
OAuth client secret as non-confidential); each user still runs their own OAuth flow and syncs into
their **own** Drive. The per-user connected-Drive token is never embedded — doing so would funnel
every user's saves into one account. A user-supplied client still wins over the embedded default, and
a developer environment variable still wins per field over the embedded value, so existing workflows
are unchanged; when neither source has a field the app falls back to its prior behaviour (ScreenScraper
stays unconfigured, rclone uses its shared client).

Mechanism: `src/EmuShelf.Infrastructure/Build/EmbeddedSecrets.targets` reads the build-time
environment variables (`SCREENSCRAPER_DEV_ID`, `SCREENSCRAPER_DEV_PASSWORD`, `SCREENSCRAPER_SOFTNAME`,
`EMUSHELF_GOOGLE_OAUTH_CLIENT_ID`, `EMUSHELF_GOOGLE_OAUTH_CLIENT_SECRET`) and generates a partial
`EmbeddedSecrets` class into `obj/` — so the secret still never enters the repository (obj/ is
gitignored), only the shipped binary. Values are XOR+Base64 encoded: Base64 guarantees the generated
C# literal is always valid regardless of the raw bytes, and the XOR only keeps the strings out of a
naive `strings`/scraper sweep — it is not a security boundary, consistent with these being
non-confidential shared credentials. `ScreenScraperDeveloperCredentialSource.Resolve` and
`RcloneConfigurator.ResolveGoogleClient` hold the precedence/all-or-nothing logic and are unit-tested;
the encode↔decode round-trip is guarded by a test that mirrors the build encoder.

CI wiring: `.github/workflows/build.yml` passes the five values as step-level `env:` on each
`dotnet publish` (Windows, Linux/AppImage, macOS) from repository secrets of the same names. Secrets
are unavailable to fork pull requests, and the matrix `build`/`test` job is deliberately left without
them, so only same-repo packaged artifacts carry credentials while every other build still compiles.

The *user's* ScreenScraper login is now persisted on every platform too, closing the earlier
session-only gap on Linux/Steam Deck and macOS. `WindowsScreenScraperCredentialStore` became the
platform-neutral `TextBackedScreenScraperCredentialStore` over a new `IProtectedTextStore`: DPAPI on
Windows, and a portable AES-GCM blob (`PortableObfuscatedTextStore`) elsewhere — the same
obfuscation-not-confidentiality trade-off already used for the RetroAchievements key, writing the same
`Settings/screenscraper.account` file so the login survives restarts and updates.

## 2026-08-04 — Gamepad grid: the selector is centre-anchored and the scroll eases instead of snapping

Controller navigation felt choppy and the selector landed unpredictably — top on one platform, middle
or bottom on another. Root cause was the reveal in `RevealFocusedGame`: it called
`GamepadRowList.ScrollIntoView(rowIndex)`, which scrolls the *minimum* to make a row visible (so the
selector walks to whichever viewport edge it hits and sticks there), then posted a *second* instant
offset change in `NudgeGlowClearance`. Two hard jumps per d-pad step, and because rows are taller on
portrait-cover platforms (PSP 0.581, PS2 0.708) than on wide ones (SNES 1.434), the number of visible
rows — and thus where the edge-stuck selector sat — differed per platform.

Both are now replaced by a single rule: **anchor the focused row on the viewport's vertical centre**
(clamped at the ends) and **ease the `ScrollViewer.Offset` toward that anchor**. Centre-anchoring makes
the selector position aspect-ratio-independent — the focused tile settles on the same line on every
platform (asserted for PSP/SNES/PS2 in `SelectorIsCentered_DeepInList_RegardlessOfAspectRatio`). The
ease turns a held d-pad into one continuous scroll: a retarget mid-flight just moves the goal the follow
chases, so fast auto-repeat no longer strobes row-to-row.

The ease is deliberately **not** a wall-clock `DispatcherTimer`. It self-reposts at
`DispatcherPriority.Render`, advancing a fixed fraction (0.3) of the remaining distance per step. At
runtime that is ~one step per frame (smooth, and it stops reposting the instant it settles, so an idle
grid and the Deck battery cost nothing); under the headless test pump the same `Render` flushes advance
it deterministically with no real time passing — matching the repo convention that reveal work settle
via dispatcher priority, never a timer, so `GamepadGridSelectorTests` stays reliable. A generation token
guarantees exactly one live loop when a jump interleaves with a queued step, and the loop terminates the
instant the offset stops advancing (arrived, or the panel clamped it), so it can never spin.

Big discrete jumps (LB/RB platform switch resetting focus to the first game, restoring a deep remembered
row, landing on an end) **snap** rather than ease — a move of more than 1.5 viewports is not a d-pad
step, and easing across many screens only feels sluggish. A jump uses `ScrollIntoView`, not a manual
offset: a virtualizing panel discards an offset set into a not-yet-realized region on the next layout
pass (observed: a manual jump to the last row was reset to the top), whereas `ScrollIntoView` realizes
and positions the far row reliably. Row height for the centre maths is read from any realized row
(uniform per view), falling back to `ShelfCoverHeight + 90` chrome before the first row realizes.

## 2026-08-04 — Gamepad uniform cover frame applies only to mixed views (revises the entry above)

The earlier "Gamepad grid uses one uniform cover frame" decision was too broad. Drawing every tile
into one fixed frame regardless of platform also barred single-platform views — a landscape SNES or
square PS1 view, whose covers already share a shape and never needed unifying, gained letterbox bars
top and bottom, which read as worse than the ragged mixed view the change was meant to fix (user
feedback). Corrected to be scope-aware. `MainViewModel.GamepadCoverHeightFor(games, coverWidth)`
returns the platform's true height when the visible view holds a single system, and the uniform
`GameViewModel.GamepadMixedCoverAspectRatio` (0.708) height only when the view mixes platforms (All
Games and the like). A single-platform view is therefore back to the desktop cover shape; only a
genuinely mixed view is flattened onto one frame. The cover `Image` also went from `Uniform`
(letterbox) back to `UniformToFill`, so covers fill the frame with no bars anywhere: a single-platform
frame matches its art exactly, and the mixed frame crops the off-ratio minority (wide SNES, square
PS1) to fill rather than barring it. `ApplyCoverLayout`'s gamepad height is nullable and defaults to
the tile's own `CoverHeight`, which every single-platform caller and the constructor rely on. Covered
by `GamepadCoverHeightFor_UsesTheTrueHeightForOnePlatform_AndAUniformHeightForAMix` and the rewritten
`GamepadCoverHeight_DefaultsToTheTrueFrame_ButHonorsAnExplicitUniformHeight`. Trade-off accepted:
in a mixed All Games view the wide/square minority is cropped rather than shown whole — the couch grid
stays even and bar-free, which the crop preserves and letterboxing did not.

## 2026-08-05 — Settings opens without a per-system database walk; ScreenScraper reaches Gamepad settings

Opening Settings (both Desktop and Gamepad) rebuilt its per-system emulator-configuration map by
calling `IEmulatorConfigurationStore.Get` once per system, and `SqliteEmulatorConfigurationStore.Get`
opens a fresh SQLite connection and runs a JOIN each call. Five systems meant five connection opens
plus five queries on the path between the button press and the panel appearing — the visible "slight
delay," worst on the first open when the thread pool is cold. Replaced with `GetAll(systemIds)`, which
reads every requested system in one connection/query and returns an entry per id (null when
unconfigured), preserving the old dictionary shape. The interface ships a default that falls back to
per-system `Get` so minimal stores and test doubles need no change; the SQLite store overrides it. The
map is still re-read on each open (not cached) so edits made in the Emulators section stay authoritative
— the cost is now one query, not five connections.

Two smaller fixes on the same path: `RequestSettingsFromGamepadAsync` now guards against re-entry with
`_openingGamepadSettings` (it awaits a database read, so a double-press could otherwise start two
overlapping opens and race on `GamepadSettings`); and `GamepadSettingsViewModel` no longer rebuilds its
row list twice per synchronous edit. A toggle/choice/text edit writes to the settings model, which
echoes `PropertyChanged` back into `OnSettingsPropertyChanged` — the caller already runs one explicit
`RebuildRows`, so the echoed rebuild is suppressed via `_applyingLocalEdit` (set only around the
synchronous write). Async action rows (connect, sync, disconnect) stay unguarded so their live status
keeps refreshing during the operation.

ScreenScraper account management was Desktop-only: the Gamepad projection was constructed with
`screenScraper: null` on the theory that the per-game scraper overlay covers login. That buried account
setup behind first selecting a game, unlike RetroAchievements which has its own Gamepad section. The
Gamepad settings model now receives the real `CreateScreenScraperSettingsContext()`, and
`GamepadSettingsViewModel` projects the section (username / masked password / connect when
disconnected; account summary + destructive disconnect when connected), mirroring RetroAchievements.
The section rail gained a ScreenScraper button, shown only when an account context exists
(`Settings.HasScreenScraper`), so builds without ScreenScraper credentials are unchanged.
## 2026-08-05 — Batch scraper serialises progress against finalisation with a lock

`GameBatchScraperViewModel` reports progress through `Progress<GameScrapeBatchProgress>`. In the real
app that captures Avalonia's `SynchronizationContext`, so `OnProgress` and the completion continuation
both run ordered on the UI thread and the final summary always wins. Under test there is no captured
context, so `Progress<T>` delivers callbacks on the thread pool, unordered — a `"Scraping… N of N"`
report queued mid-run can land *after* the summary is written and overwrite `StatusMessage` back from
`"N scraped"`. The prior mitigation (set `State = Done` before writing the summary, and have
`OnProgress` drop reports when `State != Running`) is a cross-thread check-then-act with no barrier, so
it still raced — it surfaced as an intermittent Ubuntu CI failure of
`Start_WithEverythingSelected_RunsFillMissing_AllFields_AllMedia` (macOS/Windows passed the same
commit). Fixed by serialising both `OnProgress` and the run's finalisation on one `_statusGate` lock, so
the "still Running?" check and the status write are atomic against completion: a late report either ran
before finalisation or, acquiring the lock afterwards, sees `Done` and drops itself. The lock is
uncontended in production (single UI thread) and adds no behaviour there; it only makes the ordering
deterministic where no context serialises it.
## 2026-08-05 — Gamepad grid: centre by ScrollIntoView + one relative nudge, drop the eased scroll (revises the centre-anchor entry above)

The "centre-anchor + ease" change above regressed on the real Steam Deck compositor: the selector could
land off-screen, a ton of phantom empty space appeared above the content, and the grid felt chaotic
after LB/RB switches. Root cause: that design wrote `ScrollViewer.Offset` **directly, every frame, to an
absolute value computed as `rowIndex * rowHeight`**. Avalonia's `VirtualizingStackPanel` has no real
extent — it *estimates* one from the average height of realized rows — so external absolute offset writes
desync its accounting and it renders realized rows with phantom space before them. It never reproduced
headless (both the centring test and a fresh platform-switch repro pass), which is why it shipped green;
the corruption is real-compositor-only. Easing also *worsened* the cover-recycle flashing, because it
scrolls *through* every intermediate row (each realizes and loads a cover) instead of jumping.

Reverted to the stable pattern the pre-PR reveal used — `ScrollIntoView` plus a **single relative
nudge** — but nudging to *centre* instead of edge-clearance. `RevealFocusedGame` now: if the focused
row's container is realized, `CentreRealizedRow` reads its measured position and does one relative
`Offset` nudge so the row's centre lands on the viewport centre (a one-row move nudges by one row, the
grid scrolling under a stationary selector); if the row is NOT realized (a far jump), it `ScrollIntoView`s
and retries so the centring runs in a *later* pass (mixing `ScrollIntoView`, which schedules its own
scroll, with the nudge in one pass makes the two fight). No per-frame writes, no absolute `rowHeight`
math, no `Extent` reads — so nothing can desync the panel. The selector is still centred and
aspect-ratio-independent (`SelectorIsCentered_DeepInList_RegardlessOfAspectRatio` still passes); the
scroll is now a clean single jump per step rather than an animation. Deleted the ease loop, its
generation token, and the smoothing/snap constants. Smooth animation was dropped deliberately (user
chose reliability over animation) and can be revisited later with a position-relative ease.

Two supporting fixes found while debugging this:
- **The grid tiles take no keyboard focus.** The old reveal focused the tile "for directional routing",
  but gamepad input is routed by the window-level *tunnel* `OnWindowKeyDown` (which runs before any
  focused control) and the ring is `IsFocused`-driven, so the focus was unnecessary — and
  `FocusManager.Focus(tile, Directional)` raises a bring-into-view that scrolled the freshly focused tile
  to an edge (measured: centred at offset 8543, focus shoved it to 8868), fighting the centring. Removing
  it is what makes the centre stick. Taking no focus is also safe on return from a text overlay: those
  hide via `IsVisible`, so Avalonia drops focus off their `TextBox` to null on close (verified — the
  focused element becomes null), and the window tunnel then handles the d-pad. (An interim guard that
  re-focused the row list on a lingering `TextBox` was dropped: it was a no-op — `ListBox.Focus()` returns
  false here — for a case that never arises.) Guarded by
  `Reveal_DoesNotStealFocus_FromOpenSearchBox`, which also pins that the reveal never disturbs an open
  overlay's live text focus.
- The reveal's ScrollViewer/viewport-not-ready retry is kept for the first frame after a mode/scope
  switch.

## 2026-08-05 — Gamepad grid: re-introduce smooth scroll as a position-relative ease, and warm covers ahead of it (revises the "drop the eased scroll" entry above)

The single-jump reveal above was reliable but still read as janky: a held d-pad auto-repeats ~one row
every 110ms (`GamepadNavigationController`), and each repeat teleported the grid a whole row, so a hold
produced a staccato staircase rather than a glide. The couch-UI expectation is a continuous scroll under
a stationary centred selector. The previous eased design was reverted because it wrote
`ScrollViewer.Offset` **absolutely** (`rowIndex * rowHeight`), which desynced the `VirtualizingStackPanel`'s
*estimated* extent on the real compositor. This entry brings the ease back in the form that entry
explicitly sanctioned — **position-relative** — plus the cover work that stops the ease from flashing.

Three coupled changes (`MainWindow.axaml.cs`, `MainViewModel.cs`):

- **Position-relative ease toward a FIXED target (`StartOrRetargetGamepadScroll` + `StepGamepadScroll`).**
  A d-pad move (`RevealFocusedGame(animate: true)`) measures the target row's centre offset **once**, in
  the same safe (input/loaded) context the snap uses (`TryMeasureCentreDelta`), and computes an absolute
  target `currentOffset + delta` — position-relative (never `rowIndex*rowHeight`), so it cannot desync the
  panel's estimated extent (the failure that forced the revert). A self-reposting `Render`-priority loop
  then eases the offset a fraction (`GamepadScrollSmoothing = 0.28`) of the remaining distance to that
  fixed number. **The loop does pure offset arithmetic — it never reads the visual tree.** An earlier
  attempt re-measured the row (`TranslatePoint`/`Bounds`) every frame; that read forces a re-entrant layout
  that **stack-overflows the `VirtualizingStackPanel` on short-cover rows** (reproduced deterministically
  by the `snes` case of `SelectorIsCentered…`, which crashed the test host; the tall-cover `psp`/`ps2`
  cases survived, so it shipped-looking-green in the earlier eased design too). Measuring once and lerping
  a stored number is exactly the structure of the pre-revert ease, which was headless-safe. A held d-pad
  retargets the one running loop (no stacked snaps); it settles within a few frames of release and stops
  reposting, so an idle grid burns no CPU. Termination is covered on both ends: within
  `GamepadScrollSettleThreshold` (0.5px) it lands exactly and stops, and if a step produces no offset
  movement it is clamped at a list end (first/last rows can't be centred) and stops.
- **Snap is kept for everything that is not a one-row step.** `RevealFocusedGame` eases only when the row
  moved ≤ `GamepadMaxEaseRowStep` (2) from the last revealed row; a scope restore of a deep row, a pointer
  tap on a distant tile, or the first reveal in a scope snaps via the old measured nudge / `ScrollIntoView`.
  Easing across many screens is exactly what realizes and flashes a cover on every intermediate row, which
  is why far moves must not ease. `_lastRevealedRowIndex` is reset on a scope/platform switch so the
  post-switch landing is treated as fresh (snap), and the non-animate reveals (resize, GridCoverWidth)
  stay snaps.
- **Cover prefetch (`PrefetchCoversAroundFocus`).** Cover loads were gated on tile realization
  (`OnGameCoverAttached`), one row too late for a smooth scroll — the row arrived blank and the art popped
  in a beat behind. On every focus change the view model now also warms the covers `GamepadCoverPrefetchRows`
  (3) either side of the focused row, decoupling the load from realization so the glide passes over
  already-painted tiles. Re-warming an already-loaded/loading cover is a synchronous no-op and covers
  persist on the view model, so re-running the window each step is cheap.

Also, an unrelated-but-adjacent perf win in the shared cover path: `LoadGameCoverAsync` now decodes each
thumbnail to the tile's displayed pixel width (`Bitmap.DecodeToWidth`, `MaxCoverWidth × CoverRenderScale`,
capped at the source's `CoverThumbnailNativeWidth` so it is never upscaled) instead of the full 300×400.
The grid never renders a cover wider than `MaxCoverWidth`, so this is visually identical while cutting the
GPU upload and per-cover memory — most valuable when a run of covers lands together during a fast scroll.
`CoverRenderScale` is pushed in from the view (`ApplyCellWidth`) so the decode stays crisp on HiDPI.

`SelectorIsCentered_DeepInList_RegardlessOfAspectRatio` still asserts the settled centre (its `SettleScroll`
helper pumps the ease to rest); the smoothing/snap constants and the ease loop's generation token are back.
## 2026-08-05 — Gamepad spotlight view: a switchable list + fanart hero beside the cover grid

Added the couch-mode "spotlight" layout from the target screenshots — a scrolling game **list** on
the left and a large **fanart hero** (title, filename subtitle, star rating, achievement progress,
Play) on the right — as a **second, switchable** gamepad view rather than a replacement for the
existing cover grid. The grid is mature and tested, and the target toolbar itself implies multiple
views, so the two coexist and the grid stays the default (`LibraryViewSettings.GamepadSpotlightView`,
off by default, remembered across launches; a couch-only preference kept independent of the desktop
`IsGridView`).

Design choices:
- **Reuses the existing `FocusedGame` model.** LB/RB, A/Y, and every overlay behave exactly as over
  the grid. The window-level *tunnel* key handler consumes the d-pad before the list can see it, so
  the list is a passive selection surface (`SelectedItem` tracks `FocusedGame`, auto-scrolling into
  view) — no second input path. In the single-column list, Down/Up step one game and Left/Right are
  inert, branched in `DispatchLibraryAction` (the grid keeps its `GamepadColumnCount`-stride 2-D
  movement).
- **Fan art is displayed for the first time.** It was scrapeable (`GameMediaKind.Fanart`) but never
  shown. The focused game's scraped details (fan-art path + rating) are read once per game off the UI
  thread and cached on the view model; only the current hero keeps a decoded bitmap (released as focus
  moves, generation-guarded), so a long list never accumulates full-size images. No-fanart games fall
  back to the cover on the themed surface. The grid isn't realizing tiles in spotlight mode, so the
  focused **cover** is loaded here too (for the ambient palette and the fallback).
- **Rating scale.** ScreenScraper stores its rating on a 0–20 scale; the hero presents it as the 0–10
  star score the screenshots show (e.g. 14/20 → "7.0").
- **Switching.** Controller/keyboard toggle via Start ▸ Menu ▸ "Spotlight view" / "Cover grid view"
  (toggles then closes the menu). A dedicated pointer/touch toolbar toggle and the full floating-
  toolbar chrome from the screenshots are deferred to a polish pass.

Deferred (tracked in `docs/couch-artwork-theming-and-spotlight.md`): sampling the ambient palette from
the fan art instead of the cover; the list's favourites hearts/filter; the screenshots' left-edge
button-hint column and floating icon toolbar; the couch-side ambient toggle. Desktop is untouched.

## 2026-08-05 — Spotlight hero restructure: full-bleed fan-art backdrop, game logo, canonical titles

Phase A of making the couch spotlight look finished (screenshots in the plan doc). Three
changes, all reusing the loaders already built for the first slice:

- **Fan art is the full-window backdrop.** It now fills the whole content area behind the
  floating list and the hero text, instead of being boxed in a right-hand card. Two scrims
  (bottom + left) keep the title/actions and list legible over any art; the list panel is
  translucent so the backdrop shows through.
- **No-fan-art fallback is a themed gradient**, not the cover (user choice): a dark base
  (`EmuLibraryBrush`) plus an `EmuAccentBrush` glow faded by an opacity mask, so an art-less
  game still recolours with the ambient palette. Built from the ambient *brushes* because the
  palette swap exposes brushes, not colours — a gradient of `DynamicResource` colours was not
  available without touching every theme file.
- **Game logo** (`GameMediaKind.Wheel`) renders above the title; when a game has no logo,
  nothing shows there (the title text is directly below, so no placeholder is needed).
- **Canonical titles.** The list and hero show the scraped ScreenScraper name instead of the
  filename. This is spotlight-only and non-destructive: a new bulk
  `IGameMetadataStore.GetProviderTitles()` (default-interface method so the 4 test stubs need
  no change; real SQLite override reads `GameMetadataValues` Field=Title in one query,
  preferring the neutral locale) fills `GameViewModel.SpotlightDisplayTitle` at scope build.
  `Game.Title`, the cover grid, and the desktop keep the original title.

Per-hero art (fan art + logo) is loaded generation-guarded so a fast scroll never leaks a
bitmap onto a stale view model, and only the current hero holds decoded bitmaps.

**Dropped from the original mockups (user, 2026-08-05):** the left-edge button-hint column and
the floating icon toolbar. Couch navigation is unchanged (top platform rail, Start ▸ Menu). The
rating scale mapping (ScreenScraper /20 → /10) is unchanged from the first slice.
## 2026-08-05 — ScreenScraper `UnsupportedFormat` falls back to title search, not a dead end

Formats with no whole-file hash rule (arcade sets, 3DS, disc-id systems like Dreamcast/PS3, or a
stray container under an otherwise hashable system) return `ScreenScraperPreviewStatus.UnsupportedFormat`
from the preview. The single-game scraper (`GameScraperViewModel.MapFailureState`) used to route that
to the read-only `Unsupported` message — a dead end — even though the fingerprint-profile comments
already promised these systems "fall back to title search." It now routes `UnsupportedFormat` to
`NoMatch`, which auto-searches by cleaned title so the user can pick a match; `UnsupportedSystem`
(platform not mapped to ScreenScraper at all, so nothing to search within) stays the dead-end message.
The batch scraper keeps treating both as `Unsupported`: it can't interactively confirm a title-search
guess, and auto-applying one risks mismatching rom hacks to their originals.

## 2026-08-05 — Arcade matches ScreenScraper by ROM file name (`romnom`)

Arcade sets have no whole-file hash (a repacked FBNeo/MAME archive isn't byte-stable), so they used
to reach only the title-search fallback. But the set's file name (e.g. `tmnt.zip`) *is* the identity
ScreenScraper indexes for arcade — the same role a disc serial plays for PlayStation. So arcade now
takes a dedicated file-name route in `ScreenScraperPreviewService` (`FileNameMatchSystems`): a single
`romnom`-only lookup, no file read and no fingerprint-consent gate, recorded as
`GameProviderMatchMethod.FileName`. An unknown or renamed set returns `NotFound`, which still falls
through to the title search.

File-name-only matching is opt-in per request (`ScreenScraperGameRequest.AllowFileNameMatch`) and
enabled only for arcade. Console systems are deliberately excluded: there the file name is an
arbitrary, hack-prone label, and the client already rejects a provider result that matched by name
only when a hash was queried (`ReturnedRomMatchesRequestedHash`). The batch scraper gets arcade
matching for free — `romnom` is a single deterministic request, not the multi-request title search it
still refuses to run.

## 2026-08-05 — Navigation motion: gamepad repeat accelerates; nav moments animate via mounted layers

Inspired by comparing against the neostation frontend, whose smoothness comes from an accelerating
d-pad auto-repeat plus in-place animated state changes (never route transitions, never hard cuts).

- **Gamepad auto-repeat now accelerates.** `GamepadNavigationController` held-direction repeats used
  to fire at a fixed 110 ms after a 400 ms initial delay. They now start at 90 ms after a 320 ms delay
  and ramp down to a 38 ms floor over `_rampRepeats` steps, so a long hold glides toward the target
  instead of crawling. Letter/page-jump escalation was deliberately left out of this slice.
- **Navigation surfaces animate by staying mounted + driving `Opacity`/`RenderTransform`,** not by
  toggling `IsVisible` (which can't animate — a collapsed element isn't rendered). New motion, all on
  composited properties at 130–280 ms with `CubicEaseOut`, matching the existing cover/toast layer:
  the gamepad overlay fades its scrim and lifts the sheet from scale(0.97); the focused couch cover
  scales to 1.045 (the focus *ring* stays instant — it tracks the d-pad); the library grid dips out
  and eases back on `IsLibraryLoading`.
- **The focused-tile scale is applied to the whole cover *stack*** (`Panel.gamepad-cover-stack`), not
  the cover Border alone. The focus frame is a concentric accent pad *behind* the cover (radius 18, 6px
  larger) masked to an even 6px border by the opaque cover (radius 12); scaling only the cover grows it
  into the fixed pad and clips the frame at the corners. Scaling the stack keeps ring + well + cover
  concentric.
- **The overlay sheet animates in but snaps out** (its `Transitions` live in the `.overlay-open` state,
  not the base). On close the sheet's size/alignment class drops and it reverts to the default centred
  620px card; with a symmetric fade that reverted card flashed for a frame ("a second menu"). Snapping
  opacity to 0 hides the revert; the scrim keeps fading out, so the close still reads as a soft dissolve.
- **Cached scope switches intentionally stay instant.** The library crossfade keys off
  `IsLibraryLoading`, which a cached scope sets true→false synchronously (never rendered), so only the
  slower uncached loads fade. No frame is deferred to force a fade on the fast path.
- **The spotlight backdrop uses a `TransitioningContentControl` + `CrossFade`** over an always-present
  gradient base, so fan art dissolves between focused games (and when a late-decoded bitmap arrives)
  instead of hard-cutting; the gradient shows through no-art games and mid-fade.
- **The toolbar search animates its width open** rather than cross-fading in place: its column is
  `Auto`, so mounting both the icon and the box would permanently shrink the `*` title column. The box
  eases width 0→218 and the trigger hides, preserving the collapsed layout.
- **The focused couch tile carries no shadow or glow — only the solid accent ring and the scale lift.**
  Once the tile scales up, the cover's drop shadow and the ring's `EmuFocusGlow` both spilled a halo
  past the 6px ring frame, which read as a "weird shadow." The focused cover's `BoxShadow` is set to
  `none` and the library tile ring drops its inline `EmuFocusGlow`; unfocused tiles keep their shadow,
  and the spotlight Play/Achievements rings keep their glow (they sit over dark art where it reads).
## 2026-08-05 — Spotlight: themed list surface + a self-scrolling marquee title

Two couch-mode refinements from testing:

- **The spotlight game list follows the theme.** Its panel was a fixed dark wash (`#BF15171E`)
  with hardcoded white text, so it ignored the palette (and was illegible on a light theme). It now
  uses the themed surface/text brushes (`EmuPopoverBrush`, `EmuTextPrimaryBrush`, `EmuBorderBrush`,
  `EmuHoverBrush`), so it recolours with the chosen theme and the artwork-ambient palette like the
  rest of the shell. This trades the faint see-through-to-fanart effect for legible, on-theme colour;
  a themed *translucent* surface would need a new palette token and can be revisited.

- **Long hero titles scroll instead of wrapping.** New `MarqueeTextBlock` control
  (`src/EmuShelf.App/Controls/`) shows the spotlight title on one line and, only when it is wider than
  its slot, runs a gentle there-and-back scroll with a pause at each end (a `TranslateTransform`
  animation on the inner text; distance/duration computed from the measured overflow). Titles that fit
  stay static. It's a `Decorator` hosting one `TextBlock`; font family is inherited from the Gamepad
  shell (Exo 2) and size/weight/foreground are forwarded. The hero title stack was switched to stretch
  so the marquee gets the full hero width to measure overflow against. `IsOverflowing` is exposed for a
  headless test of the fits-vs-scrolls decision.
## 2026-08-05 — Multiple emulators per console via per-system "profiles" (PS1 gets RetroArch)

EmuShelf assumed one emulator per system everywhere: launch picked
`emulators.FirstOrDefault(e => e.Supports(systemId))`, `EmulatorConfigs` was keyed by `SystemId`
alone, and the save/texture registries hard-wired one emulator per system. To let a console offer
alternatives (the first being PlayStation on RetroArch alongside DuckStation), a per-system **active
emulator profile** was introduced.

- **Data model.** A "profile" is a `(SystemId, EmulatorId)` pairing that owns its own launch
  arguments, installation, and core. Schema **v16** repoints the `EmulatorConfigs` primary key from
  `SystemId` to `(SystemId, EmulatorId)` and adds `SystemEmulatorSelection(SystemId PK, EmulatorId)`
  for the active pointer. The migration keeps every existing single-row config and points the
  selection at its current emulator, so DuckStation/PCSX2/Dolphin/RetroArch setups are untouched and
  PS1 stays on DuckStation until the user changes it. v16 is self-healing (`CREATE TABLE IF NOT
  EXISTS EmulatorConfigs`) so a DB synthesised at v9+ (where v8 is skipped) still migrates.

- **Resolution seam.** `IEmulatorConfigurationStore.Get(systemId)` now returns the *active* profile's
  config (it carries `EmulatorId`). `EmulatorLaunchService` derives the emulator from
  `config.EmulatorId`, falling back to the first supporting emulator when there is no usable
  selection — so a system that was never given an explicit profile (and every launch-service test)
  behaves exactly as before. Save-sync and texture resolution already flowed through `Get`, so they
  automatically pick up the active installation/core; they additionally key on `EmulatorId`.

- **RetroArch on PS1.** `"playstation"` was added to `RetroArchDefinition.SupportedSystemIds`. It
  reuses the shared RetroArch installation (`SharesDefaultInstallation`) and the existing
  `-L {CorePath} {GamePath}` template; only the core (Beetle PSX / SwanStation / PCSX ReARMed) is
  PS1-specific. Those core ids were added to `RetroArchCore.KnownCoreNames` so save-folder-by-core
  resolves without the info file. RetroAchievements needs no change — identification is per-system
  and emulator-agnostic, so a PS1 disc hashes identically under either emulator.

- **Saves follow the active profile.** The single PlayStation save descriptor now branches on
  `SaveProviderContext.ActiveEmulatorId`: DuckStation memory cards by default, the generic
  `RetroArchSaveLocationProvider` (battery saves + guarded states) when RetroArch is active. The
  registry stays one-descriptor-per-system, so the Saves section keeps one row per console.

- **UI.** Each Settings → Emulators row gains an emulator picker, shown only when a system has more
  than one supported emulator (`HasMultipleProfiles`). The row caches one editable draft per emulator
  so switching the picker never loses the other profile's edits, and Save persists every configured
  profile plus `SetActiveEmulator`. The **Emulators section stays Desktop-only** — the Gamepad
  settings projection already excludes it — so profile *selection* is Desktop-only for now, while the
  Gamepad Saves/Texture sections inherit active-profile behaviour through the shared model with no
  change. Surfacing the picker in Gamepad mode is deferred.

- **Scope.** This is the incremental "profile model + resolution seam" pass with DuckStation +
  RetroArch(PS1) as the reference conversion; the other emulators keep their existing registration.
  The follow-up full-unification path (one cohesive per-emulator profile owning launch/saves/states/
  textures/config, replacing the scattered registries) is written up in
  `docs/emulator-profiles-refactor.md`. Textures for PS1 stay DuckStation-only: when RetroArch is the
  active PS1 profile, `TexturePackCoordinator` sits the texture row out. This was verified against the
  libretro/DuckStation docs (Aug 2026): among RetroArch PS1 cores only Beetle PSX HW (Vulkan) supports
  texture replacement (SwanStation and PCSX ReARMed do not), it stores packs next to the ROM
  (`<game_filename>-texture-replacements/`) rather than in an emulator-owned folder, and its
  filename-keyed format is not interchangeable with DuckStation's serial-keyed packs — so a Beetle
  provider would be a separate per-game sibling-folder inventory, not a reuse of the DuckStation
  adapter. Deferred, not blocked; see the refactor doc.

## 2026-08-05 — App version comes from the git tag at build time; Settings → About shows version + commit

The displayed version was stuck at the hardcoded `<Version>0.1.0</Version>` in
`src/EmuShelf.App/EmuShelf.App.csproj` while GitHub releases had moved to `v1.0.8`, so the app and
the repo disagreed and `--version` only printed `EmuShelf`.

- **`git describe` (nearest `vX.Y.Z` tag) is the single source of truth for the version.** A
  `StampGitVersion` MSBuild target (runs `BeforeTargets="GetAssemblyVersion"`) reads the newest tag
  and sets `Version` from it, so tagging a release on GitHub is the only place the number lives — no
  separate csproj bump. The csproj `<Version>` is now only a fallback for tag-less/git-less builds
  (source tarballs). The target is best-effort: missing git, tags, or `.git` never fails the build.
- **The exact commit is pinned into the assembly**, not just the version: the short hash is appended
  to `AssemblyInformationalVersion` (`1.0.8+3f2383650`) and both the hash and the ISO commit date go
  in as `AssemblyMetadata`. `AppBuildInfo` reads these back by reflection at runtime (no process/file
  access), feeding Settings → About and `--version`.
- **CI checkout switched to `fetch-depth: 0`** in `.github/workflows/build.yml` (all four build/
  package jobs). The default shallow clone fetches no tags, so `git describe` would otherwise find
  nothing and every release binary would fall back to the csproj version.
- **About is a Desktop-only settings section.** It renders a read-only info card, but the Gamepad
  settings shell only projects interactive *row* sections, so About is filtered out of the derived
  gamepad list alongside the existing Desktop-only `Emulators` and gallery-page `Themes`. About is
  otherwise always present (it needs no host context) and sits at the permanent tail of the list.
- **Caveat:** incremental local builds may show a stale commit if the assembly-info generation is
  skipped as up-to-date; clean/Release/CI builds (which the shipped binaries use) always re-stamp.

## 2026-08-05 — Spotlight crash: no cross-fade over disposed fan-art bitmaps

Selecting the couch spotlight crashed the macOS build (SIGABRT in SkiaSharp on the render thread).
Cause: the spotlight backdrop was switched to a `TransitioningContentControl` with a `CrossFade`
(motion-polish pass) whose `Content` binds to `FocusedGame.FanartImage` — a `Bitmap`. But the
spotlight deliberately **disposes** the focused game's fan-art bitmap as focus moves (`OnFocusedGame
Changed` → `oldValue.FanartImage = null` → `Dispose`) so a long list never accumulates ~1080p images.
The cross-fade keeps the *outgoing* bitmap rendered for its 280 ms fade, so it draws a disposed
`Bitmap` and the render thread aborts.

Reverted the backdrop to a plain `Image` (instant swap, as before). The cross-fade and the eager
per-focus disposal are fundamentally at odds; bringing the cross-fade back needs deferred disposal
(release the outgoing bitmap only after the transition completes, or retain the last N), not a bind to
the disposable bitmap. The rest of the motion-polish pass (grid dissolve, overlay fade, focused-tile
lift, search expand, accelerating repeat) is unaffected and kept.
## 2026-08-05 — macOS data lives in Application Support, not beside the executable

On macOS the app runs from a `.app` bundle whose executable sits at `Contents/MacOS/`, so the
Windows/Linux "portable, beside the executable" rule would bury `Data/Covers/Cache/Logs/Settings/Saves`
*inside* the bundle. That is hidden from Finder, wiped whenever the user drags a new build over the old
one, and unwritable once Gatekeeper translocates a quarantined bundle to a read-only mount.
`AppPaths.ResolveBaseDirectory()` (renamed from `ResolvePortableBaseDirectory`) therefore returns
`~/Library/Application Support/EmuShelf` on macOS, keeping AppImage and Windows/Linux portable behavior
unchanged. The home directory is used directly because `SpecialFolder.ApplicationData` maps to `~/.config`
on .NET for macOS, not the Cocoa location. This is the "non-portable data location" the 2026-07-12 macOS
entry deferred. A "Open data folder" button in Settings → General reveals this root in the file manager
(threaded via `LibraryMaintenanceActions.DataDirectory`), so the location is discoverable on every OS.

## 2026-08-05 — In-app auto-update from GitHub Releases (hand-rolled, not a framework)

EmuShelf now checks its GitHub Releases on launch (throttled, opt-out) and can download, verify, and
install a newer build in place, then relaunch — surfaced as a banner plus Settings → About
(Desktop) and Settings → General (Gamepad). See `docs/auto-update.md`.

**Hand-rolled over Velopack/NetSparkle.** CI already publishes the update source: portable per-platform
artifacts + `.sha256` on every `vX.Y.Z` tag, and the app already knows its version from the tag.
Velopack insists on owning packaging (it would replace the working zip/AppImage/.app pipeline and
fights the portable "data beside the executable" model); NetSparkle gives an appcast + UI but still
leaves the portable in-place swap and the gaming-mode restart to us. Both fights were avoidable, so a
small `IUpdateService` (Core interface) + `GitHubUpdateService` (Infrastructure) + per-platform
`IUpdateApplier` reuses the existing artifacts as-is with no packaging change.

**The download is always SHA-256 verified against the release's own checksum file before use** — a
mismatch deletes the file and aborts. The check hits only the public API; no token, nothing about the
user is sent.

**Gaming mode is never left, per platform.** A process can't hot-swap its own code, so a restart is
unavoidable — but "stay in gaming mode / never hit the desktop" is met:

- **SteamOS/Linux:** the build is a single AppImage. The applier replaces the file and `execv()`s the
  same path, keeping the *same PID*, so a non-Steam shortcut's tracked process never exits and Steam
  never sees the game stop. No Steam wrapper script needed — this is why the AppImage single-file model
  was worth preserving.
- **Windows/macOS:** a short-lived helper waits for exit, swaps files, and relaunches. Windows overlays
  only program files so portable user data survives; macOS swaps the whole bundle (data lives in
  Application Support) and clears `com.apple.quarantine` on the downloaded, un-notarized bundle so
  Gatekeeper allows the relaunch.

**Deferred:** delta updates, macOS notarization, Windows code-signing. Documented in
`docs/auto-update.md`.
## 2026-08-05 — Gamepad grid: a fast held Up/Down glides into not-yet-realized rows instead of snapping

The position-relative ease (entry above) already made a held d-pad a continuous scroll — but only while the
*target* row was realized: `RevealFocusedGame`'s ease branch was gated on
`ContainerFromIndex(rowIndex) is { } easeRow`. A held Down auto-repeats ~one row per 110ms
(`GamepadNavigationController`), while the ease closes 28% of the remaining distance per frame and takes
~300ms to settle one row, so **focus outruns the glide**: after a couple of repeats the target row sits
below the realized set, the ease gate fails, and the reveal fell through to the `ScrollIntoView` snap — a
hard jump that broke the glide. That intermittent snap mid-hold was the residual "Up/Down isn't as smooth
as Left/Right" (Left/Right moves inside one row and never scrolls, so it can't exhibit this).

Fix: when a **near** step (≤ `GamepadMaxEaseRowStep`) has an unrealized target, keep easing instead of
snapping. The target is computed **position-relative to the still-realized PREVIOUS row**: centre prev
(`Offset + prevDelta`) and shift by `(rowIndex − prev) × prevRow.Bounds.Height`. Rows are uniform per view
(the invariant the whole centring relies on), so prev's own container height *is* the row stride, and the
result lands the not-yet-realized row on the centre line. Because the eased offset flows **continuously**
into the adjacent region — the panel realizes each row as the offset enters it — it never teleports far, so
it cannot desync the `VirtualizingStackPanel`'s estimated extent the way the reverted absolute
`rowIndex × rowHeight` write did (the 2026-08-04 revert). At steady state during a fast hold the offset
trails focus by ~half a row (28%/frame vs. one row/6.6 frames), so the focused tile rides slightly below
centre while held and re-centres on release — the couch-UI momentum feel, and it never walks off-screen.
Covers are already pre-warmed 3 rows ahead (`PrefetchCoversAroundFocus`), so the row the glide uncovers is
painted, not a blank pop. Only the realized-target read is the fast path (most accurate); the stride
fallback engages solely when focus has outrun realization; everything that is not a near step still snaps.

Guarded by `FastDownBurst_GlidesIntoUnrealizedRows_AndSettlesCentered` (a 24-row Down burst pumping only
`Render`, so focus lands on unrealized rows): the focused tile stays realized through the burst and settles
on the centre line. The eight existing `GamepadGridSelectorTests` (incl. the snes short-cover deep-list
case that a re-entrant-layout misimplementation stack-overflowed) stay green. **Caveat:** the previous two
eased designs shipped headless-green but corrupted on the real Steam Deck compositor (phantom extent,
selector off-screen) — that class of failure is real-compositor-only, so this still needs on-device
verification of a fast held Up/Down deep in a long single-platform (short-cover snes) and mixed All-Games
library before it is trusted.
## 2026-08-05 — Marquee scroll via a timer, not Avalonia's Animation API

The spotlight hero's `MarqueeTextBlock` drove its scroll with `Animation.RunAsync(_transform, …)` where
`_transform` is a bare `TranslateTransform`. Avalonia's transform animator casts the animated object to
`Visual`, so this threw `InvalidCastException` synchronously during the arrange pass — crashing the
whole app the instant a title was long enough to actually scroll. It slipped through because the unit
test arranges the control detached (never starts the scroll) and short titles never overflow, so it
only surfaced on a Steam Deck with a long arcade title.

Rewrote the scroll to a plain `DispatcherTimer` that writes `TranslateTransform.X` directly each frame
(there-and-back with end pauses, smoothstep easing) — no animator, no cast, and it can never re-enter
layout. Added a headless test that shows an overflowing marquee in a window and asserts the scroll
starts without throwing (the path the crash lived on). The correct Animation-API route would have been
to animate the child Visual's `RenderTransform` with `TransformOperations`, but the timer is simpler and
gives full control over the cadence.
## 2026-08-05 — Gamepad grid: gentler vertical auto-repeat floor than horizontal

The accelerating auto-repeat (90 → 38 ms floor) felt great moving Left/Right in the couch grid but
janky moving Up/Down — rows "blinking and jumping" on Steam Deck. Cause: Left/Right steps within a row
(no scroll), but each Up/Down step scrolls a whole row and drives the centre-reveal, which falls back
to a hard `ScrollIntoView` snap whenever the target row hasn't virtualized yet. At the 38 ms floor the
vertical hold outruns row virtualization, so the snap path dominates and covers pop in/out.

`GamepadNavigationController` now ramps vertical (Up/Down) to a gentler `verticalMinRepeatIntervalMs`
(72 ms) while horizontal keeps the fast 38 ms floor, so the reveal keeps up and the grid glides both
ways. Per-axis floor, unit-tested. The value is tunable; the reveal logic itself is untouched.

## 2026-08-05 — Opening Settings reads every system's remembered folders in one connection

A follow-up to the earlier `IEmulatorConfigurationStore.GetAll` batching (which killed the
per-system config connection opens on the settings-open path). That fix missed a second per-system
database read on the same path: `EmulatorSettingsRowViewModel`'s constructor calls
`GetLibraryFolders(systemId)` once per system, and connection pooling is deliberately off, so every
call is a fresh open of `library.db` — N sequential opens on the UI thread while the panel builds,
worst cold and on a portable/external drive. The remaining "significant delay before Settings opens."

`LibraryFolderManagementActions` gained a batched `GetAll`, and both open paths now read every
system's folders once, off the UI thread, in the same `Task.Run` that already batched configs and
profiles. The grouped result seeds each row (an empty seed for a system with no folders still counts,
so a zero-folder system never reopens the database either). A null map — a caller that supplies only
the per-system `Get`, i.e. tests — preserves the old per-row read. Later refreshes after a folder
edit still read the current rows directly.

## 2026-08-06 — macOS is a shipped target, not just a dev platform (supersedes 2026-07-12 Windows-only)

The 2026-07-12 decision framed macOS as a first-class *dev* platform with a Windows-only v1 release
and a "deferred" macOS package. That deferral is now retired: macOS is a supported, released target
alongside Windows and the Linux/AppImage build. Most of this was already true in the code and CI and
had simply outrun the decision log — this entry makes the policy match reality and removes the
"Windows-only ship" framing that later work (`.app` packaging, the Application Support data root, the
macOS in-app updater) had already contradicted.

What backs the promotion, all pre-existing:

- **CI builds and tests on macOS every run** (the `build` matrix includes `macos-latest`), and the
  dedicated `package-macos` job publishes `osx-arm64`, bundles rclone, assembles `EmuShelf.app`
  (`packaging/macos/build-macos-app.sh` + `Info.plist`), verifies the payload, and uploads a
  checksummed zip. The `release` job attaches that artifact to the GitHub Release on a version tag.
- **Launching** resolves a selected `.app` bundle to its inner Mach-O binary via `Info.plist`'s
  `CFBundleExecutable` (`EmulatorLaunchService.ResolveExecutablePath`), so DuckStation/PCSX2/RPCS3/
  Dolphin/PPSSPP/RetroArch/Azahar `.app`s launch with the same per-emulator argument templates.
- **Data + saves + updates** are macOS-aware: app data lives under `~/Library/Application Support/
  EmuShelf` (2026-08-05), every save-location/texture resolver has a verified `~/Library/Application
  Support/<Emulator>` branch, and `MacUpdateApplier` swaps the whole bundle and clears
  `com.apple.quarantine` on relaunch.

Known, accepted caveats (documented, not blockers):

- **Unsigned / un-notarized, arm64 only.** The bundle carries only the .NET apphost's ad-hoc
  signature. Gatekeeper therefore needs the quarantine flag cleared on a fresh download
  (`xattr -dr com.apple.quarantine EmuShelf.app`, or right-click → Open). A Developer ID signature +
  notarization and an Intel/universal slice remain future work, not release gates.
- **On-screen keyboard** falls back to the hardware keyboard on macOS (Windows-only TabTip/osk today).

Also fixed here: **fullscreen was broken on macOS** for EmuShelf's borderless window
(`WindowDecorations="None"` + extended client area). Avalonia's native `WindowState.FullScreen` is a
no-op for such a window on macOS — verified by running the app and probing the live NSWindow: the
managed state reads `FullScreen` while the window stays at its floating 1240×800 size, so a Gamepad
launch (and any fullscreen request) opened as a small window. Driving AppKit's `toggleFullScreen:`
directly left the content unresized inside a fullscreen shell, and `Maximized` mispositioned the
window (the known ExtendClientArea offset, AvaloniaUI/Avalonia#15956); only sizing the window
ourselves produced a clean fill.

New `MacFullScreenController` (App layer, macOS-gated, inert elsewhere) observes `WindowState` and
mirrors any `FullScreen` onto a manual borderless fill of the window's display, restoring the floating
size on exit. One observer covers every fullscreen path because they all set `WindowState.FullScreen`:
launching into Gamepad, mode switches, returning from a game, and the new desktop keyboard toggle. The
window fills everything below the menu bar (macOS keeps a normal-level window out from under it; true
menu-bar-hiding fullscreen needs a native fullscreen space, which AppKit will not grant a borderless
window — an accepted cosmetic limitation). Windows/Linux are untouched, where native `FullScreen`
works. Verified live on macOS: the Gamepad window now measures 1470×923 filling the display.

Separately, because the borderless window has no title bar (no green fullscreen button or menu item),
Desktop mode had no way to fill the screen at all — a keyboard toggle now covers it: **F11**
(cross-platform) and **Cmd+Ctrl+F** (the macOS system standard), routed through the same
`WindowState.FullScreen` the controller acts on.

## 2026-08-06 — One path-identity comparer: macOS is case-insensitive like Windows

Path identity was compared inconsistently. The `Games.Path` database index is `COLLATE NOCASE`
(2026-07-12: NTFS and macOS APFS/HFS+ are case-insensitive, so `game.cue` and `GAME.CUE` are one
file), but ten in-memory comparers across App/Infrastructure/Integrations each special-cased *only*
Windows — `OperatingSystem.IsWindows() ? OrdinalIgnoreCase : Ordinal` — which quietly treated macOS
as case-sensitive. On the default case-insensitive Mac volume that disagreed with the database:
differently-cased references to one on-disk file were deduped as one row by SQLite but kept apart by
the library, save-sync path matching, the ScreenScraper fingerprint cache, and Dolphin's save
enumeration.

Introduced `EmuShelf.Core.Storage.FilePathComparison` (`IsCaseInsensitive`/`Comparison`/`Comparer`),
case-insensitive on Windows **or** macOS and ordinal on Linux, and routed all ten sites through it so
the rule can't drift per call site again. Non-path dictionaries keep their own comparer (Flatpak app
ids stay `Ordinal` — reverse-DNS, case-sensitive). Windows and Linux behavior is unchanged; only
macOS flips, to agree with both its filesystem and the database. The database collation is
case-insensitive on Linux too (a pre-existing, lower-stakes mismatch, since Linux filesystems are
case-sensitive); reconciling that would require a platform-dependent schema collation and is left out
of scope. Behavioral guard in `FilePathComparisonTests`; full suite green on macOS.

## 2026-08-06 — macOS: native open panel to pick a `.app`, and correct core discovery

Surfaced by driving the real app on macOS: the whole "add a RetroArch emulator and launch a game"
flow was broken, in two independent places.

**Selecting an emulator.** Every macOS emulator ships as a `.app` bundle, and Avalonia's
cross-platform `StorageProvider` cannot select one — its open panel treats the bundle as a package to
navigate into, so a file picker returns nothing and a folder picker cannot select it either (both
confirmed by logging the picker result: `count=0` / `<null>`). No `FilePickerFileType` /
`AppleUniformTypeIdentifiers` combination fixed it. `PickEmulatorExecutableAsync` now calls a native
`NSOpenPanel` on macOS (`MacOpenPanel`, libobjc message-send) configured with
`treatsFilePackagesAsDirectories = NO` + `canChooseFiles = YES`, which makes a `.app` selectable as a
single item; it falls back to the Avalonia picker if the native call is unavailable. Windows/Linux are
untouched. Verified live: `/Applications/RetroArch.app` selected, stored as a portable relative path,
resolved to `Contents/MacOS/RetroArch`, and a GBA game launched through RetroArch (exit 0) with the
minimize/restore lifecycle intact.

**Finding cores.** `EmulatorSettingsRowViewModel.CoreSearchDirectories` scanned only *beside the
executable* and the Linux XDG path `~/.config/retroarch/cores` — never the macOS location. macOS
RetroArch keeps downloaded cores under `~/Library/Application Support/RetroArch/cores` (the same root
its config/saves use), so the core picker was always empty on macOS even after cores were installed
— the same class of bug the 2026-07 review fixed for PPSSPP's Memory Stick. Added the Application
Support branch (macOS returns it and stops, mirroring the Windows/Linux structure). The settings hint
that claimed cores live "beside the configured RetroArch executable" was corrected to be
location-neutral. EmuShelf still never downloads or edits cores — it only lists what RetroArch has
installed. Tests: the XDG discovery test is now Linux-only (`&& !IsMacOS`), plus a new
Application-Support discovery test.

## 2026-08-06 — Spotlight polish: translucent list, floating depth, and a view-mode picker

Three couch-UI refinements toward the OpenEmu-style reference, driven by the design comparison.

**Translucent game list.** The spotlight list was a fully opaque `EmuPopoverBrush` fill, so the
fan-art backdrop never showed through. Opacity can't go on the list container (it would fade the game
titles too), so the fill now lives on its own background layer (`Opacity=0.72`) behind a transparent
`ListBox`; the titles stay fully opaque and the backdrop's existing left scrim keeps them legible. It
is palette-agnostic — no per-theme brush edits.

**Floating depth.** The list and the hero pills sat flat on the busy backdrop. The list is now a
floating card: the translucent fill layer itself carries the hairline stroke and the drop shadow
(`0 16 36`), so the shadow is cast by a filled element (it renders regardless of the container having a
background, unlike a shadow on a transparent wrapper) and nothing needs a clip — the list items are
inset well inside the rounded corners. The `spotlight-pill` style gains a `0 6 16` shadow so the
rating/achievements/Play chips lift off the art.

**View-mode picker relocated.** The grid↔spotlight toggle was a single self-relabelling entry buried in
the system-menu option list. It is now a two-tile "View mode" selector (Grid / List) at the top of the
menu, styled like the theme picker (active tile filled with the accent wash). Controller model: the
tiles are a focus row above the option list — D-pad Up from the top option lands the ring on the row,
Left/Right pick Grid/List and apply live, A is inert there (the choice is already applied), and Down
drops back into the options. Pointer users click either tile. `IsGamepadViewModeRowFocused` drives the
row ring and suppresses the option ring while it's active.

## 2026-08-06 — Desktop rubber-band (marquee) multi-select

M25 gave the desktop library a full keyboard-driven multi-select (Ctrl/Cmd-click, Shift-range,
Ctrl/Cmd+A). It had no mouse-only way to grab a group, so a left-drag from the library's empty
canvas now paints a selection box that claims every cover it touches — the classic file-manager
gesture — in both the grid and list layouts.

**Where the logic lives.** Geometry is inherently view work, so the box, the drag threshold, and the
tile hit-testing sit in `MainWindow.axaml.cs` (window-level tunnel pointer handlers, matching the
existing pointer-tunnel selection). The *selection state* stays in the view model behind three
methods — `BeginMarqueeSelection(additive)`, `UpdateMarqueeSelection(realized, inBox)`,
`EndMarqueeSelection()` — so grid and list keep sharing one `IsSelected`/`SelectedGame` model and the
behavior is unit-testable without a window.

**Realized-tiles-only, content-anchored top, drag-edge auto-scroll.** The box is drawn and hit-tested
in `LibraryContentPanel` (viewport) coordinates, and only *realized* (on-screen) tiles are enumerated
each move — the view model therefore never touches off-screen games, so a game the box already claimed
keeps its selection when it scrolls out. When the pointer enters the top/bottom margin of the viewport
a `DispatcherTimer` scrolls the active `ScrollViewer` (the grid's own, or the one the ListBox
templates in) at a depth-ramped speed and re-hit-tests each tick. For that to *extend* the selection
rather than shed scrolled-past claims, the box's **top edge is anchored in content space**: it is
offset by how far the view has scrolled since the drag began (`_marqueeOriginScrollOffset`), while the
bottom edge tracks the pointer — so revealed rows fall inside the growing box and off-screen rows keep
their state. Only the vertical axis is adjusted (these layouts never scroll horizontally). The
velocity ramp is a pure static method (`ComputeAutoScrollVelocity`) so the edge-zone math is unit
tested without a window. Ctrl/Cmd+drag still extends additively and Ctrl/Cmd+A still grabs everything.

**Deferred clear + threshold, mouse only.** A press on the empty canvas only *arms* the marquee; it
begins on the first drag past a 4px threshold (Ctrl/Cmd makes it additive to the pre-drag selection).
A press that never drags falls back to the historical empty-canvas behavior — clear the selection on
release. Arming is gated to `PointerType.Mouse` so a touch/pen drag on the canvas stays a pan/scroll.

**`IsLibrarySurface` now counts the content panel.** The grid `ScrollViewer`/`ItemsRepeater` are
hit-test transparent in the gaps between covers, so a press there falls through to
`LibraryContentPanel`'s brush. The surface test now accepts the panel *itself* (but not its
descendants, which would swallow toast/banner clicks), which both starts the rubber-band from grid
gaps and fixes empty-gap clicks never clearing the grid selection.
## 2026-08-06 — Google Drive uses the embedded OAuth client only; the "import client JSON" flow is removed

The 2026-08-04 decision let a user import their own Google OAuth client JSON, which took precedence over
the client embedded in the build. That import path had a latent trap: the client **id** was persisted to
settings and reloaded on the next launch, but the **secret** was intentionally never persisted (it lives
only in rclone's config). After any restart, Connect therefore sent the prefilled id with a null secret,
which `RcloneConfigurator` rejected (`A Google client id also needs its client secret`) before rclone ever
ran — surfaced to the user as the misleading "The Google sign-in may have been declined." A connected user
could not reconnect without re-importing the JSON every session, with nothing in the UI saying so.

Resolution: **EmuShelf ships one application-identity Google OAuth client baked into the build, exactly like
its ScreenScraper devid, and there is no in-app way to supply a different one.** This is how a normal app
ships OAuth access. Removed: the "Import client JSON…" button (Desktop and Gamepad), `CloudClientId` /
`CloudClientStatusText` / the in-memory secret and `ImportGoogleClientCommand` on the settings view model,
`IDialogService.PickGoogleClientJsonAsync`, `GoogleOAuthClientFile`, and the `GoogleClientId` settings field.
`ConnectGoogleDriveAsync` / `CreateGoogleDriveRemoteAsync` no longer take a client id/secret;
`ResolveGoogleClient` now returns the embedded client, or null so rclone falls back to its shared client on
an unconfigured local build. Dropping `GoogleClientId` from `CloudSaveSyncSettings` is forward-safe — an old
settings.json with the field simply deserializes it away.

Operational consequence: because the client is baked at **build time** from the `EMUSHELF_GOOGLE_OAUTH_CLIENT_ID`
/ `EMUSHELF_GOOGLE_OAUTH_CLIENT_SECRET` repository secrets, rotating the Google client (deleting it and
creating a new one in the Google Cloud console) requires updating those two secrets and producing a new
release build; the running app has no runtime credential input. A rotated client also invalidates the token
stored in an existing rclone remote, so users reconnect (Disconnect → Connect) once against the new build to
re-run OAuth.
## 2026-08-06 — Gamepad scraper: Apply-first focus and a scroll-fade cue

Two couch-UX fixes for the controller-native ScreenScraper overlay (`GamepadScraperViewModel`
+ the `IsGamepadScraperOpen` body in `MainWindow.axaml`), from live use on a pad.

**Apply is the default focus in the Ready review.** The D-pad ring used to open on the first
metadata field, so reaching the (already pinned, always-visible) Apply button meant pressing Down
through every field and media row. Since the scraper pre-selects sensible fields, the common path is
accept-all — so `GamepadScraperViewModel.DefaultFocusIndex()` now lands the ring on the Apply target
whenever the state is `Ready`/`Applying`; D-pad Up walks back into the fields to deselect. Every other
state keeps its first target (connect username, search query, first candidate). The apply command
itself is unchanged. Overlay tests that encoded "first field focused" were updated to the new default.

**A scroll fade tells you the field list continues.** The field list is a `gamepad-scroll`
`ScrollViewer` whose thin Fluent scrollbar is an overlay that only shows on pointer hover — invisible
on a pad — so the page read as "everything, then it suddenly jumps." Two view-only changes (in
`MainWindow.axaml.cs`): (1) an alpha-only `OpacityMask`
gradient fades whichever edge still has off-screen content (top/bottom/both/none), recomputed on the
scroller's `ScrollChanged` and again whenever focus is revealed (the ring opens on Apply, off the
list, so no scroll fires on open — without the reveal-time recompute the cue would miss the first
frame). Alpha-only keeps it
palette-agnostic — `EmuPopoverBrush` behind the list varies per theme and has no matching `Color`
resource, so a coloured gradient stop would need 28 palette edits. (2) `RevealScraperRowWithLookahead`
keeps ~40px of the neighbouring row peeking past the focused one, so the list is visibly mid-scroll
rather than static-then-jump; it falls back to `BringIntoView` for controls outside a gamepad scroll
region (the pinned Apply/Refresh block, connect form, terminal messages).

## 2026-08-07 — Gamepad platform rail is icon-only, active tab expands to its name

The controller-mode top rail (`MainWindow.axaml`) used to show `icon + name` for every
console tab, plus a text-only "All Games" pill. To declutter the couch view, every console tab is
now icon-only; the name reappears only on the **active** tab (bound to `IsActive` /
`IsAllGamesSelected`), so "you are here" stays legible without a wall of text. "All Games" — which has
no console badge — gets a 2×2 grid `PathIcon` glyph so the rail reads as one uniform row, and it too
expands to its label while selected. The dropped names are preserved for pointers and screen readers
via `ToolTip.Tip` + `AutomationProperties.Name`. Icon bumped 26→30px; vertical padding unchanged so
the tab stays 54px tall, inside the `MainWindowVisualSnapshotTests` 53–55px height guard. Console
identity also remains visible in the spotlight bar at the bottom, which names the focused game's system.

## 2026-08-07 — Gamepad rail selection is one sliding pill, translate-animated only

The controller rail's selection was reworked from a per-tab background (which read as the highlight
popping in place, and clipped when a `scale()` pop was tried) to a single `Border`
(`gamepad-platform-indicator`) that sits behind the tabs and is moved to overlay the active one, so a
platform switch reads as the highlight travelling left/right. The pill and the tabs share one `Panel`,
so it stays aligned regardless of how the centred ScrollViewer positions the row; `UpdateRailIndicator`
(code-behind, hooked to the existing rail-reveal + a `SizeChanged` snap) measures the active tab and
drives the pill's size and a translate transform. Only the **translate** eases — it is GPU-composited,
so the glide stays smooth even while the library relayouts on switch. **Width/Height are set instantly**
on purpose: animating a layout property runs a per-frame measure/arrange on the UI thread and stuttered.
The rail stays centred per user preference; the residual cost is that changing the selected tab's name
width shifts the row slightly on each switch (a stable alternative would reserve the name's width).
## 2026-08-06 — Google Drive connect clears leftover rclone and names the port-busy failure

rclone's `config create … drive` runs the OAuth flow by binding a loopback web server on
`127.0.0.1:53682`, opening the browser, and blocking until the user finishes signing in. If that is
abandoned — the app is closed (a Steam Deck force-quit) while the browser is open, or the browser
never appears (Gaming Mode/gamescope has no default browser) — the rclone process is orphaned and keeps
holding the port. Every later Connect then dies instantly with `bind: address already in use`, which the
UI reported as the generic "the sign-in may have been declined," and repeatedly clicking Connect only
spawned more contenders for the port. Observed in the field on a Steam Deck.

Three changes in `RcloneConfigurator`:

- **Clear our own leftovers before a sign-in.** `CreateGoogleDriveRemoteAsync` first kills any running
  instance of the *bundled* rclone, on a thread-pool thread. "Ours" is matched by executable path on
  Windows/macOS; on Linux the AppImage mounts at a fresh `$APPDIR` each launch, so a cross-session
  orphan's binary path no longer matches — there we also match on the `--config` argument (its
  `rclone.conf` lives in the portable data dir and is stable across launches), so a Steam Deck orphan is
  still reaped after a force-quit. An unrelated rclone the user runs, pointed at a different config, is
  never touched. Connect and sync are serialized by the coordinator's gate, so any of our rclone alive at
  connect time is an orphan, never a live transfer — making this safe.
- **Never orphan the process we spawn.** `RunAsync` kills the process (whole tree) in a `finally`, so a
  cancelled or abandoned OAuth run can't walk away still holding the port. A run that exited on its own is
  already gone, so this is a no-op for the normal path and for the short-lived `mkdir`/transport-adjacent
  calls.
- **Name the failure.** `DescribeFailure` (static/pure, unit-tested) maps `address already in use` to a
  dedicated `RcloneSignInServerBusyException`; the coordinator catches it and returns a new
  `CloudSaveSyncConnectResult.SignInServerBusy`, which Settings renders as "A previous Google sign-in is
  still open. Close that browser window (or restart EmuShelf), then try again," instead of the misleading
  declined-sign-in text.

## 2026-08-07 — Desktop Settings polish: busy-gated commit, no launch-target auto-reset, connect-first Saves

Polishing pass over the Desktop Settings window (`EmulatorSettingsWindow`). Three non-obvious choices:

- **Save/Cancel gate on an aggregate `IsBusy`, not just `IsWorking`.** `IsWorking` only covered save +
  library maintenance, so the global footer buttons stayed live during cloud sync, account connects,
  texture rescans, and the rclone download. Committing or closing mid-operation raced the in-flight
  task's own writes (e.g. `PersistCloudSaveLocations` running twice) and left it posting progress to a
  torn-down VM. `IsBusy = IsWorking || IsCloudBusy || IsRetroAchievementsBusy || IsScreenScraperBusy ||
  IsTexturePackBusy || IsDownloadingRclone` now gates both buttons and `SaveAsync`. The per-operation
  buttons were already correctly gated on their own busy flags; only the footer was not.
- **The launch-target combo no longer carries `SelectedIndex="0"`.** It also had a TwoWay
  `SelectedItem="{Binding TargetKind}"`; the literal index could win during initialization (the row is
  realized even while hidden on non-Linux), force "Direct", and — because the binding is TwoWay — write
  that back over a persisted "Flatpak" target, then cascade it onto shared installs. `TargetKind` always
  holds a valid value, so the index added only risk. Removed it; the binding is authoritative.
- **Saves leads with Connect.** The connect / connected-summary block moved above the per-platform folder
  list (the per-platform Replace actions are hidden until connected, so showing them first was inert
  detail before the one action that turns the feature on). Alongside: the window grew to 880×700
  (min 760×540) and the cramped four-button Saves/Textures rows now wrap their secondary actions onto a
  second line so the path field never collapses; the placeholder legend moved out of the always-on
  header to sit inline under the Emulators launch-arguments field, and the header subtitle now follows
  the selected section.

## 2026-08-07 — Desktop Settings Tier 2: card token, accent rule, and the deliberately-shared field id

- **One `Border.settings-card` token** (EmuCardBrush / EmuBorderBrush / 1px / CornerRadius 10 / Padding 18)
  replaces the per-card inline styling across every section, so padding (was 16 vs 18) and radius (was 9
  vs 10) can't drift. The Emulators accordion card opts out with `Padding="0"` because its row manages its
  own insets. `section-eyebrow` / `field-label` styles similarly tokenize the two label tiers.
- **Accent = primary call-to-action only.** Connect / Download / Update / Sync stay accent; maintenance
  "Rescan" is default everywhere — the Texture Packs "Rescan" was demoted to match General and Emulators.
- **Texture rows own their commands.** `TexturePackRowViewModel` now exposes Browse/UseDetected/OpenFolder
  `[RelayCommand]`s that delegate to parent-provided funcs, so the view binds row commands instead of
  `$parent[Window].((EmulatorSettingsViewModel)DataContext)…`. The parent's `BrowseTextureOverride` /
  `ClearTextureOverride` / `OpenTextureFolder` commands are kept — `GamepadSettingsViewModel` and the
  parity tests still consume them.
- **The shared folder/Browse AutomationId is intentional, not a bug.** The Desktop↔Gamepad parity test in
  `MainWindowVisualSnapshotTests` asserts the *set* of `saves.*` / `textures.*` ids on Desktop equals the
  Gamepad `ParityId` set, so a field's TextBox and its Browse button deliberately share one field key.
  Giving the Browse button a distinct id would break parity unless mirrored on the Gamepad surface, so it
  was left as-is. The Emulators section (not in the parity set) gained Desktop-only
  `emulators.{systemId}.*` ids for scripting/accessibility.

## 2026-08-07 — ScreenScraper matches GameCube/Wii/PS3/Dreamcast by disc product code, not whole-file hash

GameCube (and Wii) could not be auto-scraped: they were not in the preview service's serial-route
set, so they fell through to the whole-file hash route, which rejects every compressed/container
format (`.rvz`/`.wbfs`/`.ciso`) as `UnsupportedFormat` and, for a bare `.iso`, sends a full-image
hash ScreenScraper does not index for these systems. PS3 and Dreamcast were worse — their
fingerprint policy has no whole-file extensions at all, so every automatic lookup failed.

- **Route these four disc systems through `serialnum`.** `SerialSystems` now includes `gamecube`,
  `wii`, `playstation3`, and `dreamcast`. The disc product code is read from inside the container
  (the small header only), so a compressed image that cannot be whole-file hashed still matches —
  the same story CHD/CSO already had for PlayStation.
- **`FindSerial` accepts `DiscId` as well as `Serial`.** GameCube/Wii emit the 6-char disc game code
  as `GameIdentifierKind.DiscId`; PlayStation/Dreamcast emit `Serial`. Both are what ScreenScraper
  indexes as `serialnum`. A `Serial` is preferred when a system offers both, so the more specific
  disc serial wins. The match is still recorded as `GameProviderMatchMethod.Serial` (no new enum
  ordinal / DB migration).
- **Whole-file hash extension lists were deliberately NOT widened.** A `.rvz`/`.wbfs`/`.ciso` is a
  container whose bytes are not the raw disc image, so its whole-file hash would never match the
  catalogue; the serial route is the correct path for them. `ScreenScraperFingerprintProfileTests`
  still asserts these formats are never whole-file hashed.
- Cartridge header codes remain excluded from serial matching, so a rom hack is never matched to the
  original release by a shared code.

## 2026-08-07 — ScreenScraper matches Nintendo 3DS by whole-file hash, not serial

Follow-up to the disc-serial fix. 3DS previously had no ScreenScraper match route at all (empty
fingerprint policy, not serial-routed), so every automatic lookup returned `UnsupportedFormat` and
only the manual title search could reach it. Checked how ScreenScraper actually indexes 3DS: like
other ROM systems it matches by whole-file hash (CRC/MD5/SHA1) + size, aligned to No-Intro — the
NCCH product code is *not* the reliable key, and 3DS is cartridge-based so its header code is shared
by rom hacks (the reason cartridge systems are excluded from the serial route).

- **`.3ds`/`.cci` are now whole-file hashed.** They are the same CTR card image (only the extension
  differs) and are exactly the file No-Intro/ScreenScraper index, so a clean dump gets an exact hash
  match like NES/SNES/GBA/NDS. A miss (trimmed/decrypted dump) returns NotFound and the single-game
  scraper falls back to title search, as before.
- **Serial route deliberately NOT used for 3DS.** The NCCH product code is a cartridge header code a
  rom hack keeps, and hash matching already distinguishes hacks; whether ScreenScraper even indexes
  3DS by serialnum is unverified. So 3DS stays out of `SerialSystems`.
- **`.cia`/`.cxi`/`.app`/homebrew/compressed stay excluded** from whole-file hashing — they are not
  the catalogued cartridge dump, so their whole-file hash would never match; they fall back to
  filename/title search. Locked by `ScreenScraperFingerprintProfileTests`.
- The multi-gigabyte-dump cost concern that originally gated this is handled the same way as every
  other hashable system: a one-time read gated behind fingerprint consent, cached by path+size+mtime.
- Batch scraping now covers clean 3DS dumps too (it passes fingerprint consent); before, 3DS was
  always Unsupported in batch.

## 2026-08-07 — Save-sync review fixes: per-platform isolation, Dolphin JP paths, crash recovery

From a three-agent review of everything touching save sync. Changes and the non-obvious calls:

- **A locked file or unreadable emulator config now costs one platform, not the whole "Sync all".**
  The apply loop (`SaveSyncService`) previously caught only `CloudPayloadMissingException` /
  `SaveUnitNotResolvableException`, and provider enumeration ran outside any try, so one
  `IOException`/`UnauthorizedAccessException` mid-apply, one out-of-root `ArgumentException`, or one
  `SaveProviderConfigurationException` propagated out of `SyncAllAsync` — dropping the manifest flush
  and every other platform's sync (the coordinator batches all systems into one call). The apply loop
  now also skips `IOException`/`UnauthorizedAccessException`; the snapshot loop also skips
  `ArgumentException`; per-target enumeration skips `SaveProviderConfigurationException`. A
  whole-platform skip is recorded under a synthetic `<prefix>(configuration)` unit id so the row still
  surfaces it. **`InvalidDataException` is deliberately still fatal** — a corrupt download must not be
  silently skipped (guarded by `CorruptDownload_DoesNotReplaceLocalOrAdvanceItsBaseline`).
- **PCSX2 unit-id name guard now rejects `.`/`..`.** `Path.GetFileName("..") == ".."`, so the
  round-trip check alone admitted the parent-dir name and let a crafted cloud id resolve outside the
  memcards root. This was the only provider missing that guard; the others anchor the name shape.
- **Dolphin: unit ids keep the logical region token "JPN"; physical paths map to Dolphin's on-disk
  "JAP".** Dolphin names Japanese saves `MemoryCard*.JAP.raw` and `GC/JAP/Card *`. Resolving a fresh
  download to a `JPN` path wrote where Dolphin never reads. The unit id keeps `JPN` so it stays stable
  across machines; only the resolved path is mapped via `OnDiskRegion`. (Was previously a
  read-only-if-JAP-exists fallback, which fixed reads but not new writes.)
- **Interrupted folder writes self-heal.** `FileSystemLocalSaveEndpoint` folder installs use two
  renames (live → `_emushelf-previous-*`, then incoming → live); a crash between them left the save
  only under the staging sibling. Staging dirs are now deterministic (`_emushelf-previous-<leaf>` /
  `_emushelf-incoming-<leaf>`) so a lazy sweep on the next snapshot/write restores the displaced
  folder when the live path is missing, drops it when newer content is already installed, and clears
  unverified incoming scratch. Deterministic names (not guids) are safe because sync is single-flight.
- Removed confirmed-dead code: `CloudSaveSyncCoordinator.VerifyCloudDataAsync`,
  `ICloudSyncTransport.IsConnectedAsync` + its rclone `lsjson` impl, RetroArch `_corePath` and
  `RetroArchCore.CoreId`. Off-thread provider construction (`Task.Run`) in the sync/force pipelines to
  match detection; memoized Dolphin GCI folder reads + single-enumeration; lazy RetroArch per-game
  override probe (detection-only); skipped counts now surfaced in the sync status lines.

## 2026-08-08 — Spotlight hero: logo over title, metadata as centered chips

Simplified the spotlight (couch) hero's right column. The big game title was dropped: the logo
carries the identity, and the name still shows in the left list. The former single `·`-joined info
line (genre · year · players · developer · publisher · filename) is now a centered `WrapPanel` of
per-fact chips, with the launch source (file/disc) as a dim caption below. Non-obvious calls:

- **Title fallback is gated on resolved details, not on logo-bitmap presence.** Showing the title
  when `!HasWheelImage` would flash it in the gap before a logo bitmap decodes (details resolve, then
  fan art, then the wheel). `ShowSpotlightTitleFallback` is `AreSpotlightDetailsLoaded && WheelPath is
  null`, so the title only stands in once we've confirmed the game has no logo art. Locked by
  `ShowSpotlightTitleFallback_OnlyWhenResolvedDetailsConfirmNoLogoArt`.
- **Publisher chip collapses into the developer when identical** (common for first-party titles), and
  players read in words ("1 player" / "2 players") instead of "2P". `ComposeSpotlightInfo` now returns
  the chip list (`IReadOnlyList<string>`) rather than a joined string.

## 2026-08-08 — Spotlight hero: UI polish pass

A follow-up cleanup on the spotlight hero and list. Non-obvious calls:

- **Metadata chips split into two fixed rows (genre/year/players, then developer/publisher), not one
  `WrapPanel`.** A `WrapPanel` left-packs its wrapped remainder, so a partial second line hugged the
  left instead of centring under the first. Each row is now its own centred `ItemsControl` fed by
  `SpotlightFactsPrimary` (first 3) / `SpotlightFactsSecondary` (rest); the second row hides when empty.
- **Action row is a centred, content-sized cluster (rating · achievements · Play), not an edge-to-edge
  `Auto,*,Auto` span.** The span left the achievements pill stretched with left-packed content and dead
  space, and — worse — as the flex column it *clipped its own count/bar* on narrow windows. The cluster
  matches the centred logo/chips above and keeps every action intact.
- **The cluster is wrapped in a `Viewbox` (`Stretch=Uniform`, `StretchDirection=DownOnly`,
  `ClipToBounds=False`).** Because the cluster is content-sized it can outgrow the hero column on a
  narrow (windowed, down to the app's 900px min) couch and clip the primary Play button. DownOnly leaves
  it at natural size at real couch resolutions (Deck/TV, ≥1280) and only shrinks it to fit when squeezed.
  `ClipToBounds=False` lets the focused action's ring (`-5` margin) and glow (12px blur) spill past the
  Viewbox instead of being clipped, so no compensating inset is needed and the pills' bottom edge stays
  flush with the list card (whose bottom margin just matches the hero's own 10px inset).
- **Hero pills now use the list card's layered translucency, not an opaque fill.** `Border.spotlight-pill`
  became a transparent content layer over a new `Border.spotlight-pill-fill` (EmuPopoverBrush at the same
  0.72 alpha as the card), so the pills read as the same glass instead of flat opaque slabs.
- **`MarqueeTextBlock` centres a title that fits.** The no-logo fallback title was left-aligned while the
  chips/subtitle centred. `MarqueeTextBlock` now forwards a `TextAlignment` property (default the
  control-neutral Start; the hero sets Center), so the control isn't silently re-defaulted for other
  callers. It's a no-op while overflowing (the text is arranged at exactly its own width, so the
  there-and-back scroll still starts at the left).
## 2026-08-08 — Gamepad mode disables the mouse entirely

The 2026-07-25 M31 decision kept "mixed input" in Gamepad mode: the shell suppressed stale
pointer-hover treatment while a controller was active but deliberately *did not* disable the mouse.
In practice that made an accidental trackpad or mouse bump (common on handhelds and couch/TV setups,
and under Steam) drop the controller-focus visuals and park a visible cursor over the shell. Gamepad
mode is a controller/TV surface, so it now disables the mouse outright — matching console and
Big-Picture expectations.

Two independent layers, both always on in Gamepad mode:

- **Cursor hidden (`WindowInterfaceModeService`).** Entering Gamepad sets `Window.Cursor` to a shared
  `StandardCursorType.None` instance; Desktop restores `Cursor.Default`. This lives beside the existing
  window-level fullscreen handling and rides the same mode transitions.
- **Surface non-hit-testable (`IsHitTestVisible="False"` on `GamepadRoot`).** The whole gamepad UI —
  rail, grid, spotlight, and every overlay — is a single container shown only in Gamepad mode (desktop
  content is `IsVisible=false` beside it). Marking that container non-hit-testable makes the mouse
  unable to hover, select, drag, or scroll *anything*. An earlier attempt swallowed `PointerPressed`/
  `Released`/`WheelChanged` at the window tunnel, but that could not stop hover: Avalonia sets the
  `:pointerover` pseudo-class during hit-testing regardless of routed-event `Handled`, so elements
  still lit up under the (invisible) pointer. Disabling hit-testing is the one lever that covers hover
  too. Controller/keyboard input is unaffected — it drives focus through the view model, not
  hit-testing.

Consequences: the mouse-driven Gamepad code paths were removed — the pointer-move modality flip
(`NotifyGamepadPointerInput`), the click-to-focus-achievement handler, and the now-orphaned
`FocusGamepadAchievement` command. `IsGamepadControllerInputActive` is therefore always true during a
Gamepad session (kept as the `.controller-input` visual toggle rather than rewiring every binding).
The keyboard/Steam-Input path (B/Esc, Start/`F10`, confirmation to exit) is untouched, so a controller
still drives everything and the app can always be left.

## 2026-08-08 — RetroArch core name is read from every install's info folder, not only the portable one

A RetroArch save/state folder is named after the *core* whenever "sort into folders by core name" is
on, so resolving that folder needs the core's `corename`. `RetroArchSaveLocationProvider` read it from
one place — `<installationDirectory>/info` — plus a small hardcoded `KnownCoreNames` fallback. On a
Steam Deck (Flatpak RetroArch) the installation directory is EmuShelf's own portable base, which has no
`info` folder; the real info files live under `~/.var/app/org.libretro.RetroArch/config/retroarch/info`.
So *no* core's info file was read there, and the fallback table decided everything. Genesis Plus GX was
in the table and resolved; **bsnes was not**, so its save-state folder came back `null` and Settings
showed "the emulator configuration does not expose a safe folder for save states." Any core outside the
table (Mesen-S, MAME, gpSP, …) hit the same wall on Flatpak/Linux/macOS installs.

Fix, mirroring how core *version* is already resolved for state-compatibility keys:

- Core-name lookup now searches an ordered list of `info` directories — portable first, then the
  platform's user-profile location (Flatpak `~/.var/app/...`, Linux `~/.config/retroarch/info` or
  `$XDG_CONFIG_HOME`, macOS `~/Library/Application Support/RetroArch/info`) — the same per-platform
  branching as `ResolveConfigPath`. The core's own info entry is authoritative and always wins over the
  fallback table. This is the general fix: it names *any* installed core, not a fixed list.
- `KnownCoreNames` gained bsnes and its variants (`bsnes`, the three `bsnes_mercury_*`, `bsnes_hd_beta`)
  plus `snes9x2010`, as defense for the rare no-info-file case (a bare core). Because the info entry is
  read first, a slightly-off fallback name can only ever apply when there is no info file at all — where
  the alternative was a total miss anyway — so this is strictly safer than the previous `null`.

Core-name resolution therefore moved to the end of the constructor (it now depends on the platform and
Flatpak flags). This fixes save states *and* the sibling base-save case, which previously failed closed
for the same unresolved-name reason when files sorted by core.

## 2026-08-08 — Save-sync robustness pass: empty folders, PS2 system data, shared cores, one pass per launch

A two-agent review of real Steam Deck sync logs surfaced four issues; all four fixed together.

1. **An empty save folder must never overwrite a good cloud copy.** `FileSystemLocalSaveEndpoint.Snapshot`
   returned a *non-null* snapshot for a folder that exists but holds no files — hash-of-nothing plus an
   epoch (1970) mtime. With a baseline present, the planner then chose **Upload**, and the Upload leg
   (unlike a conflict) takes no backup, so an emptied folder destroyed the cloud copy and propagated
   emptiness to other machines. This was the only permanent-data-loss path found. Fix, two parts:
   `Snapshot` returns `null` for a contentless folder, and `SaveSyncPlanner` treats an empty-content
   snapshot (empty folder or 0-byte file) as absent on *both* sides. So an emptied local restores the
   cloud copy (never overwrites it), two empty sides are a no-op (a stray empty entry already in the cloud
   is not re-downloaded on every sync), and a machine holding the real save uploads over an empty cloud
   entry to heal it — all consistent with "sync never deletes". The planner's two-sided normalization is
   essential: without it, `Snapshot` returning `null` would make every already-propagated empty cloud entry
   re-download forever. (The field manifest's empty-hash/1970 Dolphin Wii NAND entries were this.)

2. **PS2 memory-card system directories are not saves.** `BADATA-SYSTEM` (and the `B?DATA-SYSTEM`
   region variants + `BWNETCNF`) are the PS2 BIOS browser's own data; the BIOS rewrites them on nearly
   every boot, so they re-uploaded every session (a field card reached revision 15). Excluded from
   `Pcsx2SaveLocationProvider.IsSaveDirectory` like PCSX2's own `_`-prefixed housekeeping.

3. **A core shared by two systems no longer double-tracks its folder.** When "sort saves by core" is on,
   a per-core folder was treated as *exclusive* (claim every file). That is correct for one-core-one-system
   but wrong when one core serves several systems (mGBA for both GBA and GBC): both rows resolved to the
   same `saves/mGBA` (and `states/mGBA`) folder and each claimed — and uploaded — every file, so every
   Game-Boy-family save/state synced twice. The coordinator now detects a core configured for more than
   one system and passes `CoreSharedAcrossSystems`; a shared provider claims only its own library's saves
   (`IsExclusive` off) and states (`StateBelongsToThisSystem`). Single-system per-core folders are
   unchanged (still claim the whole folder, so a save for an un-imported game still syncs).

4. **One combined pass per launch instead of two.** `SyncSystemAsync` ran a saves pass then a separate
   save-states pass, each with its own cloud index read + commit and manifest load/save. It now runs a
   single `SyncContentScope.All` pass (base saves plus states when the toggle is on), halving the cloud
   round-trips for a state-enabled platform. The pre-pass state diagnostic and the "states toggle off"
   flag are preserved, and one combined result line is logged in place of the two.
## 2026-08-08 — "Show in folder" reveals a game's file in the OS file manager

The desktop grid/list context menu gained an entry that opens the game's containing folder with
the ROM preselected. Because "reveal a file, selected in its folder" has no cross-platform API,
this lives behind a Core interface (`IFileRevealService`) with one Infrastructure implementation
(`FileRevealService`), per the platform-behind-interfaces rule.

- **Per platform.** Windows `explorer /select,<path>`; macOS `open -R <path>`; Linux the
  freedesktop `org.freedesktop.FileManager1.ShowItems` D-Bus method (the one call most Linux file
  managers honour for selecting an item). Linux has no universal "select" otherwise.
- **Windows needs a raw argument.** `explorer.exe` uses a non-standard command line: the whole
  `/select,"<path>"` must arrive as one raw, quoted token, so that one invocation sets
  `ProcessStartInfo.Arguments` directly instead of using `ArgumentList` (whose argv-style escaping
  makes explorer ignore the path and open Documents). Every other invocation uses `ArgumentList`.
- **Reveal is fire-and-forget.** The file manager window outlives us, so we only confirm the OS
  started the process (explorer's own exit code is unreliable). The one exception is the Linux
  D-Bus reveal, whose exit code we await so a missing/unanswered `FileManager1` provider can fall
  back to `xdg-open` on the containing folder.
- **Target + fallback.** It reveals `LaunchModel.Path` — the concrete source that would launch (the
  selected disc of a multi-disc set), so the highlighted file is the right one. When that file is
  gone but its folder still exists (an unavailable game), the folder is opened instead; when neither
  exists, the command reports a friendly status rather than throwing. The menu label is
  platform-native ("Show in File Explorer" / "Reveal in Finder" / "Show in file manager").

## 2026-08-08 — "Open texture folder" is the texture subsystem's one allowed write

The game context menu gained **Open texture folder** (desktop grid and list), which opens the folder
an emulator loads a game's replacement textures from and, when it doesn't exist, **creates it with the
correct id** so a downloaded pack can be dropped straight in. It appears only for the six
texture-capable systems (`TexturePackProviderRegistry.Find(SystemId) is not null`).

This is a deliberate, bounded departure from the subsystem's read-only stance (see the
`TexturePackSettingsContext` note: "no install, repair, move, rename, or delete … EmuShelf never
performs one"). Creating the id folder is the **only** write the texture subsystem makes, and it is
tightly scoped:

- It creates an **empty** directory inside the emulator's own textures root, on explicit user action.
- It never touches a game file and never modifies, moves, renames, or deletes an existing pack.

The path is resolved by `TexturePackCoordinator.ResolveTextureFolderAsync`, which reuses the same
provider/root resolution the scan uses (honouring the override and Flatpak, and sitting out when a
different emulator is active for the system). The folder **id** comes from the game's stored
identifiers via the pure `TexturePackFolderNaming.Build` (the inverse of `TexturePackMatcher`: serial
for PS1/PS2, hyphen-less game-id for PSP, disc id for GameCube/Wii, title id for 3DS); when none are
stored it extracts them locally (no network) exactly as the rescan backfill does and persists them
**only when the game has no identifiers yet**, so an existing Sha1/Crc32 set is never clobbered and the
folder EmuShelf creates is the one a later scan will match. Opening the folder reuses the same shell
service "Show in folder" introduced — `IFileRevealService.OpenDirectoryAsync` (added alongside
`RevealAsync`) — so there is one place that talks to each platform's file manager, and the coordinator
keeps no UI or write policy (the create-and-open lives in `MainViewModel.OpenTextureFolderAsync`).

## 2026-08-08 — EmuShelf may write emulator hotkey config, as a reversible, machine-local layer

The library has been strictly read-only toward emulator configuration everywhere but one place — the
texture-folder create ("Open texture folder" is the texture subsystem's one allowed write). A new
**M40** deliberately widens that: EmuShelf will write a small, uniform controller-hotkey scheme into
each supported emulator's own settings, so one combo means the same thing everywhere (Start+Select =
close game, Select+Square = rewind, Select+Circle = fast-forward, Select+Triangle = save state,
Select+Cross = load state). This is a user-approved break from the read-only stance, not an accident,
and it is bounded the way the texture write is: explicit user action, never a game file, and — because
this write is larger and can disturb existing bindings — fully reversible.

Three findings from grounding the design in the user's real configs (`G:\ES-DE\Emulators`) set the
scope:

- **It is not uniform across all seven emulators.** DuckStation, PCSX2, Dolphin, and PPSSPP express
  two-button controller chords directly in their config (`&` for the shared DuckStation/PCSX2 engine,
  `&`/`@()` for Dolphin, `:` for PPSSPP once `AllowMappingCombos` is on). RetroArch can too, through
  its enable-hotkey modifier, but stores raw driver-specific button *numbers* that differ for the same
  pad across the xinput/sdl2/hid drivers, so its bindings must be resolved at write time against the
  pad RetroArch actually matched — never a hardcoded table. **RPCS3 and Azahar cannot bind these
  actions to a controller at all** (keyboard/menu-only shortcuts), so they are out of M40; reaching
  them would need EmuShelf to detect the chord itself and inject a keystroke while the game runs, which
  contradicts the in-game input-suspend decision and is deferred.
- **Rewind barely exists.** Only DuckStation, RetroArch, and PPSSPP have a rewind feature; PCSX2,
  Dolphin, RPCS3, and Azahar have none. Where an emulator has no rewind, Select+Square is left unbound
  and reported as unsupported rather than repurposed, so the same combo never silently does something
  different on a different system. Where rewind exists it is off by default and costs memory, so
  applying the rewind chord also flips the feature's enable flag (`RewindEnable`, rewind) — a bound key
  to a disabled feature is a dead key.
- **Every emulator rewrites its config on exit.** An edit made while the emulator is running is
  clobbered on its next clean shutdown, so M40 writes only while the target emulator is not running
  (EmuShelf already tracks the process) and states that a change takes effect next launch.

Safety model: back up each file into portable `Settings/` before its first modification; edit
surgically — update/insert only the specific lines, preserving comments, ordering, unknown keys, and
the `SettingsVersion`/format markers verbatim — rather than parse-and-regenerate, since the existing
`EmulatorIniFile` is read-only and lossy on purpose and a new writer is required; write through
`AtomicFile`; offer a preview diff before applying and a one-click revert to the backup. Tokens are
derived from the emulator's own existing pad binding where possible (its `[Pad1]`/`[ControlMapping]`
already maps the physical button to that emulator's vocabulary), so EmuShelf reuses the user's real
controller and device index instead of guessing `A/B/X/Y` vs `FaceWest` vs `Button W` vs `20-99`.

This is deliberately a **machine-local** layer, which is why it does not conflict with M33 Phase 4's
"controller profiles and input remaps are usually wrong to *sync*": device names and indices differ
per machine, so EmuShelf authors the scheme on each machine rather than copying one machine's bindings
to another. The canonical action set and the surgical writer are built to extend to the user's stated
next steps — correct memory-card/save settings, and eventually an EmuShelf-owned emulator user
directory — without a second mechanism.

## 2026-08-08 — M40 pivots from controller chords to a uniform keyboard scheme + Steam Input

Real-hardware testing of the controller-chord implementation (above) showed that writing *controller*
hotkeys into each emulator is fundamentally controller-specific and fragile: RetroArch stores raw,
driver-specific joypad button numbers (the standard table was wrong for the user's XInput wrapper, so
nothing fired); Dolphin binds by device name; PPSSPP two-button combos are "not planned" upstream;
only DuckStation/PCSX2 (SDL position tokens) survive a controller swap. Injecting keystrokes from
EmuShelf instead is blocked — RetroArch ignores injected keys (raw input, libretro #16438), and
reliable injection needs a non-portable kernel virtual-HID driver.

**Decision: write a uniform _keyboard_ hotkey scheme instead** (rewind=R, fast-forward=L, save=F2,
load=F4, close=F8), because a keyboard key is identical on every controller. The controller→keyboard
translation is done once, outside the emulators, in a **Steam Input** layout (a bundled preset the
user imports); one combo→key mapping serves every emulator since they all read the same keys. Keys
were chosen to match RetroArch's own defaults so it needs almost no change. Conflicting default
keyboard shortcuts are **overwritten** (backed up, revertible). Scope: DuckStation, PCSX2, Dolphin,
PPSSPP, RetroArch write all applicable actions; Azahar writes save/load/close/fast-forward; RPCS3 is
**close-only** (it has no load-state hotkey — its save states are suspend/resume, not
quicksave/quickload). Full implementation spec, verified per-emulator tokens, and reuse-vs-replace
code map are in `docs/hotkey-keyboard-scheme.md`. Implementation resolved both open items: RPCS3's
shortcuts serialize under `[Shortcuts]` with the key `game_window_stop` (from RPCS3's
`shortcut_settings`), and Azahar's action names vary by version ("Quick Save" vs. "Save to Oldest Slot",
"Toggle Turbo Mode" vs. "Toggle Per-Application Speed"), so its configurator binds whichever candidate
name the machine's config actually has. One thing still needs a real-hardware check: whether Steam
Input's emulated keystrokes reach RetroArch (it filters injected input — testable with zero code against
RetroArch's existing keyboard defaults). The infrastructure from the controller implementation (surgical
INI editor, backup/revert, coordinator, Settings UI, registry) is reused unchanged; only the
configurators and the profile model are rewritten.

## 2026-08-08 — Desktop list view keeps its ListBox and gains a VM-driven, persisted column model (M40)

The Desktop list view was a `ListBox` whose column layout was hard-coded twice — the header Grid and
the row `DataTemplate` both carried the same literal `ColumnDefinitions="84,*,150,90,96,92,100"` — so
adding columns meant editing two places in lockstep, and there was no show/hide, reorder, or resize.
M40 wants an iTunes-style table (user-chosen visible columns, drag reorder/resize, sort by any
column, persisted).

`Avalonia.Controls.DataGrid` was evaluated first, because it offers show/hide, drag-reorder,
drag-resize, and per-column sort natively. A Phase-0 spike (package added, view wired up) rejected
it: DataGrid would **regress shipped M25 interactions** and force fighting the control's internals.

- **Marquee auto-scroll (M25) breaks.** The rubber-band drag reads `ScrollViewer.Offset/Extent/
  Viewport` off the list to auto-scroll and to anchor the box to content while scrolling
  (`MainWindow.axaml.cs` `GetActiveScroller`/`UpdateMarquee`/`OnMarqueeAutoScrollTick`). DataGrid
  manages its own scrolling and does not expose that `ScrollViewer`, so auto-scroll-to-extend would
  degrade.
- **Selection visuals fight the control.** Selection is view-model-owned (`GameViewModel.IsSelected`,
  shared with the grid, driven by window-level tunnel pointer handlers and by marquee/select-all),
  not by a control's `SelectedItems`. DataGrid insists on drawing its own row-selection highlight,
  which desyncs from our model (marquee/select-all select many rows the control doesn't know about)
  and can only be suppressed by overriding theme-internal part/resource names.
- Per-row **context menu** and **VM-driven custom sort** (Achievements/Textures sort by `*SortKey`,
  not displayed text) would also need re-engineering onto DataGrid's model.

DataGrid's only advantage over the alternative is that reorder/resize are *native* rather than
hand-rolled — an internal detail invisible to the user. It is not worth regressing shipped features
for, so:

**Chosen: keep the `ListBox`, drive its columns from a view-model column model.** The row stays a
`ListBox` item, so marquee, multi-select, the per-row context menu, inline title edit, custom sort,
and the async cover-load hooks are all **untouched and un-regressed**. A view-model
`ObservableCollection` of column descriptors (key, header, visible, resolved pixel width, sort
mapping, order) drives both the header and each row: cells stay statically defined but bind their
`Grid.Column` to the column's ordered position and their `Grid` column widths to the model, so
reorder is a data move, resize updates a width, and hide collapses a position — all via bindings, no
control internals. One flex column (Title) absorbs remaining viewport width (already reported to the
view model by `OnLibrarySizeChanged`). Reorder and resize *gestures* are self-contained header
pointer handling in code-behind (view wiring only).

Column configuration (visibility, order, width, active sort) is **owned by the view model and
persisted to portable `Settings/`**, so it survives restart and moves with the portable install.
Persistence tolerates unknown/removed column ids, and Title is a minimum always-on column so the
table can never be emptied.

New scraped columns read a **bulk metadata projection** (`IGameDetailsStore.GetAllDetailsProjections`)
built once per scope build on the load worker, never a per-row `GetDetails` on the UI thread — the
details store is per-game today, and a naive column binding would reintroduce the exact N+1 the M11
performance work removed. The cloud-save-status column is deferred behind M29 (battery/memory-card
sync) and the save-state variant behind M33.

## 2026-08-09 — Gamepad progress/notification consistency

Fixing "progress bars and notifications are missing or broken in gamepad mode":

- **Settings progress text is cleared on completion, not just at start.** The metadata/RA/cloud
  `*ProgressText` (+ their bar totals) were seeded per run but never reset, so the single Gamepad
  settings pill — which ranks a section's progress text ahead of its status text — kept showing a
  finished run's "N of N", and a later maintenance run could even re-show Desktop's metadata bar
  (gated on the still-non-zero total). They are now reset in each op's `finally`, mirroring the
  TexturePacks path that already did this.
- **The General section clears its sibling status on start.** Metadata and library maintenance share
  one Gamepad pill; a rescan now clears the metadata status/progress up front so the pill reflects the
  most recent library action rather than whichever `FirstNonEmpty` happened to rank first.
- **"Rescan all consoles" reports live per-console progress.** `LibraryMaintenanceActions.RescanSystem/
  RescanAll` now take an `IProgress<string>`; `RescanAsync` already computed "Rescanning {system}… {n}
  found" for the main-window bar (hidden behind the modal) and now also reports it to Settings. A
  synchronous reporter is used (the scan already marshals to the UI thread) so the final result line
  can't be reordered behind a late progress post the way `Progress<T>` could.
- **Desktop maintenance/texture actions get an indeterminate bar** (gated on a rescan-specific flag /
  `IsTexturePackBusy`) and the **Gamepad settings pill gets a section-scoped indeterminate bar**, so a
  "working" affordance is consistent across surfaces instead of some actions showing a bar and others
  only text. The bars are busy-gated, so they stay collapsed (no layout shift) in visual snapshots.
- **Gamepad launch/exit save sync shows a large centered "Syncing saves…" panel** (`IsSyncingSavesForLaunch`)
  rather than only the corner toast, which was too easy to miss at a couch distance — this is a UX
  gap, not a code bug (the toast machinery was already correct and on the UI thread).

## 2026-08-09 — Batch scraper and RPCS3 sync reachable from Gamepad mode

Gamepad mode dropped the whole emulator-rows Settings section and had no multi-select, so two flows
were controller-unreachable:

- **Batch scrape** (the app's only multi-item determinate progress bar) is now reachable via a System
  Menu "Scrape all in view" entry that batch-scrapes the games currently in view. A new
  `GamepadBatchScraperViewModel` wraps the shared `GameBatchScraperViewModel` with a linear D-pad
  focus model (Configure → determinate progress → summary). The couch flow keeps the sensible defaults
  (fill missing, all media) and exposes only the "replace existing values" choice; per-field media
  selection stays a Desktop power-user control.
- **RPCS3 library sync** — the only PS3 import path, and deliberately skipped by "Rescan all consoles"
  (`NonRpcs3Systems()`) — is exposed as a Gamepad General-section action so a controller-only user can
  import PS3 games. It is `ExcludeFromParity` because Desktop reaches it from the PS3 emulator row, not
  a General field. Per-system rescan was intentionally **not** added to Gamepad: "Rescan all consoles"
  already covers every non-PS3 console, and RPCS3 sync covers PS3, so the two together are the couch
  equivalent of Desktop's per-platform rescans.

## 2026-08-09 — Gamepad status toast (and save-sync panel) hidden in Spotlight view

The Gamepad status toast — which carries launch/preflight errors like "the configured PCSX2
executable was not found" — and the new centered save-sync panel are direct children of GamepadRoot
that share a grid cell with the single-game **Spotlight** view's opaque edge-to-edge backdrop, which
is declared after them. Panels paint children in ZIndex order, so the backdrop painted over both:
errors and sync status were visible in the cover grid and on Desktop but **invisible in Spotlight
view** (a common couch view). Fixed by layering with ZIndex rather than reordering the markup: the
toast and save-sync panel are ZIndex 1 (above the library/spotlight/dock content at 0), and the
overlay scrim is ZIndex 2 (still above them, so an open overlay covers them). Pre-existing bug for the
toast; the save-sync panel would have shipped with the same flaw. Regression test asserts the toast's
ZIndex exceeds the spotlight backdrop's.
## 2026-08-09 — M40 hotkey applier: Steam Deck fixes from real-hardware testing

First real-hardware run of the M40 keyboard-hotkey applier (on a Steam Deck) surfaced four issues; the
per-emulator token verification had been done against Windows configs on `G:` only.

- **Dolphin — Hotkeys.ini absent.** Dolphin writes `Config/Hotkeys.ini` only once a hotkey is
  customised in its UI (not merely from playing games), so even a long-used install can lack it, and the
  configurator reported `ConfigurationNotFound`. Decision: a missing file is **not** an error — build
  from an empty document and **create** `Config/Hotkeys.ini` with a `[Hotkeys]` section Dolphin reads on
  next launch. `HotkeyConfiguratorBase.Apply` now ensures the target directory exists before writing.
  **Guard against the wrong-folder case:** creating is only safe if we resolved Dolphin's *real* user
  directory, so it creates the file only when a `Config/Dolphin.ini` is present there (Dolphin always
  writes that on first run); if neither file exists the folder isn't Dolphin's user dir, so it reports
  `ConfigurationNotFound` with the path rather than silently writing a file Dolphin never reads (which
  would look "Applied" but do nothing). (Revert of a file EmuShelf created leaves it in place — there is
  no pre-existing backup to restore; acceptable, the created file holds a valid default scheme.)
- **RetroArch — F8 collides with screenshot, and quit needs two presses.** The M40 note claimed "F8 is
  free of conflicts across all emulators checked"; that was **wrong for RetroArch**, whose built-in
  screenshot key is also `f8` (so a single F8 screenshotted *and* closed), and whose `quit_press_twice`
  defaults to `true` (so close needed two presses). A survey of all seven confirmed F8 is the least-bad
  uniform close key and no key is both unbound everywhere *and* bindable on Dolphin across
  Windows/Linux/macOS (Dolphin's macOS backend has no Insert/nav keys at all), so **F8 stays** and the
  clash is cleared in-config: neutralise `input_screenshot` even when it is absent (RetroArch falls back
  to the internal `f8` default) unless the user moved it off F8, and set `quit_press_twice=false` so a
  single deliberate Steam Input combo quits. RetroArch is the *only* emulator needing this — PCSX2's
  modern Qt build ships no default hotkey keys (so nothing is on F8 unless the user set it, which the
  overwrite policy then clears), DuckStation's screenshot defaults to F10, and Dolphin's to F9 (its F8
  load-slot bareword is already cleared). All backed up and revertible.
- **DuckStation — `SettingsVersion 'unknown'` on the Deck (direct AppImage; upstream dropped the Linux
  Flatpak).** The version gate refused because the `settings.ini` it read had no `[Main] SettingsVersion`.
  The gate exists to avoid writing into an unknown format, but refusing a *missing* version is too strict:
  the file was found at DuckStation's own config path, the `Keyboard/<Key>` token format is stable across
  versions, and the emulator rewrites the version itself. Decision: the gate now refuses only a
  *different* explicit version (a real format change); a *missing* one is accepted as long as the
  version's section (`[Main]`) is present — otherwise it's treated as a stub and refused with a clear
  message. Diagnostics also name the exact file read. This self-heals newer AppImage/fork builds without
  the user touching anything.
- **"Bundled Steam Input layout" was never an importable file.** The Settings summary said "Import the
  bundled Steam Input layout", but Steam Input layouts are set up per game inside Steam and cannot be
  dropped in as files by a third-party app (see `docs/steam-input-preset.md`). Decision: correct the
  wording (no file to import) and **surface the controller mapping in-app** — a "Controller setup (Steam
  Input)" card in the Hotkeys settings section lists Select+face-button → key/action and the Steam Deck
  steps, so the guidance no longer lives only in a repo markdown file a Deck user never sees.

## 2026-08-09 — Flatpak targets can pin a branch; install check lists branches instead of `flatpak info`

A Deck user installed the PCSX2 **nightly** (the `beta` branch on `flathub-beta`) and every PS2 launch
then failed with "Flatpak application 'net.pcsx2.PCSX2' is not installed." Root cause: stable and
beta/nightly builds share one application id and differ only by *branch*, and the preflight check ran
`flatpak info <appId>`, which errors with "Multiple branches available…" (non-zero exit) when both
branches are installed — so an app that launches fine looked uninstalled.

- **`FlatpakApplicationTarget` gains an optional `Branch`** and a `Ref` (`appId` or `appId//branch`).
  `Ref` is what goes to `flatpak run`/`flatpak info`; pinning the branch makes those commands
  unambiguous. `AppId` stays the clean id because every branch shares one per-app data dir
  (`.var/app/<appId>`) for saves/config, so save-location and RetroArch core paths must not include the
  branch. The ref (with branch) is what round-trips through the store's `TargetValue` column — no schema
  change.
- **The install check now lists installed branches** (`flatpak list --app --columns=application,branch`)
  and tests membership rather than calling `flatpak info <appId>`. An unpinned target passes if *any*
  branch is present; a branch-pinned target passes only when *that* branch is present. This kills the
  multiple-branch false negative and precisely validates a pinned nightly.
- **Settings surfaces one dropdown entry per installed branch.** `FlatpakApplicationDiscovery` returns
  the bare app id when a single branch is installed (unchanged behaviour) and branch-qualified refs
  (e.g. `net.pcsx2.PCSX2//stable`, `net.pcsx2.PCSX2//beta`) when several are, so the user explicitly
  picks stable vs nightly. The editable ComboBox is unchanged — the ref strings flow through as items.

## 2026-08-09 — Gamepad Hotkeys is a dedicated overlay wrapping the same settings view model (M40)

The Desktop **Settings › Hotkeys** section (a per-emulator × per-action matrix with Apply-to-all,
Install-Steam-template, per-emulator Apply/Preview/Revert, and the hold-Select controller mapping) had
no counterpart in Gamepad mode: `GamepadSettingsViewModel` projects Desktop settings into a flat list
of controller rows, and a matrix does not survive that projection (the same reason Themes is a
dedicated gallery page rather than rows). A controller-only user could see every other settings
section on the couch but not the one that most needs a controller.

Decision: a **dedicated `GamepadOverlayKind.Hotkeys` overlay** driven by `MainViewModel.GamepadOverlay`,
built exactly like the other gamepad overlays. `GamepadHotkeysViewModel` **wraps the same
`EmulatorSettingsViewModel` instance** the gamepad Settings projection already holds — reusing its
`HotkeyEmulators` rows, `ApplyAllHotkeysCommand`, `InstallSteamTemplateCommand`, `SteamTemplateStatus`
and `HotkeySchemeSummary` verbatim — and adds *only* a linear D-pad focus model (the same pattern as
`GamepadScraperViewModel` over `GameScraperViewModel`). No hotkey logic, no config files, and no engine
are duplicated; nothing is re-read when the overlay opens; and it is Gamepad-native, never a Desktop
window hand-off.

- **Entry point is the gamepad Settings General row, not a projected section or a System-Menu peer.**
  `SettingsSection.Hotkeys` is excluded from the gamepad Settings rail (alongside Emulators/Themes/About
  — a latent gap since PR #71 added the Desktop section but not a gamepad projection), and a
  General-section "Emulator hotkeys" action row opens the overlay through a `Func<Task>` callback
  (mirroring the existing `applyTheme` callback). B returns to Settings, whose projection
  `OpenGamepadOverlay` deliberately leaves intact — the same "open bespoke overlay B from overlay A,
  B backs to A" contract the scraper uses from the game Actions menu. The row is excluded from the
  Desktop↔Gamepad `general.*` field parity because Desktop's equivalents are the `hotkeys.*` controls
  *inside* its own section.
- **Preview was dropped from the hotkey UI entirely — both surfaces expose only Apply / Revert.** It
  started as a controller simplification (the gamepad surface never had it) and then came off Desktop
  too: Preview was a dry run that surfaced only a change *count* ("N settings would change"), not a diff,
  and the same dry-run that runs when the panel opens already shows each action's support and the
  applied / not-applied status — so it added almost nothing on screen, while Apply is fully backed-up and
  revertible. The underlying dry-run (`configurator.Preview`) stays — it's what populates the matrix
  cells and the initial status — but the user-facing button/command, `HotkeySettingsContext.PreviewAsync`,
  and the coordinator's `Operation.Preview` were removed.
- **Focus flags live on the shared row, not a new row type.** `HotkeyEmulatorRowViewModel` gained
  `IsApplyFocused`/`IsRevertFocused` observable flags the Desktop matrix ignores, so the one row type
  carries the D-pad ring on the couch and the Apply/Revert buttons on the desktop — reuse over a
  parallel VM, as with `ScraperFieldRowViewModel.IsFocused`.
- **Non-operable emulators show in the matrix but are never focus targets.** A row whose config dir
  can't be resolved (`CanOperate == false`) still renders its cells and status (so the user reads
  *why*), but offers no focusable Apply/Revert — matching the Desktop buttons being disabled there.
- **The matrix scrolls the focused row into view** (`RevealGamepadHotkeysFocus`, mirroring the
  scraper's reveal): seven emulator rows overflow the viewport at 1280×800, so without scroll-to-focus
  the ring on Azahar/RPCS3 would walk off-screen. The two global actions sit above the scroll and are
  always visible, so only per-emulator buttons need revealing; the `.focused` class carries the ring
  and the view model routes A directly, so no keyboard focus is taken (no Fluent adorner to suppress).

## 2026-08-09 — RetroArch apply clears controller hotkey bindings so the D-pad stops misfiring (M40)

Real-hardware follow-up: after applying the keyboard scheme, a Deck user's **D-pad left/right started
changing the save-state slot mid-game** (and screenshot/pause/fps fired off face buttons). Root cause is
a RetroArch design constraint, not a bad token: RetroArch has a **single hotkey-enable gate shared by
keyboard and controller** (`input_enable_hotkey` / `_btn`). The keyboard scheme deliberately leaves it
**off** (unset ⇒ hotkeys always active) so a bare Steam-Input key like `f2` fires without a modifier — but
"off" also un-gates every *controller* button RetroArch has bound as a hotkey. A stock pad autoconfig
lands those on game-facing buttons (verified in the user's `retroarch.cfg`:
`input_state_slot_decrease_btn = "h0left"`, `input_state_slot_increase_btn = "h0right"`,
`input_screenshot_btn = "0"`, `input_pause_toggle_btn = "1"`, `input_fps_toggle_btn = "2"`,
`input_runahead_toggle_btn = "h0up"`, plus trigger-axis binds `input_rewind_axis`/`input_hold_fast_forward_axis`),
so bare presses fire them during play.

The gate can't be "keyboard only" — it's one switch — so re-introducing a modifier would break the
bare-keyboard scheme. **Decision (option A1): the RetroArch configurator clears the controller bindings
instead.** On apply it nul's both `<control>_btn` and `<control>_axis` for the scheme's own actions
(so a leftover trigger-axis can't rewind/fast-forward alongside the keyboard key), for
`input_enable_hotkey` (nul keeps the gate off), and for the hotkeys a stock autoconfig commonly puts on
game buttons: `state_slot_increase`/`decrease`, `screenshot`, `pause_toggle`, `fps_toggle`,
`runahead_toggle`, `toggle_fast_forward`. Game inputs (`input_playerN_*`) are never touched, so the pad
still plays; clearing only rewrites keys that exist and hold a non-`nul` value, and everything is backed
up and revertible. Net model: **on RetroArch the controller is game-input only and the keyboard (via
Steam Input) is the sole hotkey path** — consistent with the rest of M40.

Bounds and follow-ups: it clears by hotkey *name*, not by "is this button a game input", so it also
silences one of these hotkeys bound to a non-game button — acceptable under M40's controller-is-game-only
model and reversible. Two things a bare files check can't settle and are left for hardware: (1) if the
pad's **autoconfig profile** re-applies these binds on reconnect, editing `retroarch.cfg` won't be
durable — the fix would then belong in the autoconfig, a larger scope; (2) the bundled Steam Input
template covers rewind/ff/save/load/close but **not slot ±**, so after this a controller has no
slot-change unless the template is extended (deferred). RetroArch-specific: the other emulators use
explicit keyboard tokens and have no shared always-on gate, so none of them need this.

## 2026-08-09 — Platforms are grouped by manufacturer, oldest-manufacturer-first

The navigation list had no ordering logic — `KnownSystems.All` was in the order systems were added
during development (PlayStation family, then a scattered mix), so Nintendo appeared in three separate
clumps and handhelds interleaved arbitrarily. Surveying comparable frontends (ES-DE's `systemsSortMode`,
LaunchBox's platform categories, OpenEmu's maker-prefixed alphabetical list, NeoStation) the common,
predictable axis is **manufacturer**. Chosen over hardware-type (console/handheld/arcade) or a
user-configurable sort mode because EmuShelf ships a fixed ~15-system set, so one sensible default beats
a settings surface. A per-mode picker can be layered on later — the metadata added here already supports it.

- **`GameSystem` gains a `Manufacturer` grouping key** (optional trailing param, default `""` = ungrouped,
  so the two direct `new GameSystem(...)` test constructions and any future ones stay source-compatible).
- **`KnownSystems.All` is authored in display order:** groups run Nintendo → Sega → Sony → Arcade
  (each group ordered by its *oldest* system, so the heritage reads chronologically), and within a group
  systems are oldest-first with handhelds interleaved by year. This authored order *is* the navigation
  order; ids are unchanged so libraries are untouched. Leading with Nintendo rather than the PS-centric
  original is deliberate (chronology is the "logic"); flipping to Sony-first is a one-block move.
- **Desktop renders a manufacturer header above the first *visible* system of each group.** Rather than
  change `NavigationSystems` (kept as `ObservableCollection<GameSystem>` — selection binds to it and a test
  asserts it equals `KnownSystems.All`), the VM exposes `GroupLeaderSystemIds` (recomputed on every
  nav refresh, a fresh set each time so bindings re-fire) and a multi-value converter shows the header only
  for leaders when the sidebar is expanded. The row hover/selection fill moved from the whole `ListBoxItem`
  onto an inner `Border.nav-row`, so the header sharing the item is never highlighted.
- **Gamepad mode inherits the reorder for free** — the horizontal rail and LB/RB platform cycling both walk
  the same list, so they group by manufacturer without new couch UI (the rail stays icon-only by design).
## 2026-08-09 — Gamepad collections: Recently-* become a Sort row, not places; Collections overlay removed

The controller "Collections" overlay was a dead end: buried two levels deep (Start → Collections), and
picking Recently Played/Added dropped the couch into a `LibraryScope` that is not a stop on the LB/RB rail,
so nothing highlighted and B could not get back. The fix reframes the concept — Recently Played/Added are
an *order*, not a *place*.

- **Recently Played / Recently Added are couch sort orders, not scopes.** The Gamepad Start menu gains a
  "Sort by" row — Recently played / Recently added / Title A–Z / Rating — that drives the existing global
  `SortColumn`/`SortDescending`. Couch sorting therefore reuses the desktop's sort path and persists across
  restarts for free (both already round-trip through `LibraryViewSettings`). Cards set the sort state
  *directly* — each carrying its own direction (recency/rating descending, title ascending) — rather than
  via the list-header `SortByCommand`, whose toggle semantics would force ascending and float
  never-played / unrated games to the top.
- **The Sort row reuses the View-mode picker verbatim.** Same `gamepad-viewmode-row` / `gamepad-viewmode-card`
  styles and brushes — four cards across one row, so Up/Down move between menu sections exactly like the
  View-mode row and Left/Right step the sorts. Labels are shortened (Played / Added / A–Z / Rating; full
  name in the tooltip) so all four fit the row; a 2×2 was tried first but made Up/Down ambiguous — it
  skipped the whole grid. No new style, brush, or background was added.
- **Direction is reversible on the couch too.** Picking a field applies its sensible default (recency /
  rating descending, title A→Z); **A while the sort row is focused flips ascending/descending** — the
  desktop list's ▲/▼ has no other couch analogue. The sort header shows a direction arrow plus a
  plain-language label ("↓ Newest first", "↑ A to Z"), and an "A Reverse" affordance appears there while
  the row owns focus.
- **The `GamepadOverlayKind.Collections` overlay is deleted.** With Recently-* demoted to sort, the couch
  has no Collections drill-in; the Start menu drops the "Collections" entry. The Desktop sidebar keeps its
  Recently Added/Played entries unchanged — this is a gamepad-only change.
- **Menu focus grew from one selector row to two.** The single `IsGamepadViewModeRowFocused` bool became a
  `GamepadMenuFocusRegion` enum (ViewMode / Sort / Options); the old bool stays as a computed alias so
  existing bindings and tests keep working. Up/Down walk the regions, Left/Right pick within the focused
  row and apply live, A is inert on either selector row.
- **Couch never lands in a scope or sort it can't show.** Entering gamepad mode (or restoring into it)
  coerces a leftover `RecentlyAdded`/`RecentlyPlayed` scope to All Games, and any non-couch sort column
  (e.g. Console/Genre set on the desktop) to Recently played — so the rail always highlights a stop, a sort
  card is always selected, and the Sort header stays honest.
- Future custom user collections get the rail as their home — extra stops after the systems — rather than
  reviving the overlay.

## 2026-08-09 — RVZ junk-padding regeneration fixed; Wii/GameCube achievement hashes recomputed

Almost no .rvz Wii games were being matched to RetroAchievements (and a few GameCube titles, e.g. Mario
Power Tennis), while .iso Wii games worked. Root cause was in the RVZ reader's lagged-Fibonacci "junk"
generator (`RvzLaggedFibonacciGenerator` in [NintendoDiscImageReader.cs](src/EmuShelf.Integrations/Achievements/NintendoDiscImageReader.cs)),
which reconstructs the pseudo-random padding Dolphin strips out of RVZ.

- **The generator was missing Dolphin's per-word `Initialize` transform** — `x = swap32((x & 0xFF00FFFF) |
  ((x >> 2) & 0x00FF0000))` applied to the whole buffer after seed extension — **and extracted bytes
  big-endian instead of little-endian** (host order, matching Dolphin's `reinterpret_cast<u8*>`). Both are
  now corrected. An earlier audit fix (`>>18`→`>>16`) had patched only one byte lane and left the real
  defect. Verified by reconstructing full Ghost Squad + Mario Kart Wii (Wii) and Luigi's Mansion + Mario
  Power Tennis (GameCube) discs **byte-for-byte** against DolphinTool-produced ISOs.
- **Why Wii was hit hard but GameCube barely:** the Wii hash reads 1024 partition clusters (~32 MiB), which
  for smaller games routinely includes junk padding; GameCube hashes only the disc header + apploader + DOL,
  which is real data unless a title's hashed region happens to abut junk (measured: 1 of 23 local GC titles).
- **Both algorithm versions were bumped** (`gamecube-v3`, `wii-v4`) and both dropped from the legacy
  `disc-v2` compatibility set, so the corrected reader recomputes every Wii/GameCube hash the broken reader
  had already stored — otherwise the wrong hashes would be reused forever and the games would stay
  unidentified. PlayStation stays compatible with the legacy version.
- **Added a regression test that emits an actual RVZ junk segment.** The pre-existing synthetic RVZ builders
  only exercised literal packing, never the junk path — which is why this shipped twice. The new test pins
  the generator's output (cross-checked against Dolphin) so a future byte-order/transform regression fails.

## 2026-08-09 — Cover picker: fall back to the proxied preview the user can see

The web cover picker showed DuckDuckGo image results, but selecting one often failed with "That image
is no longer available" even though the thumbnail was plainly visible in the grid. The preview and the
selection were fetching two different addresses.

- **Selection now tries `[OriginalCandidate, ThumbnailCandidate]`, not the original alone.** The grid
  previews the search engine's proxied thumbnail (a stable CDN), while selection downloaded the
  full-resolution original from the *source* host — which routinely 404s, hotlink-blocks with a 403 or
  an HTML page, exceeds the 8 MB cap, or serves an unsupported format. `DownloadFirstAsync` already
  returns the first candidate that yields an image, so the picker prefers the crisp original and falls
  back to the exact proxied preview on screen. "No longer available" now means *neither* address
  produced an image.
- **The user-driven web-artwork HttpClient sends a mainstream browser User-Agent, not `EmuShelf/1.0`.**
  Arbitrary image hosts and CDNs refuse an unknown agent, so a browser UA materially raises how often
  the full-resolution original is retrieved rather than the thumbnail fallback. Scoped to the
  picker/ScreenScraper-media client only; the automatic metadata client keeps its honest EmuShelf agent.

## 2026-08-09 — Region-free catalog serials: keep every regional entry, disambiguate by filename

A region-free 3DS cartridge (late Pokémon titles such as Ultra Sun/Moon) carries one NCCH product
code for *every* regional dump, so a single serial keys several No-Intro DAT entries whose only
difference is the region and the localized name. `LibretroDatCatalog` collapsed a shared key to the
lowest `PreferenceScore` (title length, plus Beta/Proto/Rev penalties). The Korean No-Intro name has
no language suffix, so it was the shortest and always won — a European `CTR-P-A2BA` dump was labelled
"Pocket Monsters Ultra Moon (Korea)".

- **The catalog index keeps all entries per key, not just the preferred one.** `CatalogIndex` stores
  `IReadOnlyList<CatalogEntry>` per (kind, key); the region is already parsed into `CatalogEntry.Region`
  and was simply being discarded by the old collapse. The region-agnostic pick is unchanged — the same
  `PreferenceScore`, ties broken by first-seen order — so `Entries`, the 3-arg `TryGetValue`, and every
  system whose key is unique behave exactly as before.
- **`IGameMetadataCatalog.FindMatchAsync` gained an optional `regionHint`.** The coordinator passes the
  game's filename (which carries the No-Intro `(Europe)` tag); when a key is shared, the entry whose
  region the filename advertises wins, else the preferred entry is returned. The serial stays the only
  3DS catalogue key — this fixes a *wrong* match without dropping the key (contrast the DS decision to
  drop the ambiguous game code entirely).
- **Region matching is a token intersection, needing no maintained region vocabulary.** Both the DAT
  region and the filename's parenthetical tags are split on `, / & +` and upper-cased; a spelled-out
  DAT region ("Europe", "Korea") never collides with a two-letter language code ("En", "Ko"), so a
  `(En,Ja,Fr,…,Ko)` language list is not mistaken for a Korean region.

## 2026-08-09 — Normalized scraped titles show across the whole library

The normalized title scraped from a metadata provider (ScreenScraper's region-preferred game name,
e.g. "Prince of Persia: The Sands of Time") was already read in bulk (`GetProviderTitles`) but only
overlaid on the gamepad spotlight. It now feeds a view-agnostic `GameViewModel.DisplayTitle` used by
the grid, list, gamepad tile, spotlight, placeholder monogram, search, and sort — so a file named
"Prince of Persia - The Sands of Time (USA) (En,Fr,Es).gba" reads as the retail title everywhere,
while the raw filename stays visible on the list row's path line.

The user-facing **status surface** was aligned to the same name: launch/remove/cover/texture/disc/
achievement toasts, the `UnavailableLaunchStatus`, the remove-confirmation and cover-picker dialogs,
and the achievement-details title all name the game by its `DisplayTitle`. The launch flow threads the
display name through the parts that only have the `Game` model: `SyncSavesForLaunchAsync`, and Core's
`IEmulatorLaunchService.LaunchAsync`, which gained an optional `displayName` (defaulting to
`Game.Title`) so the completion (`"{name} finished"`) and every `"Cannot launch {name}: …"` message it
produces match the library too — otherwise the terminal launch toast would be the only stop still
showing the raw filename. Two surfaces deliberately keep the raw `Title`: **diagnostic logs**
(`_logger.*`, keyed to the real record + `game.Id` for debugging) and the **rename box seed**
(`DraftTitle`) plus its no-op comparison — seeding the editor with the scraped name would bake it in as
a `User` rename on an unchanged save. The ScreenScraper search seed also stays raw (it is the identity
being searched, not a label).

- **A deliberate user rename wins over the scraped title** (its `TitleOrigin` is `User`). Showing the
  provider name over a name the user explicitly typed would look like the rename did nothing.
- **The underlying `Game.Title` record is untouched.** `DisplayTitle` is a presentation overlay, so no
  migration/write is needed and clearing a scrape reverts the shown name for free. (This differs from
  the local-DAT catalog title, which `TryApplyCatalogTitle` does persist into `Game.Title`.)
- **Search matches the displayed title OR the original title**, so scraping never makes a game
  unfindable by the name it had before.

## 2026-08-09 — The RPCS3 library sync must enrich metadata like every other import

PlayStation 3 games enter the library through exactly one path — the RPCS3 `games.yml` sync
(`SyncRpcs3LibraryFromSettingsAsync`). All four file/folder import paths deliberately refuse PS3
("imported only from RPCS3"), so that sync is the sole importer for the platform. It was, however, the
only import path that never called `MaybeStartMetadataForImportAsync`; newly synced PS3 games were
therefore never handed to the opt-in title/cover enrichment (nor RetroAchievements identification) and
stayed cover-less regardless of provider — the live library had 53 PS3 games with valid serials, full
covers on every other system, and zero enrichment attempts for PS3. The sync now makes that call with
the reconcile result's `AddedGameIds`.

- **Only `AddedGameIds` (genuinely inserted rows) is enriched, never updated ones.**
  `ReconcileExternalLibrary` appends to `addedIds` only on insert; a re-sync reports existing entries
  as updated, so an empty added-list makes the enrichment call a no-op — it cannot re-download the
  whole library on every sync.
- **Existing cover-less PS3 entries are not backfilled by a re-sync** (they are "updated", not
  "added"). The one-time **Fetch all metadata** action is the route for those, since it selects on
  missing cover/title rather than on import recency.
- **Any future `IExternalLibrarySource` must trigger enrichment the same way.** The enrichment
  kickoff lives at the app import-orchestration layer, not inside `ExternalLibrarySyncService`, so it
  is easy to forget for a new source. RPCS3 is currently the only such source.

## 2026-08-09 — Wii WAD (WiiWare / Virtual Console / channels) import support

`.wad` is now an importable Wii extension alongside the disc containers. A WAD is an installable
title package, not a disc image, so it needs its own reader and deliberately different handling per
subsystem.

- **Recognition is structural, never extension-only.** `WiiWadReader` validates the WAD header
  (`header_size == 0x20`, `"Is"` installable type) and that the cert/ticket/TMD/data sections fit
  the 0x40-aligned layout within the file, so a Doom IWAD/PWAD or any other file that merely borrows
  the extension is rejected. A WAD has no disc-header magic, so it never goes through
  `NintendoDiscDetector`; `.wad` is added only to Wii's extension set and to no other system, so it
  routes to Wii and never to GameCube.
- **Identity comes from the TMD title id.** The four-character game code in the low word of the
  64-bit title id (e.g. `WB4E`) is emitted as the primary `DiscId` identifier — the same id-addressed
  key the existing `GameTdbArtworkProvider` already serves Wii covers by — with the full sixteen-hex
  title id retained as secondary `TitleId` evidence. This mirrors the 3DS route: WiiWare/VC titles
  are not in the Wii Redump DAT, so there is no catalogue title match; the cover resolves by id and
  the title falls back to the filename. A system title / IOS WAD carries no printable code and yields
  only the title id (or nothing), never a guess.
- **RetroAchievements: WADs are hashed and matched** (WiiWare and Virtual Console titles have RA
  sets, e.g. game 7739). rcheevos routes `.wad` through `rc_hash_wii` → `rc_hash_wiiware`, which is
  **not** the disc reader: it MD5s the TMD followed by the leading bytes of each content section.
  Crucially the content is hashed **encrypted, as stored** — no title-key or Wii common-key
  decryption — so no Nintendo key material is embedded. `WiiWareHasher` is a line-for-line port, and
  `GetAlgorithmVersion` returns a distinct `rcheevos-wiiware-v1` for a `.wad` so a disc hash and a WAD
  hash for the same system are never confused. rcheevos ships **no** WAD test vector, so parity is
  proven a different way than the disc hashers (which pinned an upstream constant): an independent
  Python transcription of `rc_hash_wiiware` hashes a synthetic WAD, and the test pins both that MD5
  and the fixture's SHA-256 so the two implementations are provably hashing identical bytes. A `.wad`
  that is not an installable ("Is") WAD is rejected (`UnsupportedFormat`), never given a bogus hash.
- **ScreenScraper** keeps `Profile("wii", ".iso")` unchanged: a WAD is not the Redump whole-file
  dump ScreenScraper fingerprints, so (like a 3DS `.cia`) it name-searches instead of ROM-hashing.
- **Launching** needs no change — Dolphin already boots a WAD through the existing `-b -e` template.
- **Deliberate non-goals.** Cloud **save sync** stays disc-only: WiiWare/VC/channel saves live under
  the NAND `title/00010001` group, which the 2026-07-28 Dolphin-save decision already excludes from
  sync along with the rest of the NAND. **Texture packs** are unaffected: Dolphin keys texture
  folders by 6- or 3-character disc ids, not the 4-character WAD code, so there is nothing new to
  inventory. Both would be separate, independently verified efforts.

## 2026-08-09 — Desktop and Gamepad Settings share one section structure

Gamepad Settings had drifted from Desktop: it dropped the Emulators, Hotkeys, and About sections and
relocated their controller-relevant actions into General, so General became a catch-all (empty
platforms + metadata *plus* PS3 sync from Emulators, a Hotkeys overlay entry, and the update
actions from About). The enforced "parity" was only **field-level** — every mutating control that
appears in *both* modes shares a stable id — not **structural**, so nothing stopped the section sets
from diverging. An audit reorganized both surfaces around one rule.

**Both modes present the same section list, in the same order. Only Themes is special** (appearance
is not part of the settings model, so it stays a gamepad gallery page appended after the projected
sections, matching the earlier Themes decision). Everything a controller can't project as flat rows
still appears as its own rail section rather than leaking into General:

- **Emulators is now a gamepad section** projecting per-platform *library* actions from the same
  `EmulatorSettingsRowViewModel` rows Desktop uses — PS3 `Sync RPCS3 library` (the one import that
  "Rescan all consoles" skips), per-platform rescan, and remembered-folder add/forget — reusing the
  same commands and stable field ids (`emulators.{id}.sync`, `.rescan`, `.add-folder`). Editing an
  emulator's executable, arguments, and RetroArch core stays Desktop-only for now, as the section
  description says.
  This removed the bespoke `general.sync-rpcs3` entry and the now-dead `SyncRpcs3LibraryGeneralCommand`
  / `HasRpcs3LibrarySync` on `EmulatorSettingsViewModel` — the gamepad row drives the per-emulator
  command directly, so there is one RPCS3-sync path, not two.
- **Hotkeys is a gamepad section** whose row opens the controller-native `GamepadHotkeysViewModel`
  overlay (same B-returns-to-Settings contract as the M40 hotkeys entry), replacing the General row.
- **About is a gamepad section** projecting the read-only version/commit rows and the in-place update
  actions (`about.check-updates` / `about.install-update`), replacing the General update rows. Version
  and commit are now visible on a controller for the first time.
- **General is renamed "Library"** in both modes (the `SettingsSection.General` enum member and its
  `general.*` field ids are unchanged for settings/parity stability; only the display label changed).
  After the moves it holds exactly what Desktop's does: empty platforms, metadata, rescan-all, data
  folder.

The executable field-parity test is unchanged and still passes: the removed General rows were all
already `ExcludeFromParity`, and Emulators/Hotkeys/About sit outside the prefix-scoped parity
comparison (like Themes) because the two surfaces intentionally expose different subsets there
(Desktop edits paths; the couch runs library actions). RetroAchievements + ScreenScraper stay
separate peers rather than nesting under an "Accounts" parent — nine rail entries is not enough to
justify the extra hierarchy.

Because the rail can now hold up to nine entries, it is wrapped in a hidden-scrollbar `ScrollViewer`
whose selected section is brought into view from code-behind (`RevealSelectedGamepadSection`), so it
never overflows the column or clips About behind Save at 1280×720 or ×800; nav button height dropped
58→50 so the common (7-section) case still fits without scrolling. Reviewed real renders of the
Library, Emulators, and About sections at 1280×800 confirmed the layout; the gamepad-settings
snapshot test now also captures the Emulators and About sections.
## 2026-08-09 — Self-update relaunches through Steam so the controller survives (Windows + macOS)

Reported on Windows: after installing an update while in Gamepad mode (launched from Steam), the
gamepad was dead until the app was quit and restarted from Steam. Cause: the Windows applier's `.cmd`
helper relaunched the bare `EmuShelf.exe` (`start "" "<exe>"`). The documented Gamepad setup adds
EmuShelf to Steam as a **non-Steam game**, and Steam Input attaches only to the process Steam launches.
A self-started binary is outside Steam's launched-game tree, so Steam Input never attaches and the
controller is unresponsive — restarting from Steam is what fixed it. The macOS applier has the same
escape (`open` on the `.app`), so the fix is applied there too rather than left as a latent bug.

The fix mirrors what the AppImage applier already does on SteamOS (same-PID `execv`, so "Steam never
registers the game as stopped"). Windows and macOS can't keep the PID — the locked `.exe` / swapped
bundle forces a fresh process — so the helper instead **re-enters Steam's launch path**: when
`SteamGameId` is present in the environment (Steam exports it for every game it starts, including
non-Steam shortcuts, and its value is exactly the `steam://rungameid/<id>` token), the relaunch becomes
`start "" "steam://rungameid/<id>"` (Windows) / `open "steam://rungameid/<id>"` (macOS) rather than the
app binary. That reapplies Steam Input and the shortcut's own launch options (`--gamepad-ui` included),
which also removes the incidental loss of that flag on relaunch. A run with no `SteamGameId` (a plain
shortcut or a dev run) relaunches the executable/bundle directly, unchanged.

The target selection is one shared helper, `UpdateRelaunch.ResolveTarget`, so the two appliers can't
diverge; it gates on `ulong.TryParse(...) && id != 0` so an empty or malformed value falls back to the
app binary, and it is unit-tested. Linux needs nothing — its same-PID re-exec never leaves Steam.

## 2026-08-10 — Dolphin's config directory is a separate XDG tree on Linux, not `<userDir>/Config`

Real-hardware retest on a Steam Deck: the M40 hotkey applier reported Dolphin's Flatpak folder
`~/.var/app/org.DolphinEmu.dolphin-emu/data/dolphin-emu` "isn't Dolphin's user directory (no
Config/Dolphin.ini there)". It was looking in the wrong tree. Dolphin's `SetUserDirectory` (UICommon)
splits its user directory on non-Apple POSIX: the **data** user dir is `$XDG_DATA_HOME/dolphin-emu`
(saves, `Load/`), but it explicitly overrides `D_CONFIG_IDX` to `$XDG_CONFIG_HOME/dolphin-emu`, which
holds `Dolphin.ini`/`Hotkeys.ini`/`GFX.ini` **directly** — there is no `Config/` level. The Flatpak
sandbox reflects this (`org.DolphinEmu.dolphin-emu`'s manifest sets no XDG override and no `--persist`),
so config is `.var/app/<id>/config/dolphin-emu` and data is `.var/app/<id>/data/dolphin-emu`. On
Windows, macOS and portable installs config *is* `<userDir>/Config`, which is why the single assumption
"append `Config` to the user dir" had passed Windows verification.

`EmulatorUserDirectories.FindDolphin` (the data user dir) stays as-is; a new sibling
`FindDolphinConfigDirectory` resolves the config dir per platform with a candidate list that mirrors
`FindDolphin`'s precedence — Flatpak `config/dolphin-emu`; else portable `User/Config`, Windows
`Documents\Dolphin Emulator\Config`, native Linux `$XDG_CONFIG_HOME/dolphin-emu` (absolute var or
`~/.config`), macOS `…/Dolphin/Config`. `HotkeyProviderRegistry` passes that to
`DolphinHotkeyConfigurator`, which now treats its argument as the config directory outright (the
`Dolphin.ini` sanity gate and lazy `Hotkeys.ini` creation from the previous entry are unchanged, just
rooted correctly).

Why only hotkeys surfaced this: saves and textures read `Config/Dolphin.ini` **only to follow a
relocated path**, and fall back to defaults that live under the data dir when it is absent — which is
correct on Flatpak, so they worked by luck of the fallback. The hotkey applier has no default location
to fall back to (it must find and write `Hotkeys.ini`), so it is the canary. `DolphinTextureRootResolver`
and `DolphinSaveLocationProvider` still read the wrong `<dataDir>/Config/Dolphin.ini` on Linux and would
silently ignore a user's *moved* Load/save folder there; that latent bug is tracked separately.

## 2026-08-10 — Dolphin texture and save resolvers read the split config tree too

Follow-up to the entry above: `DolphinTextureRootResolver` and `DolphinSaveLocationProvider` now read
`Dolphin.ini` from Dolphin's real config directory instead of `<dataDir>/Config`. Before this, a Linux
or Flatpak user who *relocated* their Load or save folder inside Dolphin had the override silently
ignored — the resolvers read a `Dolphin.ini` that isn't there, so they fell back to the data-dir
defaults, which is right only until the folder actually moves.

The two resolvers reach the config directory differently, because they sit at different layers:

- **Texture resolver.** `DolphinTextureRootResolver` gains an optional `configDirectory` argument that
  defaults to `<userDir>/Config` (so Windows, macOS, portable, and every existing test are unchanged).
  `TexturePackProviderRegistry` resolves it with the shared `FindDolphinConfigDirectory` and threads it
  in. The user (data) directory is still needed and still passed — it supplies the `<User>/Load` default
  and is the base for a *relative* `LoadPath`, which Dolphin resolves against the user dir, not the
  config dir.
- **Save provider.** `DolphinSaveLocationProvider` can't call the static `FindDolphinConfigDirectory`:
  that helper only knows `(installationDirectory, isFlatpak)`, but the provider's effective directory
  can also come from a Settings override, a `-u`/`--user` launch argument, or portable mode — cases the
  helper doesn't model. It instead resolves the config dir internally (`GetConfigDirectory`), mirroring
  its own user-directory resolution: an explicit directory (override/`-u`/portable) and the
  Windows/macOS defaults keep config at `<userDir>/Config`; Flatpak and native Linux use the separate
  XDG config tree (honouring an injected `xdgConfigHome`, else `~/.config`), with the pre-XDG unified
  `~/.dolphin-emu` layout kept on `<userDir>/Config`. Flatpak is checked before Windows/macOS, matching
  `GetUserDirectory`, so the config and data trees can never disagree about which install this is.

Tests assert the split directly: the texture resolver follows a `LoadPath` written into a *separate*
config directory (and still resolves a relative `LoadPath` against the data dir), and the save provider
reads a raw-card layout from the XDG/Flatpak config tree while ignoring a decoy `Dolphin.ini` planted in
the old `<dataDir>/Config` location.

## 2026-08-10 — The texture-loading status reads GFX.ini from the config tree too

A third consumer had the same `<dataDir>/Config` assumption: `DolphinTexturePackLoadingResolver` reads
`GFX.ini`'s `[Settings] HiresTextures` to tell Settings whether Dolphin will actually load the packs it
found. It read `<dataDir>/Config/GFX.ini`, so on Linux/Flatpak the status showed *Unknown* ("settings
file not found") even when the user had custom textures enabled in the real
`$XDG_CONFIG_HOME/dolphin-emu/GFX.ini`. It fails safe (Unknown, never a confident wrong answer) and
touches no save data, which is why it was a quieter symptom than the two resolvers above.

Unlike those, this one couldn't just be pointed at the config directory, because `GFX.ini` and the
per-game `GameSettings/` directory live in **different** trees on Linux: `GFX.ini` is config
(`D_CONFIG_IDX`), but `GameSettings/` is data (`D_USER_IDX + GameSettings`). The shared base
`IniTexturePackLoadingResolver` rooted both at one directory. It now takes an optional
`perGameRootDirectory` that defaults to the configuration directory — so DuckStation, PCSX2, PPSSPP,
and Azahar (which keep both under one directory) are unchanged — and `DolphinTexturePackLoadingResolver`
passes the resolved config directory for `GFX.ini` and the data user directory for `GameSettings`.
`TexturePackProviderRegistry` feeds it the same `FindDolphinConfigDirectory` value it already resolves
for the texture-root resolver. Tests assert `GFX.ini` is read from the config tree (ignoring a decoy in
`<dataDir>/Config`) and that a per-game override is found under the data tree's `GameSettings`, not the
config tree's.

## 2026-08-10 — ScreenScraper: more media kinds, per-system cover source, and opt-in video

ScreenScraper exposes far more media than the initial four kinds. Six were added to
`GameMediaKind` (appended, since the enum is persisted by ordinal in `SqliteGameDetailsStore`):
`TitleScreen` (`sstitle`), `BoxBack` (`box-2D-back`), `BoxSpine` (`box-2D-side`), `PhysicalMedia`
(`support-2D`, the cartridge/disc label), `PhysicalMediaTexture` (`support-texture`), and `Video`
(prefers `video-normalized` over `video`). The five image kinds join the default
`ScreenScraperSettings.MediaKinds`; the desktop batch window gained checkboxes for them and the
gamepad batch inherits them through the shared view-model defaults.

The cover source is now per-system instead of a hardcoded box front. `GameScrapeApplyRequest` carries
a `CoverKind` (default `BoxFront`, null disables projection), and `ScreenScraperMetadataMapper.CoverKindFor`
returns `TitleScreen` for arcade and `BoxFront` otherwise — computed once in the preview service and
carried on the preview. Arcade box art is nearly nonexistent and the built-in arcade cover already
prefers the Libretro title thumbnail, so a scraped `box-2D` was overwriting a good title-screen cover;
now the scraped title screen becomes the arcade cover instead. User-set covers stay protected by the
existing `TryApplyDownloadedCover` origin guard; box art is still stored, just not used as the cover.

Video is **opt-in and off by default**: it is absent from the default `MediaKinds`, so nothing
downloads video until a user adds `GameMediaKind.Video` to their settings. There is no in-app player
yet — that is a later addition — so a video row in the single-game scraper shows text instead of a
decoded thumbnail (and skips the preview download). The image-only `RemoteArtworkDownloader` became
media-kind aware via a `RemoteMediaKind` on `ArtworkCandidate` (no interface change, so existing call
sites and test doubles are untouched): images keep the exact prior behavior (`image/*`, 8 MB cap,
PNG/JPEG/BMP/WEBP signatures); video allows `video/*`, a 64 MB cap, and an MP4 `ftyp` signature check.

## 2026-08-10 — Cover well follows CoverKind; list-view columns for the new artwork

Two follow-ups after adding the new ScreenScraper media kinds.

The scrape dialog's "cover safe-zone" was hardwired to the box front (`BoxArtRow`/`BoxArtPreview`,
literal "Box art" / "No box art"). Once arcade started projecting the title screen to the cover, that
well misrepresented the outcome — it showed "No box art" while the title screen was what actually
became the cover. It now follows the preview's `CoverKind`: the row whose kind is the cover owns the
well, with a dynamic label (`CoverArtLabel`/`CoverArtEmptyText`) that reads "Title screen" for arcade
and "Box art" elsewhere. Renamed `BoxArt*`→`CoverArt*` and `GamepadScraperTargetKind.BoxArt`→`Cover`
for honest naming. Consoles are unchanged.

The desktop list view now surfaces all five new image kinds as opt-in presence columns (Title screen,
Box back, Box spine, Cartridge/disc, Cartridge/disc texture), matching the existing Screenshot/Fan
art/Logo pattern — hidden by default, sortable, fed by `GameDetailsProjection`. The five new
`GameDetailsProjection` flags are appended with defaults so existing constructions are unaffected.

The "Metadata" completeness score moves from n/5 to **n/6**: it adds the title screen — a core,
reliably-available type (and the arcade cover source) — but deliberately excludes the four sparse
secondary kinds (box back, box spine, cartridge/disc, cartridge/disc texture). Those are near-absent on
ScreenScraper, especially for arcade, so counting them made a full score unreachable and left a
well-scraped game reading as perpetually incomplete; they remain standalone presence columns instead.
Video is excluded from the list view entirely (opt-in scrape, no player, no presence column).
## 2026-08-10 — Gamepad Settings shows Themes in Desktop's slot (before About), not after it

The 2026-08-09 "one section structure" work left a real ordering bug: it excluded Themes from the
gamepad `Sections` list (correct — Themes is a gallery page, not projected rows) but then re-attached
it as a page **after** the projected sections. Because Desktop always makes About its last section,
"after the projected sections" meant *after About*, so the couch rail and LB/RB paging read
`… Texture Packs, About, Themes` while Desktop reads `… Texture Packs, Themes, About`. The stated goal
of that commit — both surfaces present the same sections in the same order — was met for every entry
except the one it special-cased. No test caught it because the only navigation test asserted the buggy
"paging Down lands on Themes last" behavior.

Fix: the gamepad now derives an ordered **page list** (`GamepadSettingsViewModel.Pages`) that splices
Themes back into Desktop's slot — immediately before About — instead of appending it. `Pages` is
`Sections` when there are no theme choices, otherwise `Sections` with `Themes` inserted right before
`About` (falling back to the end if About is somehow absent, though Desktop always emits it last).
`MoveSection`/`CurrentPageIndex`/`SelectPage` page over `Pages`; `Sections` keeps its narrower meaning
(the projected-row sections) for row building and the `SelectSection` guard. The hardcoded rail markup
in `MainWindow.axaml` was reordered to match: the Themes button is now the second-to-last row (Row 7,
shown only when `ShowThemes`) and About the always-visible last row (Row 8). Because Themes is hidden
when there are no theme choices, its now-collapsed Auto row leaves About in the same visual position, so
the gamepad-settings visual snapshots (none of which seed theme choices) are unaffected.

Themes staying a dedicated gallery page rather than a projected `Sections` entry is unchanged from the
earlier appearance decision — only *where* that page sits in the navigation order was wrong.

The same audit also aligned one label the earlier commit left divergent: the gamepad Emulators section
projected the PS3 sync row as "Sync PlayStation 3 library" while Desktop's PS3 card button (same
`SyncFieldId`) reads "Sync RPCS3 library". Both were internally consistent — each surface's own header
supplies the missing half — but a control shared by field id should read the same on both, so the
gamepad row now uses Desktop's wording; the gamepad section's own "PlayStation 3" header still supplies
the platform.

## 2026-08-10 — Settings load re-merges ScreenScraper's catalogue-default lists

`ScreenScraperSettings.MediaKinds` and `.MetadataFields` are code-owned catalogue defaults — nothing in
the app edits them (the batch window's per-kind toggles build a *per-run* include set, not this list).
They are nonetheless serialized into the portable `settings.json` like every other public property, so a
file written by an older build froze the shorter lists that build shipped with. After the newer media
kinds landed (title screen, box back, box spine, cartridge/disc, and its texture — 2026-08-09) and users
auto-updated in place, `JsonSettingsService.Load()` deserialized the stale four-kind array
(`BoxFront, Screenshot, Wheel, Fanart`) and `SelectMedia` filtered every new kind out *before* it reached
the scraper — so the review dialog only ever offered the original four, even though the code and the
provider both had the rest. It was reported as a suspected bad merge; it was not (the commit was in the
running build), it was stale on-disk settings shadowing the new default.

Fix: `Load()` calls `ScreenScraperSettings.WithCatalogDefaultsEnsured()`, which appends any default kind/
field the persisted list is missing (preserving what the file already had, and returning the array
untouched when nothing is missing so an already-current file is a no-op). Because these two lists are not
user-editable, "ensure all defaults are present" is the whole contract — a future kind is picked up by
every existing install automatically. The heal runs on the load path only (so `Update()` persists the
merged set on its next write); the file is not rewritten just for being opened. The round-trip test was
relaxed accordingly: it now asserts the entries a file lists are preserved *and* the current defaults are
ensured, rather than faithful round-trip of an arbitrary subset. If these ever become a real user
preference (e.g. an opt-out list), that will need an explicit disabled-set rather than this allow-list.

The same load-path heal now also tolerates a hand-edited `"Scraping": null` / `"ScreenScraper": null` by
substituting defaults, so a malformed-but-valid-JSON edit can't throw an NRE out of `Load()` (whose
try/catch only covers `JsonException`/IO/permission errors, not a null property).

## 2026-08-10 — An explicitly selected scrape media row replaces the current art

The single-game scraper's "Replace values ScreenScraper already set" checkbox drove one shared
`GameMetadataApplyMode` for both metadata *and* media, so with it unchecked (fill-missing) a media kind
the game already had was skipped — even though the user had ticked that exact art row and could see the
new image in the dialog. Reported as "if I select art it should overwrite any pre-existing art," which is
right: ticking a media row is a direct per-item choice, unlike the bulk field-refresh policy the checkbox
governs.

`GameScrapeApplyRequest` gains `OverwriteExistingMedia`. The single-game scraper
(`GameScraperViewModel.ApplyAsync`) sets it true, so a selected media kind replaces the game's current
asset of that kind regardless of the checkbox (which now only affects text fields). Batch leaves it false,
so a fill-missing batch still skips games that already have the kind — batch has no per-item review, so
its checkbox stays the only overwrite control there. The change is media-only and does not touch metadata
precedence: user-owned art is still unreachable (its scraper row is disabled), and the detail store still
refuses to overwrite a user-owned or foreign-provider file on disk, so "replace" only ever swaps
provider/downloaded art — which is exactly what the cover projection already permitted (it overwrites a
`Downloaded` cover but never a `User` one). Cross-provider covers (e.g. a built-in-catalogue cover) were
already replaced on first scrape because they leave no detail-store asset to skip; this fixes the
re-scrape case, where the prior ScreenScraper asset was being skipped.

## 2026-08-12 — Gamepad confirmations use a standard two-button dialog

The controller-native confirmations (Remove, Switch-to-Desktop, Quit) reused the picker overlays'
generic single-option list: the chrome title posed the question, then one full-width button repeated the
same words as the menu item that opened it ("Switch to Desktop mode" → title "Switch to Desktop mode?" →
button "Switch to Desktop mode"), with dismissal only offered as a `B` footer hint. Reported as redundant
and un-standard — pressing a menu action and being asked again with identical text.

All three now render a self-contained, centred modal in a tighter 500px card (`.confirmation` on the
overlay Border): a centred title poses the question, a centred body explains the consequence, and an
equal-halves **[Cancel] [verb]** button bar (Cancel + "Remove"/"Switch"/"Quit") sits beneath it. This
replaced a first attempt that reused the picker layout — top-left body, bottom-right buttons, bottom-left
footer — which read as scattered/unaligned. The group is vertically centred as one unit rather than
pinning the title to the sheet's top-left. Left/Right walk the pair, A confirms the focused button, B
cancels; focus lands on the action so a deliberate pick still confirms in one press.

The focus ring is colour-coded to the controller prompts: the confirm half rings **green** (the A button),
the Cancel half rings **red** (the B button) — matching the "A Select / B Cancel" footer legend so the
ring communicates the outcome of pressing A on that button. Destructive confirms (Remove/Quit) keep red
label text as an extra warning. A `bool IsCancel` was added to `GamepadOverlayOptionViewModel` to drive the
Cancel ring; the buttons still bind the same `GamepadOverlayOptions` list the pickers use, so focus/activate
routing is unchanged. Redundant secondary "Press B to…" body lines and the Remove dialog's duplicate bold
heading were dropped now that the button + footer cover cancellation.
## 2026-08-12 — Rescan reads embedded evidence only for newly discovered entries

"Rescan all consoles" re-read every candidate file's embedded evidence on each run:
`RescanAsync` fed the whole scan selection into `PrepareImportEntriesAsync`, which opens each
file (disc serials, ISO/ROM headers) even for games already in the library. The database write
was already idempotent (`INSERT OR IGNORE` on `(SystemId, Path)`) and evidence persistence only
fills empty identifiers, so all that file I/O for known games was wasted — the dominant cost of a
rescan on a large library.

`RescanAsync` now loads the system's existing game paths once and, via `SelectUnimportedEntries`,
drops already-imported entries before preparing them, so a steady-state rescan of an unchanged
library opens no files. The recursive folder walk still enumerates everything (needed so
descriptor/playlist/multi-disc collapse in `SelectGameEntries` stays correct), and `SuppressedPaths`
still flow through unchanged so stale components are removed even when their descriptor was imported
earlier. Removals remain handled by the existing `UpdateAvailabilityAsync` stat pass. Existing rows
keep their titles/identifiers — a rescan adds new and drops missing rather than re-deriving.

This intentionally drops rescan's re-read of embedded evidence for already-imported games, which
`PersistImportEvidenceAsync` previously used to retry a transiently-failed identifier write. That
retry is redundant: a missing identifier already self-heals on demand elsewhere (texture-pack
matching, metadata scraping, ScreenScraper preview all re-extract when none is stored), and
re-adding the file still retries. Not worth re-reading every file on every rescan to cover it.

## 2026-08-10 — Couch layout is a three-way enum (Grid/Spotlight/Shelf), not a bool

Phase 1 of the physical-media shelf (`docs/couch-physical-media-shelf.md`) needed a third couch layout
beside the cover grid and the spotlight. The couch layout was a single `bool IsGamepadSpotlightView`
(grid when false, spotlight when true), threaded through ~30 view-model sites, the XAML picker/panels,
and tests. Rather than add a second parallel bool (which makes "both true" unrepresentable and every
call site test three states by hand), the source of truth is now
`MainViewModel.GamepadLayout` — a `GamepadLibraryLayout { Grid, Spotlight, Shelf }` observable.
`IsGamepadSpotlightView`, `IsGamepadShelfView`, and `IsGamepadGridLayout` are computed aliases over it,
so the many spotlight-only checks and their XAML bindings are untouched; only the handful of writes moved
to setting the enum. `ToggleGamepadViewCommand` stays a binary grid⇄spotlight flip (the shelf is reached
from the picker), so its existing tests are unaffected; the view-mode row's Left/Right now steps the
three tiles clamped instead of the old absolute grid/list pair.

Persistence keeps both keys for compatibility: `LibraryViewSettings` gained a by-name
`GamepadLayout` string (like `Scope`/`SortColumn`) and still writes the legacy `GamepadSpotlightView`
bool. Restore prefers the parsed layout name, falls back to the bool when the name is missing/unknown
(so a pre-Shelf settings file still opens into spotlight), then to the grid — and a `Grid` name plus a
true legacy bool resolves to spotlight, since an absent name deserializes to the record default "Grid".

The shelf view (Phase 1) is a passive horizontal `ListBox` (`GamepadShelfList`): `SelectedItem` tracks
`FocusedGame` and the list auto-scrolls it into view, exactly like the spotlight list, while the focused
cover's enlarge/dim emphasis is driven by `GameViewModel.IsFocused` (maintained centrally in
`OnFocusedGameChanged`), not the ListBox's own selection chrome. Its root is a `Border`, not a bare
`Panel`, so the spotlight snapshot test's "the backdrop is the only bare Panel among GamepadRoot's
children" assertion still holds. The 3D media models and right-stick rotation are later phases; Phase 1
renders flat covers on a calm monochrome background.

## 2026-08-10 — The couch shelf's 3D hero renders on the GPU (Silk.NET), not in Skia

Phase 2 of the physical-media shelf first shipped as a hand-rolled Skia software renderer, per the
feasibility research's "no OpenGL, no new native dependency" recommendation. That renderer is
reverted. The goal for the shelf hero is that a keep case reads as *plastic* — a broad soft highlight
sliding across the sleeve as the player turns it — and that requires a prefiltered environment
sampled per fragment at a roughness-dependent mip. A painter-sorted CPU rasteriser drawing textured
`DrawVertices` quads has no path to it: it can shade a face, but it cannot reflect a room. The
research had optimised for "can we draw a rotating box without new dependencies", which was the
wrong question — the box was never the hard part.

The renderer is a new project, `src/EmuShelf.Rendering`, rather than living in the app. It is
neither UI-framework code nor domain nor persistence, and keeping it out of `EmuShelf.App` buys the
property that matters: it takes a `Silk.NET` `GL` whose context *somebody else* made current plus a
framebuffer id to draw into, so the identical renderer serves both Avalonia's `OpenGlControlBase`
and a headless EGL context. `tools/EmuShelf.Rendering.Preview` uses the latter to render every shell
to PNG over Mesa's surfaceless platform, which is how the hero is looked at and tuned — the app
cannot be run from a headless checkout, and the previous phase's defects were exactly the kind that
only a picture reveals. Two shipped-quality bugs were caught this way: artwork projected onto the
*back* of a turned shell (a world-space normal compared against an object-space panel normal), and
mirrored back/spine panels (a hard-coded u axis per face instead of deriving it from the face).

Silk.NET is bindings only — no native payload, no window/GLFW dependency, entry points resolved from
Avalonia's loader. Shaders are written to the intersection of GLSL ES 3.00 and desktop GLSL 1.50
with only the `#version` header injected per backend, because Avalonia hands us ANGLE (GLES 3.0) on
Windows and a core profile on macOS; attribute locations are bound with `glBindAttribLocation`
rather than `layout(location=)`, which would demand GLSL 330. If the context cannot be brought up at
all, the control raises `InitializationFailed` and the shelf puts every game back on its flat cover
— a supported outcome, not an error.

Lighting is a **procedural** studio baked to a cubemap at load and re-baked per accent change, then
convolved into diffuse irradiance and a GGX-prefiltered specular chain, with Karis' analytic
environment BRDF instead of a lookup texture. A shipped HDR panorama would be a multi-megabyte asset
in an app whose whole premise is portability, and generating the room lets it pick up the focused
system's accent so the hero belongs to the shelf it sits on. The non-obvious part is that the
softboxes sit **in front of** the subject rather than above it: a flat vertical face reflects the
hemisphere in front of it, so the intuitive overhead rig puts every highlight where the shell can
never show one, and the case comes out looking matte. This is a beauty-dish-beside-the-camera
arrangement, and it is the difference between "a lit box" and "plastic".

Artwork is projected onto faces in **object space** rather than through the models' UVs. Two of the
three shells cannot carry a UV decal at all — the SNES cartridge's coordinates span -93 to 1.7, and
the GBA's label is packed rotated into a shared atlas — so a projected rectangle per face is the
only approach that serves every shell with one code path, and it survives a model being re-exported.
Each panel carries its own roughness, aspect fitting, and whether printed art flattens the moulding
beneath it: a cartridge label is a sticker laid over grooves and hides them, while a keep case's
sleeve sits under a curved clear cover whose curvature is exactly what makes it read as a case.

The shells are three CC BY 4.0 Sketchfab models rather than original geometry, which departs from
the design doc's "shell geometry must be wholly original" note. That note existed to keep OpenEmu's
work out of EmuShelf, and it still does — these are independently authored, and CC BY permits
redistribution given attribution, which `THIRD-PARTY-NOTICES.md` now carries. The game artwork their
authors photographed onto them (a Mortal Kombat sleeve, a Wario Kart label) is always painted over
at render time by a panel, so no third-party packaging is ever displayed.

One shell serves four consoles: PS2, PS3, GameCube and Wii all shipped in the same 135x190x14mm keep
case. PS1 and Dreamcast (jewel cases) and PSP (UMD) are genuinely different shapes and stay on flat
covers rather than borrowing a case that is not theirs.

## 2026-08-13 — Authored shelf media renders without cover art; GL receives transposed Numerics matrices

An authored physical medium is useful even before ScreenScraper supplies artwork, so the shelf hero
is gated only on focus, an available shell, and GPU support. A missing `CoverImage` now leaves the
renderer on its existing no-art path, which paints the label/sleeve panel with the system accent,
instead of replacing the entire medium with the flat missing-cover placeholder.

The renderer also uploads `System.Numerics` matrices with `transpose=false`. Numerics stores matrices
for row-vector multiplication while the GLSL shaders multiply column vectors; interpreting the same
memory as column-major supplies the transpose the shader needs. Passing `true` preserved the wrong
mathematical layout, reversed the product perspective so the far edge grew, and is not accepted by
the OpenGL ES path used through ANGLE on Windows.

The live hero surface is responsive rather than a fixed 560x360 island: it consumes the shelf's
available star row up to 1100x680 while the platform/title occupies a separate automatic row. Small
windows therefore shrink without losing the title, while a fullscreen couch layout gives the object
enough physical screen area to read as the focused item. Camera framing keeps a 16% margin because
correct perspective enlarges the near corner during combined yaw/pitch; the prior 7% margin could
clip that corner at the control edge.

Follow-up hardware testing removed the capped stretch surface: Avalonia arranged the 1100px-capped
child at the left of its wider Grid, so the camera was centered inside a render surface that was not
centered on the shelf. The GL control now takes the media host's exact width and height and is centered
explicitly; the cover strip keeps the same full-width viewport and fixed 410px slots, so its selected
slot and all neighbours remain on one horizontal centreline behind the hero. The camera margin is 30%
after controller testing found combined yaw/pitch poses that still projected a near corner outside 16%.

Only the SNES shell receives an additional 180-degree roll to put its authored top at canonical +Y;
its projected label bounds are rolled with it. GBA keeps its existing orientation because its contact
fingers already prove that its long bottom edge is correct, and the disc case was authored upright.

The GL viewport derives its device scale through `TopLevel.GetTopLevel(control)`, rather than casting
`VisualRoot`. An OpenGL control can be rooted through Avalonia's composition host, so that cast may
fail and silently select 1.0. On a scaled display the resulting viewport then covers only the
top-left portion of the actual framebuffer, making a correctly centred model appear far to the left.

## 2026-08-13 — The physical shelf is one metric scene with bounded media and continuous travel

The physical-media mode no longer composes a separately translated 2D cover strip with one 3D hero.
`MediaShelf3DControl` submits the focused game and three neighbours on each side to one renderer scene;
the old strip is retained only as the session-wide fallback when OpenGL initialization fails. Games on
systems without authored media use a procedural, closed, thin cover card in that same scene, rather than
silently changing back to an Avalonia image or borrowing an inaccurate generic cartridge/case.

`PhysicalMediaProfile` is the presentation contract: measured width/height/depth, canonical correction,
artwork slots, material variant, insertion-animation id and an optional small presentation correction.
Geometry is scaled into 190mm-reference shelf units, placed on one baseline and viewed through one fixed
22-degree product camera. Horizontal centres come from the adjacent profiles' measured widths plus a
constant gap. Focus may lift an item slightly but never changes its scale, so selecting a GBA cartridge
cannot make it grow into a keep case. PS2, GameCube and Wii initially use 135x190x14mm profiles; PS3 uses
the shorter 135x171x14mm Blu-ray profile while the first asset pass still shares keep-case geometry.

Shelf travel is a continuous selection coordinate owned by a pure `PhysicalShelfMotionModel`, not a
queue of per-key transitions. It uses the exact critically damped solution for elapsed-time ticks,
preserves velocity when held input retargets it, caps stalled-frame time and stops its 16ms UI timer at
rest. Scope/far jumps snap instead of flying through the library, and the model's reduced-motion policy
lands on the same destination immediately. The renderer likewise redraws only on shelf-position, cover,
accent or user-rotation changes and bounds decoded/GPU cover observation to the seven visible games.

The existing surfaceless-EGL preview now emits `physical-shelf-scene.png`, exercising the exact shared
camera and multi-item renderer. Its acceptance composition deliberately places a full-height keep case
between SNES and GBA cartridges and leaves one cartridge art-free, so relative scale, baseline, centring
and the empty-shell fallback remain reviewable without an app window.

## 2026-08-13 — The first production SNES slice uses a cleaned CC BY PAL shell

SomeKevin's `Super Nintendo Cartridge` is the Phase 2 SNES base: its embedded metadata and live
Sketchfab listing identify CC BY 4.0, and its rounded moulding, screws, contacts, tangents and
base-colour/metallic-roughness/normal maps are materially better than the prototype shell. It is the
PAL/Super Famicom form, so `snes` now carries an explicit 129x87x20mm `snes-pal-grey` profile; a North
American shell remains a future regional variant. The authored label points toward -Z, making the
canonical correction a 180-degree Y rotation rather than the prototype's face-up axis conversion.

The downloaded 25.4MB GLB remains untouched under the user-supplied `models/snes/` sourcing area.
`SnesModelPrep` creates the 3.47MB redistributable derivative deterministically: it neutralizes the
fixed placeholder label in all three PBR texture channels, reduces the 4096px maps to 1024px, removes
six collapsed triangles and flips inconsistent triangle winding against the authored normals. Source
authorship/license metadata is retained and EmuShelf's modifications are added to the GLB metadata and
`THIRD-PARTY-NOTICES.md`. The supplied Super Mario World scan was rejected because its BY-NC license
fails redistribution; the linked paid Store model was rejected because raw/extractable redistribution
is not granted.

Dynamic art is projected over the measured front-label recess while the source UVs continue to drive
the body PBR maps. Missing art therefore produces an intact cartridge with an accent-coloured blank
label, never the static 2D missing-cover tile. A separately authored, slightly offset label mesh and
material, cleanup of the remaining welded boundary/non-manifold topology, direct key/contact shadow
lighting and real-Windows 1080p close-ups remain the open parts of the Phase 2 quality gate.

## 2026-08-13 — Physical media combines IBL with a direct key and analytic contact shadows

The baked studio environment remains responsible for broad diffuse light and material reflections,
but it cannot give small front-facing grooves and bevels enough stable local contrast. The shell PBR
shader therefore adds one world-space studio key using the same GGX distribution, Smith geometry and
Schlick Fresnel terms as the material model. It is deliberately restrained and warm-neutral; it adds
shape information without flattening the accent-tinted environment or becoming a moving flashlight.

Grounding uses a transparent horizontal receiving plane and at most seven analytic shadow footprints,
matching the renderer's already-bounded visible window. Each footprint combines a tight contact lobe
with a wider offset softbox lobe, is derived from the profile's rotated physical width/depth, and gets
slightly softer when focus lifts the medium. This avoids a shadow-map framebuffer, depth pass, PCF
samples and cross-GL shadow-format differences for five-to-seven small shelf objects. The plane emits
premultiplied black only; Avalonia's themed backdrop remains visually in charge and no opaque floor
rectangle can appear around the GL control.

All uploaded 2D maps already received trilinear mip chains. Upload now additionally requests up to 8x
anisotropy when EXT/ARB texture-filter anisotropy is advertised, improving projected labels at steep
yaw without making the extension mandatory. The exact renderer's front/hero/side/back/top/bottom and
mixed-shelf matrix is the automated visual artifact; real Windows at 1080p remains the final Phase 2
hardware gate.

## 2026-08-13 — The SNES label is a separate rounded dielectric surface

SNES artwork is no longer a material override on fragments of the downloaded body mesh. The body is
drawn first with zero artwork panels, preserving its neutralized base-colour, metallic/roughness and
normal maps; a generated rounded sticker is then drawn as a second mesh/material pass. Its conventional
UVs are retained for the future `support-texture` path, while today's shared object-space panel shader
supplies cropped cover art or the empty accent tint. Paper roughness and a flat normal now belong only
to the sticker, so the shell's moulded grain cannot punch through or distort it at steep yaw/pitch.

The sticker uses eight segments per corner (36 triangles total), sits 0.0008 canonical units above the
frontmost face, and follows the shell's existing model transform, metric scaling and controller rotation.
That is enough separation for the shared 24-bit depth target without reading as a floating card side-on.
A rejected tuning experiment ray-projected a tessellated sticker onto the source front topology; the
model contains broad overlapping moulded arcs through the nominal label region, so conforming to it
visibly cut those arcs out of the artwork. A real adhesive label bridges that authored topology, making
the deliberately flat second surface both visually correct and substantially simpler.

## 2026-08-13 — Hardware review replaces the separate SNES sticker with an attached decal

The separate rounded label surface from the preceding experiment is superseded. In motion and at a
three-quarter hardware view, even its sub-millimetre canonical offset exposed a bright edge and read as
a card hovering over the cartridge; the earlier projected label felt materially more unified. SNES art
therefore returns to the shell's body fragments, with no extra mesh or depth separation.

The projection path now carries the polish that motivated the experiment. `ArtPanel` records a corner
radius relative to its shorter physical edge. The fragment shader evaluates an aspect-correct rounded-
rectangle signed-distance mask, uses `fwidth` for resolution/angle-independent antialiasing, and blends
base colour, metallic value, paper roughness and flat label normal only across that mask. SNES uses a
0.075 radius; temporary GBA/case panels retain square bounds. This keeps the label visually attached at
every rotation while preventing shell grain from punching through its printed area and avoiding jagged
or stretched corners on the landscape recess.

## 2026-08-13 — The studio key now casts filtered geometry shadows; contact remains analytic

The direct GGX key previously illuminated every front-facing fragment even when cartridge moulding sat
between it and the light. Normal maps described grooves locally, but could not let the top lip, grip rails,
screw wells or contact opening shade adjacent plastic, which left the sourced SNES scan looking flatter and
glossier than its geometry warranted. The renderer now draws the exact visible body transforms into one
2048px directional depth map before the colour pass. A slope-biased 3×3 PCF lookup attenuates only the
direct key; image-based studio light remains present inside shadows, matching a softly filled product shot.
One colour renderbuffer accompanies the sampled depth attachment because that framebuffer shape behaves
consistently across core OpenGL, macOS GL 3.2 and ANGLE/GLES without divergent draw-buffer setup.

This does not replace the transparent receiving-plane footprints. Those two-lobe analytic shadows still
provide stable shelf contact for at most seven moving items, while the depth map supplies object-to-self
visibility. The two jobs therefore stay separate and scrolling cannot expose an opaque floor surface.
`MediaShellDefinition` also gains restrained per-shell roughness and dielectric-reflectance corrections;
only the sourced SNES body uses them (1.12× roughness, 3.5% dielectric F0) so its broad highlight reads as
aged moulded plastic without globally dulling GBA or case materials. The GLB declares no occlusion texture,
so fake screen-space cavity darkening was deliberately not added; future models can add authored AO through
the material pipeline. The desktop-GL full-HD seven-angle matrix and mixed metric shelf render pass cleanly;
real Windows/ANGLE remains the hardware acceptance gate.

## 2026-08-13 — SNES lighting contrast is calibrated at couch distance, not only in close-up

Hardware feedback found the first self-shadow pass technically correct but visually too conservative at
the cartridge's real shelf size. The SNES variant now receives 70% of the image-based studio fill while the
direct key is raised to 1.08/1.00/0.92 radiance. Key-depth visibility suppresses up to 58% of ambient fill,
so the lower recess, top lip, side rails and screw wells remain legible when the cartridge occupies only a
fraction of a 1080p screen. Ambient specular is attenuated less than diffuse, preserving a broad moving
plastic reflection rather than turning shadows into dead black paint.

The embedded metallic/roughness map's red channel is uniformly 255 and the GLB declares no occlusion map,
so it is not repurposed as fictional AO. Cavity instead comes from only strongly tilted texels in the
authored tangent-space normal map; shallow scan waviness is thresholded out and the SNES normal amplitude
is reduced to 72%. Projected paper explicitly blends cavity back to one, keeping the label clean and
visually attached. This is a per-shell material correction: temporary GBA/case assets retain their own
source response until they pass the same asset gate.

The Windows launch check also reconfirmed that shelf fallback diagnostics use EmuShelf's message-first
`IAppLogger` contract and already preserve the renderer exception in the portable log. It is deliberately
not treated as Serilog's exception-first API even though the infrastructure implementation ultimately
writes through Serilog.

## 2026-08-13 — The shelf uses a high-left raking key and self-only shadow maps

The front-biased studio key made a neutral cartridge easy to expose but nearly shadowless: its direction
from the surface toward the light was (-0.42, 0.68, 0.60), close enough to the viewing axis that broad
front faces received almost uniform light. The key now sits high and substantially camera-left at the
normalized direction of (-0.78, 0.56, 0.29). Its warmer 1.42/1.31/1.18 radiance compensates for the lower
front-face cosine. This raking position makes the label recess, lower slot, grip rails, screw wells and
opposite shell edge cast readable shade before the user rotates the cartridge, which is the composition
the couch shelf spends most of its time showing.

A single row-wide directional map was rejected with this angle because the selected tall case could cast
a large, physically plausible but compositionally destructive shadow over the next cartridge. Shelf items
now render one at a time against a 1024px map containing only their own geometry. That is finer effective
resolution than stretching 2048px across seven media, avoids cross-item occlusion, and keeps the total map
area bounded. Polygon depth offset plus a geometric-normal receiver bias prevents grazing-angle shadow acne;
the normal-mapped surface is deliberately excluded from bias calculation because its fine variation caused
moire-like self-interference on flat cartridge faces. The analytic receiving-plane footprints remain the
separate mechanism that visually connects every item to the shared shelf.

## 2026-08-13 — Physical-media performance policy is shared and quality-scaled

The SNES visual slice established the renderer baseline, but its initial quality settings scaled poorly:
fixed 2× supersampling shaded four output pixels for every display pixel, and every one of seven visible
items regenerated and sampled a 1024px 3×3-PCF self-shadow map during shelf travel. The renderer now chooses
up to 2× dynamically while capping its off-screen scene at 2560×1440 and never rendering below native. A
1920×1080 shelf therefore falls from 8.29 million shaded scene pixels to 3.69 million; 4K stays native rather
than attempting an 8K intermediate.

Dynamic self-shadow quality follows attention. At rest only the focused medium renders/samples PCF; during
continuous travel the outgoing and incoming pair qualify through their non-zero focus amount. Smaller
neighbours keep direct GGX light, IBL and analytic receiving-plane contact shadows, avoiding imperceptible
depth work. In the llvmpipe full-HD mixed-shelf acceptance render this reduced the measured shelf frame from
439 ms to 308 ms (about 30%); software timings are not a hardware FPS claim, but exercise the same draw path.

The shelf control retains twenty-one uploaded covers—three complete seven-item windows—in an explicit GPU
LRU. Reversing direction no longer repeats bitmap conversion, texture upload, mip generation and driver
capability queries at the visibility boundary. Anisotropy support/max is cached once per GL context, while
the renderer keeps four recent accent-tinted studio environments so platform backtracking avoids another
GPU convolution bake. Per-frame render lists and shadow footprints are reused, visible-game lookup is by
stable id, and property subscriptions are recomputed only when the rounded seven-item range changes.

These choices live below platform mapping in `MediaShelf3DControl`, `MediaShellRenderer` and `GlTexture`.
Every current and future physical-media profile inherits them; only physical dimensions, canonical pose,
artwork placement and deliberate material corrections remain platform-specific.

## 2026-08-13 — Shelf navigation never performs environment convolution or model decode

The performance review found that the first optimization still left two large first-use jobs inside the GL
render callback. A new platform accent compiled three temporary IBL programs and convolved a new cubemap,
while the first draw of a shell read and decoded its embedded GLB before uploading it. Caching helped only
the second visit, so the first visit could interrupt the very shelf movement the feature is designed to
show. The renderer now bakes one neutral studio environment per GL context. Platform colour is a cheap PBR
uniform applied mainly to ambient room light, leaving the neutral softbox reflections intact; `SetAccent`
therefore only stores a vector. Immutable `ModelAsset` data is decoded through one process-wide asynchronous
cache before rendering, while context-owned mesh and texture upload remains on the GL thread as required.
Until preparation completes the renderer simply omits that shell and requests another frame on completion.

`PhysicalMediaProfile.MaterialVariant` is now part of the shipping draw path rather than documentary
metadata. The temporary shared case geometry can express black PS2/GameCube, glossier blue-clear PS3 and
white Wii finishes through body tint, roughness and dielectric-reflectance corrections without duplicating
the mesh. These are restrained baseline variants, not a substitute for authored clear-sleeve geometry or
platform-specific models in the asset gate.

Interactive resize targets grow in 256px buckets and reuse their allocation while the requested scene fits,
preventing full colour/depth framebuffer churn on every intermediate window size. Artwork panel placements
are likewise calculated once when a shell is uploaded instead of allocating and resolving a list for every
visible item on every frame. Finally, the Avalonia shelf control detaches collection and visible-game event
handlers when it leaves the visual tree, then restores them on reattachment, so a discarded GL view cannot
be retained by the library collection.

## 2026-08-13 — The GL shelf is an active-mode resource with an explicit readiness contract

Avalonia can reject or lose the shared OpenGL surface before `MediaShelf3DControl.OnOpenGlInit` runs. An
exception event raised only by that override therefore cannot guarantee the designed flat-cover fallback.
`MediaShelf3DHost` now keeps the GL control out of the visual tree unless physical-shelf mode is active and
supported, and starts a four-second readiness watchdog only after the child is actually attached. Renderer
success cancels it; renderer exceptions, context loss that never recovers, and silent framework-level
initialization failure all converge on the same session-latched flat fallback. Grid and spotlight modes no
longer create a GL surface, decode models, subscribe to the game window or allocate GPU shelf resources.

Focus changes also capture the outgoing game's yaw and pitch before the incoming hero is recentered. The
shared scene blends that captured pose toward its standard neighbour angle as focus amount falls, preserving
the physical continuity of a turned cartridge instead of snapping it at the start of travel. The captured
set is bounded to the seven-item scene window.

Finally, bucketed scene targets are no longer grow-only. A target at least 1.5 times larger than the rounded
logical request is released after thirty sustained under-use frames, avoiding churn during live resize while
returning colour/depth memory after a move from a large display or maximized window. Until then a scissor
limits colour/depth clears to the logical scene rectangle, so spare bucket capacity consumes memory but no
per-frame clear bandwidth. These lifecycle, motion and allocation policies sit below platform profiles and
therefore apply to every current and future physical-media model.

## 2026-08-13 — Self-shadow quality follows visibility, not selection focus

The attention-scaled shadow policy from the earlier performance pass is superseded. Hardware review showed
that a cartridge lost the depth in its moulded lip, recesses and grip rails at the instant focus moved to its
neighbour, even though the outgoing cartridge remained large and fully visible during and after travel. That
read as a material/lighting pop rather than a quality optimization.

Every item submitted to the bounded seven-item shelf scene now receives its own isolated 1024px key-depth
pass and PCF sampling. Games outside that scene window are not submitted and still incur no rendering work.
If integrated-GPU acceptance later requires a lower shadow tier, it must be an explicit stable quality mode
or a screen-space visibility decision; focus alone may not change the rendered material of an on-screen
object. This policy is renderer-wide and applies automatically to every current and future media profile.

## 2026-08-13 — Cartridge models consume support-texture, not support-2D or box art

ScreenScraper's media names were ambiguous enough that the design originally treated `support-2D` as a
front-label fallback. Inspection of the downloaded SNES assets established a sharper contract:
`support-2D` is a complete pre-rendered cartridge image, while `support-texture` is the flattened label
texture that belongs in the authored model's cartridge-support slot. Projecting either portrait box art
or the complete support render into that recess produces a visibly cropped, physically incorrect label.

The metadata store therefore exposes selected media paths for one kind through a bulk query, and game
view models receive the selected `PhysicalMediaTexture` path during normal scope construction. The shelf
decodes those files off the UI thread only for its focused-neighbour window and retains at most twenty-one
decoded images, matching the existing GPU artwork bound. Missing, malformed, changed, or not-yet-decoded
textures leave the model's authored accent-colour blank label intact. This policy is selected by the
profile's `CartridgeSupport` artwork slot, so future cartridge platforms inherit it without bespoke UI
logic; `PhysicalMedia` remains stored for a future flat physical-media preview.

## 2026-08-13 — Physical-art cache bounds include pending and active decode work

A twenty-one-entry decoded/GPU LRU bounds retained artwork but does not, by itself, bound work. Starting
one `Task.Run` for each game entering the seven-item window lets rapid gamepad traversal queue much more
I/O than the renderer can display; late off-screen completions can then evict useful visible textures.

The shelf now runs at most two physical-art decodes concurrently. Its pending queue is reprioritized by
distance from the focused game whenever the visible window changes, and queued work outside that window
is discarded. An already-running decode may finish because the platform bitmap decoder is synchronous,
but its result is accepted only if the exact request is still current and visible. The bound is shared by
all cartridge profiles and complements rather than replaces the twenty-one-entry decoded and GPU LRUs.

## 2026-08-13 — Shelf composition uses one raised floor plus small cartridge presentation corrections

Full-window review showed the physically scaled SNES row occupying the lower third of the content area,
with substantially more empty space above it than between the media and game title. Zooming the shared
camera would enlarge every platform and reduce controller-rotation headroom for tall keep cases, while an
SNES-only translation would break the common physical floor.

The shared baseline and transparent receiving plane therefore move upward together by 0.08 shelf units,
roughly 45–50 pixels at the reviewed full-HD composition. The PAL SNES profile uses the contract's existing
1.10 presentation correction; its measured dimensions remain the source of width/height/depth ratios. A
separate profile clearance lifts SNES by 0.014 and GBA by 0.010 shelf units while cases remain grounded.
Analytic cast shadows expand and soften from this real lift value, so the gap reads as product-display
lighting rather than a detached object. Focus lift remains additive, and the common camera is unchanged.

## 2026-08-13 — Process start commits at the physical insertion pose

The emulator launch service already owns a callback after configuration/content preflight succeeds and
immediately before the process can read saves. Physical launch choreography runs inside that callback,
after pre-launch save sync: invalid launches never animate, saves finish before visual commitment, and the
process cannot start until the selected medium reaches its held insertion pose.

A pure elapsed-time state model drives `Lift -> Spin -> Align -> Insert -> Committed`; the spin covers
exactly three full turns. The view model supplies its pose to the existing shelf scene, while the renderer
only applies translation, rotation and scale. At `Committed`, roughly half the cartridge remains visible
at the lower viewport edge. Process-start failure, cancellation, unexpected exceptions and tracked emulator
exit all use `Return`, interpolating from the current pose back to the captured shelf pose. `IsBusy` and the
existing suspended-input guard prevent repeated launch or navigation during the sequence.

Save sync remains blocking in the launch pipeline but is no longer visually modal in physical-shelf mode:
the existing corner progress toast replaces the full-screen sync panel so the cartridge stays visible.
Grid and spotlight retain their centered panel. The model also has a shortened no-spin reduced-motion path;
exposing that policy as a user setting remains a separate rollout item.

## 2026-08-14 — A metric profile must match its asset's proportions; SNES was 12% too tall

`MediaShellRenderer.ShelfModel` scales each axis of a shell onto its `PhysicalMediaProfile`
independently. That is deliberate — downloaded geometry is close to, not equal to, the real package
— but it means a profile that disagrees with its asset never reads as a size error. The model is
silently deformed instead, and every later judgement about lighting, label placement and framing is
then made on a deformed object.

The SNES profile recorded 129x87x20mm. The cleaned PAL/Super Famicom asset's own ratios are W/H
1.6651 and D/H 0.2570, which agree with 129mm and 20mm to within 0.4% and put the height at 77.5mm.
87mm — the North American shell's height — was therefore stretching the gold-standard cartridge 12%
vertically: oval screws, non-circular corner radii, and a label mask whose object-space aspect no
longer matched what was drawn. The profile is now 129x77.5x20mm, and `PresentationScale` moves
1.10 -> 1.235 so removing the stretch gives the height back to the cartridge rather than to the
empty space above it; the row is correspondingly ~12% wider.

`MediaShellTests.MetricProfiles_MatchTheProportionsOfTheirAuthoredAsset` now fails any profile whose
width/height and depth/height ratios drift more than 3% from its loaded asset. Two systems are
excluded as decisions rather than oversights: GBA (85x60mm is not a Game Pak either, but correcting
it also roughly halves the cartridge on screen, so it belongs with that asset's pass) and PS3 (its
shorter Blu-ray profile is knowingly applied to shared DVD-case geometry until a PS3 case is
authored — that distortion is the reason the geometry is called temporary).

## 2026-08-14 — Shelf focus reads through light, arrival and departure share one blend

Three composition defects found by reading the M42 scene, all fixed together because they are the
same subject — what one d-pad step actually looks like.

**Contact shadow followed the wrong axis.** The focused medium steps toward the camera, but
`DrawShelfShadows` passed the vertical `FocusLift` (0.035) as its footprint's world Z while
`ShelfModel` placed the item at 0.08. The shadow trailed the selected cartridge and swam as focus
interpolated, which is precisely the failure the design's lighting gate names. Both now share one
`FocusDepth` constant.

**Arrival snapped while departure eased.** `ResolvePose` returned the focused angle the instant
selection changed — while the incoming medium was still a full slot from centre — but blended the
outgoing one back to the neighbour angle over its travel. Every step therefore turned one cartridge
smoothly and snapped the other through the ~14 degrees between the two rest poses. Both directions
now use the same focus blend.

**Focus was carried by ~2% of projected size.** Physical scale is data and focus may not change it,
so the selected medium differed from its neighbours only by a depth step and its angle. A row of
similar grey cartridges could not be read at couch distance. Neighbours now fall off to 48% of the
studio exposure, interpolated by the same focus value, so the selected medium stands in the key and
the others stand out of it. It is a light change, not a material one: colour and reflections are
untouched, and no scale is involved.

## 2026-08-14 — Launch choreography runs beside pre-launch save sync, not after it

The physical launch animation existed partly to cover the delay before an emulator appears, but it
ran after `SyncSavesForLaunchAsync` completed, so a slow cloud round-trip was dead time in front of
the animation rather than hidden behind it. The animation is now started before the sync is awaited
and both are awaited before the process starts, so the ordering guarantee is unchanged — saves are
still finished and preflight still passed before any process runs — while the visible cost of the
sync is absorbed by the cartridge already being in motion. When sync outruns the choreography the
medium simply holds its committed insertion pose.

Consequence, and the reason this is recorded: the earlier "saves finish before visual commitment"
property is gone by design. The medium can reach the inserted pose while a sync is still running.
Two supporting fixes fall out of running them concurrently: a sync failure now observes the pending
animation task instead of leaving it unobserved, and `RestorePhysicalShelfAfterLaunchAsync`
completes any pending launch completion source before replacing it, since a return can now begin
while the outward animation is still waiting on the old one.

## 2026-08-14 — macOS prefers OpenGL over Metal, because a GL control cannot live under Metal

Avalonia 12 defaults `AvaloniaNativePlatformOptions.RenderingMode` to `[Metal, OpenGl, Software]`.
Under Metal the platform graphics object is an `IMetalDevice`, so `OpenGlControlBase` requests a GL
context, does not receive one, and returns without initializing and without throwing. The couch
shelf's `MediaShelf3DControl` therefore never rendered a single frame on macOS. Nothing looked
broken: `MediaShelf3DHost`'s four-second watchdog fired and the designed flat-cover fallback took
over, so the mode appeared to work and simply was not the 3D scene. Verified on 2026-08-14 by
running the same build twice — stock logged
`TimeoutException: The OpenGL shelf did not initialize`, and with OpenGl preferred the scene came up
and stayed up.

Consequence worth stating plainly: every judgement made about the physical shelf on macOS before
this date was made on flat covers, not on the renderer.

Avalonia ships no Metal counterpart to `OpenGlControlBase` — `Avalonia.Metal` exports interop
interfaces (`IMetalDevice`, `IMetalPlatformSurface` and friends) and no control base — so hosting
the scene under a Metal compositor is not supported at any price short of a second renderer. The
alternatives were a Metal/MSL backend (a duplicate of the whole shading path to keep in step), an
offscreen render presented as a bitmap (a GPU-to-CPU-to-GPU round trip per frame), or accepting that
a shipped feature never runs on a shipped platform. Preferring OpenGl is one line and the only cheap
option, and it is Avalonia's own second choice rather than an exotic path.

The cost is real and accepted: the whole macOS app now composites through Apple's deprecated OpenGL,
capped at 4.1. Metal and Software remain behind it in the list, so a Mac whose GL context fails
degrades instead of failing to start. Windows (ANGLE) and Linux are unaffected. If general UI
rendering later proves to suffer on macOS, the escape route is a Metal backend for
`EmuShelf.Rendering`, not reverting this — reverting returns macOS to flat covers.
