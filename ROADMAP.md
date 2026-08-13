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
      (CHD joined that set on 2026-07-26 under the same PARAM.SFO rule — see M21.)
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

- [x] Add PSP CHD feasibility and import support. CHD already serves the existing PlayStation
      profile, but it can also contain a PSP UMD image; support it only after the logical-disc
      reader can locate and validate `PSP_GAME/PARAM.SFO`, preserve exact `DISC_ID` evidence,
      launch it with a verified PPSSPP release, and prove read-only source bytes/timestamps with
      ISO/CSO parity and malformed-container fixtures. Do not treat a CHD as an opaque PSP file.
      (2026-07-26) `.chd` joined the PSP extension map, and `PspGameMetadataReader` and
      `PspDiscHasher` dispatch it to the existing DVD-geometry `ChdSectorSource`. PARAM.SFO
      validation, `DISC_ID` evidence, read-only bytes/timestamps, and the pinned RetroAchievements
      hash are all asserted at ISO/CSO parity, with malformed-descriptor and malformed-container
      cases covered. Tests build CHDs via `ChdImageBuilder`, so no chdman install is required.
  - Verified against a real library (4 real DVD-geometry `lzma` PSP CHDs, 886 MB–1.58 GB, plus one
    `chdman createraw -us 2048` conversion of a real PSP ISO). Each imported as PSP with its real
    embedded title and `DISC_ID`, hashed for RetroAchievements, and left byte-for-byte and
    timestamp untouched. The converted CHD and its source ISO produced the identical RA hash
    (`4c04d31a…`), confirming the deliberate non-bump of the PSP algorithm version. 20 real PS1/PS2
    CHDs were re-checked for regressions and still suggest PlayStation, never PSP.
  - PPSSPP 1.20.4 launch confirmed: each CHD booted to a window titled with the matching disc id
    and title (e.g. `ULUS10100 : Def Jam® Fight For NY™`). Sustained gameplay was not observed —
    PPSSPP exits after a few seconds when started from an automation session, but a real PSP `.iso`
    does the same, so the instability is the harness, not the container. One manual play session
    launched from the EmuShelf UI is still worth doing.
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

M24 is a product-hardening gate, not a visual reskin. Complete its phases in order before adding
new end-user features or marking a new Windows/SteamOS release as a candidate. Preserve the
portable, read-only game-file contract throughout; polish work must not modify game files,
emulator configuration, or emulator-owned data.

### Phase 0 — Baseline and quality bar

- [ ] Capture the current Desktop and 1280×800 Gamepad experience for populated, empty, loading,
      unavailable, search-empty, error, Settings, and achievement states. Turn the approved
      references into intentional visual-regression baselines rather than treating a render-only
      test as proof of quality.
- [ ] Define a small visual system for type scale, spacing, control heights, radii, elevations,
      semantic color states, focus rings, cover treatment, and motion. Apply it consistently to
      the library, overlays, dialogs, Settings, metadata, and RetroAchievements without importing
      OpenEmu branding or unlicensed artwork.
- [ ] Establish the review matrix: Windows at 100%, 125%, 150%, and 200% scaling; 900×560 minimum
      desktop window; 1280×800 Gamepad/Deck viewport; light, dark, and follow-system appearances;
      mouse, keyboard, and controller paths.
- [x] Integrate the Desktop shell into theme-owned window chrome while retaining drag,
      double-click maximize, caption actions, edge/corner resizing, taskbar identity, and a
      caption layout that remains inside the live window bounds at high DPI. Separate brand/
      selection, achievement, information/progress, success, warning, and danger colors, and
      normalize system and collection artwork inside one sidebar icon frame (2026-07-29).

### Phase 1 — Make the game-session loop reliable

- [x] Guard the existing Gamepad return path against input used to close an emulator: native
      polling is suspended for the tracked session and resets on return, while late Steam-Input
      actions are consumed during a short return guard. Focused tests cover B/Escape-held return,
      fullscreen restoration, and controller-state reset (2026-07-25).
- [ ] Treat launch through return as one explicit frontend session. In Gamepad mode suspend
      controller/Steam-Input routing while an external emulator owns the session, restore the
      fullscreen window and the prior focused tile on exit, and ignore held/late input until the
      return is stable. A game exit must never silently switch the user to Desktop mode.
- [x] Make leaving Gamepad mode deliberate and discoverable. B/Escape dismisses the current
      overlay or returns to the Gamepad library; switching to Desktop mode uses a separately named
      action and, when initiated by a controller, a confirmation that cannot be triggered by the
      button used to close an emulator (2026-07-25; implemented by M31 Menu and confirmation).
- [ ] Add end-to-end tests for normal, failed, and non-zero launches; emulator exit with B/Escape
      held; focus restoration; and Gamepad fullscreen restoration. Verify the same behavior on
      real Windows and Deck/Gaming Mode hardware.

### Phase 2 — Validate and refine the existing controller-first Gamepad mode

- [ ] Preserve the implemented fullscreen Gamepad shell: upper platform rail, SDL2 and Steam Input
      routing, focused-cover navigation, controller-owned actions/search/collections/rename/remove/
      achievement overlays, and the controller-safe cover handoff. This phase is not a redesign or
      replacement of that work.
- [ ] Use the Phase 0 references and real controller sessions to make only evidence-backed
      refinements to hierarchy, target size, contextual help, modal grouping, rail overflow/reveal,
      and controller-safe Settings access. Keep the existing action set and focus model unless an
      acceptance finding shows a concrete usability failure.
- [ ] Verify every existing Gamepad state—empty, unavailable, no-emulator, launch failure,
      scan progress, search, rename, cover handoff, collections, and achievements—has a predictable
      focus entry, selection, cancel, and return target with no desktop-only fallback except the
      explicit platform file picker.
- [ ] Validate the adopted shared-shelf cover model at Deck resolution with real artwork of mixed
      aspect ratios. Preserve each artwork's aspect ratio while confirming stable row geometry,
      title baselines, focus treatment, and navigation.

### Phase 3 — Desktop library and high-frequency flows

- [ ] Improve first-run guidance into a clear sequence: add games, configure the required
      emulator, then launch. Make the empty-library actions, PS3/RPCS3 source path, and unavailable
      reasons actionable without exposing implementation terminology.
- [x] Add the first user-requested **multi-disc title-set flow** without treating every CD as a separate
      library game. A title set has one stable library identity and one representative card, while
       its ordered disc members retain their own source paths and availability. Persist the user-
       selected disc as that set's default launch target; neither grouping nor disc
      selection may modify any game file, playlist, emulator configuration, or emulator-owned data
      (2026-07-25; explicit independently imported discs only).
  - [x] Build conservative discovery for independently imported discs: group only same-system,
        complete sources with an explicit, recognized disc-number marker and a shared normalized
        release title. Exclude regional variants, revisions, demos, bonus discs, loose CUE tracks,
        and ambiguous filenames. Retain the current `.m3u` behavior as one canonical library entry
        (2026-07-25).
  - [ ] Extend the title-set model to enumerate `.m3u`-declared discs where possible, and never
        claim that a selected disc can be launched through an emulator until that emulator's command
        behavior is verified.
  - [ ] Provide a safe correction path for bad filenames: users can split an automatic set or merge
        compatible library entries, with a reviewable ordered-disc list. Preserve manual title,
        cover, metadata, achievement, and removal semantics; these continue to affect EmuShelf's
        records only, never the source media.
  - [x] Present one card in Grid and one row in List, showing a quiet disc count and a clear
        `Disc N selected` state when the remembered default differs from Disc 1. Opening the title
        uses the Gamepad title menu rather than expanding and reflowing the virtualized shelf
        (2026-07-25).
  - [x] In Gamepad mode, `A` launches the remembered disc. `Y` opens the existing title menu and
        exposes `Select disc` for multi-disc sets; the picker marks the current default, has a
        deterministic focus/cancel return, and selecting a disc changes the next default without
        launching it. `A` remains the explicit launch action. Single-disc behavior is unchanged
        (2026-07-25).
  - [x] Give Desktop the same remembered-disc and picker behavior through the title details/context
        menu; keyboard, mouse, and controller routes must resolve the same selected source and
        produce the same notification/recovery states (2026-07-25; Grid and List context menus).
  - [ ] Add migration, importer, view-model, launcher, and visual-snapshot coverage for ordering,
        false-positive prevention, `.m3u` descriptors, missing individual discs, manual merge/split,
        remembered-default persistence, failed launch non-mutation, Grid/List counts, Gamepad `Y`
        focus flow, and platform-specific launch templates. Complete real-emulator acceptance with
        representative PS1 and PS2 multi-disc titles before calling the flow release-ready.
- [ ] Refine scanning/import, metadata fetch, cover download, availability refresh, and launch
      states. Every long-running operation needs a visible owner, useful progress, cancellation
      behavior where supported, completion feedback, and an error state that states what the user
      can do next.
- [x] Establish a consistent selection model across grid, list, context menu, keyboard shortcuts,
      and bulk actions. Make primary, secondary, destructive, and disabled controls visually
      distinct; retain the guarantee that removal affects only EmuShelf records (2026-07-29;
      pointer-tunnel selection, additive Ctrl/Cmd+Shift ranges, right-click targeting, Escape/empty-
      canvas clearing, and one count-aware removal action shared by the contextual bar and menus).
- [ ] Replace the generic status toast treatment with semantic, accessible notices for progress,
      success, warning, and blocking failure. Messages must remain readable, dismissible when
      appropriate, and never hide the only recovery action.

### Phase 4 — Responsive, accessible, platform-aware configuration

- [ ] Validate library virtualization and cover loading at the small window, large-library, and
      high-DPI matrix from Phase 0. Eliminate clipped controls, orphaned whitespace, unstable cover
      rows, focus that scrolls off-screen, and layout shifts after cover images load.
- [ ] Complete keyboard navigation, visible focus, screen-reader names for icon-only controls,
      logical tab order, color-independent unavailable/error/achievement states, and contrast
      checks in each supported theme. Do not rely on hover-only affordances.
- [x] Make emulator settings platform-aware. Windows presents direct executable configuration only;
      Linux/SteamOS presents direct/AppImage and Flatpak targets with installed-candidate guidance,
      target-specific validation, and clear unavailable-target messaging. Existing configurations
      remain readable and never launch through an unsupported target (2026-07-25).
- [ ] Simplify Settings with progressive disclosure: show a human-readable launch summary by
      default; keep templates, executable/core paths, Flatpak IDs, and other advanced fields in an
      explicit advanced area. Make shared RetroArch installations and per-system cores legible.

### Phase 5 — Verification and release exit gate

- [ ] Add focused view-model and integration coverage for every refined lifecycle, command route,
      focus transition, platform-specific configuration, and recovery state. Keep business behavior
      in view models/services; code-behind remains view wiring only.
- [ ] Add reviewed visual snapshots for all Phase 0 states, including Gamepad rail/actions and
      Settings emulator-target variants. Snapshot changes require an intentional approval note;
      screenshots alone do not replace interaction tests.
- [ ] Run the real Windows acceptance checklist using the portable release artifact: first launch,
      import, configured direct emulator launch/return, failure paths, restart, and portable-folder
      relocation. Complete the Deck/Gaming Mode checks for controller input, Steam + X text entry,
      gamescope restore, AppImage fallback, and Flatpak path errors.
- [ ] M24 exits only when the automated suite is green, the baseline screenshots are approved, the
      real-device checks above pass, and no P0/P1 usability or launch-return issue remains open.

## M25 — Multi-select and bulk library actions ✅ (2026-07-20)

Surfaced during the Windows GUI pass (2026-07-19): the library is single-select only and
Remove works one game at a time, so clearing or pruning a library is tedious.

- [x] Add multi-selection to both the cover grid and the list view: Ctrl/Cmd-click to toggle,
      Shift-click to range-select, Ctrl/Cmd+Shift-click to add a range, and Ctrl/Cmd+A to select
      every game in the current collection. Right-click first targets an unselected game; clicking
      the empty canvas or pressing Escape clears selection.
      Keep the selection model in the view model over the existing `IsSelected`/`SelectedGame`
      state; code-behind stays gesture wiring only.
