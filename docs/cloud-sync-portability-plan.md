# Cloud save sync without rclone — portability plan

Status: Phase 1 (managed transport) and Phase 2 (coordinator wiring + desktop settings UI) built.
The desktop path is now reachable end to end: a user can choose the built-in Google Drive transport,
sign in, and sync, with the scope-migration warning shown at connect time. Not yet done: the first
sign-in against Google's real API and the Android transport half (custom-scheme redirect, Keystore token
store, and gamepad connect UI). The original Phase-4 SAF endpoint was ruled out for the Thor by an
on-device real-path capability probe; DuckStation and Dolphin local-save wiring has started in its place.
See "Relationship to the decision log" and the master Android plan's current-status section.

This is the save-sync half of the Android port. The master plan is `docs/android-port-plan.md`, where
this work is Milestone E; the detail lives here rather than being duplicated there.

## NOT DONE YET — do not treat as finished

Stated plainly so it is not mistaken for shipped:

1. **Gamepad mode does not support the built-in transport.** The gamepad Saves section is rclone-shaped
   and is built with `allowManagedTransport: false`, so in gamepad mode the built-in Google Drive
   transport cannot be *connected* at all — the user only sees the rclone flow. An existing built-in
   connection made in Desktop mode keeps syncing (launch/exit sync and "Sync all now" are
   transport-agnostic), but you cannot set one up, and you cannot reconnect after a token revocation,
   without Desktop mode. **The gamepad Saves section needs a full rebuild** (a transport chooser + a
   controller-native connect flow). This is the same rebuild the Android head requires, and the Thor is
   gamepad-only — so on Android today, as written, the built-in transport would be *unreachable*. This
   must be done. Tracked as Phase 3 below and Milestone E-android in the master plan.
2. **No real Google API call has ever happened.** Every test runs against an in-memory fake Drive.
3. **Built and tested on macOS only.** Windows and Linux are shipped targets but were not exercised for
   this change; the browser launch and the refresh-token-at-rest path differ per OS and are unverified.
4. **Phase 3's Android transport/UI work is unstarted. Phase 4 was reshaped by hardware:** the Thor can
   use the filesystem endpoint inside `Android/data`; DuckStation is device-verified and Dolphin fixed-root
   wiring is implemented/tested but still needs an on-device export/restore. Folder-configurable emulator
   overrides remain.

## Why

Save sync currently runs through an external rclone binary. That is fine on Windows, macOS, and
Linux, and it cannot work on Android at all:

- Android blocks executing a binary downloaded at runtime. `RcloneInstaller` fetches rclone into
  the portable app directory and runs it from there; on Android the exec is denied outright,
  regardless of permissions.
- Every rclone call in `RcloneCloudSyncTransport` and `RcloneConfigurator` is a `Process.Start`.
  There is no process to start.

An Android port therefore needs a transport that speaks to the provider over HTTP from inside the
app. That transport is also strictly better on the three desktop targets, so this is worth doing
whether or not the Android port happens.

## What the research found

Six findings shaped the design. Three of them changed it.

**1. The layering is already right.** `ICloudSyncTransport` (`src/EmuShelf.Core/SaveSync/ICloudSyncTransport.cs`)
is a five-method interface and nothing above it knows rclone exists. `SaveSyncService`,
`SaveSyncPlanner`, the manifest, and the conflict logic are all transport-agnostic. This is a swap,
not a rewrite.

**2. The OAuth objection is already dead.** DECISIONS 2026-07-24 chose rclone *specifically* so that
EmuShelf would embed no OAuth client secret ("it would be extractable from a GPL binary"). That
rationale no longer holds: DECISIONS 2026-08-04 and 2026-08-06 reversed it. The build already bakes
`EMUSHELF_GOOGLE_OAUTH_CLIENT_ID` / `_SECRET` into `EmbeddedSecrets` and hands them to rclone, and
the "import your own client JSON" path was deliberately removed in favour of one shipped
application-identity client. So a managed client needs **no new credential story** — it consumes
exactly what is already there.

**3. The multi-backend benefit is mostly unrealized.** The other stated reason for rclone was reach:
Drive plus Dropbox/OneDrive/S3/WebDAV. In practice `CloudSaveSyncCoordinator.ConnectGoogleDriveAsync`
only ever calls `RcloneConfigurator.CreateGoogleDriveRemoteAsync`. There is no UI for any other
backend. The one real escape hatch is that `CloudRemoteName` is a user-editable field, so someone
who hand-writes `Settings/rclone.conf` can point EmuShelf at any remote they like. That path is
undocumented but real, and it is the reason to keep rclone rather than delete it.

