# Android port plan

Target: **AYN Thor** (Snapdragon 8 Gen 2, Android 13, dual-screen clamshell) — owned; **delivered and
driven over USB ADB as of 2026-08-18** (`adb -s 2fd555f4`; see "Milestone 0b — first device facts").
Architecture targets Android arm64 handhelds generally; every acceptance gate is the Thor. Status
(2026-08-22): **0a, A0, 0b, A1/A2, B, C, D, E and F are all built, and cloud sync is verified end-to-end
on the Thor against real Google Drive** — the app imports a real SD-card library without a keyboard
(all-files grant UX + a 41-game scan verified), launches real games in their emulators from the couch,
runs post-play completion on return (surviving process death), reads analog sticks / D-pad / IME /
back-vs-B (C), and **syncs saves to Google Drive with a controller-native connect + per-system Save-folder
UI** (E-android). PS1 (DuckStation), GameCube and Wii (Dolphin) round-tripped over real Drive; PS2/PSP/3DS
are wired and RetroArch systems ship the launch-config fix in the signed v1.5.8 APK — those await only an
on-device play-test pass, not code. What genuinely remains is **Milestone S (stabilization)** plus a few
known limitations (PS1 owner-only cards, PS2 single-file `.ps2` churn — see E). See **"Current status and
what's next"** under Sequencing for the milestone-by-milestone checklist.

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
| Everything lost shares one cause: `Android/data` | Three different causes. ~~And on the Thor root is one documented Settings toggle~~ — **corrected 0b (2026-08-18): "Run script as Root" is a one-shot `.sh` runner, not a persistent grant; there is no ambient `su`. v1 is strictly no-root. The list stays capability-gated, but the free root path this row assumed does not exist on firmware 1.0.0.377** |
| "Launching is behind one interface" | 17 `Process.Start` sites across 12 files; one is `TrackedProcessRunner` |
| "Core and Integrations are portable as-is" | They compile. 53 OS-branch sites across 29 files, **including Core**, and `OperatingSystem.IsLinux()` returns *false* on Android, so every `IsWindows/IsMacOS/else-Linux` ladder takes a wrong branch |
| "Descriptor trees are already resolved" | `GameLaunchDependencyResolver` runs on the Flatpak branch only ([EmulatorLaunchService.cs:182](../src/EmuShelf.Core/Launching/EmulatorLaunchService.cs:182)) and hard-throws on a missing reference |
| `ILocalSaveEndpoint` is stream-based, so SAF is a swap | The interface is; the 448-line implementation is `Directory.Move`/`File.Move`/`SetLastWriteTimeUtc` path work, and `SaveUnitLocation` is a path record in Core |
| "New App.Android referencing the existing App" | The composition root lives inside `if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime)` — ~265 of 314 lines of `App.axaml.cs`. The head would link, launch, and show nothing |
| The solution pins 12.1.0 throughout | `Avalonia.Controls.ItemsRepeater` is 12.0.0 and no 12.1.x exists. It backs the gamepad grid |
| Milestone E is "Done (Phases 1–2)" | It is one unmerged 28-file commit on a side branch, never on `main`, and **this plan document lives in that same commit** |
| Fold in as M41+ | M41–M43 are taken (M43 is Playtime tracking, [ROADMAP.md:1849](../ROADMAP.md:1849)). Android is **M44+** — the ROADMAP umbrella is M44 |
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
now-playing. "Nothing" is an acceptable answer, stated. **(0b, 2026-08-18: the `Presentation` surface is
confirmed present and available to third-party apps on-device — `displayId=4`, `FLAG_PRESENTATION`,
1240×1080, 120 Hz. Now a pure product choice, not gated on a hardware unknown. A0/A1 shipped before this
was decided, so the second screen is currently unused — revisit as its own item.)** **DECIDED
(2026-08-22): a companion surface — app dock, all-apps drawer, RetroAchievements panel, dimmed
game-logo idle. Scoped as [Milestone SS](#ss--second-screen-thor-dual-screen-companion).**

## What Android v1 is, and when to stop

The first draft defined neither, and gave kill criteria to Milestone 0 only — so every milestone
after it was implicitly unabandonable. Both gaps are filled here.

**Android v1 is:** the Gamepad shell on the Thor, importing a library without a keyboard, launching
at least one maintained emulator per supported system, syncing the saves that are reachable, and
saying honestly why the rest are not. Shipped as a sideload APK from a GitHub release, marked
experimental.

**Android v1 is not:** feature parity with desktop, Play Store distribution, phone or tablet support,
automatic RetroArch core installation, or any feature that writes an emulator's configuration.

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

## Milestone 0b — first device facts (2026-08-18)

The Thor arrived and is driven over USB ADB (`adb -s 2fd555f4`). This is the day-one pass: the device
questions answered, the installed-emulator set recorded, the handoff matrix not yet re-run. A1 was also
installed and verified here (see the A1 section). What 0b establishes:

**Confirmed as assumed.** Android 13 / SDK 33, arm64-v8a, firmware `Thor_V1.0.0.377_20260206` — no OTA
past 13, so the foreground-service and notification-permission escalations in B and E stay dormant.
Main screen 1920×1080 landscape, 120 Hz, density 369.

**The second screen is available to third-party apps.** Live standard display (`displayId=4`,
"Screen-2", 1240×1080 landscape, 120 Hz) carrying `FLAG_PRESENTATION`, so a `Presentation` can target
it. AYN's own `com.odin.dualscreen.assistant` drives it. The open second-screen decision is now a pure
product choice.

**Root is not one toggle** — no ambient `su`; "Run script as Root" is a one-shot `.sh` runner. v1 is
strictly no-root (capability model corrected above).

**The CRT tube renders on real Adreno GL.** A1's one open item (1×1-px tube on the AVD's software GL) is
resolved: on the Thor the tube paints full-screen (phosphor/scanline sheen across 1920×1080). A
software-GL artifact, not a shell defect.

**Installed emulator set differs from the AVD — build-sensitivity confirmed:**

