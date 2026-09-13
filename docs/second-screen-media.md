# Second-screen artwork and media

## Artwork behavior

Every platform uses the same resting companion view: saved fanart with a separate logo.
If no logo is available, the game title is shown. Screenshots and covers belong to the
gallery rather than serving as temporary resting backgrounds. Changing selection clears
stale artwork immediately; a running game remains the companion's target until returning
to the library.

Selecting a game reads local artwork once and never starts a Steam provider request.
Steam enrichment is separate from presentation:

- Settings → Artwork & metadata → Fetch missing metadata fills missing Steam fanart,
  logos and screenshots, including games with existing titles and covers.
- Fetch after import uses the same enrichment when enabled.
- The gallery's Fetch missing artwork action handles the current game.
- Existing ScreenScraper and custom artwork is preserved. Steam fills missing kinds.
- Newly saved fanart can appear while a logo download remains pending. Updates are
  applied only while the affected game remains the companion's target.

Downloaded artwork is stored as provider-attributed GameMediaAssets under Covers/Media,
using the same details store as ScreenScraper. These files survive cache eviction.
Artwork cached by earlier media-preview builds is migrated locally on first access,
without requesting it again or replacing existing saved artwork.

## Gallery interaction

Swipe horizontally on resting artwork or tap the photo icon to open the gallery.
Screenshots appear first. The gallery occupies the entire second display and hides the
dock. Photos use an aspect-fitted black canvas with compact controls over gradients;
tapping a photo hides or restores the controls.

Swipe left/right or use arrows/controller navigation to change media. Swipe down or use
Close/B to restore the dock. Video pages use screenshot posters and require Play before
video bytes are downloaded. Playback starts muted; tapping pauses/resumes. Closing,
changing media/game, presentation teardown, or opening a dock app stops playback.
The viewer stays awake while open during gameplay.

Android video uses a centered native VideoView with narrow top and bottom control bands,
since native views paint above Avalonia. ScreenScraper MP4s play from saved local files.
Steam supports publisher MP4 microtrailers, not full adaptive trailers. The viewer does
not automatically initiate ScreenScraper scraping.

## Provider and storage boundaries

ICompanionMediaSource supplies the shared media list and saved-artwork notifications.
IGameMediaEnricher handles explicit/import-time acquisition. The shared view model owns
paging, cancellation, decoded-image lifetime and video state; Android provides the native
video host and display lifecycle wiring.

Steam metadata uses public StoreBrowse assets. A missing direct publisher logo may use
the public SteamCMD metadata mirror to resolve its content-hashed CDN path. Requests are
bounded and URLs must belong to the requested numeric app ID. No Steam account credentials,
game files or game paths are sent.

Cache/CompanionMedia holds recreatable manifests and lazy payloads: manifests have a
seven-day TTL, failed requests have cooldowns, and payloads have a 256 MiB trimming target.
Cached reads do not wait behind downloads. Video descriptors may be read from an existing
manifest, but video download starts only on Play. Persistent artwork and game/save files
are outside cache trimming.

## Validation

161 distinct focused tests passed across companion media, SQLite details, second-screen
UI, metadata enrichment and gamepad settings, with targeted reruns after the final changes.
Coverage includes:

- Provider URL/app-ID validation and preservation of existing artwork selections.
- Offline reads, cache eviction and migration of preview-cache artwork.
- Saved artwork access during blocked downloads and fanart updates before logo completion.
- Inclusion of Steam games with complete titles/covers in missing-artwork enrichment.
- Gallery paging, cancellation, standby and a full 538 × 468 logical viewport with the
  dock hidden and restored after dismissal.

Android Release publish succeeded. Release-signed versionCode 861
(1.8.5-media-preview5) was installed over the existing Thor app with data retained.
Final-device checks verified saved Ape Out fanart/logo, its eight-item gallery, and a
per-game fetch that preserved its complete artwork. Earlier device checks verified
full-display gallery layout, native Steam preview playback and downward dismissal.

A full library-wide network fetch was not started on the device; its inclusion logic is
covered by the service test. Full gameplay, both launch-screen placements, offline-device
behavior, native resume, and local ScreenScraper MP4 playback still need device acceptance.
Gameplay testing stopped before disrupting an existing Steam session on another device.
