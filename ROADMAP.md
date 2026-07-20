# Roadmap to version 1

Derived from the design doc's milestones (docs/design-document.pdf §13), split so each milestone is a self-contained work session. Check items off as they land. Definition of done for v1 is the doc's §14.

## M1 — App shell ✅ (2026-07-12)

Avalonia shell: system sidebar fed from Integrations, toolbar (grid/list toggle, search, add, settings), empty-state content area, and contextual operation feedback. Builds and runs on macOS, zero warnings.

## M2 — Portable storage ✅ (2026-07-12)

- [x] Settings service: JSON in `Settings/` beside the executable, loaded at startup.
- [x] SQLite database in `Data/library.db` (Microsoft.Data.Sqlite): schema for games, library folders, per-system emulator config. Schema versioning table from day one.
- [x] Relative-path handling: store paths relative to the app directory when on the same volume, absolute otherwise.
- [x] App startup wiring: create data directories on first run.

## M3 — Library views and import plumbing ✅ (2026-07-12)

- [x] Game grid view (virtualized) and list view, switched by the existing toggle; search filtering with debounce.
- [x] "Add games" flow: pick files or a folder, assign to system (suggest by extension, user confirms), persist to DB.
- [x] Recursive folder scanning off the UI thread with contextual progress feedback.
- [x] Startup availability check (background stat of known paths); unavailable games marked and not launchable.
- [x] Manual rescan action (per system and global).

## M4 — Format rules for file-based systems ✅ (2026-07-13)

- [x] Extension maps: PS1 (.cue/.chd/.m3u/.pbp/.iso), PS2 (.cue/.bin/.iso/.chd/.cso/.m3u), GC/Wii (.iso/.rvz/.wbfs/.gcm/.ciso).
- [x] .cue parsing: referenced .bin files never appear as separate games.
- [x] .m3u playlists: playlist is the game entry; referenced discs hidden.
- [x] GC vs Wii disambiguation by disc-header magic words (plain and within .rvz/.wbfs containers).

## M5 — PS3 importing → superseded by M13 (2026-07-12)

The original directory-scanning design was deferred and is now superseded by the
user-requested **M13** RPCS3-library sync design. Milestone numbers M6–M8 remain as-is
so existing references do not shift.

## M6 — Emulator configuration and launching

- [x] Settings UI: per-emulator executable picker and editable global launch arguments.
- [x] Argument templates with {GamePath}, {GameDirectory}, {GameFileName}, {EmulatorDirectory}; args passed as an array, never a shell string.
- [x] Launch flow: validate game + emulator, minimize frontend, start process, track exit, restore. Double-click and context menu.
- [x] Launch-failure feedback in a contextual notification.
- [ ] Verify the current file-based launch paths on Windows with DuckStation, PCSX2, and
      Dolphin. RPCS3 has its own source-import and launch acceptance gate in M13.

## M7 — Titles, covers, and editing ✅ (2026-07-13)

- [x] Manual cover assignment: copy the chosen image into `Covers/`, generate cached thumbnails in `Cache/` off the UI thread.
- [x] System-branded placeholder covers using the licensed OpenEmu platform-icon set; upstream BSD license and author credit ship with the app.
- [x] Title editing via compact popover; right-click context menu (launch, edit, set cover, remove).
- [x] Remove flow: DB-only, confirmation states files are untouched.

## M8 — Polish and packaging

- [x] Light/dark theme toggle (and follow-system default).
- [x] Cohesive OpenEmu-inspired library shell with console artwork, collection navigation, polished empty/missing-art states, and future-platform asset coverage.
- [x] Performance pass: indexed/limited recent queries, batched availability writes, bulk UI collection refreshes, ReadyToRun Windows startup, and deferred cover UI work during play.
- [x] Error handling and daily diagnostic logging to portable `Logs/` files.
- [x] Self-contained portable win-x64 zip plus SHA-256 checksum via CI.
- [ ] Test the scoped Windows acceptance checklist on a real Windows machine. PS3/RPCS3
      returns in M13 with a deliberately different, emulator-library-owned import model.

## M9 — Opt-in metadata enrichment ✅ (2026-07-13)

- [x] Keep local scanning independent from network work and ask for consent only after the first successful import.
- [x] Support one-time, automatic-after-import, per-platform, and all-library fetch actions.
- [x] Extract exact PS1/PS2 product codes and GameCube/Wii disc ids without modifying game files.
- [x] Resolve canonical titles through cached Libretro DAT catalogs and covers through ordered xlenore/Libretro providers.
- [x] Persist identifiers, match status, source provenance, and title/cover ownership so downloaded data never replaces a manual edit.
- [x] Store downloaded covers and catalog caches in the existing portable directories; bundle no game artwork.
- [x] Document the provider architecture and new-platform checklist in `docs/metadata-enrichment.md`.

