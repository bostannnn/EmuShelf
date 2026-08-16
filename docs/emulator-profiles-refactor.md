# Emulator profiles — what shipped and how to finish the refactor

This documents the incremental "multiple emulators per console" work (see the 2026-08-05 entry in
`DECISIONS.md`) and the concrete steps to reach the full LaunchBox-style "profile owns everything"
end state in later passes. Read this before adding another alternative emulator or generalising the
model. The Android port adds a dimension none of the desktop sections below model — the operating
system — so read "OS-aware profiles and Android" before touching launch or saves for Android.

## What a "profile" is today

A **profile** is a `(SystemId, EmulatorId)` pairing. Per system, one profile is **active**; the
active profile decides which emulator launches, which saves sync, and which texture packs (if any)
are inventoried. The pieces:

| Concern | Where it lives now |
| --- | --- |
| Which emulators can serve a system | `EmulatorDefinition.SupportedSystemIds` (`Integrations`) |
| Stored per-profile config | `EmulatorConfigs (SystemId, EmulatorId)` + `SystemEmulatorSelection` (schema v16) |
| Active-profile resolution | `IEmulatorConfigurationStore.Get` returns the active profile; `GetProfiles`/`GetAllProfiles`/`SetActiveEmulator` for the rest |
| Launch | `EmulatorLaunchService.ResolveEmulator` picks the emulator from `config.EmulatorId`; the launch *kind* is the polymorphic `EmulatorLaunchTarget` (`DirectExecutableTarget` vs `FlatpakApplicationTarget`) |
| Saves | `SaveProviderRegistry` — one descriptor per system; PS1 branches on `SaveProviderContext.ActiveEmulatorId` |
| Textures | `TexturePackProviderRegistry` — one descriptor per (system, emulator); `TexturePackCoordinator` sits out non-active emulators |
| UI | `EmulatorSettingsRowViewModel` per-row emulator picker (Desktop only) |

## The reference conversion (done)

- **PlayStation** now supports **DuckStation** (default) and **RetroArch** (Beetle PSX /
  SwanStation / PCSX ReARMed). This is the worked example every other multi-emulator system should
  copy.
- The save registry keeps **one descriptor per system** and branches internally on the active
  emulator id. This is deliberate — it keeps the Saves section one-row-per-console and every existing
  test/coordinator path stable.

## Known limitations / deferred (pick these up next)

1. **PS1 save branching is an `if (IsRetroArch(...))` inside the descriptor.** It does not generalise
   to N emulators per system. The clean version is per-`(system, emulator)` save descriptors plus a
   `Resolve(systemId, activeEmulatorId)` that presentation iterates over `SystemIds` (not `All`). That
   touches `CloudSaveSyncCoordinator.DescribePlatforms`, the test `CreateCloudContext`, and any `All`
   consumer, so it was left out of the incremental pass.
2. **Profile *selection* is Desktop-only.** Gamepad ships a *reduced* Emulators section, not no section:
   `GamepadSettingsViewModel` excludes only `SettingsSection.Themes`, and its Emulators rows project
   per-platform library actions (sync/rescan/folder) — never the executable/args/core editor or an
   emulator picker, which stay Desktop-only. The Gamepad Saves/Texture sections already reflect the
   active profile because they project the shared `CloudPlatforms`/`TexturePlatforms`, but a controller
   user cannot yet *switch* a console's emulator. Add a Gamepad-projected emulator picker to that
   reduced section.
3. **PS1 textures are DuckStation-only** (verified against the libretro/DuckStation docs, Aug 2026).
   Among RetroArch PS1 cores, **only Beetle PSX HW** (`mednafen_psx_hw`, Vulkan renderer) supports
   texture replacement — SwanStation and PCSX ReARMed do not. So when RetroArch is the active PS1
   profile the texture row sits out. Two facts make a Beetle provider a *distinct* provider, not a
   reuse of the DuckStation adapter:
   - **Different storage model.** Beetle reads/writes `<game_filename>-texture-replacements/` **next
     to the ROM** (same dir as the `.cue`/`.bin`/`.m3u`), whereas every existing EmuShelf texture
     provider scans an *emulator-owned* directory. A Beetle inventory would be a per-game sibling-
     folder walk keyed on the game filename (read-only), not a single-root scan.
   - **Packs are not interchangeable.** DuckStation keys textures by game serial with its own
     `texupload-…` hash naming; Beetle keys by filename with a different hashing scheme, so a
     DuckStation pack is meaningless to Beetle and vice versa. There is no shortcut of pointing
     Beetle at DuckStation's folder.
   If added later, gate it on the Vulkan renderer and pin it to a specific core/format version, since
   Beetle's format is still explicitly work-in-progress.
