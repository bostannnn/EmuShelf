# In-app auto-update

EmuShelf updates itself from its GitHub Releases. The check is a passive notification; installing is
one action, and on the Steam Deck it never leaves gaming mode.

## What the user sees

- **On launch** (background, throttled to ~once a day) EmuShelf checks for a newer release. If one
  exists, a banner appears with **Update & restart**, **Later**, and **Skip this version**.
- **Any time**, Settings → About → **Check for updates** does the same on demand. In the Gamepad
  (couch) interface the same actions live in Settings → General, so a controller can always reach
  them without a mouse.
- Automatic checking can be turned off; the manual button still works. This is stored in
  `Settings/settings.json` under `Updates` (`AutomaticallyCheck`, `LastCheckUtc`, `SkippedVersion`).

Only the public GitHub Releases API is contacted. No token is used and nothing about the user or
their library is sent.

## How it works

The whole flow is **check → download → verify → apply → relaunch**:

1. **Check** — `GET /repos/bostannnn/EmuShelf/releases/latest`, compare `tag_name` to the running
   version (stamped from the git tag at build time, see `EmuShelf.App/AppBuildInfo.cs`).
2. **Download** — the portable artifact for this platform is streamed into `Cache/updates/<version>/`.
3. **Verify** — its SHA-256 is checked against the release's published `.sha256` file **before it is
   ever used**. A mismatch deletes the file and aborts.
4. **Apply + relaunch** — platform-specific (below).

The updater reuses the artifacts CI already publishes on every `vX.Y.Z` tag
(`.github/workflows/build.yml`): `EmuShelf-win-x64.zip`, `EmuShelf-linux-x64.AppImage`,
`EmuShelf-macos-arm64.zip`, each with a matching `.sha256`. No packaging changes were needed.

Code layout:

- `EmuShelf.Core/Updates/` — `SemanticVersion`, the result models, and the `IUpdateService` /
  `IUpdateApplier` interfaces (pure, unit-tested).
- `EmuShelf.Infrastructure/Updates/` — `GitHubUpdateService` (check + download + verify) and the
  per-platform appliers, selected by `UpdateApplierFactory`.
- `EmuShelf.App/Services/AppUpdateCoordinator.cs` — orchestration and the banner/Settings state.

## Per-platform apply — and why gaming mode is never left

A running process can't overwrite the code it is executing, so the app must restart. The real goal —
*never drop to the desktop / never leave Steam's gaming mode* — is met differently per platform:

### Linux / SteamOS (AppImage)

The Linux build is a **single AppImage file**. The applier writes the new file beside the old one,
renames it into place (atomic; the running process keeps the old inode), then calls `execv()` on the
same path. `execv` **keeps the same process id**, so a non-Steam shortcut's tracked process never
exits — Steam never registers the game as stopped, and the session stays in gaming mode. No Steam
wrapper script is required.

### Windows

The running `.exe`/`.dll` files are locked, so a short-lived `.cmd` helper waits for EmuShelf to
exit, overlays the new payload onto the app folder, relaunches, and deletes itself. The overlay only
writes program files, so the portable `Data/ Covers/ Cache/ Logs/ Settings/ Saves/` directories are
left untouched. The relaunched app restores the saved interface mode from settings.

### macOS

The user's data lives in `~/Library/Application Support/EmuShelf` (see `DECISIONS.md`), so the whole
`.app` bundle can be swapped. A helper waits for exit, replaces the bundle, and reopens it. Because
EmuShelf isn't notarized, the freshly downloaded bundle has its `com.apple.quarantine` flag cleared
first (EmuShelf downloaded it, so it may) — otherwise Gatekeeper would refuse the replacement.

## Known limitations

- **No delta updates.** Each update downloads the full portable artifact. Fine at EmuShelf's size.
- **macOS is not notarized.** Clearing quarantine on the downloaded bundle lets it launch, but proper
  Developer ID signing + notarization is a separate future task.
- **Windows helper isn't code-signed**, so SmartScreen may prompt once.
- **Dev runs can't self-update.** An AppImage update needs `$APPIMAGE` (a real `.AppImage`), not a
  `dotnet run`; the applier reports this rather than doing something unsafe.
