# Handheld Mode — a third, dual-screen interface

Status: proposal (2026-11-06), still in design. After four mockup rounds the owner is not sold yet:
"it still feels very unfinished". Nothing is built. It is not yet in `ROADMAP.md`; once agreed, add
a milestone there and record the non-obvious calls in `DECISIONS.md`.

**Handoff:** [docs/prototypes/handheld-mode/](docs/prototypes/handheld-mode/) has the interactive
mockup (`index.html`), Andrew's Cocoon reference photos, the round-by-round feedback log, an honest
list of what is unfinished, and a ready-made brief for a reviewer. "Mockup 1–4" below refers to
those rounds.

"Handheld mode" is a working name.

## Product goal

Add a third interface mode, alongside **Desktop** (OpenEmu-like library) and **Gamepad** (couch
shelf). It should feel like a handheld console's home menu (DS/3DS HOME Menu, Cocoon). Two screens
work together:

- The **bottom screen** is where you pick things: a grid of big rounded icons that you arrange.
- The **top screen** shows off whatever is selected.

The feel matters more than the feature count. It should be warm, rounded, bouncy, and responsive
to every press. Every move, open, close and swap is animated, and nothing ever just pops in.

Gamepad mode remains the TV/couch interface. Handheld mode does not replace it.

### References

- Cocoon (Android dual-screen launcher): owner screenshots and video from 2026-11-06 on an AYN Thor.
- Nintendo DS/3DS HOME Menu: for feel and motion only.

As with NeoStation in `GAMEPAD-REDESIGN.md`, **copy the ideas, not the product**. Do not use any
Cocoon or Nintendo source, artwork, glyphs, sounds, or exact layouts. Folder glyphs use EmuShelf's
own licensed platform artwork (`Assets/PlatformConsoleArt`).

## What the references show

**Cocoon home** (photos 1–3):

- Bottom screen:
  - 5×4 grid of large rounded-square icons.
  - Platform folders are a tinted folder shape with a console glyph. Apps are plain tiles.
  - A `>` edge hint means there is another page to the right.
  - Bottom bar: `X` swap-screens button on the left, a pill dock in the centre (app drawer and
    pinned apps), `Y` menu button on the right.
- Selector: a glowing violet rounded ring around the focused tile. The tile inside is slightly
  raised.
- Top screen:
  - Background is a soft pattern of diagonal rounded tiles.
  - The focused item appears very large and centred, with a name plate below (name, then a kind
    such as "Folder" or "Pod").
  - When an app is focused, a wave of that app's colour sweeps across the background.

**Cocoon inside a folder** (video):

- Bottom screen:
  - Header bar: back button, folder name, sort chip ("Manual"), list toggle. All of these are
    focusable by the selector.
  - 4×3 grid of square game tiles, which are box covers cropped to square.
  - The grid scrolls **horizontally**, a column at a time, with part of the next column peeking in
    at the edge.
- Selector: glides from tile to tile and sits on the focused tile with a glow.
- Top screen:
  - Full-bleed artwork of the focused game (title logo on art).
  - On selection change it **cross-fades** to the next game in about 200–300 ms.
  - Small overlays: an achievements badge bottom-left, a pair of round buttons bottom-right, and
    a status pill top-right.

## Fixed product decisions (owner, 2026-11-06)

1. **A third mode.** Desktop and Gamepad stay as they are.
2. **Android dual-screen only for now.** The AYN Thor is the reference device: top display 0 is
   1920×1080, bottom display 4 is 1240×1080, both 120 Hz. The mode is offered only when a
   presentation display exists. A single-screen fallback is deferred.
3. **You arrange the home screen.** As on the 3DS and in Cocoon, icons and folders can be moved
   anywhere, and folders hold games.
4. **Current cover art for now.** Tiles use the existing covers, cropped to a rounded square, and
   the top screen uses the same cover. Square icon art, hero/fanart and logos come later.
5. **Nintendo-like, animated and comfy.** This includes an animated selector, big icons, a folder
   that animates when selected and opened, an animated game change on the top screen, and one
   button that swaps the two screens.
