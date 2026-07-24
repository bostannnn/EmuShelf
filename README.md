# EmuShelf

A lightweight, portable game-library frontend for external emulators, inspired by [OpenEmu](https://openemu.org/)'s library design.

EmuShelf organizes PlayStation, PlayStation 2, PlayStation 3, PSP, Mega Drive / Genesis, Nintendo DS, Game Boy Advance, GameCube, and Wii games in a cover-grid library and launches them in separately installed emulators (DuckStation, PCSX2, RPCS3, PPSSPP, RetroArch, and Dolphin). PSP currently accepts standalone ISO and CSO images with a readable `PSP_GAME/PARAM.SFO`. Mega Drive / Genesis accepts header-proven `.md`, `.gen`, and `.bin` ROMs plus canonical copier/interleaved `.smd` dumps. Nintendo DS accepts header-validated raw `.nds` ROMs, and Game Boy Advance accepts header-validated raw `.gba` ROMs; archives, copier-header variants, and other containers remain deliberately unsupported until they have a tested read-only normalization path. PlayStation 3 imports only from an explicitly selected RPCS3 game library. EmuShelf performs no emulation itself, stores all user data beside the executable, and never modifies game files. Optional metadata enrichment can fetch canonical titles and individual covers after the user opts in; no artwork library is bundled.

**Status:** Windows validation candidate. Automated builds and tests pass; the remaining work is
the real-Windows acceptance gates for the expansion platforms and release (M13–M20).

- Product scope: [docs/design-document.pdf](docs/design-document.pdf)
- Metadata enrichment and new-platform guide: [docs/metadata-enrichment.md](docs/metadata-enrichment.md)
- Architectural decisions: [DECISIONS.md](DECISIONS.md)
- Windows acceptance: [docs/windows-test-checklist.md](docs/windows-test-checklist.md)

## Building

Requires the .NET SDK.

```
dotnet build
dotnet run --project src/EmuShelf.App
```

The release workflow also publishes a self-contained `EmuShelf-linux-x64.AppImage` for
SteamOS and other x64 Linux desktops. Extract/download it into a writable folder, mark it
executable, then add it to Steam as a non-Steam game and start it with `--gamepad-ui` for
Gaming Mode. If FUSE is unavailable, run `./EmuShelf-linux-x64.AppImage --appimage-extract-and-run`.

In Gamepad mode EmuShelf reads the controller natively via bundled SDL2 (Windows, Linux, and
macOS, x64 and arm64): A confirm, B back, X search, Y actions, LB/RB switch platform,
d-pad/left-stick navigate. No Steam Input layout is required, though the same keyboard mapping
(arrows, Enter, Escape, Ctrl+PageUp/PageDown, X, Y) still works as a fallback; use Steam + X for
the on-screen keyboard when typing.

Configure standalone emulator Flatpaks (e.g. `net.pcsx2.PCSX2`) by their explicit app id.
EmuShelf grants the sandbox read-only access to just the game being launched, for that launch
only — so ROMs anywhere under your home (Documents, SD card, etc.) launch without Flatseal or
`flatpak override`, and EmuShelf never persistently alters an emulator's Flatpak permissions.

The Build workflow publishes a self-contained `EmuShelf-win-x64.zip` artifact. Extract
the zip to a writable folder before launching `EmuShelf.exe`. Runtime data and daily
diagnostic logs are created beside the executable.

## License

[GPL-3.0](LICENSE).

EmuShelf is an independent project and is not affiliated with OpenEmu. No OpenEmu code
is used. Redistributed OpenEmu platform artwork retains its BSD 2-Clause license and
author credit in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
