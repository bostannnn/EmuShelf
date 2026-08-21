# Android save-sync model — finalized

The finalized answer to "how do saves sync between desktop and the Thor, and across emulators?"
This is the data-model half of Milestone **E-android** (see [android-port-plan.md](android-port-plan.md)).
The transport half (managed Google Drive, OAuth) lives in
[cloud-sync-portability-plan.md](cloud-sync-portability-plan.md) and is unchanged by this document.

## The core principle (what the industry does)

Two mature, shipping solutions were checked and they agree, so EmuShelf follows the same shape:

- **NeoStation / NeoSync** syncs *native SRAM saves* ("fully compatible between different emulators")
  and treats save states as emulator-specific.
- **RetroArch's own cloud sync** says SRAM saves "generally work just fine, even between different
  cores/platforms," but save states need the same core version and *"sometimes this still does not
  work"* — and it requires the user to keep folder/sorting config identical on every device because
  the config itself is not synced.

Nobody converts save formats. The state of the art is: **sync the console-native save payload, key it
so the right file lands in the right place, gate the risky restores, and never delete — then make the
user align a small amount of emulator config for the cases that genuinely diverge.** EmuShelf already
implements most of this; this document finalizes the remaining decisions and the Android specifics.

The two safety invariants that make the whole thing tolerant of a wrong guess, and that every rule
below leans on:

1. **Sync never deletes.** A save that cannot be matched or restored stays in the cloud, untouched.
2. **The emulator is the final judge.** A save the emulator cannot read, it refuses on load. So a
   too-lax match degrades to "the emulator declined it," never to a corrupted save.

## Battery saves (SRAM / memory cards / EEPROM)

These are the console's own save data — the "official" progress. They are small, and for most systems
the *file itself is already emulator-agnostic*, which is what makes cross-emulator sync possible at all.

**Rule: sync the payload; key it by the console *system* it belongs to; align config where the
container format differs. Do not convert.**

The cloud key is `<systemId>/<localId>` (e.g. `playstation/per-game/<serial>`, `playstation2/<card>.ps2`,
`nds/<game>.srm`) — **the system owns the namespace, not the emulator** (superseding decision recorded in
DECISIONS 2026-08-21). Each provider derives its battery key from `SystemId` (`UnitIdPrefix = SystemId +
"/"`), so any emulator serving that system emits the same key and a save round-trips 1:1. Two payoffs:

### Any emulator for a system interoperates by construction

PS1 (DuckStation), GC/Wii (Dolphin), PSP (PPSSPP), 3DS (Azahar), and the RetroArch systems all key by
their `SystemId`, so desktop↔Android round-trips need no special handling — and, more importantly, a
*different* emulator for the same system (a second PS1 core, a future PS2 emulator) syncs with the same
key by construction. Nothing "adopts" another emulator's namespace and nothing has to be wired per pair.

### Different emulator per platform — same key, config alignment only

Two systems change emulator between platforms. Because the key is the system, no namespace adoption is
needed; the Android provider just emits the system key like every other. A per-emulator **setup
checklist** enforces the matching on-disk format. No converter is written.

| System | Desktop | Android | Cloud key | Config the checklist must enforce |
|---|---|---|---|---|
| PS2 | PCSX2 | ARMSX2 | `playstation2/` | Single-file `.ps2` memory card, matching filename (the `.ps2` card is "the universal currency" across PCSX2/AetherSX2/NetherSX2/ARMSX2). PCSX2 *folder* cards do **not** sync — the user must use a file card on both. |
| DS | RetroArch (melonDS core) | WatermelonDS | `nds/` | WatermelonDS's *"use `.srm` not `.sav`"* toggle on, so the on-disk filename matches what the RetroArch provider syncs. |

The desktop and Android providers for these systems are already the same class (ARMSX2 reuses
`Pcsx2SaveLocationProvider`, WatermelonDS reuses `RetroArchSaveLocationProvider`), so their `localId`
schemes match too — the system key is the only thing that had to change.

