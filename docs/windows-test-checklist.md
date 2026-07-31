# Windows acceptance checklist

Use the `EmuShelf-win-x64` artifact produced by the Build workflow. Verify the
SHA-256 file, extract `EmuShelf-win-x64.zip` to a writable folder, and run
`EmuShelf/EmuShelf.exe` from the extracted folder. Do not run it inside the zip.

Record the Windows version, artifact commit, emulator versions, and any failure's
matching file from `EmuShelf/Logs/`.

## M20 expansion release-run record

Complete this record before marking a run below as passed. Use the exact release or
nightly build displayed by each emulator/core, not a channel name such as `latest`.
Record only file names and versions here; do not put account names, tokens, or other
credentials in this checklist.

| Run date / artifact commit | Windows edition and version | Portable-folder location | Result / log reference |
| --- | --- | --- | --- |
|  |  |  |  |

For each expansion-system run, record the executable and core actually selected in
Settings. A file extension alone is not sufficient: the input must meet the stated
read-only import contract.

| System | Executable / core selected | Exact emulator/core version or build | Supported input exercised | Pass / fail |
| --- | --- | --- | --- | --- |
| PlayStation 3 | `rpcs3.exe` (no core) |  | `games.yml`-listed installed or disc/directory entry; generic file/folder import is unsupported |  |
| PSP | `PPSSPP*.exe` |  | `.iso`, `.cso`, and `.chd` with readable `PSP_GAME/PARAM.SFO` |  |
| Mega Drive / Genesis | `retroarch.exe` + selected core |  | Header-valid raw `.md`, `.gen`, or `.bin`, or canonical copier/interleaved `.smd` |  |
| Nintendo DS | `retroarch.exe` + selected core |  | Header-valid raw `.nds` only |  |
| Game Boy Advance | `retroarch.exe` + selected core |  | Header-valid raw `.gba` only |  |

Do not mark unsupported archives, PSP PBP, DS DSi-exclusive or copier-header
images, or GBA headered/copier images as a successful format run. They must remain
rejected or visibly unmatched as specified below.

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
- [ ] Add one standalone PSP `.iso`, one `.cso`, and one `.chd` containing `PSP_GAME/PARAM.SFO`.
      Confirm the embedded trustworthy title appears, the exact `DISC_ID` is retained for later
      metadata, and an image with an invalid/missing SFO is not imported as PSP even when manually
      confirmed. A PSP CHD must suggest PSP, not PS1/PS2.
- [ ] Confirm archives, PBP, and other compressed variants are not imported as PSP entries.
- [ ] Launch an ISO/CSO/CHD whose game and PPSSPP paths contain spaces. Confirm PPSSPP gets one intact
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

## Nintendo DS and Game Boy Advance import

- [ ] Add a raw Nintendo DS `.nds` ROM with a valid header. Confirm its trustworthy header title
      appears, its game code remains local evidence only, and a malformed, DSi-exclusive, archive,
      or copier-header input is not imported. Add a valid `####` homebrew ROM and confirm it uses
      its local title but has no guessed catalogue title.
- [ ] Add a raw Game Boy Advance `.gba` ROM with a valid header. Confirm its trustworthy header
      title appears and a headered/copier, archive, or malformed image is not imported. Import two
      valid files with the same game code but different payloads and confirm they remain separate
      entries with no title collision.
- [ ] Compare the accepted ROMs before and after import, availability refresh, rescan, and launch:
      their contents and timestamps must be unchanged. Repeat a DS and GBA launch using paths with
      spaces, then verify the shared RetroArch configuration, overrides, playlists, and achievement
      settings remain unchanged.

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

## M19 exact covers and RetroAchievements

- [ ] With metadata consent enabled, fetch missing metadata for one verified PS3, PSP, Mega Drive /
      Genesis, Nintendo DS, and Game Boy Advance entry. Confirm the canonical title and cover are
      applied only after an exact Redump/No-Intro match, downloaded art is under portable `Covers/`,
      and the source game/RPCS3 data remains unchanged. A deliberately absent cover must leave the
      existing placeholder/manual cover intact.
- [ ] While a cover request is pending, manually choose a cover for the same game. Confirm the
      manual cover wins and the temporary downloaded file is removed. Disconnect the network or
      provoke a provider error and confirm cached/manual covers remain visible with no retry loop.
- [ ] Connect a test RetroAchievements account, then identify and refresh one accepted PSP
      ISO/CSO/CHD (a CHD must produce the same hash as the ISO it was built from),
      Mega Drive raw/SMD ROM, Nintendo DS ROM, and GBA ROM. Confirm each consults its own console
      catalogue (PSP 41, Mega Drive 1, DS 18, GBA 5); an archive, headered/unknown layout, or stale
      catalogue miss must stay unknown rather than display `No achievements`.
- [ ] Launch the same supported PSP and RetroArch/core games with achievement unlocking enabled in
      the emulator, then exit normally. Confirm EmuShelf only refreshes cached read-only progress;
      it must not alter the game, PPSSPP, RetroArch configuration, account credentials, overrides,
      playlists, or achievement settings. Change/disconnect the account during the post-exit delay
      and confirm stale progress is not saved to the new account.

## M20 expansion release acceptance

- [ ] Start from a newly extracted artifact with no portable data folders. Confirm the empty
      library is useful and free of unhandled errors, then cancel each Add Game/Add Folder picker
      and the RPCS3-configuration-folder picker before a source sync begins. Confirm the cancelled
      operations add no partial rows, change no manual metadata, trigger no unexpected network
      fetch, and leave pre-existing source-owned PS3 rows intact until a subsequent successful sync.
- [ ] Exercise the explicit RPCS3 source path (including blank, malformed, and missing `games.yml`),
      an ordinary missing-file record, and an external-drive record. Confirm source-owned PS3 rows
      show `Source missing`, ordinary rows show `File missing`, neither launches while unavailable,
      and both recover only through their appropriate refresh/sync path.
- [ ] Repeat imports, metadata/cover fetching, and configured launches with spaces in every portable
      folder, executable/core path, and supported content path. Move the complete portable folder to
      a different drive/location, reconnect an external content drive, and confirm relative paths,
      cover cache/placeholder behavior, and availability survive without rewriting game or emulator
      data.
- [ ] With no executable, no RetroArch core, a missing selected executable/core, a directory passed
      as content, and a malformed launch template, confirm each preflight failure is actionable and
      leaves EmuShelf visible. Then perform one normal and one non-zero launch for PPSSPP, RPCS3,
      and each configured RetroArch system; EmuShelf must minimize only after a valid start, restore
      after exit, and write the outcome to the portable log.
- [ ] During an exact metadata cover request, assign a manual title and cover. Confirm the manual
      values survive completion, rescan, source sync, restart, and the move above; an unavailable
      provider or missing cover must retain the placeholder/manual cover rather than retrying
      indefinitely.
- [ ] On a real Windows run, connect a test RetroAchievements account and use one validated PSP
      image plus one validated Mega Drive / Genesis, Nintendo DS, or Game Boy Advance image. Enable
      unlocking/submission only in PPSSPP or the selected RetroArch core, unlock an achievement,
      then exit. Confirm EmuShelf refreshes only its cached read-only progress and PS3 still states
      that RetroAchievements is unsupported.
