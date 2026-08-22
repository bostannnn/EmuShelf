# Installing EmuShelf on Android (experimental sideload)

EmuShelf's Android build is an **experimental sideload APK**, not a Play Store app and not a fourth
supported release target (see `docs/android-port-plan.md`). It ships as `EmuShelf-android-arm64.apk`,
attached to each tagged GitHub release alongside a `.sha256` checksum. It targets arm64 Android
handhelds (developed against the AYN Thor, Android 13 / API 33).

## Get the APK

1. On the [Releases page](https://github.com/bostannnn/EmuShelf/releases), download
   `EmuShelf-android-arm64.apk` and `EmuShelf-android-arm64.sha256` from the latest tag.
2. Verify the download (optional but recommended):
   ```bash
   sha256sum -c EmuShelf-android-arm64.sha256
   ```

## Install it

**Option A — from the device (file manager):**
1. Copy the APK to the handheld (USB, SD card, or a download).
2. Open it with a file manager and confirm the install. Android will prompt to allow installs from
   that source the first time — grant it for the file manager you used.

**Option B — over USB with ADB (recommended for updates):**
```bash
adb install -r EmuShelf-android-arm64.apk
```
`-r` keeps your existing library and settings **only when the new APK is signed with the same key** as
the installed one. Release APKs from CI are signed with EmuShelf's stable release key, so tagged-release
upgrades preserve your data. Switching between a release APK and a locally built debug APK is a signing
change: Android refuses the in-place upgrade, and you must uninstall first (**which erases the on-device
library, settings, and the storage-access grant**).

## First run

- **Grant all-files access.** EmuShelf reads your ROMs and each emulator's saves by real path, so on
  first launch it walks you through granting "All files access" (Settings → Apps → EmuShelf →
  Permissions). Without it the library cannot be scanned.
- **Install and set up your emulators separately.** EmuShelf launches external emulator apps
  (DuckStation, Dolphin, PPSSPP, Azahar, WatermelonDS, RetroArch, an ARMSX2/PCSX2 build). Install the
  ones you need and, in each, grant it access to the folder your ROMs live in (e.g. your `roms/psx`
  folder). EmuShelf hands the game to the emulator using that folder — it does not and cannot grant the
  emulator access on your behalf.
- **Import from the same folder you granted the emulator.** When you add a system's games in EmuShelf,
  point it at the same folder you granted the emulator. That keeps multi-disc games (a per-game folder
  with `Disc 1`/`Disc 2` and an `.m3u`) launchable — see DECISIONS 2026-08-22.

## Notes and limitations

- **Not on Google Play.** EmuShelf is GPLv3 and needs all-files access; both rule out Play distribution.
- **Developer verification.** Google is phasing in developer-verification for sideloaded apps on
  certified devices with Play services (enforcement begins in a first set of countries on
  30 September 2026, expanding afterward). On affected devices, unverified apps install through Android's
  advanced sideloading flow or ADB rather than a one-tap install. This does not block installation, but
  the exact flow depends on your device and Android version; follow the on-screen prompts, or use ADB
  (Option B), which is unaffected.
