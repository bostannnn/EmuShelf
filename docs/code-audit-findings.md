# EmuShelf code audit — real problems

_Produced 2026-08-04 on branch `claude/app-problems-analysis-fv05q2`._

This is a defect audit of the whole `src/` tree (~50k LOC across Core, Infrastructure,
Integrations, App). The goal was to find **real, verifiable problems** — bugs that
produce wrong behavior, data-loss/robustness risks, resource leaks, and cross-platform
breakage — not style nits.

> **Fix status (this branch):** B1, S1, S2, S3, M1, L1, L2 have source fixes committed here,
> with tests added for M1 (filename-fallback rejection) and S1 (unreadable-unit skip). These
> are **not build-verified** — no .NET 10 SDK was available in the environment — so run
> `dotnet build` + `dotnet test` before merging. Note the initial S1 attempt also guarded the
> *apply* loop, which would have swallowed the corrupt-download / offline-remote integrity
> errors that `SaveSyncServiceTests` deliberately asserts propagate; the committed fix guards
> only the planning-loop local snapshot. The remaining Low-severity items are not yet addressed.

## How this was produced

1. **Fan-out (6 agents).** One deep-read pass per high-stakes subsystem: Save-sync/cloud,
   emulator launching, binary parsing (ROM/disc/CHD/hashing), metadata scraping/HTTP,
   App UI/threading, and persistence/storage. Each traced real control flow and reported
   concrete defects with `file:line` + a failure scenario.
2. **Adversarial verification (24 agents).** Every candidate finding got an independent
   skeptic that re-read the actual code and tried to **refute** it, returning
   CONFIRMED / PLAUSIBLE / REFUTED plus a corrected severity. **4 candidates were refuted
   and dropped**; several were downgraded.

> ⚠️ **Not build-verified.** No .NET SDK was available in the audit environment, so the
> project was **not compiled and the tests were not run**. Findings are from static
> reading (the two test projects were read as documentation of intended behavior). Confirm
> each fix with `dotnet build` + `dotnet test` before shipping.

## What was checked and found **sound** (the negative result matters)

- **Cloud-save conflict reconciliation is non-destructive** — the planner backs up the
  losing side before any overwrite, non-conflict overwrites only replace a side that still
  equals the baseline, and writes are hash-verified and fail closed on mismatch. No
  data-loss/corruption defect was found in the reconciliation core.
- **App threading discipline is careful** — `Progress<T>` created on the UI thread,
  Dispatcher marshaling for cross-thread events, generation counters guarding stale
  reloads, single-flight semaphores. No UI-thread-mutation violations or deadlocks.
- **Persistence fundamentals** — SQL is parameterized (no injection), multi-statement
  writes are transactional, connections/commands disposed; **removing a game touches DB
  rows only, never user files**; DPAPI is gated behind `OperatingSystem.IsWindows()` with
  an AES-GCM portable fallback for macOS/Linux.
- **Launch argument templating** — tokenize-then-substitute keeps spaces/unicode/quotes in
  paths as single argv entries; the frontend window is always restored via `finally` even
  on emulator crash; a failed start is never reported as success.
- **The user-facing artwork/cover path** is correctly SSRF-hardened (address-pinned
  transport, no auto-redirect, content-type/size/signature validation).

## Summary

| Severity | Count | IDs |
|---|---|---|
| High | 1 | B1 |
| Medium | 6 | M1, L1, S1, S2, S3, L2 |
| Low | 13 | M3, P1, P2, A1, S4, S5, M4, M5, M6, P3, P5, L3, A2 |
| Refuted (dropped) | 4 | M2, P4, A3, B2 |

---

## High

### B1 — RVZ junk-region PRNG corrupts every 4th byte, breaking achievement hashing
- **File:** `src/EmuShelf.Integrations/Achievements/NintendoDiscImageReader.cs:1349`
- **Defect:** `RvzLaggedFibonacciGenerator.WriteBytes` maps intra-word byte lane 1 to
  `word >> 18`; the other lanes use `24/8/0`. Big-endian byte extraction requires lane 1 =
  `word >> 16`. So every 4th regenerated "junk" byte is wrong. Confirmed reachable: the
  `isJunk` branch in `TryUnpackRvz` constructs this generator to fill the reconstructed
  disc image used for hashing.