### Same emulator, but a container/config that must still be aligned

Even within one emulator, the *card mode* can change the on-disk shape and therefore the unit id:

- **GameCube (Dolphin).** The unit id encodes both the **slot** (`.../a/...` vs `.../b/...`) and the
  **card device mode**: a *raw* single-file card is `gamecube/raw/<slot>/<region>`, a *GCI folder*
  is `gamecube/gci/<slot>/<gameId>`. These are different unit-id shapes and different container
  formats, so a desktop **raw card** never matches an Android **GCI folder** even though both are
  Dolphin. The provider *does* absorb the path-depth difference (desktop `GC/USA/` vs Android
  `GC/USA/Card A/`) — that is already handled — but not raw-vs-GCI. **Checklist item:** GameCube must
  use **GCI folder mode, slot A** on both platforms (Android's default), not a raw memory card. (Both
  modes now sit under the same `gamecube/` system key; raw-vs-GCI is disambiguated by the `localId`
  shape, and the emulator declines a card it cannot read — sync never deletes.)

### Cross-emulator battery caveats are config, not conversion

The residual per-device config alignment (PS2 file card, DS `.srm`, GameCube GCI/slot) is unavoidable
and universal — RetroArch's first-party sync requires exactly the same discipline. It belongs in the
**per-emulator setup checklist** (Milestone B already needs this checklist for launching), verified
rather than assumed. Where a remote save cannot be matched, it stays in the cloud and the emulator
declines it on load; nothing is lost.