## M10 — RetroAchievements display (planned; read-only)

This is the design document's §12 follow-up: EmuShelf shows availability and the
connected user's progress, while external emulators remain solely responsible for
unlocking and submitting achievements. EmuShelf may read game images to identify them,
but it never modifies game files, emulator configuration, or RetroAchievements state.

### 1. Identification feasibility gate

- [x] Put RetroAchievements game identification behind a Core interface and evaluate the
      official `rcheevos` hash implementation rather than matching by title, filename, or
      emulator-private data. Record the dependency/version/license decision before it lands.
- [x] Prove exact hash matches with fixtures for every supported imported format: PS1
      (`.cue`, `.chd`, `.m3u`, `.iso`), PS2 (`.cue`, `.bin`, `.iso`, `.chd`, `.cso`, `.m3u`), and
      GameCube/Wii (`.iso`, `.rvz`, `.wbfs`, `.gcm`, `.ciso`). Raw/CUE media use the stock disc
      reader; compressed containers use compatible logical-disc reader adapters and never silently
      fall back to whole-file MD5. `.pbp` is cancelled for RetroAchievements (see below): it stays
      importable and launchable but is never RA-matched, so it shows `Unknown`.
  - [x] Add official-vector parity fixtures and readers for PS1/PS2 cooked ISO/BIN,
        ordinary CUE/BIN (2048- and 2352-byte sectors), M3U entries resolving to those
        media, and GameCube ISO/GCM. Fixtures now execute and pass: each expected MD5 is the
        verbatim constant from rcheevos `test/rhash/test_hash_disc.c` at the pinned commit, so
        the C# hashers are byte-identical to rcheevos. Added all four edge-case parity fixtures
        too (no `SYSTEM.CNF` → PSX.EXE fallback, executable in a subdirectory via the multi-level
        ISO9660 walk, extra-slash boot path, and a PS1 disc under the PS2 console id).
  - [x] Add PlayStation compressed-container support: the hasher now opens `.chd` (zlib/LZMA,
        cdzl/cdlz) and `.cso`/`.zso` through the shared `ILogicalSectorReader` readers built for
        M11, with parity fixtures proving they hash byte-identically to the uncompressed disc
        (CSO/ZSO in-code; CHD verified against a real chdman-produced container). A malformed
        container falls back to `UnsupportedFormat`, never whole-file MD5.
  - [x] Add a read-only Nintendo logical-disc layer for GameCube/Wii CISO and WBFS plus
        Dolphin RVZ with the ordinary `none`/Zstandard codecs. GameCube ISO/GCM, CISO, WBFS,
        and a genuinely Zstandard-compressed RVZ all reproduce the official rcheevos GameCube
        vector. Wii ISO/CISO/WBFS use the upstream partition/TMD/encrypted-cluster selection;
        encrypted Wii RVZ reconstructs the encrypted partition sectors (including RVZ hash
        exceptions) before hashing, with a fixture covering that reconstruction. Malformed
        images, already-decrypted Wii RVZ images, and RVZ files using an unverified codec remain
        `UnsupportedFormat`; no format falls back to a whole-file hash.
  - [x] **Cancelled (2026-07-19).** `.pbp` RetroAchievements support is dropped: rcheevos hashes
        `.pbp` as a PSP whole-file (its CD reader cannot extract a PS1-in-PBP disc), so a PS1 `.pbp`
        hash cannot be cross-checked against rcheevos the way every other format was. Shipping an
        unverifiable reader would risk a silent no-match — the failure mode this feature forbids —
        so `.pbp` stays importable and launchable but is never RA-matched; it shows `Unknown`.
- [ ] Make the supported-format result an explicit gate: ship only formats with verified
      parity on Windows and macOS, and present all other cases as `Unknown`, never `No`.
      PlayStation 3 is out of scope because RetroAchievements has no PS3 console id.
- [ ] Cache each successful or terminal identification by game id plus a source fingerprint
      (size/modified time and descriptor dependencies for CUE/M3U). Re-identify only new or
      changed games, on a single background worker, without a full startup pass.
  - [x] Add schema-v4 identification records, dependency fingerprints, and a composed
        single-worker service that reuses unchanged terminal results. The worker is now wired
        to run on newly imported games after import, off the UI thread and independent of the
        network-metadata consent. Gating it on an RA-enabled flag is deferred to the §2 account
        slice so it does not read discs for users who never connect an account.

