# EmuShelf

A lightweight, portable game-library frontend for external emulators, inspired by [OpenEmu](https://openemu.org/)'s library design.

EmuShelf organizes PlayStation, PlayStation 2, GameCube, and Wii games in a cover-grid library and launches them in separately installed emulators (DuckStation, PCSX2, and Dolphin). RPCS3 configuration is present, but PS3 importing is currently deferred. EmuShelf performs no emulation itself, stores all user data beside the executable, and never modifies game files. Optional metadata enrichment can fetch canonical titles and individual covers after the user opts in; no artwork library is bundled.

**Status:** Windows validation candidate. Automated builds and tests pass; real-Windows acceptance is the remaining M8 gate.

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

Version 1 targets Windows (portable zip). The codebase builds and runs on macOS throughout development; a packaged macOS release is planned for later.

The Build workflow publishes a self-contained `EmuShelf-win-x64.zip` artifact. Extract
the zip to a writable folder before launching `EmuShelf.exe`. Runtime data and daily
diagnostic logs are created beside the executable.

## License

[GPL-3.0](LICENSE).

EmuShelf is an independent project and is not affiliated with OpenEmu. No OpenEmu code
is used. Redistributed OpenEmu platform artwork retains its BSD 2-Clause license and
author credit in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
