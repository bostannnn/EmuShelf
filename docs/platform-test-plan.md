# EmuShelf — per-platform GUI test plan (living doc)

Working checklist for the Windows GUI acceptance pass. Unlike
`windows-test-checklist.md` (the formal release record), this file is a **living log**:
we tick items off per platform and record every problem in the Findings table so
nothing is forgotten. Update it as we go and reuse the per-platform template for each
new console.

## Environment

| Item | Value |
| --- | --- |
| OS | Windows 11 Pro 26200 |
| .NET SDK | 10.0.302 (user-local `C:\Users\Andre\.dotnet`) |
| Build/run | `& "$HOME\.dotnet\dotnet.exe" run --project src/EmuShelf.App/EmuShelf.App.csproj` |
| Portable data | `src/EmuShelf.App/bin/Debug/net10.0/{Data,Covers,Cache,Logs,Settings}` |
| Test DB | `…/Data/library.db` (inspect with `sqlite3`) |

## How each step is verified

The daily log (`…/Logs/EmuShelf-YYYY-MM-DD.log`) records startup/exit, all
RetroAchievements activity, and **every failure/warning** — but not successful routine
actions. Successful adds/covers/launches are verified directly:

| Step | Verified by |
| --- | --- |
| Configure emulator | `SELECT SystemId, ExecutablePath, CorePath FROM EmulatorConfigs;` |
| Add ROM(s) | `SELECT SystemId, Title, IsAvailable, Path FROM Games;` + `GameIdentifiers` |
| Launch | log line: minimize/restore, exit code, preflight errors |
| Covers | files under `Covers/`, `GameMetadata.CoverPath`, thumbnails in `Cache/Covers/` |
| Achievements | `RetroAchievementGameLinks` / `RetroAchievementProgress` / `…Details` rows; RA key never in any log or request URI |

## Per-platform template

For each platform: (1) configure emulator, (2) add ROM(s), (3) launch + failure cases,
(4) covers, (5) achievements. Mark ✅/❌/➖ (n/a) and link any Finding IDs.

---

## PlayStation (PS1) — DuckStation ✅ core acceptance complete

- Emulator: `F:\ES-DE\Emulators\duckstation\duckstation-qt-x64-ReleaseLTCG.exe`
- ROMs: `F:\ES-DE\ROMs\psx` — per-game folders of `.cue` + `.bin` (add via **Add Folder**;
  the `.cue` is the import unit, not the raw `.bin`).

| # | Check | Result | Notes / Finding |
| --- | --- | --- | --- |
| 1 | Settings → PlayStation → select DuckStation exe; path persists | ✅ | `EmulatorConfigs.playstation` = `…\duckstation-qt-x64-ReleaseLTCG.exe` |
| 2 | Add Folder `F:\ES-DE\ROMs\psx` → choose PlayStation; games import with clean titles | ✅ | 22 imported, all `.cue`, all Available; titles are canonical Catalog matches |
| 3 | Multi-disc games (Parasite Eve I/II) import sanely; no duplicate BIN track entries | ✅ | PE I/II each import as separate Disc 1/Disc 2 entries; no raw-`.bin` rows |
| 4 | Each game shows `CUE` format pill and is Available | ✅ | 22/22 Available; import unit is the `.cue` |
| 5 | Double-click launches a game in DuckStation; EmuShelf minimizes, restores on exit | ✅ | CTR - Crash Team Racing launched, DuckStation exited with code `0`, and EmuShelf restored. |
| 6 | Source CUE/BIN remains unchanged after launch | ✅ | CTR `.cue` and `.bin` retained identical size, UTC timestamp, and SHA-256 fingerprints before/after launch. |
| 7 | Launch with no exe configured → clear Settings instruction + warning in log | ⬜ | |
| 8 | Launch with a bad/missing exe path → clean preflight error, no minimize | ⬜ | |
| 9 | Metadata fetch: covers in `Covers/`, thumbnails in `Cache/` | ✅ (data) | 22/22 `CoverPath` set (libretro-database + xlenore-psx CDN); visual confirm pending |
| 10 | RA account → supported PS1 games show trophy mark + awarded/total | ✅ (data) | 22/22 hash-matched to RA IDs; progress cached as `0 / total`; **key not logged**; visual confirm pending |
| 11 | Remove a game → only its DB row goes; the `.cue`/`.bin` files are untouched | ⬜ | |

Identifiers: exact PS1 disc serials captured as primary evidence (e.g. `SCUS-94900`,
`SLUS-00662`, source `DiscContent`). RA matching used `rcheevos … playstation-v3` hashes.
Metadata enrichment auto-ran after import (consent persisted from earlier sessions).
The missing-executable/path and remove-game cases remain separate negative-path checks.

