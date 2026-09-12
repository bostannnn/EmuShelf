# Steam through GameNative

## Add games

1. In GameNative, enable frontend export/sync and choose an export folder.
2. In EmuShelf, open Settings → Emulators → Steam and add that **export folder**.
   Choose the folder containing `.steam` shortcuts, not the installed `.exe` files.
3. Games appear under Steam with a PC icon in grid, list and shelf modes. Launch opens
   GameNative on Android; GameNative must be installed and still have that game installed.
4. After changing installations, use Steam's Rescan action. EmuShelf never modifies
   shortcuts or game installations. Forgetting a folder preserves existing library entries.

A shortcut contains only a positive decimal Steam app ID. Its filename supplies the initial
name. The stable library identity is GameNative + Steam app ID: duplicate exports do not
create duplicate games, and moving an export preserves history and manually edited titles.
If an existing shortcut is replaced with a different app ID, remove the old library entry
and import the replacement. This prevents attaching one game's history to another game.

Artwork uses the Steam portrait capsule. Optional ScreenScraper metadata uses PC Windows
(platform 138); ambiguous titles require manual selection. Steam uses a dedicated midnight
keep-case profile in the 3D shelf. Other stores and arbitrary executables are not supported.

Desktop builds can organize and scrape Steam games; GameNative launching requires Android.
GameNative is exempt from “close emulator on return” to allow its Steam uploads to finish.
GameNative owns Steam login, game installation, achievement submission and cloud saves.
EmuShelf records its own local play sessions and does not request a Steam save folder.

## View Steam achievements

Open Settings → Achievements → Steam. Enter your SteamID64 or HTTPS Steam Community
profile URL, plus a personal [Steam Web API key](https://steamcommunity.com/dev/apikey).
Each provider has its own expandable settings row. Open **Steam → Sync all Steam achievements**
to update every added Steam game at once, or open a single game's Achievements action.
Bulk sync shows counts, supports cancellation, and stops on an account change, authentication
failure, network outage or rate limit. Private/unavailable games are counted separately. Desktop, gamepad and Android second-screen
views select Steam automatically; console games continue to use RetroAchievements.

The viewer shows earned/total achievements, icons, descriptions and unlock times. Legacy
Steam achievement image URLs are normalized to the current Steam CDN when read, including
URLs already stored in the local cache; no new achievement sync is required for that repair. Steam
has no RetroAchievements points or hardcore mode. Hidden locked achievements require an
explicit reveal. Private or unavailable progress is shown as unknown, never as zero earned.
The key does not bypass Steam privacy settings; game details must be available to the API.

Progress caches are separated by Steam account and game. Bulk sync publishes one library
update when it finishes, including when cancelled, so completed results remain visible. Opening a stale viewer or pressing
Refresh fetches data; returning from GameNative schedules two bounded refresh attempts to
allow Steam submission to finish. Offline refreshes preserve cached results. Reconnecting
or switching accounts cannot attach a previous account's in-flight response to the new one.

The API key is encrypted using Android Keystore or Windows DPAPI. On macOS/Linux it remains
in memory for the session and must be entered again after restart. Portable settings contain
only profile identity. Keys are never included in request URLs or application error messages.
Disconnect removes the saved connection; cached data is no longer selected by the viewer.

This is a read-only account link, not a Steam client login. Steam OpenID provides identity,
not a Web API key or access to private achievement data. No backend or embedded Steam
password/Steam Guard flow is needed for this implementation.

## Verification

Automated coverage includes shortcut validation, Android integer intent payloads, artwork
identity, matching, import identity/history, API responses, privacy, cache isolation, stale
account responses and the shared viewer. Full desktop and Android build results are recorded
in the implementation checklist. Live authenticated Steam refresh and a fresh device
launch/return test require final acceptance; fixture tests do not establish those results.

## Contract references

- [GameNative launch parser](https://github.com/utkarshdalal/GameNative/blob/master/app/src/main/java/app/gamenative/utils/IntentLaunchManager.kt)
- [Steam user statistics API](https://partner.steamgames.com/doc/webapi/ISteamUserStats)
- [Steam authentication and OpenID](https://partner.steamgames.com/doc/features/auth)
