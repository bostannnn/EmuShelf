# Emulator profiles — what shipped and how to finish the refactor

This documents the incremental "multiple emulators per console" work (see the 2026-08-05 entry in
`DECISIONS.md`) and the concrete steps to reach the full LaunchBox-style "profile owns everything"
end state in later passes. Read this before adding another alternative emulator or generalising the
model.

## What a "profile" is today

A **profile** is a `(SystemId, EmulatorId)` pairing. Per system, one profile is **active**; the
active profile decides which emulator launches, which saves sync, and which texture packs (if any)
are inventoried. The pieces:

| Concern | Where it lives now |
| --- | --- |
| Which emulators can serve a system | `EmulatorDefinition.SupportedSystemIds` (`Integrations`) |
| Stored per-profile config | `EmulatorConfigs (SystemId, EmulatorId)` + `SystemEmulatorSelection` (schema v16) |
| Active-profile resolution | `IEmulatorConfigurationStore.Get` returns the active profile; `GetProfiles`/`GetAllProfiles`/`SetActiveEmulator` for the rest |
| Launch | `EmulatorLaunchService.ResolveEmulator` picks the emulator from `config.EmulatorId` |
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
2. **Profile *selection* is Desktop-only.** `GamepadSettingsViewModel` excludes
   `SettingsSection.Emulators` (pre-existing decision). The Gamepad Saves/Texture sections already
   reflect the active profile because they project the shared `CloudPlatforms`/`TexturePlatforms`,
   but a controller user cannot yet *switch* a console's emulator. Add a Gamepad-projected emulator
   picker when the Emulators section is brought to Gamepad mode.
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

## Guardrails to preserve (do not regress)

- One row per console in Settings, Saves, and Textures (`Rows.Count`, `SaveProviderRegistry.SystemIds`
  invariants and their tests).
- `Get(systemId)` returns the **active** profile; launch falls back to first-supporting when there is
  no usable selection (keeps single-emulator systems and the launch-service tests unchanged).
- Migrations stay self-healing and transactional; never lose a portable relative path or an existing
  shared installation.
- RetroAchievements stays emulator-agnostic (per-system console mapping).
- Read-only contract: never modify game files or emulator configuration.