---

## PlayStation 2 (PCSX2) — pending

## GameCube (Dolphin)

- Emulator: `F:\ES-DE\Emulators\dolphin-emu\Dolphin.exe`
- ROMs: `F:\ES-DE\ROMs\gamecube` — Dolphin `.rvz` images imported through **Add Folder**.

| # | Check | Result | Notes / Finding |
| --- | --- | --- | --- |
| 1 | Settings → GameCube → select Dolphin exe; path persists | ✅ | `EmulatorConfigs.gamecube` = `…\dolphin-emu\Dolphin.exe`; default arguments are `-b -e "{GamePath}"` |
| 2 | Add the GameCube folder; RVZ files are recognized as GameCube | ✅ | 14/14 imported and Available; each has a header-derived six-character disc id |
| 3 | Metadata fetch: clean canonical titles and covers | ✅ | 14/14 covers downloaded and visually confirmed |
| 4 | RA account: supported games show trophy/progress | ✅ | 11/14 exact hashes matched achievement sets; Luigi's Mansion, Mario Power Tennis, and Rogue Squadron III have no set for these images |
| 5 | Double-click launches through Dolphin; EmuShelf minimizes then restores | ✅ | Sonic Adventure DX launched; lifecycle log records Dolphin exit code `0` and frontend restoration |
| 6 | Source game image remains unchanged after import/launch | ⬜ | Repeat with before/after size and timestamp capture on a future pass |
| 7 | Missing executable and non-zero Dolphin exit preflight | ⬜ | |

Both Metal Gear Solid: The Twin Snakes discs remain intentionally separate GameCube entries;
no raw disc or emulator configuration data was modified by EmuShelf.

## Wii (Dolphin) — pending

## PSP (PPSSPP)

- Emulator: `F:\ES-DE\Emulators\ppsspp\PPSSPPWindows64.exe`
- ROMs: `F:\ES-DE\ROMs\psp` — 16 valid `.iso` images; text metadata was skipped.

| # | Check | Result | Notes / Finding |
| --- | --- | --- | --- |
| 1 | Settings → PSP → select PPSSPP exe; path persists | ✅ | `EmulatorConfigs.psp` persists `PPSSPPWindows64.exe`; default argument is `"{GamePath}"`. |
| 2 | Add the PSP folder; valid images import | ✅ | 16/16 validated `PSP_GAME/PARAM.SFO` images imported and are Available. |
| 3 | Fetch missing metadata reports progress and downloads covers | ✅ (data) | 14/16 exact serial catalog matches; 11 covers downloaded. PSP Libretro scans have a `0.581` median ratio, so the tall cover frame was corrected from the DVD-case default. |
| 4 | Missing catalog/artwork remains honest | ✅ | The translated Kurohyou images lack catalog entries. Dissidia Final Fantasy, Persona 2 Batsu, and Tony Hawk match exact catalog serials but their canonical and filename thumbnail URLs return `404`; their status remains partial rather than falsely unmatched. |
| 5 | RA account scan finds supported games and caches progress | ✅ (data) | All 16 hashes were linked; 15 have achievement sets and Kurohyou 2 is a confirmed no-set outcome. Tony Hawk is linked to RA game `3199` with `1 / 147` cached progress. |
| 6 | Double-click launches through PPSSPP; EmuShelf minimizes then restores | ✅ | Tony Hawk launched from a path with spaces, PPSSPP exited with code `0`, and EmuShelf restored. |
| 7 | Source ISO remains unchanged after import/launch | ✅ | Tony Hawk's 1.25 GB ISO retained identical size, UTC timestamp, and SHA-256 fingerprint before/after launch. |
| 8 | CSO import and missing-executable/non-zero-exit handling | ⬜ | Exercise as a separate format/error pass. |

## Mega Drive / Genesis (RetroArch + core)

- Emulator: `F:\ES-DE\Emulators\RetroArch\retroarch.exe`
- ROMs: `F:\ES-DE\ROMs\megadrive` — 29 `.md` ROMs; `metadata.txt` and
  `systeminfo.txt` were correctly skipped as non-games.

