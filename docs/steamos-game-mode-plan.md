# SteamOS, Gamepad Mode, and Linux Emulator Targets

## Summary

This plan adds Linux launcher support, an AppImage build for SteamOS, and a
controller-first Gamepad interface mode. EmuShelf continues to discover and
launch user-installed emulators only: it never installs, updates, configures,
or broadens Flatpak permissions for them.

The work is split into two post-M26 milestones:

- **M27 — Linux launcher targets and AppImage distribution.** Add typed launch
  targets for direct binaries, AppImages, and installed standalone Flatpak
  emulators; retain shell-free process execution; add a read-only Flatpak access
  preflight; and publish a self-contained portable AppImage.
- **M28 — Steam-Input-driven Gamepad interface mode.** Add a fullscreen,
  controller-first UI selected in Settings or forced for one launch with
  `--gamepad-ui`. It uses Steam Input keyboard mappings and an upper platform
  rail with LB/RB platform switching.

M27 ships first and is largely verifiable on an ordinary Linux desktop. M28 is
not SteamOS-exclusive, but its Gaming Mode and gamescope acceptance gate runs on
a real Steam Deck or SteamOS/gamescope environment.

## M27 — Linux launcher targets and AppImage distribution

### Typed targets and persistence

- Add Core launch-target types:
  - `DirectExecutableTarget(path)` for Windows executables, native Linux
    binaries, and AppImages.
  - `FlatpakApplicationTarget(appId)` for installed Flatpak emulators.
- AppImages remain direct executables. Settings detects the `.AppImage`
  extension only to label the selected direct target appropriately.
- A target belongs exclusively to the shared `EmulatorInstallations` record;
  never duplicate target kind/value on `EmulatorConfigs` rows. System configs
  retain only emulator/system identity, installation reference, launch template,
  and core selection.
- Add schema v11:
  - ensure every legacy config refers to an installation row (using the private
    IDs created by v8 where needed);
  - add `TargetKind` and `TargetValue` to `EmulatorInstallations`;
  - migrate every existing installation to `DirectExecutableTarget` without
    changing relative paths, shared installation IDs, templates, or direct
    RetroArch core paths;
  - retain the old per-config `ExecutablePath` only as a v8 legacy-read fallback;
    all future reads and writes resolve the target through the installation join.
- Keep direct-target and direct-core paths under the existing relative-path
  resolver. Never transform Flatpak app IDs or future runtime-only paths.

### Launch construction and template validation

- Replace path-only process-runner input with
  `ProcessStartSpec(FileName, Arguments, WorkingDirectory)`. Retain
  `UseShellExecute = false` and argument arrays.
- Direct targets map to their executable path plus existing expanded arguments.
  Flatpak targets map to `flatpak run <app-id> <expanded arguments...>`.
- Make `EmulatorLaunchService` target-aware and inject a Core
  `ILaunchTargetInspector` implemented by Infrastructure. All target validation
  completes before EmuShelf suspends/minimizes.
- Direct targets retain all existing placeholders. Flatpak targets reject any
  launch template containing `{EmulatorDirectory}` during preflight because an
  app ID has no meaningful executable directory. Default templates remain
  compatible.
- Preserve the current explicit RetroArch validation:
  `-L {CorePath} {GamePath}` remains mandatory for core-aware direct targets.

### Flatpak content access and dependency resolution

- Add Core `IGameLaunchDependencyResolver`. Its Integrations implementation
  reuses `ReferencedFileParser` and returns all paths required for a launch:
  - ordinary file/directory game: the launch path;
  - CUE: descriptor plus referenced tracks;
  - M3U: playlist plus referenced discs, recursively resolving referenced CUEs;
  - cycles, unreadable descriptors, malformed references, or a recursion bound:
    return an incomplete result.
- Flatpak launches require a complete dependency result. A direct launch retains
  current behavior and does not become blocked by descriptor parsing.
- The Infrastructure Flatpak inspector checks each required path with the
  machine-readable `flatpak info --file-access=<path> <app-id>` command:
  - `read` or `read-write`: pass;
  - `none`: fail before minimizing and name the emulator plus inaccessible path;
  - unavailable CLI, unsupported access inspection, or unparseable response:
    show a pre-launch warning and allow the user to attempt the launch.
- Do not parse `--show-permissions`, export documents, modify overrides, or
  broaden filesystem access. The user grants emulator access to ROM folders and
  removable media outside EmuShelf.

### Flatpak discovery and exclusions

- Add an integration-owned candidate registry and discover installed Flatpaks
  read-only. Settings presents compatible installed candidates and requires the
  user to choose one; it never auto-configures an emulator.
- Initial candidates are:
  - PCSX2: `net.pcsx2.PCSX2`
  - Dolphin: `org.DolphinEmu.dolphin-emu`
  - RPCS3: `net.rpcs3.RPCS3`
  - PPSSPP: `org.ppsspp.PPSSPP`
- Manually entered app IDs are permitted only after live validation confirms the
  application is installed. DuckStation remains direct/AppImage by default.
- Flatpak RetroArch is explicitly deferred. Linux RetroArch remains a direct or
  AppImage installation with adjacent-core discovery. Do not infer or scan
  RetroArch's private Flatpak core layout.
- A Flatpak RPCS3 target changes only launch behavior; its existing explicit,
  read-only library-location sync remains unchanged.

### AppImage distribution and portable data

- Add Ubuntu build/test coverage and publish a self-contained `linux-x64`
  AppImage alongside the current Windows artifact.