| System | Thor build | vs. 0a AVD |
|---|---|---|
| PS1 | **DuckStation** `com.github.stenzek.duckstation` (from Play) | Absent on the AVD; its content-URI sibling resolution — listed unverifiable-without-hardware — is now testable |
| PS2 | **ARMSX2** `com.armsx2` 2.6.6.7 (targetSdk 37) | ~~AetherSX2~~ — corrected 2026-08-20: the installed PS2 build is the ARMSX2 fork, same family as the AVD |
| DS | **WatermelonDS** `me.magnum.melondualds` 0.7.0.rc5 | added 2026-08-20; label is "WatermelonDS" (melonDS fork, kept melonDS's package id) |
| GC/Wii | Dolphin `org.dolphinemu.dolphinemu` 2606a | same package (rejected FileProvider, wants a SAF tree URI) |
| PSP | PPSSPP `org.ppsspp.ppsspp` v1.20.4 | same (content URI works, path refused) |
| 3DS | Azahar `org.azahar_emu.azahar` 2126.0-vanilla | same (first-run wizard gate) |
| multi | RetroArch `com.retroarch.aarch64` 1.22.2_GIT | same (targetSdk 28, plain-path) |
| PS3 | — (aPS3e not installed) | supply if PS3 handoff is to be measured |

Reference frontends installed on-device: `com.neogamelab.neostation` (NeoStation) and
`rip.moth.cocoonshell` (Cocoon) — both studied in "Prior art"; their real launch intents can be
observed live.

**OLED burn-in is a shipped concern, not hypothetical.** Thor settings exposes "OLED Screen
Protection", and a "Burn-in Protection Refresher" window was observed firing; a static 3D shelf should
respect it.

**Still to run on-device:** the handoff matrix against these real builds (DuckStation first), the pad
and `Android/data` capability probes, and BIOS-gated full boots (owner to supply BIOS for AetherSX2 /
Azahar). `/sdcard/ROMs` is currently empty — a test corpus is needed to see a populated shelf. None of
this blocks A1, which is done.

**0b progress (2026-08-18).** Synthetic corpus pushed to `/sdcard/EmuShelfTest/` (single `.iso`; two
`.bin`+`.cue` pairs; an `.m3u` over both). First measurement, **DuckStation** (PS1, the AVD-unverifiable
case): handed `-e bootPath /sdcard/EmuShelfTest/game.m3u` on its `MainActivity`, it **ignored the
plain-path handoff** and showed "No games were found — Add Game Directory". `appops set … MANAGE_EXTERNAL_STORAGE
allow` was a no-op — **DuckStation declares no all-files access**, so a bare path is unreadable; it needs a
content URI backed by its **own persisted directory grant** (strategy 4). So the per-emulator setup
checklist is a hard gate on the device, exactly as on the AVD.

**RetroArch (plain-path) confirmed on the Thor.** `am start … RetroActivityFuture -e ROM
/sdcard/EmuShelfTest/game.m3u -e LIBRETRO …/swanstation_libretro_android.so`: RetroArch logged `[ENV]
Auto-start game "…/game.m3u"`, loaded the SwanStation core, and went to a fullscreen running state — the
plain-path `.m3u` handoff is accepted, matching the AVD's byte-for-byte sibling-resolution result.
RetroArch's targetSdk 28 gives it all-files, so no grant was needed and no core `.so` had to be named by
us beyond the standard filename. (Its cores live in app-private storage, unreadable without root.)

**Corpus limitation — the synthetic-zeros trick only carries the open-then-read emulators.** RetroArch
opens the file regardless of content, so zeros suffice. **DuckStation validates the disc image before it
will list or boot it**, so the zero-filled corpus is rejected ("No games were found" even after the folder
was granted) and it cannot be driven to the sibling-resolution step this way. So the content-URI /
validating emulators (DuckStation, PPSSPP, Dolphin, AetherSX2, Azahar) need **at least one valid small disc
image** — and BIOS for a real boot — neither of which is on the device yet. That is the gate on the rest of
0b. Continuing also needs either per-emulator folder grants or EmuShelf's own `FileProvider` (Milestone B).

**0b progress (2026-08-20).** A pass of the on-device probes that need neither a valid disc image nor
BIOS. What it settles, and what stays gated:

- **Exact emulator versions captured** (the 0b table left these open): DuckStation
  `0.1-8969-g611bb8fb4` (targetSdk 35), Dolphin `2606a` (36), PPSSPP `v1.20.4` (36), Azahar
  `2126.0-vanilla` (35), RetroArch `1.22.2_GIT` (**28**), NeoStation `0.10.0`, Cocoon `3.04`.
- **Two build drifts from the 0b table**, both build-sensitivity in action: **PS2 is now ARMSX2
  `com.armsx2` 2.6.6.7 (targetSdk 37)**, not the AetherSX2 the table recorded; and the DS emulator
  installed is **WatermelonDS** (package `me.magnum.melondualds`, aapt `application-label:'WatermelonDS'`,
  0.7.0.rc5) — a melonDS fork that kept melonDS's package id, which is why the package string reads
  "melondualds". The E save table's "WatermelonDS" naming was right. Table below corrected.
- **The device is not rooted** — `su` is `inaccessible or not found`, and per the owner it never was. The
  E-android per-emulator save findings (2026-08-19) were taken via the **CX File Manager app with no root**,
  which reaches `Android/data` on this firmware without `su`. So the `Android/data` save cases (DuckStation
  / PS2 / Dolphin) are *not* root-gated on the Thor; they are already mapped (see E). The open item is which
  no-root mechanism EmuShelf itself can use to reach `Android/data`, not re-measuring with root.
- **EmuShelf head's all-files grant is wiped** (`appops … MANAGE_EXTERNAL_STORAGE` → `Default mode:
  default`), matching the `pm clear` during A2. It must be re-granted before any EmuShelf-side read test.
- **Pad-probe surface confirmed present.** `dumpsys input` enumerates a `KEYBOARD | GAMEPAD | JOYSTICK`
  device (`Xbox Wireless Controller`), plus `gpio-keys` and AYN's `ODIN Station Virtual Mouse`. A real
  gamepad/joystick source exists to read for Milestone C; the full probe (events reaching the Activity)
  is C's, and A1 already proved `DispatchKeyEvent` routing on device.
- **The `Android/data` capability probe cannot be answered by adb.** `run-as com.emushelf.app ls
  /sdcard/Android/data/<pkg>/…` *succeeds* (returned DuckStation's `bios/cheats/covers`), but this is a
  **false positive**: `run-as` spawns a fresh process outside the app's runtime mount namespace and so
  bypasses the FUSE `Android/data` restriction a real app hits. The honest probe is the app itself
  attempting `File.Open` at runtime — i.e. the capability-probe feature the capability model calls for,
  not an adb shortcut. Do not record "EmuShelf can read `Android/data`" off `run-as`.
- **DuckStation's exported surface is `.MainActivity` only** (MAIN/LAUNCHER; no VIEW filter with a data
  scheme). So its content-URI route is `-e bootPath <content://…>` into a tree DuckStation itself was
  granted — which needs a valid disc it will list *and* driving its SAF folder-grant UI. Same gate.

**The real library and BIOS are on the microSD, not internal storage** (corrected same day). `/sdcard`
(internal) is empty of games — the earlier "no corpus, gated on owner files" reading was wrong because
it only scanned internal storage. The owner's full collection is on the SD card mounted at
`/storage/AE6A-1092`: **374 CHD, 98 RVZ, 125 NDS, 54 3DS, 23 `.m3u`**, real multi-disc PS1 sets (Metal
Gear Solid, Parasite Eve I/II, Xenogears, Resident Evil 2, Koudelka) with `.m3u` playlists, plus **BIOS
for PS2 (`SCPH50003` .bin/.mec/.nvm) and PSX (`SCPH-101`)**. Every emulator is pre-configured against this
card. So 0b is *not* gated on owner-supplied inputs after all — they are present; the gate was that the
files sit on a volume the first scan missed.

**Strategy 4 is live, not hypothetical — the whole device is already wired this way.** The persisted-URI
dump (`dumpsys activity permissions`) shows **each emulator holds its own persisted `com.android.externalstorage`
tree grant, scoped to exactly its system folder** on the SD:

| Emulator | Persisted SAF tree grant(s) |
|---|---|
| DuckStation (PS1) | `AE6A-1092:roms/psx`, `primary:ROMs` |
| ARMSX2 (PS2) | `AE6A-1092:roms/ps2`, **`AE6A-1092:bios/ps2`**, `primary:User/ARMSX2` |
| Dolphin (GC/Wii) | `AE6A-1092:roms/ngc`, `AE6A-1092:roms/wii` |
| PPSSPP (PSP) | `AE6A-1092:roms/psp`, `primary:User/ppsspp` |
| Azahar (3DS) | `AE6A-1092:roms/3ds`, `primary:User/Azahar` |
| WatermelonDS (DS) | `AE6A-1092:roms/nds`, `AE6A-1092:saves/Nintendo DS` |

This settles two things the plan flagged as open. (a) **Sibling resolution is covered by construction:**
each grant is the *whole* `roms/<system>` subtree, so an `.m3u` and the sibling `.chd`s it names all fall
under one grant — the "descriptors are impossible over content URIs" worry does not apply when the
emulator holds a tree, not a single-file grant. (b) **The handoff shape is specific:** the URI EmuShelf
hands must be the **tree-scoped** `…/tree/<TREE>/document/<DOC>` form matching the persisted prefix, not a
bare `…/document/<DOC>`. The per-emulator "setup checklist" in Milestone B is therefore concretely: *does
this emulator already hold a tree grant whose prefix covers the game's folder?* — and note an app can only
enumerate its **own** grants, so EmuShelf cannot read another app's grant to pre-verify; it must hand the
URI and treat failure as "walk the user through granting."

**The exact per-emulator launch intents, recovered as ground truth from working frontends on this device.**
Cocoon writes every intent it fires to `/sdcard/Cocoon/launch_debug.log`, and NeoStation stores its
emulator table in `/sdcard/User/Neostation/data.sqlite` (`app_emulators` → `android_package_name` /
`android_activity_name`). Between them, plus DuckStation's own manifest+dex, the real launch shapes are:

| System / emulator | Component | Action | ROM payload |
|---|---|---|---|
| PS1 / DuckStation | `com.github.stenzek.duckstation/.EmulationActivity` (exported, no filter) | explicit | **extra `bootPath`** = tree-scoped content URI, `--ez isOneShot true` |
| PS2 / ARMSX2 | `com.armsx2/com.armsx2.Main` (exported, VIEW filter, `content`/`file` schemes) | `VIEW` | content URI **as intent DATA** (no extra); also declares `MANAGE_EXTERNAL_STORAGE` so a `file` path works too |
| GC·Wii / Dolphin | `org.dolphinemu.dolphinemu/.ui.main.MainActivity` | `MAIN` | **extra `AutoStartFile`** = content URI |
| PSP / PPSSPP | `org.ppsspp.ppsspp/.PpssppActivity` | `VIEW` | content URI **as intent DATA** (no extra) |
| 3DS / Azahar | `org.azahar_emu.azahar/org.citra.citra_emu.activities.EmulationActivity` | `VIEW` | content URI **as intent DATA** |
| DS / WatermelonDS | `me.magnum.melondualds/me.magnum.melonds.ui.emulator.EmulatorActivity` | `me.magnum.melondualds.LAUNCH_ROM` | **extra `uri`** = content URI |
| RetroArch | `com.retroarch.aarch64/com.retroarch.browser.retroactivity.RetroActivityFuture` | `VIEW` | extras `ROM` (path) + `LIBRETRO` (core `.so`) + `CONFIGFILE`/`DATADIR`/`SDCARD`/`EXTERNAL` (+ `APK`/`IME`) |

> **`CONFIGFILE` is load-bearing (2026-08-21).** Cocoon's log records its RetroArch intent extras as
> `DATADIR, SDCARD, EXTERNAL, APK, IME, ROM, CONFIGFILE, LIBRETRO`. EmuShelf originally sent only
> `ROM`+`LIBRETRO`, so RetroArch launched with a default config — the user's hotkeys, gamepad autoconfig
> and settings never loaded (matched a user report). Verified on the Thor by firing both intents: with
> only `ROM`+`LIBRETRO` the parse log emits no "Config file" line; adding `CONFIGFILE` (=
> `/storage/emulated/0/Android/data/com.retroarch.aarch64/files/retroarch.cfg`) plus `DATADIR`/`SDCARD`/
> `EXTERNAL` makes it load the real config and resolve the correct save/state/system folders. Now built
> in `AndroidIntentFactory` from the target package; `APK`/`IME` are omitted (install/device-specific and
> not load-bearing — the four-extra launch loaded everything correctly).

Every URI is the tree-scoped `…/tree/<TREE>/document/<DOC>` form. Three distinct payload conventions
(extra-named vs intent-DATA vs custom-action-plus-extra) confirm the plan's core thesis: **the handoff is
per-emulator, and the Android launch definition must carry component + action + payload-slot + strategy,
not just a path.** This *is* Milestone B's data model, now populated from live evidence rather than
guessed. (Cocoon's log had no PS1/PS2 launch, so DuckStation's shape came from its manifest — exported
`EmulationActivity` — and its dex string constants — `bootPath`, `gameTitle`, `isOneShot`, `getFullPathFromTreeUri`.)

**DuckStation PS1 handoff confirmed working (2026-08-20).** The earlier failures were my error: I fired
`bootPath` at **`MainActivity`** (the game list), which ignores it, and mis-read the resulting library scan
+ startup `BIOS is mapped` logs as a boot (a control launch with no `bootPath` emits those identically —
they are *not* a boot signal). Firing the correct intent — `.EmulationActivity` + `-e bootPath <tree-scoped
content URI to the real Metal Gear Solid `.m3u`>` + `--ez isOneShot true` — put **`EmulationActivity`** in the
foreground (the in-game screen, not the menu) and opened a **non-standby audio output track** that stayed
active, i.e. a running game producing sound; both signals were absent on every menu-only attempt. **The
owner then visually confirmed MGS was running and manually exited it** — so this is eyes-on confirmed, not
just inferred. So the
AVD-unverifiable question is answered: **DuckStation resolves a multi-disc `.m3u` and its sibling `.chd`s
from a tree-scoped content URI, and boots** — no copying, no derived descriptor needed for PS1 here.
Confirmation method note: for this closed, GL-bypass emulator the reliable external signals are
*foreground = `EmulationActivity`* and *an active (non-standby) audio track*, not screencap (GL surface
unreadable), `gfxinfo` (HWUI bypassed), or FUSE/streaming logs (content-URI reads go through
`openFileDescriptor`).

**ARMSX2 PS2 handoff confirmed working (2026-08-20).** `am start -a android.intent.action.VIEW -d
<tree-scoped content URI to a real `.chd`> -n com.armsx2/com.armsx2.Main` booted the disc: logcat shows the
PS2 IOP disc modules loading (`RegisterLibraryEntries: cdvdman version 1.01` / `cdvdfsv version 1.01`) —
an unambiguous disc-mount-and-boot signal, unlike DuckStation's opaque GL UI — alongside a non-standby
audio track and `com.armsx2.Main` in the foreground. ARMSX2 reads via its own persisted `roms/ps2` +
`bios/ps2` grants (BIOS `SCPH50003` present). So PS1 (DuckStation) and PS2 (ARMSX2) — the two systems the
0a/0b exit criterion hinges on — both accept a constructible content-URI handoff and boot on the Thor.

**Still genuinely gated:** nothing on the handoff question. Save-side, the `Android/data` cases are already
mapped (owner, via CX File Manager, no root — see E); the remaining E work is finding the no-root
`Android/data` access mechanism EmuShelf can use programmatically, not re-measuring. Optional: hands-on
full-boot passes of the remaining BIOS-gated systems (3DS Azahar) — their launch *shapes* are already
recovered above from Cocoon's live log.

**Post-A1 UI findings on the Thor (2026-08-18), and where they belong.** Two things real hardware
surfaced that A1's done-criterion did not cover; both are "judged by hand on the device" items the plan
said would wait for delivery:

- **The couch shell is oversized and vertical content overflows.** ✅ (2026-08-20, validated on the Thor)
  It is tuned for the Steam Deck's 1280×800; the Thor is 1920×1080 physical but only ~833×468 **dip** at
  its ~2.31× density, so the whole shell was oversized. Decision #2 ("do not hard-code one aspect ratio,
  one DPI") anticipated this. **Primary fix:** the Android head re-derives the activity resource density
  in `MainActivity.AttachBaseContext` toward a target dip width (`CouchTargetDipWidth = 1280`), giving the
  shell a Deck-class ~1280×720 dip canvas so everything scales down to fit — Android-only, guarded to only
  ever lower density. Avalonia honours the overridden activity resource density even though the
  window-manager display config still reports 369dpi. **Complementary fix:** the system menu's `Auto` View
  mode / Sort picker had starved the `*` option row to ~0px (Settings/Quit clipped); the picker now shares
  the option list's one scroll region. Both verified on the Thor.
- **Vertical gamepad menus do not scroll to follow the selector.** ✅ (2026-08-20) Folded into A2: the
  picker rows now join the option list's scroll-follow and the merged region brings each entry into view.
  The suspected Android focus-vs-selection split was **not** the cause — `RevealGamepadOverlayFocus`
  calls `BringIntoView` off the view-model selection, verified on desktop headless. (No longer a
  Milestone C item.)

Still open on A2 (not blocking): a **populated-library visual pass at the new density** — grid / list / 3D
shelf covers and the achievements / scraper / hotkeys overlays with real games. The density override is
global so they scale too, but cover sizing, text truncation, and shelf geometry deserve a real look. Gated
on staging ROMs on the Thor (the SAF all-files grant was wiped by a `pm clear` during A2 testing).

**Build-infra gap surfaced during A2:** the Android head is out-of-solution, so `dotnet build`/`dotnet test`
never compile it — `IDialogService` grew a `PickSaveArchiveAsync` member and the head's
`SingleViewDialogService` silently drifted until an on-device build failed. Worth a lightweight CI/pre-push
step that compiles `EmuShelf.App.Android` so shared-interface drift fails fast instead of at deploy time.

(Already fixed, separately: the dark-grey shelf backdrop and distorted couch text — two HiDPI/single-view
rendering bugs, see DECISIONS 2026-08-18.)

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
differently. **The earlier claim that "on the Thor root is one documented Settings toggle away" is wrong
(0b, 2026-08-18):** no ambient `su`, and AYN's "Run script as Root" is a one-shot `.sh` runner, not a
persistent grant — so **treat v1 as strictly no-root**, which is the base capability model anyway; the
root-gated extras below simply do not get a free ambient path on this firmware. Replace the static list
with a **runtime capability probe** ("can I read `Android/data/<pkg>`?") feeding the existing
"not possible here, and why" channels. That is less code than deleting three features, and it keeps
them alive for rooted and Shizuku users — a large fraction on a handheld. A probe `.sh` fed to "Run
script as Root" can still measure what one-shot root buys, if E ever needs it.

**Cause 1 — `Android/data` is restricted, but reachable on the Thor without root.** The generic-app
`File.Open` restriction is real for Android 12+ even with all-files access, and it gates M40 uniform
hotkeys, M32 texture-pack inventory, M33 auxiliary sync, and save sync for DuckStation / AetherSX2 /
Dolphin. Three emulators keep saves in a *normal* folder and are trivially reachable: PPSSPP, Azahar,
RetroArch — also the cleanest-handoff emulators, an argument for choosing targets on both properties
(one trap: PPSSPP records its memstick path in app-private storage, so it must be *asked for*, not
discovered). **The important correction (owner, 2026-08-20): the `Android/data` save set was reached on
the Thor with NO root — via the CX File Manager app, and the device is not rooted at all.** So the
capability is not "root-only" here; some file-manager access path to `Android/data` exists on this
firmware. The open question for E is therefore *which* mechanism EmuShelf can replicate programmatically
(a SAF tree grant to `Android/data/<pkg>` on Thor firmware, a Shizuku/`ADB` path, or similar) — not
"does root exist." Via that CX access the owner mapped all three gated cases: DuckStation syncs its
`Android/data` save 1:1, Dolphin reshapes the GameCube path (region+slot vs desktop's region-only), and
PS2 needs a format conversion rather than a copy. See the per-emulator save mapping under E — Cloud sync.

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

**Done, 2026-08-17.** `EmuShelf.App` was split into a shared `EmuShelf.UI` library (assembly
`EmuShelf.UI`, Avalonia-core only) and a thin desktop head that keeps the `EmuShelf` assembly name
(so the executable, launch scripts and `.app` are unchanged). The composition root is now
lifetime-agnostic: `App.Compose(...)` in the shared library builds the whole service graph and hands
the window-typed subset to an `IPlatformShell`, which the desktop head registers via
`App.DesktopShellFactory` — **this is where the A1 single-view (Android) head plugs in.** The window
services already sat behind interfaces (`IInterfaceModeService`, `IFrontendController`,
`IApplicationLifetimeService`, `IDialogService`), so only their `Window`-touching implementations moved
to the head. `DialogService`'s owner was re-typed `Window` → `TopLevel` (pickers are now host-agnostic;
the modal `ShowDialog` sites stay desktop-only behind a `Window` cast). The 19 `avares://EmuShelf/`
URIs became `avares://EmuShelf.UI/`. Full Release suite green (1128 + 888). **One item deferred to
A1:** splitting the 5,119-line `MainWindow.axaml` so the `GamepadRoot` can host in a single view —
A0's done-criterion holds with `MainWindow` moved whole to the head, and the XAML split's only consumer
is A1's single-view hosting. See DECISIONS 2026-08-17.

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

**Gamepad mode's desktop escape hatches — close each, don't port desktop.** The right mental model:
Android runs *the gamepad shell made self-sufficient*, not an adaptation of desktop mode. Desktop mode
does not exist on Android — the desktop head (`MainWindow`, the 9 dialog `Window`s, the grid,
`SteamInputTemplateInstaller`) never links into the Android head; A0 already put all of it behind
`App.DesktopShellFactory`/`IPlatformShell`, so the Android head simply registers its own single-view
shell and omits them. The gamepad view-model already lives in shared `EmuShelf.UI` and comes along for
free. The catch is that the gamepad view-model is **not self-sufficient today** — in several flows it
does not implement the action, it hands off to Desktop mode. Every one of those hatches must be
replaced by a gamepad-native flow or an honest "unavailable here"; none is dead code, all of it runs on
Android. Checklist:

- **Import** — `AddGamesCommand`/`AddFolderCommand` are bound only in the desktop head's
  [MainWindow.axaml](../src/EmuShelf.App/Views/MainWindow.axaml); on Android there is no binding at all.
  This is the same item as "Gamepad-native library import" above.
- **Empty-library copy** — the couch empty state hardcodes *"No games are available in this view. Use
  Menu to switch to Desktop mode and add games."* ([GamepadShellView.axaml](../src/EmuShelf.UI/Views/GamepadShellView.axaml)).
  On Android that is the literal first-run screen and it points at a mode that does not exist. It must
  read as a gamepad-native call to import once import lands. Distinct from the system-menu "Switch to
  Desktop" hatch below because it is static copy, not a command — easy to miss.
- **Cover setting** — "Set cover" opens the `CoverDesktopHandoff` overlay
  ([MainViewModel.cs:2006](../src/EmuShelf.UI/ViewModels/MainViewModel.cs:2006)) whose whole job is to
  route the user to Desktop mode. Needs a gamepad-native cover picker or a clean "not here".
- **Switch to Desktop** — the system menu offers `RequestDesktopModeFromGamepadCommand` /
  `SwitchToDesktopModeCommand` ([:2961](../src/EmuShelf.UI/ViewModels/MainViewModel.cs:2961)); on
  Android this option must not appear, and the `DesktopModeConfirmation` overlay path is unreachable.
- **Search / rename text entry** — routes through `IOnScreenKeyboardService`, whose only implementation
  is Windows osk; on Android it falls back to a hardware keyboard. This is Milestone C's IME work; until
  it lands, gamepad search is unusable.
- **Saves** — ✅ **done in E-android (`740b4d6`).** The gamepad Saves rows were rebuilt with a
  controller-native managed-connect + per-system Save-folder picker; the earlier
  `allowManagedTransport: false` suppression is gone and the built-in transport is reachable on the
  gamepad-only Thor (verified over real Drive).
- **Sort columns** — the couch Sort row offers only `GamepadSortColumns`; any column "set on the
  desktop" ([:1895](../src/EmuShelf.UI/ViewModels/MainViewModel.cs:1895)) falls back. Verify the
  fallback is sane when no desktop ever set one.

A1 owns the first three (they gate a usable first run); C owns search IME; E-android owns Saves. The
rule for all of them: make the `InterfaceMode.Desktop`-aware branches in shared `EmuShelf.UI` degrade
sensibly when desktop is unreachable, rather than assuming they are dead code.

**Answers:** does Avalonia render, does the GLES shelf draw, does SQLite work (it does — 
`SQLitePCLRaw.bundle_e_sqlite3` 3.0.3 ships `runtimes/android-arm64/native/libe_sqlite3.so`).
**Done when:** the app launches on device, imports a folder without a keyboard, and shows the library.

**Skeleton verified on the AVD, 2026-08-17.** The real head now boots the shared `App` composition
root on `emushelf-api33` (Android 13, arm64) and answers all three questions affirmatively, in one
frame:
- **Avalonia renders** — the header/status/footer chrome paints.
- **The GLES context is real, and asserted rather than eyeballed** — `MediaShelf3DControl`'s
  `InitializationSucceeded` fired ("GL: OpenGL ES context OK"), and logcat shows the shelf's
  `eglCreateContext maj 3 min 0` (ES 3.0, the exact profile `ShaderLibrary` targets). EGL is pinned
  with `Software` dropped, so a fallback could not have masqueraded as success.
- **SQLite works** — `Data/library.db` (204 KB) was created and initialised in app-private storage,
  and the portable `Data/Covers/Cache/Logs/Saves/Settings` layout exists; the log runs cleanly from
  `EmuShelf startup began` to `startup services initialized`.

Structure that landed: a new out-of-solution `src/EmuShelf.App.Android` head (`EmuShelfAndroidApplication :
AvaloniaAndroidApplication<App>` + a thin `AvaloniaMainActivity`), plugging into a new
`App.SingleViewShellFactory`/`ISingleViewApplicationLifetime` branch that mirrors the desktop
`DesktopShellFactory`. `AppBootstrapper` gained a base-directory override (the head injects
`FilesDir`), and the Android shell supplies `AndroidInterfaceModeService` (Gamepad-locked),
`AndroidFrontendController`, `SingleViewApplicationLifetimeService` and a stub `SingleViewDialogService`.
Full desktop Release suite still green (1128 + 889). Build/run traps hit and resolved, plus two
findings, are in DECISIONS 2026-08-17.

**Done in A1 so far:**
- **The walking-skeleton head boots** the shared `App` composition root on the AVD (Avalonia renders,
  real GLES 3.0 context asserted, SQLite in app-private storage). See "Skeleton verified on the AVD"
  above and DECISIONS 2026-08-17.
- **The gamepad tree was extracted from `MainWindow.axaml`** into a shared
  `EmuShelf.UI/Views/GamepadShellView` `UserControl`, so the desktop `MainWindow` and the Android
  `MainView` now both host the *real* couch shell rather than a probe (this was the A0-deferred largest
  pole). Done in gated stages behind the desktop suite; see DECISIONS 2026-08-18.
- **The "switch to Desktop" escape hatches are closed.** A platform capability
  `IInterfaceModeService.SupportsDesktopMode` (desktop true even under a forced-Gamepad override;
  Android false) gates all three of the checklist items A1 owns — the system-menu "Switch to Desktop"
  option, the Set-cover handoff (now an honest "unavailable here"), and the empty-library first-run copy
  (now points at Menu → Add games, not a mode that does not exist). Desktop wording is unchanged, so the
  snapshots are byte-identical. See DECISIONS 2026-08-18.
- **The `OperatingSystem.Is*` ladder audit is done.** All 50 sites triaged: one live crash risk fixed
  (`FileRevealService`'s `xdg-open` fall-through would trip Android's W^X restriction — now throws a
  clear, catchable `PlatformNotSupportedException`); the rest are correct-as-Linux, degrade safely, are
  already Android-aware, or are dormant until the later milestone that supplies an Android
  implementation. Full disposition in DECISIONS 2026-08-18.

- **Gamepad-native library import is built and verified on device.** The couch shell imports a folder
  without a keyboard: a folder pick via `IDialogService` (the Android head drives the SAF picker through
  `TopLevel.StorageProvider`; with all-files access it translates the `externalstorage` tree URI to a
  real path so the shared `FolderScanner` reads it — SAF-reader fallback for other providers is
  Milestone D), then a controller-native `GamepadOverlayKind.ImportSystem` chooser, then the existing
  scan. "Add games" appears in the couch menu only where Desktop mode is absent. **Verified end-to-end
  on the AVD, driven entirely by the gamepad**: Start → Add games → SAF pick → PlayStation → 2 games
  imported. See DECISIONS 2026-08-18.
- **On-device couch input is wired (a Milestone C slice, pulled forward).** Android gamepad buttons do
  not reach Avalonia's `KeyDown` (they report `Key.None`), so `MainActivity.DispatchKeyEvent` maps
  Android keycodes to `GamepadAction` and routes them to the shared `DispatchGamepadAction`. The desktop
  window's key contract is now the shared `GamepadKeyMap` it calls. This is what makes the couch menu and
  import driveable on device; full native analog-stick reading + IME remain Milestone C.

**A1 is done (2026-08-18), verified on the Thor.** The one open item — the CRT tube rendering at 1×1 px
on the AVD's *software* GL — is resolved on real hardware: installed to the Thor via the Debug
`-t:Install` loop, the head boots, the gamepad shell renders, and the CRT tube renders **full-screen on
Adreno GL** (the phosphor/scanline sheen paints across the whole 1920×1080; a backdrop patch measures
real per-pixel variance, not the AVD's flat grey). So the 1×1 tube was a software-GL artifact, not a
shell defect. What remains is Milestone C proper (native analog-stick reading, IME, back-vs-B
arbitration) — a separate milestone, not A1. A1's "imports a folder without a keyboard, shows the
library" done-criterion is met.

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

**Landed 2026-08-20 (code review pending on device).** `allowBackup="false"` and `MANAGE_EXTERNAL_STORAGE`
were already in the A1 manifest; this pass added the rest of the testable storage core:
`AndroidExternalStorageUri` (Core, pure) now owns the SAF tree/document URI ↔ `/storage/…` translation —
the head's private copy was deleted and `SingleViewDialogService` calls the shared one — with build/parse
and containment tests; `RelativePathResolver` stores game paths **absolute** on Android via a new
`IAppPaths.UsesPortableStorage` flag (false only on the Android head), fixing the fragile
`../../../storage/…` anchoring, with a test; the `allowBackup`/app-private/absolute-path decisions are in
DECISIONS 2026-08-20. **Still open:** the all-files runtime grant UX (directing the user to the Settings
toggle), a SAF-backed reader fallback for providers with no local path, the per-API-level AVD matrix, and
verifying `FolderScanner`/availability against real Android storage.

**Landed 2026-08-21, verified on the Thor.**

- **All-files runtime grant UX — provided by D2 onboarding (below).** The grant this milestone needs is
  secured by the same-day first-run onboarding + `IStoragePermissionService` (D2): once the user grants
  all-files there, EmuShelf reads the SD-card library by real path, so no separate grant flow was needed.
  (Field note for future Android storage work: `appops get` prints a per-package line that can read `deny`
  while the effective **uid** app-op is `allow` — the uid mode is what `Environment.IsExternalStorageManager`
  returns, so trust the API, not the per-package line.)
- **`FolderScanner`/availability over real Android storage.** Driven end-to-end on the Thor against the real
  SD library: an SAF pick of `/storage/AE6A-1092/roms/psx` (all-files held → translated to a real path) fed the
  shared `FolderScanner`, which recursed the folder — nested multi-disc game subfolders **and** loose
  single-file `.chd`s — and imported **41 games**; they render as available cards with Play enabled, i.e.
  `FileAvailabilityChecker` stats the real `/storage/…` paths correctly. So the two shared readers work
  unchanged over Android shared storage under all-files, no SAF-reader fallback needed for the Thor.
- **Couch import chooser density-collapse fixed (shared UI, Android-scoped).** Driving the import surfaced a
  Thor-only defect: the gamepad "Add games — choose system" overlay rendered its title and hints but the
  system list collapsed to zero height, so nothing was pickable. Fixed with
  `MainViewModel.GamepadOverlayOptionsMinHeight` (a 240-dip floor on Android only; 0 on desktop, snapshots
  unchanged). See DECISIONS 2026-08-21 and the Milestone S entry.

**Still open in D (deferred, owner call, not Thor blockers):** the **SAF-backed reader fallback** for a device
without all-files (a portability item, same posture as the SAF save-endpoint in E) and the **per-API-level
AVD matrix** (30/31/34 — verification-only; the Thor is 33).

#### D2 — user-chosen external data folder (first-run onboarding) — 2026-08-21

**Decision (owner):** on Android, EmuShelf's own data (`Data/library.db`, `Covers/`, `Cache/`, `Logs/`,
`Settings/`, `Saves/`) does **not** live in app-private `FilesDir` anymore. On first launch an onboarding
step asks the user to pick a folder, and the whole layout is created under `<picked>/EmuShelf`. The folder
can be changed later in Settings. This is Android-only; desktop keeps its portable-beside-exe / macOS
Application Support behaviour untouched. It formally breaks the "data lives beside the executable" rule for
Android (there is no beside-the-executable there) — see DECISIONS 2026-08-21.

**Why a real path, not SAF content URIs.** SQLite (`library.db`) needs a real filesystem path with random
access, as do the log writer and cover cache — a SAF-stream rewrite of the whole storage layer was the one
budgeted "genuine rewrite" and is avoided here. So the data folder is a real `/storage/…` path and the
feature is deliberately **coupled to the `MANAGE_EXTERNAL_STORAGE` (all-files) grant**, which is what makes
`File`/`Directory`/SQLite work by path. The `File.Open` restriction on Android 12+ applies only to *other
apps'* `Android/data/<pkg>` dirs, not to an ordinary shared-storage folder — so a normal folder (e.g.
`/storage/AE6A-1092/EmuShelf` on the microSD, or `/storage/emulated/0/EmuShelf`) is fully read/write. The
picker rejects `Android/data/*` targets.

