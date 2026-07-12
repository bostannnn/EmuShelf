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
