# Android production-readiness review

Reviewed 2026-09-12. Scope: the existing arm64 handheld **sideload APK**, with AYN Thor
as the primary acceptance device. This is a source/configuration audit, not device
certification. Android remains explicitly experimental in the install guide and CI.

## Verdict

Implementation completed for identity, startup/recovery and release safeguards.
Still not certified for production until the device acceptance below is closed. The app already has substantial library functionality,
onboarding, progress feedback, release/AOT packaging, update installation and device
performance work. The missing finish is Android identity and launch presentation,
strict release gates, startup recovery, and a recorded release-candidate acceptance pass.

## Original audit findings

The first seven findings below are now addressed in code. The performance/device gate
remains open. This table preserves the original evidence and acceptance criteria.

| Priority | Gap and evidence | Completion criterion |
| --- | --- | --- |
| P1 | No Android launcher artwork. `src/EmuShelf.App.Android/Resources/` contains only strings and XML service/provider resources; the manifest application has no `android:icon` or `android:roundIcon`. The desktop `src/EmuShelf.UI/Assets/emushelf.ico` does not provide Android launcher resources. | Wire an original EmuShelf adaptive foreground/background, themed monochrome layer, and legacy icons. Check circle/squircle masks and launcher/Settings/Recents on device. |
| P1 | No branded native launch theme. `MainActivity.cs` selects `Theme.AppCompat.NoActionBar`; there are no app splash styles or drawables. | Add an AppCompat-compatible launch theme, Android 12+ splash customization and pre-12 fallback. Match the first Avalonia frame's background; verify cold/warm launches without a flash or duplicate splash. |
| P1 | No startup recovery boundary around composition. `App.axaml.cs`, `BuildInitialAndroidView`, calls `BuildAndRun()` synchronously before returning the initial view. `BuildAndRun` constructs `AppBootstrapper` and composes the shell before registering exception handlers. | Show a lightweight startup surface before expensive work; move eligible I/O off the UI thread while retaining UI construction there. On database/storage initialization failure, offer retry, data-folder recovery and diagnostics without resetting the library. Test a corrupt database and a folder becoming unwritable after resolution. |
| P1 | Release signing is optional. `package-android` in `.github/workflows/build.yml` falls back to debug signing when the keystore secret is absent, including on a tag. | Fail tagged Android packaging when signing inputs are missing. Verify the resulting APK certificate against the expected release certificate; keep PR builds independent of secrets. |
| P1 | Android can fail while the release is still published. The `release` condition requires only desktop package success. | Require Android success for releases advertised as supporting Android, or give Android a separately gated release workflow. Do not silently omit the production APK. |
| P2 | Downloaded checksum instructions do not match the checksum filename entry. CI runs `sha256sum artifacts/EmuShelf-android-arm64.apk`; the install guide tells users to download both files together and run `sha256sum -c`. The checksum therefore points into an `artifacts/` subfolder users do not have. | Generate the checksum from inside `artifacts`, so its entry is the APK basename. Exercise the published verification command from a fresh download directory. |
| P2 | Companion notification uses Android's generic `IcDialogInfo` and has no content intent (`SecondScreenKeepAliveService.BuildNotification`). | Use an EmuShelf monochrome small icon and make tapping the notification return to the app. Verify while an emulator occupies the primary display. |
| P2 | Known performance acceptance remains open. `docs/performance/thor-2026-09-06.md` records UI publication at 22.7–123.7 ms; `ROADMAP.md` retains pending projection, wizard/settings and Steam/GameNative device checks. | Close the recorded checks against the actual release candidate, with measured cold start, scrolling/search/platform switching and a longer game/return session. Do not substitute a successful build for these checks. |

## Artwork specification

- **Launcher:** one recognizable EmuShelf mark; adaptive foreground/background in a
  108×108 dp canvas with essential artwork inside the central 66×66 safe region;
  monochrome variant; scalable legacy vector for the supported API 24/25 devices. Reuse the existing product
  identity after inspecting the master artwork; do not reuse OpenEmu branding.