**The startup chicken-and-egg.** The composition root opens the DB/logger/settings from a fixed base
directory *before* any `TopLevel` exists, but the chosen folder can only be picked from the UI and cannot be
stored inside the data folder itself. Resolved with a **bootstrap pointer** — a tiny `data-location.json`
kept in app-private `FilesDir`, the one always-writable place — recording the chosen base path (plus the
source SAF tree URI for display/re-validation). Startup flow:

1. `DataLocationResolver` (Core, pure) reads the pointer, checks the all-files grant, and write-probes the
   folder. It returns either `Resolved(basePath)` or `Onboarding(reason)` where reason ∈ {`FirstRun`,
   `StoragePermissionMissing`, `LocationUnavailable`} (pointer present but grant lost or SD card gone).
2. Resolved → the head sets `App.BaseDirectoryOverride` to `<picked>` and boots normally.
3. Onboarding → the shared composition root shows an **onboarding-only** view (no `AppBootstrapper`, no DB).
   The user either accepts the **recommended folder** (`<primary>/EmuShelf`, created by path — no document
   picker, which is what sidesteps SAF's refusal of Download/Documents/root) or picks a different folder via
   SAF. On success it persists the pointer and **restarts the process** (ProcessPhoenix-style: start the
   launch activity while still foreground, then `System.exit`), which re-runs the composition root, resolves
   the pointer, and boots straight to the library. A restart — not a live view swap — because Avalonia's
   Android single-view host captures its `MainView` at startup and does **not** re-render a live-reassigned
   one (verified on the Thor: the pointer was written but the screen stayed on onboarding until relaunch).

**Changing the folder in Settings** re-runs the grant+pick flow, writes the new pointer, and **restarts**
the app (same relaunch helper; services are already open against the old path). Old data is **left in place** (owner's choice) —
the user re-picks the old folder to keep the existing library, or starts fresh. The restart is a small
Android relaunch helper (PendingIntent + `Process.killProcess`). **This row is a follow-up:** it threads a
folder-change action + the relaunch through the 2084-line gamepad settings system and the shared
`MainViewModel`, a device-only cross-cutting change. First-run onboarding does not depend on it and lands
first; the pointer store, pick/validate, and resolver it reuses are already in place.

**Seams (all cross-platform-safe; desktop path byte-identical when the hooks are unset):**
- `IStoragePermissionService` (Core) — `RequiresGrant`/`IsGranted`/`RequestGrant`. Desktop: granted no-op
  (`GrantedStoragePermissionService`); Android: `Environment.IsExternalStorageManager` +
  `ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION`, re-checked on the existing `OnTopResumedActivityChanged`
  return signal.
- `IDataLocationStore` + `DataLocation` (Core) with `JsonDataLocationStore` (Infrastructure, `AtomicFile`).
- `DataLocationResolver` (Core) — the pure decision, unit-tested with fakes.
- `IDataLocationBootstrap` (App) + `App.DataLocation` hook — the onboarding gate; null on desktop.
- `OnboardingViewModel` + onboarding view (shared UI). `AndroidDataLocationBootstrap` in the head drives the
  SAF picker (via `SingleViewDialogService`, translated with `AndroidExternalStorageUri`) and the grant.

**Verification:** the resolver, the JSON store, and the onboarding view-model are unit-tested on desktop.
The Android head wiring was **verified end-to-end on the Thor (2026-08-21)**: fresh install → gamepad-driven
Grant → all-files toggle → grant detected on return (process not even killed) → one-tap recommended folder
→ auto-restart → library shelf, with `library.db`/`settings.json` and all six folders created under
`/storage/emulated/0/EmuShelf`. Findings folded in from that pass: the SAF picker refuses Download/Documents/
root (hence the recommended-folder path); a live `MainView` swap doesn't render (hence the restart handoff);
and the double-`EmuShelf` nesting when the picked folder is already named EmuShelf is avoided.

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

**Launch wired and verified on the Thor (2026-08-20).** The couch launch path is connected end to end and
proven on device: `IPlatformShell` gained an optional `LaunchService` (null on desktop → the shared
process launcher; the Android head supplies its own), and `AndroidEmulatorLaunchService` turns a couch
"play" into an intent via `AndroidLaunchResolver` + `AndroidGameLauncher`. It iterates the maintained-first
candidates and falls through one it cannot satisfy (RetroArch with no core) to the next (DuckStation), so
PS1 launches without a core configured. It is fire-and-forget by design (no process to await): the
pre-launch save-pull hook still runs, but post-play sync is manual until the exit signal lands. **Proven:**
installed to the Thor, all-files re-granted, a single-file PS1 game (Castlevania SotN) seeded into the
library, and a gamepad Confirm from the couch produced `Launching DuckStation for Castlevania SotN` →
DuckStation foreground in its `EmulationActivity`, then a clean return to EmuShelf (the `isOneShot` flag).
This exercised the verified tree path (single-file game whose parent folder *is* the emulator's grant
folder). **The nested-multi-disc tree question is now resolved (2026-08-22):** a game in a sub-folder below
the grant folder failed with a `SecurityException` when the launch URI's tree was scoped to the sub-folder
(reproduced live on the Thor), and booted when scoped to the emulator's grant folder (`roms/psx`). The
launch service now supplies that grant root from the game's remembered import folder
(`AndroidLibraryGrantRoot`). See "What's left in B" and DECISIONS 2026-08-22.

**Exit signal + deferred completion landed and verified on the Thor (2026-08-20).** Because the Android
launch is fire-and-forget, the "returned from the game" work (play-time accrual, post-play save sync) is
deferred: the launch service writes a durable single-slot `PendingPlaySession`
(`FilePendingPlaySessionStore`, JSON in the app-private Settings dir) the moment the intent fires, and
`MainActivity.OnTopResumedActivityChanged(true)` — the plan's chosen signal, correct on the Thor's
multi-display Android 13 where several activities can be resumed at once — invokes
`MainViewModel.CompleteDeferredPlaySessionAsync`, which accrues play time, runs the post-play save sync
(a safe no-op until E-android configures it), refreshes Recently Played, and clears the record. Because
the record is on disk and the signal also fires on cold start, a session interrupted by **process death**
is completed on the next launch — the "must survive process death" requirement. **Verified on device:** a
seeded pending session (start stamped 120 s earlier) was auto-completed on a cold start — playtime went
0 → 133 s and the record was cleared, with no configured-sync noise. The duration is wall-clock
launch→return (approximate; it over-counts time spent away before returning) and is **capped at 12 h** so a
long-delayed post-death recovery cannot record a fake multi-day session — beyond the cap the duration is
dropped, but the launch was already stamped last-played + play-count at start. Six store tests
(round-trip across instances, overwrite, clear, corrupt-file tolerance) plus the desktop suite stay green.
Still deferred: an API-<29 `OnResume` fallback (the Thor is 33), and the actual save data to push
(E-android). Remaining B core below is the testable groundwork under this:

The testable core of B is in and green in the desktop suite (41 new unit tests): `AndroidLaunchProfile` + `AndroidEmulatorLaunchProfiles` encode the
measured per-emulator intents (DuckStation/ARMSX2/Dolphin/PPSSPP/Azahar/WatermelonDS/RetroArch);
`AndroidIntentFactory` and `AndroidLaunchResolver` are pure functions turning (system, game path,
selection) → a concrete `AndroidIntentRequest`, asserted against the exact shapes that booted MGS and Auto
Modellista on the Thor; the `<queries>` manifest block lists every emulator package (kept in sync with the
profiles by a test); `AndroidGameLauncher` in the head converts the request to a framework `Intent` and
fires it (head compiles). **All of these later landed:** the Android path plugs in as a dedicated
`IEmulatorLaunchService` (`AndroidEmulatorLaunchService` via `IPlatformShell.LaunchService`); the **exit
signal** (`onTopResumedActivityChanged`, surviving process death) and deferred completion are in and
verified; the **nested-multi-disc/grant-folder** case is fixed by scoping the launch URI to the game's
import folder (2026-08-22); the SDL native payload is excluded structurally (Avalonia.Desktop is kept out
of the head). `GameLaunchDependencyResolver` promotion is desktop/Flatpak-only and out of Android scope
(the emulator resolves the `.m3u` itself). See "What's left in B".

### C — Controller input and text entry

- **`IGamepadReader` over Android `MotionEvent` — analog sticks. ✅ done + verified on the Thor
  (2026-08-21).** The impedance mismatch the plan flagged (polling interface vs event-driven Android input)
  is bridged by `AndroidGamepadReader`: the Activity's `DispatchGenericMotionEvent` feeds joystick-source
  moves into it, and the *already-shared* `GamepadInputService` poll loop samples the stored axes ~60×/s and
  drives left-stick navigation + right-stick 3D-hero rotation through the same
  `GamepadNavigationController`/`ApplyRightStickRotation` logic desktop uses. The reader is injected via a new
  `App.GamepadReaderFactory` hook (desktop still uses `SdlGamepadReader`; Android supplies its own). Buttons
  and the D-pad stay on the existing `DispatchKeyEvent` path (reader reports `Buttons.None`), so nothing
  double-fires. Axis mapping (left `X`/`Y`, right `Z`/`RZ`) was validated against the Thor's actual Xbox
  controller. **Verified on the Thor:** a live probe showed the poll loop reading the Android reader
  (`IsAvailable=True`) and 900+ joystick samples arriving with correct axes on both sticks; the owner
  confirmed left-stick navigation and right-stick 3D-cover rotation respond. (Bring-up note: a plain
  `dotnet build` + `adb install` did **not** repackage the signed APK, so the analog-stick code wasn't
  actually on device until a `-t:Clean` + `-t:Install` — verify APK mtime after building the head.)
- **An Android on-screen keyboard implementation of `IOnScreenKeyboardService`. ✅ done + verified on the
  Thor (2026-08-21).** `AndroidOnScreenKeyboardService` raises the system IME through
  `InputMethodManager.ShowSoftInput` on the focused view — which is Avalonia's own text-input target once a
  couch `TextBox` has focus, so characters route back into the field. It is injected via a new
  `App.OnScreenKeyboardFactory` hook mirroring `GamepadReaderFactory` (desktop keeps
  `PlatformOnScreenKeyboardService`/Windows osk). The explicit request matters because gamepad-driven
  (directional) focus does not auto-raise the IME the way a screen tap does; hiding it again is Avalonia's
  job when the text client loses focus. This is what makes gamepad search / rename usable. Reaches the live
  activity through a `MainActivity.Current` holder (set on resume, cleared on destroy). **Verified on the
  Thor:** opening Search from the couch (X) raised Gboard (`dumpsys input_method` → `mInputShown=true`),
  injected text landed in the field (the suggestion strip reflected the composing text), and Back dismissed
  the keyboard while leaving the overlay up.
- **Back-gesture vs B-button arbitration. ✅ done + verified on the Thor (2026-08-21).** The Activity
  handles `Keycode.Back` on key-up separately from the button map, routing it to a new
  `MainViewModel.DispatchBackButton`: it closes an open couch overlay/menu (consuming Back, like B) but
  returns false at the root library so the platform exits. Kept *off* the shared `DispatchGamepadAction`
  path on purpose — the library-level Cancel deliberately swallows B, which would otherwise trap Back and
  make the app impossible to leave. When the soft keyboard is showing, Android dismisses it on Back before
  the event ever reaches the activity, so IME dismissal stays the system's job. Unit-tested on desktop
  (`DispatchBackButton_ClosesOpenOverlayButLetsRootLibraryExit`, and the not-in-gamepad-mode no-op).
  **Verified on the Thor:** Back over the open system menu closed it and stayed in EmuShelf; a second Back
  at the root library dropped to the launcher; Back with the keyboard up dismissed only the keyboard.
- **Map the Thor's controls against the existing navigation model. ✅ (2026-08-21) — and this surfaced two
  real bugs.** (Face buttons mapped in A1; analog sticks mapped in PR #163.)
  - **R3→reset-rotation** — `Keycode.ButtonThumbr` → the existing `GamepadAction.ResetRotation`, matching
    the desktop native-pad mapping, so the right-stick click recentres the 3D hero. **The bug it fixed:**
    before this mapping, R3 (`BUTTON_THUMBR`) was *unmapped*, so it fell through to Avalonia — and on a real
    gamepad-source press Avalonia activated the focused library item, i.e. **R3 launched the focused game.**
    Reproduced on the Thor with `input gamepad keyevent 107` (Dolphin booted "1080 Avalanche"); after the
    mapping the same injection is consumed and the couch stays put. Source-sensitive: a keyboard-source
    `input keyevent 107` did **not** launch — only the gamepad source did — so injection tests must set the
    gamepad source to reproduce pad behaviour.
  - **The D-pad did nothing.** On the Thor's controller the D-pad is a hat *axis* (`ABS_HAT0X/Y`), delivered
    as a joystick `MotionEvent` — **there are no D-pad key events at all** (confirmed by `getevent`: only
    `ABS_HAT0*`, zero `EV_KEY`). So the A1 `DispatchKeyEvent` map never saw it, and PR #163's
    `DispatchGenericMotionEvent` *consumed* the hat event while `AndroidGamepadReader` read only the stick
    axes (X/Y/Z/Rz) — the D-pad fell into the gap. Fixed by reading `Axis.HatX/HatY` in the reader and
    surfacing them as the `Dpad*` buttons, so the shared `GamepadNavigationController` drives D-pad nav with
    the same auto-repeat as the sticks. No double-fire risk because this device emits no D-pad key events.
    **Verified on the Thor 2026-08-21:** the D-pad steps the couch selector in all directions.
- ~~**Drop the SDL2 native payload from the APK.**~~ **✅ Already clean — nothing to drop (verified
  2026-08-21).** The APK's native libs are `lib/arm64-v8a/…` only; there is **no `libSDL2.so`** and no SDL
  managed assembly, and **no XA0141** warning fires. The plan assumed `ppy.SDL2-CS`'s `runtimes/linux-x64`
  native lib ships into the APK, but Android ABI filtering excludes non-Android RIDs automatically, so it
  never reaches the package. `SdlGamepadReader` stays as harmless managed code that Android simply never
  constructs (the factory hook above supplies `AndroidGamepadReader` instead).

**Cannot be validated off-device** — and per the project's own notes there is no pad on the dev
machine at all, so the SDL path has never been hand-verified either. This is why C's probe is folded
into Milestone 0.

### E — Cloud sync

Transport detail in `docs/cloud-sync-portability-plan.md`; the finalized **save-sync data model**
(battery saves vs save states, cross-emulator handling, no converters) is in
`docs/android-save-sync-model.md` (DECISIONS 2026-08-21).

**Status (updated 2026-08-22 — the list below was written before the Android wiring landed; it is
corrected here rather than deleted, because several "remaining" items were solved *differently* than
predicted and that is worth recording).** Cloud sync is **built and verified end-to-end on the Thor
against real Google Drive.** rclone was removed entirely (`10cdc4e`); the built-in Google Drive
transport is the sole cloud path on every platform. The desktop settings UI and the **controller-native
gamepad Saves section** both offer a managed connect flow, a per-system Save-folder picker, save-state
override, and replace-cloud/replace-local. On Android the browser consent page opens via an
`ACTION_VIEW` intent (`App.ExternalUriOpener`) and redirects to a sockets-based loopback listener; the
refresh token persists via the portable obfuscated store. A code-review pass on the transport found and
fixed five defects (403 rate-limiting mis-read as fatal; non-deterministic duplicate-blob resolution; an
unbounded resumable-upload loop; a dropped date-form `Retry-After`; a pre-cancelled sign-in reported as
a failure).

**How the predicted "NOT DONE" list actually resolved (all done):**
- **Gamepad connect — DONE (`740b4d6`).** The predicted `allowManagedTransport: false` suppression was
  removed. `GamepadSettingsViewModel.BuildSaveRows()` renders a controller-native connect + per-platform
  Save-folder rows; `IsManagedTransportAvailable` is computed live from the head-supplied hook. There is
  no rclone flow to hide anymore.
- **Real Google sign-in — DONE.** Verified on the Thor against real Drive (PS1/GameCube/Wii round-tripped;
  GameCube `gamecube/gci/a/GYQE01` end-to-end). Automated tests still use an in-memory fake Drive by design.
- **Second OAuth client / custom-scheme redirect — NOT NEEDED.** Solved by reusing the same
  `http://127.0.0.1:port/` loopback via `TcpLoopbackOAuthRedirectHandler` (sockets-based, since
  `HttpListener` is unsupported on Android), so **one OAuth client serves every platform**.
  `OAuthRedirectHandlerFactory` selects it on Android.
- **Android `IProtectedTextStore` (Keystore) — DECIDED AGAINST, not missing.** Android uses
  `PortableObfuscatedTextStore` (the same AES-GCM wrap the RetroAchievements and ScreenScraper keys use),
  a deliberate portable-install tradeoff documented in `GoogleDriveTokenStore.cs`, not a gap.
- ~~**A SAF-backed `ILocalSaveEndpoint` — budget this as a rewrite, not a swap.**~~ **Not needed for the
   Thor (2026-08-20).** A runtime probe from EmuShelf's own process showed all-files access reaches
   `Android/data/<pkg>` for read *and* write on Thor firmware — including the `Directory.Move` atomic swap
   the endpoint relies on — so the existing `FileSystemLocalSaveEndpoint` works over real `/storage/…`
   paths for every emulator, with no SAF rewrite. The original concern (SAF has no cross-tree atomic
   rename, no settable mtime, no path containment) only bites on a device that enforces the `Android/data`
   FUSE restriction against all-files; the Thor does not. This item reverts to a portability concern for a
   second device. See DECISIONS 2026-08-20.
6. Per-emulator Android save providers, and the capability probe from the section above. **DONE
   (2026-08-22).** The folder-configurable emulators (PPSSPP, Azahar, RetroArch, WatermelonDS —
   the "pick any folder" set) **reuse the existing desktop providers**, handed the user's chosen folder as
   the pipeline's existing per-system `DirectoryOverride` — set through the per-platform Save-folder picker
   now present in both the desktop and **gamepad** Saves UIs (`GamepadSettingsViewModel.BuildSaveRows`,
   `PickDirectoryCommand` → `_cloudSaves.UpdateOverride`). Fixed-location emulators get package-derived
   roots from the Android composition root.
   **`DuckStationAndroidSaveLocationProvider` was later removed (2026-08-22):** DuckStation's Android
   memory cards are owner-only (`-rw-------`) and unreadable by EmuShelf, so it synced nothing on current
   builds. PS1 on Android now syncs only via a RetroArch PS1 core (Beetle PSX), which emits DuckStation's
   `playstation/per-game/file-title/…` card key for 1:1 round-trips. See DECISIONS 2026-08-22.
   **Dolphin fixed-root wiring landed (2026-08-20):** both GameCube and Wii resolve
   `Android/data/org.dolphinemu.dolphinemu/files` from the package id and pass it as the existing
   `DolphinSaveLocationProvider`'s explicit user directory. That provider already maps
   `GC/<region>/Card <slot>` and `Wii/title/00010000/<title>/data` to the desktop-compatible unit ids,
   including configured Card B layouts, so a second Android-only provider would only duplicate its
   security and GCI-header logic. Deterministic layout/resolver/registry tests are green and the Thor
   export/restore was verified over real Drive. Remaining (deferred to S / owner's call): DuckStation
   shared/global cards and PS1 owner-only-card readability.

   **Per-system wiring status on the Thor (updated 2026-08-22).** Sync is per-system, and a
   folder-configurable system with no Save folder set is a silent no-op (`CanSyncSystem` false). Full
   table + the exact folder each needs is under "Per-system wiring status" in
   [android-save-sync-model.md](android-save-sync-model.md); summary:
   - ✅ **Wired & verified over real Drive:** PS1 (DuckStation — but owner-only cards don't sync, see
     model doc), GameCube + Wii (Dolphin). GameCube round-trip verified end-to-end
     (`gamecube/gci/a/GYQE01`, `…/GC6E01`).
   - 🟡 **Wired (Save folder set), on-device play-test pending:** PS2 (ARMSX2), PSP (PPSSPP), 3DS (Azahar) —
     all at `/storage/emulated/0/User/<Emulator>/`, verified readable; PS2 has card-name/slot and
     single-file re-upload caveats (model doc). Not RetroArch-dependent, so they sync on the installed build.
   - 🟡 **Fix shipped, on-device play-test pending:** all RetroArch systems (Mega Drive, SNES, NDS, GBA,
     GBC, NES, Dreamcast, Arcade) + melonDS DS. The launch-config fix (RetroArch wrote saves next to the ROM
     instead of a `saves/` tree) merged as **PR #171** and now ships in the **release-signed v1.5.8 APK**
     (main CI green; the #168 import tests that were red now pass). Remaining is purely on-device: install
     v1.5.8, set each system's Save folder, confirm the `saves/` tree, verify the round-trip.
   - ❌ **Not syncable:** PS3 (RPCS3) — no Android emulator.

**Per-emulator save mapping — on-device findings (2026-08-19).** Reached via **CX File Manager with NO
root** (the Thor is not rooted); do not read these rows as requiring root. Battery/memory-card
saves only; save states are out of scope and the providers already exclude the `states` namespace
([ISaveLocationProvider.cs:28](../src/EmuShelf.Core/SaveSync/ISaveLocationProvider.cs)). Each Android
provider maps a title's save to the emulator's on-device location; most are a 1:1 directory copy, two
carry format constraints the desktop providers do not:

| System / emulator | Android location | Mapping | Notes |
|---|---|---|---|
| DuckStation (PS1) | `Android/data/<pkg>` | 1:1 | Confirmed 1:1 via CX File Manager, no root — the `Android/data` case, reachable on this firmware without root |
| PS2 (NetherSX2 / AetherSX2 / ARMSX2) | `Android/data/<pkg>` | **format conversion** | **Folder memory cards are not accepted — Android wants a single-file `.ps2` card.** Desktop `Pcsx2SaveLocationProvider` is built around folder cards, so this is a real conversion step, not a copy. **Confirmed working on hardware (2026-08-20).** |
| Azahar (3DS) | any chosen folder | 1:1 | Reachable without root |
| WatermelonDS (DS) | any chosen folder | 1:1 | **Requires the "use `.srm` not `.sav`" toggle enabled** so the on-device filename matches what the provider syncs |
| Dolphin (GC/Wii) | `Android/data/<pkg>` | **path reshape** | Reached via CX File Manager, no root; **confirmed working on hardware (2026-08-20)**. GameCube saves sit under a deeper path than desktop: Windows can be configured at the region folder while Android uses `USA/Card A/`. The existing Dolphin provider already models the standard region+slot tree; Android now supplies its fixed `files/` user root. Deterministic mapping is green; device export/restore remains |
| PPSSPP (PSP) | any chosen folder | 1:1 | Reachable without root; PPSSPP records its memstick path in app-private storage, so it must be *asked for*, not discovered (see capability model) |
| RetroArch | RetroArch saves folder | 1:1 | Plain-path case; the only emulator handed `{file.path}` |

Three design items before implementation, all now hardware-confirmed: the **PS2 single-file `.ps2`
conversion** (folder card ⇄ `.ps2`, which the desktop provider has no path for), the **Dolphin
GameCube region ⇄ region+slot path reshape**, and the **WatermelonDS `.srm`/`.sav` extension
constraint**. PS2 still needs a format conversion and WatermelonDS needs an explicit setup check;
Dolphin's physical-path difference is absorbed by the existing provider once Android supplies the correct
user root, so it does not need a divergent parser or cloud-id model.

### F — Packaging and release

- APK/AAB from a **dedicated CI job** (the head is outside the solution, so the existing 3-OS matrix
  will not build it). Cache the workload; it is minutes per run on top of a JDK and SDK 36.
  **Landed (2026-08-20):** `package-android` in [.github/workflows/build.yml](../.github/workflows/build.yml)
  — JDK 21 + SDK platform 36 + `dotnet workload install android`, publishes a Release APK
  (`-p:AndroidPackageFormat=apk`, debug-key-signed for sideload), runs on PRs as the build floor, and
  uploads the APK on non-PR events. The tagged **release attaches the APK** alongside the desktop targets:
  `package-android` is in the `release` job's `needs` (so the release waits for and includes it), but the
  job's `if` only *requires* the three desktop packages to have succeeded — so a failing experimental APK
  build is left off the release rather than blocking the Windows/macOS/Linux release (the plan's original
  "don't let Android block a release" guarantee, kept while still shipping the APK when it builds). Still
  to do here: the owner runs the keystore one-time setup (below), and the Android OAuth client id in
  `EmbeddedSecrets`.
- Signing keystore. This is a permanent, unrecoverable obligation — lose it and every user must
  uninstall to upgrade. **CI signing is wired (2026-08-20):** `package-android` release-signs when the
  `ANDROID_KEYSTORE_*` secrets are present and debug-signs otherwise, so nothing breaks before setup;
  passwords pass to MSBuild as `env:VAR` (never on the command line). What remains is the owner running the
  one-time keystore generation + `gh secret set` runbook and holding an offline backup — see the
  DECISIONS 2026-08-20 "Android release signing keystore" entry for the exact commands.
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

### SS — Second screen (Thor dual-screen companion)

The Thor's bottom panel, decided (2026-08-22, owner) after being parked as "revisit as its own item"
in 0b. It is a live standard `Presentation` display — `displayId=4`, "Screen-2", 1240×1080 landscape,
120 Hz, `FLAG_PRESENTATION` (measured 0b) — that AYN's own `com.odin.dualscreen.assistant`
default-drives. EmuShelf takes it over while it is the active frontend and turns it into a companion
surface: an **app dock**, an **all-apps drawer**, a **RetroAchievements panel**, and a **dimmed
game-logo idle** while a game plays on the main screen.

**Product decisions (owner):**
- **Active whenever EmuShelf is open**, not only in-game. While browsing the library, Screen-2 shows
  the dock + drawer/achievements affordances; when a game launches it switches to the dim+logo idle.
  Handed back to AYN's assistant on EmuShelf exit.
- **Dock/drawer-launched apps open on Screen-2** (beside the running game — the Cocoon dual-screen
  pattern), via `ActivityOptions.setLaunchDisplayId(4)` + `NEW_TASK`. Some target apps may ignore the
  launch display; that is an accepted per-app limitation, not a blocker.
- **Achievements = the running game, else the selected game.** Cache-first, **pull only on the icon
  press** — reuse the existing `IRetroAchievementsDetailsService` / details store and the shipped
  5-minute staleness gate so re-presses do not hammer RA. No timer, no polling. There is no new RA
  path.

**Architecture.** The second screen is **native C# Android Views inside an `Android.App.Presentation`**,
not a second Avalonia surface. It is launcher chrome (app icons, dock) that reads the *shared Core
services in-process* — the achievements stores are framework-neutral and the badge cache returns a
file path, so an `ImageView` renders it directly. A second Avalonia `TopLevel` on a Presentation is
unproven and the wrong tool here; keep the option open only to embed an `AvaloniaView` for the
achievements panel later if native re-rendering proves not worth it.

- **SS0 — Presentation-lifetime + AYN-coexistence spike (gating; do first).** The one genuine unknown:
  does a `Presentation` on display 4 survive when EmuShelf is backgrounded as an emulator takes the
  main screen (a multi-resume device — the head already relies on `OnTopResumedActivityChanged`)? Stand
  up a placeholder Presentation, launch a real game, watch it over adb. Also answer: **must AYN's
  `com.odin.dualscreen.assistant` be disabled/dismissed for our Presentation to own Screen-2, or do we
  coexist** (who wins the display, and how it is handed back on exit). Output: the confirmed keep-alive
  mechanism (almost certainly a foreground service pinning the process — Thor is SDK 33, so the
  notification-permission escalation stays dormant) and the AYN hand-off behavior. Everything below
  assumes SS0's answers. Measure on device, per the "instrument, don't guess" rule.
- **SS1 — second-screen host.** `SecondScreenController` in the head: find the `FLAG_PRESENTATION`
  display via `DisplayManager`, create/attach the Presentation, tear down on display-removed and app
  exit. Shown from the frontend-shown hook (`SingleViewShell.Show`). Root = a `FrameLayout` with two
  layers (Browse chrome / Game-idle) plus a shared bottom bar.
- **SS2 — bottom bar.** Drawer icon (bottom-left), achievements icon (bottom-right), 5-slot dock
  (centre). Native layout.
- **SS3 — app drawer.** Add a `<queries><intent>` for `ACTION_MAIN`+`CATEGORY_LAUNCHER` to the manifest
  (Play-policy-safe, mirrors the existing narrow `<queries>`, avoids `QUERY_ALL_PACKAGES`).
  `PackageManager.queryIntentActivities` → a grid of every launchable app; tap launches it on Screen-2.
- **SS4 — dock pinning.** Tap an empty slot / long-press a filled one → drawer in pick-mode → pin.
  Persist 5 component names in a portable `Settings/second-screen-dock.json` (the pattern
  `pending-play-session.json` already uses); model + store live in Core so the desktop suite tests
  them. Tapping a pinned slot launches on Screen-2.
- **SS5 — achievements panel (cache-first, pull-on-press).** Achievements icon → resolve the target
  game (running session's game, else `MainViewModel.FocusedGame`), map local→RA id via
  `IRetroAchievementsReadStore.GetAllLinks()`, render the cached details snapshot natively (badge via
  `IRetroAchievementsBadgeCache.GetBadgePathAsync`). One manual refresh on open, gated by the existing
  5-minute staleness check, plus an explicit Refresh; honors the shipped rate-limit/offline handling.
- **SS6 — game-idle state.** On game launch, switch to the idle layer: dim + the running game's logo
  (scraped clear-logo if present, else cover, else title text), centred. 3 s no-touch → dim; any touch
  → reveal the bottom bar, then re-dim after a timeout (`Handler.postDelayed` state machine).

**Testing.** Pure logic (dock store, target-game/RA-id resolution) → desktop unit tests, matching the
port's "Android logic in a `net10.0` assembly" rule. The Presentation and native rendering are
Thor-verified over adb; there is no headless equivalent for Android Views on macOS.

## Sequencing

**0a → A0 → 0b → A1 → D → B → C → E-android → F → SS → S (repeat)**, with E-desktop parallel throughout.
Second screen (SS) is a self-contained feature milestone gated only on its SS0 spike; it can slot
before or interleave with S.

Changes from the first draft: A0 is new and comes first among the engineering work; **D moves before
B** because D produces B's input; the GL and pad probes move into Milestone 0, because each can end
the project and both are nearly free once a device is booted; and Milestone 0 splits at the delivery
date.

### S — Stabilization passes (features first, then iterate until solid)

**The strategy, decided 2026-08-20 (owner).** The lettered milestones deliberately build the *features* —
they land and are each verified in a narrow way (empty library, headless snapshots, a targeted on-device
check). They do **not** by themselves produce a polished, pleasant-to-use build: the first time the couch
shell is driven with a **full library on the Thor**, a class of interaction and visual bugs surfaces that
none of those checks catch. So the plan does not treat "all milestones checked" as "done." After the
feature milestones give a **working core** (a build that imports, launches, and returns end to end), the
work switches to **repeated stabilization passes** — and keeps repeating them until the build feels
finished:

1. Drive the real couch shell on the Thor with a populated library (grid / list / 3D shelf, every overlay).
2. Catalogue every glitch and dead-end into the known-issues backlog below.
3. Fix, re-verify on device, and go round again. Each pass should leave fewer, smaller issues than the last.

This is a first-class phase, not cleanup tacked on the end — do not defer it indefinitely as "cosmetic,"
which is how the density/populated-view bugs below went unnoticed until the app was actually played.

**Known on-device issues (stabilization backlog).** Seeded from the owner's first real play session on the
Thor (2026-08-20); expand it each pass. None is a feature gap in the milestone sense — they are quality
bugs the feature checklist does not track:

- **Analog sticks are not read on Android.** ✅ **Fixed + verified on the Thor 2026-08-21.**
  `AndroidGamepadReader` feeds joystick `MotionEvent`s (from `DispatchGenericMotionEvent`) into the shared
  `GamepadInputService` poll loop, driving left-stick navigation and right-stick 3D-hero rotation through the
  same logic desktop uses; injected via `App.GamepadReaderFactory` (desktop keeps SDL). Buttons/D-pad stay on
  `DispatchKeyEvent`, so nothing double-fires. On-device probe confirmed the poll loop reads the reader and
  correct axes arrive on both sticks; owner confirmed nav + rotation respond. See Milestone C.
- **3D shelf covers change size while scrolling.** Almost certainly the A2 density override (which shrinks
  the whole UI to fit the Thor's ~833×468 dip) interacting badly with `MediaShelf3DControl`'s geometry
  and/or grid virtualization during a scroll animation. This is the "populated-library visual pass at the
  new density" the A2 notes flagged as open and deferred as cosmetic — it is neither cosmetic nor optional.
- **The "Add games — choose system" chooser rendered no system list on the Thor.** ✅ **Fixed 2026-08-21,
  verified on the Thor.** Found driving a real import on device: the `GamepadOverlayKind.ImportSystem`
  overlay showed its title and the D-pad/A/B hint legend but the option list between them collapsed to
  ~0px, so no system was visible to pick (import could only be completed by counting D-pad presses blind,
  landing off-by-one and tagging PS1 discs as PS2). The options collection *was* populated and the styles
  innocent; the collapse is the shared overlay's centred, content-sized Border giving its option
  ScrollViewer no height when the system-menu picker header (the only thing propping the body open) is
  absent — which it is for every option-list overlay except the system menu. **It does not reproduce in
  desktop headless** (a repro test proved the desktop list renders at 780px and scrolls when short — the
  classic "won't reproduce here, it's device/density-specific" case), so the fix is platform-scoped: a new
  `MainViewModel.GamepadOverlayOptionsMinHeight` gives the option ScrollViewer a 240-dip floor **on Android
  only** (0 on the desktop targets, so the pinned snapshot pixel-heights are byte-identical). On the Thor
  the chooser now shows the full scrollable list and scroll-follows the selector; PlayStation imports as
  PlayStation. Desktop-regression guard is `GamepadImportChooserLayoutTests`; full App Release suite (906)
  green. See DECISIONS 2026-08-21.
- **(more to catalogue.)** The owner reported "many others" not yet enumerated; the first stabilization
  pass with a staged library is where they get written down.

### Current status and what's next (2026-08-22)

| Milestone | State | What remains |
|---|---|---|
| 0a — AVD spike | ✅ done | — |
| A0 — desktop split | ✅ done | — |
| 0b — device facts + handoff matrix | ✅ done | PS3 (aPS3e) never measured — out of v1 scope |
| A1/A2 — skeleton, gamepad import, couch responsiveness | ✅ done, on Thor | populated-library visual pass at the new density — **moved to Milestone S** (it is the "covers resize on scroll" bug, not cosmetic) |
| **D — storage & permissions** | ✅ done for Thor (2026-08-21) | grant secured via D2 first-run onboarding (`IStoragePermissionService`, verified on Thor); D2 user-chosen data folder verified end-to-end on Thor; `FolderScanner`/availability verified on the real SD library (41 games); the couch import chooser density-collapse found here is fixed. **Deferred (owner call, not Thor blockers):** SAF-backed reader fallback (portability, a device without all-files) and the per-API-level AVD matrix (verification-only). D2 Settings folder-change is the one remaining follow-up |
| **B — launching** | ✅ done (2026-08-22) | nested multi-disc fixed; #3 is desktop-only (not an Android item), #4 is old-Android-only (Thor is 33), #1 is unimplementable-as-specified and moot on a granted device — see "What's left in B" below |
| C — controller input & text entry | ✅ done, verified on Thor | left stick + **D-pad** (hat-axis, fixed 2026-08-21) nav, 3D rotation, SDL-drop (moot), **IME, back-vs-B arbitration, R3→reset-rotation — all verified on the Thor 2026-08-21**. Two device-only bugs found & fixed during the hands-on pass: R3 launched the focused game (unmapped → Avalonia activation), and the D-pad did nothing (reported as a hat axis the reader ignored). Optional follow-up: an API-<29 path is not needed (Thor is 33) |
| E-desktop — managed Drive transport | ✅ done | rclone removed; built-in Google Drive is the sole transport (`10cdc4e`); one real Google sign-in proven on the Thor (same OAuth client/loopback serves desktop). Automated tests still use an in-memory fake Drive by design |
| **E-android — cloud sync** | ✅ done, verified on Thor over real Drive | managed connect + per-system Save-folder picker in the **gamepad** Saves UI (`740b4d6`); Android OAuth reuses the loopback (`TcpLoopbackOAuthRedirectHandler`, single client — no custom-scheme handler needed); token via `PortableObfuscatedTextStore` (no Keystore — deliberate). PS1/GC/Wii round-tripped; PS2/PSP/3DS + RetroArch systems (fix ships in signed v1.5.8) await only an on-device play-test. **Known limits → S:** PS1 owner-only cards, PS2 single-file `.ps2` churn — see E |
| F — packaging & release | ✅ done | APK CI job done + attached to releases; **release-signing is live** — all four `ANDROID_KEYSTORE_*`/`ANDROID_KEY_*` secrets are set (2026-08-20), so tagged builds are release-signed (verified via `gh secret list`); Android OAuth client-id accessor (`GoogleOAuthAndroidClientId`) + `EMUSHELF_GOOGLE_OAUTH_CLIENT_*` secrets present; user install/sideload docs written (`docs/android-install.md`); stale `package-android` needs-comment fixed. **Only non-engineering remainder:** register a Google developer-verification identity — region/time-gated (enforcement starts 30 Sep 2026), not blocking. |
| **SS — second screen** | ⬜ not started | Thor bottom panel as a companion surface (dock, app drawer, RA panel, game-logo idle); decided 2026-08-22, gated on the **SS0** Presentation-lifetime + AYN-coexistence spike — see "Milestone SS" above |
| **S — stabilization passes** | ⬜ not started (repeat until solid) | the on-device bug/polish rounds after the core works; seeded backlog: 3D covers resize on scroll, "many others" TBD (analog-stick input is now fixed, PR #163) — see "Milestone S" above |

**What's left in B (launching):** the launch path is wired and boots real games on the Thor, plus the exit
signal + deferred post-play completion (survives process death). RetroArch-backed systems now have a
controller-native per-system selector: it offers compatible known core filenames, persists the selected
app-private path, activates RetroArch for that system, and makes the launcher honor that choice before its
fallbacks. EmuShelf does not install or inspect cores; the matching core must already be installed in
RetroArch.

**(2) Nested-multi-disc tree — FIXED (2026-08-22).** A game in its own sub-folder (a per-game `.m3u` beside
its `Disc 1`/`Disc 2` — MGS, Xenogears, Twin Snakes, Shadow Hearts Covenant) would not launch, while
single files in the same system folder did. Reproduced live on the Thor: EmuShelf hands the emulator a
`content://` **tree/document** URI, and Android matches the URI's *tree* against a tree the emulator was
granted. Each emulator holds a persisted **prefix** grant to its whole system folder (DuckStation →
`roms/psx`, Dolphin → `roms/ngc`+`roms/wii`, …), so a URI re-rooted at the game's sub-folder matches no
grant and the emulator is denied (`SecurityException` from `ExternalStorageProvider`). The launch service
now scopes the tree to the game's remembered **import folder** via `AndroidLibraryGrantRoot.ForGame`,
which in the normal setup equals the emulator's grant folder. Single-file games are unchanged (same URI as
before). See DECISIONS 2026-08-22.

**(1) Per-emulator setup checklist — reframed, and largely moot on a set-up device.** The original idea
("does the emulator hold a grant covering the game's folder, with a verify step") is **not implementable
as EmuShelf reading the grant**: a normal app cannot enumerate another app's persisted SAF permissions
(the grant list is only visible via shell `dumpsys`). And EmuShelf cannot detect a failed launch either —
`startActivity` succeeds; the read `SecurityException` happens *inside the emulator's* process. So EmuShelf
must *infer* the grant folder (it does — the import folder), and cannot surface a precise "grant this
emulator access" error at launch. What remains as an option, if the inference proves too fragile in the
field, is for EmuShelf to hold its **own** persisted SAF grant to the library folders and delegate read via
`FLAG_GRANT_READ_URI_PERMISSION` at launch — removing the dependency on each emulator's own grant. That is
a larger change overlapping the deferred SAF-storage work in Milestone D and is **not built**; on a device
where each emulator is already granted (the Thor), it is unnecessary.

**(3) `GameLaunchDependencyResolver` promotion — NOT an Android item.** On Android the *emulator* opens the
`.m3u` and resolves its own disc files (verified: DuckStation opened `…(Disc 1).chd` from the m3u). The
resolver only runs on the desktop Flatpak launch path, where the frontend must pre-list files for the
portal handoff. Tracked as a desktop-only improvement, out of the Android port's scope.

**(4) API-<29 `OnResume` return-signal fallback — NOT needed for supported hardware.** The return signal
(`onTopResumedActivityChanged`) is API 29+. The Thor is API 33. A fallback only matters for Android ≤ 9,
which the experimental sideload does not target; deferred indefinitely.

**What E-android needed, and where it landed (2026-08-22).** The auto-sync path is wired to the exit
signal and now moves real saves. **The single biggest item shrank on 2026-08-20:** a runtime capability
probe from EmuShelf's own process proved all-files access **reads and writes `Android/data/<pkg>` over
real paths** (DuckStation memcards + Dolphin GC, including `Directory.Move`), so the SAF-backed
`ILocalSaveEndpoint` rewrite is **not needed for the Thor**; the existing `FileSystemLocalSaveEndpoint`
serves every emulator over real paths. DuckStation and Dolphin providers are wired (Dolphin feeds its
package-derived `files/` root through the desktop provider's explicit-user-directory seam), and the
folder-configurable emulators (PPSSPP, Azahar, WatermelonDS, RetroArch) reuse the desktop providers via
the per-system `DirectoryOverride`, now settable through a **controller-native Save-folder picker in the
gamepad Saves UI** (`740b4d6`). The three predicted "hard" auth items were solved more cheaply than
budgeted: **no second OAuth client and no custom-scheme handler** (the loopback redirect is reused via
`TcpLoopbackOAuthRedirectHandler`, one client for all platforms), and **no Keystore** (the refresh token
uses `PortableObfuscatedTextStore`, the same wrap as the achievements key — deliberate). The SAF endpoint
reverts to a portability concern for a hypothetical second device, not v1 work. **Deferred to S / owner's
call:** PS1 owner-only-card readability, PS2 folder-card→`.ps2` conversion + cross-emulator sync.

**Strategy (owner, 2026-08-20): finish the feature milestones to a working core, *then* stabilize —
repeatedly.** With E-android landed, the feature core is complete: the app imports, launches, returns and
**syncs to real Google Drive** end to end. The next phase is **Milestone S** — the repeated stabilization
passes above. The known-issues backlog (3D-covers-resize-on-scroll, …) is parked for S.

**Recommended next step:** **Milestone S**, opened by an on-device play-test pass on the signed v1.5.8 APK
to close the remaining save round-trips (RetroArch systems, PSP, 3DS, PS2 — all wired, all awaiting a play
test rather than code). **B, C, D, E and F are done** (E 2026-08-22: cloud sync verified on the Thor over
real Drive — PS1/GC/Wii round-tripped; C: analog sticks + D-pad, 3D rotation, IME, back-vs-B; B: nested
multi-disc launch; F: release-signed CI + install docs). No feature milestone remains on the critical path.

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

Answer these before writing Android code against assumptions. **Most are answered in Milestone 0b
(2026-08-18); status annotated.**

- ✅ **Android version** — 13 / SDK 33, firmware `Thor_V1.0.0.377`. No OTA past 13; the B/E
  foreground-service and notification-permission notes stay dormant.
- ✅ **Which emulator builds are installed** — recorded in the 0b table (DuckStation, AetherSX2,
  Dolphin, PPSSPP, Azahar, RetroArch; no aPS3e). Exact per-emulator versions still to capture.
- ✅ **"Run script as Root"** — present, but a one-shot script runner, not a persistent grant. v1 is
  no-root; capability model corrected.
- ✅ **The second screen** — a live `Presentation` display available to third-party apps
  (`FLAG_PRESENTATION`); default behavior is AYN's own dual-screen assistant. Product use now decided:
  [Milestone SS](#ss--second-screen-thor-dual-screen-companion). One open sub-question moved into SS0:
  whether AYN's `com.odin.dualscreen.assistant` must be dismissed for our Presentation to own Screen-2.
- ⬜ **Wireless ADB pairing**, so the `adb install` / `logcat` / `screencap` loop survives unplugging.
  Currently on USB. Still to set up.
- ⬜ **How the ROM library gets onto the device**, in practice, at size. `/sdcard/ROMs` is empty today.
  This is the real first-run experience and nothing in the plan substitutes for trying it. Still to do.

## Effort

**Writing the code is not the schedule.** Milestone 0a — toolchain from zero, five emulators
installed, manifests analysed, the multi-disc question answered — took about an hour. Size the rest
the same way: in agent sessions, not person-weeks.

| Milestone | Sessions | What could stretch it |
|---|---|---|
| 0a — toolchain + AVD spike | done | — |
| A0 — desktop split | done | 17 snapshot baselines and 19 `avares://` URIs came out green; MainWindow.axaml internal split moved to A1 |
| A1 — head + skeleton + gamepad import | 2–3 | Gamepad import is a real feature; GL may not initialise |
| D — storage & permissions | 1–2 | Grows if the SAF-only emulators need EmuShelf-side SAF readers |
| B — launching | 1–2 | Mostly per-emulator definitions, and Cocoon's configs are a working reference |
| C — controller + IME | 1–2 | Pad behaviour is unverifiable until the Thor is here |
| E — desktop remainder | 1 | Merge the Drive commit, settings UI, one real sign-in |
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

M41–M43 are taken (M43 is Playtime tracking); there are already two M40s. Android is the **M44**
umbrella in [ROADMAP.md](../ROADMAP.md), with the plan's sections (0a, A0, A1, D, B, C, E, F, SS) as its
phases. Note that [ROADMAP.md:522](../ROADMAP.md:522) states M24 is a product-hardening gate to be
completed "before adding new end-user features" and its Phase 0 is entirely unchecked — starting
Android is a decision to set that aside, which is fine if made knowingly.

Milestone 0 has reported (0a) and A0/A1-skeleton have landed, so DECISIONS now carries the
2026-08-15 (0a), 2026-08-17 (A0) and 2026-08-17 (A1 skeleton) entries.