| # | Check | Result | Notes / Finding |
| --- | --- | --- | --- |
| 1 | Settings → Mega Drive / Genesis → select RetroArch and a core | ✅ | Config persists `genesis_plus_gx_libretro.dll`; launch template is `-L "{CorePath}" "{GamePath}"`. The selector lists core binaries in RetroArch's adjacent `cores/` folder instead of requiring a path to be typed. |
| 2 | Add the Mega Drive folder; accepted ROMs are imported | ✅ | 29/29 `.md` ROMs imported and Available; two text files were skipped. |
| 3 | Fetch missing metadata reports progress and downloads covers | ✅ (data) | 29/29 covers stored. The first pass exposed a closed-stream race in the shared Libretro DAT cache; after that fix and the filename artwork fallback, all covers downloaded. |
| 4 | Catalog titles are matched and shown in the library | ✅ | 29/29 exact catalog matches; all stored `Title` values equal their canonical titles. In this ROM set those canonical titles retain the same region/revision wording as the filenames. |
| 5 | RA account scan finds supported games and caches progress | ✅ (data) | 29/29 exact rcheevos Mega Drive matches, all with achievement sets; cached totals span 13–117 achievements with 47 awarded. |
| 6 | Double-click launches through RetroArch; EmuShelf minimizes then restores | ✅ | Aladdin launched; log records RetroArch exit code `0` and frontend restoration. |
| 7 | Source ROM remains unchanged after import/launch | ⬜ | Repeat with before/after size and timestamp capture. |
| 8 | Missing executable, missing core, and non-zero exit handling | ⬜ | Exercise as a separate preflight/error pass. |

The artwork downloader first requests the canonical Libretro thumbnail title, then retries only with
the ROM filename title when a thumbnail catalog uses a different punctuation or language-tag spelling.
This shared metadata pipeline now applies to every platform with cover fetching; it is not
Genesis-specific. A game with an already-downloaded cover but no exact catalog title is now recorded
as partial metadata rather than incorrectly counted as unmatched.

## Nintendo DS (RetroArch + core)

- Emulator: `F:\ES-DE\Emulators\RetroArch\retroarch.exe`
- ROMs: `F:\ES-DE\ROMs\nds` — 41 validated raw `.nds` cartridges. Save files and text
  metadata were skipped.

| # | Check | Result | Notes / Finding |
| --- | --- | --- | --- |
| 1 | Settings → Nintendo DS → select RetroArch and a core | ✅ | Config persists `melondsds_libretro.dll`; launch template is `-L "{CorePath}" "{GamePath}"`. |
| 2 | Add the Nintendo DS folder; accepted ROMs are imported | ✅ | 41/41 `.nds` files imported and Available. |
| 3 | Fetch missing metadata reports progress, fixes titles, and downloads covers | ✅ | 39/41 exact No-Intro catalog matches now use canonical titles with covers. DS Libretro boxart is wide (median 1.115), so the per-platform frame was corrected from the portrait default. [F7] |
| 4 | Exact-match misses remain honest and readable | ✅ | `Zoo_Keeper_(U)_(UNDUB).nds` is a 4.6 MB modified variant with SHA-1 `E15A…11EB`, not the clean USA DAT hash; translated `Jump Ultimate Stars` is also modified. Both filename artwork fallbacks returned `404`, so neither has a downloaded cover; each now displays its filename title rather than an internal header abbreviation. |
| 5 | RA account scan finds supported games and caches progress | ✅ (data) | 29/41 hashes matched achievement sets; 12 are confirmed no-set outcomes. Credentials are absent from logs and `settings.json`; the protected key blob remains separate. |
| 6 | Double-click launches through RetroArch; EmuShelf minimizes then restores | ✅ | Contra 4 launched, RetroArch exited with code `0`, and EmuShelf restored. Its progress row refreshed eight seconds after exit. |
| 7 | Source ROM remains unchanged after import/launch | ⬜ | Repeat with before/after size and timestamp capture. |
| 8 | Missing executable, missing core, and non-zero exit handling | ⬜ | Exercise as a separate preflight/error pass. |

## Game Boy Advance (RetroArch + core)

- Emulator: `F:\ES-DE\Emulators\RetroArch\retroarch.exe`
- ROMs: `F:\ES-DE\ROMs\gba` — 19 validated raw `.gba` cartridges.