### 2. Account connection and read-only API client

- [x] Account connection logic: `RetroAchievementsAccountService` validates username + **Web API
      key** with `API_GetUserProfile`, saves the returned ULID, and supports disconnect/reconnect
      (credential setup, not password login; never reuses an emulator token). The Settings connect
      **card** (the UI) is deferred to the library-presentation slice, its first consumer.
- [x] Put the API key behind a platform-specific Core abstraction
      (`IRetroAchievementsCredentialStore`), never in `settings.json`, diagnostics, exception text,
      or a logged request URI (test-asserted). Windows = DPAPI-protected blob under portable
      `Settings/` via `crypt32` P/Invoke (no new package); macOS dev = session-only. Only the
      non-secret username + ULID persist to `settings.json`. Tradeoff recorded in `DECISIONS.md`.
- [x] Typed, cancellable `IRetroAchievementsClient` for the required read endpoints (profile
      validation, per-console game/hash catalogue, batched user progress). Authentication, offline,
      malformed-response, 429 (with `Retry-After`), and server failures map to distinct results so
      callers keep cached data and never auto-retry auth failures. Full game/user progress for the
      popup lands with §5. 15 unit tests (result mapping, parsing, redaction, connect/disconnect).

### 3. Portable catalogue and progress cache

- [x] Cache achievement-bearing game/hash catalogues for PlayStation (RA 12), GameCube
      (RA 16), Wii (RA 19), and PlayStation 2 (RA 21) as JSON under `Cache/RetroAchievements/`
      (`RetroAchievementsCatalogueCache`). `API_GetGameList` (achievements + hashes) is requested
      at most once per console every seven days unless refresh is forced, a stale cache is still
      served offline, and the response is exposed as a hash→game lookup.
- [x] Hash→game matching (`RetroAchievementsMatchingService` + `RetroAchievementsConsoles`):
      resolves each locally hashed game against its console catalogue and updates the schema-v4
      link (RA game id + has-achievements). A miss against a **fresh** catalogue records "no
      achievements"; a miss against a stale/absent catalogue is left unresolved so it never
      becomes a false "no". 13 unit tests (TTL, stale fallback, force refresh, match/miss states).
- [x] Add schema-v5 `RetroAchievementProgress` records for account-scoped **progress summaries**
      (awarded / total, hardcore) with last-refresh times, available offline. Store methods live
      on a separate `IRetroAchievementsProgressStore`; `RetroAchievementsProgressService` refreshes
      linked RA game ids in `MaxUserProgressBatchSize` batches, stops and reports on failure while
      keeping the cache, and can clear on disconnect. 6 unit tests (round-trip, batching, failure).
- [x] Download achievement badges on demand, off the UI thread, into a bounded
      `Cache/RetroAchievements/Badges/` cache. Deduplicate concurrent requests and render a
      local placeholder when an image is unavailable. **Completed in §5**, where badges render.

### 4. Library availability presentation

- [x] A pure `RetroAchievementsDisplay` state machine resolves link + progress + connection into
      `(ShowMark, ColumnText, Tooltip)`, so the grid mark and list column can never disagree.
      `GameViewModel` exposes the three; `MainViewModel` fills them from `IRetroAchievementsReadStore`
      (all links + progress in two queries) on the load worker. 12 state-machine + 1 store tests.
- [x] Grid tile shows a small neutral trophy mark (mdi-trophy geometry, not RA branding) only on a
      confirmed hash match. List view has an Achievements column showing cached `awarded / total`
      (including `0 / total`) or `—`, with tooltips distinguishing the reasons behind a `—`
      (no set, pending, unsupported, not connected, stale). Both verified by headless render tests.
- [x] Settings is now sectioned (General / Emulators / RetroAchievements) instead of one long list.
      The RetroAchievements section is a connect card (username + masked Web API key) driving the
      §2 account service; on connect it performs an explicit, cached backfill identification of
      the existing library, then matches hashes, refreshes progress, and reloads the library so
      marks appear. Settings shows the active phase, a real progress bar, and the game currently
      being identified/matched; later imports, including newly discovered games from remembered-
      folder rescans, join the same serialized pipeline. A connected user can explicitly refresh
      RA catalogues and rematch cached hashes, so a previous no-set result can be retried without
      reopening unchanged ROMs. Disconnect clears the account and account-scoped progress cache
      only after in-flight sync work finishes, while macOS session-only credentials show a
      reconnect-required state after restart. Tests cover the existing-library pipeline,
      post-import/rescan identification, manual retry, progress presentation, auth failures,
      disconnect/import overlap, and sections.