**4. The wire format is the actual contract.** Both transports address the same two things: one
`index.json` holding `{UnitId, ContentHash, ModifiedUtc, Compatibility}` per unit, and one
`<unitId>.payload` blob per unit. If the managed transport reproduces that format byte-for-byte,
the two transports are interchangeable against the same cloud folder — which is what makes a staged
rollout and a fallback possible. This is a hard requirement, not a nicety.

**5. Scope choice creates a migration problem.** *(changed the design)* rclone's Drive backend
requests the full `drive` scope. A managed client should request `drive.file`, which is
least-privilege — EmuShelf can only ever see files it created, never the rest of the user's Drive —
and which avoids the restricted-scope verification burden Google places on `drive`. The cost is that
a folder created by rclone under the full scope is **invisible** to a `drive.file` client. A user
switching transports would see an empty remote and re-upload. Because the transport is copy-only and
conflicts are backed up, nothing is destroyed — but another machine's saves become unreachable until
that machine re-uploads. This must be stated in the UI at switch time, not discovered.

**6. Android needs a second OAuth client.** *(changed the design)* Google issues OAuth clients per
platform type. The desktop client (id + secret, loopback redirect `http://127.0.0.1:<port>`) cannot
be used from Android; an Android client has **no secret**, uses a custom-scheme redirect, and is
bound to the APK's package name and signing certificate. So the flow must support a secret-less
public client, and `EmbeddedSecrets` needs a third value. PKCE is required either way and covers
both.

A seventh, smaller finding: the batching and commit discipline inside `RcloneCloudSyncTransport`
(payloads first, index second, committed per batch so an interrupted pass keeps what it landed;
progress anchored to units rather than bytes) is transport-agnostic *policy* that happens to live
inside the rclone class. Reimplementing it in a second transport would be duplicating the hardest-won
logic in the feature. It should be extracted and shared.

## Design

### Wire format (unchanged)

```
<cloud folder>/
  index.json                     [{UnitId, ContentHash, ModifiedUtc, Compatibility}, ...]
  <unitId>.payload               opaque bytes, one per save unit
```

Extracted into `CloudSaveIndex` so both transports serialize and validate it through one code path,
with the existing validation rules preserved exactly (reject unsafe unit ids, empty hashes, default
timestamps, duplicates).

### Transport selection

`CloudSaveSyncSettings` gains `TransportKind` (`GoogleDrive` | `Rclone`), defaulting to `Rclone` so
that **an existing settings.json keeps its current behaviour untouched**. New connections default to
`GoogleDrive`. On Android, `Rclone` is not offered and the setting is forced to `GoogleDrive`.

### New components (`src/EmuShelf.Infrastructure/SaveSync/GoogleDrive/`)

| Component | Responsibility |
|---|---|
| `GoogleOAuthClient` | PKCE authorization-code flow and refresh. Handles both confidential (desktop, id+secret) and public (Android, id only) clients. |
| `IOAuthRedirectHandler` | Receives the authorization code. `LoopbackOAuthRedirectHandler` (HttpListener on 127.0.0.1) now; an Android custom-scheme handler later. |
| `GoogleDriveTokenStore` | Persists the refresh token through the existing `IProtectedTextStore` — DPAPI on Windows, the portable AES-GCM wrap elsewhere. Same pattern as the RetroAchievements key. |
| `GoogleDriveApiClient` | Thin Drive v3 REST wrapper: resolve/create folder, list, download, upload. Injectable `HttpClient` so tests use a fake handler. |
| `GoogleDriveCloudSyncTransport` | `ICloudSyncTransport` over the above, reusing the shared index and batching policy. |

The access token is held in memory only. The refresh token is the sole persisted secret, and — as
with the RetroAchievements key — is never logged.

### What stays

`RcloneCloudSyncTransport`, `RcloneConfigurator`, `RcloneExecutable`, `RcloneInstaller` and their
tests stay exactly as they are, reachable when `TransportKind == Rclone`. On Android they are simply
never constructed. This preserves finding 3's escape hatch and gives a fallback if the managed
transport misbehaves in the field.

## Phases

