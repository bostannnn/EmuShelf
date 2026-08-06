# EmuShelf

Portable, OpenEmu-inspired game-library frontend in C# + Avalonia. Organizes PS1/PS2/PS3/GameCube/Wii games and launches external emulators (DuckStation, PCSX2, RPCS3, Dolphin). No emulation cores. Product scope: `docs/design-document.pdf`. Decision log: `DECISIONS.md` (append-only — add an entry when a non-obvious choice is made). Work plan: `ROADMAP.md` — work the current milestone top to bottom and check items off as they land.

## Commands

If `dotnet` is not on PATH, use `$HOME/.dotnet/dotnet` (user-local SDK install).

- Build: `dotnet build`
- Run: `dotnet run --project src/EmuShelf.App`
- Test: `dotnet test`

## Layout

- `src/EmuShelf.App` — Avalonia UI: views, view models, app lifecycle.
- `src/EmuShelf.Core` — domain models and interfaces. No package or project dependencies.
- `src/EmuShelf.Infrastructure` — SQLite persistence, folder scanning, process launching, portable storage.
- `src/EmuShelf.Integrations` — per-system and per-emulator definitions as feature folders (`Systems/…`, `Emulators/…`).

## Rules

- MVVM with CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`). No logic in code-behind beyond view wiring.
- Windows, macOS (arm64), and Linux/AppImage are all shipped release targets (see DECISIONS 2026-08-06); the app must build, run, and pass tests on macOS at all times. Platform-specific behavior goes behind interfaces in Core. The macOS `.app` is currently unsigned/un-notarized — don't assume a code-signing/notarization step exists.
- Never modify or delete the user's game files. Removing a game touches only EmuShelf's own database.
- All user data lives beside the executable (portable): `Data/`, `Covers/`, `Cache/`, `Logs/`, `Settings/`. Support relative paths so the app, emulators, and games can move together on one drive.
- Performance: virtualize grid/list views, load covers asynchronously off the UI thread, cache scaled thumbnails, debounce search, no full library rescan at startup.
- No OpenEmu code. OpenEmu artwork may be imported only when its redistribution terms
  are verified, the upstream license ships with EmuShelf, and the original authors are
  credited in `THIRD-PARTY-NOTICES.md`; never import OpenEmu branding or unlicensed game art.
