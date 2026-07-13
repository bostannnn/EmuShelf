# Windows acceptance checklist

Use the `EmuShelf-win-x64` artifact produced by the Build workflow. Verify the
SHA-256 file, extract `EmuShelf-win-x64.zip` to a writable folder, and run
`EmuShelf/EmuShelf.exe` from the extracted folder. Do not run it inside the zip.

Record the Windows version, artifact commit, emulator versions, and any failure's
matching file from `EmuShelf/Logs/`.

## Portable startup and persistence

- [ ] EmuShelf starts without requiring a separately installed .NET runtime.
- [ ] First launch creates `Data/`, `Covers/`, `Cache/`, `Logs/`, and `Settings/`
      beside `EmuShelf.exe`; it does not write user data under AppData.
- [ ] Light, dark, and follow-system appearance work and survive restart.
- [ ] Settings shows the correct platform icons, expandable platform sections,
      editable launch arguments, and executable pickers.
- [ ] Closing and reopening EmuShelf preserves games, titles, covers, folders,
      emulator paths, launch arguments, and appearance.
- [ ] Moving the complete portable folder preserves relative game and emulator
      paths that were located under that folder.

## Library workflow

- [ ] Add individual PS1 and PS2 CUE/CHD/ISO/PBP/M3U files; explicitly confirmed
      standalone BIN files import without duplicate CUE track entries.
- [ ] Add GameCube and Wii ISO/RVZ/WBFS/GCM/CISO files; Nintendo header detection
      assigns each image only to the correct console.
- [ ] Add dedicated folders recursively; non-game files are ignored.
- [ ] Per-platform `Rescan library` discovers a newly copied game, and `Rescan all
      consoles` refreshes All Games and Recently Added immediately.
- [ ] Grid and list views scroll smoothly with the largest available library;
      search remains responsive and clears correctly.
- [ ] Missing files are marked `File missing`, cannot launch, and become available
      again after the drive/file returns and availability is refreshed.
- [ ] Title edits and assigned covers survive restart; thumbnail files appear in
      `Cache/Covers/` rather than repeatedly decoding the original image.
- [ ] Removing a game removes only its database entry. The game file and copied
      cover remain untouched.

## Launching and failures

- [ ] With no emulator configured, double-click gives a clear Settings instruction
      and writes a warning to the daily `Logs/EmuShelf-YYYY-MM-DD.log` file.
- [ ] A missing configured executable and a malformed argument template both fail
      clearly without minimizing the frontend.
- [ ] DuckStation launches a PS1 game; EmuShelf minimizes and restores after exit.
- [ ] PCSX2 launches a PS2 game; paths containing spaces remain one argument.
- [ ] Dolphin launches GameCube and Wii games using their independent per-system
      configurations; EmuShelf restores after each exit.
- [ ] A non-zero emulator exit and an intentional scan/configuration failure appear
      in both contextual UI feedback and the portable daily log.

## Current scope exception

PS3 directory importing and PARAM.SFO title parsing are deliberately deferred in
`ROADMAP.md`. RPCS3 can be configured in Settings, but RPCS3 game import/launch is
not an acceptance gate for this Windows candidate. Completing the design document's
original five-system section 14 definition requires bringing the PS3 backlog back
into scope.
