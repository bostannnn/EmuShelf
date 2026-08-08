# M40 pivot — uniform keyboard-hotkey scheme + Steam Input preset

**Status:** design settled, ready to implement in a fresh session on this branch
(`claude/emushelf-hotkey-config-d2544c`). The branch currently holds the **original
controller-chord implementation** (green: `dotnet build`/`dotnet test` pass). This document is the
spec to pivot it to a keyboard scheme. Update [DECISIONS.md](../DECISIONS.md) and ROADMAP M40 as part
of the work — both currently describe the controller approach.

## Why the pivot (read first)

The first implementation wrote **controller** hotkeys into each emulator. Testing on the user's real
setup proved that approach is fundamentally controller-specific and fragile:

- **RetroArch** stores raw, driver-specific joypad button *numbers*; the standard table was wrong for
  the user's XInput wrapper (Select/Start were 7/6 not 6/7; faces reordered), so nothing fired.
- **Dolphin** binds by device *name*; **PPSSPP** two-button combos are "not planned" by the
  maintainer; only **DuckStation/PCSX2** (SDL position tokens) survive a controller swap.
- Injecting keystrokes from EmuShelf is blocked: **RetroArch ignores injected keys** (raw input,
  libretro #16438); reliable injection needs a kernel virtual-HID driver (reWASD/ViGEm) — not
  portable.

**The fix (user-directed): write a uniform _keyboard_ scheme.** Keyboard keys are identical on every
controller, so all the fragility disappears. The controller→keyboard step is done **once**, outside
the emulators, in a **Steam Input** layout the user configures (bundled preset). One combo→key
mapping works for every emulator because they all listen for the same keys.

## Canonical scheme

| Action | Key |
|---|---|
| Rewind | `R` |
| Fast-forward | `L` |
| Save state | `F2` |
| Load state | `F4` |
| Close game | `F8` |

Keys chosen to match RetroArch's own defaults (rewind=r, hold_fast_forward=l, save=f2, load=f4) so
RetroArch needs almost nothing. `F8` (close) is free of conflicts across all emulators checked.

## Scope — which actions each emulator supports

| Emulator | rewind | fast-fwd | save | load | close |
|---|:--:|:--:|:--:|:--:|:--:|
| DuckStation | ✓ | ✓ | ✓ | ✓ | ✓ |
| PCSX2 | — | ✓ | ✓ | ✓ | ✓ |
| Dolphin | — | ✓ | ✓ | ✓ | ✓ |
| PPSSPP | ✓ | ✓ | ✓ | ✓ | ✓ |
| RetroArch | ✓ | ✓ | ✓ | ✓ | ✓ |
| Azahar | — | ✓ | ✓ | ✓ | ✓ |
| RPCS3 | — | — | — | — | ✓ |

Unsupported actions are reported (`HotkeyBindingStatus.Unsupported`), never bound.

## Conflict policy: **overwrite** (user-chosen)

Where a target key already has an emulator default, overwrite it — our action wins; the displaced
default loses only its keyboard shortcut (still in the emulator's menu). Everything is backed up
before the first write and Revert restores it. Use the existing base's conflict-clearing
(`KeysWithValue` + remove other keys holding the same value).

## Per-emulator exact tokens (all verified against the real configs on `G:`)

Config-dir resolution already exists via `EmulatorUserDirectories.FindX(...)` (RetroArch/Dolphin
included). File formats are edited with the existing `EmulatorConfigDocument` (surgical, preserves
comments/order/newlines). **Verify each token against a real file before shipping — a wrong token is
exactly what bit us on RetroArch.**

### DuckStation — `settings.ini` `[Hotkeys]`
Token = `Keyboard/<Key>` (e.g. `Keyboard/F2`). Version gate `[Main] SettingsVersion = 3`.
- `Rewind = Keyboard/R` **and set `[Main] RewindEnable = true`** (else dead key)
- `FastForward = Keyboard/L`
- `SaveSelectedSaveState = Keyboard/F2`
- `LoadSelectedSaveState = Keyboard/F4`
- `PowerOff = Keyboard/F8`
- Conflict to overwrite: `SelectNextSaveStateSlot` defaults to `Keyboard/F4`.

### PCSX2 — `inis/PCSX2.ini` `[Hotkeys]` (same engine as DuckStation)
Token = `Keyboard/<Key>`. Version gate `[UI] SettingsVersion = 1`. **No rewind.**
- `HoldTurbo = Keyboard/L` (fast-forward)
- `SaveStateToSlot = Keyboard/F2`
- `LoadStateFromSlot = Keyboard/F4`
- `ShutdownVM = Keyboard/F8`
- Conflict to overwrite: `ToggleFrameLimit` defaults to `Keyboard/F4`.

### Dolphin — `User/Config/Hotkeys.ini` `[Hotkeys]`
Token = `` `DInput/0/Keyboard Mouse:<Key>` `` (keyboard device verified from their `GCKeyNew.ini`;
fully-qualified so it works regardless of the `Device =` line). **No rewind.**
- `Emulation Speed/Disable Emulation Speed Limit = `DInput/0/Keyboard Mouse:L`` (fast-forward analog)
- `Save State/Save to Selected Slot = `DInput/0/Keyboard Mouse:F2``
- `Load State/Load from Selected Slot = `DInput/0/Keyboard Mouse:F4``
- `General/Exit = `DInput/0/Keyboard Mouse:F8``

### PPSSPP — `memstick/PSP/SYSTEM/controls.ini` `[ControlMapping]`
Token = `1-<NKCODE>` (device 1 = keyboard). **NKCODE (Android): R=46, L=40, F2=132, F4=134,
F8=138** (confirmed: their file has Pause=1-111=Esc, FF=1-61=Tab). No `AllowMappingCombos` needed —
these are single keys, not combos.
- `Rewind = 1-46`
- `Fast-forward = 1-40`
- `Save State = 1-132`
- `Load State = 1-134`
- `Exit App = 1-138` (close; control name is literally `Exit App`, from `Core/KeyMap.cpp`)

### RetroArch — `retroarch.cfg` (flat, no sections)
Token = quoted lowercase key, e.g. `"f2"`. Defaults already match rewind/save/load.
- `input_rewind = "r"` **and `rewind_enable = "true"`**
- `input_hold_fast_forward = "l"`
- `input_save_state = "f2"`
- `input_load_state = "f4"`
- `input_exit_emulator = "f8"` (default was `"escape"`)
- **Also clear the controller hotkeys the first implementation wrote** — set these back to `"nul"`:
  `input_enable_hotkey_btn`, `input_exit_emulator_btn`, `input_rewind_btn`,
  `input_hold_fast_forward_btn`, `input_save_state_btn`, `input_load_state_btn`.

### Azahar — `user/config/qt-config.ini` `[UI]` (Qt `QSettings`)
Keys are URL-encoded with `%20` for spaces and `\` separators, paired with a `\default` flag. To set
a shortcut: write `Shortcuts\Main%20Window\<Name>\KeySeq=<QKeySequence>` **and**
`Shortcuts\Main%20Window\<Name>\KeySeq\default=false`. QKeySequence values are plain (`F2`, `L`, …).
`EmulatorConfigDocument` handles these keys (split on first `=`). **No rewind.**
- Save → `Quick Save` KeySeq `F2`
- Load → `Quick Load` KeySeq `F4`
- Close → `Stop Emulation` KeySeq `F8`
- Fast-forward → `Toggle Turbo Mode` KeySeq `L`
- Conflicts to overwrite (clear their KeySeq): `Load Amiibo` (F2), `Continue\Pause Emulation` (F4).

### RPCS3 — `GuiConfigs/CurrentSettings.ini` — **close only**
Shortcut IDs from RPCS3 source (`rpcs3qt/shortcut_settings.*`): close = `gw_stop`. Value is a
QKeySequence `F8`. RPCS3 has **no load-state hotkey** and no rewind/fast-forward (save is Ctrl+S,
suspend/resume model), so only close is written. **OPEN ITEM: confirm the exact INI section header**
that the `gui::sc` group serializes to (check `gui_save.h` / where `gui::sc` is defined) before
writing — the user's file currently has no such section (defaults unwritten).

## Steam Input preset (bundle + document)

Provide a Steam Input layout that, while **Select is held**, maps: Square→`R`, Circle→`L`,
Triangle→`F2`, Cross→`F4`, Start→`F8`. One layout works for every emulator (shared keys).
- Bundling: ship the config file + import instructions (Steam applies configs per-app; no clean
  auto-install for third parties). Natural on the Steam Deck; on desktop the emulator must be
  launched under Steam Input.
- **UNVERIFIED LINCHPIN:** confirm Steam Input's emulated keystrokes actually reach **RetroArch**
  (it filters injected keys). RetroArch already has the keyboard hotkeys, so this can be tested with
  zero code. If Steam Input keys don't drive RetroArch, revisit before promising RetroArch.

## Code map — reuse vs. replace

**Reuse unchanged (infrastructure):**
- `Integrations/Emulators/EmulatorConfigDocument.cs` — surgical editor.
- `Integrations/Emulators/HotkeyConfigBackup.cs` — backup/revert.
- `Integrations/Emulators/HotkeyConfiguratorBase.cs` — plan/apply/revert scaffolding + conflict
  clearing (rename the `ChordResolution`/`ApplyChordSection` internals to binding terms).
- `App/Services/HotkeyCoordinator.cs`, `HotkeyProviderRegistry.cs`,
  `ViewModels/HotkeyEmulatorRowViewModel.cs`, the `SettingsSection.Hotkeys` UI — adapt copy only.
- `Core/Hotkeys/`: `HotkeyAction`, `HotkeyActionSupport`, `HotkeyApplyResult`,
  `IEmulatorHotkeyConfigurator` — keep.

**Replace:**
- `Core/Hotkeys/HotkeyProfile.cs` + `ControllerButton.cs` + `ControllerChord.cs` → a `HotkeyKey`
  enum (`R,L,F2,F4,F8`) and an action→key `HotkeyProfile`. Rename `HotkeyBindingResult.Chord` → the
  key label.
- The 5 configurators (`DuckStation/PCSX2/Dolphin/Ppsspp/RetroArch`) → keyboard tokens above.
- `IniChordHotkeyConfigurator.cs` → a keyboard INI base (`Keyboard/<Key>`), keep the version gate +
  DuckStation `RewindEnable` hook.
- **Delete RetroArch's autoconfig/raw-button-number resolution** — keyboard needs none of it.
- **Add** `Azahar/AzaharHotkeyConfigurator.cs` and `Rpcs3/Rpcs3HotkeyConfigurator.cs`; register both
  in `HotkeyProviderRegistry` (`EmulatorUserDirectories.FindAzahar` exists; RPCS3 config dir is the
  RPCS3 install dir's `GuiConfigs/`).

## Testing

- Reuse the fixture-based test pattern (`tests/EmuShelf.Infrastructure.Tests/Emulators/`): each
  configurator against a real-shaped fixture; overwrite/backup/revert/idempotency; the App-layer
  coordinator tests. Keep `dotnet build`/`dotnet test` green (macOS bar + zero warnings).
- Real configs: `G:\ES-DE\Emulators\<emu>\…`. A seeded test build lives at `G:\EmuShelf-M40-test\`
  (copied from the live `G:\EmuShelf\` install; **the live install is untouched**). See the
  `real-game-library-locations` and `emulator-hotkey-config-formats` memories.

## Open items for the new session
1. Confirm RPCS3's shortcut INI **section header** (`gui::sc`).
2. **Test the Steam-Input→RetroArch keystroke linchpin** (zero-code, via the existing keyboard
   defaults) before finalizing RetroArch.
3. Rewrite DECISIONS.md M40 entry + ROADMAP M40 to describe the keyboard scheme (they still say
   controller).
