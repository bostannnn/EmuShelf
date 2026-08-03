# Gamepad Settings field audit

Audited against `EmulatorSettingsWindow.axaml`, `EmulatorSettingsViewModel`,
`EmulatorSettingsRowViewModel`, `CloudSavePlatformRowViewModel`, and
`TexturePackRowViewModel` on 2026-08-01. The Desktop window remains the complete source of settings
semantics; Gamepad presentation adapts the same properties and commands.

| Section | Desktop control | Classification | Gamepad behavior |
| --- | --- | --- | --- |
| General | Show empty platforms | Toggle | A or Left/Right toggles; saved through the existing Save command. |
| General | Automatically fetch metadata after import | Toggle | A or Left/Right toggles; saved through the existing Save command. |
| General | Rescan all consoles | Action | A runs the existing maintenance command; status stays in-window. |
| General | Fetch missing metadata | Action | A runs the existing metadata command; progress stays in-window. |
| General | Metadata-source links | External information actions | Kept as explanatory source text in the first controller slice; they are not settings values. |
| Emulators | Launch target | Choice | Deferred to the emulator-path slice; Left/Right chooses Direct or Flatpak. |
| Emulators | Executable/configuration path | Executable or folder path | Deferred; A uses the existing native executable/folder picker. |
| Emulators | Flatpak application id | Choice plus text | Deferred; installed ids are choices and a manual id uses shared text entry. |
| Emulators | Launch arguments | Text | Deferred; A opens shared text entry. |
| Emulators | Reset arguments | Action | Deferred; A runs the existing reset command. |
| Emulators | RetroArch core search | Text filter | Deferred; A opens shared text entry. |
| Emulators | RetroArch core | Choice | Deferred; D-pad browses the installed-core list and A selects. |
| Emulators | Clear core | Action | Deferred; A runs the existing clear command. |
| Emulators | Add/change game folder | Folder | Deferred; A uses the existing native folder picker. |
| Emulators | Forget game folder | Destructive EmuShelf-only action | Deferred; A opens an in-window confirmation. Game and emulator files remain untouched. |
| Emulators | Sync RPCS3 library / rescan library | Action | Deferred; A runs the existing read-only maintenance command. |
| RetroAchievements | Username | Text | A opens shared single-line text entry and requests an OS keyboard when supported. |
| RetroAchievements | Web API key | Secret | A opens masked text entry and requests a protected OS keyboard when supported. The value is cleared after a successful connection and never enters `settings.json`. |
| RetroAchievements | Connect | Action | A runs the existing account pipeline; validation and progress remain in-window. |
| RetroAchievements | Refresh matches | Action | A runs the existing refresh command; unchanged ROMs are not rehashed. |
| RetroAchievements | Disconnect | Account action | A confirms, then runs the existing disconnect command. Achievement state remains display-only. |
| RetroAchievements | API-key help link | External information action | The controller surface explains where the key comes from; opening a browser is not a setting mutation. |
| Saves | rclone remote name | Text | A opens shared text entry. |
| Saves | Cloud folder | Text | A opens shared text entry. |
| Saves | Google OAuth client JSON | File | A uses the existing native JSON picker. The client secret passes directly to rclone and is never displayed or persisted by EmuShelf. |
| Saves | Download rclone | Action | A runs the existing installer command; status remains in-window. |
| Saves | Per-platform save location | Folder | A uses the existing native folder picker; detected and disconnected-drive states remain visible. |
| Saves | Sync save states | Toggle | A or Left/Right toggles and uses the existing per-platform persistence callback. |
| Saves | Connect / disconnect Google Drive | Action | A runs the existing command; disconnect requires an in-window confirmation. OAuth remains owned by rclone. |
| Saves | Sync all / stop | Action | A starts or stops the existing cancellable operation; progress remains in-window. |
| Saves | Replace cloud / replace local | Destructive sync action | A opens an in-window confirmation before invoking the existing direction-specific command. Existing backup rules remain authoritative. |
| Saves | Sync activity log | External read-only action | Shown as status/path in the first controller slice; it does not change a setting. |
| Texture Packs | Rescan | Read-only action | A runs the existing inventory rescan. EmuShelf does not alter packs or emulator configuration. |
| Texture Packs | Per-platform root override | Folder | A uses the existing native folder picker. |
| Texture Packs | Use detected root | Action | A clears only EmuShelf's override and rescans. |
| Texture Packs | Open folder | External read-only action | The detected path remains visible; opening a desktop file manager is omitted from the controller surface. |
| Texture Packs | Emulator filter | Choice | Left/Right or A cycles the existing filter choices. |
| Texture Packs | Status filter | Choice | Left/Right or A cycles the existing filter choices. |
| Texture Packs | Pack inventory entries | Read-only information | D-pad can inspect installed-pack status, matched games, emulator, and source path. |
| Footer | Save | Action | START saves directly; D-pad Up from a section's initial row then A reaches the same persistent Save action. Both invoke the existing Save command and close back to Menu only on success. |
| Footer | Cancel | Action | B exits to Menu without invoking Save; the previously focused Settings menu row is restored. |

The first controller slice includes General, RetroAchievements, Saves, and Texture Packs. Emulator
paths, arguments, cores, and remembered library folders remain explicitly classified above but are
deferred to the next Phase 2 slice, matching `GAMEPAD-REDESIGN.md`.

`AutomaticallyFetchMetadataAfterImport` is an existing Desktop field and is persisted by the
existing `IMetadataPreferencesService`; Gamepad does not introduce a second value or preference.
Every mutating field in the four landed sections has a stable parity id on both real Desktop and
Gamepad controls. A real-window test compares Desktop's effectively visible field ids with the
complete virtualized controller projection for every section, while separately checking that each
realized controller row publishes its matching id. Read-only inventory/status rows and external
links are excluded because they are not settings mutations.
