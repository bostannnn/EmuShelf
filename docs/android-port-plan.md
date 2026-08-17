# Android port plan

Target: **AYN Thor** (Snapdragon 8 Gen 2, Android 13, dual-screen clamshell) — owned, on order,
not yet delivered. Architecture targets Android arm64 handhelds generally; every acceptance gate is
the Thor. Status: planning, revised 2026-08-15 after an adversarial review of the first draft.
Nothing Android-specific has been built.

This is the master plan. `docs/cloud-sync-portability-plan.md` holds the detail for the save-sync
half and is referenced rather than repeated.

## What changed from the first draft

The first draft was checked against the codebase, NuGet, and the Android emulator ecosystem. Its
architecture instincts held up; several of its load-bearing facts did not. Corrections that changed
the plan, so they are not re-litigated:

| First draft said | Actually |
|---|---|
| Real file paths + all-files access is "what ES-DE and Daijishō do"; the expected answer | **Backwards.** Both ship SAF/content URIs for almost everything and plain paths only for RetroArch. Per-emulator handoff is the ecosystem's steady state, not a fallback |
| Milestone 0 decides whether the port proceeds | It decides *which* handoff per emulator. It is a measurement, not a kill gate — and it was aimed at DuckStation, whose Android build was abandoned in 2026 |
| "PS3 support ends. No RPCS3 for Android" | aPS3e exists (GitHub + Play, releases through 2026) with a documented intent. Low compatibility, but launching is possible. Only the M13 *library sync* has no counterpart |
| Everything lost shares one cause: `Android/data` | Three different causes. And on the Thor specifically, root is one documented Settings toggle ("Run script as Root"), so most of the list is capability-gated, not dead |
| "Launching is behind one interface" | 17 `Process.Start` sites across 12 files; one is `TrackedProcessRunner` |
| "Core and Integrations are portable as-is" | They compile. 53 OS-branch sites across 29 files, **including Core**, and `OperatingSystem.IsLinux()` returns *false* on Android, so every `IsWindows/IsMacOS/else-Linux` ladder takes a wrong branch |
| "Descriptor trees are already resolved" | `GameLaunchDependencyResolver` runs on the Flatpak branch only ([EmulatorLaunchService.cs:182](../src/EmuShelf.Core/Launching/EmulatorLaunchService.cs:182)) and hard-throws on a missing reference |
| `ILocalSaveEndpoint` is stream-based, so SAF is a swap | The interface is; the 448-line implementation is `Directory.Move`/`File.Move`/`SetLastWriteTimeUtc` path work, and `SaveUnitLocation` is a path record in Core |
| "New App.Android referencing the existing App" | The composition root lives inside `if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime)` — ~265 of 314 lines of `App.axaml.cs`. The head would link, launch, and show nothing |
| The solution pins 12.1.0 throughout | `Avalonia.Controls.ItemsRepeater` is 12.0.0 and no 12.1.x exists. It backs the gamepad grid |
| Milestone E is "Done (Phases 1–2)" | It is one unmerged 28-file commit on a side branch, never on `main`, and **this plan document lives in that same commit** |
| Fold in as M41+ | M41 and M42 are taken ([ROADMAP.md:1665](../ROADMAP.md:1665), [:1736](../ROADMAP.md:1736)). Android is **M43+** |
| "~1,936 tests" | 1,826 |
| `GamepadRoot` at `MainWindow.axaml:2252` | 2267 |

Two claims survived every attempt to break them, and are the plan's real foundation: the sync engine
above `ICloudSyncTransport` is genuinely transport-agnostic, and `ShaderLibrary` genuinely emits
GLSL ES 3.00 with the two desktop-only GL calls already guarded.

## What the port is

EmuShelf on Android is **the Gamepad shell on a handheld, firing intents at Android emulator apps.**
The desktop targets keep working unchanged.

One thing to be accurate about, because it changes what to build rather than whether to: on Android
this is a **thinner build that shares a domain layer.** The "EmuShelf configures the emulator for
you" family does not survive, RetroArch's desktop `-L core content` route does not survive (9 of 15
systems use it today), and there is an emulator-setup step the desktop build does not have. Plan for
those three, rather than discovering them.

## Decisions the owner must make

The plan proceeds on these defaults. Change any of them and the shape changes.

1. **Android v1 ships as an experimental sideload build, not a fourth supported release target.**
   Promote it once the hardware gates close. This keeps the signing key, developer verification, and
   support burden off the critical path.
2. **v1 is gamepad-first with a minimal touch seam** — tap-to-focus and tap-to-launch only. Silence
   here is the decision that forecloses phones and tablets forever, so it is made explicitly. Do not
   hard-code one aspect ratio, one DPI, or "no touch exists."
3. **The Thor is the target and the verification device.** The port exists to run EmuShelf on it.
   Architect for Android handhelds anyway — that costs nothing here and is what keeps the Thor's
   quirks from becoming load-bearing — but every acceptance gate is "it works on the Thor."

Open and genuinely undecided: what the Thor's **second screen** does. It is a standard Android
`Presentation` display and AYN's own software uses it. Shipping a forced-single-view app leaves half
the hardware black on the device the port exists for. Decide before A1: nothing / cover art /
now-playing. "Nothing" is an acceptable answer, stated.

## What Android v1 is, and when to stop

The first draft defined neither, and gave kill criteria to Milestone 0 only — so every milestone
after it was implicitly unabandonable. Both gaps are filled here.

**Android v1 is:** the Gamepad shell on the Thor, importing a library without a keyboard, launching
at least one maintained emulator per supported system, syncing the saves that are reachable, and
saying honestly why the rest are not. Shipped as a sideload APK from a GitHub release, marked
experimental.

**Android v1 is not:** feature parity with desktop, Play Store distribution, phone or tablet support,
RetroArch-backed systems, or any feature that writes an emulator's configuration.

**Per-milestone exit criteria** — each is a point where stopping leaves the desktop product no worse
than before, which is the property that makes them real rather than decorative:

| Milestone | Abandon or degrade if |
|---|---|
| 0a / 0b | No maintained emulator accepts a constructible handoff for PS1 **or** PS2, even with a derived descriptor in `Cache/`. Stop — the third outcome (single-file formats only) is a different, smaller product and needs a fresh decision, not a continuation |
| A0 | Nothing. It is a desktop refactor gated by the existing suite; if it cannot be made green it is reverted and the port stops, with the shipping product untouched |
| A1 | **GL:** if `OpenGlControlBase` cannot get a context on Avalonia's Android backend, v1 is flat-cover-only. Decide then whether that is still worth shipping — do not let the silent fallback make the decision. **Import:** if a gamepad-native import flow proves larger than A1's whole budget, fall back to a first-run "point me at one folder" wizard rather than porting the desktop flow |
| D | If neither all-files access nor a per-emulator tree grant gives EmuShelf's own readers random access to game files, the scanner and hashers need SAF-backed sources — that is a new milestone, not a detail of D, and it should be sized before continuing |
| B | If the exit signal cannot be made to survive process death, ship launch without automatic post-play sync and make sync manual. Do not ship a sync that fires mid-session |
| C | If pad events cannot be read without fighting Avalonia's own handling, ship touch-first for v1 and treat the pad as the follow-up. This inverts decision 2 above and needs saying out loud |
| E-android | If the SAF `ILocalSaveEndpoint` cannot be made atomic, ship read-only cloud sync (download and restore, no upload) rather than a write path that can corrupt a save |
| F | If developer verification blocks distribution before v1 is ready, ship via ADB install and documented sideload, not by delaying |

## The file-handoff problem, corrected

On desktop, launching is: pass an absolute path as an argument. On Android there are four shapes, and
**the ecosystem already uses three of them, per emulator.** This is not something Milestone 0 might
discover; it is the documented starting design.

Evidence — ES-DE's shipped Android `es_systems.xml` and Daijishō's platform JSONs:

| Emulator | Shape in the wild |
|---|---|
| PCSX2 / AetherSX2 / NetherSX2 | `%EXTRA_AutoStartFile%=%ROMSAF%` / `-e bootPath {file.uri}` — SAF URI |
| Dolphin (standalone) | `%EXTRA_AutoStartFile%=%ROMSAF%` / `-e AutoStartFile {file.uri}` |
| DuckStation | `-e bootPath {file.uri}`; the plain-path form is labelled **"Legacy"** in Daijishō |
| PPSSPP | `-d {file.uri}`, and upstream landed "Android: Explicitly allow content URI intents" |
| Azahar / Lime3DS | `-a android.intent.action.VIEW -d {file.uri}` |
| aPS3e | `%EXTRA_iso_uri%=%ROMSAF%` |
| MAME4droid | `%DATA%=%ROMPROVIDER%` — the frontend's own FileProvider |
| RetroArch | `-e ROM {file.path}` — the **only** plain-path case |

Two corrections that follow:

**The permission is on the receiving side.** ES-DE's own documentation is explicit that emulators
"still need to have scoped storage access setup within the emulator interface… you will therefore
generally need to manually provide scoped storage access to each game system directory." Field
reports match: `Permission denied` through a frontend, fine when the emulator opens the file itself.
So there is a fourth strategy the first draft missed, and it is the one that works for descriptor
formats: **the emulator holds its own persisted tree grant; EmuShelf just names the file.** That makes
"has the user added this folder inside DuckStation yet?" a product concept EmuShelf must model — a
per-emulator setup checklist with a verify step — not an incidental.

**`MANAGE_EXTERNAL_STORAGE` buys scanning, not launching.** EmuShelf needs it for `FolderScanner`,
the disc readers, the achievement hashers, and the cover cache — all of which are `File.Open` on an
absolute path doing random access over multi-gigabyte files ([FolderScanner.cs:37](../src/EmuShelf.Infrastructure/Storage/FolderScanner.cs:37)).
It does not make the emulator able to read the file. It remains grantable on sideloaded APKs in 2026
(it is a Settings toggle; the Permissions Declaration Form is a Play Console requirement only).

**On the StrictMode cost the first draft listed: it is a phantom.** `FileUriExposedException` fires on
a `file://` **`Uri`** in intent data or extras. Every real-world path launch passes a bare *String*
(`-e bootPath /storage/emulated/0/…`), which never trips it. The thing that fails is the emulator's
`open()`.

## Milestone 0 — the spike

**Do this first.** It is a measurement, not a go/no-go — but it sizes B, C, D and E, and the wrong
answer still ends the project.

Budget a throwaway Android app plus the toolchain. It does not need EmuShelf.

**The Thor is on order, and most of this does not wait for it.** The dev host is arm64, so
arm64-v8a AVDs run Android emulator APKs *natively* — DuckStation, PPSSPP, Dolphin, RetroArch and
Azahar all install and boot in an AVD. Emulation speed will be poor, which does not matter: the
file-handoff question is about whether `open()` succeeds, not how fast the game runs. So Milestone 0
splits.

### 0a — on an AVD, before the device arrives