- **Native splash:** the same mark on a solid background coordinated with startup.
  Use Android's splash icon dimensions/masking, not a stretched landscape image.
  Android 12+ already supplies the launch surface, so integrate with it instead of
  adding a separate splash Activity. See [Android splash guidance](https://developer.android.com/develop/ui/views/launch/splash-screen)
  and [adaptive icon guidance](https://developer.android.com/develop/ui/compose/system/icon_design_adaptive).
- **Managed startup:** a matching, inexpensive view with an honest loading status and
  recovery state. No artificial delay or permanently animated decorative background.
- **Notification:** a dedicated legible monochrome small icon.
- **Optional polish:** empty-library illustration, permission-help diagrams and release
  screenshots. These are not substitutes for startup reliability. Existing empty-library,
  no-search-result, progress/toast and save-sync views already exist in
  `src/EmuShelf.UI/Views/GamepadShellView.axaml`; they do not need wholesale replacement.

## Release-candidate verification still required

These are verification gaps, not claims that each feature is broken.

- [ ] Inspect the final merged manifest: production certificate, non-debuggable build,
      package/version/ABI, icon/theme references and exported components.
- [ ] Verify native library ELF and APK ZIP alignment, then launch on a 16 KB page-size
      environment. AOT/Skia/native dependencies make this relevant even for sideloading;
      an SDK target alone does not prove compatibility. Follow the
      [Android native page-size checks](https://developer.android.com/guide/practices/page-sizes).
- [ ] Fresh install; first-run controller and touch navigation; denied/revoked storage
      permission; SD removal; read-only/full data volume; recovery without data loss.
- [ ] Upgrade from the previous published APK with the same key, preserving database,
      settings, covers and account state. Cancel and retry the unknown-source/install flow.
- [ ] Offline startup and cached library; provider outage/auth expiry; canceled imports;
      unavailable emulator; large-library import with responsive cancellation.
- [ ] Emulator launch/return, suspend/resume, process death, controller reconnect, IME,
      second-screen disconnect/reconnect and Shizuku unavailable/permission denied.
- [ ] Verify supported Android versions explicitly. The project declares API 24 minimum
      and targets API 36; the install guide describes Thor/API 33 development. Do not
      advertise the entire range as device-tested without evidence.
- [ ] Check touch targets, focus visibility, text clipping, contrast and accessibility
      on the smallest supported display. Record support limits for the landscape shell.
- [ ] Provide user-facing data/privacy documentation covering metadata providers,
      achievements, cloud saves, broad file access and optional accessibility/Shizuku use;
      verify included dependency notices and support/diagnostic instructions.
- [ ] Update the experimental support wording only after the acceptance gates pass.

Google Play publication is a separate scope; store graphics, AAB delivery and permission
policy review should be planned only if that distribution channel is requested.

## Review limitations

Source and CI inspection confirmed the gaps above. Local build and device verification
results are recorded below; no published release or signing-secret configuration was
inspected. The configured knowledge-base lookup could not run because Obsidian was not
running; the Wiki surface also reported no scope. Repository documents were used for
project history.

- The installed Android workload is 36.1.69. The initial Release build without restore
  failed with `NETSDK1004` because this checkout has no `project.assets.json`.
- A normal Release build was attempted next, but did not reach compilation while
  restoring dependencies; cancellation was requested. There is no successful local
  build result from this review.
- No Android device was exercised. Runtime appearance, frame timing and upgrade
  behavior remain acceptance work, rather than verified outcomes.

## Implementation verification (2026-09-12)

- Added native icon/splash resources; revised the initial flat shelf mark after user
  feedback to a tactile red/ivory/charcoal cartridge collection. Startup keeps the
  existing dark-theme brushes and Exo 2 font. Artwork/prompt: `src/EmuShelf.UI/Assets/Branding/`.
- Release/AOT arm64 APK published locally before the final artwork revision. Artifact verification passed for signature,
  non-debuggable manifest, launcher icon, ZIP alignment and all 139 native libraries.
  The final cartridge artwork also passed an Android Debug build and handheld layout test.
  The local build uses a development signing key; this is not a distributable release.
- Full UI suite: 1,152 tests passed. The final five startup tests also passed, including
  corrupt-database preservation, safe error details, reattachment and recovery rendering.
  Small-display screenshots were inspected and saved in `docs/prototypes/android-startup/`.
- Connected device identified as AYN Thor / Android 13 (API 33). Its installed APK uses
  a different certificate from the local build, so no update/uninstall was attempted.
  Signed in-place upgrade and live UI acceptance remain open.
- CI now requires all existing signing inputs on tags; exported keystore certificate
  is compared with the packaged APK. No new signing secrets are required. The workflow
  was syntax-parsed locally; GitHub-hosted execution remains to be observed.
- New support/data documentation: `docs/android-privacy.md`.
