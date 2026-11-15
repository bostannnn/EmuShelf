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
- [x] Add standalone melonDS (release and nightly channels) as selectable DS emulators alongside
      RetroArch — on desktop and on Android — with battery saves keyed per game so one cloud entry
      serves melonDS's `.sav` and a libretro core's `.srm` (DECISIONS 2026-09-01).
- [ ] On a real machine with melonDS installed, launch a `.nds` ROM through each channel, then sync
      a save round-trip melonDS ↔ RetroArch and confirm the migrated cloud key carries the existing
      DS saves over.
- [ ] On the Thor, pick melonDS (and the nightly) in the couch Emulators section, launch a DS game
      through each, and sync a save against the desktop melonDS install.

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

### Phase 4 — Portable export (2026-08-18)

- [x] Export saves to a single portable `.zip` for use on a platform EmuShelf isn't on. Two scopes —
      **This device** and **This device + cloud** (a union; device copy wins on conflict, cloud adds
      only what is missing locally). Save states are always included. Read-only over save/game data.
      `SaveExportService` (Infrastructure, unit-tested with the in-memory sync fakes) +
      `ZipSaveExportSink` (temp-then-move) + `CloudSaveSyncCoordinator.ExportSavesAsync` sharing the
      sync gate; two buttons under Settings → Saves. See DECISIONS 2026-08-18. Real-hardware check of
      the produced archive on a second machine remains the manual gate.

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
      Hands-on SNES composition review added a restrained 1.10 presentation correction, raised the
      common shelf/floor by 0.08 world units, and gave cartridges a small profile-owned clearance so
      the row sits near the content area's optical centre without changing cross-platform proportions.
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

- [x] Shelf artwork performance follow-up (2026-09-06): pin visible GPU textures, prioritize fronts
      around selection, and prepare owned RGBA buffers with two background workers. Preserve the previous
      same-game face during replacement. Thor GameCube sweeps verified 2026-09-06; see
      `docs/performance/shelf-2026-09-06/README.md` for measurements and comparison limits. GPU timing,
      CRT/rotation coverage and long-session memory validation remain outstanding.

### Phase 2 — One gold-standard SNES vertical slice

- [x] Select a redistribution-safe SNES shell (prefer CC0/CC BY downloadable GLB) and retain source URL,
      author, exact license and downloaded archive. Reject view-only, Editorial, NC or ND assets and any
      model whose baked game art cannot be removed or completely replaced. — SomeKevin's CC BY 4.0
      PAL/Super Famicom shell integrated 2026-08-13 as a deterministic cleaned runtime derivative;
      the supplied BY-NC scan and paid Store model were rejected.
- [ ] Pass the asset gate: measured proportions, correct pivot/orientation, closed visible silhouette,
      bevelled edges, stable normals, usable UVs/tangents, separate body/label materials, and no borrowed
      packaging visible from any review angle. Record attribution and modifications in notices. — Source,
      license, 129×77.5×20mm PAL profile, 180° orientation, PBR maps, placeholder removal, topology repair
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
      self-shadows without one shelf item shadowing another. AO-map ingestion landed 2026-08-15 —
      `occlusionTexture` was being dropped by the loader, and the Game Boy and Mega Drive shells both
      ship one — but the SNES asset itself carries no authored occlusion texture, so that shell still
      infers it from normal-map slope, and the real-Windows 1080p review keeps this gate open.

### Phase 3 — Correct scraped media and remaining launch set

- [x] Project selected ScreenScraper `support-texture` into authored cartridge label slots, decoded
      off the UI thread for the visible shelf window with a bounded LRU. Missing/invalid art keeps the
      authored accent label; `support-2D` is a complete flat cartridge render and box art is never used.
- [ ] Map case front/back/spine/wrap media onto authored case material slots.
- [ ] Bring GBA and the case family through the same asset gate. Use separate material/dimension variants
      where the real packaging differs rather than recolouring one inaccurate universal case.
      — PS3 left the universal case 2026-08-15: Diablo's CC BY Blu-ray shell is authored at the real
      135x171.5x13mm, so PS3 no longer stands at a DVD case's height, and its sleeve prints on the
      film's own material slot rather than a rectangle over the shell. 3DS left the cover card
      2026-08-15 on SGLilac's CC BY scan of a real card, authored as its own geometry rather than a
      recoloured DS card because the anti-insertion tab is moulded rather than measured; every system
      in KnownSystems now has authored media. PSP is the last profile still borrowing the DVD case,
      knowingly squeezed to a UMD case's shape, and a UMD case is what the case family still owes
      this gate.
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
      profile's material variant for platform-specific body tint, roughness and reflectance. Physical
      support artwork now uses a focus-prioritized queue with no more than two concurrent decodes;
      queued/off-screen requests are discarded before they can turn rapid traversal into a library-wide
      I/O burst or displace visible textures from the LRU.
      Real integrated-GPU frame pacing remains the acceptance gate.

### Phase 4 — Launch/return choreography and rollout

- [ ] Add a view-model-owned transition: `Idle -> Preflight -> Lift -> Spin/Align -> Insert -> Committed`,
      with cancel, start-failure return, emulator-exit restoration and repeated-launch suppression. The
      animation exposes transforms but never starts a process itself; the existing launcher commits it.
      — Implemented as a pure elapsed-time model on 2026-08-13: after launch preflight and pre-launch
      save sync, the focused medium lifts/approaches, turns exactly three times, aligns and holds half
      inserted before releasing process start. Failure, cancellation and tracked exit reverse from the
      current pose. Executable verification remains pending because the local execution allowance ended.
- [ ] Give cartridges and disc cases appropriate downward handoff paths. Reduced motion uses a brief
      translate/fade; optional sounds must be licensed, independently mutable and independently muted.
      — Split into two choreographies on 2026-08-15, selected by the profile's `InsertionAnimationId`
      (see DECISIONS). A cartridge keeps the turn-and-insert path; a case squares up, slides its disc
      out from behind its own edge and settles back onto the shelf while the disc spins up and drops
      away flat. Both run to the same 1920ms so process start is unaffected, reduced motion skips the
      opening for both, and closing the game plays the whole sequence backwards.
      An exposed reduced-motion setting and optional sound remain open, as does moving PS1, Dreamcast
      and PSP onto the disc path once they stop rendering as flat cover cards.
- [ ] Keep the mode experimental until SNES, GBA and supported case variants pass real-hardware visual,
      performance, no-cover, no-GL, cancellation and every launch-failure acceptance path.

## M43 — Playtime tracking ✅ (2026-08-16)

The deferred follow-up to M38's single last-played column: how long each game has been played and how
many times it was launched. Two aggregate totals on the game row, not a session-history table (see
`DECISIONS.md` 2026-08-16). Surfaced as opt-in list columns and a gamepad spotlight caption.

- [x] Persist two totals: schema v18 adds `Games.PlaytimeSeconds` and `Games.PlayCount`
      (`INTEGER NOT NULL DEFAULT 0`, healed by `AddGameColumnIfMissing`), surfaced as
      `Game.Playtime` (`TimeSpan`) and `Game.PlayCount` (`int`). `IGameLibrary.SetLastPlayed` became
      `RecordLaunchStarted`, stamping last-played and incrementing the count in one atomic update; a
      new `AddPlaytime` accrues a completed session's duration (ignoring a zero/negative span).
- [x] Count at start, time at exit: `RecordLaunchStarted` fires in the launch `beforeStart` callback
      (so a killed session still counts as one play), while `EmulatorLaunchService` times a `Stopwatch`
      around the tracked process and returns it on `GameLaunchResult.PlayDuration` (present on zero and
      non-zero exit, null when the process never starts). `MainViewModel` persists it via `AddPlaytime`
      off the UI thread, guarded so a write failure can't turn a completed launch into a reported error.
- [x] Surface it: two opt-in Desktop list columns (`Play Time` "Nh Nm"/"< 1m"/"—", `Plays`), wired
      through `LibraryColumnKey`/`LibrarySortColumn`/`LibraryColumnCatalog`, keyed cell templates, and
      `SortGames`; plus a gamepad spotlight caption ("12h 34m • 5 plays", hidden when unplayed) under
      the launch-source line.
- [x] Tests: `RecordLaunchStarted`/`AddPlaytime` round-trip (accrual, count-every-launch, zero
      defaults, ignore non-positive), schema-v18 migration version + column defaults, launch-service
      `PlayDuration` coverage (tracked runtime, non-zero exit, none when unstarted), and
      `GameViewModel` formatting (compact hours/minutes, dash-when-unplayed, gamepad summary).
      `dotnet build`/`dotnet test` green on macOS.
- [ ] On real Windows, launch and exit a game and confirm the play time and count increment, persist
      across restart, and that no game file or emulator data was modified.

## M44 — Android port (AYN Thor) 🚧

The Gamepad shell on an Android handheld, firing intents at Android emulator apps. Master plan and
per-phase detail live in [docs/android-port-plan.md](docs/android-port-plan.md); non-obvious choices
are in `DECISIONS.md`. Shipped as an experimental sideload APK, not a fourth supported release target.
The head (`src/EmuShelf.App.Android`) is deliberately **outside `EmuShelf.slnx`** so the workload never
breaks the whole-solution macOS build/test loop.

- [x] **0a — AVD spike** (2026-08-15). Toolchain from zero, five emulators installed, the file-handoff
      matrix measured on an arm64 AVD. Settled the per-emulator handoff design and passed the multi-disc
      kill criterion via RetroArch/SwanStation. See the plan's "Milestone 0a — results".
- [x] **A0 — split the App project** (2026-08-17). `EmuShelf.App` split into a shared `EmuShelf.UI`
      library + a thin desktop head, with a lifetime-agnostic composition root behind
      `App.DesktopShellFactory`/`IPlatformShell`. Full desktop Release suite green.
