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
5. Resolve the catalog title (when there is one) and then the game's filename against each
   `IArtworkTitleIndexProvider`'s remote directory listing, ask the profile's
   `IGameArtworkProvider` instances for their own candidates, and download the first available
   image through `IRemoteArtworkDownloader`.
6. Stage the image with the normal cover service, atomically associate it only if the game still
   has no cover, and record the provider id and source URI.
7. Record `Matched`, `Partial`, `Unmatched`, or `Failed` plus the last error and attempt time.

Identifier extraction is targeted, not a scan. The PlayStation extractor reads the disc's
`SYSTEM.CNF` boot record through the shared `CdSectorReader`/ISO9660 reader — a few kilobytes —
and only falls back to a bounded, early-exit ASCII scan when an image has no readable layout.
Because identifiers are cached, a re-run never re-reads a disc whose serial is already known.

Candidate order within step 5 is deliberate. An entry resolved from a provider's own directory
listing is known to exist, so those are probed first — catalog-title matches ahead of filename
matches — and only then the URLs a provider fabricates from a title, followed by local sidecar
artwork. Filename resolution runs for every system, not only the checksum-keyed ones: a
translated, undubbed, patched, or trimmed dump matches no DAT entry, but its filename still
carries the retail title next to the release tags. Title comparison ignores release tags, a
version suffix ahead of them (`Crazy Taxi v1.004 (1999)(Sega)`), and a leading publisher
possessive carried by only one source (`Disney's Donald Duck - Goin' Quackers`). It remains a
whole-title equality after that normalization, never a prefix or substring search, so a sequel or
spin-off cannot borrow another game's cover. Where several regional entries match, the catalog
region wins, then retail releases outrank kiosk demos, prototypes, and control-scheme hacks.

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
| PlayStation 3 | RPCS3's exact nine-character title id, normalized as its product serial | Libretro redump DAT, keyed by normalized serial | GameTDB by serial with regional fallbacks, then Libretro source-indexed title match |
| PSP | `DISC_ID` from `PSP_GAME/PARAM.SFO`, normalized as its product serial | Libretro redump DAT, keyed by normalized serial | Libretro source-indexed title match after the exact serial match |
| GameCube | Six-character disc id from ISO/GCM/CISO/RVZ/WBFS header | Libretro GameTDB DAT, keyed by disc id | GameTDB by disc id, then Libretro by canonical title |
| Wii | Six-character disc id from ISO/CISO/RVZ/WBFS header | Libretro GameTDB DAT, keyed by disc id | GameTDB by disc id, then Libretro by canonical title |
| Mega Drive / Genesis | SHA-1 of the verified normalized cartridge stream | Libretro No-Intro DAT, keyed by SHA-1 | Libretro source-indexed title match after the exact SHA-1 match |
| Nintendo DS | SHA-1 of the verified raw cartridge; header game code is retained only as local evidence | Libretro No-Intro DAT, keyed by SHA-1 | Libretro source-indexed title match after the exact SHA-1 match |
| Game Boy Advance | SHA-1 of the verified raw cartridge; header game code is retained only as local evidence | Libretro No-Intro DAT, keyed by SHA-1 | Libretro source-indexed title match after the exact SHA-1 match |
| Super Nintendo | SHA-1 of the headerless ROM (optional 512-byte copier header normalized away); header title is display-only | Libretro No-Intro DAT, keyed by SHA-1 | Libretro source-indexed title match after the exact SHA-1 match |

A cartridge header game code is never a catalog key. A romhack patches the ROM but leaves that
code untouched, so keying on it would resolve every hack to the original release and give the two
the same title and cover. The checksum stays the only cartridge key, and a modified dump is
matched by filename through the artwork index instead.

PlayStation 3 covers are addressed by serial through GameTDB, high-resolution `coverHQ` set first
and the standard `cover` set after it: `coverHQ` is partial, and several releases only ever
received the standard image.

GameCube and Wii covers are addressed by the disc id through GameTDB — the disc id's fourth
character selects a region/language folder (`US`, `JA`, `EN`, `DE`, …), with `EN` and `US` tried
as fallbacks — before the title-addressed Libretro provider. This id-addressed route does not
depend on an exact catalog title match, so it succeeds even when the DAT lookup does not.

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

