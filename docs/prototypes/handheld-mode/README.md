# Handheld mode: interactive mockup and handoff

**Status (2026-11-06): exploration, not approved.** After four rounds Andrew's verdict was "not
completely sold yet, it still feels very unfinished." An independent review
([`REVIEW.md`](REVIEW.md)) found the cause to be missing rules rather than missing polish, and
round 5 built its five-item list. Nothing has been built in the app. Nothing is in `ROADMAP.md`
or `DECISIONS.md` yet.

This folder holds everything produced so far, so another person or agent can pick it up, review it
or push it further.

| File | What it is |
|---|---|
| [`index.html`](index.html) | The interactive mockup (version 5). It is a single self-contained file: no build step, and nothing loaded except Google Fonts. |
| [`REVIEW.md`](REVIEW.md) | The round-5 review: why it felt unfinished, the missing core logic, what to borrow from the DS, 3DS and Wii, and the list round 5 was built from. |
| [`../../../HANDHELD-MODE.md`](../../../HANDHELD-MODE.md) | The design spec covering goal, fixed decisions, screen anatomy, controls, motion spec, architecture and phases. It also has **"What already exists, and what is new"**, a map of every feature to the existing view model or service behind it. |
| [`reference/`](reference/) | Andrew's photos and video frames of Cocoon on his AYN Thor. This is the target feel. |

The same mockup is published as a private Artifact at
<https://claude.ai/artifact/LnCjSpe7cv1N2xyuwBqHWJ> (version 4). Only Andrew can open that link
unless he shares it, so use the local `index.html`.

## What Handheld mode is

It is a **third interface mode**, alongside Desktop (OpenEmu-like) and Gamepad (couch shelf), for
**dual-screen Android handhelds**. The AYN Thor is the reference device: top 1920×1080, bottom
1240×1080. The feel is a Nintendo DS/3DS-style home menu, with Cocoon as the closest existing
example:

- **Bottom screen, while browsing:** a launcher. It is a 5×4 grid of big rounded tiles: platform
  folders, your own folders and pinned games. Below it sits a dock of real Android apps.
- **Top screen:** shows whatever is selected, large.
- **Bottom screen, while a game runs:** today's companion (achievements, photos, dock), redesigned.
- **Animation:** everything is animated, rounded and "comfy".

## Opening the mockup

Open `index.html` in Chrome or Safari. Opening it straight from disk works. If the fonts or icon
colours misbehave, serve it instead:

```bash
python3 -m http.server 8000 --directory docs/prototypes/handheld-mode
```

Then go to `http://localhost:8000/`.

- **Drive it:** use the drawn device buttons, tap or click the bottom screen, or use the keyboard:
  arrows; `A`/`Enter`; `B`/`Esc`; `X` swaps screens; `Y` opens actions; `Q`/`E` are LB/RB; `S` is
  Start.
- **Jump to** (side panel) goes straight to each state: Home, Game on home, Empty slot, New
  folder (the keyboard), Title page, Inside a folder, Actions (Y), Move mode, Dock and apps,
  Swapped screens, Launch a game, In-game companion, Achievements while playing and Idle drift.
- **Look** switches the interface between Light (default) and Dark, and the folder face between
  Monogram (chosen), Line icon, Covers and Sticker. The **Folder icon options** card under the
  device compares all four faces.
- **Motion** has Normal, Slow and Very slow settings, for judging animation. It also has optional
  synthesised sounds.
- **What you're looking at** (side panel) explains the current state and what to look for.

## How it got here: owner feedback by round

| Round | Andrew's feedback | What changed |
|---|---|---|
| 1 | Brief: a third mode, Android dual-screen only, user-arrangeable folders as on the 3DS, current covers for now, "very Nintendo-like, animated and comfy"; animated selector, big icons, animated folder, animated game change on top, one button to swap screens. Widgets and themes not important. | First mockup: Stage on top, Launcher below, folders, move mode, Y sheet, swap, launch. |
| 2 | "I don't like these folder icons with OpenEmu-style consoles." "Dock should work the same way as it does now… real Android apps." "While the game is running the companion should be present on the second screen… in updated comfy design." All core features (settings, emulators, save sync) must stay. Asked for a map of what exists and what is new. | Four folder face styles. The dock became the real companion dock (5 slots plus All apps, pin/clear). In-game companion added. The inventory map was written into `HANDHELD-MODE.md`. |
| 3 | "Animations are weird and jump (game switch, folder open)." "How will a real docked app look on the upper screen?" "Are folders created manually or automatically?" "Let's use monogram folders." | Spring physics for the selector and scrolling. Fly/pop animations moved off the tile, which stopped the snap. The folder open/close sequence was rebuilt. App icons were drawn like real ones, with the background colour picked from each icon. New folder flow added. Monogram became the default. |
| 4 | "I don't see any changes." (The open tab had not reloaded.) "Games are appearing from the side; I want it from the centre like 3DS/Cocoon." "I don't understand what I selected; there should be a visible selector." "Isn't the companion too small for two widgets at once?" | Top-screen art now grows out of the centre on a spring. The selector became a thick violet frame with white keylines and a pulsing glow. The companion now shows one big panel at a time (Achievements or Photos, switched on LB/RB). |
| 5 | "Not completely sold yet, it still feels very unfinished." "If folders are created manually, how? Core logic is missing." Wants the best DS/3DS/Wii decisions. | An independent review (`REVIEW.md`) and the five things it asked for: visible empty slots with A-on-a-slot folder creation and a naming keyboard; a Title page before every launch, with the disc and screen choosers as its states and a bottom-to-top launch; fan art, logos, the artwork tint and a status pill on the top screen; a Recent folder and an idle drift; a light Wii-airy look with a dark variant. Also: drop a game on a game in move mode to make a folder, drop it on your folder to add it, rename, recolour and delete folders, and an empty-folder state. |
| Now | Awaiting the owner's verdict on round 5. | — |

