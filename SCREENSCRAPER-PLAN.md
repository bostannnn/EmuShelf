# ScreenScraper integration research and implementation plan

Status: implementation in progress on `codex/screenscraper-planning`.

Implemented foundation (2026-08-01):

- capability-aware registry and independent toggles for built-in enrichment, ScreenScraper, and
  DuckDuckGo artwork search;
- on-demand metadata values, localized descriptions, media assets, provider matches, and
  field/selection provenance, persisted by SQLite schema v13 without expanding the hot `Game`
  projection;
- ScreenScraper settings for all NeoStation-equivalent data fields and the initial box-front,
  screenshot, wheel, and fanart media set;
- fixture-backed `ssuserInfos.php`/`jeuInfos.php` client boundary, typed quota/error results,
  hash-plus-size validation, safe public provenance links, HTTPS-only media candidates, regional
  and language selection, and an explicit mapping for every currently supported EmuShelf system;
- DPAPI-protected ScreenScraper account storage on Windows and session-only storage on other
  platforms; developer credentials are environment-provisioned and remain absent from source,
  settings, SQLite, and logs.

Still gated on the requested developer approval: validate the provisional system mapping against
`systemesListe.php`, capture sanitized real response variants, record the approved attribution and
caching terms, and enable any live composition/UI path. No live request is made at startup.

Date researched: 2026-08-01

Reference NeoStation revision: `458c17513d1aaf9f79e456cfdc2a199e8464095d`

## Outcome

ScreenScraper should be introduced as a separate authenticated provider with full metadata and
media capabilities. It should share a provider/settings surface and the eventual game-scraping UI
with the existing DuckDuckGo cover search, but it should not implement the existing
`IGameArtworkSearchProvider` contract or silently replace the current exact-match enrichment
pipeline.

The provider families have materially different trust and operational behavior:

| Provider | Capability | Match trust | Automatic use |
| --- | --- | --- | --- |
| Built-in catalog and artwork chain | Exact title and box-front enrichment | Verified local identifier/catalog match | Existing opt-in behavior |
| ScreenScraper | Verified metadata plus several media kinds | Hash/serial match; manual confirmation for title fallback | Disabled until connected; batch use requires explicit consent |
| DuckDuckGo Images | General web image search | Unverified until the user reviews a result | Never automatic |

The Settings UI should therefore list these sources together, show what each can do, and allow
each source to be enabled or disabled. Capability and trust labels must still prevent DuckDuckGo
from looking equivalent to an authenticated metadata service.

## Constraints already fixed by the project

- ScreenScraper is M31 continuation Phase 5. The current DuckDuckGo controller picker remains the
  earlier artwork-search phase.
- ScreenScraper must use its API, not scrape its website.
- User and developer credentials, quota, locale, platform mapping, cache, attribution,
  provenance, and provider failure isolation are required groundwork.
- Matching is exact whenever EmuShelf applies data without review. Title search is only a
  controlled fallback and must not silently alter a game.
- Manual title, metadata, and media choices always win.
- All network work remains opt-in and must not block normal library use.
- Game files remain read-only. Hashing and serial extraction may read them but never rewrite them.
- Storage remains portable beside the executable, while secrets use platform protection and are
  never written to `settings.json` or logs.
- The grid remains virtualized and must not load descriptions or full media manifests for every
  visible game.

## Current EmuShelf shape

EmuShelf currently has three adjacent but distinct pieces:

1. `GameMetadataService` identifies a game through a `MetadataSystemProfile`, resolves an exact
   Libretro catalog match, then tries ordered artwork providers. It applies only a catalog title
   and a downloaded cover.
2. `SqliteGameMetadataStore` persists extracted identifiers, one catalog match/attempt, and cover
   provenance. The public `Game` projection contains identity, title, cover, availability, and
   external-library provenance only.