- [x] Add one count-aware removal action to each context menu, a persistent contextual selection
      bar, and the Delete key, with a single confirmation that names one game or states the selected
      count. Removal touches only EmuShelf's database rows — never the game files or covers — and
      leaves the selection empty and the view refreshed afterward.
- [x] Add headless pointer and view-model tests for click/toggle/replace-range/add-range/select-all,
      Grid/List switching, right-click targeting, Escape/empty-canvas clearing, search clearing, the
      contextual bar/menu copy, and bulk remove of a mixed available/missing selection. Keep
      `dotnet build`/`dotnet test` green on macOS and Windows.
- [x] Add a mouse-only **rubber-band (marquee) selection** for both layouts: a left-drag from the
      empty canvas paints a box that selects every cover it touches, dragging to the top/bottom edge
      auto-scrolls to extend the selection, Ctrl/Cmd makes it additive to the pre-drag selection, and
      a plain click still clears. Geometry/auto-scroll live in code-behind; the selection state stays
      in the view model (`Begin/Update/EndMarqueeSelection`). Covered by view-model, velocity-math,
      and headless drag + auto-scroll tests. See DECISIONS 2026-08-06.

## M26 — Super Nintendo library (planned)

Surfaced 2026-07-19: SNES was missed when the M16–M18 cartridge platforms were added. It reuses the
same RetroArch launcher, opt-in metadata pipeline, and read-only RetroAchievements identification.

- [x] Add strict `.sfc`/`.smc` recognition. The SNES has no magic bytes, so `SuperNintendoRomReader`
      validates the internal LoROM/HiROM header (checksum/complement consistency, reset vector in
      the ROM window, plausible map-mode) and normalizes the optional 512-byte copier header away.
      The Shift-JIS-capable header title is display-only and never gates recognition; `.fig`/`.swc`
      copier formats and archives are excluded until their normalization has fixtures.
- [x] Use the headerless-ROM SHA-1 as the sole exact catalogue key (No-Intro DAT); the header has no
      reliable game code, so no title-id evidence is produced and a filename is presentation fallback.
- [x] Register `snes` as a stable, separately filterable system on the RetroArch core mapping, using
      the already-bundled licensed OpenEmu `snes` icon and a landscape `1.434` cover frame measured
      from representative Libretro box art.
- [x] Add Super Nintendo to RetroAchievements (console id 3) behind `SuperNintendoRomHasher`, which
      strips the copier header then MD5s the rest (`rcheevos-2ac45d3-snes-v1`) — distinct from the
      whole-file cartridge hash. Add reader/hasher/extractor fixtures (LoROM/HiROM, `.smc` copier
      normalization, checksum/reset/size rejection, copier-strip MD5 parity) proving source bytes and
      timestamps stay unchanged.
- [ ] On real Windows, launch an accepted `.sfc` and a `.smc` through a configured RetroArch core;
      verify paths with spaces, minimize/restore, and that neither ROM nor RetroArch configuration,
      overrides, playlists, or achievement settings are modified.

## M27 — SteamOS launcher targets and AppImage

- [x] Store direct executable/AppImage and Flatpak application targets only on shared emulator
      installations; schema v11 migrates legacy executable configurations to direct targets.
- [x] Launch direct targets and Flatpaks through shell-free argv, installed-target preflight,
      descriptor dependency resolution, and ephemeral read-only filesystem grants. Linux Flatpak
      RetroArch discovers already-installed cores from its host-visible per-app directory;
      direct/AppImage RetroArch keeps its existing adjacent and user-config discovery paths.
- [x] Add a self-contained linux-x64 AppImage build path with ICU, desktop metadata, checksum,
      extraction-run validation, and portable data rooted beside `$APPIMAGE`.
- [ ] On Linux/SteamOS hardware, verify every supported standalone Flatpak candidate, direct
      AppImage emulator launch, permission denial/warning states, and no emulator/game mutation.

## M28 — Steam Input Gamepad mode

- [x] Persist Desktop/Gamepad interface mode; `--gamepad-ui` forces a one-run fullscreen Gamepad
      layout with upper platform rail, LB/RB boundaries, controller focus, and no list view.
- [x] Add Steam Input keyboard command wiring, controller-safe game actions, game-session window
      restoration, and post-exit RetroAchievements refresh preservation.
- [x] Capture 1280×800 visual snapshots and implement controller-owned modal workflows for actions,
      achievements, search, collections, rename, remove, and the reviewed cover handoff.
- [ ] Perform Deck Gaming Mode acceptance: rail reveal,
      Steam + X search keyboard, gamescope restore, AppImage fallback, and Flatpak path errors.

## M29 — Cloud save sync (planned)

EmuShelf launches external emulators and owns no saves; this adds an opt-in, portable cloud
sync for the games' own **battery / memory-card saves**. Save states (build/arch-fragile) and
emulator configs (machine-specific input/backend/paths) are deliberately out of scope for now.
Transport is a user-owned rclone remote; all conflict handling is non-destructive and manifest-
driven, never a raw creation/modified-date comparison. Proven end-to-end on PCSX2 first, then
generalized.

### Phase 1 — Foundation + PCSX2 end-to-end

- [x] Core seams: `ICloudSyncTransport` (rclone-backed), `ISaveLocationProvider` (per emulator),
      a `SaveSyncService` orchestrator, and a `SaveSyncManifest`. No provider or emulator
      specifics leak into the orchestrator.
- [x] Portable rclone beside the app; rclone config, sync manifests, and conflict backups under
      a new portable `Saves/`. Shell-free argv, off the UI thread, cancellable. EmuShelf stores
      only the remote name + cloud folder — never the OAuth token, and never in `settings.json`,
      logs, or exception text (rclone owns the token).
- [x] In-app **Connect Google Drive**: drive rclone's OAuth from Settings (no terminal), create a
      dedicated remote + cloud folder, and show connected/disconnected state.
- [x] PCSX2 save-location provider, read-only. Discover the *actual* memcard directory and enabled
      cards by reading PCSX2's own config (`PCSX2.ini`) through a versioned adapter — users and
      EmuDeck relocate it — rather than assuming a default path; fall back to the documented
      default (`Documents/PCSX2/memcards`, Deck Flatpak
      `~/.var/app/net.pcsx2.PCSX2/config/PCSX2/memcards`). Never write PCSX2 config.
- [x] Model both PCSX2 card types as sync units: a **file card** (`Mcd00N.ps2`, monolithic) syncs
      as one whole-card unit; a **folder card** (`Mcd00N/` with `_pcsx2_index` + per-serial
      subfolders under "Automatically manage saves based on running game") syncs as one unit per
      game serial. Folder cards are the safer per-game case and are recommended in docs — but
      EmuShelf never enables them for the user.
- [x] Sync engine: a per-unit manifest (content hash + mtime + last-synced revision) classifies
      each unit as unchanged / local-changed / remote-changed / both-changed, so a machine with a
      slow or skewed clock never loses a save to a naive "newer date wins." mtime is only a
      tie-breaker inside a genuine both-sides conflict. Pull before launch, push on emulator exit
      (reusing the tracked-exit hook). rclone is used only for list/copy/read — never `sync --delete`.
- [x] Conflict handling: both-sides-changed keeps the newer copy active, backs the loser up to
      `Saves/conflicts/` with a timestamp, and surfaces a non-blocking notice. Back up local
      before any overwrite; never delete the only copy of anything.
- [ ] Manual controls: the auto-detected save folder is shown and user-editable (confirm or point
      at a custom location); explicit **Sync now**, **Upload local → cloud (overwrite)**, and
      **Download cloud → local (overwrite)** actions; and per-conflict keep-local / keep-cloud /
      keep-both. Automatic is the default, manual is always available.
- [x] Tests: Win/Flatpak path + `PCSX2.ini` resolution, file-card vs folder-card unit chunking,
      every manifest state transition, clock-skew tie-breaking, conflict backup, forced
      upload/download, offline/failure-leaves-saves-intact, and cancellation — with rclone faked
      behind the transport interface. Green on macOS + Windows. Never modifies game files or
      emulator config.
- [x] Harden the original v1 rclone failure classification without changing its remote format:
      failed or empty
      index reads cannot become an empty cloud, operational download failures cannot prune an
      indexed save, verification requires its known `index.json`, and caller cancellation kills
      and awaits the transfer process; reject null/duplicate indexes and make explicitly staged
      outbound copies unconditional (2026-07-28).
- [ ] Revisit v1 global-catalog damage handling without changing its clean remote layout. The
      attempted fail-closed guard and later v2 replacement were rolled back; matching local/cloud
      content still repairs a stale interrupted-commit baseline (2026-07-29).
- [ ] Design any future concurrency fix inside one clean internal folder. The folder-per-commit v2
      implementation was reverted because it polluted the user's Drive root and accumulated one
      visible directory per changed save (2026-07-29).

### Phase 2 — Generalize to the other emulators

- [x] **Generalization safety gate.** Replace the PCSX2-only filesystem endpoint with a generic,
      provider-resolved endpoint. A provider must explicitly resolve every local or remote unit to
      an allow-listed file or folder; an unknown unit, inactive card/profile, traversal,
      symlink escape, or layout mismatch fails closed. Preserve the existing `pcsx2/...` unit ids,
      remote payloads, and manifests so the proven pilot needs no cloud migration. Completed with
      provider-resolved file/folder boundaries (2026-07-29).
- [x] Keep the generic endpoint limited to ordinary file and folder units. The attempted sibling
      file-set abstraction for Dolphin GCI saves was removed after review: every GCI is already a
      self-contained file, and selective shared-directory replacement added disproportionate
      rollback risk. Single-file games retain their original cloud ids; uncommon multi-file games
      use the GCI header's internal save identity to distinguish their files (2026-07-29).
- [x] Generalize orchestration to register multiple `(provider, local endpoint)` pairs, load the
      cloud index + local manifest once, reconcile enabled systems, flush rclone once, and save the
      manifest atomically. Forced upload/download is scoped to one system, never an implicit
      all-platform overwrite. `SaveProviderRegistry` owns all platform knowledge, so the coordinator,
      settings view model, and settings view name no emulator; `CanSyncSystem` answers by calling the
      same provider factory the pipeline uses, so participation and construction cannot disagree
      (2026-07-26). Reporting one provider's unsupported configuration without failing the others
      remains open: a provider exception still fails the whole run.
- [x] Replace the single `Pcsx2ConfigDirectory` setting with backward-compatible per-system save
      locations: overridden data directory, last-success time, and latest error, migrated from the
      legacy fields on load and mirrored back for rollback. A configured emulator or explicit
      override participates automatically; there is no provider-specific activation state. Settings
      renders one icon-led row per registered platform from a single template, with its detected
      path, save shape, per-platform Replace cloud / Replace local, and its own last result
      (2026-07-26). Optional local profile binding waits for RPCS3, its first consumer.
- [x] Wire the launch lifecycle promised by Phase 1: reconcile only the selected game's system
      after launch preflight succeeds but before the process starts, and again after a tracked
      emulator exit. A failed pre-launch pass warns and permits launch using the save state then on
      disk without claiming the multi-unit pass was atomic; conflicts are surfaced and remain
      recoverable. Manual sync is disabled during an EmuShelf-tracked session, and users are told
      to close externally launched emulator instances first.
- [x] **PPSSPP first:** resolve its Memory Stick from Windows `installed.txt`, portable layout,
      Linux/macOS config layout, Flatpak defaults, or a user override. Each immediate child of
      `PSP/SAVEDATA/` is one folder unit; never include `PPSSPP_STATE`, config, plugins, textures,
      or other Memory Stick content.