| # | Check | Result | Notes / Finding |
| --- | --- | --- | --- |
| 1 | Settings → Game Boy Advance → select RetroArch and a core | ✅ | Config persists `mgba_libretro.dll`; launch template is `-L "{CorePath}" "{GamePath}"`. The always-visible filter now narrows a fixed scrollable installed-core list, so scrolling does not alter the selected core. [F8] |
| 2 | Add the GBA folder; accepted ROMs are imported | ✅ | 19/19 `.gba` files imported and Available after correcting the GBA header complement check. [F9] |
| 3 | Fetch missing metadata reports progress, fixes titles, and downloads covers | ✅ | 17/19 exact No-Intro catalog matches have canonical titles and covers. GBA Libretro boxart is predominantly square, so the platform frame uses a `1.0` ratio. |
| 4 | Exact-match misses remain honest and readable | ✅ | The translated `Mother 3` and `Pokémon Unbound` ROM hack have no exact DAT cover. Their filename titles remain readable instead of the internal `MOTHER3` / `POKEMON FIRE` header labels. |
| 5 | RA account scan finds supported games and caches progress | ✅ (data) | All 19 hashes are linked; 17 have achievement sets and 2 are confirmed no-set outcomes. `Metroid Fusion` cached `4 / 43` progress; credentials remain absent from logs and `settings.json`. |
| 6 | Double-click launches through RetroArch; EmuShelf minimizes then restores | ✅ | Metroid Fusion launched from a path with spaces, RetroArch exited with code `0`, EmuShelf restored, and the progress row refreshed after exit. |
| 7 | Source ROM remains unchanged after import/launch | ⬜ | Repeat with before/after size and timestamp capture. |
| 8 | Missing executable, missing core, and non-zero exit handling | ⬜ | Exercise as a separate preflight/error pass. |

## PlayStation 3 (RPCS3 library sync) — pending

Focus: the restored auto-detect — with `rpcs3.exe` configured, **Sync RPCS3 library**
must NOT prompt for the config folder (it finds `games.yml` beside the exe).

---

## Findings & fixes

| ID | Area | Description | Status |
| --- | --- | --- | --- |
| F1 | Library / selection | No multi-select or Ctrl+A select-all; Remove is one game at a time, so clearing a library is tedious. Add multi-select (Ctrl/Shift-click), Ctrl+A select-all, and a bulk "Remove selected" with one confirmation. | Planned — [ROADMAP M25](../ROADMAP.md) |
| F2 | Library / import | No drag-and-drop: cannot drop an ISO/folder onto the window, pick a system, and import (OpenEmu supports this). | Planned — [ROADMAP M22](../ROADMAP.md) |
| F3 | Library / list view | List-view cover thumbnail was a hardcoded 43×52 portrait box for every platform, so square PS1 covers (aspect 1.0) were cropped left/right. Now the list thumbnail height is fixed and its width follows the platform's `CoverAspectRatio` (52×52 for PS1, ~37×52 for portrait systems), matching the grid. | Fixed (2026-07-19) — confirmed |
| F4 | Library / grid view | Grid reserved a fixed 266px cover slot + `MinItemHeight=326` (sized for the tallest, portrait platform), so a single short-cover platform (square PS1) showed ~78px dead space above every cover and huge row gaps. Now each tile's cover shelf is sized to the tallest cover in the current view (`ShelfCoverHeight`) and `MinItemHeight` is dropped, so a pure-PS1 view packs tightly while a mixed collection still bottom-aligns to one baseline. | Fixed (2026-07-19) — awaiting visual re-check |
| F4 | RetroAchievements badges | Badge `493263` reported an `UnauthorizedAccessException` while caching its PNG. It did not affect GameCube identification, progress, or launch; retry the relevant achievements popup and investigate only if it recurs. | Needs repro (2026-07-19) |
| F8 | RetroArch core selector | A `ComboBox` let the mouse wheel cycle the selected core and its width visibly changed with the name. Replaced it with an always-visible filter and fixed-height scrollable list; selection now changes only by choosing a row. | Fixed (2026-07-19) |
| F9 | GBA import / presentation | Valid GBA ROMs were rejected because the header-complement constant had the wrong sign. After correcting it, all 19 files imported. Unmatched translated/hack ROMs now show filename titles while retaining header evidence for exact matching. | Fixed (2026-07-19) |
| F5 | Metadata / catalog cache | Concurrent Genesis enrichment attempted to move a Libretro DAT before its temporary download stream had closed, producing Windows file-lock errors and leaving games without canonical metadata. The stream is now closed before the atomic move and concurrent cache refreshes share a per-cache lock. | Fixed (2026-07-19) |
| F6 | Metadata / artwork status | Re-fetching a game with an existing downloaded cover but no catalog match was reported as unmatched because the summary considered only covers downloaded in that specific run. Existing covers now yield `Partial`, not `Unmatched`. | Fixed (2026-07-19) |
| F7 | Nintendo DS / metadata presentation | DS import initially displayed short internal cartridge-header IDs and rendered broad Libretro DS boxart inside a portrait frame. Exact catalog titles now replace non-user embedded headers, repairable rows rejoin Fetch missing metadata, and DS uses the measured 1.115 artwork frame ratio. | Fixed (2026-07-19) |