3. `DuckDuckGoArtworkSearchProvider` implements `IGameArtworkSearchProvider` for an explicit
   result picker. `CoverSearchViewModel` receives exactly one search provider, previews at most 24
   results through the safe downloader, and applies only the image the user selects.

This is a useful boundary. The missing abstraction is a provider registry/capability layer, not a
larger DuckDuckGo interface.

## What NeoStation does

NeoStation is a useful behavior and field reference, not source to copy. Its repository is GPL-3.0
and EmuShelf should independently implement the integration against the official API.

Useful patterns:

- Developer credentials are supplied at build time; users connect their own ScreenScraper
  account.
- It has separate account, scrape mode, media, region, language, and system configuration.
- It supports single-game rescraping and an explicitly started batch scraper.
- It reads `maxthreads` and quota information from the returned user object.
- It selects media by region/language and prefers `wheel-hd` over `wheel` and `ss-hd` over `ss`.
- Its primary media set is fanart, screenshot, wheel, box front, and video.
- It stores localized descriptions plus developer, publisher, genre, release date, rating, and
  players.
- Its per-game editor exposes title, developer, publisher, genre, six localized descriptions, and
  screenshot/wheel/fanart/box-art replacement.

Shortcuts EmuShelf must not inherit:

- NeoStation base64-encodes the user password and calls that encryption. EmuShelf must use the
  existing platform credential boundary.
- NeoStation disables TLS certificate validation for its ScreenScraper client. EmuShelf must never
  do this.
- It tries a name lookup before hashing in batch mode and retries name lookup in single-game mode.
  EmuShelf's automatic path must be hash/serial-first.
- It does not consistently send the ROM size even though the current API documentation requires a
  hash plus size unless ScreenScraper grants a waiver.
- A batch path hard-codes a quota ceiling rather than honoring the returned per-user limits.
- Media is written directly by ROM name without EmuShelf's validation, atomic import, ownership,
  or provenance rules.
- `is_fully_scraped` is too coarse: one boolean cannot describe partial fields, selected media,
  provider ownership, or a failed refresh.
- Some metadata operations key only by filename, which can collide across systems. EmuShelf should
  always key by its stable game id.

## Official API findings and prerequisite

The current official WebAPI v2 documentation says:

- API v2 is beta and may change without notice.
- Integration is permitted for fully free distributed applications; otherwise prior approval is
  required. Developers must present their software in the ScreenScraper forum to receive developer
  credentials.
- Requests use developer id/password and a software name. ScreenScraper user credentials are
  optional at the protocol level, but EmuShelf should require a connected user for this feature so
  quota and account ownership are explicit.
- `jeuInfos.php` accepts CRC32, MD5, SHA-1, system id, ROM type/name/size, serial, or a forced game
  id. Unless a waiver is granted, at least one hash plus the ROM size is required.
- `jeuRecherche.php` returns up to 30 title matches ranked by probability.
- User responses include allowed concurrent threads, request counts, request limits per minute/day,
  failed-lookup limits, and download speed.
- The client is required to manage returned quotas. Important statuses include authentication
  failure (403), not found (404), API unavailable (401/423), blacklisted client/version (426),
  concurrency/rate limit (429), daily quota exhausted (430), and too many negative lookups (431).
- The API exposes the requested metadata fields and many media types. The first EmuShelf media
  scope maps to `ss`/`ss-hd`, `fanart`, `wheel`/`wheel-hd`, and `box-2D`.
- ScreenScraper's site identifies contributed material as Creative Commons
  Attribution-NonCommercial-ShareAlike 4.0 and lists multiple upstream media sources.

Before client implementation, the project owner must obtain ScreenScraper developer credentials
and the integration terms applicable to EmuShelf. The resulting written rules must settle:

- required in-app attribution and third-party notices;
- whether and how long metadata/API responses may be cached;
- whether downloaded media may remain in a user's portable local library;
- whether EmuShelf may rely on serial-only requests for disc/container formats;
- how developer credentials are expected to be provisioned in distributed builds;
- any software-name/version and minimum-version requirements.

