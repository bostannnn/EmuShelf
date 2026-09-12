# Steam / GameNative implementation checklist

Updated 2026-09-12. Implementation integrated into the current checkout from the earlier
GameNative experiment and extended with stable import identity and Steam achievements.
See [setup and behavior](steam-gamenative.md).

## Platform and library

- [x] Stable `steam` platform after Arcade; PC icon, 2:3 covers and GameNative registration.
- [x] Original PC artwork with bounded decoded size and documented provenance.
- [x] Desktop/gamepad folder selection, export-folder help, multiple portable roots and rescan.
- [x] Bounded `.steam` parser; positive integer app ID; filename title; invalid files ignored.
- [x] GameNative/app-ID identity, duplicate-root deduplication and moved-shortcut reconciliation.
- [x] Preserve manual titles, play history and library IDs on reconciliation.
- [x] Reject conflicting shortcut identity without modifying user files.
- [x] Validate availability against shortcut content; preserve disconnected/inaccessible roots.
- [x] Steam capsule artwork and conservative ScreenScraper PC Windows matching.

## Launch and presentation

- [x] Android package/activity visibility, GameNative launch action and integer `app_id` extra.
- [x] Revalidate shortcut before launch; clear Android-only rejection on desktop.
- [x] Local sessions and return handling; exclude GameNative from forced termination.
- [x] Skip EmuShelf save synchronization; GameNative owns Steam cloud uploads.
- [x] Grid/list/shared platform surfaces and midnight PC keep-case shelf geometry/material.
- [x] Shared production/preview rendering profiles.

## Achievements

- [x] Provider-neutral game reference, achievement row, snapshot and read-only service contract.
- [x] RetroAchievements adapter retaining points/hardcore behavior.
- [x] Steam schema/progress API client joining by achievement API name.
- [x] SteamID64/profile URL resolution and local Web API key connection in both settings UIs.
- [x] Android Keystore and Windows DPAPI; explicit session-only fallback on macOS/Linux.
- [x] No secrets in portable settings, URLs or error messages.
- [x] Account/game-scoped snapshot cache, shared icon cache, refresh age and rate-limit handling.
- [x] Offline cache retention, unknown private progress, hidden-achievement reveal.
- [x] Account-switch/disconnect generation guard against late network responses.
- [x] Desktop, gamepad and second-screen viewer routing; omit Steam points/hardcore.
- [x] Expandable Steam/RetroAchievements settings groups, one provider open at a time.
- [x] Whole-library Steam sync with counts, cancellation, duplicate filtering and bounded request pacing.
- [x] Refresh on viewer open/manual request and two bounded post-return attempts.
- [x] Read-only behavior: submission and Steam client authentication stay in GameNative.

## Validation and remaining acceptance

- [x] Full automated suite: 1,348 infrastructure + 1,133 application tests passed.
- [x] Follow-up validation: 127 achievement UI tests passed after the final cache/timeout review fixes.
- [x] Desktop Steam viewer rendered and visually inspected (hidden/locked/unlocked states).
- [x] Desktop/gamepad provider groups rendered and inspected; two final UI tests passed.
- [x] Final Android Release/AOT packaging passed (0 errors; 10 existing/platform warnings).
- [ ] Fresh target-device visual pass: platform row, settings, shelf, achievements and second screen.
- [ ] Fresh game launch/return with current build, including missing GameNative/export cases.
- [ ] Live Steam account: public/private data, real earned timestamps, post-game cloud propagation.

The connected Thor's export directory was inspected read-only and contains 23 `.steam`
shortcuts. No authenticated Steam API result is claimed from fixture tests. No full Steam
client login, backend service, other-store importer or achievement-unlock writer is included.


### Achievement icon follow-up

The Thor cache contained `steamcdn-a.akamaihd.net/steamcommunity/public/images/apps/…`
URLs. The loader rejected that host, and the old CDN also returned 404. The same image at
`shared.fastly.steamstatic.com/community_assets/images/apps/…` returned HTTP 200/image/jpeg.
Normalize only recognized Steam legacy hosts and asset paths to HTTPS on the current CDN,
including cached URLs, and release the per-row loading guard after failed downloads.
134 achievement tests passed, followed by 21 Steam tests including image decoding and retry.