6. **Widgets and themes are out of scope** for this work.

Added after mockup 1 (2026-11-06):

7. **No OpenEmu-style console art on folders. Start with Monogram** (owner, after mockup 2).
   Platform folders show the system's short name ("PS2", "GBA", "DS") in big rounded type. Your
   own folders show a glyph instead, so the two kinds look different at a glance. Line icon,
   Covers and Sticker stay in the mockup as alternatives.
8. **The dock is today's companion dock.** It holds five slots of real Android apps plus All apps,
   with the same pin, clear and launch rules. It has no fake Search or Achievements apps.
9. **While a game runs, the bottom screen is today's companion,** redesigned in the comfy look.
   It keeps the same features: the resting dim, achievements, photos and video, and the dock.
   Dual-screen games are the exception; see open question 2.
10. **All core features stay:** settings, emulators, save sync, achievements, scraping, adding
    games and the setup wizard. Handheld mode is a new face on them, not a reduced app.

Proposed in round 5 (2026-11-06, built into mockup 5, awaiting the owner's verdict; the reasoning
is in [docs/prototypes/handheld-mode/REVIEW.md](docs/prototypes/handheld-mode/REVIEW.md)):

11. **Home is a place with slots.** All 40 slots are drawn faintly, the selector can land on any
    of them, and A on an empty slot makes a folder there (the 3DS gesture). The other two ways,
    dropping a game on a game in move mode and Y → Add to folder…, land in the same naming flow:
    keyboard on the bottom screen, the folder and its name on the top.
12. **A game can be in several places at once.** It is always in its platform folder, and may also
    be pinned to home and in any number of your folders. Your folders and pins are references,
    never moves. Every "Remove" names the place it means, and "Remove from library" stays a
    separate, clearly different action.
13. **A on a game opens its Title page; Play is a second press.** Like a Wii channel page or a 3DS
    banner, it shows what you are about to do: cover, play data, emulator, screen, disc and
    achievements. The disc chooser and the screen chooser are states of this page, not overlays
    on a launch that already started. Launching lifts the cover off the bottom screen and lands it
    on the top one before the emulator starts.
14. **A Recent folder is made for you,** like the platform folders: the last twelve games played,
    newest first, so two presses of A resume last night's game. After fifteen seconds without
    input the top screen drifts through those games; the first press only wakes it.
15. **Fan art and logos on the top screen from day one.** They are not deferred work: Fanart and
    Wheel media are scraped and stored, and Gamepad mode's spotlight hero already draws them.
    The artwork palette engine tints the selector glow, the Title page and the backdrop.
16. **Look:** a light, airy white-and-grey default on EmuShelf's indigo accent, derived from the
    theme catalogue, with a dark variant from the dark theme. Nunito for names and plates only;
    the system face for chrome.

## Screen anatomy

### Bottom screen — the Launcher (default)

**Home page:**

- A **5×4 grid** of rounded-square tiles. Tiles are large: about 200 px on the Thor's bottom panel.
- More pages scroll horizontally, with a `>` / `<` edge hint and page dots.
- **Tile kinds:**
  - **Platform folder.** Created automatically, one per platform that has games. It appears and
    disappears with your library. It always contains every game on that platform, and you can
    move it anywhere. It is a folder tinted in the platform's colour, with a monogram face
    (decision 7).
  - **User folder.** Created by you: Y on a game, then Add to folder…, then New folder. It starts
    with that game in it and lands in the first free space on home. You give it a name and a
    colour, and it holds whichever games you put in. Deleting it never touches the games.
  - **Game.** A game pinned directly to home, as the 3DS allows. Shows a cropped cover.
- **Bottom bar:**
  - Left: `X ⇅` swap screens.
  - Centre: the dock, with All apps followed by the five pinned app slots. Down from the bottom
    row of the grid reaches it.
  - Right: the menu button, the same as pressing Start. It holds Search, Add games, Settings,
    switching modes and Sounds.

**Inside a folder:**

