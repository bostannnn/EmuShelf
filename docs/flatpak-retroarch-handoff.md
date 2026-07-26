# Handoff — Enable Flatpak RetroArch

**Status:** researched and empirically validated; no code written yet.
**Date:** 2026-07-26
**Branch at time of research:** `metadata-shared-title-resolution`

## Goal

Allow a Linux/SteamOS user to configure and launch **Flathub RetroArch**
(`org.libretro.RetroArch`) as an emulator target. Today EmuShelf hides the
Direct/Flatpak selector on RetroArch rows on every platform and hard-blocks the
launch, so a Deck user with only the Flatpak RetroArch installed cannot use
EmuShelf's RetroArch systems at all.

## Why this is being reopened

The exclusion rests on a rationale that testing proved false. Two decision-log
entries state that Flatpak RetroArch's cores are "private to the sandbox" and
its core paths cannot be inferred:

- `DECISIONS.md` — *2026-07-21, Typed launcher targets retain portable ownership boundaries*
- `DECISIONS.md` — *2026-07-25, Flatpak configuration is platform-aware, with explicit legacy migration*

Both predate the *2026-07-24* entry that introduced ephemeral `--filesystem=…:ro`
grants, and neither was revisited afterward.

## Verified findings — do not re-derive these

Tested on Ubuntu WSL2, flatpak **1.16.6**, RetroArch **1.22.2** (Flathub
`stable`), user install (`flatpak install --user`).

### 1. The per-app directory is mounted at the identical host path

There is **no** path remapping. Host and sandbox produce byte-identical listings:

```
/home/<user>/.var/app/org.libretro.RetroArch/config/retroarch/cores/genesis_plus_gx_libretro.so
```

A host path stored in `CorePath` can be passed straight to `-L` with **no
translation**. `File.Exists(CorePath)` on the host is also valid — the file
genuinely exists there.

### 2. Flatpak RetroArch already holds full host filesystem access

`flatpak info --show-permissions org.libretro.RetroArch`:

```
filesystems=xdg-run/pipewire-0;xdg-run/app/com.discordapp.Discord:create;
            xdg-config/kdeglobals:ro;/run/udev:ro;xdg-run/gamescope-0:ro;host;
            xdg-run/discord-ipc-0:create;
```

Note `host`. This is the **opposite** of PCSX2 (no user-file access), which is
what the 2026-07-24 grant machinery was built for. Launch matrix, all using
EmuShelf's exact argv order `flatpak run [opts] <appid> -v -L <core> <rom>`:

| Case | flatpak opts | Core loaded | Content read |
|---|---|---|---|
| ROM under `$HOME` | *none* | yes | yes |
| ROM on `/mnt/c` (external media) | *none* | yes | yes |
| ROM under `$HOME` | `--filesystem=<dir>:ro` | yes | yes |
| **control:** ROM under `$HOME` | `--nofilesystem=host` | no | no |

The control is the proof the harness detects failure: stripping `host` yields a
43-line log ending in `[ERROR] [Content] Could not read content file`, while a
working launch runs to 116 lines and negotiates `SET_PIXEL_FORMAT: RGB565`.
Filenames containing spaces worked in every passing case.

**Consequence:** EmuShelf's existing `:ro` grant is redundant for RetroArch. It
is also harmless — it composes fine with `host`. Leave
`BuildReadOnlyFilesystemGrants` alone; do not special-case RetroArch out of it.

### 3. Flatpak RetroArch ships zero cores

The deploy tree contains only `.info` metadata under `files/share/libretro/info/`
— no `*_libretro.so` anywhere. Users download cores via RetroArch's own core
downloader. Confirmed by the generated `retroarch.cfg`:

```
libretro_directory = "~/.var/app/org.libretro.RetroArch/config/retroarch/cores"
savefile_directory = "~/.var/app/org.libretro.RetroArch/config/retroarch/saves"
system_directory   = "~/.var/app/org.libretro.RetroArch/config/retroarch/system"
```

