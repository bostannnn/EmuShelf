# Couch shell burns ~50% CPU while idle (Android/Thor) — investigation handoff

_Opened 2026-08-24 from the idle-sway profiling detour. Not yet root-caused._

## The finding (one line)

On the AYN Thor, the **couch (Gamepad) shell pegs ~50% of one CPU core while completely idle** —
in the Shelf layout, CRT effect off, drawing **zero** GL frames, no user input, fully settled.
This is a pre-existing cost, unrelated to any single feature, and it is the real fan/battery drain
on the device.

## How it was found (and what it is NOT)

It surfaced while profiling the resting-hero idle sway (`MediaRotationModel`). The sway was suspected
of being expensive; profiling proved it is **cheap** and exonerated it. The ~50% remains with the sway
**off**.

Measured on the Thor, **Release build** (`-c Release -p:RunAOTCompilation=false`, non-debuggable),
Shelf layout, CRT off, 76-game library, fully settled:

| State | App CPU (`top`) | `glfps` |
|---|---|---|
| Sway **off** (shelf draws nothing) | **~50%** | 0 |
| Sway on @ 60 fps | ~67% | 60 |
| Sway on @ ~16 fps (capped) | ~57% | 16 |

Per-thread (`top -H`) with the sway **off**:
- **Main/UI thread (`om.emushelf.app`, tid == pid): ~48%**  ← the whole cost is here
- Render Thread: ~0% (nothing rendering)
- Choreographer: ~0–4%
- everything else: ~0%

From the in-app tracer (`PerfTrace`, logcat tag `EmuShelfPerf`) in the same idle state:
`glfps=0 glRenderMaxMs=0.0 allocMB/s=0.0 gen0/s=0`.

So the cost is:
- **On the UI thread**, and
- **Not GL rendering** (`glfps=0`, Render Thread idle), and
- **Not managed allocation / GC** (`allocMB/s=0.0`, `gen0/s=0`).

That rules out the 3-D shelf render, cover decoding, and GC churn. Something is keeping the Avalonia
UI thread busy continuously without allocating and without drawing GL frames.

## Leading suspects (unverified)

1. **The 60 Hz gamepad-input poll on the UI thread.** `GamepadInputService`
   (`src/EmuShelf.UI/Services/GamepadInputService.cs`) runs a `DispatcherTimer` at **16 ms**,
   `DispatcherPriority.Input`, the whole time `InterfaceMode == Gamepad`. Each tick reads the pad,
   polls navigation, and calls `ApplyRightStickRotation`. The per-tick work looks cheap, but the
   timer + Avalonia-on-Android dispatcher/Looper wakeup 60×/s is the most obvious always-on UI-thread
   load. **First experiment: raise the interval (e.g. 16 ms → 100 ms) and re-measure `top`.** If CPU
   drops roughly 6×, this is it (and the fix is a smarter poll — event-driven, or a lower idle rate).