- [x] **A1 — walking skeleton** ✅ (2026-08-18). Verified on the AVD and then **on the Thor**: the real
      head boots the shared composition root, Avalonia renders, the GLES 3D shelf gets a real OpenGL ES
      3.0 context (asserted via `InitializationSucceeded`, EGL pinned with Software dropped), and SQLite
      creates `Data/library.db` in app-private storage. The head hosts the real gamepad shell (extracted
      `GamepadShellView`), and the gamepad-native import, escape hatches, ladder audit, and an on-device
      couch-input slice (Menu/D-pad/A-B) are all done and verified — the keyboard-free import runs end to
      end, driven entirely by the gamepad. Desktop suite green (1128 + 895). A1's done-criterion
      ("imports a folder without a keyboard, shows the library") is **met**. **The one open item — the CRT
      tube rendering at 1×1 px on the AVD's *software* GL — is resolved on real hardware: on the Thor's
      Adreno GL the CRT tube renders full-screen (the phosphor/scanline sheen paints across 1920×1080),
      so it was a software-GL artifact, not a shell defect.** Installed to the Thor via the Debug
      `-t:Install` loop; the gamepad shell renders correctly against an empty library.
  - [x] Single-view seam: `App.SingleViewShellFactory` + `ISingleViewApplicationLifetime` branch;
        `AppBootstrapper` base-directory injection; Android shell services (`AndroidInterfaceModeService`
        Gamepad-locked, frontend controller, lifetime service, stub `SingleViewDialogService`).
  - [x] `net10.0-android36.0` head boots on device, EGL-pinned, `Avalonia.Desktop` kept out.
  - [x] Extract the gamepad tree from the desktop `MainWindow.axaml` into a shared `EmuShelf.UI`
        `GamepadShellView` (couch UI + CRT tube + ~40 gamepad code-behind methods; the A0-deferred item).
        Both heads host it: desktop `MainWindow` and the Android `MainView`. Done in gated stages —
        shared styles to app scope, shared cover-interaction helper, then the view+code-behind
        partition. Desktop suite green (1128 + 889); the real gamepad shell renders on the AVD.
  - [x] Close the gamepad escape hatches via `IInterfaceModeService.SupportsDesktopMode` (desktop true,
        Android false): the system-menu "Switch to Desktop" and the cover handoff disappear/reword, and
        the empty-library copy in `GamepadShellView.axaml` points at Menu → Add games instead of a
        Desktop mode that does not exist. Desktop wording (and snapshots) unchanged.
  - [x] `OperatingSystem.Is*` ladder audit (50 sites) beyond the base-directory branch: fixed the one
        live crash risk (`FileRevealService`'s `xdg-open` fall-through vs Android W^X); the rest are
        correct-as-Linux, degrade safely, already Android-aware, or dormant until a later milestone.
        See DECISIONS 2026-08-18.
  - [x] Gamepad-native library import: folder pick via `IDialogService` (Android head drives the SAF
        picker through `TopLevel.StorageProvider`; translates the `externalstorage` tree URI to a real
        path with all-files access) → a controller-native `ImportSystem` overlay chooser → the existing
        scan. "Add games" shows in the couch menu only where Desktop mode is absent. **Verified on the
        AVD, driven entirely by the gamepad**: Start → Add games → SAF pick → PlayStation → Alpha + Beta
        imported. See DECISIONS 2026-08-18.
  - [x] On-device couch input (a Milestone C slice, pulled forward): `MainActivity.DispatchKeyEvent`
        maps Android gamepad keycodes (which never reach Avalonia's `KeyDown`) to `GamepadAction` and
        routes them to the shared `DispatchGamepadAction`; the desktop key contract is now a shared
        `GamepadKeyMap`. Menu/D-pad/A-B/L1-R1 work on device.
- [x] **Shelf backdrop + chrome capture fixed on the Thor** (2026-08-18). Two HiDPI/single-view bugs the
      device surfaced after A1: the couch shelf backdrop resolved a hardcoded dark-grey fallback because
      the theme brush `TryFindResource` keys off a not-yet-settled `ActualThemeVariant` on the single-view
      tree, and the couch chrome was captured in dip (833 px) then upscaled onto the 1920 px tube (blurred
      text). Fixed in shared `EmuShelf.UI`; desktop App Release suite green (895/895). See DECISIONS 2026-08-18.
- [x] **A2 — couch responsiveness on dense/short panels** (Thor, 2026-08-18 → 2026-08-20, validated on
      hardware). The couch shell is tuned for the Steam Deck's 1280×800; the Thor is 1920×1080 physical but
      only ~833×468 **dip** at its ~2.31× density, so the whole couch UI was oversized ("everything is
      huge"). **Primary fix:** the Android head overrides the activity resource density
      (`MainActivity.AttachBaseContext`, `CouchTargetDipWidth = 1280`) to give the shell a Deck-class
      ~1280×720 dip canvas, so everything scales down to fit — Android-only, no shared-UI/desktop/snapshot
      impact. **Complementary fix:** the system menu's `Auto` View mode / Sort picker used to starve the
      `*` option row to ~0px (Settings/Quit clipped); the picker now shares the option list's one
      `GamepadOverlayOptionsScroller` scroll region. Verified on the Thor (menu, Settings, quit confirm all
      Deck-proportioned and reachable) and guarded headlessly
      (`GamepadSystemMenuOnShortPanel_ScrollsToReachSettings`); desktop App Release suite green (898/898).
      See DECISIONS 2026-08-20. **Remaining confidence check** (not blocking — the density change is global
      so these scale too): eyeball the *populated* views — grid / list / 3D shelf covers and the
      achievements / scraper / hotkeys overlays — at the new density with real games. Gated on staging
      ROMs on the Thor (the SAF all-files grant was wiped by a `pm clear` during testing).
- [x] **Vertical gamepad menus scroll to follow the selector** (Thor, 2026-08-18 → 2026-08-20). Solved for
      the system menu as part of A2: the picker rows now join the option list's scroll-follow, and the
      shared merge means moving the ring down brings each entry into view (`RevealGamepadOverlayFocus`).
      The Settings/achievements lists already had dedicated scroll-follow. The suspected Android
      focus-vs-selection split (`DispatchKeyEvent`→`GamepadAction` without real Avalonia focus) was not the
      cause here — `BringIntoView` fires off the view-model selection, verified headlessly on desktop.
  - [x] **0b — on-device handoff matrix** (2026-08-20). Every shipped system's handoff measured on the
        Thor and captured as data: DuckStation (PS1) boots via `EmulationActivity`+`bootPath`, ARMSX2 (PS2)
        via `VIEW`+data URI (both booted real games); Dolphin/PPSSPP/Azahar/WatermelonDS/RetroArch shapes
        recovered from Cocoon's live launch log + NeoStation's DB. Strategy 4 (each emulator holds its own
        SAF tree grant) confirmed live. `Android/data` saves reachable **without root** via CX File Manager;
        per-emulator save mapping recorded. See the plan's "Milestone 0b" / E save table.
  - [x] **D — storage & permissions (done for Thor)** (2026-08-21, verified on Thor). `AndroidExternalStorageUri`
        (shared, tested) owns SAF tree/document URI ↔ `/storage` translation; game paths stored absolute on
        Android (`IAppPaths.UsesPortableStorage`); `allowBackup=false`. The **all-files runtime grant UX** is
        provided by D2's first-run onboarding + `IStoragePermissionService` (below) — once onboarding secures
        all-files, EmuShelf reads the SD library by real path. Verified here: **`FolderScanner`/availability
        over the real SD library** (41-game import from `/storage/AE6A-1092/roms/psx`, rendered available).
        The couch import chooser's Thor-only density-collapse found while doing this is fixed
        (`MainViewModel.GamepadOverlayOptionsMinHeight`, Android-only 240-dip floor; 0 on desktop, snapshots
        unchanged; `GamepadImportChooserLayoutTests` guards it). **Deferred (owner call, not Thor blockers):**
        SAF-backed reader fallback (device without all-files) and the per-API-level AVD matrix
        (verification-only). See DECISIONS 2026-08-21.
  - [x] **B — launching** ✅ (done 2026-08-22, verified on Thor). Per-emulator intent data + pure
        `AndroidIntentFactory`/`AndroidLaunchResolver`; `<queries>` manifest; `AndroidEmulatorLaunchService`
        wired via `IPlatformShell.LaunchService`. A couch button launches a real game; exit signal
        (`OnTopResumedActivityChanged`) + durable deferred post-play completion survive process death.
        Controller Settings now presents one flat emulator picker per system: every standalone Android
        app is an entry and every compatible RetroArch core is an equal `RetroArch · core` entry without
        trying to read RetroArch's app-private directory. The saved short emulator id plus optional exact
        `LIBRETRO` path round-trips through the existing configuration schema; Android-only ARMSX2 and
        WatermelonDS are selectable instead of being unreachable launch defaults. Desktop Settings uses
        the same picker model, expanding RetroArch into disk-discovered core entries after its executable
        or Flatpak target is configured while retaining each emulator's own executable/arguments draft.
        **Nested multi-disc launch fixed (2026-08-22):** the launch service now scopes the SAF URI's tree
        to the game's remembered import folder (`AndroidLibraryGrantRoot`) instead of the game's own
        sub-folder, so a per-game `.m3u` (MGS, Xenogears, Twin Snakes, Shadow Hearts Covenant) matches the
        emulator's own `roms/<system>` prefix grant. Reproduced live on the Thor (sub-folder tree →
        `SecurityException`; `roms/psx` tree → MGS boots and reads Disc 1), 7 selector tests + the existing
        on-device resolver test green. See DECISIONS 2026-08-22. Remaining: dependency-resolver promotion
        (desktop/Flatpak only) and a grant-root verification step for the rarer import≠grant-folder case.
  - [ ] **B1 — unified-picker device acceptance.** On the Thor (or an arm64 AVD), confirm DS lists
        WatermelonDS plus melonDS DS / melonDS / DeSmuME as four flat choices, restart to prove the chosen
        `(EmulatorId, CorePath?)` persists, and launch one standalone app plus each RetroArch core. The
        shared Debug suite is green; this checkbox is deliberately hardware-only.
  - [~] **E-android — cloud sync (started, 2026-08-20)**. The auto-sync path was already wired; this adds
        the actual save data. **Capability finding that reshaped the milestone:** a runtime probe from the
        app's own process proved all-files access reads *and writes* `Android/data/<pkg>` on the Thor
        (group-readable files; owner-only `-rw-------` files stay unreadable and are skipped gracefully) —
        so the SAF-backed `ILocalSaveEndpoint` rewrite (the plan's stated long pole) is **not needed** for
        the Thor; the existing `FileSystemLocalSaveEndpoint` serves every emulator over real paths.
        **DuckStation (PS1) landed + verified on device**: `DuckStationAndroidSaveLocationProvider` (pure,
        6 tests) reads the fixed `…/files/memcards` and emits the same `duckstation/per-game/{title|serial}`
        unit ids as desktop; `AppBootstrapper` synthesises the fixed-location install from the package name;
        `SaveProviderRegistry` builds it under `IsAndroid()`. A device-only export enumerated 10 real
        memcards. **Dolphin (GameCube + Wii) fixed-root wiring also landed**: both systems resolve the
        package-derived external `files/` user root, then reuse `DolphinSaveLocationProvider` through its
        explicit-user-directory seam, preserving desktop-compatible GCI/Wii unit ids without duplicating
        parsing or restore logic. Android-layout fixtures cover default Card A, configured Card B and Wii
        title data; package-root resolver checks cover both systems. Full Release suite: 2,093 tests green.
        Thor export/restore is still the hardware gate. Provider split: folder-configurable emulators
        (PPSSPP/Azahar/WatermelonDS/RetroArch) reuse the desktop providers via a one-time manual folder pick
        (per-system override). See DECISIONS 2026-08-20. Remaining: that folder-picker/override plumbing,
        then the transport half (Android OAuth client + custom-scheme redirect, Keystore token store,
        gamepad Saves rebuild). PS2 folder-card→`.ps2` / cross-emulator save sync is a separate deferred
        feature.
  - [x] **F — packaging & release** ✅ (engineering done 2026-08-22). `package-android` in
        `.github/workflows/build.yml` builds the out-of-solution head (JDK 21 + SDK 36 + android workload) as
        a PR build floor and **attaches the APK to tagged releases** (in the `release` job's `needs`, but its
        `if` only requires the desktop packages, so a broken APK can never block a desktop release; the stale
        "not in needs" comment on the `package-android` job is fixed). **Release-signing is live** — all four
        `ANDROID_KEYSTORE_*`/`ANDROID_KEY_*` secrets are configured (2026-08-20, verified via `gh secret
        list`), so tagged builds are release-signed. The Android OAuth client-id accessor
        (`GoogleOAuthAndroidClientId`) is present, and user install/sideload instructions are written
        (`docs/android-install.md`). **Only non-engineering remainder:** register a Google
        developer-verification identity — region/time-gated (enforcement starts 30 Sep 2026), not blocking.
  - [ ] **C — controller + IME** (native analog-stick reading — the sticks do nothing today — + IME),
        **E-android** save providers/transport, **E-desktop** (one real Google sign-in). Land these to a
        working core, then:
  - [x] **SS — second screen (Thor dual-screen companion)** ✅ (implemented and verified on Thor
        2026-08-22). Uses the Thor's bottom `Presentation` panel (`displayId=4`, 1240×1080,
        `FLAG_PRESENTATION`) as a companion surface while EmuShelf is the active frontend — an app dock,
        an all-apps drawer, a RetroAchievements panel, and a dimmed game-logo idle while a game plays on
        the main screen. Native C# Android Views inside an `Android.App.Presentation`, reading the shared
        Core services in-process (no second Avalonia surface, no new RA path). Owner calls: active
        **whenever EmuShelf is open**; dock/drawer-launched apps open **on Screen-2**; achievements show
        the **running-or-selected game, cache-first, pull only on the icon press**. Steps: **SS0** —
        gating spike (does the Presentation survive an emulator taking the main screen; must AYN's
        `com.odin.dualscreen.assistant` be dismissed to own Screen-2; keep-alive mechanism) → **SS1**
        `SecondScreenController` host → **SS2** bottom bar (drawer/achievements icons + 5-slot dock) →
        **SS3** app drawer (manifest `<intent>` LAUNCHER query, launch on Screen-2) → **SS4** dock
        pinning (portable `Settings/second-screen-dock.json`, Core-tested) → **SS5** achievements panel
        (reuse `IRetroAchievementsDetailsService` + 5-min staleness gate) → **SS6** dim+logo idle
        (touch-to-wake). The SS0 spike confirmed the Presentation coexists with AYN's assistant without
        disabling it and survives a real ARMSX2 handoff; a `specialUse` foreground service runs only while
        a game owns the main panel. Release-device checks covered the virtualized drawer, Screen-2 Clock
        launch, persisted dock, game artwork, overlay Close→game-idle restoration, return→browse, and
        service teardown. Full detail in the plan's "Milestone SS".
  - [ ] **S — stabilization passes (features first, then iterate until solid)**. Owner strategy
        (2026-08-20): the lettered milestones build features, each verified narrowly; they do not produce a
        polished build. After the core imports/launches/returns/syncs end to end, switch to **repeated
        on-device bug/polish rounds** with a full library, and keep repeating until it feels finished — a
        first-class phase, not end-of-line cleanup. Seeded backlog: analog sticks unread on Android (blocks
        rotating the 3D cover, and every stick interaction — really Milestone C), 3D shelf covers resize
        while scrolling (A2 density × shelf geometry/virtualization), plus "many others" to catalogue in the
        first pass. See the plan's "Milestone S — Stabilization passes".
    - [x] **S1 — Android auto-update + grid/settings polish** (2026-08-22, four Thor-pass fixes;
          on-device verification pending device return). (1) **In-app auto-update now works on Android.**
          CI already publishes a signed `EmuShelf-android-arm64.apk` + `.sha256`, so the shared
          check/download/checksum-verify path just needed the Android asset name (`UpdatePlatform`); a new
          `AndroidUpdateApplier` (injected via `App.UpdateApplierFactoryOverride`) hands the verified APK
          to the system package installer through a `FileProvider` content URI. It is not silent —
          Android has no in-place file-swap for an installed app — and the update only installs when the
          new APK is signed with the same key as the running build (the CI release keystore). Adds
          `REQUEST_INSTALL_PACKAGES` + the provider to the manifest. (2) **Hotkeys section hidden on
          Android.** The feature writes a *keyboard* scheme into desktop emulator configs for Steam Input;
          neither exists on Android, so `MainViewModel.CreateHotkeySettingsContext` returns null there,
          dropping the section and the gamepad hotkey-editor overlay. (3) **Removed the redundant
          "ScreenScraper" header** in the gamepad Artwork & Metadata section — it stacked directly above
          the "Sign in to ScreenScraper" sub-header and only ate couch vertical space. (4) **Grid scroll
          no longer fans/chops on Android:** the per-tile 20 px blurred `BoxShadow` (recomposited every
          frame for ~40 tiles — the dominant grid cost in the fan-on-scroll investigation) is dropped via
          a `reduced-effects` class gated on `IsReducedEffectsPlatform`, which also collapses one overdraw
          layer. Desktop keeps the depth. See DECISIONS 2026-08-22.
    - [x] **S2 — couch grid vertical scroll glides instead of jumping** (2026-08-23, measured on the Thor).
          The couch grid "jumped" one whole row per d-pad step / tap-to-focus with no glide, and felt
          identical on `-c Release` — so it was never throughput. Cause: the vertical follow-scroll ease
          (`GamepadShellView.RevealFocusedGame`) self-reposted at `DispatcherPriority.Render`; on Android's
          compositor consecutive Render jobs drain within one paint, so every ease step ran before a single
          frame showed. Fixed by driving the glide from `TopLevel.RequestAnimationFrame` (one step per
          rendered frame, frame-rate-independent ease) — the continuous-offset model NeoStation (Flutter)
          and Cocoon use. Verified on-device via a temporary glide-cadence trace: glide frames are now ~13 ms
          (~60 fps). See DECISIONS 2026-08-23. **Remaining (deferred, own item below):** one heavy render
          frame per row scrolled — the `VirtualizingStackPanel` realizing a row of 5 deep tile trees costs
          ~120 ms (Debug) / ~66 ms (Release) as a single mid-glide spike. Still felt as a slight catch.
    - [ ] **S3 — cut couch grid row-realization cost** (backlog; the remaining scroll jank after S2). The
          per-row realize/paint of 5 heavy `GamepadGameButtonTheme` tiles is the last scroll hitch (measured
          ~66 ms/row in Release). Levers, cheapest first: (a) **AOT** (`RunAOTCompilation` +
          profiled AOT) — zero visual change, needs the Android NDK `26.3.11579264` (not yet installed on the
          build Mac); (b) **lighten the tile visual tree / collapse overdraw** — bigger win but a look
          tradeoff (get sign-off); (c) confirm whether the nested per-row `ItemsControl` re-instantiates its
          5 tiles on recycle rather than rebinding, and if so give the grid true tile recycling. Measure each
          with the S2 glide-cadence trace approach (`adb logcat -s EmuShelfPerf`).
    - Landed 2026-08-22: Android now hosts the UI via `IActivityApplicationLifetime.MainViewFactory` (fresh
      view per activity) instead of `ISingleViewApplicationLifetime.MainView`, clearing Avalonia's
      "MainView is not fully supported on Android" warning (was 33×/day on the Thor). Verified on-device.
      NOT fixed by this: the `OpenGlException: Window 0 is invalid` render-loop errors — a separate
      surface-teardown race on emulator-launch backgrounding, still open (see DECISIONS 2026-08-22).
    - [x] **S4 — close the emulator when returning to EmuShelf** (2026-08-25; reworked and **verified on the
          Thor 2026-08-26**). A launched emulator is another app; nothing exited it on return, so it lingered
          in the background draining the battery. The first cut used `ActivityManager.killBackgroundProcesses`,
          which **does not work**: it is deprecated and skips any process holding a foreground service — which
          every emulator does while emulating — so the emulator survived. Replaced with **Shizuku**: its helper
          runs at the adb-shell UID (holds `FORCE_STOP_PACKAGES`), so `am force-stop <package>` through Shizuku
          is a genuine force stop, rootless. `AndroidEmulatorProcessTerminator` now prefers Shizuku (fires
          Shizuku's one-time permission dialog on first use, which doubles as onboarding) and falls back to the
          old kill only when Shizuku is absent. Still gated by the opt-in `AppSettings.CloseEmulatorOnReturn`
          (default on) in the couch Settings → Emulators section; the launched package rides in
          `PendingPlaySession.EmulatorPackage`; the close fires from `SingleViewShell.CompletePendingSession`
          for both built-in and second-screen returns. Shizuku is pulled as `AndroidMavenLibrary … Bind="false"`
          and reached via `java.lang.reflect` (no C# binding); manifest gains `moe.shizuku.manager.permission.
          API_V23` + `ShizukuProvider`. On-device: Dolphin force-stopped on return, log `Shizuku force-stop …
          exited with 0`. Requires the user to install + start Shizuku (once per boot on non-rooted devices).
          See DECISIONS 2026-08-25 and 2026-08-26.
    - [x] **S5 — onboarding no longer reappears after it was completed** (2026-09-05). Two device-read
          causes: (1) the onboarding-vs-shell verdict was taken at *process* creation (`Application.OnCreate`),
          and Android creates the process headlessly — re-binding the accessibility service after every
          install, Shizuku's provider — so a stale verdict (and a fully composed shell + Screen-2 Presentation
          nobody asked for) was frozen into the process the user later opened; (2) the data-folder pointer
          lived only in app-private storage, so a reinstall re-ran onboarding over a library that was still
          there. Now: `App` installs an `IActivityApplicationLifetime.MainViewFactory` that resolves when the
          Activity asks for its first view, onboarding re-resolves on every foreground return and completes
          itself when the pointer becomes readable, and `JsonDataLocationStore` mirrors the pointer to
          `<shared storage>/.emushelf-data-location.json` (best-effort, self-healing). Verdict logged to
          logcat as `EmuShelfBoot`. Side fix: the SAF picker's and the L3 overlay's TopLevel lookups read the
          Activity content, not the always-null `MainView`. See DECISIONS 2026-09-05.
    - [x] **S5 — couch UI polish round: selection, row density, second-screen wake, screen chooser**
          (2026-09-05, driven from a Thor pass and verified on it). (1) **The selection marker moved off
          the artwork.** The focus puddle had become a smudge after the justified-row repack; a proper
          accent halo was built to replace it and was rejected on sight ("terrible") — the third coloured
          treatment this grid has thrown out — so three replacements were shot on device and he picked
          the one that never touches a cover: a crisp accent bar in the gap between the cover and its
          title, wiping out from the centre as focus lands, with neighbours at 0.6. A continuous
          "breathing" version was measured first and cost 43% of a core on the render thread forever
          (0.5% with no animation), which is its own finding: no perpetual couch animation.
          (2) **Row/column gutters widened** for the couch only (44 px between covers, 20/32 above/below a
          row): at the desktop's 28 px the focused tile's lift nearly closed the gap to its neighbour.
          (3) **Landscape platforms pack four covers a row, not three** — the packer takes a
          `minCoversPerRow` the couch sets to 4, so SNES/arcade shelves stop reading as three oversized
          covers next to a portrait platform's five. (4) **The companion's doubled logo is fixed** (the
          resting spotlight and the standby wash were both painting it, half a dock bar apart) and **a
          touch now wakes the dimmed companion** for 5 s so you can see what you are pressing. (5) **The
          screen chooser is two cards drawing the Thor's clamshell** (`ThorDeviceGlyph`: stick over D-pad
          on the left, face buttons over stick on the right, like the real base) with the chosen screen
          lit, over a "remember" checkbox, laid out as glyph-beside-caption so it fits inside the Thor's
          own 833×468 overlay (pinned by test);
          "external screen" is gone from every string — the only hardware this reaches owns both screens.
          See DECISIONS 2026-09-05.


### 2026-09-06 — Decoded cover memory follow-up

- [x] Bound decoded 2D cover retention across cached scopes with a 64 MiB LRU target;
      protect realized covers with counted leases and reload evicted covers on demand.
- [x] Verify on AYN Thor (2026-09-06): 975-game library, decoded-cover retention stayed at
      the 64 MiB target; 1,326 evictions, reloaded search covers, and stable memory across two
      scrolling passes. See `docs/performance/thor-2026-09-06.md`.

- [x] Cancel superseded cover loads before thumbnail I/O/decode; release canceled queue waits.
- [x] Prepare filtering/sorting/justified rows off-thread above 256 games, coalesce resize bursts,
      and reject stale projections while preserving UI-thread publication.
- [ ] On AYN Thor, measure search/sort latency, resize/layout publication and rapid platform
      switching with a full library; tune the projection threshold and cache target from measurements.

- [x] Thor cancellation/search pass (2026-09-06): 35 obsolete cover requests skipped; 975-source
      projections prepared off-thread in 1.2–4.9 ms. Evidence in `docs/performance/thor-2026-09-06.md`.
- [ ] Profile and reduce UI publication spikes: still 22.7–123.7 ms on the Thor after background
      preparation. Finding 3 is only partially resolved; inspect rebinding, observers, and GC.
    - [x] **S6 — first-run onboarding rebuilt as a two-phase setup wizard** (2026-09-05; Thor pass pending).
          The three-treatment onboarding card is gone. Phase A (pre-boot: storage access, data folder — with
          "use your existing library" for a reinstall) and phase B (in-app, the Settings projection in setup
          mode: second screen, closing games, games and emulators, saves) share one rail, one row control
          and one legend with couch Settings. Plain wording throughout (no "second-screen return", "emulator
          return", "watcher"). `SetupCompletedVersion` gates the auto-open; Settings → Library has "Run setup
          again". See DECISIONS 2026-09-05 (the setup-wizard entry) and `docs/prototypes/android-setup-wizard/`.
    - [x] **S7 — every couch Settings section in the Emulators language, one control vocabulary**
          (2026-09-05; Thor pass pending). Library, RetroAchievements, Artwork & Metadata, Saves, Themes,
          About and Texture Packs now use the compact one-line rows PR #228 gave Emulators. A row ends in
          one of four things — chevron (red when destructive), caption-less switch, ‹ value ›, or a plain
          value; the glyph circles, switch captions, leading glyph wells and flat "info" rows are gone.
          Saves and Texture Packs platforms are summary rows that open one at a time; Y on an account row
          (Google Drive, RetroAchievements, ScreenScraper) disconnects it, Y on Export includes cloud
          copies, Y on a texture folder returns to the detected one. Parity is compared against
          `CollectParityIds` (every platform open, Y keys included). The wizard's Saves step is this section
          unchanged (summary cards; the platform still missing a folder opens itself) — the heading row kind
          it used to fall back on is gone. See DECISIONS 2026-09-05 (the Settings-language entry), 2026-09-06
          and `docs/prototypes/couch-settings-v5/`.

### Steam games through GameNative and Steam achievements (2026-09-12)

- [x] Steam platform/PC artwork, export-folder scan, stable app-ID reconciliation and metadata.
- [x] Android launch, return/session handling and Steam-specific save/termination exclusions.
- [x] Steam shelf case and shared grid/list/platform presentation.
- [x] Provider-neutral achievement viewer with Steam schema/progress, hidden/unknown states.
- [x] Local account connection, protected keys, offline cache and stale-account guards.
- [x] Desktop, gamepad and second-screen routing and settings.
- [x] Expandable provider groups and cancellable whole-library Steam achievement sync.
- [x] Automated regression suite, rendered Steam viewer and Android Release/AOT build.
- [ ] Target-device and live Steam-account acceptance — tracked in `docs/steam-gamenative-plan.md`.

### Android production preparation (2026-09-12)

- [x] Reuse the existing shelf mark for native launcher, adaptive/themed icon, splash and notification.
- [x] Show startup immediately, prepare storage/database off-thread, and offer non-destructive recovery.
- [x] Require Android packaging/signing on tagged releases; verify certificate, manifest and 16 KB alignment.
- [x] Ship arm64 only and fix downloaded checksum paths.
- [x] Add startup recovery tests, small-handheld render checks, and Android data/support documentation.
- [ ] Signed release-candidate cold/warm start, upgrade and lifecycle acceptance on Thor; 16 KB runtime acceptance.
- [ ] Close remaining performance, wizard/settings and Steam/GameNative device gates.

See `docs/android-production-readiness.md` for evidence and remaining acceptance scope.


### Second-screen artwork and media viewer (2026-09-13)

- [x] Diagnose Steam's blank companion on Thor: library covers are available, but the old
      spotlight only reads fanart/wheels. Confirm console spotlight and Steam achievements work.
- [x] Add provider-neutral local media gallery, cover fallback, horizontal swipe/controller
      navigation, and lazy, bounded image decoding.
- [x] Add cached public Steam hero/screenshots/MP4 previews with existing automatic-metadata
      consent or explicit Get Steam media; preserve manual art and source files.
- [x] Add Android tap-to-play muted video and cancel/stop on viewer lifecycle transitions.
- [x] Install the release-signed update on Thor; verify Steam/ScreenScraper images and actual
      Steam preview decoding with lazy download. Preserve library/settings during upgrade.
- [ ] Complete offline-device, local ScreenScraper MP4 and both-placement gameplay checks.
      Steam gameplay was stopped at the other-active-session warning; see docs/second-screen-media.md.

- [x] Rework media after user review: full-display dark viewer, hidden dock, compact controls,
      screenshot-first ordering, tap-to-hide chrome and swipe-down return. Verify both image
      and native-video dismissal on Thor; 47 companion regression tests pass.

- [x] Restore Steam spotlight to fanart plus separate publisher logo, including hashed logo
      paths. Load on selection independently of import metadata settings. Polish gallery
      fades, controls, loading feedback and video posters; 61 focused tests passed.

- [x] Unify companion fanart/logo rendering across platforms; move Steam enrichment to explicit/import fetch, save shared media records, migrate preview cache locally, and extend Fetch missing metadata to missing Steam artwork.

## M45 — Handheld mode: a dual-screen home menu for the Thor (in progress)

A third interface mode beside Desktop and Gamepad, for dual-screen Android handhelds: the bottom
screen is a 3DS-style launcher you arrange, the top screen shows what is selected. The spec is
[HANDHELD-MODE.md](HANDHELD-MODE.md) (product decisions 1–16, screen anatomy, controls, motion
spec, the map of what already exists). The visual contract is the interactive mockup in
[docs/prototypes/handheld-mode/](docs/prototypes/handheld-mode/) (`index.html`, version 5): every
screen below has a **Jump to** scene there, and the build must match it. The reasoning is in
`docs/prototypes/handheld-mode/REVIEW.md`; the baseline is recorded in `DECISIONS.md` (2026-11-06).

**How to work it.** One sub-milestone per session: `/milestone M45 H<n>`. Each is ordered so the
app is usable at its exit, each ends with a check on the Thor (Screen-2 is `FLAG_SECURE`, so that
means hands on the device plus `adb logcat`, not screenshots), and each gets one `M45 H<n>: …`
commit. The Android head stays outside `EmuShelf.slnx`; every model, store and view model lives in
`EmuShelf.Core` / `EmuShelf.UI` so the macOS suite (`EmuShelf.App.Tests`,
`EmuShelf.Infrastructure.Tests`) covers it. Copy the ideas, never the assets: no Nintendo or Cocoon
artwork, glyphs, sounds or exact layouts. Don't re-open decisions 1–16; when a choice is genuinely
open, pick the simplest option, record it in `DECISIONS.md`, and keep going.

**What already exists (reuse, don't duplicate).** Launching, choosers, save sync, achievements,
scraping, settings pages, the on-screen keyboard, the companion and its dock, the setup wizard —
all of it is logic behind `MainViewModel`, `SecondScreenViewModel`, `GamepadSettingsViewModel`,
`SecondScreenController` and friends; the spec's "What already exists, and what is new" table maps
each feature to its owner. Fan art and logos are scraped and stored (`GameMediaKind.Fanart`,
`GameMediaKind.Wheel`) and already drawn by the Gamepad spotlight hero. The artwork palette engine
(`ArtworkPaletteFactory`, `ArtworkPaletteExtractor`, `AppThemeService.ApplyArtworkPalette`) is
built and tested. Play data (`Game.LastPlayedAt`, `Playtime`, `PlayCount`) feeds M38's Recently
Played. Pad input already reaches one dispatcher from both displays
(`MainActivity.DispatchKeyEvent`, `ThorSecondScreenPresentation` → `DispatchGamepadAction`).

- [x] **H0 — device spike** ✅ (2026-11-07; passed — Avalonia for both screens, frame times and
      the facts H1–H5 build on are in `DECISIONS.md` 2026-11-07). (throwaway code is fine; nothing from it needs to be kept). On the Thor:
      both Avalonia surfaces animating at once (the `MainActivity` view and the
      `ThorSecondScreenPresentation` `AvaloniaView`); a selector gliding over 20 cover tiles on the
      bottom while the top crossfades; the two views re-hosted between displays; the pad routed from
      either display to one view model. Record frame times for both surfaces in `DECISIONS.md`.
      **Exit:** both screens animate smoothly enough to commit to Avalonia for both. **Kill
      criterion:** if the Presentation can't hold the selector glide at the panel's refresh rate with
      20 decoded tiles, stop and redesign before H1 (for example a lighter Launcher, or fewer live
      animations) rather than pushing on.
- [x] **H1 — mode and skeleton.** ✅ (2026-11-07; checked on the Thor by the owner, choices in
      `DECISIONS.md` 2026-11-07.) `InterfaceMode.Handheld` in Core; `AndroidInterfaceModeService`
      offers it only when a presentation display exists and persists the choice in `AppSettings`;
      every mode's Start/Settings menu can switch to it and back. `HandheldShellViewModel`
      (`EmuShelf.UI`) owns all state: home layout, open folder, selection, zones (grid / header /
      dock), overlays. `HandheldStageView` and `HandheldLauncherView` are pure views onto it; the
      Android host shows the Stage in `MainActivity`'s view and the Launcher in the Presentation
      while browsing. Platform folders are created automatically in the default order; the home grid
      is 5×4 with visible empty slots and page dots; a folder is a 4×3 grid scrolling by column with
      a header (Back, name, count, sort chip); the dock is today's companion dock with the D-pad
      reaching it. Tiles decode at tile size off the UI thread through the existing thumbnail
      pipeline. The Stage shows the focused folder or game with the name plate, the corner pills and
      the status pill (time, Wi-Fi, battery). Plain transitions only. Tests: default layout
      generation, grid navigation including empty slots and paging, folder sorts. **Exit:** from a
      cold start you can open a folder, scroll it, pick a game (launching temporarily via the
      existing path), press B back, and switch to Gamepad mode and back.
- [x] **H2 — Title page.** ✅ (2026-11-07; Stage hero, screen chooser, launch and return checked on the
      Thor over adb; the Launcher side is `FLAG_SECURE`, so its look awaits the owner's hands-on check.
      Choices in `DECISIONS.md` 2026-11-07 "M45 H2".) A on a game opens the Title page on the Launcher: cover, title, platform
      and emulator, last played, play time, which screen it runs on, disc, achievement count; the
      buttons Play, Disc (multi-disc only) and Achievements; Y the full actions sheet; B back. The
      disc chooser and the launch-screen chooser (`LaunchScreenResolver`'s Ask / Built-in /
      External, with "Always use this screen") are states of this page, as is the save-sync blocking
      panel; Play then runs the existing launch core (factor the shared part of
      `MainViewModel.LaunchGameCoreAsync` into a service both view models call rather than copying
      it). The Stage shows the hero: fan art full-bleed with the logo and a meta line, cover on a
      tinted backdrop as fallback; the browsing backdrop is the fan art. Returning from the game lands
      on the grid. **Exit:** every launch goes through the Title page, including both choosers and
      the sync panel; a game with no fan art still looks finished.
- [x] **H3 — motion pass.** ✅ (2026-11-07; checked on the Thor over adb — top screen recorded through folder
      focus, open, browsing, a held run, close and a launch — and frame by frame headlessly on macOS for
      the Launcher, which is `FLAG_SECURE`. The feel is the owner's call. Numbers in the spec's Motion section; choices in `DECISIONS.md` 2026-11-07
      "M45 H3".) Implement the spec's motion section with compositor animations,
      transform and opacity only, pre-rendered shadows, no per-frame blur. Selector spring (the
      spring keeps its speed when retargeted), focus lift, breathing glow, edge bump, folder focus /
      open / close, the top-screen centre pop with the held-direction debounce, page slides, the
      bottom-to-top launch (cover lifts off the Launcher, lands on the Stage, zooms into the
      emulator). Start from the mockup's constants and tune on the device; write the final numbers
      into the spec. **Exit:** judged by feel on the Thor by the owner, not by checklist. The
      selector is visible on the first frame of every transition.
      *Revised after the folder lab (2026-11-07, `DECISIONS.md` "M45 H4: Zoom through and the era
      motif"):* folder open/close is now the owner's pick, Zoom through (the spill is gone), and the
      Stage's backdrop is the era motif. Built and tested on macOS; the Thor feel check is still to come.
- [x] **H4 — arranging home.** ✅ (2026-11-08; checked on the Thor by pad, by touch alone and through
      `Settings/handheld-home.json`. Choices in `DECISIONS.md` 2026-11-08 "M45 H4: arranging home", "… polish
      and optimisation pass" and "… touch everywhere".) `HandheldHomeLayout` in Core (pages of slots holding a platform
      folder, a user folder, a game pin, or a system tile; user folders with name, colour, ordered
      ids and sort mode) and `IHandheldHomeLayoutStore` → portable `Settings/handheld-home.json`,
      the same pattern as `FileSecondScreenDockStore`. Reconciliation: new platform appended with a
      "New" ribbon, platform without games removed with its slot left empty, removed games dropped
      from every folder and pin, missing files dimmed. A game may be in several places at once
      (decision 12). Move mode with pad and touch; dropping a game on a game makes a folder, on a
      user folder adds it. Create a folder from an empty slot, from the merge, or from Y → Add to
      folder…; naming on the bottom screen through `AndroidOnScreenKeyboardService` while the Stage
      shows the folder, with colour chips. Rename, recolour, delete (games kept, confirmation says
      so). Pin to home, remove from home, with every "Remove" naming its place. The Recent folder,
      made for you from the Recently Played source, newest first, sort chip hidden. The empty-folder
      state. Sort chip Manual / A–Z / Last played / Most played. Touch everywhere on the Launcher,
      as the spec's controls table has it: tap selects, a second tap opens, swipe pages, long-press
      lifts into move mode, drag carries (the Launcher view takes no pointer input today; only move
      mode mentioned touch before the 2026-11-08 review). Tests cover the model, store and
      reconciliation on macOS. **Exit:** the layout survives a cold start and a reinstall-style
      restore of `Settings/`, and home can be browsed and arranged by touch alone.
      *Done 2026-11-08:* everything above, checked on the Thor by pad (folders made and named, add to folder,
      pin, move, cold start, a restore of `Settings/`) and then by touch alone (a folder named on the drawn
      keyboard, a folder opened, a game pinned from its sheet, Back, a folder deleted through its
      confirmation). Naming uses the mockup's drawn keyboard, because Android's keyboard cannot open on
      Screen-2. A long press without a drag opens a tile's actions sheet, so touch reaches every action.
- [x] **H5 — screen swap.** ✅ (2026-11-08; a Stage and a Launcher kept on each display, so a swap only changes which
      is in front. Checked on the Thor by the owner and over adb — swaps both ways from home and an open folder, a cold
      start coming up swapped, Gamepad mode and back, and what each native screen drew read back with `glReadPixels`,
      both screens being `FLAG_SECURE`. Choices in `DECISIONS.md` 2026-11-08 "M45 H5: screen swap".) X re-hosts the Stage and Launcher between displays with the slide
      (precedent: `MainScreenCompanion` re-hosting in `SecondScreenController`). Selection, open
      folder and scroll position survive; `IsSwapped` is remembered. On the narrow screen the Stage
      moves its hint and achievement pills to the top-left and widens the plate, as in the mockup;
      the Launcher on the 16:9 screen gets a layout of its own (7×3 at the same tile size with the
      page label in the spare height — decide and record), not the centred 5×4. The companion
      too: when a game takes the narrow screen the companion sits on the 16:9 one, which the mockup
      never tried; decide its layout there (one panel at a time still holds) and record it.
- **H6, split 2026-11-08.** The old H6 carried the companion, the Y sheet, the Start menu,
      Settings, Search, idle drift, sounds and dock icons in one item; the owner's review found two
      of them effectively missing (the in-game companion is still Gamepad mode's, and Settings cannot
      be reached at all: today's Start menu holds only Switch mode and Quit). It is now four
      sub-milestones, each with its own Thor check and `M45 H6<x>: …` commit, in this order.
- [x] **H6a — Settings tile, Start menu, Settings.** ✅ (2026-11-08; checked on the Thor over adb with both screens
      read back (native surfaces by `glReadPixels`, the Avalonia Launcher rendered to a bitmap; not kept), on the owner's
      library, swapped and not: the tile appended to an existing home, opened from the tile, from the Start menu and from
      inside a folder, every section with its live status, an emulator chooser, a text row on the drawn keyboard
      (cancelled), the section list, touch, the tile moved and surviving a cold start, folded into a folder, moved back,
      and the folder deleted. Choices in `DECISIONS.md` 2026-11-08 "M45 H6a". The menu's Search, Add games, Achievements
      and Sounds rows arrive with their screens in H6c and H6d.) *Stop-gap first, its own small commit:* a
      Settings row in today's Start menu that opens Gamepad mode's settings overlay on the Stage, so
      Settings is reachable at all. Then the real entrance, as the 3DS and Cocoon do it: Settings is
      an app on the grid. `HandheldSlotKind.SystemTile` with one kind for now, Settings (decision 18,
      `DECISIONS.md` 2026-11-08): in the default layout right after the platform folders, movable and
      foldable like any tile, never removable so there is always a way in; a glyph face on the indigo
      accent so it reads as neither a platform nor a user folder; the Stage shows a plate for it like
      a dock app's; A opens Settings. Settings as master-detail across the screens: sections and rows
      on the Launcher using today's row kinds from `GamepadSettingsViewModel`, the focused row's
      description and value on the Stage; every section (Library, Emulators, Achievements, Artwork &
      metadata, Saves, About) including the per-platform emulator and folder choosers, with text rows
      on the on-screen keyboard. The Start menu stays a list rather than a page of tiles, since the
      tiles live on the grid: Settings, Search, Add games, Achievements, Sounds, Switch mode, Quit.
      Tests: the default layout places the tile, reconciliation never drops it, remove is refused.
      **Exit:** every setting Gamepad mode has can be changed from Handheld mode without leaving it,
      from the tile and from the menu, and the tile survives a cold start in the slot you moved it to.
- [x] **H6b — the in-game companion.** ✅ (2026-11-08; checked on the Thor over adb with Screen-2 read back (the
      companion is Avalonia, rendered to a bitmap; the native screens by `glReadPixels`; not kept), on the owner's
      library, swapped and not: Castlevania launched from Recent on the top screen, the companion coming up as the Launcher
      (or Stage) slid away, resting after 12 s and the first touch only waking it, every badge with All / Locked /
      Unlocked, the photos paged and the viewer, All apps from the dock and back, the return sliding the screen back
      onto the game's tile in Recent; the Title page's badge grid and the dock's All apps over the Launcher. The
      companion on the 16:9 screen is checked headlessly only: the Thor's accessibility watcher is off, so a game cannot
      go to the bottom screen. Choices in `DECISIONS.md` 2026-11-08 "M45 H6b".) Today `SecondScreenController.UpdateHandheldSurface` shows
      Gamepad mode's companion unchanged while a game runs. Restyle it on the Launcher's dark palette
      as the spec's Companion section describes, every feature kept: resting dim with the swallowed
      wake press, the Now playing row, one panel at a time with Achievements / Photos on LB/RB, the
      full badge grid with All / Locked / Unlocked, the photo viewer and the muted video, the dock;
      the D-pad reaches all of it (dock, drawer and the overlay buttons are touch-only today). The
      handoff both ways: Launcher → companion as the launch lands on the Stage, companion → Launcher
      on return, landing on the launched game's own tile with the Recent folder already updated so two
      presses resume. The companion on the 16:9 screen (H5) is checked here with a game running.
      Dual-screen emulators (DS/3DS) stay deferred. **Exit:** on the Thor a whole session — launch,
      play, check an achievement and a photo, open the dock, return — never shows the Gamepad-mode
      companion.
- [x] **H6c — the Y sheet, Search, Add games, and the surfaces behind them.** ✅ (2026-11-08; checked on the Thor over
      adb with both screens read back (native surfaces by `glReadPixels`, the Avalonia Launcher rendered to a bitmap; not
      kept), on the owner's library, swapped and not: the game sheet in a folder and over a Title page (scrolling to Remove
      from library), Achievements from both with the real set, badges, filters and the badge on the Stage, Edit title
      (cancelled), Set cover with real web results (none picked), the Remove confirmation (kept), Search typed on the drawn
      keyboard with the matches fanned on the Stage and opened as a folder, a folder's and a dock app's sheets, Add games
      opening Android's picker (cancelled), and the failure toast. This build has no ScreenScraper credentials, so the
      scraper and batch scraper show their "unavailable" toast on the device and their panels are checked headlessly.
      Choices in `DECISIONS.md` 2026-11-08 "M45 H6c".) The Y sheet with
      every Gamepad action (Launch, Select disc, Achievements, Edit title, Set cover, Scrape with
      ScreenScraper, Remove from library with its confirmation) plus Move, Add to folder…, Pin to home
      / Remove from home, the folder actions (Rename, Colour, Delete folder) and the dock-slot actions
      (Open, Change app…, Clear slot). Search with the keyboard on the Launcher and results on the
      Stage. Add games through the existing folder picker. The screens those open that have no
      Handheld face yet: the scraper, batch scraper and cover search (choices on the Launcher, preview
      on the Stage), full-screen achievements while browsing, and errors and toasts beyond the status
      pill (launch failed, sync failed, scrape finished, update available). **Exit:** every
      Gamepad-mode action is reachable from Handheld mode and its outcome is visible without switching
      modes.
- [x] **H6d — idle drift, sound, dock icons, first run.** ✅ (2026-11-08; checked on the Thor over adb with both
      screens read back (native surfaces by `glReadPixels`, the Avalonia Launcher rendered to a bitmap; not kept), on the
      owner's library, swapped: the drift through Recent's heroes and the first press only waking it, the sounds' tracks
      playing (Android's player list) and the Start menu's switch off and on, the docked apps' 512 px icons on their
      colours, a fresh-install start (mode never chosen, wizard unfinished) coming up in Handheld mode with the wizard
      across both screens walked to Finish, "Run setup again" and Settings → Display's Sounds row. The empty library is
      checked headlessly only (emptying the owner's library on the Thor was not worth the risk to it). Choices in
      `DECISIONS.md` 2026-11-08 "M45 H6d".) Idle drift: after fifteen seconds the
      Stage drifts through recent games on a slow crossfade, the first press only wakes it. Five
      original sound effects (move, open, close, confirm, back) with an off switch. Dock icons loaded
      at about 512 px with one colour picked per app. First run in Handheld mode, which M45 never
      covered: the setup wizard's steps laid across the two screens (decision 10), and the
      empty-library state (what home shows with no games, and the way to add some). **Exit:** a fresh
      install on the Thor reaches a playable home without switching to Gamepad mode, and nothing a
      Gamepad-mode user can do is missing in Handheld mode.
- [ ] **H7 — console backdrops on the Thor (native Stage shaders).** Replace the era motif behind the
      Stage, on Android only, with the 53 animated backdrops the owner approved in the motion lab
      (`docs/prototypes/handheld-mode/backdrops-motion-study/`; the set is `picks.json`, the look and
      motion reference is `backdrops.js` / `console-rotation.html`). Each system rotates through its
      picks (random, no immediate repeat, about 12 s each, crossfade). Desktop keeps the era motif.
      Why GPU shaders, and the alternatives that were rejected, are in `DECISIONS.md` (2026-11-07,
      "M45 H7"). It can be scheduled ahead of H4–H6 if the owner wants: it touches only the Stage's
      backdrop path. Three slices, one commit each:
  - [ ] **H7a — framework and one backdrop per system (16).** In `EmuShelf.Rendering/Stage/Backdrops/`:
        a registry (backdrop id → program, internal resolution, output settings); a low-res
        offscreen target per backdrop at its console's resolution (NES 256×144 up to 480×270); one
        shared output pass to the 1920×1080 canvas (nearest or linear upscale, bit depth with Bayer
        dither, LCD/CRT cell grid, composite blur, bloom, and ghost/persistence through a ping-pong
        target); and three draw paths: full-screen fragment shaders (planes, patterns, caustics),
        instanced boxes/heightfield meshes lit per pixel (tiles, cubes, seas), and PS1-style meshes
        (snapped vertices, `noperspective` affine UVs, 16×16 textures). `StageCompositor` gets a
        procedural backdrop beside the picture layers, using the existing two-state crossfade, and
        `NextFrameMilliseconds` returns the backdrop's step (33 ms) while one is showing. A system's
        set comes from data in `KnownSystems` (backdrop ids per system id), carried through
        `HandheldPlatform` → `HandheldBackdrop` → `HandheldNativeStageFeed` as a new Stage command.
        Browsing a system's games keeps the running backdrop (value equality, no restart). If a
        program fails to compile, that system falls back to today's era motif layers. Slice set (one
        per system, chosen to cover every draw path and output stage): `nes-meta`, `runway`,
        `gbc-bands`, `gba-iso`, `gc-caustics`, `ds-ripples`, `wii-tiles`, `3ds-tiles`, `md-planes`,
        `dc-checker`, `field`, `ps2-sea`, `psp-field-v2`, `ps3-sheet`, `arc-checker`, `steam-mesh`.
        **Verify on macOS:** `tools/EmuShelf.Rendering.Preview` renders every backdrop headless to
        PNG frames, compared side by side with the prototype at the same time values. Tests cover
        the registry (every system resolves at least one id, every id has a program and settings)
        and rotation (no repeat, dwell, seeded). **Exit, on the Thor:** the owner judges all 16 by feel
        against the prototype; `adb` measurements recorded in `DECISIONS.md` for idle on home, idle
        on a game, and scrolling home (GL thread %, GPU %, render/UI thread %), plus skin temperature
        and battery drain over 30 minutes idle on the Stage. **Kill criterion:** if idle on home costs
        more than about 10% of the GL thread or 15% GPU at 30 Hz, or the device warms noticeably,
        stop and choose between a lower step rate, pre-rendered loops, or fewer animated systems
        before H7b.
        *Built and checked on macOS (2026-11-07); on the Thor the owner judged all 16 good by feel.* They
        compile on the device's GL ES (no "unavailable" lines in logcat). First reading idle on a game
        (Release AOT, one sample): the Stage's GL thread ~15% of a core (4% before), GPU ~4%. The full
        measurement suite is folded into H7b's exit, against the heaviest backdrop of each draw path.
        *Measured 2026-11-13 (`DECISIONS.md` 2026-11-13 "M45 H7: the backdrops measured on the Thor"):* idle on home 5.6–6.9%
        GL thread, 0.9–4.2% GPU at 28 fps, for all 49 backdrops the Thor can show; idle on a game 8% GL, 4% GPU; scrolling home
        7% GL, 7% GPU, app 53%. Well under the kill criterion. Still open: battery drain over 30 minutes, which needs the Thor
        unplugged (wireless adb), so it waits for the owner.
  - [ ] **H7b — the remaining 37 and rotation.** Port the rest of `picks.json`, then turn on per-system
        rotation (dwell and crossfade timed in the compositor, so the UI thread never wakes for
        it). Each port is checked against the prototype in the preview tool. **Exit:** all 53 on the
        Thor, rotation judged by the owner, measurements repeated for the heaviest backdrop of each
        draw path.
        *Built 2026-11-07: all 53 in `BackdropCatalog`, every system's list is its `picks.json` row, and the
        compositor rotates them (random, never the same twice running, 12 s dwell, 1.6 s eased crossfade).
        Every port was rendered through the app's renderer and compared with the prototype at t = 8 s; only
        hash-seeded layouts differ (tap spots, orange blocks, cover colours). All backdrop shaders pass a GL ES
        front-end check (`glslc`, ES 3.10 rules), the suite passes, and the Release AOT build is installed on
        the Thor with no "unavailable" lines in logcat.* Still open for the exit: the owner's judgement of
        the rotation, and the measurements (only a rough `top` reading so far: the busiest app thread at
        4–12% of a core while GameCube rotated).
        *Measured 2026-11-13:* the heaviest of each draw path is `wii-pointer-v2`/`gc-fur` (fragment, 6.3% GL), `gc-cubes-v3`
        (boxes, 6.7%), `ps2-trails-v3` (lines and particles, 6.9%) and the PS1 meshes (6.0%); the heaviest GPU is `tiles` at 4.2%.
        All 49 cost within a point of each other, since the cost is per posted frame. Not caught: the three PS3 backdrops (no
        PS3 games on the Thor) and `frames`. Still open for the exit: the owner's judgement of the rotation.
  - [x] **H7c — rest and fallbacks.** ✅ (2026-11-08; checked on the Thor over adb, Release AOT: reduced motion
        (`animator_duration_scale 0`) and battery saver (`low_power 1` with the battery faked unplugged, since Android
        refuses the saver while charging) each hold the Stage still within two seconds, through navigations and past
        where the idle drift would have started, at the app's background floor (0.0–0.5% of a core, no Stage frames, GPU
        0–1%); a recreated activity (font scale changed) logged both Stages carrying on into their new contexts (the Screen-2
        one its era motif, clock and rest state; the main one its running `nes-raster`); the resume of a running animated backdrop and the hold are checked under GL on macOS
        by `tools/EmuShelf.Rendering.Preview --stage-resume`. Choices in `DECISIONS.md` 2026-11-08 "M45 H7c".) The
        backdrop holds a still frame when the Stage is idle for
        a while (as the glow rests), when Android battery saver is on, and under the system's
        reduced-motion setting; a lost GL context rebuilds the running backdrop. The desktop Stage
        stays on the era motif (recorded, not built). **Exit:** an hour idle on the Stage shows no
        measurable cost from the backdrop once it rests.
        *Rest after idle built 2026-11-07 (pulled forward by the owner's "I don't want my fan to ramp up"):*
        a minute after the last navigation the backdrop's clock and the medium's idle sway ease to a stop
        over 2 s and the Stage posts no frames; measured on the Thor, the whole system is then back at the
        floor of the app being in the background. *Battery saver, reduced motion and the rebuild built 2026-11-08:*
        the device's say (`DeviceStatus.HoldsStill`, watched, not polled) rests the backdrop, the medium's sway and the
        idle drift together, and lifting it puts the ordinary minute rule back; the surface captures the old
        compositor's running backdrop and rest state and applies them after the retained replay. Idle measured over
        five minutes, not an hour: two and five minutes after the last press the app was at 0.0–1.5% of a core with no
        Stage frames and the GPU at 0–6%, the CPU and GPU sensors a degree cooler than while animating. *The hour, 2026-11-13:* 62 minutes without input on
        home; once the drift rested (about 70 s in), the Stage GL thread gained no ticks for the rest of the hour, the
        Stage presented one frame a minute (the clock), and the app sat at 0.19–0.23% of a core in every 5-minute window,
        the background floor. What SurfaceFlinger did meanwhile was the ROM's burn-in refresher, not EmuShelf. Exit met.
        With the recreation crash
        (below) fixed, two font-scale recreations of the merged build left the app up with both Stages logging their
        resume each time (the Thor runs that build, installed 2026-11-08 22:16).
    - [x] **Recreating the activity crashes Handheld mode.** ✅ (2026-11-08; found by the font-scale change used
          to check the GL-context rebuild, which recreates `MainActivity`. The process died in Avalonia's layout
          because the Screen-2 Presentation's Avalonia top level was never disposed with its window; it is now.
          `DECISIONS.md` 2026-11-08 "M45 H7c: a recreated activity no longer kills Handheld mode".)
- [ ] **Open exits hiding inside items above** (collected 2026-11-08 so they are not lost; tick
      each where it lives, then this one): H3's Thor feel check of the revised folder motion (closed as superseded by
      M47, owner 2026-11-13); H7a's
      battery drain over 30 minutes unplugged (the rest of its suite was measured 2026-11-13); H7b's owner
      judgement of the rotation (its measurements were taken 2026-11-13); H2's
      Launcher-side look, which awaited the owner's hands-on check (closed as superseded by M47, owner 2026-11-13);
      H6d's real fresh install on the Thor (cleared data or a second device: the pre-boot steps, then Handheld mode, the
      wizard and a playable home, end to end; the device check faked "fresh" in the owner's settings), its empty library on
      the device (checked headlessly only), and the owner's ear on the five sounds (character and level).
- [x] **A home tile goes undrawn until focused.** ✅ (2026-11-13; `DECISIONS.md` 2026-11-13 "M45: a folder tile is
      never sent without its art".) Found on the Thor: after a burst of scrolling that flipped to page 2 and back, and
      the GameCube folder opened and closed, the PSP tile (row 1, column 6) was blank while unfocused, not even an
      empty-slot outline; focusing it brought it back. Not reproduced since (the scroll and folder sequence replayed,
      80 randomized menu visits, three leave-and-return cycles). Fixed the one path that gives exactly this: a folder
      tile pictured while its art could not be captured went to the native Launcher without it, marked sent. If it
      comes back, the cause is elsewhere.
- **Later, not part of M45:** square icon art; themes and widgets; grid-size options; a
      single-screen fallback; other dual-screen devices; whether the companion steps aside for
      DS/3DS emulators that draw their own second screen (check melonDS / Azahar on the Thor first).

## M46 — Play log: sessions, accurate handheld timing, an Activity screen (in progress)

Builds on M43, whose two totals per game (`Games.PlaytimeSeconds`, `Games.PlayCount`) can't say when
a game was played or for how long each time. The goal is what the 3DS Activity Log and Cocoon's Log
show: every sitting with its date and length, and per game the total, times played, average per
session, first and last played, plus a ranking. The reasoning, and why it reverses M43's "totals, not
a session table", is in `DECISIONS.md` (2026-11-08, "M46"). One slice per session, each with its own
commit; the model, store and view models live in Core / Infrastructure / `EmuShelf.UI` so the macOS
suite covers them. Play data stays in the portable `Data/library.db`; nothing here reads or writes the
user's game files.

Problems with today's recording that this fixes (found in the 2026-11-08 review):

- **No history.** Only running totals exist, so no day view, average or first-played date is possible.
  Play from before the log can't be split back into sessions.
- **Android over-counts.** A session is the wall-clock gap from launch to EmuShelf's return, capped at
  12 h. Sleeping the Thor mid-game, or time spent in other apps, is credited as play.
- **Desktop under-counts hand-off launchers** (a wrapper that starts the real process and exits), and a
  session whose EmuShelf was killed adds no time at all.
- **Per-device totals can't be merged** later by a sync, and a wrong session can't be corrected.

- [x] **P1 — the sessions table.** ✅ (2026-11-08; built and tested on macOS, the v20 → v21 upgrade
      checked in the Release app against a scratch library; the launch-and-exit path is covered by
      view-model tests with the real library, not by a hands-on launch, and the Thor check is still to
      come. Choices in `DECISIONS.md` 2026-11-08 "M46 P1".) Schema v21: `PlaySessions` (id, game id, started at UTC, duration
      in seconds or null while open, device id, how the duration was measured: tracked process / usage
      access / wall-clock estimate / edited). A row is inserted at launch where `RecordLaunchStarted`
      runs today, and closed where `AddPlaytime` runs (desktop exit, Android
      `CompleteDeferredPlaySessionAsync`); a row never closed is a play of unknown length, which keeps
      M43's "count at start, time at exit" rule. `Games.PlaytimeSeconds`, `PlayCount` and
      `LastPlayedUnixMilliseconds` stay as a cache updated in the same transaction, so every existing
      surface (list columns, Gamepad caption, Recently Played, the Handheld Title page) keeps reading a
      plain column. Play recorded before v21 stays in the totals and is shown as "earlier play", never
      invented as sessions; first played is the earliest session, or unknown when the game only has
      earlier play. A stable per-install device id in `Settings/`. Removing a game from the library
      keeps its sessions, as Cocoon does, so the log and the totals of a re-added game survive.
      Tests: migration from v20 with existing totals, open and closed rows, cache consistency, an
      unclosed row after a simulated kill, removal keeps history. **Exit:** on macOS and the Thor every
      launch writes one row, and the totals match the sum of the rows plus earlier play.
- [x] **P2 — accurate timing on Android through Usage access.** ✅ (2026-11-08; built and tested on
      macOS, the Android head compiles with no new warnings, and the timing is checked against a real
      Thor session read with `dumpsys usagestats`: three sleeps in 98.6 wall-clock minutes, 28:58 on
      screen. Installed on the Thor at 13:05 once M45 H4 was done: the owner's library (1001 games)
      upgraded to schema 21 and the app starts cleanly. Still open on the device: granting access in the
      new row and a session timed by the app itself. The row is in Gamepad mode's Settings (Library);
      Handheld mode can't reach Settings until M45 H6a.
      *Exit checked 2026-11-13:* access granted. A PPSSPP session launched from Handheld mode had 2 minutes awake, 2
      asleep and 1 awake (305 s wall clock), and recorded 184 s measured from usage events, exactly what `dumpsys
      usagestats` shows (124 s + 60 s). `DECISIONS.md` 2026-11-13.
      Choices in `DECISIONS.md` 2026-11-08 "M46 P2".) An opt-in "Accurate play time" row in
      Settings (Library) that explains the permission and opens Android's Usage access page, as Cocoon
      does. Granted: the session's length is the time the emulator's package was actually in the
      foreground between launch and return, from `UsageStatsManager` activity resumed / paused events,
      so sleep and other apps drop out. Not granted, or the events have already aged out (a return days
      later after process death): today's capped wall-clock figure, stored as an estimate and labelled
      "about" wherever it shows. Behind an interface in Core so the computation from a list of events is
      unit-tested on macOS. **Exit:** on the Thor, a session with the screen asleep in the middle records
      only the awake minutes, checked against `adb shell dumpsys usagestats`.
- [x] **P3 — the Activity screen (Desktop and Gamepad).** ✅ (2026-11-08; built and tested on macOS;
      every page rendered headlessly at Desktop and Gamepad sizes by `ActivitySnapshotTests`. Not yet
      used on a real week of Thor play; that check waits for the device testing together with P2. Choices
      in `DECISIONS.md` 2026-11-08 "M46 P3".) A day-by-day timeline (each day's games and
      minutes, newest first, with a week total); a page per game with total, times played, average per
      session, first and last played and its recent sessions; a ranking by total time, times played or
      average. Edit a session's length or delete it from the timeline or the game page (Y / long press
      on Gamepad, context menu on Desktop); an edited row says so. Opened from the Gamepad Start menu,
      the Desktop toolbar, and a game's actions. Reads are paged and off the UI thread; a large log
      must not hitch the grid. **Exit:** a week of real play on the Thor reads correctly in the timeline,
      and an edit or delete updates the totals everywhere.
- [x] **P4 — Handheld mode.** ✅ (2026-11-13; built and tested on macOS, every screen rendered headlessly on both
      canvases by `HandheldActivitySnapshotTests`. Not yet on the Thor: the exit below waits for the device testing
      together with P2 and P3, and the Android head was not compiled in that session. Also on the Start menu and as
      "Play history" on a game's Y sheet, as in Gamepad mode; foldable into your folders like Settings. Checked on the
      Thor: tile, week, ranking, game page, folding in and out, the editor (cancelled) and the Title page's new facts;
      the companion's session line is left for a real play session. Choices in `DECISIONS.md` 2026-11-13 "M46 P4" and
      its follow-ups.) An Activity system tile on home beside Settings (amends M45 decision 18,
      which allowed only Settings; record the amendment when built), opening the timeline as
      master-detail across the screens: days and games on the Launcher, the focused day's or game's
      stats on the Stage. The Title page adds first played and average per session. The in-game
      companion's Now playing row shows the running session's time (M45 H6b). **Exit:** from the
      Handheld home you can see what you played yesterday and for how long, without leaving the mode.
- **Later, not part of M46:** syncing sessions between devices (the device id makes rows mergeable;
      belongs beside M33); timing hand-off launchers on desktop by following the child process; export
      to CSV; play-time goals or reminders.

## M47 — Handheld: the Big Type look (planned; nothing built)

Handheld mode restyled to the direction the owner chose on 2026-11-13 (`DECISIONS.md`, "Handheld look: the Big
Type direction"): the focused item's name fills the Stage over that console's backdrop, the Launcher takes the
console's flat colour, the face is M PLUS Rounded 1c, nothing sits in a pill, and every change is animated. The
visual contract is the drivable mock, [docs/prototypes/handheld-big-type/index.html](docs/prototypes/handheld-big-type/index.html):
fifteen numbered moves, a table of type, shape, motion and colour values at Thor resolution, and a stress sheet
(`#stress`) that measures the Stage's text against all 53 approved backdrops. It overturns decision 16 of
`HANDHELD-MODE.md` and nothing else; the grid, the dock, the backdrops, the 3D medium and every feature stay.

**What the code says about the size of this** (mapped 2026-11-13, before any build):

- **There are no Handheld tokens.** About 330 colour literals, 110 font sizes and 100 corner radii are written
  inline across the four views (`HandheldLauncherView.axaml` alone has 178, 52 and 56), with more brushes in
  `HandheldConverters.cs` and `Color.Parse` calls in about ten other files. A restyle has to collect them first.
- **A console's colour is parsed ad hoc** from `GameSystem.AccentColor` at seven call sites. Those accents were
  tuned for the backdrops and several are too light for white type; the mock uses darker ones for nine consoles only.
- **On the Thor both screens are drawn by native GL compositors that only draw pictures.** `StageCompositor` and
  `LauncherCompositor` draw textured quads with rectangular scissor clipping. They have no text, no circular or
  masked clip, and nothing reads the backdrop's brightness back. All text is a picture of an Avalonia control, and
  each picture is a GPU round trip. The selector's three bands are hard-coded in `selector.frag.glsl`.
- **Menus, sheets, Settings, naming, move mode and panels are not native.** The Avalonia Launcher takes the screen
  back while one is open, so the hand-off between the two has to be invisible in the new look.
- **Only `Exo2.ttf` is bundled.** Handheld views set no typeface and use the system font (decision 16's Nunito was
  never added).
- **Reduced motion covers the Stage only.** `HoldsStill` (battery saver, or Android's animator scale at 0) stops
  the medium's sway, the idle drift and the backdrop; the Launcher compositor has no hook.
- **Snapshot tests never compare frames.** 27 Handheld test files, five of them snapshot tests that write PNGs on
  request and assert only visibility, size and view-model state. A full set that renders 84 states exists on
  `claude/design-system-visual-refresh-f11cc2` (commit 6b49476, `HandheldDesignSnapshotTests.cs`), unmerged.
- **A on a game opens the Title page as its own state** (`HandheldTitleState`). The mock plays straight away.
  Launching directly would skip the screen chooser, the disc chooser, the save-sync panel and the launch lead; the Y
  sheet's Play already runs the page with `playWhenReady`, which is the safe route to "A plays".
- **M45 still has open exits a restyle replaces:** H2's hands-on check of the Launcher's look and H3's feel check of
  the folder motion would be judging things this milestone changes.

**How to work it.** As M45: one item per session, each leaves the app usable, each ends hands-on on the Thor
(Screen-2 is `FLAG_SECURE`), each gets one `M47 <id>: …` commit. Build behind a look switch so today's Handheld
mode stays the default until B10. Static before motion; Avalonia views before the native compositors, because the
native side only pictures them.

**Who builds.** The owner's instruction (2026-11-13): build steps run in their own sessions on Opus, one at a
time, each from the previous step's branch. The design session that wrote this plan starts them, checks what they
report against the exits below and against the mock, and does not build.

**Where the device checks run.** From 2026-11-14 the owner's Thor is not available, and the owner's instruction is to
use an emulator: each step's device check runs on the Android emulator with a second display the size of the Thor's
bottom screen, and its note says "emulator", never "Thor". An emulator check shows that the app starts, that both
screens draw and whether the native compositors ran; it cannot judge colour on the AMOLED panels, real frame times or
feel. Every exit that says "on the Thor" therefore stays open for the owner until the device is back.
**The Thor is back (later on 2026-11-14; the owner: "I've connected ayn thor, you can use it again").** From B6 on,
device checks run on the Thor again, at 60 Hz as the owner runs it and with no system setting changed. A build goes on
with `adb install -r` only, so the owner's data stays; nothing is uninstalled and no app data is cleared. A step may
measure frame times there and record them; how a step looks and feels is still the owner's to judge. B3, B4 and B5 were
checked on the emulator only, so their exits stay open for the owner's look.
**Emulator again from B8 (late on 2026-11-14; the owner: "Use the emulator").** The Thor left adb at the end of B7 and
the owner chose the emulator over reconnecting it. B6 and B7 were checked on the Thor; B8 onward are emulator checks,
labelled so, until the owner says the Thor is back. The Thor keeps B7's build, with the look last read as `bigtype`.
**The Thor again (2026-11-15; the owner: "ayn thor is connected again, you can use it for testing and review").** B8's
check, already under way, moves back to the Thor, and B9 and B10 run there too, under the same rules as before: 60 Hz, no
system setting changed, `adb install -r` only, the owner's data untouched. If adb loses the Thor mid-step, that step
falls back to the emulator and says so.

### Before building: decide (owner)

- [x] **D1 — A on a game plays.** ✅ (owner, 2026-11-13; `DECISIONS.md` "Handheld: A on a game plays".) Built by
      opening the page with `playWhenReady`, so the screen chooser still appears for a console set to ask, save sync
      still runs and nothing is skipped; the page's facts (emulator, last played, disc) sit under the name on the
      Stage; the disc is a row in the Y sheet. Drawn in the mock. Amends decision 13.
- [x] **D2 — All fifteen moves stay.** ✅ (owner, 2026-11-13, after R1 showed each one fits a frame on the Thor.)
- [x] **D3 — The colour table.** ✅ (owner approved the sixteen colours 2026-11-13; the values are `SYS` and `XC` in
      the mock's script and are listed on its Stress sheet tab.) Sixteen consoles need a ground colour dark enough for white type (4.5 to 1 on the
      ground, 3 to 1 on a tile); the mock has nine, five of them between blue and violet. Either spread them by hand
      or derive each from `AccentColor` by a rule and accept what comes out. **Owner, 2026-11-13: by hand, in the
      mock, reviewed on the page before the values go into the app.** Drawn 2026-11-13 (the Stress sheet tab lists all
      sixteen with their contrast and how far apart they are; closest pair 19, where PS2 and PSP were 5); approved by the
      owner the same day.
- [x] **D4 — M45's open look and feel exits (H2, H3) are closed as superseded.** ✅ (owner, 2026-11-13.) They
      would have judged what this milestone replaces; both are marked where they live in M45.
- [x] **D5 — Today's look is removed at B10.** ✅ (owner, 2026-11-13.) It stays the default behind the switch
      during the build, then it is deleted; there is no setting to choose between the two.

### Before building: find out

- [x] **R1 — The Thor spike.** ✅ (2026-11-13; branch `claude/elegant-lederberg-05c307`, commits 354de31 and
      984b487; the full tables are in that branch's `DECISIONS.md`.) Built behind `debug.emushelf.bigtype` and measured
      on the Thor at 60 Hz, as the owner runs it. **The kill criterion did not trigger: move 2 stays as drawn.** Every
      steady frame of the three motions, on either display, costs under 5.2 ms of CPU and under 2.4 ms of GPU, so
      they fit a 120 Hz frame (8.3 ms) as well as a 60 Hz one. The worst case, a 35-letter three-line name arriving
      during a backdrop wipe with the clock rolling, is 0.9 ms typical and 5.2 ms worst on the CPU. **How letters
      reach the Stage:** one atlas per name. The UI thread fits the name and rasterises each distinct letter once
      into one bitmap (3.5 to 14.4 ms there); the Stage uploads one texture and draws one quad per letter. The flood
      and the wipe are a circle uniform added to two fragment shaders.
      **What does not fit, and is not new:** the first frame of a console change on the top display takes 9 to 36 ms
      (the new backdrop starting and its two full-screen fallback layers uploading). Today's look pays it more often
      (14 frames over 8.3 ms in 20 presses, against 4 to 10 with the spike). B6 should take it off that frame.
- [ ] **R2 — The legibility rule without readback.** The mock samples the backdrop twice a second; the device
      cannot read it back. **Baked 2026-11-13:** `docs/prototypes/handheld-big-type/legibility.json` has ink and veil
      per backdrop for the three zones. 52 of the 53 keep one ink in every zone at all eight moments measured, and
      only four need more than the resting veil (`gc-caustics`, `gc-fur`, `md-pipe-v2`, `ps2-sea`), so a table in
      `BackdropCatalog` is enough and no readback is needed. One zone could not be baked: `ps2-sea`'s button hints
      change ink over time. **Still to do, with B4:** the table comes from the JavaScript prototypes, not the GLSL
      ports, so look at each console's backdrops on the Thor once, and choose `ps2-sea`'s hint ink there.
- [x] **R3 — The font on Android.** Confirm the licence text (SIL OFL on Google Fonts), measure the three weights'
      size in the APK (the family has 8,546 glyphs including Japanese), decide between shipping whole and a Latin
      subset with the system face as fallback, and check one Japanese title renders either way.
      ✅ (2026-11-13; `DECISIONS.md` "M47 R3: M PLUS Rounded 1c ships whole".) OFL 1.1, no Reserved Font Name,
      "Copyright 2016 The Rounded M+ Project Authors." Each weight has 8,546 glyphs (8,201 mapped characters):
      Medium 3,432,376 bytes, Bold 3,542,384, Black 3,635,260; 10.6 MB for three, about 4.9 MB deflated and
      7.0 MB with LZ4 (the assembly store's compression), both estimates. A Latin subset (Latin-1, Latin
      Extended-A, punctuation, arrows, the UI's symbols) is 250 KB for three (99 KB deflated) and drops the 5,143
      kana and kanji, and Cyrillic, Greek and Vietnamese. Fallback works per glyph either way (no missing-glyph
      box, Avalonia on macOS), but with the subset a Japanese title is drawn in two system faces at one weight
      under Latin in this one. **Shipped whole**: the three files are in `Assets/Fonts/MPLUSRounded1c/`, declared
      as `EmuHandheldFontFamily` (the name after `#` must be "Rounded Mplus 1c", the name inside the files), the
      licence ships beside Exo 2's, and nothing applies it yet; B2 does.
- [ ] **R4 — Real covers and real panels.** Load the owner's covers into the stress sheet (it reads them in the
      browser only), and look at the nine flat colours on the Thor's AMOLED panels at normal brightness.
- [ ] **R5 — The bottom panel's rate.** `HANDHELD-MODE.md` records both displays at 120 Hz; reviews list the bottom
      one at 60 Hz. Measure it, because the motion table's staggers are tuned by eye at 120.
      Found 2026-11-13 by the R1 spike: the owner's Thor is set to 60 Hz (system `peak_refresh_rate` and
      `min_refresh_rate` both 60), and the owner chose to leave it there. So 60 Hz is the rate to tune for; R1
      reports render time against both the 16.7 ms and the 8.3 ms budget. The spike also read the displays: both the top panel and
      the bottom one list 60 and 120 Hz modes and are active at 60 under the cap. Whether the bottom one really
      reaches 120 with the cap lifted (M45 H0 saw it stay at 60) was not tested.

### Before building: finish the design in the mock

- [x] **M1 — The screens not drawn yet.** ✅ (2026-11-13, in the mock under "More screens".) The screen chooser, the
      disc row and "Syncing saves…"; the generic panel as one drawing (rows below, the focused row as the Stage's name),
      shown for a game's achievements and for Activity, with cover search as a grid of candidates worn by the case;
      messages as the line under the name (progress counting in place, errors held longer with one shake); a game
      whose file is missing; the companion's Session, Achievements and Photos tabs and its rest state; the empty
      library and setup as three questions and a climbing count; X swap. Not wired separately because they reuse the
      panel drawing: the scraper, Edit title and the batch scraper. Not drawn: the companion's badges layer and photo
      viewer, and the All apps drawer.
- [x] **M2 — Freeze the numbers.** ✅ (2026-11-13.) `docs/prototypes/handheld-big-type/tokens.js`: about 140 named
      values at Thor resolution for face, type, the Stage's and the Launcher's layout, shape, colour, motion and
      legibility. The mock reads its console colours and legibility rule from it and prints all of it under "Frozen
      numbers"; the app's resource dictionary mirrors it name for name.

### Build

- [x] **B0 — A safety net.** ✅ (2026-11-13; macOS only, the Thor was busy with R1. Choices in `DECISIONS.md`
      2026-11-13 "M47 B0".) Bring `HandheldDesignSnapshotTests.cs` over from 6b49476. Add a frame comparison that
      fails on any pixel difference, used only by B1. Add the look switch (today's look, Big Type), off by default.
      **Exit:** all 84 frames render on macOS and the comparison passes against itself.
      Built: the design set renders **158 frames** (77 top/bottom pairs and four companion frames; 81 at 1240×1080,
      77 at 1920×1080), up from 144 at 6b49476 because the Activity tile now sits in its own slot of the sample home
      and the play log has six new pairs. `HandheldDesignFrameComparisonTests` compares two directories pixel for pixel
      with no tolerance, failing with each frame's name and count of differing pixels and writing a diff image per frame
      into `_diff`; its logic (`FrameComparison`) has unit tests that render nothing. Two renders were made identical
      by pinning, never by loosening: a fixed wall clock, the culture, the backdrop's drift, indeterminate progress bars,
      a wait until both screens stop changing, a fresh draw of the visual tree instead of the compositor's frame, and
      the selector ring's resting radius (the one app change that draws: under a quarter pixel). Three renders on macOS,
      one beside a full `dotnet test`, matched on all 158 frames; a render takes about 2½ minutes. The look switch is
      `HandheldLook` (Classic, BigType) in `EmuShelf.Core`, saved as `AppSettings.HandheldLook`, set on
      `HandheldShellViewModel.Look` when Handheld mode starts; nothing reads it yet. Render into a fresh directory, then
      compare:
      `EMUSHELF_DESIGN_SNAPSHOT_DIR=/tmp/b0 dotnet test tests/EmuShelf.App.Tests --filter "FullyQualifiedName~DesignSnapshot"`
      and `EMUSHELF_DESIGN_BASELINE_DIR=/tmp/b0 EMUSHELF_DESIGN_SNAPSHOT_DIR=/tmp/b1 dotnet test tests/EmuShelf.App.Tests
      --filter "FullyQualifiedName~DesignFrames_MatchBaseline"`. A normal `dotnet test` runs neither.
- [x] **B1 — Tokens, with no visible change.** ✅ (2026-11-13; choices in `DECISIONS.md` 2026-11-13 "M47 B1".) One
      Handheld resource dictionary for colour, type, radius and
      spacing; every inline literal in the four views and every brush in code reads from it; one accessor for a
      console's colour replaces the seven parse sites. **Exit:** every frame is pixel-identical to B0's.
      Built: the home is a static class, `HandheldTokens` (`EmuShelf.UI/ViewModels/Handheld`), read from XAML with
      `x:Static` and from code directly: **434 tokens, 158 of them `Classic`** (108 brushes, 30 gradient and shadow
      colours, 26 box shadows, 143 sizes, 87 corner radii plus 20 derived, 14 weights, 6 border widths). All 879 colour,
      type, weight and radius literals in the five views (`HandheldLauncherView`, `HandheldStageView`,
      `HandheldCompanionView`, `HandheldPanelView`, `HandheldFolderArt`) now read a token, as do the converters' brushes,
      the native feeds' gradients, glow, lift shadow and launch card, the motion's pitches and selector widths, and the
      shadow's default. `HandheldTokens.ConsoleColour` / `ConsoleBrush` replace the seven `AccentColor` parses with the
      same fallbacks. `HandheldTokens.Use(look)` runs once when Handheld mode starts; Big Type has no values of its own
      yet. Left inline: `Transparent`, one `BoxShadow="none"`, layout, non-selector border widths, opacities, transforms
      and transitions (listed in the decision). Not touched: `EmuShelf.Rendering` and its shaders, whose own copies of
      today's look are `selector.frag.glsl`'s bands (4 / 14 / 4 px, violet 91,88,217), `LauncherCompositor`'s launch bar
      colours (#29465088, #5B58D9) and clear colour (0.96, 0.97, 0.98), `StageCompositor`'s clear colour (0.94, 0.95,
      0.96) and `LauncherMotion.LiftScale` 1.13. **Result:** the design set rendered before and after the refactor
      matched on all 158 frames with no tolerance (byte-identical PNGs), twice, the second after the last edit.
- [x] **B2 — The font and the colour table.** ✅ (2026-11-13; choices in `DECISIONS.md` 2026-11-13 "M47 B2".) Bundle the
      face per R3 with its notice in `THIRD-PARTY-NOTICES.md`; add the ground colours per D3 with a unit test that holds
      each to its contrast floors.
      Built: the face was bundled by R3; under Big Type, `HandheldTokens.ApplyFace` sets `Face` (App.axaml's
      `EmuHandheldFontFamily`, held equal by a test) at `WeightText` 500 on the roots of the Launcher, Stage, Companion and
      Panel views, where every text inherits it, and Big Type gives the 14 role weights tokens.js's 700 or 900. Under
      Classic `Face` is null and nothing is set, so the system face stays. The sixteen approved grounds are
      `GameSystem.GroundColor`, authored in `KnownSystems` beside `Era` and `Backdrops`, and reach Handheld mode on
      `HandheldPlatform.GroundColor`. `HandheldTokens.ConsoleColour(HandheldPlatform?)` replaces the accent-string accessor:
      Classic gives `AccentColor` exactly as before, Big Type the ground, or for a system without one its accent darkened
      until the floors hold (`HandheldGround.Derive` in Core). `FolderColour(source)` sends a platform folder to it and keeps
      your folders' own colours; `ColourOf`/`BrushOf` read stored text. **Derived colours: none.** All sixteen systems the
      app knows are in the table. `HandheldGroundColourTests` holds the table to tokens.js and the floors (white 4.5 to 1 on
      the ground, 3 to 1 on a tile of white at 17% over it; closest pair snes/gba at 18.6 by CIE76, floor 18) and every
      accent's derived colour to the first two. On Android the Handheld views are built with the first MainView, before
      Handheld mode starts, so `SingleViewShell.Show` now gives the tokens the look first (without it a saved Big Type look
      would have drawn the views in Classic values). The volatile `debug.emushelf.look` property (`bigtype` or `classic`)
      overrides the saved look for one run and is never saved. **Render either look** into its own fresh directory:
      `EMUSHELF_DESIGN_SNAPSHOT_DIR=/tmp/classic dotnet test tests/EmuShelf.App.Tests --filter "FullyQualifiedName~DesignSnapshot"`
      (today's look, the default) and the same with `EMUSHELF_DESIGN_LOOK=BigType` and another directory.
      **Result:** Classic rendered before and after: 158 of 158 frames identical by the comparison, and byte-identical
      PNGs. Big Type: all 158 frames render, no errors, no missing-glyph boxes; 157 differ from Classic (the one that does
      not, an empty home page, has no text). Thor (Release AOT, `adb install -r`, uid and data kept): Classic starts clean;
      with the property set, Big Type draws M PLUS Rounded on both screens and the table's colours on the home folders, and
      the owner's one Japanese-titled game (PSP, "ヱヴァンゲリヲン新劇場版-サウンドインパクト-", no cover) draws on the Stage
      and on its tile with no missing-glyph box. Property cleared, app restarted in Classic; settings, layout and library
      checksums and the 60 Hz refresh settings unchanged.
- [ ] **B3 — The Launcher, still.** Flat ground in the focused console's colour, matte squarer tiles, monograms
      fitted to the tile, header, dock and counts as plain type, the ring as one white band (the shader's bands become
      parameters). **Exit:** home and a folder look like the mock at rest on the Thor, and the hand-off to a sheet
      and back shows no seam.
      **Built 2026-11-14; awaiting the owner's look on the Thor.** (Choices in `DECISIONS.md` 2026-11-14 "M47 B3".) Under
      Big Type the Launcher's ground is the focused item's flat colour (`HandheldShellViewModel.LauncherGround`, drawn by the
      Avalonia view and cleared to by the native compositor through `LauncherGroundCommand`, both with a 200 ms crossfade);
      tiles are 176 and 208 with radius 30, flat, monograms fitted to 76% of the width and at most 56% of the side, the
      focused folder or app white with its colour as ink, covers cropped square, missing files at 0.45 with a white "!"
      mark, the New ribbon a mark with a dot, empty slots a 0.30 dot and a plus when focused; the ring is one white 8 px
      band 26 out from the resting tile, radius 50 (38 on the dock), no glow, tiles lifting 1.10 and dock buttons 1.14 with
      no shadow; a folder's header is its name and count as 100 px type; the dock has no bar, "X Swap" at the bottom left
      and the page ("1 / 2") or a folder's sort at the bottom right. `selector.frag`'s bands are uniforms
      (`LauncherRingBands`), and a native sprite carries its own lift. Today's look is the default and unchanged. A
      render-harness fix comes first: the Settings › About frames print fixed stand-ins for the build's commit and date
      (`DesignHoldBuild`), so renders on different commits compare. **Verified on macOS:** Classic rendered before and
      after, 158 of 158 frames identical by the comparison; the Big Type set renders (158 frames, kept as B4's baseline in
      the build sessions' notes folder, `m47-b3-renders/bigtype`) and home and a folder were put beside the mock's bottom
      screen part by part (ground, tiles, monograms, focused tile, ring, header, empty slot, missing-file tile, dock): the
      differences left are listed in the decision (the dock row's spacing for seven buttons, the app's filled icons, the
      no-cover tile, top-cropped covers). New tests hold the Big Type values to tokens.js, today's values after switching
      back, the ground following the focus, and the native ring and lift defaults; `dotnet build` and `dotnet test` pass.
      **Thor:** the Release build was installed with `adb install -r` (uid kept), then the device left USB before the app
      was started; nothing else was done on it. The owner then said the Thor is not available and to use the emulator.
      **Emulator check (not a Thor check):** a new arm64 AVD `emushelf-b3` from the installed android-35 image (nothing
      downloaded), main display 1920×1080, a second display of 1240×1080 from the AVD's own `hw.display1.*` (a presentation
      display), GLES 3.0 through gfxstream on the host GPU. The same Release APK: Classic starts cleanly into Handheld mode
      (Stage on the main display, Launcher on the second), drawn by the native compositors (four `GLThread`s rendering, no
      fallback logged). With `debug.emushelf.look bigtype` the native Launcher draws the flat ground (exactly PS2's #2B5BC7),
      flat tiles, the focused folder white with its colour as ink, the plain header and one white ring, at the same pixels
      as the macOS render (the focused tile's ring 136–395 × 182–441 in both); a sheet opens over the Avalonia Launcher in the
      same place (its ring within 1 px) and, closed, leaves the native screen pixel-identical to before. Property cleared,
      Classic again. Emulator artefact: its second display carries the system's navigation bar over the bottom 72 px, which
      covers half of the dock row. B3's exit still needs the owner's eyes on the Thor.
- [ ] **B4 — The Stage, still.** The fitted name, the four corners, no plate and no pills, the veil and ink per R2.
      **Exit:** the name is readable over every backdrop of every console in the owner's library, on the device.
      **Built 2026-11-14; awaiting the owner's look on the Thor.** (Choices in `DECISIONS.md` 2026-11-14 "M47 B4".) Under Big Type
      the Stage has no plate and no pills: the focused item's name is fitted by the spike's `BigTypeLayout` (now in
      `EmuShelf.Rendering/Stage/BigType`) into 1360 × 680 (1120 beside a game), bottom-anchored at 80 / 240, at most three lines; the
      console's name (or the item's kind) top left, battery and clock top right, the line and second line under the name, outlined hint
      glyphs bottom right; a game's medium (or flat cover) in its corner; a missing game's name at 0.5 with "File not found". The texts
      are `Label`/`Name`/`Line`/`Facts` on the Stage's view model. **The ink follows the backdrop that is showing on the native side:**
      the feed pictures each text in white with its zone and sends the veils as white masks; `StageCompositor`, which picks and rotates
      the backdrops, looks the showing one up in `BackdropLegibility` (the baked `legibility.json`, beside `BackdropCatalog`) and tints
      each zone's text and veil, mixing through crossfades; no shader (the pictured fallback, the Avalonia Stage) is white with the
      resting veils. `ps2-sea`'s hints are white with a 0.5 veil, **for the owner to check on the Thor**. The swapped screen shows the
      16:9 Stage whole, letterboxed, as the mock does. Today's look is the default and unchanged. **Verified on macOS:** Classic rendered
      before and after, 158 of 158 frames identical by the comparison (byte-identical PNGs); the Big Type set renders (158 frames, kept
      as B5's baseline in the build sessions' notes folder, `m47-b4-renders/bigtype`) and was put beside the mock's top screen, captured
      headless at Thor scale, part by part: the corners land on the mock's pixels, the name within 1–5 px (monogram "PS2", one-line
      "Ico", three-line "Burnout 3: Takedown", the missing game); what still differs is listed in the decision. A new desktop GL check
      (`tools/EmuShelf.Rendering.Preview -- --stage-bigtype`) drives the real compositor over the real backdrops and reads the ink back:
      dark over `ps2-towers`, white over `ps2-trails-v3`, split over `arc-checker` and `gc-caustics`, white over the fallback, and the
      letterbox. Unit tests hold the table to `legibility.json`, the Stage's values to tokens.js, the ink's mixing and the veils' shape, and
      the name fit (monogram, long title, a single very long word); `dotnet build` and `dotnet test` pass. **Emulator check (not a Thor
      check):** on `emushelf-b3` the Release build ran Classic cleanly; with `debug.emushelf.look bigtype` the native Stage drew the name,
      corners, hints and the 3D case with no plate or pills, the backdrop shaders ran and the ink followed the rotation (dark over
      `ps2-towers` and `ps2-orbs-v3`, white over `ps2-trails-v3`); Recent, Settings, an empty slot and the dock drew in white over the
      fallback; property cleared, Classic again. The emulator's library is PS2 only, so the other consoles' backdrops were not seen.
- [ ] **B5 — Motion that needs no new drawing.** The ring's stretch between tiles, dock and rows; the tile lift and
      neighbours making room; rolling digits; the idle hop. Give the Launcher compositor the `HoldsStill` hook now.
      **Built 2026-11-14; awaiting the owner's hands on the Thor.** (Choices in `DECISIONS.md` 2026-11-14 "M47 B5".) Under Big Type
      only, from one set of maths shared by the native screens and the Avalonia Launcher (`BigTypeMotion`, `BigTypeLauncherMotion`,
      `BigTypeRollDraw` in `EmuShelf.Rendering/Stage/BigType`): **the ring** is a timed path (not the springs): it covers the old and
      the new target by 38% of 300 ms, lands by 74% (squashed across its travel when the two are the same size), changes size and
      radius on the way (tile 50, dock 38, header rows 32; sheets, Settings and panels get the ring with B7), pops in on another home
      page, and breathes 1 → 1.03 at rest; **the lift** lands on the landing curve in 380 ms and the up-to-eight tiles around the focused
      one stand 8 px off it (the feed gives each tile its place, `LauncherSprite.Cell`); the focused tile bobs 6 px; **rolling text**
      (the clock and battery, the Stage's line with its counts, a folder's count, the page "1 / 2") rolls only the characters that
      changed, cut from the pictures the app already sends with where each character sits (`StageText`), each clipped to its own cell;
      **the idle hop** runs over the grid in view 4.8 s after a press, then every 6.5 s. The breath, the bob and the hop live a minute
      after the last press and then rest, as the Stage's backdrop does; the native Launcher steps at 30 Hz over that minute and posts
      nothing after it. **`LauncherMotionCommand.HoldStill`** (battery saver, reduced motion): the ring and lift land at once, nothing
      breathes, bobs, hops or rolls. **Fallback ink fixed:** where no backdrop shader runs (the pictured era motif) the text is the dark
      ink over a white veil at its resting strength (the Avalonia Stage too). Today's look is the default and its motion is unchanged.
      **Verified on macOS:** Classic rendered before and after, 158 of 158 frames identical by the comparison; the Big Type set renders
      (158 frames; kept as B6's baseline in the build sessions' notes folder, `m47-b5-renders/bigtype`) and differs from the render made
      before B5 in 113 frames, every one explained: 76 of 77 top frames by the fallback's ink (the one that does not is the empty page,
      which has no text), and 36 bottom frames by the neighbours standing 8 px off the focused tile (the swapped screen's bottom is the
      Stage, so the ink). Nothing needed pinning in the harness: the breath, the bob and the hop run on the Avalonia compositor and never
      reach a frame, and the push and the rolls settle inside the harness's wait for rest. New tests hold the maths (`HandheldBigTypeMotionTests`:
      the ring at 0, 38%, 74% and 100% for equal and unequal targets and mid-flight, which neighbours move and how far, which characters
      roll and when, the hop's schedule and stagger, the rest cadence, and that nothing moves when held still) and tokens.js; a new desktop
      GL check drives the real `LauncherCompositor` at chosen moments (`dotnet run --project tools/EmuShelf.Rendering.Preview --
      --launcher-bigtype`) and reads the ring, the neighbours, a roll, the hop, the 30 Hz rest and holding still back; `dotnet build` and
      `dotnet test` pass. **Emulator check (not a Thor check):** on `emushelf-b3` the Release build ran Classic cleanly
      into Handheld mode, the native compositors drawing (four GL threads), and its moves were today's (three bands on the spring, the
      1.13 lift). With `debug.emushelf.look bigtype`, frames read back from recordings of both displays showed: the ring covering
      Settings and Recent at once, then landing on Recent (home), growing tall over an empty slot and All apps then shrinking onto the
      dock button at its smaller radius (the dock), stretching between games right, down and right (a folder); Settings and PS2 standing
      off the focused tile; "1 / 2" caught mid-roll with only its first cell turning, and the ring popping in on page 2; the Stage's line
      rolling "9 games" → "Loading…" → "Not played yet"; the idle hop running left to right over home's tiles 4.8 s after the last press;
      the sheet opening over the Avalonia Launcher as before. The GL threads gained CPU ticks through the minute after a press and none
      in 20 s from about 66 s after it. The Stage drew the fallback in the dark ink. Holding still was not tried there (it needs a system
      setting changed); the desktop GL check covers it. Property cleared, Classic again. **Seen on the emulator and present on B4's build
      too** (the same steps on a build of `c7573b3`), so not B5's: after moving from page 2 back to page 1 the native Launcher shows no ring
      and no lift on the focused slot, in either look; a sheet opening shows two or three white frames on the second display; and the
      Launcher's corner type ("1 / 2", Swap) draws with black boxes behind each glyph, the pictured-translucent-text fault B4 fixed on the
      Stage only.
- [ ] **B6 — Letters, and the flood.** Start from the spike's code on `claude/elegant-lederberg-05c307`
      (`BigTypeLayout`, `BigTypeScene`, `BigTypeStageLayer`, the `uClip` circle in `stage.frag` and
      `backdrop-output.frag`, `NativeFrameProbe`, `GpuFrameTimer`) and turn it from a flagged spike into the real
      thing. Also move the backdrop's start-up cost off the first frame of a console change (R1). **Exit:** frame times on the Thor at or under
      R1's figures for a three-line name during a wipe.
      **Built 2026-11-14. Exit measured on the Thor and NOT met (figures below): not ticked; the owner's call.** (Choices and the full
      figures in `DECISIONS.md` 2026-11-14 "M47 B6".) Under Big Type
      only: **the name arrives letter by letter** on the native Stage from one atlas per name, laid out by the Avalonia name's own fit
      (`HandheldBigTypeName.Letters`, held to the control's drawing by a headless test) and drawn one quad per letter in the left zone's ink
      (`StageNameCommand`, `BigTypeStageLayer`); each letter lands from the move (right, left, up, down, into a folder from the hinge, out of
      one, or rising) on tokens.js `LetterIn`, the old name leaving the other way on `LetterOut`; the Avalonia name moves the same way where
      the native Stage does not run, and swaps while it is only pictured or held still. **The flood**: the Launcher's new ground grows as a
      circle from the focused tile (`Flood`, 640 ms to 1640 px), replacing `LauncherGroundCommand`'s fade. **The wipe**: the Stage's new
      backdrop grows through a circle from 40 px below its bottom edge at the focused tile's x (`Wipe`, 110 ms, 760 ms, 2300 px), over the
      ones beneath until it covers them; text and veils are drawn per backdrop inside its own circle and outside those above it, each in that
      backdrop's own ink, so no pixel of text is ever in an ink chosen for another backdrop. **The letters hop** on the tiles' schedule from the
      shell's `InputArrived` (`StageInputCommand`). **The start-up cost**: the backdrop is sent at once and prepared over the frames after the
      press (started on the next, first step drawn on the one after; its circle starts at 110 ms or when ready), and its pictured layers go up
      only if drawn. **Held still**: the name is there at once and the old one goes in 60 ms, the flood lands, the wipe cuts once ready.
      tokens.js gained the mock's inline numbers for the entrances, `LetterOut.stillMs`, `Wipe.below` and the letters' hop. R1's probe is kept
      behind `adb shell setprop debug.emushelf.probe 1` (logcat `EmuShelfBigType`, per motion). **Fixed on the way:** after moving from home's
      page 2 back onto page 1 the ring and lift went to page 2's slot, off screen (both looks; Classic changes by the restored highlight only),
      and the Launcher's corner type is pictured with grey-scale antialiasing (no black boxes). **Verified on macOS:** Classic rendered before
      and after, 158 of 158 frames byte-identical; Big Type (the final render): 13 of 158 differ from both renders made before B6, none in a
      way the step meant to change at rest (the name at rest is drawn as before): 12 only on the focused ring's anti-aliased edge (39 to
      1,202 pixels inside a box the size of the ring, channel differences 6 to 26; the same frames differ between the two renders of B5's
      commit, run to run), and the launching frame, where the Avalonia name's letters now move during the
      harness's wait, so the frame is taken after the launch card's flight has ended. New tests hold the letters', flood's, wipe's and hop's timing at fixed
      moments to tokens.js (`HandheldBigTypeLettersTests`); a new desktop GL check (`dotnet run --project tools/EmuShelf.Rendering.Preview --
      --stage-letters`) reads each letter where the maths puts it mid-flight, the hop, holding still, the backdrop not started on the frame
      its command lands on, and the text white inside and dark outside the wipe's circle; `--launcher-bigtype` now reads the flood mid-flight
      and held still. `dotnet build` and `dotnet test` pass. **Emulator check** (`emushelf-b3`, before the Thor came back): recordings of
      both displays caught the flood, the letters, the wipe, the letters rising into a folder and the hop mid-flight, and the page fault gone.
      **Thor check** (Release AOT, `adb install -r`, uid and data kept, 60 Hz untouched, screens unswapped as found; R1's probe on with
      `debug.emushelf.probe`): the page fault is gone in Classic (back from page 2 the ring and lift are on GBA) and the corner type has no
      black boxes. **The exit, measured: not met.** Three-line names arriving during a wipe (Recent's games across consoles: "Shin Megami
      Tensei : Persona 2, Innocent Sin", "Tony Hawk's Underground 2 Remix", "Metal Gear Solid : The Twin Snakes", "Bleach : Heat the Soul",
      "Batman : Return of the Joker"; 304 steady frames): CPU 1.81 ms typical, 5.10 p95, **18.6 worst** (frames over 5.2 in four of the six
      runs, most of them 0.7 to 0.85 s after the press, once the letters have landed; their cause is not isolated); GPU 1.48 typical, 2.25
      p95, **2.54 worst**. R1: every steady frame under 5.2 ms CPU and 2.4 ms GPU (0.9 and 0.7 typical). These games all show the 3D case,
      which R1's stress run did not have. Monogram names during a wipe (home's first row): GPU 1.47 typical, **3.25 worst**. **The first frame
      of a console change** (20 presses along home's first row, Big Type, the same probe): before B6 (B5 plus the probe only) the Stage paid
      17.6 to 41.2 ms on 10 of 20 presses, about 380 ms after the press (after the settle; repeats included), 10 frames over 16.7 ms; with
      B6 the press frame is 0.3 to 0.6 ms on every press, and the start-up cost falls on the next frame only the first time each backdrop
      shows in a run (its shaders compile: 13.1 to 36.6 ms, 9 backdrops), repeats 0.3 to 0.5 ms; 4 frames over 16.7 ms, 11 over 8.3 (10
      before). Left on the Thor: this build with `debug.emushelf.look` set to `bigtype` (a reboot or `adb shell setprop debug.emushelf.look
      classic` returns it to Classic). **The slow frames (the owner: "Find the slow frames"):** found with a finer
      probe (each slow frame's commands, draw parts and quad count): the frames 0.6 to 1.0 s after the press were Big Type's own quads, about
      50 µs of CPU each, many at once (the line rolling when its facts arrive, 54 to 59 pieces; the name's letters, drawn twice during a
      wipe), not the 3D case, whose first render (8 to 19 ms) comes earlier and once per game. **Fix:** under Big Type a quad sets only the
      uniforms that changed (about 24 µs a quad; nothing drawn changes). **Measured again: CPU still over, not ticked.** Three-line names
      during a wipe: CPU 1.58 typical, 3.91 p95 (was 5.10), 19.3 worst (the 3D case's first render; Big Type's own worst is 6.9, a long
      name and a rolling line in a wipe); GPU 1.51 typical, 2.18 p95, 7.73 worst (one frame; the rest at most 2.80). Console row: over 16.7 ms
      2 (was 4). **Then two savings that change nothing on screen:** a zone's text drawn once
      when every backdrop showing answers it the same way, and the letters and roll pieces in instanced batches (0 pixels different on desktop
      GL; GPU-neutral by an in-run A/B on the Thor). **Final figures** (the same Recent run): three-line names during a wipe, CPU 1.31 typical,
      2.83 p95; **Big Type's own drawing, worst 4.5 ms; the 3D case's first render, worst 18.0 ms**; GPU 1.42 typical, 2.14 p95, 2.87 worst
      (over 2.4 only in step with the two backdrops' renders during a wipe). Today's look on the same run: CPU 1.02 typical, 2.25 p95, 50.0
      worst, 9 frames over 8.3 and 4 over 16.7 (Big Type 6 and 3); its 3D first render 19.0 ms; GPU 0.74 typical, 7.40 worst. **Still not
      met, not ticked:** the 3D first render and the GPU through a wipe. **Open for the owner:** whether to accept that; the swapped screens'
      wipe (the mock has none); the feel of every motion.
      **Merged into the design branch on 2026-11-14 with the exit left open** (the orchestrating session, while the owner was away):
      what is over the limit is the 3D first render, which today's look pays the same, and the GPU through a wipe, 2.87 ms inside a
      16.7 ms frame; neither is touched by B7, so the build goes on and the decision stays the owner's.
- [ ] **B7 — Lists.** The Y sheet, the Start menu, Settings and the generic panel as type on the colour, with the
      focused row as the Stage's name. Avalonia only.
      **Built 2026-11-14; awaiting the owner's look on the Thor.** (Choices in `DECISIONS.md` 2026-11-14 "M47 B7".) Under Big Type only
      (`HandheldBigTypeLists`, under the one ring): the Start menu, the Y sheet and its confirmations, Settings (rail, rows, switches,
      a choice's list, a confirmation) and the panels (achievements, Activity, cover search as the mock's grid of covers, the scrapers)
      are type on the Launcher's flat colour under a heading whose letters land (`HandheldBigTypeHeading`, tokens.js `HeadingIn`, the
      mock's `lettersIn`); rows sit on `RowPitch` / `RowPitchSettings` / `RowPitchPanel` at the mock's places, slide in (`RowsIn`; a
      Settings section's rows rise, `SectionIn`), move over and brighten when focused, and scroll by whole rows past their window; the
      ring frames them at `RingRadiusRow` on B5's timed path, stretching between rows and breathing, and jumps into and out of a list;
      the grid under a sheet steps back (`StepBack`) and goes under Settings and a panel. What the Stage says follows the mock's code (round 2): the
      Y sheet and the Start menu keep the name they were opened over, with the kicker ("More", "Menu") over it and the row's meta or
      hint under it; Settings and the panels make the focused row the name, arriving by B6's letters (Activity: the day's play time as
      the figure; cover search: the game, its case wearing the candidate). Settings takes its own colour, and the Avalonia Launcher now floods
      too. Held still, everything is in place at once. tokens.js gained the mock's inline list numbers (listed in the decision; one,
      `RowGroupIndent`, is the app's own). Typing (Settings' and the panels' keyboards) keeps today's look until B9. **Round 2:**
      rows leave as the mock's do (a closing sheet's right, a replaced sheet's left, Settings' and a panel's down) and on the Thor the
      native Launcher comes back only after; long rail names and row labels shrink to fit down to `LabelFitMin`, then end in an ellipsis,
      and a row's value gives way to its label; the Stage's lines end before the hints; the dock row's corner rolls to the version under
      Settings; the ring's resting drift is fixed (the neighbours' push is stepped before the ring); cover search's case wears the
      candidate; a test that left the process-wide look in Big Type (and made other tests fail by their order) now runs apart. **Fixed:** the two or three white
      frames when a sheet opens (both looks: the native Launcher was hidden before the Avalonia one had drawn; it now waits for two
      rendered frames), and B5's rolling text drawing an earlier text. **Verified on macOS (round 2):** Classic at the final code is byte-identical to Classic
      at the starting commit `7c93bea` on all 158 frames; two Big Type renders of the final code are byte-identical on all 158; the full
      `dotnet test` ran three times after the last change, all three green (1,407 + 1,651), after the first three runs had each failed one
      test, `HandheldGroundColourTests.TheFace_IsAppAxamlsHandheldFamily_AndTodaysLookSetsNone` ("Expected: Classic, Actual: BigType"),
      because a B0 test left the process-wide look in Big Type (now fixed).
      **Round 1, for the record:** Classic rendered before and after (and between), 158 of 158 frames byte-identical; Big Type: 72
      of 158 frames differ from the starting render, all lists (sheets, menu, Settings, Activity, cover search, add games, the Title
      page's actions sheet) plus the folder header's ring at 32 (`RingRadiusRow`), home page 2's Stage line (the rolling-text fix) and
      ring-edge drift; two Big Type renders of one build differ only in 2 folder frames on the ring's edge. Sheet, menu, Settings
      (rail and rows), achievements, Activity and cover search were put beside the mock's bottom screen captured headless at Thor
      scale: heading, rows and ring land on the mock's places (also held by a headless test to the mock's `buildList` ring), the switch
      matches. New tests: `HandheldBigTypeListMotionTests` (timing at fixed moments, tokens.js) and `HandheldShellViewModelTests+BigTypeLists`
      (tokens, every row's and ring's place for the menu, a sheet and Settings, the Stage's text, Classic untouched, the native Launcher
      handing over only after this view has drawn). `dotnet build` and `dotnet test` pass. **Thor check** (Release AOT, `adb install -r`,
      uid 10211 kept, 60 Hz untouched; both displays recorded and screencapped): with `bigtype`, the Y sheet, the Start menu, Settings,
      Activity, achievements and cover search open with their rows arriving and the heading's letters landing, the ring stretching between
      rows, the Stage naming the focused row by its letters, Settings flooding to its colour and a section's rows rising; no frame of the
      second display was white while a sheet or the menu opened. Faults seen there were fixed and checked again (the rail's names cut, a
      long value squeezing its label, cover search's grid running under the dock and its rows vanishing once scrolled, the candidate
      missing on the native Stage). With `classic`: the sheet and the menu as before, no white frames; set back to `bigtype`. **Thor, round
      2** (the same way, `bigtype`): an item's sheet keeps its name with "More" over it; a sheet's rows leave right and fade with the
      ring hidden, then the native home returns; "Add to folder…" sends the old rows left and the new sheet's arrive; Settings shows
      "Achievements" whole, "Artwork & metadata" fitted then ended in an ellipsis, a long value giving way to its label, the version in the
      corner, and on closing its rail and rows drop; the Stage's line ends before the hints. Left on the Thor: this build,
      `debug.emushelf.look` `bigtype`. Renders, mock captures, recordings: `m47-b7-renders` in the build sessions' notes.
- [ ] **B8 — Folders, launch, in-game.** Folders deal their games; the cover leaves through the hinge; the
      companion's session count and achievement roll.
      **Built 2026-11-15; awaiting the owner's look on the Thor. Checked on the Thor (the owner reconnected it) and, first, on the emulator.**
      (Choices in `DECISIONS.md` 2026-11-15 "M47 B8".) Under Big Type only, from shared maths (`BigTypeFolderMotion`,
      `BigTypeLaunchMotion`) on the native screens and the Avalonia ones: **a folder opens** as the mock's `openFolder` (home's tiles fall
      away from it, the games deal in at 370 ms, the name's letters land, the count rolls in, the ring pops in on the first game) and
      **closes** as its `closeFolder` (the games drop out, home pops back in around the folder's tile, the ring lands on it), all on the
      native Launcher with no new pictures (the name's letters from one atlas, as B6's); **A on a game plays** through the Title page's
      `playWhenReady` route, never drawn: a console set to ask shows "Which screen?" as a B7 sheet (plus "Always the bottom", which today's
      chooser can do), a missing file does not launch and the Stage's line says so and shakes, the disc is a row of the Y sheet that
      cycles; **the launch**: the tile flies up through the hinge, the case bumps, the console's colour takes the Stage over through a
      circle with the title's letters landing and the status rolling from the app's own steps (starting, syncing saves, running, an
      unlock), the Launcher floods to the in-game colour and its grid goes; **the companion**: Now playing, the title, Session /
      Achievements / Photos (the time counting each second, the totals, the figure and the three latest unlocks, six photos, the bar),
      parts rising in and dropping out, tabs sliding, resting after 9 s, today's dock, badge grid, photo viewer and All apps kept and
      reachable; **the return**: the parts leave, the colour fades, the ground floods back from the middle, the tiles pop in. Held still,
      everything is in place at once. tokens.js gained the mock's inline numbers for all of it. **Verified on macOS:** Classic at the final
      code is byte-identical to Classic at the starting commit `3479cc4` on all 158 frames; two Big Type renders of the final code are
      byte-identical on all 158; 44 of 158 Big Type frames differ from the starting render (16 folder frames by the folder's name, now
      the landing heading, a few pixels; the 25 Title-page frames; the 3 in-game companion frames), listed in the build sessions' notes
      (`m47-b8-renders/bigtype-diff-vs-start.txt`, the final set beside it); the Title page's frames keep their names and under Big Type show the
      game focused with its facts, its Y sheet, "Which screen?" and the launch at rest. New tests: `HandheldBigTypeFolderLaunchMotionTests`
      (tokens.js and every move at fixed moments, held still) and `HandheldShellViewModelTests+BigTypePlay` (A plays with no page drawn and
      the status following the launch and the save sync, the missing file, the screen sheet and its B, today's look still opening the page,
      the launch title's size and lines, the companion's places and tabs). `dotnet build` and `dotnet test` passed three times running
      (1,407 + 1,665 each time). **Emulator check (first, while the owner had said to use it; `emushelf-b3`, Release build without
      AOT):** with `bigtype`,
      recordings of both displays caught the PS2 folder opening (home falling away, the games dealing, the name's letters landing, the
      count, the ring popping in) and closing (games dropping, home popping in, the ring back), A on a game of a console set to ask showing
      the "Which screen?" sheet, and the launch from it: the tile flying, the Launcher flooding dark, the grid going, the Stage's circle,
      title letters and "Starting ARMSX2…"; the emulator has no PS2 emulator installed, so the launch failed at once and the shelf came
      back (the colour fading, the ground flooding back, the tiles popping in). Found there and fixed: a launch from the sheet never flew
      on the native Launcher. With `classic`: the folder, the Title page, its chooser and the failed launch's message as before; set back
      to `bigtype`. **Not checked on any device:** a launch that runs (so the companion in a game, its tabs, rest and wake, an unlock, and the
      return from a game), a missing file (the emulator's library has none), A on a console that does not ask, holding still, colour,
      frame times and feel. **Thor check (then; Release AOT, `adb install -r`, uid 10211 kept, 60 Hz untouched, both displays recorded):**
      with `bigtype`, the GBC folder opened and closed natively as on the emulator; every console on the owner's Thor is set to ask, so
      A on a game (GBC, GBA, PSP) showed "Which screen?" each time, and a launch that runs straight away could not be seen without
      changing the owner's settings; "Top screen" from it launched Donald Duck in RetroArch and Bleach in PPSSPP: the sheet's rows left,
      the tile flew, the Launcher flooded to the in-game colour and its grid went, the Stage's circle, letters and "Starting …" status
      showed until the emulator covered the top screen, and the companion arrived on the bottom (Now playing, the timer counting each
      second, the total, 0 / 18 and 0 / 60 achievements, nine photos of the game, the dock, "Quit to shelf"); each tab by touch (keys go
      to the game), the rest after 9 s and a touch waking it; leaving the game (force-stopping the emulator, nothing saved) the parts
      left, the colour faded, the ground flooded back and the tiles popped in. Found there and fixed: the companion flashed on the top
      screen while its parts left, and the Achievements tab kept "Loading achievements…" once the set had loaded (both checked again on
      the Thor); after the orchestrating session's review, the companion's descenders were cut by each line's own clip (fixed, and seen
      whole on the Thor). With `classic`: the folder, the Title page, its chooser cards, a launch with today's companion and the way back as before.
      Left installed with `debug.emushelf.look` `bigtype`. **Not seen on any device:** a launch with no screen question, a missing file,
      an unlock while playing, holding still.
- [ ] **B9 — Typing and moving.** Naming and Search with letters landing on the Stage; pick up and put down.
      **Built 2026-11-15; awaiting the owner's look. Checked on the Android emulator (`emushelf-b3`), not the Thor: the Thor was not on adb
      for this step.** (Choices in `DECISIONS.md` 2026-11-15 "M47 B9".) Under Big Type only, from shared maths (`BigTypeTypingMotion`,
      `BigTypeMoveMotion`): **pick up and put down** are the mock's trades (the held tile grows to `HeldScale` above its neighbours, its face
      rocks, the ring hides, each direction trades places with the next slot, across pages, every step remembered; A puts it down with the
      squash, B walks every step back; Stage "Moving" / "It trades places with what it passes", hints A Drop, B Put back); today's merges
      are on Y while a tile is held (Put in a folder of yours, or a new one; Settings or Activity into a folder). **The keyboard** is the
      mock's for every field the app types into (naming and renaming a folder, Search, Edit title, Settings' text rows, the scrapers'
      fields, cover search's "Search for"): its heading lands, the keys rise in at the mock's places, the ring lands on the first key after
      260 ms, a focused key is white with the ground's colour, a pressed one pulses; the app's digits and symbols are on two pages behind a
      key at the end of the third row (RB), Shift where a field keeps its own case (LB), a rename's colours in a row above the letters. The
      **typed word is the Stage's name**: only new letters drop in, a deleted one falls off the end, the word resizes, the placeholder at
      0.34, the name before it leaves upward (native Stage and Avalonia alike). The mock's typing rules at the app's limits (24, 256).
      **Search** shows up to five hits above the keys (rebuilt only when the games change), the pad goes between them and the keys, a hit
      is the game on the Stage (the ground floods to it), A plays it through B8's route; Done with matches still opens them all as a folder.
      Closing, the keys drop and 290 ms later the layer under is back, a folder made is born on its slot. The native Launcher now waits for
      what only the Avalonia Launcher draws (`ScreenHoldRemaining`) and lands its ring and ground on what it finds. tokens.js gained the
      mock's inline numbers for all of it (two numbers are the app's and say so). **Verified on macOS:** Classic at the final code is
      byte-identical to Classic at the starting commit `1eae802` on all 158 frames; two Big Type renders of the final code are
      byte-identical; 4 of 158 Big Type frames differ from the starting render (Search empty and typed, both screens: the keyboard and the
      typed word), and the design set has no frame of naming or move mode, so `BigTypeTypingAndMovingShots` renders those on request (held,
      traded, put back, Put in, naming empty / typed / colours / symbols, Search empty / typed / a hit). New tests:
      `HandheldBigTypeTypingMotionTests` (tokens.js, every move at fixed moments, held still) and `HandheldShellViewModelTests+BigTypeTypingAndMoving`
      (the keys' places, the rules, Search's hits and pad, A on a hit, Edit title's Shift, trades, B walking back, across a page, Y into a
      folder, today's move mode kept). `dotnet build` and `dotnet test` passed three times running after the last change. **Emulator check**
      (Release build without AOT, `adb install -r`, `debug.emushelf.look` `bigtype`; both displays recorded): pick up from the Y sheet's
      Move, the rock, two trades and B putting everything back; A putting a tile down (and a second hold putting it back); naming on an
      empty slot (placeholder, letters dropping in, a deleted letter falling, the count, the backdrop staying); Search (hits arriving, up to a
      hit with the game on the Stage, B back to the nearest key, delete, cancel; nothing launched); a test folder "T" named, born on its slot,
      then deleted through the app's own Y sheet (twice). Found there and fixed: the Stage's backdrop changed on every key while naming;
      the native ring travelled from its old place after a put-down; the native ground flashed the old colour and flooded again after a
      folder was made. With `classic`: today's naming keyboard and today's move mode as before. Left on the emulator: this build, `bigtype`;
      its home exactly as it started (Settings, Recent, PS2, Activity). **Not seen on any device:** the Thor at all (colour, feel, frame
      times), Edit title / Settings / scraper keyboards, a rename's colours, the symbol pages, a hardware keyboard, held still, and a
      launch from a Search hit. Renders, recordings and strips: `m47-b9-renders` in the build sessions' notes.
      **Review fix (2026-11-15):** the Launcher's "X Swap" corner showed under a keyboard while X was Done; it now shows only while X
      really swaps (`XSwaps`: the grid, a folder, its header, the dock), on both Launchers, and goes under sheets, the menu, Settings,
      panels, keyboards, a held tile, a launch and Big Type's unseen Title page. Classic byte-identical on all 158 frames; two Big Type
      renders identical; 32 Big Type bottom frames differ from f47f67c, each only in the corner (every sheet, menu, Settings, panel and
      keyboard frame). Checked on the emulator (the Thor was not on adb): the corner at home, gone under naming, Search and a held tile,
      back after each; home left as found.
      **First Thor pass (2026-11-15, from B10a's session; Release AOT, `adb install -r`, 60 Hz, no system setting changed,
      `bigtype`):** pick up from the Y sheet's Move (the tile grown, the ring hidden, "Moving" / A Drop, B Put back), one trade and B
      walking it back; a held tile's Y opening "Put in" and B out twice; naming on an empty slot (the keys and the colours row, the
      ring on the first key, typed letters dropping into the Stage's name with the word resizing and the count rolling, a deleted letter
      going, the two other character pages, the placeholder back when empty, B out; no folder made); Search from the Start menu (hits
      above the keys, Up to a hit showing the game on the Stage with A Play / B Back and the ground flooding, B back to the keys, delete,
      B out; nothing launched); the "X Swap" corner absent under the keyboards and a held tile and back at home. **Seen wrong on the
      Thor, not on macOS:** while naming, the ring sat roughly 15–20 px down and right of the focused key (E, L, 123, the page key), at rest
      more than a second after the press; on Search it was centred on T, then off again after B back from a hit. B9's macOS render has
      it centred. Fixed the same day (below). Pacing, from the screen recordings (the recorder's own frames, so rough): the keyboard opening
      and the held tile's rock drew new content in 94–100% of recorded frames. Not seen: a hardware keyboard, Edit title / Settings /
      scraper keyboards, a rename's colours, a launch from a hit, held still with the keyboard. Evidence: `m47-b10a-renders/thor`.
      **The ring fault, found, fixed and re-checked on the Thor (2026-11-15):** the cause was B5's breath, not the key's place. The
      compositor applies a visual's Scale about its CenterPoint after the control's RenderTransform, and the Avalonia ring is placed
      by a translate, so the breath (×1.03, centred on half the ring's size) scaled about the Launcher's top left and the ring sat 3% of
      its place away from its key while the Avalonia Launcher had the screen; desktop frames never include compositor animations. It
      now breathes about its own middle (`DECISIONS.md` "M47 B10a", last bullet). A new test renders through the compositor with the
      breath held at ×1.5 in naming (first key, E, a colour, back from the colours, both other pages, L) and Search (no hits, hits,
      back from a hit) and fails without the fix. On the Thor, two shots half a breath apart each showed the ring centred on Q, E, a
      colour, E after the colours, `#+=`, `ABC`, L, and in Search on Q, on C with hits and on E after B back from a hit. Not
      re-checked on the device: Edit title, Settings fields and the scrapers' keyboards (same ring and breath code).
- [ ] **B10 — Make it the look.** A reduced-motion setting beside the system one; the owner's ear on the sounds
      with the new motion; Big Type becomes the default and D5 is carried out; `HANDHELD-MODE.md` decision 16 and the
      motion spec are rewritten to match. **Exit:** a week of normal use on the Thor with nothing that reads as the
      old look.
      **First part built 2026-11-15 (B10a); not ticked. Waiting for the owner:** making Big Type the default, carrying out
      D5 (removing today's look), the sounds, and the week of use. Nothing of those was done. (Choices in `DECISIONS.md`
      2026-11-15 "M47 B10a".) **Built, under Big Type only:** the app's own **Reduce motion** switch in Settings → Display, after
      Sounds ("everything arrives in place at once and the backdrop holds still · Android's own setting does this too"), off by
      default, saved as `AppSettings.HandheldReduceMotion` in the portable `Settings/` folder; it joins the system's say in
      `HoldsStill` (either one is enough), so every B5–B9 move takes its held-still form at once on the Avalonia screens and both
      native compositors, with no restart; the edge bump gained a held-still form (none). **The sweep:** the app's messages are
      the Stage's line under the name (the mock's toast: an error shakes and holds `Message.errorHoldMs`, work running holds,
      a long one runs to two lines), today's pill left only where the Stage has no name; a folder's name keeps `HeaderGap` (new
      in tokens.js) from its count; Activity's game page no longer draws its two lines of facts over the line. Left, with sizes,
      in the decision: the companion's All apps, badge grid and photo viewer; the drift's fan-art hero; the dock app's colour
      sweep; the native Stage's grey ground before the first backdrop; the empty library; the edge bump's numbers, the screen
      swap and the medium's sway. **Documents:** `HANDHELD-MODE.md` decision 16 and the motion spec describe Big Type, behind
      `HandheldLook` (default Classic), today's text kept and marked superseded. **Verified on macOS:** Classic at the final code
      is byte-identical to Classic at the starting commit `4eafe18` on all 158 frames; two Big Type renders of the final code are
      byte-identical; 6 of 158 Big Type frames differ from the starting render (the three message frames' tops, Activity's game
      top, and Settings › Display's two bottoms with the new row). New tests: `HandheldShellViewModelTests+AppReduceMotion` (B5–B9's
      holding-still tests asked by the switch instead of the device: the sway, the drift, both native screens and the Avalonia
      Launcher, a launch and the companion, either one enough, both off), `MainViewModelTests+ReduceMotionSwitch` (at once, saved,
      read back after a restart; Classic offers no row and ignores a saved one), `+BigTypeSweep`, the Display row. `dotnet build`
      and the full `dotnet test` passed three times running after the last change. **Emulator check, not the Thor** (the Thor was
      not on adb; `emushelf-b3`, `-no-snapshot-save`, Release build without AOT, `adb install -r`): Classic as before (Display
      has no new row; a notice is today's pill); under `bigtype` the row shows Off, A turns it on (saved `true` in
      `Settings/settings.json`), and over the same four home presses the Launcher changed in 1–2 frames per press and the Stage in
      2–4 (held still) against 6–7 per press and a moving Stage with it off; turned back off (saved `false`); "Sounds off" rolled
      onto the line and back with no pill (Sounds turned back on). Left there: this build, `debug.emushelf.look` `bigtype`, home
      as found (Settings, Recent, PS2, Activity). **Not seen on any device:** the Thor at all (B9's first Thor pass included), the
      system's own reduced-motion switch with this build, a failed launch's message, a long folder name. Renders and recordings:
      `m47-b10a-renders` in the build sessions' notes.
      **Thor pass (2026-11-15; Release AOT, `adb install -r`, 60 Hz, no system setting changed; the owner's library):** under
      `bigtype` Settings → Display shows "Reduce motion" Off; on (saved `true` in the owner's `Settings/settings.json`), over the same
      home presses the Launcher changed in 2–3 recorded frames a press and the Stage in 3–4, nothing moved between presses (the
      backdrop held), and two presses into the left wall changed nothing; off again (saved `false`) with no restart, the moves ran
      8–12 frames a press, the wall nudged and the backdrop moved. "Sounds off" rolled onto the Stage's line and back with no pill
      (Sounds put back on). Message roll on the native Stage, by the app's probe (`debug.emushelf.probe`, then cleared): 36 frames
      in 558 ms and 34 in 535 ms, frame gap median 16.4 ms, worst 21.4 ms, render-thread CPU p95 2.4 ms, GPU 1.06 ms. Under
      `classic`: Display has no Reduce motion row, a notice is today's pill, move mode and naming are today's. **Found wrong:** the
      folder header's gap does not reach the native Launcher. "Game Boy Advance" beside 106 is still drawn at the old fit, about
      10 px from its count. The Avalonia header is right, but `HandheldNativeLauncherFeed.SyncFolder` keys the name's picture and
      letters by the name alone, so the re-fit is never pictured. Not fixed: it needs the feed to re-sync on the fit, more than a
      line. **Not seen on the Thor:** the system's own reduced-motion or battery-saver switch (owner's rule: no system setting
      changed), a failed launch's message. Left on the Thor: this build, `debug.emushelf.look` `bigtype`, home byte-identical to
      before, Sounds on, Reduce motion off (the file now holds the key with its default).
      **The header fault, fixed and re-checked on the Thor (2026-11-15):** the name now fits the room its column gives it in its own
      measure pass (`FitsRoom`, the folder header only), the native feed keys the name's picture and letters by the fitted size, and a
      new fit makes the feed sync again. On the Thor "Game Boy Advance" now stands `HeaderGap` from 106, as on the desktop, and
      "PlayStation 2" beside 234 is at full size as before. A new test (the native feed's letters at the heading's fit, a later re-fit
      followed, a short name at `FolderHeading`) fails without the fix. Classic: 158 of 158 design frames byte-identical to the
      render before the fixes; Big Type: none of the 158 changes (a render matched the starting one byte for byte, and a third
      render matched both; one other render differed in three Title-page sheet frames by at most 15 levels in the faded grid, run
      to run, not reproduced). Classic's naming on the Thor as before. The presses taken as "first press only wakes" after 15 s idle
      are today's behaviour in both looks (M45 decision 14, the idle drift); one was under Classic in the first pass. Home, Sounds
      and Reduce motion left as found, `bigtype`, this build installed.
- [ ] **B12 — The model view on LB and RB.** (owner, 2026-11-15: "I want only model on rb/lb, not big type + model", "Yes, keep the
      small line", "I like one big-type mode and separate model - approve"; `docs/prototypes/handheld-big-type/proposals.html`, first
      section.) Under Big Type a game no longer shows its 3D medium beside the big name: the name view (the default) is the name alone
      across the whole name box, and on LB or RB on a focused game (in a folder or pinned to home) the model view is the medium alone in
      the middle of the Stage (1.36×, its
      floor's shadow under it), the name only the line, rising through the hinge, floating, turning once all the way round every 15 s,
      flipping into the next game's medium, turned by the stick and swinging back. On home LB / RB switch the view on a pinned game (the
      owner, 2026-11-15: "Switch the view") and turn the page on anything else; the d-pad's edges still turn it. Choices in `DECISIONS.md`
      2026-11-15 "M47 B12".
      **Built (2026-11-15), under Big Type only, in folders and on home:** the shared maths `BigTypeModelMotion` /
      `BigTypeModelState` (rise, flips, showcase, float, shadow, the stick's hold and swing back, held still, rest after a minute), the
      native Stage's `StageCompositor.ModelView.cs` (posed every frame; a wide medium set smaller by `ModelMaxWidth`), the Avalonia Stage's
      `HandheldStageMotion.Model.cs`, the shell's `HandheldShellViewModel.ModelView.cs` (texts, hints with RB / RS as rounded keys, the
      stick), the launch's colour from the medium or the name's middle. Where each piece of the old arrangement lives now, and the choices
      waiting for the owner (the view at start, the launch from the name view, the drift waiting in the model view), are in the
      decision. **Verified on macOS:** Classic byte-identical to the render before any change on all 158 design frames; Big Type: 37 of 158 frames differ from the render before (every frame with a game focused: the name across the whole box with no case or cover beside it, and RB Model in its hints; Activity's RB hints as rounded keys; under the folder's game, All apps' Stage; home's second page now turned from the tile beside the pinned game, as RB on the game switches its view), each looked at for clipped, overlapping or overflowing text and none found; two Big Type renders of the final code byte-identical; the
      model view's frames on request (`EMUSHELF_B12_SHOTS=1`: PS2 name and model, no cover, missing file, long name, PS1 three discs,
      3DS, a game pinned to home in both views); the native Stage's real media on the Mac (`tools/EmuShelf.Rendering.Preview --stage-model`: keep case, jewel case, SNES
      cartridge, DS and 3DS cards, a flat cover; centred, clear of the corners, clear at the rise's first frame, gone after leaving, still
      when held). New tests: `HandheldBigTypeModelMotionTests` (tokens, the rise, the leave from mid-rise, the showcase and float, flips
      carrying on, the stick and swing back, held still, the minute's rest, the shadow) and `HandheldShellViewModelTests+BigTypeModelView`
      (LB / RB in a folder both ways, kept across games and folders, a folder tile on home still turns the page, a sheet or the menu over
      it, a missing file, the stick, no drift over it, Classic unchanged; on home a pinned game switches the view and does not turn the
      page, any other tile turns the page and does not switch, the d-pad's edge still turns the page from a pinned game, and from a pinned
      game's model to a folder tile and back the model returns and carries into a folder). `dotnet build` and the full `dotnet test` passed three times running after the
      last change. **Thor (Release AOT, `adb install -r`, replacing B11's build; 60 Hz; no system setting changed; the owner's library):**
      the name view with RB Model; RB rises the PS2 keep case through the hinge, it floats and turns once all the way round (its back);
      single presses flip into the next game, a held direction carries on through several; LB lands the name; the PS1 jewel case, the
      SNES cartridge and the DS card in the model view (the jewel case touched the battery under the owner's 12-hour clock until
      `ModelMaxWidth` came down to 720); the choice carried into another folder; Reduce motion on: at rest, views at once, no blank frame
      after a fix, off again; Classic: RB in a folder does nothing. Frame probe: the rise 37 frames after the press frame, interval median
      16.5 ms, p95 19.4, worst 20.7; flips 429 frames, median 16.6, p95 20.0, worst 28.7 (one); render-thread CPU p95 2.6 ms, GPU p95
      2.2 ms. The medium's first appearance in a fresh process: 16.6 ms for its first 3D render (B6 measured 8 to 19 ms) after a 25.7 ms
      first apply on the press frame (78.6 ms gap, once per process; a later rise's press frame 5.8 ms). Found and fixed there: the idle
      drift took the Stage at 15 s with the medium drawn over its fan art (the drift now waits in the model view), and a held-still frame
      with neither medium nor name. **Not seen on the Thor:** the right stick and its swing back (adb cannot move an axis; Mac renders and
      tests only), the launch from either view (no games launched), a platform with no authored medium (every console on the owner's
      Thor has one), a game pinned to home (the owner's home has none, and pinning one would change it; the home rule is held by the tests
      and the frames on request). **Waiting for B11:** the model view on the narrow Stage (screens swapped) and B11's held-direction rule for the flip.
      Renders, recordings and the probe log: `m47-b12-renders` in the build sessions' notes. Home, the swap, Sounds (on) and Reduce
      motion (off) as found, `bigtype`. This build replaced the one installed at 18:27; another session installed its own build at
      19:18 and again at 19:25 (with its probe on `rest`), so the Thor now runs that one, not B12's.
      Built, awaiting the owner's look.