Everything below except the pad probe and anything device-specific. Run the full format ladder and
the full emulator matrix at API 33 (matching the Thor's Android 13) plus one other level. This is
the bulk of the spike and it is available today.

### 0b — on the Thor, day one

Re-run the matrix against the device's actual installed emulator builds, plus the pad probe, plus
the device questions in "What cannot be verified without hardware." An AVD answer that the Thor
contradicts is itself a finding — it means the per-emulator strategy is build-sensitive, which
changes how the launch definitions are versioned.

**The matrix**, on an AVD first and the Thor on arrival:

- **Formats, in this order:** single-file (`.chd`/`.iso`/`.rvz`) → `.cue`+`.bin` pair → `.m3u` over
  two `.cue` pairs. Each step proves one more level of sibling resolution. The first draft started at
  the hardest case, which cannot distinguish "descriptors are impossible" from "the intent is wrong."
- **Emulators:** ARMSX2 **first** (PS2 is the fragile system, not PS1), then DuckStation, Dolphin,
  PPSSPP, RetroArch, Azahar, aPS3e.
- **Per emulator, which of:** bare path / FileProvider content URI + `FLAG_GRANT_READ_URI_PERMISSION`
  / SAF document URI against a tree the *emulator* already holds.
- **Two probes that cost nothing extra on the same device, and each of which can also end the port:**
  a GL probe (one `.glb` through `OpenGlControlBase` on Avalonia.Android — see Milestone A1) and a pad
  probe (`InputDevice`/`MotionEvent` reaching an Activity under Avalonia).
- **One EmuShelf-side read:** seek-and-read over a real CHD from the chosen storage strategy. If
  paths are unavailable, `FolderScanner` and every disc reader need SAF-backed sources — a large,
  unbudgeted body of work the first draft's preference ordering concealed.

**Outcomes.**

1. Paths or per-emulator URIs cover every shipped system → proceed as planned.
2. Descriptor formats need a **derived** `.m3u`/`.cue` written into EmuShelf's own cache → proceed.
   This is explicitly permitted: the project rule is *never modify or delete the user's game files*,
   and a generated descriptor in `Cache/` modifies nothing and copies no game data. The first draft
   ruled this out by lumping it with "materialising copies." It is the community's standard
   workaround.
3. Single-file formats work, descriptors do not, even derived → **stop and reconsider.** A
   `.chd`/`.iso`/`.rvz`-only product is smaller but SAF-native, needs no all-files access, and is
   therefore distributable in ways this plan otherwise is not. That is a real product, not a failure.

**Note on kill criteria:** do not stake the decision on DuckStation. Its Android build was abandoned
in 2026 — still on Play, still working, frozen and unsupported. The criterion is *at least one
maintained emulator per shipped system accepts a handoff EmuShelf can construct.* Emulator
maintenance status becomes a first-class field in the Android launch definitions.

## Milestone 0a — results so far (2026-08-15)

Measured first-hand on the `emushelf-api33` AVD (Android 13, arm64-v8a) against APKs pulled from
each project's official release channel. **This settles the handoff question and confirms the
corrected framing: there is no single strategy, and the original plan's "Strategy 1 is the expected
answer" is refuted.**

All-files capability was verified by **granting it**, not by reading the manifest —
`appops set <pkg> MANAGE_EXTERNAL_STORAGE allow` sticks only where the permission is declared.

| Emulator | Package | targetSdk | All-files grant | Intent shape | Result |
|---|---|---|---|---|---|
| RetroArch | `com.retroarch.aarch64` | **28** | legacy, pre-scoped | `-e ROM <path> -e LIBRETRO <core>` | **Plain path works. `.cue` and `.m3u` both resolved** |
| PPSSPP (PSP) | `org.ppsspp.ppsspp` | 36 | **refused** | `VIEW`, data URI | Path fails; **content URI works** |
| ARMSX2 (PS2) | `com.armsx2` | 37 | **allow** | `VIEW`, `content`+`file` | Untestable — wizard gates on BIOS |
| Azahar (3DS) | `org.azahar_emu.azahar` | 35 | **allow** | `VIEW`, `content` only | Untestable — first-run wizard |
| aPS3e (PS3) | `aenu.aps3e` | 36 | **refused** | `APS3E` action, `iso_uri` extra | Untestable — first-run wizard |
| Dolphin (GC/Wii) | `org.dolphinemu.dolphinemu` | 36 | **refused** | `MAIN` + `AutoStartFile` extra | Extra accepted; **rejects a FileProvider URI — wants a SAF tree URI** |
| DuckStation (PS1) | — | — | — | — | **No APK published outside Play** |

**EmuShelf needs all-files access to *serve* files, not just to scan them.** The content-URI route
goes through EmuShelf's own `FileProvider`, which opens the file with EmuShelf's identity. Measured:
with no storage permission the spike's provider returned `EACCES` and PPSSPP reported
`Boot failed: File is empty`; after `MANAGE_EXTERNAL_STORAGE` was granted **to the spike**, the same
intent produced `Boot failed: Not a PSP game` and PPSSPP held the file open. Same file, same intent —
only the frontend's permission changed.

**Three of the five standalone emulators discard a launch intent until their own first-run wizard is
done.** ARMSX2 ("Select your BIOS" — `Next` does nothing without a BIOS folder), Azahar ("Welcome!
… Get started"), aPS3e ("Welcome to aPS3e!"). Granting storage first does not help; the wizard is a
hard gate. RetroArch and PPSSPP accept a launch immediately. So the per-emulator setup checklist is
mandatory, and it must be *verified*, not assumed.

**The 3D shelf draws.** `OpenGlControlBase` initialised on Avalonia 12.1.0's Android backend with
`AndroidPlatformOptions.RenderingMode` pinned to `Egl`: `GL_VERSION = OpenGL ES 3.0`,
`GLSL = OpenGL ES GLSL ES 3.00` — the exact dialect `ShaderLibrary` emits — and a first frame
rendered to the screen. Pin the rendering mode explicitly, as on macOS; do not rely on the default
list, whose `Software` fallback is what makes this failure silent.

Five things this establishes:

1. **RetroArch's targetSdk 28 is why every launcher uses `-e ROM {file.path}` for it and only for it.**
   It predates scoped storage, so paths simply work. That single fact explains the whole pattern the
   research observed in ES-DE and Daijishō.
2. **PPSSPP and aPS3e cannot be handed a path under any permission.** `appops set … MANAGE_EXTERNAL_STORAGE
   allow` is refused because the permission is not declared. Confirmed by experiment, not inference.
3. **The failure is silent and misleading.** Handed `file:///sdcard/…/single.iso` — a real, 4 MB
   file — PPSSPP parsed the URI, logged it, then reported `LocalFileLoader: failed to open file` →
   `ReadAt from 0-sized file` → **`Boot failed: File is empty`**. Not "permission denied". A launcher
   would report a successful launch and the user would see a file-corruption message. Any handoff
   EmuShelf ships needs a preflight check, because the emulator's own error text will misdirect.
4. **A fresh emulator swallows the launch intent entirely.** ARMSX2 handed a valid `VIEW` intent
   showed its first-run wizard instead — *App Data Folder / BIOS Location / ROM Location*. The
   per-emulator setup checklist is therefore mandatory, not a nicety, and "launched successfully"
   proves nothing on an unconfigured emulator.
5. **DuckStation is out of reach**, corroborating its abandonment: no APK on GitHub releases at all,
   Play-only. It cannot be part of the spike, and it must not be the PS1 plan. PS1 routes through
   RetroArch (SwanStation/Beetle), which is the plain-path case anyway.

### Multi-disc works — the original kill criterion is passed

Tested on the AVD with RetroArch + the SwanStation PS1 core, handed a plain path:

- `disc1.cue` → RetroArch opened `disc1.bin`. **Confirmed by file descriptor**, not by absence of an
  error: `/proc/<pid>/fd/87 -> /storage/emulated/0/EmuShelfTest/disc1.bin`.
- `game.m3u` → same result. Two levels of relative resolution, `.m3u` → `.cue` → `.bin`, and the
  `.bin` ends up open.

So a multi-disc PS1 game can be handed over **without EmuShelf copying or rewriting anything**. The
first draft's kill criterion is met, by the RetroArch route rather than the DuckStation one it
assumed. PS1 should be planned on RetroArch/SwanStation, which is also the only plain-path emulator
in the set and therefore the least fragile.

Incidental: the AVD reports `OpenGL ES 3.0` through `Android Emulator OpenGL ES Translator
(Apple M4)`, so a GLES3 context is available for the shelf probe.

**Still to measure, and why each is blocked or not:**

1. **The SAF-tree route for Dolphin.** GameCube and Wii are core systems and Dolphin rejects a
   FileProvider URI. It needs a document URI derived from a folder tree the user granted — which is
   what ES-DE's `%ROMSAF%` is. Requires driving `ACTION_OPEN_DOCUMENT_TREE` once. Not blocked, just
   not done.
2. **A second API level.** Only API 33 tested so far.
3. **A real `.chd`, and EmuShelf's own disc readers** against Android storage.
4. **PS2, 3DS, PS3 end-to-end** — blocked on BIOS and system files, which only the owner can supply,
   on the Thor. The corpus is on the AVD at `/sdcard/EmuShelfTest/` — synthetic `.bin`/`.cue`/`.m3u`
files, no game content required, since the discriminator is *read succeeded / read failed*, not
whether a game boots.

**One thing the spike cannot answer here:** ARMSX2 and aPS3e require console BIOS images, and Azahar
needs 3DS system files. Those are yours to supply on the Thor; the AVD can prove the file handoff but
not a full boot.

## Prior art: what the shipping Android frontends do

Checked because it is cheaper to read a working launcher's config than to rediscover it. Two are
worth knowing about.

**Cocoon** (`cocoon-shell.com`, APK-only, but it publishes its 119 per-platform launch configs at
[inssekt/CocoonFE](https://github.com/inssekt/CocoonFE/tree/main/platforms)). The config format is
Daijishō-shaped, and its PS1 file confirms the measurements above exactly:

- Every RetroArch entry uses `{file.path}` — plain paths — with the core at
  `/data/data/com.retroarch.aarch64/cores/<core>_libretro_android.so`. Same route this spike proved.
- Standalone emulators split by app: DuckStation modern is `{file.uri}`, and there is a separate
  **"Duckstation (Legacy)"** entry using `{file.path}`. FPse and ePSXe take paths.
- **`killPackageProcesses: true` on every RetroArch entry**, with a `killPackageProcessesWarning`
  flag. They force-stop the emulator before launching. Worth stealing — it is a concrete answer to
  "the emulator is already running" that the exit-signal work in Milestone B otherwise has to invent.
- The PS1 `acceptedFilenameRegex` is `^(?!(?:\._|\.).*).*(?<!bin)$` — hide `.bin`, list `.cue`/`.m3u`.
  The same descriptor-vs-payload split EmuShelf's import rules already make.
- Cocoon takes All Files Access for itself and states plainly that without it *"some emulators may
  report 'file not found' even when the file is present"* — independent confirmation that this
  failure is silent and misleading, and that a shipping frontend papers over it rather than solving it.
- It explicitly supports **dual-screen handhelds including the AYN Thor**, with a single-screen
  toggle. Relevant to the open second-screen decision.

**NeoStation** ([misobadev/neostation-frontend](https://github.com/misobadev/neostation-frontend),
full source, Flutter/Dart, ships on Android/Windows/Linux/macOS). One technique worth taking:

- It solves multi-disc by **generating `.m3u` playlists** during a library scan, reusing existing
  ones rather than duplicating. That is the derived-descriptor approach. The difference for EmuShelf
  is where the file lands: `Cache/`, never beside the user's games.

Neither publishes anything that changes the plan's architecture. They confirm it.

## Capability model — replacing the static loss list

The first draft listed features that "end" under one cause. There are three causes, they bite
differently, and on the Thor root is one documented Settings toggle away. Replace the static list
with a **runtime capability probe** ("can I read `Android/data/<pkg>`?") feeding the existing
"not possible here, and why" channels. That is less code than deleting three features, and it keeps
them alive for rooted and Shizuku users — a large fraction on a handheld.

**Cause 1 — `Android/data` is unreadable (capability-gated, not dead).** Confirmed for Android 12+
even with all-files access. Gates M40 uniform hotkeys, M32 texture-pack inventory, M33 auxiliary
sync, and save sync for DuckStation / AetherSX2 / Dolphin. Reachable without root: PPSSPP, Azahar,
RetroArch — note these are also the emulators with the cleanest handoff, which is an argument for
choosing target emulators on the pair of properties. One trap: PPSSPP records its memstick path in
app-private storage, so it must be *asked for*, not discovered.

**Cause 2 — no process to execute.** Android 10+ treats exec from the app's writable home as a W^X
violation. Kills rclone (this is what forced the managed Drive transport), `FileRevealService`,
`PlatformOnScreenKeyboardService`, the Flatpak inspectors, `UpdateProcess`, and the `flatpak info`
probe in `SaveProviderRegistry`. 17 `Process.Start` sites across 12 files, only one of which is
`TrackedProcessRunner` — so an `AndroidIntentLauncher` behind `ITrackedProcessRunner` leaves 16 live.

**Cause 3 — no desktop shell.** Self-update becomes a package-install intent
(`UpdateApplierFactory` already falls back to `UnsupportedUpdateApplier`, but `UpdatePlatform`
enumerates only win-x64/linux-x64/osx-arm64 and needs an Android branch). Four App services are typed
on `Window`: `WindowFrontendController` (minimize-on-launch / restore-on-exit),
`WindowLayoutService`, `WindowInterfaceModeService`, `MacFullScreenController`.

**Losses the first draft missed entirely, all of which meet its own criterion:**

- **RetroArch as a launch backend.** [RetroArchDefinition.cs:16](../src/EmuShelf.Integrations/Emulators/RetroArch/RetroArchDefinition.cs:16) backs
  playstation, megadrive, nds, gba, snes, nes, dreamcast, arcade, gbc — **9 of 15 systems** — and
  `EmulatorLaunchService` hard-fails unless the arguments are `-L {CorePath} {GamePath}`. RetroArch
  Android takes a `LIBRETRO` extra naming an `.so` inside its own private directory, chosen through a
  file dialog that cannot reach it. This is the largest unlisted loss.
- **M41, the in-app emulator install & update manager** — planned, being designed right now, and
  entirely void on Android (an app cannot install APKs, and there is no writable app dir to install
  into).
- **Steam Input**, which is half the gamepad input path
  ([MainWindow.axaml.cs:1038](../src/EmuShelf.App/Views/MainWindow.axaml.cs:1038) maps
  Steam-Input-as-keyboard into `DispatchGamepadAction`) and is surfaced *inside* gamepad mode.
- **Text entry.** `IOnScreenKeyboardService`'s only implementation is Windows TabTip/osk; everything
  else relies on a hardware keyboard. Gamepad search, rename, ScreenScraper login and the
  RetroAchievements key all need it. Without an Android IME path, gamepad search is unusable.
- **Cover setting**, which in gamepad mode is a documented handoff to Desktop mode
  ([MainViewModel.cs:1849](../src/EmuShelf.App/ViewModels/MainViewModel.cs:1849)) — and forcing
  Gamepad mode removes the destination.
- **M36 automatic save states**, which hang off the same exit signal as save sync.
- **The tracked-session loop itself.** `ITrackedProcessRunner.RunAsync` returns an exit code and
  `EmulatorLaunchService` branches on it. Losing the process handle *and* the M40 hotkey that closes
  an emulator together degrades the one loop M24 Phase 1 exists to harden.

## Milestones

### A0 — Split the App project (desktop only, no Android code)

**This is the port's real first task and the first draft did not contain it.** `src/EmuShelf.App` is
`OutputType=WinExe` with a `PackageReference` to `Avalonia.Desktop`; more importantly its entire
composition root — theme service, database, `MainViewModel` and ~20 collaborators, gamepad service,
the four `HttpClient`s, both unhandled-exception handlers — sits inside the
`IClassicDesktopStyleApplicationLifetime` branch of a 314-line `App.axaml.cs`. Under
`ISingleViewApplicationLifetime` that is skipped wholesale: the head links, launches, and shows
nothing.

- Extract a shared UI library: `App.axaml`/`.axaml.cs` with a **lifetime-agnostic composition root**,
  `ViewModels/`, `Controls/`, `Styles/`, `Assets/`, and the non-`Window` half of `Services/`.
- Desktop head keeps `Program.cs` (it configures `AvaloniaNativePlatformOptions`, a desktop-only
  type), `MainWindow`'s `Window` shell and its `WindowState` code-behind, the 9 dialog `Window`s, the
  macOS helpers, `SteamInputTemplateInstaller`, and the `Avalonia.Desktop` reference.
- Re-type `DialogService`'s owner from `Window` to `TopLevel` — its 6 picker call sites are what
  Milestone D needs, and every one currently returns `TryGetLocalPath()`, which is null for a SAF URI.
- Split `MainWindow.axaml` (4,784 lines; desktop shell and `GamepadRoot` at :2267 interleaved) so the
  gamepad tree can be hosted in a single view.

Scope: 19 `avares://EmuShelf/…` URIs get renamed; 48 `MainWindow` references across 4 test files; 17
snapshot/render test files whose baselines pin pixel heights; `tools/EmuShelf.Rendering.Preview` also
references the App project.

**Why first:** it is verified end to end by the existing 1,826 desktop tests, it improves the shipping
product regardless of whether Android proceeds, and it fails fast on the avares/lifetime/snapshot
problems before a single line of Android exists.
**Done when:** the full Release suite is green and the desktop app is unchanged by eye.

### A1 — Walking skeleton

A `net10.0-android36.0` head, `ISingleViewApplicationLifetime`, Gamepad UI browsing a library.

- **The TFM is not a choice.** `Avalonia.Android` 12.1.0 ships exactly one asset,
  `lib/net10.0-android36.0`, and pulls AndroidX AppCompat/Window bindings. That requires **Android SDK
  platform 36** and **JDK 21**. Avalonia's own package text calls Android support *experimental* —
  that is a different risk posture from "Avalonia has an Android head."
- **Pin the rendering backend deliberately.** `AndroidPlatformOptions.RenderingMode` defaults to
  `[Egl, Software]`. If EGL init or context sharing is unavailable, Avalonia falls back to Software,
  `OpenGlControlBase.EnsureInitializedCore` **logs and returns false without throwing**, and only the
  watchdog notices. That is precisely the failure that hid the macOS/Metal bug (DECISIONS 2026-08-14),
  and "a flat-cover fallback exists so failure degrades rather than blocks" is the reasoning that hid
  it. Instrument the `Fail()` path at
  [MediaShelf3DControl.cs:368](../src/EmuShelf.App/Controls/MediaShelf3DControl.cs:368) and assert GL
  initialised, rather than checking by eye.
- **`Avalonia.Desktop` must not reach the head.** It has no android asset but restores anyway via
  `lib/net10.0`, silently shipping Win32/X11/Native backends into the APK.
- **Gamepad-native library import.** The gamepad system menu is exhaustively Search / Scrape all in
  view / Settings / Switch to Desktop / Quit ([MainViewModel.cs:2693](../src/EmuShelf.App/ViewModels/MainViewModel.cs:2693));
  `AddGamesCommand`/`AddFolderCommand` are bound only inside the desktop-only grid. On Android there
  is no Desktop mode to import from. Folder pick → system picker → metadata consent → scan progress
  must all exist as gamepad overlays. **This is a feature, not a port detail, and it is Milestone A1's
  largest single item.**
- `AppPaths` gains an Android branch — and so does every `OperatingSystem.Is*` ladder that currently
  falls through to Linux. 53 sites, 29 files, including Core.
- Force `InterfaceMode.Gamepad`.
- **Build gating — one option, not two.** `.slnx` has **no** `Condition` support (the feature request
  is open upstream), and the workload error fires at **restore**, so `dotnet build`, `dotnet test` and
  `dotnet restore` all fail identically. Self-neutralising the TFM does not work either, because
  `Avalonia.Android` has no `net10.0` asset. The only mechanism that works: **keep the head out of
  `EmuShelf.slnx` and build it from its own path**, with a dedicated CI job. The macOS dev loop is
  then untouched. `EmuShelf.slnx` lists eight projects, not the seven the first draft counted.

**Answers:** does Avalonia render, does the GLES shelf draw, does SQLite work (it does — 
`SQLitePCLRaw.bundle_e_sqlite3` 3.0.3 ships `runtimes/android-arm64/native/libe_sqlite3.so`).
**Done when:** the app launches on device, imports a folder without a keyboard, and shows the library.

### D — Storage and permissions (before B, not after)

D produces B's input. Building B on Milestone 0's single data point and then discovering in D that it
varies by API level guarantees rework of every launch definition.

- All-files access for EmuShelf's own reads; SAF folder picking through `TopLevel.StorageProvider`.
- A URI-aware replacement for `TryGetLocalPath()` across the six picker sites.
- Verify `FolderScanner`, `RelativePathResolver` (its base-directory anchoring is meaningless when
  both app storage and `/storage/emulated/0` root at `/`), and the availability checker.
- Decide `allowBackup` explicitly. Android auto-backup is on by default and will silently ship the
  SQLite library, `Settings/`, and the Drive refresh token to another install.
- Decide what "portable" means here. App-private storage means *Clear data* wipes the library and
  uninstall deletes every scraped cover.
- **Use AVDs, not the device.** API 30/31/33/34 answer where each restriction bites; the Thor answers
  for one.

### B — Launching games

- `AndroidIntentLauncher` behind `ITrackedProcessRunner`, plus honest handling for the other 16
  `Process.Start` sites.
- Per-emulator Android launch definitions: package, activity, extras, **handoff strategy**, and
  maintenance status.
- **`<queries>` package visibility.** Presence detection on API 30+ requires every supported emulator
  package declared in the manifest, or `QUERY_ALL_PACKAGES`. Either way this is a regression against
  the desktop model where `EmulatorDefinition` is data: adding an emulator becomes an APK change.
- **Promote `GameLaunchDependencyResolver` to a primary launch path.** It runs only on the Flatpak
  branch today, covers only `.m3u`/`.cue`/`.gdi`, and *throws* on a missing reference — which would
  convert today's emulator-tolerated cases into launch failures. It needs arcade `.zip` parent/BIOS
  sets, and a softened failure mode.
- **The per-emulator setup checklist**: has the user granted this emulator access to the library
  folder? With a verify step.
- **Re-express the exit signal.** Not `onResume` — since Android 10 multiple activities can be
  resumed at once, and the Thor is literally a multi-display device. The signal is
  `onTopResumedActivityChanged(boolean)`. And it must survive **process death**: after launching a PS2
  emulator on a handheld, EmuShelf is a prime kill candidate, so pending-sync state has to be
  persisted, not held in memory.

**Done when:** a game launches in the right emulator and returning to EmuShelf syncs.

### C — Controller input and text entry

- `IGamepadReader` over Android `InputDevice`/`MotionEvent`. Note the impedance mismatch: the
  interface is **polling** (`IsAvailable`, `Read()`); Android input is **event-driven** and arrives at
  the Activity, where Avalonia's own key handling already consumes it.
- An Android on-screen keyboard implementation of `IOnScreenKeyboardService`, without which gamepad
  search and rename do not work.
- Back-gesture vs B-button arbitration.
- Map the Thor's controls against the existing navigation model.

**Cannot be validated off-device** — and per the project's own notes there is no pad on the dev
machine at all, so the SDL path has never been hand-verified either. This is why C's probe is folded
into Milestone 0.

### E — Cloud sync

Detail in `docs/cloud-sync-portability-plan.md`.

**Status — E-desktop is done (updated 2026-08-17; supersedes the "NOT DONE" list this section
carried at planning time).** Google Drive is now the **sole** cloud transport: rclone was removed
entirely (`10cdc4e refactor(save-sync): remove rclone`), so there is no connection-method chooser —
`CloudTransportKind.Rclone` survives only as a legacy "not connected" marker for a stale pre-existing
connection. The managed Drive transport, its coordinator wiring, the desktop settings UI **and** a
controller-native gamepad connect flow are all on `main`. Reachable end to end on desktop and in
gamepad mode: a "Connect Google Drive" action opens Google's sign-in in the browser and stores only
the refresh token, with a transport-aware connected summary and disconnect. A code-review pass fixed
five transport defects (403 rate-limiting mis-read as a fatal reconnect; non-deterministic
duplicate-blob resolution; an unbounded resumable-upload loop; a date-form `Retry-After` dropped; a
pre-cancelled sign-in reported as a failure), and later work bounded **real** Google Drive stalls so a
flaky sync fails fast instead of hanging (`1a91040`, PR #148 `gdrive-save-sync-debug`, PR #149
`gamepad-save-sync-steamdeck`) — flaky-stall behaviour is only observable against the live API, so the
planning-time "no sign-in has ever hit Google's real API" no longer holds.

**Resolved since the first draft (this section's old "NOT DONE" list):**
- **Gamepad mode connects the built-in transport.** `GamepadSettingsViewModel` renders a
  controller-native "Connect Google Drive" / "Disconnect Google Drive" pair (PR #149). The
  `allowManagedTransport` suppression flag is gone from the codebase. The planned "the gamepad Saves
  section needs a full rebuild" is done.
- **The rclone chooser is removed** — Google Drive is the only transport, so there is nothing to
  choose, on desktop or in gamepad mode.
- **A real Google Drive sign-in path is exercised** (see the debug/hardening commits above), not just
  the in-memory fake used by tests.

**Remaining, desktop (verification, not code):** confirm the production OAuth client
(`EMUSHELF_GOOGLE_OAUTH_CLIENT_ID` / `EMUSHELF_GOOGLE_OAUTH_CLIENT_SECRET`) is provisioned in the
release build — the plumbing (`EmbeddedSecrets`, `GoogleOAuthClient`, `LoopbackOAuthRedirectHandler`)
is complete; and Windows/Linux remain unverified for the sync path (macOS-tested).

**Remaining, Android:**

1. A second OAuth client (public, no secret, custom-scheme redirect). `EmbeddedSecrets` already models
   the client id/secret; the Android client is a public variant of it.
2. A custom-scheme `IOAuthRedirectHandler`. The interface exists; only the loopback implementation
   does.
3. Force `TransportKind` to `GoogleDrive` on the Android head. Google Drive is already the only
   transport and the gamepad connect flow already exists (that rebuild is done, above), so this is now
   just the head defaulting the transport — not the "rclone UI rebuild" the first draft anticipated.
   The connection state is `RemoteName` in Core (`CloudRemoteName` is only the App view-models'
   editable field).
4. An Android `IProtectedTextStore` (Keystore / EncryptedSharedPreferences). The Windows
   implementation P/Invokes `crypt32.dll` and the fallback is obfuscation, neither of which is right
   for a refresh token.
5. **A SAF-backed `ILocalSaveEndpoint` — budget this as a rewrite, not a swap.** The interface is
   stream-shaped but the implementation is 448 lines of path work: cross-directory `Directory.Move`
   for the atomic swap, `File.SetLastWriteTimeUtc` (which the manifest uses as the conflict
   tie-breaker), recursive delete, and `Path.GetFullPath` containment. SAF has no cross-tree atomic
   rename, no settable mtime, and no path containment. `SaveUnitLocation` is a path record in Core and
   changes with it.
6. Per-emulator Android save providers, and the capability probe from the section above.

### F — Packaging and release

- APK/AAB from a **dedicated CI job** (the head is outside the solution, so the existing 3-OS matrix
  will not build it). Cache the workload; it is minutes per run on top of a JDK and SDK 36.
- Signing keystore. This is a permanent, unrecoverable obligation — lose it and every user must
  uninstall to upgrade. It deserves its own DECISIONS entry.
- `EmbeddedSecrets.targets` gains the Android OAuth client id — one `Append(...)` line plus one
  accessor, verified.
- **Android developer verification.** Enforcement begins 30 September 2026 in Brazil, Indonesia,
  Singapore and Thailand, expanding globally in 2027, on certified devices with Play services — which
  the Thor is. Unverified apps are not blocked outright but must be installed through the advanced
  sideloading flow or ADB, and **unverified developers cannot push updates.** Milestone F needs a line
  on registering an identity, and install docs for users on post-enforcement devices.
- Note EmuShelf is GPLv3, which is an independent obstacle to Play distribution — all-files access is
  not the only thing ruling it out.
- **Do not add the Android artifact to the existing release job** while it is experimental: that job
  runs with `fail_on_unmatched_files: true`, so an Android failure would block the Windows, macOS and
  Linux release.

## Sequencing

**0a → A0 → 0b → A1 → D → B → C → E-android → F**, with E-desktop parallel throughout. **E-desktop is
now complete** (rclone removed, built-in Google Drive is the sole transport with desktop and gamepad
connect flows); only the Android half (E-android) and the OAuth-client provisioning check remain.

Changes from the first draft: A0 is new and comes first among the engineering work; **D moves before
B** because D produces B's input; the GL and pad probes move into Milestone 0, because each can end
the project and both are nearly free once a device is booted; and Milestone 0 splits at the delivery
date.

E-desktop was genuinely parallel and improved the shipping product either way — removing rclone deleted
its download steps from `build.yml` and a bundled binary from all three artifacts.

### While the Thor is in transit

Nothing on the critical path needs the device, which is the useful accident of ordering it before
starting. In rough priority:

1. **Toolchain** — JDK 21, Android SDK platform 36, `dotnet workload install android`, an arm64-v8a
   API 33 AVD. Half a day, and it gates everything else.
2. **0a, the AVD spike.** The format ladder × emulator matrix, the GL probe, and the EmuShelf-side
   CHD read. This answers most of what sizes B, D and E.
3. **A0, the desktop project split.** The longest single pole, needs no Android anything, and is
   verified by the existing 1,826 tests. If the Thor is more than a couple of weeks out, this is where
   the time goes.
4. **E-desktop** — merge the unmerged Drive commit, ship the settings UI, make one real Google
   sign-in. Improves the shipping product regardless.
5. **Prep the device's inputs**: a test library with one multi-disc PS1 game (two `.bin`/`.cue` pairs
   plus an `.m3u`), one single-file `.chd`, one `.rvz`, and the list of emulator APKs to install.

### Day one on the Thor

Answer these before writing Android code against assumptions:

- **Android version.** The plan assumes 13 (all firmware notes through Feb 2026 are 1.0.0.x on
  Android 13). If an OTA has moved it to 14+, the foreground-service and notification-permission
  notes in B and E become live.
- **Which emulator builds are actually installed**, and their versions — the intent APIs in this plan
  were observed against specific builds.
- **Whether "Run script as Root" is present** in Thor settings. It decides whether the capability
  probe finds `Android/data` readable, and therefore whether M40 hotkeys, M32 texture packs, M33 and
  full save sync survive.
- **Wireless ADB pairing**, so the `adb install` / `logcat` / `screencap` loop works from the dev
  machine.
- **The second screen**: what it does by default, and whether a `Presentation` surface is available to
  a third-party app.
- **How the ROM library gets onto the device**, in practice, at size. This is the real first-run
  experience and nothing in the plan substitutes for trying it.

## Effort

**Writing the code is not the schedule.** Milestone 0a — toolchain from zero, five emulators
installed, manifests analysed, the multi-disc question answered — took about an hour. Size the rest
the same way: in agent sessions, not person-weeks.

| Milestone | Sessions | What could stretch it |
|---|---|---|
| 0a — toolchain + AVD spike | done | — |
| A0 — desktop split | 1–2 | 17 snapshot baselines and 19 `avares://` URIs have to come out green |
| A1 — head + skeleton + gamepad import | 2–3 | Gamepad import is a real feature; GL may not initialise |
| D — storage & permissions | 1–2 | Grows if the SAF-only emulators need EmuShelf-side SAF readers |
| B — launching | 1–2 | Mostly per-emulator definitions, and Cocoon's configs are a working reference |
| C — controller + IME | 1–2 | Pad behaviour is unverifiable until the Thor is here |
| E — desktop remainder | done | Drive transport, desktop + gamepad connect flows, real-API hardening all landed; rclone removed. Only remaining: confirm the production OAuth client ships in the release build |
| E — Android | 2–3 | SAF save endpoint is the one genuine rewrite |
| F — packaging | 1 | Keystore and CI job |

**What actually gates the calendar**, none of which goes faster with more code written:

1. **The Thor is not here yet.** 0b, all of C, and every acceptance check wait on delivery.
2. **BIOS and system files.** ARMSX2, aPS3e and Azahar need them; they are yours to supply and no
   amount of agent time substitutes.
3. **Things only judged by hand** — does the shelf look right, does the pad feel right, does a real
   game boot.

So: the implementable surface is roughly a week of sessions. Everything after A1 that touches the
device is bounded by the device, not by the work.

## Test strategy

- **Core, Integrations, Infrastructure tests keep running unchanged** on the desktop TFM. Anything
  Android-specific goes behind a Core interface.
- **Android logic stays in a `net10.0` assembly** — intent construction, handoff-strategy selection,
  path/URI mapping, save-location resolution are pure functions and belong where the existing suite
  runs them. State this as a rule, or a third `net10.0-android` test project appears and needs an
  emulator.
- **Compiled bindings are the trimming trap.** `x:CompileBindings` appears zero times; all 4,784 lines
  of XAML use reflection `{Binding}`. Safe at the default `AndroidLinkMode=SdkOnly`; raising link mode
  to shrink the APK produces silent blank bindings, not compile errors. Also note
  `RunAOTCompilation` defaults to true for Release Android builds.
- **Visual snapshot tests stay desktop-only.**
- **CI floor: the APK builds on every PR touching `src/`.** An emulator-based UI test that flakes is
  worse than none, but "only if it can be made reliable" is not a floor — without the build job the
  head silently rots and A0's refactor breaks it unnoticed.

## Development setup

**Done, 2026-08-15.** The full chain builds, installs, launches and renders on an AVD: .NET 10 → APK
→ Android 13 arm64. No sudo was needed at any point. Exact working configuration:

| Piece | Value |
|---|---|
| JDK | `brew install openjdk@21` → 21.0.12, keg-only |
| `JavaSdkDirectory` | `/opt/homebrew/opt/openjdk@21/libexec/openjdk.jdk/Contents/Home` |
| SDK | `brew install --cask android-commandlinetools` |
| `AndroidSdkDirectory` | `/opt/homebrew/share/android-commandlinetools` |
| Packages | `platform-tools` 37.0.1, `platforms;android-36`, `build-tools;36.0.0`, `emulator` 37.1.11, `system-images;android-33;google_apis;arm64-v8a` |
| Workload | `dotnet workload install android` → `Microsoft.Android.Sdk.Darwin` 36.1.69 |
| AVD | `emushelf-api33` — pixel_6, Android 13 (API 33), arm64-v8a |

Three traps, all hit and resolved:

1. **`JavaSdkDirectory` must be the bundle path, not the Homebrew prefix.** `/opt/homebrew/opt/openjdk@21`
   has `bin/java` and passes `java -version`, but has no `release` file, so the Android SDK rejects it
   with `XA5300: The Java SDK directory could not be found`. This is the macOS JDK-discovery failure
   the research flagged, in its Homebrew form. Set the property explicitly; do not rely on
   `/usr/libexec/java_home`, which fails on this machine with "Root element is missing".
2. **The workload pulls `Microsoft.Android.Sdk.Darwin` 36.1.69** — independent confirmation that API
   36 is the floor, matching `Avalonia.Android` 12.1.0's single `net10.0-android36.0` asset.
3. **.NET mangles activity names to `crc64<hash>.MainActivity`.** `adb shell am start -n
   <pkg>/<namespace>.MainActivity` fails with "does not exist"; resolve the real name with
   `adb shell cmd package resolve-activity --brief <pkg>`. Any activity EmuShelf exposes to other
   apps needs an explicit `Name=` in its `[Activity]` attribute, or the name changes under it.

Still to come: arm64-v8a AVDs at API 30/31/34 for Milestone D, and **wireless ADB to the Thor**,
which matters more than the emulator.

## What cannot be verified without hardware

- Milestone 0's matrix, in full.
- All of Milestone C.
- Whether DuckStation Android resolves `.m3u`/`.cue` siblings from a content URI. Community reports
  **directly conflict** and its source is not readily inspectable after the licence change.
- Whether the Thor's driver advertises `EGL_KHR_get_all_proc_addresses` (Avalonia resolves GL entry
  points only through `eglGetProcAddress` with no dlsym fallback — this project's own preview tool
  adds that fallback and says why) and `EXT_color_buffer_half_float` (the IBL cube is allocated
  `Rgba16f` and rendered into, which ES 3.0 does not guarantee is colour-renderable; there is no
  check).
- Whether Avalonia 12.1.0's Android EGL path implements the context-sharing feature
  `OpenGlControlBase` requires. **This decides whether the 3D shelf renders at all**, it is strictly
  larger than the shader-dialect question, and it is answerable in an emulator in an afternoon.
- Real GPU behaviour, thermals, OLED retention (AYN shipped anti-burn-in modes; a static 3D shelf is a
  real concern).
- Every cloud-sync test to date runs against an in-memory fake Drive.

## Roadmap integration

M41 and M42 are taken; there are already two M40s. Android is **M43+**, one milestone per section
above, once Milestone 0 has reported. Note that [ROADMAP.md:522](../ROADMAP.md:522) states M24 is a
product-hardening gate to be completed "before adding new end-user features" and its Phase 0 is
entirely unchecked — starting Android is a decision to set that aside, which is fine if made
knowingly.

A DECISIONS entry lands when Milestone 0 reports, not now.