### 5. Steam-like achievements popup

- [x] Open a compact game achievements window from the grid mark, list row, or context menu.
      Show the game title, `unlocked / total`, progress bar, earned points, and last refresh,
      followed by the RA display-ordered achievement list with badge, title, description,
      points, earned date, and locked/unlocked state.
- [x] Count any earned achievement toward the primary progress bar and additionally mark
      hardcore unlocks, rather than forcing a softcore/hardcore mode choice. The first pass
      displays the resolved game's core data returned by the Web API; leaderboards, rich
      presence, activity feeds, and separate subset/multiset controls are deferred.
- [x] Render cached content immediately and refresh in the background only when detail data is
      older than five minutes or the user requests it. Keep the popup useful when offline.

### 6. Refresh and rate-limit policy

- [x] Use one request coordinator with one in-flight request and at least one second between
      automatic calls. Coalesce duplicate work, honor `Retry-After`, back off with jitter after
      429/5xx responses, and never retry authentication failures automatically.
- [x] When the app starts, refresh summary progress only if the last successful summary sync is
      older than 15 minutes. Query only distinct RA game ids linked to the local library, in
      bounded batches through `API_GetUserProgress`; do not poll recent achievements.
- [x] After a tracked emulator exits, wait briefly for its submission to settle, then refresh
      that launched game's full progress once and update the open popup/library summary. Do no
      achievement polling while the emulator is running. Also provide an explicit manual
      refresh action.

### 7. Verification and acceptance

- [x] Deterministic fixture tests cover every verified hash/container format (PS1/PS2 cooked
      ISO/BIN, CUE/BIN 2048/2352, M3U, CHD, CSO/ZSO; GameCube ISO/GCM/CISO/WBFS/RVZ; encrypted and
      decrypted Wii ISO/CISO/WBFS/RVZ), the four PS1 boot edge cases, and M3U/CUE dependency
      invalidation. Nintendo/PlayStation vectors are cross-checked against rcheevos' own algorithm
      (the decrypted-Wii mismatch this surfaced is fixed), and a test asserts source bytes and
      timestamps are unchanged for compressed and Nintendo images.
- [x] Client/cache tests cover valid/invalid credentials, key redaction, offline/stale-while-
      revalidate cache serving, cancellation, corrupt cache (refetch or degrade to null),
      429 `Retry-After`, server-error backoff, duplicate-request coalescing, and account switching.
- [x] Headless/view-model tests cover the `unlocked / total`-or-`—` column semantics (including
      `0 / total` and each `—` tooltip reason), cached progress, popup states, and post-session
      refresh. `dotnet build`/`dotnet test` are green on macOS; the Windows run remains part of the
      real-hardware acceptance below.
- [ ] On real Windows, connect a test RA account and verify one supported game in DuckStation,
      PCSX2, and Dolphin: EmuShelf identifies it before launch, the emulator performs the
      unlock, and EmuShelf reflects the new progress after process exit without writing to the
      game or emulator data.

## M11 — Metadata matching: speed and coverage

Follow-up to M9. The first enrichment implementation re-derives each PlayStation serial by
brute-force scanning up to 32 MiB of every disc on every pass, at a two-wide concurrency gate,
and cannot read the compressed containers most libraries actually use. External tools
(DuckStation, PCSX2) are fast and complete because they read the serial from a tiny known
location or decode the container, then fan cover downloads out widely. This milestone closes
that gap. See `DECISIONS.md` for the diagnosis.

### Phase 1 — Targeted reads, identifier caching, pipeline decoupling ✅ (2026-07-16)

- [x] Replace the 32 MiB PlayStation serial scan with a targeted `SYSTEM.CNF` read that reuses
      the existing `CdSectorReader`/ISO9660 walk from the RetroAchievements disc code; keep a
      bounded, early-exit ASCII fallback and the filename fallback.
- [x] Reuse stored identifiers instead of re-extracting on every enrichment pass
      (`IGameMetadataStore.GetIdentifiers`); only scan a disc that has none.
- [x] Split enrichment into disk-bound and network-bound stages with independent concurrency so
      cover downloads are no longer throttled behind disc reads.
- [x] Give the metadata `HttpClient` a pooled handler with a raised per-server connection limit.
- [x] Fixtures: valid 2048-sector ISO9660 targeted read with decoy-serial precision, cached-
      identifier reuse, and store round-trip. `dotnet build`/`dotnet test` green on macOS.