- **Failure scenario:** Import a Dolphin-produced `.rvz` GameCube/Wii game whose disc has
  junk padding inside the hashed region (Wii hashing reads the first ~1024 clusters of each
  data partition; Nintendo discs fill unused sectors there with exactly this pattern).
  `WiiDiscHasher`/`GameCubeDiscHasher` feed corrupted bytes into MD5 → the hash never
  matches rcheevos → **RetroAchievements silently fails to identify the game**, with no
  error. `.iso`/`.ciso`/`.wbfs` are unaffected; existing RVZ tests only exercise the
  non-junk branch, so nothing catches it.
- **Fix:** change the `1 =>` arm from `word >> 18` to `word >> 16`. Add a test with a
  junk-marked RVZ segment.

---

## Medium

### M1 — ScreenScraper hash/serial scrape can silently apply the *wrong* game
- **File:** `src/EmuShelf.Infrastructure/Metadata/ScreenScraper/ScreenScraperClient.cs:68`
  (+ `309-317`); `ScreenScraperPreviewService.cs:97`
- **Defect:** every `jeuInfos.php` request also sends `romnom` (the filename) in *addition*
  to the hash/serial, never as a replacement. `ParseGame` reads only `game.id`/`rom.id` and
  never the returned ROM's crc/md5/sha1 (`ScreenScraperGameInfo` has no field to even carry
  it), so nothing verifies the returned ROM matches what was queried. `BuildPreviewAsync`
  stamps `matchMethod = Sha1` regardless of how ScreenScraper actually matched.