- Introduce an app-data-root resolver:
  - unpacked builds keep `AppContext.BaseDirectory`;
  - AppImage builds use the writable parent of `$APPIMAGE`, never the read-only
    `$APPDIR` mount;
  - `Data`, `Covers`, `Cache`, `Logs`, and `Settings` remain beside the AppImage.
- Test the Type 2 `--appimage-extract-and-run` fallback with the same data-root
  behavior. Bundle ICU in the self-contained AppImage; do not enable invariant
  globalization.
- Add AppImage desktop metadata, icon, checksum, and Steam Desktop Mode setup
  instructions. Do not ship EmuShelf itself as Flatpak in M27; that needs a
  separate portal/sandbox design.

### M27 verification

- Test v10-to-v11 migration, direct/Flatpak targets, shared installations,
  legacy private installations, and prevention of divergent target values.
- Test shell-free direct and Flatpak `ProcessStartSpec` construction, missing
  CLI/app, executable permissions, and `{EmulatorDirectory}` rejection.
- Test access preflight for `read`, `read-write`, `none`, unavailable inspection,
  CUE tracks, recursive M3U/CUE references, cycles, malformed descriptors, and
  dependencies outside a granted path.
- Build/test on Ubuntu, macOS, and Windows; validate the Linux AppImage payload
  and `$APPIMAGE` sidecar-data resolution in CI.
- On Linux/Deck hardware, launch direct/AppImage and Flatpak standalone emulators
  with paths containing spaces and removable-media ROM locations. Verify no game,
  emulator, or Flatpak data is modified.

## M28 — Steam-Input-driven Gamepad interface mode

### Interface mode and layout

- Add `InterfaceMode` (`Desktop`, `Gamepad`) to `AppSettings`, defaulting to
  Desktop. Add `--gamepad-ui` as a non-persisted launch override.
- Add App-layer `IInterfaceModeService` to apply/persist the selected mode and
  coordinate fullscreen window behavior.
- Desktop retains the existing sidebar and grid/list views. In Gamepad mode:
  - enter fullscreen;
  - replace the sidebar with an upper rail: `All Games`, then existing systems
    in order, plus a separate Collections menu containing `Recently Added`;
  - horizontally scroll the rail and automatically reveal the active tab; do not
    use paged rail groups;
  - show existing library title/count under the rail;
  - use large controller-readable controls and visible focus styling;
  - retain the virtualized cover grid and omit list view.
- Keep Settings reachable from the header so keyboard/mouse can always switch
  back to Desktop mode.

### Focus, actions, and Steam Input

- Add `FocusedGame` and `GameViewModel.IsFocused`; do not reuse desktop
  multi-selection. Preserve focused game by library scope after filtering,
  platform changes, reloads, and game return, otherwise focus the first
  available game.
- Navigate through calculated responsive-grid columns. A view-focused coordinator
  scrolls the realized focused tile into view; code-behind only forwards visual
  events and commands.
- Desktop click, Ctrl/Cmd selection, range selection, and Delete behavior remain
  unchanged.
- Add a focused-game action popup with Launch, Achievements when available, Edit
  title, Set cover, and Remove. Desktop context menus use the same actions.
- Up from the grid's first row focuses the active platform tab; Down restores
  game focus. Header controls expose Collections, Search, and Settings.
- Document and handle Steam Input mappings as root commands:
  - D-pad/left stick -> arrow keys -> move focused game;
  - A -> Enter -> launch focused game;
  - B -> Escape -> dismiss popup/search or return to library focus;
  - LB/RB -> Ctrl+PageUp/Ctrl+PageDown -> previous/next platform without wrap;
  - X -> search;
  - Y -> focused-game actions.
- Do not handle those commands while text input or a modal owns input. Document
  Steam's `Steam + X` on-screen keyboard chord so X search supports controller
  text entry.

### Frontend lifecycle and M28 verification

- Replace `IFrontendController.Minimize/Restore` with game-session
  suspend/resume methods. Desktop continues minimizing; Gamepad mode minimizes or
  lowers before launch, then restores fullscreen, activates, and restores focused
  cover/keyboard focus after tracked exit.
- Do not introduce `Hide()` unless a real Deck/gamescope test proves
  minimize/lower insufficient. Preserve the existing post-exit RetroAchievements
  refresh flow.
- Add tests for mode persistence, launch-flag precedence, focus movement and
  restoration, platform boundaries, rail reveal behavior, action routing,
  suspend/resume, and post-exit refresh.
- Add 1280x800 Gamepad-mode visual snapshots for upper rail, focus ring, empty
  state, action popup, and unchanged Desktop snapshots.
- Validate Gamepad mode first through Steam Input on Windows/Linux, then on a
  real Deck for Gaming Mode, `Steam + X`, AppImage launch, gamescope restore,
  RetroAchievements refresh, and Flatpak failure messages.

## Documentation and decisions

- Add M27 and M28 after the existing M26 roadmap milestone.
- Append decisions for installation-owned schema v11 targets, dependency-aware
  Flatpak access checks, Flatpak RetroArch deferral, portable AppImage data plus
  bundled ICU, and Steam-Input-only Gamepad mode.
- Document SteamOS setup, AppImage FUSE fallback, Flatpak emulator setup, Steam
  Input bindings, and the Deck acceptance checklist.

## Fixed assumptions

- Gamepad mode requires Steam Input for physical-controller operation; without
  Steam it remains keyboard/mouse usable but has no raw-controller support.
- Flatpak support covers standalone emulators only in M27/M28.
- Confirmed Flatpak access denial blocks launch; unavailable inspection warns and
  permits a user-initiated attempt.
- AppImage is the first SteamOS distribution; an EmuShelf Flatpak is later work.