No developer password should be committed to Git. A distributed client credential cannot be made
truly secret inside a desktop binary; build-time injection limits accidental disclosure but is not
cryptographic protection. The ScreenScraper-approved provisioning rules are therefore a release
gate, not something to infer from NeoStation.

Official references:

- https://www.screenscraper.fr/webapi2.php
- https://api.screenscraper.fr/membreinscription.php
- https://github.com/misobadev/neostation-frontend

## Proposed domain model

### Keep `Game` lean

Do not add descriptions and several media paths directly to `Game`. `Game` is the hot library-grid
projection and is loaded in bulk. Long localized descriptions and media provenance are detail data
that should be fetched only for a selected game or scraper surface.

Add an on-demand aggregate conceptually shaped like:

- `GameDetails`
  - canonical/display title projection;
  - developer;
  - publisher;
  - genres;
  - localized descriptions;
  - release date, players, and rating reserved for the same first schema even if their UI is
    deferred;
  - provider matches and per-field origin/provenance.
- `GameMediaAsset`
  - kind: `BoxFront`, `Screenshot`, `Wheel`, `Fanart` initially;
  - local path;
  - selected/active state;
  - origin: provider download or user import;
  - provider id and provider game/media id;
  - source URI;
  - region and language;
  - format, dimensions, and server-provided hashes when present;
  - fetched timestamp.
- `GameProviderMatch`
  - game id and provider id;
  - provider game id and optional ROM id;
  - match method (`Sha1`, `Md5`, `Crc32`, `Serial`, `ProviderGameId`, or
    `UserSelectedTitleSearch`);
  - evidence and system mapping version;
  - status, last attempt, last error, and timestamps.

Descriptions should be locale-keyed rows, not six fixed properties. The first UI can show English
and the preferred language, while persistence retains every supported locale returned by the
provider.

Scalar field values should retain field-level provenance. A single record-level origin is
insufficient when a user edits the title but leaves provider-owned developer and genre values in
place. A practical SQLite design is a row per canonical metadata field/locale with validated string
storage and origin columns, materialized into typed `GameDetails` by the repository. Release date
and rating parsing/validation belongs at the domain boundary. If future filtering makes typed SQL
columns necessary, they can be added as indexed projections without changing provider contracts.

### Box art and the existing cover

`box-2D` is the canonical `BoxFront` media kind. The currently selected `BoxFront` continues to
project to `Games.CoverPath` so the virtualized shelf and existing thumbnail cache do not need an
all-at-once rewrite. New media provenance is authoritative; the cover columns remain the fast grid
projection during migration.

A DuckDuckGo image chosen by the user also becomes a `BoxFront` asset, with provider id
`duckduckgo-image-search`, a user-selected origin, and its source URI. It remains unverified and is
never eligible for automatic application.

### Portable storage

- Keep active cover originals under `Covers/` and their display thumbnails under `Cache/` through
  the existing cover service.
- Put other owned, selected media under `Data/Media/{gameId}/{kind}.{extension}`.
- Put short-lived response manifests and preview thumbnails under
  `Cache/ScreenScraper/{providerGameId}/`.
- Use game ids rather than titles or ROM filenames for owned paths.
- Store relative paths through `IRelativePathResolver`.
- Apply the current image content-type, size, signature, decode, and SSRF/redirect checks. Import
  atomically, then switch the database reference; remove uncommitted staged files on failure.

Do not cache credentials, raw request URLs, or API responses containing credential-bearing URLs in
diagnostics.

## Proposed provider contracts

Keep the narrow `IGameArtworkSearchProvider` contract for user-driven web image search. Add a
separate full scrape contract and a shared descriptor/registry:

- `GameScrapeProviderDescriptor`
  - stable id and display name;
  - capabilities (`Metadata`, `BoxFront`, `Screenshot`, `Wheel`, `Fanart`, `Batch`,
    `TitleSearch`);
  - trust level and whether user confirmation is required;
  - account/configuration state.