**Explicitly out of scope for v1** (deferred to their own feature, owner's call):

- PS2 **folder-card ⇄ `.ps2` conversion**.
- Any **cross-emulator format converter** (e.g. one PS2 emulator's card format into another's).

## Save states

Save states are a dump of the emulator's internal memory. Their portability depends on the emulator/core
**build**, and — the sharp case — on the **CPU architecture**: some cores serialize state in an
arch-independent way (snes9x states work across Windows and Android), others do not (mGBA states written
on Windows x86 do not load on Android arm64).

**Rule: keep states in a separate, opt-in namespace, tag each with its provenance, and gate restore on
compatibility. Auto-restore only when safe; let the user override for the rest; never delete.**

This is *already built* on desktop and shared into the Android head, and is **unchanged by the battery
system-keying above**: state cloud keys stay **emulator-scoped** (`<emulatorId>/states/…`, e.g.
`duckstation/states/…`, `retroarch/nds/states/…`), because two emulators for one system can write
same-named state files and it is the emulator-scoped namespace plus the compatibility gate that keep
them apart. The provider exposes this as a second namespace (`StateNamespacePrefix`) distinct from the
system-scoped battery `UnitIdPrefix`; the migration below leaves state keys untouched.

- States live in a separate `states` namespace, **opt-in per platform** (`SyncSaveStates` toggle).
- Each state carries a compatibility key `st1|<emulatorId>|<arch>|<provenance>:<version>`
  ([AuxiliarySyncProvider.cs](../src/EmuShelf.UI/Services/AuxiliarySyncProvider.cs)).
- `AreCompatible` gates restore: **same emulator/core id and same architecture are required**; the
  version is enforced only when *both* sides recorded an authoritative one.
- `GetRemoteIncompatibilityReason` surfaces an incompatible remote state as "available in the cloud,
  not restored" rather than pushing it.

The architecture token is read from the emulator binary, falling back to the host architecture — so on
Android it is naturally `arm64`, which is *why* the existing gate already blocks a Windows-x86 mGBA
state from auto-restoring on the Thor. That behaviour is correct and stays.

### The one refinement: arch-portable cores

The existing gate treats *every* core's states as architecture-sensitive. That correctly blocks mGBA,
but it also blocks snes9x, whose states are genuinely portable — so a snes9x state that would load is
refused. Close the gap two ways, layered:

1. **Arch-portable allowlist (auto-restore, implemented first).** A small, curated set of emulator/core
   ids whose state format is known architecture-independent. For an id on the list, `AreCompatible`
   skips the architecture check (version rules still apply), so its states auto-restore across
   platforms. Seeded conservatively — **snes9x only** to start (the verified example) — with a clear
   bar for adding entries, because a wrong entry would auto-restore a state the emulator then rejects
   (harmless thanks to the invariants, but noisy). The always-hard-gate behaviour is preserved for
   every id not on the list.
2. **User-override manual restore (general mechanism, built with the gamepad Saves rebuild).** For any
   cross-arch / cross-version state not covered by the allowlist, surface it as *"from snes9x on
   Windows — may not load — restore anyway?"* and let the user pull it. The emulator's load-time check
   and sync-never-deletes are the backstops. This puts the judgment where it belongs and needs no
   curated knowledge.

## Android-specific work (the E-android delta)

The model above is mostly shared code. The Android head must:

1. **Per-emulator battery providers** with the right namespace:
   - Folder-configurable set (PPSSPP, Azahar, RetroArch, WatermelonDS): reuse the desktop providers,
     handed the user's chosen save folder as the existing per-system `DirectoryOverride`.
   - Fixed-location set (DuckStation ✅ landed, Dolphin ✅ wiring landed): package-derived roots from the
     Android composition root, feeding the existing providers' explicit-user-directory seam.
   - PS2 (ARMSX2) → `playstation2/` system key; DS (WatermelonDS) → `nds/` system key — both fall out of
     the system-scoped `UnitIdPrefix` via the reused desktop provider class, no per-pair wiring.
2. **Setup checklist entries** for the config-alignment cases: PS2 single-file `.ps2`, DS `.srm` toggle,
   GameCube GCI-folder/slot-A. Verified, not assumed.
3. **Save-state roots** per emulator, and report `arm64` (already automatic via host arch).
4. **Gamepad Saves rebuild** — the gamepad Saves rows are rclone-only today
   (`allowManagedTransport: false`). Rebuild them to offer the managed Drive transport, the
   `SyncSaveStates` toggle, and the cross-arch **override** prompt.
5. Transport plumbing (own doc): second public OAuth client + custom-scheme redirect, Android
   `IProtectedTextStore` (Keystore) for the refresh token, force `TransportKind = GoogleDrive`.

The SAF-backed `ILocalSaveEndpoint` rewrite is **not needed for the Thor** (all-files reaches
`Android/data/<pkg>` for read+write, including `Directory.Move`); it reverts to a portability concern
for a hypothetical second device.

## On-device ground truth (Thor, 2026-08-21)

Measured directly on the Thor (`adb -s 2fd555f4`) so the wiring is not guessed:

- **ARMSX2 (PS2) is structurally desktop PCSX2.** Its user directory `/sdcard/User/ARMSX2/` holds a
  `PCSX2-Android.ini` — the **byte-identical PCSX2 version-1 INI format** (`[Folders] MemoryCards =
  memcards`, `[MemoryCards] Slot1_Filename = ...ps2`) — and a `memcards/` folder with real single-file
  cards (`Mcdf01_converted.ps2` 34 MB, `mcd002.ps2` 8 MB). So the existing `Pcsx2SaveLocationProvider`
  reads it verbatim once it knows the filename, emitting `playstation2/` system unit ids → PS2 needs
  **no new provider class**, just the `PCSX2-Android.ini` candidate. The card *filenames* here
  (`Mcdf01_converted.ps2`) will only sync with a desktop PCSX2 card of the same name — the checklist's
  "matching filename" item, now concrete.
- **WatermelonDS (DS) and RetroArch share one unified saves root.** WatermelonDS writes
  `<Game>.srm` into `/storage/AE6A-1092/saves/Nintendo DS/`, which is the *same* per-system-sorted
  `saves/` tree RetroArch uses (`saves/PlayStation`, `saves/Game Boy Advance`, …). So the DS `.srm`
  already coincides with the RetroArch save layout on-device: the Android DS provider is the RetroArch
  save provider pointed at that shared root, so it emits the same `nds/` system key. Both `.sav` and
  `.srm` are present, confirming the toggle requirement — sync must claim the `.srm`.
- **These are folder-configurable emulators**, not `Android/data`-locked: ARMSX2's user dir, the DS/RA
  `saves/` tree, PPSSPP, and Azahar all live on the SD under normal paths the app reads with all-files.
  So they route through the existing `DirectoryOverride` seam (a picked/known folder), not a
  package-derived `Android/data` root.
- **The fixed-root wiring is already live and points at real saves.** `AppBootstrapper.ResolveAndroidEmulator`
  auto-supplies DuckStation's and Dolphin's `Android/data/<pkg>/files` roots (no user pick). Verified on
  the Thor: DuckStation's auto-root resolves to real per-game `.mcd` cards (`memcards/Metal Gear Solid
  (USA)_1.mcd`, …); **Dolphin's resolves to real `GC/USA/Card A/*.gci` — GCI folder mode, slot A, USA**,
  which is exactly the config GameCube sync requires. So the GameCube "raw-vs-GCI" alignment item is
  already satisfied on the Android side; the open question is only whether the *desktop* Dolphin also uses
  GCI folder mode (if it uses a raw card, they will not match — see the config-alignment note).

**Composition-root wiring status (corrected after on-device inspection).** Most of the Android
save-provider wiring already exists in the shared code, so "slice 3" is smaller than first scoped:
- Fixed-root (DuckStation, Dolphin): auto-supplied roots, verified reaching real saves. **Done.**
- Folder-configurable (PS2→`playstation2/`, DS→`nds/`, PSP, 3DS, the RetroArch systems): the
  descriptors already resolve to the correct provider and system key; they only need a **per-system
  save-folder override**, which the user cannot set yet because the gamepad Saves UI is rclone-only.
  So the real remaining blocker for these is the **gamepad Saves rebuild (slice 4)**, not more wiring.

## Implementation sequence (slices)

Ordered so each slice is independently testable and the safe/shared ones land first.

1. **Save-state arch-portable relaxation** — ✅ **done (2026-08-21)**. `AreCompatible` skips the arch
   gate for an allowlist seeded with snes9x; hard gate preserved for everything else. Unit-tested, full
   App Release suite green.
2. **Battery cloud key → system-scoped** — 🟡 in progress (supersedes "namespace adoption"). Each
   provider's battery `UnitIdPrefix` derives from `SystemId` (`duckstation/…`→`playstation/…`,
   `pcsx2/…`→`playstation2/…`, `retroarch/<sys>/…`→`<sys>/…`, `dolphin/gc|wii/…`→`gamecube|wii/…`, etc.);
   `StateNamespacePrefix` keeps states emulator-scoped and unchanged. A one-time **copy-only migration**
   re-keys existing cloud battery entries to the system key (states/cheats/patches excluded), guarded by a
   persisted flag. PS2 provider reuse (ARMSX2 `PCSX2-Android.ini`) and DS→RetroArch-on-shared-`saves/`
   both fall out of the system key for free. Remaining: the setup-checklist config-alignment entries.
3. **Folder-configurable save-override plumbing** — ✅ **mostly already present** (2026-08-21). The
   descriptors resolve to the correct provider + namespace already; fixed-root DuckStation/Dolphin roots
   are auto-supplied and verified against real Thor saves. What remains is exposing the per-system
   folder override in the gamepad UI, which is slice 4.
4. **Gamepad Saves rebuild** — ✅ **already built** (found 2026-08-21; the plan's `allowManagedTransport:
   false` / "rclone-only" claim was stale). The gamepad Saves section already offers Connect/Disconnect/
   Sync Google Drive, a per-system **Save folder** override (`PickDirectoryCommand` → SAF picker →
   real path), a **Save states** toggle (compatibility-gated), and a save-state folder override. The one
   real gap was Android-specific: the managed sign-in opened its browser with
   `Process.Start(UseShellExecute)`, which throws on Android. Fixed with an `App.ExternalUriOpener` hook
   the head sets to fire an `ACTION_VIEW` intent; `EmulatorSettingsViewModel` falls back to it. Android
   head compiles; shared suite green. The cross-arch manual-override *prompt* remains a future nicety —
   the states toggle already auto-gates on `AreCompatible` (+ the snes9x arch-portable relaxation).
5. **Transport** — 🟡 foundation done; **no second OAuth client needed after all**.
   - **The plan's "second client + custom scheme" was based on a false premise** — that Android cannot
     bind a loopback port for the browser to reach. It can; the real obstacle was only that .NET's
     `HttpListener` (which the desktop redirect handler uses) is unsupported on Android. So Android
     **reuses the desktop OAuth client** over the same `http://127.0.0.1:port/` loopback redirect.
   - ✅ **`TcpLoopbackOAuthRedirectHandler`** (2026-08-21): a portable sockets-based redirect handler
     (parses the redirect, validates state with a fixed-time compare, tolerates favicon/prefetch,
     cancellable), selected on Android by `OAuthRedirectHandlerFactory`; desktop keeps its tested
     `HttpListener` handler. Unit-tested on the dev host by driving a real HTTP GET at its loopback
     port. `GoogleOAuthClientSource.Resolve()` reuses the desktop client on Android (a dedicated public
     client is still honoured if a build embeds one, but none is required). The desktop client secret
     ships in the APK, which Google treats as non-confidential and which the desktop build already does.
   - **Remaining:** the head supplies the `openBrowser` action (an `ACTION_VIEW` intent) and triggers
     connect — both come with the gamepad Saves rebuild (slice 4); then **one on-device sign-in on the
     Thor** to confirm loopback works there. Hardening (not a blocker): an Android Keystore
     `IProtectedTextStore` for the refresh token (the obfuscated fallback works meanwhile).
6. **On-device acceptance** — export/restore round-trips on the Thor per system (Dolphin GC/Wii is the
   pending gate; DuckStation already verified).

## On-device verification (Thor, 2026-08-21)

Built the Android head with a **dummy** OAuth client (so the managed transport is "available" without
real credentials) and drove the couch by gamepad keyevents:

- The **gamepad Saves section renders fully**: Connect Google Drive, Cloud folder (`EmuShelf/Saves`),
  and per-system rows — PlayStation's **Save folder shows DuckStation's real auto-wired path**
  (`/storage/emulated/0/Android/data/com.github…`), confirming the fixed-root composition wiring reaches
  real saves on device — plus the Save states toggle.
- Activating **Connect Google Drive** fired the `ACTION_VIEW` intent from EmuShelf's uid and **Chrome
  opened Google's real sign-in URL** (logcat `START … act=android.intent.action.VIEW
  dat=https://accounts.google.com/… cmp=com.android.chrome`). So the one Android-specific gap
  (`Process.Start` → intent) is closed and verified. The dummy client makes Google reject the sign-in
  (`invalid_client`), which is expected; the browser-open is the thing under test.

**Still needs the owner's real OAuth secrets** for the final end-to-end pass: build with
`EMUSHELF_GOOGLE_OAUTH_CLIENT_ID`/`_SECRET` set, sign in on the Thor, confirm the `TcpListener` loopback
catches the redirect, then a real save round-trip (DuckStation/Dolphin sync first — their roots are
already wired). Note: an incremental build does **not** re-embed changed OAuth env vars — force a clean
Infrastructure rebuild so `EmbeddedSecrets` regenerates.

(Aside found during this pass: `libSDL2.so` (linux-x64) *is* packaged into the APK — an `XA0141`
warning names it — contradicting the plan's "SDL drop is moot / nothing to drop" note. Harmless on
arm64, but the claim is stale.)
