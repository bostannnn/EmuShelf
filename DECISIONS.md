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