- `IGameScrapeProvider`
  - returns a typed `GameScrapeResult` containing match provenance, field candidates, media
    candidates, and quota state;
  - performs no database or final-file mutation.
- `IGameScrapeProviderRegistry`
  - exposes enabled providers and their capabilities to Settings, Desktop, and Gamepad view
    models;
  - does not encode provider precedence in UI code.
- `IGameScrapeApplicationService`
  - owns field precedence, consent, provider-owned refresh, safe media import, and atomic database
    updates.

This keeps ScreenScraper parsing/transport in Infrastructure, system mappings in Integrations,
domain contracts in Core, and orchestration/view models in App.

## Provider settings and toggles

Add a `Scraping` settings section backed by one portable `ScrapingSettings` record. Secrets remain
outside it.

Recommended provider rows:

1. **Built-in catalog & covers** — enabled by default; exact-match title/box-front enrichment.
2. **ScreenScraper** — disabled until connected; metadata and selected media; account/quota status
   and Connect/Disconnect actions.
3. **DuckDuckGo Web Images** — enabled by default to preserve the existing explicit cover picker;
   labelled “manual cover search only.”

Recommended ScreenScraper settings:

- preferred language, with application language as the initial value and English fallback;
- ordered region preference, initially world, user region, Europe/US, then remaining regions;
- field toggles: title, developer, publisher, genre, descriptions;
- media toggles: box front, screenshot, wheel, fanart;
- per-system enablement based on an explicit EmuShelf-to-ScreenScraper map;
- batch mode: fill missing values (default) or refresh ScreenScraper-owned values;
- automatic after-import use off by default, independent from the existing built-in metadata
  preference.

Do not add video in the first slice. It materially increases bandwidth, disk usage, decoding, and
living-room playback work without being needed by the supplied game-settings design. The model and
provider capability enum should allow it later.

## Credentials and request coordination

Generalize the current RetroAchievements credential pattern into a platform-neutral secret store,
or add a parallel ScreenScraper credential store with the same rules:

- Windows: a DPAPI-protected blob under `Settings/` for the ScreenScraper username/password pair;
- macOS/Linux until a verified Keychain/libsecret implementation exists: session-only storage and
  a clear reconnect-required state;
- no plaintext secret in SQLite, JSON, diagnostics, exception messages, crash reports, or tests;
- Disconnect waits for in-flight account work, cancels queued work, then clears the secret and
  account state.

The ScreenScraper client needs a single request coordinator for metadata and media calls:

- cap concurrency at the latest returned `maxthreads` and an EmuShelf safety ceiling;
- track returned per-minute/day and failed-lookup quotas rather than inventing a fixed limit;
- parse 401/403/404/423/426/429/430/431 into distinct result states;
- retry only transient timeouts and server failures with bounded exponential backoff and jitter;
- honor server cooldowns and cancellation;
- never retry authentication, not-found, exhausted-quota, or blacklisted-version responses;
- use ordinary TLS validation;
- log endpoint names and result states, never query strings.

Because credentials are GET parameters, tests must explicitly prove that user password, developer
password, and complete request URI never reach the logger.

## Identification plan

The official contract favors hash plus file size. EmuShelf already stores exact identifiers, but
their hashing scope varies by platform, so a `Sha1` value must not automatically be assumed to be a
ScreenScraper full-file hash merely because its algorithm name matches.

Add a provider-specific, cached file fingerprint record containing:

- file size;
- last-write/identity validation data used only to decide whether the cached fingerprint is stale;
- CRC32, MD5, and SHA-1 calculated in one cancellable streaming pass where applicable;
- explicit scope (`WholeFile`, normalized cartridge payload, logical disc track, archive member,
  directory, or unsupported).

Lookup order:

1. Use a compatible cached whole-file hash plus size.
2. Compute a compatible fingerprint on explicit single/batch consent, off the UI thread.
3. Use a validated serial/product/disc id only where the approved ScreenScraper contract confirms
   the relevant system accepts it.