**Phase 1 — managed transport, desktop, not yet wired to the UI. ✅ (2026-08-15)**
`CloudSaveIndex`, `GoogleOAuthClient` + loopback redirect, `GoogleDriveTokenStore`,
`GoogleDriveApiClient`, `GoogleDriveCloudSyncTransport`, and unit tests against a fake HTTP handler.
Nothing user-visible changes. Verifiable by `dotnet test`.

See "What Phase 1 actually built" below for the three places the implementation diverged from this
plan.

**Phase 2 — wire it up. ✅** `TransportKind` in settings, transport factory in
`CloudSaveSyncCoordinator`, a managed connect flow (`ConnectGoogleDriveManagedAsync`) beside
`ConnectGoogleDriveAsync`, and `IVerifiableCloudSyncTransport` so the coordinator holds either
transport behind one type. The **desktop** settings UI now surfaces all of it: a connection-method
chooser (built-in vs advanced rclone, shown only when the build ships a client), a managed connect
flow that opens the browser and stores only the refresh token, a transport-aware connected summary,
and the switch-time warning from finding 5 (`drive.file` cannot see an rclone-created folder, so
saves re-upload). Selecting the built-in transport also suppresses the "install rclone" prompt.
The **gamepad** Saves section is still rclone-shaped and shares this one view-model's connect command,
so its host builds the view-model with the managed transport suppressed (`allowManagedTransport:
false`). Without that, a client-embedded build would silently run the browser OAuth flow behind an
rclone-looking UI (a real regression a review caught). Wiring the transport chooser into the gamepad
rows is the Phase-3 rebuild, the same one the Android head needs — see the master plan's Milestone E.

**Phase 3 — Android.** Android OAuth client in `EmbeddedSecrets`, custom-scheme redirect handler,
force `TransportKind` to `GoogleDrive`, hide the rclone UI.

**Phase 4 — reaching the saves on Android. 🟡** The Thor's all-files implementation reaches
`Android/data` with the real-path semantics `FileSystemLocalSaveEndpoint` needs, so v1 does not build a
SAF endpoint. DuckStation's fixed provider is device-verified. Dolphin's package-derived `files/` root is
wired for GameCube and Wii and deliberately reuses the existing provider's Card A/B, GCI identity, Wii
title-folder and stable unit-id logic; deterministic Android-layout tests are green, with device
export/restore pending. Next is the one-time gamepad folder override for PPSSPP, Azahar, WatermelonDS and
RetroArch. Devices that enforce stock `Android/data` isolation still report honest "unreachable here"
reasons through the existing `GetRemoteIncompatibilityReason` / `ResolveUnit`-returns-null channels.

## What Phase 1 actually built

Files, all under `src/EmuShelf.Infrastructure/SaveSync/`:

- `CloudSaveIndex.cs` — the extracted wire format. `RcloneCloudSyncTransport` was refactored onto it
  and its 212 existing tests still pass unchanged, which is the evidence that the extraction is
  behaviour-preserving.
- `GoogleDrive/GoogleDriveApiClient.cs` — Drive v3, with retry/backoff and resumable upload.
- `GoogleDrive/GoogleOAuthClient.cs` — PKCE, confidential and public clients.
- `GoogleDrive/LoopbackOAuthRedirectHandler.cs` — desktop redirect, behind `IOAuthRedirectHandler`.
- `GoogleDrive/GoogleDriveTokenStore.cs` — refresh token over `IProtectedTextStore`.
- `GoogleDrive/GoogleAccessTokenSource.cs` — token cache and renewal.
- `GoogleDrive/GoogleDriveCloudSyncTransport.cs` — the `ICloudSyncTransport` implementation.

82 new tests. The transport's tests run against an in-memory fake Drive rather than a script of
canned responses, specifically so they can assert the *resulting remote layout* — the folder tree and
file names — which is the finding-4 contract that a response script could not check.

Three divergences from the plan above, found while building:

1. **`ExpectDownloads` is served by a one-shot tree walk, not per-unit prefetch.** Drive resolves no
   paths of its own, so the transport walks the saves folder once per session and caches every folder
   and file id. A three-unit download across two emulator folders costs three listings, not one per
   path segment per unit. A test pins that count.