- [x] **DuckStation second:** locate its current/legacy/portable user directory, read
      `settings.ini` read-only, and honor enabled `Card1Type`/`Card2Type` plus explicit card paths.
      Per-game code/title/file-title cards are individual file units; file-title cards retain their
      exact filenames with a portability warning. A shared card is one monolithic unit and carries
      the same cross-game conflict warning as a PCSX2 file card. Never include save states.
- [x] **RPCS3 third:** resolve `/dev_hdd0` through a versioned, read-only `vfs.yml` adapter and bind
      one user-selected local RPCS3 profile to a stable EmuShelf profile key. Each complete
      `home/<user>/savedata/<save>/` directory is one unit, including its `PARAM.SFO`/`PARAM.PFD`;
      trophies, licenses, installed games, caches, configs, and save states stay out of scope.
      The profile key is the unit id's *absence* of an account: ids address the save alone, the
      account is bound locally, and the existing per-system save-location override is what selects
      it when several accounts hold saves (2026-07-26). Extended the same day beyond the original
      scope: the bound account's `trophy/<NPWR…>/` sets and the console-wide
      `dev_hdd0/savedata/vmc` PS1/PS2 Classics cards sync as their own unit namespaces.
- [x] **Dolphin fourth, split by storage model:** discover its real user directory (portable,
      global/legacy, `-u`, XDG/Flatpak, macOS, or override) and read custom paths read-only. Sync a
      raw GameCube card as one unit; sync each GCI as an ordinary file; sync each non-empty Wii disc
      title's `Wii/title/00010000/<title-id>/data/` as one folder unit.
      Never sync the whole NAND; surface that Mii/console-identity-dependent saves may not be fully
      portable. Reads `Dolphin.ini` plus relevant `GameSettings/*.ini` overrides, follows configured
      raw/GCI/NAND paths and slot devices, validates local GCI structure and embedded identity, and
      fails closed on per-game or nested layouts it cannot represent safely. Raw card-size filename
      variants retain distinct portable ids, cross-slot path aliases are rejected, Settings reports
      effective save roots, and every incoming payload is hash-verified before replacing live data
      (2026-07-28).
- [x] **RetroArch last, one verified core adapter at a time:** resolve the effective save path from
      `retroarch.cfg`, core/content-directory/game overrides, save sorting flags, and the configured
      core. Start with the exact cores used by EmuShelf's Mega Drive, DS, GBA, SNES, and Dreamcast
      rows; unsupported cores fail closed. Detect RetroArch's own cloud sync and refuse overlapping
      management rather than run two manifest systems over the same saves. Flatpak targets add the
      host-visible `$HOME/.var/app/<id>/config/retroarch/saves` layout; resolve its effective path
      through the same read-only config/override rules instead of assuming that default.
      Shipped for Mega Drive, SNES, DS, GBA, and Dreamcast. Saves are claimed by game name rather
      than a per-core extension list, so changing core does not silently stop the backup; the folder
      RetroArch sorts into comes from the installed core's own `corename`. While RetroArch's shared
      save folder is in use, each row claims only saves named after its own library entries
      (2026-07-26). Flycast's *shared* VMU images live in RetroArch's system directory and remain out
      of scope — only its per-game VMUs are in the save folder.
- [ ] Provider contract tests cover Windows, Linux/Flatpak, portable, custom, and macOS paths;
      unknown config versions; local + remote-only resolution; card/profile mismatch; traversal and
      symlink rejection; deterministic folder/file-set hashing; and strict save-state/config
      exclusion. Keep the existing engine tests for every manifest transition, clock skew,
      conflicts, forced directions, cancellation, and offline safety.
- [ ] Real-device acceptance for every provider: create an in-game save on Windows, sync to Steam
      Deck, load and advance it in the emulator, sync back, then edit both sides and verify the
      losing copy appears under `Saves/conflicts/`. Build and automated tests remain green on macOS
      even where the v1 UI is Windows-first.

### Phase 3 — Beyond Google Drive

- [ ] Expose rclone's other backends (Dropbox, OneDrive, S3, WebDAV/SFTP, self-hosted Nextcloud)
      through the same connect flow — the transport is already provider-agnostic.

## M30 — Dreamcast library (planned)

- [x] Add strict `.gdi` descriptor discovery for complete, read-only Dreamcast track sets. The
      primary track must validate its IP.BIN marker; loose tracks, CDI, and CHD remain unsupported
      until their logical-track behavior has parity fixtures.
- [x] Add `.chd` support on the same terms: the container's own track table places the
      high-density area, a declared data track must validate its IP.BIN marker, and a validated
      image rules out the PlayStation systems that share the extension. Import, folder scanning,
      metadata, and the rcheevos hash all reach it through one reader, so a GDI set and a CHD of
      the same disc identify and hash identically. CDI remains unsupported.
- [x] Register Dreamcast as a RetroArch/core system and retain its existing licensed navigation
      artwork. Use a portrait `0.708` (width÷height) cover frame matching representative 512×722
      Libretro box art, so downloaded covers and the missing-art placeholder have the same ratio.
- [x] Add exact metadata and cover downloading through the existing opt-in cache/downloader:
      hash the validated primary data track for the Libretro Redump Dreamcast catalogue, then use
      its canonical title with the existing Libretro Dreamcast thumbnail provider. Never title-guess
      an unmatched GDI set.
- [x] Add RetroAchievements console 40 for validated GDI sets only, using the rcheevos Dreamcast
      hash (full IP.BIN plus the named boot executable) and existing credential/cache/progress flow.
      Unverified, incomplete, or unsupported images remain Unknown, never "No achievements".
- [ ] On real Windows, launch a supported `.gdi` set through a configured Flycast RetroArch core;
      verify paths with spaces, BIOS/core configuration stays user-owned, and neither game tracks
      nor RetroArch configuration/overrides/playlists/achievement settings are modified.

## M31 — Controller-first Gamepad shell redesign and hardening (in progress)

This milestone is the concrete implementation of M24 Phase 2 after the 2026-07-25 Gamepad audit.
The audit found interaction failures that require changing the shell and focus model rather than
preserving the existing header verbatim. Work the phases in order and keep Desktop behavior
unchanged unless an item explicitly says otherwise.

### Phase 1 — Correctness and safe navigation

- [x] Fix Search, Rename, Remove, and cover-handoff overlay geometry so body copy, text fields,
      selectable actions, and contextual help never occupy the same layout cell at 1280×800.
- [x] Make `B` a safe Back action: it closes the current overlay or returns rail focus to the
      library, and never switches interface mode from the main shelf (2026-07-25).
- [x] Make cover-grid navigation spatial. Left/Right stop at row edges; Down never jumps sideways
      when the final row has no tile in the current column; platform shoulder navigation remains
      bounded and non-wrapping (2026-07-25).
- [x] Give empty libraries, empty searches, launch/configuration failures, scan progress, and status
      notifications a visible controller-readable Gamepad presentation (2026-07-25; status-driven
      operations share the new Gamepad toast).

### Phase 2 — Steam-like shell and deliberate global menu

- [x] Remove the duplicated platform heading and the focusable Launch/Actions/Exit controls from
      the upper content header. The selected rail tab owns scope identity (2026-07-25).
- [x] Add a persistent bottom command bar with controller-only language and contextual actions:
      Menu, A Play/Select, B Back, X Search, Y Actions, and LB/RB Platforms as applicable.
- [x] Read the controller Start/Menu button natively and map the keyboard/Steam Input fallback to
      the same logical action. Menu opens an in-window global side sheet (2026-07-25; F10 fallback).
- [x] Move Collections, Settings/Desktop handoff, and application-level actions into the global
      menu. Leaving Gamepad mode is named accurately and requires a separate confirmation surface
      (2026-07-25; Settings hands off explicitly and Quit has its own confirmation).
- [x] Keep per-game actions in the Y surface; remove redundant Back and Desktop-mode rows because
      B and the global menu own those responsibilities (2026-07-25; Y opens a right-side sheet).
- [x] Keep the Desktop sidebar and Gamepad platform rail library-focused: hide systems with no
      database entries by default, retain systems whose files are temporarily unavailable, and
      expose a persisted **Show empty platforms** override in General Settings. Import and emulator
      configuration continue to list every supported platform. Membership uses a distinct-system
      database query, empty active platforms fall back to All Games, and background refreshes retain
      tentative controller-rail focus (2026-07-29).
- [x] Make one portable install context-safe: `--gamepad-ui` and `--desktop-ui` are non-persisted
      shortcut overrides, while an unqualified launch uses the remembered interface mode
      and concurrent launches serialize settings updates through a portable lock file (2026-07-29).

### Phase 3 — One focus and input-modality model

- [x] Track controller versus pointer modality. Controller input suppresses stale pointer-hover
      selection; meaningful pointer movement restores mouse affordances without disabling mixed
      input (2026-07-25).
- [x] Synchronize logical focused game/rail/overlay state with Avalonia focus so exactly one
      actionable element has the strong focus treatment. Active platform styling remains visibly
      distinct from input focus, and the shelf focus ring is suppressed behind overlays
      (2026-07-25).
- [x] Keep native Avalonia focus for routing and accessibility but suppress Fluent's additional
      focus adorner on Gamepad surfaces that draw their own focus ring. This prevents doubled
      outlines and Linux/SteamOS compositor corner artifacts (2026-07-26).
- [x] Keep the mixed-platform shelf cell visually transparent in every tile interaction state;
      mouse hover and controller focus follow the actual cover frame so short artwork never gains
      an oversized grey rectangle in All Games (2026-07-26).
- [ ] Add controller-family-aware glyphs or neutral physical-position glyphs; never mix
      `L1/R1`, `A/B/X/Y`, and keyboard key names in one Gamepad surface.
- [ ] Provide a controller-safe text-entry path and automatically request an available on-screen
      keyboard where the host platform permits it; retain ordinary keyboard entry as fallback.

### Phase 4 — Verification and Deck acceptance

- [ ] Replace render-only Gamepad snapshots with reviewed image baselines or equivalent geometry
      assertions that fail on overlap, clipping, off-screen focus, or duplicate strong focus.
- [ ] Cover populated, empty, no-results, unavailable, no-emulator, launch-failure, search, rename,
      collections, actions, disc selection, removal, cover handoff, achievements, and global-menu
      states at 1280×800, plus a large 16:9 living-room viewport.
- [ ] Run keyboard, mouse, Xbox-style pad, PlayStation-style pad, Steam Input, and native SDL paths;
      verify Windows fullscreen and real Steam Deck/Gaming Mode focus, OSK, emulator return, and
      menu behavior before marking M31 complete.

### Approved couch-first continuation (2026-08-01)

This product sequence builds on the hardened M31 shell without copying NeoStation branding,
artwork, or source. The target is a complete living-room interface, not a larger or reduced Desktop
layout. The reference mockup's clock is deliberately excluded: it does not help library navigation
or launch flow, and the host already owns time/status UI.

1. **Focused-game presence.** Replace the thin command-only footer with a persistent dock for the
   focused title: platform, large title, availability/disc context, a prominent **A Play** action,
   and a RetroAchievements count plus progress bar when a confirmed achievement set exists. Add
   fixed semantic colors for A/B/X/Y and a stronger focused-cover shadow while keeping the existing
   per-platform cover ratios and virtualized shelf. The initial dock, semantic prompts, projection
   tests, and 1280×800 render check landed on 2026-08-01. The screenshot-led refinement then made
   achievement and Play surfaces a shared 60px height, removed the redundant percentage and
   "Available" copy, showed the actual launch filename, and added subtle depth to every cover. A
   further populated-library review replaced the two-row 126px footer with one 104px information
   row: only game identity, a custom clipped achievement track, and Play remain persistent. The
   library count and direct-shortcut legend now live in Menu; overlays retain only their contextual
   hints. The achievements overlay now follows the same focused hierarchy: a selected-achievement
   detail card beside a virtualized square-badge grid, All/Locked/Unlocked tabs on LB/RB, spatial
   D-pad navigation, X refresh, and Y cycling Default/Points/Unlocked first/Recently unlocked
   ordering. Sorting atomically replaces the visible rows and keeps the selector in its physical
   grid slot, so the badge under the selector changes without the ring chasing the previously
   selected badge across the screen. A real 86-badge compositor regression replaced collection
   Reset with a fresh immutable item-source snapshot, delays manual realization until the viewport
   is measured, and verifies the top-left cell after every sort mode. Achievement D-pad movement
   now clamps at real row edges/missing final-row cells; filtering always restores layout even when
   the same badge survives, and pointer selection updates the same logical focus as the controller.
   Community unlock-percentage sorting remains deferred until that data has an explicit API and
   portable-cache field. Real-controller tuning remains part of the M31 acceptance pass above.
