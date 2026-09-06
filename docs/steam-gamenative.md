# Steam through GameNative

1. In GameNative, enable frontend export/sync and choose an export folder.
2. In EmuShelf, open Settings → Emulators → Steam → Add game folder and select that folder.
   On the connected Thor it is `/storage/emulated/0/User/Gamenative` (23 exports inspected).
3. Steam games now appear in grid, list and shelf modes. Launching opens the selected game in
   GameNative; GameNative must be installed and the game must still be installed there.
4. After installing/uninstalling games in GameNative, use Steam's Rescan action in EmuShelf.
   Missing shortcuts are handled by normal availability and rescan controls. EmuShelf never
   deletes the exported shortcuts or the Steam game installations.

Built-in artwork lookup uses the exported Steam app id for Steam's portrait library capsule.
ScreenScraper uses PC Windows metadata and media. Batch matching requires a unique exact title;
use its title-search picker when the title differs or more than one match is returned.
ScreenScraper needs the usual credential-provisioned build and connected account.

Steam achievements and cloud saves remain in GameNative/Steam. EmuShelf's RetroAchievements
viewer is unchanged. GameNative is exempt from “close emulator on return” so returning to the
shelf does not interrupt Steam cloud uploads. EmuShelf still records local play sessions.

GameNative launch is Android-only. Desktop builds can organize and scrape the portable library,
but cannot launch these shortcuts. `.pcgame` files from other stores and arbitrary shell commands
are deliberately outside this Steam integration.

## Verification

- 2,444 automated tests passed, including invalid/oversized/missing shortcuts, integer launch
  payloads, Steam artwork identity and unique/ambiguous ScreenScraper title matching.
- Desktop build and Android Release/AOT build passed.
- Production renderer reviewed with the Steam case profile in a five-game shelf.
- Retrieved the 80 Days (381780) Steam capsule successfully.
- Confirmed GameNative 1.2.0, exported app ids and launch activity on the connected Thor.
- User confirmed games visible and game launch working on Thor using the separate test app.
- Return behavior and authenticated ScreenScraper checks remain pending. The separate test app
  remains installed while the user tests; remove it after testing as agreed.
- Steam platform artwork now uses an original shaded PC sprite matching the other hardware art.

## Code review follow-up

- Steam is last in the shared platform order, after Arcade.
- Android-only desktop rejection checks the resolved emulator; a desktop alternative
  for the same system is not blocked just because GameNative is also registered.
- Shortcut reads are bounded during reading as well as by the initial file size.
- ScreenScraper normalizes the requested title once and rejects empty normalized titles.
- The Steam icon is cached at 256 pixels wide (about 0.27 MiB decoded), rather than
  retaining the 1207×1303 source bitmap (about 6 MiB).
- Production and preview shelf definitions remain consistent; adding Steam does not
  change the preview tool's pre-existing default keep-case example.
- Catalog-free metadata profiles are explicitly supported; Steam capsule lookup
  needs no DAT download. Tests for other platforms still assert their catalog URI.

## Contract references

- [GameNative launch parser](https://github.com/utkarshdalal/GameNative/blob/master/app/src/main/java/app/gamenative/utils/IntentLaunchManager.kt)
- [ScreenScraper PC Windows platform (138)](https://www.screenscraper.fr/gamesinfos.php?plateforme=138&alpha=0&numpage=0)