2. **The loopback redirect binds an ephemeral port, not a fixed one.** rclone binds 53682 every time,
   and an abandoned sign-in holding it is a documented failure mode with its own exception type and UI
   copy (DECISIONS 2026-08-06). Google exempts loopback redirects from exact port matching, so the
   managed flow picks a free port per sign-in and that entire failure class disappears. This was not
   in the plan; it is a free improvement that fell out of not using rclone.
3. **Progress is per-unit only.** The rclone transport folds each batch's byte percentage into the
   bar to keep it moving within a batch. The managed transport reports as each unit lands, which is
   already finer-grained for small saves but coarser inside a single large one. Worth revisiting only
   if a multi-hundred-megabyte unit looks stalled in practice.

Not done at the end of Phase 1, deliberately: nothing constructed these classes. `EmbeddedSecrets` was
not read, `CloudSaveSyncCoordinator` was untouched, and no setting selected the transport. Phase 2
did all of that.

### Review findings, fixed

A review pass after Phase 1 found four defects, each confirmed with a failing test before it was
fixed. All four are in the "the happy path worked, the recovery path did not" family, which is what
the original 82 tests were weakest at.

1. **Every upload retry was broken.** `StreamContent` disposes the stream it wraps, and the client
   disposes the request after each attempt — so the first 429 closed the caller's payload and the
   retry faulted on a spent stream. Surviving a rate limit is most of the reason this client is
   hand-written, so it failed exactly where it was supposed to earn its keep. The original tests only
   ever retried a *list*, never an upload. Fixed with a non-closing stream shim.
2. **An upload-only pass duplicated every blob.** Drive accepts two files with the same name in one
   folder. The create-vs-replace decision read a cache populated by the folder walk, and the walk only
   ran on download — so a repeat sync where local is simply newer (the common case) created a second
   payload each time, and which one a later sync read came down to listing order. The existing
   "replaces in place" test passed only because it downloaded first. Fixed by loading the tree at the
   start of a flush.
3. **The loopback listener treated the first HTTP request as the redirect.** Browsers fetch
   `/favicon.ico` unprompted and some prefetch before navigating, either of which failed the sign-in
   with "Google returned no authorization code". Now it answers and ignores anything not carrying the
   flow's parameters.
4. **Resumable uploads trusted the chunk length over Drive's `Range` header.** If Drive persisted less
   than was sent, the next chunk started past the gap and wrote a corrupt save that still reported
   success. Now the server's count wins whenever it sends one.

`LoopbackOAuthRedirectHandler` had no tests at all and now has six, run against a real listener over
real HTTP.

### Phase 2, and its review findings

Built: the embedded-client resolver, `TransportKind` in settings (defaulting to `Rclone` so an
existing settings.json is untouched), the transport factory, a managed connect flow, and
`IVerifiableCloudSyncTransport` so the coordinator can hold either transport behind one type.

A review of both phases found three more defects, all fixed:

5. **Connecting via rclone left the transport set to `GoogleDrive`.** `ConnectGoogleDriveAsync` never
   reset the kind, so a user switching away from the managed client would keep building a Drive
   transport — syncing against whatever account the stored token pointed at rather than the remote
   they had just configured.
6. **A revoked token escaped the sync pipeline.** `GoogleAuthorizationRequiredException` derived from
   plain `Exception` and no catch clause listed it. Automatic sync runs on the launch path, so this
   surfaced as an unhandled failure while starting a game instead of "reconnect". It now derives from
   `IOException`, which every cloud-failure handler already catches.