2. **Settings entirely on the controller.** Replace the Settings-to-Desktop handoff with an
   in-window, sectioned Gamepad surface over the existing settings view model. Land General,
   RetroAchievements, Saves, and Texture Packs first; then emulator paths/arguments. Use the
   controller-safe text-entry path for text and keep an explicit OS file-picker handoff only where
   selecting a native executable or folder is unavoidable. The complete Desktop field audit and
   first in-window slice landed on 2026-08-01: LB/RB changes among those four sections, D-pad owns
   stable per-section row focus, A edits or activates, B safely cancels or returns, and Save is one
   Up press from each section's initial row. Text and secrets use focused, masked in-window entry
   with an automatic host-OSK request where supported and an explicit keyboard/Steam+X fallback;
   native pickers remain limited to real file/folder selection. The surface reuses the existing
   settings view model and services, including persistence and destructive-operation confirmation.
   A populated-library review rejected the initial generic-card presentation on the same day. The
   replacement is a full-height, proportional two-pane screen with a persistent section rail,
   pinned Save action, equal-width virtualized rows, recognizable ON/OFF switches, left/right
   choices, edit/choose affordances, ordinary actions, and visually separate destructive actions.
   Section and Save actions fill the rail width; START invokes the existing Save command directly,
   while Up then A remains a tested controller route. General uses Desktop's field wording and
   values, and real Desktop/Gamepad windows now enforce identical visible mutating-field ids across
   all four sections so a controller-only field or a missing Desktop field fails validation.
   Geometry and focus-reveal coverage now exercises real Avalonia windows at 1280x800, 1280x720,
   and the reported 2048x1152 viewport. Emulator paths/arguments and RPCS3 library maintenance
   remain the next Phase 2 slice.
3. **Controller cover search using the existing DuckDuckGo provider.** Reuse M34's explicit,
   bounded `DuckDuckGoArtworkSearchProvider` and safe preview/download pipeline in a Gamepad
   candidate grid. Search and selection stay user-driven; unverified results never enter automatic
   metadata enrichment. Retain **Use local image** as a secondary, clearly labelled Desktop/file
   picker handoff.
4. **Full portable themes.** Move beyond Light/Dark plus one accent to complete palettes covering
   backgrounds, panels, text, borders, selection, and focus. Provide a controller-native theme
   gallery and portable `Themes/` import, while A/B/X/Y semantic colors remain stable across every
   palette. The palette-swap engine and first complete set landed on 2026-08-01: `ThemeCatalog`
   enumerates System/Light/Dark plus full Nord, OLED, and Cyberpunk palettes, each a flat resource
   dictionary that redefines every `EmuXxxBrush` token (verified by rendering OLED and Cyberpunk with
   no hardcoded colour leaking through). `AppThemeService` swaps the active palette as an override
   dictionary and sets the base `ThemeVariant`; all consumers already use `DynamicResource`, so a swap
   re-colours the UI live. A controller-native theme gallery is a Themes page in Gamepad Settings, and
   the Desktop Settings gallery lists the same catalog, so appearance is changeable in both modes. The
   default accent moved from red to rose so selection/focus no longer reads as the danger colour, and
   the focused game gained a thicker ring, a themed accent glow, and a subtle lift. Portable `Themes/`
   import and Fluent per-theme accent chrome remain the open work for this item.
5. **ScreenScraper.fr integration.** Treat ScreenScraper as a separate authenticated metadata
   provider project, not as the first implementation of the Gamepad picker. Add secure user
   credentials, application/developer credentials, platform-id mapping, hash-first matching with
   title search fallback, locale/region preferences, quota/concurrency handling, cached attribution
   and provenance, and explicit single-game/batch consent. Feed its metadata and media variants
   into the same controller scraper UI only after deterministic fixtures and provider-failure
   isolation are in place. ScreenScraper account sharing and quota workarounds are never supported.
   Foundation landed on the isolated `codex/screenscraper-planning` branch: provider capabilities
   and toggles, detail/media/provenance storage, secure account storage, all supported system
   mappings, a fixture-backed typed API client, quota-aware request admission, scoped cached
   fingerprints, and non-mutating single-game preview orchestration. Live-response validation,
   attribution/caching policy, UI exposure, and mapping verification wait for approved developer
   access; raw formats can be developed safely while container/serial rules remain explicit gates.

## M32 — Installed texture-pack inventory (in progress)

Inventory replacement-texture packs owned by Dolphin, PCSX2, DuckStation, and PPSSPP, match them
to library games through the exact identifiers each emulator uses, and surface confirmed matches
in the library. This feature is strictly read-only: EmuShelf never edits emulator configuration,
moves or deletes packs, or writes into dump/replacement directories. "Installed" means a usable
pack was found and matched; it does not claim that the emulator will load it unless the effective
loading setting can also be resolved without guessing.

### Phase 1 — Inventory model and read-only path discovery

- [x] Add Core contracts for emulator-specific texture-pack sources and an inventory snapshot keyed
      by emulator installation. Keep this external state separate from `Game`; persist scan time,
      resolved root, pack key/path, matching scope, validation state, and errors without copying
      texture contents into EmuShelf (2026-07-26; atomic portable JSON cache).
- [x] Resolve texture roots per configured installation through versioned, read-only adapters and an
      optional user override. Reuse PCSX2's configuration-directory parsing and PPSSPP's Memory
      Stick resolution; support DuckStation current/legacy/portable layouts and Dolphin portable,
      platform-default, custom user-directory, and applicable Flatpak layouts. Unknown versions or
      ambiguous custom paths fail visibly rather than falling back to a plausible but unproven root.
      (Portable PCSX2/DuckStation INIs, PPSSPP Memory Stick reuse, effective Dolphin User folders,
      and explicit overrides landed 2026-07-26; `EmulatorUserDirectories` completed platform-default
      and Flatpak discovery 2026-07-26, selecting only candidates that exist so an unconfigured
      emulator reads as unconfigured rather than missing.)
- [x] Run cancellable scans off the UI thread on first setup, path/configuration changes, explicit
      **Rescan**, and optional cache staleness — never recursively rescan every texture pack during
      every application startup. Cache the last good inventory and preserve it with a visible stale
      or unavailable status when an external drive is disconnected (2026-07-26; `TexturePackCoordinator`
      loads cache only at startup, holds a single-flight gate, and is cancellable throughout).

### Phase 2 — Emulator-accurate pack validation and identifier matching

- [x] **PCSX2:** inventory `<Textures>/<serial>/replacements`; match the normalized PS2 serial and
      require at least one filename and image format accepted by PCSX2. A serial folder containing
      only `dumps`, empty directories, and wrong-case layouts that fail on case-sensitive systems
      are attention states, not installed-pack matches. (2026-07-26; verified against live packs.)
- [x] **DuckStation:** inventory current `<Textures>/<serial>/replacements` and the supported legacy
      layout; validate recognized replacement names plus `config.yaml` aliases. Match exact PS1
      serials and mirror DuckStation's first-disc fallback so one pack can correctly cover a
      multi-disc set (2026-07-26; the set-level `GetMatches(gameIds)` overload matches a displayed
      multi-disc title when any of its discs matches, and deduplicates shared packs).
- [x] **Dolphin:** inventory `User/Load/Textures` using exact six-character game IDs, explicit
      three-character region-independent folders, nested game-ID marker files, and shared `all.txt`
      packs. Require recursively discoverable `tex1_` PNG/DDS replacements; report shared packs
      separately and never label them as having no library match. (2026-07-26; verified against live packs.)
- [x] **PPSSPP:** inventory `<Memory Stick>/PSP/TEXTURES/<game-id-without-hyphen>` directories and
      supported `textures.zip` packs. Match normalized PSP disc IDs, recognize valid replacement
      content/configuration, and treat a dump-only `new` directory as attention rather than an
      installed pack. (2026-07-26; verified against live packs.)
- [x] Bulk-load cached `GameIdentifier` rows and build the game-to-pack map in one background pass —
      no per-row database reads, repeated disc parsing, title matching, or fuzzy serial matching.
      A displayed multi-disc title is matched when any applicable disc is matched; two emulator
      installations retain separate inventories (2026-07-26; `IGameMetadataStore.GetAllIdentifiers`
      plus `TexturePackLibraryMap.Build`, asserted by a no-N+1 coordinator test).
- [x] Classify inventory entries as **Matched**, **No library match**, **Shared pack**, **Empty or
      dumps only**, **Unrecognized layout**, **Folder unavailable**, or **Identifier pending**.
      "No library match" deliberately does not imply that the pack is broken or safe to delete
      (2026-07-26; `TexturePackEntryStatus`, with an unmatched pack staying `IdentifierPending`
      until the library actually holds identifiers of that kind).

### Phase 3 — Library marks and Settings inventory

- [x] Add a sortable **Textures** column to Desktop list view after Achievements. Show a small
      image/layers mark with `Installed` (or a pack count) only for confirmed matches, an em dash
      otherwise, and a tooltip containing emulator, matched identifier, pack location, validation
      state, and loading status when known (2026-07-26).
- [x] Add the same neutral, non-clickable mark beside the achievement badge on Desktop grid covers.
      Drive grid visibility, list text, tooltip, and sort value from one pure display-state result so
      the views cannot disagree (2026-07-26; `TexturePackDisplay`, with both marks in one top-left
      stack so the texture mark keeps its place whether or not the trophy shows). Extending the mark
      to Gamepad covers after M31's focus/template work, and verifying it does not obscure disc,
      availability, or focus treatments, remains open.
- [x] Add a dedicated **Texture packs** Settings section with matched/no-match/attention totals,
      last scan time, emulator and status filters, detected/overridden roots, **Rescan**, and
      **Open folder**. List pack ID, matched game(s), installation, path, and status; provide no
      delete, move, rename, install, or repair operation (2026-07-26; a test asserts the command
      surface contains only Rescan, Open folder, and the two override actions).
- [x] Resolve effective global/per-game replacement-loading settings only through emulator-specific,
      versioned read-only adapters. Report **Loading disabled** when it is proven and **Loading
      status unknown** when precedence, configuration version, or runtime override cannot be
      resolved; the library mark continues to mean installed and matched rather than guaranteed
      active (2026-07-26; a per-game configuration file for the game being asked about forces
      Unknown — see `DECISIONS.md`).

### Phase 4 — Verification and acceptance