4. Offer `jeuRecherche.php` title candidates only in a user-reviewed picker. Persist the selected
   ScreenScraper game id and mark the match method as user-selected title search.

Do not hash an M3U text file and call it the game. Multi-disc/container rules need deterministic
per-format handling. A primary validated disc serial may identify a release when permitted;
otherwise the user chooses a title result. Arcade ZIP identity likewise cannot assume the ZIP
container checksum is the canonical ROM-set checksum.

Initial system mapping candidates taken from the inspected NeoStation definitions are:

| EmuShelf | Candidate ScreenScraper id |
| --- | ---: |
| PlayStation | 57 |
| PlayStation 2 | 58 |
| PlayStation 3 | 59 |
| PSP | 61 |
| GameCube | 13 |
| Wii | 16 |
| Mega Drive / Genesis | 1 |
| Nintendo DS | 15 |
| Game Boy Advance | 12 |
| Super Nintendo | 4 |
| Dreamcast | 23 |
| FinalBurn Neo Arcade | 75 |
| Game Boy Color | 10 |

These are now explicit version-1 Integration mappings covered for every supported EmuShelf system,
but remain a release-gated provisional set until they are validated against `systemesListe.php`
using approved developer credentials. Do not infer mappings at runtime from display names.

## Apply and overwrite rules

- Batch `Fill missing` writes only empty fields/media.
- Batch `Refresh ScreenScraper values` may replace only values currently owned by ScreenScraper.
- A manual edit/import becomes user-owned and is never replaced by any provider.
- Existing built-in catalog values are not replaced by ScreenScraper in an automatic batch unless
  the user previews and confirms the change.
- A force-rescrape is provider-scoped. It does not erase user values or unrelated provider assets.
- Partial success remains partial: each field/media asset records its own result. There is no
  global “fully scraped” boolean.
- Provider errors never delete an existing game, metadata value, media file, or last-good cache.

## UI direction

The supplied NeoStation-inspired screenshots translate naturally into one shared game-scraping
view model rendered by Desktop and Gamepad surfaces:

- **Data**: force/rescrape action, title, developer, publisher, genre, localized descriptions.
- **Media**: box art, screenshot, wheel, fanart; each row shows current media, provenance, and a
  Change action.
- The provider selector is visible when more than one enabled provider can satisfy the action.
- ScreenScraper exact matches can preview all returned fields/media before Apply.
- ScreenScraper title-search matches always require candidate selection and confirmation.
- DuckDuckGo appears only for box-art Change/Search, never for developer/description or batch.
- Local file import remains available for every image kind.
- Apply/Cancel restores the same game/platform/controller focus.

The Gamepad surface should reuse the M31 overlay/focus model and controller-safe text-entry work.
Do not fork provider rules or persistence between Desktop and Gamepad.

## Implementation sequence

### Phase 0 — approval and contract fixture

1. Obtain ScreenScraper developer approval and credentials.
2. Record the approved authentication, attribution, caching, quota, and credential-provisioning
   rules in `DECISIONS.md`.
3. Capture sanitized JSON fixtures for account info, exact game match, title search, no match,
   partial media, malformed payload, and every important HTTP error.
4. Validate and freeze the supported-system mapping.

Exit: terms and mappings are documented; no live credential is needed in tests.

### Phase 1 — provider-neutral model and persistence

1. Add provider descriptors/capabilities and the registry.
2. Add on-demand details, localized values, media assets, provider matches, and field-level
   provenance to Core.
3. Add the next SQLite migration and repositories; keep `Game`/cover columns as grid projections.
4. Add `ScrapingSettings` and migration-safe JSON defaults.
5. Generalize safe cover import into a media service without changing current cover behavior.

Exit: the model can represent the supplied Data/Media tabs and DuckDuckGo provenance without a
ScreenScraper network client.

### Phase 2 — ScreenScraper account and client