2. **A continuous UI invalidation / animation.** Something may be invalidating the visual tree every
   vsync, forcing a Skia recomposite of the couch chrome on the UI thread (which would burn CPU
   without incrementing `glfps` — that counter only covers the GL shelf control's `OnOpenGlRender`).
   Check for a looping `Animation`/`Transition` (`IterationCount` infinite), a `MarqueeTextBlock`
   (`src/EmuShelf.UI/Controls/MarqueeTextBlock.cs`) scrolling the focused title/system name, a pulsing
   focus glow, or the platform-rail indicator. Grep `GamepadShellView.axaml` for `Animation`,
   `KeyFrame`, `IterationCount`, `RepeatBehavior`.
3. **Android second-screen / Presentation work.** The Thor drives a second display; the
   `SecondScreenController` (`src/EmuShelf.App.Android/Services/SecondScreenController.cs`) or its
   watcher could be doing continuous work. Test by measuring with nothing on Screen-2.

## How to reproduce / measure (the working recipe)

adb is at `/opt/homebrew/share/android-commandlinetools/platform-tools/adb`; the Thor is `-s 2fd555f4`.
Toolchain paths and traps: see the repo's Android build notes.

1. **Build + install Release** (representative of what ships; Debug is ~2× slower and misleads):
   ```
   DOTNET_ROOT=$HOME/.dotnet $HOME/.dotnet/dotnet build src/EmuShelf.App.Android/EmuShelf.App.Android.csproj \
     -c Release -p:RunAOTCompilation=false \
     -p:JavaSdkDirectory=/opt/homebrew/opt/openjdk@21/libexec/openjdk.jdk/Contents/Home \
     -p:AndroidSdkDirectory=/opt/homebrew/share/android-commandlinetools
   adb -s 2fd555f4 install -r src/EmuShelf.App.Android/bin/Release/net10.0-android36.0/com.emushelf.app-Signed.apk
   ```
   Use **`adb install -r`** (not `-t:Install`): `-t:Install` reinstalls under a **new uid**, which wipes
   the app-private data-location pointer and forces onboarding every time. `-r` keeps the uid and data.
2. **Get to the Shelf layout with games.** The library + `GamepadLayout=Shelf` + `CrtScreenEffect=false`
   live in the portable data folder `/storage/emulated/0/EmuShelf` (already populated on the Thor). If
   onboarding appears: `adb -s 2fd555f4 shell appops set com.emushelf.app MANAGE_EXTERNAL_STORAGE allow`,
   then drive it (Grant → Back → "Use recommended folder"); button bounds via `uiautomator dump`.
3. **Turn the perf sampler on.** `PerfTrace` (`src/EmuShelf.UI/Diagnostics/PerfTrace.cs`) is **not**
   auto-on in Release — it's behind a triple-L3 toggle (`RenderOverlayDiagnostics`), and injected
   `input gamepad keyevent 106` did **not** trigger it here. The reliable hack used during this
   investigation: temporarily add `PerfTrace.StartSampling();` right after the `Sink` is set in
   `src/EmuShelf.App.Android/EmuShelfAndroidApplication.cs` (~line 81). Then:
   `adb -s 2fd555f4 logcat -s EmuShelfPerf` → one `PERF …` line per second with
   `layout= crt= path= glfps= glRenderMaxMs= allocMB/s= gen0/s=`.
4. **Measure CPU:** `PID=$(adb -s 2fd555f4 shell pidof com.emushelf.app)`;
   overall `adb -s 2fd555f4 shell top -b -n 4 -d 1 -p $PID`;
   per-thread `adb -s 2fd555f4 shell top -H -b -n 1 -p $PID | grep u0_ | sort -k9 -rn | head`.
   Let it **settle 20–30 s** first — startup work (cover prefetch, achievements, availability scan)
   inflates CPU right after launch and was what originally led to mis-blaming the sway.

## Traps already paid for

- **`simpleperf` is blocked** on this locked-down, non-debuggable, non-`profileable` Release build
  (`failed to open perf event file … Permission denied`). To get a flame graph you'd need
  `<profileable android:shell="true"/>` in the manifest (or a debuggable build, but Debug perf is not
  representative). Consider adding `profileable` behind a build flag for perf work.
- **`dumpsys gfxinfo` is blind to Avalonia's GL surface** — don't trust its frame counts.
- **The Thor's OLED anti-burn-in screensaver** drops a black/noise curtain on idle and corrupts
  `screencap`; only a touchscreen `input tap` (on an empty area — not screen-centre, which launches the
  focused game) resets its timer. adb key events do not.
- **Contaminated first measurement:** measuring `top` too soon after launch catches startup background
  work, not steady state. This is exactly the error that produced the wrong "sway costs 75%" conclusion.

## Definition of done

Root-caused (a named UI-thread cost), a fix that brings the **idle** couch shell to a low single-digit
CPU on the Thor, verified by the recipe above (settled, sway off and on), with the temporary
`StartSampling` hack removed.
