# Metadata enrichment

EmuShelf currently enriches a game with two presentation fields: its canonical title and one
cover. The feature is deliberately optional. EmuShelf ships provider URLs and matching logic,
but no game artwork or metadata catalog.

## User flow and privacy

Scanning is always local. EmuShelf first discovers and saves the game using its filename as the
initial title. It does not contact a metadata source during the scan and never performs a network
metadata pass at startup.

After the first successful import, the app asks the user to choose one of three behaviors:

- **Not now** leaves metadata fetching disabled and does not ask again. The user can still fetch
  manually or enable automatic fetching in Settings.
- **Fetch once** enriches only the games just imported.
- **Always after import** enriches newly imported games in the background and saves that choice.

Settings also provides **Fetch missing metadata** for the whole library and an equivalent action
inside each platform section. The automatic-after-import toggle can be changed there at any time.

Identifier extraction reads game data locally and read-only. Only provider request values such as
a normalized product code, disc id, or exact canonical-title filename are used in network URLs.
Game files and local filesystem paths are never uploaded. Catalog requests identify only the
platform. Downloaded catalogs are cached under `Cache/Metadata/Catalogs/` for 30 days; temporary
downloads use `Cache/Metadata/Downloads/`; accepted covers use `Covers/` and the existing scaled
thumbnail cache under `Cache/Covers/`. All locations remain portable beside the executable.

## Processing pipeline

For each requested game, `GameMetadataService` performs the following bounded background work:

1. Select the `MetadataSystemProfile` registered for the game's stable system id.
2. Reuse the game's already-extracted identifiers when present; otherwise run its
   `IGameIdentifierExtractor` against the local entry and persist every typed identifier.
3. Ask `IGameMetadataCatalog` for an exact match using the profile's declared key kind.
4. Apply the canonical title only if the current title is filename- or catalog-derived.
5. Ask the profile's `IGameArtworkProvider` instances for candidates in priority order, then
   download the first available image through `IRemoteArtworkDownloader`.
6. Stage the image with the normal cover service, atomically associate it only if the game still
   has no cover, and record the provider id and source URI.
7. Record `Matched`, `Partial`, `Unmatched`, or `Failed` plus the last error and attempt time.

Identifier extraction is targeted, not a scan. The PlayStation extractor reads the disc's
`SYSTEM.CNF` boot record through the shared `CdSectorReader`/ISO9660 reader — a few kilobytes —
and only falls back to a bounded, early-exit ASCII scan when an image has no readable layout.
Because identifiers are cached, a re-run never re-reads a disc whose serial is already known.

Steps 1–4 (disk-bound identification) and steps 5–6 (network-bound download) run under separate
concurrency limits so cover downloads are not throttled behind disc reads, and only one enrichment
run is active at a time. HTTP requests stream responses with explicit size limits (12 MiB catalogs
and 8 MiB covers) over a pooled connection. Missing, timed-out, unsuccessful, or invalid artwork
candidates are logged and fall through to the next provider. A catalog outage does not prevent an
identifier-only provider from finding a cover.

No metadata operation changes library identity, deletes an entry, or writes to a game file. A
manual title has `GameTitleOrigin.User`, a manual or pre-migration cover has
`GameCoverOrigin.User`, and neither is overwritten. A manual edit made while a download is in
flight also wins the final database compare-and-set.

## Current platform profiles

| System | Local exact identifier | Title catalog | Cover order |
| --- | --- | --- | --- |
| PlayStation | Product code from disc data; CUE/M3U references are followed | Libretro redump DAT, keyed by normalized serial | xlenore PSX by serial, then Libretro by canonical title |
| PlayStation 2 | Product code from disc data; CUE/M3U references are followed | Libretro redump DAT, keyed by normalized serial | xlenore PS2 by serial, then Libretro by canonical title |
| GameCube | Six-character disc id from ISO/GCM/CISO/RVZ/WBFS header | Libretro GameTDB DAT, keyed by disc id | Libretro by canonical title |
| Wii | Six-character disc id from ISO/CISO/RVZ/WBFS header | Libretro GameTDB DAT, keyed by disc id | Libretro by canonical title |

