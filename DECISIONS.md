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