The Mega Drive / Genesis reader accepts only a `SEGA` cartridge header at the standard offset in
a bounded raw `.md`/`.gen`/`.bin` file, or a 512-byte copier header followed by complete 16 KiB
SMD interleaving blocks which normalize to that header. It hashes the normalized stream with
SHA-1 and never uses a filename as catalogue evidence. The No-Intro DAT's nested ROM `sha1` field
is matched exactly for a canonical title. Only then may the Libretro thumbnail provider request its
matching named box-art path; an absent image leaves the cover unchanged.

The Nintendo DS reader accepts only a raw `.nds` file no larger than 512 MiB with coherent ARM9
and ARM7 ranges, DS/DSi-enhanced unit code, bounded card/header declarations, the canonical
Nintendo header logo, and valid logo/header CRC-16 values. It reads a printable header title and commercial game code without
changing the source. A valid `####` homebrew header is importable for local use, but retains no
shared game code; it can only be catalogue-matched by the raw-ROM SHA-1. DSi-exclusive files,
malformed headers, archives, and headered layouts are not accepted. The verified raw bytes are the
canonical first-pass layout, so the No-Intro nested `sha1` record is the sole catalogue key.

The Game Boy Advance reader accepts only a raw `.gba` file no larger than 32 MiB whose canonical
Nintendo header logo, boot branch, fixed header byte, main-unit/reserved fields, printable header evidence, and complement check are
valid. It retains a commercial game code only as local evidence and streams the raw bytes through
SHA-1 for every catalogue match. Thus, regional revisions or altered payloads with the same code
cannot collide. Copier/headered variants and archives stay unsupported until their normalization
has deterministic fixtures.

The Super Nintendo reader accepts a raw `.sfc` or `.smc` file between 32 KiB and 8 MiB. The SNES
has no magic bytes, so recognition is structural: the internal LoROM (`0x7FC0`) or HiROM (`0xFFC0`)
header must carry a consistent checksum/complement pair (`checksum XOR complement == 0xFFFF`), an
emulation reset vector pointing into the `$8000-$FFFF` ROM window, and a plausible map-mode byte.
The header title is Shift-JIS on Japanese cartridges, so it is read best-effort for display only
and never gates recognition. An optional 512-byte copier header (present when `size % 0x2000 == 512`)
is normalized away before hashing, matching both the No-Intro sets and the rcheevos algorithm, so a
headered `.smc` and a headerless `.sfc` of the same cartridge resolve to one SHA-1. `.fig`/`.swc`
copier formats stay unsupported until their normalization has deterministic fixtures.

The expansion artwork route uses the same official Libretro thumbnail server as the existing
title-addressed fallback. A title-path lookup is intentionally never made from a filename, header
title, RPCS3 display title, or product code alone: it starts only from the canonical title produced
by an exact Redump or No-Intro catalog match. The exact title is tried first; if it is absent,
EmuShelf downloads and caches the relevant Libretro `Named_Boxarts` directory index for 14 days,
then selects an actual source filename whose normalized product title is exact. Region, language,
and revision labels are excluded from that product-title comparison. A short PSP-only list covers
verified commercial renames and the `Ac!d` typography; each alias must still be present in that
playlist's source index. Filename
compatibility lookups retain only the literal filename and never query the index. This is not a
similarity or edit-distance search: ambiguous or unrelated source names are rejected, so an
unavailable cover cannot become another game's artwork. The server and its per-console repositories are
updated periodically; a `404`, non-image response, size-limit rejection, offline failure, or
provider outage simply leaves the existing/placeholder cover in place. The bounded downloader,
portable cache, provenance record, and manual-cover compare-and-set are shared with every other
metadata profile.

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

Use this checklist for SNES, PS3, or any later system:

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

- **SNES:** CRC32 or SHA-1 of the normalized, headerless ROM stream; copier-header handling must be
  fixture-tested before support is declared.
- **PlayStation 3:** `TITLE_ID` and `TITLE` from `PARAM.SFO`. Directory and disc layouts should
  share an SFO reader, and the title id should be the catalog key; hashing large installed games is
  unnecessary for title/cover enrichment.

These examples are guidance, not implemented support. A new platform should remain visibly
unmatched until its extractor and catalog behavior have deterministic tests.
