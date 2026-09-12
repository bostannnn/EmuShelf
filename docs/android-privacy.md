# Android data and permissions

EmuShelf is a library frontend. Emulators are installed separately and run your games.
Removing a game from EmuShelf removes its library entry, not the game file.

## On your device

The selected EmuShelf data folder holds your library database, settings, downloaded
artwork, caches, save-sync data and diagnostic logs. It can be on shared storage or
removable media, so other apps with access to that location may also read it. Keep
backups of this folder and your emulator saves. Choosing another data folder does
not erase the previous folder.

Account credentials use the provider's credential store; do not share credential files
or your complete Settings folder when asking for help. Logs and library files can
include local paths and game titles. Review them before sharing.

## Network features

- Artwork and metadata lookup contacts the configured providers, including Libretro,
  xlenore and ScreenScraper. Lookups may send game identifiers, names or fingerprints
  needed to identify a game. Downloaded game artwork is cached locally.
- Connecting RetroAchievements or Steam enables requests for account/game achievement
  information using the credentials you supply. Disconnect through Settings when you
  no longer want to use an account.
- Google Drive save sync, when configured, transfers emulator save data to your linked
  Drive account. The save-sync feature does not upload game images. Review your sync
  configuration and save-folder selection before enabling it.
- Update checks contact GitHub; downloading an update fetches the APK and checksum.
  Android asks you to approve installation.

Network services receive ordinary connection information such as your IP address and
apply their own privacy policies. Local library browsing does not require connecting
an achievements or cloud account.

## Android permissions

- **All files access:** reads game images and configured emulator save folders by path,
  and writes EmuShelf data and enabled save-sync output. You can revoke it in Android
  Settings; disconnected storage or a revoked grant can make the library unavailable.
- **Install unknown apps:** used only to hand an EmuShelf update to Android's installer.
- **Optional second-screen accessibility service:** observes window/package changes
  to return the companion display after a launched emulator closes. Enable it only
  if you want that second-screen behavior; disable it in Android Accessibility Settings.
- **Optional Shizuku access:** permits closing a launched emulator when you return, if
  you enable that option. EmuShelf can still be used without Shizuku.
- **Foreground service:** keeps the companion display available during a game session.
  Its notification returns to EmuShelf when tapped.

## Startup problems and support

If startup fails, reconnect the data volume and check free space and file permissions,
then choose **Retry**. **Choose data folder** lets you select another location without
deleting the old library. Avoid uninstalling as a troubleshooting step: it removes
app-private state and permission grants.

**Details** on the recovery screen identifies the error type. Additional diagnostics
are in your data folder's `Logs/` directory. If that directory could not be opened,
capture `adb logcat -s EmuShelfBoot` instead. Review diagnostics before attaching them
to a [support issue](https://github.com/bostannnn/EmuShelf/issues).