### Phase 2 — PBP support (`.pbp`) ✅ (2026-07-16)

- [x] Read the serial from the embedded `PARAM.SFO` `DISC_ID` key (uncompressed, targeted).
- [x] Fixtures: known `DISC_ID` (with and without separators), malformed/truncated PBP falls back
      to filename.

### Phase 3 — CSO / ZSO support (`.cso`, `.zso`) ✅ (2026-07-16)

- [x] Decode the block index (deflate for CSO, lz4 for ZSO) to expose logical sectors to the
      shared ISO9660 reader through a common `ILogicalSectorReader`; no second `SYSTEM.CNF` parser.
- [x] Hand-rolled minimal LZ4 block decoder (no new dependency); rationale in `DECISIONS.md`.
- [x] Fixtures: CSO (deflate) and ZSO (lz4) wrapping a known-serial ISO with a decoy; corrupt
      header falls back to filename; LZ4 decoder unit tests (literals, overlapping match, truncation).

### Phase 4 — CHD support (`.chd`) ✅ (2026-07-17)

- [x] Decode the CHD v5 header and the Huffman-coded hunk map (ported from MAME/libchdr with a
      crc16 self-check), then decompress only the hunks backing the read sectors: `zlib`/`lzma`
      for DVD geometry and `cdzl`/`cdlz` (with CD frame reassembly) for CD geometry. `huff`, `flac`,
      and `cdfl` hunks gracefully fall back to the filename serial.
- [x] Vendor a minimal public-domain LZMA decoder (no third-party package); credited in
      `THIRD-PARTY-NOTICES.md` alongside the MAME/libchdr-derived CHD code.
- [x] Verified byte-exact against chdman 0.288 vectors: committed tiny DVD `zlib`/`lzma` fixtures in
      CI, plus a real CD CHD (`cdlz`+`cdzl`, 20k frames) and an opt-in real-file smoke test
      (`EMUSHELF_TEST_CHD_DIR`). Reads are bounded and never modify the source.

### Phase 5 — Nintendo disc-id cover route ✅ (2026-07-17)

- [x] Add an id-addressed GameCube/Wii cover provider (`GameTdbArtworkProvider`) keyed by the
      six-character disc id, ordered before the title-based Libretro fallback. URL, region mapping
      (Dolphin-compatible, with EN/US fallbacks), and real 200/404 responses verified;
      `THIRD-PARTY-NOTICES.md` updated.
- [x] Tests: candidate URLs per region with fallback order; non-disc-id identifiers ignored;
      unavailable covers 404 and fall through to the Libretro title provider.

## M12 — Expansion launcher and library-source foundation (planned)

This is the common work required before adding the requested platforms. It keeps the
current per-system launch experience while allowing one RetroArch installation to serve
several systems and one external emulator to own a game catalogue.

- [x] Introduce a backwards-compatible launcher mapping: an emulator executable may be
      shared by several systems, while each system retains its own editable launch template
      and settings. Migrate existing per-system executable paths and arguments without
      losing portable relative paths or changing current DuckStation/PCSX2/Dolphin behavior.
- [x] Add a controlled per-system `CorePath` launch setting for RetroArch systems and one
      additional template placeholder. The Settings UI selects an already installed core
      file; it does not download, update, enumerate, configure, or switch cores per game.
      A missing core is a preflight error, not a reason to start RetroArch without content.
- [x] Register PSP, Mega Drive / Genesis, Nintendo DS, and Game Boy Advance as stable,
      separately filterable systems. Reuse licensed platform art where available; choose
      each platform's canonical cover ratio from representative licensed/sample artwork
      before finalizing the grid shelf.
- [x] Add a read-only external-library-source contract alongside the existing folder
      scanner. Source imports must retain their source provenance, run only on an explicit
      user action or rescan, reconcile without deleting user library rows, and preserve the
      no-full-library-scan-at-startup rule.
- [x] Cover migrations, missing executable/core failures, source-refresh cancellation,
      and portable relocation with a shared emulator/core must have deterministic tests on
      macOS and Windows.

## M13 — PlayStation 3 from the RPCS3 game library (planned)

This replaces the original PS3 directory scanner. EmuShelf will show only games that RPCS3
itself knows about; it will never recursively discover arbitrary PS3 directories or offer
an individual PS3-folder import.

