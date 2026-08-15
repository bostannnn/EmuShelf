# Android port plan

Target: **AYN Thor**, an Android arm64 handheld. Status: planning (2026-08-15, revised). Nothing
Android-specific has been built yet; the cloud-sync groundwork this depends on is partly done — see
Milestone E.

This is the master plan. `docs/cloud-sync-portability-plan.md` holds the detail for the save-sync half
and is referenced rather than repeated here.

## What the port is

EmuShelf on Android is the **Gamepad shell running on a handheld, launching Android emulator apps**.
Not a new product, not a rewrite. The desktop targets keep working unchanged; platform-specific
behaviour goes behind interfaces in Core, as the project rules already require.

The reason it is plausible at all is that the hard parts are already portable: the domain model, the
format rules, the scraper, the sync engine, and a controller-native UI.

The reason it might not work is Milestone 0.

## Milestone 0 — the spike, and the decision to continue

**Do this before anything else. Everything after it is contingent on the answer.**

One question: *can EmuShelf launch a multi-disc PS1 game in DuckStation Android via an intent, from a
library on shared storage?*

Not "can we send an intent" — that part is easy and not in doubt. The question is whether the
**file handoff** works for the file shapes EmuShelf actually ships (see the next section). Prove it
end to end with one `.m3u` playlist over two `.bin`/`.cue` disc pairs, on the Thor, with the library
in a normal folder on internal storage or SD.

**Kill criterion.** If a multi-disc PS1 game cannot be handed to DuckStation Android without EmuShelf
copying or rewriting the user's game files, the port does not proceed in this form. Rewriting a `.cue`
or materialising copies would violate the project's first rule — never modify the user's game files —
and a launcher that cannot launch PS1 and PS2 properly is not EmuShelf.

Acceptable outcomes short of full success, in preference order:

1. File paths work with all-files access for every target emulator → proceed as planned.
2. Paths work for some emulators, content URIs for others → proceed, with a per-emulator handoff
   strategy in the launch definition.
3. Single-file formats (`.chd`, `.iso`, `.rvz`) work but descriptor formats do not → **stop and
   reconsider.** This is a much smaller product, and the decision is yours, not the plan's.

Budget this as a throwaway spike. It does not need EmuShelf — a minimal Android app that fires the
intent is enough, and is faster than getting Milestone A working first.

## The file-handoff problem

This is the central technical problem of the port and it deserves stating properly.

On desktop, launching is: pass an absolute path as an argument. On Android there is no such
guarantee. The platform wants you to pass a `content://` URI with a temporary permission grant, and
passing a `file://` URI in an intent throws `FileUriExposedException` on modern Android unless the app
opts out of that StrictMode policy.

**Why this collides with EmuShelf specifically.** A `.cue` file contains `FILE "game.bin"` — a
*filename*, resolved relative to the descriptor's own directory. A `.m3u` lists disc filenames the
same way. An emulator handed a content URI for a `.cue` has no directory to resolve those siblings
against, and no permission to reach them even if it could. Content URIs do not compose into a
directory the way relative references need.

This is PS1 and PS2 — the systems EmuShelf exists for, and the subject of the shipped M4 format rules.

**What already exists to build on.** `GameLaunchDependencyResolver` in
`src/EmuShelf.Integrations/Launching/` already walks `.m3u` / `.cue` / `.gdi` descriptor trees and
returns the complete set of files a launch needs — it was written for sandboxed Flatpak launches,
which is the same problem in a friendlier form. Android does not need a different resolver; it needs
a decision about what to do with that set.

**The three candidate strategies**, to be settled by Milestone 0:

| Strategy | How | Cost |
|---|---|---|
| Real file paths + all-files access | Grant `MANAGE_EXTERNAL_STORAGE`, pass a plain path, suppress the file-URI StrictMode policy. What ES-DE and Daijishō do. | Play Store blocker; depends on each emulator accepting paths |
| Content URI per file | Grant a URI for every file the resolver returns | Descriptor-relative references still break; likely only viable for single-file formats |
| SAF tree grant on the library root | User grants the whole games folder once; emulators that support tree URIs resolve siblings themselves | Depends entirely on emulator support; least likely to be universal |

Strategy 1 is the expected answer and the one the spike should try first. The plan assumes it until
Milestone 0 says otherwise.

## Verified starting conditions

Checked against the codebase and nuget during planning, not assumed:

| Fact | Evidence |
|---|---|
| Avalonia has an Android head at the exact pinned version | `Avalonia.Android` 12.1.0 published; the solution pins 12.1.0 throughout |
| The Gamepad UI is already single-view and controller-native | `GamepadRoot` in `MainWindow.axaml:2252`, toggled by `IsGamepadMode`; no child windows |
| The 3D shelf needs no shader work | `ShaderLibrary` already emits GLSL ES 3.00 via its `Es300` dialect — the path Windows already uses through ANGLE |
| Launching is behind one interface | `ITrackedProcessRunner` in Core; `EmulatorDefinition` holds the argument template |
| Descriptor trees are already resolved for sandboxed launches | `GameLaunchDependencyResolver` handles `.m3u` / `.cue` / `.gdi` |
| Storage already branches per-OS | `AppPaths.ResolveBaseDirectory` |
| Core and Integrations are portable as-is | Core has no package or project references; Integrations is parsing and definitions (~24k LOC) |
| Gamepad input needs a new backend | `ppy.SDL2-CS` ships win/linux/osx/ios RIDs — no android |
| The .NET Android workload is available | `dotnet workload search android` on the 10.0.302 SDK |
| Nothing Android is installed on the dev machine | no JDK, adb, SDK or Studio; host is arm64, so arm64 AVDs run natively |

## What EmuShelf loses on Android

Larger than a first pass suggests. Everything below shares one cause: **Android 11+ blocks reading
another app's `Android/data`**, and the SAF picker refuses to select it. No permission fixes it —
only root or Shizuku.

- **PS3 support ends.** No RPCS3 for Android. The M13 RPCS3-library sync has no counterpart. PS1, PS2,
  GameCube/Wii, PSP and 3DS all have Android emulators.
- **Uniform hotkeys (M40) end.** Shipped 2026-08-08, and it works by writing each emulator's own
  config file — `settings.ini`, `PCSX2.ini`, `Hotkeys.ini`. Unreachable on Android. This also removes
  the button that closes a running emulator, which is a Gamepad-shell affordance, not a nicety.
- **Texture pack inventory (M32) ends.** Same cause: it reads texture roots inside emulator data dirs.
- **Save sync becomes partial.** Emulators keeping saves in a user-chosen public folder can sync;
  those pinned inside `Android/data` cannot. See Milestone E.
- **Self-update changes shape.** File-swap becomes a package-install intent.
  `UpdateApplierFactory` already has an `UnsupportedUpdateApplier` to fall back on.
- **rclone is gone.** Android will not execute a runtime-downloaded binary. This is what forced the
  cloud-sync work already underway.

The general rule: **every "EmuShelf configures the emulator for you" feature dies.** Save sync was
only the first instance found. Hiding a feature is the correct handling — the existing
`GetRemoteIncompatibilityReason` / `ResolveUnit`-returns-null channels already express "not possible
here, and why", and the same shape should be used for hotkeys and texture packs.

## Milestones

### A — Walking skeleton

A `net10.0-android` head, `ISingleViewApplicationLifetime`, and the Gamepad UI browsing a library.

- New `src/EmuShelf.App.Android` referencing the existing App; `MainWindow`'s content extracted into a
  `UserControl` so the desktop window and the Android single view host the same tree.
- `AppPaths` gains an Android branch — app-private storage, since "beside the executable" has no
  meaning here.
- Force `InterfaceMode.Gamepad`.
- **Build gating, decided here and not deferred.** `EmuShelf.slnx` currently lists five source and two
  test projects, all unconditional. Adding an Android head makes the Android workload mandatory for
  every `dotnet build` — yours, mine, and CI's existing matrix job. Options: keep the head out of the
  default solution and build it explicitly; or gate it on a property so a machine without the workload
  skips it. Either is fine; leaving it unconsidered breaks everyone's build on day one.

**Answers:** does Avalonia render, does the GLES shelf draw, does SQLite work.
**Risk:** the 3D shelf is the likeliest thing to misbehave; a flat-cover fallback and watchdog already
exist, so failure degrades rather than blocks.
**Done when:** the app launches on device and shows an imported library.

### B — Launching games

Contingent on Milestone 0. With the handoff strategy settled, this is mechanical but broad.

- `AndroidIntentLauncher` behind `ITrackedProcessRunner`.
- Per-emulator Android launch definitions: package, activity, extras, and **handoff strategy** replace
  the argument template.
- Emulator *presence* detection replaces "is the executable at this path".
- **Re-express the exit signal.** Automatic save sync currently hangs off `WaitForExitAsync` — there
  is no process to wait on. Activity resume is the available signal and it is weaker: the user may
  switch away without quitting the emulator, so a resume does not prove the game ended. Options are a
  resume-triggered sync that tolerates being early, or a foreground-state check. This needs designing,
  not assuming; getting it wrong syncs a save mid-session.

**Done when:** a game launches in the right emulator and returning to EmuShelf syncs.

### C — Controller input

- `IGamepadReader` over Android `InputDevice`/`MotionEvent`, replacing `SdlGamepadReader`.
- Map the Thor's built-in controls against the existing Gamepad navigation model.

**Cannot be validated off-device.** No pad on the dev machine, none in an AVD.
**Done when:** the Gamepad shell is fully navigable on the Thor without touching the screen.

### D — Storage and permissions

