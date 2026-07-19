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

## PSP / PPSSPP

- [ ] Record the PPSSPP release used (M14 validates 1.20.4), select its executable in the PSP
      Settings section, and confirm the default launch template is exactly one game-path argument.
- [ ] Add one standalone PSP `.iso` and one `.cso` containing `PSP_GAME/PARAM.SFO`. Confirm the
      embedded trustworthy title appears, the exact `DISC_ID` is retained for later metadata, and
      an image with an invalid/missing SFO is not imported as PSP even when manually confirmed.
- [ ] Confirm archives, CHD, PBP, and other compressed variants are not imported as PSP entries.
- [ ] Launch an ISO/CSO whose game and PPSSPP paths contain spaces. Confirm PPSSPP gets one intact
      content argument, EmuShelf minimizes, restores after a normal exit, reports a non-zero exit,
      and fails before minimizing when the selected executable is missing.
- [ ] Compare the game files and PPSSPP settings directory before and after import/launch; EmuShelf
      must not write either. Temporarily remove an imported image, refresh availability, and confirm
      it is visibly unavailable and blocked from launch.

## RetroArch core launcher

- [ ] In the Mega Drive / Genesis, Nintendo DS, and Game Boy Advance Settings sections, select one
      shared RetroArch executable and a distinct installed core for each system. Confirm Settings
      shows each core's file name, **Replace** changes only that system's core, and **Clear** removes
      only that system's core; EmuShelf must not scan, download, or edit any core.
- [ ] With a valid content file for each platform, launch through each configured core. Confirm
      RetroArch receives `-L`, the selected core path, and the game path as three separate argv
      entries; content and executable paths containing spaces must remain intact. A missing core,
      a folder selected as content, or malformed launch arguments must fail before EmuShelf minimizes.
- [ ] Compare RetroArch's configuration, override, playlist, and achievements-settings files before
      and after Settings use and tracked launches; their contents and timestamps must be unchanged.
      Move the complete portable folder, then repeat the three launches and confirm the shared
      executable and each core path still resolve relative to its new location.

## PlayStation 3 / RPCS3 library

- [ ] In Settings for PlayStation 3, choose the RPCS3 configuration folder that contains
      `games.yml`, then select **Sync RPCS3 library**. Confirm no location is auto-detected
      and the sync reads only the games recorded in that file; Add Game, Add Folder, and
      folder rescans must not import PS3 directories.
- [ ] With one listed installed game and one listed disc/directory game, confirm each entry
      records the RPCS3 path, title, exact title id, availability, and `RPCS3 library`
      provenance. An unlisted directory with a valid `PARAM.SFO` must not appear.
- [ ] Confirm a listed `PARAM.SFO` enriches a filename title only when its `TITLE_ID` exactly
      matches the RPCS3 list entry. A manual title or cover edit must survive another sync.
- [ ] Replace `games.yml` with an unsupported or malformed format. The sync must give an
      actionable failure, import nothing, and leave the file's contents and timestamp
      unchanged.
- [ ] Replace the list with a blank file (a valid empty RPCS3 library). Sync must succeed and
      mark previously listed entries `Source missing`; it must not infer folders or report a
      format error.
- [ ] Point a listed title id at a path already owned by a different EmuShelf game. Sync must
      reject the collision with an actionable message and leave both records unchanged.
- [ ] Remove an entry from `games.yml` while EmuShelf is open, sync again, and refresh
      availability. Its existing EmuShelf row must remain as `Source missing`, be blocked
      from launch, and never be revived by a generic folder/availability rescan.
- [ ] Launch both listed game types through the current RPCS3 launch template. Verify paths
      containing spaces remain one argument, EmuShelf minimizes and restores, a non-zero
      exit is reported, and neither RPCS3 data nor game files are written by EmuShelf.
- [ ] Confirm PlayStation 3 displays RetroAchievements as unsupported and does not perform
      RetroAchievements identification or matching.