- [x] Add an explicit **Sync RPCS3 library** action. The user selects the RPCS3 data/config
      location (no auto-detection); the integration reads only RPCS3's own game-list data
      through a versioned, read-only adapter. A changed or unsupported upstream format must
      fail with an actionable message and import nothing rather than guess.
- [x] Import the RPCS3-recorded path, title, title id, and availability as one PS3 entry,
      with source provenance. The recorded list is authoritative for discovery; a targeted
      `PARAM.SFO` read may validate/enrich an already listed entry but must never turn an
      unlisted directory into a library game. Entries absent from a later sync remain in
      EmuShelf and are visibly unavailable/source-missing until the user removes them.
- [x] Treat RPCS3-supplied title data as embedded metadata that can replace a filename only;
      manual title/cover edits always win. The title id is the exact evidence for the later
      PS3 cover route, not a title-similarity key.
- [ ] Verify the current RPCS3 launch contract on real Windows with an imported installed
      game and a listed disc/directory game: quoted paths, minimize/restore, non-zero exit,
      source refresh while the app is open, and no writes to RPCS3 data or game files.
- [x] Keep PS3 out of RetroAchievements matching and display it as unsupported: there is no
      RetroAchievements PlayStation 3 console mapping in the existing design.

## M14 — PSP and PPSSPP (planned)

- [x] Add a PSP file-import profile after a format feasibility pass against PPSSPP 1.20.4.
      The verified initial set is standalone `.iso` and `.cso` images containing a parseable
      `PSP_GAME/PARAM.SFO`; archive, CHD, PBP, and other compressed variants wait for an exact
      content/identity design rather than treating a container as an opaque game file.
- [x] Read small PSP `PARAM.SFO` evidence (`DISC_ID`, title where trustworthy) from every
      accepted image without modifying it. Store a valid exact disc id for later metadata lookup,
      retain distinct regions/revisions as distinct paths, and fall back to the filename only for
      display when the title evidence is invalid or unavailable.
- [x] Activate PPSSPP executable selection and its argv-safe default game-path launch template.
      PPSSPP remains responsible for emulator settings and actual achievement unlocking.
- [x] Add file-recognition, SFO/container, availability, and launch fixtures; every reader proves
      source bytes and timestamps remain unchanged.
- [ ] On real Windows with PPSSPP 1.20.4, verify ISO and CSO import/metadata, a path containing
      spaces, missing-executable preflight, minimize/restore after tracked zero and non-zero exits,
      and no writes to the game image or PPSSPP settings.

## M15 — RetroArch as a core-aware launcher (planned)

RetroArch is a shortcut launcher with one necessary per-system choice: the default installed
Libretro core. It is not a core manager and EmuShelf will not edit RetroArch configuration,
core options, overrides, playlists, or achievements settings.

- [x] Configure one shared RetroArch executable plus one per-system default core selected from
      a dropdown of installed core binaries beside that executable. Keep the choice at system
      scope, not a prompt at every launch and not a per-game setting.
- [x] Launch content through the explicit core-and-content argv form (`-L {CorePath}` plus
      `{GamePath}`), so several compatible installed cores cannot produce an ambiguous or
      different launch. Continue to use argument arrays, never a shell string.
- [x] Show installed cores in a dropdown, retain the configured core's file name, and allow
      clearing the selection in Settings;
      reject a missing core, unsupported selected content, or malformed template before
      minimizing EmuShelf. Scan only the adjacent `cores/` directory; never download or alter
      RetroArch cores, configuration, overrides, playlists, or achievements settings.
- [x] Prove a shared portable RetroArch installation can move with EmuShelf and the library;
      verify each platform's core is invoked, saved RetroArch overrides remain untouched, and
      EmuShelf restores after the process exits.

## M16 — Mega Drive / Genesis library (planned)

Mega Drive and Genesis are one system, not duplicate regional sidebars: the label recognizes
both names while the library keeps regions and revisions as separate game entries.

- [x] Add strict folder and explicit-file recognition for header-proven raw `.md`, `.gen`, and
      `.bin` ROMs plus canonical copier-header/interleaved `.smd` ROMs. Normalization has
      raw/SMD parity fixtures; archives, raw `.smd` files, oversized inputs, and unsupported
      dumps are excluded from automatic discovery.
- [x] Extract a bounded normalized-ROM SHA-1 for exact catalogue and future achievement matching,
      including copier-header and interleaving fixtures. A filename is presentation fallback,
      never automatic metadata evidence.
- [x] Exercise the M15 RetroArch core mapping with Mega Drive portable-path coverage, and add
      scan/rescan, availability, and cover-placeholder-ratio tests.