That directory is fully enumerable from the host. **This is the only place new
logic is required.**

### 4. Incidental

`/tmp` inside the sandbox is a private tmpfs — a host `/tmp` path is *not*
visible to the app. Irrelevant for real libraries, but do not use `/tmp` when
writing manual repro steps.

## What to change

### 1. Register the application id

`src/EmuShelf.Infrastructure/Launching/FlatpakApplicationDiscovery.cs:8`

Add `["retroarch"] = "org.libretro.RetroArch"` to `ApplicationByEmulatorId` and
`"org.libretro.RetroArch"` to `SupportedApplicationIds`.

### 2. Show the launch-target picker for RetroArch

`src/EmuShelf.App/ViewModels/EmulatorSettingsRowViewModel.cs:30`

```csharp
public bool CanSelectFlatpakTarget => OperatingSystem.IsLinux() && !RequiresCorePath;
```

Drop `&& !RequiresCorePath`. Platform-awareness (Linux-only) stays — that part
of the 2026-07-25 decision is still correct and is not being reversed.

Also update `UnsupportedFlatpakTargetMessage` at line 40. Its non-Windows branch
currently reads *"Flatpak RetroArch is unsupported because its cores are private
to the sandbox…"*, which becomes both false and unreachable on Linux. After the
change that branch is only reachable on macOS; reword it accordingly.

### 3. Remove the launch block

`src/EmuShelf.Core/Launching/EmulatorLaunchService.cs:113-115`

```csharp
if (target is FlatpakApplicationTarget && emulator.RequiresCorePath)
    return LaunchPreparation.Failed(…);
```

Delete it. Everything downstream already works: the `-L {CorePath} {GamePath}`
template check (`HasExplicitCoreAndContentForm`) is target-agnostic, the
`{EmulatorDirectory}` rejection does not apply to RetroArch's default template,
and `File.Exists(CorePath)` is valid against the host path.

### 4. Core discovery for a Flatpak target — the only real design work

`src/EmuShelf.App/ViewModels/EmulatorSettingsRowViewModel.cs:173` —
`RefreshAvailableCores` returns early when `ExecutablePath` is blank, and a
Flatpak target has **no** executable path. So the core picker comes up empty
even after steps 1–3.

Required behavior:

- When the row's target is Flatpak, derive the core directory from the **app id**
  rather than from `ExecutablePath`:
  `$HOME/.var/app/<appId>/config/retroarch/cores`.
- Keep the existing direct/AppImage search paths unchanged for direct targets
  (`<exe>/cores`, `$XDG_CONFIG_HOME/retroarch/cores`).
- Re-run discovery when `TargetKind` or `FlatpakAppId` changes, not only when
  `ExecutablePath` changes (see `OnExecutablePathChanged` at line 145 and
  `OnTargetKindChanged` at line 151).
- Update the `CoreSearchDirectories` comment block at line 206 — it explicitly
  forbids the Flatpak layout and states the now-disproven rationale.

Derive the path from the configured app id. Do not hardcode
`org.libretro.RetroArch` in the path builder — a user may enter a fork's id
manually, which the picker already permits.

### 5. Decision log

Append a `DECISIONS.md` entry that supersedes the two entries named above,
following the pattern the 2026-07-24 entry used. It must state plainly that the
"cores are private to the sandbox" rationale was **wrong**, not merely
outdated — the per-app dir is mounted at the identical host path and the Flathub
manifest already grants `host`. Record the tested versions (flatpak 1.16.6,
RetroArch 1.22.2) so the claim is falsifiable later.

## Tests

Existing tests that should still pass unchanged — check, do not assume:

- `tests/EmuShelf.App.Tests/EmulatorSettingsViewModelTests.cs:174`
  `EmulatorTargets_AreLimitedToTheCurrentPlatformAndLegacyFlatpaksCanMigrate`
  uses **DuckStation**, not RetroArch, so it is unaffected by the
  `RequiresCorePath` change. It still pins the Linux-only rule.