7. **A stale cached folder id read as an empty cloud.** Drive answers a listing for a folder that is
   not there with an empty list, not an error — so an id that had become invalid (folder deleted, or
   created under rclone's full-Drive scope and therefore invisible to `drive.file`) would report an
   empty remote, re-upload everything, and never reconcile with the machine holding the real saves.
   Nothing destroyed, nothing looking wrong. A cached id that yields no index is now re-resolved by
   path once before it is believed; a request-count test pins that the healthy path pays nothing.

Also withdrawn: a test asserting the kind is reset on a successful rclone connect. It failed for the
wrong reason — with rclone absent the method returns early and correctly touches nothing — and the
successful branch needs both an rclone binary and an interactive Google consent screen, so it cannot
be covered in this suite. Replaced with one pinning that a failed connect does not half-apply, and
the inspection-only status of the fix is recorded in the test itself.

### Merge review — five further fixes (desktop-UI landing)

Bringing this work onto the shipping branch alongside the desktop settings UI drew a fresh review of
the transport. Five more defects, each with a failing test first, all in the "happy path worked,
recovery path did not" family the earlier passes were weakest at:

1. **403 was always read as "reconnect your account".** Drive answers HTTP 403 for rate limiting as
   well as for authorization, told apart only by the `error.errors[].reason` / `error.status` token.
   The client retried only 429/5xx and mapped every 401/403 to `GoogleAuthorizationRequiredException`,
   so a large `Sync all` that merely tripped the per-user QPS limit failed with a misleading
   "reconnect" and no backoff. Now a rate-limit reason (`userRateLimitExceeded` and kin) backs off and
   retries like a 429; `storageQuotaExceeded` surfaces as "Drive is full"; only a genuine permission
   403 asks to reconnect. The 403 body is buffered and re-attached so the eventual message still reads
   it.
2. **Duplicate-blob resolution depended on listing order.** The one-shot tree walk cached the
   first-*listed* file id for a name, but Drive lists in no defined order, so two machines could
   resolve the same unit to different blobs and never converge (the transport never deletes). The walk
   now orders children oldest-first (`ModifiedTime` then `Id`, matching `FindChildAsync`) and keeps the
   oldest for a colliding leaf name. Crucially it still descends **every** same-named folder, not only
   the oldest — two machines' concurrent first-writes can leave two provider folders each holding
   different units, and a unit that lives only in the newer folder must stay discoverable rather than
   be orphaned and pruned.
3. **A stuck resumable upload could loop forever.** A `308` whose `Range` did not advance was re-sent
   with no cap and no backoff. A forward-progress guard now fails the upload instead of spinning.
4. **A date-form `Retry-After` was dropped.** Only the delta-seconds form was read; an absolute
   HTTP-date now counts too, so the server's requested wait is honoured either way.
5. **A pre-cancelled sign-in reported as a connect failure.** A token already cancelled on entry (or
   cancelled between the favicon/prefetch and the next accept) made the loopback listener throw
   `InvalidOperationException`, which escaped as "connect failed" rather than a cancellation. It now
   throws `OperationCanceledException`.

The desktop UI's own connect path got the matching bound: the built-in sign-in runs under a timeout
and is cancelled immediately if the browser cannot be opened, so an abandoned or un-launchable sign-in
can no longer wedge `IsCloudBusy` (and the coordinator's sync gate) indefinitely.

## Risks

Addressed during Phases 1–2, listed with what closed them so the state is not re-litigated:

- ~~**Drive quota and rate limits.**~~ Exponential backoff with jitter, honouring `Retry-After`, on
  429 and the 5xx family; batching keeps request counts at parity with the rclone path. Retry on
  *uploads* specifically was broken and is now covered by dedicated tests (finding 1).
- ~~**Resumable upload for large payloads.**~~ Implemented above 5 MB, chunked, and resuming from the
  byte count Drive reports rather than the byte count sent (finding 4).
- ~~**Token revocation mid-sync.**~~ `invalid_grant` surfaces as `GoogleAuthorizationRequiredException`
  meaning "reconnect", and that type now derives from `IOException` so the sync pipeline catches it
  instead of letting it escape onto the launch path (finding 6).

Still open:

- **Scope migration** (finding 5) is the one change a user can actually feel, and the only remaining
  risk that needs UI rather than code. A folder created under rclone's full-Drive access is invisible
  to the managed client's per-file access, so switching re-uploads. The transport now detects the
  resulting stale folder id rather than reading it as an empty cloud (finding 7), but the user still
  has to be told at switch time. This lands with the settings UI.
- **Clock skew** is handled by the manifest design (mtime is only a conflict tie-breaker); the managed
  transport must not reintroduce a date comparison anywhere. Nothing in Phases 1–2 does.
- **Nothing has touched Google's real API.** Every test to date runs against an in-memory fake Drive.
  Findings 1 and 4 are exactly the class of defect a fake cannot fully anticipate, so treat the first
  real sign-in as a test event, not a formality.

## Relationship to the decision log

DECISIONS 2026-07-24 justified rclone partly on "no embedded OAuth client secret". That premise was
already retired by 2026-08-04 and 2026-08-06. This plan does not overturn a live decision — it
completes a direction the project had already taken, and adds a reason (Android) the original
decision did not have to consider. A DECISIONS entry should land with the settings UI, when behaviour
first changes for a user; Phases 1–2 add code nothing user-facing reaches yet.