4. **Save-shape display text for PS1 is emulator-neutral** ("Memory-card saves from the configured
   PlayStation emulator"). The live detection warning is accurate per emulator; the static shape line
   is generic. A per-profile display string would need the descriptor to be profile-aware.

## The full-unification pass (the "profile owns everything" end state)

Goal: adding an emulator becomes *write one profile, register it once*, instead of editing
`KnownEmulators` + `SaveProviderRegistry` + `TexturePackProviderRegistry` in two projects.

Suggested shape:

1. Define a Core/Integrations `IEmulatorProfile` (or extend `EmulatorDefinition`) that carries, or
   references via Core-defined factory interfaces, the emulator's:
   - supported systems + launch template + core/content requirements (already on `EmulatorDefinition`);
   - `ISaveLocationProvider` factory;
   - texture-pack provider factory;
   - config-detection specifics.
2. Move the App-layer context types (`SaveProviderContext`, `TextureProviderContext`,
   `TexturePackProvider`) down to Core/Integrations so a profile in Integrations can reference them.
   This is the main layering cost and why it was deferred.
3. Make `SaveProviderRegistry` / `TexturePackProviderRegistry` *derive* their tables from
   `KnownEmulators` (one registration point) rather than hand-maintained parallel lists, keyed by
   `(systemId, emulatorId)` with a `Resolve(systemId, activeEmulatorId)` used by presentation.
4. Convert the remaining emulators (PCSX2, RPCS3, Dolphin, PPSSPP, Azahar, the other RetroArch
   systems) to the cohesive shape one at a time — each should be a no-user-visible-change refactor
   with its existing tests still green.
5. Consider a **per-game** emulator override on top of per-system selection (LaunchBox allows this).
   That needs a per-game column and launch/menu UI and is a separate feature, not part of unification.

## OS-aware profiles and Android — the end state

The sections above are desktop-shaped. Android forces a dimension none of them model — the
**operating system** — and clears up one thing that turns out to be a non-issue.

**Already solved, do not re-plan it: saves are per-emulator.** Cloud unit ids lead with the emulator,
not the system — `duckstation/`, `pcsx2/`, `rpcs3/`, `ppsspp/`, `azahar/`,
`dolphin/gc/` · `dolphin/wii/`, and `retroarch/{systemId}/` (RetroArch is the one that then sub-keys by
system under a shared `retroarch/` root — still emulator-first, so no collision). PS1 on DuckStation
(`duckstation/…`) and
PS1 on RetroArch (`retroarch/playstation/…`) occupy separate cloud namespaces and never collide. So
each emulator already owns its own save scope; there is no cross-emulator save mixing to design around
and no "canonical card per platform" to build. If a user wants two format-compatible emulators to
share a card (e.g. the PCSX2 family's `.ps2` images), the mechanism is the per-emulator folder
override below, pointing both at one folder — EmuShelf never converts save formats.

### The real gaps for the end state

1. **Profiles must become OS-aware.** `EmulatorDefinition.SupportedSystemIds` is a flat, OS-agnostic
   list. Desktop launch is *not* OS-blind — it already resolves macOS `.app` bundles and runs Linux
   Flatpaks — but that OS-awareness lives imperatively in `EmulatorLaunchService` and the polymorphic
   `EmulatorLaunchTarget` (`DirectExecutableTarget` vs `FlatpakApplicationTarget`), not as a declared
   dimension, and nothing models Android. On Android a platform's emulators are *different apps* than
   on desktop (PS2 → ARMSX2 / AetherSX2 / NetherSX2 / a PCSX2-Android build, not desktop PCSX2),
   launched by intent, not an executable. The clean move is a **third `EmulatorLaunchTarget` subtype**
   (an Android intent/package target) selected per OS, carrying package/activity + intent template +
   **handoff strategy** (plain path / content-URI / SAF tree) + **maintenance status** — not a fresh
   data block bolted onto `EmulatorDefinition`. Mind the layer: today's launch data already lives on
   the stored `EmulatorConfiguration` (schema v16) and its `EmulatorLaunchTarget`, not on the
   Definition, so "pure data on the profile" is off by a layer and the migration is not free (see the
   open decisions below).

2. **The save-folder *override* must move from per-system to per-`(system, emulator)`.** The per-
   emulator save *scope* is already right, but the user's folder override is keyed by `systemId`
   (`CloudSaveSyncSettings.SaveLocations`), looked up as `GetOverride(systemId)` and handed to whichever
   emulator is active — so two emulators on one system share one override folder. Harmless on desktop;
   wrong on Android, where each emulator hides its saves in its own `Android/data/<pkg>` and needs its
   own granted folder. Re-key the override by `(system, emulator)`. (This is exactly NeoStation's
   `user_custom_save_folders` table, keyed by `(system_folder_name, emulator_slug)`.)
   **Two migration subtleties this hides.** (a) The re-key is *cross-store*: the overrides live in
   `settings.json` (`CloudSaveSyncSettings.SaveLocations`, System.Text.Json), but the emulator each
   existing `systemId` override should map to is the system's *active* emulator, which lives in **SQLite
   `SystemEmulatorSelection` (schema v16)** — so the settings migration has to read the DB to place a
   legacy override. (b) `SaveLocationSettings` bundles the override *with* per-system result metadata
   (`LastSuccessUtc`, `LastError`, `SyncSaveStates`, `StateDirectoryOverride`) under one `systemId` key;
   moving just the override to `(system, emulator)` either splits that record or moves all of it
   per-emulator — which then changes the per-system state the one-row-per-console Saves UI reads. Decide
   which, and follow the existing `NormalizeSaveLocations` legacy-fold pattern (and the
   `Pcsx2ConfigDirectory` / `PpssppMemoryStickDirectory` back-compat fields) so an older build still
   loads the file.

3. **Android save/launch providers do not exist.** Every current provider (DuckStation, PCSX2, RPCS3,
   Dolphin, PPSSPP, Azahar, RetroArch) is a desktop resolver. Android needs its own per-
   `(system, emulator)` providers whose save resolution **probes known `Android/data` locations, then
   falls back to the per-`(system, emulator)` override** — because on Android 11+ those folders are
   often unreadable and cannot be derived from the emulator's own config the way the desktop providers
   do. The per-emulator *scheme* is right; the Android *members* of it are missing.

4. **The registry still branches per system.** `SaveProviderRegistry` is one descriptor per system with
   the PS1 RetroArch-vs-DuckStation split in **two** places inside the descriptor — the `CreateProvider`
   ternary (`IsRetroArch(context.ActiveEmulatorId)`) and a parallel type-check in `DetectAsync` — both
   of which have to be generalised. Move to `(system, emulator)` descriptors derived from one emulator
   registration, with `Resolve(system, activeEmulatorId)` for presentation — the cleanup already noted
   under "Known limitations," now also required to hold N Android emulators per platform.

### Prior art — NeoStation

`misobadev/neostation-frontend` (full source, Flutter/Dart) makes exactly this split: the *declarative*
half is data (one JSON per system: metadata, an `emulators[]` array with per-OS launch strings +
default/RetroAchievements flags, and a `neosync` block of per-OS save-folder tokens), and the
*procedural* half is code (a path resolver expands tokens like `{PCSX2_MEMCARDS}` by probing known
paths; per-`(system, emulator)` user folders live in SQLite; Switch saves get a titleId-aware
resolver). Its cloud layout is `saves|states/<system>/<emulator-slug>/…` — per-emulator, same as ours.
Worth copying for the launch catalog; its token → probe → user-override pattern is the model for
Android saves.

### The first decision — is OS a launch *kind* or an emulator *identity*? (settle this before "how data-driven")

The gaps above straddle two different data models, and which one is right must be settled first — it
decides what a "catalog entry" even is:

- **OS as a launch kind (one emulator, per-OS launch blocks).** A single profile — say PS2/PCSX2 —
  carries a desktop launch (executable) and an Android launch (intent). Fewer entries; fits emulators
  that genuinely span OSes (RetroArch, Dolphin, PPSSPP, DuckStation's Android build). Breaks down where
  the Android app is a *different program* with a different save format and different RetroAchievements
  support (NetherSX2 / AetherSX2 are not "PCSX2"), and where one OS offers *several* choices for a
  system — you cannot hang three Android PS2 apps off one PCSX2 profile.
- **OS as identity (Android emulators are their own profiles) — recommended.** NetherSX2, AetherSX2, an
  ARMSX2 build, etc. are distinct `EmulatorId`s that *declare which OS(es) they run on*; the shared
  RetroArch / Dolphin / PPSSPP builds declare multiple OSes. This keeps save format, RA capability, and
  maintenance status attached to the thing they belong to, and lets a system offer N emulators per OS.
  Cost: more catalog entries, a `SupportedOperatingSystems` (or per-OS `EmulatorLaunchTarget`) on the
  profile, and active-profile selection that filters by the current OS.

The "per-OS launch block on one profile" model only fits the cross-OS emulators; it cannot express the
Android-only long tail, which is the whole point of the port. Recommend **OS as identity** and record
the choice in `DECISIONS.md` before writing any catalog.

### The second decision — how data-driven to go (record it, do not pre-decide)

- **Data-driven launch catalog + code resolvers (recommended).** Move launch to data (JSON or a table),
  keep save/texture/detection as code resolvers selected by the profile. Gets the "add an emulator = a
  data edit" win where it matters (Android launch, the long tail of cores) without rewriting working,
  tested desktop detection.
- **Full JSON system defs (NeoStation-style).** Everything declarative in JSON, code only behind
  tokens. Cleanest long-term and proven cross-platform, but it is a migration of a working desktop
  product (schema v16, migrations, precise config-derived save paths → probe-and-ask) — desktop
  regression risk for a mostly-Android benefit.

Either way the *procedural* logic (save-folder derivation, texture inventory, compatibility keys) stays
code. JSON only moves the declaration, not the procedure.

### Out of scope: texture packs on Android

The end state above is launch + saves only. Texture packs are **desktop-only** and stay that way:
Android emulator builds do not expose the DuckStation / PCSX2 / Dolphin / PPSSPP replacement-texture
inputs the desktop providers inventory, and `TexturePackProviderRegistry` is desktop directory finders
end to end. So an OS-aware profile marks its texture-pack factory **desktop-only** (no Android member,
no probe) rather than leaving it unmodeled — the "profile owns everything" registration must not imply
a texture row on the Thor. Revisit only if an Android core ships a real texture-replacement path.

### UI end state

One row per console, with an emulator picker **and** a transport chooser in **both** desktop and
gamepad. The gamepad Saves rebuild (today `allowManagedTransport: false`, rclone-only — see
`docs/cloud-sync-portability-plan.md`) lands here, since the Thor is gamepad-only and needs both the
emulator picker and the built-in transport.

### Litmus test for "done"

Adding a new Android PS2 emulator is: add one catalog entry (its intent + handoff strategy), point its
saves at a probe or a folder, register once — and it appears on the Thor with launch and per-emulator
save-sync working, with no code spread across two projects.

### Where this sits in the port

This refactor is a prerequisite for `docs/android-port-plan.md` Milestone B (launching games) and
E-android (reaching the saves). Do it before, or as the first part of, those.

## Guardrails to preserve (do not regress)

- One row per console in Settings, Saves, and Textures (`Rows.Count`, `SaveProviderRegistry.SystemIds`
  invariants and their tests).
- `Get(systemId)` returns the **active** profile; launch falls back to first-supporting when there is
  no usable selection (keeps single-emulator systems and the launch-service tests unchanged).
- Migrations stay self-healing and transactional; never lose a portable relative path or an existing
  shared installation.
- RetroAchievements stays emulator-agnostic (per-system console mapping).
- Read-only contract: never modify game files or emulator configuration.