- `tests/EmuShelf.Infrastructure.Tests/Launching/RetroArchLaunchTests.cs` covers
  the direct/portable path only.
- `tests/EmuShelf.Infrastructure.Tests/Launching/EmulatorLaunchServiceTests.cs:150,179,216`
  cover Flatpak argv, grants, and `{EmulatorDirectory}` rejection.

Add:

- `EmulatorLaunchServiceTests`: a Flatpak RetroArch launch builds
  `flatpak run [grants…] org.libretro.RetroArch -L <core> <game>` with the core
  path passed through **unmodified**, and the app id positioned before all
  emulator arguments.
- `EmulatorLaunchServiceTests`: a Flatpak RetroArch config with a missing/blank
  `CorePath` still fails preflight with the existing core message — removing the
  block at :113 must not weaken core validation.
- `EmulatorSettingsViewModelTests`: on Linux a RetroArch row exposes the picker
  (`CanSelectFlatpakTarget`, `IsLaunchTargetPickerVisible`); on Windows/macOS it
  does not.
- `EmulatorSettingsViewModelTests`: with a Flatpak target and a fake
  `.var/app/<id>/config/retroarch/cores` tree, `AvailableCores` is populated
  without any `ExecutablePath`. Use a redirectable home root — do not touch the
  real `$HOME`.

Run `dotnet test`. Note `dotnet` may not be on PATH; use `$HOME/.dotnet/dotnet`.

## Do not do

- Do not write to `retroarch.cfg`, overrides, playlists, or achievement
  settings. EmuShelf reads only. (`CLAUDE.md`, and the M15 roadmap items.)
- Do not download, install, or manage cores. The picker selects an
  already-installed core file; a missing core stays a preflight error.
- Do not add a `flatpak override` call or otherwise persistently widen any
  sandbox. The ephemeral per-launch grant is the only permitted mechanism.
- Do not remove or narrow `BuildReadOnlyFilesystemGrants` for RetroArch —
  redundant here, still required for PCSX2 and others.
- Do not touch save sync in this change (see below).

## Open risks

1. **Deck confirmation still outstanding.** Mount semantics come from
   flatpak/bubblewrap and are distro-independent, and `host` comes from the
   Flathub manifest, so the result should hold. But SteamOS ships an older
   flatpak than 1.16.6, and a user who ran `flatpak override` could have
   narrowed permissions. One real launch on hardware closes this. This also
   completes the open M27 roadmap item for Linux/SteamOS hardware verification.
2. **Multi-file content unverified inside the sandbox.** Dreamcast CHD/GDI via
   flycast was not tested (needs BIOS), so CUE/M3U sibling resolution inside the
   sandbox is untested. With `host` there is no mechanism for it to differ, but
   it is unproven.
3. **Save sync scope creep.** The M29 RetroArch save-sync item resolves paths
   from `retroarch.cfg`; enabling Flatpak launches means that milestone inherits
   a second layout (`~/.var/app/<id>/config/retroarch/saves`). Out of scope
   here — note it in the roadmap rather than implementing it.

## Manual repro

Cores land in `$HOME/.var/app/org.libretro.RetroArch/config/retroarch/cores`
only after RetroArch has been run once (first run generates the tree).

```bash
flatpak install --user -y flathub org.libretro.RetroArch
flatpak run org.libretro.RetroArch                     # first run generates config tree
flatpak info --show-permissions org.libretro.RetroArch # expect `host` in filesystems
flatpak run org.libretro.RetroArch -v -L "$HOME/.var/app/org.libretro.RetroArch/config/retroarch/cores/<core>.so" "/path/to/rom.md"
```

Negative control — should fail with `Could not read content file`:

```bash
flatpak run --nofilesystem=host org.libretro.RetroArch -v -L "<core>" "<rom>"
```
