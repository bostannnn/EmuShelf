# AYN Thor shelf artwork verification — 2026-09-06

## Result

The updated shelf completed two 20-right/20-left GameCube sweeps with visible cover artwork and no
AndroidRuntime errors returned for its PID. Expensive recurring artwork uploads observed in the old
build were absent from both updated sweeps. This validates the targeted behavior on this device; it
is not an isolated benchmark of the patch or a measurement of input-to-photon latency.

| Observation | Previously installed build | Updated sweep | Updated repeat |
|---|---:|---:|---:|
| Worst sampled GL callback duration | 122.4 ms | 4.6 ms | 4.3 ms |
| Logged expensive/deferred upload passes in sweep window | 438 | 0 | 0 |
| Largest logged upload pass | 121.8 ms | None logged | None logged |

Upload logging is conditional: a pass logs only when it takes at least 4 ms or defers faces. Zero
logged passes does **not** mean zero uploads. GL duration comes from CPU Stopwatch timing around the
render callback, including driver calls and possible waits, not GPU timestamp queries or full frame
time. `glfps` counts callbacks and is not presented-frame FPS.

Ten seconds of updated idle measurements showed 12–15 GL callbacks/second (existing idle animation),
a worst sampled callback of 6.5 ms, and no logged expensive/deferred uploads. Diagnostics were disabled
after measurements; a screenshot confirmed the clean shelf with Wind Waker selected and neighbours
visible. Analogue rotation, launches, CRT mode and long-session memory stability were not tested.

## Method and comparison limits

- Physical AYN Thor, 1920×1080 main screen, GameCube library of 26 games, Shelf, CRT off,
  `shelf-inline-gl` path; existing user artwork and data. Both APKs contain 126 AOT libraries.
- Start at The Legend of Zelda: The Wind Waker, index 2 in descending title order. Send 20 Android
  DPAD_RIGHT events then 20 DPAD_LEFT events, with 120 ms waits plus input-command overhead. Updated
  sweeps include one second of settling before their end marker. Existing FPS/render diagnostics were
  enabled with triple L3 and the app's EmuShelfPerf sampler collected through ADB.
- Earlier baseline window: 22:48:40 through 22:48:48.279, extracted from the baseline navigation trace.
  Updated sweeps have explicit BENCH start/end markers; raw logs are alongside this file.
- **These are different application builds**, not the same commit with a feature flag. Baseline was
  the previously installed Steam experiment, version 833 / `1.8.1-steam`. Updated was this checkout plus
  shelf changes, version 839 / `1.8.1-shelf-review`. The Steam experiment is absent from this checkout.
- Baseline was collected with only 195 MB free on internal storage. The user freed space before the
  updated APK could install (3.1 GB available at that point). Cache warmth, process age, thermal state,
  background activity and memory pressure were not controlled. Do not attribute the entire numerical
  difference to the shelf patch or claim a percentage FPS improvement.

## Memory

Android values are KiB. Baseline memory was sampled after the original sweep and further idle time;
updated readings were taken after each sweep. Cache history differs, so no memory saving is claimed.

| Snapshot | Total PSS | Total RSS | Total swap PSS |
|---|---:|---:|---:|
| Baseline development process | 787,210 | 801,620 | 105,267 |
| Updated first sweep | 803,694 | 930,100 | 14 |
| Updated repeat + idle | 810,106 | 936,824 | 9 |

The second updated pass increased PSS by about 6.3 MiB; two passes do not prove a memory plateau.
Owned CPU upload buffers and pinned visible GPU textures are deliberate bounded retention costs.

## Deployment and final device state

Only `com.emushelf.steamtest` was updated, using its matching local debug signing certificate. The
regular release package `com.emushelf.app` was not changed. Every install used `-r`; no uninstall,
app-data clearing, cache clearing, game-file mutation or emulator launch was performed by this task.
The user independently freed device space after Android rejected the first update for insufficient
storage. An initial incremental package failed in generated Java/native application bindings before
reaching the shelf; cleaning generated Android bin/obj and rebuilding fixed startup (1,479 ms Android
activity launch time). The working original APK was restored while rebuilding.

After the successful comparison, the original Steam experiment APK was restored in place to preserve
its extra functionality. The verified shelf APK remains at:
`src/EmuShelf.App.Android/bin/Release/net10.0-android36.0/android-arm64/publish/com.emushelf.steamtest-Signed.apk`.

APK SHA-256:
- Original: `c7c67e85b07541fc387e92a61feb9c0cdca89cc975af9120237258da6671bd75`
- Verified shelf build: `2ba4a40f1818ef4170a2209ec0548cdec0ff864ac73a9d2c6a17ca2fe91dd06d`

The development package/provider overrides use the ignored temporary manifest
`artifacts/shelf-dev-manifest.xml`; production manifest/source was not modified for deployment.