- [ ] On real Windows, launch an accepted raw ROM and an accepted `.smd` through a configured
      RetroArch core; verify paths with spaces, minimize/restore, and that neither ROM nor
      RetroArch configuration/overrides/playlists/achievement settings are modified.

## M17 — Nintendo DS library (planned)

- [x] Add strict `.nds`-format discovery and explicit-file import, with no archive support in
      the first pass. Read the ROM header game code and title as local evidence without
      modifying the ROM.
- [x] Use a normalized ROM checksum as a required exact-match fallback where revisions share
      a DS game code; malformed headers, homebrew, and unsupported containers remain visibly
      unmatched rather than being title-guessed.
- [x] Exercise the M15 RetroArch core mapping with Nintendo DS portable-path coverage, and add
      header/checksum, availability, and cover-placeholder-ratio tests.
- [ ] On real Windows, launch an accepted `.nds` ROM through a configured RetroArch core; verify
      paths with spaces, minimize/restore, and that neither ROM nor RetroArch configuration,
      overrides, playlists, or achievement settings are modified.

## M18 — Game Boy Advance library (planned)

- [x] Add strict `.gba`-format discovery and explicit-file import, deferring archives and
      headered/converted variants until their normalization has test vectors.
- [x] Read bounded header evidence for display but use a normalized ROM checksum for exact
      catalogue/achievement matching, so regional revisions and altered dumps cannot collide.
- [x] Exercise the M15 RetroArch core mapping with Game Boy Advance portable-path coverage, and
      add checksum, availability, and cover-placeholder-ratio tests.
- [ ] On real Windows, launch an accepted `.gba` ROM through a configured RetroArch core; verify
      paths with spaces, minimize/restore, and that neither ROM nor RetroArch configuration,
      overrides, playlists, or achievement settings are modified.

## M19 — Exact covers and RetroAchievements for the expansion systems (planned)

This extends the opt-in M9 metadata pipeline and the read-only M10 achievement pipeline;
it must not create a second downloader, account flow, or background polling mechanism.

- [x] For PS3, PSP, Mega Drive / Genesis, DS, and GBA, select a catalogue and artwork provider
      only after validating its identifier semantics, availability, licensing/terms, update
      behavior, image limits, and a real 200/404 fallback. Prefer id/checksum-addressed art;
      use a title-addressed fallback only after an exact catalogue match. Record sources and
      redistribution status in `THIRD-PARTY-NOTICES.md` before shipping.
- [x] Extend the existing consent, caching, bounded downloader, thumbnail staging, provenance,
      and user-ownership rules. Downloaded covers remain under portable `Covers/` and may fill
      only an empty/non-user cover; a manual edit made during a fetch wins. No game art or
      catalogue is bundled by default.
- [x] Add PSP, Mega Drive / Genesis, DS, and GBA to RetroAchievements only after each accepted
      format has byte-for-byte parity fixtures against the pinned `rcheevos` behavior and its
      console catalogue semantics have been verified. Unsupported containers, archives, hacks,
      and uncertain matches stay `Unknown`, never `No achievements`.
- [x] Reuse the current account credential handling, cached catalogue/progress policy, rate
      limiter, library mark, and achievement popup. PPSSPP and RetroArch/core settings alone
      perform unlock/submission; EmuShelf identifies and displays cached progress after launch.
      PS3 remains excluded from this milestone.
- [x] Cover offline/stale-cache, 429, provider failure, manual-edit race, account switch, and
      post-session-refresh outcomes through the reused metadata and RetroAchievements services;
      expansion-system mapping and reader fixtures prove every new supported system uses them.
- [ ] On Windows, validate one real supported game per applicable emulator/core without writing to
      game or emulator data.

## M20 — Expansion release acceptance (planned)

- [ ] Extend `docs/windows-test-checklist.md` with the M13 RPCS3-library-sync path, PPSSPP,
      and the three RetroArch systems. Record the exact emulator/core versions and the supported
      import-format matrix used for each run.
- [ ] Test first launch, empty/error states, source/cancelled rescan, unavailable records,
      paths containing spaces, external/portable-drive relocation, cover fetch/placeholder,
      core and emulator launch preflight, process restoration, and manual metadata ownership.
- [ ] On a real Windows machine, verify RetroAchievements end to end for one validated PSP and
      one validated RetroArch-platform game: EmuShelf shows only read-only progress after the
      emulator/core unlocks and submits it. Verify PS3 stays explicitly unsupported.

## M21 — Miscellaneous backlog (planned)