## Settled so far

These are the owner decisions. `HANDHELD-MODE.md` lists them as "Fixed product decisions" 1–10.

- A third mode; Desktop and Gamepad stay as they are. Android dual-screen only for now.
- Platform folders are automatic, one per system with games. You make your own folders with
  Y → Add to folder… → New folder. Both kinds can be moved anywhere.
- **Monogram folder faces:** "PS2", "GBA" and so on in big rounded type. Your own folders show a
  glyph instead.
- Current cover art for now, cropped to rounded squares. No console art on folders.
- **The dock is the existing companion dock:** real Android apps in five slots plus All apps, with
  the same pin and clear rules. Apps launch on the bottom screen.
- **While a game runs,** the bottom screen is the existing companion, restyled. It keeps the
  resting dim, achievements, photos/video and the dock.
- **Every core feature stays:** settings, emulators, save sync, achievements, scraping, adding
  games and the setup wizard.
- **Motion:**
  - Top-screen changes grow from the centre.
  - Anything that travels uses a real spring.
  - The selector must be unmistakable.

Proposed in round 5 (in the mockup, not yet confirmed by the owner; see `REVIEW.md`):

- **Home is a place with slots.** Empty slots are drawn faintly and A on one makes a folder there.
- **A game can be in several places at once:** always in its platform folder, and also pinned to
  home or in any of your folders. Every "Remove" names the place it means.
- **A on a game opens its Title page; Play is a second press.** The disc and screen choosers are
  states of that page.
- **A Recent folder** is made for you, like the platform folders.
- **Fan art and logos on the top screen from day one.** They are already scraped and shown by
  Gamepad mode's spotlight hero.

## What is still unfinished

This is an honest list, roughly in the order a reviewer is likely to notice. Most of it is why the
mockup "feels unfinished".

1. **The top screen while browsing is still a cover on a backdrop.** Round 5 put fan art and the
   logo on the Title page and the idle drift, and the browsing backdrop is now the fan art, but
   the folder-focused and game-focused compositions could still use the 16:9 width better.
2. **The art is placeholder.** The covers, fan art and logos are generated, so the overall look is
   hard to judge. Real covers, cropped square, will look busier than Cocoon's clean icons. Square
   icon art is a separate open question.
3. **The visual identity is a first pass.** Round 5 replaced the lavender with a light
   white-and-grey look on EmuShelf's indigo, added a dark variant, and tints from the artwork,
   but none of it has been checked against the real Gamepad-mode themes side by side.
4. **Motion is tuned in a browser, not on the device.** The spring constants and timings are
   guesses. There is no 120 Hz check, no Thor GPU cost check, and touch-driven motion (drag to
   move, flick to page) was never designed.
5. **Screens that still don't exist.** The mockup now covers home, empty slots, folder creation
   and naming, the Title page with its disc and screen choosers, folder, the Y sheet, move mode
   with merges, delete confirmation, the empty folder, the dock and app drawer, swap, launch, the
   companion and the idle drift. Missing are:
   - Settings: Library, Emulators, Achievements, Artwork & metadata, Saves, About.
   - Search and Add games.
   - The save-sync blocking panel, and the scraper, batch scraper and cover search.
   - Full-screen achievements while browsing.
   - Empty library, and errors and toasts beyond a basic pill.
   - The six setup-wizard steps.
6. **The swapped layout is not designed.** When the launcher moves to the 16:9 top screen it is
   just the 5×4 grid centred with wide empty margins. The companion was never tried on the top
   screen.
7. **Smaller gaps:**
   - Manual reordering inside a folder isn't in the mockup.
   - The Start menu is a plain list.
   - The dock and launcher bar look different from the companion's dark dock.
   - The keyboard has no numbers or shift; the app's couch keyboard will replace it.
8. **The sounds** are synthesised blips, standing in for designed sound effects.

## Things that are not open

Don't spend review time on these; the code was read to establish them. See `HANDHELD-MODE.md` →
"What already exists, and what is new".

- **Almost all behaviour already exists as logic.** That covers launch flow, settings, save sync,
  scraping, achievements, the companion and its dock, and the keyboard. Handheld mode is mostly new
  views and motion.