1. Add secure credential storage and build-time developer credential provisioning.
2. Add the tolerant JSON parser and typed account/quota/error models.
3. Add the request coordinator, retry/cooldown policy, and secret-redaction tests.
4. Add the Settings provider row with Connect/Disconnect and quota state.

Exit: account validation and fixture-backed client tests work; no game data is applied.

### Phase 3 — identification and preview

1. Add cached, scoped fingerprinting and system mapping profiles.
2. Implement exact `jeuInfos.php` requests and provenance.
3. Add serial routes only after they are validated per system.
4. Add manual `jeuRecherche.php` fallback and a result preview model.

Exit: a single game produces a deterministic preview or a typed failure without mutating the
library.

### Phase 4 — safe metadata/media application

1. Apply selected scalar/localized values using the overwrite rules.
2. Resolve region/language media variants and import box front, screenshot, wheel, and fanart
   atomically.
3. Project selected box front into the existing cover path and thumbnail flow.
4. Add provider-scoped rescrape and local replacement.

Exit: one game can be previewed, applied, cancelled, refreshed, and restored offline with complete
provenance.

### Phase 5 — unified Desktop/Gamepad scraper UI

1. Add the Data/Media game-settings surface backed by the shared view model.
2. Integrate ScreenScraper and DuckDuckGo through capabilities, not conditional view code.
3. Add controller navigation, loading, partial/error/quota states, and focus restoration.
4. Test 1280×800 and a large 16:9 viewport with long descriptions and missing media.

Exit: the supplied interaction is complete without leaving Gamepad mode except for a native local
file picker.

### Phase 6 — explicit batch scraping

1. Add system/field/media selection, game count, mode, and confirmation.
2. Process with the account's current concurrency/quota and cancellable progress.
3. Preserve last-good data on cancellation/offline/quota exhaustion and produce a resumable
   per-game summary.
4. Keep automatic after-import ScreenScraper use off until separately enabled by the user.

Exit: batch operation is safe, quota-aware, cancellable, resumable, and never required for ordinary
library use.

## Required tests

- Fixture parsing for optional/malformed/localized fields and media variants.
- System mapping for every supported EmuShelf system.
- Fingerprint scope, cache invalidation, cancellation, multi-disc/container rules, and proof that
  source bytes/timestamps are unchanged.
- Authentication and every documented HTTP state; transient retry/cooldown timing with a fake
  clock.
- Dynamic concurrency and quota boundaries.
- Secret redaction from logs, exceptions, diagnostics, settings, and database.
- Field-level precedence: fill missing, provider refresh, manual override, competing provider, and
  partial failure.
- Media validation, SSRF/redirect policy, size/signature/decode failures, atomic replacement,
  cleanup, and relative paths.
- DuckDuckGo disabled/enabled behavior and proof that its result cannot enter automatic metadata.
- Desktop/Gamepad command routing, candidate selection, cancellation, focus restoration, and
  populated/empty/error/quota layouts.
- Windows build/test plus macOS build compatibility; no platform API leaks into Core.

## Decisions adopted for the foundation

These choices are recorded in `DECISIONS.md`; the approval/terms-specific decision remains open:

1. Keep `Game` lean and store detail metadata/media in an on-demand aggregate.
2. Put built-in enrichment, ScreenScraper, and DuckDuckGo in one capability-aware provider settings
   surface; keep their trust and automatic-use rules distinct.
3. Include title, developer, publisher, genre, localized descriptions, release date, players, and
   rating in the first persistence model; expose the screenshot's smaller data set first.
4. Limit first media support to box front, screenshot, wheel, and fanart; defer video.
5. Disable ScreenScraper until account connection and keep automatic-after-import use separately
   off by default.
6. Use fill-missing as the default batch mode; refresh only ScreenScraper-owned values on explicit
   request.
7. Require manual confirmation for every title-search fallback.
8. Treat ScreenScraper approval, system-map validation, and written attribution/caching rules as
   implementation gates.