- [ ] Provider fixtures cover Windows, macOS, Linux/XDG, portable, custom, and supported Flatpak
      roots; malformed/unknown configuration; missing and disconnected folders; case sensitivity;
      empty and dumps-only layouts; PPSSPP ZIPs; DuckStation aliases/first-disc fallback; and
      Dolphin exact, three-character, marker-file, and shared packs. (Source, root-resolver, cache,
      and loading-resolver fixtures landed 2026-07-26; the platform-default/Flatpak candidate order
      in `EmulatorUserDirectories` is still only exercised on the host's own layout.)
- [x] Matching tests cover identifier normalization, region variants, multi-disc aggregation,
      multiple emulator installations, missing identifiers, strict no-title/no-fuzzy behavior, and
      the rule that invalid content never produces a library mark (2026-07-26;
      `TexturePackLibraryMapTests`).
- [x] Verify cancellation, scan-cache invalidation, no N+1 database work, short-circuit validation,
      list sorting/tooltips, Settings filters, zero-pack/error states, and Desktop light/dark
      layouts (2026-07-26; coordinator, display, and Settings-section tests). Gamepad cover geometry
      at 1280×800 and a large 16:9 viewport waits on the Gamepad mark above.
- [ ] On real Windows and SteamOS/Linux installations, snapshot game files, emulator configuration,
      and texture roots before and after detection/rescan; verify all bytes and timestamps remain
      unchanged and that a missing or unreadable provider cannot block the rest of the library.
      (Windows verified 2026-07-26 against a real ES-DE library: 649 games and 208 packs across
      PCSX2/Dolphin/DuckStation/PPSSPP, 171 matched. All four roots resolved from the emulators'
      own configuration — including relative paths and Dolphin's redirected `LoadPath` — and all
      four reported replacement loading correctly. Emulator configs and pack files were unchanged
      in bytes and timestamps after repeated rescans, and an empty Dolphin root did not stop the
      other three providers. SteamOS/Linux remains open.)

## M33 — Sync beyond saves: states, cheats, patches, per-game settings (planned)

M29 syncs the games' own battery/memory-card saves and stops there, on the reasoning that
everything else is either machine-specific or fragile. That reasoning holds for *some* of it, but a
library that moves between a desktop and a Steam Deck loses more than saves: a save state mid-boss,
a widescreen patch, a cheat file, and the per-game settings that made a game run at all. This
milestone extends the existing engine — one unit id, one manifest baseline, non-destructive
conflicts — to those kinds, one kind at a time, with the risky ones behind their own switch.

Everything here reuses M29's machinery. A content kind is a new unit-id namespace under the same
provider (`duckstation/cheats/…` beside `duckstation/per-game/…`), so the planner, backup-before-
overwrite rule, conflict backups, and activity log need no new concepts.

### Phase 1 — Foundation

- [x] Per-kind opt-in per platform, defaulting to saves only. Emulator cheat and patch roots can
      contain thousands of bundled database files, while save states can be gigabytes and change
      every session, so optional kinds participate only in manual Sync all/replace actions. Settings
      shows each kind's exact resolved path, eligible file count, and size before it is enabled.
- [x] Per-file units for kinds made of many independent files, so one changed state does not
      re-upload a folder. The existing folder unit stays for save data that is only meaningful whole.
- [x] Sync every manual state present without deleting local or cloud copies. Exclude auto/undo slots
      (`.state.auto`, DuckStation's resume state, PCSX2's backup slot), which change on every exit and
      are worth nothing on another machine.
- [x] Kind-aware conflict handling. A cheat or patch file is user-edited text where "keep the newer
      one" is wrong: keep both sides, both readable, and say so — the current timestamp tie-break
      stays right for opaque binary state.

### Phase 2 — Portable-by-nature kinds (do these first)

- [x] **Cheats.** DuckStation `cheats/<serial>.cht`, PCSX2 `cheats/<CRC>.pnach`, PPSSPP
      `PSP/Cheats/*.ini`, RetroArch `cheats/`, Dolphin's Gecko/AR sections. Small, text, keyed by
      game id rather than by machine; the clearest win in this milestone. Dolphin's Gecko/AR
      sections remain excluded because they share the same INI files as the per-game settings the
      user chose not to sync; copying those files wholesale would also copy machine-bound settings.
- [x] **Patches.** PCSX2 `patches/` pnach files and RPCS3 `patches/patch.yml` — the community
      patch sets that carry widescreen and performance fixes, and that are pure content with no
      machine-specific paths. Soft patches that live *beside the ROM* (`.ips`/`.bps`/`.ups`) are
      excluded: EmuShelf never writes into the user's game folders.
- [ ] **Per-game settings.** DuckStation `gamesettings/`, PCSX2 `gamesettings/`, Dolphin
      `GameSettings/`, RetroArch per-game overrides. High value — this is the tuning that makes a
      specific game work — but the files can name a renderer, an adapter, or an absolute path, so
      sync must filter machine-bound keys rather than copy blindly, and a Deck and a desktop must be
      able to keep different graphics choices for the same game.

### Phase 3 — Save states, behind a version guard

- [x] **Save states.** DuckStation `savestates/`, PCSX2 `sstates/*.p2s`, PPSSPP
      `PSP/PPSSPP_STATE/*.ppst`, Dolphin `StateSaves/`, RetroArch `states/*.state`, RPCS3's own
      `.SAVESTAT`. These are the reason the original exclusion existed: a state is bound to the
      emulator build that wrote it, and often to its CPU architecture, so restoring one into a
      different build ranges from a graphical mess to a crash.
- [x] Record the writing emulator's version and CPU architecture beside each state unit, and refuse to
      restore a state whose version does not match the local emulator — surfacing it as "available,
      not restored" rather than silently overwriting or silently skipping. A matching version
      restores normally.
- [x] Bandwidth honesty: show the transfer size before the first sync of a platform's states, and
      keep them out of the pre-launch pass's critical path — a state is not needed to *start* a
      game the way a memory card is.

### Phase 4 — Worth considering, decide before building

- [ ] **EmuShelf's own library** (`Data/library.db`, `Covers/`) so a second machine sees the same
      collection, ratings, and artwork. Arguably the highest-value item here after cheats, and the
      one with the most design questions: stored paths differ per machine, and the cover cache is
      large. Needs its own decision on portable paths before any code.
- [ ] **Screenshots and captures** — RetroArch `screenshots/`, PPSSPP `PSP/SCREENSHOT/`, RPCS3
      `captures/`. Purely additive, no conflicts possible, cheap to implement; low value, so only
      worth it once the machinery above exists.
- [ ] **Controller profiles and input remaps** — RetroArch `config/remaps`, DuckStation and PCSX2
      input profiles. Tempting and usually wrong: device names, indices, and Steam Input differ
      between a Deck and a desktop, so a synced remap can silently break input on the other machine.
      If it happens at all, it should be an explicit "copy this profile there", not background sync.
- [ ] Deliberately out of scope, recorded so it is not revisited: BIOS and firmware images (large,
      static, and the user's own to place), texture packs (gigabytes, already inventoried by M32),
      emulator binaries, and RetroAchievements progress (the server owns it).

## M34 — User-driven web cover picker ✅ (2026-07-29)

- [x] Replace Desktop **Set cover…**'s file-only interaction with a Grimmory-style search dialog:
      prefill title/platform, show a bounded preview grid with resolution/source host, keep an
      explicit local-file action, and apply nothing until the user selects a result.
- [x] Keep unverified web results out of automatic metadata enrichment. Search DuckDuckGo Images
      only on an explicit user action, retain search rank, use the platform cover ratio only as a
      light ordering signal, and never guess that a result belongs to a ROM.
- [x] Route previews and selected images through the bounded artwork path: require HTTPS public
      addresses on the initial URL and every redirect, cap both compressed bytes and decoded pixel
      dimensions, decode scaled previews off the UI thread as they arrive, delete staging files,
      and preserve the accepted portable `Covers/` copy as user-owned artwork.
- [x] Cover query construction, unsafe/tiny-result rejection, format fallback, picker selection,
      local-file fallback, portable import, and temporary-file cleanup with deterministic tests;
      verify the live search-token/results exchange without downloading or redistributing artwork.

## M35 — Arcade (FinalBurn Neo)

Single user-facing **Arcade** platform, launched through the existing RetroArch launcher with the
user-supplied `fbneo_libretro` core. FinalBurn Neo only — no MAME, Naomi/Flycast, Atomiswave, or
TeknoParrot. BIOS management and ROM repair/conversion stay out of scope. Builds and the full test
suite are green on macOS; a real-romset launch on Windows is the remaining acceptance gate.

- [x] Register the `arcade` system (stable id, landscape 4:3 cover ratio) and add it to
      `RetroArchDefinition` so it launches a `.zip` through `-L {CorePath} {GamePath}`.
- [x] Import `.zip` under Arcade (suggest-by-extension, user confirms); one zip is one game; the
      archive is never opened and user files are never modified. `.7z` deferred — no 7z reader.
- [x] Hide BIOS/device archives (neogeo, pgm, …) at import and in folder scans via a bundled
      set-name list; the FBNeo DAT's `isbios`/`isdevice` flags are the authoritative filter during
      enrichment.
- [x] Recognize sets by zip basename == FBNeo DAT `game name`; canonical title from the
      `<description>` element; unmatched zips keep a filename title rather than being discarded.
- [x] Read the FBNeo Arcade DAT (`metadat/fbneo-split`, Logiqx XML) through a streaming XML parse
      path in `LibretroDatCatalog`, selected per profile, with a raised per-profile size cap for the
      ~8k-set DAT.
- [x] Arcade artwork from the libretro `FBNeo - Arcade Games` thumbnails, subfolder order
      Named_Titles → Named_Snaps → Named_Boxarts, falling back to the bundled Arcade placeholder.
- [x] Cloud save-sync parity: FBNeo battery/NVRAM `.srm` saves and `.state` save states sync through
      the generic RetroArch descriptor, matched by zip basename, with the same core-version state
      gating as the other RetroArch platforms.
- [x] Deterministic tests: Logiqx-XML parse (name/description/isbios/clone), BIOS hiding, `.zip`
      routing, arcade artwork candidate order, and save-sync registration. Full suite green on macOS.
- [ ] Verify on Windows: a real FBNeo romset (e.g. `mslug`, `sf2`) launches through RetroArch with
      the `fbneo_libretro` core, a BIOS-dependent game surfaces a clean failure when `neogeo.zip` is
      absent, and saves/states round-trip through cloud sync.

## M36 — Automatic save states and visible library folders

- [x] Automatically synchronize opted-in, version-guarded save states before launch and after
      emulator exit. Ordinary battery/memory-card saves commit first, and launch waits for the
      complete pass rather than abandoning it after a fixed application-level budget.
- [x] Show every remembered ROM folder in its platform's emulator-settings row, including missing
      folders, and allow the user to add, change, forget, and rescan roots without touching game
      files. Keep RPCS3 on its explicit emulator-owned library workflow.
- [x] Preserve library records when a remembered root moves by rebasing games whose relative paths
      are present in the replacement scan; retain unmatched games as unavailable and reject path
      conflicts atomically.
- [x] Deterministic view-model, persistence, launch-lifecycle, and save-provider tests; full build
      and test suite green.

## M37 — Nintendo 3DS (Azahar)

3DS is a standalone-emulator handheld cartridge system launched through Azahar (the maintained
Citra successor), not RetroArch. The first pass recognizes and launches every Azahar container
while extracting exact identity only from the uncompressed dumps.

- [x] Register `3ds` as a stable, separately filterable system and `azahar` as a standalone
      emulator (own executable, `"{GamePath}"` launch template), landing together so a system with
      no emulator never breaks Settings. Bundle an original 3DS clamshell placeholder icon (OpenEmu
      ships none) and a measured near-square 1.129 cover frame from GameTDB's fixed 768×680 canvas.
- [x] Recognize and launch every format Azahar loads by a bounded magic/structure check: NCSD
      cartridge (`.3ds`/`.cci`), NCCH title (`.cxi`/`.app`), CIA (`.cia`), homebrew (`.3dsx`/`.elf`/
      `.axf`), and the seekable-Zstandard compressed variants (`.z3ds`/`.zcci`/`.zcxi`/`.zcia`/
      `.z3dsx`). A renamed arbitrary file is never imported.
- [x] Extract exact identity from uncompressed NCSD/NCCH dumps only — the plaintext NCCH product
      code (primary, the GameTDB cover key) and title id — via targeted header reads, with no
      decryption and no whole-file hashing (dumps are multi-gigabyte). Compressed, CIA, and homebrew
      files carry no header identity and match covers by filename until their own reader lands.
- [x] Resolve covers through an id-addressed GameTDB 3DS provider keyed by the product code, so
      artwork resolves without a catalogue title match; the No-Intro 3DS DAT and Libretro title
      provider are best-effort fallbacks. Reuse the existing opt-in consent, caching, downloader,
      provenance, and user-ownership rules.
- [x] Reader/launch/extractor/provider fixtures plus updated system-set assertions; full build and
      test suite green on the dev SDK. RetroAchievements stays unmapped for 3DS (shown unsupported,
      like PS3), matching the "no RA yet" scope.
- [x] Save sync (parity with every other platform): `AzaharSaveLocationProvider` syncs each title's
      SD-card save archive and each extdata archive by its machine-independent id, rebased under the
      local console-unique `ID0/ID1`, so a save moves across machines; installed updates/DLC and
      save states are excluded (the M29 boundary). Registered in `SaveProviderRegistry`, with a
      cross-console round-trip test.
- [x] Texture packs (parity with the HD-enhancement emulators): `load/textures/<title id>` inventory
      keyed by a new `TexturePackMatchRule.Nintendo3dsTitleId` (with `GameIdentifierKind.TitleId`
      indexing added to the matcher/library map) and `qt-config.ini [Utility] custom_textures`
      loading state; registered in `TexturePackProviderRegistry`.
- [ ] On real Windows with Azahar, verify import/metadata for a decrypted `.3ds`/`.cci` and a
      `.cxi`, a `.z3ds`, a path containing spaces, missing-executable preflight, minimize/restore
      after tracked zero and non-zero exits, and that neither the ROM nor Azahar data is modified.
- [x] Verified read-only against a real Windows Azahar install (portable `user/`, opt-in
      `EMUSHELF_TEST_AZAHAR_DIR` test): the save provider enumerated all 8 title/extdata save units
      and resolved each to its real SD-card folder, the texture inventory found usable
      `load/textures/<title id>` packs keyed by title id (11 pack folders), and `qt-config.ini
      [Utility] custom_textures` read as a concrete state. Confirmed the portable `user/` layout, the
      all-zero default console `ID0/ID1`, lowercase `title/<hi>/<lo>` vs uppercase `extdata` ids, and
      uppercase 16-hex texture folder ids.
- [ ] Remaining: a live rclone cloud save round-trip on real hardware, and a real emulator
      launch/return of a game (both need a configured remote / manual play session).

### Deferred follow-ups

- Exact identity for the compressed `z*` and CIA containers (Zstandard-frame metadata / ticket-TMD
  parsing), so those formats get precise covers rather than a filename match.
- The SMDH short title from the NCCH ExeFS as an embedded display title.
- RetroAchievements for 3DS once an exact, verified hash/console mapping exists.

## M38 — Recently Played collection ✅ (2026-08-04)

A first-class smart collection beside All Games and Recently Added — the recency data layer a
future Steam-style multi-shelf "home" view would compose from (that home view is deliberately out of
scope here; see `DECISIONS.md`).

- [x] Persist a launch time: schema v15 nullable `Games.LastPlayedUnixMilliseconds` (partial index
      over played rows), surfaced as `Game.LastPlayedAt` (`DateTimeOffset?`, null = never played) with
      an `IGameLibrary.SetLastPlayed` writer. A single column, not a play-history table; PlayCount /
      playtime are a later, deliberate addition.
- [x] Stamp at launch: written in the `beforeStart` callback, which fires only after preflight passes
      and immediately before the emulator process starts, so failed-validation launches are never
      recorded and a started game is recorded even if EmuShelf is killed mid-session. No game file is
      touched.
- [x] Add `LibraryScope.RecentlyPlayed`, mirroring Recently Added: excludes never-played games, groups
      multi-disc title sets and surfaces each by its most recently played disc, orders most-recent
      first, caps at 30. Reuses the scope cache, virtualized grid, empty states, desktop COLLECTIONS
      sidebar entry, Gamepad Collections overlay, and scope persistence.
- [x] Recency collections display in recency order: `SortGames` preserves the load order for
      RecentlyAdded/RecentlyPlayed instead of applying the Title column sort (also fixing Recently
      Added, previously only newest-first by title coincidence). A recorded play refreshes the
      collection surgically — rebuild if viewing it, otherwise evict its cache for the next visit.
- [x] Tests: `SetLastPlayed` round-trip + null default, schema-v15 migration version, and headless
      scope coverage (never-played exclusion, recency order, empty-state copy, launch stamps + surfaces,
      launch-from-within moves to front). `dotnet build`/`dotnet test` green on macOS (1208 tests).
- [ ] On real Windows, launch a game and confirm it appears at the top of Recently Played on return,
      persists across restart, and that no game file or emulator data was modified.

## M39 — In-app auto-update from GitHub ✅ (2026-08-05)

Notify → update → relaunch, from GitHub Releases, without leaving SteamOS gaming mode. Reuses the
per-platform portable artifacts + `.sha256` CI already publishes; no packaging changes. Full design
in `docs/auto-update.md`; the framework-vs-hand-rolled call is in `DECISIONS.md`.

- [x] Core `Updates/`: `SemanticVersion` (tolerant `vX.Y.Z` parse/compare), result models, and the
      `IUpdateService` / `IUpdateApplier` interfaces. `UpdateSettings` (opt-out, last-check throttle,
      skipped version) added to `AppSettings`.
- [x] `GitHubUpdateService`: reads `releases/latest`, selects this platform's asset, streams it to
      `Cache/updates/`, and **verifies SHA-256 against the release's checksum file before use** — a
      mismatch deletes the file and aborts.
- [x] Per-platform appliers: AppImage replace + `execv` (same PID → gaming mode retained on the Steam
      Deck); Windows `.cmd` overlay that preserves portable user data; macOS bundle swap with
      quarantine cleared. Selected by `UpdateApplierFactory`; a no-op fallback keeps dev runs safe.
- [x] `AppUpdateCoordinator`: throttled launch check + notification banner (`Update & restart` /
      `Later` / `Skip this version`) and the download→apply flow. Controller-operable via Settings →
      General; Desktop adds Settings → About "Check for updates".
- [x] Tests: version parse/compare, asset selection per RID, release-JSON + checksum parsing,
      end-to-end check/download/verify (including a tampered-checksum abort), settings round-trip, and
      coordinator throttle/skip/apply logic. `dotnet build`/`dotnet test` green on macOS (1318 tests).
- [ ] On real Windows and a real Steam Deck (gaming mode), install an update end to end and confirm
      the relaunch keeps portable data intact (Windows) and never drops to the desktop (SteamOS).

## M40 — Uniform keyboard hotkeys across emulators (implemented 2026-08-08; real-hardware check pending)

One uniform **keyboard**-hotkey scheme written into each supported emulator's own config, so a key means
the same thing everywhere: **R** = rewind, **L** = fast-forward, **F2** = save state, **F4** = load
state, **F8** = close game (keys chosen to match RetroArch's own defaults). The controller→key step is
done once, outside the emulators, in a **Steam Input** layout the user imports (hold Select + a face
button), so one mapping serves every emulator. This is EmuShelf's first substantial write into emulator
configuration — a user-approved, reversible break from the read-only stance. It authors bindings **per
machine** and never syncs them (the machine-local counterpart to M33 Phase 4). Conflicting default
shortcuts are overwritten (backed up, revertible). Scope: DuckStation, PCSX2, Dolphin, PPSSPP, RetroArch,
Azahar, and RPCS3 (close-only). The pivot from the original controller-chord approach and the verified
per-emulator tokens are in `DECISIONS.md` (2026-08-08) and `docs/hotkey-keyboard-scheme.md`.

### Phase 1 — Canonical model and a reversible config writer

- [x] Core: an `IEmulatorHotkeyConfigurator` seam plus a canonical model — `HotkeyAction`
      (CloseGame, Rewind, FastForward, SaveState, LoadState), a `HotkeyKey` (R, L, F2, F4, F8), the
      recommended default profile, a per-action result (`Bound` / `Unsupported(reason)`), and an apply
      status (`Changed` / `Unchanged` / `EmulatorRunning` / `ConfigurationNotFound` / `UnsupportedFormat`
      / `Failed`) so an emulator that cannot express an action reports it instead of failing the apply.
- [x] A surgical config editor (`EmulatorConfigDocument`) in Integrations that updates/inserts only the specific lines
      and leaves comments, ordering, unknown keys, and version/format markers byte-identical — the
      read-only `EmulatorIniFile` is not reused for writing. Writes go through `AtomicFile`.
- [x] Reversibility: back up each target file into portable `Settings/` before its first modification,
      a preview-diff of exactly what would change, and a revert-to-backup action. Apply is refused
      while the target emulator process is running and states "takes effect next launch".
- [x] Each action's binding is the fixed keyboard token for its key — the same key on every controller,
      so nothing is derived from the pad and there is no per-machine resolution step to get wrong.

### Phase 2 — The INI-section emulators

- [x] **DuckStation**: `settings.ini [Hotkeys]` `Keyboard/<Key>` for PowerOff / FastForward /
      SaveSelectedSaveState / LoadSelectedSaveState / Rewind; also set `[Main] RewindEnable=true` when
      binding rewind; preserve `SettingsVersion`. Load state (F4) displaces the default
      `SelectNextSaveStateSlot`, which the conflict-clearing unbinds.
- [x] **PCSX2**: `PCSX2.ini [Hotkeys]` `Keyboard/<Key>` for ShutdownVM / HoldTurbo / SaveStateToSlot /
      LoadStateFromSlot; **no rewind action exists** → left unbound and reported. Load state (F4)
      displaces `ToggleFrameLimit`; exact-value clearing leaves modifier chords like `Shift & F8`
      untouched.
- [x] **Dolphin**: `Hotkeys.ini` fully-qualified `` `DInput/0/Keyboard Mouse:<Key>` `` for General/Exit,
      Save/Load to-selected-slot, and Disable-Emulation-Speed-Limit as the fast-forward analog; **no
      rewind** → unbound + reported. The token resolves regardless of the controller `Device =` line.
- [x] **PPSSPP**: `controls.ini [ControlMapping]` `1-<NKCODE>` (device 1 = keyboard) for Rewind /
      Fast-forward / Save State / Load State / Exit App — single keys, so no `AllowMappingCombos` flag
      is needed.

### Phase 3 — RetroArch and the keyboard-native emulators

- [x] **RetroArch**: section-less `retroarch.cfg` quoted keys — `input_rewind="r"`,
      `input_hold_fast_forward="l"`, `input_save_state="f2"`, `input_load_state="f4"`,
      `input_exit_emulator="f8"` (default was `escape`), plus `rewind_enable="true"`. Clear any
      controller hotkey buttons an earlier version wrote (`*_btn` back to `nul`). No autoconfig or
      button-number resolution — keyboard keys are the same on every driver.
- [x] **Azahar**: `qt-config.ini [UI]` Qt shortcuts (`Shortcuts\Main%20Window\<Name>\KeySeq` + a
      `\KeySeq\default=false` pin) for Quick Save (F2), Quick Load (F4), Stop Emulation (F8), and Toggle
      Turbo Mode (L); **no rewind**. Action names vary by Azahar version, so each binds whichever
      candidate name exists in the config and clears any shortcut holding the same key.
- [x] **RPCS3**: `GuiConfigs/CurrentSettings.ini [Shortcuts]` `game_window_stop=F8` — **close-only**
      (RPCS3 has no load-state hotkey; save is Ctrl+S / suspend-resume). Other actions reported
      unsupported.

### Phase 4 — Settings surface, Steam Input preset, and verification

- [x] A `SettingsSection.Hotkeys` presenting the five canonical actions once, a per-emulator
      status (applied / unsupported-here / needs the emulator closed), and Apply / Revert —
      one apply-to-all plus per-emulator. (A dry-run Preview button was later removed as redundant — it
      only showed a change count, not a diff; see `DECISIONS.md` 2026-08-09.) Gate Apply on the emulator
      not running; keep all write policy in the view model/services, code-behind as view wiring only.
- [x] A controller-native Hotkeys surface for Gamepad mode at parity with the Desktop section, reached
      from the gamepad Settings General row (Desktop's matrix section can't be walked as a flat row
      list). `GamepadHotkeysViewModel` wraps the *same* `EmulatorSettingsViewModel` the gamepad Settings
      already built — reusing its rows, Apply-to-all / Install-Steam-template commands, scheme summary
      and Steam status verbatim — and adds only a linear D-pad focus model over Apply-to-all, Install
      template, and each operable emulator's Apply / Revert. A `GamepadOverlayKind.Hotkeys` overlay
      (`MainWindow.axaml`) renders the emulators × actions matrix beside the hold-Select controller
      mapping; the matrix scrolls the focused row into view. Gamepad-native, never a Desktop hand-off.
- [x] Deterministic tests: surgical edits preserve comments/order/unknown keys/version markers on real
      fixture configs; each emulator's tokens and unsupported-action reporting; backup/preview/revert;
      the refuse-while-running guard; and idempotent re-apply. `dotnet build`/`dotnet test` green on
      macOS.
- [x] A Steam Input preset + import guide (`docs/steam-input-preset.md`): hold Select and press
      Square→R, Circle→L, Triangle→F2, Cross→F4, Start→F8. One layout serves every emulator (shared keys).
- [ ] On real Windows against the live `G:\ES-DE` emulators, apply the scheme to each of the seven and
      confirm in-emulator that each supported key works, unsupported ones are reported not bound,
      existing user files are backed up, and revert restores them byte-for-byte.
- [ ] **Linchpin**: confirm Steam Input's emulated keystrokes actually reach RetroArch (it filters
      injected input, libretro #16438) — testable with zero code against RetroArch's existing keyboard
      defaults before relying on the preset for RetroArch.

### Phase 5 — Steam Deck real-hardware fixes (2026-08-09)

First Steam Deck run surfaced four issues; details in `DECISIONS.md` (2026-08-09).

- [x] **Dolphin**: create `Config/Hotkeys.ini` when it is absent (Dolphin writes it lazily, so a fresh
      install had none and apply reported not-found). `HotkeyConfiguratorBase.Apply` now creates the
      target directory before writing a planned-but-not-yet-existing file.
- [x] **RetroArch**: F8 (close) collided with RetroArch's default `input_screenshot` and `quit_press_twice`
      defaulted true, so Select+Start screenshotted and needed two presses. Neutralise `input_screenshot`
      (even when absent → internal f8 default) unless moved off F8, and set `quit_press_twice=false`. A key
      survey confirmed F8 stays (no key is unbound everywhere *and* bindable on Dolphin/macOS); RetroArch
      is the only emulator with an *internal* F8 screenshot default, so it's the only one needing this —
      PCSX2 ships no default hotkey keys, DuckStation's screenshot is F10, Dolphin's is F9.
- [x] **RetroArch**: the D-pad started changing the save-state slot (and screenshot/pause/fps fired off
      face buttons) after apply. RetroArch's single hotkey-enable gate is shared by keyboard and
      controller; the keyboard scheme leaves it off (so bare Steam-Input keys fire), which also un-gates
      controller buttons a stock autoconfig binds to game buttons. Fix: clear those controller bindings —
      nul both `<control>_btn` and `<control>_axis` for the scheme's actions, `input_enable_hotkey`, and
      the usual autoconfig hotkeys (`state_slot ±`, `screenshot`, `pause_toggle`, `fps_toggle`,
      `runahead_toggle`, `toggle_fast_forward`); game inputs (`input_playerN_*`) are untouched. Option A1,
      backed up and revertible; details in `DECISIONS.md` (2026-08-09).
- [x] **DuckStation**: the `SettingsVersion` gate now refuses only a *different* explicit version; a
      *missing* one is accepted when the `[Main]` section is present (newer AppImage/fork builds omit it),
      which fixes the Steam Deck `unknown` refusal. Diagnostics also name the exact file read.
- [x] Correct the "Import the bundled Steam Input layout" wording (no importable file exists) and surface
      the controller mapping + Steam Deck steps in the Hotkeys settings section, not only in a repo doc.
- [x] Decided against an EmuShelf-generated Steam Input `.vdf`: the mechanism (drop into
      `controller_base/templates/`) works, but the hold-modifier/action-layer config is undocumented and
      unverifiable without Steam's own exporter. Documented the reliable path instead — build the layout
      once, **Export Config** in Steam (Steam-generated template), and reuse it across every emulator
      (`docs/steam-input-preset.md`).

### Phase 6 — Steam Deck real-hardware fixes, round 2 (2026-08-10)

A second Steam Deck run showed Dolphin still reporting "isn't Dolphin's user directory (no
Config/Dolphin.ini there)"; details in `DECISIONS.md` (2026-08-10).

- [x] **Dolphin config directory on Linux is a separate XDG tree.** Phase 5's create-`Hotkeys.ini` fix
      was rooted at `<dataDir>/Config`, but Dolphin's `SetUserDirectory` puts config under
      `$XDG_CONFIG_HOME/dolphin-emu` (Flatpak `.var/app/<id>/config/dolphin-emu`), holding the `.ini`
      files directly, while saves/textures stay under the data user dir. New
      `EmulatorUserDirectories.FindDolphinConfigDirectory` resolves it per platform (mirroring
      `FindDolphin`'s precedence); `HotkeyProviderRegistry` feeds it to `DolphinHotkeyConfigurator`,
      which now takes the config directory outright. Only hotkeys surfaced this — saves/textures read
      the same wrong path but fall back to correct data-dir defaults when `Dolphin.ini` is absent.
- [x] **Follow-up:** `DolphinTextureRootResolver` and `DolphinSaveLocationProvider` no longer read
      `<dataDir>/Config/Dolphin.ini` on Linux/Flatpak, so a *relocated* Load/save folder is now honoured.
      The texture resolver takes a `configDirectory` (defaulting to `<userDir>/Config`) that
      `TexturePackProviderRegistry` fills with `FindDolphinConfigDirectory`; the save provider resolves
      the config dir internally (`GetConfigDirectory`), mirroring its own user-directory resolution
      because it must also honour Settings overrides, `-u`/`--user`, and portable mode — cases the static
      helper doesn't model. Details in `DECISIONS.md` (2026-08-10).
- [x] **Follow-up (3rd consumer):** `DolphinTexturePackLoadingResolver` read `GFX.ini` from
      `<dataDir>/Config` too, so the "will Dolphin load these packs?" status showed Unknown on
      Linux/Flatpak. `GFX.ini` (config tree) and per-game `GameSettings/` (data tree) live in different
      trees there, so the shared `IniTexturePackLoadingResolver` gained an optional `perGameRootDirectory`
      (default = config dir, so the other emulators are unchanged) and Dolphin passes both roots. Details
      in `DECISIONS.md` (2026-08-10).

### Deferred (recorded so it is not re-litigated)

- The original controller-chord implementation was abandoned as fundamentally controller/driver-specific
  and fragile (see `DECISIONS.md` 2026-08-08); the keyboard scheme + Steam Input replaced it. RPCS3 and
  Azahar, previously out for having keyboard/menu-only hotkeys, are now in — that is exactly what a
  keyboard scheme wants.
- A configurable key set (something other than the F-key defaults), and reusing the same writer for
  memory-card/save-path settings and an EmuShelf-owned emulator user directory, are follow-ups the
  model is built to accommodate.

## M40 — Configurable Desktop list-view columns (in progress)

The Desktop list view is a fixed seven-column layout (cover, title/path, console, format,
achievements, textures, status) whose column set is hard-coded twice — once in the header Grid and
once in the row `DataTemplate` — so it cannot show the scraped/metadata state a user actually wants
to scan a large library by. This milestone rebuilds it as an **iTunes-style configurable table**:
the user picks which columns show, reorders and resizes them, and sorts by any of them, with the
choice persisted in portable `Settings/`. New columns surface the metadata EmuShelf already stores
(scraped completeness, artwork/description presence, rating, genre/year/players) plus the cheap
record fields (Last Played, Date Added). Gamepad mode is untouched — this is Desktop-list only.

Guardrails: virtualization, async off-thread cover loading, and the "no bulk work on the UI thread"
rule from CLAUDE.md must survive; the new scraped columns must not reintroduce an N+1 read across the
visible library. A Phase-0 spike rejected `Avalonia.Controls.DataGrid` (it regresses M25 marquee
auto-scroll and fights VM-owned selection/sort) in favour of keeping the `ListBox` and driving its
columns from the view model — same iTunes UX, no regression to shipped interactions. Architecture
decision recorded in `DECISIONS.md`.

### Phase 0 — View-model column model on the existing ListBox (the risk gate) ✅ (2026-08-08)

- [x] Keep the Desktop list as a `ListBox` so marquee/auto-scroll (M25), multi-select, per-row
      context menu, inline title edit, custom VM sort, and the async cover-load hooks
      (`OnGameCoverAttached` / `DataContextChanged`) stay untouched and un-regressed.
- [x] Introduce a view-model column model: `LibraryColumn` descriptors (key, header, `IsVisible`,
      `Width`, `LibrarySortColumn` mapping) in `Columns`/`VisibleColumns`, built from
      `LibraryColumnCatalog`. One flex column (Title) absorbs remaining width from a new
      `ListViewportWidth` (the grid scroller that feeds `LibraryViewportWidth` is collapsed in list mode).
- [x] Drive header + each row from `VisibleColumns` via `ItemsControl`s and a
      `LibraryColumnCellSelector` (a keyed `IDataTemplate`), so hide/reorder/resize are data changes.
      The row's cell panel is stretch+clipped so it can never overflow the list. Header keeps the
      `sort-header` buttons + glyphs.
- [x] Verified build + existing list-view visual snapshots render at parity (default 7-column set
      unchanged); no baseline regen needed. `dotnet build`/`dotnet test` green (525 App tests).

### Phase 1 — The iTunes column chrome ✅ (2026-08-08; drag-reorder deferred)

- [x] Right-click the header for a column checklist that shows/hides columns (Title disabled so the
      table always keeps it). Drag-resize via a right-edge grip on each fixed header cell that sets
      the column width and lets the flex column absorb it (code-behind view wiring).
- [ ] **Drag-reorder deferred.** The data layer + persistence already carry column order
      (`Columns.Move`, restored order); only the drag-vs-sort-click gesture is unbuilt, because it
      needs interactive verification the headless harness can't give.
- [x] Persist per-column visibility, order, and width (plus the existing sort column/direction) to
      portable `Settings/` via `LibraryViewSettings.ListColumns`, restored at startup and resilient
      to an unknown/removed column id. Title is a minimum always-on column.
- [x] Tests: settings round-trip, unknown/missing-key tolerance, Title-cannot-hide, hide→visible
      set, resize→flex+persist.

### Phase 2 — Cheap record-field columns ✅ (2026-08-08)

- [x] Add **Last Played** (`Game.LastPlayedAt`, from M38, "Never" when unplayed) and **Date Added**
      (`Game.DateAdded`) columns — off by default, sortable, no new data plumbing.

### Phase 3 — Bulk metadata projection (data plumbing; parallelizable) ✅ (2026-08-08)

- [x] Added `IGameDetailsStore.GetAllDetailsProjections()` → per-game `GameDetailsProjection`
      (media-kind presence + description, rating, genre/year/players/dev/pub) in three grouped
      queries, never a per-game loop. Infrastructure parity test vs N× `GetDetails`. (Built in
      parallel by a subagent; 899 Infra tests green.)

### Phase 4 — Scraped-metadata columns ✅ (2026-08-08)

- [x] **Metadata completeness** column, on by default: `n/5` (cover, screenshot, fanart, logo,
      description), tooltip lists present/missing, sorts incomplete-first, `—` when never scraped
      (distinct from a partial `n/5`).
- [x] Per-asset presence columns (Has Cover / Screenshot / Fan Art / Logo / Description as ✓/—),
      Rating (0–10), and Genre/Year/Players/Developer/Publisher — all off by default, each sortable.
      Fed by the Phase 3 projection read once per scope build on the load worker; no per-row reads.
- [x] Headless view-model tests for completeness counting (unscraped vs partial), rating/year
      formatting, and dash fallbacks.

### Phase 5 — Cloud save status column (depends on M29)

- [ ] After M29 (battery/memory-card cloud sync) is far enough along, add a **Cloud save** column
      that reads the sync manifest/local state and shows a per-game status (Synced / Local newer /
      Cloud newer / Conflict / Not synced / Not set up), off by default. This needs a game→save-unit
      resolution that does not stat files on the UI thread. The save-state variant is M33 territory
      and stays out until that lands. Deferred behind M29; tracked here so the column set is planned.

## M41 — In-app emulator install & update manager (planned)

One place — a new Settings section — that lists every emulator EmuShelf supports, shows the installed
version against the latest upstream release, and installs or updates it with one action. It serves both
an established setup (update DuckStation to today's build) and a brand-new one (an empty library
downloads its emulators here instead of hunting the web). Reuses the M39 self-updater's
download→verify→stage pattern (`GitHubUpdateService`) and the `RcloneInstaller` "drop an official
third-party binary into the portable folder" pattern; the launcher is untouched because a managed
install just auto-populates `EmulatorConfiguration.ExecutablePath`. Scope: all seven emulators —
DuckStation, PCSX2, RPCS3, PPSSPP, Azahar (GitHub Releases), plus Dolphin and RetroArch (their own
build servers). Install target: a **managed portable `Emulators/<id>/` folder** beside the executable.
Design decisions land in `DECISIONS.md` as they're implemented (not pre-committed — the Dolphin/
RetroArch source formats and macOS `.dmg` handling need spikes that may revise them).

Key simplification the managed model buys us: because EmuShelf is the installer, "installed version"
is read from an **install manifest we write**, not probed out of an arbitrary binary — so the fragile
per-emulator version-probe is avoided for managed installs (it stays an optional nicety for detecting a
user's own install). Guardrails: EmuShelf only ever overwrites/updates a manifest-tracked managed
install; a user-provided path is read-only to us (version check + "open download page" only). Portable
rules from CLAUDE.md hold — everything lives beside the executable and moves with the drive. Downloads
are HTTPS-from-official-source, SHA-256-verified when the release publishes a checksum (reusing the
M39 verify path). On Linux, prefer Flatpak where the emulator ships one, matching the existing
`EmulatorUserDirectories` Flatpak handling, over dropping raw binaries.

New third-party dependency (own `DECISIONS.md` entry per the CLAUDE.md rule when it lands): **SharpCompress**
for `.7z`/`.tar.xz` extraction (RPCS3, PCSX2); `.zip`/`.AppImage` need nothing new; macOS `.dmg` mounts
via `hdiutil`. Downloaded emulators are unsigned and EmuShelf is unsigned, so the `com.apple.quarantine`
xattr is stripped after extract (same as the M39 macOS applier).

### Phase 1 — Core contracts and the install manifest

- [ ] Core: an `EmulatorReleaseSource` describing where an emulator's builds come from (GitHub repo or
      custom URL, per-OS/arch asset pattern, archive kind, optional checksum), hung off each
      `EmulatorDefinition` in Integrations. An `IEmulatorInstallService`
      (`GetStatusAsync`/`InstallAsync`/`UpdateAsync`) with a status model (NotInstalled / Managed(version)
      / UserProvided(path) / UpdateAvailable(current, latest) / CheckFailed(reason)) mirroring `Updates/`.
- [ ] Install manifest: a portable `Settings/` JSON recording per managed install
      `{emulatorId, installedVersion, installedAt, executableRelativePath, sourceTag}`, with a store
      interface + implementation. `IAppPaths.EmulatorsDirectory` added for the managed root.

### Phase 2 — Infrastructure: the five GitHub-Releases emulators

- [ ] `GitHubEmulatorInstaller` reusing `GitHubReleaseParser` and the M39 download/verify code, plus a
      portable-drop/chmod/quarantine-strip step modeled on `RcloneInstaller`. Extractors for
      `.zip` (built-in), `.AppImage` (chmod), and `.7z`/`.tar.xz` (SharpCompress). Writes the manifest
      and returns the resolved executable path so the caller can wire the config.
- [ ] Per-emulator sources: DuckStation (stenzek/duckstation), PCSX2 (PCSX2/pcsx2), RPCS3
      (rpcs3/rpcs3-binaries-{win,linux,mac}), PPSSPP (hrydgard/ppsspp), Azahar (azahar-emu/azahar) —
      each with verified per-OS/arch asset patterns. Managed-only overwrite guard enforced here.

### Phase 3 — Custom fetchers (the highest-risk items; spike each first)

- [ ] Dolphin: resolve latest build + per-platform asset from dolphin-emu.org, mount/copy the macOS
      `.dmg` via `hdiutil`. Spike the listing format before committing the parser.
- [ ] RetroArch: resolve latest stable + asset from buildbot.libretro.com's directory layout; prefer
      Flatpak on Linux. Spike the stable-version discovery before committing.

### Phase 4 — App UI

- [ ] A new Settings section (sibling to Emulators) with one row per emulator: **Not installed / vX
      installed / vY available**, an Install/Update button, and download progress + busy state modeled
      on the rclone-download and self-update flows. Doubles as the empty-library onboarding step.
- [ ] On successful install, auto-select the managed install for every system the emulator supports
      that has no executable configured yet; never override an existing user choice.

### Phase 5 — Tests

- [ ] Release parsing + asset selection per source and per OS/arch, manifest round-trip,
      managed-vs-user-install overwrite guard, extractor unit tests (zip/7z/tar.xz), and headless
      view-model tests for the status/progress states. Full `dotnet build`/`dotnet test` green on macOS.

## M42 — Physical-media shelf scene (selected next; planned)

Turn the running single-hero prototype into the intended minimal physical-library mode: correctly
scaled media share one horizontal 3D scene, move continuously as focus changes, carry realistic
materials/art and contact shadows, and perform a safe insertion transition before launch. The detailed
design, asset gates and failure semantics live in `docs/couch-physical-media-shelf.md`; implementation
decisions land in `DECISIONS.md` with each phase.

### Phase 1 — Metric profiles and the smallest multi-item scene

- [x] Add a `PhysicalMediaProfile` contract with real dimensions, canonical orientation, artwork slots,
      material variant and insertion-animation id. Use one camera and one shelf baseline; never auto-fit
      each medium independently. Keep case = large, SNES = medium, GBA = small by physical proportion.
      — Landed 2026-08-13; PS3 keeps its shorter metric profile while sharing temporary case geometry.
- [x] Replace the translated 2D strip plus single hero with one bounded `MediaShelf3DControl` rendering
      the focused item and two or three neighbours on each side. Unsupported systems render a thin cover
      card in the same scene. Preserve the current flat shelf as the no-GL fallback. — Landed 2026-08-13;
      GPU covers/subscriptions are bounded to the seven visible items.
- [x] Add a pure continuous shelf-motion model (target index, position, velocity, elapsed time and
      reduced-motion mode). Held input updates the target without stacking animations; rendering stops
      once motion and user rotation settle. — Landed 2026-08-13 with an exact critically damped step,
      far-jump snapping, reduced-motion snap policy and an idle-stopping UI timer.

Phase 1 code acceptance: full solution build and tests green (1,666 passed, 1 skipped); shared-camera
headless acceptance render generated. Windows composition, movement feel and controller framing remain
the hands-on review gate before Phase 2 begins.

### Phase 2 — One gold-standard SNES vertical slice

- [x] Select a redistribution-safe SNES shell (prefer CC0/CC BY downloadable GLB) and retain source URL,
      author, exact license and downloaded archive. Reject view-only, Editorial, NC or ND assets and any
      model whose baked game art cannot be removed or completely replaced. — SomeKevin's CC BY 4.0
      PAL/Super Famicom shell integrated 2026-08-13 as a deterministic cleaned runtime derivative;
      the supplied BY-NC scan and paid Store model were rejected.
- [ ] Pass the asset gate: measured proportions, correct pivot/orientation, closed visible silhouette,
      bevelled edges, stable normals, usable UVs/tangents, separate body/label materials, and no borrowed
      packaging visible from any review angle. Record attribution and modifications in notices. — Source,
      license, 129×87×20mm PAL profile, 180° orientation, PBR maps, placeholder removal, topology repair
      and attribution landed 2026-08-13. Hardware review rejected a separate label plane because it
      visibly floated; the replacement body-attached decal has aspect-correct rounded corners,
      derivative-antialiased edges and its own paper roughness/flat shading without a geometry gap.
      Remaining boundary/non-manifold audit and real-Windows 1080p close-up review keep this gate open.
- [ ] Add the production material/lighting path: base colour, normal, metallic/roughness and AO maps,
      mipmapped/anisotropic label art, direct key plus soft receiving-plane shadow, while retaining the
      existing IBL reflections. Verify front/back/side/top close-ups at 1080p on real Windows hardware.
      — Direct GGX key lighting, analytic softbox-style contact shadows on a transparent receiving plane,
      geometry-cast key self-shadows with 3×3 PCF, per-shell dielectric/roughness calibration, trilinear
      mipmaps and driver-capped 8× anisotropic filtering landed 2026-08-13 and passed the full-HD
      headless angle matrix. A couch-distance contrast pass now gives the SNES a 70% studio fill,
      lets true depth visibility suppress 58% of that fill, and applies thresholded normal-map cavity
      only to strong relief while excluding the printed label. The studio key now rakes from high-left
      rather than following the camera axis, and isolated 1024px per-item maps give readable neutral-pose
      self-shadows without one shelf item shadowing another. The SNES asset contains no authored occlusion
      texture, so AO-map ingestion and the real-Windows 1080p review keep this gate open.

### Phase 3 — Correct scraped media and remaining launch set

- [ ] Add lazy ScreenScraper support/box media kinds: cartridge `support-2D`/`support-texture`; case
      front/back/spine/wrap. An authored empty shell with accent label remains valid when artwork is absent.
- [ ] Bring GBA and the case family through the same asset gate. Use separate material/dimension variants
      where the real packaging differs rather than recolouring one inaccurate universal case.
- [ ] Bound decoded/GPU artwork with a focused-neighbour LRU and shared shell resources. Hold 60 fps at
      1280x800 and 1920x1080 with a 500-game library on the Windows integrated-GPU acceptance machine.
      — Shared baseline optimization landed 2026-08-13: 21-entry cover-texture LRU, adaptive scene
      resolution capped at 2560×1440, isolated PCF self-shadows for every submitted shelf item, cached GL
      filtering capabilities, one neutral studio bake with navigation-time shader tinting, async cached
      model decode, allocation-free panel bindings and resize-bucketed render targets that shrink after
      sustained material under-use. The GL scene exists only while shelf mode is visible; an explicit
      readiness watchdog preserves the flat fallback even when Avalonia fails before renderer init. The
      control reuses its seven-item render buffers, cleans up observers on detach and changes cover
      subscriptions only when the rounded visible range changes. Shared geometry now consumes the
      profile's material variant for platform-specific body tint, roughness and reflectance.
      Real integrated-GPU frame pacing remains the acceptance gate.

### Phase 4 — Launch/return choreography and rollout

- [ ] Add a view-model-owned transition: `Idle -> Preflight -> Lift -> Spin/Align -> Insert -> Committed`,
      with cancel, start-failure return, emulator-exit restoration and repeated-launch suppression. The
      animation exposes transforms but never starts a process itself; the existing launcher commits it.
- [ ] Give cartridges and disc cases appropriate downward handoff paths. Reduced motion uses a brief
      translate/fade; optional sounds must be licensed, independently mutable and independently muted.
- [ ] Keep the mode experimental until SNES, GBA and supported case variants pass real-hardware visual,
      performance, no-cover, no-GL, cancellation and every launch-failure acceptance path.