- **Failure scenario:** a renamed file, rom-hack, or bad dump whose SHA-1 is absent from
  ScreenScraper's DB but whose filename matches a retail title → ScreenScraper's documented
  `romnom` fallback returns the *original* game, and EmuShelf records it as an exact SHA-1
  match. In Overwrite/batch mode it clobbers correct metadata/cover with the wrong game's.
  (Downgraded from High: stays inside EmuShelf's own DB — never touches user game files.)
- **Fix:** parse the returned `rom` crc/md5/sha1; for hash/serial-initiated lookups reject
  the result unless the returned hash/serial equals what was queried.

### L1 — Flatpak RetroArch: core path not granted to the sandbox → silent load failure
- **File:** `src/EmuShelf.Core/Launching/EmulatorLaunchService.cs:186` (grant builder
  `209-222`)
- **Defect:** for sandboxed Flatpak launches, `BuildReadOnlyFilesystemGrants` iterates only
  the game-descriptor paths from `_dependencyResolver.Resolve(game)`; `configuration.CorePath`
  (expanded separately into `-L {CorePath}`) is never added to the `--filesystem=…:ro`
  grants. The only core preflight is a host-side `File.Exists`.
- **Failure scenario:** RetroArch (`RequiresCorePath: true`) paired with a Flatpak target,
  core at `…/Emulators/RetroArch/cores/mgba.so` outside the sandbox's default-visible dirs
  (the norm for portable installs). Preflight passes, but the sandboxed emulator can't read
  the core dir and fails to load it — a silent post-launch failure. (Linux/Flatpak-only, so
  Windows v1 and the macOS build are unaffected → Medium.)
- **Fix:** include the core file's directory in the read-only `--filesystem` grant set for
  Flatpak launches.

### S1 — One symlinked/locked save folder aborts the *entire* multi-platform sync
- **File:** `src/EmuShelf.Core/SaveSync/SaveSyncService.cs:88-102`
  (throw origin: `src/EmuShelf.Infrastructure/SaveSync/FileSystemLocalSaveEndpoint.cs:266-267,287,297`)
- **Defect:** the up-front planning loop wraps `SnapshotAsync` in a `try` whose only `catch`
  is `SaveUnitNotResolvableException`. But `EnumerateAllFolderFiles` throws
  `InvalidDataException` on any reparse point/symlink inside a folder-save, and `HashFile`
  throws `IOException` on a file locked by a running emulator. All units are planned before
  the first transfer, so one throwing unit propagates out and aborts `SyncAllAsync` for
  **every** platform, on every run. (Reachable on Windows too via NTFS junctions or a locked
  save — not just Steam Deck.) At the coordinator, `InvalidDataException` isn't in the catch
  filter either, so it escapes as an unhandled exception.
- **Fix:** in the plan loop (and apply loop) catch `InvalidDataException`/`IOException`
  per-unit and record the unit as `Skipped`, like `SaveUnitNotResolvableException`.

### S2 — Corrupt cloud index / hash mismatch escapes handling and shows stale "success"
- **File:** `src/EmuShelf.App/Services/CloudSaveSyncCoordinator.cs:670-672` (also `576-578`)
- **Defect:** the `when` catch filters list
  `IOException or InvalidOperationException or ArgumentException or SaveProviderConfigurationException`
  but omit `InvalidDataException` and `JsonException` (neither derives from those).
  `ReadRemoteIndexAsync` throws `InvalidDataException` on an empty/invalid/duplicate cloud
  `index.json`, and JSON deserialization throws `JsonException` on malformed input.
- **Failure scenario:** a partial upload / another client / tampering leaves the remote
  `index.json` empty, truncated, malformed, or with duplicate units → the exception bypasses
  the catch, so `RecordOutcome(failed)` never runs (the platform row keeps a stale "last
  synced" success) and it propagates out unhandled. The deliberately-written
  `ShouldForgetCloudFolderIdAfter` guard (`failure is not InvalidDataException`, exercised by
  a unit test) is dead code because no catch here ever catches `InvalidDataException`.
- **Fix:** add `InvalidDataException` and `JsonException` to both catch filters.

### S3 — Force-upload recovery aborts on a missing remote payload
- **File:** `src/EmuShelf.Core/SaveSync/SaveSyncService.cs:289-293` (`ForceAsync` upload branch)
- **Defect:** the forced-upload loop calls `BackupRemoteAsync` (→ `_remote.DownloadAsync`)
  with no per-unit `try/catch`. The download branch (`326-332`) and `SyncAllAsync`'s apply
  loop (`177-187`) both guard the identical case; the upload branch does not.
- **Failure scenario:** the user runs "Upload local → cloud" to recover from a broken
  remote. For a unit whose index lists it but whose payload is gone, `BackupRemoteAsync`
  throws `CloudPayloadMissingException`, which aborts the whole forced upload before
  `FlushAsync`/`SaveAsync` — defeating the exact recovery the feature exists for.
- **Fix:** wrap the per-unit backup+upload in a `try/catch` for
  `CloudPayloadMissingException` (nothing to back up when the remote copy is already absent —
  proceed with the upload).

### L2 — macOS `.app` bundles are rejected at launch
- **File:** `src/EmuShelf.Core/Launching/DefaultLaunchTargetInspector.cs:17`
  (and `EmulatorLaunchService.cs:113`)
- **Defect:** the preflight and inspector both reject the target with `!File.Exists(path)`;
  `File.Exists` returns false for a directory, and a macOS `.app` is a directory. There is no
  `.app` → `Contents/MacOS/<binary>` (or `open -a`) resolution anywhere in the launch path,
  and `TrackedProcessRunner` uses `UseShellExecute=false`, which can't exec a bundle even if
  the existence check were bypassed.
- **Failure scenario:** on macOS the file picker surfaces `DuckStation.app`/`PCSX2.app` as
  the natural selectable artifact; selecting it makes every launch fail as "executable not
  found." (v1 ships Windows-only, so this is a dev/macOS-target gap → Medium.)
- **Fix:** on macOS, resolve a selected `.app` to its inner `Contents/MacOS/<binary>` (or
  launch via `open -a`) before the existence/exec checks.

---

## Low

These are real but limited in blast radius (leaks reclaimed by GC/finalizer, narrow races,
theoretical-only, or masked by current callers). Worth fixing; none is urgent.

- **M3 — Uncapped API response reads.** `ScreenScraperClient.cs:184-185`,
  `RetroAchievementsWebClient.cs:144-145`, `DuckDuckGoArtworkSearchProvider.cs:46,61-62`
  read JSON/HTML with no size cap (unlike every binary-download path, which caps bytes). A
  hostile/malfunctioning upstream can spike memory / OOM the app. **Fix:** wrap in a
  length-limited read or set `MaxResponseContentBufferSize`, matching the download caps.
- **P1 — `AtomicFile` is not power-loss durable.** `AtomicFile.cs:25,39,56,75` write the
  temp file then rename, but never `Flush(flushToDisk:true)`/fsync the temp file or the
  directory. The rename protects the *old* content (its core invariant holds), but the *new*
  content isn't durable — a power cut in the write window can leave `settings.json` /
  credential blob / texture cache zero-length. **Fix:** write via `FileStream` +
  `Flush(flushToDisk:true)` before rename; optionally fsync the directory.
- **P2 — Portable-path drive-boundary check is a no-op on macOS/Linux.**
  `RelativePathResolver.cs:19-27` uses `Path.GetPathRoot`, which returns `/` for every path
  on Unix, so off-drive game paths get stored *relative* instead of absolute and re-resolve
  to the wrong location after the drive moves. Handled correctly on Windows (diverges by OS;
  a portability-rule violation on the macOS build). **Fix:** only relativize when the target
  is actually under `BaseDirectory` (or compare real mount points); else persist absolute.
- **A1 — Scraper preview bitmaps leak on close.**
  `GameScraperViewModel.cs:728-732` `Dispose()` never calls `ClearRows()`, the only place
  the decoded Media-row bitmaps are disposed, so every scrape strands 2–5 small Skia bitmaps
  until finalization. **Fix:** call `ClearRows()` inside `Dispose()`.
- **A2 — Cover-search bitmaps leak on cancel.** `CoverSearchViewModel.cs:130-153` — a
  preview decoded but not yet inserted into `Results` when the search is cancelled (retype /
  close / select) is dropped undisposed; `ClearResults`/`Dispose` only reclaim bitmaps in
  `Results`. **Fix:** dispose any result not added to `Results` in the cancellation path and
  the `finally`.
- **S4 — Rclone staging dirs leaked on the error path.**
  `RcloneCloudSyncTransport.cs:288-302` cleans `outbox`/`inbox` only in `FlushAsync`'s
  `finally`; a pass that throws before `FlushAsync` leaves staged save copies under
  `Saves/transfers/` that accumulate across failed passes. **Fix:** sweep stale
  `outbox-*/inbox-*/index-*` dirs on transport construction, or dispose/flush in a `finally`.
- **S5 — Rclone installer staging file leaked if the final move fails.**
  `RcloneInstaller.cs:41-43` — if `File.Move(staged, destination)` throws (destination locked
  by a running rclone, AV, read-only volume), the `.download` staging file is left beside the
  executable (the `finally` deletes only the temp zip). **Fix:** add the staged file to the
  cleanup `finally`.
- **M4 — Per-game semaphores never evicted.**
  `ScreenScraperFingerprintService.cs:12,39` accumulates one `SemaphoreSlim` per game id in a
  `ConcurrentDictionary`, never removed. Growth is bounded by library size and no native
  `WaitHandle` is ever allocated, so impact is small. **Fix:** evict/dispose the gate after
  its critical section (or a ref-counted keyed lock).
- **M5 — Badge cache can double-download.** `RetroAchievementsBadgeCache.cs:70` —
  `ConcurrentDictionary.GetOrAdd(key, factory)` doesn't serialize the factory, so two racing
  first-callers can each fetch the same badge (contradicting the class's stated coalescing).
  Harmless (writes are atomic), just a wasted download slot. **Fix:** insert a
  `Lazy<Task>`/`TaskCompletionSource` placeholder so the fetch starts once.
- **M6 — Metadata artwork downloader lacks the SSRF guard.** `App.axaml.cs:73-87` builds
  `RemoteArtworkDownloader` for the automatic pipeline with no `IRemoteArtworkUriPolicy` and
  a handler at `AllowAutoRedirect = true`, so it auto-follows redirects with no per-hop
  vetting. Today all candidate URLs are fixed reputable hosts, so exploitation needs an
  open-redirect/compromise — but the guard present on the user-facing path is absent here.
  **Fix:** pass `publicArtworkPolicy` and route through the pinned `PublicArtworkHttpTransport`
  handler (`AllowAutoRedirect = false`).
- **P3 — Concurrent first-run DB migration crashes the second process.**
  `LibraryDatabase.cs:55-136` reads the schema version once, and migrations from V2 use bare
  `ALTER TABLE ADD COLUMN` / `CREATE …` (no `IF NOT EXISTS`), each in its own transaction.
  Two processes on a shared/portable drive doing a concurrent first run both see version 0;
  the second hits "duplicate column name" and fails to start (data not lost). **Fix:** run
  the whole ladder in one `BEGIN IMMEDIATE` transaction (re-read the version after taking the
  write lock), or make each step idempotent.
- **P5 — Tri-state `IsPresentInExternalSource` collapsed to `true` for local games.**
  `SqliteGameMetadataStore.cs:293` uses `IsDBNull(11) || GetInt64(11) != 0`, mapping NULL
  (local) → `true` instead of `null`, diverging from `GameLibrary.ReadGame` (which yields
  `null`). Currently masked because the only consumer guards on `IsExternalSourceGame`, but
  any future consumer misclassifies local games. **Fix:**
  `IsDBNull(11) ? (bool?)null : GetInt64(11) != 0`.
- **L3 — Latent stderr pipe-full deadlock (PLAUSIBLE).**
  `FlatpakLaunchTargetInspector.cs:69-71` redirects both stdout and stderr but only drains
  stdout via a blocking `ReadToEnd()` before `WaitForExit()`. The one call site
  (`flatpak info`) never writes enough to stderr to trigger it, so it can't be shown to fire
  today — but the pattern is a real latent deadlock (the sibling `FlatpakApplicationDiscovery`
  avoids it). **Fix:** read stderr concurrently, or don't redirect it if unused.

---

## Refuted on verification (dropped — documented so they aren't re-flagged)

- **M2 — Libretro DAT-catalog shared cancellation token.** The token *is* bound to the first
  caller, but all concurrent same-system callers in a run share one run-wide token and runs
  are serialized by `_runLock`, so no game is ever cancelled while its own token is live; the
  on-disk cache is written atomically and survives. Not reachable.
- **P4 — Case-insensitive unique path index.** Deliberate and documented
  (`DECISIONS.md`, 2026-07-12): v1's supported filesystems (NTFS, APFS/HFS+) are
  case-insensitive, so `game.cue`/`GAME.CUE` are the same file and *must* collide. The
  "case-sensitive Linux is a shipping target" premise is false per `CLAUDE.md`.
- **A3 — Gamepad overlay index bounds.** `GamepadOverlayOptions` and the selection index are
  always rebuilt together in `OpenGamepadOverlay`; no path shortens the list while an overlay
  is open, and other index writes are clamped. Always in range.
- **B2 — CHD parent-hunk offset uses `SectorSize` not `unitBytes`.** A real deviation from
  libchdr, but dead code for all supported inputs (parent/delta CHDs are rejected); for a
  hypothetical parent CD-CHD it only causes a harmless whole-container rejection. Fix only
  matters if parent-CHD support is ever added.

---

## Suggested order of attack

1. **B1** (one-character fix, restores achievements for all RVZ GameCube/Wii games).
2. **S1 / S2 / S3** (save-sync robustness — the subsystem where a thrown exception hurts most).
3. **M1** (metadata correctness — stop mislabeling filename matches as SHA-1).
4. **L1 / L2** if/when Linux-Flatpak and macOS launching are in scope.
5. The Low-severity leaks and hardening items as cleanup.