- **Header bar:** back, folder name, sort chip, game count. The selector can reach all of them.
- A **4×3 grid of game tiles** that scrolls horizontally a column at a time, with the next column
  peeking in.
- **Sort chip:** Manual / A–Z / Last played / Most played. Only Manual allows reordering.

### Bottom screen while a game runs — the Companion

This is today's companion, with every feature kept. It is restyled in a darker version of the
comfy look so it doesn't pull your eye from the game.

- **Resting:** after a few seconds without input it dims to a faint cover and title. A touch or any
  button wakes it, and that first press is swallowed.
- **Awake (one big panel at a time).** Two cards side by side were too small on a 3.9-inch screen
  (owner, mockup 3). From top to bottom:
  - A slim Now playing row: a small cover, title, platform and play time.
  - Two tabs, **Achievements** and **Photos**. LB/RB or a tap switches between them.
  - One large panel showing the chosen tab:
    - Achievements: a big progress ring with count and points, plus the three most recent
      unlocks. A opens the full badge grid.
    - Photos: one large screenshot. Left/right pages through them in place, and A opens the full
      viewer at that photo.
  - The dock.
  - Up and down move between the panel and the dock. The default tab is Achievements when the game
    has them, otherwise Photos.
- **Achievements:**
  - A badge grid with a detail strip.
  - Refresh follows today's cache rules.
  - New: All / Locked / Unlocked on LB/RB, taken from Gamepad mode's achievements screen.
- **Photos:** screenshots, then the video. The video plays muted and only downloads when asked.
- **New:** the D-pad reaches every part of it. Today the dock, drawer and the buttons that open
  overlays are touch-only.

### Top screen — the Stage

- **Background:** a slowly drifting pattern of soft rounded tiles in EmuShelf's own design. It is
  calm and low-contrast, and it is never blurred per frame.
- **Focused folder:**
  - A large folder icon, centred.
  - A name plate: the name, plus "Folder · 42 games".
  - Optionally the first three covers peeking out of the folder.
- **Focused game:**
  - The cover, large, over a backdrop tinted from the cover's colours.
  - A name plate: title, plus platform and play time.
  - A compact achievements badge when the game has RetroAchievements data.
- **Focused dock app:**
  - The app's real launcher icon, large, on a coloured sweep.
  - The sweep colour is **picked from the icon itself**, like Android's Palette: white, black and
    grey are ignored, and the strongest hue wins. So it changes with every app, and a
    white-background icon still gets its brand colour. If an icon has no colour at all, a neutral
    slate is used.
  - Name plate: the app name, plus "Android app".
  - **New logic:** today the dock rasterises icons at 96 px, which is too small for the top
    screen. Load them at about 512 px (adaptive icons scale cleanly), pick the colour once per app,
    and keep it alongside the dock data.
- **Focused All apps:** a small panel of nine app icons.

## Controls

| Input | Home | Inside a folder | Move mode |
|---|---|---|---|
| D-pad / left stick | Move selector (edges page left/right) | Move selector (columns scroll) | Carry the lifted tile |
| A | Open folder / launch game / open system tile | Launch game | Drop here |
| B | — | Close folder | Cancel; tile flies back |
| X | Swap screens | Swap screens | — |
| Y | Tile actions sheet | Game actions sheet | — |
| Start | System menu | System menu | — |
| LB / RB | Previous / next page | Previous / next folder | Previous / next page |
| Touch | Tap = select, tap again = open; swipe = page; long-press = move | Same | Drag |

**Tile actions (Y):**

- **Every action Gamepad mode has today:** Launch, Select disc, Achievements, Edit title, Set cover,
  Scrape with ScreenScraper, and Remove from library (with its confirmation).
- **New:** Move, Add to folder…, Pin to home / Remove from home.
- **Folders only:** Rename, Colour, Delete folder. Deleting a folder never touches its games.
- **Dock slot:** Open, Change app…, Clear slot. A long press does the same thing, as it does today.

Removing a tile only changes EmuShelf's home layout. It never removes the game from the library,
and never touches its files.

## Motion spec (the heart of this mode)

