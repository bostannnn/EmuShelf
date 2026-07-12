# EmuShelf

A lightweight, portable game-library frontend for external emulators, inspired by [OpenEmu](https://openemu.org/)'s library design.

EmuShelf organizes your PlayStation, PlayStation 2, PlayStation 3, GameCube, and Wii games in a cover-grid library and launches them in separately installed emulators (DuckStation, PCSX2, RPCS3, Dolphin). It performs no emulation itself, stores all of its data beside the executable, and never modifies your game files.

**Status:** early development — not yet usable.

- Product scope: [docs/design-document.pdf](docs/design-document.pdf)
- Architectural decisions: [DECISIONS.md](DECISIONS.md)

## Building

Requires the .NET SDK.

```
dotnet build
dotnet run --project src/EmuShelf.App
```

Version 1 targets Windows (portable zip). The codebase builds and runs on macOS throughout development; a packaged macOS release is planned for later.

## License

[GPL-3.0](LICENSE).

EmuShelf is an independent project and is not affiliated with OpenEmu. No OpenEmu code or artwork is used.