- All-files access (assuming Milestone 0 chose strategy 1), plus SAF folder picking through Avalonia's
  `StorageProvider` for adding library folders.
- Verify `FolderScanner` and the availability checker against real Android paths.

**Use the emulator, not the device.** API 30/31/33/34 AVDs answer where each restriction bites across
four OS versions; the Thor answers for one.

### E — Cloud sync

Design and review history in `docs/cloud-sync-portability-plan.md`.

**Done (Phases 1–2):** managed Google Drive transport speaking the API directly with no external
binary; OAuth/PKCE sign-in and protected token store; the shared `CloudSaveIndex` wire format so it
and the rclone transport address the same folder interchangeably; a `TransportKind` setting defaulting
to rclone for existing users; coordinator wiring. Reviewed twice, seven defects found and fixed.

**Remaining, desktop:** the settings UI — transport choice, connect/disconnect, and the warning that
switching means an rclone-created folder is invisible under the narrower scope so saves re-upload.

**Remaining, Android:**
1. A second OAuth client (Android clients have no secret and bind to package + signing cert).
   `GoogleOAuthClientCredentials` already models a public client: one embedded field, one branch.
2. A custom-scheme `IOAuthRedirectHandler` — nothing can bind a loopback port for the browser here.
   The interface exists; only the desktop implementation does.
3. Force `TransportKind` to `GoogleDrive`; hide the rclone UI.
4. **A SAF-backed `ILocalSaveEndpoint`** — the genuinely hard part, independent of everything above.
   `ILocalSaveEndpoint` is stream-based, so `FileSystemLocalSaveEndpoint`'s staging, verification and
   conflict-backup logic carries over with `File.Open` swapped for a DocumentFile URI.
5. Emulators unreachable inside `Android/data` return a reason rather than failing.

### F — Packaging and release

- APK/AAB in CI beside the existing three targets; signing key handling.
- `EmbeddedSecrets.targets` gains the Android OAuth client id.
- Distribution: a GitHub release APK matches how EmuShelf ships today. Note that all-files access
  rules out the Play Store without a different storage strategy.

## Test strategy

The desktop suite is ~1,936 tests and none of it should regress. Concretely:

- **Core, Integrations, Infrastructure tests keep running unchanged** on the desktop TFM. The Android
  head must not pull platform types into those projects; anything Android-specific goes behind a Core
  interface, which is what keeps them testable.
- **Android implementations get unit tests where the logic is real** — intent construction, handoff
  strategy selection, path/URI mapping, save-location resolution. These are pure functions of input
  and are worth pinning; they do not need a device.
- **What cannot be unit-tested** is the part that talks to Android: actually starting an activity,
  actually reading through SAF, actually receiving pad events. Those are verified on hardware and
  recorded as such, not asserted.
- **Visual snapshot tests are desktop-only.** They assert pixel heights and already vary by OS; do not
  extend them to Android.
- **Add an Android smoke check to CI only if it can be made reliable.** An emulator-based UI test that
  flakes is worse than no test. Building the APK in CI is worth doing regardless.

## Development setup

Not needed until Milestone A — but Milestone 0 needs a device, not a toolchain.

- Android Studio, purely as the least-annoying route to a JDK, the SDK, `platform-tools`, the AVD
  manager and an **arm64-v8a** image. No code is written in it.
- `dotnet workload install android`.
- **Wireless ADB to the Thor** matters more than the emulator: the same `adb install` / `logcat` /
  `screencap` loop works against the real device, and the real device is the only thing that can
  answer Milestones 0, B and C.

## What cannot be verified without hardware

Listed so it is never mistaken for done:

- Milestone 0's entire question.
- Anything in Milestone C. No pad on the dev machine, none in an AVD.
- Whether each emulator's intent API behaves as documented, and whether the Thor's installed builds
  match the versions those APIs were observed on.
- Real GPU behaviour, display, refresh rate, thermals.
- Where `Android/data` restrictions land on the Thor's specific OS build — **find out its Android
  version early**, it decides the storage story.
- Every cloud-sync test to date runs against an in-memory fake Drive. Nothing has touched Google's
  real API; the first real call happens when the desktop settings UI lands.

## Sequencing

**0 → A → B → C → D**, with E's desktop remainder runnable in parallel at any point.

Milestone 0 first because it is the only step that can end the project, and everything else is wasted
if it fails. This is a change from the first draft of this plan, which put the safe desktop sync work
first — comfortable, but backwards: it invested further before answering the question that decides
whether the investment is worth making.

E's desktop remainder is genuinely parallel. It improves the shipping product whether or not the port
happens, and it is where the managed transport meets the real Drive API for the first time.

## Roadmap integration

`ROADMAP.md` carries numbered milestones through M40. Folding this in as M41+ — one per section above
— is the natural step once the scope is agreed and Milestone 0 has reported.