- **Fan art and logos are not deferred work.** `GameMediaKind.Fanart` and `GameMediaKind.Wheel`
  are scraped, stored, and already drawn by Gamepad mode's spotlight hero (`MainViewModel`
  picks both). The artwork palette engine (`ArtworkPaletteFactory`, `ArtworkPaletteExtractor`)
  that tints the interface from the focused cover is built and tested.
- **Genuinely new logic:**
  - The home layout model with your own folders, custom order and games pinned to home. Favorites
    and collections don't exist anywhere in the code today. Round 5 adds a Recent folder and the
    rule that a game may be in several places at once.
  - The Title page as a step between selecting and launching, with the disc and screen choosers
    as its states.
  - A third interface mode on Android (it is locked to Gamepad today).
  - Re-hosting the two screens for the swap.
  - One controller selector spanning both screens and the companion.
  - Small additions: move mode, Manual sort, companion achievement tabs, high-res dock icons and
    picking their colour, and sounds.
- **Open question on DS/3DS games:** they launch on the built-in screen and the companion stays on
  Screen-2. Check on the Thor whether melonDS or Azahar draw their own second screen, and decide
  whether the companion should step aside for them.

## Suggested brief for a reviewer

> Read `HANDHELD-MODE.md` and this README, look at `reference/`, then open `index.html` and go
> through every **Jump to** scene at Normal and Slow speed. The owner likes the direction but says
> it "feels very unfinished". Say concretely why, screen by screen, and what you would change to
> make it feel like a finished, Nintendo-quality home menu without copying Cocoon or Nintendo
> assets. Focus on the top-screen composition, visual identity, motion quality and the missing
> screens, and keep in mind the Thor's real sizes (top 833×468 dip, bottom 1240×1080 px). Don't
> re-open the settled decisions above unless you think one is the cause of the unfinished feel;
> if so, say which and why.

## Code map of `index.html`

Everything is in one file: CSS at the top, then a single IIFE script. Search for these names.

**Data**
- `P`, `GAMES`, `F` and `home`: example platforms, games, folders and the 40-slot home layout.
  `F["r:recent"]` is the Recent folder, whose `ids` are computed from play data. `DISCS` marks
  multi-disc games; `P[k].screen` is `"ask"` for Dolphin.
- `APPS` and `dock`: example Android apps and the five dock slots.
- `coverURL`, `fanartURL`, `logoURL`, `badgeURL`, `shotURL` and `appIcon`: generated placeholder
  art (canvas or SVG). Each game's `hue` and `tint` come from its cover.
- `iconColour()`: picks a colour from an app icon, the same idea as Android's Palette.

**Round 5 pieces**
- `ensureSlots` / `.eslot`: the 40 visible slots. `onEmptyClick`, and `activate()` on an empty
  slot → `createFolderAt`.
- `openKeyboard`, `kbInput`, `liveName`: the naming keyboard; the top screen's name follows it.
- `setHomeFocus` (merge branch) and `dropMove`: dropping a game on a game or on your folder.
- `openTitle`, `renderTitle`, `playFromTitle`, `screenSheet`, `discSheet`, `launchFromTitle`:
  the Title page and the cover that travels to the top screen.
- `heroEl`, `setAccent`, `tickClock`: fan art + logo layer, the artwork tint, the status pill.
- `armIdle`, `startAttract`, `stopAttract`: the idle drift.
- `.device[data-ui="dark"]` and the `--ui-*` / `--card*` variables: the Light and Dark looks.

**Motion**
- `springTo` / `moveSel` / `setTrack`: spring physics for the selector and scrolling tracks. A
  spring keeps its speed when retargeted.
- `springFrames` / `POP_IN`: a spring baked into keyframes, used for the centre pop on the top
  screen.
- `renderArt`, `stageShow`, `setPlate` and `setBackdrop`: top-screen transitions. Plate and art
  change together, and when a direction is held the update waits until movement settles.
- `openFolder` / `closeFolder`: the folder sequence.
  - Tiles have a `.fly` wrapper so that fly and pop animations never fight the `.tile` focus lift.
    This is the fix for the round-3 "jump".

**Screens**
- `buildStage` and `buildLauncher`: the two screens. `swap()` re-hosts them between displays.
- `buildDock`, `openDrawer` and `openApp`: the dock, the app drawer, and an opened Android app.
- `buildComp`, `compPanelHTML`, `openAch` and `openMedia`: the in-game companion.

**Input and the side panel**
- `press()`: the single input dispatcher. `KEYMAP` maps keyboard keys to it.
- `scene()`: the Jump-to presets.
- `updateNotes()`: the "What you're looking at" text.

The mockup is a design tool, not production code. In the app, the motion maps onto Avalonia
compositor animations with the same constants (see the motion spec in `HANDHELD-MODE.md`).

## About `reference/`

These are Andrew's own photos of Cocoon, a third-party launcher, running on his Thor. They are kept
for direction only. Copy the ideas, never the assets, layouts or branding.

This repo is public on GitHub, so decide whether these images should be committed before anyone
runs `git add` on this folder.