All animation is **transform and opacity only**, so it can be composited cheaply. There is no
per-frame blur and no animated `BoxShadow`; shadows are pre-rendered images, following the Android
`reduced-effects` lesson. Durations are starting points to tune on the device.

**Rules learned from mockup 2,** where the owner found that animations jumped:

- **Anything that travels uses a real spring, not a timed curve.** That covers the selector and
  scrolling (pages, folder columns).
  - A spring keeps its speed when the target changes, so fast presses glide.
  - A CSS-style transition restarts from zero speed on every press, which reads as a stutter.
  - Mockup values: selector stiffness 560 / damping 43 (barely any overshoot); tracks 250 / 31.5
    (no overshoot).
- **Never animate one property from two places.** The tile's focus lift lives on the tile.
  Fly-in, pop and press animations live on a wrapper around it.
  - When both were on the tile, the lift snapped in the moment a fly-in ended. That was the most
    visible jump.
- **Overshoot is a seasoning.** Small pops (focus lift, press) get about 5–10% overshoot.
  Crossfades and screen-level moves get none.
- **The top screen changes once per move.** Name plate and artwork change together.
  - A single press updates at once.
  - A held direction (presses under 150 ms apart) waits until movement settles.
  - Mockup 2 updated the plate first and the art later, and that read as two jumps.

The motions:

- **Selector.** It must be unmistakable on any folder colour (owner, mockup 3: "I don't understand
  what I selected").
  - A thick violet frame, about 7.5% of the tile wide, with a white keyline inside and out.
  - A soft glow behind it that pulses on a 1.2 s cycle.
  - The focused tile lifts to 1.13× with a deeper shadow.
  - The frame springs from tile to tile.
  - Dock buttons, header chips, drawer apps and sheet rows use the same frame language.
- **Edge bump.** Pressing into a wall nudges the selector about 7% and eases it back, so the
  device clearly says "nothing there".
- **Folder focus.** On the top screen, the big folder grows out of the centre (as for every focus
  change), and after about 200 ms its flap tilts so three covers peek out.
- **Folder open (A).**
  1. The flap swings open on both the tile and the big top-screen folder. The tile grows slightly
     (150 ms).
  2. Home recedes to 0.94× and fades out (260 ms). At the same time, the games spill out of the
     folder's position, nearest first (each 440 ms, 14 ms apart).
  3. The header fades in, and the selector appears once the first tiles have landed.
  4. At about 300 ms the top screen crossfades from the open folder to the first game.
- **Folder close (B).** The reverse:
  1. The games gather back into the folder, farthest first (280 ms) while home returns.
  2. The flap closes with a small bounce, and the top screen crossfades back to the folder.
- **Focus change on top (games, folders, apps): from the centre, never from the side** (owner,
  mockup 3, asking for it to feel like the 3DS and Cocoon).
  - The old art shrinks to 0.86× and fades out (150 ms).
  - The new art grows from 0.72× to full size on a real spring with about 2% overshoot (stiffness
    190, damping 17, settling in about 0.45 s), fading in over the first 170 ms.
  - The name plate pops in from 0.94× at the same time.
  - The movement never depends on which direction you pressed.
- **Page change.** The grid slides on the track spring, and the page dots follow.
- **Launch.**
  1. The tile presses in (0.94×) and pops (1.1×).
  2. The top-screen cover zooms in and fades.
  3. Then the emulator starts.
- **Screen swap (X).** Both screens' contents slide out (top goes up, bottom goes down) and the
  other screen's content slides in (about 300 ms). The selector, folder and scroll position are
  all kept.
- **Move mode.** The lifted tile scales up, gains a shadow and wobbles gently. Other tiles slide
  aside to open a gap where it would land. Dropping it gives a small squash-and-settle.
- **Sounds** (Phase 5): soft original effects for move, open, close, swap and launch, and an off
  switch in Settings. They must be self-made or CC0-licensed. No Nintendo sounds.

## What already exists, and what is new

This comes from a read of the code on 2026-11-06 covering Gamepad mode (`MainViewModel`, the
`Gamepad*` view models, `GamepadShellView`) and the Android companion (`SecondScreenViewModel`,
`SecondScreenView`, `SecondScreenController`).

**The short answer:** almost everything a user *does* in Handheld mode already exists as logic.
The new work is mostly views and motion. There are four real pieces of new logic:

1. Arranging the home screen.
2. The plumbing for a third mode on Android.
3. One controller selector that spans both screens and the companion.
4. A handful of small additions.

### Kept 1:1 (existing logic; only a new view in the comfy look)

| Area | What it is today | Backed by |
|---|---|---|
| Launching | Picking the screen (per-system Ask / Built-in / External; DS/3DS always built-in), file-missing checks, save sync before and after, the "Launching…" and blocking sync panel, play-time recording, achievements refresh on return | `MainViewModel.LaunchGameCoreAsync`, `AndroidEmulatorLaunchService`, `LaunchScreenResolver` |
| Launch-screen chooser | Two screen cards plus "Always use this screen" | `GamepadOverlayKind` chooser |
| Disc chooser | Choose and remember the active disc | `MainViewModel` disc selection |
| Game actions | Launch, Select disc, Achievements, Edit title, Set cover, Scrape, Remove from library (with confirmation) | `MainViewModel` game-actions overlay |
| Achievements (full screen) | RetroAchievements and Steam; All / Locked / Unlocked, sort, refresh, reveal | `AchievementDetailsViewModel` |
| Cover search | DuckDuckGo image search, preview and apply | `GamepadCoverSearchViewModel`, `CoverSearchViewModel` |
| Scraper | Single game, with all its states (consent, no match, applying…) | `GamepadScraperViewModel`, `GameScraperViewModel` |
| Batch scraper | Configure, run, done | `GamepadBatchScraperViewModel` |
| Search | Filters by title | `LibraryViewProjection` (on X today; in the Start menu here) |
| Add games | Android folder picker, then the system chooser | `MainViewModel` import flow |
| Settings: Library | Show empty platforms, rescan all, change data folder, run setup again | `GamepadSettingsViewModel` |
| Settings: Emulators | Close emulator on return (Shizuku), per-platform emulator choice, launch screen, game folders | `GamepadSettingsViewModel`, `EmulatorSettingsViewModel` |
| Settings: Achievements | Steam profile and key, RetroAchievements account, refresh matches | same |
| Settings: Artwork & metadata | Fetch after import, fetch missing, web image search, ScreenScraper sign-in | same |
| Settings: Saves | Google Drive, cloud folder, per-platform save and state folders, replace cloud/local, export | same |
| Settings: About | Version, check for and install updates (`AndroidUpdateApplier`) | same |
| On-screen keyboard | The app's own couch keyboard, mirrored onto the Thor's second screen | `AndroidOnScreenKeyboardService` |
| Setup wizard | Storage access, data folder, second screen, closing games, games and emulators, saves | `SetupWizardViewModel`, `StartupViewModel` |
| Status | Toasts (info, progress, error), empty library, loading | `MainViewModel` |
| Play data | Last played, play time, play count; used by sorts and the name plate | `Game.LastPlayedAt`, `Playtime`, `PlayCount` |
| Companion: resting and wake | Dims during play; a touch wakes it for 5 s | `SecondScreenViewModel` standby |
| Companion: achievements | Badge grid, detail, refresh, follows the running or focused game | `SecondScreenViewModel`, `SecondScreenController` |
| Companion: photos and video | Screenshots, then a muted video that downloads only on play | `SecondScreenViewModel.Media`, `ICompanionMediaSource` |
| Dock and app drawer | Five slots, pin (moves the app if it's already pinned) and clear, launch on Screen-2, hide and come back via the accessibility watcher | `SecondScreenDock`, `FileSecondScreenDockStore`, `SecondScreenController` |
| Game-running lifetime | Keep-alive service, return detection, "game took Screen-2" handling | `SecondScreenController`, `SecondScreenKeepAliveService` |

Already gone on Android, so not needed here: Hotkeys, Texture Packs, Sync RPCS3, the local cover
file picker and Desktop mode.

### New logic

| What | Why it's new | Size |
|---|---|---|
| **Home layout and user folders** | Favorites, collections, custom order and pinning games to home don't exist anywhere in the code today. This needs the `HandheldHomeLayout` model, a portable store (`Settings/handheld-home.json`), reconciliation when platforms or games appear and disappear, user folders (name, colour, games), and a manual order within folders. | Medium. It lives in Core and is fully unit-testable on macOS. |
| **Third interface mode on Android** | `AndroidInterfaceModeService` fixes Android to Gamepad mode today. Needed: `InterfaceMode.Handheld`, switching between modes, the main screen hosting the Stage, and the Presentation hosting the Launcher while browsing and the Companion while a game runs. | Medium. The risky part is device behaviour, which Phase 0 tests. |
| **Screen swap** | Re-host the Stage and Launcher between the two displays. There is a precedent: the companion already moves to the main screen via `MainScreenCompanion`. | Small to medium |
| **One selector across both screens** | Both windows send controller input to one dispatcher, which is the same pattern as today. New: the companion's dock, drawer and overlay buttons work with the D-pad (today they are touch-only). | Small to medium |
| **Move mode** | Lift, carry and drop with the pad and touch. Only visual on top of the layout model. | Small |
| **Sort chip inside folders** | Manual is new. A–Z, Last played and Most played come from existing data. | Small |
| **Companion achievement tabs** | All / Locked / Unlocked is logic Gamepad mode already has; it gets wired into the companion. | Small |
| **Sounds** | Short original effects and an off switch. | Small |
| **Dock app icons at top-screen size** | Load launcher icons at about 512 px instead of 96 px, and pick each app's colour from its icon once. | Small |

### Purely visual (the bulk of the remaining work)

- The Stage, Launcher and Companion views.
- The folder graphic in the chosen style.
- The selector, all the motion in the motion spec, and the restyled sheets and overlays.
- Every settings page in the comfy look, built on today's row kinds (toggle, choice, action, text,
  secret, folder, file, information, summary).

## Architecture

- **New mode value.** Add `InterfaceMode.Handheld` in Core. It is offered on Android only when a
  presentation display is present. Every mode's Start/Settings menu can switch to it and away
  from it.
- **Both screens are Avalonia.**
  - The second screen already hosts an `AvaloniaView` inside `ThorSecondScreenPresentation`.
  - In Handheld mode, that Presentation shows the Launcher, and `MainActivity`'s single view shows
    the Stage.
- **One view model, two views.**
  - `HandheldShellViewModel` owns all state: home layout, open folder, selection, move mode, swap
    state, and the overlays.
  - `HandheldStageView` and `HandheldLauncherView` are pure views onto it.
- **Swap by re-hosting, not by moving windows.**
  - Each display has a root slot that shows either the Stage or the Launcher, decided by
    `IsSwapped`.
  - There is already a precedent: `SecondScreenController` re-hosts the companion model on the
    main screen via `MainScreenCompanion` when a game takes Screen-2.
- **Input.**
  - Android sends pad events to whichever display was last touched. So both
    `MainActivity.DispatchKeyEvent` and the Presentation's `DispatchKeyEvent` forward to the same
    `HandheldShellViewModel.DispatchGamepadAction`.
  - There is one selector, whichever screen has focus. This is the same pattern as the
    2026-08-23 second-screen gamepad decision.
- **Home layout model.**
  - `HandheldHomeLayout` holds pages of slots. Each slot holds a tile reference: a platform folder
    (by system id), a user folder (by folder id), a game (by game id), or a system tile (by kind).
  - User folders store their name, colour, ordered game ids and sort mode.
  - It persists to portable `Settings/handheld-home.json`. The model and store live in Core, so
    the macOS desktop test suite covers them; this is the same pattern as
    `second-screen-dock.json`.
- **Reconciliation.** New platforms get a folder appended at the end. Ids for removed games or
  platforms are dropped quietly. Missing files show the usual "unavailable" dimming.
- **Reuse rather than duplicate:**
  - `GameViewModel` and the cover/thumbnail pipeline, with tiles decoded at tile size off the UI
    thread.
  - The Android launch path and the achievements services.
  - Gamepad mode's Settings, scraper and cover-search overlays, shown on the top screen.
- **During gameplay.** Keep the existing companion lifetime rules: the game idle screen,
  keep-alive, and a game or app taking Screen-2. Handheld mode changes browsing, not how sessions
  are handled.

## Phases

Each phase must be usable and checked on the Thor before the next one starts. Screen-2 is
`FLAG_SECURE`, so checking it on the device means hands on the device plus logs, not screenshots.

### Phase 0 — device spike

- Get both Avalonia surfaces animating at the same time on the Thor.
- Glide a selector over 20 cover tiles on the bottom while the top cross-fades.
- Re-host the two views between displays.
- Route the pad from either display to one view model.

**Exit:** frame times are recorded, and both screens animate smoothly enough to commit to Avalonia
for both. If they don't, revisit before Phase 1.

### Phase 1 — mode and skeleton

- `InterfaceMode.Handheld` and switching into and out of it.
- Stage and Launcher views.
- Platform folders created automatically, in the default layout.
- Open a folder, scroll the game grid, launch a game, B back.
- Plain transitions only.

**Exit:** a game can be launched from Handheld mode, and you can get back to Desktop/Gamepad mode.

### Phase 1b — Title page (round 5)

- A on a game opens the Title page; Play launches. Disc and screen choosers as states of it.
- Fan art and logo on the top screen for the Title page, with the cover as fallback.
- The bottom-to-top launch motion.

**Exit:** every launch goes through the Title page, including the existing choosers and the
save-sync panel.

### Phase 2 — motion pass

- Selector spring, focus lift and breathing.
- Edge bump.
- Folder focus, open and close.
- Top-screen game change with a debounce while the d-pad is held.
- Page slides.
- Launch animation.

**Exit:** the owner judges it on the Thor; this phase is judged by feel, not just by checklist.

### Phase 3 — screen swap

- X swaps the Stage and Launcher between displays with the slide animation.
- The selector, folder and scroll position survive the swap.
- Whether the screens are swapped is remembered.

### Phase 4 — arranging the home screen

- Layout model and store, plus reconciliation. Visible empty slots; the Recent folder.
- Move mode with the pad and with touch, including dropping a game on a game (new folder) or on
  one of your folders (add).
- User folders: create from an empty slot, rename and recolour on the keyboard, delete with the
  games kept. The empty-folder state.
- Add to folder, pin to home.
- Sort chip inside folders.
- The idle drift.

**Exit:** the layout survives a cold start and a reinstall-style restore.

### Phase 5 — actions, system and sound

- Y actions sheet.
- Start system menu (Settings, Achievements, Search, switch mode), reusing the couch overlays.
- Sound effects with an off switch.

### Later (not part of this work)

- Square icon art, hero/fanart and logos for the Stage (the ScreenScraper work).
- Themes and widgets.
- A status pill.
- Grid size options.
- A single-screen fallback.
- Other dual-screen devices (AYANEO Pocket DS, Retroid dual-screen).

## Open questions

1. **Why it still feels unfinished.** The owner's latest verdict. The leading suspects are listed in
   [docs/prototypes/handheld-mode/README.md](docs/prototypes/handheld-mode/README.md) → "What is
   still unfinished": a thin top screen, placeholder art, a generic visual identity, motion never
   tried on the device, and most screens not drawn yet. Next step: an independent review.
2. **Dual-screen games (DS/3DS).** The rule is that the companion should not show for them. The
   code today always launches DS/3DS on the built-in screen and leaves the companion on Screen-2
   in its resting dim. Nothing detects an emulator that draws its own second screen, and the
   companion sits above ordinary app windows. Check on the Thor what melonDS/Azahar actually do,
   then decide whether the companion should step aside for `IsDualScreen` systems.
3. **Themes.** Gamepad mode has a Themes page. Hide it in Handheld mode for now (decision 6), or
   keep it?
4. **The mode's name.** Handheld, Pocket, Home, or something else.