The PlayStation extractor reads the boot product code from the disc's `SYSTEM.CNF` record via the
shared ISO9660 reader. It reaches `SYSTEM.CNF` through raw images, CUE tracks, CSO/ZSO compressed
images, and CHD images alike — each adapter decompresses only the blocks or hunks that back the
sectors it reads — and reads a PlayStation EBOOT (`.pbp`) serial from its embedded PARAM.SFO
`DISC_ID`. The CHD reader supports `zlib`/`lzma` (DVD) and `cdzl`/`cdlz` (CD) hunks; `huff`, `flac`,
and `cdfl` hunks are unsupported and fall back. When an image has no readable layout the extractor
scans at most the first 16 MiB and stops at the first product code. For an unsupported container it
uses a product code only when one is explicitly present in the filename, such as
`Game Name [SLUS-12345].chd`; otherwise the game remains unmatched. This is a safe fallback, not
fuzzy matching.

## Code ownership

The stable contracts and value types live in `src/EmuShelf.Core/Metadata/`. SQLite schema and
provider-agnostic storage/download behavior live in `src/EmuShelf.Infrastructure/Metadata/`.
Platform extractors, provider URL rules, and the profile registry live in
`src/EmuShelf.Integrations/Metadata/`. Consent, orchestration, and UI-facing summaries live in
`src/EmuShelf.App/`.

This separation is intentional: the enrichment coordinator should not gain a switch statement
when a platform is added. Platform-specific knowledge belongs in an extractor and one declarative
profile in `KnownMetadataProfiles`.

## Adding another platform

Use this checklist for DS, SNES, PS3, or any later system:

1. **Keep the stable system id.** Register import rules and the library system first. The metadata
   profile must use exactly the same id.
2. **Choose exact evidence.** Extend `GameIdentifierKind` only if none of `Serial`, `DiscId`,
   `TitleId`, `Crc32`, or `Sha1` describes the catalog key. Never use display-title similarity as
   automatic evidence.
3. **Implement a read-only extractor.** Put format/container knowledge in an
   `IGameIdentifierExtractor`. Bound all reads, follow descriptors safely, support cancellation at
   the caller boundary, and return no identifier rather than guessing.
4. **Choose and verify a catalog.** Add its URI and key kind to `KnownMetadataProfiles`. Confirm
   matching semantics, license, update behavior, and file-size limit. If its format is not the
   existing clrmamepro DAT shape, add another `IGameMetadataCatalog` implementation instead of
   leaking parsing rules into the coordinator.
5. **Compose artwork providers.** Add serial/id-addressed providers before title-addressed
   fallbacks. URL construction belongs in `IGameArtworkProvider`; networking and portable storage
   remain shared.
6. **Preserve ownership rules.** New metadata fields must carry origin/provenance and may never
   overwrite user data silently.
7. **Add fixtures and failure tests.** Cover every supported container, multi-disc behavior,
   normalization, exact catalog lookup, 404 fallback, malformed/oversize responses, cancellation,
   and manual-edit races. Verify source bytes and timestamps are unchanged.
8. **Update notices and this table.** Record source links, licensing, limitations, and any
   non-obvious architectural choice in `THIRD-PARTY-NOTICES.md`, this document, and
   `DECISIONS.md`.

Likely identifiers for planned systems are:

- **Nintendo DS:** the ROM header's game code, with a catalog CRC32/SHA-1 fallback where revisions
  share a code.
- **SNES:** CRC32 or SHA-1 of the normalized, headerless ROM stream; copier-header handling must be
  fixture-tested before support is declared.
- **PlayStation 3:** `TITLE_ID` and `TITLE` from `PARAM.SFO`. Directory and disc layouts should
  share an SFO reader, and the title id should be the catalog key; hashing large installed games is
  unnecessary for title/cover enrichment.

These examples are guidance, not implemented support. A new platform should remain visibly
unmatched until its extractor and catalog behavior have deterministic tests.