- [ ] Add PSP CHD feasibility and import support. CHD already serves the existing PlayStation
      profile, but it can also contain a PSP UMD image; support it only after the logical-disc
      reader can locate and validate `PSP_GAME/PARAM.SFO`, preserve exact `DISC_ID` evidence,
      launch it with a verified PPSSPP release, and prove read-only source bytes/timestamps with
      ISO/CSO parity and malformed-container fixtures. Do not treat a CHD as an opaque PSP file.
- [ ] Run the opt-in `chdman` CD-decode test on Windows CI/dev. `ChdSectorSourceTests`
      `CompressedCd_CookedFrameBytes_AreNotOffsetAsRawHeaders_WhenChdmanAvailable` now skips
      cleanly when `chdman` is absent (fixed 2026-07-19); provision a pinned `chdman` (from MAME
      tools) so the CD framing path is actually exercised on Windows rather than only skipped.

## M22 — Drag-and-drop library import (planned)

- [ ] Accept dropped individual files and folders in the library view using the same read-only,
      system-aware import path as Add Game and Add Folder. Support multi-item drops, recurse only
      through explicitly dropped folders, and ignore unsupported files without changing them.
- [ ] When a dropped file is compatible with multiple systems, preserve the existing explicit
      system-selection flow instead of guessing from its extension. Surface accepted, skipped, and
      failed items clearly; a cancelled selection must leave no partial library changes.
- [ ] Exercise paths containing spaces, mixed valid/invalid drops, duplicate entries, unavailable
      external-drive content, portable relocation, metadata consent, and cover-placeholder behavior
      with focused view-model/import fixtures. Keep drag-and-drop wiring out of code-behind business
      logic and never block the UI while scanning a folder.

## M23 — Achievement view filtering and sorting (planned)

- [ ] Add composable achievement filters for unlocked and locked state, including an explicit
      unknown/unavailable state so games that have not been matched are never presented as locked.
- [ ] Add accessible, consistent color coding for unlocked, locked, and unknown/unavailable
      achievements, with text and icon/state cues that preserve the distinction in high-contrast,
      dark, and light appearances rather than relying on color alone.
- [ ] Add deterministic sorting by unlock date, unlock percentage, and the existing display order.
      Define null/zero-total behavior and stable tie-breakers so cached, offline, and partial
      achievement data remain predictable.
- [ ] Keep filtering and sorting in the achievement view model over cached read-only data; do not
      create background polling or mutate emulator, game, or RetroAchievements account data. Add
      headless tests for combined filters, sort directions, state transitions, and empty results.

## M24 — UI polish and product-quality pass (planned)

- [ ] Establish a cohesive visual system for typography, spacing, color, elevation, controls,
      platform shelves, and cover treatment. Apply it consistently to the library, game details,
      Settings, metadata, and RetroAchievements views without importing OpenEmu branding or
      unlicensed artwork.
- [ ] Refine high-frequency flows so the first launch, empty library, scanning/import progress,
      selection, search, filtering, unavailable games, metadata failures, and launch/preflight
      errors feel deliberate and understandable rather than like developer states.
- [ ] Improve layout responsiveness and accessibility: keyboard navigation and visible focus,
      screen-reader labels, readable contrast in light/dark/follow-system modes, sensible scaling,
      and polished grid/list virtualization at small and large window sizes.
- [ ] Add focused UI/view-model coverage and visual regression/manual acceptance checks for every
      refined state. Keep business behavior in view models and services; code-behind remains view
      wiring only.

## M25 — Multi-select and bulk library actions ✅ (2026-07-20)

Surfaced during the Windows GUI pass (2026-07-19): the library is single-select only and
Remove works one game at a time, so clearing or pruning a library is tedious.

- [x] Add multi-selection to both the cover grid and the list view: Ctrl/Cmd-click to toggle,
      Shift-click to range-select, and Ctrl/Cmd+A to select every game in the current collection.
      Keep the selection model in the view model over the existing `IsSelected`/`SelectedGame`
      state; code-behind stays gesture wiring only.
- [x] Add a bulk "Remove selected" action (context menu + Delete key) with a single confirmation
      that states the count. Removal touches only EmuShelf's database rows — never the game files
      or covers — and leaves the selection empty and the view refreshed afterward.
- [x] Add headless view-model tests for toggle/range/select-all across grid and list, selection
      surviving (or clearing on) collection reloads, and bulk remove of a mixed available/missing
      selection. Keep `dotnet build`/`dotnet test` green on macOS and Windows.
